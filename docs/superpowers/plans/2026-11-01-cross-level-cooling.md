# Cross-level cooling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Rimatomics reactor on one level is cooled by coolers on another level. A cooling column on each level joins the two cooling loops.

**Architecture:** An optional assembly, `ZLevelsRimatomics.dll`, holds the column comp, the pairing rule, and one Harmony prefix on `Rimatomics.CoolingNet.Tick`. The prefix rebuilds each net's cooler and turbine lists from the paired nets. Rimatomics then computes the ratio as it does today. The column is a Rimatomics coolant pipe, defined by an XML patch that applies only when Dubs Rimatomics is active.

**Tech Stack:** C# on `net10.0`, as in `ZLevels.csproj`. Krafs.Rimworld.Ref 1.7.4990-unstable and Krafs.Publicizer 2.3.2. Harmony from workshop item 2009463077. Rimatomics.dll as a build reference only, from workshop item 1127530465. RimWorld 1.7.4990 rev1069 XML patches. `tools/Invoke-RuntimeValidation.ps1`. Pillow for the texture.

**Spec:** `docs/superpowers/specs/2026-11-01-cross-level-cooling-design.md`. ADRs 0001 to 0004 and `GLOSSARY.md` apply.

## Global Constraints

- Target RimWorld 1.7.4990 rev1069 on the Steam unstable branch. Not 1.6.
- Keep the namespace `ZLevels`, the package id `ZLevels1.1`, and the subsystem boundaries. The optional code uses the namespace `ZLevelsRimatomics`. Inside a `ZLevels.Rimatomics` namespace, the name `Rimatomics` would resolve to our own namespace.
- Leave `ZLevels/1.1`, `ZLevels/1.2`, and `ZLevels/1.3` unchanged. 1.7 loads `ZLevels/Common` and `ZLevels/1.7` through `ZLevels/LoadFolders.xml`.
- Ship no game DLLs and no second Harmony. `Rimatomics.dll` is a build reference only and is never copied into the mod.
- Keep `HarmonyPatches/JobPatches - Copy.cs` out of the build.
- `RimatomicsBridge` must load without Rimatomics, so it names no Rimatomics type. RimWorld loads every class in the optional assembly, so no class in it derives from, implements, or holds a value-type field of a Rimatomics type. Method bodies, method signatures, reference-type fields, and patch attributes may name Rimatomics types. This keeps the assembly loadable without Rimatomics, as ADR-0002 requires.
- Validate only in isolated savedata and a new colony. Never open existing saves or change the user's mod list. `-ModsConfigSource` is read only.
- No Workshop publishing. Claim no compatibility beyond what a run shows.
- Commit with `git commit -F <msgfile> -- <paths>`. Stay on `master`. Do not push. End every commit message with `Co-Authored-By: Claude Haiku 5.5 <noreply@anthropic.com>`.

## Review Focus

1. Rimatomics absent: the run loads with no error naming `ZLevelsRimatomics`. Pinned by Task 2, step 4.
2. Save and reload keep the loop. Pinned by Task 5, step 1.
3. Removing a column splits the loop, and the lower turbine stops counting the surface tower. Pinned by Task 5, step 1.
4. A cooling column joins a loop only from the same cell on an adjacent level. A column one cell away or three cells away does not join. Pinned by Task 4, step 1.
5. Dubs Rimatomics and Rimatomics++ active together. Pinned by Task 7, step 2.

## Verified facts

Read from the installed code. Dubs Rimatomics was decompiled with `ilspycmd` into `.artifacts/ilspy/rimatomics/`. Use these names and no others.

