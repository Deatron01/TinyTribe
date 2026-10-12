using System;
using System.Linq;
using NUnit.Framework;
using TinyTribe.Brains;
using TinyTribe.Sim.Core;

namespace TinyTribe.Tests
{
    /// <summary>
    /// The bots' way up from a chest and a fire (rule R40): a garden and an Elders' Circle first, then,
    /// as research allows, a lumber mill, a library and the big farm.
    /// </summary>
    public class BotBuildingTests
    {
        static SimRunner Bots(Simulation sim) =>
            new SimRunner(sim, new SurvivalBrain(1)) { MaxMillisecondsPerAdvance = double.MaxValue };

        /// <summary>
        /// Open ground with a chest and a fire in the middle, pines to the east, oaks to the west, rocks
        /// to the south, berries and carrots about and a lake on the western edge.
        /// </summary>
        static Simulation Township()
        {
            var sim = Kit.NewSim(44, 26);
            sim.World.SpawnX = 22;
            sim.World.SpawnY = 13;
            var world = sim.World;
            for (int y = 0; y < world.Height; y++)
                for (int x = 0; x <= 1; x++)
                {
                    world.SetTerrain(x, y, Kit.Data.TerrainById("water"));
                    world.PlaceNode(x, y, Kit.Data.NodeById("water_source"));
                }
            for (int i = 0; i < 16; i++) world.PlaceNode(34 + i % 4 * 2, 5 + i / 4 * 4, Kit.Data.NodeById("pine_tree"));
            for (int i = 0; i < 6; i++) world.PlaceNode(6 + i % 3 * 2, 4 + i / 3 * 3, Kit.Data.NodeById("oak_tree"));
            for (int i = 0; i < 5; i++) world.PlaceNode(10 + i * 2, 22, Kit.Data.NodeById("surface_rock"));
            for (int i = 0; i < 8; i++) world.PlaceNode(12 + i * 3, 2, Kit.Data.NodeById("berry_bush"));
            for (int i = 0; i < 4; i++) world.PlaceNode(27 + i * 2, 21, Kit.Data.NodeById("wild_carrot"));
            sim.PlaceBuilding("basic_chest", 20, 13);
            sim.PlaceBuilding("campfire", 24, 13).FuelTicks = 2000;
            return sim;
        }

        /// <summary>The simple works a village starts with (rule R40), clear of where the bots build.</summary>
        static void SimpleWorks(Simulation sim)
        {
            sim.PlaceBuilding("garden_plot", 26, 9);
            sim.PlaceBuilding("study_circle", 30, 8);
        }

        /// <summary>A villager with a job, a spear in hand, and food and water in its pack.</summary>
        static Agent Villager(Simulation sim, string roleId, int x, int y)
        {
            var agent = sim.AddAgent(x, y, Kit.Neutral);
            sim.SetRole(agent, roleId);
            sim.AddItem(agent, Kit.Item("berry"), 9);
            sim.AddItem(agent, Kit.Item("water"), 2);
            sim.AddItem(agent, Kit.Item("wooden_spear"), 1);
            int spear = Kit.Item("wooden_spear");
            sim.Step(sim.Agents.Select(a => a == agent ? new AgentAction(sim.Actions.Equip, spear) : new AgentAction(sim.Actions.Wait)).ToArray());
            return agent;
        }

        static Building Find(Simulation sim, string buildingId) =>
            sim.Buildings.FirstOrDefault(b => b.Def.Id == buildingId && !b.Destroyed);

        static bool Built(Simulation sim, string buildingId)
        {
            var building = Find(sim, buildingId);
            return building != null && building.Complete;
        }

        static int Farmland(Simulation sim)
        {
            int farmland = Kit.Data.TerrainById("farmland").Int;
            return sim.World.Terrain.Count(t => t == farmland);
        }

        static int Plots(string buildingId) => (int)Kit.Data.BuildingById[buildingId].EffectValue;

        static int Run(SimRunner runner, Func<bool> done, int limit, string what)
        {
            for (int t = 1; t <= limit; t++)
            {
                runner.StepOnce();
                if (done()) return t;
            }
            Assert.Fail(what + " did not happen within " + limit + " ticks");
            return -1;
        }

