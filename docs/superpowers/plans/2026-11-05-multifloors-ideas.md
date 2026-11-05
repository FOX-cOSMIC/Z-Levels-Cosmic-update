# MultiFloors Ideas Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Chosen execution: native, inline in this session. Reason: the tasks share `JobPatches.cs`, `ZLevelsManager.cs` and one validation harness, and each needs a serial validation run, so parallel agents would collide.

**Goal:** Implement every idea and the four bugs on `docs/mod-comparison/ideas.html`. Each change is proven by a validation run whose named rows fail before the change and pass after it.

**Architecture:** Keep the stairs model and the per-level job-list patches. Add three small pieces: `FloorPolicy` for per-colonist and per-floor rules, `StorageIndex` for the storage lookup, and `StairRouteCache` for stair choice. Each piece saves its state with the same Scribe lists that `ZLevelsManager` already uses. Replace the copied all-floors job search (`JobPatches.cs:1987-2319`) with "own floor first, then the normal search on each other floor". The large features (ladders and elevators, pipes, the stair transfer tab) wait for the questions D3 to D5.

**Tech Stack:** C# on the RimWorld 1.7.4998 API, Harmony (the game's copy, never shipped), Verse XML defs, the PowerShell runner `tools/Invoke-RuntimeValidation.ps1`, `tools/RuntimeValidation/RuntimeValidation.cs`, and the new `tools/RuntimeValidation/MultiFloorsScenarios.cs`.

**Spec:** `docs/mod-comparison/ideas.html` (the ideas and the bugs). Scope: `docs/superpowers/specs/2026-10-27-zlevels-17-design.md`. Open decisions: `docs/roadmap.md`.

## Global Constraints

- Target RimWorld 1.7.4998 rev1299 on the Steam unstable branch. Not 1.6.
- Keep the namespace `ZLevels`, the types XML uses, and the subsystem boundaries. Package id `foxcosmic.zlevels17`.
- Leave `ZLevels/1.1`, `1.2` and `1.3` unchanged. 1.7 loads through `ZLevels/LoadFolders.xml`.
- Ship no game DLLs and no second Harmony. `0MultiplayerAPI.dll` ships as the older versions do.
- Keep `HarmonyPatches/JobPatches - Copy.cs` out of the build.
- Multiplayer stays optional and unverified. Claim no multiplayer compatibility.
- Validate only in isolated savedata and a new colony. Never open existing saves or change the user's mod list.
- Keep attribution to RamRod. Upstream has no license, and this port grants no redistribution rights. MultiFloors has no license either, so its ideas are rebuilt here, never copied.
- A gameplay change is proven only by a validation run that prints `Runtime validation passed. Evidence: <run folder>`. Test-first means a scenario whose named rows FAIL before the change and PASS after it.
- Record failures and untested cases as they are. Never disable a patch or hide an error to get a green run.
- Check every Harmony attribute and reflected name against the installed 1.7 code (`.artifacts/decomp-1.7.4998`, or `ilspycmd` for anything missing). Record confirmed names in `docs/game-api-findings.md`.
- Add as few Harmony patches as the feature needs. Never copy a whole game method to change one step.
- Commit only your own paths. Other sessions' uncommitted work is listed in Task 0. Stay on `master`. Push only when Lucas asks. Never force-push.

## Review Focus

1. A colonist whose job target is taken, or whose reservation is refused, while walking to the stairs searches again and never pauses the game. Tests: Task 4 and Task 23 rows.
2. A colonist with no rest target, or a rest search where no floor gives a job, lies down or rests normally with no logged error. Test: Task 1 rows.
3. A "stay on this floor", "work on this floor" or floor setting survives save and reload, and ends when its colonist dies. Tests: Task 6, Task 7 and Task 29 rows.
4. A trade whose negotiator stands on another floor leaves the trader on the spot where the trader stood. Test: Task 2 row.
5. A visitor or thief on the lower or upper floor reaches the surface before leaving, and does not stand at a dead end. Tests: Task 26 and Task 27d rows.

## Decisions

Answered 2026-11-05: D2, "all of them". D1 is read as "rebuild in place" (the code stays, each idea changes it), which is how the ideas page framed option A. Lucas can overrule either in `docs/roadmap.md`.

**Decisions I made** under "implement all of them". Reverse each in the place named.

