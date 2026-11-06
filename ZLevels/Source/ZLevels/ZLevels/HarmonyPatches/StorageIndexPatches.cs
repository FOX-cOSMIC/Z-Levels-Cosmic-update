using HarmonyLib;
using RimWorld;
using Verse;

namespace ZLevels
{
    // Each change to a map's haul destinations marks the saved list of its tile stale (idea 7 on docs/mod-comparison/ideas.html).
    [HarmonyPatch(typeof(HaulDestinationManager), "AddHaulDestination")]
    public static class StorageIndexAddPatch
    {
        private static void Postfix()
        {
            StorageIndex.NoteChanged();
        }
    }

    [HarmonyPatch(typeof(HaulDestinationManager), "RemoveHaulDestination")]
    public static class StorageIndexRemovePatch
    {
        private static void Postfix()
        {
            StorageIndex.NoteChanged();
        }
    }

    [HarmonyPatch(typeof(HaulDestinationManager), "Notify_HaulDestinationChangedPriority")]
    public static class StorageIndexPriorityPatch
    {
        private static void Postfix()
        {
            StorageIndex.NoteChanged();
        }
    }
}
