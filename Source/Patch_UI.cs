using System.Globalization;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// Tooltip line: Grandmaster progress while at 20, achieved state at 21.
    /// A postfix on the description string is the least invasive place to say this -- the
    /// Skills tab itself is not rewritten.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_SkillUI_GetSkillDescription
    {
        // SkillUI.GetSkillDescription is private static in 1.6, so it is targeted explicitly.
        [HarmonyTargetMethod]
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(SkillUI), "GetSkillDescription", new[] { typeof(SkillRecord) });
        }

        [HarmonyPostfix]
        public static void Postfix(SkillRecord sk, ref string __result)
        {
            if (!Gm21Mod.Settings.showGrandmasterProgress) return;
            if (sk == null || sk.TotallyDisabled) return;

            if (sk.levelInt >= Gm21.GrandmasterLevel)
            {
                __result += "\n\n" + "GM21_TooltipAchieved".Translate();
                return;
            }

            if (sk.levelInt == Gm21.VanillaMaxLevel)
            {
                double current = GrandmasterStore.Get(sk);
                double required = Gm21Mod.Settings.grandmasterXpRequirement;
                double percent = GrandmasterStore.ProgressPercent(sk) * 100.0;

                __result += "\n\n" + "GM21_TooltipProgress".Translate(
                    current.ToString("N0", CultureInfo.InvariantCulture),
                    required.ToString("N0", CultureInfo.InvariantCulture),
                    percent.ToString("0.####", CultureInfo.InvariantCulture));
            }
        }
    }

    /// <summary>
    /// Purely cosmetic star on a Grandmaster skill row.
    ///
    /// Drawn in a postfix, after vanilla's BeginGroup/EndGroup has closed, so holdingRect is
    /// back in the caller's coordinate space. Wrapped in try/finally so a UI change can never
    /// leave GUI state dirty, and the whole thing is optional -- functionality does not depend
    /// on it.
    /// </summary>
    [HarmonyPatch(typeof(SkillUI), nameof(SkillUI.DrawSkill),
        new[] { typeof(SkillRecord), typeof(Rect), typeof(SkillUI.SkillDrawMode), typeof(string) })]
    public static class Patch_SkillUI_DrawSkill
    {
        [HarmonyPostfix]
        public static void Postfix(SkillRecord skill, Rect holdingRect, SkillUI.SkillDrawMode mode)
        {
            if (!Gm21Mod.Settings.showGrandmasterProgress) return;
            if (skill == null || skill.levelInt < Gm21.GrandmasterLevel) return;
            if (mode == SkillUI.SkillDrawMode.Gameplay && skill.TotallyDisabled) return;

            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            Color oldColor = GUI.color;
            try
            {
                Rect starRect = new Rect(holdingRect.xMax - 16f, holdingRect.y, 16f, holdingRect.height);
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Small;
                GUI.color = new Color(1f, 0.85f, 0.3f);
                Widgets.Label(starRect, "★");
            }
            finally
            {
                GUI.color = oldColor;
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
            }
        }
    }
}
