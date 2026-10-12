using System;
using System.Collections.Generic;
using TinyTribe.Sim.Data;

namespace TinyTribe.Sim.Core
{
    /// <summary>Animals: spawning, moving, biting, and being hunted (Sim Rules R03, R05, R32, R33).</summary>
    public sealed partial class Simulation
    {
        const string BehaviorFlee = "flee";
        const string BehaviorTerritorial = "territorial";
        const int SpawnAttempts = 400;

        static readonly int[] StepX = { 1, -1, 0, 0 };
        static readonly int[] StepY = { 0, 0, 1, -1 };

        readonly List<Animal> animals = new List<Animal>();
        int[] animalTargets;
        int rawMeatItem, animalHideItem, boneItem;

        // A coarse grid of where the living villagers are, rebuilt each tick, so that an animal only
        // looks at the villagers near it. A cell's list is valid while cellSeenAgent equals cellMark.
        int cellShift, cellsWide, cellsHigh;
        long[] cellSeenAgent;
        int[] cellFirstAgent;
        int[] nextAgentInCell = new int[0];
        long cellMark;

        /// <summary>Every animal that was ever on the map, by index. Dead ones stay in the list until their place is reused.</summary>
        public IReadOnlyList<Animal> Animals => animals;

        void InitAnimals()
        {
            rawMeatItem = Data.ItemById["raw_meat"].Int;
            animalHideItem = Data.ItemById["animal_hide"].Int;
            boneItem = Data.ItemById["bone"].Int;

            // Cells at least as wide as the longest sight, so an animal only has to look at its own cell and the ones around it.
            int sight = 1;
            foreach (var def in Data.Animals) sight = Math.Max(sight, Math.Max(def.FleeRadius, def.AggroRadius));
            cellShift = 3;
            while (1 << cellShift < sight) cellShift++;
            cellsWide = (World.Width >> cellShift) + 1;
            cellsHigh = (World.Height >> cellShift) + 1;
            cellSeenAgent = new long[cellsWide * cellsHigh];
            cellFirstAgent = new int[cellsWide * cellsHigh];
        }

        /// <summary>The living animal on the tile, or null.</summary>
        public Animal AnimalAt(int x, int y)
        {
            if (!World.InBounds(x, y)) return null;
            int index = World.AnimalIndex[World.Index(x, y)];
            return index < 0 ? null : animals[index];
        }

        public Animal SpawnAnimal(string animalId, int x, int y)
        {
            var def = Data.AnimalById(animalId);
            if (!CanEnter(x, y)) throw new InvalidOperationException("Cannot place a " + animalId + " at " + x + "," + y);

            // Reuse the place of a dead animal so the list does not grow for ever.
            int index = animals.FindIndex(a => !a.Alive);
            var animal = new Animal { Index = index < 0 ? animals.Count : index, Def = def, X = x, Y = y, HomeX = x, HomeY = y, Hp = def.Hp };
            if (index < 0) animals.Add(animal);
            else animals[index] = animal;
            World.AnimalIndex[World.Index(x, y)] = animal.Index;
            return animal;
        }

        /// <summary>Takes an animal off the map without loot.</summary>
        public void DespawnAnimal(Animal animal)
        {
            if (!animal.Alive) return;
            animal.Alive = false;
            World.AnimalIndex[World.Index(animal.X, animal.Y)] = -1;
        }

        /// <summary>How many of the species the map is kept stocked with; 0 before PopulateAnimals.</summary>
        public int AnimalTarget(string animalId)
        {
            if (animalTargets == null) return 0;
            for (int i = 0; i < Data.Animals.Count; i++)
                if (Data.Animals[i].Id == animalId) return animalTargets[i];
            return 0;
        }

        /// <summary>R33: stocks the map with each species at its density, away from the village.</summary>
        public void PopulateAnimals()
        {
            int walkable = 0;
            for (int tile = 0; tile < World.Terrain.Length; tile++)
                if (terrainWalkable[World.Terrain[tile]]) walkable++;

            animalTargets = new int[Data.Animals.Count];
            for (int i = 0; i < Data.Animals.Count; i++)
            {
                var def = Data.Animals[i];
                animalTargets[i] = Math.Max(1, (int)Math.Round(def.SpawnPer1000Tiles * walkable / 1000.0));
                for (int n = 0; n < animalTargets[i]; n++) TrySpawnSomewhere(def);
            }
        }

        bool TrySpawnSomewhere(AnimalDef def)
        {
            int distance = Config.AnimalVillageDistance;
            for (int attempt = 0; attempt < SpawnAttempts; attempt++)
            {
                int x = rng.Range(0, World.Width), y = rng.Range(0, World.Height);
                if (!CanEnter(x, y) || Chebyshev(x - World.SpawnX, y - World.SpawnY) < distance) continue;
                bool nearVillager = false;
                foreach (var agent in agents)
                    nearVillager |= agent.Alive && Chebyshev(x - agent.X, y - agent.Y) < distance;
                if (nearVillager) continue;
                SpawnAnimal(def.Id, x, y);
                return true;
            }
            return false;
        }

