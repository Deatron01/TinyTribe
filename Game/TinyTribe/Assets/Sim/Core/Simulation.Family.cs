using System;
using System.Globalization;
using TinyTribe.Sim.Data;

namespace TinyTribe.Sim.Core
{
    /// <summary>
    /// Age, company and family (Lifespan &amp; Family, Social Web, rules R12, R22 and R24).
    ///
    /// Two things here are actions: showing an emote, and giving something to the villager in front.
    /// Everything else follows from how the villagers live (R24). A happy emote with others close by
    /// and a gift make two villagers think better of each other. Two grown villagers who have thought
    /// the world of each other for long enough marry and share a home. A couple that sleeps the night
    /// in the home they share may have a child in the morning, who takes after both. Children cannot
    /// fell, quarry, hunt or vote until they come of age; the old lose half their strength and
    /// endurance, and in the end die. Whoever loses a spouse or a child grieves.
    ///
    /// Nothing in this file decides what a villager does. The numbers are the workbook's (Sim Config:
    /// social, family, lifespan). Chance here has its own stream of random numbers, so that the rest
    /// of a village goes the same way whether or not anyone ever marries.
    /// </summary>
    public sealed partial class Simulation
    {
        /// <summary>In the Died event's Item field: the villager died of old age.</summary>
        public const int DiedOfOldAge = -3;

        /// <summary>The emote_int rows of Sim Observation that an action shows.</summary>
        public const int EmoteFood = 1, EmoteDanger = 2, EmoteResource = 3, EmoteHappy = 4, EmoteAngry = 5, EmoteFollow = 12;

        Rng familyRng;
        int[] emoteOfAction = new int[0];
        bool[] nodeHardLabour = new bool[0];
        float elderStatMult = 1f, griefDrainMult = 1f;

        int ticksPerAgeYear, childUntilAge, elderFromAge, oldAgeFrom, founderAgeMin, founderAgeMax;
        float oldAgeDeathChance;
        float relationMin, relationMax, deltaEmote, deltaGift, marriageThreshold;
        int socialRange, isolationRange, emoteTicks, emoteCooldownTicks;
        long marriageSustainTicks, birthCooldownTicks, griefTicks;
        float birthChance, griefMoraleHit, socialMutation, intCapGain;
        int maxChildren, statMutation;
        float moraleGroup, moraleIsolated, moraleSpouseNear, moraleEmoteExchange;

        // What each villager thinks of each other one, when two last exchanged an emote, and since when
        // two have been close enough to marry. Tables of familyCap x familyCap, [a * familyCap + b].
        int familyCap;
        float[] relation = new float[0];
        long[] exchangedAt = new long[0], friendsSince = new long[0];
        bool familyWasNight;

        /// <summary>How many villagers the village may grow to: no child is born beyond it. The workbook's max_agents unless set.</summary>
        public int MaxAgents { get; set; }

        /// <summary>How many villagers the village may have had in all, the dead counted too; 0 for no limit. No child is born beyond it.</summary>
        public int MaxVillagersEver { get; set; }

