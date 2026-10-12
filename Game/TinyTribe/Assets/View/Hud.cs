using System.Collections.Generic;
using System.Text;
using TinyTribe.Brains;
using TinyTribe.Sim.Core;
using TinyTribe.Sim.Data;
using UnityEngine;

namespace TinyTribe.View
{
    /// <summary>
    /// The windows: launch options before a game, and during one the time controls, the list of
    /// villagers, the selected villager with its needs and pack, and the village resources.
    /// Drawn with Unity's immediate-mode GUI so it needs no scene objects or assets.
    /// </summary>
    public sealed partial class Hud : MonoBehaviour
    {
        /// <summary>Speeds offered in the Time window, in multiples of normal speed. The last one is "as fast as possible".</summary>
        public static readonly float[] SpeedSteps = { 1f, 2f, 5f, 20f, 100f, 4000f };

        static readonly string[] SpeedLabels = { "1x", "2x", "5x", "20x", "100x", "Max" };
        static readonly string[] MapLabels = { "Small 64", "Medium 128", "Large 192" };
        static readonly int[] MapSizes = { 64, 128, 192 };
        static readonly string[] BrainLabels = { "Survival bot", "Trained network", "Random", "Nobody" };
        static readonly BrainKind[] BrainKinds = { BrainKind.Survival, BrainKind.Neural, BrainKind.Random, BrainKind.Idle };
        static readonly string[] StartSpeedLabels = { "Paused", "1x", "5x", "20x" };
        static readonly float[] StartSpeeds = { 0f, 1f, 5f, 20f };
        static readonly string[] NeedLabels = { "Health", "Hunger", "Thirst", "Energy", "Warmth", "Morale" };
        static readonly Color[] NeedColors =
        {
            new Color(0.86f, 0.25f, 0.25f), new Color(0.90f, 0.58f, 0.20f), new Color(0.27f, 0.56f, 0.92f),
            new Color(0.92f, 0.82f, 0.25f), new Color(0.35f, 0.78f, 0.80f), new Color(0.70f, 0.45f, 0.86f),
        };
        static readonly string[] SlotLabels = { "Hand", "Body", "Feet", "Head", "Back" };

        const float BarHeight = 24f;
        const float SummaryInterval = 0.5f;
        const string WarmthEffect = "warmth_radius";

        GameApp app;
        IArt art;

        /// <summary>The number of living villagers the windows currently show.</summary>
        public int AliveShown => aliveCount;

        /// <summary>Number of frames the windows have been painted. Lets tests confirm drawing happens.</summary>
        public int FramesDrawn { get; private set; }

        // Which windows are open, and where.
        bool showTime = true, showVillagers = true, showSelected = true, showResources = true, showLabels = true;
        Rect timeRect, villagersRect, selectedRect, resourcesRect, launcherRect;
        bool placed;
        Vector2 villagersScroll, resourcesScroll, selectedScroll;
        float selectedBodyHeight = 260f, viewHeight = 720f;

        /// <summary>Height of the parts of the Selected villager window that do not scroll: title, needs and the buttons at the bottom.</summary>
        const float SelectedFixedHeight = 280f;
        bool showMake = true;
        float userScale;
        readonly List<Rect> occupied = new List<Rect>();

        // Launch options being edited.
        string seedText = "1";
        float agentSlider = 6f;
        int mapChoice = 1, seasonChoice, brainChoice, speedChoice = 1;
        bool weatherChoice = true, sicknessChoice, jobReviewChoice = true;
        string[] seasonLabels = new string[0];

        // Village totals, refreshed twice a second.
        int[] itemTotals = new int[0];
        int[] nodeTotals = new int[0];
        int[] storedTotals = new int[0];
        int[] buildingTotals = new int[0];
        int[] siteTotals = new int[0];
        int[] animalTotals = new int[0];
        string fireText = "";
        int aliveCount;

        // What each craft_build argument makes, and which of them the player's villager can start right now.
        string[] argNames = new string[0];
        string[] roleNames = new string[0];
        bool[] argIsBuilding = new bool[0];
        string[] argVerb = new string[0], argDoing = new string[0];
        bool[] craftMask = new bool[0];
        int craftMaskFrame = -1, craftMaskAgent = -1;
        float summaryAge = float.MaxValue;
        SimRunner summarised;

        GUIStyle richLabel, smallLabel, barLabel, wrapLabel, headerLabel, rowButton, mapLabel;
        readonly StringBuilder text = new StringBuilder();

