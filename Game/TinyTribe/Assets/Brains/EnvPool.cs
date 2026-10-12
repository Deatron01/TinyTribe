using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TinyTribe.Sim.Core;
using TinyTribe.Sim.Data;

namespace TinyTribe.Brains
{
    /// <summary>How a pool of villages is set up. The fields a trainer sends are the ones it wants to change.</summary>
    public sealed class EnvOptions
    {
        public int Envs = 8;

        /// <summary>A village ends after this many ticks, or when nobody is left.</summary>
        public int MaxTicks = 6720;

        /// <summary>The first village's seed; every village after it takes the next one.</summary>
        public ulong Seed = 1;

        /// <summary>
        /// Cut the very first village of each place short, each by a different amount, so that from then
        /// on the villages are out of step: at any moment some are in spring and some in winter. A trainer
        /// whose villages all start together would see nothing but spring for a while, then nothing but
        /// summer. The villages cut short end with the reason "staggered".
        /// </summary>
        public bool Stagger;
        public int Agents = -1;

        /// <summary>
        /// Places per village in the pool's tables: the villagers it starts with and the children born
        /// to them, living or dead. No child is born once they are all taken. 0: as many as it starts
        /// with, so no children.
        /// </summary>
        public int MaxAgents;
        public int MapWidth, MapHeight;
        public string StartSeason;
        public bool Weather = true, Sickness, JobReviews = true;

        /// <summary>Negative: as the workbook says.</summary>
        public float RewardShapingScale = -1f;

        /// <summary>Villages stepped at once. 0: one per processor.</summary>
        public int Threads;

        /// <summary>Record every village into this folder like any other run (rule R43); null: do not.</summary>
        public string RecordFolder;

        /// <summary>What run.json says played: the name of the policy.</summary>
        public string BrainsLabel = "policy";

        /// <summary>
        /// Also say, every tick, what the scripted survival bot would do in each villager's place. The
        /// trainer plays its own choices and learns from the bot's: correction where the learner really
        /// goes, not only where the bot would have gone.
        /// </summary>
        public bool Expert;

        /// <summary>
        /// Whether that bot keeps its plans whatever the villager does, as when it plays itself. Off
        /// (SurvivalBrain.Memoryless), a plan lasts only while the villager does as advised; when it
        /// does not, the advice starts again from the situation, which is what a learner can learn from.
        /// </summary>
        public bool ExpertRemembers;

        /// <summary>The advising bot keeps no plan at all: the same situation always gets the same advice (SurvivalBrain.Situational).</summary>
        public bool ExpertSituational;

        public GameSetup SetupFor(ulong seed)
        {
            var setup = new GameSetup { Record = false };
            setup.Sim.Seed = seed;
            setup.Sim.Agents = Agents;
            setup.Sim.MapWidth = MapWidth;
            setup.Sim.MapHeight = MapHeight;
            setup.Sim.StartSeason = StartSeason;
            setup.Sim.Weather = Weather;
            setup.Sim.Sickness = Sickness;
            setup.Sim.JobReviews = JobReviews;
            setup.Sim.MaxVillagersEver = Math.Max(MaxAgents, Agents);
            return setup;
        }
    }

    /// <summary>How a village of the pool ended.</summary>
    public sealed class EpisodeResult
    {
        public int Env;
        public ulong Seed;
        public long Ticks;
        public int Alive;
        public string EndReason;
        public int Achievements;
        public string RunId;
        public Dictionary<string, double> Metrics;
    }

    /// <summary>
    /// Many villages stepped side by side, for a trainer that picks every villager's action itself:
    /// the actions of all of them go in, and what each sees, may do and earned comes out, in flat
    /// arrays one row per village per villager. A village that ends is reported in Finished and a new
    /// one, with the next unused seed, takes its place at once. No Unity types.
    /// </summary>
    public sealed class EnvPool : IDisposable
    {
        public readonly int Envs;
        public readonly int Agents;
        public readonly ObservationLayout Layout;
        public readonly int ArgMaskBytes;

        // One row per village per villager, villages in order.
        public readonly byte[] Grid;
        public readonly float[] Vector;
        public readonly byte[] ActionMask;

        /// <summary>The argument flags of every action, packed eight to a byte, highest bit first.</summary>
        public readonly byte[] ArgMask;

        /// <summary>Two per villager: earned for itself, and as one of the village, on the tick just played.</summary>
        public readonly float[] Reward;
        public readonly byte[] Alive;

        /// <summary>One per village: 1 when the tick just played was its last. What is shown is then a new village.</summary>
        public readonly byte[] Done;

