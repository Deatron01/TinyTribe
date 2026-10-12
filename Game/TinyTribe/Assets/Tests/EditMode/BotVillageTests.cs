using System;
using System.Linq;
using NUnit.Framework;
using TinyTribe.Brains;
using TinyTribe.Sim.Core;

namespace TinyTribe.Tests
{
    /// <summary>What the survival bot does about animals, fire and storage.</summary>
    public class BotVillageTests
    {
        static SimRunner Bots(Simulation sim) =>
            new SimRunner(sim, new SurvivalBrain(1)) { MaxMillisecondsPerAdvance = double.MaxValue };

        /// <summary>A villager that is fed, watered and armed, so only the behaviour under test is left to do.</summary>
        static Agent Settled(Simulation sim, int x, int y, bool armed = true)
        {
            var agent = sim.AddAgent(x, y, Kit.Neutral);
            Kit.Face(agent, 1, 0);
            sim.AddItem(agent, Kit.Item("berry"), 9);
            sim.AddItem(agent, Kit.Item("water"), 2);
            if (armed)
            {
                sim.AddItem(agent, Kit.Item("wooden_spear"), 1);
                var actions = sim.Agents.Select(a => a == agent ? new AgentAction(sim.Actions.Equip, Kit.Item("wooden_spear")) : new AgentAction(sim.Actions.Wait)).ToArray();
                sim.Step(actions);
            }
            return agent;
        }

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

        static int Distance(Agent agent, int x, int y) => Math.Max(Math.Abs(agent.X - x), Math.Abs(agent.Y - y));

        [Test]
        public void Villager_kills_a_wolf_that_comes_for_it()
        {
            var sim = Kit.NewSim(20, 9);
            var agent = Settled(sim, 4, 4);
            var wolf = sim.SpawnAnimal("wolf", 7, 4);

            Run(Bots(sim), () => !wolf.Alive, 40, "killing the wolf");

            Assert.IsTrue(agent.Alive);
            Assert.Greater(agent.Get(Need.Hp), 50f, "a spear should end the fight quickly");
        }

        [Test]
        public void Villager_turns_to_face_a_wolf_that_comes_from_the_side()
        {
            var sim = Kit.NewSim(20, 9);
            var agent = Settled(sim, 4, 4);
            var wolf = sim.SpawnAnimal("wolf", 4, 5);

            Run(Bots(sim), () => !wolf.Alive, 30, "killing the wolf beside it");

            Assert.IsTrue(agent.Alive);
        }

        [Test]
        public void Unarmed_villager_still_fights_back()
        {
            var sim = Kit.NewSim(20, 9);
            var agent = Settled(sim, 4, 4, armed: false);
            var wolf = sim.SpawnAnimal("wolf", 5, 4);

            Run(Bots(sim), () => !wolf.Alive || !agent.Alive, 80, "the fight ending");

            Assert.IsTrue(agent.Alive, "bare hands should just about beat one wolf");
            Assert.IsFalse(wolf.Alive);
        }

        [Test]
        public void Villager_backs_away_from_a_bear_and_is_not_caught()
        {
            var sim = Kit.NewSim(30, 9);
            var agent = Settled(sim, 10, 4);
            var bear = sim.SpawnAnimal("bear", 12, 4);
            var runner = Bots(sim);

            for (int t = 0; t < 20; t++) runner.StepOnce();

            Assert.AreEqual(100f, agent.Get(Need.Hp), 0.01f);
            Assert.Greater(Distance(agent, bear.X, bear.Y), Kit.Data.AnimalById("bear").AggroRadius);
        }

        [Test]
        public void Unarmed_villager_with_wood_makes_a_spear_and_holds_it()
        {
            var sim = Kit.NewSim(20, 9);
            var agent = Settled(sim, 4, 4, armed: false);
            sim.AddItem(agent, Kit.Item("pine_wood"), 2);

            Run(Bots(sim), () => agent.Equipped[(int)EquipSlot.Hand].Item == Kit.Item("wooden_spear"), 15, "arming itself");
        }

        [Test]
        public void Unarmed_villager_fetches_wood_for_a_spear()
        {
            var sim = Kit.NewSim(20, 9);
            sim.World.PlaceNode(8, 4, Kit.Data.NodeById("pine_tree"));
            var agent = Settled(sim, 4, 4, armed: false);

            Run(Bots(sim), () => agent.Equipped[(int)EquipSlot.Hand].Item == Kit.Item("wooden_spear"), 80, "arming itself from a tree");
        }

        /// <summary>Open ground with wood, stone, berries and water close by.</summary>
        static Simulation Homestead()
        {
            var sim = Kit.NewSim(24, 14);
            sim.World.SpawnX = 12;
            sim.World.SpawnY = 7;
            var pine = Kit.Data.NodeById("pine_tree");
            foreach (var (x, y) in new[] { (17, 4), (17, 6), (17, 8), (18, 10), (19, 5) }) sim.World.PlaceNode(x, y, pine);
            sim.World.PlaceNode(6, 10, Kit.Data.NodeById("surface_rock"));
            sim.World.PlaceNode(5, 4, Kit.Data.NodeById("berry_bush"));
            sim.World.PlaceNode(7, 3, Kit.Data.NodeById("berry_bush"));
            for (int x = 1; x <= 3; x++)
            {
                sim.World.SetTerrain(x, 1, Kit.Data.TerrainById("water"));
                sim.World.PlaceNode(x, 1, Kit.Data.NodeById("water_source"));
            }
            return sim;
        }

