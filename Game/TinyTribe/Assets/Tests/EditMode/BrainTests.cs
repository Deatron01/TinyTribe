using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TinyTribe.Brains;
using TinyTribe.Sim.Core;

namespace TinyTribe.Tests
{
    /// <summary>Test double: always answers with the same action and remembers who asked.</summary>
    sealed class FixedBrain : IBrain
    {
        readonly AgentAction action;
        public readonly List<int> Asked = new List<int>();

        public FixedBrain(AgentAction action) => this.action = action;

        public AgentAction Decide(Simulation sim, int agentIndex)
        {
            Asked.Add(agentIndex);
            return action;
        }
    }

    /// <summary>
    /// Test double: wraps a brain and counts the actions that were already invalid at the moment
    /// they were chosen. (An action can still fail later in the same tick if another villager
    /// gets there first, e.g. takes the last berries; that is not the brain's mistake.)
    /// </summary>
    sealed class ValidityCheckingBrain : IBrain
    {
        readonly IBrain inner;
        public int InvalidChoices;

        public ValidityCheckingBrain(IBrain inner) => this.inner = inner;

        public AgentAction Decide(Simulation sim, int agentIndex)
        {
            var action = inner.Decide(sim, agentIndex);
            if (!sim.IsValid(agentIndex, action)) InvalidChoices++;
            return action;
        }

        public static ValidityCheckingBrain WrapAll(SimRunner runner)
        {
            var checker = new ValidityCheckingBrain(runner.GetBrain(0));
            for (int i = 0; i < runner.Sim.Agents.Count; i++) runner.SetBrain(i, checker);
            return checker;
        }
    }

    public class SimRunnerTests
    {
        static SimRunner OneAgent(IBrain brain = null)
        {
            var sim = Kit.NewSim();
            sim.AddAgent(4, 4, Kit.Neutral);
            return new SimRunner(sim, brain ?? new IdleBrain()) { MaxMillisecondsPerAdvance = double.MaxValue };
        }

        [Test]
        public void Advance_runs_ticks_at_the_set_speed()
        {
            var runner = OneAgent();
            runner.TicksPerSecond = 5;

            int ran = runner.Advance(1f);

            Assert.AreEqual(5, ran);
            Assert.AreEqual(5, runner.Sim.Tick);
        }

        [Test]
        public void Advance_carries_fractions_of_a_tick_over_to_the_next_frame()
        {
            var runner = OneAgent();
            runner.TicksPerSecond = 5;

            for (int frame = 0; frame < 20; frame++) runner.Advance(0.05f);

            Assert.AreEqual(5, runner.Sim.Tick);
        }

        [Test]
        public void Paused_runner_does_not_advance()
        {
            var runner = OneAgent();
            runner.Paused = true;

            Assert.AreEqual(0, runner.Advance(10f));
            Assert.AreEqual(0, runner.Sim.Tick);
        }

        [Test]
        public void Time_spent_paused_is_not_caught_up_afterwards()
        {
            var runner = OneAgent();
            runner.TicksPerSecond = 5;
            runner.Paused = true;
            runner.Advance(10f);

            runner.Paused = false;
            runner.Advance(0.2f);

            Assert.AreEqual(1, runner.Sim.Tick);
        }

        [Test]
        public void StepOnce_runs_exactly_one_tick_even_when_paused()
        {
            var runner = OneAgent();
            runner.Paused = true;

            runner.StepOnce();

            Assert.AreEqual(1, runner.Sim.Tick);
        }

        [Test]
        public void Ticks_per_advance_are_capped()
        {
            var runner = OneAgent();
            runner.TicksPerSecond = 1_000_000;
            runner.MaxTicksPerAdvance = 50;

            Assert.AreEqual(50, runner.Advance(1f));
        }

        [Test]
        public void A_slow_frame_does_not_build_up_a_backlog()
        {
            var runner = OneAgent();
            runner.TicksPerSecond = 1_000_000;
            runner.MaxTicksPerAdvance = 50;
            runner.Advance(1f);

            runner.TicksPerSecond = 5;
            Assert.AreEqual(0, runner.Advance(0.01f), "ticks that did not fit are dropped, not queued");
        }

