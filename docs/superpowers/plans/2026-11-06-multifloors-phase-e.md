# MultiFloors Phase E Implementation Plan

> **For agentic workers:** Execute inline in this session with superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax for tracking. Parent plan: `docs/superpowers/plans/2026-11-05-multifloors-ideas.md`, Tasks 36 to 39.

**Goal:** Build the three large ideas that Lucas answered on 2026-11-06: more ways up (D3), floor pipes (D4), and the stair transfer (D5). Each change is proven by a validation scenario whose named rows FAIL before the change and PASS after it.

**Architecture:** Ways up become kinds in one table, `WayUpKinds`. Each kind has an up piece and a down piece. A large kind is a single 2 by 2 building, and its partner covers the same four cells on the other floor. A floor pipe is one building that places its other end on the floor above. The source end moves a fixed number of one chosen item def each game day, and the receiving floor's normal haul logic stores the items. The stair transfer is a window opened from each stairs piece. It sends the checked colonists with the Call colonists here route, and it marks the checked items with the haul-to-floor mark.

**Tech Stack:** C# on the RimWorld 1.7.4998 API, Verse XML defs, the runner `tools/Invoke-RuntimeValidation.ps1`, and the harness `tools/RuntimeValidation/` (`RuntimeValidation.cs` dispatch plus one scenario file per task).

**Spec:** `docs/mod-comparison/ideas.html` rows "More ways up" (line 80), "Pipes between floors" (line 81) and "Stair transfer tab" (line 75). Scope: `docs/superpowers/specs/2026-10-27-zlevels-17-design.md`. Decisions: `docs/roadmap.md`, "Decisions for Lucas".

## Global constraints

- The parent plan's Global Constraints apply unchanged: target 1.7.4998, namespace `ZLevels`, no game DLLs shipped, multiplayer unverified, isolated saves only, commit only own paths, never push, never force-push.
- A gameplay change is proven only by a run that prints `Runtime validation passed. Evidence: <run folder>`.
- Red rows reach missing names through `DefDatabase<ThingDef>.GetNamedSilentFail` or reflection, so the harness builds before the feature exists. A missing name is a FAIL, never a build error.
- Row labels must not contain the word "error" in uppercase. The runner's ERROR gate is case-sensitive.

## Decisions

**Answered 2026-11-06** (asked with the question tool; the recommended option was taken in each case):

- D3 (Task 36): ways up are ladders and stair sizes. Powered elevators and the freight elevator are not built.
- D4 (Task 37): one chosen resource per pipe, at a fixed rate.
- D5 (Task 38): the transfer sends a chosen list now. The standing "keep a set amount on the next floor" option is not built.

**Decisions I made** under "implement all of them". Each reverses in the place named.

