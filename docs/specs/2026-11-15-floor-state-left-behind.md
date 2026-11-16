# Spec: floor state left behind by the floor loops and the reach patch

Date: 2026-11-15. Decision D15 (answered 2026-11-15: yes). Branch `fix/floor-state-left-behind`, from `zlevels-1.7` at `2b02da7`.

## Goal

No search leaves floor state behind, whether it ends normally, stops early or throws. That covers two things:

1. The floor field. `ZUtils.currentLookedIntoMap` names the floor a search looks into. The two floor iterators in `ZUtils.cs` (`GetAllMapsInClosestOrder` and `GetAllMapsInClosestOrderForTwoThings`) set it before each floor they yield, and never put it back. While a colonist's tracker is the one being filled (`LocalTargetInfo_Constructor_Patch.curPawnJobTracker`, set by `ResetJobTrackerFor` and cleared by `BuildJobListFor`), each cell built from then on is filed under that field. `CanReach_Patch` then answers the colonist's reach to the cell on the filed floor. A field left on another floor makes a reach to an open cell on the own floor refused. The work search was fixed for this in `81c3557` (D13, option a). The other 20 call sites were not (D13, option b).
2. The colonist's floor after a reach check. `CanReach_Patch.Prefix` moves the colonist to the target's floor and answers there. Its `Postfix` moves it back. Harmony skips the postfix when the call throws, so a throw leaves the colonist on the other floor (roadmap, "Reachability bugs", run `193352`).

## Decisions

- The iterators put the field back on their own-floor argument (`oldMap`) when the enumeration ends: at the end of the floors, when the caller stops early (`foreach` disposes the iterator), and when the caller's loop body throws. This is the same target Q3 and D13 chose for the work search. Reason: the value on entry can itself be stale. A nested search runs with the colonist moved to the outer search's current floor, so its `oldMap` is that floor, and the outer search keeps its field.
- The iterators move nothing else back. The callers already move the colonist back (`FloorRestore`, or their own `TeleportThing`), and some callers act on the colonist's floor after the loop.
- `CanReach_Patch` keeps the colonist's floor and cell in `__state`, not in static fields, and gets a Harmony finalizer that moves the colonist back when the call throws. The exception still reaches the caller. The finalizer also moves the colonist back after a normal call, and replaces the postfix. Harmony runs finalizers after every postfix, so another mod's postfix on `Reachability.CanReach` sees the colonist still on the target's floor.
- The work search's own restores (`OwnFloorFirstSearch.Search` and `SearchOwnFloorOnly`, Q3 and D13) stay as they are. They now do nothing the iterator does not, but they cover the own-floor passes, which do not run inside an iterator.

## Out of scope

- The work search's other state (`mapDest`, the cell table), already fixed by Q3.
- The meditation search (needs Royalty, which the default validation mod list does not load, D7), the trade search (needs a trader), the animal follow search and the float menu. The iterator rows cover their loops. The float menu already saves and restores the field itself.
- Making a search that throws move the colonist back. Every caller already wraps its loop in `FloorRestore` or a catch that does.

## Acceptance checks

The new scenario switch is `-FloorStateAfterSearch`. Its required pass line is `floor state after search`.

1. Before the fix, the scenario fails at least these rows: the full-enumeration iterator row, the food row, the joy row, and the reach-patch throw row. Run the scenario on the scenario commit with the product code of `2b02da7`. Observe `FAIL` on each of those rows.
2. After the fix, `tools/Invoke-RuntimeValidation.ps1 -FloorStateAfterSearch` prints `Runtime validation passed. Evidence: <run folder>`.
3. The rows cover, for each of the two iterators: an enumeration to the end, an early stop on another floor, and a throw inside the loop body on another floor. Each one checks that the field is the own floor afterwards.
4. The rows cover the food, joy and rest searches, through their real Harmony prefixes, with the colonist's reach to an open own-floor cell checked after each one. A harness prefix on each search's per-floor step makes every floor come up empty, and records which floors it ran on. Each search must have looked into the lower floor.
5. A row covers the attack search's loop over the other floors (`CombatPatches.cs`, `BestAttackTarget`). The scenario turns on `multiMapSearch`, as the Z-Levels callers do, and calls `AttackTargetFinder.BestAttackTarget` with the colonist as the searcher and no hostile on any floor. The search must have looked into the lower floor, and the field must be the own floor afterwards.
6. The reach-patch rows: a throw while the colonist stands on the target's floor leaves it on its own floor and cell, for a target thing on the lower floor and for a cell filed under the lower floor. The exception still reaches the caller.
7. A nested iterator row: an iterator run inside an outer loop body on the lower floor leaves the field on the lower floor.
8. `-SearchThrowState` still passes.
9. The full regression (core, every scenario switch, and RimatomicsPlusPlus) runs on the fix commit with a clean tree. Every run passes except `-CoolingNoColumns`, which fails by design. Every run builds the same `ZLevels.dll` hash.
10. `tools/Check-Docs.ps1` passes.
11. A haul to storage on the lower floor, and one to storage on the own floor, file the store cell under the storage's floor, and leave the floor field on the own floor (review round 1, finding 1). The lower-floor row fails on the scenario commit `0ae7dde` with the product code of `fb02e65`, and passes after the fix.
