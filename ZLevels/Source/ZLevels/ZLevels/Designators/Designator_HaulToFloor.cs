using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ZLevels
{
    // Haul to a floor (idea "haul to a floor" on docs/mod-comparison/ideas.html). The player clicks or drags over loose items, then picks a floor
    // from the menu. Each item that the chosen floor needs is marked for it with ItemFloorMarks, so a colonist on that floor hauls it into storage
    // there. Items already in storage are not offered, since the background scan drops the marks of stored items.
    public class Designator_HaulToFloor : Designator
    {
        private const string NoItemReason = "No loose item is here to haul to another floor.";

        public Designator_HaulToFloor()
        {
            defaultLabel = "Haul to floor";
            defaultDesc = "Click or drag over loose items, then choose the floor whose colonists should haul them into storage there.";
            icon = ContentFinder<Texture2D>.Get("UI/Designators/Haul");
        }

        public override AcceptanceReport CanDesignateCell(IntVec3 loc)
        {
            Map map = Find.CurrentMap;
            if (map == null || !loc.InBounds(map) || !loc.GetThingList(map).Any(IsLooseItem))
            {
                return new AcceptanceReport(NoItemReason);
            }
            return AcceptanceReport.WasAccepted;
        }

        // The game sends a single click here, and then finishes the designation itself (DesignatorManager.cs, line 101).
        public override void DesignateSingleCell(IntVec3 cell)
        {
            OfferFloors(new List<IntVec3> { cell });
        }

        // The game sends a drag here, and does not finish the designation itself (DesignatorManager.cs, line 126). One menu covers every dragged cell.
        public override void DesignateMultiCell(IEnumerable<IntVec3> cells)
        {
            Finalize(OfferFloors(cells.ToList()));
        }

        // Opens the floor menu for the loose items under the cells. Returns false when there are no such items, or no other floor to offer.
        private static bool OfferFloors(List<IntVec3> cells)
        {
            Map map = Find.CurrentMap;
            List<Thing> items = cells.Where(cell => cell.InBounds(map))
                .SelectMany(cell => cell.GetThingList(map))
                .Where(IsLooseItem)
                .Distinct()
                .ToList();
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (Map floor in ZUtils.ZTracker.GetAllMaps(map.Tile))
            {
                if (floor == map)
                {
                    continue;
                }
                int zIndex = ZUtils.ZTracker.GetZIndexFor(floor);
                options.Add(new FloatMenuOption(FloorsTab.FloorName(zIndex), () => ReportMarks(items, zIndex)));
            }
            if (items.Count == 0 || options.Count == 0)
            {
                return false;
            }
            Find.WindowStack.Add(new FloatMenu(options));
            return true;
        }

        // Marks the items for the chosen floor, then tells the player how many the floor needed.
        private static void ReportMarks(List<Thing> items, int floorIndex)
        {
            int marked = MarkItemsForFloor(items, floorIndex);
            Messages.Message(marked + " of " + items.Count + " items marked for " + FloorsTab.FloorName(floorIndex)
                + ". The others are not needed on that floor.", MessageTypeDefOf.NeutralEvent, historical: false);
        }

        // Marks each item that the floor needs, and returns how many were marked. An item the floor does not need stays unmarked, since the next
        // scan would drop its mark. The floor is the map that has the given Z index.
        public static int MarkItemsForFloor(IEnumerable<Thing> items, int floorIndex)
        {
            List<Thing> list = items.ToList();
            if (list.Count == 0)
            {
                return 0;
            }
            Map floor = ZUtils.ZTracker.GetAllMaps(list[0].Map.Tile).FirstOrDefault(map => ZUtils.ZTracker.GetZIndexFor(map) == floorIndex);
            if (floor == null)
            {
                throw new ArgumentException("no floor has the Z index " + floorIndex);
            }
            int marked = 0;
            foreach (Thing item in list)
            {
                if (item.Map == floor || !ItemFloorMarks.FloorNeeds(floor, item))
                {
                    continue;
                }
                ItemFloorMarks.Mark(item, floorIndex);
                marked++;
            }
            return marked;
        }

        // A loose item the player can haul: an item that is spawned and is not in storage yet.
        internal static bool IsLooseItem(Thing thing)
        {
            return thing.Spawned && thing.def.EverHaulable && thing.def.category == ThingCategory.Item && !thing.IsInAnyStorage();
        }
    }
}
