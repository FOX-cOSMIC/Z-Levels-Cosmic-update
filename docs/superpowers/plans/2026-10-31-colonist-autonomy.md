# Colonist autonomy plan (2026-10-31)

Source: port plan `docs/superpowers/plans/2026-10-27-zlevels-17.md`, Task 3, box 74 ("Colonists take cross-level work by themselves"). Requirement 3 of `docs/superpowers/plans/2026-10-30-cross-level-work.md`: "Colonists go on their own to haul, build and mine on another level, with no forced job."

## State before this plan

- The single-colonist unforced haul (stage 20) passes. Since run `20261030-205256`, which failed, 56 runs have passed the stage with no stage 20 failure. Run `20261030-205256` used a colonist whose backstory disabled Hauling. `FindValidationColonist` now skips such colonists.
- The earlier stage 20 failures in runs `20261030-194056` and `202312` are not fully explained. Run `194056` had a sealed lower stairs pocket (one reachable cell). Run `202312` had an open room, and its colonist idled on the surface with no haul leg. Its log shows the same "invalid priority" restore messages as passing runs, so those messages do not explain it. Its colonist may have had Hauling disabled, but the log does not show it.
- The "starts and ends at once" loop in the roadmap (124 times, run `20261028-145345`) is from 2026-10-28. Runs `20261030-190222`, `191739` and `192636` (debug) show 210 to 239 same-map exits each. Those runs came before the mod commits of 2026-10-31 (`aba13be`, `2801b4e`, `0222951`). Debug runs from 2026-10-31 show 0 to 3 same-map exits per run.
- Not tested with the current harness: more than one colonist hauling across levels at the same time. Runs `20261030-191739` and `192636` showed colonists competing for the same logs, before the harness fixes. This is the open part of box 74.

## Rulings

- Ruling 1: no worktree. The project CLAUDE.md says "Stay on `master`", so commits go to `master`. Cost if wrong: none, since there is no remote.
- Ruling 2: the progress record is the Progress section of this file. I did not use the plugin workspace scripts, because they assume tasks with `Expected:` lines, and this plan does not use that format. Cost if wrong: a lost record after compaction, rebuilt from `git log`.
- Ruling 3: the test is a harness scenario in `tools/RuntimeValidation/`. Harness code is test code, so changing it does not change the mod DLL.
- Ruling 4: "Colonists" in box 74 means more than one colonist. The single-colonist result is recorded as partial evidence, not as box 74 done. Cost if wrong: about 10 minutes of extra runs.

## Tasks

### Task 1: Red. Several colonists haul the same logs

- Add `-zlevelsallcolonistshaul` (runner switch `-AllColonistsHaul`, required pass label `all colonists haul`).
- With the flag: `StartUnforcedHaul` does not set the other colonists' Hauling priority to 0. Other logs stay forbidden, as now.
- With the flag: stage 20 passes when the seven logs are in the zone and any colonist was on the lower level during the wait. Stage 20 then records `all colonists haul | PASS` and ends the run. The later stages are skipped.
- Run `tools/Invoke-RuntimeValidation.ps1 -AllColonistsHaul` with the game closed.
- Expected: the run either passes (the variant is green before any mod change) or fails at stage 20. Record the run ID and the result in the Progress section.

### Task 2: Diagnose, only if Task 1 fails

- Use `systematic-debugging`. Reproduce in debug mode (`-ZLevelsDebug`) before any change.
- Identify the cause from the logs: the job each colonist holds, the `Test -1` and `Test 1` leg counts, same-map exits, and reservation or reachability messages.
- Write one failing test per cause (the harness variant or a targeted check), then fix the mod, then re-run the variant and the regression set.
- Regression set: main chain (`tools/Invoke-RuntimeValidation.ps1`), `-Supply`, `-VaultEnter`, `-VaultLoad`, `-VaultSave`, `-VaultInfestation`, `-InfestationGuard`, `-StaleLookup`, `-VaultPlacement`, `-VaultPocket`.

### Task 3: Green. Prove it holds

- If Task 1 passes, run the variant two more times with the game closed each time. All three runs must pass.
- Tick box 74 in the port plan only after all three pass. Cite the three run IDs.

### Task 4: Record

