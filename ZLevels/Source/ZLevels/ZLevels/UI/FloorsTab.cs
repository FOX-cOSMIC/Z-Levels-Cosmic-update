using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ZLevels
{
    // The Floors tab (floor settings, Task 29). Each row is a free colonist on the colony's floors. The first column names the floor the
    // colonist stands on. The other columns name the floor each need's search uses. A click on one of them opens the floor menu, where
    // "Any" sets the need back to every floor.
    public class FloorsTab : MainTabWindow
    {
        private const float HeaderHeight = 30f;
        private const float RowHeight = 30f;
        private const float NameWidth = 200f;
        private const float ColumnWidth = 150f;

        // The needs with a floor setting, in column order.
        public static readonly NeedKind[] Needs = { NeedKind.Sleep, NeedKind.Food, NeedKind.Fun, NeedKind.Meditation };

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

        // Column 0 is the colonist, column 1 the current floor, and columns 2 to 5 the needs in the order of Needs.
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
            List<Map> floors = FloorsOfColony();
            List<Pawn> colonists = ColonistsOnFloors(floors);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;

            Rect header = new Rect(inRect.x, inRect.y, inRect.width, HeaderHeight);
            string[] names = { "Colonist", "Current floor", "Sleep floor", "Food floor", "Fun floor", "Meditation floor" };
            for (int column = 0; column < names.Length; column++)
            {
                Widgets.Label(ColumnRect(header, column), names[column]);
            }

            float y = inRect.y + HeaderHeight;
            foreach (Pawn colonist in colonists)
            {
                Rect row = new Rect(inRect.x, y, inRect.width, RowHeight);
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

            Text.Anchor = TextAnchor.UpperLeft;
        }
    }
}
