# TinyTribe training journal

Every experiment in the order it was run: what was tried, why, with what, and what came of it. Each entry is written by the script that ran the experiment; the numbers and curves are in the folder it names. Notes added by hand are marked as such.

## 2026-10-07 12:51  bc-bots  (imitation)

First imitation run: can the network learn to choose what the scripted survival bots chose? This is the warm start for learning from outcomes, and the week-9 model.

**Data.** `ML/data/datasets/bots`: 48 recorded villages played by Survival; 40 to learn from
(1,534,258 villager-ticks), 8 held out whole (314,152).
**Network.** CNN over the 11 x 11 view (77 one-hot planes), MLP over the 168-value vector, LSTM (128), heads for
21 actions and 21 x 68 arguments; 665,130 parameters.
**Training.** 16 epochs, windows of 128 ticks (16 to warm the LSTM up), 12 villages a batch, AdamW, lr 0.001, plain imitation (cross-entropy over what was possible).

**Result on the held-out villages** (best epoch, 15):

|  | action as recorded | argument as recorded | both |
|---|---|---|---|
| this network | 0.7767 | 0.9214 | 0.7728 |
| always the commonest action (sleep) | 0.2868 |  |  |
| a blind pick among what is possible | 0.1138 |  |  |

By action, most chosen first:

| action | share of choices | chosen as recorded |
|---|---|---|
| sleep | 0.2837 | 0.975 |
| emote_none | 0.1449 | 0.8551 |
| move_west | 0.09972 | 0.5548 |
| move_east | 0.09844 | 0.562 |
| harvest_attack | 0.09315 | 0.9638 |
| move_south | 0.09236 | 0.5957 |
| move_north | 0.09124 | 0.5878 |
| craft_build | 0.06439 | 0.8716 |
| consume | 0.01859 | 0.06763 |
| vote | 0.008259 | 1 |
| deposit | 0.003218 | 0.4209 |
| equip | 0.001141 | 0 |

Curves: `curves.svg`. Agreeing with the recording tick by tick is not the same as playing well: see the evaluation of this network.

Folder: `ML/experiments/20261007-1251-bc-bots/`. Run again with: `ML/train_bc.py --dataset ML/data/datasets/bots --name bc-bots --epochs 16 --note First imitation run: can the network learn to choose what the scripted survival bots chose? This is the warm start for learning from outcomes, and the week-9 model.`

## 2026-10-07 12:54  eval-bc-bots  (evaluation)

Can the imitation network run a village by itself for a year? Agreeing with the bots tick by tick does not say so: its own small mistakes change what it sees next.

**Setup.** The network of `20261007-1251-bc-bots` played 16 villages (seeds 1001 to 1016, none of them used in training)
for up to 28 days each, sampling its choices, choosing only among what the engine allows. The scripted bots played the same seeds.
Each village is recorded in `ML/data/runs/20261007-1254-eval-bc-bots/` with its own report. The network played 14,569 villager-ticks a second.

**Averages over the villages:**

|  | villages | days lived | alive at the end | achievements | buildings | technologies | things crafted | things gathered | crops reaped | villager-days | reward per villager-tick |
|---|---|---|---|---|---|---|---|---|---|---|---|
| this network | 16 | 4.548 | 0 | 3.875 | 0.0625 | 0 | 1.25 | 99.56 | 0 | 16.77 | -0.07124 |
| scripted bots | 16 | 26.94 | 5.125 | 20.31 | 4.062 | 0.5625 | 144.8 | 865.9 | 3.625 | 159.9 | 0.002294 |
| blind choice | 16 | 2.915 | 0 | 2.812 | 0 | 0 | 0 | 8.562 | 0 | 15.05 | -0.08455 |

**Share of villages that reached each achievement:**

| achievement | this network | scripted bots | blind choice |
|---|---|---|---|
| First Night | 100% | 100% | 100% |
| Everyone Has a Job | 100% | 100% | 100% |
| A Full Larder | 0% | 100% | 0% |
| Somewhere to Put Things | 6% | 100% | 0% |
| The Council Changed Its Mind | 100% | 100% | 81% |
| Wolf Down | 0% | 100% | 0% |
| One Season | 6% | 100% | 0% |
| Toolmakers | 75% | 100% | 0% |
| A Full Year | 0% | 88% | 0% |
| First Garden | 0% | 88% | 0% |
| A Fire in the Village | 0% | 88% | 0% |
| Fish Supper | 0% | 88% | 0% |
| First Catch | 0% | 88% | 0% |
| Winter Survived | 0% | 88% | 0% |
| Something to Study | 0% | 88% | 0% |
| A Place to Think | 0% | 88% | 0% |
| Nobody Lost to Winter | 0% | 81% | 0% |
| A Year Without a Death | 0% | 75% | 0% |
| Roast | 0% | 75% | 0% |
| Seeds in the Ground | 0% | 69% | 0% |
| First Harvest | 0% | 56% | 0% |
| Carpentry | 0% | 56% | 0% |
| A Proper Village | 0% | 44% | 0% |
| Planks | 0% | 38% | 0% |
| Sawn Timber | 0% | 38% | 0% |

Folder: `ML/experiments/20261007-1254-eval-bc-bots/`. Run again with: `ML/evaluate.py --model ML/experiments/20261007-1251-bc-bots/model.pt --name eval-bc-bots --villages 16 --days 28 --random --note Can the imitation network run a village by itself for a year? Agreeing with the bots tick by tick does not say so: its own small mistakes change what it sees next.`

## 2026-10-08 12:17  bc-bots-compass  (imitation)

The same imitation run with the compass added to what a villager sees (the way to the nearest water, food, wood, stone, storage, fire and building site). The first network agreed with the bots on 78% of ticks but could not run a village: the bots plan over the whole map and it saw 11 x 11 tiles.

**Data.** `ML/data/datasets/bots`: 48 recorded villages played by Survival; 40 to learn from
(1,534,258 villager-ticks), 8 held out whole (314,152).
**Network.** CNN over the 11 x 11 view (77 one-hot planes), MLP over the 189-value vector, LSTM (128), heads for
21 actions and 21 x 68 arguments; 667,818 parameters.
**Training.** 16 epochs, windows of 128 ticks (16 to warm the LSTM up), 12 villages a batch, AdamW, lr 0.001, plain imitation (cross-entropy over what was possible).

**Result on the held-out villages** (best epoch, 16):

|  | action as recorded | argument as recorded | both |
|---|---|---|---|
| this network | 0.7866 | 0.918 | 0.7826 |
| always the commonest action (sleep) | 0.2868 |  |  |
| a blind pick among what is possible | 0.1138 |  |  |

By action, most chosen first:

| action | share of choices | chosen as recorded |
|---|---|---|
| sleep | 0.2837 | 0.9741 |
| emote_none | 0.1449 | 0.8559 |
| move_west | 0.09972 | 0.5574 |
| move_east | 0.09844 | 0.5808 |
| harvest_attack | 0.09315 | 0.9685 |
| move_south | 0.09236 | 0.6026 |
| move_north | 0.09124 | 0.6367 |
| craft_build | 0.06439 | 0.917 |
| consume | 0.01859 | 0.04589 |
| vote | 0.008259 | 0.9995 |
| deposit | 0.003218 | 0.4047 |
| equip | 0.001141 | 0 |

Curves: `curves.svg`. Agreeing with the recording tick by tick is not the same as playing well: see the evaluation of this network.

Folder: `ML/experiments/20261008-1217-bc-bots-compass/`. Run again with: `ML/train_bc.py --dataset ML/data/datasets/bots --name bc-bots-compass --epochs 16 --note The same imitation run with the compass added to what a villager sees (the way to the nearest water, food, wood, stone, storage, fire and building site). The first network agreed with the bots on 78% of ticks but could not run a village: the bots plan over the whole map and it saw 11 x 11 tiles.`

## 2026-10-08 12:37  eval-bc-bots-compass  (evaluation)

Does the compass let the imitation network run a village? Same test as before: a year on 16 maps it never saw, against the bots and blind choice.

**Setup.** The network of `20261008-1217-bc-bots-compass` played 16 villages (seeds 1001 to 1016, none of them used in training)
for up to 28 days each, sampling its choices, choosing only among what the engine allows. The scripted bots played the same seeds.
Each village is recorded in `ML/data/runs/20261008-1237-eval-bc-bots-compass/` with its own report. The network played 7,702 villager-ticks a second.

**Averages over the villages:**

|  | villages | days lived | alive at the end | achievements | buildings | technologies | things crafted | things gathered | crops reaped | villager-days | reward per villager-tick |
|---|---|---|---|---|---|---|---|---|---|---|---|
| this network | 16 | 6.486 | 0 | 4.625 | 0.3125 | 0 | 2.875 | 153.1 | 0 | 19.78 | -0.05985 |
| scripted bots | 16 | 26.94 | 5.125 | 20.31 | 4.062 | 0.5625 | 144.8 | 865.9 | 3.625 | 159.9 | 0.002294 |
| blind choice | 16 | 2.915 | 0 | 2.812 | 0 | 0 | 0 | 8.562 | 0 | 15.05 | -0.08455 |

**Share of villages that reached each achievement:**

| achievement | this network | scripted bots | blind choice |
|---|---|---|---|
| One Season | 31% | 100% | 0% |
| Somewhere to Put Things | 19% | 100% | 0% |
| First Night | 100% | 100% | 100% |
| Toolmakers | 88% | 100% | 0% |
| A Full Larder | 0% | 100% | 0% |
| Everyone Has a Job | 100% | 100% | 100% |
| Wolf Down | 12% | 100% | 0% |
| The Council Changed Its Mind | 100% | 100% | 81% |
| A Place to Think | 0% | 88% | 0% |
| A Full Year | 0% | 88% | 0% |
| A Fire in the Village | 0% | 88% | 0% |
| First Catch | 0% | 88% | 0% |
| Something to Study | 0% | 88% | 0% |
| First Garden | 12% | 88% | 0% |
| Fish Supper | 0% | 88% | 0% |
| Winter Survived | 0% | 88% | 0% |
| Nobody Lost to Winter | 0% | 81% | 0% |
| A Year Without a Death | 0% | 75% | 0% |
| Roast | 0% | 75% | 0% |
| Seeds in the Ground | 0% | 69% | 0% |
| First Harvest | 0% | 56% | 0% |
| Carpentry | 0% | 56% | 0% |
| A Proper Village | 0% | 44% | 0% |
| Planks | 0% | 38% | 0% |
| Sawn Timber | 0% | 38% | 0% |

Folder: `ML/experiments/20261008-1237-eval-bc-bots-compass/`. Run again with: `ML/evaluate.py --model ML/experiments/20261008-1217-bc-bots-compass/model.pt --name eval-bc-bots-compass --villages 16 --days 28 --random --note Does the compass let the imitation network run a village? Same test as before: a year on 16 maps it never saw, against the bots and blind choice.`


## 2026-10-08 12:38  dagger-bots  (imitation with correction, cut short)

Plain imitation agrees with the bots on 78% of ticks but its villages are gone in under a week: one slip and it is somewhere the bots never were. Here the network plays its own villages (24 new ones a round, up to 10 days each) and the bot says on every tick what it would have done; those situations are added to the training data. Started from 20261008-1217-bc-bots-compass. The session it ran in was interrupted during the sixth round; the network saved after round 5 is kept and the next run carries on from it. What it shows: as soon as the bot stops playing part of the time (rounds 4 and 5), the network alone agrees with the bot on only about 30% of ticks and its villages last 5 to 7 of 10 days, but it is improving from round to round.

What it had logged when it stopped:

| round | bot_share | days_lived | alive_at_end | villages_wiped_out | agreement_while_playing | val_both_acc |
|---|---|---|---|---|---|---|
| 1 | 0.5 | 10 | 5.667 | 0 | 0.5147 | 0.7911 |
| 2 | 0.3 | 10 | 5 | 0 | 0.4589 | 0.7942 |
| 3 | 0.15 | 9.534 | 2 | 4 | 0.3379 | 0.7963 |
| 4 | 0 | 5.344 | 0.04167 | 23 | 0.2696 | 0.8016 |
| 5 | 0 | 6.903 | 0.2917 | 18 | 0.3153 | 0.8062 |

Folder: `ML/experiments/20261008-1238-dagger-bots/`. Run again with: `ML/train_dagger.py --start 20261008-1217-bc-bots-compass --dataset ML/data/datasets/bots --name dagger-bots --rounds 6 --note Plain imitation agrees with the bots on 78% of ticks but its villages are gone in under a week: one slip and it is somewhere the bots never were. Here it plays its own villages and the bot corrects it on the spot.`

## 2026-10-08 14:56  dagger-bots-2  (imitation with correction, cut short)

A continuation of 20261008-1238-dagger-bots with the network playing alone, stopped by hand after its first round. Looking at what the earlier rounds had gathered showed why progress was slow: playing alone, the network was told 'walk that way' on 60% of ticks and got the direction right on 13 to 28% of them, because the bot's advice depended on plans it kept in its own memory (the route it was on, a pack it was in the middle of filling) that the network had left and could not see. The next run changes how the bot advises instead of adding rounds.

What it had logged when it stopped:

| round | bot_share | days_lived | alive_at_end | villages_wiped_out | agreement_while_playing | val_both_acc |
|---|---|---|---|---|---|---|
| 1 | 0 | 5.074 | 0.04167 | 23 | 0.3236 | 0.8005 |

Folder: `ML/experiments/20261008-1456-dagger-bots-2/`. Run again with: `ML/train_dagger.py --start 20261008-1238-dagger-bots --dataset ML/data/datasets/bots --extra ML/data/datasets/20261008-1238-dagger-bots --name dagger-bots-2 --rounds 8 --bot-share 0 --seed 6000 --note Carries on from 20261008-1238-dagger-bots, which was cut off after five rounds: eight more rounds with the network playing entirely by itself and the bot correcting it.`

## 2026-10-08 15:06  bc-balanced  (imitation)

The first networks matched the bots on sleeping and gathering more than 95% of the time but on eating and drinking 7%, and never took a spear in hand: those choices are under 2% of ticks, so the loss hardly noticed them. Here rare actions weigh more.

**Data.** `ML/data/datasets/bots`: 48 recorded villages played by Survival; 40 to learn from
(1,534,258 villager-ticks), 8 held out whole (314,152).
**Network.** CNN over the 11 x 11 view (77 one-hot planes), MLP over the 189-value vector, LSTM (128), heads for
21 actions and 21 x 68 arguments; 667,818 parameters.
**Training.** 16 epochs, windows of 128 ticks (16 to warm the LSTM up), 12 villages a batch, AdamW, lr 0.001, plain imitation (cross-entropy over what was possible), rare actions weighted up (power 0.5).

**Result on the held-out villages** (best epoch, 15):

|  | action as recorded | argument as recorded | both |
|---|---|---|---|
| this network | 0.7882 | 0.9379 | 0.7854 |
| always the commonest action (sleep) | 0.2868 |  |  |
| a blind pick among what is possible | 0.1138 |  |  |

By action, most chosen first:

| action | share of choices | chosen as recorded |
|---|---|---|
| sleep | 0.2837 | 0.9562 |
| emote_none | 0.1449 | 0.8257 |
| move_west | 0.09972 | 0.5667 |
| move_east | 0.09844 | 0.5745 |
| harvest_attack | 0.09315 | 0.9518 |
| move_south | 0.09236 | 0.6424 |
| move_north | 0.09124 | 0.6709 |
| craft_build | 0.06439 | 0.939 |
| consume | 0.01859 | 0.1463 |
| vote | 0.008259 | 1 |
| deposit | 0.003218 | 0.8023 |
| equip | 0.001141 | 0.4328 |

Curves: `curves.svg`. Agreeing with the recording tick by tick is not the same as playing well: see the evaluation of this network.

