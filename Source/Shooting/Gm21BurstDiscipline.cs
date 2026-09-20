using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// Burst discipline: a Grandmaster releases the trigger once the objective is met.
    ///
    /// THE PROBLEM THIS SOLVES. RimWorld fires a burst by calling Verb.TryCastNextBurstShot once
    /// per shot, and the target's anatomy changes between those calls. Choosing a body part once
    /// and living with it for the whole burst is wrong, and so is continuing to fire after the
    /// target is already on the ground.
    ///
    /// HOW THE BURST IS STOPPED, AND WHY IT IS SAFE. Verb.TryCastNextBurstShot is, in essence:
    ///
    ///     if (Available() &amp;&amp; TryCastShot()) { ...effects...; burstShotsLeft--; }
    ///     else                                 { burstShotsLeft = 0; }
    ///     if (burstShotsLeft &gt; 0) { schedule next shot; return; }
    ///     state = Idle; apply cooldown stance; invoke castCompleteCallback;
    ///
    /// A TryCastShot that returns false is therefore already a first-class outcome the engine
    /// handles every time a shot line is lost mid-burst: burstShotsLeft is zeroed and control
    /// falls through to the ordinary end-of-burst path, so the cooldown stance is still applied,
    /// the completion callback still fires, and the verb still returns to Idle. Nothing is left
    /// half-finished. That makes "make TryCastShot return false" the safe cancellation point the
    /// design called for -- no burst counter is poked directly, and no lifecycle step is skipped.
    ///
    /// WHY THE FIRST SHOT OF A BURST IS NEVER HELD. If a Grandmaster could decline to fire at all,
    /// a job that keeps re-issuing the attack would spin: aim, decline, aim, decline. Requiring at
    /// least one shot per burst guarantees forward progress while still delivering the intended
    /// behaviour, which is about not emptying the rest of a magazine into someone already down.
    /// Single-shot weapons therefore never hold fire, which is correct -- there is no "rest of the
    /// burst" to withhold.
    /// </summary>
    internal static class Gm21Burst
    {
        // Verb.burstShotsLeft and Verb.ShotsPerBurst are both protected, so both are read
        // reflectively and cached once. If either ever disappears, burst holding disables itself
        // and per-projectile less-lethal retargeting carries on alone -- the feature degrades, it
        // does not break.
        private static readonly FieldInfo BurstShotsLeftField =
            AccessTools.Field(typeof(Verb), "burstShotsLeft");

        private static readonly MethodInfo ShotsPerBurstGetter =
            AccessTools.PropertyGetter(typeof(Verb), "ShotsPerBurst");

        internal static bool Available
        {
            get { return BurstShotsLeftField != null && ShotsPerBurstGetter != null; }
        }

        /// <summary>
        /// True when this Grandmaster should stop the remainder of the current burst.
        ///
        /// Ordered so an ordinary shooter exits on the first comparison.
        /// </summary>
        internal static bool ShouldHoldFire(Verb verb)
        {
            if (!Available) return false;
            if (!Gm21Shooting.AnatomicalTargetingEnabled) return false;

            Pawn shooter = verb.CasterPawn;
            if (shooter == null) return false;

            Gm21AimMode mode = Gm21AimModeStore.Get(shooter);
            if (mode == Gm21AimMode.Normal) return false;
            if (!Gm21Shooting.IsShootingGrandmaster(shooter)) return false;

            // Never hold the opening shot -- see the class comment on job spin.
            if (!IsMidBurst(verb)) return false;

            Pawn victim = verb.CurrentTarget.Thing as Pawn;
            if (victim == null || victim.Destroyed) return false;

            if (mode == Gm21AimMode.Killer)
            {
                // Optional, and strictly an ammo courtesy: the target is already dead.
                return victim.Dead;
            }

            // Downed mode. Two reasons to stop: the objective is achieved, or there is no longer a
            // shot that can be taken without aiming at something that kills.
            if (victim.Dead || victim.Downed) return true;
            return Gm21BodyTargeting.ChooseIncapacitatingPart(victim) == null;
        }

        /// <summary>
        /// True once at least one shot of the current burst has already been fired.
        ///
        /// burstShotsLeft is decremented AFTER TryCastShot returns, so on the opening shot it
        /// still equals ShotsPerBurst.
        /// </summary>
        private static bool IsMidBurst(Verb verb)
        {
            object left = BurstShotsLeftField.GetValue(verb);
            if (!(left is int)) return false;

            object total = ShotsPerBurstGetter.Invoke(verb, null);
            if (!(total is int)) return false;

            return (int)left < (int)total;
        }
    }
}
