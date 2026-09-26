using System;
using System.Collections.Generic;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace Grandmaster21
{
    /// <summary>
    /// Medicine 21 startup, self-contained like Crafting 21's: it runs after every Def is loaded and
    /// resolved, performs the one medicine-tier scan, applies the Medicine patches under the mod's
    /// Harmony id, and registers the Medicine cleaner with "Prepare Save for Uninstall".
    /// One summary line is logged; nothing is logged per tend, per tick or per pawn.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class Gm21MedicineStartup
    {
        static Gm21MedicineStartup()
        {
            try
            {
                Gm21MedicineTiers.Build();
            }
            catch (Exception e)
            {
                Log.Error("[Grandmaster 21] Medicine 21: the medicine-tier scan failed; Grandmaster "
                          + "tending will fall back to vanilla this session. " + e);
            }

            Gm21MedicinePatches.Apply(new Harmony("ared.grandmaster21"));
            Gm21Uninstall.RegisterPawnCleaner(Gm21MedicineUninstall.CleanPawn);

            Log.Message("[Grandmaster 21] Medicine 21  |  tend=" + Gm21Medicine.TendEnabled
                        + " treatment=" + Gm21Medicine.TreatmentEnabled
                        + " recovery=" + Gm21Medicine.RecoveryEnabled
                        + " immunity=" + Gm21Medicine.ImmunityEnabled
                        + " surgery=" + Gm21Medicine.SurgeryEnabled
                        + "  |  " + (Gm21MedicineTiers.Built ? Gm21MedicineTiers.Describe() : "no medicine tiers"));
        }
    }

    /// <summary>
    /// Medicine's part of "Prepare Save for Uninstall", called once per collected pawn (animals and
    /// corpses' inner pawns included):
    ///
    ///   * every Grandmaster Treatment on the pawn's hediffs is dropped, so no gm21Treatment*
    ///     element is written into any hediff node;
    ///   * the Medicine mode is dropped, so no gm21MedicineMode element is written;
    ///   * Grandmaster Resuscitation Shock is removed (the pawn simply wakes), so no GM21 HediffDef
    ///     is left in any hediff list -- a save that must load without the mod cannot reference it;
    ///   * an intervention in progress or queued is ended, so no GM21_Medicine* JobDef is saved
    ///     into the pawn's job tracker -- a save that must load without the mod cannot reference
    ///     this mod's JobDefs.
    ///
    /// Returns the number of entries removed.
    /// </summary>
    internal static class Gm21MedicineUninstall
    {
        internal static int CleanPawn(Pawn pawn)
        {
            if (pawn == null) return 0;
            int cleared = 0;

            if (Gm21MedicineModeStore.Clear(pawn)) cleared++;

            if (pawn.health != null && pawn.health.hediffSet != null)
            {
                List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
                for (int i = 0; i < hediffs.Count; i++)
                {
                    if (Gm21TreatmentStore.Clear(hediffs[i])) cleared++;
                }
            }

            cleared += RemoveShock(pawn);

            cleared += EndInterventions(pawn);
            return cleared;
        }

        private static int RemoveShock(Pawn pawn)
        {
            HediffDef shock = Gm21MedicineDefOf.GM21_ResuscitationShock;
            if (shock == null || pawn.health == null || pawn.health.hediffSet == null) return 0;
            List<Hediff> found = new List<Hediff>();
            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (hediffs[i] != null && hediffs[i].def == shock) found.Add(hediffs[i]);
            }
            for (int i = 0; i < found.Count; i++) pawn.health.RemoveHediff(found[i]);
            return found.Count;
        }

        private static int EndInterventions(Pawn pawn)
        {
            Pawn_JobTracker jobs = pawn.jobs;
            if (jobs == null) return 0;
            int ended = 0;

            if (jobs.jobQueue != null)
            {
                List<Job> queued = new List<Job>();
                foreach (QueuedJob q in jobs.jobQueue)
                {
                    if (q != null && q.job != null && Gm21MedicineDefOf.IsIntervention(q.job.def)) queued.Add(q.job);
                }
                for (int i = 0; i < queued.Count; i++)
                {
                    jobs.jobQueue.Extract(queued[i]);
                    ended++;
                }
            }

            if (jobs.curJob != null && Gm21MedicineDefOf.IsIntervention(jobs.curJob.def))
            {
                jobs.EndCurrentJob(JobCondition.InterruptForced);
                ended++;
            }
            return ended;
        }
    }
}
