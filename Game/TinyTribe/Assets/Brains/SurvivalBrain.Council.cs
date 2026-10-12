using System;
using System.Collections.Generic;
using TinyTribe.Sim.Core;

namespace TinyTribe.Brains
{
    /// <summary>How the bots vote, and what a role changes about a bot's day.</summary>
    public sealed partial class SurvivalBrain
    {
        // Bot habits, not game balance: how much the village wants each job. Every job there is work for
        // is filled once before any is filled twice. A farmer is wanted only when there are plots to sow,
        // a scholar only when there is something to study and somewhere to study it.
        static readonly Dictionary<string, float> RoleWants = new Dictionary<string, float>
        {
            ["builder"] = 3f, ["lumberjack"] = 2f, ["butcher"] = 1.6f, ["miner"] = 1.4f, ["farmer"] = 1.2f, ["scholar"] = 1f,
            ["scout"] = 0.8f, ["cook"] = 0.6f, ["tailor"] = 0.5f, ["doctor"] = 0.5f, ["blacksmith"] = 0.4f, ["guard"] = 0.3f,
        };

        // Bot habit: who fishes. The game has no fisher's job, and these have little else to do before
        // there are farms, kitchens and ore worth mining.
        static readonly string[] FishingRoles = { "butcher", "cook", "farmer", "miner" };
        const int CottonReserve = 3;

        // Whoever has a job is that much better at it than their stats say: they have the tools and the
        // work in hand. It keeps jobs from changing hands between villagers who are as good as each other.
        const float KnowsTheJob = 0.05f;
        const float CannotDoIt = -100f;
        const int WoodStockTarget = 30;

        int builderRole, lumberjackRole, scholarRole;
        int[] rolesByWant = new int[0];
        int[] plannedJob = new int[0];
        long plannedAt = -1;
        bool somethingToStudy;
        readonly List<int> staff = new List<int>();
        readonly List<int> postRole = new List<int>(), postHolder = new List<int>();
        readonly List<float> postWeight = new List<float>();
        int axeItem, axeArg, axeWood;
        int researchArg = -1;
        float[] roleWant = new float[0];
        bool[] roleFishes = new bool[0];
        string[] roleStat1 = new string[0], roleStat2 = new string[0];

        void BindRoles(Simulation sim)
        {
            int count = 0;
            foreach (var role in sim.Data.Roles) count = Math.Max(count, role.Int + 1);
            roleWant = new float[count];
            roleStat1 = new string[count];
            roleStat2 = new string[count];
            roleFishes = new bool[count];
            foreach (var role in sim.Data.Roles)
            {
                roleFishes[role.Int] = Array.IndexOf(FishingRoles, role.Id) >= 0;
                RoleWants.TryGetValue(role.Id, out roleWant[role.Int]);
                roleStat1[role.Int] = role.Stat1;
                roleStat2[role.Int] = role.Stat2;
            }
            builderRole = sim.Data.RoleInt("builder");
            lumberjackRole = sim.Data.RoleInt("lumberjack");
            scholarRole = sim.Data.RoleInt("scholar");
            // Most wanted first; the roles table breaks ties.
            var wanted = new List<int>();
            for (int role = 1; role < count; role++)
                if (roleWant[role] > 0f) wanted.Add(role);
            wanted.Sort((a, b) => roleWant[a] != roleWant[b] ? roleWant[b].CompareTo(roleWant[a]) : a.CompareTo(b));
            rolesByWant = wanted.ToArray();
            plannedAt = -1;
            axeItem = sim.Data.ItemById["wooden_axe"].Int;
            axeArg = sim.Data.ArgFor("wooden_axe");
            foreach (var recipe in sim.Data.Recipes)
                if (recipe.OutputItem == "wooden_axe") axeWood = WoodIn(recipe);
            foreach (var arg in sim.Data.ActionArgs)
                if (arg.Kind == "research") researchArg = arg.Int;
        }

        /// <summary>Votes as soon as a vote is open. Every bot works out the same answer, so the bots agree.</summary>
        bool CastVote(Simulation sim, Agent agent, out AgentAction action)
        {
            action = default;
            var vote = sim.OpenVote;
            if (vote == null || vote.HasVoted(agent.Index)) return false;
            if (vote.Kind == VoteKind.Research)
            {
                // The first technology on the list that can be studied here: carpentry, then agriculture, and with a Library the rest.
                for (int option = 1; option < sim.Data.Tech.Count; option++)
                {
                    action = new AgentAction(sim.Actions.Vote, option);
                    if (sim.IsValid(agent.Index, action)) return true;
                }
                return false;
            }
            // A first vote about someone who has a job by now is dropped by the Council; a review is not.
            if (vote.Kind != VoteKind.Role || (!vote.Review && sim.Agents[vote.Subject].Role != 0)) return false;
            int role = RoleFor(sim, vote.Subject);
            if (role == 0) return false;
            action = new AgentAction(sim.Actions.Vote, role);
            return sim.IsValid(agent.Index, action);
        }

