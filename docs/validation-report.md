# Z-Levels 1.7 validation report

Date: 2026-10-28. Machine: Windows 11 PC. Game: RimWorld 1.7.4990 rev1069, Steam unstable branch.

## Result

- The clean Release build of the final C# sources passes with 0 errors and 8 warnings (7 distinct messages). Log: `.artifacts/build/release-build-final.log`.
- The final visual validation passed. Its last line is `Runtime validation passed. Evidence: .artifacts/runs/20261028-135603`. Every PASS/FAIL check reports PASS, including the stairs checks after a save reload. The only ERROR line is the duplicate-package message for another mod, `Aoba.DeadManSwitch.Core`. This run has no caves on the lower level, so it logs no animal warnings.
- The lower stairs open onto a pocket of one walkable cell, walled in by natural rock on every side. The colony must dig out before a colonist can walk anywhere else. The harness removes one rock wall and lays concrete beside the stairs for its haul, so walking beyond that cell is untested. See "Open issues".
- On some lower levels, the vanilla game logs `RandomAnimalSpawnCell_MapGen failed` once per spawn call. The mod then places each animal on a walkable cell, so no animal stands in rock. None of the animals measured for reachability could reach the stairs. See "Open issues".
- Earlier runs failed the harness for other reasons. Run 035000 hit a stairs `KeyNotFoundException`, which is fixed. Run 032903 hit a refuel job error, which is not explained. Runs 134128 and 134553 failed on harness problems, and the first is fixed. See "Failures found and resolved" and "Open issues".
- Not verified: multiplayer, existing saves, DLC and other mods, float-menu options that cross levels, combat across levels, walking or digging beyond the stairs pocket, and hauling or power after a reload. See "Not tested".

## Setup

| Item | Value |
|---|---|
| Game | RimWorld 1.7.4990 rev1069, Steam unstable |
| Harmony | Steam Workshop 2009463077. Log line: `Harmony Mod v3.0.1.0; Harmony library v3.0.0.0-rc.3` |
| Mods active in runs | `brrainz.harmony`, `Ludeon.RimWorld` (Core only), `ZLevels1.1`, `local.zlevels.validation` |
| DLC active | None |
| .NET SDK | 10.0.301 |

Each run gets its own folder under `.artifacts/runs/`. The harness passes `-savedatafolder` pointing at that folder, so the run's `Config/` and `Saves/` live there and the harness writes its own `ModsConfig.xml` there. The user's RimWorld data folder has no `Config/` folder, so there was no user mod-list file to change. Its `Saves/` folder shows no change since Jul 28.

The harness makes two junctions in the game's `Mods` folder: `ZLevels-Local17` (linked to `ZLevels/`) and `ZLevels-Validation` (linked to `tools/RuntimeValidation/`). They are not in the mod list. They stay until removed; the README gives the command.

## Build

Command: `dotnet build ZLevels/Source/ZLevels/ZLevels/ZLevels.csproj -c Release --no-incremental`. The `--no-incremental` flag makes the compiler emit every warning again.

| Code | Location | Meaning | Origin |
|---|---|---|---|
| NU1701 | `ZLevels.csproj` | `RimWorld.MultiplayerAPI 0.6.0` was restored for .NET Framework. The log shows it twice | Package |
| CS0168 | `HarmonyPatches/RoofAndFloorPatches.cs:151` | `ex` declared, never used | Upstream; the port adds no `catch` line here |
| CS8073 | `Jobs/JobDriver_ZLevels.cs:46` | Null comparison on `IntVec3` is always true | Upstream; file unchanged |
| CS0219 | `HarmonyPatches/JobPatches.cs:1651` | `dest` assigned, never used | Upstream; no added line declares it |
| CS8073 | `Jobs/JobDriver_HaulThingToDestAndToCell.cs:145` | Null comparison on `IntVec3` is always true | Upstream; file unchanged |
| CS0168 | `ZPathfinder.cs:179` | `e` declared, never used | Upstream; no added line declares it |
| CS0219 | `ZPathfinder.cs:217` | `found` assigned, never used | Upstream; no added line declares it |

None of the warnings is in code the port added. They were left as they are.

## Runtime runs

