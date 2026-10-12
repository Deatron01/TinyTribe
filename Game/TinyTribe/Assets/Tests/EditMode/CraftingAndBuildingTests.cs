using System.Linq;
using NUnit.Framework;
using TinyTribe.Sim.Core;
using TinyTribe.Sim.Data;

namespace TinyTribe.Tests
{
    /// <summary>Shared helpers for tests about crafting, buildings and storage.</summary>
    static class Village
    {
        public static readonly string[] Filler =
            { "cobblestone", "coal", "herb", "cotton", "bone", "marble", "wheat", "iron_ore", "copper_ore", "gold_ore" };

        /// <summary>One villager at (4,4) on open grass, facing east.</summary>
        public static (Simulation sim, Agent agent) One(AgentStats? stats = null, int width = 12, int height = 9)
        {
            var sim = Kit.NewSim(width, height);
            var agent = sim.AddAgent(4, 4, stats ?? Kit.Neutral);
            Kit.Face(agent, 1, 0);
            return (sim, agent);
        }

        public static void Give(Simulation sim, Agent agent, string item, int qty) =>
            Assert.AreEqual(qty, sim.AddItem(agent, Kit.Item(item), qty), "giving " + qty + " " + item);

        public static int Has(Agent agent, string item) => agent.Count(Kit.Item(item));

        public static AgentAction Craft(Simulation sim, string recipeOrBuilding) =>
            new AgentAction(sim.Actions.CraftBuild, Kit.Data.ArgFor(recipeOrBuilding));

        public static AgentAction Deposit(Simulation sim, string item) => new AgentAction(sim.Actions.Deposit, Kit.Item(item));

        public static AgentAction Take(Simulation sim, string item) => new AgentAction(sim.Actions.Take, Kit.Item(item));

        public static RecipeDef Recipe(string output) => Kit.Data.Recipes.First(r => r.OutputItem == output);

        public static void Do(Simulation sim, AgentAction action, int times = 1)
        {
            for (int i = 0; i < times; i++)
            {
                foreach (var agent in sim.Agents) Kit.Refill(agent);
                var actions = new AgentAction[sim.Agents.Count];
                for (int a = 0; a < actions.Length; a++) actions[a] = a == 0 ? action : new AgentAction(sim.Actions.Wait);
                sim.Step(actions);
            }
        }

        public static void FillPack(Simulation sim, Agent agent)
        {
            for (int i = 0; agent.FreeSlots > 0; i++) sim.AddItem(agent, Kit.Item(Filler[i]), 1);
        }

        public static int StoredIn(Building building, string item) =>
            building.Storage == null ? 0 : building.Storage.Where(s => s.Qty > 0 && s.Item == Kit.Item(item)).Sum(s => s.Qty);
    }

    public class CraftingTests
    {
        [Test]
        public void Crafting_takes_the_recipe_time_and_yields_the_item()
        {
            var (sim, agent) = Village.One();
            Village.Give(sim, agent, "pine_wood", 2);
            var recipe = Village.Recipe("wooden_axe");

            Village.Do(sim, Village.Craft(sim, "wooden_axe"), recipe.CraftTicks - 1);
            Assert.AreEqual(0, Village.Has(agent, "wooden_axe"), "one tick too early");

            Village.Do(sim, Village.Craft(sim, "wooden_axe"));
            Assert.AreEqual(recipe.OutputQty, Village.Has(agent, "wooden_axe"));
            Assert.IsTrue(Kit.HasEvent(sim, SimEventType.Crafted));
            Assert.AreEqual(0, agent.CraftArg, "nothing is being crafted any more");
        }

        [Test]
        public void Inputs_are_taken_when_crafting_starts()
        {
            var (sim, agent) = Village.One();
            Village.Give(sim, agent, "pine_wood", 3);

            Village.Do(sim, Village.Craft(sim, "wooden_axe"));

            Assert.AreEqual(1, Village.Has(agent, "pine_wood"));
            Assert.AreEqual(Kit.Data.ArgFor("wooden_axe"), agent.CraftArg);
        }

        [Test]
        public void Doing_something_else_cancels_the_craft_and_returns_the_inputs()
        {
            var (sim, agent) = Village.One();
            Village.Give(sim, agent, "pine_wood", 2);
            Village.Do(sim, Village.Craft(sim, "wooden_axe"), 2);

            Village.Do(sim, new AgentAction(sim.Actions.Wait));

            Assert.AreEqual(2, Village.Has(agent, "pine_wood"));
            Assert.AreEqual(0, agent.CraftArg);
            Assert.AreEqual(0f, agent.CraftProgress);
        }

        [Test]
        public void Switching_to_another_recipe_starts_over()
        {
            var (sim, agent) = Village.One();
            Village.Give(sim, agent, "pine_wood", 2);
            var recipe = Village.Recipe("wooden_spear");
            Village.Do(sim, Village.Craft(sim, "wooden_axe"), 3);

            Village.Do(sim, Village.Craft(sim, "wooden_spear"), recipe.CraftTicks);

            Assert.AreEqual(1, Village.Has(agent, "wooden_spear"));
            Assert.AreEqual(0, Village.Has(agent, "wooden_axe"));
            Assert.AreEqual(0, Village.Has(agent, "pine_wood"));
        }

