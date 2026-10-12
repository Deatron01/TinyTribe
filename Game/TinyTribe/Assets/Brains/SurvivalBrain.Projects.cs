using System;
using System.Collections.Generic;
using TinyTribe.Sim.Core;
using TinyTribe.Sim.Data;

namespace TinyTribe.Brains
{
    /// <summary>
    /// The builder's works after the chest and the fire, simple ones first (rule R40): a garden plot and
    /// an Elders' Circle, which take no research; then a lumber mill once carpentry is known, a library,
    /// and the big farm once agriculture is. And the farmer's year on the farms.
    ///
    /// A pack holds four or five stacks and a library costs four, so the chest is the builder's yard:
    /// materials are gathered into it, and only when everything is there does the builder empty its
    /// pack, load up, and walk to the place it has picked.
    /// </summary>
    public sealed partial class SurvivalBrain
    {
        const string PlanksId = "planks", OakId = "oak_wood", StoneAxeId = "stone_axe";
        const string MillId = "lumber_mill", LibraryId = "library", FarmId = "farm_windmill";
        const string GardenId = "garden_plot", CircleId = "study_circle", TentId = "hide_tent", HideId = "animal_hide";
        const string CropSlotsEffect = "crop_slots";
        const string FarmlandId = "farmland", RipeCropId = "crop_mature";

        // Bot habits.
        const int SpotSearchRadius = 18;
        const int PlacePatience = 12;
        const float FedEnoughToCarry = 60f;
        static readonly string[] SowingOrder = { "carrot_crop", "potato_crop", "wheat_crop" };

        int planksItem, oakItem, stoneAxeItem;
        int planksArg, stoneAxeArg;
        int stoneAxeWood, stoneAxeStone;
        int farmlandTerrain, ripeCropNode, farmerRole;
        BuildingDef millDef, libraryDef, farmDef, gardenDef, circleDef, tentDef;
        int hideItem, tentHides, greetEveryTicks, socialRange;
        bool[] nodeIsOak;
        int[] sowArgs = new int[0], sowSeeds = new int[0];
        string[] sowSeasons = new string[0];

        // The village's works, refreshed with the rest in LookAround. The first four may still be building sites.
        Building mill, farm, garden, circle, libraryInAnyState;

        /// <summary>Every finished building with plots to sow.</summary>
        readonly List<Building> farms = new List<Building>();

        /// <summary>Where the technology being researched can be studied: the Library, or the circle for what it can teach.</summary>
        Building studyPlace;

        /// <summary>What the builder is working on, or null; decides what it keeps in its pack.</summary>
        BuildingDef project;

        /// <summary>The tool the builder's job wants in hand right now (0: the spear), remembered between ticks.</summary>
        int builderTool;

        /// <summary>True while the builder has its pack loaded for a building, so it does not wander off to pick berries.</summary>
        bool loadedForBuilding;

        // Where the next big building goes: the tile to stand on, and the way to face from it.
        int spotStand = -1, spotDx, spotDy;
        BuildingDef spotFor;

        readonly int[] costItems = new int[3], costQtys = new int[3];

