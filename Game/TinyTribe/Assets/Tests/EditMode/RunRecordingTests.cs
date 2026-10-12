using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TinyTribe.Brains;
using TinyTribe.Sim.Core;

namespace TinyTribe.Tests
{
    /// <summary>
    /// A run's counters and achievements (Sim Achievements), and the folder a run is recorded into
    /// (Sim Run Log Schema, rule R43): enough to play the whole run again, tick for tick.
    /// </summary>
    public class RunRecordingTests
    {
        string folder;

        [SetUp]
        public void MakeFolder()
        {
            folder = Path.Combine(Path.GetTempPath(), "tinytribe-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
        }

        [TearDown]
        public void RemoveFolder()
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }

        static GameSetup BotGame(ulong seed = 1)
        {
            var setup = new GameSetup { Brains = BrainKind.Survival };
            setup.Sim.Seed = seed;
            return setup;
        }

        /// <summary>A bot village recorded for that many ticks and finished.</summary>
        (SimRunner runner, RunRecorder recorder) Recorded(int ticks, ulong seed = 1)
        {
            var setup = BotGame(seed);
            var runner = SimRunner.Create(Kit.Data, setup);
            runner.MaxMillisecondsPerAdvance = double.MaxValue;
            var recorder = RunRecorder.Start(folder, Kit.Data, runner.Sim, setup);
            runner.Recorder = recorder;
            for (int t = 0; t < ticks; t++) runner.StepOnce();
            recorder.Finish("stopped");
            return (runner, recorder);
        }

        static AgentAction Wait(Simulation sim) => new AgentAction(sim.Actions.Wait);

        // ------------------------------------------------------------------ counters and achievements

        [Test]
        public void Stats_count_what_was_gathered_made_and_built()
        {
            var (sim, agent) = Village.One();
            sim.World.PlaceNode(5, 4, Kit.Data.NodeById("berry_bush"));
            var stats = new RunStats(Kit.Data, sim);
            int gathered = 0;

            for (int t = 0; t < 40 && gathered == 0; t++)
            {
                Kit.Refill(agent);
                var actions = new[] { new AgentAction(sim.Actions.Harvest) };
                sim.Step(actions);
                stats.AfterTick(sim, actions);
                gathered += sim.Events.Where(e => e.Type == SimEventType.Harvested).Sum(e => e.Qty);
            }
            sim.World.RemoveNode(sim.World.Index(5, 4));
            var chest = Kit.Data.BuildingById["basic_chest"];
            Village.Give(sim, agent, chest.CostItem1, chest.CostQty1);
            for (int t = 0; t < chest.BuildTicks + 5 && (sim.BuildingAt(5, 4) == null || !sim.BuildingAt(5, 4).Complete); t++)
            {
                Kit.Refill(agent);
                var actions = new[] { Village.Craft(sim, "basic_chest") };
                sim.Step(actions);
                stats.AfterTick(sim, actions);
            }

            Assert.Greater(gathered, 0);
            Assert.AreEqual(gathered, stats.Get("gathered.berry"));
            Assert.AreEqual(gathered, stats.Get("gathered"));
            Assert.AreEqual(1, stats.Get("built.basic_chest"));
            Assert.AreEqual(1, stats.Get("built"));
            Assert.AreEqual(sim.Tick, stats.Get("ticks"));
            Assert.AreEqual(0, stats.Get("deaths"));
            Assert.AreEqual(0, stats.Get("never.heard.of.it"), "a counter nothing has added to is zero");
        }

        [Test]
        public void An_achievement_is_reached_once_on_the_tick_its_counter_gets_there()
        {
            var (sim, agent) = Village.One();
            var stats = new RunStats(Kit.Data, sim);
            Assert.IsFalse(stats.HasAchieved("storage"));
            var chest = Kit.Data.BuildingById["basic_chest"];
            Village.Give(sim, agent, chest.CostItem1, chest.CostQty1 * 2);

            long finished = -1;
            for (int t = 0; t < chest.BuildTicks + 5 && finished < 0; t++)
            {
                Kit.Refill(agent);
                var actions = new[] { Village.Craft(sim, "basic_chest") };
                sim.Step(actions);
                stats.AfterTick(sim, actions);
                if (sim.BuildingAt(5, 4).Complete) finished = sim.Tick;
            }

            Assert.IsTrue(stats.HasAchieved("storage"));
            var reached = stats.Achievements.Single(a => a.Def.Id == "storage");
            Assert.AreEqual(finished, reached.Tick);
            Assert.AreEqual("Somewhere to Put Things", reached.Def.Name);
            Assert.IsFalse(stats.HasAchieved("hearth"));
        }

