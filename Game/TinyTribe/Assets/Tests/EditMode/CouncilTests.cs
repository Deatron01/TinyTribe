using System;
using System.Linq;
using NUnit.Framework;
using TinyTribe.Sim.Core;

namespace TinyTribe.Tests
{
    /// <summary>The Council: votes that give villagers their roles (Sim Rules R16, R17).</summary>
    public class CouncilTests
    {
        static AgentAction Wait(Simulation sim) => new AgentAction(sim.Actions.Wait);

        static AgentAction VoteFor(Simulation sim, string roleId) => new AgentAction(sim.Actions.Vote, Kit.Data.RoleInt(roleId));

        static AgentStats WithInfluence(float influence)
        {
            var stats = Kit.Neutral;
            stats.Influence = influence;
            return stats;
        }

        /// <summary>A village of villagers with the given influence, one tick in, so the first vote is open.</summary>
        static Simulation Village(params float[] influence)
        {
            var sim = Kit.NewSim(12, 9);
            for (int i = 0; i < influence.Length; i++) sim.AddAgent(2 + i * 2, 4, WithInfluence(influence[i]));
            sim.Step(sim.Agents.Select(_ => Wait(sim)).ToArray());
            return sim;
        }

        static void Step(Simulation sim, params AgentAction[] actions)
        {
            foreach (var agent in sim.Agents) Kit.Refill(agent);
            sim.Step(actions);
        }

        static int RoleOf(Simulation sim, int agent) => sim.Agents[agent].Role;

        [Test]
        public void No_vote_is_open_before_the_first_tick()
        {
            var sim = Kit.NewSim();
            sim.AddAgent(4, 4, Kit.Neutral);

            Assert.IsNull(sim.OpenVote);
            Assert.IsFalse(sim.IsValid(0, VoteFor(sim, "builder")));
        }

        [Test]
        public void A_role_vote_opens_for_a_new_villager()
        {
            var sim = Village(0.5f);

            Assert.IsNotNull(sim.OpenVote);
            Assert.AreEqual(VoteKind.Role, sim.OpenVote.Kind);
            Assert.AreEqual(0, sim.OpenVote.Subject);
            Assert.IsTrue(sim.Events.Any(e => e.Type == SimEventType.VoteOpened && e.Agent == 0));
            Assert.IsTrue(sim.IsValid(0, VoteFor(sim, "builder")));
        }

        [Test]
        public void The_role_everyone_votes_for_is_given_at_once()
        {
            var sim = Village(0.5f, 0.5f, 0.5f);

            Step(sim, VoteFor(sim, "builder"), VoteFor(sim, "builder"), VoteFor(sim, "builder"));

            Assert.AreEqual(Kit.Data.RoleInt("builder"), RoleOf(sim, 0));
            Assert.IsTrue(sim.Events.Any(e => e.Type == SimEventType.RoleAssigned && e.Agent == 0 && e.Item == Kit.Data.RoleInt("builder")));
            Assert.AreEqual(3, sim.Events.Count(e => e.Type == SimEventType.VoteCast));
            Assert.IsTrue(sim.Events.Any(e => e.Type == SimEventType.VoteClosed && e.Agent == 0 && e.Qty == Kit.Data.RoleInt("builder")));
        }

        [Test]
        public void Votes_are_added_up_by_weight_not_counted()
        {
            // 1.9 for builder against 1.1 + 1.2 for lumberjack.
            var sim = Village(0.9f, 0.1f, 0.2f);

            Step(sim, VoteFor(sim, "builder"), VoteFor(sim, "lumberjack"), VoteFor(sim, "lumberjack"));

            Assert.AreEqual(Kit.Data.RoleInt("lumberjack"), RoleOf(sim, 0));
        }

        [Test]
        public void Influence_gives_a_vote_more_weight()
        {
            var sim = Village(0.9f, 0.1f);

            Step(sim, VoteFor(sim, "builder"), VoteFor(sim, "lumberjack"));

            Assert.AreEqual(Kit.Data.RoleInt("builder"), RoleOf(sim, 0));
        }

