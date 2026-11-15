using HarmonyLib;
using RimWorld;
using Verse;

namespace ZLevels
{
    // Task 20 (idea "lighter update on extra floors" on docs/mod-comparison/ideas.html). A floor above or below the surface skips four steps of
    // the map tick: the wind update, wild animal spawning, the choice of the next weather, and passing ships. The surface runs all four. Each
    // prefix skips only its own method, and only on a map whose Z index is not 0. Map.MapPreTick calls WindManagerTick. Map.MapPostTick calls
    // WildAnimalSpawnerTick, WeatherDeciderTick and PassingShipManagerTick.
    public static class ExtraFloorTickPatches
    {
        // True for a map that is not the surface. A pocket map is not a level, and GetZIndexFor returns 0 for it, so it keeps its ticks.
        internal static bool IsExtraFloor(Map map)
        {
            return map != null && ZUtils.ZTracker != null && ZUtils.ZTracker.GetZIndexFor(map) != 0;
        }

        [HarmonyPatch(typeof(WindManager), nameof(WindManager.WindManagerTick))]
        internal static class WindTickPatch
        {
            private static bool Prefix(Map ___map)
            {
                return !IsExtraFloor(___map);
            }
        }

        [HarmonyPatch(typeof(WildAnimalSpawner), nameof(WildAnimalSpawner.WildAnimalSpawnerTick))]
        internal static class WildAnimalSpawnTickPatch
        {
            private static bool Prefix(Map ___map)
            {
                return !IsExtraFloor(___map);
            }
        }

        [HarmonyPatch(typeof(WeatherDecider), nameof(WeatherDecider.WeatherDeciderTick))]
        internal static class WeatherChoiceTickPatch
        {
            private static bool Prefix(Map ___map)
            {
                return !IsExtraFloor(___map);
            }
        }

        [HarmonyPatch(typeof(PassingShipManager), nameof(PassingShipManager.PassingShipManagerTick))]
        internal static class PassingShipTickPatch
        {
            private static bool Prefix(Map ___map)
            {
                return !IsExtraFloor(___map);
            }
        }
    }
}
