using System.Linq;
using NUnit.Framework;
using TinyTribe.Sim.Core;
using TinyTribe.Sim.Data;

namespace TinyTribe.Tests
{
    /// <summary>Rain, storms and heatwaves (Sim Weather, Sim Seasons, rule R37). Kit.NewSim starts with the daily weather rolls off.</summary>
    public class WeatherTests
    {
        static WeatherDef Kind(string id) => Kit.Data.WeatherById(id);

        static AgentAction Wait(Simulation sim) => new AgentAction(sim.Actions.Wait);

        static (Simulation sim, Agent agent) One()
        {
            var sim = Kit.NewSim(12, 9);
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            Kit.Face(agent, 1, 0);
            return (sim, agent);
        }

        static float Change(Simulation sim, Agent agent, Need need, AgentAction action)
        {
            float before = agent.Get(need);
            sim.Step(new[] { action });
            return agent.Get(need) - before;
        }

        [Test]
        public void Weather_is_clear_at_the_start()
        {
            var sim = Kit.NewSim();

            Assert.AreEqual("clear", sim.Weather.Id);
        }

        [Test]
        public void Weather_lasts_its_time_and_then_clears()
        {
            var sim = Kit.NewSim();
            int duration = Kind("rain").DurationTicks;

            sim.SetWeather("rain");
            Assert.AreEqual("rain", sim.Weather.Id);
            Assert.AreEqual(sim.Tick + duration, sim.WeatherEndsTick);
            Kit.Advance(sim, duration - 1);
            Assert.AreEqual("rain", sim.Weather.Id, "one tick to go");

            Kit.Advance(sim, 1);

            Assert.AreEqual("clear", sim.Weather.Id);
            Assert.IsTrue(sim.Events.Any(e => e.Type == SimEventType.WeatherChanged));
        }

        [Test]
        public void Rain_puts_a_campfire_out()
        {
            var sim = Kit.NewSim();
            var fire = sim.PlaceBuilding("campfire", 4, 4);
            fire.FuelTicks = 100000;
            Assert.Greater(Kind("rain").Effect("campfire_extinguish_chance_per_tick"), 0f);

            // Long enough that staying lit would be a one in a billion chance.
            sim.SetWeather("rain", 400);
            Kit.Advance(sim, 400);

            Assert.IsFalse(fire.IsLit);
            Assert.AreEqual(0, fire.FuelTicks);
        }

        [Test]
        public void A_campfire_burns_as_usual_in_clear_weather()
        {
            var sim = Kit.NewSim();
            var fire = sim.PlaceBuilding("campfire", 4, 4);
            fire.FuelTicks = 1000;

            Kit.Advance(sim, 400);

            Assert.AreEqual(600, fire.FuelTicks);
        }

        [Test]
        public void Rain_chills_a_villager_in_the_open()
        {
            var (sim, agent) = One();
            agent.Set(Need.Temp, 50);
            float dry = Change(sim, agent, Need.Temp, Wait(sim));

            sim.SetWeather("rain");
            float wet = Change(sim, agent, Need.Temp, Wait(sim));

            float chill = Kind("rain").Effect("temp_delta_unsheltered");
            Assert.Less(chill, 0f);
            Assert.AreEqual(dry + chill, wet, 1e-4f);
        }

        [Test]
        public void Rain_does_not_reach_a_villager_asleep_at_home()
        {
            var (sim, agent) = One();
            sim.PlaceBuilding("hide_tent", 5, 4);
            var sleep = new AgentAction(sim.Actions.Sleep);
            sim.Step(new[] { sleep });
            Assert.IsTrue(agent.Sheltered);
            agent.Set(Need.Temp, 50);

            sim.SetWeather("rain");

            Assert.AreEqual(Kit.Data.Modifier("temp_indoors"), Change(sim, agent, Need.Temp, sleep), 1e-4f);
        }

