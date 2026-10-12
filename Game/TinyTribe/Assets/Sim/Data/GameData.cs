using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;

namespace TinyTribe.Sim.Data
{
    /// <summary>
    /// All design data for the simulation, loaded from the JSON files that
    /// ML/export_data.py writes from the workbook. Immutable after loading.
    /// </summary>
    public sealed class GameData
    {
        public IReadOnlyList<ItemDef> Items { get; private set; }
        public IReadOnlyList<EquipmentDef> Equipment { get; private set; }
        public IReadOnlyList<RecipeDef> Recipes { get; private set; }
        public IReadOnlyList<BuildingDef> Buildings { get; private set; }
        public IReadOnlyList<TerrainDef> Terrain { get; private set; }
        public IReadOnlyList<ResourceNodeDef> ResourceNodes { get; private set; }
        public IReadOnlyList<NeedDef> Needs { get; private set; }
        public IReadOnlyList<ActionDef> Actions { get; private set; }
        public IReadOnlyList<ActionArgDef> ActionArgs { get; private set; }

        /// <summary>Seasons in calendar order (spring first).</summary>
        public IReadOnlyList<SeasonDef> Seasons { get; private set; }

        public IReadOnlyList<WorldGenDef> WorldGen { get; private set; }
        public IReadOnlyList<AnimalDef> Animals { get; private set; }
        public IReadOnlyList<RoleDef> Roles { get; private set; }
        public IReadOnlyList<WeatherDef> Weather { get; private set; }

        /// <summary>In the order of Sim Tech. The place of a technology in this list is its option in a research vote.</summary>
        public IReadOnlyList<TechDef> Tech { get; private set; }
        public IReadOnlyList<RewardDef> Rewards { get; private set; }
        public IReadOnlyList<AchievementDef> Achievements { get; private set; }

        /// <summary>The workbook the data was exported from, and its SHA-256: which numbers a run was played with.</summary>
        public string Workbook { get; private set; } = "";
        public string WorkbookSha256 { get; private set; } = "";

        public IReadOnlyList<CropDef> Crops { get; private set; }
        public IReadOnlyList<SicknessDef> Sicknesses { get; private set; }

        public IReadOnlyDictionary<string, ItemDef> ItemById { get; private set; }
        public IReadOnlyDictionary<string, BuildingDef> BuildingById { get; private set; }

        readonly Dictionary<string, object> config = new Dictionary<string, object>();
        readonly Dictionary<string, object> modifiers = new Dictionary<string, object>();
        readonly Dictionary<string, ResourceNodeDef> nodeById = new Dictionary<string, ResourceNodeDef>();
        readonly Dictionary<string, TerrainDef> terrainById = new Dictionary<string, TerrainDef>();
        readonly Dictionary<string, NeedDef> needById = new Dictionary<string, NeedDef>();
        readonly Dictionary<string, int> actionIdByName = new Dictionary<string, int>();
        readonly Dictionary<string, AnimalDef> animalById = new Dictionary<string, AnimalDef>();
        readonly Dictionary<string, int> roleIntById = new Dictionary<string, int>();
        readonly Dictionary<string, int> argByRefId = new Dictionary<string, int>();
        ItemDef[] itemByInt;
        EquipmentDef[] equipmentByItemInt;
        ResourceNodeDef[] nodeByInt;
        TerrainDef[] terrainByInt;

        /// <summary>Loads the data files from a folder on disk.</summary>
        public static GameData Load(string dataDirectory) => Load(file =>
        {
            var path = Path.Combine(dataDirectory, file);
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing data file. Run ML/export_data.py.", path);
            return File.ReadAllText(path);
        });

