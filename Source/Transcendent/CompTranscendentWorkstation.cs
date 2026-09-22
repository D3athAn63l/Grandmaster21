using System;
using System.Collections.Generic;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// Component attached to transcendent workstations (Magical/Mythical/Divine).
    /// Manages the committed project system.
    /// 
    /// Key behaviors:
    /// - Only Crafting 21 pawns can start/continue projects
    /// - Ingredients and catalysts consumed at project start
    /// - Tier rolled once at start, never re-rolled
    /// - Project cannot be cancelled (no refund)
    /// - Project persists through save/load
    /// - Destroying workstation destroys project contents (no refund)
    /// </summary>
    public class CompTranscendentWorkstation : ThingComp
    {
        /// <summary>The currently active project, or null if none.</summary>
        public TranscendentProject activeProject;
        
        /// <summary>The tier ceiling this workstation can attempt.</summary>
        public CraftsmanshipTier workstationTier = CraftsmanshipTier.None;
        
        /// <summary>Cached list of eligible recipes for this workstation.</summary>
        private List<RecipeDef> eligibleRecipes;
        
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            
            // Initialize eligible recipes if not already done
            if (eligibleRecipes == null && !respawningAfterLoad)
            {
                eligibleRecipes = DiscoverEligibleRecipes();
            }
        }
        
        public override void PostExposeData()
        {
            base.PostExposeData();
            
            Scribe_Values.Look(ref workstationTier, "workstationTier", CraftsmanshipTier.None);
            
            if (Scribe.mode == LoadSaveMode.Saving || activeProject != null)
            {
                Scribe_Deep.Look(ref activeProject, "activeProject");
            }
            
            // Recipes are re-discovered on load, not saved
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                eligibleRecipes = DiscoverEligibleRecipes();
            }
        }
        
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (var gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }
            
            // Dev mode: show project status
            if (Prefs.DevMode && activeProject != null)
            {
                var infoGizmo = new Command_Info
                {
                    defaultLabel = "Project: " + (activeProject.recipe?.LabelCap ?? "Unknown"),
                    defaultDesc = GetProjectDebugInfo(),
                    icon = ContentFinder<Texture2D>.Get("UI/Icons/DevMode")
                };
                yield return infoGizmo;
                
                // Dev cancel (destroys project, no refund)
                var cancelGizmo = new Command_Action
                {
                    defaultLabel = "DEV: Cancel Project (No Refund)",
                    defaultDesc = "Destroy the active project without refunding ingredients or catalyst.",
                    icon = ContentFinder<Texture2D>.Get("UI/Icons/Cancel"),
                    action = () =>
                    {
                        activeProject.isActive = false;
                        activeProject = null;
                        Messages.Message("Dev: Project cancelled (no refund)", MessageTypeDefOf.NeutralEvent);
                    }
                };
                yield return cancelGizmo;
            }
        }
        
        /// <summary>
        /// Check if a pawn is eligible to start or continue transcendent projects.
        /// Must have Crafting skill at level 21 (stored level, not temporary buffs).
        /// </summary>
        public static bool PawnIsEligible(Pawn pawn)
        {
            if (pawn == null) return false;
            return Gm21.IsGrandmaster(pawn, SkillDefOf.Crafting);
        }
        
        /// <summary>
        /// Discover eligible recipes for transcendent crafting.
        /// Filters based on: must be craftable, must have quality, must be equipment/apparel.
        /// </summary>
        private List<RecipeDef> DiscoverEligibleRecipes()
        {
            var result = new List<RecipeDef>();
            
            foreach (var recipe in DefDatabase<RecipeDef>.AllDefs)
            {
                if (IsRecipeEligible(recipe))
                {
                    result.Add(recipe);
                }
            }
            
            return result;
        }
        
        /// <summary>
        /// Check if a recipe is eligible for transcendent crafting.
        /// Conservative filter: weapons, apparel, armor only. Excludes food, drugs, components, etc.
        /// </summary>
        private bool IsRecipeEligible(RecipeDef recipe)
        {
            // Must produce a thing
            if (recipe.products == null || recipe.products.Count == 0)
                return false;
            
            var product = recipe.products[0];
            var thingDef = product.thingDef;
            
            if (thingDef == null)
                return false;
            
            // Must have quality category (not buildings, resources, etc.)
            if (!thingDef.HasQualityCategory())
                return false;
            
            // Exclude consumables
            if (IsConsumable(thingDef))
                return false;
            
            // Prefer weapons and apparel
            if (thingDef.IsWeapon || thingDef.IsApparel)
                return true;
            
            return false;
        }
        
        /// <summary>
        /// Check if a thing is a consumable (food, drug, medicine, etc.).
        /// </summary>
        private bool IsConsumable(ThingDef thingDef)
        {
            // Food
            if (thingDef.IsNutritionGivingIngestible)
                return true;
            
            // Drugs
            if (thingDef.IsDrug)
                return true;
            
            // Medicine
            if (thingDef.Medicine != null)
                return true;
            
            // Components and raw resources
            if (thingDef.category == ThingCategory.Item && 
                (thingDef.IsResourcesStuff || thingDef.defName.Contains("Component")))
                return true;
            
            return false;
        }
        
        /// <summary>
        /// Start a new transcendent project.
        /// Returns true if successful, false if requirements not met.
        /// Consumes ingredients and catalyst immediately.
        /// </summary>
        public bool StartProject(Pawn crafter, RecipeDef recipe, ThingDef stuff, CraftsmanshipTier targetTier)
        {
            if (activeProject != null && activeProject.isActive)
            {
                Log.Warning($"[GM21] Workstation already has an active project");
                return false;
            }
            
            if (!PawnIsEligible(crafter))
            {
                Log.Warning($"[GM21] Pawn {crafter.Name} is not Crafting 21, cannot start transcendent project");
                return false;
            }
            
            // Calculate total work required
            int totalWork = CalculateTotalWork(recipe.workAmount, targetTier);
            
            // Roll the tier (once, at start)
            CraftsmanshipTier rolledTier = RollTier(targetTier);
            
            // Create the project
            activeProject = new TranscendentProject
            {
                recipe = recipe,
                stuff = stuff,
                totalWork = totalWork,
                workDone = 0,
                rolledTier = rolledTier,
                projectSeed = Find.TickManager.TicksGame ^ (int)(UnityEngine.Random.value * 10000),
                originalCrafterName = crafter.Name.ToStringShort,
                isActive = true
            };
            
            // Consume ingredients and catalyst here
            // (This would be called from a bill/job driver in full implementation)
            ConsumeIngredientsAndCatalyst(recipe, stuff, targetTier);
            
            Log.Message($"[GM21] Started {targetTier} project: {recipe.LabelCap} (rolled: {rolledTier})");
            
            return true;
        }
        
        /// <summary>
        /// Calculate total work required based on recipe work and tier multiplier/minimum.
        /// </summary>
        private int CalculateTotalWork(int baseWork, CraftsmanshipTier tier)
        {
            float multiplier;
            int minimum;
            
            switch (tier)
            {
                case CraftsmanshipTier.Magical:
                    multiplier = Gm21TranscendentConfig.MagicalWorkMultiplier;
                    minimum = Gm21TranscendentConfig.MagicalMinimumWork;
                    break;
                case CraftsmanshipTier.Mythical:
                    multiplier = Gm21TranscendentConfig.MythicalWorkMultiplier;
                    minimum = Gm21TranscendentConfig.MythicalMinimumWork;
                    break;
                case CraftsmanshipTier.Divine:
                    multiplier = Gm21TranscendentConfig.DivineWorkMultiplier;
                    minimum = Gm21TranscendentConfig.DivineMinimumWork;
                    break;
                default:
                    multiplier = 1f;
                    minimum = baseWork;
                    break;
            }
            
            return Math.Max((int)(baseWork * multiplier), minimum);
        }
        
        /// <summary>
        /// Roll the craftsmanship tier based on workstation ceiling.
        /// Uses downward roll from highest tier.
        /// </summary>
        private CraftsmanshipTier RollTier(CraftsmanshipTier ceiling)
        {
            float roll = UnityEngine.Random.value;
            
            switch (ceiling)
            {
                case CraftsmanshipTier.Magical:
                    if (roll < Gm21TranscendentConfig.MagicalChance_Magical)
                        return CraftsmanshipTier.Magical;
                    return CraftsmanshipTier.None; // Legendary
                    
                case CraftsmanshipTier.Mythical:
                    if (roll < Gm21TranscendentConfig.MythicalChance_Mythical)
                        return CraftsmanshipTier.Mythical;
                    roll -= Gm21TranscendentConfig.MythicalChance_Mythical;
                    if (roll / (1f - Gm21TranscendentConfig.MythicalChance_Mythical) < Gm21TranscendentConfig.MythicalChance_Magical_GivenNotMythical)
                        return CraftsmanshipTier.Magical;
                    return CraftsmanshipTier.None; // Legendary
                    
                case CraftsmanshipTier.Divine:
                    if (roll < Gm21TranscendentConfig.DivineChance_Divine)
                        return CraftsmanshipTier.Divine;
                    roll -= Gm21TranscendentConfig.DivineChance_Divine;
                    float remaining = 1f - Gm21TranscendentConfig.DivineChance_Divine;
                    if (roll / remaining < Gm21TranscendentConfig.DivineChance_Mythical_GivenNotDivine)
                        return CraftsmanshipTier.Mythical;
                    roll -= remaining * Gm21TranscendentConfig.DivineChance_Mythical_GivenNotDivine;
                    remaining *= (1f - Gm21TranscendentConfig.DivineChance_Mythical_GivenNotDivine);
                    if (roll / remaining < Gm21TranscendentConfig.DivineChance_Magical_GivenNotMythicalOrDivine)
                        return CraftsmanshipTier.Magical;
                    return CraftsmanshipTier.None; // Legendary
                    
                default:
                    return CraftsmanshipTier.None;
            }
        }
        
        /// <summary>
        /// Consume ingredients and catalyst for the project.
        /// This is a placeholder - actual implementation depends on bill/job system.
        /// </summary>
        private void ConsumeIngredientsAndCatalyst(RecipeDef recipe, ThingDef stuff, CraftsmanshipTier tier)
        {
            // Placeholder: In full implementation, this consumes:
            // 1. Recipe ingredients from map/storage
            // 2. Appropriate catalyst (Magical/Mythical/Divine Catalyst)
            // For now, just log what would be consumed
            Log.Message($"[GM21] Would consume ingredients for {recipe.LabelCap} + {tier} catalyst");
        }
        
        /// <summary>
        /// Add work to the active project. Called by the working pawn each tick.
        /// Applies diminishing returns to crafting speed.
        /// </summary>
        public int AddWorkToProject(Pawn worker, int workAmount)
        {
            if (activeProject == null || !activeProject.isActive)
                return 0;
            
            if (!PawnIsEligible(worker))
            {
                Log.Warning($"[GM21] Pawn {worker.Name} is not Crafting 21, cannot work on transcendent project");
                return 0;
            }
            
            // Apply diminishing returns to crafting speed
            float craftingSpeed = worker.GetStatValue(StatDefOf.GeneralLaborSpeed);
            float effectiveSpeed = Gm21TranscendentConfig.ApplyDiminishingReturns(craftingSpeed);
            
            int actualWork = (int)(workAmount * effectiveSpeed);
            int added = activeProject.AddWork(actualWork);
            
            // Check if project is complete
            if (activeProject.IsComplete)
            {
                CompleteProject(worker);
            }
            
            return added;
        }
        
        /// <summary>
        /// Complete the project and spawn the finished product.
        /// </summary>
        private void CompleteProject(Pawn completer)
        {
            if (activeProject == null || !activeProject.IsComplete)
                return;
            
            var recipe = activeProject.recipe;
            var stuff = activeProject.stuff;
            var tier = activeProject.rolledTier;
            
            // Spawn the product
            var product = ThingMaker.MakeThing(recipe.products[0].thingDef, stuff);
            
            // Set quality to Legendary
            var qualityComp = product.TryGetComp<CompQuality>();
            if (qualityComp != null)
            {
                qualityComp.SetQuality(QualityCategory.Legendary, StatContext.Crafter);
            }
            
            // Attach craftsmanship tier component
            var tierComp = product.TryGetComp<CompCraftsmanshipTier>();
            if (tierComp != null)
            {
                tierComp.Initialize(tier, activeProject.projectSeed);
            }
            
            // Spawn the product near the workstation
            GenPlace.TryPlaceThing(product, parent.Position, parent.Map, ThingPlaceMode.Near);
            
            Log.Message($"[GM21] Completed {tier} {recipe.LabelCap} (by {completer.Name})");
            
            // Clear the project
            activeProject = null;
        }
        
        /// <summary>
        /// Called when the workstation is destroyed.
        /// Active project is lost with no refund.
        /// </summary>
        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            
            if (activeProject != null && activeProject.isActive)
            {
                Log.Message($"[GM21] Workstation destroyed - project {activeProject.recipe?.LabelCap} lost (no refund)");
                activeProject.isActive = false;
                activeProject = null;
            }
        }
        
        /// <summary>
        /// Get debug info string for the active project.
        /// </summary>
        private string GetProjectDebugInfo()
        {
            if (activeProject == null)
                return "No active project";
            
            return $"Recipe: {activeProject.recipe?.LabelCap}\n" +
                   $"Progress: {activeProject.workDone}/{activeProject.totalWork} ({activeProject.ProgressPercent:P1})\n" +
                   $"Rolled tier: {activeProject.rolledTier}\n" +
                   $"Original crafter: {activeProject.originalCrafterName}\n" +
                   $"Active: {activeProject.isActive}";
        }
    }
}
