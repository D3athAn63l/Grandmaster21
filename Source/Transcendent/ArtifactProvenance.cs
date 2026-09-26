using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Grandmaster21.Transcendent
{
    internal static class ArtifactProvenance
    {
        internal static Dictionary<Hediff, float> Snapshot(Pawn target)
        {
            try { return target.health.hediffSet.hediffs.OfType<Hediff_Injury>().ToDictionary(h => (Hediff)h, h => h.Severity); }
            catch (Exception ex) { Log.ErrorOnce("[Grandmaster 21] Injury snapshot unavailable: " + ex, 213713); return null; }
        }
        internal static List<Hediff> Affected(IEnumerable<Hediff> current, DamageWorker.DamageResult result, Dictionary<Hediff, float> before)
        {
            var injuries = result.hediffs == null ? new List<Hediff>() : result.hediffs.Where(h => h is Hediff_Injury).ToList();
            // Native AddHediff may merge the newly returned wound into an existing one.
            if (before != null) foreach (Hediff_Injury wound in current.OfType<Hediff_Injury>())
                if (before.TryGetValue(wound, out float severity) && wound.Severity > severity
                    && injuries.Any(h => h.def == wound.def && h.Part == wound.Part)) injuries.Add(wound);
            return injuries.Distinct().ToList();
        }
        internal static void Tag(IEnumerable<Hediff> injuries, ThingDef weapon, string label)
        {
            foreach (Hediff injury in injuries)
            {
                injury.sourceDef = weapon; // Metadata only: DamageInfo.Weapon stays null.
                injury.sourceLabel = label;
                injury.sourceHediffDef = null; injury.sourceToolLabel = null; injury.sourceBodyPartGroup = null;
            }
        }
        internal static void Record(CompArtifact artifact, Pawn wielder, Pawn target, DamageWorker.DamageResult result, Dictionary<Hediff, float> before)
        {
            if (!ArtifactEffects.PositiveFinite(result.totalDamageDealt)) return;
            // Presentation callbacks cannot abort subsequent damage, healing or regeneration.
            try
            {
                var injuries = Affected(target.health.hediffSet.hediffs, result, before);
                Tag(injuries, artifact.parent.def, ArtifactIdentity.Label(artifact.phenomenon));
                // Native log construction/grammar may draw random values. Presentation cannot consume gameplay RNG.
                Rand.PushState();
                try
                {
                    var log = new BattleLogEntry_DamageTaken(target, DefDatabase<RulePackDef>.GetNamed("GM21_Log_" + artifact.phenomenon), wielder);
                    Find.BattleLog.Add(log);
                    // Associate merged survivors too, retaining native Health-tab battle-log tooltips.
                    if (result.hediffs != null) foreach (Hediff injury in injuries.Distinct()) if (!result.hediffs.Contains(injury)) result.hediffs.Add(injury);
                    result.AssociateWithLog(log);
                }
                finally { Rand.PopState(); }
            }
            catch (Exception ex) { Log.ErrorOnce("[Grandmaster 21] Artifact injury/log presentation failed: " + ex, 213710); }
        }
        internal static void Status(Pawn wielder, Pawn target, string rule)
        {
            try
            {
                Rand.PushState();
                try { Find.BattleLog.Add(new BattleLogEntry_Event(target, DefDatabase<RulePackDef>.GetNamed(rule), wielder)); }
                finally { Rand.PopState(); }
            }
            catch (Exception ex) { Log.ErrorOnce("[Grandmaster 21] Artifact status log failed: " + ex, 213711); }
        }
    }
}
