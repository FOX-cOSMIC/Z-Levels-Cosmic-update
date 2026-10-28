# Z-Levels 1.7 validation report

Date: 2026-10-28. Machine: Windows 11 PC. Game: RimWorld 1.7.4990 rev1069, Steam unstable branch.

## Result

- The clean Release build of the final C# sources passes with 0 errors and 8 warnings (7 distinct messages). Log: `.artifacts/build/release-build-final.log`.
- The final visual validation passed. Its last line is `Runtime validation passed. Evidence: .artifacts/runs/20261028-041107`. Every PASS/FAIL check reports PASS, including the stairs checks after a save reload. The only ERROR line is the duplicate-package message for another mod, `Aoba.DeadManSwitch.Core`.
- The final run also logs 44 animal-spawn warnings from the lower level. They do not fail the harness. See "Open issues".
- Two earlier runs failed the harness. Run 035000 hit a stairs `KeyNotFoundException`, which is fixed. Run 032903 hit a refuel job error, which is not explained.
- Not verified: multiplayer, existing saves, DLC and other mods, float-menu options that cross levels, combat across levels, and hauling or power after a reload. See "Not tested".

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
| 035000 | visual | none | 1 | All scenarios PASS. Failed on the stairs ERROR line (fixed) |
| 035409 | visual | 1607 of 1607 / 1449 of 1449 | 0 | All scenarios PASS. Built before the roof-terrain fix |
| 035538 | headless | none | 0 | All scenarios PASS. Built before the roof-terrain fix |
| 040440 | visual | none | 0 | All scenarios PASS, 4 screenshots. Ran before the reload stairs checks were added |
| **041107** | **visual** | none | **0** | **Final.** All scenarios PASS, including stairs down and back up after the reload, 4 screenshots |

Earlier development runs (folders from 003430 to 032749) used earlier code. Their logs are still under `.artifacts/runs/`, but they are not evidence for the final build.

## Scenario results

The harness records each step as PASS or as an informational line. Visual runs also take screenshots.

| Scenario | What the harness checks | 032903 | 035000 | 035409 | 035538 | 040440 | 041107 |
|---|---|---|---|---|---|---|---|
| fresh colony | surface is Z-index 0 | PASS | PASS | PASS | PASS | PASS | PASS |
| stairs down and underground generation | pawn reaches the lower level (index -1) | PASS | PASS | PASS | PASS | PASS | PASS |
| return upstairs | pawn returns to the surface by the lower level's up stairs | PASS | PASS | PASS | PASS | PASS | PASS |
| stairs up and upper generation | pawn reaches the upper level (index +1) | PASS | PASS | PASS | PASS | PASS | PASS |
| return downstairs | pawn returns to the surface; three levels tracked | PASS | PASS | PASS | PASS | PASS | PASS |
| cross-level hauling | 7 wood logs hauled from the surface to a lower-level cell, quantity kept | PASS | PASS | PASS | PASS | PASS | PASS |
| power bring-up (ticks until lamp is powered, informational) | transmitters placed, lower lamp then on | 272 | 445 | 267 | 404 | 287 | 273 |
| cross-level power transfer | lower lamp on, powered from the surface generator | PASS | PASS | PASS | PASS | PASS | PASS |
| cross-level power loss without source | lamp off after the generator is removed | PASS | PASS | PASS | PASS | PASS | PASS |
| surface hole over lower wall | hole terrain created; lower cell revealed (visual runs only) | not run | PASS | PASS | not run | PASS | PASS |
| save and reload three-level colony | save, reload; three levels and pawn on surface | PASS | PASS | PASS | PASS | PASS | PASS |
| reload stairs down | after the reload, the pawn takes the reloaded stairs down to the lower level | not run | not run | not run | not run | not run | PASS |
| reload stairs return | after the reload, the pawn takes the lower level's up stairs back to the surface | not run | not run | not run | not run | not run | PASS |
| screenshots: lower level, upper void, surface, surface hole | written to `Screenshots/` (visual runs only) | not run | 4 | 4 | not run | 4 | 4 |

Lower-level glow (informational): the lamp cell reads `RGBA(203, 140, 89, 0)` in every run. The sample three cells away is `RGBA(90, 62, 39, 0)` in 032903 and zero in the other runs. The check does not prove glow propagation, so it is not used as evidence.

The final run's lower-level screenshot shows the lit lamp room with the rest of the lower level unexplored. I checked it by eye. The other screenshots were not compared with a baseline.

## Failures found and resolved

