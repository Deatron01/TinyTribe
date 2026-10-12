using System.Collections.Generic;
using TinyTribe.Sim.Core;
using UnityEngine;

namespace TinyTribe.View
{
    /// <summary>
    /// One sprite per building. A building site is the finished picture drawn faintly, more solid
    /// as the work gets on; a lit campfire has a flame over it that also shows at night.
    /// </summary>
    public sealed class BuildingLayer : MonoBehaviour
    {
        const float SiteOpacityStart = 0.3f, SiteOpacityEnd = 0.75f;
        const float FlameFramesPerSecond = 9f;
        const string WarmthEffect = "warmth_radius";
        static readonly Vector3 FlameOffset = new Vector3(0.5f, 0.3f, 0f);

        sealed class Entry
        {
            public SpriteRenderer Body;
            public SpriteRenderer Flame;
        }

        readonly List<Entry> entries = new List<Entry>();
        Simulation sim;
        IArt art;
        Material material;
        int flameOrder;
        float clock;

        public int Count => entries.Count;

        /// <summary>Where the lower-left corner of the building's footprint is drawn.</summary>
        public Vector3 PositionOf(int buildingIndex) => entries[buildingIndex].Body.transform.position;

        public bool IsDrawn(int buildingIndex) =>
            buildingIndex < entries.Count && entries[buildingIndex].Body.enabled && entries[buildingIndex].Body.sprite != null;

        /// <summary>1 for a finished building, less for a site.</summary>
        public float Opacity(int buildingIndex) => entries[buildingIndex].Body.color.a;

        public bool ShowsFlame(int buildingIndex) =>
            buildingIndex < entries.Count && entries[buildingIndex].Flame != null && entries[buildingIndex].Flame.enabled;

        /// <summary>flameSortingOrder should be above the night shade, so that a fire stays bright in the dark.</summary>
        public void Build(Simulation simulation, IArt artSource, Material spriteMaterial, int flameSortingOrder)
        {
            sim = simulation;
            art = artSource;
            material = spriteMaterial;
            flameOrder = flameSortingOrder;
            Sync(0f);
        }

        SpriteRenderer CreateRenderer(string objectName, Vector3 position)
        {
            var child = new GameObject(objectName);
            child.transform.SetParent(transform, false);
            child.transform.position = position;
            var spriteRenderer = child.AddComponent<SpriteRenderer>();
            if (material != null) spriteRenderer.sharedMaterial = material;
            return spriteRenderer;
        }

        void Add(Building building)
        {
            var corner = new Vector3(building.X, building.Y, 0f);
            var entry = new Entry { Body = CreateRenderer(building.Def.Name, corner) };
            entry.Body.sprite = art.Building(building.Def);
            // The lowest row of the footprint decides what the building is drawn in front of.
            entry.Body.sortingOrder = WorldView.RowOrder(sim.World.Height, building.Y);
            if (building.Def.EffectKey == WarmthEffect && art.FlameFrames > 0)
            {
                entry.Flame = CreateRenderer("Flame", corner + FlameOffset);
                entry.Flame.sortingOrder = flameOrder;
                entry.Flame.enabled = false;
            }
            entries.Add(entry);
        }

        /// <summary>Adds sprites for new buildings and redraws them all. Call once per frame.</summary>
        public void Sync(float deltaTime)
        {
            clock += deltaTime;
            var buildings = sim.Buildings;
            while (entries.Count < buildings.Count) Add(buildings[entries.Count]);

            for (int i = 0; i < entries.Count; i++)
            {
                var building = buildings[i];
                var entry = entries[i];
                entry.Body.enabled = !building.Destroyed;
                float done = building.Def.BuildTicks > 0 ? Mathf.Clamp01(building.Progress / building.Def.BuildTicks) : 1f;
                entry.Body.color = building.Complete ? Color.white : new Color(1f, 1f, 1f, Mathf.Lerp(SiteOpacityStart, SiteOpacityEnd, done));

                if (entry.Flame == null) continue;
                entry.Flame.enabled = building.IsLit;
                // Fires next to each other should not flicker in step.
                if (building.IsLit) entry.Flame.sprite = art.Flame((int)(clock * FlameFramesPerSecond) + i * 3);
            }
        }
    }
}
