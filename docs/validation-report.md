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

5. **Retried haul to a walled storage cell (run 192636).** The storage search could choose a stockpile cell behind rock. Each haul to it ended with `ErroredPather`, and the job came back about every second. The reachability check in `IsGoodStoreCell` was commented out. It now runs for storage cells on the colonist's own level. See "Storage reachability (2026-10-31)".

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

**Debug runs fail on an expected reservation error (run `20261031-214306`).** In debug mode, `ReachAndReservationPatches.cs:280` logs `claimant ... can't reserve target` as an ERROR when a reservation fails. A reservation conflict is a normal outcome, for example when a colonist tries to refuel a generator that another colonist holds. The runner counts every ERROR line as a failure, so run `214306` passed every check and still exited 1. Earlier debug runs have the same line, for example `20261030-192636`. Not changed.

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

## Lower room and work hold (2026-10-30)

Task 1 of `docs/superpowers/plans/2026-10-30-cross-level-work.md` opens a room of natural rock around the lower stairs. This is test only, and the mod keeps the stairs sealed as designed. Run 202151 measured 53 walkable cells on the lower level, and 29 of them are reachable from the stairs. Stage 1 now requires at least 25.

The save-below stage now holds the colonist's work at zero during its wait. With the room open, a surface job can take the colonist up the stairs. Run 202009 failed "colonist stayed below before the save" that way. The work is restored before the unforced haul.

| Run | Result |
|---|---|
| 202009 | FAIL at stage 18. The colonist left the lower level within five seconds. The work hold was added after this run. |
| 202151 | PASS, all stages. |
| 202312 | FAIL at stage 20. The colonist was idle on the surface, and the logs did not move. Same failure as run 194056. |
| 202827 | Debug mode. All stages PASS. |
| 203026 | Debug mode. All stages PASS. |
| 203216 | PASS, all stages. |

Among the default runs that reached stage 20, one of three failed. The cause is not known. The timeout message now reports whether the logs are forbidden for the colonist, whether the colonist can reserve them, and the colonist's Hauling priority.

## Mining on the lower level (2026-10-30)

After the unforced haul passes, the harness places a mining designation on a rock wall at the edge of the lower room. It turns mining off for the other colonists. The validation colonist goes down by itself, and the stage passes when the wall is gone. This stage replaces the forced "reload stairs down" check, because the colonist now uses the stairs by itself after the reload.

| Run | Result |
|---|---|
| 203503 | Mining stage PASS. The run then timed out, because the harness no longer shut the game down. The shutdown is restored. |
| 205033 | PASS, all stages. The game exits on its own again. |

Building on the lower level is not tested yet.

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

## Infestation depth guard and stale look-up fix

- Infestations are blocked on level five and deeper, and on pocket maps. Run `20261030-215956` (`-InfestationGuard`) passes with no ERROR lines. The depth 4 control gets a hole, and depth 5 gets no record, no active infestation, and no hole.
- The guard-off control, run `20261029-111044`, fails as expected: depth 5 gets a record and a hole.
- Earlier guard runs failed on harness setup, not on the guard. The harness now forces caves below the surface and carves its own start and food cells. Run `20261029-111236` passed the scenario but logged 51 animal spawn errors, which `a6bb2e3` fixes.
- A stale look-up entry could make the reachability prefix use a map that had left the game. The prefix teleported the colonist onto that map and threw a `NullReferenceException`, as run `20261030-212014` showed after a reload. Red with the defect: `20261030-215738`. The fix in `bd17ecb` passes in runs `20261030-215858` and `20261030-220111`. The regression check is `-StaleLookup` (`5144db9`).
- The main chain passes with both fixes in run `20261030-220145`, with no ERROR lines.
- The campfire failures in runs `20261030-212212` and `20261030-210423` are still open. Four later runs with the look-up diagnostics passed (`213427`, `213543`, `213704`, `213816`), and the cause is unknown.

## Vault v1 entrance placement

- `ZL_VaultEntrance` is placeable on the surface only. `PlaceWorker_VaultSurface` refuses pocket maps and every level other than the surface, and the refusal text reaches the player.
- Run `20261031-110445` (`-VaultPlacement`) fails with the placement rule removed from `Vault.xml` for that run. The lower cell accepts the entrance, and the check throws `a lower level refuses a vault entrance`.
- Run `20261031-110532` passes with the rule in place. The surface takes the entrance. The lower cell refuses it with `Vault entrances can only be built on the surface.` The runner finds no relevant ERROR lines and prints a warning for unrelated Workshop duplicate-package errors.
- Runs `20261031-105856` (red) and `20261031-105952` (green) came from an earlier harness that did not unfog the lower level before the placement checks. The pair above is the evidence.
- The `portal` block and the pocket map come in Task 2.

## Vault v1 shared pocket

- Red run `20261031-111428` (`-VaultPocket`) fails because the entrance has no portal block.
- Three defects surfaced before the green run. Each one has a fix in `aba13be`.
  - Run `20261031-111614` threw a `NullReferenceException` in `GenerateMap`. The engine reads `map.Biome` during generation, and the pocket had none. The generator now sets `biome` to `Underground`.
  - Runs `20261031-111739` and `20261031-111900` recorded the pocket in the level index. The `MapComponentZLevel` guards alone did not stop it. `ZLevelsManager.GetZIndexFor` cached every map it was given. It now returns 0 for a pocket and records nothing.
  - Run `20261031-112223` ran without the `FinalizeInit` guard. `MapComponentZLevel.FinalizeInit` threw `ArgumentOutOfRangeException` at `Find.WorldGrid[map.Tile]`, and the runner failed on the logged error. The guard stays.
- Green run `20261031-115059` passes on the committed code. The first entrance makes the pocket, the pocket is in no level list, and a second entrance shares it with its own exit.

## Regression runs for the shared pocket

- `-InfestationGuard` (`20261031-112915`), `-StaleLookup` (`20261031-113001`), and `-VaultPlacement` (`20261031-113035`) pass on the guard build.
- The main chain fails at stage 22, the campfire stage, in two of four guard-build runs: `20261031-112409` and `20261031-113130`. The colonist wanders (`Wait_Wander`) instead of hauling wood to the campfire. Runs `20261031-114607` and `20261031-114837` pass on the same build.
- The main chain passes on the baseline build, which has the two guard files reverted to HEAD. The passing runs are `20261031-113739`, `20261031-114027`, `20261031-114249`, `20261031-114726`, and `20261031-114940`.
- Two failures in four guard-build runs, against zero in five baseline runs, is not a significant difference. The guards change behavior only when `IsPocketMap` is true, and ZLevels sets that flag only for the vault pocket. The stage 22 cause is found and fixed in the harness. See "Main chain stage 22" below.
- Runs `20261031-113922`, `20261031-114145`, and `20261031-114423` used a stale build and do not count. Their DLL was the baseline build while the source had the guards. Later batches rebuild on every state change and check for compile output.

## Vault v1 enter, build inside, and exit

- Red run `20261031-120628` (`-VaultEnter`) fails at the wall stage. The colonist is in the pocket, and the blueprint is placed, but the colonist wanders and the wall stays unbuilt.
- The cause is in `TryIssueJobPackagePatch`. For a humanlike pawn, the prefix replaces the vanilla job search with the mod's cross-level search. That search covers the maps the level tracker knows, and a pocket map is not one of them. The prefix now returns to the vanilla search when the pawn is on a pocket map. Other maps are unchanged.
- Green after that fix: `20261031-121640` (with `-ZLevelsDebug`), `20261031-121745`, `20261031-121832`, `20261031-121918`, `20261031-123838`, `20261031-123918`, `20261031-124003`, and `20261031-124049` (debug). Also `20261031-124228`, `20261031-124319`, `20261031-124414`, `20261031-124459`, `20261031-124723`, and `20261031-124813`. Each one passes with the colonist entering, the wall built, and the colonist leaving.
- Setup fix: the entrance used to go on a fixed offset from the centre. A random surface layout can put it where the colonist cannot reach it. Run `20261031-122503` recorded `colonist can reach the entrance | False` and timed out at stage 1. The entrance now goes on the nearest cell the colonist can reach, and every later run records `True`.
- Not explained: run `20261031-121150` (stage 1, the colonist never entered, before reachability was recorded) and run `20261031-122033` (stage 2, the wall stalled after the wood beside it dropped from 10 logs to 2). Run `20261031-122033` recorded that the entrance was reachable, but not the wall cell. Since the reachable-entrance fix, six runs have passed with no stall.
- The main chain still fails at stage 22 on the same code, at a rate the campfire notes already record. Failures: `20261031-112409` and `20261031-113130` on the guard build, and `20261031-122935` after the job-search fix. Passes: `20261031-114607` and `20261031-114837` on the guard build, and `20261031-123439` after the job-search fix. The baseline build passed in `20261031-113739`, `20261031-114027`, `20261031-114249`, `20261031-114726`, and `20261031-114940`.

## Vault v1 infestation on the pocket (acceptance g)

- `-VaultInfestation` (`-zlevelsvaultinfestation`) forces the pocket's cave flag on, puts food on the pocket, and fires the infestation incident at the pocket. It requires no infestation record, no active infestation, and no hole. It then runs the same setup on the surface as a control, which must dig a hole.
- The pocket check is `map.IsPocketMap` in `Patch_Infestation_TryExecuteWorker` (`HarmonyPatches/AnimalPatches.cs`). The depth check does not cover the pocket, because the pocket reports index 0 and levels down to -4 are allowed. So the pocket check is the only guard.
- Red: the check was removed for one run. Run `20261031-150543` then reported `records 0 -> 1, active 0 -> 1, holes 1`, and the scenario failed. The file was restored from HEAD afterwards. Only this run was red, and no committed code changed.
- Green: runs `20261031-150455` and `20261031-150625`. Both show `records 0 -> 0, active 0 -> 0, holes 0, caves below True` on the pocket and `holes 1` on the surface control.

## Vault v1 save and reload (acceptance f)

- `-VaultSave` (`-zlevelsvaultsave`) builds the entrance, has a colonist enter, builds a wall inside, and puts seven logs on the pocket floor. It then saves with the colonist in the pocket and checks, after reload, that the pocket exists, the entrance still leads to it, the colonist is on the pocket at the saved cell, the wall stands, and the logs are on the floor.
- Pass runs: `20261031-150003`, `20261031-150102`, and `20261031-150149`. The post-reload checks ran about five seconds after the save, so the reload happened. The runner reported no errors.
- No red run: the first real run passed, so no mod change was needed. The saved fields are the stage, the wall cell, and the log count. The colonist, the surface, and the stage come from the existing save fields.

## Vault v1 wealth (acceptance e)

- Red: run `20261031-145150` (`-VaultLoad`). The surface total stayed at 37766.6 and the storyteller wealth at 25698.6 after the seven logs (8.4) reached the pocket. Run `20261031-145333` showed the cause: the pocket held the logs (`pocket items 8.4`), and the surface's raw item count did not change.
- Cause: vanilla `WealthWatcher.ForceRecount` adds each pocket map's wealth to the map it came from, when `pocketMap.sourceMap == map`. The spec requires the logs to leave the surface's total, so the pocket's items cannot count for the surface.
- Fix: `ForceRecountVaultPocketPatch` in `HarmonyPatches/WealthPatches.cs`. It is a postfix on `ForceRecount` that subtracts the vault pocket's items, buildings, and floors. It only acts on the shared vault pocket. Pawns stay counted, because colonists are people, not stock. This is a decision: the spec does not say what a colonist inside the vault counts as.
- Green: run `20261031-145522`. The total fell from 20138.0 to 20129.6 and the storyteller wealth from 16335.3 to 16326.9. Both fell by 8.4, the logs' value.
- Regressions after the fix: the main chain (`20261031-145610`) and `-VaultEnter` (`20261031-145726`) pass.

## Vault v1 load list (acceptance d)

- `-VaultLoad` (`-zlevelsvaultload`) puts seven logs on the entrance's load list with `AddToTheToLoadList`. The test places the pocket's storage zone beside the exit, as the player would. Colonists haul the logs in with vanilla `WorkGiver_HaulToPortal`, with no forced job.
- Pass runs: `20261031-144723`, `20261031-144819` (debug), `20261031-144908`, and `20261031-144946`. Each shows the surface go from 307 logs to 300, the pocket hold 7, the load list end at 0, and every log in the zone or within 4 cells of the exit.
- Run `20261031-144622` failed on a check the test got wrong. It counted the colony's own 300 logs as left on the surface. The check now compares with the count before the seven were placed.
- No red run exists for this feature. The first real run passed, so no mod change was needed. The spec's open item (who places the storage zone) is still open. The test places the zone the way a player would, which matches the recommended default: the player places one.

## Main chain stage 22: the campfire rebuild stall (2026-10-31)

- Failure: the campfire build can fail. Vanilla `JobDriver_ConstructFinishFrame` rolls against the colonist's `ConstructSuccessChance` each tick. A failure calls `Frame.FailConstruction`. The frame goes, and 10 of the 20 delivered logs land on its cell. The new blueprint needs 20 logs again.
- Cause: the unforced-haul test forbids every other log on both levels and sets the other colonists' hauling priority to 0. Nothing lifts either before the campfire stage. The colony's other logs stay forbidden to the colonist, so the rebuild has only the 10 logs below, and it never starts. Run `173903` shows it: 10 logs below that the colonist may take, and 7 on the surface.
- Evidence for the failure: run `20261031-141006` (debug, no forced failure) has two campfire blueprint IDs, `142700` and `142703`. The `FinishFrame` job ends `Succeeded` just before the second blueprint appears. Each of the eight debug runs that passed (`133307`, `133550`, `133745`, `133909`, `135417`, `140434`, `140635`, `140845`) has one blueprint ID and no failure. The non-debug stalls `20261031-112409`, `113130`, `122935`, and `135821` show the same trail (logs on the surface, colonist wandering). Their logs carry no IDs, so their failure roll is not confirmed.
- The stage 1 stall in run `20261031-134036` was a harness gap. Stage 1 did not reset needs, so rest fell on the walk to the stairs at superfast speed, and the colonist lay down. Stage 1 now resets needs like the other stages.
- Harness change: the first frame fails once on purpose, so the rebuild is always tested. The rebuild record prints the logs on the level after the failure, and what the surface holds.
- Red with 20 logs: `20261031-142258`, `143044`, `163254`, `163924`, `164720`, and `173903`. Stage 22 times out with the colonist wandering.
- Fix: the harness releases the forbidden logs before the campfire stage (`ReleaseForbiddenWood`). The other colonists keep no work, as the test needs. The mod needed no change. Green with 20 logs: `20261031-175236`. The rebuild record shows `surface available True`, and the run shows `core validation | PASS`.
- The earlier fix hid the stall. Runs `143912`, `144104`, and `144206` used 40 logs. The rebuild then took everything from the lower level, so it never needed the surface. The campfire is back to 20 logs.
- Open, no decision yet:
  - A failed build can drop logs on the blueprint's own cell. The blocker haul then moved 10 logs to the surface (run `165858`).
  - `ConstructDeliverJobOnThing` in `HarmonyPatches/JobPatches.cs` loops over the levels but never moves the colonist. A search on another level therefore runs only while the colonist stands there. Run `172505` (debug) shows 415 searches from the lower level and none from the surface. The tested cases pass, so no check fails on it. Not changed.

## Cross-level supply (2026-10-31)

The `-Supply` scenario checks whether a job may take materials from the other level. Before each check, the scenario clears the logs and stone chunks on both levels. It also removes the campfires, walls, stonecutters, and blueprints that earlier checks built. The colony's own buildings stay.

| Check | Job | Materials | Result |
|---|---|---|---|
| S1 | Build below | 20 logs on the surface | Pass |
| S2 | Build on the surface | Stone blocks below | Pass |
| S3 | Craft on the surface | 20 chunks below | Pass |
| S4 | Craft below | 20 chunks on the surface | Pass |
| S5 | Refuel on the surface | 20 logs below | Pass |
| S6 | Refuel below | 20 logs on the surface | Pass |
| S7 | Build below | 10 logs below, 40 on the surface | Pass, all 20 taken from the surface |
| S8 | Build below | 40 logs on bare floor on the surface | Pass |
| S9 | Rebuild after a failed build | 20 logs below, 40 on the surface | Pass |

All nine pass in run `20261031-175919`. The mod needed no change. A job can take materials from the other level for building, crafting, and refuelling, in both directions. The scenario does not require it. In S7, the campfire took all 20 logs from the surface and left the 10 below.

Harness problems found and fixed while building the checks:

- A chunk stacks to 1 in the game. The 20-chunk stacks were cut to one chunk (run `20261031-154500`). The stacks are split now.
- A natural chunk on the surface was used before the test's chunks (run `20261031-155330`). Every stone chunk is cleared before each check now.
- Debug mode pauses the game on every logged error (`HarmonyPatches/Logging.cs`). Run `20261031-155330` froze at its first error. The harness keeps the game running during debug runs now.

Regression runs after these changes, all passing: main chain `20261031-175236`, VaultPlacement `175414`, VaultPocket `175448`, VaultEnter `175517`, VaultLoad `175603`, VaultSave `175640`, VaultInfestation `175732`, InfestationGuard `175805`, StaleLookup `175845`, and Supply `175919`.

Open:

- The test makes the storage zone itself (decision 2). A check with the real zone tool is still to do.
- Spilled materials can move to another level (see stage 22).

## Colonist autonomy (2026-10-31)

Plan: `docs/superpowers/plans/2026-10-31-colonist-autonomy.md`. Two harness flags let free colonists take the unforced cross-level haul. Neither changes the mod.

- `-AllColonistsHaul` (pass label `all colonists haul`). The other colonists keep their hauling. Stage 20 passes when the seven logs reach the zone and any colonist was below during the wait. No colonist may be below before the haul.
- `-OtherColonistHauls` (pass label `other colonist hauls across levels`). The same rules, and the validation colonist's hauling is off, so a colonist other than it must take the haul. The run records the colonists below during the wait.

| Run | Flag | Result | Colonists below during the haul |
|---|---|---|---|
| 20261031-193644 | `-AllColonistsHaul` | PASS, before the no-colonist-below check | not recorded |
| 20261031-193842 | `-AllColonistsHaul` | PASS | not recorded |
| 20261031-193939 | `-AllColonistsHaul` | PASS | not recorded |
| 20261031-194037 | `-AllColonistsHaul` | PASS | not recorded |
| 20261031-194231 | `-OtherColonistHauls` | PASS | Gin |
| 20261031-194328 | `-OtherColonistHauls` | PASS | O'Connell |
| 20261031-194425 | `-OtherColonistHauls` | PASS | Biscuit |
| 20261031-194734 | `-FreeColonists` (full chain) | PASS | not recorded |
| 20261031-195200 | `-FreeColonists` (full chain) | PASS | Rebecca, the validation colonist, mined and built |
| 20261031-201436 | `-OtherColonistsWork` (full chain) | PASS | Skvelt mined and built |
| 20261031-201557 | `-OtherColonistsWork` (full chain) | PASS | Darby mined and built |
| 20261031-201704 | `-OtherColonistsWork` (full chain) | PASS | Ash mined and built |

`-FreeColonists` runs the full chain with every colonist's work on. `-OtherColonistsWork` runs the full chain with the validation colonist's hauling, mining, and building off. Other colonists have no work until the unforced haul, and then their work is restored. The chain passes only if a colonist other than the validation colonist carried the logs during the haul, mined the wall, and built the campfire. The records `mined by` and `built by` name who did each job.

Before the hold change, `-OtherColonistsWork` failed five times: `195518` (stage 22, no frame ever started), `200303` and `200858` (stage 21, no colonist mined in four minutes), `200801` (stage 20, no colonist came below during the haul), and `200017` (stage 15, a start check that now applies only to `-AllColonistsHaul`). The only pass before the change was `195406`, where Lumi mined and built.

Single colonist: the validation colonist alone passed the unforced haul in 56 runs after run `20261030-205256`, the last stage 20 failure. That run used a colonist whose backstory disables work types, including Hauling. `FindValidationColonist` now skips such colonists.

Open:

- The hold is a test choice. Other colonists have no work until the unforced haul, so this variant does not show what they do before it.
- Four of the five failures before the hold change have no diagnosed cause. The timeout messages now list each other colonist's job and priorities (`DescribeOthers`). Three runs since the hold change is a small sample.
- The first four runs predate the record of colonists below, so their colonists are not known.

## Storage reachability (2026-10-31)

Plan: `docs/superpowers/plans/2026-10-31-storage-reachability.md`.

**Cause.** The reachability check in `IsGoodStoreCell` (`ZLevels/Source/ZLevels/ZLevels/HarmonyPatches/JobPatches.cs`) was commented out. The storage search picks the closest cell by straight-line distance, so a stockpile cell behind rock can win. Each haul to it ends with `ErroredPather`, and the job is offered again about every second.

**Fix.** A storage cell on the colonist's own level must be reachable from the colonist. The query starts at the colonist's position and uses `PathEndMode.OnCell`. Storage cells on other levels are unchanged. Checking them needs the stairs route, and this query cannot see it (plan, Ruling 1).

**Scenario.** `-zlevelsunreachablezone` (runner switch `-UnreachableZone`, pass label `unreachable zone`). The colony has two stockpile cells. The walled cell has walls on all eight sides. The reachable cell is farther from the logs and from the colonist, so the search picks the walled cell before the fix. Seven logs lie outside the walls. Only the validation colonist may haul. The run passes when no haul job targets the walled cell for 60 seconds and at least one targets the reachable cell.

