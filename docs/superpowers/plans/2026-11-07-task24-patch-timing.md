# Task 24 patch timing (multifloors ideas, "leave the busiest questions alone")

> **For agentic workers:** Execute inline. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Decide Task 24 of `docs/superpowers/plans/2026-11-05-multifloors-ideas.md` from measured per-patch costs. Lucas answered D1 with option A on 2026-11-07: measure, then rebuild the job search in place.

**Architecture:** The `-SearchTiming` scenario (Task 19) gets a per-patch phase. Each candidate patch is unpatched from the running game, timed against a baseline taken just before and after, then re-applied. The measurement changes no mod code. A patch that meets the rule in R2 is removed from the code in step B, one at a time, with a row for each. A removal that breaks a row is restored.

**Tech stack:** The RuntimeValidation harness (C#, partial classes), Harmony (`0Harmony.dll` from the Steam workshop, the same reference as `ZLevels.csproj`), and `tools/Invoke-RuntimeValidation.ps1`.

**Spec:** Task 24 and its gate in the ideas plan. The D1 option text in `docs/mod-comparison/rebuild.html`. The method of Task 19 in `docs/validation-report.md`, "Search timing baseline".

## Global constraints

- Validate only in isolated savedata and a new colony. Never open an existing save. Never change the user's mod list.
- One runner at a time. No harness or mod edit while a runner runs.
- Commit only this task's paths. The vault session's uncommitted files stay out: `tools/Invoke-RuntimeValidation.ps1`, `tools/RuntimeValidation/RuntimeValidation.cs`, and the vault files.
- Never push.

## Rulings, made before the first measurement

- R1. Scope. The reach patch is `Reachability.CanReach` (`ReachAndReservationPatches.cs`, `CanReach_Patch`). The reservation patches are `ReservationManager.CanReserve` (`ReservationManager_CanReserve_Patch`) and `ReservationManager.CanReserveStack` (`ReservationManager_CanReserveStack_Patch`). The plan names an "IsTaken" check, but no patch in the code has that name. The attack patches are `AttackTargetFinder.BestShootTargetFromCurrentPosition` (`BestShootTargetFromCurrentPosition_Patch`), `JobGiver_AIFightEnemy.FindAttackTarget` (`FindAttackTarget_Patch`), and `AttackTargetFinder.BestAttackTarget` (`CombatPatches_BestAttackTarget_Patch`). Not toggled here: `GenGrid.CanBeSeenOverFast`, `Verb.TryFindShootLineFromTo`, and the four `TryGiveJob` patches in `CombatPatches.cs`. They are candidates for a later task. Cost if wrong: a busy patch is missed, and the report says which ones were not measured.
- R2. A patch costs time when removing it lowers the mean microseconds per call by at least 10 percent, and by more than the spread of the baselines in the same run. Reason: the Task 19 repeats moved by 7 to 20 percent between runs, so a smaller drop is noise. Cost if wrong: a patch with a real cost of about 5 percent stays, or one that shows 12 percent by chance is removed and must still pass its rows.
- R3. The reach and reservation patches sit on the work search, so the Task 19 method times them: the same ten colonists, three floors, 500 calls, with the 601-tick clock advance. The attack patches are not on the work search. They are timed by direct calls, `FindAttackTarget` and `BestShootTargetFromCurrentPosition`, on one colonist that holds a revolver, with no hostile in range. That times the scan, not a found target. Cost if wrong: a fight that finds a target costs differently, and that cost is not measured.
- R4. Removal for the measurement is `Harmony.Unpatch(original, patchMethod)` in the running game, then `Harmony.Patch` to re-apply it. The mod code does not change in this step. Cost if wrong: the runtime removal can differ from a code removal, which step B tests.
- R5. Every measurement is an idle search, as in Task 19. A measurement that finds a job is reported as such, and its cost is not compared.

## Task A: the per-patch phase

**Files:**
- Modify: `tools/RuntimeValidation/MultiFloorsScenarios.cs` (`RunSearchTimingCheck`, the phase call and its rows)
- Create: `tools/RuntimeValidation/PatchTimingScenarios.cs` (the toggles, the timing, the restore check)
- Modify: `tools/RuntimeValidation/RuntimeValidation.csproj` (a reference to `0Harmony.dll`, with the same default path as `ZLevels.csproj`)

**Interfaces:**
- Consumes: `TimeWorkSearches(tracker, colonists, callsEach)` returns `WorkSearchTiming` (Task 19).
- Produces: rows `patch timing, <patch> removed`, `patch timing, baseline`, and `patch timing recorded` (PASS when every patch is applied again after its measurement and each measurement has its calls).

- [ ] **Step 1: Write the row that must fail first.** Add `patch timing recorded` to `RunSearchTimingCheck` with the pass rule above. Before the phase exists, the row reports `FAIL: the per-patch phase did not run`.
- [ ] **Step 2: Run `-SearchTiming` and read the row.** Expected: the row FAILs with that message, and `search timing recorded` still PASSes.
- [ ] **Step 3: Write the toggles.** One entry per patch method: the original `MethodBase`, the patch `MethodInfo`, and whether it is a prefix or a postfix. Each toggle removes all of its patch's methods (the Prefix and the Postfix of one class).
- [ ] **Step 4: Write the phase.** Per round: a baseline, then each work toggle (`CanReach`, `CanReserve`, `CanReserveStack`) removed and timed, with a baseline after it. Then the attack probe: `FindAttackTarget` and `BestShootTargetFromCurrentPosition` timed with each attack toggle removed, with baselines around them. Three rounds. Each measurement is 500 calls.
- [ ] **Step 5: Run `-SearchTiming` three times,** one at a time. Each run's rows are read in full.
- [ ] **Step 6: Apply R2.** Write the result for each patch in `docs/validation-report.md`.

## Task B: removal in the code (only for a patch that meets R2)

- [ ] For each patch that meets R2, one at a time: delete the patch class from the code, add one row named after the patch to the scenario that exercises it, and run the rows. If a row breaks, restore the patch and record the row and the reason here.
- [ ] Rows and their scenarios are written after Task A's numbers. Each removal gets its own run folder.
- [ ] If no patch meets R2, Task B is not run. The gate result is then the answer for Task 24.

## Task C: record

- [ ] Report section in `docs/validation-report.md`: the rules, the numbers, the runs, and the outcome of each patch.
- [ ] `docs/roadmap.md`: the Task 24 line, and D1 answered.
- [ ] Commit only the paths above, with the trailer `Co-Authored-By: Claude Haiku 5.5 <noreply@anthropic.com>`.
