using System.Linq;
using NUnit.Framework;
using TinyTribe.Sim.Core;

namespace TinyTribe.Tests
{
    /// <summary>The Council goes over the village's jobs again as the village changes (Sim Rules R41).</summary>
    public class JobReviewTests
    {
        static AgentAction Wait(Simulation sim) => new AgentAction(sim.Actions.Wait);

        static AgentAction VoteFor(Simulation sim, string roleId) => new AgentAction(sim.Actions.Vote, Kit.Data.RoleInt(roleId));

        static int Role(string roleId) => Kit.Data.RoleInt(roleId);

        /// <summary>Villagers who have their jobs already, in a village that reviews them.</summary>
        static Simulation Village(params string[] roles)
        {
            var sim = Kit.NewSim(16, 9);
            sim.JobReviewsEnabled = true;
            for (int i = 0; i < roles.Length; i++) sim.SetRole(sim.AddAgent(2 + i * 2, 4, Kit.Neutral), roles[i]);
            return sim;
        }

        /// <summary>One tick with everyone fed; whoever is given no action waits.</summary>
        static void Step(Simulation sim, params AgentAction[] actions)
        {
            foreach (var agent in sim.Agents) Kit.Refill(agent);
            sim.Step(actions.Length > 0 ? actions : sim.Agents.Select(_ => Wait(sim)).ToArray());
        }

        static void AllVote(Simulation sim, string roleId) => Step(sim, sim.Agents.Select(_ => VoteFor(sim, roleId)).ToArray());

        static bool ReviewOpen(Simulation sim) => sim.OpenVote != null && sim.OpenVote.Kind == VoteKind.Role && sim.OpenVote.Review;

        [Test]
        public void Nobody_is_voted_on_again_without_a_reason()
        {
            var sim = Village("builder", "miner");

            for (int t = 0; t < 100; t++)
            {
                Step(sim);
                Assert.IsNull(sim.OpenVote);
            }
        }

        [Test]
        public void A_review_puts_every_living_villager_to_the_vote_in_turn()
        {
            var sim = Village("builder", "miner", "cook");
            sim.CallJobReview();

            Step(sim);
            Assert.IsTrue(ReviewOpen(sim));
            Assert.AreEqual(0, sim.OpenVote.Subject);
            Assert.IsTrue(sim.IsValid(1, VoteFor(sim, "scholar")));
            AllVote(sim, "scholar");
            Assert.AreEqual(Role("scholar"), sim.Agents[0].Role, "the Council changed its mind about its builder");
            Assert.IsTrue(sim.Events.Any(e => e.Type == SimEventType.RoleAssigned && e.Agent == 0 && e.Item == Role("scholar")));
            Assert.AreEqual(1, sim.OpenVote.Subject);
            AllVote(sim, "builder");
            Assert.AreEqual(2, sim.OpenVote.Subject);
            AllVote(sim, "cook");

            Assert.IsNull(sim.OpenVote);
            CollectionAssert.AreEqual(new[] { "scholar", "builder", "cook" }.Select(Role).ToArray(), sim.Agents.Select(a => a.Role).ToArray());
        }

        [Test]
        public void A_review_vote_that_decides_nothing_leaves_the_job_as_it_is()
        {
            var sim = Village("builder", "miner");
            sim.CallJobReview();
            Step(sim);

            Step(sim, VoteFor(sim, "cook"), VoteFor(sim, "scout"));

            Assert.AreEqual(Role("builder"), sim.Agents[0].Role, "a tie");
            Assert.AreEqual(1, sim.OpenVote.Subject, "and the review moves on");
            for (int t = 0; t < Kit.Data.ConfigInt("vote_duration_ticks"); t++) Step(sim);
            Assert.AreEqual(Role("miner"), sim.Agents[1].Role, "nobody voted");
            Assert.IsNull(sim.OpenVote);
        }

        [Test]
        public void Keeping_a_job_is_not_a_new_appointment()
        {
            var sim = Village("builder");
            sim.CallJobReview();
            Step(sim);

            AllVote(sim, "builder");

            Assert.IsTrue(sim.Events.Any(e => e.Type == SimEventType.VoteClosed && e.Qty == Role("builder")));
            Assert.IsFalse(sim.Events.Any(e => e.Type == SimEventType.RoleAssigned));
            Assert.AreEqual(Role("builder"), sim.Agents[0].Role);
        }

