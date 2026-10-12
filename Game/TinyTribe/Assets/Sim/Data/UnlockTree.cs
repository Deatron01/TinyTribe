using System;
using System.Collections.Generic;
using System.Text;

namespace TinyTribe.Sim.Data
{
    public enum TreeNodeKind { Tech, Building, Recipe, Crop, Group }

    /// <summary>One box of the unlock tree.</summary>
    public sealed class TreeNode
    {
        public TreeNodeKind Kind;

        /// <summary>The tech, building or crop id; for a recipe, the item it makes.</summary>
        public string Id;
        public string Name;

        /// <summary>What it costs: research points, building materials, a recipe's inputs or a crop's seed.</summary>
        public string Detail = "";

        /// <summary>What else it takes that the tree's lines do not show: a technology, a role, another place to make it.</summary>
        public string Needs = "";

        public TechDef Tech;
        public BuildingDef Building;
        public RecipeDef Recipe;
        public CropDef Crop;

        public TreeNode Parent;
        public readonly List<TreeNode> Children = new List<TreeNode>();

        /// <summary>Column, from the left.</summary>
        public int Depth;

        /// <summary>Place in the column, in node heights from the top. A parent is level with its first child.</summary>
        public float Row;
    }

    /// <summary>
    /// What unlocks what, as one tree for the screen (rule R40): from what is known at the start,
    /// through research, to the buildings a technology allows and what is made in each building.
    /// Built from the workbook's tables, so it is always the game as it is.
    /// </summary>
    public sealed class UnlockTree
    {
        const string ByHandId = "by_hand", ByHandName = "Made by hand";
        const string CropSlots = "crop_slots", AnyRole = "any";

        public TreeNode Root { get; private set; }
        public IReadOnlyList<TreeNode> Nodes => nodes;

        /// <summary>Size of the laid-out tree, in nodes.</summary>
        public int Columns { get; private set; }
        public int Rows { get; private set; }

        readonly List<TreeNode> nodes = new List<TreeNode>();

