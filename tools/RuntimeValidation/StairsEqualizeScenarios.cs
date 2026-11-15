using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using ZLevels;

namespace ZLevelsValidation
{
    // Task 34 (idea "heat and gas through stairs" on docs/mod-comparison/ideas.html). A walled room with a roof stands on each floor, around the stairs.
    // The surface room is 30 degrees and the lower room 20, and smoke lies on the surface stairs cell. The equalizer is reached by name, so this file
    // builds before it exists, and the rows fail until it does. Every row is measured within one frame, so no game tick runs between two readings.
    public partial class RuntimeValidation
    {
        // The stairs stand at the centre of a 5 by 5 box. The ring of the box is wall, and the 3 by 3 interior is roofed.
        private const int StairsRoomRadius = 2;

        // The speed setting's values, as the settings window stores them. Medium is the default.
        private const int EqualizeSlow = 0;
        private const int EqualizeMedium = 1;
        private const int EqualizeFast = 2;

        // Calls the equalizer by name: StairEqualizer.Step(Building_Stairs pair, int ticks). A missing class throws, and the row reports it.
        private static void CallStairEqualizer(Building_Stairs pair, int ticks)
        {
            Type equalizer = typeof(FloorPolicy).Assembly.GetType("ZLevels.StairEqualizer")
                ?? throw new InvalidOperationException("the mod has no ZLevels.StairEqualizer class");
            System.Reflection.MethodInfo step = equalizer.GetMethod("Step",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, new[] { typeof(Building_Stairs), typeof(int) }, null)
                ?? throw new InvalidOperationException("StairEqualizer has no public static Step(Building_Stairs, int)");
            try
            {
                step.Invoke(null, new object[] { pair, ticks });
            }
            catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException != null)
            {
                throw exception.InnerException;
            }
        }

