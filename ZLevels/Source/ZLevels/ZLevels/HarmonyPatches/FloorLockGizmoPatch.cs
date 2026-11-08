using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZLevels
{
    // The colonist's "Stay on this floor" toggle. Turning it on locks the colonist to the floor it stands on, and turning it off clears the
    // lock. The toggle is shown for player colonists only.
    [HarmonyPatch(typeof(Pawn), "GetGizmos")]
    public static class FloorLockGizmoPatch
    {
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (__instance == null || !__instance.IsColonistPlayerControlled || __instance.Map == null)
            {
                return;
            }
            var toggle = new Command_Toggle
            {
                defaultLabel = "Stay on this floor",
                defaultDesc = "Keeps this colonist's jobs on the floor it stands on now. Turn it off to let the colonist work on other floors.",
                // Borrowed from vanilla. Z-Levels has no icon of its own for this toggle yet.
                icon = TexCommand.HoldOpen,
                isActive = () => ZUtils.ZTracker.floorPolicy.HasLock(__instance),
                toggleAction = () =>
                {
                    FloorPolicy policy = ZUtils.ZTracker.floorPolicy;
                    if (policy.HasLock(__instance))
                    {
                        policy.ClearLock(__instance);
                    }
                    else
                    {
                        policy.SetLock(__instance, ZUtils.ZTracker.GetZIndexFor(__instance.Map), int.MaxValue);
                    }
                },
            };
            __result = __result.Concat(new Gizmo[] { toggle });
        }
    }

    // A dead colonist keeps no floor lock, so a lock cannot come back for it after a load.
    [HarmonyPatch(typeof(Pawn), "Kill")]
    public static class FloorLockDeathPatch
    {
        public static void Postfix(Pawn __instance)
        {
            ZLevelsManager tracker = ZUtils.ZTracker;
            if (tracker != null)
            {
                tracker.floorPolicy.ClearLock(__instance);
            }
        }
    }
}
