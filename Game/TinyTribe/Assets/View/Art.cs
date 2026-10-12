using System.Collections.Generic;
using TinyTribe.Sim.Data;
using UnityEngine;

namespace TinyTribe.View
{
    /// <summary>Where the view gets its pictures. Lets an art pack replace the generated placeholders.</summary>
    public interface IArt
    {
        int TerrainVariants { get; }
        Sprite Terrain(TerrainDef terrain, int variant);

        /// <summary>Null when the node has no picture of its own (water is drawn by the terrain).</summary>
        Sprite Node(ResourceNodeDef node);

        /// <summary>Picture of the node while it regrows (a stump, a bare bush). Null means: show the normal picture faded.</summary>
        Sprite NodeDepleted(ResourceNodeDef node);

        /// <summary>frame 0 is standing still; 1 and up are walking frames.</summary>
        Sprite Villager(int agentIndex, int faceDx, int faceDy, int frame, out bool flipX);

        /// <summary>Null means: lay the standing picture on its side.</summary>
        Sprite VillagerDead(int agentIndex);

        /// <summary>
        /// Picture of a whole building. Its pivot is the lower-left corner of the footprint, so a
        /// picture may be wider or taller than the footprint and overlap what stands behind it.
        /// </summary>
        Sprite Building(BuildingDef building);

        /// <summary>Flames drawn over a lit campfire, pivot at the bottom centre.</summary>
        int FlameFrames { get; }
        Sprite Flame(int frame);

        /// <summary>frame 0 is standing still; 1 and up are moving frames.</summary>
        Sprite Animal(AnimalDef animal, int faceDx, int faceDy, int frame, out bool flipX);

        Sprite SelectionRing { get; }
        Color VillagerColor(int agentIndex);
    }

    /// <summary>
    /// One texture that pictures are copied into at run time, each with a one-pixel repeated border
    /// so neighbouring pictures never bleed into each other when the camera zooms.
    /// </summary>
    public sealed class PixelAtlas
    {
        const int Size = 1024;
        const int Pad = 1;

        readonly Texture2D texture;
        readonly Color32[] pixels = new Color32[Size * Size];
        readonly List<Sprite> sprites = new List<Sprite>();
        int cursorX, cursorY, rowHeight;
        bool dirty;

        public PixelAtlas()
        {
            texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "TinyTribe art atlas",
                hideFlags = HideFlags.HideAndDontSave,
            };
        }

        /// <summary>
        /// Adds a picture. source holds width x height pixels, bottom row first.
        /// pivot is the point of the picture (0-1) that sits on the centre of its tile.
        /// </summary>
        public Sprite Add(string name, Color32[] source, int width, int height, Vector2 pivot)
        {
            int paddedWidth = width + 2 * Pad, paddedHeight = height + 2 * Pad;
            if (cursorX + paddedWidth > Size)
            {
                cursorX = 0;
                cursorY += rowHeight;
                rowHeight = 0;
            }
            if (cursorY + paddedHeight > Size) throw new System.InvalidOperationException("The art atlas is full.");

            int originX = cursorX + Pad, originY = cursorY + Pad;
            for (int y = -Pad; y < height + Pad; y++)
                for (int x = -Pad; x < width + Pad; x++)
                {
                    int sx = Mathf.Clamp(x, 0, width - 1), sy = Mathf.Clamp(y, 0, height - 1);
                    pixels[(originY + y) * Size + originX + x] = source[sy * width + sx];
                }
            cursorX += paddedWidth;
            rowHeight = Mathf.Max(rowHeight, paddedHeight);
            dirty = true;

            var sprite = Sprite.Create(texture, new Rect(originX, originY, width, height), pivot, 16f, 0, SpriteMeshType.FullRect);
            sprite.name = name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            sprites.Add(sprite);
            return sprite;
        }

        /// <summary>Uploads newly added pictures to the GPU. Cheap when nothing changed.</summary>
        public void Apply()
        {
            if (!dirty) return;
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            dirty = false;
        }

