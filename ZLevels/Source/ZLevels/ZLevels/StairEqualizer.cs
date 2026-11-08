using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ZLevels
{
    // Idea "heat and gas through stairs" (docs/mod-comparison/ideas.html). The rooms that a pair of stairs joins on two floors trade heat, and the gas
    // on the stairs cell passes between the floors. The game calls StepAll every 60 ticks, and each pair is stepped once, from the end on the upper
    // floor. A room that takes its temperature from the weather is left alone. A pair whose other end is gone moves nothing.
    public static class StairEqualizer
    {
        // The temperature gap that closes in one game day at medium speed: 10 degrees a day. Slow closes half that rate and fast four times it, so a
        // 10 degree gap closes in two days at slow, in one day at medium, and in a quarter of a day at fast (DM12 in the multifloors plan).
        private const float DegreesPerDayAtMedium = 10f;

        // The share of the gas difference that moves in one 60 tick step at medium speed.
        private const float GasSharePerStepAtMedium = 0.1f;

        // The game's own call interval for StepAll.
        private const int StepTicks = 60;

        // The gas types in the order of GasGrid's packed densities, which AddGas and SetDirect also use.
        private static readonly GasType[] Gases = { GasType.BlindSmoke, GasType.ToxGas, GasType.RotStink, GasType.DeadlifeDust };

        // The speed multiplier for a setting: 0 slow, 1 medium (the default), 2 fast. Values outside that range are clamped.
        public static float SpeedMultiplier(int setting)
        {
            switch (Math.Max(0, Math.Min(2, setting)))
            {
                case 0:
                    return 0.5f;
                case 2:
                    return 4f;
                default:
                    return 1f;
            }
        }

        // Called every StepTicks ticks. Each pair of stairs is stepped once, from its end on the upper floor.
        public static void StepAll(ZLevelsManager tracker)
        {
            if (tracker == null)
            {
                return;
            }
            foreach (Map map in Find.Maps.ToList())
            {
                foreach (Building_Stairs stairs in tracker.GetStairsDownFor(map).ToList())
                {
                    Step(stairs, StepTicks);
                }
            }
        }

        // One step for one pair. Ticks is the time that the step stands for.
        public static void Step(Building_Stairs pair, int ticks)
        {
            if (pair == null || !pair.Spawned || ticks <= 0)
            {
                return;
            }
            Building_Stairs other = ZUtils.ZTracker.GetOtherEnd(pair);
            if (other == null)
            {
                return;
            }
            float multiplier = SpeedMultiplier(ZLevelsMod.settings.StairsEqualizeSpeed);
            StepHeat(pair.Position.GetRoom(pair.Map), other.Position.GetRoom(other.Map), multiplier, ticks);
            StepGas(pair, other, multiplier, ticks);
        }

        // Heat. The gap between the two rooms closes by DegreesPerDayAtMedium × multiplier × ticks / TicksPerDay, never more than the gap itself.
        // Each room takes half of the closing, so the gap shrinks by exactly that amount and does not overshoot.
        private static void StepHeat(Room a, Room b, float multiplier, int ticks)
        {
            if (a == null || b == null || a == b || a.UsesOutdoorTemperature || b.UsesOutdoorTemperature)
            {
                return;
            }
            float gap = a.Temperature - b.Temperature;
            if (gap == 0f)
            {
                return;
            }
            float closing = Math.Min(Math.Abs(gap), DegreesPerDayAtMedium * multiplier * ticks / GenDate.TicksPerDay);
            float share = closing / 2f;
            float sign = Math.Sign(gap);
            a.Temperature -= sign * share;
            b.Temperature += sign * share;
        }

        // Gas. On each type, the stairs cell on one floor and the stairs cell on the other move the share of their difference that the speed allows.
        private static void StepGas(Building_Stairs pair, Building_Stairs other, float multiplier, int ticks)
        {
            float share = Math.Min(1f, GasSharePerStepAtMedium * multiplier * ticks / StepTicks);
            IntVec3 cellA = pair.Position;
            IntVec3 cellB = other.Position;
            foreach (GasType gas in Gases)
            {
                int densityA = pair.Map.gasGrid.DensityAt(cellA, gas);
                int densityB = other.Map.gasGrid.DensityAt(cellB, gas);
                int move = (int)Math.Round((densityA - densityB) * share, MidpointRounding.AwayFromZero);
                if (move > 0)
                {
                    MoveGas(pair.Map, cellA, other.Map, cellB, gas, move);
                }
                else if (move < 0)
                {
                    MoveGas(other.Map, cellB, pair.Map, cellA, gas, -move);
                }
            }
        }

        // Moves up to amount of one gas between two cells. The receiving cell decides how much it takes, and the sending cell gives up exactly that
        // amount, so the total of the gas stays the same. A receiving cell that takes nothing, such as a gas that the game refuses, moves nothing.
        private static void MoveGas(Map fromMap, IntVec3 fromCell, Map toMap, IntVec3 toCell, GasType gas, int amount)
        {
            if (!toMap.gasGrid.GasCanMoveTo(toCell))
            {
                return;
            }
            int before = toMap.gasGrid.DensityAt(toCell, gas);
            toMap.gasGrid.AddGas(toCell, gas, amount, canOverflow: false);
            int taken = toMap.gasGrid.DensityAt(toCell, gas) - before;
            if (taken <= 0)
            {
                return;
            }
            TakeGas(fromMap, fromCell, gas, taken);
        }

        // Lowers one gas on one cell. SetDirect writes the four packed densities, so all four are read first.
        private static void TakeGas(Map map, IntVec3 cell, GasType gas, int amount)
        {
            GasGrid grid = map.gasGrid;
            int[] densities =
            {
                grid.DensityAt(cell, GasType.BlindSmoke),
                grid.DensityAt(cell, GasType.ToxGas),
                grid.DensityAt(cell, GasType.RotStink),
                grid.DensityAt(cell, GasType.DeadlifeDust)
            };
            int index = Array.IndexOf(Gases, gas);
            densities[index] = Math.Max(0, densities[index] - amount);
            grid.SetDirect(cell, (byte)densities[0], (byte)densities[1], (byte)densities[2], (byte)densities[3]);
            map.mapDrawer.MapMeshDirty(cell, MapMeshFlagDefOf.Gas);
        }
    }
}