        [Test]
        public void Crafting_is_invalid_without_the_inputs()
        {
            var (sim, agent) = Village.One();
            Village.Give(sim, agent, "pine_wood", 1);

            Assert.IsFalse(sim.IsValid(agent.Index, Village.Craft(sim, "wooden_axe")));
        }

        [Test]
        public void Crafting_is_valid_when_the_inputs_make_room_for_the_result()
        {
            var (sim, agent) = Village.One();
            Village.Give(sim, agent, "pine_wood", 2);
            Village.FillPack(sim, agent);

            Assert.IsTrue(sim.IsValid(agent.Index, Village.Craft(sim, "wooden_axe")));
        }

        [Test]
        public void Crafting_is_invalid_when_the_result_would_not_fit()
        {
            var (sim, agent) = Village.One();
            Village.Give(sim, agent, "pine_wood", 3);
            Village.FillPack(sim, agent);

            Assert.IsFalse(sim.IsValid(agent.Index, Village.Craft(sim, "wooden_axe")), "one wood stays behind and keeps its slot");
        }

        [Test]
        public void Dexterity_speeds_up_crafting()
        {
            var stats = Kit.Neutral;
            stats.Dex = Kit.Data.ConfigInt("core_stat_max");
            var (sim, agent) = Village.One(stats);
            Village.Give(sim, agent, "pine_wood", 2);
            var recipe = Village.Recipe("wooden_axe");
            float speed = 1 + Kit.Data.ConfigFloat("dex_speed_per_point") * (stats.Dex - Kit.Data.ConfigInt("stat_midpoint"));
            int expected = Kit.ExpectedActions(recipe.CraftTicks, speed);
            Assert.Less(expected, recipe.CraftTicks);

            Village.Do(sim, Village.Craft(sim, "wooden_axe"), expected - 1);
            Assert.AreEqual(0, Village.Has(agent, "wooden_axe"));
            Village.Do(sim, Village.Craft(sim, "wooden_axe"));
            Assert.AreEqual(1, Village.Has(agent, "wooden_axe"));
        }

        [Test]
        public void Crafting_costs_energy_each_tick()
        {
            var (sim, agent) = Village.One();
            Village.Give(sim, agent, "pine_wood", 2);
            float before = agent.Get(Need.Energy);

            sim.Step(new[] { Village.Craft(sim, "wooden_axe") });

            float cost = Kit.Data.Actions[sim.Actions.CraftBuild].EnergyCost;
            Assert.Greater(cost, 0f);
            Assert.AreEqual(before - cost - Kit.Data.NeedById("energy").BaseDrainPerTick, agent.Get(Need.Energy), 1e-4f);
        }

        [Test]
        public void Workstation_recipe_needs_the_villager_to_face_the_workstation()
        {
            var (sim, agent) = Village.One();
            Village.Give(sim, agent, "raw_meat", 1);
            Assert.IsFalse(sim.IsValid(agent.Index, Village.Craft(sim, "cooked_meat")), "no campfire");

            sim.PlaceBuilding("campfire", 5, 4);
            Assert.IsTrue(sim.IsValid(agent.Index, Village.Craft(sim, "cooked_meat")));

            Village.Do(sim, Village.Craft(sim, "cooked_meat"), Village.Recipe("cooked_meat").CraftTicks);
            Assert.AreEqual(1, Village.Has(agent, "cooked_meat"));
            Assert.AreEqual(0, Village.Has(agent, "raw_meat"));
        }

        [Test]
        public void Cooking_needs_the_campfire_to_be_lit()
        {
            var (sim, agent) = Village.One();
            Village.Give(sim, agent, "raw_meat", 1);
            sim.PlaceBuilding("campfire", 5, 4).FuelTicks = 0;

            Assert.IsFalse(sim.IsValid(agent.Index, Village.Craft(sim, "cooked_meat")));
        }

        [Test]
        public void Recipe_that_needs_a_technology_is_locked_until_it_is_researched()
        {
            var (sim, agent) = Village.One();
            Village.Give(sim, agent, "pine_wood", 2);
            Village.Give(sim, agent, "cobblestone", 1);
            Assert.IsNotEmpty(Village.Recipe("farming_hoe").TechRequired);
            Assert.IsFalse(sim.IsValid(agent.Index, Village.Craft(sim, "farming_hoe")));

            sim.Research(Village.Recipe("farming_hoe").TechRequired);

            Assert.IsTrue(sim.IsResearched("agriculture"));
            Assert.IsTrue(sim.IsValid(agent.Index, Village.Craft(sim, "farming_hoe")));
        }

        [Test]
        public void Recipe_that_needs_a_role_is_locked_for_everyone_else()
        {
            var (sim, agent) = Village.One();
            var recipe = Village.Recipe("bandage");
            Assert.AreNotEqual("any", recipe.RoleRequired);
            sim.PlaceBuilding(recipe.Workstation, 5, 4);
            sim.Research(recipe.TechRequired);
            Village.Give(sim, agent, "herb", 2);
            Village.Give(sim, agent, "cotton", 1);
            Assert.IsFalse(sim.IsValid(agent.Index, Village.Craft(sim, "bandage")));

            sim.SetRole(agent, recipe.RoleRequired);

            Assert.IsTrue(sim.IsValid(agent.Index, Village.Craft(sim, "bandage")));
        }