Folder: `ML/experiments/20261008-1506-bc-balanced/`. Run again with: `ML/train_bc.py --dataset ML/data/datasets/bots --name bc-balanced --epochs 16 --balance 0.5 --note The first networks matched the bots on sleeping and gathering more than 95% of the time but on eating and drinking 7%, and never took a spear in hand: those choices are under 2% of ticks, so the loss hardly noticed them. Here rare actions weigh more.`

## 2026-10-08 15:15  eval-bc-balanced  (evaluation)

Does weighting rare actions up keep the imitation network's villages alive longer? Same 16 maps.

**Setup.** The network of `20261008-1506-bc-balanced` played 16 villages (seeds 1001 to 1016, none of them used in training)
for up to 28 days each, sampling its choices, choosing only among what the engine allows. The scripted bots played the same seeds.
Each village is recorded in `ML/data/runs/20261008-1515-eval-bc-balanced/` with its own report. The network played 9,589 villager-ticks a second.

**Averages over the villages:**

|  | villages | days lived | alive at the end | achievements | buildings | technologies | things crafted | things gathered | crops reaped | villager-days | reward per villager-tick |
|---|---|---|---|---|---|---|---|---|---|---|---|
| this network | 16 | 8.08 | 0 | 4.938 | 0.3125 | 0 | 7.625 | 315.6 | 0 | 23.03 | -0.05462 |
| scripted bots | 16 | 26.94 | 5.125 | 20.31 | 4.062 | 0.5625 | 144.8 | 865.9 | 3.625 | 159.9 | 0.002294 |

**Share of villages that reached each achievement:**

| achievement | this network | scripted bots |
|---|---|---|
| Toolmakers | 94% | 100% |
| Somewhere to Put Things | 25% | 100% |
| First Night | 100% | 100% |
| Wolf Down | 25% | 100% |
| A Full Larder | 0% | 100% |
| One Season | 44% | 100% |
| Everyone Has a Job | 100% | 100% |
| The Council Changed Its Mind | 100% | 100% |
| A Fire in the Village | 0% | 88% |
| Something to Study | 0% | 88% |
| Fish Supper | 0% | 88% |
| First Garden | 0% | 88% |
| Winter Survived | 0% | 88% |
| A Full Year | 0% | 88% |
| First Catch | 6% | 88% |
| A Place to Think | 0% | 88% |
| Nobody Lost to Winter | 0% | 81% |
| Roast | 0% | 75% |
| A Year Without a Death | 0% | 75% |
| Seeds in the Ground | 0% | 69% |
| Carpentry | 0% | 56% |
| First Harvest | 0% | 56% |
| A Proper Village | 0% | 44% |
| Planks | 0% | 38% |
| Sawn Timber | 0% | 38% |

Folder: `ML/experiments/20261008-1515-eval-bc-balanced/`. Run again with: `ML/evaluate.py --model ML/experiments/20261008-1506-bc-balanced/model.pt --name eval-bc-balanced --villages 16 --days 28 --note Does weighting rare actions up keep the imitation network's villages alive longer? Same 16 maps.`

## 2026-10-08 15:16  dagger-situational  (imitation with correction)

Correction again, with the bot advising differently. Before, its advice depended on plans it kept whatever the network did, and the network alone matched it on about 30% of ticks. Now a plan lasts only while the network does as advised; otherwise the advice starts again from the situation.

**Why.** Trained only on villages the bots ran, the network had never seen where its own slips lead. Here it plays, the bot says on every
tick what it would have done in that villager's place, and those situations are added to the training data.
**The bot's advice.** It keeps a villager's plan only while the villager does as advised; when the villager does something else the advice starts again from the situation, so it never hangs on a plan the network left and cannot see.
Rare actions are weighted up in the loss (power 0.5).
**Setup.** Started from `20261008-1506-bc-balanced`. 8 rounds; each plays 24 new villages for up to 2400 ticks
(10 days), then trains 2 passes over the 40 recorded villages plus everything gathered so far
(lr 0.0003). In the first rounds the bot's own choice is played part of the time, so the villages last long enough to be worth learning from.
The gathered data is in `ML/data/datasets/20261008-1516-dagger-situational/`.

| round | bot plays | days lasted | alive at the end | wiped out | same choice as the bot, while playing | on recorded villages (held out) |
|---|---|---|---|---|---|---|
| 1 | 50% | 10 | 5.583 | 0 of 24 | 0.567 | 0.7849 |
| 2 | 30% | 10 | 4.542 | 0 of 24 | 0.477 | 0.7913 |
| 3 | 15% | 9.871 | 3.042 | 1 of 24 | 0.3854 | 0.7951 |
| 4 | 0% | 7.976 | 0.625 | 13 of 24 | 0.3552 | 0.7966 |
| 5 | 0% | 6.159 | 0.08333 | 22 of 24 | 0.3801 | 0.7978 |
| 6 | 0% | 7.556 | 0.2083 | 19 of 24 | 0.4027 | 0.7899 |
| 7 | 0% | 8.465 | 1.292 | 8 of 24 | 0.4266 | 0.7906 |
| 8 | 0% | 9.068 | 1.583 | 5 of 24 | 0.4549 | 0.8009 |

"Days lasted" in the last rounds is the network alone. Whether it can run a village for a whole year is the evaluation that follows.

Folder: `ML/experiments/20261008-1516-dagger-situational/`. Run again with: `ML/train_dagger.py --start 20261008-1506-bc-balanced --dataset ML/data/datasets/bots --name dagger-situational --rounds 8 --epochs 2 --seed 7000 --note Correction again, with the bot advising differently. Before, its advice depended on plans it kept whatever the network did, and the network alone matched it on about 30% of ticks. Now a plan lasts only while the network does as advised; otherwise the advice starts again from the situation.`

## 2026-10-08 15:28  eval-dagger-situational  (evaluation)

The corrected network alone for a year on the same 16 maps, against the bots and blind choice.

**Setup.** The network of `20261008-1516-dagger-situational` played 16 villages (seeds 1001 to 1016, none of them used in training)
for up to 28 days each, sampling its choices, choosing only among what the engine allows. The scripted bots played the same seeds.
Each village is recorded in `ML/data/runs/20261008-1528-eval-dagger-situational/` with its own report. The network played 11,217 villager-ticks a second.

**Averages over the villages:**

|  | villages | days lived | alive at the end | achievements | buildings | technologies | things crafted | things gathered | crops reaped | villager-days | reward per villager-tick |
|---|---|---|---|---|---|---|---|---|---|---|---|
| this network | 16 | 17.27 | 0 | 6.5 | 0.375 | 0 | 7.812 | 579.1 | 0 | 57.81 | -0.02245 |
| scripted bots | 16 | 26.94 | 5.125 | 20.31 | 4.062 | 0.5625 | 144.8 | 865.9 | 3.625 | 159.9 | 0.002294 |
| blind choice | 16 | 2.915 | 0 | 2.812 | 0 | 0 | 0 | 8.562 | 0 | 15.05 | -0.08455 |

**Share of villages that reached each achievement:**

| achievement | this network | scripted bots | blind choice |
|---|---|---|---|
| The Council Changed Its Mind | 100% | 100% | 81% |
| First Night | 100% | 100% | 100% |
| Everyone Has a Job | 100% | 100% | 100% |
| Somewhere to Put Things | 31% | 100% | 0% |
| Wolf Down | 100% | 100% | 0% |
| Toolmakers | 100% | 100% | 0% |
| One Season | 100% | 100% | 0% |
| A Full Larder | 6% | 100% | 0% |
| A Fire in the Village | 0% | 88% | 0% |
| A Place to Think | 0% | 88% | 0% |
| First Garden | 0% | 88% | 0% |
| First Catch | 6% | 88% | 0% |
| A Full Year | 0% | 88% | 0% |
| Something to Study | 0% | 88% | 0% |
| Fish Supper | 0% | 88% | 0% |
| Winter Survived | 0% | 88% | 0% |
| Nobody Lost to Winter | 0% | 81% | 0% |
| A Year Without a Death | 0% | 75% | 0% |
| Roast | 0% | 75% | 0% |
| Seeds in the Ground | 0% | 69% | 0% |
| Carpentry | 0% | 56% | 0% |
| First Harvest | 0% | 56% | 0% |
| A Proper Village | 0% | 44% | 0% |
| Planks | 0% | 38% | 0% |
| Sawn Timber | 0% | 38% | 0% |
| Bear Down | 6% | 0% | 0% |

Folder: `ML/experiments/20261008-1528-eval-dagger-situational/`. Run again with: `ML/evaluate.py --model ML/experiments/20261008-1516-dagger-situational/model.pt --name eval-dagger-situational --villages 16 --days 28 --random --note The corrected network alone for a year on the same 16 maps, against the bots and blind choice.`

## 2026-10-08 15:29  ppo-from-dagger  (learning from outcomes)

The first run of stage 2: the corrected imitation network plays villages and is paid only for outcomes (shaping off). Does what the engine pays make it better at keeping a village alive than imitation left it?

**Setup.** Started from `20261008-1516-dagger-situational` (the value head alone for the first 10 updates; the policy held to its starting self with weight 1.0 fading to 0 over 100 updates). 32 villages of 6 at once, each played for 7 days and then replaced by a
new map; 300 updates of 128 ticks = 7,372,800 villager-ticks over 1213 villages.
Reward: Sim Rewards with shaping as the workbook has it (0: outcomes only), a villager's own and the village's parts mixed by its empathy. PPO: gamma 0.999, lambda 0.95,
clip 0.2, 3 epochs x 4 minibatches, lr 0.0001, entropy 0.01.

**From the start of training to the end** (averages over the villages that ended in those updates):

|  | reward_per_tick | days_lived | alive_at_end | achievements | buildings | technologies | gathered | crafted |
|---|---|---|---|---|---|---|---|---|
| first tenth of training | -0.0367 | 4.271 | 0.2091 | 3.854 | 0 | 0 | 73.83 | 1.41 |
| last tenth of training | -0.03842 | 4.403 | 0.07167 | 3.149 | 0 | 0 | 145.2 | 0.0075 |

Curves: `curves.svg`. These are villages of 7 days played while learning, with the policy picking by chance in proportion to its scores;
the evaluation that follows plays whole years on maps not used here and compares with the bots.

Folder: `ML/experiments/20261008-1529-ppo-from-dagger/`. Run again with: `ML/train_ppo.py --start 20261008-1516-dagger-situational --name ppo-from-dagger --updates 300 --envs 32 --steps 128 --days 7 --note The first run of stage 2: the corrected imitation network plays villages and is paid only for outcomes (shaping off). Does what the engine pays make it better at keeping a village alive than imitation left it?`

## 2026-10-08 15:48  eval-ppo-from-dagger  (evaluation)

The network after learning from outcomes, a year on the same 16 maps.

**Setup.** The network of `20261008-1529-ppo-from-dagger` played 16 villages (seeds 1001 to 1016, none of them used in training)
for up to 28 days each, sampling its choices, choosing only among what the engine allows. The scripted bots played the same seeds.
Each village is recorded in `ML/data/runs/20261008-1548-eval-ppo-from-dagger/` with its own report. The network played 16,377 villager-ticks a second.

**Averages over the villages:**

|  | villages | days lived | alive at the end | achievements | buildings | technologies | things crafted | things gathered | crops reaped | villager-days | reward per villager-tick |
|---|---|---|---|---|---|---|---|---|---|---|---|
| this network | 16 | 4.617 | 0 | 3.25 | 0 | 0 | 0 | 170.7 | 0 | 16.68 | -0.06327 |
| scripted bots | 16 | 26.94 | 5.125 | 20.31 | 4.062 | 0.5625 | 144.8 | 865.9 | 3.625 | 159.9 | 0.002294 |

**Share of villages that reached each achievement:**

| achievement | this network | scripted bots |
|---|---|---|
| Wolf Down | 12% | 100% |
| A Full Larder | 0% | 100% |
| Somewhere to Put Things | 0% | 100% |
| First Night | 100% | 100% |
| Everyone Has a Job | 100% | 100% |
| Toolmakers | 0% | 100% |
| One Season | 12% | 100% |
| The Council Changed Its Mind | 100% | 100% |
| First Catch | 0% | 88% |
| First Garden | 0% | 88% |
| A Fire in the Village | 0% | 88% |
| A Place to Think | 0% | 88% |
| A Full Year | 0% | 88% |
| Something to Study | 0% | 88% |
| Fish Supper | 0% | 88% |
| Winter Survived | 0% | 88% |
| Nobody Lost to Winter | 0% | 81% |
| Roast | 0% | 75% |
| A Year Without a Death | 0% | 75% |
| Seeds in the Ground | 0% | 69% |
| Carpentry | 0% | 56% |
| First Harvest | 0% | 56% |
| A Proper Village | 0% | 44% |
| Planks | 0% | 38% |
| Sawn Timber | 0% | 38% |

Folder: `ML/experiments/20261008-1548-eval-ppo-from-dagger/`. Run again with: `ML/evaluate.py --model ML/experiments/20261008-1529-ppo-from-dagger/model.pt --name eval-ppo-from-dagger --villages 16 --days 28 --note The network after learning from outcomes, a year on the same 16 maps.`


## 2026-10-08 19:05  note: why the first run of learning from outcomes made the network worse

The network that could keep a village alive for 17 days came out of 300 updates of PPO lasting 4.6. Its numbers show two mistakes in the method, not in the idea. First: for the opening ten updates only the value estimate was meant to learn, but policy and value shared one network, so the value error ran back through the whole of it. By update 11, before the policy had been trained at all, it already differed from its starting self by 1.4 nats: what imitation had taught was overwritten by a critic that knew nothing yet. Second: the first real policy update then moved the policy by a KL of 0.15 with 47% of its choices outside the clipping range, many times what PPO is meant to allow. The trainer was rewritten: the critic is a network of its own that reads what the policy's LSTM holds but sends nothing back into it; the policy learns at a third of the rate, an update stops once the policy has moved a KL of 0.015, the clip is 0.1 instead of 0.2 and the entropy bonus is a third of what it was. In a trial of the new trainer the policy did not move at all during the critic's warm-up and by a KL of about 0.0005 per update after it.


## 2026-10-08 19:03  dagger-situational-2  (imitation with correction)

Eight rounds of correction took the network from 8 days to 17, and it was still getting better each round (the share of ticks on which it did what the bot advised rose from 36% to 46%). Twelve more rounds, the network playing alone throughout.

**Why.** Trained only on villages the bots ran, the network had never seen where its own slips lead. Here it plays, the bot says on every
tick what it would have done in that villager's place, and those situations are added to the training data.
**The bot's advice.** It keeps a villager's plan only while the villager does as advised; when the villager does something else the advice starts again from the situation, so it never hangs on a plan the network left and cannot see.
Rare actions are weighted up in the loss (power 0.5).
**Setup.** Started from `20261008-1516-dagger-situational`. 12 rounds; each plays 24 new villages for up to 2400 ticks
(10 days), then trains 2 passes over the 40 recorded villages plus everything gathered so far
(lr 0.0003). In the first rounds the bot's own choice is played part of the time, so the villages last long enough to be worth learning from.
The gathered data is in `ML/data/datasets/20261008-1903-dagger-situational-2/`. It also trained on what earlier correction runs gathered: ML/data/datasets/20261008-1516-dagger-situational.

