using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using TinyTribe.Sim.Core;

namespace TinyTribe.Tests
{
    public class WorldGenTests
    {
        static readonly Dictionary<ulong, World> cache = new Dictionary<ulong, World>();

        static World Map(ulong seed)
        {
            if (!cache.TryGetValue(seed, out var world))
                cache[seed] = world = WorldGenerator.Generate(Kit.Data, seed);
            return world;
        }

        static int TerrainInt(string id) => Kit.Data.TerrainById(id).Int;

        static float Share(World world, string terrainId)
        {
            int count = 0, terrain = TerrainInt(terrainId);
            for (int y = 0; y < world.Height; y++)
                for (int x = 0; x < world.Width; x++)
                    if (world.TerrainAt(x, y) == terrain) count++;
            return count / (float)(world.Width * world.Height);
        }

        [Test]
        public void Map_has_the_configured_size_and_a_central_spawn()
        {
            var world = Map(1);

            Assert.AreEqual(Kit.Data.ConfigInt("map_width"), world.Width);
            Assert.AreEqual(Kit.Data.ConfigInt("map_height"), world.Height);
            Assert.AreEqual((world.Width / 2, world.Height / 2), (world.SpawnX, world.SpawnY));
        }

        [Test]
        public void Village_centre_is_clear_grass()
        {
            var world = Map(1);
            int radius = Kit.Data.ConfigInt("spawn_clear_radius");

            for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int x = world.SpawnX + dx, y = world.SpawnY + dy;
                    Assert.AreEqual(TerrainInt("grass"), world.TerrainAt(x, y), "terrain at " + x + "," + y);
                    Assert.AreEqual(0, world.NodeAt(x, y), "node at " + x + "," + y);
                }
        }

        [TestCase("water", "water_share")]
        [TestCase("sand", "sand_share")]
        public void Terrain_shares_are_close_to_the_config(string terrainId, string configKey)
        {
            Assert.AreEqual(Kit.Data.ConfigFloat(configKey), Share(Map(1), terrainId), 0.03f);
        }

        [Test]
        public void Mountains_are_split_into_rock_and_walkable_cave()
        {
            var world = Map(1);
            float mountain = Share(world, "mountain"), cave = Share(world, "cave");

            Assert.AreEqual(Kit.Data.ConfigFloat("mountain_share"), mountain + cave, 0.03f);
            Assert.AreEqual(Kit.Data.ConfigFloat("cave_share"), cave / (mountain + cave), 0.05f);
        }

        [Test]
        public void Land_is_a_mix_of_grass_and_forest()
        {
            var world = Map(1);
            float grass = Share(world, "grass"), forest = Share(world, "forest");

            Assert.AreEqual(Kit.Data.ConfigFloat("forest_share"), forest / (grass + forest), 0.06f);
        }

        [Test]
        public void Every_water_tile_is_a_water_source()
        {
            var world = Map(1);
            int water = TerrainInt("water"), source = Kit.Data.NodeById("water_source").Int;

            for (int y = 0; y < world.Height; y++)
                for (int x = 0; x < world.Width; x++)
                    if (world.TerrainAt(x, y) == water)
                        Assert.AreEqual(source, world.NodeAt(x, y), "at " + x + "," + y);
        }

        [Test]
        public void Nodes_sit_on_their_own_terrain()
        {
            var world = Map(1);

            for (int y = 0; y < world.Height; y++)
                for (int x = 0; x < world.Width; x++)
                {
                    int node = world.NodeAt(x, y);
                    if (node == 0) continue;
                    Assert.AreEqual(Kit.Data.NodeByInt(node).Terrain, Kit.Data.TerrainByInt(world.TerrainAt(x, y)).Id,
                        Kit.Data.NodeByInt(node).Id + " at " + x + "," + y);
                }
        }

        [Test]
        public void Rare_nodes_keep_their_distance_from_the_village()
        {
            var world = Map(1);
            var limits = Kit.Data.WorldGen.Where(g => g.MinSpawnDistance > 0)
                .ToDictionary(g => Kit.Data.NodeById(g.NodeId).Int, g => g.MinSpawnDistance);
            Assert.IsNotEmpty(limits);

            for (int y = 0; y < world.Height; y++)
                for (int x = 0; x < world.Width; x++)
                    if (limits.TryGetValue(world.NodeAt(x, y), out int min))
                    {
                        double distance = Math.Sqrt(Math.Pow(x - world.SpawnX, 2) + Math.Pow(y - world.SpawnY, 2));
                        Assert.GreaterOrEqual(distance, min, Kit.Data.NodeByInt(world.NodeAt(x, y)).Id);
                    }
        }

