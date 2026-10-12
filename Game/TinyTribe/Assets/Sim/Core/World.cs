using System.Collections.Generic;
using TinyTribe.Sim.Data;

namespace TinyTribe.Sim.Core
{
    /// <summary>
    /// The map: one flat array per layer, indexed by y * Width + x. North is +y.
    /// Holds state only; the rules live in Simulation.
    /// </summary>
    public sealed class World
    {
        public const short InfiniteCharges = -1;

        public readonly int Width;
        public readonly int Height;

        /// <summary>terrain_int per tile.</summary>
        public readonly byte[] Terrain;

        /// <summary>node_int per tile, 0 = none. A depleted node that will regrow keeps its int here.</summary>
        public readonly short[] Node;

        /// <summary>Yields left on the node: InfiniteCharges, or 0 when depleted.</summary>
        public readonly short[] NodeCharges;

        /// <summary>Harvest progress toward the next yield, shared by everyone working the tile.</summary>
        public readonly float[] NodeProgress;

        /// <summary>Tick at which a depleted node returns; 0 when it is not regrowing.</summary>
        public readonly long[] NodeRegrowTick;

        /// <summary>Index of the living agent on the tile, or -1.</summary>
        public readonly int[] AgentIndex;

        /// <summary>Index of the living animal on the tile, or -1.</summary>
        public readonly int[] AnimalIndex;

        /// <summary>Index of the building or building site covering the tile, or -1.</summary>
        public readonly int[] BuildingIndex;

        /// <summary>Tiles with a depleted node waiting to regrow.</summary>
        public readonly List<int> Regrowing = new List<int>();

        public int SpawnX, SpawnY;

        public World(int width, int height, TerrainDef fill)
        {
            Width = width;
            Height = height;
            int size = width * height;
            Terrain = new byte[size];
            Node = new short[size];
            NodeCharges = new short[size];
            NodeProgress = new float[size];
            NodeRegrowTick = new long[size];
            AgentIndex = new int[size];
            AnimalIndex = new int[size];
            BuildingIndex = new int[size];
            for (int i = 0; i < size; i++)
            {
                Terrain[i] = (byte)fill.Int;
                AgentIndex[i] = -1;
                AnimalIndex[i] = -1;
                BuildingIndex[i] = -1;
            }
            SpawnX = width / 2;
            SpawnY = height / 2;
        }

        public int Index(int x, int y) => y * Width + x;

        public bool InBounds(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

        public int TerrainAt(int x, int y) => Terrain[Index(x, y)];

        public int NodeAt(int x, int y) => Node[Index(x, y)];

        public int NodeChargesAt(int x, int y) => NodeCharges[Index(x, y)];

        public int AgentAt(int x, int y) => AgentIndex[Index(x, y)];

        /// <summary>True when the tile holds a node that can still be harvested (it also blocks movement).</summary>
        public bool HasActiveNode(int tile) => Node[tile] != 0 && NodeCharges[tile] != 0;

        public bool HasActiveNode(int x, int y) => InBounds(x, y) && HasActiveNode(Index(x, y));

        public void SetTerrain(int x, int y, TerrainDef terrain) => Terrain[Index(x, y)] = (byte)terrain.Int;

        /// <summary>Puts a fresh node on the tile, replacing whatever was there.</summary>
        public void PlaceNode(int x, int y, ResourceNodeDef node)
        {
            int tile = Index(x, y);
            RemoveNode(tile);
            Node[tile] = (short)node.Int;
            NodeCharges[tile] = node.Charges > 0 ? (short)node.Charges : InfiniteCharges;
        }

        public void RemoveNode(int tile)
        {
            if (NodeRegrowTick[tile] != 0) Regrowing.Remove(tile);
            Node[tile] = 0;
            NodeCharges[tile] = 0;
            NodeProgress[tile] = 0;
            NodeRegrowTick[tile] = 0;
        }
    }
}