        /// <summary>
        /// The Scholar studies whatever the Council chose, wherever it can be studied. A village without a
        /// Scholar would never get its mill, so there the builder studies when it has nothing to build.
        /// </summary>
        bool Study(Simulation sim, Agent agent, Plan plan, out AgentAction action)
        {
            action = default;
            if (studyPlace == null || researchArg < 0) return false;
            if (agent.Role != scholarRole && (hasScholar || agent.Index != builder)) return false;
            return Go(sim, agent, plan, Goal.StudyPlace, new AgentAction(sim.Actions.CraftBuild, researchArg), out action);
        }

        /// <summary>
        /// The job the village's plan has for the villager. Every bot works the same plan out from the
        /// same village, so the bots vote as one, whoever is being voted on.
        /// </summary>
        int RoleFor(Simulation sim, int subject)
        {
            PlanJobs(sim);
            return (uint)subject < (uint)plannedJob.Length ? plannedJob[subject] : 0;
        }

        /// <summary>Is there work for the job right now?</summary>
        bool HasWork(int role) =>
            role == farmerRole ? farms.Count > 0 : role != scholarRole || somethingToStudy;

        /// <summary>
        /// Shares the jobs out among everyone alive, whatever they do now. One post for each job there is
        /// work for, most wanted first, and the same again while there are villagers left over. Each post
        /// goes to the villager best at it; then any two swap whenever the village is better off for it,
        /// so the one clever villager ends up the scholar even if it was first in line to be the builder.
        /// </summary>
        void PlanJobs(Simulation sim)
        {
            if (plannedAt == sim.Tick) return;
            plannedAt = sim.Tick;

            staff.Clear();
            foreach (var agent in sim.Agents)
                if (agent.Alive) staff.Add(agent.Index);
            if (plannedJob.Length < sim.Agents.Count) plannedJob = new int[sim.Agents.Count];
            Array.Clear(plannedJob, 0, plannedJob.Length);

            somethingToStudy = false;
            bool anyPlace = false, hasLibrary = false;
            foreach (var building in sim.Buildings)
            {
                if (building.Destroyed || !building.Complete) continue;
                hasLibrary |= building.Def.EffectKey == LibraryEffect;
                anyPlace |= building.Def.EffectKey == LibraryEffect || building.Def == circleDef;
            }
            if (anyPlace)
                foreach (var tech in sim.Data.Tech)
                    somethingToStudy |= !sim.IsResearched(tech.Id) && sim.IsResearched(tech.Prerequisite) && (hasLibrary || tech.NeedsLibrary == 0);

            postRole.Clear();
            postWeight.Clear();
            postHolder.Clear();
            for (int round = 0; postRole.Count < staff.Count && round <= staff.Count; round++)
                foreach (int role in rolesByWant)
                {
                    if (postRole.Count == staff.Count) break;
                    // The bots build with one builder; a second would stand about.
                    if (!HasWork(role) || (round > 0 && role == builderRole)) continue;
                    postRole.Add(role);
                    postWeight.Add(roleWant[role] / (1 + round));
                    postHolder.Add(-1);
                }

            // Each post to the best villager still free.
            for (int post = 0; post < postRole.Count; post++)
            {
                int best = -1;
                float bestSuit = float.NegativeInfinity;
                foreach (int index in staff)
                {
                    if (plannedJob[index] != 0) continue;
                    float suit = Suits(sim.Agents[index], postRole[post]);
                    if (suit <= bestSuit) continue;
                    bestSuit = suit;
                    best = index;
                }
                if (best < 0) break;
                postHolder[post] = best;
                plannedJob[best] = postRole[post];
            }

            // Two villagers swap posts when that makes the village better off; until nobody would.
            bool swapped = true;
            for (int pass = 0; swapped && pass < 4 * postRole.Count; pass++)
            {
                swapped = false;
                for (int a = 0; a < postRole.Count; a++)
                    for (int b = a + 1; b < postRole.Count; b++)
                    {
                        if (postHolder[a] < 0 || postHolder[b] < 0 || postRole[a] == postRole[b]) continue;
                        Agent first = sim.Agents[postHolder[a]], second = sim.Agents[postHolder[b]];
                        float now = postWeight[a] * Suits(first, postRole[a]) + postWeight[b] * Suits(second, postRole[b]);
                        float then = postWeight[a] * Suits(second, postRole[a]) + postWeight[b] * Suits(first, postRole[b]);
                        if (then <= now + 1e-4f) continue;
                        postHolder[a] = second.Index;
                        postHolder[b] = first.Index;
                        plannedJob[second.Index] = postRole[a];
                        plannedJob[first.Index] = postRole[b];
                        swapped = true;
                    }
            }
        }

        /// <summary>How good the villager is at the job, by the stats the job works with: 0 to 1, and a little more for having it already.</summary>
        float Suits(Agent agent, int role)
        {
            // The builder carries the two materials of the campfire at once.
            if (role == builderRole && agent.InventoryCapacity < MinSlotsForFire) return CannotDoIt;
            float fit = roleStat2[role] == null ? Stat(agent, roleStat1[role]) : (Stat(agent, roleStat1[role]) + Stat(agent, roleStat2[role])) * 0.5f;
            return agent.Role == role ? fit + KnowsTheJob : fit;
        }

