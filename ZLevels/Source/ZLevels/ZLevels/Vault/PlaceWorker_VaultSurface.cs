using RimWorld;
using Verse;

namespace ZLevels
{
    // Vault entrances go on the surface only. Pocket maps and every level below or above the surface refuse them.
    public class PlaceWorker_VaultSurface : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            if (map.IsPocketMap || ZUtils.ZTracker.GetZIndexFor(map) != 0)
            {
                return new AcceptanceReport("Vault entrances can only be built on the surface.");
            }
            return AcceptanceReport.WasAccepted;
        }
    }
}