| Run | Mode | River cells (surface / lower) | Harness exit | Notes |
|---|---|---|---|---|
| 032903 | headless | 1301 of 1301 / 1147 of 1147 | 1 | All scenarios PASS. Failed on the refuel ERROR line (see Open issues) |
| 035000 | visual | none | 1 | All scenarios PASS. Failed on the stairs ERROR line (fixed). 136 animal warnings |
| 035409 | visual | 1607 of 1607 / 1449 of 1449 | 0 | All scenarios PASS. Built before the roof-terrain fix |
| 035538 | headless | none | 0 | All scenarios PASS. Built before the roof-terrain fix |
| 040440 | visual | none | 0 | All scenarios PASS, 4 screenshots. Ran before the reload stairs checks were added |
| 041107 | visual | none | 0 | All scenarios PASS, 4 screenshots. Ran before the lower-level census and the save guard were added. 44 animal warnings |
| 133324 | headless | none | 0 | Caves below. 89 animals and 89 animal warnings. Passed |
| 134128 | headless | 1687 of 2426 / 1827 of 1827 | 1 | Reload check failed: the colonist was on the lower level at save. Fixed by the save guard |
| 134553 | headless | not recorded | 1 | Timed out at stage 1: the colonist lay down before the stairs job. Not fixed |
| 135155 | headless | none | 0 | Caves below. 90 animals and no animal warnings. None of the animals could reach the stairs |
| **135603** | **visual** | none | **0** | **Final.** All scenarios PASS, stairs down and back up after the reload, 4 screenshots. No caves below |

Earlier development runs (folders from 003430 to 032749) used earlier code. Their logs are still under `.artifacts/runs/`, but they are not evidence for the final build.

## Scenario results

The harness records each step as PASS or as an informational line. Visual runs also take screenshots.

| Scenario | What the harness checks | 032903 | 035000 | 035409 | 035538 | 040440 | 041107 | 135603 |
|---|---|---|---|---|---|---|---|---|
| fresh colony | surface is Z-index 0 | PASS | PASS | PASS | PASS | PASS | PASS | PASS |
| stairs down and underground generation | pawn reaches the lower level (index -1) | PASS | PASS | PASS | PASS | PASS | PASS | PASS |
| return upstairs | pawn returns to the surface by the lower level's up stairs | PASS | PASS | PASS | PASS | PASS | PASS | PASS |
| stairs up and upper generation | pawn reaches the upper level (index +1) | PASS | PASS | PASS | PASS | PASS | PASS | PASS |
| return downstairs | pawn returns to the surface; three levels tracked | PASS | PASS | PASS | PASS | PASS | PASS | PASS |
| cross-level hauling | 7 wood logs hauled to the lower-level cell beside the stairs. The harness clears and paves that cell first. Quantity kept. | PASS | PASS | PASS | PASS | PASS | PASS | PASS |
| power bring-up (ticks until lamp is powered, informational) | transmitters placed, lower lamp then on | 272 | 445 | 267 | 404 | 287 | 273 | 282 |
| cross-level power transfer | lower lamp on, powered from the surface generator | PASS | PASS | PASS | PASS | PASS | PASS | PASS |
| cross-level power loss without source | lamp off after the generator is removed | PASS | PASS | PASS | PASS | PASS | PASS | PASS |
| surface hole over lower wall | hole terrain created; lower cell revealed (visual runs only) | not run | PASS | PASS | not run | PASS | PASS | PASS |
| save and reload three-level colony | save, reload; three levels and pawn on surface | PASS | PASS | PASS | PASS | PASS | PASS | PASS |
| reload stairs down | after the reload, the pawn takes the reloaded stairs down to the lower level | not run | not run | not run | not run | not run | PASS | PASS |
| reload stairs return | after the reload, the pawn takes the lower level's up stairs back to the surface | not run | not run | not run | not run | not run | PASS | PASS |
| screenshots: lower level, upper void, surface, surface hole | written to `Screenshots/` (visual runs only) | not run | 4 | 4 | not run | 4 | 4 | 4 |
| lower-level census at arrival (informational) | caves, lower animals, and walkable cells the stairs reach | not run | not run | not run | not run | not run | not run | no caves, 0 animals, 1 of 21 walkable cells reachable |

Lower-level glow (informational): the lamp cell reads `RGBA(203, 140, 89, 0)` in every run. The sample three cells away is `RGBA(90, 62, 39, 0)` in 032903 and zero in the other runs. The check does not prove glow propagation, so it is not used as evidence.