        static int Count(Simulation sim, string buildingId, bool finishedOnly) =>
            sim.Buildings.Count(b => b.Def.Id == buildingId && (b.Complete || !finishedOnly));

        static int FromVillageCentre(Simulation sim, string buildingId)
        {
            var building = sim.Buildings.First(b => b.Def.Id == buildingId);
            return Math.Max(Math.Abs(building.X - sim.World.SpawnX), Math.Abs(building.Y - sim.World.SpawnY));
        }

        [Test]
        public void A_lone_villager_builds_a_chest_and_then_the_village_campfire()
        {
            var sim = Homestead();
            var agent = sim.AddAgent(12, 7, Kit.Neutral);
            var runner = Bots(sim);

            int chestAt = Run(runner, () => Count(sim, "basic_chest", true) == 1, 700, "building a chest");
            // Packs are small: the campfire's builder needs somewhere to leave its tools.
            Assert.AreEqual(0, Count(sim, "campfire", false), "the chest comes first");
            int fireAt = chestAt + Run(runner, () => Count(sim, "campfire", true) == 1, 900, "building a campfire");

            Assert.IsTrue(agent.Alive);
            Assert.LessOrEqual(FromVillageCentre(sim, "basic_chest"), 5, "the chest belongs in the village");
            Assert.LessOrEqual(FromVillageCentre(sim, "campfire"), 5, "the fire belongs in the village");
            Assert.AreEqual(Kit.Item("wooden_spear"), agent.Equipped[(int)EquipSlot.Hand].Item, "the pickaxe was only for the stones");
            Assert.Pass("chest after " + chestAt + " ticks, campfire after " + fireAt);
        }

        [Test]
        public void A_village_builds_one_chest_and_one_campfire_not_one_each()
        {
            var sim = Homestead();
            // The homestead feeds one; four need more bushes.
            foreach (var (x, y) in new[] { (9, 2), (11, 2), (13, 2), (15, 2), (9, 12), (11, 12), (13, 12), (15, 12) })
                sim.World.PlaceNode(x, y, Kit.Data.NodeById("berry_bush"));
            foreach (var (x, y) in new[] { (12, 7), (11, 7), (13, 7), (12, 8) }) sim.AddAgent(x, y, Kit.Neutral);
            var runner = Bots(sim);

            Run(runner, () => Count(sim, "campfire", true) >= 1, 1500, "building a campfire");
            for (int t = 0; t < 200; t++) runner.StepOnce();

            Assert.AreEqual(1, Count(sim, "campfire", false));
            Assert.AreEqual(1, Count(sim, "basic_chest", false));
        }

        [Test]
        public void The_builder_puts_its_pickaxe_in_the_chest_once_it_has_the_stones()
        {
            var sim = Homestead();
            sim.AddAgent(12, 7, Kit.Neutral);

            Run(Bots(sim), () => Count(sim, "campfire", true) == 1, 1600, "building a campfire");

            var chest = sim.Buildings.First(b => b.Def.Id == "basic_chest");
            Assert.AreEqual(1, Village.StoredIn(chest, "wooden_pickaxe"));
        }

        [Test]
        public void The_builder_keeps_the_fire_burning_for_days()
        {
            var sim = Homestead();
            sim.AddAgent(12, 7, Kit.Neutral);
            var runner = Bots(sim);
            Run(runner, () => Count(sim, "campfire", true) == 1, 1600, "building a campfire");
            var fire = sim.Buildings.First(b => b.Def.Id == "campfire");

            int unlit = 0;
            for (int t = 0; t < 4 * Kit.Data.ConfigInt("ticks_per_day"); t++)
            {
                runner.StepOnce();
                if (!fire.IsLit) unlit++;
            }

            Assert.AreEqual(0, unlit, "ticks without a fire");
        }

        [Test]
        public void Villager_cooks_raw_meat_at_the_fire()
        {
            var sim = Kit.NewSim(20, 9);
            var fire = sim.PlaceBuilding("campfire", 10, 4);
            fire.FuelTicks = 2000;
            var agent = Settled(sim, 4, 4);
            sim.AddItem(agent, Kit.Item("raw_meat"), 2);

            Run(Bots(sim), () => agent.Count(Kit.Item("cooked_meat")) == 2, 40, "cooking the meat");

            Assert.AreEqual(0, agent.Count(Kit.Item("raw_meat")));
        }

        [Test]
        public void Villager_leaves_loot_it_cannot_use_in_the_chest()
        {
            var sim = Kit.NewSim(20, 9);
            var chest = sim.PlaceBuilding("basic_chest", 10, 4);
            var agent = Settled(sim, 4, 4);
            sim.AddItem(agent, Kit.Item("animal_hide"), 1);
            sim.AddItem(agent, Kit.Item("raw_meat"), 1);

            Run(Bots(sim), () => Village.StoredIn(chest, "animal_hide") == 1 && Village.StoredIn(chest, "raw_meat") == 1, 40, "storing the loot");

            Assert.AreEqual(9, agent.Count(Kit.Item("berry")), "food stays in the pack");
            Assert.AreEqual(2, agent.Count(Kit.Item("water")));
        }

