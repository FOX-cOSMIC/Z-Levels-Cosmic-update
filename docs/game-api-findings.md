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
- `Thing.DrawAt` draws the graphic in real time only when `drawerType` is `RealtimeOnly`, or when the thing is not spawned (`Verse.Thing` decompile, line 1326). A `MapMeshAndRealTime` thing draws its graphic through `Print` instead (`Verse.Thing` decompile, line 1333).
- `Verse.Building` declares no `DrawAt` or `Print` override (`Verse.Building` decompile, no matches).
- `DynamicDrawManager.DrawDynamicThings()` is `public void` (`Verse.DynamicDrawManager` decompile, line 182). The map it draws is the private field `map` (line 145), which a Harmony postfix reads as `___map`. ZLevels already patches this method (`RenderPatches.cs`, line 103).
- `Map.MapUpdate()` calls `dynamicDrawManager.DrawDynamicThings()` at line 1177. That call sits inside the block that starts at line 1162 with `if (drawingMap && Find.CurrentMap == this)`. A postfix on the method therefore runs once per frame, for the map in view (`Verse.Map` decompile, lines 1162-1177).
- `GenDraw.DrawFieldEdges(List<IntVec3> cells, Color color, float? altOffset = null, HashSet<IntVec3> ignoreBorderCells = null, int renderQueue = 2900)` is `public static void` (`Verse.GenDraw` decompile, line 487). It draws on `Find.CurrentMap` (line 489) with `Graphics.DrawMeshInstanced` (line 534) and an instancing-enabled material (line 492). Its outline material is `Misc/FieldEdge` (line 66).
- The partner highlight does not call `DrawFieldEdges`. It draws one plane per open edge with `Graphics.DrawMesh(MeshPool.plane10, matrix, material, 0)`, the form that `GenDraw.DrawLineBetween` uses (`Verse.GenDraw` decompile, line 192). With the instanced `DrawFieldEdges` call, two of five look runs lost the GPU device. The failures reported `VK_ERROR_DEVICE_LOST` in runs `20261102-215009` and `20261102-215926`. Five look runs with the per-edge draw had no device loss: `20261102-220522`, `20261102-220809`, `20261102-220916`, `20261102-221026`, and `20261102-221353`. The cause is unknown.
- `MaterialPool.MatFrom(string texPath, Shader shader, Color color, int renderQueue)` cannot find `Misc/FieldEdge` by path (`Verse.MaterialPool` decompile, line 64). Each failing call logs `Could not load Texture2D at 'Misc/FieldEdge' in any active mod or in base resources.` and then `MatFrom with null sourceTex.` A call on every frame floods the log (run `20261102-220338`). `MatLoader.LoadMat("Misc/FieldEdge")` returns the material (`Verse.MatLoader` decompile, line 60). `MaterialPool.MatFrom(Texture2D srcTex, Shader shader, Color color, int renderQueue)` (line 40) takes the `mainTexture` of that material.
- `GenAdj.CardinalDirections` is `public static IntVec3[]` (`Verse.GenAdj` decompile, line 9).

## Light

- `TorchLamp` (`Data/Core/Defs/ThingDefs_Buildings/Buildings_Furniture.xml`, line 1153) has a `CompProperties_Glower` with `glowRadius` 10. Its parent `TorchBase` (line 1067) adds a wood-fuelled `CompProperties_Refuelable` that starts full (`initialFuelPercent` 1, line 1108). It needs no power network.
- The lower level is unlit, and rock limits the glow. The torch lit only its own cell while rock surrounded it (run `20261102-215753`). With the cells within two of the highlight cell cleared, it lit them (run `20261102-220522`).

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

## Costs and stats

- `BuildableDef.CostList` and `BuildableDef.CostStuffCount` return the values from `costListForDifficulty` when it is set and applies. Otherwise they return `costList` and `costStuffCount` (`Verse.BuildableDef` decompile, lines 190-212).
- `RimWorld.StatExtension.GetStatValueAbstract` is declared as `public static float GetStatValueAbstract(this BuildableDef def, StatDef stat, ThingDef stuff = null)` (`RimWorld.StatExtension` decompile, line 17).
- `BuildingProperties.isEdifice` is a public `bool` with default `true` (`RimWorld.BuildingProperties` decompile, line 12).
- `ThingDef.holdsRoof` is a public `bool` (`Verse.ThingDef` decompile, line 304). `ThingDef.drawerType` is a public `DrawerType` with default `RealtimeOnly` (line 189).

## Components

- `ThingComp.PostSpawnSetup(bool respawningAfterLoad)` is `public virtual` (`Verse.ThingComp` decompile, line 31). `ThingComp.parent` is a public `ThingWithComps` field (line 12).
- `ThingComp.PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)` is `public virtual` (`Verse.ThingComp` decompile, line 39). An override can omit the default value.
- `ThingWithComps.AllComps` is a public `List<ThingComp>` property (`Verse.ThingWithComps` decompile, line 33).
- `ThingWithComps.GetComp<T>()` is declared as `public T GetComp<T>() where T : ThingComp` (`Verse.ThingWithComps` decompile, line 126).
- `CompProperties.compClass` is a public `Type` field with default `typeof(ThingComp)` (`Verse.CompProperties` decompile, line 12).

## Linked graphics