        [Test]
        public void The_brain_decides_the_agents_action()
        {
            var runner = OneAgent(new FixedBrain(new AgentAction(0)));
            var sim = runner.Sim;
            runner.SetBrain(0, new FixedBrain(new AgentAction(sim.Actions.MoveEast)));

            runner.StepOnce();

            Assert.AreEqual((5, 4), (sim.Agents[0].X, sim.Agents[0].Y));
        }

        [Test]
        public void Each_agent_can_have_its_own_brain()
        {
            var sim = Kit.NewSim();
            var first = sim.AddAgent(2, 2, Kit.Neutral);
            var second = sim.AddAgent(6, 6, Kit.Neutral);
            var runner = new SimRunner(sim, new IdleBrain());
            runner.SetBrain(1, new FixedBrain(new AgentAction(sim.Actions.MoveEast)));

            runner.StepOnce();

            Assert.AreEqual((2, 2), (first.X, first.Y));
            Assert.AreEqual((7, 6), (second.X, second.Y));
        }

        [Test]
        public void Agents_added_later_get_the_default_brain()
        {
            var sim = Kit.NewSim();
            var brain = new FixedBrain(new AgentAction(sim.Actions.Wait));
            var runner = new SimRunner(sim, brain);
            sim.AddAgent(2, 2, Kit.Neutral);

            runner.StepOnce();

            Assert.AreSame(brain, runner.GetBrain(0));
            CollectionAssert.AreEqual(new[] { 0 }, brain.Asked);
        }

        [Test]
        public void Dead_agents_are_not_asked_for_an_action()
        {
            var sim = Kit.NewSim();
            var doomed = sim.AddAgent(2, 2, Kit.Neutral);
            sim.AddAgent(6, 6, Kit.Neutral);
            var brain = new FixedBrain(new AgentAction(sim.Actions.Wait));
            var runner = new SimRunner(sim, brain);
            doomed.Set(Need.Hunger, 0);
            doomed.Set(Need.Hp, 0.1f);
            runner.StepOnce();
            Assert.IsFalse(doomed.Alive);
            brain.Asked.Clear();

            runner.StepOnce();

            CollectionAssert.AreEqual(new[] { 1 }, brain.Asked);
        }

        [Test]
        public void Frame_events_keep_everything_from_all_ticks_of_one_advance()
        {
            var sim = Kit.NewSim();
            var bush = Kit.Data.NodeById("berry_bush");
            sim.World.PlaceNode(5, 4, bush);
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            Kit.Face(agent, 1, 0);
            var runner = new SimRunner(sim, new FixedBrain(new AgentAction(sim.Actions.Harvest)))
            {
                TicksPerSecond = bush.HarvestTicks + 1,
                MaxMillisecondsPerAdvance = double.MaxValue,
            };

            runner.Advance(1f);

            Assert.IsFalse(sim.Events.Any(e => e.Type == SimEventType.Harvested), "the yield was not on the last tick");
            Assert.IsTrue(runner.FrameEvents.Any(e => e.Type == SimEventType.Harvested));
        }

        [Test]
        public void Frame_events_are_cleared_by_the_next_advance()
        {
            var runner = OneAgent(new FixedBrain(new AgentAction(4)));
            runner.StepOnce();
            Assert.IsNotEmpty(runner.FrameEvents, "harvesting thin air is reported as invalid");

            runner.SetBrain(0, new IdleBrain());
            runner.StepOnce();

            Assert.IsEmpty(runner.FrameEvents);
        }

        [Test]
        public void Taking_control_swaps_in_the_manual_brain_and_release_restores_the_old_one()
        {
            var bot = new IdleBrain();
            var runner = OneAgent(bot);
            Assert.AreEqual(-1, runner.ControlledAgent);

            runner.TakeControl(0);
            Assert.AreEqual(0, runner.ControlledAgent);
            Assert.AreSame(runner.Manual, runner.GetBrain(0));

            runner.ReleaseControl();
            Assert.AreEqual(-1, runner.ControlledAgent);
            Assert.AreSame(bot, runner.GetBrain(0));
        }

