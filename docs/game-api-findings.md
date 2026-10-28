# RimWorld 1.7 API findings for Z-Levels

Each fact below was confirmed against the installed game (RimWorld 1.7.4990 rev1069) and cites where it was read. Decompiles are written under `.artifacts/ilspy/` (gitignored). Regenerate one type with:

```
ilspycmd -t <Namespace.Type> "C:/SteamLibrary/steamapps/common/RimWorld/Assembly-CSharp.dll" -o .artifacts/ilspy/<name>
```

Game data is under `C:/SteamLibrary/steamapps/common/RimWorld/Data/Core/Defs/`.

## Map generation

- `RiverMaker` and `BeachMaker` do not exist in 1.7. The 1.6 code that used them fails to compile (CS0103). Rivers and coasts now come from `RimWorld.TileMutatorWorker` subclasses (`TileMutatorWorker_River`, `TileMutatorWorker_Coast`).
- `TileMutatorWorker` virtual hooks: `Init(Map)`, `Tick(Map)`, `GeneratePostElevationFertility(Map)`, `GeneratePostTerrain(Map)`, `GenerateCriticalStructures(Map)`, `GenerateNonCriticalStructures(Map)`, `GeneratePostFog(Map)`. Source: `TileMutatorWorker.cs` decompile, lines 25-59.
- Engine pipeline (`Data/Core/Defs/MapGeneration/CommonMapGenerator.xml`): `GenStep_ElevationFertility`, then `MutatorPostElevationFertility`, then `GenStep_Terrain`, then `MutatorPostTerrain`, then the rest, then `GenStep_Fog`, then `MutatorFinal`.
- The ZLevels underground and upper definitions in `ZLevels/Common/Defs/MapGenerator/` contain no `Mutator*` gensteps (grep finds none), so the engine's `GeneratePost*` calls do not reach ZLevels maps. `UndergroundWaterMutators.RiverAndCoastWorkers` calls those hooks itself. Checked in run `20261028-035409`: surface and lower river cells are equal to their water cells.
- `Map.TileInfo` returns `pocketTileInfo` when it is set and the world tile otherwise (`Verse.Map` decompile, lines 386-396). ZLevels maps share the surface tile, so `TileInfo` matches the surface (`same tile: True` in every run).

## Drawing

- `Thing.DrawPos` is `public virtual Vector3 DrawPos => this.TrueCenter();` (`Verse.Thing` decompile, line 624). A Harmony patch on the base getter does not reach an override. `Mote` overrides it, so ZLevels patches `Mote.DrawPos` separately (`RenderPatches.cs`).
- `Thing.DrawNowAt(Vector3 drawLoc, bool flip = false)` (`Verse.Thing` decompile, line 1303).
- `Thing.Draw()` is not present in 1.7. Use `DrawNowAt`.

## Float menus

- `FloatMenuMakerMap.GetOptions(List<Pawn> selectedPawns, Vector3 clickPos, out FloatMenuContext context)` is the entry point. Options come from `FloatMenuOptionProvider` subclasses.
- `AddHumanlikeOrders`, `AddJobGiverWorkOrders`, `AddUndraftedOrders` and `AddDraftedOrders` are absent from 1.7. They do not appear in the assembly's string table, while `GetOptions` does (positive control). The ZLevels patches for these therefore run on `GetOptions`, after vanilla has listed its own options.
- The two-argument `FloatMenuMap.StillValid(FloatMenuOption, List<FloatMenuOption>)` revalidates with `Find.Selector.SelectedPawns` (`Verse.FloatMenuMap` decompile, lines 92-95). The 1.6 `forPawn` parameter does not exist.
- `FloatMenuUtility.DecoratePrioritizedTask(option, pawn, target, ...)` sets `option.revalidateClickTarget = target.Thing` (`RimWorld.FloatMenuUtility` decompile, line 190). Vanilla work options therefore carry their item, and a label is the only key that matches options across providers.

## Beds

- `RestUtility.FindBedFor(Pawn sleeper, Pawn traveler, bool checkSocialProperness, bool ignoreOtherReservations = false, GuestStatus? guestStatus = null)` (`RimWorld.RestUtility` decompile).
- Vanilla Capture and Arrest providers search with `checkSocialProperness: false` and `GuestStatus.Prisoner`. Vanilla Rescue searches with `checkSocialProperness: false` and no guest status (`FloatMenuOptionProvider_CapturePawn`, `_Arrest`, `_RescuePawn` decompiles, lines 37-50).
- Vanilla Arrest returns no option unless the selected pawn is drafted or the target is a wild man who is not a prisoner (`FloatMenuOptionProvider_Arrest`, line 27).

## Power

- `CompPowerPlant.UpdateDesiredPowerOutput()` is `public virtual`. It sets `PowerOutput` to 0 when the plant is broken, unfuelled, switched off, or unpowered. Otherwise it sets `PowerOutput` to `DesiredPowerOutput` (`CompPowerPlant` decompile, lines 65-75).

