using System.Linq;
using NUnit.Framework;
using TinyTribe.Sim.Core;
using TinyTribe.Sim.Data;

namespace TinyTribe.Tests
{
    /// <summary>Research at the Library, chosen by the Council (Sim Tech, rules R18, R38).</summary>
    public class ResearchTests
    {
        static int ResearchArg => Kit.Data.ActionArgs.First(a => a.Kind == "research").Int;

        static int TechOption(string techId) => Kit.Data.Tech.ToList().FindIndex(t => t.Id == techId);

        static AgentAction Wait(Simulation sim) => new AgentAction(sim.Actions.Wait);

        static AgentAction Research(Simulation sim) => new AgentAction(sim.Actions.CraftBuild, ResearchArg);

        /// <summary>One villager with a role (so no role vote gets in the way) facing a finished Library.</summary>
        static (Simulation sim, Agent agent, Building library) AtTheLibrary(string roleId = "scholar", int intelligence = 5)
        {
            var sim = Kit.NewSim(14, 9);
            var stats = Kit.Neutral;
            stats.Int = intelligence;
            var agent = sim.AddAgent(4, 4, stats);
            Kit.Face(agent, 1, 0);
            sim.SetRole(agent, roleId);
            var library = sim.PlaceBuilding("library", 5, 4);
            return (sim, agent, library);
        }

        static void Step(Simulation sim, AgentAction action)
        {
            Kit.Refill(sim.Agents[0]);
            sim.Step(new[] { action });
        }

        /// <summary>Votes the technology in: one tick for the vote to open, one to cast the only vote.</summary>
        static void Choose(Simulation sim, string techId)
        {
            Step(sim, Wait(sim));
            Assert.AreEqual(VoteKind.Research, sim.OpenVote.Kind);
            Step(sim, new AgentAction(sim.Actions.Vote, TechOption(techId)));
            Assert.AreEqual(techId, sim.ResearchingTech.Id);
        }

        [Test]
        public void The_first_technology_is_known_from_the_start()
        {
            var sim = Kit.NewSim();

            Assert.AreEqual(0, Kit.Data.Tech[0].RpCost);
            Assert.IsTrue(sim.IsResearched(Kit.Data.Tech[0].Id));
            Assert.IsFalse(sim.IsResearched("agriculture"));
        }

        [Test]
        public void No_research_vote_without_a_library()
        {
            var sim = Kit.NewSim();
            sim.SetRole(sim.AddAgent(4, 4, Kit.Neutral), "scholar");

            for (int t = 0; t < 5; t++) Step(sim, Wait(sim));

            Assert.IsNull(sim.OpenVote);
        }

        [Test]
        public void A_research_vote_opens_once_there_is_a_library()
        {
            var (sim, _, _) = AtTheLibrary();

            Step(sim, Wait(sim));

            Assert.AreEqual(VoteKind.Research, sim.OpenVote.Kind);
            Assert.AreEqual(-1, sim.OpenVote.Subject);
            Assert.IsTrue(sim.Events.Any(e => e.Type == SimEventType.VoteOpened && e.Item == (int)VoteKind.Research));
        }

        [Test]
        public void Only_technologies_whose_prerequisite_is_known_can_be_voted_for()
        {
            var (sim, agent, _) = AtTheLibrary();
            Step(sim, Wait(sim));
            var mask = new bool[Kit.Data.ConfigInt("num_action_args")];

            sim.GetArgMask(agent.Index, sim.Actions.Vote, mask);

            var open = Kit.Data.Tech.Where(t => t.RpCost > 0 && sim.IsResearched(t.Prerequisite)).Select(t => TechOption(t.Id)).ToArray();
            CollectionAssert.AreEqual(open, Enumerable.Range(0, mask.Length).Where(i => mask[i]).ToArray());
            Assert.IsFalse(mask[TechOption("economics")], "economics needs metallurgy first");
        }

        [Test]
        public void The_technology_voted_for_becomes_the_one_being_researched()
        {
            var (sim, _, _) = AtTheLibrary();
            Assert.IsNull(sim.ResearchingTech);

            Choose(sim, "medicine");

            Assert.AreEqual(0f, sim.ResearchPoints);
            Assert.IsNull(sim.OpenVote, "nothing more to vote on while it is being researched");
        }

