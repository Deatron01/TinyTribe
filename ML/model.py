"""The villager's network: one set of weights shared by every villager.

    grid (11 x 11 x 5 integers)  -> one-hot planes -> CNN  \\
                                                            > mix -+-> LSTM -+-> action head (21)
    vector (195 floats) -> items and jobs read out -> MLP  /       |         +-> argument head (21 x 68)
                                                                   +---------+-> value head (for PPO)

What a villager sees now reaches the heads directly as well as through the LSTM. With the LSTM
alone in the way, a network corrected on its own play chose the advised step on 58% of the ticks it
had been trained on, where a plain network without memory, given the same view, got 72% on villages
it had never seen: which way to step is mostly a matter of what is in front of the villager now, and
had to squeeze through its memory.

In the vector an item is one number (its id divided by the highest id), and so is a job. A layer of
weights cannot tell item 51 from item 52 that way, so the network first turns those numbers back
into what they stand for: how much of each item is in the pack, what is in the hand, which job the
villager has (TribeNet.read). Without that it never learned anything that depends on what it carries.

The argument head gives a row of 68 scores for every action; the row of the chosen action is the
one that counts, and it is masked with that action's valid arguments. So a villager never asks the
engine for something impossible, and what it learns about arguments is tied to the action.
"""
import json
from pathlib import Path

import torch
import torch.nn as nn
import torch.nn.functional as F

NEG = -1e9  # what a masked-out logit is set to
DATA = Path(__file__).resolve().parent.parent / "Game" / "TinyTribe" / "Assets" / "StreamingAssets" / "Data"


def highest(file: str, key: str) -> int:
    """The highest number in a table of the game's data: for layouts written before they carried their scales."""
    return max(int(row[key]) for row in json.loads((DATA / file).read_text(encoding="utf-8")))


class Layout:
    """Sizes of an observation, as the engine reports them (episode.json or the host's hello)."""

    def __init__(self, d: dict):
        self.grid_size = int(d["grid_size"])
        self.grid_channels = int(d["grid_channels"])
        self.channel_sizes = [int(c) for c in d["channel_sizes"]]
        self.vector_length = int(d["vector_length"])
        self.action_count = int(d["action_count"])
        self.arg_count = int(d["arg_count"])
        self.fields = {k: (int(v[0]), int(v[1])) for k, v in d.get("fields", {}).items()}
        # An item in the vector is its id / item_scale, a job its id / role_scale.
        self.item_scale = int(d.get("item_scale") or highest("items.json", "item_int"))
        self.role_scale = int(d.get("role_scale") or highest("roles.json", "role_int"))
        self.planes = sum(self.channel_sizes)
        self.grid_length = self.grid_size * self.grid_size * self.grid_channels

    def to_dict(self) -> dict:
        return {"grid_size": self.grid_size, "grid_channels": self.grid_channels, "channel_sizes": self.channel_sizes,
                "vector_length": self.vector_length, "action_count": self.action_count, "arg_count": self.arg_count,
                "fields": {k: list(v) for k, v in self.fields.items()}, "item_scale": self.item_scale, "role_scale": self.role_scale}


def onehot_grid(grid: torch.Tensor, layout: Layout) -> torch.Tensor:
    """[..., grid_length] integers (rows from the north, a tile's channels together) -> [..., planes, size, size] floats."""
    lead = grid.shape[:-1]
    g = grid.reshape(*lead, layout.grid_size, layout.grid_size, layout.grid_channels).long()
    planes = [F.one_hot(g[..., c].clamp_(max=size - 1), size) for c, size in enumerate(layout.channel_sizes)]
    x = torch.cat(planes, dim=-1).float()
    return x.movedim(-1, -3)


