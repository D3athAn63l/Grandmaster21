using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// THE ONE RULE THE GUARDIAN DOES NOT TRADE AWAY: do not knowingly put a protected pawn in the
    /// path or the blast of something the Grandmaster chose to redirect.
    ///
    /// WHY THIS IS A SEPARATE FILE, AND WHY IT RETURNS bool. It used to be a score. Protected pawns
    /// in the blast subtracted 400, hostiles in the blast added 30, and the two were compared --
    /// which meant fourteen raiders (+420) outvoted one colonist (-400) and the Guardian would
    /// cheerfully work out that Alice was worth it. That is a weighted-utility answer to a
    /// question that is not economic. No number of raiders buys a colonist.
    ///
    /// The fix is not a bigger negative number. A bigger negative number is the same bug with a
    /// larger threshold, and float.MinValue is the same bug plus NaN risk. Safety is now a
    /// PREDICATE evaluated BEFORE scoring: a candidate that endangers one of ours is not a bad
    /// candidate, it is not a candidate. Scoring then happens only among the survivors, so the
    /// clever offensive behaviour is kept and simply cannot reach past the boundary.
    ///
    /// Lexicographic, not weighted:
    ///     1. do not endanger our own people
    ///     2. among the choices that pass, do the most good
    ///
    /// Every Guardian redirect path shares this file so the definition of "protected" cannot drift
    /// between threat detection, explosive disposal and safe deflection. It is the same
    /// Gm21GuardianThreat.Protects rule those already use -- the Grandmaster themselves, their
    /// faction, and formal allies.
    /// </summary>
    internal static class Gm21ProtectedSafety
    {
        /// <summary>
        /// Extra tiles added to an explosion's nominal radius before asking whether one of ours is
        /// inside it.
        ///
        /// A pawn standing exactly on the edge of a blast is not "clear": they may step, the
        /// explosion resolves over cells rather than points, and the Guardian is choosing this
        /// outcome deliberately rather than having it happen to them. One and a half tiles buys
        /// that uncertainty without making redirection unusable -- it is a buffer, not a
        /// second blast radius.
        /// </summary>
        internal const float BlastSafetyMargin = 1.5f;

        /// <summary>
        /// Danger zone for a projectile landing at a cell. An explosive's is its blast plus the
        /// margin; an inert projectile's is the margin alone, which is what stops a deflected
        /// bullet being aimed into a colonist's tile.
        /// </summary>
        internal static float DangerRadius(float explosionRadius)
        {
            if (explosionRadius < 0f) explosionRadius = 0f;
            return explosionRadius + BlastSafetyMargin;
        }

        /// <summary>
        /// Every pawn this Guardian must not knowingly hit, in one pass over the map's spawned
        /// pawns.
        ///
        /// Deliberately NOT distance-filtered. A colonist standing outside the throw range can
        /// still be inside the blast of something thrown to the edge of it, and "far away" is not
        /// the same as "safe".
        ///
        /// The Grandmaster is included, because Protects answers true for the pawn asking. A
        /// Guardian does not redirect a warhead onto their own cell either.
        /// </summary>
        internal static void GatherProtected(Pawn guardian, Map map, List<Pawn> into)
        {
            if (guardian == null || map == null || map.mapPawns == null || into == null) return;

            IReadOnlyList<Pawn> all = map.mapPawns.AllPawnsSpawned;
            if (all == null) return;

            for (int i = 0; i < all.Count; i++)
            {
                Pawn pawn = all[i];
                if (pawn == null || pawn.Dead || !pawn.Spawned) continue;
                if (Gm21GuardianThreat.Protects(guardian, pawn)) into.Add(pawn);
            }
        }

        /// <summary>
        /// HARD VETO 1 -- would anything landing here catch one of ours?
        ///
        /// Also covers the brief's third veto without needing a third test: the Grandmaster and
        /// the pawn they just rescued are both in the protected list, so a destination that would
        /// put either of them inside the blast is rejected by exactly the same check.
        /// </summary>
        internal static bool BlastEndangersProtected(IntVec3 cell, float dangerRadius,
                                                     List<Pawn> protectedPawns)
        {
            if (protectedPawns == null || protectedPawns.Count == 0) return false;

            float radiusSquared = dangerRadius * dangerRadius;
            for (int i = 0; i < protectedPawns.Count; i++)
            {
                IntVec3 p = protectedPawns[i].Position;
                float dx = p.x - cell.x, dz = p.z - cell.z;
                if (dx * dx + dz * dz <= radiusSquared) return true;
            }
            return false;
        }

        /// <summary>
        /// Can this projectile actually collide with something standing in its way?
        ///
        /// A thrown grenade arcs OVER the pawns between thrower and landing point -- that is what
        /// arcHeightFactor means, and RimWorld's own mid-flight interception respects it. A bullet
        /// or a rocket does not arc, and will hit whoever is standing in the line. An overhead
        /// shell is not in the line at all.
        ///
        /// The path veto is therefore only asked of direct-flight projectiles. Applying it to an
        /// arcing grenade would veto every bearing whenever an ally stood adjacent to the
        /// Grandmaster, on the strength of a collision that physically cannot happen -- and the
        /// Guardian would be paralysed exactly when it is most needed. A projectile whose def does
        /// not declare an arc is treated as direct, which is the conservative direction.
        /// </summary>
        internal static bool IsDirectFlight(ProjectileProperties props)
        {
            if (props == null) return true;
            return !props.flyOverhead && props.arcHeightFactor <= 0f;
        }

        /// <summary>
        /// HARD VETO 2 -- would it have to fly THROUGH one of ours to get there?
        ///
        /// EXACT, not sampled. This walks the same Bresenham cell line RimWorld uses for its own
        /// line questions, so a protected pawn standing anywhere on the trajectory is found rather
        /// than possibly stepped over between samples. That precision matters most for exactly the
        /// cases the brief calls out -- bullets, rockets and launched explosives travel far, and a
        /// coarse sample down a forty-tile line can miss a single pawn standing in it.
        ///
        /// The starting cell is skipped: that is where the Grandmaster is standing, and they are
        /// themselves a protected pawn, so counting it would veto every bearing.
        ///
        /// GenSight.BresenhamCellsBetween hands back a list RimWorld reuses between calls, so the
        /// result is consumed immediately here and never held.
        ///
        /// Only asked of direct-flight projectiles -- see IsDirectFlight.
        /// </summary>
        internal static bool PathEndangersProtected(IntVec3 from, IntVec3 to, List<Pawn> protectedPawns,
                                                    bool directFlight)
        {
            if (!directFlight) return false;
            if (protectedPawns == null || protectedPawns.Count == 0) return false;
            if (from == to) return false;

            List<IntVec3> line = GenSight.BresenhamCellsBetween(from, to);
            if (line == null) return false;

            for (int c = 0; c < line.Count; c++)
            {
                IntVec3 cell = line[c];
                if (cell == from) continue;

                for (int i = 0; i < protectedPawns.Count; i++)
                {
                    if (protectedPawns[i].Position == cell) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Both vetoes together -- the single question every redirect candidate has to pass.
        /// </summary>
        internal static bool EndangersProtected(IntVec3 from, IntVec3 to, float dangerRadius,
                                                List<Pawn> protectedPawns, bool directFlight)
        {
            return BlastEndangersProtected(to, dangerRadius, protectedPawns)
                || PathEndangersProtected(from, to, protectedPawns, directFlight);
        }

        /// <summary>
        /// How badly a candidate that ALREADY FAILED the veto fails it, for the one place that
        /// needs to pick a least-bad option: safe disposal, where declining to choose means the
        /// projectile carries on into the people it was already going to hit.
        ///
        /// This is damage minimisation with no offensive component -- hostiles are not counted
        /// here at all, so it can never become "kill one of ours to kill more of theirs". It is
        /// only ever consulted when every single bearing is unsafe.
        /// </summary>
        internal static float DangerScore(IntVec3 from, IntVec3 to, float dangerRadius,
                                          List<Pawn> protectedPawns, bool directFlight)
        {
            if (protectedPawns == null || protectedPawns.Count == 0) return 0f;

            float danger = 0f;
            float radiusSquared = dangerRadius * dangerRadius;

            for (int i = 0; i < protectedPawns.Count; i++)
            {
                IntVec3 p = protectedPawns[i].Position;
                float dx = p.x - to.x, dz = p.z - to.z;
                float distSquared = dx * dx + dz * dz;
                if (distSquared > radiusSquared) continue;

                // Closer to the centre of the blast is worse, so a near-miss outranks a direct hit.
                danger += 1f + (radiusSquared - distSquared) / Mathf.Max(1f, radiusSquared);
            }

            if (PathEndangersProtected(from, to, protectedPawns, directFlight)) danger += 1f;
            return danger;
        }
    }
}
