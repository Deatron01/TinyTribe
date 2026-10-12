using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TinyTribe.Brains;
using TinyTribe.Sim.Core;

namespace TinyTribe.Tests
{
    /// <summary>
    /// Many villages stepped side by side for a trainer: actions in, what every villager sees and
    /// earns out, and a new village in the place of one that has ended.
    /// </summary>
    public class EnvPoolTests
    {
        static EnvOptions Options(int envs, int maxTicks, ulong seed = 11) => new EnvOptions { Envs = envs, MaxTicks = maxTicks, Seed = seed, MapWidth = 48, MapHeight = 48 };

        static byte[] Waiting(EnvPool pool, Simulation like)
        {
            var actions = new byte[pool.Envs * pool.Agents * 2];
            for (int i = 0; i < pool.Envs * pool.Agents; i++) actions[i * 2] = (byte)like.Actions.Wait;
            return actions;
        }

        static Simulation Alone(EnvOptions options, ulong seed)
        {
            var setup = options.SetupFor(seed);
            return Simulation.Create(Kit.Data, setup.Sim);
        }

        [Test]
        public void A_new_pool_shows_every_villager_of_every_village_its_first_view()
        {
            var options = Options(3, 100);
            var pool = new EnvPool(Kit.Data, options);
            var layout = pool.Layout;

            pool.Reset();

            Assert.AreEqual(3, pool.Envs);
            Assert.AreEqual(Kit.Data.ConfigInt("initial_agents"), pool.Agents);
            Assert.AreEqual(3 * pool.Agents * layout.GridLength, pool.Grid.Length);
            Assert.AreEqual(3 * pool.Agents * layout.VectorLength, pool.Vector.Length);
            // The second village is the one with the next seed; its third villager sees what that villager sees there.
            var sim = Alone(options, options.Seed + 1);
            var grid = new byte[layout.GridLength];
            var vector = new float[layout.VectorLength];
            sim.Observe(2, grid, vector);
            int row = 1 * pool.Agents + 2;
            CollectionAssert.AreEqual(grid, pool.Grid.Skip(row * layout.GridLength).Take(layout.GridLength).ToArray());
            CollectionAssert.AreEqual(vector, pool.Vector.Skip(row * layout.VectorLength).Take(layout.VectorLength).ToArray());
            Assert.IsTrue(pool.Alive.All(a => a == 1));
            Assert.IsTrue(pool.Done.All(d => d == 0));
            Assert.AreEqual(1, pool.ActionMask[row * layout.ActionCount + sim.Actions.Wait]);
        }

        [Test]
        public void A_step_plays_the_given_actions_in_every_village()
        {
            var options = Options(2, 100);
            var pool = new EnvPool(Kit.Data, options);
            pool.Reset();
            var sim = Alone(options, options.Seed);
            var actions = Waiting(pool, sim);
            // The first villager of the first village walks; everyone else waits.
            actions[0] = (byte)sim.Actions.MoveEast;
            var same = Enumerable.Range(0, pool.Agents).Select(i => new AgentAction(i == 0 ? sim.Actions.MoveEast : sim.Actions.Wait)).ToArray();

            for (int t = 0; t < 5; t++)
            {
                pool.Step(actions);
                sim.Step(same);
            }

            var layout = pool.Layout;
            var vector = new float[layout.VectorLength];
            var grid = new byte[layout.GridLength];
            sim.Observe(0, grid, vector);
            CollectionAssert.AreEqual(vector, pool.Vector.Take(layout.VectorLength).ToArray());
            CollectionAssert.AreEqual(grid, pool.Grid.Take(layout.GridLength).ToArray());
            sim.RewardPartsOf(0, out float own, out float village);
            Assert.AreEqual(own, pool.Reward[0]);
            Assert.AreEqual(village, pool.Reward[1]);
        }

        [Test]
        public void A_village_that_has_run_its_time_is_reported_and_replaced()
        {
            var options = Options(2, 30);
            var pool = new EnvPool(Kit.Data, options);
            pool.Reset();
            var actions = Waiting(pool, Alone(options, options.Seed));

            for (int t = 0; t < 29; t++) pool.Step(actions);
            Assert.IsTrue(pool.Done.All(d => d == 0));
            Assert.AreEqual(0, pool.Finished.Count);
            pool.Step(actions);

            Assert.IsTrue(pool.Done.All(d => d == 1), "the thirtieth tick was the last");
            Assert.AreEqual(2, pool.Finished.Count);
            var first = pool.Finished.OrderBy(f => f.Seed).First();
            Assert.AreEqual(options.Seed, first.Seed);
            Assert.AreEqual(30, first.Ticks);
            Assert.AreEqual("time_up", first.EndReason);
            Assert.AreEqual(pool.Agents, first.Alive);
            Assert.AreEqual(30, first.Metrics["ticks"]);
            // What is shown now is the first view of new villages, with seeds nobody has used yet.
            var next = Alone(options, options.Seed + 2);
            var vector = new float[pool.Layout.VectorLength];
            next.Observe(0, new byte[pool.Layout.GridLength], vector);
            CollectionAssert.AreEqual(vector, pool.Vector.Take(pool.Layout.VectorLength).ToArray());

            pool.Step(actions);
            Assert.IsTrue(pool.Done.All(d => d == 0));
            Assert.AreEqual(0, pool.Finished.Count, "reported once");
        }

