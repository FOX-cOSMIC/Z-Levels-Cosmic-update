using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ZLevels
{
    // The stairs' view button (idea "view the connected floor" on docs/mod-comparison/ideas.html). It moves the camera to the other end of the
    // stairs: the stairs at the same cell on the floor above or below. Building_Stairs.GetGizmos adds it through AddViewButton. It is not a
    // Harmony patch: Harmony could not resolve GetGizmos for Building_Stairs, and that stopped the whole mod's patching at load.
    public static class StairsGizmoPatch
    {
        public static IEnumerable<Gizmo> AddViewButton(Building_Stairs stairs, IEnumerable<Gizmo> gizmos)
        {
            Building_Stairs other = ZUtils.ZTracker.GetOtherEnd(stairs);
            if (other == null)
            {
                return gizmos;
            }
            var view = new Command_Action
            {
                defaultLabel = "View the connected floor",
                defaultDesc = "Moves the camera to the other end of these stairs, on the floor above or below.",
                // Borrowed from vanilla. Z-Levels has no icon of its own for this button yet.
                icon = TexCommand.HoldOpen,
                action = () => ZUtils.ZTracker.DoCameraJumpToThing(other),
            };
            // The transfer button (idea "stair transfer", decision D5). It opens the window that sends checked colonists and loose items to the other end.
            var transfer = new Command_Action
            {
                defaultLabel = "Transfer across the stairs",
                defaultDesc = "Opens a list of the colonists and loose items on this floor. The checked ones are sent to the other floor.",
                icon = TexCommand.Install,
                action = () => StairTransferWindow.Open(stairs),
            };
            return gizmos.Concat(new Gizmo[] { view, transfer });
        }
    }
}
