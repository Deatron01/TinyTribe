using System;
using System.Collections.Generic;
using TinyTribe.Sim.Data;

namespace TinyTribe.Sim.Core
{
    /// <summary>
    /// Builds a map from a seed. Terrain comes from smooth noise cut at the shares set in the
    /// 'worldgen' config; resource nodes are scattered at the densities in Sim World Gen.
    /// A map is only accepted if the village can walk to water, wood, stone and food.
    /// </summary>
    public static class WorldGenerator
    {
        const int Octaves = 3;

        /// <summary>Width or height of 0 uses map_width / map_height from the config.</summary>
        public static World Generate(GameData data, ulong seed, int width = 0, int height = 0)
        {
            if (width <= 0) width = data.ConfigInt("map_width");
            if (height <= 0) height = data.ConfigInt("map_height");
            int attempts = data.ConfigInt("max_generation_attempts");
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                var world = Build(data, unchecked(seed + (ulong)attempt * 0x9E3779B97F4A7C15UL), width, height);
                if (RequiredNodesReachable(data, world)) return world;
            }
            throw new InvalidOperationException("No playable map after " + attempts + " attempts for seed " + seed + ".");
        }

        static World Build(GameData data, ulong seed, int width, int height)
        {
            float scale = data.ConfigFloat("noise_scale");
            float waterShare = data.ConfigFloat("water_share");
            float sandShare = data.ConfigFloat("sand_share");
            float mountainShare = data.ConfigFloat("mountain_share");
            float caveShare = data.ConfigFloat("cave_share");
            float forestShare = data.ConfigFloat("forest_share");
            int clearRadius = data.ConfigInt("spawn_clear_radius");
            float landRadius = data.ConfigFloat("spawn_land_radius");

            var grass = data.TerrainById("grass");
            var forest = data.TerrainById("forest");
            var water = data.TerrainById("water");
            var sand = data.TerrainById("sand");
            var mountain = data.TerrainById("mountain");
            var cave = data.TerrainById("cave");

            var world = new World(width, height, grass);
            int size = width * height;
            uint noiseSeed = (uint)(seed ^ (seed >> 32));

            var elevation = new float[size];
            var moisture = new float[size];
            var cavern = new float[size];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    elevation[i] = Fbm(x / scale, y / scale, noiseSeed);
                    moisture[i] = Fbm(x / scale, y / scale, noiseSeed + 7919u);
                    cavern[i] = Fbm(x / (scale * 0.5f), y / (scale * 0.5f), noiseSeed + 104729u);
                }

            // Cut the elevation at fixed shares of the map so every seed has the same mix of terrain.
            float waterLevel = Percentile(elevation, waterShare);
            float sandLevel = Percentile(elevation, waterShare + sandShare);
            float mountainLevel = Percentile(elevation, 1f - mountainShare);

