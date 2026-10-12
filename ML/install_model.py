"""Put a trained network into the game.

    ML/.venv/Scripts/python ML/install_model.py --experiment <experiment id> [--name villager]

Writes into Game/TinyTribe/Assets/Resources/Models/:
    <name>.onnx            the network, for Unity's Inference Engine
    <name>.json            where it came from and the shapes it expects
    <name>.reference.json  a few inputs with the outputs onnxruntime gives for them; a Unity test
                           (NeuralBrainTests) feeds the same inputs through the game and must get the same
Then choose "Trained network" on the game's launch screen.
"""
import argparse
import json
from pathlib import Path

import numpy as np
import onnxruntime as ort
import torch

from experiment import EXPERIMENTS, ROOT
from model import Layout, export_onnx, saved_net

MODELS = ROOT / "Game" / "TinyTribe" / "Assets" / "Resources" / "Models"


def main():
    p = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    p.add_argument("--experiment", required=True, help="experiment id (folder name under ML/experiments) or a path to model.pt")
    p.add_argument("--name", default="villager", help="what the game calls it")
    args = p.parse_args()

    path = Path(args.experiment)
    if not path.exists():
        path = EXPERIMENTS / args.experiment / "model.pt"
    elif path.is_dir():
        path = path / "model.pt"
    saved = torch.load(path, map_location="cpu")
    layout = Layout(saved["layout"])
    net = saved_net(saved)
    net.eval()

    MODELS.mkdir(parents=True, exist_ok=True)
    onnx_path = MODELS / (args.name + ".onnx")
    export_onnx(net, str(onnx_path))
    info = {"name": args.name, "experiment": saved.get("experiment"), "hidden": saved["hidden"], "grid_size": layout.grid_size,
            "channel_sizes": layout.channel_sizes, "vector_length": layout.vector_length, "action_count": layout.action_count,
            "arg_count": layout.arg_count}
    (MODELS / (args.name + ".json")).write_text(json.dumps(info, indent=1), encoding="utf-8")

    # A few ticks with the memory carried over, as the game will run it.
    session = ort.InferenceSession(str(onnx_path), providers=["CPUExecutionProvider"])
    rng = np.random.default_rng(0)
    h = np.zeros((1, 1, saved["hidden"]), np.float32)
    c = np.zeros((1, 1, saved["hidden"]), np.float32)
    steps = []
    for _ in range(3):
        planes = np.zeros((1, layout.planes, layout.grid_size, layout.grid_size), np.float32)
        at = 0
        for size in layout.channel_sizes:
            value = rng.integers(0, size, (layout.grid_size, layout.grid_size))
            for y in range(layout.grid_size):
                for x in range(layout.grid_size):
                    planes[0, at + value[y, x], y, x] = 1
            at += size
        vector = rng.random((1, layout.vector_length), dtype=np.float32)
        action, arg, _, h, c = session.run(None, {"planes": planes, "vector": vector, "h_in": h, "c_in": c})
        steps.append({"planes": planes.ravel().tolist(), "vector": vector.ravel().tolist(), "action_logits": action.ravel().tolist(),
                      "arg_logits": arg.ravel().tolist(), "h_out": h.ravel().tolist()})
    (MODELS / (args.name + ".reference.json")).write_text(json.dumps({"steps": steps}), encoding="utf-8")
    print(f"Installed {saved.get('experiment')} as '{args.name}' in {MODELS} ({onnx_path.stat().st_size // 1024} KB).")
    print("Let Unity import it (focus the editor), then pick 'Trained network' on the launch screen.")


if __name__ == "__main__":
    main()
