using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TinyTribe.Sim.Core;
using TinyTribe.Sim.Data;
using UnityEngine;

namespace TinyTribe.Tests
{
    /// <summary>Shared setup for sim tests. Uses the real exported data, so expected values are read from it.</summary>
    static class Kit
    {
        static GameData data;

        public static GameData Data => data ??= GameData.Load(Path.Combine(Application.streamingAssetsPath, "Data"));

        /// <summary>Every stat at its midpoint, so all stat multipliers are exactly 1.</summary>
        public static AgentStats Neutral
        {
            get
            {
                int mid = Data.ConfigInt("stat_midpoint");
                return new AgentStats
                {
                    Str = mid, Dex = mid, Int = mid, End = mid,
                    Influence = 0.5f, Empathy = 0.5f, Sociability = 0.5f, Aggression = 0.5f,
                };
            }
        }

        /// <summary>
        /// A small all-grass world with no nodes and no agents, under clear skies: the daily weather rolls
        /// are off, and so are job reviews, so a villager keeps the role a test gives it.
        /// </summary>
        public static Simulation NewSim(int width = 9, int height = 9, ulong seed = 1) =>
            new Simulation(Data, new World(width, height, Data.TerrainById("grass")), seed) { WeatherEnabled = false, JobReviewsEnabled = false };

        public static int Item(string id) => Data.ItemById[id].Int;

        public static void Advance(Simulation sim, long ticks)
        {
            Assert.AreEqual(0, sim.Agents.Count, "Advance is for empty simulations");
            for (long i = 0; i < ticks; i++) sim.Step(Array.Empty<AgentAction>());
        }

        public static void AdvanceTo(Simulation sim, int day, int hour)
        {
            long limit = 1_000_000;
            while (!(sim.Day == day && sim.Hour == hour))
            {
                Advance(sim, 1);
                if (--limit == 0) Assert.Fail("never reached day " + day + " hour " + hour);
            }
        }

        public static void AdvanceToSeason(Simulation sim, string seasonId, int hour)
        {
            int day = Data.Seasons.First(s => s.Id == seasonId).Order * Data.ConfigInt("days_per_season") + 1;
            AdvanceTo(sim, day, hour);
            Assert.AreEqual(seasonId, sim.Season.Id);
        }

        public static void Face(Agent agent, int dx, int dy)
        {
            agent.FaceDx = dx;
            agent.FaceDy = dy;
        }

        public static void Refill(Agent agent)
        {
            float max = Data.ConfigFloat("need_max");
            for (int need = 0; need < agent.Needs.Length; need++) agent.Needs[need] = max;
        }

        /// <summary>Repeats the harvest action until the agent holds more of the item. Returns the number of actions used.</summary>
        public static int HarvestUntilYield(Simulation sim, Agent agent, int itemInt, int limit = 500)
        {
            int before = agent.Count(itemInt);
            for (int n = 1; n <= limit; n++)
            {
                Refill(agent);
                sim.Step(new[] { new AgentAction(sim.Actions.Harvest) });
                if (agent.Count(itemInt) > before) return n;
            }
            Assert.Fail("no yield after " + limit + " harvest actions");
            return -1;
        }

        public static bool HasEvent(Simulation sim, SimEventType type) => sim.Events.Any(e => e.Type == type);

        public static int ExpectedActions(float harvestTicks, float speed) => (int)Math.Ceiling(harvestTicks / speed - 1e-4);

        /// <summary>The same stats with another strength (strength decides how many pack slots a villager has).</summary>
        public static AgentStats WithStrength(this AgentStats stats, int strength)
        {
            stats.Str = strength;
            return stats;
        }
    }
}
