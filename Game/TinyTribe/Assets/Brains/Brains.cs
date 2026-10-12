using TinyTribe.Sim.Core;

namespace TinyTribe.Brains
{
    /// <summary>Decides one agent's action each tick: a person, a script or (later) a neural network.</summary>
    public interface IBrain
    {
        AgentAction Decide(Simulation sim, int agentIndex);
    }

    /// <summary>Neural is a trained network; the game supplies it (it needs Unity's Inference Engine), so SimRunner.Create is given the brain.</summary>
    /// <summary>Situational is the survival bot with no plans kept from one tick to the next (SurvivalBrain.Situational).</summary>
    public enum BrainKind { Survival, Random, Idle, Neural, Situational }

    /// <summary>Does nothing. Useful for watching needs drain or for controlling one villager by hand.</summary>
    public sealed class IdleBrain : IBrain
    {
        public AgentAction Decide(Simulation sim, int agentIndex) => new AgentAction(sim.Actions.Wait);
    }

    /// <summary>Picks uniformly among the actions and arguments the masks allow.</summary>
    public sealed class RandomBrain : IBrain
    {
        readonly Rng rng;
        bool[] actionMask = new bool[0];
        bool[] argMask = new bool[0];

        public RandomBrain(ulong seed) => rng = new Rng(seed);

        public AgentAction Decide(Simulation sim, int agentIndex)
        {
            int actionCount = sim.Data.Actions.Count, argCount = sim.Data.ActionArgs.Count;
            if (actionMask.Length != actionCount) actionMask = new bool[actionCount];
            if (argMask.Length != argCount) argMask = new bool[argCount];

            sim.GetActionMask(agentIndex, actionMask);
            int action = PickTrue(actionMask);
            if (action < 0) return new AgentAction(sim.Actions.Wait);
            if (sim.Data.Actions[action].Argument == "none") return new AgentAction(action);

            sim.GetArgMask(agentIndex, action, argMask);
            return new AgentAction(action, System.Math.Max(0, PickTrue(argMask)));
        }

        int PickTrue(bool[] mask)
        {
            int count = 0;
            foreach (bool allowed in mask)
                if (allowed) count++;
            if (count == 0) return -1;
            int pick = rng.Range(0, count);
            for (int i = 0; i < mask.Length; i++)
                if (mask[i] && pick-- == 0) return i;
            return -1;
        }
    }

    /// <summary>
    /// Plays whatever the player queued, once, or keeps doing what the player holds, and waits
    /// otherwise. The view fills it from the keyboard and the action buttons.
    /// </summary>
    public sealed class ManualBrain : IBrain
    {
        AgentAction pending, held;
        bool hasPending, hasHeld;

        public bool HasPending => hasPending;

        /// <summary>True while a held action is being repeated.</summary>
        public bool IsHolding => hasHeld;

        /// <summary>Does the action once, on the next tick. Ends whatever was being held.</summary>
        public void Queue(AgentAction action)
        {
            pending = action;
            hasPending = true;
            hasHeld = false;
        }

        /// <summary>
        /// Does the action every tick for as long as it can be done, like a key held down. Crafting and
        /// building take several ticks in a row, and a single wait in between abandons a craft.
        /// </summary>
        public void Hold(AgentAction action)
        {
            held = action;
            hasHeld = true;
            hasPending = false;
        }

        public AgentAction Decide(Simulation sim, int agentIndex)
        {
            if (hasPending)
            {
                hasPending = false;
                return pending;
            }
            if (hasHeld)
            {
                if (sim.IsValid(agentIndex, held)) return held;
                hasHeld = false;
            }
            return new AgentAction(sim.Actions.Wait);
        }
    }
}
