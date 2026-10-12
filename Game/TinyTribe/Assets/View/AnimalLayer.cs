using System.Collections.Generic;
using TinyTribe.Brains;
using TinyTribe.Sim.Core;
using UnityEngine;

namespace TinyTribe.View
{
    /// <summary>
    /// One sprite per animal, gliding between tiles like the villagers. The simulation reuses the
    /// place of a dead animal for a new one, so a sprite is tied to a place in the list, not to an animal.
    /// </summary>
    public sealed class AnimalLayer : MonoBehaviour
    {
        const float MinGlideSpeed = 6f;
        const float SnapDistance = 2.5f;
        const float MoveFramesPerSecond = 6f;

        readonly List<SpriteRenderer> bodies = new List<SpriteRenderer>();
        readonly List<Animal> shown = new List<Animal>();
        Simulation sim;
        IArt art;
        Material material;
        float clock;

        public int Count => bodies.Count;

        public Vector3 PositionOf(int animalIndex) => bodies[animalIndex].transform.position;

        public bool IsDrawn(int animalIndex) =>
            animalIndex < bodies.Count && bodies[animalIndex].enabled && bodies[animalIndex].sprite != null;

        public void Build(Simulation simulation, IArt artSource, Material spriteMaterial)
        {
            sim = simulation;
            art = artSource;
            material = spriteMaterial;
        }

        static Vector3 TileCentre(Animal animal) => new Vector3(animal.X + 0.5f, animal.Y + 0.5f, 0f);

        /// <summary>Moves and redraws every animal. Call once per frame after the simulation has advanced.</summary>
        public void Sync(SimRunner runner, float deltaTime)
        {
            clock += deltaTime;
            var animals = sim.Animals;
            while (bodies.Count < animals.Count)
            {
                var child = new GameObject("Animal");
                child.transform.SetParent(transform, false);
                var body = child.AddComponent<SpriteRenderer>();
                if (material != null) body.sharedMaterial = material;
                body.enabled = false;
                bodies.Add(body);
                shown.Add(null);
            }

            float glide = Mathf.Max(MinGlideSpeed, runner.TicksPerSecond * 1.5f) * deltaTime;
            for (int i = 0; i < bodies.Count; i++)
            {
                var animal = animals[i];
                var body = bodies[i];
                if (!animal.Alive)
                {
                    body.enabled = false;
                    continue;
                }

                var target = TileCentre(animal);
                var position = body.transform.position;
                float distance = (target - position).sqrMagnitude;
                bool arrived = !body.enabled || !ReferenceEquals(shown[i], animal) || distance > SnapDistance * SnapDistance;
                body.transform.position = arrived ? target : Vector3.MoveTowards(position, target, glide);
                shown[i] = animal;
                body.enabled = true;

                int frame = !arrived && distance > 0.0004f ? 1 + (int)(clock * MoveFramesPerSecond) : 0;
                body.sprite = art.Animal(animal.Def, animal.FaceDx, animal.FaceDy, frame, out bool flipX);
                // Seen from the side, an animal walking up or down keeps looking the way it last did.
                if (animal.FaceDx != 0) body.flipX = flipX;
                // In step with the villagers: over the nodes of its own row, behind those of the rows in front.
                body.sortingOrder = WorldView.RowOrder(sim.World.Height, Mathf.FloorToInt(body.transform.position.y)) + 1;
            }
        }
    }
}
