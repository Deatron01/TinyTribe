using System.Linq;
using NUnit.Framework;
using TinyTribe.Sim.Core;
using TinyTribe.Sim.Data;

namespace TinyTribe.Tests
{
    /// <summary>
    /// What a villager is paid for a tick (Sim Rewards, rule R42). Outcome rewards say how it and the
    /// village are doing; shaping rewards pay for one particular action and are off unless the scale is raised.
    /// </summary>
    public class RewardTests
    {
        const float Tolerance = 1e-4f;

        static float Value(string rewardId) => Kit.Data.Rewards.First(r => r.Id == rewardId).Value;

        static AgentAction Wait(Simulation sim) => new AgentAction(sim.Actions.Wait);

        /// <summary>One tick: the first villager does the action, the rest wait. Nobody is fed first.</summary>
        static void Step(Simulation sim, AgentAction? action = null)
        {
            var actions = sim.Agents.Select(_ => Wait(sim)).ToArray();
            if (action.HasValue) actions[0] = action.Value;
            sim.Step(actions);
        }

        static Simulation InSeason(string seasonId, int width = 12, int height = 9) =>
            new Simulation(Kit.Data, new World(width, height, Kit.Data.TerrainById("grass")), 1, seasonId) { WeatherEnabled = false, JobReviewsEnabled = false };

        // ------------------------------------------------------------------ the table

        [Test]
        public void Every_reward_is_an_outcome_or_shaping()
        {
            Assert.Greater(Kit.Data.Rewards.Count, 30);
            foreach (var reward in Kit.Data.Rewards)
                CollectionAssert.Contains(new[] { "outcome", "shaping" }, reward.Kind, reward.Id);
            Assert.IsFalse(Kit.Data.Rewards.First(r => r.Id == "agent_death").IsShaping);
            Assert.IsTrue(Kit.Data.Rewards.First(r => r.Id == "vote_cast").IsShaping, "being paid to vote is being told to vote");
        }

        [Test]
        public void Every_reward_the_engine_pays_is_in_the_table()
        {
            Assert.GreaterOrEqual(Simulation.RewardsInEngine.Count, 12);
            foreach (string id in Simulation.RewardsInEngine)
                Assert.IsTrue(Kit.Data.Rewards.Any(r => r.Id == id), id);
        }

        [Test]
        public void Shaping_is_off_unless_asked_for()
        {
            var (sim, _) = Village.One();

            Assert.AreEqual(Kit.Data.ConfigFloat("reward_shaping_scale"), sim.RewardShapingScale);
            Assert.AreEqual(0f, sim.RewardShapingScale, "the villagers find out for themselves what is worth doing");
        }

        // ------------------------------------------------------------------ outcomes

        [Test]
        public void A_quiet_tick_pays_nothing()
        {
            var (sim, agent) = Village.One();

            Step(sim);

            Assert.AreEqual(0f, sim.RewardOf(agent.Index));
        }

        [Test]
        public void A_need_at_zero_costs_every_tick_however_many_are()
        {
            var (sim, agent) = Village.One();
            agent.Set(Need.Hunger, 0);

            Step(sim);
            Assert.AreEqual(Value("stat_at_zero"), sim.RewardOf(agent.Index), Tolerance);

            agent.Set(Need.Thirst, 0);
            Step(sim);
            Assert.AreEqual(Value("stat_at_zero"), sim.RewardOf(agent.Index), Tolerance, "charged once, not per need");
        }

        [Test]
        public void Only_a_need_that_takes_health_at_zero_is_charged()
        {
            var (sim, agent) = Village.One();
            Assert.AreEqual(0f, Kit.Data.Needs.First(n => n.Id == "morale").HpDeltaPerTickAtZero, "low spirits do not kill");
            agent.Set(Need.Morale, 0);

            Step(sim);

            Assert.AreEqual(0f, agent.Get(Need.Morale));
            Assert.AreEqual(0f, sim.RewardOf(agent.Index), "nothing a villager can do raises morale yet, so nothing is charged for it");
        }

