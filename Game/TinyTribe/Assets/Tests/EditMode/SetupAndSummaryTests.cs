using System;
using System.Linq;
using NUnit.Framework;
using TinyTribe.Sim.Core;

namespace TinyTribe.Tests
{
    public class SimulationOptionsTests
    {
        [Test]
        public void Default_options_match_the_config()
        {
            var sim = Simulation.Create(Kit.Data, new SimulationOptions { Seed = 3 });

            Assert.AreEqual(Kit.Data.ConfigInt("initial_agents"), sim.Agents.Count);
            Assert.AreEqual(Kit.Data.ConfigInt("map_width"), sim.World.Width);
            Assert.AreEqual(Kit.Data.ConfigInt("map_height"), sim.World.Height);
            Assert.AreEqual(Kit.Data.ConfigString("start_season"), sim.Season.Id);
        }

        [Test]
        public void Create_spawns_the_requested_number_of_agents()
        {
            var sim = Simulation.Create(Kit.Data, new SimulationOptions { Seed = 3, Agents = 12 });

            Assert.AreEqual(12, sim.Agents.Count);
        }

        [Test]
        public void Agent_count_is_capped_at_max_agents()
        {
            var sim = Simulation.Create(Kit.Data, new SimulationOptions { Seed = 3, Agents = 999 });

            Assert.AreEqual(Kit.Data.ConfigInt("max_agents"), sim.Agents.Count);
        }

        [Test]
        public void Create_builds_a_map_of_the_requested_size()
        {
            var sim = Simulation.Create(Kit.Data, new SimulationOptions { Seed = 3, MapWidth = 64, MapHeight = 48 });

            Assert.AreEqual((64, 48), (sim.World.Width, sim.World.Height));
            Assert.AreEqual((32, 24), (sim.World.SpawnX, sim.World.SpawnY));
        }

        [Test]
        public void Create_starts_in_the_requested_season_at_tick_zero()
        {
            var sim = Simulation.Create(Kit.Data, new SimulationOptions { Seed = 3, StartSeason = "winter" });

            Assert.AreEqual("winter", sim.Season.Id);
            Assert.AreEqual(0, sim.Tick);
            Assert.AreEqual(Kit.Data.ConfigInt("day_start_hour"), sim.Hour);
        }

        [Test]
        public void Season_still_advances_after_a_late_start()
        {
            var sim = new Simulation(Kit.Data, new World(9, 9, Kit.Data.TerrainById("grass")), 1, "winter");
            int startDay = sim.Day;

            Kit.AdvanceTo(sim, startDay + Kit.Data.ConfigInt("days_per_season"), 0);

            Assert.AreEqual("spring", sim.Season.Id);
        }

        [Test]
        public void Unknown_season_is_rejected()
        {
            Assert.Throws<ArgumentException>(() =>
                Simulation.Create(Kit.Data, new SimulationOptions { Seed = 3, StartSeason = "monsoon" }));
        }

        [Test]
        public void Same_options_give_the_same_world()
        {
            var a = Simulation.Create(Kit.Data, new SimulationOptions { Seed = 9, Agents = 8, MapWidth = 64, MapHeight = 64 });
            var b = Simulation.Create(Kit.Data, new SimulationOptions { Seed = 9, Agents = 8, MapWidth = 64, MapHeight = 64 });

            Assert.AreEqual(a.ComputeStateHash(), b.ComputeStateHash());
        }
    }

    public class QueryTests
    {
        [Test]
        public void CouldHarvest_is_true_for_a_node_the_agent_can_work_wherever_it_stands()
        {
            var sim = Kit.NewSim();
            sim.World.PlaceNode(7, 7, Kit.Data.NodeById("pine_tree"));
            var agent = sim.AddAgent(1, 1, Kit.Neutral);

            Assert.IsTrue(sim.CouldHarvest(agent, 7, 7));
        }

        [Test]
        public void CouldHarvest_depends_on_the_equipped_tool()
        {
            var sim = Kit.NewSim();
            sim.World.PlaceNode(7, 7, Kit.Data.NodeById("surface_rock"));
            var agent = sim.AddAgent(1, 1, Kit.Neutral);
            Assert.IsFalse(sim.CouldHarvest(agent, 7, 7));

            sim.AddItem(agent, Kit.Item("wooden_pickaxe"), 1);
            sim.Step(new[] { new AgentAction(sim.Actions.Equip, Kit.Item("wooden_pickaxe")) });

            Assert.IsTrue(sim.CouldHarvest(agent, 7, 7));
        }

        [Test]
        public void CouldHarvest_is_false_for_empty_or_outside_tiles()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(1, 1, Kit.Neutral);

            Assert.IsFalse(sim.CouldHarvest(agent, 5, 5));
            Assert.IsFalse(sim.CouldHarvest(agent, -1, 0));
        }

