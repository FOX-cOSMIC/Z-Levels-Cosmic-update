using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using ZLevels;

namespace ZLevelsValidation
{
    // Scenarios for docs/superpowers/plans/2026-11-05-multifloors-ideas.md. A scenario starts from a game
    // command-line flag in RuntimeValidation's dispatch and writes its rows with Record(). Each task adds its
    // own scenario methods here.
    public partial class RuntimeValidation
    {
        // Task 1 (bugs 1 and 2 on docs/mod-comparison/ideas.html). The rest search must not read a null result: a colonist
        // in a mental state with no bed gets a ground job with no Thing, and a colonist whom no floor gives a job gets null.
        private void RunRestSearchNullCheck()
        {
            int errors = 0;
            UnityEngine.Application.LogCallback counter = (condition, stackTrace, type) =>
            {
                if (type == UnityEngine.LogType.Error && condition.Contains("Some kind of error occurred in Z-Levels JobManager"))
                {
                    errors++;
                }
            };
            UnityEngine.Application.logMessageReceived += counter;
            StorytellerDef storyteller = null;
            bool tutorialMode = false;
            try
            {
                Map map = Find.CurrentMap;
                // The validation colony's storyteller is in tutorial mode, which blocks mental breaks for player pawns. The flag is off
                // for this isolated run and restored in finally.
                storyteller = Find.Storyteller.def;
                tutorialMode = storyteller.tutorialMode;
                storyteller.tutorialMode = false;
                // Every bed on the map is removed, so no floor can give a bed job for row 1. Forbidding a bed does not do this:
                // the bed search still returned a forbidden bed in the first red run.
                foreach (Building_Bed bed in map.listerBuildings.allBuildingsColonist.OfType<Building_Bed>().ToList())
                {
                    bed.Destroy(DestroyMode.Vanish);
                }
                Pawn mental = SpawnRestPawn(map);
                JobGiver_GetRest giver = FindThinkNode<JobGiver_GetRest>(mental.thinker.MainThinkNodeRoot);
                if (giver == null)
                {
                    throw new InvalidOperationException("the colonist's think tree has no JobGiver_GetRest");
                }

                // Row 1: a tired colonist in a mental state, with no bed on the map.
                mental.needs.rest.CurLevelPercentage = 0.1f;
                bool inState = mental.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Wander_Sad, "validation", forced: true) && mental.MentalStateDef != null;
                errors = 0;
                Job groundJob = TryGiveRestJob(giver, mental);
                bool groundOk = inState && errors == 0 && (groundJob == null || groundJob.def == JobDefOf.LayDown);
                Record("rest search diagnostic", "INFO: in state " + inState + ", job " + (groundJob == null ? "null" : groundJob.def.defName + " targetA " + groundJob.targetA + " thing " + (groundJob.targetA.Thing == null ? "none" : groundJob.targetA.Thing.ToString())) + ", errors " + errors + ", disturbed " + RestUtility.DisturbancePreventsLyingDown(mental) + ", bed " + (RestUtility.FindBedFor(mental) == null ? "none" : "found") + ", category " + mental.needs.rest.CurCategory + " at " + mental.needs.rest.CurLevelPercentage);
                Record("rest search stays clean without a bed", groundOk ? "PASS" : "FAIL: mental state " + inState + ", errors " + errors + ", job " + groundJob);

                // Row 2 (control): the same colonist with one new bed on the map takes that bed.
                Building_Bed freeBed = SpawnRestBed(map);
                errors = 0;
                Job bedJob = TryGiveRestJob(giver, mental);
                bool bedOk = inState && errors == 0 && bedJob != null && bedJob.targetA.Thing == freeBed;
                Record("mental-state rest with a bed on the map uses the bed", bedOk ? "PASS" : "FAIL: mental state " + inState + ", errors " + errors + ", job " + bedJob);
                freeBed.Destroy(DestroyMode.Vanish);

                // Row 3: a rested colonist, not in a mental state, with no bed on the map. No floor gives a job.
                Pawn rested = SpawnRestPawn(map);
                rested.needs.rest.CurLevelPercentage = 1f;
                errors = 0;
                Job noJob = TryGiveRestJob(giver, rested);
                bool noJobOk = errors == 0 && noJob == null;
                Record("rest search stays clean with no job on any floor", noJobOk ? "PASS" : "FAIL: errors " + errors + ", job " + noJob);

                Record("rest search null checks", groundOk && bedOk && noJobOk ? "PASS" : "FAIL: a row above failed");
            }
            catch (Exception exception)
            {
                Record("rest search null checks", "FAIL: " + exception);
                Log.Error("Z-Levels rest search null validation failed: " + exception);
            }
            finally
            {
                UnityEngine.Application.logMessageReceived -= counter;
                if (storyteller != null)
                {
                    storyteller.tutorialMode = tutorialMode;
                }
            }
            Root.Shutdown();
        }

        // The game ignores a time-speed change unless the player has control. Control is lost while the screen fades in and while a
        // gravship landing waits for confirmation. A check that expects a pause to stick waits for control first.
        private static bool PlayerControlReady()
        {
            return Current.Game.PlayerHasControl && !(Find.GravshipController != null && Find.GravshipController.LandingAreaConfirmationInProgress);
        }

        // Task 4 (idea 2 on docs/mod-comparison/ideas.html). A queued job whose reservation is refused must not pause the game. The
        // colonist's job list is cleared, so the colonist searches again.
        private void RunReservationRefusedCheck()
        {
            int pauses = 0;
            UnityEngine.Application.LogCallback counter = (condition, stackTrace, type) =>
            {
                if (type == UnityEngine.LogType.Error && condition.Contains("Fail in TryMakePreToilReservations"))
                {
                    pauses++;
                }
            };
            UnityEngine.Application.logMessageReceived += counter;
            try
            {
                Map map = Find.CurrentMap;
                ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
                IntVec3 steelCell = FindOpenBlock(map, 1, 1);
                if (!steelCell.IsValid)
                {
                    throw new InvalidOperationException("no open cell for the steel");
                }
                Thing steel = ThingMaker.MakeThing(ThingDef.Named("Steel"));
                steel.stackCount = 30;
                GenSpawn.Spawn(steel, steelCell, map);
                IntVec3 destCell = FindOpenBlock(map, 1, 1);
                if (!destCell.IsValid)
                {
                    throw new InvalidOperationException("no open cell for the destination");
                }
                Pawn holder = SpawnRestPawn(map);
                Job holdJob = JobMaker.MakeJob(JobDefOf.HaulToCell, steel, destCell);
                if (!holder.Reserve(steel, holdJob))
                {
                    throw new InvalidOperationException("the holder could not reserve the steel");
                }
                Pawn colonist = SpawnRestPawn(map);
                Job job = JobMaker.MakeJob(JobDefOf.HaulToCell, steel, destCell);
                job.count = 30;
                if (!tracker.jobTracker.TryGetValue(colonist, out JobTracker jobTracker))
                {
                    jobTracker = new JobTracker();
                    tracker.jobTracker[colonist] = jobTracker;
                }
                jobTracker.activeJobs = new List<Job> { job };
                jobTracker.mainJob = job;
                bool refused = !job.TryMakePreToilReservations(colonist, false);
                Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                tracker.TryTakeFirstJob(colonist);
                bool paused = Find.TickManager.CurTimeSpeed == TimeSpeed.Paused;
                bool cleared = !tracker.jobTracker.TryGetValue(colonist, out JobTracker after) || after.activeJobs == null || !after.activeJobs.Any();
                bool noJob = colonist.CurJob == null;
                Record("reservation diagnostic", "INFO: player control " + Current.Game.PlayerHasControl + ", fading " + ScreenFader.IsFading() + ", landing confirmation " + (Find.GravshipController != null && Find.GravshipController.LandingAreaConfirmationInProgress) + ", speed after the refusal " + Find.TickManager.CurTimeSpeed + ", pause entries " + pauses);
                Record("reservation refusal is set up", refused ? "PASS" : "FAIL: the steel was not reserved for the holder");
                Record("refused reservation does not pause the game", paused ? "FAIL: the game paused" : "PASS");
                Record("no pause logged for a refused reservation", pauses == 0 ? "PASS" : "FAIL: " + pauses + " pause entries logged");
                Record("refused colonist searches again", cleared && noJob ? "PASS" : "FAIL: job list cleared " + cleared + ", current job " + colonist.CurJob);
                bool allOk = refused && !paused && pauses == 0 && cleared && noJob;
                Record("reservation refused", allOk ? "PASS" : "FAIL: a row above failed");
            }
            catch (Exception exception)
            {
                Record("reservation refused", "FAIL: " + exception);
                Log.Error("Z-Levels reservation refusal validation failed: " + exception);
            }
            finally
            {
                UnityEngine.Application.logMessageReceived -= counter;
            }
            Root.Shutdown();
        }

        // Task 2 (bug 3 on docs/mod-comparison/ideas.html). Pawn_TraderTracker.ColonyThingsWillingToBuy saves the negotiator's cell and sends
        // the trader back to it. The trader must end on its own cell and map.
        private void RunTraderReturnCheck()
        {
            try
            {
                Map map = Find.CurrentMap;
                IntVec3 traderCell = FindOpenBlock(map, 1, 1);
                if (!traderCell.IsValid)
                {
                    throw new InvalidOperationException("no open cell for the trader");
                }
                Pawn trader = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                trader.mindState.wantsToTradeWithColony = true;
                GenSpawn.Spawn(trader, traderCell, map);
                PawnComponentsUtility.AddAndRemoveDynamicComponents(trader, true);
                if (trader.trader == null)
                {
                    throw new InvalidOperationException("the trader has no trader tracker");
                }
                Pawn negotiator = SpawnRestPawn(map);
                trader.trader.ColonyThingsWillingToBuy(negotiator).ToList();
                bool back = trader.Spawned && trader.Map == map && trader.Position == traderCell;
                Record("trader return", back ? "PASS" : "FAIL: the trader stands on " + trader.Position + " after the trade, not on its own cell " + traderCell + "; the negotiator stands on " + negotiator.Position);
            }
            catch (Exception exception)
            {
                Record("trader return", "FAIL: " + exception);
                Log.Error("Z-Levels trader return validation failed: " + exception);
            }
            Root.Shutdown();
        }

