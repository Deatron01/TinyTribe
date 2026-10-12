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
| UI | Immediate-mode GUI (`OnGUI`) for the control windows | Built entirely from code, so it needs no scene or asset files (the repo tracks scripts only). A styled in-game UI can replace it later |

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

**Art in use (since 2026-10-06).** Three free CC0 packs are downloaded into `Game/ArtSources/` (not in git):

| Pack | Source | Used for |
|---|---|---|
| Ninja Adventure - Asset Pack, by Pixel-Boy and AAA | pixel-boy.itch.io/ninja-adventure-asset-pack | Terrain, trees, rocks, ores, plants, 12 villager characters with walk frames, the tent, chest and well, the campfire's flame, and the animals. Also holds item icons, emotes, UI, sounds and music not wired in yet |
| Kenney Roguelike/RPG pack | kenney.nl/assets/roguelike-rpg-pack | Not used yet; spare tiles |
| Kenney Emotes pack | kenney.nl/assets/emotes-pack | Not used yet; emote bubbles |

`python tools/install_art.py` copies the 49 files the game uses into `Assets/Resources/Art/`. `Assets/Editor/ArtImportSettings.cs` imports them as pixel art, and `Assets/View/PackArt.cs` maps game ids to tiles. If the art is missing, the game draws simple pictures in code instead, so a fresh clone still runs.

Stand-ins to replace when better CC0 sprites turn up: the pack has no wolf, deer or rabbit, so a black dog, a brown horse and a hamster are used; the bear is the pack's red bear. The campfire's stone ring is drawn in code (the pack's is two tiles wide). Buildings without a picture of their own (everything beyond the tent, chest, well and campfire) get a plain house drawn in code, coloured by category. A building site is its finished picture drawn faintly.

The local ComfyUI install can produce concept images, but it does not make clean, grid-aligned pixel art; treat it as reference only.

## 6. AI and training pipeline

**Python environment.** A dedicated virtual environment in the repo, created with `python -m venv` (the `uv venv` route is broken on this machine). Packages: PyTorch with CUDA, NumPy, pandas, openpyxl, onnx, onnxruntime, TensorBoard, Jupyter.

**Phase 1: Behavioral Cloning (course weeks 1-9).**

1. Play the game in Unity. The logger writes one row per agent per tick in the format of the `Sim BC Log Schema` sheet: grid, 189-float vector, action masks, chosen action and argument.
2. Train in PyTorch: a CNN over the 11x11x5 grid, an MLP over the vector, an LSTM on top, and two output heads (21 actions, 68 arguments). The custom loss from the GDD is written here.
3. Export to `.onnx`, drop the file into the Unity project, and the ONNX brain plays the game.

This phase needs nothing but Unity, Python and the Inference Engine.

**Phase 2: Reinforcement Learning.** Python must drive the simulation step by step. Decided 2026-10-08: neither ML-Agents option. The rules live in two assemblies that use no Unity types (`Assets/Sim`, `Assets/Brains`), so the same C# files are also built as a console program, `ML/host` (.NET, no Unity needed to run it). It plays and records villages, turns recorded runs into datasets, and serves villages to Python over a pipe: actions in, and for every villager what it sees, may do and earned out. Measured: about 70,000 villager-ticks a second with 16 villages, observation and masks included; a year of six villagers records in half a second. Training code (`ML/*.py`) is ours throughout: imitation, imitation with correction, PPO. See `ML/README.md`.

One caveat found on the way: a run recorded in Unity does not always replay exactly in the standalone program and the other way round (Unity's runtime keeps extra precision inside sums and products of three or more decimals). Within either, a replay is exact. Training, export and evaluation all use the standalone program.

**Inference in the game.** Models run through the Inference Engine on the CPU backend. The networks are small, so this is cheap.

## 7. Performance plan

The game is light, and these rules keep it that way:

- Game logic runs on ticks, independent of frame rate. Rendering targets 60 fps; the sim runs at a chosen tick rate (for example 5 ticks per second at normal speed) with a fast-forward control.
- Sim state is plain arrays and structs, not one Unity object per tile.
- The world is drawn with Tilemaps; only changed tiles are updated.
- Moving sprites (agents, animals) are pooled, never created and destroyed during play.
- All agents that share a model are evaluated in one batched inference call per tick.
- Headless training runs the sim core with rendering off, as fast as the CPU allows.

Measured in the Editor on this machine (2026-10-06, `Benchmark_full_village_ticks_per_second`): about 60,000 ticks per second for the rules alone with 40 villagers and some 95 animals on a 128x128 map, each villager taking a random action every tick. Animals look for villagers through a coarse grid, so the ones far from everyone cost almost nothing. The survival bot, which plans routes, is slower than the rules; a neural network will be slower still, so the rules are not the bottleneck.

## 8. Project layout and version control

```
TinyTribe/
  Plan/                     design documents and the workbook
  Game/TinyTribe/           the Unity project
    Assets/
      Sim/                  sim core (plain C#, own assembly, no Unity types)
      Brains/               brains and the runner that steps the sim (plain C#): survival bot (decisions, village, council, routes), random, manual; ONNX later
      View/                 map, buildings, animals, villagers, camera, windows, launch screen
      Editor/               import settings for the art
      Resources/Art/        installed art (not committed)
      StreamingAssets/Data/ exported JSON from the workbook (not committed)
      Models/               .onnx files
      Tests/EditMode/       rules, bots, data
      Tests/PlayMode/       the running game
  Game/ArtSources/          downloaded art packs (not committed)
  ML/                       Python: export script, training, notebooks
    data/                   recorded play logs (not committed)
  tools/                    install_art.py
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

**Where this stands (2026-10-08).** Steps 1 to 5 and 7 to 8 are done except for the Android module; step 6 (presenting the dataset) is a matter of showing what exists. The rules cover the map, needs, gathering, tools, crafting, building, storage, fire, homes, animals, roles, the Council with job reviews, weather, research, farming and (optional) sickness: 534 tests without graphics, 34 in Play mode. A villager's observation (grid, 189-float vector with a compass, masks per action) and its rewards (outcome or shaping, shaping off) are in the engine. Every run is recorded and replayable; `ML/host` runs the simulation without Unity; recorded runs become datasets; and the training path is built end to end: imitation, imitation with correction, PPO on outcomes, year-long evaluation against the bots, export to ONNX, and the network playing in the game. Each experiment leaves its settings, numbers and curves under `ML/experiments/` and an entry in `ML/JOURNAL.md`; `ML/report.py` builds `ML/REPORT.html` from them. What the networks reached so far is in that report. Not built yet: justice, trade, the Ages, skills, traits and ideology (rule R34 in the workbook).

**Since then (2026-10-11).** (1) Why no network could walk was found and fixed in two places: the compass now shows the nearest thing a villager could use by the way it would walk, and the bots go where it points (the vector is 195 floats); and the network's heads read what the villager sees now directly, beside its memory. The story with its numbers is in `ML/JOURNAL.md` and the workbook's Open Issues. (2) Family life is in the engine: age, emotes, giving, relationship scores, marriage, shared homes, children, grief and morale from company, with 19 tests; the bots greet each other, build a tent for a couple and sleep at home, so bot villages have weddings in the first year and children in the second. A village can grow, so recordings, replays, datasets and the trainer's villages take a changing number of villagers. (3) The Edit-mode tests also run without the Unity Editor: `dotnet test ML/tests` (549 tests in about 15 seconds).

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