The final run's lower-level screenshot shows the lamp and the cells around it, with the rest of the lower level dark. I checked it by eye. The other screenshots were not compared with a baseline.

## Failures found and resolved

1. **River and coast generation.** 1.7 removed `RiverMaker` and `BeachMaker`, so the build failed with CS0103 at three call sites. The port now calls the 1.7 `TileMutatorWorker` river and coast hooks from `UndergroundWaterMutators.cs`. Proof: the build has 0 errors, and runs 032903 and 035409 show every water cell on both levels as a river cell.

2. **Stairs `KeyNotFoundException` (run 035000).** `ZUtils.GetAllMapsInClosestOrder` read `stairsUp[lowerMap]` directly. `Building_StairsUp` and `Building_StairsDown` `SpawnSetup` replace the stairs dictionaries, and `ReCheckStairs` refills them on the next game tick, so a map can have no entry in between. The error came from a combat target search (`CombatPatches_BestAttackTarget_Patch`) run during a pawn's think tree. The fix reads through `GetStairsUpFor` and `GetStairsDownFor`, which return an empty list for a missing key. `GetClosestStair` also returns null for an empty list; it used to create `new Dictionary(targetList.Count - 1)`, which throws for an empty list. Proof: no recurrence in runs 035409, 035538, 040440 and 041107. The trigger was seen once, and no scenario reproduces it on purpose, so the fix is shown only by its absence.

3. **Roof terrain still used a 1.6 field (found in the final review of the defs).** `ZL_RoofTerrain` kept `<holdSnow>`. The vanilla 1.7 defs use `holdSnowOrSand`, and none use `holdSnow`. The 1.7 patch in `ZLevels/1.7/Patches/TerrainFields.xml` covered only `ZL_OutsideTerrain` and `ZL_OutsideTerrainTwo`. The patch now covers `ZL_RoofTerrain` too. Proof: runs 040440 and 041107 load with no ERROR line except the duplicate-package message. Its XPath matches the one `holdSnow` node in `RoofTerrain.xml` (the only TerrainDef in that file). The game writes no log line for a successful XML patch, and it does not report unknown fields, so the run log cannot confirm that the patch applied.

4. **Reload check with the colonist downstairs (run 134128).** The harness saved while the colonist stood on the lower level, so `reload three-level colony` failed. The save file puts the colonist on map 1, `ZL_Underground`. The harness now brings the colonist up and saves after they arrive. Runs 135155 and 135603 pass the reload checks with this change. Before this change, nothing checked where the colonist stood before a save.

## Open issues

**Sealed stairs pocket (lower level).** The lower stairs stand on a hewn floor under a thick roof (`RoofRockThick`). Natural rock walls fill all eight neighbouring cells. Run 134416 has limestone walls, and run 135603 has granite walls. The game data marks both stone types as mineable (`Data/Core/Defs/ThingDefs_Misc/Various_Stone.xml`). Across the 13 runs that measured reachability, the stairs reached no other walkable cell. The walkable cells on the lower level number from 21 to 1998 in those runs.

Only `GenStep_FindLocationUnderground` reads the start spot, and it only copies it to `MapGenerator.PlayerStartSpot` (`ZLevels/Source/ZLevels/ZLevels/GenSteps/GenStep_FindLocationUnderground.cs:21`). So no code cuts a path from the start spot toward a cave. The mod description promises that players can dig underground (`ZLevels/About/About.xml`, line 14), and a sealed start fits that promise. The code does not say whether the start should open onto a cave. That is a design decision. Digging was not tested. The About text also warns about roof collapse when building lower (line 18).

**Animal spawn warnings on the lower level.** Some lower-level runs log `WARN [Verse] RandomAnimalSpawnCell_MapGen failed` once per spawn call. Run 041107 logs 44 of them, run 035000 logs 136, and run 133324 logs 89. In each of these runs the animal count equals the warning count, so every spawn call took the fallback. Three of the seven runs with lower-level animals did this. Four others logged no warning: 024938, 025443, 032659 and 135155. Their spawn calls found a cell through the normal search.