        [Test]
        public void A_tie_leaves_the_villager_without_a_role()
        {
            var sim = Village(0.5f, 0.5f);

            Step(sim, VoteFor(sim, "builder"), VoteFor(sim, "lumberjack"));

            Assert.AreEqual(0, RoleOf(sim, 0));
            Assert.IsTrue(sim.Events.Any(e => e.Type == SimEventType.VoteClosed && e.Agent == 0 && e.Qty == 0));
            Assert.IsFalse(sim.Events.Any(e => e.Type == SimEventType.RoleAssigned));
        }

        [Test]
        public void A_villager_votes_once_per_vote()
        {
            var sim = Village(0.5f, 0.5f);

            Step(sim, VoteFor(sim, "builder"), Wait(sim));

            Assert.IsTrue(sim.OpenVote.HasVoted(0));
            Assert.IsFalse(sim.IsValid(0, VoteFor(sim, "lumberjack")));
            Assert.IsTrue(sim.IsValid(1, VoteFor(sim, "lumberjack")));
        }

        [Test]
        public void Only_roles_can_be_voted_for()
        {
            var sim = Village(0.5f);
            int roles = Kit.Data.Roles.Count;

            Assert.IsFalse(sim.IsValid(0, new AgentAction(sim.Actions.Vote, 0)), "unassigned is not a role");
            Assert.IsFalse(sim.IsValid(0, new AgentAction(sim.Actions.Vote, roles)), "past the last role");
            var mask = new bool[Kit.Data.ConfigInt("num_action_args")];
            sim.GetArgMask(0, sim.Actions.Vote, mask);
            CollectionAssert.AreEqual(Enumerable.Range(1, roles - 1).ToArray(), Enumerable.Range(0, mask.Length).Where(i => mask[i]).ToArray());
        }

        [Test]
        public void A_vote_waits_its_full_time_for_those_who_have_not_voted()
        {
            var sim = Village(0.5f, 0.5f);
            int duration = Kit.Data.ConfigInt("vote_duration_ticks");
            long opened = sim.Tick;
            Assert.AreEqual(opened + duration, sim.OpenVote.ClosesTick);

            Step(sim, VoteFor(sim, "miner"), Wait(sim));
            while (sim.Tick < opened + duration - 1) Step(sim, Wait(sim), Wait(sim));
            Assert.AreEqual(0, RoleOf(sim, 0), "one tick to go");
            Assert.AreEqual(0, sim.OpenVote.Subject);

            Step(sim, Wait(sim), Wait(sim));

            Assert.AreEqual(Kit.Data.RoleInt("miner"), RoleOf(sim, 0), "the one vote cast decides");
        }

        [Test]
        public void A_vote_nobody_takes_part_in_changes_nothing()
        {
            var sim = Village(0.5f);
            int duration = Kit.Data.ConfigInt("vote_duration_ticks");

            for (int t = 0; t < duration; t++) Step(sim, Wait(sim));

            Assert.AreEqual(0, RoleOf(sim, 0));
            Assert.IsNull(sim.OpenVote, "the next try is at the turn of the season");
        }

        [Test]
        public void Villagers_get_their_votes_one_after_another()
        {
            var sim = Village(0.5f, 0.5f, 0.5f);

            Step(sim, VoteFor(sim, "builder"), VoteFor(sim, "builder"), VoteFor(sim, "builder"));
            Assert.AreEqual(1, sim.OpenVote.Subject, "the next vote opens as the first closes");
            Step(sim, VoteFor(sim, "miner"), VoteFor(sim, "miner"), VoteFor(sim, "miner"));
            Assert.AreEqual(2, sim.OpenVote.Subject);
            Step(sim, VoteFor(sim, "scout"), VoteFor(sim, "scout"), VoteFor(sim, "scout"));

            Assert.IsNull(sim.OpenVote);
            CollectionAssert.AreEqual(new[] { "builder", "miner", "scout" }.Select(Kit.Data.RoleInt).ToArray(), sim.Agents.Select(a => a.Role).ToArray());
        }

