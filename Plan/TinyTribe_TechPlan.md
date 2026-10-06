# TinyTribe: Development Tech Plan

Written 2026-10-06. Companion to `TinyTribe_GDD.md` (what the game is) and `AISim_English_v12.xlsx` (the numbers). This document covers what we build it with.

## 1. Decision

**Use Unity 6.3 LTS, with our own PyTorch code for training and Unity's Inference Engine to run the trained models inside the game.**

Why Unity fits the requirements:

| Requirement | How Unity meets it |
|---|---|
| Android + PC from one project | Both are first-class build targets; same code and assets. |
| Web (nice to have) | WebGL build target. Works for this game; see section 9 for limits. |
| 2D pixel art, easy to develop | Tilemap, Sprite Atlas, Pixel Perfect Camera and an Aseprite importer are built in. |
| Fast running | The simulation is a small grid (128x128, up to 40 agents). The plan in section 3 keeps it far below what a phone can handle. |
| Trained neural networks in the game | Inference Engine (formerly Sentis) runs `.onnx` models on every Unity platform, offline. |
| Course needs our own CNN / LSTM / custom loss | Training stays in plain PyTorch, outside Unity, so the models are ours. |

Alternatives considered:

- **Godot.** Lighter and fully open source, and a good 2D engine. Rejected because there is no first-party way to run ONNX models on Android; we would have to build and maintain that ourselves, and it is the one part of the project we cannot afford to have fail.
- **Pure Python simulation (NumPy / PettingZoo) plus a separate viewer.** Fastest possible training, but the game would have to be written twice (once in Python, once for phones) and kept identical. Rejected for a one-semester project.

## 2. What is already on this machine

Checked on 2026-10-06.

| Tool | Status |
|---|---|
| Unity 6000.3.23f1 (6.3 LTS) | Installed, **Windows build module only** |
| Unity 6000.4.9f1 | Installed (Windows + WebGL). Not LTS; do not use for this project |
| Unity 2022.3.55f1 | Installed. Not needed |
| Visual Studio 2022, VS Code | Installed |
| Git, Git LFS | Installed. Repo: `github.com/Deatron01/TinyTribe` |
| Python 3.11.0 | Installed |
| Docker Desktop | Installed |
| NVIDIA RTX 3070 Ti (8 GB) | Enough for every model in this project |
| Android Build Support for 6000.3 | **Missing** |
| WebGL Build Support for 6000.3 | **Missing** |
| Aseprite (or other pixel editor) | **Not found** |

To install: open Unity Hub, Installs, the gear next to 6000.3.23f1, Add modules, and tick **Android Build Support** (with OpenJDK and Android SDK & NDK Tools) and **Web Build Support**.

## 3. Architecture

Three parts, kept separate on purpose.

```
AISim_English_v12.xlsx  --export script-->  JSON data files
                                                  |
                                                  v
        +-------------------------------------------------------+
        |  Unity project                                        |
        |                                                       |
        |   Sim core (plain C#, no Unity types)                 |
        |     world grid, agents, needs, recipes, rules, masks  |
        |     advances one tick at a time, deterministic        |
        |             |                         |               |
        |             v                         v               |
        |   View (Unity)                 Brain interface        |
        |     tilemap, sprites,            human input          |
        |     emote bubbles, UI            or ONNX model        |
        |                                       |               |
        +---------------------------------------|---------------+
                                                v
                              play logs (state, mask, action)
                                                |
                                                v
                     Python / PyTorch: train -> export .onnx
                                                |
                                                +--> back into Unity
```

**Sim core.** All game rules live in a C# assembly that does not reference `UnityEngine`. It loads the JSON data, holds the world state, and exposes `Step(actions)`. This is the single most important decision for speed and for the AI work:

- The simulation can run many ticks per rendered frame (fast-forward), or with no rendering at all for training.
- It can be unit-tested without opening a scene.
- A fixed random seed gives identical runs, which makes bugs and AI behaviour reproducible.

**View.** Unity reads the sim state each frame and draws it. Agents and animals are sprites that slide between tiles; the logic does not live on them.

**Brain interface.** Each agent asks a "brain" for its action every tick. There are three implementations behind one interface: human input (for recording data), an ONNX model (for the trained AI), and a simple scripted bot (for testing before any model exists).

**Data.** The workbook stays the single source of truth. A small Python script exports every `Sim ...` sheet to JSON in the Unity project. Nobody types game numbers into C#.

## 4. Unity setup

