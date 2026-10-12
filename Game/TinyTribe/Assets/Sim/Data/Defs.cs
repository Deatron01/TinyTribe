using Newtonsoft.Json;

namespace TinyTribe.Sim.Data
{
    // Row types for the JSON files exported from the 'Sim ...' sheets of the design workbook.
    // Field names match the sheet column headers. Empty cells arrive as null.

    public sealed class ItemDef
    {
        [JsonProperty("item_int")] public int Int;
        [JsonProperty("item_id")] public string Id;
        [JsonProperty("name")] public string Name;
        [JsonProperty("category")] public string Category;
        [JsonProperty("phase")] public int Phase;
        [JsonProperty("max_stack")] public int MaxStack;
        [JsonProperty("base_value")] public int BaseValue;
        [JsonProperty("consumable")] public int Consumable;
        [JsonProperty("hunger")] public float Hunger;
        [JsonProperty("thirst")] public float Thirst;
        [JsonProperty("energy")] public float Energy;
        [JsonProperty("hp")] public float Hp;
        [JsonProperty("morale")] public float Morale;
        [JsonProperty("temp")] public float Temp;
        [JsonProperty("rot_ticks")] public int RotTicks;
        [JsonProperty("prevents_scurvy")] public int PreventsScurvy;
        [JsonProperty("sickness_on_eat")] public string SicknessOnEat;
        [JsonProperty("sickness_chance")] public float SicknessChance;
        [JsonProperty("cures")] public string Cures;
        [JsonProperty("storage_group")] public string StorageGroup;
    }

    public sealed class EquipmentDef
    {
        [JsonProperty("item_id")] public string ItemId;
        [JsonProperty("slot")] public string Slot;
        [JsonProperty("tool_type")] public string ToolType;
        [JsonProperty("tier")] public int Tier;
        [JsonProperty("speed_mult")] public float SpeedMult;
        [JsonProperty("energy_cost_mult")] public float EnergyCostMult;
        [JsonProperty("durability")] public int Durability;
        [JsonProperty("attack_damage")] public float AttackDamage;
        [JsonProperty("attack_range")] public int AttackRange;
        [JsonProperty("effect_key")] public string EffectKey;
        [JsonProperty("effect_value")] public float EffectValue;
    }

    public sealed class RecipeDef
    {
        [JsonProperty("recipe_int")] public int Int;
        [JsonProperty("output_item")] public string OutputItem;
        [JsonProperty("output_qty")] public int OutputQty;
        [JsonProperty("input_1")] public string Input1;
        [JsonProperty("qty_1")] public int Qty1;
        [JsonProperty("input_2")] public string Input2;
        [JsonProperty("qty_2")] public int Qty2;
        [JsonProperty("input_3")] public string Input3;
        [JsonProperty("qty_3")] public int Qty3;
        [JsonProperty("workstation")] public string Workstation;
        [JsonProperty("workstation_alt")] public string WorkstationAlt;
        [JsonProperty("craft_ticks")] public int CraftTicks;
        [JsonProperty("role_required")] public string RoleRequired;
        [JsonProperty("tech_required")] public string TechRequired;
        [JsonProperty("phase")] public int Phase;
    }

    public sealed class BuildingDef
    {
        [JsonProperty("building_int")] public int Int;
        [JsonProperty("building_id")] public string Id;
        [JsonProperty("name")] public string Name;
        [JsonProperty("category")] public string Category;
        [JsonProperty("phase")] public int Phase;
        [JsonProperty("tech_required")] public string TechRequired;
        [JsonProperty("footprint_w")] public int FootprintW;
        [JsonProperty("footprint_h")] public int FootprintH;
        [JsonProperty("build_ticks")] public int BuildTicks;
        [JsonProperty("max_hp")] public int MaxHp;
        [JsonProperty("storm_destroy_chance")] public float StormDestroyChance;
        [JsonProperty("capacity_agents")] public int CapacityAgents;
        [JsonProperty("storage_slots")] public int StorageSlots;
        [JsonProperty("storage_filter")] public string StorageFilter;
        [JsonProperty("cost_item_1")] public string CostItem1;
        [JsonProperty("cost_qty_1")] public int CostQty1;
        [JsonProperty("cost_item_2")] public string CostItem2;
        [JsonProperty("cost_qty_2")] public int CostQty2;
        [JsonProperty("cost_item_3")] public string CostItem3;
        [JsonProperty("cost_qty_3")] public int CostQty3;
        [JsonProperty("effect_key")] public string EffectKey;
        [JsonProperty("effect_value")] public float EffectValue;
    }

    public sealed class TerrainDef
    {
        [JsonProperty("terrain_int")] public int Int;
        [JsonProperty("terrain_id")] public string Id;
        [JsonProperty("walkable")] public int Walkable;
        [JsonProperty("move_cost_ticks")] public int MoveCostTicks;
    }