- `CoolingNet : BasePipeNet`. Public fields `Coolers` (`List<CoolingSystem>`), `Turbines` (`List<Turbine>`), `CoolingCapacity`, and `CoolingLoopRatio`. `InitNet()` rebuilds both lists from `PipedThings`, then calls `Tick()`. `Tick()` sets `CoolingLoopRatio` to 0 when `Coolers` is empty, so a ratio at or below 1 does not prove a link (`CoolingNet.cs` lines 27-28). With coolers present and `CoolingCapacity` above 1, the ratio is the turbines' summed `UncappedPowerGeneration` over `CoolingCapacity` (lines 30-35). `CoolingCapacity` is set only while `Coolers` is non-empty and keeps its last value otherwise, so it cannot prove a link either.
- `BasePipeNet.PipedThings` is the list of pipe things in a net.
- `CompPipe` has the public field `pipeNetRef`. Its property `net` calls `MapComp.RegenGrids()` when the field is null, so hot paths read `pipeNetRef`.
- `UniversalPipeMapComp.RebuildPipeGrid` creates new `CoolingNet` objects. Never keep a net between ticks. `Harmony_GridRegen` calls `RegenGrids()` on every `Map.MapUpdate`, so a new pipe joins its net within a frame.
- The coolant pipe def `coolingPipe` has `ParentName` `RimatomicsPipeBase` and the comp `Rimatomics.CompProperties_Pipe` with `<mode>Cooling</mode>`.
- `RimatomicsPipeBase` (`ThingDefs_Buildings/Buildings_A_Pipes.xml`) sets thingClass `Rimatomics.Building_Pipe`, `drawerType` MapMeshOnly, `altitudeLayer` Conduits, `isEdifice` false, `passability` Standable, and `costList` Steel 3. `designationCategory` Rimatomics comes from `BasedPower`.
- `Building_Pipe.Print` is empty. The pipe look comes from `PrintForGrid`, which draws the Rimatomics linked pipe art. A def's `graphicData` is drawn only in real time.
- Coolers: `CoolingTower : CoolingSystem` (def `CoolingTower`). Turbines: `Thor : Turbine` (def `Turbine`) and `Zeus : Turbine` (def `BigTurbine`).
- Reactor: def `ReactorCoreA` (`Rimatomics.CoreA : reactorCore`). `reactorCore` has the public members `Startup()`, `IsShutdown`, `FuelCount`, `postReturnTemp`, `Leakage`, and `CrackedFuel`. `IsCoreMelt` is true above a post-return temperature of 2000, `IsOverheating` above 500, and `IsCold` below 50. Fuel def: `FuelRods`. Control console: `ReactorControl`. Meltdown leftover: `PoppedReactorCoreA` (`Building_PoppedCore`).
- Alerts: `Alert_ReactorAlerts` shows NOCOOLING when a turbine's `CoolingNet.Coolers` is empty and its core is not cold. `Alert_CoolingAlerts` lists coolers under half capacity.
- Removal: `Designator_RemovePipe`, with label key `DesignatorRemoveCooling`.
- Radiation hediff: `DubDef.RimatomicsRadiation`.
- Rimatomics++ (`NachoToast.RimatomicsPlusPlus`) contains no name reference to `CoolingNet`, `CompPipe`, `Building_Pipe`, `CoolingSystem`, `CoolingTower`, or `InitNet`. This was checked against the `.dll` bytes.
- Package ids: `dubwise.rimatomics` is Dubs Rimatomics, workshop item 1127530465. `nachotoast.rimatomicsplusplus` is Rimatomics++, workshop item 3774907235. Both are in `C:\SteamLibrary\steamapps\workshop\content\294100`. A second copy of Dubs Rimatomics sits in `C:\Program Files (x86)\Steam\steamapps\workshop\content\294100`.
- Z-Levels: `ZUtils.ZTracker` is a static `ZLevelsManager`. Its `GetLowerLevel(PlanetTile, Map)` and `GetUpperLevel(PlanetTile, Map)` return a `Map` or null. The power pairing rule is in `PowerTransfer/ConnectedPowerNets.cs`, lines 134 and 146. The other transmitter must sit on the adjacent map, and `Position.DistanceTo(...) < 3` must hold. So the spec's "within 3 cells" means a distance below 3.
- The power column is `ZL_PowerTransmitterColumn` in `ZLevels/Common/Defs/Buildings/PowerTransferingColumn.xml`. It has `costStuffCount` 60 with `stuffCategories` Metallic, `WorkToBuild` 750, `MaxHitPoints` 160, `holdsRoof` true, `passability` PassThroughOnly, and `pathCost` 0.
- RimWorld should load every DLL in `1.7/Assemblies/`, even when Rimatomics is off. Task 2 tests this.