- DM1 Order: Phase A (bugs and small fixes), then B (speed and the own-floor change), then C (raids, exits, events, fights), then D (player features), then E (large features, after D3 to D5). Reason: the small fixes and the speed work change the base that the features build on. Reverse: reorder the task list.
- DM2 Keys: N switches one floor up, M one floor down (the order of the two bindings in `ZLevels/Common/Defs/KeybindingDefs/KeyBindings.xml`). Reverse: change the two defaults there.
- DM3 Stay on this floor is a per-colonist toggle in the colonist's gizmos, saved with the game. Reverse: `FloorPolicy`.
- DM4 "Work on this floor" uses the same lock with an expiry tick: 6000 ticks, as the ideas page says.
- DM5 Needs cool-down: at most one redirect per 2000 ticks for food or fun. Food skips the wait when `HungerCategory` is `UrgentlyHungry` or worse (`.artifacts/decomp-1.7.4998/project/RimWorld/HungerCategory.cs`). Rest is not redirected by this rule. Reverse: `FloorPolicy.RedirectAllowed`.
- DM6 Idle pause: an idle colonist whose search finds nothing waits 600 ticks before the next search.
- DM7 Background marking: every 2000 ticks per floor, up to 200 items are marked, and marking stops when the destination storage is full. These are the ideas page's numbers.
- DM8 Raids land only on the surface, Z index 0. Lower and upper maps are never raid targets.
- DM9 Leaving: a pawn on the lower or upper floor walks to the stairs toward the surface, changes floor, and leaves by the surface exit. Visitors, caravans, thieves and kidnappers all follow this.
- DM10 The floor settings are a main tab named "Floors" (a `MainButtonDef` and a `MainTabWindow`). Reverse: the tab class.
- DM11 Own floor first: the priority threshold `WorkGiverHighPriorityThreshold` defaults to 2 and lives in `ZLevelsSettings`.
- DM12 Heat and gas through stairs: a setting with three speeds (slow, medium, fast). The default is medium. The rates are my choice: a 10-degree gap closes in 2, 1 and 0.25 game days at slow, medium and fast. The scenario calls the step function directly for a fixed number of ticks, so it does not wait for game time.
- DM13 Correction to the ideas page. `ZUtils.cs:96-120` orders the stairs by straight-line distance and takes the first one the colonist can reach, which is not always the shortest walk. `ZPathfinder.GetClosestStair` (`ZPathfinder.cs:482-505`) ranks by real path cost, but runs one path search per stair on every call. Task 13 fixes both.

**Decisions for Lucas** (blocking, asked with the question tool when Phase D is done; Phase E waits):

- D3 (Task 36). Which ways up are built: ladders and stairs of other sizes, powered elevators, or a freight elevator that also carries items? Options: (a) ladders and stair sizes, no power (Recommended); (b) plus a powered elevator that carries colonists; (c) plus a freight elevator. Default if no answer: (a). Phase E waits.
- D4 (Task 37). Which resource does a pipe between floors carry, and at what rate? Options: (a) one resource the player picks per connector, at a fixed rate (Recommended); (b) power only, which `PowerTransfer` already covers in part; (c) fluids and items. Default if no answer: none is built; the roadmap line stays open. Phase E waits.
- D5 (Task 38). What does the stair transfer tab do? Options: (a) send a chosen list of colonists and items now (Recommended); (b) also keep a set amount on the next floor, such as 200 steel. Default if no answer: (a). Phase E waits.

## Execution pattern (every task)

- E1 Red. Add the task's named rows to `tools/RuntimeValidation/MultiFloorsScenarios.cs` and wire the scenario switch as Task 0 describes. Run `tools/Invoke-RuntimeValidation.ps1 -<Switch>` in the background. Expect the named rows to FAIL. Record the run folder in the task.
- E2 Implement the smallest change in the files the task names.
- E3 Green. Run the same scenario. Every named row must PASS, and the script must print the proof line.
- E4 Regression. Run the core validation and each scenario whose files the change touches. All must pass.
- E5 Commit only the task's paths. Message: an imperative subject, a body with the red and green run folders, and the trailer `Co-Authored-By: Claude Haiku 5.5 <noreply@anthropic.com>`.
- E6 Tick the task's checkboxes here with the run folder names.

## Task 0: Harness and commit method

