using System.Linq;
using NUnit.Framework;
using TinyTribe.Sim.Core;

namespace TinyTribe.Tests
{
    public class ConsumeTests
    {
        [Test]
        public void Eating_applies_the_item_values_and_uses_one()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            var berry = Kit.Data.ItemById["berry"];
            sim.AddItem(agent, berry.Int, 3);
            agent.Set(Need.Hunger, 40);
            agent.Set(Need.Thirst, 40);

            sim.Step(new[] { new AgentAction(sim.Actions.Consume, berry.Int) });

            Assert.AreEqual(40 + berry.Hunger - Kit.Data.NeedById("hunger").BaseDrainPerTick, agent.Get(Need.Hunger), 1e-4f);
            Assert.AreEqual(40 + berry.Thirst - Kit.Data.NeedById("thirst").BaseDrainPerTick, agent.Get(Need.Thirst), 1e-4f);
            Assert.AreEqual(2, agent.Count(berry.Int));
            Assert.IsTrue(Kit.HasEvent(sim, SimEventType.Consumed));
        }

        [Test]
        public void Eating_cannot_push_a_need_above_the_maximum()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            var bread = Kit.Data.ItemById["bread"];
            sim.AddItem(agent, bread.Int, 1);
            float max = Kit.Data.ConfigFloat("need_max");
            agent.Set(Need.Hunger, max - 1);

            sim.Step(new[] { new AgentAction(sim.Actions.Consume, bread.Int) });

