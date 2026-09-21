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
    /// SAFETY IS A VETO, NOT A PENALTY. A destination that would catch one of our own in the
    /// blast, or that the warhead would have to fly through one of them to reach, is rejected
    /// before it is scored at all. It is not a bad option competing with good ones -- it is not an
    /// option. That ordering is the whole correctness argument: hostile value is only ever summed
    /// among destinations that are already safe, so no number of raiders can ever add up to
    /// "Alice was worth it".
    ///
    /// BOUNDED, NOT OPTIMAL. Candidate destinations are the hostiles already standing on the map,
    /// capped, and each is checked against one small protected-pawn list gathered in a single
    /// pass. There is no cell-by-cell search of the map and no global optimisation: it is a few
    /// hundred distance comparisons, and it only runs on a successful interception of a friendly
    /// explosive.
    /// </summary>
    internal static class Gm21ExplosiveDisposal
    {
        /// <summary>
        /// How many hostiles are considered as destinations. A cap rather than a sort, because
        /// the point is to find a good throw quickly, not the provably best one.
        /// </summary>
        private const int MaxHostileCandidates = 24;

        /// <summary>
        /// Score for each hostile caught inside the blast. This is the ONLY term left, because it
        /// is the only thing scoring is still allowed to decide.
        ///
        /// There is deliberately no protected-pawn penalty to balance it against. Protected-pawn
        /// safety is not a term in this sum any more -- it is a veto applied before the sum
        /// exists. See Gm21ProtectedSafety for why a penalty, however large, was the wrong shape.
        /// </summary>
        private const float HostileInBlast = 30f;

        /// <summary>
        /// A destination must catch at least one hostile to be worth throwing a live warhead at.
        /// Below that, safe disposal is the better answer.
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
                                                         ProjectileProperties props, float maxDistance)
        {
            float blastRadius = props == null ? 0f : props.explosionRadius;
            bool directFlight = Gm21ProtectedSafety.IsDirectFlight(props);
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

            float dangerRadius = Gm21ProtectedSafety.DangerRadius(blastRadius);
            IntVec3 best = IntVec3.Invalid;
            float bestScore = MinAcceptableScore;

            for (int i = 0; i < hostiles.Count; i++)
            {
                IntVec3 candidate = hostiles[i].Position;

                // A projectile does not go through walls any more than a Grandmaster does.
                if (!GenSight.LineOfSight(from, candidate, map)) continue;

                // ---- HARD VETOES, BEFORE ANY SCORING ----------------------------------
                //
                // A destination that would catch one of our own in the blast, or that the warhead
                // would have to fly through one of them to reach, is not a low-scoring candidate.
                // It is not a candidate. No quantity of raiders standing on it changes that, which
                // is the entire point of doing this here rather than as a penalty term: there is
                // no arithmetic left for a big enough hostile count to win.
                //
                // Vetoing first is also the cheaper order -- an invalid destination never pays for
                // the hostile count that would have justified it.
                if (Gm21ProtectedSafety.EndangersProtected(from, candidate, dangerRadius, friends,
                                                           directFlight))
                {
                    continue;
                }

                // ---- only now, among destinations that are SAFE, do we optimise --------
                float score = ScoreDestination(candidate, hostiles, blastRadius);
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
        /// How good a SAFE destination is -- how many hostiles the blast catches, and nothing else.
        ///
        /// This only ever sees candidates that have already passed the vetoes, so it does not need
        /// to know protected pawns exist and deliberately has no way to weigh them. Clustering
        /// still needs no special pass: counting hostiles in the blast IS the cluster search, so
        /// three raiders together outscore one isolated raider automatically.
        /// </summary>
        private static float ScoreDestination(IntVec3 cell, List<Pawn> hostiles, float blastRadius)
        {
            float score = 0f;
            float blastSquared = blastRadius * blastRadius;

            for (int i = 0; i < hostiles.Count; i++)
            {
                if (WithinSquared(hostiles[i].Position, cell, blastSquared)) score += HostileInBlast;
            }
            return score;
        }

        private static bool WithinSquared(IntVec3 a, IntVec3 b, float radiusSquared)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return dx * dx + dz * dz <= radiusSquared;
        }

    }
}