| round | bot plays | days lasted | alive at the end | wiped out | same choice as the bot, while playing | on recorded villages (held out) |
|---|---|---|---|---|---|---|
| 1 | 0% | 9.712 | 2.292 | 3 of 24 | 0.4793 | 0.7986 |
| 2 | 0% | 9.566 | 2.292 | 5 of 24 | 0.5023 | 0.7924 |
| 3 | 0% | 9.882 | 2.583 | 1 of 24 | 0.5148 | 0.7927 |
| 4 | 0% | 9.44 | 2.625 | 3 of 24 | 0.5194 | 0.7909 |
| 5 | 0% | 10 | 3.375 | 0 of 24 | 0.5544 | 0.7949 |
| 6 | 0% | 10 | 3.417 | 0 of 24 | 0.5529 | 0.7867 |
| 7 | 0% | 9.711 | 3.875 | 1 of 24 | 0.5613 | 0.79 |
| 8 | 0% | 10 | 3.917 | 0 of 24 | 0.5608 | 0.7954 |
| 9 | 0% | 10 | 4.417 | 0 of 24 | 0.569 | 0.8029 |
| 10 | 0% | 10 | 4.25 | 0 of 24 | 0.5574 | 0.8066 |
| 11 | 0% | 10 | 4.083 | 0 of 24 | 0.583 | 0.8001 |
| 12 | 0% | 10 | 4.625 | 0 of 24 | 0.5768 | 0.7932 |

"Days lasted" in the last rounds is the network alone. Whether it can run a village for a whole year is the evaluation that follows.

Folder: `ML/experiments/20261008-1903-dagger-situational-2/`. Run again with: `ML/train_dagger.py --start 20261008-1516-dagger-situational --dataset ML/data/datasets/bots --extra ML/data/datasets/20261008-1516-dagger-situational --name dagger-situational-2 --rounds 12 --bot-share 0 --epochs 2 --seed 8000 --note Eight rounds of correction took the network from 8 days to 17, and it was still getting better each round (the share of ticks on which it did what the bot advised rose from 36% to 46%). Twelve more rounds, the network playing alone throughout.`

## 2026-10-08 19:25  eval-dagger-situational-2  (evaluation)

After twenty rounds of correction in all: a year on the same 16 maps.

**Setup.** The network of `20261008-1903-dagger-situational-2` played 16 villages (seeds 1001 to 1016, none of them used in training)
for up to 28 days each, sampling its choices, choosing only among what the engine allows. The scripted bots played the same seeds.
Each village is recorded in `ML/data/runs/20261008-1925-eval-dagger-situational-2/` with its own report. The network played 15,809 villager-ticks a second.

**Averages over the villages:**

|  | villages | days lived | alive at the end | achievements | buildings | technologies | things crafted | things gathered | crops reaped | villager-days | reward per villager-tick |
|---|---|---|---|---|---|---|---|---|---|---|---|
| this network | 16 | 18.77 | 0 | 6 | 0 | 0 | 6.375 | 988.7 | 0 | 88.13 | -0.01631 |
| scripted bots | 16 | 26.94 | 5.125 | 20.31 | 4.062 | 0.5625 | 144.8 | 865.9 | 3.625 | 159.9 | 0.002294 |

**Share of villages that reached each achievement:**

| achievement | this network | scripted bots |
|---|---|---|
| A Full Larder | 0% | 100% |
| First Night | 100% | 100% |
| Toolmakers | 100% | 100% |
| The Council Changed Its Mind | 100% | 100% |
| Wolf Down | 100% | 100% |
| Everyone Has a Job | 100% | 100% |
| Somewhere to Put Things | 0% | 100% |
| One Season | 100% | 100% |
| Winter Survived | 0% | 88% |
| A Fire in the Village | 0% | 88% |
| A Place to Think | 0% | 88% |
| Fish Supper | 0% | 88% |
| First Catch | 0% | 88% |
| First Garden | 0% | 88% |
| A Full Year | 0% | 88% |
| Something to Study | 0% | 88% |
| Nobody Lost to Winter | 0% | 81% |
| A Year Without a Death | 0% | 75% |
| Roast | 0% | 75% |
| Seeds in the Ground | 0% | 69% |
| Carpentry | 0% | 56% |
| First Harvest | 0% | 56% |
| A Proper Village | 0% | 44% |
| Sawn Timber | 0% | 38% |
| Planks | 0% | 38% |

Folder: `ML/experiments/20261008-1925-eval-dagger-situational-2/`. Run again with: `ML/evaluate.py --model ML/experiments/20261008-1903-dagger-situational-2/model.pt --name eval-dagger-situational-2 --villages 16 --days 28 --note "After twenty rounds of correction in all: a year on the same 16 maps."`

## 2026-10-08 19:27  dagger-years  (imitation with correction)

The corrected network gets through its first ten days nearly every time, then loses the village in autumn: of 96 villagers in the year-long evaluation 66 froze and 24 starved, half of them by day 18. Every round of correction so far played ten days of spring, so it was never corrected in the cold. These rounds play whole years.

**Why.** Trained only on villages the bots ran, the network had never seen where its own slips lead. Here it plays, the bot says on every
tick what it would have done in that villager's place, and those situations are added to the training data.
**The bot's advice.** It keeps a villager's plan only while the villager does as advised; when the villager does something else the advice starts again from the situation, so it never hangs on a plan the network left and cannot see.
Rare actions are weighted up in the loss (power 0.5).
**Setup.** Started from `20261008-1903-dagger-situational-2`. 8 rounds; each plays 8 new villages for up to 6720 ticks
(28 days), then trains 1 passes over the 40 recorded villages plus everything gathered so far
(lr 0.0003). In the first rounds the bot's own choice is played part of the time, so the villages last long enough to be worth learning from.
The gathered data is in `ML/data/datasets/20261008-1927-dagger-years/`. It also trained on what earlier correction runs gathered: ML/data/datasets/20261008-1516-dagger-situational,ML/data/datasets/20261008-1903-dagger-situational-2.

| round | bot plays | days lasted | alive at the end | wiped out | same choice as the bot, while playing | on recorded villages (held out) |
|---|---|---|---|---|---|---|
| 1 | 0% | 18.75 | 0 | 8 of 8 | 0.5754 | 0.7921 |
| 2 | 0% | 18.64 | 0 | 8 of 8 | 0.6025 | 0.8014 |
| 3 | 0% | 18.81 | 0 | 8 of 8 | 0.5813 | 0.7871 |
| 4 | 0% | 19.01 | 0 | 8 of 8 | 0.5917 | 0.8018 |
| 5 | 0% | 18.56 | 0 | 8 of 8 | 0.5764 | 0.7971 |
| 6 | 0% | 18.54 | 0 | 8 of 8 | 0.5812 | 0.8083 |
| 7 | 0% | 18.8 | 0 | 8 of 8 | 0.5756 | 0.788 |
| 8 | 0% | 18.86 | 0 | 8 of 8 | 0.6053 | 0.7981 |

"Days lasted" in the last rounds is the network alone. Whether it can run a village for a whole year is the evaluation that follows.

Folder: `ML/experiments/20261008-1927-dagger-years/`. Run again with: `ML/train_dagger.py --start 20261008-1903-dagger-situational-2 --dataset ML/data/datasets/bots --extra ML/data/datasets/20261008-1516-dagger-situational,ML/data/datasets/20261008-1903-dagger-situational-2 --name dagger-years --rounds 8 --villages 8 --horizon 6720 --bot-share 0 --epochs 1 --seed 9000 --note "The corrected network gets through its first ten days nearly every time, then loses the village in autumn: of 96 villagers in the year-long evaluation 66 froze and 24 starved, half of them by day 18. Every round of correction so far played ten days of spring, so it was never corrected in the cold. These rounds play whole years."`

## 2026-10-08 19:38  eval-dagger-years  (evaluation)

After correction over whole years: a year on the same 16 maps.

**Setup.** The network of `20261008-1927-dagger-years` played 16 villages (seeds 1001 to 1016, none of them used in training)
for up to 28 days each, sampling its choices, choosing only among what the engine allows. The scripted bots played the same seeds.
Each village is recorded in `ML/data/runs/20261008-1938-eval-dagger-years/` with its own report. The network played 15,501 villager-ticks a second.

**Averages over the villages:**

|  | villages | days lived | alive at the end | achievements | buildings | technologies | things crafted | things gathered | crops reaped | villager-days | reward per villager-tick |
|---|---|---|---|---|---|---|---|---|---|---|---|
| this network | 16 | 18.93 | 0 | 6 | 0 | 0 | 6.188 | 892.2 | 0 | 88.72 | -0.01657 |
| scripted bots | 16 | 26.94 | 5.125 | 20.31 | 4.062 | 0.5625 | 144.8 | 865.9 | 3.625 | 159.9 | 0.002294 |

**Share of villages that reached each achievement:**

| achievement | this network | scripted bots |
|---|---|---|
| Somewhere to Put Things | 0% | 100% |
| Everyone Has a Job | 100% | 100% |
| The Council Changed Its Mind | 100% | 100% |
| Wolf Down | 100% | 100% |
| One Season | 100% | 100% |
| First Night | 100% | 100% |
| Toolmakers | 100% | 100% |
| A Full Larder | 0% | 100% |
| Fish Supper | 0% | 88% |
| A Full Year | 0% | 88% |
| First Catch | 0% | 88% |
| A Fire in the Village | 0% | 88% |
| Winter Survived | 0% | 88% |
| First Garden | 0% | 88% |
| A Place to Think | 0% | 88% |
| Something to Study | 0% | 88% |
| Nobody Lost to Winter | 0% | 81% |
| A Year Without a Death | 0% | 75% |
| Roast | 0% | 75% |
| Seeds in the Ground | 0% | 69% |
| First Harvest | 0% | 56% |
| Carpentry | 0% | 56% |
| A Proper Village | 0% | 44% |
| Planks | 0% | 38% |
| Sawn Timber | 0% | 38% |

Folder: `ML/experiments/20261008-1938-eval-dagger-years/`. Run again with: `ML/evaluate.py --model ML/experiments/20261008-1927-dagger-years/model.pt --name eval-dagger-years --villages 16 --days 28 --note "After correction over whole years: a year on the same 16 maps."`

## 2026-10-08 19:39  ppo-outcomes  (learning from outcomes)

Stage 2 again, with the trainer rewritten after the first attempt (see the note on why that one failed). The corrected imitation network plays whole years, 32 villages out of step with each other, and is paid only for outcomes.

**Setup.** Started from `20261008-1927-dagger-years` (only the critic for the first 30 updates; then the policy held to its starting self with weight 1.0 fading to 0 over 150 updates). 32 villages of 6 at once, each played for 28 days and then replaced by a
new map, the villages out of step so that every update holds every season; 600 updates of 128 ticks = 14,745,600 villager-ticks over 515 villages.
Reward: Sim Rewards with shaping as the workbook has it (0: outcomes only), a villager's own and the village's parts mixed by its empathy. PPO: gamma 0.999, lambda 0.95,
clip 0.1, up to 3 epochs x 4 minibatches stopping at KL 0.015, policy lr 3e-05, entropy 0.003;
a critic of its own (lr 0.001) whose errors do not reach the policy.

**Before and after** (averages over the updates named; villages that ended in them):

|  | reward_per_tick | alive_share | days_lived | alive_at_end | achievements | buildings | technologies | gathered | crafted |
|---|---|---|---|---|---|---|---|---|---|
| before the policy changed (critic warm-up) | -0.004732 | 0.855 | nan | nan | nan | nan | nan | nan | nan |
| last tenth of training | -0.009333 | 0.8082 | 18.9 | 0 | 6.015 | 0.01053 | 0 | 679.4 | 6.445 |

"alive_share" is the share of villagers alive over all the ticks played in an update. Curves: `curves.svg`. These are villages of 28 days played
while learning, the policy picking by chance in proportion to its scores; the evaluation that follows plays whole years on maps not used here.

Folder: `ML/experiments/20261008-1939-ppo-outcomes/`. Run again with: `ML/train_ppo.py --start 20261008-1927-dagger-years --name ppo-outcomes --updates 600 --envs 32 --steps 128 --days 28 --note "Stage 2 again, with the trainer rewritten after the first attempt (see the note on why that one failed). The corrected imitation network plays whole years, 32 villages out of step with each other, and is paid only for outcomes."`

## 2026-10-08 20:56  eval-ppo-outcomes  (evaluation)

The network after learning from outcomes: a year on the same 16 maps as every other evaluation.

**Setup.** The network of `20261008-1939-ppo-outcomes` played 16 villages (seeds 1001 to 1016, none of them used in training)
for up to 28 days each, sampling its choices, choosing only among what the engine allows. The scripted bots played the same seeds.
Each village is recorded in `ML/data/runs/20261008-2056-eval-ppo-outcomes/` with its own report. The network played 10,226 villager-ticks a second.

**Averages over the villages:**

|  | villages | days lived | alive at the end | achievements | buildings | technologies | things crafted | things gathered | crops reaped | villager-days | reward per villager-tick |
|---|---|---|---|---|---|---|---|---|---|---|---|
| this network | 16 | 18.75 | 0 | 6 | 0 | 0 | 7.812 | 700.9 | 0 | 93.09 | -0.01494 |
| scripted bots | 16 | 26.94 | 5.125 | 20.31 | 4.062 | 0.5625 | 144.8 | 865.9 | 3.625 | 159.9 | 0.002294 |

**Share of villages that reached each achievement:**

| achievement | this network | scripted bots |
|---|---|---|
| The Council Changed Its Mind | 100% | 100% |
| A Full Larder | 0% | 100% |
| Toolmakers | 100% | 100% |
| First Night | 100% | 100% |
| One Season | 100% | 100% |
| Everyone Has a Job | 100% | 100% |
| Somewhere to Put Things | 0% | 100% |
| Wolf Down | 100% | 100% |
| A Fire in the Village | 0% | 88% |
| First Catch | 0% | 88% |
| Something to Study | 0% | 88% |
| A Place to Think | 0% | 88% |
| Fish Supper | 0% | 88% |
| First Garden | 0% | 88% |
| Winter Survived | 0% | 88% |
| A Full Year | 0% | 88% |
| Nobody Lost to Winter | 0% | 81% |
| Roast | 0% | 75% |
| A Year Without a Death | 0% | 75% |
| Seeds in the Ground | 0% | 69% |
| First Harvest | 0% | 56% |
| Carpentry | 0% | 56% |
| A Proper Village | 0% | 44% |
| Planks | 0% | 38% |
| Sawn Timber | 0% | 38% |

Folder: `ML/experiments/20261008-2056-eval-ppo-outcomes/`. Run again with: `ML/evaluate.py --model ML/experiments/20261008-1939-ppo-outcomes/model.pt --name eval-ppo-outcomes --villages 16 --days 28 --note "The network after learning from outcomes: a year on the same 16 maps as every other evaluation."`

## 2026-10-08 20:57  ppo-from-nothing  (learning from outcomes)

The comparison: the same learning from outcomes with no demonstrations at all, from random weights. How far does what the engine pays get a network by itself?

**Setup.** Started from `none` (nothing to start from: random weights). 32 villages of 6 at once, each played for 28 days and then replaced by a
new map, the villages out of step so that every update holds every season; 300 updates of 128 ticks = 7,372,800 villager-ticks over 1552 villages.
Reward: Sim Rewards with shaping as the workbook has it (0: outcomes only), a villager's own and the village's parts mixed by its empathy. PPO: gamma 0.999, lambda 0.95,
clip 0.2, up to 3 epochs x 4 minibatches stopping at KL 0.03, policy lr 0.00025, entropy 0.01;
a critic of its own (lr 0.001) whose errors do not reach the policy.

**Before and after** (averages over the updates named; villages that ended in them):

|  | reward_per_tick | alive_share | days_lived | alive_at_end | achievements | buildings | technologies | gathered | crafted |
|---|---|---|---|---|---|---|---|---|---|
| first tenth of training | -0.05155 | 0.7977 | 2.982 | 0 | 2.948 | 0 | 0 | 14.75 | 0 |
| last tenth of training | -0.03817 | 0.8204 | 3.429 | 0 | 2.844 | 0 | 0 | 89.62 | 0 |

