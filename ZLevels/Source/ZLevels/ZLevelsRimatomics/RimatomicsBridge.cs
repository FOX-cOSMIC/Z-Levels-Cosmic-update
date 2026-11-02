using HarmonyLib;
using Verse;

namespace ZLevelsRimatomics
{
    // RimWorld loads this whole assembly with or without Rimatomics, so this class names no Rimatomics type.
    // The Harmony patches in this assembly are applied only when a Rimatomics mod is active.
    [StaticConstructorOnStartup]
    public static class RimatomicsBridge
    {
        public static readonly bool Active;

        static RimatomicsBridge()
        {
            Active = ModsConfig.IsActive("dubwise.rimatomics") || ModsConfig.IsActive("nachotoast.rimatomicsplusplus");
            if (Active)
            {
                new Harmony("zlevels.rimatomics").PatchAll(typeof(RimatomicsBridge).Assembly);
            }
        }
    }
}