## Planet tiles

- `PlanetTile.ToString()` returns `"{tileId},{layerId}"`. `PlanetTile.TryParse` accepts one or two integer fields, so bare-integer keys from older saves also parse (`RimWorld.Planet.PlanetTile` decompile, lines 99-127).

## Designators

- `Designator_Build.ProcessInput` checks material availability with `ListerThings.ThingsOfDef` only inside the `entDef is ThingDef { MadeFromStuff: not false }` branch (decompile, lines 398-411). `ZLevels/Source/ZLevels/ZLevels/HarmonyPatches/ResourceCounterPatch.cs` depends on that: its transpiler requires exactly one such call and throws otherwise.

## ZLevels-specific behaviour

- `Building_StairsUp.SpawnSetup` and `Building_StairsDown.SpawnSetup` replace `ZLevelsManager.stairsUp` and `stairsDown`. `ReCheckStairs` refills them on the next game-component tick. In between, a map can have no entry, so any reader must tolerate a missing key. Readers use `GetStairsUpFor` and `GetStairsDownFor`. Run `20261028-035000` threw `KeyNotFoundException` for key `Map-1` in `ZUtils.GetAllMapsInClosestOrder` during a hostile pawn's combat think, before this change.

## Patch-target name check

- 77 string-named Harmony targets were extracted from the source. 74 names appear in `Assembly-CSharp.dll`. The other three (`AddUndraftedOrders`, `GotoLocationOption`, `TargetsAt_NewTemp`) occur only in commented-out patches in `FloatMenuPatches.cs` (lines 130, 140, 155).
- This checks names only. Signatures are checked for the points above and by the runtime load: the validation runs log no Harmony patch errors.

## Underground animals and stairs

- `RCellFinder.RandomAnimalSpawnCell_MapGen(Map)` tries up to 1000 cells with `CellFinderLoose.TryGetRandomCellWith`. A cell qualifies if it is standable, its terrain is not `avoidWander` or `dangerous`, and its `District` touches the map edge (decompile, lines 597-628). When none qualifies, it logs a warning with three counters and returns `CellFinder.RandomCell(map)` (lines 629-649).
- `CellFinder.RandomCell(Map)` picks a uniform cell from the whole map, walkable or not (`Verse.CellFinder` decompile, lines 52-55).
- `CellFinder.RandomClosewalkCellNear(root, map, radius)` returns `root` when `TryFindRandomReachableNearbyCell` finds nothing (decompile, lines 301-308). That search moves a non-standable root to an adjacent standable cell when a thing on the root blocks standing. It returns false when the root has no region (decompile, from line 211).
- `WildAnimalSpawner.SpawnRandomWildAnimalAt(loc, canFlyIn, animalKind)` picks a kind from the biome and spawns each group member near `loc` through `RandomClosewalkCellNear` (`RimWorld.WildAnimalSpawner` decompile, lines 109-161). ZLevels replaces this method with a Harmony prefix while `allowZLevelsInfestation` is true. The setting defaults to true (`ZLevels/Source/ZLevels/ZLevels/ZLevelsSettings.cs:14`). The prefix is in `ZLevels/Source/ZLevels/ZLevels/HarmonyPatches/AnimalPatches.cs:21-79`.
- `GenSpawn.Spawn` defaults to `WipeMode.Vanish`. That calls `WipeExistingThings`, which destroys an existing thing only where `SpawningWipes(newDef, oldDef)` returns true (decompile, lines 86-151, 267-279, 355 onward). By those rules a pawn spawn does not remove natural rock. The game defs define no `blocksAltitudes`. Not run in the game.
- A pawn on an unwalkable cell moves only when it starts a path. `Pawn_PathFollower.StartPath` calls `TryRecoverFromUnwalkablePosition()` (decompile, line 164). That method moves the pawn to the first walkable cell in `GenRadial.RadialPattern`. It logs `Teleporting to` when `error` is true (lines 529-555). None of the 53 run logs contains that line or the `Could not find walkable position` error that follows it.
- Natural rock is a mineable building. `Limestone` sets `mineableThing` to `ChunkLimestone`, and `Granite` sets it to `ChunkGranite` (`Data/Core/Defs/ThingDefs_Misc/Various_Stone.xml`). On the lower level, a rock cell has terrain `<stone>_Rough`, roof `RoofRockThick`, and a `<stone>` thing. A carved floor has terrain `<stone>_RoughHewn` and no rock thing (runs `20261028-134416` and `20261028-135603`).
- `ZLevelsManager.CreateLowerLevel(origin, playerStartSpot)` stores the start spot it receives (`ZLevels/Source/ZLevels/ZLevels/ZLevelsManager.cs:1557-1563`). Only `GenStep_FindLocationUnderground` reads the property. It only copies it to `MapGenerator.PlayerStartSpot` (`ZLevels/Source/ZLevels/ZLevels/GenSteps/GenStep_FindLocationUnderground.cs:21`).
