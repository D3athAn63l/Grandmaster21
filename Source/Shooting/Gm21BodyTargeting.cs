using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// Generic anatomical shot placement for Killer and Downed modes.
    ///
    /// WHY TAGS AND NOT PART NAMES: RimWorld ships humanlikes, animals and mechanoids, and mods
    /// add races with multiple hearts, no head, six legs or anatomy no one anticipated. Hardcoding
    /// "Brain"/"Heart"/"LeftLeg" would work for colonists and crash or silently no-op for
    /// everything else. BodyPartTagDefs are how RimWorld itself expresses "this part is what makes
    /// the creature conscious / pumps its blood / moves it", and every body def uses them, so
    /// scoring by tag works on anatomy this mod has never seen.
    ///
    /// Tags are resolved by NAME through GetNamedSilentFail rather than a DefOf: a missing tag
    /// becomes null and is skipped, instead of throwing during static init.
    ///
    /// COST: nothing here runs for an ordinary shooter. It is reached only on an actual hit from a
    /// Grandmaster who is in Killer or Downed mode. No LINQ, no allocation beyond the enumerator
    /// vanilla already hands back.
    /// </summary>
    internal static class Gm21BodyTargeting
    {
        private static bool resolved;

        // Lethal-priority tags.
        private static BodyPartTagDef tagConsciousness;   // brain
        private static BodyPartTagDef tagBreathingPathway; // neck / trachea
        private static BodyPartTagDef tagBloodPumping;    // heart
        private static BodyPartTagDef tagBreathing;       // lungs
        private static BodyPartTagDef tagBloodFiltration; // liver / kidney

        // Mobility tags.
        private static BodyPartTagDef tagMovingCore;
        private static BodyPartTagDef tagMovingSegment;
        private static BodyPartTagDef tagMovingDigit;

        private static void Resolve()
        {
            if (resolved) return;
            resolved = true;
            tagConsciousness = Tag("ConsciousnessSource");
            tagBreathingPathway = Tag("BreathingPathway");
            tagBloodPumping = Tag("BloodPumpingSource");
            tagBreathing = Tag("BreathingSource");
            tagBloodFiltration = Tag("BloodFiltrationSource");
            tagMovingCore = Tag("MovingLimbCore");
            tagMovingSegment = Tag("MovingLimbSegment");
            tagMovingDigit = Tag("MovingLimbDigit");
        }

        private static BodyPartTagDef Tag(string defName)
        {
            return DefDatabase<BodyPartTagDef>.GetNamedSilentFail(defName);
        }

        private static bool HasTag(BodyPartRecord part, BodyPartTagDef tag)
        {
            if (tag == null || part == null || part.def == null) return false;
            List<BodyPartTagDef> tags = part.def.tags;
            if (tags == null) return false;
            for (int i = 0; i < tags.Count; i++)
            {
                if (tags[i] == tag) return true;
            }
            return false;
        }

        private static bool IsVital(BodyPartRecord part)
        {
            return HasTag(part, tagConsciousness)
                || HasTag(part, tagBloodPumping)
                || HasTag(part, tagBreathing)
                || HasTag(part, tagBreathingPathway)
                || HasTag(part, tagBloodFiltration);
        }

        /// <summary>
        /// KILLER -- the anatomy whose destruction kills fastest.
        ///
        /// Priority follows the design brief: brain, then the part containing the brain (the
        /// "head", derived structurally rather than by name so it works on any body), then the
        /// breathing pathway (neck), then the blood pump, then other critical organs.
        ///
        /// Returns null if the creature has no meaningful vital target -- some mechanoids and
        /// modded creatures genuinely do not -- and the caller then leaves vanilla resolution
        /// alone. This is the "do not crash because a modded creature lacks a brain" path.
        /// </summary>
        internal static BodyPartRecord ChooseLethalPart(Pawn target)
        {
            Resolve();
            Pawn_HealthTracker health = target.health;
            if (health == null || health.hediffSet == null) return null;

            // The "head" is whatever contains the consciousness source. Structural, not by name.
            BodyPartRecord brainContainer = null;
            foreach (BodyPartRecord part in health.hediffSet.GetNotMissingParts())
            {
                if (HasTag(part, tagConsciousness)) { brainContainer = part.parent; break; }
            }

            BodyPartRecord best = null;
            int bestScore = 0;
            float bestCoverage = 0f;

            foreach (BodyPartRecord part in health.hediffSet.GetNotMissingParts())
            {
                if (!IsTargetable(health, part)) continue;

                int score;
                if (HasTag(part, tagConsciousness)) score = 100;
                else if (brainContainer != null && part == brainContainer) score = 90;
                else if (HasTag(part, tagBreathingPathway)) score = 80;
                else if (HasTag(part, tagBloodPumping)) score = 70;
                else if (HasTag(part, tagBreathing)) score = 60;
                else if (HasTag(part, tagBloodFiltration)) score = 50;
                else continue;

                // Between equally lethal options, take the one that is easier to hit.
                float coverage = part.coverageAbsWithChildren;
                if (score > bestScore || (score == bestScore && coverage > bestCoverage))
                {
                    best = part;
                    bestScore = score;
                    bestCoverage = coverage;
                }
            }

            return best;
        }

        /// <summary>
        /// DOWNED -- the anatomy whose loss most reduces Moving, while avoiding vitals.
        ///
        /// Mobility is expressed by the MovingLimb* tags, so this works on bipeds, quadrupeds,
        /// insects and anything else that declares how it moves. Core limbs outrank segments,
        /// which outrank digits, because losing a core structure costs the most Moving.
        ///
        /// Vital parts are excluded outright: the point of Downed mode is a live captive. It is
        /// not a guarantee of survival -- blood loss, later untreated wounds and fire still kill
        /// normally -- it only means the Grandmaster is not aiming at anything immediately fatal.
        ///
        /// ADAPTIVE: missing parts and parts already at zero health are filtered every shot, so a
        /// Grandmaster never keeps firing into a leg that is already gone.
        /// </summary>
        internal static BodyPartRecord ChooseIncapacitatingPart(Pawn target)
        {
            Resolve();
            Pawn_HealthTracker health = target.health;
            if (health == null || health.hediffSet == null) return null;

            BodyPartRecord best = null;
            int bestScore = 0;
            float bestHealth = 0f;

            foreach (BodyPartRecord part in health.hediffSet.GetNotMissingParts())
            {
                if (!IsTargetable(health, part)) continue;
                if (IsVital(part)) continue;

                int score;
                if (HasTag(part, tagMovingCore)) score = 100;
                else if (HasTag(part, tagMovingSegment)) score = 70;
                else if (HasTag(part, tagMovingDigit)) score = 40;
                else continue;

                // Prefer the healthiest remaining mobility part: shooting the one that still works
                // takes more Moving away than finishing off one that is already ruined.
                float remaining = health.hediffSet.GetPartHealth(part);
                if (remaining <= 0f) continue;

                if (score > bestScore || (score == bestScore && remaining > bestHealth))
                {
                    best = part;
                    bestScore = score;
                    bestHealth = remaining;
                }
            }

            return best;
        }

        /// <summary>
        /// Shared viability test: the part must still exist and still have health to lose.
        /// GetNotMissingParts already excludes destroyed parts; this also skips parts that are
        /// present but already at zero.
        /// </summary>
        private static bool IsTargetable(Pawn_HealthTracker health, BodyPartRecord part)
        {
            if (part == null || part.def == null) return false;
            return health.hediffSet.GetPartHealth(part) > 0f;
        }
    }
}
