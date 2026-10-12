using System.Linq;
using NUnit.Framework;
using TinyTribe.Sim.Core;
using TinyTribe.Sim.Data;

namespace TinyTribe.Tests
{
    public class GatheringTests
    {
        static ResourceNodeDef Node(string id) => Kit.Data.NodeById(id);

        static float BareHandSpeed => Kit.Data.ConfigFloat("bare_hand_speed_mult");

        /// <summary>Agent at (4,4) facing a node of the given kind at (5,4).</summary>
        static (Simulation sim, Agent agent) FacingNode(string nodeId, AgentStats? stats = null)
        {
            var sim = Kit.NewSim();
            var def = Node(nodeId);
            if (def.Terrain == "water") sim.World.SetTerrain(5, 4, Kit.Data.TerrainById("water"));
            sim.World.PlaceNode(5, 4, def);
            var agent = sim.AddAgent(4, 4, stats ?? Kit.Neutral);
            Kit.Face(agent, 1, 0);
            return (sim, agent);
        }

        static void EquipFromNothing(Simulation sim, Agent agent, string itemId)
        {
            Assert.AreEqual(1, sim.AddItem(agent, Kit.Item(itemId), 1));
            sim.Step(new[] { new AgentAction(sim.Actions.Equip, Kit.Item(itemId)) });
            Assert.AreEqual(Kit.Item(itemId), agent.Equipped[(int)EquipSlot.Hand].Item);
        }

        static AgentAction Harvest(Simulation sim) => new AgentAction(sim.Actions.Harvest);

        [Test]
        public void Bare_hands_chop_a_pine_at_the_bare_hand_speed()
        {
            var (sim, agent) = FacingNode("pine_tree");
            var pine = Node("pine_tree");

            int actions = Kit.HarvestUntilYield(sim, agent, Kit.Item(pine.YieldItem));

            Assert.AreEqual(Kit.ExpectedActions(pine.HarvestTicks, BareHandSpeed), actions);
            Assert.AreEqual(pine.YieldQty, agent.Count(Kit.Item(pine.YieldItem)));
            Assert.IsTrue(Kit.HasEvent(sim, SimEventType.Harvested));
        }

        [Test]
        public void Harvest_is_invalid_with_nothing_in_front()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            Kit.Face(agent, 1, 0);

            Assert.IsFalse(sim.IsValid(agent.Index, Harvest(sim)));
        }

        [Test]
        public void Invalid_action_changes_nothing_and_is_reported()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            float thirstBefore = agent.Get(Need.Thirst);

            sim.Step(new[] { Harvest(sim) });