        void InitFamily(ulong seed)
        {
            familyRng = new Rng(seed ^ 0xC2B2AE3D27D4EB4FUL);
            MaxAgents = Config.MaxAgents;

            ticksPerAgeYear = Data.ConfigInt("ticks_per_age_year");
            childUntilAge = Data.ConfigInt("child_until_age");
            elderFromAge = Data.ConfigInt("elderly_from_age");
            oldAgeFrom = Data.ConfigInt("old_age_death_from");
            oldAgeDeathChance = Data.ConfigFloat("old_age_death_chance_per_day");
            founderAgeMin = Data.ConfigInt("founder_age_min");
            founderAgeMax = Data.ConfigInt("founder_age_max");
            elderStatMult = Data.ConfigFloat("elderly_stat_mult");

            relationMin = Data.ConfigFloat("relationship_min");
            relationMax = Data.ConfigFloat("relationship_max");
            deltaEmote = Data.ConfigFloat("delta_emote_exchange");
            deltaGift = Data.ConfigFloat("delta_gift_item");
            socialRange = Data.ConfigInt("social_range");
            isolationRange = Data.ConfigInt("isolation_range");
            emoteTicks = Data.ConfigInt("emote_ticks");
            emoteCooldownTicks = Data.ConfigInt("emote_exchange_cooldown_ticks");

            marriageThreshold = Data.ConfigFloat("marriage_threshold");
            marriageSustainTicks = (long)Data.ConfigInt("marriage_sustain_days") * Config.TicksPerDay;
            birthChance = Data.ConfigFloat("reproduction_chance_per_night");
            birthCooldownTicks = (long)Data.ConfigInt("reproduction_cooldown_days") * Config.TicksPerDay;
            maxChildren = Data.ConfigInt("max_children_per_couple");
            statMutation = Data.ConfigInt("stat_mutation");
            socialMutation = Data.ConfigFloat("social_stat_mutation");
            intCapGain = Data.ConfigFloat("int_cap_gain_per_generation");
            griefMoraleHit = Data.ConfigFloat("grief_morale_hit");
            griefTicks = (long)Data.ConfigInt("grief_duration_days") * Config.TicksPerDay;
            griefDrainMult = Data.ConfigFloat("grief_morale_drain_mult");

            moraleGroup = PerSociability("morale_group");
            moraleIsolated = PerSociability("morale_isolated");
            moraleSpouseNear = Data.Modifier("morale_spouse_near");
            moraleEmoteExchange = Data.Modifier("morale_emote_exchange");

            emoteOfAction = new int[actionCount];
            for (int i = 0; i < actionCount; i++)
            {
                switch (Data.Actions[i].Name)
                {
                    case "emote_food": emoteOfAction[i] = EmoteFood; break;
                    case "emote_danger": emoteOfAction[i] = EmoteDanger; break;
                    case "emote_resource": emoteOfAction[i] = EmoteResource; break;
                    case "emote_happy": emoteOfAction[i] = EmoteHappy; break;
                    case "emote_angry": emoteOfAction[i] = EmoteAngry; break;
                    case "emote_follow": emoteOfAction[i] = EmoteFollow; break;
                    // emote_trade waits for the Gold Age; emote_none is waiting.
                }
            }

            // R22: what a child may not harvest. Trees, rocks, veins, seams and deposits: whatever gives wood, stone, ore or coal.
            int maxNode = 0;
            foreach (var node in Data.ResourceNodes) maxNode = Math.Max(maxNode, node.Int);
            nodeHardLabour = new bool[maxNode + 1];
            foreach (var node in Data.ResourceNodes)
            {
                Data.ItemById.TryGetValue(node.YieldItem ?? "", out var yields);
                nodeHardLabour[node.Int] = yields != null
                    && (yields.StorageGroup == "wood" || yields.Id == "coal" || Array.IndexOf(StoneItems, yields.Id) >= 0 || Array.IndexOf(MetalItems, yields.Id) >= 0);
            }
        }

        /// <summary>The number a modifier such as "0.05 x sociability" begins with.</summary>
        float PerSociability(string modifierId)
        {
            string text = Data.ModifierText(modifierId).Trim();
            int end = text.IndexOf(' ');
            return float.Parse(end < 0 ? text : text.Substring(0, end), CultureInfo.InvariantCulture);
        }