        /// <summary>Two per villager, when EnvOptions.Expert is set: the action and argument the scripted bot would choose now.</summary>
        public readonly byte[] Expert;

        /// <summary>The villages that ended on the last Step.</summary>
        public readonly List<EpisodeResult> Finished = new List<EpisodeResult>();

        readonly GameData data;
        readonly EnvOptions options;
        readonly ParallelOptions parallel;
        readonly Env[] envs;
        readonly EpisodeResult[] ended;
        ulong nextSeed;

        sealed class Env
        {
            public Simulation Sim;
            public ulong Seed;
            public RunStats Stats;
            public RunRecorder Recorder;
            public SurvivalBrain Expert;
            public long Limit;
            public AgentAction[] Actions;
            public bool[] Mask;
            public byte[] Table;
            public byte[] Grid;
            public float[] Vector;
        }

        public EnvPool(GameData data, EnvOptions options)
        {
            this.data = data;
            this.options = options;
            Envs = Math.Max(1, options.Envs);
            nextSeed = options.Seed;
            int threads = options.Threads > 0 ? options.Threads : Environment.ProcessorCount;
            parallel = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Math.Min(threads, Envs)) };
            envs = new Env[Envs];
            ended = new EpisodeResult[Envs];
            for (int e = 0; e < Envs; e++) envs[e] = new Env();
            Begin(envs[0], e: 0, first: true);
            Agents = Math.Max(options.MaxAgents, envs[0].Sim.Agents.Count);
            envs[0].Sim.MaxVillagersEver = Agents;
            Layout = envs[0].Sim.Observation;
            ArgMaskBytes = (Layout.ActionCount * Layout.ArgCount + 7) / 8;
            for (int e = 1; e < Envs; e++) Begin(envs[e], e, first: true);