        // Calls the rest giver's TryGiveJob, which the Z-Levels prefix patches. The method is protected, so it is reached by reflection.
        private static Job TryGiveRestJob(JobGiver_GetRest giver, Pawn pawn)
        {
            System.Reflection.MethodInfo method = typeof(JobGiver_GetRest).GetMethod("TryGiveJob", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return (Job)method.Invoke(giver, new object[] { pawn });
        }

        private static T FindThinkNode<T>(ThinkNode node) where T : ThinkNode
        {
            if (node is T found)
            {
                return found;
            }
            foreach (ThinkNode sub in node.subNodes)
            {
                T inner = FindThinkNode<T>(sub);
                if (inner != null)
                {
                    return inner;
                }
            }
            return null;
        }

        private static Building_Bed SpawnRestBed(Map map)
        {
            IntVec3 cell = FindOpenBlock(map, 1, 2);
            if (!cell.IsValid)
            {
                throw new InvalidOperationException("no open cells for the bed");
            }
            ThingDef bedDef = ThingDef.Named("Bed");
            Building_Bed bed = (Building_Bed)ThingMaker.MakeThing(bedDef, bedDef.MadeFromStuff ? GenStuff.DefaultStuffFor(bedDef) : null);
            bed.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(bed, cell, map, Rot4.North);
            return bed;
        }

        private static Pawn SpawnRestPawn(Map map)
        {
            IntVec3 cell = FindOpenBlock(map, 1, 1);
            if (!cell.IsValid)
            {
                throw new InvalidOperationException("no open cell for a colonist");
            }
            Pawn pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            GenSpawn.Spawn(pawn, cell, map);
            return pawn;
        }

        // Task 5 (idea 1 on docs/mod-comparison/ideas.html). N and M switch floors, and a switch keeps the selected designator and the
        // camera's x and z. A player's prefs file can hold both switch keys as None. The game adds a default only for a key the file lacks,
        // so such a file leaves the switch keys unbound. The scenario writes that saved state into the run's own Config folder and loads it
        // with KeyPrefs.Init, the call the game makes at start.
        private void RunFloorHotkeysCheck()
        {
            try
            {
                Map surface = Find.CurrentMap;
                ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
                string prefsPath = GenFilePaths.KeyPrefsFilePath;
                WriteUnboundSwitchKeysPrefs(prefsPath);
                KeyPrefs.Init();
                KeyBindingDef upKey = ZLevelsManager.UpperSwitchKey;
                KeyBindingDef downKey = ZLevelsManager.LowerSwitchKey;
                Record("floor hotkey diagnostic", "INFO: prefs file " + prefsPath + ", " + upKey.defName + " bound to " + upKey.MainKey + ", " + downKey.defName + " bound to " + downKey.MainKey + ", defaults " + upKey.defaultKeyCodeA + " and " + downKey.defaultKeyCodeA);
                bool upOk = upKey.MainKey == UnityEngine.KeyCode.N;
                bool downOk = downKey.MainKey == UnityEngine.KeyCode.M;
                Record("upper switch key is N", upOk ? "PASS" : "FAIL: the upper switch key is " + upKey.MainKey + " after the prefs file loads");
                Record("lower switch key is M", downOk ? "PASS" : "FAIL: the lower switch key is " + downKey.MainKey + " after the prefs file loads");

                Map lower = CreateLevelBelow(tracker, surface);
                Designator_Cancel designator = new Designator_Cancel();
                Find.DesignatorManager.Select(designator);
                UnityEngine.Vector3 start = new UnityEngine.Vector3(surface.Center.x + 0.25f, 15f, surface.Center.z + 0.25f);
                Find.CameraDriver.SetRootPosAndSize(start, 24f);
                SyncRememberedCamera();
                UnityEngine.Vector3 before = LiveCameraRootPos();

                tracker.SwitchFloor(false);
                bool onLower = Current.Game.CurrentMap == lower;
                UnityEngine.Vector3 atLower = LiveCameraRootPos();
                bool designatorAtLower = Find.DesignatorManager.SelectedDesignator == designator;

                SyncRememberedCamera();
                tracker.SwitchFloor(true);
                bool onSurface = Current.Game.CurrentMap == surface;
                UnityEngine.Vector3 atSurface = LiveCameraRootPos();
                bool designatorAtSurface = Find.DesignatorManager.SelectedDesignator == designator;

                // A row needs the switch to have happened. Without this, a switch that did nothing would leave the camera where it was and pass.
                bool designatorOk = onLower && onSurface && designatorAtLower && designatorAtSurface;
                bool cameraOk = onLower && onSurface && SameXZ(before, atLower) && SameXZ(before, atSurface);
                Record("floor switch keeps the selected designator", designatorOk ? "PASS" : "FAIL: reached the lower level " + onLower + ", back on the surface " + onSurface + ", designator kept " + designatorAtLower + " and " + designatorAtSurface);
                Record("floor switch keeps the camera x and z", cameraOk ? "PASS" : "FAIL: the camera was at " + Describe(before) + ", then " + Describe(atLower) + " on the lower level and " + Describe(atSurface) + " back on the surface");
                bool allOk = upOk && downOk && designatorOk && cameraOk;
                Record("floor hotkeys", allOk ? "PASS" : "FAIL: a row above failed");
            }
            catch (Exception exception)
            {
                Record("floor hotkeys", "FAIL: " + exception);
                Log.Error("Z-Levels floor hotkey validation failed: " + exception);
            }
            Root.Shutdown();
        }

        // Writes a prefs file in the form a player's file has: both switch keys saved as None, under the names the mod used before the fix.
        private static void WriteUnboundSwitchKeysPrefs(string path)
        {
            string entries = "";
            foreach (string name in new[] { "ZL_switchToUpperMap", "ZL_switchToLowerMap" })
            {
                entries += "  <li>\n    <key>" + name + "</key>\n    <value>\n      <keyBindingA>None</keyBindingA>\n      <keyBindingB>None</keyBindingB>\n    </value>\n  </li>\n";
            }
            System.IO.File.WriteAllText(path, "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<KeyPrefsData>\n" + entries + "</KeyPrefsData>\n", new System.Text.UTF8Encoding(false));
        }

        // CameraDriver.rootPos is private, so the live position is read by reflection. The reflected value is exact, where
        // CurrentViewRect rounds to cells.
        private static UnityEngine.Vector3 LiveCameraRootPos()
        {
            System.Reflection.FieldInfo field = typeof(CameraDriver).GetField("rootPos", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field == null)
            {
                throw new InvalidOperationException("CameraDriver has no rootPos field");
            }
            return (UnityEngine.Vector3)field.GetValue(Find.CameraDriver);
        }

        // CameraDriver.Update copies the camera's position into the map's remembered position every frame, and a switch reads the remembered
        // position of the map it leaves. The scenario makes the same copy before each switch.
        private static void SyncRememberedCamera()
        {
            RememberedCameraPos remembered = Current.Game.CurrentMap.rememberedCameraPos;
            remembered.rootPos = LiveCameraRootPos();
            remembered.rootSize = Find.CameraDriver.RootSize;
        }

        private static bool SameXZ(UnityEngine.Vector3 a, UnityEngine.Vector3 b)
        {
            return Math.Abs(a.x - b.x) < 0.001f && Math.Abs(a.z - b.z) < 0.001f;
        }

        private static string Describe(UnityEngine.Vector3 v)
        {
            return "(" + v.x + ", " + v.z + ")";
        }

        // Task 6 (idea 5 on docs/mod-comparison/ideas.html). A colonist locked to a floor does no job on another floor, the lock survives a
        // save and reload, and it ends when the colonist dies. The first run of the scenario sets the lock and saves. The instance that the
        // load creates runs the rest.
        private bool stayOnFloorSaved;
        private bool stayOnFloorChecked;
        private static int stayLockedPawnId = -1;
        private static int stayLowerZIndex = int.MinValue;
        private static bool stayBeforeSaveOk;

        private void RunStayOnFloorBeforeSave()
        {
            try
            {
                Map surface = Find.CurrentMap;
                ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
                Map lower = CreateLevelBelow(tracker, surface);
                // The lower stairs are made from a surface stairs down: SpawnStairsBelow places the partner at the same cell on the lower level.
                IntVec3 stairCell = FindOpenBlock(surface, 1, 1);
                if (!stairCell.IsValid)
                {
                    throw new InvalidOperationException("no open cell on the surface for the stairs");
                }
                surface.terrainGrid.SetTerrain(stairCell, TerrainDefOf.Concrete);
                Building_StairsDown surfaceStairs = (Building_StairsDown)ThingMaker.MakeThing(ZLevelsDefOf.ZL_StairsDown, ThingDefOf.Steel);
                surfaceStairs.SetFaction(Faction.OfPlayer);
                GenSpawn.Spawn(surfaceStairs, stairCell, surface);
                ClearCell(lower, stairCell);
                lower.terrainGrid.SetTerrain(stairCell, TerrainDefOf.Concrete);
                Pawn surfaceColonist = SpawnRestPawn(surface);
                tracker.SpawnStairsBelow(surfaceColonist, stairCell, lower);
                Building_StairsUp stairs = lower.listerThings.AllThings.OfType<Building_StairsUp>().FirstOrDefault();
                if (stairs == null)
                {
                    throw new InvalidOperationException("the lower level has no stairs up after SpawnStairsBelow");
                }
                // The lower level is solid rock around the new stairs, so an open area of radius 2 is carved for the colonists to stand on.
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(stairs.Position, 2.5f, true))
                {
                    if (cell == stairs.Position || !cell.InBounds(lower)) continue;
                    ClearCell(lower, cell);
                    lower.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete);
                }
                // Building.SpawnSetup sets recheckStairs, and the tracker re-reads its stairs on the next tick. This scenario runs no tick loop,
                // so it runs that tick here. Without it the lower level registers no stairs up and a lower colonist cannot climb.
                tracker.GameComponentTick();
                // Both colonists stand on cells the lower stairs can reach. A cell in a sealed part of the lower level gives no haul at all.
                IntVec3 arrival = stairs.Position + new IntVec3(1, 0, 0);
                ClearCell(lower, arrival);
                lower.terrainGrid.SetTerrain(arrival, TerrainDefOf.Concrete);
                IntVec3 lockedCell = FindReachableCellNear(lower, arrival, new List<IntVec3>());
                IntVec3 freeCell = FindReachableCellNear(lower, arrival, new List<IntVec3> { lockedCell });
                if (!lockedCell.IsValid || !freeCell.IsValid)
                {
                    throw new InvalidOperationException("no cells the lower stairs can reach for two colonists");
                }
                Pawn locked = SpawnHaulerAt(lower, lockedCell);
                Pawn free = SpawnHaulerAt(lower, freeCell);
                locked.workSettings.SetPriority(WorkTypeDefOf.Hauling, 3);
                free.workSettings.SetPriority(WorkTypeDefOf.Hauling, 3);

                // Two steel stacks lie on the surface outside any storage, and a storage zone on the surface takes them. A search reserves the
                // stack it chooses, so each colonist's haul needs a stack of its own. Other steel on the surface is forbidden.
                ThingDef steelDef = ThingDef.Named("Steel");
                IntVec3 steelCell = FindOpenBlock(surface, 1, 1);
                if (!steelCell.IsValid)
                {
                    throw new InvalidOperationException("no open cell on the surface for the steel");
                }
                Thing steel = ThingMaker.MakeThing(steelDef);
                steel.stackCount = 30;
                GenSpawn.Spawn(steel, steelCell, surface);
                IntVec3 steelCellB = FindOpenBlock(surface, 1, 1);
                if (!steelCellB.IsValid)
                {
                    throw new InvalidOperationException("no second open cell on the surface for the steel");
                }
                Thing steelB = ThingMaker.MakeThing(steelDef);
                steelB.stackCount = 30;
                GenSpawn.Spawn(steelB, steelCellB, surface);
                foreach (Thing other in surface.listerThings.ThingsOfDef(steelDef).ToList())
                {
                    if (other != steel && other != steelB) other.SetForbidden(true);
                }
                IntVec3 zoneNear = FindOpenBlock(surface, 1, 1);
                if (!zoneNear.IsValid)
                {
                    throw new InvalidOperationException("no open cell on the surface for the storage zone");
                }
                Zone_Stockpile zone = MakeSupplyZone(surface, zoneNear);

                int lowerZ = tracker.GetZIndexFor(lower);
                tracker.floorPolicy.SetLock(locked, lowerZ, int.MaxValue);
                // The setup's own work searches can leave the free colonist in its idle pause (DM6). The clock moves past the pause, so
                // the searches below test the lock, not the pause. The lock has no expiry, so the move does not end it.
                Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + FloorPolicy.IdlePauseTicks + 1);
                // Each colonist starts its search with a fresh job tracker, as the unforced haul scenario does.
                tracker.ResetJobTrackerFor(locked);
                tracker.ResetJobTrackerFor(free);
                Job lockedJob = SearchWorkJob(locked);
                Job freeJob = SearchWorkJob(free);
                bool lockedSkips = !HaulsOn(lockedJob, surface);
                bool freeTakes = HaulsOn(freeJob, surface);
                bool gizmoOffered = locked.GetGizmos().OfType<Command_Toggle>().Any(toggle => toggle.defaultLabel == "Stay on this floor");
                Record("locked colonist skips the surface haul", lockedSkips ? "PASS" : "FAIL: the locked colonist on " + locked.Map + " takes " + lockedJob.def.defName + " for " + lockedJob.targetA.Thing);
                Record("unlocked colonist takes the same haul", freeTakes ? "PASS" : "FAIL: the unlocked colonist on " + free.Map + " takes " + (freeJob == null ? "no job" : freeJob.def.defName + " for " + freeJob.targetA.Thing));
                Record("stay on this floor gizmo is offered", gizmoOffered ? "PASS" : "FAIL: the colonist has no gizmo labelled Stay on this floor");
                Job surfaceJob = SearchWorkJob(surfaceColonist);
                bool freeSeesSurface = tracker.GetAllMapsInClosestOrder(lower).Contains(surface);
                Record("stay on floor diagnostic", "INFO: the locked colonist's search gives " + DescribeJob(lockedJob) + "; the unlocked colonist's search gives " + DescribeJob(freeJob) + "; a colonist on the surface gets " + DescribeJob(surfaceJob) + "; the unlocked colonist's search covers the surface " + freeSeesSurface + "; stairs up on the lower level " + lower.listerThings.AllThings.OfType<Building_StairsUp>().Count() + " (registered " + tracker.GetStairsUpFor(lower).Count() + "); steel at " + steel.Position + " on " + steel.Map + "; zone cells " + zone.Cells.Count() + "; surface colonist hauling priority " + surfaceColonist.workSettings.GetPriority(WorkTypeDefOf.Hauling) + " disabled " + surfaceColonist.WorkTypeIsDisabled(WorkTypeDefOf.Hauling) + "; steel forbidden " + steel.IsForbidden(surfaceColonist) + "; zone accepts steel " + zone.settings.AllowedToAccept(steel) + "; auto haul allowed " + HaulAIUtility.PawnCanAutomaticallyHaul(surfaceColonist, steel, true) + "; surface reach to steel " + surface.reachability.CanReach(surfaceColonist.Position, steel.Position, PathEndMode.ClosestTouch, TraverseParms.For(TraverseMode.PassDoors)) + "; unlocked colonist at " + free.Position + " on " + free.Map);
                // Diagnostic only, and no row above depends on it. It shows where the stairs, the steel and the zone stand, whether the steel is
                // reserved, and what a second search gives after a fresh tracker and a clock move past the idle pause.
                string surfaceStairsAt = string.Join(" ", surface.listerThings.AllThings.OfType<Building_StairsDown>().Select(stair => stair.Position.ToString()));
                string lowerStairsAt = string.Join(" ", lower.listerThings.AllThings.OfType<Building_StairsUp>().Select(stair => stair.Position.ToString()));
                string zoneAt = string.Join(" ", zone.Cells.Take(4).Select(cell => cell.ToString()));
                bool steelReserved = surface.reservationManager.IsReservedByAnyoneOf(steel, Faction.OfPlayer);
                bool steelBReserved = surface.reservationManager.IsReservedByAnyoneOf(steelB, Faction.OfPlayer);
                tracker.ResetJobTrackerFor(free);
                Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + FloorPolicy.IdlePauseTicks + 1);
                Job retryFree = SearchWorkJob(free);
                Record("stay on floor geometry", "INFO: surface stairs down at " + (surfaceStairsAt.Length == 0 ? "none" : surfaceStairsAt)
                    + "; lower stairs up at " + (lowerStairsAt.Length == 0 ? "none" : lowerStairsAt)
                    + "; steel at " + steel.Position + " (reserved " + steelReserved + ") and " + steelB.Position + " (reserved " + steelBReserved + ")"
                    + "; zone cells start at " + zoneAt + "; unlocked colonist at " + free.Position + " on " + free.Map
                    + "; surface colonist at " + surfaceColonist.Position + "; a second search for the unlocked colonist after a fresh tracker gives " + DescribeJob(retryFree));
                // Diagnostic only. It says whether the steel is already stored, whether the surface has a store cell for it, and how many claims in the
                // colonists' job trackers name either stack, since a claim or a full store stops a haul search.
                bool steelStored = steel.IsInAnyStorage();
                bool steelBStored = steelB.IsInAnyStorage();
                bool storeFound = StoreUtility.TryFindBestBetterStoreCellFor(steel, surfaceColonist, surface, StoragePriority.Unstored, Faction.OfPlayer, out IntVec3 storeCell);
                int claimsOnSteel = 0;
                foreach (JobTracker jobTracker in tracker.jobTracker.Values)
                {
                    if (jobTracker.reservedThings == null) continue;
                    claimsOnSteel += jobTracker.reservedThings.Count(claim => claim.HasThing && (claim.Thing == steel || claim.Thing == steelB));
                }
                Record("stay on floor storage", "INFO: steel in storage " + steelStored + " and " + steelBStored + "; a store cell for the first stack from the surface "
                    + (storeFound ? storeCell.ToString() : "none") + "; claims by colonists' job trackers on either stack " + claimsOnSteel);
                stayBeforeSaveOk = lockedSkips && freeTakes && gizmoOffered;
                Record("floor locks before the save", "INFO: " + tracker.floorPolicy.Count + " held; the locked colonist held " + tracker.floorPolicy.HasLock(locked));
                stayLockedPawnId = locked.thingIDNumber;
                stayLowerZIndex = lowerZ;
                GameDataSaveLoader.SaveGame("ZLevelsValidation");
                LongEventHandler.ExecuteWhenFinished(() => GameDataSaveLoader.LoadGame("ZLevelsValidation"));
            }
            catch (Exception exception)
            {
                Record("stay on floor", "FAIL: " + exception);
                Log.Error("Z-Levels stay on floor validation failed: " + exception);
                Root.Shutdown();
            }
        }

        private void RunStayOnFloorAfterReload()
        {
            try
            {
                ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
                int now = Find.TickManager.TicksGame;
                Pawn locked = FindPawnById(stayLockedPawnId);
                Record("floor locks after the load", "INFO: " + tracker.floorPolicy.Count + " held; the locked colonist found " + (locked != null));
                bool survives = locked != null && tracker.floorPolicy.LockedFloorIndex(locked, now) == stayLowerZIndex;
                Record("lock survives save and reload", survives ? "PASS" : "FAIL: after the load the locked colonist " + (locked == null ? "is not on any map" : "has no lock to floor " + stayLowerZIndex));

                // The row needs the lock to exist before the death, so a lost lock cannot pass as an ended one.
                bool ended = false;
                if (survives)
                {
                    locked.Kill(null);
                    ended = locked.Dead && !tracker.floorPolicy.HasLock(locked) && tracker.floorPolicy.LockedFloorIndex(locked, now) == null;
                }
                Record("lock ends when the colonist dies", ended ? "PASS" : "FAIL: the lock " + (survives ? "is still held after the death" : "was not present before the death"));
                Record("stay on floor", stayBeforeSaveOk && survives && ended ? "PASS" : "FAIL: a row above failed");
            }
            catch (Exception exception)
            {
                Record("stay on floor", "FAIL: " + exception);
                Log.Error("Z-Levels stay on floor validation failed after the load: " + exception);
            }
            Root.Shutdown();
        }

        // The colonist's work job search. Its TryIssueJobPackage carries the Z-Levels all-floors search.
        private static Job SearchWorkJob(Pawn pawn)
        {
            JobGiver_Work giver = FindNormalWorkNode(pawn.thinker.MainThinkNodeRoot);
            if (giver == null)
            {
                throw new InvalidOperationException("the colonist's think tree has no normal JobGiver_Work");
            }
            return giver.TryIssueJobPackage(pawn, new JobIssueParams()).Job;
        }

        // The think tree has an emergency JobGiver_Work, which offers only prioritized and emergency work, and a normal one, which offers hauling.
        private static JobGiver_Work FindNormalWorkNode(ThinkNode node)
        {
            if (node is JobGiver_Work work && !work.emergency)
            {
                return work;
            }
            foreach (ThinkNode sub in node.subNodes)
            {
                JobGiver_Work found = FindNormalWorkNode(sub);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        // The first standable cell near the given cell that the cell can reach, and that no cell in avoid is next to.
        private static IntVec3 FindReachableCellNear(Map map, IntVec3 near, List<IntVec3> avoid)
        {
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(near, 6f, false))
            {
                if (!cell.InBounds(map) || !cell.Standable(map) || cell.GetEdifice(map) != null || cell.GetFirstItem(map) != null) continue;
                if (avoid.Any(other => other.DistanceTo(cell) < 2f)) continue;
                if (!map.reachability.CanReach(near, cell, PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassDoors))) continue;
                return cell;
            }
            return IntVec3.Invalid;
        }

        // What a reachable search sees from its origin: the origin's own cell, and how many open cells within six of it the search reaches from there.
        // A failure that names these tells a closed area apart from a failed reachability read.
        private static string DescribeSearchOrigin(Map map, IntVec3 near)
        {
            if (!near.InBounds(map)) return "origin " + near + " is off " + map;
            int open = 0;
            int reachable = 0;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(near, 6f, false))
            {
                if (!cell.InBounds(map) || !cell.Standable(map) || cell.GetEdifice(map) != null || cell.GetFirstItem(map) != null) continue;
                open++;
                if (map.reachability.CanReach(near, cell, PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassDoors))) reachable++;
            }
            return "origin " + near + " has terrain " + near.GetTerrain(map).defName + ", standable " + near.Standable(map) + ", roofed " + near.Roofed(map)
                + ", edifice " + (near.GetEdifice(map)?.def.defName ?? "none") + "; open cells within 6: " + open + ", reachable from it: " + reachable;
        }

        private static Pawn SpawnPawnAt(Map map, IntVec3 cell)
        {
            Pawn pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            GenSpawn.Spawn(pawn, cell, map);
            return pawn;
        }

        // A colonist who can haul. A random colonist can have Hauling disabled. Then SetPriority for Hauling fails, and the scenario's haul never
        // starts. The helper spawns again until the colonist can haul, and destroys each colonist it does not keep.
        private static Pawn SpawnHaulerAt(Map map, IntVec3 cell)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                Pawn candidate = SpawnPawnAt(map, cell);
                if (!candidate.WorkTypeIsDisabled(WorkTypeDefOf.Hauling))
                {
                    return candidate;
                }
                candidate.Destroy(DestroyMode.Vanish);
            }
            throw new InvalidOperationException("no colonist who can haul could be placed at " + cell);
        }

        // A job that hauls a thing lying on the map. The scenario's surface haul is any haul of a thing on the surface.
        private static bool HaulsOn(Job job, Map map)
        {
            return job != null && job.targetA.Thing != null && job.targetA.Thing.Map == map;
        }

        private static string DescribeJob(Job job)
        {
            return job == null ? "no job" : job.def.defName + " for " + job.targetA.Thing + " on " + job.targetA.Thing?.Map;
        }

        // Task 7 (idea 5 on docs/mod-comparison/ideas.html). The stairs float menu offers "Work on this floor (100 s)". Its order locks the
        // selected colonists to the floor of the stairs for 6000 ticks. The scenario reads the lock at 5999 and 6000 ticks after the order.
        private void RunWorkOnFloorOrderCheck()
        {
            try
            {
                Map surface = Find.CurrentMap;
                ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
                Building_StairsUp stairs = SetUpLowerStairs(surface, tracker, out Map lower);
                // The view is on the lower floor, where the colonist stands and the stairs are clicked.
                Current.Game.CurrentMap = lower;
                IntVec3 colonistCell = FindReachableCellNear(lower, stairs.Position + new IntVec3(1, 0, 0), new List<IntVec3>());
                if (!colonistCell.IsValid)
                {
                    throw new InvalidOperationException("no cell the lower stairs can reach for the colonist");
                }
                Pawn colonist = SpawnPawnAt(lower, colonistCell);
                List<FloatMenuOption> options = FloatMenuMakerMap.GetOptions(new List<Pawn> { colonist }, stairs.Position.ToVector3Shifted(), out FloatMenuContext context);
                FloatMenuOption order = options?.FirstOrDefault(option => option.Label == "Work on this floor (100 s)");
                bool offered = order != null;
                Record("stairs float menu offers the order", offered ? "PASS" : "FAIL: the stairs float menu for the colonist offers no " + "Work on this floor (100 s)" + "; the menu has " + (options == null ? 0 : options.Count) + " options");
                if (!offered)
                {
                    Record("order stores the floor index", "FAIL: no order to run");
                    Record("lock is active 5999 ticks after the order", "FAIL: no order to run");
                    Record("lock ends 6000 ticks after the order", "FAIL: no order to run");
                    Record("work on floor order", "FAIL: the stairs float menu offers no order");
                    Root.Shutdown();
                    return;
                }

                int orderTick = Find.TickManager.TicksGame;
                order.action();
                int lowerZ = tracker.GetZIndexFor(lower);
                int? stored = tracker.floorPolicy.LockedFloorIndex(colonist, orderTick);
                int? active = tracker.floorPolicy.LockedFloorIndex(colonist, orderTick + 5999);
                int? ended = tracker.floorPolicy.LockedFloorIndex(colonist, orderTick + 6000);
                bool storesFloor = stored == lowerZ;
                bool activeAt5999 = active == lowerZ;
                bool endsAt6000 = ended == null;
                Record("order stores the floor index", storesFloor ? "PASS" : "FAIL: the order stored floor " + (stored == null ? "none" : stored.Value.ToString()) + ", the stairs are on floor " + lowerZ);
                Record("lock is active 5999 ticks after the order", activeAt5999 ? "PASS" : "FAIL: the lock at 5999 ticks is " + (active == null ? "none" : active.Value.ToString()));
                Record("lock ends 6000 ticks after the order", endsAt6000 ? "PASS" : "FAIL: the lock at 6000 ticks is still floor " + ended);
                Record("work on floor order", storesFloor && activeAt5999 && endsAt6000 ? "PASS" : "FAIL: a row above failed");
            }
            catch (Exception exception)
            {
                Record("work on floor order", "FAIL: " + exception);
                Log.Error("Z-Levels work on floor order validation failed: " + exception);
            }
            Root.Shutdown();
        }

        // The lower level, the stairs between it and the surface, and an open area of radius 2.5 around the lower stairs. The stairs
        // come from SpawnStairsBelow, and the registry is refilled by one game-component tick (see the stay on floor scenario).
        private static Building_StairsUp SetUpLowerStairs(Map surface, ZLevelsManager tracker, out Map lower)
        {
            lower = CreateLevelBelow(tracker, surface);
            IntVec3 stairCell = FindOpenBlock(surface, 1, 1);
            if (!stairCell.IsValid)
            {
                throw new InvalidOperationException("no open cell on the surface for the stairs");
            }
            surface.terrainGrid.SetTerrain(stairCell, TerrainDefOf.Concrete);
            Building_StairsDown surfaceStairs = (Building_StairsDown)ThingMaker.MakeThing(ZLevelsDefOf.ZL_StairsDown, ThingDefOf.Steel);
            surfaceStairs.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(surfaceStairs, stairCell, surface);
            ClearCell(lower, stairCell);
            lower.terrainGrid.SetTerrain(stairCell, TerrainDefOf.Concrete);
            tracker.SpawnStairsBelow(SpawnRestPawn(surface), stairCell, lower);
            Building_StairsUp stairs = lower.listerThings.AllThings.OfType<Building_StairsUp>().FirstOrDefault();
            if (stairs == null)
            {
                throw new InvalidOperationException("the lower level has no stairs up after SpawnStairsBelow");
            }
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(stairs.Position, 2.5f, true))
            {
                if (cell == stairs.Position || !cell.InBounds(lower)) continue;
                ClearCell(lower, cell);
                lower.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete);
            }
            tracker.GameComponentTick();
            return stairs;
        }

        // Task 8 (idea "Needs get a cool-down" on docs/mod-comparison/ideas.html). A colonist is redirected for food or fun at most
        // once per 2000 ticks, and a very hungry colonist is never held back. A search counts as a redirect when its job, or a job
        // the search leaves in the colonist's chain, works on the lower map. The scenario sets the game clock with DebugSetTicksGame.
        private void RunNeedCooldownCheck()
        {
            try
            {
                Map surface = Find.CurrentMap;
                ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
                Building_StairsUp stairs = SetUpLowerStairs(surface, tracker, out Map lower);
                CarveAround(lower, stairs.Position, 5f);
                // The joy search skips fogged things (JoyGiver_Ingest.CanIngestForJoy), and the lower level has never been seen.
                lower.fogGrid.ClearAllFog();
                DestroySurfaceFood(surface);
                int start = Find.TickManager.TicksGame;
                List<IntVec3> avoidSurface = new List<IntVec3>();
                List<IntVec3> avoidLower = new List<IntVec3>();

                Pawn eater = SpawnColonistNear(surface, stairs.Position, avoidSurface);
                // A food job giver with the Hungry minimum. The think tree's first food giver can be a duty's Starving one.
                JobGiver_GetFood foodGiver = new JobGiver_GetFood();
                typeof(JobGiver_GetFood).GetField("minCategory", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .SetValue(foodGiver, HungerCategory.Hungry);
                bool firstFood = TryFoodSearch(eater, foodGiver, tracker, lower, stairs, avoidLower, start + 100, 0.2f);
                Record("first food redirect is allowed", firstFood ? "PASS" : "FAIL: the first food search did not reach the lower map");
                bool secondFood = TryFoodSearch(eater, foodGiver, tracker, lower, stairs, avoidLower, start +200, 0.2f);
                Record("second food redirect within 2000 ticks is refused", !secondFood ? "PASS" : "FAIL: the second food search reached the lower map 100 ticks after the first");
                bool urgentFood = TryFoodSearch(eater, foodGiver, tracker, lower, stairs, avoidLower, start +300, 0.1f);
                Record("very hungry colonist is redirected at once", urgentFood ? "PASS" : "FAIL: the very hungry search did not reach the lower map");
                bool laterFood = TryFoodSearch(eater, foodGiver, tracker, lower, stairs, avoidLower, start +2400, 0.2f);
                Record("redirect after 2000 ticks is allowed", laterFood ? "PASS" : "FAIL: the food search 2100 ticks after the urgent redirect did not reach the lower map");

                Pawn fun = SpawnColonistNear(surface, stairs.Position, avoidSurface);
                // The base joy job giver. A think tree can hold JobGiver_GetJoyInBed, which overrides TryGiveJob and so skips the patch.
                JobGiver_GetJoy joyGiver = new JobGiver_GetJoy();
                joyGiver.ResolveReferences();
                SpawnChocolateNear(lower, stairs.Position, avoidLower);
                bool firstJoy = TryJoyUntilLower(fun, joyGiver, tracker, lower, start + 3000);
                Record("first joy redirect is allowed", firstJoy ? "PASS" : "FAIL: no joy search in 60 tries reached the lower map");
                bool secondJoy = TryJoyOnce(fun, joyGiver, tracker, lower, start + 3100);
                Record("second joy redirect within 2000 ticks is refused", !secondJoy ? "PASS" : "FAIL: the second joy search reached the lower map 100 ticks after the first");
                SpawnChocolateNear(lower, stairs.Position, avoidLower);
                bool laterJoy = TryJoyUntilLower(fun, joyGiver, tracker, lower, start + 5100);
                Record("joy redirect after 2000 ticks is allowed", laterJoy ? "PASS" : "FAIL: no joy search 2100 ticks after the first redirect reached the lower map in 60 tries");

                bool all = firstFood && !secondFood && urgentFood && laterFood && firstJoy && !secondJoy && laterJoy;
                Record("need cool-down", all ? "PASS" : "FAIL: a row above failed");
            }
            catch (Exception exception)
            {
                Record("need cool-down", "FAIL: " + exception);
                Log.Error("Z-Levels need cool-down validation failed: " + exception);
            }
            Root.Shutdown();
        }

        // One food search at a food level and a game tick, with a fresh stack of meals on the lower level. A search that reaches the
        // lower map is a redirect.
        private static bool TryFoodSearch(Pawn pawn, JobGiver_GetFood giver, ZLevelsManager tracker, Map lower, Building_StairsUp stairs,
            List<IntVec3> avoid, int tick, float foodLevel)
        {
            SpawnMealsNear(lower, stairs.Position, avoid);
            Find.TickManager.DebugSetTicksGame(tick);
            pawn.needs.food.CurLevelPercentage = foodLevel;
            if (tracker.jobTracker != null) tracker.jobTracker.Remove(pawn);
            Job job = InvokeTryGiveJob(giver, pawn);
            return ReachesMap(pawn, tracker, job, lower);
        }

        // Fun picks among the joy jobs of every allowed map, so one search can pick a surface job. The scenario searches again, up to 60
        // times, and stops at the first search that reaches the lower map.
        private static bool TryJoyUntilLower(Pawn pawn, JobGiver_GetJoy giver, ZLevelsManager tracker, Map lower, int tick)
        {
            for (int attempt = 0; attempt < 60; attempt++)
            {
                if (TryJoyOnce(pawn, giver, tracker, lower, tick)) return true;
            }
            return false;
        }

        // One joy search at a game tick. The joy level is 0.5, because a joy job giver returns nothing at 0.99 or more, and a fresh
        // colonist starts there.
        private static bool TryJoyOnce(Pawn pawn, JobGiver_GetJoy giver, ZLevelsManager tracker, Map lower, int tick)
        {
            Find.TickManager.DebugSetTicksGame(tick);
            pawn.needs.joy.CurLevelPercentage = 0.5f;
            if (tracker.jobTracker != null) tracker.jobTracker.Remove(pawn);
            Job job = InvokeTryGiveJob(giver, pawn);
            return ReachesMap(pawn, tracker, job, lower);
        }

        // True when the search's job, or a job the search left in the colonist's chain, works on the given map.
        private static bool ReachesMap(Pawn pawn, ZLevelsManager tracker, Job returned, Map map)
        {
            if (returned != null && returned.targetA.Thing != null && returned.targetA.Thing.Map == map) return true;
            if (tracker.jobTracker == null || !tracker.jobTracker.TryGetValue(pawn, out JobTracker chain) || chain.activeJobs == null)
            {
                return false;
            }
            return chain.activeJobs.Any(job => job.targetA.Thing != null && job.targetA.Thing.Map == map);
        }

        // The job giver's own TryGiveJob(Pawn). It is protected, so the scenario calls it by reflection; the Harmony prefix still runs.
        private static Job InvokeTryGiveJob(object giver, Pawn pawn)
        {
            System.Reflection.MethodInfo method = giver.GetType().GetMethod("TryGiveJob",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                null, new[] { typeof(Pawn) }, null);
            if (method == null)
            {
                throw new InvalidOperationException("no TryGiveJob(Pawn) on " + giver.GetType().Name);
            }
            return (Job)method.Invoke(giver, new object[] { pawn });
        }

        // Opens the rock within a radius of the lower stairs, so the scenario's food and table have room that a colonist can reach.
        private static void CarveAround(Map map, IntVec3 center, float radius)
        {
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, radius, true))
            {
                if (!cell.InBounds(map) || cell == center) continue;
                ClearCell(map, cell);
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete);
            }
        }

        // Opens the cell that a reachable search starts from. CarveAround keeps its centre, and a rock centre has no reachable cell.
        private static void OpenSearchOrigin(Map map, IntVec3 origin)
        {
            ClearCell(map, origin);
            map.terrainGrid.SetTerrain(origin, TerrainDefOf.Concrete);
        }

        private static void DestroySurfaceFood(Map map)
        {
            foreach (Thing thing in map.listerThings.AllThings.Where(t => t.def.category == ThingCategory.Item && t.def.IsIngestible).ToList())
            {
                thing.Destroy();
            }
        }

        private static Pawn SpawnColonistNear(Map map, IntVec3 near, List<IntVec3> avoid)
        {
            IntVec3 cell = FindReachableCellNear(map, near + new IntVec3(1, 0, 0), avoid);
            if (!cell.IsValid)
            {
                throw new InvalidOperationException("no reachable cell near the stairs for a colonist");
            }
            avoid.Add(cell);
            return SpawnPawnAt(map, cell);
        }

        private static void SpawnMealsNear(Map map, IntVec3 near, List<IntVec3> avoid)
        {
            IntVec3 cell = FindReachableCellNear(map, near + new IntVec3(2, 0, 0), avoid);
            if (!cell.IsValid)
            {
                throw new InvalidOperationException("no reachable cell on the lower level for a meal stack");
            }
            avoid.Add(cell);
            Thing meals = ThingMaker.MakeThing(ThingDefOf.MealSimple);
            meals.stackCount = 10;
            GenSpawn.Spawn(meals, cell, map);
        }

        private static void SpawnChocolateNear(Map map, IntVec3 near, List<IntVec3> avoid)
        {
            IntVec3 cell = FindReachableCellNear(map, near + new IntVec3(-2, 0, 0), avoid);
            if (!cell.IsValid)
            {
                throw new InvalidOperationException("no reachable cell on the lower level for chocolate");
            }
            avoid.Add(cell);
            Thing chocolate = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Chocolate"));
            chocolate.stackCount = 10;
            GenSpawn.Spawn(chocolate, cell, map);
        }

        // Task 9 (idea "unlimited idle searches" on docs/mod-comparison/ideas.html). An idle colonist whose work search finds nothing waits
        // 600 ticks before its next search. The scenario runs a search every 10 ticks for 1500 ticks and counts the searches that run.
        private void RunIdleSearchPauseCheck()
        {
            try
            {
                Map surface = Find.CurrentMap;
                ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
                // The pause limits the cross-floor search, so the colony needs a second floor for the pause to count anything. On one floor the
                // vanilla search runs instead (Task 16), and this scenario would count no searches.
                CreateLevelBelow(tracker, surface);
                Pawn idle = SpawnRestPawn(surface);
                idle.workSettings.DisableAll();
                int start = Find.TickManager.TicksGame;
                int before = tracker.floorPolicy.SearchCount(idle);
                bool foundWork = false;
                for (int step = 0; step <= 150; step++)
                {
                    Find.TickManager.DebugSetTicksGame(start + step * 10);
                    if (SearchWorkJob(idle) != null) foundWork = true;
                }
                int searches = tracker.floorPolicy.SearchCount(idle) - before;
                bool noWork = !foundWork;
                bool searched = searches >= 1;
                bool atMostThree = searches <= 3;
                Record("the idle colonist finds no work", noWork ? "PASS" : "FAIL: a search found work for a colonist with every work type off");
                Record("the idle colonist is searched", searched ? "PASS" : "FAIL: no search ran, so a count of 0 proves nothing");
                Record("an idle colonist searches at most 3 times in 1500 ticks", atMostThree ? "PASS: " + searches + " searches ran" : "FAIL: " + searches + " searches ran in 1500 ticks");
                // Rows 4 and 5. The pause keeps the own floor's settings. Each colonist does one search that finds nothing, which starts the pause, and
                // then a paused search. Row 4: the surface does no hauling. Row 5: the colonist is locked to the level below.
                Map belowSurface = tracker.GetLowerLevel(surface.Tile, surface) ?? throw new InvalidOperationException("no level below the surface");
                // ClearCell must clear a natural steam geyser. Destroy refuses a geyser and logs an error, and the geyser stays on its cell. The run
                // log gate fails the run on that error. This check puts a geyser in a cleared disc, then clears its cell.
                IntVec3 geyserCell = OnMapCell(surface, new IntVec3(surface.Size.x / 2 - 20, 0, surface.Size.z / 2 + 20));
                CarveAround(surface, geyserCell, 3);
                ClearCell(surface, geyserCell);
                Thing geyser = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("SteamGeyser"));
                GenSpawn.Spawn(geyser, geyserCell, surface);
                ClearCell(surface, geyserCell);
                Record("ClearCell clears a steam geyser from its cell", geyser.Spawned ? "FAIL: the geyser is still on the map" : "PASS");
                IntVec3 pauseBlock = FindCentralOpenBlock(surface, 9, 9);
                if (!pauseBlock.IsValid)
                {
                    throw new InvalidOperationException("no open 9 by 9 block on the surface away from the edges");
                }
                MakeSupplyZone(surface, pauseBlock + new IntVec3(4, 0, 4));
                ClearCell(surface, pauseBlock);
                Thing pauseSteel = ThingMaker.MakeThing(ThingDefOf.Steel);
                pauseSteel.stackCount = 10;
                GenSpawn.Spawn(pauseSteel, pauseBlock, surface);
                int surfaceIndex = tracker.GetZIndexFor(surface);
                int belowIndex = tracker.GetZIndexFor(belowSurface);
                List<IntVec3> noAvoid = new List<IntVec3>();
                Pawn hauler = SpawnCapableColonistNear(surface, pauseBlock + new IntVec3(6, 0, 6), noAvoid, WorkTypeDefOf.Hauling);
                SetWork(hauler, WorkTypeDefOf.Hauling, 3, WorkTypeDefOf.Mining, 0);
                tracker.floorPolicy.SetWorkAllowed(WorkTypeDefOf.Hauling, surfaceIndex, false);
                Job haulerFirst = SearchWorkJob(hauler);
                Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + 10);
                Job haulerPaused = SearchWorkJob(hauler);
                tracker.floorPolicy.SetWorkAllowed(WorkTypeDefOf.Hauling, surfaceIndex, true);
                bool pausedHaulOk = haulerFirst == null && haulerPaused == null;
                Record("a paused search keeps the floor's work settings", pausedHaulOk ? "PASS" : "FAIL: the paused search gave " + DescribeOwnFloorJob(haulerPaused) + " on a floor that does not do hauling");

                Pawn locked = SpawnCapableColonistNear(surface, pauseBlock + new IntVec3(2, 0, 6), noAvoid, WorkTypeDefOf.Hauling);
                SetWork(locked, WorkTypeDefOf.Hauling, 3, WorkTypeDefOf.Mining, 0);
                tracker.floorPolicy.SetLock(locked, belowIndex, Find.TickManager.TicksGame + 100000);
                Job lockedFirst = SearchWorkJob(locked);
                Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + 10);
                Job lockedPaused = SearchWorkJob(locked);
                bool pausedLockOk = lockedFirst == null && lockedPaused == null;
                Record("a paused search keeps a floor lock", pausedLockOk ? "PASS" : "FAIL: the paused search gave " + DescribeOwnFloorJob(lockedPaused) + " while the colonist is locked below");
                // Row 6, a paused search and storage on another floor. The colonist is on the level below, with a book. A stair pair joins the two floors, so the
                // floors are connected from the start. The first search must find nothing, so the pause starts, and the surface stockpiles that accept the book
                // are held back for that one search. Then a bookcase on the surface, at a higher priority, and a stockpile on the level below are built. The
                // paused search runs only on the colonist's own floor, and the own-floor gate is meant to keep it away from the surface storage. This row does
                // not check that the surface storage is reachable from the colonist. The storage checks still pass with the gate removed, so this row does not
                // prove the gate's own effect on storage.
                belowSurface.fogGrid.ClearAllFog();
                // The level below is sealed rock until it is carved, so the colonist, the book and the stockpile get a clear disc first.
                IntVec3 lowerSpot = new IntVec3(belowSurface.Size.x / 2, 0, belowSurface.Size.z / 2);
                CarveAround(belowSurface, lowerSpot, 12);
                Thing novel = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Novel"));
                novel.stackCount = 1;
                GenSpawn.Spawn(novel, FindReachableCellNear(belowSurface, lowerSpot + new IntVec3(2, 0, 2), noAvoid), belowSurface);
                Pawn bookHauler = SpawnCapableColonistNear(belowSurface, lowerSpot + new IntVec3(3, 0, 3), noAvoid, WorkTypeDefOf.Hauling);
                SetWork(bookHauler, WorkTypeDefOf.Hauling, 3, WorkTypeDefOf.Mining, 0);
                // Surface hauling is off for the first search. The setting governs the hauling givers on the surface floor, so that search does not look for
                // hauling there. It does not keep a surface stockpile out as a destination. The hold-back below does that.
                tracker.floorPolicy.SetWorkAllowed(WorkTypeDefOf.Hauling, surfaceIndex, false);
                // A stair pair between this floor and the surface, eight cells across and six up from the centre. It is built before the first search, so the
                // first search runs on the same connected floors as the paused search. Once the floors connect, a surface stockpile takes the book. The stockpiles
                // that accept the book are held back for that one search, and they are allowed again after it, even if the search throws.
                IntVec3 bookStairs = lowerSpot + new IntVec3(-8, 0, -6);
                CarveAround(surface, bookStairs, 6);
                CarveAround(belowSurface, bookStairs, 6);
                SpawnStairPair(tracker, surface, belowSurface, bookStairs);
                tracker.GameComponentTick();
                List<Zone_Stockpile> bookHeldBack = surface.zoneManager.AllZones.OfType<Zone_Stockpile>().Where(z => z.settings.AllowedToAccept(novel)).ToList();
                foreach (Zone_Stockpile bookHeldZone in bookHeldBack) bookHeldZone.settings.filter.SetAllow(novel.def, false);
                Job bookFirst;
                try
                {
                    bookFirst = SearchWorkJob(bookHauler);
                }
                finally
                {
                    foreach (Zone_Stockpile bookHeldZone in bookHeldBack) bookHeldZone.settings.filter.SetAllow(novel.def, true);
                }
                Record("INFO: the colonist's map after the first search", "INFO: " + bookHauler.Map + ", at " + bookHauler.Position + ", surface stockpiles held back from the book " + bookHeldBack.Count);
                Record("INFO: surface stockpiles that accept the book after the first search", "INFO: " + bookHeldBack.Count(z => z.settings.AllowedToAccept(novel)) + " of " + bookHeldBack.Count);
                string firstDestination = bookFirst == null ? "no job" : !bookFirst.targetB.Cell.IsValid ? "no cell" : "cell " + bookFirst.targetB.Cell + ", surface zone " + (surface.zoneManager.ZoneAt(bookFirst.targetB.Cell)?.ToString() ?? "none") + ", lower zone " + (belowSurface.zoneManager.ZoneAt(bookFirst.targetB.Cell)?.ToString() ?? "none");
                Record("INFO: the first search's destination", "INFO: " + firstDestination);
                // The first search must find nothing, so the idle pause starts.
                Record("the first search finds nothing, so the pause starts", bookFirst == null ? "PASS" : "FAIL: the first search found " + DescribeOwnFloorJob(bookFirst));
                ThingDef bookcaseDef = DefDatabase<ThingDef>.GetNamed("Bookcase");
                Building_Bookcase bookcase = (Building_Bookcase)ThingMaker.MakeThing(bookcaseDef, GenStuff.DefaultStuffFor(bookcaseDef));
                bookcase.SetFaction(Faction.OfPlayer);
                IntVec3 bookcaseCell = bookStairs + new IntVec3(2, 0, 2);
                ClearCell(surface, bookcaseCell);
                GenSpawn.Spawn(bookcase, bookcaseCell, surface, Rot4.North, WipeMode.VanishOrMoveAside);
                bookcase.GetStoreSettings().Priority = StoragePriority.Important;
                Zone_Stockpile lowerZone = MakeSupplyZone(belowSurface, lowerSpot + new IntVec3(-1, 0, -1));
                lowerZone.settings.Priority = StoragePriority.Normal;
                Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + 10);
                // The paused search runs only on the colonist's own floor, so it does not read the surface's hauling setting. The surface stockpiles were held back
                // only for the first search, so they are candidates for the paused search, and the own-floor gate is meant to keep it out of them. The storage
                // checks still pass with the gate removed, so this row does not prove that the gate does.
                // The surface's hauling setting goes back on here, for the rows after this one.
                tracker.floorPolicy.SetWorkAllowed(WorkTypeDefOf.Hauling, surfaceIndex, true);
                Map pausedStartMap = bookHauler.Map;
                Job bookPaused = null;
                // The cross-floor searches that the paused search starts, read only. The storage check of a thing on a floor is one of them.
                int bookPausedCrossFloor = CrossFloorSearchesDuring(() => { bookPaused = SearchWorkJob(bookHauler); });
                bool onSurface = bookPaused != null && bookPaused.targetB.Thing != null && bookPaused.targetB.Thing.Map == surface;
                Record("the book search before the pause", "INFO: " + DescribeOwnFloorJob(bookFirst));
                Record("the stockpile below", "INFO: " + lowerZone.CellCount + " cells, accepts the book " + lowerZone.settings.AllowedToAccept(novel));
                Record("the bookcase on the surface", "INFO: accepts the book " + bookcase.Accepts(novel) + ", priority " + bookcase.GetStoreSettings().Priority + ", on " + bookcase.Map);
                Record("the paused search job", "INFO: " + DescribeOwnFloorJob(bookPaused) + ", on the surface " + onSurface);
                Record("INFO: the colonist's map around the paused search", "INFO: before " + pausedStartMap + ", after " + bookHauler.Map);
                // Diagnostic, read only: the destination of the paused job, the zones of each floor at that cell, the colonist's map, and the map its job tracker names.
                IntVec3 pausedCell = bookPaused != null && bookPaused.targetB.Thing == null ? bookPaused.targetB.Cell : IntVec3.Invalid;
                string pausedZones = pausedCell.IsValid
                    ? "surface zone " + (surface.zoneManager.ZoneAt(pausedCell)?.ToString() ?? "none") + ", lower zone " + (belowSurface.zoneManager.ZoneAt(pausedCell)?.ToString() ?? "none")
                    : "no cell";
                Record("INFO: the paused job's destination", "INFO: " + (bookPaused == null ? "no job" : "cell " + pausedCell) + ", " + pausedZones + ", colonist on " + bookHauler.Map + ", job tracker names " + MapDestOf(tracker, bookHauler));
                Record("the cross-floor searches the paused storage search starts", "INFO: " + bookPausedCrossFloor + " (counts FloorPolicy.Allowed calls and storage checks together; zero shows neither ran, a count above zero does not show which)");
                // A haul to a cell names no thing. A cell of the surface's stockpile, which is no stockpile of this floor, is a haul to another floor.
                bool onSurfaceCell = bookPaused != null && bookPaused.targetB.Thing == null && surface.zoneManager.ZoneAt(bookPaused.targetB.Cell) != null
                    && belowSurface.zoneManager.ZoneAt(bookPaused.targetB.Cell) == null;
                bool pausedStorageOk = bookFirst == null && !onSurface && !onSurfaceCell;
                Record("a paused search does not send a haul to storage on another floor", pausedStorageOk ? "PASS" : "FAIL: the paused search gave " + DescribeOwnFloorJob(bookPaused) + ", with its destination on the surface");
                // The paused search still finds the book's haul to this floor's stockpile. Without this row the rows above would pass when the search finds nothing.
                // The haul must be the book's own HaulToCell job, to a cell with no thing, that the colonist on this floor holds, to this floor's stockpile, with no zone
                // of the surface at that cell. The helper IsBookHaulToOwnStockpile checks each of those.
                bool toOwnStockpile = IsBookHaulToOwnStockpile(bookPaused, novel, lowerZone, belowSurface, bookHauler, surface);
                Record("a paused search keeps the book's haul on this floor's stockpile", toOwnStockpile ? "PASS" : "FAIL: the paused search gave " + DescribeOwnFloorJob(bookPaused) + ", not a haul to the stockpile on this floor");
                // The check must tell the book's haul from another item's haul to the same cell. The cell-only check that row 6 used before accepts that other haul.
                Job otherHaul = JobMaker.MakeJob(JobDefOf.HaulToCell, ThingMaker.MakeThing(ThingDefOf.Steel), lowerZone.cells[0]);
                bool otherItemRejected = !IsBookHaulToOwnStockpile(otherHaul, novel, lowerZone, belowSurface, bookHauler, surface);
                bool cellOnlyAcceptsOther = otherHaul.targetB.Thing == null && belowSurface.zoneManager.ZoneAt(otherHaul.targetB.Cell) == lowerZone;
                Record("the book check rejects another item's haul to this floor's stockpile", otherItemRejected ? "PASS" : "FAIL: the check accepts another item's haul to this floor's stockpile");
                Record("INFO: the cell-only check on the same other-item haul", "INFO: accepts " + cellOnlyAcceptsOther + ", the book check accepts " + !otherItemRejected);

                // Row 7, a paused search and food for an animal, with the food on another floor (S10). This row makes a stair pair between the surface and the level
                // below, nine cells from the surface centre, so the food check can walk to the stairs. The colonist and a tame monkey stand beside the stairs. The
                // monkey is set to learn obedience, so its training job needs food for it, and the colonist has the animals skill that the job asks for. Raw berries
                // are the food: the food search takes foods up to the raw tasty preference, and berries are one. The first search finds no food, so the pause starts.
                // Then berries are built on the level below, and the paused search must not take them. Row 8 puts berries on the surface, and the paused search takes
                // those.
                IntVec3 foodStairs = surface.Center + new IntVec3(-9, 0, 0);
                CarveAround(surface, foodStairs, 6);
                CarveAround(belowSurface, foodStairs, 6);
                SpawnStairPair(tracker, surface, belowSurface, foodStairs);
                tracker.GameComponentTick();
                Pawn feeder = SpawnCapableColonistNear(surface, foodStairs + new IntVec3(2, 0, 2), noAvoid, WorkTypeDefOf.Handling);
                SetWork(feeder, WorkTypeDefOf.Handling, 3, WorkTypeDefOf.Mining, 0);
                feeder.skills.GetSkill(SkillDefOf.Animals).Level = 12;
                Pawn monkey = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("Monkey"), Faction.OfPlayer);
                GenSpawn.Spawn(monkey, FindReachableCellNear(surface, foodStairs + new IntVec3(3, 0, 4), noAvoid), surface);
                monkey.training.SetWantedRecursive(TrainableDefOf.Obedience, true);
                TrainableDef monkeyTrainable = monkey.training.NextTrainableToTrain();
                Job feederFirst = SearchWorkJob(feeder);
                Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + 10);
                Thing lowerBerries = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("RawBerries"));
                lowerBerries.stackCount = 50;
                GenSpawn.Spawn(lowerBerries, FindReachableCellNear(belowSurface, lowerSpot + new IntVec3(-3, 0, 3), noAvoid), belowSurface);
                Job feederPaused = SearchWorkJob(feeder);
                Record("the monkey's next trainable", "INFO: " + (monkeyTrainable == null ? "none" : monkeyTrainable.defName) + ", the first search gave " + DescribeOwnFloorJob(feederFirst));
                bool pausedFoodOk = feederFirst == null && feederPaused == null;
                Record("a paused search does not take food for an animal from another floor", pausedFoodOk ? "PASS" : "FAIL: the paused search gave " + DescribeOwnFloorJob(feederPaused) + ", with the food on the level below");
                // Diagnostics for the food check of the paused search, read only apart from the colonist's cell, which is put back. The check visits the other floors
                // from the colonist's cell, and the food search runs on each one. Here: the floors the check visits, and the best food on the level below, found
                // from the arrival there with the same arguments the check uses.
                IntVec3 feederSpot = feeder.Position;
                string visitedFloors = string.Join(", ", ClosestOrderMaps(feeder, surface, feederSpot).Select(map => tracker.GetZIndexFor(map).ToString()));
                TeleportThingMethod().Invoke(null, new object[] { feeder, belowSurface, foodStairs });
                ThingDef lowerFoodDef;
                Thing lowerFood = FoodUtility.BestFoodSourceOnMap(feeder, monkey, desperate: false, out lowerFoodDef, FoodPreferability.RawTasty, allowPlant: false, allowDrug: false, allowCorpse: false, allowDispenserFull: false, allowDispenserEmpty: false, allowForbidden: false, allowSociallyImproper: false, allowHarvest: false, forceScanWholeMap: false, ignoreReservations: false, calculateWantedStackCount: false, FoodPreferability.Undefined, JobDriver_InteractAnimal.RequiredNutritionPerFeed(monkey) * 2f * 4f);
                TeleportThingMethod().Invoke(null, new object[] { feeder, surface, feederSpot });
                Record("the food check from the colonist's cell", "INFO: visits the floors with zIndex " + visitedFloors + ", the best food from the level below's arrival " + (lowerFood == null ? "none" : lowerFood.ToString() + " on " + lowerFood.Map) + ", the colonist's cell " + feederSpot + " on " + feeder.Map);
                Thing surfaceBerries = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("RawBerries"));
                surfaceBerries.stackCount = 50;
                GenSpawn.Spawn(surfaceBerries, FindReachableCellNear(surface, foodStairs + new IntVec3(4, 0, -2), noAvoid), surface);
                Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + 10);
                Job feederOwn = SearchWorkJob(feeder);
                bool pausedFoodOwnOk = feederOwn != null && feederOwn.targetA.Thing == surfaceBerries;
                Record("a paused search takes food for an animal from this floor", pausedFoodOwnOk ? "PASS" : "FAIL: the paused search gave " + DescribeOwnFloorJob(feederOwn) + ", not the berries on the surface");
                // Diagnostics, read only: each gate the training job passes before it asks for food, for the colonist and the monkey as they stand now.
                WorkGiver_Train trainGiver = feeder.workSettings.WorkGiversInOrderNormal.OfType<WorkGiver_Train>().FirstOrDefault();
                bool canInteract = WorkGiver_InteractAnimal.CanInteractWithAnimal(feeder, monkey, out string interactReason, false);
                Job trainJob = trainGiver == null ? null : trainGiver.JobOnThing(feeder, monkey, false);
                Record("the training job's gates for the monkey", "INFO: giver " + (trainGiver != null) + ", can interact " + canInteract + " (" + interactReason + "), awake " + monkey.Awake() + ", handling skill needed " + TrainableUtility.MinimumHandlingSkill(monkey) + " against the colonist's " + feeder.skills.GetSkill(SkillDefOf.Animals).Level + ", eats the berries " + monkey.WillEat(surfaceBerries, feeder) + ", trained too recently " + TrainableUtility.TrainedTooRecently(monkey) + ", the job " + DescribeOwnFloorJob(trainJob));

                // Row 9, a marked item from another floor (S11). The surface's haulables and the berries of the rows above are cleared first, so the colonist's first
                // search finds nothing and the pause starts. Then berries on the level below are marked for the surface, and the paused search must not haul them up.
                // Row 10 advances the clock past the pause, and the normal search then hauls the same berries.
                foreach (Thing haulable in surface.listerHaulables.ThingsPotentiallyNeedingHauling().ToList())
                {
                    haulable.Destroy(DestroyMode.Vanish);
                }
                foreach (Thing lowerHaulable in belowSurface.listerHaulables.ThingsPotentiallyNeedingHauling().ToList())
                {
                    lowerHaulable.Destroy(DestroyMode.Vanish);
                }
                foreach (Thing food in new[] { lowerBerries, surfaceBerries, novel })
                {
                    if (food.Spawned)
                    {
                        food.Destroy(DestroyMode.Vanish);
                    }
                }
                Pawn markedHauler = SpawnCapableColonistNear(surface, pauseBlock + new IntVec3(2, 0, 6), noAvoid, WorkTypeDefOf.Hauling);
                SetWork(markedHauler, WorkTypeDefOf.Hauling, 3, WorkTypeDefOf.Mining, 0);
                Job markedFirst = SearchWorkJob(markedHauler);
                Thing markedBerries = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("RawBerries"));
                markedBerries.stackCount = 50;
                GenSpawn.Spawn(markedBerries, FindReachableCellNear(belowSurface, lowerSpot + new IntVec3(-3, 0, -3), noAvoid), belowSurface);
                ItemFloorMarks.Mark(markedBerries, surfaceIndex);
                Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + 10);
                Job markedPaused = SearchWorkJob(markedHauler);
                bool pausedMarkOk = markedFirst == null && markedPaused == null;
                Record("a paused search does not haul an item marked for this floor from another floor", pausedMarkOk ? "PASS" : "FAIL: the first search gave " + DescribeOwnFloorJob(markedFirst) + ", the paused search gave " + DescribeOwnFloorJob(markedPaused) + ", with the berries on the level below");
                // The haul candidates of the paused search, listed with the own-floor state set the way SearchOwnFloorOnly sets it. The marked berries of the level
                // below must not be among them. The control is the same list outside the pause, which must include them, so the first check cannot pass on an empty list.
                WorkGiver_Haul markedGiver = markedHauler.workSettings.WorkGiversInOrderNormal.OfType<WorkGiver_Haul>().FirstOrDefault();
                bool candidatesPausedOk = markedGiver != null && !PausedHaulCandidates(markedGiver, markedHauler, surface).Contains(markedBerries);
                bool candidatesNormalOk = markedGiver != null && markedGiver.PotentialWorkThingsGlobal(markedHauler).Contains(markedBerries);
                Record("the paused search's haul candidates leave out an item marked from another floor", candidatesPausedOk ? "PASS" : "FAIL: the paused search's haul candidates include the marked berries on the level below");
                Record("the haul candidates outside the pause include the marked berries", candidatesNormalOk ? "PASS" : "FAIL: the candidates outside the pause leave out the marked berries");
                AdvanceClockPastIdlePause();
                Job markedNormal = SearchWorkJob(markedHauler);
                bool normalMarkOk = markedNormal != null && markedNormal.targetA.Thing == markedBerries;
                Record("the same marked item is hauled once the pause is over", normalMarkOk ? "PASS" : "FAIL: the normal search gave " + DescribeOwnFloorJob(markedNormal) + ", not the berries");

                Record("idle search pause", noWork && searched && atMostThree && pausedHaulOk && pausedLockOk && pausedStorageOk && toOwnStockpile && otherItemRejected && pausedFoodOk && pausedFoodOwnOk && pausedMarkOk && candidatesPausedOk && candidatesNormalOk && normalMarkOk ? "PASS" : "FAIL: a row above failed");
            }
            catch (Exception exception)
            {
                Record("idle search pause", "FAIL: " + exception);
                Log.Error("Z-Levels idle search pause validation failed: " + exception);
            }
            Root.Shutdown();
        }

        // Task 10 (idea "someone is already going" on docs/mod-comparison/ideas.html). A colonist's reserved targets are claims that block
        // other colonists. A claim ends with its job, and it ends with its map. The scenario reads the claims the way the reservation check does.
        private void RunStaleClaimsCheck()
        {
            try
            {
                Map surface = Find.CurrentMap;
                ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
                IntVec3 steelCell = FindOpenBlock(surface, 1, 1);
                if (!steelCell.IsValid)
                {
                    throw new InvalidOperationException("no open cell on the surface for the steel");
                }
                Thing steel = ThingMaker.MakeThing(ThingDefOf.Steel);
                steel.stackCount = 20;
                GenSpawn.Spawn(steel, steelCell, surface);
                IntVec3 dest = FindReachableCellNear(surface, steelCell + new IntVec3(3, 0, 0), new List<IntVec3> { steelCell });
                if (!dest.IsValid)
                {
                    throw new InvalidOperationException("no reachable cell on the surface for the haul");
                }
                // The claim blocks a colonist on another map, so the first colonist works on the lower level, and the second one stays on the surface.
                Building_StairsUp stairs = SetUpLowerStairs(surface, tracker, out Map lower);
                IntVec3 firstCell = FindReachableCellNear(lower, stairs.Position + new IntVec3(2, 0, 0), new List<IntVec3>());
                if (!firstCell.IsValid)
                {
                    throw new InvalidOperationException("no reachable cell on the lower level for the first colonist");
                }
                Pawn first = SpawnPawnAt(lower, firstCell);
                Pawn second = SpawnRestPawn(surface);
                Job haul = JobMaker.MakeJob(JobDefOf.HaulToCell, steel, dest);
                // The job is set as the colonist's current job without a driver. StartJob would replace it with the think tree's next job, and
                // that reset clears the claims, so the scenario sets the field directly.
                tracker.ReserveTargets(first, haul);
                first.jobs.curJob = haul;
                bool blockedBefore = !second.CanReserve(steel);
                Record("a second colonist is blocked while the claim is live", blockedBefore ? "PASS" : "FAIL: the second colonist could reserve the steel while the first holds the claim");

                first.jobs.EndCurrentJob(JobCondition.Incompletable, false);
                bool claimGone = !HasClaim(tracker, first, steel);
                Record("claim is released when its job ends", claimGone ? "PASS" : "FAIL: the claim is still held after the job ended");
                bool takeable = second.CanReserve(steel);
                Record("a second colonist takes the job after the release", takeable ? "PASS" : "FAIL: the second colonist is still blocked after the release");

                IntVec3 lowerCell = FindReachableCellNear(lower, stairs.Position + new IntVec3(2, 0, 0), new List<IntVec3> { firstCell });
                if (!lowerCell.IsValid)
                {
                    throw new InvalidOperationException("no reachable cell on the lower level for the steel");
                }
                Thing lowerSteel = ThingMaker.MakeThing(ThingDefOf.Steel);
                lowerSteel.stackCount = 20;
                GenSpawn.Spawn(lowerSteel, lowerCell, lower);
                IntVec3 lowerDest = FindReachableCellNear(lower, lowerCell + new IntVec3(1, 0, 0), new List<IntVec3> { lowerCell });
                if (!lowerDest.IsValid)
                {
                    throw new InvalidOperationException("no reachable cell on the lower level for the haul");
                }
                IntVec3 claimerCell = FindReachableCellNear(lower, stairs.Position + new IntVec3(-2, 0, 0), new List<IntVec3> { firstCell, lowerCell });
                if (!claimerCell.IsValid)
                {
                    throw new InvalidOperationException("no reachable cell on the lower level for the claimer");
                }
                Pawn claimer = SpawnPawnAt(lower, claimerCell);
                tracker.ReserveTargets(claimer, JobMaker.MakeJob(JobDefOf.HaulToCell, lowerSteel, lowerDest));
                bool claimBefore = HasClaim(tracker, claimer, lowerSteel);
                Record("the claim exists before its map is removed", claimBefore ? "PASS" : "FAIL: no claim was made for the steel on the lower level");

                Current.Game.DeinitAndRemoveMap(lower, false);
                bool claimAfter = HasClaim(tracker, claimer, lowerSteel);
                Record("claim is released when its map is removed", !claimAfter ? "PASS" : "FAIL: the claim is still held after the map was removed");

                bool all = blockedBefore && claimGone && takeable && claimBefore && !claimAfter;
                Record("stale claims", all ? "PASS" : "FAIL: a row above failed");
            }
            catch (Exception exception)
            {
                Record("stale claims", "FAIL: " + exception);
                Log.Error("Z-Levels stale claims validation failed: " + exception);
            }
            Root.Shutdown();
        }

        // Task 11 (idea "Always move the colonist back" on docs/mod-comparison/ideas.html). A medical search moves two colonists to the lower
        // level before it looks for medicine there. If the search fails at that point, both colonists must be back on their own floor and
        // cell. The scenario makes the search fail on the lower level with FloorPolicy.ThrowInSearchForTest. Medicine on the surface is
        // removed first, so the search does not stop on the surface before it reaches the lower level.
        private void RunFloorGuardCheck()
        {
            try
            {
                Map surface = Find.CurrentMap;
                ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
                foreach (Thing medicine in surface.listerThings.ThingsInGroup(ThingRequestGroup.Medicine).ToList())
                {
                    medicine.Destroy();
                }
                SetUpLowerStairs(surface, tracker, out Map lower);
                Pawn colonist = SpawnRestPawn(surface);
                Pawn patient = SpawnRestPawn(surface);
                IntVec3 colonistCell = colonist.Position;
                IntVec3 patientCell = patient.Position;

                Exception thrown = null;
                FloorPolicy.ThrowInSearchForTest = true;
                try
                {
                    JobPatches.TryIssueJobPackagePatch.TendJobOnThing(colonist, patient);
                }
                catch (Exception exception)
                {
                    thrown = exception;
                }
                finally
                {
                    FloorPolicy.ThrowInSearchForTest = false;
                }

                bool failedOnLower = thrown is InvalidOperationException && thrown.Message.Contains("another floor");
                Record("the medical search fails on another floor", failedOnLower ? "PASS" : "FAIL: " + (thrown == null ? "the search did not fail" : thrown.ToString()));
                bool colonistBack = colonist.Map == surface;
                Record("colonist is back on its own floor after a failed search", colonistBack ? "PASS" : "FAIL: the colonist is on " + colonist.Map + " at " + colonist.Position);
                bool colonistCellKept = colonist.Position == colonistCell;
                Record("colonist keeps its cell", colonistCellKept ? "PASS" : "FAIL: the colonist is at " + colonist.Position + ", not " + colonistCell);
                bool patientBack = patient.Map == surface && patient.Position == patientCell;
                Record("patient is back on its own floor and cell after a failed search", patientBack ? "PASS" : "FAIL: the patient is at " + patient.Position + " on " + patient.Map + ", not " + patientCell + " on the surface");
                bool all = failedOnLower && colonistBack && colonistCellKept && patientBack;
                Record("floor guard", all ? "PASS" : "FAIL: a row above failed");
            }
            catch (Exception exception)
            {
                Record("floor guard", "FAIL: " + exception);
                Log.Error("Z-Levels floor guard validation failed: " + exception);
            }
            Root.Shutdown();
        }

        // Task 12 (idea 7 on docs/mod-comparison/ideas.html). The haul search lists the storage of every floor of a tile. The list should be
        // built only when a storage is added, removed or re-prioritised. The scenario counts the builds over 200 lookups, and after each
        // change it compares the index with a list built the old way.
        private void RunStorageIndexCheck()
        {
            try
            {
                Map surface = Find.CurrentMap;
                ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
                SetUpLowerStairs(surface, tracker, out Map lower);
                PlanetTile tile = surface.Tile;
                MakeSupplyZone(surface, FindOpenBlock(surface, 1, 1));
                Zone_Stockpile lowerZone = MakeSupplyZone(lower, FindOpenBlock(lower, 1, 1));

                List<IHaulDestination> fresh = FreshDestinations(tracker, tile);
                int buildsBefore = StorageIndex.Rebuilds;
                IReadOnlyList<IHaulDestination> listed = null;
                for (int i = 0; i < 200; i++)
                {
                    listed = StorageIndex.Destinations(tile);
                }
                int builds = StorageIndex.Rebuilds - buildsBefore;
                Record("200 lookups rebuild the index at most once", builds <= 1 ? "PASS: " + builds + " builds for 200 lookups" : "FAIL: " + builds + " builds for 200 lookups");
                bool same = listed.SequenceEqual(fresh);
                Record("the index lists the same destinations as before", same ? "PASS" : "FAIL: the index differs from a list built the old way");

                int countBefore = fresh.Count;
                MakeSupplyZone(surface, FindOpenBlock(surface, 1, 1));
                List<IHaulDestination> freshAfterAdd = FreshDestinations(tracker, tile);
                bool addListed = StorageIndex.Destinations(tile).SequenceEqual(freshAfterAdd) && freshAfterAdd.Count > countBefore;
                Record("a storage added after the index was built is listed", addListed ? "PASS" : "FAIL: the index still has the old list (" + countBefore + " then " + freshAfterAdd.Count + " destinations)");

                lowerZone.Deregister();
                List<IHaulDestination> freshAfterRemove = FreshDestinations(tracker, tile);
                bool removeListed = StorageIndex.Destinations(tile).SequenceEqual(freshAfterRemove) && freshAfterRemove.Count < freshAfterAdd.Count;
                Record("a storage removed after the index was built is dropped", removeListed ? "PASS" : "FAIL: the index still lists the removed storage");

                bool all = builds <= 1 && same && addListed && removeListed;
                Record("storage index", all ? "PASS" : "FAIL: a row above failed");
            }
            catch (Exception exception)
            {
                Record("storage index", "FAIL: " + exception);
                Log.Error("Z-Levels storage index validation failed: " + exception);
            }
            Root.Shutdown();
        }

        // The list the old haul search built on each lookup, for comparison with the index.
        private static List<IHaulDestination> FreshDestinations(ZLevelsManager tracker, PlanetTile tile)
        {
            var destinations = new List<IHaulDestination>();
            foreach (Map map in tracker.GetAllMaps(tile))
            {
                destinations.AddRange(map.haulDestinationManager.AllHaulDestinationsListInPriorityOrder);
            }
            return destinations;
        }

        // Task 13 (idea "saved stair routes" on docs/mod-comparison/ideas.html). A colonist changes floor at the reachable stair with the shortest
        // walk, not the one nearest in a straight line. The nearest stair is walled in, a farther one lies behind a wall with a long walk
        // around it, and the last one has the shortest walk. The scenario checks the chosen stair, and it counts the walk checks of a second
        // decision from the same cell.
        private void RunStairRoutesCheck()
        {
            try
            {
                Map surface = Find.CurrentMap;
                ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
                Map lower = CreateLevelBelow(tracker, surface);
                IntVec3 center = new IntVec3(surface.Size.x / 2, 0, surface.Size.z / 2);
                CarveAround(surface, center, 14);
                CarveAround(lower, center, 14);
                IntVec3 start = center + new IntVec3(-8, 0, 0);
                IntVec3 blocked = center + new IntVec3(-5, 0, 0);
                IntVec3 far = center + new IntVec3(-2, 0, 0);
                IntVec3 near = center + new IntVec3(-8, 0, 8);
                for (int z = -10; z <= 9; z++)
                {
                    SpawnWallOn(lower, center + new IntVec3(-3, 0, z));
                }
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (dx != 0 || dz != 0)
                        {
                            SpawnWallOn(lower, blocked + new IntVec3(dx, 0, dz));
                        }
                    }
                }
                SpawnStairPair(tracker, surface, lower, blocked);
                SpawnStairPair(tracker, surface, lower, far);
                SpawnStairPair(tracker, surface, lower, near);
                tracker.GameComponentTick();
                Pawn colonist = SpawnPawnAt(lower, start);

                ChooseStairFloor(colonist, lower, start);
                bool shortestWalk = colonist.Map == surface && colonist.Position == near;
                Record("a farther stair with a shorter walk is picked", shortestWalk ? "PASS" : "FAIL: the colonist went to " + colonist.Position + " on " + colonist.Map + ", not " + near);
                bool reachable = colonist.Map == surface && (colonist.Position == far || colonist.Position == near);
                Record("a wall that blocks the nearest stair sends the colonist to the reachable one", reachable ? "PASS" : "FAIL: the colonist went to " + colonist.Position + " on " + colonist.Map);

                TeleportThingMethod().Invoke(null, new object[] { colonist, lower, start });
                int checksBefore = StairRouteCache.WalkChecks;
                ChooseStairFloor(colonist, lower, start);
                int checks = StairRouteCache.WalkChecks - checksBefore;
                Record("a second decision runs no walk check", checks == 0 ? "PASS" : "FAIL: " + checks + " walk checks in the second decision");

                // Row 3. A cell walled in on four sides has no reachable stair, so its decision is no route. Opening one side lets a walk reach a stair,
                // and a decision saved before the opening must expire. Otherwise the cell keeps no route for good.
                IntVec3 pocket = center + new IntVec3(6, 0, 0);
                foreach (IntVec3 side in new[] { new IntVec3(1, 0, 0), new IntVec3(-1, 0, 0), new IntVec3(0, 0, 1), new IntVec3(0, 0, -1) })
                {
                    SpawnWallOn(lower, pocket + side);
                }
                Building_Stairs walled = StairRouteCache.Best(lower, pocket, true);
                bool walledNone = walled == null;
                foreach (Thing wall in (pocket + new IntVec3(1, 0, 0)).GetThingList(lower).Where(thing => thing.def == ThingDefOf.Wall).ToList())
                {
                    wall.Destroy(DestroyMode.Vanish);
                }
                Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + 600);
                Building_Stairs opened = StairRouteCache.Best(lower, pocket, true);
                bool openedFound = opened != null;
                Record("a walled cell has no route, and a route opened later is found", walledNone && openedFound ? "PASS" : "FAIL: the walled cell gave " + (walled == null ? "no route" : walled.ToString()) + ", and after the opening " + (opened == null ? "still no route" : opened.ToString()));
                // Row 5, three floors (S2). The colonist is on the lower floor, and its only work is on the upper floor, two floors up. The way there is
                // the surface: the colonist walks to the surface stairs, and then up the stairs on the surface. The search must reach the upper floor
                // through the surface, as the base search did, so the job it finds is the haul on the upper floor.
                Map upperFloor = tracker.CreateUpperLevel(surface, surface.Center);
                upperFloor.fogGrid.ClearAllFog();
                // The climb area must hold no fixed thing, such as a steam geyser. The harness cannot destroy one, and clearing over it logs an error.
                IntVec3 climb = IntVec3.Invalid;
                foreach (IntVec3 offset in new[] { new IntVec3(6, 0, -6), new IntVec3(-6, 0, -6), new IntVec3(6, 0, 6), new IntVec3(-6, 0, 6), new IntVec3(0, 0, -9), new IntVec3(0, 0, 9) })
                {
                    if (AreaFree(surface, center + offset, 3) && AreaFree(upperFloor, center + offset, 3))
                    {
                        climb = center + offset;
                        break;
                    }
                }
                if (!climb.IsValid)
                {
                    throw new InvalidOperationException("no 7 by 7 area free of fixed things near the centre of the surface");
                }
                foreach (Map floor in new[] { surface, upperFloor })
                {
                    for (int x = -3; x <= 3; x++)
                    {
                        for (int z = -3; z <= 3; z++)
                        {
                            ClearCell(floor, climb + new IntVec3(x, 0, z));
                            floor.terrainGrid.SetTerrain(climb + new IntVec3(x, 0, z), TerrainDefOf.Concrete);
                        }
                    }
                }
                SpawnStairPair(tracker, upperFloor, surface, climb);
                tracker.GameComponentTick();
                IntVec3 upperStock = climb + new IntVec3(-3, 0, 2);
                ClearCell(upperFloor, upperStock);
                Thing upperSteel = ThingMaker.MakeThing(ThingDefOf.Steel);
                upperSteel.stackCount = 10;
                GenSpawn.Spawn(upperSteel, upperStock, upperFloor);
                MakeSupplyZone(upperFloor, climb + new IntVec3(3, 0, 3));
                Pawn climber = SpawnCapableColonistNear(lower, start, new List<IntVec3>(), WorkTypeDefOf.Hauling);
                SetWork(climber, WorkTypeDefOf.Hauling, 3, WorkTypeDefOf.Mining, 0);
                Job climbFirst = SearchWorkJob(climber);
                Job climbWork = MainWorkJob(tracker, climber, climbFirst);
                bool upperOk = climbWork != null && climbWork.targetA.Thing != null && climbWork.targetA.Thing.Map == upperFloor;
                Record("the search reaches work on the top floor through the surface", upperOk ? "PASS" : "FAIL: the search gave " + DescribeOwnFloorJob(climbWork) + " for a colonist on " + climber.Map + ", not the steel on the top floor");
                bool all = shortestWalk && reachable && checks == 0 && walledNone && openedFound && upperOk;
                Record("stair routes", all ? "PASS" : "FAIL: a row above failed");
            }
            catch (Exception exception)
            {
                Record("stair routes", "FAIL: " + exception);
                Log.Error("Z-Levels stair routes validation failed: " + exception);
            }
            Root.Shutdown();
        }

        // The walk up to a floor that the colonist reaches after it has yielded the floor below. The iterator yields the floors nearest first. The upper
        // level and the lower level are equally far from the surface, and the iterator keeps their order of creation, so the upper level is made first.
        // The colonist yields the upper level by the surface's stairs up, then the lower level by the surface's stairs down. The stairs up to the top
        // level stand on the upper level. The reachability patch teleports the colonist to the cell at the lower stairs' coordinates on the upper level,
        // and that cell is walled in here. A walk that starts there cannot reach the top stairs. The walk must start at the arrival on the upper level.
        private void RunFloorWalkCheck()
        {
            try
            {
                Map surface = Find.CurrentMap;
                ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
                Map upper = tracker.CreateUpperLevel(surface, surface.Center);
                Map lower = CreateLevelBelow(tracker, surface);
                Map top = tracker.CreateUpperLevel(upper, upper.Center);
                IntVec3 center = surface.Center;
                IntVec3 start = center;
                IntVec3 downStairs = center + new IntVec3(1, 0, 0);
                IntVec3 arrivalUp = center + new IntVec3(-4, 0, 0);
                IntVec3 topStairs = center + new IntVec3(-4, 0, 3);
                foreach (Map floor in new[] { surface, upper, lower, top })
                {
                    CarveAround(floor, center, 9);
                    floor.fogGrid.ClearAllFog();
                }
                ClearCell(surface, start);
                surface.terrainGrid.SetTerrain(start, TerrainDefOf.Concrete);
                SpawnStairPair(tracker, surface, lower, downStairs);
                SpawnStairPair(tracker, upper, surface, arrivalUp);
                SpawnStairPair(tracker, top, upper, topStairs);
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (dx != 0 || dz != 0)
                        {
                            SpawnWallOn(upper, downStairs + new IntVec3(dx, 0, dz));
                        }
                    }
                }
                tracker.GameComponentTick();
                IntVec3 topStock = topStairs + new IntVec3(3, 0, -2);
                ClearCell(top, topStock);
                Thing topSteel = ThingMaker.MakeThing(ThingDefOf.Steel);
                topSteel.stackCount = 10;
                GenSpawn.Spawn(topSteel, topStock, top);
                MakeSupplyZone(top, topStairs + new IntVec3(-3, 0, -2));
                Pawn colonist = SpawnPawnAt(surface, start);
                SetWork(colonist, WorkTypeDefOf.Hauling, 3, WorkTypeDefOf.Mining, 0);

                List<Map> order = tracker.GetAllMapsInClosestOrder(surface);
                bool upperFirst = order.IndexOf(upper) >= 0 && order.IndexOf(upper) < order.IndexOf(lower);
                Record("the floors in closest order from the surface", (upperFirst ? "PASS" : "FAIL: the lower level comes first, so the walk is not tested") + " (zIndex " + string.Join(", ", order.Select(map => tracker.GetZIndexFor(map).ToString())) + ")");

                // Diagnostics, read only. They report the stairs each level lists, the cell the reachability patch teleports the colonist to when it walks from
                // the lower arrival, and the reachability of the top stairs from that cell and from the arrival.
                Record("stairs up on the surface", "INFO: " + string.Join(", ", tracker.GetStairsUpFor(surface).Select(stairs => stairs.Position.ToString())));
                Record("stairs down on the surface", "INFO: " + string.Join(", ", tracker.GetStairsDownFor(surface).Select(stairs => stairs.Position.ToString())));
                Record("stairs up on the upper level", "INFO: " + string.Join(", ", tracker.GetStairsUpFor(upper).Select(stairs => stairs.Position.ToString())));
                MethodInfo cellToTeleport = typeof(ZLevelsManager).Assembly.GetType("ZLevels.ZUtils", true).GetMethod("GetCellToTeleportFrom", BindingFlags.Public | BindingFlags.Static);
                IntVec3 lowerWalkStart = (IntVec3)cellToTeleport.Invoke(null, new object[] { lower, downStairs, upper });
                int walls = 0;
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if ((dx != 0 || dz != 0) && (downStairs + new IntVec3(dx, 0, dz)).GetEdifice(upper) != null)
                        {
                            walls++;
                        }
                    }
                }
                Record("the walk from the lower arrival", "INFO: starts at " + lowerWalkStart + " on the upper level; the surface arrival is " + arrivalUp + ", the lower stairs' cell is " + downStairs + " with " + walls + " of 8 neighbours walled");
                Building_Stairs topUp = tracker.GetStairsUpFor(upper).FirstOrDefault(stairs => stairs.Position == topStairs);
                if (topUp != null)
                {
                    Record("the reachability of the top stairs", "INFO: from the lower walk's start " + upper.reachability.CanReach(lowerWalkStart, topUp, PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassDoors)) + ", from the surface arrival " + upper.reachability.CanReach(arrivalUp, topUp, PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassDoors)));
                }
                else
                {
                    Record("the reachability of the top stairs", "FAIL: no stairs up on the upper level at " + topStairs);
                }

                Job first = SearchWorkJob(colonist);
                Job work = MainWorkJob(tracker, colonist, first);
                bool reachedTop = work != null && work.targetA.Thing != null && work.targetA.Thing.Map == top;
                Record("the search reaches work two floors up after the floor below", reachedTop ? "PASS" : "FAIL: the search gave " + DescribeOwnFloorJob(work) + " for a colonist on " + colonist.Map + ", not the steel on the top level");
                if (topUp != null)
                {
                    TeleportThingMethod().Invoke(null, new object[] { colonist, lower, downStairs });
                    bool fromLowerArrival = colonist.CanReach(topUp, PathEndMode.OnCell, Danger.Deadly);
                    TeleportThingMethod().Invoke(null, new object[] { colonist, upper, arrivalUp });
                    bool fromUpperArrival = colonist.CanReach(topUp, PathEndMode.OnCell, Danger.Deadly);
                    Record("the colonist's walk to the top stairs", "INFO: from the lower arrival " + fromLowerArrival + ", from the arrival on the upper level " + fromUpperArrival);
                }
                Record("floor walk", upperFirst && reachedTop ? "PASS" : "FAIL: a row above failed");
            }
            catch (Exception exception)
            {
                Record("floor walk", "FAIL: " + exception);
                Log.Error("Z-Levels floor walk validation failed: " + exception);
            }
            Root.Shutdown();
        }

        // The floor change the colonist makes from the lower level to the surface. The first map the iterator yields is the surface, and the
        // colonist is already moved to the stair the decision chose.
        private static void ChooseStairFloor(Pawn colonist, Map lower, IntVec3 start)
        {
            foreach (Map map in ClosestOrderMaps(colonist, lower, start))
            {
                break;
            }
        }

        // ZUtils.GetAllMapsInClosestOrder with skipOldMap set. ZUtils is internal to the mod, so the harness calls it by name.
        private static IEnumerable<Map> ClosestOrderMaps(Pawn colonist, Map oldMap, IntVec3 oldPosition)
        {
            Type zutils = typeof(ZLevelsManager).Assembly.GetType("ZLevels.ZUtils", true);
            MethodInfo method = zutils.GetMethod("GetAllMapsInClosestOrder", BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException("ZLevels.ZUtils.GetAllMapsInClosestOrder is missing");
            return (IEnumerable<Map>)method.Invoke(null, new object[] { colonist, oldMap, oldPosition, true, false });
        }

        // The haul candidates of a colonist with the own-floor search state set the way OwnFloorFirstSearch.SearchOwnFloorOnly sets it, and restored after.
        // The gate reads that state only inside the search, so the harness sets the two fields by reflection to see the candidates the search would see.
        private static List<Thing> PausedHaulCandidates(WorkGiver_Haul giver, Pawn colonist, Map ownMap)
        {
            Type search = typeof(ZLevelsManager).Assembly.GetType("ZLevels.OwnFloorFirstSearch", true);
            FieldInfo pawnField = search.GetField("OwnFloorOnlyPawn", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("ZLevels.OwnFloorFirstSearch.OwnFloorOnlyPawn is missing");
            FieldInfo mapField = search.GetField("OwnFloorOnlyMap", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("ZLevels.OwnFloorFirstSearch.OwnFloorOnlyMap is missing");
            Pawn previousPawn = (Pawn)pawnField.GetValue(null);
            Map previousMap = (Map)mapField.GetValue(null);
            pawnField.SetValue(null, colonist);
            mapField.SetValue(null, ownMap);
            try
            {
                return giver.PotentialWorkThingsGlobal(colonist).ToList();
            }
            finally
            {
                pawnField.SetValue(null, previousPawn);
                mapField.SetValue(null, previousMap);
            }
        }

        // The haul of the book to this floor's stockpile, as row 6 checks it: a HaulToCell job for the book, to a cell with no thing, that a colonist on this floor holds,
        // to a cell of this floor's stockpile, and with no zone of the other map at that cell.
        private static bool IsBookHaulToOwnStockpile(Job job, Thing book, Zone floorZone, Map floorMap, Pawn holder, Map otherMap)
        {
            return job != null && job.def == JobDefOf.HaulToCell && job.targetA.Thing == book && job.targetB.Thing == null
                && holder.Map == floorMap && floorZone.Map == floorMap && floorMap.zoneManager.ZoneAt(job.targetB.Cell) == floorZone
                && otherMap.zoneManager.ZoneAt(job.targetB.Cell) == null;
        }

        // The map that the colonist's job tracker names as its destination map. The tracker is internal to the mod, so the harness reads it by reflection.
        private static string MapDestOf(ZLevelsManager tracker, Pawn colonist)
        {
            FieldInfo trackers = typeof(ZLevelsManager).GetField("jobTracker", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            System.Collections.IDictionary dictionary = trackers == null ? null : trackers.GetValue(tracker) as System.Collections.IDictionary;
            if (dictionary == null || !dictionary.Contains(colonist))
            {
                return "no job tracker";
            }
            object jobTracker = dictionary[colonist];
            FieldInfo mapDest = jobTracker.GetType().GetField("mapDest", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return mapDest == null ? "no mapDest field" : (mapDest.GetValue(jobTracker) as Map)?.ToString() ?? "null";
        }

        private static void SpawnWallOn(Map map, IntVec3 cell)
        {
            ClearCell(map, cell);
            GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.Steel), cell, map);
        }

        // A stair pair at one cell: a stairs down on the surface and the stairs up below it, as SetUpLowerStairs makes them.
        // True when every cell within the radius holds no thing that cannot be destroyed, so ClearCell can clear it without an error.
        private static bool AreaFree(Map map, IntVec3 center, int radius)
        {
            for (int x = -radius; x <= radius; x++)
            {
                for (int z = -radius; z <= radius; z++)
                {
                    IntVec3 cell = center + new IntVec3(x, 0, z);
                    if (!cell.InBounds(map) || cell.GetThingList(map).Any(thing => !thing.def.destroyable))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private static void SpawnStairPair(ZLevelsManager tracker, Map surface, Map lower, IntVec3 cell)
        {
            ClearCell(surface, cell);
            surface.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete);
            Building_StairsDown surfaceStairs = (Building_StairsDown)ThingMaker.MakeThing(ZLevelsDefOf.ZL_StairsDown, ThingDefOf.Steel);
            surfaceStairs.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(surfaceStairs, cell, surface);
            ClearCell(lower, cell);
            lower.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete);
            tracker.SpawnStairsBelow(SpawnRestPawn(surface), cell, lower);
        }

        // Task 14 (idea "add-ons load only when needed" on docs/mod-comparison/ideas.html). The cooling add-on needs Dubs Rimatomics, so its
        // assembly must not load for Z-Levels when Rimatomics is not active. This run has the runner's default mod list, which has no
        // Rimatomics. The cooling runs (-Cooling and the others) need Rimatomics, and they check that the cooling link works.
        private void RunAddonLoadCheck()
        {
            try
            {
                ModContentPack zlevels = LoadedModManager.RunningMods.FirstOrDefault(mod => mod.PackageId == "foxcosmic.zlevels17");
                if (zlevels == null)
                {
                    throw new InvalidOperationException("the Z-Levels mod is not running");
                }
                if (LoadedModManager.RunningMods.Any(mod => mod.PackageId == "dubwise.rimatomics"))
                {
                    throw new InvalidOperationException("Dubs Rimatomics is active in this run, so the run cannot show the add-on without it");
                }
                bool cooling = zlevels.assemblies.loadedAssemblies.Any(assembly => assembly.GetName().Name == "ZLevelsRimatomics");
                Record("no cooling assembly loads without Rimatomics", cooling ? "FAIL: Z-Levels loads ZLevelsRimatomics without Dubs Rimatomics" : "PASS");
                Record("add-on load", cooling ? "FAIL: a row above failed" : "PASS");
            }
            catch (Exception exception)
            {
                Record("add-on load", "FAIL: " + exception);
                Log.Error("Z-Levels add-on load validation failed: " + exception);
            }
            Root.Shutdown();
        }

        // Task 15 (idea "spread out the regular chores" on docs/mod-comparison/ideas.html). Each floor runs its periodic check on its own tick, so
        // two floors never run it on one tick. The scenario runs both floors' checks on the same ticks and counts the ticks on which a second
        // floor ran its check.
        private void RunChoreSpreadCheck()
        {
            try
            {
                Map surface = Find.CurrentMap;
                ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
                Map lower = CreateLevelBelow(tracker, surface);
                MapComponentZLevel surfaceComp = surface.GetComponent<MapComponentZLevel>();
                MapComponentZLevel lowerComp = lower.GetComponent<MapComponentZLevel>();
                int twoFloorsBefore = MapComponentZLevel.ChoreTicksWithTwoFloors;
                int runsBefore = MapComponentZLevel.ChoreRuns;
                for (int tick = 0; tick <= 3000; tick++)
                {
                    Find.TickManager.DebugSetTicksGame(tick);
                    surfaceComp.MapComponentTick();
                    lowerComp.MapComponentTick();
                }
                int twoFloors = MapComponentZLevel.ChoreTicksWithTwoFloors - twoFloorsBefore;
                int runs = MapComponentZLevel.ChoreRuns - runsBefore;
                Record("no tick runs the chore on two floors", twoFloors == 0 ? "PASS: " + runs + " checks, no tick shared" : "FAIL: " + twoFloors + " ticks ran the check on two floors");
                Record("chore spread", twoFloors == 0 && runs > 0 ? "PASS" : "FAIL: " + twoFloors + " shared ticks in " + runs + " checks");
            }
            catch (Exception exception)
            {
                Record("chore spread", "FAIL: " + exception);
                Log.Error("Z-Levels chore spread validation failed: " + exception);
            }
            Root.Shutdown();
        }

        // Task 16 (idea "nothing extra until floors exist" on docs/mod-comparison/ideas.html). A colony with one floor runs the vanilla job
        // searches. The scenario runs one colonist's work, rest, food and fun searches on the surface. FloorPolicy.CrossFloorSearches counts the
        // searches that go through the floor filter, and a one-floor colony must add nothing to it.
        private void RunOneFloorColonyCheck()
        {
            const string label = "a one-floor colony runs no cross-floor search";
            try
            {
                Map surface = Find.CurrentMap;
                ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
                int floors = tracker.GetAllMaps(surface.Tile).Count;
                if (floors != 1)
                {
                    throw new InvalidOperationException("the colony has " + floors + " floors, not one");
                }
                Pawn colonist = SpawnRestPawn(surface);
                JobGiver_GetRest restGiver = FindThinkNode<JobGiver_GetRest>(colonist.thinker.MainThinkNodeRoot);
                if (restGiver == null)
                {
                    throw new InvalidOperationException("the colonist's think tree has no JobGiver_GetRest");
                }
                colonist.needs.rest.CurLevelPercentage = 0.1f;
                // A hungry food giver and a fun giver, set up as the need cool-down scenario does.
                colonist.needs.food.CurLevelPercentage = 0.2f;
                JobGiver_GetFood foodGiver = new JobGiver_GetFood();
                typeof(JobGiver_GetFood).GetField("minCategory", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .SetValue(foodGiver, HungerCategory.Hungry);
                colonist.needs.joy.CurLevelPercentage = 0.5f;
                JobGiver_GetJoy joyGiver = new JobGiver_GetJoy();
                joyGiver.ResolveReferences();

                int work = CrossFloorSearchesDuring(() => SearchWorkJob(colonist));
                int rest = CrossFloorSearchesDuring(() => TryGiveRestJob(restGiver, colonist));
                int food = CrossFloorSearchesDuring(() => InvokeTryGiveJob(foodGiver, colonist));
                int fun = CrossFloorSearchesDuring(() => InvokeTryGiveJob(joyGiver, colonist));
                // The haul check of a stored thing (StoreUtility.IsInValidBestStorage, patched in JobPatches). Ten steel on a stockpile of the
                // surface are the thing. The stockpile must hold the steel and accept it, or the check returns before its loop and counts nothing.
                IntVec3 stockNear = FindOpenBlock(surface, 1, 1);
                if (!stockNear.IsValid)
                {
                    throw new InvalidOperationException("no open cell on the surface for the stockpile");
                }
                Zone_Stockpile stockpile = MakeSupplyZone(surface, stockNear);
                PlaceSupplyStack(surface, stockpile, ThingDefOf.Steel, 10);
                Thing steel = stockpile.Cells.Select(cell => cell.GetFirstItem(surface)).FirstOrDefault(thing => thing != null && thing.def == ThingDefOf.Steel);
                IHaulDestination destination = steel == null ? null : StoreUtility.CurrentHaulDestinationOf(steel);
                if (destination == null || !destination.Accepts(steel))
                {
                    throw new InvalidOperationException("the stockpile does not hold and accept the steel, so the haul check would not run");
                }
                int haul = CrossFloorSearchesDuring(() => StoreUtility.IsInValidBestStorage(steel));
                // The answer is shown, not gated: the steel sits in its stockpile, so the check should say it is stored.
                Record("one-floor haul answer", "INFO: the steel counts as stored in its stockpile: " + StoreUtility.IsInValidBestStorage(steel));
                string counts = "work " + work + ", rest " + rest + ", food " + food + ", fun " + fun + ", haul check " + haul;
                // The gate reads the required row as a plain PASS, so the counts go on their own row.
                Record("one-floor search counts", "INFO: " + counts);
                Record(label, work + rest + food + fun + haul == 0 ? "PASS" : "FAIL: cross-floor searches on one floor: " + counts);
            }
            catch (Exception exception)
            {
                Record(label, "FAIL: " + exception);
                Log.Error("Z-Levels one-floor colony validation failed: " + exception);
            }
            Root.Shutdown();
        }

        // Task 17 (idea "view the connected floor" on docs/mod-comparison/ideas.html). Each stairs piece offers a button that moves the camera to
        // its other end, the matching stairs on the floor above or below. The scenario checks the button on both ends, presses the lower end's
        // button from the lower floor and the surface end's button from the surface, and reads the floor the camera ends on.
        private void RunStairsViewCheck()
        {
            try
            {
                Map surface = Find.CurrentMap;
                ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
                Building_StairsUp lowerStairs = SetUpLowerStairs(surface, tracker, out Map lower);
                Building_Stairs surfaceStairs = tracker.GetOtherEnd(lowerStairs);
                if (surfaceStairs == null)
                {
                    throw new InvalidOperationException("the lower stairs have no matching stairs on the surface");
                }
                Command_Action lowerButton = StairsViewButton(lowerStairs);
                Command_Action surfaceButton = StairsViewButton(surfaceStairs);
                string missing = lowerButton == null ? "the lower stairs" : surfaceButton == null ? "the surface stairs" : null;
                Record("stairs offer the view button", missing == null ? "PASS" : "FAIL: no view button on " + missing);
                if (lowerButton == null || surfaceButton == null)
                {
                    Record("pressing it moves the camera to the other end", "FAIL: there is no button to press");
                }
                else
                {
                    Current.Game.CurrentMap = lower;
                    lowerButton.action();
                    Map fromLower = Current.Game.CurrentMap;
                    Current.Game.CurrentMap = surface;
                    surfaceButton.action();
                    Map fromSurface = Current.Game.CurrentMap;
                    bool moved = fromLower == surface && fromSurface == lower;
                    Record("pressing it moves the camera to the other end", moved ? "PASS"
                        : "FAIL: the lower button ended on " + (fromLower == surface ? "the surface" : "another map") + ", the surface button ended on " + (fromSurface == lower ? "the lower floor" : "another map"));
                }
            }
            catch (Exception exception)
            {
                Record("pressing it moves the camera to the other end", "FAIL: " + exception);
                Log.Error("Z-Levels stairs view validation failed: " + exception);
            }
            Root.Shutdown();
        }

        // The view button of a stairs piece, or null when the piece offers none. The label is the one the gizmo shows.
        private static Command_Action StairsViewButton(Building_Stairs stairs)
        {
            return stairs.GetGizmos().OfType<Command_Action>().FirstOrDefault(gizmo => gizmo.defaultLabel == "View the connected floor");
        }

        // Task 18 (idea "better stair placement messages" on docs/mod-comparison/ideas.html). A stairs piece is refused, with a message, when it
        // cannot work: its room is under 25 cells, stairs up have no roof over their room, or stairs down start above ground. The scenario asks
        // the game's own placement check (GenConstruct.CanPlaceBlueprintAt) about one cell for each rule and reads the answer.
        private void RunStairPlacementCheck()
        {
            const string roomMessage = "This room is under 25 cells. Stairs need a room of at least 25 cells.";
            const string roofMessage = "No roof to build on. Stairs up need a roof over the room.";
            const string groundMessage = "Stairs going down cannot start above ground.";
            try
            {
                Map surface = Find.CurrentMap;
                ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();

                // No roof: stairs up on an open cell of the surface.
                IntVec3 block = FindCentralOpenBlock(surface, 5, 5);
                if (!block.IsValid)
                {
                    throw new InvalidOperationException("no open 5 by 5 block on the surface away from the edges");
                }
                // The stairs need terrain that supports light, and the validation map's terrain differs from run to run, so the block gets concrete.
                for (int x = 0; x < 5; x++)
                {
                    for (int z = 0; z < 5; z++)
                    {
                        surface.terrainGrid.SetTerrain(block + new IntVec3(x, 0, z), TerrainDefOf.Concrete);
                    }
                }
                // A plain stairs down on the open surface is allowed: the new rules must not refuse ordinary stairs.
                string openDown = PlacementRefusal(ZLevelsDefOf.ZL_StairsDown, block + new IntVec3(2, 0, 2), surface);
                Record("stairs down on the surface are allowed", openDown == null ? "PASS" : "FAIL: refused with \"" + openDown + "\"");
                string noRoof = PlacementRefusal(ZLevelsDefOf.ZL_StairsUp, block + new IntVec3(2, 0, 2), surface);
                Record("no roof refuses with its message", noRoof == roofMessage ? "PASS" : "FAIL: " + DescribeRefusal(noRoof, roofMessage));

                // Room under 25 cells: stairs down in a closed room of 3 by 3 cells, with a roof over it.
                for (int x = 0; x < 5; x++)
                {
                    for (int z = 0; z < 5; z++)
                    {
                        IntVec3 cell = block + new IntVec3(x, 0, z);
                        ClearCell(surface, cell);
                        if (x == 0 || z == 0 || x == 4 || z == 4)
                        {
                            GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.Steel), cell, surface);
                        }
                        else
                        {
                            surface.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);
                        }
                    }
                }
                string smallRoom = PlacementRefusal(ZLevelsDefOf.ZL_StairsDown, block + new IntVec3(2, 0, 2), surface);
                Record("room under 25 cells refuses with its message", smallRoom == roomMessage ? "PASS" : "FAIL: " + DescribeRefusal(smallRoom, roomMessage));

                // Above ground: stairs down on an upper floor, which is above the surface.
                Map upper = tracker.CreateUpperLevel(surface, surface.Center);
                // The upper floor is fogged until seen, and the game refuses placement in undiscovered areas.
                upper.fogGrid.ClearAllFog();
                IntVec3 upperBlock = FindCentralOpenBlock(upper, 3, 3);
                if (!upperBlock.IsValid)
                {
                    throw new InvalidOperationException("no open 3 by 3 block on the upper floor");
                }
                for (int x = 0; x < 3; x++)
                {
                    for (int z = 0; z < 3; z++)
                    {
                        upper.terrainGrid.SetTerrain(upperBlock + new IntVec3(x, 0, z), TerrainDefOf.Concrete);
                    }
                }
                string aboveGround = PlacementRefusal(ZLevelsDefOf.ZL_StairsDown, upperBlock + new IntVec3(1, 0, 1), upper);
                Record("stairs down from above ground refuses with its message", aboveGround == groundMessage ? "PASS" : "FAIL: " + DescribeRefusal(aboveGround, groundMessage));
            }
            catch (Exception exception)
            {
                Record("stairs down from above ground refuses with its message", "FAIL: " + exception);
                Log.Error("Z-Levels stair placement validation failed: " + exception);
            }
            Root.Shutdown();
        }

        // The lowest corner of a width by height block that lies at least 10 cells inside every map edge, whose cells are standable and empty.
        // The game refuses buildings near the map edge, so FindOpenBlock, which allows a 2 cell margin, is not enough here.
        private static IntVec3 FindCentralOpenBlock(Map map, int width, int height)
        {
            for (int x = 10; x < map.Size.x - width - 10; x++)
            {
                for (int z = 10; z < map.Size.z - height - 10; z++)
                {
                    bool open = true;
                    for (int i = 0; i < width && open; i++)
                    {
                        for (int j = 0; j < height && open; j++)
                        {
                            IntVec3 cell = new IntVec3(x + i, 0, z + j);
                            open = cell.Standable(map) && cell.GetEdifice(map) == null && cell.GetThingList(map).Count == 0;
                        }
                    }
                    if (open)
                    {
                        return new IntVec3(x, 0, z);
                    }
                }
            }
            return IntVec3.Invalid;
        }

        // The refusal message the game's placement check gives for a stairs piece at a cell, or null when the placement is allowed.
        private static string PlacementRefusal(ThingDef def, IntVec3 cell, Map map)
        {
            AcceptanceReport report = GenConstruct.CanPlaceBlueprintAt(def, cell, Rot4.North, map);
            return report.Accepted ? null : report.Reason;
        }

        private static string DescribeRefusal(string got, string expected)
        {
            return got == null ? "placement is allowed" : "refused with \"" + got + "\", expected \"" + expected + "\"";
        }

        // The number of cross-floor searches that the action starts, read from FloorPolicy.CrossFloorSearches before and after it.
        private static int CrossFloorSearchesDuring(Action action)
        {
            int before = FloorPolicy.CrossFloorSearches;
            action();
            return FloorPolicy.CrossFloorSearches - before;
        }

        // Task 19 (the baseline that Phase B is measured against). A colonist's work search, timed on a one-floor, a two-floor and a three-floor
        // colony, and the map tick on each extra floor. Ten colonists with every work type on make 50 searches each. The colony has nothing for
        // them, so a search that finds no job is the usual result. The clock moves past the idle pause before every call. The pause (600 ticks
        // after a search that found nothing) makes the vanilla search answer in its place, and the timing would measure that. A map tick is
        // MapPreTick and MapPostTick, the per-map calls of TickManager.DoSingleTick. Things tick through the global tick lists, not per map, so
        // their cost is not in the map tick numbers. The map ticks run last, because the animals and plants they spawn would change the searches.
        private void RunSearchTimingCheck()
        {
            const string label = "search timing recorded";
            const int colonistCount = 10;
            const int callsEach = 50;
            const int mapTicks = 300;
            // The colonists and the floors come from one fixed random state, so every run builds the same ones. The surface does not: the runner
            // starts a new random game, and its surface is recorded in the rows above each floor.
            Rand.PushState(20261106);
            try
            {
                Map surface = Find.CurrentMap;
                ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
                RequireFloors(tracker, surface, 1);
                IntVec3 block = FindCentralOpenBlock(surface, 7, 7);
                if (!block.IsValid)
                {
                    throw new InvalidOperationException("no open 7 by 7 block on the surface away from the edges");
                }
                IntVec3 near = block + new IntVec3(3, 0, 3);
                List<IntVec3> avoid = new List<IntVec3>();
                List<Pawn> colonists = new List<Pawn>();
                for (int i = 0; i < colonistCount; i++)
                {
                    colonists.Add(SpawnColonistNear(surface, near, avoid));
                }
                int expected = colonistCount * callsEach;

                WorkSearchTiming oneFloor = TimeWorkSearches(tracker, colonists, callsEach);
                Record("surface before the lower floor", "INFO: " + SurfaceState(surface));
                Map lower = CreateLevelBelow(tracker, surface);
                lower.fogGrid.ClearAllFog();
                Record("surface after the lower floor", "INFO: " + SurfaceState(surface));
                RequireFloors(tracker, surface, 2);
                WorkSearchTiming twoFloors = TimeWorkSearches(tracker, colonists, callsEach);
                Record("surface before the upper floor", "INFO: " + SurfaceState(surface));
                Map upper = tracker.CreateUpperLevel(surface, surface.Center);
                upper.fogGrid.ClearAllFog();
                Record("surface after the upper floor", "INFO: " + SurfaceState(surface));
                RequireFloors(tracker, surface, 3);
                WorkSearchTiming threeFloors = TimeWorkSearches(tracker, colonists, callsEach);

                // Task 24: the per-patch timing. A probe colonist outside the work-search group holds a revolver for the attack metrics. The phase runs
                // before the map ticks, as the map ticks spawn the animals and plants that would change a search.
                Pawn probe = SpawnColonistNear(surface, near, avoid);
                List<PatchTimingLine> patchLines = TimePatchRemovals(tracker, colonists, probe);

                // The map ticks come after every search, so the animals and plants they spawn cannot change a search.
                double lowerMs = TimeMapTicks(lower, mapTicks);
                double upperMs = TimeMapTicks(upper, mapTicks);
                double surfaceMs = TimeMapTicks(surface, mapTicks);

                int enabledWork = colonists.Sum(colonist => DefDatabase<WorkTypeDef>.AllDefsListForReading.Count(work => colonist.workSettings.WorkIsActive(work)));
                Record("search timing colonists", "INFO: " + colonistCount + " idle colonists, " + callsEach + " calls each, " + expected + " calls per setup, "
                    + enabledWork + " enabled work types across them");
                Record("search timing, one floor", "INFO: " + TimingText(oneFloor));
                Record("search timing, two floors", "INFO: " + TimingText(twoFloors));
                Record("search timing, three floors", "INFO: " + TimingText(threeFloors));
                Record("map tick, lower floor", "INFO: " + TickText(lowerMs, mapTicks));
                Record("map tick, upper floor", "INFO: " + TickText(upperMs, mapTicks));
                Record("map tick, surface (reference)", "INFO: " + TickText(surfaceMs, mapTicks));

                // One floor: the vanilla search runs, so ZLevels counts nothing. Two and three floors: every call runs the ZLevels search and reaches
                // the other floors. A short count means the timing measured the pause or a cached job, not the search.
                bool oneOk = oneFloor.Searches == 0 && oneFloor.CrossFloor == 0;
                bool twoOk = twoFloors.Searches == expected && twoFloors.CrossFloor > 0;
                bool threeOk = threeFloors.Searches == expected && threeFloors.CrossFloor > 0;
                Record(label, oneOk && twoOk && threeOk
                    ? "PASS"
                    : "FAIL: one floor ran " + oneFloor.Searches + " searches and " + oneFloor.CrossFloor + " cross-floor, two floors " + twoFloors.Searches + " of " + expected + " searches and " + twoFloors.CrossFloor + " cross-floor, three floors " + threeFloors.Searches + " of " + expected + " searches and " + threeFloors.CrossFloor + " cross-floor");
                // Task 24: one line per patch and metric. A line fails when a removal or a re-apply did not take effect, or when a measurement had no calls.
                foreach (PatchTimingLine line in patchLines) Record("patch timing, " + line.Name + ", " + line.Metric, "INFO: " + line.Text());
                Record("patch timing recorded", patchLines.All(line => line.Integrity)
                    ? "PASS"
                    : "FAIL: " + string.Join("; ", patchLines.Where(line => !line.Integrity).Select(line => line.Problem)));
            }
            catch (Exception exception)
            {
                Record(label, "FAIL: " + exception);
                Log.Error("Z-Levels search timing validation failed: " + exception);
            }
            finally
            {
                Rand.PopState();
            }
            Root.Shutdown();
        }

        // Stops the scenario when the colony does not have the floors that a setup needs, so no timing is taken on the wrong layout.
        private static void RequireFloors(ZLevelsManager tracker, Map surface, int floors)
        {
            int count = tracker.GetAllMaps(surface.Tile).Count;
            if (count != floors)
            {
                throw new InvalidOperationException("the colony has " + count + " floors, not " + floors);
            }
        }

        private struct WorkSearchTiming
        {
            public int Calls;
            public int Jobs;
            public int Searches;
            public int CrossFloor;
            public double Milliseconds;
            public double FirstRoundMs;
            public double MedianRoundMs;
            public double SlowestRoundMs;
            public int Gen0;
            public int Gen1;
            public int Gen2;
        }

        // The colonists' work searches, timed. Each colonist searches once untimed, then callsEach times in turn. The clock moves before every
        // call, and only the calls are timed.
        private static WorkSearchTiming TimeWorkSearches(ZLevelsManager tracker, List<Pawn> colonists, int callsEach)
        {
            foreach (Pawn colonist in colonists)
            {
                AdvanceClockPastIdlePause();
                SearchWorkJob(colonist);
            }
            int searchesBefore = colonists.Sum(colonist => tracker.floorPolicy.SearchCount(colonist));
            int crossBefore = FloorPolicy.CrossFloorSearches;
            int gen0Before = GC.CollectionCount(0);
            int gen1Before = GC.CollectionCount(1);
            int gen2Before = GC.CollectionCount(2);
            WorkSearchTiming timing = new WorkSearchTiming();
            List<double> rounds = new List<double>();
            System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();
            for (int call = 0; call < callsEach; call++)
            {
                // A round is one call for each colonist. The rounds show whether the first round or one slow round makes the total.
                System.Diagnostics.Stopwatch roundWatch = System.Diagnostics.Stopwatch.StartNew();
                foreach (Pawn colonist in colonists)
                {
                    AdvanceClockPastIdlePause();
                    watch.Start();
                    Job job = SearchWorkJob(colonist);
                    watch.Stop();
                    timing.Calls++;
                    if (job != null) timing.Jobs++;
                }
                roundWatch.Stop();
                rounds.Add(roundWatch.Elapsed.TotalMilliseconds);
            }
            timing.Milliseconds = watch.Elapsed.TotalMilliseconds;
            timing.FirstRoundMs = rounds[0];
            rounds.Sort();
            timing.MedianRoundMs = rounds[rounds.Count / 2];
            timing.SlowestRoundMs = rounds[rounds.Count - 1];
            timing.Gen0 = GC.CollectionCount(0) - gen0Before;
            timing.Gen1 = GC.CollectionCount(1) - gen1Before;
            timing.Gen2 = GC.CollectionCount(2) - gen2Before;
            timing.Searches = colonists.Sum(colonist => tracker.floorPolicy.SearchCount(colonist)) - searchesBefore;
            timing.CrossFloor = FloorPolicy.CrossFloorSearches - crossBefore;
            return timing;
        }

        // Moves the clock one step past the idle pause, so the next search is not held back by it.
        private static void AdvanceClockPastIdlePause()
        {
            TickManager ticks = Find.TickManager;
            ticks.DebugSetTicksGame(ticks.TicksGame + FloorPolicy.IdlePauseTicks + 1);
        }

        // MapPreTick and MapPostTick, the two per-map calls of TickManager.DoSingleTick, for the given number of ticks. Returns milliseconds.
        private static double TimeMapTicks(Map map, int ticks)
        {
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            for (int tick = 0; tick < ticks; tick++)
            {
                map.MapPreTick();
                map.MapPostTick();
            }
            watch.Stop();
            return watch.Elapsed.TotalMilliseconds;
        }

        private static string TimingText(WorkSearchTiming timing)
        {
            return timing.Calls + " calls in " + OneDecimal(timing.Milliseconds) + " ms, " + OneDecimal(timing.Milliseconds * 1000.0 / timing.Calls)
                + " us per call; " + timing.Searches + " searches ran; " + timing.Jobs + " calls found a job; " + timing.CrossFloor + " cross-floor searches; rounds of one call per colonist: first "
                + OneDecimal(timing.FirstRoundMs) + " ms, median " + OneDecimal(timing.MedianRoundMs) + " ms, slowest " + OneDecimal(timing.SlowestRoundMs) + " ms; garbage collections gen0 "
                + timing.Gen0 + ", gen1 " + timing.Gen1 + ", gen2 " + timing.Gen2;
        }

        private static string TickText(double milliseconds, int ticks)
        {
            return ticks + " ticks in " + OneDecimal(milliseconds) + " ms, " + OneDecimal(milliseconds * 1000.0 / ticks) + " us per tick";
        }

        // The parts of a floor that a search reads: its walkable cells, its roofed cells and its things. Read outside the timing.
        private static string SurfaceState(Map map)
        {
            int roofed = 0;
            foreach (IntVec3 cell in map.AllCells)
            {
                if (map.roofGrid.RoofAt(cell) != null) roofed++;
            }
            return "walkable " + CountWalkable(map) + ", roofed " + roofed + ", things " + map.listerThings.AllThings.Count();
        }

        private static string OneDecimal(double value)
        {
            return value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        }

        // Task 20 (idea "lighter update on extra floors" on docs/mod-comparison/ideas.html). A floor above or below the surface skips four steps of
        // the map tick: wind, wild animal spawning, weather choice and passing ships. This scenario checks the spawn step and the weather step on
        // the lower floor, and that the surface still runs its weather. A spawn needs an edge the spawner can reach, so the lower floor's outer ring
        // is opened. The spawn chance is made to pass by drawing the random state from a seed in which it passes.
        private void RunLighterFloorsCheck()
        {
            const string label = "lighter floors";
            const int sentinel = -12345;
            Rand.PushState(20261107);
            try
            {
                Map surface = Find.CurrentMap;
                ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
                RequireFloors(tracker, surface, 1);
                Map lower = CreateLevelBelow(tracker, surface);
                lower.fogGrid.ClearAllFog();
                RequireFloors(tracker, surface, 2);
                OpenLowerEdge(lower);
                if (!lower.reachability.CanReachMapEdge(lower.Center, TraverseParms.For(TraverseMode.NoPassClosedDoors).WithFenceblocked(forceFenceblocked: true)))
                {
                    throw new InvalidOperationException("the start spot on the lower floor cannot reach the edge");
                }

                // An entry cell must be reachable from a colonist or a colony building on the same floor, so the scenario places one colonist there.
                SpawnPawnAt(lower, lower.Center);
                if (!RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 entry, lower, CellFinder.EdgeRoadChance_Animal, allowFogged: true))
                {
                    throw new InvalidOperationException("the spawner finds no entry cell on the lower floor");
                }
                int kinds = lower.BiomeAt(entry).AllWildAnimals.Count(kind => lower.mapTemperature.SeasonAcceptableFor(kind.race));
                Record("lighter floors entry", "INFO: entry cell " + entry + ", wild animal kinds acceptable there " + kinds);
                if (kinds == 0)
                {
                    throw new InvalidOperationException("no wild animal kind is acceptable on the lower floor at " + entry);
                }

                WildAnimalSpawner spawner = lower.wildAnimalSpawner;
                float density = (float)typeof(WildAnimalSpawner).GetProperty("DesiredAnimalDensity", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(spawner);
                float chance = 0.026955556f * density;
                int seed = FindSeedPassing(chance);
                int animalsBefore = CountAnimals(lower);
                // The spawner acts only on ticks that are a multiple of 1213. The 2000 ticks from 1213 hold two of them, at 1213 and 2426.
                foreach (int tick in new[] { 1213, 2426 })
                {
                    Find.TickManager.DebugSetTicksGame(tick);
                    Rand.PushState(seed);
                    spawner.WildAnimalSpawnerTick();
                    Rand.PopState();
                }
                int spawned = CountAnimals(lower) - animalsBefore;
                Record("lighter floors inputs", "INFO: spawn density " + density + ", chance " + chance + ", seed " + seed + ", ecosystem full " + spawner.AnimalEcosystemFull);
                Record("no wild spawn on the lower floor in 2000 ticks", spawned == 0 ? "PASS" : "FAIL: " + spawned + " wild animals spawned on the lower floor");

                // The weather decider changes the weather when the weather age passes its duration. A sentinel duration that the decider replaces
                // shows that it ran. The weather itself must also stay the same.
                WeatherDef lowerWeather = lower.weatherManager.curWeather;
                SetDecider(lower.weatherDecider, sentinel);
                lower.weatherDecider.WeatherDeciderTick();
                bool lowerKept = DeciderDuration(lower.weatherDecider) == sentinel && lower.weatherManager.curWeather == lowerWeather;
                Record("lower floor weather does not change", lowerKept ? "PASS" : "FAIL: the lower floor's weather decider ran, its duration is " + DeciderDuration(lower.weatherDecider) + " and its weather changed " + (lower.weatherManager.curWeather != lowerWeather));
                SetDecider(surface.weatherDecider, sentinel);
                surface.weatherDecider.WeatherDeciderTick();
                bool surfaceRan = DeciderDuration(surface.weatherDecider) != sentinel;
                Record("the surface still runs its weather", surfaceRan ? "PASS" : "FAIL: the surface weather decider did not run");
                // The four steps the change skips, timed alone on the surface, which the change does not skip. The clock sits on a tick that is not a
                // multiple of 1213, so the spawner's chance is not reached. The rows show how much of the extra floor's tick the four steps can cost.
                Find.TickManager.DebugSetTicksGame(1000);
                Record("tick step cost, surface", "INFO: wind " + MicrosecondsPerCall(() => surface.windManager.WindManagerTick(), 300)
                    + " us, wild spawning " + MicrosecondsPerCall(() => surface.wildAnimalSpawner.WildAnimalSpawnerTick(), 300)
                    + " us, weather choice " + MicrosecondsPerCall(() => surface.weatherDecider.WeatherDeciderTick(), 300)
                    + " us, passing ships " + MicrosecondsPerCall(() => surface.passingShipManager.PassingShipManagerTick(), 300) + " us per call");
                Record(label, spawned == 0 && lowerKept && surfaceRan ? "PASS" : "FAIL: a row above failed");
            }
            catch (Exception exception)
            {
                Record(label, "FAIL: " + exception);
                Log.Error("Z-Levels lighter floors validation failed: " + exception);
            }
            finally
            {
                Rand.PopState();
            }
            Root.Shutdown();
        }

        // Opens the outer ring of cells on a lower floor, and a straight corridor from the start spot to the ring, so the spawner can reach an edge.
        private static void OpenLowerEdge(Map map)
        {
            int maxX = map.Size.x - 1;
            int maxZ = map.Size.z - 1;
            foreach (IntVec3 cell in map.AllCells.ToList())
            {
                if (cell.x == 0 || cell.z == 0 || cell.x == maxX || cell.z == maxZ)
                {
                    ClearCell(map, cell);
                    map.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete);
                    map.roofGrid.SetRoof(cell, null);
                }
            }
            IntVec3 start = map.Center;
            for (int x = 0; x <= start.x; x++)
            {
                IntVec3 cell = new IntVec3(x, 0, start.z);
                ClearCell(map, cell);
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete);
                map.roofGrid.SetRoof(cell, null);
            }
        }

        // The mean cost of one call, in microseconds, over the given number of calls.
        private static string MicrosecondsPerCall(Action call, int calls)
        {
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < calls; i++)
            {
                call();
            }
            watch.Stop();
            return OneDecimal(watch.Elapsed.TotalMilliseconds * 1000.0 / calls);
        }

        private static int CountAnimals(Map map)
        {
            return map.mapPawns.AllPawns.Count(pawn => pawn.RaceProps.Animal);
        }

        // The first seed from 0 in which the spawn chance passes on its first draw. The draw is the first one the spawner makes.
        private static int FindSeedPassing(float chance)
        {
            for (int seed = 0; seed < 100000; seed++)
            {
                Rand.PushState(seed);
                bool passes = Rand.Chance(chance);
                Rand.PopState();
                if (passes)
                {
                    return seed;
                }
            }
            throw new InvalidOperationException("no seed below 100000 passes the spawn chance " + chance);
        }

        private static void SetDecider(WeatherDecider decider, int duration)
        {
            typeof(WeatherDecider).GetField("curWeatherDuration", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(decider, duration);
        }

        private static int DeciderDuration(WeatherDecider decider)
        {
            return (int)typeof(WeatherDecider).GetField("curWeatherDuration", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(decider);
        }

        // Task 21 (idea 8 on docs/mod-comparison/ideas.html, own floor first). A colonist searches its own floor first, and another floor only when the
        // own floor has no work. Work types at or below the threshold setting (2 by default) are searched on every floor before the lower-priority
        // work on the own floor. The rows: a priority-3 job on the own floor is taken with no search on another floor; a priority-1 mining job on the
        // lower floor beats a priority-3 hauling job on the surface; a priority-3 job is taken when nothing is on the lower floor; and the threshold
        // setting defaults to 2. The settings class is internal, so the scenario reads it by name.
        // Task 25 (idea 3, raids land on the ground). A raid aimed at a lower map arrives on the surface of the same tile, where the colonists stand. A
        // raid aimed at the surface is unchanged. Each raid fires through the incident worker, and the scenario counts the hostile pawns that appear
        // on each map.
        private void RunRaidGroundCheck()
        {
            const string label = "raid ground";
            try
            {
                Map surface = Find.CurrentMap;
                ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
                RequireFloors(tracker, surface, 1);
                Building_StairsUp stairs = SetUpLowerStairs(surface, tracker, out Map lower);
                RequireFloors(tracker, surface, 2);
                CarveAround(lower, stairs.Position, 6f);
                // The game fires a raid only at a map that has a colonist on it, so the lower floor gets one.
                SpawnRestPawn(lower);
                Record("raid setup", "INFO: the surface " + surface + " is level " + tracker.GetZIndexFor(surface) + "; the lower map " + lower
                    + " is level " + tracker.GetZIndexFor(lower) + "; the surface map of the lower tile is " + tracker.GetMapByIndex(lower.Tile, 0));

                IncidentWorker raid = IncidentDefOf.RaidEnemy.Worker;

                List<Pawn> hostileBeforeLower = HostilePawns(surface, lower);
                IncidentParms lowerParms = RaidParms(lower);
                bool firedLower = raid.TryExecute(lowerParms);
                List<Pawn> newLower = HostilePawns(surface, lower).Where(p => !hostileBeforeLower.Contains(p)).ToList();
                int lowerOnSurface = newLower.Count(p => p.Map == surface);
                int lowerOnLower = newLower.Count(p => p.Map == lower);
                bool lowerOk = firedLower && lowerOnSurface > 0 && lowerOnLower == 0;
                Record("raid aimed at a lower map arrives on the surface", lowerOk
                    ? "PASS: " + lowerOnSurface + " raiders on the surface, none on the lower map"
                    : "FAIL: fired " + firedLower + ", " + lowerOnSurface + " raiders on the surface, " + lowerOnLower + " on the lower map; faction "
                        + (lowerParms.faction == null ? "none" : lowerParms.faction.Name) + ", spawn center "
                        + (lowerParms.spawnCenter.IsValid ? lowerParms.spawnCenter.ToString() : "none"));

                List<Pawn> hostileBeforeSurface = HostilePawns(surface, lower);
                bool firedSurface = raid.TryExecute(RaidParms(surface));
                List<Pawn> newSurface = HostilePawns(surface, lower).Where(p => !hostileBeforeSurface.Contains(p)).ToList();
                int surfaceOnSurface = newSurface.Count(p => p.Map == surface);
                int surfaceOnLower = newSurface.Count(p => p.Map == lower);
                bool surfaceOk = firedSurface && surfaceOnSurface > 0 && surfaceOnLower == 0;
                Record("raid on the surface is unchanged", surfaceOk
                    ? "PASS: " + surfaceOnSurface + " raiders on the surface"
                    : "FAIL: fired " + firedSurface + ", " + surfaceOnSurface + " raiders on the surface, " + surfaceOnLower + " on the lower map");

                Record(label, lowerOk && surfaceOk ? "PASS" : "FAIL: a row above failed");
            }
            catch (Exception exception)
            {
                Record(label, "FAIL: " + exception);
                Log.Error("Z-Levels raid ground validation failed: " + exception);
            }
            finished = true;
            Root.Shutdown();
        }

        private static List<Pawn> HostilePawns(Map surface, Map lower)
        {
            return surface.mapPawns.AllPawnsSpawned.Concat(lower.mapPawns.AllPawnsSpawned)
                .Where(p => p.Faction != null && p.Faction.HostileTo(Faction.OfPlayer)).ToList();
        }

        private static IncidentParms RaidParms(IIncidentTarget target)
        {
            return new IncidentParms
            {
                target = target,
                points = 300f,
                silent = true,
                raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn,
                raidStrategy = RaidStrategyDefOf.ImmediateAttack
            };
        }

        // Task 26 (idea 4, DM9: leaving means walking to the ground first). A visitor on the lower floor is given the exit duty. Its leave job must walk to
        // the stairs toward the surface, change floor, and leave by the surface exit. The walk takes game time, so the check runs over several frames.
        private int leaveStage;
        private int leaveDueTick;
        private bool leaveDone;
        private bool leaveReachedSurface;
        private Pawn leavePawn;
        private Map leaveSurface;
        private Map leaveLower;
        private Lord leaveLord;
        private string leaveLastTrace = "";

        private void RunLeaveFromLowerTick()
        {
            if (leaveDone)
            {
                return;
            }
            try
            {
                if (leaveStage == 0)
                {
                    SetUpLeaveScene();
                    leaveStage = 1;
                    leaveDueTick = Find.TickManager.TicksGame + 6000;
                    return;
                }
                string trace = DescribeJob(leavePawn.CurJob) + " on " + leavePawn.Map;
                if (trace != leaveLastTrace)
                {
                    leaveLastTrace = trace;
                    Record("job trace", "INFO: " + trace + " at " + leavePawn.Position);
                }
                if (leavePawn.Map == leaveSurface)
                {
                    leaveReachedSurface = true;
                }
                if (!leavePawn.Spawned && leaveReachedSurface)
                {
                    FinishLeaveCheck();
                    return;
                }
                if (Find.TickManager.TicksGame > leaveDueTick)
                {
                    FinishLeaveCheck();
                }
            }
            catch (Exception exception)
            {
                leaveDone = true;
                Record("visitor on the lower floor reaches the surface and leaves", "FAIL: " + exception);
                Log.Error("Z-Levels leave from lower validation failed: " + exception);
                finished = true;
                Root.Shutdown();
            }
        }

        private void FinishLeaveCheck()
        {
            leaveDone = true;
            bool left = !leavePawn.Spawned && leaveReachedSurface;
            bool standingBelow = leavePawn.Spawned && leavePawn.Map == leaveLower;
            Record("visitor on the lower floor reaches the surface and leaves", left
                ? "PASS: the visitor reached the surface and left"
                : "FAIL: reached the surface " + leaveReachedSurface + ", spawned " + leavePawn.Spawned + " on "
                    + (leavePawn.Map == null ? "no map" : leavePawn.Map.ToString()) + ", job " + DescribeJob(leavePawn.CurJob)
                    + ", duty " + (leavePawn.mindState.duty == null ? "none" : leavePawn.mindState.duty.def.defName));
            Record("no pawn is left standing at a dead end", standingBelow
                ? "FAIL: the visitor stands on the lower floor with " + DescribeJob(leavePawn.CurJob)
                : "PASS");
            bool allOk = left && !standingBelow;
            Record("leave from lower", allOk ? "PASS" : "FAIL: a row above failed");
            finished = true;
            Root.Shutdown();
        }

        // A visitor on the lower floor beside the stairs up. The visitor's exit duty comes from a lord that leaves the map, as the game's visit and leave
        // lords do: the think tree reaches a duty only through its lord (ThinkNode_ConditionalHasLordDuty). The surface is the same tile's surface map.
        private void SetUpLeaveScene()
        {
            Map surface = Find.CurrentMap;
            ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
            RequireFloors(tracker, surface, 1);
            Building_StairsUp stairs = SetUpLowerStairs(surface, tracker, out Map lower);
            RequireFloors(tracker, surface, 2);
            CarveAround(lower, stairs.Position, 6f);
            leaveSurface = surface;
            leaveLower = lower;
            IntVec3 cell = FindReachableCellNear(lower, stairs.Position + new IntVec3(3, 0, 0), new List<IntVec3>());
            if (!cell.IsValid)
            {
                throw new InvalidOperationException("no reachable cell on the lower floor for the visitor");
            }
            Faction visitorFaction = Find.FactionManager.RandomNonHostileFaction(allowNonHumanlike: false);
            if (visitorFaction == null)
            {
                throw new InvalidOperationException("no non-hostile humanlike faction for a visitor");
            }
            leavePawn = PawnGenerator.GeneratePawn(visitorFaction.def.basicMemberKind ?? PawnKindDefOf.Colonist, visitorFaction);
            GenSpawn.Spawn(leavePawn, cell, lower);
            leaveLord = LordMaker.MakeNewLord(visitorFaction, new LordJob_ExitMapBest(LocomotionUrgency.Walk, canDig: true), lower, new List<Pawn> { leavePawn });
            Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            Record("leave scene", "INFO: the visitor from " + visitorFaction.Name + " stands at " + cell + " on the lower floor in a lord whose toil is "
                + leaveLord.CurLordToil.GetType().Name + "; its duty is " + (leavePawn.mindState.duty == null ? "none" : leavePawn.mindState.duty.def.defName)
                + "; the stairs up are at " + stairs.Position);
        }

        // Task 23 (idea "check the job on arrival"). A cross-floor job waits in the colonist's job list while the colonist walks to the stairs. Its target
        // is checked again on arrival. If the target is gone, the job ends with no error and no pause, and the colonist searches again. The walk takes
        // game time, so the check runs over several frames.
        private int arrivalStage;
        private int arrivalDueTick;
        private int arrivalErrors;
        private bool arrivalPaused;
        private bool arrivalWentDown;
        private Job arrivalLastJob;
        private bool arrivalDone;
        private Pawn arrivalColonist;
        private Map arrivalSurface;
        private IntVec3 arrivalStart;
        private Job arrivalFirstStep;
        private Thing arrivalSteel;
        private Thing arrivalSecondSteel;

        private void ArrivalLogCallback(string condition, string stackTrace, UnityEngine.LogType type)
        {
            if (type == UnityEngine.LogType.Error || type == UnityEngine.LogType.Exception)
            {
                arrivalErrors++;
            }
        }

        private void RunJobTakenOnArrivalTick()
        {
            if (arrivalDone)
            {
                return;
            }
            try
            {
                if (arrivalStage == 0)
                {
                    SetUpArrivalScene();
                    arrivalStage = 1;
                    arrivalDueTick = Find.TickManager.TicksGame + 1200;
                    return;
                }
                if (Find.TickManager.CurTimeSpeed == TimeSpeed.Paused)
                {
                    arrivalPaused = true;
                }
                int now = Find.TickManager.TicksGame;
                if (arrivalColonist.CurJob != arrivalLastJob)
                {
                    arrivalLastJob = arrivalColonist.CurJob;
                    Record("job trace", "INFO: " + DescribeJob(arrivalLastJob) + " at " + arrivalColonist.Position + " on " + arrivalColonist.Map);
                }
                if (arrivalColonist.Map != arrivalSurface)
                {
                    // The colonist took the stairs: the outcome is settled.
                    arrivalWentDown = true;
                    FinishArrivalCheck();
                    return;
                }
                if (arrivalPaused || now > arrivalDueTick)
                {
                    FinishArrivalCheck();
                    return;
                }
                if (arrivalStage == 1)
                {
                    // The steel is taken away, not destroyed: it leaves the map and nothing carries it. The colonist has left its start cell, so it is still on the walk.
                    if (!OnStaleWalk())
                    {
                        FinishArrivalCheck();
                        return;
                    }
                    if (arrivalColonist.Position != arrivalStart)
                    {
                        arrivalSteel.DeSpawn();
                        arrivalSecondSteel = SpawnArrivalSecondSteel();
                        arrivalStage = 2;
                        arrivalDueTick = now + 3000;
                    }
                    return;
                }
                if (HauledSecondSteel())
                {
                    FinishArrivalCheck();
                }
            }
            catch (Exception exception)
            {
                arrivalDone = true;
                UnityEngine.Application.logMessageReceived -= ArrivalLogCallback;
                Record("job taken on arrival", "FAIL: " + exception);
                Log.Error("Z-Levels job taken on arrival validation failed: " + exception);
                finished = true;
                Root.Shutdown();
            }
        }

        // The colonist is still on the walk that started the check: its job targets the steel that was taken away. Job objects are pooled and can
        // come back as a new job, so the checks read what a job targets, not which object it is.
        private bool OnStaleWalk()
        {
            Job job = arrivalColonist.CurJob;
            return job != null && job.targetA.Thing == arrivalSteel;
        }

        // The colonist has taken the haul of the second steel on the surface.
        private bool HauledSecondSteel()
        {
            Job job = arrivalColonist.CurJob;
            return job != null && job.targetA.Thing == arrivalSecondSteel && arrivalColonist.Map == arrivalSurface;
        }

        private void FinishArrivalCheck()
        {
            arrivalDone = true;
            UnityEngine.Application.logMessageReceived -= ArrivalLogCallback;
            Job job = arrivalColonist.CurJob;
            bool leftWalk = !OnStaleWalk();
            bool onSurface = arrivalColonist.Map == arrivalSurface;
            bool hauled = HauledSecondSteel();
            Record("target taken during the walk: colonist searches again", !arrivalWentDown && leftWalk && onSurface
                ? "PASS: the colonist dropped the walk and searched on the surface, now " + DescribeJob(job)
                : arrivalWentDown
                    ? "FAIL: the colonist took the stairs down for a steel that was already gone"
                    : "FAIL: the colonist has " + DescribeJob(job) + " on " + (onSurface ? "the surface" : "another floor")
                        + (OnStaleWalk() ? ", still on the walk" : ""));
            Record("no error and no pause", arrivalErrors == 0 && !arrivalPaused ? "PASS" : "FAIL: " + arrivalErrors + " errors logged, paused " + arrivalPaused);
            Record("a new job is taken", hauled ? "PASS: " + DescribeJob(job) : "FAIL: no haul of the second steel; the colonist has " + DescribeJob(job));
            if (!hauled)
            {
                bool listed = arrivalSurface.listerHaulables.ThingsPotentiallyNeedingHauling().Contains(arrivalSecondSteel);
                bool hasStore = StoreUtility.TryFindBestBetterStoreCellFor(arrivalSecondSteel, arrivalColonist, arrivalSurface, StoragePriority.Unstored, arrivalColonist.Faction, out IntVec3 storeCell);
                string control;
                try
                {
                    control = DescribeJob(SearchWorkJob(arrivalColonist));
                }
                catch (Exception exception)
                {
                    control = "the search threw " + exception.GetType().Name + ": " + exception.Message;
                }
                object lookMap = typeof(ZLogger).Assembly.GetType("ZLevels.ZUtils", throwOnError: true)
                    .GetField("currentLookedIntoMap", BindingFlags.Public | BindingFlags.Static).GetValue(null);
                Record("second steel diagnostic", "INFO: listed as haulable " + listed + "; store cell " + (hasStore ? storeCell.ToString() : "none")
                    + "; steel at " + arrivalSecondSteel.Position + " spawned " + arrivalSecondSteel.Spawned + "; control search " + control
                    + "; look-up map " + (lookMap == null ? "none" : lookMap.ToString()));
            }
            bool allOk = !arrivalWentDown && leftWalk && onSurface && hauled && arrivalErrors == 0 && !arrivalPaused;
            Record("job taken on arrival", allOk ? "PASS" : "FAIL: a row above failed");
            finished = true;
            Root.Shutdown();
        }

        // A surface colonist eight to twelve cells from the surface stairs down, with steel on the lower floor beside the stairs up and an empty
        // stockpile on the surface. The first step is the search's own result. The second steel is placed on the surface only once the walk has
        // begun, so the first search still finds the lower steel.
        private void SetUpArrivalScene()
        {
            Map surface = Find.CurrentMap;
            ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
            UnityEngine.Application.logMessageReceived += ArrivalLogCallback;
            RequireFloors(tracker, surface, 1);
            Building_StairsUp stairs = SetUpLowerStairs(surface, tracker, out Map lower);
            RequireFloors(tracker, surface, 2);
            Building_StairsDown downStairs = surface.listerThings.AllThings.OfType<Building_StairsDown>().First();
            arrivalSurface = surface;

            CarveAround(lower, stairs.Position, 6f);
            IntVec3 steelCell = FindReachableCellNear(lower, stairs.Position + new IntVec3(2, 0, 0), new List<IntVec3>());
            if (!steelCell.IsValid)
            {
                throw new InvalidOperationException("no reachable cell on the lower floor for the steel");
            }
            arrivalSteel = ThingMaker.MakeThing(ThingDefOf.Steel);
            arrivalSteel.stackCount = 30;
            GenSpawn.Spawn(arrivalSteel, steelCell, lower);

            IntVec3 stockNear = FindOpenBlock(surface, 1, 1);
            if (!stockNear.IsValid)
            {
                throw new InvalidOperationException("no open cell on the surface for the stockpile");
            }
            Zone_Stockpile stockpile = MakeSupplyZone(surface, stockNear);

            IntVec3 walkNear = IntVec3.Invalid;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(downStairs.Position, 12f, false))
            {
                if (cell.DistanceTo(downStairs.Position) < 8f || !cell.InBounds(surface) || !cell.Standable(surface) || cell.GetEdifice(surface) != null
                    || cell.GetFirstItem(surface) != null || surface.zoneManager.ZoneAt(cell) != null)
                {
                    continue;
                }
                if (!surface.reachability.CanReach(cell, downStairs.Position, PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassDoors)))
                {
                    continue;
                }
                walkNear = cell;
                break;
            }
            if (!walkNear.IsValid)
            {
                throw new InvalidOperationException("no reachable cell eight to twelve cells from the surface stairs");
            }
            arrivalColonist = SpawnCapableColonistNear(surface, walkNear, new List<IntVec3>(), WorkTypeDefOf.Hauling);
            arrivalStart = arrivalColonist.Position;
            SetWork(arrivalColonist, WorkTypeDefOf.Hauling, 3, WorkTypeDefOf.Mining, 0);
            // The other colonists keep their work but do not haul: one of them would store the second steel before the check runs.
            foreach (Pawn other in surface.mapPawns.FreeColonistsSpawned.Where(p => p != arrivalColonist).ToList())
            {
                other.workSettings.SetPriority(WorkTypeDefOf.Hauling, 0);
            }

            Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            arrivalFirstStep = SearchWorkJob(arrivalColonist);
            if (arrivalFirstStep == null)
            {
                throw new InvalidOperationException("the colonist's search found no job");
            }
            Job mainJob = MainWorkJob(tracker, arrivalColonist, arrivalFirstStep);
            if (mainJob.targetA.Thing != arrivalSteel)
            {
                throw new InvalidOperationException("the search took " + DescribeJob(mainJob) + " instead of the steel on the lower floor");
            }
            arrivalColonist.jobs.StartJob(arrivalFirstStep);
            Record("arrival scene", "INFO: the colonist walks from " + arrivalStart + " toward the surface stairs at " + downStairs.Position + " with "
                + DescribeJob(arrivalFirstStep) + "; the haul is " + DescribeJob(mainJob) + "; stockpile " + stockpile.label);
        }

        // A free surface cell near the colonist's start, off the stockpile, for the second steel. Returns the steel.
        private Thing SpawnArrivalSecondSteel()
        {
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(arrivalStart, 6f, false))
            {
                if (!cell.InBounds(arrivalSurface) || !cell.Standable(arrivalSurface) || cell.GetEdifice(arrivalSurface) != null
                    || cell.GetFirstItem(arrivalSurface) != null || arrivalSurface.zoneManager.ZoneAt(cell) != null)
                {
                    continue;
                }
                if (!arrivalSurface.reachability.CanReach(arrivalStart, cell, PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassDoors)))
                {
                    continue;
                }
                Thing second = ThingMaker.MakeThing(ThingDefOf.Steel);
                second.stackCount = 10;
                GenSpawn.Spawn(second, cell, arrivalSurface);
                Record("second steel", "INFO: " + second + " placed " + second.stackCount + " steel at " + cell + " on the surface; spawned " + second.Spawned);
                return second;
            }
            throw new InvalidOperationException("no free surface cell near " + arrivalStart + " for the second steel");
        }

        // Items the lower floor needs are marked in the background (idea "items checked in the background"). A colonist on the lower floor is offered
        // the surface items that are marked for its floor. Before the change every surface item is offered, so rows 2 and 3 fail first.
        private void RunBackgroundHaulCheck()
        {
            const string label = "background haul";
            Rand.PushState(20261110);
            try
            {
                Map surface = Find.CurrentMap;
                ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
                RequireFloors(tracker, surface, 1);
                Building_StairsUp stairs = SetUpLowerStairs(surface, tracker, out Map lower);
                lower.fogGrid.ClearAllFog();
                RequireFloors(tracker, surface, 2);
                // Plants and items may stand in the block, so the block is found by its ground alone and then cleared.
                IntVec3 block = FindStandableBlock(surface, 9, 9);
                if (!block.IsValid)
                {
                    throw new InvalidOperationException("no standable 9 by 9 block on the surface away from the edges");
                }
                for (int i = 0; i < 9; i++)
                {
                    for (int j = 0; j < 9; j++)
                    {
                        ClearCell(surface, block + new IntVec3(i, 0, j));
                    }
                }
                List<IntVec3> avoid = new List<IntVec3>();

                // The lower floor needs steel: a steel wall blueprint there. The steel and a wood log lie on the surface.
                ThingDef wallDef = Def("Wall");
                // The lower floor is rock until it is carved, and a wall needs open ground away from the map edge. The stairs can stand near the edge.
                IntVec3 wallCell = IntVec3.Invalid;
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(stairs.Position, 14f, false))
                {
                    if (!InsideEdgeMargin(lower, cell))
                    {
                        continue;
                    }
                    CarveAround(lower, cell, 3f);
                    if (GenConstruct.CanPlaceBlueprintAt(wallDef, cell, Rot4.North, lower).Accepted)
                    {
                        wallCell = cell;
                        break;
                    }
                }
                if (!wallCell.IsValid)
                {
                    throw new InvalidOperationException("no cell on the lower floor within 14 of the stairs takes a wall blueprint");
                }
                avoid.Add(wallCell);
                Blueprint_Build wall = GenConstruct.PlaceBlueprintForBuild(wallDef, wallCell, lower, Rot4.North, Faction.OfPlayer, ThingDefOf.Steel);
                IntVec3 steelCell = block + new IntVec3(7, 0, 7);
                ClearCell(surface, steelCell);
                Thing steel = ThingMaker.MakeThing(ThingDefOf.Steel);
                steel.stackCount = 10;
                GenSpawn.Spawn(steel, steelCell, surface);
                IntVec3 logCell = block + new IntVec3(8, 0, 2);
                ClearCell(surface, logCell);
                Thing log = ThingMaker.MakeThing(ThingDefOf.WoodLog);
                log.stackCount = 10;
                GenSpawn.Spawn(log, logCell, surface);
                Pawn lowerColonist = SpawnCapableColonistNear(lower, stairs.Position, avoid, WorkTypeDefOf.Hauling);
                SetWork(lowerColonist, WorkTypeDefOf.Hauling, 3, WorkTypeDefOf.Mining, 0);

                // Row 1. The steel is needed below, so it is marked within 2000 ticks and the lower colonist is offered it.
                int offeredAfter = TickUntil(tracker, 2000, () => OfferedHauls(lowerColonist).Contains(steel));
                bool row1 = offeredAfter >= 0;
                int lowerIndex = tracker.GetZIndexFor(lower);
                Record("surface item needed on the lower floor is marked within 2000 ticks", row1 ? "PASS: offered after " + offeredAfter + " ticks"
                    : "FAIL: not offered within 2000 ticks; the blueprint still needs " + wall.ThingCountNeeded(ThingDefOf.Steel) + " steel, the lower floor is index " + lowerIndex
                    + ", the steel is marked for it " + ItemFloorMarks.IsMarkedFor(steel, lowerIndex) + ", the steel is a surface haul " + surface.listerHaulables.ThingsPotentiallyNeedingHauling().Contains(steel)
                    + ", the tick is " + Find.TickManager.TicksGame);

                // Rows 2 and 3. Nothing below needs the steel any more, and the lower stockpile is full. The steel and the log are then unmarked.
                wall.Destroy();
                CarveAround(lower, stairs.Position + new IntVec3(3, 0, 0), 4f);
                Zone_Stockpile lowerStock = MakeSupplyZone(lower, stairs.Position + new IntVec3(3, 0, 0));
                if (lowerStock.Cells.Count == 0)
                {
                    throw new InvalidOperationException("no stockpile cells on the lower floor");
                }
                // A wood log on each cell fills the stockpile for steel. A building would remove the cell from the zone, so the zone would be gone.
                foreach (IntVec3 cell in lowerStock.Cells.ToList())
                {
                    ClearCell(lower, cell);
                    Thing filler = ThingMaker.MakeThing(ThingDefOf.WoodLog);
                    filler.stackCount = 10;
                    GenSpawn.Spawn(filler, cell, lower);
                }
                TickUntil(tracker, 2000, () => false);
                bool steelOffered = OfferedHauls(lowerColonist).Contains(steel);
                Record("a full destination stops new marks", steelOffered ? "FAIL: the steel is still offered to the lower floor" : "PASS");
                bool logOffered = OfferedHauls(lowerColonist).Contains(log);
                Record("a lower-floor colonist does not offer an unmarked surface item", logOffered ? "FAIL: the surface log is offered" : "PASS");

                // Row 4. The steel is hauled into the lower stockpile, which frees one cell for it. Stored items are not offered, marked or not.
                IntVec3 storedCell = lowerStock.Cells[0];
                ClearCell(lower, storedCell);
                steel.DeSpawn();
                GenSpawn.Spawn(steel, storedCell, lower);
                TickUntil(tracker, 2000, () => false);
                bool hauledOffered = OfferedHauls(lowerColonist).Contains(steel);
                Record("a marked item is unmarked after it is hauled", hauledOffered ? "FAIL: the hauled steel is still offered" : "PASS");

                bool all = row1 && !steelOffered && !logOffered && !hauledOffered;
                Record(label, all ? "PASS" : "FAIL: a row above failed");
            }
            catch (Exception exception)
            {
                Record(label, "FAIL: " + exception);
                Log.Error("Z-Levels background haul validation failed: " + exception);
            }
            finally
            {
                Rand.PopState();
            }
            Root.Shutdown();
        }

        // A cell at least eight cells from every map edge, where blueprints and the placement check are not refused for the edge.
        private static bool InsideEdgeMargin(Map map, IntVec3 cell)
        {
            return cell.x >= 8 && cell.z >= 8 && cell.x < map.Size.x - 8 && cell.z < map.Size.z - 8;
        }

        // A block of standable ground with no buildings, away from the map edges. Things on the ground are allowed; the caller clears them.
        private static IntVec3 FindStandableBlock(Map map, int width, int height)
        {
            for (int x = 10; x < map.Size.x - width - 10; x++)
            {
                for (int z = 10; z < map.Size.z - height - 10; z++)
                {
                    bool open = true;
                    for (int i = 0; i < width && open; i++)
                    {
                        for (int j = 0; j < height && open; j++)
                        {
                            IntVec3 cell = new IntVec3(x + i, 0, z + j);
                            open = cell.Standable(map) && cell.GetEdifice(map) == null;
                        }
                    }
                    if (open)
                    {
                        return new IntVec3(x, 0, z);
                    }
                }
            }
            return IntVec3.Invalid;
        }

        // The surface items that the lower colonist's haul search offers. The offer is the patched PotentialWorkThingsGlobal of the haul giver.
        private static List<Thing> OfferedHauls(Pawn pawn)
        {
            WorkGiver_Haul giver = (WorkGiver_Haul)DefDatabase<WorkGiverDef>.GetNamed("HaulGeneral").Worker;
            return giver.PotentialWorkThingsGlobal(pawn).ToList();
        }

        // Advances the game clock one tick at a time and runs the Z-Levels tick each time, so a periodic refresh runs on its own tick. Returns the
        // tick count at which the condition first holds, or -1 when it never holds within maxTicks.
        private static int TickUntil(ZLevelsManager tracker, int maxTicks, Func<bool> condition)
        {
            int start = Find.TickManager.TicksGame;
            for (int step = 0; step <= maxTicks; step++)
            {
                Find.TickManager.DebugSetTicksGame(start + step);
                tracker.GameComponentTick();
                if (condition())
                {
                    return step;
                }
            }
            return -1;
        }

        // A tend job without medicine went into the Z-Levels job list as a haul with no target. The colonist's driver then failed when it started
        // the job, and the colonist tried again. The row fails before the fix and passes after it. (A refuel job cannot lack fuel here: vanilla
        // CanRefuel returns false when no fuel is reachable, before any job is made.)
        private void RunJobTargetCheck()
        {
            const string label = "job targets";
            Rand.PushState(20261109);
            try
            {
                Map surface = Find.CurrentMap;
                ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
                RequireFloors(tracker, surface, 1);
                IntVec3 block = FindCentralOpenBlock(surface, 9, 9);
                if (!block.IsValid)
                {
                    throw new InvalidOperationException("no open 9 by 9 block on the surface away from the edges");
                }
                List<IntVec3> avoid = new List<IntVec3>();

                // Row 1. A tend job for a patient, with no medicine on the surface.
                foreach (Thing medicine in surface.listerThings.ThingsInGroup(ThingRequestGroup.Medicine).ToList())
                {
                    medicine.Destroy();
                }
                Pawn doctor = SpawnColonistNear(surface, block + new IntVec3(2, 0, 2), avoid);
                Pawn patient = SpawnColonistNear(surface, block + new IntVec3(4, 0, 4), avoid);
                Job tend = JobPatches.TryIssueJobPackagePatch.TendJobOnThing(doctor, patient);
                if (tend == null || tend.targetB.HasThing)
                {
                    throw new InvalidOperationException("the tend job did not come back without medicine");
                }
                tracker.ResetJobTrackerFor(doctor);
                tracker.BuildJobListFor(doctor, surface, tend);
                List<Job> queued = tracker.jobTracker[doctor].activeJobs ?? new List<Job>();
                Job emptyHaul = queued.FirstOrDefault(job => job.def == ZLevelsDefOf.ZL_HaulThingToDest && !job.targetA.HasThing);
                bool tendOk = emptyHaul == null;
                Record("tend without medicine makes no haul with no target", tendOk ? "PASS" : "FAIL: a " + emptyHaul.def.defName + " with no target; the job list is " + string.Join(", ", queued.Select(job => job.def.defName)));

                Record(label, tendOk ? "PASS" : "FAIL: a row above failed");
            }
            catch (Exception exception)
            {
                Record(label, "FAIL: " + exception);
                Log.Error("Z-Levels job target validation failed: " + exception);
            }
            finally
            {
                Rand.PopState();
            }
            Root.Shutdown();
        }

        private void RunOwnFloorFirstCheck()
        {
            const string label = "own floor first";
            Rand.PushState(20261108);
            // The threshold reads the priorities the game shows. The game reports priority 3 for every work type while "use work priorities"
            // is off, so this check turns the setting on for its own length and restores it afterwards.
            bool previousUseWorkPriorities = Find.PlaySettings.useWorkPriorities;
            Find.PlaySettings.useWorkPriorities = true;
            try
            {
                Record("use work priorities during the check", "INFO: was " + (previousUseWorkPriorities ? "on" : "off") + ", on for this check");
                Map surface = Find.CurrentMap;
                ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
                RequireFloors(tracker, surface, 1);
                // The stairs join the floors, so the colony searches the lower floor. Without stairs the search does not reach it.
                Building_StairsUp stairs = SetUpLowerStairs(surface, tracker, out Map lower);
                lower.fogGrid.ClearAllFog();
                RequireFloors(tracker, surface, 2);

                // A stockpile and three steel stacks outside it on the surface, and the colonists near the middle of the open block.
                IntVec3 block = FindCentralOpenBlock(surface, 9, 9);
                if (!block.IsValid)
                {
                    throw new InvalidOperationException("no open 9 by 9 block on the surface away from the edges");
                }
                IntVec3 center = block + new IntVec3(4, 0, 4);
                Zone_Stockpile stockpile = MakeSupplyZone(surface, center);
                foreach (IntVec3 corner in new[] { block, block + new IntVec3(8, 0, 0), block + new IntVec3(0, 0, 8) })
                {
                    if (stockpile.ContainsCell(corner))
                    {
                        throw new InvalidOperationException("a steel stack would start inside the stockpile at " + corner);
                    }
                    ClearCell(surface, corner);
                    Thing steel = ThingMaker.MakeThing(ThingDefOf.Steel);
                    steel.stackCount = 10;
                    GenSpawn.Spawn(steel, corner, surface);
                }
                List<IntVec3> avoid = new List<IntVec3>();

                // Row 1. A colonist with priority-3 hauling and mining, and nothing to mine below. Its own floor has the haul.
                Pawn plain = SpawnCapableColonistNear(surface, block + new IntVec3(4, 0, 6), avoid, WorkTypeDefOf.Hauling, WorkTypeDefOf.Mining);
                SetWork(plain, WorkTypeDefOf.Hauling, 3, WorkTypeDefOf.Mining, 3);
                Record("own floor first work list", "INFO: " + string.Join(", ", plain.workSettings.WorkGiversInOrderNormal.Select(giver => giver.def.defName)));
                int runsBefore = FloorPolicy.OtherFloorSearchRuns;
                Job plainJob = SearchWorkJob(plain);
                int otherRuns = FloorPolicy.OtherFloorSearchRuns - runsBefore;
                bool plainOk = otherRuns == 0 && IsSurfaceJob(plainJob, surface);
                Record("own-floor work: zero searches on other floors", plainOk ? "PASS" : "FAIL: " + otherRuns + " searches on other floors, job " + DescribeOwnFloorJob(plainJob));

                // Row 2. A priority-1 mining job on the lower floor, and a priority-3 haul on the surface. The mining job must win.
                IntVec3 rock = FindMineableNextToOpen(lower, stairs.Position + new IntVec3(1, 0, 0));
                if (!rock.IsValid)
                {
                    throw new InvalidOperationException("no mineable rock next to an open cell on the lower floor");
                }
                Designation mine = new Designation(rock, DesignationDefOf.Mine);
                lower.designationManager.AddDesignation(mine);
                Pawn digger = SpawnCapableMiner(surface, stairs.Position);
                SetWork(digger, WorkTypeDefOf.Hauling, 3, WorkTypeDefOf.Mining, 1);
                // A job on another floor comes back as its first step (ZL_GoToMap). The work itself is the tracker's mainJob.
                Job diggerJob = SearchWorkJob(digger);
                Job diggerWork = MainWorkJob(tracker, digger, diggerJob);
                bool diggerOk = diggerWork != null && diggerWork.def == JobDefOf.Mine && diggerWork.targetA.Thing != null && diggerWork.targetA.Thing.Map == lower;
                Record("priority-1 work upstairs beats priority-3 work on the own floor", diggerOk ? "PASS" : "FAIL: the work is " + DescribeOwnFloorJob(diggerWork) + ", first step " + DescribeOwnFloorJob(diggerJob));

                // Row 3. Nothing on the lower floor, and a priority-3 haul on the surface.
                lower.designationManager.RemoveDesignation(mine);
                Pawn hauler = SpawnCapableColonistNear(surface, block + new IntVec3(6, 0, 6), avoid, WorkTypeDefOf.Hauling);
                SetWork(hauler, WorkTypeDefOf.Hauling, 3, WorkTypeDefOf.Mining, 0);
                Job haulerJob = SearchWorkJob(hauler);
                bool haulerOk = IsSurfaceJob(haulerJob, surface);
                Record("priority-3 work with nothing upstairs is taken", haulerOk ? "PASS" : "FAIL: the job is " + DescribeOwnFloorJob(haulerJob));

                // Row 5. High-priority mining has nothing to do, and the colonist's priority-3 haul is at home. The search that looks upstairs for the
                // mining must hand the colonist back to its own floor before the lower-priority search runs there.
                Pawn displaced = SpawnCapableColonistNear(surface, block + new IntVec3(6, 0, 4), avoid, WorkTypeDefOf.Hauling, WorkTypeDefOf.Mining);
                SetWork(displaced, WorkTypeDefOf.Hauling, 3, WorkTypeDefOf.Mining, 1);
                Job displacedJob = SearchWorkJob(displaced);
                bool displacedOk = IsSurfaceJob(displacedJob, surface);
                Record("high-priority work upstairs is empty, and the haul at home is taken", displacedOk ? "PASS" : "FAIL: the job is " + DescribeOwnFloorJob(displacedJob));

                // Row 4. The threshold setting, read by name, is 2 on a new settings object.
                Type settingsType = Type.GetType("ZLevels.ZLevelsSettings, ZLevels");
                FieldInfo threshold = settingsType == null ? null : settingsType.GetField("WorkGiverHighPriorityThreshold", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object value = threshold == null ? null : threshold.GetValue(Activator.CreateInstance(settingsType));
                bool defaultTwo = value is int && (int)value == 2;
                Record("threshold setting is 2 by default", defaultTwo ? "PASS" : "FAIL: the setting is " + (value == null ? "missing" : value.ToString()));

                bool all = plainOk && diggerOk && haulerOk && displacedOk && defaultTwo;
                Record(label, all ? "PASS" : "FAIL: a row above failed");
            }
            catch (Exception exception)
            {
                Record(label, "FAIL: " + exception);
                Log.Error("Z-Levels own floor first validation failed: " + exception);
            }
            finally
            {
                Find.PlaySettings.useWorkPriorities = previousUseWorkPriorities;
                Rand.PopState();
            }
            Root.Shutdown();
        }

        private static void SetWork(Pawn pawn, WorkTypeDef first, int firstPriority, WorkTypeDef second, int secondPriority)
        {
            pawn.workSettings.DisableAll();
            pawn.workSettings.SetPriority(first, firstPriority);
            pawn.workSettings.SetPriority(second, secondPriority);
        }

        private static bool IsSurfaceJob(Job job, Map surface)
        {
            return job != null && job.targetA.Thing != null && job.targetA.Thing.Map == surface;
        }

        // The work job behind a search result. A job on another floor comes back as its first step, and the tracker keeps the work job as mainJob.
        private static Job MainWorkJob(ZLevelsManager tracker, Pawn pawn, Job firstStep)
        {
            if (tracker.jobTracker != null && tracker.jobTracker.TryGetValue(pawn, out JobTracker claims) && claims.mainJob != null)
            {
                return claims.mainJob;
            }
            return firstStep;
        }

        private static string DescribeOwnFloorJob(Job job)
        {
            if (job == null)
            {
                return "none";
            }
            return job.def.defName + " on " + (job.targetA.Thing == null ? "no thing" : job.targetA.Thing.ToString() + " on " + job.targetA.Thing.Map);
        }

        // A colonist on the surface beside the lower stairs, whose position maps onto the carved lower area, and who can mine. The copied search runs
        // on the lower floor at the colonist's surface position, so that position must be open there.
        private static Pawn SpawnCapableMiner(Map surface, IntVec3 stairsCell)
        {
            foreach (IntVec3 offset in new[] { new IntVec3(1, 0, 0), new IntVec3(-1, 0, 0), new IntVec3(0, 0, 1), new IntVec3(0, 0, -1) })
            {
                IntVec3 cell = stairsCell + offset;
                if (!cell.Standable(surface) || cell.GetEdifice(surface) != null || cell.GetFirstItem(surface) != null)
                {
                    continue;
                }
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    Pawn candidate = SpawnPawnAt(surface, cell);
                    // Row 2 sets hauling as well as mining, and SetWork logs an error for a work type that is disabled.
                    if (!candidate.WorkTypeIsDisabled(WorkTypeDefOf.Mining) && !candidate.WorkTypeIsDisabled(WorkTypeDefOf.Hauling))
                    {
                        return candidate;
                    }
                    candidate.Destroy(DestroyMode.Vanish);
                }
            }
            throw new InvalidOperationException("no colonist who can mine and haul could be placed beside the stairs");
        }

        // A colonist near the cell who is not disabled from any of the work types. Some backstories disable work types, and SetWork cannot give a
        // disabled work type a priority, so a colonist who has one disabled is removed and another is spawned, up to ten times.
        private static Pawn SpawnCapableColonistNear(Map map, IntVec3 near, List<IntVec3> avoid, params WorkTypeDef[] needed)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                Pawn candidate = SpawnColonistNear(map, near, avoid);
                if (needed.All(work => !candidate.WorkTypeIsDisabled(work)))
                {
                    return candidate;
                }
                candidate.Destroy(DestroyMode.Vanish);
            }
            throw new InvalidOperationException("no colonist who can " + string.Join(" and ", needed.Select(work => work.defName)) + " could be placed near " + near);
        }

        // A mineable rock next to an open cell that the start cell can reach, within eight cells of the start cell.
        private static IntVec3 FindMineableNextToOpen(Map map, IntVec3 start)
        {
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(start, 8f, false))
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }
                Building rock = cell.GetEdifice(map);
                if (rock == null || !rock.def.mineable)
                {
                    continue;
                }
                foreach (IntVec3 direction in GenAdj.CardinalDirections)
                {
                    IntVec3 neighbor = cell + direction;
                    if (neighbor.InBounds(map) && neighbor.Standable(map) && map.reachability.CanReach(start, neighbor, PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassDoors)))
                    {
                        return cell;
                    }
                }
            }
            return IntVec3.Invalid;
        }

        private static bool HasClaim(ZLevelsManager tracker, Pawn pawn, Thing thing)
        {
            return tracker.jobTracker != null && tracker.jobTracker.TryGetValue(pawn, out JobTracker claims) && claims.reservedThings != null
                && claims.reservedThings.Any(target => target.Thing == thing);
        }

        private static Pawn FindPawnById(int id)
        {
            foreach (Map map in Find.Maps)
            {
                foreach (Pawn pawn in map.mapPawns.AllPawns)
                {
                    if (pawn.thingIDNumber == id) return pawn;
                }
            }
            return null;
        }
    }
}