        /// <summary>Room in the tables for everyone there is.</summary>
        void MakeRoomForFamily()
        {
            if (agents.Count <= familyCap) return;
            int cap = Math.Max(Math.Max(agents.Count, Config.MaxAgents), familyCap * 2);
            var newRelation = new float[cap * cap];
            var newExchanged = new long[cap * cap];
            var newSince = new long[cap * cap];
            for (int i = 0; i < newSince.Length; i++)
            {
                newExchanged[i] = long.MinValue / 4;
                newSince[i] = -1;
            }
            for (int a = 0; a < familyCap; a++)
                for (int b = 0; b < familyCap; b++)
                {
                    newRelation[a * cap + b] = relation[a * familyCap + b];
                    newExchanged[a * cap + b] = exchangedAt[a * familyCap + b];
                    newSince[a * cap + b] = friendsSince[a * familyCap + b];
                }
            relation = newRelation;
            exchangedAt = newExchanged;
            friendsSince = newSince;
            familyCap = cap;
        }

        // ------------------------------------------------------------------ age

        /// <summary>How long ago someone a game starts with was born: an age between founder_age_min and founder_age_max.</summary>
        long FounderAgeTicks() => (long)familyRng.Range(founderAgeMin, founderAgeMax + 1) * ticksPerAgeYear;

        /// <summary>The villager's age in years of its own life (ticks_per_age_year ticks each).</summary>
        public int AgeOf(Agent agent) => (int)((Tick - agent.BornTick) / ticksPerAgeYear);

        /// <summary>Makes the villager that old, as of now. For tests and set-up.</summary>
        public void SetAge(Agent agent, int years)
        {
            agent.BornTick = Tick - (long)years * ticksPerAgeYear;
            NoteStageOfLife(agent);
        }

        /// <summary>Child, grown or old: what follows from the villager's age.</summary>
        void NoteStageOfLife(Agent agent)
        {
            int age = AgeOf(agent);
            bool child = age < childUntilAge, elder = age >= elderFromAge;
            if (child == agent.Child && elder == agent.Elder) return;
            bool cameOfAge = agent.Child && !child;
            agent.Child = child;
            agent.Elder = elder;
            RefreshDerived(agent);
            if (!cameOfAge) return;
            events.Add(new SimEvent(SimEventType.CameOfAge, agent.Index, age, 0, agent.X, agent.Y));
            // R17: now the Council votes on its job.
            if (agent.Role == 0 && !roleVotesDue.Contains(agent.Index)) roleVotesDue.Enqueue(agent.Index);
        }

        // ------------------------------------------------------------------ what they think of each other

        /// <summary>What villager a thinks of villager b, relationship_min to relationship_max; 0 for strangers.</summary>
        public float RelationshipOf(int a, int b) =>
            a == b || (uint)a >= (uint)familyCap || (uint)b >= (uint)familyCap ? 0f : relation[a * familyCap + b];

        /// <summary>Sets what two villagers think of each other, both ways. For tests and set-up.</summary>
        public void SetRelationship(Agent a, Agent b, float score)
        {
            score = Math.Max(relationMin, Math.Min(relationMax, score));
            relation[a.Index * familyCap + b.Index] = score;
            relation[b.Index * familyCap + a.Index] = score;
        }

        /// <summary>Both think that much better (or worse) of each other.</summary>
        void Befriend(Agent a, Agent b, float delta)
        {
            int ab = a.Index * familyCap + b.Index, ba = b.Index * familyCap + a.Index;
            relation[ab] = Math.Max(relationMin, Math.Min(relationMax, relation[ab] + delta));
            relation[ba] = Math.Max(relationMin, Math.Min(relationMax, relation[ba] + delta));
        }

        /// <summary>Parent and child, or children of the same parent.</summary>
        static bool CloseKin(Agent a, Agent b)
        {
            if (a.Parent1 == b.Index || a.Parent2 == b.Index || b.Parent1 == a.Index || b.Parent2 == a.Index) return true;
            return a.Parent1 >= 0 && (a.Parent1 == b.Parent1 || a.Parent1 == b.Parent2 || a.Parent2 == b.Parent1 || a.Parent2 == b.Parent2);
        }

        // ------------------------------------------------------------------ emotes and gifts