1. **River and coast generation.** 1.7 removed `RiverMaker` and `BeachMaker`, so the build failed with CS0103 at three call sites. The port now calls the 1.7 `TileMutatorWorker` river and coast hooks from `UndergroundWaterMutators.cs`. Proof: the build has 0 errors, and runs 032903 and 035409 show every water cell on both levels as a river cell.

2. **Stairs `KeyNotFoundException` (run 035000).** `ZUtils.GetAllMapsInClosestOrder` read `stairsUp[lowerMap]` directly. `Building_StairsUp` and `Building_StairsDown` `SpawnSetup` replace the stairs dictionaries, and `ReCheckStairs` refills them on the next game tick, so a map can have no entry in between. The error came from a combat target search (`CombatPatches_BestAttackTarget_Patch`) run during a pawn's think tree. The fix reads through `GetStairsUpFor` and `GetStairsDownFor`, which return an empty list for a missing key. `GetClosestStair` also returns null for an empty list; it used to create `new Dictionary(targetList.Count - 1)`, which throws for an empty list. Proof: no recurrence in runs 035409, 035538, 040440 and 041107. The trigger was seen once, and no scenario reproduces it on purpose, so the fix is shown only by its absence.

3. **Roof terrain still used a 1.6 field (found in the final review of the defs).** `ZL_RoofTerrain` kept `<holdSnow>`. The vanilla 1.7 defs use `holdSnowOrSand`, and none use `holdSnow`. The 1.7 patch in `ZLevels/1.7/Patches/TerrainFields.xml` covered only `ZL_OutsideTerrain` and `ZL_OutsideTerrainTwo`. The patch now covers `ZL_RoofTerrain` too. Proof: runs 040440 and 041107 load with no ERROR line except the duplicate-package message. Its XPath matches the one `holdSnow` node in `RoofTerrain.xml` (the only TerrainDef in that file). The game writes no log line for a successful XML patch, and it does not report unknown fields, so the run log cannot confirm that the patch applied.

## Open issues

**Refuel job pause (run 032903).** The log has:

```
ERROR [Verse] Pausing, reason: Fail in TryMakePreToilReservations, pawn: Summers, job: Refuel ... driver: RimWorld.JobDriver_Refuel, map: Map-0-PlayerHome
```

`ZLogger.Pause` raised it from `ZLevelsManager.TryTakeFirstJob`. Both are upstream code that the port did not change. It happened during the power bring-up scenario, while a colonist refuelled a wood-fired generator on the surface map. The harness counts ERROR lines as failures, so the run exited 1 even though every scenario passed.

It was seen once, and it has not appeared in runs 035000, 035409, 035538, 040440 or 041107. It has not been reproduced. The cause is not known. It could be a queued job whose reservations are on another level, or an ordinary vanilla reservation failure.

**Save and reload.** There is one save and one reload per run. After the reload, the checks confirm three levels, the pawn on the surface, and a trip down the stairs and back up (`reload stairs down` and `reload stairs return`, run 041107). Hauling and power are not repeated after the reload.

**Animal spawn warnings on the lower level (runs 035000 and 041107).** Generating the lower level runs the underground animal step (`GenStep_AnimalsUnderground.Generate`, `ZLevels/Source/ZLevels/ZLevels/GenSteps/GenStep_Animals.cs:44`), which calls the vanilla `RCellFinder.RandomAnimalSpawnCell_MapGen` when no infestation spot applies. Run 041107 logs `WARN [Verse] RandomAnimalSpawnCell_MapGen failed` 44 times. Every line reports `numDistrict=0`, and each line returns a different cell. Run 035000 logs it 136 times. No other run folder has the line, including the earlier development runs. The cause is not known, and nothing checked whether the animals ended up where they can reach the colony. The harness does not fail on warnings.

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
- **Warnings.** The 8 build warnings were left in place. All come from upstream code or the package restore. The runtime warnings of the final run are under Open issues. The other runs' logs were not reviewed for warnings beyond the animal-spawn line.
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

The last line should be `Runtime validation passed. Evidence: <run folder>`.

## Evidence

- Build log: `.artifacts/build/release-build-final.log`
- Final run: `.artifacts/runs/20261028-041107/` (`validation-results.txt`, `Player.log`, `Screenshots/`, `Config/`, `Saves/`)
- Earlier runs: `.artifacts/runs/20261028-040440/`, `20261028-035409/`, `20261028-035538/`, `20261028-035000/`, `20261028-032903/`
- Decompiles: `.artifacts/ilspy/` (git-ignored)
