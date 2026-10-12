using System;
using TinyTribe.Sim.Data;

namespace TinyTribe.Sim.Core
{
    /// <summary>
    /// Sickness (Sim Sicknesses, rule R39): what brings each one on, what it does, and what ends it.
    /// In the engine: common cold, food poisoning, infection, bleeding, heatstroke and scurvy.
    /// Exhaustion and depression wait for the systems they depend on (see rule R34).
    /// </summary>
    public sealed partial class Simulation
    {
        // Ids and effect keys from Sim Sicknesses.
        const string ColdId = "common_cold", PoisoningId = "food_poisoning", InfectionId = "infection";
        const string BleedingId = "bleeding", HeatstrokeId = "heatstroke", ScurvyId = "scurvy";
        const string SickEnergyDrain = "energy_drain_mult", SickMaxHp = "max_hp_delta", SickHungerDrain = "hunger_drain_mult";
        const string SickOnsetEnergy = "energy_delta_on_onset", SickHpPerTick = "hp_delta_per_tick", SickSpread = "spread_chance_per_tick_adjacent";
        const string SickThirstDrain = "thirst_drain_mult", SickMaxHpPerTick = "max_hp_delta_per_tick", SickWorkSpeed = "work_speed_mult";

        /// <summary>Marks a sickness that lasts until it is cured.</summary>
        public const int UntilCured = -1;

        int coldIndex, poisoningIndex, infectionIndex, bleedingIndex, heatstrokeIndex, scurvyIndex;
        SicknessDef coldDef, poisoningDef, infectionDef, bleedingDef, heatstrokeDef, scurvyDef;

        /// <summary>Whether villagers can fall sick. Off by default: see the note on infection in the workbook's Open Issues.</summary>
        public bool SicknessEnabled { get; set; }

        void InitSickness()
        {
            for (int i = 0; i < Data.Sicknesses.Count; i++)
            {
                var def = Data.Sicknesses[i];
                switch (def.Id)
                {
                    case ColdId: coldIndex = i; coldDef = def; break;
                    case PoisoningId: poisoningIndex = i; poisoningDef = def; break;
                    case InfectionId: infectionIndex = i; infectionDef = def; break;
                    case BleedingId: bleedingIndex = i; bleedingDef = def; break;
                    case HeatstrokeId: heatstrokeIndex = i; heatstrokeDef = def; break;
                    case ScurvyId: scurvyIndex = i; scurvyDef = def; break;
                }
            }
        }

        int SicknessIndex(string sicknessId)
        {
            for (int i = 0; i < Data.Sicknesses.Count; i++)
                if (Data.Sicknesses[i].Id == sicknessId) return i;
            throw new ArgumentException("Unknown sickness: " + sicknessId);
        }

        public bool IsSick(Agent agent, string sicknessId) => agent.SickTicks[SicknessIndex(sicknessId)] != 0;

        /// <summary>Gives the villager a sickness, whether or not sickness is switched on. For tests and set-up; in play the triggers below do it.</summary>
        public void MakeSick(Agent agent, string sicknessId) => MakeSick(agent, SicknessIndex(sicknessId));

        void MakeSick(Agent agent, int index)
        {
            if (!agent.Alive || agent.SickTicks[index] != 0) return;
            var def = Data.Sicknesses[index];
            agent.SickTicks[index] = def.DurationTicks > 0 ? def.DurationTicks : UntilCured;
            if (index == poisoningIndex) AddNeed(agent, Need.Energy, def.Effect(SickOnsetEnergy));
            if (index == coldIndex) agent.ShelterRestTicks = 0;
            events.Add(new SimEvent(SimEventType.SicknessStarted, agent.Index, index, 0, agent.X, agent.Y));
            RefreshHealth(agent);
        }

        public void Cure(Agent agent, string sicknessId) => Cure(agent, SicknessIndex(sicknessId));

        void Cure(Agent agent, int index)
        {
            if (agent.SickTicks[index] != 0) EndSickness(agent, index);
        }

        void EndSickness(Agent agent, int index)
        {
            agent.SickTicks[index] = 0;
            if (index == scurvyIndex) agent.ScurvyDamage = 0f;
            events.Add(new SimEvent(SimEventType.SicknessEnded, agent.Index, index, 0, agent.X, agent.Y));
            RefreshHealth(agent);
        }

        /// <summary>The most health the villager can have, and how fast it works, given what it is sick with.</summary>
        void RefreshHealth(Agent agent)
        {
            float max = Config.NeedMax;
            if (agent.SickTicks[coldIndex] != 0) max += coldDef.Effect(SickMaxHp);
            max -= agent.ScurvyDamage;
            agent.MaxHp = Math.Max(1f, max);
            agent.SickWorkMult = agent.SickTicks[scurvyIndex] != 0 ? scurvyDef.Effect(SickWorkSpeed) : 1f;
        }

