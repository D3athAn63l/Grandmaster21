using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// Melee strike placement. Builds on the shared anatomical chooser the Shooting package
    /// already uses -- identical behaviour, no duplicated tag table -- and adds the one thing a
    /// melee Grandmaster genuinely knows that a marksman does not.
    ///
    /// WHY MELEE CAN BE SMARTER THAN SHOOTING. A bullet is committed the instant it leaves the
    /// barrel: the shooter chooses an aim point and the anatomy that is there when it arrives is
    /// what it hits. A melee strike is guided all the way in. The Grandmaster knows how hard THIS
    /// blow is about to land, can see what is left of the target, and can pick the part that the
    /// blow will actually finish rather than merely the part that is most lethal in principle.
    /// That is the difference this file encodes, and it is why the expected damage of the specific
    /// strike is a parameter here and is not available at all on the shooting side.
    /// </summary>
    internal static class Gm21MeleeTargeting
    {
        /// <summary>
        /// KILLER -- prefer a vital organ this exact blow can destroy outright; otherwise fall
        /// back to the shared "most lethal anatomy" answer.
        ///
        /// The finisher search is what makes Killer melee different: a strike carrying 40 damage
        /// into a target whose heart has 8 points left goes to the heart, even though the shared
        /// chooser would rank the brain higher, because destroying the heart NOW ends the fight
        /// and a partial hit on a full-health brain does not.
        ///
        /// expectedDamage is the strike's real pre-armour damage including any critical
        /// multiplier. Armour may still reduce it, so a finisher is a strong preference rather
        /// than a promise -- which is correct: the Grandmaster reads anatomy, not armour plating.
        /// Between two viable finishers the one that is easiest to actually connect with wins.
        /// </summary>
        internal static BodyPartRecord ChooseLethalPart(Pawn target, float expectedDamage)
        {
            Pawn_HealthTracker health = target == null ? null : target.health;
            if (health == null || health.hediffSet == null) return null;

            if (expectedDamage > 0f)
            {
                BodyPartRecord finisher = null;
                float finisherCoverage = 0f;

                foreach (BodyPartRecord part in health.hediffSet.GetNotMissingParts())
                {
                    if (part == null || part.def == null) continue;
                    if (!Gm21BodyTargeting.IsVitalPart(part)) continue;

                    float remaining = health.hediffSet.GetPartHealth(part);
                    if (remaining <= 0f || remaining > expectedDamage) continue;

                    float coverage = part.coverageAbsWithChildren;
                    if (finisher == null || coverage > finisherCoverage)
                    {
                        finisher = part;
                        finisherCoverage = coverage;
                    }
                }

                if (finisher != null) return finisher;
            }

            return Gm21BodyTargeting.ChooseLethalPart(target);
        }

        /// <summary>
        /// DOWNED -- identical to the shooting ladder: mobility first, then manipulation, then
        /// non-vital external anatomy, never a vital organ or anything wrapping one.
        ///
        /// It is deliberately NOT re-derived here. The ladder is a statement about anatomy, not
        /// about the weapon delivering the damage, and two copies of it would drift. What melee
        /// adds on top is not a different target -- it is a different amount of force, which is
        /// Gm21MeleeDamage.PullStrike's job.
        /// </summary>
        internal static BodyPartRecord ChooseIncapacitatingPart(Pawn target)
        {
            return Gm21BodyTargeting.ChooseIncapacitatingPart(target);
        }
    }
}
