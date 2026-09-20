using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// The Shooting Grandmaster patches, applied MANUALLY rather than by attribute.
    ///
    /// Why manual: these targets are combat internals, several of them private, and a couple have
    /// been renamed across RimWorld versions (ShotReport's chance properties were
    /// ChanceToNotGoWild/ChanceToNotHitCover before 1.3). An attribute patch whose target cannot
    /// be resolved throws during PatchAll and takes the WHOLE mod down with it -- including level
    /// 21 permanence, which has nothing to do with shooting. Resolving each target by hand lets a
    /// missing method disable one feature, log once, and leave everything else running.
    ///
    /// The same applies to combat overhauls: if one is active, none of this is applied at all.
    /// </summary>
    internal static class Gm21ShootingPatches
    {
        private static readonly HarmonyMethod NoPatch = null;

        internal static void Apply(Harmony harmony)
        {
            if (DetectCombatOverhaul(out string overhaul))
            {
                Log.Warning("[Grandmaster 21] " + overhaul + " detected. The Shooting Grandmaster "
                            + "package (accuracy compensation, cover negation, warmup/cooldown "
                            + "mastery and Killer/Downed anatomical targeting) is DISABLED, because "
                            + "it replaces the shooting pipeline these patches rely on. Level 21 "
                            + "itself, its permanence and the quality rules are unaffected.");
                return;
            }

            bool passive = ApplyPassivePatches(harmony);
            bool targeting = ApplyTargetingPatches(harmony);

            Gm21Shooting.PassiveBonusesEnabled = passive;
            Gm21Shooting.AnatomicalTargetingEnabled = targeting;

            if (targeting && !Gm21Burst.Available)
            {
                Log.Warning("[Grandmaster 21] Verb.burstShotsLeft not found; a Grandmaster will no "
                            + "longer stop a burst once the target is down. Per-projectile "
                            + "less-lethal targeting is unaffected.");
            }

            if (!passive)
            {
                Log.Warning("[Grandmaster 21] Could not apply the passive Shooting Grandmaster "
                            + "patches; marksman accuracy, cover negation and warmup/cooldown "
                            + "mastery are disabled this session.");
            }
            if (!targeting)
            {
                Log.Warning("[Grandmaster 21] Could not apply the anatomical targeting patch; "
                            + "Killer and Downed aim modes will behave as Normal this session.");
            }
        }

        // ------------------------------------------------------------------ passive bonuses

        private static bool ApplyPassivePatches(Harmony harmony)
        {
            // Context: who is shooting. Without this the ShotReport property postfixes cannot tell
            // whose shot they are looking at -- see Gm21ShotContext for the full reasoning.
            MethodInfo tryCastShot = AccessTools.Method(typeof(Verb_LaunchProjectile), "TryCastShot");
            MethodInfo hitReportFor = AccessTools.Method(typeof(ShotReport), nameof(ShotReport.HitReportFor));
            if (tryCastShot == null || hitReportFor == null) return false;

            // The two probabilistic gates in TryCastShot. Names differ across versions.
            MethodInfo aimChance = Getter(typeof(ShotReport),
                "AimOnTargetChance_IgnoringPosture", "ChanceToNotGoWild_IgnoringPosture");
            MethodInfo coverChance = Getter(typeof(ShotReport),
                "PassCoverChance", "ChanceToNotHitCover");
            if (aimChance == null || coverChance == null) return false;

            try
            {
                harmony.Patch(tryCastShot,
                    prefix: Hook(nameof(Prefix_OpenShotContext)),
                    postfix: NoPatch, transpiler: NoPatch,
                    finalizer: Hook(nameof(Finalizer_CloseShotContext)));

                harmony.Patch(hitReportFor, prefix: NoPatch,
                    postfix: Hook(nameof(Postfix_ObserveShotContext)));

                harmony.Patch(aimChance, prefix: NoPatch, postfix: Hook(nameof(Postfix_AimChance)));
                harmony.Patch(coverChance, prefix: NoPatch, postfix: Hook(nameof(Postfix_CoverChance)));

                // Cosmetic only: keeps the targeting readout consistent with what will happen.
                MethodInfo total = Getter(typeof(ShotReport), "TotalEstimatedHitChance");
                if (total != null)
                {
                    harmony.Patch(total, prefix: NoPatch, postfix: Hook(nameof(Postfix_AimChance)));
                }

                // Warmup and cooldown. Patching the stance constructors is the narrowest possible
                // intervention: it changes how long the pawn aims and recovers, and touches
                // nothing about burst timing, magazines, reloads or projectile behaviour.
                PatchStanceCtor(harmony, typeof(Stance_Warmup));
                PatchStanceCtor(harmony, typeof(Stance_Cooldown));
                return true;
            }
            catch (Exception e)
            {
                Log.Error("[Grandmaster 21] Failed to apply passive shooting patches: " + e);
                return false;
            }
        }

        private static void PatchStanceCtor(Harmony harmony, Type stanceType)
        {
            ConstructorInfo ctor = AccessTools.Constructor(stanceType,
                new[] { typeof(int), typeof(LocalTargetInfo), typeof(Verb) });
            if (ctor == null)
            {
                Log.Warning("[Grandmaster 21] " + stanceType.Name + " constructor not found; that "
                            + "half of the warmup/cooldown mastery is disabled.");
                return;
            }
            harmony.Patch(ctor, prefix: Hook(nameof(Prefix_StanceTicks)));
        }

        // ------------------------------------------------------------------ anatomical targeting

        private static bool ApplyTargetingPatches(Harmony harmony)
        {
            MethodInfo preApply = AccessTools.Method(typeof(Pawn), nameof(Pawn.PreApplyDamage));
            if (preApply == null) return false;

            try
            {
                harmony.Patch(preApply, prefix: Hook(nameof(Prefix_ChooseAimedPart)));
            }
            catch (Exception e)
            {
                Log.Error("[Grandmaster 21] Failed to apply anatomical targeting patch: " + e);
                return false;
            }

            // Death-on-downed suppression is a bonus, not a requirement: Downed mode still aims at
            // mobility anatomy without it. If either piece is missing, the feature degrades to
            // "the storyteller may still convert this downing into a death".
            MethodInfo checkState = AccessTools.Method(typeof(Pawn_HealthTracker), "CheckForStateChange");
            if (checkState == null || Gm21DownedGuard.ForceDownedField == null)
            {
                Log.Warning("[Grandmaster 21] Death-on-downed suppression is unavailable "
                            + "(Pawn_HealthTracker.CheckForStateChange or forceDowned not found). "
                            + "Downed mode still targets mobility anatomy.");
                return true;
            }

            try
            {
                harmony.Patch(checkState,
                    prefix: Hook(typeof(Gm21DownedGuard), nameof(Gm21DownedGuard.Prefix)),
                    postfix: NoPatch, transpiler: NoPatch,
                    finalizer: Hook(typeof(Gm21DownedGuard), nameof(Gm21DownedGuard.Finalizer)));
            }
            catch (Exception e)
            {
                Log.Warning("[Grandmaster 21] Could not patch death-on-downed suppression: " + e);
            }
            return true;
        }

        // ------------------------------------------------------------------ patch bodies

        /// <summary>
        /// Opens the shot context around an actual cast, and enforces burst discipline.
        ///
        /// Returning false with __result = false is not a hack: it is the same outcome vanilla
        /// produces whenever a shot cannot be taken mid-burst, and TryCastNextBurstShot already
        /// handles it by zeroing burstShotsLeft and running the normal end-of-burst path --
        /// cooldown stance, completion callback, verb back to Idle. See Gm21Burst.
        ///
        /// The context is opened FIRST so the finalizer's Close is always balanced, whether the
        /// original method runs, is skipped, or throws.
        /// </summary>
        internal static bool Prefix_OpenShotContext(Verb __instance, ref bool __result)
        {
            Gm21ShotContext.Open(__instance.caster);

            if (Gm21Burst.ShouldHoldFire(__instance))
            {
                __result = false;
                return false;
            }
            return true;
        }

        /// <summary>
        /// Finalizer, not postfix: it runs even when the cast throws, so the context can never be
        /// left open.
        ///
        /// VOID ON PURPOSE. A Harmony finalizer that returns Exception does not "report" the
        /// exception -- its return value REPLACES the pending one, so `return null` means "there
        /// is no exception any more". This method used to do exactly that, which silently ate any
        /// error thrown inside TryCastShot by RimWorld or by another mod's patch. A void finalizer
        /// cannot alter exception state at all, which is precisely what cleanup wants: clean up,
        /// change nothing. (Returning Exception while taking __exception and handing it straight
        /// back would also be correct, but there is nothing here to transform.)
        ///
        /// Verified by execution in Tests/VerifyFinalizerSemantics.cs against the real Harmony
        /// assembly, because this distinction is invisible on inspection.
        /// </summary>
        internal static void Finalizer_CloseShotContext()
        {
            Gm21ShotContext.Close();
        }

        /// <summary>Covers the targeting UI, which builds a report and reads it immediately.</summary>
        internal static void Postfix_ObserveShotContext(Thing caster)
        {
            Gm21ShotContext.Observe(caster);
        }

        /// <summary>
        /// Near-perfect accuracy compensation.
        ///
        /// Applied once, at the single value TryCastShot rolls against, so it cannot double-apply.
        /// Because every ordinary accuracy penalty -- weapon inaccuracy, range, weather, darkness,
        /// target size, the shooter's own stat -- has already been folded into this number, the
        /// proportional removal compensates for all of them in one place. That is deliberate: it
        /// avoids hunting down and zeroing individual modifiers, which is where double-application
        /// and mod conflicts come from.
        /// </summary>
        internal static void Postfix_AimChance(ref float __result)
        {
            if (!Gm21ShotContext.ShooterIsGrandmaster) return;
            __result = Gm21Shooting.CompensateChance(__result);
        }

        /// <summary>
        /// Cover negation. The Grandmaster shoots past sandbags, barricades and doorway edges.
        ///
        /// This is an ACCURACY term only. Line of sight is resolved separately by
        /// TryFindShootLineFromTo, which is untouched -- a solid wall still blocks the shot, and
        /// no shot path still means no shot.
        /// </summary>
        internal static void Postfix_CoverChance(ref float __result)
        {
            if (!Gm21ShotContext.ShooterIsGrandmaster) return;
            __result = 1f;
        }

        /// <summary>
        /// Warmup and cooldown mastery: 99% off, floored at one tick.
        ///
        /// Gated to Verb_LaunchProjectile so a Grandmaster's psycasts and melee are untouched --
        /// Verb_CastAbility does not derive from it. Burst timing (ticksBetweenBurstShots) is a
        /// separate mechanism and is deliberately left alone, as are magazines and reloads.
        /// </summary>
        internal static void Prefix_StanceTicks(ref int ticks, Verb verb)
        {
            if (ticks <= Gm21Shooting.MinDelayTicks) return;
            if (!(verb is Verb_LaunchProjectile)) return;
            Pawn pawn = verb.CasterPawn;
            if (pawn == null || !Gm21Shooting.HasPassiveBonuses(pawn)) return;
            ticks = Gm21Shooting.ReduceDelayTicks(ticks);
        }

        /// <summary>
        /// Killer / Downed shot placement.
        ///
        /// Runs at the moment RimWorld is about to decide which BodyPartRecord takes the damage,
        /// and only sets dinfo.HitPart -- the shot then flows through armour, damage, hediffs,
        /// part destruction and death/downing exactly as any other shot would. No fake impacts, no
        /// direct health edits, no bonus damage.
        ///
        /// Gates, cheapest first, so a normal hit costs a handful of comparisons:
        ///   * feature enabled, and no part already chosen by something else
        ///   * instigator is a pawn, not the victim, and in a non-Normal mode
        ///   * instigator is a legitimate Shooting Grandmaster
        ///   * the damage came from a ranged weapon and was aimed at THIS pawn, so strays and
        ///     friendly fire are never redirected
        /// </summary>
        internal static void Prefix_ChooseAimedPart(Pawn __instance, ref DamageInfo dinfo)
        {
            if (!Gm21Shooting.AnatomicalTargetingEnabled) return;
            if (dinfo.HitPart != null) return;

            Pawn shooter = dinfo.Instigator as Pawn;
            if (shooter == null || shooter == __instance) return;

            Gm21AimMode mode = Gm21AimModeStore.Get(shooter);
            if (mode == Gm21AimMode.Normal) return;

            if (dinfo.IntendedTarget != __instance) return;
            if (dinfo.Weapon == null || !dinfo.Weapon.IsRangedWeapon) return;
            if (dinfo.Def == null || !dinfo.Def.harmsHealth) return;
            if (!Gm21Shooting.IsShootingGrandmaster(shooter)) return;

            // Re-evaluated for EVERY projectile, never cached across a burst. The anatomy this
            // reads has already absorbed the previous shot, so a limb that came off a moment ago
            // is gone from consideration and the next best target is chosen instead.
            BodyPartRecord part = mode == Gm21AimMode.Killer
                ? Gm21BodyTargeting.ChooseLethalPart(__instance)
                : Gm21BodyTargeting.ChooseIncapacitatingPart(__instance);

            if (part != null)
            {
                dinfo.SetHitPart(part);
                return;
            }

            // null means the anatomy offers nothing this mode wants. Burst discipline normally
            // stops the shooter before it gets here, but a projectile already in flight cannot be
            // recalled, so vanilla resolves this one. Gm21Burst.ShouldHoldFire will withhold the
            // rest of the burst on the next TryCastShot.
        }

        // ------------------------------------------------------------------ helpers

        private static HarmonyMethod Hook(string name)
        {
            return Hook(typeof(Gm21ShootingPatches), name);
        }

        private static HarmonyMethod Hook(Type type, string name)
        {
            MethodInfo m = AccessTools.Method(type, name);
            return m == null ? null : new HarmonyMethod(m);
        }

        private static MethodInfo Getter(Type type, params string[] candidateNames)
        {
            for (int i = 0; i < candidateNames.Length; i++)
            {
                PropertyInfo p = AccessTools.Property(type, candidateNames[i]);
                if (p != null && p.GetGetMethod(true) != null) return p.GetGetMethod(true);
            }
            return null;
        }

        /// <summary>
        /// Combat overhauls replace the shooting pipeline wholesale. Matching on the package id
        /// rather than a display name keeps this working across reuploads and translations.
        /// </summary>
        private static bool DetectCombatOverhaul(out string name)
        {
            name = null;
            try
            {
                foreach (ModMetaData mod in ModsConfig.ActiveModsInLoadOrder)
                {
                    if (mod == null) continue;
                    string id = mod.PackageId;
                    if (id == null) continue;
                    id = id.ToLowerInvariant();
                    if (id.Contains("combatextended"))
                    {
                        name = "Combat Extended";
                        return true;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning("[Grandmaster 21] Could not inspect the active mod list: " + e.Message);
            }
            return false;
        }
    }

    /// <summary>
    /// Suppresses ONLY the storyteller's artificial "downed enemies sometimes just die" roll, and
    /// only for a downing caused by a Grandmaster shooting in Downed mode.
    ///
    /// This is not immortality, and that is not an assumption -- it is visible in 1.6's IL.
    /// CheckForStateChange tests ShouldBeDead() first and only reaches the downed branch if that
    /// is false, where forceDowned short-circuits past the death roll straight to MakeDowned. A
    /// genuinely lethal wound still kills. Blood loss, destroyed organs, fire and untreated
    /// wounds all still kill normally, afterwards.
    ///
    /// Scoped with a prefix/finalizer pair around one CheckForStateChange call, so the flag is
    /// restored even if that call throws, and global death-on-downed behaviour is untouched for
    /// every other pawn and every other shot.
    /// </summary>
    internal static class Gm21DownedGuard
    {
        internal static readonly FieldInfo ForceDownedField =
            AccessTools.Field(typeof(Pawn_HealthTracker), "forceDowned");

        /// <summary>
        /// Pawn_HealthTracker.pawn is private, and it is the only way to ask "who is this tracker
        /// for?" -- which is what makes the intended-target check below possible.
        /// </summary>
        internal static readonly FieldInfo PawnField =
            AccessTools.Field(typeof(Pawn_HealthTracker), "pawn");

        internal static void Prefix(Pawn_HealthTracker __instance, DamageInfo? dinfo, out bool __state)
        {
            __state = false;
            if (ForceDownedField == null) return;
            if (!dinfo.HasValue) return;

            DamageInfo info = dinfo.Value;
            Pawn shooter = info.Instigator as Pawn;
            if (shooter == null) return;
            if (Gm21AimModeStore.Get(shooter) != Gm21AimMode.Downed) return;
            if (info.Weapon == null || !info.Weapon.IsRangedWeapon) return;

            // The pawn going down must be the pawn that was actually aimed at.
            //
            // Without this, the suppression keyed only on "a Downed-mode Grandmaster fired a
            // ranged weapon", which also matches a stray round, a friendly caught in the line, or
            // anything else that happened to share the instigator. Those are not deliberate
            // incapacitations and have no business being spared the storyteller's roll.
            //
            // DamageInfo already carries everything needed, so there is no attack-context object
            // and nothing to clean up.
            if (PawnField != null)
            {
                Pawn victim = PawnField.GetValue(__instance) as Pawn;
                if (victim == null || info.IntendedTarget != victim) return;
            }

            if (!Gm21Shooting.IsShootingGrandmaster(shooter)) return;

            object current = ForceDownedField.GetValue(__instance);
            if (current is bool && (bool)current) return; // already forced by something else

            ForceDownedField.SetValue(__instance, true);
            __state = true;
        }

        /// <summary>
        /// Restores forceDowned. Void for the same reason as Finalizer_CloseShotContext: an
        /// Exception-returning finalizer replaces the pending exception, so returning null here
        /// was swallowing anything CheckForStateChange threw -- including other mods' errors on a
        /// code path that runs on every damage event. Cleanup only; exception state untouched.
        /// </summary>
        internal static void Finalizer(Pawn_HealthTracker __instance, bool __state)
        {
            if (__state && ForceDownedField != null)
            {
                ForceDownedField.SetValue(__instance, false);
            }
        }
    }
}
