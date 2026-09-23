using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Grandmaster21.Transcendent
{
    public sealed partial class Building_MagicalWorkstation
    {
        // Deliberately session-only. A queued test override is consumed by the next commitment.
        private ArtifactTier? devTier;
        private ArtifactPhenomenon? devPhenomenon;
        internal static bool CanForce(ArtifactTier ceiling, ArtifactTier tier)
        {
            return (int)tier >= 0 && (int)tier <= 5 && TranscendentTierConfig.For(ceiling) != null
                && (ceiling == ArtifactTier.Divine || tier <= ceiling);
        }
        private void ApplyDeveloperOverride(TranscendentProject prepared)
        {
            if (Prefs.DevMode)
            {
                if (devTier.HasValue && CanForce(prepared.ceiling, devTier.Value)) prepared.finalTier = devTier.Value;
                prepared.phenomenon = prepared.finalTier == ArtifactTier.None ? ArtifactPhenomenon.None
                    : devPhenomenon.HasValue && ArtifactIdentity.Eligible(prepared.product, devPhenomenon.Value) ? devPhenomenon.Value
                    : ArtifactIdentity.Assign(prepared.product, prepared.finalTier, prepared.phenomenonSeed);
                Log.Message("[Grandmaster 21][DEV] Committed " + prepared.id + " ceiling=" + prepared.ceiling
                    + " final=" + prepared.finalTier + " phenomenon=" + prepared.phenomenon + " seed=" + prepared.phenomenonSeed);
            }
            devTier = null; devPhenomenon = null;
        }
        private IEnumerable<Gizmo> DeveloperGizmos()
        {
            if (!Prefs.DevMode) yield break;
            if (project == null && Config != null)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: Next result (" + (devTier.HasValue ? devTier.ToString() : "random") + ")",
                    action = () =>
                    {
                        var options = new List<FloatMenuOption> { new FloatMenuOption("Normal rolls", () => devTier = null) };
                        foreach (ArtifactTier tier in Enum.GetValues(typeof(ArtifactTier)))
                        {
                            ArtifactTier selected = tier;
                            if (CanForce(Config.ceiling, selected)) options.Add(new FloatMenuOption("Force " + selected, () => devTier = selected));
                        }
                        Find.WindowStack.Add(new FloatMenu(options));
                    }
                };
                yield return new Command_Action
                {
                    defaultLabel = "DEV: Next phenomenon",
                    action = () => Find.WindowStack.Add(new FloatMenu(PhenomenonOptions(p => devPhenomenon = p, true)))
                };
            }
            if (project != null)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: Inspect committed state",
                    action = () => Log.Message("[Grandmaster 21][DEV] " + project.id + " ceiling=" + project.ceiling
                        + " result=" + project.finalTier + " phenomenon=" + project.phenomenon + " seed=" + project.phenomenonSeed
                        + " work=" + project.completedWork + "/" + project.totalWork + " output=" + project.outputCreated + "/" + project.outputDelivered)
                };
                yield return new Command_Action
                {
                    defaultLabel = "DEV: Finish work",
                    defaultDesc = "Sets work complete. A legitimate Crafting Grandmaster must still deliver the result.",
                    action = () => { if (project.Valid && !project.faulted) project.completedWork = project.totalWork; }
                };
            }
        }
        internal static List<FloatMenuOption> PhenomenonOptions(Action<ArtifactPhenomenon?> select, bool random)
        {
            var options = new List<FloatMenuOption>();
            if (random) options.Add(new FloatMenuOption("Normal assignment", () => select(null)));
            foreach (ArtifactPhenomenon phenomenon in Enum.GetValues(typeof(ArtifactPhenomenon)))
            {
                ArtifactPhenomenon selected = phenomenon;
                options.Add(new FloatMenuOption(selected.ToString(), () => select(selected)));
            }
            return options;
        }
    }

    public sealed partial class CompArtifact
    {
        public override IEnumerable<Gizmo> CompGetGizmosExtra() { return DevGizmos(); }
        public override IEnumerable<Gizmo> CompGetWornGizmosExtra() { return DevGizmos(); }
        private IEnumerable<Gizmo> DevGizmos()
        {
            if (!Prefs.DevMode || tier == ArtifactTier.None) yield break;
            yield return new Command_Action
            {
                defaultLabel = "DEV: Artifact state",
                action = () => Log.Message("[Grandmaster 21][DEV] " + parent.GetUniqueLoadID() + " tier=" + tier + " phenomenon=" + phenomenon
                    + " seed=" + phenomenonSeed + " counter=" + procCounter + " cooldown=" + nextProcTick + " origin=" + projectId + " / " + initiatorName)
            };
            yield return new Command_Action
            {
                defaultLabel = "DEV: Set phenomenon",
                action = () => Find.WindowStack.Add(new FloatMenu(Building_MagicalWorkstation.PhenomenonOptions(p =>
                {
                    if (p.HasValue && ArtifactIdentity.Eligible(parent.def, p.Value)) phenomenon = p.Value;
                }, false)))
            };
            yield return new Command_Action
            {
                defaultLabel = "DEV: Trigger phenomenon",
                defaultDesc = "Requires an equipped weapon, a standing hostile pawn, and both pawns on the map.",
                action = () =>
                {
                    Pawn wielder = parent.ParentHolder is Pawn_EquipmentTracker equipment ? equipment.pawn : null;
                    if (wielder == null || !wielder.Spawned) return;
                    Find.Targeter.BeginTargeting(TargetingParameters.ForAttackAny(), target =>
                    {
                        if (target.Thing is Pawn victim) ArtifactEffects.TryTrigger(this, wielder, victim, true);
                    });
                }
            };
            if (tier == ArtifactTier.Anomaly)
                yield return new Command_Action { defaultLabel = "DEV: Manifest", action = () => ArtifactWatcher.Manifest(this) };
        }
    }
}
