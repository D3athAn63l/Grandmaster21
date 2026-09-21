using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grandmaster21
{
    /// <summary>What the Guardian is answering, once a projectile is known to matter.</summary>
    internal enum Gm21ThreatIntent : byte
    {
        /// <summary>
        /// An enemy fired it, OR a friendly deliberately aimed it at someone the Guardian
        /// protects. Both are hostile intent, and both may be sent back.
        /// </summary>
        Hostile = 0,

        /// <summary>
        /// A friendly shot that was aimed at a legitimate enemy and is now going to hit the wrong
        /// person. Corrective, not offensive: the Guardian salvages it, it is never returned to
        /// the ally who fired it.
        /// </summary>
        AccidentalFriendlyFire = 1
    }

    /// <summary>A credible, imminent threat, and the Guardian who will answer it.</summary>
    internal struct Gm21Threat
    {
        public Pawn guardian;
        /// <summary>The protected pawn this projectile is actually going to harm.</summary>
        public Pawn victim;
        public IntVec3 impactCell;
        public Gm21ThreatIntent intent;
        public bool explosive;
        /// <summary>Distance from the Guardian to the victim, for the micro-dash roll.</summary>
        public float dashDistance;
        /// <summary>The Guardian's own interception probability, already computed for selection.</summary>
        public float reachChance;
    }

    /// <summary>
    /// THE GUARDIAN DOCTRINE'S FRONT DOOR: is this projectile worth reacting to at all, and if so,
    /// who answers it?
    ///
    /// THE RULE THIS FILE EXISTS TO ENFORCE. A Melee Grandmaster is not a CIWS turret. They do not
    /// swat at every round that happens to fly through their three tiles. They react only to a
    /// projectile that credibly threatens themselves or a protected ally. An enemy minigun burst
    /// of a hundred rounds, of which ninety miss harmlessly and four hit other raiders, produces
    /// exactly six Guardian reactions -- and the other ninety-four cost one grid lookup each.
    ///
    /// HOW THE THREAT IS DETERMINED, AND WHY NOT BY SIMULATION. RimWorld has already resolved
    /// where this projectile is going: Verb_LaunchProjectile decides hit, miss or cover at CAST
    /// time and launches the projectile at the result. The projectile therefore carries both
    /// answers -- usedTarget is where it is ACTUALLY going, intendedTarget is who the shooter
    /// MEANT to hit -- and its destination field is where it will physically land. Reading those
    /// is strictly better than re-simulating a trajectory: it is cheaper, and it agrees with the
    /// engine by construction rather than by approximation.
    ///
    /// WHAT IS DELIBERATELY NOT MODELLED. RimWorld can also hit a pawn standing in the flight path
    /// through its own free-intercept roll, which is probabilistic and cannot be predicted without
    /// exactly the trajectory simulator the design rules out. A Guardian therefore does not react
    /// to a round that would have clipped a bystander by chance. That is a deliberate, documented
    /// limit, and it errs toward doing nothing.
    /// </summary>
    internal static class Gm21GuardianThreat
    {
        /// <summary>
        /// Reused across calls on the main thread. Sized for the handful of pawns a blast can
        /// cover; it grows if a modded explosion is enormous and then stays grown.
        /// </summary>
        [System.ThreadStatic] private static List<Pawn> victimBuffer;

        /// <summary>
        /// THE FAST PATH AND THE WHOLE DECISION, in the order that rejects most cheaply.
        ///
        /// Returns false -- meaning "ignore this projectile entirely" -- for every harmless round,
        /// and does so before any Guardian scan, any reach maths, any safe-vector work and any
        /// return-chance calculation.
        /// </summary>
        internal static bool TryResolve(Projectile proj, ProjectileProperties props, Map map,
                                        IntVec3 impactCell, out Gm21Threat threat)
        {
            threat = default(Gm21Threat);

            List<Pawn> victims = victimBuffer;
            if (victims == null) victims = victimBuffer = new List<Pawn>(8);
            victims.Clear();

            bool explosive = props.explosionRadius > 0f;
            CollectEndangeredPawns(proj, props, map, impactCell, explosive, victims);

            // THE 94 HARMLESS ROUNDS EXIT HERE. Nothing is going to be hit by this projectile, so
            // there is nothing to guard and no reason to look for a Guardian.
            if (victims.Count == 0) return false;

            Thing launcher = proj.Launcher;
            Pawn bestGuardian = null;
            Pawn bestVictim = null;
            float bestScore = 0f;
            float bestDistance = 0f;
            float bestReach = 0f;

            float difficulty = Gm21ProjectileDefence.Difficulty(props);

            for (int i = 0; i < victims.Count; i++)
            {
                Pawn victim = victims[i];

                Pawn guardian;
                float distance, reach, score;
                if (!SelectBestGuardian(victim, launcher, map, difficulty,
                                        out guardian, out distance, out reach, out score))
                {
                    continue;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestGuardian = guardian;
                    bestVictim = victim;
                    bestDistance = distance;
                    bestReach = reach;
                }
            }

            victims.Clear();
            if (bestGuardian == null) return false;

            threat.guardian = bestGuardian;
            threat.victim = bestVictim;
            threat.impactCell = impactCell;
            threat.explosive = explosive;
            threat.dashDistance = bestDistance;
            threat.reachChance = bestReach;
            threat.intent = ClassifyIntent(bestGuardian, bestVictim, proj, props, map);
            return true;
        }

        // ---------------------------------------------------------------- who gets hit

        /// <summary>
        /// Everyone this projectile is actually going to harm, from the engine's own resolution.
        ///
        /// DIRECT projectiles harm exactly one thing: whatever usedTarget resolved to, or whoever
        /// happens to be standing on the cell it lands on. A round that resolved to bare ground
        /// with nobody on it harms nobody, which is the case this whole doctrine is built around.
        ///
        /// EXPLOSIVE projectiles harm everyone inside the blast, which is why a rocket aimed at
        /// the dirt beside Bob still counts: the brief is explicit that the projectile does not
        /// have to collide with Bob to threaten him.
        /// </summary>
        private static void CollectEndangeredPawns(Projectile proj, ProjectileProperties props,
                                                   Map map, IntVec3 impactCell, bool explosive,
                                                   List<Pawn> into)
        {
            if (explosive)
            {
                CollectPawnsInRadius(map, impactCell, props.explosionRadius, into);
                return;
            }

            // Resolved onto a specific thing: that is who takes it, wherever they are standing.
            Pawn resolved = proj.usedTarget.Thing as Pawn;
            if (resolved != null && resolved.Spawned && resolved.Map == map && !resolved.Dead)
            {
                into.Add(resolved);
                return;
            }

            // Resolved onto a cell. One grid lookup decides whether this round matters at all.
            if (!impactCell.InBounds(map)) return;
            List<Thing> things = map.thingGrid.ThingsListAtFast(impactCell);
            if (things == null) return;
            for (int i = 0; i < things.Count; i++)
            {
                Pawn pawn = things[i] as Pawn;
                if (pawn != null && !pawn.Dead) into.Add(pawn);
            }
        }

        private static void CollectPawnsInRadius(Map map, IntVec3 centre, float radius, List<Pawn> into)
        {
            int r = Mathf.CeilToInt(radius);
            float rSquared = radius * radius;

            for (int dz = -r; dz <= r; dz++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    if (dx * dx + dz * dz > rSquared) continue;

                    IntVec3 cell = new IntVec3(centre.x + dx, centre.y, centre.z + dz);
                    if (!cell.InBounds(map)) continue;

                    List<Thing> things = map.thingGrid.ThingsListAtFast(cell);
                    if (things == null) continue;
                    for (int i = 0; i < things.Count; i++)
                    {
                        Pawn pawn = things[i] as Pawn;
                        if (pawn != null && !pawn.Dead && !into.Contains(pawn)) into.Add(pawn);
                    }
                }
            }
        }

        // ---------------------------------------------------------------- who answers

        /// <summary>
        /// THE BEST Guardian for this victim, not the first one grid iteration happens to reach.
        ///
        /// The score IS the interception probability: getting there (movement speed, reaction,
        /// distance, opposed by the projectile) multiplied by turning it once there (manipulation,
        /// sight, consciousness, what is in their hands). Scoring by the thing we actually care
        /// about means there is no separate heuristic to keep honest, and it produces the
        /// behaviour the brief asks for without special cases: a much faster Grandmaster three
        /// tiles away beats a sluggish one standing adjacent, because they really are more likely
        /// to make the catch.
        ///
        /// ONE Guardian attempts, and a failure is a failure. Letting five Grandmasters each roll
        /// against the same bullet until one succeeds would make defence a function of headcount
        /// rather than of skill.
        /// </summary>
        private static bool SelectBestGuardian(Pawn victim, Thing launcher, Map map, float difficulty,
                                               out Pawn guardian, out float distance,
                                               out float reachChance, out float score)
        {
            guardian = null;
            distance = 0f;
            reachChance = 0f;
            score = 0f;

            if (!victim.Spawned || victim.Map != map) return false;

            IntVec3 centre = victim.Position;
            int radius = Mathf.CeilToInt(Gm21Melee.ProtectiveRadius);

            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int distSquared = dx * dx + dz * dz;
                    if (distSquared > Gm21Melee.ProtectiveRadiusSquared) continue;

                    IntVec3 cell = new IntVec3(centre.x + dx, centre.y, centre.z + dz);
                    if (!cell.InBounds(map)) continue;

                    List<Thing> things = map.thingGrid.ThingsListAtFast(cell);
                    if (things == null) continue;

                    for (int i = 0; i < things.Count; i++)
                    {
                        Pawn candidate = things[i] as Pawn;
                        if (candidate == null) continue;

                        // Cheapest first: an integer field comparison rejects every ordinary pawn.
                        if (!Gm21Melee.IsMeleeGrandmaster(candidate)) continue;
                        if (!Gm21Melee.CanAct(candidate)) continue;
                        if (candidate == launcher) continue;
                        if (!Protects(candidate, victim)) continue;

                        float d = Mathf.Sqrt(distSquared);

                        // The micro-dash is abstract, but the route is not. No reaching through
                        // sealed granite.
                        if (!Gm21Reach.CanDashTo(map, candidate.Position, victim.Position)) continue;

                        float reach = ReachChance(candidate, difficulty, d);
                        if (reach <= 0f) continue;

                        float candidateScore = reach * Gm21Melee.Opposed(
                            Gm21ProjectileDefence.DeflectionQuality(candidate), difficulty,
                            Gm21ProjectileDefence.DeflectHardnessValue,
                            Gm21ProjectileDefence.MaxDeflectChanceValue);

                        if (candidateScore > score)
                        {
                            score = candidateScore;
                            guardian = candidate;
                            distance = d;
                            reachChance = reach;
                        }
                    }
                }
            }

            return guardian != null;
        }

        /// <summary>
        /// Stage 3's roll, computed here too because Guardian selection has to compare it.
        /// Movement speed is the dominant term, scaled by how quickly the pawn registers what is
        /// happening, opposed by how hard the projectile is to reach, and reduced with distance
        /// across the zone.
        /// </summary>
        internal static float ReachChance(Pawn guardian, float difficulty, float distance)
        {
            return Gm21InterceptCurve.Evaluate(
                       Gm21Melee.MoveSpeed(guardian) * Gm21Melee.Reaction(guardian) / difficulty)
                 * Gm21InterceptCurve.DistanceFactor(distance);
        }

        /// <summary>
        /// Does this Grandmaster protect this pawn?
        ///
        /// Themselves always. Otherwise the pawn must be genuinely ON THEIR SIDE -- same faction,
        /// or a faction they are formally allied with. "Not hostile" is deliberately NOT enough:
        /// that would sweep in every neutral trader, visitor and wild animal that wandered past,
        /// and the brief says neutrals are not automatically protected.
        /// </summary>
        internal static bool Protects(Pawn guardian, Pawn victim)
        {
            // Null first: without this, Protects(null, null) would take the identity branch and
            // answer "yes", which is nonsense that would then propagate into the safe-vector
            // scoring as a phantom protected pawn.
            if (guardian == null || victim == null || victim.Dead) return false;
            if (guardian == victim) return true;

            Faction gf = guardian.Faction, vf = victim.Faction;
            if (gf == null || vf == null) return false;
            if (GenHostility.HostileTo(victim, guardian)) return false;
            if (gf == vf) return true;

            try
            {
                return vf.RelationKindWith(gf) == FactionRelationKind.Ally;
            }
            catch
            {
                // A modded faction whose relation table is incomplete must not break combat.
                return false;
            }
        }

        // ---------------------------------------------------------------- intent

        /// <summary>
        /// INTENT OUTRANKS FACTION IDENTITY.
        ///
        /// A shot is hostile if an enemy fired it, or if an ally fired it AT someone the Guardian
        /// protects. The second half is the anti-abuse rule and it matters: without it, ordering a
        /// low-Shooting colonist to force-attack the Grandmaster would turn the Grandmaster into a
        /// free aim-correction computer, salvaging every deliberately terrible shot toward a
        /// convenient enemy. With it, deliberately shooting at a Melee Grandmaster means the round
        /// may come straight back, which is exactly the deterrent the design wants.
        ///
        /// Everything else from a friendly launcher is an accident, and accidents are corrected,
        /// never punished.
        /// </summary>
        internal static Gm21ThreatIntent ClassifyIntent(Pawn guardian, Pawn victim, Projectile proj,
                                                        ProjectileProperties props, Map map)
        {
            Thing launcher = proj.Launcher;

            // No launcher, or an enemy launcher: hostile, with nothing further to ask.
            if (launcher == null) return Gm21ThreatIntent.Hostile;
            if (GenHostility.HostileTo(launcher, guardian)) return Gm21ThreatIntent.Hostile;

            // A friendly launcher. What were they aiming AT?
            Thing intended = proj.intendedTarget.Thing;
            if (intended != null)
            {
                if (intended == guardian) return Gm21ThreatIntent.Hostile;

                Pawn intendedPawn = intended as Pawn;
                if (intendedPawn != null && Protects(guardian, intendedPawn))
                {
                    // Deliberately aimed at someone under this Guardian's protection.
                    return Gm21ThreatIntent.Hostile;
                }
                return Gm21ThreatIntent.AccidentalFriendlyFire;
            }

            // Aimed at a cell rather than a thing. For an ordinary bullet that is just ground
            // fire. For an EXPLOSIVE it is not: dropping a warhead on a cell whose blast covers a
            // protected pawn is aiming at that pawn, however the order was phrased.
            if (props.explosionRadius > 0f && victim != null)
            {
                IntVec3 aimed = proj.intendedTarget.Cell;
                if (aimed.IsValid
                    && aimed.DistanceTo(victim.Position) <= props.explosionRadius)
                {
                    return Gm21ThreatIntent.Hostile;
                }
            }

            return Gm21ThreatIntent.AccidentalFriendlyFire;
        }
    }
}
