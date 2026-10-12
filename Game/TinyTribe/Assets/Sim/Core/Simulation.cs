using System;
using System.Collections.Generic;
using TinyTribe.Sim.Data;

namespace TinyTribe.Sim.Core
{
    /// <summary>Action ids from Sim Actions, resolved by name once so the rules never use raw numbers.</summary>
    public sealed class ActionIds
    {
        public readonly int MoveNorth, MoveSouth, MoveEast, MoveWest;
        public readonly int Harvest, Deposit, Take, CraftBuild, Wait, Consume, Sleep, Equip, Give, Vote, EmoteHappy;

        public ActionIds(GameData data)
        {
            MoveNorth = data.ActionId("move_north");
            MoveSouth = data.ActionId("move_south");
            MoveEast = data.ActionId("move_east");
            MoveWest = data.ActionId("move_west");
            Harvest = data.ActionId("harvest_attack");
            Deposit = data.ActionId("deposit");
            Take = data.ActionId("take");
            CraftBuild = data.ActionId("craft_build");
            Wait = data.ActionId("emote_none");
            Consume = data.ActionId("consume");
            Sleep = data.ActionId("sleep");
            Equip = data.ActionId("equip");
            Give = data.ActionId("give");
            Vote = data.ActionId("vote");
            EmoteHappy = data.ActionId("emote_happy");
        }
    }

    /// <summary>
    /// The game rules. Holds the world, the villagers, the buildings and the animals, and advances
    /// them one tick at a time. No rendering and no Unity types: the same code runs in the game,
    /// in tests and in headless training.
    ///
    /// This file has the tick loop, movement, needs, sleep, gathering, eating and equipment.
    /// Crafting, buildings and storage, and animals are in the other Simulation.*.cs files.
    /// Age, emotes, giving, friendship, marriage and children are in Simulation.Family.cs.
    /// </summary>
    public sealed partial class Simulation
    {
        // Ids the rules refer to directly (Sim Rules, Sim Equipment effect keys).
        const string WaterSourceNode = "water_source";
        const string FishingSpotNode = "fishing_spot";
        const string ItemPileNode = "item_pile";
        const string WaterItem = "water";
        const string SnowTerrain = "snow";
        const string EffectColdDrain = "cold_drain_mult";
        const string EffectSnowMoveCost = "snow_move_cost";
        const string EffectExtraSlots = "extra_inventory_slots";
        const string EffectBuildSpeed = "build_speed_mult";
        const string AxeTool = "axe";
        const string PickaxeTool = "pickaxe";
        const int OreMinToolTier = 2;

        public GameData Data { get; }
        public SimConfig Config { get; }
        public World World { get; }
        public ActionIds Actions { get; }
        public IReadOnlyList<Agent> Agents => agents;

        /// <summary>What happened during the most recent Step.</summary>
        public IReadOnlyList<SimEvent> Events => events;

        /// <summary>Number of completed ticks.</summary>
        public long Tick { get; private set; }

        readonly List<Agent> agents = new List<Agent>();
        readonly List<SimEvent> events = new List<SimEvent>();
        readonly Rng rng;
        readonly int actionCount;
        readonly int argCount;
        readonly bool[] actionTakesArg;
        readonly NeedDef[] needs;
        readonly bool[] terrainWalkable;
        readonly int[] terrainMoveCost;
        readonly int snowTerrainInt;
        readonly int[] equipSlotByItemInt;
        readonly ItemDef[] nodeYield;
        readonly ItemDef[] nodeSecondary;
        readonly bool[][] nodeInSeason;
        readonly bool[] nodeIsFelled;
        readonly bool[] nodeIsOre;
        readonly int lumberjackRole, minerRole, butcherRole, scoutRole;
        readonly float lumberjackEnergyMult, minerOreSpeedMult, butcherLootMult;
        readonly ResourceNodeDef waterSource;
        readonly ResourceNodeDef fishingSpot;
        readonly ResourceNodeDef itemPile;
        readonly ItemDef waterItem;
        readonly float moveEnergyCost;
        readonly float harvestEnergyCost;
        readonly float craftEnergyCost;
        readonly long clockOffsetTicks;