"alive_share" is the share of villagers alive over all the ticks played in an update. Curves: `curves.svg`. These are villages of 28 days played
while learning, the policy picking by chance in proportion to its scores; the evaluation that follows plays whole years on maps not used here.

Folder: `ML/experiments/20261008-2057-ppo-from-nothing/`. Run again with: `ML/train_ppo.py --start none --name ppo-from-nothing --updates 300 --envs 32 --steps 128 --days 28 --lr 2.5e-4 --clip 0.2 --entropy 0.01 --target-kl 0.03 --note "The comparison: the same learning from outcomes with no demonstrations at all, from random weights. How far does what the engine pays get a network by itself?"`

## 2026-10-08 21:22  eval-ppo-from-nothing  (evaluation)

The network that learned from outcomes alone, from nothing: a year on the same 16 maps.

**Setup.** The network of `20261008-2057-ppo-from-nothing` played 16 villages (seeds 1001 to 1016, none of them used in training)
for up to 28 days each, sampling its choices, choosing only among what the engine allows. The scripted bots played the same seeds.
Each village is recorded in `ML/data/runs/20261008-2122-eval-ppo-from-nothing/` with its own report. The network played 7,518 villager-ticks a second.

**Averages over the villages:**

|  | villages | days lived | alive at the end | achievements | buildings | technologies | things crafted | things gathered | crops reaped | villager-days | reward per villager-tick |
|---|---|---|---|---|---|---|---|---|---|---|---|
| this network | 16 | 3.516 | 0 | 3.125 | 0 | 0 | 0 | 88.62 | 0 | 16.01 | -0.07099 |
| scripted bots | 16 | 26.94 | 5.125 | 20.31 | 4.062 | 0.5625 | 144.8 | 865.9 | 3.625 | 159.9 | 0.002294 |

**Share of villages that reached each achievement:**

| achievement | this network | scripted bots |
|---|---|---|
| Everyone Has a Job | 100% | 100% |
| Wolf Down | 12% | 100% |
| Toolmakers | 0% | 100% |
| One Season | 0% | 100% |
| First Night | 100% | 100% |
| Somewhere to Put Things | 0% | 100% |
| The Council Changed Its Mind | 100% | 100% |
| A Full Larder | 0% | 100% |
| A Fire in the Village | 0% | 88% |
| A Full Year | 0% | 88% |
| First Catch | 0% | 88% |
| Winter Survived | 0% | 88% |
| Fish Supper | 0% | 88% |
| First Garden | 0% | 88% |
| Something to Study | 0% | 88% |
| A Place to Think | 0% | 88% |
| Nobody Lost to Winter | 0% | 81% |
| Roast | 0% | 75% |
| A Year Without a Death | 0% | 75% |
| Seeds in the Ground | 0% | 69% |
| Carpentry | 0% | 56% |
| First Harvest | 0% | 56% |
| A Proper Village | 0% | 44% |
| Sawn Timber | 0% | 38% |
| Planks | 0% | 38% |

Folder: `ML/experiments/20261008-2122-eval-ppo-from-nothing/`. Run again with: `ML/evaluate.py --model ML/experiments/20261008-2057-ppo-from-nothing/model.pt --name eval-ppo-from-nothing --villages 16 --days 28 --note "The network that learned from outcomes alone, from nothing: a year on the same 16 maps."`

## 2026-10-08 21:23  eval-dagger-years-greedy  (evaluation)

The same network as eval-dagger-years, but always taking its highest-scoring choice instead of picking by chance in proportion to the scores. Picking by chance, it made 431 put-down moves per village that the bot had not asked for and threw away what it had gathered.

**Setup.** The network of `20261008-1927-dagger-years` played 16 villages (seeds 1001 to 1016, none of them used in training)
for up to 28 days each, taking its highest-scoring choice every time, choosing only among what the engine allows. The scripted bots played the same seeds.
Each village is recorded in `ML/data/runs/20261008-2123-eval-dagger-years-greedy/` with its own report. The network played 11,044 villager-ticks a second.

**Averages over the villages:**

|  | villages | days lived | alive at the end | achievements | buildings | technologies | things crafted | things gathered | crops reaped | villager-days | reward per villager-tick |
|---|---|---|---|---|---|---|---|---|---|---|---|
| this network | 16 | 14.67 | 0 | 5.625 | 0 | 0 | 4.625 | 296.6 | 0 | 46.71 | -0.03451 |
| scripted bots | 16 | 26.94 | 5.125 | 20.31 | 4.062 | 0.5625 | 144.8 | 865.9 | 3.625 | 159.9 | 0.002294 |

**Share of villages that reached each achievement:**

| achievement | this network | scripted bots |
|---|---|---|
| Wolf Down | 94% | 100% |
| Everyone Has a Job | 100% | 100% |
| A Full Larder | 0% | 100% |
| The Council Changed Its Mind | 94% | 100% |
| One Season | 81% | 100% |
| Somewhere to Put Things | 0% | 100% |
| First Night | 100% | 100% |
| Toolmakers | 94% | 100% |
| Fish Supper | 0% | 88% |
| A Fire in the Village | 0% | 88% |
| A Place to Think | 0% | 88% |
| Winter Survived | 0% | 88% |
| First Catch | 0% | 88% |
| Something to Study | 0% | 88% |
| First Garden | 0% | 88% |
| A Full Year | 0% | 88% |
| Nobody Lost to Winter | 0% | 81% |
| A Year Without a Death | 0% | 75% |
| Roast | 0% | 75% |
| Seeds in the Ground | 0% | 69% |
| First Harvest | 0% | 56% |
| Carpentry | 0% | 56% |
| A Proper Village | 0% | 44% |
| Planks | 0% | 38% |
| Sawn Timber | 0% | 38% |

Folder: `ML/experiments/20261008-2123-eval-dagger-years-greedy/`. Run again with: `ML/evaluate.py --model ML/experiments/20261008-1927-dagger-years/model.pt --name eval-dagger-years-greedy --villages 16 --days 28 --greedy --note "The same network as eval-dagger-years, but always taking its highest-scoring choice instead of picking by chance in proportion to the scores. Picking by chance, it made 431 put-down moves per village that the bot had not asked for and threw away what it had gathered."`

## 2026-10-11 22:44  bc-reads-items  (imitation)

Every network so far walked toward wood no better than a coin toss (a step toward the tree the bot meant on 50% of moves) and never built anything. In the vector an item is one number, its id over 57, and so is a job: a wooden spear is 0.895 and an iron spear 0.912. Here the network first turns those numbers back into what they stand for (how much of each item it carries, what is in its hand, which job it has). Everything else is as in bc-balanced.

**Data.** `ML/data/datasets/bots`: 48 recorded villages played by Survival; 40 to learn from
(1,534,258 villager-ticks), 8 held out whole (314,152).
**Network.** CNN over the 11 x 11 view (77 one-hot planes), MLP over the 189-value vector, LSTM (128), heads for
21 actions and 21 x 68 arguments; 693,418 parameters.
**Training.** 16 epochs, windows of 128 ticks (16 to warm the LSTM up), 12 villages a batch, AdamW, lr 0.001, plain imitation (cross-entropy over what was possible), rare actions weighted up (power 0.5).

**Result on the held-out villages** (best epoch, 16):

|  | action as recorded | argument as recorded | both |
|---|---|---|---|
| this network | 0.7877 | 0.9548 | 0.7866 |
| always the commonest action (sleep) | 0.2868 |  |  |
| a blind pick among what is possible | 0.1138 |  |  |

By action, most chosen first:

| action | share of choices | chosen as recorded |
|---|---|---|
| sleep | 0.2837 | 0.9569 |
| emote_none | 0.1449 | 0.8275 |
| move_west | 0.09972 | 0.5551 |
| move_east | 0.09844 | 0.5861 |
| harvest_attack | 0.09315 | 0.9645 |
| move_south | 0.09236 | 0.6419 |
| move_north | 0.09124 | 0.6383 |
| craft_build | 0.06439 | 0.9526 |
| consume | 0.01859 | 0.1178 |
| vote | 0.008259 | 1 |
| deposit | 0.003218 | 0.8244 |
| equip | 0.001141 | 0.6918 |

Curves: `curves.svg`. Agreeing with the recording tick by tick is not the same as playing well: see the evaluation of this network.

Folder: `ML/experiments/20261011-2244-bc-reads-items/`. Run again with: `ML/train_bc.py --dataset ML/data/datasets/bots --name bc-reads-items --epochs 16 --balance 0.5 --note "Every network so far walked toward wood no better than a coin toss (a step toward the tree the bot meant on 50% of moves) and never built anything. In the vector an item is one number, its id over 57, and so is a job: a wooden spear is 0.895 and an iron spear 0.912. Here the network first turns those numbers back into what they stand for (how much of each item it carries, what is in its hand, which job it has). Everything else is as in bc-balanced."`

## 2026-10-11 22:50  bc-reads-items-corrected  (imitation)

Does reading items and jobs matter where the earlier networks failed? In the bots' own villages a walk to wood is under 1% of ticks, so bc-reads-items could not show it. In villages the networks ran it is 12 to 15% of what the bot advised its builder, and they followed it on 35% of those ticks. This network reads items and jobs and learns from the same situations those networks were corrected on, without playing itself: the recorded bots and the three correction runs.

**Data.** `ML/data/datasets/bots`: 48 recorded villages played by Survival; 40 to learn from
(7,792,224 villager-ticks), 8 held out whole (314,152).
It also learned from 544 villages that earlier networks played with the bot advising on every tick: ML/data/datasets/20261008-1516-dagger-situational,ML/data/datasets/20261008-1903-dagger-situational-2,ML/data/datasets/20261008-1927-dagger-years. **Network.** CNN over the 11 x 11 view (77 one-hot planes), MLP over the 189-value vector, LSTM (128), heads for
21 actions and 21 x 68 arguments; 693,418 parameters.
**Training.** 8 epochs, windows of 128 ticks (16 to warm the LSTM up), 12 villages a batch, AdamW, lr 0.001, plain imitation (cross-entropy over what was possible), rare actions weighted up (power 0.5).

**Result on the held-out villages** (best epoch, 8):

|  | action as recorded | argument as recorded | both |
|---|---|---|---|
| this network | 0.746 | 0.9642 | 0.7442 |
| always the commonest action (sleep) | 0.2868 |  |  |
| a blind pick among what is possible | 0.1138 |  |  |

By action, most chosen first:

| action | share of choices | chosen as recorded |
|---|---|---|
| sleep | 0.2837 | 0.939 |
| emote_none | 0.1449 | 0.8927 |
| move_west | 0.09972 | 0.3793 |
| move_east | 0.09844 | 0.3341 |
| harvest_attack | 0.09315 | 0.947 |
| move_south | 0.09236 | 0.5702 |
| move_north | 0.09124 | 0.6449 |
| craft_build | 0.06439 | 0.96 |
| consume | 0.01859 | 0.3146 |
| vote | 0.008259 | 1 |
| deposit | 0.003218 | 0.7791 |
| equip | 0.001141 | 0.6885 |

Curves: `curves.svg`. Agreeing with the recording tick by tick is not the same as playing well: see the evaluation of this network.

Folder: `ML/experiments/20261011-2250-bc-reads-items-corrected/`. Run again with: `ML/train_bc.py --dataset ML/data/datasets/bots --extra ML/data/datasets/20261008-1516-dagger-situational,ML/data/datasets/20261008-1903-dagger-situational-2,ML/data/datasets/20261008-1927-dagger-years --name bc-reads-items-corrected --epochs 8 --balance 0.5 --note "Does reading items and jobs matter where the earlier networks failed? In the bots' own villages a walk to wood is under 1% of ticks, so bc-reads-items could not show it. In villages the networks ran it is 12 to 15% of what the bot advised its builder, and they followed it on 35% of those ticks. This network reads items and jobs and learns from the same situations those networks were corrected on, without playing itself: the recorded bots and the three correction runs."`

## 2026-10-11 22:59  eval-bc-reads-items-corrected  (evaluation)

The network that reads items and jobs, taught on the situations earlier networks were corrected on: a year on the same 16 maps.

**Setup.** The network of `20261011-2250-bc-reads-items-corrected` played 16 villages (seeds 1001 to 1016, none of them used in training)
for up to 28 days each, sampling its choices, choosing only among what the engine allows. The scripted bots played the same seeds.
Each village is recorded in `ML/data/runs/20261011-2259-eval-bc-reads-items-corrected/` with its own report. The network played 14,021 villager-ticks a second.

**Averages over the villages:**

|  | villages | days lived | alive at the end | achievements | buildings | technologies | things crafted | things gathered | crops reaped | villager-days | reward per villager-tick |
|---|---|---|---|---|---|---|---|---|---|---|---|
| this network | 16 | 18.68 | 0 | 6 | 0 | 0 | 6.625 | 719.7 | 0 | 77.63 | -0.01839 |
| scripted bots | 16 | 26.94 | 5.125 | 20.31 | 4.062 | 0.5625 | 144.8 | 865.9 | 3.625 | 159.9 | 0.002294 |

**Share of villages that reached each achievement:**

| achievement | this network | scripted bots |
|---|---|---|
| The Council Changed Its Mind | 100% | 100% |
| Toolmakers | 100% | 100% |
| One Season | 100% | 100% |
| A Full Larder | 0% | 100% |
| First Night | 100% | 100% |
| Wolf Down | 100% | 100% |
| Everyone Has a Job | 100% | 100% |
| Somewhere to Put Things | 0% | 100% |
| Winter Survived | 0% | 88% |
| First Catch | 0% | 88% |
| First Garden | 0% | 88% |
| Fish Supper | 0% | 88% |
| A Fire in the Village | 0% | 88% |
| A Place to Think | 0% | 88% |
| Something to Study | 0% | 88% |
| A Full Year | 0% | 88% |
| Nobody Lost to Winter | 0% | 81% |
| Roast | 0% | 75% |
| A Year Without a Death | 0% | 75% |
| Seeds in the Ground | 0% | 69% |
| First Harvest | 0% | 56% |
| Carpentry | 0% | 56% |
| A Proper Village | 0% | 44% |
| Planks | 0% | 38% |
| Sawn Timber | 0% | 38% |

Folder: `ML/experiments/20261011-2259-eval-bc-reads-items-corrected/`. Run again with: `ML/evaluate.py --model ML/experiments/20261011-2250-bc-reads-items-corrected/model.pt --name eval-bc-reads-items-corrected --villages 16 --days 28 --note "The network that reads items and jobs, taught on the situations earlier networks were corrected on: a year on the same 16 maps."`

## 2026-10-11 23:16  bc-compass-walks  (imitation)

The compass now points at the nearest thing a villager could use, by the way it would walk, and the bots go where it points (see Open Issues, 2026-10-11): before, the needle was on what the bot was walking to in under half of its steps, and no network learned to walk. 48 villages recorded afresh by the bots (seeds 101 to 148); the network reads items and jobs. Everything else as in bc-balanced, where a step was told right 57 to 67% of the time.

**Data.** `ML/data/datasets/bots-compass2`: 48 recorded villages played by Survival; 40 to learn from
(1,536,944 villager-ticks), 8 held out whole (312,717).
**Network.** CNN over the 11 x 11 view (77 one-hot planes), MLP over the 195-value vector, LSTM (128), heads for
21 actions and 21 x 68 arguments; 694,186 parameters.
**Training.** 16 epochs, windows of 128 ticks (16 to warm the LSTM up), 12 villages a batch, AdamW, lr 0.001, plain imitation (cross-entropy over what was possible), rare actions weighted up (power 0.5).

**Result on the held-out villages** (best epoch, 16):

|  | action as recorded | argument as recorded | both |
|---|---|---|---|
| this network | 0.7788 | 0.9538 | 0.7776 |
| always the commonest action (sleep) | 0.2835 |  |  |
| a blind pick among what is possible | 0.1134 |  |  |

