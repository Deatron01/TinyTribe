using System.Linq;
using NUnit.Framework;
using TinyTribe.Brains;
using TinyTribe.Sim.Core;

namespace TinyTribe.Tests
{
    /// <summary>
    /// How the bots share out the jobs: the best villager for each, only the jobs there is work for,
    /// and shared out again whenever the Council reviews them (rule R41).
    /// </summary>
    public class BotJobTests
    {
        const int VotingTime = 60;

        static SimRunner Bots(Simulation sim) =>
            new SimRunner(sim, new SurvivalBrain(1)) { MaxMillisecondsPerAdvance = double.MaxValue };

        static int Role(string roleId) => Kit.Data.RoleInt(roleId);

        static AgentStats Stats(int str, int dex, int intelligence, int end)
        {
            var stats = Kit.Neutral;
            stats.Str = str;
            stats.Dex = dex;
            stats.Int = intelligence;
            stats.End = end;
            return stats;
        }

        /// <summary>Open ground and a village that reviews its jobs.</summary>
        static Simulation Field()
        {
            var sim = Kit.NewSim(40, 16);
            sim.JobReviewsEnabled = true;
            sim.World.SpawnX = 20;
            sim.World.SpawnY = 8;
            return sim;
        }

        static void People(Simulation sim, params AgentStats[] stats)
        {
            for (int i = 0; i < stats.Length; i++) sim.AddAgent(8 + i * 3, 12, stats[i]);
        }

        static AgentStats[] Alike(int count) => Enumerable.Repeat(Kit.Neutral, count).ToArray();

        /// <summary>Runs the bots, well fed, long enough for a round of votes.</summary>
        static void Hold(SimRunner runner, int ticks = VotingTime)
        {
            for (int t = 0; t < ticks; t++)
            {
                foreach (var agent in runner.Sim.Agents)
                    if (agent.Alive) Kit.Refill(agent);
                runner.StepOnce();
            }
        }

        static int Holders(Simulation sim, string roleId) => sim.Agents.Count(a => a.Alive && a.Role == Role(roleId));

        static string[] Jobs(Simulation sim) => sim.Agents.Select(a => Kit.Data.Roles.First(r => r.Int == a.Role).Id).ToArray();

        [Test]
        public void With_nothing_to_farm_or_study_nobody_is_made_farmer_or_scholar()
        {
            var sim = Field();
            People(sim, Alike(6));

            Hold(Bots(sim));

            CollectionAssert.DoesNotContain(sim.Agents.Select(a => a.Role).ToArray(), 0, "everyone has a job");
            Assert.AreEqual(0, Holders(sim, "farmer"), "no farm");
            Assert.AreEqual(0, Holders(sim, "scholar"), "nowhere to study");
            Assert.AreEqual(1, Holders(sim, "builder"));
            Assert.AreEqual(1, Holders(sim, "lumberjack"));
            Assert.AreEqual(6, sim.Agents.Select(a => a.Role).Distinct().Count(), "six villagers, six different jobs");
        }

        [Test]
        public void A_garden_gets_the_village_a_farmer()
        {
            var sim = Field();
            People(sim, Alike(6));
            var runner = Bots(sim);
            Hold(runner);
            int builder = sim.Agents.First(a => a.Role == Role("builder")).Index;

            sim.PlaceBuilding("garden_plot", 30, 4);
            Hold(runner);

            Assert.AreEqual(1, Holders(sim, "farmer"), string.Join(" ", Jobs(sim)));
            Assert.AreEqual(Role("builder"), sim.Agents[builder].Role, "the builder goes on building");
            Assert.AreEqual(6, sim.Agents.Select(a => a.Role).Distinct().Count());
        }

        [Test]
        public void A_place_of_study_makes_the_cleverest_villager_the_scholar()
        {
            var sim = Field();
            var clever = Kit.Neutral;
            clever.Int = 10;
            // First in line, so the first votes make the clever one the builder: everyone builds equally well.
            People(sim, new[] { clever }.Concat(Alike(5)).ToArray());
            var runner = Bots(sim);
            Hold(runner);
            Assert.AreEqual(0, Holders(sim, "scholar"));

            sim.PlaceBuilding("study_circle", 30, 4);
            Hold(runner);

            Assert.AreEqual(Role("scholar"), sim.Agents[0].Role, string.Join(" ", Jobs(sim)));
            Assert.AreEqual(1, Holders(sim, "builder"), "and someone else builds");
        }