**Counter limit.** The harness counts a haul job when it sees the job on a game tick. A job that starts and fails within one tick can be missed. Run `20261031-211055` is a debug run of an earlier version of the scenario, before the fix. Its log shows 8 haul jobs to the same walled cell, and 7 of them end with `ErroredPather`. The harness counted 1. A zero count shows that no attempt was seen. It does not show that none started.

| Run | Cells | Reachability check | Haul jobs to walled cell | Haul jobs to reachable cell | Result |
|---|---|---|---|---|---|
| `20261031-211443` | one | off | 1 | n/a | FAIL |
| `20261031-211859` | one | on | 0 | n/a | PASS. No other cell existed, so the run shows nothing about hauling. |
| `20261031-212210` | two | off | 1 | 0 | FAIL |
| `20261031-212346` | two | on | 0 | 1 | PASS |

Counts come from the harness. In run `212210`, the walled-cell haul targets `B = (105, 0, 129)`. In run `212346`, the reachable-cell haul targets `B = (133, 0, 124)`.

**Debug check (run `20261031-220806`).** The scenario passes in debug mode. Every haul-job line in the log names the target `B = (119, 0, 104)`, which is the reachable cell. The log has no `ErroredPather` line. This check reads the job log, so it does not depend on the harness counter.

**Regression set.** Fourteen runs on the fixed mod, 21:25 to 21:40 local. These ran before the harness changes below. Twelve passed.

| Variant | Run | Result |
|---|---|---|
| Main chain | `20261031-212558` | PASS |
| `-StaleLookup` | `212729` | PASS |
| `-Supply` | `212800` | PASS |
| `-VaultPlacement` | `213240` | FAIL: "a plain wall is accepted on the surface cell". The reason was not recorded. |
| `-VaultPocket` | `213310` | PASS |
| `-VaultEnter` | `213337` | PASS |
| `-VaultLoad` | `213417` | PASS |
| `-VaultSave` | `213455` | PASS |
| `-VaultInfestation` | `213535` | PASS |
| `-InfestationGuard` | `213604` | PASS |
| `-OtherColonistsWork` | `213642` | PASS |
| `-AllColonistsHaul` | `213745` | PASS |
| `-OtherColonistHauls` | `213839` | FAIL: stage 15, "no colonist below before the unforced haul". No names were recorded. |
| `-FreeColonists` | `213930` | PASS |

**Repeat runs for the two failures.** These ran with the expanded messages, before the unfog and the hold.

| Variant | Runs | Result |
|---|---|---|
| `-VaultPlacement` | `214410`, `214751`, `214941`, `215634` | PASS |
| `-VaultPlacement` | `215514` | FAIL: "Cannot place in undiscovered areas." The test cell was in fog. |
| `-OtherColonistHauls` | `214609`, `214658`, `215543`, `215708`, `215803` | PASS |
| `-OtherColonistHauls` | `214849` | FAIL: stage 15. Anna, a colonist other than the validation colonist, was on the lower level with no hauling job. |
| `-OtherColonistHauls` | `215012` | FAIL: stage 20 timeout. No colonist hauled the logs in the wait. The message did not list the other colonists, so the cause is not known. |
| `-OtherColonistHauls`, debug | `214306` | Every check passed. The runner exited 1 on a debug-only log line. See Open issues. |

**Harness changes after the batch.** These change test code only. Behavior for the core chain and the other variants is unchanged.

- Messages, made before the repeat runs. The placement and entrance messages give the reason and the pawns on the test cell. The no-colonist-below message names each colonist below, with its job and position.
- `-VaultPlacement` unfogs the test cell before the placement check. The game refuses placement in fog.
- `-AllColonistsHaul` and `-OtherColonistHauls` keep the other colonists' work off from the start until the unforced haul begins. `-OtherColonistsWork` already did this. The check that no colonist is below runs while they are still held.
- The stage 20 timeout message lists the other colonists' jobs and priorities for these variants.

**Verification after the harness changes.**

| Variant | Runs | Result |
|---|---|---|
| `-VaultPlacement` | `220111`, `220503` | PASS |
| `-OtherColonistHauls` | `220153`, `220357`, `220643` | PASS |
| `-AllColonistsHaul` | `220253`, `220541` | PASS |

Before the change, `-OtherColonistHauls` failed 3 of 8 non-debug runs, and `-VaultPlacement` failed 2 of 6. After the change, all 7 runs pass. That is a small sample. The stage 20 timeout in run `215012` has not recurred, and its cause is not known.

**Still open.**

- Storage cells on other levels are not checked. The check needs the stairs route (plan, Ruling 1).
- Route length (requirement 4 in `docs/superpowers/plans/2026-10-30-cross-level-work.md`) is open.
- `CanReach_Patch.Prefix` can still throw. See run `193352` in "Open issues". Not reproduced.
- The extra reachability query runs for each candidate cell that passes the earlier checks. Its frame cost is not measured.
- The stage 20 timeout in run `215012` has no cause yet.

## Multiple builds below and a staircase destroyed by damage (2026-10-31)

**Multiple builds below, logs below.** Scenario `-zlevelsmultibuild -zlevelsmultibuildlowerlogs` (runner switch `-MultiBuildLowerLogs`, pass label `multiple builds below`). Runs before 2026-11-01 passed `-MultiBuild` alone for this variant. Three wall sites lie in the lower room, with 20 logs beside them. Surface colonists take no hauling. The surface's haulable items are removed at the start, and any that appear later are removed too. The walls must be built from the lower level alone. The run passes when all three walls stand and no surface item is carried down.

**Logs on the surface.** Before 2026-11-01 this control was `-zlevelsmultibuildsurfacelogs`, and it failed on purpose. The same case is now the default `-zlevelsmultibuild`, which passes (see the next section).

| Run | Variant | Walls built | Surface items carried down | Walks to lower items | Result |
|---|---|---|---|---|---|
| `20261031-230253` | control | 3 of 3 | 1 | 0 | FAIL, as expected |
| `20261031-230355` | pass | 3 of 3 | 0 | 0 | PASS |
| `20261031-230502` | pass | 3 of 3 | 0 | 1 | PASS |
| `20261031-230547` | pass | 3 of 3 | 0 | 1 | PASS |
| `20261031-231051` | pass, final harness | 3 of 3 | 0 | 0 | PASS |

Items removed at the start: 1294, 1720, and 1203 in the three pass runs. The builders were Duster, Chant, and Lips. "Walks to lower items" counts colonists who walk down the stairs to pick up logs below. That is not hauling between levels, so it does not fail the run.

Earlier attempts. Runs `20261031-225614` to `230119` counted the walks as cross-level hauls. The monitor counts only items that start on the surface now. Run `20261031-225148` found no room for the sites. Run `20261031-225249` mined the room to a radius of nine. A roof collapse then damaged the lower staircase, which led to the exception below. The harness then failed on the validation colonist's needs, which were null. The damage source was not logged.

**Staircase destroyed by damage.** Scenario `-zlevelsstairsdamage` (runner switch `-StairsDamage`, pass label `stairs damage`). The validation colonist walks down, and the lower staircase takes 10,000 crush damage.

Runs `20261031-230657` and `230754` fail with `System.NullReferenceException`. The stack runs from `Building_StairsUp.GetMatchingStair()` (`ZLevels/Source/ZLevels/ZLevels/Buildings/Building_StairsUp.cs:139`), through `Building_Stairs.PostApplyDamage` (line 49), to `Thing.TakeDamage`. Once the damage destroys the staircase, `PostApplyDamage` still reads the staircase's map. Run `230754` records that the staircase is not spawned and has no map when the exception fires. Run `225249` reached the same exception during a game update.

Fixed 2026-11-01. Decision 4 is no, so a destroyed staircase does not hit its partner. `Building_Stairs.PostApplyDamage` now checks `if (syncDamage && Spawned)`, so a staircase that the damage has removed skips the partner hit. Red: run `20261101-112308` fails with the same exception on the code before the fix. The test now also checks that the surface staircase stands. Green: run `20261101-113033` passes. The lower staircase is destroyed, and the surface staircase is standing. Regression: core validation passes in run `20261101-113126`.

Run `20261101-112402` timed out at stage 0. The colonist wandered after the stairs job, so the lower level was never reached. The stairs cell was a fixed offset of three cells from the colonist, and nothing checked that the colonist could reach it. The harness now keeps that cell when the colonist can reach it. Otherwise it takes another cell three or four cells away that the colonist can reach, and it records the cell (`surface stairs cell`). The cause of run `112402` is not confirmed.

## Multiple builds below with the logs on the surface (2026-11-01)

Scenario `-zlevelsmultibuild` (runner switch `-MultiBuild`, pass label `multiple builds below`). Three wall sites lie in the lower room. Four piles of five logs lie on the surface beside the stairs. Other surface logs are removed at the start, so only the piles can feed the walls. Surface colonists keep their hauling, so the walls need cross-level hauls. Each wall uses five logs. The run passes when all three walls stand and at least 15 logs have left the surface.

| Run | Walls built | Logs that left the surface | Logs left below | Builder | Result |
|---|---|---|---|---|---|
| `20261101-105213` | 3 of 3 | 20 | 5 | Mac | PASS |
| `20261101-105259` | 3 of 3 | 20 | 5 | Elana | PASS |
| `20261101-105358` | 3 of 3 | 20 | 3 | Max | PASS |

The map count is the measure. The monitor records only ZL haul jobs, and it saw two per run. The four piles left the surface, so two of them moved without a job the monitor recorded. The builder column comes from sampling, so it can be empty.

**Logs below, surface hauling stopped.** Scenario `-zlevelsmultibuild -zlevelsmultibuildlowerlogs`. The logs lie below, and surface items are removed at the start. Run `20261101-105502` passes: 3 of 3 walls, no surface item carried down, and 1277 surface items removed. Earlier runs of this variant are listed under "logs below" above.

## Other mods (2026-11-01)

`tools/Invoke-RuntimeValidation.ps1 -ModsConfigSource <ModsConfig.xml>` runs the core validation with the mod list from that file. The source is only read. The run loads the same mods in the same order, with Z-Levels last, in an isolated savedata folder.

Readable Numbers (`7h0m4s.rimworldreadablenumbers`) is left out. It throws `NullReferenceException` in `RnGuiBackend.SetTextLanguage` during play-data load on a fresh save, even with the language set. Runs `20261101-133603` and `135707` stopped there.

Run `20261101-141256` passes `core validation` with 78 mods. Other mods logged these errors:
- `Aoba.DeadManSwitch.Core` is loaded from two Workshop copies. The game ignores the duplicate.
- `Milira.MiliraGameComponent_OverallControl.CheckMiliraPermanentEnemyStatus` throws `NullReferenceException` at new game start and at reload. Neither stack has a Z-Levels frame.

No error entry names Z-Levels code. The other-mod errors are in `other-mod-errors.txt` in the run folder.

## Cross-level cooling: reactor research

This section answers the research gate for Task 6 of the cross-level cooling plan. Rimatomics line numbers come from `.artifacts/ilspy/rimatomics-1.7-installed/Rimatomics/`, decompiled with `ilspycmd` from the installed `1.7/Assemblies/Rimatomics.dll`. RimWorld line numbers come from `.artifacts/ilspy/rimworld-1.7-installed-power/`, decompiled from the installed `Assembly-CSharp.dll` of RimWorld 1.7.4995 rev309.

The Rimatomics DLL has SHA-256 `41a1887390ce39fd0c07719c8765b8ce580678823b3a74618636507647fe5b51`. The earlier decompile in `.artifacts/ilspy/rimatomics/` is identical to it. `diff -rq` finds no difference in its 209 files, so the names in the plan are the installed names.

### Names match the installed code

`CoolingNet` extends `BasePipeNet` at `CoolingNet.cs`, line 7. Its fields are `CoolingCapacity` at line 9, `CoolingLoopRatio` at line 11, `Coolers` at line 13, and `Turbines` at line 15. `Tick` sets the ratio to 0 at line 27. It computes the ratio only when `Coolers` holds a cooler, at line 28.

`reactorCore` is declared at `reactorCore.cs`, line 14. Its fields are `FuelCount` at line 40, `IsShutdown` at line 50, `postReturnTemp` at line 80, `powerComp` at line 82, `SlotDesignations` at line 104, and `TargetControlRodTo` at line 111. The property `IsCoreMelt` is at line 157, and `IsOverheating` is at line 181. `Tick` starts at line 750.

`CoreA` extends `reactorCore` at `CoreA.cs`, line 6. The def `ReactorCoreA` names that class at `Buildings_C_Power.xml`, line 150.

`Turbine` is declared at `Turbine.cs`, line 10. Its fields are `UncappedPowerGeneration` at line 28 and `UncooledWater` at line 30. `Tick` starts at line 72. The def `Turbine` names the class `Thor` at `Buildings_C_Power.xml`, line 235.

`CoolingTower` extends `CoolingSystem` at `CoolingTower.cs`, line 8. Its `coolingCapacity` property spans lines 14 to 37. `CoolingSystem` declares `powerComp` at `CoolingSystem.cs`, line 16, and `Stalled` at line 18.

`Alert_ReactorAlerts` is declared at `Alert_ReactorAlerts.cs`, line 11. It adds the NOCOOLING text at line 89.

`DubDef.BuildReactorCore` is declared at `DubDef.cs`, line 70. `Complete` is declared at `GameComp_RimatomicsResearch.cs`, line 310. `RimatomicsResearch._instance` is declared at `RimatomicsResearch.cs`, line 31.

### The core and the tower need power

`CompPowerTrader.PowerOn` is a property over the field `powerOnInt`, at `RimWorld.CompPowerTrader.cs`, lines 16 and 74 to 134. The setter stores the new value at line 86, before its own checks run.

`PowerNet.PowerNetTick` sets `PowerOn` to true at line 240 of `RimWorld.PowerNet.cs`. It does so when the stored energy plus the net's gain covers the part's draw. One watt draws 1.6666667E-05 watt-days per tick, at `RimWorld.CompPower.cs`, line 20.

The core draws 1337 W, at `Buildings_C_Power.xml`, line 96. The tower draws 550 W, at line 550. The scenario has no generator, so a charged battery supplies each net. The base game `Battery` stores 600 watt-days, at `Data/Core/Defs/ThingDefs_Buildings/Buildings_Power.xml`, line 475. Its stored energy starts at 0, at `RimWorld.CompPowerBattery.cs`, line 9. Calling `SetStoredEnergyPct` with `1f` fills it, at line 100.

A connector joins a transmitting building within 6 cells, at `RimWorld.PowerConnectionMaker.cs`, lines 103 and 117.

The tower needs power for its capacity too. `coolingCapacity` returns 0 unless `powerComp.PowerOn` is true, at `CoolingTower.cs`, lines 28 to 36. With a capacity of 0, `Turbine.Tick` sets `UncooledWater` to the full output, at `Turbine.cs`, lines 92 to 94. An unpowered tower cools nothing. The plan's Task 6 check list does not name this.

### Layout for the tower, the turbine, and the core

The tower is 8 by 8, at `Buildings_C_Power.xml`, line 534. Its only pipe is `Cooling`, at lines 544 to 546. A roofed cell in its footprint sets `Stalled`, which the game checks every 250 ticks, at `CoolingTower.cs`, lines 86 to 94. A stalled tower gives 25,000 capacity instead of 250,000, at lines 32 and 34.

The `Turbine` def is 4 by 7, at `Buildings_C_Power.xml`, line 261. It has `HighVoltage`, `Steam`, `ColdWater`, and `Cooling` pipes, at lines 271 to 282. The `ReactorCoreA` def is 5 by 5, at line 157. It has `Steam`, `ColdWater`, and `Loom` pipes, at lines 85 to 93, and no `Cooling` pipe.

Pipes of one mode join when their footprints touch side by side. The flood fill that builds a net is called at `UniversalPipeMapComp.cs`, line 208. The game's `FloodFiller` walks the cardinal neighbours through `GenAdj.CardinalDirectionsAround`, at line 72 of the `Verse.FloodFiller` decompile. A building with a pipe of one mode forms a net by itself. So the core needs no pipe to share the turbine's steam and cold-water nets.

With `c` as the pairing cell, the core covers x `c-2` to `c+2` and z `c-12` to `c-8`. The turbine covers x `c-2` to `c+1` and z `c-7` to `c-1`. The two footprints touch along the rows at z `c-8` and `c-7`, for x `c-2` to `c+1`. The scenario uses no pipe. The pipe defs are `steamPipe` and `waterPipe` in Rimatomics' `Buildings_A_Pipes.xml`, at lines 181 and 255.

### Startup clears the flag and nothing else

`Startup()` sets `IsShutdown` to false and does nothing else, at `reactorCore.cs`, lines 503 to 506. The START button calls it, at `Dialog_ReactorConsole.cs`, line 515. `punchit()` also clears the flag, at `reactorCore.cs`, line 1026. The decompile has no call site for `punchit()`. `SCRAM()` and two automatic trips set the flag to true, at `reactorCore.cs`, lines 515, 806, and 811. The scenario calls `Startup()`.

### Placement workers do not run on a code spawn

`PlaceWorker_ResearchStage` appears only in the defs, at `Buildings_C_Power.xml`, lines 141 to 143 and 295 to 297. `Verse.GenSpawn` in the installed `Assembly-CSharp.dll` has no reference to a placement worker. So `GenSpawn.Spawn`, which the harness calls, skips the placement check.

### Research gate

`CoreA.IsBuilt` returns `DubDef.BuildReactorCore.IsFinished`, at `CoreA.cs`, line 12. While that is false, `reactorCore.Tick` returns before its own work, at `reactorCore.cs`, lines 763 to 766. `Complete` marks a step as completed, at `GameComp_RimatomicsResearch.cs`, lines 310 to 320.

The research component is `RimatomicsResearch._instance`. Its constructor sets that field, at `RimatomicsResearch.cs`, lines 33 to 37. `DubUtils.GetResearch()` returns it, at `DubUtils.cs`, lines 271 to 274. The core's `SpawnSetup` method calls `DubUtils.GetResearch().NotifyResearch()`, at `reactorCore.cs`, line 405.

### Fuel and control rods

`InsertFuel(Item_NuclearFuel)` stores a fuel rod only in a slot designated `RodDesignate.Fuel`, at `reactorCore.cs`, lines 518 to 534. The designations live in the public list `SlotDesignations`, at line 104. `RefreshFuelGrid` sets `FuelCount`, at lines 356 to 362. The fuel def is `FuelRods`, at `ThingDefs_Misc/Nuclear.xml`, line 186.

`TargetControlRodTo` is a public float, at `reactorCore.cs`, line 111. The rod request is 0 unless the core is powered, running, and holding fuel, at lines 270 to 280. `Interps()` moves the rod position toward `TargetControlRodTo`, at line 907.

### The temperature path

`Interps()` sets `postReturnTemp` to `RealTemp` plus `overheating`, at `reactorCore.cs`, line 927. With a turbine on the cold-water net, the `overheating` target is the cold-water feed return divided by 100, capped at `MaxPowerForFuelLevel`, at lines 918 to 921. The feed return sums the turbines' `UncooledWater` and divides by the core count, at `ColdWaterNet.cs`, line 26.

`UncooledWater` is 0 when the net has cooling capacity and its ratio is below 1, at `Turbine.cs`, lines 81 to 86.

`IsOverheating` is true above 500, at `reactorCore.cs`, line 181. `IsCoreMelt` is true above 2000, at line 157. Temperature predictions for 21 fresh rods do not apply to one rod. The runs record the real samples.

## Cross-level cooling: reactor scenario

The scenario runs after the reload in the cooling link sequence. The lower level holds the core and its battery, and the core's footprint touches the turbine's. A second battery powers the surface tower. A colonist is on the surface cell above the core.

The scenario loads 21 `FuelRods`, one into each of the core's 21 `Fuel` slots, and sets `TargetControlRodTo` to 1. `Startup()` runs once the core and the surface tower both read `PowerOn` and the tower is not stalled. The scenario then takes 11 samples, one every 250 ticks from `Startup()` to 2500 ticks. Each `reactor sample` row holds `postReturnTemp`, `IsShutdown`, `IsOverheating`, the loop ratio, and the cooler count.

The runner accepts one scenario switch per run. `-CoolingNoColumns` runs the same scenario without the column pair, and the no-columns run uses it alone. The reactor rows run in that variant too.

The `cooling link` row passes only when every label it needs passes. With `-Cooling`, those labels include the seven reactor rows below.

- `reactor runs on the lower level` passes when `IsShutdown` is false and `FuelCount` is 21 right after `Startup()`.
- `loop ratio at or below 1` passes when `CoolingLoopRatio` in the lower loop is 1 or less in every sample.
- `surface tower stays in the lower loop` passes when the surface tower is in the lower turbine's `Coolers` in every sample.
- `no cooling alert` passes when the lower turbine's `Coolers` holds at least one cooler in every sample. The alert itself is not tested.
- `no meltdown` passes when `IsCoreMelt` is false in every sample.
- `surface colonist not irradiated` passes when the colonist on the surface cell has no `RimatomicsRadiation` hediff at the end.
- `cooled core stays below overheating` passes when `IsOverheating` is false in every sample.

The no-columns run has one overheating row of its own, `uncooled core overheats`. It passes when `IsOverheating` is true in at least one sample by 2500 ticks. This row is the positive control. It shows that the same load overheats the core with no cooling. Without this row, a pass on `cooled core stays below overheating` could come from a load too small to overheat anything.

### Which checks can fail

The no-columns run reads a loop ratio of 0.0000 in every sample, because its lower loop has no cooler and no capacity. The loop ratio check passes there by construction, so that run gives no evidence on the ratio. The cooled run measures the loop. Its ratio rises to 0.5948 by 2500 ticks.