By action, most chosen first:

| action | share of choices | chosen as recorded |
|---|---|---|
| sleep | 0.2818 | 0.9528 |
| emote_none | 0.1464 | 0.8339 |
| move_west | 0.1018 | 0.5426 |
| move_east | 0.1005 | 0.5486 |
| move_north | 0.09528 | 0.6461 |
| move_south | 0.09473 | 0.6461 |
| harvest_attack | 0.08987 | 0.9617 |
| craft_build | 0.05704 | 0.9406 |
| consume | 0.01877 | 0.1283 |
| vote | 0.008347 | 1 |
| deposit | 0.00324 | 0.8028 |
| equip | 0.001312 | 0.7364 |

Curves: `curves.svg`. Agreeing with the recording tick by tick is not the same as playing well: see the evaluation of this network.

Folder: `ML/experiments/20261011-2316-bc-compass-walks/`. Run again with: `ML/train_bc.py --dataset ML/data/datasets/bots-compass2 --name bc-compass-walks --epochs 16 --balance 0.5 --note "The compass now points at the nearest thing a villager could use, by the way it would walk, and the bots go where it points (see Open Issues, 2026-10-11): before, the needle was on what the bot was walking to in under half of its steps, and no network learned to walk. 48 villages recorded afresh by the bots (seeds 101 to 148); the network reads items and jobs. Everything else as in bc-balanced, where a step was told right 57 to 67% of the time."`

## 2026-10-11 23:21  dagger-compass  (imitation with correction)

Correction with the new compass. With the old one, eight rounds like these took a network from 8 days to 17 and no further: told to walk to wood it stepped toward the tree on half of the ticks. Same settings as dagger-situational.

**Why.** Trained only on villages the bots ran, the network had never seen where its own slips lead. Here it plays, the bot says on every
tick what it would have done in that villager's place, and those situations are added to the training data.
**The bot's advice.** It keeps a villager's plan only while the villager does as advised; when the villager does something else the advice starts again from the situation, so it never hangs on a plan the network left and cannot see.
Rare actions are weighted up in the loss (power 0.5).
**Setup.** Started from `20261011-2316-bc-compass-walks`. 8 rounds; each plays 24 new villages for up to 2400 ticks
(10 days), then trains 2 passes over the 40 recorded villages plus everything gathered so far
(lr 0.0003). In the first rounds the bot's own choice is played part of the time, so the villages last long enough to be worth learning from.
The gathered data is in `ML/data/datasets/20261011-2321-dagger-compass/`.

| round | bot plays | days lasted | alive at the end | wiped out | same choice as the bot, while playing | on recorded villages (held out) |
|---|---|---|---|---|---|---|
| 1 | 50% | 10 | 5.667 | 0 of 24 | 0.5699 | 0.7809 |
| 2 | 30% | 10 | 4.542 | 0 of 24 | 0.5141 | 0.7808 |
| 3 | 15% | 10 | 3.583 | 0 of 24 | 0.4479 | 0.7874 |
| 4 | 0% | 9.045 | 0.9583 | 7 of 24 | 0.3504 | 0.7881 |
| 5 | 0% | 7.881 | 0.5417 | 15 of 24 | 0.3914 | 0.791 |
| 6 | 0% | 8.9 | 1.25 | 7 of 24 | 0.4233 | 0.7893 |
| 7 | 0% | 9.153 | 1.875 | 5 of 24 | 0.4576 | 0.7889 |
| 8 | 0% | 10 | 3.042 | 0 of 24 | 0.4636 | 0.7954 |

"Days lasted" in the last rounds is the network alone. Whether it can run a village for a whole year is the evaluation that follows.

Folder: `ML/experiments/20261011-2321-dagger-compass/`. Run again with: `ML/train_dagger.py --start 20261011-2316-bc-compass-walks --dataset ML/data/datasets/bots-compass2 --name dagger-compass --rounds 8 --villages 24 --horizon 2400 --epochs 2 --seed 7000 --note "Correction with the new compass. With the old one, eight rounds like these took a network from 8 days to 17 and no further: told to walk to wood it stepped toward the tree on half of the ticks. Same settings as dagger-situational."`

## 2026-10-11 23:29  eval-dagger-compass  (evaluation)

After eight rounds of correction with the new compass: a year on the same 16 maps (the bots played them afresh, with the new compass).

**Setup.** The network of `20261011-2321-dagger-compass` played 16 villages (seeds 1001 to 1016, none of them used in training)
for up to 28 days each, sampling its choices, choosing only among what the engine allows. The scripted bots played the same seeds.
Each village is recorded in `ML/data/runs/20261011-2329-eval-dagger-compass/` with its own report. The network played 12,647 villager-ticks a second.

**Averages over the villages:**

|  | villages | days lived | alive at the end | achievements | buildings | technologies | things crafted | things gathered | crops reaped | villager-days | reward per villager-tick |
|---|---|---|---|---|---|---|---|---|---|---|---|
| this network | 16 | 15.75 | 0 | 5.688 | 0 | 0 | 5.562 | 545.1 | 0 | 63.35 | -0.02479 |
| scripted bots | 16 | 26.92 | 5.062 | 20 | 4.125 | 0.4375 | 148.4 | 862.9 | 3.125 | 159.8 | 0.002113 |

**Share of villages that reached each achievement:**

| achievement | this network | scripted bots |
|---|---|---|
| A Full Larder | 0% | 100% |
| Wolf Down | 94% | 100% |
| The Council Changed Its Mind | 88% | 100% |
| Somewhere to Put Things | 0% | 100% |
| Everyone Has a Job | 100% | 100% |
| Toolmakers | 100% | 100% |
| First Night | 100% | 100% |
| One Season | 81% | 100% |
| Something to Study | 0% | 88% |
| First Catch | 6% | 88% |
| A Place to Think | 0% | 88% |
| A Full Year | 0% | 88% |
| First Garden | 0% | 88% |
| A Fire in the Village | 0% | 88% |
| Winter Survived | 0% | 88% |
| Fish Supper | 0% | 88% |
| Roast | 0% | 75% |
| Nobody Lost to Winter | 0% | 75% |
| A Year Without a Death | 0% | 69% |
| Seeds in the Ground | 0% | 56% |
| First Harvest | 0% | 50% |
| Planks | 0% | 44% |
| A Proper Village | 0% | 44% |
| Carpentry | 0% | 44% |
| Sawn Timber | 0% | 44% |

Folder: `ML/experiments/20261011-2329-eval-dagger-compass/`. Run again with: `ML/evaluate.py --model ML/experiments/20261011-2321-dagger-compass/model.pt --name eval-dagger-compass --villages 16 --days 28 --note "After eight rounds of correction with the new compass: a year on the same 16 maps (the bots played them afresh, with the new compass)."`

## 2026-10-11 23:33  bc-sees-now  (imitation)

The heads of the network now read what the villager sees on this tick directly, beside what its memory holds. Before, everything passed through the LSTM, and a network corrected for eight rounds chose the advised step on 58% of the very ticks it had been trained on; a plain network with no memory, given the same view, told the step right on 72% of ticks in villages it had not seen. Learns from the bots recorded with the new compass and from the situations the last correction run gathered.

**Data.** `ML/data/datasets/bots-compass2`: 48 recorded villages played by Survival; 40 to learn from
(3,406,012 villager-ticks), 8 held out whole (312,717).
It also learned from 192 villages that earlier networks played with the bot advising on every tick: ML/data/datasets/20261011-2321-dagger-compass. **Network.** CNN over the 11 x 11 view (77 one-hot planes), MLP over the 195-value vector, LSTM (128), heads for
21 actions and 21 x 68 arguments; 1,065,386 parameters.
**Training.** 12 epochs, windows of 128 ticks (16 to warm the LSTM up), 12 villages a batch, AdamW, lr 0.001, plain imitation (cross-entropy over what was possible), rare actions weighted up (power 0.5).

**Result on the held-out villages** (best epoch, 12):

|  | action as recorded | argument as recorded | both |
|---|---|---|---|
| this network | 0.7657 | 0.9635 | 0.7646 |
| always the commonest action (sleep) | 0.2835 |  |  |
| a blind pick among what is possible | 0.1134 |  |  |

By action, most chosen first:

| action | share of choices | chosen as recorded |
|---|---|---|
| sleep | 0.2818 | 0.9592 |
| emote_none | 0.1464 | 0.8667 |
| move_west | 0.1018 | 0.4353 |
| move_east | 0.1005 | 0.4313 |
| move_north | 0.09528 | 0.6645 |
| move_south | 0.09473 | 0.6371 |
| harvest_attack | 0.08987 | 0.9578 |
| craft_build | 0.05704 | 0.9586 |
| consume | 0.01877 | 0.2026 |
| vote | 0.008347 | 1 |
| deposit | 0.00324 | 0.8063 |
| equip | 0.001312 | 0.8109 |

Curves: `curves.svg`. Agreeing with the recording tick by tick is not the same as playing well: see the evaluation of this network.

Folder: `ML/experiments/20261011-2333-bc-sees-now/`. Run again with: `ML/train_bc.py --dataset ML/data/datasets/bots-compass2 --extra ML/data/datasets/20261011-2321-dagger-compass --name bc-sees-now --epochs 12 --balance 0.5 --note "The heads of the network now read what the villager sees on this tick directly, beside what its memory holds. Before, everything passed through the LSTM, and a network corrected for eight rounds chose the advised step on 58% of the very ticks it had been trained on; a plain network with no memory, given the same view, told the step right on 72% of ticks in villages it had not seen. Learns from the bots recorded with the new compass and from the situations the last correction run gathered."`

## 2026-10-11 23:38  dagger-sees-now  (imitation with correction)

Correction of the network whose heads see the present. Same rounds as dagger-compass, which ended at 15.7 days and nothing built.

**Why.** Trained only on villages the bots ran, the network had never seen where its own slips lead. Here it plays, the bot says on every
tick what it would have done in that villager's place, and those situations are added to the training data.
**The bot's advice.** It keeps a villager's plan only while the villager does as advised; when the villager does something else the advice starts again from the situation, so it never hangs on a plan the network left and cannot see.
Rare actions are weighted up in the loss (power 0.5).
**Setup.** Started from `20261011-2333-bc-sees-now`. 8 rounds; each plays 24 new villages for up to 2400 ticks
(10 days), then trains 2 passes over the 40 recorded villages plus everything gathered so far
(lr 0.0003). In the first rounds the bot's own choice is played part of the time, so the villages last long enough to be worth learning from.
The gathered data is in `ML/data/datasets/20261011-2338-dagger-sees-now/`. It also trained on what earlier correction runs gathered: ML/data/datasets/20261011-2321-dagger-compass.

| round | bot plays | days lasted | alive at the end | wiped out | same choice as the bot, while playing | on recorded villages (held out) |
|---|---|---|---|---|---|---|
| 1 | 30% | 10 | 5.833 | 0 of 24 | 0.565 | 0.7674 |
| 2 | 15% | 10 | 5.792 | 0 of 24 | 0.5693 | 0.7713 |
| 3 | 0% | 10 | 5.25 | 0 of 24 | 0.5785 | 0.7752 |
| 4 | 0% | 10 | 5.625 | 0 of 24 | 0.5994 | 0.7729 |
| 5 | 0% | 10 | 5.375 | 0 of 24 | 0.6074 | 0.78 |
| 6 | 0% | 10 | 5.583 | 0 of 24 | 0.6093 | 0.7773 |
| 7 | 0% | 10 | 5.458 | 0 of 24 | 0.6368 | 0.7857 |
| 8 | 0% | 10 | 5.667 | 0 of 24 | 0.6531 | 0.7909 |

"Days lasted" in the last rounds is the network alone. Whether it can run a village for a whole year is the evaluation that follows.

Folder: `ML/experiments/20261011-2338-dagger-sees-now/`. Run again with: `ML/train_dagger.py --start 20261011-2333-bc-sees-now --dataset ML/data/datasets/bots-compass2 --extra ML/data/datasets/20261011-2321-dagger-compass --name dagger-sees-now --rounds 8 --villages 24 --horizon 2400 --bot-share 0.3,0.15,0 --epochs 2 --seed 11000 --note "Correction of the network whose heads see the present. Same rounds as dagger-compass, which ended at 15.7 days and nothing built."`

## 2026-10-11 23:50  eval-dagger-sees-now  (evaluation)

The network whose heads see the present, after imitation and eight rounds of correction: a year on the same 16 maps.

**Setup.** The network of `20261011-2338-dagger-sees-now` played 16 villages (seeds 1001 to 1016, none of them used in training)
for up to 28 days each, sampling its choices, choosing only among what the engine allows. The scripted bots played the same seeds.
Each village is recorded in `ML/data/runs/20261011-2350-eval-dagger-sees-now/` with its own report. The network played 12,606 villager-ticks a second.

**Averages over the villages:**

|  | villages | days lived | alive at the end | achievements | buildings | weddings | children born | technologies | things crafted | things gathered | crops reaped | villager-days | reward per villager-tick |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| this network | 16 | 19.05 | 0 | 6.188 | 0 | 0 | 0 | 0 | 12.69 | 916.1 | 0 | 104.2 | -0.01486 |
| scripted bots | 16 | 26.92 | 5.062 | 20 | 4.125 | 0 | 0 | 0.4375 | 148.4 | 862.9 | 3.125 | 159.8 | 0.002113 |

**Share of villages that reached each achievement:**

| achievement | this network | scripted bots |
|---|---|---|
| A Full Larder | 0% | 100% |
| Wolf Down | 100% | 100% |
| Somewhere to Put Things | 0% | 100% |
| First Night | 100% | 100% |
| The Council Changed Its Mind | 100% | 100% |
| One Season | 100% | 100% |
| Toolmakers | 100% | 100% |
| Everyone Has a Job | 100% | 100% |
| A Full Year | 0% | 88% |
| First Catch | 19% | 88% |
| Something to Study | 0% | 88% |
| First Garden | 0% | 88% |
| A Place to Think | 0% | 88% |
| A Fire in the Village | 0% | 88% |
| Winter Survived | 0% | 88% |
| Fish Supper | 0% | 88% |
| Roast | 0% | 75% |
| Nobody Lost to Winter | 0% | 75% |
| A Year Without a Death | 0% | 69% |
| Seeds in the Ground | 0% | 56% |
| First Harvest | 0% | 50% |
| Planks | 0% | 44% |
| Carpentry | 0% | 44% |
| Sawn Timber | 0% | 44% |
| A Proper Village | 0% | 44% |

Folder: `ML/experiments/20261011-2350-eval-dagger-sees-now/`. Run again with: `ML/evaluate.py --model ML/experiments/20261011-2338-dagger-sees-now/model.pt --name eval-dagger-sees-now --villages 16 --days 28 --note "The network whose heads see the present, after imitation and eight rounds of correction: a year on the same 16 maps."`

## 2026-10-11 23:53  note: why no network could walk: the compass and the bot did not mean the same thing

Every network so far froze around day 19 having built nothing. Looked at step by step (ML/host advice): told by the bot to walk to wood, a villager stepped toward the tree on half of the ticks, a coin toss, also when the tree was two tiles away in plain view. So no wood came home, so no chest and no fire. Two things that were tried first did nothing for it: letting the network read which item is which and which job it has (bc-reads-items; needed for arguments, where it took the right item to hand 69% of the time instead of 43%), and teaching that network on every situation gathered so far (bc-reads-items-corrected: 18.7 days, nothing built). A plain network with no memory, given one tick's view, could tell the bot's step on 85% of the ticks it had learned from and 47% of new ones: the step could not be told from what a villager sees. The reason: the bot walked to the nearest thing by its own search (by walking, something it could harvest), and the compass pointed at the nearest as the crow flies, harvestable or not; the needle was on the bot's target in under half of its steps. The engine now has one search for both (Simulation.CompassTarget): the compass shows the nearest thing the villager could use by the way it would walk, with needles for cotton and oak too, and the bot goes where it points. After that the needle is on the bot's target on 94 to 100% of steps and the same plain network tells the step right on 72% of new ticks.

