using System;
using System.Collections.Generic;
using TinyTribe.Sim.Core;

namespace TinyTribe.Brains
{
    /// <summary>Where a villager is going and how it gets there.</summary>
    public sealed partial class SurvivalBrain
    {
        const int SearchRadius = 48;
        const int RetryAfterTicks = 20;
        const int PatienceTicks = 6;

        /// <summary>
        /// Drink to Fire end beside the target (a node or a building) and face it.
        /// WarmSpot, Centre, Home and Spot end standing on the target tile.
        /// </summary>
        enum Goal { None, Drink, Forage, FetchWater, Wood, Stone, Fish, Cotton, Oak, Reap, Site, Chest, Fire, StudyPlace, Mill, House, WarmSpot, Centre, Home, Spot }

        const int GoalCount = 20;

        sealed class Plan
        {
            public Goal Goal;
            public int TargetTile = -1;
            public bool StandOn;
            public readonly List<int> Path = new List<int>();
            public int PathPos;
            public int Stuck;
            public readonly long[] RetryAt = new long[GoalCount];

            /// <summary>On the way to the fire, or sitting by it, until warm again.</summary>
            public bool Warming;

            /// <summary>Worn out by day and sleeping it off.</summary>
            public bool Napping;

            /// <summary>The one tile the Spot goal leads to.</summary>
            public int SpotTile = -1;

            /// <summary>Out of water, or low on food, and filling up again: a trip is made for a full load, not for one piece.</summary>
            public bool Watering, Foraging;

            /// <summary>Tick at which this brain last chose for the villager.</summary>
            public long AskedAt = -1;

            /// <summary>Tick at which the villager last greeted whoever was near.</summary>
            public long GreetedAt = long.MinValue / 4;

            /// <summary>What it chose then; a memoryless bot checks whether the villager did it.</summary>
            public AgentAction Advised;
            public long AdvisedAt = -1;

            public void Clear()
            {
                Goal = Goal.None;
                TargetTile = -1;
                StandOn = false;
                Path.Clear();
                PathPos = 0;
                Stuck = 0;
            }
        }

        // Flood-fill scratch space, reused between searches.
        int[] cameFrom = new int[0];
        int[] steps = new int[0];
        int[] visitedStamp = new int[0];
        int[] queue = new int[0];
        int stamp;

        static bool StandsOn(Goal goal) => goal >= Goal.WarmSpot;

        /// <summary>The village is looked for across the whole map; things to gather only nearby.</summary>
        static bool LeadsHome(Goal goal) => goal >= Goal.Site;

        /// <summary>Is this the node or building the goal wants the villager to stand next to?</summary>
        bool IsTarget(Simulation sim, Agent agent, Goal goal, int tile)
        {
            var world = sim.World;
            switch (goal)
            {
                case Goal.Site: return site != null && world.BuildingIndex[tile] == site.Index;
                case Goal.Chest: return chest != null && chest.Complete && world.BuildingIndex[tile] == chest.Index;
                case Goal.Fire: return fire != null && fire.Complete && world.BuildingIndex[tile] == fire.Index;
                case Goal.StudyPlace: return studyPlace != null && world.BuildingIndex[tile] == studyPlace.Index;
                case Goal.Mill: return mill != null && mill.Complete && world.BuildingIndex[tile] == mill.Index;
                case Goal.House:
                    var house = HouseFor(sim, agent);
                    return house != null && world.BuildingIndex[tile] == house.Index;
            }
            if (!world.HasActiveNode(tile) || dangerAt[tile] == dangerStamp) return false;
            int node = world.Node[tile];
            int x = tile % world.Width, y = tile / world.Width;
            switch (goal)
            {
                case Goal.Drink: return node == waterNode;
                case Goal.FetchWater: return node == waterNode && sim.CouldHarvest(agent, x, y);
            }
            // Whoever tends the fire does not gather far from it.
            if (agent.Index == builder && fire != null && Chebyshev(x - world.SpawnX, y - world.SpawnY) > BuilderRange) return false;
            switch (goal)
            {
                case Goal.Forage: return nodeIsFood[node] && MayPick(agent, node) && sim.CouldHarvest(agent, x, y);
                case Goal.Wood: return nodeIsWood[node] && sim.CouldHarvest(agent, x, y);
                case Goal.Stone: return nodeIsStone[node] && sim.CouldHarvest(agent, x, y);
                // With a rod in hand, water gives fish.
                case Goal.Fish: return node == waterNode && sim.CouldHarvest(agent, x, y);
                case Goal.Cotton: return nodeIsCotton[node] && sim.CouldHarvest(agent, x, y);
                case Goal.Oak: return nodeIsOak[node] && sim.CouldHarvest(agent, x, y);
                case Goal.Reap: return node == ripeCropNode && sim.CouldHarvest(agent, x, y);
                default: return false;
            }
        }

