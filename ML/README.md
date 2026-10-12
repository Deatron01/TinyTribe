# TinyTribe: training the villagers

Everything needed to go from the game's rules to a trained network playing in the game, and to
show how it got there. Each step is one command; each experiment writes a folder that explains
itself and an entry in [JOURNAL.md](JOURNAL.md).

## The pieces

| What | Where | What it does |
|---|---|---|
| The rules | `Game/TinyTribe/Assets/Sim` | The simulation. Plain C#, no Unity. Numbers come from the workbook (`Plan/AISim_English_v12.xlsx`). |
| What a villager sees | `Simulation.Observation.cs` | An 11 x 11 x 5 grid, a 195-value vector, and masks of what is possible (workbook sheet `Sim Observation`). |
| What it is paid | `Simulation.Rewards.cs` | Sheet `Sim Rewards`. Outcomes only by default (`reward_shaping_scale` = 0). |
| The standalone simulator | `ML/host` | The same C# files built as a console program: plays, records, exports, and serves villages to Python. |
| Recorded runs | `ML/data/runs/...` | One folder per village: every action, every event, counters, achievements, `report.md`. Replayable. |
| Datasets | `ML/data/datasets/...` | Recorded runs turned into arrays: what each villager saw, could do, chose, earned. |
| The network | `ML/model.py` | CNN over the grid, MLP over the vector (items and jobs read out of it first), LSTM; the heads for action, argument and value read what the villager sees now beside what the LSTM remembers. |
| The tests | `ML/tests` | The game's Edit-mode tests run with `dotnet test ML/tests`, no Unity Editor needed. |
| Experiments | `ML/experiments/...` | `config.json`, `metrics.csv`, `curves.svg`, `summary.json` (and the weights) per run; `index.csv` lists them. |
| The journal | `ML/JOURNAL.md` | What was tried, why, and what came of it, in order. Written by the scripts. |

## Once

```bash
python ML/export_data.py                 # workbook -> JSON for the game (after any workbook change)
dotnet build ML/host -c Release          # the standalone simulator
```

The simulator needs the .NET SDK and the Unity project opened once (it borrows Unity's JSON
library). Python packages: PyTorch with CUDA, NumPy, pandas, onnx, onnxruntime (`ML/.venv`).

## The path

`H` is `ML/host/bin/Release/net10.0/tinytribe-host.exe`, `PY` is `ML/.venv/Scripts/python`.

**1. Demonstrations.** The scripted survival bots play villages; every run is recorded.

```bash
H batch  --out ML/data/runs/bots --seeds 101-148 --days 28
H export --runs ML/data/runs/bots --out ML/data/datasets/bots
```

**2. Imitation.** The network learns to choose what the bots chose. Whole villages are held out.

```bash
PY ML/train_bc.py --dataset ML/data/datasets/bots --name bc-balanced --balance 0.5
```

`--balance` weighs rare actions up. Without it the network learns to sleep and walk like the bots
and almost never eats, because eating is under 2% of ticks. `--return-weight 1` turns the loss into
the design's custom loss: examples are weighted by what came after them for that villager, its own
and the village's rewards mixed by its empathy.

**3. Imitation with correction.** The network plays villages itself; the bot says what it would
have done in each villager's place; the network trains on those situations too.

```bash
PY ML/train_dagger.py --start <imitation experiment> --dataset ML/data/datasets/bots --name dagger-situational
PY ML/train_dagger.py --start <that experiment> --dataset ML/data/datasets/bots \
   --extra ML/data/datasets/<that experiment> --bot-share 0 --name dagger-situational-2      # more rounds
```

The bot advises from the situation: it keeps a villager's plan only while the villager does as
advised (`--remembers` gives the old behaviour, where its advice hung on plans the network had left
and could not see; corrected that way the network hardly improved).

**4. Learning from outcomes.** The network plays many villages and is rewarded only by the
engine. This is where it can become better than the bots it started from.

```bash
PY ML/train_ppo.py --start <experiment> --name ppo-outcomes --updates 600
PY ML/train_ppo.py --start none --name ppo-from-nothing --updates 300 --lr 2.5e-4 --clip 0.2 --entropy 0.01 --target-kl 0.03
```

Villages are played for whole years and kept out of step with each other, so every update holds
every season. The critic is a network of its own and the policy moves in small, bounded steps: the
first attempt, with one shared network and ordinary PPO settings, undid in ten updates what
imitation had taught (see the journal).

**5. Judging.** A network plays whole years on maps nothing was trained on; the bots (and blind
choice) play the same maps. Every village is recorded.

```bash
PY ML/evaluate.py --model ML/experiments/<experiment>/model.pt --name eval-<what> --villages 16 --random
```

**6. Into the game.**

```bash
PY ML/install_model.py --experiment <experiment> --name villager
```

Then in Unity choose "Trained network" on the launch screen. `NeuralBrainTests` checks that the
game gets the same numbers out of the network as the trainer did.

## Villages that grow

Villagers marry and have children (`Simulation.Family.cs`), so a village is not always six. A
recorded run says in every tick's record how many villagers there were, and a dataset has as many
columns as the run came to have. A trainer's villages have a fixed number of places, `--slots`
(12 unless said otherwise): the villagers a village starts with and the children born to them; a
place nobody has been born into yet looks like a dead villager (all zeros, `alive` 0), and no child
is born once every place is taken.

## While something long is running

A training run keeps starting the simulator. To rebuild the simulator or export the workbook
meanwhile, give the run copies of its own first:

```bash
cp -r ML/host/bin/Release/net10.0 ML/host/frozen
cp -r Game/TinyTribe/Assets/StreamingAssets/Data ML/host/frozen-data
export TINYTRIBE_HOST=$(pwd -W)/ML/host/frozen/tinytribe-host.exe TINYTRIBE_DATA=$(pwd -W)/ML/host/frozen-data
```

## Looking into what went wrong

- `H advice --runs <folder>`: what the bot would have advised on every tick of recorded runs against
  what was done, by what the bot was after; and whether the compass needle was on the bot's target.
  `--steps file.csv` writes every advised step with the target's offset.
- `PY ML/evaluate.py ... --temperature 0.5` plays closer to the network's first choice.

## Reading results

- `PY ML/report.py` builds `ML/REPORT.html`: the ladder of every evaluated network against the bots,
  what each achieved, and the journal with every curve. One file, opens anywhere: the thing to show.
- `ML/experiments/index.csv`: one line per experiment. `ML/JOURNAL.md`: the same as a story.
- `PY ML/note.py "title" "text"` adds a note to the journal (what was understood between experiments);
  `PY ML/close_experiment.py <id> --note "..."` records a run that was cut off;
  `PY ML/forget_experiment.py <id>` removes a trial run from the records.
- `PY ML/runs.py ML/data/runs/<folder>`: one line per recorded village; `--run <id>` for one in detail.
- Any run folder has `report.md`: achievements with the day reached, each villager's jobs and what it did.
- `H replay --runs <folder>` plays recorded runs again and checks they come out the same.

## What makes it reproducible

- The simulation is deterministic: a seed and the actions give the same village, tick for tick.
  Training villages, evaluation villages and demonstration villages use different seeds
  (demonstrations 101-148, correction 5000 and up, evaluation 1001 and up, learning 20000 and up).
- Each experiment stores the exact command, its settings, the code version and the workbook's hash.
- One caveat: a run recorded inside Unity does not always replay exactly in the standalone
  simulator, and the other way round (the two runtimes round a few sums differently). Record,
  export and evaluate with the same one. Training uses the standalone simulator throughout.
