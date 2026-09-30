using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Grandmaster21
{
    [DefOf]
    public static class Gm21CookingDefOf
    {
        public static JobDef GM21_PurifyFood;
        public static JobDef GM21_PurifyFoodAuto;
        public static ThoughtDef GM21_MasterfulMeal;

        static Gm21CookingDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(Gm21CookingDefOf)); }

        public static bool IsPurifyJob(JobDef def)
        {
            return def != null && (def == GM21_PurifyFood || def == GM21_PurifyFoodAuto);
        }
    }

    /// <summary>
    /// Purify Food: what counts as contaminated prepared food, how to clear it, and how to find the
    /// nearest such stack. No UI and no job logic lives here.
    ///
    /// A target is any spawned Thing carrying vanilla's CompFoodPoisonable with PoisonPercent above
    /// zero. It is found through the comp, never a DefName, so modded food built on the vanilla comp is
    /// covered by the same rule.
    ///
    /// Vanilla exposes SetPoisoned (which raises the poison to 100%) but no way to clear it, so the
    /// clear is a write to the comp's two private fields. Their accessors are resolved ONCE, at startup,
    /// into cached delegates (Harmony FieldRefAccess): nothing here reflects on a hot path. If either
    /// field cannot be resolved, Purify Food is disabled and vanilla is untouched.
    /// </summary>
    internal static class Gm21PurifyFood
    {
        private static AccessTools.FieldRef<CompFoodPoisonable, float> poisonPct;
        private static AccessTools.FieldRef<CompFoodPoisonable, FoodPoisonCause> cause;

        /// <summary>Resolves the field accessors. True only when both exist with the audited types.</summary>
        internal static bool Bind()
        {
            try
            {
                poisonPct = AccessTools.FieldRefAccess<CompFoodPoisonable, float>("poisonPct");
                cause = AccessTools.FieldRefAccess<CompFoodPoisonable, FoodPoisonCause>("cause");
                return poisonPct != null && cause != null;
            }
            catch (Exception e)
            {
                poisonPct = null;
                cause = null;
                Log.Warning("[Grandmaster 21] Cooking 21: Purify Food is disabled this session; "
                            + "CompFoodPoisonable's poison fields could not be resolved (" + e.GetType().Name
                            + ": " + e.Message + "). Vanilla food poisoning is unchanged.");
                return false;
            }
        }

        /// <summary>True when the thing is a spawned, contaminated prepared-food stack.</summary>
        internal static bool IsPurifyTarget(Thing thing)
        {
            if (thing == null || thing.Destroyed || !thing.Spawned || thing.Map == null) return false;
            CompFoodPoisonable comp = thing.TryGetComp<CompFoodPoisonable>();
            return comp != null && comp.PoisonPercent > 0f;
        }

        /// <summary>
        /// Clears the contamination and nothing else: poison percentage to 0 and the cause back to
        /// Unknown. Stack count, rot, Masterful provenance and every other comp are not touched.
        /// Returns false, changing nothing, if the thing is not contaminated or Purify is disabled.
        /// </summary>
        internal static bool Purify(Thing thing)
        {
            if (thing == null || poisonPct == null || cause == null) return false;
            CompFoodPoisonable comp = thing.TryGetComp<CompFoodPoisonable>();
            if (comp == null || !(comp.PoisonPercent > 0f)) return false;
            poisonPct(comp) = 0f;
            cause(comp) = FoodPoisonCause.Unknown;
            return true;
        }

        /// <summary>
        /// The full order-time check for one target, with the player-facing reason on refusal. The same
        /// check runs again when the job starts. Reachability and reservation are decided here, at the
        /// moment the pawn is asked to walk, not by a background scan.
        /// </summary>
        internal static bool CanOrder(Pawn pawn, Thing thing, out string reason)
        {
            reason = null;
            if (!Gm21Cooking.CanPractise(pawn))
            {
                reason = "GM21_Cook_NotPractising".Translate(pawn != null ? pawn.LabelShortCap : "").ToString();
                return false;
            }
            if (!IsPurifyTarget(thing) || thing.Map != pawn.Map)
            {
                reason = "GM21_Cook_NotContaminated".Translate().ToString();
                return false;
            }
            if (!pawn.CanReach(thing, PathEndMode.Touch, Danger.Deadly))
            {
                reason = "GM21_Cook_CannotReach".Translate(thing.LabelShortCap).ToString();
                return false;
            }
            if (!pawn.CanReserve(thing, 1, -1, null, false))
            {
                reason = "GM21_Cook_Reserved".Translate(thing.LabelShortCap).ToString();
                return false;
            }
            return true;
        }

        /// <summary>
        /// The nearest reachable, unreserved contaminated stack, or null. One region-based search, run
        /// only when a target is needed -- never on a timer. <paramref name="skipped"/> holds stacks the
        /// running job already failed on, so an unreachable or contested stack is not tried twice.
        /// </summary>
        internal static Thing FindNearest(Pawn pawn, List<int> skipped)
        {
            if (pawn == null || pawn.Map == null || !pawn.Spawned) return null;
            return GenClosest.ClosestThingReachable(pawn.Position, pawn.Map,
                ThingRequest.ForGroup(ThingRequestGroup.FoodSourceNotPlantOrTree),
                PathEndMode.Touch, TraverseParms.For(pawn), 9999f,
                delegate(Thing t)
                {
                    if (skipped != null && skipped.Contains(t.thingIDNumber)) return false;
                    return IsPurifyTarget(t) && pawn.CanReserve(t, 1, -1, null, false);
                });
        }

        /// <summary>
        /// Releases this job's reservation on a stack as soon as it is done with it, so a long Auto run
        /// never keeps meals reserved that colonists want to eat. Only a reservation this pawn holds for
        /// this job is released. (Job end releases everything else, as in vanilla.)
        /// </summary>
        internal static void Release(Pawn pawn, Thing thing, Job job)
        {
            if (pawn == null || thing == null || pawn.Map == null) return;
            ReservationManager reservations = pawn.Map.reservationManager;
            if (reservations.ReservedBy(thing, pawn, job)) reservations.Release(thing, pawn, job);
        }

        internal static Job MakeJob(Thing target, bool auto)
        {
            return JobMaker.MakeJob(auto ? Gm21CookingDefOf.GM21_PurifyFoodAuto : Gm21CookingDefOf.GM21_PurifyFood, target);
        }
    }
}
