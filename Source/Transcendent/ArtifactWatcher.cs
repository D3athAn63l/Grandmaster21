using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Grandmaster21.Transcendent
{
    // Non-pawn placeholder manifestation. Registry rebuilt from spawn/equip events, not saved references.
    public sealed class ArtifactWatcher : MapComponent
    {
        private readonly Queue<CompArtifact> queue = new Queue<CompArtifact>();
        private readonly HashSet<CompArtifact> registered = new HashSet<CompArtifact>();
        internal const int CheckInterval = 250, Budget = 8, MinimumDelay = 30000;
        public ArtifactWatcher(Map map) : base(map) { }
        internal static void Track(CompArtifact artifact)
        {
            if (artifact == null || artifact.tier != ArtifactTier.Anomaly || artifact.parent == null || artifact.parent.Destroyed) return;
            Map map = artifact.parent.MapHeld;
            if (map == null) return;
            ArtifactWatcher watcher = map.GetComponent<ArtifactWatcher>();
            if (watcher == null) return;
            if (watcher.registered.Add(artifact)) watcher.queue.Enqueue(artifact);
        }
        internal static void TrackPawn(Pawn pawn)
        {
            if (pawn == null) return;
            if (pawn.equipment != null) foreach (Thing thing in pawn.equipment.AllEquipmentListForReading) Track(thing.TryGetComp<CompArtifact>());
            if (pawn.apparel != null) foreach (Thing thing in pawn.apparel.WornApparel) Track(thing.TryGetComp<CompArtifact>());
            if (pawn.inventory != null) foreach (Thing thing in pawn.inventory.innerContainer) Track(thing.TryGetComp<CompArtifact>());
        }
        public override void FinalizeInit()
        {
            // One load-time walk also finds equipment loaded before the map component was initialized.
            foreach (Thing thing in map.listerThings.AllThings)
            {
                Track(thing.TryGetComp<CompArtifact>());
                TrackPawn(thing as Pawn);
            }
        }
        public override void MapComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % CheckInterval != 0) return;
            int count = Math.Min(Budget, queue.Count);
            for (int i = 0; i < count; i++)
            {
                CompArtifact artifact = queue.Dequeue();
                if (artifact.parent == null || artifact.parent.Destroyed || artifact.tier != ArtifactTier.Anomaly || artifact.parent.MapHeld != map)
                {
                    registered.Remove(artifact);
                    Track(artifact); // Transfer to another loaded map if appropriate.
                    continue;
                }
                queue.Enqueue(artifact);
                if (now < artifact.nextWatcherTick) continue;
                bool first = artifact.nextWatcherTick == 0;
                artifact.nextWatcherTick = now + MinimumDelay + (int)(ArtifactRolls.Unit(artifact.phenomenonSeed, now) * MinimumDelay);
                if (!first) Manifest(artifact);
            }
        }
        internal static bool Manifest(CompArtifact artifact)
        {
            Thing thing = artifact.parent;
            if (thing == null || thing.Destroyed || thing.MapHeld == null || artifact.tier != ArtifactTier.Anomaly) return false;
            IntVec3 position = thing.PositionHeld;
            Map map = thing.MapHeld;
            if (!position.InBounds(map) || !map.areaManager.Home[position]) return false;
            try { FleckMaker.Static(position.ToVector3Shifted(), map, FleckDefOf.Smoke, 1.8f); }
            catch (Exception ex) { Log.ErrorOnce("[Grandmaster 21] Artifact manifestation unavailable: " + ex.Message, 213703); }
            return true;
        }
    }

    public sealed partial class CompArtifact
    {
        public override void PostSpawnSetup(bool respawningAfterLoad) { base.PostSpawnSetup(respawningAfterLoad); ArtifactWatcher.Track(this); }
        public override void Notify_Equipped(Pawn pawn) { base.Notify_Equipped(pawn); ArtifactWatcher.Track(this); }
    }
}
