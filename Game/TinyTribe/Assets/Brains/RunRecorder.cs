using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using TinyTribe.Sim.Core;
using TinyTribe.Sim.Data;

namespace TinyTribe.Brains
{
    /// <summary>
    /// Writes a run into a folder of its own as it is played (Sim Run Log Schema, rule R43).
    ///
    /// The simulation is deterministic, so the complete record of a run is small: its setup and what
    /// every villager chose on every tick (actions.bin, two bytes a villager a tick). RunReplay plays
    /// that back to any tick, and whatever was not written down can be worked out again from there.
    /// Beside it go the things worth having without a replay: every event (events.bin), the village
    /// and each villager at regular moments (timeline.csv, agents.csv), state hashes to check a replay
    /// against (hashes.bin), and in run.json who played what with which data, and how it ended:
    /// counters, achievements and each villager's part.
    /// </summary>
    public sealed class RunRecorder
    {
        public const int Schema = 1;
        public const string ActionsMagic = "TTACT001", EventsMagic = "TTEVT001", HashesMagic = "TTHSH001";
        public const int EventRecordBytes = 18;

        const string IndexHeader = "run_id,started_utc,seed,agents,brains,ticks,days,alive,deaths,built,techs,achievements,reward_mean,folder";
        const int FlushEveryTicks = 240;
        static readonly CultureInfo Plain = CultureInfo.InvariantCulture;

        /// <summary>Several villages can start or end in the same instant on different threads: one at a time at the shared folder.</summary>
        static readonly object FolderLock = new object();

        public string Folder { get; }
        public string RunId { get; }
        public RunStats Stats { get; }
        public bool Finished { get; private set; }

        readonly string runsFolder;
        readonly GameData data;
        readonly Simulation sim;
        readonly RunFile file;
        readonly Stopwatch wall = Stopwatch.StartNew();
        readonly BinaryWriter actions, events, hashes;
        readonly StreamWriter timeline, agentRows;
        readonly StringBuilder line = new StringBuilder(256);
        readonly int sampleEvery, hashEvery;
        readonly int[] finished, sites;
        readonly float[] needSum;
        double rewardSinceSample;

