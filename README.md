<h1 align="center">TinyTribe</h1>

<p align="center">
  Tiny agents learn to survive, then build a civilisation.<br />
  <samp>UNITY 6.3 LTS · PYTORCH · ONNX · REINFORCEMENT LEARNING · PLANNING STAGE</samp>
</p>

### <samp>01 — WHAT IT IS</samp>

A 2D grid-based survival simulation where AI agents, trained with deep and reinforcement
learning, learn to stay alive and then build a civilisation. They have to balance their own
needs, coordinate with each other, and adapt to an environment that does not forgive
mistakes.

**Status: planning.** This repository currently holds the design, not the game. There is
no game code yet.

### <samp>02 — THE GAME</samp>

- **A world that changes.** A full seasonal cycle, weather events (rain, storms,
  heatwaves), trees with life cycles, finite local resources, and wildfires that spread
  from unattended campfires.
- **Needs that compete.** Health, hunger, thirst, energy, temperature and morale. Let one
  fail and the agent is penalised or dies.
- **An economy to grow into.** Raw resources, processed materials, workstations, four tiers
  of tools, and buildings from campfires and wells up to smelteries and cabins.

The simulation is deliberately small: a 128×128 grid with up to 40 agents.

### <samp>03 — THE TECHNICAL PLAN</samp>

| Decision | Choice |
|---|---|
| Engine | Unity 6.3 LTS, for Android and PC from one project, with WebGL as a nice-to-have |
| Rendering | 2D pixel art: Tilemap, Sprite Atlas, Pixel Perfect Camera |
| Training | Plain PyTorch, outside Unity, so the CNN / LSTM models and custom losses are our own |
| Inference | Unity's Inference Engine (formerly Sentis) running the exported `.onnx` models in-game, offline |

Godot and a pure-Python simulation with a separate viewer were both considered and
rejected. The reasoning is in the tech plan.

### <samp>04 — DOCUMENTS</samp>

| File | What it is |
|---|---|
| [`Plan/TinyTribe_GDD.md`](./Plan/TinyTribe_GDD.md) | Game design document: what the game is |
| [`Plan/TinyTribe_TechPlan.md`](./Plan/TinyTribe_TechPlan.md) | Tech plan: what it is built with, and why |
| `Plan/AISim_English_v12.xlsx` | The numbers: balance tables for the simulation |
| `Plan/xlsx_dump.txt` | A plain-text dump of that spreadsheet |
| `Plan/Plan.txt` | Early planning notes |

<br />

<p align="center">
  <a href="https://github.com/Deatron01"><img src="https://raw.githubusercontent.com/Deatron01/Deatron01/main/assets/icon.png" width="56" alt="Dea" /></a><br />
  <sub><i>Little chaos in a quiet mind.</i></sub><br />
  <sub><samp>MORE WORK AT <a href="https://github.com/Deatron01">GITHUB.COM/DEATRON01</a></samp></sub>
</p>
