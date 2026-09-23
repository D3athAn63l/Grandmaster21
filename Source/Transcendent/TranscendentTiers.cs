using System;
using RimWorld;
using Verse;

namespace Grandmaster21.Transcendent
{
    // Balance and public progression live here. Def costs/recipe quantities live in Progression.xml.
    internal sealed class TranscendentTierConfig
    {
        internal readonly ArtifactTier ceiling;
        internal readonly double multiplier, minimum;
        internal readonly string researchName, catalystName, label;
        private TranscendentTierConfig(ArtifactTier tier, double multiplier, double minimum, string catalyst)
        {
            ceiling = tier; this.multiplier = multiplier; this.minimum = minimum;
            label = tier.ToString(); researchName = "GM21_" + label + "Craftsmanship"; catalystName = catalyst;
        }
        internal static readonly TranscendentTierConfig Magical = new TranscendentTierConfig(ArtifactTier.Magical, 3, 45000, "GM21_MagicalCatalyst");
        internal static readonly TranscendentTierConfig Mythical = new TranscendentTierConfig(ArtifactTier.Mythical, 7, 120000, "GM21_MythicalMatrix");
        internal static readonly TranscendentTierConfig Divine = new TranscendentTierConfig(ArtifactTier.Divine, 15, 270000, "GM21_DivineEssence");
        internal static readonly TranscendentTierConfig[] All = { Magical, Mythical, Divine };
        internal ThingDef Catalyst { get { return DefDatabase<ThingDef>.GetNamedSilentFail(catalystName); } }
        internal ResearchProjectDef Research { get { return DefDatabase<ResearchProjectDef>.GetNamedSilentFail(researchName); } }
        internal static TranscendentTierConfig For(ArtifactTier tier)
        {
            switch (tier) { case ArtifactTier.Magical: return Magical; case ArtifactTier.Mythical: return Mythical; case ArtifactTier.Divine: return Divine; default: return null; }
        }
        internal static TranscendentTierConfig ForBench(ThingDef def)
        {
            if (def == null) return null;
            foreach (var config in All) if (def.defName == "GM21_" + config.label + "Workstation") return config;
            return null;
        }
        internal static bool IsCatalyst(ThingDef def)
        {
            if (def == null) return false;
            foreach (var config in All) if (def.defName == config.catalystName) return true;
            return false;
        }
        internal double Work(float original) { return Math.Max(original * multiplier, minimum); }
    }

    internal static class ArtifactRolls
    {
        internal const double NullChance = 0.0001, AnomalyChance = 0.001;
        // Stateless stream: stable across runtime versions; does not perturb Verse.Rand.
        internal static double Unit(int seed, int stream)
        {
            unchecked
            {
                uint x = (uint)seed + 0x9e3779b9u * (uint)(stream + 1);
                x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16;
                return x / 4294967296d;
            }
        }
        internal static ArtifactTier Resolve(ArtifactTier ceiling, double secretNull, double secretAnomaly, double top, double middle, double low)
        {
            if (ceiling == ArtifactTier.Magical) return top < .5 ? ArtifactTier.Magical : ArtifactTier.None;
            if (ceiling == ArtifactTier.Mythical) return top < .4 ? ArtifactTier.Mythical : middle < .7 ? ArtifactTier.Magical : ArtifactTier.None;
            if (ceiling != ArtifactTier.Divine) return ArtifactTier.None;
            if (secretNull < NullChance) return ArtifactTier.Null;
            if (secretAnomaly < AnomalyChance) return ArtifactTier.Anomaly;
            return top < .3 ? ArtifactTier.Divine : middle < .6 ? ArtifactTier.Mythical : low < .8 ? ArtifactTier.Magical : ArtifactTier.None;
        }
        internal static ArtifactTier Roll(ArtifactTier ceiling, int seed)
        {
            // Preserve the established Magical mapping as well as already committed saves.
            if (ceiling == ArtifactTier.Magical) return TranscendentMath.RollFromSeed(seed);
            return Resolve(ceiling, Unit(seed, 0), Unit(seed, 1), Unit(seed, 2), Unit(seed, 3), Unit(seed, 4));
        }
        internal static double Reliability(ArtifactTier tier)
        {
            switch (tier) { case ArtifactTier.Magical: return .08; case ArtifactTier.Mythical: return .18; case ArtifactTier.Divine: return .30; case ArtifactTier.Anomaly: return .60; case ArtifactTier.Null: return .85; default: return 0; }
        }
    }
}
