# Work search profile Implementation Plan

> **For agentic workers:** Execute inline in this session, as superpowers:executing-plans says. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Find where the colonist job search spends its time on one, two and three floors. The profile times the mod's own steps and the vanilla search they call. No mod code changes.

**Architecture:** A new validation phase, `-WorkSearchProfile`, builds the three-floor colony that `-SearchTiming` builds. For each floor count it times the search twice: once with no timers (the baseline), then with timers that the harness adds at run time to a fixed list of methods. Each timer counts only the outermost call. The timers are removed in a `finally` block before the phase writes its rows.

**Tech Stack:** C# harness in `tools/RuntimeValidation`, Harmony through the reference the harness already has, and the PowerShell runner `tools/Invoke-RuntimeValidation.ps1`.

**Spec:** Decision D9 in `docs/roadmap.md`. Lucas answered on 2026-11-08 with (a): profile the work search's own code, as a new plan, with no mod change. The method follows `docs/superpowers/plans/2026-11-07-task24-patch-timing.md`.

## Global Constraints

- No change to `ZLevels/` mod code. The phase adds timers in `tools/RuntimeValidation/` only.
- Validate only in isolated savedata and a new colony. Never open an existing save or change the user's mod list.
- Record what the timers show. Do not remove a timer or hide a row to get a green run.
- Each name below was checked against the mod source or the RimWorld 1.7 decompile. A name that does not match is a plan defect: fix the table, and record a ruling in the ledger.

## Review Focus

- Nested calls. `JobGiver_Work.TryIssueJobPackage` calls itself through the own-floor search. `Search` contains `SearchOtherFloors`, which contains `RunVanillaSearch`. Each probe counts and times only its outermost activation, so no call is counted twice.
- A call that throws. Harmony runs a finalizer whether or not the original throws, so the finalizer lowers each timer's depth. A fault cannot leave a timer stuck at depth one.
- A search that moves the colonist. `FloorRestore` puts the colonist back. Its `Dispose` is a probe, so a move that is not undone shows up as a timed call.
- Lazy sequences. `FloorPolicy.Allowed` and `ZUtils.GetAllMapsInClosestOrder` return sequences that do their work when read. Their timers count the calls that build the sequence, and nothing more.

## Task 1: Timers and the phase

**Files:**
- Create: `tools/RuntimeValidation/WorkSearchProfileScenarios.cs`
- Modify: `tools/RuntimeValidation/RuntimeValidation.cs` (a dispatch branch after the `zlevelssearchtiming` branch)
- Modify: `tools/Invoke-RuntimeValidation.ps1` (the switch, the `$scenarioSwitches` entry, the argument line, and the required label `work search profile`)

**Interfaces:**
- Consumes: the setup of `RunSearchTimingCheck` in `tools/RuntimeValidation/MultiFloorsScenarios.cs`: `Rand.PushState(20261106)`, `FindCentralOpenBlock`, `SpawnColonistNear`, `CreateLevelBelow`, `tracker.CreateUpperLevel`, `RequireFloors`, and `TimeWorkSearches` with `AdvanceClockPastIdlePause`.
- Produces: `private void RunWorkSearchProfile()`, which sets `finished`, records the rows in Step 6, and calls `Root.Shutdown()`.

**Probes.** Each name was read from its source. The table gives the file and line.

| Probe | Method (signature) | Source | Timed as |
|---|---|---|---|
| P1 | `JobGiver_Work.TryIssueJobPackage(Pawn pawn, JobIssueParams jobParams)`, `public override` | game 1.7, decompile line 49 | inclusive, the whole call |
| P2 | `OwnFloorFirstSearch.Search(JobGiver_Work instance, bool emergency, Pawn pawn, JobIssueParams jobParams, JobTracker jobTracker, Map oldMap, IntVec3 oldPosition)`, `internal static` | `ZLevels/Source/ZLevels/ZLevels/HarmonyPatches/OwnFloorFirstPatch.cs`, line 25 | inclusive |
| P3 | `OwnFloorFirstSearch.RunVanillaSearch(JobGiver_Work instance, Pawn pawn, JobIssueParams jobParams, JobTracker jobTracker, Map map, Func<WorkGiver, bool> filter)`, `private static` | same file, line 94 | inclusive, and a count of the maps searched |
| P4 | `OwnFloorFirstSearch.SearchOtherFloors(JobGiver_Work instance, Pawn pawn, JobIssueParams jobParams, JobTracker jobTracker, Map oldMap, IntVec3 oldPosition, Func<WorkGiver, bool> filter)`, `private static` | same file, line 72 | inclusive |
| P5 | `ZUtils.TeleportThing(Thing thing, Map map, IntVec3 position)`, `public static` | `ZLevels/Source/ZLevels/ZLevels/ZUtils.cs`, line 329 | inclusive, and a count |
| P6 | `ZLevelsManager.ResetJobTrackerFor(Pawn pawn)`, `public` | `ZLevels/Source/ZLevels/ZLevels/ZLevelsManager.cs`, line 1068 | inclusive, and a count |
| P7 | `FloorPolicy.Allowed(Pawn pawn, IEnumerable<Map> maps)`, `public`, lazy | `ZLevels/Source/ZLevels/ZLevels/FloorPolicy.cs`, line 109 | a count only |
| P8 | `ZUtils.GetAllMapsInClosestOrder(Thing thing, Map oldMap, IntVec3 oldPosition, bool skipOldMap = false, bool dontCheckForStairs = false)`, `public static`, lazy | `ZLevels/Source/ZLevels/ZLevels/ZUtils.cs`, line 74 | a count only |
| P9 | `FloorRestore.Dispose()`, `public` | `ZLevels/Source/ZLevels/ZLevels/FloorRestore.cs`, line 33 | inclusive, and a count |