        [Test]
        public void Villager_feeds_a_fire_that_is_burning_low()
        {
            var sim = Kit.NewSim(20, 9);
            var fire = sim.PlaceBuilding("campfire", 10, 4);
            fire.FuelTicks = 60;
            var agent = Settled(sim, 4, 4);
            agent.Inventory[1] = default;
            sim.AddItem(agent, Kit.Item("pine_wood"), 4);

            Run(Bots(sim), () => fire.FuelTicks > 100, 30, "adding wood to the fire");
        }

        [Test]
        public void Villager_relights_a_dead_fire()
        {
            var sim = Kit.NewSim(20, 9);
            var fire = sim.PlaceBuilding("campfire", 10, 4);
            fire.FuelTicks = 0;
            var agent = Settled(sim, 4, 4);
            agent.Inventory[1] = default;
            sim.AddItem(agent, Kit.Item("pine_wood"), 4);

            Run(Bots(sim), () => fire.IsLit, 30, "relighting the fire");
        }

        [Test]
        public void Villager_sleeps_by_the_fire_on_a_cold_night()
        {
            var sim = Kit.NewSim(24, 9);
            Kit.AdvanceToSeason(sim, "autumn", 23);
            var fire = sim.PlaceBuilding("campfire", 12, 4);
            fire.FuelTicks = 2000;
            var agent = Settled(sim, 3, 4);
            int radius = (int)fire.Def.EffectValue;

            Run(Bots(sim), () => agent.Sleeping && Distance(agent, fire.X, fire.Y) <= radius, 30, "settling down by the fire");
        }

        [Test]
        public void Cold_villager_goes_to_the_fire_and_warms_up()
        {
            var sim = Kit.NewSim(24, 9);
            Kit.AdvanceToSeason(sim, "winter", 12);
            var fire = sim.PlaceBuilding("campfire", 12, 4);
            fire.FuelTicks = 2000;
            var agent = Settled(sim, 3, 4);
            agent.Set(Need.Temp, 40);

            Run(Bots(sim), () => agent.Get(Need.Temp) > 60f, 80, "warming up");

            Assert.LessOrEqual(Distance(agent, fire.X, fire.Y), (int)fire.Def.EffectValue);
        }

        [Test]
        public void Villager_stores_a_full_stack_of_food_in_the_village_chest()
        {
            var sim = Kit.NewSim(20, 9);
            var chest = sim.PlaceBuilding("basic_chest", 10, 4);
            var agent = Settled(sim, 4, 4);
            sim.AddItem(agent, Kit.Item("berry"), 1);
            Assert.AreEqual(Kit.Data.ItemById["berry"].MaxStack, agent.Count(Kit.Item("berry")));

            Run(Bots(sim), () => Village.StoredIn(chest, "berry") > 0, 30, "storing food");

            Assert.AreEqual(Kit.Data.ItemById["berry"].MaxStack, Village.StoredIn(chest, "berry"));
        }

        [Test]
        public void Hungry_villager_with_nothing_to_forage_eats_from_the_chest()
        {
            var sim = Kit.NewSim(20, 9);
            var chest = sim.PlaceBuilding("basic_chest", 10, 4);
            chest.Storage[0] = new ItemStack { Item = Kit.Item("berry"), Qty = 10 };
            var agent = Settled(sim, 4, 4);
            agent.Inventory[0] = default;
            agent.Set(Need.Hunger, 30);
            int berry = Kit.Item("berry");
            var runner = Bots(sim);

            Run(runner, () => runner.Sim.Events.Any(e => e.Type == SimEventType.Consumed && e.Item == berry), 30, "eating from the chest");
        }

        [Test]
        public void Villager_ignores_a_wolf_it_cannot_reach_and_looks_after_itself()
        {
            var sim = Kit.NewSim(20, 9);
            // A river with the wolf on the far bank, two tiles from the villager.
            for (int y = 0; y < 9; y++)
            {
                sim.World.SetTerrain(6, y, Kit.Data.TerrainById("water"));
                sim.World.PlaceNode(6, y, Kit.Data.NodeById("water_source"));
            }
            var agent = Settled(sim, 5, 4);
            var wolf = sim.SpawnAnimal("wolf", 7, 4);
            agent.Set(Need.Thirst, 20);
            agent.Inventory[1] = default;

            Run(Bots(sim), () => agent.Get(Need.Thirst) > 50f, 10, "drinking with a wolf across the water");

            Assert.IsTrue(wolf.Alive);
        }

        [Test]
        public void Villager_with_a_pack_full_of_loot_puts_it_down_to_make_room_for_food()
        {
            var sim = Kit.NewSim(20, 9);
            sim.World.PlaceNode(8, 4, Kit.Data.NodeById("berry_bush"));
            var agent = Settled(sim, 4, 4);
            agent.Inventory[0] = default;
            agent.Inventory[1] = default;
            foreach (string loot in new[] { "animal_hide", "bone", "cotton", "herb" }) sim.AddItem(agent, Kit.Item(loot), 1);
            Assert.AreEqual(0, agent.FreeSlots);
            agent.Set(Need.Hunger, 30);
            int berry = Kit.Item("berry");
            var runner = Bots(sim);

            Run(runner, () => runner.Sim.Events.Any(e => e.Type == SimEventType.Consumed && e.Item == berry), 40, "eating");
        }

