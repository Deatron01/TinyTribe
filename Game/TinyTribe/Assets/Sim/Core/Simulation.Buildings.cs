using System;
using System.Collections.Generic;
using TinyTribe.Sim.Data;

namespace TinyTribe.Sim.Core
{
    /// <summary>Crafting, buildings, storage, campfires, wells and item piles.</summary>
    public sealed partial class Simulation
    {
        const string CategoryResidential = "residential";
        const string CategoryUtility = "utility";
        const string CategoryStorage = "storage";
        const string AnyoneCanBuild = "hide_tent";
        const string BuilderRole = "builder";
        const string EffectWarmthRadius = "warmth_radius";
        const string EffectInfiniteWater = "infinite_water";
        const string EffectCraftSpeed = "craft_speed_mult";
        const string EffectMoraleInside = "morale_per_tick_inside";
        const string GroupWood = "wood";
        const string CoalItem = "coal";
        const string AnyRole = "any";
        const string AnyStorage = "any";

        static readonly IReadOnlyList<ItemStack> NoStacks = new ItemStack[0];

        readonly List<Building> buildings = new List<Building>();
        readonly List<Building> campfires = new List<Building>();
        readonly SortedDictionary<int, List<ItemStack>> piles = new SortedDictionary<int, List<ItemStack>>();
        readonly HashSet<string> researched = new HashSet<string>();
        RecipeDef[] recipeByArg;
        BuildingDef[] buildingByArg;
        ItemStack[] scratchSlots;
        int builderRoleInt;

        /// <summary>Every building and building site, by index. Indexes never change.</summary>
        public IReadOnlyList<Building> Buildings => buildings;

        void InitCrafting()
        {
            recipeByArg = new RecipeDef[argCount];
            buildingByArg = new BuildingDef[argCount];
            var recipeByOutput = new Dictionary<string, RecipeDef>();
            foreach (var recipe in Data.Recipes) recipeByOutput[recipe.OutputItem] = recipe;
            foreach (var arg in Data.ActionArgs)
            {
                if (arg.Kind == "recipe") recipeByOutput.TryGetValue(arg.RefId ?? "", out recipeByArg[arg.Int]);
                else if (arg.Kind == "building") Data.BuildingById.TryGetValue(arg.RefId ?? "", out buildingByArg[arg.Int]);
            }
            scratchSlots = new ItemStack[Config.MaxSlots];
            builderRoleInt = Data.RoleInt(BuilderRole);
        }

        // ------------------------------------------------------------------ technology

        /// <summary>Marks a technology as known. Nothing in the engine researches yet; this is for tests and later systems.</summary>
        public void Research(string techId)
        {
            if (researched.Add(techId)) JobsMayHaveChanged();
        }

        public bool IsResearched(string techId) => string.IsNullOrEmpty(techId) || researched.Contains(techId);

        // ------------------------------------------------------------------ craft_build

        bool TryCraftBuild(Agent agent, int arg, bool execute)
        {
            if ((uint)arg >= (uint)argCount) return false;
            if (recipeByArg[arg] != null) return TryCraft(agent, arg, recipeByArg[arg], execute);
            if (buildingByArg[arg] != null) return TryBuild(agent, buildingByArg[arg], execute);
            if (cropByArg[arg] != null) return TryPlant(agent, arg, execute);
            if (arg == researchArg) return TryResearch(agent, execute);
            return false;
        }

        /// <summary>R07.</summary>
        bool TryCraft(Agent agent, int arg, RecipeDef recipe, bool execute)
        {
            if (!StationReady(agent, recipe, out float stationSpeed)) return false;
            bool continuing = agent.CraftArg == arg;
            if (!continuing && !CanStartRecipe(agent, recipe)) return false;
            if (!execute) return true;

            if (!continuing)
            {
                TakeInputs(agent, recipe);
                agent.CraftArg = arg;
                agent.CraftProgress = 0f;
            }
            AddNeed(agent, Need.Energy, -craftEnergyCost * agent.EnduranceMult * weatherWorkEnergyMult);
            agent.CraftProgress += agent.CraftSpeedMult * stationSpeed;
            if (agent.CraftProgress + 1e-4f < recipe.CraftTicks) return true;

            var output = Data.ItemById[recipe.OutputItem];
            AddItem(agent, output.Int, recipe.OutputQty);
            agent.CraftArg = 0;
            agent.CraftProgress = 0f;
            events.Add(new SimEvent(SimEventType.Crafted, agent.Index, output.Int, recipe.OutputQty, agent.X, agent.Y));
            return true;
        }

