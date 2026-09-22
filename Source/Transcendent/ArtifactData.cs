using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Grandmaster21.Transcendent
{
    // Stable numeric schema. Only None and Magical are produced by this release.
    internal enum ArtifactTier { None = 0, Magical = 1, Mythical = 2, Divine = 3, Anomaly = 4, Null = 5 }

    public sealed class CompProperties_Artifact : CompProperties
    {
        public CompProperties_Artifact() { compClass = typeof(CompArtifact); }
    }

    public sealed class CompArtifact : ThingComp
    {
        internal ArtifactTier tier;
        internal string projectId;
        internal string initiatorName;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref tier, "gm21ArtifactTier", ArtifactTier.None);
            Scribe_Values.Look(ref projectId, "gm21ProjectId");
            Scribe_Values.Look(ref initiatorName, "gm21InitiatorName");
        }

        private string TierText { get { return tier == ArtifactTier.Magical ? "GM21_TC_Artifact".Translate().ToString() : null; } }
        public override string CompInspectStringExtra() { return TierText; }
        public override string CompTipStringExtra() { return TierText; }
        public override string GetDescriptionPart() { return TierText; }
        public override bool AllowStackWith(Thing other)
        {
            CompArtifact comp = other.TryGetComp<CompArtifact>();
            return comp != null && comp.tier == tier && comp.projectId == projectId;
        }
    }

    // A receipt, not refundable items. No live ingredient references survive commitment.
    public sealed class CommittedIngredient : IExposable
    {
        public ThingDef def;
        public int count;
        public CommittedIngredient() { }
        public CommittedIngredient(ThingDef def, int count) { this.def = def; this.count = count; }
        public void ExposeData()
        {
            Scribe_Defs.Look(ref def, "def");
            Scribe_Values.Look(ref count, "count");
        }
    }

    public sealed class TranscendentProject : IExposable
    {
        public int schemaVersion = 1;
        public string id;
        public RecipeDef recipe;
        public ThingDef product;
        public ThingDef stuff;
        public string initiatorId;
        public string initiatorName;
        public List<CommittedIngredient> ingredients = new List<CommittedIngredient>();
        public double totalWork;
        public double completedWork;
        public int seed;
        internal ArtifactTier finalTier;
        public UnityEngine.Color color = UnityEngine.Color.white;
        public bool applyColor;
        public bool outputCreated;
        public bool outputDelivered;
        public bool faulted;

        public float Progress { get { return totalWork > 0 ? (float)System.Math.Min(1, completedWork / totalWork) : 0f; } }
        public bool Valid
        {
            get
            {
                return schemaVersion == 1 && !string.IsNullOrEmpty(id) && recipe != null && product != null
                    && (!product.MadeFromStuff || (stuff != null && stuff.stuffProps != null && stuff.stuffProps.CanMake(product)))
                    && totalWork > 0 && !double.IsNaN(totalWork) && !double.IsInfinity(totalWork)
                    && completedWork >= 0 && !double.IsNaN(completedWork) && !double.IsInfinity(completedWork)
                    && (finalTier == ArtifactTier.None || finalTier == ArtifactTier.Magical);
            }
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref schemaVersion, "schemaVersion", 1);
            Scribe_Values.Look(ref id, "id");
            Scribe_Defs.Look(ref recipe, "recipe");
            Scribe_Defs.Look(ref product, "product");
            Scribe_Defs.Look(ref stuff, "stuff");
            Scribe_Values.Look(ref initiatorId, "initiatorId");
            Scribe_Values.Look(ref initiatorName, "initiatorName");
            Scribe_Collections.Look(ref ingredients, "ingredients", LookMode.Deep);
            Scribe_Values.Look(ref totalWork, "totalWork");
            Scribe_Values.Look(ref completedWork, "completedWork");
            Scribe_Values.Look(ref seed, "seed");
            Scribe_Values.Look(ref finalTier, "finalTier", ArtifactTier.None);
            Scribe_Values.Look(ref color, "color", UnityEngine.Color.white);
            Scribe_Values.Look(ref applyColor, "applyColor");
            Scribe_Values.Look(ref outputCreated, "outputCreated");
            Scribe_Values.Look(ref outputDelivered, "outputDelivered");
            Scribe_Values.Look(ref faulted, "faulted");
        }
    }

    internal static class TranscendentMath
    {
        // 2,500 ticks/game hour; 12 working hours/day; 1.5 working days at 1 work/tick.
        internal const double MagicalMinimum = 45000;
        internal static double WorkAmount(float original) { return System.Math.Max(original * 3d, MagicalMinimum); }
        internal static double EffectiveSpeed(double actual)
        {
            if (double.IsNaN(actual) || double.IsInfinity(actual) || actual <= 0) return 0;
            return actual <= 1 ? actual : System.Math.Sqrt(actual);
        }
        internal static ArtifactTier RollFromSeed(int seed) { return (seed & 1) == 0 ? ArtifactTier.Magical : ArtifactTier.None; }
    }
}
