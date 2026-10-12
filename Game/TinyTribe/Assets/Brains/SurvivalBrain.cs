using System;
using System.Collections.Generic;
using TinyTribe.Sim.Core;
using TinyTribe.Sim.Data;

namespace TinyTribe.Brains
{
    /// <summary>
    /// A hand-written villager. In this order it: defends itself, drinks, eats, keeps the village
    /// fire burning, sleeps by the fire at night, warms up when cold, makes and holds a spear,
    /// keeps food and water in its pack, tidies its pack into the village chest, and then works
    /// for the village. One villager builds the chest and the campfire and tends the fire;
    /// everyone fills the chest with food and cooks the meat in it.
    ///
    /// It sees the whole map, which a trained network will not. Exists so there is something to
    /// watch and test against before any model is trained. One instance drives any number of villagers.
    ///
    /// This file has the order of decisions and the pack; SurvivalBrain.Village.cs has animals,
    /// building and the fire; SurvivalBrain.Routes.cs finds the way.
    /// </summary>
    public sealed partial class SurvivalBrain : IBrain
    {
        // Bot habits, not game balance.
        const float DrinkBelow = 45f;
        const float EatBelow = 45f;
        const float SleepBelowByDay = 12f;
        const float NapUntil = 35f;
        const float FoodStockTarget = 60f;
        const float FoodStockLow = 30f;
        const int WaterStockTarget = 4;
        const float ColdBelow = 50f;
        const float WarmEnough = 85f;
        const float WanderChance = 0.15f;
        const int ChestSlotsKeptFree = 3;

        // What the bot knows by name.
        const string WaterId = "water", WoodId = "pine_wood", StoneId = "cobblestone";
        const string RawMeatId = "raw_meat", CookedMeatId = "cooked_meat";
        const string SpearId = "wooden_spear", PickaxeId = "wooden_pickaxe";
        const string RodId = "fishing_rod", CottonId = "cotton", RawFishId = "raw_fish", CookedFishId = "cooked_fish";
        const string ChestId = "basic_chest", CampfireId = "campfire";
        const string WaterNodeId = "water_source";

        static readonly int[] StepX = { 1, -1, 0, 0 };
        static readonly int[] StepY = { 0, 0, 1, -1 };

        readonly Rng rng;
        readonly List<Plan> plans = new List<Plan>();

        /// <summary>
        /// When set, a villager's plan (its route, its "until the pack is full") is kept only for as long
        /// as the villager does what it was advised. The moment it does something else, what it should
        /// do is worked out afresh from where it is and what it is doing. That is how the bot advises a
        /// learner (EnvOptions.Expert): advice that hangs on a plan the learner left, and cannot see,
        /// is advice it cannot learn from. Followed to the letter, this is the bot exactly.
        /// </summary>
        public bool Memoryless;

        /// <summary>
        /// With Memoryless: no plan is kept at all, not even while the villager does as advised. What it
        /// should do is worked out from the situation on every tick, so the same situation always gets
        /// the same advice, whatever went before. (Kept plans hold things a learner cannot see: that
        /// the villager is in the middle of a trip for food or water, which goes on until the pack is full.)
        /// </summary>
        public bool Situational;

        // Looked up once per simulation.
        Simulation boundSim;
        long boundTick;
        int waterItem, woodItem, stoneItem, rawMeatItem, cookedMeatItem, spearItem, pickaxeItem;
        int rodItem, cottonItem, rawFishItem, cookedFishItem;
        int rodArg, cookFishArg, rodWood, rodCotton;
        bool[] nodeIsCotton;
        int waterNode;
        int spearArg, pickaxeArg, cookArg, chestArg, fireArg;
        int spearWood, pickaxeWood;
        float spearDamage;
        BuildingDef chestDef, fireDef;
        bool[] itemIsSafeFood;
        float[] itemHunger;
        int[] itemMaxStack;
        bool[] nodeIsFood, nodeIsWood, nodeIsStone;
        int[] nodeYieldItem, nodeYieldQty;
        bool[] terrainWalkable;

