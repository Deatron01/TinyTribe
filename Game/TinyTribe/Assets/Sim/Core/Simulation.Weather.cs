using System;
using TinyTribe.Sim.Data;

namespace TinyTribe.Sim.Core
{
    /// <summary>Rain, storms and heatwaves (Sim Weather, Sim Seasons, rule R37).</summary>
    public sealed partial class Simulation
    {
        // Ids and effect keys from Sim Weather and Sim Equipment.
        const string ClearWeather = "clear", RainWeather = "rain", StormWeather = "storm", HeatwaveWeather = "heatwave";
        const string EffectExtinguish = "campfire_extinguish_chance_per_tick";
        const string EffectChill = "temp_delta_unsheltered";
        const string EffectWreck = "building_destroy_roll";
        const string EffectThirst = "thirst_drain_mult";
        const string EffectWorkEnergy = "work_energy_drain_mult";
        const string EffectHatThirst = "heatwave_thirst_mult";

        WeatherDef clearWeather, weather, comingWeather;
        long weatherEndsTick, comingWeatherTick;
        int weatherDay;

        // What the weather of the moment does, looked up when it changes.
        float weatherExtinguishChance, weatherChill, weatherThirstMult = 1f, weatherWorkEnergyMult = 1f;

        /// <summary>Whether weather comes by the chances of the season. SetWeather works either way.</summary>
        public bool WeatherEnabled { get; set; } = true;

        /// <summary>The weather right now; clear when nothing else is going on.</summary>
        public WeatherDef Weather => weather;

        /// <summary>The tick at which the current weather clears; 0 while it is clear.</summary>
        public long WeatherEndsTick => weatherEndsTick;

        void InitWeather()
        {
            clearWeather = Data.WeatherById(ClearWeather);
            weather = clearWeather;
            weatherDay = Day;
        }

        /// <summary>Starts a kind of weather now, for its own length of time unless one is given. For tests and set-up.</summary>
        public void SetWeather(string weatherId, int durationTicks = 0)
        {
            var kind = Data.WeatherById(weatherId);
            BeginWeather(kind, durationTicks > 0 ? durationTicks : kind.DurationTicks);
        }

        void BeginWeather(WeatherDef kind, int duration)
        {
            weather = kind;
            weatherEndsTick = kind == clearWeather ? 0 : Tick + duration;
            weatherExtinguishChance = kind.Effect(EffectExtinguish);
            weatherChill = kind.Effect(EffectChill);
            weatherThirstMult = kind.Has(EffectThirst) ? kind.Effect(EffectThirst) : 1f;
            weatherWorkEnergyMult = kind.Has(EffectWorkEnergy) ? kind.Effect(EffectWorkEnergy) : 1f;

            int index = 0;
            for (int i = 0; i < Data.Weather.Count; i++)
                if (Data.Weather[i] == kind) index = i;
            events.Add(new SimEvent(SimEventType.WeatherChanged, -1, index, kind == clearWeather ? 0 : duration, World.SpawnX, World.SpawnY));

            if (kind.Has(EffectWreck)) WreckBuildings();
        }

        /// <summary>What the weather does during a tick, apart from what it does to the villagers' needs: rain and storms put fires out.</summary>
        void WeatherTick()
        {
            if (weatherExtinguishChance <= 0f) return;
            foreach (var fire in campfires)
            {
                if (fire.FuelTicks <= 0 || !rng.Chance(weatherExtinguishChance)) continue;
                fire.FuelTicks = 0;
                events.Add(new SimEvent(SimEventType.CampfireOut, -1, fire.Def.Int, fire.Index, fire.X, fire.Y));
            }
        }

        /// <summary>Ends weather that has run its time, rolls for the new day, and starts what was rolled when its hour comes.</summary>
        void AdvanceWeather()
        {
            if (weather != clearWeather && Tick >= weatherEndsTick) BeginWeather(clearWeather, 0);

            if (Day != weatherDay)
            {
                weatherDay = Day;
                if (WeatherEnabled && comingWeather == null) RollWeather();
            }

            if (comingWeather != null && Tick >= comingWeatherTick && weather == clearWeather)
            {
                var kind = comingWeather;
                comingWeather = null;
                BeginWeather(kind, kind.DurationTicks);
            }
        }

        /// <summary>One kind of weather a day at most, the rarest first, starting at a random hour that lets it finish within the day.</summary>
        void RollWeather()
        {
            var season = Season;
            string id = rng.Chance(season.StormChancePerDay) ? StormWeather
                : rng.Chance(season.HeatwaveChancePerDay) ? HeatwaveWeather
                : rng.Chance(season.RainChancePerDay) ? RainWeather
                : null;
            if (id == null) return;
            comingWeather = Data.WeatherById(id);
            comingWeatherTick = Tick + rng.Range(0, Math.Max(1, Config.TicksPerDay - comingWeather.DurationTicks + 1));
        }

        /// <summary>A storm rolls once for every finished building, at the building's own chance.</summary>
        void WreckBuildings()
        {
            for (int i = 0; i < buildings.Count; i++)
            {
                var building = buildings[i];
                if (!building.Complete || building.Def.StormDestroyChance <= 0f) continue;
                if (rng.Chance(building.Def.StormDestroyChance)) DestroyBuilding(building);
            }
        }

        /// <summary>
        /// Takes a building off the map. What was stored in it is left on the ground where it stood, and
        /// whoever lived there is homeless. The building keeps its place in the list, marked Destroyed.
        /// </summary>
        public void DestroyBuilding(Building building)
        {
            if (building.Destroyed) return;
            building.Destroyed = true;
            JobsMayHaveChanged();
            building.Complete = false;
            building.FuelTicks = 0;
            campfires.Remove(building);
            for (int y = building.Y; y < building.Y + building.Height; y++)
                for (int x = building.X; x < building.X + building.Width; x++) World.BuildingIndex[World.Index(x, y)] = -1;

            foreach (int resident in building.Residents)
            {
                agents[resident].Home = -1;
                agents[resident].Sheltered = false;
            }
            building.Residents.Clear();

            if (building.Storage != null)
            {
                int tile = World.Index(building.X, building.Y);
                foreach (var stack in building.Storage) AddToPile(tile, stack);
                building.Storage = null;
            }
            events.Add(new SimEvent(SimEventType.BuildingDestroyed, -1, building.Def.Int, building.Index, building.X, building.Y));
        }
    }
}
