using System.Collections.Generic;
using TinyTribe.Sim.Data;
using UnityEngine;

namespace TinyTribe.View
{
    /// <summary>
    /// Pictures from the Ninja Adventure asset pack by Pixel-Boy and AAA (CC0), installed by
    /// tools/install_art.py into Assets/Resources/Art/NinjaAdventure. The sheets are cut into tiles
    /// here; cell coordinates are column and row counted from the top-left of the sheet.
    /// Anything the pack does not cover falls back to the generated placeholder.
    /// </summary>
    public sealed class PackArt : IArt
    {
        const string Root = "Art/NinjaAdventure/";
        const int Tile = 16;
        const int WalkFrames = 4;

        /// <summary>Keep in step with tools/install_art.py.</summary>
        static readonly string[] Characters =
        {
            "Villager", "Villager2", "Villager3", "Villager4", "Villager5", "Villager6",
            "Woman", "Boy", "OldMan", "Hunter", "Monk", "Cavegirl",
        };

        readonly struct Cut
        {
            public readonly string Sheet;
            public readonly int X, Y, Width, Height;

            public Cut(string sheet, int x, int y, int width = 1, int height = 1)
            {
                Sheet = sheet;
                X = x;
                Y = y;
                Width = width;
                Height = height;
            }
        }

        const string Floor = "TilesetFloor", Nature = "TilesetNature", Water = "TilesetWater", Relief = "TilesetRelief";

        // Added with buildings and animals; a project whose art was installed before then simply falls back for these.
        const string Camp = "TilesetCamp", Element = "TilesetElement", Fire = "Fire", Bear = "Animals/Bear";
        const int FireFrameWidth = 8, FirstFlame = 1, Flames = 7;
        static readonly string[] OptionalSheets = { Camp, Element, Fire, Bear, "Animals/Wolf", "Animals/Deer", "Animals/Rabbit" };

        /// <summary>Wider or taller than the footprint is fine: the picture is centred on it and stands on its lower edge.</summary>
        static readonly Dictionary<string, Cut> BuildingCuts = new Dictionary<string, Cut>
        {
            ["hide_tent"] = new Cut(Camp, 4, 0, 3, 3),
            ["basic_chest"] = new Cut(Camp, 4, 8),
            ["stone_well"] = new Cut(Element, 6, 1),
            ["garden_plot"] = new Cut(Element, 15, 0),
            ["study_circle"] = new Cut(Camp, 12, 3, 2, 2),
        };

        /// <summary>Two frames side by side, standing and running, seen from the side and facing right. The pack has no wolf, deer or rabbit.</summary>
        static readonly Dictionary<string, string> SideViewAnimals = new Dictionary<string, string>
        {
            ["wolf"] = "Animals/Wolf",
            ["deer"] = "Animals/Deer",
            ["rabbit"] = "Animals/Rabbit",
        };

        static readonly Dictionary<string, Cut[]> TerrainCuts = new Dictionary<string, Cut[]>
        {
            ["grass"] = new[] { new Cut(Floor, 0, 12), new Cut(Floor, 0, 12), new Cut(Floor, 0, 12), new Cut(Floor, 4, 12), new Cut(Floor, 2, 12), new Cut(Floor, 1, 12) },
            ["forest"] = new[] { new Cut(Floor, 11, 12), new Cut(Floor, 11, 12), new Cut(Floor, 11, 12), new Cut(Floor, 15, 12), new Cut(Floor, 13, 12), new Cut(Floor, 12, 12) },
            ["sand"] = new[] { new Cut(Floor, 1, 1) },
            ["snow"] = new[] { new Cut(Floor, 0, 19), new Cut(Floor, 0, 19), new Cut(Floor, 0, 19), new Cut(Floor, 2, 19), new Cut(Floor, 3, 19), new Cut(Floor, 1, 19) },
            ["farmland"] = new[] { new Cut(Floor, 16, 16) },
            ["cave"] = new[] { new Cut(Floor, 11, 19), new Cut(Floor, 11, 19), new Cut(Floor, 11, 19), new Cut(Floor, 14, 19), new Cut(Floor, 13, 19), new Cut(Floor, 12, 19) },
            ["mountain"] = new[] { new Cut(Relief, 5, 1) },
            ["water"] = new[] { new Cut(Water, 1, 1), new Cut(Water, 1, 1), new Cut(Water, 1, 1), new Cut(Water, 1, 1), new Cut(Water, 11, 2), new Cut(Water, 11, 2) },
        };

