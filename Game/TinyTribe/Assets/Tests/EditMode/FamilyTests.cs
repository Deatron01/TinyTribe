using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TinyTribe.Sim.Core;

namespace TinyTribe.Tests
{
    /// <summary>Age, emotes, gifts, friendship, marriage, children and grief (Simulation.Family.cs).</summary>
    public class FamilyTests
    {
        static int TicksPerDay => Kit.Data.ConfigInt("ticks_per_day");

        /// <summary>Two villagers side by side at (4,4) and (4,5), both facing east.</summary>
        static (Simulation sim, Agent a, Agent b) Two(int width = 14, int height = 10)
        {
            var sim = Kit.NewSim(width, height);
            var a = sim.AddAgent(4, 4, Kit.Neutral);
            var b = sim.AddAgent(4, 5, Kit.Neutral);
            Kit.Face(a, 1, 0);
            Kit.Face(b, 1, 0);
            return (sim, a, b);
        }

        /// <summary>Everyone does the same thing for so many ticks, kept fed and rested. Returns every event of those ticks.</summary>
        static List<SimEvent> All(Simulation sim, AgentAction action, long ticks, Func<bool> until = null)
        {
            var seen = new List<SimEvent>();
            for (long t = 0; t < ticks; t++)
            {
                foreach (var agent in sim.Agents) Kit.Refill(agent);
                sim.Step(Enumerable.Repeat(action, sim.Agents.Count).ToArray());
                seen.AddRange(sim.Events);
                if (until != null && until()) break;
            }
            return seen;
        }

        static AgentAction Wait(Simulation sim) => new AgentAction(sim.Actions.Wait);

        static float[] Vector(Simulation sim, Agent agent, out byte[] grid)
        {
            grid = new byte[sim.Observation.GridLength];
            var vector = new float[sim.Observation.VectorLength];
            sim.Observe(agent.Index, grid, vector);
            return vector;
        }

        // ------------------------------------------------------------------ age

        [Test]
        public void Those_a_game_starts_with_are_grown()
        {
            var sim = Simulation.CreateDefault(Kit.Data, 7);
            int youngest = Kit.Data.ConfigInt("founder_age_min"), oldest = Kit.Data.ConfigInt("founder_age_max");

            foreach (var agent in sim.Agents)
            {
                Assert.That(sim.AgeOf(agent), Is.InRange(youngest, oldest));
                Assert.IsFalse(agent.Child);
                Assert.IsFalse(agent.Elder);
                Assert.AreEqual(-1, agent.Spouse);
            }
        }

        [Test]
        public void Age_is_counted_in_years_of_a_villagers_own_life_and_seen()
        {
            var (sim, agent) = Village.One();
            sim.SetAge(agent, 20);
            int year = Kit.Data.ConfigInt("ticks_per_age_year");

            All(sim, Wait(sim), year - 1);
            Assert.AreEqual(20, sim.AgeOf(agent));
            All(sim, Wait(sim), 1);
            Assert.AreEqual(21, sim.AgeOf(agent));

            var v = Vector(sim, agent, out _);
            Assert.AreEqual(21f / Kit.Data.ConfigInt("old_age_death_from"), v[sim.Observation.Fields["age"].Start], 1e-6f);
        }

        [Test]
        public void A_child_cannot_fell_quarry_fight_or_vote_but_can_pick_berries()
        {
            var (sim, child) = Village.One();
            sim.SetAge(child, 5);
            Assert.IsTrue(child.Child);
            var harvest = new AgentAction(sim.Actions.Harvest);

            sim.World.PlaceNode(5, 4, Kit.Data.NodeById("pine_tree"));
            Assert.IsFalse(sim.IsValid(child.Index, harvest), "a tree");
            sim.World.PlaceNode(5, 4, Kit.Data.NodeById("berry_bush"));
            Assert.IsTrue(sim.IsValid(child.Index, harvest), "a bush");
            sim.World.RemoveNode(sim.World.Index(5, 4));

            sim.SetAge(child, Kit.Data.ConfigInt("child_until_age"));
            Assert.IsFalse(child.Child);
            sim.World.PlaceNode(5, 4, Kit.Data.NodeById("pine_tree"));
            Assert.IsTrue(sim.IsValid(child.Index, harvest), "grown: a tree");
        }

