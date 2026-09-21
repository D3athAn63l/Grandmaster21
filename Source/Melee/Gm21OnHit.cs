using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// What happens the instant a Grandmaster's strike lands, in the order it happens.
    ///
    /// The critical roll is deliberately NOT here: it has to be decided before the damage is
    /// applied, and by the time an attack reports success the damage is already resolved. It
    /// happens in Gm21MeleeDamage, and this file only reads its outcome -- which is why a
    /// critical can improve the disarm that follows it.
    /// </summary>
    internal static class Gm21OnHit
    {
        internal static void Resolve(Gm21MeleeFrame frame, Verb verb)
        {
            Pawn attacker = frame.attacker;
            if (attacker == null) return;

            bool critical = frame.critMultiplier > 1f;

            // Disarm first: it is about the blow that just landed, and its success feeds nothing
            // else. Cleave is a follow-through and belongs after the strike it follows.
            Pawn victim = frame.target as Pawn;
            if (victim != null && !victim.Dead)
            {
                Gm21Disarm.TryDisarm(attacker, victim, frame.atkPrecision, critical);
            }

            // Cleave is scheduled, never executed here, for the same reason ripostes are: a
            // follow-through that resolved inside the swing it follows would put a second melee
            // attack on the current call stack, and a crowded fight would nest them.
            //
            // And a follow-through does not follow through again: one swing carries into the
            // enemies around the one it hit, it does not start a new swing that spreads further.
            if (!frame.isFollowThrough)
            {
                Gm21Cleave.TryCleave(attacker, frame.target, verb, frame.atkPower);
            }
        }
    }
}