        /// <summary>A recipe with a workstation needs the villager to face it, finished and (for a campfire) lit.</summary>
        bool StationReady(Agent agent, RecipeDef recipe, out float speed)
        {
            speed = 1f;
            if (string.IsNullOrEmpty(recipe.Workstation)) return true;
            var station = BuildingFacing(agent);
            if (station == null || !station.Complete) return false;
            if (station.Def.Id != recipe.Workstation && station.Def.Id != recipe.WorkstationAlt) return false;
            if (IsCampfire(station.Def) && !station.IsLit) return false;
            if (station.Def.EffectKey == EffectCraftSpeed) speed = station.Def.EffectValue;
            return true;
        }

        bool CanStartRecipe(Agent agent, RecipeDef recipe)
        {
            if (!IsResearched(recipe.TechRequired) || !HasRole(agent, recipe.RoleRequired)) return false;
            if (!HasInput(agent, recipe.Input1, recipe.Qty1) || !HasInput(agent, recipe.Input2, recipe.Qty2) || !HasInput(agent, recipe.Input3, recipe.Qty3))
                return false;

            // Would the result fit once the inputs are gone?
            Array.Copy(agent.Inventory, scratchSlots, scratchSlots.Length);
            RemoveInput(scratchSlots, agent.InventoryCapacity, recipe.Input1, recipe.Qty1);
            RemoveInput(scratchSlots, agent.InventoryCapacity, recipe.Input2, recipe.Qty2);
            RemoveInput(scratchSlots, agent.InventoryCapacity, recipe.Input3, recipe.Qty3);
            var output = Data.ItemById[recipe.OutputItem];
            return Slots.Room(scratchSlots, agent.InventoryCapacity, output.Int, output.MaxStack) >= recipe.OutputQty;
        }

        bool HasRole(Agent agent, string roleId) =>
            string.IsNullOrEmpty(roleId) || roleId == AnyRole || agent.Role == Data.RoleInt(roleId);

        bool HasInput(Agent agent, string itemId, int qty) =>
            string.IsNullOrEmpty(itemId) || qty <= 0 || agent.Count(Data.ItemById[itemId].Int) >= qty;

        void RemoveInput(ItemStack[] slots, int capacity, string itemId, int qty)
        {
            if (!string.IsNullOrEmpty(itemId) && qty > 0) Slots.Remove(slots, capacity, Data.ItemById[itemId].Int, qty);
        }

        void TakeInputs(Agent agent, RecipeDef recipe)
        {
            RemoveInput(agent.Inventory, agent.InventoryCapacity, recipe.Input1, recipe.Qty1);
            RemoveInput(agent.Inventory, agent.InventoryCapacity, recipe.Input2, recipe.Qty2);
            RemoveInput(agent.Inventory, agent.InventoryCapacity, recipe.Input3, recipe.Qty3);
        }

        /// <summary>Abandons the recipe in progress and puts its inputs back in the pack.</summary>
        void CancelCraft(Agent agent)
        {
            if (agent.CraftArg == 0) return;
            var recipe = recipeByArg[agent.CraftArg];
            agent.CraftArg = 0;
            agent.CraftProgress = 0f;
            if (recipe == null) return;
            Refund(agent, recipe.Input1, recipe.Qty1);
            Refund(agent, recipe.Input2, recipe.Qty2);
            Refund(agent, recipe.Input3, recipe.Qty3);
        }

        void Refund(Agent agent, string itemId, int qty)
        {
            if (!string.IsNullOrEmpty(itemId) && qty > 0) AddItem(agent, Data.ItemById[itemId].Int, qty);
        }

        // ------------------------------------------------------------------ buildings

        static bool IsHouse(BuildingDef def) => def.Category == CategoryResidential;

        static bool IsCampfire(BuildingDef def) => def.EffectKey == EffectWarmthRadius;

        /// <summary>The building or site covering the tile, or null.</summary>
        public Building BuildingAt(int x, int y)
        {
            if (!World.InBounds(x, y)) return null;
            int index = World.BuildingIndex[World.Index(x, y)];
            return index < 0 ? null : buildings[index];
        }

