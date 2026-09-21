using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// Ally melee interception -- the Grandmaster's three-tile defensive zone.
    ///
    /// An enemy swings at the researcher. A Grandmaster three tiles away decides otherwise. The
    /// attack does not land, and the enemy now has a much more immediate problem.
    ///
    /// HOW THE INTERCEPTION IS REPRESENTED, AND WHY. The brief explicitly permits a logical
    /// representation when physically moving the pawn would be unsafe, and it would be: relocating
    /// a pawn mid-attack means fighting the job system, the pathing reservation system and the
    /// stance tracker all at once, and every one of those is a source of stuck colonists. What is
    /// implemented instead is the OUTCOME -- the attack is turned aside and the Grandmaster
    /// answers it -- with every PHYSICAL CONSTRAINT still enforced: range, consciousness,
    /// mobility, and an actual unobstructed walkable route. No pawn is moved, so no pawn can be
    /// misplaced, and nothing phases through a wall.
    ///
    /// TWO ROLLS, NOT ONE. Getting there is a movement problem; turning the blow aside once there
    /// is a defensive one. A very fast Grandmaster with ruined arms arrives and fails; a superb
    /// duellist who cannot walk never arrives at all. Both must succeed, which is what makes the
    /// stat mapping in the brief actually visible in play.
    /// </summary>
    internal static class Gm21AllyIntercept
    {
        /// <summary>
        /// Interception chance for this guardian, at this distance.
        ///
        /// The curve and the distance falloff live in Gm21InterceptCurve, shared with projectile
        /// interception, so the brief's "8 m/s -> approximately 90%" benchmark is stated in one
        /// place. Here the effective speed is simply the pawn's real MoveSpeed scaled by how
        /// quickly they register what is happening: a fast pawn who cannot see or think does not
        /// arrive in time, however fast their legs are.
        /// </summary>
        internal static float Chance(float moveSpeed, float reaction, float distance)
        {
            if (moveSpeed <= 0f || reaction <= 0f) return 0f;
            return Gm21InterceptCurve.Evaluate(moveSpeed * reaction)
                 * Gm21InterceptCurve.DistanceFactor(distance);
        }

        /// <summary>
        /// Looks for a Grandmaster willing and able to take this attack, and lets them.
        ///
        /// Returns true if the attack was intercepted, in which case the caller aborts the
        /// original attack entirely -- the intended victim is simply not struck.
        /// </summary>
        internal static bool TryIntercept(Pawn attacker, Pawn victim)
        {
            if (attacker == null || victim == null || attacker == victim) return false;
            if (!victim.Spawned || victim.Map == null) return false;
            if (victim.Map != attacker.Map) return false;

            // Only real aggression is intercepted. Social fights, training and any other
            // non-hostile melee are left completely alone.
            if (!GenHostility.HostileTo(attacker, victim)) return false;

            Map map = victim.Map;
            IntVec3 centre = victim.Position;
            int radius = Mathf.CeilToInt(Gm21Melee.ProtectiveRadius);

            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    // Circle, not square: the radius is a distance, and the corners of a 7x7
                    // block are 4.24 tiles away.
                    int distSquared = dx * dx + dz * dz;
                    if (distSquared > Gm21Melee.ProtectiveRadiusSquared) continue;

                    IntVec3 cell = new IntVec3(centre.x + dx, centre.y, centre.z + dz);
                    if (!cell.InBounds(map)) continue;

                    List<Thing> things = map.thingGrid.ThingsListAtFast(cell);
                    if (things == null) continue;

                    for (int i = 0; i < things.Count; i++)
                    {
                        Pawn guardian = things[i] as Pawn;
                        if (guardian == null || guardian == victim || guardian == attacker) continue;
                        if (!IsWillingGuardian(guardian, attacker, victim)) continue;

                        if (TryGuard(guardian, attacker, victim, map, Mathf.Sqrt(distSquared)))
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Is this pawn a Grandmaster who would, and could, step in for the victim?
        ///
        /// Ordered cheapest first: the level check is an integer field comparison, and every
        /// ordinary pawn in the zone exits on it.
        /// </summary>
        private static bool IsWillingGuardian(Pawn guardian, Pawn attacker, Pawn victim)
        {
            if (!Gm21Melee.IsMeleeGrandmaster(guardian)) return false;
            if (!Gm21Melee.CanAct(guardian)) return false;

            // Protecting someone means being on their side and against the attacker's. Both
            // halves matter: a hostile Grandmaster standing nearby does not shield your colonist,
            // and a Grandmaster does not throw themselves in front of their own ally's blow.
            if (GenHostility.HostileTo(guardian, victim)) return false;
            if (!GenHostility.HostileTo(guardian, attacker)) return false;

            // Getting there is movement. A Grandmaster who cannot move cannot cross three tiles
            // in the time an opponent takes to swing, however good they are.
            if (!guardian.health.capacities.CapableOf(PawnCapacityDefOf.Moving)) return false;

            return true;
        }

        private static bool TryGuard(Pawn guardian, Pawn attacker, Pawn victim, Map map, float distance)
        {
            // A physical route, not a teleport. Shared with projectile interception through
            // Gm21Reach so both Guardian systems answer reachability the same way; the behaviour
            // here is unchanged -- it is the same walkability-validated line it always was.
            if (!Gm21Reach.CanDashTo(map, guardian.Position, victim.Position)) return false;

            float reaction = Gm21Melee.Reaction(guardian);
            if (!Rand.Chance(Chance(Gm21Melee.MoveSpeed(guardian), reaction, distance))) return false;

            // Arriving is not the same as succeeding. The blow still has to be turned aside, and
            // that is the same defensive composite the passive parry uses.
            float parry = Gm21Melee.Compensate(0f, Gm21Melee.Defence(guardian));
            if (!Rand.Chance(parry)) return false;

            if (guardian.Map != null)
            {
                MoteMaker.ThrowText(guardian.DrawPos, guardian.Map,
                    "GM21_Melee_Intercepted".Translate(), 3.8f);
            }

            // The defence succeeded, so the Grandmaster is owed a riposte -- scheduled, never
            // resolved here, because this is running inside the attacker's own TryCastShot.
            Gm21CombatScheduler.ScheduleRiposte(guardian, attacker);
            return true;
        }
    }
}