The warning comes from the vanilla function that `GenStep_AnimalsUnderground` calls before each spawn (`ZLevels/Source/ZLevels/ZLevels/GenSteps/GenStep_Animals.cs:44`). The function tries up to 1000 cells. It needs one that is standable and lies in a district touching the map edge. Run 041107 found none. Its counters were `numStand=976, numDistrict=0, numTouch=24`: the 24 standable samples all lay in districts that do not touch the edge. The function then returns a random map cell and logs the warning (decompile of `RimWorld.RCellFinder`, lines 597-650).

The mod's animal prefix moves an unwalkable spawn cell to a random walkable cell on the whole lower level (`ZLevels/Source/ZLevels/ZLevels/HarmonyPatches/AnimalPatches.cs:58-61`). Animals do not stand in rock. In run 133324, none of the 89 animals stood on an unwalkable cell. In run 135155, none of the 90 did.

Only run 135155 measured animal reachability, and none of its 90 animals could reach the stairs. Run 133324 used the first version of the check, so only its unwalkable count is used.

No code change was made for these warnings. The warning reports a failed search, and the placement after it is correct. Hiding the warning would also hide the failed search.

**Harness timeout at stage 1 (run 134553).** The colonist lay down to rest before taking the stairs down. The harness waited four minutes and timed out (`Timed out at stage 1, pawn job=LayDown`). The harness does not issue the stairs job again. A rerun passed. The timeout is open because the harness has no retry.

**Refuel job pause (run 032903).** The log has:

```
ERROR [Verse] Pausing, reason: Fail in TryMakePreToilReservations, pawn: Summers, job: Refuel ... driver: RimWorld.JobDriver_Refuel, map: Map-0-PlayerHome
```

`ZLogger.Pause` raised it from `ZLevelsManager.TryTakeFirstJob`. Both are upstream code that the port did not change. It happened during the power bring-up scenario, while a colonist refuelled a wood-fired generator on the surface map. The harness counts ERROR lines as failures, so the run exited 1 even though every scenario passed.

It was seen once. It has not appeared in any other run folder, and it has not been reproduced. The cause is not known. It could be a queued job whose reservations are on another level, or an ordinary vanilla reservation failure.

**Save and reload.** Each validation run saves and reloads once. Since run 144444 the harness no longer climbs the colonist up first. It saves with the colonist on the lower level, reloads, and checks the same level and cell. Run 134128 failed its reload check when it saved with the colonist downstairs, but that check expected the surface. It did not prove a lost level. Hauling, power and the wood are not rechecked after the reload. Run 135603 saved with the colonist on the surface.

**The reachability prefix can throw (found in run 193352, not fixed).** `ZLevels.CanReach_Patch.Prefix` (`HarmonyPatches/ReachAndReservationPatches.cs:71-85`) reads the pawn's stale `lookedAtLocalCellMap` outside a job search. It teleports the pawn to that map, then calls `mapDest.reachability.CanReach` at line 85, and the call threw `NullReferenceException`. The postfix that moves the pawn back does not run after an exception, so the pawn can stay on the other map. The harness now asks the vanilla question, with `TraverseMode.PassDoors` and no pawn. The mod prefix is unchanged. Normal play may reach it. It needs its own reproduction.

**A haul to an unreachable cell is retried (observed in run 192636, not diagnosed).** A colonist was offered a haul to a cell it could not path to. Each attempt ended with `ErroredPather`, and the job was offered again about every second. Vanilla avoids this by checking reachability before it offers the job. Whether the ZL reachability patch causes it is not shown.

## Runs after 135603: save below and unforced haul