        [TestCase("scholar", 5)]
        [TestCase("scholar", 10)]
        [TestCase("cook", 5)]
        public void Research_adds_points_by_intelligence_and_job(string roleId, int intelligence)
        {
            var (sim, agent, _) = AtTheLibrary(roleId, intelligence);
            Choose(sim, "agriculture");
            Assert.IsTrue(sim.IsValid(agent.Index, Research(sim)));

            Step(sim, Research(sim));

            float expected = Kit.Data.ConfigFloat("rp_per_tick_base") * intelligence / Kit.Data.ConfigInt("stat_midpoint");
            if (roleId != "scholar") expected *= Kit.Data.ConfigFloat("research_rp_mult_non_scholar");
            Assert.AreEqual(expected, sim.ResearchPoints, 1e-5f);
        }

        [Test]
        public void Research_needs_a_finished_library_in_front_and_something_chosen()
        {
            var (sim, agent, library) = AtTheLibrary();
            Assert.IsFalse(sim.IsValid(agent.Index, Research(sim)), "nothing chosen yet");

            Choose(sim, "agriculture");
            Kit.Face(agent, -1, 0);
            Assert.IsFalse(sim.IsValid(agent.Index, Research(sim)), "not facing the library");

            Kit.Face(agent, 1, 0);
            sim.DestroyBuilding(library);
            Assert.IsFalse(sim.IsValid(agent.Index, Research(sim)), "no library");
        }

        [Test]
        public void A_technology_is_known_once_its_cost_is_reached_and_unlocks_what_needs_it()
        {
            var (sim, agent, _) = AtTheLibrary("scholar", 10);
            var hoe = Village.Craft(sim, "farming_hoe");
            Village.Give(sim, agent, "pine_wood", 2);
            Village.Give(sim, agent, "cobblestone", 1);
            Assert.IsFalse(sim.IsValid(agent.Index, hoe), "the hoe needs agriculture");
            Choose(sim, "agriculture");
            var tech = Kit.Data.Tech.First(t => t.Id == "agriculture");
            float perAction = Kit.Data.ConfigFloat("rp_per_tick_base") * 2f;

            int actions = 0;
            bool announced = false;
            while (!sim.IsResearched("agriculture") && actions < 10000)
            {
                Step(sim, Research(sim));
                announced |= sim.Events.Any(e => e.Type == SimEventType.TechResearched && e.Item == TechOption("agriculture"));
                actions++;
            }

            Assert.AreEqual(tech.RpCost / perAction, actions, 2);
            Assert.IsTrue(announced);
            Assert.IsNull(sim.ResearchingTech);
            Assert.IsTrue(sim.IsValid(agent.Index, hoe));
        }

        [Test]
        public void The_next_vote_opens_when_a_technology_is_done()
        {
            var (sim, _, _) = AtTheLibrary("scholar", 10);
            Choose(sim, "agriculture");

            for (int n = 0; n < 10000 && !sim.IsResearched("agriculture"); n++) Step(sim, Research(sim));
            Step(sim, Wait(sim));

            Assert.AreEqual(VoteKind.Research, sim.OpenVote.Kind);
            Assert.IsFalse(sim.IsValid(0, new AgentAction(sim.Actions.Vote, TechOption("agriculture"))), "already known");
            Assert.IsTrue(sim.IsValid(0, new AgentAction(sim.Actions.Vote, TechOption("medicine"))));
        }

        [Test]
        public void A_research_vote_nobody_takes_part_in_is_held_again_the_next_day()
        {
            var (sim, _, _) = AtTheLibrary();
            Step(sim, Wait(sim));
            int duration = Kit.Data.ConfigInt("vote_duration_ticks");

            for (int t = 0; t < duration; t++) Step(sim, Wait(sim));
            Assert.IsNull(sim.OpenVote);
            Step(sim, Wait(sim));
            Assert.IsNull(sim.OpenVote, "not straight away");

            for (int t = 0; t < Kit.Data.ConfigInt("ticks_per_day"); t++) Step(sim, Wait(sim));
            Assert.AreEqual(VoteKind.Research, sim.OpenVote.Kind);
        }
    }