            int rows = Envs * Agents;
            Grid = new byte[rows * Layout.GridLength];
            Vector = new float[rows * Layout.VectorLength];
            ActionMask = new byte[rows * Layout.ActionCount];
            ArgMask = new byte[rows * ArgMaskBytes];
            Reward = new float[rows * 2];
            Alive = new byte[rows];
            Done = new byte[Envs];
            Expert = new byte[rows * 2];
        }

        /// <summary>The village in that place, to look at. Changing it by hand is for tests.</summary>
        public Simulation SimulationOf(int env) => envs[env].Sim;

        /// <summary>A new village in that place, with the next seed nobody has used.</summary>
        void Begin(Env env, int e, bool first)
        {
            ulong seed = nextSeed++;
            env.Limit = first && options.Stagger ? Math.Max(1L, (long)options.MaxTicks * (e + 1) / Envs) : options.MaxTicks;
            var setup = options.SetupFor(seed);
            env.Seed = seed;
            env.Sim = Simulation.Create(data, setup.Sim);
            if (options.RewardShapingScale >= 0f) env.Sim.RewardShapingScale = options.RewardShapingScale;
            if (Agents != 0 && env.Sim.Agents.Count > Agents)
                throw new InvalidOperationException("Village " + seed + " has " + env.Sim.Agents.Count + " villagers; the pool is laid out for " + Agents + ".");
            if (Agents != 0) env.Sim.MaxVillagersEver = Agents;
            env.Recorder = options.RecordFolder != null ? RunRecorder.Start(options.RecordFolder, data, env.Sim, setup, null, options.BrainsLabel) : null;
            env.Stats = env.Recorder != null ? env.Recorder.Stats : new RunStats(data, env.Sim);
            env.Expert = options.Expert
                ? new SurvivalBrain(seed) { Memoryless = !options.ExpertRemembers, Situational = options.ExpertSituational && !options.ExpertRemembers } : null;
            int agents = env.Sim.Agents.Count;
            var layout = env.Sim.Observation;
            env.Actions = new AgentAction[agents];
            env.Mask = new bool[layout.ActionCount];
            env.Table = new byte[layout.ActionCount * layout.ArgCount];
            env.Grid = new byte[layout.GridLength];
            env.Vector = new float[layout.VectorLength];
        }

        /// <summary>Starts every village afresh and shows the first views.</summary>
        public void Reset()
        {
            Finished.Clear();
            Array.Clear(Done, 0, Done.Length);
            Array.Clear(Reward, 0, Reward.Length);
            for (int e = 0; e < Envs; e++)
            {
                if (envs[e].Sim.Tick != 0)
                {
                    envs[e].Recorder?.Finish("reset");
                    Begin(envs[e], e, first: true);
                }
                Show(e);
            }
        }

        /// <summary>
        /// Plays one tick in every village. actions holds, village by village and villager by villager,
        /// the action and its argument: two bytes each. What a dead villager is told to do is ignored.
        /// </summary>
        public void Step(byte[] actions)
        {
            if (actions.Length != Envs * Agents * 2) throw new ArgumentException("Step needs two bytes for each of " + Envs * Agents + " villagers.");
            Parallel.For(0, Envs, parallel, e => StepOne(e, actions));
            // New villages are made here, one after another in the order of their places: made on the
            // threads above, which of two villages ending together got which seed would be left to chance.
            Finished.Clear();
            for (int e = 0; e < Envs; e++)
            {
                if (ended[e] == null) continue;
                Finished.Add(ended[e]);
                Begin(envs[e], e, first: false);
                Show(e);
            }
        }

        void StepOne(int e, byte[] actions)
        {
            var env = envs[e];
            var sim = env.Sim;
            int first = e * Agents, born = sim.Agents.Count;
            if (env.Actions.Length != born) env.Actions = new AgentAction[born];
            for (int i = 0; i < born; i++) env.Actions[i] = new AgentAction(actions[(first + i) * 2], actions[(first + i) * 2 + 1]);
            sim.Step(env.Actions);
            if (env.Recorder != null) env.Recorder.AfterTick(env.Actions);
            else env.Stats.AfterTick(sim, env.Actions);
            for (int i = 0; i < Agents; i++) sim.RewardPartsOf(i, out Reward[(first + i) * 2], out Reward[(first + i) * 2 + 1]);

            int alive = 0;
            foreach (var agent in sim.Agents)
                if (agent.Alive) alive++;
            bool over = alive == 0 || sim.Tick >= env.Limit;
            Done[e] = (byte)(over ? 1 : 0);
            ended[e] = null;
            if (over)
            {
                string reason = alive == 0 ? "everyone_dead" : sim.Tick < options.MaxTicks ? "staggered" : "time_up";
                if (env.Recorder != null) env.Recorder.Finish(reason);
                else
                {
                    env.Stats.Sample(sim);
                    env.Stats.Close();
                }
                ended[e] = new EpisodeResult
                {
                    Env = e, Seed = env.Seed, Ticks = sim.Tick, Alive = alive, EndReason = reason, Achievements = env.Stats.Achievements.Count,
                    RunId = env.Recorder?.RunId, Metrics = new Dictionary<string, double>(env.Stats.Metrics),
                };
                return;
            }
            Show(e);
        }

        /// <summary>Fills the village's rows with what its villagers see and may do now.</summary>
        void Show(int e)
        {
            var env = envs[e];
            var sim = env.Sim;
            int actions = Layout.ActionCount, flags = Layout.ActionCount * Layout.ArgCount;
            for (int i = 0; i < Agents; i++)
            {
                int row = e * Agents + i;
                bool living = i < sim.Agents.Count && sim.Agents[i].Alive;
                Alive[row] = (byte)(living ? 1 : 0);
                Expert[row * 2] = Expert[row * 2 + 1] = 0;
                if (!living)
                {
                    Array.Clear(Grid, row * Layout.GridLength, Layout.GridLength);
                    Array.Clear(Vector, row * Layout.VectorLength, Layout.VectorLength);
                    Array.Clear(ActionMask, row * actions, actions);
                    Array.Clear(ArgMask, row * ArgMaskBytes, ArgMaskBytes);
                    continue;
                }
                sim.Observe(i, env.Grid, env.Vector);
                sim.GetMasks(i, env.Mask, env.Table);
                if (env.Expert != null)
                {
                    // Asked every tick, whoever's choice is played: the bot plans from where the villager is, not from where it meant it to be.
                    var advice = env.Expert.Decide(sim, i);
                    Expert[row * 2] = (byte)advice.Id;
                    Expert[row * 2 + 1] = (byte)advice.Arg;
                }
                Buffer.BlockCopy(env.Grid, 0, Grid, row * Layout.GridLength, Layout.GridLength);
                Buffer.BlockCopy(env.Vector, 0, Vector, row * Layout.VectorLength * 4, Layout.VectorLength * 4);
                for (int a = 0; a < actions; a++) ActionMask[row * actions + a] = (byte)(env.Mask[a] ? 1 : 0);
                int at = row * ArgMaskBytes;
                Array.Clear(ArgMask, at, ArgMaskBytes);
                for (int flag = 0; flag < flags; flag++)
                    if (env.Table[flag] != 0) ArgMask[at + (flag >> 3)] |= (byte)(0x80 >> (flag & 7));
            }
        }

        /// <summary>Closes the recordings of the villages still under way.</summary>
        public void Dispose()
        {
            foreach (var env in envs) env.Recorder?.Finish("cut_off");
        }
    }
}