        [Test]
        public void A_child_comes_of_age_and_only_then_is_given_a_job()
        {
            var (sim, a, b) = Two();
            sim.JobReviewsEnabled = true;
            var child = sim.AddAgent(8, 8, Kit.Neutral);
            sim.SetAge(child, Kit.Data.ConfigInt("child_until_age") - 1);
            int year = Kit.Data.ConfigInt("ticks_per_age_year");

            var seen = All(sim, Wait(sim), year - 1);
            Assert.IsFalse(seen.Any(e => e.Type == SimEventType.VoteOpened && e.Agent == child.Index), "no vote on a child's job");
            Assert.IsFalse(seen.Any(e => e.Type == SimEventType.CameOfAge));

            seen = All(sim, Wait(sim), 2);
            Assert.IsTrue(seen.Any(e => e.Type == SimEventType.CameOfAge && e.Agent == child.Index));
            Assert.IsFalse(child.Child);
        }

        [Test]
        public void The_old_have_half_their_strength_and_endurance()
        {
            var (sim, agent) = Village.One();
            float young = agent.GatherSpeedMult, tires = agent.EnduranceMult;

            sim.SetAge(agent, Kit.Data.ConfigInt("elderly_from_age"));

            Assert.IsTrue(agent.Elder);
            Assert.Less(agent.GatherSpeedMult, young);
            Assert.Greater(agent.EnduranceMult, tires, "it tires and chills faster");
        }

        [Test]
        public void Old_age_ends_a_life_and_nobody_is_charged_for_it()
        {
            var (sim, a, b) = Two();
            sim.SetAge(a, Kit.Data.ConfigInt("old_age_death_from") + 40);
            float before = 0f;
            SimEvent death = default;
            bool died = false;

            for (int t = 0; t < 400 * TicksPerDay && !died; t++)
            {
                foreach (var agent in sim.Agents) Kit.Refill(agent);
                sim.Step(new[] { Wait(sim), Wait(sim) });
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.Died)
                    {
                        death = e;
                        died = true;
                        before = sim.RewardOf(b.Index);
                    }
            }

            Assert.IsTrue(died, "within four hundred days at 2% a day");
            Assert.AreEqual(a.Index, death.Agent);
            Assert.AreEqual(Simulation.DiedOfOldAge, death.Item);
            Assert.GreaterOrEqual(before, 0f, "the other villager is not charged for a death of old age");
        }

        // ------------------------------------------------------------------ emotes and gifts

        [Test]
        public void A_happy_emote_makes_those_close_by_think_better_of_each_other()
        {
            var (sim, a, b) = Two();
            var far = sim.AddAgent(12, 8, Kit.Neutral);
            float step = Kit.Data.ConfigFloat("delta_emote_exchange");
            a.Set(Need.Morale, 50f);
            b.Set(Need.Morale, 50f);

            sim.Step(new[] { new AgentAction(sim.Actions.EmoteHappy), Wait(sim), Wait(sim) });

            Assert.AreEqual(step, sim.RelationshipOf(a.Index, b.Index));
            Assert.AreEqual(step, sim.RelationshipOf(b.Index, a.Index));
            Assert.AreEqual(0f, sim.RelationshipOf(a.Index, far.Index), "too far to see it");
            Assert.IsTrue(sim.Events.Any(e => e.Type == SimEventType.EmoteExchanged && e.Agent == a.Index && e.Item == b.Index));
            Assert.Greater(b.Get(Need.Morale), 50f, "and feel better for it");

            sim.Step(new[] { new AgentAction(sim.Actions.EmoteHappy), Wait(sim), Wait(sim) });
            Assert.AreEqual(step, sim.RelationshipOf(a.Index, b.Index), "not again so soon");

            All(sim, Wait(sim), Kit.Data.ConfigInt("emote_exchange_cooldown_ticks"));
            sim.Step(new[] { new AgentAction(sim.Actions.EmoteHappy), Wait(sim), Wait(sim) });
            Assert.AreEqual(2 * step, sim.RelationshipOf(a.Index, b.Index));
        }

        [Test]
        public void An_emote_is_seen_on_the_grid_for_a_few_ticks()
        {
            var (sim, a, b) = Two();
            int emoteChannel = 4, size = sim.Observation.GridSize, channels = sim.Observation.GridChannels;
            int south = ((size / 2 + 1) * size + size / 2) * channels + emoteChannel;   // b is one tile north of a, so a is south of b

            sim.Step(new[] { new AgentAction(sim.Actions.EmoteHappy), Wait(sim) });
            Vector(sim, b, out var grid);
            Assert.AreEqual(Simulation.EmoteHappy, grid[south]);

            All(sim, Wait(sim), Kit.Data.ConfigInt("emote_ticks"));
            Vector(sim, b, out grid);
            Assert.AreEqual(0, grid[south]);
        }