        [Test]
        public void Taking_control_of_another_agent_releases_the_first()
        {
            var sim = Kit.NewSim();
            sim.AddAgent(2, 2, Kit.Neutral);
            sim.AddAgent(6, 6, Kit.Neutral);
            var bot = new IdleBrain();
            var runner = new SimRunner(sim, bot);

            runner.TakeControl(0);
            runner.TakeControl(1);

            Assert.AreSame(bot, runner.GetBrain(0));
            Assert.AreSame(runner.Manual, runner.GetBrain(1));
        }

        [Test]
        public void Manual_brain_plays_a_queued_action_once_then_waits()
        {
            var runner = OneAgent();
            var sim = runner.Sim;
            runner.TakeControl(0);
            runner.Manual.Queue(new AgentAction(sim.Actions.MoveEast));

            runner.StepOnce();
            runner.StepOnce();

            Assert.AreEqual((5, 4), (sim.Agents[0].X, sim.Agents[0].Y), "moved once, not twice");
            Assert.AreEqual(sim.Actions.Wait, sim.Agents[0].LastAction.Id);
        }

        [Test]
        public void Manual_brain_keeps_doing_a_held_action_until_it_is_done()
        {
            var runner = OneAgent();
            var sim = runner.Sim;
            var agent = sim.Agents[0];
            int spear = Kit.Item("wooden_spear");
            sim.AddItem(agent, Kit.Item("pine_wood"), 2);
            runner.TakeControl(0);

            runner.Manual.Hold(new AgentAction(sim.Actions.CraftBuild, Kit.Data.ArgFor("wooden_spear")));
            Assert.IsTrue(runner.Manual.IsHolding);
            for (int t = 0; t < Village.Recipe("wooden_spear").CraftTicks; t++) runner.StepOnce();

            Assert.AreEqual(1, agent.Count(spear), "a wait in between would have cancelled the craft");
            runner.StepOnce();
            Assert.IsFalse(runner.Manual.IsHolding, "nothing left to make it from");
            Assert.AreEqual(sim.Actions.Wait, agent.LastAction.Id);
            Assert.IsTrue(agent.LastActionValid);
        }

        [Test]
        public void Any_other_action_ends_a_held_one()
        {
            var runner = OneAgent();
            var sim = runner.Sim;
            runner.TakeControl(0);
            runner.Manual.Hold(new AgentAction(sim.Actions.Sleep));
            runner.StepOnce();
            Assert.IsTrue(sim.Agents[0].Sleeping);

            runner.Manual.Queue(new AgentAction(sim.Actions.MoveEast));
            runner.StepOnce();
            runner.StepOnce();

            Assert.IsFalse(runner.Manual.IsHolding);
            Assert.AreEqual((5, 4), (sim.Agents[0].X, sim.Agents[0].Y));
            Assert.AreEqual(sim.Actions.Wait, sim.Agents[0].LastAction.Id);
        }

        [Test]
        public void Runner_says_when_a_frame_had_more_events_than_it_keeps()
        {
            var sim = Kit.NewSim(30, 30);
            for (int i = 0; i < 40; i++) sim.AddAgent(i % 20 + 2, i / 20 * 4 + 2, Kit.Neutral);
            // Harvesting thin air: one "could not be done" event per villager per tick.
            var runner = new SimRunner(sim, new FixedBrain(new AgentAction(sim.Actions.Harvest)))
            {
                TicksPerSecond = 1000,
                MaxMillisecondsPerAdvance = double.MaxValue,
            };

            runner.Advance(0.01f);
            Assert.IsFalse(runner.FrameEventsDropped, "400 events fit");

            runner.Advance(1f);
            Assert.IsTrue(runner.FrameEventsDropped, "40000 events do not");

            runner.Advance(0.01f);
            Assert.IsFalse(runner.FrameEventsDropped, "each frame starts afresh");
        }

        [Test]
        public void Idle_brain_waits()
        {
            var runner = OneAgent();

            runner.StepOnce();

            Assert.AreEqual(runner.Sim.Actions.Wait, runner.Sim.Agents[0].LastAction.Id);
        }

