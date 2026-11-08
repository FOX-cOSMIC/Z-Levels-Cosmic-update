using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using ZLevels;

namespace ZLevelsValidation
{
    // Task 33 (idea "call colonists here" on docs/mod-comparison/ideas.html). The order is reached by name, so this file builds before the order exists,
    // and the rows fail until it does. Two calls are made.
    //   The walker is a colonist on the lower floor, locked to that floor. It is called to a free cell on the surface. It must stand on that cell
    //   within 6000 ticks, and its lock must then name the surface.
    //   The stayer is a drafted colonist standing on the surface. It is called to its own cell. The order must send it nowhere, and no route job may
    //   appear for it while the walker walks.
    public partial class RuntimeValidation
    {
        // The walker must stand on its cell before this many ticks pass.
        private const int CallColonistsTicks = 6000;

        private int callStage;
        private bool callDone;
        private int callStartTick;
        private int callDueTick;
        private int callNextBeat;
        private int callSurfaceZ;
        private int callLowerZ;
        private Map callSurface;
        private Map callLower;
        private Pawn callWalker;
        private Pawn callStayer;
        private IntVec3 callCell;
        private IntVec3 callStayCell;
        private bool callWalkSent;
        private bool callGizmoOffered;
        private string callStayImmediate = "";
        private bool callStayBroken;
        private string callStayDetail = "";
        private string callLastTrace = "";

        // The order, reached by name. A missing order throws, so the first row reads FAIL with the reason.
        private static List<Pawn> CallColonistsTo(IEnumerable<Pawn> colonists, IntVec3 cell, Map map)
        {
            Type order = typeof(FloorPolicy).Assembly.GetType("ZLevels.CallColonistsTargeter")
                ?? throw new InvalidOperationException("the mod has no ZLevels.CallColonistsTargeter class");
            System.Reflection.MethodInfo method = order.GetMethod("CallTo", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                ?? throw new InvalidOperationException("CallColonistsTargeter has no public static CallTo");
            try
            {
                return (List<Pawn>)method.Invoke(null, new object[] { colonists.ToList(), cell, map });
            }
            catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException != null)
            {
                throw exception.InnerException;
            }
        }

        // A route job of the Z-Levels kind, held as the current job, in the queue, or in the tracker's list of jobs still to start.
        private static bool HasRouteJob(Pawn pawn)
        {
            bool isRoute(Job job) => job != null && job.def != null && job.def.defName == "ZL_GoToMap";
            if (isRoute(pawn.CurJob) || pawn.jobs.jobQueue.Any(queued => isRoute(queued.job)))
            {
                return true;
            }
            ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
            return tracker.jobTracker != null && tracker.jobTracker.TryGetValue(pawn, out var jobTracker)
                && jobTracker.activeJobs != null && jobTracker.activeJobs.Any(isRoute);
        }

        private static string DescribeCallFloor(Map map, Map surface, Map lower)
        {
            return map == null ? "no floor" : map == surface ? "the surface" : map == lower ? "the lower floor" : "another floor";
        }

        private void SetUpCallColonists()
        {
            callSurface = Find.CurrentMap;
            ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
            RequireFloors(tracker, callSurface, 1);
            Building_StairsUp stairs = SetUpLowerStairs(callSurface, tracker, out callLower);
            callLower.fogGrid.ClearAllFog();
            RequireFloors(tracker, callSurface, 2);
            // The open ground around the stairs on the lower floor. The carve is centred on the stairs, as in the other floor scenarios.
            CarveAround(callLower, stairs.Position, 6f);
            callSurfaceZ = tracker.GetZIndexFor(callSurface);
            callLowerZ = tracker.GetZIndexFor(callLower);

            // A clear 9 by 9 block on the surface holds both colonists and the walker's cell. The block must not cover the stairs, because ClearCell
            // removes what it clears.
            IntVec3 block = FindStandableBlock(callSurface, 9, 9);
            if (!block.IsValid)
            {
                throw new InvalidOperationException("no standable 9 by 9 block on the surface away from the edges");
            }
            int offsetX = stairs.Position.x - block.x;
            int offsetZ = stairs.Position.z - block.z;
            if (offsetX >= 0 && offsetX < 9 && offsetZ >= 0 && offsetZ < 9)
            {
                throw new InvalidOperationException("the clear block on the surface covers the stairs");
            }
            for (int i = 0; i < 9; i++)
            {
                for (int j = 0; j < 9; j++)
                {
                    ClearCell(callSurface, block + new IntVec3(i, 0, j));
                }
            }

            List<IntVec3> avoid = new List<IntVec3>();
            callStayer = SpawnCapableColonistNear(callSurface, block + new IntVec3(2, 0, 2), avoid);
            callStayer.workSettings.DisableAll();
            callStayer.drafter.Drafted = true;
            callStayCell = callStayer.Position;

            // The button is offered on a selected player colonist. The row is recorded before the order is called, so it fails on its own when the
            // button is missing.
            callGizmoOffered = callStayer.GetGizmos().OfType<Command_Action>().Any(gizmo => gizmo.defaultLabel == "Call colonists here");
            Record("call colonists gizmo is offered to a selected colonist", callGizmoOffered
                ? "PASS: the colonist's gizmos hold Call colonists here"
                : "FAIL: the colonist's gizmos hold no Call colonists here button");

            callWalker = SpawnCapableColonistNear(callLower, stairs.Position + new IntVec3(2, 0, 2), avoid);
            callWalker.workSettings.DisableAll();
            callCell = OnMapCell(callSurface, block + new IntVec3(6, 0, 6));
            if (!callCell.Standable(callSurface) || callCell == callStayCell)
            {
                throw new InvalidOperationException("the walker's cell " + callCell + " is not standable or is taken");
            }
            // The walker is locked to the lower floor before the call, so the lock must move with the call for the last row to pass.
            tracker.floorPolicy.SetLock(callWalker, callLowerZ, int.MaxValue);

            // The stayer is called to the cell it stands on. Nothing may be sent, and nothing may be started for it.
            List<Pawn> sentStayer = CallColonistsTo(new List<Pawn> { callStayer }, callStayCell, callSurface);
            callStayImmediate = sentStayer.Count == 0 && callStayer.Map == callSurface && callStayer.Position == callStayCell && !HasRouteJob(callStayer)
                ? "PASS"
                : "FAIL: the call sent the colonist already on the spot, or gave it a route job";

            // The walker is called to the free cell on the surface.
            callStartTick = Find.TickManager.TicksGame;
            List<Pawn> sentWalker = CallColonistsTo(new List<Pawn> { callWalker }, callCell, callSurface);
            callWalkSent = sentWalker.Contains(callWalker);
            callDueTick = callStartTick + CallColonistsTicks;
            callNextBeat = callStartTick;
            callStage = 1;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
        }

