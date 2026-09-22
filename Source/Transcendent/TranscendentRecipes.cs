using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Grandmaster21.Transcendent
{
    [DefOf]
    public static class TranscendentDefOf
    {
        public static ThingDef GM21_MagicalWorkstation;
        public static ThingDef GM21_MagicalCatalyst;
        public static ResearchProjectDef GM21_MagicalCraftsmanship;
        public static JobDef GM21_TranscendentCraft;
        static TranscendentDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(TranscendentDefOf)); }
    }

    [StaticConstructorOnStartup]
    internal static class TranscendentStartup
    {
        static TranscendentStartup() { TranscendentRecipes.Initialize(); }
    }

    internal static class TranscendentRecipes
    {
        internal static readonly List<RecipeDef> Eligible = new List<RecipeDef>();
        internal static readonly HashSet<RecipeDef> EligibleSet = new HashSet<RecipeDef>();
        private static bool initialized;

        internal static void Initialize()
        {
            if (initialized) return;
            initialized = true;
            // Only new definitions receive copied graphics. The source definitions are untouched.
            CopyGraphic(TranscendentDefOf.GM21_MagicalWorkstation, DefDatabase<ThingDef>.GetNamed("TableMachining"));
            CopyGraphic(TranscendentDefOf.GM21_MagicalCatalyst, ThingDefOf.ComponentSpacer);
            foreach (RecipeDef recipe in DefDatabase<RecipeDef>.AllDefsListForReading)
            {
                string reason;
                try { if (!IsSupported(recipe, out reason)) continue; }
                catch (Exception ex)
                {
                    Log.Warning("[Grandmaster 21] Skipping unsupported recipe " + recipe.defName + ": " + ex.Message);
                    continue;
                }
                ThingDef product = recipe.products[0].thingDef;
                // The component must exist before any new game / loaded save instantiates items.
                if (!product.HasComp(typeof(CompArtifact))) product.comps.Add(new CompProperties_Artifact());
                Eligible.Add(recipe);
                EligibleSet.Add(recipe);
            }
            Eligible.Sort((a, b) => string.Compare(a.LabelCap, b.LabelCap, StringComparison.CurrentCulture));
            Log.Message("[Grandmaster 21] Magical crafting: " + Eligible.Count + " supported recipes; vanilla recipes unchanged.");
        }

        private static void CopyGraphic(ThingDef target, ThingDef source)
        {
            target.graphicData = new GraphicData();
            target.graphicData.CopyFrom(source.graphicData);
            target.uiIcon = source.uiIcon;
            target.uiIconColor = source.uiIconColor;
            target.uiIconAngle = source.uiIconAngle;
            // Graphic's setter updates ThingDef's cached graphic, including builds that accessed it early.
            target.graphic = target.graphicData.Graphic;
            if (target.blueprintDef != null && source.blueprintDef != null)
            {
                target.blueprintDef.graphicData = new GraphicData();
                target.blueprintDef.graphicData.CopyFrom(source.blueprintDef.graphicData);
                target.blueprintDef.graphic = target.blueprintDef.graphicData.Graphic;
            }
        }

        internal static bool IsSupported(RecipeDef r, out string reason)
        {
            return IsSupported(r, SkillDefOf.Crafting, TranscendentDefOf.GM21_MagicalCatalyst, out reason);
        }

        // Explicit Def inputs let the policy be tested with real Def instances without a loaded map.
        internal static bool IsSupported(RecipeDef r, SkillDef crafting, ThingDef catalyst, out string reason)
        {
            reason = "Unsupported recipe";
            if (r == null || r.GetType() != typeof(RecipeDef) || r.workerClass != typeof(RecipeWorker)
                || crafting == null || r.workSkill != crafting || r.ingredients.NullOrEmpty()
                || r.products == null || r.products.Count != 1 || r.products[0].count != 1
                || !r.specialProducts.NullOrEmpty() || r.efficiencyStat != null || r.workTableEfficiencyStat != null
                || r.allowMixingIngredients || r.ignoreIngredientCountTakeEntireStacks
                || r.IsSurgery || r.mechanitorOnlyRecipe || r.gestationCycles != 0 || r.formingTicks != 0)
                return false;
            ThingDef p = r.products[0].thingDef;
            if (p == null || p.category != ThingCategory.Item || (!p.IsWeapon && !p.IsApparel)
                || !p.useHitPoints || p.stackLimit != 1 || !p.HasComp(typeof(CompQuality))
                || p.IsIngestible || p.IsMedicine || p.IsDrug || p.IsStuff || p.destroyOnDrop
                || (p.thingClass != typeof(ThingWithComps) && p.thingClass != typeof(Apparel)))
                return false;
            if (p.Verbs != null && p.Verbs.Any(v => v.verbClass != null && typeof(Verb_ShootOneUse).IsAssignableFrom(v.verbClass)))
                return false;
            if (p.comps.Any(c => typeof(CompUsable).IsAssignableFrom(c.compClass)
                || typeof(CompDestroyAfterDelay).IsAssignableFrom(c.compClass)
                || typeof(CompApparelReloadable).IsAssignableFrom(c.compClass)
                || typeof(CompEquippableAbilityReloadable).IsAssignableFrom(c.compClass))) return false;
            if (r.IngredientValueGetter.GetType() != typeof(IngredientValueGetter_Volume)) return false;

            if (p.MadeFromStuff && (!r.productHasIngredientStuff || r.ingredients[0].filter.AllowedThingDefs
                .Any(d => !d.IsStuff || !d.stuffProps.CanMake(p)))) return false;
            if (!p.MadeFromStuff && r.productHasIngredientStuff) return false;

            // Disjoint slots avoid ambiguous allocation and vanilla's overlapping-slot edge cases.
            HashSet<ThingDef> used = new HashSet<ThingDef>();
            foreach (IngredientCount ing in r.ingredients)
            {
                if (ing.GetBaseCount() <= 0 || float.IsNaN(ing.GetBaseCount()) || float.IsInfinity(ing.GetBaseCount())) return false;
                List<ThingDef> defs = ing.filter.AllowedThingDefs.ToList();
                if (defs.Count == 0) return false;
                foreach (ThingDef d in defs)
                {
                    if (!used.Add(d) || d == catalyst
                        || d.category != ThingCategory.Item || d.thingClass != typeof(ThingWithComps)
                        || d.HasComp(typeof(CompQuality)) || d.IsIngestible || d.IsMedicine || d.IsDrug) return false;
                }
            }
            float work = r.WorkAmountForStuff(null);
            if (work < 0 || float.IsNaN(work) || float.IsInfinity(work)) return false;
            reason = null;
            return true;
        }

        internal static IEnumerable<ThingDef> Materials(RecipeDef r)
        {
            return r.ingredients[0].filter.AllowedThingDefs.Where(d => d.IsStuff && d.stuffProps.CanMake(r.products[0].thingDef))
                .OrderBy(d => d.label);
        }

        internal static bool CanCraft(Pawn pawn)
        {
            return pawn != null && Gm21.IsGrandmaster(pawn, SkillDefOf.Crafting)
                && pawn.skills != null && !pawn.skills.GetSkill(SkillDefOf.Crafting).TotallyDisabled
                && !pawn.Dead && !pawn.Downed && !pawn.Drafted && pawn.Awake()
                && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation);
        }

        internal static bool MeetsRecipe(Pawn pawn, RecipeDef recipe)
        {
            return CanCraft(pawn) && recipe != null && (recipe.skillRequirements == null
                || recipe.skillRequirements.All(s => s.PawnSatisfies(pawn)));
        }
    }
}
