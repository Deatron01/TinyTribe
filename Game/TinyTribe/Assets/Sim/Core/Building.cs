using System.Collections.Generic;
using TinyTribe.Sim.Data;

namespace TinyTribe.Sim.Core
{
    /// <summary>A building or a building site on the map. X, Y is the corner with the lowest coordinates.</summary>
    public sealed class Building
    {
        public int Index;
        public BuildingDef Def;
        public int X, Y, Width, Height;
        public float Progress;
        public bool Complete;

        /// <summary>Wrecked by a storm. It keeps its place in the list of buildings but is gone from the map.</summary>
        public bool Destroyed;

        /// <summary>Campfires: ticks of fuel left.</summary>
        public int FuelTicks;

        /// <summary>Storage slots, or null when the building stores nothing.</summary>
        public ItemStack[] Storage;

        /// <summary>Indexes of the villagers who live here.</summary>
        public readonly List<int> Residents = new List<int>();

        public bool IsLit => Complete && FuelTicks > 0;

        public bool Contains(int x, int y) => x >= X && x < X + Width && y >= Y && y < Y + Height;
    }

    public sealed class Animal
    {
        public int Index;
        public AnimalDef Def;
        public bool Alive = true;
        public int X, Y;
        public int FaceDx, FaceDy = -1;
        public float Hp;
        public float MoveBudget;
        public int AttackCooldown;

        /// <summary>Where it spawned; territorial animals stay near it.</summary>
        public int HomeX, HomeY;
    }
}
