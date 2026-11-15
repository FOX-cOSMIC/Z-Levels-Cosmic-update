using HarmonyLib;
using Verse;

namespace ZLevels
{
    // A removed map takes its claims with it (idea "someone is already going"). The game removes a map through Game.DeinitAndRemoveMap.
    [HarmonyPatch(typeof(Game), "DeinitAndRemoveMap")]
    public static class ClaimMapRemovalPatch
    {
        private static void Prefix(Map map)
        {
            ZLevelsManager tracker = ZUtils.ZTracker;
            tracker?.ReleaseClaimsOnMap(map);
        }
    }
}
