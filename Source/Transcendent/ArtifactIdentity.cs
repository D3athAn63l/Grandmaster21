using Verse;

namespace Grandmaster21.Transcendent
{
    internal static class ArtifactIdentity
    {
        internal static bool Eligible(ThingDef product, ArtifactPhenomenon phenomenon)
        {
            if (phenomenon == ArtifactPhenomenon.None) return true;
            return product != null && product.IsWeapon && (int)phenomenon >= 1 && (int)phenomenon <= 7;
        }
        internal static bool Valid(ArtifactTier tier, ArtifactPhenomenon phenomenon, ThingDef product)
        {
            return (int)tier >= 0 && (int)tier <= 5 && Eligible(product, phenomenon)
                && (tier != ArtifactTier.None || phenomenon == ArtifactPhenomenon.None);
        }
        internal static ArtifactPhenomenon Assign(ThingDef product, ArtifactTier tier, int seed)
        {
            if (tier == ArtifactTier.None || product == null || !product.IsWeapon) return ArtifactPhenomenon.None;
            return (ArtifactPhenomenon)(1 + (int)(ArtifactRolls.Unit(seed, 10) * 7));
        }
        internal static string Label(ArtifactPhenomenon p) { return ("GM21_TC_Phenomenon_" + p).Translate(); }
    }
}