        [Test]
        public void Craft_argument_mask_lists_exactly_what_can_be_started()
        {
            var (sim, agent) = Village.One();
            Village.Give(sim, agent, "pine_wood", 2);
            var mask = new bool[Kit.Data.ConfigInt("num_action_args")];

            sim.GetArgMask(agent.Index, sim.Actions.CraftBuild, mask);

            var allowed = Enumerable.Range(0, mask.Length).Where(i => mask[i]).Select(i => Kit.Data.ActionArgs[i].RefId).OrderBy(id => id).ToArray();
            CollectionAssert.AreEqual(new[] { "wooden_axe", "wooden_pickaxe", "wooden_spear" }, allowed);
        }
    }

    public class BuildingTests
    {
        static BuildingDef Def(string id) => Kit.Data.BuildingById[id];

        static void GiveCost(Simulation sim, Agent agent, string buildingId)
        {
            var def = Def(buildingId);
            if (!string.IsNullOrEmpty(def.CostItem1)) Village.Give(sim, agent, def.CostItem1, def.CostQty1);
            if (!string.IsNullOrEmpty(def.CostItem2)) Village.Give(sim, agent, def.CostItem2, def.CostQty2);
            if (!string.IsNullOrEmpty(def.CostItem3)) Village.Give(sim, agent, def.CostItem3, def.CostQty3);
        }

        [Test]
        public void Building_starts_a_site_on_the_faced_tile_and_takes_the_cost()
        {
            var (sim, agent) = Village.One();
            GiveCost(sim, agent, "campfire");

            sim.Step(new[] { Village.Craft(sim, "campfire") });

            var site = sim.BuildingAt(5, 4);
            Assert.IsNotNull(site);
            Assert.AreEqual("campfire", site.Def.Id);
            Assert.IsFalse(site.Complete);
            Assert.AreEqual(agent.InventoryCapacity, agent.FreeSlots, "the materials went into the site");
            Assert.IsFalse(sim.CanEnter(5, 4), "a site blocks movement");
            Assert.IsTrue(Kit.HasEvent(sim, SimEventType.BuildingStarted));
        }

        [Test]
        public void Building_is_finished_after_its_build_time()
        {
            var (sim, agent) = Village.One();
            GiveCost(sim, agent, "campfire");
            int ticks = Def("campfire").BuildTicks;

            Village.Do(sim, Village.Craft(sim, "campfire"), ticks - 1);
            Assert.IsFalse(sim.BuildingAt(5, 4).Complete, "one tick too early");

            Village.Do(sim, Village.Craft(sim, "campfire"));
            Assert.IsTrue(sim.BuildingAt(5, 4).Complete);
            Assert.IsTrue(Kit.HasEvent(sim, SimEventType.BuildingCompleted));
        }

        [Test]
        public void Another_villager_can_continue_a_site_without_materials()
        {
            var (sim, agent) = Village.One();
            sim.PlaceBuilding("campfire", 5, 4, complete: false);
            float before = sim.BuildingAt(5, 4).Progress;

            Assert.IsTrue(sim.IsValid(agent.Index, Village.Craft(sim, "campfire")));
            sim.Step(new[] { Village.Craft(sim, "campfire") });

            Assert.Greater(sim.BuildingAt(5, 4).Progress, before);
        }

        [Test]
        public void Building_is_invalid_without_the_full_cost()
        {
            var (sim, agent) = Village.One();
            Village.Give(sim, agent, "pine_wood", Def("campfire").CostQty1);

            Assert.IsFalse(sim.IsValid(agent.Index, Village.Craft(sim, "campfire")));
        }

        [Test]
        public void Building_needs_every_tile_of_its_footprint_free()
        {
            var (sim, agent) = Village.One();
            GiveCost(sim, agent, "hide_tent");
            Assert.IsTrue(sim.IsValid(agent.Index, Village.Craft(sim, "hide_tent")));

            sim.World.PlaceNode(6, 5, Kit.Data.NodeById("pine_tree"));

            Assert.IsFalse(sim.IsValid(agent.Index, Village.Craft(sim, "hide_tent")));
        }

        [Test]
        public void Building_cannot_stand_on_water()
        {
            var (sim, agent) = Village.One();
            GiveCost(sim, agent, "campfire");
            sim.World.SetTerrain(5, 4, Kit.Data.TerrainById("water"));

            Assert.IsFalse(sim.IsValid(agent.Index, Village.Craft(sim, "campfire")));
        }

        [TestCase(0, 1, 4, 5)]
        [TestCase(0, -1, 4, 2)]
        [TestCase(1, 0, 5, 4)]
        [TestCase(-1, 0, 2, 4)]
        public void Footprint_extends_away_from_the_builder(int faceDx, int faceDy, int cornerX, int cornerY)
        {
            var (sim, agent) = Village.One();
            Kit.Face(agent, faceDx, faceDy);
            GiveCost(sim, agent, "hide_tent");
            var def = Def("hide_tent");
            Assert.AreEqual((2, 2), (def.FootprintW, def.FootprintH));

            sim.Step(new[] { Village.Craft(sim, "hide_tent") });

            var tent = sim.BuildingAt(agent.X + faceDx, agent.Y + faceDy);
            Assert.IsNotNull(tent, "the faced tile is part of the building");
            Assert.AreEqual((cornerX, cornerY, 2, 2), (tent.X, tent.Y, tent.Width, tent.Height));
            Assert.IsNull(sim.BuildingAt(agent.X, agent.Y), "the builder is not built over");
        }

