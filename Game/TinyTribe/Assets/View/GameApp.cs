using System.Collections.Generic;
using System.IO;
using TinyTribe.Brains;
using TinyTribe.Neural;
using TinyTribe.Sim.Core;
using TinyTribe.Sim.Data;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;

namespace TinyTribe.View
{
    /// <summary>Creates the game object when Play starts, so no scene setup is needed.</summary>
    static class GameBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<GameApp>() == null)
                new GameObject("TinyTribe").AddComponent<GameApp>();
        }
    }

    /// <summary>
    /// The running game: loads the data, shows the launch screen, and while a game is running
    /// advances the simulation each frame and keeps the map, the villagers, the camera and the
    /// windows in step with it. Everything on screen is built from code.
    /// </summary>
    public sealed class GameApp : MonoBehaviour
    {
        /// <summary>Ticks per second at "1x" speed.</summary>
        public const float NormalSpeed = 5f;

        /// <summary>Colour laid over the whole scene at night, at up to MaxDarkness opacity.</summary>
        static readonly Color NightColor = new Color(0.04f, 0.07f, 0.24f);
        const float MaxDarkness = 0.5f;

        /// <summary>Sorting order of the night shade. Anything drawn above it stays bright in the dark.</summary>
        public const int NightOrder = 30000;

        public static GameApp Instance { get; private set; }

        public GameData Data { get; private set; }

        /// <summary>The current game, or null on the launch screen.</summary>
        public SimRunner Runner { get; private set; }

        public bool IsRunning => Runner != null;

        /// <summary>Index of the villager being watched, or -1.</summary>
        public int SelectedAgent { get; private set; } = -1;

        public bool FollowSelected = true;
        public WorldView World { get; private set; }
        public AgentLayer Villagers { get; private set; }
        public BuildingLayer Buildings { get; private set; }
        public AnimalLayer Animals { get; private set; }
        public Hud Hud { get; private set; }
        public CameraRig CameraRig { get; private set; }

        /// <summary>The setup of the current game, also used to pre-fill the launch screen.</summary>
        public GameSetup Setup { get; private set; } = new GameSetup();

        /// <summary>Simulation speed actually reached, averaged over the last half second.</summary>
        public float MeasuredTicksPerSecond { get; private set; }

        /// <summary>The pictures in use: the art pack when installed, otherwise the generated ones.</summary>
        public IArt Art => art;

        /// <summary>How dark the scene is drawn, 0 by day to 1 in the middle of the night.</summary>
        public float Darkness { get; private set; }

        PixelAtlas atlas;
        IArt art;
        Material spriteMaterial;
        SpriteRenderer night;
        float measureTime;
        int measureTicks;

        void Awake()
        {
            Instance = this;
            // A simulation should keep running while its window is behind others.
            Application.runInBackground = true;
            Data = GameData.Load(ReadDataFile);
            foreach (string problem in Data.Validate()) Debug.LogError("Game data: " + problem);

            // The art pack if it is installed (tools/install_art.py), otherwise pictures drawn in code.
            atlas = new PixelAtlas();
            var placeholder = new PlaceholderArt(atlas);
            art = (IArt)PackArt.TryLoad(atlas, placeholder) ?? placeholder;
            atlas.Apply();
            var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader != null) spriteMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };

            CameraRig = new CameraRig();
            night = CreateNightOverlay();
            Hud = gameObject.AddComponent<Hud>();
            Hud.Init(this, art);
        }

        /// <summary>A plain square stretched over the camera view and drawn above everything in the world.</summary>
        SpriteRenderer CreateNightOverlay()
        {
            var overlay = new GameObject("Night");
            overlay.transform.SetParent(transform, false);
            var overlayRenderer = overlay.AddComponent<SpriteRenderer>();
            var white = Texture2D.whiteTexture;
            overlayRenderer.sprite = Sprite.Create(white, new Rect(0, 0, white.width, white.height), new Vector2(0.5f, 0.5f), white.width);
            overlayRenderer.sprite.hideFlags = HideFlags.HideAndDontSave;
            overlayRenderer.sortingOrder = NightOrder;
            if (spriteMaterial != null) overlayRenderer.sharedMaterial = spriteMaterial;
            overlayRenderer.enabled = false;
            return overlayRenderer;
        }

        void DrawNight(float deltaTime)
        {
            float target = IsRunning && Runner.Sim.IsNight ? 1f : 0f;
            Darkness = Mathf.MoveTowards(Darkness, target, deltaTime * 1.5f);
            night.enabled = IsRunning && Darkness > 0.001f;
            if (!night.enabled) return;
            var view = CameraRig.Camera;
            float height = view.orthographicSize * 2f + 2f;
            night.transform.position = new Vector3(view.transform.position.x, view.transform.position.y, 0f);
            night.transform.localScale = new Vector3(height * Mathf.Max(1f, view.aspect), height, 1f);
            night.color = new Color(NightColor.r, NightColor.g, NightColor.b, Darkness * MaxDarkness);
        }

        /// <summary>Where runs are recorded: beside the project in the editor, in the app's own data folder in a build.</summary>
        public string RunsFolder
        {
            get => runsFolder ?? (runsFolder = Application.isEditor
                ? Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Runs")
                : Path.Combine(Application.persistentDataPath, "Runs"));
            set => runsFolder = value;
        }

        string runsFolder;
        NeuralBrain neural;

        /// <summary>The trained network playing this game, or null.</summary>
        public NeuralBrain Neural => neural;

        /// <summary>The recording of the game being played, or of the one just ended; null when it is not recorded.</summary>
        public RunRecorder Recorder { get; private set; }

        /// <summary>Counters and achievements of the game being played.</summary>
        public RunStats Stats { get; private set; }

        /// <summary>What is worth knowing later about where a run was played. Nothing that names the person or the machine.</summary>
        static Dictionary<string, string> HostInfo() => new Dictionary<string, string>
        {
            ["app_version"] = Application.version,
            ["unity"] = Application.unityVersion,
            ["platform"] = Application.platform.ToString(),
            ["editor"] = Application.isEditor ? "true" : "false",
            ["os"] = SystemInfo.operatingSystem,
            ["cpu"] = SystemInfo.processorType,
            ["cores"] = SystemInfo.processorCount.ToString(),
        };

        void FinishRecording(string reason)
        {
            if (Recorder == null || Recorder.Finished) return;
            try
            {
                Recorder.Finish(reason);
            }
            catch (System.Exception problem)
            {
                Debug.LogWarning("The run could not be saved: " + problem.Message);
            }
        }

        void OnApplicationQuit() => FinishRecording("quit");

        void OnDestroy()
        {
            FinishRecording("quit");
            neural?.Dispose();
            if (Instance == this) Instance = null;
            atlas?.Dispose();
            if (spriteMaterial != null) Destroy(spriteMaterial);
        }

        /// <summary>
        /// Reads one exported data file from StreamingAssets/Data. On Android those files sit inside
        /// the APK and have to be fetched through a web request; everywhere else they are plain files.
        /// </summary>
        static string ReadDataFile(string fileName)
        {
            string path = Path.Combine(Application.streamingAssetsPath, "Data", fileName);
            if (!path.Contains("://"))
            {
                if (!File.Exists(path)) throw new FileNotFoundException("Missing data file. Run ML/export_data.py.", path);
                return File.ReadAllText(path);
            }
            using (var request = UnityWebRequest.Get(path))
            {
                var operation = request.SendWebRequest();
                while (!operation.isDone) { }
                if (request.result != UnityWebRequest.Result.Success)
                    throw new FileNotFoundException("Missing data file: " + request.error, path);
                return request.downloadHandler.text;
            }
        }

        // ------------------------------------------------------------------ starting and stopping

        public void StartGame(GameSetup setup)
        {
            if (IsRunning) StopGame();
            Setup = setup;
            neural = setup.Brains == BrainKind.Neural ? NeuralBrain.Load(setup.Model, setup.Sim.Seed, setup.Greedy) : null;
            Runner = SimRunner.Create(Data, setup, neural);
            var sim = Runner.Sim;
            Recorder = null;
            if (setup.Record)
            {
                try
                {
                    // A run played by a network is named after the experiment that trained it.
                    Recorder = RunRecorder.Start(RunsFolder, Data, sim, setup, HostInfo(), neural != null ? neural.Info.Experiment ?? neural.Info.Name : null);
                    Runner.Recorder = Recorder;
                }
                catch (System.Exception problem)
                {
                    // A full disk or a folder that cannot be written is no reason not to play.
                    Debug.LogWarning("This run is not being recorded: " + problem.Message);
                    Recorder = null;
                }
            }
            Stats = Recorder != null ? Recorder.Stats : (Runner.Stats = new RunStats(Data, sim));

            World = new GameObject("World").AddComponent<WorldView>();
            World.transform.SetParent(transform, false);
            World.Build(sim, art, spriteMaterial);

            Buildings = new GameObject("Buildings").AddComponent<BuildingLayer>();
            Buildings.transform.SetParent(transform, false);
            Buildings.Build(sim, art, spriteMaterial, NightOrder + 1);

            Animals = new GameObject("Animals").AddComponent<AnimalLayer>();
            Animals.transform.SetParent(transform, false);
            Animals.Build(sim, art, spriteMaterial);

            Villagers = new GameObject("Villagers").AddComponent<AgentLayer>();
            Villagers.transform.SetParent(transform, false);
            Villagers.Build(sim, art, spriteMaterial);

            atlas.Apply();
            CameraRig.Frame(sim.World);
            SelectedAgent = -1;
            Darkness = 0f;
            MeasuredTicksPerSecond = 0f;
            measureTime = 0f;
            measureTicks = 0;
        }

        public void StopGame()
        {
            if (!IsRunning) return;
            FinishRecording("stopped");
            neural?.Dispose();
            neural = null;
            Runner = null;
            SelectedAgent = -1;
            Darkness = 0f;
            night.enabled = false;
            if (World != null) Destroy(World.gameObject);
            if (Villagers != null) Destroy(Villagers.gameObject);
            if (Buildings != null) Destroy(Buildings.gameObject);
            if (Animals != null) Destroy(Animals.gameObject);
            World = null;
            Villagers = null;
            Buildings = null;
            Animals = null;
        }

        /// <summary>Runs exactly one tick, also while paused, and brings the map up to date with it.</summary>
        public void StepOnce()
        {
            if (!IsRunning) return;
            Runner.StepOnce();
            ApplyEvents();
        }

        void ApplyEvents()
        {
            if (Runner.FrameEventsDropped) World.RefreshAllNodes();
            else World.Apply(Runner.FrameEvents);
        }

        // ------------------------------------------------------------------ selection and control

        /// <summary>Watches a villager. An index that does not exist clears the selection.</summary>
        public void Select(int agentIndex)
        {
            if (!IsRunning) return;
            if (agentIndex < 0 || agentIndex >= Runner.Sim.Agents.Count) agentIndex = -1;
            if (agentIndex == SelectedAgent) return;
            Runner.ReleaseControl();
            SelectedAgent = agentIndex;
        }

        public void SelectNext(int step)
        {
            if (!IsRunning) return;
            int count = Runner.Sim.Agents.Count;
            if (count == 0) return;
            int start = SelectedAgent < 0 ? (step > 0 ? -1 : 0) : SelectedAgent;
            Select(((start + step) % count + count) % count);
        }

        /// <summary>Hands the selected villager to the player, or back to its own brain.</summary>
        public void SetControl(bool on)
        {
            if (!IsRunning) return;
            if (on && SelectedAgent >= 0 && Runner.Sim.Agents[SelectedAgent].Alive) Runner.TakeControl(SelectedAgent);
            else Runner.ReleaseControl();
        }

        public bool IsControlling => IsRunning && Runner.ControlledAgent >= 0;

        // ------------------------------------------------------------------ frame

        void Update()
        {
            if (!IsRunning) return;
            float deltaTime = Time.unscaledDeltaTime;
            var sim = Runner.Sim;

            ReadKeyboard(deltaTime);
            ReadMouse();

            int ran = Runner.Advance(deltaTime);
            measureTime += deltaTime;
            measureTicks += ran;
            if (measureTime >= 0.5f)
            {
                MeasuredTicksPerSecond = measureTicks / measureTime;
                measureTime = 0f;
                measureTicks = 0;
            }

            if (Runner.ControlledAgent >= 0 && !sim.Agents[Runner.ControlledAgent].Alive) Runner.ReleaseControl();
            // An empty village is the end of the run, though the world goes on.
            if (ran > 0 && Recorder != null && !Recorder.Finished && Stats.Get("alive") == 0) FinishRecording("everyone_dead");

            if (ran > 0) ApplyEvents();
            World.RevealBehind(sim.Agents);
            Buildings.Sync(deltaTime);
            Animals.Sync(Runner, deltaTime);
            Villagers.Sync(Runner, SelectedAgent, deltaTime);
            atlas.Apply();

            if (SelectedAgent >= 0 && (FollowSelected || IsControlling))
                CameraRig.Follow(Villagers.PositionOf(SelectedAgent), deltaTime);
            DrawNight(deltaTime);
        }

        void ReadKeyboard(float deltaTime)
        {
            var keys = Keyboard.current;
            if (keys == null) return;

            if (keys.pKey.wasPressedThisFrame || (!IsControlling && keys.spaceKey.wasPressedThisFrame)) Runner.Paused = !Runner.Paused;
            if (keys.periodKey.wasPressedThisFrame) StepOnce();
            if (keys.tabKey.wasPressedThisFrame) SelectNext(keys.shiftKey.isPressed ? -1 : 1);
            if (keys.fKey.wasPressedThisFrame) FollowSelected = !FollowSelected;
            if (keys.cKey.wasPressedThisFrame) SetControl(!IsControlling);
            if (keys.escapeKey.wasPressedThisFrame) SetControl(false);
            for (int i = 0; i < Hud.SpeedSteps.Length; i++)
                if (keys[Key.Digit1 + i].wasPressedThisFrame) SetSpeed(Hud.SpeedSteps[i]);

            var direction = Vector2.zero;
            if (keys.wKey.isPressed || keys.upArrowKey.isPressed) direction.y += 1f;
            if (keys.sKey.isPressed || keys.downArrowKey.isPressed) direction.y -= 1f;
            if (keys.dKey.isPressed || keys.rightArrowKey.isPressed) direction.x += 1f;
            if (keys.aKey.isPressed || keys.leftArrowKey.isPressed) direction.x -= 1f;

            if (!IsControlling)
            {
                if (direction != Vector2.zero)
                {
                    FollowSelected = false;
                    CameraRig.Pan(direction.normalized, deltaTime);
                }
                return;
            }

            // The player's villager: held keys repeat every tick.
            var actions = Runner.Sim.Actions;
            if (direction.y > 0f) Runner.Manual.Queue(new AgentAction(actions.MoveNorth));
            else if (direction.y < 0f) Runner.Manual.Queue(new AgentAction(actions.MoveSouth));
            else if (direction.x > 0f) Runner.Manual.Queue(new AgentAction(actions.MoveEast));
            else if (direction.x < 0f) Runner.Manual.Queue(new AgentAction(actions.MoveWest));
            else if (keys.spaceKey.isPressed || keys.eKey.isPressed) Runner.Manual.Queue(new AgentAction(actions.Harvest));
            else if (keys.zKey.isPressed) Runner.Manual.Queue(new AgentAction(actions.Sleep));
            else if (keys.qKey.wasPressedThisFrame) Runner.Manual.Queue(new AgentAction(actions.Consume, 0));
        }

        void ReadMouse()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            Vector2 position = mouse.position.ReadValue();
            if (Hud.IsPointerOverWindow(position)) return;

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f) CameraRig.Zoom(Mathf.Sign(scroll));

            if (mouse.rightButton.isPressed || mouse.middleButton.isPressed)
            {
                FollowSelected = false;
                CameraRig.Drag(mouse.delta.ReadValue());
            }

            if (mouse.leftButton.wasPressedThisFrame)
            {
                int hit = Villagers.Nearest(CameraRig.ScreenToWorld(position), 0.8f);
                if (hit >= 0)
                {
                    Select(hit);
                    FollowSelected = true;
                }
            }
        }

        /// <summary>Sets the speed in multiples of normal speed. Zero pauses.</summary>
        public void SetSpeed(float multiple)
        {
            if (!IsRunning) return;
            Runner.Paused = multiple <= 0f;
            if (multiple > 0f) Runner.TicksPerSecond = NormalSpeed * multiple;
        }
    }
}