            Assert.LessOrEqual(agent.Get(Need.Hunger), max);
            Assert.Greater(agent.Get(Need.Hunger), max - 1);
        }

        [Test]
        public void Consume_is_invalid_without_the_item()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);

            Assert.IsFalse(sim.IsValid(agent.Index, new AgentAction(sim.Actions.Consume, Kit.Item("berry"))));
        }

        [Test]
        public void Materials_cannot_be_consumed()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            sim.AddItem(agent, Kit.Item("pine_wood"), 1);

            Assert.IsFalse(sim.IsValid(agent.Index, new AgentAction(sim.Actions.Consume, Kit.Item("pine_wood"))));
        }

        [Test]
        public void Drinking_at_water_needs_no_item()
        {
            var sim = Kit.NewSim();
            sim.World.SetTerrain(5, 4, Kit.Data.TerrainById("water"));
            sim.World.PlaceNode(5, 4, Kit.Data.NodeById("water_source"));
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            Kit.Face(agent, 1, 0);
            agent.Set(Need.Thirst, 30);

            sim.Step(new[] { new AgentAction(sim.Actions.Consume, 0) });

            float water = Kit.Data.ItemById["water"].Thirst;
            Assert.AreEqual(30 + water - Kit.Data.NeedById("thirst").BaseDrainPerTick, agent.Get(Need.Thirst), 1e-4f);
        }

        [Test]
        public void Drinking_is_invalid_away_from_water()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);

            Assert.IsFalse(sim.IsValid(agent.Index, new AgentAction(sim.Actions.Consume, 0)));
        }
    }

    public class EquipTests
    {
        static ref ItemStack Hand(Agent agent) => ref agent.Equipped[(int)EquipSlot.Hand];

        [Test]
        public void Added_tools_start_at_full_durability()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            int axe = Kit.Item("stone_axe");

            sim.AddItem(agent, axe, 1);

            Assert.AreEqual(Kit.Data.EquipmentByItemInt(axe).Durability, agent.Inventory.First(s => s.Item == axe).Durability);
        }

        [Test]
        public void Equip_moves_a_tool_from_the_inventory_to_the_hand()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            int axe = Kit.Item("stone_axe");
            sim.AddItem(agent, axe, 1);

            sim.Step(new[] { new AgentAction(sim.Actions.Equip, axe) });

            Assert.AreEqual(axe, Hand(agent).Item);
            Assert.AreEqual(Kit.Data.EquipmentByItemInt(axe).Durability, Hand(agent).Durability);
            Assert.AreEqual(0, agent.Count(axe));
        }

        [Test]
        public void Equipping_another_tool_swaps_and_keeps_the_old_durability()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            int axe = Kit.Item("stone_axe"), pick = Kit.Item("stone_pickaxe");
            sim.AddItem(agent, axe, 1);
            sim.AddItem(agent, pick, 1);
            sim.Step(new[] { new AgentAction(sim.Actions.Equip, axe) });
            Hand(agent).Durability = 17;

            sim.Step(new[] { new AgentAction(sim.Actions.Equip, pick) });

            Assert.AreEqual(pick, Hand(agent).Item);
            Assert.AreEqual(17, agent.Inventory.First(s => s.Item == axe).Durability);
        }

        [Test]
        public void Unequip_returns_the_tool_to_the_inventory()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            int axe = Kit.Item("stone_axe");
            sim.AddItem(agent, axe, 1);
            sim.Step(new[] { new AgentAction(sim.Actions.Equip, axe) });

            sim.Step(new[] { new AgentAction(sim.Actions.Equip, 0) });

            Assert.IsTrue(Hand(agent).IsEmpty);
            Assert.AreEqual(1, agent.Count(axe));
        }

        [Test]
        public void Unequip_is_invalid_with_a_full_inventory()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            int axe = Kit.Item("stone_axe");
            sim.AddItem(agent, axe, 1);
            sim.Step(new[] { new AgentAction(sim.Actions.Equip, axe) });
            string[] filler = { "cobblestone", "coal", "herb", "cotton", "bone", "marble", "wheat", "iron_ore", "copper_ore", "gold_ore" };
            for (int i = 0; i < agent.InventoryCapacity; i++) sim.AddItem(agent, Kit.Item(filler[i]), 1);

            Assert.IsFalse(sim.IsValid(agent.Index, new AgentAction(sim.Actions.Equip, 0)));
        }

        [Test]
        public void Only_equipment_can_be_equipped()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            sim.AddItem(agent, Kit.Item("berry"), 1);

            Assert.IsFalse(sim.IsValid(agent.Index, new AgentAction(sim.Actions.Equip, Kit.Item("berry"))));
        }

        [Test]
        public void Clothing_goes_to_its_own_slot_and_leaves_the_hand_alone()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            int axe = Kit.Item("stone_axe"), hat = Kit.Item("leather_hat");
            sim.AddItem(agent, axe, 1);
            sim.AddItem(agent, hat, 1);
            sim.Step(new[] { new AgentAction(sim.Actions.Equip, axe) });

            sim.Step(new[] { new AgentAction(sim.Actions.Equip, hat) });

            Assert.AreEqual(axe, Hand(agent).Item);
            Assert.AreEqual(hat, agent.Equipped[(int)EquipSlot.Head].Item);
        }

        [TestCase(2, "personal_slots_str_1_3")]
        [TestCase(5, "personal_slots_str_4_7")]
        [TestCase(9, "personal_slots_str_8_10")]
        public void Inventory_capacity_follows_strength(int strength, string configKey)
        {
            var sim = Kit.NewSim();
            var stats = Kit.Neutral;
            stats.Str = strength;

            var agent = sim.AddAgent(4, 4, stats);

            Assert.AreEqual(Kit.Data.ConfigInt(configKey), agent.InventoryCapacity);
            Assert.AreEqual(agent.InventoryCapacity, agent.FreeSlots);
        }

        [Test]
        public void Items_stack_up_to_the_stack_limit_then_use_a_new_slot()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            var berry = Kit.Data.ItemById["berry"];

            int added = sim.AddItem(agent, berry.Int, berry.MaxStack + 1);

            Assert.AreEqual(berry.MaxStack + 1, added);
            Assert.AreEqual(berry.MaxStack + 1, agent.Count(berry.Int));
            Assert.AreEqual(agent.InventoryCapacity - 2, agent.FreeSlots);
        }

        [Test]
        public void Adding_more_than_fits_returns_only_what_was_added()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            var berry = Kit.Data.ItemById["berry"];
            int room = agent.InventoryCapacity * berry.MaxStack;

            int added = sim.AddItem(agent, berry.Int, room + 7);

            Assert.AreEqual(room, added);
            Assert.AreEqual(0, agent.FreeSlots);
        }

        [Test]
        public void Backpack_adds_inventory_slots()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            int pack = Kit.Item("explorer_backpack");
            int before = agent.InventoryCapacity;
            sim.AddItem(agent, pack, 1);

            sim.Step(new[] { new AgentAction(sim.Actions.Equip, pack) });

            int extra = (int)Kit.Data.EquipmentByItemInt(pack).EffectValue;
            Assert.Greater(extra, 0);
            Assert.AreEqual(before + extra, agent.InventoryCapacity);
        }

        [Test]
        public void Winter_clothing_slows_cold_drain()
        {
            var sim = Kit.NewSim();
            Kit.AdvanceToSeason(sim, "winter", 0);
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            int coat = Kit.Item("winter_clothing");
            sim.AddItem(agent, coat, 1);
            float before = agent.Get(Need.Temp);

            sim.Step(new[] { new AgentAction(sim.Actions.Equip, coat) });

            float delta = Kit.Data.Seasons.First(s => s.Id == "winter").TempDeltaNight;
            float mult = Kit.Data.EquipmentByItemInt(coat).EffectValue;
            Assert.Less(mult, 1f);
            Assert.AreEqual(before + delta * mult, agent.Get(Need.Temp), 1e-4f);
        }

        [Test]
        public void Fur_boots_remove_the_snow_movement_penalty()
        {
            var sim = Kit.NewSim();
            sim.World.SetTerrain(5, 4, Kit.Data.TerrainById("snow"));
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            int boots = Kit.Item("fur_boots");
            sim.AddItem(agent, boots, 1);
            sim.Step(new[] { new AgentAction(sim.Actions.Equip, boots) });

            sim.Step(new[] { new AgentAction(sim.Actions.MoveEast) });

            Assert.AreEqual((5, 4), (agent.X, agent.Y));
            Assert.AreEqual(0, agent.BusyTicks);
        }
    }

    public class MaskTests
    {
        static bool[] ActionMask(Simulation sim, Agent agent)
        {
            var mask = new bool[Kit.Data.ConfigInt("num_actions")];
            sim.GetActionMask(agent.Index, mask);
            return mask;
        }

        static bool[] ArgMask(Simulation sim, Agent agent, int actionId)
        {
            var mask = new bool[Kit.Data.ConfigInt("num_action_args")];
            sim.GetArgMask(agent.Index, actionId, mask);
            return mask;
        }

        [Test]
        public void Idle_agent_on_open_ground_can_move_wait_sleep_and_show_an_emote_only()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);

            var mask = ActionMask(sim, agent);

            var a = sim.Actions;
            int[] expected = { a.MoveNorth, a.MoveSouth, a.MoveEast, a.MoveWest, a.Wait, a.Sleep };
            for (int id = 0; id < mask.Length; id++)
            {
                // An emote can always be shown, but for the one that waits for the Gold Age.
                string name = Kit.Data.Actions[id].Name;
                bool emote = name.StartsWith("emote_") && name != "emote_trade";
                Assert.AreEqual(expected.Contains(id) || emote, mask[id], name);
            }
        }

        [Test]
        public void Harvest_is_unmasked_when_facing_a_workable_node()
        {
            var sim = Kit.NewSim();
            sim.World.PlaceNode(5, 4, Kit.Data.NodeById("pine_tree"));
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            Kit.Face(agent, 1, 0);

            Assert.IsTrue(ActionMask(sim, agent)[sim.Actions.Harvest]);
        }

        [Test]
        public void Consume_and_equip_are_unmasked_when_the_inventory_allows_them()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            sim.AddItem(agent, Kit.Item("berry"), 1);
            sim.AddItem(agent, Kit.Item("stone_axe"), 1);

            var mask = ActionMask(sim, agent);

            Assert.IsTrue(mask[sim.Actions.Consume]);
            Assert.IsTrue(mask[sim.Actions.Equip]);
        }

        [Test]
        public void Arg_mask_for_consume_lists_only_held_consumables()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            sim.AddItem(agent, Kit.Item("berry"), 1);
            sim.AddItem(agent, Kit.Item("pine_wood"), 1);

            var mask = ArgMask(sim, agent, sim.Actions.Consume);

            Assert.AreEqual(1, mask.Count(m => m));
            Assert.IsTrue(mask[Kit.Item("berry")]);
        }

        [Test]
        public void Arg_mask_for_equip_lists_held_equipment()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            sim.AddItem(agent, Kit.Item("berry"), 1);
            sim.AddItem(agent, Kit.Item("stone_axe"), 1);

            var mask = ArgMask(sim, agent, sim.Actions.Equip);

            Assert.AreEqual(1, mask.Count(m => m));
            Assert.IsTrue(mask[Kit.Item("stone_axe")]);
        }

        [Test]
        public void Arg_mask_of_an_action_without_argument_allows_only_zero()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);

            var mask = ArgMask(sim, agent, sim.Actions.MoveEast);

            Assert.IsTrue(mask[0]);
            Assert.AreEqual(1, mask.Count(m => m));
        }

        [Test]
        public void Dead_agent_has_no_valid_actions()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            agent.Set(Need.Hunger, 0);
            agent.Set(Need.Hp, 0.1f);
            sim.Step(new[] { new AgentAction(sim.Actions.Wait) });

            Assert.IsFalse(ActionMask(sim, agent).Any(m => m));
        }

        [Test]
        public void Forced_sleep_masks_everything_but_sleep()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            agent.Set(Need.Energy, 0);
            sim.Step(new[] { new AgentAction(sim.Actions.Wait) });

            var mask = ActionMask(sim, agent);

            Assert.AreEqual(1, mask.Count(m => m));
            Assert.IsTrue(mask[sim.Actions.Sleep]);
        }

        [Test]
        public void Busy_agent_can_only_wait()
        {
            var sim = Kit.NewSim();
            sim.World.SetTerrain(5, 4, Kit.Data.TerrainById("snow"));
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            sim.Step(new[] { new AgentAction(sim.Actions.MoveEast) });
            Assert.Greater(agent.BusyTicks, 0);

            var mask = ActionMask(sim, agent);

            Assert.AreEqual(1, mask.Count(m => m));
            Assert.IsTrue(mask[sim.Actions.Wait]);
        }
    }
}
