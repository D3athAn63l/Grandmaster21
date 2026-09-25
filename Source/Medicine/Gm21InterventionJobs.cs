using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Grandmaster21
{
    [DefOf]
    public static class Gm21MedicineDefOf
    {
        public static JobDef GM21_MedicineCure;
        public static JobDef GM21_MedicineReconstruct;
        public static JobDef GM21_MedicineResuscitate;

        static Gm21MedicineDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(Gm21MedicineDefOf)); }

        public static bool IsIntervention(JobDef def)
        {
            return def != null && (def == GM21_MedicineCure || def == GM21_MedicineReconstruct
                                   || def == GM21_MedicineResuscitate);
        }
    }

    /// <summary>
    /// Makes the intervention jobs. The selected condition travels in Job.source, vanilla's own
    /// ILoadReferenceable slot: a Hediff is a deep-saved, cross-referenceable object (vanilla itself
    /// references hediffs through Scribe_References), so a job saved mid-intervention reloads
    /// pointing at the same condition. If the condition is gone, the reference resolves to null and
    /// the job fails cleanly.
    /// </summary>
    public static class Gm21InterventionJobs
    {
        public static Job MakeCure(Pawn patient, Hediff condition)
        {
            Job job = JobMaker.MakeJob(Gm21MedicineDefOf.GM21_MedicineCure, patient);
            job.source = condition;
            job.count = 1;
            return job;
        }

        public static Job MakeReconstruct(Pawn patient, Hediff_MissingPart missing)
        {
            Job job = JobMaker.MakeJob(Gm21MedicineDefOf.GM21_MedicineReconstruct, patient);
            job.source = missing;
            job.count = 1;
            return job;
        }

        public static Job MakeResuscitate(Corpse corpse)
        {
            Job job = JobMaker.MakeJob(Gm21MedicineDefOf.GM21_MedicineResuscitate, corpse);
            job.count = 1;
            return job;
        }
    }

    /// <summary>
    /// Shared shape of a Grandmaster intervention, modelled on JobDriver_TendPatient: the doctor
    /// owns the job and reserves the target, walks to it, performs timed medical work with the
    /// vanilla tend sound, progress bar and Medicine as the active skill, then applies the result in
    /// one final instant step.
    ///
    /// A standing patient is asked to hold still with vanilla's own PawnUtility.ForceWait -- the
    /// same thing a drafted tend does -- so the patient needs no custom job. A downed or bedded
    /// patient is simply treated where they lie.
    ///
    /// INTERRUPTION IS SAFE: nothing is applied until the final step, and the final step re-runs the
    /// full validation first. An interrupted intervention changes nothing and can be ordered again.
    /// Only a practising Medicine Grandmaster can hold one of these jobs; the check is repeated
    /// every tick of the work.
    /// </summary>
    public abstract class JobDriver_Gm21Intervention : JobDriver
    {
        private PathEndMode pathEndMode = PathEndMode.ClosestTouch;

        /// <summary>Tick the timed work began, or -1. Saved, so a reload mid-work keeps its clock.</summary>
        protected int workStartedTick = -1;

        /// <summary>This driver asked a standing patient to hold still, and must release them.</summary>
        private bool heldPatient;

        protected abstract int BaseWorkTicks { get; }

        /// <summary>Cheap per-tick check that the intervention still makes sense.</summary>
        protected abstract bool StillPlausible();

        /// <summary>The full validation, run before the work and again at the finish.</summary>
        protected abstract bool Validate(out string reason);

        /// <summary>Applies the result. Only called straight after a successful Validate.</summary>
        protected abstract void Complete();

        protected Pawn Patient
        {
            get { return job.targetA.Thing as Pawn; }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref pathEndMode, "pathEndMode", PathEndMode.ClosestTouch);
            Scribe_Values.Look(ref workStartedTick, "gm21WorkStartedTick", -1);
            Scribe_Values.Look(ref heldPatient, "gm21HeldPatient", false);
        }

        public override void Notify_Starting()
        {
            base.Notify_Starting();
            Pawn patient = Patient;
            pathEndMode = (patient != null && patient.InBed()) ? PathEndMode.InteractionCell
                : (patient != null ? PathEndMode.ClosestTouch : PathEndMode.Touch);
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            string reason;
            if (!Gm21Medicine.CanPractise(pawn) || !Validate(out reason)) return false;
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => !Gm21Medicine.CanPractise(pawn) || !StillPlausible());

            yield return Toils_Goto.GotoThing(TargetIndex.A, pathEndMode);

            int ticks = Gm21Medicine.WorkTicks(pawn, BaseWorkTicks);
            Toil work = Toils_General.Wait(ticks, TargetIndex.None);
            work.AddPreInitAction(delegate
            {
                string reason;
                if (!Validate(out reason))
                {
                    Reject(reason);
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                if (workStartedTick < 0) workStartedTick = Find.TickManager.TicksGame;
                HoldPatientStill(ticks);
            });
            work.WithProgressBarToilDelay(TargetIndex.A).PlaySustainerOrSound(SoundDefOf.Interact_Tend);
            work.activeSkill = () => SkillDefOf.Medicine;
            work.handlingFacing = true;
            work.tickIntervalAction = delegate(int delta)
            {
                pawn.rotationTracker.FaceTarget(job.targetA);
            };
            work.FailOn(() => !pawn.CanReachImmediate(job.targetA, pathEndMode));
            // Same release vanilla's drafted tend performs: whether the work completes or is
            // interrupted, a patient we asked to hold still is not left standing there.
            work.AddFinishAction(ReleasePatient);
            yield return work;

            yield return Toils_General.Do(delegate
            {
                string reason = null;
                bool practising = Gm21Medicine.CanPractise(pawn);
                if (!practising || !Validate(out reason))
                {
                    Reject(practising ? reason : "GM21_Med_NotPractising".Translate(pawn.LabelShortCap).ToString());
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                Complete();
            });
        }

        private void HoldPatientStill(int ticks)
        {
            Pawn patient = Patient;
            if (patient == null || patient == pawn || !patient.Spawned || patient.Dead) return;
            if (patient.Downed || patient.InBed()) return;
            PawnUtility.ForceWait(patient, ticks, pawn, true);
            heldPatient = true;
        }

        private void ReleasePatient()
        {
            if (!heldPatient) return;
            heldPatient = false;
            Pawn patient = Patient;
            if (patient == null || patient == pawn || patient.Dead || patient.jobs == null) return;
            Job current = patient.CurJob;
            if (current != null && (current.def == JobDefOf.Wait || current.def == JobDefOf.Wait_MaintainPosture))
            {
                patient.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
        }

        protected void Reject(string reason)
        {
            if (reason.NullOrEmpty() || pawn.Faction != Faction.OfPlayer) return;
            Messages.Message("GM21_Med_InterventionStopped".Translate(pawn.LabelShortCap, reason),
                new LookTargets(pawn), MessageTypeDefOf.RejectInput, false);
        }
    }

    /// <summary>Cure: removes one selected pathological condition.</summary>
    public sealed class JobDriver_Gm21Cure : JobDriver_Gm21Intervention
    {
        private Hediff Condition
        {
            get { return job.source as Hediff; }
        }

        protected override int BaseWorkTicks
        {
            get { return Gm21Medicine.CureWorkTicks; }
        }

        protected override bool StillPlausible()
        {
            Pawn patient = Patient;
            Hediff condition = Condition;
            return patient != null && !patient.Dead && condition != null
                && patient.health.hediffSet.hediffs.Contains(condition);
        }

        protected override bool Validate(out string reason)
        {
            if (!Gm21MedicineOrders.CanTreatPatient(pawn, Patient, false, out reason)) return false;
            Hediff condition = Condition;
            if (condition == null || !Patient.health.hediffSet.hediffs.Contains(condition))
            {
                reason = "GM21_Med_ConditionGone".Translate();
                return false;
            }
            if (!Gm21CureCandidates.IsCureCandidate(condition))
            {
                reason = "GM21_Med_NoLongerCurable".Translate(condition.LabelCap);
                return false;
            }
            return true;
        }

        protected override void Complete()
        {
            Pawn patient = Patient;
            Hediff condition = Condition;
            string label = condition.LabelCap;
            Gm21CureCandidates.ApplyCure(patient, condition);
            Messages.Message("GM21_Med_Cured".Translate(pawn.LabelShortCap, patient.LabelShortCap, label),
                new LookTargets(patient), MessageTypeDefOf.PositiveEvent, true);
        }
    }

    /// <summary>Reconstruct: restores one selected missing natural body part.</summary>
    public sealed class JobDriver_Gm21Reconstruct : JobDriver_Gm21Intervention
    {
        private Hediff_MissingPart Missing
        {
            get { return job.source as Hediff_MissingPart; }
        }

        protected override int BaseWorkTicks
        {
            get { return Gm21Medicine.ReconstructWorkTicks; }
        }

        protected override bool StillPlausible()
        {
            Pawn patient = Patient;
            Hediff_MissingPart missing = Missing;
            return patient != null && !patient.Dead && missing != null
                && patient.health.hediffSet.hediffs.Contains(missing);
        }

        protected override bool Validate(out string reason)
        {
            if (!Gm21MedicineOrders.CanTreatPatient(pawn, Patient, false, out reason)) return false;
            Hediff_MissingPart missing = Missing;
            Gm21ReconstructVerdict verdict = Gm21ReconstructCandidates.Classify(Patient.health.hediffSet, missing);
            if (verdict != Gm21ReconstructVerdict.Candidate)
            {
                reason = "GM21_Med_NoLongerReconstructable".Translate();
                return false;
            }
            return true;
        }

        protected override void Complete()
        {
            Pawn patient = Patient;
            BodyPartRecord part = Missing.Part;
            string label = part.LabelCap;
            Gm21ReconstructCandidates.ApplyReconstruction(patient, part);
            Messages.Message("GM21_Med_Reconstructed".Translate(pawn.LabelShortCap, patient.LabelShortCap, label),
                new LookTargets(patient), MessageTypeDefOf.PositiveEvent, true);
        }
    }

    /// <summary>
    /// Resuscitate: brings a viable corpse back to life. The viability window is measured to the
    /// moment the work begins (workStartedTick), so a resuscitation started in time is not failed
    /// by the clock while the Grandmaster is performing it.
    /// </summary>
    public sealed class JobDriver_Gm21Resuscitate : JobDriver_Gm21Intervention
    {
        private Corpse Corpse
        {
            get { return job.targetA.Thing as Corpse; }
        }

        protected override int BaseWorkTicks
        {
            get { return Gm21Medicine.ResuscitateWorkTicks; }
        }

        protected override bool StillPlausible()
        {
            Corpse corpse = Corpse;
            return corpse != null && !corpse.Destroyed && corpse.InnerPawn != null && corpse.InnerPawn.Dead;
        }

        protected override bool Validate(out string reason)
        {
            Corpse corpse = Corpse;
            int reference = workStartedTick >= 0 ? workStartedTick : Find.TickManager.TicksGame;
            return Gm21Resuscitation.CanResuscitate(corpse, reference, out reason);
        }

        protected override void Complete()
        {
            Corpse corpse = Corpse;
            Pawn patient = corpse.InnerPawn;
            int restored, closed;
            if (!Gm21Resuscitation.TryResuscitate(pawn, corpse, out restored, out closed))
            {
                Reject("GM21_Med_ResFailed".Translate());
                EndJobWith(JobCondition.Incompletable);
                return;
            }
            Messages.Message("GM21_Med_Resuscitated".Translate(pawn.LabelShortCap, patient.LabelShortCap),
                new LookTargets(patient), MessageTypeDefOf.PositiveEvent, true);
            if (closed > 0 && Prefs.DevMode)
            {
                Log.Message("[Grandmaster 21] Medicine 21: resuscitated " + patient.ToStringSafe() + "; "
                            + restored + " fresh wound(s) preserved and stabilised, " + closed
                            + " closed because restoring them would have been fatal.");
            }
        }
    }
}
