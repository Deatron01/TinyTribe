using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace TinyTribe.Tests
{
    public class GameDataLookupTests
    {
        [Test]
        public void Seasons_are_in_calendar_order()
        {
            CollectionAssert.AreEqual(
                new[] { "spring", "summer", "autumn", "winter" },
                Kit.Data.Seasons.Select(s => s.Id).ToArray());
        }

        [Test]
        public void Need_modifiers_are_read_by_id()
        {
            Assert.Greater(Kit.Data.Modifier("hp_regen"), 0f);
            Assert.Less(Kit.Data.Modifier("thirst_work_action"), 0f);
        }

        [Test]
        public void Unknown_modifier_throws()
        {
            Assert.Throws<KeyNotFoundException>(() => Kit.Data.Modifier("no_such_modifier"));
        }

        [Test]
        public void Items_are_found_by_int()
        {
            var berry = Kit.Data.ItemById["berry"];
            Assert.AreSame(berry, Kit.Data.ItemByInt(berry.Int));
            Assert.IsNull(Kit.Data.ItemByInt(0));
            Assert.IsNull(Kit.Data.ItemByInt(9999));
        }

        [Test]
        public void Equipment_is_found_by_item_int_and_null_for_other_items()
        {
            Assert.AreEqual("axe", Kit.Data.EquipmentByItemInt(Kit.Item("stone_axe")).ToolType);
            Assert.IsNull(Kit.Data.EquipmentByItemInt(Kit.Item("berry")));
        }

        [Test]
        public void Nodes_and_terrain_are_found_by_id_and_int()
        {
            var pine = Kit.Data.NodeById("pine_tree");
            Assert.AreEqual("pine_wood", pine.YieldItem);
            Assert.AreSame(pine, Kit.Data.NodeByInt(pine.Int));

            var water = Kit.Data.TerrainById("water");
            Assert.AreEqual(0, water.Walkable);
            Assert.AreSame(water, Kit.Data.TerrainByInt(water.Int));
        }

        [Test]
        public void Needs_are_found_by_id()
        {
            Assert.AreEqual(1, Kit.Data.NeedById("hunger").ObsIndex);
        }

        [Test]
        public void Action_ids_are_found_by_name()
        {
            int id = Kit.Data.ActionId("harvest_attack");
            Assert.AreEqual("harvest_attack", Kit.Data.Actions[id].Name);
        }

        [Test]
        public void World_gen_table_names_existing_nodes()
        {
            Assert.IsNotEmpty(Kit.Data.WorldGen);
            foreach (var entry in Kit.Data.WorldGen)
                Assert.IsNotNull(Kit.Data.NodeById(entry.NodeId), entry.NodeId);
        }
    }
}
