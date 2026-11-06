using RimWorld;
using Verse;

namespace ZLevels
{
    // The placement check for stairs (idea "better stair placement messages" on docs/mod-comparison/ideas.html). Three rules refuse a stairs piece
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

        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            Room room = GridsUtility.GetRoom(loc, map);
            if (room == null || room.CellCount < MinRoomCells)
            {
                return RoomTooSmallMessage;
            }
            if (checkingDef == ZLevelsDefOf.ZL_StairsUp && room.OpenRoofCount >= MaxOpenRoofCells)
            {
                return NoRoofMessage;
            }
            if (checkingDef == ZLevelsDefOf.ZL_StairsDown && ZUtils.ZTracker.GetZIndexFor(map) > 0)
            {
                return AboveGroundMessage;
            }
            return true;
        }
    }
}