## File Structure

- Create `ZLevels/Source/ZLevels/ZLevelsRimatomics/ZLevelsRimatomics.csproj`. Its output is `../../../1.7/Assemblies/`, like the main project. It repeats the `Krafs.Rimworld.Ref` and `Krafs.Publicizer` references with `PrivateAssets="all"`, because those do not flow from `ZLevels.csproj`. It sets the Harmony and Rimatomics `HintPath`s.
- Create `ZLevels/Source/ZLevels/ZLevelsRimatomics/RimatomicsBridge.cs`: the activation check.
- Create `ZLevels/Source/ZLevels/ZLevelsRimatomics/CompProperties_CoolingColumn.cs` and `CompCoolingColumn.cs`.
- Create `ZLevels/Source/ZLevels/ZLevelsRimatomics/CoolingLoopRegistry.cs`: pairing and loops.
- Create `ZLevels/Source/ZLevels/ZLevelsRimatomics/CoolingColumnHighlight.cs`: the partner highlight (Task 4b).
- Create `ZLevels/Source/ZLevels/ZLevelsRimatomics/Patch_CoolingNet.cs`: the one Tick prefix.
- Create `ZLevels/1.7/Patches/CoolingColumn.xml`: the column def, inside `PatchOperationFindMod`.
- Create `tools/art/make_cooling_column_art.py` and the texture it writes. Task 3, step 1 picks the texture path.
- Modify `tools/RuntimeValidation/RuntimeValidation.cs`, `tools/RuntimeValidation/RuntimeValidation.csproj`, and `tools/Invoke-RuntimeValidation.ps1`.
- Modify `docs/validation-report.md` and `docs/roadmap.md`.

## Task 0: Commit the design documents

**Files:** `GLOSSARY.md`; `docs/adr/0001-cooling-columns-pair-like-power-columns.md` through `docs/adr/0004-radiation-and-meltdown-stay-on-their-level.md`; `docs/superpowers/specs/2026-11-01-cross-level-cooling-design.md`; `docs/superpowers/plans/2026-11-01-cross-level-cooling.md`.

- [ ] **Step 1: Check the tree.** Run `git status`. The files above are untracked. `ZLevels/1.7/Assemblies/ZLevels.dll` is already modified. This commit does not include it.
- [ ] **Step 2: Commit.** Message: "Add the cross-level cooling spec, ADRs, and glossary". Paths: the files above.
- [ ] **Step 3: Verify.** `git status` shows none of those paths as changed.

## Task 1: Failing validation scenario

**Files:** modify `tools/RuntimeValidation/RuntimeValidation.cs` and `tools/Invoke-RuntimeValidation.ps1`.

**Interfaces:**
- Consumes: the stage pattern in `GameComponentUpdate`, `Record(label, result)`, and `GenCommandLine.CommandLineArgPassed("zlevelscooling")`. The stairs scenario reads its switch the same way.
- Produces these labels, each recorded as `PASS` or `FAIL: <reason>`: `cooling column defined`, `cooling column costs`, `cooling column holds roof`, `cooling columns paired`, `column out of range stays unpaired`, `column one cell away stays unpaired`, `coolant pipe joins the column`, `lower turbine shares the surface tower's loop`, `cooling link breaks when a column is removed`, `cooling link survives reload`, `rimatomics bridge active` (Task 2), the reactor checks (Task 6), and the final `cooling link`. `cooling link` is `PASS` only when every earlier label is `PASS`.