        [Test]
        public void A_game_can_be_set_up_to_start_paused()
        {
            var setup = new GameSetup { StartPaused = true };
            setup.Sim.MapWidth = 64;
            setup.Sim.MapHeight = 64;

            var runner = SimRunner.Create(Kit.Data, setup);

            Assert.IsTrue(runner.Paused);
            Assert.AreEqual(0, runner.Advance(1f));
        }

        [TestCase(BrainKind.Survival, typeof(SurvivalBrain))]
        [TestCase(BrainKind.Random, typeof(RandomBrain))]
        [TestCase(BrainKind.Idle, typeof(IdleBrain))]
        public void Create_builds_the_simulation_and_brains_from_a_setup(BrainKind kind, System.Type expected)
        {
            var setup = new GameSetup { Brains = kind, TicksPerSecond = 20 };
            setup.Sim.Seed = 4;
            setup.Sim.Agents = 3;
            setup.Sim.MapWidth = 64;
            setup.Sim.MapHeight = 64;

            var runner = SimRunner.Create(Kit.Data, setup);

            Assert.AreEqual(3, runner.Sim.Agents.Count);
            Assert.AreEqual(64, runner.Sim.World.Width);
            Assert.AreEqual(20, runner.TicksPerSecond);
            Assert.IsInstanceOf(expected, runner.GetBrain(0));
        }
    }

    public class RandomBrainTests
    {
        [Test]
        public void Random_brain_only_picks_valid_actions_and_gets_around()
        {
            var setup = new GameSetup { Brains = BrainKind.Random };
            setup.Sim.Seed = 2;
            setup.Sim.MapWidth = 64;
            setup.Sim.MapHeight = 64;
            var runner = SimRunner.Create(Kit.Data, setup);
            var sim = runner.Sim;
            var start = sim.Agents.Select(a => (a.X, a.Y)).ToArray();
            var checker = ValidityCheckingBrain.WrapAll(runner);

            for (int t = 0; t < 300; t++) runner.StepOnce();

            Assert.AreEqual(0, checker.InvalidChoices);
            Assert.IsTrue(sim.Agents.Where((a, i) => (a.X, a.Y) != start[i]).Any(), "nobody moved in 300 ticks");
        }
    }

    public class SurvivalBrainTests
    {
        static (Simulation sim, Agent agent, SimRunner runner) Setup()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            var runner = new SimRunner(sim, new SurvivalBrain(1)) { MaxMillisecondsPerAdvance = double.MaxValue };
            return (sim, agent, runner);
        }

        static void PlaceWater(Simulation sim, int x, int y)
        {
            sim.World.SetTerrain(x, y, Kit.Data.TerrainById("water"));
            sim.World.PlaceNode(x, y, Kit.Data.NodeById("water_source"));
        }

        static AgentAction Decide(SimRunner runner) => runner.GetBrain(0).Decide(runner.Sim, 0);

        /// <summary>Runs ticks until the predicate holds. Fails the test if it never does.</summary>
        static int RunUntil(SimRunner runner, System.Func<bool> done, int limit, string what)
        {
            for (int t = 1; t <= limit; t++)
            {
                runner.StepOnce();
                Assert.IsFalse(Kit.HasEvent(runner.Sim, SimEventType.InvalidAction), "bot chose an invalid action");
                if (done()) return t;
            }
            Assert.Fail(what + " did not happen within " + limit + " ticks");
            return -1;
        }

        [Test]
        public void Thirsty_villager_facing_water_drinks()
        {
            var (sim, agent, runner) = Setup();
            PlaceWater(sim, 5, 4);
            Kit.Face(agent, 1, 0);
            agent.Set(Need.Thirst, 20);

            var action = Decide(runner);

            Assert.AreEqual((sim.Actions.Consume, 0), (action.Id, action.Arg));
        }

        [Test]
        public void Thirsty_villager_carrying_water_drinks_it()
        {
            var (sim, agent, runner) = Setup();
            sim.AddItem(agent, Kit.Item("water"), 1);
            agent.Set(Need.Thirst, 20);

            var action = Decide(runner);

            Assert.AreEqual((sim.Actions.Consume, Kit.Item("water")), (action.Id, action.Arg));
        }