        /// <summary>
        /// Loads the data files through a reader that returns the text of a file by name.
        /// Android keeps StreamingAssets inside the APK, where plain file access does not work.
        /// </summary>
        public static GameData Load(Func<string, string> readFile)
        {
            var d = new GameData
            {
                Items = Read<ItemDef>(readFile, "items.json"),
                Equipment = Read<EquipmentDef>(readFile, "equipment.json"),
                Recipes = Read<RecipeDef>(readFile, "recipes.json"),
                Buildings = Read<BuildingDef>(readFile, "buildings.json"),
                Terrain = Read<TerrainDef>(readFile, "terrain.json"),
                ResourceNodes = Read<ResourceNodeDef>(readFile, "resource_nodes.json"),
                Needs = Read<NeedDef>(readFile, "needs.json"),
                Actions = Read<ActionDef>(readFile, "actions.json"),
                ActionArgs = Read<ActionArgDef>(readFile, "action_args.json"),
                WorldGen = Read<WorldGenDef>(readFile, "world_gen.json"),
                Animals = Read<AnimalDef>(readFile, "animals.json"),
                Roles = Read<RoleDef>(readFile, "roles.json"),
                Weather = Read<WeatherDef>(readFile, "weather.json"),
                Tech = Read<TechDef>(readFile, "tech.json"),
                Rewards = Read<RewardDef>(readFile, "rewards.json"),
                Achievements = Read<AchievementDef>(readFile, "achievements.json"),
                Crops = Read<CropDef>(readFile, "crops.json"),
                Sicknesses = Read<SicknessDef>(readFile, "sicknesses.json"),
            };
            var seasons = Read<SeasonDef>(readFile, "seasons.json");
            seasons.Sort((a, b) => a.Order.CompareTo(b.Order));
            d.Seasons = seasons;

            var manifest = JsonConvert.DeserializeObject<ManifestDef>(readFile("manifest.json"));
            d.Workbook = manifest?.Workbook ?? "";
            d.WorkbookSha256 = manifest?.WorkbookSha256 ?? "";

            foreach (var e in Read<ConfigEntry>(readFile, "config.json")) d.config[e.Key] = e.Value;
            foreach (var m in Read<NeedModifierDef>(readFile, "need_modifiers.json")) d.modifiers[m.Id ?? ""] = m.Value;

            var items = new Dictionary<string, ItemDef>();
            foreach (var i in d.Items) items[i.Id] = i;
            d.ItemById = items;

            var buildings = new Dictionary<string, BuildingDef>();
            foreach (var b in d.Buildings) buildings[b.Id] = b;
            d.BuildingById = buildings;

            d.itemByInt = IndexByInt(d.Items, i => i.Int);
            d.nodeByInt = IndexByInt(d.ResourceNodes, n => n.Int);
            d.terrainByInt = IndexByInt(d.Terrain, t => t.Int);
            d.equipmentByItemInt = new EquipmentDef[d.itemByInt.Length];
            foreach (var e in d.Equipment)
                if (items.TryGetValue(e.ItemId ?? "", out var item)) d.equipmentByItemInt[item.Int] = e;

            foreach (var n in d.ResourceNodes) d.nodeById[n.Id] = n;
            foreach (var t in d.Terrain) d.terrainById[t.Id] = t;
            foreach (var n in d.Needs) d.needById[n.Id] = n;
            foreach (var a in d.Actions) d.actionIdByName[a.Name] = a.Id;
            foreach (var a in d.Animals) d.animalById[a.Id] = a;
            foreach (var r in d.Roles) d.roleIntById[r.Id] = r.Int;
            foreach (var arg in d.ActionArgs)
                if (arg.Kind == "recipe" || arg.Kind == "building" || arg.Kind == "plant_crop") d.argByRefId[arg.RefId] = arg.Int;
            return d;
        }

        static List<T> Read<T>(Func<string, string> readFile, string file) =>
            JsonConvert.DeserializeObject<List<T>>(readFile(file));

        static T[] IndexByInt<T>(IReadOnlyList<T> rows, Func<T, int> key) where T : class
        {
            int max = 0;
            foreach (var row in rows) max = Math.Max(max, key(row));
            var table = new T[max + 1];
            foreach (var row in rows) table[key(row)] = row;
            return table;
        }

        static T At<T>(T[] table, int index) where T : class => (uint)index < (uint)table.Length ? table[index] : null;

        static T Find<T>(Dictionary<string, T> table, string id, string what)
        {
            if (id != null && table.TryGetValue(id, out var value)) return value;
            throw new KeyNotFoundException(what + " not found: " + id);
        }

        public int ConfigInt(string key) => Convert.ToInt32(ConfigRaw(key), CultureInfo.InvariantCulture);

        public float ConfigFloat(string key) => Convert.ToSingle(ConfigRaw(key), CultureInfo.InvariantCulture);

