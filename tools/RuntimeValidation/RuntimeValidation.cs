using System;
using System.IO;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;
using Verse.AI;
using ZLevels;

namespace ZLevelsValidation
{
    public class RuntimeValidation : GameComponent
    {
        private int stage;
        private Pawn pawn;
        private Map surface;
        private Map lower;
        private Map upper;
        private DateTime deadline = DateTime.UtcNow.AddMinutes(4);
        private bool finished;
        private bool reloaded;
        private Thing hauledThing;
        private IntVec3 haulDestination;
        private CompPowerZTransmitter surfaceTransmitter;
        private CompPowerZTransmitter lowerTransmitter;
        private CompPowerTrader lamp;
        private ThingWithComps powerSource;
        private DateTime shotDue;
        private int firstPoweredTick = -1;
        private int lastUnpoweredTick;
        private int stageTick;
        private IntVec3 powerCell;
        private bool belowAtSave;
        private IntVec3 savedPosition;
        private Thing unforcedWood;
        private Zone_Stockpile unforcedZone;
        private bool wentDownForWood;

        public RuntimeValidation(Game game) { }

        public override void LoadedGame()
        {
            reloaded = true;
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref stage, "validationStage");
            Scribe_References.Look(ref pawn, "validationPawn");
            Scribe_References.Look(ref surface, "validationSurface");
            Scribe_References.Look(ref lower, "validationLower");
            Scribe_References.Look(ref upper, "validationUpper");
            Scribe_Values.Look(ref belowAtSave, "validationBelowAtSave");
            Scribe_Values.Look<IntVec3>(ref savedPosition, "validationSavedPosition", IntVec3.Invalid);
        }

