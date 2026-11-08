using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using ZLevels;

namespace ZLevelsValidation
{
    // Task 29 (the Floors tab, and the floor each colonist's needs search on). The tab is reached by reflection, so this file compiles
    // before the tab exists. Each row names the missing piece until it does.
    public partial class RuntimeValidation
    {
        private bool floorsTabSaved;
        private bool floorsTabChecked;
        // Static, because the game recreates the component on load, and the rows before the save are read after it.
        private static bool floorsTabBeforeOk;
        private static int floorsTabPawnId = -1;
        private static int floorsTabLowerZ = int.MinValue;

        // Colonists in the fun row. Each has its own redirect cool-down, so each gets one search. A search free to pick either floor
        // takes the lower one about half the time, so sixteen lower picks in a row happen by chance about once in 65536 runs.
        private const int FloorsTabFunSearches = 16;

        private static Type FloorsTabType()
        {
            return typeof(FloorPolicy).Assembly.GetType("ZLevels.FloorsTab");
        }

        private static Type NeedKindType()
        {
            return typeof(FloorPolicy).Assembly.GetType("ZLevels.NeedKind");
        }

        private static object SetNeedFloorByName(FloorPolicy policy, Pawn pawn, string kindName, int floorIndex)
        {
            Type kind = NeedKindType() ?? throw new InvalidOperationException("the mod has no NeedKind type");
            System.Reflection.MethodInfo method = typeof(FloorPolicy).GetMethod("SetNeedFloor")
                ?? throw new InvalidOperationException("FloorPolicy has no SetNeedFloor");
            return method.Invoke(policy, new object[] { pawn, Enum.Parse(kind, kindName), floorIndex });
        }

        private static object NeedFloorByName(FloorPolicy policy, Pawn pawn, string kindName)
        {
            Type kind = NeedKindType() ?? throw new InvalidOperationException("the mod has no NeedKind type");
            System.Reflection.MethodInfo method = typeof(FloorPolicy).GetMethod("NeedFloorFor")
                ?? throw new InvalidOperationException("FloorPolicy has no NeedFloorFor");
            return method.Invoke(policy, new object[] { pawn, Enum.Parse(kind, kindName) });
        }