One unstalled tower caps the ratio near 0.6. The output of the 21-rod core peaked at about 148,700 W in both runs. One tower has a capacity of 250,000 W, so the ratio tops out near 0.595. The ratio can exceed 1 only if the tower stalls. No run has tested a stalled tower with the column pair in place.

The melt check cannot fail in this scenario, because the uncooled peaks stay under the melt limit of 2000. Those peaks are 1364.6 in run `20261103-131225` and 1486.9 in run `20261103-130813`. The colonist check passes by design, because the core's radiation scans only its own map. The alert check tests the cooler list, as the plan says, and does not test the alert.

### Roof over the tower footprint

The surface map differs from run to run, and the runner and the harness set no seed. A roof over any cell of the surface tower's footprint sets its `Stalled` flag. The flag updates at the tower's next 250-tick check, at `CoolingTower.cs`, lines 86 to 93. A stalled tower has a capacity of 25000, against 250000 when unstalled, at `CoolingTower.cs`, lines 30 to 34.

Run `20261103-130813` read a stalled tower in every sample. That run had no column pair, so the tower never joined the lower loop, and the stall did not change the loop readings. With the column pair, a stalled tower gives a ratio of about 5.95, since 148,700 W over 25,000 W is 5.95. No run has tested that case.

The scenario now clears the roof under the tower footprint and waits up to 1500 ticks for the `Stalled` flag to clear before `Startup()`. `OpenTowerFootprint` in `tools/RuntimeValidation/RuntimeValidation.cs` clears the cells and writes a `reactor tower footprint` row with the count. Run `20261103-131225` cleared 12 cells, and its tower read unstalled in every sample. Run `20261103-131411` found no roof to clear.

### Final runs

The two reactor runs used the 21-rod load and the roof handling above. The core run had no reactor.

| Run | Switch | Result |
|---|---|---|
| `20261103-131225` | `-CoolingNoColumns` | Fails as expected. The rows that need a column pair fail, and so do `surface tower stays in the lower loop`, `no cooling alert`, and `cooling link`. `uncooled core overheats` passes. |
| `20261103-131411` | `-Cooling` | Passes. All seven reactor rows and `cooling link` pass. |
| `20261103-131550` | none | Passes. No row reads FAIL. |

All runs in this section used the installed build, RimWorld 1.7.4995 rev309. The project target is 1.7.4990 rev1069.

### Temperatures at 21 rods

The table lists every sample from both reactor runs, from `Startup()` to 2500 ticks. The uncooled columns come from the no-columns run, and the cooled columns come from the cooled run.

| Ticks | Uncooled postReturnTemp | Uncooled IsOverheating | Uncooled IsShutdown | Cooled postReturnTemp | Cooled ratio |
|---|---|---|---|---|---|
| 0 | 0.0 | no | no | 0.0 | 0.0000 |
| 250 | 76.3 | no | no | 54.1 | 0.0909 |
| 500 | 391.7 | no | no | 132.9 | 0.2486 |
| 750 | 766.3 | yes | no | 212.7 | 0.3994 |
| 1000 | 1128.1 | yes | yes | 277.4 | 0.5226 |
| 1250 | 1364.6 | yes | yes | 304.0 | 0.5737 |
| 1500 | 1359.0 | yes | yes | 312.1 | 0.5893 |
| 1750 | 1305.2 | yes | yes | 314.3 | 0.5935 |
| 2000 | 1194.7 | yes | yes | 314.8 | 0.5945 |
| 2250 | 1010.4 | yes | yes | 315.0 | 0.5948 |
| 2500 | 757.6 | yes | yes | 315.0 | 0.5948 |

The cooled `postReturnTemp` matches the one-rod cooled run at every sample. `RealTemp` moves toward `StandardTemp` for any fuel count above zero, at `reactorCore.cs`, lines 258 to 268 and line 913. The cooled readings match `RealTemp` alone, because the loop ratio stays below 1 and the overheating term is 0.

The no-columns run crossed 500 between the +500 and +750 samples. It peaked at 1364.6 at +1250 ticks. The cooled run peaked at 315.0 at +2500 ticks and never read `IsOverheating` as true.

The no-columns run tripped once. `IsShutdown` first read true at the +1000 sample, so the trip came between the +750 and +1000 samples. The +750 sample read 766.3, and the +1000 sample read 1128.1, so the trip came after `postReturnTemp` passed 800. The only automatic trip this scene can reach is AutoScram. The flare trip needs an electricity outage, and the core and the tower stayed powered in every sample. The scram checks run every 240 ticks, at `reactorCore.cs`, line 802, so a trip can come up to 240 ticks after the threshold. After the trip, `UncappedPowerGeneration` fell to 0.0 by +2500 ticks, and `postReturnTemp` fell to 757.6, still above 500.

At one rod, the uncooled core settled 17.5 above the cooled core. That gap matched `ColdWaterFeedReturn` divided by 100 for an uncooled output of 1750 W, at `reactorCore.cs`, lines 918 to 927, and `ColdWaterNet.cs`, line 26. At 21 rods, the output reached about 148,700 W. A model without a trip predicted a plateau near 1,802 at 21 rods. The temperature fell after the trip, so it never reached that plateau.

### Earlier runs

The one-rod runs did not show overheating. The uncooled core peaked at 332.5 in `20261103-124931`, far below 500, so the load was raised to 21 rods. The core has 21 fuel slots, at `reactorCore.cs`, line 159, so 21 is the most rods it takes. The 21-rod run crossed 500, so no higher load was tried.

| Run | Load and switch | Result |
|---|---|---|
| `20261103-124931` | 1 rod, `-CoolingNoColumns` | The uncooled core peaked at 332.5 and never read `IsOverheating` as true. |
| `20261103-125122` | 1 rod, `-Cooling` | The cooled core settled at 315.0, and the loop ratio reached 0.0070. |
| `20261103-125300` | None | The core validation passed with 30 rows, and no row reads FAIL. |
| `20261103-130813` | 21 rods, `-CoolingNoColumns` | The tower read stalled in every sample. The uncooled core peaked at 1486.9 at +1500 ticks, and the first trip read at +1250 ticks. |

## Regression runs for cross-level cooling

These runs check the cooling link beside the other regression runs. Each run starts a new colony in its own savedata folder, under `-quicktest`. The tree under test is commit `a613670`. Its code matches `d7e2e30`, because `a613670` changes only the plan.

| Run | Command | Result |
|---|---|---|
| `20261103-134920` | Core validation, no switch | PASS. `core validation` reads PASS, and no row reads FAIL. |
| `20261103-134331` | `-Cooling`, with Dubs Rimatomics only | PASS. `cooling link` and `cooled core stays below overheating` read PASS, and no row reads FAIL. The cross-check reads 45 result rows and 45 marker lines. |
| `20261103-135031` | `-Cooling -RimatomicsPlusPlus` | PASS. `cooling link` and `cooled core stays below overheating` read PASS, and no row reads FAIL. The runner observes `rimatomics++ mod active` as PASS. |
| `20261103-135227` | Core validation with `-ModsConfigSource` set to the user's `ModsConfig.xml` | Not passed. The game reset the mod list during play-data load, and the runner stopped. No results file was written. |

The mod-list run stopped during play-data load. The game catches a `System.NullReferenceException` from `RimworldReadableNumbers.RnGuiBackend.SetTextLanguage`, and it resets the mod config. The user's `ModsConfig.xml` lists `7h0m4s.rimworldreadablenumbers`, which the other-mods run `20261101-141256` did not list. That run used 80 mods, which is the user's 79 without that mod, plus the two Z-Levels entries. The runner has never filtered that mod, so the other-mods source did not list it.

The language is not the cause. The user's `Prefs.xml` sets `English`, the run's `Config/Prefs.xml` copies it, and the exception still occurs.

The user's file is unchanged. Its SHA-256 is `C1BA0E321A6BD99E74932A7DAB3BEAF30C6D3CC6BDEB7DD4A08B99B39EACBE2A` before and after the run, and its write time is the same.

No run in this section tests the user's list with that mod removed. The expected line, `Runtime validation passed with other mods`, was not printed, so this section makes no claim that the user's list passes.

## Partial mod-list run: the user's list minus RimworldReadableNumbers

This is a partial run, and it does not show that the user's mod list passes. The full list fails at load in run `20261103-135227`. The exception comes from `RimworldReadableNumbers.RnGuiBackend.SetTextLanguage`. The call path runs through `Verse.LanguageDatabase.InitAllMetadata` and `Verse.PlayDataLoader.DoPlayLoad`. The game then resets the mod config. The stack has no Z-Levels frame.

The scratch copy is the user's `ModsConfig.xml` with `7h0m4s.rimworldreadablenumbers` removed, and nothing else changed. It lives at `%TEMP%\claude-scratch\task-7-modlist\ModsConfig.xml`. Its SHA-256 is `D041D62E6C7804B15A24D2E17A709007239E1EDB0346894C314AF19EF6108B3E`. The user's `Prefs.xml` sits beside it, so the run uses the same language as the failed run.

| Run | Command | Result |
|---|---|---|
| `20261103-135706` | Core validation with `-ModsConfigSource` set to the scratch copy | Passed. `core validation` reads PASS, and no row reads FAIL. The runner printed `Runtime validation passed with other mods`. That line covers the scratch list only. |

The run loaded 80 entries. The runner read them from the run's `Player.log`, after `Initializing new game with mods:`. They are the 78 entries of the scratch list, in order, plus `zlevels1.1` and `local.zlevels.validation`. They match the other-mods run `20261101-141256` exactly, IDs and notes. The game marks four entries with the note incompatible version: `adaptive.storage.framework`, `Sov.Nephilim`, `Ancot.AncotLibrary`, and `Ancot.MiliraRace`. The other-mods run marks the same four.

The entries, in log order, are: `brrainz.harmony`, `Chezhou.RimUnity`, `Ludeon.RimWorld`, `Ludeon.RimWorld.Royalty`, `Ludeon.RimWorld.Biotech`, `adaptive.storage.framework`, `sbz.NeatStorageFridge`, `sbz.NeatStorage`, `Mlie.SdAdvancedPowergeneration`, `Mlie.XNDUseThatSniperScope`, `OskarPotocki.VanillaFactionsExpanded.Core`, `sarg.alphaanimals`, `Mlie.ApparelOrganizer`, `com.bymarcin.ArchitectIcons`, `void.charactereditor`, `Mlie.ChooseBiomeCommonality`, `CrashM.ColorCodedMoodBar.11`, `Mlie.ColourfulSterileTiles`, `com.lessprisonbreaks`, `DimonSever000.BackstoriesPlus.Specific`, `DimonSever000.Events.Specific`, `DimonSever000.PsionicPlus.Specific`, `CaesarV6.DamageIndicators`, `telardo.DragSelect`, `Dubwise.DubsMintMenus`, `Dubwise.DubsPerformanceAnalyzer.steam`, `Dubwise.Rimatomics`, `zeracronius.dynamictradeinterface`, `V1024.EBFramework`, `V1024.EliteArchotech`, `asunib.EnemyMechanitors`, `Aoba.Exosuit.Framework`, `AOBA.Framework`, `Aoba.Fortress.Industrial`, `Goji.GojisMiscGenes`, `bambaryla.AllBambaGuns`, `MaoSaur.ImplantsRipper`, `erdelf.HumanoidAlienRaces`, `chezhou.creature.hivelord`, `m00nl1ght.UnofficialUpdates.HugsLogPublisher`, `m00nl1ght.MapPreview`, `hyaukyuu.mechlinkreinforcement`, `CaptainMcMuffin.MHP`, `Haplo.Miscellaneous.Training`, `Yorbil.missingfabricationrecipes`, `IssacZhuang.MuzzleFlash`, `AmCh.NeedBarOverflow`, `Andromeda.NiceBillTab`, `Andromeda.MilkyWay`, `Andromeda.NiceHealthTab`, `Andromeda.NiceResearchTab`, `sk.noprisonersympathy`, `morton.NobleandPsycasterFurniture`, `AgentOrange.PassiveExoskeletons`, `cem.PositiveConnections`, `Mlie.PrisonersDontHaveKeys`, `Mlie.ProstheticNoMissingBodyParts`, `DawnsGlow.qualcolor`, `NachoToast.RimatomicsPlusPlus`, `Dubwise.Rimefeller`, `Mlie.RepairingPriority`, `forti.rimmarket`, `rimsenal.EVP`, `EdelweissPirate.traumateamcomplete`, `CH4AcKO3.SearchAndRescue`, `EBSG.Framework`, `Sov.Nephilim`, `Sovereign.SeraphimReborn`, `dhultgren.smarterconstruction`, `Indeed.StackXXL`, `Aoba.DeadManSwitch.Core`, `Aoba.DeadManSwitch.AncientCorps`, `Aoba.DeadManSwitch.MobileDragoon`, `Aoba.DeadManSwitch.Taktikmagier`, `TimmyLiang.TradeHelper`, `VanillaExpanded.VChemfuelE`, `Ancot.AncotLibrary`, `Ancot.MiliraRace`, `ZLevels1.1`, `local.zlevels.validation`

The run logged three other-mod errors. They are in `other-mod-errors.txt` in the run folder, and none names Z-Levels code.

The user's file is unchanged. Its SHA-256 is `C1BA0E321A6BD99E74932A7DAB3BEAF30C6D3CC6BDEB7DD4A08B99B39EACBE2A` before and after both runs, and its write time is the same.

This section does not claim that the user's list passes. The user's list still contains `7h0m4s.rimworldreadablenumbers`, and that entry fails at load.

## Cross-level cooling

This section records the cross-level cooling work. The plan is `docs/superpowers/plans/2026-11-01-cross-level-cooling.md`. The design is in `docs/superpowers/specs/2026-11-01-cross-level-cooling-design.md`, and the decisions are in `docs/adr/0001` to `0005`. The detail sits in four earlier sections: "Cross-level cooling: reactor research", "Cross-level cooling: reactor scenario", "Regression runs for cross-level cooling", and "Partial mod-list run: the user's list minus RimworldReadableNumbers".

### Installed game build

The installed game is RimWorld 1.7.4995 rev309. The plan targets 1.7.4990 rev1069. All 167 run folders dated 2026-11-02 and 2026-11-03 that have a `Player.log` record 1.7.4995 rev309. The other-mods run `20261101-141256` records the same build. Every run cited in this section uses the installed build.

### Red and green runs

Each row gives the run that failed before the change and the run that passed after it. Run IDs are folders under `.artifacts/runs/`.

| Step | Red run | Green run | Notes |
|---|---|---|---|
| Task 3, column def | `20261102-104330`: the three def rows FAIL, column missing | `20261102-105720`: the three def rows PASS | The look is under Untested cases. |
| Task 4, pairing within three cells | `20261102-115623`: pairing rows FAIL, registry not built | `20261102-115859`: the four Task 4 rows PASS | Replaced by the same-cell rule. |
| Task 4, same cell only | `20261102-125457`: `column one cell away stays unpaired` FAIL, the column one cell away shares the loop | `20261102-125613`: that row PASS, and the other Task 4 rows still PASS | Commit `dd55200`. |
| Task 4b, partner highlight | `20261102-214741`: both highlight rows FAIL, highlight not built | `20261102-221308`: both highlight rows PASS, and the six Task 4 rows PASS | The look run is `20261102-221353`. |
| Task 5, loop merge | `20261103-041745`: the three link rows FAIL | `20261103-041951`: the three link rows PASS | The reactor rows were still stubs. The last in-game run, `20261103-120341`, shows the three link rows PASS. |
| Task 6, round 1 | `20261103-124931`, no columns: the link, tower, and alert rows FAIL | `20261103-125122`: all rows PASS | One fuel rod. Round 2 replaced this scenario. |
| Task 6, round 2 | `20261103-131225`, no columns, 21 rods: `uncooled core overheats` PASS, and the link, tower, and alert rows FAIL | `20261103-131411`, 21 rods: all rows PASS, including `cooled core stays below overheating` | Core validation `20261103-131550` passes. |
| Task 6, round 3 | None. The red run was not repeated, and the uncooled row is unchanged. | `20261103-134331`: all rows PASS | The cooled check now fails when the core trips. |

Round 1 could not show a cooling effect on temperature. With one fuel rod, the uncooled core peaked at 332.5 and never overheated. Round 2 raised the load to 21 rods. The uncooled core then overheated in run `20261103-131225`, and `IsOverheating` first read true at +750 ticks.

### Research

The Task 6 research summarizes the Dubs Rimatomics code that the reactor scenario depends on. The full file is `.superpowers/sdd/2026-11-01-cross-level-cooling/task-6-research.md`. The `.superpowers/` folder is git-ignored, so that file is not in the repository. These points come from the file.

- `Startup()` clears `IsShutdown` and checks nothing else. The core also needs power, at least one fuel rod, and raised control rods.
- `postReturnTemp` is `RealTemp` plus `overheating`. The thresholds are 500 for overheating, 2000 for melt, and 50 for cold.
- An unstalled tower gives 250,000 W, and a stalled tower gives 25,000 W. With 21 rods, the core makes at most 148,750 W. One unstalled tower therefore gives a ratio of at most 0.595. A stalled tower gives about 5.95.
- `emitRadiation` scans only the map it receives. The core does not reach the surface cell above it, as ADR-0004 states. A fallout condition can reach that cell, but only while it is active on the surface map.
- A scratch model levels off at 315 with one unstalled tower. With no cooling, it passes 500 at tick 563. The model is not a game run, and it is not in the repository.
- The file lists six points it could not confirm from the code. One is whether the decompile matches the installed Rimatomics DLL.

### Untested cases

- Water flow through the column is not simulated. The spec puts it out of scope, and ADR-0003 says the link moves capacity, not water.
- The Remove Cooling designator is not run. The harness removes the surface column with `Destroy(DestroyMode.Vanish)` in `RemoveSurfaceColumn` instead.
- The Task 3 look rests on one screenshot. Run `20261102-110012` shows a grey pipe with a red wheel on the column cell, read at twelve times zoom. The column covers about 20 pixels at game zoom. Runs `20261102-104827` and `20261102-105806` show nothing on that cell. Run `20261102-112616`, without `linkType` `None`, shows only the linked teal pipe art. The committed def therefore uses drawer `RealtimeOnly` and `linkType` `None`, which differs from the plan's Task 3 values. The comment-only commit `d19a012` has no run after it. The report does not record a check at normal zoom.
- The Task 4b partner highlight look rests on run `20261102-221353`. The amber crop, taken before the lower column stands there, shows an amber square on the empty cell. The green crop, taken after it stands there, shows a green square on the same cell with the column's red wheel inside. Both crops are six times zoom. The look also clears cells and places a torch lamp, which the feature does not need. Open items: the report says a person still needs to check the screenshots by eye. The cause of the GPU device loss in two of five earlier look runs is unknown, and the hook avoids the call that caused it. The report lists no run that removes a column while the outline is drawn, or that saves and reloads with the outline showing.
- Three Task 6 checks pass without the link:
  - `loop ratio at or below 1` cannot fail with one unstalled tower, because the ratio is at most 0.595. It fails only if the tower stalls, and no run has stalled it on purpose. It passed in both no-columns red runs, `20261103-124931` and `20261103-131225`, where the ratio reads 0 with no coolers.
  - `no meltdown` cannot fail in this scenario. The uncooled peaks were 1364.6 in run `20261103-131225` and 1486.9 in run `20261103-130813`, both under the melt limit of 2000. It passed in both no-columns red runs.
  - `surface colonist not irradiated` passes by design under ADR-0004. It passed in both no-columns red runs, so it does not test the link.
- The synthetic model is the only test of these Task 5 items. No game run covers them.
  - The carried cap. A cap that is pending when the game changes writes a carried line in the next game. No run carried a cap, and the wording is unconfirmed.
  - The missing-pipe path. A column with no Rimatomics pipe logs an error and does not join the registry.
  - The flush postfix. Held lines flush from a postfix on `GameComponentUtility.GameComponentTick`. No run held a line, so the in-game flush did not run.
  - The per-line stop at Verse's 10,000th message, the count carried across a game change, and the def and map key.
  - A valid loop beside an unusable column. An unusable column forms no loop, and the other loops of its net still join, as spec decision 2 says. The report records no in-game run of this case.
- The same-cell check for a column on a level above the surface is not run. The harness builds one level below the surface only.
- Rimatomics++ has one cooled run. `20261103-135031` passes every row, including `cooling link`. No breakage was seen in that run, and no other Rimatomics++ run was made.

### Mod-list status

- The full user list fails at load in run `20261103-135227`. During play-data load, `RimworldReadableNumbers.RnGuiBackend.SetTextLanguage` throws a `NullReferenceException`. The game then resets the mod config.
- Run `20261103-135706` uses a scratch copy of the list without `7h0m4s.rimworldreadablenumbers`. Core validation passes. The runner printed `Runtime validation passed with other mods`, and that line covers the scratch list only.
- The run loads 80 entries. They match run `20261101-141256` in order and in IDs, ignoring letter case.
- Four entries load with the note `incompatible version`: `adaptive.storage.framework`, `Sov.Nephilim`, `Ancot.AncotLibrary`, and `Ancot.MiliraRace`.
- The user's list still contains `7h0m4s.rimworldreadablenumbers`. Lucas has not decided whether to remove it, so this section does not claim that the user's list passes.

## Vault rock and exit cap (2026-11-04)

The vault pocket is underground. It is solid rock with the underground's ore, no caves, and a clear room of radius 10 at the centre. The exit stands at the centre. The `-VaultRock` scenario checks that layout, then forces a mine job on a wall beside the room. Normal work selection is not tested. The `-VaultFull` scenario checks the exit cap, including entrances and blueprints that are built before any is entered. Source: `GenStep_VaultRock`, `VaultExitSpot.RoomRadius`, and the `-VaultRock` and `-VaultFull` scenarios in `tools/RuntimeValidation/RuntimeValidation.cs`.

