using System;
using System.Collections.Generic;
using TinyTribe.Sim.Core;
using TinyTribe.Sim.Data;

namespace TinyTribe.Brains
{
    /// <summary>What the bots do together: dangerous animals, the chest, the campfire.</summary>
    public sealed partial class SurvivalBrain
    {
        // Bot habits, not game balance.
        const int KeepClear = 2;
        const int EngageWithin = 2;
        const int FeedFireBelow = 240;
        const string RainOnFire = "campfire_extinguish_chance_per_tick";
        const string LibraryEffect = "rp_per_tick";
        const int BuilderRange = 40;
        const int HomeRadius = 6;
        const int TurnInMargin = 10;
        const int FuelReserve = 4;
        const int ChestIsNear = 16;
        const int MinSlotsForFire = 4;
        const int BuilderReviewTicks = 60;
        const string ResidentialCategory = "residential";
        const int CentreRadius = 2, MaxCentreRadius = 6, WidenCentreAfterTicks = 25;

        // The village as the bots see it, refreshed once per tick.
        long lookedAt = -1;
        Building chest, fire, site;
        bool hasLumberjack, hasScholar;

        /// <summary>Someone who fishes is without a rod and the cotton for one is at hand: the chest's wood is spoken for.</summary>
        bool rodWaiting;
        int builder = -1;
        int centreRadius = CentreRadius;
        int placeFails;
        readonly List<Animal> threats = new List<Animal>();
        int[] dangerAt = new int[0];
        int dangerStamp;

        void ForgetVillage()
        {
            lookedAt = -1;
            chest = fire = site = null;
            builder = -1;
            centreRadius = CentreRadius;
            placeFails = 0;
            threats.Clear();
            dangerStamp = 0;
        }

        void LookAround(Simulation sim)
        {
            if (sim.Tick == lookedAt) return;
            lookedAt = sim.Tick;

            chest = fire = site = mill = farm = garden = circle = libraryInAnyState = studyPlace = null;
            farms.Clear();
            Building library = null;
            foreach (var building in sim.Buildings)
            {
                if (building.Destroyed) continue;
                var def = building.Def;
                if (!building.Complete && IsOurWork(def)) site = site ?? building;
                if (building.Complete && def.EffectKey == LibraryEffect) library = library ?? building;
                if (building.Complete && HasPlots(def)) farms.Add(building);
                if (def == chestDef) chest = chest ?? building;
                else if (def == fireDef) fire = fire ?? building;
                else if (def == millDef) mill = mill ?? building;
                else if (def == libraryDef) libraryInAnyState = libraryInAnyState ?? building;
                else if (def == farmDef) farm = farm ?? building;
                else if (def == gardenDef) garden = garden ?? building;
                else if (def == circleDef) circle = circle ?? building;
            }
            // The Library teaches everything and faster; the circle only the simple things.
            var studied = sim.ResearchingTech;
            if (studied != null) studyPlace = library ?? (circle != null && circle.Complete && studied.NeedsLibrary == 0 ? circle : null);

            hasLumberjack = hasScholar = rodWaiting = false;
            foreach (var agent in sim.Agents)
            {
                if (!agent.Alive || !IsDriven(sim, agent.Index)) continue;
                hasLumberjack |= agent.Role == lumberjackRole;
                hasScholar |= agent.Role == scholarRole;
                rodWaiting |= Fishes(agent) && !Owns(agent, rodItem) && agent.Count(woodItem) < rodWood
                    && agent.Count(cottonItem) + Stored(cottonItem) >= rodCotton;
            }

            // Animals that bite. The slow ones are walked around, with room to spare.
            threats.Clear();
            dangerStamp++;
            var world = sim.World;
            foreach (var animal in sim.Animals)
            {
                if (!animal.Alive || animal.Def.AttackDamage <= 0f) continue;
                threats.Add(animal);
                if (!CanOutrun(animal.Def)) continue;
                int reach = animal.Def.AggroRadius + KeepClear;
                int fromX = Math.Max(0, animal.X - reach), toX = Math.Min(world.Width - 1, animal.X + reach);
                int fromY = Math.Max(0, animal.Y - reach), toY = Math.Min(world.Height - 1, animal.Y + reach);
                for (int y = fromY; y <= toY; y++)
                    for (int x = fromX; x <= toX; x++) dangerAt[world.Index(x, y)] = dangerStamp;
            }

            PickBuilder(sim);
        }