        [Test]
        public void Villager_keeps_loot_while_there_is_room_in_the_pack()
        {
            var sim = Kit.NewSim(20, 9);
            var agent = Settled(sim, 4, 4);
            // Someone else is the builder, who keeps more of its pack free.
            sim.SetRole(Settled(sim, 14, 4), "builder");
            sim.SetRole(agent, "cook");
            sim.AddItem(agent, Kit.Item("animal_hide"), 1);
            Assert.AreEqual(1, agent.FreeSlots);
            var runner = Bots(sim);

            for (int t = 0; t < 60; t++) runner.StepOnce();

            Assert.AreEqual(1, agent.Count(Kit.Item("animal_hide")), "it will go in the chest once there is one");
        }

        [Test]
        public void Bots_never_choose_an_action_that_is_already_invalid_in_a_village_with_animals_and_buildings()
        {
            var setup = new GameSetup { Brains = BrainKind.Survival };
            setup.Sim.Seed = 2;
            var runner = SimRunner.Create(Kit.Data, setup);
            var checker = ValidityCheckingBrain.WrapAll(runner);

            for (int t = 0; t < 6 * Kit.Data.ConfigInt("ticks_per_day"); t++) runner.StepOnce();

            Assert.AreEqual(0, checker.InvalidChoices);
        }

        [Test]
        public void The_fire_is_not_fed_in_the_rain_but_relit_when_it_clears()
        {
            var sim = Kit.NewSim(20, 9);
            var fire = sim.PlaceBuilding("campfire", 10, 4);
            fire.FuelTicks = 0;
            var agent = Settled(sim, 4, 4);
            agent.Inventory[1] = default;
            int wood = Kit.Item("pine_wood");
            sim.AddItem(agent, wood, 4);
            var runner = Bots(sim);

            sim.SetWeather("rain", 40);
            while (sim.Weather.Id == "rain") runner.StepOnce();
            Assert.AreEqual(4, agent.Count(wood), "no wood wasted on a fire the rain would put out again");

            Run(runner, () => fire.IsLit, 30, "relighting the fire once the rain has stopped");
        }

        [Test]
        public void With_a_library_the_bots_choose_a_technology_and_the_scholar_studies_it()
        {
            var sim = Kit.NewSim(24, 11);
            sim.PlaceBuilding("library", 12, 4);
            var scholar = Settled(sim, 6, 5);
            sim.SetRole(scholar, "scholar");
            sim.SetRole(Settled(sim, 20, 9), "builder");
            var runner = Bots(sim);

            Run(runner, () => sim.ResearchingTech != null, 5, "choosing what to research");
            Run(runner, () => sim.ResearchPoints > 0f, 40, "studying at the library");

            Assert.AreEqual(sim.Actions.CraftBuild, scholar.LastAction.Id);
        }

        // ------------------------------------------------------------------ habits

        [Test]
        public void A_villager_out_of_water_fills_its_waterskin_in_one_trip()
        {
            var sim = Kit.NewSim(24, 9);
            for (int y = 0; y < 9; y++)
            {
                sim.World.SetTerrain(0, y, Kit.Data.TerrainById("water"));
                sim.World.PlaceNode(0, y, Kit.Data.NodeById("water_source"));
            }
            var agent = Settled(sim, 12, 4);
            // Not one of the jobs that fish: those work at the water and carry none.
            sim.SetRole(agent, "scholar");
            agent.Inventory[1] = default;
            int water = Kit.Item("water");
            var runner = Bots(sim);

            Run(runner, () => agent.Count(water) >= 4, 60, "filling up");

            Assert.LessOrEqual(agent.X, 2, "it stayed at the water until the skin was full");
        }

        [Test]
        public void A_villager_with_some_water_left_does_not_walk_off_for_more()
        {
            var sim = Kit.NewSim(24, 9);
            for (int y = 0; y < 9; y++)
            {
                sim.World.SetTerrain(0, y, Kit.Data.TerrainById("water"));
                sim.World.PlaceNode(0, y, Kit.Data.NodeById("water_source"));
            }
            var agent = Settled(sim, 12, 4);
            sim.SetRole(agent, "scholar");
            agent.Inventory[1].Qty = 1;
            var runner = Bots(sim);

            for (int t = 0; t < 40; t++) runner.StepOnce();

            Assert.Greater(agent.X, 6, "one waterskin is enough to stay put");
        }

        [Test]
        public void A_lumberjack_makes_itself_an_axe_and_works_with_it()
        {
            var sim = Kit.NewSim(24, 9);
            var pine = Kit.Data.NodeById("pine_tree");
            foreach (var (x, y) in new[] { (16, 2), (17, 4), (16, 6), (18, 3), (18, 6) }) sim.World.PlaceNode(x, y, pine);
            sim.PlaceBuilding("basic_chest", 10, 4);
            var lumberjack = Settled(sim, 12, 4);
            sim.SetRole(lumberjack, "lumberjack");
            sim.SetRole(Settled(sim, 4, 6), "builder");
            int axe = Kit.Item("wooden_axe");
            var runner = Bots(sim);

            Run(runner, () => lumberjack.Equipped[(int)EquipSlot.Hand].Item == axe, 120, "making an axe");
            int woodBefore = lumberjack.Count(Kit.Item("pine_wood"));
            Run(runner, () => lumberjack.Count(Kit.Item("pine_wood")) > woodBefore, 40, "felling with the axe");

            Assert.IsTrue(lumberjack.Count(Kit.Item("wooden_spear")) > 0, "the spear stays in the pack for wolves");
        }