        [Test]
        public void Stats_tell_a_death_and_what_it_was_of()
        {
            var sim = Kit.NewSim(12, 9);
            var doomed = sim.AddAgent(2, 4, Kit.Neutral);
            sim.AddAgent(8, 4, Kit.Neutral);
            var stats = new RunStats(Kit.Data, sim);
            doomed.Set(Need.Thirst, 0);
            doomed.Set(Need.Hp, 0.1f);
            var actions = new[] { Wait(sim), Wait(sim) };

            sim.Step(actions);
            stats.AfterTick(sim, actions);

            Assert.AreEqual(1, stats.Get("deaths"));
            Assert.AreEqual(1, stats.Get("deaths.thirst"));
            Assert.AreEqual(1, stats.Get("alive"));
            Assert.AreEqual(1, stats.Get("min_alive"));
            Assert.AreEqual(sim.Tick, stats.Agents[doomed.Index].DiedTick);
            Assert.AreEqual("thirst", stats.Agents[doomed.Index].DiedOf);
            Assert.AreEqual(2 * Kit.Data.Rewards.First(r => r.Id == "agent_death").Value, stats.Get("reward_total"), 1e-4);
        }

        [Test]
        public void Stats_follow_the_jobs_the_council_gives_and_changes()
        {
            var sim = Kit.NewSim(12, 9);
            sim.JobReviewsEnabled = true;
            sim.AddAgent(2, 4, Kit.Neutral);
            var stats = new RunStats(Kit.Data, sim);
            void Step(AgentAction action)
            {
                Kit.Refill(sim.Agents[0]);
                var actions = new[] { action };
                sim.Step(actions);
                stats.AfterTick(sim, actions);
            }

            Step(Wait(sim));
            Step(new AgentAction(sim.Actions.Vote, Kit.Data.RoleInt("miner")));
            Assert.AreEqual(1, stats.Get("roles_assigned"));
            Assert.AreEqual(0, stats.Get("role_changes"));
            Assert.AreEqual(1, stats.Get("all_have_jobs"));
            Assert.IsTrue(stats.HasAchieved("jobs_for_all"));

            sim.CallJobReview();
            for (int t = 0; t < Kit.Data.ConfigInt("job_review_gap_ticks") + 2 && sim.OpenVote == null; t++) Step(Wait(sim));
            Step(new AgentAction(sim.Actions.Vote, Kit.Data.RoleInt("builder")));

            Assert.AreEqual(1, stats.Get("role_changes"));
            Assert.IsTrue(stats.HasAchieved("changed_mind"));
            CollectionAssert.AreEqual(new[] { Kit.Data.RoleInt("miner"), Kit.Data.RoleInt("builder") }, stats.Agents[0].RoleHistory.Select(r => r.Role).ToArray());
            Assert.AreEqual(2, stats.Agents[0].ActionCounts[sim.Actions.Vote]);
        }

        [Test]
        public void Every_achievement_is_a_counter_the_stats_keep()
        {
            Assert.GreaterOrEqual(Kit.Data.Achievements.Count, 30);
            CollectionAssert.AllItemsAreUnique(Kit.Data.Achievements.Select(a => a.Id).ToArray());
            foreach (var achievement in Kit.Data.Achievements)
            {
                Assert.IsTrue(RunStats.IsMetric(Kit.Data, achievement.Metric), achievement.Id + " counts '" + achievement.Metric + "', which no run keeps");
                Assert.Greater(achievement.AtLeast, 0, achievement.Id);
                Assert.IsNotEmpty(achievement.Name);
            }
            Assert.IsFalse(RunStats.IsMetric(Kit.Data, "built.no_such_building"));
            Assert.IsFalse(RunStats.IsMetric(Kit.Data, "happiness"));
        }

        // ------------------------------------------------------------------ the folder of a run

