using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Grandmaster21
{
    /// <summary>
    /// Purify Food as real pawn work: find a contaminated stack, reserve it, walk to it, work for a short
    /// timed interval with a progress bar, then clear the contamination.
    ///
    ///   acquire -> go -> work -> finish
    ///
    /// SINGLE (GM21_PurifyFood): one stack, then the job ends.
    ///
    /// AUTO (GM21_PurifyFoodAuto): the same four toils, and 'finish' jumps back to 'acquire', which looks
    /// for the next nearest contaminated stack. It is a FINITE cleanup, not a standing toggle: when no
    /// reachable, unreserved contaminated stack remains, the job ends and the pawn returns to normal
    /// behaviour. Nothing scans while the pawn walks or works, and nothing runs after the job ends.
    ///
    /// A single Job carries the whole run, so it ends through the ordinary JobDriver machinery only:
    ///   * drafted, downed, dead, or ordered elsewhere -- vanilla ends the job (reservations are released
    ///     by vanilla with it); this driver adds a per-tick "still a practising Cooking Grandmaster" check;
    ///   * a target destroyed, eaten, hauled off, purified by someone else, or no longer reachable /
    ///     reservable -- that STACK is dropped (Auto moves on to the next one, Single ends);
    ///   * a stack that fails is remembered for the rest of the run and never tried twice.
    ///
    /// Only the stack being worked is reserved, and it is released as soon as it is done, so a long run
    /// never keeps meals reserved that colonists want to eat. No global fail-condition or Reset patch is
    /// involved: skipping a stack is a JumpToToil from a pre-tick action, which vanilla's DriverTick
    /// explicitly supports (it re-checks the current toil after every pre-tick action).
    /// </summary>
    public abstract class JobDriver_Gm21PurifyFoodBase : JobDriver
    {
        /// <summary>Stacks are examined in nearest-first batches of at most this many per acquisition.</summary>
        private const int MaxAcquireAttempts = 32;

        protected abstract bool Auto { get; }

        /// <summary>Stacks this job has purified. Saved, so a reload keeps the end-of-run summary honest.</summary>
        private int purified;

        /// <summary>Stacks this run gave up on. Deliberately not saved: a reload simply re-tries them once.</summary>
        private readonly List<int> skipped = new List<int>();

        private Toil acquire;

        private Thing Target
        {
            get { return job.targetA.Thing; }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref purified, "gm21Purified", 0);
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!Gm21Cooking.CanPractise(pawn) || Target == null) return false;
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => !Gm21Cooking.CanPractise(pawn));

            acquire = ToilMaker.MakeToil("Gm21PurifyAcquire");
            acquire.defaultCompleteMode = ToilCompleteMode.Instant;
            acquire.initAction = Acquire;
            yield return acquire;

            Toil go = ToilMaker.MakeToil("Gm21PurifyGoto");
            go.defaultCompleteMode = ToilCompleteMode.PatherArrival;
            go.initAction = delegate { pawn.pather.StartPath(job.targetA, PathEndMode.Touch); };
            go.AddPreTickAction(DropTargetIfInvalid);
            yield return go;

            // The validity check is added FIRST: DriverTick stops running a toil's remaining pre-tick actions
            // once one of them has moved the driver on, so the progress bar and sound below never see a
            // target that has just vanished. Facing is done here rather than by Wait(.., TargetIndex.A) for
            // the same reason: it only ever faces a stack that is still on the map.
            Toil work = Toils_General.Wait(Gm21Cooking.PurifyWorkTicks);
            work.AddPreTickAction(DropTargetIfInvalid);
            work.handlingFacing = true;
            work.tickIntervalAction = delegate(int delta)
            {
                Thing t = Target;
                if (t != null && t.Spawned) pawn.rotationTracker.FaceTarget(t);
            };
            work.WithProgressBarToilDelay(TargetIndex.A);
            work.PlaySustainerOrSound(SoundDefOf.Interact_CleanFilth);
            work.activeSkill = () => SkillDefOf.Cooking;
            yield return work;

            Toil finish = ToilMaker.MakeToil("Gm21PurifyFinish");
            finish.defaultCompleteMode = ToilCompleteMode.Instant;
            finish.initAction = Finish;
            yield return finish;
        }

        /// <summary>
        /// A stack could not be reached (the pather gave up). Single: the vanilla behaviour, end the job.
        /// Auto: drop just this stack for the rest of the run and look for the next.
        /// </summary>
        public override void Notify_PatherFailed()
        {
            if (Auto && acquire != null && Target != null)
            {
                DropTarget(true);
                return;
            }
            base.Notify_PatherFailed();
        }

        // ---------------------------------------------------------------- toil actions

        /// <summary>
        /// The one place a target is chosen. First pass: the ordered target, already reserved by
        /// TryMakePreToilReservations. Later passes (Auto only): the nearest remaining stack, reserved
        /// here. Ends the job when there is nothing left to do.
        /// </summary>
        private void Acquire()
        {
            if (!Gm21Cooking.CanPractise(pawn))
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            Thing current = Target;
            if (current != null && current.Map == pawn.Map && Gm21PurifyFood.IsPurifyTarget(current)
                && !skipped.Contains(current.thingIDNumber))
            {
                return; // the target in hand is still good and is already reserved by this job
            }

            ReleaseTarget();
            job.targetA = LocalTargetInfo.Invalid;
            if (!Auto)
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            for (int attempt = 0; attempt < MaxAcquireAttempts; attempt++)
            {
                Thing next = Gm21PurifyFood.FindNearest(pawn, skipped);
                if (next == null) break;
                if (pawn.Reserve(next, job, 1, -1, null, false))
                {
                    job.targetA = next;
                    return;
                }
                skipped.Add(next.thingIDNumber); // contested between the search and the claim
            }
            EndRun();
        }

        private void DropTargetIfInvalid()
        {
            Thing t = Target;
            if (t != null && t.Map == pawn.Map && Gm21PurifyFood.IsPurifyTarget(t)) return;
            DropTarget(false);
        }

        /// <summary>Gives up on the current stack: Auto goes on to the next, Single ends the job.</summary>
        private void DropTarget(bool rememberAsFailed)
        {
            Thing t = Target;
            if (rememberAsFailed && t != null) skipped.Add(t.thingIDNumber);
            ReleaseTarget();
            job.targetA = LocalTargetInfo.Invalid;
            if (Auto && acquire != null) JumpToToil(acquire);
            else EndJobWith(JobCondition.Incompletable);
        }

        private void Finish()
        {
            Thing t = Target;
            if (!Gm21Cooking.CanPractise(pawn))
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }
            if (t == null || t.Map != pawn.Map)
            {
                DropTarget(false); // removed from the map since the work began
                return;
            }
            if (!Gm21PurifyFood.Purify(t))
            {
                // Already clean (someone else purified it first), or purification is unavailable. Either
                // way it is remembered, so an Auto run can never pick the same stack twice.
                DropTarget(true);
                return;
            }

            purified++;
            Feedback(t);
            ReleaseTarget();
            job.targetA = LocalTargetInfo.Invalid;
            if (Auto) JumpToToil(acquire);
            else EndJobWith(JobCondition.Succeeded);
        }

        /// <summary>The run is complete: nothing reachable and unreserved is left. One summary, then stop.</summary>
        private void EndRun()
        {
            try
            {
                if (Auto && purified > 0 && pawn.Faction == Faction.OfPlayer)
                {
                    Messages.Message("GM21_Cook_AutoDone".Translate(pawn.LabelShortCap, purified),
                        new LookTargets(pawn), MessageTypeDefOf.PositiveEvent, false);
                }
            }
            catch (System.Exception e)
            {
                Gm21CookingPatches.WarnOnce("purify summary", e);
            }
            EndJobWith(JobCondition.Succeeded);
        }

        /// <summary>
        /// Purely cosmetic, and run AFTER the contamination is already gone: a failure here must not turn a
        /// successful purification into a vanilla job error, so it is contained and logged once.
        /// </summary>
        private void Feedback(Thing t)
        {
            try
            {
                MoteMaker.ThrowText(t.DrawPos, t.Map, "GM21_Cook_PurifiedMote".Translate(), Color.white);
                // A single purification is reported. An Auto run reports once, at its end.
                if (!Auto && pawn.Faction == Faction.OfPlayer)
                {
                    Messages.Message("GM21_Cook_Purified".Translate(pawn.LabelShortCap, t.LabelShortCap),
                        new LookTargets(t), MessageTypeDefOf.PositiveEvent, false);
                }
            }
            catch (System.Exception e)
            {
                Gm21CookingPatches.WarnOnce("purify feedback", e);
            }
        }

        private void ReleaseTarget()
        {
            Gm21PurifyFood.Release(pawn, Target, job);
        }
    }

    /// <summary>One contaminated stack, then done.</summary>
    public sealed class JobDriver_Gm21PurifyFood : JobDriver_Gm21PurifyFoodBase
    {
        protected override bool Auto
        {
            get { return false; }
        }
    }

    /// <summary>Every currently reachable contaminated stack, nearest first, then stop.</summary>
    public sealed class JobDriver_Gm21AutoPurifyFood : JobDriver_Gm21PurifyFoodBase
    {
        protected override bool Auto
        {
            get { return true; }
        }
    }
}