        public override void GameComponentUpdate()
        {
            if (!GenCommandLine.CommandLineArgPassed("zlevelsvalidation") || finished)
                return;
            try
            {
                if (DateTime.UtcNow > deadline)
                    throw new InvalidOperationException("Timed out at stage " + stage + ", pawn job=" + pawn?.CurJob + ", pawn at " + pawn?.Position + " on " + pawn?.Map + ", " + DescribeWood());
                if (Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting)
                    return;
                if (!ZLogger.DebugEnabled && GenCommandLine.CommandLineArgPassed("zlevelsdebug"))
                {
                    // ZLevelsMod and ZLevelsSettings are internal to the port, so the flag is set by reflection.
                    var settings = typeof(ZLogger).Assembly.GetType("ZLevels.ZLevelsMod", throwOnError: true)
                        .GetField("settings", BindingFlags.Public | BindingFlags.Static).GetValue(null);
                    settings.GetType().GetField("DebugEnabled", BindingFlags.Public | BindingFlags.Instance).SetValue(settings, true);
                }
                if (GenCommandLine.CommandLineArgPassed("zlevelsinfestation"))
                {
                    finished = true;
                    RunInfestationGuard();
                    return;
                }
                Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
                switch (stage)
                {
                    case 0:
                        surface = Find.CurrentMap;
                        pawn = surface.mapPawns.FreeColonistsSpawned.First();
                        RestNeeds(pawn);
                        Require(Current.Game.GetComponent<ZLevelsManager>().GetZIndexFor(surface) == 0, "surface registered");
                        Record("fresh colony", "PASS");
                        var down = SpawnStair<Building_StairsDown>(ZLevelsDefOf.ZL_StairsDown, pawn.Position + new IntVec3(3, 0, 0));
                        Next(1);
                        down.GiveJob(pawn, down);
                        break;
                    case 1:
                        if (pawn.Map == surface) return;
                        lower = pawn.Map;
                        Require(Current.Game.GetComponent<ZLevelsManager>().GetZIndexFor(lower) == -1, "lower index");
                        Record("stairs down and underground generation", "PASS");
                        Record("water census", "surface: " + WaterCensus(surface) + " (mutators " + MutatorNames(surface) + "); lower: " + WaterCensus(lower) + " (mutators " + MutatorNames(lower) + "); same tile: " + (surface.Tile == lower.Tile));
                        Record("lower animals at arrival", AnimalCensus(lower));
                        OpenLowerRoom(lower);
                        Require(CountReachableFromStairs(lower, out _) >= 25, "lower room is open");
                        Record("lower walkable reachability", WalkableReachability(lower));
                        Record("lower stairs neighbourhood", StairsNeighbourhood(lower, pawn.Position));
                        Record("lower stairs surroundings", StairsSurroundings(lower));
                        var upFromLower = lower.listerThings.AllThings.OfType<Building_StairsUp>().First();
                        Next(2);
                        upFromLower.GiveJob(pawn, upFromLower);
                        break;
                    case 2:
                        if (pawn.Map != surface) return;
                        Record("return upstairs", "PASS");
                        var up = SpawnStair<Building_StairsUp>(ZLevelsDefOf.ZL_StairsUp, pawn.Position + new IntVec3(-3, 0, 0));
                        Next(3);
                        up.GiveJob(pawn, up);
                        break;
                    case 3:
                        if (pawn.Map == surface) return;
                        upper = pawn.Map;
                        Require(Current.Game.GetComponent<ZLevelsManager>().GetZIndexFor(upper) == 1, "upper index");
                        Record("stairs up and upper generation", "PASS");
                        var downFromUpper = upper.listerThings.AllThings.OfType<Building_StairsDown>().First();
                        Next(4);
                        downFromUpper.GiveJob(pawn, downFromUpper);
                        break;
                    case 4:
                        if (pawn.Map != surface) return;
                        Record("return downstairs", "PASS");
                        Require(Current.Game.GetComponent<ZLevelsManager>().GetAllMaps(surface.Tile).Count == 3, "all levels tracked");
                        var lowerStair = lower.listerThings.AllThings.OfType<Building_StairsUp>().First();
                        haulDestination = lowerStair.Position + new IntVec3(1, 0, 0);
                        ClearCell(lower, haulDestination);
                        lower.terrainGrid.SetTerrain(haulDestination, TerrainDefOf.Concrete);
                        var wood = ThingMaker.MakeThing(ThingDefOf.WoodLog);
                        wood.stackCount = 7;
                        hauledThing = GenSpawn.Spawn(wood, pawn.Position, surface);
                        hauledThing.SetForbidden(false);
                        var haulJob = JobMaker.MakeJob(JobDefOf.HaulToCell, hauledThing, haulDestination);
                        haulJob.count = 7;
                        haulJob.haulMode = HaulMode.ToCellNonStorage;
                        var tracker = Current.Game.GetComponent<ZLevelsManager>();
                        tracker.ResetJobTrackerFor(pawn);
                        tracker.BuildJobListFor(pawn, lower, haulJob);
                        Next(6);
                        Require(tracker.TryTakeFirstJob(pawn, true), "cross-level haul accepted");
                        break;
                    case 6:
                        if (!hauledThing.Spawned || hauledThing.Map != lower || hauledThing.Position != haulDestination) return;
                        Require(hauledThing.stackCount == 7, "hauling preserves quantity");
                        Record("cross-level hauling", "PASS");
                        var returnStair = lower.listerThings.AllThings.OfType<Building_StairsUp>().First();
                        Next(7);
                        returnStair.GiveJob(pawn, returnStair);
                        break;
                    case 7:
                        if (pawn.Map != surface) return;
                        powerCell = surface.Center + new IntVec3(12, 0, 0);
                        var transmitterDef = DefDatabase<ThingDef>.GetNamed("ZL_PowerTransmitterColumn");
                        surfaceTransmitter = SpawnBuilding(transmitterDef, surface, powerCell, ThingDefOf.Steel).GetComp<CompPowerZTransmitter>();
                        lowerTransmitter = SpawnBuilding(transmitterDef, lower, powerCell, ThingDefOf.Steel).GetComp<CompPowerZTransmitter>();
                        powerSource = SpawnBuilding(DefDatabase<ThingDef>.GetNamed("WoodFiredGenerator"), surface, powerCell + new IntVec3(5, 0, 0));
                        powerSource.GetComp<CompRefuelable>().Refuel(20f);
                        var lampBuilding = SpawnBuilding(DefDatabase<ThingDef>.GetNamed("StandingLamp"), lower, powerCell + new IntVec3(3, 0, 0));
                        lamp = lampBuilding.GetComp<CompPowerTrader>();
                        ConnectWithConduit(surface, powerCell, powerSource);
                        ConnectWithConduit(lower, powerCell, lampBuilding);
                        Next(8);
                        break;
                    case 8:
                        SamplePower();
                        if (!lamp.PowerOn) lastUnpoweredTick = Find.TickManager.TicksGame;
                        if (lamp.PowerOn && firstPoweredTick < 0) firstPoweredTick = Find.TickManager.TicksGame;
                        if (Find.TickManager.TicksGame - lastUnpoweredTick < 600) return;
                        Record("power bring-up", "lamp first powered " + (firstPoweredTick - stageTick) + " ticks after the transmitters were placed");
                        Record("power diagnostics", "surface net=" + surfaceTransmitter.PowerNet + " lower net=" + lowerTransmitter.PowerNet + " lamp on=" + lamp.PowerOn + " outputs=" + surfaceTransmitter.PowerOutput + "/" + lowerTransmitter.PowerOutput + " generator output=" + powerSource.GetComp<CompPowerPlant>().PowerOutput + " surface spawned=" + surfaceTransmitter.parent.Spawned);
                        Record("lower glow", "lamp cell " + lower.glowGrid.VisualGlowAt(lamp.parent.Position) + "; 3 cells away " + lower.glowGrid.VisualGlowAt(lamp.parent.Position + new IntVec3(3, 0, 0)) + "; 12 cells away " + lower.glowGrid.VisualGlowAt(lamp.parent.Position + new IntVec3(12, 0, 0)));
                        Require(surfaceTransmitter.PowerNet != null && lowerTransmitter.PowerNet != null, "power networks formed");
                        Require(lamp.PowerOn, "lower-floor lamp powered");
                        Require(lowerTransmitter.PowerOutput > 0f, "power supplied across to lower level");
                        Record("cross-level power transfer", "PASS");
                        if (GenCommandLine.CommandLineArgPassed("zlevelsscreenshots"))
                        {
                            // Fog hides unexplored underground; reveal the lamp's surroundings so the screenshot shows its light.
                            foreach (IntVec3 cell in CellRect.CenteredOn(lamp.parent.Position, 7))
                            {
                                if (cell.InBounds(lower)) lower.fogGrid.Unfog(cell);
                            }
                            CameraJumper.TryJump(lamp.parent.Position, lower, CameraJumper.MovementMode.Cut);
                            shotDue = DateTime.UtcNow.AddSeconds(3);
                            Next(10);
                            break;
                        }
                        powerSource.Destroy(DestroyMode.Vanish);
                        Next(9);
                        break;
                    case 10:
                        if (DateTime.UtcNow < shotDue) return;
                        Record("lower animals before screenshot", AnimalCensus(lower));
                        ScreenshotTaker.TakeNonSteamShot("zlevels-lower-level");
                        Record("lower level screenshot", "TAKEN");
                        CameraJumper.TryJump(powerCell, upper, CameraJumper.MovementMode.Cut);
                        shotDue = DateTime.UtcNow.AddSeconds(3);
                        Next(13);
                        break;
                    case 13:
                        if (DateTime.UtcNow < shotDue) return;
                        // The upper level is void except where it is built; the surface transmitter below should show through it.
                        Require(powerCell.GetTerrain(upper) == ZLevelsDefOf.ZL_OutsideTerrain, "upper cell over transmitter is void");
                        ScreenshotTaker.TakeNonSteamShot("zlevels-upper-void");
                        Record("upper void screenshot", "TAKEN");
                        CameraJumper.TryJump(surfaceTransmitter.parent.Position, surface, CameraJumper.MovementMode.Cut);
                        shotDue = DateTime.UtcNow.AddSeconds(3);
                        Next(11);
                        break;
                    case 11:
                        if (DateTime.UtcNow < shotDue) return;
                        ScreenshotTaker.TakeNonSteamShot("zlevels-surface");
                        Record("surface screenshot", "TAKEN");
                        var hole = surface.Center + new IntVec3(-6, 0, -6);
                        ClearCell(surface, hole);
                        ClearCell(lower, hole);
                        SpawnBuilding(DefDatabase<ThingDef>.GetNamed("Wall"), lower, hole, ThingDefOf.Steel);
                        surface.terrainGrid.SetTerrain(hole, ZLevelsDefOf.ZL_OutsideTerrain);
                        Require(hole.GetTerrain(surface) == ZLevelsDefOf.ZL_OutsideTerrain, "surface hole created");
                        Record("surface hole over lower wall", "PASS");
                        lower.fogGrid.Unfog(hole);
                        Require(!hole.Fogged(lower), "lower cell under hole revealed");
                        CameraJumper.TryJump(hole, surface, CameraJumper.MovementMode.Cut);
                        shotDue = DateTime.UtcNow.AddSeconds(3);
                        Next(12);
                        break;
                    case 12:
                        if (DateTime.UtcNow < shotDue) return;
                        ScreenshotTaker.TakeNonSteamShot("zlevels-surface-hole");
                        Record("surface hole screenshot", "TAKEN");
                        powerSource.Destroy(DestroyMode.Vanish);
                        Next(9);
                        break;
                    case 9:
                        if (Find.TickManager.TicksGame - stageTick < 600) return;
                        Require(!lamp.PowerOn, "lower-floor lamp unpowered after generator removal");
                        Record("cross-level power loss without source", "PASS");
                        var downToLower = surface.listerThings.AllThings.OfType<Building_StairsDown>().First();
                        Next(17);
                        downToLower.GiveJob(pawn, downToLower);
                        break;
                    case 17:
                        if (pawn.Map != lower) return;
                        Record("colonist takes the stairs down before the save", "PASS");
                        HoldWork(pawn);
                        Next(18);
                        break;
                    case 18:
                        // The save must hold a colonist who stays below; there is no climb back up first.
                        if (Find.TickManager.TicksGame - stageTick < 1800) return;
                        Require(pawn.Map == lower, "colonist stayed below before the save");
                        belowAtSave = true;
                        savedPosition = pawn.Position;
                        Record("colonist below before save", "PASS");
                        SaveAndReload();
                        break;
                    case 5:
                        if (!reloaded) return;
                        Require(surface != null && lower != null && upper != null && pawn != null, "saved references resolved");
                        Require(Current.Game.GetComponent<ZLevelsManager>().GetAllMaps(surface.Tile).Count == 3, "levels survive reload");
                        Require(belowAtSave && pawn.Map == lower, "colonist below survives reload");
                        Require(pawn.Position == savedPosition, "colonist keeps the saved cell");
                        Record("reload with colonist below", "PASS");
                        var climbAfterReload = lower.listerThings.AllThings.OfType<Building_StairsUp>().First();
                        Next(15);
                        climbAfterReload.GiveJob(pawn, climbAfterReload);
                        break;
                    case 15:
                        if (pawn.Map != surface) return;
                        Require(Current.Game.GetComponent<ZLevelsManager>().GetAllMaps(surface.Tile).Count == 3, "levels tracked after reload stairs");
                        Record("reload stairs up", "PASS");
                        StartUnforcedHaul();
                        Next(20);
                        break;
                    case 20:
                        // Nothing assigns this job. The colonist has to find the wood on the lower level by itself.
                        // Needs stay full for the whole wait. At superfast speed rest falls inside the window (run 20261030-191130 lay down at the lower stairs).
                        RestNeeds(pawn);
                        if (pawn.Map == lower) wentDownForWood = true;
                        if (!UnforcedWoodInZone()) return;
                        Require(wentDownForWood, "colonist went down to the wood");
                        Record("unforced cross-level haul", "PASS");
                        var reloadedDown = surface.listerThings.AllThings.OfType<Building_StairsDown>().First();
                        Next(14);
                        reloadedDown.GiveJob(pawn, reloadedDown);
                        break;
                    case 14:
                        if (pawn.Map == surface) return;
                        Require(Current.Game.GetComponent<ZLevelsManager>().GetZIndexFor(pawn.Map) == -1, "stairs after reload lead to lower level");
                        Record("reload stairs down", "PASS");
                        finished = true;
                        Record("core validation", "PASS");
                        Root.Shutdown();
                        break;
                }
            }
            catch (Exception exception)
            {
                finished = true;
                Record("stage " + stage, "FAIL: " + exception);
                Log.Error("Z-Levels runtime validation failed: " + exception);
                Root.Shutdown();
            }
        }