        public SurvivalBrain(ulong seed) => rng = new Rng(seed ^ 0xB5297A4D3F84D5B5UL);

        public AgentAction Decide(Simulation sim, int agentIndex)
        {
            Bind(sim);
            while (plans.Count <= agentIndex) plans.Add(new Plan());
            var plan = plans[agentIndex];
            if (Memoryless)
            {
                var did = sim.Agents[agentIndex].LastAction;
                bool obeyed = plan.AdvisedAt == sim.Tick - 1 && did.Id == plan.Advised.Id && did.Arg == plan.Advised.Arg;
                if (!obeyed || Situational)
                {
                    LookAround(sim);
                    FromTheSituation(sim, sim.Agents[agentIndex], plan);
                }
            }
            plan.Advised = Greet(sim, sim.Agents[agentIndex], plan, Choose(sim, agentIndex, plan));
            plan.AdvisedAt = sim.Tick;
            return plan.Advised;
        }

        AgentAction Choose(Simulation sim, int agentIndex, Plan plan)
        {
            plan.AskedAt = sim.Tick;
            LookAround(sim);
            var agent = sim.Agents[agentIndex];
            var actions = sim.Actions;
            var harvest = new AgentAction(actions.Harvest);

            if (agent.BusyTicks > 0) return new AgentAction(actions.Wait);
            if (agent.ForcedSleep && agent.Get(Need.Energy) < sim.Config.ForcedSleepWakeEnergy) return new AgentAction(actions.Sleep);

            if (sim.IsImmobile(agent))
            {
                // Heatstroke: no walking until it has had water.
                var water = new AgentAction(actions.Consume, agent.Count(waterItem) > 0 ? waterItem : 0);
                return sim.IsValid(agentIndex, water) ? water : new AgentAction(actions.Wait);
            }

            if (Defend(sim, agent, plan, out var action)) return action;
            if (CastVote(sim, agent, out action)) return action;

            if (agent.Get(Need.Thirst) < DrinkBelow)
            {
                if (agent.Count(waterItem) > 0) return new AgentAction(actions.Consume, waterItem);
                if (Go(sim, agent, plan, Goal.Drink, new AgentAction(actions.Consume, 0), out action)) return action;
            }

            if (agent.Get(Need.Hunger) < EatBelow)
            {
                int food = CarriedFood(agent);
                if (food != 0) return new AgentAction(actions.Consume, food);
                int cook = Cookable(agent);
                if (cook != 0 && Go(sim, agent, plan, Goal.Fire, new AgentAction(actions.CraftBuild, cook), out action)) return action;
                if (agent.FreeSlots == 0 && MakeRoom(sim, agent, out action)) return action;
                if (FindFood(sim, agent, plan, out action)) return action;
                if (TakeMeatToCook(sim, agent, plan, out action)) return action;
            }

            if (TendFire(sim, agent, plan, out action)) return action;

            bool cold = FeelsCold(sim, agent, plan);
            bool turnIn = TimeToTurnIn(sim, agent);
            bool tired = turnIn || NeedsNap(agent, plan);
            if (tired || cold)
            {
                var rest = new AgentAction(tired ? actions.Sleep : actions.Wait);
                // Whoever has a home, or a spouse and a house standing empty, sleeps there: sheltered, and with its spouse.
                if (turnIn && HouseFor(sim, agent) != null && Go(sim, agent, plan, Goal.House, new AgentAction(actions.Sleep), out action)) return action;
                // By the fire if it is night or cold out; a nap on a warm day is taken on the spot.
                if (fire != null && fire.IsLit && (cold || turnIn || Freezing(sim)))
                {
                    if (NearFire(agent.X, agent.Y)) return rest;
                    if (Go(sim, agent, plan, Goal.WarmSpot, rest, out action)) return action;
                }
                if (tired) return rest;
            }

            // A spear before the larder: one wolf can be beaten bare-handed, the second cannot.
            if (HoldRightTool(sim, agent, plan, out action)) return action;
            // Someone out fishing lives off the catch and drinks from the lake; with a rod in hand, water gives fish anyway.
            // The same goes for the builder while its pack is loaded with a building's worth of materials.
            bool fishing = Fishes(agent) && Owns(agent, rodItem);
            bool carrying = agentIndex == builder && loadedForBuilding;
            if (!fishing && !carrying)
            {
                // A trip to the bushes or the lake is for a full load: it starts when stocks run low and ends when they are up again.
                float food = FoodPoints(agent);
                if (food < FoodStockLow) plan.Foraging = true;
                else if (food >= FoodStockTarget) plan.Foraging = false;
                if (plan.Foraging && Go(sim, agent, plan, Goal.Forage, harvest, out action)) return action;

                // Whoever fishes for the village works at the water and needs no waterskin, rod or no rod yet.
                int water = agent.Count(waterItem);
                if (water == 0) plan.Watering = true;
                else if (water >= WaterStockTarget) plan.Watering = false;
                if (plan.Watering && !Fishes(agent) && Go(sim, agent, plan, Goal.FetchWater, harvest, out action)) return action;
            }
            if (TidyPack(sim, agent, plan, out action)) return action;
            if (agentIndex == builder && BuildVillage(sim, agent, plan, out action)) return action;
            if (Farm(sim, agent, plan, out action)) return action;
            if (Study(sim, agent, plan, out action)) return action;
            if (Fish(sim, agent, plan, out action)) return action;
            if (MakeAxe(sim, agent, plan, out action)) return action;
            if (StockWood(sim, agent, plan, out action)) return action;
            if (Stockpile(sim, agent, plan, out action)) return action;

            // Nothing to do: stay around the village.
            if ((chest != null || fire != null) && Chebyshev(agent.X - sim.World.SpawnX, agent.Y - sim.World.SpawnY) > HomeRadius
                && Go(sim, agent, plan, Goal.Home, new AgentAction(actions.Wait), out action)) return action;

            plan.Clear();
            return rng.Chance(WanderChance) ? RandomMove(sim, agent) : new AgentAction(actions.Wait);
        }