        void BindProjects(Simulation sim)
        {
            var data = sim.Data;
            planksItem = data.ItemById[PlanksId].Int;
            oakItem = data.ItemById[OakId].Int;
            stoneAxeItem = data.ItemById[StoneAxeId].Int;
            planksArg = data.ArgFor(PlanksId);
            stoneAxeArg = data.ArgFor(StoneAxeId);
            millDef = data.BuildingById[MillId];
            libraryDef = data.BuildingById[LibraryId];
            farmDef = data.BuildingById[FarmId];
            gardenDef = data.BuildingById[GardenId];
            circleDef = data.BuildingById[CircleId];
            tentDef = data.BuildingById[TentId];
            hideItem = data.ItemById[HideId].Int;
            tentHides = Cost(tentDef, HideId);
            greetEveryTicks = data.ConfigInt("emote_exchange_cooldown_ticks");
            socialRange = data.ConfigInt("social_range");
            farmlandTerrain = data.TerrainById(FarmlandId).Int;
            ripeCropNode = data.NodeById(RipeCropId).Int;
            farmerRole = data.RoleInt("farmer");
            foreach (var recipe in data.Recipes)
            {
                if (recipe.OutputItem != StoneAxeId) continue;
                stoneAxeWood = WoodIn(recipe);
                stoneAxeStone = (recipe.Input1 == StoneId ? recipe.Qty1 : 0) + (recipe.Input2 == StoneId ? recipe.Qty2 : 0) + (recipe.Input3 == StoneId ? recipe.Qty3 : 0);
            }

            nodeIsOak = new bool[nodeIsWood.Length];
            foreach (var node in data.ResourceNodes) nodeIsOak[node.Int] = node.YieldItem == OakId;

            sowArgs = new int[SowingOrder.Length];
            sowSeeds = new int[SowingOrder.Length];
            sowSeasons = new string[SowingOrder.Length];
            for (int i = 0; i < SowingOrder.Length; i++)
                foreach (var crop in data.Crops)
                {
                    if (crop.Id != SowingOrder[i]) continue;
                    sowArgs[i] = data.ArgFor(crop.Id);
                    sowSeeds[i] = data.ItemById[crop.SeedItem].Int;
                    sowSeasons[i] = ";" + crop.PlantSeasons + ";";
                }

            mill = farm = garden = circle = libraryInAnyState = studyPlace = null;
            farms.Clear();
            project = null;
            builderTool = 0;
            loadedForBuilding = false;
            spotStand = -1;
            spotFor = null;
        }

        int Stored(int item) => chest != null && chest.Complete ? Slots.Count(chest.Storage, chest.Storage.Length, item) : 0;

        /// <summary>
        /// What the chest holds that the builder may put into its works. A take empties the whole stack,
        /// so while a fishing rod is waiting to be made the wood is left alone: food comes before building.
        /// </summary>
        int StoredForWorks(int item) => item == woodItem && rodWaiting ? 0 : Stored(item);

        int ArgOf(BuildingDef def) => boundSim.Data.ArgFor(def.Id);

        /// <summary>Is this one of the buildings the bots put up, and so finish when it is left half built?</summary>
        bool IsOurWork(BuildingDef def) =>
            def == tentDef || def == chestDef || def == fireDef || def == gardenDef || def == circleDef || def == millDef || def == libraryDef || def == farmDef;

        static bool HasPlots(BuildingDef def) => def.EffectKey == CropSlotsEffect;

        /// <summary>
        /// The next work the village lacks, or null. What takes no research comes first. Then the village
        /// waits for its scholar: the mill takes carpentry, the library and the big farm take the mill's planks.
        /// </summary>
        BuildingDef NextProject(Simulation sim)
        {
            if (garden == null && farm == null) return gardenDef;
            if (circle == null && libraryInAnyState == null) return circleDef;
            // A tent for a couple with nowhere to live, once the chest holds the hides for it.
            if (HomesShort(sim) > 0 && Stored(hideItem) + (builder >= 0 ? sim.Agents[builder].Count(hideItem) : 0) >= tentHides) return tentDef;
            if (mill == null) return sim.IsResearched(millDef.TechRequired) ? millDef : null;
            if (libraryInAnyState == null) return libraryDef;
            if (farm == null && sim.IsResearched(farmDef.TechRequired)) return farmDef;
            return null;
        }

        int Costs(BuildingDef def)
        {
            int count = 0;
            var data = boundSim.Data;
            void Add(string itemId, int qty)
            {
                if (string.IsNullOrEmpty(itemId) || qty <= 0) return;
                costItems[count] = data.ItemById[itemId].Int;
                costQtys[count++] = qty;
            }
            Add(def.CostItem1, def.CostQty1);
            Add(def.CostItem2, def.CostQty2);
            Add(def.CostItem3, def.CostQty3);
            return count;
        }

        /// <summary>Does the builder's current work need the item? Planks are sawn from pine, so pine counts while planks are short.</summary>
        bool IsProjectMaterial(int item)
        {
            if (project == null) return false;
            int count = Costs(project);
            for (int i = 0; i < count; i++)
            {
                if (costItems[i] == item) return true;
                if (costItems[i] == planksItem && item == woodItem) return true;
            }
            return false;
        }

