using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// SkillRecord.set_Level -- the generation gate.
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
    /// The one difference from vanilla: an already-earned Grandmaster is not demoted when some
    /// other mod assigns a level above the cap.
    /// </summary>
    [HarmonyPatch(typeof(SkillRecord), "set_Level")]
    public static class Patch_SkillRecord_SetLevel
    {
        [HarmonyPrefix]
        public static bool Prefix(SkillRecord __instance, int value)
        {
            int cap;
            if (__instance.levelInt >= Gm21.GrandmasterLevel)
            {
                // Already a Grandmaster: never knocked back down by a stray assignment.
                cap = Gm21.GrandmasterLevel;
            }
            else
            {
                cap = Gm21Mod.Settings.clampGeneratedPawns ? Gm21.VanillaMaxLevel : Gm21.GrandmasterLevel;
            }

            __instance.levelInt = Mathf.Clamp(value, Gm21.MinLevel, cap);
            return false;
        }
    }

    /// <summary>
    /// SkillRecord.GetLevel -- vanilla returns Clamp(levelInt + Aptitude, 0, 20).
    ///
    /// We raise the ceiling to 21 ONLY when the stored level is already 21. Gating on levelInt
    /// rather than on the aptitude-inclusive value is essential: Aptitude is contributed by
    /// genes, traits and hediffs, so a cap of 21 applied unconditionally would let a randomly
    /// generated gene turn an ordinary level-20 pawn into a "Grandmaster".
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

    /// <summary>Same treatment for the UI variant, which gates on PermanentlyDisabled.</summary>
    [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.GetLevelForUI))]
    public static class Patch_SkillRecord_GetLevelForUI
    {
        [HarmonyPostfix]
        public static void Postfix(SkillRecord __instance, bool includeAptitudes, ref int __result)
        {
            if (__instance.levelInt < Gm21.GrandmasterLevel) return;
            if (__instance.PermanentlyDisabled) return;

            int raw = __instance.levelInt + (includeAptitudes ? __instance.Aptitude : 0);
            __result = Mathf.Clamp(raw, Gm21.MinLevel, Gm21.GrandmasterLevel);
        }
    }

    /// <summary>
    /// SkillRecord.LevelDescriptor is switch(GetLevelForUI(true)) over exactly 21 cases
    /// ("Skill0".."Skill20"). Level 21 falls through the jump table to the default, which
    /// yields no descriptor -- one of the two genuine 21-index hazards found in the audit.
    /// </summary>
    [HarmonyPatch(typeof(SkillRecord), "get_LevelDescriptor")]
    public static class Patch_SkillRecord_LevelDescriptor
    {
        [HarmonyPostfix]
        public static void Postfix(SkillRecord __instance, ref string __result)
        {
            if (__instance.levelInt >= Gm21.GrandmasterLevel)
            {
                __result = "GM21_GrandmasterDescriptor".Translate();
            }
        }
    }
}
