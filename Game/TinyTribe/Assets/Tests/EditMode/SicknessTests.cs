using System.Linq;
using NUnit.Framework;
using TinyTribe.Sim.Core;
using TinyTribe.Sim.Data;

namespace TinyTribe.Tests
{
    /// <summary>Sickness (Sim Sicknesses, rule R39). It is off unless a simulation switches it on.</summary>
    public class SicknessTests
    {
        static SicknessDef Kind(string id) => Kit.Data.Sicknesses.First(s => s.Id == id);

        static AgentAction Wait(Simulation sim) => new AgentAction(sim.Actions.Wait);

        static (Simulation sim, Agent agent) One(int width = 12, int height = 9)
        {
            var sim = Kit.NewSim(width, height);
            sim.SicknessEnabled = true;
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            Kit.Face(agent, 1, 0);
            sim.SetRole(agent, "cook");
            return (sim, agent);
        }

        /// <summary>One tick with every need topped up first, except the ones a test is about.</summary>
        static void Tick(Simulation sim, AgentAction action, params Need[] leave)
        {
            foreach (var agent in sim.Agents)
                foreach (Need need in System.Enum.GetValues(typeof(Need)))
                    if (System.Array.IndexOf(leave, need) < 0) agent.Set(need, Kit.Data.ConfigFloat("need_max"));
            var actions = sim.Agents.Select(a => a.Index == 0 ? action : Wait(sim)).ToArray();
            sim.Step(actions);
        }

        [Test]
        public void Nobody_falls_sick_while_sickness_is_switched_off()
        {
            var sim = Kit.NewSim(12, 9);
            Assert.IsFalse(sim.SicknessEnabled);
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            sim.SpawnAnimal("wolf", 5, 4);
            sim.AddItem(agent, Kit.Item("raw_meat"), 5);

            for (int t = 0; t < 300; t++) Tick(sim, t < 5 ? new AgentAction(sim.Actions.Consume, Kit.Item("raw_meat")) : Wait(sim));

            Assert.IsFalse(sim.IsSick(agent, "infection"));
            Assert.IsFalse(sim.IsSick(agent, "food_poisoning"));
        }

        [Test]
        public void A_sickness_with_a_length_passes_by_itself()
        {
            var (sim, agent) = One();
            var bleeding = Kind("bleeding");

            sim.MakeSick(agent, "bleeding");
            Assert.IsTrue(sim.IsSick(agent, "bleeding"));
            Assert.IsTrue(sim.Events.Any(e => e.Type == SimEventType.SicknessStarted && e.Agent == agent.Index));
            for (int t = 0; t < bleeding.DurationTicks - 1; t++) Tick(sim, Wait(sim));
            Assert.IsTrue(sim.IsSick(agent, "bleeding"), "one tick to go");

            Tick(sim, Wait(sim));

            Assert.IsFalse(sim.IsSick(agent, "bleeding"));
            Assert.IsTrue(sim.Events.Any(e => e.Type == SimEventType.SicknessEnded && e.Agent == agent.Index));
        }

        [Test]
        public void A_wolf_bite_can_infect()
        {
            var (sim, agent) = One();
            sim.SpawnAnimal("wolf", 5, 4);
            Assert.Greater(Kit.Data.AnimalById("wolf").SicknessChance, 0f);

            int ticks = 0;
            while (!sim.IsSick(agent, "infection") && ticks++ < 400) Tick(sim, Wait(sim));

            Assert.IsTrue(sim.IsSick(agent, "infection"), "a hundred bites and more without an infection");
        }

        [Test]
        public void An_infection_drains_health_until_it_is_bandaged()
        {
            var (sim, agent) = One();
            sim.MakeSick(agent, "infection");
            agent.Set(Need.Hunger, 40);
            float before = agent.Get(Need.Hp);

            sim.Step(new[] { Wait(sim) });
            Assert.AreEqual(before + Kind("infection").Effect("hp_delta_per_tick"), agent.Get(Need.Hp), 1e-4f);
            for (int t = 0; t < 1000; t++) Tick(sim, Wait(sim));
            Assert.IsTrue(sim.IsSick(agent, "infection"), "it does not pass by itself");

            int bandage = Kit.Item("bandage");
            sim.AddItem(agent, bandage, 1);
            Tick(sim, new AgentAction(sim.Actions.Consume, bandage));

            Assert.IsFalse(sim.IsSick(agent, "infection"));
        }