        [Test]
        public void Giving_hands_one_over_and_makes_friends()
        {
            var (sim, a, b) = Two();
            Kit.Face(a, 0, 1);
            Village.Give(sim, a, "berry", 3);
            var give = new AgentAction(sim.Actions.Give, Kit.Item("berry"));
            Assert.IsTrue(sim.IsValid(a.Index, give));
            Assert.IsFalse(sim.IsValid(b.Index, new AgentAction(sim.Actions.Give, Kit.Item("berry"))), "b has none and faces nobody");

            sim.Step(new[] { give, Wait(sim) });

            Assert.AreEqual(2, Village.Has(a, "berry"));
            Assert.AreEqual(1, Village.Has(b, "berry"));
            Assert.AreEqual(Kit.Data.ConfigFloat("delta_gift_item"), sim.RelationshipOf(b.Index, a.Index));
            Assert.IsTrue(sim.Events.Any(e => e.Type == SimEventType.Gave && e.Agent == a.Index && e.Qty == b.Index));
        }

        // ------------------------------------------------------------------ marriage

        [Test]
        public void Two_who_stay_close_for_long_enough_marry()
        {
            var (sim, a, b) = Two();
            long sustain = (long)Kit.Data.ConfigInt("marriage_sustain_days") * TicksPerDay;
            sim.SetRelationship(a, b, Kit.Data.ConfigFloat("marriage_threshold"));

            var seen = All(sim, Wait(sim), sustain - 1);
            Assert.AreEqual(-1, a.Spouse, "not yet");

            float earned = 0f;
            for (int t = 0; t < 3 && a.Spouse < 0; t++)
            {
                sim.Step(new[] { Wait(sim), Wait(sim) });
                seen.AddRange(sim.Events);
                earned = sim.RewardOf(a.Index);
            }
            Assert.AreEqual(b.Index, a.Spouse);
            Assert.AreEqual(a.Index, b.Spouse);
            Assert.IsTrue(seen.Any(e => e.Type == SimEventType.Married));
            Assert.AreEqual(Kit.Data.Rewards.First(r => r.Id == "marriage").Value, earned, 1e-4f);

            var v = Vector(sim, a, out _);
            Assert.AreEqual(1f, v[sim.Observation.Fields["status flags"].Start], "married");
            Kit.Face(a, 0, 1);
            v = Vector(sim, a, out _);
            int faced = sim.Observation.Fields["faced agent"].Start;
            Assert.AreEqual(Kit.Data.ConfigFloat("marriage_threshold") / Kit.Data.ConfigFloat("relationship_max"), v[faced + 1], 1e-6f, "what it thinks of whom it faces");
            Assert.AreEqual(1f, v[faced + 2], "its spouse");
        }

        [Test]
        public void Friends_who_fall_out_do_not_marry_and_neither_do_children()
        {
            var (sim, a, b) = Two();
            long sustain = (long)Kit.Data.ConfigInt("marriage_sustain_days") * TicksPerDay;
            sim.SetRelationship(a, b, 100f);
            All(sim, Wait(sim), sustain / 2);
            sim.SetRelationship(a, b, 10f);
            All(sim, Wait(sim), 5);
            sim.SetRelationship(a, b, 100f);
            All(sim, Wait(sim), sustain - 10);
            Assert.AreEqual(-1, a.Spouse, "the days are counted afresh");

            var (sim2, c, d) = Two();
            sim2.SetAge(c, 3);
            sim2.SetRelationship(c, d, 100f);
            All(sim2, Wait(sim2), sustain + 10);
            Assert.AreEqual(-1, d.Spouse);
        }

        [Test]
        public void A_couple_shares_a_home()
        {
            var (sim, a, b) = Two();
            var tent = sim.PlaceBuilding("hide_tent", 5, 4);
            sim.Step(new[] { new AgentAction(sim.Actions.Sleep), Wait(sim) });
            Assert.AreEqual(tent.Index, a.Home);
            Assert.AreEqual(-1, b.Home);

            sim.SetRelationship(a, b, 100f);
            All(sim, Wait(sim), (long)Kit.Data.ConfigInt("marriage_sustain_days") * TicksPerDay + 2);

            Assert.AreEqual(a.Index, b.Spouse);
            Assert.AreEqual(tent.Index, b.Home, "moved in");
            CollectionAssert.Contains(tent.Residents, b.Index);
        }

        // ------------------------------------------------------------------ children

        /// <summary>A married couple with a tent, asleep in front of it for as long as it takes.</summary>
        static (Simulation sim, Agent a, Agent b, Building tent) Couple(int width = 14, int height = 10)
        {
            var (sim, a, b) = Two(width, height);
            var tent = sim.PlaceBuilding("hide_tent", 5, 4);
            sim.SetRelationship(a, b, 100f);
            All(sim, new AgentAction(sim.Actions.Sleep), (long)Kit.Data.ConfigInt("marriage_sustain_days") * TicksPerDay + 2);
            Assert.AreEqual(b.Index, a.Spouse);
            Assert.AreEqual(tent.Index, a.Home);
            Assert.AreEqual(tent.Index, b.Home);
            return (sim, a, b, tent);
        }