        /// <summary>Is this a free tile the goal wants the villager to stand on?</summary>
        bool IsStandTarget(Simulation sim, Agent agent, Goal goal, int tile)
        {
            var world = sim.World;
            int occupant = world.AgentIndex[tile];
            if (occupant >= 0 && occupant != agent.Index) return false;
            int x = tile % world.Width, y = tile / world.Width;
            switch (goal)
            {
                case Goal.WarmSpot: return NearFire(x, y);
                case Goal.Centre: return Chebyshev(x - world.SpawnX, y - world.SpawnY) <= centreRadius;
                case Goal.Home: return Chebyshev(x - world.SpawnX, y - world.SpawnY) <= HomeRadius;
                case Goal.Spot: return tile == plans[agent.Index].SpotTile;
                default: return false;
            }
        }

        /// <summary>
        /// Works toward the goal: plans a route if needed, then walks and turns, and answers with
        /// onArrival once the villager is in place. Returns false when nothing suitable is within reach.
        /// </summary>
        bool Go(Simulation sim, Agent agent, Plan plan, Goal goal, AgentAction onArrival, out AgentAction action)
        {
            action = default;
            bool keep = plan.Goal == goal && plan.TargetTile >= 0
                && (plan.StandOn ? IsStandTarget(sim, agent, goal, plan.TargetTile) : IsTarget(sim, agent, goal, plan.TargetTile));
            if (!keep)
            {
                if (sim.Tick < plan.RetryAt[(int)goal]) return false;
                if (!FindRoute(sim, agent, plan, goal))
                {
                    GiveUp(sim, plan, goal);
                    return false;
                }
            }
            action = Follow(sim, agent, plan, onArrival);
            return true;
        }

        void GiveUp(Simulation sim, Plan plan, Goal goal)
        {
            plan.Clear();
            plan.RetryAt[(int)goal] = sim.Tick + RetryAfterTicks;
        }

        AgentAction Follow(Simulation sim, Agent agent, Plan plan, AgentAction onArrival)
        {
            var world = sim.World;
            var wait = new AgentAction(sim.Actions.Wait);
            int here = world.Index(agent.X, agent.Y);
            while (plan.PathPos < plan.Path.Count && plan.Path[plan.PathPos] == here) plan.PathPos++;

            if (plan.PathPos >= plan.Path.Count)
            {
                if (plan.StandOn)
                {
                    if (here != plan.TargetTile)
                    {
                        plan.Clear();
                        return wait;
                    }
                }
                else
                {
                    // At the end of the route: the target should be the next tile over.
                    int dx = plan.TargetTile % world.Width - agent.X, dy = plan.TargetTile / world.Width - agent.Y;
                    if (Math.Abs(dx) + Math.Abs(dy) != 1)
                    {
                        plan.Clear();
                        return wait;
                    }
                    // Moving into a blocked tile only turns the villager to face it.
                    if (agent.FaceDx != dx || agent.FaceDy != dy) return MoveToward(sim, dx, dy);
                }

                if (sim.IsValid(agent.Index, onArrival))
                {
                    plan.Stuck = 0;
                    return onArrival;
                }
                // Not possible right now (someone got there first). Wait a little, then think again.
                if (++plan.Stuck > PatienceTicks) GiveUp(sim, plan, plan.Goal);
                return wait;
            }

            int next = plan.Path[plan.PathPos];
            int stepX = next % world.Width - agent.X, stepY = next / world.Width - agent.Y;
            if (Math.Abs(stepX) + Math.Abs(stepY) != 1)
            {
                plan.Clear();
                return wait;
            }
            if (!sim.CanEnter(agent.X + stepX, agent.Y + stepY))
            {
                // Someone is in the way. Give them a moment, then step aside and plan again.
                if (++plan.Stuck <= 2) return wait;
                plan.Clear();
                return RandomMove(sim, agent);
            }
            plan.Stuck = 0;
            return MoveToward(sim, stepX, stepY);
        }

