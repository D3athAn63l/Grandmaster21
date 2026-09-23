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
            RecipeDiscoveryReport report = new RecipeDiscoveryReport(Prefs.DevMode);
            foreach (RecipeDef recipe in DefDatabase<RecipeDef>.AllDefsListForReading.OrderBy(r => r.defName, StringComparer.Ordinal))
            {
                string reason;
                try
                {
                    if (IsSupported(recipe, out reason)) RegisterEligible(recipe);
                    report.Record(recipe.defName, reason);
                }
                catch (Exception ex)
                {
                    report.Record(recipe.defName, "exception", ex.GetType().Name);
                }
            }
            Eligible.Sort((a, b) => string.Compare(a.LabelCap, b.LabelCap, StringComparison.CurrentCulture));
            Log.Message(report.Summary);
            foreach (string sample in report.Samples) Log.Message("[Grandmaster 21][Magical][debug] " + sample);
        }

        internal static void RegisterEligible(RecipeDef recipe)
        {
            if (EligibleSet.Contains(recipe)) return;
            ThingDef product = recipe.products[0].thingDef;
            // The component must exist before new games / saves instantiate items. Several
            // recipes may share a product, and repeated registration must not duplicate its comp.
            if (!product.HasComp(typeof(CompArtifact))) product.comps.Add(new CompProperties_Artifact());
            EligibleSet.Add(recipe);
            Eligible.Add(recipe);
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
            if (r == null) return Reject("invalidRecipe", out reason);
            // Custom Def/worker/value-getter logic is not adopted by our completion pipeline.
            // Ordinary implied recipes are plain RecipeDefs; XML inheritance is not CLR inheritance.
            if (r.GetType() != typeof(RecipeDef)) return Reject("recipeClass", out reason);
            if (crafting == null || r.workSkill != crafting) return Reject("wrongSkill", out reason);
            if (r.workerClass != typeof(RecipeWorker)) return Reject("customWorker", out reason);
            if (r.products == null || r.products.Count != 1 || r.products[0] == null || r.products[0].count != 1)
                return Reject("productCount", out reason);
            if (!r.specialProducts.NullOrEmpty()) return Reject("specialProducts", out reason);
            // ResolveReferences assigns WorkTableEfficiencyFactor to ALL ordinary recipes.
            // Its presence is not evidence of special output semantics. Only this standard stat
            // is supported, and its live value must be neutral before ingredients are committed.
            if (r.efficiencyStat != null || (r.workTableEfficiencyStat != null
                && r.workTableEfficiencyStat != StatDefOf.WorkTableEfficiencyFactor)) return Reject("efficiency", out reason);
            if (r.allowMixingIngredients) return Reject("mixedIngredients", out reason);
            if (r.ignoreIngredientCountTakeEntireStacks) return Reject("wholeStacks", out reason);
            if (r.IsSurgery) return Reject("surgery", out reason);
            if (r.mechanitorOnlyRecipe || r.gestationCycles != 0 || r.formingTicks != 0) return Reject("mechRecipe", out reason);
            if (r.ingredients.NullOrEmpty() || r.ingredients.Any(i => i == null || i.filter == null))
                return Reject("ingredientFilter", out reason);
            ThingDef p = r.products[0].thingDef;
            if (p == null || p.category != ThingCategory.Item || !CompatibleItemClass(p.thingClass))
                return Reject("productClass", out reason);
            if (!p.IsWeapon && !p.IsApparel) return Reject("nonEquipment", out reason);
            if (!p.useHitPoints || p.stackLimit != 1) return Reject("notDurable", out reason);
            if (p.comps == null || !p.HasComp(typeof(CompQuality))) return Reject("noQuality", out reason);
            if (p.IsIngestible || p.IsMedicine || p.IsDrug || p.IsStuff || p.destroyOnDrop)
                return Reject("consumable", out reason);
            if (p.Verbs != null && p.Verbs.Any(v => v.verbClass != null && typeof(Verb_ShootOneUse).IsAssignableFrom(v.verbClass)))
                return Reject("consumable", out reason);
            if (p.comps.Any(c => typeof(CompUsable).IsAssignableFrom(c.compClass)
                || typeof(CompDestroyAfterDelay).IsAssignableFrom(c.compClass)
                || typeof(CompApparelReloadable).IsAssignableFrom(c.compClass)
                || typeof(CompEquippableAbilityReloadable).IsAssignableFrom(c.compClass))) return Reject("consumable", out reason);
            if (r.IngredientValueGetter.GetType() != typeof(IngredientValueGetter_Volume)) return Reject("ingredientSemantics", out reason);

            if (p.MadeFromStuff && (!r.productHasIngredientStuff || r.ingredients[0].filter.AllowedThingDefs
                .Any(d => d == null || !d.IsStuff || !d.stuffProps.CanMake(p)))) return Reject("stuffSemantics", out reason);
            if (!p.MadeFromStuff && r.productHasIngredientStuff) return Reject("stuffSemantics", out reason);

            // Disjoint slots avoid ambiguous allocation and vanilla's overlapping-slot edge cases.
            HashSet<ThingDef> used = new HashSet<ThingDef>();
            foreach (IngredientCount ing in r.ingredients)
            {
                if (ing.GetBaseCount() <= 0 || float.IsNaN(ing.GetBaseCount()) || float.IsInfinity(ing.GetBaseCount()))
                    return Reject("ingredientCount", out reason);
                List<ThingDef> defs = ing.filter.AllowedThingDefs.ToList();
                if (defs.Count == 0) return Reject("ingredientFilter", out reason);
                foreach (ThingDef d in defs)
                {
                    if (d == null || d.category != ThingCategory.Item || !CompatibleItemClass(d.thingClass))
                        return Reject("ingredientClass", out reason);
                    if (!used.Add(d)) return Reject("overlappingIngredients", out reason);
                    if (d == catalyst) return Reject("catalystIngredient", out reason);
                    if (d.HasComp(typeof(CompQuality)) || d.IsIngestible || d.IsMedicine || d.IsDrug)
                        return Reject("ingredientKind", out reason);
                }
            }
            float work = r.WorkAmountForStuff(null);
            if (work < 0 || float.IsNaN(work) || float.IsInfinity(work)) return Reject("workAmount", out reason);
            reason = null;
            return true;
        }

        private static bool CompatibleItemClass(Type type)
        {
            return type != null && !type.IsAbstract && !type.ContainsGenericParameters
                && typeof(ThingWithComps).IsAssignableFrom(type) && type.GetConstructor(Type.EmptyTypes) != null
                && !typeof(Corpse).IsAssignableFrom(type) && !typeof(MinifiedThing).IsAssignableFrom(type)
                && !typeof(UnfinishedThing).IsAssignableFrom(type);
        }

        private static bool Reject(string category, out string reason) { reason = category; return false; }

        internal static bool NeutralEfficiency(float value) { return value == 1f; }

        internal static bool HasNeutralEfficiency(RecipeDef recipe, Building_WorkTable bench)
        {
            return recipe.workTableEfficiencyStat == null || NeutralEfficiency(bench.GetStatValue(recipe.workTableEfficiencyStat));
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

    // One startup summary, at most eight dev samples (at most two per category), never per tick.
    internal sealed class RecipeDiscoveryReport
    {
        private readonly bool debug;
        private int scanned, supported;
        private readonly SortedDictionary<string, int> rejected = new SortedDictionary<string, int>(StringComparer.Ordinal);
        internal readonly List<string> Samples = new List<string>();
        internal RecipeDiscoveryReport(bool debug) { this.debug = debug; }
        internal void Record(string defName, string reason, string detail = null)
        {
            scanned++;
            if (reason == null) { supported++; return; }
            int count;
            rejected.TryGetValue(reason, out count);
            rejected[reason] = ++count;
            if (debug && count <= 2 && Samples.Count < 8)
                Samples.Add(defName + " rejected: " + reason + (detail == null ? "" : " (" + detail + ")"));
        }
        internal string Summary
        {
            get
            {
                return "[Grandmaster 21] Magical recipe discovery: scanned=" + scanned + " supported=" + supported
                    + "; rejected: " + (rejected.Count == 0 ? "none" : string.Join(", ", rejected.Select(kv => kv.Key + "=" + kv.Value).ToArray()))
                    + "; research checked in selection dialog; vanilla recipes unchanged.";
            }
        }
    }
}
