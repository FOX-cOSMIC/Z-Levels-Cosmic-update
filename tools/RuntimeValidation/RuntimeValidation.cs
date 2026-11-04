using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
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
        // Test only: names of the colonists seen mining and building under -zlevelsfreecolonists.
        private readonly HashSet<string> minersSeen = new HashSet<string>();
        private readonly HashSet<string> buildersSeen = new HashSet<string>();
        // Test only: -zlevelsunreachablezone walls in one stockpile cell and counts the haul jobs that head for it. Run 20261030-192636 retried such a haul every second.
        private int unreachableStage;
        private DateTime unreachableEnd;
        private int unreachableStarts;
        private readonly Dictionary<Pawn, Job> unreachableLastJobs = new Dictionary<Pawn, Job>();
        private IntVec3 unreachableCell = IntVec3.Invalid;
        private IntVec3 unreachableFarCell = IntVec3.Invalid;
        private int unreachableFarStarts;
        private const int MultiBuildSiteCount = 3;
        private int multiBuildStage;
        private List<IntVec3> multiBuildSites = new List<IntVec3>();
        private int multiBuildRemovedItems;
        private readonly Dictionary<Pawn, Job> multiBuildLastJobs = new Dictionary<Pawn, Job>();
        private readonly HashSet<string> multiBuildBuilders = new HashSet<string>();
        private int multiBuildCrossHauls;
        private int multiBuildLateItems;
        private int multiBuildLowerWalks;
        private int multiBuildInitialLogs;
        private int stairsDamageStage;
        private Building_StairsDown stairsDamagePartner;
        // Test only: with -zlevelsallcolonistshaul, every colonist keeps its hauling, so several colonists can take the unforced haul.
        private static bool OtherColonistsHaul => GenCommandLine.CommandLineArgPassed("zlevelsothercolonistshaul");
        // With -zlevelsothercolonistshaul the validation colonist is also kept off hauling, so the haul must be taken by another colonist.
        private static bool AllColonistsHaul => OtherColonistsHaul || GenCommandLine.CommandLineArgPassed("zlevelsallcolonistshaul");
        // Test only: -zlevelsfreecolonists leaves every colonist's work on for the whole chain, so the mining and the campfire can be done by any colonist.
        private static bool FreeColonists => GenCommandLine.CommandLineArgPassed("zlevelsfreecolonists");
        // Test only: -zlevelsothercolonistswork keeps the validation colonist off hauling, mining and building, so other colonists must do all three.
        private static bool OtherColonistsWork => GenCommandLine.CommandLineArgPassed("zlevelsothercolonistswork");
        // Colonists seen below during the unforced haul, by name.
        private readonly HashSet<string> belowDuringWait = new HashSet<string>();
        // Colonists seen carrying the harness's logs during the unforced haul, by name. The haul checks read this set.
        private readonly HashSet<string> haulersSeen = new HashSet<string>();

        // State for the vault enter-and-build check. It is not saved.
        private int vaultStage;
        private Pawn vaultColonist;
        private MapPortal vaultEntrance;
        private Map vaultPocket;
        private IntVec3 vaultWallCell;
        private IntVec3 vaultMineCell;
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

        // State for the cross-level cooling checks. It is not saved. A save and reload makes a new RuntimeValidation, and the link sequence continues in
        // that instance, so the fields it reads across the reload are static. They last for the process, which runs one scenario.
        private static readonly Dictionary<string, string> coolingResults = new Dictionary<string, string>();
        private static bool coolingChecksRecorded;
        private DateTime? coolingShotDue;
        private bool coolingShotTaken;
        // A core screenshot row is written once its file has had three seconds to appear, the same wait the cooling row uses.
        private string pendingShotRow;
        private string pendingShotName;
        private DateTime pendingShotDue;
        // Set when the screenshot row fails, so the cooling link row cannot read PASS beside it.
        private static bool coolingScreenshotFailed;
        // Set when the partner outline row fails, so the cooling link row cannot read PASS beside it.
        private static bool partnerDrawnFailed;
        // State for the partner highlight rows and look. It is not saved. partnerLookStage is 0 when the look is not running.
        private IntVec3 partnerCell = IntVec3.Invalid;
        private ThingDef partnerColumn;
        private ZLevelsRimatomics.CompCoolingColumn partnerSurfaceComp;
        private int partnerLookStage;
        private DateTime partnerShotDue;
        // -zlevelscoolingnocolumns runs the same scenario without the column pair. It places no columns, so the pairing labels, the partner highlight labels,
        // and the link rows that need a column record NoColumnsPairMissing. The link scene and the reactor checks still run.
        private static bool CoolingNoColumns => GenCommandLine.CommandLineArgPassed("zlevelscoolingnocolumns");
        private const string NoColumnsPairMissing = "FAIL: no column pair is placed in the no-columns variant";
        // The screenshot row. The first frame of the cooling tick records it, and the outer catch of that tick writes it when the tick throws.
        private const string CoolingScreenshotLabel = "cooling column screenshot";
        // Every label the final "cooling link" line needs. Each is recorded PASS or FAIL: <reason>. A label that is never recorded blocks the link.
        private static readonly string[] CoolingLabels =
        {
            "cooling column defined",
            "cooling column costs",
            "cooling column holds roof",
            "cooling columns paired",
            "column one cell away stays unpaired",
            "column out of range stays unpaired",
            "coolant pipe joins the column",
            "column three cells away stays off the pipe net",
            "column moved to another level and back stays paired",
            "partner highlight marks the cell",
            "partner highlight shows the pair",
            "lower turbine shares the surface tower's loop",
            "surface tower stays in the lower loop",
            "cooling link breaks when a column is removed",
            "cooling link breaks when a column is deconstructed",
            "cooling link survives reload",
            "rimatomics bridge active",
            "reactor runs on the lower level",
            "loop ratio at or below 1",
            "no cooling alert",
            "no meltdown",
            "surface colonist not irradiated",
        };
        // The labels the pairing check records. A failure in the pairing setup records FAIL on each of these that is not yet recorded.
        private static readonly string[] CoolingPairingLabels =
        {
            "cooling columns paired",
            "column one cell away stays unpaired",
            "column out of range stays unpaired",
            "coolant pipe joins the column",
            "column three cells away stays off the pipe net",
            "column moved to another level and back stays paired",
        };

        // The partner highlight labels. RecordPartnerHighlight is the only writer, so each one gets one row. CoolingLabels makes cooling link wait for them.
        private const string PartnerMarksLabel = "partner highlight marks the cell";
        private const string PartnerPairsLabel = "partner highlight shows the pair";
        private static readonly string[] CoolingHighlightLabels = { PartnerMarksLabel, PartnerPairsLabel };
        // The two screenshot rows of the partner look. The runner's cooling list names them, so the gate requires them in a -Visual run.
        private const string PartnerAmberLabel = "partner highlight amber screenshot";
        private const string PartnerGreenLabel = "partner highlight green screenshot";
        // The calls row. The look writes it in a -Visual run, after the view has settled on the lower level and before the amber shot. It counts the draw
        // calls that the frame issued, with the cell, side, and colour of each. It reads no pixels, so it does not show what the screen displays.
        private const string PartnerDrawnLabel = "partner highlight calls";
        // The same row at the green stage, after the lower column stands. Its planes are compared with the planes that the scenario expects then.
        private const string PartnerDrawnPairedLabel = "partner highlight calls with pair";
        private const string PartnerAmberName = "zlevels-partner-amber";
        private const string PartnerGreenName = "zlevels-partner-green";
        // The highlight cell, offset from the surface centre. It is 4 cells north of the pairing cell and clear of the pairing layout and its pipe.
        private static readonly IntVec3 PartnerOffset = new IntVec3(-12, 0, 4);
        // Every cooling column the scenario spawns, with the map and cell it was placed on. The outline row is built from these records, so a column that goes
        // missing fails the row and names its cell, instead of dropping out of the expectation.
        private static readonly List<ColumnPlacement> scenarioPlacements = new List<ColumnPlacement>();

        // One placed cooling column. It is usable only while it stands on the map and cell where the scenario placed it.
        private sealed class ColumnPlacement
        {
            public readonly Thing Column;
            public readonly Map PlacedOn;
            public readonly IntVec3 Cell;

            public ColumnPlacement(Thing column, Map placedOn, IntVec3 cell)
            {
                Column = column;
                PlacedOn = placedOn;
                Cell = cell;
            }

            public bool Usable
            {
                get { return Column != null && Column.Spawned && Column.Map == PlacedOn && Column.Position == Cell; }
            }
        }

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
                    throw new InvalidOperationException("Timed out at stage " + stage + ", pawn job=" + pawn?.CurJob + ", pawn at " + pawn?.Position + " on " + pawn?.Map + ", " + DescribeWood() + ", " + DescribeCampfire() + ", " + DescribeLookUpMaps(pawn) + ", trail: " + DescribeCampfireTrail() + ", rebuild: " + DescribeRebuild() + ((AllColonistsHaul || OtherColonistsWork) ? ", others: " + DescribeOthers() : ""));
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
                if (GenCommandLine.CommandLineArgPassed("zlevelsvaultrock"))
                {
                    RunVaultRockTick();
                    return;
                }
                if (GenCommandLine.CommandLineArgPassed("zlevelsvaultfull"))
                {
                    finished = true;
                    RunVaultFullCheck();
                    return;
                }
                if (GenCommandLine.CommandLineArgPassed("zlevelssupply"))
                {
                    RunSupplyTick();
                    return;
                }
                if (GenCommandLine.CommandLineArgPassed("zlevelsunreachablezone"))
                {
                    RunUnreachableZoneTick();
                    return;
                }
                if (GenCommandLine.CommandLineArgPassed("zlevelsmultibuild"))
                {
                    RunMultiBuildTick();
                    return;
                }
                if (GenCommandLine.CommandLineArgPassed("zlevelsstairsdamage"))
                {
                    RunStairsDamageTick();
                    return;
                }
                if (GenCommandLine.CommandLineArgPassed("zlevelscoolingtwonets"))
                {
                    // RunTwoNetsTick sets finished when its rows are written.
                    RunTwoNetsTick();
                    return;
                }
                if (GenCommandLine.CommandLineArgPassed("zlevelscooling") || GenCommandLine.CommandLineArgPassed("zlevelscoolingnocolumns"))
                {
                    // RunCoolingLinkTick sets finished when it is done. The look check runs over several frames.
                    RunCoolingLinkTick();
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
                if (pendingShotRow != null && DateTime.UtcNow >= pendingShotDue)
                {
                    Record(pendingShotRow, ScreenshotResult(pendingShotName));
                    pendingShotRow = null;
                }
                switch (stage)
                {
                    case 0:
                        surface = Find.CurrentMap;
                        pawn = FindValidationColonist(surface);
                        RestNeeds(pawn);
                        // Test only: with -zlevelsothercolonistswork or -zlevelsallcolonistshaul the other colonists stay off work until the unforced haul, so none wanders below early.
                        if (AllColonistsHaul || OtherColonistsWork) foreach (Pawn other in surface.mapPawns.FreeColonistsSpawned) if (other != pawn) HoldWork(other);
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
                        QueueShotRow("lower level screenshot", "zlevels-lower-level");
                        CameraJumper.TryJump(powerCell, upper, CameraJumper.MovementMode.Cut);
                        shotDue = DateTime.UtcNow.AddSeconds(3);
                        Next(13);
                        break;
                    case 13:
                        if (DateTime.UtcNow < shotDue) return;
                        // The upper level is void except where it is built; the surface transmitter below should show through it.
                        Require(powerCell.GetTerrain(upper) == ZLevelsDefOf.ZL_OutsideTerrain, "upper cell over transmitter is void");
                        ScreenshotTaker.TakeNonSteamShot("zlevels-upper-void");
                        QueueShotRow("upper void screenshot", "zlevels-upper-void");
                        CameraJumper.TryJump(surfaceTransmitter.parent.Position, surface, CameraJumper.MovementMode.Cut);
                        shotDue = DateTime.UtcNow.AddSeconds(3);
                        Next(11);
                        break;
                    case 11:
                        if (DateTime.UtcNow < shotDue) return;
                        ScreenshotTaker.TakeNonSteamShot("zlevels-surface");
                        QueueShotRow("surface screenshot", "zlevels-surface");
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
                        QueueShotRow("surface hole screenshot", "zlevels-surface-hole");
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
                        if ((AllColonistsHaul || FreeColonists || OtherColonistsWork) && lower.mapPawns.FreeColonistsSpawned.Count > 0) wentDownForWood = true;
                        if (AllColonistsHaul || FreeColonists || OtherColonistsWork)
                        {
                            foreach (Pawn below in lower.mapPawns.FreeColonistsSpawned) belowDuringWait.Add(below.LabelShort);
                        }
                        if (AllColonistsHaul || FreeColonists || OtherColonistsWork) RecordHaulers();
                        if (!UnforcedWoodInZone()) return;
                        Require(wentDownForWood, "colonist went down to the wood");
                        Record("unforced cross-level haul", "PASS");
                        if (AllColonistsHaul || FreeColonists || OtherColonistsWork) Record("hauled by", string.Join(", ", haulersSeen));
                        if (OtherColonistsWork) Require(haulersSeen.Any(name => name != pawn.LabelShort), "a colonist other than the validation colonist carried the logs");
                        if (AllColonistsHaul)
                        {
                            finished = true;
                            Record("colonists below during the haul", string.Join(", ", belowDuringWait));
                            if (OtherColonistsHaul) Require(haulersSeen.Any(name => name != pawn.LabelShort), "a colonist other than the validation colonist carried the logs");
                            else Require(haulersSeen.Count > 0, "a colonist carried the logs");
                            if (OtherColonistsHaul)
                            {
                                Record("other colonist hauls across levels", "PASS");
                            }
                            else
                            {
                                Record("all colonists haul", "PASS");
                            }
                            if (FreeColonists) Record("colonist work recorded", "FAIL: not checked, because the haul run ends before the mining and building checks");
                            if (OtherColonistsWork) Record("other colonists work recorded", "FAIL: not checked, because the haul run ends before the mining and building checks");
                            Root.Shutdown();
                            break;
                        }
                        DesignateMining(lower);
                        deadline = DateTime.UtcNow.AddMinutes(4);
                        Next(21);
                        break;
                    case 21:
                        // Nothing assigns this job. The colonist has to go down and mine the wall by itself.
                        RestNeeds(pawn);
                        RecordCampfireTrail();
                        if (FreeColonists || OtherColonistsWork) RecordMiners();
                        if (pawn.Map == lower || ((FreeColonists || OtherColonistsWork) && lower.mapPawns.FreeColonistsSpawned.Count > 0)) wentDownForWall = true;
                        if (minedWall == null || !minedWall.Destroyed) return;
                        Require(wentDownForWall, "colonist went down to the wall");
                        Record("mines below by itself", "PASS");
                        if (FreeColonists || OtherColonistsWork) Record("mined by", string.Join(", ", minersSeen));
                        if (OtherColonistsWork) Require(minersSeen.Any(name => name != pawn.LabelShort), "a colonist other than the validation colonist mined");
                        if (FreeColonists) Require(minersSeen.Count > 0, "a colonist mined the wall");
                        ReleaseForbiddenWood();
                        PlaceCampfire(lower);
                        deadline = DateTime.UtcNow.AddMinutes(4);
                        Next(22);
                        break;
                    case 22:
                        // Nothing assigns this job. The colonist has to haul the logs to the blueprint and build it by itself.
                        RestNeeds(pawn);
                        RecordCampfireTrail();
                        if (FreeColonists || OtherColonistsWork) RecordBuilders();
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
                        if (FreeColonists || OtherColonistsWork) Record("built by", string.Join(", ", buildersSeen));
                        if (OtherColonistsWork) Require(buildersSeen.Any(name => name != pawn.LabelShort), "a colonist other than the validation colonist built");
                        if (FreeColonists) Require(buildersSeen.Count > 0, "a colonist built the campfire");
                        if (FreeColonists) Record("colonist work recorded", "PASS");
                        if (OtherColonistsWork) Record("other colonists work recorded", "PASS");
                        if (pendingShotRow != null) { Record(pendingShotRow, ScreenshotResult(pendingShotName)); pendingShotRow = null; }
                        RecordCombatContainment();
                        finished = true;
                        Record("core validation", "PASS");
                        Root.Shutdown();
                        break;
                }
            }
            catch (Exception exception)
            {
                finished = true;
                Log.Error("Z-Levels runtime validation failed: " + exception);
                Root.Shutdown();
                Record("stage " + stage, "FAIL: " + exception);
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
                // The game refuses placement in fog, and the cell's fog depends on where the colonists happen to be. The player would see this cell.
                surface.fogGrid.Unfog(surfaceCell);
                AcceptanceReport wallReport = GenConstruct.CanPlaceBlueprintAt(wall, surfaceCell, Rot4.North, surface);
                Require(wallReport.Accepted, "a plain wall is accepted on the surface cell: " + wallReport.Reason + "; pawns there: " + string.Join(", ", surfaceCell.GetThingList(surface).OfType<Pawn>().Select(p => p.LabelShort)));
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
                SpawnFood(pocket, pocket.Center + new IntVec3(2, 0, 0));
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

        // Vault rock. The pocket is solid rock with ore in it, except a room at its centre, and the exit stands in that room. The checks run
        // when the entrance makes the pocket, and each one gets its own row. A colonist then walks in and mines a wall beside the room.
        private const float VaultRoomRadius = VaultExitSpot.RoomRadius;

        private void RunVaultRockTick()
        {
            try
            {
                if (vaultStage == 0)
                {
                    deadline = DateTime.MaxValue;
                    vaultDeadline = DateTime.UtcNow.AddMinutes(8);
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
                    vaultPocket = vaultEntrance.GetOtherMap();
                    IntVec3 center = vaultPocket.Center;
                    IntVec3 exit = vaultEntrance.exit.Position;
                    int openOutsideRoom = 0;
                    int roomBlocked = 0;
                    foreach (IntVec3 cell in vaultPocket.AllCells)
                    {
                        if (cell.DistanceTo(center) <= VaultRoomRadius)
                        {
                            if (!cell.Standable(vaultPocket)) roomBlocked++;
                        }
                        else if (!(cell.GetEdifice(vaultPocket) is Mineable))
                        {
                            openOutsideRoom++;
                        }
                    }
                    bool ore = vaultPocket.listerThings.AllThings.OfType<Mineable>().Any(m => m.def.building.isResourceRock);
                    // The pocket's rock types must be the surface's natural rock types, the list the underground draws from.
                    List<ThingDef> pocketRock = vaultPocket.AllCells.Where(c => c.DistanceTo(center) > VaultRoomRadius)
                        .Select(c => c.GetEdifice(vaultPocket)).OfType<Mineable>().Where(m => !m.def.building.isResourceRock)
                        .Select(m => m.def).Distinct().ToList();
                    List<ThingDef> surfaceRock = Find.World.NaturalRockTypesIn(surface.Tile).ToList();
                    bool surfaceRocks = pocketRock.Count > 0 && pocketRock.All(d => surfaceRock.Contains(d));
                    bool atCentre = exit == center;
                    Record("vault rock: outside the room every cell is rock", openOutsideRoom == 0 ? "PASS" : "FAIL: " + openOutsideRoom + " cells outside the room hold no rock");
                    Record("vault rock: every room cell can be stood on", roomBlocked == 0 ? "PASS" : "FAIL: " + roomBlocked + " room cells cannot be stood on");
                    Record("vault rock: exit in the middle", atCentre ? "PASS" : "FAIL: exit at " + exit + ", centre " + center);
                    Record("vault rock: ore in the rock", ore ? "PASS" : "FAIL: no ore wall in the pocket");
                    Record("vault rock: rock types are the surface's natural rock", surfaceRocks ? "PASS" : "FAIL: pocket rock " + string.Join(", ", pocketRock.Select(d => d.defName).ToArray()) + ", surface rock " + string.Join(", ", surfaceRock.Select(d => d.defName).ToArray()));
                    if (openOutsideRoom > 0 || roomBlocked > 0 || !atCentre || !ore || !surfaceRocks)
                    {
                        throw new InvalidOperationException("the vault pocket is not underground rock with the exit in the middle");
                    }
                    vaultMineCell = FindVaultMineCell(vaultPocket, center);
                    vaultPocket.designationManager.AddDesignation(new Designation(vaultMineCell, DesignationDefOf.Mine));
                    Record("vault rock: wall beside the room marked to mine", "PASS");
                    vaultColonist.jobs.StartJob(JobMaker.MakeJob(JobDefOf.EnterPortal, vaultEntrance), JobCondition.InterruptForced);
                    vaultStage = 1;
                    return;
                }
                if (DateTime.UtcNow > vaultDeadline)
                {
                    throw new InvalidOperationException("Timed out at vault rock stage " + vaultStage + ", colonist job " + vaultColonist?.CurJob?.def?.defName + ", colonist on the pocket " + (vaultColonist?.Map == vaultPocket) + ", wall still rock " + (vaultMineCell.GetEdifice(vaultPocket) is Mineable));
                }
                if (vaultStage == 1)
                {
                    if (vaultColonist.Map != vaultPocket)
                    {
                        return;
                    }
                    Record("colonist enters the pocket", "PASS");
                    Mineable wall = vaultMineCell.GetEdifice(vaultPocket) as Mineable;
                    Require(wall != null, "the wall beside the room is not rock");
                    vaultColonist.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Mine, wall), JobCondition.InterruptForced);
                    vaultStage = 2;
                    return;
                }
                if (vaultStage == 2)
                {
                    if (vaultMineCell.GetEdifice(vaultPocket) is Mineable)
                    {
                        return;
                    }
                    Require(vaultMineCell.Walkable(vaultPocket), "the mined cell is not open floor");
                    Record("colonist mines a wall beside the room", "PASS");
                    finished = true;
                    Record("vault rock", "PASS");
                    Root.Shutdown();
                }
            }
            catch (Exception exception)
            {
                finished = true;
                Record("vault rock", "FAIL: " + exception);
                Log.Error("Z-Levels vault rock validation failed: " + exception);
                Root.Shutdown();
            }
        }

        // A rock wall beside the room, with a room cell on one of its sides for the colonist to mine from.
        private static IntVec3 FindVaultMineCell(Map pocket, IntVec3 center)
        {
            IntVec3[] sides = { new IntVec3(1, 0, 0), new IntVec3(-1, 0, 0), new IntVec3(0, 0, 1), new IntVec3(0, 0, -1) };
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, VaultRoomRadius + 1.5f, useCenter: false))
            {
                if (!cell.InBounds(pocket) || cell.DistanceTo(center) <= VaultRoomRadius || !(cell.GetEdifice(pocket) is Mineable))
                {
                    continue;
                }
                foreach (IntVec3 side in sides)
                {
                    IntVec3 beside = cell + side;
                    if (beside.InBounds(pocket) && beside.DistanceTo(center) <= VaultRoomRadius && beside.Standable(pocket))
                    {
                        return cell;
                    }
                }
            }
            throw new InvalidOperationException("No rock wall stands beside the vault room.");
        }

        // Vault full. Entrances go on the surface one after another, and each one makes its exit in the shared pocket. When the pocket has
        // no room for another exit, the placement check refuses the next entrance, and no exit is attempted for it.
        private void RunVaultFullCheck()
        {
            try
            {
                deadline = DateTime.UtcNow.AddMinutes(10);
                var entranceDef = DefDatabase<ThingDef>.GetNamed("ZL_VaultEntrance");
                Map surface = Find.CurrentMap;
                var entrances = new List<MapPortal>();
                string refusal = null;
                for (int x = surface.Center.x - 60; x <= surface.Center.x + 60; x += 2)
                {
                    IntVec3 cell = new IntVec3(x, 0, surface.Center.z + 12);
                    // A thing the game will not destroy, such as a geyser, takes this cell. Skip it rather than clear it.
                    if (cell.GetThingList(surface).Any(t => !t.def.destroyable))
                    {
                        continue;
                    }
                    ClearCell(surface, cell);
                    surface.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete);
                    // The game refuses placement in fog, and the player would see this cell.
                    surface.fogGrid.Unfog(cell);
                    AcceptanceReport report = GenConstruct.CanPlaceBlueprintAt(entranceDef, cell, Rot4.North, surface);
                    if (!report.Accepted)
                    {
                        if (report.Reason.Contains("no room"))
                        {
                            refusal = report.Reason;
                            break;
                        }
                        continue;
                    }
                    MapPortal entrance = (MapPortal)GenSpawn.Spawn(ThingMaker.MakeThing(entranceDef), cell, surface);
                    entrances.Add(entrance);
                    entrance.GetOtherMap();
                }
                Require(entrances.Count >= 20, "the pocket took only " + entrances.Count + " entrances before it refused one");
                Require(refusal != null, "the next entrance was not refused by the room");
                Map pocket = entrances[0].GetOtherMap();
                int exits = entrances.Count(e => e.exit != null && e.exit.Map == pocket);
                Require(exits == entrances.Count, "each entrance has an exit in the pocket: " + exits + " of " + entrances.Count);
                Record("vault full: entrances placed, each with an exit", entrances.Count.ToString());
                Record("vault full: the next entrance is refused", refusal);
                finished = true;
                Record("vault full", "PASS");
                Root.Shutdown();
            }
            catch (Exception exception)
            {
                finished = true;
                Record("vault full", "FAIL: " + exception);
                Log.Error("Z-Levels vault full validation failed: " + exception);
                Root.Shutdown();
            }
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
            SpawnFood(map, map.Center);
        }

        // A meal on the given cell. The vault exit stands at the pocket's centre, so the vault test puts its meal beside it.
        private static void SpawnFood(Map map, IntVec3 spot)
        {
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
            var spawned = (ThingWithComps)GenSpawn.Spawn(building, cell, map);
            if (spawned == null) throw new InvalidOperationException("GenSpawn.Spawn returned no thing for " + definition.defName + " at " + cell);
            if (definition.defName == "ZL_CoolingColumn") scenarioPlacements.Add(new ColumnPlacement(spawned, map, cell));
            return spawned;
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
            if (OtherColonistsWork) pawn.workSettings.SetPriority(WorkTypeDefOf.Construction, 0);
            foreach (var colonistMap in new[] { surface, lower, upper })
            {
                if (colonistMap == null) continue;
                foreach (var colonist in colonistMap.mapPawns.FreeColonistsSpawned)
                {
                    if (FreeColonists || OtherColonistsWork || colonist == pawn || colonist.WorkTypeIsDisabled(WorkTypeDefOf.Construction)) continue;
                    colonist.workSettings.SetPriority(WorkTypeDefOf.Construction, 0);
                }
            }
        }

        // Test only: every colonist except the validation colonist, on any level.
        private IEnumerable<Pawn> OtherColonists()
        {
            foreach (Map map in new[] { surface, lower, upper })
            {
                if (map == null) continue;
                foreach (Pawn colonist in map.mapPawns.FreeColonistsSpawned)
                {
                    if (colonist != pawn) yield return colonist;
                }
            }
        }

        // Test only: a haul can start and end inside one frame, so the unreachable-zone check counts haul starts on every game tick.
        public override void GameComponentTick()
        {
            base.GameComponentTick();
            if (unreachableStage != 1 || surface == null) return;
            foreach (Pawn colonist in surface.mapPawns.FreeColonistsSpawned)
            {
                unreachableLastJobs.TryGetValue(colonist, out Job last);
                Job job = colonist.CurJob;
                if (job == null || job == last) continue;
                unreachableLastJobs[colonist] = job;
                bool towardsWalled = HaulsToCell(colonist, job, unreachableCell);
                if (!towardsWalled && !HaulsToCell(colonist, job, unreachableFarCell)) continue;
                if (towardsWalled) unreachableStarts++;
                else unreachableFarStarts++;
                Log.Message((towardsWalled ? "unreachable zone haul start by " : "reachable zone haul start by ") + colonist + ": " + job);
            }
        }

        // Test only: a colonist must not take hauls to a stockpile cell it cannot reach. Only the validation colonist may haul. Seven logs lie outside the walls, and a reachable second stockpile cell lies farther away and must get the haul.
        private void RunUnreachableZoneTick()
        {
            try
            {
                if (unreachableStage == 0)
                {
                    Map map = Find.CurrentMap;
                    surface = map;
                    pawn = FindValidationColonist(map);
                    RestNeeds(pawn);
                    foreach (Pawn other in map.mapPawns.FreeColonistsSpawned)
                    {
                        if (other != pawn) HoldWork(other);
                    }
                    if (!FindWalledZone(map, pawn.Position, out IntVec3 center, out IntVec3 logCell)) throw new InvalidOperationException("No open 3 by 3 block with a reachable cell beside it.");
                    if (!FindFarCell(map, pawn.Position, logCell, center, out IntVec3 farCell)) throw new InvalidOperationException("No reachable cell far from the logs for a second stockpile cell.");
                    ThingDef wallDef = DefDatabase<ThingDef>.GetNamed("Wall");
                    foreach (IntVec3 neighbour in GenRadial.RadialCellsAround(center, 1.5f, false))
                    {
                        GenSpawn.Spawn(ThingMaker.MakeThing(wallDef, GenStuff.DefaultStuffFor(wallDef)), neighbour, map);
                    }
                    unreachableCell = center;
                    unreachableFarCell = farCell;
                    Zone_Stockpile zone = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
                    map.zoneManager.RegisterZone(zone);
                    zone.AddCell(center);
                    zone.AddCell(farCell);
                    Thing logs = ThingMaker.MakeThing(ThingDefOf.WoodLog);
                    logs.stackCount = 7;
                    GenSpawn.Spawn(logs, logCell, map);
                    logs.SetForbidden(false);
                    unreachableEnd = DateTime.UtcNow.AddSeconds(60);
                    unreachableStage = 1;
                    return;
                }
                if (DateTime.UtcNow < unreachableEnd) return;
                finished = true;
                Record("unreachable zone haul starts", unreachableStarts.ToString());
                Record("reachable zone haul starts", unreachableFarStarts.ToString());
                Record("unreachable zone", unreachableStarts > 0 ? "FAIL: " + unreachableStarts + " haul job(s) headed for the walled cell in 60 seconds" : unreachableFarStarts == 0 ? "FAIL: no haul job headed for the reachable cell in 60 seconds" : "PASS");
                Root.Shutdown();
            }
            catch (Exception exception)
            {
                finished = true;
                Record("unreachable zone", "FAIL: " + exception);
                Log.Error("Z-Levels unreachable zone validation failed: " + exception);
                Root.Shutdown();
            }
        }

        // Test only: true for a haul job whose destination is the cell, before or after the cross-level conversion.
        private bool HaulsToCell(Pawn colonist, Job job, IntVec3 cell)
        {
            if (job.def == JobDefOf.HaulToCell) return job.targetB.Cell == cell;
            return job.def == ZLevelsDefOf.ZL_HaulThingToDest && Current.Game.GetComponent<ZLevelsManager>().jobTracker.TryGetValue(colonist, out JobTracker tracker) && tracker.targetDest.Cell == cell;
        }

        // Test only: an open 3 by 3 block at least five cells from the colonist, and a free cell three cells below its centre that the colonist can reach.
        private static bool FindWalledZone(Map map, IntVec3 near, out IntVec3 center, out IntVec3 logCell)
        {
            foreach (IntVec3 candidate in GenRadial.RadialCellsAround(near, 12f, false))
            {
                center = candidate;
                logCell = candidate + new IntVec3(0, 0, -3);
                if (candidate.DistanceTo(near) < 5f || !logCell.InBounds(map) || !logCell.Standable(map) || logCell.GetEdifice(map) != null || logCell.GetFirstItem(map) != null) continue;
                if (!map.reachability.CanReach(near, logCell, PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassDoors))) continue;
                bool open = true;
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        IntVec3 cell = candidate + new IntVec3(dx, 0, dz);
                        if (!cell.InBounds(map) || !cell.Standable(map) || cell.GetEdifice(map) != null || cell.GetFirstItem(map) != null) open = false;
                    }
                }
                if (open) return true;
            }
            center = IntVec3.Invalid;
            logCell = IntVec3.Invalid;
            return false;
        }

        // Test only: a free, reachable cell at least 4.5 cells from the logs and farther from the colonist than the walled cell, so the walled cell is the closer choice before the fix.
        private static bool FindFarCell(Map map, IntVec3 near, IntVec3 logCell, IntVec3 center, out IntVec3 farCell)
        {
            foreach (IntVec3 candidate in GenRadial.RadialCellsAround(logCell, 9f, false))
            {
                if (candidate.DistanceTo(logCell) < 4.5f || candidate.DistanceTo(center) <= 2f || candidate.DistanceTo(near) <= center.DistanceTo(near)) continue;
                if (!candidate.InBounds(map) || !candidate.Standable(map) || candidate.GetEdifice(map) != null || candidate.GetFirstItem(map) != null) continue;
                if (!map.reachability.CanReach(near, candidate, PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassDoors))) continue;
                if (!map.reachability.CanReach(logCell, candidate, PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassDoors))) continue;
                farCell = candidate;
                return true;
            }
            farCell = IntVec3.Invalid;
            return false;
        }

        // Test only: three wall sites below. By default the logs lie on the surface in four piles, so the walls need cross-level hauls. The test passes when
        // every site is built and at least as many logs as the walls use (five each) have left the surface. -zlevelsmultibuildlowerlogs is the other variant: the logs lie below, surface hauling
        // is stopped, and the test passes only if no surface item is carried down.
        private static bool MultiBuildSurfaceLogs => !GenCommandLine.CommandLineArgPassed("zlevelsmultibuildlowerlogs");

        private void RunMultiBuildTick()
        {
            try
            {
                if (multiBuildStage == 0)
                {
                    surface = Find.CurrentMap;
                    pawn = FindValidationColonist(surface);
                    RestNeeds(pawn);
                    bool surfaceVariant = MultiBuildSurfaceLogs;
                    if (!surfaceVariant)
                    {
                        foreach (Pawn colonist in surface.mapPawns.FreeColonistsSpawned) colonist.workSettings.SetPriority(WorkTypeDefOf.Hauling, 0);
                    }
                    var down = SpawnStair<Building_StairsDown>(ZLevelsDefOf.ZL_StairsDown, pawn.Position + new IntVec3(3, 0, 0));
                    if (surfaceVariant)
                    {
                        // Other logs on the surface are removed, so only the piles below can feed the walls, and the count of logs that leave the surface is exact.
                        foreach (Thing log in surface.listerThings.ThingsOfDef(ThingDefOf.WoodLog).ToList()) log.Destroy(DestroyMode.Vanish);
                        // Four piles of five, so the walls need several cross-level hauls.
                        if (!FindOpenCells(surface, pawn.Position, 4, out List<IntVec3> pileCells)) throw new InvalidOperationException("No open cells near the colonist for the surface log piles.");
                        foreach (IntVec3 pileCell in pileCells)
                        {
                            var pile = ThingMaker.MakeThing(ThingDefOf.WoodLog);
                            pile.stackCount = 5;
                            GenSpawn.Spawn(pile, pileCell, surface);
                            pile.SetForbidden(false);
                            multiBuildInitialLogs += pile.stackCount;
                        }
                    }
                    else
                    {
                        // After the stairs are placed, because placing them clears their cell. The surface then has no items to carry down.
                        foreach (Thing thing in surface.listerThings.AllThings.ToList())
                        {
                            if (thing.def.EverHaulable && !(thing is Pawn))
                            {
                                thing.Destroy(DestroyMode.Vanish);
                                multiBuildRemovedItems++;
                            }
                        }
                    }
                    deadline = DateTime.UtcNow.AddMinutes(8);
                    multiBuildStage = 1;
                    down.GiveJob(pawn, down);
                    return;
                }
                if (multiBuildStage == 1)
                {
                    RestNeeds(pawn);
                    if (pawn.Map == surface) return;
                    lower = pawn.Map;
                    Require(Current.Game.GetComponent<ZLevelsManager>().GetZIndexFor(lower) == -1, "lower index");
                    OpenLowerRoom(lower);
                    IntVec3 stairs = lower.listerThings.AllThings.OfType<Building_StairsUp>().First().Position;
                    UnfogAround(lower, stairs, 20f);
                    if (!FindBuildSites(lower, stairs, MultiBuildSiteCount, out List<IntVec3> sites, out IntVec3 logCell)) throw new InvalidOperationException("No open sites below the stairs for the walls and the logs.");
                    multiBuildSites = sites;
                    ThingDef wallDef = DefDatabase<ThingDef>.GetNamed("Wall");
                    foreach (IntVec3 site in sites) GenConstruct.PlaceBlueprintForBuild(wallDef, site, lower, Rot4.North, Faction.OfPlayer, ThingDefOf.WoodLog);
                    if (!MultiBuildSurfaceLogs)
                    {
                        var logs = ThingMaker.MakeThing(ThingDefOf.WoodLog);
                        logs.stackCount = 20;
                        GenSpawn.Spawn(logs, logCell, lower);
                        logs.SetForbidden(false);
                    }
                    Record("multi-build sites", string.Join(", ", sites));
                    multiBuildStage = 2;
                    return;
                }
                RestNeeds(pawn);
                // Items that appear on the surface after the start are removed and counted, so none can be carried down.
                if (!MultiBuildSurfaceLogs)
                {
                    foreach (Thing thing in surface.listerThings.AllThings.ToList())
                    {
                        if (!thing.def.EverHaulable || thing is Pawn) continue;
                        thing.Destroy(DestroyMode.Vanish);
                        multiBuildLateItems++;
                    }
                }
                foreach (Map map in new[] { surface, lower })
                {
                    foreach (Pawn colonist in map.mapPawns.FreeColonistsSpawned)
                    {
                        Job job = colonist.CurJob;
                        multiBuildLastJobs.TryGetValue(colonist, out Job last);
                        if (job == null || job == last) continue;
                        multiBuildLastJobs[colonist] = job;
                        if (job.def == ZLevelsDefOf.ZL_HaulThingToDest)
                        {
                            // The job walks the colonist down the stairs. The item only changes level if it starts on the surface.
                            Thing carried = job.targetA.Thing;
                            if (carried != null && carried.Map == surface)
                            {
                                multiBuildCrossHauls++;
                                Log.Message("multi-build cross-level haul by " + colonist + ": " + job + "; item at " + carried.Position + " on " + carried.Map + ", stack " + carried.stackCount + ", id " + carried.thingIDNumber);
                            }
                            else
                            {
                                multiBuildLowerWalks++;
                            }
                        }
                        if (colonist.Map == lower && job.def == JobDefOf.FinishFrame) multiBuildBuilders.Add(colonist.LabelShort);
                    }
                }
                ThingDef builtDef = DefDatabase<ThingDef>.GetNamed("Wall");
                int built = multiBuildSites.Count(site => site.GetEdifice(lower)?.def == builtDef);
                if (built < multiBuildSites.Count) return;
                finished = true;
                Record("multi-build walls built", built + " of " + multiBuildSites.Count);
                Record("cross-level hauls during the builds", multiBuildCrossHauls + " surface items carried down; " + multiBuildLowerWalks + " walks to items below");
                Record("builders below", string.Join(", ", multiBuildBuilders));
                Record("surface items removed", (multiBuildRemovedItems + multiBuildLateItems) + " (" + multiBuildLateItems + " appeared after the start)");
                // The monitor sees only ZL haul jobs. The logs that left the surface are counted from the map, since the walls can be fed another way.
                int logsOnSurface = surface.listerThings.ThingsOfDef(ThingDefOf.WoodLog).Sum(log => log.stackCount);
                int logsBelow = lower.listerThings.ThingsOfDef(ThingDefOf.WoodLog).Sum(log => log.stackCount);
                int logsOffSurface = multiBuildInitialLogs - logsOnSurface;
                Record("logs left on the surface, below", logsOnSurface + ", " + logsBelow + " (" + logsOffSurface + " left the surface)");
                bool passes = MultiBuildSurfaceLogs ? logsOffSurface >= MultiBuildSiteCount * 5 : multiBuildCrossHauls == 0;
                Record("multiple builds below", passes ? "PASS" : MultiBuildSurfaceLogs ? "FAIL: only " + logsOffSurface + " logs left the surface" : "FAIL: " + multiBuildCrossHauls + " surface item(s) carried down");
                Root.Shutdown();
            }
            catch (Exception exception)
            {
                finished = true;
                Record("multiple builds below", "FAIL: " + exception);
                Log.Error("Z-Levels multiple builds validation failed: " + exception);
                Root.Shutdown();
            }
        }

        // Test only: wall sites two to three and a half cells from the stairs and two apart, all reachable from them, with a log cell beside them.
        // The room the stairs open onto is small, so the sites stay inside it.
        private static bool FindBuildSites(Map map, IntVec3 near, int count, out List<IntVec3> sites, out IntVec3 logCell)
        {
            sites = new List<IntVec3>();
            logCell = IntVec3.Invalid;
            foreach (IntVec3 candidate in GenRadial.RadialCellsAround(near, 3.5f, false))
            {
                if (candidate.DistanceTo(near) < 2f || !IsOpenCell(map, candidate)) continue;
                if (sites.Any(site => site.DistanceTo(candidate) < 2f)) continue;
                if (!map.reachability.CanReach(near, candidate, PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassDoors))) continue;
                sites.Add(candidate);
                if (sites.Count == count) break;
            }
            if (sites.Count < count) return false;
            foreach (IntVec3 candidate in GenRadial.RadialCellsAround(near, 3.5f, false))
            {
                if (candidate.DistanceTo(near) < 2f || !IsOpenCell(map, candidate) || sites.Any(site => site.DistanceTo(candidate) < 2f)) continue;
                if (!map.reachability.CanReach(near, candidate, PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassDoors))) continue;
                logCell = candidate;
                return true;
            }
            return false;
        }

        // Test only: open cells two to six cells from the colonist, at least two apart, that it can reach. Used for the surface log piles.
        private static bool FindOpenCells(Map map, IntVec3 near, int count, out List<IntVec3> cells)
        {
            cells = new List<IntVec3>();
            foreach (IntVec3 candidate in GenRadial.RadialCellsAround(near, 6f, false))
            {
                if (candidate.DistanceTo(near) < 2f || !IsOpenCell(map, candidate)) continue;
                if (cells.Any(cell => cell.DistanceTo(candidate) < 2f)) continue;
                if (!map.reachability.CanReach(near, candidate, PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassDoors))) continue;
                cells.Add(candidate);
                if (cells.Count == count) return true;
            }
            return false;
        }

        private static bool IsOpenCell(Map map, IntVec3 cell)
        {
            return cell.InBounds(map) && cell.Standable(map) && cell.GetEdifice(map) == null && cell.GetFirstItem(map) == null;
        }

        private static void UnfogAround(Map map, IntVec3 center, float radius)
        {
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, radius, false))
            {
                if (cell.InBounds(map)) map.fogGrid.Unfog(cell);
            }
        }

        // Test only: the stairs need a free cell the colonist can reach now. A fixed offset can land behind a wall. Then the stairs job
        // fails, and the colonist wanders off.
        private static IntVec3 FindStairsCellNear(Pawn pawn)
        {
            Map map = pawn.Map;
            var cells = new List<IntVec3> { pawn.Position + new IntVec3(3, 0, 0) };
            cells.AddRange(GenRadial.RadialCellsAround(pawn.Position, 4f, false).Where(c => c.DistanceToSquared(pawn.Position) >= 9));
            foreach (IntVec3 cell in cells)
            {
                if (!cell.InBounds(map) || !cell.Standable(map) || cell.GetEdifice(map) != null) continue;
                if (!map.reachability.CanReach(pawn.Position, cell, PathEndMode.OnCell, TraverseParms.For(pawn))) continue;
                return cell;
            }
            throw new InvalidOperationException("No cell 3 to 4 cells from the colonist can be reached for the stairs on " + map);
        }

        // Test only: damage that destroys the lower staircase must not throw, and must leave the staircase on the surface standing.
        // Building_Stairs.PostApplyDamage looks up the partner after the damage, and a destroyed staircase no longer has a map to look it up from.
        private void RunStairsDamageTick()
        {
            try
            {
                if (stairsDamageStage == 0)
                {
                    surface = Find.CurrentMap;
                    pawn = FindValidationColonist(surface);
                    RestNeeds(pawn);
                    var down = SpawnStair<Building_StairsDown>(ZLevelsDefOf.ZL_StairsDown, FindStairsCellNear(pawn));
                    stairsDamagePartner = down;
                    Record("surface stairs cell", down.Position.ToString());
                    deadline = DateTime.UtcNow.AddMinutes(4);
                    stairsDamageStage = 1;
                    down.GiveJob(pawn, down);
                    return;
                }
                RestNeeds(pawn);
                if (pawn.Map == surface) return;
                lower = pawn.Map;
                Building_StairsUp stairs = lower.listerThings.AllThings.OfType<Building_StairsUp>().First();
                finished = true;
                try
                {
                    stairs.TakeDamage(new DamageInfo(DamageDefOf.Crush, 10000f));
                    bool surfaceStanding = stairsDamagePartner.Spawned;
                    Record("lower stairs destroyed by damage", stairs.Destroyed ? "yes" : "no");
                    Record("surface stairs after the hit", surfaceStanding ? "standing" : "gone");
                    Record("stairs damage", stairs.Destroyed && surfaceStanding ? "PASS" : "FAIL: the lower stairs were not destroyed, or the surface stairs went with them");
                }
                catch (Exception exception)
                {
                    Record("lower stairs after the exception", "spawned " + stairs.Spawned + ", map " + (stairs.Map == null ? "none" : "set"));
                    Record("stairs damage", "FAIL: " + exception);
                }
                Root.Shutdown();
            }
            catch (Exception exception)
            {
                finished = true;
                Record("stairs damage", "FAIL: " + exception);
                Root.Shutdown();
            }
        }

        // Cross-level cooling. The first frame records the column's def checks, the bridge check, and the labels that later tasks still stub.
        // With -Visual (zlevelsscreenshots) it then places a column and a coolant pipe, waits for the view to render, and takes a screenshot.
        // The partner look then views the lower level on the highlight cell and takes two screenshots, amber and then green.
        // The scenario sets finished and shuts the game down after its last record.
        private void RunCoolingLinkTick()
        {
            try
            {
                if (!coolingChecksRecorded)
                {
                    RecordCoolingChecks();
                    coolingChecksRecorded = true;
                    if (GenCommandLine.CommandLineArgPassed("zlevelsscreenshots") && CoolingNoColumns)
                    {
                        RecordCooling(CoolingScreenshotLabel, "FAIL: no columns placed");
                        coolingScreenshotFailed = true;
                    }
                    else if (GenCommandLine.CommandLineArgPassed("zlevelsscreenshots"))
                    {
                        string lookProblem = PlaceCoolingLook();
                        if (lookProblem == null)
                        {
                            coolingShotDue = DateTime.UtcNow.AddSeconds(3);
                            return;
                        }
                        RecordCooling(CoolingScreenshotLabel, "FAIL: " + lookProblem);
                        coolingScreenshotFailed = true;
                    }
                }
                else if (coolingShotDue.HasValue)
                {
                    if (DateTime.UtcNow < coolingShotDue.Value) return;
                    if (!coolingShotTaken)
                    {
                        // RimWorld writes the file after this frame, so the game keeps running for three more seconds before the file check.
                        ScreenshotTaker.TakeNonSteamShot("zlevels-cooling-column");
                        coolingShotTaken = true;
                        coolingShotDue = DateTime.UtcNow.AddSeconds(3);
                        return;
                    }
                    string shot = Path.Combine(GenFilePaths.SaveDataFolderPath, "Screenshots", "zlevels-cooling-column.png");
                    string shotProblem = PngProblem(shot);
                    RecordCooling(CoolingScreenshotLabel, shotProblem == null ? "TAKEN: " + shot : "FAIL: " + shotProblem);
                    if (shotProblem != null) coolingScreenshotFailed = true;
                    coolingShotDue = null;
                }
                // The partner look follows the cooling column look. It holds the link row until its two screenshots are checked.
                if (partnerLookStage != 0 && !AdvancePartnerLook()) return;
                if (!AdvanceLinkStages()) return;
                RecordCoolingLink();
            }
            catch (Exception exception)
            {
                finished = true;
                try
                {
                    // The description comes first. DescribeOrTypeName does not throw, so the link row below is always written.
                    string cause = DescribeOrTypeName(exception);
                    // The link row carries the same text as the owed rows. It comes before them, so a failure in the owed rows cannot skip it.
                    try
                    {
                        Record("cooling link", "FAIL: " + cause);
                    }
                    catch (Exception linkException)
                    {
                        // The link row could not be written. The game still shuts down after this block, and the log names this failure.
                        Log.Error("Z-Levels cooling link row could not be recorded: " + linkException);
                    }
                    // The screenshot row belongs to this tick when screenshots are on, so an exception before or after it still leaves a row.
                    var owed = new List<string>(RequiredCoolingLabels());
                    if (GenCommandLine.CommandLineArgPassed("zlevelsscreenshots")) owed.AddRange(new[] { CoolingScreenshotLabel, PartnerAmberLabel, PartnerGreenLabel, PartnerDrawnLabel, PartnerDrawnPairedLabel });
                    foreach (string label in owed)
                    {
                        // A label whose check never ran says why. It does not read as a failure of its own check.
                        if (!coolingResults.ContainsKey(label)) RecordRowSafely(label, "FAIL: not run: " + cause);
                    }
                    Log.Error("Z-Levels cooling link validation failed: " + exception);
                }
                catch (Exception failureException)
                {
                    // The failure rows could not be built. The game still shuts down after this block, and the log names this failure.
                    Log.Error("Z-Levels cooling link failure rows could not be built: " + failureException);
                }
            }
            finished = true;
            Root.Shutdown();
        }

        // A screenshot counts only when the file starts with the 8-byte PNG signature and ends with the 12-byte IEND chunk. The IEND chunk is a
        // 4-byte zero length, the type IEND, and its CRC. An empty or partial file reads as FAIL.
        private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        private static readonly byte[] PngEnd = { 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82 };

        // Returns null when the file is a complete PNG. Otherwise it names the problem and the path.
        private static string PngProblem(string path)
        {
            if (!File.Exists(path)) return "no screenshot file at " + path;
            using (FileStream stream = File.OpenRead(path))
            {
                if (!BytesAt(stream, 0, PngSignature) || !BytesAt(stream, stream.Length - PngEnd.Length, PngEnd)) return "file is not a complete PNG at " + path;
            }
            return null;
        }

        // The result text for a core screenshot row. It runs the same file check as the cooling screenshot row.
        private static string ScreenshotResult(string name)
        {
            string shot = Path.Combine(GenFilePaths.SaveDataFolderPath, "Screenshots", name + ".png");
            string problem = PngProblem(shot);
            return problem == null ? "TAKEN: " + shot : "FAIL: " + problem;
        }

        // Queues the row for a screenshot that was just taken. The row is written once the file has had time to appear.
        private void QueueShotRow(string row, string name)
        {
            pendingShotRow = row;
            pendingShotName = name;
            pendingShotDue = DateTime.UtcNow.AddSeconds(3);
        }

        // True when the file holds the expected bytes at the offset. A file too short to hold them does not match.
        private static bool BytesAt(FileStream stream, long offset, byte[] expected)
        {
            if (offset < 0 || stream.Length < offset + expected.Length) return false;
            stream.Seek(offset, SeekOrigin.Begin);
            byte[] actual = new byte[expected.Length];
            int total = 0;
            while (total < actual.Length)
            {
                int read = stream.Read(actual, total, actual.Length - total);
                if (read == 0) return false;
                total += read;
            }
            for (int i = 0; i < expected.Length; i++)
            {
                if (actual[i] != expected[i]) return false;
            }
            return true;
        }

        // A label that a later task has not built yet records FAIL with the piece that is still missing at that stage.
        private void RecordCoolingChecks()
        {
            ThingDef column = DefDatabase<ThingDef>.GetNamedSilentFail("ZL_CoolingColumn");
            RecordCooling("cooling column defined", column != null ? "PASS" : "FAIL: column missing");
            RecordCooling("cooling column costs", column != null ? CoolingColumnCostResult(column) : "FAIL: column missing");
            RecordCooling("cooling column holds roof", column != null ? CoolingColumnRoofResult(column) : "FAIL: column missing");
            RecordCoolingPairing(column);
            RecordPartnerHighlight(column);
            // The three link rows are written by AdvanceLinkStages, after the link scene is built and the game is settled, so they are not stubbed here.
            // The reactor rows are written by the reactor stages of AdvanceLinkStages, so they are not stubbed here.
            RecordCooling("rimatomics bridge active", RimatomicsBridgeResult());
        }

        // The pairing and pipe labels. The scenario generates the lower level, then places a surface column, a lower column under it,
        // a second surface column one cell away diagonally (east and south), a third 3 cells east, and a coolant pipe north of the first surface column.
        // The diagonal cell touches neither the first column nor the pipe. A column that touched the first column would share its pipe net, and the loop
        // rule joins a whole net, so the one-cell row would test loop membership and not pairing.
        private void RecordCoolingPairing(ThingDef column)
        {
            ThingDef pipeDef = DefDatabase<ThingDef>.GetNamedSilentFail("coolingPipe");
            if (CoolingNoColumns)
            {
                // The no-columns variant builds the same two levels and the same pairing cell, and it places no column on them. The link scene and the
                // reactor checks then run as they do with the pair.
                try
                {
                    surface = Find.CurrentMap;
                    ZLevelsManager noColumnsManager = Current.Game?.GetComponent<ZLevelsManager>();
                    if (noColumnsManager == null) throw new InvalidOperationException("ZLevelsManager is missing from the current game");
                    lower = CreateLevelBelow(noColumnsManager, surface);
                    coolingPairCell = surface.Center + new IntVec3(-12, 0, 0);
                }
                catch (Exception exception)
                {
                    string noColumnsCause = DescribeOrTypeName(exception);
                    foreach (string label in CoolingPairingLabels) RecordRowSafely(label, "FAIL: the no-columns levels were not built: " + noColumnsCause);
                    Log.Error("Z-Levels no-columns level setup failed: " + exception);
                    return;
                }
                foreach (string label in CoolingPairingLabels) RecordCooling(label, NoColumnsPairMissing);
                return;
            }
            if (column == null || pipeDef == null)
            {
                foreach (string label in CoolingPairingLabels) RecordCooling(label, "FAIL: ZL_CoolingColumn or coolingPipe def is not loaded");
                return;
            }
            try
            {
                surface = Find.CurrentMap;
                ZLevelsManager manager = Current.Game?.GetComponent<ZLevelsManager>();
                if (manager == null) throw new InvalidOperationException("ZLevelsManager is missing from the current game");
                lower = CreateLevelBelow(manager, surface);
                IntVec3 cell = surface.Center + new IntVec3(-12, 0, 0);
                coolingPairCell = cell;
                IntVec3 farCell = cell + new IntVec3(3, 0, 0);
                if (!cell.InBounds(surface) || !cell.InBounds(lower) || !farCell.InBounds(surface) || !(cell + new IntVec3(0, 0, 1)).InBounds(surface)
                    || !(cell + new IntVec3(1, 0, -1)).InBounds(surface))
                {
                    throw new InvalidOperationException("the test cells are off the map");
                }
                ThingWithComps surfaceBuilding = SpawnBuilding(column, surface, cell, ThingDefOf.Steel);
                ThingWithComps lowerBuilding = SpawnBuilding(column, lower, cell, ThingDefOf.Steel);
                ThingWithComps nearBuilding = SpawnBuilding(column, surface, cell + new IntVec3(1, 0, -1), ThingDefOf.Steel);
                ThingWithComps farBuilding = SpawnBuilding(column, surface, farCell, ThingDefOf.Steel);
                ThingWithComps pipeBuilding = SpawnBuilding(pipeDef, surface, cell + new IntVec3(0, 0, 1));
                var surfaceComp = RequireCoolingComp(surfaceBuilding, "the surface column");
                var lowerComp = RequireCoolingComp(lowerBuilding, "the lower column");
                var nearComp = RequireCoolingComp(nearBuilding, "the column 1 cell east");
                var farComp = RequireCoolingComp(farBuilding, "the column 3 cells east");

                MethodInfo loopOf = CoolingLoopOfMethod(out string problem);
                if (loopOf == null)
                {
                    foreach (string label in new[] { "cooling columns paired", "column one cell away stays unpaired", "column out of range stays unpaired" })
                    {
                        RecordCooling(label, "FAIL: " + problem);
                    }
                }
                else
                {
                    string pairReason = UnjoinableReason("the surface column", surfaceComp) ?? UnjoinableReason("the lower column", lowerComp);
                    bool paired = pairReason == null && Joined(loopOf, surfaceComp, lowerComp) && Joined(loopOf, lowerComp, surfaceComp);
                    RecordCooling("cooling columns paired", pairReason != null ? "FAIL: " + pairReason : paired ? "PASS" : "FAIL: the surface column and the lower column do not share a loop");
                    RecordCooling("column one cell away stays unpaired", StaysUnpairedResult(loopOf, lowerComp, pairReason, "the column one cell away", nearComp, new IntVec3(1, 0, -1), paired));
                    RecordCooling("column out of range stays unpaired", StaysUnpairedResult(loopOf, lowerComp, pairReason, "the column three cells away", farComp, new IntVec3(3, 0, 0), paired));
                }
                // One MapUpdate runs Rimatomics' grid regeneration, which joins the new pipe to its net (Harmony_GridRegen is a prefix on Map.MapUpdate).
                surface.MapUpdate();
                RecordCooling("coolant pipe joins the column", CoolantPipeResult(surfaceComp, pipeBuilding));
                // The control for the pipe check. The column three cells east touches neither the first column nor the coolingPipe, so it must
                // stay off their net. If it joined, the pipe check would pass for a reason other than adjacency.
                RecordCooling("column three cells away stays off the pipe net", OffPipeNetResult(surfaceComp, farComp));
                // The round trip moves the surface column, so it runs after every row that reads the column's first position.
                RecordCooling("column moved to another level and back stays paired", loopOf == null ? "FAIL: " + problem : MovedAndBackResult(loopOf, surface, lower, cell, column, surfaceBuilding, surfaceComp, lowerComp));
            }
            catch (Exception exception)
            {
                // DescribeOrTypeName does not throw. If Describe fails, the rows below name the exception type, so every label still gets a row.
                string cause = DescribeOrTypeName(exception);
                foreach (string label in CoolingPairingLabels)
                {
                    // A label whose check never ran says why. It does not read as a failure of its own check.
                    if (!coolingResults.ContainsKey(label)) RecordRowSafely(label, "FAIL: not run: " + cause);
                }
                // The log comes after the rows, so a logger that throws cannot skip them.
                Log.Error("Z-Levels cooling pairing check failed: " + exception);
            }
        }

        // The partner highlight rows. The surface column on the highlight cell stands with no lower column, so HighlightTargets reads that cell
        // unpaired. Then the lower column stands on it, and HighlightTargets reads it paired. In a -Visual run the lower column waits for the
        // amber shot, so the two screenshots show both states. Otherwise it stands at once.
        private void RecordPartnerHighlight(ThingDef column)
        {
            if (CoolingNoColumns)
            {
                foreach (string label in CoolingHighlightLabels) RecordCooling(label, NoColumnsPairMissing);
                return;
            }
            if (column == null || surface == null || lower == null)
            {
                foreach (string label in CoolingHighlightLabels) RecordCooling(label, "FAIL: the cooling column or the lower level is missing");
                return;
            }
            try
            {
                partnerColumn = column;
                partnerCell = surface.Center + PartnerOffset;
                if (!partnerCell.InBounds(surface) || !partnerCell.InBounds(lower)) throw new InvalidOperationException("the highlight cell is off the map");
                // The lower cell is cleared, as SpawnBuilding clears the surface cell, so the amber shot shows an empty cell. Both cells are unfogged.
                ClearCell(lower, partnerCell);
                lower.terrainGrid.SetTerrain(partnerCell, TerrainDefOf.Concrete);
                UnfogAround(lower, partnerCell, 3f);
                UnfogAround(surface, partnerCell, 3f);
                ThingWithComps surfaceBuilding = SpawnBuilding(column, surface, partnerCell, ThingDefOf.Steel);
                partnerSurfaceComp = RequireCoolingComp(surfaceBuilding, "the surface column at the highlight cell");
                string reason = UnjoinableReason("the surface column at the highlight cell", partnerSurfaceComp);
                RecordCooling(PartnerMarksLabel, reason != null ? "FAIL: " + reason : HighlightResult(lower, partnerCell, false));
                if (GenCommandLine.CommandLineArgPassed("zlevelsscreenshots"))
                {
                    partnerLookStage = 1;
                    return;
                }
                PlacePartnerLowerColumn();
            }
            catch (Exception exception)
            {
                // A label whose check never ran says why, as in the pairing check. The log comes after the rows, so a logger that throws cannot skip them.
                string cause = DescribeOrTypeName(exception);
                foreach (string label in CoolingHighlightLabels)
                {
                    if (!coolingResults.ContainsKey(label)) RecordRowSafely(label, "FAIL: not run: " + cause);
                }
                Log.Error("Z-Levels partner highlight check failed: " + exception);
            }
        }

        // Stands the lower column on the highlight cell, then reads the highlight there. The cell must now be paired.
        private void PlacePartnerLowerColumn()
        {
            ThingWithComps lowerBuilding = SpawnBuilding(partnerColumn, lower, partnerCell, ThingDefOf.Steel);
            var lowerComp = RequireCoolingComp(lowerBuilding, "the lower column at the highlight cell");
            string reason = UnjoinableReason("the surface column at the highlight cell", partnerSurfaceComp)
                ?? UnjoinableReason("the lower column at the highlight cell", lowerComp);
            RecordCooling(PartnerPairsLabel, reason != null ? "FAIL: " + reason : HighlightResult(lower, partnerCell, true));
        }

        // Reads HighlightTargets by name, so this code compiles before the method exists. Until it does, each row says so. The row passes when the
        // target on the cell has the paired state that the row expects.
        private static string HighlightResult(Map viewed, IntVec3 cell, bool paired)
        {
            Type registry = Type.GetType("ZLevelsRimatomics.CoolingLoopRegistry, ZLevelsRimatomics");
            if (registry == null) return "FAIL: highlight not built yet: the type ZLevelsRimatomics.CoolingLoopRegistry is missing";
            MethodInfo targets = registry.GetMethod("HighlightTargets", BindingFlags.Public | BindingFlags.Static);
            if (targets == null) return "FAIL: highlight not built yet: the method CoolingLoopRegistry.HighlightTargets is missing";
            object result;
            try
            {
                result = targets.Invoke(null, new object[] { viewed });
            }
            catch (TargetInvocationException wrapped)
            {
                ExceptionDispatchInfo.Capture(wrapped.InnerException).Throw();
                return null; // Throw does not return, so this line never runs.
            }
            foreach (object target in (System.Collections.IEnumerable)result)
            {
                if ((IntVec3)ReadField(target, "Cell") != cell) continue;
                bool isPaired = (bool)ReadField(target, "Paired");
                if (isPaired == paired) return "PASS";
                return "FAIL: the target on the highlight cell is " + (isPaired ? "paired" : "unpaired") + ", not " + (paired ? "paired" : "unpaired");
            }
            return "FAIL: no target on the highlight cell";
        }

        // The partner look, in -Visual runs only. Stage 1 views the lower level on the highlight cell. Stage 2 takes the amber shot once the view has had
        // three seconds to render. Stage 3 writes the amber row, stands the lower column, and reads the pair. Stage 4 takes the green shot, and stage 5
        // writes the green row. It returns true when the look is over, so the link row can be written.
        private bool AdvancePartnerLook()
        {
            switch (partnerLookStage)
            {
                case 1:
                    // The lower level is unlit, and rock stops a torch's glow. So the cells round the highlight cell are cleared, and a torch lamp,
                    // which needs no power, lights them. An outline is dimmed in the dark, so the look needs the light.
                    foreach (IntVec3 near in CellRect.CenteredOn(partnerCell, 2))
                    {
                        if (!near.InBounds(lower)) continue;
                        ClearCell(lower, near);
                        lower.terrainGrid.SetTerrain(near, TerrainDefOf.Concrete);
                    }
                    SpawnBuilding(DefDatabase<ThingDef>.GetNamed("TorchLamp"), lower, partnerCell + new IntVec3(-2, 0, -2));
                    CameraJumper.TryJump(partnerCell, lower, CameraJumper.MovementMode.Cut);
                    partnerShotDue = DateTime.UtcNow.AddSeconds(3);
                    partnerLookStage = 2;
                    return false;
                case 2:
                    if (DateTime.UtcNow < partnerShotDue) return false;
                    RecordPartnerDrawn(lower, PartnerDrawnLabel, false);
                    ScreenshotTaker.TakeNonSteamShot(PartnerAmberName);
                    partnerShotDue = DateTime.UtcNow.AddSeconds(3);
                    partnerLookStage = 3;
                    return false;
                case 3:
                    if (DateTime.UtcNow < partnerShotDue) return false;
                    RecordShotRow(PartnerAmberLabel, PartnerAmberName);
                    PlacePartnerLowerColumn();
                    partnerShotDue = DateTime.UtcNow.AddSeconds(3);
                    partnerLookStage = 4;
                    return false;
                case 4:
                    if (DateTime.UtcNow < partnerShotDue) return false;
                    RecordPartnerDrawn(lower, PartnerDrawnPairedLabel, true);
                    ScreenshotTaker.TakeNonSteamShot(PartnerGreenName);
                    partnerShotDue = DateTime.UtcNow.AddSeconds(3);
                    partnerLookStage = 5;
                    return false;
                case 5:
                    if (DateTime.UtcNow < partnerShotDue) return false;
                    RecordShotRow(PartnerGreenLabel, PartnerGreenName);
                    partnerLookStage = 0;
                    return true;
                default:
                    return true;
            }
        }

        // Writes the row for a screenshot that was just taken. A FAIL row also fails the cooling link row.
        private void RecordShotRow(string label, string name)
        {
            string result = ScreenshotResult(name);
            RecordCooling(label, result);
            if (result.StartsWith("FAIL", StringComparison.Ordinal)) coolingScreenshotFailed = true;
        }

        // Writes one calls row. It passes when the draw calls of the last frame are the planes the scenario expects on the adjacent level, counted by cell,
        // side, and colour, and the cell and paired counts match the scenario's. The row counts the calls issued, not the pixels on screen. The look writes it
        // at the amber stage and again at the green stage, after the pair is placed. The green row also requires a paired cell to be drawn. A FAIL also fails
        // the cooling link row.
        private void RecordPartnerDrawn(Map viewed, string label, bool requirePaired)
        {
            string result = PartnerDrawnResult(viewed, surface, requirePaired);
            RecordCooling(label, result);
            if (result.StartsWith("FAIL", StringComparison.Ordinal)) partnerDrawnFailed = true;
        }

        // Reads the outline by name, so a missing field is a row failure with its cause. The calls come from the frame that ran for the viewed map.
        // The expected planes come from the columns the scenario placed, not from the registry, so a target that the registry returns in place of a correct
        // one fails the row, and a placed column that goes missing fails it too.
        private static string PartnerDrawnResult(Map viewed, Map adjacent, bool requirePaired)
        {
            Type hook = Type.GetType("ZLevelsRimatomics.CoolingColumnHighlight, ZLevelsRimatomics");
            if (hook == null) return "FAIL: partner outline not built yet: the type ZLevelsRimatomics.CoolingColumnHighlight is missing";
            FieldInfo lastMap = hook.GetField("LastMap", BindingFlags.Public | BindingFlags.Static);
            FieldInfo finished = hook.GetField("FinishedLastFrame", BindingFlags.Public | BindingFlags.Static);
            FieldInfo planes = hook.GetField("PlanesLastFrame", BindingFlags.Public | BindingFlags.Static);
            FieldInfo drawn = hook.GetField("DrawCallsLastFrame", BindingFlags.Public | BindingFlags.Static);
            var missing = new List<string>();
            if (lastMap == null) missing.Add("LastMap");
            if (finished == null) missing.Add("FinishedLastFrame");
            if (planes == null) missing.Add("PlanesLastFrame");
            if (drawn == null) missing.Add("DrawCallsLastFrame");
            if (missing.Count > 0) return "FAIL: partner outline not built yet: these fields of CoolingColumnHighlight are missing: " + string.Join(", ", missing);
            if (!object.ReferenceEquals(lastMap.GetValue(null), viewed)) return "FAIL: the outline pass did not run on the viewed level";
            if (!(bool)finished.GetValue(null)) return "FAIL: the outline pass threw before it finished";
            // Each item is one draw call, with its side and the colour its material had at the call. The verdict compares the calls with the expected planes.
            var drawCalls = new List<ZLevelsRimatomics.OutlineDraw>();
            foreach (object item in (System.Collections.IEnumerable)drawn.GetValue(null)) drawCalls.Add((ZLevelsRimatomics.OutlineDraw)item);
            List<ZLevelsRimatomics.OutlineCell> placed;
            List<ZLevelsRimatomics.OutlineCell> lost;
            ExpectedOutline(viewed, adjacent, out placed, out lost);
            float altitude = AltitudeLayer.MetaOverlays.AltitudeFor();
            return ZLevelsRimatomics.OutlineRule.RowVerdict(placed, lost, drawCalls, (int)planes.GetValue(null),
                (x, z) => new IntVec3(x, 0, z).InBounds(viewed), altitude, requirePaired);
        }

        // The row's expectation, built from every cooling column the scenario placed, whatever its state now. A column placed on the adjacent level gives its cell
        // a plane cell, once for each cell. A cell is paired when a column the scenario placed stands on the same cell of the viewed level. A placed column that
        // no longer stands where it was placed goes into lost, whichever level it was on, so the row fails and names its cell.
        private static void ExpectedOutline(Map viewed, Map adjacent, out List<ZLevelsRimatomics.OutlineCell> placed, out List<ZLevelsRimatomics.OutlineCell> lost)
        {
            placed = new List<ZLevelsRimatomics.OutlineCell>();
            lost = new List<ZLevelsRimatomics.OutlineCell>();
            var seen = new HashSet<IntVec3>();
            foreach (ColumnPlacement placement in scenarioPlacements)
            {
                if (!placement.Usable) lost.Add(new ZLevelsRimatomics.OutlineCell(placement.Cell.x, placement.Cell.z, false));
                if (placement.PlacedOn != adjacent || !seen.Add(placement.Cell)) continue;
                IntVec3 cell = placement.Cell;
                bool paired = scenarioPlacements.Exists(other => other.PlacedOn == viewed && other.Cell == cell);
                placed.Add(new ZLevelsRimatomics.OutlineCell(cell.x, cell.z, paired));
            }
        }

        // LoopOf is read by name, so a missing or renamed method is a row failure with its cause, not a compile error. When LoopOf cannot
        // be read, the method returns null and problem names the cause.
        private static MethodInfo CoolingLoopOfMethod(out string problem)
        {
            Type registry = Type.GetType("ZLevelsRimatomics.CoolingLoopRegistry, ZLevelsRimatomics");
            if (registry == null)
            {
                problem = "type ZLevelsRimatomics.CoolingLoopRegistry is not resolved";
                return null;
            }
            // LoopOf has two overloads, so the lookup names the one-column signature.
            MethodInfo loopOf = registry.GetMethod("LoopOf", new[] { typeof(ZLevelsRimatomics.CompCoolingColumn) });
            problem = loopOf == null ? "CoolingLoopRegistry.LoopOf is missing or not public static" : null;
            return loopOf;
        }

        // A spawned building with no CompCoolingColumn is a setup failure, so it is named here rather than left as a null reference.
        private static ZLevelsRimatomics.CompCoolingColumn RequireCoolingComp(ThingWithComps building, string role)
        {
            var comp = building.GetComp<ZLevelsRimatomics.CompCoolingColumn>();
            if (comp == null) throw new InvalidOperationException(role + " has no CompCoolingColumn");
            return comp;
        }

        // The rows check each column with UnjoinableReason before this runs, so an unusable column is named by its row and cannot read as
        // "not paired". A TargetInvocationException is unwrapped and rethrown with its stack trace kept.
        private static bool Joined(MethodInfo loopOf, ZLevelsRimatomics.CompCoolingColumn from, ZLevelsRimatomics.CompCoolingColumn member)
        {
            object result;
            try
            {
                result = loopOf.Invoke(null, new object[] { from });
            }
            catch (TargetInvocationException wrapped)
            {
                ExceptionDispatchInfo.Capture(wrapped.InnerException).Throw();
                return false; // Throw does not return, so this line never runs.
            }
            var loop = (System.Collections.IList)result;
            if (!loop.Contains(from)) throw new InvalidOperationException("the start column is not in its own loop");
            return loop.Contains(member);
        }

        // The reason a column cannot take part in a loop test, or null when it is spawned on a live map and registered. The role names the
        // column in the row, so a failure says which column it is and why.
        private static string UnjoinableReason(string role, ZLevelsRimatomics.CompCoolingColumn column)
        {
            if (!column.parent.Spawned || column.parent.Map == null) return role + " is not spawned on a map";
            if (column.parent.Map.Disposed) return role + " is on a disposed map";
            if (!ListedInThingGrid(column.parent)) return role + " is not listed in its map's thing grid at its cell";
            if (ReadField(column, "pipe") == null) return role + " has no pipe, so it is not registered";
            if (!ZLevelsRimatomics.CoolingLoopRegistry.IsRegistered(column)) return role + " is not registered";
            return null;
        }

        // The harness reads the thing grid itself, so a column that a move left off its grid is named by the grid, not by the registry.
        private static bool ListedInThingGrid(Thing thing)
        {
            return thing.Map.thingGrid.ThingsAt(thing.Position).Any(listed => ReferenceEquals(listed, thing));
        }

        // The row for a column that must stay off the loop. pairReason names an unusable column of the in-range pair, and the column itself must
        // be usable too. The in-range pair must be joined, so an empty loop proves nothing. The column must sit at the layout offset from the lower column.
        // The offset is exact, so a diagonal neighbour is checked as a diagonal and not as a distance.
        private static string StaysUnpairedResult(MethodInfo loopOf, ZLevelsRimatomics.CompCoolingColumn lowerComp, string pairReason, string role, ZLevelsRimatomics.CompCoolingColumn column, IntVec3 offset, bool paired)
        {
            string reason = pairReason ?? UnjoinableReason(role, column);
            if (reason != null) return "FAIL: " + reason;
            if (!paired) return "FAIL: the in-range pair is not joined, so " + role + " proves nothing";
            IntVec3 placed = column.parent.Position - lowerComp.parent.Position;
            if (placed != offset) return "FAIL: the test layout puts " + role + " at " + placed + " from the lower column, not " + offset;
            return Joined(loopOf, lowerComp, column) || Joined(loopOf, column, lowerComp) ? "FAIL: " + role + " shares the loop" : "PASS";
        }

        // The column three cells east touches neither the first column nor the coolingPipe. Only "no" passes. Any other answer fails the row,
        // because it does not show that the column is off the net.
        private static string OffPipeNetResult(ZLevelsRimatomics.CompCoolingColumn surfaceComp, ZLevelsRimatomics.CompCoolingColumn farComp)
        {
            string shares = SharesPipeNet(surfaceComp, farComp);
            if (shares == "no") return "PASS";
            if (shares == "yes") return "FAIL: the column three cells away shares the pipe net of the first surface column";
            return "FAIL: the pipe net could not be read: " + shares;
        }

        // One line per row: the exception type and message, then the type and message of each inner exception in turn. Line breaks become
        // spaces, so a row never spans two lines.
        private static string Describe(Exception exception)
        {
            var levels = new List<string>();
            for (Exception level = exception; level != null; level = level.InnerException)
            {
                levels.Add(level.GetType().Name + ": " + level.Message);
            }
            return string.Join("; inner ", levels).Replace('\r', ' ').Replace('\n', ' ');
        }

        // Describe, or a fallback when Describe throws. The fallback names the exception type and the Describe failure, so a row built from it
        // still says what failed. If the log call fails too, that failure goes into the same text, so no failure is dropped.
        private static string DescribeOrTypeName(Exception exception)
        {
            try
            {
                return Describe(exception);
            }
            catch (Exception describeException)
            {
                string fallback = exception.GetType().Name + ", because Describe failed with " + describeException.GetType().Name + ": " + OneLine(describeException.Message);
                try
                {
                    Log.Error("Z-Levels validation could not describe the exception in a row. The row says: " + fallback);
                }
                catch (Exception logException)
                {
                    fallback += ", and the log also failed with " + logException.GetType().Name + ": " + OneLine(logException.Message);
                }
                return fallback;
            }
        }

        // The text on one line, so a row never spans two lines.
        private static string OneLine(string text)
        {
            return (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
        }

        // The ZUtils.TeleportThing method. ZUtils is internal to the mod, so the harness reaches it by name. TeleportThing sets only the map
        // index and the position, so the column keeps its spawn state and its registry entry.
        private static MethodInfo TeleportThingMethod()
        {
            Type zutils = typeof(ZLevelsManager).Assembly.GetType("ZLevels.ZUtils", true);
            return zutils.GetMethod("TeleportThing", BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException("ZLevels.ZUtils.TeleportThing is missing");
        }

        // The surface column moves to a free cell on the lower level, an extra cooling column registers on the surface, and the surface column
        // moves back. The move must keep the surface column's entry, so the pair holds afterwards. The row fails when either column is unusable.
        private static string MovedAndBackResult(MethodInfo loopOf, Map surface, Map lower, IntVec3 cell, ThingDef column, ThingWithComps surfaceBuilding,
            ZLevelsRimatomics.CompCoolingColumn surfaceComp, ZLevelsRimatomics.CompCoolingColumn lowerComp)
        {
            IntVec3 away = cell + new IntVec3(0, 0, -2);
            if (!away.InBounds(lower) || !away.InBounds(surface)) throw new InvalidOperationException("the round-trip cell is off the map");
            // Before the move, the surface column must be registered. Otherwise the midpoint checks below prove nothing.
            if (!ZLevelsRimatomics.CoolingLoopRegistry.IsRegistered(surfaceComp)) return "FAIL: the surface column is not registered before the move";
            MethodInfo teleport = TeleportThingMethod();
            ClearCell(lower, away);
            lower.terrainGrid.SetTerrain(away, TerrainDefOf.Concrete);
            teleport.Invoke(null, new object[] { surfaceBuilding, lower, away });
            // Two assertions while the column stands on the lower level. The reason names the one that fails.
            string midpoint = MidpointProblem(lower, away, surfaceBuilding, surfaceComp);
            SpawnBuilding(column, surface, away, ThingDefOf.Steel);
            teleport.Invoke(null, new object[] { surfaceBuilding, surface, cell });
            if (midpoint != null) return midpoint;
            string reason = UnjoinableReason("the surface column", surfaceComp) ?? UnjoinableReason("the lower column", lowerComp);
            if (reason != null) return "FAIL: " + reason + " after the round trip";
            bool joined = Joined(loopOf, surfaceComp, lowerComp) && Joined(loopOf, lowerComp, surfaceComp);
            return joined ? "PASS" : "FAIL: the surface column and the lower column do not share a loop after the round trip";
        }

        // The two midpoint assertions. The lower map's thing grid must not list the column at the away cell, and the registry must not count the
        // column as usable. The reason names the assertion that fails.
        private static string MidpointProblem(Map lower, IntVec3 away, Thing moved, ZLevelsRimatomics.CompCoolingColumn movedComp)
        {
            if (lower.thingGrid.ThingsAt(away).Any(listed => ReferenceEquals(listed, moved))) return "FAIL: the lower map's thing grid lists the moved column at the away cell";
            if (ZLevelsRimatomics.CoolingLoopRegistry.IsRegistered(movedComp)) return "FAIL: the moved column is still usable on the lower level";
            return null;
        }

        // Rimatomics types are read by name, so this code compiles without a reference to Rimatomics. The column's pipe field must be set,
        // and the column and the coolingPipe must hold the same pipe net.
        private static string CoolantPipeResult(ZLevelsRimatomics.CompCoolingColumn column, ThingWithComps pipe)
        {
            if (ReadField(column, "pipe") == null) return "FAIL: the column's pipe field is null";
            object pipePipe = pipe.AllComps.FirstOrDefault(comp => comp.GetType().FullName == "Rimatomics.CompPipe");
            if (pipePipe == null) return "FAIL: the coolingPipe has no Rimatomics pipe comp";
            object columnNet = ReadField(ReadField(column, "pipe"), "pipeNetRef");
            object pipeNet = ReadField(pipePipe, "pipeNetRef");
            if (columnNet == null) return "FAIL: the column has no pipe net after one MapUpdate";
            if (pipeNet == null) return "FAIL: the coolingPipe has no pipe net after one MapUpdate";
            return object.ReferenceEquals(columnNet, pipeNet) ? "PASS" : "FAIL: the column and the coolingPipe are in different pipe nets";
        }

        // The control result for two columns. A column with no pipe component reports "no component", and a pipe with no net reports "no net".
        private static string SharesPipeNet(ZLevelsRimatomics.CompCoolingColumn first, ZLevelsRimatomics.CompCoolingColumn second)
        {
            object firstPipe = ReadField(first, "pipe");
            object secondPipe = ReadField(second, "pipe");
            if (firstPipe == null || secondPipe == null) return "no component";
            object firstNet = ReadField(firstPipe, "pipeNetRef");
            object secondNet = ReadField(secondPipe, "pipeNetRef");
            if (firstNet == null || secondNet == null) return "no net";
            return object.ReferenceEquals(firstNet, secondNet) ? "yes" : "no";
        }

        // Reads a public instance field by name. The cooling column and the Rimatomics pipe are named by string, so a rename throws here
        // with the type and the field, instead of a null reference.
        private static object ReadField(object instance, string fieldName)
        {
            FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            if (field == null) throw new MissingFieldException(instance.GetType().FullName, fieldName);
            return field.GetValue(instance);
        }

        // The column costs what the power column costs: no Steel in CostList, a CostStuffCount of 60, and WorkToBuild of 750.
        private static string CoolingColumnCostResult(ThingDef column)
        {
            if (column.CostList != null && column.CostList.Any(cost => cost.thingDef == ThingDefOf.Steel)) return "FAIL: Steel is in CostList";
            if (column.CostStuffCount != 60) return "FAIL: CostStuffCount is " + column.CostStuffCount;
            float work = column.GetStatValueAbstract(StatDefOf.WorkToBuild);
            return work == 750f ? "PASS" : "FAIL: WorkToBuild is " + work;
        }

        // The column holds a roof and counts as an edifice, as the power column does.
        private static string CoolingColumnRoofResult(ThingDef column)
        {
            if (!column.holdsRoof) return "FAIL: holdsRoof is false";
            return column.building != null && column.building.isEdifice ? "PASS" : "FAIL: building isEdifice is false";
        }

        // The look check: one column with a coolant pipe beside it, on the start map, with the camera on the column.
        // Returns false after a FAIL record when either def is missing.
        // Places the column and the pipe for the screenshot. Returns null when both are placed, and the reason when they are not. The caller
        // writes the screenshot row from that reason.
        private static string PlaceCoolingLook()
        {
            ThingDef column = DefDatabase<ThingDef>.GetNamedSilentFail("ZL_CoolingColumn");
            ThingDef pipe = DefDatabase<ThingDef>.GetNamedSilentFail("coolingPipe");
            if (column == null || pipe == null)
            {
                return "column or coolant pipe def missing";
            }
            Map map = Find.CurrentMap;
            IntVec3 cell = map.Center;
            foreach (IntVec3 near in CellRect.CenteredOn(cell, 4))
            {
                if (near.InBounds(map)) map.fogGrid.Unfog(near);
            }
            ThingWithComps placedColumn = SpawnBuilding(column, map, cell, ThingDefOf.Steel);
            ThingWithComps placedPipe = SpawnBuilding(pipe, map, cell + new IntVec3(1, 0, 0));
            Record("cooling look placed", "column " + DescribeLookThing(placedColumn) + "; pipe " + DescribeLookThing(placedPipe)
                + "; terrain under column " + cell.GetTerrain(map).defName + "; roofed " + cell.Roofed(map) + "; fogged " + cell.Fogged(map));
            CameraJumper.TryJump(cell, map, CameraJumper.MovementMode.Cut);
            return null;
        }

        // The facts a look check needs: where a thing is, whether it spawned, how it draws, and the texture path its def names.
        private static string DescribeLookThing(Thing thing)
        {
            return thing.def.defName + " at " + thing.Position + ", spawned " + thing.Spawned + ", drawer " + thing.def.drawerType
                + ", graphic " + (thing.Graphic == null ? "none" : thing.Graphic.GetType().Name)
                + ", texPath " + (thing.def.graphicData == null ? "none" : thing.def.graphicData.texPath)
                + ", draw position " + thing.DrawPos;
        }

        // One row write from the failure path. A write that fails is logged, and the caller goes on to the next row, so one failed write cannot stop the rest.
        private void RecordRowSafely(string label, string result)
        {
            try
            {
                RecordCooling(label, result);
            }
            catch (Exception rowException)
            {
                Log.Error("Z-Levels cooling row \"" + label + "\" could not be recorded: " + rowException);
            }
        }

        // The row is written first. A label's result is stored only after the row is written, so a failed write leaves the label
        // unrecorded, and the catch that runs later reports it.
        private void RecordCooling(string label, string result)
        {
            Record(label, result);
            coolingResults[label] = result;
        }

        // The labels the cooling link needs: CoolingLabels, and this run's overheating row. The cooled run's row is the only one the link owes.
        private static IEnumerable<string> RequiredCoolingLabels()
        {
            return CoolingLabels.Append(ReactorOverheatLabel);
        }

        // The final line passes only when every label RequiredCoolingLabels names has a PASS. Otherwise it names the labels that do not.
        private void RecordCoolingLink()
        {
            if (CoolingNoColumns)
            {
                Record("cooling link", NoColumnsPairMissing);
                return;
            }
            // A screenshot that did not complete leaves the link without a PASS, even when every label passed.
            if (coolingScreenshotFailed)
            {
                Record("cooling link", "FAIL: the cooling screenshot did not complete");
                return;
            }
            // The outline row is part of the look, so a FAIL on it also leaves the link without a PASS.
            if (partnerDrawnFailed)
            {
                Record("cooling link", "FAIL: the partner outline row failed");
                return;
            }
            var notPassed = RequiredCoolingLabels().Where(label => !coolingResults.TryGetValue(label, out string result) || result != "PASS").ToList();
            Record("cooling link", notPassed.Count == 0 ? "PASS" : "FAIL: not PASS: " + string.Join("; ", notPassed));
        }

        // The three link rows, and the sequence that writes them. A cooling tower stands on the surface and a Thor turbine on the lower level, each beside its
        // column, so each joins the column's net. The sequence reads the lower turbine's net, saves and reloads, reads again, and then removes the surface
        // column. A read is a membership check on the lower turbine's own net: the tower must be in its Coolers, and the turbine in its Turbines.
        private static readonly string[] CoolingLinkLabels =
        {
            "lower turbine shares the surface tower's loop",
            "cooling link survives reload",
            "cooling link breaks when a column is removed",
            "cooling link breaks when a column is deconstructed",
        };
        // A footprint that holds any of these would lose it to SpawnBuilding's clearing, and each one changes the nets, so the scene refuses to start.
        private static readonly string[] CoolingFootprintDefs = { "ZL_CoolingColumn", "coolingPipe", "coolingPipeHidden", "CoolingTower", "Turbine" };
        private enum LinkStep { Scene, Settle, Shares, Save, AwaitLoad, SettleAfterLoad, ReloadCheck, ReactorBuild, ReactorPower, ReactorSample, Removal, SettleAfterRemoval, RemovalCheck, Restore, SettleAfterRestore, Deconstruction, SettleAfterDeconstruction, DeconstructionCheck, Done }
        // The sequence's state. Static, because the reload continues the sequence in a new RuntimeValidation.
        private static LinkStep linkStep = LinkStep.Scene;
        private static DateTime linkDeadline;
        private static int linkTickMark;
        private static IntVec3 coolingPairCell = IntVec3.Invalid;
        private static IntVec3 linkTowerCell = IntVec3.Invalid;
        private static IntVec3 linkTurbineCell = IntVec3.Invalid;
        private static bool linkSharedBeforeSave;
        private static bool linkHeldBeforeRemoval;
        // The reason that a link building is missing at each read, or null when both stand. A row that finds a building missing names it.
        private static string linkSharedMissing;
        private static string linkRemovalMissing;
        private static string linkDeconstructMissing;
        private static bool linkHeldBeforeDeconstruction;

        // Advances the link sequence by one frame. Each step waits two game ticks where the nets have to settle: after the scene, after the load, and after
        // the removal. The reactor stages run after the reload and before the removal, so the link stays whole while the reactor is sampled. It returns true
        // once the sequence is over, so the link row can be written.
        private bool AdvanceLinkStages()
        {
            if (linkStep == LinkStep.Done) return true;
            if (linkStep == LinkStep.Scene)
            {
                linkDeadline = DateTime.UtcNow.AddMinutes(6);
                string problem = PlaceLinkScene();
                if (problem != null)
                {
                    Record("cooling link scene", "FAIL: " + problem);
                    foreach (string label in CoolingLinkLabels) RecordCooling(label, "FAIL: " + problem);
                    FailReactorChecks(problem);
                    linkStep = LinkStep.Done;
                    return true;
                }
                linkTickMark = Find.TickManager.TicksGame;
                linkStep = LinkStep.Settle;
                return false;
            }
            if (DateTime.UtcNow > linkDeadline) throw new TimeoutException("the cooling link stage " + linkStep + " did not finish in time");
            int ticks = Find.TickManager.TicksGame;
            switch (linkStep)
            {
                case LinkStep.Settle:
                    if (ticks < linkTickMark + 2) return false;
                    RecordLinkScene();
                    linkStep = LinkStep.Shares;
                    return false;
                case LinkStep.Shares:
                    linkSharedBeforeSave = RecordSharesRow();
                    linkStep = LinkStep.Save;
                    return false;
                case LinkStep.Save:
                    GameDataSaveLoader.SaveGame("ZLevelsValidation");
                    LongEventHandler.ExecuteWhenFinished(() => GameDataSaveLoader.LoadGame("ZLevelsValidation"));
                    linkStep = LinkStep.AwaitLoad;
                    return false;
                case LinkStep.AwaitLoad:
                    // Only the instance that the load creates has reloaded set. The instance that saved waits here until the load replaces it.
                    if (!reloaded) return false;
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
                    linkTickMark = Find.TickManager.TicksGame;
                    linkStep = LinkStep.SettleAfterLoad;
                    return false;
                case LinkStep.SettleAfterLoad:
                    if (ticks < linkTickMark + 2) return false;
                    linkStep = LinkStep.ReloadCheck;
                    return false;
                case LinkStep.ReloadCheck:
                    RecordReloadRow();
                    linkStep = LinkStep.ReactorBuild;
                    return false;
                case LinkStep.ReactorBuild:
                    linkTickMark = ticks;
                    linkStep = BuildReactor() ? LinkStep.ReactorPower : LinkStep.Removal;
                    return false;
                case LinkStep.ReactorPower:
                    // Two ticks let the nets and the batteries settle before PowerOn is read.
                    if (ticks < linkTickMark + 2) return false;
                    linkStep = AwaitReactorPower(ticks);
                    return false;
                case LinkStep.ReactorSample:
                    while (reactorSamplesTaken < ReactorSampleCount && ticks >= reactorStartTick + ReactorSampleInterval * reactorSamplesTaken)
                    {
                        SampleReactor(ticks);
                        reactorSamplesTaken++;
                    }
                    if (reactorSamplesTaken < ReactorSampleCount) return false;
                    FinishReactorChecks();
                    linkStep = LinkStep.Removal;
                    return false;
                case LinkStep.Removal:
                    RemoveSurfaceColumn();
                    linkTickMark = Find.TickManager.TicksGame;
                    linkStep = LinkStep.SettleAfterRemoval;
                    return false;
                case LinkStep.SettleAfterRemoval:
                    if (ticks < linkTickMark + 2) return false;
                    linkStep = LinkStep.RemovalCheck;
                    return false;
                case LinkStep.RemovalCheck:
                    RecordRemovalRow();
                    linkStep = LinkStep.Restore;
                    return false;
                case LinkStep.Restore:
                    RestoreSurfaceColumn();
                    linkTickMark = Find.TickManager.TicksGame;
                    linkStep = LinkStep.SettleAfterRestore;
                    return false;
                case LinkStep.SettleAfterRestore:
                    if (ticks < linkTickMark + 2) return false;
                    linkStep = LinkStep.Deconstruction;
                    return false;
                case LinkStep.Deconstruction:
                    DeconstructSurfaceColumn();
                    linkTickMark = Find.TickManager.TicksGame;
                    linkStep = LinkStep.SettleAfterDeconstruction;
                    return false;
                case LinkStep.SettleAfterDeconstruction:
                    if (ticks < linkTickMark + 2) return false;
                    linkStep = LinkStep.DeconstructionCheck;
                    return false;
                case LinkStep.DeconstructionCheck:
                    RecordDeconstructionRow();
                    linkStep = LinkStep.Done;
                    return true;
                default:
                    return true;
            }
        }

        // Places the link scene. It returns null when both buildings are placed, and the reason when they are not. Each footprint is set from the game's own
        // OccupiedRect, so the anchor is right whatever the rotation convention is. The footprints are checked before anything spawns, because SpawnBuilding
        // clears its footprint and would destroy a cooling thing in it.
        private string PlaceLinkScene()
        {
            ThingDef towerDef = DefDatabase<ThingDef>.GetNamedSilentFail("CoolingTower");
            ThingDef turbineDef = DefDatabase<ThingDef>.GetNamedSilentFail("Turbine");
            if (towerDef == null || turbineDef == null) return "the CoolingTower or Turbine def is not loaded";
            if (surface == null || lower == null) return "the surface or lower level is missing";
            if (coolingPairCell == IntVec3.Invalid) return "the pairing check did not record the column cell";
            if (!CoolingNoColumns && (FindBuilding(surface, coolingPairCell, "ZL_CoolingColumn") == null || FindBuilding(lower, coolingPairCell, "ZL_CoolingColumn") == null))
            {
                return "a cooling column is missing on the pairing cell";
            }
            IntVec3 c = coolingPairCell;
            // The tower covers the cell west of the surface column, so its pipe touches it. The turbine covers the cell south of the lower column.
            CellRect towerRect = CellRect.FromLimits(c.x - 8, c.z - 5, c.x - 1, c.z + 2);
            CellRect turbineRect = CellRect.FromLimits(c.x - 2, c.z - 7, c.x + 1, c.z - 1);
            if (!RectInside(surface, towerRect) || !RectInside(lower, turbineRect)) return "the link scene is off the map";
            string clash = FirstCoolingThingIn(surface, towerRect) ?? FirstCoolingThingIn(lower, turbineRect);
            if (clash != null) return "the link scene would overlap " + clash;
            IntVec3 towerAnchor = AnchorFor(towerDef.size, towerRect, out bool towerFits);
            IntVec3 turbineAnchor = AnchorFor(turbineDef.size, turbineRect, out bool turbineFits);
            if (!towerFits || !turbineFits) return "the game's footprint does not match the link scene";
            SpawnBuilding(towerDef, surface, towerAnchor);
            SpawnBuilding(turbineDef, lower, turbineAnchor);
            linkTowerCell = towerAnchor;
            linkTurbineCell = turbineAnchor;
            surface.MapUpdate();
            lower.MapUpdate();
            Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
            return null;
        }

        // Writes the scene row once the nets have formed. The tower must share a net with the surface column, and the turbine with the lower column,
        // or the three link rows would test nothing.
        private void RecordLinkScene()
        {
            Thing tower = FindBuilding(surface, linkTowerCell, "CoolingTower");
            Thing turbine = FindBuilding(lower, linkTurbineCell, "Turbine");
            if (CoolingNoColumns)
            {
                // The no-columns variant has no column pair to check, so the row records only that the tower and the turbine stand.
                if (tower == null || turbine == null)
                {
                    Record("cooling link scene", "FAIL: a link building is missing after the settle");
                    return;
                }
                Record("cooling link scene", "PASS: the tower and the turbine stand with no column pair; tower covers " + RectText(tower.OccupiedRect())
                    + " on the surface; turbine covers " + RectText(turbine.OccupiedRect()) + " on the lower level");
                return;
            }
            Thing surfaceColumn = FindBuilding(surface, coolingPairCell, "ZL_CoolingColumn");
            Thing lowerColumn = FindBuilding(lower, coolingPairCell, "ZL_CoolingColumn");
            if (tower == null || turbine == null || surfaceColumn == null || lowerColumn == null)
            {
                Record("cooling link scene", "FAIL: a link building is missing after the settle");
                return;
            }
            object towerNet = CoolingNetOf(tower);
            object turbineNet = CoolingNetOf(turbine);
            bool towerJoined = towerNet != null && ReferenceEquals(towerNet, ColumnNet(surfaceColumn));
            bool turbineJoined = turbineNet != null && ReferenceEquals(turbineNet, ColumnNet(lowerColumn));
            string verdict = towerJoined && turbineJoined
                ? "PASS"
                : "FAIL: the tower joined its column's net " + towerJoined + ", the turbine joined its column's net " + turbineJoined;
            Record("cooling link scene", verdict + "; tower covers " + RectText(tower.OccupiedRect()) + " on the surface; turbine covers "
                + RectText(turbine.OccupiedRect()) + " on the lower level");
        }

        // The first membership read, before the save. It returns whether the lower turbine's net holds the tower in Coolers. A missing building fails the
        // row by name, so a missing tower is not reported as a membership failure.
        private bool RecordSharesRow()
        {
            string missing = ReadLink(out bool holdsTower, out _, out string detail);
            linkSharedMissing = missing;
            string verdict;
            if (missing != null) verdict = "FAIL: " + missing;
            else if (holdsTower) verdict = "PASS";
            else verdict = "FAIL: the lower turbine's cooling net does not hold the surface tower in Coolers";
            RecordCooling("lower turbine shares the surface tower's loop", verdict);
            Record("cooling link shares evidence", "before the save: " + detail);
            return missing == null && holdsTower;
        }

        // The read after the reload. It passes when the tower was in Coolers before the save and is in it after the load, so the membership holds across
        // the reload.
        private void RecordReloadRow()
        {
            string missing = ReadLink(out bool holdsTower, out _, out string detail);
            string verdict;
            if (linkSharedMissing != null) verdict = "FAIL: before the save, " + linkSharedMissing;
            else if (missing != null) verdict = "FAIL: after the load, " + missing;
            else if (linkSharedBeforeSave && holdsTower) verdict = "PASS";
            else verdict = "FAIL: the tower was " + (linkSharedBeforeSave ? "" : "not ") + "in Coolers before the save, and " + (holdsTower ? "is" : "is not") + " after the load";
            RecordCooling("cooling link survives reload", verdict);
            Record("cooling link reload evidence", "before the save: " + (linkSharedMissing ?? ("holds the surface tower " + (linkSharedBeforeSave ? "yes" : "no")))
                + "; after the load and two ticks: " + detail);
        }

        // Reads the link, then removes the surface column on the lower column's cell. The read first is the start of the change, so a passing removal row shows
        // the tower in Coolers before the removal and out of Coolers two ticks after it.
        private void RemoveSurfaceColumn()
        {
            linkRemovalMissing = ReadLink(out bool holdsTower, out _, out _);
            linkHeldBeforeRemoval = linkRemovalMissing == null && holdsTower;
            // The no-columns variant placed no column, so there is nothing to remove.
            if (CoolingNoColumns) return;
            Thing column = FindBuilding(surface, coolingPairCell, "ZL_CoolingColumn");
            if (column == null) throw new InvalidOperationException("the surface column is missing before the removal");
            column.Destroy(DestroyMode.Vanish);
        }

        // The last read. It passes when the tower was in Coolers before the removal, is out of Coolers two ticks after it, and the lower turbine still holds its
        // own turbine in Turbines.
        private void RecordRemovalRow()
        {
            if (CoolingNoColumns)
            {
                RecordCooling("cooling link breaks when a column is removed", "FAIL: the no-columns variant placed no column, so there is no column to remove");
                Record("cooling link removal evidence", "no-columns variant: nothing was removed");
                return;
            }
            string missing = ReadLink(out bool holdsTower, out bool holdsTurbine, out string detail);
            string verdict;
            if (linkRemovalMissing != null) verdict = "FAIL: before the removal, " + linkRemovalMissing;
            else if (missing != null) verdict = "FAIL: after the removal, " + missing;
            else if (!linkHeldBeforeRemoval) verdict = "FAIL: the tower was not in the lower turbine's Coolers before the removal, so no link broke";
            else if (holdsTower) verdict = "FAIL: the tower is still in the lower turbine's Coolers two ticks after the removal";
            else if (!holdsTurbine) verdict = "FAIL: the lower turbine left its own Turbines after the removal";
            else verdict = "PASS";
            RecordCooling("cooling link breaks when a column is removed", verdict);
            Record("cooling link removal evidence", "before the removal: " + (linkRemovalMissing ?? ("holds the surface tower " + (linkHeldBeforeRemoval ? "yes" : "no")))
                + "; the surface column at " + coolingPairCell + " was removed; two ticks after: " + detail);
        }

        // The label of the deconstruction check. The restored column leaves through DestroyMode.Deconstruct, the call that the game's deconstruct job makes.
        // Thing.Destroy calls DeSpawn for that mode, as it does for Vanish, so the link must break the same way.
        private const string DeconstructionLabel = "cooling link breaks when a column is deconstructed";

        // Puts the surface column back on the pairing cell, so the deconstruction check starts from a joined link. The no-columns variant has no column to put back.
        private void RestoreSurfaceColumn()
        {
            if (CoolingNoColumns) return;
            ThingDef column = DefDatabase<ThingDef>.GetNamedSilentFail("ZL_CoolingColumn");
            if (column == null) throw new InvalidOperationException("the ZL_CoolingColumn def is not loaded");
            SpawnBuilding(column, surface, coolingPairCell, ThingDefOf.Steel);
        }

        // Reads the link, then deconstructs the restored surface column. The read first is the start of the change, so a passing row shows the tower in
        // Coolers before the deconstruction and out of Coolers two ticks after it.
        private void DeconstructSurfaceColumn()
        {
            linkDeconstructMissing = ReadLink(out bool holdsTower, out _, out _);
            linkHeldBeforeDeconstruction = linkDeconstructMissing == null && holdsTower;
            if (CoolingNoColumns) return;
            Thing column = FindBuilding(surface, coolingPairCell, "ZL_CoolingColumn");
            if (column == null) throw new InvalidOperationException("the restored surface column is missing before the deconstruction");
            column.Destroy(DestroyMode.Deconstruct);
        }

        // The last read of the deconstruction check. It passes when the tower was in Coolers before the deconstruction, is out of Coolers two ticks after it,
        // and the lower turbine still holds its own turbine in Turbines.
        private void RecordDeconstructionRow()
        {
            if (CoolingNoColumns)
            {
                RecordCooling(DeconstructionLabel, "FAIL: the no-columns variant placed no column, so there is no column to deconstruct");
                return;
            }
            string missing = ReadLink(out bool holdsTower, out bool holdsTurbine, out string detail);
            string verdict;
            if (linkDeconstructMissing != null) verdict = "FAIL: before the deconstruction, " + linkDeconstructMissing;
            else if (missing != null) verdict = "FAIL: after the deconstruction, " + missing;
            else if (!linkHeldBeforeDeconstruction) verdict = "FAIL: the tower was not in the lower turbine's Coolers before the deconstruction, so no link broke";
            else if (holdsTower) verdict = "FAIL: the tower is still in the lower turbine's Coolers two ticks after the deconstruction";
            else if (!holdsTurbine) verdict = "FAIL: the lower turbine left its own Turbines after the deconstruction";
            else verdict = "PASS";
            RecordCooling(DeconstructionLabel, verdict);
            Record("cooling link deconstruction evidence", "before the deconstruction: " + (linkDeconstructMissing ?? ("holds the surface tower " + (linkHeldBeforeDeconstruction ? "yes" : "no")))
                + "; the restored surface column at " + coolingPairCell + " was deconstructed; two ticks after: " + detail);
        }

        // The two-net scene. Each surface column stands on its own surface net with one tower. Both lower columns join one lower net through a coolant pipe,
        // so each surface net must hold both towers in Coolers, once each, and so must the lower net. The scene has no reactor. It runs only under
        // -zlevelscoolingtwonets.
        private const string TwoNetsSceneLabel = "two nets scene";
        private const string TwoNetsSurfaceALabel = "two nets: surface net A holds both towers once";
        private const string TwoNetsSurfaceBLabel = "two nets: surface net B holds both towers once";
        private const string TwoNetsLowerLabel = "two nets: lower net holds both towers once";
        private const string TwoNetsLinkLabel = "two nets link";
        private static readonly string[] TwoNetsRowLabels = { TwoNetsSceneLabel, TwoNetsSurfaceALabel, TwoNetsSurfaceBLabel, TwoNetsLowerLabel, "rimatomics bridge active" };
        private bool twoNetsBuilt;
        private int twoNetsTickMark;
        private Thing twoNetsTowerA;
        private Thing twoNetsTowerB;
        private Thing twoNetsColumnA;
        private Thing twoNetsColumnB;
        private Thing twoNetsLowerColumnA;
        private Thing twoNetsLowerColumnB;

        // Runs the two-net scene. The scene is built on the first call, and its rows are written two ticks later, when the nets have settled.
        private void RunTwoNetsTick()
        {
            try
            {
                if (!twoNetsBuilt)
                {
                    twoNetsBuilt = true;
                    string problem = PlaceTwoNetsScene();
                    if (problem != null)
                    {
                        FailTwoNetsRows(problem);
                        finished = true;
                        Root.Shutdown();
                        return;
                    }
                    twoNetsTickMark = Find.TickManager.TicksGame;
                    return;
                }
                if (Find.TickManager.TicksGame < twoNetsTickMark + 2) return;
                RecordTwoNetsRows();
            }
            catch (Exception exception)
            {
                finished = true;
                try
                {
                    string cause = DescribeOrTypeName(exception);
                    foreach (string label in TwoNetsRowLabels.Append(TwoNetsLinkLabel))
                    {
                        if (!coolingResults.ContainsKey(label)) RecordRowSafely(label, "FAIL: not run: " + cause);
                    }
                    Log.Error("Z-Levels two-net scene failed: " + exception);
                }
                catch (Exception failureException)
                {
                    Log.Error("Z-Levels two-net scene failure rows could not be built: " + failureException);
                }
            }
            finished = true;
            Root.Shutdown();
        }

        // Places the two-net scene. It returns null when the scene stands, and the reason when it does not. The footprints are checked before anything
        // spawns, because SpawnBuilding clears its footprint.
        private string PlaceTwoNetsScene()
        {
            ThingDef towerDef = DefDatabase<ThingDef>.GetNamedSilentFail("CoolingTower");
            ThingDef columnDef = DefDatabase<ThingDef>.GetNamedSilentFail("ZL_CoolingColumn");
            ThingDef pipeDef = DefDatabase<ThingDef>.GetNamedSilentFail("coolingPipe");
            if (towerDef == null || columnDef == null || pipeDef == null) return "the CoolingTower, ZL_CoolingColumn, or coolingPipe def is not loaded";
            ZLevelsManager manager = Current.Game?.GetComponent<ZLevelsManager>();
            if (manager == null) throw new InvalidOperationException("ZLevelsManager is missing from the current game");
            surface = Find.CurrentMap;
            lower = CreateLevelBelow(manager, surface);
            // Column A and column B are two cells apart on both levels. The lower pipe joins the two lower columns, and no pipe joins the surface columns.
            IntVec3 a = surface.Center + new IntVec3(-12, 0, 0);
            IntVec3 b = a + new IntVec3(2, 0, 0);
            IntVec3 between = a + new IntVec3(1, 0, 0);
            // Tower A stands west of column A, and tower B stands east of column B. Each tower touches only its own column.
            CellRect towerARect = CellRect.FromLimits(a.x - 8, a.z - 5, a.x - 1, a.z + 2);
            CellRect towerBRect = CellRect.FromLimits(b.x + 1, b.z - 5, b.x + 8, b.z + 2);
            if (!RectInside(surface, towerARect) || !RectInside(surface, towerBRect) || !a.InBounds(surface) || !b.InBounds(surface)
                || !a.InBounds(lower) || !b.InBounds(lower) || !between.InBounds(lower))
            {
                return "the two-net scene is off the map";
            }
            string clash = FirstCoolingThingIn(surface, towerARect) ?? FirstCoolingThingIn(surface, towerBRect);
            if (clash != null) return "the two-net scene would overlap " + clash;
            IntVec3 towerAAnchor = AnchorFor(towerDef.size, towerARect, out bool towerAFits);
            IntVec3 towerBAnchor = AnchorFor(towerDef.size, towerBRect, out bool towerBFits);
            if (!towerAFits || !towerBFits) return "the game's footprint does not match the two-net scene";
            // The lower cells take a concrete floor, as the round-trip check does, so the pipe and the columns can stand on them.
            foreach (IntVec3 cell in new[] { a, between, b })
            {
                ClearCell(lower, cell);
                lower.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete);
            }
            twoNetsTowerA = SpawnBuilding(towerDef, surface, towerAAnchor);
            twoNetsTowerB = SpawnBuilding(towerDef, surface, towerBAnchor);
            twoNetsColumnA = SpawnBuilding(columnDef, surface, a, ThingDefOf.Steel);
            twoNetsColumnB = SpawnBuilding(columnDef, surface, b, ThingDefOf.Steel);
            twoNetsLowerColumnA = SpawnBuilding(columnDef, lower, a, ThingDefOf.Steel);
            twoNetsLowerColumnB = SpawnBuilding(columnDef, lower, b, ThingDefOf.Steel);
            SpawnBuilding(pipeDef, lower, between);
            surface.MapUpdate();
            lower.MapUpdate();
            Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
            return null;
        }

        // The first two-net building that is missing, or null when all six stand.
        private string MissingTwoNetsBuilding()
        {
            if (twoNetsTowerA == null) return "tower A";
            if (twoNetsTowerB == null) return "tower B";
            if (twoNetsColumnA == null) return "surface column A";
            if (twoNetsColumnB == null) return "surface column B";
            if (twoNetsLowerColumnA == null) return "lower column A";
            if (twoNetsLowerColumnB == null) return "lower column B";
            return null;
        }

        // Writes the scene row, the three net rows, the bridge row, and the aggregate row. A net row reads the net of its tower or its lower column.
        private void RecordTwoNetsRows()
        {
            string missing = MissingTwoNetsBuilding();
            RecordCooling(TwoNetsSceneLabel, missing == null ? "PASS" : "FAIL: " + missing + " is missing after the settle");
            if (missing == null)
            {
                object surfaceNetA = CoolingNetOf(twoNetsTowerA);
                object surfaceNetB = CoolingNetOf(twoNetsTowerB);
                object lowerNet = ColumnNet(twoNetsLowerColumnA);
                RecordCooling(TwoNetsSurfaceALabel, CoolerCountResult(surfaceNetA, "surface net A"));
                RecordCooling(TwoNetsSurfaceBLabel, CoolerCountResult(surfaceNetB, "surface net B"));
                RecordCooling(TwoNetsLowerLabel, CoolerCountResult(lowerNet, "the lower net"));
                Record("two nets counts", "surface net A: " + CoolerCountsText(surfaceNetA) + "; surface net B: " + CoolerCountsText(surfaceNetB)
                    + "; lower net: " + CoolerCountsText(lowerNet));
            }
            else
            {
                RecordCooling(TwoNetsSurfaceALabel, "FAIL: the two-net scene is missing " + missing);
                RecordCooling(TwoNetsSurfaceBLabel, "FAIL: the two-net scene is missing " + missing);
                RecordCooling(TwoNetsLowerLabel, "FAIL: the two-net scene is missing " + missing);
            }
            RecordCooling("rimatomics bridge active", RimatomicsBridgeResult());
            RecordTwoNetsLink();
        }

        // Writes the rows of a scene that did not stand. Each row names the problem. The bridge row still reads the bridge.
        private void FailTwoNetsRows(string problem)
        {
            RecordCooling(TwoNetsSceneLabel, "FAIL: " + problem);
            RecordCooling(TwoNetsSurfaceALabel, "FAIL: " + problem);
            RecordCooling(TwoNetsSurfaceBLabel, "FAIL: " + problem);
            RecordCooling(TwoNetsLowerLabel, "FAIL: " + problem);
            RecordCooling("rimatomics bridge active", RimatomicsBridgeResult());
            RecordTwoNetsLink();
        }

        // Counts each tower in one net's Coolers. Each tower must appear exactly once. A net with no pipe net fails by name.
        private string CoolerCountResult(object net, string role)
        {
            if (net == null) return "FAIL: " + role + " has no pipe net";
            int towerA = CountInList(net, "Coolers", twoNetsTowerA);
            int towerB = CountInList(net, "Coolers", twoNetsTowerB);
            if (towerA == 1 && towerB == 1) return "PASS";
            return "FAIL: " + role + " holds tower A " + Times(towerA) + " and tower B " + Times(towerB) + " in Coolers, and each must be 1";
        }

        // The tower counts of one net, for the evidence row.
        private string CoolerCountsText(object net)
        {
            if (net == null) return "no pipe net";
            return "tower A " + Times(CountInList(net, "Coolers", twoNetsTowerA)) + ", tower B " + Times(CountInList(net, "Coolers", twoNetsTowerB));
        }

        private static string Times(int count)
        {
            return count == 1 ? "1 time" : count + " times";
        }

        // The number of times one thing appears in a net's list, read by name.
        private static int CountInList(object net, string listName, Thing thing)
        {
            int count = 0;
            foreach (object item in (System.Collections.IEnumerable)ReadField(net, listName))
            {
                if (ReferenceEquals(item, thing)) count++;
            }
            return count;
        }

        // The aggregate row. It passes when every row before it passes, and it names the rows that do not.
        private void RecordTwoNetsLink()
        {
            var notPassed = TwoNetsRowLabels.Where(label => !coolingResults.TryGetValue(label, out string result) || result != "PASS").ToList();
            RecordCooling(TwoNetsLinkLabel, notPassed.Count == 0 ? "PASS" : "FAIL: not PASS: " + string.Join("; ", notPassed));
        }

        // The reactor checks. A reactor on the lower level is built after the reload, started with ReactorFuelRods fuel rods, and sampled every 250 ticks for 2500 ticks.
        // Each sample reads the surface tower and the lower turbine. The state is static, because it lasts across the reload.
        private const int ReactorSampleInterval = 250;
        private const int ReactorSampleCount = 11;
        private const int ReactorPowerWaitTicks = 1500;
        // The fuel rods the reactor starts with. The core has 21 fuel slots, and the research sizes a full load at 21 rods.
        private const int ReactorFuelRods = 21;
        private const string UncooledOverheatLabel = "uncooled core overheats";
        private const string CooledOverheatLabel = "cooled core stays below overheating";
        private const string ReactorRunsLabel = "reactor runs on the lower level";
        private const string ReactorRatioLabel = "loop ratio at or below 1";
        private const string ReactorTowerLabel = "surface tower stays in the lower loop";
        private const string ReactorAlertLabel = "no cooling alert";
        private const string ReactorMeltLabel = "no meltdown";
        private const string ReactorColonistLabel = "surface colonist not irradiated";
        private static readonly string[] ReactorLabels =
        {
            ReactorRunsLabel, ReactorRatioLabel, ReactorTowerLabel, ReactorAlertLabel, ReactorMeltLabel, ReactorColonistLabel,
        };
        private static IntVec3 reactorCell = IntVec3.Invalid;
        private static IntVec3 reactorColonistCell = IntVec3.Invalid;
        private static int reactorStartTick = -1;
        private static int reactorSamplesTaken;
        private static string reactorLoopProblem;
        private static string reactorTowerProblem;
        private static string reactorAlertProblem;
        private static string reactorMeltProblem;
        private static int reactorShutdownOffset = -1;
        // The first sample that read IsOverheating, the sample with the peak postReturnTemp, and the reason a sample could not be read.
        private static int reactorOverheatOffset = -1;
        private static int reactorPeakOffset = -1;
        private static float reactorPeakTemp = float.MinValue;
        private static string reactorUnreadProblem;
        // The overheating row of this run. The uncooled run records its positive control, and the cooled run records that the core stays below 500.
        private static string ReactorOverheatLabel => CoolingNoColumns ? UncooledOverheatLabel : CooledOverheatLabel;

        // The core on the lower level, found by its cell. A read after the reload finds the new object.
        private Thing FindReactorCore()
        {
            return FindBuilding(lower, reactorCell, "ReactorCoreA");
        }

        // The surface tower and the lower turbine, each found by its cell.
        private Thing FindLinkTower()
        {
            return FindBuilding(surface, linkTowerCell, "CoolingTower");
        }

        private Thing FindLinkTurbine()
        {
            return FindBuilding(lower, linkTurbineCell, "Turbine");
        }

        private static string YesNo(bool value)
        {
            return value ? "yes" : "no";
        }

        // Reads a public static field by name. A missing field stops the check with the type and the field name.
        private static object StaticField(Type type, string fieldName)
        {
            FieldInfo field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
            if (field == null) throw new MissingFieldException(type.FullName, fieldName);
            return field.GetValue(null);
        }

        // Completes BuildReactorCore before the core spawns, because the core does no work until the step is finished. The Rimatomics types come from
        // the assembly that holds the core class, so this code compiles without a reference to Rimatomics. It returns null when the step reads as
        // finished, and the reason when it does not.
        private static string CompleteReactorResearch(ThingDef coreDef)
        {
            Assembly rimatomics = coreDef.thingClass.Assembly;
            Type researchType = rimatomics.GetType("Rimatomics.RimatomicsResearch", throwOnError: true);
            object research = StaticField(researchType, "_instance");
            if (research == null) return "the Rimatomics research component is missing";
            object step = StaticField(rimatomics.GetType("Rimatomics.DubDef", throwOnError: true), "BuildReactorCore");
            if (step == null) return "DubDef.BuildReactorCore is null";
            MethodInfo complete = researchType.GetMethod("Complete", BindingFlags.Public | BindingFlags.Instance, null, new[] { step.GetType() }, null);
            if (complete == null) return "RimatomicsResearch.Complete(ResearchStepDef) is missing";
            complete.Invoke(research, new[] { step });
            bool finished = (bool)step.GetType().GetProperty("IsFinished", BindingFlags.Public | BindingFlags.Instance).GetValue(step);
            return finished ? null : "BuildReactorCore is not finished after Complete";
        }

        // Builds the reactor scene after the reload. The core and its battery stand on the lower level, and a battery for the tower stands on the surface.
        // The core's footprint touches the turbine's, so the steam and cold-water nets join with no pipe. A colonist stands on the surface cell above the
        // core. It returns null when the scene stands, and the reason when it does not.
        private string PlaceReactorScene()
        {
            ThingDef coreDef = DefDatabase<ThingDef>.GetNamedSilentFail("ReactorCoreA");
            ThingDef batteryDef = DefDatabase<ThingDef>.GetNamedSilentFail("Battery");
            if (coreDef == null || batteryDef == null) return "the ReactorCoreA or Battery def is not loaded";
            if (surface == null || lower == null || coolingPairCell == IntVec3.Invalid) return "the link scene is missing";
            if (FindLinkTower() == null || FindLinkTurbine() == null) return "the link scene's tower or turbine is missing";
            string researchProblem = CompleteReactorResearch(coreDef);
            if (researchProblem != null) return researchProblem;

            IntVec3 c = coolingPairCell;
            CellRect coreRect = CellRect.FromLimits(c.x - 2, c.z - 12, c.x + 2, c.z - 8);
            CellRect lowerBatteryRect = CellRect.FromLimits(c.x - 4, c.z - 10, c.x - 4, c.z - 9);
            CellRect towerBatteryRect = CellRect.FromLimits(c.x - 4, c.z + 3, c.x - 4, c.z + 4);
            if (!RectInside(lower, coreRect) || !RectInside(lower, lowerBatteryRect) || !RectInside(surface, towerBatteryRect))
            {
                return "the reactor scene is off the map";
            }
            string clash = FirstCoolingThingIn(lower, coreRect);
            if (clash != null) return "the reactor scene would overlap " + clash;
            IntVec3 coreAnchor = AnchorFor(coreDef.size, coreRect, out bool coreFits);
            IntVec3 lowerBatteryAnchor = AnchorFor(batteryDef.size, lowerBatteryRect, out bool lowerBatteryFits);
            IntVec3 towerBatteryAnchor = AnchorFor(batteryDef.size, towerBatteryRect, out bool towerBatteryFits);
            if (!coreFits || !lowerBatteryFits || !towerBatteryFits) return "the game's footprint does not match the reactor scene";

            reactorCell = coreAnchor;
            SpawnBuilding(coreDef, lower, coreAnchor);
            ThingWithComps lowerBattery = SpawnBuilding(batteryDef, lower, lowerBatteryAnchor);
            ThingWithComps towerBattery = SpawnBuilding(batteryDef, surface, towerBatteryAnchor);
            lowerBattery.GetComp<CompPowerBattery>().SetStoredEnergyPct(1f);
            towerBattery.GetComp<CompPowerBattery>().SetStoredEnergyPct(1f);
            OpenTowerFootprint();

            reactorColonistCell = new IntVec3(coreAnchor.x, 0, coreAnchor.z);
            if (!reactorColonistCell.InBounds(surface)) return "the colonist cell is off the surface map";
            Pawn colonist = surface.mapPawns.FreeColonistsSpawned.FirstOrDefault();
            if (colonist == null) return "no free colonist is spawned on the surface";
            ClearCell(surface, reactorColonistCell);
            surface.terrainGrid.SetTerrain(reactorColonistCell, TerrainDefOf.Concrete);
            colonist.DeSpawn();
            GenSpawn.Spawn(colonist, reactorColonistCell, surface);
            RestNeeds(colonist);
            colonist.drafter.Drafted = true;

            surface.MapUpdate();
            lower.MapUpdate();
            Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
            Record("reactor scene", "core anchor " + coreAnchor + " on the lower level; lower battery " + lowerBatteryAnchor + "; tower battery "
                + towerBatteryAnchor + " on the surface; colonist " + colonist.Label + " at " + reactorColonistCell);
            return null;
        }

        // The surface map is generated afresh in each run, and a roof over any cell of the tower's footprint stalls the tower, which cuts its capacity to 25000.
        // The reactor scene clears the roof from those cells and records how many it cleared. The Stalled flag clears at the tower's next 250-tick check, and
        // ReactorPowerProblem waits for that before Startup.
        private void OpenTowerFootprint()
        {
            Thing tower = FindLinkTower();
            int roofed = 0;
            foreach (IntVec3 cell in GenAdj.OccupiedRect(tower).Cells)
            {
                if (!GridsUtility.Roofed(cell, surface)) continue;
                surface.roofGrid.SetRoof(cell, (RoofDef)null);
                roofed++;
            }
            Record("reactor tower footprint", roofed + " roofed cells cleared from under the surface tower, so its Stalled flag can clear");
        }

        // Builds the reactor scene. It returns true when the scene stands. Otherwise it records FAIL on each reactor row, with the reason.
        private bool BuildReactor()
        {
            string problem = PlaceReactorScene();
            if (problem == null) return true;
            FailReactorChecks(problem);
            return false;
        }

        // Records FAIL on each reactor row that no row has recorded yet, so a reactor that cannot be built or started fails by name.
        private void FailReactorChecks(string reason)
        {
            foreach (string label in ReactorLabels.Append(ReactorOverheatLabel))
            {
                if (!coolingResults.ContainsKey(label)) RecordRowSafely(label, "FAIL: " + reason);
            }
        }

        // The core and the tower need PowerOn before Startup, and the tower must not be stalled by a roof. It returns null when all hold, and the reason when one does not.
        private string ReactorPowerProblem()
        {
            Thing core = FindReactorCore();
            Thing tower = FindLinkTower();
            if (core == null || tower == null) return "the core or the tower is missing";
            bool corePowered = ((ThingWithComps)core).GetComp<CompPowerTrader>()?.PowerOn ?? false;
            bool towerPowered = ((ThingWithComps)tower).GetComp<CompPowerTrader>()?.PowerOn ?? false;
            bool towerStalled = (bool)ReadField(tower, "Stalled");
            if (corePowered && towerPowered && !towerStalled) return null;
            return "the core is powered " + YesNo(corePowered) + ", the tower is powered " + YesNo(towerPowered) + ", and the tower is stalled " + YesNo(towerStalled);
        }

        // Starts the reactor once both are powered. It returns the next link step: the sampling step once the reactor starts, the power step while the
        // wait lasts, and the removal step when the power never arrives. In that last case the reactor rows fail by name.
        private LinkStep AwaitReactorPower(int ticks)
        {
            string problem = ReactorPowerProblem();
            if (problem == null)
            {
                StartReactor(ticks);
                return LinkStep.ReactorSample;
            }
            if (ticks < linkTickMark + ReactorPowerWaitTicks) return LinkStep.ReactorPower;
            FailReactorChecks("the core and the tower did not get power in time: " + problem);
            return LinkStep.Removal;
        }

        // Starts the reactor as the START button does. One InsertFuel call puts a rod in the first Fuel slot for each of the ReactorFuelRods rods, the control
        // rods are set to 1, and Startup runs. The reactor row reads PASS when IsShutdown is false and FuelCount is the full rod count right after Startup.
        private void StartReactor(int ticks)
        {
            reactorStartTick = ticks;
            Thing core = FindReactorCore();
            if (core == null)
            {
                RecordCooling(ReactorRunsLabel, "FAIL: the reactor core is missing at start");
                return;
            }
            Type coreType = core.GetType();
            object slots = ReadField(core, "SlotDesignations");
            Type rodType = slots.GetType().GetGenericArguments()[0];
            var designations = (System.Collections.IList)slots;
            if (designations.Count < ReactorFuelRods)
            {
                RecordCooling(ReactorRunsLabel, "FAIL: the core has " + designations.Count + " fuel slots, not " + ReactorFuelRods);
                return;
            }
            object fuelDesignation = Enum.Parse(rodType, "Fuel");
            for (int slot = 0; slot < ReactorFuelRods; slot++) designations[slot] = fuelDesignation;
            ThingDef fuelDef = DefDatabase<ThingDef>.GetNamedSilentFail("FuelRods");
            if (fuelDef == null)
            {
                RecordCooling(ReactorRunsLabel, "FAIL: the FuelRods def is not loaded");
                return;
            }
            MethodInfo insert = coreType.GetMethod("InsertFuel", BindingFlags.Public | BindingFlags.Instance);
            for (int rod = 0; rod < ReactorFuelRods; rod++) insert.Invoke(core, new object[] { ThingMaker.MakeThing(fuelDef) });
            int fuelAfterInsert = (int)ReadField(core, "FuelCount");
            coreType.GetField("TargetControlRodTo", BindingFlags.Public | BindingFlags.Instance).SetValue(core, 1f);
            coreType.GetMethod("Startup", BindingFlags.Public | BindingFlags.Instance).Invoke(core, null);
            bool shutdown = (bool)ReadField(core, "IsShutdown");
            int fuel = (int)ReadField(core, "FuelCount");
            RecordCooling(ReactorRunsLabel, !shutdown && fuel == ReactorFuelRods ? "PASS" : "FAIL: IsShutdown is " + YesNo(shutdown) + " and FuelCount is " + fuel + " after Startup, not " + ReactorFuelRods);
            Record("reactor start evidence", "FuelCount " + fuelAfterInsert + " after " + ReactorFuelRods + " InsertFuel calls; TargetControlRodTo 1; IsShutdown " + YesNo(shutdown)
                + " and FuelCount " + fuel + " after Startup");
        }

        // One sample of the reactor checks, taken at offset ticks from Startup. It writes one row with the readings, and it folds the readings into the
        // four checks. Each check keeps the first sample that fails it.
        private void SampleReactor(int ticks)
        {
            int offset = ticks - reactorStartTick;
            Thing core = FindReactorCore();
            Thing tower = FindLinkTower();
            Thing turbine = FindLinkTurbine();
            if (core == null || tower == null || turbine == null)
            {
                string gone = "at +" + offset + " ticks the core, the tower, or the turbine is missing";
                reactorLoopProblem = reactorLoopProblem ?? gone;
                reactorTowerProblem = reactorTowerProblem ?? gone;
                reactorAlertProblem = reactorAlertProblem ?? gone;
                reactorMeltProblem = reactorMeltProblem ?? gone;
                reactorUnreadProblem = reactorUnreadProblem ?? gone;
                Record("reactor sample", "+" + offset + " ticks; the core, the tower, or the turbine is missing");
                return;
            }
            float temperature = (float)ReadField(core, "postReturnTemp");
            bool shutdown = (bool)ReadField(core, "IsShutdown");
            int fuel = (int)ReadField(core, "FuelCount");
            bool melted = (bool)core.GetType().GetProperty("IsCoreMelt", BindingFlags.Public | BindingFlags.Instance).GetValue(core);
            bool overheating = (bool)core.GetType().GetProperty("IsOverheating", BindingFlags.Public | BindingFlags.Instance).GetValue(core);
            object net = CoolingNetOf(turbine);
            float ratio = (float)ReadField(net, "CoolingLoopRatio");
            float capacity = (float)ReadField(net, "CoolingCapacity");
            float uncapped = (float)ReadField(turbine, "UncappedPowerGeneration");
            float uncooled = (float)ReadField(turbine, "UncooledWater");
            List<object> coolers = NetList(turbine, "Coolers");
            bool listed = coolers.Any(cooler => ReferenceEquals(cooler, tower));
            bool stalled = (bool)ReadField(tower, "Stalled");
            bool towerPowered = ((ThingWithComps)tower).GetComp<CompPowerTrader>()?.PowerOn ?? false;
            bool corePowered = ((ThingWithComps)core).GetComp<CompPowerTrader>()?.PowerOn ?? false;
            if (!(ratio <= 1f)) reactorLoopProblem = reactorLoopProblem ?? "at +" + offset + " ticks the ratio is " + ratio.ToString("F4");
            if (!listed) reactorTowerProblem = reactorTowerProblem ?? "at +" + offset + " ticks the surface tower is not in the lower turbine's Coolers";
            if (coolers.Count == 0) reactorAlertProblem = reactorAlertProblem ?? "at +" + offset + " ticks the lower turbine's Coolers is empty";
            if (melted) reactorMeltProblem = reactorMeltProblem ?? "at +" + offset + " ticks IsCoreMelt is true, with postReturnTemp " + temperature.ToString("F1");
            if (shutdown && reactorShutdownOffset < 0) reactorShutdownOffset = offset;
            if (overheating && reactorOverheatOffset < 0) reactorOverheatOffset = offset;
            if (temperature > reactorPeakTemp)
            {
                reactorPeakTemp = temperature;
                reactorPeakOffset = offset;
            }
            Record("reactor sample", "+" + offset + " ticks; postReturnTemp " + temperature.ToString("F1") + "; ratio " + ratio.ToString("F4")
                + "; capacity " + capacity.ToString("F0") + "; uncapped " + uncapped.ToString("F1") + "; uncooled " + uncooled.ToString("F1")
                + "; coolers " + coolers.Count + "; tower listed " + YesNo(listed) + "; tower stalled " + YesNo(stalled)
                + "; tower powered " + YesNo(towerPowered) + "; core powered " + YesNo(corePowered) + "; IsShutdown " + YesNo(shutdown)
                + "; IsOverheating " + YesNo(overheating) + "; fuel " + fuel + "; core melt " + YesNo(melted));
        }

        // Writes the whole-run checks and the colonist check. A check passes when no sample failed it. The trip row says whether IsShutdown turned true.
        private void FinishReactorChecks()
        {
            RecordCooling(ReactorRatioLabel, reactorLoopProblem == null ? "PASS" : "FAIL: " + reactorLoopProblem);
            RecordCooling(ReactorTowerLabel, reactorTowerProblem == null ? "PASS" : "FAIL: " + reactorTowerProblem);
            RecordCooling(ReactorAlertLabel, reactorAlertProblem == null ? "PASS" : "FAIL: " + reactorAlertProblem);
            RecordCooling(ReactorMeltLabel, reactorMeltProblem == null ? "PASS" : "FAIL: " + reactorMeltProblem);
            RecordCooling(ReactorColonistLabel, ColonistResult());
            RecordCooling(ReactorOverheatLabel, OverheatResult());
            Record("reactor peak evidence", reactorPeakOffset < 0
                ? "no sample read the core, so there is no peak"
                : "peak postReturnTemp " + reactorPeakTemp.ToString("F1") + " at +" + reactorPeakOffset + " ticks; first IsOverheating true at "
                    + (reactorOverheatOffset < 0 ? "no sample" : "+" + reactorOverheatOffset + " ticks"));
            Record("reactor trip evidence", reactorShutdownOffset < 0
                ? "IsShutdown stayed false in " + reactorSamplesTaken + " samples, so no trip was seen"
                : "IsShutdown turned true at +" + reactorShutdownOffset + " ticks");
        }

        // The overheating row. The uncooled run passes when a sample at or before +2500 ticks read IsOverheating true, so the heat path is shown to pass 500.
        // The cooled run passes when no sample read IsOverheating, every sample was read, and no sample read IsShutdown as true. A core that trips stops
        // heating, so a pass on temperature alone would not show that the core ran for the full time.
        private string OverheatResult()
        {
            if (CoolingNoColumns)
            {
                if (reactorOverheatOffset >= 0) return "PASS";
                return "FAIL: no sample reached IsOverheating by +2500 ticks" + (reactorPeakOffset < 0 ? "" : ", and the peak postReturnTemp was " + reactorPeakTemp.ToString("F1"));
            }
            if (reactorShutdownOffset >= 0) return "FAIL: IsShutdown was true at +" + reactorShutdownOffset + " ticks, so the core was not running for the full 2500 ticks";
            if (reactorOverheatOffset >= 0) return "FAIL: IsOverheating was true at +" + reactorOverheatOffset + " ticks, with the peak postReturnTemp " + reactorPeakTemp.ToString("F1");
            return reactorUnreadProblem == null ? "PASS" : "FAIL: " + reactorUnreadProblem;
        }

        // The colonist stands on the surface cell above the core, and it must have no RimatomicsRadiation hediff at the end. The core's radiation scans
        // only its own map, so this check is weak by design.
        private string ColonistResult()
        {
            Pawn colonist = FindPawnAt(surface, reactorColonistCell);
            if (colonist == null) return "FAIL: no colonist stands on " + reactorColonistCell + " on the surface at the end";
            HediffDef radiation = DefDatabase<HediffDef>.GetNamedSilentFail("RimatomicsRadiation");
            if (radiation == null) return "FAIL: the RimatomicsRadiation hediff def is not loaded";
            bool irradiated = colonist.health.hediffSet.HasHediff(radiation);
            Record("reactor colonist evidence", colonist.Label + " stands at " + colonist.Position + "; RimatomicsRadiation " + YesNo(irradiated));
            return irradiated ? "FAIL: the colonist on " + reactorColonistCell + " has RimatomicsRadiation" : "PASS";
        }

        // The pawn on the cell, or null when no pawn stands there.
        private static Pawn FindPawnAt(Map map, IntVec3 cell)
        {
            if (map == null || cell == IntVec3.Invalid) return null;
            foreach (Thing thing in cell.GetThingList(map))
            {
                if (thing is Pawn pawn) return pawn;
            }
            return null;
        }

        // Reads the lower turbine's net and reports whether it holds the tower in Coolers and the turbine in Turbines. Each building is found by its cell, so a
        // read after a reload finds the new objects. It returns null when both buildings stand. Otherwise it returns the reason that one is missing, and both
        // flags read false.
        private string ReadLink(out bool holdsTower, out bool holdsTurbine, out string detail)
        {
            holdsTower = false;
            holdsTurbine = false;
            Thing tower = FindBuilding(surface, linkTowerCell, "CoolingTower");
            Thing turbine = FindBuilding(lower, linkTurbineCell, "Turbine");
            if (tower == null && turbine == null) detail = "the surface tower and the lower turbine are both missing from their cells";
            else if (tower == null) detail = "the surface tower is missing from its cell";
            else if (turbine == null) detail = "the lower turbine is missing from its cell";
            else
            {
                List<object> coolers = NetList(turbine, "Coolers");
                List<object> turbines = NetList(turbine, "Turbines");
                holdsTower = coolers.Any(cooler => ReferenceEquals(cooler, tower));
                holdsTurbine = turbines.Any(listed => ReferenceEquals(listed, turbine));
                detail = "Coolers " + coolers.Count + " (holds the surface tower: " + (holdsTower ? "yes" : "no") + "); Turbines " + turbines.Count
                    + " (holds the lower turbine: " + (holdsTurbine ? "yes" : "no") + ")";
                return null;
            }
            return detail;
        }

        // The building of this def on the cell, or null. A loaded save makes new objects, so each read finds its building again by its cell.
        private static Thing FindBuilding(Map map, IntVec3 cell, string defName)
        {
            if (map == null || cell == IntVec3.Invalid) return null;
            foreach (Thing thing in map.thingGrid.ThingsAt(cell))
            {
                if (thing.Spawned && thing.def.defName == defName) return thing;
            }
            return null;
        }

        // The cooling pipe net of a machine, read by name: its Cooling pipe's pipeNetRef. Null when the pipe has no net.
        private static object CoolingNetOf(Thing machine)
        {
            object pipe = ReadField(machine, "Cooling");
            return pipe == null ? null : ReadField(pipe, "pipeNetRef");
        }

        // The pipe net of a cooling column, read by name through its CompCoolingColumn's pipe.
        private static object ColumnNet(Thing column)
        {
            var comp = ((ThingWithComps)column).GetComp<ZLevelsRimatomics.CompCoolingColumn>();
            if (comp == null) throw new InvalidOperationException(column.def.defName + " has no CompCoolingColumn");
            object pipe = ReadField(comp, "pipe");
            return pipe == null ? null : ReadField(pipe, "pipeNetRef");
        }

        // One of a machine's net lists, Coolers or Turbines, read by name.
        private static List<object> NetList(Thing machine, string listName)
        {
            object net = CoolingNetOf(machine);
            if (net == null) throw new InvalidOperationException(machine.def.defName + " has no cooling net");
            var items = new List<object>();
            foreach (object item in (System.Collections.IEnumerable)ReadField(net, listName)) items.Add(item);
            return items;
        }

        // The position whose footprint of this size is the rect. The offset is the game's own footprint at the origin, so no rotation rule is copied here.
        private static IntVec3 AnchorFor(IntVec2 size, CellRect rect, out bool fits)
        {
            CellRect offset = GenAdj.OccupiedRect(IntVec3.Zero, Rot4.North, size);
            IntVec3 anchor = new IntVec3(rect.minX - offset.minX, 0, rect.minZ - offset.minZ);
            CellRect footprint = GenAdj.OccupiedRect(anchor, Rot4.North, size);
            fits = footprint.minX == rect.minX && footprint.maxX == rect.maxX && footprint.minZ == rect.minZ && footprint.maxZ == rect.maxZ;
            return anchor;
        }

        // The first cooling thing that stands on the rect, named with its def and cell, or null when the rect holds none.
        private static string FirstCoolingThingIn(Map map, CellRect rect)
        {
            foreach (IntVec3 cell in rect)
            {
                foreach (Thing thing in cell.GetThingList(map))
                {
                    if (Array.IndexOf(CoolingFootprintDefs, thing.def.defName) >= 0) return thing.def.defName + " at " + thing.Position;
                }
            }
            return null;
        }

        private static bool RectInside(Map map, CellRect rect)
        {
            return new IntVec3(rect.minX, 0, rect.minZ).InBounds(map) && new IntVec3(rect.maxX, 0, rect.maxZ).InBounds(map);
        }

        private static string RectText(CellRect rect)
        {
            return "x " + rect.minX + " to " + rect.maxX + ", z " + rect.minZ + " to " + rect.maxZ;
        }

        // The bridge type is named as a string, so this code compiles without a compile-time reference to ZLevelsRimatomics.
        private static string RimatomicsBridgeResult()
        {
            Type bridge = Type.GetType("ZLevelsRimatomics.RimatomicsBridge, ZLevelsRimatomics");
            if (bridge == null)
            {
                return "FAIL: bridge type missing";
            }
            FieldInfo active = bridge.GetField("Active", BindingFlags.Public | BindingFlags.Static);
            if (active == null)
            {
                return "FAIL: bridge has no Active field";
            }
            return (bool)active.GetValue(null) ? "PASS" : "FAIL: bridge not active";
        }

        // Test only: the other colonists' jobs and work priorities, for a timeout message.
        private string DescribeOthers()
        {
            return string.Join("; ", OtherColonists().Select(c => c.LabelShort + " on " + c.Map + " " + (c.CurJob != null ? c.CurJob.def.defName : "no job")
                + " H" + c.workSettings?.GetPriority(WorkTypeDefOf.Hauling) + " M" + c.workSettings?.GetPriority(WorkTypeDefOf.Mining)
                + " C" + c.workSettings?.GetPriority(WorkTypeDefOf.Construction)));
        }

        // Test only: names of the colonists carrying the harness's logs during the unforced haul, on either level. Each frame of the wait adds
        // the carriers it sees. The haul checks read this set. An all-colonists haul needs one name. An other-colonist haul needs one that is not the validation colonist.
        private void RecordHaulers()
        {
            if (unforcedWood == null) return;
            foreach (Pawn colonist in surface.mapPawns.FreeColonistsSpawned.Concat(lower.mapPawns.FreeColonistsSpawned))
            {
                if (colonist.carryTracker != null && colonist.carryTracker.CarriedThing == unforcedWood) haulersSeen.Add(colonist.LabelShort);
            }
        }

        // Test only: names of the colonists mining the wall, collected while the designation waits. Needs -zlevelsfreecolonists.
        private void RecordMiners()
        {
            if (minedWall == null) return;
            foreach (Pawn colonist in lower.mapPawns.FreeColonistsSpawned)
            {
                if (colonist.CurJob != null && colonist.CurJob.def == JobDefOf.Mine && colonist.CurJob.targetA.Thing == minedWall) minersSeen.Add(colonist.LabelShort);
            }
        }

        // Test only: names of the colonists building the campfire, collected while its blueprint or frame stands. Needs -zlevelsfreecolonists.
        private void RecordBuilders()
        {
            foreach (Pawn colonist in lower.mapPawns.FreeColonistsSpawned)
            {
                Job job = colonist.CurJob;
                if (job != null && job.def == JobDefOf.FinishFrame && job.targetA.Cell == campfireCell) buildersSeen.Add(colonist.LabelShort);
            }
        }

        // Test only: one natural-rock wall on the edge of the lower room gets a mining designation. Only the validation colonist may
        // mine, so the job can only be its own. -zlevelsfreecolonists lifts that.
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
                if (OtherColonistsWork) pawn.workSettings.SetPriority(WorkTypeDefOf.Mining, 0);
                foreach (var colonistMap in new[] { surface, lower, upper })
                {
                    if (colonistMap == null) continue;
                    foreach (var colonist in colonistMap.mapPawns.FreeColonistsSpawned)
                    {
                        if (FreeColonists || OtherColonistsWork || colonist == pawn || colonist.WorkTypeIsDisabled(WorkTypeDefOf.Mining)) continue;
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
            if (OtherColonistsHaul || OtherColonistsWork) pawn.workSettings.SetPriority(WorkTypeDefOf.Hauling, 0);
            belowDuringWait.Clear();
            haulersSeen.Clear();
            Current.Game.GetComponent<ZLevelsManager>().ResetJobTrackerFor(pawn);
            var woodCell = lower.listerThings.AllThings.OfType<Building_StairsUp>().First().Position + new IntVec3(1, 0, 0);
            ClearCell(lower, woodCell);
            lower.terrainGrid.SetTerrain(woodCell, TerrainDefOf.Concrete);
            var logs = ThingMaker.MakeThing(ThingDefOf.WoodLog);
            logs.stackCount = 7;
            unforcedWood = GenSpawn.Spawn(logs, woodCell, lower);
            unforcedWood.SetForbidden(false);
            // Only the harness's logs may be hauled. Unless the all-colonists variant is on, only the validation colonist may haul. Run 20261030-193538 filled the zone with surface wood in one second.
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
                    if (colonist != pawn && !AllColonistsHaul && !FreeColonists && !OtherColonistsWork) colonist.workSettings.SetPriority(WorkTypeDefOf.Hauling, 0);
                }
            }
            // The all-colonists variant starts from a clean lower level, so none may already be there. Proof of the haul is a colonist who carries the logs (RecordHaulers).
            if (AllColonistsHaul) Require(lower.mapPawns.FreeColonistsSpawned.Count == 0, "no colonist below before the unforced haul: " + string.Join(", ", lower.mapPawns.FreeColonistsSpawned.Select(p => p.LabelShort + " on " + p.Map + " job " + (p.CurJob != null ? p.CurJob.def.defName : "none") + " at " + p.Position)));
            if (AllColonistsHaul || OtherColonistsWork) foreach (Pawn other in OtherColonists().ToList()) RestoreWork(other);
            if (OtherColonistsWork)
            {
                Require(OtherColonists().Any(c => !c.WorkTypeIsDisabled(WorkTypeDefOf.Hauling)), "no other colonist can haul");
                Require(OtherColonists().Any(c => !c.WorkTypeIsDisabled(WorkTypeDefOf.Mining)), "no other colonist can mine");
                Require(OtherColonists().Any(c => !c.WorkTypeIsDisabled(WorkTypeDefOf.Construction)), "no other colonist can build");
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

        // Combat patch containment. Each row calls a patched method directly, on the surface, with the lower level in reach, and reads what it
        // returns. A fault that a patch body contains never reaches the caller, so a row that gets an exception back reads FAIL with it.
        private void RecordCombatContainment()
        {
            Building_TurretGun gun = null;
            try
            {
                ThingDef turretDef = DefDatabase<ThingDef>.GetNamed("Turret_MiniTurret");
                Thing turret = ThingMaker.MakeThing(turretDef, turretDef.MadeFromStuff ? GenStuff.DefaultStuffFor(turretDef) : null);
                turret.SetFaction(Faction.OfPlayer);
                gun = (Building_TurretGun)GenSpawn.Spawn(turret, FindOpenCell(surface), surface);
                try
                {
                    LocalTargetInfo found = gun.TryFindNewTarget();
                    Record("combat containment: TryFindNewTarget", "PASS: returned " + found);
                }
                catch (Exception exception)
                {
                    Record("combat containment: TryFindNewTarget", "FAIL: " + DescribeOrTypeName(exception));
                }
                // The stairs are on the lower level, at the same tile as the turret. The shoot line check moves them up for the call.
                Building_StairsUp stairs = lower.listerThings.AllThings.OfType<Building_StairsUp>().First();
                Map ownMap = stairs.Map;
                try
                {
                    bool line = gun.AttackVerb.TryFindShootLineFromTo(gun.Position, new LocalTargetInfo(stairs), out ShootLine resultingLine);
                    if (stairs.Map == ownMap) Record("combat containment: shoot line restores target", "PASS: target back on its own map, shoot line " + line);
                    else Record("combat containment: shoot line restores target", "FAIL: target left on " + stairs.Map + " after the call, not on " + ownMap);
                }
                catch (Exception exception)
                {
                    Record("combat containment: shoot line restores target", "FAIL: " + DescribeOrTypeName(exception) + "; target on " + stairs.Map + ", back on own map: " + (stairs.Map == ownMap));
                }
                // TryCastShot. The caster is a turret on the surface. The target is a second turret on the lower level at the same cell, so the
                // Prefix moves the target up to the caster's map. The Postfix puts it back, and the finalizer does after a fault.
                Thing castCaster = null;
                Thing castTarget = null;
                try
                {
                    IntVec3 castCell = FindSharedOpenCell(surface, lower);
                    castCaster = SpawnMiniTurret(turretDef, castCell, surface);
                    castTarget = SpawnMiniTurret(turretDef, castCell, lower);
                    Verb castVerb = ((Building_TurretGun)castCaster).AttackVerb;
                    typeof(Verb).GetField("currentTarget", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(castVerb, new LocalTargetInfo(castTarget));
                    // The Prefix sets teleportBack when it moves a thing. Clearing it first makes the flag a record of this call alone.
                    TryCastShot_Patch.teleportBack = false;
                    bool returned = false;
                    Exception thrown = null;
                    try
                    {
                        MethodInfo tryCastShot = typeof(Verb_LaunchProjectile).GetMethod("TryCastShot", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                        returned = (bool)tryCastShot.Invoke(castVerb, null);
                    }
                    catch (TargetInvocationException wrapped)
                    {
                        thrown = wrapped.InnerException ?? wrapped;
                    }
                    catch (Exception exception)
                    {
                        thrown = exception;
                    }
                    bool teleported = TryCastShot_Patch.teleportBack;
                    bool targetHome = castTarget.Map == lower;
                    bool casterHome = castCaster.Map == surface;
                    string placement = "; target on " + castTarget.Map + ", back on own map: " + targetHome + "; caster on " + castCaster.Map + ", back on own map: " + casterHome;
                    if (thrown != null) Record("combat containment: TryCastShot restores target", "FAIL: " + DescribeOrTypeName(thrown) + placement);
                    else if (!teleported) Record("combat containment: TryCastShot restores target", "FAIL: the Prefix did not teleport, so this row proves nothing" + placement);
                    else if (!targetHome || !casterHome) Record("combat containment: TryCastShot restores target", "FAIL: a thing was left on the wrong map" + placement);
                    else Record("combat containment: TryCastShot restores target", "PASS: target and caster back on their own maps, prefix teleported, returned " + returned);
                }
                catch (Exception exception)
                {
                    Record("combat containment: TryCastShot restores target", "FAIL: setup: " + DescribeOrTypeName(exception));
                }
                finally
                {
                    // The rows above have recorded where each thing was. A thing left on the other map goes back before it is destroyed.
                    MethodInfo teleport = TeleportThingMethod();
                    if (castTarget != null && castTarget.Map != lower) teleport.Invoke(null, new object[] { castTarget, lower, castTarget.Position });
                    if (castCaster != null && castCaster.Map != surface) teleport.Invoke(null, new object[] { castCaster, surface, castCaster.Position });
                    if (castTarget != null && castTarget.Spawned) castTarget.Destroy(DestroyMode.Vanish);
                    if (castCaster != null && castCaster.Spawned) castCaster.Destroy(DestroyMode.Vanish);
                }
            }
            catch (Exception exception)
            {
                Record("combat containment: setup", "FAIL: " + DescribeOrTypeName(exception));
            }
            finally
            {
                if (gun != null && gun.Spawned) gun.Destroy(DestroyMode.Vanish);
            }
        }

        // The first cell near the middle of the map with nothing on it that a turret can stand on.
        private static IntVec3 FindOpenCell(Map map)
        {
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(map.Center, 15f, true))
            {
                if (cell.InBounds(map) && cell.Walkable(map) && cell.GetThingList(map).Count == 0) return cell;
            }
            throw new InvalidOperationException("no open cell near the middle of " + map);
        }

        // The cell nearest the middle of the second map that is open on both maps, so a turret can stand at it on each map. The lower
        // level is mostly rock, so the search starts from its middle. It stops at radius 79.5, inside the radial pattern's limit of 79.8.
        private static IntVec3 FindSharedOpenCell(Map first, Map second)
        {
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(second.Center, 79.5f, true))
            {
                if (cell.InBounds(first) && cell.InBounds(second) && cell.Walkable(first) && cell.Walkable(second)
                    && cell.GetThingList(first).Count == 0 && cell.GetThingList(second).Count == 0) return cell;
            }
            throw new InvalidOperationException("no cell open on both " + first + " and " + second);
        }

        // A mini turret made from its default stuff, as the TryFindNewTarget row makes its turret, placed at the cell on the map.
        private static Building_TurretGun SpawnMiniTurret(ThingDef turretDef, IntVec3 cell, Map map)
        {
            Thing turret = ThingMaker.MakeThing(turretDef, turretDef.MadeFromStuff ? GenStuff.DefaultStuffFor(turretDef) : null);
            turret.SetFaction(Faction.OfPlayer);
            return (Building_TurretGun)GenSpawn.Spawn(turret, cell, map);
        }

        private static void Record(string scenario, string result)
        {
            var line = DateTime.UtcNow.ToString("O") + " | " + scenario + " | " + result;
            File.AppendAllText(Path.Combine(GenFilePaths.SaveDataFolderPath, "validation-results.txt"), line + Environment.NewLine);
            // The row is in the results file before the copy to the game log. A failed copy does not make a caller write the row again.
            // A failed copy is recorded as its own FAIL row, so the run fails. Only a failed results write throws out of this method.
            try
            {
                Log.Message("[ZLevels validation] " + line);
            }
            catch (Exception copyException)
            {
                // DescribeOrTypeName names the cause, and it names the Describe failure too if Describe throws. The copy row is written either way.
                string detail = DescribeOrTypeName(copyException);
                File.AppendAllText(Path.Combine(GenFilePaths.SaveDataFolderPath, "validation-results.txt"),
                    DateTime.UtcNow.ToString("O") + " | log copy | FAIL: could not copy the row to Player.log: " + detail + Environment.NewLine);
            }
        }
    }
}