| Item | Choice | Reason |
|---|---|---|
| Version | 6000.3 LTS | Long-term support; already installed |
| Template | Universal 2D | 2D renderer with the Pixel Perfect Camera |
| Scripting backend | IL2CPP | Required for Android 64-bit; faster than Mono |
| Android target | ARM64, minimum Android 8 (API 26) | Covers practically all current phones |
| Input | Input System package | One code path for keyboard, mouse and touch |
| UI | uGUI (Canvas) | Simplest for a HUD, inventory and the Social Web view |

Packages to add:

| Package | Purpose |
|---|---|
| 2D Tilemap, 2D Sprite, 2D Pixel Perfect, 2D Aseprite Importer | The world and its art (most come with the 2D template) |
| Inference Engine (`com.unity.ai.inference`) | Runs the `.onnx` models |
| Newtonsoft Json (`com.unity.nuget.newtonsoft-json`) | Loads the exported data files |
| Test Framework | Unit tests for the sim core |
| ML-Agents (`com.unity.ml-agents`) | **Not at the start.** Only if chosen for the reinforcement-learning phase; see section 6 |

## 5. Art and audio

**Style.** 16x16 pixel tiles and characters. Import settings for every sprite: Pixels Per Unit 16, Filter Mode Point, Compression None. Pack sprites into a Sprite Atlas to keep draw calls low.

| Tool | Cost | Use |
|---|---|---|
| Aseprite | About 20 USD once (free if compiled from source) | Sprites and animations; Unity imports `.aseprite` files directly |
| LibreSprite or Piskel | Free | Alternatives to Aseprite |
| Kenney asset packs (CC0) | Free | Placeholder tiles and characters so development is never blocked on art |
| jsfxr, Audacity | Free | Sound effects, if wanted |

Start with free placeholder art and replace it later. The list of what needs a sprite comes straight from the workbook: 9 terrain types, 19 resource nodes, 25 buildings, 4 animals, 12 emotes, 57 item icons, and the agent.

The local ComfyUI install can produce concept images, but it does not make clean, grid-aligned pixel art; treat it as reference only.

## 6. AI and training pipeline

**Python environment.** A dedicated virtual environment in the repo, created with `python -m venv` (the `uv venv` route is broken on this machine). Packages: PyTorch with CUDA, NumPy, pandas, openpyxl, onnx, onnxruntime, TensorBoard, Jupyter.

**Phase 1: Behavioral Cloning (course weeks 1-9).**

1. Play the game in Unity. The logger writes one row per agent per tick in the format of the `Sim BC Log Schema` sheet: grid, 132-float vector, action masks, chosen action and argument.
2. Train in PyTorch: a CNN over the 11x11x5 grid, an MLP over the vector, an LSTM on top, and two output heads (21 actions, 66 arguments). The custom loss from the GDD is written here.
3. Export to `.onnx`, drop the file into the Unity project, and the ONNX brain plays the game.

This phase needs nothing but Unity, Python and the Inference Engine.

**Phase 2: Reinforcement Learning (after week 9).** Python must drive the Unity simulation step by step. Two ways to connect them; decide at week 9, not now:

| Option | What it is | Trade-off |
|---|---|---|
| A. ML-Agents as the bridge, our own PPO | Use the ML-Agents package and its low-level Python API only to pass observations and actions; training code stays ours | Keeps the models ours. Needs a separate Python 3.10 environment, because ML-Agents recommends Python 3.10.12 |
| B. ML-Agents built-in trainers | Use its ready-made PPO / MA-POCA | Fastest to get running, but the network architecture and loss are no longer ours, which conflicts with the course requirements |

Recommendation: A.

**Inference in the game.** Models run through the Inference Engine on the CPU backend. The networks are small, so this is cheap.

## 7. Performance plan

The game is light, and these rules keep it that way:

- Game logic runs on ticks, independent of frame rate. Rendering targets 60 fps; the sim runs at a chosen tick rate (for example 5 ticks per second at normal speed) with a fast-forward control.
- Sim state is plain arrays and structs, not one Unity object per tile.
- The world is drawn with Tilemaps; only changed tiles are updated.
- Moving sprites (agents, animals) are pooled, never created and destroyed during play.
- All agents that share a model are evaluated in one batched inference call per tick.
- Headless training runs the sim core with rendering off, as fast as the CPU allows.

## 8. Project layout and version control

