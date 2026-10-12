using System;
using System.Linq;
using NUnit.Framework;
using TinyTribe.Sim.Data;

namespace TinyTribe.Tests
{
    /// <summary>What unlocks what, as one tree for the screen: research, the buildings it allows, and what is made at each.</summary>
    public class UnlockTreeTests
    {
        static readonly UnlockTree Tree = UnlockTree.Build(Kit.Data);

        static TreeNode Node(TreeNodeKind kind, string id) => Tree.Nodes.Single(n => n.Kind == kind && n.Id == id);

        [Test]
        public void Everything_that_can_be_learned_built_made_or_sown_is_in_the_tree_once()
        {
            foreach (var tech in Kit.Data.Tech)
                Assert.AreEqual(1, Tree.Nodes.Count(n => n.Kind == TreeNodeKind.Tech && n.Id == tech.Id), tech.Id);
            foreach (var building in Kit.Data.Buildings)
                Assert.AreEqual(1, Tree.Nodes.Count(n => n.Kind == TreeNodeKind.Building && n.Id == building.Id), building.Id);
            foreach (var recipe in Kit.Data.Recipes)
                Assert.AreEqual(1, Tree.Nodes.Count(n => n.Kind == TreeNodeKind.Recipe && n.Id == recipe.OutputItem), recipe.OutputItem);
            foreach (var crop in Kit.Data.Crops)
                Assert.AreEqual(1, Tree.Nodes.Count(n => n.Kind == TreeNodeKind.Crop && n.Id == crop.Id), crop.Id);
        }

        [Test]
        public void The_tree_starts_from_what_is_known_from_the_start()
        {
            Assert.AreEqual(TreeNodeKind.Tech, Tree.Root.Kind);
            Assert.AreEqual(0f, Tree.Root.Tech.RpCost);
            Assert.IsNull(Tree.Root.Parent);
            Assert.AreEqual(0, Tree.Root.Depth);
            Assert.IsTrue(Tree.Nodes.All(n => n == Tree.Root || n.Parent != null));
        }

        [Test]
        public void Research_leads_to_buildings_and_buildings_to_what_is_made_in_them()
        {
            Assert.AreSame(Tree.Root, Node(TreeNodeKind.Tech, "carpentry").Parent);
            Assert.AreSame(Node(TreeNodeKind.Tech, "metallurgy"), Node(TreeNodeKind.Tech, "economics").Parent);
            Assert.AreSame(Node(TreeNodeKind.Tech, "carpentry"), Node(TreeNodeKind.Building, "lumber_mill").Parent);
            Assert.AreSame(Node(TreeNodeKind.Building, "lumber_mill"), Node(TreeNodeKind.Recipe, "planks").Parent);
            Assert.AreSame(Tree.Root, Node(TreeNodeKind.Building, "garden_plot").Parent, "a garden takes no research");
            Assert.AreSame(Node(TreeNodeKind.Tech, "agriculture"), Node(TreeNodeKind.Building, "farm_windmill").Parent);
            Assert.AreSame(Node(TreeNodeKind.Building, "campfire"), Node(TreeNodeKind.Recipe, "cooked_meat").Parent);
            Assert.AreSame(Node(TreeNodeKind.Tech, "agriculture"), Node(TreeNodeKind.Recipe, "farming_hoe").Parent, "made by hand, once agriculture is known");
        }

        [Test]
        public void What_is_made_by_hand_from_the_start_hangs_together()
        {
            var byHand = Node(TreeNodeKind.Recipe, "wooden_axe").Parent;

            Assert.AreEqual(TreeNodeKind.Group, byHand.Kind);
            Assert.AreSame(Tree.Root, byHand.Parent);
            Assert.AreSame(byHand, Node(TreeNodeKind.Recipe, "fishing_rod").Parent);
        }

        [Test]
        public void Crops_hang_under_the_garden()
        {
            foreach (var crop in Kit.Data.Crops)
                Assert.AreSame(Node(TreeNodeKind.Building, "garden_plot"), Node(TreeNodeKind.Crop, crop.Id).Parent);
        }

        [Test]
        public void A_node_says_what_it_costs_and_what_else_it_takes()
        {
            var garden = Kit.Data.BuildingById["garden_plot"];
            StringAssert.Contains(garden.CostQty1 + " " + Kit.Data.ItemById[garden.CostItem1].Name, Node(TreeNodeKind.Building, "garden_plot").Detail);
            Assert.AreEqual(garden.Name, Node(TreeNodeKind.Building, "garden_plot").Name);
            StringAssert.Contains("Agriculture", Node(TreeNodeKind.Recipe, "bread").Needs, "bread is baked at the fire, but only once agriculture is known");
            StringAssert.Contains("Blacksmith", Node(TreeNodeKind.Recipe, "iron_axe").Needs);
            StringAssert.Contains(((int)Kit.Data.Tech.First(t => t.Id == "carpentry").RpCost).ToString(), Node(TreeNodeKind.Tech, "carpentry").Detail);
        }

        [Test]
        public void Laid_out_no_two_nodes_share_a_place_and_a_parent_sits_among_its_children()
        {
            foreach (var column in Tree.Nodes.GroupBy(n => n.Depth))
            {
                var rows = column.Select(n => n.Row).OrderBy(r => r).ToArray();
                for (int i = 1; i < rows.Length; i++)
                    Assert.GreaterOrEqual(rows[i] - rows[i - 1], 1f - 1e-3f, "two nodes on top of each other in column " + column.Key);
            }
            foreach (var node in Tree.Nodes.Where(n => n.Children.Count > 0))
            {
                Assert.GreaterOrEqual(node.Row, node.Children.Min(c => c.Row) - 1e-3f, node.Id);
                Assert.LessOrEqual(node.Row, node.Children.Max(c => c.Row) + 1e-3f, node.Id);
                Assert.IsTrue(node.Children.All(c => c.Depth == node.Depth + 1));
            }
            Assert.AreEqual(Tree.Nodes.Max(n => n.Depth) + 1, Tree.Columns);
            Assert.AreEqual(Tree.Nodes.Count(n => n.Children.Count == 0), Tree.Rows, "one row for every leaf");
        }
    }
}
