using System;
using RimWorld;
using Verse;

namespace Grandmaster21
{
    /// <summary>Level-20 support inside the existing Learn -> GrandmasterStore -> promotion path.</summary>
    internal static class Gm21AspirantLearning
    {
        internal const double CriticalXpScale = 100000.0;
        internal const double CriticalBonusFraction = 0.05;

        // Scopes only the GM bookkeeping query, not vanilla's own XP, the UI or other skills.
        // A nested rate query for another skill must not inherit the exemption.
        [ThreadStatic] private static SkillRecord rateScope;
        [ThreadStatic] private static SkillRecord forceInsightFor;

        internal static float ResolvedRate(SkillRecord skill, bool direct)
        {
            SkillRecord previous = rateScope;
            rateScope = skill;
            try { return skill.LearnRateFactor(direct); }
            finally { rateScope = previous; }
        }

        internal static bool SaturatedForRate(SkillRecord skill)
        {
            if (ReferenceEquals(rateScope, skill) && skill.levelInt == Gm21.VanillaMaxLevel) return false;
            return skill.LearningSaturatedToday;
        }

        internal static void Capture(SkillRecord skill, float xp, bool direct, bool ignoreLearnRate)
        {
            // The Learn prefix owns level/disabled eligibility. Preserve its float rate semantics;
            // bank and bonus arithmetic remain double. Reject invalid inputs instead of poisoning the store.
            if (float.IsInfinity(xp)) return;
            float effective = ignoreLearnRate ? xp : xp * ResolvedRate(skill, direct);
            if (!(effective > 0f) || float.IsInfinity(effective)) return;

            GrandmasterStore.Add(skill, effective);
            // If the verified vanilla saturation branch could not be bound, retain old XP banking
            // and disable support rather than guessing a replacement learning formula.
            if (!Patch_SkillRecord_LearnRateFactor.Applied || skill.Pawn == null || skill.def == null) return;

            bool forced = ReferenceEquals(forceInsightFor, skill);
            if (forced) forceInsightFor = null; // one eligible invocation, even if another patch re-enters Learn
            if (!forced && Roll(skill.Pawn.thingIDNumber, skill.def.defName, GrandmasterStore.Get(skill)) >= Chance(effective)) return;

            double bonus = Bonus(Gm21Mod.Settings.grandmasterXpRequirement);
            if (!(bonus > 0.0)) return;
            GrandmasterStore.Add(skill, bonus);
            NotifyInsight(skill, bonus);
            // No promotion here. The existing Learn postfix checks the combined total exactly as before.
        }

        internal static double Chance(double eligibleXp)
        {
            if (!(eligibleXp > 0.0) || double.IsInfinity(eligibleXp)) return 0.0;
            double x = eligibleXp / CriticalXpScale;
            // Avoid cancellation in 1-exp(-x) for tiny modded increments (.NET Framework has no Expm1).
            if (x < 0.00001) return x * (1.0 - x * (0.5 - x / 6.0));
            return 1.0 - Math.Exp(-x);
        }

        internal static double Bonus(double requirement)
        {
            return requirement > 0.0 && !double.IsInfinity(requirement) ? requirement * CriticalBonusFraction : 0.0;
        }

        /// <summary>
        /// Stateless local draw: stable pawn ID + skill name + the NEW saved XP balance identify an event.
        /// Each increment changes the key, including multiple events in one tick. Reloading the same saved
        /// state reproduces the draw. No Rand consumption, wall clock, event counter or extra save data.
        /// SplitMix64's avalanche turns the key into 53 uniform bits; no strings/collections are allocated.
        /// </summary>
        internal static double Roll(int pawnId, string skillName, double bankedXp)
        {
            unchecked
            {
                ulong key = 14695981039346656037UL;
                if (skillName != null)
                    for (int i = 0; i < skillName.Length; i++) key = (key ^ skillName[i]) * 1099511628211UL;
                key = Mix(key ^ (uint)pawnId);
                key = Mix(key ^ (ulong)BitConverter.DoubleToInt64Bits(bankedXp));
                return (key >> 11) * (1.0 / 9007199254740992.0);
            }
        }

        private static ulong Mix(ulong value)
        {
            unchecked
            {
                value += 0x9E3779B97F4A7C15UL;
                value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
                value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
                return value ^ (value >> 31);
            }
        }

        private static void NotifyInsight(SkillRecord skill, double bonus)
        {
            Pawn pawn = skill.Pawn;
            if (!pawn.Spawned || !pawn.IsColonist || pawn.Dead) return;
            try
            {
                // Follow existing artifact-feedback convention: presentation must not consume gameplay RNG.
                Rand.PushState();
                try
                {
                    Messages.Message("GM21_GrandmasterInsight".Translate(pawn.LabelShortCap, skill.def.LabelCap, bonus.ToString("N0")),
                        pawn, MessageTypeDefOf.PositiveEvent, false);
                }
                finally { Rand.PopState(); }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[Grandmaster 21] Insight message unavailable: " + ex, 213720);
            }
        }

        // Dev action and deterministic integration tests both enter through the real Learn path.
        internal static void ForceOneInsight(SkillRecord skill)
        {
            if (skill == null || skill.levelInt != Gm21.VanillaMaxLevel || skill.TotallyDisabled) return;
            SkillRecord previous = forceInsightFor;
            forceInsightFor = skill;
            try { skill.Learn(1f, true, true); }
            finally { forceInsightFor = previous; }
        }
    }
}