        /// <summary>The emote the villager is showing now (emote_int of Sim Observation), 0 for none.</summary>
        public int EmoteOf(Agent agent) => agent.Alive && Tick < agent.EmoteUntilTick ? agent.Emote : 0;

        /// <summary>
        /// The villager shows an emote for emote_ticks ticks. A happy one reaches everyone within
        /// social_range: each of them and the villager think a little better of each other and feel
        /// better for it, once per emote_exchange_cooldown_ticks for the same two.
        /// </summary>
        bool ShowEmote(Agent agent, int actionId)
        {
            agent.Emote = emoteOfAction[actionId];
            agent.EmoteUntilTick = Tick + emoteTicks;
            events.Add(new SimEvent(SimEventType.Emoted, agent.Index, agent.Emote, 0, agent.X, agent.Y));
            if (agent.Emote != EmoteHappy) return true;

            foreach (var other in agents)
            {
                if (!other.Alive || other == agent || Chebyshev(other.X - agent.X, other.Y - agent.Y) > socialRange) continue;
                int pair = Math.Min(agent.Index, other.Index) * familyCap + Math.Max(agent.Index, other.Index);
                if (Tick - exchangedAt[pair] < emoteCooldownTicks) continue;
                exchangedAt[pair] = Tick;
                Befriend(agent, other, deltaEmote);
                AddNeed(agent, Need.Morale, moraleEmoteExchange);
                AddNeed(other, Need.Morale, moraleEmoteExchange);
                events.Add(new SimEvent(SimEventType.EmoteExchanged, agent.Index, other.Index, 0, agent.X, agent.Y));
            }
            return true;
        }

        /// <summary>R12: one of the item goes to the villager in front, if it has room for it. Both think better of each other.</summary>
        bool TryGive(Agent agent, int itemInt, bool execute)
        {
            var item = Data.ItemByInt(itemInt);
            if (item == null) return false;
            int from = agent.FindSlot(itemInt);
            if (from < 0) return false;
            int x = agent.X + agent.FaceDx, y = agent.Y + agent.FaceDy;
            if (!World.InBounds(x, y)) return false;
            int who = World.AgentIndex[World.Index(x, y)];
            if (who < 0 || who == agent.Index || !agents[who].Alive) return false;
            var other = agents[who];
            if (RoomFor(other, item) < 1) return false;
            if (!execute) return true;

            int durability = agent.Inventory[from].Durability;
            agent.Inventory[from].Qty--;
            if (agent.Inventory[from].Qty <= 0) agent.Inventory[from] = default;
            AddItem(other, item, 1, durability);
            Befriend(agent, other, deltaGift);
            events.Add(new SimEvent(SimEventType.Gave, agent.Index, itemInt, other.Index, agent.X, agent.Y));
            return true;
        }

        // ------------------------------------------------------------------ morale from company

        /// <summary>
        /// Sim Need Modifiers: with someone within social_range a villager's morale rises, with nobody
        /// within isolation_range it falls, both by its sociability; its spouse close by lifts it further.
        /// </summary>
        void CompanyMorale(Agent agent)
        {
            bool near = false, alone = true, spouseNear = false;
            foreach (var other in agents)
            {
                if (!other.Alive || other == agent) continue;
                int distance = Chebyshev(other.X - agent.X, other.Y - agent.Y);
                if (distance <= isolationRange) alone = false;
                if (distance > socialRange) continue;
                near = true;
                if (other.Index == agent.Spouse) spouseNear = true;
            }
            if (near) AddNeed(agent, Need.Morale, moraleGroup * agent.Stats.Sociability);
            else if (alone) AddNeed(agent, Need.Morale, moraleIsolated * agent.Stats.Sociability);
            if (spouseNear) AddNeed(agent, Need.Morale, moraleSpouseNear);
        }

