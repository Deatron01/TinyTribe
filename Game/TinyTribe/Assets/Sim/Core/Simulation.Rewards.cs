using System;
using System.Collections.Generic;
using TinyTribe.Sim.Data;

namespace TinyTribe.Sim.Core
{
    /// <summary>
    /// What each villager is paid for a tick (Sim Rewards, rule R42). Outcome rewards say how the
    /// villager and the village are doing. Shaping rewards pay for one particular action; they are
    /// multiplied by RewardShapingScale, which the workbook sets to 0, so that nobody is told what to
    /// do: a villager has to find out for itself what keeps it and the village well.
    /// </summary>
    public sealed partial class Simulation
    {
        /// <summary>The rewards the engine pays so far. The rest of Sim Rewards belongs to systems that are not built yet.</summary>
        public static readonly IReadOnlyList<string> RewardsInEngine = new[]
        {
            "stat_at_zero", "take_damage", "agent_death", "structure_completed", "tech_researched", "winter_survived", "granary_increase",
            "consume_when_low", "sleep_when_exhausted", "gather_item", "plant_crop", "vote_cast", "research_point",
        };

        // Ids from Sim Seasons and Sim Buildings.
        const string WinterSeason = "winter", FoodStorage = "food";

        RewardDef statAtZero, takeDamage, agentDeath, structureCompleted, techResearched, winterSurvived, granaryIncrease;
        RewardDef consumeWhenLow, sleepWhenExhausted, gatherItem, plantCrop, voteCast, researchPoint;
        RewardDef marriageReward, childBornReward, emoteExchangeReward;
        float[] rewards = new float[0], villageRewards = new float[0];
        bool[] diedThisTick = new bool[0];
        bool wasWinter;
        int deathsThisWinter;
        int granaryFood;
        bool[] kindBuilt = new bool[0];
        int newKindsThisTick;

        /// <summary>What shaping rewards are multiplied by: 0 pays for outcomes only, 1 pays the whole table.</summary>
        public float RewardShapingScale { get; set; }

        /// <summary>What the villager earned in the last Step: everything, added up as it stands in Sim Rewards.</summary>
        public float RewardOf(int agentIndex) => (uint)agentIndex < (uint)rewards.Length ? rewards[agentIndex] : 0f;

        /// <summary>
        /// The same split by the reward's scope: what it earned for itself, and what it earned as one of
        /// the village. The design weighs the two by the villager's empathy,
        /// R_total = R_individual x (1 - empathy / 2) + R_global x empathy; that is left to whoever trains.
        /// </summary>
        public void RewardPartsOf(int agentIndex, out float individual, out float village)
        {
            village = (uint)agentIndex < (uint)villageRewards.Length ? villageRewards[agentIndex] : 0f;
            individual = RewardOf(agentIndex) - village;
        }

        void InitRewards()
        {
            RewardDef Find(string id)
            {
                foreach (var reward in Data.Rewards)
                    if (reward.Id == id) return reward;
                throw new InvalidOperationException("Sim Rewards has no row '" + id + "'.");
            }
            statAtZero = Find("stat_at_zero");
            takeDamage = Find("take_damage");
            agentDeath = Find("agent_death");
            structureCompleted = Find("structure_completed");
            techResearched = Find("tech_researched");
            winterSurvived = Find("winter_survived");
            granaryIncrease = Find("granary_increase");
            consumeWhenLow = Find("consume_when_low");
            sleepWhenExhausted = Find("sleep_when_exhausted");
            gatherItem = Find("gather_item");
            plantCrop = Find("plant_crop");
            voteCast = Find("vote_cast");
            researchPoint = Find("research_point");
            marriageReward = Find("marriage");
            childBornReward = Find("child_born");
            emoteExchangeReward = Find("emote_exchange");
            RewardShapingScale = Config.RewardShapingScale;
            int kinds = 0;
            foreach (var building in Data.Buildings) kinds = Math.Max(kinds, building.Int + 1);
            kindBuilt = new bool[kinds];
            wasWinter = Season.Id == WinterSeason;
        }

        /// <summary>
        /// A building was finished. Only the first of its kind is something new for the village; a
        /// tenth chest is not, and paying for it would teach a village to build chests for ever.
        /// </summary>
        void NoteBuilt(BuildingDef def)
        {
            if (kindBuilt[def.Int]) return;
            kindBuilt[def.Int] = true;
            newKindsThisTick++;
        }

