using RimWorld;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// Shooting 21 -- Grandmaster Marksman. Shared gates and tuning constants.
    ///
    /// DESIGN LINE: a Grandmaster has near-perfect mastery of everything the SHOOTER controls --
    /// aim, timing, recovery, compensation, shot placement. Nothing here touches what the
    /// PROJECTILE controls: range, damage, armour penetration and line of sight are untouched.
    ///
    /// Everything in this file is on a combat hot path, so it is written to fail fast: the first
    /// test for a normal pawn is an integer field comparison.
    /// </summary>
    public static class Gm21Shooting
    {
        /// <summary>
        /// Fraction of the remaining miss chance a Grandmaster still suffers.
        ///
        /// Applied as  final = 1 - (1 - base) * 0.01,  NOT as a flat +99 points. A flat bonus
        /// would push a 30% pistol and a 90% rifle both to 100% and erase weapon differentiation
        /// entirely. Proportional removal keeps the better weapon better:
        ///     30% -> 99.3%,  60% -> 99.6%,  90% -> 99.9%.
        /// </summary>
        public const float MissChanceRetained = 0.01f;

        /// <summary>Fraction of warmup / cooldown ticks a Grandmaster still pays (99% reduction).</summary>
        public const float DelayRetained = 0.01f;

        /// <summary>Never hand the engine a zero- or negative-tick stance.</summary>
        public const int MinDelayTicks = 1;

        /// <summary>
        /// False when the passive shooting patches could not be applied -- a combat overhaul is
        /// active, or a target method was not found. The mod's core (level 21, permanence,
        /// quality) is unaffected; only the marksman package is off.
        /// </summary>
        public static bool PassiveBonusesEnabled;

        /// <summary>False when Killer/Downed anatomical targeting is unavailable.</summary>
        public static bool AnatomicalTargetingEnabled;

        /// <summary>
        /// Legitimate stored Shooting 21. Reads levelInt through Gm21.IsGrandmaster, so a gene or
        /// hediff aptitude can neither grant nor remove marksman status.
        /// </summary>
        public static bool IsShootingGrandmaster(Pawn pawn)
        {
            return Gm21.IsGrandmaster(pawn, SkillDefOf.Shooting);
        }

        /// <summary>
        /// The passive package also requires a body that can actually work a weapon.
        ///
        /// Mastery is not telekinesis: a pawn who cannot manipulate or is not conscious falls
        /// straight back to vanilla accuracy. This is what keeps "missing arms still matter" true
        /// while the accuracy compensation is otherwise near-total -- without it, the
        /// 1 - (1-base)*0.01 curve would erase every capacity penalty, because it works on
        /// whatever miss chance is left no matter where that miss chance came from.
        /// </summary>
        public static bool HasPassiveBonuses(Pawn pawn)
        {
            if (!PassiveBonusesEnabled) return false;
            if (!IsShootingGrandmaster(pawn)) return false;
            return IsPhysicallyCapable(pawn);
        }

        public static bool IsPhysicallyCapable(Pawn pawn)
        {
            Pawn_HealthTracker health = pawn.health;
            if (health == null || health.capacities == null) return false;
            return health.capacities.CapableOf(PawnCapacityDefOf.Consciousness)
                && health.capacities.CapableOf(PawnCapacityDefOf.Manipulation);
        }

        /// <summary>Applies the proportional miss-chance removal to an already-final 0..1 chance.</summary>
        public static float CompensateChance(float baseChance)
        {
            if (baseChance >= 1f) return 1f;
            if (baseChance < 0f) baseChance = 0f;
            return 1f - (1f - baseChance) * MissChanceRetained;
        }

        /// <summary>Applies the 99% delay reduction with the engine-safe floor.</summary>
        public static int ReduceDelayTicks(int ticks)
        {
            if (ticks <= MinDelayTicks) return ticks;
            int reduced = UnityEngine.Mathf.RoundToInt(ticks * DelayRetained);
            return reduced < MinDelayTicks ? MinDelayTicks : reduced;
        }
    }

    /// <summary>
    /// Who is currently resolving a shot on this thread, and whether they are a marksman.
    ///
    /// WHY THIS EXISTS: RimWorld's ShotReport is a struct of precomputed factors. By the time the
    /// hit-chance properties are read, the caster is no longer reachable from the report, so a
    /// postfix on those properties cannot tell whose shot it is. The context is set at the two
    /// places that DO know the caster:
    ///
    ///   * Verb_LaunchProjectile.TryCastShot -- prefix opens it, finalizer closes it. This is the
    ///     authoritative scope for an actual shot, and the finalizer runs even if the cast throws.
    ///   * ShotReport.HitReportFor -- postfix sets it from the caster argument. This covers the
    ///     targeting UI, which builds a report and immediately reads it for the readout.
    ///
    /// The flag is precomputed once per shot so the property postfixes are a single bool read:
    /// an ordinary shooter pays essentially nothing.
    ///
    /// [ThreadStatic] because RimWorld does touch some of this off the main thread.
    /// </summary>
    public static class Gm21ShotContext
    {
        [System.ThreadStatic] private static int depth;
        [System.ThreadStatic] private static bool grandmaster;

        /// <summary>True while a Shooting Grandmaster's shot is being resolved on this thread.</summary>
        public static bool ShooterIsGrandmaster
        {
            get { return grandmaster; }
        }

        /// <summary>Used by the HitReportFor postfix, which has no matching close.</summary>
        public static void Observe(Thing caster)
        {
            if (depth > 0) return; // an open TryCastShot scope is authoritative; do not clobber it
            Pawn pawn = caster as Pawn;
            grandmaster = pawn != null && Gm21Shooting.HasPassiveBonuses(pawn);
        }

        public static void Open(Thing caster)
        {
            Pawn pawn = caster as Pawn;
            if (depth == 0)
            {
                grandmaster = pawn != null && Gm21Shooting.HasPassiveBonuses(pawn);
            }
            depth++;
        }

        public static void Close()
        {
            if (depth > 0) depth--;
            if (depth == 0) grandmaster = false;
        }
    }
}