        /// <summary>The villager has died: its spouse is widowed, and spouse and parents grieve.</summary>
        void Mourn(Agent dead)
        {
            if (dead.Spouse >= 0)
            {
                var widowed = agents[dead.Spouse];
                widowed.Spouse = -1;
                widowed.TogetherTonight = false;
                Grieve(widowed);
                dead.Spouse = -1;
            }
            if (dead.Parent1 >= 0) Grieve(agents[dead.Parent1]);
            if (dead.Parent2 >= 0) Grieve(agents[dead.Parent2]);
        }

        void Grieve(Agent agent)
        {
            if (!agent.Alive) return;
            AddNeed(agent, Need.Morale, griefMoraleHit);
            agent.GriefUntilTick = Tick + griefTicks;
        }

        // ------------------------------------------------------------------ the tick

        /// <summary>Once a tick, after everyone has acted: ages, marriages, homes shared, and at dawn the children of the night.</summary>
        void UpdateFamilies()
        {
            int count = agents.Count;
            bool newDay = ClockTick % Config.TicksPerDay == 0;
            for (int i = 0; i < count; i++)
            {
                var agent = agents[i];
                if (!agent.Alive) continue;
                NoteStageOfLife(agent);
                // From old_age_death_from on, each new day may be the last.
                if (newDay && AgeOf(agent) >= oldAgeFrom && familyRng.Chance(oldAgeDeathChance)) Kill(agent, DiedOfOldAge);
            }

            for (int i = 0; i < count; i++)
            {
                var a = agents[i];
                if (!a.Alive || a.Child) continue;
                if (a.Spouse > i) LiveTogether(a, agents[a.Spouse]);
                if (a.Spouse >= 0) continue;
                for (int j = i + 1; j < count; j++)
                {
                    var b = agents[j];
                    if (!b.Alive || b.Child || b.Spouse >= 0) continue;
                    int pair = i * familyCap + j;
                    bool close = relation[pair] >= marriageThreshold && relation[j * familyCap + i] >= marriageThreshold && !CloseKin(a, b);
                    if (!close) friendsSince[pair] = -1;
                    else if (friendsSince[pair] < 0) friendsSince[pair] = Tick;
                    else if (Tick - friendsSince[pair] >= marriageSustainTicks)
                    {
                        Marry(a, b);
                        break;
                    }
                }
            }

            bool night = IsNight;
            if (night)
            {
                // A night counts for a couple if at any time in it both were asleep in the home they share.
                for (int i = 0; i < count; i++)
                {
                    var a = agents[i];
                    if (!a.Alive || a.Spouse <= i) continue;
                    var b = agents[a.Spouse];
                    if (a.Home >= 0 && a.Home == b.Home && a.Sleeping && a.Sheltered && b.Sleeping && b.Sheltered) a.TogetherTonight = true;
                }
            }
            else if (familyWasNight)
            {
                for (int i = 0; i < count; i++)
                {
                    var a = agents[i];
                    if (!a.TogetherTonight) continue;
                    a.TogetherTonight = false;
                    if (a.Alive && a.Spouse >= 0) MaybeChild(a, agents[a.Spouse]);
                }
            }
            familyWasNight = night;
        }

        void Marry(Agent a, Agent b)
        {
            a.Spouse = b.Index;
            b.Spouse = a.Index;
            friendsSince[a.Index * familyCap + b.Index] = -1;
            events.Add(new SimEvent(SimEventType.Married, a.Index, b.Index, 0, a.X, a.Y));
            LiveTogether(a, b);
        }

        /// <summary>A couple shares a home: whoever has none, or another one, moves in with the other where there is room.</summary>
        void LiveTogether(Agent a, Agent b)
        {
            if (a.Home == b.Home) return;
            if (a.Home >= 0 && MoveIn(b, a.Home)) return;
            if (b.Home >= 0) MoveIn(a, b.Home);
        }

        bool MoveIn(Agent agent, int home)
        {
            var house = buildings[home];
            if (house.Destroyed || !house.Complete || house.Residents.Count >= house.Def.CapacityAgents) return false;
            if (agent.Home >= 0) buildings[agent.Home].Residents.Remove(agent.Index);
            agent.Home = home;
            house.Residents.Add(agent.Index);
            return true;
        }