Each run was made in a clean worktree, with the game's mod links pointed at that worktree for that run, then restored. Runs from `20261104-144403` on use the exit cap and the rock-types row. Runs from `20261104-152239` on also use the claimed-exit count and the build-before-entry flow. Earlier runs are kept as history, and each entry says which code it ran.

### Final proof

- Green `-VaultRock` `20261104-155628` passes every row, including the rock-types row. The colonist enters the pocket and mines the wall beside the room. The final row is `vault rock | PASS`.
- Green `-VaultFull` `20261104-155555` builds 20 entrances and enters each one as it goes. It builds 4 more without entering them, and places 3 blueprints. The room refuses the next placement with `The vault has no room for another entrance.` The blueprints are then built, all 27 entrances are entered, and each one gets an exit. The final row is `vault full | PASS`.
- Red `-VaultFull` `20261104-152239` runs on the committed mod code, which does not count entrances or blueprints that are not entered yet. The room accepts 60 placements and refuses none. The final row is `vault full | FAIL`.
- The regression runs on the final code are `-VaultPlacement` `20261104-155741`, `-VaultPocket` `155814`, `-VaultEnter` `155848`, `-VaultLoad` `155941`, `-VaultSave` `160024`, `-VaultInfestation` `160118`, `-InfestationGuard` `160146`, and the core run `160224`. All exit 0.

### Earlier runs

- Green `-VaultRock` `20261104-144512` ran with the cap, before the claimed-exit count. Every row passes. Superseded by `20261104-155628`.
- `-VaultFull` `20261104-144438` ran with the cap, before the claimed-exit count. It placed 27 entrances, and the 28th was refused. Superseded by `20261104-155555`.
- Red `-VaultFull` `20261104-144403` ran on the code before the cap. The scenario fails with `The vault room has no room for another exit.` thrown from `VaultExitSpot.Find`.
- Regression runs `-VaultPlacement` `20261104-144706` through the core run `145155` ran on the cap code, before the claimed-exit count. All exit 0. Superseded by the set above.
- Red `-VaultRock` `20261104-134847` runs on the original mod code, the flat floor. Three of the four checks it runs fail. 9683 cells outside the room hold no rock, the exit is at (3, 0, 3) instead of the centre (50, 0, 50), and the pocket holds no ore wall. The room-standable check passes, as it does on the flat floor. The final row is `vault rock | FAIL`. This run predates the rock-types row. The original code cannot run the current harness, because the harness reads `VaultExitSpot.RoomRadius`, so this run stands as the red proof for the layout.
- Green `-VaultRock` `20261104-134919` runs on `e1dbc60`, and every row passes. It predates the rock-types row.
- Red `-VaultRock` `20261104-132942` and green `20261104-133017` ran on the harness before `e1dbc60`. They allowed the exit one cell off the centre. Sol raised that in round 1 of review, and `e1dbc60` made the check exact. Their other rows match.
- First regression pass `20261104-132622` (`-VaultInfestation`): the three scenario rows pass, and the runner's log gate fails. The harness put the infestation meal at the map centre, where the exit now stands. Its clean-up destroyed the exit, and the engine logged `Tried to destroy non-destroyable thing ZL_VaultExit`. The harness now puts the meal two cells east of the centre. This is a harness fix, not a mod change.
- Regression runs `-VaultPlacement` through the core run, `20261104-133226` to `133659`, all pass. They ran on the harness before `e1dbc60` and on the mod code before the cap, so the final set above is the proof.

### Not tested and known limits

- Raids or threats on the vault pocket, and existing saves. A pocket made before this change keeps its old floor, and it is not regenerated.
- A deconstructed entrance leaves its exit in the pocket, because a portal never removes its exit. The claimed-exit count still includes that exit, so the room fills sooner. Not tested.
- A colonist can build inside the room. A built wall can make a cell non-standable, so a later exit may find no cell, and entering that entrance fails. Not tested.

## Evidence

- Other-mod run: `.artifacts/runs/20261101-141256/`
- Build log: `.artifacts/build/release-build-final.log`
- Final run: `.artifacts/runs/20261028-135603/` (`validation-results.txt`, `Player.log`, `Screenshots/`, `Config/`, `Saves/`)
- Animal fallback runs: `.artifacts/runs/20261028-041107/`, `20261028-035000/`, `20261028-133324/`
- Animal reachability and caves: `.artifacts/runs/20261028-135155/`
- Reload check failure: `.artifacts/runs/20261028-134128/` (`Saves/ZLevelsValidation.rws`, colonist on map 1)
- Stage 1 timeout: `.artifacts/runs/20261028-134553/`
- Earlier runs: `.artifacts/runs/20261028-040440/`, `20261028-035409/`, `20261028-035538/`, `20261028-032903/`
- Decompiles: `.artifacts/ilspy/` (git-ignored)
- Cross-level cooling reactor runs: `.artifacts/runs/20261103-131225/` is the no-columns run, `.artifacts/runs/20261103-131411/` is the cooled run, and `.artifacts/runs/20261103-131550/` is the core validation run.
- Earlier cross-level cooling runs: `.artifacts/runs/20261103-124931/`, `.artifacts/runs/20261103-125122/`, `.artifacts/runs/20261103-125300/`, and `.artifacts/runs/20261103-130813/`.
- Task 7 regression runs: `.artifacts/runs/20261103-134920/`, `.artifacts/runs/20261103-134331/`, `.artifacts/runs/20261103-135031/`, and `.artifacts/runs/20261103-135227/`.
- Partial mod-list run without RimworldReadableNumbers: `.artifacts/runs/20261103-135706/`.

## Commit method for shared files (2026-11-05)

Another session's uncommitted vault work sits in `tools/RuntimeValidation/RuntimeValidation.cs` and `tools/Invoke-RuntimeValidation.ps1`. `git commit -- <file>` takes the whole working-tree file, so it would commit that work too. A shared file is committed in three steps:

