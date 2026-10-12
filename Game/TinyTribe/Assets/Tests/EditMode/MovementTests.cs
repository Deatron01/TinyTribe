using NUnit.Framework;
using TinyTribe.Sim.Core;

namespace TinyTribe.Tests
{
    public class MovementTests
    {
        static float MoveCost(Simulation sim) => Kit.Data.Actions[sim.Actions.MoveEast].EnergyCost;

        static float EnergyBaseDrain => Kit.Data.NeedById("energy").BaseDrainPerTick;

        [Test]
        public void Move_enters_a_free_tile_and_faces_that_way()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);

            sim.Step(new[] { new AgentAction(sim.Actions.MoveEast) });

            Assert.AreEqual((5, 4), (agent.X, agent.Y));
            Assert.AreEqual((1, 0), (agent.FaceDx, agent.FaceDy));
            Assert.AreEqual(agent.Index, sim.World.AgentAt(5, 4));
            Assert.AreEqual(-1, sim.World.AgentAt(4, 4));
        }

        [Test]
        public void North_is_positive_y()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);

            sim.Step(new[] { new AgentAction(sim.Actions.MoveNorth) });

            Assert.AreEqual((4, 5), (agent.X, agent.Y));
        }

        [Test]
        public void Move_costs_the_configured_energy()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            float before = agent.Get(Need.Energy);

            sim.Step(new[] { new AgentAction(sim.Actions.MoveEast) });

            Assert.AreEqual(before - MoveCost(sim) - EnergyBaseDrain, agent.Get(Need.Energy), 1e-4f);
        }

        [Test]
        public void Move_into_water_only_turns_and_costs_no_move_energy()
        {
            var sim = Kit.NewSim();
            sim.World.SetTerrain(5, 4, Kit.Data.TerrainById("water"));
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            float before = agent.Get(Need.Energy);

            sim.Step(new[] { new AgentAction(sim.Actions.MoveEast) });

            Assert.AreEqual((4, 4), (agent.X, agent.Y));
            Assert.AreEqual((1, 0), (agent.FaceDx, agent.FaceDy));
            Assert.AreEqual(before - EnergyBaseDrain, agent.Get(Need.Energy), 1e-4f);
        }

        [Test]
        public void Move_off_the_map_only_turns()
        {
            var sim = Kit.NewSim();
            var agent = sim.AddAgent(0, 0, Kit.Neutral);

            sim.Step(new[] { new AgentAction(sim.Actions.MoveWest) });

            Assert.AreEqual((0, 0), (agent.X, agent.Y));
            Assert.AreEqual((-1, 0), (agent.FaceDx, agent.FaceDy));
        }

        [Test]
        public void Move_into_a_resource_node_only_turns()
        {
            var sim = Kit.NewSim();
            sim.World.PlaceNode(5, 4, Kit.Data.NodeById("pine_tree"));
            var agent = sim.AddAgent(4, 4, Kit.Neutral);

            sim.Step(new[] { new AgentAction(sim.Actions.MoveEast) });

            Assert.AreEqual((4, 4), (agent.X, agent.Y));
            Assert.AreEqual((1, 0), (agent.FaceDx, agent.FaceDy));
        }

        [Test]
        public void Agent_cannot_enter_an_occupied_tile()
        {
            var sim = Kit.NewSim();
            var mover = sim.AddAgent(4, 4, Kit.Neutral);
            sim.AddAgent(5, 4, Kit.Neutral);

            sim.Step(new[] { new AgentAction(sim.Actions.MoveEast), new AgentAction(sim.Actions.Wait) });

            Assert.AreEqual((4, 4), (mover.X, mover.Y));
        }

        [Test]
        public void Two_agents_heading_for_the_same_tile_do_not_both_enter()
        {
            var sim = Kit.NewSim();
            var left = sim.AddAgent(3, 4, Kit.Neutral);
            var right = sim.AddAgent(5, 4, Kit.Neutral);

            sim.Step(new[] { new AgentAction(sim.Actions.MoveEast), new AgentAction(sim.Actions.MoveWest) });

            bool leftEntered = (left.X, left.Y) == (4, 4);
            bool rightEntered = (right.X, right.Y) == (4, 4);
            Assert.IsTrue(leftEntered ^ rightEntered, "exactly one agent should hold the contested tile");
        }

        [Test]
        public void Entering_slow_terrain_keeps_the_agent_busy_for_the_extra_ticks()
        {
            var snow = Kit.Data.TerrainById("snow");
            Assert.Greater(snow.MoveCostTicks, 1, "test needs a terrain that costs more than one tick");
            var sim = Kit.NewSim();
            sim.World.SetTerrain(5, 4, snow);
            var agent = sim.AddAgent(4, 4, Kit.Neutral);
            var east = new[] { new AgentAction(sim.Actions.MoveEast) };

            sim.Step(east);
            Assert.AreEqual((5, 4), (agent.X, agent.Y));
            Assert.AreEqual(snow.MoveCostTicks - 1, agent.BusyTicks);

            for (int i = 0; i < snow.MoveCostTicks - 1; i++) sim.Step(east);
            Assert.AreEqual((5, 4), (agent.X, agent.Y), "moves are ignored while busy");

            sim.Step(east);
            Assert.AreEqual((6, 4), (agent.X, agent.Y));
        }
    }
}
