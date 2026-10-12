namespace TinyTribe.Sim.Core
{
    /// <summary>
    /// PCG32 random number generator. Used instead of System.Random so a seed gives the
    /// same sequence on every platform and runtime (Editor, Android, headless training).
    /// </summary>
    public sealed class Rng
    {
        const ulong Multiplier = 6364136223846793005UL;
        const ulong Increment = 1442695040888963407UL;
        ulong state;

        public Rng(ulong seed)
        {
            state = 0;
            NextUInt();
            state = unchecked(state + seed);
            NextUInt();
        }

        public ulong State => state;

        public uint NextUInt()
        {
            ulong old = state;
            state = unchecked(old * Multiplier + Increment);
            uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
            int rotation = (int)(old >> 59);
            return (xorShifted >> rotation) | (xorShifted << ((-rotation) & 31));
        }

        /// <summary>Uniform float in [0, 1).</summary>
        public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);

        /// <summary>Uniform int in [minInclusive, maxExclusive).</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            uint span = (uint)(maxExclusive - minInclusive);
            return minInclusive + (int)(((ulong)NextUInt() * span) >> 32);
        }

        public bool Chance(float probability) => NextFloat() < probability;
    }
}
