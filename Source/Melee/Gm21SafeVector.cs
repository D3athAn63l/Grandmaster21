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
    /// scored by what is standing near where the object would land AND by what it would have to
    /// fly through to get there, and the best-scoring bearing wins. Protected pawns count heavily
    /// against a bearing, hostiles count for it, and separation from the Grandmaster and from the
    /// pawn just saved is worth something on its own -- so the priority order comes out as the
    /// brief specifies: away from the threatened pawn, away from the Grandmaster, away from other
    /// friendlies, a clear flight path, open space, hostile space where safe.
    ///
    /// WHY THE FLIGHT PATH IS SCORED, NOT JUST THE LANDING POINT. A bullet deflected forty tiles
    /// "somewhere safe" travels through forty tiles on the way. Scoring only the destination would
    /// happily fire it down a corridor full of colonists as long as the far end was empty, which
    /// is a different way of doing exactly the harm this code exists to avoid.
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

        /// <summary>Score lost for each protected pawn in the landing area.</summary>
        private const float AllyPenalty = 100f;

        /// <summary>
        /// Score lost for each protected pawn the redirected projectile would fly THROUGH.
        ///
        /// Lower than the landing penalty on purpose: a pawn standing in the flight path of a fast
        /// projectile is at real but not certain risk, whereas a pawn standing where it lands is
        /// where the damage or the blast actually happens.
        /// </summary>
        private const float PathPenalty = 60f;

        /// <summary>Score gained for each hostile pawn in the landing area.</summary>
        private const float HostileBonus = 25f;

        /// <summary>Score per tile of separation between the landing point and the Grandmaster.</summary>
        private const float DistanceWeight = 2f;

        /// <summary>
        /// Score per tile of separation between the landing point and the pawn just rescued.
        /// Weighted above the Grandmaster's own separation: the brief's first priority is away
        /// from the threatened pawn, and sending their near-miss to land on them anyway would be
        /// a poor rescue.
        /// </summary>
        private const float SavedDistanceWeight = 3f;

        /// <summary>
        /// How many points along a candidate flight path are examined. The path can be forty tiles
        /// long, and walking every cell of sixteen bearings would be thousands of grid reads for a
        /// question a coarse sample answers just as well -- a pawn occupies a cell, and a sample
        /// every couple of tiles cannot miss a group of them.
        /// </summary>
        private const int PathSamples = 12;

        /// <summary>Path sampling starts past the Grandmaster's own cell, not on top of it.</summary>
        private const float PathSampleStart = 1.5f;

        /// <summary>Minimum area examined around a candidate landing point, for inert projectiles.</summary>
        private const float MinBlastCheckRadius = 1.5f;

        /// <summary>A landing point closer than this to the Grandmaster is never chosen.</summary>
        private const float MinSeparation = 2f;

        /// <summary>
        /// Picks the least dangerous landing cell at the given distance, or an invalid cell if
        /// every bearing is blocked or off-map.
        /// </summary>
        internal static IntVec3 Choose(Projectile proj, ProjectileProperties props, Pawn guardian,
                                       Pawn saved, Map map, float distance)
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

                if (saved != null && saved != guardian && saved.Spawned
                    && candidate.DistanceTo(saved.Position) < MinSeparation)
                {
                    continue;   // never land it back on the pawn we just rescued
                }

                float score = Score(candidate, map, guardian, saved, checkRadius, separation)
                            + PathScore(from, candidate, map, guardian);
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
        private static float Score(IntVec3 cell, Map map, Pawn guardian, Pawn saved,
                                   float checkRadius, float separation)
        {
            float score = separation * DistanceWeight;

            if (saved != null && saved != guardian && saved.Spawned)
            {
                score += cell.DistanceTo(saved.Position) * SavedDistanceWeight;
            }

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

                        // "Protected" rather than merely "not hostile": the same rule the threat
                        // model uses, so the pawns a Guardian refuses to endanger are exactly the
                        // pawns a Guardian would have defended.
                        if (Gm21GuardianThreat.Protects(guardian, pawn)) score -= AllyPenalty;
                        else if (GenHostility.HostileTo(pawn, guardian)) score += HostileBonus;
                    }
                }
            }

            return score;
        }

        /// <summary>
        /// What the redirected projectile would have to fly through.
        ///
        /// Sampled rather than walked: a coarse march down the line is enough to notice a
        /// protected pawn standing in the way, and it keeps the cost of scoring sixteen bearings
        /// bounded no matter how far the deflection throws the object.
        /// </summary>
        private static float PathScore(IntVec3 from, IntVec3 to, Map map, Pawn guardian)
        {
            float dx = to.x - from.x, dz = to.z - from.z;
            float length = Mathf.Sqrt(dx * dx + dz * dz);
            if (length <= PathSampleStart) return 0f;

            int samples = PathSamples;
            float penalty = 0f;
            IntVec3 previous = IntVec3.Invalid;

            for (int i = 1; i <= samples; i++)
            {
                float t = PathSampleStart / length + (1f - PathSampleStart / length) * i / samples;
                IntVec3 cell = new IntVec3(
                    from.x + Mathf.RoundToInt(dx * t), from.y, from.z + Mathf.RoundToInt(dz * t));

                if (cell == previous) continue;      // short paths resample the same cell
                previous = cell;
                if (!cell.InBounds(map)) continue;

                List<Thing> things = map.thingGrid.ThingsListAtFast(cell);
                if (things == null) continue;

                for (int j = 0; j < things.Count; j++)
                {
                    Pawn pawn = things[j] as Pawn;
                    if (pawn == null || pawn.Dead || pawn == guardian) continue;
                    if (Gm21GuardianThreat.Protects(guardian, pawn)) penalty -= PathPenalty;
                }
            }

            return penalty;
        }
    }
}