        private static string FloorsTabCurrentText(Pawn pawn)
        {
            Type tab = FloorsTabType() ?? throw new InvalidOperationException("the mod has no ZLevels.FloorsTab class");
            System.Reflection.MethodInfo method = tab.GetMethod("CurrentFloorText", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                ?? throw new InvalidOperationException("FloorsTab has no CurrentFloorText");
            return (string)method.Invoke(null, new object[] { pawn });
        }

        // Runs one row's check. An exception from the check, or from a reflected call, is reported as the row's failure.
        private static string CheckFloorsTabRow(Func<string> check)
        {
            try
            {
                return check();
            }
            catch (Exception exception)
            {
                Exception inner = exception is System.Reflection.TargetInvocationException && exception.InnerException != null ? exception.InnerException : exception;
                return "FAIL: " + inner.Message;
            }
        }

        // The map the colonist's route goes to, from its job tracker. Null when the tracker has no route for the colonist.
        private static Map FloorsTabRouteMap(ZLevelsManager tracker, Pawn pawn)
        {
            return tracker.jobTracker != null && tracker.jobTracker.TryGetValue(pawn, out JobTracker tracked) ? tracked.targetDest.Map : null;
        }

        // The job the search returned, then the jobs the tracker queued behind it.
        private static List<Job> FloorsTabChain(ZLevelsManager tracker, Pawn pawn, Job first)
        {
            List<Job> chain = new List<Job>();
            if (first != null)
            {
                chain.Add(first);
            }
            if (tracker.jobTracker != null && tracker.jobTracker.TryGetValue(pawn, out JobTracker tracked) && tracked.activeJobs != null)
            {
                chain.AddRange(tracked.activeJobs);
            }
            return chain;
        }

        private static string DescribeFloorsTabChain(List<Job> chain)
        {
            return chain.Count == 0 ? "none" : string.Join(", ", chain.Select(job => DescribeJob(job)));
        }

        private static string FloorsTabMapText(ZLevelsManager tracker, Map map)
        {
            return map == null ? "no map" : "the floor with Z index " + tracker.GetZIndexFor(map);
        }

        // A cell moved onto the map: each axis is kept between 1 and the last cell less one. The stairs can stand near an edge of the map, and an
        // offset from them then leaves the map (run 20261106-181425: origin (12, 0, -4)). A clamp never moves a cell farther from the stairs than
        // the offset it came from, so the open area around the lower stairs still covers the cell.
        private static IntVec3 OnMapCell(Map map, IntVec3 cell)
        {
            return new IntVec3(Math.Min(Math.Max(cell.x, 1), map.Size.x - 2), 0, Math.Min(Math.Max(cell.z, 1), map.Size.z - 2));
        }

        private static JobGiver_GetRest RestGiverFor(Pawn pawn)
        {
            return FindThinkNode<JobGiver_GetRest>(pawn.thinker.MainThinkNodeRoot)
                ?? throw new InvalidOperationException("the colonist's think tree has no JobGiver_GetRest");
        }

        private static Building_Bed SpawnFloorsTabBed(Map map, IntVec3 cell)
        {
            Building_Bed bed = (Building_Bed)ThingMaker.MakeThing(ThingDefOf.Bed, ThingDefOf.WoodLog);
            bed.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(bed, cell, map);
            return bed;
        }

        private static Thing SpawnFloorsTabMeal(Map map, IntVec3 cell)
        {
            Thing meal = ThingMaker.MakeThing(ThingDefOf.MealSimple);
            meal.stackCount = 5;
            GenSpawn.Spawn(meal, cell, map);
            return meal;
        }

        private static Building SpawnFloorsTabMeditationSpot(Map map, IntVec3 cell)
        {
            Building spot = (Building)ThingMaker.MakeThing(ThingDefOf.MeditationSpot);
            spot.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(spot, cell, map);
            return spot;
        }

        private static string FloorsTabShownRow(Pawn lowerPawn, Pawn surfacePawn)
        {
            string lowerText = FloorsTabCurrentText(lowerPawn);
            string surfaceText = FloorsTabCurrentText(surfacePawn);
            bool shows = lowerText != null && lowerText.Contains("Lower") && surfaceText != null && surfaceText.Contains("Surface");
            return shows
                ? "PASS: the lower colonist shows " + lowerText + " and the surface colonist " + surfaceText
                : "FAIL: the lower colonist shows " + lowerText + " and the surface colonist " + surfaceText;
        }

        // No bed is on either floor when this row runs, so the rest has to be a ground spot, and the sleep floor is the lower floor.
        private static string FloorsTabGroundRow(ZLevelsManager tracker, Pawn colonist, Map lower, int lowerZ)
        {
            SetNeedFloorByName(tracker.floorPolicy, colonist, "Sleep", lowerZ);
            Job first = TryGiveRestJob(RestGiverFor(colonist), colonist);
            List<Job> chain = FloorsTabChain(tracker, colonist, first);
            Map route = FloorsTabRouteMap(tracker, colonist);
            Job rest = chain.FirstOrDefault(job => job.def != ZLevelsDefOf.ZL_GoToMap);
            bool onLower = route == lower && chain.Count > 0 && chain[0].def == ZLevelsDefOf.ZL_GoToMap
                && rest != null && rest.def == JobDefOf.LayDown && rest.targetA.Thing == null;
            return onLower
                ? "PASS: with no bed on either floor the colonist lies on the ground of the lower floor; the jobs are " + DescribeFloorsTabChain(chain)
                : "FAIL: with no bed the rest does not go to the lower floor; the route goes to " + FloorsTabMapText(tracker, route)
                    + "; the jobs are " + DescribeFloorsTabChain(chain);
        }

        private static string FloorsTabSleepRow(ZLevelsManager tracker, Pawn colonist, Map lower, int lowerZ)
        {
            SetNeedFloorByName(tracker.floorPolicy, colonist, "Sleep", lowerZ);
            Job first = TryGiveRestJob(RestGiverFor(colonist), colonist);
            List<Job> chain = FloorsTabChain(tracker, colonist, first);
            Map route = FloorsTabRouteMap(tracker, colonist);
            Job layDown = chain.FirstOrDefault(job => job.def == JobDefOf.LayDown);
            Building_Bed chosen = layDown?.targetA.Thing as Building_Bed;
            return chosen != null && chosen.Map == lower && route == lower
                ? "PASS: the rest job lies down in a bed on the lower floor; the jobs are " + DescribeFloorsTabChain(chain)
                : "FAIL: the rest job is not a bed on the lower floor; the route goes to " + FloorsTabMapText(tracker, route)
                    + "; the jobs are " + DescribeFloorsTabChain(chain);
        }

        private static string FloorsTabFoodRow(ZLevelsManager tracker, Pawn colonist, Map lower, int lowerZ)
        {
            SetNeedFloorByName(tracker.floorPolicy, colonist, "Food", lowerZ);
            // The base food giver with the Hungry minimum, set as the existing food scenarios set it. The think tree's first food
            // giver can be a Starving one, which refuses a colonist at urgent hunger.
            JobGiver_GetFood giver = new JobGiver_GetFood();
            typeof(JobGiver_GetFood).GetField("minCategory", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(giver, HungerCategory.Hungry);
            Job first = InvokeTryGiveJob(giver, colonist);
            List<Job> chain = FloorsTabChain(tracker, colonist, first);
            Map route = FloorsTabRouteMap(tracker, colonist);
            Job eat = chain.FirstOrDefault(job => job.def != ZLevelsDefOf.ZL_GoToMap);
            bool onLower = route == lower && eat != null && eat.targetA.Thing != null && eat.targetA.Thing.Map == lower;
            return onLower
                ? "PASS: the food job takes food on the lower floor; the jobs are " + DescribeFloorsTabChain(chain)
                : "FAIL: the food job does not take food on the lower floor; the route goes to " + FloorsTabMapText(tracker, route)
                    + "; the jobs are " + DescribeFloorsTabChain(chain);
        }

        private static string FloorsTabMeditationRow(ZLevelsManager tracker, Pawn colonist, Building spot, Map lower, int lowerZ)
        {
            SetNeedFloorByName(tracker.floorPolicy, colonist, "Meditation", lowerZ);
            // The base giver, so the patch on JobGiver_Meditate.TryGiveJob runs. A subclass in the think tree would skip it.
            JobGiver_Meditate giver = new JobGiver_Meditate();
            Job first = InvokeTryGiveJob(giver, colonist);
            List<Job> chain = FloorsTabChain(tracker, colonist, first);
            Map route = FloorsTabRouteMap(tracker, colonist);
            Job meditate = chain.FirstOrDefault(job => job.def != ZLevelsDefOf.ZL_GoToMap);
            bool onSpot = route == lower && meditate != null && spot.OccupiedRect().ExpandedBy(2).Contains(meditate.targetA.Cell);
            return onSpot
                ? "PASS: the meditation job is at the spot on the lower floor; the jobs are " + DescribeFloorsTabChain(chain)
                : "FAIL: the meditation job is not at the spot on the lower floor; the route goes to " + FloorsTabMapText(tracker, route)
                    + "; the jobs are " + DescribeFloorsTabChain(chain);
        }

        private static string FloorsTabFunRow(ZLevelsManager tracker, List<Pawn> colonists, Map lower)
        {
            // The base joy giver, with its references resolved. A think tree can hold JobGiver_GetJoyInBed, which overrides TryGiveJob
            // and so skips the patch.
            JobGiver_GetJoy giver = new JobGiver_GetJoy();
            giver.ResolveReferences();
            // The vanilla joy search makes one weighted pick, and the pick can be a joy giver that finds nothing on this floor, so a search
            // can find no job at all. What the floor setting must stop is a job on another floor.
            int onLower = 0;
            int none = 0;
            List<string> elsewhere = new List<string>();
            foreach (Pawn colonist in colonists)
            {
                Job first = InvokeTryGiveJob(giver, colonist);
                Map route = FloorsTabRouteMap(tracker, colonist);
                if (first == null)
                {
                    none++;
                }
                else if (route == lower)
                {
                    onLower++;
                }
                else
                {
                    elsewhere.Add(FloorsTabMapText(tracker, route));
                }
            }
            return elsewhere.Count == 0 && onLower > 0
                ? "PASS: no fun search takes a job off the lower floor; " + onLower + " of " + colonists.Count + " found a job on the lower floor and " + none + " found none"
                : "FAIL: " + elsewhere.Count + " fun searches took a job off the lower floor (" + string.Join(", ", elsewhere.Distinct()) + "); " + onLower + " found one on the lower floor and " + none + " found none";
        }

        private void RunFloorsTabBeforeSave()
        {
            try
            {
                Map surface = Find.CurrentMap;
                ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
                RequireFloors(tracker, surface, 1);
                Building_StairsUp stairs = SetUpLowerStairs(surface, tracker, out Map lower);
                RequireFloors(tracker, surface, 2);
                CarveAround(lower, stairs.Position, 6f);
                // The lower level has never been seen, and its fog hides things from the joy search (the need cool-down scenario clears it too).
                lower.fogGrid.ClearAllFog();
                List<IntVec3> lowerUsed = new List<IntVec3>();
                List<IntVec3> surfaceUsed = new List<IntVec3>();
                int lowerZ = tracker.GetZIndexFor(lower);
                // The surface cells are placed around the stairs, as the food scenario places its surface colonists. The surface centre
                // had no reachable cell in run 20261106-173830.
                IntVec3 middle = OnMapCell(surface, stairs.Position + new IntVec3(10, 0, 0));
                // The surface colonists and furniture below stand up to 8.5 cells from the middle, and each of their origins has to be open.
                // CarveAround keeps its centre, so the middle is opened as well. The stairs are ten cells from the middle, outside this radius.
                CarveAround(surface, middle, 9f);
                OpenSearchOrigin(surface, middle);

                // A colonist on each floor, for the tab's current floor column.
                Pawn lowerColonist = SpawnPawnAt(lower, TakeCellNear(lower, OnMapCell(lower, stairs.Position + new IntVec3(3, 0, 0)), lowerUsed));
                Pawn surfaceColonist = SpawnPawnAt(surface, TakeCellNear(surface, middle, surfaceUsed));

                string tabRow = CheckFloorsTabRow(() =>
                {
                    MainButtonDef button = DefDatabase<MainButtonDef>.GetNamedSilentFail("ZL_Floors");
                    Type tab = FloorsTabType();
                    if (button == null)
                    {
                        return "FAIL: no MainButtonDef named ZL_Floors";
                    }
                    if (tab == null || button.tabWindowClass != tab)
                    {
                        return "FAIL: the button does not open ZLevels.FloorsTab";
                    }
                    return "PASS";
                });
                Record("tab is registered", tabRow);

                string shownRow = CheckFloorsTabRow(() => FloorsTabShownRow(lowerColonist, surfaceColonist));
                Record("current floor column shows the floor", shownRow);

                // The ground row runs before any bed exists, so no bed on either floor can be the answer.
                Pawn groundColonist = SpawnPawnAt(surface, TakeCellNear(surface, OnMapCell(surface, middle + new IntVec3(0, 0, -6)), surfaceUsed));
                groundColonist.needs.rest.CurLevel = 0.05f;
                string groundRow = CheckFloorsTabRow(() => FloorsTabGroundRow(tracker, groundColonist, lower, lowerZ));
                Record("sleep floor set to lower with no bed there: the rest lies on the lower floor", groundRow);

                // The sleep row. The colonist stands on the surface beside a surface bed, and a bed stands on the lower floor. The
                // sleep floor is the lower floor, so the bed on the surface must not be chosen.
                Pawn sleeper = SpawnPawnAt(surface, TakeCellNear(surface, OnMapCell(surface, middle + new IntVec3(-3, 0, 0)), surfaceUsed));
                SpawnFloorsTabBed(surface, TakeCellNear(surface, OnMapCell(surface, middle + new IntVec3(-3, 0, 2)), surfaceUsed));
                SpawnFloorsTabBed(lower, TakeCellNear(lower, OnMapCell(lower, stairs.Position + new IntVec3(3, 0, -2)), lowerUsed));
                sleeper.needs.rest.CurLevel = 0.05f;
                string sleepRow = CheckFloorsTabRow(() => FloorsTabSleepRow(tracker, sleeper, lower, lowerZ));
                Record("sleep floor set to lower: the rest job uses a bed on the lower floor", sleepRow);

                // The food row. A meal on each floor, and a hungry colonist on the surface.
                Pawn eater = SpawnPawnAt(surface, TakeCellNear(surface, OnMapCell(surface, middle + new IntVec3(-6, 0, -4)), surfaceUsed));
                eater.needs.food.CurLevelPercentage = 0.1f;
                SpawnFloorsTabMeal(surface, TakeCellNear(surface, OnMapCell(surface, middle + new IntVec3(-6, 0, -2)), surfaceUsed));
                SpawnFloorsTabMeal(lower, TakeCellNear(lower, OnMapCell(lower, stairs.Position + new IntVec3(3, 0, 4)), lowerUsed));
                string foodRow = CheckFloorsTabRow(() => FloorsTabFoodRow(tracker, eater, lower, lowerZ));
                Record("food floor set to lower: the food job takes food on the lower floor", foodRow);

                // The meditation row. A meditation spot stands on the lower floor only.
                Pawn meditator = SpawnPawnAt(surface, TakeCellNear(surface, OnMapCell(surface, middle + new IntVec3(6, 0, 3)), surfaceUsed));
                Building spot = SpawnFloorsTabMeditationSpot(lower, TakeCellNear(lower, OnMapCell(lower, stairs.Position + new IntVec3(4, 0, 3)), lowerUsed));
                string meditationRow = CheckFloorsTabRow(() => FloorsTabMeditationRow(tracker, meditator, spot, lower, lowerZ));
                Record("meditation floor set to lower: the meditation spot is on the lower floor", meditationRow);

                // The fun row. Colonists on the surface, each with its fun floor set to the lower floor.
                List<Pawn> fans = new List<Pawn>();
                for (int i = 0; i < FloorsTabFunSearches; i++)
                {
                    Pawn fan = SpawnPawnAt(surface, TakeCellNear(surface, OnMapCell(surface, middle + new IntVec3(-6, 0, 6)), surfaceUsed));
                    fan.needs.joy.CurLevel = 0.1f;
                    SetNeedFloorByName(tracker.floorPolicy, fan, "Fun", lowerZ);
                    fans.Add(fan);
                }
                string funRow = CheckFloorsTabRow(() => FloorsTabFunRow(tracker, fans, lower));
                Record("fun floor set to lower: no fun search takes a job off the lower floor", funRow);

                floorsTabBeforeOk = tabRow == "PASS" && shownRow.StartsWith("PASS") && groundRow.StartsWith("PASS")
                    && sleepRow.StartsWith("PASS") && foodRow.StartsWith("PASS") && meditationRow.StartsWith("PASS") && funRow.StartsWith("PASS");
                floorsTabPawnId = sleeper.thingIDNumber;
                floorsTabLowerZ = lowerZ;
                GameDataSaveLoader.SaveGame("ZLevelsValidation");
                LongEventHandler.ExecuteWhenFinished(() => GameDataSaveLoader.LoadGame("ZLevelsValidation"));
            }
            catch (Exception exception)
            {
                Record("floors tab", "FAIL: " + exception);
                Log.Error("Z-Levels floors tab validation failed: " + exception);
                Root.Shutdown();
            }
        }

        private void RunFloorsTabAfterReload()
        {
            try
            {
                ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
                Pawn sleeper = FindPawnById(floorsTabPawnId);
                string survives = CheckFloorsTabRow(() =>
                {
                    if (sleeper == null)
                    {
                        return "FAIL: the colonist is not on any map after the load";
                    }
                    object value = NeedFloorByName(tracker.floorPolicy, sleeper, "Sleep");
                    return value is int floorIndex && floorIndex == floorsTabLowerZ
                        ? "PASS"
                        : "FAIL: after the load the sleep floor is " + (value == null ? "none" : value.ToString()) + ", expected " + floorsTabLowerZ;
                });
                Record("floor setting survives save and reload", survives);
                Record("floors tab", floorsTabBeforeOk && survives == "PASS" ? "PASS" : "FAIL: a row above failed");
            }
            catch (Exception exception)
            {
                Record("floors tab", "FAIL: " + exception);
                Log.Error("Z-Levels floors tab validation failed after the load: " + exception);
            }
            finished = true;
            Root.Shutdown();
        }
    }
}
