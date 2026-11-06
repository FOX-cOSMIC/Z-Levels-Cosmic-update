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
- `MatLoader.LoadMat` returns `BaseContent.BadMat`, not null, when a path does not load (`Verse.MatLoader` decompile, lines 72-73). It logs `Could not load material <path>` on every call, because a failed load is not cached (line 60 onward). `BadMat` is the placeholder material (`Verse.BaseContent` decompile, line 14), and its texture is `BaseContent.BadTex` (line 16). `GenDraw` loads its outline as `TargetSquareMatSide = MatLoader.LoadMat("Misc/FieldEdge")` (`Verse.GenDraw` decompile, line 66). The partner highlight checks for `BadMat` and `BadTex` by identity, because a null check on the result passes for the placeholder.
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

## Load order and ticks

- `Find.TickManager` is `Current.Game.tickManager` (`Verse.Find` decompile, `.artifacts/ilspy/cooling-task7-find/`, line 148). It is not null-safe for `Current.Game`. The `?.` in `CompCoolingColumn.PostSpawnSetup` guards the tick manager only.
- `Game.tickManager` is set by a field initializer, `new TickManager()` (`Verse.Game` decompile, `.artifacts/ilspy/cooling-task4/Verse.Game/Verse.Game.decompiled.cs`, line 61). It exists before any load.
- `Game.LoadGame` (line 560 of that decompile) calls `ExposeSmallComponents()` at line 571. It exposes the maps at line 589, calls `Scribe.loader.FinalizeLoading()` at line 611, and calls each `Map.FinalizeLoading()` at line 619. The tick manager is loaded before any map is finalized.
- `Scribe_Deep.Look` on load sets the target from the saved node (`Verse.Scribe_Deep` decompile, `.artifacts/ilspy/cooling-task7-find/`, from line 74). A save with no `tickManager` node would load a null tick manager. The game writes that node in every save.

## Message lock

- `Log.LockMessages()` returns a `LogLock`, which implements `IDisposable` (`.artifacts/patch-api/Log.cs`, lines 14 and 78 to 80). The constructor increments `logDisablers` (lines 16 to 19), and `Dispose` decrements it (lines 21 to 23).
- `Log.PreventLogging` is a private static property. It is true while `reachedMaxMessagesLimit` is set or `logDisablers` is above zero (lines 66 to 76). `Log.Warning` writes only while it is false (line 129), and `Log.Error` returns early while it is true (lines 156 to 158).
- `ZLevelsRimatomics` reads `PreventLogging` through a cached reflection delegate, so it can tell whether the log will write.

## GameComponent creation and ticks

