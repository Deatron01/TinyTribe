using System;
using System.Collections.Generic;
using TinyTribe.Sim.Core;
using TinyTribe.Sim.Data;

namespace TinyTribe.Brains
{
    /// <summary>
    /// The counters of a run and the achievements reached in it (Sim Achievements, rule R43).
    /// Every counter has a name ("built.campfire", "deaths.thirst", "days"); an achievement is one of
    /// them reaching a value. Fed with each tick's events, so it costs next to nothing to keep.
    /// These are for people reading a run. No villager is rewarded for them or sees them.
    /// </summary>
    public sealed class RunStats
    {
        public struct RoleChange
        {
            public long Tick;
            public int Role;
        }

        /// <summary>One villager's part in the run.</summary>
        public sealed class Villager
        {
            public float RewardTotal;
            public long TicksAlive;
            public long DiedTick = -1;
            public string DiedOf;
            public int[] ActionCounts;
            public readonly List<RoleChange> RoleHistory = new List<RoleChange>();
            internal int Role;
        }

        public struct Reached
        {
            public AchievementDef Def;
            public long Tick;
            public int Day;
        }

        // Counters that stand alone; the rest are one of the prefixes below and an id from the data.
        static readonly HashSet<string> PlainMetrics = new HashSet<string>
        {
            "ticks", "days", "seasons", "years", "alive", "min_alive", "deaths", "winters_survived", "winters_without_death",
            "years_without_death", "gathered", "crafted", "built", "buildings_started", "buildings_destroyed", "planted", "crops_reaped",
            "killed", "hurt", "techs", "votes_opened", "votes_cast", "votes_decided", "research_votes_decided", "roles_assigned",
            "role_changes", "all_have_jobs", "deposited", "taken", "tools_broken", "campfire_out", "homes_claimed", "invalid_actions",
            "cured", "max_food_stored", "max_items_stored", "reward_total", "reward_mean_per_agent_tick",
            "villagers", "max_alive", "marriages", "births", "came_of_age", "emote_exchanges", "gifts",
        };

        const string Attack = "attack", Sickness = "sickness", OldAge = "old_age", Winter = "winter", RipeCrop = "crop_mature";
        const string FoodGroup = "food", WoodGroup = "wood", StoneItem = "cobblestone";

        readonly GameData data;
        readonly Dictionary<string, double> metrics = new Dictionary<string, double>();
        readonly List<Villager> villagers = new List<Villager>();
        readonly List<Reached> reached = new List<Reached>();
        readonly List<AchievementDef> open = new List<AchievementDef>();
        readonly string[] gatheredKey, craftedKey, consumedKey, builtKey, killedKey, hurtByKey, techKey, plantedKey, weatherKey, sickKey, deathKey;
        readonly bool[] itemIsFood, itemIsWood;
        readonly int stoneItem, ripeCropNode, ticksPerDay, ticksPerYear, daysPerSeason, seasonCount;
        readonly int[] stored;
        long ticks;
        int minAlive = int.MaxValue, maxAlive;
        int deathsThisSeason, deathsThisYear;
        string lastSeason;

        public IReadOnlyDictionary<string, double> Metrics => metrics;
        public IReadOnlyList<Villager> Agents => villagers;
        public IReadOnlyList<Reached> Achievements => reached;

        /// <summary>What the village had in storage at the last sample.</summary>
        public int FoodStored { get; private set; }
        public int WoodStored { get; private set; }
        public int StoneStored { get; private set; }
        public int ItemsStored { get; private set; }

