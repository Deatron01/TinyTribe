using System.Collections.Generic;
using TinyTribe.Brains;
using TinyTribe.Neural;
using TinyTribe.Sim.Core;
using TinyTribe.Sim.Data;
using UnityEngine;

namespace TinyTribe.View
{
    /// <summary>The run's achievements, and the tree of everything that can be learned, built and made.</summary>
    public sealed partial class Hud
    {
        // Whole numbers, and an even gap between rows: a one-pixel line that lands between two pixels is not drawn at all.
        const float TreeColumnWidth = 204f, TreeNodeWidth = 188f, TreeRowHeight = 40f, TreeNodeHeight = 36f;

        static readonly Color DoneColor = new Color(0.22f, 0.50f, 0.27f), NowColor = new Color(0.72f, 0.58f, 0.16f);
        static readonly Color OpenColor = new Color(0.25f, 0.34f, 0.50f), LockedColor = new Color(0.17f, 0.18f, 0.21f);
        static readonly Color LineColor = new Color(0.55f, 0.58f, 0.64f, 0.9f), LockedLineColor = new Color(0.33f, 0.35f, 0.40f, 0.9f);

        enum TreeState { Done, Now, Open, Locked }

        const int AchievementsWindow = 5, TreeWindow = 6;
        const string LibraryStudy = "rp_per_tick", CircleStudy = "rp_per_tick_basic";

        bool showAchievements, showTree;

        /// <summary>A window that was just opened comes to the front: the tree is wide and would open under the others.</summary>
        int bringToFront;
        Rect achievementsRect, treeRect;
        Vector2 achievementsScroll, treeScroll;
        UnlockTree tree;
        readonly Dictionary<string, List<RecipeDef>> recipesMaking = new Dictionary<string, List<RecipeDef>>();
        GUIStyle treeTitle, treeDetail;
        bool recordChoice = true;

        // The trained networks installed in the project (ML/install_model.py), looked up when the launch screen opens.
        string[] models;
        int modelChoice;
        bool greedyChoice;

        /// <summary>Under the brain toolbar: which trained network, when that is what was chosen.</summary>
        void DrawModelChoice()
        {
            if (models == null) models = NeuralBrain.Installed();
            if (BrainKinds[brainChoice] != BrainKind.Neural) return;
            if (models.Length == 0)
            {
                GUILayout.Label("No trained network is installed yet (ML/install_model.py puts one in). The survival bots will play.", wrapLabel);
                return;
            }
            if (models.Length > 1) modelChoice = GUILayout.Toolbar(Mathf.Clamp(modelChoice, 0, models.Length - 1), models);
            else GUILayout.Label("Network: " + models[0], smallLabel);
            greedyChoice = GUILayout.Toggle(greedyChoice, " Always its top choice (otherwise it picks by chance, in proportion to its scores)");
        }

        /// <summary>Whether the Achievements window is open.</summary>
        public bool ShowAchievements
        {
            get => showAchievements;
            set
            {
                if (value && !showAchievements) bringToFront = AchievementsWindow;
                showAchievements = value;
            }
        }

        /// <summary>Whether the tree of what can be done is open.</summary>
        public bool ShowTree
        {
            get => showTree;
            set
            {
                if (value && !showTree) bringToFront = TreeWindow;
                showTree = value;
            }
        }

        void PlacePanels(float width)
        {
            achievementsRect = new Rect(306, BarHeight + 8, 310, 0);
            float treeWidth = Mathf.Min(width - 60f, 1060f);
            treeRect = new Rect((width - treeWidth) * 0.5f, BarHeight + 36, treeWidth, 0);
        }

        void DrawPanels()
        {
            if (showAchievements) achievementsRect = Window(AchievementsWindow, achievementsRect, DrawAchievements, "Achievements of this run");
            if (showTree) treeRect = Window(TreeWindow, treeRect, DrawTree, "What can be learned, built and made");
            if (bringToFront == 0) return;
            GUI.BringWindowToFront(bringToFront);
            GUI.FocusWindow(bringToFront);
            bringToFront = 0;
        }

        // ------------------------------------------------------------------ achievements