    /// <summary>
    /// Simple versions first, better ones after research (rule R40): a garden plot and an Elders'
    /// Circle need no research; the lumber mill, and with it planks and everything built of them, does.
    /// </summary>
    public class SimpleFirstTests
    {
        static int ResearchArg => Kit.Data.ActionArgs.First(a => a.Kind == "research").Int;

        static int TechOption(string techId) => Kit.Data.Tech.ToList().FindIndex(t => t.Id == techId);

        static AgentAction Wait(Simulation sim) => new AgentAction(sim.Actions.Wait);

        static (Simulation sim, Agent agent) One(string roleId = "scholar")
        {
            var sim = Kit.NewSim(16, 12);
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            Kit.Face(agent, 1, 0);
            if (roleId != null) sim.SetRole(agent, roleId);
            return (sim, agent);
        }

        static void Step(Simulation sim, AgentAction action)
        {
            Kit.Refill(sim.Agents[0]);
            sim.Step(new[] { action });
        }

        [Test]
        public void A_garden_plot_needs_no_research_and_gives_fewer_plots_than_a_farm()
        {
            var sim = Kit.NewSim(16, 16);
            var garden = Kit.Data.BuildingById["garden_plot"];
            Assert.IsTrue(string.IsNullOrEmpty(garden.TechRequired));
            Assert.Less(garden.EffectValue, Kit.Data.BuildingById["farm_windmill"].EffectValue);

            sim.PlaceBuilding("garden_plot", 8, 8);

            int farmland = Kit.Data.TerrainById("farmland").Int;
            Assert.AreEqual((int)garden.EffectValue, sim.World.Terrain.Count(t => t == farmland));
        }

        [Test]
        public void Anyone_can_lay_out_a_garden_plot_and_plant_in_it()
        {
            var (sim, agent) = One(null);
            var garden = Kit.Data.BuildingById["garden_plot"];
            Village.Give(sim, agent, garden.CostItem1, garden.CostQty1);
            var build = Village.Craft(sim, "garden_plot");
            Assert.IsTrue(sim.IsValid(agent.Index, build));

            for (int t = 0; t < garden.BuildTicks + 5 && (sim.BuildingAt(5, 4) == null || !sim.BuildingAt(5, 4).Complete); t++) Step(sim, build);

            Assert.IsTrue(sim.BuildingAt(5, 4).Complete);
            Assert.AreEqual(Kit.Data.TerrainById("farmland").Int, sim.World.TerrainAt(5, 3), "the first plot lies below the garden");
        }

        [Test]
        public void The_lumber_mill_needs_carpentry()
        {
            var (sim, agent) = One("builder");
            var mill = Kit.Data.BuildingById["lumber_mill"];
            Assert.AreEqual("carpentry", mill.TechRequired);
            Village.Give(sim, agent, mill.CostItem1, mill.CostQty1);
            Village.Give(sim, agent, mill.CostItem2, mill.CostQty2);
            var build = Village.Craft(sim, "lumber_mill");
            Assert.IsFalse(sim.IsValid(agent.Index, build));

            sim.Research("carpentry");

            Assert.IsTrue(sim.IsValid(agent.Index, build));
        }

        [Test]
        public void Carpentry_is_cheap_and_needs_no_library()
        {
            var carpentry = Kit.Data.Tech.First(t => t.Id == "carpentry");

            Assert.AreEqual(0, carpentry.NeedsLibrary);
            Assert.Less(carpentry.RpCost, Kit.Data.Tech.First(t => t.Id == "agriculture").RpCost);
            Assert.AreEqual(1, Kit.Data.Tech.First(t => t.Id == "metallurgy").NeedsLibrary);
        }

        [Test]
        public void An_elders_circle_is_enough_to_vote_on_the_simple_things()
        {
            var (sim, agent) = One();
            sim.PlaceBuilding("study_circle", 5, 4);

            Step(sim, Wait(sim));

            Assert.AreEqual(VoteKind.Research, sim.OpenVote.Kind);
            var mask = new bool[Kit.Data.ConfigInt("num_action_args")];
            sim.GetArgMask(agent.Index, sim.Actions.Vote, mask);
            var simple = Kit.Data.Tech.Where(t => t.RpCost > 0 && t.NeedsLibrary == 0 && sim.IsResearched(t.Prerequisite)).Select(t => TechOption(t.Id)).ToArray();
            CollectionAssert.AreEqual(simple, Enumerable.Range(0, mask.Length).Where(i => mask[i]).ToArray());
            CollectionAssert.Contains(simple, TechOption("carpentry"));
            CollectionAssert.DoesNotContain(simple, TechOption("medicine"));
        }

