"""Exported runs as training data.

A dataset is a folder of episodes written by `tinytribe-host export` (or DatasetExporter in Unity):
one folder per recorded run, holding NumPy arrays shaped [ticks, villagers, ...] and episode.json.
They are opened memory-mapped, so a dataset far larger than memory is fine.
"""
import json
from pathlib import Path

import numpy as np

from model import Layout


class Episode:
    def __init__(self, folder: Path):
        self.folder = Path(folder)
        self.meta = json.loads((self.folder / "episode.json").read_text(encoding="utf-8"))
        self.layout = Layout(self.meta["layout"])
        self.ticks, self.agents = int(self.meta["ticks"]), int(self.meta["agents"])
        load = lambda name: np.load(self.folder / (name + ".npy"), mmap_mode="r")
        self.grid, self.vector = load("grid"), load("vector")
        self.action_mask, self.arg_mask = load("action_mask"), load("arg_mask")
        self.action, self.reward, self.alive = load("action"), load("reward"), load("alive")

    @property
    def rows(self) -> int:
        return int(self.alive.sum())

    def returns(self, gamma: float, empathy_weighted: bool = True) -> np.ndarray:
        """Discounted reward-to-go of every villager at every tick, [ticks, villagers].

        With empathy_weighted the design's formula is applied first:
        R_total = R_individual x (1 - empathy / 2) + R_global x empathy.
        """
        reward = np.asarray(self.reward, dtype=np.float32)
        if empathy_weighted:
            empathy = np.array([v["empathy"] for v in self.meta["villagers"]], dtype=np.float32)
            total = reward[:, :, 0] * (1.0 - empathy / 2.0) + reward[:, :, 1] * empathy
        else:
            total = reward.sum(axis=2)
        out = np.zeros_like(total)
        running = np.zeros(self.agents, dtype=np.float32)
        for t in range(self.ticks - 1, -1, -1):
            running = total[t] + gamma * running
            out[t] = running
        return out


def load_episodes(dataset_folder) -> list:
    folders = sorted(p for p in Path(dataset_folder).iterdir() if (p / "episode.json").exists())
    if not folders:
        raise FileNotFoundError(f"No exported episodes in {dataset_folder}")
    episodes = [Episode(f) for f in folders]
    first = episodes[0].layout.to_dict()
    for e in episodes:
        if e.layout.to_dict() != first:
            raise ValueError(f"{e.folder.name} was exported with another observation layout than {episodes[0].folder.name}")
    return episodes


def split_by_seed(episodes: list, val_fraction: float) -> tuple:
    """Whole villages go to one side or the other, so validation villages are ones the network never trained on.
    The split is by seed, highest seeds to validation: the same every time."""
    ordered = sorted(episodes, key=lambda e: int(e.meta["seed"]))
    n_val = max(1, round(len(ordered) * val_fraction))
    return ordered[:-n_val], ordered[-n_val:]


def takes_argument(episode: Episode) -> np.ndarray:
    flags = episode.meta.get("takes_argument")
    if flags is None:  # exported before the field existed: the same thing from the workbook's actions table
        data = Path(__file__).resolve().parent.parent / "Game" / "TinyTribe" / "Assets" / "StreamingAssets" / "Data" / "actions.json"
        flags = [row["argument"] != "none" for row in json.loads(data.read_text(encoding="utf-8"))]
    return np.array(flags, dtype=bool)


class Windows:
    """Cuts episodes into windows of `length` consecutive ticks, all villagers of the village together.

    A batch is `blocks` windows, so blocks x villagers sequences. Each sequence starts the LSTM from
    nothing; the first `burn_in` ticks only warm it up and are left out of the loss by the trainer.
    """

    def __init__(self, episodes: list, length: int, blocks: int, rng: np.random.Generator = None, returns: dict = None):
        self.episodes, self.length, self.blocks, self.rng, self.returns = episodes, length, blocks, rng, returns
        self.layout = episodes[0].layout
        self.takes_argument = takes_argument(episodes[0])
        self.starts = [(e, s) for e in episodes for s in range(0, e.ticks - length + 1, length)]

    def __len__(self) -> int:
        return max(1, len(self.starts) // self.blocks)

    def _block(self, episode: Episode, start: int) -> dict:
        s = slice(start, start + self.length)
        layout = self.layout
        action = np.asarray(episode.action[s])                               # [T, A, 2]
        packed = np.asarray(episode.arg_mask[s])                             # [T, A, bytes]
        table = np.unpackbits(packed, axis=-1)[..., : layout.action_count * layout.arg_count]
        table = table.reshape(self.length, episode.agents, layout.action_count, layout.arg_count)
        # Only the row of the action that was chosen is needed to train the argument head.
        chosen = np.take_along_axis(table, action[..., 0].astype(np.int64)[..., None, None], axis=2)[:, :, 0]
        block = {
            "grid": np.asarray(episode.grid[s]), "vector": np.asarray(episode.vector[s]),
            "action_mask": np.asarray(episode.action_mask[s]), "arg_mask": chosen,
            "action": action, "alive": np.asarray(episode.alive[s]),
        }
        if self.returns is not None:
            block["return"] = self.returns[episode.folder.name][s]
        # [T, A, ...] -> [A, T, ...]: one sequence per villager.
        return {k: np.swapaxes(v, 0, 1) for k, v in block.items()}

    def batches(self, shuffle: bool):
        order = np.arange(len(self.starts))
        if shuffle:
            # A random offset as well, so windows do not always begin on the same ticks.
            self.rng.shuffle(order)
        for i in range(0, len(order) - self.blocks + 1, self.blocks):
            blocks = []
            for j in order[i:i + self.blocks]:
                episode, start = self.starts[j]
                if shuffle:
                    start = int(self.rng.integers(0, max(1, episode.ticks - self.length + 1)))
                blocks.append(self._block(episode, start))
            yield {k: np.concatenate([b[k] for b in blocks], axis=0) for k in blocks[0]}