        /// <summary>A stat by its name in Sim Roles, scaled to 0-1.</summary>
        float Stat(Agent agent, string name)
        {
            float max = boundSim.Config.CoreStatMax;
            switch (name)
            {
                case "str": return agent.Stats.Str / max;
                case "dex": return agent.Stats.Dex / max;
                case "int": return agent.Stats.Int / max;
                case "end": return agent.Stats.End / max;
                case "influence": return agent.Stats.Influence;
                default: return 0.5f;
            }
        }

        bool Fishes(Agent agent) => (uint)agent.Role < (uint)roleFishes.Length && roleFishes[agent.Role];

        /// <summary>
        /// Fish are there all year, winter included. A fishing villager makes itself a rod from wood and
        /// cotton, fishes the nearest water, and brings full stacks of cooked fish to the chest.
        /// </summary>
        bool Fish(Simulation sim, Agent agent, Plan plan, out AgentAction action)
        {
            action = default;
            if (!Fishes(agent)) return false;
            var actions = sim.Actions;
            var harvest = new AgentAction(actions.Harvest);

            bool chestReady = chest != null && chest.Complete;
            if (!Owns(agent, rodItem))
            {
                var craft = new AgentAction(actions.CraftBuild, rodArg);
                if (sim.IsValid(agent.Index, craft))
                {
                    action = craft;
                    return true;
                }
                // A small pack has no room for the makings: someone who works at the water needs no waterskin.
                if (agent.FreeSlots == 0 && agent.Count(waterItem) > 0)
                {
                    action = new AgentAction(actions.Consume, waterItem);
                    return true;
                }
                if (agent.Count(cottonItem) < rodCotton)
                {
                    // From the chest first: in winter that is the only cotton there is.
                    if (Stored(cottonItem) > 0 && Slots.Room(agent.Inventory, agent.InventoryCapacity, cottonItem, itemMaxStack[cottonItem]) > 0
                        && Go(sim, agent, plan, Goal.Chest, new AgentAction(actions.Take, cottonItem), out action)) return true;
                    return Go(sim, agent, plan, Goal.Cotton, harvest, out action);
                }
                if (agent.Count(woodItem) < rodWood) return GetWood(sim, agent, plan, out action);
                return false;
            }
            // HoldRightTool takes the rod in hand on the next pass.
            if (agent.Equipped[(int)EquipSlot.Hand].Item != rodItem) return false;
            // Raw fish is no use without a fire to cook it on.
            if (fire == null || !fire.Complete) return false;

            if (chestReady && FoodToStore(agent, onlyFull: true, out int full))
                return Go(sim, agent, plan, Goal.Chest, new AgentAction(actions.Deposit, full), out action);
            // A rod lasts fifty fish and cotton does not grow in winter: some is put by while it does.
            if (chestReady && Stored(cottonItem) + agent.Count(cottonItem) < CottonReserve
                && Go(sim, agent, plan, Goal.Cotton, harvest, out action)) return true;
            // A full pack and a waterskin: the lake is right there, so the water can go.
            if (agent.Count(waterItem) > 0 && Slots.Room(agent.Inventory, agent.InventoryCapacity, rawFishItem, itemMaxStack[rawFishItem]) == 0)
            {
                action = new AgentAction(actions.Consume, waterItem);
                return true;
            }
            return Go(sim, agent, plan, Goal.Fish, harvest, out action);
        }

        /// <summary>An axe fells a tree in half the strokes of bare hands. The Lumberjack makes one, and another when it breaks.</summary>
        bool MakeAxe(Simulation sim, Agent agent, Plan plan, out AgentAction action)
        {
            action = default;
            if (agent.Role != lumberjackRole || Owns(agent, axeItem)) return false;
            var craft = new AgentAction(sim.Actions.CraftBuild, axeArg);
            if (sim.IsValid(agent.Index, craft))
            {
                action = craft;
                return true;
            }
            if (agent.Count(woodItem) >= axeWood) return false;
            return Go(sim, agent, plan, Goal.Wood, new AgentAction(sim.Actions.Harvest), out action);
        }

        /// <summary>The Lumberjack keeps the chest in wood, so whoever tends the fire finds some at hand.</summary>
        bool StockWood(Simulation sim, Agent agent, Plan plan, out AgentAction action)
        {
            action = default;
            if (agent.Role != lumberjackRole || chest == null || !chest.Complete) return false;
            // Nowhere to put it: wood carried around is only in the way.
            if (!ChestTakes(woodItem) || Slots.Count(chest.Storage, chest.Storage.Length, woodItem) >= WoodStockTarget) return false;
            var actions = sim.Actions;
            if (agent.Count(woodItem) >= itemMaxStack[woodItem] && ChestTakes(woodItem))
                return Go(sim, agent, plan, Goal.Chest, new AgentAction(actions.Deposit, woodItem), out action);
            return Go(sim, agent, plan, Goal.Wood, new AgentAction(actions.Harvest), out action);
        }
    }
}
