using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;
using ZLevels;

namespace ZLevelsValidation
{
    // Task 38 (idea "stair transfer" on docs/mod-comparison/ideas.html; decision D5, answered 2026-11-06: send a chosen list now; decision DM20).
    // A stairs piece has a transfer button. The window it opens lists the free colonists and the loose items on that floor. Send walks the checked
    // colonists to the landing cell on the other floor, and marks the checked items for that floor. The window is reached by name and reflection, so
    // this file builds before it exists, and each row FAILs until its part does. The scenario is frame-driven, as the haul scenario is.
    //   The button is on the stairs (row 1). The window lists the colonists and items (row 2). Send walks the checked colonist up within 6000
    //   ticks (row 3). Send marks the checked steel for the other floor (row 4). A checked item the other floor does not need is counted and is not
    //   marked (row 5). An unchecked colonist and an unchecked item stay on the lower floor (row 6). A checked item that has left the floor is
//   counted as gone, is not marked, and does not stop Send (row 7).
    public partial class RuntimeValidation
    {
        private const string TransferButtonLabel = "Transfer across the stairs";
        private const int StairTransferTicks = 6000;
        private const string StairTransferAggregate = "stair transfer";

        // The seven rows, in the order the run records them. The aggregate is recorded after them.
        private static readonly string[] StairTransferRowLabels =
        {
            "transfer button is on the stairs",
            "window lists the colonists and items on the floor",
            "send walks the checked colonist to the other floor",
            "send marks the checked item for the other floor",
            "an item the other floor does not need is not marked",
            "unchecked colonist and item stay where they are",
            "a checked item that left the floor is skipped and counted",
        };

        private int transferStage;
        private bool transferDone;
        private Map transferSurface;
        private Map transferLower;
        private Building_StairsUp transferFrom;
        private Building_Stairs transferTo;
        private Pawn transferSent;
        private Pawn transferStayed;
        private Thing transferSteel;
        private Thing transferWoodNotNeeded;
        private Thing transferWoodStayed;
        private IntVec3 transferLanding;
        private int transferDueTick;
        private object transferResult;
        private readonly List<string> transferRecorded = new List<string>();
        private readonly List<string> transferResults = new List<string>();

        // The window's class, by name. A missing class throws, so the row reads FAIL with the reason.
        private static Type TransferWindowType()
        {
            return typeof(FloorPolicy).Assembly.GetType("ZLevels.StairTransferWindow")
                ?? throw new InvalidOperationException("the mod has no ZLevels.StairTransferWindow class");
        }

        // A public static method of the window class. A missing method throws.
        private static object CallTransferWindow(string name, params object[] args)
        {
            MethodInfo method = TransferWindowType().GetMethod(name, BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException("StairTransferWindow has no public static " + name);
            try
            {
                return method.Invoke(null, args);
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                throw exception.InnerException;
            }
        }

        // A list the window method returns, as a list of T.
        private static List<T> CallTransferList<T>(string name, params object[] args)
        {
            object value = CallTransferWindow(name, args);
            return ((IEnumerable)value).Cast<T>().ToList();
        }

        // A public count of the transfer result, by field or property name. A missing member throws.
        private static int TransferCount(object result, string name)
        {
            if (result == null)
            {
                throw new InvalidOperationException("Send returned no transfer result");
            }
            System.Reflection.FieldInfo field = result.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field != null)
            {
                return (int)field.GetValue(result);
            }
            System.Reflection.PropertyInfo property = result.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property != null)
            {
                return (int)property.GetValue(result, null);
            }
            throw new InvalidOperationException("the transfer result has no " + name);
        }

        private void RecordTransferRow(string label, string result)
        {
            transferRecorded.Add(label);
            transferResults.Add(result);
            Record(label, result);
        }

        // Runs one row's check and records its result. A check that throws records its reason as the FAIL.
        private void RecordTransferRowChecked(string label, Func<string> check)
        {
            string result;
            try
            {
                result = check();
            }
            catch (Exception exception)
            {
                result = "FAIL: " + exception.Message;
            }
            RecordTransferRow(label, result);
        }

