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
            // Rimatomics++ works only with Dubs Rimatomics, so the bridge turns on for Dubs alone.
            Active = ModsConfig.IsActive("dubwise.rimatomics");
            if (Active)
            {
                new Harmony("zlevels.rimatomics").PatchAll(typeof(RimatomicsBridge).Assembly);
            }
        }
    }
}