        [Test]
        public void Only_the_first_building_of_a_kind_pays()
        {
            var (sim, builder) = Village.One(width: 16);
            var chest = Kit.Data.BuildingById["basic_chest"];
            float paid = 0f;
            for (int number = 0; number < 2; number++)
            {
                Village.Give(sim, builder, chest.CostItem1, chest.CostQty1);
                int x = builder.X + 1;
                for (int t = 0; t < chest.BuildTicks + 5 && (sim.BuildingAt(x, 4) == null || !sim.BuildingAt(x, 4).Complete); t++)
                {
                    Kit.Refill(builder);
                    Step(sim, Village.Craft(sim, "basic_chest"));
                    paid += sim.RewardOf(builder.Index);
                }
                Assert.IsTrue(sim.BuildingAt(x, 4).Complete);
                Assert.AreEqual(Value("structure_completed"), paid, Tolerance, number == 0 ? "the first chest" : "a second chest is no new achievement");
                // Step round the chest to open ground for the next one.
                foreach (int action in new[] { sim.Actions.MoveNorth, sim.Actions.MoveEast, sim.Actions.MoveEast, sim.Actions.MoveSouth, sim.Actions.MoveEast })
                {
                    Kit.Refill(builder);
                    Step(sim, new AgentAction(action));
                }
            }
        }

        [Test]
        public void A_kind_the_village_started_with_is_not_new()
        {
            var (sim, builder) = Village.One();
            sim.PlaceBuilding("basic_chest", 9, 7);
            var chest = Kit.Data.BuildingById["basic_chest"];
            Village.Give(sim, builder, chest.CostItem1, chest.CostQty1);
            float paid = 0f;

            for (int t = 0; t < chest.BuildTicks + 5 && (sim.BuildingAt(5, 4) == null || !sim.BuildingAt(5, 4).Complete); t++)
            {
                Kit.Refill(builder);
                Step(sim, Village.Craft(sim, "basic_chest"));
                paid += sim.RewardOf(builder.Index);
            }

            Assert.IsTrue(sim.BuildingAt(5, 4).Complete);
            Assert.AreEqual(0f, paid);
        }

        [Test]
        public void A_reward_is_the_villagers_own_or_the_villages()
        {
            var sim = Kit.NewSim(12, 9);
            var hungry = sim.AddAgent(2, 4, Kit.Neutral);
            var doomed = sim.AddAgent(8, 4, Kit.Neutral);
            hungry.Set(Need.Hunger, 0);
            doomed.Set(Need.Thirst, 0);
            doomed.Set(Need.Hp, 0.1f);

            Step(sim);

            sim.RewardPartsOf(hungry.Index, out float own, out float village);
            Assert.AreEqual(Value("stat_at_zero"), own, Tolerance, "its own hunger");
            Assert.AreEqual(Value("agent_death"), village, Tolerance, "the village lost someone");
            Assert.AreEqual(own + village, sim.RewardOf(hungry.Index), Tolerance);
            Assert.AreEqual("global", Kit.Data.Rewards.First(r => r.Id == "agent_death").Scope);
            Assert.AreEqual("individual", Kit.Data.Rewards.First(r => r.Id == "stat_at_zero").Scope);
        }

        [Test]
        public void A_bite_costs_the_one_who_was_bitten()
        {
            var sim = Kit.NewSim(12, 9);
            var bitten = sim.AddAgent(2, 4, Kit.Neutral);
            var far = sim.AddAgent(10, 8, Kit.Neutral);
            sim.SpawnAnimal("wolf", 3, 4);

            Step(sim);

            Assert.IsTrue(sim.Events.Any(e => e.Type == SimEventType.AgentHurt && e.Agent == bitten.Index));
            Assert.AreEqual(Value("take_damage"), sim.RewardOf(bitten.Index), Tolerance);
            Assert.AreEqual(0f, sim.RewardOf(far.Index));
        }

        [Test]
        public void Freezing_costs_once_for_every_ten_health_lost()
        {
            var sim = InSeason("winter");
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            float per = Kit.Data.ConfigFloat("reward_hp_per_damage_penalty");
            float paid = 0f, lost = 0f;
            int ticksAtZero = 0;

            for (int t = 0; t < 70; t++)
            {
                Kit.Refill(agent);
                agent.Set(Need.Hp, 100f - lost);
                agent.Set(Need.Temp, 0);
                float before = agent.Get(Need.Hp);
                Step(sim);
                lost += before - agent.Get(Need.Hp);
                if (agent.Get(Need.Temp) <= 0f) ticksAtZero++;
                paid += sim.RewardOf(agent.Index);
            }

            Assert.Greater(lost, 2 * per, "cold enough for long enough to tell");
            float expected = ticksAtZero * Value("stat_at_zero") + (int)(lost / per + 1e-3f) * Value("take_damage");
            Assert.AreEqual(expected, paid, 1e-2f);
        }