        public void Dispose()
        {
            foreach (var sprite in sprites)
                if (sprite != null) Object.Destroy(sprite);
            Object.Destroy(texture);
        }
    }

    /// <summary>
    /// Simple pictures drawn in code, 16x16 pixels each.
    /// Used when no art pack is installed and for anything a pack does not cover.
    /// </summary>
    public sealed class PlaceholderArt : IArt
    {
        const int Cell = 16;
        static readonly Color32 Clear = new Color32(0, 0, 0, 0);
        static readonly Vector2 Centre = new Vector2(0.5f, 0.5f);

        readonly PixelAtlas atlas;
        readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        // The picture being drawn.
        Color32[] canvas = new Color32[Cell * Cell];
        int canvasWidth = Cell, canvasHeight = Cell;

        public PlaceholderArt(PixelAtlas atlas)
        {
            this.atlas = atlas;
            SelectionRing = Make("ring", DrawRing);
        }

        public int TerrainVariants => 3;

        public Sprite SelectionRing { get; }

        public Sprite Terrain(TerrainDef terrain, int variant)
        {
            string key = "t" + terrain.Int + "_" + variant;
            return sprites.TryGetValue(key, out var sprite) ? sprite : Make(key, () => DrawTerrain(terrain.Id, (uint)(terrain.Int * 31 + variant)));
        }

        public Sprite Node(ResourceNodeDef node)
        {
            if (node.Id == "water_source" || node.Id == "fishing_spot") return null;
            string key = "n" + node.Int;
            return sprites.TryGetValue(key, out var sprite) ? sprite : Make(key, () => DrawNode(node.Id));
        }

        public Sprite NodeDepleted(ResourceNodeDef node) => null;

        public Sprite Villager(int agentIndex, int faceDx, int faceDy, int frame, out bool flipX)
        {
            flipX = faceDx < 0;
            int pose = faceDx != 0 ? 2 : faceDy > 0 ? 1 : 0;
            string key = "v" + agentIndex + "_" + pose;
            return sprites.TryGetValue(key, out var sprite) ? sprite : Make(key, () => DrawVillager(VillagerColor(agentIndex), pose));
        }

        public Sprite VillagerDead(int agentIndex) => null;

        public Color VillagerColor(int agentIndex) => ColorFor(agentIndex);

        /// <summary>A distinct colour per villager, used for the generated sprites and for labels in the windows.</summary>
        public static Color ColorFor(int agentIndex) =>
            Color.HSVToRGB(Mathf.Repeat(agentIndex * 0.618034f + 0.08f, 1f), 0.62f, 0.95f);

        public Sprite Building(BuildingDef building)
        {
            string key = "b" + building.Int;
            if (sprites.TryGetValue(key, out var sprite)) return sprite;
            int width = building.FootprintW * Cell, height = building.FootprintH * Cell;
            return Make(key, width, height, Vector2.zero, () => DrawBuilding(building, width, height));
        }

        public int FlameFrames => 3;

        public Sprite Flame(int frame)
        {
            frame = (frame % FlameFrames + FlameFrames) % FlameFrames;
            string key = "f" + frame;
            return sprites.TryGetValue(key, out var sprite) ? sprite : Make(key, 8, 12, new Vector2(0.5f, 0f), () => DrawFlame(frame));
        }

        /// <summary>One picture per species, seen from the side and facing right.</summary>
        public Sprite Animal(AnimalDef animal, int faceDx, int faceDy, int frame, out bool flipX)
        {
            flipX = faceDx < 0;
            string key = "a" + animal.Int;
            return sprites.TryGetValue(key, out var sprite) ? sprite : Make(key, () => DrawAnimal(animal.Id));
        }

        Sprite Make(string key, System.Action draw) => Make(key, Cell, Cell, Centre, draw);

        Sprite Make(string key, int width, int height, Vector2 pivot, System.Action draw)
        {
            if (canvas.Length != width * height) canvas = new Color32[width * height];
            canvasWidth = width;
            canvasHeight = height;
            for (int i = 0; i < canvas.Length; i++) canvas[i] = Clear;
            draw();
            var sprite = atlas.Add(key, canvas, width, height, pivot);
            sprites[key] = sprite;
            return sprite;
        }

        // ------------------------------------------------------------------ drawing helpers (x right, y up)

        void Set(int x, int y, Color32 color)
        {
            if ((uint)x < (uint)canvasWidth && (uint)y < (uint)canvasHeight) canvas[y * canvasWidth + x] = color;
        }

        void Fill(Color32 color)
        {
            for (int i = 0; i < canvas.Length; i++) canvas[i] = color;
        }

        void Box(int x, int y, int width, int height, Color32 color)
        {
            for (int j = 0; j < height; j++)
                for (int i = 0; i < width; i++) Set(x + i, y + j, color);
        }

        void Ellipse(float cx, float cy, float rx, float ry, Color32 color)
        {
            for (int y = 0; y < canvasHeight; y++)
                for (int x = 0; x < canvasWidth; x++)
                {
                    float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry;
                    if (dx * dx + dy * dy <= 1f) Set(x, y, color);
                }
        }

        void Speckle(uint seed, Color32 color, int count)
        {
            uint state = seed * 747796405u + 2891336453u;
            for (int i = 0; i < count; i++)
            {
                state = state * 1664525u + 1013904223u;
                int x = (int)((state >> 8) % Cell);
                state = state * 1664525u + 1013904223u;
                int y = (int)((state >> 8) % Cell);
                Set(x, y, color);
            }
        }

        static Color32 Rgb(byte r, byte g, byte b) => new Color32(r, g, b, 255);

        static Color32 Darker(Color32 color) => new Color32((byte)(color.r * 4 / 5), (byte)(color.g * 4 / 5), (byte)(color.b * 4 / 5), 255);

        // ------------------------------------------------------------------ pictures

        void DrawTerrain(string id, uint seed)
        {
            switch (id)
            {
                case "grass":
                    Fill(Rgb(104, 168, 72));
                    Speckle(seed, Rgb(88, 150, 60), 14);
                    Speckle(seed + 7, Rgb(124, 186, 86), 8);
                    break;
                case "forest":
                    Fill(Rgb(74, 134, 64));
                    Speckle(seed, Rgb(60, 114, 54), 18);
                    Speckle(seed + 7, Rgb(90, 150, 74), 8);
                    break;
                case "water":
                    Fill(Rgb(64, 124, 214));
                    for (int i = 0; i < 3; i++)
                    {
                        int y = (int)((seed * 5 + (uint)i * 6) % Cell), x = (int)((seed * 3 + (uint)i * 7) % 11);
                        Box(x, y, 4, 1, Rgb(112, 164, 236));
                    }
                    break;
                case "mountain":
                    Fill(Rgb(122, 122, 132));
                    Speckle(seed, Rgb(96, 96, 106), 22);
                    Speckle(seed + 3, Rgb(152, 152, 162), 10);
                    break;
                case "cave":
                    Fill(Rgb(92, 80, 72));
                    Speckle(seed, Rgb(72, 62, 56), 20);
                    break;
                case "sand":
                    Fill(Rgb(222, 200, 130));
                    Speckle(seed, Rgb(204, 182, 112), 12);
                    break;
                case "snow":
                    Fill(Rgb(236, 242, 248));
                    Speckle(seed, Rgb(212, 224, 236), 10);
                    break;
                case "farmland":
                    Fill(Rgb(140, 96, 52));
                    for (int y = 1; y < Cell; y += 4) Box(0, y, Cell, 1, Rgb(112, 74, 40));
                    break;
                case "out_of_bounds":
                    Fill(Rgb(20, 20, 24));
                    break;
                default:
                    DrawMissing();
                    break;
            }
        }

        void DrawRock(Color32 dark, Color32 body, Color32 light)
        {
            Ellipse(8f, 5.5f, 6.5f, 4.5f, dark);
            Ellipse(8f, 6.5f, 5.5f, 3.8f, body);
            Ellipse(6.5f, 8f, 2.2f, 1.2f, light);
        }

        void Dots(Color32 color, params int[] xy)
        {
            for (int i = 0; i + 1 < xy.Length; i += 2) Box(xy[i], xy[i + 1], 2, 2, color);
        }

        void DrawNode(string id)
        {
            var trunk = Rgb(112, 76, 42);
            switch (id)
            {
                case "pine_tree":
                    Box(7, 0, 2, 4, trunk);
                    for (int y = 3; y < 15; y++)
                    {
                        int half = 1 + (14 - y) / 2;
                        Box(8 - half, y, half * 2, 1, y % 4 == 3 ? Rgb(30, 86, 48) : Rgb(40, 108, 58));
                    }
                    break;
                case "oak_tree":
                    Box(7, 0, 2, 6, trunk);
                    Ellipse(8f, 10f, 6.5f, 5.2f, Rgb(58, 130, 52));
                    Ellipse(8f, 10.5f, 5.4f, 4.2f, Rgb(76, 154, 62));
                    Ellipse(6f, 12f, 2.2f, 1.6f, Rgb(104, 180, 80));
                    break;
                case "surface_rock":
                    DrawRock(Rgb(96, 96, 106), Rgb(142, 142, 152), Rgb(178, 178, 188));
                    break;
                case "copper_vein":
                    DrawRock(Rgb(88, 88, 98), Rgb(126, 126, 136), Rgb(156, 156, 166));
                    Dots(Rgb(216, 126, 58), 4, 5, 8, 7, 10, 4);
                    break;
                case "coal_seam":
                    DrawRock(Rgb(70, 70, 78), Rgb(108, 108, 118), Rgb(136, 136, 146));
                    Dots(Rgb(26, 26, 30), 4, 5, 8, 7, 10, 4);
                    break;
                case "iron_vein":
                    DrawRock(Rgb(88, 88, 98), Rgb(126, 126, 136), Rgb(156, 156, 166));
                    Dots(Rgb(208, 216, 230), 4, 5, 8, 7, 10, 4);
                    break;
                case "gold_vein":
                    DrawRock(Rgb(88, 88, 98), Rgb(126, 126, 136), Rgb(156, 156, 166));
                    Dots(Rgb(248, 212, 60), 4, 5, 8, 7, 10, 4);
                    break;
                case "marble_deposit":
                    DrawRock(Rgb(170, 170, 182), Rgb(232, 232, 238), Rgb(252, 252, 255));
                    Box(5, 5, 4, 1, Rgb(176, 176, 190));
                    Box(8, 7, 3, 1, Rgb(176, 176, 190));
                    break;
                case "berry_bush":
                    Ellipse(8f, 6f, 6.5f, 5.2f, Rgb(44, 118, 58));
                    Ellipse(8f, 6.5f, 5.5f, 4.2f, Rgb(62, 146, 72));
                    Dots(Rgb(216, 52, 70), 4, 6, 9, 8, 10, 4, 6, 3);
                    break;
                case "herb_patch":
                    Dots(Rgb(92, 182, 92), 3, 2, 7, 4, 11, 2, 5, 6, 9, 7);
                    Set(8, 6, Rgb(250, 250, 250));
                    Set(4, 4, Rgb(250, 250, 250));
                    break;
                case "cotton_plant":
                    Box(5, 0, 1, 7, Rgb(70, 140, 70));
                    Box(10, 0, 1, 6, Rgb(70, 140, 70));
                    Ellipse(5.5f, 8.5f, 2.2f, 2.2f, Rgb(250, 250, 250));
                    Ellipse(10.5f, 7.5f, 2.2f, 2.2f, Rgb(250, 250, 250));
                    break;
                case "wild_wheat":
                    for (int x = 3; x <= 12; x += 3)
                    {
                        Box(x, 0, 1, 9, Rgb(196, 168, 70));
                        Box(x - 1, 8, 3, 4, Rgb(232, 202, 88));
                    }
                    break;
                case "wild_carrot":
                    Box(6, 6, 1, 5, Rgb(70, 160, 70));
                    Box(8, 6, 1, 6, Rgb(70, 160, 70));
                    Box(10, 6, 1, 4, Rgb(70, 160, 70));
                    Ellipse(8f, 4f, 2.6f, 2.6f, Rgb(238, 140, 46));
                    break;
                case "wild_potato":
                    Dots(Rgb(72, 150, 72), 4, 7, 8, 8, 10, 6);
                    Ellipse(6f, 3.5f, 2.6f, 2f, Rgb(160, 118, 74));
                    Ellipse(10.5f, 3f, 2.2f, 1.8f, Rgb(146, 106, 66));
                    break;
                case "item_pile":
                    Ellipse(8f, 5f, 5f, 4.2f, Rgb(150, 110, 66));
                    Box(7, 9, 2, 2, Rgb(120, 86, 50));
                    break;
                case "crop_growing":
                    Dots(Rgb(110, 190, 90), 3, 2, 8, 3, 12, 2);
                    break;
                case "crop_mature":
                    for (int x = 3; x <= 12; x += 4)
                    {
                        Box(x, 0, 1, 8, Rgb(110, 170, 70));
                        Box(x - 1, 7, 3, 4, Rgb(226, 204, 88));
                    }
                    break;
                default:
                    DrawMissing();
                    break;
            }
        }

        void DrawBuilding(BuildingDef building, int width, int height)
        {
            switch (building.Id)
            {
                case "campfire":
                    Ellipse(8f, 5.5f, 7.2f, 5f, Rgb(104, 104, 114));
                    Ellipse(8f, 6f, 6.2f, 4.2f, Rgb(150, 150, 160));
                    Ellipse(8f, 5.8f, 4.6f, 3f, Rgb(58, 48, 42));
                    Box(4, 5, 8, 2, Rgb(124, 84, 46));
                    Box(7, 3, 2, 6, Rgb(98, 64, 36));
                    break;
                case "basic_chest":
                    Box(2, 1, 12, 10, Rgb(84, 54, 30));
                    Box(3, 2, 10, 5, Rgb(150, 100, 54));
                    Box(3, 8, 10, 2, Rgb(176, 124, 70));
                    Box(7, 5, 2, 3, Rgb(238, 200, 80));
                    break;
                case "stone_well":
                    Ellipse(8f, 7f, 7f, 6f, Rgb(100, 100, 110));
                    Ellipse(8f, 7.5f, 6f, 5f, Rgb(152, 152, 162));
                    Ellipse(8f, 7.5f, 4f, 3.2f, Rgb(58, 112, 204));
                    Box(6, 8, 3, 1, Rgb(130, 176, 238));
                    break;
                case "hide_tent":
                    DrawTent(width, height);
                    break;
                case "garden_plot":
                    // A scarecrow on its post.
                    Box(7, 0, 2, 11, Rgb(124, 84, 46));
                    Box(3, 7, 10, 2, Rgb(150, 100, 54));
                    Box(5, 4, 6, 5, Rgb(196, 150, 70));
                    Ellipse(8f, 11.5f, 2.6f, 2.4f, Rgb(226, 196, 120));
                    Box(4, 13, 8, 1, Rgb(92, 64, 38));
                    Box(6, 14, 4, 2, Rgb(92, 64, 38));
                    break;
                case "study_circle":
                    // A ring of stones around trodden ground, with a log to sit on.
                    Ellipse(width * 0.5f, height * 0.45f, width * 0.47f, height * 0.42f, Rgb(112, 114, 124));
                    Ellipse(width * 0.5f, height * 0.47f, width * 0.40f, height * 0.35f, Rgb(160, 162, 170));
                    Ellipse(width * 0.5f, height * 0.47f, width * 0.33f, height * 0.28f, Rgb(206, 176, 120));
                    Box(width / 2 - 4, height / 2 - 3, 8, 3, Rgb(124, 84, 46));
                    Box(width / 2 - 3, height / 2, 6, 1, Rgb(150, 100, 54));
                    break;
                default:
                    DrawHouse(building.Category, width, height);
                    break;
            }
        }

        void DrawTent(int width, int height)
        {
            float top = height * 0.85f, centre = width * 0.5f;
            for (int y = 0; y < (int)top; y++)
            {
                int half = Mathf.RoundToInt((top - y) / top * (width * 0.46f));
                Box((int)centre - half, y, half * 2, 1, y % 5 == 4 ? Rgb(176, 136, 84) : Rgb(204, 164, 104));
                Box((int)centre - half, y, 1, 1, Rgb(92, 64, 38));
                Box((int)centre + half - 1, y, 1, 1, Rgb(92, 64, 38));
            }
            for (int y = 0; y < height * 0.4f; y++)
            {
                int half = Mathf.RoundToInt((height * 0.4f - y) / (height * 0.4f) * (width * 0.14f));
                Box((int)centre - half, y, half * 2, 1, Rgb(46, 34, 28));
            }
            Box((int)centre - 1, (int)top - 1, 2, 3, Rgb(92, 64, 38));
        }

        /// <summary>A plain house for every building without a picture of its own: walls, a roof and a door.</summary>
        void DrawHouse(string category, int width, int height)
        {
            Color32 wall, roof;
            switch (category)
            {
                case "residential": wall = Rgb(172, 124, 76); roof = Rgb(170, 70, 56); break;
                case "storage": wall = Rgb(190, 160, 96); roof = Rgb(120, 86, 50); break;
                case "production": wall = Rgb(150, 150, 158); roof = Rgb(88, 88, 98); break;
                case "community": wall = Rgb(206, 198, 180); roof = Rgb(64, 104, 170); break;
                default: wall = Rgb(140, 140, 150); roof = Rgb(96, 96, 106); break;
            }
            int wallTop = Mathf.Max(6, height * 9 / 16);
            Box(1, 0, width - 2, wallTop, wall);
            Box(1, 0, 1, wallTop, Rgb(60, 44, 34));
            Box(width - 2, 0, 1, wallTop, Rgb(60, 44, 34));
            for (int y = wallTop; y < height; y++)
            {
                int inset = (y - wallTop) * (width / 2 - 2) / Mathf.Max(1, height - wallTop);
                Box(inset, y, width - 2 * inset, 1, (y - wallTop) % 3 == 2 ? Darker(roof) : roof);
            }
            Box(width / 2 - 2, 0, 4, Mathf.Min(7, wallTop - 1), Rgb(72, 48, 30));
            if (width >= 32)
            {
                Box(4, wallTop - 6, 4, 4, Rgb(150, 196, 232));
                Box(width - 8, wallTop - 6, 4, 4, Rgb(150, 196, 232));
            }
        }

        void DrawFlame(int frame)
        {
            float height = 7f + frame * 2f;
            Ellipse(4f, height * 0.5f, 3.4f - frame * 0.4f, height * 0.5f, Rgb(232, 96, 32));
            Ellipse(4f, height * 0.33f, 1.9f, height * 0.33f, Rgb(250, 204, 76));
        }

        void DrawAnimal(string id)
        {
            var dark = Rgb(40, 34, 30);
            switch (id)
            {
                case "rabbit":
                    Ellipse(7f, 4f, 4.2f, 3.2f, Rgb(196, 180, 160));
                    Ellipse(11f, 6.5f, 2.6f, 2.3f, Rgb(210, 196, 176));
                    Box(10, 8, 1, 4, Rgb(196, 180, 160));
                    Box(12, 8, 1, 3, Rgb(196, 180, 160));
                    Box(2, 4, 2, 2, Rgb(250, 250, 250));
                    Set(12, 7, dark);
                    break;
                case "deer":
                    Box(3, 0, 1, 5, dark);
                    Box(5, 0, 1, 5, dark);
                    Box(9, 0, 1, 5, dark);
                    Box(11, 0, 1, 5, dark);
                    Ellipse(7f, 7f, 5.5f, 3f, Rgb(170, 114, 64));
                    Box(11, 8, 2, 4, Rgb(170, 114, 64));
                    Ellipse(13f, 12f, 2.2f, 1.7f, Rgb(184, 128, 76));
                    Box(12, 14, 1, 2, Rgb(226, 212, 180));
                    Box(14, 14, 1, 2, Rgb(226, 212, 180));
                    Box(1, 8, 2, 2, Rgb(250, 250, 250));
                    Set(14, 12, dark);
                    break;
                case "wolf":
                    Box(3, 0, 1, 4, dark);
                    Box(5, 0, 1, 4, dark);
                    Box(9, 0, 1, 4, dark);
                    Box(11, 0, 1, 4, dark);
                    Ellipse(7f, 6f, 5.6f, 3f, Rgb(118, 122, 134));
                    Ellipse(12.5f, 8f, 2.8f, 2.3f, Rgb(134, 138, 150));
                    Box(11, 10, 1, 2, Rgb(118, 122, 134));
                    Box(13, 10, 1, 2, Rgb(118, 122, 134));
                    Box(0, 7, 3, 1, Rgb(96, 100, 112));
                    Set(13, 8, Rgb(248, 220, 60));
                    break;
                case "bear":
                    Box(3, 0, 2, 4, dark);
                    Box(10, 0, 2, 4, dark);
                    Ellipse(7.5f, 6.5f, 6.8f, 5f, Rgb(100, 68, 44));
                    Ellipse(12.5f, 10.5f, 3.2f, 3f, Rgb(118, 82, 54));
                    Box(10, 13, 2, 2, Rgb(100, 68, 44));
                    Box(14, 13, 2, 2, Rgb(100, 68, 44));
                    Set(13, 11, dark);
                    Box(14, 9, 2, 1, Rgb(60, 44, 34));
                    break;
                default:
                    DrawMissing();
                    break;
            }
        }

        /// <summary>pose 0 = facing the camera, 1 = facing away, 2 = facing right (flipped for left).</summary>
        void DrawVillager(Color shirtColor, int pose)
        {
            Color32 shirt = shirtColor;
            Color32 shade = shirtColor * 0.72f;
            shade.a = 255;
            var skin = Rgb(242, 204, 164);
            var hair = Rgb(76, 52, 36);
            var dark = Rgb(48, 48, 66);

            Box(6, 0, 2, 3, dark);
            Box(9, 0, 2, 3, dark);
            Box(5, 3, 7, 6, shirt);
            Box(4, 4, 1, 4, shade);
            Box(12, 4, 1, 4, shade);
            Ellipse(8.5f, 11.5f, 3.5f, 3.5f, skin);
            Box(5, 13, 7, 2, hair);
            if (pose == 0)
            {
                Set(7, 11, dark);
                Set(10, 11, dark);
            }
            else if (pose == 1)
            {
                Ellipse(8.5f, 12f, 3.5f, 3f, hair);
            }
            else
            {
                Set(10, 11, dark);
                Box(5, 10, 2, 4, hair);
            }
        }

        void DrawRing()
        {
            var white = Rgb(255, 255, 255);
            for (int y = 0; y < Cell; y++)
                for (int x = 0; x < Cell; x++)
                {
                    float dx = (x + 0.5f - 8f) / 7.5f, dy = (y + 0.5f - 3.5f) / 3.2f;
                    float d = dx * dx + dy * dy;
                    if (d <= 1f && d >= 0.55f) Set(x, y, white);
                }
        }

        /// <summary>Loud checkerboard so missing art is obvious.</summary>
        void DrawMissing()
        {
            for (int y = 0; y < Cell; y++)
                for (int x = 0; x < Cell; x++)
                    Set(x, y, ((x / 4 + y / 4) & 1) == 0 ? Rgb(255, 0, 255) : Rgb(20, 20, 20));
        }
    }
}
