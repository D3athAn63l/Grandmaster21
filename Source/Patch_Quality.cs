using HarmonyLib;
using RimWorld;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// QualityUtility.GenerateQualityCreatedByPawn(int relevantSkillLevel, bool inspired)
    ///
    /// Vanilla: switch over exactly 21 cases (0..20) picks a Gaussian centre, rolls, then
    /// Mathf.Clamp(num, 0, 5). Because that clamp tops out at Masterwork, Legendary is ONLY
    /// ever produced by QualityUtility.AddLevels -- i.e. Inspired Creativity (+2) or an
    /// Ideology Production Specialist offset.
    ///
    /// Note the vanilla 21-hazard this also fixes: level 21 misses every switch case, leaving
    /// the centre at 0 -- a Grandmaster would have rolled *worse than a level-0 pawn*.
    ///
    /// The level-21 branch runs BEFORE the deterministic-quality setting is consulted. That
    /// setting governs how levels 0-20 are rolled; it is not a switch for the Grandmaster rule.
    /// This matters for compatibility: another mod calling this overload directly with 21 (for
    /// instance one that computes an effective skill level itself) gets Legendary rather than
    /// falling into vanilla's caseless default.
    ///
    /// This is the only pawn-created quality chokepoint; non-pawn generators
    /// (GenerateQualityReward / Gift / Super / TraderItem / BaseGen / RandomEqualChance) and
    /// pawn *gear* generation (GenerateQualityGeneratingPawn, used by
    /// PawnGenerator.PostProcessGeneratedGear) are deliberately untouched, so quest rewards,
    /// trader stock, map-gen loot and raider equipment can still be Legendary.
    /// </summary>
    [HarmonyPatch(typeof(QualityUtility), nameof(QualityUtility.GenerateQualityCreatedByPawn),
        new[] { typeof(int), typeof(bool) })]
    public static class Patch_QualityUtility_ByLevel
    {
        [HarmonyPrefix]
        public static bool Prefix(int relevantSkillLevel, ref QualityCategory __result)
        {
            if (relevantSkillLevel >= Gm21.GrandmasterLevel)
            {
                // Unconditional, deterministic setting or not. Grandmaster work is Legendary.
                __result = QualityCategory.Legendary;
                return false;
            }

            if (!Gm21Mod.Settings.deterministicQuality)
            {
                return true; // let vanilla roll; the postfix still enforces the Masterwork ceiling
            }

            __result = Gm21.BandFor(relevantSkillLevel);
            return false;
        }

        /// <summary>
        /// Enforces the core rule regardless of how the value was produced, including when
        /// deterministic quality is switched off and vanilla's "inspired" bonus has been applied.
        ///
        ///   level &gt;= 21 -> Legendary, always
        ///   level &lt;= 20 -> whatever was rolled, capped at Masterwork
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix(int relevantSkillLevel, ref QualityCategory __result)
        {
            if (relevantSkillLevel >= Gm21.GrandmasterLevel)
            {
                __result = QualityCategory.Legendary;
                return;
            }

            if (__result > QualityCategory.Masterwork)
            {
                __result = QualityCategory.Masterwork;
            }
        }
    }

    /// <summary>
    /// QualityUtility.GenerateQualityCreatedByPawn(Pawn, SkillDef, bool)
    ///
    /// This is what crafting (GenRecipe.PostProcessProduct), construction
    /// (Frame.CompleteConstruction) and the Anomaly cube sculpture actually call.
    ///
    /// A postfix is required in addition to the int-overload patch because the Ideology
    /// Production Specialist offset is applied by AddLevels *after* the inner call returns.
    /// Without this, a level-20 Production Specialist could still reach Legendary.
    ///
    /// The two overloads are enforced independently on purpose. Neither relies on the other
    /// being reached: a mod that calls the int overload directly still gets the rule, and a mod
    /// that replaces the inner roll of the Pawn overload still gets the rule.
    ///
    /// Grandmaster status is read from the stored levelInt, not from Level, so a positive
    /// Aptitude from a gene/trait/hediff can never grant Legendary, and a negative Aptitude
    /// cannot strip it from someone who earned it -- even though the inner int call will have
    /// been handed the aptitude-reduced level.
    /// </summary>
    [HarmonyPatch(typeof(QualityUtility), nameof(QualityUtility.GenerateQualityCreatedByPawn),
        new[] { typeof(Pawn), typeof(SkillDef), typeof(bool) })]
    public static class Patch_QualityUtility_ByPawn
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, SkillDef relevantSkill, ref QualityCategory __result)
        {
            if (Gm21.IsGrandmaster(pawn, relevantSkill))
            {
                __result = QualityCategory.Legendary;
                return;
            }

            if (__result > QualityCategory.Masterwork)
            {
                __result = QualityCategory.Masterwork;
            }
        }
    }
}