        /// <summary>Gathers what is missing into the chest, then loads the pack and builds.</summary>
        bool BigBuild(Simulation sim, Agent agent, Plan plan, BuildingDef def, out AgentAction action)
        {
            project = def;
            int count = Costs(def);
            for (int i = 0; i < count; i++)
            {
                int item = costItems[i], missing = costQtys[i] - agent.Count(item) - StoredForWorks(item);
                if (missing > 0) return Supply(sim, agent, plan, item, out action);
            }

            // Everything is in the pack or the chest. Eat and drink first: the pack is about to hold nothing else.
            bool loaded = true;
            for (int i = 0; i < count; i++) loaded &= agent.Count(costItems[i]) >= costQtys[i];
            if (!loaded && TopUp(sim, agent, plan, out action)) return true;
            loadedForBuilding = true;
            for (int i = 0; i < count; i++)
                if (agent.Count(costItems[i]) < costQtys[i]) return Load(sim, agent, plan, costItems[i], out action);
            return Place(sim, agent, plan, def, out action);
        }

        bool TopUp(Simulation sim, Agent agent, Plan plan, out AgentAction action)
        {
            var actions = sim.Actions;
            action = default;
            if (agent.Get(Need.Thirst) < FedEnoughToCarry)
            {
                if (agent.Count(waterItem) > 0)
                {
                    action = new AgentAction(actions.Consume, waterItem);
                    return true;
                }
                if (Go(sim, agent, plan, Goal.Drink, new AgentAction(actions.Consume, 0), out action)) return true;
            }
            if (agent.Get(Need.Hunger) < FedEnoughToCarry)
            {
                int food = CarriedFood(agent);
                if (food != 0)
                {
                    action = new AgentAction(actions.Consume, food);
                    return true;
                }
                if (FindFood(sim, agent, plan, out action)) return true;
            }
            action = default;
            return false;
        }

        /// <summary>One more stack of the material from the chest; whatever is not part of the building makes way for it.</summary>
        bool Load(Simulation sim, Agent agent, Plan plan, int item, out AgentAction action)
        {
            var actions = sim.Actions;
            if (Slots.Room(agent.Inventory, agent.InventoryCapacity, item, itemMaxStack[item]) > 0)
                return Go(sim, agent, plan, Goal.Chest, new AgentAction(actions.Take, item), out action);

            for (int i = 0; i < agent.InventoryCapacity; i++)
            {
                int other = agent.Inventory[i].Item;
                if (agent.Inventory[i].Qty <= 0 || IsProjectMaterial(other)) continue;
                if (ChestTakes(other)) return Go(sim, agent, plan, Goal.Chest, new AgentAction(actions.Deposit, other), out action);
                return PutDown(sim, agent, other, out action);
            }
            action = default;
            return false;
        }

        /// <summary>Gets more of one material, with the tool it takes.</summary>
        bool Supply(Simulation sim, Agent agent, Plan plan, int item, out AgentAction action)
        {
            action = default;
            if (item == woodItem) return GatherOrStore(sim, agent, plan, Goal.Wood, out action);
            if (item == planksItem) return SawPlanks(sim, agent, plan, out action);
            if (item == stoneItem)
            {
                int tool = GetTool(sim, agent, plan, pickaxeItem, pickaxeArg, out action);
                return tool == 0 ? GatherOrStore(sim, agent, plan, Goal.Stone, out action) : tool == 1;
            }
            if (item == oakItem)
            {
                // The axe that fells oak takes a stone to make.
                bool haveAxe = Owns(agent, stoneAxeItem) || Stored(stoneAxeItem) > 0 || agent.CraftArg == stoneAxeArg;
                if (!haveAxe && agent.Count(stoneItem) + Stored(stoneItem) < stoneAxeStone) return Supply(sim, agent, plan, stoneItem, out action);
                int tool = GetTool(sim, agent, plan, stoneAxeItem, stoneAxeArg, out action);
                return tool == 0 ? GatherOrStore(sim, agent, plan, Goal.Oak, out action) : tool == 1;
            }
            return false;
        }