        // Infestations must never start five levels down or deeper. This builds levels -1 to -5 and forces caves below on -4
        // and -5, so the depth rule is the only thing that can stop an infestation on -5. Depth -4 is the control that must get one.
        private void RunInfestationGuard()
        {
            try
            {
                deadline = DateTime.UtcNow.AddMinutes(10);
                var tracker = Current.Game.GetComponent<ZLevelsManager>();
                var infestation = DefDatabase<IncidentDef>.GetNamed("Infestation");
                var holeDef = DefDatabase<ThingDef>.GetNamed("ZL_NaturalHole");
                Map origin = Find.CurrentMap;
                for (int depth = 1; depth <= 4; depth++)
                {
                    origin = CreateLevelBelow(tracker, origin);
                }
                Map four = origin;
                Require(tracker.GetZIndexFor(four) == -4, "depth 4 index");
                var fourComp = four.GetComponent<MapComponentZLevel>();
                fourComp.hasCavesBelow = true;
                SpawnFood(four);
                infestation.Worker.TryExecute(new IncidentParms { target = four, points = 1000f });
                int holesFour = four.listerThings.ThingsOfDef(holeDef).Count;
                Require(fourComp.ActiveInfestations != null && fourComp.ActiveInfestations.Count == 1 && holesFour == 1,
                    "depth 4 infestation expected: holes " + holesFour);
                Record("infestation allowed at depth 4", "PASS: hole " + holesFour);

                // Built after the depth 4 record, so it inherits the infestation list the way a real new level does.
                Map five = CreateLevelBelow(tracker, four);
                Require(tracker.GetZIndexFor(five) == -5, "depth 5 index");
                var fiveComp = five.GetComponent<MapComponentZLevel>();
                fiveComp.hasCavesBelow = true;
                SpawnFood(five);
                int recordsBefore = fiveComp.TotalInfestations == null ? 0 : fiveComp.TotalInfestations.Count;
                int activeBefore = fiveComp.ActiveInfestations == null ? 0 : fiveComp.ActiveInfestations.Count;
                infestation.Worker.TryExecute(new IncidentParms { target = five, points = 1000f });
                int recordsAfter = fiveComp.TotalInfestations == null ? 0 : fiveComp.TotalInfestations.Count;
                int activeAfter = fiveComp.ActiveInfestations == null ? 0 : fiveComp.ActiveInfestations.Count;
                int holesFive = five.listerThings.ThingsOfDef(holeDef).Count;
                string outcome = "records " + recordsBefore + " -> " + recordsAfter + ", active " + activeBefore + " -> " + activeAfter + ", holes " + holesFive;
                Require(recordsAfter == recordsBefore && activeAfter == activeBefore && holesFive == 0, "depth 5 infestation not blocked: " + outcome);
                Record("infestation blocked at depth 5", "PASS: " + outcome);
                Record("infestation guard", "PASS");
                finished = true;
                Root.Shutdown();
            }
            catch (Exception exception)
            {
                finished = true;
                Record("infestation guard", "FAIL: " + exception);
                Log.Error("Z-Levels infestation guard validation failed: " + exception);
                Root.Shutdown();
            }
        }