- `Verse.Game.FillComponents` creates each non-abstract subclass of `GameComponent` on its own. It loops over `typeof(GameComponent).AllSubclassesNonAbstract()` at line 475, and it calls `Activator.CreateInstance(item2, this)` at line 481, which passes the game as the only argument. A component class therefore needs a public constructor that takes a `Game`. A failed construction logs "Could not instantiate a GameComponent of type" at line 486 (`.artifacts/ilspy/cooling-task4/Verse.Game/Verse.Game.decompiled.cs`, `FillComponents` from line 472).
- `Verse.GenTypes.AllSubclassesNonAbstract` (`.artifacts/ilspy/fix9-gamecomponent/Verse.GenTypes/Verse.GenTypes.decompiled.cs`, line 195) runs `AllTypes.AsParallel()` at line 197, and it caches the result per base type. The game does run parallel work in this place.
- `Game.ExposeSmallComponents` saves the `components` list with `Scribe_Collections.Look(ref components, "components", LookMode.Deep, this)` at line 443 (same file, `ExposeSmallComponents` from line 407). Each component in the list is written into the save. Run 20261103-101432 wrote `<li Class="ZLevelsRimatomics.CoolingLogComponent" />` into `.artifacts/runs/20261103-101432/Saves/ZLevelsValidation.rws`. The reload check in that run passes with the entry in the save. Round 10 removed the class, so the project no longer uses a GameComponent for the cooling log. Run 20261103-102622 writes no entry for it. Loading a save that holds the old entry, in a build without the class, is untested. This section does not check how the loader handles that entry.
- `Verse.GameComponent` is `public abstract class GameComponent : IExposable` (`.artifacts/ilspy/fix9-gamecomponent/Verse.GameComponent/Verse.GameComponent.decompiled.cs`, line 5). Its `public virtual void GameComponentTick()` is at line 11, and the body is empty.
- The tick path is `Verse.TickManager.DoSingleTick` (`.artifacts/ilspy/fix9-gamecomponent/Verse.TickManager/Verse.TickManager.decompiled.cs`, line 361). It calls `GameComponentUtility.GameComponentTick()` at line 464, after `Find.History.HistoryTick()`. `TickManagerUpdate` calls `DoSingleTick` at line 344. Nothing in this path depends on a cooling net. `GameComponentUtility.GameComponentTick` (`.artifacts/ilspy/fix9-gamecomponent/Verse.GameComponentUtility/Verse.GameComponentUtility.decompiled.cs`, lines 24 to 31) walks `Current.Game.components`, and it calls each one inside a try block.
- `.artifacts/runs/20261030-192636/Player.log`, lines 4839 to 4844, shows the same chain at run time: `ZLevelsManager.GameComponentTick`, `GameComponentUtility.GameComponentTick`, `TickManager.DoSingleTick`, `TickManager.TickManagerUpdate`, `Game.UpdatePlay`, and `Root_Play.Update`.
- The project already has a component of this kind. `ZLevels.ZLevelsManager` (`ZLevels/Source/ZLevels/ZLevels/ZLevelsManager.cs`) has the class at line 15, a `(Game game)` constructor at line 22, and a `GameComponentTick` override at line 30.
- `Verse.GameComponentUtility` is a `public static class`. Its file, `.artifacts/ilspy/fix9-gamecomponent/Verse.GameComponentUtility/Verse.GameComponentUtility.decompiled.cs`, declares the class at line 6. The method `public static void GameComponentTick()` starts at line 24. It takes no parameters and returns void, so a Harmony postfix `static void Postfix()` matches it. At line 31, it calls `components[i].GameComponentTick()` for each entry in `Current.Game.components`. That call sits in a try block with a catch at lines 29 to 36, so the method returns normally when one component throws.
- `GameComponentUtility.GameComponentTick` has one call site in `Assembly-CSharp.dll`. It is in `Verse.TickManager.DoSingleTick`, at line 464 of `.artifacts/ilspy/fix9-gamecomponent/Verse.TickManager/Verse.TickManager.decompiled.cs`. The call runs at method level, after the `Find.History.HistoryTick()` try block at lines 456 to 463, and outside the loops at lines 364 to 367 and 452 to 455. `DoSingleTick` advances `ticksGameInt` by 1 at line 370, or by 2000 at line 374 when `DebugSettings.fastEcology` is set. The call at line 464 therefore runs once for each call of `DoSingleTick`.
- The IL scan read 16221 types and 90638 method bodies. It looked for `call`, `callvirt`, and `ldftn` instructions that name the method token `0x06000DEB`, and it found one. The scan covers `Assembly-CSharp.dll` only, so other mods' assemblies are not checked. Its output is `.superpowers/sdd/2026-11-01-cross-level-cooling/task-5-fix10-ilscan.txt`, and its script is `.superpowers/sdd/2026-11-01-cross-level-cooling/task-5-fix10-ilscan.fsx`.
- The decompile comes from the installed game, RimWorld 1.7.4995 rev309, as `C:\SteamLibrary\steamapps\common\RimWorld\Version.txt` states. The project target in `CLAUDE.md` is 1.7.4990 rev1069. The line numbers in this section come from the installed build.

## Rimatomics reactor and cooling

Rimatomics lines come from `.artifacts/ilspy/rimatomics-1.7-installed/Rimatomics/`, decompiled from the installed `1.7/Assemblies/Rimatomics.dll`. RimWorld lines come from the installed build, RimWorld 1.7.4995 rev309.