    public sealed class ResourceNodeDef
    {
        [JsonProperty("node_int")] public int Int;
        [JsonProperty("node_id")] public string Id;
        [JsonProperty("name")] public string Name;
        [JsonProperty("terrain")] public string Terrain;
        [JsonProperty("phase")] public int Phase;
        [JsonProperty("yield_item")] public string YieldItem;
        [JsonProperty("yield_qty")] public int YieldQty;
        [JsonProperty("harvest_ticks")] public int HarvestTicks;
        [JsonProperty("tool_type")] public string ToolType;
        [JsonProperty("min_tool_tier")] public int MinToolTier;
        [JsonProperty("charges")] public int Charges;
        [JsonProperty("regrow_ticks")] public int RegrowTicks;
        [JsonProperty("secondary_item")] public string SecondaryItem;
        [JsonProperty("secondary_chance")] public float SecondaryChance;
        [JsonProperty("seasons")] public string Seasons;
    }

    public sealed class NeedDef
    {
        [JsonProperty("need_id")] public string Id;
        [JsonProperty("obs_index")] public int ObsIndex;
        [JsonProperty("start")] public float Start;
        [JsonProperty("base_drain_per_tick")] public float BaseDrainPerTick;
        [JsonProperty("emote_threshold")] public float EmoteThreshold;
        [JsonProperty("at_zero_effect")] public string AtZeroEffect;
        [JsonProperty("hp_delta_per_tick_at_zero")] public float HpDeltaPerTickAtZero;
    }

    public sealed class ActionDef
    {
        [JsonProperty("action_id")] public int Id;
        [JsonProperty("action")] public string Name;
        [JsonProperty("group")] public string Group;
        [JsonProperty("duration_ticks")] public int DurationTicks;
        [JsonProperty("energy_cost")] public float EnergyCost;
        [JsonProperty("argument")] public string Argument;
    }

    public sealed class ActionArgDef
    {
        [JsonProperty("arg_int")] public int Int;
        [JsonProperty("kind")] public string Kind;
        [JsonProperty("ref_id")] public string RefId;
    }

    public sealed class SeasonDef
    {
        [JsonProperty("season_id")] public string Id;
        [JsonProperty("order")] public int Order;
        [JsonProperty("duration_ticks")] public int DurationTicks;
        [JsonProperty("temp_delta_day")] public float TempDeltaDay;
        [JsonProperty("temp_delta_night")] public float TempDeltaNight;
        [JsonProperty("crop_growth_mult")] public float CropGrowthMult;
        [JsonProperty("rain_chance_per_day")] public float RainChancePerDay;
        [JsonProperty("storm_chance_per_day")] public float StormChancePerDay;
        [JsonProperty("heatwave_chance_per_day")] public float HeatwaveChancePerDay;
        [JsonProperty("snow_cover")] public int SnowCover;
    }

    public sealed class WeatherDef
    {
        [JsonProperty("weather_id")] public string Id;
        [JsonProperty("duration_ticks")] public int DurationTicks;
        [JsonProperty("vision_radius")] public int VisionRadius;
        [JsonProperty("effect_key")] public string EffectKey;
        [JsonProperty("effect_value")] public object EffectValue;
        [JsonProperty("effect_2_key")] public string Effect2Key;
        [JsonProperty("effect_2_value")] public object Effect2Value;

        /// <summary>The number that goes with an effect key, or 0 when this weather does not have it (or describes it in words).</summary>
        public float Effect(string key) => key == EffectKey ? Number(EffectValue) : key == Effect2Key ? Number(Effect2Value) : 0f;

        public bool Has(string key) => key == EffectKey || key == Effect2Key;

        static float Number(object value) =>
            value == null || value is string ? 0f : System.Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    public sealed class NeedModifierDef
    {
        [JsonProperty("modifier_id")] public string Id;
        [JsonProperty("need_id")] public string NeedId;
        [JsonProperty("op")] public string Op;
        [JsonProperty("value")] public object Value;
    }

    public sealed class WorldGenDef
    {
        [JsonProperty("node_id")] public string NodeId;
        [JsonProperty("per_1000_tiles")] public float Per1000Tiles;
        [JsonProperty("min_spawn_distance")] public int MinSpawnDistance;
    }

    public sealed class AnimalDef
    {
        [JsonProperty("entity_int")] public int Int;
        [JsonProperty("animal_id")] public string Id;
        [JsonProperty("name")] public string Name;
        [JsonProperty("behavior")] public string Behavior;
        [JsonProperty("hp")] public float Hp;
        [JsonProperty("attack_damage")] public float AttackDamage;
        [JsonProperty("attack_cooldown_ticks")] public int AttackCooldownTicks;
        [JsonProperty("speed_tiles_per_tick")] public float SpeedTilesPerTick;
        [JsonProperty("aggro_radius")] public int AggroRadius;
        [JsonProperty("flee_radius")] public int FleeRadius;
        [JsonProperty("loot_raw_meat")] public int LootRawMeat;
        [JsonProperty("loot_animal_hide")] public int LootAnimalHide;
        [JsonProperty("loot_bone")] public int LootBone;
        [JsonProperty("inflicts_sickness")] public string InflictsSickness;
        [JsonProperty("sickness_chance")] public float SicknessChance;
        [JsonProperty("spawn_per_1000_tiles")] public float SpawnPer1000Tiles;
    }

