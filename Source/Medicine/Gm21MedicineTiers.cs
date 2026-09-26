using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RimWorld;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// Grandmaster Medicine Utilization: what a Medicine Grandmaster gets out of a given medicine.
    ///
    /// Nothing is keyed on vanilla DefNames. At startup every loaded ThingDef that vanilla itself
    /// counts as medicine (ThingDef.IsMedicine: MedicalPotency in its stat bases) is read once for
    /// its MedicalQualityMax -- the stat TendUtility.DoTend clamps tend quality to -- and the
    /// Grandmaster target for each medicine is computed and cached. Tending then costs one
    /// dictionary lookup. No foreign Def is modified.
    ///
    /// THE RULE, for a medicine whose ordinary cap is C:
    ///
    ///     required = C * MeaningfulTierMultiplier (1.30)
    ///     target   = the lowest distinct loaded cap >= required,
    ///                or C + TopTierFallbackBonus (0.30) if no loaded cap reaches it.
    ///
    /// Vanilla caps 0.70 / 1.00 / 1.30 give 1.00 / 1.30 / 1.60. With a bridge tier at 0.85 the
    /// herbal 0.70 still becomes 1.00 (0.85 is not >= 0.91), and the bridge itself becomes 1.30
    /// (1.00 is not >= 1.105). A genuinely qualifying modded tier -- say 1.15 -- is used.
    /// </summary>
    public static class Gm21MedicineTiers
    {
        private static readonly Dictionary<ThingDef, float> TargetByDef = new Dictionary<ThingDef, float>();
        private static readonly Dictionary<ThingDef, float> CapByDef = new Dictionary<ThingDef, float>();
        private static float[] distinctCaps = new float[0];

        /// <summary>True once the startup scan has run.</summary>
        public static bool Built { get; private set; }

        /// <summary>Distinct loaded caps, ascending. Read-only view for debug output and tests.</summary>
        public static IList<float> LoadedCaps
        {
            get { return Array.AsReadOnly(distinctCaps); }
        }

        // ---------------------------------------------------------------- pure algorithm

        /// <summary>
        /// Sorts and de-duplicates caps. NaN, infinite and non-positive values are dropped; two
        /// values within CapTolerance are one tier. Duplicates in the input therefore cannot matter.
        /// </summary>
        public static float[] DistinctAscending(IEnumerable<float> caps)
        {
            List<float> sorted = new List<float>();
            if (caps != null)
            {
                foreach (float c in caps)
                {
                    if (float.IsNaN(c) || float.IsInfinity(c) || c <= 0f) continue;
                    sorted.Add(c);
                }
            }
            sorted.Sort();
            List<float> distinct = new List<float>(sorted.Count);
            for (int i = 0; i < sorted.Count; i++)
            {
                if (distinct.Count == 0 || sorted[i] - distinct[distinct.Count - 1] > Gm21Medicine.CapTolerance)
                {
                    distinct.Add(sorted[i]);
                }
            }
            return distinct.ToArray();
        }

        /// <summary>
        /// The Grandmaster target for a medicine of ordinary cap <paramref name="cap"/>, given the
        /// distinct ascending loaded caps. Pure: no game state is read.
        /// </summary>
        public static float ResolveTarget(float cap, IList<float> distinctAscendingCaps)
        {
            float required = cap * Gm21Medicine.MeaningfulTierMultiplier;
            if (distinctAscendingCaps != null)
            {
                for (int i = 0; i < distinctAscendingCaps.Count; i++)
                {
                    // Ascending, so the first qualifying cap IS the lowest qualifying cap.
                    if (distinctAscendingCaps[i] >= required - Gm21Medicine.CapTolerance)
                    {
                        return distinctAscendingCaps[i];
                    }
                }
            }
            return cap + Gm21Medicine.TopTierFallbackBonus;
        }

        // ---------------------------------------------------------------- startup scan

        /// <summary>
        /// The one-time scan. Called from Gm21MedicineStartup, after every Def is loaded and
        /// resolved. Safe to call again (dev tooling); it simply rebuilds the cache.
        /// </summary>
        public static void Build()
        {
            TargetByDef.Clear();
            CapByDef.Clear();

            List<ThingDef> defs = DefDatabase<ThingDef>.AllDefsListForReading;
            List<float> raw = new List<float>();
            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef def = defs[i];
                float cap;
                if (!TryReadCap(def, out cap)) continue;
                CapByDef[def] = cap;
                raw.Add(cap);
            }

            distinctCaps = DistinctAscending(raw);
            foreach (KeyValuePair<ThingDef, float> kv in CapByDef)
            {
                TargetByDef[kv.Key] = ResolveTarget(kv.Value, distinctCaps);
            }
            Built = true;
        }

        private static bool TryReadCap(ThingDef def, out float cap)
        {
            cap = 0f;
            if (def == null || !def.IsMedicine) return false;
            try
            {
                cap = def.GetStatValueAbstract(StatDefOf.MedicalQualityMax);
            }
            catch (Exception e)
            {
                Log.Warning("[Grandmaster 21] Medicine 21 could not read MedicalQualityMax for "
                            + def.defName + "; it is ignored by Grandmaster medicine. " + e.Message);
                return false;
            }
            return !(float.IsNaN(cap) || float.IsInfinity(cap) || cap <= 0f);
        }

        // ---------------------------------------------------------------- runtime lookup

        /// <summary>
        /// The cached Grandmaster target for this medicine. O(1). A medicine Def that did not exist
        /// at startup (dev-added at runtime) is resolved against the startup table once and cached;
        /// the Def database is never rescanned.
        /// </summary>
        public static bool TryGetTarget(ThingDef medicineDef, out float target)
        {
            target = 0f;
            if (medicineDef == null) return false;
            if (TargetByDef.TryGetValue(medicineDef, out target)) return true;

            float cap;
            if (!TryReadCap(medicineDef, out cap)) return false;
            target = ResolveTarget(cap, distinctCaps);
            CapByDef[medicineDef] = cap;
            TargetByDef[medicineDef] = target;
            return true;
        }

        /// <summary>The ordinary (vanilla-rules) cap the scan recorded, for display.</summary>
        public static bool TryGetCap(ThingDef medicineDef, out float cap)
        {
            cap = 0f;
            return medicineDef != null && CapByDef.TryGetValue(medicineDef, out cap);
        }

        /// <summary>One line per medicine, for the startup log and the dev report.</summary>
        public static string Describe()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("loaded caps [");
            for (int i = 0; i < distinctCaps.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(Pct(distinctCaps[i]));
            }
            sb.Append("]");
            List<ThingDef> keys = new List<ThingDef>(TargetByDef.Keys);
            keys.Sort((a, b) => CapByDef[a].CompareTo(CapByDef[b]));
            for (int i = 0; i < keys.Count; i++)
            {
                sb.Append(i == 0 ? "  |  " : ", ");
                sb.Append(keys[i].defName).Append(' ')
                  .Append(Pct(CapByDef[keys[i]])).Append("->").Append(Pct(TargetByDef[keys[i]]));
            }
            return sb.ToString();
        }

        private static string Pct(float v)
        {
            return (v * 100f).ToString("0.#", CultureInfo.InvariantCulture) + "%";
        }
    }
}