        /// <summary>
        /// Forgets the villager's route and takes what it is in the middle of from what can be seen:
        /// asleep means napping, at the fire means warming up, in front of food or water means filling up.
        /// The rules that follow then carry on, or stop, exactly as they would have.
        /// </summary>
        void FromTheSituation(Simulation sim, Agent agent, Plan plan)
        {
            plan.Clear();
            plan.SpotTile = -1;
            Array.Clear(plan.RetryAt, 0, plan.RetryAt.Length);
            plan.Napping = agent.Sleeping;
            plan.Warming = fire != null && fire.IsLit && NearFire(agent.X, agent.Y);
            plan.Foraging = plan.Watering = false;
            int x = agent.X + agent.FaceDx, y = agent.Y + agent.FaceDy;
            if (!sim.World.InBounds(x, y)) return;
            int tile = sim.World.Index(x, y);
            if (!sim.World.HasActiveNode(tile)) return;
            int node = sim.World.Node[tile];
            plan.Watering = node == waterNode;
            plan.Foraging = nodeIsFood[node] && MayPick(agent, node);
        }

        void Bind(Simulation sim)
        {
            if (ReferenceEquals(sim, boundSim)) return;
            boundSim = sim;
            boundTick = sim.Tick;
            plans.Clear();
            ForgetVillage();

            var data = sim.Data;
            waterItem = data.ItemById[WaterId].Int;
            woodItem = data.ItemById[WoodId].Int;
            stoneItem = data.ItemById[StoneId].Int;
            rawMeatItem = data.ItemById[RawMeatId].Int;
            cookedMeatItem = data.ItemById[CookedMeatId].Int;
            spearItem = data.ItemById[SpearId].Int;
            pickaxeItem = data.ItemById[PickaxeId].Int;
            waterNode = data.NodeById(WaterNodeId).Int;
            spearArg = data.ArgFor(SpearId);
            pickaxeArg = data.ArgFor(PickaxeId);
            cookArg = data.ArgFor(CookedMeatId);
            rodItem = data.ItemById[RodId].Int;
            cottonItem = data.ItemById[CottonId].Int;
            rawFishItem = data.ItemById[RawFishId].Int;
            cookedFishItem = data.ItemById[CookedFishId].Int;
            rodArg = data.ArgFor(RodId);
            cookFishArg = data.ArgFor(CookedFishId);
            chestArg = data.ArgFor(ChestId);
            fireArg = data.ArgFor(CampfireId);
            chestDef = data.BuildingById[ChestId];
            fireDef = data.BuildingById[CampfireId];
            spearDamage = data.EquipmentByItemInt(spearItem).AttackDamage;
            foreach (var recipe in data.Recipes)
            {
                if (recipe.OutputItem == SpearId) spearWood = WoodIn(recipe);
                else if (recipe.OutputItem == PickaxeId) pickaxeWood = WoodIn(recipe);
                else if (recipe.OutputItem == RodId)
                {
                    rodWood = WoodIn(recipe);
                    rodCotton = (recipe.Input1 == CottonId ? recipe.Qty1 : 0) + (recipe.Input2 == CottonId ? recipe.Qty2 : 0) + (recipe.Input3 == CottonId ? recipe.Qty3 : 0);
                }
            }

            int maxItem = 0;
            foreach (var item in data.Items) maxItem = Math.Max(maxItem, item.Int);
            itemIsSafeFood = new bool[maxItem + 1];
            itemHunger = new float[maxItem + 1];
            itemMaxStack = new int[maxItem + 1];
            foreach (var item in data.Items)
            {
                itemIsSafeFood[item.Int] = IsSafeFood(item);
                itemHunger[item.Int] = item.Hunger;
                itemMaxStack[item.Int] = item.MaxStack;
            }

            int maxNode = 0;
            foreach (var node in data.ResourceNodes) maxNode = Math.Max(maxNode, node.Int);
            nodeIsFood = new bool[maxNode + 1];
            nodeIsWood = new bool[maxNode + 1];
            nodeIsStone = new bool[maxNode + 1];
            nodeIsCotton = new bool[maxNode + 1];
            nodeYieldItem = new int[maxNode + 1];
            nodeYieldQty = new int[maxNode + 1];
            foreach (var node in data.ResourceNodes)
            {
                if (!data.ItemById.TryGetValue(node.YieldItem ?? "", out var yield)) continue;
                nodeYieldItem[node.Int] = yield.Int;
                nodeYieldQty[node.Int] = node.YieldQty;
                nodeIsFood[node.Int] = IsSafeFood(yield);
                nodeIsWood[node.Int] = yield.Int == woodItem;
                nodeIsStone[node.Int] = yield.Int == stoneItem;
                nodeIsCotton[node.Int] = yield.Int == cottonItem;
            }

            int maxTerrain = 0;
            foreach (var terrain in data.Terrain) maxTerrain = Math.Max(maxTerrain, terrain.Int);
            terrainWalkable = new bool[maxTerrain + 1];
            foreach (var terrain in data.Terrain) terrainWalkable[terrain.Int] = terrain.Walkable != 0;

            BindRoles(sim);
            BindProjects(sim);

            int size = sim.World.Width * sim.World.Height;
            cameFrom = new int[size];
            steps = new int[size];
            visitedStamp = new int[size];
            queue = new int[size];
            dangerAt = new int[size];
            stamp = 0;
        }

