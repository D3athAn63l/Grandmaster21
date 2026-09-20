using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// The Grandmaster Aim gizmo: one vanilla-style Command_Action opening a three-entry
    /// FloatMenu. No custom combat interface.
    ///
    /// Shown only for a player-faction colonist with a legitimate stored Shooting 21, so an
    /// ordinary pawn's gizmo bar is untouched and the control cannot appear for enemies.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos_AimMode
    {
        [HarmonyPostfix]
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            foreach (Gizmo g in __result) yield return g;

            if (!ShouldShow(__instance)) yield break;

            Gm21AimMode current = Gm21AimModeStore.Get(__instance);
            Pawn pawn = __instance;

            Command_Action cmd = new Command_Action();
            cmd.defaultLabel = "GM21_AimMode_GizmoLabel".Translate(LabelFor(current));
            cmd.defaultDesc = "GM21_AimMode_GizmoDesc".Translate();
            cmd.icon = ContentFinder<Texture2D>.Get("UI/Commands/Attack", false) ?? BaseContent.BadTex;
            cmd.action = delegate
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>(3);
                AddOption(options, pawn, Gm21AimMode.Normal);
                AddOption(options, pawn, Gm21AimMode.Killer);
                AddOption(options, pawn, Gm21AimMode.Downed);
                Find.WindowStack.Add(new FloatMenu(options));
            };

            yield return cmd;
        }

        private static void AddOption(List<FloatMenuOption> options, Pawn pawn, Gm21AimMode mode)
        {
            options.Add(new FloatMenuOption(LabelFor(mode) + " - " + DescFor(mode),
                delegate { Gm21AimModeStore.Set(pawn, mode); }));
        }

        private static bool ShouldShow(Pawn pawn)
        {
            if (pawn == null || pawn.Dead) return false;
            if (pawn.Faction == null || !pawn.Faction.IsPlayer) return false;
            if (!pawn.IsColonist) return false;
            return Gm21Shooting.IsShootingGrandmaster(pawn);
        }

        internal static string LabelFor(Gm21AimMode mode)
        {
            switch (mode)
            {
                case Gm21AimMode.Killer: return "GM21_AimMode_Killer".Translate();
                case Gm21AimMode.Downed: return "GM21_AimMode_Downed".Translate();
                default: return "GM21_AimMode_Normal".Translate();
            }
        }

        internal static string DescFor(Gm21AimMode mode)
        {
            switch (mode)
            {
                case Gm21AimMode.Killer: return "GM21_AimMode_KillerDesc".Translate();
                case Gm21AimMode.Downed: return "GM21_AimMode_DownedDesc".Translate();
                default: return "GM21_AimMode_NormalDesc".Translate();
            }
        }
    }
}