        public string ConfigString(string key) => Convert.ToString(ConfigRaw(key), CultureInfo.InvariantCulture);

        object ConfigRaw(string key) => Find(config, key, "Config key");

        /// <summary>Numeric value of a row in Sim Need Modifiers, by its modifier_id.</summary>
        public float Modifier(string id) => Convert.ToSingle(Find(modifiers, id, "Need modifier"), CultureInfo.InvariantCulture);

        /// <summary>A modifier whose value is written as a formula, such as "0.05 x sociability".</summary>
        public string ModifierText(string id) => Convert.ToString(Find(modifiers, id, "Need modifier"), CultureInfo.InvariantCulture);

        /// <summary>Null when there is no item with that int.</summary>
        public ItemDef ItemByInt(int itemInt) => At(itemByInt, itemInt);

        /// <summary>Null when the item is not a tool, weapon or clothing.</summary>
        public EquipmentDef EquipmentByItemInt(int itemInt) => At(equipmentByItemInt, itemInt);

        public ResourceNodeDef NodeByInt(int nodeInt) => At(nodeByInt, nodeInt);

        public ResourceNodeDef NodeById(string id) => Find(nodeById, id, "Resource node");

        public TerrainDef TerrainByInt(int terrainInt) => At(terrainByInt, terrainInt);

        public TerrainDef TerrainById(string id) => Find(terrainById, id, "Terrain");

        public NeedDef NeedById(string id) => Find(needById, id, "Need");

        public int ActionId(string name) => Find(actionIdByName, name, "Action");

        public AnimalDef AnimalById(string id) => Find(animalById, id, "Animal");

        public WeatherDef WeatherById(string id)
        {
            foreach (var weather in Weather)
                if (weather.Id == id) return weather;
            throw new KeyNotFoundException("Weather '" + id + "' is not in the data.");
        }

        /// <summary>role_int of a role id from Sim Roles.</summary>
        public int RoleInt(string roleId) => Find(roleIntById, roleId, "Role");

        /// <summary>The argument number in Sim Action Args for a recipe, building or crop id.</summary>
        public int ArgFor(string refId) => Find(argByRefId, refId, "Action argument");

        /// <summary>Checks every cross-reference between tables. Returns one message per problem.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();

            void Item(string id, string where)
            {
                if (!string.IsNullOrEmpty(id) && !ItemById.ContainsKey(id))
                    errors.Add(where + ": unknown item '" + id + "'");
            }

            void Building(string id, string where)
            {
                if (!string.IsNullOrEmpty(id) && !BuildingById.ContainsKey(id))
                    errors.Add(where + ": unknown building '" + id + "'");
            }

            foreach (var r in Recipes)
            {
                var where = "recipe " + r.OutputItem;
                Item(r.OutputItem, where);
                Item(r.Input1, where);
                Item(r.Input2, where);
                Item(r.Input3, where);
                Building(r.Workstation, where);
                Building(r.WorkstationAlt, where);
            }
            foreach (var b in Buildings)
            {
                var where = "building " + b.Id;
                Item(b.CostItem1, where);
                Item(b.CostItem2, where);
                Item(b.CostItem3, where);
            }
            foreach (var n in ResourceNodes)
            {
                // crop_mature points at the crops table instead of a single item.
                if (n.Id != "crop_mature") Item(n.YieldItem, "node " + n.Id);
                Item(n.SecondaryItem, "node " + n.Id);
                if (!string.IsNullOrEmpty(n.Terrain) && n.Terrain != "any" && !terrainById.ContainsKey(n.Terrain))
                    errors.Add("node " + n.Id + ": unknown terrain '" + n.Terrain + "'");
            }
            foreach (var e in Equipment)
            {
                Item(e.ItemId, "equipment " + e.ItemId);
                if (Array.IndexOf(EquipSlotNames, e.Slot) < 0)
                    errors.Add("equipment " + e.ItemId + ": unknown slot '" + e.Slot + "'");
            }
            var storageGroups = new HashSet<string>();
            foreach (var item in Items)
            {
                if (string.IsNullOrEmpty(item.StorageGroup)) errors.Add("item " + item.Id + ": no storage_group");
                storageGroups.Add(item.StorageGroup ?? "");
            }
            foreach (var b in Buildings)
                if (!string.IsNullOrEmpty(b.StorageFilter) && b.StorageFilter != "any" && !storageGroups.Contains(b.StorageFilter))
                    errors.Add("building " + b.Id + ": storage_filter '" + b.StorageFilter + "' matches no item storage_group");
            foreach (var r in Recipes)
                if (!string.IsNullOrEmpty(r.RoleRequired) && r.RoleRequired != "any" && !roleIntById.ContainsKey(r.RoleRequired))
                    errors.Add("recipe " + r.OutputItem + ": unknown role '" + r.RoleRequired + "'");
            foreach (var arg in ActionArgs)
            {
                if (arg.Kind == "recipe" && !ItemById.ContainsKey(arg.RefId ?? "")) errors.Add("action arg " + arg.Int + ": unknown recipe output '" + arg.RefId + "'");
                if (arg.Kind == "building" && !BuildingById.ContainsKey(arg.RefId ?? "")) errors.Add("action arg " + arg.Int + ": unknown building '" + arg.RefId + "'");
            }