        // ------------------------------------------------------------------ fishing

        /// <summary>A lake on the left, a lit fire in the middle, a pine and a cotton plant, a fisher and a builder.</summary>
        static (Simulation sim, Agent fisher, Building fire) Lakeside(string fisherRole = "butcher")
        {
            var sim = Kit.NewSim(24, 11);
            for (int y = 0; y < 11; y++)
                for (int x = 0; x <= 2; x++)
                {
                    sim.World.SetTerrain(x, y, Kit.Data.TerrainById("water"));
                    sim.World.PlaceNode(x, y, Kit.Data.NodeById("water_source"));
                }
            sim.World.PlaceNode(8, 2, Kit.Data.NodeById("pine_tree"));
            sim.World.PlaceNode(9, 8, Kit.Data.NodeById("cotton_plant"));
            var fire = sim.PlaceBuilding("campfire", 12, 5);
            fire.FuelTicks = 100000;
            var fisher = Settled(sim, 6, 5);
            sim.SetRole(fisher, fisherRole);
            sim.SetRole(Settled(sim, 20, 8), "builder");
            return (sim, fisher, fire);
        }

        [Test]
        public void A_fisher_makes_a_rod_from_wood_and_cotton()
        {
            var (sim, fisher, _) = Lakeside();

            Run(Bots(sim), () => fisher.Count(Kit.Item("fishing_rod")) > 0 || fisher.Equipped[(int)EquipSlot.Hand].Item == Kit.Item("fishing_rod"), 200, "making a fishing rod");

            Assert.IsTrue(fisher.Count(Kit.Item("wooden_spear")) > 0 || fisher.Equipped[(int)EquipSlot.Hand].Item == Kit.Item("wooden_spear"), "and it still has its spear");
        }

        [Test]
        public void A_fisher_with_a_rod_catches_fish_and_cooks_them()
        {
            var (sim, fisher, _) = Lakeside();
            sim.AddItem(fisher, Kit.Item("fishing_rod"), 1);

            Run(Bots(sim), () => fisher.Count(Kit.Item("cooked_fish")) > 0, 250, "fishing and cooking");
        }

        [Test]
        public void A_fisher_takes_its_spear_in_hand_when_a_wolf_comes()
        {
            var (sim, fisher, _) = Lakeside();
            sim.AddItem(fisher, Kit.Item("fishing_rod"), 1);
            var runner = Bots(sim);
            Run(runner, () => fisher.Count(Kit.Item("raw_fish")) > 0, 150, "the first catch");
            Assert.AreEqual(Kit.Item("fishing_rod"), fisher.Equipped[(int)EquipSlot.Hand].Item);
            // Beside the fisher, on whichever side is free.
            var spot = new[] { (0, 1), (0, -1), (1, 0) }.Select(d => (x: fisher.X + d.Item1, y: fisher.Y + d.Item2)).First(p => sim.CanEnter(p.x, p.y));
            var wolf = sim.SpawnAnimal("wolf", spot.x, spot.y);

            Run(runner, () => !wolf.Alive, 40, "killing the wolf");

            Assert.IsTrue(fisher.Alive);
            Assert.Greater(fisher.Get(Need.Hp), 50f, "it fought with the spear, not the rod");
        }

        static bool HasRod(Agent agent) =>
            agent.Count(Kit.Item("fishing_rod")) > 0 || agent.Equipped[(int)EquipSlot.Hand].Item == Kit.Item("fishing_rod");

        [Test]
        public void A_fisher_with_a_small_pack_still_makes_its_rod_and_fishes()
        {
            var (sim, _, _) = Lakeside();
            var small = sim.AddAgent(6, 7, Kit.Neutral.WithStrength(2));
            sim.SetRole(small, "farmer");
            sim.AddItem(small, Kit.Item("berry"), 9);
            sim.AddItem(small, Kit.Item("water"), 2);
            sim.AddItem(small, Kit.Item("wooden_spear"), 1);
            Assert.AreEqual(0, small.FreeSlots, "three slots, all taken");
            var runner = Bots(sim);

            Run(runner, () => HasRod(small), 300, "making a rod with a three-slot pack");
            Run(runner, () => small.Count(Kit.Item("raw_fish")) + small.Count(Kit.Item("cooked_fish")) > 0, 150, "the first catch");
        }

        [Test]
        public void A_fisher_puts_cotton_by_in_the_chest_for_the_next_rod()
        {
            var (sim, fisher, _) = Lakeside();
            var chest = sim.PlaceBuilding("basic_chest", 14, 7);
            sim.World.PlaceNode(10, 9, Kit.Data.NodeById("cotton_plant"));
            sim.AddItem(fisher, Kit.Item("fishing_rod"), 1);

            Run(Bots(sim), () => Village.StoredIn(chest, "cotton") >= 2, 300, "storing cotton");
        }

