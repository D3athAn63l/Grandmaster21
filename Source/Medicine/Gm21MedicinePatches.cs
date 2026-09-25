using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// The Medicine Grandmaster patches, applied MANUALLY rather than by attribute, for the reason
    /// Gm21ShootingPatches gives: an attribute patch whose target cannot be resolved throws out of
    /// PatchAll and takes the whole mod down with it. Resolving each target by hand lets one missing
    /// method disable one Medicine feature, log once, and leave everything else running.
    ///
    /// Independent groups, each behind its own flag:
    ///
    ///   persistence -- Hediff.ExposeData                                    (needed by treatment)
    ///   tend        -- TendUtility.DoTend, HediffComp_TendDuration.CompTended  (Gm21Medicine.TendEnabled)
    ///   propagation -- TendUtility.DoTend transpiler (one call site)          (Gm21Medicine.TendPropagationEnabled)
    ///   treatment   -- tend + persistence                                    (Gm21Medicine.TreatmentEnabled)
    ///   recovery    -- Pawn_HealthTracker.HealthTickInterval, Hediff_Injury.Heal (Gm21Medicine.RecoveryEnabled)
    ///   immunity    -- ImmunityRecord.ImmunityChangePerTick                   (Gm21Medicine.ImmunityEnabled)
    ///   surgery     -- SurgeryOutcomeEffectDef.GetOutcome                     (Gm21Medicine.SurgeryEnabled)
    ///
    /// The Medicine mode gizmo and mode persistence are ordinary attribute patches on
    /// Pawn.GetGizmos / Pawn.ExposeData, exactly like the Shooting and Melee controls.
    /// </summary>
    internal static class Gm21MedicinePatches
    {
        private static readonly HarmonyMethod NoPatch = null;

        internal static void Apply(Harmony harmony)
        {
            bool persistence = Try("Grandmaster Treatment persistence", delegate
            {
                MethodInfo expose = Need(AccessTools.Method(typeof(Hediff), nameof(Hediff.ExposeData)), "Hediff.ExposeData");
                harmony.Patch(expose, prefix: NoPatch, postfix: Hook(nameof(Postfix_Hediff_ExposeData)));
            });

            Gm21Medicine.TendEnabled = Try("deterministic Grandmaster tending", delegate
            {
                MethodInfo doTend = Need(AccessTools.Method(typeof(TendUtility), nameof(TendUtility.DoTend)), "TendUtility.DoTend");
                MethodInfo compTended = Need(AccessTools.Method(typeof(HediffComp_TendDuration),
                    nameof(HediffComp_TendDuration.CompTended)), "HediffComp_TendDuration.CompTended");
                harmony.Patch(doTend,
                    prefix: Hook(typeof(Gm21GrandmasterTend), nameof(Gm21GrandmasterTend.Prefix_DoTend)),
                    postfix: NoPatch, transpiler: NoPatch,
                    finalizer: Hook(typeof(Gm21GrandmasterTend), nameof(Gm21GrandmasterTend.Finalizer_DoTend)));
                harmony.Patch(compTended,
                    prefix: Hook(typeof(Gm21GrandmasterTend), nameof(Gm21GrandmasterTend.Prefix_CompTended)),
                    postfix: Hook(typeof(Gm21GrandmasterTend), nameof(Gm21GrandmasterTend.Postfix_CompTended)));
            });

            if (Gm21Medicine.TendEnabled)
            {
                // Separate group: without it the tend is still exact, but a condition-specific
                // Tended override would see the medicine's ordinary ceiling instead of the
                // Grandmaster effective quality.
                Gm21Medicine.TendPropagationEnabled = Try("Grandmaster effective tend quality", delegate
                {
                    MethodInfo doTend = Need(AccessTools.Method(typeof(TendUtility), nameof(TendUtility.DoTend)), "TendUtility.DoTend");
                    harmony.Patch(doTend, prefix: NoPatch, postfix: NoPatch,
                        transpiler: Hook(typeof(Gm21GrandmasterTend), nameof(Gm21GrandmasterTend.Transpiler_DoTend)));
                    if (Gm21GrandmasterTend.PropagationSites != 1)
                        throw new MissingMethodException("TendUtility.DoTend no longer has exactly one Hediff.Tended call");
                });
            }

            Gm21Medicine.TreatmentEnabled = Gm21Medicine.TendEnabled && persistence;

            if (Gm21Medicine.TreatmentEnabled)
            {
                // Cosmetic; its absence costs a tooltip line, nothing else.
                Try("Grandmaster Treatment tooltip", delegate
                {
                    MethodInfo tip = Need(AccessTools.PropertyGetter(typeof(HediffComp_TendDuration),
                        nameof(HediffComp_TendDuration.CompTipStringExtra)), "HediffComp_TendDuration.CompTipStringExtra");
                    harmony.Patch(tip, prefix: NoPatch,
                        postfix: Hook(typeof(Gm21GrandmasterTend), nameof(Gm21GrandmasterTend.Postfix_CompTipStringExtra)));
                });

                Gm21Medicine.RecoveryEnabled = Try("Grandmaster Treatment injury recovery", delegate
                {
                    MethodInfo tick = Need(AccessTools.Method(typeof(Pawn_HealthTracker),
                        nameof(Pawn_HealthTracker.HealthTickInterval)), "Pawn_HealthTracker.HealthTickInterval");
                    MethodInfo heal = Need(AccessTools.DeclaredMethod(typeof(Hediff_Injury), nameof(Hediff_Injury.Heal)),
                        "Hediff_Injury.Heal");
                    harmony.Patch(tick,
                        prefix: Hook(typeof(Gm21RecoveryEffects), nameof(Gm21RecoveryEffects.Prefix_HealthTickInterval)),
                        postfix: NoPatch, transpiler: NoPatch,
                        finalizer: Hook(typeof(Gm21RecoveryEffects), nameof(Gm21RecoveryEffects.Finalizer_HealthTickInterval)));
                    harmony.Patch(heal, prefix: Hook(typeof(Gm21RecoveryEffects), nameof(Gm21RecoveryEffects.Prefix_Heal)));
                });

                Gm21Medicine.ImmunityEnabled = Try("Grandmaster Treatment immunity", delegate
                {
                    MethodInfo immunity = Need(AccessTools.Method(typeof(ImmunityRecord),
                        nameof(ImmunityRecord.ImmunityChangePerTick)), "ImmunityRecord.ImmunityChangePerTick");
                    harmony.Patch(immunity, prefix: NoPatch,
                        postfix: Hook(typeof(Gm21RecoveryEffects), nameof(Gm21RecoveryEffects.Postfix_ImmunityChangePerTick)));
                });
            }
            else if (Gm21Medicine.TendEnabled)
            {
                Log.Warning("[Grandmaster 21] Medicine 21: Grandmaster Treatment is disabled this session "
                            + "because its save persistence could not be installed; deterministic tending "
                            + "still applies. Treatment state is never created when it could not be saved.");
            }

            Gm21Medicine.SurgeryEnabled = Try("perfect Grandmaster surgery", delegate
            {
                MethodInfo outcome = Need(AccessTools.Method(typeof(SurgeryOutcomeEffectDef),
                    nameof(SurgeryOutcomeEffectDef.GetOutcome)), "SurgeryOutcomeEffectDef.GetOutcome");
                harmony.Patch(outcome, prefix: Hook(typeof(Gm21Surgery), nameof(Gm21Surgery.Prefix_GetOutcome)));
            });
        }

        /// <summary>Hediff.ExposeData postfix -- delegates to the store so the format lives in one place.</summary>
        internal static void Postfix_Hediff_ExposeData(Hediff __instance)
        {
            Gm21TreatmentStore.ExposeTreatment(__instance);
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>Runs one patch group; a failure disables that group only and is logged once.</summary>
        private static bool Try(string feature, Action apply)
        {
            try
            {
                apply();
                return true;
            }
            catch (Exception e)
            {
                Log.Warning("[Grandmaster 21] Medicine 21: could not apply " + feature
                            + "; that feature is disabled this session. " + e.Message);
                return false;
            }
        }

        private static MethodInfo Need(MethodInfo method, string what)
        {
            if (method == null) throw new MissingMethodException(what + " not found");
            return method;
        }

        private static HarmonyMethod Hook(string name)
        {
            return Hook(typeof(Gm21MedicinePatches), name);
        }

        private static HarmonyMethod Hook(Type type, string name)
        {
            MethodInfo m = AccessTools.Method(type, name);
            if (m == null) throw new MissingMethodException(type.Name + "." + name + " not found");
            return new HarmonyMethod(m);
        }
    }
}