        [Test]
        public void Only_builders_can_put_up_buildings_outside_the_basic_set()
        {
            var (sim, agent) = Village.One();
            sim.Research("carpentry");
            GiveCost(sim, agent, "lumber_mill");
            Assert.IsFalse(sim.IsValid(agent.Index, Village.Craft(sim, "lumber_mill")));

            sim.SetRole(agent, "builder");

            Assert.IsTrue(sim.IsValid(agent.Index, Village.Craft(sim, "lumber_mill")));
        }

        [TestCase("campfire")]
        [TestCase("basic_chest")]
        [TestCase("hide_tent")]
        [TestCase("stone_well")]
        public void Anyone_can_put_up_the_basic_buildings(string buildingId)
        {
            var (sim, agent) = Village.One(Kit.Neutral.WithStrength(Kit.Data.ConfigInt("core_stat_max")));
            GiveCost(sim, agent, buildingId);

            Assert.IsTrue(sim.IsValid(agent.Index, Village.Craft(sim, buildingId)));
        }

        [Test]
        public void Technology_locked_building_cannot_be_built_until_researched()
        {
            var (sim, agent) = Village.One(Kit.Neutral.WithStrength(Kit.Data.ConfigInt("core_stat_max")));
            var def = Def("clinic");
            Assert.IsNotEmpty(def.TechRequired);
            sim.SetRole(agent, "builder");
            GiveCost(sim, agent, "clinic");
            Assert.IsFalse(sim.IsValid(agent.Index, Village.Craft(sim, "clinic")));

            sim.Research(def.TechRequired);

            Assert.IsTrue(sim.IsValid(agent.Index, Village.Craft(sim, "clinic")));
        }

        [Test]
        public void Buildings_block_movement()
        {
            var (sim, agent) = Village.One();
            sim.PlaceBuilding("basic_chest", 5, 4);

            sim.Step(new[] { new AgentAction(sim.Actions.MoveEast) });

            Assert.AreEqual((4, 4), (agent.X, agent.Y));
        }

        [Test]
        public void Building_cannot_be_placed_over_a_villager_or_another_building()
        {
            var sim = Kit.NewSim();
            sim.AddAgent(5, 4, Kit.Neutral);
            Assert.Throws<System.InvalidOperationException>(() => sim.PlaceBuilding("basic_chest", 5, 4));

            sim.PlaceBuilding("basic_chest", 2, 2);
            Assert.Throws<System.InvalidOperationException>(() => sim.PlaceBuilding("hide_tent", 1, 1));
        }
    }

    public class CampfireTests
    {
        static (Simulation sim, Agent agent, Building fire) WinterNight(int agentX)
        {
            var sim = Kit.NewSim(14, 9);
            Kit.AdvanceToSeason(sim, "winter", 0);
            var fire = sim.PlaceBuilding("campfire", 5, 4);
            var agent = sim.AddAgent(agentX, 4, Kit.Neutral);
            agent.Set(Need.Temp, 50);
            return (sim, agent, fire);
        }

        [Test]
        public void New_campfire_starts_with_its_starting_fuel()
        {
            var sim = Kit.NewSim();

            var fire = sim.PlaceBuilding("campfire", 5, 4);

            Assert.AreEqual(Kit.Data.ConfigInt("campfire_start_fuel_ticks"), fire.FuelTicks);
            Assert.IsTrue(fire.IsLit);
        }

        [Test]
        public void Lit_campfire_warms_villagers_within_its_radius()
        {
            int radius = (int)Kit.Data.BuildingById["campfire"].EffectValue;
            var (sim, agent, _) = WinterNight(5 + radius);

            sim.Step(new[] { new AgentAction(sim.Actions.Wait) });

            Assert.AreEqual(50 + Kit.Data.Modifier("temp_campfire"), agent.Get(Need.Temp), 1e-4f);
        }

        [Test]
        public void Villager_outside_the_radius_keeps_getting_colder()
        {
            int radius = (int)Kit.Data.BuildingById["campfire"].EffectValue;
            var (sim, agent, _) = WinterNight(5 + radius + 1);

            sim.Step(new[] { new AgentAction(sim.Actions.Wait) });

            Assert.Less(agent.Get(Need.Temp), 50f);
        }

        [Test]
        public void Campfire_burns_down_goes_out_and_stops_warming()
        {
            var (sim, agent, fire) = WinterNight(6);
            fire.FuelTicks = 2;
            var wait = new[] { new AgentAction(sim.Actions.Wait) };

            sim.Step(wait);
            Assert.IsTrue(fire.IsLit);
            sim.Step(wait);
            Assert.IsFalse(fire.IsLit);
            Assert.IsTrue(Kit.HasEvent(sim, SimEventType.CampfireOut));

            float before = agent.Get(Need.Temp);
            sim.Step(wait);
            Assert.Less(agent.Get(Need.Temp), before);
        }