        [Test]
        public void A_storm_wrecks_buildings_by_their_chance()
        {
            var sim = Kit.NewSim(80, 40);
            var tent = Kit.Data.BuildingById["hide_tent"];
            Assert.AreEqual(0.3f, tent.StormDestroyChance, 0.2f, "this test counts on tents being fragile");
            Assert.AreEqual(0f, Kit.Data.BuildingById["campfire"].StormDestroyChance);
            for (int i = 0; i < 100; i++) sim.PlaceBuilding("hide_tent", 2 + i % 25 * 3, 2 + i / 25 * 3);
            for (int i = 0; i < 20; i++) sim.PlaceBuilding("campfire", 2 + i * 3, 30);

            sim.SetWeather("storm");

            int tentsLost = sim.Buildings.Count(b => b.Def == tent && b.Destroyed);
            Assert.That(tentsLost, Is.InRange(15, 45), "about " + tent.StormDestroyChance * 100 + " of 100");
            Assert.AreEqual(0, sim.Buildings.Count(b => b.Def != tent && b.Destroyed), "a campfire cannot be blown down");
            Assert.AreEqual(tentsLost, sim.Events.Count(e => e.Type == SimEventType.BuildingDestroyed));
        }

        [Test]
        public void A_storm_spares_what_is_still_being_built()
        {
            var sim = Kit.NewSim(80, 12);
            for (int i = 0; i < 25; i++) sim.PlaceBuilding("hide_tent", 2 + i * 3, 2, complete: false);

            sim.SetWeather("storm");

            Assert.AreEqual(0, sim.Buildings.Count(b => b.Destroyed));
        }

        [Test]
        public void A_wrecked_chest_leaves_its_contents_on_the_ground()
        {
            var (sim, agent) = One();
            var chest = sim.PlaceBuilding("basic_chest", 5, 4);
            chest.Storage[0] = new ItemStack { Item = Kit.Item("berry"), Qty = 7 };
            chest.Storage[3] = new ItemStack { Item = Kit.Item("pine_wood"), Qty = 2 };

            sim.DestroyBuilding(chest);

            Assert.IsTrue(chest.Destroyed);
            Assert.IsNull(sim.BuildingAt(5, 4));
            Assert.AreEqual(Kit.Data.NodeById("item_pile").Int, sim.World.NodeAt(5, 4));
            CollectionAssert.AreEquivalent(new[] { (Kit.Item("berry"), 7), (Kit.Item("pine_wood"), 2) }, sim.PileAt(5, 4).Select(s => (s.Item, s.Qty)).ToArray());
            Assert.IsFalse(sim.IsValid(agent.Index, Village.Take(sim, "berry")), "there is no chest to take from");
            Assert.IsTrue(sim.IsValid(agent.Index, new AgentAction(sim.Actions.Harvest)), "but the pile can be picked up");
        }

        [Test]
        public void A_wrecked_house_leaves_its_people_homeless()
        {
            var (sim, agent) = One();
            var tent = sim.PlaceBuilding("hide_tent", 5, 4);
            var sleep = new AgentAction(sim.Actions.Sleep);
            sim.Step(new[] { sleep });
            Assert.AreEqual(tent.Index, agent.Home);

            sim.DestroyBuilding(tent);
            sim.Step(new[] { sleep });

            Assert.AreEqual(-1, agent.Home);
            Assert.IsFalse(agent.Sheltered);
            Assert.IsTrue(sim.CanEnter(5, 4), "the ground is free again");
        }

        [Test]
        public void A_burnt_out_fire_wrecked_or_not_is_not_counted_twice()
        {
            var sim = Kit.NewSim();
            var fire = sim.PlaceBuilding("campfire", 4, 4);

            sim.DestroyBuilding(fire);
            sim.DestroyBuilding(fire);
            Kit.Advance(sim, 5);

            Assert.IsFalse(fire.IsLit);
            Assert.AreEqual(1, sim.Buildings.Count);
        }

        [Test]
        public void A_heatwave_makes_villagers_thirstier()
        {
            var (sim, agent) = One();
            float usual = Change(sim, agent, Need.Thirst, Wait(sim));

            sim.SetWeather("heatwave");
            float hot = Change(sim, agent, Need.Thirst, Wait(sim));

            Assert.Greater(Kind("heatwave").Effect("thirst_drain_mult"), 1f);
            Assert.AreEqual(usual * Kind("heatwave").Effect("thirst_drain_mult"), hot, 1e-4f);
        }