        public void Init(GameApp owner, IArt artSource)
        {
            app = owner;
            art = artSource;
            var data = owner.Data;
            agentSlider = data.ConfigInt("initial_agents");
            seasonLabels = new string[data.Seasons.Count];
            for (int i = 0; i < seasonLabels.Length; i++) seasonLabels[i] = Title(data.Seasons[i].Id);
            string startSeason = data.ConfigString("start_season");
            for (int i = 0; i < data.Seasons.Count; i++)
                if (data.Seasons[i].Id == startSeason) seasonChoice = i;
            itemTotals = new int[data.Items.Count + 1];
            nodeTotals = new int[data.ResourceNodes.Count + 1];
            storedTotals = new int[data.Items.Count + 1];
            buildingTotals = new int[data.Buildings.Count + 1];
            siteTotals = new int[data.Buildings.Count + 1];
            int maxAnimal = 0;
            foreach (var animal in data.Animals) maxAnimal = Mathf.Max(maxAnimal, animal.Int);
            animalTotals = new int[maxAnimal + 1];

            int maxRole = 0;
            foreach (var role in data.Roles) maxRole = Mathf.Max(maxRole, role.Int);
            roleNames = new string[maxRole + 1];
            foreach (var role in data.Roles) roleNames[role.Int] = role.Int == 0 ? "no job yet" : Title(role.Id);

            argNames = new string[data.ActionArgs.Count];
            argIsBuilding = new bool[data.ActionArgs.Count];
            argVerb = new string[data.ActionArgs.Count];
            argDoing = new string[data.ActionArgs.Count];
            craftMask = new bool[data.ActionArgs.Count];
            foreach (var arg in data.ActionArgs)
            {
                if ((uint)arg.Int >= (uint)argNames.Length) continue;
                argNames[arg.Int] = Title(arg.RefId);
                argVerb[arg.Int] = arg.Kind == "building" ? "Build " : arg.Kind == "plant_crop" ? "Plant " : arg.Kind == "research" ? "Study here" : "Make ";
                argDoing[arg.Int] = arg.Kind == "building" ? "building " : arg.Kind == "plant_crop" ? "planting " : arg.Kind == "research" ? "studying" : "making ";
                if (arg.Kind == "research") argNames[arg.Int] = "";
                if (arg.Kind == "plant_crop")
                    foreach (var crop in data.Crops)
                        if (crop.Id == arg.RefId && data.ItemById.TryGetValue(crop.YieldItem, out var grown)) argNames[arg.Int] = grown.Name;
                if (arg.Kind == "recipe" && data.ItemById.TryGetValue(arg.RefId ?? "", out var item)) argNames[arg.Int] = item.Name;
                else if (arg.Kind == "building" && data.BuildingById.TryGetValue(arg.RefId ?? "", out var building))
                {
                    argNames[arg.Int] = building.Name;
                    argIsBuilding[arg.Int] = true;
                }
            }
        }

        static string Title(string id) => string.IsNullOrEmpty(id) ? "" : char.ToUpperInvariant(id[0]) + id.Substring(1).Replace('_', ' ');

        float Scale => userScale > 0f ? userScale : Mathf.Max(1f, Mathf.Round(Screen.height / 720f * 4f) / 4f);

        /// <summary>Is the pointer (in screen pixels, origin bottom-left) over a window or the top bar?</summary>
        public bool IsPointerOverWindow(Vector2 screenPosition)
        {
            float scale = Scale;
            var point = new Vector2(screenPosition.x / scale, (Screen.height - screenPosition.y) / scale);
            foreach (var rect in occupied)
                if (rect.Contains(point)) return true;
            return false;
        }

        void Update()
        {
            if (app == null || !app.IsRunning) return;
            // A new game shows its totals at once instead of what was left from the last one.
            if (!ReferenceEquals(app.Runner, summarised)) summaryAge = float.MaxValue;
            summarised = app.Runner;
            summaryAge += Time.unscaledDeltaTime;
            if (summaryAge < SummaryInterval) return;
            summaryAge = 0f;
            var sim = app.Runner.Sim;
            VillageSummary.CountHeldItems(sim, itemTotals);
            VillageSummary.CountActiveNodes(sim.World, nodeTotals);
            VillageSummary.CountStoredItems(sim, storedTotals);
            VillageSummary.CountBuildings(sim, buildingTotals, siteTotals);
            VillageSummary.CountAnimals(sim, animalTotals);
            // A recorder looks into the storage at its own samples; without one, this is where the counters learn of it.
            if (app.Recorder == null) app.Stats?.Sample(sim);
            aliveCount = VillageSummary.AliveCount(sim);

            text.Length = 0;
            foreach (var building in sim.Buildings)
            {
                if (!building.Complete || building.Def.EffectKey != WarmthEffect) continue;
                if (text.Length > 0) text.Append('\n');
                text.Append(building.Def.Name).Append(building.IsLit
                    ? ": lit, fuel for " + (building.FuelTicks / (float)sim.Config.TicksPerHour).ToString("0") + " h"
                    : ": out");
            }
            fireText = text.ToString();
        }