        [Test]
        public void Thirsty_villager_walks_to_water_and_drinks()
        {
            var (sim, agent, runner) = Setup();
            PlaceWater(sim, 8, 1);
            agent.Set(Need.Thirst, 20);

            RunUntil(runner, () => agent.Get(Need.Thirst) > 50, 30, "drinking");
        }

        [Test]
        public void Hungry_villager_eats_carried_food()
        {
            var (sim, agent, runner) = Setup();
            sim.AddItem(agent, Kit.Item("berry"), 2);
            agent.Set(Need.Hunger, 20);

            var action = Decide(runner);

            Assert.AreEqual((sim.Actions.Consume, Kit.Item("berry")), (action.Id, action.Arg));
        }

        [Test]
        public void Hungry_villager_does_not_eat_food_that_can_make_it_sick()
        {
            var (sim, agent, runner) = Setup();
            Assert.IsNotEmpty(Kit.Data.ItemById["raw_meat"].SicknessOnEat);
            sim.AddItem(agent, Kit.Item("raw_meat"), 2);
            agent.Set(Need.Hunger, 20);

            var action = Decide(runner);

            Assert.AreNotEqual(sim.Actions.Consume, action.Id);
        }

        [Test]
        public void Thirst_is_handled_before_hunger()
        {
            var (sim, agent, runner) = Setup();
            sim.AddItem(agent, Kit.Item("berry"), 2);
            sim.AddItem(agent, Kit.Item("water"), 1);
            agent.Set(Need.Hunger, 20);
            agent.Set(Need.Thirst, 20);

            var action = Decide(runner);

            Assert.AreEqual((sim.Actions.Consume, Kit.Item("water")), (action.Id, action.Arg));
        }

        [Test]
        public void Hungry_villager_forages_a_bush_and_eats()
        {
            var (sim, agent, runner) = Setup();
            sim.World.PlaceNode(7, 6, Kit.Data.NodeById("berry_bush"));
            agent.Set(Need.Hunger, 30);
            int berry = Kit.Item("berry");

            RunUntil(runner, () => sim.Events.Any(e => e.Type == SimEventType.Consumed && e.Item == berry), 40, "eating a berry");
        }

        [Test]
        public void Villager_sleeps_at_night()
        {
            var sim = Kit.NewSim();
            Kit.AdvanceTo(sim, 1, 0);
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            agent.Set(Need.Energy, 60);
            var runner = new SimRunner(sim, new SurvivalBrain(1));

            Assert.AreEqual(sim.Actions.Sleep, Decide(runner).Id);
        }

        [Test]
        public void Exhausted_villager_sleeps_by_day()
        {
            var (sim, agent, runner) = Setup();
            agent.Set(Need.Energy, 8);

            Assert.AreEqual(sim.Actions.Sleep, Decide(runner).Id);
        }

        [Test]
        public void Rested_villager_does_not_sleep_by_day()
        {
            var (sim, _, runner) = Setup();

            Assert.AreNotEqual(sim.Actions.Sleep, Decide(runner).Id);
        }

        [Test]
        public void Villager_with_nothing_urgent_gathers_wood()
        {
            var (sim, agent, runner) = Setup();
            sim.World.PlaceNode(7, 4, Kit.Data.NodeById("pine_tree"));
            sim.AddItem(agent, Kit.Item("berry"), 10);
            sim.AddItem(agent, Kit.Item("water"), 3);

            RunUntil(runner, () => agent.Count(Kit.Item("pine_wood")) > 0, 60, "gathering wood");
        }

        [Test]
        public void Villager_stocks_up_on_food_before_other_work()
        {
            var (sim, agent, runner) = Setup();
            sim.World.PlaceNode(5, 4, Kit.Data.NodeById("pine_tree"));
            sim.World.PlaceNode(4, 7, Kit.Data.NodeById("berry_bush"));
            sim.AddItem(agent, Kit.Item("water"), 3);
            // Already armed: only a spear comes before food.
            sim.AddItem(agent, Kit.Item("wooden_spear"), 1);
            sim.Step(new[] { new AgentAction(sim.Actions.Equip, Kit.Item("wooden_spear")) });

            RunUntil(runner, () => agent.Count(Kit.Item("berry")) > 0 || agent.Count(Kit.Item("pine_wood")) > 0, 60, "gathering anything");

            Assert.Greater(agent.Count(Kit.Item("berry")), 0, "berries first, although the tree is closer");
            Assert.AreEqual(0, agent.Count(Kit.Item("pine_wood")));
        }

