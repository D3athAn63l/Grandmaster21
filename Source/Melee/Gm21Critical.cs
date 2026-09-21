using UnityEngine;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// Critical strikes: the Grandmaster finds the one spot, angle and instant where the weapon
    /// does what it is actually capable of.
    ///
    /// A critical MULTIPLIES the attack's real damage. It never replaces it with a fixed number,
    /// never changes the damage type, and never bypasses armour -- all of which is the point of
    /// the brief's "a 10x knife is different from a 10x persona monosword". A knife that rolls 10x
    /// is a devastating knife wound; a monosword that rolls 10x is something else entirely, and
    /// the difference is carried by the weapon, exactly as it should be.
    /// </summary>
    internal static class Gm21Critical
    {
        /// <summary>Critical chance for a healthy Grandmaster, before stat scaling.</summary>
        private const float BaseChance = 0.20f;

        /// <summary>Ceiling, so even an absurd pawn is not simply always critical.</summary>
        private const float MaxChance = 0.75f;

        /// <summary>
        /// Relative frequency of each multiplier, 2x through 10x, for a pawn of ordinary physical
        /// power. Steeply decreasing on purpose: 2x is the common "clean hit", 10x is the perfect
        /// strike that should be a story rather than a routine.
        /// </summary>
        private static readonly float[] TierWeights =
            { 40f, 22f, 14f, 9f, 6f, 4f, 2.5f, 1.5f, 1f };

        private const int LowestMultiplier = 2;

        /// <summary>
        /// How far physical power is allowed to bend the multiplier distribution. Clamped at both
        /// ends: below 1 a weak Grandmaster is pushed toward the low tiers, above 1 a powerful one
        /// is pushed toward the high ones, and 2.5 is already enough to make 10x the single most
        /// likely outcome.
        /// </summary>
        private const float MinPowerBias = 0.5f;
        private const float MaxPowerBias = 2.5f;

        /// <summary>
        /// CHANCE -- precision and awareness, because a critical is a placement problem, not a
        /// strength problem. Seeing the opening and putting the edge exactly there is what
        /// produces one; how hard the blow lands afterwards is the multiplier's job.
        /// </summary>
        internal static float Chance(float precision, float awareness)
        {
            float chance = BaseChance * precision * awareness;
            if (chance <= 0f) return 0f;
            return chance > MaxChance ? MaxChance : chance;
        }

        /// <summary>
        /// MULTIPLIER -- a weighted draw from the 2x..10x ladder, biased by physical power.
        ///
        /// Each tier's weight is multiplied by bias^(tier-2), so the bias compounds up the ladder
        /// rather than shifting it uniformly. At bias 1 the distribution is exactly TierWeights.
        /// At bias 2 the 10x weight is multiplied by 256 while 2x is untouched, which is what
        /// turns an extremely strong Grandmaster's criticals into the "appropriately absurd"
        /// outcome the brief asks for -- without any special case for modded pawns.
        ///
        /// No allocation: the weights are accumulated in one pass and the draw is a second pass
        /// over the same nine values.
        /// </summary>
        internal static float RollMultiplier(float power)
        {
            float bias = Mathf.Clamp(power, MinPowerBias, MaxPowerBias);

            float total = 0f;
            float factor = 1f;
            for (int i = 0; i < TierWeights.Length; i++)
            {
                total += TierWeights[i] * factor;
                factor *= bias;
            }
            if (total <= 0f) return LowestMultiplier;

            float roll = Rand.Value * total;
            factor = 1f;
            for (int i = 0; i < TierWeights.Length; i++)
            {
                float w = TierWeights[i] * factor;
                if (roll < w) return LowestMultiplier + i;
                roll -= w;
                factor *= bias;
            }
            return LowestMultiplier + TierWeights.Length - 1;   // float drift guard: top tier
        }

        /// <summary>Highest multiplier the ladder can ever produce. Used by the test suite.</summary>
        internal static int HighestMultiplier
        {
            get { return LowestMultiplier + TierWeights.Length - 1; }
        }

        internal static int LowestMultiplierValue
        {
            get { return LowestMultiplier; }
        }
    }
}
