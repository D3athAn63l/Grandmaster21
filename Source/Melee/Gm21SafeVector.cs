using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// Where to send something you have just caught and cannot send back.
    ///
    /// "Do not randomly swat a grenade into the colony hospital." This is the file that keeps that
    /// promise, and the promise is what makes safe deflection a success rather than a coin flip.
    ///
    /// THE METHOD. Sixteen candidate bearings are tried around the interception point. Each one is
    /// scored by what is standing near where the object would land, weighted by the blast radius
    /// it would land with, and the best-scoring bearing wins. Allies count heavily against a
    /// bearing, hostiles count for it, and distance from the Grandmaster is worth a little on its
    /// own -- so the priority order comes out as the brief specifies: away from the Grandmaster,
    /// away from allies, toward open space, and preferably toward the enemy.
    ///
    /// DELIBERATELY LOCAL AND CHEAP. Sixteen bearings times a handful of cells each is a few
    /// hundred grid lookups, and it runs only at the moment a deflection actually happens -- not
    /// per tick, not per projectile, and never for an ordinary pawn. No pathfinding, no map scan,
    /// no allocation beyond one reused list.
    /// </summary>
    internal static class Gm21SafeVector
    {
        /// <summary>Bearings tried, evenly spaced. Sixteen is fine enough to always find a gap.</summary>
        private const int Bearings = 16;

        /// <summary>Score lost for each friendly or neutral pawn in the landing area.</summary>
        private const float AllyPenalty = 100f;

        /// <summary>Score gained for each hostile pawn in the landing area.</summary>
        private const float HostileBonus = 25f;

        /// <summary>Score per tile of separation between the landing point and the Grandmaster.</summary>
        private const float DistanceWeight = 2f;

        /// <summary>Minimum area examined around a candidate landing point, for inert projectiles.</summary>
        private const float MinBlastCheckRadius = 1.5f;

        /// <summary>A landing point closer than this to the Grandmaster is never chosen.</summary>
        private const float MinSeparation = 2f;

        /// <summary>
        /// Picks the least dangerous landing cell at the given distance, or an invalid cell if
        /// every bearing is blocked or off-map.
        /// </summary>
        internal static IntVec3 Choose(Projectile proj, ProjectileProperties props, Pawn guardian,
                                       Map map, float distance)
        {
            IntVec3 from = proj.Position;
            if (!from.InBounds(map)) from = guardian.Position;

            float checkRadius = props.explosionRadius > MinBlastCheckRadius
                ? props.explosionRadius
                : MinBlastCheckRadius;

            IntVec3 best = IntVec3.Invalid;
            float bestScore = float.NegativeInfinity;

            for (int i = 0; i < Bearings; i++)
            {
                float angle = i * (2f * Mathf.PI / Bearings);
                IntVec3 candidate = new IntVec3(
                    from.x + Mathf.RoundToInt(Mathf.Cos(angle) * distance),
                    from.y,
                    from.z + Mathf.RoundToInt(Mathf.Sin(angle) * distance));

                candidate = ClampIntoMap(candidate, map);
                if (!candidate.InBounds(map)) continue;

                float separation = candidate.DistanceTo(guardian.Position);
                if (separation < MinSeparation) continue;

                float score = Score(candidate, map, guardian, checkRadius, separation);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return best;
        }

        /// <summary>
        /// Pulls a candidate back inside the map rather than discarding it. A Grandmaster fighting
        /// near the map edge would otherwise lose most of their bearings, which is exactly when
        /// they can least afford to.
        /// </summary>
        private static IntVec3 ClampIntoMap(IntVec3 cell, Map map)
        {
            IntVec3 size = map.Size;
            int x = Mathf.Clamp(cell.x, 0, size.x - 1);
            int z = Mathf.Clamp(cell.z, 0, size.z - 1);
            return new IntVec3(x, cell.y, z);
        }

        /// <summary>
        /// How good a landing point this is. Higher is better.
        ///
        /// The pawn scan covers the area the thing would actually affect: the blast radius for an
        /// explosive, a small neighbourhood for anything inert. That is what stops a "safe"
        /// deflection from putting a grenade two tiles from the surgeon.
        /// </summary>
        private static float Score(IntVec3 cell, Map map, Pawn guardian, float checkRadius, float separation)
        {
            float score = separation * DistanceWeight;

            int radius = Mathf.CeilToInt(checkRadius);
            float radiusSquared = checkRadius * checkRadius;

            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx * dx + dz * dz > radiusSquared) continue;

                    IntVec3 c = new IntVec3(cell.x + dx, cell.y, cell.z + dz);
                    if (!c.InBounds(map)) continue;

                    List<Thing> things = map.thingGrid.ThingsListAtFast(c);
                    if (things == null) continue;

                    for (int i = 0; i < things.Count; i++)
                    {
                        Pawn pawn = things[i] as Pawn;
                        if (pawn == null || pawn.Dead) continue;

                        // The Grandmaster's own presence is already priced in by the separation
                        // term; counting them again would just bias every bearing equally.
                        if (pawn == guardian) continue;

                        score += GenHostility.HostileTo(pawn, guardian) ? HostileBonus : -AllyPenalty;
                    }
                }
            }

            return score;
        }
    }
}