        [Test]
        public void Study_at_the_circle_goes_at_the_circles_own_slower_pace()
        {
            var (sim, agent) = One();
            var circle = sim.PlaceBuilding("study_circle", 5, 4);
            Assert.Less(circle.Def.EffectValue, Kit.Data.BuildingById["library"].EffectValue);
            Step(sim, Wait(sim));
            Step(sim, new AgentAction(sim.Actions.Vote, TechOption("carpentry")));
            Assert.AreEqual("carpentry", sim.ResearchingTech.Id);
            var study = new AgentAction(sim.Actions.CraftBuild, ResearchArg);
            Assert.IsTrue(sim.IsValid(agent.Index, study));

            Step(sim, study);

            Assert.AreEqual(circle.Def.EffectValue * agent.Stats.Int / Kit.Data.ConfigInt("stat_midpoint"), sim.ResearchPoints, 1e-5f);
        }

        [Test]
        public void What_needs_a_library_cannot_be_studied_at_the_circle()
        {
            var (sim, agent) = One();
            sim.PlaceBuilding("study_circle", 5, 4);
            var library = sim.PlaceBuilding("library", 5, 8);
            Step(sim, Wait(sim));
            Step(sim, new AgentAction(sim.Actions.Vote, TechOption("medicine")));
            Assert.AreEqual("medicine", sim.ResearchingTech.Id);
            var study = new AgentAction(sim.Actions.CraftBuild, ResearchArg);

            Assert.IsFalse(sim.IsValid(agent.Index, study), "the circle in front cannot teach medicine");
            sim.DestroyBuilding(library);
            Assert.IsFalse(sim.IsValid(agent.Index, study));
        }
    }

    /// <summary>Farm plots, planting and harvesting (Sim Crops, rule R06).</summary>
    public class FarmingTests
    {
        static int Farmland => Kit.Data.TerrainById("farmland").Int;

        static AgentAction Plant(Simulation sim, string cropId) => new AgentAction(sim.Actions.CraftBuild, Kit.Data.ArgFor(cropId));

        static CropDef Crop(string id) => Kit.Data.Crops.First(c => c.Id == id);

        /// <summary>A farm with a villager standing below it, facing a tile of its farmland, holding seeds.</summary>
        static (Simulation sim, Agent agent, int x, int y) AtTheFarm(string seed = "wheat_seeds", string roleId = "cook")
        {
            var sim = Kit.NewSim(16, 16);
            sim.Research("agriculture");
            var farm = sim.PlaceBuilding("farm_windmill", 6, 6);
            // The row below the farm is farmland; the villager stands below that row and looks up at it.
            var agent = sim.AddAgent(farm.X, farm.Y - 2, Kit.Neutral);
            Kit.Face(agent, 0, 1);
            sim.SetRole(agent, roleId);
            if (seed != null) Village.Give(sim, agent, seed, 3);
            Assert.AreEqual(Farmland, sim.World.TerrainAt(farm.X, farm.Y - 1));
            return (sim, agent, farm.X, farm.Y - 1);
        }

        static void Step(Simulation sim, AgentAction action)
        {
            Kit.Refill(sim.Agents[0]);
            sim.Step(new[] { action });
        }

        static int StepsUntilRipe(Simulation sim, int x, int y, int limit = 5000)
        {
            int ripe = Kit.Data.NodeById("crop_mature").Int;
            for (int n = 1; n <= limit; n++)
            {
                Step(sim, new AgentAction(sim.Actions.Wait));
                if (sim.World.NodeAt(x, y) == ripe) return n;
            }
            Assert.Fail("the crop never ripened");
            return -1;
        }