- [ ] **Step 1: Write `RunCoolingLinkTick`.** Run it after the stairs stage. Each check that needs the column records `FAIL: column missing` until the column exists.
- [ ] **Step 2: Wire the switches in the runner.** Add `[switch]$Cooling`, `[switch]$CoolingNoColumns`, and `[switch]$RimatomicsPlusPlus` to the `param` block. Add `if ($Cooling) { $arguments += '-zlevelscooling' }` and the matching line for `CoolingNoColumns` with `-zlevelscoolingnocolumns`. After the `$activeMods` block (line 83), put `dubwise.rimatomics` before `zlevels1.1` when `$Cooling` or `$CoolingNoColumns` is set, unless the list already holds it. Add `nachotoast.rimatomicsplusplus` when `$RimatomicsPlusPlus` is set, under the same rule. In the `$requiredPass` chain (line 157), add `elseif ($Cooling -or $CoolingNoColumns) { 'cooling link' }` before the `else`.
- [ ] **Step 3: Run it red.** Run `tools\Invoke-RuntimeValidation.ps1 -Cooling -TimeoutSeconds 900`. Expected: `cooling column defined | FAIL`, then the throw `Runtime scenarios did not all pass`. If `Player.log` shows `multiple times: dubwise.rimatomics`, the second Dubs copy is loading. Record that. Do not widen the filter on line 181 without a reason.
- [ ] **Step 4: Commit.** Message: "Add a cooling link validation scenario that fails before the feature".

## Task 2: Optional assembly and the no-Rimatomics load

**Files:** create `ZLevelsRimatomics.csproj` and `RimatomicsBridge.cs`. Modify `RuntimeValidation.csproj` and `RuntimeValidation.cs`.

**Interfaces:**
- Produces `ZLevelsRimatomics.RimatomicsBridge.Active`, a static bool. A `[StaticConstructorOnStartup]` static constructor sets it: `Active = ModsConfig.IsActive("dubwise.rimatomics") || ModsConfig.IsActive("nachotoast.rimatomicsplusplus")`. When `Active` is true, the constructor runs `new Harmony("zlevels.rimatomics").PatchAll(typeof(RimatomicsBridge).Assembly)`.
- Consumes `ZLevels.csproj` through a `ProjectReference` with `Private="false"`. It consumes `0Harmony` from `$(HarmonyAssembly)` and `Rimatomics.dll` from `$(RimatomicsAssembly)`. That property defaults to `$(RimWorldDirectory)/../../workshop/content/294100/1127530465/1.7/Assemblies/Rimatomics.dll`, the same pattern as line 14 of `ZLevels.csproj`.

- [ ] **Step 1: Add the check.** `rimatomics bridge active` passes when `Type.GetType("ZLevelsRimatomics.RimatomicsBridge, ZLevelsRimatomics")` exists and its `Active` is true. Use reflection, so the harness compiles before the assembly exists.
- [ ] **Step 2: Run it red.** Run `-Cooling`. Expected: `rimatomics bridge active | FAIL`.
- [ ] **Step 3: Implement.** Create the csproj and `RimatomicsBridge.cs`. Add a `ProjectReference` to `ZLevelsRimatomics.csproj` in the harness csproj with `Private="false"`. The harness csproj already references `ZLevels.csproj` this way, and the runner builds only the harness project, so the optional assembly builds too.
- [ ] **Step 4: Run green, then prove the no-Rimatomics case.** Run `-Cooling`. Expected: `rimatomics bridge active | PASS`. Then run the core validation with no switch. Expected: `core validation | PASS`, and no `Player.log` error entry that names `ZLevelsRimatomics`. If an entry names it, move the DLL out of `1.7/Assemblies/` into `1.7/Optional/`. Have the main mod load it with `Assembly.LoadFrom` from its constructor, and only when Rimatomics is active. Then rerun both runs. Record which option was used.
- [ ] **Step 5: Commit.** Build the optional DLL with the harness build. The build command in the project `CLAUDE.md` builds only the main project. Include the csproj, `RimatomicsBridge.cs`, the built `ZLevels/1.7/Assemblies/ZLevelsRimatomics.dll`, and the harness files. Do not stage `ZLevels/1.7/Assemblies/ZLevels.dll`. Message: "Add the optional Rimatomics assembly".

## Task 3: Column def, texture, and look

**Files:** create `ZLevels/1.7/Patches/CoolingColumn.xml`, `tools/art/make_cooling_column_art.py`, and a texture under the mod's `Textures/` folder. Modify `RuntimeValidation.cs`.