        [Test]
        public void A_dead_villager_is_marked_and_shown_nothing()
        {
            var options = Options(1, 100000);
            var pool = new EnvPool(Kit.Data, options);
            pool.Reset();
            var actions = Waiting(pool, Alone(options, options.Seed));
            var doomed = pool.SimulationOf(0).Agents[2];
            doomed.Set(Need.Thirst, 0);
            doomed.Set(Need.Hp, 0.1f);

            pool.Step(actions);

            Assert.IsFalse(doomed.Alive);
            Assert.AreEqual(0, pool.Alive[2]);
            Assert.AreEqual(1, pool.Alive[1]);
            Assert.AreEqual(0, pool.Done[0], "the village goes on without it");
            var layout = pool.Layout;
            Assert.IsTrue(pool.Vector.Skip(2 * layout.VectorLength).Take(layout.VectorLength).All(v => v == 0f), "the dead see nothing");
            Assert.IsTrue(pool.ActionMask.Skip(2 * layout.ActionCount).Take(layout.ActionCount).All(m => m == 0), "the dead can do nothing");
            Assert.AreEqual(Kit.Data.Rewards.First(r => r.Id == "agent_death").Value, pool.Reward[2 * 2 + 1], "its last tick ends badly");
        }

        [Test]
        public void A_village_with_nobody_left_is_over()
        {
            var options = Options(1, 100000);
            var pool = new EnvPool(Kit.Data, options);
            pool.Reset();
            var actions = Waiting(pool, Alone(options, options.Seed));

            // Nobody eats or drinks.
            int guard = 0;
            while (pool.Finished.Count == 0 && guard++ < 20000) pool.Step(actions);

            Assert.AreEqual(1, pool.Finished.Count, "the village ended within " + guard + " ticks");
            Assert.AreEqual("everyone_dead", pool.Finished[0].EndReason);
            Assert.AreEqual(0, pool.Finished[0].Alive);
            Assert.AreEqual(pool.Agents, pool.Finished[0].Metrics["deaths"]);
            Assert.IsTrue(pool.Alive.All(a => a == 1), "a new village stands in its place");
        }

        [Test]
        public void Villages_ending_together_are_replaced_in_order_whatever_the_threads_do()
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                var options = Options(8, 3);
                options.Threads = 8;
                var pool = new EnvPool(Kit.Data, options);
                pool.Reset();
                var actions = Waiting(pool, Alone(options, options.Seed));
                for (int t = 0; t < 3; t++) pool.Step(actions);
                Assert.AreEqual(8, pool.Finished.Count);

