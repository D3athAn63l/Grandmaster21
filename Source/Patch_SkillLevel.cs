using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// SkillRecord.set_Level -- the generation gate AND the permanence gate.
    ///
    /// Vanilla body is exactly: levelInt = Mathf.Clamp(value, 0, 20).
    /// Every *generation* path assigns through this property:
    ///     PawnGenerator.GenerateSkills, CreepJoinerUtility.ApplySkillOverrides,
    ///     TraitUtility.ApplySkillGainFromTrait  (verified against 1.6 IL)
    /// while *persistence* (Scribe) writes the levelInt field directly.
    ///
    /// So leaving this clamped at 20 blocks every randomly generated pawn -- colonist, raider,
    /// trader, refugee, quest pawn, ancient, faction leader, slave, prisoner, world pawn, and
    /// modded faction pawns that use the normal pipeline -- from ever starting at 21, with no
    /// need to guess whether a pawn is "new" or "being loaded". Loading is untouched because it
    /// never calls this setter.
    ///
    /// The three cases, in order:
    ///
    ///   1. Authorised scope open  -> the mod itself is doing this. Clamp to [0, 21] and write.
    ///   2. Stored level already 21 -> IGNORE the write completely, whatever the value.
    ///      Grandmaster is an achieved state; ordinary gameplay cannot revoke it. This covers
    ///      "Level = 22" (was already handled) and, crucially, "Level = 20", "Level = 5" and
    ///      "Level = 0" (which the previous clamp-based version let through).
    ///   3. Everyone else -> clamped at 20, unconditionally. There is deliberately no setting
    ///      for this: "generated pawns cannot be 21" is the rule that gives level 21 its meaning,
    ///      so it is not the player's to switch off.
    /// </summary>
    [HarmonyPatch(typeof(SkillRecord), "set_Level")]
    public static class Patch_SkillRecord_SetLevel
    {
        [HarmonyPrefix]
        public static bool Prefix(SkillRecord __instance, int value)
        {
            if (Gm21Authorized.Active)
            {
                __instance.levelInt = Mathf.Clamp(value, Gm21.MinLevel, Gm21.GrandmasterLevel);
                return false;
            }

            if (__instance.levelInt >= Gm21.GrandmasterLevel)
            {
                // Permanent. Swallow the assignment; do not clamp, do not demote.
                return false;
            }

            __instance.levelInt = Mathf.Clamp(value, Gm21.MinLevel, Gm21.VanillaMaxLevel);
            return false;
        }
    }

    /// <summary>
    /// SkillRecord.GetLevel -- vanilla returns Clamp(levelInt + Aptitude, 0, 20).
    ///
    /// This is the MECHANICAL level. SkillRecord.Level forwards to GetLevel(true), and that is
    /// what stat workers, work speed, recipe success chance, surgery odds and so on consume.
    /// Aptitude therefore keeps its ordinary effect here: a Grandmaster with -3 aptitude still
    /// performs like level 18, exactly as a level-20 pawn with -3 aptitude performs like 17.
    ///
    /// The one change is the ceiling: it is raised from 20 to 21 ONLY when the stored level is
    /// already 21. Gating on levelInt rather than on the aptitude-inclusive value is essential:
    /// Aptitude is contributed by genes, traits and hediffs, so a cap of 21 applied
    /// unconditionally would let a randomly generated gene turn an ordinary level-20 pawn into
    /// a "Grandmaster".
    ///
    /// Note what aptitude can NOT do to a Grandmaster, all of which is enforced elsewhere:
    ///   * it cannot change the displayed level      (GetLevelForUI, below)
    ///   * it cannot remove the Grandmaster label     (LevelDescriptor, below)
    ///   * it cannot remove Legendary crafting        (Gm21.IsGrandmaster reads levelInt)
    ///   * it cannot cause a demotion                 (levelInt is never written by aptitude)
    /// </summary>
    [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.GetLevel))]
    public static class Patch_SkillRecord_GetLevel
    {
        [HarmonyPostfix]
        public static void Postfix(SkillRecord __instance, bool includeAptitudes, ref int __result)
        {
            if (__instance.levelInt < Gm21.GrandmasterLevel) return;
            // Vanilla short-circuits to 0 for a disabled skill; respect that.
            if (__instance.TotallyDisabled) return;

            int raw = __instance.levelInt + (includeAptitudes ? __instance.Aptitude : 0);
            __result = Mathf.Clamp(raw, Gm21.MinLevel, Gm21.GrandmasterLevel);
        }
    }

    /// <summary>
    /// SkillRecord.GetLevelForUI -- the DISPLAYED level. SkillUI.DrawSkill and
    /// SkillRecord.LevelDescriptor both read this.
    ///
    /// An earned Grandmaster always displays as 21, regardless of aptitude. Grandmaster is a
    /// permanent achievement: showing "20 * Grandmaster" because a pawn picked up a bad gene
    /// would contradict the stored state the player actually earned.
    ///
    /// Positive aptitude cannot push the display past 21 either -- 21 is the absolute maximum.
    /// </summary>
    [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.GetLevelForUI))]
    public static class Patch_SkillRecord_GetLevelForUI
    {
        [HarmonyPostfix]
        public static void Postfix(SkillRecord __instance, ref int __result)
        {
            if (__instance.levelInt < Gm21.GrandmasterLevel) return;
            if (__instance.PermanentlyDisabled) return;

            __result = Gm21.GrandmasterLevel;
        }
    }

    /// <summary>
    /// SkillRecord.LevelDescriptor is switch(GetLevelForUI(true)) over exactly 21 cases
    /// ("Skill0".."Skill20"). Level 21 falls through the jump table to the default, which
    /// yields no descriptor -- one of the two genuine 21-index hazards found in the audit.
    ///
    /// Gated on levelInt, so a negative aptitude cannot turn "Grandmaster" back into
    /// "Accomplished".
    /// </summary>
    [HarmonyPatch(typeof(SkillRecord), "get_LevelDescriptor")]
    public static class Patch_SkillRecord_LevelDescriptor
    {
        [HarmonyPostfix]
        public static void Postfix(SkillRecord __instance, ref string __result)
        {
            if (__instance.levelInt < Gm21.GrandmasterLevel) return;

            // Per-skill title where one exists ("Grandmaster Marksman" for Shooting), generic
            // otherwise. Same lookup convention as the tooltip text: adding a title for another
            // skill later is a translation-file change, not a code change.
            SkillDef def = __instance.def;
            if (def != null && def.defName != null)
            {
                string key = "GM21_Descriptor_" + def.defName;
                if (key.CanTranslate())
                {
                    __result = key.Translate();
                    return;
                }
            }
            __result = "GM21_GrandmasterDescriptor".Translate();
        }
    }
}