Step 1 checks the names again before the first run.

- [ ] **Step 1: Check the names.** Run `grep -rn "static ThinkResult SearchOtherFloors\|static ThinkResult RunVanillaSearch\|internal static ThinkResult Search(" ZLevels/Source/ZLevels/ZLevels` and the same kind of search for the other rows. Expected: one line per probe, with the parameters in the table. If one differs, change the table and record a ruling.
- [ ] **Step 2: Timers.** `WorkSearchTimer` is a static class in the harness. For each probe it keeps a depth counter, a start time, a total in timestamp ticks, and a count. `Enter(MethodBase __originalMethod)` is the prefix. It raises the depth, and at depth one it records the start time. `Exit(MethodBase __originalMethod)` is the finalizer. It lowers the depth. When the depth returns to zero, it adds the elapsed ticks and one count. Harmony runs a finalizer after a normal return and after a fault, so both paths lower the depth.
- [ ] **Step 3: Install and remove.** `Install()` creates `new Harmony("zlevels.validation.worksearchprofile")`. For each probe it calls `harmony.Patch(method, prefix: new HarmonyMethod(enter) { priority = HarmonyLib.Priority.First }, finalizer: new HarmonyMethod(typeof(WorkSearchTimer), nameof(WorkSearchTimer.Exit)))`. The internal and private methods are found with `AccessTools.TypeByName("ZLevels.OwnFloorFirstSearch")` and `AccessTools.Method`. `Remove()` calls `harmony.Unpatch(method, enter)` and `harmony.Unpatch(method, exit)` for each probe, then `Harmony.GetPatchInfo(method)` for each probe, and records whether any patch of ours is left. The phase calls `Remove()` in a `finally`. Ruling (2026-11-08): Harmony runs a higher priority first (`PatchSorter.cs` and `PatchInfoSerialization.PriorityComparer` in 0Harmony.dll), and a patch with no priority gets 400 (`Patch.cs`). The mod's prefix on `TryIssueJobPackage` sets no priority, so it runs at 400. Run `20261108-112712` set no priority, so the tie kept the mod's prefix first. Run `20261108-113101` set priority 0, which is `Priority.Last` and runs after that prefix. Both counted each nested vanilla call as a second call. The timer prefix now uses `Priority.First` (800).
- [ ] **Step 4: Measure.** For one, two and three floors, in this order: a baseline with no timers, then the same calls with timers. Each is three rounds of 200 calls, as `TimeWorkSearches` runs them: ten colonists, twenty calls each. The timers are reset at the start of each timed round. Record, per floor count and round: the mean microseconds per call of P1 with no timers and with timers; the share of P1 that each inclusive probe takes; and the counts per call of P3, P5, P6, P7, P8 and P9.
- [ ] **Step 5: Overhead.** The overhead is the timed P1 mean minus the baseline mean, per round. Record it as an INFO row. It is a cost of the instrument, so the shares are read as shares of the timed run.
- [ ] **Step 6: Rows.** Record one INFO row per floor count with the numbers above, and these rows:
   - `work search profile recorded` is PASS when P1 counted every call in each round, P2 has a count above zero at two and three floors, and no patch of ours is left after `Remove`.
   - `work search profile containment` is PASS when, in every round, the P2 total is no more than the P1 total, the P4 total is no more than the P2 total, and the P3 total is no more than the P2 total. Nesting makes these hold for each call, so a failure means a timer is wrong.
   - `work search profile` is the aggregate row that the runner requires. It is PASS when the two rows above pass, and FAIL with the first reason otherwise.
- [ ] **Step 7: Runner.** Add `[switch]$WorkSearchProfile`, the `WorkSearchProfile = $WorkSearchProfile` entry in `$scenarioSwitches`, the line `if ($WorkSearchProfile) { $arguments += '-zlevelsworksearchprofile' }`, and the clause `elseif ($WorkSearchProfile) { 'work search profile' }` in `$requiredPass`. Confirm the runner parses with `[System.Management.Automation.Language.Parser]::ParseFile`.
- [ ] **Step 8: Run it.** `powershell -NoProfile -ExecutionPolicy Bypass -File tools/Invoke-RuntimeValidation.ps1 -WorkSearchProfile`. Expected: `Runtime validation passed. Evidence: <run folder>`. Read every row in `validation-results.txt`.

## Task 2: Repeat and record

**Files:**
- Modify: `docs/validation-report.md` (a section, "Work search profile (D9, 2026-11-08)")
- Modify: `docs/roadmap.md` (D9 recorded as answered, with its date and the run folders)

- [ ] **Step 1: Repeat.** Run the phase twice more. Record the three run folders.
- [ ] **Step 2: Table.** One table: the floor count, the mean microseconds of P1 with no timers with its spread over the three runs, and each inclusive probe's share of P1 with its spread. Count-only probes give calls per search instead.
- [ ] **Step 3: Findings.** State what the shares show. Name the largest share and the step it belongs to, and name the parts that were not timed. Do not propose a mod change in this plan.
- [ ] **Step 4: Record.** In `docs/roadmap.md`, D9 is "answered 2026-11-08: (a)", with the profile result in one line and its run folders. The choice of the next speed step is a new decision for Lucas, and its default is no change.

## Outcome

Filled in when the plan is done: the run folders, the table, and any row that failed, with its reason.