        /// <summary>Gathers; with a full pack, or nothing more in reach, carries what it has to the chest.</summary>
        bool GatherOrStore(Simulation sim, Agent agent, Plan plan, Goal goal, out AgentAction action)
        {
            if (Go(sim, agent, plan, goal, new AgentAction(sim.Actions.Harvest), out action)) return true;
            return StoreMaterials(sim, agent, plan, out action);
        }

        bool StoreMaterials(Simulation sim, Agent agent, Plan plan, out AgentAction action)
        {
            action = default;
            if (chest == null || !chest.Complete) return false;
            for (int i = 0; i < agent.InventoryCapacity; i++)
            {
                int item = agent.Inventory[i].Item;
                if (agent.Inventory[i].Qty <= 0 || !IsProjectMaterial(item) || !ChestTakes(item)) continue;
                return Go(sim, agent, plan, Goal.Chest, new AgentAction(sim.Actions.Deposit, item), out action);
            }
            return false;
        }

        /// <summary>
        /// Brings a tool into the builder's hand: from the pack, the chest, or by making it.
        /// Returns 0 when it is in hand, 1 when an action was chosen toward it, 2 when it cannot be had now.
        /// </summary>
        int GetTool(Simulation sim, Agent agent, Plan plan, int tool, int craftArg, out AgentAction action)
        {
            var actions = sim.Actions;
            action = default;
            builderTool = tool;
            if (agent.Equipped[(int)EquipSlot.Hand].Item == tool) return 0;
            if (agent.CraftArg == craftArg)
            {
                // Half made: its makings are no longer in the pack, so nothing below would recognise it.
                action = new AgentAction(actions.CraftBuild, craftArg);
                return 1;
            }
            if (agent.Count(tool) > 0)
            {
                action = new AgentAction(actions.Equip, tool);
                return 1;
            }
            if (Stored(tool) > 0 && agent.FreeSlots > 0)
                return Go(sim, agent, plan, Goal.Chest, new AgentAction(actions.Take, tool), out action) ? 1 : 2;

            var craft = new AgentAction(actions.CraftBuild, craftArg);
            if (sim.IsValid(agent.Index, craft))
            {
                action = craft;
                return 1;
            }
            int stoneNeeded = tool == stoneAxeItem ? stoneAxeStone : 0, woodNeeded = tool == stoneAxeItem ? stoneAxeWood : pickaxeWood;
            if (agent.Count(stoneItem) < stoneNeeded)
                return Stored(stoneItem) > 0 && Go(sim, agent, plan, Goal.Chest, new AgentAction(actions.Take, stoneItem), out action) ? 1 : 2;
            if (agent.Count(woodItem) < woodNeeded)
            {
                if (StoredForWorks(woodItem) > 0 && agent.FreeSlots > 0 && Go(sim, agent, plan, Goal.Chest, new AgentAction(actions.Take, woodItem), out action)) return 1;
                return Go(sim, agent, plan, Goal.Wood, new AgentAction(actions.Harvest), out action) ? 1 : 2;
            }
            // The makings are there but the pack has no room for the tool: the materials go to the chest first.
            return StoreMaterials(sim, agent, plan, out action) ? 1 : 2;
        }

        /// <summary>Two planks to a log, at the mill: pine from the pack, the chest or the trees.</summary>
        bool SawPlanks(Simulation sim, Agent agent, Plan plan, out AgentAction action)
        {
            action = default;
            if (mill == null || !mill.Complete) return false;
            var actions = sim.Actions;
            int logs = agent.Count(woodItem);
            bool sawing = agent.CraftArg == planksArg;
            bool roomForPlanks = logs == 1 || Slots.Room(agent.Inventory, agent.InventoryCapacity, planksItem, itemMaxStack[planksItem]) >= 2;
            if (sawing || (logs > 0 && roomForPlanks))
                return Go(sim, agent, plan, Goal.Mill, new AgentAction(actions.CraftBuild, planksArg), out action);
            if (agent.Count(planksItem) > 0 && ChestTakes(planksItem) && (logs > 0 || agent.FreeSlots == 0))
                return Go(sim, agent, plan, Goal.Chest, new AgentAction(actions.Deposit, planksItem), out action);
            if (logs > 0 || agent.FreeSlots == 0) return StoreMaterials(sim, agent, plan, out action);
            if (StoredForWorks(woodItem) > 0) return Go(sim, agent, plan, Goal.Chest, new AgentAction(actions.Take, woodItem), out action);
            return Go(sim, agent, plan, Goal.Wood, new AgentAction(actions.Harvest), out action);
        }