1. Write the committed text with only this task's edit: `git show HEAD:<file> | sed '<this task's edit>' > <scratch file>`.
2. Store that text as a blob and point the index at it: `git hash-object -w <scratch file>` prints the blob, and `git update-index --cacheinfo 100644,<blob>,<file>` sets the index entry.
3. Check `git diff --cached --stat` lists only this task's lines and files, then run `git commit -F <message file>` with no path arguments.

After the commit the working copy still holds the other session's changes, so git status keeps the file as modified. That is expected. Committed text is LF, which `git show HEAD:<file> | tr -cd '\r' | wc -c` confirms.

## Search timing baseline

Recorded 2026-11-06 for Task 19 of the multi-floor ideas plan. Phase B changes are measured against these numbers, so the method is part of the record.

### What the scenario times

Run it with `tools/Invoke-RuntimeValidation.ps1 -SearchTiming`. Its required row is `search timing recorded`.

- Ten colonists each run the work search 50 times, which makes 500 calls per setup. Each call is `TryIssueJobPackage` on the colonist's normal `JobGiver_Work`, reached through `SearchWorkJob`.
- Three setups run in order in one game. One floor is the surface alone. Two floors add the lower floor. Three floors add the upper floor as well.
- Each setup first makes one untimed search per colonist. Then the clock moves 601 ticks before every timed call, one tick past `FloorPolicy.IdlePauseTicks`. Without that step, the idle pause makes the vanilla search answer, and the timing measures that search instead.
- The 500 timed calls form 50 rounds of ten calls. The rows give the first round, the median round, the slowest round, and the garbage collections inside the timed window.
- A map tick is `Map.MapPreTick` followed by `Map.MapPostTick`. Those are the two per-map calls that `TickManager.DoSingleTick` makes. Version 1.7.4998 has no `Map.MapTick` method. Things on a map tick through the global tick lists, so their cost is outside these numbers.
- The map ticks run after all three search setups. The ticks spawn animals and plants, and a spawn would change a later search.

### Repeats

Three runs used the same code: `20261106-082234`, `20261106-082409` and `20261106-082518`. Each run starts a new random game, and each setup is timed in that game.

The colonists come from a fixed random state, and the lower floor is generated in that state too. The lower floor still depends on the random surface, so its walkable cells vary. The enabled work types total 120 in `20261106-082234` and 118 in the other two runs.

Every timed call found no job in every run. The search cost per setup:

| Run | Setup | Mean us per call | Median round in ms | Slowest round in ms | Cross-floor searches | Garbage collections gen0/gen1/gen2 |
|---|---|---|---|---|---|---|
| 20261106-082234 | One floor | 359.5 | 3.3 | 5.1 | 0 | 0/0/0 |
| 20261106-082234 | Two floors | 587.7 | 5.5 | 13.1 | 34200 | 2/2/1 |
| 20261106-082234 | Three floors | 564.5 | 5.4 | 8.2 | 34200 | 0/0/0 |
| 20261106-082409 | One floor | 412.7 | 3.7 | 6.0 | 0 | 0/0/0 |
| 20261106-082409 | Two floors | 747.4 | 7.6 | 32.7 | 34500 | 1/1/0 |
| 20261106-082409 | Three floors | 443.3 | 3.6 | 25.4 | 34500 | 1/1/0 |
| 20261106-082518 | One floor | 405.6 | 3.5 | 16.3 | 0 | 1/1/0 |
| 20261106-082518 | Two floors | 526.0 | 4.5 | 24.9 | 34500 | 1/1/0 |
| 20261106-082518 | Three floors | 492.2 | 3.8 | 32.2 | 34500 | 2/2/1 |

Each map tick row covers 300 ticks:

| Run | Lower floor, us per tick | Upper floor, us per tick | Surface, reference, us per tick |
|---|---|---|---|
| 20261106-082234 | 51.2 | 75.4 | 291.1 |
| 20261106-082409 | 50.0 | 76.8 | 264.7 |
| 20261106-082518 | 54.8 | 106.0 | 51.3 |

The surface before the floors were built, in each repeat:

| Run | Walkable cells | Roofed cells | Things | Lower floor walkable cells | Enabled work types |
|---|---|---|---|---|---|
| 20261106-082234 | 55149 | 5736 | 34215 | 690 | 120 |
| 20261106-082409 | 59659 | 1972 | 43754 | 255 | 118 |
| 20261106-082518 | 59427 | 2253 | 16092 | 20 | 118 |

The upper floor changed none of these surface counts in any run. The lower floor removed at most one thing from the surface.

### Reading the numbers

One floor was the cheapest setup in all three repeats. Two floors cost 30 to 81 percent more per call than one floor. Three floors cost 7 to 57 percent more.

Three floors ran faster than two floors in five of the six runs, by 4 to 58 percent. In `20261106-082036` the two were level, and three floors was 1 percent slower.

The cause of the gap is not known. Three things were checked, and none of them explains it. The upper floor changed no surface counts. Garbage collection did not favor the faster setup: in `20261106-082518` the three-floor window had more collections than the two-floor window, and it ran faster. The round medians in `20261106-082409` are 7.6 ms for two floors and 3.6 ms for three floors, so the gap is not one slow round. One candidate is not tested: the two-floor setup is timed before the upper floor exists. A follow-up would time the two-floor setup again after the upper floor exists, or give each setup its own run.

### Limits

- The surface is random in every run, because the runner starts a new random game. Its counts differ from run to run, as the table shows. Costs that depend on the surface vary with it, including the surface reference tick.
- The lower floor depends on the random surface, so its walkable cells ran from 20 to 690 in the repeats.
- The seed fixes the colonists' random draws, but the enabled work types still differ by two between the first repeat and the other two.
- Every timed call found no job. These numbers measure idle searches. A search that finds work is not measured.
- Runs `20261106-081614`, `20261106-081820` and `20261106-082036` are exploratory. Their colonists and lower floors were not seeded, so they differ from the repeats and from one another. They also record different rows. Run `20261106-081614` has no round, collection or surface rows. Run `20261106-081820` has no surface rows. Run `20261106-082036` has all of them.
- The runner builds the working tree, which held uncommitted vault work from another session. The timed build therefore differs from commit `dfd2686` by that work.
- Each run has one `Player.log` error line. It is the duplicate package warning for `Aoba.DeadManSwitch.Core` in the installed mod list. It is not a Z-Levels error.

### Exploratory runs

These runs are not repeats of the final method.

- `20261106-081614`: one floor 378.3 us per call, two floors 685.0, three floors 314.7. Cross-floor searches 34600 in both multi-floor setups. Lower floor 6563 walkable cells.
- `20261106-081820`: one floor 566.6 us per call, two floors 487.7, three floors 207.1. Cross-floor searches 34600 in both multi-floor setups. Lower floor 97 walkable cells.
- `20261106-082036`: one floor 308.5 us per call, two floors 521.5, three floors 528.5. Cross-floor searches 28950 in both multi-floor setups. Lower floor 5671 walkable cells.

### Run folders

- Repeats: `.artifacts/runs/20261106-082234/`, `.artifacts/runs/20261106-082409/` and `.artifacts/runs/20261106-082518/`.
- Exploratory: `.artifacts/runs/20261106-081614/`, `.artifacts/runs/20261106-081820/` and `.artifacts/runs/20261106-082036/`.

Each folder holds `validation-results.txt`, which has the rows, and `Player.log`.

## Lighter update on extra floors

Recorded 2026-11-06 for Task 20 of the multi-floor ideas plan. The change is committed as `89e95e3`, and the step cost rows as `f5b349e`.

### What changed

Four steps of the map tick skip on any floor whose Z index is not 0: the wind update, wild animal spawning, the choice of the next weather, and passing ships. The surface runs all four. Each step is a Harmony prefix on the game's own tick method. The prefixes are in `HarmonyPatches/ExtraFloorTickPatches.cs`.

### Red and green

Run the scenario with `tools/Invoke-RuntimeValidation.ps1 -LighterFloors`. Its required row is `lighter floors`. The rows are `no wild spawn on the lower floor in 2000 ticks`, `lower floor weather does not change`, and `the surface still runs its weather`.

- Red, run `20261106-083418`: the spawn row fails with 2 wild animals spawned on the lower floor. The weather row fails because the lower floor's weather decider ran. The surface row passes. The patch was not in the code.
- Green, run `20261106-083516`: all rows pass. The entry row shows 3 wild animal kinds acceptable on the lower floor.
- Green rerun with the step cost rows, run `20261106-084041`: all rows pass.
- Completion check through task-done, run `20261106-084157`: passes.

An earlier red, run `20261106-083222`, did not fail the spawn row. The spawner found no entry cell, because it needs an edge the colony can reach. The scenario now opens the lower floor's outer ring, removes the roofs there, and places one colonist on the floor. Both red runs ran without the patch.

### Core regression

The core validation passes with the change: run `20261106-083608`.

### Benefit

The Task 19 timing ran three times on the patched code: `20261106-083754`, `20261106-083827` and `20261106-083901`. The table compares means and ranges with the three Task 19 repeats.

| Measure | Task 19 repeats, mean | Task 19 repeats, range | Patched runs, mean | Patched runs, range |
|---|---|---|---|---|
| Map tick, lower floor, us per tick | 52.0 | 50.0 to 54.8 | 56.0 | 44.7 to 62.0 |
| Map tick, upper floor, us per tick | 86.1 | 75.4 to 106.0 | 100.7 | 94.4 to 106.9 |
| Search, one floor, us per call | 392.6 | 359.5 to 412.7 | 387.3 | 357.5 to 416.7 |
| Search, two floors, us per call | 620.4 | 526.0 to 747.4 | 595.9 | 485.2 to 714.5 |
| Search, three floors, us per call | 500.0 | 443.3 to 564.5 | 454.6 | 306.4 to 541.5 |

No measurable benefit shows. The lower floor mean rose by 4.0 us per tick, and the upper floor mean rose by 14.6 us per tick. Both differences lie inside the run-to-run spread.

### Step cost on the surface

Run `20261106-084041` times each skipped step alone on the surface, 300 calls each, which keeps all four steps. Wind costs 20.4 us per call. Wild spawning costs 0.5 us, weather choice 0.8 us, and passing ships 0.5 us.

The extra-floor cost of each step is not measured, because the patch skips the step on those floors. The reason for the missing benefit is therefore not known. Either wind costs far less on the extra floors than on the surface, or the spread hides the difference. Three runs cannot separate the two.

### Limits

- Wind and passing ships have no row. Their skip is covered by the code and by the core regression, not by a red and green pair.
- The benefit is not shown. The means move the wrong way on both floors, but inside the spread.
- The surface reference tick in the patched runs stays at 241 to 283 us per tick, as expected, because the surface is not skipped.

### Run folders

- Red: `.artifacts/runs/20261106-083222/`, which had no entry cell and is not a valid red, and `.artifacts/runs/20261106-083418/`.
- Green: `.artifacts/runs/20261106-083516/`, `.artifacts/runs/20261106-084041/` and `.artifacts/runs/20261106-084157/`.
- Core: `.artifacts/runs/20261106-083608/`.
- Benefit: `.artifacts/runs/20261106-083754/`, `.artifacts/runs/20261106-083827/` and `.artifacts/runs/20261106-083901/`.

## Own floor first

Task 21 of the multi-floor ideas plan, idea 8 on `docs/mod-comparison/ideas.html`. Code commit `ad922eb`. This section records the change, the red and green runs, the regression runs, the search timing and the limits.

### What changed

- A colonist's work search checks its own floor before the other floors. Work at or above the threshold priority is checked on every floor first. Lower-priority work on another floor is taken only when the own floor has none.
- The threshold is `ZLevelsSettings.WorkGiverHighPriorityThreshold`. It defaults to 2, and the mod settings slider runs from 0 to 4. A value of 0 turns the high pass off. The threshold reads the game's work priorities, so with "Use work priorities" off the high pass does nothing.
- The copied `TryIssueJobPackage` is removed from `JobPatches.cs`, about 430 lines. The vanilla search runs once per floor through `OwnFloorFirstSearch` in `HarmonyPatches/OwnFloorFirstPatch.cs`. Each pass sets which work givers the search may check.
- While the own-floor search runs, the haul, bill, construction, refuel, tend and rescue givers answer with the helpers the copy used (`CrossFloorGiverJob`). Each giver keeps the copy's choice of validator and job. Rescue's job and tend's validator stay vanilla, as in the copy. A helper that calls back into its own giver gets the vanilla method, which stops the refuel helper from calling itself.
- Two fixes came out of the regression runs:
  - `IsGoodStoreCell` sets the look-up map to the stockpile's own map for the check, and restores the old map after it. The floor iterator leaves the last floor it yielded recorded, so own-floor cells were checked on the floor below, and every one was refused.
  - In `BuildJobListFor`, a tend job without medicine no longer becomes a `ZL_HaulThingToDest` with no target. Without medicine, the colonist walks to the patient's floor only when that floor is not its own.
- The own-floor check and the job-target check give their colonists the work types they set. Some backstories disable work types, and the setup logged an error for a colonist who could not haul. `SpawnCapableColonistNear` retries until the colonist has the work types the row needs.

### Red and green

Own-floor check (`-OwnFloorFirst`):

| Run | State | Result |
|---|---|---|
| 20261106-085341 | Red, on the copy (commit `1c47e0c`) | Own-floor work: 9 searches on other floors. FAIL |
| 20261106-091952 | Green, first version of the new search | All rows PASS |
| 20261106-101952 | Haul givers patched, storage fix not yet in | Rows 1 and 3 FAIL: the own-floor steel haul returns no job |
| 20261106-120655 | Probe run | The stockpile cell on `Map-0` is checked against the look-up map `Map-1` |
| 20261106-130035 | Fixture set hauling on a colonist who could not haul | All rows PASS, but the runner refused the run on an error line |
| 20261106-130231 | Final | All rows PASS, no error lines |

Job-target check (`-JobTargets`, new):

| Run | State | Result |
|---|---|---|
| 20261106-125712 | Red, before the tend fix | FAIL: a `ZL_HaulThingToDest` with no target |
| 20261106-125839 | Green, after the tend fix | PASS |

Core failure found by the regression set:

- `20261106-123103`: the core run timed out at stage 17. The log had five `JobDriver_HaulThingToDest: targetA.Thing is null` errors for one colonist, each from a new `ZL_HaulThingToDest` job.
- I did not capture the job type behind those errors. The cause rests on the error text, on the code, and on the job-target check, which reproduces the same error on the tend branch. In the searches this change touches, no other branch of `BuildJobListFor` builds a null target that I could find. Vanilla `CanRefuel` returns false when no fuel is reachable, before any refuel job is made, so the refuel branch cannot get a fuel-less job from these searches.
- On the final code, the core run passed in `20261106-125914`, `20261106-130707` and `20261106-130823`.

### Regression runs on the final code

| Run | Scenario | Result |
|---|---|---|
| 20261106-130231 | `-OwnFloorFirst` | PASS, five rows |
| 20261106-130112 | `-OtherColonistHauls` | PASS |
| 20261106-130356 | `-AllColonistsHaul` | PASS |
| 20261106-130309 | `-MultiBuild` | PASS |
| 20261106-130458 | `-Cooling` | PASS |
| 20261106-125839 | `-JobTargets` | PASS |
| 20261106-125914, 20261106-130707, 20261106-130823 | Core, three runs | PASS, 33 rows each |
| 20261106-130625, 20261106-131125, 20261106-131158 | `-SearchTiming`, three runs | PASS |

### Search timing

Same method as the Task 19 baseline. Microseconds per call. The baseline column is the mean of its three runs. The final columns are the three runs on the final code.

| Setup | Task 19 baseline mean | 130625 | 131125 | 131158 | Final mean |
|---|---|---|---|---|---|
| One floor | 392.6 | 391.3 | 282.3 | 531.9 | 401.8 |
| Two floors | 620.4 | 493.6 | 231.6 | 394.3 | 373.2 |
| Three floors | 500.0 | 187.5 | 214.1 | 346.7 | 249.4 |

Map ticks, microseconds per tick, 300 ticks per row. This change does not touch map ticks, so these rows are a control.

| Run | Lower | Upper | Surface |
|---|---|---|---|
| 20261106-130625 | 48.2 | 62.6 | 216.3 |
| 20261106-131125 | 47.3 | 86.2 | 212.9 |
| 20261106-131158 | 38.9 | 61.9 | 212.4 |

Reading the numbers:

- The one-floor setup runs no cross-floor search, and its three runs range from 282 to 532 µs. The noise in this method is about as large as the differences above.
- The two- and three-floor means are below the baseline. The three runs do not separate from the noise, so no speed-up is claimed.
- The cross-floor column is not comparable with the baseline. It counts calls to `FloorPolicy.Allowed`. The search now makes one call per pass, so the count fell from 34200 to 4300 to 6050. The counter itself is unchanged.

### Limits

- The cross-floor tend branch (no medicine, patient on another floor) runs `ZL_GoToMap`, but no row checks it.
- The job-target check covers the tend branch only. The refuel argument rests on the vanilla code, not on a row.
- The job type behind the errors in `20261106-123103` was not captured.
- The threshold reads the game's priorities. A player who turns "Use work priorities" off gets no threshold effect.
- Warning CS0219 for `dest` in `HasHaulJobOnThing` (`JobPatches.cs`) is in HEAD. This change did not add it.
- Core failed intermittently while this task ran. One failure after the storage fix (`20261106-123103`) was traced to the tend branch. The final code passed core three times. Three passes do not prove that the intermittent failure is gone.

### Run folders

- Own floor first: `.artifacts/runs/20261106-085341/` (red), `20261106-091952/` (green), `20261106-130231/` (final).
- Job targets: `.artifacts/runs/20261106-125712/` (red), `20261106-125839/` (green).
- Regression: `20261106-130112/`, `20261106-130356/`, `20261106-130309/`, `20261106-130458/`.
- Core: `20261106-123103/` (failed), `20261106-125914/`, `20261106-130707/`, `20261106-130823/`.
- Search timing: `20261106-130625/`, `20261106-131125/`, `20261106-131158/`.

## Job taken on arrival

Task 23 of the multi-floor ideas plan, idea "check the job on arrival" on `docs/mod-comparison/ideas.html`. Code change in `ZLevels/Source/ZLevels/ZLevels/Jobs/Toils_ZLevels.cs` (`GetArrivalCheck`, `MainJobTargetIsGone`). Scenario `-JobTakenOnArrival`, required row `job taken on arrival`.

### What changed

- `Toils_ZLevels.GoToMap` checks the colonist's main job when the colonist reaches the stairs. The check sits between `goToStairs` and `useStairs`.
- A target is gone when it is destroyed, or when it is neither spawned nor held by the colonist. A held target is still in use. A main job with no thing as its target is never gone.
- A gone target resets the colonist's job tracker, then ends the walk with `JobCondition.Incompletable`. The colonist searches again from the stairs.
- The runner's pass gate reads `ERROR` case-sensitively now. The row label "no error and no pause" had tripped the case-insensitive gate, which refused a run with all rows passing (`20261106-134820`).

### Red and green

| Run | State | Result |
|---|---|---|
| 20261106-134820 | First draft, steel destroyed | Rows passed. The walk ended at once, so the draft did not test the stairs. The gate refused the run on the row label |
| 20261106-135139 | Draft, steel taken away, before the fix | Row "target taken during the walk" FAIL: the colonist went down and idled on the lower floor |
| 20261106-140850 | Final scenario, committed code | FAIL: the colonist takes the stairs down for a steel that is already gone, and no haul of the second steel follows |
| 20261106-140805 | Final scenario, fixed code | All four rows PASS: the colonist ends the stale walk at the surface stairs, then hauls the second steel on its own floor |

The brief expected the red to show an error or a pause. It did not. The red that failed is the colonist walking down for a steel that is gone. The "no error and no pause" row passes on both versions.

### Test setup

- The steel is taken away with `DeSpawn`, not destroyed. A destroyed target ends the walking job at once, before the stairs.
- The second steel is placed on the surface after the walk starts, so the first search still picks the lower steel.
- The other start colonists have Hauling set to no-work during setup. One of them stored the second steel before the check ran in `20261106-135529` and `20261106-135808`.
- The check compares what a job targets. Job objects are pooled, and a comparison of job objects missed the new haul in `20261106-140546`.

### Regression runs

| Run | Scenario | Result |
|---|---|---|
| 20261106-141005 | -ReservationRefused (the Task 4 pattern) | PASS |
| 20261106-141038 | -OtherColonistHauls | PASS |
| 20261106-141140 | -AllColonistsHaul | PASS |
| 20261106-141247 | -StairRoutes | PASS |
| 20261106-141324 | -BackgroundHaul | PASS |
| 20261106-141401 | -JobTargets | PASS |
| 20261106-141433 | -OwnFloorFirst | PASS |
| 20261106-141510 | -MultiBuild | PASS |
| 20261106-141607 | -Cooling | PASS |
| 20261106-141734 | core (no switch) | PASS |

### Limits

- Only the cross-floor walk through `GoToMap` is checked. `FindRouteWithStairs`, used by `JobDriver_GoToLocation`, is unchanged. No work chain in `ZLevelsManager` builds that job.
- The planned "error or pause" red did not occur. The observed red is documented above.
- In the failing state, a diagnostic found the look-up map still on the lower floor after the colonist walked down, and no store cell found for the surface steel. This was not investigated. The fixed path does not reach that state.
- Task 24, which would remove the reach and reservation patches, is not started. The Task 19 numbers time the whole search per floor count, so they do not show those patches cost time.

## Raids land on the ground

Task 25 of the multi-floor ideas plan, idea 3 on `docs/mod-comparison/ideas.html`. Code: `ZLevels/Source/ZLevels/ZLevels/HarmonyPatches/RaidGroundPatch.cs`. Scenario `-RaidGround`, required row `raid ground`.

### What changed

- A prefix on the enemy raid's `TryExecuteWorker` aims a raid at a lower map at the surface of the same tile. The faction, points, strategy and arrival mode are kept.
- A raid on the surface, or on a pocket map, is unchanged. If the surface lookup fails, the prefix logs an error and leaves the target alone.

### Red and green

| Run | State | Result |
|---|---|---|
| 20261106-142216 | Scenario, no patch | Row 1 FAIL: no raider on the surface, `fired False` |
| 20261106-142335 | Scenario with the spawn diagnostic, no patch | Row 1 FAIL: `fired False`. The faction (The Spears) resolved, and the spawn center was none. Row 2 PASS: 6 raiders |
| 20261106-142431 | With the patch | All rows PASS: 5 raiders on the surface, none on the lower map. The surface raid: 5 raiders on the surface |

Before the change, a raid aimed at the lower map never fired. Its arrival is an edge walk-in, which needs an open entry cell, and the underground floor has none. The row fails because no raider reaches the surface.

### Regression runs

| Run | Scenario | Result |
|---|---|---|
| 20261106-142526 | core (no switch) | PASS |
| 20261106-142654 | -JobTakenOnArrival | PASS |
| 20261106-142736 | -OtherColonistHauls | PASS |

### Limits

- The scenario uses the edge walk-in, which spawns raiders at once. Drop-pod arrivals (EdgeDrop, CenterDrop) are not exercised.
- Outside debug mode the reroute writes no log line. The row counts are the proof.

## Leave by stairs

Task 26 of the multi-floor ideas plan, idea 4 (DM9 on `docs/mod-comparison/ideas.html`): a pawn on a lower or upper floor walks to the stairs toward the surface, changes floor, and leaves by the surface exit. Code: `ZLevels/Source/ZLevels/ZLevels/Jobs/LeaveByStairs.cs`. Scenario `-LeaveFromLower`, required row `leave from lower`.

### What changed

- A prefix on `JobGiver_ExitMap.TryGiveJob` (the method every exit job goes through, including the exit duty) routes a pawn on a lower or upper floor to the stairs toward the surface. The route is a `ZL_GoToMap` job whose target is the surface of the same tile.
- A pawn on the surface, on a pocket map, or on a floor with no stairs toward the surface keeps the vanilla exit. A downed pawn that cannot crawl keeps it too.
- The scenario gives the visitor the game's own exit lord, `LordJob_ExitMapBest`. The think tree reaches an exit duty only through its lord, so a bare duty is never used.

### Red and green

| Run | State | Result |
|---|---|---|
| 20261106-145250 | Lord-based scenario, route patch removed | FAIL. The visitor walks on the lower floor to its west edge at (0, 0, 12) and leaves the lower map there. It never reaches the stairs or the surface. |
| 20261106-145118 | Lord-based scenario, with the patch | PASS. The visitor walks to the stairs at (2, 0, 182), changes to the surface map, walks to the exit and leaves at (0, 0, 182). All three rows pass. |

Earlier runs gave the visitor a bare exit duty with no lord: 20261106-143153 (red), 143526 and 143835 (green attempts), and the trace run 144223. The think tree never used that duty in those runs, so they test a state the game does not produce. They are not evidence for the change.

### Regression runs

| Run | Scenario | Result |
|---|---|---|
| 20261106-145543 | core (no switch) | PASS |
| 20261106-145659 | -JobTakenOnArrival | PASS |
| 20261106-145737 | -RaidGround | PASS |
| 20261106-145814 | -OtherColonistHauls | PASS |
| 20261106-145917 | -StairRoutes | PASS |
| 20261106-145954 | -ReservationRefused | PASS |

### Limits

- The scenario covers the exit lord with defend-self off. The defend-self, escort, random-exit and drop-pod leaves are not exercised. Thieves, kidnappers and caravans are not tested on their own.
- The red outcome depends on the generated layout. In run 145250 the level had 738 walkable cells and the vanilla exit reached its west edge. A layout with no reachable edge leaves the visitor standing on the lower floor instead, which the scenario also fails.
- The lord stays on the lower map while the pawn walks on the surface. The scenario does not check the lord after the pawn leaves.

## Game events to check

Task 27 of the multi-floor ideas plan. Each event was tested first, as the brief asks, and code changed only where a row failed. Two events failed, and both fixes are in code that the failing rows exercise. Each scenario is a separate runner switch.

### What changed

- `ZLevels/Common/Defs/Jobs/Jobs.xml`: `ZL_GoToMap` sets `dropThingBeforeJob` to false and `carryThingAfterJob` to true, as vanilla `Goto` does. The game's job tracker drops a carried thing before any job whose `dropThingBeforeJob` is true, and that is the default. A pawn that starts a floor change while it carries something lost it.
- `ZLevels/Source/ZLevels/ZLevels/HarmonyPatches/ReachAndReservationPatches.cs`: the two branches of the `Reachability.CanReach` prefix that move the pawn to the target's floor run the reach check from the arrival cell there. They used the pawn's position on the surface.

Events whose rows passed were not changed.

### Red and green

| Item | Scenario | Red | Green |
|---|---|---|---|
| a. Parties and weddings on another floor | `-EventsOtherFloor` | none: the first run passed (151227) | 151227 PASS; 155500 PASS in the regression |
| b. Lover thoughts across floors | `-MoodOtherFloor` | none: the first run passed (151000) | 151000 PASS; 155423 PASS in the regression |
| c. Falling objects and the roof | `-SkyfallerRoof` | 150535 FAIL, from my check's blast radius | 150648 PASS, no game change |
| d. Thief carrying a colonist down | `-ThiefCarry` | 153500 FAIL: the colonist is dropped at the start of the route job | 153552 PASS after the `ZL_GoToMap` flags |
| e. Children, animals and mechs | `-ChildAnimalMech` | 154059 FAIL: from the surface coordinates the lower floor reports the animal unreachable (False); from the stairs cell it reports reachable (True) | 154153 PASS after the arrival-cell fix |

Run 20261106-150535 failed because the scenario used a radius of two cells. The chunk covers 2 by 2 cells, and vanilla `Skyfaller.HitRoof` breaks the roof over the footprint plus one cell. The check now uses that rectangle.

Earlier runs of d (151506, 151906, 152227, 152622, 153030) put the thief where the pathfinder cannot reach the stairs, so the route job ended with `ErroredPather`. Those runs are not evidence for the change, and the setup was corrected in the run that followed.

Runs 153721 and 153854 of e expected a surface colonist to be unable to reach a lower animal. The mod allows that through the stairs on purpose, so the expectation was wrong. The rows were rewritten to test the stairs route and a sealed rock pocket with no route. The pocket row passed in each run that had it, from 153854 onward.

### Regression runs

| Run | Scenario | Result |
|---|---|---|
| 20261106-154249 | Core (no switch) | PASS |
| 20261106-154402 | -StairRoutes | PASS |
| 20261106-154439 | -JobTakenOnArrival | PASS |
| 20261106-154516 | -LeaveFromLower | PASS |
| 20261106-154559 | -RaidGround | PASS |
| 20261106-154636 | -OtherColonistHauls | PASS |
| 20261106-154739 | -AllColonistsHaul | PASS |
| 20261106-154836 | -BackgroundHaul | PASS |
| 20261106-154913 | -JobTargets | PASS |
| 20261106-154951 | -OwnFloorFirst | PASS |
| 20261106-155028 | -MultiBuild | PASS |
| 20261106-155140 | -ReservationRefused | PASS |
| 20261106-155213 | -Cooling | PASS |
| 20261106-155345 | -SkyfallerRoof | PASS |
| 20261106-155423 | -MoodOtherFloor | PASS |
| 20261106-155500 | -EventsOtherFloor | PASS |
| 20261106-155618 | -ThiefCarry | PASS |
| 20261106-155700 | -ChildAnimalMech | PASS |

### Limits

- a: a party and a wedding on the lower floor. Rituals need Ideology data, which this install does not have. The title ceremony runs from a quest, and no quest was set up.
- b: one thought, `WantToSleepWithSpouseOrLover`, read for a lover on the lower floor. Of the alert classes that read maps, 75 use `Find.Maps` and so cover every floor. `Alert_AnimaLinkingReady` reads only the current map. It is an Anomaly alert, and no run exercised it.
- c: `ShipChunkIncoming` on the surface. Drop pods and explosive skyfallers are not run, and neither is a landing on the lower floor.
- d: a thief with the exit lord. The kidnapping job is not run.
- e: a wild Muffalo, with the reach and reservation checks that roping, charging and child jobs depend on. Mech charging, the child routines and the roping jobs themselves are not run. Their Biotech setup is not built.
- The reach fix changes which targets on another floor a pawn can reach. The 18 regression runs show no failure. They do not measure how much more cross-floor work the colony finds.

## Fighting across floors

Task 28 of the multi-floor ideas plan. The scenario is `-CrossFloorFight`, run by `RunCrossFloorFightTick` in `tools/RuntimeValidation/EventScenarios.cs`. No game code changed. The shot row failed at first, and that failure is a finding about the game's line-of-fire rule. Decision D6 (option a) keeps the rule, applied as the default on 2026-11-07. The shot row now expects the refusal, and it passes. See "Decision D6" below.

### What the scenario checks

The scenario builds a lower floor with stairs up to the surface, then runs three checks in order.

1. A hostile on the lower floor shoots at a colonist on the surface for 1800 ticks. The hostile holds its cell on the `AttackStatic` job, so it cannot walk up the stairs. Each frame the scenario records the hostile's floor and job, and whether the colonist loses health.
2. A colonist on the lower floor starts `AttackMelee` on a hostile on the surface, diagonally across the stairs cell, for 1200 ticks.
3. A control. A colonist and a hostile on the lower floor, next to each other, start the same `AttackMelee` job for 600 ticks.

Before the shot, the scenario removes every thing that is not a pawn from a band one cell either side of the line from the stairs to the colonist, five cells long. It records what it removed. The removal changes no game rule.

### Result

Run 20261106-163149, with the final scenario.

| Row | Result | Detail |
|---|---|---|
| a shot from the lower floor through the stairs hits a surface colonist, as it should | FAIL | No hit in 1800 ticks. The hostile stayed on the lower floor and kept its job in every frame. The line of fire from the lower floor was refused, and the ground under the hostile on the surface is Sand. |
| melee across the stairs cell is refused, as it should | PASS | The attack job was never taken, and the target took no damage. |
| melee on one floor is taken, as a control | PASS | The attack job was taken at once. The hostile took no damage, and the colonist switched to `FleeAndCower` 17 ticks in. |
| cross floor fight | FAIL | A row above failed. The runner exits with code 1. |

### Why the shot is refused

The mod moves the shooter onto the target's floor to check the line of fire. The prefix and postfix of `Verb.TryFindShootLineFromTo` in `ZLevels/Source/ZLevels/ZLevels/HarmonyPatches/CombatPatches.cs` (lines 362 to 421) do this. The postfix then refuses the line unless every cell on it that is at least two cells from the target is `ZL_OutsideTerrain`, which is open air. `IsVoidsEverywhereInShootingLineInBackWard` (lines 505 to 552) makes that check. Ordinary ground fails it. The stairs cell is `ZL_OutsideTerrainTwo`, which the check does not accept. The stairs set that terrain in `Building_StairsDown.cs` (lines 47 to 68).

During the run, the scenario asked the shooter's verb for the line from the lower floor. The answer was refused. The ground under the shooter on the surface was Sand.

The spec does not say whether a shot may cross from one floor to another over solid ground. The code sets the rule explicitly. The shot row stayed red until decision D6 was applied on 2026-11-07. The rule was kept, so the row now expects the refusal.

### Why the melee across the stairs is refused

The attack job is not current straight after `StartJob`, so the colonist never takes it, and the target takes no damage. On one floor the same job is current at once. The check that refuses it, whether reservation or reach across floors, is not isolated.

### Earlier runs

Two earlier runs passed the shot row. Neither one proves a shot from the lower floor.

- 20261106-160723: the hostile's floor was not recorded.
- 20261106-162135: the hostile walked up the stairs and shot from the surface. The enemy AI routes a hostile to a target on another floor. The postfixes of `JobGiver_AIGotoNearestHostile` and `JobGiver_AIFightEnemy` in `CombatPatches.cs` do that.

Other early runs failed in the scenario's own setup. They are not listed here.

### Limits

- The hostile holds its cell only while it runs `AttackStatic`. Without that job, the enemy AI walks it up the stairs.
- The control checks that the attack job starts on one floor. It does not check that the job lands a hit. In all six runs that sampled the control, the colonist switched to `FleeAndCower` between 9 and 27 ticks in.
- The stairs sit at column 2 in every run, next to the low-x edge, because the setup takes the first open block on the surface. The generated lower level ranged from 262 to 4126 walkable cells between runs. The scenario does not set the layout, so the cells it picks differ from run to run.
- The scenario does not test a shot through open air. So it says nothing about a shot the rule allows.
- The cause of the melee refusal is not isolated. It may be reservation or reach across floors.

### Run folders

- `.artifacts/runs/20261106-163149`: the final scenario.
- `.artifacts/runs/20261106-163519`: the core validation after the runner change, PASS.

### Decision D6 (2026-11-07)

Decision D6 asks whether a shot from a lower floor may reach a surface target over solid ground. Option (a) keeps the rule, and it is the recommended option. Option (b) lets the shot through. That changes the rule in `CombatPatches.cs` (lines 362 to 421) and this row. Lucas did not answer D6 by name. On 2026-11-07 he wrote "D1 a", which I read as D1, so option (a) was applied as the default. If "D1 a" was meant for D6, the answer is the same.

The shot row now expects the refusal. Its label is "a shot from the lower floor across solid ground is refused, as the rule says". It passes when the colonist is not hit, the shooter stayed on the lower floor on its attack job for the whole window, and the line of fire from the lower floor is refused. The melee rows are unchanged. No game code changed for this decision.

| Run | Build | Shot row | Scenario |
|---|---|---|---|
| `20261107-000331` | old row, a hit expected | FAIL: the colonist was not hit | FAIL |
| `20261107-173118` | new row, rule kept | PASS: not hit, the line of fire refused, the shooter held its job | PASS |
| `20261107-173346` | new row, rule switched off (temporary mutant) | FAIL: the colonist lost health, 3 frames with a hit, the first 52 ticks in; the line of fire was clear | FAIL |

The mutant switched off the back-ward branch of the postfix of `Verb.TryFindShootLineFromTo` in `CombatPatches.cs`. The file was restored from a backup kept at `%TEMP%\claude-scratch\d6-mutant\CombatPatches.cs.orig`. Its SHA-256 after the restore is `52df9830bd0ce74b2cea780107a8f6ed0590637efe0f76498a77e53c2923635f`, the same as the backup, and `git status` lists no change to it. The mutant run shows that the rule, and not the vanilla line of fire, refuses the shot.

Limits:

- The row checks a refusal. It does not test a shot through open air, so a shot from a lower floor that the rule allows is untested.
- The detail names the surface ground under the shooter. It does not name the cell that blocks the line.
- Reversal: option (b) changes the rule and this row. The row would then expect a hit.

## Floors tab

Lucas's idea "floor settings tab" (`docs/mod-comparison/ideas.html`). The Floors button (`MainButtonDef` `ZL_Floors`, order 210) opens `ZLevels.FloorsTab`: one row per free colonist on the colony's floors. The columns are the colonist, the floor it stands on, and the floor that each of four needs searches on: sleep, food, fun and meditation. A click opens a menu with "Any" and each floor. `FloorPolicy` keeps the choices per colonist and need, and `ZLevelsManager` saves them with the game.

### What the scenario checks

`-FloorsTab` runs one colony with a lower floor. Each row reads the job that a search returns, plus the route destination the tracker stores for that colonist. The scenario does not tick the game to see where the colonist ends up.

- `tab is registered`: the button opens `ZLevels.FloorsTab`.
- `current floor column shows the floor`: a lower colonist shows Lower 1, a surface colonist shows Surface.
- `sleep floor set to lower with no bed there`: with no bed on either floor, the rest is a ground spot on the lower floor, and the route goes there.
- `sleep floor set to lower: the rest job uses a bed on the lower floor`: a surface bed stands beside the colonist, so the filter must choose the lower bed.
- `food floor set to lower`: a meal lies on each floor, and the food job must take the lower one.
- `meditation floor set to lower`: a `MeditationSpot` stands on the lower floor only, and the meditation job must be at it.
- `fun floor set to lower`: sixteen colonists search for a fun job. No search may take a job off the lower floor, and at least one job must be found there.
- `floor setting survives save and reload`: the sleep floor is still set after a save and load.
- `floors tab`: passes when every row above passes.

### Result

Final green run `20261106-175108`, passed with other mods (the user's mod list, read-only):

| Row | Result |
|---|---|
| tab is registered | PASS |
| current floor column shows the floor | PASS |
| sleep floor set to lower with no bed there | PASS: the colonist lies on the ground of the lower floor, routed there |
| sleep floor set to lower: the rest job uses a bed on the lower floor | PASS: LayDown for a bed on Map-1 |
| food floor set to lower | PASS: Ingest for a meal on Map-1 |
| meditation floor set to lower | PASS: Meditate at a MeditationSpot on Map-1 |
| fun floor set to lower: no fun search takes a job off the lower floor | PASS: 11 of 16 found a job on the lower floor, 5 found none |
| floor setting survives save and reload | PASS |
| floors tab | PASS |

### Red runs

- `20261106-174127`, need filter off, user's mod list: the ground, sleep, food, meditation and fun rows FAIL. The sleep row got the surface bed, the food row the surface meal, the meditation row a surface job, and most fun searches a surface job. The save row passes, since it does not depend on the filter.
- `20261106-174345`, filter on, before the fixes: the sleep and food rows PASS, so the filter works for those needs. The ground row FAILs: the spot was picked on the lower floor, but the route went to the surface, because the spot was picked again after the search loop. The meditation row FAILs, since no meditation filter existed yet. The fun row FAILs: 13 of 16 searches found no job. The lower level has never been seen, so it is fogged. The fix clears the fog, as the need cool-down scenario does. After that, 5 of 16 found none.

### Fixes made while the rows were red

- The ground spot on a set sleep floor is routed to that floor, while the colonist still stands there. Before, the search recomputed the spot on the surface (`JobPatches.cs`, rest prefix).
- `JobGiver_Meditate.TryGiveJob` has a prefix that searches only the chosen floor, as the rest and food searches do (`JobGiver_MeditatePatch`).
- The food row uses a base `JobGiver_GetFood` with the Hungry minimum, as the food scenario sets it. The think tree's first food giver can be a Starving one.
- The aggregate flag was an instance field, and the game builds a new component on load. The flag is static now, so the rows before the save are read after it.

### Earlier runs, superseded

- `20261106-170559`: timed out. The after-reload function did not shut the game down. Fixed.
- `20261106-172310`: the sleep row read only the route job. The bed job sat in the queue behind it. The sleeper also stood beside the only lower bed, so the filter could not change the result.
- `20261106-173608`: the food search found nothing, because of the Starving giver. The meditation spot origin was off the map.
- `20261106-173830`: setup failed, because the centre of the surface had no reachable cell. The surface cells now sit around the stairs.
- `20261106-173951`: the meditation spot def was null. Royalty is not in the default validation mod list (`ModsConfig.xml` of the run lists no `ludeon.rimworld.royalty` in `activeMods`).
- `20261106-174700`: the fun row required a job from all sixteen searches. Vanilla's joy pick found none for eight. The row now requires that no search takes a job off the floor.
- `20261106-174914`: every row passed, but the aggregate failed on the flag described above.

### Regression, default mod list, final code

- `20261106-175318` core (33 rows), `20261106-175445` `-OwnFloorFirst`, `20261106-175523` `-JobTargets`, `20261106-175555` `-StayOnFloor`, `20261106-175638` `-JobTakenOnArrival`, `20261106-175720` `-BackgroundHaul`. All PASS.

The `-FloorsTab` run is the only run with Royalty loaded. It uses the user's mod list read-only, through `-ModsConfigSource`, because the default list has no Royalty. The default runs do not load Royalty.

### Other mods

The green run logged four errors from other mods (`other-mod-errors.txt` in its run folder): a duplicate package id (`Aoba.DeadManSwitch.Core`), a Rimatomics patch operation, and two null references from Milira's `CheckMiliraPermanentEnemyStatus`. None comes from Z-Levels, and the run's log has no Z-Levels error.

### Limits

- The meditation filter sits on `JobGiver_Meditate.TryGiveJob`. `JobGiver_MeditateInBed` overrides that method, so in-bed meditation is not filtered. That giver does not search: it meditates in the bed the colonist is already lying in.
- The fun search makes one weighted pick of a joy giver, and the pick can find nothing. The floor setting stops a job off the floor. It does not make every search find a job.
- The tab's clicks and drawing are not driven, and no screenshot was taken. Registration and the column text are read through the tab's own functions.
- The rows read the job chain. They do not follow the colonist after ticks.
- Multiplayer: untested. The project notes leave it optional and unverified.
- The runs were built from the working tree, which also holds another session's vault-rock file. The commit holds only Floors tab work, and it was built alone in a clean export.

## Work filter per floor

Lucas's idea "work filter per floor" in `docs/mod-comparison/ideas.html` is idea 6. The Floors tab has a second table. Its rows are the visible work types, and its columns are the floors, headed `<floor> does it`. A cleared box stops that floor's work searches from giving a job of that work type.

The check runs inside the search. The vanilla search sets `OwnFloorFirstSearch.GiverFilter` in `RunVanillaSearch`, and `PawnCanUseWorkGiverPatch` refuses each work giver the filter rejects. Each search pass names its own floor, so each pass reads the boxes of the floor it searches. A colonist on the surface therefore gets no cooking job at a stove on a lower floor whose cooking box is cleared.

`FloorPolicy` keeps the cleared boxes per floor in `workOffFloors`. A save writes them as two lists, `workOffFloorIndexes` and `workOffWorkTypes`. Each work type is saved by its definition name, because a save cannot hold a def.

### What the scenario checks

`-FloorWorkFilter` runs one colony with a lower floor. A fuelled stove with a cook-a-meal bill stands on the lower floor, and raw rice lies beside it. A colonist on the surface has cooking as its only work.

- `cooking off on the lower floor: no cook job there`. With cooking cleared for the lower floor, the search takes no cooking work at the stove.
- `cooking on: the cook job is taken`. With cooking set for the lower floor, the search takes the cooking work at the stove.
- `filter survives save and reload`. The cleared box is still cleared after a save and load.
- `floor work filter`. Passes when every row above passes.

Cooking work at the stove means a bill on the stove, a haul into it, or a haul to a cell beside it. A cook job starts by bringing its ingredients to the stove, so that haul is part of the cook job.

### Result

Green run `20261106-182202`, on the default validation mod list:

| Row | Result |
|---|---|
| cooking off on the lower floor: no cook job there | PASS. The search found no job at all. Cooking is the colonist's only work, and the filter refuses it. |
| cooking on: the cook job is taken | PASS. The search takes DoBill on FueledStove74707 on Map-1. |
| filter survives save and reload | PASS |
| floor work filter | PASS |

### Red run

Run `20261106-182024` with the filter forced off. For this run, a temporary `return true` came first in `FloorPolicy.WorkTypeAllowed`, so every work type was allowed. The file was then restored from a copy made before the switch. Its SHA-256 matched that copy, and its diff against `HEAD` held only the Task 30 lines.

- Row 1 failed. The search takes cooking work at the stove on the lower floor, `DoBill on FueledStove102380 on Map-1`.
- Row 2 passed, with the same job.
- Row 3 failed. Under the switch, every work type reads as allowed after the load, so the reload check fails as well.
- `floor work filter` failed.

An earlier red run, `20261106-180922`, failed row 1 in the same way. It ran before the rice's origin was corrected.

### Regression, final code

- `20261106-182245`, `-FloorsTab`, with the user's mod list read-only through `-ModsConfigSource`: 9 of 9 rows PASS. Other mods logged four error lines, as they did in Task 29.
- `20261106-182422`, core: 33 rows, PASS.
- `20261106-182525`, `-OwnFloorFirst`: 8 rows, PASS.

### Earlier runs, superseded

- `20261106-181024` (green), `20261106-181159` (core) and `20261106-181348` (`-OwnFloorFirst`) passed on the same production code. The scenario origins changed after them.
- `20261106-181425`, `-FloorsTab`, failed in its setup. The ground colonist's origin was `(12, 0, -4)`, off the map. The stairs stand at `(2, 0, 2)`, so the cell six cells south of the middle had a negative z coordinate. `OnMapCell` now keeps each origin on the map. It moves a cell no farther from the stairs than the offset it came from, so the open area around the lower stairs still covers it.

### Fixes made while the rows were red

- Row 1 first passed by mistake on a haul into the stove, the first step of a cook job. The check now counts that haul as cooking at the stove.
- The search kept the destination and main job of the previous search. `FloorWorkSearch` clears both before each search.

### Limits

- The grid's checkboxes are not clicked or drawn, and no screenshot was taken. The rows read the search's first step, main job, and destination. They do not follow the colonist after ticks.
- The lower level is generated. Its walkable cell count was 316 in the green run and 1791 in the red run.
- A work giver with no work type is allowed on every floor.
- A saved work type that no longer exists is dropped at load, without an error.
- No run loads a save made before this change. Such a save has no work lists, so every floor does all work. This follows from the code, not from a run.
- Multiplayer: untested. The project notes leave it optional and unverified.

## Haul to a floor

Lucas's idea "haul to a floor" in `docs/mod-comparison/ideas.html`. The haul-to-floor designator, `Designator_HaulToFloor`, sits in a new Architect category named `floors`. The player clicks or drags over loose items, then picks a floor from a menu. Each item that the chosen floor needs is marked for it with `ItemFloorMarks.Mark`. A colonist on that floor then hauls the item into storage there, through the offer that the background scan already makes (Task 22).

The designator asks the same question as the background scan: does the floor need this item? `ItemFloorMarks.FloorNeeds` and the scan both read `DestinationOf`, so they agree. A floor that needs none of the selected items takes no mark. The menu reports how many items each choice took.

### What the scenario checks

`-HaulToFloor` runs one colony with a lower floor. The lower stockpile takes steel only. Two items lie on the surface. The first is a steel stack that the scenario marks through `Designator_HaulToFloor.MarkItemsForFloor`. The second is a wood log that nothing marks. A colonist on the lower floor hauls.

- `a click on a cell with no loose item does nothing and does not throw`. A single click on an empty cell opens no menu and throws nothing. The game sends a click to `DesignateSingleCell`, not to the drag method.
- `marked item ends on the chosen floor`. The marked steel is in storage on the lower floor within 6000 ticks.
- `an unmarked item stays where it was`. The wood log is still on the surface, at its start cell.
- `haul to a floor`. Passes when every row above passes.

The scenario marks through `MarkItemsForFloor`, not through a drag. The validation harness has no mouse, so the drag and the menu are not driven.

### Result

Final green run `20261106-190430`, on the default validation mod list:

| Row | Result |
|---|---|
| a click on a cell with no loose item does nothing and does not throw | PASS |
| marked item ends on the chosen floor | PASS. The marked steel is in storage on the lower floor. |
| an unmarked item stays where it was | PASS |
| haul to a floor | PASS |

### Red runs

- `20261106-183559`, before the designator existed: the first row failed with `the mod has no ZLevels.Designator_HaulToFloor class`. The build had no errors.
- `20261106-190320`, with the marking in place and no click override: the click row failed with `The method or operation is not implemented.` The other rows passed.

### Regression, final code

- `20261106-190520`, `-BackgroundHaul`: 6 rows, PASS. `ItemFloorMarks` changed, and this run covers the refactored scan.
- `20261106-190557`, core: 33 rows, PASS.

### Fixes made while the rows were red

- The click path was missing. The base `Designator.DesignateSingleCell` throws, and the game calls it for a single click (`DesignatorManager.cs`, line 101). The designator now overrides it. The decompile showed the call, and the click row confirmed it.
- The first lower stockpile carve was centred three cells from the stairs, with radius four. `ClearCell` removed the lower stairs up. The surface stairs down then lost its partner, so `ZLevelsManager.ReCheckStairs` dropped it. The colonist could not route down, and the game paused in `Toils_ZLevels.GetSetStairs` with "cant find stairs down". The carve is now centred on the stairs, and the stockpile sits five cells away. A guard fails the setup if the lower stairs go missing.
- The first version of row 2 used a second steel stack. The background scan marks every item the lower floor needs, so that steel was hauled down as well. The row now uses a wood log, which the lower stockpile does not take.

### Limits

- The drag, the menu, and the category button are not driven, and no screenshot was taken. The menu was not opened in any run.
- Items in storage are not offered. The player takes a stored item out of storage first.
- A floor that needs none of the selected items takes no mark. The next scan would drop the mark in any case.
- The validation watchdog ends a run after four minutes of real time. A run that stalls shows `Timed out at stage`, which is the watchdog and not a scenario row.
- Multiplayer: untested.

## Workbench floor link

Lucas's idea "workbench floor link" in `docs/mod-comparison/ideas.html`. A player's work table shows a "Work on floor" button. The button is added in `WorkTableFloorGizmoPatch`, a postfix on `ThingWithComps.GetGizmos`. The button opens a menu of floors, and the chosen floor is stored in `FloorPolicy` (`SetBenchFloor` and `BenchFloor`). The store is saved with the game.

The bill ingredient search, `StartOrResumeBillJob`, tries the floors in its usual order and stops at the first floor with ingredients. A linked table tries its floor first. `LinkedFloorFirst` moves that floor to the front of the list, and the other floors keep their order. A table with no link keeps the whole order.

The gizmo patch targets `ThingWithComps.GetGizmos`, because the game declares that method there and not on `Building_WorkTable`. A patch on a derived type that does not declare the method would stop the mod loading, as `StairsGizmoPatch.cs` records.

### What the scenario checks

`-BenchLink` runs one colony with a lower floor. A fuelled stove on the surface has one cook-a-meal bill. Raw rice lies beside the stove on the surface, and more rice lies on the lower floor. Each search uses its own colonist, with cooking as the only work.

- `an unlinked bench is unchanged`. With no link, the search fetches the surface rice, as before.
- `bench linked to the lower floor fetches lower ingredients first`. With the stove linked to the lower floor, the search fetches the lower rice.
- `the link survives save and reload`. The stove, found by its id after a save and load, is still linked to the lower floor.
- `bench link`. Passes when every row above passes.

### Result

Final green run `20261106-191432`, on the default validation mod list:

| Row | Result |
|---|---|
| an unlinked bench is unchanged | PASS. The surface rice is fetched. |
| bench linked to the lower floor fetches lower ingredients first | PASS. The lower rice is fetched. |
| the link survives save and reload | PASS |
| bench link | PASS |

### Red run

Run `20261106-191145`, before the link existed. The unlinked row passed. The linked row failed with `FloorPolicy has no SetBenchFloor`, and the aggregate failed. The scenario reaches `SetBenchFloor` by name, so a missing method fails a row and not the build.

### Regression, final code

- `20261106-191530`, core: 33 rows, PASS.
- `20261106-191648`, `-FloorWorkFilter`: 4 rows, PASS. Its stove has no link, so its search order is unchanged.
- `20261106-191735`, `-OwnFloorFirst`: 8 rows, PASS.

### Limits

- The gizmo's button and its floor menu are not driven, and no screenshot was taken. The link is set through `SetBenchFloor`, the call that the menu makes.
- The save row covers one stove and one link. A link on a table that has been destroyed is left out of the save.
- The button uses the game's install icon, `TexCommand.Install`, because the mod ships no command icons.
- A linked floor with no ingredients falls back to the other floors, in their usual order.
- Multiplayer: untested.

## Call colonists here

Lucas's idea "call colonists here" in `docs/mod-comparison/ideas.html`. A player colonist's gizmo list has a "Call colonists here" button. The button starts a targeter on the floor the player is viewing. A standable cell that is picked sends each selected colonist to it. A colonist on another floor takes the stairs first. A colonist already on the cell gets no job.

The button is added by `CallColonistsGizmoPatch`, a postfix on `Pawn.GetGizmos`. The order is `CallColonistsTargeter.CallTo`. It resets each colonist's job tracker. A colonist on the target floor gets a `Goto` job. A colonist on another floor gets the route that a float-menu order builds: `BuildJobListFor` with the target floor, then `EndCurrentJob`, which starts the go-to-map job and queues the walk after it.

### What the scenario checks

`-CallColonists` runs one colony with a lower floor, and makes two calls. The walker is a colonist on the lower floor, locked to that floor, and it is called to a free cell on the surface. The stayer is a drafted colonist standing on the surface, and it is called to the cell it stands on.

- `call colonists gizmo is offered to a selected colonist`. The stayer's gizmos hold the button. The row is recorded before the calls, so it fails on its own when the button is missing.
- `colonist on the lower floor reaches the spot within 6000 ticks`. The walker stands on its cell before 6000 ticks pass.
- `colonists already at the spot stay there`. The call sends the stayer nowhere, and no route job appears for it during the walker's walk.
- `a locked colonist keeps its lock on the floor it is called to`. After the call, the walker's lock names the surface.
- `call colonists`. Passes when every row above passes.

### Result

Green run `20261106-193148`, on the default validation mod list:

| Row | Result |
|---|---|
| call colonists gizmo is offered to a selected colonist | PASS |
| colonist on the lower floor reaches the spot within 6000 ticks | PASS, after 2106 ticks |
| colonists already at the spot stay there | PASS |
| a locked colonist keeps its lock on the floor it is called to | PASS, floor index 0 |
| call colonists | PASS |

### Decision: a call moves the lock

A locked colonist keeps its lock through a call. `FloorPolicy.MoveLock` moves the lock to the floor of the cell, and the lock keeps its expiry. Without the move, the lock stays on the old floor, and the colonist's job searches are limited to that floor. To reverse the decision, remove the `MoveLock` call from `CallTo`. The last row checks for this move.

### Red runs

- Run `20261106-192923`, before the order existed. The first row failed with `the mod has no ZLevels.CallColonistsTargeter class`, and the aggregate failed. That version had no gizmo row, so the other rows failed only because setup stopped at the missing order.
- Run `20261106-193056`, with the gizmo row added. The gizmo row failed on its own, with `the colonist's gizmos hold no Call colonists here button`. The walker row failed with the missing order, and the aggregate failed.