        [Test]
        public void With_a_chest_and_a_fire_the_builder_lays_out_a_garden_and_an_elders_circle()
        {
            var sim = Township();
            var builder = Villager(sim, "builder", 22, 15);
            Villager(sim, "lumberjack", 23, 15);

            int ticks = Run(Bots(sim), () => Built(sim, "garden_plot") && Built(sim, "study_circle"), 3000, "a garden and an Elders' Circle");

            Assert.IsTrue(builder.Alive);
            Assert.AreEqual(Plots("garden_plot"), Farmland(sim), "every plot of the garden found free ground");
            Assert.IsNull(Find(sim, "lumber_mill"), "the mill waits for carpentry");
            foreach (string id in new[] { "garden_plot", "study_circle" })
            {
                var work = Find(sim, id);
                Assert.LessOrEqual(Math.Max(Math.Abs(work.X - sim.World.SpawnX), Math.Abs(work.Y - sim.World.SpawnY)), 14, id + " belongs in the village");
            }
            Assert.Pass("garden and circle after " + ticks + " ticks");
        }

        [Test]
        public void The_scholar_studies_carpentry_at_the_circle_and_then_the_builder_puts_up_a_mill()
        {
            var sim = Township();
            SimpleWorks(sim);
            Villager(sim, "builder", 22, 15);
            Villager(sim, "lumberjack", 23, 15);
            var scholar = Villager(sim, "scholar", 21, 16);
            var runner = Bots(sim);

            int learned = Run(runner, () => sim.IsResearched("carpentry"), 6000, "learning carpentry");
            int milled = learned + Run(runner, () => Built(sim, "lumber_mill"), 4000, "building a lumber mill");

            Assert.IsTrue(scholar.Alive);
            Assert.Pass("carpentry after " + learned + " ticks, mill after " + milled);
        }

        [Test]
        public void With_no_scholar_the_builder_studies_when_there_is_nothing_to_build()
        {
            var sim = Township();
            SimpleWorks(sim);
            Villager(sim, "builder", 22, 15);
            Villager(sim, "lumberjack", 23, 15);
            var runner = Bots(sim);

            for (int t = 0; t < 900; t++) runner.StepOnce();

            Assert.IsNotNull(sim.ResearchingTech);
            Assert.Greater(sim.ResearchPoints, 0f, "someone has to study, or the village never gets its mill");
        }

        [Test]
        public void Once_carpentry_is_known_the_builder_puts_up_a_lumber_mill()
        {
            var sim = Township();
            SimpleWorks(sim);
            sim.Research("carpentry");
            var builder = Villager(sim, "builder", 22, 15);
            Villager(sim, "lumberjack", 23, 15);

            int ticks = Run(Bots(sim), () => Built(sim, "lumber_mill"), 4000, "building a lumber mill");

            Assert.IsTrue(builder.Alive);
            var mill = Find(sim, "lumber_mill");
            Assert.LessOrEqual(Math.Max(Math.Abs(mill.X - sim.World.SpawnX), Math.Abs(mill.Y - sim.World.SpawnY)), 14, "the mill belongs in the village");
            Assert.Pass("lumber mill after " + ticks + " ticks");
        }

        [Test]
        public void With_a_mill_the_builder_saws_planks_fells_oak_and_builds_a_library()
        {
            var sim = Township();
            SimpleWorks(sim);
            sim.Research("carpentry");
            sim.PlaceBuilding("lumber_mill", 14, 15);
            Villager(sim, "builder", 22, 15);
            Villager(sim, "lumberjack", 23, 15);

            int ticks = Run(Bots(sim), () => Built(sim, "library"), 7000, "building a library");

            Assert.Pass("library after " + ticks + " ticks");
        }

        [Test]
        public void Once_agriculture_is_known_the_builder_builds_the_big_farm_as_well()
        {
            var sim = Township();
            SimpleWorks(sim);
            sim.Research("carpentry");
            sim.PlaceBuilding("lumber_mill", 14, 15);
            sim.PlaceBuilding("library", 14, 8);
            sim.Research("agriculture");
            Villager(sim, "builder", 22, 15);
            Villager(sim, "lumberjack", 23, 15);

            int ticks = Run(Bots(sim), () => Built(sim, "farm_windmill"), 7000, "building a farm");

            Assert.AreEqual(Plots("garden_plot") + Plots("farm_windmill"), Farmland(sim));
            Assert.Pass("farm after " + ticks + " ticks");
        }