- Update `docs/validation-report.md` (new section for this plan), `docs/roadmap.md` (Colonist autonomy entry), and the port plan box 74 evidence.
- Commit only the changed paths: `git commit -F <message file> -- <paths>`. Do not push.

## Progress

- 2026-10-31: plan written. Stage 20 single-colonist evidence reviewed (56 passes since `205256`).
- 2026-10-31: Task 1 harness change made (`-zlevelsallcolonistshaul`, switch `-AllColonistsHaul`, label `all colonists haul`). No mod change.
- 2026-10-31: Task 1 result: run `20261031-193644` (background `brphny4r4`) passed `all colonists haul | PASS`, with one cross-level leg (`Test -1`, `Test 1`, `Test 2` at 19:37:43.618 UTC+1). That run predates the check that no colonist is below before the haul. It also did not show which colonist took the job.
- 2026-10-31: added the check that no colonist is below before the haul (under the flag). Task 3 runs (background `bjnckbe6a`): `20261031-193842`, `193939`, `194037` all passed `all colonists haul`.
- 2026-10-31: ruling 4 needs proof that a colonist other than the validation colonist takes the haul. Added `-zlevelsothercolonistshaul` (runner switch `-OtherColonistHauls`, label `other colonist hauls across levels`): the validation colonist's Hauling is 0, and the harness requires a colonist other than it to be below during the wait. Results (background `bedueygwa`): `20261031-194231` (Gin), `194328` (O'Connell), `194425` (Biscuit). All three passed `other colonist hauls across levels`.
- 2026-10-31: box 74 is not ticked. Hauling by free colonists is proven. Building and mining by free colonists are not tested yet (stages 21 and 22 hold the other colonists at zero work). Those are the next task.
- 2026-10-31: committed the harness flags and docs as `8f37524`.
- 2026-10-31: Task 5 started. New flag `-zlevelsfreecolonists` (runner switch `-FreeColonists`) leaves other colonists' Mining and Construction on for the whole chain, and any colonist below counts for mining. Full chain run in background `bcrb6e4r9`. Result: run `20261031-194734` passed `core validation`, including `mines below by itself` and `builds below by itself`. That run did not record who mined or built.
- 2026-10-31: added records of who mined (`mined by`) and built (`built by`) under the flag. Runs `20261031-194937` and `195047` failed (a build error, then the start-of-haul check, which the free flag does not need). Run `20261031-195200` passed `core validation`. Miner and builder were both Rebecca, the validation colonist, so it does not show other colonists mining or building.
- 2026-10-31: Task 6 started. New flag `-zlevelsothercolonistswork` (runner switch `-OtherColonistsWork`): the validation colonist's hauling, mining and building are off. The chain passes only if another colonist hauls, mines and builds (the three `Require` checks). Three runs in background `bjd3l5r6i`. Results: `20261031-195406` passed `core validation`, with Lumi mining and building (validation colonist: Uffe). `195518` failed at stage 22: no campfire frame was ever started, so no colonist built, and the stage timed out. `200017` failed at stage 15 on the start-of-haul check, which is too strict for this flag.
- 2026-10-31: changed the variant. The start-of-haul check now applies only to `-AllColonistsHaul`. A colonist counts as coming below only if it was not below when the haul started (`enteredBelow`). Precondition: another colonist must be able to haul, mine and build, or the run fails with that message. Three runs in background `b48s13nak`. Results: `200303` and `200858` timed out at stage 21 (no colonist mined in four minutes). `200801` failed at stage 20: no colonist came below during the haul, although the logs reached the zone, so a colonist below before the haul may have carried them.
- 2026-10-31: changed again. Other colonists are held off work from stage 0 and restored at the unforced haul, so none wanders below early. Timeout messages list the other colonists' jobs and priorities (`DescribeOthers`). Three runs in background `becwtdae5`. Results: `20261031-201436` (Skvelt mined and built), `201557` (Darby), `201704` (Ash). All three passed `core validation`, with the validation colonist's hauling, mining, and building off. Each run also passed the check that a colonist other than the validation colonist came below during the haul.
- 2026-10-31: Task 3 criterion met for the other-colonists variant (three passes in a row). Box 74 ticked in the port plan. Mining and building by free colonists: the remaining gap from the earlier entry is closed by these runs.