        [Test]
        public void A_leather_hat_takes_the_edge_off_a_heatwave()
        {
            var (sim, agent) = One();
            float usual = Change(sim, agent, Need.Thirst, Wait(sim));
            int hat = Kit.Item("leather_hat");
            sim.AddItem(agent, hat, 1);
            sim.Step(new[] { new AgentAction(sim.Actions.Equip, hat) });

            sim.SetWeather("heatwave");
            float hot = Change(sim, agent, Need.Thirst, Wait(sim));

            float hatEffect = Kit.Data.EquipmentByItemInt(hat).EffectValue;
            Assert.AreEqual(usual * Kind("heatwave").Effect("thirst_drain_mult") * hatEffect, hot, 1e-4f);
        }

        [Test]
        public void Work_is_more_tiring_in_a_heatwave()
        {
            var (sim, agent) = One();
            sim.World.PlaceNode(5, 4, Kit.Data.NodeById("pine_tree"));
            var harvest = new AgentAction(sim.Actions.Harvest);
            float rest = Kit.Data.NeedById("energy").BaseDrainPerTick;
            float usual = -Change(sim, agent, Need.Energy, harvest) - rest;

            sim.SetWeather("heatwave");
            float hot = -Change(sim, agent, Need.Energy, harvest) - rest;

            Assert.Greater(Kind("heatwave").Effect("work_energy_drain_mult"), 1f);
            Assert.AreEqual(usual * Kind("heatwave").Effect("work_energy_drain_mult"), hot, 1e-4f);
        }

        [Test]
        public void Weather_comes_as_often_as_the_season_says()
        {
            var sim = Kit.NewSim(9, 9, seed: 11);
            sim.WeatherEnabled = true;
            int ticksPerDay = Kit.Data.ConfigInt("ticks_per_day"), daysPerSeason = Kit.Data.ConfigInt("days_per_season");
            int seasons = Kit.Data.Seasons.Count;
            const int years = 12;
            var started = new int[seasons, Kit.Data.Weather.Count];

            for (int t = 0; t < years * seasons * daysPerSeason * ticksPerDay; t++)
            {
                sim.Step(System.Array.Empty<AgentAction>());
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.WeatherChanged) started[sim.Season.Order, e.Item]++;
            }

            int days = years * daysPerSeason;
            foreach (var season in Kit.Data.Seasons)
                foreach (var kind in Kit.Data.Weather)
                {
                    if (kind.Id == "clear") continue;
                    float chance = kind.Id == "rain" ? season.RainChancePerDay : kind.Id == "storm" ? season.StormChancePerDay : season.HeatwaveChancePerDay;
                    int count = started[season.Order, Kit.Data.Weather.ToList().IndexOf(kind)];
                    if (chance == 0f) Assert.AreEqual(0, count, kind.Id + " in " + season.Id);
                    // Wide enough for 84 days of dice; a day gets one kind of weather at most, which takes a little off the rain.
                    else Assert.That(count, Is.InRange((int)(days * chance * 0.35f), (int)(days * chance * 1.9f) + 3), kind.Id + " in " + season.Id + " over " + days + " days");
                }
        }

        [Test]
        public void There_is_no_weather_when_it_is_switched_off()
        {
            var sim = Kit.NewSim();
            Assert.IsFalse(sim.WeatherEnabled, "Kit.NewSim switches it off so that other tests have clear skies");

            for (int t = 0; t < 60 * Kit.Data.ConfigInt("ticks_per_day"); t++)
            {
                sim.Step(System.Array.Empty<AgentAction>());
                Assert.AreEqual("clear", sim.Weather.Id);
            }
        }

        [Test]
        public void A_generated_game_has_weather_unless_asked_not_to()
        {
            var options = new SimulationOptions { Seed = 3, MapWidth = 64, MapHeight = 64 };
            Assert.IsTrue(Simulation.Create(Kit.Data, options).WeatherEnabled);

            options.Weather = false;
            Assert.IsFalse(Simulation.Create(Kit.Data, options).WeatherEnabled);
        }

        [Test]
        public void Weather_is_part_of_the_state()
        {
            var dry = Kit.NewSim();
            var wet = Kit.NewSim();

            wet.SetWeather("rain");

            Assert.AreNotEqual(dry.ComputeStateHash(), wet.ComputeStateHash());
        }
    }
}