        RunRecorder(string runsFolder, GameData data, Simulation sim, GameSetup setup, IDictionary<string, string> host, string brainsLabel)
        {
            this.runsFolder = runsFolder;
            this.data = data;
            this.sim = sim;
            Stats = new RunStats(data, sim);
            sampleEvery = Math.Max(1, data.ConfigInt("run_timeline_every_ticks"));
            hashEvery = Math.Max(1, data.ConfigInt("run_hash_every_ticks"));
            int buildingInts = 0;
            foreach (var building in data.Buildings) buildingInts = Math.Max(buildingInts, building.Int + 1);
            finished = new int[buildingInts];
            sites = new int[buildingInts];
            needSum = new float[data.Needs.Count];

            var started = DateTime.UtcNow;
            string brains = brainsLabel ?? (setup != null ? setup.Brains.ToString() : "Custom");
            string name = started.ToString("yyyyMMdd-HHmmss", Plain) + "-s" + (setup != null ? setup.Sim.Seed : 0UL) + "-" + brains.ToLowerInvariant();
            lock (FolderLock)
            {
                RunId = name;
                for (int copy = 2; Directory.Exists(Path.Combine(runsFolder, RunId)); copy++) RunId = name + "-" + copy;
                Folder = Path.Combine(runsFolder, RunId);
                Directory.CreateDirectory(Folder);
            }

            file = new RunFile
            {
                Schema = Schema,
                RunId = RunId,
                StartedUtc = started.ToString("o", Plain),
                Host = host != null ? new Dictionary<string, string>(host) : new Dictionary<string, string>(),
                Data = new RunData { Workbook = data.Workbook, WorkbookSha256 = data.WorkbookSha256 },
                Setup = new RunSetup
                {
                    Seed = setup != null ? setup.Sim.Seed : 0UL,
                    Agents = sim.Agents.Count,
                    MapWidth = sim.World.Width,
                    MapHeight = sim.World.Height,
                    StartSeason = sim.Season.Id,
                    Weather = sim.WeatherEnabled,
                    Sickness = sim.SicknessEnabled,
                    JobReviews = sim.JobReviewsEnabled,
                    MaxAgents = sim.MaxAgents,
                    MaxVillagersEver = sim.MaxVillagersEver,
                    RewardShapingScale = sim.RewardShapingScale,
                    Brains = brains,
                    Replayable = setup != null && sim.Tick == 0,
                },
                World = new RunWorld { Width = sim.World.Width, Height = sim.World.Height, SpawnX = sim.World.SpawnX, SpawnY = sim.World.SpawnY },
                Villagers = new List<RunVillager>(),
                Files = new Dictionary<string, string>
                {
                    ["actions.bin"] = "'" + ActionsMagic + "', then per tick: agent count uint8, then per agent action_id uint8, action_arg uint8",
                    ["events.bin"] = "'" + EventsMagic + "', then 18-byte records: tick uint32, type uint16, agent int16, item int16, qty int32, x int16, y int16",
                    ["hashes.bin"] = "'" + HashesMagic + "', then 12-byte records every " + hashEvery + " ticks: tick uint32, state hash uint64",
                    ["timeline.csv"] = "the village every " + sampleEvery + " ticks",
                    ["agents.csv"] = "each villager every " + sampleEvery + " ticks",
                },
            };
            foreach (var agent in sim.Agents)
                file.Villagers.Add(new RunVillager
                {
                    Index = agent.Index, Str = agent.Stats.Str, Dex = agent.Stats.Dex, Int = agent.Stats.Int, End = agent.Stats.End,
                    Influence = agent.Stats.Influence, Empathy = agent.Stats.Empathy, Sociability = agent.Stats.Sociability,
                    Aggression = agent.Stats.Aggression, PackSlots = agent.InventoryCapacity, X = agent.X, Y = agent.Y, Brain = brains,
                });
            WriteRunFile();

            actions = Open("actions.bin", ActionsMagic);
            events = Open("events.bin", EventsMagic);
            hashes = Open("hashes.bin", HashesMagic);
            timeline = new StreamWriter(Path.Combine(Folder, "timeline.csv"), false, new UTF8Encoding(false), 1 << 14);
            agentRows = new StreamWriter(Path.Combine(Folder, "agents.csv"), false, new UTF8Encoding(false), 1 << 14);
            line.Append("tick,day,hour,season,weather,alive");
            foreach (var need in data.Needs) line.Append(',').Append(need.Id);
            line.Append(",food_stored,wood_stored,stone_stored,items_stored,buildings,sites,techs,research_points,reward,vote");
            timeline.WriteLine(line.ToString());
            line.Clear().Append("tick,agent,alive,x,y,role");
            foreach (var need in data.Needs) line.Append(',').Append(need.Id);
            line.Append(",reward_total,pack_items,hand,sick");
            agentRows.WriteLine(line.ToString());
        }

        /// <summary>
        /// Starts recording a simulation into a new folder under runsFolder. Give the setup the world
        /// was generated from, at tick 0, and the run can be replayed; without it the files are still
        /// written but there is nothing to rebuild the world from.
        /// </summary>
        public static RunRecorder Start(string runsFolder, GameData data, Simulation sim, GameSetup setup, IDictionary<string, string> host = null,
            string brainsLabel = null)
        {
            Directory.CreateDirectory(runsFolder);
            return new RunRecorder(runsFolder, data, sim, setup, host, brainsLabel);
        }

        BinaryWriter Open(string name, string magic)
        {
            var writer = new BinaryWriter(new FileStream(Path.Combine(Folder, name), FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16));
            writer.Write(Encoding.ASCII.GetBytes(magic));
            return writer;
        }

        void WriteRunFile() =>
            File.WriteAllText(Path.Combine(Folder, "run.json"), JsonConvert.SerializeObject(file, Formatting.Indented), new UTF8Encoding(false));

