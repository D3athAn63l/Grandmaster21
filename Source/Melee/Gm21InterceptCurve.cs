using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// The single calibration point for both interception systems.
    ///
    /// Ally melee interception and projectile interception ask the same physical question -- can
    /// this body get its weapon to that point in the time available? -- so they share one curve.
    /// Keeping it in one place means the brief's benchmark is stated exactly once and cannot drift
    /// between the two features.
    ///
    /// THE BENCHMARK. "8 m/s -> approximately 90%". That is this curve read at 8, and it is the
    /// value an ally interception at one tile produces for a Grandmaster with healthy senses. Both
    /// callers feed it an EFFECTIVE speed rather than a raw one:
    ///
    ///   ally melee       effective = MoveSpeed * Reaction,          then scaled by distance
    ///   projectile       effective = MoveSpeed * Reaction / Difficulty,  then scaled by distance
    ///
    /// so the projectile case reuses the identical shape while being made far harder by the thing
    /// it is trying to catch. See Gm21ProjectileDefence.Difficulty.
    /// </summary>
    internal static class Gm21InterceptCurve
    {
        /// <summary>
        /// Low below 4 c/s, moderate through 6, high approaching 8, and asymptotic above it. A
        /// vanilla healthy colonist at 4.6 c/s sits at about 0.5: the zone is transformative for a
        /// FAST Grandmaster, not free for every Grandmaster, which is what makes movement speed
        /// the stat the brief says it should be.
        /// </summary>
        private static readonly SimpleCurve Curve = new SimpleCurve
        {
            new CurvePoint(0f,  0f),
            new CurvePoint(2f,  0.15f),
            new CurvePoint(4f,  0.45f),
            new CurvePoint(6f,  0.72f),
            new CurvePoint(8f,  0.90f),
            new CurvePoint(10f, 0.97f),
            new CurvePoint(14f, 0.995f),
            new CurvePoint(20f, 0.999f)
        };

        /// <summary>Nothing is ever certain; something can always go wrong.</summary>
        internal const float MaxChance = 0.99f;

        internal static float Evaluate(float effectiveSpeed)
        {
            if (effectiveSpeed <= 0f) return 0f;
            float p = Curve.Evaluate(effectiveSpeed);
            if (p <= 0f) return 0f;
            return p > MaxChance ? MaxChance : p;
        }

        /// <summary>
        /// Distance falloff across the protective zone. Mild by design, so the 8 m/s benchmark
        /// stays recognisable at the edge of the radius -- 90% at one tile, about 81% at three --
        /// rather than collapsing into "only adjacent allies are really protected".
        /// </summary>
        internal static float DistanceFactor(float distance)
        {
            if (distance <= 1f) return 1f;
            if (distance >= Gm21Melee.ProtectiveRadius) return 0.90f;
            return 1f - 0.05f * (distance - 1f);
        }
    }
}
