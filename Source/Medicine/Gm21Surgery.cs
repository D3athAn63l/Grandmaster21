using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// Perfect Grandmaster Surgery: "the surgeon does not fail" -- NOT "the surgeon ignores whether
    /// the operation is possible".
    ///
    /// WHERE FAILURE LIVES IN 1.6. Recipe_Surgery.CheckSurgeryFail asks the recipe's
    /// SurgeryOutcomeEffectDef for an outcome. GetOutcome computes a quality from the def's comps
    /// (surgeon success chance, bed, medicine, inspiration, age, the 98% clamp), lets each comp's
    /// PreApply run, then walks the def's outcome list and returns the first outcome whose Apply
    /// fires. Success is SurgeryOutcomeSuccess (Rand.Chance(quality)); the minor, catastrophic and
    /// ridiculous failures and the death branch are further outcomes flagged failure=true, whose
    /// Apply is what inflicts the damage, the letter, the thought or the death.
    ///
    /// So by the time CheckSurgeryFail sees a failure it has already happened. The only correct
    /// hook is GetOutcome itself, before any outcome applies. For a practising Medicine Grandmaster
    /// it is replaced by an equivalent walk that:
    ///
    ///   * computes quality and runs comp PreApply exactly as vanilla does,
    ///   * treats quality as at least 1.0 -- the Grandmaster operates perfectly -- so success
    ///     outcomes and quality-scaled success consequences resolve at their best,
    ///   * never evaluates an outcome that is a failure: flagged failure, a SurgeryOutcome_Failure
    ///     (including FailureWithHediff), or a SurgeryOutcome_Death,
    ///   * returns the first non-failure outcome that applies, or null -- which CheckSurgeryFail
    ///     already treats as success.
    ///
    /// UNTOUCHED: everything before and after the roll. Bills, recipe availability, part validity,
    /// ingredients and their consumption, the surgery job and its work time, anaesthesia, medical
    /// care restrictions, and every successful consequence the recipe worker applies after
    /// CheckSurgeryFail returns false. A Medicine 20 surgeon, and every other surgeon, reaches
    /// vanilla GetOutcome unchanged, including vanilla's 98% cap.
    /// </summary>
    public static class Gm21Surgery
    {
        /// <summary>SurgeryOutcomeEffectDef.GetOutcome prefix.</summary>
        internal static bool Prefix_GetOutcome(SurgeryOutcomeEffectDef __instance, RecipeDef recipe,
            Pawn surgeon, Pawn patient, List<Thing> ingredients, BodyPartRecord part, Bill bill,
            ref SurgeryOutcome __result)
        {
            if (!Gm21Medicine.SurgeryEnabled) return true;
            if (!Gm21Medicine.CanPractise(surgeon)) return true;
            __result = ResolveWithoutFailure(__instance, recipe, surgeon, patient, ingredients, part, bill);
            return false;
        }

        /// <summary>True for any outcome the Grandmaster path must never evaluate.</summary>
        public static bool IsFailureOutcome(SurgeryOutcome outcome)
        {
            return outcome == null
                || outcome.failure
                || outcome is SurgeryOutcome_Failure
                || outcome is SurgeryOutcome_Death;
        }

        /// <summary>The Grandmaster's GetOutcome. Mirrors vanilla's order of operations.</summary>
        public static SurgeryOutcome ResolveWithoutFailure(SurgeryOutcomeEffectDef def, RecipeDef recipe,
            Pawn surgeon, Pawn patient, List<Thing> ingredients, BodyPartRecord part, Bill bill)
        {
            float quality = def.GetQuality(recipe, surgeon, patient, ingredients, part, bill);

            List<SurgeryOutcomeComp> comps = def.comps;
            if (!comps.NullOrEmpty())
            {
                for (int i = 0; i < comps.Count; i++)
                {
                    if (comps[i].Affects(recipe, surgeon, patient, part))
                    {
                        comps[i].PreApply(quality, recipe, surgeon, patient, ingredients, part, bill);
                    }
                }
            }

            quality = Mathf.Max(quality, 1f);

            List<SurgeryOutcome> outcomes = def.outcomes;
            if (!outcomes.NullOrEmpty())
            {
                for (int j = 0; j < outcomes.Count; j++)
                {
                    SurgeryOutcome outcome = outcomes[j];
                    if (IsFailureOutcome(outcome)) continue;
                    if (outcome.Apply(quality, recipe, surgeon, patient, part)) return outcome;
                }
            }
            return null;
        }
    }
}
