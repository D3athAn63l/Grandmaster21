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

        // Mobility tags. These are exactly the tags PawnCapacityWorker_Moving consumes
        // (verified in 1.6 IL), so scoring by them tracks the capacity we are trying to remove.
        private static BodyPartTagDef tagMovingCore;
        private static BodyPartTagDef tagMovingSegment;
        private static BodyPartTagDef tagMovingDigit;
        private static BodyPartTagDef tagPelvis;
        private static BodyPartTagDef tagSpine;

        // Manipulation tags, likewise from PawnCapacityWorker_Manipulation.
        private static BodyPartTagDef tagManipCore;
        private static BodyPartTagDef tagManipSegment;
        private static BodyPartTagDef tagManipDigit;

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
            tagPelvis = Tag("Pelvis");
            tagSpine = Tag("Spine");
            tagManipCore = Tag("ManipulationLimbCore");
            tagManipSegment = Tag("ManipulationLimbSegment");
            tagManipDigit = Tag("ManipulationLimbDigit");
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
        /// ADDITIVE ACCESSOR, added for the Melee Grandmaster package. Nothing above or below it
        /// changed: every existing method here still behaves exactly as it did for Shooting, which
        /// is a hard requirement of the melee work -- the shooting capstone has already passed
        /// runtime validation and must not be perturbed.
        ///
        /// Melee needs the same "is this part vital?" question Killer and Downed already answer
        /// internally, because a melee Grandmaster picks a finishing blow with knowledge a bullet
        /// does not have (see Gm21MeleeTargeting). Exposing the existing predicate is strictly
        /// cheaper and safer than duplicating the tag table in a second file, where the two copies
        /// would drift.
        /// </summary>
        internal static bool IsVitalPart(BodyPartRecord part)
        {
            Resolve();
            return IsVital(part);
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

        // Less-lethal scoring ladder, highest first. The exact numbers do not matter; the
        // ORDER does, and keeping them as named constants makes the ladder readable at a glance.
        private const int ScoreMobilityCore      = 1000; // a leg
        private const int ScoreMobilitySegment   =  900; // a foot
        private const int ScoreMobilityDigit     =  800; // a toe
        private const int ScoreManipulationCore  =  700; // an arm
        private const int ScoreManipulationSeg   =  600; // a hand
        private const int ScoreManipulationDigit =  500; // a finger
        private const int ScoreExternalDistal    =  300; // ear, nose, tail, horn -- an external extremity
        private const int ScoreExternalOther     =  200; // any other external non-vital part
        private const int ScoreInternalMobility  =  150; // pelvis / spine -- see below
        private const int ScoreInternalOther     =  100; // internal, non-vital, nothing else left

        /// <summary>
        /// DOWNED -- least-lethal useful targeting.
        ///
        /// This is not "aim at the legs". It is: of everything still attached to this creature,
        /// which single part most reduces its ability to fight or flee while being least likely to
        /// kill it? That question is re-asked for EVERY projectile, because the answer changes the
        /// moment a limb comes off.
        ///
        /// THE LADDER
        ///   1. Mobility limbs      -- core &gt; segment &gt; digit. Stops the target leaving.
        ///   2. Manipulation limbs  -- core &gt; segment &gt; digit. Stops the target fighting back.
        ///   3. External extremities -- a leaf part with nothing hanging off it: ear, nose, tail,
        ///                              horn, digit, genitalia. Peripheral by construction, with no
        ///                              naming special cases and no anatomy assumptions.
        ///   4. Any other external non-vital part.
        ///   5. Pelvis / spine, then any other internal non-vital part.
        ///
        /// WHY INTERNAL PARTS RANK LAST, EVEN MOBILITY-CRITICAL ONES. A spine is non-vital and
        /// wrecks Moving, which by function alone would put it near the top. But a bullet aimed
        /// into the torso cavity can spill damage onto the parent part on its way, and the torso
        /// is where bleeding out happens. External anatomy carries no such risk, so anything on
        /// the outside is preferred to anything on the inside -- "least lethal" outranks "most
        /// disabling" whenever the two disagree. Pelvis and spine stay in the ladder as a late
        /// option rather than being excluded, because an otherwise stripped target still needs an
        /// answer that is not centre mass.
        ///
        /// Within a tier, the healthiest candidate wins: shooting a working leg removes more
        /// Moving than finishing off one that is already ruined, and it keeps the Grandmaster from
        /// wasting the rest of a burst on a limb that is nearly off anyway.
        ///
        /// WHAT IS EXCLUDED, AND WHY IT IS GENERIC: any part whose own subtree contains a vital
        /// organ. One rule, no name lists. On a human that removes the brain, the head that holds
        /// it, the neck, and the whole torso; on a six-legged modded creature with three hearts it
        /// removes exactly the parts wrapping those hearts. It is also what keeps Downed mode from
        /// ever choosing centre mass, which is the entire point.
        ///
        /// Returns null only when the creature has NO non-vital part left anywhere. The caller
        /// treats that as "there is no safe shot", not as "let vanilla pick".
        /// </summary>
        internal static BodyPartRecord ChooseIncapacitatingPart(Pawn target)
        {
            Resolve();
            Pawn_HealthTracker health = target.health;
            if (health == null || health.hediffSet == null) return null;

            HashSet<BodyPartRecord> offLimits = MarkVitalAncestors(health);

            BodyPartRecord best = null;
            int bestScore = 0;
            float bestHealth = 0f;

            foreach (BodyPartRecord part in health.hediffSet.GetNotMissingParts())
            {
                if (!IsTargetable(health, part)) continue;
                if (offLimits.Contains(part)) continue;

                int score = ScoreLessLethal(part);
                if (score <= 0) continue;

                float remaining = health.hediffSet.GetPartHealth(part);
                if (score > bestScore || (score == bestScore && remaining > bestHealth))
                {
                    best = part;
                    bestScore = score;
                    bestHealth = remaining;
                }
            }

            return best;
        }

        private static int ScoreLessLethal(BodyPartRecord part)
        {
            if (HasTag(part, tagMovingCore)) return ScoreMobilityCore;
            if (HasTag(part, tagMovingSegment)) return ScoreMobilitySegment;
            if (HasTag(part, tagMovingDigit)) return ScoreMobilityDigit;

            if (HasTag(part, tagManipCore)) return ScoreManipulationCore;
            if (HasTag(part, tagManipSegment)) return ScoreManipulationSeg;
            if (HasTag(part, tagManipDigit)) return ScoreManipulationDigit;

            if (part.depth == BodyPartDepth.Outside)
            {
                // A leaf part is an extremity by construction -- nothing is attached beyond it.
                bool leaf = part.parts == null || part.parts.Count == 0;
                return leaf ? ScoreExternalDistal : ScoreExternalOther;
            }

            // Internal, and only reached once the outside is exhausted.
            if (HasTag(part, tagPelvis) || HasTag(part, tagSpine)) return ScoreInternalMobility;
            return ScoreInternalOther;
        }

        /// <summary>
        /// Builds the off-limits set: every vital part, plus every ancestor that contains one.
        ///
        /// Walking UP from each vital part is O(vitals x depth); testing each candidate by walking
        /// DOWN would be O(parts x subtree), which for a torso means re-walking most of the body
        /// once per candidate. Since this now runs per projectile, the cheaper direction matters.
        ///
        /// The set is a reused [ThreadStatic] buffer, so a burst does not allocate one per shot.
        /// </summary>
        [System.ThreadStatic] private static HashSet<BodyPartRecord> offLimitsBuffer;

        private static HashSet<BodyPartRecord> MarkVitalAncestors(Pawn_HealthTracker health)
        {
            HashSet<BodyPartRecord> set = offLimitsBuffer;
            if (set == null) set = offLimitsBuffer = new HashSet<BodyPartRecord>();
            set.Clear();

            foreach (BodyPartRecord part in health.hediffSet.GetNotMissingParts())
            {
                if (!IsVital(part)) continue;
                for (BodyPartRecord p = part; p != null; p = p.parent)
                {
                    if (!set.Add(p)) break; // this chain is already marked all the way up
                }
            }
            return set;
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