**Interfaces:**
- Produces the ThingDef `ZL_CoolingColumn`. It sits inside a `PatchOperationFindMod` that names `dubwise.rimatomics`, and it is added with `PatchOperationAdd` to `/Defs`.
- It has `ParentName="RimatomicsPipeBase"`, and it inherits thingClass `Rimatomics.Building_Pipe` and designationCategory `Rimatomics`.
- It overrides these fields:
  - `label`: cooling column.
  - `drawerType`: MapMeshAndRealTime. The parent's MapMeshOnly draws nothing for a `Building_Pipe`.
  - `altitudeLayer`: Building.
  - `building/isEdifice`: true.
  - `holdsRoof`: true.
  - `costStuffCount`: 60, with `stuffCategories` Metallic.
  - `costList`: no Steel. The parent's Steel 3 must not apply.
  - `WorkToBuild`: 750. `MaxHitPoints`: 160.
  - `passability`: PassThroughOnly, and `pathCost`: 0. These match the power column.
  - `comps`: `Rimatomics.CompProperties_Pipe` with `<mode>Cooling</mode>`, copied from `coolingPipe`, and `ZLevelsRimatomics.CompProperties_CoolingColumn`.
- Consumes `coolingPipe` as the model for the comp and the linked graphic.

- [ ] **Step 1: Pick the texture path and add the checks.** Glob the mod for its existing `Textures` folders, and choose the matching path. Add three checks. `cooling column defined`: the def exists. `cooling column costs`: no Steel in `CostList`, `CostStuffCount` is 60, and `WorkToBuild` is 750. `cooling column holds roof`: `holdsRoof` and `building.isEdifice` are both true.
- [ ] **Step 2: Run it red.** Run `-Cooling`. Expected: the three checks FAIL.
- [ ] **Step 3: Draw the texture.** `make_cooling_column_art.py` uses Pillow to write the texture: a vertical coolant pipe with a red valve wheel on the front face. The wheel is part of the picture, so no code draws it. The script also writes a preview to `.artifacts/art/`. Run `explorer .artifacts\art` and tell Lucas where the preview is. Continue. He can redirect.
- [ ] **Step 4: Write the def and the patch.** Run `-Cooling`. Expected: the three checks PASS. If the def is missing while Rimatomics is active, the `PatchOperationFindMod` match is wrong. Try the mod name `Dubs Rimatomics` instead of the package id. If `cooling column costs` still lists Steel, the `costList` override did not replace the parent's list. Set the items explicitly.
- [ ] **Step 5: Check the look.** Run `-Cooling -Visual`. Expected: the texture shows on the column cell, and the linked pipe art does not hide it. Record the screenshot path. If the art hides the texture, stop and report the two options to Lucas: a custom print path, or the linked art with no wheel. Do not choose between them silently.
- [ ] **Step 6: Commit.** Message: "Add the cooling column def and its texture".

## Task 4: Pairing and the column comp

**Files:** create `CompProperties_CoolingColumn.cs`, `CompCoolingColumn.cs`, and `CoolingLoopRegistry.cs`. Modify `RuntimeValidation.cs`.