        [Test]
        public void Unfinished_campfire_gives_no_warmth()
        {
            var sim = Kit.NewSim(14, 9);
            Kit.AdvanceToSeason(sim, "winter", 0);
            sim.PlaceBuilding("campfire", 5, 4, complete: false);
            var agent = sim.AddAgent(6, 4, Kit.Neutral);
            agent.Set(Need.Temp, 50);

            sim.Step(new[] { new AgentAction(sim.Actions.Wait) });

            Assert.Less(agent.Get(Need.Temp), 50f);
        }

        [Test]
        public void Adding_wood_extends_the_burn_time_by_one_unit_of_wood()
        {
            var (sim, agent) = Village.One();
            var fire = sim.PlaceBuilding("campfire", 5, 4);
            Village.Give(sim, agent, "pine_wood", 3);
            int before = fire.FuelTicks;

            sim.Step(new[] { Village.Deposit(sim, "pine_wood") });

            Assert.AreEqual(before + Kit.Data.ConfigInt("campfire_fuel_ticks_per_wood") - 1, fire.FuelTicks, "one tick burned meanwhile");
            Assert.AreEqual(2, Village.Has(agent, "pine_wood"));
        }

        [Test]
        public void Coal_burns_longer_than_wood()
        {
            var (sim, agent) = Village.One();
            var fire = sim.PlaceBuilding("campfire", 5, 4);
            Village.Give(sim, agent, "coal", 1);
            int before = fire.FuelTicks;

            sim.Step(new[] { Village.Deposit(sim, "coal") });

            int perWood = Kit.Data.ConfigInt("campfire_fuel_ticks_per_wood");
            Assert.AreEqual(before + perWood * Kit.Data.ConfigInt("coal_fuel_mult") - 1, fire.FuelTicks);
        }

        [Test]
        public void Things_that_do_not_burn_cannot_be_put_on_the_fire()
        {
            var (sim, agent) = Village.One();
            sim.PlaceBuilding("campfire", 5, 4);
            Village.Give(sim, agent, "berry", 1);

            Assert.IsFalse(sim.IsValid(agent.Index, Village.Deposit(sim, "berry")));
        }

        [Test]
        public void A_dead_fire_can_be_relit_with_wood()
        {
            var (sim, agent) = Village.One();
            var fire = sim.PlaceBuilding("campfire", 5, 4);
            fire.FuelTicks = 0;
            Village.Give(sim, agent, "pine_wood", 1);

            sim.Step(new[] { Village.Deposit(sim, "pine_wood") });

            Assert.IsTrue(fire.IsLit);
        }
    }

    public class StorageTests
    {
        [Test]
        public void A_stack_can_be_stored_in_a_chest_and_taken_out_again()
        {
            var (sim, agent) = Village.One();
            var chest = sim.PlaceBuilding("basic_chest", 5, 4);
            Village.Give(sim, agent, "berry", 6);

            sim.Step(new[] { Village.Deposit(sim, "berry") });
            Assert.AreEqual(0, Village.Has(agent, "berry"));
            Assert.AreEqual(6, Village.StoredIn(chest, "berry"));
            Assert.IsTrue(Kit.HasEvent(sim, SimEventType.Deposited));

            sim.Step(new[] { Village.Take(sim, "berry") });
            Assert.AreEqual(6, Village.Has(agent, "berry"));
            Assert.AreEqual(0, Village.StoredIn(chest, "berry"));
            Assert.IsTrue(Kit.HasEvent(sim, SimEventType.Taken));
        }

        [Test]
        public void Storage_has_the_number_of_slots_in_the_data()
        {
            var sim = Kit.NewSim();

            var chest = sim.PlaceBuilding("basic_chest", 5, 4);

            Assert.AreEqual(Kit.Data.BuildingById["basic_chest"].StorageSlots, chest.Storage.Length);
        }

        [Test]
        public void Storage_only_accepts_its_own_kind_of_item()
        {
            var (sim, agent) = Village.One();
            sim.PlaceBuilding("ore_silo", 5, 3);
            Village.Give(sim, agent, "berry", 1);
            Village.Give(sim, agent, "cobblestone", 1);

            Assert.IsFalse(sim.IsValid(agent.Index, Village.Deposit(sim, "berry")));
            Assert.IsTrue(sim.IsValid(agent.Index, Village.Deposit(sim, "cobblestone")));
        }

        [Test]
        public void Deposit_is_invalid_without_the_item()
        {
            var (sim, agent) = Village.One();
            Village.Give(sim, agent, "berry", 1);
            Assert.IsFalse(sim.IsValid(agent.Index, Village.Deposit(sim, "pine_wood")), "open ground in front");

            sim.PlaceBuilding("basic_chest", 5, 4);
            Assert.IsFalse(sim.IsValid(agent.Index, Village.Deposit(sim, "pine_wood")), "a chest in front");
        }

        [Test]
        public void Deposit_moves_only_what_fits_when_storage_is_nearly_full()
        {
            var (sim, agent) = Village.One();
            var chest = sim.PlaceBuilding("basic_chest", 5, 4);
            var berry = Kit.Data.ItemById["berry"];
            for (int i = 0; i < chest.Storage.Length; i++)
                chest.Storage[i] = new ItemStack { Item = berry.Int, Qty = berry.MaxStack };
            chest.Storage[0].Qty = berry.MaxStack - 2;
            Village.Give(sim, agent, "berry", 5);

            sim.Step(new[] { Village.Deposit(sim, "berry") });

            Assert.AreEqual(3, Village.Has(agent, "berry"));
            Assert.IsFalse(sim.IsValid(agent.Index, Village.Deposit(sim, "berry")), "now it is full");
        }