        Building BuildingFacing(Agent agent) => BuildingAt(agent.X + agent.FaceDx, agent.Y + agent.FaceDy);

        bool IsWaterBuilding(int tile)
        {
            int index = World.BuildingIndex[tile];
            return index >= 0 && buildings[index].Complete && buildings[index].Def.EffectKey == EffectInfiniteWater;
        }

        /// <summary>
        /// Puts a building on the map with (x, y) as its lowest corner, finished or as an empty site.
        /// For map set-up and tests; villagers build through the craft_build action.
        /// </summary>
        public Building PlaceBuilding(string buildingId, int x, int y, bool complete = true)
        {
            var def = Data.BuildingById[buildingId];
            if (!FootprintFree(x, y, def.FootprintW, def.FootprintH))
                throw new InvalidOperationException("No room for " + buildingId + " at " + x + "," + y);
            var building = CreateSite(def, x, y);
            if (complete) Finish(building);
            return building;
        }

        bool FootprintFree(int x, int y, int width, int height)
        {
            for (int ty = y; ty < y + height; ty++)
                for (int tx = x; tx < x + width; tx++)
                {
                    if (!World.InBounds(tx, ty)) return false;
                    int tile = World.Index(tx, ty);
                    if (!terrainWalkable[World.Terrain[tile]] || World.Node[tile] != 0 || World.BuildingIndex[tile] != -1
                        || World.AgentIndex[tile] != -1 || World.AnimalIndex[tile] != -1) return false;
                }
            return true;
        }

        Building CreateSite(BuildingDef def, int x, int y)
        {
            var building = new Building { Index = buildings.Count, Def = def, X = x, Y = y, Width = def.FootprintW, Height = def.FootprintH };
            buildings.Add(building);
            for (int ty = y; ty < y + building.Height; ty++)
                for (int tx = x; tx < x + building.Width; tx++) World.BuildingIndex[World.Index(tx, ty)] = building.Index;
            events.Add(new SimEvent(SimEventType.BuildingStarted, -1, def.Int, building.Index, x, y));
            return building;
        }

        void Finish(Building building)
        {
            var def = building.Def;
            building.Complete = true;
            building.Progress = def.BuildTicks;
            if (def.StorageSlots > 0) building.Storage = new ItemStack[def.StorageSlots];
            if (IsCampfire(def))
            {
                building.FuelTicks = Config.CampfireStartFuelTicks;
                campfires.Add(building);
            }
            if (def.EffectKey == EffectCropSlots) LayFarmland(building, (int)def.EffectValue);
            events.Add(new SimEvent(SimEventType.BuildingCompleted, -1, def.Int, building.Index, building.X, building.Y));
            JobsMayHaveChanged();
            NoteBuilt(def);
        }

        /// <summary>R08.</summary>
        bool TryBuild(Agent agent, BuildingDef def, bool execute)
        {
            if (!MayBuild(agent, def)) return false;
            int faceX = agent.X + agent.FaceDx, faceY = agent.Y + agent.FaceDy;
            var site = BuildingAt(faceX, faceY);
            if (site != null)
            {
                if (site.Complete || site.Def != def) return false;
                if (execute) WorkOn(agent, site);
                return true;
            }

            if (!HasInput(agent, def.CostItem1, def.CostQty1) || !HasInput(agent, def.CostItem2, def.CostQty2) || !HasInput(agent, def.CostItem3, def.CostQty3))
                return false;
            // The faced tile is the near corner; the footprint grows away from the builder.
            int x = agent.FaceDx < 0 ? faceX - def.FootprintW + 1 : faceX;
            int y = agent.FaceDy < 0 ? faceY - def.FootprintH + 1 : faceY;
            if (!FootprintFree(x, y, def.FootprintW, def.FootprintH)) return false;
            if (!execute) return true;

            RemoveInput(agent.Inventory, agent.InventoryCapacity, def.CostItem1, def.CostQty1);
            RemoveInput(agent.Inventory, agent.InventoryCapacity, def.CostItem2, def.CostQty2);
            RemoveInput(agent.Inventory, agent.InventoryCapacity, def.CostItem3, def.CostQty3);
            WorkOn(agent, CreateSite(def, x, y));
            return true;
        }

