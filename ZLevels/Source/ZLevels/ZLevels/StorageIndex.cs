using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ZLevels
{
    // The haul destinations of every floor of a tile, in one saved list (idea 7 on docs/mod-comparison/ideas.html). The list is built again
    // only when a destination is added, removed or re-prioritised (NoteChanged, called by StorageIndexPatches), when the floors of the tile
    // change, or when the destination count differs. Test counters (debug): Lookups counts the calls to Destinations, and Rebuilds counts
    // how many times the list was built. The -StorageIndex scenario reads them.
    public static class StorageIndex
    {
        private sealed class Entry
        {
            public int Version;
            public List<Map> Maps;
            public int Count;
            public List<IHaulDestination> Destinations;
        }

        private static readonly Dictionary<PlanetTile, Entry> entries = new Dictionary<PlanetTile, Entry>();
        private static int version;

        public static int Lookups;
        public static int Rebuilds;

        // Called by the haul-destination patches each time a destination is added, removed or re-prioritised.
        public static void NoteChanged()
        {
            version++;
        }

        // Called when the zone tracker is reset, so that no saved list keeps a map that has gone.
        public static void Clear()
        {
            entries.Clear();
        }

        // The destinations of every floor of the tile, in the order of the floors (ZLevelsManager.GetAllMaps) and then the priority order of
        // each floor. The list is shared: callers read it and must not change it.
        public static IReadOnlyList<IHaulDestination> Destinations(PlanetTile tile)
        {
            Lookups++;
            List<Map> maps = ZUtils.ZTracker.GetAllMaps(tile);
            if (entries.TryGetValue(tile, out Entry entry) && entry.Version == version && SameMaps(entry.Maps, maps) && entry.Count == CountOf(maps))
            {
                return entry.Destinations;
            }
            Rebuilds++;
            var destinations = new List<IHaulDestination>();
            foreach (Map map in maps)
            {
                destinations.AddRange(map.haulDestinationManager.AllHaulDestinationsListInPriorityOrder);
            }
            entries[tile] = new Entry { Version = version, Maps = maps, Count = destinations.Count, Destinations = destinations };
            return destinations;
        }

        private static bool SameMaps(List<Map> saved, List<Map> maps)
        {
            if (saved.Count != maps.Count)
            {
                return false;
            }
            for (int i = 0; i < maps.Count; i++)
            {
                if (saved[i] != maps[i])
                {
                    return false;
                }
            }
            return true;
        }

        // The number of destinations the floors hold now. A saved list with another count is stale, even when no patch ran.
        private static int CountOf(List<Map> maps)
        {
            int count = 0;
            foreach (Map map in maps)
            {
                count += map.haulDestinationManager.AllHaulDestinationsListInPriorityOrder.Count;
            }
            return count;
        }
    }
}