        /// <summary>A new tick: nothing earned yet. What the setup did between ticks pays nobody.</summary>
        void BeginRewards()
        {
            newKindsThisTick = 0;
            if (rewards.Length != agents.Count)
            {
                rewards = new float[agents.Count];
                villageRewards = new float[agents.Count];
                diedThisTick = new bool[agents.Count];
            }
            else
            {
                Array.Clear(rewards, 0, rewards.Length);
                Array.Clear(villageRewards, 0, villageRewards.Length);
                Array.Clear(diedThisTick, 0, diedThisTick.Length);
            }
        }

        void Pay(Agent agent, RewardDef reward, float times = 1f)
        {
            if ((uint)agent.Index >= (uint)rewards.Length) return;
            float amount = reward.Value * times * (reward.IsShaping ? RewardShapingScale : 1f);
            rewards[agent.Index] += amount;
            if (reward.IsGlobal) villageRewards[agent.Index] += amount;
        }

        /// <summary>The whole village: everyone alive, and whoever died on this tick.</summary>
        void PayAll(RewardDef reward, float times = 1f)
        {
            foreach (var agent in agents)
                if (agent.Alive || diedThisTick[agent.Index]) Pay(agent, reward, times);
        }

        /// <summary>The cold took health: charged like a hit for every reward_hp_per_damage_penalty lost.</summary>
        void PayForFreezing(Agent agent, float hpLost)
        {
            agent.FrozenHp += hpLost;
            while (agent.FrozenHp >= Config.RewardHpPerDamagePenalty)
            {
                agent.FrozenHp -= Config.RewardHpPerDamagePenalty;
                Pay(agent, takeDamage);
            }
        }

        /// <summary>The end of the tick: what happened in it, and how everyone stands now.</summary>
        void SettleRewards()
        {
            int deaths = 0, learned = 0, born = 0;
            foreach (var e in events)
            {
                switch (e.Type)
                {
                    case SimEventType.Died:
                        // A death of old age is nobody's failure.
                        if (e.Item != DiedOfOldAge) deaths++;
                        diedThisTick[e.Agent] = true;
                        break;
                    case SimEventType.Married:
                        Pay(agents[e.Agent], marriageReward);
                        Pay(agents[e.Item], marriageReward);
                        break;
                    case SimEventType.Born:
                        born++;
                        break;
                    case SimEventType.EmoteExchanged:
                        Pay(agents[e.Agent], emoteExchangeReward);
                        break;
                    case SimEventType.TechResearched:
                        learned++;
                        break;
                    case SimEventType.AgentHurt:
                        Pay(agents[e.Agent], takeDamage);
                        break;
                    case SimEventType.Harvested:
                        if (e.Agent >= 0) Pay(agents[e.Agent], gatherItem, e.Qty);
                        break;
                    case SimEventType.CropPlanted:
                        Pay(agents[e.Agent], plantCrop);
                        break;
                    case SimEventType.VoteCast:
                        Pay(agents[e.Agent], voteCast);
                        break;
                }
            }
            if (deaths > 0) PayAll(agentDeath, deaths);
            if (newKindsThisTick > 0) PayAll(structureCompleted, newKindsThisTick);
            if (learned > 0) PayAll(techResearched, learned);
            if (born > 0) PayAll(childBornReward, born);

            foreach (var agent in agents)
            {
                if (!agent.Alive) continue;
                for (int need = 1; need < agent.Needs.Length; need++)
                {
                    // Charged for what is killing it. Energy at zero puts it to sleep, and nothing it can do raises morale yet.
                    if (agent.Needs[need] > Config.NeedMin || needs[need].HpDeltaPerTickAtZero == 0f) continue;
                    Pay(agent, statAtZero);
                    break;
                }
            }

            // A winter nobody died in, paid as it ends.
            if (wasWinter) deathsThisWinter += deaths;
            bool winter = Season.Id == WinterSeason;
            if (wasWinter && !winter && deathsThisWinter == 0) PayAll(winterSurvived);
            if (wasWinter != winter) deathsThisWinter = 0;
            wasWinter = winter;

            int food = 0;
            foreach (var building in buildings)
            {
                if (!building.Complete || building.Storage == null || building.Def.StorageFilter != FoodStorage) continue;
                foreach (var stack in building.Storage) food += stack.Qty;
            }
            if (food > granaryFood) PayAll(granaryIncrease);
            granaryFood = food;
        }
    }
}
