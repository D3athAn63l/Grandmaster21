using System;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// Represents an active transcendent crafting project on a workstation.
    /// This is stored in the workstation's ThingComp and persists through save/load.
    /// 
    /// Key properties:
    /// - Ingredients are consumed immediately when project starts
    /// - Catalyst is consumed immediately when project starts  
    /// - Tier is rolled once at start and never re-rolled
    /// - Project cannot be cancelled (no refund)
    /// - Project belongs to workstation, not crafter
    /// - Any Crafting 21 pawn can continue another's project
    /// </summary>
    [Serializable]
    public class TranscendentProject
    {
        /// <summary>Recipe being crafted.</summary>
        public RecipeDef recipe;
        
        /// <summary>Stuff/material used (if applicable).</summary>
        public ThingDef stuff;
        
        /// <summary>Total work required (after tier multiplier and minimum applied).</summary>
        public int totalWork;
        
        /// <summary>Work completed so far.</summary>
        public int workDone;
        
        /// <summary>The rolled craftsmanship tier. Determined at project start.</summary>
        public CraftsmanshipTier rolledTier;
        
        /// <summary>Random seed for this project. Used for deterministic effects.</summary>
        public int projectSeed;
        
        /// <summary>Original crafter reference (for lore/credits, not gameplay gating).</summary>
        public string originalCrafterName;
        
        /// <summary>Whether this project is currently active.</summary>
        public bool isActive = true;
        
        /// <summary>Progress percentage (0-1).</summary>
        public float ProgressPercent
        {
            get
            {
                if (totalWork <= 0) return 0f;
                return (float)workDone / (float)totalWork;
            }
        }
        
        /// <summary>Remaining work.</summary>
        public int WorkRemaining
        {
            get { return Math.Max(0, totalWork - workDone); }
        }
        
        /// <summary>Whether the project is complete and ready to spawn product.</summary>
        public bool IsComplete
        {
            get { return isActive && workDone >= totalWork; }
        }
        
        /// <summary>
        /// Add work to this project. Returns actual work added (may be capped).
        /// </summary>
        public int AddWork(int work)
        {
            if (!isActive) return 0;
            
            int oldWork = workDone;
            workDone = Math.Min(totalWork, workDone + work);
            return workDone - oldWork;
        }
        
        /// <summary>
        /// Save/load support. Called by the owning workstation comp.
        /// </summary>
        public void ExposeData()
        {
            Scribe_References.Look(ref recipe, "recipe");
            Scribe_References.Look(ref stuff, "stuff");
            Scribe_Values.Look(ref totalWork, "totalWork", 0);
            Scribe_Values.Look(ref workDone, "workDone", 0);
            Scribe_Values.Look(ref rolledTier, "rolledTier", CraftsmanshipTier.None);
            Scribe_Values.Look(ref projectSeed, "projectSeed", 0);
            Scribe_Values.Look(ref originalCrafterName, "originalCrafterName", null);
            Scribe_Values.Look(ref isActive, "isActive", true);
        }
    }
}