        [Test]
        public void A_fisher_whose_rod_broke_in_winter_makes_a_new_one_from_the_chest()
        {
            var sim = new Simulation(Kit.Data, new World(24, 11, Kit.Data.TerrainById("grass")), 1, "winter") { WeatherEnabled = false, JobReviewsEnabled = false };
            for (int y = 0; y < 11; y++)
                for (int x = 0; x <= 2; x++)
                {
                    sim.World.SetTerrain(x, y, Kit.Data.TerrainById("water"));
                    sim.World.PlaceNode(x, y, Kit.Data.NodeById("water_source"));
                }
            sim.PlaceBuilding("campfire", 12, 5).FuelTicks = 100000;
            var chest = sim.PlaceBuilding("basic_chest", 14, 7);
            chest.Storage[0] = new ItemStack { Item = Kit.Item("cotton"), Qty = 2 };
            chest.Storage[1] = new ItemStack { Item = Kit.Item("pine_wood"), Qty = 6 };
            chest.Storage[2] = new ItemStack { Item = Kit.Item("berry"), Qty = 10 };
            var fisher = Settled(sim, 8, 5);
            sim.SetRole(fisher, "butcher");
            sim.SetRole(Settled(sim, 20, 8), "builder");
            var runner = Bots(sim);

            Run(runner, () => HasRod(fisher), 120, "making a rod from the chest's cotton and wood");
            Run(runner, () => fisher.Count(Kit.Item("raw_fish")) + fisher.Count(Kit.Item("cooked_fish")) > 0, 150, "fishing in winter");
        }

        [Test]
        public void Nobody_fishes_before_there_is_a_fire_to_cook_on()
        {
            var (sim, fisher, fire) = Lakeside();
            sim.DestroyBuilding(fire);
            sim.AddItem(fisher, Kit.Item("fishing_rod"), 1);
            var runner = Bots(sim);
            int rawFish = Kit.Item("raw_fish"), caught = 0;

            for (int t = 0; t < 150; t++)
            {
                runner.StepOnce();
                caught += sim.Events.Count(e => e.Type == SimEventType.Harvested && e.Item == rawFish);
            }

            Assert.AreEqual(0, caught, "raw fish cannot be eaten safely and there is nowhere to cook it");
        }

        [Test]
        public void Other_villagers_do_not_fish()
        {
            var (sim, scholar, _) = Lakeside("scholar");
            sim.AddItem(scholar, Kit.Item("fishing_rod"), 1);
            var runner = Bots(sim);

            for (int t = 0; t < 150; t++) runner.StepOnce();

            Assert.AreEqual(0, scholar.Count(Kit.Item("raw_fish")) + scholar.Count(Kit.Item("cooked_fish")));
            Assert.AreEqual(Kit.Item("wooden_spear"), scholar.Equipped[(int)EquipSlot.Hand].Item);
        }

        // ------------------------------------------------------------------ advice from the situation alone

        [Test]
        public void A_memoryless_bot_gives_the_same_advice_whatever_it_advised_before()
        {
            // Two villages alike. In one the bot is asked every tick while the villagers do nothing it says;
            // in the other it is asked for the first time at the end. A thirsty villager far from the lake: same advice.
            AgentAction Advice(bool askedAllAlong)
            {
                var (sim, villager, _) = Lakeside("scholar");
                var bot = new SurvivalBrain(1) { Memoryless = true };
                var wait = sim.Agents.Select(_ => new AgentAction(sim.Actions.Wait)).ToArray();
                for (int t = 0; t < 25; t++)
                {
                    foreach (var agent in sim.Agents) Kit.Refill(agent);
                    if (askedAllAlong)
                        foreach (var agent in sim.Agents) bot.Decide(sim, agent.Index);
                    sim.Step(wait);
                }
                foreach (var agent in sim.Agents) Kit.Refill(agent);
                villager.Set(Need.Thirst, 10);
                while (villager.Count(Kit.Item("water")) > 0) sim.Step(sim.Agents.Select(a => a == villager ? new AgentAction(sim.Actions.Consume, Kit.Item("water")) : new AgentAction(sim.Actions.Wait)).ToArray());
                villager.Set(Need.Thirst, 10);
                AgentAction advice = default;
                foreach (var agent in sim.Agents)
                {
                    var given = bot.Decide(sim, agent.Index);
                    if (agent == villager) advice = given;
                }
                return advice;
            }

            var fresh = Advice(askedAllAlong: false);
            var worn = Advice(askedAllAlong: true);

            Assert.AreEqual(fresh.Id, worn.Id);
            Assert.AreEqual(fresh.Arg, worn.Arg);
        }

        [Test]
        public void A_memoryless_bot_takes_what_a_villager_is_in_the_middle_of_from_what_it_sees()
        {
            var (sim, villager, _) = Lakeside("scholar");
            var bot = new SurvivalBrain(1) { Memoryless = true };
            foreach (var agent in sim.Agents) Kit.Refill(agent);
            // Tired enough to have lain down, not yet rested: a villager seen asleep is told to sleep on.
            villager.Set(Need.Energy, 20);
            sim.Step(sim.Agents.Select(a => new AgentAction(a == villager ? sim.Actions.Sleep : sim.Actions.Wait)).ToArray());
            Assert.IsTrue(villager.Sleeping);
            foreach (var agent in sim.Agents) Kit.Refill(agent);
            villager.Set(Need.Energy, 22);

            AgentAction advice = default;
            foreach (var agent in sim.Agents)
            {
                var given = bot.Decide(sim, agent.Index);
                if (agent == villager) advice = given;
            }

            Assert.AreEqual(sim.Actions.Sleep, advice.Id, "asleep and still tired");

            // The same villager awake at the same tiredness is not sent to bed: 22 is above where a nap starts.
            sim.Step(sim.Agents.Select(_ => new AgentAction(sim.Actions.Wait)).ToArray());
            foreach (var agent in sim.Agents) Kit.Refill(agent);
            villager.Set(Need.Energy, 22);
            Assert.IsFalse(villager.Sleeping);
            foreach (var agent in sim.Agents)
            {
                var given = bot.Decide(sim, agent.Index);
                if (agent == villager) advice = given;
            }
            Assert.AreNotEqual(sim.Actions.Sleep, advice.Id);
        }

