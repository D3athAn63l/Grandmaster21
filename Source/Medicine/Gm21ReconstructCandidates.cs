using System.Collections.Generic;
using Verse;

namespace Grandmaster21
{
    /// <summary>Why a missing part is, or is not, something Grandmaster Reconstruct will offer.</summary>
    public enum Gm21ReconstructVerdict : byte
    {
        Candidate = 0,
        Invalid,
        NotPresent,
        CustomClass,
        NoPart,
        CorePart,
        NotRestorable,
        NotTopmost,
        ReplacedByArtificialPart,
        OtherStateInSubtree
    }

    /// <summary>
    /// Which missing natural anatomy Grandmaster Reconstruct may restore.
    ///
    /// HOW RIMWORLD REPRESENTS IT (1.6). Destroying a part adds a Hediff_MissingPart to it, and that
    /// hediff's PostAdd adds one to every descendant too, so a lost arm is a missing shoulder, a
    /// missing arm, a missing hand and five missing fingers. Installing a bionic or prosthetic adds a
    /// Hediff_AddedPart whose PostAdd restores the part first, so the missing markers under it are
    /// gone. Vanilla's own HediffSet.GetMissingPartsCommonAncestors walks the body from the core
    /// part, stops at any part (or ancestor) with a directly added part, and returns only the
    /// topmost missing marker of each lost subtree -- the healer serum restores exactly those.
    ///
    /// A CANDIDATE here is one of those topmost markers, and additionally:
    ///   * its runtime class is exactly Hediff_MissingPart (a modded subclass may mean something else);
    ///   * it is not the core part, and its def is everCurableByItem;
    ///   * its parent is present -- restore the parent, never a child floating under a gap;
    ///   * no directly added part sits on it or any ancestor -- a bionic left arm means there is
    ///     no "reconstruct left arm", and no biological fingers inside a bionic hand;
    ///   * every hediff anywhere in its subtree is a missing-part marker (or a def vanilla keeps on
    ///     restoration). Vanilla's RestorePart deletes EVERYTHING in the subtree, so if anything else
    ///     is there -- an implant, an added part a mod allowed under a gap -- the part is not offered
    ///     rather than silently deleting it.
    ///
    /// Left/right, arm/leg and human anatomy are never named: it is BodyPartRecord structure only,
    /// so an animal's leg or a modded race's tail qualifies the same way.
    /// </summary>
    public static class Gm21ReconstructCandidates
    {
        /// <summary>The Reconstruct menu for this patient. Never null.</summary>
        public static List<Hediff_MissingPart> GetReconstructionCandidates(Pawn patient)
        {
            List<Hediff_MissingPart> result = new List<Hediff_MissingPart>();
            if (patient == null || patient.health == null || patient.health.hediffSet == null) return result;
            HediffSet set = patient.health.hediffSet;
            List<Hediff_MissingPart> topmost = set.GetMissingPartsCommonAncestors();
            for (int i = 0; i < topmost.Count; i++)
            {
                if (Classify(set, topmost[i]) == Gm21ReconstructVerdict.Candidate) result.Add(topmost[i]);
            }
            return result;
        }

        public static bool IsReconstructionCandidate(HediffSet set, Hediff hediff)
        {
            return Classify(set, hediff) == Gm21ReconstructVerdict.Candidate;
        }

        /// <summary>The whole decision, in order.</summary>
        public static Gm21ReconstructVerdict Classify(HediffSet set, Hediff hediff)
        {
            if (set == null || hediff == null || hediff.def == null) return Gm21ReconstructVerdict.Invalid;
            if (!set.hediffs.Contains(hediff)) return Gm21ReconstructVerdict.NotPresent;
            if (hediff.GetType() != typeof(Hediff_MissingPart)) return Gm21ReconstructVerdict.CustomClass;

            BodyPartRecord part = hediff.Part;
            if (part == null) return Gm21ReconstructVerdict.NoPart;
            if (part.parent == null) return Gm21ReconstructVerdict.CorePart;
            if (!hediff.def.everCurableByItem) return Gm21ReconstructVerdict.NotRestorable;
            if (set.PartIsMissing(part.parent)) return Gm21ReconstructVerdict.NotTopmost;
            if (set.PartOrAnyAncestorHasDirectlyAddedParts(part)) return Gm21ReconstructVerdict.ReplacedByArtificialPart;
            if (!SubtreeHoldsOnlyMissingMarkers(set, part)) return Gm21ReconstructVerdict.OtherStateInSubtree;
            return Gm21ReconstructVerdict.Candidate;
        }

        /// <summary>
        /// True when every hediff on this part and all its descendants is a missing-part marker or a
        /// def vanilla keeps on restoration -- i.e. RestorePart would delete nothing but the gap.
        /// </summary>
        public static bool SubtreeHoldsOnlyMissingMarkers(HediffSet set, BodyPartRecord part)
        {
            List<Hediff> hediffs = set.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff h = hediffs[i];
                if (h == null || h.Part == null) continue;
                if (h is Hediff_MissingPart || h.def.keepOnBodyPartRestoration) continue;
                if (IsSameOrDescendant(h.Part, part)) return false;
            }
            return true;
        }

        private static bool IsSameOrDescendant(BodyPartRecord candidate, BodyPartRecord root)
        {
            for (BodyPartRecord p = candidate; p != null; p = p.parent)
            {
                if (p == root) return true;
            }
            return false;
        }

        /// <summary>
        /// Restores the part through vanilla's own path -- Pawn_HealthTracker.RestorePart, the call
        /// vanilla's natural-part installation and healer serum use -- which removes the gap and
        /// every missing marker beneath it, then re-checks the pawn's health state.
        /// </summary>
        public static void ApplyReconstruction(Pawn patient, BodyPartRecord part)
        {
            patient.health.RestorePart(part);
        }
    }
}