        void UpdateAnimals()
        {
            if (animalTargets != null && Tick > 0 && Tick % Config.AnimalRespawnIntervalTicks == 0)
                for (int i = 0; i < Data.Animals.Count; i++)
                {
                    int alive = 0;
                    foreach (var animal in animals)
                        if (animal.Alive && animal.Def == Data.Animals[i]) alive++;
                    if (alive < animalTargets[i]) TrySpawnSomewhere(Data.Animals[i]);
                }

            cellMark = Tick + 1;
            if (nextAgentInCell.Length < agents.Count) nextAgentInCell = new int[Math.Max(agents.Count, Config.MaxAgents)];
            for (int i = 0; i < agents.Count; i++)
            {
                var agent = agents[i];
                if (!agent.Alive) continue;
                int cell = (agent.Y >> cellShift) * cellsWide + (agent.X >> cellShift);
                if (cellSeenAgent[cell] != cellMark)
                {
                    cellSeenAgent[cell] = cellMark;
                    cellFirstAgent[cell] = -1;
                }
                nextAgentInCell[i] = cellFirstAgent[cell];
                cellFirstAgent[cell] = i;
            }

            for (int i = 0; i < animals.Count; i++)
                if (animals[i].Alive) UpdateAnimal(animals[i]);
        }

        /// <summary>R32.</summary>
        void UpdateAnimal(Animal animal)
        {
            if (animal.AttackCooldown > 0) animal.AttackCooldown--;
            var def = animal.Def;
            var nearest = NearestAgent(animal.X, animal.Y, Math.Max(def.FleeRadius, def.AggroRadius), out int distance);

            if (def.Behavior == BehaviorFlee)
            {
                if (nearest != null && distance <= def.FleeRadius)
                {
                    Travel(animal, nearest.X, nearest.Y, away: true);
                    return;
                }
            }
            else
            {
                if (def.Behavior == BehaviorTerritorial && Chebyshev(animal.X - animal.HomeX, animal.Y - animal.HomeY) > Config.TerritorialLeash)
                {
                    Travel(animal, animal.HomeX, animal.HomeY, away: false);
                    return;
                }
                if (nearest != null && distance <= def.AggroRadius)
                {
                    int dx = nearest.X - animal.X, dy = nearest.Y - animal.Y;
                    if (Math.Abs(dx) + Math.Abs(dy) == 1)
                    {
                        animal.FaceDx = dx;
                        animal.FaceDy = dy;
                        if (animal.AttackCooldown == 0) Bite(animal, nearest);
                    }
                    else Travel(animal, nearest.X, nearest.Y, away: false);
                    return;
                }
            }

            if (rng.Chance(Config.AnimalWanderChance))
            {
                int direction = rng.Range(0, 4);
                TryStep(animal, StepX[direction], StepY[direction]);
            }
        }

        /// <summary>The nearest living villager within the radius; of two equally near, the one with the lower index.</summary>
        Agent NearestAgent(int x, int y, int radius, out int distance)
        {
            Agent best = null;
            distance = int.MaxValue;
            int cx = x >> cellShift, cy = y >> cellShift;
            int fromX = Math.Max(0, cx - 1), toX = Math.Min(cellsWide - 1, cx + 1);
            int fromY = Math.Max(0, cy - 1), toY = Math.Min(cellsHigh - 1, cy + 1);
            for (int j = fromY; j <= toY; j++)
                for (int i = fromX; i <= toX; i++)
                {
                    int cell = j * cellsWide + i;
                    if (cellSeenAgent[cell] != cellMark) continue;
                    for (int a = cellFirstAgent[cell]; a >= 0; a = nextAgentInCell[a])
                    {
                        var agent = agents[a];
                        // Someone bitten to death earlier this tick is still in the list.
                        if (!agent.Alive) continue;
                        int d = Chebyshev(agent.X - x, agent.Y - y);
                        if (d > radius || d > distance || (d == distance && a > best.Index)) continue;
                        distance = d;
                        best = agent;
                    }
                }
            return best;
        }

