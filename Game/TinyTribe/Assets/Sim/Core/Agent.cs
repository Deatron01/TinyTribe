namespace TinyTribe.Sim.Core
{
    /// <summary>One villager. Plain state; Simulation applies the rules.</summary>
    public sealed class Agent
    {
        /// <summary>Position in Simulation.Agents. Never changes, also after death.</summary>
        public int Index;

        public bool Alive = true;
        public int X, Y;

        /// <summary>Direction of the last move. Interactions target the tile in front.</summary>
        public int FaceDx, FaceDy = -1;

        public AgentStats Stats;

        /// <summary>Indexed by the Need enum, each 0 to need_max.</summary>
        public readonly float[] Needs = new float[6];

        /// <summary>Personal inventory. Only the first InventoryCapacity slots are usable.</summary>
        public ItemStack[] Inventory;

        public int InventoryCapacity;

        /// <summary>Indexed by the EquipSlot enum.</summary>
        public readonly ItemStack[] Equipped = new ItemStack[5];

        /// <summary>Ticks left during which the agent cannot act (entering slow terrain).</summary>
        public int BusyTicks;

        /// <summary>Set when energy hit 0; the agent sleeps until it reaches the wake threshold.</summary>
        public bool ForcedSleep;

        /// <summary>True if the agent slept on the last tick.</summary>
        public bool Sleeping;

        /// <summary>Was asleep on the tick before: a sleep that goes on is one sleep.</summary>
        public bool SleptLastTick;

        /// <summary>Health lost to the cold that has not been charged as a take_damage reward yet.</summary>
        public float FrozenHp;

        /// <summary>Index of the building this villager lives in, or -1.</summary>
        public int Home = -1;

        /// <summary>role_int from Sim Roles; 0 is unassigned.</summary>
        public int Role;

        /// <summary>The recipe argument being crafted, or 0, and the progress made on it.</summary>
        public int CraftArg;
        public float CraftProgress;

        /// <summary>True if the agent slept at its own home on the last tick.</summary>
        public bool Sheltered;

        /// <summary>What the agent chose on the last tick it could act, and whether it was valid.</summary>
        public AgentAction LastAction;
        public bool LastActionValid;

        // Derived from stats and equipment; refreshed by Simulation whenever either changes.
        public float GatherSpeedMult = 1f;
        public float EnduranceMult = 1f;
        public float DurabilityLossMult = 1f;
        public float ColdDrainMult = 1f;
        public float CraftSpeedMult = 1f;
        public int SnowMoveCost;

        /// <summary>
        /// By place in the Sim Sicknesses list: 0 when well, the ticks left of a sickness that passes by
        /// itself, or Simulation.UntilCured.
        /// </summary>
        public int[] SickTicks = new int[0];

        /// <summary>The most health the villager can have right now; lower while some sicknesses last.</summary>
        public float MaxHp = 100f;

        /// <summary>Work speed while sick; 1 when well.</summary>
        public float SickWorkMult = 1f;

        /// <summary>Ticks since the villager last ate something that prevents scurvy.</summary>
        public int ScurvyClock;

        /// <summary>Health taken off the maximum by scurvy so far.</summary>
        public float ScurvyDamage;

        /// <summary>Ticks spent asleep at home since a cold began.</summary>
        public int ShelterRestTicks;

        // ---- age and family (Simulation.Family.cs)

        /// <summary>The tick it was born on. Those a game starts with were born before it began, so theirs is below zero.</summary>
        public long BornTick;

        /// <summary>0 for those a game starts with, one more than its parents' for a child.</summary>
        public int Generation;

        /// <summary>Index of the villager it is married to, or -1.</summary>
        public int Spouse = -1;

        /// <summary>Indices of its parents, or -1 for those a game starts with.</summary>
        public int Parent1 = -1, Parent2 = -1;

        /// <summary>How many children were born to it.</summary>
        public int Children;

        public long LastBirthTick = long.MinValue / 4;

        /// <summary>Until this tick it grieves for a spouse or a child: its morale drains faster.</summary>
        public long GriefUntilTick;

        /// <summary>Its stage of life, kept up to date by the simulation: under child_until_age, or elderly_from_age and over.</summary>
        public bool Child, Elder;

        /// <summary>The emote it is showing (emote_int of Sim Observation) until EmoteUntilTick; 0 for none.</summary>
        public int Emote;

        public long EmoteUntilTick;

        /// <summary>It slept this night in the home it shares, with its spouse there too.</summary>
        public bool TogetherTonight;

        public float Get(Need need) => Needs[(int)need];

        public void Set(Need need, float value) => Needs[(int)need] = value;

        /// <summary>Total quantity of the item in the pack (equipped items are not counted).</summary>
        public int Count(int itemInt) => Slots.Count(Inventory, InventoryCapacity, itemInt);

        public int FreeSlots => Slots.Free(Inventory, InventoryCapacity);

        /// <summary>First slot holding the item, or -1.</summary>
        public int FindSlot(int itemInt) => Slots.Find(Inventory, InventoryCapacity, itemInt);
    }
}
