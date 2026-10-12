"""Risk test: can a CNN + LSTM policy exported from PyTorch run in Unity's Inference Engine?

Builds a tiny untrained network with the real input/output shapes (grid, state vector,
action and argument heads as the exported config gives them; explicit LSTM state in and out),
exports it to ONNX, and records reference outputs from onnxruntime for three
consecutive ticks. The Unity EditMode test InferenceSmokeTests replays the same
inputs and must get the same numbers.

Usage:  ML/.venv/Scripts/python ML/make_smoke_model.py
"""
import json
from pathlib import Path

import numpy as np
import onnxruntime as ort
import torch
import torch.nn as nn

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "Game" / "TinyTribe" / "Assets"
MODEL_PATH = ASSETS / "Models" / "smoke_lstm.onnx"
REFERENCE_PATH = ASSETS / "StreamingAssets" / "Tests" / "smoke_lstm_reference.json"

# The real sizes, from the workbook by way of the exported config, so a change there cannot be missed here.
CONFIG = {row["key"]: row["value"] for row in json.loads((ASSETS / "StreamingAssets" / "Data" / "config.json").read_text(encoding="utf-8"))}
GRID_SIZE, GRID_CHANNELS = int(CONFIG["fov_size"]), int(CONFIG["fov_channels"])
VECTOR_LEN, NUM_ACTIONS, NUM_ARGS = int(CONFIG["obs_vector_length"]), int(CONFIG["num_actions"]), int(CONFIG["num_action_args"])
HIDDEN = 32
STEPS = 3


class SmokePolicy(nn.Module):
    def __init__(self):
        super().__init__()
        self.conv = nn.Sequential(
            nn.Conv2d(GRID_CHANNELS, 8, kernel_size=3, padding=1), nn.ReLU(),
            nn.Conv2d(8, 8, kernel_size=3, stride=2), nn.ReLU(),
            nn.Flatten(),
        )
        self.vec = nn.Sequential(nn.Linear(VECTOR_LEN, 32), nn.ReLU())
        self.lstm = nn.LSTM(8 * 5 * 5 + 32, HIDDEN, batch_first=True)
        self.action_head = nn.Linear(HIDDEN, NUM_ACTIONS)
        self.arg_head = nn.Linear(HIDDEN, NUM_ARGS)

    def forward(self, grid, vector, h_in, c_in):
        x = torch.cat([self.conv(grid), self.vec(vector)], dim=1).unsqueeze(1)
        out, (h_out, c_out) = self.lstm(x, (h_in, c_in))
        out = out[:, 0]
        return self.action_head(out), self.arg_head(out), h_out, c_out


def main():
    torch.manual_seed(0)
    rng = np.random.default_rng(0)
    model = SmokePolicy().eval()

    grid = torch.zeros(1, GRID_CHANNELS, GRID_SIZE, GRID_SIZE)
    vector = torch.zeros(1, VECTOR_LEN)
    h = torch.zeros(1, 1, HIDDEN)
    c = torch.zeros(1, 1, HIDDEN)

    MODEL_PATH.parent.mkdir(parents=True, exist_ok=True)
    torch.onnx.export(
        model, (grid, vector, h, c), str(MODEL_PATH),
        input_names=["grid", "vector", "h_in", "c_in"],
        output_names=["action_logits", "arg_logits", "h_out", "c_out"],
        opset_version=17, dynamo=False,
    )

    session = ort.InferenceSession(str(MODEL_PATH), providers=["CPUExecutionProvider"])
    h_np = np.zeros((1, 1, HIDDEN), dtype=np.float32)
    c_np = np.zeros((1, 1, HIDDEN), dtype=np.float32)
    steps = []
    for _ in range(STEPS):
        grid_np = rng.integers(0, 8, size=(1, GRID_CHANNELS, GRID_SIZE, GRID_SIZE)).astype(np.float32)
        vector_np = rng.random((1, VECTOR_LEN), dtype=np.float32)
        action, arg, h_np, c_np = session.run(
            None, {"grid": grid_np, "vector": vector_np, "h_in": h_np, "c_in": c_np})
        steps.append({
            "grid": grid_np.ravel().tolist(),
            "vector": vector_np.ravel().tolist(),
            "action_logits": action.ravel().tolist(),
            "arg_logits": arg.ravel().tolist(),
            "h_out": h_np.ravel().tolist(),
        })

    REFERENCE_PATH.parent.mkdir(parents=True, exist_ok=True)
    REFERENCE_PATH.write_text(json.dumps({
        "grid_shape": [1, GRID_CHANNELS, GRID_SIZE, GRID_SIZE],
        "vector_len": VECTOR_LEN, "hidden": HIDDEN, "steps": steps,
    }), encoding="utf-8")
    print(f"Wrote {MODEL_PATH} ({MODEL_PATH.stat().st_size} bytes)")
    print(f"Wrote {REFERENCE_PATH} ({STEPS} steps)")
    print("step 0 argmax action", int(np.argmax(steps[0]["action_logits"])),
          "arg", int(np.argmax(steps[0]["arg_logits"])))


if __name__ == "__main__":
    main()