        static int WoodIn(RecipeDef recipe) =>
            (recipe.Input1 == WoodId ? recipe.Qty1 : 0) + (recipe.Input2 == WoodId ? recipe.Qty2 : 0) + (recipe.Input3 == WoodId ? recipe.Qty3 : 0);

        static bool IsSafeFood(ItemDef item) =>
            item.Consumable != 0 && item.Hunger > 0f && string.IsNullOrEmpty(item.SicknessOnEat);

        static int Chebyshev(int dx, int dy) => Math.Max(Math.Abs(dx), Math.Abs(dy));

        // ------------------------------------------------------------------ the pack

        int CarriedFood(Agent agent)
        {
            for (int i = 0; i < agent.InventoryCapacity; i++)
            {
                var stack = agent.Inventory[i];
                if (stack.Qty > 0 && itemIsSafeFood[stack.Item]) return stack.Item;
            }
            return 0;
        }

        float FoodPoints(Agent agent)
        {
            float points = 0f;
            for (int i = 0; i < agent.InventoryCapacity; i++)
            {
                var stack = agent.Inventory[i];
                if (stack.Qty > 0 && itemIsSafeFood[stack.Item]) points += itemHunger[stack.Item] * stack.Qty;
            }
            return points;
        }

        int FoodStacks(Agent agent)
        {
            int stacks = 0;
            for (int i = 0; i < agent.InventoryCapacity; i++)
                if (agent.Inventory[i].Qty > 0 && itemIsSafeFood[agent.Inventory[i].Item]) stacks++;
            return stacks;
        }