        [Test]
        public void A_finished_building_calls_a_review()
        {
            var sim = Village("builder", "miner");
            Step(sim);
            Assert.IsNull(sim.OpenVote);

            sim.PlaceBuilding("garden_plot", 8, 7);
            Step(sim);

            Assert.IsTrue(ReviewOpen(sim), "a garden wants a farmer");
        }

        [Test]
        public void A_building_lost_calls_a_review()
        {
            var sim = Village("builder", "farmer");
            sim.JobReviewsEnabled = false;
            var garden = sim.PlaceBuilding("garden_plot", 8, 7);
            sim.JobReviewsEnabled = true;
            Step(sim);
            Assert.IsNull(sim.OpenVote);

            sim.DestroyBuilding(garden);
            Step(sim);

            Assert.IsTrue(ReviewOpen(sim), "a farmer without a farm");
        }

        [Test]
        public void A_technology_learned_calls_a_review()
        {
            var sim = Village("builder", "miner");
            Step(sim);

            sim.Research("carpentry");
            Step(sim);

            Assert.IsTrue(ReviewOpen(sim));
        }

        [Test]
        public void The_death_of_someone_with_a_job_calls_a_review()
        {
            var sim = Village("builder", "miner");
            Step(sim);
            var doomed = sim.Agents[0];
            doomed.Set(Need.Hunger, 0);
            doomed.Set(Need.Hp, 0.1f);

            sim.Step(new[] { Wait(sim), Wait(sim) });

            Assert.IsFalse(doomed.Alive);
            Assert.IsTrue(ReviewOpen(sim));
            Assert.AreEqual(1, sim.OpenVote.Subject, "the dead are not voted on; the miner may be needed as the builder now");
        }

        [Test]
        public void The_turn_of_the_season_calls_a_review()
        {
            var sim = Village("builder");
            string season = sim.Season.Id;

            int guard = 0;
            while (sim.Season.Id == season && guard++ < 5000)
            {
                Assert.IsNull(sim.OpenVote, "no review in the same season");
                Step(sim);
            }

            Assert.IsTrue(ReviewOpen(sim));
        }

        [Test]
        public void Reviews_keep_their_distance()
        {
            var sim = Village("builder");
            int gap = Kit.Data.ConfigInt("job_review_gap_ticks");
            sim.CallJobReview();
            Step(sim);
            AllVote(sim, "builder");
            Assert.IsNull(sim.OpenVote);

            sim.CallJobReview();
            int waited = 0;
            while (sim.OpenVote == null && waited < gap * 2)
            {
                Step(sim);
                waited++;
            }

            Assert.IsTrue(ReviewOpen(sim), "the reason is kept, and the review held when the gap is over");
            Assert.GreaterOrEqual(waited, gap - 2);
        }

        [Test]
        public void First_votes_come_before_a_review()
        {
            var sim = Village("builder");
            sim.AddAgent(10, 4, Kit.Neutral);
            sim.CallJobReview();

            Step(sim);
            Assert.AreEqual(1, sim.OpenVote.Subject, "the newcomer has no job at all");
            Assert.IsFalse(sim.OpenVote.Review);
            AllVote(sim, "miner");

            Assert.IsTrue(ReviewOpen(sim));
            Assert.AreEqual(0, sim.OpenVote.Subject);
        }

        [Test]
        public void With_reviews_off_a_job_once_given_is_kept()
        {
            var sim = Village("builder", "miner");
            sim.JobReviewsEnabled = false;
            sim.PlaceBuilding("garden_plot", 8, 7);
            sim.Research("carpentry");

            for (int t = 0; t < 30; t++)
            {
                Step(sim);
                Assert.IsNull(sim.OpenVote);
            }
        }

        [Test]
        public void A_review_that_is_due_is_part_of_the_state()
        {
            var called = Village("builder");
            var quiet = Village("builder");

            called.CallJobReview();

            Assert.AreNotEqual(called.ComputeStateHash(), quiet.ComputeStateHash());
        }

        [Test]
        public void A_new_game_reviews_its_jobs_unless_told_not_to()
        {
            Assert.IsTrue(Simulation.Create(Kit.Data, new SimulationOptions { Seed = 3 }).JobReviewsEnabled);
            Assert.IsFalse(Simulation.Create(Kit.Data, new SimulationOptions { Seed = 3, JobReviews = false }).JobReviewsEnabled);
        }
    }
}