- DM14 Sizes (Task 36). Small is one cell. Large is a 2 by 2 footprint in one building. Its origin is the bottom-left cell for rotation North (`GenAdj.OccupiedRect`, decompile `Verse/GenAdj.cs:595`), and its partner covers the same four cells on the other floor. Reason: "stairs of different sizes" needs a footprint, and a partner on the same origin keeps the pairing by position. Reverse: delete the large defs in `WayUps.xml`.
- DM15 Costs and strength (Task 36). Small ladder: 5 stuff, 400 work, 150 hit points. Large ladder: 20 stuff, 1600 work, 600 hit points. Large stairs: 40 stuff, 4000 work, 1500 hit points. The existing stairs stay at 10 stuff, 1000 work, 375 hit points. Reverse: the numbers in `WayUps.xml`.
- DM16 Placement of large ways (Task 36). All four cells must lie in the room the placement uses, which must have at least 25 cells, and the four cells on the other floor must be standable and empty of buildings. Small ways keep their current checks, so existing placements do not change. Reason: a partner that cannot be placed leaves a one-sided way. Reverse: the large branch in `PlaceWorker_Stairs`.
- DM17 Floor pipe shape (Task 37). A pipe is placed on the lower floor of a pair. It places its other end on the floor above, as stairs do. Each end stores whether it is the source. A "flip direction" gizmo swaps the two. Removing one end removes the other, unlike stairs, because a pipe is one object across two floors. Reverse: the flip gizmo, the rule in `PlaceWorker_FloorPipe` that refuses a pipe with no floor above, and the partner removal in `Destroy`.
- DM18 Pipe rate and form (Task 37). The pipe moves items, not fluid. It moves 60 items of its chosen def per game day, from the source to the receiving end, in 250 tick steps. Moved items appear next to the receiving end, and the normal haul logic stores them. The rate lives in `FloorPipeExtension.itemsPerDay`. Reverse: the step in `Building_FloorPipe.Step`. Cost if wrong: the item skips the haul walk, so no colonist carries it across.
- DM19 Resource choice (Task 37). The choices are Steel first, then the raw resources with a stack limit above 1. Reverse: `Building_FloorPipe.ResourceChoices`.
- DM20 Transfer form (Task 38). The transfer is a window opened by a button on each stairs piece, not a main tab. Checked colonists walk to the standable cell next to the other end, with the Call colonists here route. Checked items use the haul-to-floor mark, so an item the other floor does not need is counted and not sent. Reason: the background scan drops a mark that the floor does not need, so a forced mark would not stick without a change to `ItemFloorMarks`. Reverse: `StairTransferWindow.Send`, and a forced-mark flag in `ItemFloorMarks` if Lucas wants unneeded items sent.

## Task 36: More ways up (D3)

**Files:**
- Create: `ZLevels/Common/Defs/Buildings/WayUps.xml`. Six ThingDefs: `ZL_SmallLadderUp`, `ZL_SmallLadderDown`, `ZL_LargeLadderUp`, `ZL_LargeLadderDown`, `ZL_LargeStairsUp`, `ZL_LargeStairsDown`. Up pieces use `Building_StairsUp`, down pieces `Building_StairsDown`. Each uses `PlaceWorker_Stairs`. The large kinds use `<size>(2,2)</size>` and `drawSize` (6,6). The ladder kinds reuse the ladder textures `Building/Ladder` and `Building/LadderUp`.
- Create: `ZLevels/Source/ZLevels/ZLevels/Buildings/WayUpKinds.cs`.
- Modify: `ZLevels/Source/ZLevels/ZLevels/ZLevelsDefOf.cs` (six ThingDef fields).
- Modify: `ZLevels/Source/ZLevels/ZLevels/Buildings/Building_StairsUp.cs` (`SpawnSetup` places the partner through `WayUpKinds.SpawnPartner`, and its existing-partner check uses `PartnerOf(def)`).
- Modify: `ZLevels/Source/ZLevels/ZLevels/Buildings/Building_StairsDown.cs` (the partner in the `FC_StairsDown` branch uses `PartnerOf(def)`. The branch condition does not change.)
- Modify: `ZLevels/Source/ZLevels/ZLevels/ZLevelsManager.cs` (`SpawnStairsUpper` and `SpawnStairsBelow` place the partner through `WayUpKinds.SpawnPartner(stairs, targetMap)` at `stairs.Position`, not at the cell the pawn stands on).
- Modify: `ZLevels/Source/ZLevels/ZLevels/Buildings/PlaceWorker_Stairs.cs` (kind rules by `WayUpKinds.IsUp` and `IsDown`, plus the large checks of DM16).
- Create: `tools/RuntimeValidation/WayUpsScenarios.cs`.
- Modify: `tools/RuntimeValidation/RuntimeValidation.cs` (one dispatch block for `zlevelswayups`, placed before the `zlevelsthiefcarry` anchor) and `tools/Invoke-RuntimeValidation.ps1` (switch `WayUps`, scenario map, argument line, and `requiredPass` entry `'way ups'`).
- Modify: `docs/validation-report.md` (section "## More ways up").