        public Simulation(GameData data, World world, ulong seed, string startSeason = null)
        {
            Data = data;
            World = world;
            Config = new SimConfig(data);
            Actions = new ActionIds(data);
            rng = new Rng(seed ^ 0x9E3779B97F4A7C15UL);
            clockOffsetTicks = (long)Config.DayStartHour * Config.TicksPerHour
                + (long)SeasonOrder(data, startSeason) * Config.DaysPerSeason * Config.TicksPerDay;

            actionCount = data.Actions.Count;
            argCount = data.ActionArgs.Count;
            actionTakesArg = new bool[actionCount];
            for (int i = 0; i < actionCount; i++) actionTakesArg[i] = data.Actions[i].Argument != "none";
            moveEnergyCost = data.Actions[Actions.MoveNorth].EnergyCost;
            harvestEnergyCost = data.Actions[Actions.Harvest].EnergyCost;
            craftEnergyCost = data.Actions[Actions.CraftBuild].EnergyCost;

            needs = new NeedDef[data.Needs.Count];
            foreach (var need in data.Needs) needs[need.ObsIndex] = need;

            int maxTerrain = 0;
            foreach (var t in data.Terrain) maxTerrain = Math.Max(maxTerrain, t.Int);
            terrainWalkable = new bool[maxTerrain + 1];
            terrainMoveCost = new int[maxTerrain + 1];
            foreach (var t in data.Terrain)
            {
                terrainWalkable[t.Int] = t.Walkable != 0;
                terrainMoveCost[t.Int] = t.MoveCostTicks;
            }
            snowTerrainInt = data.TerrainById(SnowTerrain).Int;

            int maxItem = 0;
            foreach (var item in data.Items) maxItem = Math.Max(maxItem, item.Int);
            equipSlotByItemInt = new int[maxItem + 1];
            for (int i = 0; i <= maxItem; i++)
            {
                var equipment = data.EquipmentByItemInt(i);
                equipSlotByItemInt[i] = equipment == null ? -1 : Array.IndexOf(GameData.EquipSlotNames, equipment.Slot);
            }

            int maxNode = 0;
            foreach (var node in data.ResourceNodes) maxNode = Math.Max(maxNode, node.Int);
            nodeYield = new ItemDef[maxNode + 1];
            nodeSecondary = new ItemDef[maxNode + 1];
            nodeInSeason = new bool[maxNode + 1][];
            nodeIsFelled = new bool[maxNode + 1];
            nodeIsOre = new bool[maxNode + 1];
            foreach (var node in data.ResourceNodes)
            {
                nodeIsFelled[node.Int] = node.ToolType == AxeTool;
                // Ore is what takes a better pickaxe than plain rock does.
                nodeIsOre[node.Int] = node.ToolType == PickaxeTool && node.MinToolTier >= OreMinToolTier;
                data.ItemById.TryGetValue(node.YieldItem ?? "", out nodeYield[node.Int]);
                data.ItemById.TryGetValue(node.SecondaryItem ?? "", out nodeSecondary[node.Int]);
                var inSeason = new bool[data.Seasons.Count];
                var listed = (node.Seasons ?? "all").Split(';');
                foreach (var season in data.Seasons)
                    inSeason[season.Order] = node.Seasons == "all" || Array.IndexOf(listed, season.Id) >= 0;
                nodeInSeason[node.Int] = inSeason;
            }
            waterSource = data.NodeById(WaterSourceNode);
            fishingSpot = data.NodeById(FishingSpotNode);
            itemPile = data.NodeById(ItemPileNode);
            waterItem = data.ItemById[WaterItem];

            foreach (var role in data.Roles)
            {
                switch (role.Id)
                {
                    case "lumberjack": lumberjackRole = role.Int; lumberjackEnergyMult = role.BonusValue; break;
                    case "miner": minerRole = role.Int; minerOreSpeedMult = role.BonusValue; break;
                    case "butcher": butcherRole = role.Int; butcherLootMult = role.BonusValue; break;
                    case "scout": scoutRole = role.Int; break;
                }
            }

            InitCrafting();
            InitAnimals();
            InitCouncil();
            InitWeather();
            InitFarming();
            InitSickness();
            InitRewards();
            InitObservation();
            InitFamily(seed);
        }

        /// <summary>A generated map with the initial villagers standing around the village centre.</summary>
        public static Simulation CreateDefault(GameData data, ulong seed) => Create(data, new SimulationOptions { Seed = seed });

