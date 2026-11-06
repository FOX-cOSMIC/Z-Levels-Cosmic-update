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
