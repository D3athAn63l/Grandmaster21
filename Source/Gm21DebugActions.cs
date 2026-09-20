using LudeonTK;
using RimWorld;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// Dev-mode helpers. Per the design brief, deliberate developer cheating is allowed; these
    /// exist so the 20 -> 21 path can be exercised without grinding a billion XP.
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
            foreach (SkillRecord rec in p.skills.skills)
            {
                sb.AppendLine("  " + rec.def.defName
                    + "  levelInt=" + rec.levelInt
                    + "  Level=" + rec.Level
                    + "  aptitude=" + rec.Aptitude
                    + "  xpSinceLastLevel=" + rec.xpSinceLastLevel.ToString("F1")
                    + "  gmXp=" + GrandmasterStore.Get(rec).ToString("N0"));
            }
            Log.Message(sb.ToString());
        }
    }
}
