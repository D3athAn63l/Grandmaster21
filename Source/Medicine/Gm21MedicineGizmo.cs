using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// The Grandmaster Medicine command. One vanilla Command, two interactions:
    ///
    ///   * LEFT-CLICK opens a three-entry FloatMenu to choose the mode -- the same interaction as the
    ///     Shooting aim and Melee doctrine controls, so all three GM21 gizmos select a mode the same way;
    ///   * RIGHT-CLICK activates the selected mode and enters vanilla targeting.
    ///
    /// The right-click route is vanilla's own: a Command with no RightClickFloatMenuOptions has a
    /// right-click delivered to ProcessInput with the event, and vanilla's Designator_Plan_Add reads
    /// ev.button the same way. No custom window.
    ///
    /// Not groupable: each Grandmaster targets on their own behalf, so two selected Grandmasters get
    /// two commands rather than one merged command that would open two menus at once.
    /// </summary>
    public sealed class Command_Gm21Medicine : Command
    {
        public Pawn doctor;

        public Command_Gm21Medicine()
        {
            groupable = false;
        }

        public override void ProcessInput(Event ev)
        {
            base.ProcessInput(ev);
            if (doctor == null) return;
            if (ev != null && ev.button == 1)
            {
                Gm21MedicineOrders.BeginTargeting(doctor);
                return;
            }

            Pawn pawn = doctor;
            List<FloatMenuOption> options = new List<FloatMenuOption>(3);
            AddOption(options, pawn, Gm21MedicineMode.Cure);
            AddOption(options, pawn, Gm21MedicineMode.Reconstruct);
            AddOption(options, pawn, Gm21MedicineMode.Resuscitate);
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void AddOption(List<FloatMenuOption> options, Pawn pawn, Gm21MedicineMode mode)
        {
            options.Add(new FloatMenuOption(Gm21MedicineGizmo.LabelFor(mode) + " - " + Gm21MedicineGizmo.DescFor(mode),
                delegate { Gm21MedicineModeStore.Set(pawn, mode); }));
        }
    }

    /// <summary>
    /// Adds the command for a player-faction colonist with a legitimate stored Medicine 21. Every
    /// other pawn's gizmo bar is untouched, and the control can never appear for an enemy.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Gm21MedicineGizmo
    {
        /// <summary>Vanilla industrial medicine's own UI icon -- no new art.</summary>
        internal static Texture2D Icon
        {
            get
            {
                ThingDef med = ThingDefOf.MedicineIndustrial;
                return (med != null && med.uiIcon != null) ? med.uiIcon : BaseContent.BadTex;
            }
        }

        [HarmonyPostfix]
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            foreach (Gizmo g in __result) yield return g;

            if (!ShouldShow(__instance)) yield break;

            Gm21MedicineMode current = Gm21MedicineModeStore.Get(__instance);
            Command_Gm21Medicine cmd = new Command_Gm21Medicine();
            cmd.doctor = __instance;
            cmd.defaultLabel = "GM21_Med_GizmoLabel".Translate(LabelFor(current));
            cmd.defaultDesc = "GM21_Med_GizmoDesc".Translate(LabelFor(current), DescFor(current))
                              + "\n\n" + CostFor(__instance, current);
            cmd.icon = Icon;
            if (!Gm21Medicine.CanPractise(__instance))
            {
                cmd.Disable("GM21_Med_NotPractising".Translate(__instance.LabelShortCap));
            }
            yield return cmd;
        }

        private static bool ShouldShow(Pawn pawn)
        {
            if (pawn == null || pawn.Dead) return false;
            if (pawn.Faction == null || !pawn.Faction.IsPlayer) return false;
            if (!pawn.IsColonist) return false;
            return Gm21Medicine.IsMedicineGrandmaster(pawn);
        }

        internal static string LabelFor(Gm21MedicineMode mode)
        {
            switch (mode)
            {
                case Gm21MedicineMode.Reconstruct: return "GM21_Med_ModeReconstruct".Translate();
                case Gm21MedicineMode.Resuscitate: return "GM21_Med_ModeResuscitate".Translate();
                default: return "GM21_Med_ModeCure".Translate();
            }
        }

        /// <summary>The intervention's price for this Grandmaster: medicine potency and work time.</summary>
        internal static string CostFor(Pawn doctor, Gm21MedicineMode mode)
        {
            int baseTicks;
            switch (mode)
            {
                case Gm21MedicineMode.Reconstruct: baseTicks = Gm21Medicine.ReconstructWorkTicks; break;
                case Gm21MedicineMode.Resuscitate: baseTicks = Gm21Medicine.ResuscitateWorkTicks; break;
                default: baseTicks = Gm21Medicine.CureWorkTicks; break;
            }
            return "GM21_Med_ModeCost".Translate(Gm21MedicineSupplies.BudgetFor(mode).ToString("0.##"),
                Gm21Medicine.WorkTicks(doctor, baseTicks).ToStringTicksToPeriod());
        }

        internal static string DescFor(Gm21MedicineMode mode)
        {
            switch (mode)
            {
                case Gm21MedicineMode.Reconstruct: return "GM21_Med_ModeReconstructDesc".Translate();
                case Gm21MedicineMode.Resuscitate: return "GM21_Med_ModeResuscitateDesc".Translate();
                default: return "GM21_Med_ModeCureDesc".Translate();
            }
        }
    }
}
