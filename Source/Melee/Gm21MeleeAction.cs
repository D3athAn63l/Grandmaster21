using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// Executes one scheduled melee reaction as an ordinary, top-level melee attack.
    ///
    /// WHY IT GOES THROUGH Pawn_MeleeVerbs.TryMeleeAttack AND NOT A BESPOKE DAMAGE CALL. A riposte
    /// is a real attack, and the brief requires it to flow through the normal damage, armour and
    /// body-part systems and to be defensible by another Grandmaster. TryMeleeAttack is exactly
    /// the entry point the game itself uses for a pawn swinging at something: it picks the pawn's
    /// best available melee verb (equipped weapon, natural tools, or a terrain-based verb),
    /// resolves hit and dodge, writes the combat log, awards skill XP and applies the follow-up
    /// cooldown. Reimplementing any of that would produce a counterattack that looked like an
    /// attack but did not behave like one -- and, critically, a riposte that re-entered THIS
    /// package's own patches would not be defensible, because those patches live on the verb.
    ///
    /// Going through the same door also means a riposte can itself be parried, which is what makes
    /// the counter-chain in Gm21CombatScheduler possible at all.
    /// </summary>
    internal static class Gm21MeleeAction
    {
        /// <summary>
        /// Existence check only -- the call itself is made directly against the public API.
        ///
        /// Resolving it reflectively at startup turns "this method was renamed in a future
        /// RimWorld" from a MissingMethodException thrown in the middle of combat into a feature
        /// that declines to enable itself and says so once in the log.
        /// </summary>
        internal static readonly MethodInfo TryMeleeAttackMethod =
            AccessTools.Method(typeof(Pawn_MeleeVerbs), "TryMeleeAttack",
                new[] { typeof(Thing), typeof(Verb), typeof(bool) });

        /// <summary>
        /// Can this pawn still take this reaction at all? Re-asked at fire time, never trusted
        /// from scheduling time: a tick has passed, and in that tick the actor may have been
        /// downed, killed, stunned, teleported, drafted away or had the target die.
        /// </summary>
        internal static bool CanReact(Pawn actor, Thing target)
        {
            if (actor == null || target == null || actor == target) return false;
            if (target.Destroyed || !target.Spawned) return false;
            if (!Gm21Melee.CanAct(actor)) return false;
            if (actor.Map == null || actor.Map != target.Map) return false;
            if (actor.meleeVerbs == null) return false;

            Pawn targetPawn = target as Pawn;
            if (targetPawn != null && targetPawn.Dead) return false;

            return true;
        }

        internal static void Execute(Pawn actor, Thing target, Gm21ActionKind kind)
        {
            if (!CanReact(actor, target)) return;
            if (TryMeleeAttackMethod == null) return;

            Verb verb = actor.meleeVerbs.TryGetMeleeVerb(target);
            if (verb == null) return;

            // Reach is the engine's call, not ours. A cleave target that stepped out of range in
            // the intervening tick, or a riposte target that fled, simply produces no attack.
            if (!verb.CanHitTarget(target)) return;

            if (!BypassCooldown(actor)) return;

            // A cleave is a follow-through and must not follow through again -- see
            // Gm21MeleeFrame.isFollowThrough. A riposte is a genuine fresh swing and is not
            // marked, so it cleaves like any other. The flag is consumed by the frame this attack
            // opens, and cleared in a finally so a throw cannot leak it onto someone else's swing.
            Gm21MeleeContext.NextIsFollowThrough = kind == Gm21ActionKind.Cleave;
            try
            {
                actor.meleeVerbs.TryMeleeAttack(target, verb, false);
            }
            finally
            {
                Gm21MeleeContext.NextIsFollowThrough = false;
            }
        }

        /// <summary>
        /// "Riposte ignores ordinary weapon cooldown."
        ///
        /// A melee pawn spends most of a fight inside Stance_Cooldown -- the recovery between
        /// swings -- and Verb_MeleeAttack.TryCastShot refuses to fire while the stance tracker
        /// reports a busy body. Without this, a riposte would silently do nothing in the common
        /// case, because the Grandmaster is nearly always mid-recovery when attacked.
        ///
        /// ONLY the recovery stance is cleared, and it is replaced with the engine's own
        /// Stance_Mobile rather than nulled. A Stance_Warmup is deliberately left alone: that is a
        /// deliberate wind-up the pawn chose to start, and cancelling it would let a reaction
        /// steal an attack the player ordered. Anything else that is busy is left alone too, and
        /// the reaction is simply declined.
        ///
        /// The cooldown this skips is the one already in progress. The attack that follows applies
        /// its own cooldown through the normal path, so a riposte does not make the Grandmaster
        /// permanently free of recovery -- it lets them answer during it.
        /// </summary>
        private static bool BypassCooldown(Pawn actor)
        {
            Pawn_StanceTracker stances = actor.stances;
            if (stances == null) return true;

            if (stances.curStance is Stance_Cooldown)
            {
                stances.SetStance(new Stance_Mobile());
                return true;
            }

            return !stances.FullBodyBusy;
        }
    }
}