        [Test]
        public void Villager_leaves_nodes_it_cannot_work_alone()
        {
            var (sim, agent, runner) = Setup();
            sim.World.PlaceNode(5, 4, Kit.Data.NodeById("surface_rock"));
            sim.AddItem(agent, Kit.Item("berry"), 10);
            sim.AddItem(agent, Kit.Item("water"), 3);

            for (int t = 0; t < 40; t++)
            {
                runner.StepOnce();
                Assert.IsFalse(Kit.HasEvent(sim, SimEventType.InvalidAction));
                Assert.AreNotEqual(sim.Actions.Harvest, agent.LastAction.Id);
            }
        }

        [Test]
        public void Villager_walks_around_obstacles()
        {
            var (sim, agent, runner) = Setup();
            var mountain = Kit.Data.TerrainById("mountain");
            for (int y = 1; y <= 7; y++) sim.World.SetTerrain(6, y, mountain);
            PlaceWater(sim, 8, 4);
            agent.Set(Need.Thirst, 20);

            RunUntil(runner, () => agent.Get(Need.Thirst) > 50, 40, "reaching water behind the wall");
        }

        [Test]
        public void Bots_on_a_generated_map_never_choose_an_action_that_is_already_invalid()
        {
            var setup = new GameSetup { Brains = BrainKind.Survival };
            setup.Sim.Seed = 1;
            var runner = SimRunner.Create(Kit.Data, setup);
            var checker = ValidityCheckingBrain.WrapAll(runner);

            for (int t = 0; t < 3 * Kit.Data.ConfigInt("ticks_per_day"); t++) runner.StepOnce();

            Assert.AreEqual(0, checker.InvalidChoices);
        }

        [Test]
        public void An_action_can_lose_a_race_within_a_tick_and_is_reported_without_cost()
        {
            var sim = Kit.NewSim();
            var bush = Kit.Data.NodeById("berry_bush");
            sim.World.PlaceNode(4, 4, bush);
            sim.World.NodeCharges[sim.World.Index(4, 4)] = 1;
            sim.World.NodeProgress[sim.World.Index(4, 4)] = bush.HarvestTicks - 1;
            var first = sim.AddAgent(3, 4, Kit.Neutral);
            var second = sim.AddAgent(5, 4, Kit.Neutral);
            Kit.Face(first, 1, 0);
            Kit.Face(second, -1, 0);
            var harvest = new AgentAction(sim.Actions.Harvest);
            Assert.IsTrue(sim.IsValid(0, harvest) && sim.IsValid(1, harvest), "both are valid before the tick");
            float energyBefore = second.Get(Need.Energy);

            sim.Step(new[] { harvest, harvest });

            Assert.Greater(first.Count(Kit.Item("berry")), 0, "the first villager took the last berries");
            Assert.AreEqual(0, second.Count(Kit.Item("berry")));
            Assert.IsFalse(second.LastActionValid);
            Assert.IsTrue(sim.Events.Any(e => e.Type == SimEventType.InvalidAction && e.Agent == second.Index));
            Assert.AreEqual(energyBefore - Kit.Data.NeedById("energy").BaseDrainPerTick, second.Get(Need.Energy), 1e-4f,
                "the failed harvest cost no work energy");
        }

        [Test]
        public void Bots_keep_the_village_alive_through_the_first_five_days([Values(1UL, 2UL, 3UL)] ulong seed)
        {
            var setup = new GameSetup { Brains = BrainKind.Survival };
            setup.Sim.Seed = seed;
            var runner = SimRunner.Create(Kit.Data, setup);

            for (int t = 0; t < 5 * Kit.Data.ConfigInt("ticks_per_day"); t++) runner.StepOnce();

            Assert.AreEqual(runner.Sim.Agents.Count, VillageSummary.AliveCount(runner.Sim), "seed " + seed);
        }
    }
}
