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
    /// Redirect only Frame.CompleteConstruction's single notification call. Audited 1.6 IL:
    /// MakeThing -> local 5; SetQuality; ldloc.s 5; ldarg.1; SendCraftNotification(Thing, Pawn).
    /// The exact finished Thing and worker are already on the stack; no in-flight state is needed.
    /// Cube sculptures, recipes and other direct notification calls are deliberately untouched.
    /// </summary>
    public static class Patch_ConstructionLegendaryNotification
    {
        public static bool Applied { get; private set; }

        public static void Apply(Harmony harmony)
        {
            Applied = false;
            try
            {
                MethodInfo target = AccessTools.Method(typeof(Frame), nameof(Frame.CompleteConstruction), new[] { typeof(Pawn) });
                if (target == null || target.IsStatic || target.ReturnType != typeof(void))
                    throw new MissingMethodException("Frame.CompleteConstruction(Pawn) not found");
                harmony.Patch(target, transpiler: new HarmonyMethod(AccessTools.Method(
                    typeof(Patch_ConstructionLegendaryNotification), nameof(Transpiler))));
            }
            catch (Exception e)
            {
                Applied = false;
                Warn(e.Message);
            }
        }

        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            Applied = false;
            var code = new List<CodeInstruction>(instructions);
            MethodInfo notify = AccessTools.Method(typeof(QualityUtility), nameof(QualityUtility.SendCraftNotification),
                new[] { typeof(Thing), typeof(Pawn) });
            MethodInfo setQuality = AccessTools.Method(typeof(CompQuality), nameof(CompQuality.SetQuality),
                new[] { typeof(QualityCategory), typeof(ArtGenerationContext?) });
            MethodInfo wrapper = AccessTools.Method(typeof(Patch_ConstructionLegendaryNotification), nameof(SendConstructionNotification));
            int site = -1, count = 0;
            for (int i = 0; i < code.Count; i++)
                if (code[i].opcode == OpCodes.Call && Equals(code[i].operand, notify)) { site = i; count++; }

            // Fail open on ambiguity or a changed call shape, including another mod's added call.
            if (notify == null || setQuality == null || wrapper == null || count != 1 || site < 3
                || code[site - 1].opcode != OpCodes.Ldarg_1 || !LoadsFinishedThing(code[site - 2])
                || code[site - 3].opcode != OpCodes.Callvirt || !Equals(code[site - 3].operand, setQuality))
            {
                Warn("expected one verified SetQuality / finished Thing / worker / notification call sequence");
                return code;
            }

            // Same static void(Thing, Pawn) signature. Preserve every opcode, label and exception block.
            code[site].operand = wrapper;
            Applied = true;
            return code;
        }

        private static bool LoadsFinishedThing(CodeInstruction instruction)
        {
            if (instruction.opcode != OpCodes.Ldloc_S && instruction.opcode != OpCodes.Ldloc) return false;
            var local = instruction.operand as LocalVariableInfo;
            return local != null && local.LocalIndex == 5 && local.LocalType == typeof(Thing);
        }

        private static void Warn(string reason)
        {
            Log.Warning("[Grandmaster 21] Construction Grandmaster Legendary notifications setting unavailable ("
                + reason + "). Construction letters remain vanilla.");
        }

        /// <summary>
        /// Presentation only. A suppressed call never enters SendCraftNotification, including its
        /// third-party Harmony hooks. Forwarded calls run that patched method normally. Mods replacing
        /// this call site or sending their own letters are outside this setting's scope.
        /// </summary>
        public static void SendConstructionNotification(Thing thing, Pawn worker)
        {
            Gm21Settings settings = Gm21Mod.Settings;
            ThingWithComps completed = thing as ThingWithComps;
            if (settings != null && !settings.showConstructionGrandmasterLegendaryNotifications
                && completed != null && completed.compQuality != null
                && completed.compQuality.Quality == QualityCategory.Legendary
                && SkillDefOf.Construction != null && Gm21.IsGrandmaster(worker, SkillDefOf.Construction))
                return;

            QualityUtility.SendCraftNotification(thing, worker);
        }
    }
}
