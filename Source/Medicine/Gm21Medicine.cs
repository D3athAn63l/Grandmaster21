using System;
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
        /// The Grandmaster effective quality reaches Hediff.Tended itself (and so condition-specific
        /// overrides such as the heart-attack treatment roll), not only the TendDuration comp.
        /// </summary>
        public static bool TendPropagationEnabled;

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
        //
        // BALANCE LINE: an intervention costs the Grandmaster's TIME and ordinary MEDICINE, nothing
        // else. There is deliberately no cooldown, no charge, no per-pawn or per-day limit: a
        // Grandmaster who spends their day curing is a Grandmaster who did not spend it tending,
        // operating or doing anything else, and the medicine comes out of the colony's stores.

        /// <summary>
        /// Base work ticks for Cure at MedicalTendSpeed 1: one in-game hour (vanilla tend is 600,
        /// a heart or cataract operation 4500 work). A Grandmaster's own tend speed shortens it.
        /// </summary>
        public const int CureWorkTicks = 2500;

        /// <summary>Base work ticks for Reconstruct at MedicalTendSpeed 1: 2.4 in-game hours.</summary>
        public const int ReconstructWorkTicks = 6000;

        /// <summary>
        /// Base work ticks for Resuscitate at MedicalTendSpeed 1: three in-game hours, the longest
        /// intervention. The viability window only has to be met when the work BEGINS.
        /// </summary>
        public const int ResuscitateWorkTicks = 7500;

        /// <summary>
        /// MedicalTendSpeed below this counts as this, so a zero or negative modded value can never
        /// stall or invert the job. There is deliberately NO upper clamp: every point of tend speed
        /// the player invests keeps shortening the work, all the way down to MinWorkTicks.
        /// </summary>
        public const float MinWorkSpeed = 0.1f;

        /// <summary>
        /// Tick safety floor, and the only effective limit on speed: no intervention completes in
        /// under 60 ticks (one second of real time at speed 1).
        /// </summary>
        public const int MinWorkTicks = 60;

        /// <summary>
        /// Ordinary medicine each intervention consumes, measured in MedicalPotency: 1.0 is one unit
        /// of industrial medicine, 0.60 herbal, 1.60 glitterworld; modded medicine counts by its own
        /// loaded potency. Stacks combine (herbal x2 = 1.2 meets Cure). Potency decides only HOW MUCH
        /// medicine is used, never how well the intervention works.
        /// </summary>
        public const float CurePotencyBudget = 1.0f;
        public const float ReconstructPotencyBudget = 2.0f;
        public const float ResuscitatePotencyBudget = 3.0f;

        /// <summary>
        /// Reconstructing one's own body needs real hands: at least this Manipulation (a Grandmaster
        /// missing one arm is at 0.50). Self-Cure only needs the ordinary ability to practise.
        /// </summary>
        public const float SelfReconstructMinManipulation = 0.5f;

        /// <summary>
        /// Resuscitation viability: how far the corpse may have DECAYED, measured in vanilla's own
        /// CompRottable.RotProgress -- not how long ago the pawn died.
        ///
        /// PROVISIONAL: 10,000. Vanilla adds GenTemperature.RotRateAtTemperature x ticks to a
        /// corpse's RotProgress: 1 per tick at 10 C and above (it never goes faster than that), a
        /// linear fraction between 0 and 10 C, and nothing below 0 C. So at ordinary temperature
        /// 10,000 is four in-game hours of decay; refrigerated at 5 C it takes eight; frozen, the body
        /// stays recoverable for as long as it stays frozen. Vanilla's "Fresh" stage lasts to 150,000
        /// (2.5 days), so this threshold is far stricter than Fresh. Toxic fallout also adds rot to
        /// unroofed corpses, as vanilla does.
        ///
        /// Checked when the order is given and again when the Grandmaster begins the timed work.
        /// Once the work has begun on a recoverable body the procedure is committed: decay during
        /// the work never fails it.
        /// </summary>
        public const float MaxResuscitationRotProgress = 10000f;

        /// <summary>
        /// Grandmaster Resuscitation Shock: how long a revived pawn stays unconscious. PROVISIONAL,
        /// deterministic: six in-game hours, never scaled by anything.
        /// </summary>
        public const int ResuscitationShockTicks = 6 * GenDate.TicksPerHour;

        // ---------------------------------------------------------------- resuscitation trauma (PROVISIONAL)
        //
        // A resuscitated pawn keeps physical evidence of the death: up to MaxTraumaScars of the worst
        // traumatic LOCATIONS become vanilla permanent injuries. Ranking (Gm21Trauma.Score):
        //
        //     score = relative severity           (fresh injury severity at the part / part max health, <= 1;
        //                                          a destroyed-and-rebuilt part counts as 1)
        //           + DestroyedTraumaBonus         the part was destroyed and had to be rebuilt for life
        //           + VitalTraumaBonus             the part carries a lethal-capacity tag
        //           + LethalBlowTraumaBonus        the battle log names it as the death blow's target
        //
        // A location qualifies if it can carry a vanilla scar AND (it was destroyed, OR it took the
        // death blow, OR its relative severity is at least MinTraumaRelativeSeverity). Nothing is
        // invented to reach three.

        public const int MaxTraumaScars = 3;
        public const float MinTraumaRelativeSeverity = 0.10f;
        public const float DestroyedTraumaBonus = 1.0f;
        public const float VitalTraumaBonus = 0.5f;
        public const float LethalBlowTraumaBonus = 0.5f;

        /// <summary>
        /// A scar never takes more than this fraction of the part's max health, and never more than
        /// the wound it came from: meaningful, never fatal, never re-destroying the part.
        /// </summary>
        public const float MaxScarPartFraction = 0.4f;

        /// <summary>Scar severity on a vital part that had to be rebuilt, as a fraction of its max health.</summary>
        public const float RebuiltVitalScarFraction = 0.4f;

        /// <summary>A scar lighter than this is not worth creating.</summary>
        public const float MinScarSeverity = 0.5f;

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
            float speed = doctor == null ? 1f : doctor.GetStatValue(StatDefOf.MedicalTendSpeed);
            return WorkTicksForSpeed(baseTicks, speed);
        }

        /// <summary>
        /// Pure: base work divided by speed. A non-finite speed counts as 1, a speed below
        /// MinWorkSpeed counts as MinWorkSpeed, and nothing finishes in under MinWorkTicks. No upper
        /// clamp: speed 25 is base / 25, and a high enough speed simply reaches the tick floor.
        /// </summary>
        public static int WorkTicksForSpeed(int baseTicks, float speed)
        {
            if (float.IsNaN(speed) || float.IsInfinity(speed)) speed = 1f;
            if (speed < MinWorkSpeed) speed = MinWorkSpeed;
            double ticks = Math.Round(baseTicks / (double)speed, MidpointRounding.ToEven);
            if (ticks < MinWorkTicks) return MinWorkTicks;
            return ticks > int.MaxValue ? int.MaxValue : (int)ticks;
        }

        /// <summary>
        /// Whether this Grandmaster can reconstruct their own body: practising, and enough
        /// Manipulation left to do the work on themselves.
        /// </summary>
        public static bool CanSelfReconstruct(Pawn doctor)
        {
            if (!CanPractise(doctor)) return false;
            return doctor.health.capacities.GetLevel(PawnCapacityDefOf.Manipulation)
                   >= SelfReconstructMinManipulation - CapTolerance;
        }
    }
}