        /// <summary>Anyone may build utility and storage buildings and the hide tent; everything else needs a Builder.</summary>
        bool MayBuild(Agent agent, BuildingDef def)
        {
            if (!IsResearched(def.TechRequired)) return false;
            return def.Category == CategoryUtility || def.Category == CategoryStorage || def.Id == AnyoneCanBuild || agent.Role == builderRoleInt;
        }

        void WorkOn(Agent agent, Building site)
        {
            AddNeed(agent, Need.Energy, -craftEnergyCost * agent.EnduranceMult * weatherWorkEnergyMult);
            SickFromWork(agent);
            var tool = HandTool(agent);
            float toolSpeed = tool != null && tool.EffectKey == EffectBuildSpeed ? tool.EffectValue : 1f;
            site.Progress += agent.CraftSpeedMult * toolSpeed;
            if (site.Progress + 1e-4f >= site.Def.BuildTicks) Finish(site);
        }

        // ------------------------------------------------------------------ campfires

        void BurnCampfires()
        {
            foreach (var fire in campfires)
            {
                if (fire.FuelTicks <= 0) continue;
                if (--fire.FuelTicks == 0) events.Add(new SimEvent(SimEventType.CampfireOut, -1, fire.Def.Int, fire.Index, fire.X, fire.Y));
            }
        }

        /// <summary>R30.</summary>
        bool NearLitCampfire(Agent agent)
        {
            foreach (var fire in campfires)
                if (fire.FuelTicks > 0 && Chebyshev(agent.X - fire.X, agent.Y - fire.Y) <= (int)fire.Def.EffectValue) return true;
            return false;
        }

        /// <summary>Burn time one unit of the item adds to a campfire; 0 if it does not burn (R10).</summary>
        int FuelTicksFor(ItemDef item)
        {
            if (item.Id == CoalItem) return Config.CampfireFuelTicksPerWood * Config.CoalFuelMult;
            return item.StorageGroup == GroupWood ? Config.CampfireFuelTicksPerWood : 0;
        }

        // ------------------------------------------------------------------ storage

        /// <summary>R09, R10, R35: store a stack in the faced storage, feed the faced campfire, or put the stack down on open ground.</summary>
        bool TryDeposit(Agent agent, int itemInt, bool execute)
        {
            var item = Data.ItemByInt(itemInt);
            if (item == null) return false;
            int from = agent.FindSlot(itemInt);
            if (from < 0) return false;
            var building = BuildingFacing(agent);
            if (building == null) return TryPutDown(agent, from, execute);
            if (!building.Complete) return false;

            if (IsCampfire(building.Def))
            {
                int fuel = FuelTicksFor(item);
                if (fuel <= 0) return false;
                if (!execute) return true;
                Slots.Remove(agent.Inventory, agent.InventoryCapacity, itemInt, 1);
                building.FuelTicks += fuel;
                events.Add(new SimEvent(SimEventType.Deposited, agent.Index, itemInt, 1, building.X, building.Y));
                return true;
            }

            if (building.Storage == null || !MayUseStorage(agent, building) || !Accepts(building.Def, item)) return false;
            int room = Slots.Room(building.Storage, building.Storage.Length, itemInt, item.MaxStack);
            if (room <= 0) return false;
            if (!execute) return true;

            var stack = agent.Inventory[from];
            int moved = Math.Min(stack.Qty, room);
            Slots.Add(building.Storage, building.Storage.Length, itemInt, moved, item.MaxStack, stack.Durability);
            stack.Qty -= moved;
            agent.Inventory[from] = stack.Qty > 0 ? stack : default;
            events.Add(new SimEvent(SimEventType.Deposited, agent.Index, itemInt, moved, building.X, building.Y));
            return true;
        }

