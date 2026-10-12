using System;
using System.Collections.Generic;
using System.Diagnostics;
using TinyTribe.Sim.Core;
using TinyTribe.Sim.Data;

namespace TinyTribe.Brains
{
    /// <summary>Everything chosen on the launch screen.</summary>
    public sealed class GameSetup
    {
        public SimulationOptions Sim = new SimulationOptions();
        public BrainKind Brains = BrainKind.Survival;
        public float TicksPerSecond = 5f;
        public bool StartPaused;

        /// <summary>Whether the run is written to disk as it is played (rule R43).</summary>
        public bool Record = true;

        /// <summary>With BrainKind.Neural: the name of the installed network (Assets/Resources/Models), and whether it always takes its top choice.</summary>
        public string Model = "villager";
        public bool Greedy;
    }

    /// <summary>
    /// Runs a simulation in real time: asks each villager's brain for an action, steps the
    /// simulation at the chosen speed, and keeps the events of the frame for the view.
    /// No Unity types, so it also drives headless runs and tests.
    /// </summary>
    public sealed class SimRunner
    {
        const int MaxFrameEvents = 8192;

        public bool Paused;

        /// <summary>Simulation speed. 5 is normal play; very large values run as fast as the limits allow.</summary>
        public float TicksPerSecond = 5f;

        /// <summary>Upper limit of ticks in one Advance call, so a slow frame cannot snowball.</summary>
        public int MaxTicksPerAdvance = 5000;

        /// <summary>Time budget for one Advance call, so the screen stays responsive at high speed.</summary>
        public double MaxMillisecondsPerAdvance = 12;

        public Simulation Sim { get; }

        /// <summary>Everything that happened since the previous Advance or StepOnce call.</summary>
        public IReadOnlyList<SimEvent> FrameEvents => frameEvents;

        /// <summary>True when the last Advance or StepOnce produced more events than FrameEvents keeps.</summary>
        public bool FrameEventsDropped { get; private set; }

        /// <summary>The brain used while a villager is under player control.</summary>
        public ManualBrain Manual { get; } = new ManualBrain();

        /// <summary>Index of the villager the player controls, or -1.</summary>
        public int ControlledAgent { get; private set; } = -1;

        /// <summary>Writes the run to disk as it goes (rule R43), or null.</summary>
        public RunRecorder Recorder;

        /// <summary>Counters and achievements for a run that is not recorded; a recorder keeps its own.</summary>
        public RunStats Stats;

        readonly IBrain defaultBrain;
        readonly List<IBrain> brains = new List<IBrain>();
        readonly List<SimEvent> frameEvents = new List<SimEvent>();
        readonly Stopwatch watch = new Stopwatch();
        AgentAction[] actions = new AgentAction[0];
        IBrain replacedBrain;
        double carry;

        public SimRunner(Simulation sim, IBrain defaultBrain)
        {
            Sim = sim;
            this.defaultBrain = defaultBrain;
        }

        /// <summary>The world and its runner as the setup asks. brain, when given, plays every villager instead of the kind the setup names.</summary>
        public static SimRunner Create(GameData data, GameSetup setup, IBrain brain = null)
        {
            var sim = Simulation.Create(data, setup.Sim);
            if (brain != null) return new SimRunner(sim, brain) { TicksPerSecond = setup.TicksPerSecond, Paused = setup.StartPaused };
            switch (setup.Brains)
            {
                case BrainKind.Neural: throw new InvalidOperationException("A trained network has to be handed to SimRunner.Create: only the game can load one.");
                case BrainKind.Random: brain = new RandomBrain(setup.Sim.Seed); break;
                case BrainKind.Idle: brain = new IdleBrain(); break;
                case BrainKind.Situational: brain = new SurvivalBrain(setup.Sim.Seed) { Memoryless = true, Situational = true }; break;
                default: brain = new SurvivalBrain(setup.Sim.Seed); break;
            }
            return new SimRunner(sim, brain) { TicksPerSecond = setup.TicksPerSecond, Paused = setup.StartPaused };
        }

        void EnsureBrains()
        {
            while (brains.Count < Sim.Agents.Count) brains.Add(defaultBrain);
        }

        public IBrain GetBrain(int agentIndex)
        {
            EnsureBrains();
            return brains[agentIndex];
        }

        public void SetBrain(int agentIndex, IBrain brain)
        {
            EnsureBrains();
            brains[agentIndex] = brain;
        }

        /// <summary>Hands one villager to the player. Any villager controlled before goes back to its own brain.</summary>
        public void TakeControl(int agentIndex)
        {
            ReleaseControl();
            replacedBrain = GetBrain(agentIndex);
            brains[agentIndex] = Manual;
            ControlledAgent = agentIndex;
        }

        public void ReleaseControl()
        {
            if (ControlledAgent < 0) return;
            brains[ControlledAgent] = replacedBrain;
            replacedBrain = null;
            ControlledAgent = -1;
        }

        /// <summary>Runs the ticks that are due for this much real time. Returns how many ran.</summary>
        public int Advance(float deltaSeconds)
        {
            frameEvents.Clear();
            FrameEventsDropped = false;
            if (Paused || deltaSeconds <= 0f)
            {
                carry = 0;
                return 0;
            }

            carry += (double)deltaSeconds * TicksPerSecond;
            int due;
            if (carry >= MaxTicksPerAdvance)
            {
                // More than one call may run: drop the rest instead of queuing it.
                due = MaxTicksPerAdvance;
                carry = 0;
            }
            else
            {
                due = (int)carry;
                carry -= due;
            }

            watch.Restart();
            int ran = 0;
            while (ran < due)
            {
                Tick();
                ran++;
                if ((ran & 15) == 0 && watch.Elapsed.TotalMilliseconds > MaxMillisecondsPerAdvance)
                {
                    carry = 0;
                    break;
                }
            }
            return ran;
        }

        /// <summary>Runs exactly one tick, also while paused.</summary>
        public void StepOnce()
        {
            frameEvents.Clear();
            FrameEventsDropped = false;
            Tick();
        }

        void Tick()
        {
            EnsureBrains();
            int count = Sim.Agents.Count;
            if (actions.Length != count) actions = new AgentAction[count];
            for (int i = 0; i < count; i++)
                actions[i] = Sim.Agents[i].Alive ? brains[i].Decide(Sim, i) : default;

            Sim.Step(actions);
            if (Recorder != null) Recorder.AfterTick(actions);
            else Stats?.AfterTick(Sim, actions);

            var events = Sim.Events;
            int room = MaxFrameEvents - frameEvents.Count;
            if (events.Count > room) FrameEventsDropped = true;
            for (int i = 0; i < events.Count && i < room; i++) frameEvents.Add(events[i]);
        }
    }
}
