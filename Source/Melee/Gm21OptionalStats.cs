using RimWorld;
using UnityEngine;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// OPTIONAL modded stat support. Strictly optional: Grandmaster 21 has no dependency on any
    /// RPG/Isekai stat framework and behaves correctly in pure vanilla, where every factor here
    /// returns exactly 1.0 and costs one null check.
    ///
    /// WHY THIS EXISTS AT ALL. The melee composites in Gm21Melee are built from vanilla capacities
    /// because those are the only things guaranteed to be there. But the brief's fantasy -- a
    /// heavily modded pawn becoming appropriately absurd -- is about stats vanilla does not have:
    /// Strength, Intelligence, Perception. If a mod supplies one, folding it in is a strict
    /// improvement; if none does, nothing changes.
    ///
    /// HOW A MODDED STAT IS NORMALISED. A foreign stat's scale is unknown, so its raw value is
    /// meaningless to us. What is knowable is the stat's OWN declared baseline,
    /// StatDef.defaultBaseValue, which is the value the def author considers ordinary. Dividing by
    /// it converts any stat, on any scale, into this file's convention: 1.0 = ordinary. A stat
    /// whose baseline is zero or negative is unusable and is ignored.
    ///
    /// Resolution happens once, lazily, through GetNamedSilentFail -- a missing def is null, not
    /// an exception, and no def is required to exist.
    /// </summary>
    public static class Gm21OptionalStats
    {
        /// <summary>
        /// How far a single modded stat is allowed to move a composite. Even an absurd Isekai
        /// stat cannot multiply a composite by more than this, because these factors MULTIPLY
        /// into formulas that already scale with vanilla capacities -- without a cap, two
        /// stacked frameworks could produce infinities and NaNs in combat maths.
        ///
        /// This is not a nerf: 8x on top of already-superhuman vanilla capacities is far past the
        /// point where every probability in the system has saturated.
        /// </summary>
        private const float MaxFactor = 8f;

        private const float MinFactor = 0.1f;

        // Candidate defNames, most specific first. These are names commonly used by RPG-style
        // stat frameworks; none is required, and an unknown framework simply contributes nothing.
        private static readonly string[] StrengthNames =
            { "Strength", "MeleeStrength", "RPG_Strength", "SM_Strength", "PhysicalPower" };

        private static readonly string[] AwarenessNames =
            { "CombatAwareness", "Perception", "Intelligence", "RPG_Intelligence", "Awareness" };

        private static readonly string[] PrecisionNames =
            { "Finesse", "Dexterity", "RPG_Dexterity", "Precision" };

        private static bool resolved;
        private static StatDef strength, awareness, precision;

        /// <summary>
        /// Resolved lazily rather than in a static constructor: defs do not exist until the game
        /// has loaded them, and a static constructor on this class could easily run first.
        /// </summary>
        private static void Resolve()
        {
            if (resolved) return;
            resolved = true;
            strength = FirstNamed(StrengthNames);
            awareness = FirstNamed(AwarenessNames);
            precision = FirstNamed(PrecisionNames);

            if (strength != null || awareness != null || precision != null)
            {
                Log.Message("[Grandmaster 21] Optional melee stats detected -- strength: "
                            + Name(strength) + ", awareness: " + Name(awareness)
                            + ", precision: " + Name(precision)
                            + ". These amplify Melee Grandmaster composites; none is required.");
            }
        }

        private static string Name(StatDef def) { return def == null ? "<none>" : def.defName; }

        private static StatDef FirstNamed(string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                StatDef def = DefDatabase<StatDef>.GetNamedSilentFail(names[i]);
                // A stat with no sane baseline cannot be normalised, so it is not usable here.
                if (def != null && def.defaultBaseValue > 0f) return def;
            }
            return null;
        }

        private static float FactorFor(Pawn pawn, StatDef def)
        {
            if (def == null || pawn == null) return 1f;
            float value;
            try
            {
                value = pawn.GetStatValue(def);
            }
            catch
            {
                // A foreign StatWorker that throws on this pawn must not take combat down with it.
                return 1f;
            }
            if (value <= 0f || float.IsNaN(value) || float.IsInfinity(value)) return 1f;
            return Mathf.Clamp(value / def.defaultBaseValue, MinFactor, MaxFactor);
        }

        public static float StrengthFactor(Pawn pawn) { Resolve(); return FactorFor(pawn, strength); }
        public static float AwarenessFactor(Pawn pawn) { Resolve(); return FactorFor(pawn, awareness); }
        public static float PrecisionFactor(Pawn pawn) { Resolve(); return FactorFor(pawn, precision); }
    }
}