        /// <summary>Which needle of a villager's compass shows the kind of thing the goal is after; -1 when none does.</summary>
        static int CompassKind(Goal goal)
        {
            switch (goal)
            {
                case Goal.Drink:
                case Goal.FetchWater:
                case Goal.Fish: return Simulation.CompassWater;
                case Goal.Forage: return Simulation.CompassFood;
                case Goal.Wood: return Simulation.CompassWood;
                case Goal.Stone: return Simulation.CompassStone;
                case Goal.Cotton: return Simulation.CompassCotton;
                case Goal.Oak: return Simulation.CompassOak;
                default: return -1;
            }
        }

        /// <summary>
        /// The way to the goal. A bot goes where a villager's own compass points (Simulation.CompassTarget),
        /// so that what it does can be learned by someone who sees only what a villager sees. Only when
        /// the bot will not have that one (a bear is near it, its pack has no room for it, it is too far
        /// from the fire it tends) does it look for another by itself.
        /// </summary>
        bool FindRoute(Simulation sim, Agent agent, Plan plan, Goal goal)
        {
            int kind = CompassKind(goal);
            if (kind >= 0 && sim.CompassTarget(agent.Index, kind, out int known) && IsTarget(sim, agent, goal, known)
                && FindRoute(sim, agent, plan, goal, known)) return true;
            return FindRoute(sim, agent, plan, goal, -1);
        }

        /// <summary>
        /// Flood fill outward from the villager to the nearest target: a tile beside a wanted node or
        /// building, or a wanted tile to stand on. Nodes, buildings and a bear's surroundings are walked around.
        /// With only set, that tile is the one target.
        /// </summary>
        bool FindRoute(Simulation sim, Agent agent, Plan plan, Goal goal, int only)
        {
            var world = sim.World;
            int width = world.Width;
            int start = world.Index(agent.X, agent.Y);
            bool standOn = StandsOn(goal);
            int radius = LeadsHome(goal) ? int.MaxValue : SearchRadius;
            stamp++;
            int head = 0, tail = 0;
            queue[tail++] = start;
            visitedStamp[start] = stamp;
            cameFrom[start] = -1;
            steps[start] = 0;

            while (head < tail)
            {
                int tile = queue[head++];
                if (standOn && tile != start && IsStandTarget(sim, agent, goal, tile))
                {
                    SetRoute(plan, goal, tile, tile, start, standOn: true);
                    return true;
                }

                int x = tile % width, y = tile / width;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + StepX[d], ny = y + StepY[d];
                    if (!world.InBounds(nx, ny)) continue;
                    int next = world.Index(nx, ny);

                    if (!standOn && (only >= 0 ? next == only : IsTarget(sim, agent, goal, next)))
                    {
                        SetRoute(plan, goal, next, tile, start, standOn: false);
                        return true;
                    }
                    if (visitedStamp[next] == stamp || !terrainWalkable[world.Terrain[next]]) continue;
                    if (world.HasActiveNode(next) || world.BuildingIndex[next] >= 0 || dangerAt[next] == dangerStamp) continue;
                    if (steps[tile] + 1 > radius) continue;
                    visitedStamp[next] = stamp;
                    cameFrom[next] = tile;
                    steps[next] = steps[tile] + 1;
                    queue[tail++] = next;
                }
            }
            return false;
        }

        void SetRoute(Plan plan, Goal goal, int target, int lastTile, int start, bool standOn)
        {
            plan.Clear();
            plan.Goal = goal;
            plan.TargetTile = target;
            plan.StandOn = standOn;
            for (int t = lastTile; t != start; t = cameFrom[t]) plan.Path.Add(t);
            plan.Path.Reverse();
        }
    }
}