        // ------------------------------------------------------------------ animals

        static bool CanOutrun(AnimalDef animal) => animal.SpeedTilesPerTick < 1f;

        float HandDamage(Simulation sim, Agent agent)
        {
            var hand = agent.Equipped[(int)EquipSlot.Hand];
            var tool = hand.IsEmpty ? null : sim.Data.EquipmentByItemInt(hand.Item);
            return tool != null && tool.AttackDamage > 0f ? tool.AttackDamage : sim.Config.BareHandDamage;
        }

        int Reach(Simulation sim, Agent agent)
        {
            var hand = agent.Equipped[(int)EquipSlot.Hand];
            var tool = hand.IsEmpty ? null : sim.Data.EquipmentByItemInt(hand.Item);
            return tool != null ? Math.Max(1, tool.AttackRange) : 1;
        }

        /// <summary>
        /// Slow animals (bears) are kept at a distance: nothing a villager carries beats one.
        /// Fast ones (wolves) cannot be outrun, so they are fought when they come: the villager
        /// turns to face them and strikes as soon as they are in reach, but does not go after them.
        /// Returns false when there is nothing to do about the animal, so that a villager never
        /// stands guard against, or dances around, something it cannot reach while it goes hungry.
        /// </summary>
        bool Defend(Simulation sim, Agent agent, Plan plan, out AgentAction action)
        {
            action = default;
            if (threats.Count == 0) return false;
            Animal fight = null, avoid = null;
            int fightDistance = int.MaxValue, avoidDistance = int.MaxValue;
            foreach (var animal in threats)
            {
                int distance = Chebyshev(animal.X - agent.X, animal.Y - agent.Y);
                if (CanOutrun(animal.Def))
                {
                    if (distance > animal.Def.AggroRadius + KeepClear || distance >= avoidDistance) continue;
                    avoid = animal;
                    avoidDistance = distance;
                }
                else
                {
                    if (distance > EngageWithin || distance >= fightDistance) continue;
                    fight = animal;
                    fightDistance = distance;
                }
            }

            bool acting = avoid != null && Flee(sim, agent, avoid, out action);
            if (!acting && fight != null) acting = Fight(sim, agent, fight, out action);
            // Cornered by a bear: hit back if it is in reach.
            if (!acting && avoid != null) acting = Fight(sim, agent, avoid, out action);
            if (acting) plan.Clear();
            return acting;
        }

        /// <summary>One step to the neighbouring tile that is farthest from the animal; false when cornered.</summary>
        bool Flee(Simulation sim, Agent agent, Animal animal, out AgentAction action)
        {
            action = default;
            int best = -1, bestScore = Apart(agent.X, agent.Y, animal);
            for (int d = 0; d < 4; d++)
            {
                int x = agent.X + StepX[d], y = agent.Y + StepY[d];
                if (!sim.CanEnter(x, y)) continue;
                int score = Apart(x, y, animal);
                if (score <= bestScore) continue;
                bestScore = score;
                best = d;
            }
            if (best < 0) return false;
            action = MoveToward(sim, StepX[best], StepY[best]);
            return true;
        }

        static int Apart(int x, int y, Animal animal)
        {
            int dx = x - animal.X, dy = y - animal.Y;
            return Chebyshev(dx, dy) * 16 + Math.Abs(dx) + Math.Abs(dy);
        }

