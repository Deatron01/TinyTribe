using System.Collections.Generic;
using Newtonsoft.Json;
using TinyTribe.Sim.Core;

namespace TinyTribe.Brains
{
    /// <summary>run.json, as laid out in the workbook's Sim Run Log Schema sheet.</summary>
    public sealed class RunFile
    {
        [JsonProperty("schema")] public int Schema;
        [JsonProperty("run_id")] public string RunId;
        [JsonProperty("started_utc")] public string StartedUtc;
        [JsonProperty("ended_utc")] public string EndedUtc;
        [JsonProperty("wall_seconds")] public double WallSeconds;
        [JsonProperty("host")] public Dictionary<string, string> Host;
        [JsonProperty("data")] public RunData Data;
        [JsonProperty("setup")] public RunSetup Setup;
        [JsonProperty("world")] public RunWorld World;
        [JsonProperty("villagers")] public List<RunVillager> Villagers;
        [JsonProperty("files")] public Dictionary<string, string> Files;
        [JsonProperty("result")] public RunResult Result;
    }

    public sealed class RunData
    {
        [JsonProperty("workbook")] public string Workbook;
        [JsonProperty("workbook_sha256")] public string WorkbookSha256;
    }

    public sealed class RunSetup
    {
        [JsonProperty("seed")] public ulong Seed;
        [JsonProperty("agents")] public int Agents;
        [JsonProperty("map_width")] public int MapWidth;
        [JsonProperty("map_height")] public int MapHeight;
        [JsonProperty("start_season")] public string StartSeason;
        [JsonProperty("weather")] public bool Weather;
        [JsonProperty("sickness")] public bool Sickness;
        [JsonProperty("job_reviews")] public bool JobReviews;
        [JsonProperty("max_agents")] public int MaxAgents;
        [JsonProperty("max_villagers_ever")] public int MaxVillagersEver;
        [JsonProperty("reward_shaping_scale")] public float RewardShapingScale;
        [JsonProperty("brains")] public string Brains;

        /// <summary>True when the world came from the generator at tick 0, so setup and actions give the run again.</summary>
        [JsonProperty("replayable")] public bool Replayable;

        public SimulationOptions ToOptions() => new SimulationOptions
        {
            Seed = Seed, Agents = Agents, MapWidth = MapWidth, MapHeight = MapHeight, StartSeason = StartSeason,
            Weather = Weather, Sickness = Sickness, JobReviews = JobReviews, MaxAgents = MaxAgents, MaxVillagersEver = MaxVillagersEver,
        };
    }

    public sealed class RunWorld
    {
        [JsonProperty("width")] public int Width;
        [JsonProperty("height")] public int Height;
        [JsonProperty("spawn_x")] public int SpawnX;
        [JsonProperty("spawn_y")] public int SpawnY;
    }

    public sealed class RunVillager
    {
        [JsonProperty("index")] public int Index;
        [JsonProperty("str")] public int Str;
        [JsonProperty("dex")] public int Dex;
        [JsonProperty("int")] public int Int;
        [JsonProperty("end")] public int End;
        [JsonProperty("influence")] public float Influence;
        [JsonProperty("empathy")] public float Empathy;
        [JsonProperty("sociability")] public float Sociability;
        [JsonProperty("aggression")] public float Aggression;
        [JsonProperty("pack_slots")] public int PackSlots;
        [JsonProperty("x")] public int X;
        [JsonProperty("y")] public int Y;
        [JsonProperty("brain")] public string Brain;
    }

    public sealed class RunResult
    {
        [JsonProperty("ticks")] public long Ticks;
        [JsonProperty("days")] public int Days;
        [JsonProperty("end_reason")] public string EndReason;
        [JsonProperty("alive")] public int Alive;
        [JsonProperty("final_state_hash")] public string FinalStateHash;
        [JsonProperty("metrics")] public SortedDictionary<string, double> Metrics;
        [JsonProperty("achievements")] public List<RunAchievement> Achievements;
        [JsonProperty("achievements_missed")] public List<string> AchievementsMissed;
        [JsonProperty("agents")] public List<RunAgentResult> Agents;
    }

    public sealed class RunAchievement
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("name")] public string Name;
        [JsonProperty("group")] public string Group;
        [JsonProperty("tick")] public long Tick;
        [JsonProperty("day")] public int Day;
    }

    public sealed class RunAgentResult
    {
        [JsonProperty("index")] public int Index;
        [JsonProperty("alive")] public bool Alive;
        [JsonProperty("died_tick")] public long DiedTick;
        [JsonProperty("died_of")] public string DiedOf;
        [JsonProperty("ticks_alive")] public long TicksAlive;
        [JsonProperty("reward_total")] public float RewardTotal;
        [JsonProperty("role")] public string Role;

        /// <summary>Pairs of tick and role id, in the order the Council gave them.</summary>
        [JsonProperty("role_history")] public List<object[]> RoleHistory;

        /// <summary>How often the villager chose each action, by the action's name.</summary>
        [JsonProperty("action_counts")] public SortedDictionary<string, int> ActionCounts;
    }
}