        void DrawAchievements(int id)
        {
            Backdrop(achievementsRect);
            var stats = app.Stats;
            var sim = app.Runner.Sim;
            if (stats == null)
            {
                GUILayout.Label("Nothing is being counted in this game.", wrapLabel);
                GUI.DragWindow();
                return;
            }

            var recorder = app.Recorder;
            GUILayout.Label(recorder == null ? "This run is not being recorded."
                : (recorder.Finished ? "Saved as " : "Recording as ") + recorder.RunId, wrapLabel);
            GUILayout.Label("Day " + (stats.Get("days") + 1) + "   alive " + stats.Get("alive") + "/" + sim.Agents.Count +
                            "   built " + stats.Get("built") + "   learned " + stats.Get("techs") + "   deaths " + stats.Get("deaths"), smallLabel);
            GUILayout.Label(stats.Achievements.Count + " of " + app.Data.Achievements.Count + " reached", headerLabel);

            achievementsScroll = GUILayout.BeginScrollView(achievementsScroll, GUILayout.Height(Mathf.Clamp(viewHeight - 190f, 160f, 420f)));
            string group = null;
            foreach (var achievement in app.Data.Achievements)
            {
                if (achievement.Group != group)
                {
                    group = achievement.Group;
                    GUILayout.Label(Title(group), headerLabel);
                }
                int day = -1;
                foreach (var reached in stats.Achievements)
                    if (reached.Def == achievement) day = reached.Day;
                GUILayout.Label(day >= 0
                    ? "<color=#8fdc6a>[x] " + achievement.Name + "</color>   <color=#9aa0aa>day " + (day + 1) + "</color>"
                    : "<color=#7c818b>[  ] " + achievement.Name + "</color>", richLabel);
                GUILayout.Label("      " + achievement.Description, smallLabel);
            }
            GUILayout.EndScrollView();
            GUI.DragWindow();
        }

        // ------------------------------------------------------------------ the tree

        TreeState StateOf(Simulation sim, TreeNode node)
        {
            switch (node.Kind)
            {
                case TreeNodeKind.Tech:
                    if (sim.IsResearched(node.Tech.Id)) return TreeState.Done;
                    if (sim.ResearchingTech == node.Tech) return TreeState.Now;
                    // Open when what comes before it is known and there is somewhere it can be studied.
                    bool taught = FinishedWith(LibraryStudy) > 0 || (node.Tech.NeedsLibrary == 0 && FinishedWith(CircleStudy) > 0);
                    return taught && sim.IsResearched(node.Tech.Prerequisite) ? TreeState.Open : TreeState.Locked;
                case TreeNodeKind.Building:
                    if (Finished(node.Building.Id) > 0) return TreeState.Done;
                    return sim.IsResearched(node.Building.TechRequired) && Missing(sim, node) == null ? TreeState.Open : TreeState.Locked;
                case TreeNodeKind.Recipe:
                    return CanMake(sim, node.Recipe, 0) ? TreeState.Open : TreeState.Locked;
                case TreeNodeKind.Crop:
                    return node.Parent != null && node.Parent.Kind == TreeNodeKind.Building && Finished(node.Parent.Building.Id) > 0
                        ? TreeState.Open : TreeState.Locked;
                default:
                    return TreeState.Open;
            }
        }

        /// <summary>The technology is known, the place to make it stands, and everything that goes into it can be had.</summary>
        bool CanMake(Simulation sim, RecipeDef recipe, int depth)
        {
            if (!sim.IsResearched(recipe.TechRequired)) return false;
            if (!string.IsNullOrEmpty(recipe.Workstation) && Finished(recipe.Workstation) == 0 && Finished(recipe.WorkstationAlt) == 0) return false;
            return CanHave(sim, recipe.Input1, depth) && CanHave(sim, recipe.Input2, depth) && CanHave(sim, recipe.Input3, depth);
        }

        /// <summary>Raw things can always be had; made things when one of their recipes can be made.</summary>
        bool CanHave(Simulation sim, string itemId, int depth)
        {
            if (string.IsNullOrEmpty(itemId) || !recipesMaking.TryGetValue(itemId, out var recipes)) return true;
            if (depth > 6) return false;
            foreach (var recipe in recipes)
                if (CanMake(sim, recipe, depth + 1)) return true;
            return false;
        }

        /// <summary>The first material of a building or recipe that the village has no way of making yet, or null.</summary>
        string Missing(Simulation sim, TreeNode node)
        {
            string a, b, c;
            if (node.Kind == TreeNodeKind.Building)
            {
                a = node.Building.CostItem1;
                b = node.Building.CostItem2;
                c = node.Building.CostItem3;
            }
            else if (node.Kind == TreeNodeKind.Recipe)
            {
                a = node.Recipe.Input1;
                b = node.Recipe.Input2;
                c = node.Recipe.Input3;
            }
            else return null;
            foreach (string item in new[] { a, b, c })
                if (!CanHave(sim, item, 0)) return app.Data.ItemById.TryGetValue(item, out var def) ? def.Name : item;
            return null;
        }

        /// <summary>Finished buildings of that kind, as last counted for the Resources window.</summary>
        int Finished(string buildingId)
        {
            if (string.IsNullOrEmpty(buildingId) || !app.Data.BuildingById.TryGetValue(buildingId, out var building)) return 0;
            return (uint)building.Int < (uint)buildingTotals.Length ? buildingTotals[building.Int] : 0;
        }

        /// <summary>Finished buildings with that effect, whatever they are called.</summary>
        int FinishedWith(string effectKey)
        {
            int count = 0;
            foreach (var building in app.Data.Buildings)
                if (building.EffectKey == effectKey && (uint)building.Int < (uint)buildingTotals.Length) count += buildingTotals[building.Int];
            return count;
        }