        /// <summary>
        /// Packs are small, so food may open a new slot only while enough slots stay free for other
        /// things: one for most villagers, two for the builder, who carries two materials at once.
        /// </summary>
        bool MayPick(Agent agent, int node)
        {
            int item = nodeYieldItem[node], max = itemMaxStack[item];
            int free = agent.FreeSlots;
            int roomInStacks = Slots.Room(agent.Inventory, agent.InventoryCapacity, item, max) - free * max;
            if (roomInStacks >= nodeYieldQty[node]) return true;
            return FoodStacks(agent) == 0 || free > (agent.Index == builder ? 2 : 1);
        }

        /// <summary>Hungry with a full pack and nothing in it to eat: something has to go on the ground.</summary>
        bool MakeRoom(Simulation sim, Agent agent, out AgentAction action)
        {
            int drop = 0;
            for (int i = 0; i < agent.InventoryCapacity; i++)
            {
                int item = agent.Inventory[i].Item;
                if (agent.Inventory[i].Qty <= 0 || item == waterItem || item == spearItem || itemIsSafeFood[item]) continue;
                drop = item;
                // What the villager has no use for goes first.
                if (!Keeps(agent, item)) break;
            }
            action = default;
            return drop != 0 && PutDown(sim, agent, drop, out action);
        }

        /// <summary>Worn out: sleeps by day until there is enough energy for a stretch of work.</summary>
        bool NeedsNap(Agent agent, Plan plan)
        {
            float energy = agent.Get(Need.Energy);
            if (energy < SleepBelowByDay) plan.Napping = true;
            else if (energy >= NapUntil) plan.Napping = false;
            return plan.Napping;
        }

        /// <summary>Raw meat from the chest, for the fire: when hungry with nothing else, or with time to spare.</summary>
        bool TakeMeatToCook(Simulation sim, Agent agent, Plan plan, out AgentAction action)
        {
            action = default;
            if (chest == null || !chest.Complete || fire == null || !fire.IsLit || agent.FreeSlots == 0) return false;
            if (Slots.Count(chest.Storage, chest.Storage.Length, rawMeatItem) == 0) return false;
            return Go(sim, agent, plan, Goal.Chest, new AgentAction(sim.Actions.Take, rawMeatItem), out action);
        }

