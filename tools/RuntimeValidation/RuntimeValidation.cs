using System;
using System.Collections.Generic;
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
        private Building minedWall;

        // State for the vault enter-and-build check. It is not saved.
        private int vaultStage;
        private Pawn vaultColonist;
        private MapPortal vaultEntrance;
        private Map vaultPocket;
        private IntVec3 vaultWallCell;
        private ThingDef vaultWallDef;
        private DateTime vaultDeadline;
        private bool wentDownForWall;
        private IntVec3 campfireCell = IntVec3.Invalid;
        private IntVec3 campfireLogCell = IntVec3.Invalid;
        private bool constructionFailed;
        private Zone_Stockpile vaultZone;
        private Thing loadLogs;
        private int surfaceLogsBefore;
        private int vaultSaveStage;
        private IntVec3 vaultSavedWallCell = IntVec3.Invalid;
        private int savedLogs;
        // State for the cross-level supply tests. It is not saved.
        private int supplyStage;
        private DateTime supplyDeadline;
        private string supplyLabel;
        private int supplyNextStage;
        private int supplyFailures;
        private HashSet<int> supplyBaseline;
        private bool supplyFailedOnce;
        private IntVec3 supplyCell;
        private int supplyBefore;
        private int supplyOther;
        private CompRefuelable supplyFuel;
        private float logsValue;
        private float wealthTotalBefore;
        private float wealthStorytellerBefore;

        // The colonist's level, job and queue from the mining stage on, for a stall's timeout message. It is not saved.
        private readonly List<string> campfireTrail = new List<string>();
        private readonly List<string> campfireTrailLatest = new List<string>();
        private string lastTrailEntry = "";

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
            Scribe_Values.Look(ref vaultSaveStage, "vaultSaveStage");
            Scribe_Values.Look<IntVec3>(ref vaultSavedWallCell, "vaultSavedWallCell", IntVec3.Invalid);
            Scribe_Values.Look(ref savedLogs, "vaultSavedLogs");
        }

        public override void GameComponentUpdate()
        {
            if (!GenCommandLine.CommandLineArgPassed("zlevelsvalidation") || finished)
                return;
            try
            {
                if (DateTime.UtcNow > deadline)
                    throw new InvalidOperationException("Timed out at stage " + stage + ", pawn job=" + pawn?.CurJob + ", pawn at " + pawn?.Position + " on " + pawn?.Map + ", " + DescribeWood() + ", " + DescribeCampfire() + ", " + DescribeLookUpMaps(pawn) + ", trail: " + DescribeCampfireTrail() + ", rebuild: " + DescribeRebuild());
                if (Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting)
                    return;
                // Debug mode pauses the game on every logged error. Keep it running, so the debug log covers a stall.
                if (ZLogger.DebugEnabled && Find.TickManager.CurTimeSpeed == TimeSpeed.Paused)
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
                if (!ZLogger.DebugEnabled && GenCommandLine.CommandLineArgPassed("zlevelsdebug"))
                {
                    // ZLevelsMod and ZLevelsSettings are internal to the port, so the flag is set by reflection.
                    var settings = typeof(ZLogger).Assembly.GetType("ZLevels.ZLevelsMod", throwOnError: true)
                        .GetField("settings", BindingFlags.Public | BindingFlags.Static).GetValue(null);
                    settings.GetType().GetField("DebugEnabled", BindingFlags.Public | BindingFlags.Instance).SetValue(settings, true);
                }
                if (GenCommandLine.CommandLineArgPassed("zlevelsvaultplacement"))
                {
                    finished = true;
                    RunVaultPlacementCheck();
                    return;
                }
                if (GenCommandLine.CommandLineArgPassed("zlevelsvaultpocket"))
                {
                    finished = true;
                    RunVaultPocketCheck();
                    return;
                }
                if (GenCommandLine.CommandLineArgPassed("zlevelsvaultenter"))
                {
                    RunVaultEnterTick();
                    return;
                }
                if (GenCommandLine.CommandLineArgPassed("zlevelsvaultload"))
                {
                    RunVaultLoadTick();
                    return;
                }
                if (GenCommandLine.CommandLineArgPassed("zlevelsvaultsave"))
                {
                    RunVaultSaveTick();
                    return;
                }
                if (GenCommandLine.CommandLineArgPassed("zlevelsvaultinfestation"))
                {
                    RunVaultInfestationGuard();
                    return;
                }
                if (GenCommandLine.CommandLineArgPassed("zlevelssupply"))
                {
                    RunSupplyTick();
                    return;
                }
                if (GenCommandLine.CommandLineArgPassed("zlevelsstalelookup"))
                {
                    finished = true;
                    RunStaleLookupCheck();
                    return;
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
                        pawn = FindValidationColonist(surface);
                        RestNeeds(pawn);
                        Require(Current.Game.GetComponent<ZLevelsManager>().GetZIndexFor(surface) == 0, "surface registered");
                        Record("fresh colony", "PASS");
                        Record("validation colonist can haul, mine and build", "PASS: " + pawn.LabelShort);
                        var down = SpawnStair<Building_StairsDown>(ZLevelsDefOf.ZL_StairsDown, pawn.Position + new IntVec3(3, 0, 0));
                        Next(1);
                        down.GiveJob(pawn, down);
                        break;
                    case 1:
                        // The walk to the stairs runs at superfast speed. Rest can fall on the way and lay the colonist down (run 20261031-134036).
                        RestNeeds(pawn);
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
                        Record("look-up maps after reload", DescribeLookUpMaps(pawn));
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
                        DesignateMining(lower);
                        deadline = DateTime.UtcNow.AddMinutes(4);
                        Next(21);
                        break;
                    case 21:
                        // Nothing assigns this job. The colonist has to go down and mine the wall by itself.
                        RestNeeds(pawn);
                        RecordCampfireTrail();
                        if (pawn.Map == lower) wentDownForWall = true;
                        if (minedWall == null || !minedWall.Destroyed) return;
                        Require(wentDownForWall, "colonist went down to the wall");
                        Record("mines below by itself", "PASS");
                        ReleaseForbiddenWood();
                        PlaceCampfire(lower);
                        deadline = DateTime.UtcNow.AddMinutes(4);
                        Next(22);
                        break;
                    case 22:
                        // Nothing assigns this job. The colonist has to haul the logs to the blueprint and build it by itself.
                        RestNeeds(pawn);
                        RecordCampfireTrail();
                        // Vanilla construction can fail at random, and the frame goes back to a blueprint with its logs spilled. Fail the
                        // first frame once, on purpose, so the rebuild below is always tested (run 20261031-141006 stalled after such a failure).
                        if (!constructionFailed && campfireCell.GetEdifice(lower) is Frame failingFrame)
                        {
                            constructionFailed = true;
                            failingFrame.FailConstruction(pawn);
                            Record("construction failed once on purpose", "PASS");
                            Record("rebuild right after the failure", DescribeRebuild());
                            return;
                        }
                        Building campfire = campfireCell.GetEdifice(lower);
                        if (campfire == null || campfire.def != DefDatabase<ThingDef>.GetNamed("Campfire")) return;
                        Record("builds below by itself", "PASS");
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

        // Vault entrances go on the surface only. A plain wall shows the cell is buildable, so the vault rule is what refuses the entrance.
        private void RunVaultPlacementCheck()
        {
            try
            {
                deadline = DateTime.UtcNow.AddMinutes(10);
                var tracker = Current.Game.GetComponent<ZLevelsManager>();
                var entrance = DefDatabase<ThingDef>.GetNamed("ZL_VaultEntrance");
                var wall = DefDatabase<ThingDef>.GetNamed("Wall");
                Map surface = Find.CurrentMap;
                IntVec3 surfaceCell = surface.Center + new IntVec3(6, 0, 6);
                ClearCell(surface, surfaceCell);
                surface.terrainGrid.SetTerrain(surfaceCell, TerrainDefOf.Concrete);
                Require(GenConstruct.CanPlaceBlueprintAt(wall,surfaceCell, Rot4.North, surface).Accepted, "a plain wall is accepted on the surface cell");
                Require(GenConstruct.CanPlaceBlueprintAt(entrance, surfaceCell, Rot4.North, surface).Accepted, "the surface takes a vault entrance");
                Record("vault entrance on the surface", "PASS");
                Map lower = CreateLevelBelow(tracker, surface);
                foreach (IntVec3 cell in lower.AllCells)
                {
                    lower.fogGrid.Unfog(cell);
                }
                Record("lower cells that take a wall", lower.AllCells.Count(c => GenConstruct.CanPlaceBlueprintAt(wall, c, Rot4.North, lower).Accepted).ToString());
                Record("lower cells that take stairs", lower.AllCells.Count(c => GenConstruct.CanPlaceBlueprintAt(DefDatabase<ThingDef>.GetNamed("ZL_StairsUp"), c, Rot4.North, lower).Accepted).ToString());
                // Unexplored lower cells may refuse building, so unfog them first. Then the first cell that takes a plain wall is the control.
                IntVec3 lowerCell = IntVec3.Invalid;
                foreach (IntVec3 cell in lower.AllCells)
                {
                    if (GenConstruct.CanPlaceBlueprintAt(wall,cell, Rot4.North, lower).Accepted)
                    {
                        lowerCell = cell;
                        break;
                    }
                }
                Require(lowerCell.IsValid, "a lower cell takes a plain wall");
                AcceptanceReport lowerReport = GenConstruct.CanPlaceBlueprintAt(entrance, lowerCell, Rot4.North, lower);
                Require(!lowerReport.Accepted, "a lower level refuses a vault entrance");
                Record("vault entrance on a lower level", "refused: " + lowerReport.Reason);
                finished = true;
                Record("vault placement", "PASS");
                Root.Shutdown();
            }
            catch (Exception exception)
            {
                finished = true;
                Record("vault placement", "FAIL: " + exception);
                Log.Error("Z-Levels vault placement validation failed: " + exception);
                Root.Shutdown();
            }
        }

        // The enter-and-build check runs once per tick. A colonist walks into the pocket, a wall is built inside, and the colonist walks back out.
        private void RunVaultEnterTick()
        {
            try
            {
                if (vaultStage == 0)
                {
                    deadline = DateTime.MaxValue;
                    vaultDeadline = DateTime.UtcNow.AddMinutes(4);
                    Map surface = Find.CurrentMap;
                    vaultColonist = surface.mapPawns.FreeColonistsSpawned.First();
                    RestoreWork(vaultColonist);
                    IntVec3 entranceCell = FindVaultEntranceCell(surface, vaultColonist);
                    ClearCell(surface, entranceCell);
                    surface.terrainGrid.SetTerrain(entranceCell, TerrainDefOf.Concrete);
                    foreach (IntVec3 around in GenRadial.RadialCellsAround(entranceCell, 1.5f, useCenter: true))
                    {
                        ClearCell(surface, around);
                    }
                    vaultEntrance = (MapPortal)GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("ZL_VaultEntrance")), entranceCell, surface);
                    Record("colonist can reach the entrance", vaultColonist.CanReach(vaultEntrance, PathEndMode.Touch, Danger.Deadly).ToString());
                    vaultPocket = vaultEntrance.GetOtherMap();
                    vaultWallDef = DefDatabase<ThingDef>.GetNamed("Wall");
                    vaultWallCell = FindVaultWallCell(vaultPocket, vaultEntrance.exit.Position);
                    Thing wood = ThingMaker.MakeThing(ThingDefOf.WoodLog);
                    wood.stackCount = 10;
                    GenSpawn.Spawn(wood, vaultWallCell + new IntVec3(2, 0, 0), vaultPocket);
                    vaultColonist.jobs.StartJob(JobMaker.MakeJob(JobDefOf.EnterPortal, vaultEntrance), JobCondition.InterruptForced);
                    Record("colonist sent to the vault entrance", "PASS");
                    vaultStage = 1;
                    return;
                }
                if (vaultStage > 0 && DateTime.UtcNow > vaultDeadline)
                {
                    throw new InvalidOperationException("Timed out at vault stage " + vaultStage + ", " + DescribeVaultEnter());
                }
                if (vaultStage == 1)
                {
                    if (vaultColonist.Map != vaultPocket)
                    {
                        return;
                    }
                    AcceptanceReport wallReport = GenConstruct.CanPlaceBlueprintAt(vaultWallDef, vaultWallCell, Rot4.North, vaultPocket);
                    Record("wall accepted at the vault cell", wallReport.Accepted ? "yes" : "no: " + wallReport.Reason);
                    GenConstruct.PlaceBlueprintForBuild(vaultWallDef, vaultWallCell, vaultPocket, Rot4.North, Faction.OfPlayer, ThingDefOf.WoodLog);
                    Record("colonist enters the pocket", "PASS");
                    Record("colonist can reach the wall cell", vaultColonist.CanReach(vaultWallCell, PathEndMode.Touch, Danger.Deadly).ToString());
                    vaultStage = 2;
                    return;
                }
                if (vaultStage == 2)
                {
                    Building wall = vaultWallCell.GetEdifice(vaultPocket);
                    if (wall == null || wall.def != vaultWallDef)
                    {
                        return;
                    }
                    Record("wall built inside the pocket", "PASS");
                    vaultColonist.jobs.StartJob(JobMaker.MakeJob(JobDefOf.EnterPortal, vaultEntrance.exit), JobCondition.InterruptForced);
                    vaultStage = 3;
                    return;
                }
                if (vaultStage == 3)
                {
                    if (vaultColonist.Map != vaultEntrance.Map)
                    {
                        return;
                    }
                    Require(vaultWallCell.GetEdifice(vaultPocket) != null, "the wall stands after the colonist leaves");
                    Record("colonist leaves the pocket", "PASS");
                    finished = true;
                    Record("vault build inside", "PASS");
                    Root.Shutdown();
                }
            }
            catch (Exception exception)
            {
                finished = true;
                Record("vault build inside", "FAIL: " + exception);
                Log.Error("Z-Levels vault build inside validation failed: " + exception);
                Root.Shutdown();
            }
        }

        // The first cell at least six away from the colonist that the colonist can reach. The vault entrance goes there, so the colonist can walk to it.
        // Acceptance d. Seven logs go on the entrance's load list, and colonists haul them into the pocket with no forced job. The test
        // places the pocket's storage zone beside the exit, as a player would. The colonist's needs are reset each tick, as in the stairs stages.
        private void RunVaultLoadTick()
        {
            try
            {
                if (vaultStage > 0 && DateTime.UtcNow > vaultDeadline)
                {
                    throw new InvalidOperationException("Timed out at load stage " + vaultStage + ", " + DescribeLoad());
                }
                if (vaultStage == 0)
                {
                    deadline = DateTime.MaxValue;
                    vaultDeadline = DateTime.UtcNow.AddMinutes(4);
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
                    surface = Find.CurrentMap;
                    vaultColonist = FindValidationColonist(surface);
                    RestoreWork(vaultColonist);
                    foreach (Pawn other in surface.mapPawns.FreeColonistsSpawned)
                    {
                        if (other != vaultColonist && !other.WorkTypeIsDisabled(WorkTypeDefOf.Hauling)) other.workSettings.SetPriority(WorkTypeDefOf.Hauling, 0);
                    }
                    IntVec3 entranceCell = FindVaultEntranceCell(surface, vaultColonist);
                    ClearCell(surface, entranceCell);
                    surface.terrainGrid.SetTerrain(entranceCell, TerrainDefOf.Concrete);
                    foreach (IntVec3 around in GenRadial.RadialCellsAround(entranceCell, 1.5f, useCenter: true))
                    {
                        ClearCell(surface, around);
                    }
                    vaultEntrance = (MapPortal)GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("ZL_VaultEntrance")), entranceCell, surface);
                    vaultPocket = vaultEntrance.GetOtherMap();
                    vaultZone = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, vaultPocket.zoneManager);
                    vaultPocket.zoneManager.RegisterZone(vaultZone);
                    foreach (IntVec3 cell in FindPocketZoneCells(vaultPocket, vaultEntrance.GetDestinationLocation()))
                    {
                        vaultZone.AddCell(cell);
                    }
                    surfaceLogsBefore = SurfaceLogCount();
                    loadLogs = ThingMaker.MakeThing(ThingDefOf.WoodLog);
                    loadLogs.stackCount = 7;
                    GenSpawn.Spawn(loadLogs, FindReachableZoneCell(vaultColonist.Position), surface);
                    // Acceptance e. The wealth is read through the mod's getters (WealthTotal) and through the storyteller's wealth.
                    logsValue = loadLogs.MarketValue * loadLogs.stackCount;
                    surface.wealthWatcher.ForceRecount();
                    wealthTotalBefore = surface.wealthWatcher.WealthTotal;
                    wealthStorytellerBefore = surface.PlayerWealthForStoryteller;
                    Record("surface wealth before", "total " + wealthTotalBefore.ToString("F1") + ", storyteller " + wealthStorytellerBefore.ToString("F1") + ", the seven logs " + logsValue.ToString("F1")
                        + ", surface items " + RawWealthItems(surface).ToString("F1"));
                    var transferable = new TransferableOneWay();
                    transferable.things.Add(loadLogs);
                    vaultEntrance.AddToTheToLoadList(transferable, 7);
                    Record("vault load before", DescribeLoad());
                    vaultStage = 1;
                    return;
                }
                RestNeeds(vaultColonist);
                if (vaultStage == 1)
                {
                    if (PocketLogCount() < 7) return;
                    vaultStage = 2;
                    return;
                }
                if (vaultStage == 2)
                {
                    Require(vaultEntrance.leftToLoad == null || !vaultEntrance.leftToLoad.Any(), "the load list is empty");
                    // The colony starts with its own wood on the surface, so the test compares against the count before the seven were placed.
                    Require(SurfaceLogCount() <= surfaceLogsBefore, "the seven logs left the surface");
                    Require(PocketLogCount() == 7, "all seven logs are in the pocket");
                    foreach (Thing log in vaultPocket.listerThings.ThingsOfDef(ThingDefOf.WoodLog))
                    {
                        Require(vaultZone.ContainsCell(log.Position) || log.Position.DistanceTo(vaultEntrance.GetDestinationLocation()) <= 4f,
                            "log at " + log.Position + " is not beside the exit");
                    }
                    surface.wealthWatcher.ForceRecount();
                    float wealthTotalAfter = surface.wealthWatcher.WealthTotal;
                    float wealthStorytellerAfter = surface.PlayerWealthForStoryteller;
                    vaultPocket.wealthWatcher.ForceRecount();
                    Record("surface wealth after", "total " + wealthTotalAfter.ToString("F1") + ", storyteller " + wealthStorytellerAfter.ToString("F1")
                        + ", surface items " + RawWealthItems(surface).ToString("F1") + ", pocket items " + RawWealthItems(vaultPocket).ToString("F1")
                        + ", pocket tile " + vaultPocket.Tile + ", surface tile " + surface.Tile + ", maps in the surface total "
                        + Current.Game.GetComponent<ZLevelsManager>().GetAllMaps(surface.Tile).Count);
                    Require(wealthTotalBefore - wealthTotalAfter >= 0.99f * logsValue,
                        "the total still counts the logs: it fell " + (wealthTotalBefore - wealthTotalAfter).ToString("F1") + " of " + logsValue.ToString("F1"));
                    Require(wealthStorytellerBefore - wealthStorytellerAfter >= 0.99f * logsValue,
                        "the storyteller wealth still counts the logs: it fell " + (wealthStorytellerBefore - wealthStorytellerAfter).ToString("F1") + " of " + logsValue.ToString("F1"));
                    Record("vault wealth", "PASS");
                    Record("vault load after", DescribeLoad());
                    Record("vault load", "PASS");
                    finished = true;
                    Root.Shutdown();
                }
            }
            catch (Exception exception)
            {
                finished = true;
                Record("vault load", "FAIL: " + exception);
                Log.Error("Z-Levels vault load validation failed: " + exception);
                Root.Shutdown();
            }
        }

        // The game keeps the item total in a private field. Reading it shows what the mod's sum is built from.
        private static float RawWealthItems(Map map)
        {
            var field = typeof(WealthWatcher).GetField("wealthItems", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            return field == null ? -1f : (float)field.GetValue(map.wealthWatcher);
        }

        private int SurfaceLogCount()
        {
            return surface == null ? 0 : surface.listerThings.ThingsOfDef(ThingDefOf.WoodLog).Sum(log => log.stackCount);
        }

        // Acceptance f. A colonist stands in the pocket with a wall built and seven logs on its floor. The game saves and reloads, and
        // afterwards the pocket, the colonist's cell, the wall, and the logs must all be there. The saved stage picks the phase.
        private void RunVaultSaveTick()
        {
            try
            {
                if (vaultSaveStage > 0 && vaultSaveStage < 4 && DateTime.UtcNow > vaultDeadline)
                {
                    throw new InvalidOperationException("Timed out at save stage " + vaultSaveStage + ", pawn job=" + pawn?.CurJob + ", pawn at " + pawn?.Position + " on " + pawn?.Map);
                }
                if (vaultSaveStage == 0)
                {
                    deadline = DateTime.MaxValue;
                    vaultDeadline = DateTime.UtcNow.AddMinutes(4);
                    surface = Find.CurrentMap;
                    pawn = FindValidationColonist(surface);
                    RestoreWork(pawn);
                    IntVec3 entranceCell = FindVaultEntranceCell(surface, pawn);
                    ClearCell(surface, entranceCell);
                    surface.terrainGrid.SetTerrain(entranceCell, TerrainDefOf.Concrete);
                    foreach (IntVec3 around in GenRadial.RadialCellsAround(entranceCell, 1.5f, useCenter: true))
                    {
                        ClearCell(surface, around);
                    }
                    vaultEntrance = (MapPortal)GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("ZL_VaultEntrance")), entranceCell, surface);
                    vaultPocket = vaultEntrance.GetOtherMap();
                    vaultWallDef = DefDatabase<ThingDef>.GetNamed("Wall");
                    vaultWallCell = FindVaultWallCell(vaultPocket, vaultEntrance.exit.Position);
                    Thing wood = ThingMaker.MakeThing(ThingDefOf.WoodLog);
                    wood.stackCount = 10;
                    GenSpawn.Spawn(wood, vaultWallCell + new IntVec3(2, 0, 0), vaultPocket);
                    pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.EnterPortal, vaultEntrance), JobCondition.InterruptForced);
                    vaultSaveStage = 1;
                    return;
                }
                if (vaultSaveStage == 1)
                {
                    RestNeeds(pawn);
                    if (pawn.Map != vaultPocket) return;
                    GenConstruct.PlaceBlueprintForBuild(vaultWallDef, vaultWallCell, vaultPocket, Rot4.North, Faction.OfPlayer, ThingDefOf.WoodLog);
                    vaultSaveStage = 2;
                    return;
                }
                if (vaultSaveStage == 2)
                {
                    RestNeeds(pawn);
                    if (vaultWallCell.GetEdifice(vaultPocket)?.def != vaultWallDef) return;
                    Thing logs = ThingMaker.MakeThing(ThingDefOf.WoodLog);
                    logs.stackCount = 7;
                    GenSpawn.Spawn(logs, FindFloorCell(vaultPocket, vaultEntrance.GetDestinationLocation()), vaultPocket);
                    savedPosition = pawn.Position;
                    vaultSavedWallCell = vaultWallCell;
                    savedLogs = PocketLogCount();
                    Record("wall built inside the pocket before the save", "PASS: colonist at " + savedPosition + ", logs in the pocket " + savedLogs);
                    vaultSaveStage = 3;
                    return;
                }
                if (vaultSaveStage == 3)
                {
                    vaultSaveStage = 4;
                    GameDataSaveLoader.SaveGame("ZLevelsValidation");
                    LongEventHandler.ExecuteWhenFinished(() => GameDataSaveLoader.LoadGame("ZLevelsValidation"));
                    return;
                }
                if (!reloaded) return;
                Map pocket = Current.Game.GetComponent<VaultPocket>()?.Pocket;
                Require(pocket != null && pocket.IsPocketMap, "the pocket exists after reload");
                var entrance = surface.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("ZL_VaultEntrance")).OfType<MapPortal>().FirstOrDefault();
                Require(entrance != null && entrance.GetOtherMap() == pocket, "the entrance still leads to the pocket");
                Record("pocket and entrance after reload", "PASS");
                Require(pawn.Map == pocket && pawn.Position == savedPosition, "the colonist is on the pocket at " + savedPosition + ", it is on " + pawn.Map + " at " + pawn.Position);
                Record("colonist on the pocket at the saved cell", "PASS");
                Require(vaultSavedWallCell.GetEdifice(pocket)?.def == DefDatabase<ThingDef>.GetNamed("Wall"), "the wall is missing after reload");
                Record("wall stands after reload", "PASS");
                int logsAfter = pocket.listerThings.ThingsOfDef(ThingDefOf.WoodLog).Sum(log => log.stackCount);
                Require(logsAfter == savedLogs && savedLogs >= 7, "the logs on the pocket floor: " + logsAfter + " after reload, " + savedLogs + " at the save");
                Record("logs on the pocket floor after reload", "PASS");
                Record("vault save", "PASS");
                finished = true;
                Root.Shutdown();
            }
            catch (Exception exception)
            {
                finished = true;
                Record("vault save", "FAIL: " + exception);
                Log.Error("Z-Levels vault save validation failed: " + exception);
                Root.Shutdown();
            }
        }

        // A free floor cell near the pocket's exit, for logs the test puts on the floor.
        private static IntVec3 FindFloorCell(Map pocket, IntVec3 exit)
        {
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(exit, 3f, false))
            {
                if (!cell.InBounds(pocket) || cell == exit || !cell.Standable(pocket) || cell.GetEdifice(pocket) != null || cell.GetFirstItem(pocket) != null) continue;
                return cell;
            }
            throw new InvalidOperationException("No free floor cell beside the pocket exit.");
        }

        private int PocketLogCount()
        {
            return vaultPocket == null ? 0 : vaultPocket.listerThings.ThingsOfDef(ThingDefOf.WoodLog).Sum(log => log.stackCount);
        }

        private string DescribeLoad()
        {
            int queued = vaultEntrance?.leftToLoad == null ? 0 : vaultEntrance.leftToLoad.Sum(item => item.CountToTransfer);
            return "logs on the surface " + SurfaceLogCount() + ", in the pocket " + PocketLogCount() + ", on the load list " + queued
                + ", colonist job " + (vaultColonist?.CurJob?.def.defName ?? "none") + " on " + vaultColonist?.Map;
        }

        // The pocket's storage zone goes beside the exit, so the logs land near it. Cells must be reachable from the exit.
        private static List<IntVec3> FindPocketZoneCells(Map pocket, IntVec3 exit)
        {
            var cells = new List<IntVec3>();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(exit, 2.5f, false))
            {
                if (cells.Count == 4) break;
                if (!cell.InBounds(pocket) || cell == exit || !cell.Standable(pocket) || cell.GetEdifice(pocket) != null) continue;
                if (!pocket.reachability.CanReach(exit, cell, PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassDoors))) continue;
                cells.Add(cell);
            }
            if (cells.Count == 0) throw new InvalidOperationException("No free cell beside the pocket exit for a storage zone.");
            return cells;
        }

        // Acceptance g. An infestation aimed at the vault pocket records nothing and digs no hole. The pocket's cave flag is forced on, so
        // the pocket check in Patch_Infestation_TryExecuteWorker is the only thing that can stop it. Same shape as RunInfestationGuard.
        private void RunVaultInfestationGuard()
        {
            try
            {
                deadline = DateTime.UtcNow.AddMinutes(10);
                var infestation = DefDatabase<IncidentDef>.GetNamed("Infestation");
                var holeDef = DefDatabase<ThingDef>.GetNamed("ZL_NaturalHole");
                surface = Find.CurrentMap;
                vaultEntrance = PlaceVaultEntrance(surface, FindValidationColonist(surface));
                Map pocket = vaultEntrance.GetOtherMap();
                var pocketComp = pocket.GetComponent<MapComponentZLevel>();
                pocketComp.hasCavesBelow = true;
                SpawnFood(pocket);
                int recordsBefore = pocketComp.TotalInfestations == null ? 0 : pocketComp.TotalInfestations.Count;
                int activeBefore = pocketComp.ActiveInfestations == null ? 0 : pocketComp.ActiveInfestations.Count;
                infestation.Worker.TryExecute(new IncidentParms { target = pocket, points = 1000f });
                int recordsAfter = pocketComp.TotalInfestations == null ? 0 : pocketComp.TotalInfestations.Count;
                int activeAfter = pocketComp.ActiveInfestations == null ? 0 : pocketComp.ActiveInfestations.Count;
                int holes = pocket.listerThings.ThingsOfDef(holeDef).Count;
                string outcome = "records " + recordsBefore + " -> " + recordsAfter + ", active " + activeBefore + " -> " + activeAfter + ", holes " + holes
                    + ", caves below " + pocketComp.hasCavesBelow;
                Require(recordsAfter == recordsBefore && activeAfter == activeBefore && holes == 0, "the pocket took an infestation: " + outcome);
                Record("infestation blocked on the pocket", "PASS: " + outcome);
                // Control: the same setup on the surface must dig a hole. Then the empty result above comes from the pocket check, not the setup.
                surface.GetComponent<MapComponentZLevel>().hasCavesBelow = true;
                SpawnFood(surface);
                infestation.Worker.TryExecute(new IncidentParms { target = surface, points = 1000f });
                int surfaceHoles = surface.listerThings.ThingsOfDef(holeDef).Count;
                Require(surfaceHoles >= 1, "the surface control dug no hole");
                Record("infestation control on the surface", "PASS: holes " + surfaceHoles);
                Record("vault infestation", "PASS");
                finished = true;
                Root.Shutdown();
            }
            catch (Exception exception)
            {
                finished = true;
                Record("vault infestation", "FAIL: " + exception);
                Log.Error("Z-Levels vault infestation validation failed: " + exception);
                Root.Shutdown();
            }
        }

        // An entrance on a reachable surface cell. The cell and its neighbours are cleared, as in the other vault checks.
        private MapPortal PlaceVaultEntrance(Map surface, Pawn colonist)
        {
            IntVec3 entranceCell = FindVaultEntranceCell(surface, colonist);
            ClearCell(surface, entranceCell);
            surface.terrainGrid.SetTerrain(entranceCell, TerrainDefOf.Concrete);
            foreach (IntVec3 around in GenRadial.RadialCellsAround(entranceCell, 1.5f, useCenter: true))
            {
                ClearCell(surface, around);
            }
            return (MapPortal)GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("ZL_VaultEntrance")), entranceCell, surface);
        }

        // Cross-level supply. Each test puts the materials on one level and the job on the other, so the job must fetch them across
        // the stairs. Only the validation colonist has work. Wood and granite on both levels are cleared before each test.
        private void RunSupplyTick()
        {
            try
            {
                if (supplyStage == 0)
                {
                    deadline = DateTime.MaxValue;
                    supplyDeadline = DateTime.UtcNow.AddMinutes(4);
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
                    surface = Find.CurrentMap;
                    pawn = FindValidationColonist(surface);
                    RestoreWork(pawn);
                    foreach (Pawn other in surface.mapPawns.FreeColonistsSpawned)
                    {
                        if (other != pawn) HoldWork(other);
                    }
                    var down = SpawnStair<Building_StairsDown>(ZLevelsDefOf.ZL_StairsDown, pawn.Position + new IntVec3(3, 0, 0));
                    down.GiveJob(pawn, down);
                    supplyStage = 1;
                    return;
                }
                RestNeeds(pawn);
                if (supplyStage == 1)
                {
                    if (pawn.Map == surface)
                    {
                        if (DateTime.UtcNow > supplyDeadline) throw new InvalidOperationException("The colonist did not walk down the stairs, " + DescribeSupply());
                        return;
                    }
                    lower = pawn.Map;
                    OpenLowerRoom(lower);
                    Record("colonist below for the supply tests", "PASS");
                    supplyStage = 10;
                    return;
                }
                if (supplyStage > 1 && supplyStage % 10 == 1 && DateTime.UtcNow > supplyDeadline)
                {
                    EndSupplyTest(false, "timed out, " + DescribeSupply());
                    return;
                }
                if (supplyStage == 10)
                {
                    CaptureSupplyBaseline();
                    ClearSupplyItems();
                    // 40 logs, not 20: a first build that fails by chance can still finish with the second 20.
                    PlaceSupplyStack(surface, MakeSupplyZone(surface, SurfaceStairsCell()), Def("WoodLog"), 40);
                    supplyCell = FindSupplyCell(lower, LowerStairsCell(), Def("Campfire").Size);
                    GenConstruct.PlaceBlueprintForBuild(Def("Campfire"), supplyCell, lower, Rot4.North, Faction.OfPlayer, null);
                    supplyBefore = CountOn(surface, Def("WoodLog"));
                    BeginSupplyTest("supply S1, build below from surface storage", 10);
                    return;
                }
                if (supplyStage == 11)
                {
                    if (supplyCell.GetEdifice(lower)?.def != Def("Campfire")) return;
                    int used = supplyBefore - CountOn(surface, Def("WoodLog"));
                    EndSupplyTest(used >= 20, used + " logs taken from the surface, the campfire needs 20");
                    return;
                }
                if (supplyStage == 20)
                {
                    ClearSupplyItems();
                    PlaceSupplyStack(lower, MakeSupplyZone(lower, LowerStairsCell()), Def("BlocksGranite"), 20);
                    supplyCell = FindSupplyCell(surface, SurfaceStairsCell(), Def("Wall").Size);
                    GenConstruct.PlaceBlueprintForBuild(Def("Wall"), supplyCell, surface, Rot4.North, Faction.OfPlayer, Def("BlocksGranite"));
                    supplyBefore = CountOn(lower, Def("BlocksGranite"));
                    BeginSupplyTest("supply S2, build on the surface from lower storage", 20);
                    return;
                }
                if (supplyStage == 21)
                {
                    if (supplyCell.GetEdifice(surface)?.def != Def("Wall")) return;
                    int used = supplyBefore - CountOn(lower, Def("BlocksGranite"));
                    EndSupplyTest(used > 0, used + " blocks taken from the lower level");
                    return;
                }
                if (supplyStage == 30)
                {
                    ClearSupplyItems();
                    PlaceSupplyStack(lower, MakeSupplyZone(lower, LowerStairsCell()), Def("ChunkGranite"), 20);
                    PlaceStonecutter(surface);
                    supplyBefore = CountOn(surface, Def("BlocksGranite"));
                    supplyOther = CountOn(lower, Def("ChunkGranite"));
                    BeginSupplyTest("supply S3, craft on the surface from lower storage", 30);
                    return;
                }
                if (supplyStage == 31)
                {
                    int made = CountOn(surface, Def("BlocksGranite")) - supplyBefore;
                    if (made <= 0) return;
                    int used = supplyOther - CountOn(lower, Def("ChunkGranite"));
                    EndSupplyTest(used > 0, made + " blocks made, " + used + " chunks taken from the lower level");
                    return;
                }
                if (supplyStage == 40)
                {
                    ClearSupplyItems();
                    PlaceSupplyStack(surface, MakeSupplyZone(surface, SurfaceStairsCell()), Def("ChunkGranite"), 20);
                    PlaceStonecutter(lower);
                    supplyBefore = CountOn(lower, Def("BlocksGranite"));
                    supplyOther = CountOn(surface, Def("ChunkGranite"));
                    BeginSupplyTest("supply S4, craft below from surface storage", 40);
                    return;
                }
                if (supplyStage == 41)
                {
                    int made = CountOn(lower, Def("BlocksGranite")) - supplyBefore;
                    if (made <= 0) return;
                    int used = supplyOther - CountOn(surface, Def("ChunkGranite"));
                    EndSupplyTest(used > 0, made + " blocks made, " + used + " chunks taken from the surface");
                    return;
                }
                if (supplyStage == 50)
                {
                    ClearSupplyItems();
                    PlaceSupplyStack(lower, MakeSupplyZone(lower, LowerStairsCell()), Def("WoodLog"), 20);
                    supplyFuel = SpawnEmptyCampfire(surface);
                    supplyBefore = CountOn(lower, Def("WoodLog"));
                    BeginSupplyTest("supply S5, refuel on the surface from lower storage", 50);
                    return;
                }
                if (supplyStage == 51)
                {
                    if (supplyFuel.Fuel <= 0f) return;
                    int used = supplyBefore - CountOn(lower, Def("WoodLog"));
                    EndSupplyTest(used > 0, used + " logs taken from the lower level");
                    return;
                }
                if (supplyStage == 60)
                {
                    ClearSupplyItems();
                    PlaceSupplyStack(surface, MakeSupplyZone(surface, SurfaceStairsCell()), Def("WoodLog"), 20);
                    supplyFuel = SpawnEmptyCampfire(lower);
                    supplyBefore = CountOn(surface, Def("WoodLog"));
                    BeginSupplyTest("supply S6, refuel below from surface storage", 60);
                    return;
                }
                if (supplyStage == 61)
                {
                    if (supplyFuel.Fuel <= 0f) return;
                    int used = supplyBefore - CountOn(surface, Def("WoodLog"));
                    EndSupplyTest(used > 0, used + " logs taken from the surface");
                    return;
                }
                if (supplyStage == 70)
                {
                    ClearSupplyItems();
                    // 10 logs below and 40 above: the partial stock the main chain's rebuild saw in runs 141006 to 143044.
                    PlaceSupplyStack(lower, MakeSupplyZone(lower, LowerStairsCell()), Def("WoodLog"), 10);
                    PlaceSupplyStack(surface, MakeSupplyZone(surface, SurfaceStairsCell()), Def("WoodLog"), 40);
                    supplyCell = FindSupplyCell(lower, LowerStairsCell(), Def("Campfire").Size);
                    GenConstruct.PlaceBlueprintForBuild(Def("Campfire"), supplyCell, lower, Rot4.North, Faction.OfPlayer, null);
                    supplyBefore = CountOn(surface, Def("WoodLog"));
                    supplyOther = CountOn(lower, Def("WoodLog"));
                    BeginSupplyTest("supply S7, build below with part of the logs below", 70);
                    return;
                }
                if (supplyStage == 71)
                {
                    if (supplyCell.GetEdifice(lower)?.def != Def("Campfire")) return;
                    int fromSurface = supplyBefore - CountOn(surface, Def("WoodLog"));
                    int fromLower = supplyOther - CountOn(lower, Def("WoodLog"));
                    EndSupplyTest(fromSurface > 0, fromLower + " logs taken from below and " + fromSurface + " from the surface, the campfire needs 20");
                    return;
                }
                if (supplyStage == 80)
                {
                    ClearSupplyItems();
                    // Logs on bare floor, not in a zone, as the main chain's colony stock is.
                    PlaceFloorStack(surface, SurfaceStairsCell(), Def("WoodLog"), 40);
                    supplyCell = FindSupplyCell(lower, LowerStairsCell(), Def("Campfire").Size);
                    GenConstruct.PlaceBlueprintForBuild(Def("Campfire"), supplyCell, lower, Rot4.North, Faction.OfPlayer, null);
                    supplyBefore = CountOn(surface, Def("WoodLog"));
                    BeginSupplyTest("supply S8, build below from logs on the surface floor", 80);
                    return;
                }
                if (supplyStage == 81)
                {
                    if (supplyCell.GetEdifice(lower)?.def != Def("Campfire")) return;
                    int fromSurface = supplyBefore - CountOn(surface, Def("WoodLog"));
                    EndSupplyTest(fromSurface >= 20, fromSurface + " logs taken from the surface floor, the campfire needs 20");
                    return;
                }
                if (supplyStage == 90)
                {
                    ClearSupplyItems();
                    // Logs on bare floor on both levels, as the main chain has them: 20 below, 40 above.
                    PlaceFloorStack(lower, LowerStairsCell(), Def("WoodLog"), 20);
                    PlaceFloorStack(surface, SurfaceStairsCell(), Def("WoodLog"), 40);
                    supplyCell = FindSupplyCell(lower, LowerStairsCell(), Def("Campfire").Size);
                    GenConstruct.PlaceBlueprintForBuild(Def("Campfire"), supplyCell, lower, Rot4.North, Faction.OfPlayer, null);
                    supplyBefore = CountOn(surface, Def("WoodLog"));
                    supplyOther = CountOn(lower, Def("WoodLog"));
                    supplyFailedOnce = false;
                    BeginSupplyTest("supply S9, rebuild after a failed build with logs on the floor", 90);
                    return;
                }
                if (supplyStage == 91)
                {
                    if (!supplyFailedOnce)
                    {
                        if (supplyCell.GetEdifice(lower) is Frame frame)
                        {
                            supplyFailedOnce = true;
                            frame.FailConstruction(pawn);
                            Record("supply S9 construction failed once on purpose", "PASS");
                        }
                        return;
                    }
                    if (supplyCell.GetEdifice(lower)?.def != Def("Campfire")) return;
                    int fromSurface = supplyBefore - CountOn(surface, Def("WoodLog"));
                    int fromLower = supplyOther - CountOn(lower, Def("WoodLog"));
                    EndSupplyTest(true, fromLower + " logs taken from below and " + fromSurface + " from the surface after the failed build");
                    return;
                }
                if (supplyStage == 100)
                {
                    Record("cross-level supply", supplyFailures == 0 ? "PASS" : "FAIL: " + supplyFailures + " of 9 supply tests failed, see the lines above");
                    finished = true;
                    Root.Shutdown();
                }
            }
            catch (Exception exception)
            {
                finished = true;
                Record("cross-level supply", "FAIL: " + exception);
                Log.Error("Z-Levels cross-level supply validation failed: " + exception);
                Root.Shutdown();
            }
        }

        private void BeginSupplyTest(string label, int setupStage)
        {
            supplyLabel = label;
            supplyStage = setupStage + 1;
            supplyNextStage = setupStage + 10;
            supplyDeadline = DateTime.UtcNow.AddMinutes(4);
        }

        // A failed test is recorded and the next one runs, so one run shows every result.
        private void EndSupplyTest(bool passed, string detail)
        {
            if (passed)
            {
                Record(supplyLabel, "PASS: " + detail);
            }
            else
            {
                supplyFailures++;
                Record(supplyLabel, "FAIL: " + detail);
            }
            supplyStage = supplyNextStage;
        }

        // Everything that exists before the first supply test. Cleanup never removes these.
        private void CaptureSupplyBaseline()
        {
            supplyBaseline = new HashSet<int>();
            foreach (Map map in new[] { surface, lower })
            {
                foreach (Thing thing in map.listerThings.AllThings)
                {
                    supplyBaseline.Add(thing.thingIDNumber);
                }
            }
        }

        private IntVec3 StairsCellOf(Map map)
        {
            return map == surface ? SurfaceStairsCell() : LowerStairsCell();
        }

        // A campfire with no fuel, so the refuel job has to bring the logs.
        private CompRefuelable SpawnEmptyCampfire(Map map)
        {
            ThingDef campfire = Def("Campfire");
            IntVec3 cell = FindSupplyCell(map, StairsCellOf(map), campfire.Size);
            CompRefuelable refuelable = SpawnPlayerBuilding(map, campfire, cell).GetComp<CompRefuelable>();
            EmptyFuel(refuelable);
            return refuelable;
        }

        // The stairs cell may have no region to path from, so reachability starts at a free cell beside it.
        private static IntVec3 StandableBeside(Map map, IntVec3 cell)
        {
            foreach (IntVec3 neighbour in GenRadial.RadialCellsAround(cell, 1.5f, false))
            {
                if (neighbour.InBounds(map) && neighbour.Standable(map) && neighbour.GetEdifice(map) == null) return neighbour;
            }
            throw new InvalidOperationException("No free cell beside " + cell + " on " + map);
        }

        private static ThingDef Def(string name)
        {
            return DefDatabase<ThingDef>.GetNamed(name);
        }

        private IntVec3 SurfaceStairsCell()
        {
            return surface.listerThings.AllThings.OfType<Building_StairsDown>().First().Position;
        }

        private IntVec3 LowerStairsCell()
        {
            return lower.listerThings.AllThings.OfType<Building_StairsUp>().First().Position;
        }

        private static int CountOn(Map map, ThingDef def)
        {
            return map.listerThings.ThingsOfDef(def).Sum(thing => thing.stackCount);
        }

        // Removes the materials, and the buildings and blueprints that earlier supply tests made. Anything in supplyBaseline stays.
        private void ClearSupplyItems()
        {
            // Every stone chunk counts: the stonecutter takes any of them, so a natural chunk left on the map would be used instead.
            ThingCategoryDef stoneChunks = DefDatabase<ThingCategoryDef>.GetNamed("StoneChunks");
            foreach (Map map in new[] { surface, lower })
            {
                foreach (Thing thing in map.listerThings.AllThings.ToList())
                {
                    bool material = thing.def == Def("WoodLog") || thing.def == Def("BlocksGranite") || (thing.def.thingCategories?.Contains(stoneChunks) ?? false);
                    bool built = !supplyBaseline.Contains(thing.thingIDNumber) && (thing is Blueprint || thing is Frame
                        || thing.def == Def("Campfire") || thing.def == Def("Wall") || thing.def == Def("TableStonecutter"));
                    if (material || built) thing.Destroy(DestroyMode.Vanish);
                }
            }
            if (pawn.carryTracker.CarriedThing != null) pawn.carryTracker.innerContainer.ClearAndDestroyContents();
        }

        // A stockpile zone of up to four free cells, reachable from the stairs.
        private static Zone_Stockpile MakeSupplyZone(Map map, IntVec3 near)
        {
            var zone = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
            map.zoneManager.RegisterZone(zone);
            int added = 0;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(near, 3f, false))
            {
                if (added == 4) break;
                if (!cell.InBounds(map) || cell == near || !cell.Standable(map) || cell.GetEdifice(map) != null || cell.GetFirstItem(map) != null
                    || map.zoneManager.ZoneAt(cell) != null) continue;
                if (!map.reachability.CanReach(StandableBeside(map, near), cell, PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassDoors))) continue;
                zone.AddCell(cell);
                added++;
            }
            if (added == 0) throw new InvalidOperationException("No free cell for a storage zone near " + near + " on " + map);
            return zone;
        }

        // Spawns count of def as stacks no bigger than the def's stack limit, spread over the zone's cells.
        private static void PlaceSupplyStack(Map map, Zone_Stockpile zone, ThingDef def, int count)
        {
            List<IntVec3> cells = zone.Cells.Where(c => c.GetEdifice(map) == null).ToList();
            for (int i = 0; count > 0; i++)
            {
                int size = Math.Min(count, def.stackLimit);
                Thing stack = ThingMaker.MakeThing(def);
                stack.stackCount = size;
                GenSpawn.Spawn(stack, cells[i % cells.Count], map);
                count -= size;
            }
        }

        // Spawns count of def as stacks no bigger than the def's stack limit, on one free floor cell near a point (not in a zone).
        private static void PlaceFloorStack(Map map, IntVec3 near, ThingDef def, int count)
        {
            IntVec3 cell = FindSupplyCell(map, near, IntVec2.One);
            while (count > 0)
            {
                int size = Math.Min(count, def.stackLimit);
                Thing stack = ThingMaker.MakeThing(def);
                stack.stackCount = size;
                GenSpawn.Spawn(stack, cell, map);
                count -= size;
            }
        }

        // A free cell with room for the whole footprint, reachable from the stairs.
        private static IntVec3 FindSupplyCell(Map map, IntVec3 near, IntVec2 size)
        {
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(near, 4f, false))
            {
                CellRect footprint = GenAdj.OccupiedRect(cell, Rot4.North, size);
                if (!footprint.Cells.All(c => c.InBounds(map) && c.Standable(map) && c.GetEdifice(map) == null && c.GetFirstItem(map) == null
                    && map.zoneManager.ZoneAt(c) == null)) continue;
                if (!map.reachability.CanReach(StandableBeside(map, near), cell, PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassDoors))) continue;
                return cell;
            }
            throw new InvalidOperationException("No free cell within 4 cells of " + near + " on " + map);
        }

        private static ThingWithComps SpawnPlayerBuilding(Map map, ThingDef def, IntVec3 cell)
        {
            var building = (ThingWithComps)ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
            building.SetFaction(Faction.OfPlayer);
            return (ThingWithComps)GenSpawn.Spawn(building, cell, map);
        }

        private static void EmptyFuel(CompRefuelable refuelable)
        {
            if (refuelable.Fuel > 0f) refuelable.ConsumeFuel(refuelable.Fuel);
            Require(refuelable.Fuel <= 0f, "the campfire started with fuel");
        }

        // A stonecutter's table with one bill: make any stone blocks.
        private void PlaceStonecutter(Map map)
        {
            ThingDef table = Def("TableStonecutter");
            IntVec3 cell = FindSupplyCell(map, StairsCellOf(map), table.Size);
            var building = (Building_WorkTable)SpawnPlayerBuilding(map, table, cell);
            building.BillStack.AddBill(new Bill_Production(DefDatabase<RecipeDef>.GetNamed("Make_StoneBlocksAny")));
        }

        private string DescribeSupply()
        {
            return "colonist job " + (pawn?.CurJob?.def.defName ?? "none") + " on " + pawn?.Map + " at " + pawn?.Position
                + ", wood on the surface " + CountOn(surface, Def("WoodLog")) + " and below " + (lower == null ? 0 : CountOn(lower, Def("WoodLog")))
                + ", granite blocks on the surface " + CountOn(surface, Def("BlocksGranite")) + " and below " + (lower == null ? 0 : CountOn(lower, Def("BlocksGranite")))
                + ", chunks on the surface " + CountOn(surface, Def("ChunkGranite")) + " and below " + (lower == null ? 0 : CountOn(lower, Def("ChunkGranite")))
                + ", game ticks " + Find.TickManager.TicksGame + ", speed " + Find.TickManager.CurTimeSpeed;
        }

        private static IntVec3 FindVaultEntranceCell(Map surface, Pawn colonist)
        {
            IntVec3 origin = colonist.Position;
            for (int dx = -12; dx <= 12; dx++)
            {
                for (int dz = -12; dz <= 12; dz++)
                {
                    IntVec3 cell = origin + new IntVec3(dx, 0, dz);
                    if (!cell.InBounds(surface) || !cell.Standable(surface) || cell.DistanceTo(origin) < 6f)
                    {
                        continue;
                    }
                    if (colonist.CanReach(cell, PathEndMode.Touch, Danger.Deadly))
                    {
                        return cell;
                    }
                }
            }
            throw new InvalidOperationException("No cell near the colonist can be reached for the vault entrance.");
        }

        // Facts for a timeout message: the colonist's job, the blueprint, and the wood beside the wall.
        private string DescribeVaultEnter()
        {
            Blueprint blueprint = vaultPocket != null && vaultWallCell.IsValid ? vaultWallCell.GetThingList(vaultPocket).OfType<Blueprint>().FirstOrDefault() : null;
            int wood = vaultPocket != null && vaultWallCell.IsValid ? (vaultWallCell + new IntVec3(2, 0, 0)).GetThingList(vaultPocket).Where(t => t.def == ThingDefOf.WoodLog).Sum(t => t.stackCount) : -1;
            Pawn colonist = vaultColonist;
            return "colonist job " + colonist?.CurJob?.def?.defName
                + ", colonist on the pocket " + (colonist != null && colonist.Map == vaultPocket)
                + ", construction priority " + colonist?.workSettings?.GetPriority(WorkTypeDefOf.Construction)
                + ", blueprint " + (blueprint != null ? blueprint.def.defName : "none")
                + ", wood beside the wall " + wood
                + ", colonist can reach the wall cell " + (colonist != null && vaultWallCell.IsValid && colonist.Map == vaultPocket && colonist.CanReach(vaultWallCell, PathEndMode.Touch, Danger.Deadly));
        }

        // A pocket cell with a free cell two steps east for the wood. It sits away from the exit, so the colonist can walk to both.
        private static IntVec3 FindVaultWallCell(Map pocket, IntVec3 exit)
        {
            foreach (IntVec3 cell in pocket.AllCells)
            {
                IntVec3 woodCell = cell + new IntVec3(2, 0, 0);
                if (cell.DistanceTo(exit) < 4 || cell.DistanceToEdge(pocket) < 3 || !cell.Standable(pocket))
                {
                    continue;
                }
                if (!woodCell.InBounds(pocket) || !woodCell.Standable(pocket))
                {
                    continue;
                }
                if (cell.GetThingList(pocket).Count > 0 || woodCell.GetThingList(pocket).Count > 0)
                {
                    continue;
                }
                return cell;
            }
            throw new InvalidOperationException("The vault pocket has no room for a wall.");
        }

        // Every vault entrance shares one pocket map, and each entrance gets its own exit in it. The first entrance makes the pocket.
        private void RunVaultPocketCheck()
        {
            try
            {
                deadline = DateTime.UtcNow.AddMinutes(10);
                var tracker = Current.Game.GetComponent<ZLevelsManager>();
                var entranceDef = DefDatabase<ThingDef>.GetNamed("ZL_VaultEntrance");
                Map surface = Find.CurrentMap;
                IntVec3 firstCell = surface.Center + new IntVec3(6, 0, 6);
                IntVec3 secondCell = surface.Center + new IntVec3(-6, 0, 6);
                foreach (IntVec3 cell in new[] { firstCell, secondCell })
                {
                    ClearCell(surface, cell);
                    surface.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete);
                }
                Require(entranceDef.portal != null, "the vault entrance has a portal block");
                MapPortal first = (MapPortal)GenSpawn.Spawn(ThingMaker.MakeThing(entranceDef), firstCell, surface);
                Map pocket = first.GetOtherMap();
                Require(pocket != null && pocket.IsPocketMap, "the first entrance makes a pocket map");
                Require(tracker.mapIndex == null || !tracker.mapIndex.ContainsKey(pocket), "the pocket is not a numbered level");
                Require(tracker.ZLevelsTracker == null || !tracker.ZLevelsTracker.Values.Any(levels => levels.ZLevels != null && levels.ZLevels.ContainsValue(pocket)), "the pocket is in no level list");
                Require(first.exit != null && first.exit.entrance == first && first.exit.Map == pocket, "the first entrance has an exit in the pocket");
                Require(first.exit.GetOtherMap() == surface, "the first exit leads back to the surface");
                Record("vault pocket created", "PASS");
                MapPortal second = (MapPortal)GenSpawn.Spawn(ThingMaker.MakeThing(entranceDef), secondCell, surface);
                Require(second.GetOtherMap() == pocket, "the second entrance uses the same pocket");
                Require(second.exit != null && second.exit != first.exit && second.exit.entrance == second && second.exit.Map == pocket, "the second entrance has its own exit in the pocket");
                Require(second.exit.GetOtherMap() == surface, "the second exit leads back to the surface");
                Record("second entrance shares the pocket", "PASS");
                finished = true;
                Record("vault pocket", "PASS");
                Root.Shutdown();
            }
            catch (Exception exception)
            {
                finished = true;
                Record("vault pocket", "FAIL: " + exception);
                Log.Error("Z-Levels vault pocket validation failed: " + exception);
                Root.Shutdown();
            }
        }

        // A look-up table entry can name a map that has left the game. Run 20261030-212014 hit one after a reload: the
        // reachability prefix teleported the colonist onto it and threw. Red before the fix, green after.
        private void RunStaleLookupCheck()
        {
            try
            {
                deadline = DateTime.UtcNow.AddMinutes(10);
                var tracker = Current.Game.GetComponent<ZLevelsManager>();
                var colonist = Find.CurrentMap.mapPawns.FreeColonistsSpawned.First();
                Map home = colonist.Map;
                IntVec3 homeCell = colonist.Position;
                Map stale = CreateLevelBelow(tracker, home);
                IntVec3 target = stale.Center;
                Current.Game.DeinitAndRemoveMap(stale, false);
                Require(!Find.Maps.Contains(stale), "the stale map is out of the game");
                // The engine keeps the field after removal. Run 20261030-212014 had a look-up entry whose map had no reachability.
                stale.reachability = null;
                if (!tracker.jobTracker.TryGetValue(colonist, out JobTracker jobTracker))
                {
                    tracker.ResetJobTrackerFor(colonist);
                    jobTracker = tracker.jobTracker[colonist];
                }
                if (jobTracker.lookedAtLocalCellMap == null) jobTracker.lookedAtLocalCellMap = new System.Collections.Generic.Dictionary<IntVec3, Map>();
                // The LocalTargetInfo constructor postfix overwrites the look-up entry for its cell, so build the target first.
                var dest = new LocalTargetInfo(target);
                jobTracker.lookedAtLocalCellMap[target] = stale;
                string outcome;
                try
                {
                    home.reachability.CanReach(homeCell, dest, PathEndMode.OnCell, TraverseParms.For(colonist));
                    outcome = "no exception";
                }
                catch (Exception exception)
                {
                    outcome = "exception " + exception.GetType().Name;
                }
                Record("stale look-up reachability", outcome + ", colonist on " + colonist.Map + " at " + colonist.Position);
                Require(colonist.Map == home && colonist.Position == homeCell, "the colonist was moved onto the stale map");
                Require(outcome == "no exception", "reachability check " + outcome);
                jobTracker.lookedAtLocalCellMap.Remove(target);
                finished = true;
                Record("stale look-up map", "PASS");
                Root.Shutdown();
            }
            catch (Exception exception)
            {
                finished = true;
                Record("stale look-up map", "FAIL: " + exception);
                Log.Error("Z-Levels stale look-up validation failed: " + exception);
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

        // The unforced haul test forbids the colony's other wood, so it cannot be used. The campfire rebuild needs the rest of its logs
        // from the surface stock, so release that wood here. The other colonists keep no work.
        private void ReleaseForbiddenWood()
        {
            foreach (var map in new[] { surface, lower })
            {
                foreach (var wood in map.listerThings.ThingsOfDef(ThingDefOf.WoodLog).ToList())
                {
                    wood.SetForbidden(false);
                }
            }
        }

        // Test only: a campfire blueprint in the lower room, with 20 logs beside it. Only the validation colonist may haul or build.
        private void PlaceCampfire(Map map)
        {
            Building_StairsUp stairs = map.listerThings.AllThings.OfType<Building_StairsUp>().First();
            IntVec3 logCell = IntVec3.Invalid;
            campfireCell = IntVec3.Invalid;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(stairs.Position, 3f, false))
            {
                if (!cell.InBounds(map) || cell.DistanceTo(stairs.Position) < 2f || !cell.Standable(map)) continue;
                if (cell.GetEdifice(map) != null || cell.GetFirstItem(map) != null) continue;
                if (!logCell.IsValid) { logCell = cell; continue; }
                campfireCell = cell;
                break;
            }
            if (!logCell.IsValid || !campfireCell.IsValid) throw new InvalidOperationException("No free cells in the lower room for a campfire.");
            var logs = ThingMaker.MakeThing(ThingDefOf.WoodLog);
            // A campfire costs 20 logs. Exactly 20 is the case under test: a failed first build leaves part of them on this level, and the
            // rebuild must take the rest from the surface. Run 20261031-143044 stalled here with 10 logs left below. 40 was the workaround.
            logs.stackCount = 20;
            campfireLogCell = logCell;
            GenSpawn.Spawn(logs, logCell, map);
            GenConstruct.PlaceBlueprintForBuild(DefDatabase<ThingDef>.GetNamed("Campfire"), campfireCell, map, Rot4.North, Faction.OfPlayer, null);
            Record("campfire placed", DescribeCampfire());
            foreach (var colonistMap in new[] { surface, lower, upper })
            {
                if (colonistMap == null) continue;
                foreach (var colonist in colonistMap.mapPawns.FreeColonistsSpawned)
                {
                    if (colonist == pawn || colonist.WorkTypeIsDisabled(WorkTypeDefOf.Construction)) continue;
                    colonist.workSettings.SetPriority(WorkTypeDefOf.Construction, 0);
                }
            }
        }

        // Test only: one natural-rock wall on the edge of the lower room gets a mining designation. Only the validation colonist may
        // mine, so the job can only be its own.
        private void DesignateMining(Map map)
        {
            Building_StairsUp stairs = map.listerThings.AllThings.OfType<Building_StairsUp>().First();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(stairs.Position, 4.5f, false))
            {
                if (!cell.InBounds(map) || cell.DistanceTo(stairs.Position) < 3.5f) continue;
                Building rock = cell.GetEdifice(map);
                if (rock == null || !rock.def.mineable) continue;
                bool openNeighbour = false;
                foreach (IntVec3 near in GenRadial.RadialCellsAround(cell, 1.5f, false))
                {
                    if (near.InBounds(map) && near.Walkable(map) && near.DistanceTo(stairs.Position) <= 3.5f) openNeighbour = true;
                }
                if (!openNeighbour) continue;
                minedWall = rock;
                map.designationManager.AddDesignation(new Designation(rock, DesignationDefOf.Mine));
                foreach (var colonistMap in new[] { surface, lower, upper })
                {
                    if (colonistMap == null) continue;
                    foreach (var colonist in colonistMap.mapPawns.FreeColonistsSpawned)
                    {
                        if (colonist == pawn || colonist.WorkTypeIsDisabled(WorkTypeDefOf.Mining)) continue;
                        colonist.workSettings.SetPriority(WorkTypeDefOf.Mining, 0);
                    }
                }
                return;
            }
            throw new InvalidOperationException("No rock wall on the edge of the lower room to mine.");
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

        // Priority 3 is the default for an active work type. naturalPriority is a sort key (100 to 1400), not a priority: the game logs it as invalid.
        private static void RestoreWork(Pawn colonist)
        {
            foreach (WorkTypeDef work in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                if (colonist.WorkTypeIsDisabled(work)) continue;
                colonist.workSettings.SetPriority(work, 3);
            }
        }

        // Backstories can disable work types: Solid_Child.xml disables ManualDumb (Hauling, Cleaning), and others disable Mining or
        // Construction. Run 20261030-205256 used such a colonist. Its Hauling stayed 0, and it idled while the logs stayed below.
        private static Pawn FindValidationColonist(Map map)
        {
            foreach (Pawn colonist in map.mapPawns.FreeColonistsSpawned)
            {
                if (!colonist.WorkTypeIsDisabled(WorkTypeDefOf.Hauling) && !colonist.WorkTypeIsDisabled(WorkTypeDefOf.Mining)
                    && !colonist.WorkTypeIsDisabled(WorkTypeDefOf.Construction)) return colonist;
            }
            throw new InvalidOperationException("No free colonist can haul, mine and build.");
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

        // Test only: the campfire blueprint, and whether the colonist can reach it and its logs. Uses the map's reachability with no pawn,
        // because a pawn-based CanReach from the harness throws in the ZL prefix (run 20261030-193352).
        private string DescribeCampfire()
        {
            if (!campfireCell.IsValid || lower == null) return "campfire none";
            Thing blueprint = campfireCell.GetThingList(lower).FirstOrDefault(thing => thing is Blueprint);
            string state = "campfire blueprint " + (blueprint == null ? "missing" : blueprint.def.defName + " at " + blueprint.Position + " destroyed " + blueprint.Destroyed);
            if (pawn == null || pawn.Map != lower) return state + ", colonist not on the lower level";
            string reachesLogs = campfireLogCell.IsValid
                ? lower.reachability.CanReach(pawn.Position, campfireLogCell, PathEndMode.Touch, TraverseParms.For(TraverseMode.PassDoors)).ToString()
                : "no logs placed";
            return state + ", colonist reaches blueprint " + lower.reachability.CanReach(pawn.Position, campfireCell, PathEndMode.Touch, TraverseParms.For(TraverseMode.PassDoors))
                + ", colonist reaches logs " + reachesLogs
                + ", construction priority " + pawn.workSettings.GetPriority(WorkTypeDefOf.Construction) + ", construction disabled " + pawn.WorkTypeIsDisabled(WorkTypeDefOf.Construction)
                + ", hauling priority " + pawn.workSettings.GetPriority(WorkTypeDefOf.Hauling)
                + ", use work priorities " + Find.PlaySettings.useWorkPriorities;
        }

        // Test only: the colonist's look-up table, which holds map references saved with the game. A map the game no longer holds can make
        // the reachability prefix throw and leave the colonist on that map (run 20261030-212014).
        private static string DescribeLookUpMaps(Pawn colonist)
        {
            var manager = Current.Game.GetComponent<ZLevelsManager>();
            if (colonist == null || manager?.jobTracker == null || !manager.jobTracker.TryGetValue(colonist, out JobTracker tracker)
                || tracker.lookedAtLocalCellMap == null) return "look-up none";
            int notInGame = 0;
            int noReachability = 0;
            foreach (Map map in tracker.lookedAtLocalCellMap.Values)
            {
                if (map == null || !Find.Maps.Contains(map)) notInGame++;
                else if (map.reachability == null) noReachability++;
            }
            return "look-up entries " + tracker.lookedAtLocalCellMap.Count + ", maps not in the game " + notInGame + ", maps without reachability " + noReachability;
        }

        // Test only: each change of the colonist's level or job, with its queued jobs and the surface stairs it can reach. A timeout
        // message prints the last entries, so a stall shows the moves before it.
        private void RecordCampfireTrail()
        {
            if (pawn == null || pawn.Map == null) return;
            string where = pawn.Map == lower ? "lower" : pawn.Map == surface ? "surface" : pawn.Map.ToString();
            string job = pawn.CurJob == null ? "no job" : pawn.CurJob.def.defName;
            string entry = where + " " + job;
            if (entry == lastTrailEntry) return;
            lastTrailEntry = entry;
            var manager = Current.Game.GetComponent<ZLevelsManager>();
            int queued = 0;
            if (manager.jobTracker != null && manager.jobTracker.TryGetValue(pawn, out JobTracker tracker) && tracker.activeJobs != null) queued = tracker.activeJobs.Count;
            string stairs = pawn.Map == surface ? ", " + DescribeSurfaceStairs() : "";
            string text = DateTime.UtcNow.ToString("HH:mm:ss") + " " + entry + " at " + pawn.Position + ", queued " + queued + stairs;
            // The first entries show how the colonist got off the lower level; the latest show the stall.
            if (campfireTrail.Count < 60)
            {
                campfireTrail.Add(text);
                return;
            }
            campfireTrailLatest.Add(text);
            if (campfireTrailLatest.Count > 40) campfireTrailLatest.RemoveAt(0);
        }

        private string DescribeCampfireTrail()
        {
            if (campfireTrail.Count == 0) return "none";
            string first = string.Join(" | ", campfireTrail);
            return campfireTrailLatest.Count == 0 ? first : first + " | ... | " + string.Join(" | ", campfireTrailLatest);
        }

        // Test only: after the forced failure, what the lower level can still deliver to the rebuilt blueprint, as the vanilla
        // delivery search sees it. Reachability is checked from the blueprint's cell, since the colonist may be on either level.
        private string DescribeRebuild()
        {
            if (!constructionFailed || lower == null || !campfireCell.IsValid) return "none";
            Blueprint blueprint = campfireCell.GetThingList(lower).OfType<Blueprint>().FirstOrDefault();
            if (blueprint == null) return "no blueprint on the lower level";
            int needed = ((IConstructible)blueprint).ThingCountNeeded(ThingDefOf.WoodLog);
            List<Thing> logs = lower.listerThings.ThingsOfDef(ThingDefOf.WoodLog);
            Thing closest = GenClosest.ClosestThingReachable(campfireCell, lower, ThingRequest.ForDef(ThingDefOf.WoodLog), PathEndMode.ClosestTouch, TraverseParms.For(TraverseMode.PassDoors));
            bool available = lower.itemAvailability.ThingsAvailableAnywhere(ThingDefOf.WoodLog, Math.Max(needed, 1), pawn);
            List<Thing> surfaceLogs = surface.listerThings.ThingsOfDef(ThingDefOf.WoodLog);
            bool surfaceAvailable = surface.itemAvailability.ThingsAvailableAnywhere(ThingDefOf.WoodLog, Math.Max(needed, 1), pawn);
            Thing blocking = GenConstruct.FirstBlockingThing(blueprint, pawn);
            return "blueprint " + blueprint.def.defName + " needs " + needed + " logs, logs on the lower level " + logs.Sum(log => log.stackCount)
                + " in " + logs.Count + " stacks, available " + available + ", closest reachable " + (closest == null ? "none" : closest.Position.ToString())
                + ", logs on the surface " + surfaceLogs.Sum(log => log.stackCount) + " in " + surfaceLogs.Count + " stacks, surface available " + surfaceAvailable
                + ", blocking " + (blocking == null ? "none" : blocking.ToString()) + " at " + (blocking == null ? "none" : blocking.Position.ToString())
                + ", lower logs at " + string.Join(", ", logs.Select(log => log.stackCount + "@" + log.Position));
        }

        // Test only: the surface stairs down the colonist can reach by map reachability, with no pawn, as DescribeCampfire does.
        // ZUtils.GetAllMapsInClosestOrder tries these stairs with pawn.CanReach before it searches the level below.
        private string DescribeSurfaceStairs()
        {
            int total = 0;
            int reachable = 0;
            string below = "";
            foreach (Building_StairsDown stairs in surface.listerThings.AllThings.OfType<Building_StairsDown>())
            {
                total++;
                if (surface.reachability.CanReach(pawn.Position, stairs.Position, PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassDoors))) reachable++;
                // The search teleports the colonist to these stairs on the lower level. Check the campfire from there.
                if (below == "" && lower != null && campfireCell.IsValid && campfireLogCell.IsValid)
                {
                    below = ", from those stairs below the blueprint reachable " + lower.reachability.CanReach(stairs.Position, campfireCell, PathEndMode.Touch, TraverseParms.For(TraverseMode.PassDoors))
                        + ", below the logs reachable " + lower.reachability.CanReach(stairs.Position, campfireLogCell, PathEndMode.Touch, TraverseParms.For(TraverseMode.PassDoors));
                }
            }
            return "surface stairs down reachable " + reachable + " of " + total + below;
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