        private static Map CreateLevelBelow(ZLevelsManager tracker, Map origin)
        {
            // Caves below decide whether the new level has caves. Without them the level is solid rock: run 20261029-110937
            // generated 24 walkable cells per level. The start cell is carved so the start spot is always valid.
            origin.GetComponent<MapComponentZLevel>().hasCavesBelow = true;
            IntVec3 spot = origin.Center;
            ClearCell(origin, spot);
            origin.terrainGrid.SetTerrain(spot, TerrainDefOf.Concrete);
            Map level = tracker.CreateLowerLevel(origin, spot);
            Record("generated level " + tracker.GetZIndexFor(level), "walkable cells " + CountWalkable(level) + ", start spot " + spot);
            return level;
        }

        private static int CountWalkable(Map map)
        {
            int count = 0;
            foreach (IntVec3 cell in map.AllCells)
            {
                if (cell.Walkable(map)) count++;
            }
            return count;
        }

        private static void SpawnFood(Map map)
        {
            IntVec3 spot = map.Center;
            ClearCell(map, spot);
            map.terrainGrid.SetTerrain(spot, TerrainDefOf.Concrete);
            GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("MealSimple")), spot, map);
        }

        private T SpawnStair<T>(ThingDef definition, IntVec3 cell) where T : Building_Stairs
        {
            ClearCell(surface, cell);
            surface.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete);
            var stair = (T)ThingMaker.MakeThing(definition, ThingDefOf.Steel);
            stair.SetFaction(Faction.OfPlayer);
            return (T)GenSpawn.Spawn(stair, cell, surface);
        }

        private static ThingWithComps SpawnBuilding(ThingDef definition, Map map, IntVec3 cell, ThingDef stuff = null)
        {
            foreach (var occupied in GenAdj.OccupiedRect(cell, Rot4.North, definition.Size))
            {
                ClearCell(map, occupied);
                map.terrainGrid.SetTerrain(occupied, TerrainDefOf.Concrete);
                // Keep the roof: on a lower level it is the ceiling the surface floor created. Removing it opens a
                // hole that drops whatever stands above (Patch_SetRoof), which would move the surface transmitter.
            }
            var building = (ThingWithComps)ThingMaker.MakeThing(definition, stuff);
            building.SetFaction(Faction.OfPlayer);
            return (ThingWithComps)GenSpawn.Spawn(building, cell, map);
        }

        private static void ConnectWithConduit(Map map, IntVec3 from, Thing target)
        {
            var footprint = target.OccupiedRect();
            for (int x = from.x + 1; x < footprint.minX; x++)
            {
                var cell = new IntVec3(x, 0, from.z);
                ClearCell(map, cell);
                GenSpawn.Spawn(ThingDefOf.PowerConduit, cell, map);
            }
        }

        private static void ClearCell(Map map, IntVec3 cell)
        {
            foreach (var thing in cell.GetThingList(map).ToList())
                if (!(thing is Pawn)) thing.Destroy(DestroyMode.Vanish);
        }

        // Test only: a colonist with no work stays below during the save wait. With the lower room open, a surface job can take it
        // up the stairs (run 20261030-202009 failed the save-below check that way).
        private static void HoldWork(Pawn colonist)
        {
            foreach (WorkTypeDef work in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                if (colonist.WorkTypeIsDisabled(work)) continue;
                colonist.workSettings.SetPriority(work, 0);
            }
        }

        private static void RestoreWork(Pawn colonist)
        {
            foreach (WorkTypeDef work in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                if (colonist.WorkTypeIsDisabled(work)) continue;
                colonist.workSettings.SetPriority(work, work.naturalPriority);
            }
        }

        // A colonist who lies down to rest on the way stalls the stairs stages: run 144826 timed out at stage 1.
        private static void RestNeeds(Pawn colonist)
        {
            colonist.needs.rest.CurLevel = colonist.needs.rest.MaxLevel;
            colonist.needs.food.CurLevel = colonist.needs.food.MaxLevel;
            if (colonist.needs.joy != null) colonist.needs.joy.CurLevel = colonist.needs.joy.MaxLevel;
        }

        private void StartUnforcedHaul()
        {
            RestNeeds(pawn);
            RestoreWork(pawn);
            Current.Game.GetComponent<ZLevelsManager>().ResetJobTrackerFor(pawn);
            var woodCell = lower.listerThings.AllThings.OfType<Building_StairsUp>().First().Position + new IntVec3(1, 0, 0);
            ClearCell(lower, woodCell);
            lower.terrainGrid.SetTerrain(woodCell, TerrainDefOf.Concrete);
            var logs = ThingMaker.MakeThing(ThingDefOf.WoodLog);
            logs.stackCount = 7;
            unforcedWood = GenSpawn.Spawn(logs, woodCell, lower);
            unforcedWood.SetForbidden(false);
            // Only the harness's logs may be hauled, and only the validation colonist may haul. Run 20261030-193538 filled the zone with surface wood in one second.
            foreach (var map in new[] { surface, lower })
            {
                foreach (var wood in map.listerThings.ThingsOfDef(ThingDefOf.WoodLog).ToList())
                {
                    if (wood != unforcedWood) wood.SetForbidden(true);
                }
            }
            foreach (var map in new[] { surface, lower, upper })
            {
                if (map == null) continue;
                foreach (var colonist in map.mapPawns.FreeColonistsSpawned)
                {
                    if (colonist != pawn) colonist.workSettings.SetPriority(WorkTypeDefOf.Hauling, 0);
                }
            }
            var originalCell = pawn.Position + new IntVec3(0, 0, -3);
            // A pawn-based CanReach runs the ZL prefix, which needs a job search context and threw in run 20261030-193352. PassDoors without a pawn gives the vanilla answer.
            Log.Message("validation zone candidate " + originalCell + " reachable from " + pawn.Position + ": " + surface.reachability.CanReach(pawn.Position, originalCell, PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassDoors)));
            var zoneCell = FindReachableZoneCell(pawn.Position);
            ClearCell(surface, zoneCell);
            surface.terrainGrid.SetTerrain(zoneCell, TerrainDefOf.Concrete);
            unforcedZone = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, surface.zoneManager);
            surface.zoneManager.RegisterZone(unforcedZone);
            unforcedZone.AddCell(zoneCell);
        }

        // A cell cut out of rock can be walled off, and then every haul to it fails with ErroredPather (run 20261030-192636).
        private IntVec3 FindReachableZoneCell(IntVec3 near)
        {
            foreach (var cell in GenRadial.RadialCellsAround(near, 4f, false))
            {
                if (!cell.InBounds(surface) || cell.GetEdifice(surface) != null) continue;
                if (surface.reachability.CanReach(near, cell, PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassDoors))) return cell;
            }
            throw new InvalidOperationException("No reachable zone cell within 4 cells of " + near);
        }

        private string DescribeWood()
        {
            if (unforcedWood == null) return "wood none";
            string colonist = pawn == null ? "" : ", forbidden for colonist " + unforcedWood.IsForbidden(pawn)
                + ", colonist can reserve wood " + pawn.CanReserve(unforcedWood) + ", hauling priority " + pawn.workSettings?.GetPriority(WorkTypeDefOf.Hauling);
            return "wood " + unforcedWood + " x" + unforcedWood.stackCount + " destroyed " + unforcedWood.Destroyed
                + " at " + unforcedWood.PositionHeld + " on " + unforcedWood.MapHeld
                + " in zone " + UnforcedWoodInZone() + colonist;
        }

        // Only the harness's seven logs count. Other wood can reach the zone by itself.
        private bool UnforcedWoodInZone()
        {
            return unforcedWood != null && unforcedWood.Spawned && unforcedWood.MapHeld == surface
                && unforcedZone.ContainsCell(unforcedWood.Position) && unforcedWood.stackCount == 7;
        }

        private void SaveAndReload()
        {
            Next(5);
            GameDataSaveLoader.SaveGame("ZLevelsValidation");
            Record("save three-level colony", "PASS");
            LongEventHandler.ExecuteWhenFinished(() => GameDataSaveLoader.LoadGame("ZLevelsValidation"));
        }

        private void Next(int nextStage)
        {
            stage = nextStage;
            stageTick = Find.TickManager.TicksGame;
            deadline = DateTime.UtcNow.AddMinutes(4);
        }

        private static void Require(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
        }

        private static string WaterCensus(Map map)
        {
            int water = 0;
            int river = 0;
            foreach (IntVec3 cell in map.AllCells)
            {
                TerrainDef terrain = cell.GetTerrain(map);
                if (terrain.IsWater) water++;
                if (terrain.IsRiver) river++;
            }
            return "water cells " + water + ", river cells " + river;
        }

        private static string AnimalCensus(Map map)
        {
            var mapParent = map.Parent as MapParent_ZLevel;
            Building_StairsUp stairs = map.listerThings.AllThings.OfType<Building_StairsUp>().First();
            int animals = 0;
            int inRock = 0;
            int reachable = 0;
            foreach (Pawn animal in map.mapPawns.AllPawnsSpawned)
            {
                if (!animal.RaceProps.Animal || animal.Faction != null) continue;
                animals++;
                if (!animal.Position.Walkable(map)) inRock++;
                else if (map.reachability.CanReach(animal.Position, stairs, PathEndMode.Touch, TraverseParms.For(TraverseMode.PassDoors))) reachable++;
            }
            return "caves " + (mapParent != null && mapParent.hasCaves) + ", wild animals " + animals + ", on unwalkable cells " + inRock + ", walkable and reachable from the lower stairs " + reachable + ", walkable but cut off " + (animals - inRock - reachable);
        }

        private static string StairsSurroundings(Map map)
        {
            Building_StairsUp stairs = map.listerThings.AllThings.OfType<Building_StairsUp>().First();
            var parts = new System.Collections.Generic.List<string>();
            foreach (IntVec3 cell in new[] { stairs.Position }.Concat(GenAdj.CellsAdjacent8Way(stairs)))
            {
                string things = string.Join("+", cell.GetThingList(map).Select(t => t.def.defName).ToArray());
                RoofDef roof = map.roofGrid.RoofAt(cell);
                parts.Add(cell.ToString() + " " + cell.GetTerrain(map).defName + " roof " + (roof == null ? "none" : roof.defName)
                    + " things " + (things.Length == 0 ? "none" : things) + (cell.Walkable(map) ? " walkable" : " blocked"));
            }
            return string.Join(" | ", parts.ToArray());
        }

        private static string StairsNeighbourhood(Map map, IntVec3 arrival)
        {
            Building_StairsUp stairs = map.listerThings.AllThings.OfType<Building_StairsUp>().First();
            Region stairsRegion = stairs.Position.GetRegion(map);
            int walkableNeighbours = 0;
            int inStairsRegion = 0;
            foreach (IntVec3 cell in GenAdj.CellsAdjacent8Way(stairs))
            {
                if (!cell.Walkable(map)) continue;
                walkableNeighbours++;
                if (stairsRegion != null && cell.GetRegion(map) == stairsRegion) inStairsRegion++;
            }
            Region arrivalRegion = arrival.GetRegion(map);
            return "stairs cell walkable " + stairs.Position.Walkable(map) + ", stairs region cells " + (stairsRegion == null ? "none" : stairsRegion.CellCount.ToString())
                + "; adjacent walkable " + walkableNeighbours + " of 8, in stairs region " + inStairsRegion
                + "; arrival cell walkable " + arrival.Walkable(map) + ", arrival region cells " + (arrivalRegion == null ? "none" : arrivalRegion.CellCount.ToString());
        }

        // Test only: the mod keeps the lower stairs sealed, as designed. The harness cuts natural rock out of a room around the
        // stairs so that a colonist can walk, mine and build there. The stairs and the roofs stay.
        private static void OpenLowerRoom(Map map)
        {
            Building_StairsUp stairs = map.listerThings.AllThings.OfType<Building_StairsUp>().First();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(stairs.Position, 3f, true))
            {
                if (!cell.InBounds(map) || cell == stairs.Position) continue;
                foreach (Thing thing in cell.GetThingList(map).ToList())
                {
                    if (thing.def.mineable) thing.Destroy(DestroyMode.Vanish);
                }
            }
        }

        private static int CountReachableFromStairs(Map map, out int walkable)
        {
            Building_StairsUp stairs = map.listerThings.AllThings.OfType<Building_StairsUp>().First();
            walkable = 0;
            int reachable = 0;
            foreach (IntVec3 cell in map.AllCells)
            {
                if (!cell.Walkable(map)) continue;
                walkable++;
                if (map.reachability.CanReach(cell, stairs, PathEndMode.Touch, TraverseParms.For(TraverseMode.PassDoors))) reachable++;
            }
            return reachable;
        }

        // The underground animal fallback ends on a walkable cell chosen from the whole map (AnimalPatches), so the
        // share of walkable cells the lower stairs can reach is what decides whether those animals can reach the colony.
        private static string WalkableReachability(Map map)
        {
            int reachable = CountReachableFromStairs(map, out int walkable);
            return "walkable cells " + walkable + ", reachable from the lower stairs " + reachable;
        }

        private static string MutatorNames(Map map)
        {
            var names = map.TileInfo.Mutators.Select(m => m.defName).ToList();
            return names.Count == 0 ? "none" : string.Join(",", names);
        }

        private void SamplePower()
        {
            var line = Find.TickManager.TicksGame
                + " | surface " + surfaceTransmitter.PowerOn + " " + surfaceTransmitter.PowerOutput + " " + (surfaceTransmitter.PowerNet == null ? "null" : surfaceTransmitter.PowerNet.CurrentEnergyGainRate().ToString("F1"))
                + " | lower " + lowerTransmitter.PowerOn + " " + lowerTransmitter.PowerOutput + " " + (lowerTransmitter.PowerNet == null ? "null" : lowerTransmitter.PowerNet.CurrentEnergyGainRate().ToString("F1"))
                + " | lamp " + lamp.PowerOn;
            File.AppendAllText(Path.Combine(GenFilePaths.SaveDataFolderPath, "power-samples.txt"), line + Environment.NewLine);
        }

        private static void Record(string scenario, string result)
        {
            var line = DateTime.UtcNow.ToString("O") + " | " + scenario + " | " + result;
            File.AppendAllText(Path.Combine(GenFilePaths.SaveDataFolderPath, "validation-results.txt"), line + Environment.NewLine);
            Log.Message("[ZLevels validation] " + line);
        }
    }
}
