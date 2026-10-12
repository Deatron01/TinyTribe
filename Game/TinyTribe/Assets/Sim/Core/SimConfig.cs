using TinyTribe.Sim.Data;

namespace TinyTribe.Sim.Core
{
    /// <summary>Config and modifier values the tick loop reads every tick, parsed once into typed fields.</summary>
    public sealed class SimConfig
    {
        public readonly int TicksPerHour, TicksPerDay, DayStartHour, NightStartHour, DaysPerSeason, SummerOrder;
        public readonly int InitialAgents, SpawnClearRadius;
        public readonly float NeedMin, NeedMax, BareHandSpeedMult, ForcedSleepWakeEnergy, HpRegenMinNeed;
        public readonly int StatMidpoint, CoreStatMin, CoreStatMax, IntCapGeneration0;
        public readonly float StrGatherSpeedPerPoint, DexDurabilityLossPerPoint, EndDrainPerPoint;
        public readonly int SlotsStrLow, SlotsStrMid, SlotsStrHigh, MaxSlots;
        public readonly float HpRegen, ThirstWorkAction, ThirstSummerDay, EnergySleepNight, EnergySleepDay, TempRecovery;
        public readonly float EnergySleepSheltered, TempCampfire, TempIndoors;
        public readonly float DexSpeedPerPoint, BareHandDamage, AnimalWanderChance;
        public readonly int CampfireStartFuelTicks, CampfireFuelTicksPerWood, CoalFuelMult;
        public readonly int AnimalVillageDistance, AnimalRespawnIntervalTicks, TerritorialLeash, MaxAgents;
        public readonly int VoteDurationTicks, VillageRadius, JobReviewGapTicks;
        public readonly float RewardShapingScale, RewardLowNeedBelow, RewardExhaustedEnergyBelow, RewardHpPerDamagePenalty;
        public readonly float NonMinerOreSpeedMult, HungerScoutTraveling, ThirstScoutTraveling;
        public readonly float RpPerTickBase, ResearchRpMultNonScholar;
        public readonly float ColdSicknessTempBelow, HeatstrokeThirstBelow;
        public readonly int ColdCureShelterRestTicks, ScurvyAfterTicks;

        /// <summary>STR at or below these values gets the low / mid slot count (named in the config keys).</summary>
        public const int LowStrMax = 3, MidStrMax = 7;

        public SimConfig(GameData data)
        {
            TicksPerHour = data.ConfigInt("ticks_per_hour");
            TicksPerDay = data.ConfigInt("ticks_per_day");
            DayStartHour = data.ConfigInt("day_start_hour");
            NightStartHour = data.ConfigInt("night_start_hour");
            DaysPerSeason = data.ConfigInt("days_per_season");
            foreach (var season in data.Seasons)
                if (season.Id == "summer") SummerOrder = season.Order;

            InitialAgents = data.ConfigInt("initial_agents");
            SpawnClearRadius = data.ConfigInt("spawn_clear_radius");
            NeedMin = data.ConfigFloat("need_min");
            NeedMax = data.ConfigFloat("need_max");
            BareHandSpeedMult = data.ConfigFloat("bare_hand_speed_mult");
            ForcedSleepWakeEnergy = data.ConfigFloat("forced_sleep_wake_energy");
            HpRegenMinNeed = data.ConfigFloat("hp_regen_min_need");

            StatMidpoint = data.ConfigInt("stat_midpoint");
            CoreStatMin = data.ConfigInt("core_stat_min");
            CoreStatMax = data.ConfigInt("core_stat_max");
            IntCapGeneration0 = data.ConfigInt("int_cap_generation_0");
            StrGatherSpeedPerPoint = data.ConfigFloat("str_gather_speed_per_point");
            DexDurabilityLossPerPoint = data.ConfigFloat("dex_durability_loss_per_point");
            EndDrainPerPoint = data.ConfigFloat("end_drain_per_point");

            SlotsStrLow = data.ConfigInt("personal_slots_str_1_3");
            SlotsStrMid = data.ConfigInt("personal_slots_str_4_7");
            SlotsStrHigh = data.ConfigInt("personal_slots_str_8_10");
            MaxSlots = data.ConfigInt("personal_slots_max_with_backpack");

            HpRegen = data.Modifier("hp_regen");
            ThirstWorkAction = data.Modifier("thirst_work_action");
            ThirstSummerDay = data.Modifier("thirst_summer_day");
            EnergySleepNight = data.Modifier("energy_sleep_night");
            EnergySleepDay = data.Modifier("energy_sleep_day");
            TempRecovery = data.Modifier("temp_recovery");
            EnergySleepSheltered = data.Modifier("energy_sleep_sheltered");
            TempCampfire = data.Modifier("temp_campfire");
            TempIndoors = data.Modifier("temp_indoors");

            DexSpeedPerPoint = data.ConfigFloat("dex_speed_per_point");
            BareHandDamage = data.ConfigFloat("bare_hand_damage");
            MaxAgents = data.ConfigInt("max_agents");
            CampfireStartFuelTicks = data.ConfigInt("campfire_start_fuel_ticks");
            CampfireFuelTicksPerWood = data.ConfigInt("campfire_fuel_ticks_per_wood");
            CoalFuelMult = data.ConfigInt("coal_fuel_mult");
            AnimalWanderChance = data.ConfigFloat("animal_wander_chance");
            AnimalVillageDistance = data.ConfigInt("animal_village_distance");
            AnimalRespawnIntervalTicks = data.ConfigInt("animal_respawn_interval_ticks");
            TerritorialLeash = data.ConfigInt("territorial_leash");

            VoteDurationTicks = data.ConfigInt("vote_duration_ticks");
            VillageRadius = data.ConfigInt("village_radius");
            JobReviewGapTicks = data.ConfigInt("job_review_gap_ticks");
            RewardShapingScale = data.ConfigFloat("reward_shaping_scale");
            RewardLowNeedBelow = data.ConfigFloat("reward_low_need_below");
            RewardExhaustedEnergyBelow = data.ConfigFloat("reward_exhausted_energy_below");
            RewardHpPerDamagePenalty = data.ConfigFloat("reward_hp_per_damage_penalty");
            NonMinerOreSpeedMult = data.ConfigFloat("non_miner_ore_speed_mult");
            HungerScoutTraveling = data.Modifier("hunger_scout_traveling");
            ThirstScoutTraveling = data.Modifier("thirst_scout_traveling");
            RpPerTickBase = data.ConfigFloat("rp_per_tick_base");
            ResearchRpMultNonScholar = data.ConfigFloat("research_rp_mult_non_scholar");
            ColdSicknessTempBelow = data.ConfigFloat("cold_sickness_temp_below");
            HeatstrokeThirstBelow = data.ConfigFloat("heatstroke_thirst_below");
            ColdCureShelterRestTicks = data.ConfigInt("cold_cure_shelter_rest_ticks");
            ScurvyAfterTicks = data.ConfigInt("scurvy_after_ticks");
        }
    }
}