## 2026-10-11 23:53  note: the memory was in the way: the heads now see the present

With the compass put right, eight rounds of correction still ended at 15.7 days and nothing built (dagger-compass), and the network chose the advised step on 58% of the very ticks it had been trained on, where the plain network without memory got 72% on villages it had never seen. Everything a villager saw had to pass through the LSTM before reaching the heads. The heads now read what the villager sees on this tick directly, beside what the LSTM holds (ML/model.py). With that, after the same rounds, a network alone kept 5.7 of 6 villagers alive over ten days (3.0 before), carried three times the food, and stepped toward water and food on about 70 to 78% of its steps instead of half. Over a year it still froze (eval-dagger-sees-now: 19.1 days): the one walk it has not learned is the builder's to wood, 51%, so there is still no chest and no fire. Its agreement with the bot was still rising every round (0.57 to 0.65), so the next run has more rounds and longer ones. Not yet looked into: the bot's advice still hangs on two things a villager cannot see, whether it is in the middle of a foraging or a water trip, and who the bot takes for its builder when nobody holds the job.

## 2026-10-11 23:53  note: family life in the game

Age, emotes, giving, what villagers think of each other, marriage, a shared home, children, grief and morale from company are in the engine (Simulation.Family.cs; rules R12, R22, R24), and a village can grow. The bots greet whoever is near, the builder puts up a tent for a couple without a home once there are hides for it, and whoever has a home sleeps there. On 48 villages over two years (bots-family): a wedding in 44 (day 22 at the median), a tent in 32, a child in 27 (0.85 a village, first on day 35 at the median), up to ten villagers. The tent's cost was lowered from 4 hides to 2 to make this possible at all: a village comes by about 3 hides a year. Over one year the bots now keep 5.4 of 6 alive on those maps (4.9 before).

## 2026-10-11 23:52  bc-family  (imitation)

The first network on the game with family life in it. 48 villages played by the bots for two years each (seeds 101 to 148): they greet each other, marry, put up a tent for a couple and sleep at home, and in about half of them a child is born. The compass shows the nearest usable thing by walking; the network's heads see the present beside its memory.

**Data.** `ML/data/datasets/bots-family`: 48 recorded villages played by Survival; 40 to learn from
(3,000,320 villager-ticks), 8 held out whole (613,030).
**Network.** CNN over the 11 x 11 view (77 one-hot planes), MLP over the 195-value vector, LSTM (128), heads for
21 actions and 21 x 68 arguments; 1,065,386 parameters.
**Training.** 12 epochs, windows of 128 ticks (16 to warm the LSTM up), 12 villages a batch, AdamW, lr 0.001, plain imitation (cross-entropy over what was possible), rare actions weighted up (power 0.5).

**Result on the held-out villages** (best epoch, 12):

|  | action as recorded | argument as recorded | both |
|---|---|---|---|
| this network | 0.7944 | 0.9595 | 0.7933 |
| always the commonest action (sleep) | 0.2645 |  |  |
| a blind pick among what is possible | 0.06867 |  |  |

By action, most chosen first:

| action | share of choices | chosen as recorded |
|---|---|---|
| sleep | 0.2648 | 0.9382 |
| emote_none | 0.164 | 0.8703 |
| move_west | 0.09292 | 0.5679 |
| move_east | 0.09265 | 0.5801 |
| move_south | 0.08921 | 0.6447 |
| move_north | 0.08858 | 0.6434 |
| harvest_attack | 0.08416 | 0.9529 |
| craft_build | 0.06628 | 0.9713 |
| emote_happy | 0.0271 | 0.848 |
| consume | 0.01745 | 0.1766 |
| vote | 0.007215 | 0.9997 |
| deposit | 0.003206 | 0.737 |

Curves: `curves.svg`. Agreeing with the recording tick by tick is not the same as playing well: see the evaluation of this network.

Folder: `ML/experiments/20261011-2352-bc-family/`. Run again with: `ML/train_bc.py --dataset ML/data/datasets/bots-family --name bc-family --epochs 12 --balance 0.5 --note "The first network on the game with family life in it. 48 villages played by the bots for two years each (seeds 101 to 148): they greet each other, marry, put up a tent for a couple and sleep at home, and in about half of them a child is born. The compass shows the nearest usable thing by walking; the network's heads see the present beside its memory."`

## 2026-10-12 00:22  dagger-family  (imitation with correction)

Correction on the game with family life: the network plays, the bot says on every tick what it would have done. Ten days a village, twelve rounds.

**Why.** Trained only on villages the bots ran, the network had never seen where its own slips lead. Here it plays, the bot says on every
tick what it would have done in that villager's place, and those situations are added to the training data.
**The bot's advice.** It keeps a villager's plan only while the villager does as advised; when the villager does something else the advice starts again from the situation, so it never hangs on a plan the network left and cannot see.
Rare actions are weighted up in the loss (power 0.5).
**Setup.** Started from `20261011-2352-bc-family`. 12 rounds; each plays 24 new villages for up to 2400 ticks
(10 days), then trains 2 passes over the 40 recorded villages plus everything gathered so far
(lr 0.0003). In the first rounds the bot's own choice is played part of the time, so the villages last long enough to be worth learning from.
The gathered data is in `ML/data/datasets/20261012-0022-dagger-family/`.

| round | bot plays | days lasted | alive at the end | wiped out | same choice as the bot, while playing | on recorded villages (held out) |
|---|---|---|---|---|---|---|
| 1 | 30% | 10 | 5 | 0 of 24 | 0.5251 | 0.7931 |
| 2 | 15% | 9.989 | 3.292 | 1 of 24 | 0.4291 | 0.8001 |
| 3 | 0% | 6.678 | 0.1667 | 20 of 24 | 0.3351 | 0.7995 |
| 4 | 0% | 8.509 | 0.6667 | 13 of 24 | 0.3811 | 0.8017 |
| 5 | 0% | 8.31 | 0.7917 | 10 of 24 | 0.3995 | 0.8021 |
| 6 | 0% | 9.798 | 1.708 | 5 of 24 | 0.4245 | 0.8022 |
| 7 | 0% | 10 | 3.083 | 0 of 24 | 0.4719 | 0.8046 |
| 8 | 0% | 10 | 4.417 | 0 of 24 | 0.5232 | 0.8025 |
| 9 | 0% | 10 | 4.5 | 0 of 24 | 0.5581 | 0.8023 |
| 10 | 0% | 10 | 4.542 | 0 of 24 | 0.5738 | 0.806 |
| 11 | 0% | 10 | 4.917 | 0 of 24 | 0.5925 | 0.8044 |
| 12 | 0% | 10 | 5.583 | 0 of 24 | 0.593 | 0.8075 |

"Days lasted" in the last rounds is the network alone. Whether it can run a village for a whole year is the evaluation that follows.

Folder: `ML/experiments/20261012-0022-dagger-family/`. Run again with: `ML/train_dagger.py --start 20261011-2352-bc-family --dataset ML/data/datasets/bots-family --name dagger-family --rounds 12 --villages 24 --horizon 2400 --bot-share 0.3,0.15,0 --epochs 2 --seed 12000 --note "Correction on the game with family life: the network plays, the bot says on every tick what it would have done. Ten days a village, twelve rounds."`

## 2026-10-12 06:47  eval-family-28d  (evaluation)

The network after imitation and twelve rounds of correction on the game with family life: a year on the same 16 maps, against the bots on the same maps. The longer rounds planned after this (whole years, two years) were not run: see the note that follows.

**Setup.** The network of `20261012-0022-dagger-family` played 16 villages (seeds 1001 to 1016, none of them used in training)
for up to 28 days each, sampling its choices, choosing only among what the engine allows. The scripted bots played the same seeds.
Each village is recorded in `ML/data/runs/20261012-0647-eval-family-28d/` with its own report. The network played 25,357 villager-ticks a second.

**Averages over the villages:**

|  | villages | days lived | alive at the end | achievements | buildings | weddings | children born | technologies | things crafted | things gathered | crops reaped | villager-days | reward per villager-tick |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| this network | 16 | 18.95 | 0 | 7.438 | 0.5625 | 0 | 0 | 0 | 15.56 | 1024 | 0 | 102.9 | -0.01511 |
| scripted bots | 16 | 27.47 | 5.625 | 22.5 | 4.438 | 1.438 | 0.1875 | 0.3125 | 142.9 | 837.5 | 2.062 | 163.7 | 0.002906 |

**Share of villages that reached each achievement:**

| achievement | this network | scripted bots |
|---|---|---|
| Everyone Has a Job | 100% | 100% |
| The Council Changed Its Mind | 100% | 94% |
| One Season | 100% | 100% |
| Toolmakers | 100% | 100% |
| Wolf Down | 100% | 100% |
| First Night | 100% | 100% |
| Good Company | 100% | 100% |
| A Wedding | 0% | 94% |
| A Fire in the Village | 0% | 94% |
| Roast | 0% | 94% |
| Somewhere to Put Things | 38% | 94% |
| Winter Survived | 0% | 94% |
| A Full Year | 0% | 94% |
| Fish Supper | 0% | 94% |
| First Catch | 6% | 94% |
| Nobody Lost to Winter | 0% | 88% |
| First Garden | 0% | 88% |
| A Place to Think | 0% | 88% |
| A Full Larder | 0% | 88% |
| Something to Study | 0% | 88% |
| A Year Without a Death | 0% | 75% |
| A Roof | 0% | 56% |
| A Home of One's Own | 0% | 56% |
| A Proper Village | 0% | 56% |
| Seeds in the Ground | 0% | 38% |
| First Harvest | 0% | 38% |
| Carpentry | 0% | 31% |
| A Child Is Born | 0% | 19% |

Folder: `ML/experiments/20261012-0647-eval-family-28d/`. Run again with: `ML/evaluate.py --model ML/experiments/20261012-0022-dagger-family/model.pt --name eval-family-28d --villages 16 --days 28 --note "The network after imitation and twelve rounds of correction on the game with family life: a year on the same 16 maps, against the bots on the same maps. The longer rounds planned after this (whole years, two years) were not run: see the note that follows."`

## 2026-10-12 06:48  note: the overnight run stopped after its first stage: the data outgrew the machine

The run on the game with family life (ML/data/chain_9.sh) got through imitation and twelve rounds of correction over ten days, then was stopped by hand. A round's training went from 71 seconds to 85 minutes. Two things did it, both from giving every village twelve places (six villagers and room for six children): a batch held twice the sequences and filled the 8 GB of the graphics card, and what the rounds gathered came to 12 GB on top of the bots' 6.4, more than the machine's memory keeps to hand, half of it rows for children who were never born. Whole years and two years, where family life would be corrected, and learning from outcomes were not run. What the first stage gave (eval-family-28d): 19.0 days, still nobody through the winter, but the first buildings any network has put up: a chest in 6 of 16 villages. No fire, no wedding. Before the next run: places only for as many children as the length of a round can bring (none in ten days), rows of the unborn left out of what is stored, and older rounds thinned so that a round's training stays a few minutes.

## 2026-10-12 07:11  eval-family-28d-t0.5  (evaluation)

The same network as eval-family-28d, choosing closer to its highest-scoring action (temperature 0.5). Sampling as trained, it undid its own last step on a third of its steps.

**Setup.** The network of `20261012-0022-dagger-family` played 16 villages (seeds 1001 to 1016, none of them used in training)
for up to 28 days each, sampling its choices (temperature 0.5), choosing only among what the engine allows. The scripted bots played the same seeds.
Each village is recorded in `ML/data/runs/20261012-0711-eval-family-28d-t0.5/` with its own report. The network played 23,862 villager-ticks a second.

**Averages over the villages:**

|  | villages | days lived | alive at the end | achievements | buildings | weddings | children born | technologies | things crafted | things gathered | crops reaped | villager-days | reward per villager-tick |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| this network | 16 | 19.17 | 0 | 8.375 | 1.812 | 0 | 0 | 0 | 19.5 | 883.1 | 0.0625 | 86.88 | -0.01571 |
| scripted bots | 16 | 27.47 | 5.625 | 22.5 | 4.438 | 1.438 | 0.1875 | 0.3125 | 142.9 | 837.5 | 2.062 | 163.7 | 0.002906 |

**Share of villages that reached each achievement:**

| achievement | this network | scripted bots |
|---|---|---|
| Wolf Down | 100% | 100% |
| First Night | 100% | 100% |
| Toolmakers | 100% | 100% |
| Good Company | 100% | 100% |
| Everyone Has a Job | 100% | 100% |
| One Season | 100% | 100% |
| A Fire in the Village | 0% | 94% |
| First Catch | 56% | 94% |
| A Full Year | 0% | 94% |
| Roast | 0% | 94% |
| Fish Supper | 0% | 94% |
| The Council Changed Its Mind | 50% | 94% |
| Somewhere to Put Things | 62% | 94% |
| A Wedding | 0% | 94% |
| Winter Survived | 0% | 94% |
| A Place to Think | 0% | 88% |
| First Garden | 25% | 88% |
| A Full Larder | 12% | 88% |
| Something to Study | 0% | 88% |
| Nobody Lost to Winter | 0% | 88% |
| A Year Without a Death | 0% | 75% |
| A Proper Village | 6% | 56% |
| A Roof | 6% | 56% |
| A Home of One's Own | 6% | 56% |
| Seeds in the Ground | 6% | 38% |
| First Harvest | 6% | 38% |
| Carpentry | 0% | 31% |
| A Child Is Born | 0% | 19% |

Folder: `ML/experiments/20261012-0711-eval-family-28d-t0.5/`. Run again with: `ML/evaluate.py --model ML/experiments/20261012-0022-dagger-family/model.pt --name eval-family-28d-t0.5 --villages 16 --days 28 --temperature 0.5 --note "The same network as eval-family-28d, choosing closer to its highest-scoring action (temperature 0.5). Sampling as trained, it undid its own last step on a third of its steps."`

## 2026-10-12 07:11  eval-family-28d-t0.25  (evaluation)

The same network as eval-family-28d, choosing closer to its highest-scoring action (temperature 0.25). Sampling as trained, it undid its own last step on a third of its steps.

**Setup.** The network of `20261012-0022-dagger-family` played 16 villages (seeds 1001 to 1016, none of them used in training)
for up to 28 days each, sampling its choices (temperature 0.25), choosing only among what the engine allows. The scripted bots played the same seeds.
Each village is recorded in `ML/data/runs/20261012-0711-eval-family-28d-t0.25/` with its own report. The network played 24,961 villager-ticks a second.

**Averages over the villages:**

|  | villages | days lived | alive at the end | achievements | buildings | weddings | children born | technologies | things crafted | things gathered | crops reaped | villager-days | reward per villager-tick |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| this network | 16 | 18.77 | 0 | 8.438 | 1.812 | 0 | 0 | 0 | 15 | 804 | 0 | 74.08 | -0.01753 |
| scripted bots | 16 | 27.47 | 5.625 | 22.5 | 4.438 | 1.438 | 0.1875 | 0.3125 | 142.9 | 837.5 | 2.062 | 163.7 | 0.002906 |

**Share of villages that reached each achievement:**