        /// <summary>Spends this tick's movement stepping toward or away from a point. Speed above 1 gives extra steps; below 1 skips ticks.</summary>
        void Travel(Animal animal, int pointX, int pointY, bool away)
        {
            animal.MoveBudget += animal.Def.SpeedTilesPerTick;
            while (animal.MoveBudget >= 1f)
            {
                animal.MoveBudget -= 1f;
                int here = Score(animal.X, animal.Y, pointX, pointY, away);
                int best = -1, bestScore = here;
                for (int d = 0; d < 4; d++)
                {
                    int x = animal.X + StepX[d], y = animal.Y + StepY[d];
                    if (!CanEnter(x, y)) continue;
                    int score = Score(x, y, pointX, pointY, away);
                    if (score <= bestScore) continue;
                    bestScore = score;
                    best = d;
                }
                if (best < 0) return;
                TryStep(animal, StepX[best], StepY[best]);
            }
        }

        /// <summary>Higher is better: far when fleeing, near when chasing. Diagonal distance first, then steps.</summary>
        static int Score(int x, int y, int pointX, int pointY, bool away)
        {
            int dx = x - pointX, dy = y - pointY;
            int value = Chebyshev(dx, dy) * 16 + Math.Abs(dx) + Math.Abs(dy);
            return away ? value : -(Math.Abs(dx) + Math.Abs(dy));
        }

        bool TryStep(Animal animal, int dx, int dy)
        {
            int x = animal.X + dx, y = animal.Y + dy;
            if (!CanEnter(x, y)) return false;
            World.AnimalIndex[World.Index(animal.X, animal.Y)] = -1;
            World.AnimalIndex[World.Index(x, y)] = animal.Index;
            animal.X = x;
            animal.Y = y;
            animal.FaceDx = dx;
            animal.FaceDy = dy;
            return true;
        }

        void Bite(Animal animal, Agent victim)
        {
            animal.AttackCooldown = animal.Def.AttackCooldownTicks;
            AddNeed(victim, Need.Hp, -animal.Def.AttackDamage);
            events.Add(new SimEvent(SimEventType.AgentHurt, victim.Index, animal.Def.Int, (int)animal.Def.AttackDamage, victim.X, victim.Y));
            SickFromBite(victim, animal.Def);
            if (victim.Needs[(int)Need.Hp] <= Config.NeedMin) Kill(victim, -1);
        }

        // ------------------------------------------------------------------ hunting

        /// <summary>R03: the first animal on the faced line within the hand item's reach; a node, building or villager blocks the line.</summary>
        Animal FindAttackTarget(Agent agent)
        {
            var tool = HandTool(agent);
            int reach = tool != null ? Math.Max(1, tool.AttackRange) : 1;
            for (int step = 1; step <= reach; step++)
            {
                int x = agent.X + agent.FaceDx * step, y = agent.Y + agent.FaceDy * step;
                if (!World.InBounds(x, y)) return null;
                int tile = World.Index(x, y);
                if (World.AnimalIndex[tile] >= 0) return animals[World.AnimalIndex[tile]];
                if (World.HasActiveNode(tile) || World.BuildingIndex[tile] >= 0 || World.AgentIndex[tile] >= 0) return null;
            }
            return null;
        }

        void Attack(Agent agent, Animal animal)
        {
            var tool = HandTool(agent);
            AddNeed(agent, Need.Energy, -harvestEnergyCost * (tool != null ? tool.EnergyCostMult : 1f) * agent.EnduranceMult * weatherWorkEnergyMult);
            AddNeed(agent, Need.Thirst, Config.ThirstWorkAction);
            SickFromWork(agent);

            animal.Hp -= tool != null && tool.AttackDamage > 0f ? tool.AttackDamage : Config.BareHandDamage;
            if (tool != null && tool.Durability > 0) WearTool(agent);
            if (animal.Hp > 0f) return;

            int tile = World.Index(animal.X, animal.Y);
            animal.Alive = false;
            World.AnimalIndex[tile] = -1;
            events.Add(new SimEvent(SimEventType.AnimalKilled, agent.Index, animal.Def.Int, animal.Index, animal.X, animal.Y));
            // R36: a Butcher gets more out of an animal.
            float share = agent.Role == butcherRole ? butcherLootMult : 1f;
            Loot(agent, tile, rawMeatItem, Share(animal.Def.LootRawMeat, share));
            Loot(agent, tile, animalHideItem, Share(animal.Def.LootAnimalHide, share));
            Loot(agent, tile, boneItem, Share(animal.Def.LootBone, share));
        }

        static int Share(int qty, float mult) => (int)Math.Ceiling(qty * mult - 1e-4);

        /// <summary>R05: into the hunter's pack; what does not fit stays on the ground.</summary>
        void Loot(Agent agent, int tile, int itemInt, int qty)
        {
            if (qty <= 0) return;
            int taken = AddItem(agent, itemInt, qty);
            if (taken > 0) events.Add(new SimEvent(SimEventType.Harvested, agent.Index, itemInt, taken, tile % World.Width, tile / World.Width));
            if (taken < qty) AddToPile(tile, new ItemStack { Item = itemInt, Qty = qty - taken });
        }
    }
}