        /// <summary>Takes the spear in hand, turns to the animal, and hits it when it is in reach. False when it is not.</summary>
        bool Fight(Simulation sim, Agent agent, Animal animal, out AgentAction action)
        {
            var actions = sim.Actions;
            if (HandDamage(sim, agent) < spearDamage && agent.Count(spearItem) > 0)
            {
                action = new AgentAction(actions.Equip, spearItem);
                return true;
            }
            int dx = animal.X - agent.X, dy = animal.Y - agent.Y;
            int sx = Math.Sign(dx), sy = Math.Sign(dy);
            int distance = Math.Abs(dx) + Math.Abs(dy);
            if ((dx == 0 || dy == 0) && distance <= Reach(sim, agent) && LineClear(sim, agent, sx, sy, distance))
            {
                // Stepping at an animal that is in the way only turns the villager to face it.
                action = agent.FaceDx != sx || agent.FaceDy != sy ? MoveToward(sim, sx, sy) : new AgentAction(actions.Harvest);
                return true;
            }
            action = default;
            return false;
        }

        /// <summary>Nothing between the villager and a target that many tiles along the line.</summary>
        static bool LineClear(Simulation sim, Agent agent, int sx, int sy, int distance)
        {
            var world = sim.World;
            for (int step = 1; step < distance; step++)
            {
                int tile = world.Index(agent.X + sx * step, agent.Y + sy * step);
                if (world.HasActiveNode(tile) || world.BuildingIndex[tile] >= 0 || world.AgentIndex[tile] >= 0) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ company and home

        /// <summary>
        /// Villagers greet whoever is near, now and then: a happy emote when someone is within reach of
        /// it, in place of a step or a moment of waiting, no more often than it counts. Nothing is
        /// greeted with a wolf about.
        /// </summary>
        AgentAction Greet(Simulation sim, Agent agent, Plan plan, AgentAction action)
        {
            var actions = sim.Actions;
            bool canSpare = action.Id == actions.Wait || action.Id == actions.MoveNorth || action.Id == actions.MoveSouth
                || action.Id == actions.MoveEast || action.Id == actions.MoveWest;
            if (!canSpare || agent.BusyTicks > 0 || sim.Tick - plan.GreetedAt < greetEveryTicks) return action;
            foreach (var animal in threats)
                if (Chebyshev(animal.X - agent.X, animal.Y - agent.Y) <= animal.Def.AggroRadius + KeepClear) return action;
            var greeting = new AgentAction(actions.EmoteHappy);
            if (!sim.IsValid(agent.Index, greeting)) return action;
            foreach (var other in sim.Agents)
            {
                if (!other.Alive || other == agent || Chebyshev(other.X - agent.X, other.Y - agent.Y) > socialRange) continue;
                plan.GreetedAt = sim.Tick;
                return greeting;
            }
            return action;
        }

        /// <summary>
        /// The house a villager sleeps at: its home if it has one; otherwise, for someone married whose
        /// spouse has none either, a finished house nobody lives in, which it will claim by sleeping
        /// there (and its spouse then shares). Those who are single sleep by the fire.
        /// </summary>
        Building HouseFor(Simulation sim, Agent agent)
        {
            if (agent.Home >= 0) return sim.Buildings[agent.Home];
            if (agent.Spouse < 0 || sim.Agents[agent.Spouse].Home >= 0) return null;
            foreach (var building in sim.Buildings)
                if (!building.Destroyed && building.Complete && building.Def.Category == ResidentialCategory && building.Residents.Count == 0) return building;
            return null;
        }

        /// <summary>Couples without a home, less the houses standing empty or being built for them.</summary>
        int HomesShort(Simulation sim)
        {
            int couples = 0, free = 0;
            foreach (var agent in sim.Agents)
                if (agent.Alive && agent.Spouse > agent.Index && agent.Home < 0 && sim.Agents[agent.Spouse].Home < 0) couples++;
            foreach (var building in sim.Buildings)
                if (!building.Destroyed && building.Def.Category == ResidentialCategory && building.Residents.Count == 0) free++;
            return couples - free;
        }

        // ------------------------------------------------------------------ who builds

        /// <summary>
        /// One villager builds and tends the fire: the one with the most room in its pack, because the
        /// campfire needs two materials carried at once. Reviewed now and then, and when the builder dies
        /// or is taken over by the player.
        /// </summary>
        void PickBuilder(Simulation sim)
        {
            // The Council's choice comes first; until it has made one, the bots settle it among themselves.
            foreach (var agent in sim.Agents)
            {
                if (!agent.Alive || agent.Role != builderRole || !IsDriven(sim, agent.Index)) continue;
                builder = agent.Index;
                return;
            }

            bool current = builder >= 0 && builder < sim.Agents.Count && sim.Agents[builder].Alive && IsDriven(sim, builder);
            if (current && sim.Tick % BuilderReviewTicks != 0) return;

            int best = current ? builder : -1;
            int bestSlots = current ? UsableSlots(sim.Agents[builder]) : -1;
            // A builder that can do the job keeps it, also when someone with a bigger pack comes along.
            if (current && bestSlots >= MinSlotsForFire && sim.Tick - boundTick >= BuilderReviewTicks) return;
            for (int i = 0; i < sim.Agents.Count; i++)
            {
                var agent = sim.Agents[i];
                if (!agent.Alive || !IsDriven(sim, i)) continue;
                int slots = UsableSlots(agent);
                if (slots <= bestSlots) continue;
                best = i;
                bestSlots = slots;
            }
            builder = best;
        }

        /// <summary>Is this brain choosing for that villager (and not the player or another brain)?</summary>
        bool IsDriven(Simulation sim, int agentIndex) =>
            sim.Tick - boundTick < 3 || (agentIndex < plans.Count && sim.Tick - plans[agentIndex].AskedAt <= 2);

        /// <summary>Pack slots not stuck with things that only a chest can take.</summary>
        int UsableSlots(Agent agent)
        {
            int slots = agent.InventoryCapacity;
            if (chest != null && chest.Complete) return slots;
            for (int i = 0; i < agent.InventoryCapacity; i++)
            {
                var stack = agent.Inventory[i];
                if (stack.Qty <= 0) continue;
                int item = stack.Item;
                bool useful = itemIsSafeFood[item] || item == waterItem || item == woodItem || item == stoneItem
                    || item == spearItem || item == pickaxeItem;
                if (!useful) slots--;
            }
            return slots;
        }

        // ------------------------------------------------------------------ building

        static int Cost(BuildingDef def, string itemId) =>
            (def.CostItem1 == itemId ? def.CostQty1 : 0) + (def.CostItem2 == itemId ? def.CostQty2 : 0) + (def.CostItem3 == itemId ? def.CostQty3 : 0);

        /// <summary>
        /// The chest first: packs are small, and the campfire's builder needs somewhere to leave things.
        /// Then the fire. Then the other works, one at a time and simple ones first: a garden, an Elders'
        /// Circle, and what research unlocks after that (see SurvivalBrain.Projects.cs).
        /// </summary>
        bool BuildVillage(Simulation sim, Agent agent, Plan plan, out AgentAction action)
        {
            builderTool = 0;
            project = null;
            loadedForBuilding = false;
            if (chest == null || !chest.Complete) return Construct(sim, agent, plan, chestDef, chestArg, out action);
            if (fire == null || !fire.Complete)
            {
                if (agent.InventoryCapacity >= MinSlotsForFire) return Construct(sim, agent, plan, fireDef, fireArg, out action);
                action = default;
                return false;
            }
            // A building under way is finished before anything else is begun.
            if (site != null) return Go(sim, agent, plan, Goal.Site, new AgentAction(sim.Actions.CraftBuild, ArgOf(site.Def)), out action);
            // With nobody else to bring wood, the fire's reserve comes before new works.
            if (!hasLumberjack && agent.Count(woodItem) < FuelReserve && Stored(woodItem) == 0
                && Go(sim, agent, plan, Goal.Wood, new AgentAction(sim.Actions.Harvest), out action)) return true;

            var next = agent.InventoryCapacity >= MinSlotsForFire ? NextProject(sim) : null;
            if (next != null && BigBuild(sim, agent, plan, next, out action)) return true;
            project = null;
            loadedForBuilding = false;
            action = default;
            return false;
        }

        bool Construct(Simulation sim, Agent agent, Plan plan, BuildingDef def, int arg, out AgentAction action)
        {
            var actions = sim.Actions;
            var build = new AgentAction(actions.CraftBuild, arg);
            if (site != null) return Go(sim, agent, plan, Goal.Site, build, out action);

            if (agent.Count(stoneItem) < Cost(def, StoneId)) return GetStone(sim, agent, plan, out action);
            if (agent.Count(woodItem) < Cost(def, WoodId)) return GetWood(sim, agent, plan, out action);

            // A villager faces the way it last walked, so the place to build is found on foot:
            // go to the middle of the village and put the building on good ground straight ahead.
            var world = sim.World;
            if (Chebyshev(agent.X - world.SpawnX, agent.Y - world.SpawnY) > centreRadius)
                return Go(sim, agent, plan, Goal.Centre, new AgentAction(actions.Wait), out action);

            plan.Clear();
            if (GoodGround(sim, agent.X + agent.FaceDx, agent.Y + agent.FaceDy) && sim.IsValid(agent.Index, build))
            {
                placeFails = 0;
                action = build;
                return true;
            }
            for (int d = 0; d < 4; d++)
            {
                int x = agent.X + StepX[d], y = agent.Y + StepY[d];
                if (!sim.CanEnter(x, y) || !GoodGround(sim, x + StepX[d], y + StepY[d])) continue;
                action = MoveToward(sim, StepX[d], StepY[d]);
                return true;
            }
            // Crowded here: look around, and a little farther out if that keeps failing.
            if (++placeFails % WidenCentreAfterTicks == 0 && centreRadius < MaxCentreRadius) centreRadius++;
            action = RandomMove(sim, agent);
            return true;
        }

        /// <summary>Free, open ground with a tile's gap to the next building.</summary>
        bool GoodGround(Simulation sim, int x, int y)
        {
            var world = sim.World;
            if (!world.InBounds(x, y)) return false;
            int tile = world.Index(x, y);
            if (!terrainWalkable[world.Terrain[tile]] || world.Node[tile] != 0 || world.BuildingIndex[tile] >= 0
                || world.AgentIndex[tile] >= 0 || world.AnimalIndex[tile] >= 0 || dangerAt[tile] == dangerStamp) return false;

            int open = 0;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    if (!world.InBounds(x + dx, y + dy)) continue;
                    int near = world.Index(x + dx, y + dy);
                    if (world.BuildingIndex[near] >= 0) return false;
                    if (dx != 0 && dy != 0) continue;
                    if (terrainWalkable[world.Terrain[near]] && !world.HasActiveNode(near)) open++;
                }
            return open >= 3;
        }

        /// <summary>Stone takes a pickaxe, and the pickaxe takes wood.</summary>
        bool GetStone(Simulation sim, Agent agent, Plan plan, out AgentAction action)
        {
            action = default;
            var actions = sim.Actions;
            var harvest = new AgentAction(actions.Harvest);
            builderTool = pickaxeItem;
            if (agent.Equipped[(int)EquipSlot.Hand].Item == pickaxeItem) return Go(sim, agent, plan, Goal.Stone, harvest, out action);
            if (agent.Count(pickaxeItem) > 0) return false;

            var craft = new AgentAction(actions.CraftBuild, pickaxeArg);
            if (sim.IsValid(agent.Index, craft))
            {
                action = craft;
                return true;
            }
            if (agent.Count(woodItem) >= pickaxeWood) return false;
            return GetWood(sim, agent, plan, out action);
        }

        /// <summary>Wood from the chest when there is some and the pack can take it; otherwise from the trees.</summary>
        bool GetWood(Simulation sim, Agent agent, Plan plan, out AgentAction action)
        {
            var actions = sim.Actions;
            if (Stored(woodItem) > 0 && Slots.Room(agent.Inventory, agent.InventoryCapacity, woodItem, itemMaxStack[woodItem]) > 0
                && Go(sim, agent, plan, Goal.Chest, new AgentAction(actions.Take, woodItem), out action)) return true;
            return Go(sim, agent, plan, Goal.Wood, new AgentAction(actions.Harvest), out action);
        }

        // ------------------------------------------------------------------ the fire

        bool NearFire(int x, int y) =>
            fire != null && Chebyshev(x - fire.X, y - fire.Y) <= (int)fire.Def.EffectValue;

        int StepsToFire(Agent agent) => Math.Abs(agent.X - fire.X) + Math.Abs(agent.Y - fire.Y);

        /// <summary>
        /// Cold enough to head for the fire, counting what the walk there will cost; stays true until
        /// properly warm again.
        /// </summary>
        bool FeelsCold(Simulation sim, Agent agent, Plan plan)
        {
            float temp = agent.Get(Need.Temp);
            if (fire == null || !fire.IsLit || temp >= WarmEnough) plan.Warming = false;
            else
            {
                float ambient = sim.IsNight ? sim.Season.TempDeltaNight : sim.Season.TempDeltaDay;
                float lostOnTheWay = ambient < 0f ? -ambient * agent.EnduranceMult * agent.ColdDrainMult * StepsToFire(agent) : 0f;
                if (temp < ColdBelow + lostOnTheWay) plan.Warming = true;
            }
            return plan.Warming;
        }

        /// <summary>Night, or late enough in the day that the walk to the fire takes what is left of it.</summary>
        bool TimeToTurnIn(Simulation sim, Agent agent)
        {
            if (sim.IsNight) return true;
            if (fire == null || !fire.IsLit) return false;
            int ticksOfDayLeft = (sim.Config.NightStartHour - sim.Hour) * sim.Config.TicksPerHour;
            return ticksOfDayLeft <= StepsToFire(agent) + TurnInMargin;
        }

        /// <summary>The builder looks after the fire, and so does a Lumberjack, who has the wood.</summary>
        bool TendsFire(Agent agent) => agent.Index == builder || agent.Role == lumberjackRole;

        /// <summary>Whoever tends the fire feeds it before it burns down, from the pack, the chest or the trees.</summary>
        bool TendFire(Simulation sim, Agent agent, Plan plan, out AgentAction action)
        {
            action = default;
            if (!TendsFire(agent) || fire == null || !fire.Complete) return false;
            // Wood put on a fire in the rain is wood wasted: relight it when the weather clears.
            if (sim.Weather.Effect(RainOnFire) > 0f) return false;
            // The farther away, the earlier it turns back.
            if (fire.FuelTicks >= FeedFireBelow + 2 * StepsToFire(agent)) return false;
            var actions = sim.Actions;
            if (agent.Count(woodItem) > 0) return Go(sim, agent, plan, Goal.Fire, new AgentAction(actions.Deposit, woodItem), out action);
            if (chest != null && chest.Complete && agent.FreeSlots > 0
                && Slots.Count(chest.Storage, chest.Storage.Length, woodItem) > 0)
                return Go(sim, agent, plan, Goal.Chest, new AgentAction(actions.Take, woodItem), out action);
            // Wood is cut by day; a night in the trees costs more warmth than the fire gives back.
            if (sim.IsNight) return false;
            return Go(sim, agent, plan, Goal.Wood, new AgentAction(actions.Harvest), out action);
        }

        /// <summary>The weather takes warmth right now (any night, and the days of the cold seasons).</summary>
        static bool Freezing(Simulation sim) =>
            (sim.IsNight ? sim.Season.TempDeltaNight : sim.Season.TempDeltaDay) < 0f;
    }
}