        [Test]
        public void A_recorded_run_leaves_its_files_and_who_what_and_how()
        {
            var (runner, recorder) = Recorded(300);
            var sim = runner.Sim;

            foreach (string file in new[] { "run.json", "actions.bin", "events.bin", "hashes.bin", "timeline.csv", "agents.csv" })
                Assert.IsTrue(File.Exists(Path.Combine(recorder.Folder, file)), file);
            Assert.AreEqual(folder, Path.GetDirectoryName(recorder.Folder));

            var run = RunReplay.Read(recorder.Folder);
            Assert.AreEqual(1, run.Schema);
            Assert.AreEqual(recorder.RunId, run.RunId);
            Assert.AreEqual(1UL, run.Setup.Seed);
            Assert.AreEqual("Survival", run.Setup.Brains);
            Assert.AreEqual(sim.Agents.Count, run.Setup.Agents);
            Assert.AreEqual(sim.World.Width, run.World.Width);
            Assert.AreEqual(sim.JobReviewsEnabled, run.Setup.JobReviews);
            Assert.AreEqual(Kit.Data.WorkbookSha256, run.Data.WorkbookSha256);
            Assert.IsNotEmpty(run.Data.WorkbookSha256);
            Assert.AreEqual(sim.Agents.Count, run.Villagers.Count);
            Assert.AreEqual(sim.Agents[2].Stats.Str, run.Villagers[2].Str);
            Assert.IsNotNull(run.StartedUtc);
            Assert.IsNotNull(run.EndedUtc);

            Assert.AreEqual(300, run.Result.Ticks);
            Assert.AreEqual("stopped", run.Result.EndReason);
            Assert.AreEqual(sim.Agents.Count(a => a.Alive), run.Result.Alive);
            Assert.AreEqual(sim.ComputeStateHash().ToString(), run.Result.FinalStateHash);
            Assert.AreEqual(300, run.Result.Metrics["ticks"]);
            Assert.AreEqual(sim.Agents.Count, run.Result.Agents.Count);
            Assert.AreEqual(300, run.Result.Agents[0].ActionCounts.Values.Sum(), "one action a tick");
            Assert.IsTrue(run.Result.Achievements.Any(a => a.Id == "jobs_for_all"), "six bots vote six jobs on the first morning");
        }

        [Test]
        public void The_action_log_is_two_bytes_a_villager_a_tick()
        {
            var (runner, recorder) = Recorded(200);
            int agents = runner.Sim.Agents.Count;

            long size = new FileInfo(Path.Combine(recorder.Folder, "actions.bin")).Length;

            Assert.AreEqual(8 + 200 * (1 + 2 * agents), size);
        }

        [Test]
        public void The_event_log_holds_every_event_of_every_tick()
        {
            var setup = BotGame();
            var runner = SimRunner.Create(Kit.Data, setup);
            var recorder = RunRecorder.Start(folder, Kit.Data, runner.Sim, setup);
            runner.Recorder = recorder;
            int events = 0, harvests = 0;
            for (int t = 0; t < 250; t++)
            {
                runner.StepOnce();
                events += runner.Sim.Events.Count;
                harvests += runner.Sim.Events.Count(e => e.Type == SimEventType.Harvested);
            }
            recorder.Finish("stopped");

            var logged = RunReplay.ReadEvents(recorder.Folder);

            Assert.Greater(events, 0);
            Assert.AreEqual(events, logged.Count);
            Assert.AreEqual(8 + 18L * events, new FileInfo(Path.Combine(recorder.Folder, "events.bin")).Length);
            Assert.AreEqual(harvests, logged.Count(e => e.Event.Type == SimEventType.Harvested));
            Assert.IsTrue(logged.Zip(logged.Skip(1), (a, b) => a.Tick <= b.Tick).All(inOrder => inOrder));
            Assert.LessOrEqual(logged.Last().Tick, 250);
        }

        [Test]
        public void The_timeline_has_a_row_for_the_village_and_each_villager_at_every_sample()
        {
            var (runner, recorder) = Recorded(300);
            int every = Kit.Data.ConfigInt("run_timeline_every_ticks");

            var village = File.ReadAllLines(Path.Combine(recorder.Folder, "timeline.csv"));
            var villagers = File.ReadAllLines(Path.Combine(recorder.Folder, "agents.csv"));

            StringAssert.StartsWith("tick,day,hour,season,weather,alive,", village[0]);
            Assert.AreEqual(1 + 300 / every, village.Length);
            Assert.AreEqual(village[0].Split(',').Length, village[1].Split(',').Length, "a decimal comma would split a number in two");
            StringAssert.StartsWith(every + ",", village[1]);
            StringAssert.StartsWith("tick,agent,alive,x,y,role,", villagers[0]);
            Assert.AreEqual(1 + 300 / every * runner.Sim.Agents.Count, villagers.Length);
            Assert.AreEqual(villagers[0].Split(',').Length, villagers[1].Split(',').Length);
        }

        [Test]
        public void A_recorded_run_plays_again_to_the_very_same_state()
        {
            var (runner, recorder) = Recorded(600, seed: 3);

            var again = RunReplay.Replay(Kit.Data, recorder.Folder, out string problem);

            Assert.IsNull(problem);
            Assert.AreEqual(600, again.Tick);
            Assert.AreEqual(runner.Sim.ComputeStateHash(), again.ComputeStateHash());
            Assert.AreEqual(runner.Sim.Agents.Select(a => (a.X, a.Y, a.Role)).ToArray(), again.Agents.Select(a => (a.X, a.Y, a.Role)).ToArray());
        }