        [Test]
        public void Take_brings_as_much_as_the_pack_can_hold()
        {
            var (sim, agent) = Village.One();
            var chest = sim.PlaceBuilding("basic_chest", 5, 4);
            var berry = Kit.Data.ItemById["berry"];
            chest.Storage[0] = new ItemStack { Item = berry.Int, Qty = berry.MaxStack };
            Village.Give(sim, agent, "berry", berry.MaxStack - 3);
            Village.FillPack(sim, agent);

            sim.Step(new[] { Village.Take(sim, "berry") });

            Assert.AreEqual(berry.MaxStack, Village.Has(agent, "berry"));
            Assert.AreEqual(berry.MaxStack - 3, Village.StoredIn(chest, "berry"));
        }

        [Test]
        public void Take_is_invalid_when_nothing_fits_or_nothing_is_there()
        {
            var (sim, agent) = Village.One();
            var chest = sim.PlaceBuilding("basic_chest", 5, 4);
            Assert.IsFalse(sim.IsValid(agent.Index, Village.Take(sim, "berry")), "the chest is empty");

            chest.Storage[0] = new ItemStack { Item = Kit.Item("berry"), Qty = 4 };
            Village.FillPack(sim, agent);
            Assert.IsFalse(sim.IsValid(agent.Index, Village.Take(sim, "berry")), "the pack is full");
        }

        [Test]
        public void Tools_keep_their_wear_when_stored()
        {
            var (sim, agent) = Village.One();
            sim.PlaceBuilding("basic_chest", 5, 4);
            Village.Give(sim, agent, "stone_axe", 1);
            agent.Inventory[0].Durability = 17;

            sim.Step(new[] { Village.Deposit(sim, "stone_axe") });
            sim.Step(new[] { Village.Take(sim, "stone_axe") });

            Assert.AreEqual(17, agent.Inventory.First(s => s.Item == Kit.Item("stone_axe")).Durability);
        }

        [Test]
        public void An_unfinished_chest_cannot_be_used()
        {
            var (sim, agent) = Village.One();
            sim.PlaceBuilding("basic_chest", 5, 4, complete: false);
            Village.Give(sim, agent, "berry", 1);

            Assert.IsFalse(sim.IsValid(agent.Index, Village.Deposit(sim, "berry")));
        }

        [Test]
        public void Storage_argument_masks_list_what_can_be_moved()
        {
            var (sim, agent) = Village.One();
            var chest = sim.PlaceBuilding("basic_chest", 5, 4);
            chest.Storage[0] = new ItemStack { Item = Kit.Item("cotton"), Qty = 2 };
            Village.Give(sim, agent, "berry", 1);
            var mask = new bool[Kit.Data.ConfigInt("num_action_args")];

            sim.GetArgMask(agent.Index, sim.Actions.Deposit, mask);
            CollectionAssert.AreEqual(new[] { Kit.Item("berry") }, Enumerable.Range(0, mask.Length).Where(i => mask[i]).ToArray());

            sim.GetArgMask(agent.Index, sim.Actions.Take, mask);
            CollectionAssert.AreEqual(new[] { Kit.Item("cotton") }, Enumerable.Range(0, mask.Length).Where(i => mask[i]).ToArray());
        }
    }

    public class HomeTests
    {
        [Test]
        public void Sleeping_at_a_free_house_claims_it()
        {
            var (sim, agent) = Village.One();
            var tent = sim.PlaceBuilding("hide_tent", 5, 4);

            sim.Step(new[] { new AgentAction(sim.Actions.Sleep) });

            Assert.AreEqual(tent.Index, agent.Home);
            CollectionAssert.Contains(tent.Residents, agent.Index);
            Assert.IsTrue(Kit.HasEvent(sim, SimEventType.HomeClaimed));
        }

        [Test]
        public void A_house_takes_no_more_residents_than_its_capacity()
        {
            var sim = Kit.NewSim();
            var tent = sim.PlaceBuilding("hide_tent", 4, 4);
            int capacity = Kit.Data.BuildingById["hide_tent"].CapacityAgents;
            var spots = new[] { (3, 4), (3, 5), (6, 4), (6, 5) };
            Assert.Greater(spots.Length, capacity);
            foreach (var (x, y) in spots)
            {
                var agent = sim.AddAgent(x, y, Kit.Neutral);
                Kit.Face(agent, x < 4 ? 1 : -1, 0);
            }

            sim.Step(sim.Agents.Select(_ => new AgentAction(sim.Actions.Sleep)).ToArray());

            Assert.AreEqual(capacity, tent.Residents.Count);
            Assert.AreEqual(capacity, sim.Agents.Count(a => a.Home == tent.Index));
        }

