using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Grandmaster21
{
    /// <summary>Why a condition is, or is not, something Grandmaster Cure will offer.</summary>
    public enum Gm21CureVerdict : byte
    {
        Candidate = 0,
        Invalid,
        NotVisible,
        NotBad,
        NotCurableByItem,
        Injury,
        MissingPart,
        AddedPartOrImplant,
        ChemicalDependency,
        Reproduction,
        Supernatural,
        CustomClass,
        NoPathologySignal
    }

    /// <summary>
    /// Which conditions Grandmaster Cure may address. The UI only ever displays this list; the job
    /// re-validates against it before and after the work.
    ///
    /// PHILOSOPHY: false negatives over dangerous false positives. Cure is not a save editor and
    /// isBad is not a synonym for "disease". A condition must pass every structural exclusion AND
    /// show a positive pathology signal that vanilla itself defines:
    ///
    /// EXCLUDED (routed elsewhere or never touched):
    ///   * hidden conditions -- the doctor cannot treat what nobody has diagnosed;
    ///   * !isBad, and !everCurableByItem -- vanilla's own "no item may cure this" flag;
    ///   * Hediff_Injury -- ordinary Grandmaster tending handles wounds;
    ///   * Hediff_MissingPart -- Reconstruct handles missing anatomy;
    ///   * Hediff_Implant / Hediff_AddedPart / countsAsAddedPartOrImplant -- bionics, prosthetics
    ///     and implants are not pathology;
    ///   * addictions and other chemical dependencies;
    ///   * pregnancy, labour and reproduction states;
    ///   * anything defined by the Anomaly content pack -- supernatural states are not medicine;
    ///   * any custom Hediff subclass -- if a mod gave a condition its own class, GM21 cannot know
    ///     what removing it means, so it is omitted rather than guessed at.
    ///
    /// POSITIVE SIGNAL (at least one):
    ///   * immunizable disease machinery -- the def carries HediffCompProperties_Immunizable
    ///     (flu, plague, malaria, sleeping sickness, wound infection, toxic buildup, infant illness,
    ///     mechanites and modded equivalents);
    ///   * HediffDef.chronic -- vanilla's own chronic-illness flag (the same flag vanilla's
    ///     biosculpter and mutant logic key on);
    ///   * a tendable sickness -- the def makes the patient feel sick (makesSickThought) AND is
    ///     treated through vanilla tending (HediffCompProperties_TendDuration). In vanilla 1.6 that
    ///     is exactly lung rot, blood rot, gut worms and muscle parasites; requiring both keeps out
    ///     "sickness" states that are really game mechanics (cryptosleep, biosculpting, deathrest,
    ///     morning sickness, stillbirth), none of which are tendable;
    ///   * a vanilla-specific adapter, for vanilla conditions whose mechanics cannot be inferred
    ///     safely from generic comps. Currently exactly one: Food Poisoning, which is deliberately
    ///     not a tendable, immunizable disease and would otherwise be invisible to both signals.
    ///
    /// Everything else -- environmental states, psychic effects, quest markers, sicknesses that are
    /// really game-mechanic cooldowns, unknown modded states -- is simply not offered.
    /// </summary>
    public static class Gm21CureCandidates
    {
        /// <summary>The Cure menu for this patient. Never null.</summary>
        public static List<Hediff> GetCureCandidates(Pawn patient)
        {
            List<Hediff> result = new List<Hediff>();
            if (patient == null || patient.health == null || patient.health.hediffSet == null) return result;
            List<Hediff> hediffs = patient.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (Classify(hediffs[i]) == Gm21CureVerdict.Candidate) result.Add(hediffs[i]);
            }
            return result;
        }

        public static bool IsCureCandidate(Hediff hediff)
        {
            return Classify(hediff) == Gm21CureVerdict.Candidate;
        }

        /// <summary>The whole decision, in order. Pure over the Hediff and its Def.</summary>
        public static Gm21CureVerdict Classify(Hediff hediff)
        {
            if (hediff == null || hediff.def == null) return Gm21CureVerdict.Invalid;
            HediffDef def = hediff.def;

            if (!hediff.Visible) return Gm21CureVerdict.NotVisible;
            if (!def.isBad) return Gm21CureVerdict.NotBad;
            if (!def.everCurableByItem) return Gm21CureVerdict.NotCurableByItem;

            if (hediff is Hediff_Injury) return Gm21CureVerdict.Injury;
            if (hediff is Hediff_MissingPart) return Gm21CureVerdict.MissingPart;
            if (hediff is Hediff_Implant || def.countsAsAddedPartOrImplant) return Gm21CureVerdict.AddedPartOrImplant;
            if (hediff is Hediff_Addiction || def.IsAddiction || def.chemicalNeed != null)
                return Gm21CureVerdict.ChemicalDependency;
            if (def.pregnant || hediff is Hediff_Pregnant || hediff is Hediff_Labor || hediff is Hediff_LaborPushing)
                return Gm21CureVerdict.Reproduction;
            if (IsSupernatural(def)) return Gm21CureVerdict.Supernatural;

            if (IsVanillaAdapterCondition(def)) return Gm21CureVerdict.Candidate;

            System.Type type = hediff.GetType();
            if (type != typeof(Hediff) && type != typeof(HediffWithComps)) return Gm21CureVerdict.CustomClass;

            if (def.CompProps<HediffCompProperties_Immunizable>() != null) return Gm21CureVerdict.Candidate;
            if (def.chronic) return Gm21CureVerdict.Candidate;
            if (def.makesSickThought && def.CompProps<HediffCompProperties_TendDuration>() != null)
                return Gm21CureVerdict.Candidate;

            return Gm21CureVerdict.NoPathologySignal;
        }

        /// <summary>
        /// Narrow vanilla adapters. Identity is the vanilla DefOf reference, not a string, and the
        /// list is deliberately tiny: each entry is a vanilla condition whose unique mechanics make
        /// it pathology even though no generic signal says so.
        /// </summary>
        public static bool IsVanillaAdapterCondition(HediffDef def)
        {
            return def != null && def == HediffDefOf.FoodPoisoning;
        }

        private static bool IsSupernatural(HediffDef def)
        {
            ModContentPack pack = def.modContentPack;
            return pack != null && pack.PackageId != null
                && string.Equals(pack.PackageId, ModContentPack.AnomalyModPackageId, System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Removes the selected condition. Uses vanilla's own semantics: if the def says a cure by
        /// item removes every instance at once (cureAllAtOnceIfCuredByItem), the other instances of
        /// the SAME condition go too -- nothing else on the patient is touched.
        /// </summary>
        public static void ApplyCure(Pawn patient, Hediff hediff)
        {
            HediffDef def = hediff.def;
            patient.health.RemoveHediff(hediff);
            if (!def.cureAllAtOnceIfCuredByItem) return;
            for (int guard = 0; guard < 1000; guard++)
            {
                Hediff other = patient.health.hediffSet.GetFirstHediffOfDef(def);
                if (other == null) return;
                patient.health.RemoveHediff(other);
            }
        }
    }
}