        // ------------------------------------------------------------------ where it goes

        bool OpenGround(World world, int x, int y)
        {
            if (!world.InBounds(x, y)) return false;
            int tile = world.Index(x, y);
            return terrainWalkable[world.Terrain[tile]] && !world.HasActiveNode(tile) && world.BuildingIndex[tile] < 0;
        }

        /// <summary>
        /// Could the building go in front of a villager standing on (x, y) and facing (dx, dy)? The whole
        /// footprint must be bare ground, nothing may be built within a tile of it, and the villager needs
        /// a tile behind it to step in from. A farm's plots lie along its sides: those tiles must be bare
        /// too, and nothing may be built within two tiles.
        /// </summary>
        bool SpotFits(Simulation sim, BuildingDef def, int x, int y, int dx, int dy)
        {
            var world = sim.World;
            if (!OpenGround(world, x, y) || !OpenGround(world, x - dx, y - dy)) return false;
            int faceX = x + dx, faceY = y + dy;
            int left = dx < 0 ? faceX - def.FootprintW + 1 : faceX, bottom = dy < 0 ? faceY - def.FootprintH + 1 : faceY;
            bool plots = HasPlots(def);
            int margin = plots ? 2 : 1;
            for (int ty = bottom - margin; ty < bottom + def.FootprintH + margin; ty++)
                for (int tx = left - margin; tx < left + def.FootprintW + margin; tx++)
                {
                    if (!world.InBounds(tx, ty)) return false;
                    int tile = world.Index(tx, ty);
                    if (world.BuildingIndex[tile] >= 0) return false;
                    bool inColumns = tx >= left && tx < left + def.FootprintW, inRows = ty >= bottom && ty < bottom + def.FootprintH;
                    bool inside = inColumns && inRows;
                    bool alongSide = plots && !inside && (inColumns ? ty >= bottom - 1 && ty <= bottom + def.FootprintH : inRows && tx >= left - 1 && tx <= left + def.FootprintW);
                    if (!inside && !alongSide) continue;
                    if (!terrainWalkable[world.Terrain[tile]] || world.Node[tile] != 0 || (inside && dangerAt[tile] == dangerStamp)) return false;
                }
            return true;
        }

        /// <summary>The nearest place to the village centre where the building fits.</summary>
        bool FindBuildSpot(Simulation sim, BuildingDef def)
        {
            var world = sim.World;
            for (int r = 2; r <= SpotSearchRadius; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Chebyshev(dx, dy) != r) continue;
                        int x = world.SpawnX + dx, y = world.SpawnY + dy;
                        for (int d = 0; d < 4; d++)
                        {
                            if (!SpotFits(sim, def, x, y, StepX[d], StepY[d])) continue;
                            spotStand = world.Index(x, y);
                            spotDx = StepX[d];
                            spotDy = StepY[d];
                            spotFor = def;
                            return true;
                        }
                    }
            return false;
        }

        bool Place(Simulation sim, Agent agent, Plan plan, BuildingDef def, out AgentAction action)
        {
            var world = sim.World;
            var wait = new AgentAction(sim.Actions.Wait);
            bool chosen = spotFor == def && spotStand >= 0
                && SpotFits(sim, def, spotStand % world.Width, spotStand / world.Width, spotDx, spotDy);
            if (!chosen && !FindBuildSpot(sim, def))
            {
                action = default;
                return false;
            }
            int x = spotStand % world.Width, y = spotStand / world.Width;
            if (agent.X == x && agent.Y == y && agent.FaceDx == spotDx && agent.FaceDy == spotDy)
            {
                action = new AgentAction(sim.Actions.CraftBuild, ArgOf(def));
                if (sim.IsValid(agent.Index, action))
                {
                    placeFails = 0;
                    return true;
                }
                // Someone is standing where it should go. Give them a moment, then look for another place.
                if (++placeFails > PlacePatience)
                {
                    placeFails = 0;
                    spotStand = -1;
                }
                action = wait;
                return true;
            }
            return StepInto(sim, agent, plan, x, y, spotDx, spotDy, out action);
        }

