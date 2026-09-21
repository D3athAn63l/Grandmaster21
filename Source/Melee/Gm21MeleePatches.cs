using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// The Melee Grandmaster patches, applied MANUALLY rather than by attribute, for exactly the
    /// reasons Gm21ShootingPatches gives: these targets are combat internals, several of them
    /// private, and an attribute patch whose target cannot be resolved throws out of PatchAll and
    /// takes the WHOLE mod down with it -- level 21 permanence included. Resolving each target by
    /// hand lets a missing method disable one melee feature, log once, and leave the rest running.
    ///
    /// Features are applied in independent groups, so losing one target never cascades:
    ///
    ///   passive     -- hit chance, defence-ignore, parry            (Gm21Melee.PassiveEnabled)
    ///   reactions   -- riposte, disarm, critical, cleave            (Gm21Melee.ReactionsEnabled)
    ///   doctrine    -- Killer/Downed strike placement and force     (Gm21Melee.DoctrineEnabled)
    ///   ally        -- 3-tile ally melee interception               (Gm21Melee.AllyInterceptEnabled)
    ///   projectile  -- projectile interception and redirection      (Gm21Melee.ProjectileDefenceEnabled)
    /// </summary>
    internal static class Gm21MeleePatches
    {
        private static readonly HarmonyMethod NoPatch = null;

        // Resolved once and reused by the melee context opener.
        internal static MethodInfo TryCastShotMethod;

        internal static void Apply(Harmony harmony)
        {
            string overhaul;
            if (Gm21MeleeCompat.DetectCombatOverhaul(out overhaul))
            {
                Log.Warning("[Grandmaster 21] " + overhaul + " detected. The Melee Grandmaster "
                            + "package (near-perfect strikes and parries, riposte, disarm, "
                            + "critical strikes, cleave, ally interception and projectile "
                            + "deflection) is DISABLED, because it replaces the melee and "
                            + "projectile pipelines these patches rely on. Level 21 itself, its "
                            + "permanence and the quality rules are unaffected.");
                return;
            }

            bool passive = ApplyPassive(harmony);
            Gm21Melee.PassiveEnabled = passive;

            if (!passive)
            {
                Log.Warning("[Grandmaster 21] Could not apply the passive Melee Grandmaster "
                            + "patches; melee mastery is disabled this session. Every melee "
                            + "feature depends on them, so none of the rest is applied either.");
                return;
            }

            Gm21Melee.DoctrineEnabled = ApplyDoctrine(harmony);
            Gm21Melee.ReactionsEnabled = ApplyReactions(harmony);
            Gm21Melee.AllyInterceptEnabled = Gm21Melee.ReactionsEnabled;
            Gm21Melee.ProjectileDefenceEnabled = ApplyProjectileDefence(harmony);

            if (!Gm21Melee.DoctrineEnabled)
            {
                Log.Warning("[Grandmaster 21] Melee Killer/Downed doctrines are unavailable this "
                            + "session; they will behave as Normal.");
            }
            if (!Gm21Melee.ReactionsEnabled)
            {
                Log.Warning("[Grandmaster 21] Melee riposte, disarm, critical strikes, cleave and "
                            + "ally interception are unavailable this session.");
            }
            if (!Gm21Melee.ProjectileDefenceEnabled)
            {
                Log.Warning("[Grandmaster 21] Melee projectile interception and redirection are "
                            + "unavailable this session.");
            }
        }

        // ------------------------------------------------------------------ passive

        /// <summary>
        /// The three probabilistic gates of a melee exchange, plus the context that ties them to
        /// a caster.
        ///
        /// GetNonMissChance and GetDodgeChance are PRIVATE in 1.6, which is precisely why they are
        /// resolved by hand: an attribute patch on a private member that gets renamed is a startup
        /// crash, and this one resolving to null is a logged warning.
        /// </summary>
        private static bool ApplyPassive(Harmony harmony)
        {
            MethodInfo tryCastShot = AccessTools.Method(typeof(Verb_MeleeAttack), "TryCastShot");
            MethodInfo nonMiss = AccessTools.Method(typeof(Verb_MeleeAttack), "GetNonMissChance");
            MethodInfo dodge = AccessTools.Method(typeof(Verb_MeleeAttack), "GetDodgeChance");
            if (tryCastShot == null || nonMiss == null || dodge == null)
            {
                Log.Warning("[Grandmaster 21] Melee resolution methods not found (TryCastShot="
                            + (tryCastShot != null) + ", GetNonMissChance=" + (nonMiss != null)
                            + ", GetDodgeChance=" + (dodge != null) + ").");
                return false;
            }

            try
            {
                TryCastShotMethod = tryCastShot;
                harmony.Patch(tryCastShot,
                    prefix: Hook(nameof(Prefix_OpenMeleeContext)),
                    postfix: Hook(nameof(Postfix_ResolveExchange)),
                    transpiler: NoPatch,
                    finalizer: Hook(nameof(Finalizer_CloseMeleeContext)));

                harmony.Patch(nonMiss, prefix: NoPatch, postfix: Hook(nameof(Postfix_NonMissChance)));
                harmony.Patch(dodge, prefix: NoPatch, postfix: Hook(nameof(Postfix_DodgeChance)));
                return true;
            }
            catch (Exception e)
            {
                Log.Error("[Grandmaster 21] Failed to apply passive melee patches: " + e);
                return false;
            }
        }

        private static bool ApplyDoctrine(Harmony harmony)
        {
            MethodInfo preApply = AccessTools.Method(typeof(Pawn), nameof(Pawn.PreApplyDamage));
            if (preApply == null) return false;
            try
            {
                harmony.Patch(preApply, prefix: Hook(nameof(Prefix_ShapeMeleeDamage)));
            }
            catch (Exception e)
            {
                Log.Error("[Grandmaster 21] Failed to apply melee damage-shaping patch: " + e);
                return false;
            }

            // Death-on-downed suppression is a bonus, not a requirement. Without it, Downed
            // doctrine still picks non-vital anatomy and still pulls the strike; the storyteller
            // may simply convert a downing into a death occasionally, as it does in vanilla.
            MethodInfo checkState = AccessTools.Method(typeof(Pawn_HealthTracker), "CheckForStateChange");
            if (checkState == null || Gm21MeleeDownedGuard.ForceDownedField == null)
            {
                Log.Warning("[Grandmaster 21] Melee death-on-downed suppression is unavailable "
                            + "(Pawn_HealthTracker.CheckForStateChange or forceDowned not found). "
                            + "Downed doctrine still targets non-vital anatomy and pulls strikes.");
                return true;
            }

            try
            {
                harmony.Patch(checkState,
                    prefix: Hook(typeof(Gm21MeleeDownedGuard), nameof(Gm21MeleeDownedGuard.Prefix)),
                    postfix: NoPatch, transpiler: NoPatch,
                    finalizer: Hook(typeof(Gm21MeleeDownedGuard), nameof(Gm21MeleeDownedGuard.Finalizer)));
            }
            catch (Exception e)
            {
                Log.Warning("[Grandmaster 21] Could not patch melee death-on-downed suppression: " + e);
            }
            return true;
        }

        /// <summary>
        /// Riposte, cleave, disarm and critical strikes all resolve through the same two places:
        /// the TryCastShot postfix (already patched by ApplyPassive) and the tick scheduler. The
        /// scheduler is a GameComponent, which RimWorld instantiates by type scan with no patch at
        /// all, so the only thing to verify here is that the melee attack entry point the
        /// scheduler drives actually exists.
        /// </summary>
        private static bool ApplyReactions(Harmony harmony)
        {
            if (Gm21MeleeAction.TryMeleeAttackMethod == null)
            {
                Log.Warning("[Grandmaster 21] Pawn_MeleeVerbs.TryMeleeAttack not found; scheduled "
                            + "melee reactions (riposte, cleave, ally interception) are disabled.");
                return false;
            }
            return true;
        }

        private static bool ApplyProjectileDefence(Harmony harmony)
        {
            return Gm21ProjectileDefence.Apply(harmony, Hook);
        }

        // ------------------------------------------------------------------ patch bodies

        /// <summary>
        /// Opens the melee frame around one attack, and gives an ally Grandmaster the chance to
        /// step in before the attack is resolved at all.
        ///
        /// The frame is opened FIRST and unconditionally, so the finalizer's Close is always
        /// balanced whether the original runs, is skipped, or throws.
        /// </summary>
        internal static bool Prefix_OpenMeleeContext(Verb __instance, ref bool __result)
        {
            Pawn attacker = __instance.CasterPawn;
            Thing target = __instance.CurrentTarget.Thing;

            Gm21MeleeFrame frame = Gm21MeleeContext.Open(attacker, target);
            if (frame == null) return true;   // nesting too deep: pure vanilla from here

            if (attacker != null && Gm21Melee.IsActiveGrandmaster(attacker))
            {
                frame.attackerIsGm = true;
                frame.doctrine = Gm21MeleeDoctrineStore.Get(attacker);
                frame.atkAwareness = Gm21Melee.Awareness(attacker);
                frame.atkPrecision = Gm21Melee.Precision(attacker);
                frame.atkPower = Gm21Melee.Power(attacker);
            }

            Pawn defender = target as Pawn;
            if (defender != null && Gm21Melee.IsActiveGrandmaster(defender))
            {
                frame.defenderIsGm = true;
                frame.defDefence = Gm21Melee.Defence(defender);
            }
            else if (defender != null && Gm21Melee.AllyInterceptEnabled && attacker != null)
            {
                // The defender is not a Grandmaster. Someone three tiles away might be.
                if (Gm21AllyIntercept.TryIntercept(attacker, defender))
                {
                    __result = false;
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Finalizer, not postfix: it runs even when the attack throws, so the frame stack can
        /// never be left wedged.
        ///
        /// VOID ON PURPOSE, for the same reason as the shooting finalizers: a Harmony finalizer
        /// that RETURNS Exception replaces the pending exception rather than reporting it, so
        /// returning null there silently eats anything RimWorld or another mod's patch threw. A
        /// void finalizer cannot alter exception state at all, which is exactly what cleanup
        /// wants. Proved by execution in Tests/VerifyFinalizerSemantics.cs.
        /// </summary>
        internal static void Finalizer_CloseMeleeContext()
        {
            Gm21MeleeContext.Close();
        }

        /// <summary>
        /// Near-perfect martial execution.
        ///
        /// Applied at the single value TryCastShot rolls against, so it cannot double-apply. Every
        /// ordinary melee-accuracy penalty -- the pawn's MeleeHitChance stat, lighting, the
        /// target's posture -- has already been folded into this number, so the proportional
        /// removal compensates for all of them in one place rather than hunting down individual
        /// modifiers, which is where double-application and mod conflicts come from.
        ///
        /// Scaled by the attacker's own awareness composite, so a keener Grandmaster misses less
        /// often than an impaired one. It is NOT armour penetration and does not touch damage.
        /// </summary>
        internal static void Postfix_NonMissChance(ref float __result)
        {
            Gm21MeleeFrame frame = Gm21MeleeContext.Current;
            if (frame == null || !frame.attackerIsGm) return;
            __result = Gm21Melee.Compensate(__result, frame.atkAwareness);
        }

        /// <summary>
        /// Both sides of the defence roll, in the one place vanilla asks the question.
        ///
        /// ATTACKER side -- "ignore ~99% of the defence the defender's skill and reaction
        /// generate". The defender's dodge chance is SCALED DOWN, not zeroed, and armour is not
        /// touched anywhere: the Grandmaster is exploiting stance, timing and balance, and a
        /// breastplate does not care about any of that.
        ///
        /// DEFENDER side -- "almost never struck through ordinary melee skill". The same
        /// proportional rule the marksman package uses, scaled by the defender's defence
        /// composite, so a blind or armless or barely-mobile Grandmaster parries measurably worse
        /// than a whole one, and an unconscious or downed one is not here at all (CanAct already
        /// excluded them when the frame opened).
        ///
        /// ORDER MATTERS, and Grandmaster-versus-Grandmaster is the reason. Attacker first, then
        /// defender, means the defender's mastery answers whatever opening the attacker created --
        /// so two evenly matched Grandmasters produce a defender-favoured exchange, a parry, and a
        /// riposte that swaps the roles. That is the stalemate the brief asks for, and it falls
        /// out of the arithmetic rather than being special-cased.
        /// </summary>
        internal static void Postfix_DodgeChance(ref float __result, LocalTargetInfo target)
        {
            Gm21MeleeFrame frame = Gm21MeleeContext.Current;
            if (frame == null) return;

            // Recorded even for ordinary pawns: it is what later tells a dodge from a whiff.
            frame.dodgeConsulted = true;

            if (!frame.attackerIsGm && !frame.defenderIsGm) return;

            float dodge = __result;
            if (dodge < 0f) dodge = 0f;

            if (frame.attackerIsGm)
            {
                dodge *= Gm21Melee.RetainedFor(frame.atkAwareness);
            }
            if (frame.defenderIsGm)
            {
                // The single Grandmaster defensive resolution, replacing vanilla's value at the
                // one point vanilla rolls against it. No extra roll is added anywhere: a
                // Grandmaster defends once, like everyone else, just far better.
                dodge = Gm21Melee.DefenceChance(dodge, frame.defDefence);
            }

            __result = dodge;
        }

        /// <summary>
        /// The end of one exchange: either the strike landed, or the defender found an opening.
        ///
        /// A LANDED strike runs the on-hit package -- disarm, then cleave. (The critical
        /// multiplier is not applied here: it has already been decided and consumed by
        /// Pawn.PreApplyDamage, which is the only place damage can still be changed.)
        ///
        /// A FAILED strike against a Grandmaster defender schedules a riposte, but ONLY when the
        /// dodge chance was actually consulted. Vanilla consults it after the hit roll has already
        /// succeeded, so "consulted and still failed" means the defender dodged rather than the
        /// attacker whiffed -- a riposte answers a parry, not a stumble. When vanilla skips the
        /// dodge roll entirely (an immobile target, a surprise attack) nothing is scheduled, which
        /// is correct: there was no defensive action to counter from.
        /// </summary>
        internal static void Postfix_ResolveExchange(Verb __instance, bool __result)
        {
            Gm21MeleeFrame frame = Gm21MeleeContext.Current;
            if (frame == null) return;

            if (__result)
            {
                if (frame.attackerIsGm && Gm21Melee.ReactionsEnabled && !frame.onHitResolved)
                {
                    frame.onHitResolved = true;
                    Gm21OnHit.Resolve(frame, __instance);
                }
                return;
            }

            if (frame.defenderIsGm && frame.dodgeConsulted && Gm21Melee.ReactionsEnabled)
            {
                Pawn defender = frame.target as Pawn;
                if (defender != null)
                {
                    Gm21CombatScheduler.ScheduleRiposte(defender, frame.attacker);
                }
            }
        }

        /// <summary>
        /// Killer/Downed strike placement, critical damage and controlled force -- the only point
        /// in the pipeline where the body part and the damage amount can both still be changed,
        /// and still before armour resolves.
        ///
        /// Delegated to Gm21MeleeDamage so this file stays a patch map.
        /// </summary>
        internal static void Prefix_ShapeMeleeDamage(Pawn __instance, ref DamageInfo dinfo)
        {
            if (!Gm21Melee.DoctrineEnabled && !Gm21Melee.ReactionsEnabled) return;
            Gm21MeleeFrame frame = Gm21MeleeContext.Current;
            if (frame == null || !frame.attackerIsGm) return;
            Gm21MeleeDamage.Shape(frame, __instance, ref dinfo);
        }

        // ------------------------------------------------------------------ helpers

        internal static HarmonyMethod Hook(string name)
        {
            return Hook(typeof(Gm21MeleePatches), name);
        }

        internal static HarmonyMethod Hook(Type type, string name)
        {
            MethodInfo m = AccessTools.Method(type, name);
            return m == null ? null : new HarmonyMethod(m);
        }
    }

    /// <summary>
    /// Mod-environment checks. Kept separate from the shooting package's copy so that neither can
    /// change the other's behaviour: the brief's "do not redesign Shooting" applies to compat
    /// detection too, and the two feature sets touch different pipelines and may not always want
    /// the same verdict.
    /// </summary>
    internal static class Gm21MeleeCompat
    {
        /// <summary>
        /// Combat overhauls replace the melee and projectile pipelines wholesale: they retune or
        /// replace hit/dodge resolution, add their own armour and penetration model, and in
        /// Combat Extended's case replace projectile flight entirely. Every melee feature in this
        /// package sits inside one of those, so none of it is applied when one is active.
        ///
        /// Matched on package id rather than display name, so reuploads and translations do not
        /// slip past.
        /// </summary>
        internal static bool DetectCombatOverhaul(out string name)
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
                    if (id.Contains("yayo.combat"))
                    {
                        name = "Yayo's Combat";
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
}