        [Test]
        public void A_villager_who_already_has_a_role_gets_no_vote()
        {
            var sim = Kit.NewSim(12, 9);
            var first = sim.AddAgent(2, 4, Kit.Neutral);
            sim.AddAgent(4, 4, Kit.Neutral);
            sim.SetRole(first, "cook");

            sim.Step(new[] { Wait(sim), Wait(sim) });

            Assert.AreEqual(1, sim.OpenVote.Subject);
        }

        [Test]
        public void A_vote_is_dropped_when_its_villager_gets_a_role_some_other_way()
        {
            var sim = Village(0.5f, 0.5f);
            Assert.AreEqual(0, sim.OpenVote.Subject);
            sim.SetRole(sim.Agents[0], "cook");

            Step(sim, VoteFor(sim, "builder"), VoteFor(sim, "builder"));

            Assert.AreEqual(Kit.Data.RoleInt("cook"), RoleOf(sim, 0));
            Assert.AreEqual(1, sim.OpenVote.Subject);
        }

        [Test]
        public void A_villager_still_without_a_role_gets_another_vote_each_season()
        {
            var sim = Village(0.5f);
            int duration = Kit.Data.ConfigInt("vote_duration_ticks");
            for (int t = 0; t < duration; t++) Step(sim, Wait(sim));
            Assert.IsNull(sim.OpenVote);
            string season = sim.Season.Id;

            int guard = 0;
            while (sim.Season.Id == season && guard++ < 5000)
            {
                Assert.IsNull(sim.OpenVote, "no new vote in the same season");
                Step(sim, Wait(sim));
            }
            Step(sim, Wait(sim));

            Assert.IsNotNull(sim.OpenVote);
            Assert.AreEqual(0, sim.OpenVote.Subject);
        }

        [Test]
        public void A_vote_about_someone_who_has_died_is_dropped()
        {
            var sim = Village(0.5f, 0.5f);
            var doomed = sim.Agents[0];
            doomed.Set(Need.Hunger, 0);
            doomed.Set(Need.Hp, 0.1f);

            sim.Step(new[] { Wait(sim), Wait(sim) });

            Assert.IsFalse(doomed.Alive);
            Assert.AreEqual(1, sim.OpenVote.Subject);
        }

        [Test]
        public void The_dead_do_not_hold_up_a_vote()
        {
            var sim = Village(0.5f, 0.5f, 0.5f);
            var doomed = sim.Agents[2];
            doomed.Set(Need.Hunger, 0);
            doomed.Set(Need.Hp, 0.1f);
            sim.Step(new[] { Wait(sim), Wait(sim), Wait(sim) });
            Assert.IsFalse(doomed.Alive);

            Step(sim, VoteFor(sim, "builder"), VoteFor(sim, "builder"), Wait(sim));

            Assert.AreEqual(Kit.Data.RoleInt("builder"), RoleOf(sim, 0), "both of the living have voted");
        }

        [Test]
        public void Vote_shows_in_the_action_mask_only_while_one_is_open_for_the_villager()
        {
            var sim = Kit.NewSim(12, 9);
            sim.AddAgent(2, 4, Kit.Neutral);
            sim.AddAgent(4, 4, Kit.Neutral);
            var mask = new bool[Kit.Data.ConfigInt("num_actions")];
            sim.GetActionMask(0, mask);
            Assert.IsFalse(mask[sim.Actions.Vote]);

            sim.Step(new[] { Wait(sim), Wait(sim) });
            sim.GetActionMask(0, mask);
            Assert.IsTrue(mask[sim.Actions.Vote]);

            sim.Step(new[] { VoteFor(sim, "builder"), Wait(sim) });
            sim.GetActionMask(0, mask);
            Assert.IsFalse(mask[sim.Actions.Vote], "already voted");
        }

        [Test]
        public void Same_votes_give_the_same_state()
        {
            ulong Run()
            {
                var sim = Village(0.3f, 0.6f);
                Step(sim, VoteFor(sim, "builder"), VoteFor(sim, "builder"));
                Step(sim, VoteFor(sim, "miner"), Wait(sim));
                return sim.ComputeStateHash();
            }

            Assert.AreEqual(Run(), Run());
        }

