using System;
using RimWorld;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// Cooking 21 (Grandmaster Cook): constants, feature flags, and the ONE place that asks whether a
    /// pawn is a Cooking Grandmaster. Everything else in the Cooking package calls
    /// <see cref="IsCookingGrandmaster"/>; nothing compares a skill level itself.
    ///
    /// Scope of this first pass, and only this:
    ///   Perfect Hygiene      -- a Grandmaster Cook's meals are never cooking-poisoned
    ///   Masterful Meals      -- persistent per-serving provenance (CompGrandmasterMeal)
    ///   Longer freshness     -- Masterful servings slow their stack's rot
    ///   Purify Food          -- an active command that removes contamination from prepared food
    ///
    /// Deliberately NOT here: recipes, yield, ingredient substitution, any change to vanilla
    /// food-poisoning or stack semantics. Vanilla CompFoodPoisonable and CompRottable keep running;
    /// this package only reads them, and intervenes where a Grandmaster Cook explicitly requires it.
    /// </summary>
    internal static class Gm21Cooking
    {
        // ---------------------------------------------------------------- tuning

        /// <summary>
        /// How much of a stack's rot the Masterful fraction removes. The stack's rot-rate multiplier
        /// is 1 - masterfulRatio * PreservationStrength: 1.00x with no Masterful servings, 0.60x at half,
        /// 0.20x (about five times the freshness) when every serving is Masterful.
        /// </summary>
        public const float PreservationStrength = 0.80f;

        /// <summary>Ticks of work to purify one stack (3 seconds at normal speed).</summary>
        public const int PurifyWorkTicks = 180;

        // ---------------------------------------------------------------- feature flags

        /// <summary>Each flag is raised only when that feature's audited hooks actually bound.</summary>
        internal static bool HygieneEnabled;
        internal static bool MasterfulEnabled;
        internal static bool IngestionEnabled;
        internal static bool FreshnessEnabled;
        internal static bool FreshnessEstimateEnabled;
        internal static bool PurifyEnabled;

        // ---------------------------------------------------------------- predicates

        /// <summary>
        /// A legitimately earned Cooking Grandmaster. Delegates to the central
        /// <see cref="Gm21.IsGrandmaster(Pawn, SkillDef)"/>, which reads the STORED level -- never the
        /// aptitude-adjusted one -- so a gene can neither manufacture nor remove Grandmaster status.
        /// </summary>
        public static bool IsCookingGrandmaster(Pawn pawn)
        {
            return Gm21.IsGrandmaster(pawn, SkillDefOf.Cooking);
        }

        /// <summary>
        /// A Cooking Grandmaster who can actually do the work: alive, conscious and able to use their
        /// hands. Same rule as Medicine 21 -- mastery is not telekinesis.
        /// </summary>
        public static bool CanPractise(Pawn pawn)
        {
            if (!IsCookingGrandmaster(pawn) || pawn.Dead) return false;
            Pawn_HealthTracker health = pawn.health;
            if (health == null || health.capacities == null) return false;
            return health.capacities.CapableOf(PawnCapacityDefOf.Consciousness)
                && health.capacities.CapableOf(PawnCapacityDefOf.Manipulation);
        }
    }

    /// <summary>
    /// The Masterful-serving arithmetic, as pure integer functions so it can be tested exhaustively.
    ///
    /// A stack holds <c>total</c> servings of which <c>marked</c> are Masterful. Whenever servings
    /// leave a stack (a merge takes some from another stack, a split hands some to a new piece) the
    /// Masterful ones travel proportionally, rounded half up, then clamped to what is physically
    /// possible. Both stacks are updated by exactly the same number, so the Masterful total across
    /// any sequence of splits and merges is conserved exactly. There is no float and nothing to drift.
    /// </summary>
    public static class Gm21MasterfulMath
    {
        /// <summary>
        /// How many of the <paramref name="take"/> servings removed from a stack of
        /// <paramref name="total"/> (of which <paramref name="marked"/> are Masterful) are Masterful.
        /// Always within [max(0, take - unmarked), min(take, marked)], which is exactly the range that
        /// leaves the source with 0 &lt;= marked' &lt;= total'.
        /// </summary>
        public static int Share(int total, int marked, int take)
        {
            if (total <= 0 || take <= 0) return 0;
            if (marked < 0) marked = 0;
            if (marked > total) marked = total;
            if (take >= total) return marked;      // the whole stack goes, and its Masterful with it
            long scaled = ((long)take * marked * 2 + total) / (2L * total); // round half up
            int lo = Math.Max(0, take - (total - marked));
            int hi = Math.Min(take, marked);
            if (scaled < lo) return lo;
            if (scaled > hi) return hi;
            return (int)scaled;
        }

        /// <summary>Masterful fraction of a stack, in [0, 1]. A stack with no servings has none.</summary>
        public static float Ratio(int marked, int total)
        {
            if (total <= 0 || marked <= 0) return 0f;
            if (marked >= total) return 1f;
            return (float)marked / total;
        }

        /// <summary>
        /// Future-rot multiplier for a stack: 1 - ratio * strength, clamped to [0, 1]. Exactly 1 when
        /// there is no Masterful serving, so ordinary food is bit-for-bit vanilla.
        /// </summary>
        public static float RotMultiplier(int marked, int total, float strength)
        {
            float ratio = Ratio(marked, total);
            if (ratio <= 0f) return 1f;
            float m = 1f - ratio * strength;
            if (m < 0f) return 0f;
            return m > 1f ? 1f : m;
        }
    }
}