        private void RunCallColonistsTick()
        {
            if (callDone)
            {
                return;
            }
            try
            {
                if (callStage == 0)
                {
                    SetUpCallColonists();
                    return;
                }
                if (!callStayBroken)
                {
                    string trouble = null;
                    if (callStayer.Map != callSurface || callStayer.Position != callStayCell)
                    {
                        trouble = "the colonist on the spot moved to " + callStayer.Position + " on " + DescribeCallFloor(callStayer.Map, callSurface, callLower);
                    }
                    else if (HasRouteJob(callStayer))
                    {
                        trouble = "the colonist on the spot was given a route, job " + DescribeJob(callStayer.CurJob);
                    }
                    if (trouble != null)
                    {
                        callStayBroken = true;
                        callStayDetail = trouble + " at tick " + Find.TickManager.TicksGame;
                    }
                }
                string trace = "the walker's job is " + DescribeJob(callWalker.CurJob) + " on " + DescribeCallFloor(callWalker.Map, callSurface, callLower);
                if (trace != callLastTrace)
                {
                    callLastTrace = trace;
                    Record("job trace", "INFO: tick " + Find.TickManager.TicksGame + ", " + trace);
                }
                if (Find.TickManager.TicksGame >= callNextBeat)
                {
                    callNextBeat = Find.TickManager.TicksGame + 600;
                    Record("heartbeat", "INFO: tick " + Find.TickManager.TicksGame + ", speed " + Find.TickManager.CurTimeSpeed + ", the walker at " + callWalker.Position
                        + " on " + DescribeCallFloor(callWalker.Map, callSurface, callLower) + ", the colonist on the spot at " + callStayer.Position + "; " + trace);
                }
                bool arrived = callWalker.Map == callSurface && callWalker.Position == callCell;
                bool gone = callWalker.Destroyed || callWalker.Dead;
                if (arrived || gone || Find.TickManager.TicksGame > callDueTick)
                {
                    FinishCallColonists(arrived);
                }
            }
            catch (Exception exception)
            {
                callDone = true;
                Record("colonist on the lower floor reaches the spot within 6000 ticks", "FAIL: " + exception.Message);
                Record("colonists already at the spot stay there", "FAIL: not checked, the setup or the call failed");
                Record("a locked colonist keeps its lock on the floor it is called to", "FAIL: not checked, the setup or the call failed");
                Record("call colonists", "FAIL: " + exception.Message);
                Log.Error("Z-Levels call colonists validation failed: " + exception);
                finished = true;
                Root.Shutdown();
            }
        }

        private void FinishCallColonists(bool arrived)
        {
            callDone = true;
            ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
            int ticks = Find.TickManager.TicksGame - callStartTick;
            string reach = arrived
                ? "PASS: the colonist stood on the spot after " + ticks + " ticks"
                : "FAIL: after " + ticks + " ticks the colonist is on " + DescribeCallFloor(callWalker.Map, callSurface, callLower) + " at " + callWalker.Position
                    + ", the job is " + DescribeJob(callWalker.CurJob) + (callWalkSent ? "" : "; the call did not send the colonist");
            Record("colonist on the lower floor reaches the spot within 6000 ticks", reach);

            bool stayed = callStayImmediate == "PASS" && !callStayBroken && callStayer.Map == callSurface && callStayer.Position == callStayCell;
            string stayReason = callStayImmediate != "PASS" ? callStayImmediate
                : callStayDetail.Length > 0 ? callStayDetail
                : "the colonist on the spot is on " + DescribeCallFloor(callStayer.Map, callSurface, callLower) + " at " + callStayer.Position;
            Record("colonists already at the spot stay there", stayed
                ? "PASS: the colonist on the spot was sent nowhere and was given no route"
                : "FAIL: " + stayReason);

            // After the call the walker's lock names the surface, and the lock holds at this tick.
            int? lockedNow = tracker.floorPolicy.LockedFloorIndex(callWalker, Find.TickManager.TicksGame);
            bool lockMoved = lockedNow == callSurfaceZ;
            Record("a locked colonist keeps its lock on the floor it is called to", lockMoved
                ? "PASS: the lock names the surface, floor index " + callSurfaceZ
                : "FAIL: the lock names " + (lockedNow == null ? "no floor" : "floor index " + lockedNow.Value) + ", expected the surface, floor index " + callSurfaceZ);

            bool all = arrived && stayed && lockMoved && callWalkSent && callGizmoOffered;
            Record("call colonists", all ? "PASS" : "FAIL: a row above failed");
            finished = true;
            Root.Shutdown();
        }
    }
}