        /// <summary>Nothing in the pack to eat: the chest if it is close, the bushes otherwise.</summary>
        bool FindFood(Simulation sim, Agent agent, Plan plan, out AgentAction action)
        {
            bool chestFirst = chest != null && chest.Complete && Chebyshev(agent.X - chest.X, agent.Y - chest.Y) <= ChestIsNear;
            if (chestFirst && TakeFood(sim, agent, plan, out action)) return true;
            if (Go(sim, agent, plan, Goal.Forage, new AgentAction(sim.Actions.Harvest), out action)) return true;
            if (!chestFirst && TakeFood(sim, agent, plan, out action)) return true;
            action = default;
            return false;
        }

        bool TakeFood(Simulation sim, Agent agent, Plan plan, out AgentAction action)
        {
            action = default;
            if (chest == null || !chest.Complete) return false;
            foreach (var stack in chest.Storage)
            {
                if (stack.Qty <= 0 || !itemIsSafeFood[stack.Item]) continue;
                if (Slots.Room(agent.Inventory, agent.InventoryCapacity, stack.Item, itemMaxStack[stack.Item]) <= 0) continue;
                return Go(sim, agent, plan, Goal.Chest, new AgentAction(sim.Actions.Take, stack.Item), out action);
            }
            return false;
        }

        /// <summary>
        /// Everyone makes a spear first. In hand is the spear, or the tool the builder's work calls for,
        /// or the rod of someone who fishes; the spear comes back out when a wolf does (see Fight).
        /// </summary>
        bool HoldRightTool(Simulation sim, Agent agent, Plan plan, out AgentAction action)
        {
            action = default;
            var actions = sim.Actions;
            if (!Owns(agent, spearItem))
            {
                var craft = new AgentAction(actions.CraftBuild, spearArg);
                if (sim.IsValid(agent.Index, craft))
                {
                    action = craft;
                    return true;
                }
                // With enough wood but no room for the spear, tidying the pack comes first.
                if (agent.Count(woodItem) < spearWood && GetWood(sim, agent, plan, out action)) return true;
            }

            int wanted = agent.Index == builder && builderTool != 0 && Owns(agent, builderTool) ? builderTool
                : Fishes(agent) && Owns(agent, rodItem) ? rodItem
                : agent.Role == lumberjackRole && Owns(agent, axeItem) ? axeItem
                : spearItem;
            if (agent.Equipped[(int)EquipSlot.Hand].Item == wanted || agent.Count(wanted) == 0) return false;
            action = new AgentAction(actions.Equip, wanted);
            return true;
        }

        bool Owns(Agent agent, int item) => agent.Equipped[(int)EquipSlot.Hand].Item == item || agent.Count(item) > 0;

        /// <summary>
        /// Cooks raw meat at the fire and puts what the villager has no use for in the chest.
        /// Without a chest in reach, things are put down on the ground once the pack is too full to work with.
        /// </summary>
        bool TidyPack(Simulation sim, Agent agent, Plan plan, out AgentAction action)
        {
            action = default;
            var actions = sim.Actions;
            int cook = Cookable(agent);
            if (cook != 0 && WorthCookingNow(agent)) return Go(sim, agent, plan, Goal.Fire, new AgentAction(actions.CraftBuild, cook), out action);

            bool chestReady = chest != null && chest.Complete;
            bool crowded = agent.FreeSlots < (agent.Index == builder ? 2 : 1);
            for (int i = 0; i < agent.InventoryCapacity; i++)
            {
                var stack = agent.Inventory[i];
                if (stack.Qty <= 0 || Keeps(agent, stack.Item)) continue;
                if (chestReady && ChestTakes(stack.Item)
                    && Go(sim, agent, plan, Goal.Chest, new AgentAction(actions.Deposit, stack.Item), out action)) return true;
                // A hide is never thrown away: a village sees few, and a tent takes them. It waits in the pack for room in the chest.
                if (crowded && stack.Item != hideItem) return PutDown(sim, agent, stack.Item, out action);
            }
            return false;
        }