| Run | Result |
|---|---|
| 144444 | PASS, all scenarios. The colonist waited below for 1800 ticks, was saved there, and reloaded on the lower level at the same cell. Run before the unforced haul stage was added. |
| 144826 | FAIL at stage 1. The colonist lay down to rest on the way (`JobGiver_GetRest`), and the stairs stage timed out after four minutes. Same stall as run 134553. The harness now holds rest, food and joy at full at the start and before the unforced haul. |
| 145345 | FAIL at stage 20, the unforced cross-level haul. The save below and reload checks PASS. Within four minutes the wood did not reach the stockpile, and the timeout shows the colonist's job as `Wait`. |
| 190222 | FAIL at stage 20. Debug logs (`-ZLevelsDebug`) show the cross-level leg runs once. After it, each new haul start for three colonists takes the same-map exit (pawn, item and destination on Level 0). The cause was not proven at this run. |
| 191130 | FAIL at stage 20. The colonist lay down at the lower stairs. Rest fell during the four-minute wait at superfast speed. The log has 187 `Test -1` starts in the window and one `Test 1` leg. |
| 191739 | FAIL at stage 20, timed out with the colonist at `Wait`. The colonist's leg ends with the queue still running. The queued `HaulToCell` starts and ends 2 ms later. Other colonists target the same log at the same time. |
| 192636 | FAIL at stage 20. Each haul to the zone ends with `ErroredPather`. The zone, (105, 0, 129), lies three cells north of the stairs exit and is walled off by rock. The harness cut out that one cell and did not check that it could be reached. |
| 193352 | FAIL at stage 15, before the haul. `ZLevels.CanReach_Patch.Prefix` throws `NullReferenceException` at `ReachAndReservationPatches.cs:85`. The harness called `CanReach` with a pawn outside a job search. |
| 193538 | FAIL at stage 20, "colonist went down to the wood". Surface wood reached the zone one second after the reload. The check counted any wood in the zone. |
| 193751 | PASS, all scenarios, `Runtime validation passed`. During stage 20 only the validation colonist may haul, and only the harness's seven logs may be hauled. The stage passes when those seven logs are in the zone. |
| 193912 | PASS. Control run: the original `InterruptForced` exit in `JobDriver_HaulThingToDest` with the other changes. The exit change is not needed. |
| 194056 | FAIL at stage 20, timed out. The colonist was idle (`Wait_Wander`) on the surface, and the seven logs were still at their spawn cell on the lower level. Nothing moved them. Cause not known. This is the final candidate tree. |
| 194633 | Debug run (`-ZLevelsDebug`). Every stage PASSED, including the unforced haul. The harness then failed its error check on `claimant ... can't reserve target` (`ReachAndReservationPatches.cs:274`). That line is written only in debug mode, when a reservation is refused. Acceptance runs use the default mode and never write it. |

**Save below (requirement: colonists may stay below).** The check passes. It covers one save after a short wait, so a long stay is untested. The mod was not changed for this. The earlier failure was the harness expecting the surface.

**Unforced cross-level haul (requirement: colonists fetch resources on another level by themselves). Passes in isolation, but not reliably (2 of 3 default runs).** Seven logs lie on the lower level, a stockpile is on the surface, and no job is forced. During the stage only the validation colonist may haul, and only the harness's seven logs may be hauled. The colonist walks down, carries the logs up, and they reach the stockpile. The other colonists' hauling is off for that stage, so this shows one colonist working alone. With hauling on for every colonist, runs 191739 and 192636 showed colonists competing for the same logs and the same zone. That is not resolved.

The earlier failures came from the harness, not from the cross-level job. The stockpile cell was walled off (run 192636). Surface wood filled the stockpile first (run 193538). Rest ran out during the wait (run 191130). The check also counted any wood in the stockpile. The `InterruptForced` exit in `Jobs/JobDriver_HaulThingToDest.cs` is unchanged, and run 193912 passed with it.

**Smooth transitions (requirement: transitions feel smooth to colonists).** Not tested. The current transition walks the colonist to the stairs and then teleports it on arrival. What counts as smooth is not defined yet.

## Review

The code-review skill ran at medium effort. Its findings and decisions:

| # | Finding | Decision |
|---|---|---|
| 1 | `DrawPos` postfix missing, so lower-level things drew at the surface offset | Fixed in `RenderPatches.cs` |
| 2 | `PlanetTile` string round trip | No change. The decompile confirms `ToString` and `TryParse`. Old integer-key saves are not tested |
| 3 | Animal prefix throws a `NullReferenceException` when the upper map has no `MapComponentZLevel` | Fixed: the try/catch body is restored |
| 4 | Animal group spawn removed | Fixed: restored |
| 5 | `StillValid` uses `SelectedPawns` rather than the menu's pawn | No change. Matches vanilla 1.7 |
| 6 | `RemoveAll` replaces enabled options with the same label | Fixed, narrowed: replaces only disabled vanilla options with the same label |
| 7 | `FindBedAcrossLevels` loop never moves candidates | Not a defect. The generator teleports both pawns before each yield. Comment added |
| 8 | Transmitter `UpdateDesiredPowerOutput` skips the base call | Fixed: calls the base. Output stays 0 because `DesiredPowerOutput` is 0 for this class |
| 9 | `ThingsOfDefAcrossLevels` reports cross-level material for non-stuff builds | No change. The only call site is the stuff branch of `Designator_Build.ProcessInput` |
| 10 | Capture and rescue bed search arguments | No change. Matches vanilla 1.7 `FindBedFor` calls |