        [Test]
        public void Last_action_is_recorded_with_its_validity()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);

            sim.Step(new[] { new AgentAction(sim.Actions.MoveEast) });
            Assert.AreEqual(sim.Actions.MoveEast, agent.LastAction.Id);
            Assert.IsTrue(agent.LastActionValid);

            sim.Step(new[] { new AgentAction(sim.Actions.Harvest) });
            Assert.AreEqual(sim.Actions.Harvest, agent.LastAction.Id);
            Assert.IsFalse(agent.LastActionValid);
        }

        [Test]
        public void Held_items_are_totalled_across_villagers_including_equipment()
        {
            var sim = Kit.NewSim();
            var a = sim.AddAgent(2, 2, Kit.Neutral);
            var b = sim.AddAgent(6, 6, Kit.Neutral);
            int berry = Kit.Item("berry"), axe = Kit.Item("stone_axe");
            sim.AddItem(a, berry, 3);
            sim.AddItem(b, berry, 4);
            sim.AddItem(b, axe, 1);
            sim.Step(new[] { new AgentAction(sim.Actions.Wait), new AgentAction(sim.Actions.Equip, axe) });
            var totals = new int[Kit.Data.Items.Count + 1];

            VillageSummary.CountHeldItems(sim, totals);

            Assert.AreEqual(7, totals[berry]);
            Assert.AreEqual(1, totals[axe]);
            Assert.AreEqual(0, totals[Kit.Item("pine_wood")]);
        }

        [Test]
        public void Counting_held_items_starts_from_zero_each_time()
        {
            var sim = Kit.NewSim();
            var a = sim.AddAgent(2, 2, Kit.Neutral);
            sim.AddItem(a, Kit.Item("berry"), 3);
            var totals = new int[Kit.Data.Items.Count + 1];

            VillageSummary.CountHeldItems(sim, totals);
            VillageSummary.CountHeldItems(sim, totals);

            Assert.AreEqual(3, totals[Kit.Item("berry")]);
        }

        [Test]
        public void Active_nodes_are_counted_by_type_and_depleted_ones_are_left_out()
        {
            var sim = Kit.NewSim();
            var pine = Kit.Data.NodeById("pine_tree");
            var bush = Kit.Data.NodeById("berry_bush");
            sim.World.PlaceNode(1, 1, pine);
            sim.World.PlaceNode(2, 1, pine);
            sim.World.PlaceNode(3, 1, bush);
            sim.World.PlaceNode(4, 1, bush);
            sim.World.NodeCharges[sim.World.Index(4, 1)] = 0;
            var totals = new int[Kit.Data.ResourceNodes.Count + 1];

            VillageSummary.CountActiveNodes(sim.World, totals);

            Assert.AreEqual(2, totals[pine.Int]);
            Assert.AreEqual(1, totals[bush.Int]);
        }

        [Test]
        public void Stored_items_are_totalled_across_buildings()
        {
            var sim = Kit.NewSim(12, 9);
            int berry = Kit.Item("berry"), wood = Kit.Item("pine_wood");
            var first = sim.PlaceBuilding("basic_chest", 2, 2);
            var second = sim.PlaceBuilding("basic_chest", 6, 2);
            sim.PlaceBuilding("campfire", 9, 2);
            sim.PlaceBuilding("basic_chest", 2, 6, complete: false);
            first.Storage[0] = new ItemStack { Item = berry, Qty = 7 };
            first.Storage[3] = new ItemStack { Item = wood, Qty = 2 };
            second.Storage[1] = new ItemStack { Item = berry, Qty = 5 };
            var totals = new int[Kit.Data.Items.Count + 1];
            totals[wood] = 99;

            VillageSummary.CountStoredItems(sim, totals);

            Assert.AreEqual(12, totals[berry]);
            Assert.AreEqual(2, totals[wood], "the array is cleared first");
        }

        [Test]
        public void Buildings_are_counted_by_type_with_sites_apart()
        {
            var sim = Kit.NewSim(12, 9);
            sim.PlaceBuilding("basic_chest", 2, 2);
            sim.PlaceBuilding("basic_chest", 6, 2);
            sim.PlaceBuilding("basic_chest", 2, 6, complete: false);
            sim.PlaceBuilding("campfire", 9, 2);
            int chest = Kit.Data.BuildingById["basic_chest"].Int, fire = Kit.Data.BuildingById["campfire"].Int;
            var finished = new int[Kit.Data.Buildings.Count + 1];
            var sites = new int[Kit.Data.Buildings.Count + 1];

            VillageSummary.CountBuildings(sim, finished, sites);

            Assert.AreEqual((2, 1), (finished[chest], sites[chest]));
            Assert.AreEqual((1, 0), (finished[fire], sites[fire]));
        }

        [Test]
        public void Living_animals_are_counted_by_species()
        {
            var sim = Kit.NewSim(12, 9);
            sim.SpawnAnimal("rabbit", 1, 1);
            sim.SpawnAnimal("rabbit", 3, 1);
            sim.DespawnAnimal(sim.SpawnAnimal("rabbit", 5, 1));
            sim.SpawnAnimal("wolf", 7, 1);
            int maxInt = Kit.Data.Animals.Max(a => a.Int);
            var totals = new int[maxInt + 1];

            VillageSummary.CountAnimals(sim, totals);

            Assert.AreEqual(2, totals[Kit.Data.AnimalById("rabbit").Int]);
            Assert.AreEqual(1, totals[Kit.Data.AnimalById("wolf").Int]);
            Assert.AreEqual(0, totals[Kit.Data.AnimalById("bear").Int]);
        }

        [Test]
        public void Alive_count_ignores_dead_villagers()
        {
            var sim = Kit.NewSim();
            var doomed = sim.AddAgent(2, 2, Kit.Neutral);
            sim.AddAgent(6, 6, Kit.Neutral);
            doomed.Set(Need.Hunger, 0);
            doomed.Set(Need.Hp, 0.1f);

            sim.Step(new[] { new AgentAction(sim.Actions.Wait), new AgentAction(sim.Actions.Wait) });

            Assert.AreEqual(1, VillageSummary.AliveCount(sim));
        }
    }
}