        [Test]
        public void A_replay_can_stop_at_any_tick_and_be_looked_at()
        {
            var (_, recorder) = Recorded(400);
            int seen = 0;

            var early = RunReplay.Replay(Kit.Data, recorder.Folder, out string problem, untilTick: 150, afterTick: _ => seen++);

            Assert.IsNull(problem);
            Assert.AreEqual(150, early.Tick);
            Assert.AreEqual(150, seen);
        }

        [Test]
        public void A_replay_that_goes_another_way_is_caught()
        {
            var (runner, recorder) = Recorded(600);
            // Someone edited the log: the first villager stands still through the second morning instead of what it did.
            string path = Path.Combine(recorder.Folder, "actions.bin");
            var bytes = File.ReadAllBytes(path);
            int agents = bytes[8];
            int record = 1 + 2 * agents;
            for (int tick = 250; tick < 420; tick++)
            {
                bytes[8 + tick * record + 1] = (byte)runner.Sim.Actions.Wait;
                bytes[8 + tick * record + 2] = 0;
            }
            File.WriteAllBytes(path, bytes);

            RunReplay.Replay(Kit.Data, recorder.Folder, out string problem);

            Assert.IsNotNull(problem);
            StringAssert.Contains("tick", problem);
        }

        [Test]
        public void Finished_runs_are_listed_in_the_index_of_their_folder()
        {
            Recorded(120, seed: 1);
            Recorded(120, seed: 2);

            var index = File.ReadAllLines(Path.Combine(folder, "index.csv"));

            StringAssert.StartsWith("run_id,started_utc,seed,agents,brains,ticks,days,alive,deaths,built,techs,achievements,reward_mean,folder", index[0]);
            Assert.AreEqual(3, index.Length);
            Assert.AreEqual("1", index[1].Split(',')[2]);
            Assert.AreEqual("2", index[2].Split(',')[2]);
            Assert.AreEqual("120", index[2].Split(',')[5]);
        }

        [Test]
        public void A_run_that_was_cut_off_still_says_what_it_was()
        {
            var setup = BotGame();
            var runner = SimRunner.Create(Kit.Data, setup);
            var recorder = RunRecorder.Start(folder, Kit.Data, runner.Sim, setup);
            runner.Recorder = recorder;
            for (int t = 0; t < 50; t++) runner.StepOnce();

            var run = RunReplay.Read(recorder.Folder);

            Assert.AreEqual(1UL, run.Setup.Seed);
            Assert.IsNull(run.EndedUtc);
            Assert.IsNull(run.Result);
            recorder.Finish("stopped");
        }

        [Test]
        public void A_finished_run_has_a_report_a_person_can_read()
        {
            var (runner, recorder) = Recorded(600);

            string report = File.ReadAllText(Path.Combine(recorder.Folder, "report.md"));

            StringAssert.Contains(recorder.RunId, report);
            StringAssert.Contains("seed 1", report);
            StringAssert.Contains("## Achievements", report);
            StringAssert.Contains("Everyone Has a Job", report);
            StringAssert.Contains("## Villagers", report);
            StringAssert.Contains("| V" + runner.Sim.Agents.Count + " |", report);
            StringAssert.Contains("## Counters", report);
            StringAssert.Contains("gathered", report);
            StringAssert.Contains("## Not reached", report);
        }

        [Test]
        public void A_batch_plays_and_records_one_game_per_seed()
        {
            int day = Kit.Data.ConfigInt("ticks_per_day");

            var folders = RunBatch.Run(Kit.Data, folder, new ulong[] { 5, 6 }, days: 1);

            Assert.AreEqual(2, folders.Count);
            var first = RunReplay.Read(folders[0]);
            var second = RunReplay.Read(folders[1]);
            Assert.AreEqual(5UL, first.Setup.Seed);
            Assert.AreEqual(6UL, second.Setup.Seed);
            Assert.AreEqual(day, first.Result.Ticks);
            Assert.AreEqual("time_up", first.Result.EndReason);
            Assert.AreEqual(3, File.ReadAllLines(Path.Combine(folder, "index.csv")).Length);
            Assert.IsNull(Replayed(folders[1]), "a batch run replays like any other");
        }

        string Replayed(string runFolder)
        {
            RunReplay.Replay(Kit.Data, runFolder, out string problem);
            return problem;
        }

        [Test]
        public void Recording_costs_about_a_megabyte_a_year()
        {
            var (_, recorder) = Recorded(Kit.Data.ConfigInt("ticks_per_day") * 7);

            long bytes = Directory.GetFiles(recorder.Folder).Sum(f => new FileInfo(f).Length);

            Assert.Less(bytes, 600_000, "one season of six villagers");
            Assert.Pass(bytes / 1024 + " KB for a season of six villagers");
        }
    }
}
