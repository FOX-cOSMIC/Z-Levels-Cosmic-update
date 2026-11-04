using RimWorld;
using Verse;

namespace ZLevels
{
    // The vault entrance. Every entrance shares one pocket map. The first entrance makes it, and each later entrance adds its own exit to it.
    public class Building_VaultEntrance : MapPortal
    {
        public override Map GeneratePocketMapInt()
        {
            VaultPocket vault = VaultPocket.Get();
            Map shared = vault.Pocket;
            if (shared != null)
            {
                SpawnExitIn(shared);
                return shared;
            }
            // The base call can throw while the first exit spawns. The portal saved here is put back either way, so no later exit links to this entrance by mistake.
            MapPortal previous = PocketMapUtility.currentlyGeneratingPortal;
            try
            {
                shared = base.GeneratePocketMapInt();
            }
            finally
            {
                PocketMapUtility.currentlyGeneratingPortal = previous;
            }
            vault.Pocket = shared;
            return shared;
        }

        // The exit links to PocketMapUtility.currentlyGeneratingPortal when it spawns, so this entrance must be that portal while it does.
        public void SpawnExitIn(Map pocket)
        {
            MapPortal previous = PocketMapUtility.currentlyGeneratingPortal;
            PocketMapUtility.currentlyGeneratingPortal = this;
            try
            {
                GenSpawn.Spawn(ThingMaker.MakeThing(def.portal.exitDef), VaultExitSpot.Find(pocket), pocket);
            }
            finally
            {
                PocketMapUtility.currentlyGeneratingPortal = previous;
            }
        }
    }
}
