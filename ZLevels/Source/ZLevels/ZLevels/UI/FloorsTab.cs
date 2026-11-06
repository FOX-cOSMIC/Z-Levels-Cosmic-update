using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ZLevels
{
    // The Floors tab (floor settings, Tasks 29 and 30). The first table has a row for each free colonist on the colony's floors. Its first column
    // names the floor the colonist stands on. The other columns name the floor each need's search uses. A click on one of them opens the floor
    // menu, where "Any" sets the need back to every floor. The second table has a row for each work type and a box for each floor: a cleared box
    // means the floor does not run that work type's searches. The whole tab scrolls, since the tables can be taller than the window.
    public class FloorsTab : MainTabWindow
    {
        private const float HeaderHeight = 30f;
        private const float RowHeight = 30f;
        private const float NameWidth = 200f;
        private const float ColumnWidth = 150f;
        private const float ScrollbarWidth = 16f;

        // The needs with a floor setting, in column order.
        public static readonly NeedKind[] Needs = { NeedKind.Sleep, NeedKind.Food, NeedKind.Fun, NeedKind.Meditation };

        private Vector2 scrollPosition;

        // The name of a floor from its Z index. Zero is the surface, a negative index a lower floor, and a positive index an upper floor.
        public static string FloorName(int zIndex)
        {
            if (zIndex == 0)
            {
                return "Surface";
            }
            return zIndex < 0 ? "Lower " + -zIndex : "Upper " + zIndex;
        }

        // The floor a colonist stands on, as the first column shows it.
        public static string CurrentFloorText(Pawn pawn)
        {
            return pawn?.Map == null ? "No floor" : FloorName(ZUtils.ZTracker.GetZIndexFor(pawn.Map));
        }

        // The floor a need's search uses, as its column shows it. "Any" when the need searches every floor.
        public static string NeedFloorText(Pawn pawn, NeedKind kind)
        {
            int? floorIndex = ZUtils.ZTracker.floorPolicy.NeedFloorFor(pawn, kind);
            return floorIndex == null ? "Any" : FloorName(floorIndex.Value);
        }

        // The maps of the colony, the lowest floor first.
        private static List<Map> FloorsOfColony()
        {
            Map current = Find.CurrentMap;
            if (current == null)
            {
                return new List<Map>();
            }
            return ZUtils.ZTracker.GetAllMaps(current.Tile).OrderBy(map => ZUtils.ZTracker.GetZIndexFor(map)).ToList();
        }

        private static List<Pawn> ColonistsOnFloors(List<Map> floors)
        {
            List<Pawn> colonists = new List<Pawn>();
            foreach (Map floor in floors)
            {
                colonists.AddRange(floor.mapPawns.FreeColonistsSpawned);
            }
            return colonists;
        }

        // The work types the tab lists: the ones the game shows.
        private static List<WorkTypeDef> WorkTypesShown()
        {
            return DefDatabase<WorkTypeDef>.AllDefsListForReading.Where(workType => workType.visible).ToList();
        }

        // Column 0 is the name, and the columns after it are 150 wide each. The colonist table uses column 1 for the current floor and columns 2
        // to 5 for the needs in the order of Needs. The work table uses column 1 onwards for the floors.
        private static Rect ColumnRect(Rect row, int column)
        {
            float x = column == 0 ? 0f : NameWidth + ColumnWidth * (column - 1);
            float width = column == 0 ? NameWidth : ColumnWidth;
            return new Rect(row.x + x, row.y, width, row.height);
        }

        private static void OpenNeedMenu(Pawn colonist, NeedKind kind, List<Map> floors)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption("Any", () => ZUtils.ZTracker.floorPolicy.ClearNeedFloor(colonist, kind))
            };
            foreach (Map floor in floors)
            {
                int zIndex = ZUtils.ZTracker.GetZIndexFor(floor);
                options.Add(new FloatMenuOption(FloorName(zIndex), () => ZUtils.ZTracker.floorPolicy.SetNeedFloor(colonist, kind, zIndex)));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        public override void DoWindowContents(Rect inRect)
        {
            // The Guide button sits above the table, and the table takes the rest of the window (idea "in-game guide").
            if (Widgets.ButtonText(new Rect(inRect.x, inRect.y, 160f, 30f), "Guide to floors"))
            {
                FloorsGuideWindow.Open();
            }
            inRect = new Rect(inRect.x, inRect.y + 36f, inRect.width, inRect.height - 36f);
            List<Map> floors = FloorsOfColony();
            List<Pawn> colonists = ColonistsOnFloors(floors);
            List<WorkTypeDef> workTypes = WorkTypesShown();
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;

            // The colonist table, then a gap, the work header, and one row per work type.
            float contentHeight = HeaderHeight + RowHeight * colonists.Count + RowHeight + HeaderHeight + RowHeight * workTypes.Count;
            Rect viewRect = new Rect(0f, 0f, inRect.width - ScrollbarWidth, contentHeight);
            Widgets.BeginScrollView(inRect, ref scrollPosition, viewRect);

            Rect header = new Rect(0f, 0f, viewRect.width, HeaderHeight);
            string[] names = { "Colonist", "Current floor", "Sleep floor", "Food floor", "Fun floor", "Meditation floor" };
            for (int column = 0; column < names.Length; column++)
            {
                Widgets.Label(ColumnRect(header, column), names[column]);
            }

            float y = HeaderHeight;
            foreach (Pawn colonist in colonists)
            {
                Rect row = new Rect(0f, y, viewRect.width, RowHeight);
                Widgets.Label(ColumnRect(row, 0), colonist.LabelShort);
                Widgets.Label(ColumnRect(row, 1), CurrentFloorText(colonist));
                for (int i = 0; i < Needs.Length; i++)
                {
                    NeedKind kind = Needs[i];
                    if (Widgets.ButtonText(ColumnRect(row, i + 2), NeedFloorText(colonist, kind)))
                    {
                        OpenNeedMenu(colonist, kind, floors);
                    }
                }
                y += RowHeight;
            }

            y += RowHeight;
            Rect workHeader = new Rect(0f, y, viewRect.width, HeaderHeight);
            Widgets.Label(ColumnRect(workHeader, 0), "Work type");
            for (int i = 0; i < floors.Count; i++)
            {
                Widgets.Label(ColumnRect(workHeader, i + 1), FloorName(ZUtils.ZTracker.GetZIndexFor(floors[i])) + " does it");
            }
            y += HeaderHeight;
            foreach (WorkTypeDef workType in workTypes)
            {
                Rect row = new Rect(0f, y, viewRect.width, RowHeight);
                Widgets.Label(ColumnRect(row, 0), workType.LabelCap);
                for (int i = 0; i < floors.Count; i++)
                {
                    int zIndex = ZUtils.ZTracker.GetZIndexFor(floors[i]);
                    bool allowed = ZUtils.ZTracker.floorPolicy.WorkTypeAllowed(workType, zIndex);
                    bool before = allowed;
                    Widgets.CheckboxLabeled(ColumnRect(row, i + 1), "", ref allowed);
                    if (allowed != before)
                    {
                        ZUtils.ZTracker.floorPolicy.SetWorkAllowed(workType, zIndex, allowed);
                    }
                }
                y += RowHeight;
            }

            Widgets.EndScrollView();
            Text.Anchor = TextAnchor.UpperLeft;
        }
    }
}