        [Test]
        public void A_couple_asleep_at_home_has_a_child_who_takes_after_both()
        {
            var (sim, a, b, tent) = Couple();
            var sleep = new AgentAction(sim.Actions.Sleep);

            var seen = All(sim, sleep, 120L * TicksPerDay, () => sim.Agents.Count > 2);

            Assert.AreEqual(3, sim.Agents.Count, "within a hundred and twenty nights at one in ten");
            var child = sim.Agents[2];
            var born = seen.Last(e => e.Type == SimEventType.Born);
            Assert.AreEqual(child.Index, born.Agent);
            Assert.IsTrue(child.Alive);
            Assert.IsTrue(child.Child);
            Assert.AreEqual(0, sim.AgeOf(child));
            Assert.AreEqual(1, child.Generation);
            CollectionAssert.AreEquivalent(new[] { a.Index, b.Index }, new[] { child.Parent1, child.Parent2 });
            Assert.AreEqual(1, a.Children);
            Assert.AreEqual(0, child.Role, "no job for a child");
            int mutation = Kit.Data.ConfigInt("stat_mutation");
            Assert.That(child.Stats.Str, Is.InRange(Kit.Neutral.Str - mutation, Kit.Neutral.Str + mutation));
            Assert.That(child.Stats.Int, Is.LessThanOrEqualTo(Kit.Data.ConfigInt("int_cap_generation_0") + 1));
            Assert.That(child.Stats.Empathy, Is.InRange(0f, 1f));
            Assert.AreEqual(Kit.Data.Rewards.First(r => r.Id == "child_born").Value, sim.RewardOf(a.Index), 1e-4f, "the whole village is glad");
            Assert.IsFalse(sim.World.BuildingIndex[sim.World.Index(child.X, child.Y)] >= 0, "it stands on open ground by the home");
        }

        [Test]
        public void No_child_without_a_night_together_at_home()
        {
            var (sim, a, b, tent) = Couple();

            All(sim, Wait(sim), 60L * TicksPerDay);

            Assert.AreEqual(2, sim.Agents.Count, "they sat up every night");
        }

        [Test]
        public void Children_come_no_faster_than_the_cooldown_and_no_more_than_the_village_holds()
        {
            var (sim, a, b, tent) = Couple();
            var sleep = new AgentAction(sim.Actions.Sleep);
            All(sim, sleep, 120L * TicksPerDay, () => sim.Agents.Count > 2);
            long first = sim.Tick;

            All(sim, sleep, 300L * TicksPerDay, () => sim.Agents.Count > 3);
            Assert.AreEqual(4, sim.Agents.Count);
            Assert.GreaterOrEqual(sim.Tick - first, (long)Kit.Data.ConfigInt("reproduction_cooldown_days") * TicksPerDay);

            sim.MaxVillagersEver = 4;
            All(sim, sleep, 200L * TicksPerDay);
            Assert.AreEqual(4, sim.Agents.Count, "the village is full");
        }

        // ------------------------------------------------------------------ grief and company

        [Test]
        public void Losing_a_spouse_is_grieved()
        {
            var (sim, a, b, tent) = Couple();
            b.Set(Need.Morale, 80f);
            a.Set(Need.Hp, 0.001f);
            a.Set(Need.Hunger, 0f);

            sim.Step(new[] { Wait(sim), Wait(sim) });

            Assert.IsFalse(a.Alive);
            Assert.AreEqual(-1, b.Spouse, "widowed");
            Assert.Less(b.Get(Need.Morale), 80f + Kit.Data.ConfigFloat("grief_morale_hit") + 1f);
            Assert.Greater(b.GriefUntilTick, sim.Tick);
        }

        [Test]
        public void Company_lifts_morale_and_being_alone_wears_it_down()
        {
            var (sim, a, b) = Two(40, 12);
            var alone = sim.AddAgent(30, 6, Kit.Neutral);
            foreach (var agent in sim.Agents) agent.Set(Need.Morale, 50f);

            for (int t = 0; t < 100; t++) sim.Step(new[] { Wait(sim), Wait(sim), Wait(sim) });

            Assert.Greater(a.Get(Need.Morale), alone.Get(Need.Morale));
            Assert.Greater(a.Get(Need.Morale), 50f, "with company a villager of middling sociability gains a little");
            Assert.Less(alone.Get(Need.Morale), 50f - 100 * 0.02f, "alone it loses more than time alone takes");
        }
    }
}