        public static UnlockTree Build(GameData data)
        {
            var tree = new UnlockTree();
            var techNodes = new Dictionary<string, TreeNode>();
            var buildingNodes = new Dictionary<string, TreeNode>();

            foreach (var tech in data.Tech)
                techNodes[tech.Id] = new TreeNode
                {
                    Kind = TreeNodeKind.Tech, Id = tech.Id, Name = Title(tech.Id), Tech = tech,
                    Detail = tech.RpCost > 0f ? (int)tech.RpCost + " research points" : "known from the start",
                    Needs = tech.RpCost > 0f ? (tech.NeedsLibrary != 0 ? "a Library" : "an Elders' Circle or a Library") : "",
                };
            foreach (var tech in data.Tech)
            {
                var node = techNodes[tech.Id];
                if (tech.RpCost <= 0f && tree.Root == null) tree.Root = node;
                else if (!string.IsNullOrEmpty(tech.Prerequisite) && techNodes.TryGetValue(tech.Prerequisite, out var before)) Attach(before, node);
            }
            if (tree.Root == null) throw new InvalidOperationException("Sim Tech has no technology that is known from the start.");
            foreach (var node in techNodes.Values)
                if (node != tree.Root && node.Parent == null) Attach(tree.Root, node);

            TreeNode TechOr(string techId, TreeNode otherwise) =>
                !string.IsNullOrEmpty(techId) && techNodes.TryGetValue(techId, out var node) ? node : otherwise;
            string ItemName(string itemId) => data.ItemById.TryGetValue(itemId ?? "", out var item) ? item.Name : Title(itemId);
            string BuildingName(string buildingId) => data.BuildingById.TryGetValue(buildingId ?? "", out var building) ? building.Name : Title(buildingId);

            var byHand = new TreeNode { Kind = TreeNodeKind.Group, Id = ByHandId, Name = ByHandName, Detail = "no building needed" };
            Attach(tree.Root, byHand);

            TreeNode firstFarm = null;
            foreach (var building in data.Buildings)
            {
                var node = new TreeNode
                {
                    Kind = TreeNodeKind.Building, Id = building.Id, Name = building.Name, Building = building,
                    Detail = Cost(ItemName, building.CostItem1, building.CostQty1, building.CostItem2, building.CostQty2, building.CostItem3, building.CostQty3),
                };
                buildingNodes[building.Id] = node;
                Attach(TechOr(building.TechRequired, tree.Root), node);
                if (building.EffectKey == CropSlots && string.IsNullOrEmpty(building.TechRequired)) firstFarm = firstFarm ?? node;
            }

            foreach (var recipe in data.Recipes)
            {
                var node = new TreeNode
                {
                    Kind = TreeNodeKind.Recipe, Id = recipe.OutputItem, Recipe = recipe,
                    Name = (recipe.OutputQty > 1 ? recipe.OutputQty + " " : "") + ItemName(recipe.OutputItem),
                    Detail = Cost(ItemName, recipe.Input1, recipe.Qty1, recipe.Input2, recipe.Qty2, recipe.Input3, recipe.Qty3),
                };
                bool hasStation = !string.IsNullOrEmpty(recipe.Workstation) && buildingNodes.ContainsKey(recipe.Workstation);
                var parent = hasStation ? buildingNodes[recipe.Workstation] : TechOr(recipe.TechRequired, byHand);
                Attach(parent, node);

                var needs = new StringBuilder();
                // Under a building the line shows the building; the technology has to be said.
                if (hasStation && !string.IsNullOrEmpty(recipe.TechRequired) && recipe.TechRequired != buildingNodes[recipe.Workstation].Building.TechRequired)
                    needs.Append(Title(recipe.TechRequired));
                if (!string.IsNullOrEmpty(recipe.RoleRequired) && recipe.RoleRequired != AnyRole)
                    needs.Append(needs.Length > 0 ? ", " : "").Append(Title(recipe.RoleRequired));
                if (!string.IsNullOrEmpty(recipe.WorkstationAlt))
                    needs.Append(needs.Length > 0 ? "; " : "").Append("also at the ").Append(BuildingName(recipe.WorkstationAlt));
                node.Needs = needs.ToString();
            }

            foreach (var crop in data.Crops)
            {
                var node = new TreeNode
                {
                    Kind = TreeNodeKind.Crop, Id = crop.Id, Crop = crop, Name = Title(crop.Id),
                    Detail = "1 " + ItemName(crop.SeedItem) + ", " + crop.GrowthTicks + " ticks", Needs = "sown in " + (crop.PlantSeasons ?? "").Replace(";", ", "),
                };
                Attach(firstFarm ?? tree.Root, node);
            }

            // What can be done from the start comes first, then what takes research.
            var first = tree.Root.Children.FindAll(n => n.Kind != TreeNodeKind.Tech);
            var later = tree.Root.Children.FindAll(n => n.Kind == TreeNodeKind.Tech);
            tree.Root.Children.Clear();
            tree.Root.Children.AddRange(first);
            tree.Root.Children.AddRange(later);

            tree.Collect(tree.Root, 0);
            int nextRow = 0;
            tree.Place(tree.Root, ref nextRow);
            tree.Rows = nextRow;
            return tree;
        }

        static void Attach(TreeNode parent, TreeNode child)
        {
            child.Parent = parent;
            parent.Children.Add(child);
        }

        void Collect(TreeNode node, int depth)
        {
            node.Depth = depth;
            nodes.Add(node);
            Columns = Math.Max(Columns, depth + 1);
            foreach (var child in node.Children) Collect(child, depth + 1);
        }

        /// <summary>
        /// Leaves take the rows in order; a parent is level with its first child. The tree is far taller
        /// than a screen, and this way whatever is in view has its parent in view beside it.
        /// </summary>
        void Place(TreeNode node, ref int nextRow)
        {
            if (node.Children.Count == 0)
            {
                node.Row = nextRow++;
                return;
            }
            foreach (var child in node.Children) Place(child, ref nextRow);
            node.Row = node.Children[0].Row;
        }

        static string Cost(Func<string, string> itemName, string item1, int qty1, string item2, int qty2, string item3, int qty3)
        {
            var text = new StringBuilder();
            void Add(string item, int qty)
            {
                if (string.IsNullOrEmpty(item) || qty <= 0) return;
                if (text.Length > 0) text.Append(", ");
                text.Append(qty).Append(' ').Append(itemName(item));
            }
            Add(item1, qty1);
            Add(item2, qty2);
            Add(item3, qty3);
            return text.ToString();
        }

        /// <summary>"lumber_mill" to "Lumber Mill".</summary>
        public static string Title(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            var text = new StringBuilder(id.Length);
            bool upper = true;
            foreach (char c in id)
            {
                if (c == '_')
                {
                    text.Append(' ');
                    upper = true;
                    continue;
                }
                text.Append(upper ? char.ToUpperInvariant(c) : c);
                upper = false;
            }
            return text.ToString();
        }
    }
}