        [Test]
        public void A_finished_farm_turns_ground_beside_it_into_farmland()
        {
            var sim = Kit.NewSim(16, 16);
            sim.Research("agriculture");
            var def = Kit.Data.BuildingById["farm_windmill"];

            var farm = sim.PlaceBuilding("farm_windmill", 6, 6);

            int plots = 0;
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    if (sim.World.TerrainAt(x, y) != Farmland) continue;
                    plots++;
                    bool beside = x >= farm.X - 1 && x <= farm.X + farm.Width && y >= farm.Y - 1 && y <= farm.Y + farm.Height && !farm.Contains(x, y);
                    Assert.IsTrue(beside, "farmland at " + x + "," + y + " is not next to the farm");
                }
            Assert.AreEqual((int)def.EffectValue, plots);
            Assert.AreEqual(plots, sim.Events.Count(e => e.Type == SimEventType.TerrainChanged));
        }

        [Test]
        public void A_farm_that_is_still_being_built_has_no_farmland()
        {
            var sim = Kit.NewSim(16, 16);
            sim.Research("agriculture");

            sim.PlaceBuilding("farm_windmill", 6, 6, complete: false);

            Assert.IsFalse(sim.World.Terrain.Contains((byte)Farmland));
        }

        [Test]
        public void Planting_takes_a_seed_and_starts_a_crop()
        {
            var (sim, agent, x, y) = AtTheFarm();
            Assert.IsTrue(sim.IsValid(agent.Index, Plant(sim, "wheat_crop")));

            Step(sim, Plant(sim, "wheat_crop"));

            Assert.AreEqual(2, Village.Has(agent, "wheat_seeds"));
            Assert.AreEqual(Kit.Data.NodeById("crop_growing").Int, sim.World.NodeAt(x, y));
            Assert.AreEqual("wheat_crop", sim.CropAt(x, y).Id);
            Assert.IsTrue(sim.Events.Any(e => e.Type == SimEventType.CropPlanted && e.Agent == agent.Index && e.X == x && e.Y == y));
            Assert.IsFalse(sim.IsValid(agent.Index, Plant(sim, "wheat_crop")), "the tile is taken");
            Assert.IsFalse(sim.IsValid(agent.Index, new AgentAction(sim.Actions.Harvest)), "nothing to reap yet");
        }

        [Test]
        public void Planting_needs_farmland_the_seed_and_the_season()
        {
            var (sim, agent, _, _) = AtTheFarm("wheat_seeds");
            Assert.IsFalse(sim.IsValid(agent.Index, Plant(sim, "potato_crop")), "no potato to plant");

            Kit.Face(agent, 0, -1);
            Assert.IsFalse(sim.IsValid(agent.Index, Plant(sim, "wheat_crop")), "plain grass");

            // Wheat is not sown in autumn; carrots are.
            var autumn = new Simulation(Kit.Data, new World(16, 16, Kit.Data.TerrainById("grass")), 1, "autumn") { WeatherEnabled = false, JobReviewsEnabled = false };
            autumn.Research("agriculture");
            var farm = autumn.PlaceBuilding("farm_windmill", 6, 6);
            var late = autumn.AddAgent(farm.X, farm.Y - 2, Kit.Neutral);
            Kit.Face(late, 0, 1);
            Village.Give(autumn, late, "wheat_seeds", 1);
            Village.Give(autumn, late, "carrot", 1);
            Assert.IsFalse(autumn.IsValid(late.Index, Plant(autumn, "wheat_crop")));
            Assert.IsTrue(autumn.IsValid(late.Index, Plant(autumn, "carrot_crop")));
        }

        [Test]
        public void A_crop_ripens_after_its_growing_time_at_the_pace_of_the_season()
        {
            var (sim, _, x, y) = AtTheFarm("carrot");
            float pace = sim.Season.CropGrowthMult;
            Assert.Greater(pace, 1f, "spring is the best growing season");

            Step(sim, Plant(sim, "carrot_crop"));
            int steps = StepsUntilRipe(sim, x, y);

            Assert.AreEqual(Crop("carrot_crop").GrowthTicks / pace, steps, 2f);
        }

        [Test]
        public void A_farmers_crops_grow_faster()
        {
            var (sim, _, x, y) = AtTheFarm("carrot", "farmer");
            float pace = sim.Season.CropGrowthMult * Kit.Data.Roles.First(r => r.Id == "farmer").BonusValue;

            Step(sim, Plant(sim, "carrot_crop"));
            int steps = StepsUntilRipe(sim, x, y);

            Assert.AreEqual(Crop("carrot_crop").GrowthTicks / pace, steps, 2f);
        }