**Interfaces:**
- Produces `WayUpKinds.PartnerOf(ThingDef def)`: the other half of the kind (up to down, down to up), or null for any other def.
- Produces `WayUpKinds.IsUp(ThingDef)`, `WayUpKinds.IsDown(ThingDef)`, `WayUpKinds.IsLarge(ThingDef)` (true when `size.x > 1 || size.z > 1`).
- Produces `WayUpKinds.SpawnPartner(Building_Stairs source, Map partnerMap)`: returns the partner already at `source.Position` if one exists, or else places a new partner with the source's footprint, rotation and stuff, and sets the outside terrain on its cells. Returns null when placement fails. It never throws.
- Produces `WayUpKinds.PartnerCellsOpen(ThingDef def, IntVec3 origin, Rot4 rot, Map partnerMap)`: true when each cell of the footprint is standable and holds no building on `partnerMap`. Used only for large kinds.
- Consumes `ZUtils.ZTracker.GetUpperLevel(Tile, Map)` and `GetLowerLevel(Tile, Map)`, and `ZUtils.ZTracker.GetZIndexFor(Map)`.

**Red rows (`-WayUps`).** Each row FAILs before the change, on a missing def, class or behavior:

1. `small ladder places a pair of ways`: `ZL_SmallLadderUp` placed on a free lower cell creates a `ZL_SmallLadderDown` on the floor above at the same cell.
2. `large ladder covers four cells on both floors`: a `ZL_LargeLadderUp` at an open origin makes a partner whose footprint covers the same four cells on the floor above.
3. `large stairs place a pair`: the same check for `ZL_LargeStairsUp`.
4. `colonist climbs a large ladder`: a free colonist on the lower floor is called to a standable cell on the upper floor, and stands there within 6000 ticks.
5. `blocked cell above refuses a large way`: `GenConstruct.CanPlaceBlueprintAt(ZL_LargeLadderUp, ...)` returns false when one cell on the floor above holds a wall.
6. Aggregate `way ups`.

**Steps:**

- [ ] **Step 1: Red.** Write rows 1 to 6, wire the switch and dispatch, run `tools/Invoke-RuntimeValidation.ps1 -WayUps` in the background, and confirm rows 1 to 6 FAIL. Record the run folder in the ledger.
- [ ] **Step 2: Implement.** Add `WayUps.xml`, `WayUpKinds.cs`, the six DefOf fields, and the call-site changes listed under Files. Keep the small kinds' behavior identical to today.
- [ ] **Step 3: Green.** Run `-WayUps`. Every row PASSes, and the run prints the proof line.
- [ ] **Step 4: Regression.** Run the core validation and every scenario that uses stairs: `-StayOnFloor`, `-StairsDamage`, `-OneFloorColony`, `-Cooling`, `-CoolingTwoNets`, `-StairsEqualize`, `-CallColonists`, `-HaulToFloor`, `-BackgroundHaul`, `-WorkOnFloorOrder`, `-BenchLink`. Each one PASSes. Record the run folders.
- [ ] **Step 5: Commit.** Stage only the Task 36 paths. Stage the two shared harness files with the anchored method (HEAD text plus the Task 36 edits only). The commit message has the red and green run folders and the trailer `Co-Authored-By: Claude Haiku 5.5 <noreply@anthropic.com>`.

## Task 37: Floor pipes (D4)

**Files:**
- Create: `ZLevels/Common/Defs/Buildings/FloorPipe.xml`. One ThingDef `ZL_FloorPipe`, `thingClass` `ZLevels.Building_FloorPipe`, `tickerType` `Rare`, `passability` `PassThroughOnly`, a 1 by 1 footprint, and a `modExtensions` entry for `ZLevels.FloorPipeExtension` with `itemsPerDay` 60.
- Create: `ZLevels/Source/ZLevels/ZLevels/Buildings/Building_FloorPipe.cs`. Holds the link to the partner, the chosen resource, the source flag and the carry value, the gizmos, and the save data.
- Create: `ZLevels/Source/ZLevels/ZLevels/Buildings/FloorPipeExtension.cs` (`DefModExtension` with `public int itemsPerDay = 60`).
- Create: `ZLevels/Source/ZLevels/ZLevels/Buildings/FloorPipeMover.cs` (`MoveItems`, described below).
- Create: `ZLevels/Source/ZLevels/ZLevels/Buildings/PlaceWorker_FloorPipe.cs`. Refuses a pipe with no floor above it, or a cell above that is not open.
- Modify: `ZLevels/Source/ZLevels/ZLevels/ZLevelsDefOf.cs` (`ZL_FloorPipe` field).
- Create: `tools/RuntimeValidation/FloorPipeScenarios.cs`, with its dispatch block and the runner switch `FloorPipe` (requiredPass `'floor pipe'`).
- Modify: `docs/validation-report.md` (section "## Floor pipes between floors").