        /// <summary>
        /// R35: the whole stack goes on the faced tile, as a new pile or onto the pile already there.
        /// The tile must be walkable ground with nothing else on it.
        /// </summary>
        bool TryPutDown(Agent agent, int slot, bool execute)
        {
            int x = agent.X + agent.FaceDx, y = agent.Y + agent.FaceDy;
            if (!World.InBounds(x, y)) return false;
            int tile = World.Index(x, y);
            if (!terrainWalkable[World.Terrain[tile]] || World.AgentIndex[tile] != -1 || World.AnimalIndex[tile] != -1) return false;
            if (World.Node[tile] != 0 && World.Node[tile] != itemPile.Int) return false;
            if (!execute) return true;

            var stack = agent.Inventory[slot];
            agent.Inventory[slot] = default;
            AddToPile(tile, stack);
            events.Add(new SimEvent(SimEventType.Deposited, agent.Index, stack.Item, stack.Qty, x, y));
            return true;
        }

        /// <summary>R09: take one stack, or as much of it as fits, from the faced storage.</summary>
        bool TryTake(Agent agent, int itemInt, bool execute)
        {
            var item = Data.ItemByInt(itemInt);
            var building = BuildingFacing(agent);
            if (item == null || building == null || !building.Complete || building.Storage == null || !MayUseStorage(agent, building)) return false;
            int from = Slots.Find(building.Storage, building.Storage.Length, itemInt);
            if (from < 0) return false;
            int room = RoomFor(agent, item);
            if (room <= 0) return false;
            if (!execute) return true;

            var stack = building.Storage[from];
            int moved = Math.Min(stack.Qty, room);
            AddItem(agent, item, moved, stack.Durability);
            stack.Qty -= moved;
            building.Storage[from] = stack.Qty > 0 ? stack : default;
            events.Add(new SimEvent(SimEventType.Taken, agent.Index, itemInt, moved, building.X, building.Y));
            return true;
        }

        /// <summary>A house chest belongs to its residents; other storage is for everyone.</summary>
        public bool MayUseStorage(Agent agent, Building building) =>
            !IsHouse(building.Def) || building.Residents.Contains(agent.Index);

        static bool Accepts(BuildingDef def, ItemDef item) =>
            string.IsNullOrEmpty(def.StorageFilter) || def.StorageFilter == AnyStorage || def.StorageFilter == item.StorageGroup;

        // ------------------------------------------------------------------ item piles

        /// <summary>The stacks lying on a tile; empty when there is no pile.</summary>
        public IReadOnlyList<ItemStack> PileAt(int x, int y)
        {
            if (!World.InBounds(x, y)) return NoStacks;
            return piles.TryGetValue(World.Index(x, y), out var stacks) ? stacks : NoStacks;
        }

        /// <summary>R05. Drops a stack on the tile. It is lost if something else (a building, a regrowing plant) is in the way.</summary>
        void AddToPile(int tile, ItemStack stack)
        {
            if (stack.Qty <= 0) return;
            if (!piles.TryGetValue(tile, out var stacks))
            {
                if (World.Node[tile] != 0 || World.BuildingIndex[tile] != -1) return;
                piles[tile] = stacks = new List<ItemStack>();
                World.Node[tile] = (short)itemPile.Int;
                World.NodeCharges[tile] = World.InfiniteCharges;
                events.Add(new SimEvent(SimEventType.NodeChanged, -1, itemPile.Int, 0, tile % World.Width, tile / World.Width));
            }
            stacks.Add(stack);
        }

        /// <summary>harvest_attack on a pile: take whatever fits; the pile goes when it is empty.</summary>
        bool TryTakePile(Agent agent, int x, int y, bool execute)
        {
            int tile = World.Index(x, y);
            if (!piles.TryGetValue(tile, out var stacks)) return false;
            bool anyFits = false;
            foreach (var stack in stacks)
                anyFits |= RoomFor(agent, Data.ItemByInt(stack.Item)) > 0;
            if (!anyFits) return false;
            if (!execute) return true;

            for (int i = stacks.Count - 1; i >= 0; i--)
            {
                var stack = stacks[i];
                int moved = AddItem(agent, Data.ItemByInt(stack.Item), stack.Qty, stack.Durability);
                if (moved > 0) events.Add(new SimEvent(SimEventType.Harvested, agent.Index, stack.Item, moved, x, y));
                stack.Qty -= moved;
                if (stack.Qty > 0) stacks[i] = stack;
                else stacks.RemoveAt(i);
            }
            if (stacks.Count == 0)
            {
                piles.Remove(tile);
                World.RemoveNode(tile);
                events.Add(new SimEvent(SimEventType.NodeChanged, -1, 0, 0, x, y));
            }
            return true;
        }
    }
}
