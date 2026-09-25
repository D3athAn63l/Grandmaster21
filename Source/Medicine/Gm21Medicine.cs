using RimWorld;
using UnityEngine;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// Medicine 21 -- Grandmaster Physician. Shared gates, feature flags and EVERY tuning constant.
    ///
    /// DESIGN LINE: the extraordinary capability belongs to the pawn. Nothing here adds research,
    /// items, buildings or implants, and no foreign Def is mutated. A Medicine Grandmaster works
    /// through ordinary RimWorld medicine and ordinary medical jobs, and exceeds vanilla only where
    /// the capstone deliberately says so: deterministic tending, a per-condition Grandmaster
    /// Treatment, surgery without the failure branch, and three personal interventions (Cure,
    /// Reconstruct, Resuscitate).
    ///
    /// Numbers marked PROVISIONAL are first-pass tuning, not canon. They live here, and only here,
    /// so a later balance pass edits one file.
    /// </summary>
    public static class Gm21Medicine
    {
        // ---------------------------------------------------------------- feature flags

        /// <summary>Deterministic Grandmaster tend quality (TendUtility.DoTend + CompTended).</summary>
        public static bool TendEnabled;

        /// <summary>
        /// Grandmaster Treatment is attached to tended conditions. Requires the tend patch AND the
        /// Hediff.ExposeData persistence patch: state that would silently vanish on reload is never
        /// created in the first place.
        /// </summary>
        public static bool TreatmentEnabled;

        /// <summary>Faster natural recovery of a Grandmaster-treated injury.</summary>
        public static bool RecoveryEnabled;

        /// <summary>Faster immunity gain against a Grandmaster-treated immunizable disease.</summary>
        public static bool ImmunityEnabled;

        /// <summary>A Medicine Grandmaster's surgery never takes a failure outcome.</summary>
        public static bool SurgeryEnabled;

        // ---------------------------------------------------------------- medicine tiers

        /// <summary>
        /// A Grandmaster promotes medicine of cap C to the lowest loaded cap that is at least
        /// C * this. The factor is what stops a small "bridge" tier (0.70 -> 0.85) from consuming
        /// the Grandmaster bonus: a promotion has to be a meaningful one.
        /// </summary>
        public const float MeaningfulTierMultiplier = 1.30f;

        /// <summary>Added to C when no loaded medicine reaches C * MeaningfulTierMultiplier.</summary>
        public const float TopTierFallbackBonus = 0.30f;

        /// <summary>
        /// Two MedicalQualityMax values closer than this are the same tier, and a cap within this
        /// of the requirement satisfies it. XML-parsed floats such as 1.3 and 1.0 * 1.3 must not
        /// fall either side of a comparison by one ULP.
        /// </summary>
        public const float CapTolerance = 0.001f;

        // ---------------------------------------------------------------- Grandmaster Treatment (PROVISIONAL)

        // Recovery / immunity multiplier as a function of the treatment's effective tend quality.
        // A straight line through the brief's anchor points:
        //
        //     quality 0.70 -> 1.00x   (vanilla's herbal ceiling: no bonus at all)
        //     quality 1.00 -> 1.25x
        //     quality 1.30 -> 1.50x
        //     quality 1.60 -> 1.75x
        //     quality 1.90 -> 2.00x
        //
        // i.e. +0.25x per +0.30 quality, anchored at 1.00 -> 1.25x, clamped to [Min, Max].

        public const float RecoveryAnchorQuality = 1.00f;
        public const float RecoveryAnchorMultiplier = 1.25f;
        public const float RecoveryQualityStep = 0.30f;
        public const float RecoveryMultiplierStep = 0.25f;

        /// <summary>Grandmaster Treatment never makes anything recover SLOWER than vanilla.</summary>
        public const float MinRecoveryMultiplier = 1f;

        /// <summary>
        /// Safety ceiling for absurd modded medicine caps. 3x is reached at quality 3.10, well past
        /// anything the vanilla hierarchy produces (top is 1.60).
        /// </summary>
        public const float MaxRecoveryMultiplier = 3f;

        /// <summary>The single place the recovery/immunity curve is evaluated.</summary>
        public static float RecoveryMultiplierFor(float quality)
        {
            if (float.IsNaN(quality) || float.IsInfinity(quality)) return MinRecoveryMultiplier;
            float m = RecoveryAnchorMultiplier
                      + (quality - RecoveryAnchorQuality) * (RecoveryMultiplierStep / RecoveryQualityStep);
            return Mathf.Clamp(m, MinRecoveryMultiplier, MaxRecoveryMultiplier);
        }

        // ---------------------------------------------------------------- interventions (PROVISIONAL)

        /// <summary>Base work ticks for Cure at MedicalTendSpeed 1 (vanilla tend is 600).</summary>
        public const int CureWorkTicks = 1200;

        /// <summary>Base work ticks for Reconstruct at MedicalTendSpeed 1.</summary>
        public const int ReconstructWorkTicks = 4000;

        /// <summary>Base work ticks for Resuscitate at MedicalTendSpeed 1.</summary>
        public const int ResuscitateWorkTicks = 1250;

        /// <summary>Speed stat is clamped into this band so a modded stat can neither stall nor skip the job.</summary>
        public const float MinWorkSpeed = 0.25f;
        public const float MaxWorkSpeed = 4f;

        /// <summary>No intervention completes in less than one in-game minute of work.</summary>
        public const int MinWorkTicks = 60;

        /// <summary>
        /// Resuscitation viability window: how long after death the intervention may BEGIN.
        ///
        /// PROVISIONAL, deliberately conservative: four in-game hours (10,000 ticks, about 2m47s of
        /// real time at speed 1). Long enough to finish a fight and walk across a map, short
        /// enough that a corpse from yesterday -- or one kept fresh in a freezer -- stays dead.
        /// The clock stops once the Grandmaster actually begins the work, so a resuscitation
        /// started inside the window is not failed by the window while it is being performed.
        /// </summary>
        public const int ResuscitationWindowTicks = 4 * GenDate.TicksPerHour;

        // ---------------------------------------------------------------- gates

        /// <summary>
        /// Legitimate stored Medicine 21. Reads levelInt through Gm21.IsGrandmaster, so aptitude
        /// can neither grant nor remove the status.
        /// </summary>
        public static bool IsMedicineGrandmaster(Pawn pawn)
        {
            return Gm21.IsGrandmaster(pawn, SkillDefOf.Medicine);
        }

        /// <summary>
        /// A Medicine Grandmaster who can actually practise: conscious and able to manipulate.
        /// Same rule as Shooting 21 -- mastery is not telekinesis. A Grandmaster who cannot use their
        /// hands falls back to vanilla medicine, whatever their level.
        /// </summary>
        public static bool CanPractise(Pawn pawn)
        {
            if (!IsMedicineGrandmaster(pawn)) return false;
            if (pawn.Dead) return false;
            Pawn_HealthTracker health = pawn.health;
            if (health == null || health.capacities == null) return false;
            return health.capacities.CapableOf(PawnCapacityDefOf.Consciousness)
                && health.capacities.CapableOf(PawnCapacityDefOf.Manipulation);
        }

        /// <summary>Work ticks for an intervention, scaled by the doctor's MedicalTendSpeed.</summary>
        public static int WorkTicks(Pawn doctor, int baseTicks)
        {
            float speed = 1f;
            if (doctor != null)
            {
                speed = doctor.GetStatValue(StatDefOf.MedicalTendSpeed);
                if (float.IsNaN(speed) || float.IsInfinity(speed)) speed = 1f;
            }
            speed = Mathf.Clamp(speed, MinWorkSpeed, MaxWorkSpeed);
            return Mathf.Max(MinWorkTicks, Mathf.RoundToInt(baseTicks / speed));
        }
    }
}
