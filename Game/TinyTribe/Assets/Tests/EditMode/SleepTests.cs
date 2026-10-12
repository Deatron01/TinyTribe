using NUnit.Framework;
using TinyTribe.Sim.Core;

namespace TinyTribe.Tests
{
    public class SleepTests
    {
        static float EnergyBaseDrain => Kit.Data.NeedById("energy").BaseDrainPerTick;
        static float WakeEnergy => Kit.Data.ConfigFloat("forced_sleep_wake_energy");

        [Test]
        public void Sleeping_at_night_restores_energy_at_the_night_rate()
        {
            var sim = Kit.NewSim();
            Kit.AdvanceTo(sim, 1, 0);
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            agent.Set(Need.Energy, 50);

            sim.Step(new[] { new AgentAction(sim.Actions.Sleep) });

            Assert.AreEqual(50 + Kit.Data.Modifier("energy_sleep_night") - EnergyBaseDrain, agent.Get(Need.Energy), 1e-4f);
            Assert.IsTrue(agent.Sleeping);
        }

        [Test]
        public void Sleeping_by_day_restores_energy_at_the_day_rate()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            agent.Set(Need.Energy, 50);

            sim.Step(new[] { new AgentAction(sim.Actions.Sleep) });

            Assert.Less(Kit.Data.Modifier("energy_sleep_day"), Kit.Data.Modifier("energy_sleep_night"));
            Assert.AreEqual(50 + Kit.Data.Modifier("energy_sleep_day") - EnergyBaseDrain, agent.Get(Need.Energy), 1e-4f);
        }

        [Test]
        public void Agent_is_no_longer_sleeping_after_another_action()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            sim.Step(new[] { new AgentAction(sim.Actions.Sleep) });

            sim.Step(new[] { new AgentAction(sim.Actions.Wait) });

            Assert.IsFalse(agent.Sleeping);
        }

        [Test]
        public void Agent_at_zero_energy_is_forced_to_sleep_instead_of_acting()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            agent.Set(Need.Energy, 0);
            sim.Step(new[] { new AgentAction(sim.Actions.Wait) });
            Assert.IsTrue(agent.ForcedSleep);

            sim.Step(new[] { new AgentAction(sim.Actions.MoveEast) });

            Assert.AreEqual((4, 4), (agent.X, agent.Y), "the move is replaced by sleep");
            Assert.IsTrue(agent.Sleeping);
            Assert.Greater(agent.Get(Need.Energy), 0f);
        }

        [Test]
        public void Forced_sleep_continues_below_the_wake_threshold()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            agent.Set(Need.Energy, 0);
            sim.Step(new[] { new AgentAction(sim.Actions.Wait) });
            agent.Set(Need.Energy, WakeEnergy - 5);

            sim.Step(new[] { new AgentAction(sim.Actions.MoveEast) });

            Assert.AreEqual((4, 4), (agent.X, agent.Y));
            Assert.IsTrue(agent.ForcedSleep);
        }

        [Test]
        public void Forced_sleep_ends_at_the_wake_threshold()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            agent.Set(Need.Energy, 0);
            sim.Step(new[] { new AgentAction(sim.Actions.Wait) });
            agent.Set(Need.Energy, WakeEnergy);

            sim.Step(new[] { new AgentAction(sim.Actions.MoveEast) });

            Assert.IsFalse(agent.ForcedSleep);
            Assert.AreEqual((5, 4), (agent.X, agent.Y));
        }
    }
}