        [Test]
        public void A_death_costs_everyone_the_dead_included()
        {
            var sim = Kit.NewSim(12, 9);
            var doomed = sim.AddAgent(2, 4, Kit.Neutral);
            var other = sim.AddAgent(8, 4, Kit.Neutral);
            doomed.Set(Need.Thirst, 0);
            doomed.Set(Need.Hp, 0.1f);

            Step(sim);

            Assert.IsFalse(doomed.Alive);
            Assert.AreEqual(Value("agent_death"), sim.RewardOf(other.Index), Tolerance);
            Assert.AreEqual(Value("agent_death"), sim.RewardOf(doomed.Index), Tolerance, "its last tick ends badly");

            Step(sim);
            Assert.AreEqual(0f, sim.RewardOf(other.Index), "once, not every tick after");
            Assert.AreEqual(0f, sim.RewardOf(doomed.Index));
        }

        [Test]
        public void A_finished_building_pays_everyone()
        {
            var (sim, builder) = Village.One();
            var other = sim.AddAgent(9, 7, Kit.Neutral);
            var chest = Kit.Data.BuildingById["basic_chest"];
            Village.Give(sim, builder, chest.CostItem1, chest.CostQty1);
            var build = Village.Craft(sim, "basic_chest");
            float builderPaid = 0f, otherPaid = 0f;

            for (int t = 0; t < chest.BuildTicks + 5 && (sim.BuildingAt(5, 4) == null || !sim.BuildingAt(5, 4).Complete); t++)
            {
                Kit.Refill(builder);
                Kit.Refill(other);
                Step(sim, build);
                builderPaid += sim.RewardOf(builder.Index);
                otherPaid += sim.RewardOf(other.Index);
            }

            Assert.IsTrue(sim.BuildingAt(5, 4).Complete);
            Assert.AreEqual(Value("structure_completed"), builderPaid, Tolerance);
            Assert.AreEqual(Value("structure_completed"), otherPaid, Tolerance, "the village's gain is everyone's");
        }

        [Test]
        public void A_building_put_there_by_the_setup_pays_nobody()
        {
            var (sim, agent) = Village.One();

            sim.PlaceBuilding("basic_chest", 8, 6);
            Step(sim);

            Assert.AreEqual(0f, sim.RewardOf(agent.Index));
        }

        [Test]
        public void A_technology_learned_pays_everyone()
        {
            var sim = Kit.NewSim(16, 12);
            var clever = Kit.Neutral;
            clever.Int = Kit.Data.ConfigInt("core_stat_max");
            var scholar = sim.AddAgent(4, 4, clever);
            sim.SetRole(scholar, "scholar");
            Kit.Face(scholar, 1, 0);
            var other = sim.AddAgent(12, 9, Kit.Neutral);
            sim.SetRole(other, "miner");
            sim.PlaceBuilding("library", 5, 4);
            int carpentry = Kit.Data.Tech.ToList().FindIndex(t => t.Id == "carpentry");
            int research = Kit.Data.ActionArgs.First(a => a.Kind == "research").Int;
            Step(sim);
            sim.Step(new[] { new AgentAction(sim.Actions.Vote, carpentry), new AgentAction(sim.Actions.Vote, carpentry) });
            Assert.AreEqual("carpentry", sim.ResearchingTech.Id);
            float scholarPaid = 0f, otherPaid = 0f;

            for (int t = 0; t < 2000 && !sim.IsResearched("carpentry"); t++)
            {
                Kit.Refill(scholar);
                Kit.Refill(other);
                Step(sim, new AgentAction(sim.Actions.CraftBuild, research));
                scholarPaid += sim.RewardOf(scholar.Index);
                otherPaid += sim.RewardOf(other.Index);
            }

            Assert.IsTrue(sim.IsResearched("carpentry"));
            Assert.AreEqual(Value("tech_researched"), otherPaid, Tolerance);
            Assert.AreEqual(Value("tech_researched"), scholarPaid, Tolerance, "with shaping off, the hours of study pay nothing by themselves");
        }