        public RunStats(GameData data, Simulation sim)
        {
            this.data = data;
            int items = 0;
            foreach (var item in data.Items) items = Math.Max(items, item.Int + 1);
            gatheredKey = new string[items];
            craftedKey = new string[items];
            consumedKey = new string[items];
            itemIsFood = new bool[items];
            itemIsWood = new bool[items];
            stored = new int[items];
            foreach (var item in data.Items)
            {
                gatheredKey[item.Int] = "gathered." + item.Id;
                craftedKey[item.Int] = "crafted." + item.Id;
                consumedKey[item.Int] = "consumed." + item.Id;
                itemIsFood[item.Int] = item.StorageGroup == FoodGroup;
                itemIsWood[item.Int] = item.StorageGroup == WoodGroup;
            }
            stoneItem = data.ItemById[StoneItem].Int;
            builtKey = Keys(data.Buildings, b => b.Int, b => "built." + b.Id);
            killedKey = Keys(data.Animals, a => a.Int, a => "killed." + a.Id);
            hurtByKey = Keys(data.Animals, a => a.Int, a => "hurt_by." + a.Id);
            techKey = InOrder(data.Tech, t => "tech." + t.Id);
            plantedKey = InOrder(data.Crops, c => "planted." + c.Id);
            weatherKey = InOrder(data.Weather, w => "weather." + w.Id);
            sickKey = InOrder(data.Sicknesses, s => "sick." + s.Id);
            deathKey = InOrder(data.Needs, n => "deaths." + n.Id);
            ripeCropNode = data.NodeById(RipeCrop).Int;
            ticksPerDay = sim.Config.TicksPerDay;
            daysPerSeason = sim.Config.DaysPerSeason;
            seasonCount = data.Seasons.Count;
            ticksPerYear = ticksPerDay * daysPerSeason * seasonCount;
            lastSeason = sim.Season.Id;
            open.AddRange(data.Achievements);
            Meet(sim);
        }

        static string[] Keys<T>(IReadOnlyList<T> rows, Func<T, int> index, Func<T, string> key)
        {
            int size = 0;
            foreach (var row in rows) size = Math.Max(size, index(row) + 1);
            var keys = new string[size];
            foreach (var row in rows) keys[index(row)] = key(row);
            return keys;
        }

        static string[] InOrder<T>(IReadOnlyList<T> rows, Func<T, string> key)
        {
            var keys = new string[rows.Count];
            for (int i = 0; i < rows.Count; i++) keys[i] = key(rows[i]);
            return keys;
        }

        /// <summary>Is this the name of a counter a run keeps? Used to check Sim Achievements.</summary>
        public static bool IsMetric(GameData data, string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (PlainMetrics.Contains(name)) return true;
            int dot = name.IndexOf('.');
            if (dot <= 0) return false;
            string prefix = name.Substring(0, dot), id = name.Substring(dot + 1);
            switch (prefix)
            {
                case "gathered":
                case "crafted":
                case "consumed":
                    return data.ItemById.ContainsKey(id);
                case "built": return data.BuildingById.ContainsKey(id);
                case "killed":
                case "hurt_by":
                    return Has(data.Animals, a => a.Id == id);
                case "tech": return Has(data.Tech, t => t.Id == id);
                case "planted": return Has(data.Crops, c => c.Id == id);
                case "weather": return Has(data.Weather, w => w.Id == id);
                case "sick": return Has(data.Sicknesses, s => s.Id == id);
                case "deaths": return id == Attack || id == Sickness || id == OldAge || Has(data.Needs, n => n.Id == id);
                default: return false;
            }
        }

        static bool Has<T>(IReadOnlyList<T> rows, Func<T, bool> match)
        {
            foreach (var row in rows)
                if (match(row)) return true;
            return false;
        }

        public double Get(string metric) => metrics.TryGetValue(metric, out double value) ? value : 0;

        public bool HasAchieved(string achievementId)
        {
            foreach (var achievement in reached)
                if (achievement.Def.Id == achievementId) return true;
            return false;
        }

        void Add(string metric, double amount = 1)
        {
            if (metric == null) return;
            metrics.TryGetValue(metric, out double value);
            metrics[metric] = value + amount;
        }

        void Set(string metric, double value) => metrics[metric] = value;

        static string At(string[] keys, int index) => (uint)index < (uint)keys.Length ? keys[index] : null;