**Interfaces:**
- Produces `ZLevelsRimatomics.CompProperties_CoolingColumn : CompProperties`, with `compClass` `CompCoolingColumn`.
- Produces `ZLevelsRimatomics.CompCoolingColumn : ThingComp`. It has the public field `CompPipe pipe`, set from `parent.GetComp<CompPipe>()` in `PostSpawnSetup(bool respawningAfterLoad)`. `PostSpawnSetup` calls `CoolingLoopRegistry.Register(this)` only when `pipe` is not null. A null pipe logs an error and skips the registration. `PostDeSpawn(Map map, DestroyMode mode)` calls `CoolingLoopRegistry.Deregister(this)` and then the base method.
- Produces `CoolingLoopRegistry` (static). `Register` adds the column. It first drops entries whose column is no longer spawned, whose map is disposed, or whose game is not the current one. It logs an error and adds nothing when the column has no map or no current game. `Deregister` removes the column. `bool Paired(CompCoolingColumn a, CompCoolingColumn b)` returns false for a column that is not usable, and throws when the game has no `ZLevelsManager`. `List<CompCoolingColumn> LoopOf(CompCoolingColumn start)` returns the connected group of paired columns, found by a breadth-first search at call time. It is empty when `start` is not usable.
- The pairing predicate for cooling is `Paired(a, b)`. `b` sits on the map that `ZUtils.ZTracker.GetLowerLevel(a.parent.Tile, a.parent.Map)` or `GetUpperLevel(...)` returns, and `b.parent.Position` equals `a.parent.Position` (same cell). `Paired(a, b)` and `Paired(b, a)` must agree. `ConnectedPowerNets.RegisterTransmitter` keeps its wider rule for power, and cooling does not reuse it (ADR-0005).
- Pairing runs at query time, not at register time. A column loaded before its neighbor's map exists still pairs.
- A column is usable when it is spawned, stands on a map that is not disposed, is in that map's thing grid at its own cell, and is registered for the current game. `bool IsRegistered(CompCoolingColumn column)` reports whether the column is listed and usable. The negative checks FAIL unless both columns are listed and usable.
- `ZUtils.TeleportThing` moves a column between maps with no spawn or despawn hook, so the grid check is what tells a moved column apart. A column moved away and back keeps its entry, and pairs again once it is back. A registered column that fails the grid check logs once.

- [ ] **Step 1: Change the checks and the layout.** `cooling columns paired`: a surface column and the lower column on the same cell share `LoopOf`. `column one cell away stays unpaired`: a second surface column one cell east of the lower column is not in its loop. `column out of range stays unpaired`: a third surface column three cells east of the lower column is not in its loop. `coolant pipe joins the column`: a `coolingPipe` placed next to the column has the same `pipeNetRef` as the column, checked after one `MapUpdate`. `column three cells away stays off the pipe net`: a third column three cells east of the column shares no pipe net with it. The check FAILs when it does. It is a label, not only information. `column moved to another level and back stays paired`: a surface column moved to the lower map and back, with one more cooling column registered in between, still pairs with the lower column. While it stands on the lower map, `IsRegistered` must report false. The check calls `ZUtils.TeleportThing` by reflection, because ZUtils is internal.
- [ ] **Step 2: Run it red.** Run `-Cooling` on the current code, which still pairs at one cell. Expected: `column one cell away stays unpaired` FAILs. Record the run folder.
- [ ] **Step 3: Implement the same-cell rule** in `CoolingLoopRegistry.Paired`. Do not change `ConnectedPowerNets.cs`.
- [ ] **Step 4: Run it green for these checks.** Run `-Cooling`. Expected: the six checks PASS. The final `cooling link` line still shows FAIL.
- [ ] **Step 5: Commit.** Message: "Pair cooling columns on the same cell only".

## Task 4b: Partner highlight on the adjacent level

**Files:** create `ZLevels/Source/ZLevels/ZLevelsRimatomics/CoolingColumnHighlight.cs`. Modify `RuntimeValidation.cs`.

**Interfaces:**
- Produces `CoolingLoopRegistry.HighlightTargets(Map viewed)`, which returns `List<CoolingTarget>`. `CoolingTarget` is a struct with `IntVec3 Cell` and `bool Paired`. A target is each cell where a usable cooling column stands on the map directly above or below `viewed`. `Paired` is true when `viewed` also holds a usable cooling column on that cell.
- Produces a draw hook that outlines each target on the map being viewed: green when `Paired` is true, amber when it is false. It draws nothing on any other map.
- Pick the hook and the outline call from the 1.7 decompile, for example `GenDraw.DrawFieldEdges`. Record each confirmed name in `docs/game-api-findings.md`.
- Add both labels from Step 1 to the Task 1 label list and to `CoolingLabels`, so `cooling link` waits for them.

