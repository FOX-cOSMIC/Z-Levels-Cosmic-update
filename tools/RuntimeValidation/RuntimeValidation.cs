using System;
using System.IO;
using System.Linq;
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
        }

        public override void GameComponentUpdate()
        {
            if (!GenCommandLine.CommandLineArgPassed("zlevelsvalidation") || finished)
                return;
            try
            {
                if (DateTime.UtcNow > deadline)
                    throw new InvalidOperationException("Timed out at stage " + stage + ", pawn job=" + pawn?.CurJob);
                if (Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting)
                    return;
                Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
                switch (stage)
                {
                    case 0:
                        surface = Find.CurrentMap;
                        pawn = surface.mapPawns.FreeColonistsSpawned.First();
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
                        Next(5);
                        GameDataSaveLoader.SaveGame("ZLevelsValidation");
                        Record("save three-level colony", "PASS");
                        LongEventHandler.ExecuteWhenFinished(() => GameDataSaveLoader.LoadGame("ZLevelsValidation"));
                        break;
                    case 5:
                        if (!reloaded) return;
                        Require(surface != null && lower != null && upper != null && pawn != null, "saved references resolved");
                        Require(Current.Game.GetComponent<ZLevelsManager>().GetAllMaps(surface.Tile).Count == 3, "levels survive reload");
                        Require(pawn.Map == surface, "pawn survives reload on surface");
                        Record("reload three-level colony", "PASS");
                        var reloadedDown = surface.listerThings.AllThings.OfType<Building_StairsDown>().First();
                        Next(14);
                        reloadedDown.GiveJob(pawn, reloadedDown);
                        break;
                    case 14:
                        if (pawn.Map == surface) return;
                        Require(Current.Game.GetComponent<ZLevelsManager>().GetZIndexFor(pawn.Map) == -1, "stairs after reload lead to lower level");
                        Record("reload stairs down", "PASS");
                        var reloadedUp = pawn.Map.listerThings.AllThings.OfType<Building_StairsUp>().First();
                        Next(15);
                        reloadedUp.GiveJob(pawn, reloadedUp);
                        break;
                    case 15:
                        if (pawn.Map != surface) return;
                        Require(Current.Game.GetComponent<ZLevelsManager>().GetAllMaps(surface.Tile).Count == 3, "levels tracked after reload stairs");
                        Record("reload stairs return", "PASS");
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