            // Pull the area around the village toward flat land.
            float flat = (sandLevel + mountainLevel) * 0.5f;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float dx = x - world.SpawnX, dy = y - world.SpawnY;
                    float pull = 1f - MathF.Sqrt(dx * dx + dy * dy) / landRadius;
                    if (pull <= 0f) continue;
                    int i = y * width + x;
                    elevation[i] += (flat - elevation[i]) * Smooth(pull);
                }

            var mountainNoise = new List<float>();
            var landMoisture = new List<float>();
            for (int i = 0; i < size; i++)
            {
                if (elevation[i] >= mountainLevel) mountainNoise.Add(cavern[i]);
                else if (elevation[i] >= sandLevel) landMoisture.Add(moisture[i]);
            }
            float caveLevel = Percentile(mountainNoise.ToArray(), caveShare);
            float forestLevel = Percentile(landMoisture.ToArray(), 1f - forestShare);

            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    TerrainDef terrain;
                    if (InClearing(world, x, y, clearRadius)) terrain = grass;
                    else if (elevation[i] < waterLevel) terrain = water;
                    else if (elevation[i] < sandLevel) terrain = sand;
                    else if (elevation[i] >= mountainLevel) terrain = cavern[i] < caveLevel ? cave : mountain;
                    else terrain = moisture[i] >= forestLevel ? forest : grass;
                    world.SetTerrain(x, y, terrain);
                }

            PlaceNodes(data, world, new Rng(seed), clearRadius);
            return world;
        }

        static bool InClearing(World world, int x, int y, int radius) =>
            Math.Abs(x - world.SpawnX) <= radius && Math.Abs(y - world.SpawnY) <= radius;

        static void PlaceNodes(GameData data, World world, Rng rng, int clearRadius)
        {
            // Group the Sim World Gen rows by the terrain their node grows on.
            var byTerrain = new Dictionary<int, List<(ResourceNodeDef node, WorldGenDef gen)>>();
            foreach (var gen in data.WorldGen)
            {
                var node = data.NodeById(gen.NodeId);
                int terrain = data.TerrainById(node.Terrain).Int;
                if (!byTerrain.TryGetValue(terrain, out var list)) byTerrain[terrain] = list = new List<(ResourceNodeDef, WorldGenDef)>();
                list.Add((node, gen));
            }

            for (int y = 0; y < world.Height; y++)
                for (int x = 0; x < world.Width; x++)
                {
                    // One roll per tile whatever happens, so a change to one table row does not shift the rest of the map.
                    float roll = rng.NextFloat() * 1000f;
                    if (InClearing(world, x, y, clearRadius)) continue;
                    if (!byTerrain.TryGetValue(world.TerrainAt(x, y), out var candidates)) continue;

                    float dx = x - world.SpawnX, dy = y - world.SpawnY;
                    float distance = MathF.Sqrt(dx * dx + dy * dy);
                    float cumulative = 0f;
                    foreach (var (node, gen) in candidates)
                    {
                        if (distance < gen.MinSpawnDistance) continue;
                        cumulative += gen.Per1000Tiles;
                        if (roll >= cumulative) continue;
                        world.PlaceNode(x, y, node);
                        break;
                    }
                }
        }

        /// <summary>Flood fill from the village: every node in required_nodes must border a tile an agent can walk to.</summary>
        static bool RequiredNodesReachable(GameData data, World world)
        {
            var required = new HashSet<int>();
            foreach (string id in data.ConfigString("required_nodes").Split(';'))
                required.Add(data.NodeById(id.Trim()).Int);
            int maxSteps = data.ConfigInt("required_reach_distance");

            var steps = new int[world.Width * world.Height];
            for (int i = 0; i < steps.Length; i++) steps[i] = -1;
            var queue = new Queue<int>();
            int start = world.Index(world.SpawnX, world.SpawnY);
            steps[start] = 0;
            queue.Enqueue(start);

            while (queue.Count > 0 && required.Count > 0)
            {
                int tile = queue.Dequeue();
                int x = tile % world.Width, y = tile / world.Width;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0);
                    int ny = y + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    if (!world.InBounds(nx, ny)) continue;
                    int next = world.Index(nx, ny);
                    if (world.HasActiveNode(next))
                    {
                        required.Remove(world.Node[next]);
                        continue;
                    }
                    if (data.TerrainByInt(world.Terrain[next]).Walkable == 0) continue;
                    if (steps[next] >= 0 || steps[tile] + 1 > maxSteps) continue;
                    steps[next] = steps[tile] + 1;
                    queue.Enqueue(next);
                }
            }
            return required.Count == 0;
        }

        /// <summary>The value below which the given share of the values lie.</summary>
        static float Percentile(float[] values, float share)
        {
            if (values.Length == 0) return 0f;
            var sorted = (float[])values.Clone();
            Array.Sort(sorted);
            int index = (int)(share * sorted.Length);
            return sorted[Math.Min(Math.Max(index, 0), sorted.Length - 1)];
        }

        // ------------------------------------------------------------------ value noise

        static float Fbm(float x, float y, uint seed)
        {
            float sum = 0f, amplitude = 1f, total = 0f;
            for (int octave = 0; octave < Octaves; octave++)
            {
                sum += amplitude * ValueNoise(x, y, seed + (uint)octave * 1013u);
                total += amplitude;
                amplitude *= 0.5f;
                x *= 2f;
                y *= 2f;
            }
            return sum / total;
        }

        static float ValueNoise(float x, float y, uint seed)
        {
            int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
            float tx = Smooth(x - x0), ty = Smooth(y - y0);
            float bottom = Lerp(Lattice(x0, y0, seed), Lattice(x0 + 1, y0, seed), tx);
            float top = Lerp(Lattice(x0, y0 + 1, seed), Lattice(x0 + 1, y0 + 1, seed), tx);
            return Lerp(bottom, top, ty);
        }

        static float Lattice(int x, int y, uint seed)
        {
            uint h = unchecked((uint)x * 0x8DA6B343u ^ (uint)y * 0xD8163841u ^ seed * 0xCB1AB31Fu);
            h ^= h >> 15;
            h = unchecked(h * 0x2C1B3C6Du);
            h ^= h >> 12;
            h = unchecked(h * 0x297A2D39u);
            h ^= h >> 15;
            return (h >> 8) * (1f / 16777216f);
        }

        static float Smooth(float t) => t * t * (3f - 2f * t);

        static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }
}