        [TestCase(1UL)]
        [TestCase(2UL)]
        public void Followed_to_the_letter_a_memoryless_bot_is_the_bot(ulong seed)
        {
            ulong Played(bool memoryless)
            {
                var sim = Simulation.Create(Kit.Data, new SimulationOptions { Seed = seed });
                var runner = new SimRunner(sim, new SurvivalBrain(seed) { Memoryless = memoryless }) { MaxMillisecondsPerAdvance = double.MaxValue };
                for (int t = 0; t < Kit.Data.ConfigInt("ticks_per_day") * Kit.Data.ConfigInt("days_per_season"); t++) runner.StepOnce();
                return sim.ComputeStateHash();
            }

            Assert.AreEqual(Played(memoryless: false), Played(memoryless: true), "a villager that does as advised keeps the plan the advice came from");
        }

        // ------------------------------------------------------------------ the Council

        [Test]
        public void Bots_vote_as_one_so_a_role_is_given_on_the_spot()
        {
            var sim = Kit.NewSim(20, 9);
            for (int i = 0; i < 3; i++) sim.AddAgent(4 + i * 3, 4, Kit.Neutral);
            var runner = Bots(sim);

            runner.StepOnce();
            Assert.IsNotNull(sim.OpenVote);
            runner.StepOnce();

            Assert.AreNotEqual(0, sim.Agents[0].Role, "all three voted alike on the first tick they could");
        }

        [Test]
        public void Bots_give_every_villager_a_role_on_the_first_morning()
        {
            var setup = new GameSetup { Brains = BrainKind.Survival };
            setup.Sim.Seed = 1;
            var runner = SimRunner.Create(Kit.Data, setup);
            var sim = runner.Sim;

            for (int t = 0; t < 30; t++) runner.StepOnce();

            CollectionAssert.DoesNotContain(sim.Agents.Select(a => a.Role).ToArray(), 0);
            Assert.AreEqual(1, sim.Agents.Count(a => a.Role == Kit.Data.RoleInt("builder")), "one builder to start with");
            Assert.AreEqual(sim.Agents.Count, sim.Agents.Select(a => a.Role).Distinct().Count(), "six villagers, six different jobs");
        }

        [Test]
        public void Bots_pick_the_role_that_suits_the_villager()
        {
            var sim = Kit.NewSim(20, 9);
            var strong = Kit.Neutral;
            strong.Str = 10;
            strong.End = 10;
            strong.Dex = 1;
            strong.Int = 1;
            var deft = Kit.Neutral;
            deft.Dex = 10;
            deft.End = 9;
            deft.Str = 4;
            deft.Int = 1;
            sim.AddAgent(4, 4, strong);
            sim.AddAgent(8, 4, deft);
            var runner = Bots(sim);

            for (int t = 0; t < 6; t++) runner.StepOnce();

            Assert.AreEqual(Kit.Data.RoleInt("builder"), sim.Agents[1].Role, "the builder works with DEX and END");
            Assert.AreEqual(Kit.Data.RoleInt("lumberjack"), sim.Agents[0].Role, "the lumberjack with STR and END");
        }

        [Test]
        public void The_villager_the_council_made_builder_does_the_building()
        {
            var sim = Homestead();
            var deft = Kit.Neutral;
            deft.Dex = 10;
            deft.End = 10;
            var clumsy = Kit.Neutral;
            clumsy.Dex = 2;
            clumsy.End = 2;
            foreach (var (x, y, stats) in new[] { (12, 7, clumsy), (11, 7, clumsy), (13, 7, deft), (12, 8, clumsy) }) sim.AddAgent(x, y, stats);
            var runner = Bots(sim);
            int chestArg = Kit.Data.ArgFor("basic_chest");
            var builders = new System.Collections.Generic.HashSet<int>();

            for (int t = 0; t < 900 && Count(sim, "basic_chest", true) == 0; t++)
            {
                runner.StepOnce();
                foreach (var agent in sim.Agents)
                    if (agent.LastAction.Id == sim.Actions.CraftBuild && agent.LastAction.Arg == chestArg && agent.LastActionValid) builders.Add(agent.Index);
            }

            Assert.AreEqual(1, Count(sim, "basic_chest", true));
            Assert.AreEqual(Kit.Data.RoleInt("builder"), sim.Agents[2].Role);
            CollectionAssert.AreEquivalent(new[] { 2 }, builders);
        }

        [Test]
        public void A_lumberjack_keeps_wood_in_the_village_chest()
        {
            var sim = Kit.NewSim(24, 9);
            var pine = Kit.Data.NodeById("pine_tree");
            foreach (var (x, y) in new[] { (16, 2), (17, 4), (16, 6), (18, 3), (18, 6) }) sim.World.PlaceNode(x, y, pine);
            var chest = sim.PlaceBuilding("basic_chest", 10, 4);
            var lumberjack = Settled(sim, 6, 4);
            var builder = Settled(sim, 4, 6);
            sim.SetRole(lumberjack, "lumberjack");
            sim.SetRole(builder, "builder");

            Run(Bots(sim), () => Village.StoredIn(chest, "pine_wood") >= Kit.Data.ItemById["pine_wood"].MaxStack, 600, "stocking wood");
        }

