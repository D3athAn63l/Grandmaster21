using System;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// Persistent component attached to transcendent artifacts.
    /// Stores the craftsmanship tier and seed for future phenomenon effects.
    /// 
    /// This component survives save/load, inventory transfer, hauling, caravan,
    /// storage, equip/unequip - everything except destruction of the item itself.
    /// </summary>
    public class CompCraftsmanshipTier : ThingComp
    {
        /// <summary>The transcendent tier of this artifact. None = ordinary Legendary.</summary>
        public CraftsmanshipTier tier = CraftsmanshipTier.None;
        
        /// <summary>
        /// Stable random seed for this artifact. Used for deterministic phenomenon effects.
        /// Set once when the project is started or completed. Never changes.
        /// </summary>
        public int artifactSeed;
        
        /// <summary>
        /// Optional placeholder for future phenomenon ID. Not used in this PR.
        /// Reserved for Astra's phenomenon framework expansion.
        /// </summary>
        public string phenomenonId;
        
        /// <summary>Parent item's quality category (cached for convenience).</summary>
        public QualityCategory cachedQuality;
        
        public override void PostExposeData()
        {
            base.PostExposeData();
            
            Scribe_Values.Look(ref tier, "tier", CraftsmanshipTier.None);
            Scribe_Values.Look(ref artifactSeed, "artifactSeed", 0);
            Scribe_Values.Look(ref phenomenonId, "phenomenonId", null);
            Scribe_Values.Look(ref cachedQuality, "cachedQuality", QualityCategory.Normal);
        }
        
        public override void PostPostMake()
        {
            base.PostPostMake();
            
            // Cache the quality category for quick access
            if (parent.TryGetComp<CompQuality>() != null)
            {
                cachedQuality = parent.GetComp<CompQuality>().Quality;
            }
        }
        
        /// <summary>
        /// Initialize this component with the given tier and seed.
        /// Called when a transcendent project completes.
        /// </summary>
        public void Initialize(CraftsmanshipTier newTier, int seed)
        {
            tier = newTier;
            artifactSeed = seed;
            phenomenonId = null; // Reserved for future
            
            // Ensure the parent has Legendary quality
            var qualityComp = parent.TryGetComp<CompQuality>();
            if (qualityComp != null && qualityComp.Quality != QualityCategory.Legendary)
            {
                qualityComp.SetQuality(QualityCategory.Legendary, StatContext.Crafter);
            }
            
            cachedQuality = QualityCategory.Legendary;
        }
        
        /// <summary>
        /// Returns true if this item has any transcendent tier (Magical/Mythical/Divine).
        /// Anomaly and Null are excluded as they are internal-only.
        /// </summary>
        public bool HasTranscendentTier
        {
            get
            {
                return tier == CraftsmanshipTier.Magical ||
                       tier == CraftsmanshipTier.Mythical ||
                       tier == CraftsmanshipTier.Divine;
            }
        }
        
        /// <summary>
        /// Get the label for this tier (for debug/display purposes).
        /// </summary>
        public string TierLabel
        {
            get
            {
                switch (tier)
                {
                    case CraftsmanshipTier.Magical: return "Magical";
                    case CraftsmanshipTier.Mythical: return "Mythical";
                    case CraftsmanshipTier.Divine: return "Divine";
                    case CraftsmanshipTier.Anomaly: return "Anomaly"; // Internal only
                    case CraftsmanshipTier.Null: return "Null"; // Internal only
                    default: return "Legendary";
                }
            }
        }
    }
}
