using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Grandmaster21.Transcendent
{
    public sealed class WorkGiver_TranscendentCraft : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest { get { return ThingRequest.ForDef(TranscendentDefOf.GM21_MagicalWorkstation); } }
        public override PathEndMode PathEndMode { get { return PathEndMode.InteractionCell; } }
        public override bool ShouldSkip(Pawn pawn, bool forced = false) { return !TranscendentRecipes.CanCraft(pawn); }

        public override Job JobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            Building_MagicalWorkstation bench = thing as Building_MagicalWorkstation;
            if (bench == null || !TranscendentRecipes.CanCraft(pawn) || !bench.Usable || thing.IsForbidden(pawn)
                || !pawn.CanReserve(thing, 1, -1, null, forced)
                || !pawn.CanReserveSittableOrSpot(thing.InteractionCell, thing, forced)) return null;
            if (bench.Project != null)
            {
                if (!bench.Project.Valid || bench.Project.faulted) return null;
                if (!forced && bench.Project.outputCreated && Find.TickManager.TicksGame < bench.nextSearchTick) return null;
                return JobMaker.MakeJob(TranscendentDefOf.GM21_TranscendentCraft, bench);
            }
            Bill_Production bill = bench.Pending;
            if (bill == null || bench.HasRecovery || !TranscendentRecipes.MeetsRecipe(pawn, bill.recipe) || !bill.recipe.AvailableNow) return null;
            if (!forced && Find.TickManager.TicksGame < bench.nextSearchTick) return null;
            List<ThingCount> selected;
            if (!TranscendentIngredients.TryFind(bench, pawn, bill, out selected))
            {
                bench.nextSearchTick = Find.TickManager.TicksGame + 300;
                JobFailReason.Is("GM21_TC_Missing".Translate());
                return null;
            }
            Job job = JobMaker.MakeJob(TranscendentDefOf.GM21_TranscendentCraft, bench);
            job.bill = bill;
            job.targetQueueB = new List<LocalTargetInfo>();
            job.countQueue = new List<int>();
            foreach (ThingCount c in selected) { job.targetQueueB.Add(c.Thing); job.countQueue.Add(c.Count); }
            job.haulMode = HaulMode.ToCellNonStorage;
            return job;
        }
    }

    public sealed class JobDriver_TranscendentCraft : JobDriver
    {
        private string projectId;
        private Building_MagicalWorkstation Bench { get { return job.targetA.Thing as Building_MagicalWorkstation; } }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref projectId, "gm21ProjectId");
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Building_MagicalWorkstation bench = Bench;
            if (bench == null || !bench.Usable || !TranscendentRecipes.CanCraft(pawn)
                || !pawn.Reserve(bench, job, 1, -1, null, errorOnFailed)
                || !pawn.ReserveSittableOrSpot(bench.InteractionCell, job, errorOnFailed)) return false;
            if (bench.Project != null)
            {
                if (job.bill != null) return false; // A queued start cannot silently become a resume.
                projectId = bench.Project.id;
            }
            else
            {
                if (job.bill == null || job.bill != bench.Pending) return false;
                if (job.targetQueueB != null)
                    foreach (LocalTargetInfo target in job.targetQueueB)
                        if (!pawn.Reserve(target, job, 1, -1, null, errorOnFailed)) return false;
            }
            return true;
        }

        private bool InvalidJob()
        {
            Building_MagicalWorkstation bench = Bench;
            if (bench == null || !bench.Usable || bench.IsForbidden(pawn) || !TranscendentRecipes.CanCraft(pawn)) return true;
            if (projectId != null) return bench.Project == null || bench.Project.id != projectId || !bench.Project.Valid || bench.Project.faulted;
            return bench.Project != null || job.bill == null || job.bill != bench.Pending || !job.bill.recipe.AvailableNow;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(InvalidJob);
            Toil atBench = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);
            yield return Toils_Jump.JumpIf(atBench, () => projectId != null);
            Toil extract = Toils_JobTransforms.ExtractNextTargetFromQueue(TargetIndex.B);
            yield return extract;
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch, true)
                .FailOnForbidden(TargetIndex.B).FailOnSomeonePhysicallyInteracting(TargetIndex.B);
            yield return Toils_Haul.StartCarryThing(TargetIndex.B, true, false, true, false, true);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell).FailOnDestroyedOrNull(TargetIndex.B);
            Toil findPlace = Toils_JobTransforms.SetTargetToIngredientPlaceCell(TargetIndex.A, TargetIndex.B, TargetIndex.C);
            yield return findPlace;
            Toil place = ToilMaker.MakeToil("GM21_DeliverIngredients");
            place.initAction = delegate
            {
                // Vanilla PlaceHauledThingInCell records placedThings only for named vanilla jobs.
                // Use its native carry/drop API with an explicit receipt callback for this JobDef.
                if (pawn.carryTracker.CarriedThing == null) { EndJobWith(JobCondition.Incompletable); return; }
                Thing dropped;
                bool placed = pawn.carryTracker.TryDropCarriedThing(job.targetC.Cell, ThingPlaceMode.Direct, out dropped,
                    (thing, count) =>
                    {
                        HaulAIUtility.UpdateJobWithPlacedThings(job, thing, count);
                        pawn.Map.physicalInteractionReservationManager.Reserve(pawn, job, thing);
                    });
                if (!placed || pawn.carryTracker.CarriedThing != null) JumpToToil(findPlace);
            };
            place.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return place;
            yield return Toils_Jump.JumpIfHaveTargetInQueue(TargetIndex.B, extract);
            yield return atBench;
            Toil commit = ToilMaker.MakeToil("GM21_CommitProject");
            commit.initAction = delegate
            {
                if (projectId != null) return;
                if (!Bench.TryCommit(pawn, job)) { EndJobWith(JobCondition.Incompletable); return; }
                projectId = Bench.Project.id;
            };
            commit.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return commit;
            Toil work = ToilMaker.MakeToil("GM21_WorkProject");
            work.defaultCompleteMode = ToilCompleteMode.Never;
            work.tickAction = () => Bench.UsedThisTick();
            work.tickIntervalAction = delegate(int delta)
            {
                if (!Bench.Work(pawn, projectId, delta)) { EndJobWith(JobCondition.Incompletable); return; }
                pawn.GainComfortFromCellIfPossible(delta, true);
                if (Bench.Project.completedWork >= Bench.Project.totalWork) { ReadyForNextToil(); return; }
                if (pawn.IsHashIntervalTick(1000, delta)) pawn.jobs.CheckForJobOverride();
            };
            work.WithProgressBar(TargetIndex.A, () => Bench.Project == null ? 0f : Bench.Project.Progress);
            work.FailOnCannotTouch(TargetIndex.A, PathEndMode.InteractionCell);
            yield return work;
            Toil complete = ToilMaker.MakeToil("GM21_CompleteProject");
            complete.initAction = delegate
            {
                if (!Bench.TryComplete(pawn, projectId)) EndJobWith(JobCondition.Incompletable);
                else EndJobWith(JobCondition.Succeeded);
            };
            complete.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return complete;
        }
    }
}