        [Test]
        public void Sleeping_at_home_restores_more_energy_and_keeps_warm()
        {
            var sim = Kit.NewSim();
            Kit.AdvanceToSeason(sim, "winter", 0);
            sim.PlaceBuilding("hide_tent", 5, 4);
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            Kit.Face(agent, 1, 0);
            agent.Set(Need.Energy, 40);
            agent.Set(Need.Temp, 50);

            sim.Step(new[] { new AgentAction(sim.Actions.Sleep) });

            float gain = Kit.Data.Modifier("energy_sleep_night") * Kit.Data.Modifier("energy_sleep_sheltered");
            Assert.AreEqual(40 + gain - Kit.Data.NeedById("energy").BaseDrainPerTick, agent.Get(Need.Energy), 1e-4f);
            Assert.AreEqual(50 + Kit.Data.Modifier("temp_indoors"), agent.Get(Need.Temp), 1e-4f);
            Assert.IsTrue(agent.Sheltered);
        }

        [Test]
        public void Sleeping_next_to_someone_elses_full_house_is_sleeping_outside()
        {
            var sim = Kit.NewSim();
            Kit.AdvanceToSeason(sim, "winter", 0);
            var tent = sim.PlaceBuilding("hide_tent", 5, 4);
            for (int i = 0; i < Kit.Data.BuildingById["hide_tent"].CapacityAgents; i++) tent.Residents.Add(90 + i);
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            Kit.Face(agent, 1, 0);
            agent.Set(Need.Temp, 50);

            sim.Step(new[] { new AgentAction(sim.Actions.Sleep) });

            Assert.AreEqual(-1, agent.Home);
            Assert.IsFalse(agent.Sheltered);
            Assert.Less(agent.Get(Need.Temp), 50f);
        }

        [Test]
        public void Only_residents_can_use_the_house_chest()
        {
            var sim = Kit.NewSim();
            sim.PlaceBuilding("hide_tent", 5, 4);
            var resident = sim.AddAgent(4, 4, Kit.Neutral);
            var visitor = sim.AddAgent(4, 5, Kit.Neutral);
            Kit.Face(resident, 1, 0);
            Kit.Face(visitor, 1, 0);
            Village.Give(sim, resident, "berry", 1);
            Village.Give(sim, visitor, "berry", 1);
            sim.Step(new[] { new AgentAction(sim.Actions.Sleep), new AgentAction(sim.Actions.Wait) });

            Assert.IsTrue(sim.IsValid(resident.Index, Village.Deposit(sim, "berry")));
            Assert.IsFalse(sim.IsValid(visitor.Index, Village.Deposit(sim, "berry")));
        }
    }

    public class WellAndPileTests
    {
        [Test]
        public void A_well_gives_water_like_a_lake()
        {
            var (sim, agent) = Village.One();
            sim.PlaceBuilding("stone_well", 5, 4);
            agent.Set(Need.Thirst, 30);

            Assert.IsTrue(sim.IsValid(agent.Index, new AgentAction(sim.Actions.Consume, 0)));
            sim.Step(new[] { new AgentAction(sim.Actions.Consume, 0) });
            Assert.Greater(agent.Get(Need.Thirst), 60f);

            Kit.HarvestUntilYield(sim, agent, Kit.Item("water"), 10);
            Assert.AreEqual(1, Village.Has(agent, "water"));
        }

        [Test]
        public void An_unfinished_well_gives_nothing()
        {
            var (sim, agent) = Village.One();
            sim.PlaceBuilding("stone_well", 5, 4, complete: false);

            Assert.IsFalse(sim.IsValid(agent.Index, new AgentAction(sim.Actions.Consume, 0)));
        }

        [Test]
        public void A_dead_villager_leaves_its_things_in_a_pile_for_others()
        {
            var sim = Kit.NewSim();
            var doomed = sim.AddAgent(5, 4, Kit.Neutral);
            var heir = sim.AddAgent(4, 4, Kit.Neutral);
            Kit.Face(heir, 1, 0);
            Village.Give(sim, doomed, "berry", 4);
            Village.Give(sim, doomed, "stone_axe", 1);
            sim.Step(new[] { new AgentAction(sim.Actions.Equip, Kit.Item("stone_axe")), new AgentAction(sim.Actions.Wait) });
            doomed.Set(Need.Hunger, 0);
            doomed.Set(Need.Hp, 0.1f);

            sim.Step(new[] { new AgentAction(sim.Actions.Wait), new AgentAction(sim.Actions.Wait) });
            Assert.IsFalse(doomed.Alive);
            Assert.AreEqual(Kit.Data.NodeById("item_pile").Int, sim.World.NodeAt(5, 4));
            Assert.AreEqual(2, sim.PileAt(5, 4).Count, "berries and the axe it was holding");

            Kit.Refill(heir);
            sim.Step(new[] { new AgentAction(sim.Actions.Wait), new AgentAction(sim.Actions.Harvest) });
            Assert.AreEqual(4, Village.Has(heir, "berry"));
            Assert.AreEqual(1, Village.Has(heir, "stone_axe"));
            Assert.AreEqual(0, sim.World.NodeAt(5, 4), "an emptied pile disappears");
            Assert.IsEmpty(sim.PileAt(5, 4));
        }