        static readonly Dictionary<string, Cut> NodeCuts = new Dictionary<string, Cut>
        {
            ["pine_tree"] = new Cut(Nature, 2, 0, 2, 2),
            ["oak_tree"] = new Cut(Nature, 0, 0, 2, 2),
            ["surface_rock"] = new Cut(Nature, 18, 9),
            ["copper_vein"] = new Cut(Nature, 0, 15),
            ["coal_seam"] = new Cut(Nature, 8, 12),
            ["iron_vein"] = new Cut(Nature, 1, 15),
            ["gold_vein"] = new Cut(Nature, 3, 16),
            ["marble_deposit"] = new Cut(Nature, 10, 13),
            ["berry_bush"] = new Cut(Nature, 4, 14),
            ["herb_patch"] = new Cut(Nature, 8, 10),
            ["cotton_plant"] = new Cut(Nature, 6, 11),
            ["wild_wheat"] = new Cut(Nature, 7, 11),
            ["wild_carrot"] = new Cut(Nature, 0, 14),
            ["wild_potato"] = new Cut(Nature, 2, 11),
            ["crop_growing"] = new Cut(Nature, 2, 11),
            ["crop_mature"] = new Cut(Nature, 7, 11),
            ["item_pile"] = new Cut("Bag", 0, 0),
        };

        static readonly Dictionary<string, Cut> DepletedCuts = new Dictionary<string, Cut>
        {
            ["pine_tree"] = new Cut(Nature, 4, 8),
            ["oak_tree"] = new Cut(Nature, 4, 8),
            ["berry_bush"] = new Cut(Nature, 6, 14),
        };

        readonly PixelAtlas atlas;
        readonly IArt fallback;
        readonly Dictionary<string, Color32[]> sheetPixels = new Dictionary<string, Color32[]>();
        readonly Dictionary<string, Vector2Int> sheetSizes = new Dictionary<string, Vector2Int>();
        readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        readonly Sprite[][] villagers = new Sprite[Characters.Length][];
        readonly Sprite[] villagersDead = new Sprite[Characters.Length];

        PackArt(PixelAtlas atlas, IArt fallback)
        {
            this.atlas = atlas;
            this.fallback = fallback;
        }

        /// <summary>The pack art, or null when the pack is not installed (or was imported unreadable).</summary>
        public static PackArt TryLoad(PixelAtlas atlas, IArt fallback)
        {
            var art = new PackArt(atlas, fallback);
            try
            {
                foreach (string sheet in new[] { Floor, Nature, Water, Relief, "Bag" })
                    if (!art.LoadSheet(sheet)) return null;
                foreach (string character in Characters)
                    foreach (string kind in new[] { "Walk", "Idle", "Dead" })
                        if (!art.LoadSheet("Characters/" + character + "_" + kind)) return null;
                foreach (string sheet in OptionalSheets) art.LoadSheet(sheet);
            }
            catch (System.Exception error)
            {
                Debug.LogWarning("Art pack found but not usable (reimport Assets/Resources/Art): " + error.Message);
                return null;
            }
            return art;
        }

        bool LoadSheet(string sheet)
        {
            var texture = Resources.Load<Texture2D>(Root + sheet);
            if (texture == null) return false;
            sheetPixels[sheet] = texture.GetPixels32();
            sheetSizes[sheet] = new Vector2Int(texture.width, texture.height);
            return true;
        }

        public int TerrainVariants => 6;

        public Sprite SelectionRing => fallback.SelectionRing;

        public Color VillagerColor(int agentIndex) => fallback.VillagerColor(agentIndex);

        public Sprite Terrain(TerrainDef terrain, int variant)
        {
            if (!TerrainCuts.TryGetValue(terrain.Id, out var cuts)) return fallback.Terrain(terrain, variant % fallback.TerrainVariants);
            return CutSprite("t:" + terrain.Id + ":" + variant % cuts.Length, cuts[variant % cuts.Length]);
        }

        public Sprite Node(ResourceNodeDef node) =>
            NodeCuts.TryGetValue(node.Id, out var cut) ? CutSprite("n:" + node.Id, cut) : fallback.Node(node);

        public Sprite NodeDepleted(ResourceNodeDef node) =>
            DepletedCuts.TryGetValue(node.Id, out var cut) ? CutSprite("d:" + node.Id, cut) : null;

        public Sprite Building(BuildingDef building)
        {
            if (!BuildingCuts.TryGetValue(building.Id, out var cut) || !sheetPixels.ContainsKey(cut.Sheet)) return fallback.Building(building);
            var pivot = new Vector2((cut.Width - building.FootprintW) * 0.5f / cut.Width, 0f);
            return CutSprite("b:" + building.Id, cut, pivot);
        }