        /// <summary>Puts the stack on the ground ahead, after a step onto open ground if something is in the way.</summary>
        bool PutDown(Simulation sim, Agent agent, int item, out AgentAction action)
        {
            action = new AgentAction(sim.Actions.Deposit, item);
            // Not into a fire or a chest that happens to be in front.
            if (sim.BuildingAt(agent.X + agent.FaceDx, agent.Y + agent.FaceDy) == null && sim.IsValid(agent.Index, action)) return true;
            for (int d = 0; d < 4; d++)
            {
                if (!sim.CanEnter(agent.X + StepX[d], agent.Y + StepY[d])) continue;
                action = MoveToward(sim, StepX[d], StepY[d]);
                return true;
            }
            action = default;
            return false;
        }

        /// <summary>The recipe for what the villager can cook at the lit fire right now (meat first, then fish), or 0.</summary>
        int Cookable(Agent agent)
        {
            if (fire == null || !fire.IsLit) return 0;
            if (agent.CraftArg == cookArg || agent.CraftArg == cookFishArg) return agent.CraftArg;
            if (CanCookItem(agent, rawMeatItem, cookedMeatItem)) return cookArg;
            if (CanCookItem(agent, rawFishItem, cookedFishItem)) return cookFishArg;
            return 0;
        }

        /// <summary>Some of the raw thing in the pack, and room for the cooked one once a raw piece is gone.</summary>
        bool CanCookItem(Agent agent, int raw, int cooked)
        {
            int count = agent.Count(raw);
            return count > 0 && (count == 1 || Slots.Room(agent.Inventory, agent.InventoryCapacity, cooked, itemMaxStack[cooked]) > 0);
        }

        bool CanCook(Agent agent) => Cookable(agent) != 0;

        /// <summary>
        /// Someone out fishing does not walk to the fire with every fish: it cooks when the stack is full,
        /// the pack is, or it is by the fire anyway. Everyone else cooks what they have straight away.
        /// </summary>
        bool WorthCookingNow(Agent agent)
        {
            if (!Fishes(agent) || !Owns(agent, rodItem) || agent.CraftArg == cookArg || agent.CraftArg == cookFishArg) return true;
            if (agent.FreeSlots == 0 || NearFire(agent.X, agent.Y)) return true;
            return agent.Count(rawFishItem) >= itemMaxStack[rawFishItem] || agent.Count(rawMeatItem) >= itemMaxStack[rawMeatItem];
        }

        /// <summary>Does this villager have a use for the item, or does it belong in the chest?</summary>
        bool Keeps(Agent agent, int item)
        {
            if (item == waterItem || item == spearItem || itemIsSafeFood[item]) return true;
            bool isBuilder = agent.Index == builder;
            bool wantsRod = Fishes(agent) && !Owns(agent, rodItem);
            if (item == woodItem) return isBuilder || agent.Role == lumberjackRole || !Owns(agent, spearItem) || wantsRod;
            if (itemIsSafeFood[item] || item == waterItem) return true;
            if (isBuilder && IsProjectMaterial(item)) return true;
            // Hides are for a couple's tent: the builder holds on to them while one is wanted.
            if (item == hideItem) return isBuilder && HomesShort(boundSim) > 0;
            // The stone for the campfire, or for the axe the builder is about to make.
            if (item == stoneItem) return isBuilder && (fire == null || (builderTool == stoneAxeItem && !Owns(agent, stoneAxeItem)));
            if (item == pickaxeItem || item == stoneAxeItem) return isBuilder && item == builderTool;
            if (agent.Role == farmerRole && farms.Count > 0 && Array.IndexOf(sowSeeds, item) >= 0) return true;
            if (item == rodItem) return Fishes(agent);
            if (item == axeItem) return agent.Role == lumberjackRole;
            if (item == cottonItem) return wantsRod;
            bool fireLit = fire != null && fire.IsLit;
            if (item == rawMeatItem) return fireLit && CanCookItem(agent, rawMeatItem, cookedMeatItem);
            if (item == rawFishItem) return fireLit && CanCookItem(agent, rawFishItem, cookedFishItem);
            return false;
        }