        [Test]
        public void A_winter_nobody_died_in_pays_everyone_when_it_ends()
        {
            var sim = InSeason("winter");
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            float paid = 0f;

            int guard = 0;
            while (sim.Season.Id == "winter" && guard++ < 5000)
            {
                Kit.Refill(agent);
                Step(sim);
                paid += sim.RewardOf(agent.Index);
            }

            Assert.AreNotEqual("winter", sim.Season.Id);
            Assert.AreEqual(Value("winter_survived"), paid, Tolerance);
        }

        [Test]
        public void A_winter_someone_died_in_pays_nothing()
        {
            var sim = InSeason("winter");
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            var doomed = sim.AddAgent(8, 4, Kit.Neutral);
            doomed.Set(Need.Thirst, 0);
            doomed.Set(Need.Hp, 0.1f);
            float paid = 0f;

            int guard = 0;
            while (sim.Season.Id == "winter" && guard++ < 5000)
            {
                Kit.Refill(agent);
                Step(sim);
                paid += sim.RewardOf(agent.Index);
            }

            Assert.IsFalse(doomed.Alive);
            Assert.AreEqual(Value("agent_death"), paid, Tolerance, "the death, and no prize for the winter");
        }

        [Test]
        public void Food_put_into_the_granary_pays_everyone()
        {
            var (sim, agent) = Village.One();
            var other = sim.AddAgent(10, 7, Kit.Neutral);
            sim.PlaceBuilding("town_granary", 5, 3);
            Village.Give(sim, agent, "berry", 4);
            Step(sim);
            Assert.AreEqual(0f, sim.RewardOf(agent.Index), "an empty granary");

            Step(sim, Village.Deposit(sim, "berry"));

            Assert.AreEqual(4, Village.StoredIn(sim.BuildingAt(5, 3), "berry"));
            Assert.AreEqual(Value("granary_increase"), sim.RewardOf(agent.Index), Tolerance);
            Assert.AreEqual(Value("granary_increase"), sim.RewardOf(other.Index), Tolerance);
        }

        // ------------------------------------------------------------------ shaping

        [TestCase(40f, true)]
        [TestCase(80f, false)]
        public void With_shaping_on_eating_pays_only_when_hungry(float hunger, bool pays)
        {
            var (sim, agent) = Village.One();
            sim.RewardShapingScale = 1f;
            Village.Give(sim, agent, "berry", 1);
            Kit.Refill(agent);
            agent.Set(Need.Hunger, hunger);

            Step(sim, new AgentAction(sim.Actions.Consume, Kit.Item("berry")));

            Assert.AreEqual(pays ? Value("consume_when_low") : 0f, sim.RewardOf(agent.Index), Tolerance);
        }

        [Test]
        public void With_shaping_off_eating_when_hungry_pays_nothing()
        {
            var (sim, agent) = Village.One();
            Village.Give(sim, agent, "berry", 1);
            Kit.Refill(agent);
            agent.Set(Need.Hunger, 40);

            Step(sim, new AgentAction(sim.Actions.Consume, Kit.Item("berry")));

            Assert.AreEqual(0f, sim.RewardOf(agent.Index), "being fed is its own reward: no need at zero, no death");
        }

        [Test]
        public void The_scale_multiplies_shaping_and_leaves_outcomes_alone()
        {
            var (sim, agent) = Village.One();
            sim.RewardShapingScale = 0.5f;
            Village.Give(sim, agent, "berry", 1);
            Kit.Refill(agent);
            agent.Set(Need.Hunger, 40);
            // An outcome on the same tick: someone else dies.
            var doomed = sim.AddAgent(9, 7, Kit.Neutral);
            doomed.Set(Need.Thirst, 0);
            doomed.Set(Need.Hp, 0.1f);

            Step(sim, new AgentAction(sim.Actions.Consume, Kit.Item("berry")));

            Assert.IsFalse(doomed.Alive);
            Assert.AreEqual(0.5f * Value("consume_when_low") + Value("agent_death"), sim.RewardOf(agent.Index), Tolerance);
        }

