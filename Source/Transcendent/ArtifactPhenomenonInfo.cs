using System;
using System.Globalization;
using System.Linq;
using Verse;

namespace Grandmaster21.Transcendent
{
    // Mechanics and descriptions share these values. XML Frost values are checked against them.
    internal static class ArtifactPhenomenonInfo
    {
        internal const int CooldownTicks = 180, MaxTargets = 16, ChainTargets = 4, FrostTicks = 300;
        internal const int GravityStunTicks = 90, RegenTicks = 60, RegenPulseTicks = 10;
        internal const float ChainRadius = 6, SmiteRadius = 5, FlameRadius = 3, FrostRadius = 4, SpatialRadius = 3;
        internal const float ChainDamage = 12, ChainFalloff = 2, SmiteDamage = 22, FlameDamage = 12;
        internal const float GravityDamage = 18, VampireDamage = 14, SpatialDamage = 16, SpatialSecondaryDamage = 8;
        internal const float NormalPenetration = .3f, SpatialPenetration = 2, FrostMoveFactor = .6f;
        internal const float ImmediateFraction = .35f, ImmediateCap = 8, RegenFraction = .2f, RegenCap = 6;
        internal static string Number(float value) { return value.ToString("0.#", CultureInfo.CurrentCulture); }
        internal static float DamageAtJump(int jump, float scale) { return (ChainDamage - jump * ChainFalloff) * scale; }
        internal static object[] DescriptionValues(ArtifactPhenomenon phenomenon, ArtifactTier tier)
        {
            float s = ArtifactEffects.Scale(tier);
            switch (phenomenon)
            {
                case ArtifactPhenomenon.ChainLightning: return new object[] { ChainTargets, ChainRadius, string.Join(" / ", Enumerable.Range(0, ChainTargets).Select(i => Number(DamageAtJump(i, s))).ToArray()) };
                case ArtifactPhenomenon.Smite: return new object[] { SmiteRadius, MaxTargets, Number(SmiteDamage * s) };
                case ArtifactPhenomenon.FlameWave: return new object[] { FlameRadius, MaxTargets, Number(FlameDamage * s) };
                case ArtifactPhenomenon.FrostNova: return new object[] { FrostRadius, MaxTargets, Number(FrostMoveFactor), FrostTicks };
                case ArtifactPhenomenon.GravityCrush: return new object[] { Number(GravityDamage * s), GravityStunTicks };
                case ArtifactPhenomenon.VampiricStrike: return new object[] { Number(VampireDamage * s), ImmediateFraction * 100, ImmediateCap, RegenFraction * 100, RegenCap, RegenTicks, Number(RegenTicks / 60f) };
                case ArtifactPhenomenon.SpatialSlash: return new object[] { Number(SpatialDamage * s), Number(SpatialSecondaryDamage * s), SpatialRadius, SpatialPenetration * 100 };
                default: return new object[0];
            }
        }
        internal static string Describe(ArtifactPhenomenon phenomenon, ArtifactTier tier)
        {
            return string.Format(CultureInfo.CurrentCulture, ("GM21_TC_Effect_" + phenomenon).Translate().ToString(), DescriptionValues(phenomenon, tier));
        }
        internal static string Summary(ArtifactTier tier)
        {
            return "GM21_TC_Chance".Translate((ArtifactRolls.Reliability(tier) * 100).ToString("0.#") + "%")
                + "\n" + "GM21_TC_Cooldown".Translate(CooldownTicks, Number(CooldownTicks / 60f));
        }
        internal static string Details(ArtifactPhenomenon phenomenon, ArtifactTier tier)
        {
            return Summary(tier) + "\n" + Describe(phenomenon, tier) + "\n" + "GM21_TC_TriggerSafety".Translate();
        }
    }
}
