using RimWorld;
using Verse;

namespace ZLevels
{
    // The placement check for stairs (idea "better stair placement messages" on docs/mod-comparison/ideas.html). Three rules refuse a stairs piece, and a large way (decision DM16) has two more
    // that cannot work, each with its message: its room is under 25 cells; stairs up have 5 or more open-roof cells in their room, so no roof
    // covers them; stairs down start on a floor above the surface. The rules follow MultiFloors' PlaceWorker_Stairs. Its indoor, basement and
    // one-per-room rules are not used here.
    public class PlaceWorker_Stairs : PlaceWorker
    {
        public const int MinRoomCells = 25;
        public const int MaxOpenRoofCells = 5;
        public const string RoomTooSmallMessage = "This room is under 25 cells. Stairs need a room of at least 25 cells.";
        public const string NoRoofMessage = "No roof to build on. Stairs up need a roof over the room.";
        public const string AboveGroundMessage = "Stairs going down cannot start above ground.";
        public const string OtherFloorMessage = "A large way needs four open cells on the other floor.";
        public const string SameRoomMessage = "A large way needs all four of its cells in one room.";

        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            Room room = GridsUtility.GetRoom(loc, map);
            if (room == null || room.CellCount < MinRoomCells)
            {
                return RoomTooSmallMessage;
            }
            if (WayUpKinds.IsUp(checkingDef as ThingDef) && room.OpenRoofCount >= MaxOpenRoofCells)
            {
                return NoRoofMessage;
            }
            if (WayUpKinds.IsDown(checkingDef as ThingDef) && ZUtils.ZTracker.GetZIndexFor(map) > 0)
            {
                return AboveGroundMessage;
            }
            ThingDef def = checkingDef as ThingDef;
            if (WayUpKinds.IsLarge(def))
            {
                // The four cells must all lie in the room the checks above passed, so no cell of the way sits outside it.
                foreach (IntVec3 cell in GenAdj.OccupiedRect(loc, Rot4.North, def.size).Cells)
                {
                    if (GridsUtility.GetRoom(cell, map) != room)
                    {
                        return SameRoomMessage;
                    }
                }
                // The partner goes on the other floor: above for an up piece, below for a down piece. Its four cells must be open (decision DM16).
                Map partnerMap = WayUpKinds.IsUp(def) ? ZUtils.ZTracker.GetUpperLevel(map.Tile, map) : ZUtils.ZTracker.GetLowerLevel(map.Tile, map);
                if (partnerMap == null || !WayUpKinds.PartnerCellsOpen(def, loc, partnerMap))
                {
                    return OtherFloorMessage;
                }
            }
            return true;
        }
    }
}
