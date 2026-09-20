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
        /// <summary>
        /// Mirror of <see cref="Gm21.LearnPatchApplied"/>, kept as the historical public name.
        /// The authoritative flag lives on Gm21 because Gm21.Promote is the thing that has to
        /// consult it.
        /// </summary>
        public static bool TranspilerApplied
        {
            get { return Gm21.LearnPatchApplied; }
        }

        /// <summary>
        /// PREFIX -- two jobs, both cheap. The first test is a field read, so ordinary pawns
        /// below level 20 leave here immediately.
        ///
        /// 1. Grandmaster permanence: a Grandmaster absorbs no negative XP at all, so neither
        ///    SkillRecord.Interval nor any mod-supplied negative XP can walk 21 back to 20.
        ///    This is unconditional -- Grandmaster is an achieved state, not an option.
        /// 2. Grandmaster XP capture at exactly level 20.
        ///
        /// The XP we bank is the *effective* XP, obtained by calling vanilla's own public
        /// LearnRateFactor -- so passion, GlobalLearningFactor, AnimalsLearningFactor, implants,
        /// genes, traits and daily learning saturation all keep applying exactly as they do for
        /// normal levelling. We are not inventing a counter; we are banking real earned XP.
        ///
        /// Evaluation order matches vanilla: LearnRateFactor is read before xpSinceMidnight is
        /// updated, so the saturation state we see is the same one vanilla sees.
        ///
        /// XP is banked even when promotion is disabled (transpiler failed). Banking is purely
        /// additive bookkeeping and cannot destabilise anything; only the promotion itself is
        /// unsafe in that situation, and that is gated separately in Gm21.Promote.
        /// </summary>
        [HarmonyPrefix]
        public static bool Prefix(SkillRecord __instance, float xp, bool direct, bool ignoreLearnRate)
        {
            int level = __instance.levelInt;

            if (level >= Gm21.GrandmasterLevel)
            {
                if (xp < 0f)
                {
                    // Grandmaster mastery is permanent: swallow the loss entirely.
                    return false;
                }

                // Second half of the same guarantee. Vanilla's down-level loop
                //     while (xpSinceLastLevel <= -1000f) { levelInt--; ... }
                // sits OUTSIDE the "levelInt == cap" if/else, so it still runs on a POSITIVE
                // XP event. Swallowing negative XP is therefore not enough on its own: if
                // xpSinceLastLevel is already deeply negative -- written directly by another
                // mod, or carried in from a save made under different rules -- the next point
                // of positive XP would walk the Grandmaster back down.
                // Normalising here costs one float comparison on a branch only Grandmasters
                // reach, and makes the loop unreachable at 21 by construction.
                if (__instance.xpSinceLastLevel < 0f)
                {
                    __instance.xpSinceLastLevel = 0f;
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
        ///
        /// The transpiler gate is checked here as well as inside Gm21.Promote so that the
        /// (already cheap) store lookup is skipped entirely in a failed-patch session.
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix(SkillRecord __instance)
        {
            if (__instance.levelInt != Gm21.VanillaMaxLevel) return;
            if (__instance.TotallyDisabled) return;

            if (GrandmasterStore.Get(__instance) < Gm21Mod.Settings.grandmasterXpRequirement) return;

            if (!Gm21.PromotionEnabled)
            {
                // Requirement met but the environment is unsafe. Warn once, never per XP event.
                Gm21.WarnPromotionDisabledOnce();
                return;
            }

            Gm21.Promote(__instance);
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
        /// Fail-safe: if the pattern is absent the IL is returned untouched, an error is logged,
        /// and Gm21.LearnPatchApplied stays false -- which disables the 20 -> 21 promotion for
        /// the session. The mod degrades to "no new Grandmasters" rather than to a Grandmaster
        /// that vanilla immediately demotes.
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

            Gm21.LearnPatchApplied = true;
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
    /// a Grandmaster. This prefix makes that guarantee explicit rather than an accident of the
    /// jump table's size -- and keeps holding if a future patch widens the table.
    ///
    /// Unconditional: Level 21 does not decay, ever. Levels 0-20 are untouched and decay exactly
    /// as in vanilla.
    /// </summary>
    [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.Interval))]
    public static class Patch_SkillRecord_Interval
    {
        [HarmonyPrefix]
        public static bool Prefix(SkillRecord __instance)
        {
            return __instance.levelInt < Gm21.GrandmasterLevel;
        }
    }
}
