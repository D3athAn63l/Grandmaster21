using UnityEngine;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// Everything a Grandmaster decides about a strike that is already landing: where it lands,
    /// how hard, and -- uniquely to melee -- whether to hold some of it back.
    ///
    /// WHY HERE. Pawn.PreApplyDamage is the last point at which both the body part and the damage
    /// amount can still be changed, and it runs BEFORE armour. That ordering is the whole reason
    /// this is the right hook: a critical multiplies the blow and armour then resolves against the
    /// bigger number exactly as it would against any other big number. Nothing in this file
    /// bypasses, reduces or ignores armour, and nothing changes the damage TYPE -- a critical
    /// hammer blow is still blunt, a critical knife is still a cut.
    /// </summary>
    internal static class Gm21MeleeDamage
    {
        /// <summary>
        /// Fraction of a part's remaining health a perfectly pulled strike leaves intact, before
        /// precision scaling. Small on purpose: the goal is to stop just short of destroying the
        /// part, not to tickle it.
        /// </summary>
        private const float PullMargin = 0.15f;

        private const float MinPullMargin = 0.02f;   // a superb Grandmaster cuts it very fine
        private const float MaxPullMargin = 0.60f;   // a clumsy one has to hold back much more

        /// <summary>A pulled strike is still a strike; it never becomes literally nothing.</summary>
        private const float MinPulledDamage = 1f;

        internal static void Shape(Gm21MeleeFrame frame, Pawn victim, ref DamageInfo dinfo)
        {
            if (victim == null) return;

            DamageDef def = dinfo.Def;
            if (def == null || !def.harmsHealth) return;

            // This must be THIS strike, delivered by THIS attacker, against THIS target. A frame
            // is open for the duration of one melee attack, and other damage can be applied inside
            // that window -- a thorns hediff, a retaliation patch, damage propagating to a
            // bystander. None of it is the Grandmaster's blow and none of it gets shaped.
            if (dinfo.Instigator != frame.attacker) return;
            if (victim != frame.target) return;

            float amount = dinfo.Amount;
            if (amount <= 0f) return;

            // ---- critical strike -------------------------------------------------------
            if (Gm21Melee.ReactionsEnabled && !frame.critRolled)
            {
                frame.critRolled = true;
                float chance = Gm21Critical.Chance(frame.atkPrecision, frame.atkAwareness);
                if (chance > 0f && Rand.Chance(chance))
                {
                    frame.critMultiplier = Gm21Critical.RollMultiplier(frame.atkPower);
                }
            }

            if (frame.critMultiplier > 1f)
            {
                amount *= frame.critMultiplier;
            }

            // ---- doctrine: where the blow lands ---------------------------------------
            BodyPartRecord part = dinfo.HitPart;
            if (Gm21Melee.DoctrineEnabled
                && frame.doctrine != Gm21MeleeDoctrine.Normal
                && part == null)
            {
                BodyPartRecord chosen = frame.doctrine == Gm21MeleeDoctrine.Killer
                    ? Gm21MeleeTargeting.ChooseLethalPart(victim, amount)
                    : Gm21MeleeTargeting.ChooseIncapacitatingPart(victim);

                if (chosen != null)
                {
                    dinfo.SetHitPart(chosen);
                    part = chosen;
                }
                // null means this creature's anatomy offers nothing the doctrine wants -- a
                // mechanoid with no vitals for Killer, or a target stripped down to nothing but
                // vitals for Downed. Vanilla resolution then decides, which is the honest answer.
            }

            // ---- controlled force: the thing only melee can do ------------------------
            if (Gm21Melee.DoctrineEnabled && frame.doctrine == Gm21MeleeDoctrine.Downed)
            {
                amount = PullStrike(victim, part, amount, frame.atkPrecision);
            }

            if (amount != dinfo.Amount)
            {
                dinfo.SetAmount(amount);
            }
        }

        /// <summary>
        /// CONTROLLED FORCE -- the major distinction between Melee Downed and Shooting Downed.
        ///
        /// A bullet delivers whatever energy it was carrying; the marksman's only lever is where
        /// it lands. A blade or a haft is under continuous control, so a Grandmaster who wants a
        /// prisoner rather than a corpse can simply not swing all the way through. This is where
        /// that happens: the blow is capped just below what would destroy the part it is aimed at.
        ///
        /// WHY CAPPING AT THE PART, AND NOT AT SOME GLOBAL "SAFE" NUMBER. Destroying a limb is the
        /// single most lethal thing a less-lethal strike can accidentally do: the part is gone, the
        /// damage spills toward the parent, and the bleed rate jumps. Keeping the part attached but
        /// wrecked removes the capacity -- mobility, manipulation -- without any of that, and
        /// capacity loss is what actually downs a pawn.
        ///
        /// PRECISION DECIDES HOW FINE THE CUT IS, not whether it is safe. A superb Grandmaster
        /// stops within 2% of the part's remaining health, taking almost everything it has; a
        /// barely-capable one has to leave 60% and does correspondingly less to the target. Worse
        /// precision therefore means a LESS effective incapacitation, never a more dangerous one.
        ///
        /// THIS IS NOT IMMORTALITY. The target can still die -- of blood loss, of the wounds it
        /// already had, of infection, of the next blow, or because its anatomy left no safe option
        /// at all. It is substantially safer than the shooting equivalent, which is exactly what
        /// the brief asks for, and nothing more than that.
        /// </summary>
        internal static float PullStrike(Pawn victim, BodyPartRecord part, float amount, float precision)
        {
            if (part == null) return amount;

            Pawn_HealthTracker health = victim.health;
            if (health == null || health.hediffSet == null) return amount;

            float remaining = health.hediffSet.GetPartHealth(part);
            if (remaining <= 0f) return amount;

            float margin = precision <= 0f
                ? MaxPullMargin
                : Mathf.Clamp(PullMargin / precision, MinPullMargin, MaxPullMargin);

            float cap = remaining * (1f - margin);
            if (cap < MinPulledDamage) cap = MinPulledDamage;

            return amount <= cap ? amount : cap;
        }
    }
}