        public int FlameFrames => sheetPixels.ContainsKey(Fire) ? Flames : fallback.FlameFrames;

        public Sprite Flame(int frame)
        {
            if (!sheetPixels.ContainsKey(Fire)) return fallback.Flame(frame);
            int which = FirstFlame + (frame % Flames + Flames) % Flames;
            return CutPixels("f:" + which, Fire, which * FireFrameWidth, 0, FireFrameWidth, sheetSizes[Fire].y, new Vector2(0.5f, 0f));
        }

        public Sprite Animal(AnimalDef animal, int faceDx, int faceDy, int frame, out bool flipX)
        {
            if (animal.Id == "bear" && sheetPixels.ContainsKey(Bear))
            {
                // Monster sheets hold one column per direction: down, left, up, right; one row per walk frame.
                flipX = false;
                int direction = faceDy < 0 ? 0 : faceDy > 0 ? 2 : faceDx < 0 ? 1 : 3;
                int row = frame <= 0 ? 0 : (frame - 1) % 4;
                return CutSprite("a:bear:" + row + ":" + direction, new Cut(Bear, direction, row));
            }
            if (SideViewAnimals.TryGetValue(animal.Id, out string sheet) && sheetPixels.ContainsKey(sheet))
            {
                flipX = faceDx < 0;
                var size = sheetSizes[sheet];
                int frameWidth = size.x / 2, which = frame <= 0 ? 0 : frame % 2;
                // Feet on the lower edge of the tile, whatever the height of the picture.
                return CutPixels("a:" + animal.Id + ":" + which, sheet, which * frameWidth, 0, frameWidth, size.y, new Vector2(0.5f, 0.5f * Tile / size.y));
            }
            return fallback.Animal(animal, faceDx, faceDy, frame, out flipX);
        }

        /// <summary>Sheets hold one column per direction: down, up, left, right.</summary>
        public Sprite Villager(int agentIndex, int faceDx, int faceDy, int frame, out bool flipX)
        {
            flipX = false;
            int character = agentIndex % Characters.Length;
            int direction = faceDy < 0 ? 0 : faceDy > 0 ? 1 : faceDx < 0 ? 2 : 3;
            var set = villagers[character] ?? (villagers[character] = CutCharacter(character));
            int row = frame <= 0 ? 0 : 1 + (frame - 1) % WalkFrames;
            return set[row * 4 + direction];
        }

        public Sprite VillagerDead(int agentIndex)
        {
            int character = agentIndex % Characters.Length;
            if (villagers[character] == null) villagers[character] = CutCharacter(character);
            return villagersDead[character];
        }

        /// <summary>Row 0 is standing still, rows 1-4 are the walk frames; four directions per row.</summary>
        Sprite[] CutCharacter(int character)
        {
            string name = Characters[character];
            var set = new Sprite[(1 + WalkFrames) * 4];
            for (int direction = 0; direction < 4; direction++)
            {
                set[direction] = CutSprite("c:" + name + ":idle:" + direction, new Cut("Characters/" + name + "_Idle", direction, 0));
                for (int frame = 0; frame < WalkFrames; frame++)
                    set[(1 + frame) * 4 + direction] = CutSprite("c:" + name + ":walk:" + frame + ":" + direction, new Cut("Characters/" + name + "_Walk", direction, frame));
            }
            villagersDead[character] = CutSprite("c:" + name + ":dead", new Cut("Characters/" + name + "_Dead", 0, 0));
            return set;
        }

        // Tall pictures stand on their tile: the pivot is the centre of the bottom tile.
        Sprite CutSprite(string key, Cut cut) => CutSprite(key, cut, new Vector2(0.5f, 0.5f / cut.Height));

        Sprite CutSprite(string key, Cut cut, Vector2 pivot) =>
            CutPixels(key, cut.Sheet, cut.X * Tile, cut.Y * Tile, cut.Width * Tile, cut.Height * Tile, pivot);

        /// <summary>A rectangle of a sheet, in pixels counted from its top-left corner.</summary>
        Sprite CutPixels(string key, string sheet, int left, int top, int width, int height, Vector2 pivot)
        {
            if (sprites.TryGetValue(key, out var sprite)) return sprite;
            var source = sheetPixels[sheet];
            var size = sheetSizes[sheet];
            // Sheets are stored bottom row first.
            int bottom = size.y - top - height;
            var block = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    block[y * width + x] = source[(bottom + y) * size.x + left + x];

            sprite = atlas.Add(key, block, width, height, pivot);
            sprites[key] = sprite;
            return sprite;
        }
    }
}