        [Test]
        public void Every_job_goes_to_the_villager_best_at_it()
        {
            var sim = Field();
            sim.JobReviewsEnabled = false;
            sim.PlaceBuilding("garden_plot", 30, 4);
            sim.PlaceBuilding("study_circle", 33, 8);
            sim.JobReviewsEnabled = true;
            People(sim,
                Stats(str: 3, dex: 3, intelligence: 10, end: 3),   // thinker
                Stats(str: 3, dex: 3, intelligence: 8, end: 10),   // patient and bright
                Stats(str: 5, dex: 10, intelligence: 2, end: 8),   // deft, and strong enough to carry a builder's load
                Stats(str: 10, dex: 2, intelligence: 2, end: 10),  // strong and tireless
                Stats(str: 9, dex: 9, intelligence: 2, end: 3),    // strong and quick
                Stats(str: 8, dex: 3, intelligence: 3, end: 7));   // sturdy

            Hold(Bots(sim));

            CollectionAssert.AreEqual(new[] { "scholar", "farmer", "builder", "lumberjack", "butcher", "miner" }, Jobs(sim));
        }

        [Test]
        public void When_the_builder_dies_the_best_of_the_rest_takes_over()
        {
            var sim = Field();
            People(sim, Stats(5, 10, 5, 10), Stats(5, 9, 5, 9), Kit.Neutral);
            var runner = Bots(sim);
            Hold(runner);
            Assert.AreEqual(Role("builder"), sim.Agents[0].Role);
            Assert.AreNotEqual(Role("builder"), sim.Agents[1].Role);

            sim.Agents[0].Set(Need.Hunger, 0);
            sim.Agents[0].Set(Need.Hp, 0.1f);
            runner.StepOnce();
            Assert.IsFalse(sim.Agents[0].Alive);
            Hold(runner);

            Assert.AreEqual(Role("builder"), sim.Agents[1].Role, string.Join(" ", Jobs(sim)));
            Assert.AreEqual(Role("lumberjack"), sim.Agents[2].Role, "and the wood still gets cut");
        }

        [Test]
        public void With_nothing_left_to_study_the_scholar_takes_other_work_until_there_is_a_library()
        {
            var sim = Field();
            sim.JobReviewsEnabled = false;
            sim.PlaceBuilding("study_circle", 30, 4);
            sim.JobReviewsEnabled = true;
            People(sim, Alike(6));
            var runner = Bots(sim);
            Hold(runner);
            Assert.AreEqual(1, Holders(sim, "scholar"));

            foreach (var tech in Kit.Data.Tech.Where(t => t.NeedsLibrary == 0)) sim.Research(tech.Id);
            Hold(runner, Kit.Data.ConfigInt("job_review_gap_ticks") + VotingTime);
            Assert.AreEqual(0, Holders(sim, "scholar"), "the circle has taught all it can");

            sim.PlaceBuilding("library", 30, 9);
            Hold(runner, Kit.Data.ConfigInt("job_review_gap_ticks") + VotingTime);
            Assert.AreEqual(1, Holders(sim, "scholar"), "the library has more to teach");
        }

        [Test]
        public void A_review_with_nothing_changed_changes_nobodys_job()
        {
            var sim = Field();
            People(sim, Stats(6, 4, 5, 7), Stats(4, 6, 7, 5), Stats(5, 5, 5, 5), Stats(7, 3, 4, 6), Stats(3, 7, 6, 4), Stats(5, 6, 4, 5));
            var runner = Bots(sim);
            Hold(runner);
            var before = Jobs(sim);

            sim.CallJobReview();
            int appointments = 0, votes = 0;
            for (int t = 0; t < Kit.Data.ConfigInt("job_review_gap_ticks") + VotingTime; t++)
            {
                foreach (var agent in sim.Agents) Kit.Refill(agent);
                runner.StepOnce();
                appointments += sim.Events.Count(e => e.Type == SimEventType.RoleAssigned);
                votes += sim.Events.Count(e => e.Type == SimEventType.VoteClosed);
            }

            Assert.AreEqual(6, votes, "everyone was voted on");
            Assert.AreEqual(0, appointments);
            CollectionAssert.AreEqual(before, Jobs(sim));
        }
    }
}