- `GraphicData` wraps its graphic with `GraphicUtility.WrapLinked` when `Linked` is true, which is when `linkType` is not `None` (`Verse.GraphicData` decompile, lines 92 and 178-180). `WrapLinked` returns a `Graphic_Linked` for `Basic` (`Verse.GraphicUtility` decompile, lines 45-50).
- `Graphic_Linked.LinkedDrawMatFrom` adds 1, 2, 4, and 8 for the four entries of `GenAdj.CardinalDirections`, in order, where `ShouldLinkWith` is true for that neighbour. The sum picks one of 16 tiles (`Verse.Graphic_Linked` decompile, lines 49-63). The order comes from `Verse.GenAdj` decompile, lines 51-54.
- Each atlas tile covers 0.1875 of the texture in each direction. The tile offsets step by 0.25 across a 4 by 4 grid (`Verse.MaterialAtlasPool` decompile, lines 16-20). A tile therefore never shows the whole texture.

## Mod patches

- `PatchOperationFindMod` applies its match when `ModLister.HasActiveModWithName` returns true for any name in `mods` (`Verse.PatchOperationFindMod` decompile, line 19).
- `ModLister.HasActiveModWithName` compares each active mod's `Name` with the given string. It does not compare the package id (`Verse.ModLister` decompile, lines 209-213). A patch that names `dubwise.rimatomics` did not match in run `20261102-104504`. A patch that names `Dubs Rimatomics` matched in run `20261102-104625`.

## Things, maps and positions

- `Thing.Spawned` is a public `bool` property (`Verse.Thing` decompile, line 194). `Thing.Map` is a public `Map` property (line 233). `Thing.Position` is a public `IntVec3` property (line 261). `Thing.Tile` is a public `PlanetTile` property (line 536).
- `Def.defName` is a public `string` field (`Verse.Def` decompile, line 12). It defaults to `"UnnamedDef"`. The cooling column logs use `parent.def.defName` to name the column.
- `Map.Tile` is a public `PlanetTile` property (`Verse.Map` decompile, line 384). `Map.Center` is a public `IntVec3` property (line 264).
- `Map.MapUpdate()` is a public `void` method (`Verse.Map` decompile, line 1148).
- `IntVec3Utility.DistanceTo(this IntVec3 a, IntVec3 b)` returns a `float` (`Verse.IntVec3Utility` decompile, line 13). It returns the horizontal length of `a - b`. `IntVec3.LengthHorizontal` computes that length as `GenMath.Sqrt(x * x + z * z)` (`Verse.IntVec3` decompile, line 50). Two cells 3 apart along one axis have a distance of exactly 3.
- `Current.Game` is a public static `Game` property (`Verse.Current` decompile, line 43). `Game.GetComponent<T>()` is declared as `public T GetComponent<T>() where T : GameComponent` (`Verse.Game` decompile, line 683).
- `DestroyMode` is a public enum with `byte` as its underlying type, in namespace `Verse` (`Verse.DestroyMode` decompile, line 3). Its first member is `Vanish` (line 5).
- `Map.Disposed` is a public `bool` property with a private setter (`Verse.Map` decompile, line 335).
- `Map.thingGrid` is a public `ThingGrid` field (`Verse.Map` decompile, line 99).
- `ThingGrid.Register(Thing t)` is public. It registers a one-cell thing at `t.Position`, and a larger thing over `t.OccupiedRect()` (`Verse.ThingGrid` decompile, lines 24 to 31).
- `ThingGrid.ThingsAt(IntVec3 c)` is a public method that returns `IEnumerable<Thing>`. It yields nothing for a cell off the map, because it checks `c.InBounds(map)` first (`Verse.ThingGrid` decompile, lines 93 to 103).
- `ThingGrid.ThingsListAt(IntVec3 c)` is a public method that returns `List<Thing>`. For a cell off the map it calls `Log.ErrorOnce` (id 495287). The validation runner fails a run on any ERROR line except its packageId duplicate notices, so the Z-Levels grid check uses `ThingsAt` instead (`Verse.ThingGrid` decompile, lines 105 to 114).
- `Thing.def` is a public `ThingDef` field (`Verse.Thing` decompile, line 17).
- `Thing.Map` returns null when `mapIndexOrState` is below zero (`Verse.Thing` decompile, lines 233 to 241).
- `Thing.Spawned` returns false when `mapIndexOrState` is below zero or `Find.Maps` is null (`Verse.Thing` decompile, lines 194 to 198).
- `Thing.Notify_MyMapRemoved()` sets `mapIndexOrState` to -3 for a thing that no pawn holds (`Verse.Thing` decompile, lines 1123 to 1135).

## Optional assembly access

- `ZUtils` is an `internal static class` in namespace `ZLevels` (`ZLevels/Source/ZLevels/ZLevels/ZUtils.cs`, line 16). No `InternalsVisibleTo` attribute exists in the repository, so `ZLevelsRimatomics` cannot name `ZUtils`.
- `ZUtils.ZTracker` caches the manager it finds. `ZUtils.ResetZTracker()` clears the cache (`ZUtils.cs`, lines 18 and 45). `CoolingLoopRegistry.Paired` calls `Current.Game.GetComponent<ZLevelsManager>()` instead.

## Logging and spawning

- `Log.Error(string text)` is a public static method (`Verse.Log` decompile, line 152).
- `Log.Warning(string text)` is a public static method (`Verse.Log` decompile, line 125).
- `GenSpawn.Spawn(Thing newThing, IntVec3 loc, Map map, WipeMode wipeMode = WipeMode.Vanish)` returns `Thing` (`Verse.GenSpawn` decompile, line 26). It forwards to the overload at line 86, which returns null when the map is null (line 91) or the cell is out of bounds (line 107). Each case logs an error first.