        [Test]
        public void An_open_vote_is_part_of_the_state()
        {
            var voted = Village(0.5f, 0.5f);
            var waited = Village(0.5f, 0.5f);

            Step(voted, VoteFor(voted, "builder"), Wait(voted));
            Step(waited, Wait(waited), Wait(waited));

            Assert.AreNotEqual(voted.ComputeStateHash(), waited.ComputeStateHash());
        }
    }

    /// <summary>What a role changes for the villager who holds it (Sim Roles, rule R36).</summary>
    public class RoleEffectTests
    {
        static float RoleBonus(string roleId) => Kit.Data.Roles.First(r => r.Id == roleId).BonusValue;

        static (Simulation sim, Agent agent) Facing(string nodeId, string roleId, string tool = null)
        {
            var sim = Kit.NewSim();
            sim.World.PlaceNode(5, 4, Kit.Data.NodeById(nodeId));
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            Kit.Face(agent, 1, 0);
            if (roleId != null) sim.SetRole(agent, roleId);
            if (tool != null)
            {
                sim.AddItem(agent, Kit.Item(tool), 1);
                sim.Step(new[] { new AgentAction(sim.Actions.Equip, Kit.Item(tool)) });
            }
            return (sim, agent);
        }

        static float EnergySpentOnOneHarvest(Simulation sim, Agent agent)
        {
            Kit.Refill(agent);
            float before = agent.Get(Need.Energy);
            sim.Step(new[] { new AgentAction(sim.Actions.Harvest) });
            Assert.IsTrue(agent.LastActionValid);
            return before - agent.Get(Need.Energy) - Kit.Data.NeedById("energy").BaseDrainPerTick;
        }

        [Test]
        public void A_lumberjack_tires_less_from_felling_trees()
        {
            var (plainSim, plain) = Facing("pine_tree", null);
            var (sim, lumberjack) = Facing("pine_tree", "lumberjack");

            float usual = EnergySpentOnOneHarvest(plainSim, plain);
            float spent = EnergySpentOnOneHarvest(sim, lumberjack);

            Assert.Less(RoleBonus("lumberjack"), 1f);
            Assert.AreEqual(usual * RoleBonus("lumberjack"), spent, 1e-4f);
        }

        [Test]
        public void A_lumberjack_gathers_everything_else_like_anyone()
        {
            var (plainSim, plain) = Facing("berry_bush", null);
            var (sim, lumberjack) = Facing("berry_bush", "lumberjack");

            Assert.AreEqual(EnergySpentOnOneHarvest(plainSim, plain), EnergySpentOnOneHarvest(sim, lumberjack), 1e-4f);
        }

        [Test]
        public void A_miner_works_ore_faster_and_everyone_else_slower()
        {
            var vein = Kit.Data.NodeById("copper_vein");
            float pick = Kit.Data.EquipmentByItemInt(Kit.Item("stone_pickaxe")).SpeedMult;
            float others = Kit.Data.ConfigFloat("non_miner_ore_speed_mult");
            var (minerSim, miner) = Facing("copper_vein", "miner", "stone_pickaxe");
            var (cookSim, cook) = Facing("copper_vein", "cook", "stone_pickaxe");
            var (plainSim, plain) = Facing("copper_vein", null, "stone_pickaxe");

            Assert.AreEqual(Kit.ExpectedActions(vein.HarvestTicks, pick * RoleBonus("miner")), Kit.HarvestUntilYield(minerSim, miner, Kit.Item("copper_ore")));
            Assert.AreEqual(Kit.ExpectedActions(vein.HarvestTicks, pick * others), Kit.HarvestUntilYield(cookSim, cook, Kit.Item("copper_ore")));
            Assert.AreEqual(Kit.ExpectedActions(vein.HarvestTicks, pick * others), Kit.HarvestUntilYield(plainSim, plain, Kit.Item("copper_ore")), "no role is not a miner either");
        }