The order is reached by name, so a missing class fails rows and not the build.

### Regression, final code

- `20261106-193251`, core: 33 rows, PASS.
- `20261106-193447`, `-WorkOnFloorOrder`: PASS.
- `20261106-193907`, `-BenchLink`: PASS.
- `20261106-193956`, `-FloorWorkFilter`: PASS.
- `20261106-194232`, `-FloorsTab` with the user's mod list, read only: PASS. Other mods logged four error lines, and Z-Levels logged none.
- `-StayOnFloor`: run `20261106-193404` failed on `unlocked colonist takes the same haul`, before the harness fix in the next section. Runs `20261106-193622`, `20261106-193706` and `20261106-193754` pass after it.

### Harness fix found by the regression

`-StayOnFloor` spawned its two colonists at random and set Hauling priority on each, without checking that the colonist can haul. A colonist with Hauling disabled got no haul. The game logged `Tried to change priority on disabled worktype Hauling for pawn Soto`, and the unlocked row failed. The locked row could also pass for the wrong reason, since a colonist that cannot haul skips a haul too. `SpawnHaulerAt` spawns again until the colonist can haul. Task 16 recorded the same failure and left it open.

### Limits

- The button, its click and the targeter's mouse path are not driven. No screenshot was taken.
- The scenario makes one call with one walker from the lower floor. Several selected colonists in one call, a colonist on the upper floor, and a colonist two floors down are not tested.
- The gizmo group key, which should merge the button across a selection, is not tested.
- A picked cell that another pawn stands on is not tested.
- The lock's expiry is kept through a call, but no row checks a timed lock.
- The button uses `TexCommand.GatherSpotActive`, because the mod ships no command icons.
- Multiplayer: untested.

## Heat and gas through stairs

Lucas's idea "heat and gas through stairs" in `docs/mod-comparison/ideas.html`. The rooms that a pair of stairs joins on two floors now trade heat, and the gas on the stairs cell passes between the floors. The equalizer is `StairEqualizer` in `StairEqualizer.cs`. The game calls `StairEqualizer.StepAll` every 60 ticks, and each pair is stepped once, from its end on the upper floor.

### How it works

- Heat. The gap between the two rooms closes by 10 degrees a day at medium speed, scaled by the speed setting. Each room takes half of the closing, so the gap shrinks by exactly that amount and never overshoots. A room that takes its temperature from the weather is left alone.
- Gas. On each gas type, the stairs cells on the two floors move a share of their difference. At medium speed the share is a tenth per 60 tick step. Only the gas that the receiving cell takes leaves the sending cell, so the total stays the same.
- Speed. The setting has three values: slow, medium (the default) and fast. The multipliers are 0.5, 1 and 4. A 10 degree gap closes in two days at slow, one day at medium and a quarter of a day at fast, as DM12 of the multifloors plan sets out. The slider is in the mod's settings window, and its value is saved with the game.
- Removed stairs. A pair whose other end is gone moves nothing.