        /// <summary>Villagers who joined since the last look, with the role they came with.</summary>
        void Meet(Simulation sim)
        {
            while (villagers.Count < sim.Agents.Count)
            {
                var agent = sim.Agents[villagers.Count];
                var villager = new Villager { ActionCounts = new int[data.Actions.Count], Role = agent.Role };
                if (agent.Role != 0) villager.RoleHistory.Add(new RoleChange { Tick = sim.Tick, Role = agent.Role });
                if (!agent.Alive) villager.DiedTick = sim.Tick;
                villagers.Add(villager);
            }
        }

        /// <summary>Counts the tick that was just played: what every villager chose, and what came of it.</summary>
        public void AfterTick(Simulation sim, AgentAction[] chosen)
        {
            Meet(sim);
            ticks++;
            double reward = 0;
            for (int i = 0; i < villagers.Count; i++)
            {
                var villager = villagers[i];
                float earned = sim.RewardOf(i);
                villager.RewardTotal += earned;
                reward += earned;
                if (villager.DiedTick >= 0) continue;
                villager.TicksAlive++;
                if (i < chosen.Length && (uint)chosen[i].Id < (uint)villager.ActionCounts.Length) villager.ActionCounts[chosen[i].Id]++;
            }
            Add("reward_total", reward);

            int deaths = 0;
            foreach (var e in sim.Events)
            {
                switch (e.Type)
                {
                    case SimEventType.Harvested:
                        Add("gathered", e.Qty);
                        Add(At(gatheredKey, e.Item), e.Qty);
                        break;
                    case SimEventType.NodeDepleted:
                        if (e.Item == ripeCropNode) Add("crops_reaped");
                        break;
                    case SimEventType.Consumed:
                        Add(At(consumedKey, e.Item), e.Qty);
                        break;
                    case SimEventType.ToolBroke:
                        Add("tools_broken");
                        break;
                    case SimEventType.Died:
                        deaths++;
                        string cause = e.Item == Simulation.DiedOfSickness ? Sickness : e.Item == Simulation.DiedOfOldAge ? OldAge
                            : e.Item < 0 ? Attack : data.Needs[e.Item].Id;
                        Add("deaths");
                        Add("deaths." + cause);
                        villagers[e.Agent].DiedTick = sim.Tick;
                        villagers[e.Agent].DiedOf = cause;
                        break;
                    case SimEventType.Crafted:
                        Add("crafted", e.Qty);
                        Add(At(craftedKey, e.Item), e.Qty);
                        break;
                    case SimEventType.BuildingStarted:
                        Add("buildings_started");
                        break;
                    case SimEventType.BuildingCompleted:
                        Add("built");
                        Add(At(builtKey, e.Item));
                        break;
                    case SimEventType.BuildingDestroyed:
                        Add("buildings_destroyed");
                        break;
                    case SimEventType.Deposited:
                        Add("deposited", e.Qty);
                        break;
                    case SimEventType.Taken:
                        Add("taken", e.Qty);
                        break;
                    case SimEventType.CampfireOut:
                        Add("campfire_out");
                        break;
                    case SimEventType.HomeClaimed:
                        Add("homes_claimed");
                        break;
                    case SimEventType.Married:
                        Add("marriages");
                        break;
                    case SimEventType.Born:
                        Add("births");
                        break;
                    case SimEventType.CameOfAge:
                        Add("came_of_age");
                        break;
                    case SimEventType.EmoteExchanged:
                        Add("emote_exchanges");
                        break;
                    case SimEventType.Gave:
                        Add("gifts");
                        break;
                    case SimEventType.AgentHurt:
                        Add("hurt");
                        Add(At(hurtByKey, e.Item));
                        break;
                    case SimEventType.AnimalKilled:
                        Add("killed");
                        Add(At(killedKey, e.Item));
                        break;
                    case SimEventType.VoteOpened:
                        Add("votes_opened");
                        break;
                    case SimEventType.VoteCast:
                        Add("votes_cast");
                        break;
                    case SimEventType.VoteClosed:
                        if (e.Qty == 0) break;
                        Add("votes_decided");
                        if (e.Item == (int)VoteKind.Research) Add("research_votes_decided");
                        break;
                    case SimEventType.RoleAssigned:
                        var holder = villagers[e.Agent];
                        Add("roles_assigned");
                        if (holder.Role != 0) Add("role_changes");
                        holder.Role = e.Item;
                        holder.RoleHistory.Add(new RoleChange { Tick = sim.Tick, Role = e.Item });
                        break;
                    case SimEventType.WeatherChanged:
                        Add(At(weatherKey, e.Item));
                        break;
                    case SimEventType.TechResearched:
                        Add("techs");
                        Add(At(techKey, e.Item));
                        break;
                    case SimEventType.CropPlanted:
                        Add("planted");
                        Add(At(plantedKey, e.Item));
                        break;
                    case SimEventType.SicknessStarted:
                        Add(At(sickKey, e.Item));
                        break;
                    case SimEventType.SicknessEnded:
                        Add("cured");
                        break;
                    case SimEventType.InvalidAction:
                        Add("invalid_actions");
                        break;
                }
            }

            int alive = 0;
            bool allHaveJobs = true;
            foreach (var agent in sim.Agents)
            {
                if (!agent.Alive) continue;
                alive++;
                allHaveJobs &= agent.Role != 0;
            }
            minAlive = Math.Min(minAlive, alive);
            maxAlive = Math.Max(maxAlive, alive);
            Set("max_alive", maxAlive);
            Set("villagers", villagers.Count);
            Set("alive", alive);
            Set("min_alive", minAlive);
            if (alive > 0 && allHaveJobs) Set("all_have_jobs", 1);

            long days = ticks / ticksPerDay;
            Set("ticks", ticks);
            Set("days", days);
            Set("seasons", days / daysPerSeason);
            Set("years", days / daysPerSeason / seasonCount);

            deathsThisSeason += deaths;
            deathsThisYear += deaths;
            string season = sim.Season.Id;
            if (season != lastSeason)
            {
                if (lastSeason == Winter && alive > 0)
                {
                    Add("winters_survived");
                    if (deathsThisSeason == 0) Add("winters_without_death");
                }
                deathsThisSeason = 0;
                lastSeason = season;
            }
            if (ticks % ticksPerYear == 0)
            {
                if (deathsThisYear == 0 && alive > 0) Add("years_without_death");
                deathsThisYear = 0;
            }

            CheckAchievements(sim);
        }

