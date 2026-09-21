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
    /// THE METHOD. Sixteen candidate bearings are tried around the interception point. Each is
    /// first VETOED or not -- would it land on one of our own, or have to fly through one of them
    /// to get there? -- and only the survivors are then scored, by how far they get the object
    /// away from the Grandmaster and the pawn just rescued, and by whether it lands on something
    /// hostile on the way to being harmless.
    ///
    /// SAFETY IS NOT A PENALTY TERM. It used to be: protected pawns subtracted points and hostiles
    /// added them, which meant a large enough pile of raiders could outvote a colonist. That is a
    /// weighted-utility answer to a question that is not economic, and it is gone. See
    /// Gm21ProtectedSafety, which both this and explosive disposal now share so the definition of
    /// "protected" cannot drift between them.
    ///
    /// THE ONE EXCEPTION, AND WHY IT IS NOT THE SAME BUG. If every single bearing is unsafe, this
    /// still returns the least dangerous of them. Declining to choose here does not mean nothing
    /// happens -- it means the projectile carries on to the destination that was already about to
    /// hurt the people the Guardian is protecting. So the fallback is pure damage minimisation:
    /// hostiles are not counted in it at all, and it can never express "hit one of ours to hit
    /// more of theirs". Offensive value never reaches across the veto; only harm reduction does.
    ///
    /// DELIBERATELY LOCAL AND CHEAP. Sixteen bearings against one pawn list gathered in a single
    /// pass, and it runs only at the moment a deflection actually happens -- not per tick, not per
    /// projectile, and never for an ordinary pawn. No pathfinding, no map scan.
    /// </summary>
    internal static class Gm21SafeVector
    {
        /// <summary>Bearings tried, evenly spaced. Sixteen is fine enough to always find a gap.</summary>
        private const int Bearings = 16;

        /// <summary>
        /// Score gained for each hostile pawn in the landing area.
        ///
        /// The only offensive term, and it is only ever summed over bearings that have ALREADY
        /// passed the safety veto. There is no protected-pawn penalty for it to compete with,
        /// because protected pawns are not scored here at all.
        /// </summary>
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

        /// <summary>Minimum area examined around a candidate landing point, for inert projectiles.</summary>
        private const float MinBlastCheckRadius = 1.5f;

        /// <summary>A landing point closer than this to the Grandmaster is never chosen.</summary>
        private const float MinSeparation = 2f;

        // Reused across calls on the main thread; deflection never runs concurrently with itself.
        [System.ThreadStatic] private static List<Pawn> protectedBuffer;
        [System.ThreadStatic] private static List<Pawn> hostileBuffer;

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
            float dangerRadius = Gm21ProtectedSafety.DangerRadius(props.explosionRadius);
            bool directFlight = Gm21ProtectedSafety.IsDirectFlight(props);

            List<Pawn> friends = protectedBuffer;
            if (friends == null) friends = protectedBuffer = new List<Pawn>(16);
            List<Pawn> hostiles = hostileBuffer;
            if (hostiles == null) hostiles = hostileBuffer = new List<Pawn>(16);
            friends.Clear();
            hostiles.Clear();
            Gather(guardian, map, friends, hostiles);

            IntVec3 best = IntVec3.Invalid;
            float bestScore = float.NegativeInfinity;

            // The last-resort tier, used only if every bearing is vetoed. Kept separate from
            // `best` so an unsafe bearing can never win a comparison against a safe one.
            IntVec3 leastBad = IntVec3.Invalid;
            float leastBadDanger = float.PositiveInfinity;

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

                // ---- HARD VETO, BEFORE ANY SCORING --------------------------------------
                if (Gm21ProtectedSafety.EndangersProtected(from, candidate, dangerRadius, friends,
                                                           directFlight))
                {
                    float danger = Gm21ProtectedSafety.DangerScore(from, candidate, dangerRadius,
                                                                   friends, directFlight);
                    if (danger < leastBadDanger)
                    {
                        leastBadDanger = danger;
                        leastBad = candidate;
                    }
                    continue;
                }

                // ---- only safe bearings are scored --------------------------------------
                float score = Score(candidate, guardian, saved, hostiles, checkRadius, separation);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            friends.Clear();
            hostiles.Clear();

            // A safe bearing always wins. The least-bad one is reached only when there was none.
            return best.IsValid ? best : leastBad;
        }

        /// <summary>
        /// One pass over the map's spawned pawns, split by the same protection rule the rest of
        /// the Guardian uses.
        /// </summary>
        private static void Gather(Pawn guardian, Map map, List<Pawn> friends, List<Pawn> hostiles)
        {
            Gm21ProtectedSafety.GatherProtected(guardian, map, friends);

            if (map == null || map.mapPawns == null) return;
            IReadOnlyList<Pawn> all = map.mapPawns.AllPawnsSpawned;
            if (all == null) return;

            for (int i = 0; i < all.Count; i++)
            {
                Pawn pawn = all[i];
                if (pawn == null || pawn.Dead || !pawn.Spawned) continue;
                if (GenHostility.HostileTo(pawn, guardian)) hostiles.Add(pawn);
            }
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
        /// How good a SAFE bearing is: how far it gets the object away from the people who matter,
        /// and whether it happens to land on something hostile.
        ///
        /// This only ever sees bearings that already passed the veto, so it does not need to know
        /// protected pawns exist and deliberately has no way to weigh them.
        /// </summary>
        private static float Score(IntVec3 cell, Pawn guardian, Pawn saved, List<Pawn> hostiles,
                                   float checkRadius, float separation)
        {
            float score = separation * DistanceWeight;

            if (saved != null && saved != guardian && saved.Spawned)
            {
                score += cell.DistanceTo(saved.Position) * SavedDistanceWeight;
            }

            float radiusSquared = checkRadius * checkRadius;
            for (int i = 0; i < hostiles.Count; i++)
            {
                IntVec3 p = hostiles[i].Position;
                float dx = p.x - cell.x, dz = p.z - cell.z;
                if (dx * dx + dz * dz <= radiusSquared) score += HostileBonus;
            }

            return score;
        }
    }
}