        // Writes the speed setting by name, so a missing setting fails the row that needs it.
        private static void SetEqualizeSpeed(int speed)
        {
            object settings = typeof(FloorPolicy).Assembly.GetType("ZLevels.ZLevelsMod")
                .GetField("settings", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).GetValue(null);
            System.Reflection.FieldInfo field = settings.GetType().GetField("StairsEqualizeSpeed", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                ?? throw new InvalidOperationException("the settings have no StairsEqualizeSpeed");
            field.SetValue(settings, speed);
        }

        // Puts the default speed back after a row. A failure here is not raised, because the row that set the speed has already reported it.
        private static void RestoreEqualizeSpeed()
        {
            try
            {
                SetEqualizeSpeed(EqualizeMedium);
            }
            catch (Exception)
            {
            }
        }

        // The ring of the 5 by 5 box is wall. The 3 by 3 interior is roofed. The centre holds the stairs, so it is not cleared.
        private static void BuildStairsRoom(Map map, IntVec3 centre)
        {
            for (int dx = -StairsRoomRadius; dx <= StairsRoomRadius; dx++)
            {
                for (int dz = -StairsRoomRadius; dz <= StairsRoomRadius; dz++)
                {
                    IntVec3 cell = centre + new IntVec3(dx, 0, dz);
                    if (!cell.InBounds(map))
                    {
                        throw new InvalidOperationException("the stairs room cell " + cell + " is off the map");
                    }
                    if (cell != centre)
                    {
                        ClearCell(map, cell);
                    }
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) == StairsRoomRadius)
                    {
                        GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.Steel), cell, map);
                    }
                    else
                    {
                        map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);
                    }
                }
            }
        }

        private void RunStairsEqualizeCheck()
        {
            const string label = "stairs equalize";
            Map surface = null;
            Map lower = null;
            Room upperRoom = null;
            Room lowerRoom = null;
            Building_StairsDown upperStairs = null;
            IntVec3 centre = IntVec3.Invalid;
            try
            {
                surface = Find.CurrentMap;
                ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
                RequireFloors(tracker, surface, 1);
                Building_StairsUp lowerStairs = SetUpLowerStairs(surface, tracker, out lower);
                lower.fogGrid.ClearAllFog();
                RequireFloors(tracker, surface, 2);
                centre = lowerStairs.Position;
                CarveAround(lower, centre, 6f);
                BuildStairsRoom(surface, centre);
                BuildStairsRoom(lower, centre);
                upperStairs = centre.GetThingList(surface).OfType<Building_StairsDown>().FirstOrDefault()
                    ?? throw new InvalidOperationException("the surface has no stairs down at " + centre);
                upperRoom = centre.GetRoom(surface);
                lowerRoom = centre.GetRoom(lower);
                if (upperRoom == null || lowerRoom == null || upperRoom == lowerRoom || upperRoom.UsesOutdoorTemperature || lowerRoom.UsesOutdoorTemperature)
                {
                    throw new InvalidOperationException("the stairs do not join two separate indoor rooms");
                }
                upperRoom.Temperature = 30f;
                lowerRoom.Temperature = 20f;
            }
            catch (Exception exception)
            {
                Record("temperature gap between joined rooms shrinks", "FAIL: " + exception.Message);
                Record("gas moves through the stairs", "FAIL: not checked, the setup failed");
                Record("speed setting changes the rate", "FAIL: not checked, the setup failed");
                Record("nothing moves when the stairs are removed", "FAIL: not checked, the setup failed");
                Record(label, "FAIL: " + exception.Message);
                Log.Error("Z-Levels stairs equalize validation failed during setup: " + exception);
                Root.Shutdown();
                return;
            }

            // Heat. At the default speed, 6000 ticks close 1 degree of a 10 degree gap (medium closes it in one game day).
            string gapRow;
            float gapBefore = upperRoom.Temperature - lowerRoom.Temperature;
            try
            {
                CallStairEqualizer(upperStairs, 6000);
                float gapAfter = upperRoom.Temperature - lowerRoom.Temperature;
                gapRow = gapAfter < gapBefore - 0.001f
                    ? "PASS: the gap closed from " + gapBefore.ToString("F2") + " to " + gapAfter.ToString("F2")
                    : "FAIL: the gap is " + gapAfter.ToString("F2") + " after the step, it was " + gapBefore.ToString("F2");
            }
            catch (Exception exception)
            {
                gapRow = "FAIL: " + exception.Message;
            }
            Record("temperature gap between joined rooms shrinks", gapRow);

            // Gas. Smoke is used, because the other gases need an expansion that the validation mod list does not load. One step of 60 ticks moves
            // a tenth of the difference, so 200 smoke gives 20 on the lower stairs cell.
            string gasRow;
            try
            {
                int lowerBefore = lower.gasGrid.DensityAt(centre, GasType.BlindSmoke);
                surface.gasGrid.AddGas(centre, GasType.BlindSmoke, 200, canOverflow: false);
                int upperAmount = surface.gasGrid.DensityAt(centre, GasType.BlindSmoke);
                if (upperAmount != 200)
                {
                    throw new InvalidOperationException("the surface stairs cell holds " + upperAmount + " smoke after adding 200");
                }
                CallStairEqualizer(upperStairs, 60);
                int lowerAfter = lower.gasGrid.DensityAt(centre, GasType.BlindSmoke);
                gasRow = lowerAfter > lowerBefore
                    ? "PASS: the lower stairs cell took " + lowerAfter + " smoke from the surface"
                    : "FAIL: the lower stairs cell holds " + lowerAfter + " smoke after one step";
            }
            catch (Exception exception)
            {
                gasRow = "FAIL: " + exception.Message;
            }
            Record("gas moves through the stairs", gasRow);

            // Speed. Slow closes 5 degrees a day and fast 40, so 6000 ticks close 0.5 degrees at slow and 4 at fast.
            string speedRow;
            try
            {
                SetEqualizeSpeed(EqualizeSlow);
                float slowBefore = upperRoom.Temperature - lowerRoom.Temperature;
                CallStairEqualizer(upperStairs, 6000);
                float slowClosed = slowBefore - (upperRoom.Temperature - lowerRoom.Temperature);
                SetEqualizeSpeed(EqualizeFast);
                float fastBefore = upperRoom.Temperature - lowerRoom.Temperature;
                CallStairEqualizer(upperStairs, 6000);
                float fastClosed = fastBefore - (upperRoom.Temperature - lowerRoom.Temperature);
                speedRow = Math.Abs(slowClosed - 0.5f) < 0.01f && Math.Abs(fastClosed - 4f) < 0.01f
                    ? "PASS: 6000 ticks close " + slowClosed.ToString("F2") + " degrees at slow and " + fastClosed.ToString("F2") + " at fast"
                    : "FAIL: 6000 ticks close " + slowClosed.ToString("F2") + " degrees at slow, expected 0.50, and " + fastClosed.ToString("F2") + " at fast, expected 4.00";
            }
            catch (Exception exception)
            {
                speedRow = "FAIL: " + exception.Message;
            }
            finally
            {
                RestoreEqualizeSpeed();
            }
            Record("speed setting changes the rate", speedRow);

            // Removed stairs. With the lower stairs gone, no reading may change.
            string removedRow;
            try
            {
                float upperBefore = upperRoom.Temperature;
                float lowerBefore = lowerRoom.Temperature;
                int upperSmoke = surface.gasGrid.DensityAt(centre, GasType.BlindSmoke);
                int lowerSmoke = lower.gasGrid.DensityAt(centre, GasType.BlindSmoke);
                Building_StairsUp lowerNow = centre.GetThingList(lower).OfType<Building_StairsUp>().FirstOrDefault()
                    ?? throw new InvalidOperationException("no stairs up is left on the lower floor");
                lowerNow.Destroy(DestroyMode.Vanish);
                CallStairEqualizer(upperStairs, 6000);
                bool unchanged = upperRoom.Temperature == upperBefore && lowerRoom.Temperature == lowerBefore
                    && surface.gasGrid.DensityAt(centre, GasType.BlindSmoke) == upperSmoke
                    && lower.gasGrid.DensityAt(centre, GasType.BlindSmoke) == lowerSmoke;
                removedRow = unchanged
                    ? "PASS: the temperatures and the smoke stayed the same"
                    : "FAIL: a reading changed after the stairs were removed";
            }
            catch (Exception exception)
            {
                removedRow = "FAIL: " + exception.Message;
            }
            Record("nothing moves when the stairs are removed", removedRow);

            bool all = gapRow.StartsWith("PASS") && gasRow.StartsWith("PASS") && speedRow.StartsWith("PASS") && removedRow.StartsWith("PASS");
            Record(label, all ? "PASS" : "FAIL: a row above failed");
            Root.Shutdown();
        }
    }
}
