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
    /// Shared shape of a Grandmaster intervention, modelled on JobDriver_TendPatient. The lifecycle:
    ///
    ///   1. validate    -- the order and TryMakePreToilReservations run the full validation;
    ///   2. reserve     -- the patient or corpse, and every planned medicine stack (vanilla's own
    ///                     stack reservation, the same maxPawns vanilla tending uses);
    ///   3. acquire     -- walk to each planned stack and take exactly the planned count into the
    ///                     Grandmaster's inventory (vanilla TakeToInventory), then confirm the carried
    ///                     medicine meets the potency budget before going on;
    ///   4. approach    -- walk to the patient (a Grandmaster treating themself stays put);
    ///   5. work        -- timed medical work with the vanilla tend sound, progress bar and Medicine
    ///                     as the active skill, scaled by MedicalTendSpeed;
    ///   6. apply       -- re-validate everything, then apply the intervention;
    ///   7. consume     -- only after a successful apply, destroy exactly one budget's worth of the
    ///                     carried medicine. Once.
    ///
    /// INTERRUPTION IS SAFE: nothing is applied or consumed before the final instant step, which
    /// re-runs the full validation first. Medicine already collected stays in the Grandmaster's
    /// inventory -- never destroyed, never duplicated -- and a new order uses it first.
    ///
    /// A standing patient is asked to hold still with vanilla's own PawnUtility.ForceWait -- the
    /// same thing a drafted tend does -- so the patient needs no custom job. A downed or bedded
    /// patient is simply treated where they lie. Only a practising Medicine Grandmaster can hold one
    /// of these jobs; the check is repeated every tick.
    /// </summary>
    public abstract class JobDriver_Gm21Intervention : JobDriver
    {
        /// <summary>Same maxPawns vanilla uses for medicine stacks: reservations must agree on it.</summary>
        public const int MedicineReservationMaxPawns = 10;

        private PathEndMode pathEndMode = PathEndMode.ClosestTouch;

        /// <summary>Tick the timed work began, or -1. Saved, so a reload mid-work keeps its clock.</summary>
        protected int workStartedTick = -1;

        /// <summary>This driver asked a standing patient to hold still, and must release them.</summary>
        private bool heldPatient;

        protected abstract Gm21MedicineMode Mode { get; }

        protected abstract int BaseWorkTicks { get; }

        /// <summary>
        /// Whose medical-care setting limits the medicine (null: no restriction -- Resuscitate, see
        /// Gm21MedicineSupplies).
        /// </summary>
        protected abstract Pawn CarePatient { get; }

        /// <summary>Cheap per-tick check that the intervention still makes sense.</summary>
        protected abstract bool StillPlausible();

        /// <summary>The full validation, run before the work and again at the finish.</summary>
        protected abstract bool Validate(out string reason);

        /// <summary>
        /// Applies the result. Only called straight after a successful Validate and medicine check.
        /// Returns false, having changed nothing that needs medicine, if it could not be applied;
        /// <paramref name="message"/> and <paramref name="look"/> describe a success.
        /// </summary>
        protected abstract bool Complete(out string message, out LookTargets look);

        protected Pawn Patient
        {
            get { return job.targetA.Thing as Pawn; }
        }

        protected bool SelfTreatment
        {
            get { return Patient == pawn; }
        }

        public float PotencyBudget
        {
            get { return Gm21MedicineSupplies.BudgetFor(Mode); }
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
            if (patient == pawn) pathEndMode = PathEndMode.OnCell;
            else if (patient != null && patient.InBed()) pathEndMode = PathEndMode.InteractionCell;
            else pathEndMode = patient != null ? PathEndMode.ClosestTouch : PathEndMode.Touch;
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            string reason;
            if (!Gm21Medicine.CanPractise(pawn) || !Validate(out reason)) return false;
            if (!pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed)) return false;

            List<LocalTargetInfo> queue = job.GetTargetQueue(TargetIndex.B);
            for (int i = 0; i < queue.Count; i++)
            {
                Thing stack = queue[i].Thing;
                int count = job.countQueue != null && i < job.countQueue.Count ? job.countQueue[i] : 1;
                if (stack == null || stack.Destroyed || !stack.Spawned) return false;
                // Checked first so a stack that shrank since the order fails quietly, not as an error.
                if (pawn.Map.reservationManager.CanReserveStack(pawn, stack, MedicineReservationMaxPawns) < count)
                    return false;
                if (!pawn.Reserve(stack, job, MedicineReservationMaxPawns, count, null, errorOnFailed)) return false;
            }
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => !Gm21Medicine.CanPractise(pawn) || !StillPlausible());

            Toil gotoPatient = Toils_Goto.GotoThing(TargetIndex.A, pathEndMode);

            // ---- acquire: every planned map stack, exactly the planned count, into the inventory.
            Toil confirmSupplies = Toils_General.Do(delegate
            {
                List<KeyValuePair<Gm21SupplyStack, int>> plan = new List<KeyValuePair<Gm21SupplyStack, int>>();
                string reason;
                if (!Gm21MedicineSupplies.TryPlanFromInventory(pawn, CarePatient, PotencyBudget, plan, out reason))
                {
                    Reject(reason);
                    EndJobWith(JobCondition.Incompletable);
                }
            });
            yield return Toils_Jump.JumpIf(confirmSupplies, () => job.GetTargetQueue(TargetIndex.B).NullOrEmpty());
            Toil extract = Toils_JobTransforms.ExtractNextTargetFromQueue(TargetIndex.B, false);
            yield return extract;
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch)
                .FailOnDespawnedNullOrForbidden(TargetIndex.B);
            yield return Toils_Haul.TakeToInventory(TargetIndex.B, () => job.count);
            yield return Toils_Jump.JumpIfHaveTargetInQueue(TargetIndex.B, extract);
            yield return confirmSupplies;

            // ---- approach
            yield return gotoPatient;

            // ---- work
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
                if (!SelfTreatment) pawn.rotationTracker.FaceTarget(job.targetA);
            };
            work.FailOn(() => !SelfTreatment && !pawn.CanReachImmediate(job.targetA, pathEndMode));
            // Same release vanilla's drafted tend performs: whether the work completes or is
            // interrupted, a patient we asked to hold still is not left standing there.
            work.AddFinishAction(ReleasePatient);
            yield return work;

            // ---- apply, then consume: one instant step, run once.
            yield return Toils_General.Do(Finish);
        }

        private void Finish()
        {
            string reason = null;
            bool practising = Gm21Medicine.CanPractise(pawn);
            if (!practising || !Validate(out reason))
            {
                Reject(practising ? reason : "GM21_Med_NotPractising".Translate(pawn.LabelShortCap).ToString());
                EndJobWith(JobCondition.Incompletable);
                return;
            }
            List<KeyValuePair<Gm21SupplyStack, int>> plan = new List<KeyValuePair<Gm21SupplyStack, int>>();
            if (!Gm21MedicineSupplies.TryPlanFromInventory(pawn, CarePatient, PotencyBudget, plan, out reason))
            {
                Reject(reason);
                EndJobWith(JobCondition.Incompletable);
                return;
            }
            string used = Gm21MedicineSupplies.Describe(plan);

            string message;
            LookTargets look;
            if (!Complete(out message, out look))
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }
            Gm21MedicineSupplies.Consume(plan);
            if (!message.NullOrEmpty())
            {
                if (!used.NullOrEmpty()) message += " " + "GM21_Med_MedicineUsed".Translate(used);
                Messages.Message(message, look, MessageTypeDefOf.PositiveEvent, true);
            }
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

        protected override Gm21MedicineMode Mode
        {
            get { return Gm21MedicineMode.Cure; }
        }

        protected override int BaseWorkTicks
        {
            get { return Gm21Medicine.CureWorkTicks; }
        }

        protected override Pawn CarePatient
        {
            get { return Patient; }
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

        protected override bool Complete(out string message, out LookTargets look)
        {
            Pawn patient = Patient;
            Hediff condition = Condition;
            string label = condition.LabelCap;
            Gm21CureCandidates.ApplyCure(patient, condition);
            message = "GM21_Med_Cured".Translate(pawn.LabelShortCap, patient.LabelShortCap, label);
            look = new LookTargets(patient);
            return true;
        }
    }

    /// <summary>Reconstruct: restores one selected missing natural body part.</summary>
    public sealed class JobDriver_Gm21Reconstruct : JobDriver_Gm21Intervention
    {
        private Hediff_MissingPart Missing
        {
            get { return job.source as Hediff_MissingPart; }
        }

        protected override Gm21MedicineMode Mode
        {
            get { return Gm21MedicineMode.Reconstruct; }
        }

        protected override int BaseWorkTicks
        {
            get { return Gm21Medicine.ReconstructWorkTicks; }
        }

        protected override Pawn CarePatient
        {
            get { return Patient; }
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
            if (!Gm21MedicineOrders.CanReconstructOn(pawn, Patient, out reason)) return false;
            Hediff_MissingPart missing = Missing;
            Gm21ReconstructVerdict verdict = Gm21ReconstructCandidates.Classify(Patient.health.hediffSet, missing);
            if (verdict != Gm21ReconstructVerdict.Candidate)
            {
                reason = "GM21_Med_NoLongerReconstructable".Translate();
                return false;
            }
            return true;
        }

        protected override bool Complete(out string message, out LookTargets look)
        {
            Pawn patient = Patient;
            BodyPartRecord part = Missing.Part;
            string label = part.LabelCap;
            Gm21ReconstructCandidates.ApplyReconstruction(patient, part);
            message = "GM21_Med_Reconstructed".Translate(pawn.LabelShortCap, patient.LabelShortCap, label);
            look = new LookTargets(patient);
            return true;
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

        protected override Gm21MedicineMode Mode
        {
            get { return Gm21MedicineMode.Resuscitate; }
        }

        protected override int BaseWorkTicks
        {
            get { return Gm21Medicine.ResuscitateWorkTicks; }
        }

        /// <summary>No restriction: a corpse's medical care cannot be edited (see Gm21MedicineSupplies).</summary>
        protected override Pawn CarePatient
        {
            get { return null; }
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

        protected override bool Complete(out string message, out LookTargets look)
        {
            Corpse corpse = Corpse;
            Pawn patient = corpse.InnerPawn;
            message = null;
            look = null;
            Gm21ResuscitationOutcome outcome;
            if (!Gm21Resuscitation.TryResuscitate(pawn, corpse, out outcome))
            {
                Reject("GM21_Med_ResFailed".Translate());
                return false;
            }

            message = "GM21_Med_Resuscitated".Translate(pawn.LabelShortCap, patient.LabelShortCap);
            if (outcome.rebuilt.Count > 0)
            {
                List<string> parts = new List<string>();
                foreach (BodyPartRecord part in outcome.rebuilt) parts.Add(part.Label);
                message += " " + "GM21_Med_ResRebuilt".Translate(string.Join(", ", parts.ToArray()));
            }
            if (outcome.scars.Count > 0)
            {
                List<string> scars = new List<string>();
                foreach (Hediff_Injury scar in outcome.scars)
                    scars.Add(scar.Part != null ? scar.Label + " (" + scar.Part.Label + ")" : scar.Label);
                message += " " + "GM21_Med_ResScars".Translate(string.Join(", ", scars.ToArray()));
            }
            look = new LookTargets(patient);

            if (Prefs.DevMode)
            {
                Log.Message("[Grandmaster 21] Medicine 21: resuscitated " + patient.ToStringSafe() + "; "
                            + outcome.woundsRestored + " fresh wound(s) preserved and stabilised, "
                            + outcome.woundsClosed + " closed because restoring them would have been fatal, "
                            + outcome.rebuilt.Count + " vital part(s) rebuilt, " + outcome.scars.Count
                            + " permanent scar(s).");
            }
            return true;
        }
    }
}