            Assert.IsTrue(Kit.HasEvent(sim, SimEventType.InvalidAction));
            Assert.AreEqual(thirstBefore - Kit.Data.NeedById("thirst").BaseDrainPerTick, agent.Get(Need.Thirst), 1e-4f,
                "only the base drain applies; no work cost");
        }

        [Test]
        public void Rock_cannot_be_mined_bare_handed()
        {
            var (sim, agent) = FacingNode("surface_rock");

            Assert.Greater(Node("surface_rock").MinToolTier, 0);
            Assert.IsFalse(sim.IsValid(agent.Index, Harvest(sim)));
        }

        [Test]
        public void Rock_is_mined_with_a_pickaxe_at_the_tool_speed()
        {
            var (sim, agent) = FacingNode("surface_rock");
            var rock = Node("surface_rock");
            EquipFromNothing(sim, agent, "wooden_pickaxe");

            int actions = Kit.HarvestUntilYield(sim, agent, Kit.Item(rock.YieldItem));

            float speed = Kit.Data.EquipmentByItemInt(Kit.Item("wooden_pickaxe")).SpeedMult;
            Assert.AreEqual(Kit.ExpectedActions(rock.HarvestTicks, speed), actions);
        }

        [Test]
        public void Wrong_tool_type_does_not_unlock_a_node()
        {
            var (sim, agent) = FacingNode("surface_rock");
            EquipFromNothing(sim, agent, "stone_axe");

            Assert.IsFalse(sim.IsValid(agent.Index, Harvest(sim)));
        }

        [Test]
        public void Tool_below_the_required_tier_does_not_unlock_a_node()
        {
            var (sim, agent) = FacingNode("iron_vein");
            EquipFromNothing(sim, agent, "wooden_pickaxe");

            Assert.Greater(Node("iron_vein").MinToolTier, Kit.Data.EquipmentByItemInt(Kit.Item("wooden_pickaxe")).Tier);
            Assert.IsFalse(sim.IsValid(agent.Index, Harvest(sim)));
        }

        [Test]
        public void Better_axe_chops_faster()
        {
            var pine = Node("pine_tree");
            var (simA, a) = FacingNode("pine_tree");
            EquipFromNothing(simA, a, "wooden_axe");
            var (simB, b) = FacingNode("pine_tree");
            EquipFromNothing(simB, b, "stone_axe");

            int wooden = Kit.HarvestUntilYield(simA, a, Kit.Item(pine.YieldItem));
            int stone = Kit.HarvestUntilYield(simB, b, Kit.Item(pine.YieldItem));

            Assert.AreEqual(Kit.ExpectedActions(pine.HarvestTicks, Kit.Data.EquipmentByItemInt(Kit.Item("wooden_axe")).SpeedMult), wooden);
            Assert.AreEqual(Kit.ExpectedActions(pine.HarvestTicks, Kit.Data.EquipmentByItemInt(Kit.Item("stone_axe")).SpeedMult), stone);
            Assert.Less(stone, wooden);
        }

        [Test]
        public void Wrong_tool_falls_back_to_bare_hands_on_a_tier_zero_node()
        {
            var (sim, agent) = FacingNode("pine_tree");
            var pine = Node("pine_tree");
            EquipFromNothing(sim, agent, "wooden_pickaxe");

            int actions = Kit.HarvestUntilYield(sim, agent, Kit.Item(pine.YieldItem));

            Assert.AreEqual(Kit.ExpectedActions(pine.HarvestTicks, BareHandSpeed), actions);
        }

        [Test]
        public void Strength_speeds_up_gathering()
        {
            var stats = Kit.Neutral;
            stats.Str = Kit.Data.ConfigInt("core_stat_max");
            var (sim, agent) = FacingNode("pine_tree", stats);
            var pine = Node("pine_tree");

            int actions = Kit.HarvestUntilYield(sim, agent, Kit.Item(pine.YieldItem));

            float mult = 1 + Kit.Data.ConfigFloat("str_gather_speed_per_point") * (stats.Str - Kit.Data.ConfigInt("stat_midpoint"));
            Assert.AreEqual(Kit.ExpectedActions(pine.HarvestTicks, BareHandSpeed * mult), actions);
            Assert.Less(actions, Kit.ExpectedActions(pine.HarvestTicks, BareHandSpeed));
        }

        [Test]
        public void Node_without_a_tool_type_is_gathered_at_full_speed()
        {
            var (sim, agent) = FacingNode("berry_bush");
            var bush = Node("berry_bush");

            int actions = Kit.HarvestUntilYield(sim, agent, Kit.Item(bush.YieldItem));

            Assert.AreEqual(bush.HarvestTicks, actions);
            Assert.AreEqual(bush.YieldQty, agent.Count(Kit.Item(bush.YieldItem)));
        }

        [Test]
        public void Harvest_costs_energy_and_thirst()
        {
            var (sim, agent) = FacingNode("pine_tree");
            float energy = agent.Get(Need.Energy), thirst = agent.Get(Need.Thirst);

            sim.Step(new[] { Harvest(sim) });

            float cost = Kit.Data.Actions[sim.Actions.Harvest].EnergyCost;
            Assert.AreEqual(energy - cost - Kit.Data.NeedById("energy").BaseDrainPerTick, agent.Get(Need.Energy), 1e-4f);
            Assert.AreEqual(thirst + Kit.Data.Modifier("thirst_work_action") - Kit.Data.NeedById("thirst").BaseDrainPerTick,
                agent.Get(Need.Thirst), 1e-4f);
        }

        [Test]
        public void Tool_lowers_the_energy_cost_of_harvesting()
        {
            var (sim, agent) = FacingNode("pine_tree");
            EquipFromNothing(sim, agent, "stone_axe");
            float energy = agent.Get(Need.Energy);

            sim.Step(new[] { Harvest(sim) });

            var axe = Kit.Data.EquipmentByItemInt(Kit.Item("stone_axe"));
            Assert.Less(axe.EnergyCostMult, 1f);
            float cost = Kit.Data.Actions[sim.Actions.Harvest].EnergyCost * axe.EnergyCostMult;
            Assert.AreEqual(energy - cost - Kit.Data.NeedById("energy").BaseDrainPerTick, agent.Get(Need.Energy), 1e-4f);
        }

        [Test]
        public void Node_is_depleted_after_its_charges_and_stops_blocking()
        {
            var (sim, agent) = FacingNode("berry_bush");
            var bush = Node("berry_bush");

            for (int i = 0; i < bush.Charges; i++)
            {
                agent.Inventory[0] = default;
                Kit.HarvestUntilYield(sim, agent, Kit.Item(bush.YieldItem));
            }

            Assert.IsTrue(Kit.HasEvent(sim, SimEventType.NodeDepleted));
            Assert.IsFalse(sim.World.HasActiveNode(5, 4));
            Assert.IsFalse(sim.IsValid(agent.Index, Harvest(sim)));

            sim.Step(new[] { new AgentAction(sim.Actions.MoveEast) });
            Assert.AreEqual((5, 4), (agent.X, agent.Y), "a depleted node can be walked over");
        }

        [Test]
        public void Depleted_node_regrows_after_its_regrow_time()
        {
            var (sim, agent) = FacingNode("berry_bush");
            var bush = Node("berry_bush");
            for (int i = 0; i < bush.Charges; i++)
            {
                agent.Inventory[0] = default;
                Kit.HarvestUntilYield(sim, agent, Kit.Item(bush.YieldItem));
            }
            var wait = new[] { new AgentAction(sim.Actions.Wait) };

            for (int i = 0; i < bush.RegrowTicks - 1; i++) { Kit.Refill(agent); sim.Step(wait); }
            Assert.IsFalse(sim.World.HasActiveNode(5, 4), "one tick too early");

            sim.Step(wait);
            Assert.IsTrue(sim.World.HasActiveNode(5, 4));
            Assert.AreEqual(bush.Charges, sim.World.NodeChargesAt(5, 4));
            Assert.IsTrue(Kit.HasEvent(sim, SimEventType.NodeRegrown));
        }

        [Test]
        public void Node_does_not_regrow_under_an_agent()
        {
            var (sim, agent) = FacingNode("berry_bush");
            var bush = Node("berry_bush");
            for (int i = 0; i < bush.Charges; i++)
            {
                agent.Inventory[0] = default;
                Kit.HarvestUntilYield(sim, agent, Kit.Item(bush.YieldItem));
            }
            sim.Step(new[] { new AgentAction(sim.Actions.MoveEast) });
            var wait = new[] { new AgentAction(sim.Actions.Wait) };

            for (int i = 0; i < bush.RegrowTicks + 5; i++) { Kit.Refill(agent); sim.Step(wait); }
            Assert.IsFalse(sim.World.HasActiveNode(5, 4), "blocked by the agent standing on it");

            sim.Step(new[] { new AgentAction(sim.Actions.MoveEast) });
            sim.Step(wait);
            Assert.IsTrue(sim.World.HasActiveNode(5, 4));
        }

        [Test]
        public void Node_without_regrow_time_is_removed_for_good()
        {
            var (sim, agent) = FacingNode("surface_rock");
            var rock = Node("surface_rock");
            Assert.AreEqual(0, rock.RegrowTicks);
            EquipFromNothing(sim, agent, "stone_pickaxe");

            for (int i = 0; i < rock.Charges; i++) Kit.HarvestUntilYield(sim, agent, Kit.Item(rock.YieldItem));

            Assert.AreEqual(0, sim.World.NodeAt(5, 4));
            Assert.IsFalse(sim.World.HasActiveNode(5, 4));
        }

        [Test]
        public void Tool_loses_durability_per_yield_and_breaks_at_zero()
        {
            var (sim, agent) = FacingNode("pine_tree");
            var pine = Node("pine_tree");
            EquipFromNothing(sim, agent, "stone_axe");
            agent.Equipped[(int)EquipSlot.Hand].Durability = 2;

            Kit.HarvestUntilYield(sim, agent, Kit.Item(pine.YieldItem));
            Assert.AreEqual(1, agent.Equipped[(int)EquipSlot.Hand].Durability);

            Kit.HarvestUntilYield(sim, agent, Kit.Item(pine.YieldItem));
            Assert.IsTrue(agent.Equipped[(int)EquipSlot.Hand].IsEmpty, "the axe broke");
            Assert.IsTrue(Kit.HasEvent(sim, SimEventType.ToolBroke));
        }

        [Test]
        public void Harvest_is_invalid_when_the_yield_does_not_fit()
        {
            var (sim, agent) = FacingNode("pine_tree");
            string[] filler = { "cobblestone", "coal", "herb", "cotton", "bone", "marble", "wheat", "iron_ore", "copper_ore", "gold_ore" };
            for (int i = 0; i < agent.InventoryCapacity; i++) sim.AddItem(agent, Kit.Item(filler[i]), 1);
            Assert.AreEqual(0, agent.FreeSlots);

            Assert.IsFalse(sim.IsValid(agent.Index, Harvest(sim)));
        }

        [Test]
        public void Harvest_is_valid_when_the_yield_fits_an_existing_stack()
        {
            var (sim, agent) = FacingNode("pine_tree");
            string[] filler = { "pine_wood", "coal", "herb", "cotton", "bone", "marble", "wheat", "iron_ore", "copper_ore", "gold_ore" };
            for (int i = 0; i < agent.InventoryCapacity; i++) sim.AddItem(agent, Kit.Item(filler[i]), 1);
            Assert.AreEqual(0, agent.FreeSlots);

            Assert.IsTrue(sim.IsValid(agent.Index, Harvest(sim)));
        }

        [Test]
        public void Berry_bush_cannot_be_harvested_in_winter()
        {
            var sim = Kit.NewSim();
            Kit.AdvanceToSeason(sim, "winter", 12);
            sim.World.PlaceNode(5, 4, Node("berry_bush"));
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            Kit.Face(agent, 1, 0);

            StringAssert.DoesNotContain("winter", Node("berry_bush").Seasons);
            Assert.IsFalse(sim.IsValid(agent.Index, Harvest(sim)));
        }

        [Test]
        public void Harvesting_water_fills_a_water_item()
        {
            var (sim, agent) = FacingNode("water_source");
            var source = Node("water_source");

            int actions = Kit.HarvestUntilYield(sim, agent, Kit.Item("water"));

            Assert.AreEqual(source.HarvestTicks, actions);
            Assert.IsTrue(sim.World.HasActiveNode(5, 4), "water never runs out");
        }

        [Test]
        public void Fishing_rod_catches_fish_from_water()
        {
            var (sim, agent) = FacingNode("water_source");
            var spot = Node("fishing_spot");
            EquipFromNothing(sim, agent, "fishing_rod");

            int actions = Kit.HarvestUntilYield(sim, agent, Kit.Item(spot.YieldItem));

            float speed = Kit.Data.EquipmentByItemInt(Kit.Item("fishing_rod")).SpeedMult;
            Assert.AreEqual(Kit.ExpectedActions(spot.HarvestTicks, speed), actions);
            Assert.AreEqual(0, agent.Count(Kit.Item("water")));
        }

        [Test]
        public void Oak_drops_apples_at_roughly_the_secondary_chance()
        {
            var (sim, agent) = FacingNode("oak_tree");
            var oak = Node("oak_tree");
            EquipFromNothing(sim, agent, "iron_axe");
            int apple = Kit.Item(oak.SecondaryItem);
            int yields = 0, apples = 0;

            while (yields < 400)
            {
                if (!sim.World.HasActiveNode(5, 4)) sim.World.PlaceNode(5, 4, oak);
                for (int i = 0; i < agent.Inventory.Length; i++) agent.Inventory[i] = default;
                agent.Equipped[(int)EquipSlot.Hand].Durability = 100;
                Kit.Refill(agent);
                sim.Step(new[] { Harvest(sim) });
                foreach (var e in sim.Events.Where(e => e.Type == SimEventType.Harvested))
                {
                    if (e.Item == apple) apples += e.Qty; else yields++;
                }
            }

            float rate = apples / (float)yields;
            Assert.AreEqual(oak.SecondaryChance, rate, 0.08f, apples + " apples in " + yields + " yields");
        }
    }
}
