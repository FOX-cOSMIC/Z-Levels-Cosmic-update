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
            // Each entrance needs its own exit in the shared pocket. Count the exits already there, and every entrance or build that will need one, so a full pocket refuses the next entrance here.
            Map pocket = VaultPocket.Get().Pocket;
            if (VaultExitSpot.ExitsClaimed(pocket, (ThingDef)checkingDef) >= VaultExitSpot.Capacity || (pocket != null && !VaultExitSpot.TryFind(pocket, out IntVec3 _)))
            {
                return new AcceptanceReport("The vault has no room for another entrance.");
            }
            return AcceptanceReport.WasAccepted;
        }
    }
}
