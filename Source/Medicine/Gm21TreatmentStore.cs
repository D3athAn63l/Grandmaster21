using System.Runtime.CompilerServices;
using RimWorld;
using Verse;

namespace Grandmaster21
{
    /// <summary>One condition's current Grandmaster regimen. Never more than one per Hediff.</summary>
    public sealed class Gm21Treatment
    {
        /// <summary>The effective tend quality the Grandmaster delivered (1.0 = 100%).</summary>
        public float quality;

        /// <summary>Game tick of the most recent Grandmaster tend. -1 if unknown (old save).</summary>
        public int appliedTick = -1;
    }

    /// <summary>What a treatment does for its condition, decided by mechanism, never by DefName.</summary>
    public enum Gm21TreatmentEffect : byte
    {
        /// <summary>Tended, but no supported secondary mechanism: the regimen is state only.</summary>
        None = 0,
        /// <summary>A Hediff_Injury: faster natural recovery of this injury.</summary>
        Recovery = 1,
        /// <summary>An immunizable disease that can actually build immunity: faster immunity gain.</summary>
        Immunity = 2
    }

    /// <summary>
    /// Grandmaster Treatment state, attached to the SPECIFIC Hediff that was treated.
    ///
    /// Same architecture as GrandmasterStore and the aim/doctrine stores: a ConditionalWeakTable
    /// keyed on the object that owns the state, persisted from a postfix on that object's own
    /// ExposeData. The regimen is written inside the hediff's own save node, so it follows the
    /// condition through caravans, world-pawn conversion, map transitions and corpses with no extra
    /// code, and disappears with the hediff. There is no global registry to prune and nothing can
    /// leak: a healed or removed condition is simply collected.
    ///
    /// REFRESH, NEVER STACK. Apply overwrites the one box; there is no count and no product of
    /// multipliers. A non-Grandmaster tend of the same Hediff clears it, because the regimen is the
    /// CURRENT treatment, not a permanent blessing.
    ///
    /// ACTIVE means the vanilla tend underneath is still in force (HediffComp_TendDuration has
    /// tendTicksLeft &gt; 0). Vanilla tending is left entirely intact; the Grandmaster regimen rides
    /// on it and lapses with it. A lapsed regimen has no effect and is not written to the save.
    /// </summary>
    public static class Gm21TreatmentStore
    {
        private static readonly ConditionalWeakTable<Hediff, Gm21Treatment> Table =
            new ConditionalWeakTable<Hediff, Gm21Treatment>();

        private static readonly ConditionalWeakTable<Hediff, Gm21Treatment>.CreateValueCallback Factory =
            _ => new Gm21Treatment();

        /// <summary>Starts or refreshes the regimen. Refreshing replaces quality; it never compounds.</summary>
        public static void Apply(Hediff hediff, float quality, int tick)
        {
            if (hediff == null || float.IsNaN(quality) || float.IsInfinity(quality) || quality <= 0f) return;
            Gm21Treatment t = Table.GetValue(hediff, Factory);
            t.quality = quality;
            t.appliedTick = tick;
        }

        /// <summary>Drops the regimen. Returns true if there was one.</summary>
        public static bool Clear(Hediff hediff)
        {
            if (hediff == null) return false;
            Gm21Treatment t;
            if (!Table.TryGetValue(hediff, out t)) return false;
            t.quality = 0f; // anything still holding the box sees "no regimen"
            Table.Remove(hediff);
            return true;
        }

        /// <summary>The stored regimen, active or not.</summary>
        public static bool TryGet(Hediff hediff, out Gm21Treatment treatment)
        {
            treatment = null;
            return hediff != null && Table.TryGetValue(hediff, out treatment) && treatment.quality > 0f;
        }

        /// <summary>The regimen, only while the vanilla tend underneath is still in force.</summary>
        public static bool TryGetActive(Hediff hediff, out Gm21Treatment treatment)
        {
            return TryGet(hediff, out treatment) && TendStillInForce(hediff);
        }

        /// <summary>
        /// Reads tendTicksLeft rather than IsTended: IsTended answers false whenever the program
        /// state is not Playing, which would make every regimen look inactive while saving.
        /// </summary>
        public static bool TendStillInForce(Hediff hediff)
        {
            HediffWithComps withComps = hediff as HediffWithComps;
            if (withComps == null) return false;
            HediffComp_TendDuration tend = withComps.TryGetComp<HediffComp_TendDuration>();
            return tend != null && tend.tendTicksLeft > 0;
        }

        /// <summary>Which secondary mechanism this condition supports. Unknown shapes get None.</summary>
        public static Gm21TreatmentEffect EffectFor(Hediff hediff)
        {
            if (hediff == null || hediff.def == null) return Gm21TreatmentEffect.None;
            if (hediff is Hediff_Injury) return Gm21TreatmentEffect.Recovery;
            // Vanilla's own test, not mere presence of the comp: Asthma, Alzheimer's and heart artery
            // blockage carry HediffCompProperties_Immunizable only to drive severity and never build
            // immunity, so promising faster immunity for them would be false.
            if (hediff.def.PossibleToDevelopImmunityNaturally()) return Gm21TreatmentEffect.Immunity;
            return Gm21TreatmentEffect.None;
        }

        /// <summary>
        /// The active multiplier for this specific condition, or false if it has none. The hot-path
        /// entry point: an untreated condition costs one weak-table miss.
        /// </summary>
        public static bool TryGetRecoveryMultiplier(Hediff hediff, out float multiplier)
        {
            multiplier = 1f;
            Gm21Treatment t;
            if (!TryGetActive(hediff, out t)) return false;
            multiplier = Gm21Medicine.RecoveryMultiplierFor(t.quality);
            return multiplier > 1f;
        }

        // ---------------------------------------------------------------- persistence

        /// <summary>
        /// Postfix body for Hediff.ExposeData. Every Hediff subclass calls base.ExposeData(), so this
        /// runs for all of them and writes two values into the hediff's own node:
        ///
        ///     &lt;gm21TreatmentQuality&gt;1.3&lt;/gm21TreatmentQuality&gt;
        ///     &lt;gm21TreatmentTick&gt;123456&lt;/gm21TreatmentTick&gt;
        ///
        /// Only an ACTIVE regimen is written, with defaults that are omitted, so every other hediff
        /// in the save is byte-for-byte unchanged. Without the mod RimWorld ignores both elements.
        /// </summary>
        internal static void ExposeTreatment(Hediff hediff)
        {
            float quality = 0f;
            int tick = -1;
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                Gm21Treatment t;
                if (!TryGetActive(hediff, out t)) return;
                quality = t.quality;
                tick = t.appliedTick;
            }
            else if (Scribe.mode != LoadSaveMode.LoadingVars)
            {
                return;
            }

            Scribe_Values.Look(ref quality, "gm21TreatmentQuality", 0f);
            Scribe_Values.Look(ref tick, "gm21TreatmentTick", -1);

            if (Scribe.mode == LoadSaveMode.LoadingVars && quality > 0f)
            {
                Apply(hediff, quality, tick);
            }
        }
    }
}
