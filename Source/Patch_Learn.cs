using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// SkillRecord.Learn -- XP capture, the 21 -> 22 -> 20 guard, and the authorised promotion.
    /// </summary>
    [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.Learn))]
    public static class Patch_SkillRecord_Learn
    {
        /// <summary>Set once by the transpiler so we can warn loudly if the IL shape changed.</summary>
        public static bool TranspilerApplied;

        /// <summary>
        /// PREFIX -- two jobs, both cheap. The first test is a field read, so ordinary pawns
        /// below level 20 leave here immediately.
        ///
        /// 1. Grandmaster decay guard: a Grandmaster absorbs no negative XP at all, so neither
        ///    SkillRecord.Interval nor any mod-supplied negative XP can walk 21 back to 20.
        /// 2. Grandmaster XP capture at exactly level 20.
        ///
        /// The XP we bank is the *effective* XP, obtained by calling vanilla's own public
        /// LearnRateFactor -- so passion, GlobalLearningFactor, AnimalsLearningFactor, implants,
        /// genes, traits and daily learning saturation all keep applying exactly as they do for
        /// normal levelling. We are not inventing a counter; we are banking real earned XP.
        ///
        /// Evaluation order matches vanilla: LearnRateFactor is read before xpSinceMidnight is
        /// updated, so the saturation state we see is the same one vanilla sees.
        /// </summary>
        [HarmonyPrefix]
        public static bool Prefix(SkillRecord __instance, float xp, bool direct, bool ignoreLearnRate)
        {
            int level = __instance.levelInt;

            if (level >= Gm21.GrandmasterLevel)
            {
                if (xp < 0f && Gm21Mod.Settings.grandmasterPreventsDecay)
                {
                    // Grandmaster mastery is permanent: swallow the loss entirely.
                    return false;
                }
                return true;
            }

            if (level == Gm21.VanillaMaxLevel && xp > 0f && !__instance.TotallyDisabled)
            {
                float effective = ignoreLearnRate ? xp : xp * __instance.LearnRateFactor(direct);
                if (effective > 0f)
                {
                    GrandmasterStore.Add(__instance, effective);
                }
            }

            return true;
        }

        /// <summary>
        /// POSTFIX -- authorised 20 -> 21 transition, checked only while sitting at 20.
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix(SkillRecord __instance)
        {
            if (__instance.levelInt != Gm21.VanillaMaxLevel) return;
            if (__instance.TotallyDisabled) return;

            if (GrandmasterStore.Get(__instance) >= Gm21Mod.Settings.grandmasterXpRequirement)
            {
                Gm21.Promote(__instance);
            }
        }

        /// <summary>
        /// TRANSPILER -- one instruction changed.
        ///
        /// Vanilla 1.6 pattern (verified in IL):
        ///
        ///     ldarg.0
        ///     ldfld    int32 RimWorld.SkillRecord::levelInt
        ///     ldc.i4.s 20            &lt;-- replaced
        ///     bne.un   IL_013f
        ///
        /// i.e. "if (levelInt == 20) { clamp surplus XP } else { level-up loop }".
        ///
        /// We swap the literal 20 for Gm21.LearnCapFor(this), which returns 21 for a stored
        /// Grandmaster and 20 for everyone else. Consequences:
        ///
        ///   level 19 -> 19 != 20  -> vanilla level-up loop, unchanged
        ///   level 20 -> 20 == 20  -> vanilla at-cap clamp, unchanged
        ///   level 21 -> 21 == 21  -> at-cap clamp, so vanilla's loop never runs
        ///
        /// That last line is the whole point. Untouched, vanilla would enter the loop at 21,
        /// increment to 22, hit "if (levelInt >= 20) levelInt = 20" and silently demote the
        /// Grandmaster. It also means level 22 remains unreachable: the only writer of 21 is
        /// Gm21.Promote, and the loop that could produce 22 is now unreachable from 21.
        ///
        /// The literal 20 at the loop's own "if (levelInt >= 20) levelInt = 20" is intentionally
        /// left alone, so ordinary levelling still stops dead at 20.
        ///
        /// Fail-safe: if the pattern is absent the IL is returned untouched and an error is
        /// logged. The mod then degrades to "no Grandmaster promotion" rather than corrupting
        /// skill progression.
        /// </summary>
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            FieldInfo levelIntField = AccessTools.Field(typeof(SkillRecord), "levelInt");
            MethodInfo capMethod = AccessTools.Method(typeof(Gm21), nameof(Gm21.LearnCapFor));

            if (levelIntField == null || capMethod == null)
            {
                Log.Error("[Grandmaster 21] Transpiler could not resolve SkillRecord.levelInt or Gm21.LearnCapFor. "
                          + "SkillRecord.Learn left unpatched; Grandmaster promotion is disabled.");
                return code;
            }

            int patchIndex = -1;
            for (int i = 1; i < code.Count - 1; i++)
            {
                if (!IsLdcI4(code[i], 20)) continue;
                if (code[i - 1].opcode != OpCodes.Ldfld) continue;
                if (!ReferenceEquals(code[i - 1].operand, levelIntField)
                    && (code[i - 1].operand as FieldInfo) != levelIntField) continue;
                if (code[i + 1].opcode != OpCodes.Bne_Un && code[i + 1].opcode != OpCodes.Bne_Un_S) continue;

                patchIndex = i;
                break;
            }

            if (patchIndex < 0)
            {
                Log.Error("[Grandmaster 21] Could not find the expected 'levelInt == 20' pattern in "
                          + "SkillRecord.Learn (ldfld levelInt / ldc.i4.s 20 / bne.un). "
                          + "RimWorld's IL has changed. SkillRecord.Learn left unpatched; "
                          + "Grandmaster promotion is disabled, but vanilla skill progression is intact.");
                return code;
            }

            CodeInstruction original = code[patchIndex];
            CodeInstruction loadThis = new CodeInstruction(OpCodes.Ldarg_0);
            loadThis.labels.AddRange(original.labels);
            loadThis.blocks.AddRange(original.blocks);

            code[patchIndex] = loadThis;
            code.Insert(patchIndex + 1, new CodeInstruction(OpCodes.Call, capMethod));

            TranspilerApplied = true;
            return code;
        }

        private static bool IsLdcI4(CodeInstruction ins, int value)
        {
            if (ins.opcode == OpCodes.Ldc_I4_S) return Convert.ToInt32(ins.operand) == value;
            if (ins.opcode == OpCodes.Ldc_I4) return Convert.ToInt32(ins.operand) == value;
            return false;
        }
    }

    /// <summary>
    /// SkillRecord.Interval is the decay driver: switch(levelInt - 10) over 11 cases (10..20).
    /// Level 21 already falls past the jump table to the default 'ret', so vanilla never decays
    /// a Grandmaster. This prefix makes that guarantee explicit and setting-controlled rather
    /// than an accident of the jump table's size.
    /// </summary>
    [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.Interval))]
    public static class Patch_SkillRecord_Interval
    {
        [HarmonyPrefix]
        public static bool Prefix(SkillRecord __instance)
        {
            if (__instance.levelInt >= Gm21.GrandmasterLevel && Gm21Mod.Settings.grandmasterPreventsDecay)
            {
                return false;
            }
            return true;
        }
    }
}