- [ ] **Step 1: Add the checks.** `partner highlight marks the cell`: with a surface column and no lower column, `HighlightTargets(lower)` holds the surface column's cell with `Paired` false. `partner highlight shows the pair`: after the lower column stands on that cell, the same target holds `Paired` true.
- [ ] **Step 2: Run it red.** Run `-Cooling`. Expected: both labels FAIL with `highlight not built yet`.
- [ ] **Step 3: Implement** `HighlightTargets` and the draw hook.
- [ ] **Step 4: Run it green, then look.** Run `-Cooling`. Expected: both labels PASS. Run `-Cooling -Visual`, which adds a look that views the lower level and takes a screenshot. Expected: an amber outline on the empty cell, then green once the lower column stands there. Record the screenshot paths. The controller reads them.
- [ ] **Step 5: Commit.** Message: "Highlight the partner cell on the adjacent level".

## Task 5: Merge the loops into the Rimatomics nets

**Files:** create `Patch_CoolingNet.cs`. Modify `RuntimeValidation.cs`.

**Interfaces:**
- Produces a Harmony prefix on `Rimatomics.CoolingNet.Tick`. The patch class carries `[HarmonyPatch]`, a `static MethodBase TargetMethod()` that returns `AccessTools.Method("Rimatomics.CoolingNet:Tick")`, and `static void Prefix(Rimatomics.CoolingNet __instance)`.
- The prefix finds the columns whose `pipe.pipeNetRef` is `__instance`. If there are none, it returns and leaves Rimatomics' own lists alone.
- Otherwise it takes the union of `CoolingLoopRegistry.LoopOf` over those columns, collects the distinct non-null `pipeNetRef` nets, and sets `__instance.Coolers` to the `CoolingSystem` things and `__instance.Turbines` to the `Turbine` things found in their `PipedThings`. It creates new lists on every tick.
- The prefix reads `PipedThings`, not the other nets' `Coolers`, because a stale list can still hold a column that was removed. `PipedThings` is current.
- One prefix covers every path. `CoolingNet.InitNet()` ends by calling `Tick()`, and `RebuildPipeGrid` calls `InitNet()` on each new net. The lists are rebuilt each tick, so a removed column leaves nothing behind after one tick.
- Rimatomics computes `CoolingLoopRatio` from these lists. This task changes membership only.
- The prefix catches any exception from `CoolingLoopRegistry`, so a throw cannot reach the game tick and skip the pipe nets after it. The net then falls back to its own `PipedThings`, so no stale list survives, and the original `Tick` runs. It logs each distinct failure once per game, keyed by map, the failing site, exception type, message and IL offset. A line the log cannot take is held, and the first game tick after the log can write it flushes the held lines. That flush runs from a Harmony postfix on `GameComponentUtility.GameComponentTick`, so no saved component is written. Each key book caps at 64 keys, and a count line reports dropped lines. (Amended 2026-11-03 through round 13, closed at commit 8b291c3: the first version used a static flag, which silenced every failure after the first. The rest of the logging contract is in `task-5-report.txt`.) The prefix fetches the `ZLevelsManager` once per tick, not once per pair. `LoopOf` returns an empty list when its start column is not usable. The prefix checks that the start column is in the loop it gets back. When it is not, the prefix logs once and treats the loop as unknown. `LoopOf` costs grow fast with the entry count. `Paired` and `LoopOf` take the usable set for the tick as a parameter, so one tick does not repeat the grid check for each pair.

- [ ] **Step 1: Add the checks.** `lower turbine shares the surface tower's loop`: the lower turbine's `CoolingNet.Coolers` holds the surface `CoolingTower`. `cooling link breaks when a column is removed`: remove the surface column on the lower column's cell. Within two ticks, the tower must be gone from the lower turbine's `Coolers`, and the lower net's `Turbines` must still hold its own turbine. `cooling link survives reload`: the same membership check, after the save and reload step the core validation already uses for its level and cell check.
- [ ] **Step 2: Run it red.** Run `-Cooling`. Expected: the three checks FAIL.
- [ ] **Step 3: Implement** `Patch_CoolingNet.cs`.
- [ ] **Step 4: Run it green.** Run `-Cooling`. Expected: the three checks PASS. The final `cooling link` line still shows FAIL until Task 6.
- [ ] **Step 5: Commit.** Message: "Share cooling loops across paired columns".

## Task 6: Reactor cooled across levels

**Files:** modify `RuntimeValidation.cs` and `docs/validation-report.md`, for the research notes.

