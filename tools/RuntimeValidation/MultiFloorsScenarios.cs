using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
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
                Pawn locked = SpawnPawnAt(lower, lockedCell);
                Pawn free = SpawnPawnAt(lower, freeCell);
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

        private static Pawn SpawnPawnAt(Map map, IntVec3 cell)
        {
            Pawn pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            GenSpawn.Spawn(pawn, cell, map);
            return pawn;
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
                Record("idle search pause", noWork && searched && atMostThree ? "PASS" : "FAIL: a row above failed");
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