        /// <summary>
        /// Gets the villager onto (x, y) by a last step in direction (dx, dy). A villager faces the way
        /// it last walked, so this is how it comes to face a tile that has nothing on it yet.
        /// </summary>
        bool StepInto(Simulation sim, Agent agent, Plan plan, int x, int y, int dx, int dy, out AgentAction action)
        {
            int fromX = x - dx, fromY = y - dy;
            if (agent.X == fromX && agent.Y == fromY)
            {
                action = sim.CanEnter(x, y) ? MoveToward(sim, dx, dy) : new AgentAction(sim.Actions.Wait);
                return true;
            }
            plan.SpotTile = sim.World.Index(fromX, fromY);
            return Go(sim, agent, plan, Goal.Spot, new AgentAction(sim.Actions.Wait), out action);
        }

        // ------------------------------------------------------------------ the farms

        /// <summary>The farmer reaps what is ripe and sows every empty plot it has seed for, in season, on every farm there is.</summary>
        bool Farm(Simulation sim, Agent agent, Plan plan, out AgentAction action)
        {
            action = default;
            if (agent.Role != farmerRole || farms.Count == 0) return false;
            var actions = sim.Actions;
            if (Go(sim, agent, plan, Goal.Reap, new AgentAction(actions.Harvest), out action)) return true;

            var world = sim.World;
            int plot = -1;
            foreach (var plotted in farms)
                for (int y = plotted.Y - 1; y <= plotted.Y + plotted.Height && plot < 0; y++)
                    for (int x = plotted.X - 1; x <= plotted.X + plotted.Width && plot < 0; x++)
                    {
                        if (!world.InBounds(x, y)) continue;
                        int tile = world.Index(x, y);
                        if (world.Terrain[tile] == farmlandTerrain && world.Node[tile] == 0 && world.AgentIndex[tile] < 0) plot = tile;
                    }
            if (plot < 0) return false;

            string season = ";" + sim.Season.Id + ";";
            int arg = 0;
            for (int i = 0; i < sowArgs.Length && arg == 0; i++)
            {
                if (sowArgs[i] == 0 || sowSeasons[i].IndexOf(season, StringComparison.Ordinal) < 0) continue;
                if (agent.Count(sowSeeds[i]) > 0) arg = sowArgs[i];
            }
            if (arg == 0)
            {
                // Seed from the chest, if the pack can take it.
                for (int i = 0; i < sowArgs.Length; i++)
                {
                    if (sowArgs[i] == 0 || sowSeasons[i].IndexOf(season, StringComparison.Ordinal) < 0 || Stored(sowSeeds[i]) == 0) continue;
                    if (Slots.Room(agent.Inventory, agent.InventoryCapacity, sowSeeds[i], itemMaxStack[sowSeeds[i]]) <= 0) continue;
                    return Go(sim, agent, plan, Goal.Chest, new AgentAction(actions.Take, sowSeeds[i]), out action);
                }
                return false;
            }

            // Stand next to the plot, having stepped toward it.
            int plotX = plot % world.Width, plotY = plot / world.Width;
            for (int d = 0; d < 4; d++)
            {
                int x = plotX - StepX[d], y = plotY - StepY[d];
                if (!OpenGround(world, x, y) || !OpenGround(world, x - StepX[d], y - StepY[d])) continue;
                if (agent.X == x && agent.Y == y && agent.FaceDx == StepX[d] && agent.FaceDy == StepY[d])
                {
                    action = new AgentAction(actions.CraftBuild, arg);
                    if (!sim.IsValid(agent.Index, action)) action = new AgentAction(actions.Wait);
                    return true;
                }
                if (StepInto(sim, agent, plan, x, y, StepX[d], StepY[d], out action)) return true;
            }
            return false;
        }
    }
}