- `CoolingNet` extends `BasePipeNet` at `CoolingNet.cs`, line 7. Its public fields are `CoolingCapacity` at line 9, `CoolingLoopRatio` at line 11, `Coolers` at line 13, and `Turbines` at line 15.
- `reactorCore` extends `Building` at `reactorCore.cs`, line 14. Its public fields are `FuelCount` at line 40, `IsShutdown` at line 50, `postReturnTemp` at line 80, `powerComp` at line 82, `SlotDesignations` at line 104, and `TargetControlRodTo` at line 111. The property `IsCoreMelt` is at line 157. The public methods `Startup`, `InsertFuel`, and `punchit` start at lines 503, 518, and 1024.
- `CoreA` extends `reactorCore` at `CoreA.cs`, line 6. Its `IsBuilt` property at line 12 returns `DubDef.BuildReactorCore.IsFinished`.
- `Turbine` extends `Building` at `Turbine.cs`, line 10. Its public fields are `UncappedPowerGeneration` at line 28 and `UncooledWater` at line 30. The properties `SteamNet` and `CoolingNet` are at lines 36 and 38.
- `CoolingSystem` extends `Building` at `CoolingSystem.cs`, line 8. Its public fields are `powerComp` at line 16 and `Stalled` at line 18. `CoolingTower` extends it at `CoolingTower.cs`, line 8. Its `coolingCapacity` property spans lines 14 to 37.
- `Alert_ReactorAlerts` extends `Alert` at `Alert_ReactorAlerts.cs`, line 11. The NOCOOLING text is added at line 89.
- `CompPipe.pipeNetRef` is a public field at `CompPipe.cs`, line 14. `BasePipeNet.PipedThings` is a public field at `BasePipeNet.cs`, line 14.
- `DubDef.BuildReactorCore` is a public static field at `DubDef.cs`, line 70. `RimatomicsResearch._instance` is a public static field at `RimatomicsResearch.cs`, line 31. `DubUtils.GetResearch` is at `DubUtils.cs`, line 271.
- `RimWorld.CompPowerTrader.PowerOn` is a property over `powerOnInt`, at `RimWorld.CompPowerTrader.cs`, lines 74 to 134. `RimWorld.PowerNet.PowerNetTick` sets it to true at line 240 of `RimWorld.PowerNet.cs`.
- `RimWorld.CompPowerBattery.SetStoredEnergyPct` is a public method at `RimWorld.CompPowerBattery.cs`, line 100. Its stored energy starts at 0, at line 9.
- `RimWorld.PowerConnectionMaker` searches a box of 6 cells around a connector, at lines 103 and 117.
- `Verse.GenSpawn` has no reference to `placeWorkers`, `PlaceWorker`, or `AllowsPlacing`. Its RimWorld decompile is `.artifacts/ilspy/rimworld-1.7-installed-power/Verse.GenSpawn.cs`.

## Jobs and reservations

Checked against RimWorld 1.7.4998 rev1299, from `C:/SteamLibrary/steamapps/common/RimWorld/Assembly-CSharp.dll`. The full decompile is under `.artifacts/decomp-1.7.4998/project/` (gitignored).

- `JobDriver_SocialRelax.TryMakePreToilReservations(bool errorOnFailed)` reserves its chair with `pawn.ReserveSittableOrSpot(cell, pawn.CurJob)` (`RimWorld/JobDriver_SocialRelax.cs`, line 30). The 1.6 install under `C:/Program Files (x86)/Steam/steamapps/common/RimWorld` has the same line. No other `TryMakePreToilReservations` body in the decompile passes `pawn.CurJob` to a reservation.
- `JobDriver_Refuel.TryMakePreToilReservations` reserves with its own `job` (`RimWorld/JobDriver_Refuel.cs`, line 23).
- `Pawn.ReserveSittableOrSpot(this Pawn pawn, IntVec3 exactSittingPos, Job job, bool errorOnFailed = true)` passes `job` to `Pawn.Reserve` (`Verse.AI.ReservationUtility` decompile, lines 44-61).
- `ReservationManager.Reserve` returns false when `job` is null, and logs `<claimant> tried to reserve thing <target> without a valid job` first (`Verse.AI.ReservationManager` decompile, lines 306-310).
- `Pawn_JobTracker.StartJob` sets `curJob` (line 331) before it calls `curDriver.TryMakePreToilReservations` (line 336). `Pawn_JobTracker.TryTakeOrderedJob` runs the pre-check before the job is current (lines 922-956).
- Z-Levels runs `TryTakeFirstJob` from an `EndCurrentJob` postfix only when `pawn.CurJob` is null (`HarmonyPatches/JobPatches.cs`, line 1124). `TryTakeFirstJob` runs the pre-check on the queued job before the job is current (`ZLevelsManager.cs`, line 1236). `ZLogger.Pause` then logs `ERROR` and pauses the game (`ZLogger.cs`, lines 12-16), whatever the `Pause on error` setting is.