**Interfaces:**
- `Building_FloorPipe`: `ThingDef Resource` (saved by defName), `bool IsSource` (saved), `float carry` (saved). `Building_FloorPipe Partner()` returns the pipe on the other floor at `Position`, or null. `SpawnSetup` (not after a load) makes this end the source, with `Resource` set to Steel, and creates the partner with `IsSource` false. `Destroy` also destroys the partner.
- `public void SetResource(ThingDef def)` sets `Resource` on both ends. The gizmo "Resource: <label>" calls it through a float menu over `ResourceChoices`.
- `public void FlipDirection()` swaps `IsSource` on both ends. The gizmo "Flip direction" calls it.
- `public void Step(int ticks)`: when this end is the source and has a partner and a resource, adds `itemsPerDay * ticks / GenDate.TicksPerDay` to `carry` and moves `(int)carry` items with `FloorPipeMover.MoveItems`. `TickRare` calls `Step(GenTicks.TickRareInterval)`.
- `public static int FloorPipeMover.MoveItems(ThingDef def, int count, Map from, Map to, IntVec3 near)`: moves up to `count` spawned, unforbidden, unreserved items of `def` from `from` to `to`. It splits a stack when needed, and places each item next to `near` with `ThingPlaceMode.Near`. Returns the number moved.
- `public static List<ThingDef> Building_FloorPipe.ResourceChoices`: Steel first, then the raw resources with a stack limit above 1.

**Red rows (`-FloorPipe`).** Each row FAILs before the change:

1. `floor pipe places its other end above`: `ZL_FloorPipe` placed on a lower cell creates a pipe on the floor above at the same cell.
2. `steel moves up at 60 a day`: 60 steel on the lower floor, a source pipe with Steel. `Step(250)` runs 240 times (one game day). Afterwards 60 steel are on the upper floor and none on the lower.
3. `only the chosen resource moves`: 60 steel and 60 wood on the lower floor, a pipe with Steel. After one day the wood is still all there.
4. `flip direction sends the other way`: after `FlipDirection`, 30 steel on the upper floor move down within one day.
5. `removing one end removes the other`: destroying the lower end leaves no pipe on the upper floor.
6. `resource gizmo sets both ends`: `SetResource(Steel)` on one end makes the partner's `Resource` Steel too.
7. Aggregate `floor pipe`.

**Steps:** the same five steps as Task 36, with the switch `-FloorPipe`, the red and green runs, and the regression set: the core validation, `-StairsDamage` (it destroys a stairs piece, so it covers the partner logic), `-StairsEqualize`, `-OneFloorColony`, and `-Cooling`.

## Task 38: Stair transfer (D5)

**Files:**
- Create: `ZLevels/Source/ZLevels/ZLevels/UI/StairTransferWindow.cs`. A `Window` opened from the stairs piece. It lists the free colonists and the loose items on the stairs' floor, with a checkbox for each, and a "Send checked" button.
- Modify: `ZLevels/Source/ZLevels/ZLevels/HarmonyPatches/StairsGizmoPatch.cs`. `AddViewButton` also adds "Transfer across the stairs" (a `Command_Action` that opens the window). Building_Stairs keeps calling `AddViewButton`.
- Modify: `ZLevels/Source/ZLevels/ZLevels/Designators/Designator_HaulToFloor.cs` (`IsLooseItem` becomes `internal static`, so the window applies the same rule).
- Create: `tools/RuntimeValidation/StairTransferScenarios.cs`, with its dispatch block and the runner switch `StairTransfer` (requiredPass `'stair transfer'`).
- Modify: `docs/validation-report.md` (section "## Stair transfer").

