using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using TinyTribe.Sim.Core;
using TinyTribe.Sim.Data;

namespace TinyTribe.Brains
{
    /// <summary>Plays recorded games without a screen, one per seed: the way to get many runs to compare.</summary>
    public static class RunBatch
    {
        /// <summary>
        /// One game per seed, each recorded into its own folder under runsFolder, for that many days or
        /// until nobody is left. adjust can change a game's setup before it starts. Returns the run folders.
        /// </summary>
        public static List<string> Run(GameData data, string runsFolder, IEnumerable<ulong> seeds, int days,
            BrainKind brains = BrainKind.Survival, Action<GameSetup> adjust = null, IDictionary<string, string> host = null)
        {
            var folders = new List<string>();
            foreach (ulong seed in seeds)
            {
                var setup = new GameSetup { Brains = brains };
                setup.Sim.Seed = seed;
                adjust?.Invoke(setup);
                var runner = SimRunner.Create(data, setup);
                var sim = runner.Sim;
                var recorder = RunRecorder.Start(runsFolder, data, sim, setup, host);
                runner.Recorder = recorder;
                long ticks = (long)days * sim.Config.TicksPerDay;
                string end = "time_up";
                for (long t = 0; t < ticks; t++)
                {
                    runner.StepOnce();
                    if (recorder.Stats.Get("alive") > 0) continue;
                    end = "everyone_dead";
                    break;
                }
                recorder.Finish(end);
                folders.Add(recorder.Folder);
            }
            return folders;
        }
    }

    /// <summary>report.md: a finished run written out for a person to read.</summary>
    public static class RunReport
    {
        static readonly CultureInfo Plain = CultureInfo.InvariantCulture;

        public static void Write(string folder, RunFile run, GameData data)
        {
            var result = run.Result;
            var text = new StringBuilder();
            text.Append("# Run ").Append(run.RunId).Append("\n\n");
            text.Append(run.Setup.Agents).Append(" villagers (").Append(run.Setup.Brains).Append("), seed ").Append(run.Setup.Seed)
                .Append(", map ").Append(run.Setup.MapWidth).Append(" x ").Append(run.Setup.MapHeight).Append(", starting in ").Append(run.Setup.StartSeason)
                .Append(". Weather ").Append(OnOff(run.Setup.Weather)).Append(", sickness ").Append(OnOff(run.Setup.Sickness))
                .Append(", job reviews ").Append(OnOff(run.Setup.JobReviews)).Append(", reward shaping x")
                .Append(run.Setup.RewardShapingScale.ToString("0.##", Plain)).Append(".\n\n");
            text.Append("Ran ").Append(result.Ticks).Append(" ticks (").Append(result.Days).Append(" days) in ")
                .Append(run.WallSeconds.ToString("0.#", Plain)).Append(" s and ended: ").Append(result.EndReason).Append(". ")
                .Append(result.Alive).Append(" of ").Append(run.Setup.Agents).Append(" alive at the end.\n\n");
            text.Append("Data: ").Append(run.Data.Workbook).Append(" (").Append(Short(run.Data.WorkbookSha256)).Append("). ")
                .Append(run.Setup.Replayable ? "The run can be played again from actions.bin." : "The run cannot be replayed: its world was not generated from a seed.")
                .Append("\n\n");

            text.Append("## Achievements\n\n").Append(result.Achievements.Count).Append(" of ").Append(data.Achievements.Count).Append(" reached.\n\n");
            if (result.Achievements.Count > 0)
            {
                text.Append("| Day | Tick | Achievement | Group |\n|---:|---:|---|---|\n");
                foreach (var achievement in result.Achievements)
                    text.Append("| ").Append(achievement.Day + 1).Append(" | ").Append(achievement.Tick).Append(" | ").Append(achievement.Name)
                        .Append(" | ").Append(achievement.Group).Append(" |\n");
                text.Append('\n');
            }

            text.Append("## Villagers\n\n| Villager | Stats (STR DEX INT END) | Job at the end | Jobs held | Lived (ticks) | Died of | Reward | Did most |\n|---|---|---|---|---:|---|---:|---|\n");
            foreach (var agent in result.Agents)
            {
                var born = agent.Index < run.Villagers.Count ? run.Villagers[agent.Index] : null;
                var jobs = new StringBuilder();
                foreach (var change in agent.RoleHistory)
                    jobs.Append(jobs.Length > 0 ? ", " : "").Append(change[1]).Append(" @").Append(change[0]);
                var most = new List<KeyValuePair<string, int>>(agent.ActionCounts);
                most.Sort((a, b) => b.Value.CompareTo(a.Value));
                var did = new StringBuilder();
                for (int i = 0; i < most.Count && i < 3; i++)
                    did.Append(did.Length > 0 ? ", " : "").Append(most[i].Key).Append(' ').Append((100L * most[i].Value / Math.Max(1, agent.TicksAlive))).Append('%');
                text.Append("| V").Append(agent.Index + 1).Append(" | ")
                    .Append(born != null ? born.Str + " " + born.Dex + " " + born.Int + " " + born.End : "").Append(" | ").Append(agent.Role).Append(" | ")
                    .Append(jobs).Append(" | ").Append(agent.TicksAlive).Append(" | ").Append(agent.Alive ? "" : agent.DiedOf + " @" + agent.DiedTick)
                    .Append(" | ").Append(agent.RewardTotal.ToString("0.##", Plain)).Append(" | ").Append(did).Append(" |\n");
            }

            text.Append("\n## Counters\n\n| Counter | Value |\n|---|---:|\n");
            foreach (var metric in result.Metrics)
                text.Append("| ").Append(metric.Key).Append(" | ").Append(metric.Value.ToString("0.####", Plain)).Append(" |\n");

            text.Append("\n## Not reached\n\n");
            if (result.AchievementsMissed.Count == 0) text.Append("Nothing: every achievement was reached.\n");
            foreach (string id in result.AchievementsMissed)
                foreach (var achievement in data.Achievements)
                    if (achievement.Id == id)
                        text.Append("- ").Append(achievement.Name).Append(": ").Append(achievement.Description).Append(" (").Append(achievement.Metric)
                            .Append(" at least ").Append(achievement.AtLeast.ToString("0.##", Plain)).Append(", got ")
                            .Append((result.Metrics.TryGetValue(achievement.Metric, out double got) ? got : 0).ToString("0.##", Plain)).Append(")\n");

            text.Append("\n## Files\n\n");
            foreach (var file in run.Files) text.Append("- `").Append(file.Key).Append("`: ").Append(file.Value).Append('\n');
            text.Append("- `run.json`: everything above, for a program to read\n\nThe layout of every file is in the workbook's Sim Run Log Schema sheet; `ML/runs.py` loads them into tables.\n");

            File.WriteAllText(Path.Combine(folder, "report.md"), text.ToString(), new UTF8Encoding(false));
        }

        static string OnOff(bool on) => on ? "on" : "off";

        static string Short(string sha) => string.IsNullOrEmpty(sha) ? "?" : sha.Substring(0, Math.Min(8, sha.Length));
    }
}