## Key bindings and floor locks (MultiFloors ideas, Tasks 5 and 6)

Checked against RimWorld 1.7.4998 rev1299, from `.artifacts/decomp-1.7.4998/project/`.

- `KeyPrefs.Init` resets every binding to its default only when the prefs file is missing (`Verse/KeyPrefs.cs`, lines 32-60). A binding the file already holds keeps its saved value, `None` included.
- `KeyPrefsData.AddMissingDefaultBindings` adds a default only for a `KeyBindingDef` the prefs do not hold (`Verse/KeyPrefsData.cs`, lines 17-24). A changed `defaultKeyCodeA` therefore does not reach a saved `None`. Run `20261106-000524` showed both switch keys `None` after the prefs load, before the rename.
- `KeyPrefsData.ErrorCheck` logs `Key binding conflict` as a warning. It resets a binding only when the bound key differs from the default (`Verse/KeyPrefsData.cs`, lines 113-141). Base `Misc4` (N) and `Misc7` (M) conflict with the floor keys, and the runs log those warnings.
- `KeyBindingDef.defaultKeyCodeA` and `defaultKeyCodeB` are public `KeyCode` fields (`Verse/KeyBindingDef.cs`, lines 11 and 13). `KeyDownEvent` and `MainKey` read the saved prefs, not the default (lines 22-70).
- `Game.CurrentMap`'s setter calls `Find.MapUI.Notify_SwitchedMap()` (`Verse/Game.cs`, lines 118-150). That method calls `designatorManager.Deselect()` (`RimWorld/MapInterface.cs`, line 192), so a map switch clears the selected designator.
- `CameraDriver.rootPos` is a private field (`Verse/CameraDriver.cs`, line 30). `CameraDriver.Update` copies it into `Find.CurrentMap.rememberedCameraPos.rootPos` every frame (lines 429-431). `SetRootPosAndSize` (line 560) sets the field at once.
- `GenFilePaths.ConfigFolderPath` sits under the savedata folder (`Verse/GenFilePaths.cs`, line 178). A validation run passes `-savedatafolder`, so its `Config/KeyPrefs.xml` is its own file.
- `Pawn.Kill(DamageInfo? dinfo, Hediff exactCulprit = null)` is `public override void` (`Verse/Pawn.cs`, line 2096). `Pawn.GetGizmos()` is `public override IEnumerable<Gizmo>` (line 3353). `Pawn.IsColonistPlayerControlled` is a public property (line 778).
- `Command_Toggle` has public `isActive` (`Func<bool>`) and `toggleAction` (`Action`). It takes `defaultLabel`, `defaultDesc` and `icon` from `Command` (`Verse/Command_Toggle.cs`, lines 7-11). `TexCommand.HoldOpen` is a public static `Texture2D` (`RimWorld/TexCommand.cs`, line 15). The stay-on-floor toggle borrows it because no other icon in that file fits.
- `JobGiver_Work.emergency` is a public `bool` field. `TryIssueJobPackage(Pawn, JobIssueParams)` is `public override ThinkResult` (`RimWorld/JobGiver_Work.cs`, lines 8-10 and 49). The humanoid think tree holds an emergency node and a normal one. The emergency node offers only prioritized work, so a validation that calls the first node finds no haul.
- `Building.SpawnSetup` and `Building.DeSpawn` are patched in `HarmonyPatches/MapPatches.cs` (lines 146-170) to set `ZLevelsManager.recheckStairs`. The stairs registry refills on the next `GameComponentTick` (`ZLevelsManager.cs`, lines 30-40). A synchronous check runs no tick, so it must call `GameComponentTick()` once. Run `20261106-001053` showed `registered 0` for a lower stairs-up that existed.
- `ZLevelsManager.SpawnStairsBelow(Pawn, IntVec3, Map)` creates the lower `ZL_StairsUp` at the same cell, when a `ZL_StairsDown` stands at that cell on a higher map. It is public.
- Scribe fills a `LookMode.Reference` list only during `ResolvingCrossRefs`. A local list in `ExposeData` is null at `PostLoadInit`, so the lists must live in fields until then. Run `20261106-020356` logged `LoadingVars: no pawn list`, `ResolvingCrossRefs: 1 pawns listed` and `PostLoadInit: no pawn list`, and the lock was lost. Run `20261106-020512` kept it.