        void EnsureStyles()
        {
            if (richLabel != null) return;
            richLabel = new GUIStyle(GUI.skin.label) { richText = true };
            smallLabel = new GUIStyle(GUI.skin.label) { fontSize = 10, richText = true };
            wrapLabel = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 11 };
            headerLabel = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            barLabel = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontSize = 11, padding = new RectOffset(4, 4, 0, 0) };
            barLabel.normal.textColor = Color.white;
            rowButton = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, richText = true, fontSize = 11 };
            mapLabel = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 10, fontStyle = FontStyle.Bold };
        }

        void OnGUI()
        {
            if (app == null) return;
            EnsureStyles();
            float scale = Scale;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            float width = Screen.width / scale, height = Screen.height / scale;
            viewHeight = height;
            if (Event.current.type == EventType.Layout) occupied.Clear();

            if (!app.IsRunning)
            {
                placed = false;
                DrawLauncher(width, height);
            }
            else
            {
                if (!placed) PlaceWindows(width);
                if (showLabels) DrawMapLabels(scale);
                DrawTopBar(width);
                if (showTime) timeRect = Window(1, timeRect, DrawTime, "Time");
                if (showVillagers) villagersRect = Window(2, villagersRect, DrawVillagers, "Villagers");
                if (showSelected) selectedRect = Window(3, selectedRect, DrawSelected, "Selected villager");
                if (showResources) resourcesRect = Window(4, resourcesRect, DrawResources, "Resources");
                DrawPanels();
            }

            if (Event.current.type == EventType.Repaint) FramesDrawn++;
        }

        void PlaceWindows(float width)
        {
            placed = true;
            // The Time window grows by a line while the Council votes; the villagers' rows carry their jobs.
            timeRect = new Rect(8, BarHeight + 8, 290, 0);
            villagersRect = new Rect(8, BarHeight + 222, 290, 0);
            selectedRect = new Rect(width - 288, BarHeight + 8, 280, 0);
            resourcesRect = new Rect(Mathf.Max(306, width - 288 - 228), BarHeight + 8, 220, 0);
            PlacePanels(width);
        }

        /// <summary>
        /// Solid panel behind a window's contents. The built-in window skin is see-through,
        /// which makes text unreadable over the map.
        /// </summary>
        static void Backdrop(Rect window)
        {
            if (Event.current.type != EventType.Repaint) return;
            var old = GUI.color;
            GUI.color = new Color(0.10f, 0.11f, 0.14f, 0.96f);
            GUI.DrawTexture(new Rect(2f, 18f, window.width - 4f, Mathf.Max(0f, window.height - 20f)), Texture2D.whiteTexture);
            GUI.color = old;
        }

        Rect Window(int id, Rect rect, GUI.WindowFunction draw, string title)
        {
            // Height 1 lets the window shrink to its contents each frame; the backdrop uses the height it had last.
            var asked = new Rect(rect.x, rect.y, rect.width, 1f);
            rect = GUILayout.Window(id, asked, draw, title, GUILayout.Width(rect.width));
            if (Event.current.type == EventType.Layout) occupied.Add(rect);
            return rect;
        }

        // ------------------------------------------------------------------ launch screen

        void DrawLauncher(float width, float height)
        {
            launcherRect = new Rect((width - 420) * 0.5f, Mathf.Max(8f, (height - 400) * 0.5f), 420, 0);
            launcherRect = GUILayout.Window(10, launcherRect, DrawLauncherWindow, "TinyTribe - new game", GUILayout.Width(420));
            if (Event.current.type == EventType.Layout) occupied.Add(launcherRect);
        }

        void DrawLauncherWindow(int id)
        {
            Backdrop(launcherRect);
            var data = app.Data;
            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Map seed", GUILayout.Width(120));
            seedText = GUILayout.TextField(seedText, 20);
            if (GUILayout.Button("Random", GUILayout.Width(70))) seedText = Random.Range(1, 1000000).ToString();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Villagers", GUILayout.Width(120));
            agentSlider = GUILayout.HorizontalSlider(agentSlider, 1f, data.ConfigInt("max_agents"));
            GUILayout.Label(Mathf.RoundToInt(agentSlider).ToString(), GUILayout.Width(30));
            GUILayout.EndHorizontal();

            GUILayout.Label("Map size");
            mapChoice = GUILayout.Toolbar(mapChoice, MapLabels);
            GUILayout.Label("Starting season");
            seasonChoice = GUILayout.Toolbar(seasonChoice, seasonLabels);
            GUILayout.Label("Villagers are driven by");
            brainChoice = GUILayout.Toolbar(brainChoice, BrainLabels);
            DrawModelChoice();
            GUILayout.Label("Starting speed");
            speedChoice = GUILayout.Toolbar(speedChoice, StartSpeedLabels);
            weatherChoice = GUILayout.Toggle(weatherChoice, " Weather: rain, storms and heatwaves");
            sicknessChoice = GUILayout.Toggle(sicknessChoice, " Sickness (hard: a wolf's bite infects, and the cure needs Medicine)");
            jobReviewChoice = GUILayout.Toggle(jobReviewChoice, " Job reviews: the Council goes over everyone's job as the village changes");
            recordChoice = GUILayout.Toggle(recordChoice, " Record this run: every action and event, a timeline, and the result");

            GUILayout.Space(8);
            if (GUILayout.Button("Start game", GUILayout.Height(34))) app.StartGame(BuildSetup());

            GUILayout.Space(6);
            GUILayout.Label("In the game so far: the map, day and night, seasons and weather, needs, gathering, tools, crafting, buildings, " +
                            "storage, animals, jobs voted by the Council, research (an Elders' Circle first, a Library later), farming and sickness. " +
                            "Not yet: families, justice, money and trade.", wrapLabel);
        }

        /// <summary>The setup described by the launch screen as it stands.</summary>
        public GameSetup BuildSetup()
        {
            var setup = new GameSetup { Brains = BrainKinds[brainChoice] };
            if (setup.Brains == BrainKind.Neural)
            {
                // Nothing trained is installed: the bots play, and the launch screen has said so.
                if (models.Length == 0) setup.Brains = BrainKind.Survival;
                else setup.Model = models[Mathf.Clamp(modelChoice, 0, models.Length - 1)];
                setup.Greedy = greedyChoice;
            }
            if (!ulong.TryParse(seedText, out setup.Sim.Seed)) setup.Sim.Seed = (ulong)Mathf.Abs((seedText ?? "").GetHashCode()) + 1UL;
            setup.Sim.Agents = Mathf.RoundToInt(agentSlider);
            setup.Sim.MapWidth = setup.Sim.MapHeight = MapSizes[mapChoice];
            setup.Sim.StartSeason = app.Data.Seasons[seasonChoice].Id;
            setup.Sim.Weather = weatherChoice;
            setup.Sim.Sickness = sicknessChoice;
            setup.Sim.JobReviews = jobReviewChoice;
            setup.Record = recordChoice;
            setup.TicksPerSecond = GameApp.NormalSpeed * Mathf.Max(1f, StartSpeeds[speedChoice]);
            setup.StartPaused = StartSpeeds[speedChoice] <= 0f;
            return setup;
        }

        // ------------------------------------------------------------------ top bar and map labels

        void DrawTopBar(float width)
        {
            var bar = new Rect(0, 0, width, BarHeight);
            if (Event.current.type == EventType.Layout) occupied.Add(bar);
            GUI.Box(bar, GUIContent.none);
            GUILayout.BeginArea(new Rect(4, 2, width - 8, BarHeight - 2));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("New game", GUILayout.Width(80))) app.StopGame();
            GUILayout.Space(10);
            showTime = GUILayout.Toggle(showTime, "Time", GUI.skin.button, GUILayout.Width(52));
            showVillagers = GUILayout.Toggle(showVillagers, "Villagers", GUI.skin.button, GUILayout.Width(72));
            showSelected = GUILayout.Toggle(showSelected, "Selected", GUI.skin.button, GUILayout.Width(70));
            showResources = GUILayout.Toggle(showResources, "Resources", GUI.skin.button, GUILayout.Width(80));
            showLabels = GUILayout.Toggle(showLabels, "Names", GUI.skin.button, GUILayout.Width(58));
            GUILayout.Space(10);
            ShowAchievements = GUILayout.Toggle(showAchievements, "Achievements", GUI.skin.button, GUILayout.Width(100));
            ShowTree = GUILayout.Toggle(showTree, "Tree", GUI.skin.button, GUILayout.Width(48));
            GUILayout.FlexibleSpace();
            if (app.IsRunning)
            {
                var sim = app.Runner.Sim;
                GUILayout.Label(ClockText(sim) + "   " + aliveCount + "/" + sim.Agents.Count + " alive" + (app.Runner.Paused ? "   PAUSED" : ""), richLabel);
            }
            GUILayout.Space(10);
            if (GUILayout.Button("A-", GUILayout.Width(28))) userScale = Mathf.Max(0.75f, Scale - 0.25f);
            if (GUILayout.Button("A+", GUILayout.Width(28))) userScale = Mathf.Min(3f, Scale + 0.25f);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        static string ClockText(Simulation sim) =>
            "Day " + (sim.Day + 1) + "  " + sim.Hour.ToString("00") + ":00  " + Title(sim.Season.Id) +
            (sim.WeatherEndsTick > 0 ? "  " + Title(sim.Weather.Id) : "");

        void DrawMapLabels(float scale)
        {
            if (Event.current.type != EventType.Repaint || app.CameraRig.Size > 18f) return;
            var sim = app.Runner.Sim;
            for (int i = 0; i < sim.Agents.Count && i < app.Villagers.Count; i++)
            {
                if (!sim.Agents[i].Alive) continue;
                Vector2 screen = app.CameraRig.WorldToScreen(app.Villagers.PositionOf(i) + new Vector3(0f, 0.75f, 0f));
                var rect = new Rect(screen.x / scale - 30f, (Screen.height - screen.y) / scale - 16f, 60f, 16f);
                var old = GUI.color;
                GUI.color = Color.black;
                GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), "V" + (i + 1), mapLabel);
                GUI.color = i == app.SelectedAgent ? Color.yellow : Color.white;
                GUI.Label(rect, "V" + (i + 1), mapLabel);
                GUI.color = old;
            }
        }

        // ------------------------------------------------------------------ time

        void DrawTime(int id)
        {
            var runner = app.Runner;
            if (runner == null) return;
            Backdrop(timeRect);
            var sim = runner.Sim;
            int dayOfSeason = sim.Day % sim.Config.DaysPerSeason + 1;

            GUILayout.Label(ClockText(sim) + (sim.IsNight ? "  (night)" : "  (day)"), headerLabel);
            GUILayout.Label("Day " + dayOfSeason + " of " + sim.Config.DaysPerSeason + " in " + Title(sim.Season.Id) +
                            "     tick " + sim.Tick, smallLabel);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(runner.Paused ? "Resume" : "Pause")) runner.Paused = !runner.Paused;
            if (GUILayout.Button("Step one tick")) app.StepOnce();
            GUILayout.EndHorizontal();

            int current = -1;
            for (int i = 0; i < SpeedSteps.Length; i++)
                if (Mathf.Approximately(runner.TicksPerSecond, GameApp.NormalSpeed * SpeedSteps[i])) current = i;
            int chosen = GUILayout.Toolbar(current, SpeedLabels);
            if (chosen != current && chosen >= 0) app.SetSpeed(SpeedSteps[chosen]);

            GUILayout.Label(runner.Paused ? "Paused" : "Running at " + app.MeasuredTicksPerSecond.ToString("0") + " ticks per second", smallLabel);
            GUILayout.Label(sim.WeatherEndsTick > 0
                ? "Weather: " + Title(sim.Weather.Id) + " for another " + System.Math.Max(0, sim.WeatherEndsTick - sim.Tick) + " ticks"
                : "Weather: clear", smallLabel);
            string council = CouncilText(sim);
            if (council.Length > 0) GUILayout.Label("<color=#ffe080>" + council + "</color>", smallLabel);
            GUILayout.Label("P pause   . step   1-6 speed", smallLabel);
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        // ------------------------------------------------------------------ villagers (spectate)

        void DrawVillagers(int id)
        {
            var runner = app.Runner;
            if (runner == null) return;
            Backdrop(villagersRect);
            var sim = runner.Sim;

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", GUILayout.Width(28))) app.SelectNext(-1);
            if (GUILayout.Button(">", GUILayout.Width(28))) app.SelectNext(1);
            app.FollowSelected = GUILayout.Toggle(app.FollowSelected, "Follow", GUI.skin.button);
            bool control = GUILayout.Toggle(app.IsControlling, "Control", GUI.skin.button);
            if (control != app.IsControlling) app.SetControl(control);
            GUILayout.EndHorizontal();

            villagersScroll = GUILayout.BeginScrollView(villagersScroll, GUILayout.Height(Mathf.Min(260f, 8f + sim.Agents.Count * 23f)));
            for (int i = 0; i < sim.Agents.Count; i++)
            {
                var agent = sim.Agents[i];
                text.Length = 0;
                text.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(art.VillagerColor(i))).Append(">■</color> V").Append(i + 1);
                if (agent.Role != 0) text.Append("  ").Append(RoleName(agent.Role));
                text.Append("   ").Append(StatusText(runner, agent));
                if (agent.Alive) text.Append("   HP ").Append(Mathf.RoundToInt(agent.Get(Need.Hp)));

                var old = GUI.backgroundColor;
                if (i == app.SelectedAgent) GUI.backgroundColor = new Color(1f, 0.9f, 0.3f);
                if (GUILayout.Button(text.ToString(), rowButton))
                {
                    app.Select(i);
                    app.FollowSelected = true;
                }
                GUI.backgroundColor = old;
            }
            GUILayout.EndScrollView();
            GUILayout.Label("Click a villager here or on the map. Tab next, F follow, C control.", smallLabel);
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        string StatusText(SimRunner runner, Agent agent)
        {
            if (!agent.Alive) return "dead";
            if (runner.ControlledAgent == agent.Index) return "you";
            if (agent.Sleeping) return "asleep";
            return ActionText(runner.Sim, agent.LastAction);
        }

        string ActionText(Simulation sim, AgentAction action)
        {
            var actions = sim.Actions;
            int id = action.Id;
            if (id == actions.MoveNorth || id == actions.MoveSouth || id == actions.MoveEast || id == actions.MoveWest) return "walking";
            if (id == actions.Harvest) return "gathering";
            if (id == actions.Sleep) return "sleeping";
            if (id == actions.Consume)
            {
                if (action.Arg == 0) return "drinking";
                var item = sim.Data.ItemByInt(action.Arg);
                return item == null ? "eating" : (item.Hunger > 0f ? "eating " : "using ") + item.Name;
            }
            if (id == actions.Equip) return "changing gear";
            if (id == actions.CraftBuild && (uint)action.Arg < (uint)argNames.Length) return argDoing[action.Arg] + argNames[action.Arg];
            if (id == actions.Vote) return "voting";
            if (id == actions.Deposit) return "putting away";
            if (id == actions.Take) return "taking out";
            return "idle";
        }

        // ------------------------------------------------------------------ selected villager

        void DrawSelected(int id)
        {
            var runner = app.Runner;
            if (runner == null) return;
            Backdrop(selectedRect);
            var sim = runner.Sim;
            int index = app.SelectedAgent;
            if (index < 0 || index >= sim.Agents.Count)
            {
                GUILayout.Label("Nobody selected.\nClick a villager on the map or in the Villagers window.", wrapLabel);
                GUI.DragWindow(new Rect(0, 0, 10000, 20));
                return;
            }

            var agent = sim.Agents[index];
            bool controlled = runner.ControlledAgent == index;
            var data = sim.Data;

            GUILayout.Label("<color=#" + ColorUtility.ToHtmlStringRGB(art.VillagerColor(index)) + ">■</color> Villager " + (index + 1) +
                            "  -  " + StatusText(runner, agent), richLabel);
            GUILayout.Label("At " + agent.X + ", " + agent.Y + " facing " + FacingText(agent) + "     driven by " +
                            (controlled ? "you" : BrainText(runner.GetBrain(index))), smallLabel);
            GUILayout.Label("Job: " + RoleName(agent.Role), smallLabel);
            string sickWith = SickText(sim, agent);
            if (sickWith.Length > 0) GUILayout.Label("<color=#ff9060>Sick: " + sickWith + "</color>", smallLabel);
            if (agent.Alive && !agent.LastActionValid) GUILayout.Label("<color=#ff8080>Last action could not be done.</color>", smallLabel);
            var faced = agent.Alive ? sim.BuildingAt(agent.X + agent.FaceDx, agent.Y + agent.FaceDy) : null;
            if (agent.Alive)
            {
                GUILayout.Label("In front: " + InFrontText(sim, agent) + (agent.Home >= 0 ? "     Home: " + sim.Buildings[agent.Home].Def.Name : ""), smallLabel);
                if (agent.CraftArg != 0 && (uint)agent.CraftArg < (uint)argNames.Length)
                    GUILayout.Label("Making " + argNames[agent.CraftArg] + "...", smallLabel);
            }

            for (int need = 0; need < NeedLabels.Length; need++)
                Bar(NeedLabels[need], agent.Needs[need] / sim.Config.NeedMax, NeedColors[need], Mathf.RoundToInt(agent.Needs[need]).ToString());

            // Everything between the needs and the buttons scrolls when the window would not fit on the screen.
            float room = Mathf.Max(120f, viewHeight - selectedRect.y - SelectedFixedHeight);
            selectedScroll = GUILayout.BeginScrollView(selectedScroll, GUILayout.Height(Mathf.Min(selectedBodyHeight, room)));
            GUILayout.BeginVertical();

            var stats = agent.Stats;
            GUILayout.Label("STR " + stats.Str + "   DEX " + stats.Dex + "   INT " + stats.Int + "   END " + stats.End, smallLabel);
            GUILayout.Label("Influence " + stats.Influence.ToString("0.00") + "   Empathy " + stats.Empathy.ToString("0.00") +
                            "\nSociability " + stats.Sociability.ToString("0.00") + "   Aggression " + stats.Aggression.ToString("0.00"), smallLabel);

            GUILayout.Label("Wearing", headerLabel);
            bool wearsAnything = false;
            for (int slot = 0; slot < agent.Equipped.Length; slot++)
            {
                var stack = agent.Equipped[slot];
                if (stack.IsEmpty) continue;
                wearsAnything = true;
                GUILayout.BeginHorizontal();
                GUILayout.Label(SlotLabels[slot] + ": " + StackText(data, stack), smallLabel);
                if (controlled && slot == (int)EquipSlot.Hand)
                    ActionButton(runner, index, "Put away", new AgentAction(sim.Actions.Equip, 0));
                GUILayout.EndHorizontal();
            }
            if (!wearsAnything) GUILayout.Label("Nothing", smallLabel);

            int used = agent.InventoryCapacity - agent.FreeSlots;
            GUILayout.Label("Pack  (" + used + " of " + agent.InventoryCapacity + " slots)", headerLabel);
            for (int slot = 0; slot < agent.InventoryCapacity; slot++)
            {
                var stack = agent.Inventory[slot];
                GUILayout.BeginHorizontal();
                GUILayout.Label(stack.IsEmpty ? "-" : StackText(data, stack), smallLabel);
                if (controlled && !stack.IsEmpty)
                {
                    var item = data.ItemByInt(stack.Item);
                    if (item != null && item.Consumable != 0)
                        ActionButton(runner, index, item.Hunger > 0f ? "Eat" : "Use", new AgentAction(sim.Actions.Consume, stack.Item), SmallButton);
                    else if (data.EquipmentByItemInt(stack.Item) != null)
                        ActionButton(runner, index, "Equip", new AgentAction(sim.Actions.Equip, stack.Item), SmallButton);
                    // Into the chest or onto the fire in front; onto the ground when nothing is there.
                    string put = faced == null ? "Drop" : faced.Def.EffectKey == WarmthEffect ? "Burn" : "Store";
                    ActionButton(runner, index, put, new AgentAction(sim.Actions.Deposit, stack.Item), SmallButton);
                }
                GUILayout.EndHorizontal();
            }

            if (controlled && faced != null && faced.Complete && faced.Storage != null && sim.MayUseStorage(agent, faced))
            {
                GUILayout.Label("In the " + faced.Def.Name, headerLabel);
                bool anything = false;
                for (int slot = 0; slot < faced.Storage.Length; slot++)
                {
                    var stack = faced.Storage[slot];
                    if (stack.Qty <= 0) continue;
                    // One row per kind of item: taking moves a whole stack anyway.
                    bool listed = false;
                    int total = 0;
                    for (int other = 0; other < faced.Storage.Length; other++)
                    {
                        if (faced.Storage[other].Qty <= 0 || faced.Storage[other].Item != stack.Item) continue;
                        if (other < slot) listed = true;
                        total += faced.Storage[other].Qty;
                    }
                    if (listed) continue;
                    anything = true;
                    GUILayout.BeginHorizontal();
                    GUILayout.Label((data.ItemByInt(stack.Item)?.Name ?? "item " + stack.Item) + "  x" + total, smallLabel);
                    ActionButton(runner, index, "Take", new AgentAction(sim.Actions.Take, stack.Item), SmallButton);
                    GUILayout.EndHorizontal();
                }
                if (!anything) GUILayout.Label("Empty", smallLabel);
            }

            if (controlled) DrawVoteButtons(runner, index);

            if (controlled)
            {
                GUILayout.Space(4);
                showMake = GUILayout.Toggle(showMake, "Make and build", GUI.skin.button);
                if (showMake) DrawMakeList(runner, index);
                GUILayout.Label("WASD or arrows walk and turn.  Space gathers or hits.  Q drinks.  Z sleeps.  Esc hands control back.", wrapLabel);
            }

            GUILayout.EndVertical();
            if (Event.current.type == EventType.Repaint) selectedBodyHeight = GUILayoutUtility.GetLastRect().height + 6f;
            GUILayout.EndScrollView();

            GUILayout.Space(4);
            if (controlled)
            {
                GUILayout.BeginHorizontal();
                ActionButton(runner, index, "Gather / hit", new AgentAction(sim.Actions.Harvest));
                ActionButton(runner, index, "Drink", new AgentAction(sim.Actions.Consume, 0));
                ActionButton(runner, index, "Sleep", new AgentAction(sim.Actions.Sleep), hold: true);
                GUILayout.EndHorizontal();
                if (GUILayout.Button("Hand control back")) app.SetControl(false);
            }
            else if (agent.Alive)
            {
                if (GUILayout.Button("Take control  (C)")) app.SetControl(true);
            }
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        string SickText(Simulation sim, Agent agent)
        {
            text.Length = 0;
            for (int i = 0; i < agent.SickTicks.Length && i < sim.Data.Sicknesses.Count; i++)
            {
                if (agent.SickTicks[i] == 0) continue;
                if (text.Length > 0) text.Append(", ");
                text.Append(Title(sim.Data.Sicknesses[i].Id));
            }
            return text.ToString();
        }

        string RoleName(int role) => (uint)role < (uint)roleNames.Length && roleNames[role] != null ? roleNames[role] : "job " + role;

        /// <summary>What the Council is voting on right now, or an empty string.</summary>
        string CouncilText(Simulation sim)
        {
            var vote = sim.OpenVote;
            if (vote == null) return "";
            long left = System.Math.Max(0, vote.ClosesTick - sim.Tick);
            if (vote.Kind == VoteKind.Role)
                return (vote.Review ? "Council: reviewing the job of V" : "Council: voting on a job for V") + (vote.Subject + 1) + "  (" + left + " ticks left)";
            if (vote.Kind == VoteKind.Research) return "Council: voting on what to research  (" + left + " ticks left)";
            return "Council: a vote is open  (" + left + " ticks left)";
        }

        /// <summary>The options of the open vote as buttons, while the player's villager has not voted yet.</summary>
        void DrawVoteButtons(SimRunner runner, int agentIndex)
        {
            var sim = runner.Sim;
            var vote = sim.OpenVote;
            if (vote == null) return;
            GUILayout.Space(4);
            GUILayout.Label(vote.Kind == VoteKind.Role ? (vote.Review ? "Council review: the job of Villager " : "Council: a job for Villager ") + (vote.Subject + 1)
                : vote.Kind == VoteKind.Research ? "Council: what to research" : "Council vote", headerLabel);
            if (vote.HasVoted(agentIndex))
            {
                GUILayout.Label("You have voted. The others have " + System.Math.Max(0, vote.ClosesTick - sim.Tick) + " ticks left.", smallLabel);
                return;
            }
            // Three to a row.
            int inRow = 0;
            int options = Mathf.Max(roleNames.Length, sim.Data.Tech.Count);
            for (int option = 1; option < options; option++)
            {
                var action = new AgentAction(sim.Actions.Vote, option);
                if (!sim.IsValid(agentIndex, action)) continue;
                string label = vote.Kind == VoteKind.Research ? Title(sim.Data.Tech[option].Id) : RoleName(option);
                if (inRow == 0) GUILayout.BeginHorizontal();
                if (GUILayout.Button(label, rowButton, GUILayout.Width(84))) runner.Manual.Queue(action);
                if (++inRow == 3)
                {
                    GUILayout.EndHorizontal();
                    inRow = 0;
                }
            }
            if (inRow != 0) GUILayout.EndHorizontal();
        }

        /// <summary>Everything the villager could start making or building right now, each as a button that keeps at it until done.</summary>
        void DrawMakeList(SimRunner runner, int agentIndex)
        {
            var sim = runner.Sim;
            if (craftMaskFrame != Time.frameCount || craftMaskAgent != agentIndex)
            {
                sim.GetArgMask(agentIndex, sim.Actions.CraftBuild, craftMask);
                craftMaskFrame = Time.frameCount;
                craftMaskAgent = agentIndex;
            }
            int possible = 0;
            for (int arg = 1; arg < craftMask.Length; arg++)
                if (craftMask[arg]) possible++;
            if (possible == 0)
            {
                GUILayout.Label("Nothing can be made or built here with what is in the pack. A building goes on the free tile in front.", wrapLabel);
                return;
            }

            for (int arg = 1; arg < craftMask.Length; arg++)
            {
                if (!craftMask[arg]) continue;
                if (GUILayout.Button(argVerb[arg] + argNames[arg], rowButton))
                    runner.Manual.Hold(new AgentAction(sim.Actions.CraftBuild, arg));
            }
        }

        /// <summary>What an action of the villager would act on.</summary>
        string InFrontText(Simulation sim, Agent agent)
        {
            int x = agent.X + agent.FaceDx, y = agent.Y + agent.FaceDy;
            var world = sim.World;
            var data = sim.Data;
            if (!world.InBounds(x, y)) return "the edge of the map";

            var building = sim.BuildingAt(x, y);
            if (building != null)
            {
                if (!building.Complete)
                    return building.Def.Name + ", " + Mathf.FloorToInt(100f * building.Progress / Mathf.Max(1, building.Def.BuildTicks)) + "% built";
                return building.Def.EffectKey == WarmthEffect ? building.Def.Name + (building.IsLit ? " (lit)" : " (out)") : building.Def.Name;
            }
            var animal = sim.AnimalAt(x, y);
            if (animal != null) return animal.Def.Name + " (" + Mathf.CeilToInt(animal.Hp) + " health)";
            int villager = world.AgentAt(x, y);
            if (villager >= 0) return "Villager " + (villager + 1);

            int node = world.NodeAt(x, y);
            if (node != 0)
            {
                var pile = sim.PileAt(x, y);
                if (pile.Count > 0)
                {
                    text.Length = 0;
                    text.Append("on the ground: ");
                    for (int i = 0; i < pile.Count && i < 3; i++) text.Append(i > 0 ? ", " : "").Append(StackText(data, pile[i]));
                    if (pile.Count > 3) text.Append(" and more");
                    return text.ToString();
                }
                return (data.NodeByInt(node)?.Name ?? "something") + (world.NodeChargesAt(x, y) == 0 ? " (used up)" : "");
            }
            var terrain = data.TerrainByInt(world.TerrainAt(x, y));
            return terrain == null || terrain.Walkable != 0 ? "open ground" : Title(terrain.Id);
        }

        static string FacingText(Agent agent) =>
            agent.FaceDx > 0 ? "east" : agent.FaceDx < 0 ? "west" : agent.FaceDy > 0 ? "north" : "south";

        static string BrainText(IBrain brain) =>
            brain is SurvivalBrain ? "the survival bot" : brain is RandomBrain ? "random choices" : brain is IdleBrain ? "nobody" : "you";

        static string StackText(GameData data, ItemStack stack)
        {
            var item = data.ItemByInt(stack.Item);
            string name = item != null ? item.Name : "item " + stack.Item;
            var equipment = data.EquipmentByItemInt(stack.Item);
            if (equipment != null && equipment.Durability > 0) return name + "  (" + stack.Durability + "/" + equipment.Durability + ")";
            return stack.Qty > 1 ? name + "  x" + stack.Qty : name;
        }

        const float WideButton = 86f, SmallButton = 50f;

        /// <summary>
        /// A button that gives the player's villager an action, greyed out when the action is not possible now.
        /// With hold, the villager keeps at it until told something else.
        /// </summary>
        void ActionButton(SimRunner runner, int agentIndex, string label, AgentAction action, float width = WideButton, bool hold = false)
        {
            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && runner.Sim.IsValid(agentIndex, action);
            if (GUILayout.Button(label, GUILayout.Width(width)))
            {
                if (hold) runner.Manual.Hold(action);
                else runner.Manual.Queue(action);
            }
            GUI.enabled = wasEnabled;
        }

        void Bar(string label, float fraction, Color color, string value)
        {
            var rect = GUILayoutUtility.GetRect(100f, 17f, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                var old = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(rect, Texture2D.whiteTexture);
                GUI.color = color;
                GUI.DrawTexture(new Rect(rect.x + 1, rect.y + 1, (rect.width - 2) * Mathf.Clamp01(fraction), rect.height - 2), Texture2D.whiteTexture);
                GUI.color = old;
            }
            GUI.Label(rect, label + "  " + value, barLabel);
            GUILayout.Space(1);
        }

        // ------------------------------------------------------------------ resources

        void DrawResources(int id)
        {
            var runner = app.Runner;
            if (runner == null) return;
            Backdrop(resourcesRect);
            var data = runner.Sim.Data;

            GUILayout.Label("Villagers: " + aliveCount + " alive of " + runner.Sim.Agents.Count, smallLabel);
            resourcesScroll = GUILayout.BeginScrollView(resourcesScroll, GUILayout.Height(300));

            GUILayout.Label("Carried by the village", headerLabel);
            bool anyItem = false;
            for (int i = 1; i < itemTotals.Length; i++)
            {
                if (itemTotals[i] <= 0) continue;
                anyItem = true;
                Row(data.ItemByInt(i)?.Name ?? ("item " + i), itemTotals[i]);
            }
            if (!anyItem) GUILayout.Label("Nothing yet", smallLabel);

            GUILayout.Space(4);
            GUILayout.Label("In storage", headerLabel);
            bool anyStored = false;
            for (int i = 1; i < storedTotals.Length; i++)
            {
                if (storedTotals[i] <= 0) continue;
                anyStored = true;
                Row(data.ItemByInt(i)?.Name ?? ("item " + i), storedTotals[i]);
            }
            if (!anyStored) GUILayout.Label("Nothing yet", smallLabel);

            GUILayout.Space(4);
            GUILayout.Label("Buildings", headerLabel);
            bool anyBuilding = false;
            foreach (var building in data.Buildings)
            {
                if ((uint)building.Int >= (uint)buildingTotals.Length) continue;
                if (buildingTotals[building.Int] > 0) Row(building.Name, buildingTotals[building.Int]);
                if (siteTotals[building.Int] > 0) Row(building.Name + " (being built)", siteTotals[building.Int]);
                anyBuilding |= buildingTotals[building.Int] > 0 || siteTotals[building.Int] > 0;
            }
            if (!anyBuilding) GUILayout.Label("None yet", smallLabel);
            if (fireText.Length > 0) GUILayout.Label(fireText, smallLabel);

            GUILayout.Space(4);
            GUILayout.Label("Knowledge", headerLabel);
            var sim = runner.Sim;
            text.Length = 0;
            foreach (var tech in data.Tech)
                if (sim.IsResearched(tech.Id)) text.Append(text.Length > 0 ? ", " : "").Append(Title(tech.Id));
            GUILayout.Label("Known: " + text, smallLabel);
            if (sim.ResearchingTech != null)
                GUILayout.Label("Studying " + Title(sim.ResearchingTech.Id) + ": " + sim.ResearchPoints.ToString("0") + " of " + sim.ResearchingTech.RpCost.ToString("0"), smallLabel);

            GUILayout.Space(4);
            GUILayout.Label("Animals", headerLabel);
            foreach (var animal in data.Animals)
                if ((uint)animal.Int < (uint)animalTotals.Length) Row(animal.Name, animalTotals[animal.Int]);

            GUILayout.Space(4);
            GUILayout.Label("Left on the map", headerLabel);
            for (int i = 1; i < nodeTotals.Length; i++)
            {
                if (nodeTotals[i] <= 0) continue;
                Row(data.NodeByInt(i)?.Name ?? ("node " + i), nodeTotals[i]);
            }
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        void Row(string label, int amount)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, smallLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label(amount.ToString(), smallLabel);
            GUILayout.EndHorizontal();
        }
    }
}