        [Test]
        public void The_farmer_sows_the_garden_with_what_seed_there_is_and_reaps_the_crop()
        {
            var sim = Township();
            sim.PlaceBuilding("garden_plot", 26, 9);
            var chest = Find(sim, "basic_chest");
            chest.Storage[0] = new ItemStack { Item = Kit.Item("carrot"), Qty = 6 };
            var farmer = Villager(sim, "farmer", 22, 15);
            Villager(sim, "builder", 23, 15);
            var runner = Bots(sim);
            int ripe = Kit.Data.NodeById("crop_mature").Int;
            int sown = 0, reaped = 0;

            for (int t = 0; t < 1500 && reaped == 0; t++)
            {
                runner.StepOnce();
                sown += sim.Events.Count(e => e.Type == SimEventType.CropPlanted && e.Agent == farmer.Index);
                reaped += sim.Events.Count(e => e.Type == SimEventType.NodeDepleted && e.Item == ripe);
            }

            Assert.IsFalse(sim.IsResearched("agriculture"), "a garden takes no research");
            Assert.GreaterOrEqual(sown, 2, "seed in the chest and three plots to sow");
            Assert.Greater(reaped, 0, "a crop was reaped");
        }

        [Test]
        public void The_farmer_works_every_farm_the_village_has()
        {
            var sim = Township();
            sim.Research("agriculture");
            var garden = sim.PlaceBuilding("garden_plot", 26, 9);
            var farm = sim.PlaceBuilding("farm_windmill", 26, 16);
            var chest = Find(sim, "basic_chest");
            chest.Storage[0] = new ItemStack { Item = Kit.Item("carrot"), Qty = 10 };
            chest.Storage[1] = new ItemStack { Item = Kit.Item("carrot"), Qty = 10 };
            var farmer = Villager(sim, "farmer", 22, 15);
            Villager(sim, "builder", 23, 15);
            var runner = Bots(sim);
            int atGarden = 0, atFarm = 0;

            for (int t = 0; t < 1500 && (atGarden == 0 || atFarm == 0); t++)
            {
                runner.StepOnce();
                foreach (var e in sim.Events)
                {
                    if (e.Type != SimEventType.CropPlanted || e.Agent != farmer.Index) continue;
                    if (Math.Abs(e.X - garden.X) + Math.Abs(e.Y - garden.Y) == 1) atGarden++;
                    else if (e.X >= farm.X - 1 && e.X <= farm.X + farm.Width && e.Y >= farm.Y - 1 && e.Y <= farm.Y + farm.Height) atFarm++;
                }
            }

            Assert.Greater(atGarden, 0, "the garden was sown");
            Assert.Greater(atFarm, 0, "the big farm was sown");
        }

        [Test]
        public void A_village_with_a_circle_learns_carpentry_and_agriculture_and_builds_all_the_way_to_the_big_farm()
        {
            var sim = Township();
            SimpleWorks(sim);
            Villager(sim, "builder", 22, 15);
            Villager(sim, "lumberjack", 23, 15);
            Villager(sim, "scholar", 21, 16);
            var farmer = Villager(sim, "farmer", 24, 16);
            var runner = Bots(sim);

            int carpentry = Run(runner, () => sim.IsResearched("carpentry"), 6000, "learning carpentry");
            int mill = carpentry + Run(runner, () => Built(sim, "lumber_mill"), 4000, "building the mill");
            int agriculture = mill + Run(runner, () => sim.IsResearched("agriculture"), 8000, "learning agriculture");
            int farm = agriculture + Run(runner, () => Built(sim, "farm_windmill"), 9000, "building the big farm");

            Assert.IsTrue(farmer.Alive);
            Assert.Pass("carpentry " + carpentry + ", mill " + mill + ", agriculture " + agriculture + ", big farm " + farm);
        }
    }
}
