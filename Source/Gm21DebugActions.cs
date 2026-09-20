using LudeonTK;
using RimWorld;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// Dev-mode helpers. Grandmaster is permanent during normal gameplay, so the only way to
    /// exercise both directions of the transition during testing is a deliberate developer
    /// pathway. None of these weaken the rules for ordinary play: the demote action goes through
    /// the same authorised path the uninstall cleanup uses, and the promote action still refuses
    /// to run if the Learn safety patch did not apply.
    /// </summary>
    public static class Gm21DebugActions
    {
        [DebugAction("Grandmaster 21", "Grant Grandmaster XP (full requirement)",
            actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void GrantFullGrandmasterXp(Pawn p)
        {
            if (p?.skills == null) return;
            foreach (SkillRecord rec in p.skills.skills)
            {
                if (rec.levelInt == Gm21.VanillaMaxLevel && !rec.TotallyDisabled)
                {
                    GrandmasterStore.Set(rec, Gm21Mod.Settings.grandmasterXpRequirement);
                }
            }
            // Nudge each maxed skill so the promotion postfix runs.
            foreach (SkillRecord rec in p.skills.skills)
            {
                if (rec.levelInt == Gm21.VanillaMaxLevel && !rec.TotallyDisabled)
                {
                    rec.Learn(0.01f, true, true);
                }
            }
            Messages.Message("Granted Grandmaster XP to " + p.LabelShortCap, p, MessageTypeDefOf.NeutralEvent, false);
        }

        [DebugAction("Grandmaster 21", "Promote all level 20 skills to Grandmaster",
            actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void PromoteToGrandmaster(Pawn p)
        {
            if (p?.skills == null) return;
            if (!Gm21.PromotionEnabled)
            {
                Messages.Message("Grandmaster 21: promotion is disabled this session (Learn patch did not apply).",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            int n = 0;
            foreach (SkillRecord rec in p.skills.skills)
            {
                if (rec.levelInt == Gm21.VanillaMaxLevel && !rec.TotallyDisabled)
                {
                    Gm21.Promote(rec);
                    if (rec.levelInt >= Gm21.GrandmasterLevel) n++;
                }
            }
            Messages.Message("Promoted " + n + " skill(s) on " + p.LabelShortCap, p, MessageTypeDefOf.NeutralEvent, false);
        }

        [DebugAction("Grandmaster 21", "Demote Grandmaster skills to 20",
            actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DemoteGrandmaster(Pawn p)
        {
            if (p?.skills == null) return;
            int n = 0;
            foreach (SkillRecord rec in p.skills.skills)
            {
                if (Gm21.ForceDemoteForUninstall(rec)) n++;
                GrandmasterStore.Clear(rec);
            }
            Messages.Message("Demoted " + n + " Grandmaster skill(s) on " + p.LabelShortCap,
                p, MessageTypeDefOf.NeutralEvent, false);
        }

        [DebugAction("Grandmaster 21", "Max all skills to 20",
            actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void MaxSkills(Pawn p)
        {
            if (p?.skills == null) return;
            foreach (SkillRecord rec in p.skills.skills)
            {
                if (!rec.TotallyDisabled) rec.Level = Gm21.VanillaMaxLevel;
            }
        }

        [DebugAction("Grandmaster 21", "Report skill state",
            actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ReportSkills(Pawn p)
        {
            if (p?.skills == null) return;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("Grandmaster 21 state for " + p.LabelShortCap + ":");
            sb.AppendLine("  promotion enabled = " + Gm21.PromotionEnabled);
            foreach (SkillRecord rec in p.skills.skills)
            {
                sb.AppendLine("  " + rec.def.defName
                    + "  levelInt=" + rec.levelInt
                    + "  Level=" + rec.Level
                    + "  LevelForUI=" + rec.GetLevelForUI()
                    + "  aptitude=" + rec.Aptitude
                    + "  xpSinceLastLevel=" + rec.xpSinceLastLevel.ToString("F1")
                    + "  gmXp=" + GrandmasterStore.Get(rec).ToString("N0"));
            }
            Log.Message(sb.ToString());
        }
    }
}