### What the scenario checks

`-StairsEqualize` runs one colony with a lower floor. Each floor has a walled room with a roof around the stairs. The surface room is 30 degrees, the lower room is 20, and smoke lies on the surface stairs cell. Every row is measured within one frame, so no game tick runs between two readings.

- `temperature gap between joined rooms shrinks`. One call over 6000 ticks at medium closes the gap from 10.00 to 9.00.
- `gas moves through the stairs`. One step of 60 ticks moves 20 of the 200 smoke to the lower stairs cell.
- `speed setting changes the rate`. 6000 ticks close 0.50 degrees at slow and 4.00 at fast.
- `nothing moves when the stairs are removed`. With the lower stairs destroyed, both temperatures and both smoke readings stay the same.
- `stairs equalize`. Passes when every row above passes.

### Result

Green run `20261106-195102`, on the default validation mod list:

| Row | Result |
|---|---|
| temperature gap between joined rooms shrinks | PASS, from 10.00 to 9.00 |
| gas moves through the stairs | PASS, 20 smoke on the lower stairs cell |
| speed setting changes the rate | PASS, 0.50 at slow and 4.00 at fast |
| nothing moves when the stairs are removed | PASS |
| stairs equalize | PASS |

### Red run

Run `20261106-194904`, before the equalizer existed. The first row failed with `the mod has no ZLevels.StairEqualizer class`. The gas and removed-stairs rows failed the same way, and the speed row failed with `the settings have no StairsEqualizeSpeed`. The aggregate failed. Setup passed, so the two joined rooms were built and checked as indoor before any row ran.

### Regression, final code

- `20261106-195159`, core: 33 rows, PASS.
- `20261106-195313`, `-OneFloorColony`: 3 rows, PASS.
- `20261106-195346`, `-StairsDamage`: 4 rows, PASS.
- `20261106-195424`, `-Cooling`: 47 rows, PASS. The cooling runs depend on room temperature, so they show the equalizer does not disturb them.
- `20261106-195557`, `-CoolingTwoNets`: 8 rows, PASS.

### Decisions

- Linear closing. The plan's DM12 gives days for a 10 degree gap to close, and a steady rate meets those figures exactly. Reverse in `StepHeat`. Cost if wrong: the gap closes on a different curve, and the three day figures no longer hold.
- Half each. Each room takes half of the closing. That is exact when the rooms are the same size, and approximate when they are not. Reverse in `StepHeat`.
- Gas share. The tenth of the difference per step, and the same speed multipliers as heat, are my choice. Reverse with `GasSharePerStepAtMedium`.
- Smoke in the test. `GasGrid.AddGas` refuses tox gas unless the Biotech expansion is active, and deadlife dust unless Anomaly is active (`ModLister.CheckBiotech` and `ModLister.CheckAnomaly`). The validation mod list loads neither, so the scenario uses smoke, which needs no expansion.

### Limits

- The settings slider is not driven. Its label is plain English, as the new labels of the port are. No translation key is added.
- Only smoke is tested. Tox gas and deadlife dust take the same code path, but no run loads their expansions.
- One pair of stairs in one colony is tested. Several pairs, and a pair on a third floor, are not.
- The rooms are closed rooms that the scenario builds. Rooms open to the outdoors are not tested.
- The 60 tick call in `ZLevelsManager.GameComponentTick` is not checked by a row. The scenario calls `Step` directly. The regression runs show that the call does not break a run, but not that the game makes it.
- Multiplayer: untested.

## In-game guide

Lucas's idea "in-game guide" in `docs/mod-comparison/ideas.html`. A guide window explains the floors in plain words. The Guide button at the top of the Floors tab opens it. The window is `FloorsGuideWindow`, and its text is `FloorsGuideWindow.GuideText`.

### What the guide covers

The text names the floors, the stairs, the two floor keys (N for one floor up and M for one floor down, the defaults), Stay on this floor, Work on this floor, Call colonists here, and the Floors tab.

### What the scenario checks

`-FloorsGuide` opens the guide the way the Floors tab's button does, then reads the guide text. The button itself is not clicked.

- `guide opens`. The guide window is on the window stack after `Open`.
- `guide text covers stairs, floors, stay on floor, work on floor, and the keys`. The text names the stairs, the floors, Stay on this floor, Work on this floor, N and M.
- `floors guide`. Passes when both rows above pass.

### Result

Green run `20261106-200016`, on the default validation mod list:

| Row | Result |
|---|---|
| guide opens | PASS |
| guide text covers stairs, floors, stay on floor, work on floor, and the keys | PASS |
| floors guide | PASS |

### Red run

Run `20261106-195902`, before the window existed. The first row failed with `the mod has no ZLevels.FloorsGuideWindow class`. The text row failed the same way, and the aggregate failed.

### Regression, final code

- `20261106-200050`, core: 33 rows, PASS.
- `20261106-200158`, `-FloorsTab` with the user's mod list, read only: PASS with other mods, 10 rows. The Floors tab moved its table down by 36 pixels for the Guide button, and this run passes with the table in its new place.

### Limits

- The Guide button, its click, and the window's layout on screen are not driven. No screenshot was taken.
- The text check looks for keywords. It does not check each sentence against the current menus.
- The keys named in the guide are the defaults. A player who rebinds them sees the defaults in the text.
- The guide text is English only, as the other new labels of the port are.
- Multiplayer: untested.

## More ways up (2026-11-06)

Lucas's decision D3, answered 2026-11-06: ladders and stair sizes. The idea "more ways up" is in `docs/mod-comparison/ideas.html`. The plan is `docs/superpowers/plans/2026-11-06-multifloors-phase-e.md`, Task 36.

### What was built

Small ladders take one cell. Large ladders and large stairs take a 2 by 2 block. Each kind has an up piece on the lower floor and a down piece on the floor above, on the same cells. The defs are in `ZLevels/Common/Defs/Buildings/WayUps.xml`, and the kind table is `ZLevels/Source/ZLevels/ZLevels/Buildings/WayUpKinds.cs`. Costs and strength are decision DM15 in the plan. The existing stairs keep their behaviour.

The large kinds are not rotatable, so both floors get the same footprint. A large way may be placed only when its four cells lie in one room of at least 25 cells, and its four cells on the other floor are standable and empty. Small ways keep their old checks.

### What the scenario checks

`-WayUps` (file `tools/RuntimeValidation/WayUpsScenarios.cs`). Both floors share one block of cells, because a partner is found by its cell.

- `small ladder places a pair of ways`. A small ladder up on the lower floor creates its down piece on the floor above, at the same cell.
- `large ladder covers four cells on both floors`. A large ladder up creates a partner on all four cells above.
- `large stairs place a pair`. The same check for large stairs.
- `colonist climbs a large ladder`. The small ladder and the large stairs are removed first, so the large ladder is the only way between the floors. A colonist is called to the surface cell and must stand there within 6000 ticks.
- `large way allowed with four open cells on the other floor`. The placement check allows a large way whose four cells above are open.
- `blocked cell on the other floor refuses a large way`. A wall on one of the four cells above makes the check refuse the way, with the message "A large way needs four open cells on the other floor."
- `way ups`. Passes when the six rows above pass.

### Red run

Run `20261106-212937`, before any def existed. Every row failed with `the mod has no <def> def`, and the aggregate failed. The build had no errors. Its two warnings (CS0168 in `RoofAndFloorPatches.cs` and `ZPathfinder.cs`) were already there.

### Green run

Run `20261106-213318`, on the default validation mod list. The log ends with `Runtime validation passed. Evidence: ...20261106-213318`.

| Row | Result |
|---|---|
| small ladder places a pair of ways | PASS, 1 by 1 cells hold the pair |
| large ladder covers four cells on both floors | PASS, 2 by 2 cells hold the pair |
| large stairs place a pair | PASS, 2 by 2 cells hold the pair |
| large way allowed with four open cells on the other floor | PASS |
| blocked cell on the other floor refuses a large way | PASS, refused with the other-floor message |
| colonist climbs a large ladder | PASS, on the surface after 253 ticks |
| way ups | PASS |

### Regression

Final batch, after the teleport terrain fix below, run one at a time on the default validation mod list. The logs are in the ledger folder, named `task36-v2-<name>.log`.

| Run | Command | Result |
|---|---|---|
| 20261106-221515 | `-WayUps` | PASS, six rows and the aggregate |
| 20261106-221600 | core | PASS |
| 20261106-221713 | `-StayOnFloor` | PASS, with the two INFO rows added to it |
| 20261106-221801 | `-StairsDamage` | PASS |
| 20261106-221844 | `-OneFloorColony` | PASS |
| 20261106-221917 | `-Cooling` | PASS |
| 20261106-222046 | `-CoolingTwoNets` | PASS |
| 20261106-222129 | `-StairsEqualize` | PASS |
| 20261106-222207 | `-CallColonists` | PASS |
| 20261106-222250 | `-HaulToFloor` | PASS |
| 20261106-222333 | `-BackgroundHaul` | PASS |
| 20261106-222411 | `-WorkOnFloorOrder` | PASS |
| 20261106-222449 | `-BenchLink` | PASS |

First batch, before the teleport terrain fix. The logs are named `task36-reg-<name>.log`. Every run passed except `-StayOnFloor`, which is covered in the next part.

| Run | Command | Result |
|---|---|---|
| 20261106-213436 | core | PASS |
| 20261106-213559 | `-StayOnFloor` | FAIL on `unlocked colonist takes the same haul` |
| 20261106-213648 | `-StairsDamage` | PASS |
| 20261106-213731 | `-OneFloorColony` | PASS |
| 20261106-213804 | `-Cooling` | PASS |
| 20261106-213937 | `-CoolingTwoNets` | PASS |
| 20261106-214015 | `-StairsEqualize` | PASS |
| 20261106-214053 | `-CallColonists` | PASS |
| 20261106-214137 | `-HaulToFloor` | PASS |
| 20261106-214220 | `-BackgroundHaul` | PASS |
| 20261106-214258 | `-WorkOnFloorOrder` | PASS |
| 20261106-214336 | `-BenchLink` | PASS |

### `-StayOnFloor` is intermittent

The row `unlocked colonist takes the same haul` ("takes no job") failed in run `20261106-213559` and in its first rerun, `20261106-214555`. The two reruns after them, `214639` and `214727`, passed. Later runs passed too: three with extra diagnostic rows, eight more, sixteen more with a storage row, and the final batch. That is 2 failures in 31 runs on the Task 36 code. The same row failed once in Task 33, in run `193404`, before any Task 36 change.

The cause is not known. The failing runs were made before the diagnostic rows existed, so they show no storage row. The rows now record the stairs, the steel, the zone, the colonists, whether the steel is stored, a store cell for it, and the claims on either stack. A failing run with these rows would show whether a claim or a missing store cell blocks the first search. The pass criteria of the row did not change.

### Teleport terrain fix

While reading the teleport paths, I found that the shared partner helper set the outside terrain tile on the cell where the other stairs end. The old `SpawnStairsBelow` and `SpawnStairsUpper` never did. The helper now takes a flag. The stairs' own spawn passes `true`, as before, and both teleport paths pass `false`. The final batch ran after this change.

### Limits

- Save and reload of the large kinds is not tested.
- Damage and destruction of a large way are not tested. The stairs damage run covers the small stairs only.
- Upper-floor pairs use the same code, but only the lower-to-surface pair was run.
- The climb row checks that a colonist arrives. It does not measure how many colonists climb at once.
- The ladders and the large stairs share the stairs' textures, so a large way looks like a bigger ladder. This was not checked on screen.
- Multiplayer: untested.

## Floor pipes between floors (2026-11-06)

Lucas's decision D4, answered 2026-11-06, is one chosen resource per pipe, at a fixed rate. The idea "pipes between floors" is in `docs/mod-comparison/ideas.html`. The plan is `docs/superpowers/plans/2026-11-06-multifloors-phase-e.md`, Task 37. The rules DM17 to DM19 are in that plan.

### What was built

A floor pipe is one object across two floors. Its lower end is placed on a floor that has a floor above, and its other end stands on the same cell up there. The def is in `ZLevels/Common/Defs/Buildings/FloorPipe.xml` (steel 10, 120 hit points, 300 work). The class is `ZLevels/Source/ZLevels/ZLevels/Buildings/Building_FloorPipe.cs`.

- The end that sends takes stored items of the chosen resource and moves 60 a game day to the other end. The rate is `itemsPerDay` in the def, read through `FloorPipeExtension.cs`. The flow runs in steps of 250 ticks, the game's rare-tick interval.
- Only items in storage move. Forbidden and reserved items are skipped. The items land next to the receiving end (`FloorPipeMover.cs`). If the receiving floor has no room for an item, the item goes back where it was, and the part of the move it would have made is not carried over.
- The resource is steel first, then the raw resources with a stack limit above 1, by label. A gizmo sets it on both ends. A second gizmo flips which end sends.
- Removing one end removes the other.
- The placement check (`PlaceWorker_FloorPipe.cs`) refuses a pipe on the top floor, and refuses a pipe where the cell above is blocked.

### What the scenario checks

`-FloorPipe` (file `tools/RuntimeValidation/FloorPipeScenarios.cs`). The scenario is synchronous. A game day is 240 calls to the pipe's `Step` method, each for 250 ticks, as the rare tick makes them.

- `floor pipe places its other end above`. A pipe on the lower floor has a pipe on the cell above.
- `steel moves up at 60 a day`. 60 steel stored on the lower floor leave the zone, and 60 land above.
- `only the chosen resource moves`. Under a steel pipe, 60 wood stays stored below.
- `flip direction sends the other way`. After a flip, 30 steel stored above land below.
- `removing one end removes the other`. Removing the lower end removes the upper one.
- `resource gizmo sets both ends`. Setting WoodLog on one end gives the other end WoodLog too.
- `pipe refused on the top floor`. The placement check returns its top-floor message.
- `pipe refused where the cell above is blocked`. A wall above makes the check return its blocked message.
- `pipe allowed where the cell above is clear`.
- `pipe on the lowest of three floors has one other end`. The top floor is made after the rows above, so their two-floor layout does not change. A pipe that also stands on the top floor fails the row.
- `floor pipe`. Passes when the ten rows pass.

The three placement rows were added after the first green run, because no row had reached the placement check. Their red is the mutant run below, not a missing-def run.

### Red and green runs

- Red, run `20261106-223004`. Every row fails. The first reason reads "the mod has no ZL_FloorPipe def". The aggregate fails. The build had no errors.
- Green, six rows, run `20261106-223558`. The log ends with `Runtime validation passed. Evidence: ...20261106-223558`.
- Green, nine rows, run `20261106-223851`. The log ends the same way.
- Mutant, run `20261106-223803`. The first statement of `AllowsPlacing` was made `return true;`. `pipe refused on the top floor` fails ("the top floor placement is allowed"). `pipe refused where the cell above is blocked` fails ("the lower floor placement is allowed"). `pipe allowed where the cell above is clear` and the six pipe rows pass, because they never reach the check. The aggregate fails. The file was then restored from a scratch copy and matches it byte for byte.

The build of `tools/RuntimeValidation/RuntimeValidation.csproj` before the first green run reported 0 errors and 12 warnings. None of the warnings is in a floor pipe file.

### Regression

Run one at a time on the default validation mod list, after the nine rows were in place. The logs are in the ledger folder, named `task37-reg-<name>.log`.

| Run | Command | Result |
|---|---|---|
| 20261106-223946 | core | PASS |
| 20261106-224109 | `-StairsDamage` | PASS |
| 20261106-224147 | `-StairsEqualize` | PASS |
| 20261106-224230 | `-OneFloorColony` | PASS |
| 20261106-224304 | `-Cooling` | PASS |
| 20261106-224432 | `-StayOnFloor` | PASS |
| 20261106-224516 | `-WayUps` | PASS |

`-StayOnFloor` passed in this batch. Its intermittent failure from Task 36 did not recur here.

### Three floors, found in review

On a colony with three floors, placing a pipe's other end made a further end on the top floor. One pipe became a chain of three pipes, not one pair. The two-floor rows could not show this, because the surface is the top floor in those rows.

The cause is in `Building_FloorPipe.SpawnSetup`. The other end is placed through the game's placement, which runs that end's own `SpawnSetup`, and that call made the top end. `Partner()` checks the floor above first, so the middle end paired with the top end. A flip or the flow could then go to the wrong floor.

The row `pipe on the lowest of three floors has one other end` was added last, so the two-floor rows keep their layout. It builds a top floor with `CreateUpperLevel`. It passes when the middle floor has a pipe at the test cell and the top floor has none.

- Red, run `20261106-230839`, built before the fix: the row fails with `FAIL: the middle floor has a pipe True and the top floor has a pipe True, at (24, 0, 58)`. The nine earlier rows pass.
- Green, run `20261106-230929`, after the fix in commit `c57e5f1`: all ten rows pass, and the aggregate `floor pipe` passes. The row reads `PASS: one other end on the middle floor at (24, 0, 26), none on the top floor`.

The fix makes the placement of the other end a case that `SpawnSetup` skips. A flag is set around the call and reset in a `finally` block.

### Limits

- Saving and loading a pipe, with its chosen resource and its direction, is not tested.
- The rows stop when the items land. The haul logic that stores them on the receiving floor is not run here.
- Forbidden and reserved items are skipped in the code, but no row covers them.
- A full receiving floor is not tested. The return-to-origin path has no row.
- Damage and deconstruction of a pipe are not tested. The removal row uses `DestroyMode.Vanish` only.
- The pipe's texture is `Things/Building/Furniture/Column`, the same texture the power column uses. Runs are headless, so no texture was drawn, and the pipe's look on screen was not checked.
- The gizmos were not clicked. The rows call the methods the gizmos call.
- The scenario calls `Step` itself, 240 times a day. It does not wait for the game's rare tick, so the real tick's timing is not proven by a row.
- Flow and flip were run only on the lower-to-surface pair. The three-floor row checks where the other end stands. No flow or flip runs on that colony.
- Multiplayer: untested.

## Stair transfer (2026-11-06)

Lucas's decision D5, answered 2026-11-06, is to send a chosen list now. The idea "stair transfer" is in `docs/mod-comparison/ideas.html`. The plan is `docs/superpowers/plans/2026-11-06-multifloors-phase-e.md`, Task 38. The rule DM20 is in that plan.

### What was built

- Every stairs piece has a "Transfer across the stairs" button. It sits beside the view button, in the gizmo list that `StairsGizmoPatch.AddViewButton` builds. `Building_Stairs` is the base class of both stairs pieces, so the upper and the lower piece both get it.
- The button opens `StairTransferWindow` (`ZLevels/Source/ZLevels/ZLevels/UI/StairTransferWindow.cs`). It lists the free colonists and the loose items on the stairs' floor, with a checkbox for each, and a "Send checked" button.
- Send walks the checked colonists to the landing cell beside the other end. It uses `CallColonistsTargeter.CallTo`, so a colonist on the other floor takes the stairs by the same route a float-menu order uses. Each checked item goes to `Designator_HaulToFloor.MarkItemsForFloor`. An item the other floor does not need is not marked, and Send counts it.
- `LandingCell` is the first standable cell next to the other end that holds no building. If none is free, it is the other end's own cell.
- `Designator_HaulToFloor.IsLooseItem` is now internal, so the window uses the same rule as the designator.
- The floors guide's Stairs paragraph names the new button.

### What the scenario checks

`-StairTransfer` (file `tools/RuntimeValidation/StairTransferScenarios.cs`). The scenario is frame-driven. On the lower floor it places one colonist to send, one to keep, a steel stack, and two wood stacks: one to send and one to keep. The surface has a stockpile that takes steel only, so the surface needs steel and does not need wood. This is the set-up of `-HaulToFloor`.

- `transfer button is on the stairs`. The gizmo list of the lower stairs has "Transfer across the stairs".
- `window lists the colonists and items on the floor`. `ColonistsOn` lists both colonists, and `ItemsOn` lists all three items.
- `send walks the checked colonist to the other floor`. Within 6000 ticks the colonist stands on the landing cell above.
- `send marks the checked item for the other floor`. The steel carries the mark for the surface, and one item is marked.
- `an item the other floor does not need is not marked`. The checked wood is counted as not needed, is not marked, and stays on the lower floor. The plan does not list this row. It tests DM20 directly.
- `unchecked colonist and item stay where they are`. After the transfer, the unchecked colonist and the unchecked wood are still on the lower floor, and the wood is unmarked.
- `a checked item that left the floor is skipped and counted`. A checked steel stack that is no longer on the floor is counted as gone, and `Send` marks nothing for it. The stand-in is a steel stack that was never spawned, so its map is null, as a carried item's is.
- `stair transfer`. Passes when the seven rows pass.

### Red and green runs

- Red, run `20261106-225314`. Every row fails. The button row reads "the stairs piece has no 'Transfer across the stairs' button", and the other rows read "the mod has no ZLevels.StairTransferWindow class". The build had no compile errors.
- An earlier red attempt did not compile. The scenario read `ZUtils`, which is internal to the mod, and the build reported four CS0122 errors. The scenario now reads the tracker from `Current.Game`, as the other scenarios do. No run went on the failed build.
- Green, run `20261106-225433`. The six rows and the aggregate pass. The counts row reads "sent 1 colonist, marked 1 item, and 1 item was not needed on the floor above", and the landing cell is (3, 0, 18).

### Regression

Run one at a time on the default validation mod list, on the code as it stood at the end of Task 38. The logs are in the ledger folder, named `task38-reg-<name>.log`. These runs predate the stale-item fix in commit `58668d1`. The final regression, at the end of this report, covers the fixed code.

