using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZLevels
{
    // Work table floor link (idea "workbench floor link" on docs/mod-comparison/ideas.html). A player's work table shows a "Work on floor" button. The
    // button opens a menu of floors. The chosen floor is tried first by the ingredient search of the table's bills, and "Any floor" removes the link.
    // The button is added to ThingWithComps.GetGizmos, the method that declares GetGizmos in this build. Building_WorkTable does not declare it, so a
    // patch on the table type would not resolve.
    [HarmonyPatch(typeof(ThingWithComps), "GetGizmos")]
    public static class WorkTableFloorGizmoPatch
    {
        private static void Postfix(ThingWithComps __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!(__instance is Building_WorkTable) || __instance.Faction != Faction.OfPlayer)
            {
                return;
            }
            __result = __result.Concat(new Gizmo[] { FloorLinkButton(__instance) });
        }

        private static Gizmo FloorLinkButton(Thing bench)
        {
            return new Command_Action
            {
                defaultLabel = "Work on floor",
                defaultDesc = "Choose the floor that this table's bills try first when they look for ingredients. Any floor removes the choice.",
                icon = TexCommand.Install,
                action = () => Find.WindowStack.Add(new FloatMenu(FloorOptions(bench)))
            };
        }

        private static List<FloatMenuOption> FloorOptions(Thing bench)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption("Any floor", () => ZUtils.ZTracker.floorPolicy.SetBenchFloor(bench, null))
            };
            foreach (Map floor in ZUtils.ZTracker.GetAllMaps(bench.Map.Tile))
            {
                int zIndex = ZUtils.ZTracker.GetZIndexFor(floor);
                options.Add(new FloatMenuOption(FloorsTab.FloorName(zIndex), () => ZUtils.ZTracker.floorPolicy.SetBenchFloor(bench, zIndex)));
            }
            return options;
        }
    }
}