        /// <summary>
        /// Morning after a night together at home: with reproduction_chance_per_night a child is born,
        /// unless the couple has had one within reproduction_cooldown_days, has max_children_per_couple
        /// already, or the village is full. It appears beside the home and lives there if there is room.
        /// </summary>
        void MaybeChild(Agent a, Agent b)
        {
            if (!b.Alive || a.Child || b.Child || a.Home < 0 || a.Home != b.Home) return;
            if (Math.Max(a.Children, b.Children) >= maxChildren) return;
            if (Tick - Math.Max(a.LastBirthTick, b.LastBirthTick) < birthCooldownTicks) return;
            int alive = 0;
            foreach (var agent in agents)
                if (agent.Alive) alive++;
            if (alive >= MaxAgents || (MaxVillagersEver > 0 && agents.Count >= MaxVillagersEver)) return;
            if (!familyRng.Chance(birthChance)) return;
            var home = buildings[a.Home];
            if (!FreeTileBeside(home, out int x, out int y)) return;

            int generation = Math.Max(a.Generation, b.Generation) + 1;
            int intCap = Math.Min(Config.CoreStatMax, Config.IntCapGeneration0 + (int)(generation * intCapGain));
            var stats = new AgentStats
            {
                Str = Inherit(a.Stats.Str, b.Stats.Str, Config.CoreStatMax),
                Dex = Inherit(a.Stats.Dex, b.Stats.Dex, Config.CoreStatMax),
                Int = Inherit(a.Stats.Int, b.Stats.Int, intCap),
                End = Inherit(a.Stats.End, b.Stats.End, Config.CoreStatMax),
                Influence = Inherit(a.Stats.Influence, b.Stats.Influence),
                Empathy = Inherit(a.Stats.Empathy, b.Stats.Empathy),
                Sociability = Inherit(a.Stats.Sociability, b.Stats.Sociability),
                Aggression = Inherit(a.Stats.Aggression, b.Stats.Aggression),
            };
            var child = AddAgent(x, y, stats, Tick, grown: false);
            child.Generation = generation;
            child.Parent1 = a.Index;
            child.Parent2 = b.Index;
            a.Children++;
            b.Children++;
            a.LastBirthTick = b.LastBirthTick = Tick;
            MoveIn(child, a.Home);
            events.Add(new SimEvent(SimEventType.Born, child.Index, a.Index, b.Index, x, y));
        }

        /// <summary>The parents' average, give or take stat_mutation, within the stat's range.</summary>
        int Inherit(int one, int other, int max)
        {
            int value = (int)Math.Round((one + other) / 2.0, MidpointRounding.AwayFromZero) + familyRng.Range(-statMutation, statMutation + 1);
            return Math.Max(Config.CoreStatMin, Math.Min(max, value));
        }

        float Inherit(float one, float other)
        {
            float value = (one + other) / 2f + (familyRng.NextFloat() * 2f - 1f) * socialMutation;
            return Math.Max(0f, Math.Min(1f, value));
        }

        /// <summary>A tile next to the building that a villager could stand on, nearest the door side first.</summary>
        bool FreeTileBeside(Building building, out int x, out int y)
        {
            for (int ring = 1; ring <= 3; ring++)
                for (int ty = building.Y - ring; ty < building.Y + building.Height + ring; ty++)
                    for (int tx = building.X - ring; tx < building.X + building.Width + ring; tx++)
                    {
                        bool onRing = tx == building.X - ring || tx == building.X + building.Width + ring - 1
                            || ty == building.Y - ring || ty == building.Y + building.Height + ring - 1;
                        if (!onRing || !CanEnter(tx, ty)) continue;
                        x = tx;
                        y = ty;
                        return true;
                    }
            x = y = -1;
            return false;
        }
    }
}