        bool ChestTakes(int item)
        {
            int max = itemMaxStack[item];
            int room = Slots.Room(chest.Storage, chest.Storage.Length, item, max);
            if (room <= 0) return false;
            // The last free slot is for hides: a tent takes them, and a village sees few.
            int free = Slots.Free(chest.Storage, chest.Storage.Length);
            return item == hideItem || free > 1 || room > free * max;
        }

        /// <summary>Food may top up a stack in the chest, or open a new one while a few slots stay free for wood and tools.</summary>
        bool ChestTakesFood(int item)
        {
            int max = itemMaxStack[item];
            int free = Slots.Free(chest.Storage, chest.Storage.Length);
            int roomInStacks = Slots.Room(chest.Storage, chest.Storage.Length, item, max) - free * max;
            return roomInStacks > 0 || free > ChestSlotsKeptFree;
        }

        /// <summary>Fills the chest with food: gather until a stack is full, carry it home, start again.</summary>
        bool Stockpile(Simulation sim, Agent agent, Plan plan, out AgentAction action)
        {
            var actions = sim.Actions;
            bool chestReady = chest != null && chest.Complete;
            if (chestReady && FoodToStore(agent, onlyFull: true, out int full))
                return Go(sim, agent, plan, Goal.Chest, new AgentAction(actions.Deposit, full), out action);
            if (Go(sim, agent, plan, Goal.Forage, new AgentAction(actions.Harvest), out action)) return true;
            // Nothing more to pick: a second kind of food is better off in the chest than in the pack.
            if (chestReady && FoodStacks(agent) > 1 && FoodToStore(agent, onlyFull: false, out int spare))
                return Go(sim, agent, plan, Goal.Chest, new AgentAction(actions.Deposit, spare), out action);
            // Time to spare: the raw meat in the chest is worth twice as much cooked.
            if (agent.FreeSlots > 1 && TakeMeatToCook(sim, agent, plan, out action)) return true;
            action = default;
            return false;
        }

        /// <summary>A food stack the chest has room for: a full one, or else the one worth the least.</summary>
        bool FoodToStore(Agent agent, bool onlyFull, out int item)
        {
            item = 0;
            float leastPoints = float.MaxValue;
            for (int i = 0; i < agent.InventoryCapacity; i++)
            {
                var stack = agent.Inventory[i];
                if (stack.Qty <= 0 || !itemIsSafeFood[stack.Item] || !ChestTakesFood(stack.Item)) continue;
                if (onlyFull)
                {
                    if (stack.Qty < itemMaxStack[stack.Item]) continue;
                    item = stack.Item;
                    return true;
                }
                float points = itemHunger[stack.Item] * stack.Qty;
                if (points >= leastPoints) continue;
                leastPoints = points;
                item = stack.Item;
            }
            return item != 0;
        }

        // ------------------------------------------------------------------ moving

        AgentAction RandomMove(Simulation sim, Agent agent)
        {
            int d = rng.Range(0, 4);
            int x = agent.X + StepX[d], y = agent.Y + StepY[d];
            // Not into a bear's reach.
            if (sim.World.InBounds(x, y) && dangerAt[sim.World.Index(x, y)] == dangerStamp) return new AgentAction(sim.Actions.Wait);
            return MoveToward(sim, StepX[d], StepY[d]);
        }

        static AgentAction MoveToward(Simulation sim, int dx, int dy)
        {
            var actions = sim.Actions;
            if (dx > 0) return new AgentAction(actions.MoveEast);
            if (dx < 0) return new AgentAction(actions.MoveWest);
            return new AgentAction(dy > 0 ? actions.MoveNorth : actions.MoveSouth);
        }
    }
}