        [Test]
        public void An_infection_can_pass_to_the_villager_next_to_the_sick_one()
        {
            var (sim, agent) = One(30, 9);
            var beside = sim.AddAgent(5, 4, Kit.Neutral);
            var apart = sim.AddAgent(20, 4, Kit.Neutral);
            sim.SetRole(beside, "cook");
            sim.SetRole(apart, "cook");
            sim.MakeSick(agent, "infection");
            Assert.Greater(Kind("infection").Effect("spread_chance_per_tick_adjacent"), 0f);

            int ticks = 0;
            while (!sim.IsSick(beside, "infection") && ticks++ < 30000) Tick(sim, Wait(sim));

            Assert.IsTrue(sim.IsSick(beside, "infection"));
            Assert.IsFalse(sim.IsSick(apart, "infection"), "it only passes between neighbours");
        }

        [Test]
        public void Raw_meat_can_poison_and_the_poisoning_makes_hungry_and_tired()
        {
            var (sim, agent) = One();
            var poisoning = Kind("food_poisoning");
            int raw = Kit.Item("raw_meat");
            float energyBefore = 0f;
            for (int piece = 0; piece < 40 && !sim.IsSick(agent, "food_poisoning"); piece++)
            {
                sim.AddItem(agent, raw, 1);
                Kit.Refill(agent);
                energyBefore = agent.Get(Need.Energy);
                sim.Step(new[] { new AgentAction(sim.Actions.Consume, raw) });
            }
            Assert.IsTrue(sim.IsSick(agent, "food_poisoning"), "forty pieces of raw meat and still well");
            Assert.AreEqual(energyBefore + poisoning.Effect("energy_delta_on_onset") - Kit.Data.NeedById("energy").BaseDrainPerTick, agent.Get(Need.Energy), 1e-3f);

            agent.Set(Need.Hunger, 50);
            sim.Step(new[] { Wait(sim) });

            Assert.AreEqual(50 - Kit.Data.NeedById("hunger").BaseDrainPerTick * poisoning.Effect("hunger_drain_mult"), agent.Get(Need.Hunger), 1e-4f);
        }

        [Test]
        public void A_healing_potion_cures_what_it_says_it_cures()
        {
            var (sim, agent) = One();
            sim.MakeSick(agent, "food_poisoning");
            sim.MakeSick(agent, "common_cold");
            sim.MakeSick(agent, "infection");
            int potion = Kit.Item("healing_potion");
            sim.AddItem(agent, potion, 1);

            Tick(sim, new AgentAction(sim.Actions.Consume, potion));

            Assert.IsFalse(sim.IsSick(agent, "food_poisoning"));
            Assert.IsFalse(sim.IsSick(agent, "common_cold"));
            Assert.IsTrue(sim.IsSick(agent, "infection"), "that takes a bandage");
        }

        [Test]
        public void Being_very_cold_can_bring_on_a_cold_that_tires_and_weakens()
        {
            var (sim, agent) = One();
            var cold = Kind("common_cold");
            int ticks = 0;
            while (!sim.IsSick(agent, "common_cold") && ticks++ < 8000)
            {
                Kit.Refill(agent);
                agent.Set(Need.Temp, Kit.Data.ConfigFloat("cold_sickness_temp_below") - 5f);
                sim.Step(new[] { Wait(sim) });
            }
            Assert.IsTrue(sim.IsSick(agent, "common_cold"));

            Kit.Refill(agent);
            agent.Set(Need.Energy, 50);
            sim.Step(new[] { Wait(sim) });

            Assert.AreEqual(50 - Kit.Data.NeedById("energy").BaseDrainPerTick * cold.Effect("energy_drain_mult"), agent.Get(Need.Energy), 1e-4f);
            Assert.AreEqual(Kit.Data.ConfigFloat("need_max") + cold.Effect("max_hp_delta"), agent.Get(Need.Hp), 1e-4f, "health is capped lower while the cold lasts");
        }

        [Test]
        public void A_warm_villager_in_clear_weather_does_not_catch_cold()
        {
            var (sim, agent) = One();

            for (int t = 0; t < 3000; t++) Tick(sim, Wait(sim));

            Assert.IsFalse(sim.IsSick(agent, "common_cold"));
        }