        // The lower level and its stairs, a surface stockpile that takes steel only, and the items and colonists on the lower floor. Rows 1 and 2
        // are checked here, before anything is sent.
        private void SetUpStairTransfer()
        {
            transferSurface = Find.CurrentMap;
            ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
            RequireFloors(tracker, transferSurface, 1);
            transferFrom = SetUpLowerStairs(transferSurface, tracker, out transferLower);
            transferLower.fogGrid.ClearAllFog();
            transferSurface.fogGrid.ClearAllFog();
            RequireFloors(tracker, transferSurface, 2);
            transferTo = tracker.GetOtherEnd(transferFrom)
                ?? throw new InvalidOperationException("the lower stairs have no other end on the surface");

            // Open ground around both ends, so the stairs stay reachable and the landing cell is standable. CarveAround skips its centre, so the
            // stairs stay.
            CarveAround(transferLower, transferFrom.Position, 6f);
            CarveAround(transferSurface, transferTo.Position, 6f);

            // The surface stockpile takes steel only, so the surface needs steel and does not need wood, as the haul scenario sets it up.
            Zone_Stockpile stock = MakeSupplyZone(transferSurface, OnMapCell(transferSurface, transferTo.Position + new IntVec3(4, 0, 4)));
            stock.settings.filter.SetDisallowAll();
            stock.settings.filter.SetAllow(ThingDefOf.Steel, true);
            if (stock.Cells.Count == 0)
            {
                throw new InvalidOperationException("no stockpile cells on the surface");
            }

            // Open cells on the lower floor, two and a half to five cells from the stairs. Nothing on the lower floor stores these items.
            List<IntVec3> spots = GenRadial.RadialCellsAround(transferFrom.Position, 5f, false)
                .Where(cell => cell.InBounds(transferLower) && cell.Standable(transferLower) && cell.GetEdifice(transferLower) == null
                    && cell.GetFirstItem(transferLower) == null && cell.DistanceTo(transferFrom.Position) >= 2.5f)
                .ToList();
            if (spots.Count < 5)
            {
                throw new InvalidOperationException("fewer than five open cells near the lower stairs");
            }
            transferSteel = SpawnStackOn(transferLower, spots[0], ThingDefOf.Steel);
            transferWoodNotNeeded = SpawnStackOn(transferLower, spots[1], ThingDefOf.WoodLog);
            transferWoodStayed = SpawnStackOn(transferLower, spots[2], ThingDefOf.WoodLog);

            // Two colonists beside the stairs. Hauling and mining are off for both, so nothing moves them but the transfer.
            List<IntVec3> avoid = spots.Take(3).ToList();
            avoid.Add(transferFrom.Position);
            transferSent = SpawnCapableColonistNear(transferLower, transferFrom.Position, avoid, WorkTypeDefOf.Hauling, WorkTypeDefOf.Mining);
            SetWork(transferSent, WorkTypeDefOf.Hauling, 0, WorkTypeDefOf.Mining, 0);
            avoid.Add(transferSent.Position);
            transferStayed = SpawnCapableColonistNear(transferLower, transferFrom.Position, avoid, WorkTypeDefOf.Hauling, WorkTypeDefOf.Mining);
            SetWork(transferStayed, WorkTypeDefOf.Hauling, 0, WorkTypeDefOf.Mining, 0);

            RecordTransferRowChecked(StairTransferRowLabels[0], () =>
            {
                bool button = transferFrom.GetGizmos().OfType<Command>().Any(command => command.defaultLabel == TransferButtonLabel);
                return button
                    ? "PASS: the stairs piece has a transfer button"
                    : "FAIL: the stairs piece has no \"" + TransferButtonLabel + "\" button";
            });
            RecordTransferRowChecked(StairTransferRowLabels[1], () =>
            {
                List<Pawn> colonists = CallTransferList<Pawn>("ColonistsOn", transferLower);
                List<Thing> items = CallTransferList<Thing>("ItemsOn", transferLower);
                bool listed = colonists.Contains(transferSent) && colonists.Contains(transferStayed)
                    && items.Contains(transferSteel) && items.Contains(transferWoodNotNeeded) && items.Contains(transferWoodStayed);
                return listed
                    ? "PASS: the window lists both colonists and all three items on the lower floor"
                    : "FAIL: the window lists " + colonists.Count + " colonists and " + items.Count + " items, not the ones on the floor";
            });
            transferStage = 1;
        }