        [Test]
        public void A_lumberjack_feeds_the_fire_too()
        {
            var sim = Kit.NewSim(20, 9);
            var fire = sim.PlaceBuilding("campfire", 10, 4);
            fire.FuelTicks = 30;
            var lumberjack = Settled(sim, 4, 4);
            sim.SetRole(lumberjack, "lumberjack");
            sim.SetRole(Settled(sim, 16, 7), "builder");
            sim.AddItem(lumberjack, Kit.Item("pine_wood"), 3);

            Run(Bots(sim), () => fire.FuelTicks > 100, 30, "the lumberjack adding wood to the fire");

            Assert.Less(lumberjack.Count(Kit.Item("pine_wood")), 3);
        }

        [Test]
        public void Food_is_not_stored_in_the_last_free_slots_of_the_chest()
        {
            var sim = Kit.NewSim(20, 9);
            var chest = sim.PlaceBuilding("basic_chest", 10, 4);
            // Every slot but two holds something that is not berries.
            for (int slot = 0; slot < chest.Storage.Length - 2; slot++)
                chest.Storage[slot] = new ItemStack { Item = Kit.Item("carrot"), Qty = Kit.Data.ItemById["carrot"].MaxStack };
            var agent = Settled(sim, 4, 4);
            sim.SetRole(agent, "cook");
            sim.AddItem(agent, Kit.Item("berry"), 1);
            Assert.AreEqual(Kit.Data.ItemById["berry"].MaxStack, agent.Count(Kit.Item("berry")));
            var runner = Bots(sim);

            for (int t = 0; t < 60; t++) runner.StepOnce();

            Assert.AreEqual(0, Village.StoredIn(chest, "berry"), "the last slots are kept for wood and tools");
        }

        [Test]
        public void A_lumberjack_does_not_pile_up_wood_when_the_chest_is_full()
        {
            var sim = Kit.NewSim(24, 9);
            var pine = Kit.Data.NodeById("pine_tree");
            foreach (var (x, y) in new[] { (16, 2), (17, 4), (16, 6) }) sim.World.PlaceNode(x, y, pine);
            var chest = sim.PlaceBuilding("basic_chest", 10, 4);
            for (int slot = 0; slot < chest.Storage.Length; slot++)
                chest.Storage[slot] = new ItemStack { Item = Kit.Item("carrot"), Qty = Kit.Data.ItemById["carrot"].MaxStack };
            var lumberjack = Settled(sim, 6, 4);
            sim.SetRole(lumberjack, "lumberjack");
            var runner = Bots(sim);

            for (int t = 0; t < 150; t++) runner.StepOnce();

            Assert.AreEqual(0, lumberjack.Count(Kit.Item("pine_wood")));
        }

        [Test]
        public void Hungry_villager_with_no_room_puts_something_down_to_eat_from_the_chest()
        {
            var sim = Kit.NewSim(20, 9);
            var chest = sim.PlaceBuilding("basic_chest", 10, 4);
            for (int slot = 0; slot < chest.Storage.Length; slot++)
                chest.Storage[slot] = new ItemStack { Item = Kit.Item("berry"), Qty = Kit.Data.ItemById["berry"].MaxStack };
            var agent = Settled(sim, 4, 4);
            sim.SetRole(agent, "lumberjack");
            agent.Inventory[0] = default;
            // Wood in every free slot: wanted by a lumberjack, and the full chest will not take it.
            while (agent.FreeSlots > 0) sim.AddItem(agent, Kit.Item("pine_wood"), Kit.Data.ItemById["pine_wood"].MaxStack);
            agent.Set(Need.Hunger, 30);
            int berry = Kit.Item("berry");
            var runner = Bots(sim);

            Run(runner, () => runner.Sim.Events.Any(e => e.Type == SimEventType.Consumed && e.Item == berry), 40, "eating");
        }

        /// <summary>
        /// The whole loop at once: wolves, a chest, a fire kept burning, food put by for winter.
        /// One villager may be lost; more means something the village depends on has stopped working.
        /// </summary>
        [Test]
        public void Bot_village_lives_through_a_whole_year([Values(2UL, 3UL, 4UL, 5UL)] ulong seed)
        {
            int ticksPerSeason = Kit.Data.ConfigInt("days_per_season") * Kit.Data.ConfigInt("ticks_per_day");
            var setup = new GameSetup { Brains = BrainKind.Survival };
            setup.Sim.Seed = seed;
            var runner = SimRunner.Create(Kit.Data, setup);
            var sim = runner.Sim;
            var report = new System.Text.StringBuilder("alive after each season:");

            for (int season = 0; season < Kit.Data.Seasons.Count; season++)
            {
                for (int t = 0; t < ticksPerSeason; t++) runner.StepOnce();
                report.Append(' ').Append(VillageSummary.AliveCount(sim));
            }

            Assert.AreEqual(1, Count(sim, "basic_chest", true), "chests");
            Assert.AreEqual(1, Count(sim, "campfire", true), "campfires");
            Assert.GreaterOrEqual(VillageSummary.AliveCount(sim), sim.Agents.Count - 1, report.ToString());
            Assert.Pass(report.ToString());
        }
    }
}
