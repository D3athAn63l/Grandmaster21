using System;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// "Could this pawn physically get there?" -- the one reachability question both Guardian
    /// systems ask, in one place so they cannot drift apart.
    ///
    /// WHY IT IS NOT FULL PATHFINDING. Ally interception runs on melee attacks and projectile
    /// interception runs on threatened shots; a region-based path request for each would be far
    /// more work than the question needs. What actually has to be ruled out is the absurd case:
    /// a Grandmaster "protecting" someone through a sealed granite wall. A straight line that is
    /// walkable the whole way rules that out, costs a handful of cell reads, and never returns a
    /// false positive through solid rock -- the only direction of error that matters here.
    ///
    /// It can return a false NEGATIVE: two cells connected only by a dog-leg around a corner read
    /// as unreachable. Inside a three-tile radius that is a rare and conservative failure -- the
    /// Guardian simply does not intervene -- which is the right way round.
    /// </summary>
    internal static class Gm21Reach
    {
        // Allocated once per thread rather than once per call: both callers run inside combat
        // resolution, and a closure per projectile or per melee swing would be pure garbage.
        [ThreadStatic] private static Map validatorMap;
        [ThreadStatic] private static Func<IntVec3, bool> walkableValidator;

        private static Func<IntVec3, bool> WalkableIn(Map map)
        {
            validatorMap = map;
            if (walkableValidator == null)
            {
                walkableValidator = c => c.Walkable(validatorMap);
            }
            return walkableValidator;
        }

        /// <summary>
        /// True when <paramref name="from"/> can reach <paramref name="to"/> without crossing a
        /// wall or impassable terrain.
        ///
        /// skipFirstCell is true because the starting cell is the pawn's own, and a pawn standing
        /// in a doorway or on a non-walkable cell it is legitimately occupying must not fail its
        /// own reachability test.
        /// </summary>
        internal static bool CanDashTo(Map map, IntVec3 from, IntVec3 to)
        {
            if (map == null) return false;
            if (from == to) return true;
            return GenSight.LineOfSight(from, to, map, true, WalkableIn(map), 0, 0);
        }
    }
}