        static Color ColorOf(TreeState state) =>
            state == TreeState.Done ? DoneColor : state == TreeState.Now ? NowColor : state == TreeState.Open ? OpenColor : LockedColor;

        static Rect NodeRect(Rect area, TreeNode node) =>
            new Rect(area.x + node.Depth * TreeColumnWidth, area.y + node.Row * TreeRowHeight + (TreeRowHeight - TreeNodeHeight) * 0.5f, TreeNodeWidth, TreeNodeHeight);

        static void Fill(Rect rect, Color color)
        {
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = old;
        }

        void DrawTree(int id)
        {
            Backdrop(treeRect);
            if (tree == null)
            {
                tree = UnlockTree.Build(app.Data);
                foreach (var recipe in app.Data.Recipes)
                {
                    if (!recipesMaking.TryGetValue(recipe.OutputItem, out var list)) recipesMaking[recipe.OutputItem] = list = new List<RecipeDef>();
                    list.Add(recipe);
                }
            }
            if (treeTitle == null)
            {
                treeTitle = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold, clipping = TextClipping.Clip, wordWrap = false, richText = true };
                treeDetail = new GUIStyle(GUI.skin.label) { fontSize = 9, clipping = TextClipping.Clip, wordWrap = false };
                treeTitle.normal.textColor = Color.white;
                treeDetail.normal.textColor = new Color(0.86f, 0.88f, 0.92f);
            }
            var sim = app.Runner.Sim;

            GUILayout.BeginHorizontal();
            Legend(DoneColor, "known / built");
            Legend(NowColor, "being studied");
            Legend(OpenColor, "can be done now");
            Legend(LockedColor, "not yet: follow the line back");
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            treeScroll = GUILayout.BeginScrollView(treeScroll, GUILayout.Height(Mathf.Clamp(viewHeight - 150f, 200f, 620f)));
            var area = GUILayoutUtility.GetRect(tree.Columns * TreeColumnWidth, tree.Rows * TreeRowHeight);
            area.x = Mathf.Round(area.x);
            area.y = Mathf.Round(area.y);
            if (Event.current.type == EventType.Repaint)
            {
                // Lines first, so the boxes sit on top of them: out of the parent, down or up, into the child.
                // The lines to what is open are drawn last, over the fainter ones they share a stretch with.
                for (int pass = 0; pass < 2; pass++)
                    foreach (var node in tree.Nodes)
                    {
                        if (node.Parent == null) continue;
                        bool locked = StateOf(sim, node) == TreeState.Locked;
                        if (locked != (pass == 0)) continue;
                        Rect from = NodeRect(area, node.Parent), to = NodeRect(area, node);
                        var color = locked ? LockedLineColor : LineColor;
                        float bendX = to.x - (TreeColumnWidth - TreeNodeWidth) * 0.5f, fromY = from.center.y, toY = to.center.y;
                        Fill(new Rect(from.xMax, fromY, bendX - from.xMax + 1f, 1f), color);
                        Fill(new Rect(bendX, Mathf.Min(fromY, toY), 1f, Mathf.Abs(toY - fromY) + 1f), color);
                        Fill(new Rect(bendX, toY, to.x - bendX, 1f), color);
                    }
                foreach (var node in tree.Nodes)
                {
                    var rect = NodeRect(area, node);
                    var state = StateOf(sim, node);
                    Fill(rect, ColorOf(state));
                    string name = node.Name;
                    if (node.Kind == TreeNodeKind.Building && state == TreeState.Done) name += "  x" + Finished(node.Building.Id);
                    if (node.Kind == TreeNodeKind.Tech && state == TreeState.Now && node.Tech.RpCost > 0f)
                        name += "  " + Mathf.FloorToInt(100f * sim.ResearchPoints / node.Tech.RpCost) + "%";
                    var old = GUI.color;
                    if (state == TreeState.Locked) GUI.color = new Color(1f, 1f, 1f, 0.55f);
                    GUI.Label(new Rect(rect.x + 4f, rect.y + 1f, rect.width - 6f, 16f), name, treeTitle);
                    string detail = string.IsNullOrEmpty(node.Needs) ? node.Detail : node.Detail + (node.Detail.Length > 0 ? "  |  " : "") + node.Needs;
                    // Nothing in the way but a material nobody can make yet: say which.
                    string missing = state == TreeState.Locked ? Missing(sim, node) : null;
                    if (missing != null) detail = "no " + missing + " yet  |  " + detail;
                    GUI.Label(new Rect(rect.x + 4f, rect.y + 17f, rect.width - 6f, 15f), detail, treeDetail);
                    GUI.color = old;
                }
            }
            GUILayout.EndScrollView();
            GUI.DragWindow();
        }

        void Legend(Color color, string label)
        {
            var swatch = GUILayoutUtility.GetRect(12f, 12f, GUILayout.Width(12f));
            if (Event.current.type == EventType.Repaint) Fill(new Rect(swatch.x, swatch.y + 4f, 12f, 10f), color);
            GUILayout.Label(label, smallLabel);
            GUILayout.Space(10f);
        }
    }
}