                // The village in place e is now the one with seed first + 8 + e.
                var vector = new float[pool.Layout.VectorLength];
                var grid = new byte[pool.Layout.GridLength];
                for (int e = 0; e < 8; e++)
                {
                    Alone(options, options.Seed + 8 + (ulong)e).Observe(0, grid, vector);
                    CollectionAssert.AreEqual(vector, pool.Vector.Skip(e * pool.Agents * pool.Layout.VectorLength).Take(pool.Layout.VectorLength).ToArray(), "place " + e);
                }
            }
        }

        [Test]
        public void Staggered_villages_fall_out_of_step_and_stay_so()
        {
            var options = Options(4, 40);
            options.Stagger = true;
            var pool = new EnvPool(Kit.Data, options);
            pool.Reset();
            var actions = Waiting(pool, Alone(options, options.Seed));
            var ends = new System.Collections.Generic.List<(int tick, int place, long ticks, string reason)>();

            for (int t = 1; t <= 100; t++)
            {
                pool.Step(actions);
                foreach (var done in pool.Finished) ends.Add((t, done.Env, done.Ticks, done.EndReason));
            }

            // The first villages are cut at a quarter, a half, three quarters and the whole of the time; after that each runs its full time.
            CollectionAssert.AreEqual(new[] { (10, 0), (20, 1), (30, 2), (40, 3) }, ends.Take(4).Select(x => (x.tick, x.place)).ToArray());
            CollectionAssert.AreEqual(new[] { "staggered", "staggered", "staggered", "time_up" }, ends.Take(4).Select(x => x.reason).ToArray());
            CollectionAssert.AreEqual(new[] { (50, 0), (60, 1), (70, 2), (80, 3), (90, 0), (100, 1) }, ends.Skip(4).Select(x => (x.tick, x.place)).ToArray());
            Assert.IsTrue(ends.Skip(4).All(x => x.ticks == 40 && x.reason == "time_up"));
        }

        [Test]
        public void On_request_the_pool_says_what_the_scripted_bot_would_do()
        {
            var options = Options(2, 1000);
            options.Expert = true;
            options.ExpertRemembers = true;
            var pool = new EnvPool(Kit.Data, options);
            pool.Reset();
            // The same village played by the bot itself: while the pool plays the bot's advice, the two stay in step.
            var sim = Alone(options, options.Seed);
            var bot = new SurvivalBrain(options.Seed);
            var actions = new byte[pool.Envs * pool.Agents * 2];

            for (int t = 0; t < 60; t++)
            {
                var chosen = Enumerable.Range(0, pool.Agents).Select(i => bot.Decide(sim, i)).ToArray();
                for (int i = 0; i < pool.Agents; i++)
                {
                    Assert.AreEqual(chosen[i].Id, pool.Expert[i * 2], "action of villager " + i + " at tick " + t);
                    Assert.AreEqual(chosen[i].Arg, pool.Expert[i * 2 + 1], "argument of villager " + i + " at tick " + t);
                }
                Array.Copy(pool.Expert, actions, actions.Length);
                pool.Step(actions);
                sim.Step(chosen);
            }

            Assert.AreEqual(sim.ComputeStateHash(), pool.SimulationOf(0).ComputeStateHash());
        }

        [Test]
        public void Advice_that_is_followed_is_the_bots_own_play_also_without_its_memory()
        {
            var options = Options(1, 1000);
            options.Expert = true;
            options.ExpertRemembers = false;
            var pool = new EnvPool(Kit.Data, options);
            pool.Reset();
            var sim = Alone(options, options.Seed);
            var bot = new SurvivalBrain(options.Seed);
            var actions = new byte[pool.Envs * pool.Agents * 2];

            for (int t = 0; t < 200; t++)
            {
                var chosen = Enumerable.Range(0, pool.Agents).Select(i => bot.Decide(sim, i)).ToArray();
                Array.Copy(pool.Expert, actions, actions.Length);
                pool.Step(actions);
                sim.Step(chosen);
            }

            Assert.AreEqual(sim.ComputeStateHash(), pool.SimulationOf(0).ComputeStateHash());
        }

        [Test]
        public void Without_the_request_no_advice_is_given()
        {
            var pool = new EnvPool(Kit.Data, Options(1, 100));
            pool.Reset();

            Assert.IsTrue(pool.Expert.All(b => b == 0));
        }

        [Test]
        public void Villages_stepped_on_several_threads_come_out_the_same()
        {
            var one = Options(6, 10000);
            var many = Options(6, 10000);
            one.Threads = 1;
            many.Threads = 4;
            var a = new EnvPool(Kit.Data, one);
            var b = new EnvPool(Kit.Data, many);
            a.Reset();
            b.Reset();
            var rng = new System.Random(5);
            var actions = new byte[a.Envs * a.Agents * 2];

            for (int t = 0; t < 120; t++)
            {
                // Anything the masks allow, the same for both pools.
                for (int i = 0; i < a.Envs * a.Agents; i++)
                {
                    int action;
                    do action = rng.Next(a.Layout.ActionCount); while (a.ActionMask[i * a.Layout.ActionCount + action] == 0);
                    actions[i * 2] = (byte)action;
                    actions[i * 2 + 1] = 0;
                }
                a.Step(actions);
                b.Step(actions);
            }

            CollectionAssert.AreEqual(a.Vector, b.Vector);
            CollectionAssert.AreEqual(a.Grid, b.Grid);
            CollectionAssert.AreEqual(a.Reward, b.Reward);
            CollectionAssert.AreEqual(a.ArgMask, b.ArgMask);
        }

        [Test]
        public void A_pool_can_record_its_villages_like_any_other_run()
        {
            string folder = Path.Combine(Path.GetTempPath(), "tinytribe-tests", Guid.NewGuid().ToString("N"));
            var options = Options(2, 40);
            options.RecordFolder = folder;
            options.BrainsLabel = "test-policy";
            var pool = new EnvPool(Kit.Data, options);
            try
            {
                pool.Reset();
                var actions = Waiting(pool, Alone(options, options.Seed));
                for (int t = 0; t < 40; t++) pool.Step(actions);
                Assert.AreEqual(2, pool.Finished.Count);
                Assert.IsTrue(pool.Finished.All(f => f.RunId != null));
                string first = Path.Combine(folder, pool.Finished[0].RunId);
                // The villages that took their places are being recorded too; closing the pool ends those recordings.
                pool.Dispose();

                var runs = Directory.GetDirectories(folder).Select(RunReplay.Read).ToArray();
                Assert.AreEqual(4, runs.Length);
                Assert.AreEqual(2, runs.Count(r => r.Result.EndReason == "time_up"));
                Assert.AreEqual(2, runs.Count(r => r.Result.EndReason == "cut_off"));
                var run = RunReplay.Read(first);
                Assert.AreEqual("test-policy", run.Setup.Brains);
                Assert.AreEqual(40, run.Result.Ticks);
                RunReplay.Replay(Kit.Data, first, out string problem);
                Assert.IsNull(problem);
            }
            finally
            {
                pool.Dispose();
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }
    }
}
