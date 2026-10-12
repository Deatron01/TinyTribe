"""Villages for a trainer: the simulation program (`tinytribe-host serve`) driven from Python.

    env = TribeEnv(envs=16, max_ticks=1680, seed=1)
    frame = env.frame                 # what every villager of every village sees now
    frame = env.step(actions)         # actions: uint8 [envs, agents, 2] = (action, argument)
    env.close()

A frame holds, for every village and villager: grid [E, A, grid_length] uint8, vector [E, A, V]
float32, action_mask [E, A, actions] uint8, arg_mask [E, A, bytes] uint8 (packed bits, one row of
`arg_count` flags per action), reward [E, A, 2] float32 (for itself, as one of the village), alive
[E, A] uint8 (A is max_agents when that is given: the villagers a village starts with and places for
the children born to them, whose rows are zeros and alive 0 until they are); done [E] uint8; expert [E, A, 2] uint8 (with expert=True: what the scripted bot would
do in each villager's place); and `finished`, the villages that ended on that tick. A village that
ends is replaced at once by a new one with the next seed, and that is what the frame then shows.

`Actor` runs a network on frames: it keeps each villager's LSTM state and picks, among what the
masks allow, an action and then an argument for it.
"""
import json
import os
import struct
import subprocess
from pathlib import Path

import numpy as np
import torch

from model import Layout, masked

ROOT = Path(__file__).resolve().parent.parent
# TINYTRIBE_HOST points at another build of the simulator, e.g. one built aside while a long run still uses the usual one.
HOST = Path(os.environ.get("TINYTRIBE_HOST") or ROOT / "ML" / "host" / "bin" / "Release" / "net10.0" / "tinytribe-host.exe")
# TINYTRIBE_DATA points that build at a copy of the exported workbook kept with it, so the game's own can change meanwhile.
DATA_ARGS = ["--data", os.environ["TINYTRIBE_DATA"]] if os.environ.get("TINYTRIBE_DATA") else []

# The names the host's EnvOptions uses.
OPTION_NAMES = {
    "envs": "Envs", "max_ticks": "MaxTicks", "seed": "Seed", "agents": "Agents", "map_width": "MapWidth", "map_height": "MapHeight",
    "start_season": "StartSeason", "weather": "Weather", "sickness": "Sickness", "job_reviews": "JobReviews",
    "reward_shaping_scale": "RewardShapingScale", "threads": "Threads", "record_folder": "RecordFolder", "brains_label": "BrainsLabel",
    "expert": "Expert", "expert_remembers": "ExpertRemembers", "stagger": "Stagger", "max_agents": "MaxAgents", "expert_situational": "ExpertSituational",
}


class TribeEnv:
    def __init__(self, **options):
        if not HOST.exists():
            raise FileNotFoundError(f"{HOST} is not built. Run: dotnet build ML/host -c Release")
        unknown = set(options) - set(OPTION_NAMES)
        if unknown:
            raise TypeError(f"unknown options {sorted(unknown)}; known: {sorted(OPTION_NAMES)}")
        if options.get("record_folder") is not None:
            options["record_folder"] = str(Path(options["record_folder"]).resolve())
        self.options = options
        self.process = subprocess.Popen([str(HOST), "serve"] + DATA_ARGS, stdin=subprocess.PIPE, stdout=subprocess.PIPE, cwd=ROOT)
        message = json.dumps({OPTION_NAMES[k]: v for k, v in options.items()}).encode("utf-8")
        self.process.stdin.write(b"I" + struct.pack("<i", len(message)) + message)
        self.process.stdin.flush()
        self.hello = self._json()
        self.layout = Layout(self.hello)
        self.envs, self.agents = int(self.hello["envs"]), int(self.hello["agents"])
        self.arg_mask_bytes = int(self.hello["arg_mask_bytes"])
        self.action_names = self.hello["actions"]
        self.takes_argument = np.array(self.hello["takes_argument"], dtype=bool)
        rows, lay = self.envs * self.agents, self.layout
        self._parts = [
            ("grid", np.uint8, (self.envs, self.agents, lay.grid_length)),
            ("vector", np.float32, (self.envs, self.agents, lay.vector_length)),
            ("action_mask", np.uint8, (self.envs, self.agents, lay.action_count)),
            ("arg_mask", np.uint8, (self.envs, self.agents, self.arg_mask_bytes)),
            ("reward", np.float32, (self.envs, self.agents, 2)),
            ("alive", np.uint8, (self.envs, self.agents)),
            ("done", np.uint8, (self.envs,)),
            ("expert", np.uint8, (self.envs, self.agents, 2)),
        ]
        self._size = sum(int(np.prod(shape)) * np.dtype(kind).itemsize for _, kind, shape in self._parts)
        self.frame = self._frame()

    def _read(self, count: int) -> bytes:
        data = self.process.stdout.read(count)
        if len(data) != count:
            raise ConnectionError("the simulation program stopped answering (exit code %s)" % self.process.poll())
        return data

    def _json(self):
        (length,) = struct.unpack("<i", self._read(4))
        return json.loads(self._read(length).decode("utf-8"))

    def _frame(self) -> dict:
        raw = self._read(self._size)
        frame, at = {}, 0
        for name, kind, shape in self._parts:
            size = int(np.prod(shape)) * np.dtype(kind).itemsize
            frame[name] = np.frombuffer(raw, dtype=kind, count=int(np.prod(shape)), offset=at).reshape(shape)
            at += size
        frame["finished"] = self._json()
        return frame

    def step(self, actions: np.ndarray) -> dict:
        actions = np.ascontiguousarray(actions, dtype=np.uint8)
        assert actions.shape == (self.envs, self.agents, 2), actions.shape
        self.process.stdin.write(b"S" + actions.tobytes())
        self.process.stdin.flush()
        self.frame = self._frame()
        return self.frame

    def reset(self) -> dict:
        self.process.stdin.write(b"R")
        self.process.stdin.flush()
        self.frame = self._frame()
        return self.frame

    def close(self):
        if self.process.poll() is None:
            try:
                self.process.stdin.write(b"Q")
                self.process.stdin.flush()
                self.process.wait(timeout=30)
            except Exception:
                self.process.kill()

    def __enter__(self):
        return self

    def __exit__(self, *exc):
        self.close()


