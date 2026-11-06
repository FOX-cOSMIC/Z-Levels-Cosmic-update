using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZLevels
{
    // The stair a colonist changes floor at: the reachable stair with the shortest walk, not the one nearest in a straight line (idea "saved
    // stair routes" on docs/mod-comparison/ideas.html). A decision is saved per map, start cell and direction. A saved decision is used only
    // while the stair list it was made from is unchanged, and MarkFailed drops it when a walk to its stair fails, so the next decision
    // picks again. Test counter (debug): WalkChecks counts the walk checks a decision runs. The -StairRoutes scenario reads it.
    public static class StairRouteCache
    {
        private sealed class Route
        {
            public List<Building_Stairs> Candidates;
            public Building_Stairs Chosen;
        }

        private static readonly Dictionary<(Map, IntVec3, bool), Route> routes = new Dictionary<(Map, IntVec3, bool), Route>();

        public static int WalkChecks;

        // The stair with the shortest walk from the cell, among the stairs of the map that lead in the given direction. Null when no stair can
        // be walked to.
        public static Building_Stairs Best(Map from, IntVec3 cell, bool goingUp)
        {
            List<Building_Stairs> candidates = goingUp ? ZUtils.ZTracker.GetStairsUpFor(from) : ZUtils.ZTracker.GetStairsDownFor(from);
            var key = (from, cell, goingUp);
            if (routes.TryGetValue(key, out Route saved) && SameStairs(saved.Candidates, candidates))
            {
                return saved.Chosen;
            }
            Building_Stairs chosen = null;
            float shortest = float.MaxValue;
            TraverseParms parms = TraverseParms.For(TraverseMode.PassDoors, Danger.Deadly, false);
            foreach (Building_Stairs stairs in candidates)
            {
                if (stairs.Position == cell)
                {
                    chosen = stairs;
                    break;
                }
                WalkChecks++;
                PawnPath path = from.pathFinder.FindPathNow(cell, new LocalTargetInfo(stairs), parms);
                if (path.Found && path.TotalCost < shortest)
                {
                    shortest = path.TotalCost;
                    chosen = stairs;
                }
                path.ReleaseToPool();
            }
            routes[key] = new Route { Candidates = candidates.ToList(), Chosen = chosen };
            return chosen;
        }

        // Drops the saved decisions that start on the map. Called when stairs are placed on it or on the floor it connects to.
        public static void Invalidate(Map map)
        {
            foreach (var key in routes.Keys.Where(key => key.Item1 == map).ToList())
            {
                routes.Remove(key);
            }
        }

        // Drops the saved decision that chose the stairs, so the next decision on the map walks again.
        public static void MarkFailed(Map map, Building_Stairs stairs)
        {
            foreach (var key in routes.Where(pair => pair.Key.Item1 == map && pair.Value.Chosen == stairs).Select(pair => pair.Key).ToList())
            {
                routes.Remove(key);
            }
        }

        // Called when the zone tracker is reset, so that no saved decision keeps a map that has gone.
        public static void Clear()
        {
            routes.Clear();
        }

        private static bool SameStairs(List<Building_Stairs> saved, List<Building_Stairs> now)
        {
            if (saved.Count != now.Count)
            {
                return false;
            }
            for (int i = 0; i < now.Count; i++)
            {
                if (saved[i] != now[i])
                {
                    return false;
                }
            }
            return true;
        }
    }
}