        [Test]
        public void Plain_rock_is_not_ore()
        {
            var rock = Kit.Data.NodeById("surface_rock");
            float pick = Kit.Data.EquipmentByItemInt(Kit.Item("stone_pickaxe")).SpeedMult;
            var (minerSim, miner) = Facing("surface_rock", "miner", "stone_pickaxe");
            var (cookSim, cook) = Facing("surface_rock", "cook", "stone_pickaxe");

            int expected = Kit.ExpectedActions(rock.HarvestTicks, pick);
            Assert.AreEqual(expected, Kit.HarvestUntilYield(minerSim, miner, Kit.Item("cobblestone")));
            Assert.AreEqual(expected, Kit.HarvestUntilYield(cookSim, cook, Kit.Item("cobblestone")));
        }

        [TestCase("butcher")]
        [TestCase(null)]
        public void A_butcher_gets_more_from_an_animal(string roleId)
        {
            var sim = Kit.NewSim(12, 9);
            var hunter = sim.AddAgent(4, 4, Kit.Neutral.WithStrength(10));
            Kit.Face(hunter, 1, 0);
            if (roleId != null) sim.SetRole(hunter, roleId);
            var deer = Kit.Data.AnimalById("deer");
            // Boxed in, so it cannot run.
            var rock = Kit.Data.NodeById("surface_rock");
            foreach (var (x, y) in new[] { (6, 4), (5, 5), (5, 3) }) sim.World.PlaceNode(x, y, rock);
            var animal = sim.SpawnAnimal("deer", 5, 4);

            for (int hit = 0; hit < 200 && animal.Alive; hit++)
            {
                Kit.Refill(hunter);
                sim.Step(new[] { new AgentAction(sim.Actions.Harvest) });
            }

            Assert.IsFalse(animal.Alive);
            float more = roleId == null ? 1f : RoleBonus("butcher");
            Assert.AreEqual((int)Math.Ceiling(deer.LootRawMeat * more - 1e-4), hunter.Count(Kit.Item("raw_meat")));
            Assert.AreEqual((int)Math.Ceiling(deer.LootAnimalHide * more - 1e-4), hunter.Count(Kit.Item("animal_hide")));
            Assert.AreEqual((int)Math.Ceiling(deer.LootBone * more - 1e-4), hunter.Count(Kit.Item("bone")));
        }

        [Test]
        public void A_scout_far_from_the_village_gets_hungry_and_thirsty_more_slowly()
        {
            var sim = Kit.NewSim(70, 9);
            int radius = Kit.Data.ConfigInt("village_radius");
            var away = sim.AddAgent(sim.World.SpawnX + radius + 1, 4, Kit.Neutral);
            var home = sim.AddAgent(sim.World.SpawnX + radius, 4, Kit.Neutral);
            var plain = sim.AddAgent(sim.World.SpawnX + radius + 1, 6, Kit.Neutral);
            sim.SetRole(away, "scout");
            sim.SetRole(home, "scout");
            sim.SetRole(plain, "cook");
            var wait = new AgentAction(sim.Actions.Wait);
            float hunger = Kit.Data.NeedById("hunger").BaseDrainPerTick, thirst = Kit.Data.NeedById("thirst").BaseDrainPerTick;
            float hungerBefore = away.Get(Need.Hunger), thirstBefore = away.Get(Need.Thirst);

            sim.Step(new[] { wait, wait, wait });

            Assert.AreEqual(hungerBefore - hunger * Kit.Data.Modifier("hunger_scout_traveling"), away.Get(Need.Hunger), 1e-4f);
            Assert.AreEqual(thirstBefore - thirst * Kit.Data.Modifier("thirst_scout_traveling"), away.Get(Need.Thirst), 1e-4f);
            Assert.AreEqual(hungerBefore - hunger, home.Get(Need.Hunger), 1e-4f, "inside the village radius");
            Assert.AreEqual(hungerBefore - hunger, plain.Get(Need.Hunger), 1e-4f, "not a scout");
        }
    }
}
