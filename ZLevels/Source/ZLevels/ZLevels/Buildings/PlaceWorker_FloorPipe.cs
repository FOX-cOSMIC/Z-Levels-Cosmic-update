using System.Linq;
using RimWorld;
using Verse;

namespace ZLevels
{
    // The placement check for a floor pipe (idea "pipes between floors", decision DM17). A pipe is built on a floor that has a floor above it, and its
    // other end goes on the same cell there, so that cell must be standable and must hold no building.
    public class PlaceWorker_FloorPipe : PlaceWorker
    {
        public const string NoFloorAboveMessage = "There is no floor above this one, so the pipe has no other end.";
        public const string BlockedAboveMessage = "The cell above is blocked, so the pipe has no other end there.";

        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            Map above = ZUtils.ZTracker.GetUpperLevel(map.Tile, map);
            if (above == null || above == map)
            {
                return NoFloorAboveMessage;
            }
            if (!loc.InBounds(above) || !loc.Standable(above) || loc.GetThingList(above).Any(other => other.def.category == ThingCategory.Building))
            {
                return BlockedAboveMessage;
            }
            return true;
        }
    }
}