class TribeNet(nn.Module):
    def __init__(self, layout: Layout, hidden: int = 128, width: int = 32, decode: bool = True, skip: bool = True):
        """decode=False is the network as it was first built, which took the vector as it comes;
        skip=False is the network whose heads saw only what came out of the LSTM."""
        super().__init__()
        self.layout = layout
        self.hidden = hidden
        self.skip = bool(skip)
        self.decode = bool(decode) and "inventory" in layout.fields
        extra = 0
        if self.decode:
            items, roles = layout.item_scale + 1, layout.role_scale + 1
            self.register_buffer("item_ids", torch.arange(items, dtype=torch.float32), persistent=False)
            self.register_buffer("role_ids", torch.arange(roles, dtype=torch.float32), persistent=False)
            extra = 3 * items + 2 * roles
        size = layout.grid_size
        after = (size - 3) // 2 + 1
        self.conv = nn.Sequential(
            nn.Conv2d(layout.planes, width, kernel_size=1), nn.ReLU(),
            nn.Conv2d(width, width, kernel_size=3, padding=1), nn.ReLU(),
            nn.Conv2d(width, width, kernel_size=3, stride=2), nn.ReLU(),
            nn.Flatten(),
        )
        self.vec = nn.Sequential(nn.Linear(layout.vector_length + extra, 128), nn.ReLU())
        self.mix = nn.Sequential(nn.Linear(width * after * after + 128, 256), nn.ReLU())
        self.lstm = nn.LSTM(256, hidden, batch_first=True)
        reach = hidden + (256 if self.skip else 0)
        self.action_head = nn.Linear(reach, layout.action_count)
        self.arg_head = nn.Linear(reach, layout.action_count * layout.arg_count)
        self.value_head = nn.Linear(reach, 1)

    def initial_state(self, batch: int, device=None):
        zeros = torch.zeros(1, batch, self.hidden, device=device)
        return zeros, zeros.clone()

    @staticmethod
    def one_of(number: torch.Tensor, ids: torch.Tensor) -> torch.Tensor:
        """[...] numbers -> [..., len(ids)]: 1 at the id each number stands for. Plain arithmetic, so that it exports as it is."""
        return ((number.unsqueeze(-1) - ids).abs() < 0.5).float()

    def read(self, vector: torch.Tensor) -> torch.Tensor:
        """[N, V] -> [N, V + extra]: the vector, and after it what its item and job numbers stand for.
        For every item: how much of it is in the pack (in stacks) and in how many slots (id 0 is an empty
        slot), and whether it is the one in hand. Then the villager's own job, and the job of whoever a vote is about."""
        fields, items, roles = self.layout.fields, self.layout.item_scale, self.layout.role_scale
        start, length = fields["inventory"]
        pack = vector[:, start:start + length].reshape(-1, length // 2, 2)
        which = self.one_of(pack[:, :, 0] * items, self.item_ids)                     # [N, slots, items]
        amount = (which * pack[:, :, 1:2]).sum(dim=1)
        slots = which.sum(dim=1)
        hand = self.one_of(vector[:, fields["equipped tool"][0]] * items, self.item_ids)
        job = self.one_of(vector[:, fields["role"][0]] * roles, self.role_ids)
        subject = self.one_of(vector[:, fields["vote subject role"][0]] * roles, self.role_ids)
        return torch.cat([vector, amount, slots, hand, job, subject], dim=1)

    def features(self, planes: torch.Tensor, vector: torch.Tensor) -> torch.Tensor:
        """planes [N, P, S, S], vector [N, V] -> [N, 256]"""
        if self.decode:
            vector = self.read(vector)
        return self.mix(torch.cat([self.conv(planes), self.vec(vector)], dim=1))

    def encode(self, grid: torch.Tensor, vector: torch.Tensor) -> torch.Tensor:
        """grid [B, T, grid_length] integers, vector [B, T, V] -> what goes into the LSTM, [B, T, 256]."""
        b, t = vector.shape[:2]
        planes = onehot_grid(grid.reshape(b * t, -1), self.layout)
        return self.features(planes, vector.reshape(b * t, -1)).reshape(b, t, -1)

    def reach(self, out: torch.Tensor, seen: torch.Tensor) -> torch.Tensor:
        """What the heads read: what came out of the LSTM and, beside it, what went in (what the villager sees now)."""
        return torch.cat([out, seen], dim=-1) if self.skip else out

    def heads(self, out: torch.Tensor, seen: torch.Tensor):
        """What comes out of the LSTM [B, T, H] and what went into it [B, T, 256] -> action logits [B, T, A] and argument logits [B, T, A, G]."""
        b, t = out.shape[:2]
        h = self.reach(out, seen)
        return self.action_head(h), self.arg_head(h).reshape(b, t, self.layout.action_count, self.layout.arg_count)

    def forward(self, grid: torch.Tensor, vector: torch.Tensor, state=None):
        """grid [B, T, grid_length] integers, vector [B, T, V]. Returns action logits [B, T, A],
        argument logits [B, T, A, G], values [B, T] and the LSTM state after the last step."""
        seen = self.encode(grid, vector)
        out, state = self.lstm(seen, state)
        action, arg = self.heads(out, seen)
        return action, arg, self.value_head(self.reach(out, seen)).squeeze(-1), state


class ExportNet(nn.Module):
    """One villager, one tick, as the game runs it: one-hot planes in (the game builds them), raw scores out."""

    def __init__(self, net: TribeNet):
        super().__init__()
        self.net = net

    def forward(self, planes, vector, h_in, c_in):
        x = self.net.features(planes, vector).unsqueeze(1)
        out, (h_out, c_out) = self.net.lstm(x, (h_in, c_in))
        out = self.net.reach(out, x)[:, 0]
        return self.net.action_head(out), self.net.arg_head(out), self.net.value_head(out), h_out, c_out


def saved_net(saved: dict) -> TribeNet:
    """The network a model.pt holds. Files from before the network read items and jobs say nothing about it: those took the vector as it comes."""
    net = TribeNet(Layout(saved["layout"]), hidden=saved["hidden"], decode=saved.get("decode", False), skip=saved.get("skip", False))
    net.load_state_dict(saved["state"])
    return net


def to_save(net: TribeNet, **more) -> dict:
    """What goes into model.pt: enough to build the network again."""
    return {"state": net.state_dict(), "layout": net.layout.to_dict(), "hidden": net.hidden, "decode": net.decode, "skip": net.skip, **more}


def masked(logits: torch.Tensor, mask: torch.Tensor) -> torch.Tensor:
    """Logits with everything the mask forbids pushed out of reach. A row with nothing allowed is left as it is."""
    allowed = mask.bool()
    none = ~allowed.any(dim=-1, keepdim=True)
    return torch.where(allowed | none, logits, torch.full_like(logits, NEG))


def export_onnx(net: TribeNet, path: str):
    """Writes the network for the game's Inference Engine. Inputs: planes [1, P, S, S], vector [1, V], h_in, c_in [1, 1, H]."""
    layout = net.layout
    wrapper = ExportNet(net).eval().cpu()
    planes = torch.zeros(1, layout.planes, layout.grid_size, layout.grid_size)
    vector = torch.zeros(1, layout.vector_length)
    h, c = torch.zeros(1, 1, net.hidden), torch.zeros(1, 1, net.hidden)
    torch.onnx.export(
        wrapper, (planes, vector, h, c), path,
        input_names=["planes", "vector", "h_in", "c_in"],
        output_names=["action_logits", "arg_logits", "value", "h_out", "c_out"],
        opset_version=17, dynamo=False,
    )
