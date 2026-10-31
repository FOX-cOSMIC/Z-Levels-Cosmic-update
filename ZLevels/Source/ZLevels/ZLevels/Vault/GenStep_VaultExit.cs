using System;
using Verse;

namespace ZLevels
{
    // Puts the exit of the entrance that is generating this pocket. Vanilla caves spawn CaveExit and ignore the entrance's exitDef, so the vault needs its own step.
    public class GenStep_VaultExit : GenStep
    {
        public override int SeedPart => 7731042;

        public override void Generate(Map map, GenStepParams parms)
        {
            Building_VaultEntrance entrance = PocketMapUtility.currentlyGeneratingPortal as Building_VaultEntrance;
            if (entrance == null)
            {
                throw new InvalidOperationException("The vault pocket generator ran without a vault entrance.");
            }
            entrance.SpawnExitIn(map);
        }
    }
}