The arrest postfix keeps its `!__result.Disabled` check. Vanilla returns a disabled option only for same-faction or no-path cases.

## Not tested

- **Multiplayer.** `MultiplayerSupport.cs` is optional and was not exercised.
- **Existing saves.** None were opened, as the project rules require. Old integer-key saves are parsed by `PlanetTile.TryParse`, which was read but not run.
- **Save and reload.** One reload per run. Hauling and power are not repeated after it.
- **DLC and other mods.** None were active.
- **Float menu.** No scenario opens it. Cross-level capture, rescue, arrest, food and work options are unverified in game.
- **Combat.** No scenario fights on purpose. The combat path that failed in run 035000 is fixed by code change and by its absence since.
- **Rivers.** The final run has no river. Underground rivers appeared in 2 of the 5 runs (032903 and 035409).
- **Rendering.** The visual checks are screenshots and one look by eye at the lower-level image. There is no pixel baseline, so the `DrawPos` offset is not measured.
- **Harmony.** 77 string-named targets were checked by name: 74 exist, and the other 3 are commented out upstream. Signatures were checked only for the points listed in `docs/game-api-findings.md`. No Harmony error appears in the logs. Not every signature was compared.
- **XML.** The XML was not audited field by field against the 1.7 defs. The `holdSnow` rename is the one confirmed. The game log does not report unknown XML fields, so a wrong field name would pass unnoticed.
- **Lower-level walking and digging.** The lower stairs open onto sealed rock. The harness clears and paves one cell for its haul. Walking beyond that cell and digging out are untested.
- **Animal reachability in fallback runs.** Only run 135155 measured animal reachability. The fallback runs did not.
- **Warnings.** The 8 build warnings were left in place. All come from upstream code or the package restore. The animal warnings are under Open issues. The Unity `Persistent allocation` lines (463 in run 135603) are logged by the native allocation runtime. They name no ZLevels class, and they were not investigated. The Vulkan loader lines name a Medal overlay layer on this machine. The Large Pawns metadata line concerns a mod outside the run's list.
- **Upstream debug output.** `Log.Message("Test -1")` in `Jobs/JobDriver_HaulThingToDest.cs:31` writes an INFO line when a haul job starts (2 lines in run 035538). It is unchanged.

## Reproduce

Build:

```
dotnet build ZLevels/Source/ZLevels/ZLevels/ZLevels.csproj -c Release
```

Validate (close RimWorld first):

```
powershell -ExecutionPolicy Bypass -File tools\Invoke-RuntimeValidation.ps1 -Visual
```

The last line should be `Runtime validation passed. Evidence: <run folder>`. The lower-level records are in `validation-results.txt`: `lower animals at arrival`, `lower walkable reachability`, and `lower stairs surroundings`. They report what the run found and do not count as PASS checks. Animal counts per run come from the `ZL_Underground` map in each run's `Saves/ZLevelsValidation.rws`.

## Evidence

- Build log: `.artifacts/build/release-build-final.log`
- Final run: `.artifacts/runs/20261028-135603/` (`validation-results.txt`, `Player.log`, `Screenshots/`, `Config/`, `Saves/`)
- Animal fallback runs: `.artifacts/runs/20261028-041107/`, `20261028-035000/`, `20261028-133324/`
- Animal reachability and caves: `.artifacts/runs/20261028-135155/`
- Reload check failure: `.artifacts/runs/20261028-134128/` (`Saves/ZLevelsValidation.rws`, colonist on map 1)
- Stage 1 timeout: `.artifacts/runs/20261028-134553/`
- Earlier runs: `.artifacts/runs/20261028-040440/`, `20261028-035409/`, `20261028-035538/`, `20261028-032903/`
- Decompiles: `.artifacts/ilspy/` (git-ignored)