def chosen_arg_mask(arg_mask: np.ndarray, action: np.ndarray, layout: Layout) -> np.ndarray:
    """The valid arguments of each villager's chosen action. arg_mask [N, bytes] packed, action [N] -> [N, arg_count] 0/1."""
    table = np.unpackbits(arg_mask, axis=-1)[:, : layout.action_count * layout.arg_count]
    table = table.reshape(len(action), layout.action_count, layout.arg_count)
    return table[np.arange(len(action)), action]


class Actor:
    """Plays every villager of an environment with one network, remembering each villager's LSTM state."""

    def __init__(self, net, env: TribeEnv, device, greedy: bool = False, temperature: float = 1.0):
        self.net, self.env, self.device, self.greedy, self.temperature = net, env, device, greedy, temperature
        self.n = env.envs * env.agents
        self.state = net.initial_state(self.n, device)

    @torch.no_grad()
    def act(self, frame: dict) -> dict:
        """Chooses for everyone. Returns the actions [E, A, 2] and, for a trainer, their log-probability and the value seen."""
        env, lay, n = self.env, self.env.layout, self.n
        grid = torch.from_numpy(frame["grid"].reshape(n, 1, -1)).to(self.device)
        vector = torch.from_numpy(frame["vector"].reshape(n, 1, -1)).to(self.device)
        action_mask = torch.from_numpy(frame["action_mask"].reshape(n, -1)).to(self.device)
        state_before = self.state
        action_logits, arg_logits, value, self.state = self.net(grid, vector, self.state)
        action_dist = torch.distributions.Categorical(logits=masked(action_logits[:, 0].float() / self.temperature, action_mask))
        action = action_dist.probs.argmax(-1) if self.greedy else action_dist.sample()

        arg_mask = torch.from_numpy(chosen_arg_mask(frame["arg_mask"].reshape(n, -1), action.cpu().numpy(), lay)).to(self.device)
        row = arg_logits[:, 0].float()[torch.arange(n, device=self.device), action]
        arg_dist = torch.distributions.Categorical(logits=masked(row / self.temperature, arg_mask))
        arg = arg_dist.probs.argmax(-1) if self.greedy else arg_dist.sample()

        actions = torch.stack([action, arg], dim=-1).to(torch.uint8).cpu().numpy().reshape(env.envs, env.agents, 2)
        return {
            "actions": actions, "action": action, "arg": arg, "arg_mask": arg_mask,
            "logprob": action_dist.log_prob(action) + arg_dist.log_prob(arg), "value": value[:, 0].float(),
            "state": state_before,
        }

    def forget(self, done: np.ndarray):
        """A village that ended was replaced by a new one: its villagers start with an empty memory."""
        if not done.any():
            return
        gone = torch.from_numpy(np.repeat(done.astype(bool), self.env.agents)).to(self.device)
        h, c = self.state
        h[:, gone] = 0
        c[:, gone] = 0