        // Send with the checked colonist and the two checked items. The steel is marked, the wood is counted as not needed, and the unchecked
        // colonist and wood are not in the call. Rows 4 and 5 are checked at once. Row 3 waits for the colonist.
        private void SendStairTransfer()
        {
            transferLanding = (IntVec3)CallTransferWindow("LandingCell", transferTo);
            transferResult = CallTransferWindow("Send", transferFrom, new List<Pawn> { transferSent }, new List<Thing> { transferSteel, transferWoodNotNeeded });
            int surfaceFloor = Current.Game.GetComponent<ZLevelsManager>().GetZIndexFor(transferSurface);
            int colonistsSent = TransferCount(transferResult, "ColonistsSent");
            int marked = TransferCount(transferResult, "ItemsMarked");
            int notNeeded = TransferCount(transferResult, "ItemsNotNeeded");
            Record("stair transfer counts", "INFO: sent " + colonistsSent + " colonist, marked " + marked + " item, and " + notNeeded
                + " item was not needed on the floor above; the landing cell is " + transferLanding);

            RecordTransferRowChecked(StairTransferRowLabels[3], () => ItemFloorMarks.IsMarkedFor(transferSteel, surfaceFloor) && marked == 1
                ? "PASS: the steel is marked for the surface"
                : "FAIL: the steel is not marked for the surface, and " + marked + " items were marked");
            RecordTransferRowChecked(StairTransferRowLabels[4], () =>
            {
                bool woodMarked = ItemFloorMarks.IsMarkedFor(transferWoodNotNeeded, surfaceFloor);
                bool woodOnLower = transferWoodNotNeeded.MapHeld == transferLower;
                return !woodMarked && woodOnLower && notNeeded == 1
                    ? "PASS: the wood the surface does not need is counted as not needed, is not marked, and is on the lower floor"
                    : "FAIL: the wood is marked " + woodMarked + ", is on " + transferWoodNotNeeded.MapHeld + ", and " + notNeeded + " items were not needed";
            });

            // Row 7: a checked item that has left the floor is counted as gone and is not marked, and Send still returns. A steel stack that was never
            // spawned stands in for one a colonist has picked up: its Map is null in both cases.
            RecordTransferRowChecked(StairTransferRowLabels[6], () =>
            {
                Thing left = ThingMaker.MakeThing(ThingDefOf.Steel);
                object gone = CallTransferWindow("Send", transferFrom, new List<Pawn>(), new List<Thing> { left });
                int goneCount = TransferCount(gone, "ItemsGone");
                int markedGone = TransferCount(gone, "ItemsMarked");
                return goneCount == 1 && markedGone == 0
                    ? "PASS: the item that left the floor is counted as gone and not marked"
                    : "FAIL: " + goneCount + " items counted as gone and " + markedGone + " marked";
            });
            transferDueTick = Find.TickManager.TicksGame + StairTransferTicks;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
            transferStage = 2;
        }

        // Row 3: the colonist stands on the landing cell within the time limit. The row is recorded when it arrives or when the limit passes.
        private void WaitForStairTransfer()
        {
            bool arrived = transferSent.MapHeld == transferSurface && transferSent.Position == transferLanding;
            if (!arrived && Find.TickManager.TicksGame <= transferDueTick)
            {
                return;
            }
            RecordTransferRowChecked(StairTransferRowLabels[2], () => arrived
                ? "PASS: the colonist stands on the landing cell " + transferLanding + " above"
                : "FAIL: after " + StairTransferTicks + " ticks the colonist is at " + transferSent.Position + " on " + transferSent.MapHeld
                    + ", the landing cell is " + transferLanding + "; the job is " + DescribeJob(transferSent.CurJob));
            transferStage = 3;
        }

        // Row 6, then the aggregate. The unchecked colonist and the unchecked wood must still be on the lower floor and unmarked.
        private void FinishStairTransfer()
        {
            int surfaceFloor = Current.Game.GetComponent<ZLevelsManager>().GetZIndexFor(transferSurface);
            RecordTransferRowChecked(StairTransferRowLabels[5], () =>
            {
                bool colonistOnLower = transferStayed.MapHeld == transferLower;
                bool woodOnLower = transferWoodStayed.MapHeld == transferLower && !ItemFloorMarks.IsMarkedFor(transferWoodStayed, surfaceFloor);
                return colonistOnLower && woodOnLower
                    ? "PASS: the unchecked colonist and the unchecked wood are on the lower floor"
                    : "FAIL: the unchecked colonist is on " + transferStayed.MapHeld + " and the unchecked wood on " + transferWoodStayed.MapHeld;
            });
            bool allPass = transferRecorded.Count == StairTransferRowLabels.Length && transferResults.All(result => result.StartsWith("PASS"));
            Record(StairTransferAggregate, allPass ? "PASS" : "FAIL: a row above failed");
            transferDone = true;
            finished = true;
            Root.Shutdown();
        }

        // A failure ends the run. Every row not yet recorded fails with the reason, so no row is left out.
        private void FailStairTransfer(Exception exception)
        {
            transferDone = true;
            foreach (string missing in StairTransferRowLabels.Where(label => !transferRecorded.Contains(label)).ToList())
            {
                RecordTransferRow(missing, "FAIL: " + exception.Message);
            }
            Record(StairTransferAggregate, "FAIL: " + exception.Message);
            Log.Error("Z-Levels stair transfer validation failed: " + exception);
            finished = true;
            Root.Shutdown();
        }

        private void RunStairTransferTick()
        {
            if (transferDone)
            {
                return;
            }
            try
            {
                switch (transferStage)
                {
                    case 0:
                        SetUpStairTransfer();
                        return;
                    case 1:
                        SendStairTransfer();
                        return;
                    case 2:
                        WaitForStairTransfer();
                        return;
                    default:
                        FinishStairTransfer();
                        return;
                }
            }
            catch (Exception exception)
            {
                FailStairTransfer(exception);
            }
        }
    }
}