| achievement | this network | scripted bots |
|---|---|---|
| Good Company | 100% | 100% |
| First Night | 100% | 100% |
| Everyone Has a Job | 100% | 100% |
| Toolmakers | 100% | 100% |
| Wolf Down | 100% | 100% |
| One Season | 100% | 100% |
| A Fire in the Village | 0% | 94% |
| The Council Changed Its Mind | 56% | 94% |
| Winter Survived | 0% | 94% |
| A Wedding | 0% | 94% |
| Somewhere to Put Things | 81% | 94% |
| A Full Year | 0% | 94% |
| Roast | 0% | 94% |
| Fish Supper | 0% | 94% |
| First Catch | 69% | 94% |
| Nobody Lost to Winter | 0% | 88% |
| Something to Study | 0% | 88% |
| First Garden | 12% | 88% |
| A Place to Think | 0% | 88% |
| A Full Larder | 19% | 88% |
| A Year Without a Death | 0% | 75% |
| A Roof | 0% | 56% |
| A Home of One's Own | 0% | 56% |
| A Proper Village | 6% | 56% |
| Seeds in the Ground | 0% | 38% |
| First Harvest | 0% | 38% |
| Carpentry | 0% | 31% |
| A Child Is Born | 0% | 19% |

Folder: `ML/experiments/20261012-0711-eval-family-28d-t0.25/`. Run again with: `ML/evaluate.py --model ML/experiments/20261012-0022-dagger-family/model.pt --name eval-family-28d-t0.25 --villages 16 --days 28 --temperature 0.25 --note "The same network as eval-family-28d, choosing closer to its highest-scoring action (temperature 0.25). Sampling as trained, it undid its own last step on a third of its steps."`

## 2026-10-12 07:14  dagger-family-years  (imitation with correction)

Whole years, so that what comes after the chest, and autumn and winter, are corrected too. The network picks at temperature 0.5 while it plays: at 1 it built a chest in 6 of 16 villages and no fire; at 0.5 the same network built one in 10 and still no fire. A round now trains on at most 500 batches drawn from everything gathered.

**Why.** Trained only on villages the bots ran, the network had never seen where its own slips lead. Here it plays, the bot says on every
tick what it would have done in that villager's place, and those situations are added to the training data.
**The bot's advice.** It keeps a villager's plan only while the villager does as advised; when the villager does something else the advice starts again from the situation, so it never hangs on a plan the network left and cannot see.
Rare actions are weighted up in the loss (power 0.5).
**Setup.** Started from `20261012-0022-dagger-family`. 10 rounds; each plays 12 new villages for up to 6720 ticks
(28 days), then trains 1 passes over the 40 recorded villages plus everything gathered so far
(lr 0.0003). In the first rounds the bot's own choice is played part of the time, so the villages last long enough to be worth learning from.
The gathered data is in `ML/data/datasets/20261012-0714-dagger-family-years/`. It also trained on what earlier correction runs gathered: ML/data/datasets/20261012-0022-dagger-family.

| round | bot plays | days lasted | alive at the end | wiped out | same choice as the bot, while playing | on recorded villages (held out) |
|---|---|---|---|---|---|---|
| 1 | 0% | 18.54 | 0 | 12 of 12 | 0.5319 | 0.8017 |
| 2 | 0% | 18.95 | 0 | 12 of 12 | 0.5944 | 0.8024 |
| 3 | 0% | 18.98 | 0 | 12 of 12 | 0.6016 | 0.8035 |
| 4 | 0% | 18.89 | 0 | 12 of 12 | 0.6023 | 0.8085 |
| 5 | 0% | 18.88 | 0 | 12 of 12 | 0.6297 | 0.8015 |
| 6 | 0% | 18.88 | 0 | 12 of 12 | 0.6011 | 0.7901 |
| 7 | 0% | 19.04 | 0 | 12 of 12 | 0.6502 | 0.8041 |
| 8 | 0% | 18.81 | 0 | 12 of 12 | 0.6532 | 0.8068 |
| 9 | 0% | 18.87 | 0 | 12 of 12 | 0.6459 | 0.8 |
| 10 | 0% | 18.94 | 0 | 12 of 12 | 0.6438 | 0.8065 |

"Days lasted" in the last rounds is the network alone. Whether it can run a village for a whole year is the evaluation that follows.

Folder: `ML/experiments/20261012-0714-dagger-family-years/`. Run again with: `ML/train_dagger.py --start 20261012-0022-dagger-family --dataset ML/data/datasets/bots-family --extra ML/data/datasets/20261012-0022-dagger-family --name dagger-family-years --rounds 10 --villages 12 --horizon 6720 --bot-share 0 --epochs 1 --seed 13000 --slots 8 --temperature 0.5 --batches 500 --note "Whole years, so that what comes after the chest, and autumn and winter, are corrected too. The network picks at temperature 0.5 while it plays: at 1 it built a chest in 6 of 16 villages and no fire; at 0.5 the same network built one in 10 and still no fire. A round now trains on at most 500 batches drawn from everything gathered."`

## 2026-10-12 07:27  dagger-family-two-years  (imitation with correction)

Two years a village: weddings come around day 21 and children after that, so this is where a network's own family life is corrected.

**Why.** Trained only on villages the bots ran, the network had never seen where its own slips lead. Here it plays, the bot says on every
tick what it would have done in that villager's place, and those situations are added to the training data.
**The bot's advice.** It keeps a villager's plan only while the villager does as advised; when the villager does something else the advice starts again from the situation, so it never hangs on a plan the network left and cannot see.
Rare actions are weighted up in the loss (power 0.5).
**Setup.** Started from `20261012-0714-dagger-family-years`. 4 rounds; each plays 8 new villages for up to 13440 ticks
(56 days), then trains 1 passes over the 40 recorded villages plus everything gathered so far
(lr 0.0003). In the first rounds the bot's own choice is played part of the time, so the villages last long enough to be worth learning from.
The gathered data is in `ML/data/datasets/20261012-0727-dagger-family-two-years/`. It also trained on what earlier correction runs gathered: ML/data/datasets/20261012-0022-dagger-family,ML/data/datasets/20261012-0714-dagger-family-years.

| round | bot plays | days lasted | alive at the end | wiped out | same choice as the bot, while playing | on recorded villages (held out) |
|---|---|---|---|---|---|---|
| 1 | 0% | 18.92 | 0 | 8 of 8 | 0.7371 | 0.7984 |
| 2 | 0% | 18.9 | 0 | 8 of 8 | 0.6853 | 0.8004 |
| 3 | 0% | 18.86 | 0 | 8 of 8 | 0.7202 | 0.805 |
| 4 | 0% | 18.85 | 0 | 8 of 8 | 0.7172 | 0.8094 |

"Days lasted" in the last rounds is the network alone. Whether it can run a village for a whole year is the evaluation that follows.

Folder: `ML/experiments/20261012-0727-dagger-family-two-years/`. Run again with: `ML/train_dagger.py --start 20261012-0714-dagger-family-years --dataset ML/data/datasets/bots-family --extra ML/data/datasets/20261012-0022-dagger-family,ML/data/datasets/20261012-0714-dagger-family-years --name dagger-family-two-years --rounds 4 --villages 8 --horizon 13440 --bot-share 0 --epochs 1 --seed 14000 --slots 10 --temperature 0.5 --batches 500 --note "Two years a village: weddings come around day 21 and children after that, so this is where a network's own family life is corrected."`

## 2026-10-12 07:38  eval-family-years-28d  (evaluation)

After correction over whole years and two years: 28 days on the same 16 maps, picking at temperature 0.5, against the bots on the same maps.

**Setup.** The network of `20261012-0727-dagger-family-two-years` played 16 villages (seeds 1001 to 1016, none of them used in training)
for up to 28 days each, sampling its choices (temperature 0.5), choosing only among what the engine allows. The scripted bots played the same seeds.
Each village is recorded in `ML/data/runs/20261012-0738-eval-family-years-28d/` with its own report. The network played 22,644 villager-ticks a second.

**Averages over the villages:**

|  | villages | days lived | alive at the end | achievements | buildings | weddings | children born | technologies | things crafted | things gathered | crops reaped | villager-days | reward per villager-tick |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| this network | 16 | 19.07 | 0 | 7.188 | 0.0625 | 0 | 0 | 0 | 13.31 | 826.5 | 0 | 100.3 | -0.01528 |
| scripted bots | 16 | 27.47 | 5.625 | 22.5 | 4.438 | 1.438 | 0.1875 | 0.3125 | 142.9 | 837.5 | 2.062 | 163.7 | 0.002906 |

**Share of villages that reached each achievement:**

| achievement | this network | scripted bots |
|---|---|---|
| Toolmakers | 100% | 100% |
| One Season | 100% | 100% |
| Everyone Has a Job | 100% | 100% |
| Wolf Down | 100% | 100% |
| First Night | 100% | 100% |
| Good Company | 100% | 100% |
| Somewhere to Put Things | 6% | 94% |
| A Full Year | 0% | 94% |
| First Catch | 25% | 94% |
| A Fire in the Village | 0% | 94% |
| The Council Changed Its Mind | 88% | 94% |
| Winter Survived | 0% | 94% |
| Roast | 0% | 94% |
| Fish Supper | 0% | 94% |
| A Wedding | 0% | 94% |
| A Full Larder | 0% | 88% |
| Nobody Lost to Winter | 0% | 88% |
| First Garden | 0% | 88% |
| A Place to Think | 0% | 88% |
| Something to Study | 0% | 88% |
| A Year Without a Death | 0% | 75% |
| A Roof | 0% | 56% |
| A Proper Village | 0% | 56% |
| A Home of One's Own | 0% | 56% |
| First Harvest | 0% | 38% |
| Seeds in the Ground | 0% | 38% |
| Carpentry | 0% | 31% |
| A Child Is Born | 0% | 19% |

Folder: `ML/experiments/20261012-0738-eval-family-years-28d/`. Run again with: `ML/evaluate.py --model ML/experiments/20261012-0727-dagger-family-two-years/model.pt --name eval-family-years-28d --villages 16 --days 28 --temperature 0.5 --note "After correction over whole years and two years: 28 days on the same 16 maps, picking at temperature 0.5, against the bots on the same maps."`

## 2026-10-12 07:39  eval-family-years-56d  (evaluation)

After correction over whole years and two years: 56 days on the same 16 maps, picking at temperature 0.5, against the bots on the same maps.

**Setup.** The network of `20261012-0727-dagger-family-two-years` played 16 villages (seeds 1001 to 1016, none of them used in training)
for up to 56 days each, sampling its choices (temperature 0.5), choosing only among what the engine allows. The scripted bots played the same seeds.
Each village is recorded in `ML/data/runs/20261012-0739-eval-family-years-56d/` with its own report. The network played 23,330 villager-ticks a second.

**Averages over the villages:**

|  | villages | days lived | alive at the end | achievements | buildings | weddings | children born | technologies | things crafted | things gathered | crops reaped | villager-days | reward per villager-tick |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| this network | 16 | 19.02 | 0 | 7.312 | 0.1875 | 0 | 0 | 0 | 14.06 | 828.8 | 0 | 101.2 | -0.01528 |
| scripted bots | 16 | 53.72 | 5.375 | 25.88 | 5.812 | 2.875 | 1.188 | 1.062 | 299.6 | 1493 | 2.75 | 328.3 | 0.001206 |

**Share of villages that reached each achievement:**

| achievement | this network | scripted bots |
|---|---|---|
| First Night | 100% | 100% |
| Wolf Down | 100% | 100% |
| Everyone Has a Job | 100% | 100% |
| One Season | 100% | 100% |
| Good Company | 100% | 100% |
| Toolmakers | 100% | 100% |
| Somewhere to Put Things | 19% | 94% |
| First Catch | 19% | 94% |
| A Full Year | 0% | 94% |
| Roast | 0% | 94% |
| A Fire in the Village | 0% | 94% |
| A Full Larder | 0% | 94% |
| A Wedding | 0% | 94% |
| Winter Survived | 0% | 94% |
| The Council Changed Its Mind | 94% | 94% |
| Fish Supper | 0% | 94% |
| Something to Study | 0% | 88% |
| A Proper Village | 0% | 88% |
| First Garden | 0% | 88% |
| A Place to Think | 0% | 88% |
| Nobody Lost to Winter | 0% | 88% |
| Carpentry | 0% | 75% |
| A Year Without a Death | 0% | 75% |
| Planks | 0% | 69% |
| Sawn Timber | 0% | 69% |
| A Home of One's Own | 0% | 56% |
| A Roof | 0% | 56% |
| A Child Is Born | 0% | 50% |
| Seeds in the Ground | 0% | 44% |
| First Harvest | 0% | 38% |
| Agriculture | 0% | 31% |
| A Growing Village | 0% | 25% |
| A Library | 0% | 12% |
| A Town | 0% | 6% |
| Farmers | 0% | 6% |

Folder: `ML/experiments/20261012-0739-eval-family-years-56d/`. Run again with: `ML/evaluate.py --model ML/experiments/20261012-0727-dagger-family-two-years/model.pt --name eval-family-years-56d --villages 16 --days 56 --temperature 0.5 --note "After correction over whole years and two years: 56 days on the same 16 maps, picking at temperature 0.5, against the bots on the same maps."`

## 2026-10-12 07:40  dagger-family-cool  (imitation with correction)

Ten days a village, the network picking at temperature 0.5 while it plays, so that it gets as far as the chest and is corrected on what comes after: the pickaxe, the stone, the fire. Whole-year rounds (dagger-family-years) raised agreement with the bot from 0.53 to 0.65 and lost the chest.

**Why.** Trained only on villages the bots ran, the network had never seen where its own slips lead. Here it plays, the bot says on every
tick what it would have done in that villager's place, and those situations are added to the training data.
**The bot's advice.** It keeps a villager's plan only while the villager does as advised; when the villager does something else the advice starts again from the situation, so it never hangs on a plan the network left and cannot see.
Rare actions are weighted up in the loss (power 0.5).
**Setup.** Started from `20261012-0022-dagger-family`. 12 rounds; each plays 24 new villages for up to 2400 ticks
(10 days), then trains 1 passes over the 40 recorded villages plus everything gathered so far
(lr 0.0003). In the first rounds the bot's own choice is played part of the time, so the villages last long enough to be worth learning from.
The gathered data is in `ML/data/datasets/20261012-0740-dagger-family-cool/`. It also trained on what earlier correction runs gathered: ML/data/datasets/20261012-0022-dagger-family.

| round | bot plays | days lasted | alive at the end | wiped out | same choice as the bot, while playing | on recorded villages (held out) |
|---|---|---|---|---|---|---|
| 1 | 0% | 10 | 5.125 | 0 of 24 | 0.558 | 0.8014 |
| 2 | 0% | 10 | 4.542 | 0 of 24 | 0.5827 | 0.8024 |
| 3 | 0% | 9.775 | 5.417 | 1 of 24 | 0.63 | 0.8044 |
| 4 | 0% | 10 | 5.375 | 0 of 24 | 0.6334 | 0.7992 |
| 5 | 0% | 10 | 5.375 | 0 of 24 | 0.6239 | 0.8035 |
| 6 | 0% | 10 | 5 | 0 of 24 | 0.6553 | 0.8035 |
| 7 | 0% | 10 | 5.125 | 0 of 24 | 0.6345 | 0.8041 |
| 8 | 0% | 10 | 4.875 | 0 of 24 | 0.6718 | 0.7955 |
| 9 | 0% | 10 | 5.167 | 0 of 24 | 0.697 | 0.7986 |
| 10 | 0% | 10 | 5.292 | 0 of 24 | 0.7101 | 0.803 |
| 11 | 0% | 9.984 | 5.25 | 1 of 24 | 0.6896 | 0.8054 |
| 12 | 0% | 10 | 5.083 | 0 of 24 | 0.7235 | 0.8015 |

"Days lasted" in the last rounds is the network alone. Whether it can run a village for a whole year is the evaluation that follows.