| Run | Command | Result |
|---|---|---|
| 20261106-225540 | core | PASS |
| 20261106-225653 | `-CallColonists` | PASS |
| 20261106-225731 | `-HaulToFloor` | PASS |
| 20261106-225819 | `-BackgroundHaul` | PASS |
| 20261106-225858 | `-StairsDamage` | PASS |
| 20261106-225936 | `-StairsEqualize` | PASS |
| 20261106-230014 | `-FloorsGuide` | PASS |

### A checked item that left the floor, found in review

The window keeps the list of loose items that it built when it opened. A colonist who picks up a checked item holds it outside the map, so `Thing.Map` is null for that item. `Send` then threw `NullReferenceException` in `MarkItemsForFloor`, after the colonists were already sent. A second send would throw again while the item stayed checked.

`Send` now keeps only the items that `Designator_HaulToFloor.IsLooseItem` accepts. Any other checked item, whether stored, carried or used, is counted in `TransferResult.ItemsGone`. The window's message reports that count, and the item is not marked.

- Red, run `20261106-231843`, before the fix. The row `a checked item that left the floor is skipped and counted` fails with `Object reference not set to an instance of an object.` The six earlier rows pass, and the aggregate `stair transfer` fails.
- Green, run `20261106-231957`, after the fix in commit `58668d1`. All seven rows pass, and the aggregate `stair transfer` passes. The build has no `error CS` line.

Not tested: the window's own "Send checked" button and the new message text. The scenario calls the static `Send`. No real carried item was used, so the stand-in is the only test of that state.

### Limits

- The window is not opened on screen. The scenario calls the methods the window calls: `ColonistsOn`, `ItemsOn`, `Send` and `LandingCell`. The checkboxes, the layout and the Send button were not clicked.
- The button is read from the gizmo list. It was not clicked.
- The window's lists are taken when it opens, and they are not refreshed. A checked item that has left the floor by then is skipped and counted, as the section above describes. A colonist who dies or goes down by then is skipped by `CallTo`. No row covers that.
- A downed or dead colonist is left out of the list. No row covers that case.
- The landing cell's fallback, the other end's own cell, is not tested.
- `CallTo` moves the colonist's floor lock to the floor it is sent to. No row checks the lock.
- The transfer is run from the lower stairs only. The surface's button uses the same code, but was not run.
- Items that do not fit on the other floor, and a destination that is full, are not tested.
- The guide's new sentence is checked only by the keyword check in `-FloorsGuide`.
- Multiplayer: untested.

## Final regression, every scenario (2026-11-06 and 2026-11-07)

The final regression ran the core validation and each scenario switch in the runner's `$scenarioSwitches` map, one run at a time. That is 59 runs. The batch script is `.superpowers/sdd/2026-11-05-multifloors-ideas/task39-run-all.sh`. Its summary is `task39-all-summary.txt`, and each run has a log named `task39-<name>.log`. The batch started at 23:21 on 2026-11-06, on commit `58668d1`, the last code change of Task 39.

### Result

The runner printed its pass line for 54 of the 59 runs. One of the five runs that did not pass was a batch mistake, and a rerun passes it. After that rerun, 55 switches pass. The other four are explained below.

- `-FloorsTab` failed in the batch because the batch script did not pass `-ModsConfigSource`. The scenario needs Royalty, and the default mod list does not load it. The rerun reads the user's mod list, read only, and passes: run `20261107-002345`. Every row and the aggregate pass.

### Runs that do not pass

- `-VaultInfestation`, run `20261106-232823`. Its two rows and its aggregate pass. The runner then stopped on a game error in Player.log: `Tried to destroy non-destroyable thing ZL_VaultExit48371`. The stack starts at `ZLevelsValidation.RuntimeValidation.ClearCell`, which `SpawnFood` calls (`tools/RuntimeValidation/RuntimeValidation.cs`, lines 2279 and 2324). The helper clears a cell for food, and that cell held the vault exit. The fault sits in the vault session's scenario and a shared harness helper. Nothing here changed it.
- `-UnreachableZone`, run `20261106-233639`. The reachable control cell got no haul in 60 seconds, so the row failed. Five reruns passed: `20261107-001507`, `20261107-001639`, `20261107-001828`, `20261107-002001` and `20261107-002129`. Each rerun had one haul to the reachable cell. The failing run's log has one `ERROR` line, the known duplicate package id warning for `Aoba.DeadManSwitch.Core`. The working tree did not change during the batch. The cause is not found, and no fix was made. Treat this check as flaky until the cause is found.
- `-CoolingNoColumns`, run `20261106-234147`. This is a negative control, so the failure is by design. Its variant places no column pair, so its 14 link rows fail, and so does its aggregate. The positive control, `uncooled core overheats`, passes.
- `-CrossFloorFight`, run `20261107-000331`. The shot row fails, as it did in Task 28 (run `20261106-163149`). The rule refuses the line, because the cells on it that are two or more from the target are not open air (`ZL_OutsideTerrain`). The ground under the shooter on the surface is `Marble_Rough` in this run and `Sand` in the Task 28 run. Whether a shot may cross solid ground is decision D6. Its default (a) was applied on 2026-11-07, and the shot row now expects the refusal: run `20261107-173118` passes (see "Fighting across floors"). The two melee rows pass.

### Table

Result lines counts the lines of `validation-results.txt` that carry a `|` separator, so the generated-level and INFO lines are included. Failing lines counts the `FAIL` lines.

| Switch | Run folder | Batch verdict | Result lines | Failing lines |
|---|---|---|---|---|
| core | 20261106-232115 | PASS | 33 | 0 |
| `-InfestationGuard` | 20261106-232255 | PASS | 8 | 0 |
| `-StaleLookup` | 20261106-232343 | PASS | 3 | 0 |
| `-VaultPlacement` | 20261106-232427 | PASS | 6 | 0 |
| `-VaultPocket` | 20261106-232505 | PASS | 3 | 0 |
| `-VaultEnter` | 20261106-232544 | PASS | 8 | 0 |
| `-VaultLoad` | 20261106-232637 | PASS | 6 | 0 |
| `-VaultSave` | 20261106-232720 | PASS | 6 | 0 |
| `-VaultInfestation` | 20261106-232823 | not passed | 3 | 0 |
| `-VaultRock` | 20261106-232902 | PASS | 8 | 0 |
| `-Supply` | 20261106-233236 | PASS | 12 | 0 |
| `-UnreachableZone` | 20261106-233639 | not passed | 3 | 1 |
| `-MultiBuild` | 20261106-233812 | PASS | 7 | 0 |
| `-StairsDamage` | 20261106-233915 | PASS | 4 | 0 |
| `-Cooling` | 20261106-233953 | PASS | 47 | 0 |
| `-CoolingNoColumns` | 20261106-234147 | not passed | 46 | 15 |
| `-CoolingTwoNets` | 20261106-234315 | PASS | 8 | 0 |
| `-SocialRelaxQueue` | 20261106-234353 | PASS | 2 | 0 |
| `-RestSearchNull` | 20261106-234431 | PASS | 5 | 0 |
| `-TraderReturn` | 20261106-234504 | PASS | 1 | 0 |
| `-ReservationRefused` | 20261106-234536 | PASS | 6 | 0 |
| `-FloorHotkeys` | 20261106-234614 | PASS | 7 | 0 |
| `-StayOnFloor` | 20261106-234652 | PASS | 12 | 0 |
| `-WorkOnFloorOrder` | 20261106-234740 | PASS | 6 | 0 |
| `-NeedCooldown` | 20261106-234813 | PASS | 9 | 0 |
| `-IdleSearchPause` | 20261106-234856 | PASS | 5 | 0 |
| `-StaleClaims` | 20261106-234934 | PASS | 7 | 0 |
| `-FloorGuard` | 20261106-235012 | PASS | 6 | 0 |
| `-StorageIndex` | 20261106-235050 | PASS | 6 | 0 |
| `-StairRoutes` | 20261106-235128 | PASS | 5 | 0 |
| `-AddonLoad` | 20261106-235205 | PASS | 2 | 0 |
| `-ChoreSpread` | 20261106-235238 | PASS | 3 | 0 |
| `-OneFloorColony` | 20261106-235311 | PASS | 3 | 0 |
| `-StairsView` | 20261106-235344 | PASS | 3 | 0 |
| `-StairPlacement` | 20261106-235421 | PASS | 4 | 0 |
| `-SearchTiming` | 20261106-235454 | PASS | 13 | 0 |
| `-LighterFloors` | 20261106-235532 | PASS | 8 | 0 |
| `-OwnFloorFirst` | 20261106-235605 | PASS | 8 | 0 |
| `-JobTargets` | 20261106-235638 | PASS | 2 | 0 |
| `-BackgroundHaul` | 20261106-235710 | PASS | 6 | 0 |
| `-JobTakenOnArrival` | 20261106-235748 | PASS | 10 | 0 |
| `-RaidGround` | 20261106-235826 | PASS | 5 | 0 |
| `-LeaveFromLower` | 20261106-235904 | PASS | 10 | 0 |
| `-SkyfallerRoof` | 20261106-235947 | PASS | 6 | 0 |
| `-MoodOtherFloor` | 20261107-000024 | PASS | 5 | 0 |
| `-EventsOtherFloor` | 20261107-000102 | PASS | 8 | 0 |
| `-ThiefCarry` | 20261107-000215 | PASS | 12 | 0 |
| `-ChildAnimalMech` | 20261107-000253 | PASS | 8 | 0 |
| `-CrossFloorFight` | 20261107-000331 | not passed | 8 | 2 |
| `-FloorsTab` | 20261107-000504 | not passed | 7 | 1 |
| `-FloorWorkFilter` | 20261107-000541 | PASS | 5 | 0 |
| `-HaulToFloor` | 20261107-000629 | PASS | 78 | 0 |
| `-BenchLink` | 20261107-000717 | PASS | 5 | 0 |
| `-CallColonists` | 20261107-000755 | PASS | 13 | 0 |
| `-StairsEqualize` | 20261107-000838 | PASS | 6 | 0 |
| `-FloorsGuide` | 20261107-000916 | PASS | 3 | 0 |
| `-WayUps` | 20261107-000948 | PASS | 8 | 0 |
| `-FloorPipe` | 20261107-001031 | PASS | 12 | 0 |
| `-StairTransfer` | 20261107-001109 | PASS | 10 | 0 |

The `-FloorsTab` row is the batch run. Its rerun with the user's mod list is run `20261107-002345`, and it passes.

### Fix and rerun folders

- Three-floor pipe: red `20261106-230839`, green `20261106-230929`, fix in commit `c57e5f1`.
- Stale item in the transfer window: red `20261106-231843`, green `20261106-231957`, fix in commit `58668d1`.
- `-UnreachableZone` reruns: `20261107-001507`, `20261107-001639`, `20261107-001828`, `20261107-002001` and `20261107-002129`.
- `-FloorsTab` rerun with the user's mod list, read only: `20261107-002345`.

### Working tree

The runs used the working tree, which holds the vault session's uncommitted work. That is `VaultPocket.xml`, `VaultExitSpot.cs`, a deleted `GenStep_VaultFloor.cs`, an untracked `GenStep_VaultRock.cs`, vault lines in `tools/Invoke-RuntimeValidation.ps1` and `tools/RuntimeValidation/RuntimeValidation.cs`, and the build outputs `ZLevels.dll` and `ZLevelsRimatomics.dll`. Those shared files last changed before the batch began, so every run in the batch tested the same sources. The vault switches (`-VaultPlacement`, `-VaultPocket`, `-VaultEnter`, `-VaultLoad`, `-VaultSave`, `-VaultRock` and `-VaultInfestation`) belong to that session. The committed tree was not run alone, because the game's mod junctions point at this checkout.

### Harness findings

- The batch script read the run path from the last verdict line. PowerShell cuts that line short for a failing run, so the summary lost the path for four runs. It also recorded no folder for `-VaultInfestation`, which stopped on a logged error and printed a different line. The table takes each folder from the log. These are faults in the batch script, not in the scenarios.
- The batch script omitted `-ModsConfigSource` for `-FloorsTab`. That caused its batch failure, as described above.
- The log gate matches ` ERROR ` without regard to case. The fix belongs in the runner, which the vault session also edits, so it was not changed.
- In one earlier core run, 27 of 33 rows reached Player.log (Task 1). The cause is not known, and no later run has shown it.

### Limits and open items

- Decision D6 decided the shot row of `-CrossFloorFight` on 2026-11-07, by its default (a). See "Fighting across floors".
- The `-UnreachableZone` flake has no cause yet.
- The `ClearCell` helper destroys a non-destroyable thing. The vault session should decide the fix.
- `-SocialRelaxQueue` reads the game speed during the screen fade, so its paused row may pass without a pause (Task 4). This was not checked.
- No switch covers these: the pipe texture on screen, saving and loading a pipe, pipe damage, deconstructing one end of a pipe, flow on the three-floor colony, the stair transfer button on the surface, and the window's "Send checked" button with its messages.
- Multiplayer: untested.


## Per-patch timing (Task 24, 2026-11-07)

Task 24 of the multi-floor ideas plan removes the busiest questions if they cost time. Its gate needs per-patch numbers, and the Task 19 timing does not give them. The plan is `docs/superpowers/plans/2026-11-07-task24-patch-timing.md`. Lucas answered D1 with option A on 2026-11-07, and option A starts with measuring.

The gate is measured, and nothing is removed. The reach and reservation patches cost no measurable time in the work search. One attack search loop costs a little, 1.2 to 2.2 microseconds per idle call. Removing it would change how the attack search looks at other floors. Decision D8 in `docs/roadmap.md` asks Lucas about it. The default keeps the loop.

### What the scenario does

`-SearchTiming` runs a per-patch phase after the three-floor timing. The code is in `tools/RuntimeValidation/PatchTimingScenarios.cs`. For each patch group, the phase removes the patch from the running game with `Harmony.Unpatch`, measures, and applies the patch again with `Harmony.Patch`. Each removal has a baseline before it and a baseline after it. Each group runs in three rounds. The mod's code does not change.

- Work search: the Task 19 method on three floors, 500 calls per measure. Groups: `CanReach`, `CanReserve` and `CanReserveStack`.
- Attack probe: 500 calls per measure on one colonist that holds a revolver. No hostile is in range, so every call finds nothing. Groups: `BestShootTargetFromCurrentPosition`, `FindAttackTarget` and `BestAttackTarget`. `FindAttackTarget` is called through reflection, so its times include a fixed reflection cost.
- The row `patch timing recorded` passes only when every removal took effect, every patch was applied again, and every measure had calls.

### Runs

| Run | Result |
|---|---|
| `20261107-173554` | Red, before the phase existed. `patch timing recorded` FAIL with "the per-patch phase did not run". Runner exit 1, as planned. |
| `20261107-173706` and `20261107-173731` | Did not build. A helper named `Find` hid the game's `Find` class, and then two Harmony members had the wrong shape. Both fixed. No rows. |
| `20261107-173858` | PASS. The first run with the phase. |
| `20261107-174047` | PASS. Repeat 2. |
| `20261107-174220` | PASS. Repeat 3. |

### Results

Each cell gives the change in the mean, the drop in microseconds, and the spread of the baselines in that run. The drop is the baseline mean minus the mean without the patch, so a negative drop means the call was slower without the patch. The change is the same comparison as a percentage. The spread is the range of the baseline values around the removal. Rule R2 from the plan says a patch costs time when removing it lowers the mean by at least 10 percent and by more than that spread.

Work search, three floors:

| Patch | Run 173858 | Run 174047 | Run 174220 | Runs where R2 holds |
|---|---|---|---|---|
| `CanReach` | -0.6%, drop 0.8 us, spread 27.9 us | +11.9%, drop -14.8 us, spread 39.4 us | -0.8%, drop 1.6 us, spread 87.5 us | 0 of 3 |
| `CanReserve` | -7.1%, drop 9.8 us, spread 69.3 us | +4.4%, drop -5.3 us, spread 43.7 us | +2.8%, drop -4.8 us, spread 26.9 us | 0 of 3 |
| `CanReserveStack` | +1.9%, drop -2.5 us, spread 8.1 us | +1.0%, drop -1.2 us, spread 23.1 us | +9.9%, drop -15.5 us, spread 33.9 us | 0 of 3 |

Every change in this table is smaller than the spread in its own run. `CanReserveStack` was slower without the patch in all three runs, but its spread was larger than the change each time.

Attack probe, idle, 500 calls per measure:

| Removed patch, metric | Run 173858 | Run 174047 | Run 174220 | Runs where R2 holds |
|---|---|---|---|---|
| `BestShootTargetFromCurrentPosition`, metric `FindAttackTarget` | -29.1%, drop 2.7 us, spread 16.2 us | -30.6%, drop 2.8 us, spread 17.2 us | -26.1%, drop 2.4 us, spread 14.6 us | 0 of 3 |
| `BestShootTargetFromCurrentPosition`, metric `BestShootTargetFromCurrentPosition` | -26.1%, drop 2.0 us, spread 0.4 us | -27.1%, drop 2.0 us, spread 0.5 us | 0.0%, drop 0.0 us, spread 0.5 us | 2 of 3 |
| `FindAttackTarget`, metric `FindAttackTarget` | -31.9%, drop 1.9 us, spread 1.6 us | -29.6%, drop 1.9 us, spread 0.3 us | -11.9%, drop 0.8 us, spread 3.1 us | 2 of 3 |
| `FindAttackTarget`, metric `BestShootTargetFromCurrentPosition` | -3.7%, drop 0.2 us, spread 5.1 us | -12.9%, drop 1.1 us, spread 4.4 us | +12.9%, drop -0.9 us, spread 5.7 us | 0 of 3 |
| `BestAttackTarget`, metric `FindAttackTarget` | -26.9%, drop 1.8 us, spread 0.2 us | -34.1%, drop 2.2 us, spread 0.7 us | -17.4%, drop 1.2 us, spread 0.2 us | 3 of 3 |
| `BestAttackTarget`, metric `BestShootTargetFromCurrentPosition` | -22.8%, drop 1.7 us, spread 0.1 us | -26.5%, drop 1.7 us, spread 3.1 us | -14.8%, drop 1.1 us, spread 0.1 us | 2 of 3 |

The `BestAttackTarget` row for `FindAttackTarget` is the one that holds in every run. The two flag patches, on `BestShootTargetFromCurrentPosition` and on `FindAttackTarget`, set a flag that the same loop reads. The `FindAttackTarget` flag drops that metric by 1.9 microseconds in two runs, and by 0.8 microseconds in the third, where the spread was 3.1. The `BestShootTargetFromCurrentPosition` flag drops its own metric by 2.0 microseconds in two runs and by nothing in the third. The `BestShootTargetFromCurrentPosition` patch also lowers the `FindAttackTarget` metric by 2.4 to 2.8 microseconds in all three runs. The code gives no path for that. `FindAttackTarget` calls `BestAttackTarget` directly, and the shoot flag is set only inside `BestShootTargetFromCurrentPosition`. The cause is not known. The probe runs both metrics in one call, so the effect may come from the probe. Those numbers are not used.

### What the numbers mean

- The busiest questions are not the cost. Removing a reach or reservation patch changed the work search by less than the run's own spread. Leaving them alone would save nothing that can be measured.
- The attack loop costs 1.2 to 2.2 microseconds per idle call. A three-floor work search call took 120 to 192 microseconds in these runs, so the loop is under 2 percent of one work search call. The work search is not where the time goes.
- The loop is the search of other floors for targets. The prefix of `BestAttackTarget` in `ZLevels/Source/ZLevels/ZLevels/HarmonyPatches/CombatPatches.cs` runs it, nearest floor first, and keeps the first target it finds. Removing it would stop that search. The effect on fights is untested.

### Rule R6 and Task B

Rule R2 is applied per run. This report counts a patch as costing time when R2 holds in all three runs. That is a ruling made after the repeats. It makes a removal harder to justify, not easier. Read per run, the two flag patches would also count in some runs.

Task B of the plan removes each patch that costs time, one at a time, with a row for each removal. Task B is not run. Only the loop meets R6. Removing it changes the attack search, and no row in the plan tests that search across floors. A removal needs a row that shows a hostile on one floor still finds a colonist on another floor with the loop gone. That row does not exist yet. Decision D8 asks Lucas. The default keeps the loop.

### Limits

- The probe is idle. Every call found nothing, so the attack numbers are the cost of a search with no target. A fight with targets is not measured.
- The surface is random in every run, because the runner starts a new random game. The three-floor work search baselines were 120 to 192 microseconds, so the runs differ. Each run compares only its own removals.
- `FindAttackTarget` is called through reflection. Its times include a fixed reflection cost, and its percentages are against those times.
- The spreads are large for the work search and for the `FindAttackTarget` probe. Three runs cannot separate small effects from noise there.
- The probe is noisy by about 2 microseconds in some windows. In run `20261107-174220`, removing the `BestShootTargetFromCurrentPosition` patch changed its own metric by 0.0 percent, while the other two runs dropped by 2.0 microseconds. The flag patches are not relied on for the decision. The `BestAttackTarget` result is the one used, and the code gives it a path to both metrics.
- The cost of `CanBeSeenOverFast`, `TryFindShootLineFromTo` and the four `TryGiveJob` patches is not measured. They are not in the plan's list.
- Where the work search spends its time is not known. No part of the search has been profiled.

### Run folders

- Red: `.artifacts/runs/20261107-173554`.
- Green and repeats: `.artifacts/runs/20261107-173858`, `.artifacts/runs/20261107-174047` and `.artifacts/runs/20261107-174220`.
- Did not build: `.artifacts/runs/20261107-173706` and `.artifacts/runs/20261107-173731`. They have no rows.
- Harness commit: `e1d5291`.