        /// <summary>R23: heatstroke pins a villager where it stands.</summary>
        public bool IsImmobile(Agent agent) => agent.SickTicks[heatstrokeIndex] != 0;

        // ------------------------------------------------------------------ what brings it on

        /// <summary>A bite from an animal that carries something.</summary>
        void SickFromBite(Agent victim, AnimalDef animal)
        {
            if (!SicknessEnabled || string.IsNullOrEmpty(animal.InflictsSickness)) return;
            if (rng.Chance(animal.SicknessChance)) MakeSick(victim, SicknessIndex(animal.InflictsSickness));
        }

        /// <summary>Food and medicine: raw food can poison; medicine, water and fresh food each cure something.</summary>
        void SickFromEating(Agent agent, ItemDef item)
        {
            if (!string.IsNullOrEmpty(item.Cures))
                foreach (string cured in item.Cures.Split(';')) Cure(agent, SicknessIndex(cured));
            if (item.Thirst > 0f) Cure(agent, heatstrokeIndex);
            if (item.PreventsScurvy != 0)
            {
                agent.ScurvyClock = 0;
                Cure(agent, scurvyIndex);
            }
            if (SicknessEnabled && !string.IsNullOrEmpty(item.SicknessOnEat) && rng.Chance(item.SicknessChance))
                MakeSick(agent, SicknessIndex(item.SicknessOnEat));
        }

        /// <summary>Hard work in the summer sun or a heatwave, on too little water.</summary>
        void SickFromWork(Agent agent)
        {
            if (!SicknessEnabled || agent.SickTicks[heatstrokeIndex] != 0) return;
            bool hot = weatherThirstMult > 1f || (!IsNight && Season.Order == Config.SummerOrder);
            if (hot && agent.Needs[(int)Need.Thirst] < Config.HeatstrokeThirstBelow && rng.Chance(heatstrokeDef.TriggerChanceValue))
                MakeSick(agent, heatstrokeIndex);
        }

        // ------------------------------------------------------------------ every tick

        /// <summary>Runs for each living villager before its needs are updated. Returns the hp change of the tick from sickness.</summary>
        float UpdateSickness(Agent agent)
        {
            var sick = agent.SickTicks;
            if (SicknessEnabled)
            {
                // Cold, or caught in the rain.
                if (sick[coldIndex] == 0
                    && (agent.Needs[(int)Need.Temp] < Config.ColdSicknessTempBelow || (weatherChill != 0f && !agent.Sheltered))
                    && rng.Chance(coldDef.TriggerChanceValue)) MakeSick(agent, coldIndex);

                // Too long without fresh food.
                if (sick[scurvyIndex] == 0 && ++agent.ScurvyClock > Config.ScurvyAfterTicks) MakeSick(agent, scurvyIndex);
            }

            float hp = 0f;
            bool changed = false;
            for (int i = 0; i < sick.Length; i++)
            {
                if (sick[i] == 0) continue;
                if (i == infectionIndex) hp += infectionDef.Effect(SickHpPerTick);
                else if (i == bleedingIndex) hp += bleedingDef.Effect(SickHpPerTick);
                else if (i == scurvyIndex)
                {
                    agent.ScurvyDamage -= scurvyDef.Effect(SickMaxHpPerTick);
                    changed = true;
                }
                else if (i == coldIndex && agent.Sheltered && ++agent.ShelterRestTicks >= Config.ColdCureShelterRestTicks)
                {
                    Cure(agent, i);
                    continue;
                }
                // The last tick of a sickness that passes by itself.
                if (sick[i] == 1) EndSickness(agent, i);
                else if (sick[i] > 1) sick[i]--;
            }
            if (changed) RefreshHealth(agent);

            if (sick[infectionIndex] != 0) SpreadInfection(agent);
            return hp;
        }

        /// <summary>An infection can pass to a villager standing right next to the sick one.</summary>
        void SpreadInfection(Agent agent)
        {
            float chance = infectionDef.Effect(SickSpread);
            if (!SicknessEnabled || chance <= 0f) return;
            for (int d = 0; d < 4; d++)
            {
                int x = agent.X + StepX[d], y = agent.Y + StepY[d];
                if (!World.InBounds(x, y)) continue;
                int other = World.AgentIndex[World.Index(x, y)];
                if (other >= 0 && agents[other].SickTicks[infectionIndex] == 0 && rng.Chance(chance)) MakeSick(agents[other], infectionIndex);
            }
        }
    }
}