        [Test]
        public void Every_listed_node_type_appears_on_the_map()
        {
            var world = Map(1);
            var present = new HashSet<int>();
            for (int y = 0; y < world.Height; y++)
                for (int x = 0; x < world.Width; x++)
                    present.Add(world.NodeAt(x, y));

            foreach (var entry in Kit.Data.WorldGen)
                Assert.IsTrue(present.Contains(Kit.Data.NodeById(entry.NodeId).Int), entry.NodeId + " missing");
        }

        [Test]
        public void Required_nodes_can_be_reached_on_foot_from_the_village([Values(1UL, 2UL, 3UL, 4UL, 5UL)] ulong seed)
        {
            var world = Map(seed);
            var reachable = NodesNextToWalkableArea(world, Kit.Data.ConfigInt("required_reach_distance"));

            foreach (string id in Kit.Data.ConfigString("required_nodes").Split(';'))
                Assert.IsTrue(reachable.Contains(Kit.Data.NodeById(id).Int), id + " not reachable, seed " + seed);
        }

        /// <summary>Independent flood fill: node ints adjacent to a tile an agent can walk to within maxSteps.</summary>
        static HashSet<int> NodesNextToWalkableArea(World world, int maxSteps)
        {
            var found = new HashSet<int>();
            var steps = new Dictionary<(int, int), int> { [(world.SpawnX, world.SpawnY)] = 0 };
            var queue = new Queue<(int, int)>();
            queue.Enqueue((world.SpawnX, world.SpawnY));
            int[] dx = { 1, -1, 0, 0 }, dy = { 0, 0, 1, -1 };
            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();
                int here = steps[(x, y)];
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + dx[d], ny = y + dy[d];
                    if (!world.InBounds(nx, ny)) continue;
                    if (world.HasActiveNode(nx, ny)) { found.Add(world.NodeAt(nx, ny)); continue; }
                    if (Kit.Data.TerrainByInt(world.TerrainAt(nx, ny)).Walkable == 0) continue;
                    if (here + 1 > maxSteps || steps.ContainsKey((nx, ny))) continue;
                    steps[(nx, ny)] = here + 1;
                    queue.Enqueue((nx, ny));
                }
            }
            return found;
        }

        [Test]
        public void Same_seed_gives_the_same_map_and_another_seed_a_different_one()
        {
            var a = WorldGenerator.Generate(Kit.Data, 11);
            var b = WorldGenerator.Generate(Kit.Data, 11);
            var c = WorldGenerator.Generate(Kit.Data, 12);
            bool sameAsB = true, sameAsC = true;

            for (int y = 0; y < a.Height; y++)
                for (int x = 0; x < a.Width; x++)
                {
                    sameAsB &= a.TerrainAt(x, y) == b.TerrainAt(x, y) && a.NodeAt(x, y) == b.NodeAt(x, y);
                    sameAsC &= a.TerrainAt(x, y) == c.TerrainAt(x, y) && a.NodeAt(x, y) == c.NodeAt(x, y);
                }

            Assert.IsTrue(sameAsB);
            Assert.IsFalse(sameAsC);
        }
    }

    public class SimulationSetupTests
    {
        [Test]
        public void Default_simulation_spawns_the_initial_agents_on_free_tiles_by_the_village()
        {
            var sim = Simulation.CreateDefault(Kit.Data, 3);

            Assert.AreEqual(Kit.Data.ConfigInt("initial_agents"), sim.Agents.Count);
            Assert.AreEqual(sim.Agents.Count, sim.Agents.Select(a => (a.X, a.Y)).Distinct().Count(), "one agent per tile");
            foreach (var agent in sim.Agents)
            {
                Assert.IsTrue(agent.Alive);
                Assert.AreEqual(agent.Index, sim.World.AgentAt(agent.X, agent.Y));
                Assert.LessOrEqual(Math.Abs(agent.X - sim.World.SpawnX) + Math.Abs(agent.Y - sim.World.SpawnY),
                    Kit.Data.ConfigInt("spawn_clear_radius") * 2);
            }
        }

        [Test]
        public void Rolled_stats_stay_within_their_ranges()
        {
            var sim = Kit.NewSim();
            int min = Kit.Data.ConfigInt("core_stat_min"), max = Kit.Data.ConfigInt("core_stat_max");
            int intCap = Kit.Data.ConfigInt("int_cap_generation_0");
            var seen = new HashSet<int>();

            for (int i = 0; i < 300; i++)
            {
                var s = sim.RollStats();
                foreach (int v in new[] { s.Str, s.Dex, s.End }) Assert.That(v, Is.InRange(min, max));
                Assert.That(s.Int, Is.InRange(min, intCap));
                foreach (float v in new[] { s.Influence, s.Empathy, s.Sociability, s.Aggression }) Assert.That(v, Is.InRange(0f, 1f));
                seen.Add(s.Str);
            }

            Assert.AreEqual(max - min + 1, seen.Count, "every strength value should come up in 300 rolls");
        }

        static ulong RunScript(ulong seed, int ticks)
        {
            var sim = Simulation.CreateDefault(Kit.Data, seed);
            var script = new System.Random(99);
            var actions = new AgentAction[sim.Agents.Count];
            int actionCount = Kit.Data.ConfigInt("num_actions"), argCount = Kit.Data.ConfigInt("num_action_args");
            for (int t = 0; t < ticks; t++)
            {
                for (int i = 0; i < actions.Length; i++)
                    actions[i] = new AgentAction(script.Next(actionCount), script.Next(argCount));
                sim.Step(actions);
            }
            return sim.ComputeStateHash();
        }

        [Test]
        public void Same_seed_and_actions_give_the_same_state()
        {
            Assert.AreEqual(RunScript(7, 600), RunScript(7, 600));
        }

        [Test]
        public void Different_seed_gives_a_different_state()
        {
            Assert.AreNotEqual(RunScript(7, 600), RunScript(8, 600));
        }

        [Test]
        public void State_hash_changes_when_an_agent_moves()
        {
            var sim = Kit.NewSim();
            sim.AddAgent(4, 4, Kit.Neutral);
            ulong before = sim.ComputeStateHash();

            sim.Step(new[] { new AgentAction(sim.Actions.MoveEast) });

            Assert.AreNotEqual(before, sim.ComputeStateHash());
        }

        [Test, Category("Benchmark")]
        public void Benchmark_full_village_ticks_per_second()
        {
            var sim = Simulation.CreateDefault(Kit.Data, 5);
            int target = Kit.Data.ConfigInt("max_agents");
            for (int r = 1; sim.Agents.Count < target && r < 40; r++)
                for (int dy = -r; dy <= r && sim.Agents.Count < target; dy++)
                    for (int dx = -r; dx <= r && sim.Agents.Count < target; dx++)
                    {
                        int x = sim.World.SpawnX + dx, y = sim.World.SpawnY + dy;
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                        if (sim.World.AgentAt(x, y) != -1 || sim.World.HasActiveNode(x, y)) continue;
                        if (Kit.Data.TerrainByInt(sim.World.TerrainAt(x, y)).Walkable == 0) continue;
                        sim.AddAgent(x, y, sim.RollStats());
                    }
            Assert.AreEqual(target, sim.Agents.Count);

            var script = new System.Random(1);
            var actions = new AgentAction[sim.Agents.Count];
            int[] pool = { sim.Actions.MoveNorth, sim.Actions.MoveSouth, sim.Actions.MoveEast, sim.Actions.MoveWest, sim.Actions.Harvest, sim.Actions.Harvest, sim.Actions.Wait };
            const int ticks = 20000;
            var watch = Stopwatch.StartNew();
            for (int t = 0; t < ticks; t++)
            {
                for (int i = 0; i < actions.Length; i++) actions[i] = new AgentAction(pool[script.Next(pool.Length)]);
                // Often enough that the wolves cannot finish anyone off: this measures a full, living village.
                if (t % 10 == 0) foreach (var agent in sim.Agents) Kit.Refill(agent);
                sim.Step(actions);
            }
            watch.Stop();

            double perSecond = ticks / watch.Elapsed.TotalSeconds;
            Assert.Greater(perSecond, 2000, "ticks per second with " + target + " agents");
            Assert.Pass($"{perSecond:F0} ticks/s with {target} agents ({sim.Agents.Count(a => a.Alive)} alive at the end), {watch.ElapsedMilliseconds} ms for {ticks} ticks");
        }
    }
}