        /// <summary>Call after every Step, with the actions that went into it.</summary>
        public void AfterTick(AgentAction[] chosen)
        {
            if (Finished) return;
            long tick = sim.Tick;

            actions.Write((byte)chosen.Length);
            for (int i = 0; i < chosen.Length; i++)
            {
                actions.Write((byte)chosen[i].Id);
                actions.Write((byte)chosen[i].Arg);
            }

            foreach (var e in sim.Events)
            {
                events.Write((uint)tick);
                events.Write((ushort)e.Type);
                events.Write((short)e.Agent);
                events.Write((short)e.Item);
                events.Write(e.Qty);
                events.Write((short)e.X);
                events.Write((short)e.Y);
            }

            Stats.AfterTick(sim, chosen);
            for (int i = 0; i < sim.Agents.Count; i++) rewardSinceSample += sim.RewardOf(i);

            if (tick % sampleEvery == 0) WriteSample(tick);
            if (tick % hashEvery == 0)
            {
                hashes.Write((uint)tick);
                hashes.Write(sim.ComputeStateHash());
            }
            if (tick % FlushEveryTicks == 0) Flush();
        }

        void Flush()
        {
            actions.Flush();
            events.Flush();
            hashes.Flush();
            timeline.Flush();
            agentRows.Flush();
        }

        void WriteSample(long tick)
        {
            Stats.Sample(sim);
            VillageSummary.CountBuildings(sim, finished, sites);
            int built = 0, begun = 0, techs = 0, alive = 0;
            foreach (int count in finished) built += count;
            foreach (int count in sites) begun += count;
            foreach (var tech in data.Tech)
                if (tech.RpCost > 0f && sim.IsResearched(tech.Id)) techs++;
            Array.Clear(needSum, 0, needSum.Length);
            foreach (var agent in sim.Agents)
            {
                if (!agent.Alive) continue;
                alive++;
                for (int need = 0; need < needSum.Length; need++) needSum[need] += agent.Needs[need];
            }

            line.Clear().Append(tick).Append(',').Append(sim.Day).Append(',').Append(sim.Hour).Append(',').Append(sim.Season.Id).Append(',')
                .Append(sim.Weather.Id).Append(',').Append(alive);
            for (int need = 0; need < needSum.Length; need++) Number(alive > 0 ? needSum[need] / alive : 0f, "0.#");
            line.Append(',').Append(Stats.FoodStored).Append(',').Append(Stats.WoodStored).Append(',').Append(Stats.StoneStored).Append(',')
                .Append(Stats.ItemsStored).Append(',').Append(built).Append(',').Append(begun).Append(',').Append(techs);
            Number(sim.ResearchPoints, "0.##");
            Number(rewardSinceSample, "0.###");
            line.Append(',').Append(sim.OpenVote != null ? (int)sim.OpenVote.Kind : 0);
            timeline.WriteLine(line.ToString());
            rewardSinceSample = 0;

            foreach (var agent in sim.Agents)
            {
                line.Clear().Append(tick).Append(',').Append(agent.Index).Append(',').Append(agent.Alive ? 1 : 0).Append(',')
                    .Append(agent.X).Append(',').Append(agent.Y).Append(',').Append(agent.Role);
                for (int need = 0; need < needSum.Length; need++) Number(agent.Needs[need], "0.#");
                Number(Stats.Agents[agent.Index].RewardTotal, "0.###");
                int carried = 0, sick = 0;
                for (int slot = 0; slot < agent.InventoryCapacity; slot++) carried += agent.Inventory[slot].Qty;
                foreach (int ticksSick in agent.SickTicks)
                    if (ticksSick != 0) sick++;
                var hand = agent.Equipped[(int)EquipSlot.Hand];
                line.Append(',').Append(carried).Append(',').Append(hand.Qty > 0 ? hand.Item : 0).Append(',').Append(sick);
                agentRows.WriteLine(line.ToString());
            }
        }

        /// <summary>A number with a decimal point whatever the machine's language is: a decimal comma would break the CSV.</summary>
        void Number(double value, string format) => line.Append(',').Append(value.ToString(format, Plain));

