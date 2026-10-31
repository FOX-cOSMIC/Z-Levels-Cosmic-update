# Storage reachability plan (2026-10-31)

Source: open issue "A haul to an unreachable cell is retried" (run `20261030-192636`). Fixed on 2026-10-31 by Task 2. The regression set is Task 3. See "Storage reachability (2026-10-31)" in `docs/validation-report.md`. Roadmap item "Reachability patch bugs".

## Evidence

- Run `20261030-192636`: each haul to the zone ended with `ErroredPather`, and the job was offered again about every second. The zone cell lay behind rock. The first report blamed the harness, which had cut out one cell without checking reachability. The retry was diagnosed on 2026-10-31 (see Progress).
- `ZLevels/Source/ZLevels/ZLevels/HarmonyPatches/JobPatches.cs`, `IsGoodStoreCell` (line 1553): the reachability check was commented out (lines 1587 to 1592 before the fix). Nothing else checks that a storage cell can be reached. `TryFindBestBetterStoreCellForWorker` then picks the closest cell by straight-line distance, so a walled cell can win.
- The commented line used the item's position as the start. For a cross-level haul, the item is on another level, so the check was wrong there. That is probably why it was disabled.
- Requirement 4 of `docs/superpowers/plans/2026-10-30-cross-level-work.md` (route length, not straight-line distance) is related and stays open.

## Rulings

- Ruling 1: fix only the same-level case. A storage cell on the carrier's own map is checked with the carrier's position as the start. Storage cells on another level keep today's behavior, because checking them needs the stairs route. Cost if wrong: a cross-level storage cell can still be unreachable.
- Ruling 2: no worktree. Commits stay on `master`, as the project CLAUDE.md requires.
- Ruling 3: a pass also needs one haul to the reachable cell. Without it, a fix that stops every haul would pass.

## Tasks

### Task 1: Red. A walled storage cell must not get hauls

- Harness scenario `-zlevelsunreachablezone` (runner switch `-UnreachableZone`, pass label `unreachable zone`).
- Setup: a fresh colony on the surface. One stockpile cell has walls on all eight sides. A second stockpile cell is reachable and farther from the logs and from the colonist than the walled cell, so the search picks the walled cell before the fix. Seven logs lie outside the walls, reachable. Only the validation colonist may haul. Other colonists' Hauling is 0.
- Count haul jobs over 60 seconds, on each game tick. Pass if no job targets the walled cell and at least one targets the reachable cell.
- Expected before the fix: at least one haul to the walled cell. The count can miss a job that starts and fails inside one tick. The debug log of an earlier one-cell version showed 8 such jobs in 60 seconds, and the count showed 1.

### Task 2: Fix

- In `IsGoodStoreCell`, when the cell is on the carrier's map, reject the cell unless `carrier.Map.reachability.CanReach(carrier.Position, c, PathEndMode.OnCell, TraverseParms.For(carrier, Danger.Deadly, TraverseMode.ByPawn, false))` is true. The traverse parameters are the ones the old commented-out line used.
- Cells on other maps are unchanged.
- Re-run Task 1. It must pass.
- Done. Run `20261031-212346` passes. The runner builds only the harness. Build the mod first with `dotnet build ZLevels/Source/ZLevels/ZLevels/ZLevels.csproj -c Release`.

### Task 3: Regression

- Main chain, `-StaleLookup`, `-Supply`, `-VaultPlacement`, `-VaultPocket`, `-VaultEnter`, `-VaultLoad`, `-VaultSave`, `-VaultInfestation`, `-InfestationGuard`, `-OtherColonistsWork`, and the colonist-haul runs `-AllColonistsHaul`, `-OtherColonistHauls`, `-FreeColonists`. The last three cover colonist hauls, which use the same storage search.
- Record each run ID in `docs/validation-report.md`.

## Progress

- 2026-10-31: plan written.
- 2026-10-31: Task 1 done. The first version had one stockpile cell. Run `20261031-211443` failed with one counted haul. Run `20261031-211859` passed with the fix, but it shows nothing about hauling, because no other cell existed. The scenario now has a reachable second cell. With the check off, run `20261031-212210` fails: one haul to the walled cell and none to the reachable cell.
- 2026-10-31: Task 2 done. The check is back on. Run `20261031-212346` passes: no haul to the walled cell and one haul to the reachable cell.
- 2026-10-31: Task 3 running. Results go to `docs/validation-report.md`.