        /// <summary>Looks into the village's storage: the fullest it has been is a counter too.</summary>
        public void Sample(Simulation sim)
        {
            VillageSummary.CountStoredItems(sim, stored);
            int food = 0, wood = 0, all = 0;
            for (int item = 0; item < stored.Length; item++)
            {
                all += stored[item];
                if (itemIsFood[item]) food += stored[item];
                if (itemIsWood[item]) wood += stored[item];
            }
            FoodStored = food;
            WoodStored = wood;
            StoneStored = stored[stoneItem];
            ItemsStored = all;
            if (food > Get("max_food_stored")) Set("max_food_stored", food);
            if (all > Get("max_items_stored")) Set("max_items_stored", all);
            CheckAchievements(sim);
        }

        /// <summary>The counters that are worked out from the others, for the end of a run.</summary>
        public void Close()
        {
            long agentTicks = 0;
            foreach (var villager in villagers) agentTicks += villager.TicksAlive;
            Set("reward_mean_per_agent_tick", agentTicks > 0 ? Get("reward_total") / agentTicks : 0);
        }

        void CheckAchievements(Simulation sim)
        {
            for (int i = open.Count - 1; i >= 0; i--)
            {
                var achievement = open[i];
                if (Get(achievement.Metric) < achievement.AtLeast) continue;
                reached.Add(new Reached { Def = achievement, Tick = sim.Tick, Day = (int)(ticks / ticksPerDay) });
                open.RemoveAt(i);
            }
        }
    }
}