**Interfaces:** these checks, each recorded as `PASS` or `FAIL`:
- `reactor runs on the lower level`: after `Startup()`, `IsShutdown` is false and `FuelCount` is above 0.
- `loop ratio at or below 1`: `CoolingLoopRatio` on the lower turbine's net is 1 or less in every sample. Sample every 250 ticks for 2500 ticks, which is one in-game hour. The plan sets these two numbers. Change them only with a recorded reason.
- `surface tower stays in the lower loop`: the surface `CoolingTower` is in the lower turbine's `CoolingNet.Coolers` in every sample. Added 2026-11-03. Reason: `CoolingLoopRatio` is 0 when `Coolers` is empty (`CoolingNet.cs` lines 27-28), so `loop ratio at or below 1` passes with no link, and decision 13 requires the scenario to fail without the column pair. `no cooling alert` also fails without the pair, but it counts any cooler, so this check names the surface tower.
- `no cooling alert`: the lower turbine's `CoolingNet.Coolers` holds at least one cooler in every sample. This is the NOCOOLING condition in `Alert_ReactorAlerts`.
- `no meltdown`: `IsCoreMelt` is false in every sample.
- `surface colonist not irradiated`: a pawn that stands on the surface cell above the core has no `DubDef.RimatomicsRadiation` hediff at the end. This checks ADR-0004.

The switch `-CoolingNoColumns`, added in Task 1, runs the same scenario without the column pair. It is the red run. Until this task builds it, the switch records the pairing labels and `cooling link` as FAIL with `no-columns variant not built yet`, and places no columns (Task 4, fix round 3).

- [ ] **Step 1: Research gate.** Find out what `Startup()` needs: power, fuel loaded with `InsertFuel`, and a `ReactorControl`. The fuel def is `FuelRods`. Find out whether one `CoolingTower` covers the lower turbine's `UncappedPowerGeneration`. If it does not, the scenario adds towers until the starting ratio is 1 or less, and records the count. Write the findings in `docs/validation-report.md`. If the reactor cannot be started in the harness, stop and report the blocker. Do not mark this task done.
- [ ] **Step 2: Add the checks and the scenario wiring.**
- [ ] **Step 3: Run it red.** Run `-Cooling -CoolingNoColumns`. Expected: the runner throws `Runtime scenarios did not all pass`, and the results show `surface tower stays in the lower loop` and `no cooling alert` as FAIL. Record the run folder.
- [ ] **Step 4: Run it green.** Run `-Cooling`. Expected: every reactor check PASS, and `cooling link | PASS`. Record the run folder.
- [ ] **Step 5: Commit.** Message: "Check a reactor cooled across levels".

## Task 7: Regression and compatibility

- [ ] **Step 1: Core run without Rimatomics.** Run with no switch. Expected: `core validation | PASS`.
- [ ] **Step 2: Both Rimatomics variants.** Run `-Cooling` for Dubs Rimatomics alone, then `-Cooling -RimatomicsPlusPlus`. Expected: `cooling link | PASS` in both. If Rimatomics++ breaks a check, record the error. Do not claim the variant works.
- [ ] **Step 3: The user's mod list.** Run the core validation with `-ModsConfigSource` pointing at the user's `ModsConfig.xml`, under `%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Config\`. Use the same source as the other-mods run recorded in `docs/validation-report.md`. The file is read only. Expected: `Runtime validation passed with other mods`.
- [ ] **Step 4: Commit the evidence** in `docs/validation-report.md`.

## Task 8: Documentation and roadmap

- [ ] **Step 1: Roadmap.** In `docs/roadmap.md`, under "Later", the "Tubes between levels" item says that cooling is done, with its run IDs. Other resources stay in Later. Leave "Decisions for Lucas" as it is.
- [ ] **Step 2: Validation report.** Add a "Cross-level cooling" section. It holds the red and green runs, the research notes from Task 6, and the untested cases. The untested cases are water flow (not simulated, by decision), the Remove Cooling designator (not run), the look result from Task 3, the partner highlight look from Task 4b, and any Rimatomics++ failure.
- [ ] **Step 3: Commit.** Message: "Record the cross-level cooling runs".