Folder: `ML/experiments/20261012-0740-dagger-family-cool/`. Run again with: `ML/train_dagger.py --start 20261012-0022-dagger-family --dataset ML/data/datasets/bots-family --extra ML/data/datasets/20261012-0022-dagger-family --name dagger-family-cool --rounds 12 --villages 24 --horizon 2400 --bot-share 0 --epochs 1 --seed 15000 --temperature 0.5 --batches 500 --note "Ten days a village, the network picking at temperature 0.5 while it plays, so that it gets as far as the chest and is corrected on what comes after: the pickaxe, the stone, the fire. Whole-year rounds (dagger-family-years) raised agreement with the bot from 0.53 to 0.65 and lost the chest."`

## 2026-10-12 07:53  eval-family-cool-28d  (evaluation)

After ten-day rounds played at temperature 0.5: a year on the same 16 maps, picking at 0.5.

**Setup.** The network of `20261012-0740-dagger-family-cool` played 16 villages (seeds 1001 to 1016, none of them used in training)
for up to 28 days each, sampling its choices (temperature 0.5), choosing only among what the engine allows. The scripted bots played the same seeds.
Each village is recorded in `ML/data/runs/20261012-0753-eval-family-cool-28d/` with its own report. The network played 22,832 villager-ticks a second.

**Averages over the villages:**

|  | villages | days lived | alive at the end | achievements | buildings | weddings | children born | technologies | things crafted | things gathered | crops reaped | villager-days | reward per villager-tick |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| this network | 16 | 18.94 | 0 | 7.5 | 0.1875 | 0 | 0 | 0 | 10 | 757.1 | 0 | 100.1 | -0.0154 |
| scripted bots | 16 | 27.47 | 5.625 | 22.5 | 4.438 | 1.438 | 0.1875 | 0.3125 | 142.9 | 837.5 | 2.062 | 163.7 | 0.002906 |

**Share of villages that reached each achievement:**

| achievement | this network | scripted bots |
|---|---|---|
| Good Company | 100% | 100% |
| The Council Changed Its Mind | 100% | 94% |
| First Night | 100% | 100% |
| One Season | 100% | 100% |
| Wolf Down | 100% | 100% |
| Toolmakers | 100% | 100% |
| Everyone Has a Job | 100% | 100% |
| Fish Supper | 0% | 94% |
| A Fire in the Village | 0% | 94% |
| A Full Year | 0% | 94% |
| First Catch | 38% | 94% |
| Roast | 0% | 94% |
| Somewhere to Put Things | 6% | 94% |
| A Wedding | 0% | 94% |
| Winter Survived | 0% | 94% |
| Something to Study | 0% | 88% |
| A Full Larder | 6% | 88% |
| Nobody Lost to Winter | 0% | 88% |
| A Place to Think | 0% | 88% |
| First Garden | 0% | 88% |
| A Year Without a Death | 0% | 75% |
| A Proper Village | 0% | 56% |
| A Roof | 0% | 56% |
| A Home of One's Own | 0% | 56% |
| Seeds in the Ground | 0% | 38% |
| First Harvest | 0% | 38% |
| Carpentry | 0% | 31% |
| A Child Is Born | 0% | 19% |

Folder: `ML/experiments/20261012-0753-eval-family-cool-28d/`. Run again with: `ML/evaluate.py --model ML/experiments/20261012-0740-dagger-family-cool/model.pt --name eval-family-cool-28d --villages 16 --days 28 --temperature 0.5 --note "After ten-day rounds played at temperature 0.5: a year on the same 16 maps, picking at 0.5."`

## 2026-10-12 08:28  note: more correction raised agreement and lost the chest; what was ruled out on the way

Three things were looked into after the overnight run. (1) Whether the bot's advice hangs on trips a villager cannot see: no. A bot that keeps no plan at all (SurvivalBrain.Situational; host advice --compare) advises the same as the usual one on over 98% of ticks, 99.6% of the builder's steps toward wood. Played by itself such a bot keeps 0.9 of 6 alive, so it is no teacher either. (2) What the walking looks like: on every kind of walk the network undoes its own last step on about a third of its steps. Picking closer to its first choice (evaluate.py --temperature 0.5) the network of dagger-family built a chest in 10 of 16 villages instead of 6 and a garden in 4, and still no fire; it lived no longer (19.2 days). (3) More correction, now that a round is bounded (rows of the unborn left out, at most 500 batches a pass: a round takes a minute, not an hour): ten rounds over whole years and four over two years (dagger-family-years, dagger-family-two-years), and separately twelve over ten days played at temperature 0.5 (dagger-family-cool). Agreement with the bot while playing rose from 0.53 to 0.72 both ways, and both ways the chest was lost again (1 village in 16) and the villages froze on day 19 as before. So agreeing with the bot more often, tick for tick, is not what building takes, and more rounds of the same are not the way. The network of dagger-family is still the best there is and is the one installed in the game. Not tried: weighing the ticks that matter (a craft, a deposit, the step that ends a walk) above the thousands of ordinary ones, and learning from outcomes started from this network.

## 2026-10-12 08:31  dagger-family-turns  (imitation with correction)

More rounds of plain correction raised agreement with the bot and lost the chest: a craft, a deposit or the step that ends a walk counted like any of the thousands of steps on which a villager just carries on. Here every kind of action counts the same in total, and a tick on which the advice changes from the tick before counts four times.

**Why.** Trained only on villages the bots ran, the network had never seen where its own slips lead. Here it plays, the bot says on every
tick what it would have done in that villager's place, and those situations are added to the training data.
**The bot's advice.** It keeps a villager's plan only while the villager does as advised; when the villager does something else the advice starts again from the situation, so it never hangs on a plan the network left and cannot see.
Rare actions are weighted up in the loss (power 1.0).
**Setup.** Started from `20261012-0022-dagger-family`. 12 rounds; each plays 24 new villages for up to 2400 ticks
(10 days), then trains 1 passes over the 40 recorded villages plus everything gathered so far
(lr 0.0003). In the first rounds the bot's own choice is played part of the time, so the villages last long enough to be worth learning from.
The gathered data is in `ML/data/datasets/20261012-0831-dagger-family-turns/`. It also trained on what earlier correction runs gathered: ML/data/datasets/20261012-0022-dagger-family.

| round | bot plays | days lasted | alive at the end | wiped out | same choice as the bot, while playing | on recorded villages (held out) |
|---|---|---|---|---|---|---|
| 1 | 0% | 10 | 4.833 | 0 of 24 | 0.58 | 0.701 |
| 2 | 0% | 10 | 4.917 | 0 of 24 | 0.5483 | 0.6855 |
| 3 | 0% | 10 | 4.875 | 0 of 24 | 0.5741 | 0.7103 |
| 4 | 0% | 10 | 4.75 | 0 of 24 | 0.5744 | 0.6658 |
| 5 | 0% | 10 | 5 | 0 of 24 | 0.5735 | 0.699 |
| 6 | 0% | 10 | 4.583 | 0 of 24 | 0.5515 | 0.7098 |
| 7 | 0% | 10 | 5.083 | 0 of 24 | 0.5662 | 0.7041 |
| 8 | 0% | 10 | 4 | 0 of 24 | 0.5689 | 0.7245 |
| 9 | 0% | 10 | 4.708 | 0 of 24 | 0.5885 | 0.7202 |
| 10 | 0% | 10 | 5.375 | 0 of 24 | 0.5921 | 0.718 |
| 11 | 0% | 10 | 5.333 | 0 of 24 | 0.6044 | 0.7232 |
| 12 | 0% | 10 | 5.333 | 0 of 24 | 0.6026 | 0.7187 |

"Days lasted" in the last rounds is the network alone. Whether it can run a village for a whole year is the evaluation that follows.

Folder: `ML/experiments/20261012-0831-dagger-family-turns/`. Run again with: `ML/train_dagger.py --start 20261012-0022-dagger-family --dataset ML/data/datasets/bots-family --extra ML/data/datasets/20261012-0022-dagger-family --name dagger-family-turns --rounds 12 --villages 24 --horizon 2400 --bot-share 0 --epochs 1 --seed 16000 --temperature 0.5 --batches 500 --balance 1.0 --turn 4 --note "More rounds of plain correction raised agreement with the bot and lost the chest: a craft, a deposit or the step that ends a walk counted like any of the thousands of steps on which a villager just carries on. Here every kind of action counts the same in total, and a tick on which the advice changes from the tick before counts four times."`

## 2026-10-12 09:18  eval-family-turns-28d  (evaluation)

After correction weighing the ticks that matter: a year on the same 16 maps, picking at temperature 0.5.

**Setup.** The network of `20261012-0831-dagger-family-turns` played 16 villages (seeds 1001 to 1016, none of them used in training)
for up to 28 days each, sampling its choices (temperature 0.5), choosing only among what the engine allows. The scripted bots played the same seeds.
Each village is recorded in `ML/data/runs/20261012-0918-eval-family-turns-28d/` with its own report. The network played 19,654 villager-ticks a second.

**Averages over the villages:**

|  | villages | days lived | alive at the end | achievements | buildings | weddings | children born | technologies | things crafted | things gathered | crops reaped | villager-days | reward per villager-tick |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| this network | 16 | 18.97 | 0 | 7.562 | 0.3125 | 0 | 0 | 0 | 12.06 | 812.8 | 0 | 101.6 | -0.01531 |
| scripted bots | 16 | 27.47 | 5.625 | 22.5 | 4.438 | 1.438 | 0.1875 | 0.3125 | 142.9 | 837.5 | 2.062 | 163.7 | 0.002906 |

**Share of villages that reached each achievement:**

| achievement | this network | scripted bots |
|---|---|---|
| Toolmakers | 100% | 100% |
| One Season | 100% | 100% |
| The Council Changed Its Mind | 100% | 94% |
| First Night | 100% | 100% |
| Everyone Has a Job | 100% | 100% |
| Good Company | 100% | 100% |
| Wolf Down | 100% | 100% |
| Roast | 0% | 94% |
| Fish Supper | 0% | 94% |
| A Full Year | 0% | 94% |
| A Wedding | 0% | 94% |
| First Catch | 31% | 94% |
| Winter Survived | 0% | 94% |
| Somewhere to Put Things | 25% | 94% |
| A Fire in the Village | 0% | 94% |
| Something to Study | 0% | 88% |
| A Place to Think | 0% | 88% |
| First Garden | 0% | 88% |
| A Full Larder | 0% | 88% |
| Nobody Lost to Winter | 0% | 88% |
| A Year Without a Death | 0% | 75% |
| A Proper Village | 0% | 56% |
| A Home of One's Own | 0% | 56% |
| A Roof | 0% | 56% |
| First Harvest | 0% | 38% |
| Seeds in the Ground | 0% | 38% |
| Carpentry | 0% | 31% |
| A Child Is Born | 0% | 19% |

Folder: `ML/experiments/20261012-0918-eval-family-turns-28d/`. Run again with: `ML/evaluate.py --model ML/experiments/20261012-0831-dagger-family-turns/model.pt --name eval-family-turns-28d --villages 16 --days 28 --temperature 0.5 --note "After correction weighing the ticks that matter: a year on the same 16 maps, picking at temperature 0.5."`

## 2026-10-12 09:19  ppo-family  (learning from outcomes)

Learning from outcomes, from the network of dagger-family: the first to start this stage that can keep a village going for ten days and sometimes builds a chest. Six places a village, so no children here.

**Setup.** Started from `20261012-0022-dagger-family` (only the critic for the first 30 updates; then the policy held to its starting self with weight 1.0 fading to 0 over 150 updates). 32 villages of 6 at once, each played for 28 days and then replaced by a
new map, the villages out of step so that every update holds every season; 300 updates of 128 ticks = 7,372,800 villager-ticks over 244 villages.
Reward: Sim Rewards with shaping as the workbook has it (0: outcomes only), a villager's own and the village's parts mixed by its empathy. PPO: gamma 0.999, lambda 0.95,
clip 0.1, up to 3 epochs x 4 minibatches stopping at KL 0.015, policy lr 3e-05, entropy 0.003;
a critic of its own (lr 0.001) whose errors do not reach the policy.

**Before and after** (averages over the updates named; villages that ended in them):

|  | reward_per_tick | alive_share | days_lived | alive_at_end | achievements | buildings | technologies | gathered | crafted |
|---|---|---|---|---|---|---|---|---|---|
| before the policy changed (critic warm-up) | -0.002347 | 0.9642 | nan | nan | nan | nan | nan | nan | nan |
| last tenth of training | -0.01145 | 0.8645 | 18.98 | 0 | 6.991 | 0.005556 | 0 | 732.4 | 7.214 |

"alive_share" is the share of villagers alive over all the ticks played in an update. Curves: `curves.svg`. These are villages of 28 days played
while learning, the policy picking by chance in proportion to its scores; the evaluation that follows plays whole years on maps not used here.

Folder: `ML/experiments/20261012-0919-ppo-family/`. Run again with: `ML/train_ppo.py --start 20261012-0022-dagger-family --name ppo-family --updates 300 --envs 32 --steps 128 --days 28 --slots 6 --note "Learning from outcomes, from the network of dagger-family: the first to start this stage that can keep a village going for ten days and sometimes builds a chest. Six places a village, so no children here."`

## 2026-10-12 09:41  eval-ppo-family-28d  (evaluation)

After learning from outcomes: a year on the same 16 maps, picking at temperature 0.5.

**Setup.** The network of `20261012-0919-ppo-family` played 16 villages (seeds 1001 to 1016, none of them used in training)
for up to 28 days each, sampling its choices (temperature 0.5), choosing only among what the engine allows. The scripted bots played the same seeds.
Each village is recorded in `ML/data/runs/20261012-0941-eval-ppo-family-28d/` with its own report. The network played 20,419 villager-ticks a second.

**Averages over the villages:**

|  | villages | days lived | alive at the end | achievements | buildings | weddings | children born | technologies | things crafted | things gathered | crops reaped | villager-days | reward per villager-tick |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| this network | 16 | 17.87 | 0 | 6.375 | 0 | 0 | 0 | 0 | 5.875 | 450.2 | 0 | 88.64 | -0.01884 |
| scripted bots | 16 | 27.47 | 5.625 | 22.5 | 4.438 | 1.438 | 0.1875 | 0.3125 | 142.9 | 837.5 | 2.062 | 163.7 | 0.002906 |

**Share of villages that reached each achievement:**

| achievement | this network | scripted bots |
|---|---|---|
| Wolf Down | 94% | 100% |
| One Season | 94% | 100% |
| First Night | 100% | 100% |
| Good Company | 100% | 100% |
| Everyone Has a Job | 100% | 100% |
| Toolmakers | 100% | 100% |
| Winter Survived | 0% | 94% |
| A Fire in the Village | 0% | 94% |
| First Catch | 0% | 94% |
| A Full Year | 0% | 94% |
| The Council Changed Its Mind | 50% | 94% |
| A Wedding | 0% | 94% |
| Roast | 0% | 94% |
| Fish Supper | 0% | 94% |
| Somewhere to Put Things | 0% | 94% |
| Something to Study | 0% | 88% |
| Nobody Lost to Winter | 0% | 88% |
| A Full Larder | 0% | 88% |
| A Place to Think | 0% | 88% |
| First Garden | 0% | 88% |
| A Year Without a Death | 0% | 75% |
| A Proper Village | 0% | 56% |
| A Home of One's Own | 0% | 56% |
| A Roof | 0% | 56% |
| First Harvest | 0% | 38% |
| Seeds in the Ground | 0% | 38% |
| Carpentry | 0% | 31% |
| A Child Is Born | 0% | 19% |

Folder: `ML/experiments/20261012-0941-eval-ppo-family-28d/`. Run again with: `ML/evaluate.py --model ML/experiments/20261012-0919-ppo-family/model.pt --name eval-ppo-family-28d --villages 16 --days 28 --temperature 0.5 --note "After learning from outcomes: a year on the same 16 maps, picking at temperature 0.5."`