            foreach (var g in WorldGen)
                if (!nodeById.ContainsKey(g.NodeId ?? ""))
                    errors.Add("world gen: unknown node '" + g.NodeId + "'");

            if (Actions.Count != ConfigInt("num_actions"))
                errors.Add("actions.json has " + Actions.Count + " rows, config num_actions = " + ConfigInt("num_actions"));
            if (ActionArgs.Count != ConfigInt("num_action_args"))
                errors.Add("action_args.json has " + ActionArgs.Count + " rows, config num_action_args = " + ConfigInt("num_action_args"));
            for (int i = 0; i < Actions.Count; i++)
                if (Actions[i].Id != i) errors.Add("action ids are not contiguous at row " + i);
            for (int i = 0; i < ActionArgs.Count; i++)
                if (ActionArgs[i].Int != i) errors.Add("action arg ints are not contiguous at row " + i);

            for (int i = 0; i < Needs.Count; i++)
                if (i >= NeedNames.Length || Needs[i].Id != NeedNames[i] || Needs[i].ObsIndex != i)
                    errors.Add("needs.json row " + i + " should be '" + (i < NeedNames.Length ? NeedNames[i] : "?") + "' with obs_index " + i);
            if (Needs.Count != NeedNames.Length) errors.Add("needs.json should have " + NeedNames.Length + " rows");

            if (Seasons.Count != 4) errors.Add("seasons.json should have 4 rows");
            int seasonTicks = ConfigInt("days_per_season") * ConfigInt("ticks_per_day");
            for (int i = 0; i < Seasons.Count; i++)
            {
                if (Seasons[i].Order != i) errors.Add("season orders should be 0-3");
                if (Seasons[i].DurationTicks != seasonTicks)
                    errors.Add("season " + Seasons[i].Id + ": duration_ticks should be days_per_season x ticks_per_day = " + seasonTicks);
            }

            // The same energy costs appear in Sim Actions and Sim Need Modifiers; they must agree.
            ActionCostMatches("move_north", "energy_move_action", errors);
            ActionCostMatches("harvest_attack", "energy_harvest_action", errors);
            ActionCostMatches("craft_build", "energy_craft_action", errors);

            return errors;
        }

        /// <summary>Need ids in the order of the Need enum and the observation vector.</summary>
        public static readonly string[] NeedNames = { "hp", "hunger", "thirst", "energy", "temp", "morale" };

        /// <summary>Equipment slot ids in the order of the EquipSlot enum.</summary>
        public static readonly string[] EquipSlotNames = { "hand", "body", "feet", "head", "back" };

        void ActionCostMatches(string action, string modifier, List<string> errors)
        {
            if (!actionIdByName.TryGetValue(action, out int id) || !modifiers.ContainsKey(modifier))
            {
                errors.Add("missing action '" + action + "' or modifier '" + modifier + "'");
                return;
            }
            if (Math.Abs(Actions[id].EnergyCost + Modifier(modifier)) > 1e-6f)
                errors.Add("energy cost of " + action + " (" + Actions[id].EnergyCost + ") disagrees with modifier " + modifier + " (" + Modifier(modifier) + ")");
        }
    }
}