**Interfaces:**
- `public static void StairTransferWindow.Open(Building_Stairs stairs)`.
- `public static List<Pawn> StairTransferWindow.ColonistsOn(Map floor)`: free colonists spawned on the floor.
- `public static List<Thing> StairTransferWindow.ItemsOn(Map floor)`: loose items on the floor, by the `IsLooseItem` rule.
- `public static IntVec3 StairTransferWindow.LandingCell(Building_Stairs other)`: the nearest standable cell next to `other.Position`, or the position itself when none exists. Deterministic.
- `public static TransferResult StairTransferWindow.Send(Building_Stairs stairs, IEnumerable<Pawn> colonists, IEnumerable<Thing> items)`. Colonists go through `CallColonistsTargeter.CallTo(colonists, LandingCell(other), other.Map)`. Items go through `Designator_HaulToFloor.MarkItemsForFloor(items, otherFloorIndex)`. `TransferResult` holds `ColonistsSent`, `ItemsMarked` and `ItemsNotNeeded`.

**Red rows (`-StairTransfer`).** Each row FAILs before the change:

1. `transfer button is on the stairs`: the gizmo list of a stairs piece has a command labelled "Transfer across the stairs".
2. `window lists the colonists and items on the floor`: `ColonistsOn` contains the scenario colonist, and `ItemsOn` contains the scenario steel.
3. `send walks the checked colonist to the other floor`: `Send` with one colonist makes it reach the other floor and stand on the landing cell within 6000 ticks.
4. `send marks the checked item for the other floor`: the item carries the haul-to-floor mark for the other floor. The scenario sets up the other floor so it needs steel, the way the `-HaulToFloor` scenario does.
5. `unchecked colonist and item stay where they are`: a second colonist and a second item that were not sent are still on the lower floor.
6. Aggregate `stair transfer`.

**Steps:** the same five steps as Task 36, with the switch `-StairTransfer`, and the regression set: the core validation, `-CallColonists`, `-HaulToFloor`, `-BackgroundHaul`, `-StairsDamage`, `-StairsEqualize`, and `-FloorsGuide`.

## Task 39: Final regression, records and report

Follows the parent plan's Task 39 steps. Specific to Phase E:

- [ ] Run the core validation and every scenario in the runner's `$scenarioSwitches` map, each in the background. Record each run folder. Read every log, not only the exit code: the background wrapper reports exit 0 whatever the result.
- [ ] Update `docs/roadmap.md`: the three ideas built, the decisions answered (D3 to D5, with dates) and the decisions made (DM14 to DM20). Keep D6 and D7 open.
- [ ] Update `docs/game-api-findings.md` with every name these tasks confirm, and `docs/validation-report.md` with each section's runs.
- [ ] Self-review of the whole branch diff against this plan and the ideas page. The subagent tool is not used for review unless Lucas asks, so the review is a self-review, and the final report says so.
- [ ] Final report: what changed, the run folder for each proof, what is untested, and the open decisions D6 and D7.

## Self-review of this plan

Checked on 2026-11-06 before any step ran. The re-read fixed the source flag and default resource of a pipe, the removal of a pipe's partner, and the rotation note in DM14:

1. Coverage: each ideas row in the three ranges has a task (36 for line 80, 37 for line 81, 38 for line 75).
2. Names: `WayUpKinds.PartnerOf`, `SpawnPartner`, `PartnerCellsOpen`, `FloorPipeMover.MoveItems`, `Building_FloorPipe.Step`, `StairTransferWindow.Send` and `LandingCell` are each defined once and used with the same signature in later steps. `CallColonistsTargeter.CallTo` and `Designator_HaulToFloor.MarkItemsForFloor` already exist.
3. Red rows reach missing names through `DefDatabase` or reflection, so the harness builds before any feature exists.
4. Each decision DM14 to DM20 names what it changes and where to reverse it.
