using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Grandmaster21
{
    /// <summary>
    /// The Purify Food command, one vanilla Command with two interactions:
    ///
    ///   * LEFT-CLICK  starts vanilla targeting for ONE contaminated food stack;
    ///   * RIGHT-CLICK opens a float menu whose single entry, "Auto Purify", starts a finite cleanup of
    ///     every currently reachable contaminated stack.
    ///
    /// The right-click menu is vanilla's own mechanism: GizmoGridDrawer opens a FloatMenu whenever a
    /// gizmo's RightClickFloatMenuOptions is non-empty. No custom window, no toggle, no state.
    /// Not groupable: each Grandmaster purifies on their own behalf.
    /// </summary>
    public sealed class Command_Gm21PurifyFood : Command
    {
        public Pawn cook;

        public Command_Gm21PurifyFood()
        {
            groupable = false;
        }

        public override void ProcessInput(Event ev)
        {
            base.ProcessInput(ev);
            if (cook != null) Gm21PurifyOrders.BeginTargeting(cook);
        }

        public override IEnumerable<FloatMenuOption> RightClickFloatMenuOptions
        {
            get
            {
                Pawn pawn = cook;
                if (pawn == null) yield break;
                yield return new FloatMenuOption("GM21_Cook_AutoPurify".Translate(),
                    delegate { Gm21PurifyOrders.OrderAuto(pawn); });
            }
        }
    }

    /// <summary>
    /// Adds the command for a player-faction colonist with a legitimately stored Cooking 21. Every other
    /// pawn's gizmo bar is untouched, and the control can never appear for an enemy.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Gm21PurifyGizmo
    {
        /// <summary>A prepared meal's own vanilla icon -- no new art.</summary>
        internal static Texture2D Icon
        {
            get
            {
                ThingDef meal = ThingDefOf.MealFine;
                return (meal != null && meal.uiIcon != null) ? meal.uiIcon : BaseContent.BadTex;
            }
        }

        [HarmonyPostfix]
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            foreach (Gizmo g in __result) yield return g;

            if (!ShouldShow(__instance)) yield break;

            Command_Gm21PurifyFood cmd = new Command_Gm21PurifyFood();
            cmd.cook = __instance;
            cmd.defaultLabel = "GM21_Cook_PurifyLabel".Translate();
            cmd.defaultDesc = "GM21_Cook_PurifyDesc".Translate(
                Gm21Cooking.PurifyWorkTicks.ToStringTicksToPeriod());
            cmd.icon = Icon;
            if (!Gm21Cooking.PurifyEnabled)
            {
                cmd.Disable("GM21_Cook_PurifyUnavailable".Translate());
            }
            else if (!Gm21Cooking.CanPractise(__instance))
            {
                cmd.Disable("GM21_Cook_NotPractising".Translate(__instance.LabelShortCap));
            }
            yield return cmd;
        }

        private static bool ShouldShow(Pawn pawn)
        {
            if (pawn == null || pawn.Dead) return false;
            if (pawn.Faction == null || !pawn.Faction.IsPlayer) return false;
            if (!pawn.IsColonist) return false;
            return Gm21Cooking.IsCookingGrandmaster(pawn);
        }
    }

    /// <summary>
    /// Targeting and ordering for Purify Food. It holds no cooking logic: Gm21PurifyFood decides what is
    /// valid and Gm21Cooking who may act; this only asks them, then hands a Job to the pawn. Every order
    /// is checked again when the job starts.
    /// </summary>
    public static class Gm21PurifyOrders
    {
        /// <summary>Left-click: pick one contaminated stack with vanilla targeting.</summary>
        public static void BeginTargeting(Pawn cook)
        {
            if (!Gm21Cooking.PurifyEnabled || !Gm21Cooking.CanPractise(cook))
            {
                Reject("GM21_Cook_NotPractising".Translate(cook != null ? cook.LabelShortCap : "").ToString());
                return;
            }
            TargetingParameters parameters = new TargetingParameters
            {
                canTargetPawns = false,
                canTargetBuildings = false,
                canTargetItems = true,
                mapObjectTargetsMustBeAutoAttackable = false,
                // Only contaminated stacks light up; a click elsewhere is refused by the targeter.
                validator = delegate(TargetInfo t) { return Gm21PurifyFood.IsPurifyTarget(t.Thing); }
            };
            Find.Targeter.BeginTargeting(parameters,
                delegate(LocalTargetInfo t) { OrderSingle(cook, t.Thing); },
                cook, null, Gm21PurifyGizmo.Icon);
        }

        public static void OrderSingle(Pawn cook, Thing target)
        {
            string reason;
            if (!Gm21PurifyFood.CanOrder(cook, target, out reason))
            {
                Reject(reason);
                return;
            }
            cook.jobs.TryTakeOrderedJob(Gm21PurifyFood.MakeJob(target, false), JobTag.Misc);
        }

        /// <summary>
        /// Right-click "Auto Purify": start with the nearest reachable, unreserved contaminated stack.
        /// The job finds its own following targets and stops by itself. With nothing to purify, no job
        /// is started at all.
        /// </summary>
        public static void OrderAuto(Pawn cook)
        {
            if (!Gm21Cooking.PurifyEnabled || !Gm21Cooking.CanPractise(cook))
            {
                Reject("GM21_Cook_NotPractising".Translate(cook != null ? cook.LabelShortCap : "").ToString());
                return;
            }
            Thing first = Gm21PurifyFood.FindNearest(cook, null);
            if (first == null)
            {
                Reject("GM21_Cook_NothingToPurify".Translate(cook.LabelShortCap).ToString());
                return;
            }
            cook.jobs.TryTakeOrderedJob(Gm21PurifyFood.MakeJob(first, true), JobTag.Misc);
        }

        private static void Reject(string reason)
        {
            if (reason.NullOrEmpty()) return;
            Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
        }
    }
}
