using System.Linq;
using NUnit.Framework;
using TinyTribe.Sim.Core;

namespace TinyTribe.Tests
{
    public class NeedsTests
    {
        static AgentAction[] Wait(Simulation sim) => new[] { new AgentAction(sim.Actions.Wait) };

        static float BaseDrain(string need) => Kit.Data.NeedById(need).BaseDrainPerTick;

        [Test]
        public void Needs_start_at_their_configured_values()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);

            foreach (var def in Kit.Data.Needs)
                Assert.AreEqual(def.Start, agent.Get((Need)def.ObsIndex), def.Id);
        }

        [Test]
        public void Hunger_drains_by_its_base_rate_each_tick()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            float before = agent.Get(Need.Hunger);

            for (int i = 0; i < 10; i++) sim.Step(Wait(sim));

            Assert.AreEqual(before - 10 * BaseDrain("hunger"), agent.Get(Need.Hunger), 1e-3f);
        }

        [Test]
        public void Starving_agent_loses_hp_each_tick()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            agent.Set(Need.Hunger, 0);
            agent.Set(Need.Hp, 80);

            sim.Step(Wait(sim));

            Assert.AreEqual(80 + Kit.Data.NeedById("hunger").HpDeltaPerTickAtZero, agent.Get(Need.Hp), 1e-4f);
        }

        [Test]
        public void Agent_dies_when_hp_reaches_zero()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            agent.Set(Need.Hunger, 0);
            agent.Set(Need.Hp, 0.1f);

            sim.Step(Wait(sim));

            Assert.IsFalse(agent.Alive);
            Assert.AreEqual(-1, sim.World.AgentAt(4, 4), "a dead agent no longer occupies its tile");
            Assert.IsTrue(sim.Events.Any(e => e.Type == SimEventType.Died && e.Agent == agent.Index));
        }

        [Test]
        public void Dead_agent_ignores_actions()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            agent.Set(Need.Hunger, 0);
            agent.Set(Need.Hp, 0.1f);
            sim.Step(Wait(sim));

            sim.Step(new[] { new AgentAction(sim.Actions.MoveEast) });

            Assert.AreEqual((4, 4), (agent.X, agent.Y));
        }

        [Test]
        public void Hp_regenerates_when_fed_and_watered()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            agent.Set(Need.Hp, 50);

            sim.Step(Wait(sim));

            Assert.AreEqual(50 + Kit.Data.Modifier("hp_regen"), agent.Get(Need.Hp), 1e-4f);
        }

        [Test]
        public void Hp_does_not_regenerate_when_hungry()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            agent.Set(Need.Hp, 50);
            agent.Set(Need.Hunger, Kit.Data.ConfigFloat("hp_regen_min_need") - 1);

            sim.Step(Wait(sim));

            Assert.AreEqual(50, agent.Get(Need.Hp), 1e-4f);
        }

        [Test]
        public void Needs_never_drop_below_zero()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            agent.Set(Need.Thirst, 0.01f);

            sim.Step(Wait(sim));

            Assert.AreEqual(0, agent.Get(Need.Thirst));
        }

        [Test]
        public void Winter_night_drains_body_temp()
        {
            var sim = Kit.NewSim();
            Kit.AdvanceToSeason(sim, "winter", 0);
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            float before = agent.Get(Need.Temp);

            sim.Step(Wait(sim));

            float delta = Kit.Data.Seasons.First(s => s.Id == "winter").TempDeltaNight;
            Assert.Less(delta, 0f);
            Assert.AreEqual(before + delta, agent.Get(Need.Temp), 1e-4f);
        }

        [Test]
        public void Body_temp_recovers_when_it_is_not_cold()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            agent.Set(Need.Temp, 50);

            sim.Step(Wait(sim));

            Assert.AreEqual(50 + Kit.Data.Modifier("temp_recovery"), agent.Get(Need.Temp), 1e-4f);
        }

        [Test]
        public void Freezing_agent_loses_hp()
        {
            var sim = Kit.NewSim();
            Kit.AdvanceToSeason(sim, "winter", 0);
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            agent.Set(Need.Temp, 0);
            agent.Set(Need.Hp, 80);

            sim.Step(Wait(sim));

            Assert.AreEqual(80 + Kit.Data.NeedById("temp").HpDeltaPerTickAtZero, agent.Get(Need.Hp), 1e-4f);
        }

        [Test]
        public void Endurance_reduces_energy_drain()
        {
            var sim = Kit.NewSim();
            var stats = Kit.Neutral;
            stats.End = Kit.Data.ConfigInt("core_stat_max");
            var agent = sim.AddAgent(4, 4, stats);
            float before = agent.Get(Need.Energy);

            sim.Step(Wait(sim));

            float mult = 1 - Kit.Data.ConfigFloat("end_drain_per_point") * (stats.End - Kit.Data.ConfigInt("stat_midpoint"));
            Assert.Less(mult, 1f);
            Assert.AreEqual(before - BaseDrain("energy") * mult, agent.Get(Need.Energy), 1e-5f);
        }

        [Test]
        public void Summer_days_increase_thirst()
        {
            var sim = Kit.NewSim();
            Kit.AdvanceToSeason(sim, "summer", 12);
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            float before = agent.Get(Need.Thirst);

            sim.Step(Wait(sim));

            Assert.AreEqual(before - BaseDrain("thirst") * Kit.Data.Modifier("thirst_summer_day"), agent.Get(Need.Thirst), 1e-4f);
        }
    }
}