**Files:**
- Modify: `tools/RuntimeValidation/RuntimeValidation.cs:14` (`public class RuntimeValidation` becomes `public partial class RuntimeValidation`) and the scenario dispatch near line 333 (one line per scenario).
- Create: `tools/RuntimeValidation/MultiFloorsScenarios.cs` (a partial class with one method per scenario).
- Modify: `tools/Invoke-RuntimeValidation.ps1` (one `[switch]` parameter, one entry in `Assert-SwitchesRead`'s reader list, one entry in `Get-RequiredLabels`, one argument line). Follow commit `5370d92` for the pattern.

**Shared-file warning.** Uncommitted work from another session sits in `tools/RuntimeValidation/RuntimeValidation.cs` (123 added lines), `tools/Invoke-RuntimeValidation.ps1`, `ZLevels/Source/ZLevels/ZLevels/Vault/VaultExitSpot.cs`, `ZLevels/Common/Defs/MapGenerator/VaultPocket.xml`, `ZLevels/Source/ZLevels/ZLevels/Vault/GenStep_VaultRock.cs` (new), the deleted `GenStep_VaultFloor.cs`, and the rebuilt `ZLevels/1.7/Assemblies/ZLevels.dll` and `ZLevelsRimatomics.dll`. Do not stage those hunks.

- [ ] **Step 1: Record the state.** Run `git status --short` and `git diff --stat`. Save the output in the scratch folder.
- [ ] **Step 2: Baseline run.** Run `tools/Invoke-RuntimeValidation.ps1` with no switch, in the background. Expect `Runtime validation passed. Evidence: <run folder>`. If it fails on another session's work, record the failing row and report it. Do not fix the vault files.
- [ ] **Step 3: Make the harness extensible.** Change the class to `partial`. Add `MultiFloorsScenarios.cs` with an empty partial. Run `dotnet build tools/RuntimeValidation/RuntimeValidation.csproj -c Release` and expect 0 errors.
- [ ] **Step 4: Commit method for shared files.** Create the patch that holds only this task's hunks: `git diff HEAD -- <file>` shows the foreign hunks, so write a patch with only this task's hunks, then `git apply --cached <patch>`. Check `git diff --cached --stat`, which must list only this task's files. Then `git commit -m <message file>`. Never run `git commit -- <shared file>`, because it takes the whole working-tree file. Write the method into `docs/validation-report.md` under "Commit method for shared files".

Done when: the baseline is recorded, the partial class builds, and the method is written down.

## Task 1: Rest search null checks (bugs 1 and 2)

**Files:** `ZLevels/Source/ZLevels/ZLevels/HarmonyPatches/JobPatches.cs:746` and `:775`.
**Scenario:** `-RestSearchNull`. Rows: `rest search logs no error`, `mental-state rest with a bed on the map uses the bed`, `no floor gives rest: colonist lies on the ground`.

- Red: the Player.log carries `Some kind of error occurred in Z-Levels JobManager`, from the null reference at 746 or 775.
- Fix at 746: test `result != null && result.targetA.Thing != null && result.targetA.Thing.Map == pawn.Map`. The old test used `== null` where it meant `!= null`. A null target crashed on `.Thing.Map`, and a real bed job fell through to the vanilla rest job and was never used. After the fix, a mental-state colonist with a bed on its map gets that bed job, which is the intent.
- Fix at 775: test `result == null || result.targetA.Thing == null` before the ground-rest path.

## Task 2: Trader stays on own spot (bug 3)

**Files:** `ZLevels/Source/ZLevels/ZLevels/HarmonyPatches/TradePatches.cs` (the postfix saves the negotiator's position at line 30 and teleports the trader to it at line 56).
**Scenario:** `-TraderReturn`. Row: `trader is back on its own cell and map after the trade`.

- Red: the trader ends on the negotiator's cell, on another floor.
- Fix: save the trader's own map and position before the trade, and restore those.

## Task 3: About text (bug 4)

**Files:** `ZLevels/About/About.xml:24`.
No gameplay change, so no run is needed. Replace the sentence about the structures tab with: the natural hole is only used for infestations underground, and avoid it. `ZL_NaturalHole` has no `designationCategory` (`ZLevels/Common/Defs/Buildings/Stairs.xml:83-105`).

- [ ] Proof: `python -I -c "import sys,xml.dom.minidom as m; m.parse(sys.argv[1])" ZLevels/About/About.xml` exits 0, and `grep -c "structures tab"` on the file prints 0.

## Task 4: A refused reservation retries quietly (idea 2)

**Files:** `ZLevels/Source/ZLevels/ZLevels/ZLevelsManager.cs:1258-1261` (the `else` branch calls `ZLogger.Pause` on the reservation failure).
**Scenario:** `-ReservationRefused`. Rows: `refused reservation does not pause the game`, `refused colonist searches again`, `no error logged for a refused reservation`.
Setup: a colonist's queued job targets a cell that another colonist reserves first. The -SocialRelaxQueue scenario (commit `5370d92`) is the model.

- Red: `TimeSpeed` is Paused, and the log has `Pausing, reason: Fail in TryMakePreToilReservations`.
- Fix: replace that pause with a debug-level `ZLogger.Message`, and keep `ResetJobTrackerFor(pawn)` so the colonist searches again.
- Other `ZLogger.Pause` calls stay. They report broken pawn state (`ZUtils.cs:328-441`), map removal (`ZLevelsManager.cs:300`) or missing stairs (`Jobs/Toils_ZLevels.cs:88, 100`). None is a normal failure. Record this in the commit body.

## Task 5: N and M switch floors and keep the designator and camera (idea 1)

**Files:**
- `ZLevels/Common/Defs/KeybindingDefs/KeyBindings.xml` (default keys).
- `ZLevels/Source/ZLevels/ZLevels/ZLevelsManager.cs:140-182` (`CheckHotkeys`).
- Confirm the default-key field names in `.artifacts/decomp-1.7.4998/project/Verse/KeyBindingDef.cs`. Confirm N and M are free in the base game's key defs (`Data/Core/Defs/KeybindingDefs`).

**Scenario:** `-FloorHotkeys`. Rows: `upper switch key is N`, `lower switch key is M`, `floor switch keeps the selected designator`, `floor switch keeps the camera x and z`.

- Red: the first two rows FAIL because the keys are unset. The designator row FAILs if the switch clears the designator.
- Fix: set the two default keys. Move each switch into `ZLevelsManager.SwitchFloor(bool up)`, which `CheckHotkeys` calls. Keep the designator selection and the camera's x and z.

## Task 6: Stay on this floor (idea 5)

**Files:**
- Create `ZLevels/Source/ZLevels/ZLevels/FloorPolicy.cs`.
- Modify `ZLevelsManager.cs` `ExposeData` (`:1922`) with the same parallel-list pattern as `activeAreas`.
- Modify the search loops in `HarmonyPatches/JobPatches.cs` (`TryIssueJobPackagePatch` at `:1135`, and the rest, food and joy patches at `:290`, `:472`, `:654`) so a locked colonist skips other floors.
- Create `HarmonyPatches/FloorLockGizmoPatch.cs` to add the toggle to the colonist's gizmos.

**Interfaces produced (FloorPolicy):** `SetLock(Pawn pawn, int floorIndex, int untilTick)`, where `untilTick == int.MaxValue` means until cleared. `ClearLock(Pawn pawn)`. `LockedFloorIndex(Pawn pawn, int nowTick) -> int?`. `AllowsMap(Pawn pawn, Map map, int nowTick) -> bool`. The lock stores the floor's Z index, not the `Map`, because maps are not saved as objects.

**Scenario:** `-StayOnFloor`. Rows: `locked colonist skips the surface haul`, `unlocked colonist takes the same haul`, `lock survives save and reload`, `lock ends when the colonist dies`.

- Red: the first row FAILs, because the colonist takes the surface job today.

## Task 7: Work on this floor, timed (player feature, small)

**Files:** `ZLevels/Source/ZLevels/ZLevels/HarmonyPatches/FloatMenuPatches.cs` (a float-menu option on stairs for selected colonists: "Work on this floor (100 s)"), and `FloorPolicy.cs` (`SetLock` with `untilTick = TicksGame + 6000`).
**Needs:** Task 6.
**Scenario:** `-WorkOnFloorOrder`. Rows: `stairs float menu offers the order`, `lock is active 5999 ticks after the order`, `lock ends 6000 ticks after the order`, `order stores the floor index`.

- Red: the option is missing, so the first row FAILs.

## Task 8: Needs cool-down (idea "Needs get a cool-down")

**Files:** `HarmonyPatches/JobPatches.cs:350` (food: the `ResetJobTrackerFor` call) and the joy patch (`:472-650`, its matching reset call). Rest is excluded (DM5).
**Scenario:** `-NeedCooldown`. Rows: `second food redirect within 2000 ticks is refused`, `very hungry colonist is redirected at once`, `second joy redirect within 2000 ticks is refused`, `redirect after 2000 ticks is allowed`.

- Red: the second redirect is allowed.
- Fix: keep the last redirect tick per colonist in `FloorPolicy`. It is not saved, so after a load the first redirect is always allowed. Record that in the commit body.

## Task 9: Idle search pause (the "unlimited idle searches" point)

**Files:** `HarmonyPatches/JobPatches.cs:1135` (`TryIssueJobPackagePatch`).
**Interfaces (FloorPolicy):** `IdleSearchAllowed(Pawn pawn, int nowTick) -> bool` and `NoteIdleSearch(Pawn pawn, int nowTick)`. The pause is 600 ticks.
**Scenario:** `-IdleSearchPause`. Row: `an idle colonist searches at most 3 times in 1500 ticks`. A counter, `FloorPolicy.SearchCount`, records each search.

- Red: the count is well above 3.

## Task 10: Clear stale "already going" claims (idea "someone is already going")

**Files:** the claim store used by `ZLevelsManager.ReserveTargets` (`:508`), `ZLevelsManager.ResetJobTrackerFor` (`:961`), `JobPatches.cs:1094` (`EndCurrentJobPatch`), and `ZLevelsManager.cs:300` (map removal).
**Scenario:** `-StaleClaims`. Rows: `claim is released when its job ends`, `claim is released when its map is removed`, `a second colonist takes the job after the release`.

- Red: the second colonist stays blocked.

## Task 11: Guard the colonist's floor during searches (idea "always move the colonist back")

**Files:** the teleport-and-search pairs in `JobPatches.cs` (`ZUtils.TeleportThing(pawn, oldMap, oldPosition)` calls around `:746-800` and in the copied search at `:1987-2319`). Each pair becomes a `try/finally` that puts the colonist back.
**Scenario:** `-FloorGuard`. A debug switch `FloorPolicy.ThrowInSearchForTest` (default false, set only by the scenario) makes the search throw. Rows: `colonist is back on its own floor after a failed search`, `colonist keeps its cell`.

- Red: the colonist stays on the other floor.

## Task 12: One storage index (idea 7)

**Files:** create `ZLevels/Source/ZLevels/ZLevels/StorageIndex.cs`. Modify `JobPatches.cs:942-955` (`PotentialWorkThingsGlobalPatch`, which adds every other floor's haulables on each call) and whichever rebuild a counter finds.
**Interfaces produced:** `StorageIndex.Destinations(PlanetTile tile) -> IReadOnlyList<IHaulDestination>`, rebuilt only when the haul-destination set changes. Confirm `HaulDestinationManager`'s members in the decompile before writing it.
**Scenario:** `-StorageIndex`. Rows: `200 lookups rebuild the index at most once`, `the index lists the same destinations as before`.

- Red: the rebuild count equals the lookup count.

## Task 13: Stair routes, shortest and cached (idea "saved stair routes")

**Files:** `ZUtils.cs:96-120` (straight-line order, then the first reachable stair), `ZPathfinder.cs:482-505` (`GetClosestStair`). Create `ZLevels/Source/ZLevels/ZLevels/StairRouteCache.cs`.
**Interfaces produced:** `StairRouteCache.Best(Map from, IntVec3 cell, bool goingUp) -> Building_Stairs`, `Invalidate(Map map)` (called by `SpawnStairsUpper`, `SpawnStairsBelow` at `:1292`, `:1311`, and by `ReCheckStairs` at `:1805`), and `MarkFailed(Map map, Building_Stairs stairs)`, which forces one recompute.
**Scenario:** `-StairRoutes`. Rows: `a farther stair with a shorter walk is picked`, `a wall that blocks the nearest stair sends the colonist to the reachable one`, `a second decision runs no path search`.

- Red: the nearest stair by straight line is picked, and the path count is above zero.

## Task 14: Add-ons load only when needed (idea "add-ons load only when needed")

**Files:** `ZLevels/LoadFolders.xml`. Move the cooling assembly `ZLevels/1.7/Assemblies/ZLevelsRimatomics.dll` into a new folder `ZLevels/1.7-Rimatomics/Assemblies/`. Change the output path in `ZLevels/Source/ZLevels/ZLevelsRimatomics/ZLevelsRimatomics.csproj` to match. `ModLoadFolders.cs:53` reads `IfModActive` on a load-folder entry. Read the Rimatomics package id from its `About.xml`.
**Scenario:** `-AddonLoad`. It runs the core validation with a mod list that lacks Rimatomics (`-ModsConfigSource`). Rows: `no cooling assembly loads without Rimatomics`, `the cooling assembly loads with Rimatomics`.

- Red: the cooling assembly loads in the run without Rimatomics.

## Task 15: Spread the periodic chores (idea "spread out the regular chores")

**Files:** `ZLevelsManager.cs:41` (the 60-tick `ZLevelsFixer` pass) and `Map/MapComponentZLevel.cs:94` (the random-period check). Add a per-floor offset so two floors do not run the same chore on one tick.
**Scenario:** `-ChoreSpread`. Row: `no tick runs the chore on two floors`. A counter records the floors per tick.

- Red: some ticks run two floors together.

## Task 16: Nothing extra until floors exist (idea "nothing extra until floors exist")

**Files:** the cross-floor loops in `JobPatches.cs` and `ZUtils.cs`. Add `ZUtils.HasExtraFloors(Pawn pawn) -> bool` (true when `ZTracker.GetAllMaps(pawn.Tile).Count > 1`). When it is false, run the vanilla path.
**Scenario:** `-OneFloorColony`. Row: `a one-floor colony runs no cross-floor search`. The counter is `FloorPolicy.CrossFloorSearches`.

- Red: the counter is above zero.

## Task 17: View the connected floor (player feature, small)

**Files:** a gizmo on `Building_Stairs` in a new file `HarmonyPatches/StairsGizmoPatch.cs`. The button calls `ZLevelsManager.DoCameraJumpToThing` on the other end (`ZLevelsManager.cs:1443`).
**Scenario:** `-StairsView`. Rows: `stairs offer the view button`, `pressing it moves the camera to the other end`.

- Red: the button is missing.

## Task 18: Better stair placement messages (player feature, small)

**Files:** the stairs placement check. Find it first (`Buildings/Stairs.xml` and the `Building_Stairs` placement code). It must refuse, with a clear message: no roof to build on, a room under 25 cells, and stairs going down from above ground.
**Scenario:** `-StairPlacement`. Rows: `no roof refuses with its message`, `room under 25 cells refuses with its message`, `stairs down from above ground refuses with its message`.

- Red: the refusal messages are missing, or the placement is allowed.

## Task 19: Measure the search and the extra floors (diagnosing-bugs)

**Files:** `MultiFloorsScenarios.cs` only, scenario `-SearchTiming`. Time `TryIssueJobPackage` for 10 idle colonists over 50 calls each, with one floor, two floors and three floors. Time `Map.MapTick` on each extra floor for 300 ticks. Repeat each setup three times.
**Output:** a section "Search timing baseline" in `docs/validation-report.md`, with the run folders.
**Done when:** the numbers are recorded with their run folders. No code changes.

## Task 20: Lighter update on extra floors (idea "lighter update on extra floors")

**Files:** create `HarmonyPatches/ExtraFloorTickPatches.cs`. Skip exactly four steps on maps whose Z index is not 0: wind, wild animal spawning, weather choice, and passing ships. Read the target methods' names and signatures in the decompile first. Do not copy any whole method.
**Scenario:** `-LighterFloors`. Rows: `no wild spawn on the lower floor in 2000 ticks`, `lower floor weather does not change`, `the surface still runs its weather`.
**Proof of benefit:** the Task 19 numbers, rerun. Record the change.

- Red: the spawn and the weather rows FAIL.

## Task 21: Own floor first, threshold, and the copy removed (idea 8)

**Files:**
- `HarmonyPatches/JobPatches.cs:1987-2319`: the 333-line copied `TryIssueJobPackage`. Delete it, and point `TryIssueJobPackagePatch` (`:1135`) at the new path.
- Create `HarmonyPatches/OwnFloorFirstPatch.cs`.
- `ZLevelsSettings.cs` gets `WorkGiverHighPriorityThreshold = 2`, saved in `ExposeData`, with a slider in `ZLevelsMod.cs`.

**Behavior:**
1. Run the vanilla `JobGiver_Work` search on the colonist's own floor.
2. If that finds nothing, run the same vanilla search on each other floor, nearest first. Use the Task 11 guard for the colonist's position.
3. Work types whose priority is at or below the threshold are checked on all floors before lower-priority work on the own floor.

**Scenario:** `-OwnFloorFirst`. Rows: `own-floor work: zero searches on other floors`, `priority-1 work upstairs beats priority-3 work on the own floor`, `priority-3 work with nothing upstairs is taken`, `threshold setting is 2 by default`.
**Regression:** `-OtherColonistHauls`, `-AllColonistsHaul`, `-MultiBuild`, `-Cooling`, and the core run.
**Needs:** Task 11, Task 19 numbers.

- Red: the first row FAILs, because the copy searches every floor.

## Task 22: Background item marking (idea "items checked in the background")

**Files:** create `ZLevels/Source/ZLevels/ZLevels/ItemFloorMarks.cs`. `ZLevelsManager.cs:30` runs the 2000-tick refresh. `JobPatches.cs:942-955` offers only marked items from other floors.
**Interfaces produced:** `ItemFloorMarks.Mark(Thing item, int floorIndex)`, `IsMarkedFor(Thing item, int floorIndex) -> bool`, `Unmark(Thing item)`, and `RefreshFloor(Map map)`, which marks at most 200 items and stops when the destination is full (DM7).
**Scenario:** `-BackgroundHaul`. Rows: `surface item needed on the lower floor is marked within 2000 ticks`, `a full destination stops new marks`, `a lower-floor colonist does not offer an unmarked surface item`, `a marked item is unmarked after it is hauled`.

- Red: the second and third rows FAIL, because every item is offered today.

## Task 23: Check the job on arrival (idea "check the job on arrival")

**Files:** `ZLevelsManager.cs:1000-1272` (`TryTakeFirstJob`) and `Jobs/Toils_ZLevels.cs:81-100` (the stairs toils).
**Behavior:** a cross-floor job is held until the colonist reaches the stairs. On arrival the target is checked again. If it is gone, the colonist searches again with no error and no pause (the Task 4 pattern).
**Scenario:** `-JobTakenOnArrival`. Rows: `target taken during the walk: colonist searches again`, `no error and no pause`, `a new job is taken`.
**Needs:** Task 4.

- Red: an error is logged or the game pauses.

## Task 24: Leave the busiest questions alone (idea "leave the busiest questions alone") — conditional

**Files:** the patches on `CanReach`, on the reservation check (`IsTaken`), and on "who do I attack" (`ReachAndReservationPatches.cs`, `CombatPatches.cs`).
**Gate:** start only after Tasks 21 and 23 are green, and only when the Task 19 numbers show these patches cost time.
**Method:** remove one patch at a time, and give each removal a scenario row named after the patch. If a removal breaks a row, restore the patch and record the reason in this plan.

## Task 25: Raids land on the ground (idea 3)

**Files:** create `HarmonyPatches/RaidGroundPatch.cs`. Patch the raid worker's `TryExecuteWorker` (check the name and signature in the decompile). If the target is a non-surface map, use `ZUtils.ZTracker.GetMapByIndex(tile, 0)` (`ZLevelsManager.cs:391`).
**Scenario:** `-RaidGround`. Rows: `raid aimed at a lower map arrives on the surface`, `raid on the surface is unchanged`.

- Red: the first row FAILs.

## Task 26: Leaving means walking to the ground first (idea 4)

**Files:** `HarmonyPatches/MapPatches.cs:92-105` (`ExitCells_Patch` removes the exit grid on level maps). Create `Jobs/LeaveByStairs.cs`, and hook it into the leave jobs (names confirmed in the decompile).
**Behavior:** DM9.
**Scenario:** `-LeaveFromLower`. Rows: `visitor on the lower floor reaches the surface and leaves`, `no pawn is left standing at a dead end`.
**Needs:** Task 13 (stair routes).

- Red: the visitor stays on the lower floor until the timeout.

## Task 27: Game events to check (idea "game events to check")

Test each event first. Fix only what fails. A row that passes is recorded as tested, with no change. Do not change code that passes.

- a. Parties, weddings, rituals and title ceremonies on another floor. Scenario `-EventsOtherFloor`.
- b. Alerts and mood thoughts that assume one map, such as lovers sleeping near each other. Scenario `-MoodOtherFloor`.
- c. Falling objects breaking every roof above the landing spot (`HarmonyPatches/SkyfallerPatches.cs`). Scenario `-SkyfallerRoof`. Row: `only the landing floor's roof breaks`.
- d. Thieves and kidnappers carrying the target down before leaving. Scenario `-ThiefCarry`. Row: `the carried colonist reaches the surface with the thief`.
- e. Children's behaviour, mech charging, and roping animals from another floor. Scenario `-ChildAnimalMech`.

## Task 28: Fighting across floors (idea "what not to copy")

**Files:** none, unless a test fails. Scenario `-CrossFloorFight`. Rows: `a shot from the lower floor through the stairs hits a surface colonist, as it should`, `melee across the stairs cell is refused, as it should`. Record the result either way. The ideas page says testing this is worth more than removing it.

## Task 29: Floors tab with per-colonist floor settings (idea "floor settings tab")

**Files:** create `ZLevels/Common/Defs/MainButtons/ZLevelsFloors.xml` (a `MainButtonDef`) and `ZLevels/Source/ZLevels/ZLevels/UI/FloorsTab.cs` (a `MainTabWindow`). Extend `FloorPolicy.cs` with `SetNeedFloor(Pawn pawn, NeedKind kind, int floorIndex)` and `NeedFloorFor(Pawn pawn, NeedKind kind) -> int?`.
**Behavior:** a table of colonists, with columns current floor, sleep floor, food floor, fun floor and meditation floor. Each need's floor is a choice of one floor or "any". The need's search uses only the chosen floor.
**Scenario:** `-FloorsTab`. Rows: `tab is registered`, `sleep floor set to lower: the rest job uses a bed on the lower floor`, `floor setting survives save and reload`, `current floor column shows the floor`.

- Red: the tab is missing, so the first row FAILs.

## Task 30: Work filter per floor (idea 6)

**Files:** `FloorsTab.cs` (a grid of work types against floors), `FloorPolicy.cs` (`WorkTypeAllowed(WorkTypeDef def, int floorIndex) -> bool`), and the work search in `JobPatches.cs`.
**Scenario:** `-FloorWorkFilter`. Rows: `cooking off on the lower floor: no cook job there`, `cooking on: the cook job is taken`, `filter survives save and reload`.

- Red: the first row FAILs.

## Task 31: Haul to a floor (idea "haul to a floor")

**Files:** create `ZLevels/Source/ZLevels/ZLevels/Designators/Designator_HaulToFloor.cs`. The designator selects items, and the player then picks a floor. It uses `ItemFloorMarks` (Task 22).
**Scenario:** `-HaulToFloor`. Rows: `marked item ends on the chosen floor`, `an unmarked item stays where it was`.

- Red: the first row FAILs.

## Task 32: Workbench floor link (idea "workbench floor link")

**Files:** a gizmo on work tables in a new file. `FloorPolicy.BenchFloor(Thing bench) -> int?` stores the link. The bill-ingredient search (`JobPatches.cs` `DoBillJobOnThing`, around `:1200-1320`) tries the linked floor first.
**Scenario:** `-BenchLink`. Rows: `bench linked to the lower floor fetches lower ingredients first`, `an unlinked bench is unchanged`.

- Red: the first row FAILs.

## Task 33: Call colonists here (idea "call colonists here")

**Files:** a targeter in `ZLevels/Source/ZLevels/ZLevels/UI/CallColonistsTargeter.cs`, opened by a gizmo on a selected colonist group. A picked cell on the current floor sends each selected colonist, from any floor, through the stairs, with the existing `ZL_GoToMap` job.
**Scenario:** `-CallColonists`. Rows: `colonist on the lower floor reaches the spot within 6000 ticks`, `colonists already at the spot stay there`.

- Red: the first row FAILs.

## Task 34: Heat and gas through stairs (idea "heat and gas through stairs")

**Files:** create `ZLevels/Source/ZLevels/ZLevels/StairEqualizer.cs` with `Step(Building_Stairs pair, int ticks)`. The game calls it every 60 ticks for each stairs pair. `ZLevelsSettings.cs` gets the speed setting (DM12).
**Scenario:** `-StairsEqualize`. Rows: `temperature gap between joined rooms shrinks`, `gas moves through the stairs`, `speed setting changes the rate`, `nothing moves when the stairs are removed`.

- Red: the first row FAILs.

## Task 35: In-game guide (idea "in-game guide")

**Files:** create `ZLevels/Source/ZLevels/ZLevels/UI/FloorsGuideWindow.cs`. Open it from a button on the Floors tab.
**Scenario:** `-FloorsGuide`. Rows: `guide opens`, `guide text covers stairs, floors, stay on floor, work on floor, and the keys`.

- Red: the guide is missing.

## Task 36: More ways up (idea "more ways up") — waits for D3

**Needs:** D3 answered. Ladders and stair sizes are one set, powered elevators another, freight elevators a third. The plan for each is written after the answer.

## Task 37: Pipes between floors (idea "pipes between floors") — waits for D4

**Needs:** D4 answered. The roadmap's "Tubes between levels" line stays open until then.

## Task 38: Stair transfer tab (idea "stair transfer tab") — waits for D5

**Needs:** D5 answered.

## Task 39: Final regression, records and report

- [ ] Run the core validation, and every scenario added by this plan, each in the background. Record each run folder.
- [ ] Update `docs/roadmap.md` (the features built, the decisions answered and the open ones), `docs/game-api-findings.md` (every confirmed name), and `docs/validation-report.md`.
- [ ] Run `superpowers:requesting-code-review` on the whole branch's diff.
- [ ] Final report: what changed, the run folder for each proof, what is untested, and the open decisions D3 to D5.