    public sealed class SicknessDef
    {
        [JsonProperty("sickness_id")] public string Id;
        [JsonProperty("obs_flag_index")] public int ObsFlagIndex;
        [JsonProperty("trigger")] public string Trigger;
        [JsonProperty("trigger_chance")] public object TriggerChance;
        [JsonProperty("duration_ticks")] public int DurationTicks;
        [JsonProperty("effect_key")] public string EffectKey;
        [JsonProperty("effect_value")] public object EffectValue;
        [JsonProperty("effect_2_key")] public string Effect2Key;
        [JsonProperty("effect_2_value")] public object Effect2Value;
        [JsonProperty("cure")] public string Cure;

        /// <summary>The chance per tick or per event; 0 where the sheet points somewhere else for it.</summary>
        public float TriggerChanceValue => Number(TriggerChance);

        /// <summary>The number that goes with an effect key, or 0 when this sickness does not have it.</summary>
        public float Effect(string key) => key == EffectKey ? Number(EffectValue) : key == Effect2Key ? Number(Effect2Value) : 0f;

        static float Number(object value) =>
            value == null || value is string ? 0f : System.Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    public sealed class TechDef
    {
        [JsonProperty("tech_id")] public string Id;
        [JsonProperty("rp_cost")] public float RpCost;
        [JsonProperty("prerequisite")] public string Prerequisite;

        /// <summary>1 when only a Library can research it; 0 when an Elders' Circle is enough.</summary>
        [JsonProperty("needs_library")] public int NeedsLibrary;
    }

    /// <summary>A row of Sim Rewards. The trigger is prose in the workbook; the engine pays the ones it knows by id.</summary>
    public sealed class RewardDef
    {
        [JsonProperty("reward_id")] public string Id;
        [JsonProperty("scope")] public string Scope;
        [JsonProperty("credited_to")] public string CreditedTo;
        [JsonProperty("value")] public float Value;

        /// <summary>"outcome": how the villager and the village are doing. "shaping": pay for one particular action (rule R42).</summary>
        [JsonProperty("kind")] public string Kind;

        public bool IsShaping => Kind == "shaping";

        /// <summary>Scope "global": earned as one of the village (R_global), not for oneself (R_individual).</summary>
        public bool IsGlobal => Scope == "global";
    }

    /// <summary>A row of Sim Achievements: a counter of the run reaching a value (rule R43). For people reading a run, never for the villagers.</summary>
    public sealed class AchievementDef
    {
        [JsonProperty("achievement_id")] public string Id;
        [JsonProperty("name")] public string Name;
        [JsonProperty("group")] public string Group;
        [JsonProperty("metric")] public string Metric;
        [JsonProperty("at_least")] public float AtLeast;
        [JsonProperty("description")] public string Description;
    }

    /// <summary>manifest.json: which workbook the data files were exported from.</summary>
    public sealed class ManifestDef
    {
        [JsonProperty("workbook")] public string Workbook;
        [JsonProperty("workbook_sha256")] public string WorkbookSha256;
    }

    public sealed class CropDef
    {
        [JsonProperty("crop_id")] public string Id;
        [JsonProperty("seed_item")] public string SeedItem;
        [JsonProperty("growth_ticks")] public int GrowthTicks;
        [JsonProperty("yield_item")] public string YieldItem;
        [JsonProperty("yield_qty")] public int YieldQty;
        [JsonProperty("seed_return")] public int SeedReturn;
        [JsonProperty("plant_seasons")] public string PlantSeasons;
        [JsonProperty("dies_in_winter")] public int DiesInWinter;
    }

    public sealed class RoleDef
    {
        [JsonProperty("role_int")] public int Int;
        [JsonProperty("role_id")] public string Id;
        [JsonProperty("stat_1")] public string Stat1;
        [JsonProperty("stat_2")] public string Stat2;
        [JsonProperty("skill")] public string Skill;
        [JsonProperty("workstation")] public string Workstation;
        [JsonProperty("bonus_key")] public string BonusKey;
        [JsonProperty("bonus_value")] public float BonusValue;
    }

    public sealed class ConfigEntry
    {
        [JsonProperty("section")] public string Section;
        [JsonProperty("key")] public string Key;
        [JsonProperty("value")] public object Value;
        [JsonProperty("unit")] public string Unit;
    }
}
