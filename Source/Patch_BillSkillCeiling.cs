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
    /// Vanilla bills assume no skill exceeds 20. A new Bill's allowedSkillRange is 0..20 (the bill
    /// dialog's slider cannot go higher), and Bill.PawnAllowedToStartAnew rejects a worker whose
    /// recipe work skill is above the range's max with "Above allowed skill 20". A legitimate
    /// Grandmaster's skill reads 21, so every ordinary bill using that skill -- a Medicine
    /// Grandmaster's "Remove artificial part" surgery, a Crafting Grandmaster's workbench bill --
    /// was refused before the job could even start. WorkGiver_DoBill calls this gate for every bill
    /// type (Bill_Medical, Bill_Production, Bill_Mech and Bill_Autonomous all call the base first),
    /// so it is fixed here, once.
    ///
    /// THE RULE, and only this: when the upper bound being compared is vanilla's own maximum (20),
    /// the recipe has a work skill, and the pawn is a legitimate stored Grandmaster in exactly that
    /// skill, the bound is read as 21. Everything else is vanilla:
    ///   * a bill capped below 20 (0..15, 5..10) is an intentional restriction and still refuses a
    ///     Grandmaster;
    ///   * the minimum skill check, pawn restriction, slaves/mechs-only, mechanitor, ingredients,
    ///     recipe availability and everything a subclass or WorkGiver adds are untouched;
    ///   * ordinary pawns, Medicine 0-20 pawns, and a pawn whose skill some other mod pushes past 20
    ///     without GM21 storage all see exactly vanilla's comparison -- and the bound only ever
    ///     becomes 21, so nothing reporting above 21 passes either.
    ///
    /// HOW: a transpiler rewrites the operand of that single comparison. Vanilla compiles it as
    ///     ldloc level ; ldarg.0 ; ldflda allowedSkillRange ; ldfld IntRange::max ; ble.s OK
    /// and UpperBoundFor(max, bill, pawn) is inserted right after the ldfld. No Bill field is ever
    /// written -- allowedSkillRange is saved with the bill, so a mutated bill would leak into saves,
    /// need migration and survive uninstalling. The "Above allowed skill" message still reads the
    /// real max. If the method no longer has exactly one such comparison (a game update or another
    /// mod's transpiler), nothing is changed, the bridge is off for the session, and one warning is
    /// logged.
    /// </summary>
    public static class Patch_BillSkillCeiling
    {
        /// <summary>True once the single upper-bound comparison has actually been rewritten.</summary>
        public static bool Applied { get; private set; }

        /// <summary>Called from Gm21Startup after PatchAll, like the Shooting and Melee groups.</summary>
        public static void Apply(Harmony harmony)
        {
            try
            {
                MethodInfo target = AccessTools.Method(typeof(Bill), nameof(Bill.PawnAllowedToStartAnew), new[] { typeof(Pawn) });
                if (target == null) throw new MissingMethodException("Bill.PawnAllowedToStartAnew(Pawn) not found");
                harmony.Patch(target,
                    transpiler: new HarmonyMethod(AccessTools.Method(typeof(Patch_BillSkillCeiling), nameof(Transpiler))));
            }
            catch (Exception e)
            {
                Applied = false;
                Log.Warning("[Grandmaster 21] Could not patch Bill.PawnAllowedToStartAnew (" + e.Message + ").");
            }
            if (!Applied)
            {
                Log.Warning("[Grandmaster 21] The vanilla bill skill-ceiling bridge did not apply: Grandmasters "
                            + "may be refused ordinary bills with \"Above allowed skill 20\" this session. "
                            + "Everything else is unaffected.");
            }
        }

        /// <summary>
        /// The upper bound vanilla should compare against. Pure; never writes to the bill.
        /// </summary>
        public static int UpperBoundFor(int max, Bill bill, Pawn pawn)
        {
            if (max != Gm21.VanillaMaxLevel) return max;                // an intentional cap is respected
            RecipeDef recipe = bill == null ? null : bill.recipe;
            if (recipe == null || recipe.workSkill == null) return max;
            if (!Gm21.IsGrandmaster(pawn, recipe.workSkill)) return max; // only a stored GM21 in THIS skill
            return Gm21.GrandmasterLevel;                                // vanilla's ceiling, not the player's
        }

        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            FieldInfo maxField = AccessTools.Field(typeof(IntRange), nameof(IntRange.max));
            MethodInfo bound = AccessTools.Method(typeof(Patch_BillSkillCeiling), nameof(UpperBoundFor));
            List<CodeInstruction> list = new List<CodeInstruction>(instructions);

            int site = -1, sites = 0;
            for (int i = 0; i + 1 < list.Count; i++)
            {
                if (IsUpperBoundLoad(list[i], list[i + 1], maxField))
                {
                    site = i;
                    sites++;
                }
            }
            Applied = false;
            if (sites != 1 || bound == null)
            {
                Log.Warning("[Grandmaster 21] Bill.PawnAllowedToStartAnew has " + sites
                            + " upper skill-range comparisons (expected exactly 1); left unchanged.");
                return list;
            }

            // Stack before: [level, max]. After: [level, UpperBoundFor(max, this, p)].
            list.InsertRange(site + 1, new[]
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Call, bound)
            });
            Applied = true;
            return list;
        }

        /// <summary>
        /// ldfld IntRange::max immediately followed by a conditional branch: the comparison, not the
        /// message argument (which is followed by a box).
        /// </summary>
        private static bool IsUpperBoundLoad(CodeInstruction load, CodeInstruction next, FieldInfo maxField)
        {
            if (load.opcode != OpCodes.Ldfld || !Equals(load.operand, maxField)) return false;
            return next.opcode.FlowControl == FlowControl.Cond_Branch;
        }
    }
}
