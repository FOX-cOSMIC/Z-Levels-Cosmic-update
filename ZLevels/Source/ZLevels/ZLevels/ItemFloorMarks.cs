using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ZLevels
{
    // Items that another floor needs are marked in the background (idea "items checked in the background"). Every 2000 ticks each floor is scanned, and
    // up to 200 of its items that another floor needs are marked for that floor. A colonist is offered the other floors' items only when they are
    // marked for the colonist's floor, so the haul search does not look at every item of every floor.
    //
    // Another floor needs an item when one of its blueprints or frames still lacks that material, or when one of its stockpiles accepts the item and
    // has an empty cell. A floor that has neither takes no marks, so a full destination stops new marks. Marks are not saved; the next scan rebuilds them.
    public static class ItemFloorMarks
    {
        public const int ScanIntervalTicks = 2000;
        public const int MaxMarksPerScan = 200;

        // The floor index of the destination each marked item is marked for.
        private static readonly Dictionary<Thing, int> markedFor = new Dictionary<Thing, int>();

        public static void Mark(Thing item, int floorIndex)
        {
            markedFor[item] = floorIndex;
        }

        public static bool IsMarkedFor(Thing item, int floorIndex)
        {
            return markedFor.TryGetValue(item, out int mark) && mark == floorIndex;
        }

        public static void Unmark(Thing item)
        {
            markedFor.Remove(item);
        }

        // Scans one floor. Marks that no longer hold are removed first: the item is gone, is stored, or no other floor needs it any more. Then up to
        // MaxMarksPerScan new marks are added for the items of this floor that another floor needs.
        public static void RefreshFloor(Map map)
        {
            List<Destination> destinations = DestinationsFor(map);
            foreach (Thing item in markedFor.Keys.ToList())
            {
                bool gone = item.Destroyed || !item.Spawned || item.IsInAnyStorage();
                if (gone || (item.Map == map && DestinationFor(item, destinations) == null))
                {
                    markedFor.Remove(item);
                }
            }

            int marked = 0;
            foreach (Thing item in map.listerHaulables.ThingsPotentiallyNeedingHauling().ToList())
            {
                if (marked >= MaxMarksPerScan)
                {
                    break;
                }
                if (markedFor.ContainsKey(item))
                {
                    continue;
                }
                Destination destination = DestinationFor(item, destinations);
                if (destination != null)
                {
                    markedFor[item] = destination.FloorIndex;
                    marked++;
                }
            }
        }

        // What one other floor needs, read once per scan: the materials its blueprints and frames still lack, and the stockpile groups that have an
        // empty cell.
        private sealed class Destination
        {
            public int FloorIndex;
            public readonly HashSet<ThingDef> NeededDefs = new HashSet<ThingDef>();
            public readonly List<SlotGroup> GroupsWithRoom = new List<SlotGroup>();
        }

        private static List<Destination> DestinationsFor(Map source)
        {
            List<Destination> destinations = new List<Destination>();
            foreach (Map other in ZUtils.ZTracker.GetAllMaps(source.Tile))
            {
                if (other != source)
                {
                    destinations.Add(DestinationOf(other));
                }
            }
            return destinations;
        }

        // What one floor needs. The background scan and a manual mark read the same answer.
        private static Destination DestinationOf(Map other)
        {
            Destination destination = new Destination { FloorIndex = ZUtils.ZTracker.GetZIndexFor(other) };
            foreach (IConstructible site in other.listerThings.AllThings.OfType<IConstructible>())
            {
                foreach (ThingDefCountClass need in site.TotalMaterialCost())
                {
                    if (site.ThingCountNeeded(need.thingDef) > 0)
                    {
                        destination.NeededDefs.Add(need.thingDef);
                    }
                }
            }
            foreach (SlotGroup group in other.haulDestinationManager.AllGroupsListInPriorityOrder)
            {
                if (group.CellsList.Any(cell => cell.GetFirstItem(other) == null))
                {
                    destination.GroupsWithRoom.Add(group);
                }
            }
            return destination;
        }

        // True when the floor needs the item: one of its blueprints or frames still lacks the item's material, or one of its stockpiles accepts the
        // item and has an empty cell. The designator marks an item only for a floor that needs it.
        public static bool FloorNeeds(Map floor, Thing item)
        {
            Destination destination = DestinationOf(floor);
            return destination.NeededDefs.Contains(item.def) || destination.GroupsWithRoom.Any(group => group.parent.Accepts(item));
        }

        // The first destination that needs the item, or null when no other floor needs it. A floor index is not used for this answer, because the
        // underground floor has the index -1.
        private static Destination DestinationFor(Thing item, List<Destination> destinations)
        {
            foreach (Destination destination in destinations)
            {
                if (destination.NeededDefs.Contains(item.def))
                {
                    return destination;
                }
                foreach (SlotGroup group in destination.GroupsWithRoom)
                {
                    if (group.parent.Accepts(item))
                    {
                        return destination;
                    }
                }
            }
            return null;
        }
    }
}