        [Test]
        public void With_shaping_on_going_to_sleep_exhausted_pays_once_per_sleep()
        {
            var (sim, agent) = Village.One();
            sim.RewardShapingScale = 1f;
            var sleep = new AgentAction(sim.Actions.Sleep);
            float low = Kit.Data.ConfigFloat("reward_exhausted_energy_below") - 5f;
            Kit.Refill(agent);
            agent.Set(Need.Energy, low);

            Step(sim, sleep);
            Assert.AreEqual(Value("sleep_when_exhausted"), sim.RewardOf(agent.Index), Tolerance);
            agent.Set(Need.Energy, low);
            Step(sim, sleep);
            Assert.AreEqual(0f, sim.RewardOf(agent.Index), "the same sleep");

            Step(sim);
            agent.Set(Need.Energy, low);
            Step(sim, sleep);
            Assert.AreEqual(Value("sleep_when_exhausted"), sim.RewardOf(agent.Index), Tolerance, "a new sleep");
        }

        [Test]
        public void With_shaping_on_gathering_pays_by_the_piece()
        {
            var (sim, agent) = Village.One();
            sim.RewardShapingScale = 1f;
            sim.World.PlaceNode(5, 4, Kit.Data.NodeById("berry_bush"));
            float paid = 0f;
            int gathered = 0;

            for (int t = 0; t < 40 && gathered == 0; t++)
            {
                Kit.Refill(agent);
                Step(sim, new AgentAction(sim.Actions.Harvest));
                gathered += sim.Events.Where(e => e.Type == SimEventType.Harvested).Sum(e => e.Qty);
                paid += sim.RewardOf(agent.Index);
            }

            Assert.Greater(gathered, 0);
            Assert.AreEqual(gathered * Value("gather_item"), paid, Tolerance);
        }

        [Test]
        public void With_shaping_on_a_vote_cast_and_a_crop_planted_pay()
        {
            var sim = Kit.NewSim(16, 16);
            sim.RewardShapingScale = 1f;
            var agent = sim.AddAgent(5, 4, Kit.Neutral);
            Step(sim);
            Assert.IsNotNull(sim.OpenVote);

            Step(sim, new AgentAction(sim.Actions.Vote, Kit.Data.RoleInt("farmer")));
            Assert.AreEqual(Value("vote_cast"), sim.RewardOf(agent.Index), Tolerance);

            // The garden's first plot lies below it: right in front of a villager looking up at the garden.
            sim.PlaceBuilding("garden_plot", 5, 6);
            Assert.AreEqual(Kit.Data.TerrainById("farmland").Int, sim.World.TerrainAt(5, 5));
            Kit.Face(agent, 0, 1);
            Village.Give(sim, agent, "carrot", 1);
            Kit.Refill(agent);
            var plant = new AgentAction(sim.Actions.CraftBuild, Kit.Data.ArgFor("carrot_crop"));
            Assume.That(sim.IsValid(agent.Index, plant), "carrots are sown in the season the test world starts in");

            Step(sim, plant);

            Assert.IsTrue(sim.Events.Any(e => e.Type == SimEventType.CropPlanted));
            Assert.AreEqual(Value("plant_crop"), sim.RewardOf(agent.Index), Tolerance);
        }

        [Test]
        public void With_shaping_on_study_pays_by_the_research_point()
        {
            var sim = Kit.NewSim(16, 12);
            sim.RewardShapingScale = 1f;
            var scholar = sim.AddAgent(4, 4, Kit.Neutral);
            sim.SetRole(scholar, "scholar");
            Kit.Face(scholar, 1, 0);
            sim.PlaceBuilding("study_circle", 5, 4);
            int carpentry = Kit.Data.Tech.ToList().FindIndex(t => t.Id == "carpentry");
            Step(sim);
            Step(sim, new AgentAction(sim.Actions.Vote, carpentry));
            int research = Kit.Data.ActionArgs.First(a => a.Kind == "research").Int;
            Kit.Refill(scholar);

            Step(sim, new AgentAction(sim.Actions.CraftBuild, research));

            Assert.Greater(sim.ResearchPoints, 0f);
            Assert.AreEqual(sim.ResearchPoints * Value("research_point"), sim.RewardOf(scholar.Index), Tolerance);
        }
    }
}
