using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// Where to put an ally's live explosive once the Grandmaster has hold of it.
    ///
    /// THE PRIORITY IS NOT REVENGE. A friendly warhead that threatens protected pawns is never
    /// sent back at the ally who launched it, whatever they meant by it. It goes at the enemy if
    /// an enemy is reachable, and somewhere empty if not. That is the whole decision.
    ///
    /// WHY THIS IS NOT THE DIRECT-FIRE RECOVERY RULE. An ally's stray BULLET is salvaged toward
    /// the specific enemy it was aimed at, deliberately, so that a Grandmaster cannot be used as
    /// free aim correction for a pawn who cannot shoot. An explosive has no such shot to
    /// reconstruct -- it is an area weapon, and reconstructing "the original trajectory" of one is
    /// meaningless. The Grandmaster simply picks the best place for it to go off, which is what a
    /// person holding a live grenade would actually do.
    ///
    /// BOUNDED, NOT OPTIMAL. Candidate destinations are the hostiles already standing on the map,
    /// capped, and each is scored against two small pawn lists gathered in a single pass. There is
    /// no cell-by-cell search of the map and no global optimisation: it is a few hundred distance
    /// comparisons, and it only runs on a successful interception of a friendly explosive.
    /// </summary>
    internal static class Gm21ExplosiveDisposal
    {
        /// <summary>
        /// How many hostiles are considered as destinations. A cap rather than a sort, because
        /// the point is to find a good throw quickly, not the provably best one.
        /// </summary>
        private const int MaxHostileCandidates = 24;

        /// <summary>Score for each additional hostile caught inside the blast.</summary>
        private const float HostileInBlast = 30f;

        /// <summary>
        /// Score lost per protected pawn inside the blast. An order of magnitude above the hostile
        /// bonus, so no number of raiders ever justifies catching one of our own: three raiders
        /// (+90) cannot outweigh a single colonist (-400).
        /// </summary>
        private const float ProtectedInBlast = 400f;

        /// <summary>Score lost per protected pawn standing on the redirected flight path.</summary>
        private const float ProtectedOnPath = 120f;

        /// <summary>
        /// Margin added to the blast radius when testing our own people. A warhead that lands
        /// exactly on the edge of a colonist's tile is not "clear"; the extra tile buys the
        /// uncertainty in where a pawn actually is when it goes off.
        /// </summary>
        private const float ProtectedSafetyMargin = 1.5f;

        /// <summary>Samples taken along a candidate trajectory when checking what it flies over.</summary>
        private const int PathSamples = 8;

        /// <summary>
        /// Below this score a destination is not worth throwing at, and the explosive goes to safe
        /// disposal instead. Zero rather than negative: a throw that helps nobody and endangers
        /// nobody is still not a reason to aim at a raider we cannot actually reach usefully.
        /// </summary>
        private const float MinAcceptableScore = 1f;

        // Reused across calls on the main thread; this only ever runs from projectile resolution.
        [System.ThreadStatic] private static List<Pawn> hostileBuffer;
        [System.ThreadStatic] private static List<Pawn> protectedBuffer;

        /// <summary>
        /// The best hostile destination for this explosive, or an invalid cell when there is none
        /// worth using -- in which case the caller falls through to safe disposal.
        ///
        /// <paramref name="maxDistance"/> is how far this Grandmaster can actually send the thing,
        /// which is already strength-scaled for a thrown object and fixed for a rocket. A raider
        /// camp beyond that range is simply not a throw an ordinary colonist can make.
        /// </summary>
        internal static IntVec3 ChooseHostileDestination(Pawn guardian, Map map, IntVec3 from,
                                                         float blastRadius, float maxDistance)
        {
            if (guardian == null || map == null || map.mapPawns == null) return IntVec3.Invalid;

            List<Pawn> hostiles = hostileBuffer;
            if (hostiles == null) hostiles = hostileBuffer = new List<Pawn>(16);
            List<Pawn> friends = protectedBuffer;
            if (friends == null) friends = protectedBuffer = new List<Pawn>(16);
            hostiles.Clear();
            friends.Clear();

            Partition(guardian, map, from, maxDistance, hostiles, friends);
            if (hostiles.Count == 0)
            {
                friends.Clear();
                return IntVec3.Invalid;
            }

            float protectedRadius = blastRadius + ProtectedSafetyMargin;
            IntVec3 best = IntVec3.Invalid;
            float bestScore = MinAcceptableScore;

            for (int i = 0; i < hostiles.Count; i++)
            {
                IntVec3 candidate = hostiles[i].Position;

                // A projectile does not go through walls any more than a Grandmaster does.
                if (!GenSight.LineOfSight(from, candidate, map)) continue;

                float score = ScoreDestination(candidate, from, map, guardian,
                                               hostiles, friends, blastRadius, protectedRadius);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            hostiles.Clear();
            friends.Clear();
            return best;
        }

        /// <summary>
        /// One pass over the map's spawned pawns, splitting them into "worth hitting" and "must
        /// not be hit".
        ///
        /// Iterating the pawn list rather than the cell grid is what keeps this cheap: the cost is
        /// proportional to how many pawns exist, not to how much ground the explosive could cover,
        /// and a map has tens of pawns and tens of thousands of cells.
        ///
        /// Protected pawns are collected without a distance filter -- a colonist just outside the
        /// throw range can still be inside the blast of something thrown to the edge of it.
        /// </summary>
        private static void Partition(Pawn guardian, Map map, IntVec3 from, float maxDistance,
                                      List<Pawn> hostiles, List<Pawn> friends)
        {
            IReadOnlyList<Pawn> all = map.mapPawns.AllPawnsSpawned;
            if (all == null) return;

            float maxSquared = maxDistance * maxDistance;

            for (int i = 0; i < all.Count; i++)
            {
                Pawn pawn = all[i];
                if (pawn == null || pawn.Dead || !pawn.Spawned) continue;

                if (Gm21GuardianThreat.Protects(guardian, pawn))
                {
                    friends.Add(pawn);
                    continue;
                }

                if (hostiles.Count >= MaxHostileCandidates) continue;
                if (!GenHostility.HostileTo(pawn, guardian)) continue;

                float dx = pawn.Position.x - from.x, dz = pawn.Position.z - from.z;
                if (dx * dx + dz * dz > maxSquared) continue;

                hostiles.Add(pawn);
            }
        }

        /// <summary>
        /// How good a place this is to put a live warhead.
        ///
        /// Hostiles caught in the blast score, so a cluster of three raiders naturally beats an
        /// isolated one without any special "find the cluster" pass -- the scoring IS the cluster
        /// search. Anyone of ours inside the blast, or standing on the way there, costs far more
        /// than any number of raiders can earn back, so a tempting group with a colonist beside it
        /// is never chosen.
        /// </summary>
        private static float ScoreDestination(IntVec3 cell, IntVec3 from, Map map, Pawn guardian,
                                              List<Pawn> hostiles, List<Pawn> friends,
                                              float blastRadius, float protectedRadius)
        {
            float score = 0f;
            float blastSquared = blastRadius * blastRadius;
            float protectedSquared = protectedRadius * protectedRadius;

            for (int i = 0; i < hostiles.Count; i++)
            {
                if (WithinSquared(hostiles[i].Position, cell, blastSquared)) score += HostileInBlast;
            }

            for (int i = 0; i < friends.Count; i++)
            {
                if (WithinSquared(friends[i].Position, cell, protectedSquared)) score -= ProtectedInBlast;
            }

            score += PathPenalty(from, cell, friends);
            return score;
        }

        private static bool WithinSquared(IntVec3 a, IntVec3 b, float radiusSquared)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return dx * dx + dz * dz <= radiusSquared;
        }

        /// <summary>
        /// What the explosive would have to fly over. Sampled rather than walked, for the same
        /// reason the safe-vector chooser samples: a coarse march cannot miss a pawn-sized
        /// obstruction, and it keeps the cost of scoring two dozen destinations bounded.
        /// </summary>
        private static float PathPenalty(IntVec3 from, IntVec3 to, List<Pawn> friends)
        {
            if (friends.Count == 0) return 0f;

            float dx = to.x - from.x, dz = to.z - from.z;
            float penalty = 0f;

            for (int s = 1; s < PathSamples; s++)
            {
                float t = (float)s / PathSamples;
                IntVec3 point = new IntVec3(from.x + Mathf.RoundToInt(dx * t), from.y,
                                            from.z + Mathf.RoundToInt(dz * t));

                for (int i = 0; i < friends.Count; i++)
                {
                    if (friends[i].Position == point) { penalty -= ProtectedOnPath; break; }
                }
            }

            return penalty;
        }
    }
}
