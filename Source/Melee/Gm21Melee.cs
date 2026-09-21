using RimWorld;
using UnityEngine;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// Melee 21 -- Grandmaster of Combat. Shared gates, stat model and tuning constants.
    ///
    /// DESIGN LINE: a Melee Grandmaster controls the immediate battlefield. Everything the mastery
    /// grants is an amplification of what the pawn's BODY can already do -- reach, precision,
    /// awareness, physical power. Nothing here invents a capability the pawn does not have:
    /// armour, weapon damage type, blast radius and line of sight are untouched, and a pawn who
    /// cannot see, think, move or grip degrades toward vanilla exactly as far as the missing
    /// capacity says it should.
    ///
    /// WHY THE STAT COMPOSITES ARE SHAPED THE WAY THEY ARE. Every composite in this file is
    /// normalised so that a HEALTHY VANILLA HUMAN SCORES EXACTLY 1.0. That single convention is
    /// what makes the whole system readable: a number below 1 is an impaired Grandmaster, a number
    /// above 1 is a superhuman one, and every formula downstream can be reasoned about in those
    /// terms without knowing which stats fed it. It is also what lets modded/Isekai pawns become
    /// absurd without a single special case -- their capacities simply exceed 1.
    ///
    /// Everything here is on a combat hot path, so it is written to fail fast: the first test for
    /// an ordinary pawn is an integer field comparison inside Gm21.IsGrandmaster.
    /// </summary>
    public static class Gm21Melee
    {
        // ---------------------------------------------------------------- feature flags

        /// <summary>
        /// False when the passive melee patches could not be applied -- a combat overhaul is
        /// active, or a target method was not found. The mod's core (level 21, permanence,
        /// quality) and the Shooting package are unaffected.
        /// </summary>
        public static bool PassiveEnabled;

        /// <summary>Riposte scheduling, cleave and the on-hit effects that ride the same hook.</summary>
        public static bool ReactionsEnabled;

        /// <summary>Killer/Downed melee doctrines (anatomical strike placement + controlled force).</summary>
        public static bool DoctrineEnabled;

        /// <summary>Ally melee interception inside the protective radius.</summary>
        public static bool AllyInterceptEnabled;

        /// <summary>Projectile interception, deflection, return-to-sender and safe redirection.</summary>
        public static bool ProjectileDefenceEnabled;

        // ---------------------------------------------------------------- geometry / references

        /// <summary>The Grandmaster's protective radius, in tiles. Ally and projectile defence both use it.</summary>
        public const float ProtectiveRadius = 3f;

        /// <summary>Squared, so the hot-path distance test never takes a square root.</summary>
        public const float ProtectiveRadiusSquared = ProtectiveRadius * ProtectiveRadius;

        /// <summary>
        /// A healthy unencumbered vanilla human's MoveSpeed, in cells per second. Used only to
        /// normalise movement into the "1.0 = healthy human" convention.
        /// </summary>
        public const float ReferenceMoveSpeed = 4.6f;

        /// <summary>
        /// Projectile speed, in cells per second, that a Grandmaster treats as "baseline slow" --
        /// roughly a thrown grenade, doubled to express the mastery itself. Interception
        /// difficulty is measured against this, so it is the single knob that moves every
        /// projectile's difficulty at once. See Gm21ProjectileDefence.Difficulty.
        /// </summary>
        public const float ReferenceProjectileSpeed = 24f;

        // ---------------------------------------------------------------- the 99% rule

        /// <summary>
        /// Fraction of the remaining failure chance a Grandmaster still suffers, BEFORE the
        /// pawn's own composite scales it.
        ///
        /// Applied as  final = 1 - (1 - base) * retained,  never as a flat +99 points, for exactly
        /// the reason Shooting gives: a flat bonus erases weapon and situation differentiation.
        /// </summary>
        public const float FailureRetained = 0.01f;

        /// <summary>Floor on any retained-failure fraction, so nothing can reach a true 0/1.</summary>
        public const float MinRetained = 0.0005f;

        // ---------------------------------------------------------------- gates

        /// <summary>Legitimate stored Melee 21. Reads levelInt, so aptitude cannot grant or remove it.</summary>
        public static bool IsMeleeGrandmaster(Pawn pawn)
        {
            return Gm21.IsGrandmaster(pawn, SkillDefOf.Melee);
        }

        /// <summary>
        /// Can this pawn take a deliberate martial ACTION at all -- attack, parry, riposte,
        /// intercept, deflect?
        ///
        /// This is the hard gate the brief demands: grandmastery is skill, not telekinesis. A
        /// downed, dead, unconscious, asleep or stunned pawn does none of it, no matter what their
        /// skill level says. Everything softer than this is expressed as a composite below 1
        /// rather than as a gate, so injury degrades the Grandmaster instead of switching them off.
        /// </summary>
        public static bool CanAct(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || !pawn.Spawned) return false;

            Pawn_HealthTracker health = pawn.health;
            if (health == null || health.capacities == null) return false;
            if (health.Downed) return false;
            if (!health.capacities.CapableOf(PawnCapacityDefOf.Consciousness)) return false;
            if (!RestUtility.Awake(pawn)) return false;

            Pawn_StanceTracker stances = pawn.stances;
            if (stances != null && stances.stunner != null && stances.stunner.Stunned) return false;

            return true;
        }

        /// <summary>The full gate for every passive melee feature.</summary>
        public static bool IsActiveGrandmaster(Pawn pawn)
        {
            return PassiveEnabled && IsMeleeGrandmaster(pawn) && CanAct(pawn);
        }

        // ---------------------------------------------------------------- capacity readers

        public static float Capacity(Pawn pawn, PawnCapacityDef cap)
        {
            if (pawn == null || cap == null) return 0f;
            Pawn_HealthTracker health = pawn.health;
            if (health == null || health.capacities == null) return 0f;
            float v = health.capacities.GetLevel(cap);
            return v < 0f ? 0f : v;
        }

        public static float Manipulation(Pawn pawn) { return Capacity(pawn, PawnCapacityDefOf.Manipulation); }
        public static float Sight(Pawn pawn) { return Capacity(pawn, PawnCapacityDefOf.Sight); }
        public static float Consciousness(Pawn pawn) { return Capacity(pawn, PawnCapacityDefOf.Consciousness); }
        public static float Moving(Pawn pawn) { return Capacity(pawn, PawnCapacityDefOf.Moving); }

        /// <summary>MoveSpeed normalised so a healthy human is 1.0. Cheap, and never negative.</summary>
        public static float MoveFactor(Pawn pawn)
        {
            if (pawn == null) return 0f;
            float speed = pawn.GetStatValue(StatDefOf.MoveSpeed);
            if (speed <= 0f) return 0f;
            return speed / ReferenceMoveSpeed;
        }

        /// <summary>Raw MoveSpeed in cells per second -- the interception curve's actual input.</summary>
        public static float MoveSpeed(Pawn pawn)
        {
            if (pawn == null) return 0f;
            float speed = pawn.GetStatValue(StatDefOf.MoveSpeed);
            return speed < 0f ? 0f : speed;
        }

        // ---------------------------------------------------------------- composites (1.0 = healthy human)

        /// <summary>
        /// PRECISION -- fine control of the weapon's point. Drives disarm, critical chance,
        /// projectile deflection and return-to-sender, and how well a pulled strike is pulled.
        ///
        /// Manipulation dominates because precision is a grip-and-wrist property; sight is a
        /// half-weighted multiplier because a blind duellist can still place a strike by feel,
        /// just not as well. Optionally amplified by a modded precision/finesse stat if one exists.
        /// </summary>
        public static float Precision(Pawn pawn)
        {
            if (pawn == null) return 0f;
            return Manipulation(pawn) * (0.5f + 0.5f * Sight(pawn)) * Gm21OptionalStats.PrecisionFactor(pawn);
        }

        /// <summary>
        /// AWARENESS -- reading stance, timing and openings. Drives defence-ignore and critical
        /// chance. Sight and consciousness only: this is perception and processing, not grip.
        /// Optionally amplified by a modded intelligence/combat-awareness stat.
        /// </summary>
        public static float Awareness(Pawn pawn)
        {
            if (pawn == null) return 0f;
            return Sight(pawn) * Consciousness(pawn) * Gm21OptionalStats.AwarenessFactor(pawn);
        }

        /// <summary>
        /// REACTION -- how fast the body answers what the mind saw. Drives both interception
        /// systems as a multiplier on raw movement speed, and gates riposte quality.
        /// </summary>
        public static float Reaction(Pawn pawn)
        {
            if (pawn == null) return 0f;
            return Consciousness(pawn) * (0.5f + 0.5f * Sight(pawn));
        }

        /// <summary>
        /// DEFENCE -- the composite behind parry/dodge. Consciousness gates it outright (an
        /// unaware pawn does not parry), and the rest is split between seeing it coming, meeting
        /// it with the weapon, and getting the body out of the way.
        ///
        /// The weights sum to 1.0, so a healthy human scores exactly 1.0 and every impairment
        /// subtracts its own share. A blind Grandmaster keeps 0.65; an armless one keeps 0.65; an
        /// immobilised one keeps 0.70. None of them are switched off -- they are simply worse.
        /// </summary>
        public static float Defence(Pawn pawn)
        {
            if (pawn == null) return 0f;
            float move = MoveFactor(pawn);
            if (move > 2f) move = 2f;  // extra speed helps defence, but not without limit
            return Consciousness(pawn)
                 * (0.35f * Sight(pawn) + 0.35f * Manipulation(pawn) + 0.30f * move)
                 * WeaponReadiness(pawn);
        }

        /// <summary>
        /// PHYSICAL POWER -- drives cleave, critical multiplier weighting and how far a deflected
        /// object is sent.
        ///
        /// MeleeDamageFactor is the vanilla stat that already aggregates "how hard does this pawn
        /// hit" across genes, hediffs and body type, so it is the honest vanilla stand-in for
        /// Strength. Body size scales it the way mass scales leverage. A modded Strength stat, if
        /// one is present, multiplies on top -- but nothing here requires one.
        /// </summary>
        public static float Power(Pawn pawn)
        {
            if (pawn == null) return 0f;
            float melee = pawn.GetStatValue(StatDefOf.MeleeDamageFactor);
            if (melee <= 0f) melee = 1f;
            float size = pawn.BodySize;
            if (size <= 0f) size = 1f;
            // Body size enters as a square root: a thrumbo is much heavier than a human but not
            // proportionally better at levering things.
            return melee * Mathf.Sqrt(size) * Gm21OptionalStats.StrengthFactor(pawn);
        }

        // ---------------------------------------------------------------- weapon reads

        /// <summary>The pawn's equipped weapon, or null. Never throws on a pawn with no tracker.</summary>
        public static ThingWithComps EquippedWeapon(Pawn pawn)
        {
            if (pawn == null) return null;
            Pawn_EquipmentTracker eq = pawn.equipment;
            return eq == null ? null : eq.Primary;
        }

        /// <summary>Mass of the equipped weapon in kg, 0 for an unarmed pawn.</summary>
        public static float WeaponMass(Pawn pawn)
        {
            ThingWithComps weapon = EquippedWeapon(pawn);
            if (weapon == null) return 0f;
            float mass = weapon.GetStatValue(StatDefOf.Mass);
            return mass < 0f ? 0f : mass;
        }

        /// <summary>
        /// Is there a weapon in hand that can be used to meet an incoming attack?
        ///
        /// A ranged weapon counts: a rifle held up to block a swing is worse than a sword but far
        /// better than nothing, which is why it scores between the two.
        /// </summary>
        public static float WeaponReadiness(Pawn pawn)
        {
            ThingWithComps weapon = EquippedWeapon(pawn);
            if (weapon == null || weapon.def == null) return 0.85f;      // trained bare hands
            if (weapon.def.IsMeleeWeapon) return 1f;
            if (weapon.def.IsWeapon) return 0.92f;                       // hafted/parried ranged weapon
            return 0.85f;
        }

        /// <summary>
        /// How well the pawn's current implement can meet a flying object: a long, heavy, rigid
        /// melee weapon is a far better interception surface than a bare hand.
        ///
        /// Mass buys leverage up to a point and then stops mattering, which is why it is capped.
        /// </summary>
        public static float DeflectionImplement(Pawn pawn)
        {
            ThingWithComps weapon = EquippedWeapon(pawn);
            if (weapon == null || weapon.def == null) return 0.35f;      // bare hands
            if (!weapon.def.IsWeapon) return 0.35f;

            float mass = weapon.GetStatValue(StatDefOf.Mass);
            if (mass < 0f) mass = 0f;
            float leverage = mass / 3f;
            if (leverage > 1f) leverage = 1f;

            float baseValue = weapon.def.IsMeleeWeapon ? 1f : 0.55f;
            return baseValue * (1f + 0.5f * leverage);
        }

        // ---------------------------------------------------------------- the 99% rule, stat-scaled

        /// <summary>
        /// The remaining-failure fraction for a composite. This is the single place the "remove
        /// ~99% of the remaining failure" rule is turned into a number.
        ///
        /// retained = FailureRetained / quality:  a healthy Grandmaster keeps 1% of the failure,
        /// a superhuman one keeps less, an impaired one keeps proportionally more. Clamped at both
        /// ends so nothing becomes a certainty and nothing becomes a division by zero.
        /// </summary>
        public static float RetainedFor(float quality)
        {
            if (quality <= 0f) return 1f;
            float retained = FailureRetained / quality;
            if (retained < MinRetained) return MinRetained;
            if (retained > 1f) return 1f;
            return retained;
        }

        /// <summary>Proportional failure removal applied to an already-final 0..1 chance.</summary>
        public static float Compensate(float baseChance, float quality)
        {
            if (baseChance >= 1f) return 1f;
            if (baseChance < 0f) baseChance = 0f;
            return 1f - (1f - baseChance) * RetainedFor(quality);
        }

        /// <summary>
        /// An opposed 0..1 chance built from a quality and a difficulty, both on the same scale.
        ///
        /// q / (q + k*d) is used rather than a curve because it has the properties this system
        /// needs everywhere: it is 0 when the pawn is incapable, it never reaches 1, it is smooth,
        /// and doubling the pawn's quality has a bigger effect against an easy target than a hard
        /// one -- which is exactly how skill against physics should behave.
        /// </summary>
        public static float Opposed(float quality, float difficulty, float hardness, float max)
        {
            if (quality <= 0f) return 0f;
            float opposition = difficulty * hardness;
            if (opposition <= 0f) return max;
            float p = quality / (quality + opposition);
            return p > max ? max : p;
        }
    }
}
