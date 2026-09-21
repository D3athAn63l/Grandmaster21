using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// The Grandmaster Melee Doctrine gizmo: one vanilla-style Command_Action opening a
    /// three-entry FloatMenu. No custom combat interface.
    ///
    /// DELIBERATELY DISTINCT FROM THE SHOOTING CONTROL. A pawn who is a Grandmaster at both skills
    /// gets two gizmos, and the brief is explicit that they must not be confusable. Each one names
    /// its skill in its label -- "Grandmaster Melee" and "Grandmaster Aim (Shooting)" -- and each
    /// reads and writes its own store, so setting one never moves the other.
    ///
    /// Shown only for a player-faction colonist with a legitimate stored Melee 21, so an ordinary
    /// pawn's gizmo bar is untouched and the control can never appear for an enemy.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos_MeleeDoctrine
    {
        [HarmonyPostfix]
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            foreach (Gizmo g in __result) yield return g;

            if (!ShouldShow(__instance)) yield break;

            Gm21MeleeDoctrine current = Gm21MeleeDoctrineStore.Get(__instance);
            Pawn pawn = __instance;

            Command_Action cmd = new Command_Action();
            cmd.defaultLabel = "GM21_Melee_GizmoLabel".Translate(LabelFor(current));
            cmd.defaultDesc = "GM21_Melee_GizmoDesc".Translate();
            cmd.icon = ContentFinder<Texture2D>.Get("UI/Commands/Attack", false) ?? BaseContent.BadTex;
            cmd.action = delegate
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>(3);
                AddOption(options, pawn, Gm21MeleeDoctrine.Normal);
                AddOption(options, pawn, Gm21MeleeDoctrine.Killer);
                AddOption(options, pawn, Gm21MeleeDoctrine.Downed);
                Find.WindowStack.Add(new FloatMenu(options));
            };

            yield return cmd;
        }

        private static void AddOption(List<FloatMenuOption> options, Pawn pawn, Gm21MeleeDoctrine doctrine)
        {
            options.Add(new FloatMenuOption(LabelFor(doctrine) + " - " + DescFor(doctrine),
                delegate { Gm21MeleeDoctrineStore.Set(pawn, doctrine); }));
        }

        private static bool ShouldShow(Pawn pawn)
        {
            if (pawn == null || pawn.Dead) return false;
            if (pawn.Faction == null || !pawn.Faction.IsPlayer) return false;
            if (!pawn.IsColonist) return false;
            return Gm21Melee.IsMeleeGrandmaster(pawn);
        }

        internal static string LabelFor(Gm21MeleeDoctrine doctrine)
        {
            switch (doctrine)
            {
                case Gm21MeleeDoctrine.Killer: return "GM21_Melee_Killer".Translate();
                case Gm21MeleeDoctrine.Downed: return "GM21_Melee_Downed".Translate();
                default: return "GM21_Melee_Normal".Translate();
            }
        }

        internal static string DescFor(Gm21MeleeDoctrine doctrine)
        {
            switch (doctrine)
            {
                case Gm21MeleeDoctrine.Killer: return "GM21_Melee_KillerDesc".Translate();
                case Gm21MeleeDoctrine.Downed: return "GM21_Melee_DownedDesc".Translate();
                default: return "GM21_Melee_NormalDesc".Translate();
            }
        }
    }
}
