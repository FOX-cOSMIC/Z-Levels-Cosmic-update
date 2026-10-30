# Cross-level work plan (2026-10-30)

**Source:** Lucas's replies of 2026-10-30. "1 yes" accepts the proposal that a level change is smooth when the colonist keeps walking through the stairs. "2 forget that about the specific starting scenarios" drops the New Utopia 400 question. Do not edit that scenario file.

## Requirements

1. A level change is smooth. The colonist keeps walking through the stairs, with no teleport on arrival.
2. The lower level around the stairs has more walkable space than the one cell it has now, so tests can walk, mine and build there.
3. Colonists go on their own to haul, build and mine on another level, with no forced job.
4. A haul picks its destination by route length, including the stairs. It does not pick a long route because the straight-line distance looks short.
5. Hauling works with installed mods that change hauling.

## Constraints

- Validate only with `tools/Invoke-RuntimeValidation.ps1`, in isolated savedata.
- Never open existing saves. Never change the user's mod list. A run may add mods to its own copy of the mod list only.
- Commit only the paths a task changed. Stay on `master`. Do not push.
- Test first. A task's test is a validation scenario that fails before the change and passes after it.

## Tasks

### Task 1: Open space on the lower level

The harness opens a room of at least 5 by 5 walkable cells around the lower stairs, connected to them. The validation records the count of walkable cells reachable from the stairs. Standard validation passes with this room in place.

### Task 2: Smooth level change

During one stairs trip, the colonist never sits idle inside the trip, and it does not teleport on arrival. The last cell on the first level and the first cell on the second level are adjacent. The validation checks this across one trip, and standard validation passes.

### Task 3: Route length for cross-level hauls

A scenario has two stockpiles. One is close in straight-line distance but reached only through the stairs. The other is farther on the same level but has the shorter route. The haul goes to the shorter route. Standard validation passes.

### Task 4: Mining and building on the lower level

A colonist, with no forced job, goes down and finishes a mining designation and a construction blueprint on the lower level. The validation checks that the designation is gone and the blueprint is complete.

### Task 5: Hauling with installed hauling mods

First, list the installed mods that change hauling, from the user's active mod list and the Workshop folder. Then add them to the run's own mod list. Stages 4 to 20 must pass with them loaded. The run's mod list is a copy, and the user's list stays unchanged.

## Order

Task 1 first, because Tasks 3 and 4 need the space. Then Task 4, which is the largest gap. Then Task 3, then Task 2, then Task 5. Each task ends with a standard validation run and a commit of its own paths.

## Known open items that this plan does not cover

- The flaky stage 20 result in run 20261030-194056 (the colonist idled). Task 4 may reveal its cause. Investigate it first if it blocks a task.
- The reachability patch's NullReferenceException (`ReachAndReservationPatches.cs:85`).
- The animal spawn errors from run 20261029-111236.
