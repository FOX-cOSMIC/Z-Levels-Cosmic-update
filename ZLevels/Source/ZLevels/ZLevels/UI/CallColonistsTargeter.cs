using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZLevels
{
    // Idea "call colonists here" (docs/mod-comparison/ideas.html). A player colonist's gizmo list has "Call colonists here". Choosing it starts a
    // targeter on the floor the player is viewing. A standable cell that is picked sends each selected colonist to it. A colonist on another floor
    // takes the stairs by the route a float-menu order builds (ZL_GoToMap first, then the walk). A colonist already on the cell gets no job. A colonist
    // locked to a floor (Stay on this floor) moves its lock to the floor of the cell, and the lock keeps its expiry.
    public static class CallColonistsTargeter
    {
        // Gizmos with the same group key merge, so one button serves a selection of several colonists.
        public const int GroupKey = 6051833;

        // Opens the targeter for the player colonists selected now.
        public static void Begin()
        {
            List<Pawn> colonists = Find.Selector.SelectedObjects.OfType<Pawn>().Where(pawn => pawn.IsColonistPlayerControlled).ToList();
            if (colonists.Count == 0)
            {
                return;
            }
            Map map = Find.CurrentMap;
            Find.Targeter.BeginTargeting(
                new TargetingParameters { canTargetLocations = true },
                target => CallTo(colonists, target.Cell, map),
                highlightAction: null,
                targetValidator: target => target.IsValid && target.Cell.InBounds(map) && target.Cell.Standable(map),
                mouseAttachment: TexCommand.GatherSpotActive);
        }

        // The order itself. The validation scenario reaches it by name, so it is public and static. Returns the colonists it sent. A colonist that
        // is dead, downed or not spawned is left out, and so is a colonist already on the cell.
        public static List<Pawn> CallTo(IEnumerable<Pawn> colonists, IntVec3 cell, Map map)
        {
            ZLevelsManager tracker = ZUtils.ZTracker;
            int floorIndex = tracker.GetZIndexFor(map);
            List<Pawn> sent = new List<Pawn>();
            foreach (Pawn colonist in colonists.ToList())
            {
                if (colonist == null || !colonist.Spawned || colonist.Dead || colonist.Downed)
                {
                    continue;
                }
                if (colonist.Map == map && colonist.Position == cell)
                {
                    continue;
                }
                sent.Add(colonist);
                tracker.ResetJobTrackerFor(colonist);
                Job walk = JobMaker.MakeJob(JobDefOf.Goto, cell);
                if (colonist.Map == map)
                {
                    colonist.jobs.TryTakeOrderedJob(walk);
                }
                else
                {
                    // The route is the one a float-menu order builds: the go-to-map job first, then the walk on the target floor.
                    tracker.BuildJobListFor(colonist, map, walk);
                    colonist.jobs.EndCurrentJob(JobCondition.InterruptForced);
                }
                tracker.floorPolicy.MoveLock(colonist, floorIndex);
            }
            return sent;
        }
    }

    // The button. It is shown on player colonists only, and it opens the targeter for the selection.
    [HarmonyPatch(typeof(Pawn), "GetGizmos")]
    public static class CallColonistsGizmoPatch
    {
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (__instance == null || !__instance.IsColonistPlayerControlled || __instance.Map == null)
            {
                return;
            }
            var call = new Command_Action
            {
                defaultLabel = "Call colonists here",
                defaultDesc = "Pick a standable spot on this floor. The selected colonists walk to it, and a colonist on another floor takes the stairs first. A colonist already on the spot stays there.",
                icon = TexCommand.GatherSpotActive,
                groupKey = CallColonistsTargeter.GroupKey,
                action = CallColonistsTargeter.Begin,
            };
            __result = __result.Concat(new Gizmo[] { call });
        }
    }
}
