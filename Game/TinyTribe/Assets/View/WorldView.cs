using System.Collections.Generic;
using TinyTribe.Sim.Core;
using TinyTribe.Sim.Data;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace TinyTribe.View
{
    /// <summary>
    /// Draws the map: a tilemap for the terrain and one sprite per resource node on top.
    /// One tile is one world unit, and tile (x, y) covers the square from (x, y) to (x + 1, y + 1).
    /// Nodes are separate sprites so that tall ones (trees) can overlap the row behind them and
    /// villagers can walk behind or in front of them.
    /// </summary>
    public sealed class WorldView : MonoBehaviour
    {
        const int TerrainOrder = -10;
        const float SeeThroughOpacity = 0.4f;
        static readonly Color Faded = new Color(1f, 1f, 1f, 0.3f);

        enum Look : byte { None, Harvestable, Regrowing }

        World world;
        GameData data;
        Tilemap terrainMap;
        Tile[][] terrainTiles;
        byte[] terrainDrawn;
        Material material;
        Transform nodeRoot;
        Sprite[] nodeSprites;
        Sprite[] nodeDepletedSprites;
        SpriteRenderer[] nodeRenderers;
        Look[] nodeLooks;
        readonly List<Object> created = new List<Object>();
        readonly List<int> seeThrough = new List<int>();

        /// <summary>
        /// Sorting order for something standing on row y. Rows nearer the bottom of the screen get
        /// higher numbers, so they are drawn over what stands behind them.
        /// </summary>
        public static int RowOrder(int mapHeight, int y) => 2 * (mapHeight - y);

        public void Build(Simulation sim, IArt art, Material spriteMaterial)
        {
            world = sim.World;
            data = sim.Data;
            material = spriteMaterial;
            int width = world.Width, height = world.Height;

            gameObject.AddComponent<Grid>().cellSize = Vector3.one;
            var terrainObject = new GameObject("Terrain");
            terrainObject.transform.SetParent(transform, false);
            terrainMap = terrainObject.AddComponent<Tilemap>();
            var terrainRenderer = terrainObject.AddComponent<TilemapRenderer>();
            terrainRenderer.sortingOrder = TerrainOrder;
            if (material != null) terrainRenderer.sharedMaterial = material;

            int maxTerrain = 0, maxNode = 0;
            foreach (var terrain in data.Terrain) maxTerrain = Mathf.Max(maxTerrain, terrain.Int);
            foreach (var node in data.ResourceNodes) maxNode = Mathf.Max(maxNode, node.Int);

            terrainTiles = new Tile[maxTerrain + 1][];
            terrainDrawn = (byte[])world.Terrain.Clone();
            foreach (var terrain in data.Terrain)
            {
                terrainTiles[terrain.Int] = new Tile[art.TerrainVariants];
                for (int v = 0; v < art.TerrainVariants; v++) terrainTiles[terrain.Int][v] = MakeTile(art.Terrain(terrain, v));
            }
            var block = new TileBase[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    var variants = terrainTiles[world.Terrain[y * width + x]];
                    block[y * width + x] = variants[Variant(x, y, variants.Length)];
                }
            terrainMap.SetTilesBlock(new BoundsInt(0, 0, 0, width, height, 1), block);

            nodeSprites = new Sprite[maxNode + 1];
            nodeDepletedSprites = new Sprite[maxNode + 1];
            foreach (var node in data.ResourceNodes)
            {
                nodeSprites[node.Int] = art.Node(node);
                nodeDepletedSprites[node.Int] = art.NodeDepleted(node);
            }
            nodeRoot = new GameObject("Nodes").transform;
            nodeRoot.SetParent(transform, false);
            nodeRenderers = new SpriteRenderer[width * height];
            nodeLooks = new Look[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (world.Node[y * width + x] != 0) RefreshNode(x, y);
        }

        static int Variant(int x, int y, int count)
        {
            uint h = unchecked((uint)x * 73856093u ^ (uint)y * 19349663u);
            h ^= h >> 13;
            return (int)(h % (uint)count);
        }

        Tile MakeTile(Sprite sprite)
        {
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprite;
            tile.flags = TileFlags.None;
            tile.colliderType = Tile.ColliderType.None;
            tile.hideFlags = HideFlags.HideAndDontSave;
            created.Add(tile);
            return tile;
        }

        void OnDestroy()
        {
            foreach (var tile in created) Destroy(tile);
        }

        public bool HasTerrainTile(int x, int y) => terrainMap != null && terrainMap.HasTile(new Vector3Int(x, y, 0));

        /// <summary>True when the tile shows a node that can be harvested (a stump or a faded, regrowing one does not count).</summary>
        public bool IsNodeDrawn(int x, int y) =>
            nodeLooks != null && world.InBounds(x, y) && nodeLooks[world.Index(x, y)] == Look.Harvestable;

        /// <summary>Is the picture on this tile taller than one tile, so that it can hide what stands behind it?</summary>
        public bool IsTallNode(int x, int y)
        {
            if (nodeRenderers == null || !world.InBounds(x, y)) return false;
            var nodeRenderer = nodeRenderers[world.Index(x, y)];
            return nodeRenderer != null && nodeRenderer.enabled && nodeRenderer.sprite != null && nodeRenderer.sprite.bounds.size.y > 1.01f;
        }

        /// <summary>How solid the node on this tile is drawn: 1 normally, less while a villager stands behind it.</summary>
        public float NodeOpacity(int x, int y)
        {
            if (nodeRenderers == null || !world.InBounds(x, y)) return 1f;
            var nodeRenderer = nodeRenderers[world.Index(x, y)];
            return nodeRenderer == null ? 1f : nodeRenderer.color.a;
        }

        /// <summary>
        /// Makes tall nodes see-through while a villager stands in the row behind them, so nobody
        /// disappears behind a tree. A two-tile-wide picture also covers half of each neighbouring column.
        /// </summary>
        public void RevealBehind(IReadOnlyList<Agent> agents)
        {
            foreach (int tile in seeThrough) RefreshNode(tile % world.Width, tile / world.Width);
            seeThrough.Clear();

            for (int i = 0; i < agents.Count; i++)
            {
                var agent = agents[i];
                if (!agent.Alive) continue;
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = agent.X + dx, y = agent.Y - 1;
                    if (!IsTallNode(x, y)) continue;
                    int tile = world.Index(x, y);
                    var color = nodeRenderers[tile].color;
                    if (color.a <= SeeThroughOpacity) continue;
                    color.a = SeeThroughOpacity;
                    nodeRenderers[tile].color = color;
                    seeThrough.Add(tile);
                }
            }
        }

        /// <summary>Updates the nodes touched by the events of this frame.</summary>
        public void Apply(IReadOnlyList<SimEvent> events)
        {
            for (int i = 0; i < events.Count; i++)
            {
                var type = events[i].Type;
                if (type == SimEventType.NodeDepleted || type == SimEventType.NodeRegrown || type == SimEventType.NodeChanged)
                    RefreshNode(events[i].X, events[i].Y);
                else if (type == SimEventType.TerrainChanged) RefreshTerrain(events[i].X, events[i].Y);
            }
        }

        /// <summary>The terrain_int the map shows on a tile (farmland appears when a farm is finished).</summary>
        public int TerrainDrawn(int x, int y) => terrainDrawn[world.Index(x, y)];

        void RefreshTerrain(int x, int y)
        {
            int tile = world.Index(x, y);
            var variants = terrainTiles[world.Terrain[tile]];
            terrainMap.SetTile(new Vector3Int(x, y, 0), variants[Variant(x, y, variants.Length)]);
            terrainDrawn[tile] = world.Terrain[tile];
        }

        /// <summary>Redraws every node. For when more happened in a frame than its list of events holds.</summary>
        public void RefreshAllNodes()
        {
            for (int y = 0; y < world.Height; y++)
                for (int x = 0; x < world.Width; x++)
                {
                    int tile = world.Index(x, y);
                    if (world.Node[tile] != 0 || nodeLooks[tile] != Look.None) RefreshNode(x, y);
                    if (world.Terrain[tile] != terrainDrawn[tile]) RefreshTerrain(x, y);
                }
            seeThrough.Clear();
        }

        /// <summary>
        /// Redraws one node: its picture while it can be harvested; a stump, bare bush or faded
        /// picture while it regrows; nothing once it is used up for good.
        /// </summary>
        public void RefreshNode(int x, int y)
        {
            int tile = world.Index(x, y);
            int node = world.Node[tile];
            var picture = node != 0 ? nodeSprites[node] : null;
            var nodeRenderer = nodeRenderers[tile];
            if (picture == null)
            {
                if (nodeRenderer != null) nodeRenderer.enabled = false;
                nodeLooks[tile] = Look.None;
                return;
            }

            if (nodeRenderer == null)
            {
                var nodeObject = new GameObject("Node");
                nodeObject.transform.SetParent(nodeRoot, false);
                nodeObject.transform.position = new Vector3(x + 0.5f, y + 0.5f, 0f);
                nodeRenderer = nodeObject.AddComponent<SpriteRenderer>();
                nodeRenderer.sortingOrder = RowOrder(world.Height, y);
                if (material != null) nodeRenderer.sharedMaterial = material;
                nodeRenderers[tile] = nodeRenderer;
            }

            bool harvestable = world.NodeCharges[tile] != 0;
            var depleted = nodeDepletedSprites[node];
            nodeRenderer.sprite = harvestable || depleted == null ? picture : depleted;
            nodeRenderer.color = harvestable || depleted != null ? Color.white : Faded;
            nodeRenderer.enabled = true;
            nodeLooks[tile] = harvestable ? Look.Harvestable : Look.Regrowing;
        }
    }
}