        /// <summary>
        /// Ends the recording: closes the logs, writes the result into run.json and adds the run to the
        /// folder's index. endReason is "stopped", "everyone_dead" or whatever the caller knows.
        /// </summary>
        public void Finish(string endReason)
        {
            if (Finished) return;
            Finished = true;
            Stats.Close();
            actions.Dispose();
            events.Dispose();
            hashes.Dispose();
            timeline.Dispose();
            agentRows.Dispose();

            int alive = 0;
            foreach (var agent in sim.Agents)
                if (agent.Alive) alive++;
            var result = new RunResult
            {
                Ticks = (long)Stats.Get("ticks"),
                Days = (int)Stats.Get("days"),
                EndReason = endReason,
                Alive = alive,
                FinalStateHash = sim.ComputeStateHash().ToString(Plain),
                Metrics = new SortedDictionary<string, double>(StringComparer.Ordinal),
                Achievements = new List<RunAchievement>(),
                AchievementsMissed = new List<string>(),
                Agents = new List<RunAgentResult>(),
            };
            foreach (var metric in Stats.Metrics) result.Metrics[metric.Key] = Math.Round(metric.Value, 4);
            foreach (var reached in Stats.Achievements)
                result.Achievements.Add(new RunAchievement { Id = reached.Def.Id, Name = reached.Def.Name, Group = reached.Def.Group, Tick = reached.Tick, Day = reached.Day });
            result.Achievements.Sort((a, b) => a.Tick != b.Tick ? a.Tick.CompareTo(b.Tick) : string.CompareOrdinal(a.Id, b.Id));
            foreach (var achievement in data.Achievements)
                if (!Stats.HasAchieved(achievement.Id)) result.AchievementsMissed.Add(achievement.Id);
            for (int i = 0; i < Stats.Agents.Count; i++)
            {
                var villager = Stats.Agents[i];
                var agent = sim.Agents[i];
                var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
                foreach (var action in data.Actions)
                    if ((uint)action.Id < (uint)villager.ActionCounts.Length && villager.ActionCounts[action.Id] > 0) counts[action.Name] = villager.ActionCounts[action.Id];
                var history = new List<object[]>();
                foreach (var change in villager.RoleHistory) history.Add(new object[] { change.Tick, RoleId(change.Role) });
                result.Agents.Add(new RunAgentResult
                {
                    Index = i, Alive = agent.Alive, DiedTick = villager.DiedTick, DiedOf = villager.DiedOf, TicksAlive = villager.TicksAlive,
                    RewardTotal = (float)Math.Round(villager.RewardTotal, 4), Role = RoleId(agent.Role), RoleHistory = history, ActionCounts = counts,
                });
            }
            file.Result = result;
            file.EndedUtc = DateTime.UtcNow.ToString("o", Plain);
            file.WallSeconds = Math.Round(wall.Elapsed.TotalSeconds, 3);
            WriteRunFile();
            RunReport.Write(Folder, file, data);

            string index = Path.Combine(runsFolder, "index.csv");
            lock (FolderLock)
            using (var writer = new StreamWriter(index, true, new UTF8Encoding(false)))
            {
                bool fresh = writer.BaseStream.Length == 0;
                if (fresh) writer.WriteLine(IndexHeader);
                writer.WriteLine(string.Join(",", new[]
                {
                    RunId, file.StartedUtc, file.Setup.Seed.ToString(Plain), file.Setup.Agents.ToString(Plain), file.Setup.Brains,
                    result.Ticks.ToString(Plain), result.Days.ToString(Plain), alive.ToString(Plain), Stats.Get("deaths").ToString(Plain),
                    Stats.Get("built").ToString(Plain), Stats.Get("techs").ToString(Plain), result.Achievements.Count.ToString(Plain),
                    Stats.Get("reward_mean_per_agent_tick").ToString("0.#####", Plain), RunId,
                }));
            }
        }

        string RoleId(int role)
        {
            foreach (var def in data.Roles)
                if (def.Int == role) return def.Id;
            return role.ToString(Plain);
        }
    }
}
