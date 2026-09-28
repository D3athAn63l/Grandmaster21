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
    /// Keep the resolved learning-rate pipeline, including other mods' prefixes/postfixes.
    /// Replace only the condition guarding vanilla's verified daily-saturation multiplication.
    /// A final-rate /0.2 postfix would incorrectly amplify additive modifiers applied after it.
    /// </summary>
    [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.LearnRateFactor))]
    public static class Patch_SkillRecord_LearnRateFactor
    {
        public static bool Applied { get; private set; }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            Applied = false;
            var code = new List<CodeInstruction>(instructions);
            MethodInfo getter = AccessTools.PropertyGetter(typeof(SkillRecord), nameof(SkillRecord.LearningSaturatedToday));
            MethodInfo replacement = AccessTools.Method(typeof(Gm21AspirantLearning), "SaturatedForRate");
            int site = -1, count = 0;
            for (int i = 0; i + 5 < code.Count; i++)
            {
                // Verified 1.6: call get_LearningSaturatedToday; brfalse; ldloc.0; ldc.r4 0.2; mul; stloc.0.
                if ((code[i].opcode != OpCodes.Call && code[i].opcode != OpCodes.Callvirt) || !Equals(code[i].operand, getter)) continue;
                if (code[i + 1].opcode != OpCodes.Brfalse && code[i + 1].opcode != OpCodes.Brfalse_S) continue;
                if (code[i + 2].opcode != OpCodes.Ldloc_0 || code[i + 3].opcode != OpCodes.Ldc_R4
                    || !(code[i + 3].operand is float) || (float)code[i + 3].operand != 0.2f
                    || code[i + 4].opcode != OpCodes.Mul || code[i + 5].opcode != OpCodes.Stloc_0) continue;
                site = i;
                count++;
            }
            if (getter == null || replacement == null || count != 1)
            {
                Log.Warning("[Grandmaster 21] Could not identify the vanilla LearnRateFactor saturation branch exactly once. "
                    + "Aspirant support is disabled; existing GM XP banking and promotion remain available.");
                return code;
            }
            // Preserve the original instruction's labels and exception blocks.
            code[site].opcode = OpCodes.Call;
            code[site].operand = replacement;
            Applied = true;
            return code;
        }
    }
}
