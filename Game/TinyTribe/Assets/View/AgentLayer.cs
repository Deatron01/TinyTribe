using System.Collections.Generic;
using TinyTribe.Brains;
using TinyTribe.Sim.Core;
using UnityEngine;

namespace TinyTribe.View
{
    /// <summary>One sprite per villager, gliding between tiles, plus the ring under the selected one.</summary>
    public sealed class AgentLayer : MonoBehaviour
    {
        const float MinGlideSpeed = 6f;
        const float SnapDistance = 2.5f;
        const float WalkFramesPerSecond = 8f;
        const int DeadOrder = 1;

        static readonly Color SleepTint = new Color(0.75f, 0.8f, 1f, 0.9f);
        static readonly Color FallenTint = new Color(0.55f, 0.55f, 0.55f, 0.6f);
        static readonly Color ControlledRing = new Color(1f, 0.9f, 0.2f);

        readonly List<SpriteRenderer> bodies = new List<SpriteRenderer>();
        Simulation sim;
        IArt art;
        Material material;
        SpriteRenderer ring;
        float walkClock;

        public int Count => bodies.Count;

        public Vector3 PositionOf(int agentIndex) => bodies[agentIndex].transform.position;

        public void Build(Simulation simulation, IArt artSource, Material spriteMaterial)
        {
            sim = simulation;
            art = artSource;
            material = spriteMaterial;
            ring = CreateRenderer("Selection");
            ring.sprite = art.SelectionRing;
            ring.enabled = false;
            EnsureBodies();
        }

        SpriteRenderer CreateRenderer(string objectName)
        {
            var child = new GameObject(objectName);
            child.transform.SetParent(transform, false);
            var spriteRenderer = child.AddComponent<SpriteRenderer>();
            if (material != null) spriteRenderer.sharedMaterial = material;
            return spriteRenderer;
        }

        static Vector3 TileCentre(Agent agent) => new Vector3(agent.X + 0.5f, agent.Y + 0.5f, 0f);

        void EnsureBodies()
        {
            while (bodies.Count < sim.Agents.Count)
            {
                var agent = sim.Agents[bodies.Count];
                var body = CreateRenderer("Villager " + (agent.Index + 1));
                body.transform.position = TileCentre(agent);
                bodies.Add(body);
                Draw(body, agent, false);
            }
        }

        /// <summary>Moves and redraws every villager. Call once per frame after the simulation has advanced.</summary>
        public void Sync(SimRunner runner, int selected, float deltaTime)
        {
            EnsureBodies();
            walkClock += deltaTime;
            float glide = Mathf.Max(MinGlideSpeed, runner.TicksPerSecond * 1.5f) * deltaTime;

            for (int i = 0; i < bodies.Count; i++)
            {
                var agent = sim.Agents[i];
                var body = bodies[i];
                var target = TileCentre(agent);
                var position = body.transform.position;
                float distance = (target - position).sqrMagnitude;
                body.transform.position = distance > SnapDistance * SnapDistance ? target : Vector3.MoveTowards(position, target, glide);
                Draw(body, agent, distance > 0.0004f);
            }

            bool showRing = selected >= 0 && selected < bodies.Count;
            ring.enabled = showRing;
            if (showRing)
            {
                var body = bodies[selected];
                ring.transform.position = body.transform.position;
                ring.sortingOrder = body.sortingOrder - 1;
                ring.color = runner.ControlledAgent == selected ? ControlledRing : Color.white;
            }
        }

        void Draw(SpriteRenderer body, Agent agent, bool moving)
        {
            var fallen = agent.Alive ? null : art.VillagerDead(agent.Index);
            if (fallen != null)
            {
                body.sprite = fallen;
                body.flipX = false;
                body.transform.rotation = Quaternion.identity;
                body.color = Color.white;
            }
            else
            {
                int frame = moving && agent.Alive ? 1 + (int)(walkClock * WalkFramesPerSecond) : 0;
                body.sprite = art.Villager(agent.Index, agent.FaceDx, agent.FaceDy, frame, out bool flipX);
                body.flipX = flipX;
                // No lying-down picture: lay the standing one on its side.
                bool lying = !agent.Alive || agent.Sleeping;
                body.transform.rotation = lying ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.identity;
                body.color = !agent.Alive ? FallenTint : agent.Sleeping ? SleepTint : Color.white;
            }

            // Villagers are drawn over the nodes of their own row and behind those of the rows in front.
            int row = Mathf.FloorToInt(body.transform.position.y);
            body.sortingOrder = agent.Alive ? WorldView.RowOrder(sim.World.Height, row) + 1 : DeadOrder;
        }

        /// <summary>Index of the villager drawn nearest to a world position, within reach, or -1.</summary>
        public int Nearest(Vector3 worldPosition, float reach)
        {
            int best = -1;
            float bestDistance = reach * reach;
            for (int i = 0; i < bodies.Count; i++)
            {
                float distance = ((Vector2)(bodies[i].transform.position - worldPosition)).sqrMagnitude;
                if (distance > bestDistance) continue;
                bestDistance = distance;
                best = i;
            }
            return best;
        }
    }
}