        [Test]
        public void Working_thirsty_in_the_summer_sun_can_bring_on_heatstroke_which_pins_the_villager_down()
        {
            var sim = new Simulation(Kit.Data, new World(12, 9, Kit.Data.TerrainById("grass")), 1, "summer") { WeatherEnabled = false, JobReviewsEnabled = false, SicknessEnabled = true };
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            Kit.Face(agent, 1, 0);
            sim.SetRole(agent, "cook");
            sim.World.PlaceNode(5, 4, Kit.Data.NodeById("pine_tree"));
            var heatstroke = Kind("heatstroke");
            // Chop by day, rest by night, for most of a summer: a one in a hundred chance per stroke.
            int ticks = 0;
            while (!sim.IsSick(agent, "heatstroke") && sim.Season.Id == "summer" && ticks++ < 1500)
            {
                Kit.Refill(agent);
                agent.Set(Need.Thirst, Kit.Data.ConfigFloat("heatstroke_thirst_below") - 5f);
                sim.World.NodeCharges[sim.World.Index(5, 4)] = 5;
                for (int slot = 0; slot < agent.Inventory.Length; slot++) agent.Inventory[slot] = default;
                sim.Step(new[] { sim.IsNight ? Wait(sim) : new AgentAction(sim.Actions.Harvest) });
            }
            Assert.IsTrue(sim.IsSick(agent, "heatstroke"), "worked " + ticks + " ticks in the sun without it");

            Assert.IsFalse(sim.IsValid(agent.Index, new AgentAction(sim.Actions.MoveNorth)), "cannot walk");
            Assert.IsTrue(sim.IsValid(agent.Index, Wait(sim)));
            agent.Set(Need.Thirst, 50);
            float summer = sim.IsNight ? 1f : Kit.Data.Modifier("thirst_summer_day");
            sim.Step(new[] { Wait(sim) });
            Assert.AreEqual(50 - Kit.Data.NeedById("thirst").BaseDrainPerTick * summer * heatstroke.Effect("thirst_drain_mult"), agent.Get(Need.Thirst), 1e-4f);

            sim.AddItem(agent, Kit.Item("water"), 1);
            sim.Step(new[] { new AgentAction(sim.Actions.Consume, Kit.Item("water")) });
            Assert.IsFalse(sim.IsSick(agent, "heatstroke"), "water cures it");
            Assert.IsTrue(sim.IsValid(agent.Index, new AgentAction(sim.Actions.MoveNorth)));
        }

        [Test]
        public void Going_without_fresh_food_brings_scurvy_and_fresh_food_cures_it()
        {
            var (sim, agent) = One();
            sim.World.PlaceNode(5, 4, Kit.Data.NodeById("berry_bush"));
            var scurvy = Kind("scurvy");
            int after = Kit.Data.ConfigInt("scurvy_after_ticks");

            for (int t = 0; t < after - 1; t++) Tick(sim, Wait(sim));
            Assert.IsFalse(sim.IsSick(agent, "scurvy"));
            Tick(sim, Wait(sim));
            Tick(sim, Wait(sim));
            Assert.IsTrue(sim.IsSick(agent, "scurvy"));

            // Health is capped a little lower every tick, and work goes slower.
            for (int t = 0; t < 100; t++) Tick(sim, Wait(sim));
            Assert.AreEqual(Kit.Data.ConfigFloat("need_max") + 101 * scurvy.Effect("max_hp_delta_per_tick"), agent.Get(Need.Hp), 0.1f);
            var bush = Kit.Data.NodeById("berry_bush");
            Assert.AreEqual(Kit.ExpectedActions(bush.HarvestTicks, scurvy.Effect("work_speed_mult")), Kit.HarvestUntilYield(sim, agent, Kit.Item("berry")));

            Assert.AreEqual(1, Kit.Data.ItemById["berry"].PreventsScurvy);
            Tick(sim, new AgentAction(sim.Actions.Consume, Kit.Item("berry")));
            Tick(sim, Wait(sim));

            Assert.IsFalse(sim.IsSick(agent, "scurvy"));
            Assert.AreEqual(Kit.Data.ConfigFloat("need_max"), agent.Get(Need.Hp), 0.1f, "health can be whole again");
        }

        [Test]
        public void Eating_fresh_food_now_and_then_keeps_scurvy_away()
        {
            var (sim, agent) = One();
            int berry = Kit.Item("berry"), after = Kit.Data.ConfigInt("scurvy_after_ticks");
            sim.AddItem(agent, berry, 10);

            for (int t = 0; t < 3 * after; t++) Tick(sim, t % (after / 2) == 0 ? new AgentAction(sim.Actions.Consume, berry) : Wait(sim));

            Assert.IsFalse(sim.IsSick(agent, "scurvy"));
        }

        [Test]
        public void Sickness_is_part_of_the_state()
        {
            var (well, _) = One();
            var (sick, agent) = One();

            sick.MakeSick(agent, "infection");

            Assert.AreNotEqual(well.ComputeStateHash(), sick.ComputeStateHash());
        }
    }
}