        [Test]
        public void Reaping_a_ripe_crop_gives_its_yield_and_seed_and_frees_the_tile()
        {
            var (sim, agent, x, y) = AtTheFarm("wheat_seeds");
            var crop = Crop("wheat_crop");
            Step(sim, Plant(sim, "wheat_crop"));
            StepsUntilRipe(sim, x, y);
            int seedsBefore = Village.Has(agent, "wheat_seeds");

            int actions = Kit.HarvestUntilYield(sim, agent, Kit.Item(crop.YieldItem));

            Assert.AreEqual(Kit.Data.NodeById("crop_mature").HarvestTicks, actions);
            Assert.AreEqual(crop.YieldQty, Village.Has(agent, crop.YieldItem));
            Assert.AreEqual(seedsBefore + crop.SeedReturn, Village.Has(agent, "wheat_seeds"));
            Assert.AreEqual(0, sim.World.NodeAt(x, y));
            Assert.AreEqual(Farmland, sim.World.TerrainAt(x, y));
            Assert.IsNull(sim.CropAt(x, y));
            Assert.IsTrue(sim.IsValid(agent.Index, Plant(sim, "wheat_crop")), "ready for the next sowing");
        }

        [Test]
        public void A_hoe_makes_reaping_quicker()
        {
            var (sim, agent, x, y) = AtTheFarm("carrot");
            int hoe = Kit.Item("farming_hoe");
            sim.AddItem(agent, hoe, 1);
            Step(sim, new AgentAction(sim.Actions.Equip, hoe));
            Step(sim, Plant(sim, "carrot_crop"));
            StepsUntilRipe(sim, x, y);
            int before = Village.Has(agent, "carrot");

            int actions = 0;
            while (Village.Has(agent, "carrot") <= before && actions < 50)
            {
                Step(sim, new AgentAction(sim.Actions.Harvest));
                actions++;
            }

            float speed = Kit.Data.EquipmentByItemInt(hoe).SpeedMult;
            Assert.Greater(speed, 1f);
            Assert.AreEqual(Kit.ExpectedActions(Kit.Data.NodeById("crop_mature").HarvestTicks, speed), actions);
        }

        [Test]
        public void Crops_in_the_field_die_when_winter_comes()
        {
            var sim = new Simulation(Kit.Data, new World(16, 16, Kit.Data.TerrainById("grass")), 1, "autumn") { WeatherEnabled = false, JobReviewsEnabled = false };
            sim.Research("agriculture");
            var farm = sim.PlaceBuilding("farm_windmill", 6, 6);
            var agent = sim.AddAgent(farm.X, farm.Y - 2, Kit.Neutral);
            Kit.Face(agent, 0, 1);
            sim.SetRole(agent, "cook");
            Village.Give(sim, agent, "carrot", 1);
            Step(sim, Plant(sim, "carrot_crop"));
            Assert.AreNotEqual(0, sim.World.NodeAt(farm.X, farm.Y - 1));

            for (int guard = 0; guard < 5000 && sim.Season.Id != "winter"; guard++) Step(sim, new AgentAction(sim.Actions.Wait));
            Step(sim, new AgentAction(sim.Actions.Wait));

            Assert.AreEqual(0, sim.World.NodeAt(farm.X, farm.Y - 1));
            Assert.IsNull(sim.CropAt(farm.X, farm.Y - 1));
            Assert.AreEqual(Farmland, sim.World.TerrainAt(farm.X, farm.Y - 1), "the plot itself stays");
        }

        [Test]
        public void A_growing_crop_is_part_of_the_state()
        {
            var (planted, _, _, _) = AtTheFarm("carrot");
            var (bare, _, _, _) = AtTheFarm("carrot");
            Step(planted, Plant(planted, "carrot_crop"));
            Step(bare, new AgentAction(bare.Actions.Wait));
            // Level the seed count, so only the crop differs.
            planted.AddItem(planted.Agents[0], Kit.Item("carrot"), 1);
            ulong before = planted.ComputeStateHash();

            Step(planted, new AgentAction(planted.Actions.Wait));
            Step(bare, new AgentAction(bare.Actions.Wait));

            Assert.AreNotEqual(bare.ComputeStateHash(), planted.ComputeStateHash());
            Assert.AreNotEqual(before, planted.ComputeStateHash());
        }
    }
}