```
TinyTribe/
  Plan/                     design documents and the workbook
  Game/                     the Unity project
    Assets/
      Sim/                  sim core (plain C#, own assembly)
      View/                 rendering, UI, input
      Brains/               human, scripted, ONNX
      Art/                  sprites, atlases, tilesets
      StreamingAssets/Data/ exported JSON from the workbook
      Models/               .onnx files
      Tests/
  ML/                       Python: export script, training, notebooks
    data/                   recorded play logs (not committed)
  Server/                   Docker leaderboard (week 13)
```

Git setup before the first Unity commit:

- Add the standard Unity `.gitignore` (ignores `Library/`, `Temp/`, `Logs/`, `Build/`).
- Track binary files with Git LFS: `*.png`, `*.aseprite`, `*.onnx`, `*.wav`, `*.ogg`.
- Keep recorded play logs and Python virtual environments out of the repo.

## 9. Platforms

| Platform | Status | Notes |
|---|---|---|
| Windows PC | Primary | Development and training happen here |
| Android | Primary | Needs the Android module (section 2). Test on a real phone over USB; touch controls for camera pan, zoom and agent inspection |
| Web | Optional | WebGL build. Single-threaded and slower to load; fine for watching a trained village, not for training. Decide after the Android build works |

Publishing on Google Play costs a one-time 25 USD developer fee. Sharing an `.apk` file directly for the course demo costs nothing.

## 10. Leaderboard server (course week 13)

A small Python web service (FastAPI) with a SQLite database, packaged with Docker; Docker Desktop is already installed. It needs two endpoints: submit a village's survival time, and list the top results. The game calls it only when online; everything else works offline.

## 11. Costs

Everything is free except two optional items: Aseprite (about 20 USD) and the Google Play fee (25 USD, only if publishing). Unity Personal is free for projects earning under 200,000 USD a year.

## 12. Build order

| Step | Course week | Result |
|---|---|---|
| 1. Install Unity modules, create the project in `Game/`, set up Git LFS and `.gitignore` | 1-2 | Empty project builds for Windows and Android |
| 2. Export script: workbook to JSON | 2 | Data files in the project |
| 3. Sim core, phase 1 content only: map, movement, needs, gathering, crafting, a few buildings | 2-4 | Rules run in tests with no graphics |
| 4. View and human controls with placeholder art | 3-4 | Playable prototype |
| 5. Play logger | 4 | Recorded sessions on disk |
| 6. Dataset cleaning and presentation | 5 | Week 5 deliverable |
| 7. PyTorch models: CNN, LSTM, custom loss; export to ONNX | 6-9 | Week 9 deliverable |
| 8. ONNX brain in Unity; first AI-driven village | 9-10 | AI plays the game |
| 9. Android build, touch UI, performance pass | 10-12 | Week 12 deliverable |
| 10. Docker leaderboard | 13 | Week 13 deliverable |
| 11. Demo polish; reinforcement learning and later-Age content if time allows | 14 | Final presentation |

Build only phase 1 rows from the workbook first (Stone Age: 42 items, 23 recipes, 17 buildings). The Iron and Gold Age content, the economy, justice and ideologies come after a working AI village exists.

## 13. Risks to check early

| Risk | Why it matters | Check |
|---|---|---|
| LSTM export to ONNX and the Inference Engine | The whole inference path depends on it. LSTMs need their hidden state passed in and out explicitly | In week 1, export a tiny dummy CNN+LSTM from PyTorch and run it in Unity on Windows and on a phone |
| One network per agent (neuroevolution) | The GDD gives every agent its own weights. Forty separate models cannot be batched and are awkward to breed at runtime inside the game | For the app, share one model per generation or per role; keep per-agent evolution on the Python training side |
| Scope | The design has 61 sheets of content; the semester has 14 weeks | Hold to phase 1 until step 8 is done |
| Sim speed for reinforcement learning | Unity is slower than a pure Python grid sim | Measure ticks per second headless at step 3; the plain-C# sim core can be run outside Unity if needed |
| Unplaytested numbers | Every value in the workbook is a first pass | Expect a tuning round after the first play sessions in step 4 |

## Sources

- [ML-Agents 4.0 installation (Unity and Python version requirements)](https://docs.unity3d.com/Packages/com.unity.ml-agents@4.0/manual/Installation.html)
- [ML-Agents and the Inference Engine](https://docs.unity3d.com/Packages/com.unity.ml-agents@4.0/manual/Inference-Engine.html)
- [Inference Engine manual (platform and ONNX support)](https://docs.unity3d.com/Packages/com.unity.ai.inference@2.4/manual/index.html)