        [Test]
        public void Deposit_on_open_ground_puts_the_stack_down_as_a_pile()
        {
            var (sim, agent) = Village.One();
            Village.Give(sim, agent, "animal_hide", 3);
            Village.Give(sim, agent, "berry", 2);
            Assert.IsTrue(sim.IsValid(agent.Index, Village.Deposit(sim, "animal_hide")));

            sim.Step(new[] { Village.Deposit(sim, "animal_hide") });

            Assert.AreEqual(0, Village.Has(agent, "animal_hide"));
            Assert.AreEqual(2, Village.Has(agent, "berry"), "only the chosen stack goes");
            Assert.AreEqual(Kit.Data.NodeById("item_pile").Int, sim.World.NodeAt(5, 4));
            var pile = sim.PileAt(5, 4);
            Assert.AreEqual(1, pile.Count);
            Assert.AreEqual((Kit.Item("animal_hide"), 3), (pile[0].Item, pile[0].Qty));
            Assert.IsTrue(sim.Events.Any(e => e.Type == SimEventType.Deposited && e.Agent == agent.Index && e.Item == Kit.Item("animal_hide") && e.Qty == 3));
            Assert.IsTrue(sim.Events.Any(e => e.Type == SimEventType.NodeChanged && e.X == 5 && e.Y == 4));
        }

        [Test]
        public void More_can_be_put_on_a_pile_and_all_of_it_picked_up_again()
        {
            var (sim, agent) = Village.One();
            Village.Give(sim, agent, "bone", 2);
            Village.Give(sim, agent, "stone_axe", 1);
            agent.Inventory[1].Durability = 9;

            sim.Step(new[] { Village.Deposit(sim, "bone") });
            sim.Step(new[] { Village.Deposit(sim, "stone_axe") });
            Assert.AreEqual(2, sim.PileAt(5, 4).Count);
            Assert.AreEqual(0, agent.Inventory.Count(s => s.Qty > 0));

            Kit.Refill(agent);
            sim.Step(new[] { new AgentAction(sim.Actions.Harvest) });

            Assert.AreEqual(2, Village.Has(agent, "bone"));
            Assert.AreEqual(9, agent.Inventory.First(s => s.Item == Kit.Item("stone_axe")).Durability, "wear is kept");
            Assert.AreEqual(0, sim.World.NodeAt(5, 4));
        }

        [Test]
        public void Things_cannot_be_put_down_where_something_is_in_the_way()
        {
            var (sim, agent) = Village.One();
            Village.Give(sim, agent, "bone", 1);
            var drop = Village.Deposit(sim, "bone");

            sim.World.SetTerrain(5, 4, Kit.Data.TerrainById("water"));
            Assert.IsFalse(sim.IsValid(agent.Index, drop), "water");
            sim.World.SetTerrain(5, 4, Kit.Data.TerrainById("grass"));

            sim.World.PlaceNode(5, 4, Kit.Data.NodeById("berry_bush"));
            Assert.IsFalse(sim.IsValid(agent.Index, drop), "a bush");
            sim.World.NodeCharges[sim.World.Index(5, 4)] = 0;
            Assert.IsFalse(sim.IsValid(agent.Index, drop), "a picked bush that will grow back");
            sim.World.RemoveNode(sim.World.Index(5, 4));

            sim.AddAgent(5, 4, Kit.Neutral);
            Assert.IsFalse(sim.IsValid(agent.Index, drop), "a villager");
            Kit.Face(agent, -1, 0);
            sim.SpawnAnimal("rabbit", 3, 4);
            Assert.IsFalse(sim.IsValid(agent.Index, drop), "an animal");
            Kit.Face(agent, 0, 1);
            Assert.IsTrue(sim.IsValid(agent.Index, drop), "open ground");

            agent.X = 0;
            Kit.Face(agent, -1, 0);
            Assert.IsFalse(sim.IsValid(agent.Index, drop), "the edge of the map");
        }

        [Test]
        public void A_pile_is_in_the_way_like_any_other_node()
        {
            var (sim, agent) = Village.One();
            Village.Give(sim, agent, "bone", 1);
            sim.Step(new[] { Village.Deposit(sim, "bone") });

            sim.Step(new[] { new AgentAction(sim.Actions.MoveEast) });

            Assert.AreEqual((4, 4), (agent.X, agent.Y));
        }

        [Test]
        public void A_pile_keeps_what_the_finder_cannot_carry()
        {
            var sim = Kit.NewSim();
            var doomed = sim.AddAgent(5, 4, Kit.Neutral);
            var finder = sim.AddAgent(4, 4, Kit.Neutral);
            Kit.Face(finder, 1, 0);
            Village.Give(sim, doomed, "berry", 4);
            Village.Give(sim, doomed, "pine_wood", 2);
            doomed.Set(Need.Hunger, 0);
            doomed.Set(Need.Hp, 0.1f);
            sim.Step(new[] { new AgentAction(sim.Actions.Wait), new AgentAction(sim.Actions.Wait) });
            for (int i = 0; finder.FreeSlots > 1; i++) sim.AddItem(finder, Kit.Item(Village.Filler[i]), 1);

            Kit.Refill(finder);
            sim.Step(new[] { new AgentAction(sim.Actions.Wait), new AgentAction(sim.Actions.Harvest) });

            Assert.AreEqual(1, sim.PileAt(5, 4).Count, "one of the two stacks is still there");
            Assert.AreEqual(Kit.Data.NodeById("item_pile").Int, sim.World.NodeAt(5, 4));
        }
    }
}
