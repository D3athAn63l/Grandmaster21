namespace Grandmaster21
{
    /// <summary>
    /// Craftsmanship tier above Legendary. This is separate from QualityCategory to maintain
    /// vanilla compatibility. Items are QualityCategory.Legendary with an attached tier.
    /// 
    /// Progression: None < Magical < Mythical < Divine
    /// 
    /// Hidden/internal tiers (unreachable in this PR):
    /// - Anomaly: Placeholder for future expansion
    /// - Null: Placeholder for future expansion
    /// </summary>
    public enum CraftsmanshipTier
    {
        /// <summary>No transcendent tier. Item is ordinary Legendary.</summary>
        None = 0,
        
        /// <summary>First transcendent tier. Accessible via Magical workstation.</summary>
        Magical = 1,
        
        /// <summary>Second transcendent tier. Accessible via Mythical workstation.</summary>
        Mythical = 2,
        
        /// <summary>Third transcendent tier. Accessible via Divine workstation.</summary>
        Divine = 3,
        
        // === HIDDEN TIERS BELOW - NOT ROLLABLE IN THIS PR ===
        
        /// <summary>Internal placeholder. Chance = 0. Not documented. Not UI-visible.</summary>
        Anomaly = 4,
        
        /// <summary>Internal placeholder. Chance = 0. Not documented. Not UI-visible.</summary>
        Null = 5
    }
    
    /// <summary>
    /// Centralized configuration for transcendent crafting system.
    /// All numbers should be tuned here for easy adjustment.
    /// </summary>
    public static class Gm21TranscendentConfig
    {
        // === WORK AMOUNT MULTIPLIERS ===
        
        /// <summary>Magical project work = max(recipeWork * 3, minimumWork)</summary>
        public const float MagicalWorkMultiplier = 3f;
        
        /// <summary>Mythical project work = max(recipeWork * 7, minimumWork)</summary>
        public const float MythicalWorkMultiplier = 7f;
        
        /// <summary>Divine project work = max(recipeWork * 15, minimumWork)</summary>
        public const float DivineWorkMultiplier = 15f;
        
        // === MINIMUM WORK AMOUNTS (in ticks) ===
        // RimWorld: 1 second = 60 ticks, 1 minute = 3600 ticks
        // Ordinary working day ~1500-2000 ticks of actual work time
        
        /// <summary>Magical minimum: ~1-2 working days (6000 ticks)</summary>
        public const int MagicalMinimumWork = 6000;
        
        /// <summary>Mythical minimum: ~3-5 working days (18000 ticks)</summary>
        public const int MythicalMinimumWork = 18000;
        
        /// <summary>Divine minimum: ~7-12 working days (45000 ticks)</summary>
        public const int DivineMinimumWork = 45000;
        
        // === TIER ROLL PROBABILITIES ===
        // Roll downward from highest ceiling. Probabilities are centralized here.
        
        // Magical workstation
        public const float MagicalChance_Magical = 0.50f;
        
        // Mythical workstation
        public const float MythicalChance_Mythical = 0.40f;
        public const float MythicalChance_Magical_GivenNotMythical = 0.70f;
        
        // Divine workstation
        public const float DivineChance_Divine = 0.30f;
        public const float DivineChance_Mythical_GivenNotDivine = 0.60f;
        public const float DivineChance_Magical_GivenNotMythicalOrDivine = 0.80f;
        
        // === HIDDEN TIER CHANCES (MUST BE ZERO IN THIS PR) ===
        public const float AnomalyChance = 0f;
        public const float NullChance = 0f;
        
        // === CRAFTING SPEED DIMINISHING RETURNS ===
        /// <summary>
        /// Effective transcendent speed formula:
        /// if craftingSpeed <= 1: use normally
        /// if craftingSpeed > 1: use sqrt(craftingSpeed)
        /// 
        /// Examples: 1x->1x, 4x->2x, 9x->3x, 25x->5x
        /// </summary>
        public static float ApplyDiminishingReturns(float craftingSpeed)
        {
            if (craftingSpeed <= 1f) return craftingSpeed;
            return (float)Math.Sqrt(craftingSpeed);
        }
    }
}