        /// <summary>A generated map, villagers and animals set up as the options ask.</summary>
        public static Simulation Create(GameData data, SimulationOptions options)
        {
            SeasonOrder(data, options.StartSeason);
            var world = WorldGenerator.Generate(data, options.Seed, options.MapWidth, options.MapHeight);
            var sim = new Simulation(data, world, options.Seed, options.StartSeason)
            {
                WeatherEnabled = options.Weather, SicknessEnabled = options.Sickness, JobReviewsEnabled = options.JobReviews,
            };

            if (options.MaxAgents > 0) sim.MaxAgents = options.MaxAgents;
            sim.MaxVillagersEver = options.MaxVillagersEver;
            int wanted = options.Agents > 0 ? options.Agents : sim.Config.InitialAgents;
            wanted = Math.Min(wanted, sim.Config.MaxAgents);
            int maxRadius = Math.Max(world.Width, world.Height);
            for (int radius = 0; sim.agents.Count < wanted && radius <= maxRadius; radius++)
                for (int dy = -radius; dy <= radius; dy++)
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius) continue;
                        if (sim.agents.Count >= wanted) break;
                        int x = world.SpawnX + dx, y = world.SpawnY + dy;
                        if (sim.CanEnter(x, y)) sim.AddAgent(x, y, sim.RollStats());
                    }
            sim.PopulateAnimals();
            return sim;
        }

        /// <summary>Calendar position of a season id; null means the configured start_season.</summary>
        static int SeasonOrder(GameData data, string seasonId)
        {
            seasonId = seasonId ?? data.ConfigString("start_season");
            foreach (var season in data.Seasons)
                if (season.Id == seasonId) return season.Order;
            throw new ArgumentException("Unknown season: " + seasonId);
        }

        // ------------------------------------------------------------------ clock

        long ClockTick => Tick + clockOffsetTicks;

        /// <summary>Hour of the day, 0-23. The simulation starts at day_start_hour.</summary>
        public int Hour => (int)(ClockTick % Config.TicksPerDay) / Config.TicksPerHour;

        /// <summary>Day of the calendar; rolls over at midnight. A game that starts in a later season starts on the first day of that season.</summary>
        public int Day => (int)(ClockTick / Config.TicksPerDay);

        public bool IsNight => Hour >= Config.NightStartHour || Hour < Config.DayStartHour;

        public SeasonDef Season => Data.Seasons[Day / Config.DaysPerSeason % Data.Seasons.Count];

        // ------------------------------------------------------------------ agents and items

        /// <summary>Random stats for a first-generation villager.</summary>
        public AgentStats RollStats()
        {
            int min = Config.CoreStatMin, max = Config.CoreStatMax;
            return new AgentStats
            {
                Str = rng.Range(min, max + 1),
                Dex = rng.Range(min, max + 1),
                Int = rng.Range(min, Config.IntCapGeneration0 + 1),
                End = rng.Range(min, max + 1),
                Influence = rng.NextFloat(),
                Empathy = rng.NextFloat(),
                Sociability = rng.NextFloat(),
                Aggression = rng.NextFloat(),
            };
        }

        /// <summary>A grown villager, of an age such as those a game starts with have. The Council will vote on its job.</summary>
        public Agent AddAgent(int x, int y, AgentStats stats) => AddAgent(x, y, stats, Tick - FounderAgeTicks(), grown: true);

        Agent AddAgent(int x, int y, AgentStats stats, long bornTick, bool grown)
        {
            if (!CanEnter(x, y)) throw new InvalidOperationException("Cannot place an agent at " + x + "," + y);
            var agent = new Agent
            {
                Index = agents.Count,
                X = x,
                Y = y,
                Stats = stats,
                Inventory = new ItemStack[Config.MaxSlots],
                SickTicks = new int[Data.Sicknesses.Count],
                MaxHp = Config.NeedMax,
                BornTick = bornTick,
                Child = !grown,
            };
            foreach (var need in needs) agent.Needs[need.ObsIndex] = need.Start;
            RefreshDerived(agent);
            agents.Add(agent);
            MakeRoomForFamily();
            World.AgentIndex[World.Index(x, y)] = agent.Index;
            // R17: the Council votes on a role for everyone who arrives; for a child, when it comes of age (R22).
            if (grown) roleVotesDue.Enqueue(agent.Index);
            return agent;
        }

        /// <summary>Gives a villager a role from Sim Roles without a vote. For tests and map set-up; in play the Council decides.</summary>
        public void SetRole(Agent agent, string roleId) => agent.Role = Data.RoleInt(roleId);

        /// <summary>Puts items into the agent's pack. Returns how many fitted.</summary>
        public int AddItem(Agent agent, int itemInt, int qty)
        {
            var item = Data.ItemByInt(itemInt);
            if (item == null || qty <= 0) return 0;
            return AddItem(agent, item, qty, Data.EquipmentByItemInt(itemInt)?.Durability ?? 0);
        }

        int AddItem(Agent agent, ItemDef item, int qty, int durability) =>
            Slots.Add(agent.Inventory, agent.InventoryCapacity, item.Int, qty, item.MaxStack, durability);

        int RoomFor(Agent agent, ItemDef item) => Slots.Room(agent.Inventory, agent.InventoryCapacity, item.Int, item.MaxStack);

        /// <summary>Recomputes everything derived from stats and equipment.</summary>
        void RefreshDerived(Agent agent)
        {
            int mid = Config.StatMidpoint;
            // R22: an elder has half its strength and endurance.
            float old = agent.Elder ? elderStatMult : 1f;
            agent.GatherSpeedMult = 1f + Config.StrGatherSpeedPerPoint * (agent.Stats.Str * old - mid);
            agent.EnduranceMult = 1f - Config.EndDrainPerPoint * (agent.Stats.End * old - mid);
            agent.DurabilityLossMult = Math.Max(0f, 1f - Config.DexDurabilityLossPerPoint * (agent.Stats.Dex - mid));
            agent.CraftSpeedMult = 1f + Config.DexSpeedPerPoint * (agent.Stats.Dex - mid);
            agent.ColdDrainMult = EquipmentEffect(agent, EquipSlot.Body, EffectColdDrain, 1f);
            agent.SnowMoveCost = (int)EquipmentEffect(agent, EquipSlot.Feet, EffectSnowMoveCost, 0f);

            int slots = agent.Stats.Str <= SimConfig.LowStrMax ? Config.SlotsStrLow
                : agent.Stats.Str <= SimConfig.MidStrMax ? Config.SlotsStrMid
                : Config.SlotsStrHigh;
            slots += (int)EquipmentEffect(agent, EquipSlot.Back, EffectExtraSlots, 0f);
            agent.InventoryCapacity = Math.Min(slots, Config.MaxSlots);
        }

        float EquipmentEffect(Agent agent, EquipSlot slot, string effectKey, float fallback)
        {
            var stack = agent.Equipped[(int)slot];
            if (stack.IsEmpty) return fallback;
            var equipment = Data.EquipmentByItemInt(stack.Item);
            return equipment != null && equipment.EffectKey == effectKey ? equipment.EffectValue : fallback;
        }

        /// <summary>The tool or weapon in the agent's hand, or null.</summary>
        EquipmentDef HandTool(Agent agent)
        {
            var hand = agent.Equipped[(int)EquipSlot.Hand];
            return hand.IsEmpty ? null : Data.EquipmentByItemInt(hand.Item);
        }

        void AddNeed(Agent agent, Need need, float delta)
        {
            float value = agent.Needs[(int)need] + delta;
            agent.Needs[(int)need] = value < Config.NeedMin ? Config.NeedMin : value > Config.NeedMax ? Config.NeedMax : value;
        }

        static int Chebyshev(int dx, int dy) => Math.Max(Math.Abs(dx), Math.Abs(dy));

        // ------------------------------------------------------------------ tick

        /// <summary>Advances one tick. actions[i] is the choice of Agents[i]; entries for dead agents are ignored.</summary>
        public void Step(AgentAction[] actions)
        {
            if (actions == null || actions.Length != agents.Count)
                throw new ArgumentException("Step needs exactly one action per agent (" + agents.Count + ").");
            events.Clear();
            BeginRewards();

            // Agents act one after another; the starting agent rotates so nobody always goes first (R25).
            int count = agents.Count;
            int start = count == 0 ? 0 : (int)(Tick % count);
            for (int k = 0; k < count; k++)
            {
                int i = start + k;
                if (i >= count) i -= count;
                if (agents[i].Alive) Act(agents[i], actions[i]);
            }

            RegrowNodes();
            UpdateCrops();
            WeatherTick();
            BurnCampfires();
            UpdateAnimals();

            bool night = IsNight;
            var season = Season;
            for (int i = 0; i < count; i++)
                if (agents[i].Alive) UpdateNeeds(agents[i], night, season);

            Tick++;
            AdvanceWeather();
            UpdateCouncil();
            UpdateFamilies();
            SettleRewards();
        }

        void Act(Agent agent, AgentAction action)
        {
            agent.SleptLastTick = agent.Sleeping;
            agent.Sleeping = false;
            agent.Sheltered = false;
            agent.LastActionValid = true;
            if (agent.BusyTicks > 0)
            {
                agent.BusyTicks--;
                agent.LastAction = new AgentAction(Actions.Wait);
                return;
            }
            if (agent.ForcedSleep)
            {
                if (IsForcedAsleep(agent))
                {
                    CancelCraft(agent);
                    agent.LastAction = new AgentAction(Actions.Sleep);
                    Sleep(agent);
                    return;
                }
                agent.ForcedSleep = false;
            }

            agent.LastAction = action;
            int id = action.Id;
            // Anything other than carrying on with the same recipe abandons it and returns the inputs (R07).
            if (agent.CraftArg != 0 && !(id == Actions.CraftBuild && action.Arg == agent.CraftArg)) CancelCraft(agent);

            bool done;
            if (id == Actions.MoveNorth) done = Move(agent, 0, 1);
            else if (id == Actions.MoveSouth) done = Move(agent, 0, -1);
            else if (id == Actions.MoveEast) done = Move(agent, 1, 0);
            else if (id == Actions.MoveWest) done = Move(agent, -1, 0);
            else if (id == Actions.Wait) done = true;
            else if (id == Actions.Sleep)
            {
                // Lying down worn out, by choice: collapsing at zero is not rewarded.
                if (!agent.SleptLastTick && agent.Get(Need.Energy) < Config.RewardExhaustedEnergyBelow) Pay(agent, sleepWhenExhausted);
                done = Sleep(agent);
            }
            else if (id == Actions.Harvest) done = TryHarvestOrAttack(agent, true);
            else if (id == Actions.Consume) done = TryConsume(agent, action.Arg, true);
            else if (id == Actions.Equip) done = TryEquip(agent, action.Arg, true);
            else if (id == Actions.CraftBuild) done = TryCraftBuild(agent, action.Arg, true);
            else if (id == Actions.Deposit) done = TryDeposit(agent, action.Arg, true);
            else if (id == Actions.Take) done = TryTake(agent, action.Arg, true);
            else if (id == Actions.Vote) done = TryVote(agent, action.Arg, true);
            else if (id == Actions.Give) done = TryGive(agent, action.Arg, true);
            else if (emoteOfAction[id] != 0) done = ShowEmote(agent, id);
            else done = false;
            if (done) return;

            CancelCraft(agent);
            agent.LastActionValid = false;
            events.Add(new SimEvent(SimEventType.InvalidAction, agent.Index, id, action.Arg, agent.X, agent.Y));
        }

        bool IsForcedAsleep(Agent agent) => agent.ForcedSleep && agent.Needs[(int)Need.Energy] < Config.ForcedSleepWakeEnergy;

        // ------------------------------------------------------------------ validity and masks

        /// <summary>Would this action do something if the agent chose it now?</summary>
        public bool IsValid(int agentIndex, AgentAction action)
        {
            var agent = agents[agentIndex];
            int id = action.Id;
            if (!agent.Alive || (uint)id >= (uint)actionCount) return false;
            if (agent.BusyTicks > 0) return id == Actions.Wait;
            if (IsForcedAsleep(agent)) return id == Actions.Sleep;

            // R23: heatstroke pins a villager where it stands.
            if (id == Actions.MoveNorth || id == Actions.MoveSouth || id == Actions.MoveEast || id == Actions.MoveWest) return !IsImmobile(agent);
            if (id == Actions.Wait || id == Actions.Sleep) return true;
            if (id == Actions.Harvest) return TryHarvestOrAttack(agent, false);
            if (id == Actions.Consume) return TryConsume(agent, action.Arg, false);
            if (id == Actions.Equip) return TryEquip(agent, action.Arg, false);
            if (id == Actions.CraftBuild) return TryCraftBuild(agent, action.Arg, false);
            if (id == Actions.Deposit) return TryDeposit(agent, action.Arg, false);
            if (id == Actions.Take) return TryTake(agent, action.Arg, false);
            if (id == Actions.Vote) return TryVote(agent, action.Arg, false);
            if (id == Actions.Give) return TryGive(agent, action.Arg, false);
            return emoteOfAction[id] != 0;
        }

        /// <summary>mask[id] is true when the action is valid with at least one argument. Length num_actions.</summary>
        public void GetActionMask(int agentIndex, bool[] mask)
        {
            for (int id = 0; id < actionCount; id++)
            {
                bool valid = false;
                if (!actionTakesArg[id]) valid = IsValid(agentIndex, new AgentAction(id));
                else
                    for (int arg = 0; arg < argCount && !valid; arg++)
                        valid = IsValid(agentIndex, new AgentAction(id, arg));
                mask[id] = valid;
            }
        }

        /// <summary>mask[arg] is true when the action is valid with that argument. Length num_action_args.</summary>
        public void GetArgMask(int agentIndex, int actionId, bool[] mask)
        {
            bool takesArg = (uint)actionId < (uint)actionCount && actionTakesArg[actionId];
            for (int arg = 0; arg < argCount; arg++)
                mask[arg] = (takesArg || arg == 0) && IsValid(agentIndex, new AgentAction(actionId, arg));
        }

        // ------------------------------------------------------------------ movement

        /// <summary>R25: walkable terrain with no active node, building, animal or living agent.</summary>
        public bool CanEnter(int x, int y)
        {
            if (!World.InBounds(x, y)) return false;
            int tile = World.Index(x, y);
            return terrainWalkable[World.Terrain[tile]] && !World.HasActiveNode(tile)
                && World.AgentIndex[tile] == -1 && World.AnimalIndex[tile] == -1 && World.BuildingIndex[tile] == -1;
        }

        bool Move(Agent agent, int dx, int dy)
        {
            if (IsImmobile(agent)) return false;
            agent.FaceDx = dx;
            agent.FaceDy = dy;
            int x = agent.X + dx, y = agent.Y + dy;
            if (!CanEnter(x, y)) return true;

            int tile = World.Index(x, y);
            World.AgentIndex[World.Index(agent.X, agent.Y)] = -1;
            World.AgentIndex[tile] = agent.Index;
            agent.X = x;
            agent.Y = y;
            AddNeed(agent, Need.Energy, -moveEnergyCost * agent.EnduranceMult);

            int terrain = World.Terrain[tile];
            int cost = terrainMoveCost[terrain];
            if (terrain == snowTerrainInt && agent.SnowMoveCost > 0) cost = agent.SnowMoveCost;
            if (cost > 1) agent.BusyTicks = cost - 1;
            return true;
        }

        // ------------------------------------------------------------------ sleep

        /// <summary>R11: sleeping while facing a house with room claims it; at home the sleep is sheltered.</summary>
        bool Sleep(Agent agent)
        {
            agent.Sleeping = true;
            float gain = IsNight ? Config.EnergySleepNight : Config.EnergySleepDay;

            var house = BuildingFacing(agent);
            if (house != null && house.Complete && IsHouse(house.Def))
            {
                if (agent.Home < 0 && house.Residents.Count < house.Def.CapacityAgents)
                {
                    agent.Home = house.Index;
                    house.Residents.Add(agent.Index);
                    events.Add(new SimEvent(SimEventType.HomeClaimed, agent.Index, house.Def.Int, 0, house.X, house.Y));
                }
                if (agent.Home == house.Index)
                {
                    agent.Sheltered = true;
                    gain *= Config.EnergySleepSheltered;
                }
            }
            AddNeed(agent, Need.Energy, gain);
            return true;
        }

        // ------------------------------------------------------------------ gathering

        struct HarvestPlan
        {
            public int X, Y, Tile;
            public ResourceNodeDef TileNode;
            public ResourceNodeDef Node;
            public ItemDef Yield;
            public int YieldQty;
            public float Speed;
            public EquipmentDef Tool;
        }

        /// <summary>R03: a pile or node on the faced tile is worked; otherwise an animal in reach is hit.</summary>
        bool TryHarvestOrAttack(Agent agent, bool execute)
        {
            int x = agent.X + agent.FaceDx, y = agent.Y + agent.FaceDy;
            if (World.InBounds(x, y) && World.Node[World.Index(x, y)] == itemPile.Int) return TryTakePile(agent, x, y, execute);

            if (TryPlanHarvest(agent, x, y, out var plan))
            {
                if (execute) Harvest(agent, plan);
                return true;
            }
            // R22: a child does not fight.
            if (agent.Child) return false;
            var target = FindAttackTarget(agent);
            if (target == null) return false;
            if (execute) Attack(agent, target);
            return true;
        }

        /// <summary>
        /// Could this agent harvest the node on that tile with what it has equipped and the room in its
        /// inventory, if it stood next to it? Used by bots to pick targets.
        /// </summary>
        public bool CouldHarvest(Agent agent, int x, int y) => TryPlanHarvest(agent, x, y, out _);

        /// <summary>R02, R04, R26, R27, R31: what harvesting that tile would do, or false if it cannot be harvested.</summary>
        bool TryPlanHarvest(Agent agent, int x, int y, out HarvestPlan plan)
        {
            plan = default;
            if (!World.InBounds(x, y)) return false;
            int tile = World.Index(x, y);

            ResourceNodeDef tileNode;
            if (World.HasActiveNode(tile)) tileNode = Data.NodeByInt(World.Node[tile]);
            else if (IsWaterBuilding(tile)) tileNode = waterSource;
            else return false;

            var tool = HandTool(agent);
            var node = tileNode;
            if (node == waterSource && tool != null && tool.ToolType == fishingSpot.ToolType) node = fishingSpot;
            if (!nodeInSeason[node.Int][Season.Order]) return false;
            // R22: felling, quarrying and mining are not for children.
            if (agent.Child && nodeHardLabour[node.Int]) return false;
            var yield = nodeYield[node.Int];
            int yieldQty = node.YieldQty;
            // R06: a ripe crop gives what was sown there.
            Crop crop = null;
            bool ripeCrop = node == cropMature && crops.TryGetValue(tile, out crop);
            if (ripeCrop)
            {
                yield = Data.ItemById[crop.Def.YieldItem];
                yieldQty = crop.Def.YieldQty;
            }
            if (yield == null) return false;

            float speed;
            EquipmentDef usedTool = null;
            if (string.IsNullOrEmpty(node.ToolType)) speed = 1f;
            else if (tool != null && tool.ToolType == node.ToolType && tool.Tier >= node.MinToolTier)
            {
                speed = tool.SpeedMult;
                usedTool = tool;
            }
            else if (node.MinToolTier == 0) speed = Config.BareHandSpeedMult;
            else return false;
            // R36: ore is a Miner's work; anyone else is slow at it.
            if (nodeIsOre[node.Int]) speed *= agent.Role == minerRole ? minerOreSpeedMult : Config.NonMinerOreSpeedMult;
            // R06: a hoe makes short work of reaping.
            if (ripeCrop && tool != null && tool.ToolType == HoeTool)
            {
                speed = tool.SpeedMult;
                usedTool = tool;
            }

            if (RoomFor(agent, yield) < yieldQty) return false;

            plan = new HarvestPlan
            {
                X = x, Y = y, Tile = tile, TileNode = tileNode, Node = node, Yield = yield, YieldQty = yieldQty,
                Speed = speed * agent.GatherSpeedMult * agent.SickWorkMult, Tool = usedTool,
            };
            return true;
        }

        void Harvest(Agent agent, HarvestPlan plan)
        {
            float toolCost = plan.Tool != null ? plan.Tool.EnergyCostMult : 1f;
            // R36: felling trees tires a Lumberjack less.
            if (agent.Role == lumberjackRole && nodeIsFelled[plan.Node.Int]) toolCost *= lumberjackEnergyMult;
            AddNeed(agent, Need.Energy, -harvestEnergyCost * toolCost * agent.EnduranceMult * weatherWorkEnergyMult);
            AddNeed(agent, Need.Thirst, Config.ThirstWorkAction);
            SickFromWork(agent);

            int tile = plan.Tile;
            World.NodeProgress[tile] += plan.Speed;
            if (World.NodeProgress[tile] + 1e-4f < plan.Node.HarvestTicks) return;
            World.NodeProgress[tile] = Math.Max(0f, World.NodeProgress[tile] - plan.Node.HarvestTicks);

            AddItem(agent, plan.Yield, plan.YieldQty, 0);
            events.Add(new SimEvent(SimEventType.Harvested, agent.Index, plan.Yield.Int, plan.YieldQty, plan.X, plan.Y));

            var secondary = nodeSecondary[plan.Node.Int];
            if (secondary != null && rng.Chance(plan.Node.SecondaryChance) && AddItem(agent, secondary, 1, 0) == 1)
                events.Add(new SimEvent(SimEventType.Harvested, agent.Index, secondary.Int, 1, plan.X, plan.Y));

            if (World.NodeCharges[tile] > 0 && --World.NodeCharges[tile] == 0) Deplete(plan);
            if (plan.TileNode == cropMature) ReapCrop(agent, tile);
            if (plan.Tool != null && plan.Tool.Durability > 0) WearTool(agent);
        }

        void Deplete(HarvestPlan plan)
        {
            int tile = plan.Tile;
            World.NodeProgress[tile] = 0f;
            events.Add(new SimEvent(SimEventType.NodeDepleted, -1, plan.TileNode.Int, 0, plan.X, plan.Y));
            if (plan.TileNode.RegrowTicks > 0)
            {
                World.NodeRegrowTick[tile] = Tick + plan.TileNode.RegrowTicks;
                World.Regrowing.Add(tile);
            }
            else World.RemoveNode(tile);
        }

        void WearTool(Agent agent)
        {
            // A multiplier of 1.12 means one point plus a 12% chance of a second.
            float mult = agent.DurabilityLossMult;
            int loss = (int)mult;
            float fraction = mult - loss;
            if (fraction > 0f && rng.Chance(fraction)) loss++;

            ref var hand = ref agent.Equipped[(int)EquipSlot.Hand];
            hand.Durability -= loss;
            if (hand.Durability > 0) return;
            events.Add(new SimEvent(SimEventType.ToolBroke, agent.Index, hand.Item, 1, agent.X, agent.Y));
            hand = default;
        }

        void RegrowNodes()
        {
            var regrowing = World.Regrowing;
            for (int k = regrowing.Count - 1; k >= 0; k--)
            {
                int tile = regrowing[k];
                if (Tick < World.NodeRegrowTick[tile] || World.AgentIndex[tile] != -1 || World.AnimalIndex[tile] != -1) continue;

                var node = Data.NodeByInt(World.Node[tile]);
                World.NodeCharges[tile] = (short)node.Charges;
                World.NodeProgress[tile] = 0f;
                World.NodeRegrowTick[tile] = 0;
                regrowing[k] = regrowing[regrowing.Count - 1];
                regrowing.RemoveAt(regrowing.Count - 1);
                events.Add(new SimEvent(SimEventType.NodeRegrown, -1, node.Int, 0, tile % World.Width, tile / World.Width));
            }
        }

        // ------------------------------------------------------------------ eating and drinking

        /// <summary>A water tile or a finished well in front of the agent.</summary>
        bool FacesWater(Agent agent)
        {
            int x = agent.X + agent.FaceDx, y = agent.Y + agent.FaceDy;
            if (!World.InBounds(x, y)) return false;
            int tile = World.Index(x, y);
            return (World.HasActiveNode(tile) && World.Node[tile] == waterSource.Int) || IsWaterBuilding(tile);
        }

        /// <summary>Argument 0 drinks from the faced water; any other argument is the item_int to use.</summary>
        bool TryConsume(Agent agent, int arg, bool execute)
        {
            ItemDef item;
            if (arg == 0)
            {
                if (!FacesWater(agent)) return false;
                item = waterItem;
            }
            else
            {
                item = Data.ItemByInt(arg);
                if (item == null || item.Consumable == 0 || agent.Count(arg) == 0) return false;
            }
            if (!execute) return true;

            if (arg != 0) Slots.Remove(agent.Inventory, agent.InventoryCapacity, arg, 1);
            if ((item.Hunger > 0f && agent.Get(Need.Hunger) < Config.RewardLowNeedBelow)
                || (item.Thirst > 0f && agent.Get(Need.Thirst) < Config.RewardLowNeedBelow)) Pay(agent, consumeWhenLow);
            AddNeed(agent, Need.Hunger, item.Hunger);
            AddNeed(agent, Need.Thirst, item.Thirst);
            AddNeed(agent, Need.Energy, item.Energy);
            AddNeed(agent, Need.Hp, item.Hp);
            AddNeed(agent, Need.Morale, item.Morale);
            AddNeed(agent, Need.Temp, item.Temp);
            events.Add(new SimEvent(SimEventType.Consumed, agent.Index, item.Int, 1, agent.X, agent.Y));
            SickFromEating(agent, item);
            return true;
        }

        // ------------------------------------------------------------------ equipment

        /// <summary>Argument 0 puts the hand item back in the pack; any other argument is the item_int to equip.</summary>
        bool TryEquip(Agent agent, int arg, bool execute)
        {
            if (arg == 0)
            {
                if (agent.Equipped[(int)EquipSlot.Hand].IsEmpty || agent.FreeSlots == 0) return false;
                if (!execute) return true;
                for (int i = 0; i < agent.InventoryCapacity; i++)
                {
                    if (!agent.Inventory[i].IsEmpty) continue;
                    agent.Inventory[i] = agent.Equipped[(int)EquipSlot.Hand];
                    agent.Equipped[(int)EquipSlot.Hand] = default;
                    break;
                }
            }
            else
            {
                if ((uint)arg >= (uint)equipSlotByItemInt.Length || equipSlotByItemInt[arg] < 0) return false;
                int from = agent.FindSlot(arg);
                if (from < 0) return false;
                if (!execute) return true;
                // The item being replaced takes the pack slot the new one came from.
                int slot = equipSlotByItemInt[arg];
                var incoming = agent.Inventory[from];
                agent.Inventory[from] = agent.Equipped[slot];
                agent.Equipped[slot] = incoming;
            }
            RefreshDerived(agent);
            return true;
        }

        // ------------------------------------------------------------------ needs

        void UpdateNeeds(Agent agent, bool night, SeasonDef season)
        {
            var values = agent.Needs;
            float sickHp = UpdateSickness(agent);
            var sick = agent.SickTicks;
            // R36: a Scout out beyond the village travels light.
            bool traveling = agent.Role == scoutRole && Chebyshev(agent.X - World.SpawnX, agent.Y - World.SpawnY) > Config.VillageRadius;
            float hunger = needs[(int)Need.Hunger].BaseDrainPerTick;
            if (traveling) hunger *= Config.HungerScoutTraveling;
            if (sick[poisoningIndex] != 0) hunger *= poisoningDef.Effect(SickHungerDrain);
            AddNeed(agent, Need.Hunger, -hunger);

            float thirst = needs[(int)Need.Thirst].BaseDrainPerTick;
            if (!night && season.Order == Config.SummerOrder) thirst *= Config.ThirstSummerDay;
            if (traveling) thirst *= Config.ThirstScoutTraveling;
            // R37: a heatwave, less so under a hat.
            if (weatherThirstMult != 1f) thirst *= weatherThirstMult * EquipmentEffect(agent, EquipSlot.Head, EffectHatThirst, 1f);
            if (sick[heatstrokeIndex] != 0) thirst *= heatstrokeDef.Effect(SickThirstDrain);
            AddNeed(agent, Need.Thirst, -thirst);

            float tiring = sick[coldIndex] != 0 ? coldDef.Effect(SickEnergyDrain) : 1f;
            AddNeed(agent, Need.Energy, -needs[(int)Need.Energy].BaseDrainPerTick * agent.EnduranceMult * tiring);
            AddNeed(agent, Need.Morale, -needs[(int)Need.Morale].BaseDrainPerTick * (Tick < agent.GriefUntilTick ? griefDrainMult : 1f));
            CompanyMorale(agent);

            // Warmth: home, then a fire, then the weather (R11, R30).
            float ambient = night ? season.TempDeltaNight : season.TempDeltaDay;
            if (agent.Sheltered)
            {
                AddNeed(agent, Need.Temp, Config.TempIndoors);
                var home = buildings[agent.Home];
                if (home.Def.EffectKey == EffectMoraleInside) AddNeed(agent, Need.Morale, home.Def.EffectValue);
            }
            else if (NearLitCampfire(agent)) AddNeed(agent, Need.Temp, Config.TempCampfire);
            else if (ambient < 0f) AddNeed(agent, Need.Temp, ambient * agent.EnduranceMult * agent.ColdDrainMult);
            else AddNeed(agent, Need.Temp, Config.TempRecovery);
            // R37: rain chills everyone who is not asleep at home.
            if (weatherChill != 0f && !agent.Sheltered) AddNeed(agent, Need.Temp, weatherChill);

            float hpLoss = 0f;
            int cause = -1;
            for (int i = 1; i < needs.Length; i++)
            {
                if (values[i] > Config.NeedMin || needs[i].HpDeltaPerTickAtZero == 0f) continue;
                hpLoss += needs[i].HpDeltaPerTickAtZero;
                if (i == (int)Need.Temp) PayForFreezing(agent, -needs[i].HpDeltaPerTickAtZero);
                if (cause < 0) cause = i;
            }
            if (hpLoss < 0f) AddNeed(agent, Need.Hp, hpLoss);
            else if (values[(int)Need.Hunger] > Config.HpRegenMinNeed && values[(int)Need.Thirst] > Config.HpRegenMinNeed)
                AddNeed(agent, Need.Hp, Config.HpRegen);
            // R39: what a sickness takes, and the cap it puts on health.
            if (sickHp != 0f)
            {
                AddNeed(agent, Need.Hp, sickHp);
                if (cause < 0) cause = DiedOfSickness;
            }
            if (values[(int)Need.Hp] > agent.MaxHp) values[(int)Need.Hp] = agent.MaxHp;

            if (values[(int)Need.Hp] <= Config.NeedMin) Kill(agent, cause);
            else if (values[(int)Need.Energy] <= Config.NeedMin) agent.ForcedSleep = true;
        }

        /// <summary>In the Died event's Item field: the villager died of a sickness.</summary>
        public const int DiedOfSickness = -2;

        /// <summary>
        /// R05, R28. The villager leaves the map, its home, and a pile with what it carried and wore.
        /// The Died event's Item field holds the index of the need that caused it, -1 for an attack,
        /// or DiedOfSickness.
        /// </summary>
        void Kill(Agent agent, int causeNeed)
        {
            if (!agent.Alive) return;
            agent.Alive = false;
            if (agent.Role != 0) JobsMayHaveChanged();
            int tile = World.Index(agent.X, agent.Y);
            World.AgentIndex[tile] = -1;
            events.Add(new SimEvent(SimEventType.Died, agent.Index, causeNeed, 0, agent.X, agent.Y));

            if (agent.Home >= 0) buildings[agent.Home].Residents.Remove(agent.Index);
            agent.Home = -1;
            agent.CraftArg = 0;
            Mourn(agent);
            for (int i = 0; i < agent.InventoryCapacity; i++)
            {
                AddToPile(tile, agent.Inventory[i]);
                agent.Inventory[i] = default;
            }
            for (int i = 0; i < agent.Equipped.Length; i++)
            {
                AddToPile(tile, agent.Equipped[i]);
                agent.Equipped[i] = default;
            }
        }

        // ------------------------------------------------------------------ determinism check

        /// <summary>
        /// A fingerprint of the whole simulation state. Two runs with the same seed and the
        /// same actions must give the same value; used by tests and to verify replays.
        /// </summary>
        public ulong ComputeStateHash()
        {
            ulong hash = 14695981039346656037UL;
            void Mix(ulong value)
            {
                hash ^= value;
                hash = unchecked(hash * 1099511628211UL);
            }
            void MixInt(int value) => Mix(unchecked((ulong)(uint)value));
            void MixFloat(float value) => MixInt(BitConverter.SingleToInt32Bits(value));
            void MixStack(ItemStack stack)
            {
                MixInt(stack.Item);
                MixInt(stack.Qty);
                MixInt(stack.Durability);
            }

            Mix((ulong)Tick);
            Mix(rng.State);
            Mix(familyRng.State);
            foreach (float score in relation) MixFloat(score);
            foreach (var agent in agents)
            {
                MixInt(agent.Alive ? 1 : 0);
                MixInt(agent.X);
                MixInt(agent.Y);
                MixInt(agent.FaceDx);
                MixInt(agent.FaceDy);
                MixInt(agent.BusyTicks);
                MixInt(agent.ForcedSleep ? 1 : 0);
                MixInt(agent.Home);
                MixInt(agent.Role);
                MixInt(agent.CraftArg);
                MixFloat(agent.CraftProgress);
                foreach (float need in agent.Needs) MixFloat(need);
                foreach (int ticks in agent.SickTicks) MixInt(ticks);
                MixInt(agent.ScurvyClock);
                MixFloat(agent.ScurvyDamage);
                MixInt(agent.ShelterRestTicks);
                Mix((ulong)agent.BornTick);
                MixInt(agent.Spouse);
                MixInt(agent.Children);
                Mix((ulong)agent.GriefUntilTick);
                MixInt(agent.Emote);
                foreach (var stack in agent.Inventory) MixStack(stack);
                foreach (var stack in agent.Equipped) MixStack(stack);
            }
            for (int i = 0; i < World.Node.Length; i++)
            {
                MixInt(World.Terrain[i]);
                MixInt(World.Node[i]);
                MixInt(World.NodeCharges[i]);
                MixFloat(World.NodeProgress[i]);
                Mix((ulong)World.NodeRegrowTick[i]);
            }
            foreach (var building in buildings)
            {
                MixInt(building.Def.Int);
                MixInt(building.X);
                MixInt(building.Y);
                MixFloat(building.Progress);
                MixInt(building.Complete ? 1 : 0);
                MixInt(building.Destroyed ? 1 : 0);
                MixInt(building.FuelTicks);
                MixInt(building.Residents.Count);
                if (building.Storage != null)
                    foreach (var stack in building.Storage) MixStack(stack);
            }
            foreach (var animal in animals)
            {
                MixInt(animal.Alive ? 1 : 0);
                MixInt(animal.Def.Int);
                MixInt(animal.X);
                MixInt(animal.Y);
                MixFloat(animal.Hp);
                MixFloat(animal.MoveBudget);
                MixInt(animal.AttackCooldown);
            }
            foreach (var pile in piles)
            {
                MixInt(pile.Key);
                foreach (var stack in pile.Value) MixStack(stack);
            }
            foreach (string tech in researched) MixInt(tech.Length);
            if (openVote != null)
            {
                MixInt((int)openVote.Kind);
                MixInt(openVote.Subject);
                MixInt(openVote.Review ? 1 : 0);
                Mix((ulong)openVote.ClosesTick);
                for (int i = 0; i < openVote.BallotCount; i++) MixInt(openVote.BallotOf(i));
            }
            foreach (int waiting in roleVotesDue) MixInt(waiting);
            foreach (int waiting in reviewsDue) MixInt(waiting);
            MixInt(reviewCalled ? 1 : 0);
            Mix((ulong)nextReviewTick);
            MixInt(researching != null ? researching.Id.Length : 0);
            MixFloat(researchPoints);
            Mix((ulong)nextResearchVoteTick);
            foreach (var planted in crops)
            {
                MixInt(planted.Key);
                MixInt(planted.Value.DefIndex);
                MixFloat(planted.Value.Growth);
                MixFloat(planted.Value.Pace);
            }
            MixInt(weather.Id.Length);
            MixInt(weather.DurationTicks);
            Mix((ulong)weatherEndsTick);
            if (comingWeather != null)
            {
                MixInt(comingWeather.DurationTicks);
                Mix((ulong)comingWeatherTick);
            }
            return hash;
        }
    }
}
