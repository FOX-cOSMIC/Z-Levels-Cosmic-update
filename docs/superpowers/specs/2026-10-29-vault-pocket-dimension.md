# Vault: a pocket dimension (design)

Status: draft for Lucas's approval. No code written yet.
Date: 2026-10-29

## Decisions from Lucas

- The vault is a special, built building. It is not a level.
- It is a pocket dimension, outside the level stack. The number of levels is unlimited, so the vault does not count as one.
- Colonists can enter the vault and build inside it.
- Automatic hauling into the vault stays. (Lucas reversed an earlier "no automatic hauling".)
- Power generators in the vault, and tubes (RimAtomics or similar) between levels, are not goals now. They are on `docs/roadmap.md`.

## What the game already provides

Checked in the installed 1.7 `Assembly-CSharp.dll`:

- `Verse.PocketMapUtility.GeneratePocketMap(size, generatorDef, extraGenStepDefs, sourceMap)` makes a pocket map. `DestroyPocketMap(map)` removes one.
- `RimWorld.MapPortal : Building` is an entrance. It has `PocketMap`, `exit`, `leftToLoad`, `containerProxy`, `IsEnterable`, `GetOtherMap`, `GetDestinationLocation` and `OnEntered`.
- `RimWorld.PocketMapExit : MapPortal` is the way back out, placed inside the pocket.
- `RimWorld.MapPortalProperties` is the portal's def data: `pocketMapGenerator`, `exitDef`, `pocketMapSize` (default 100), and the entry letter.
- Colonists enter with `JobGiver_EnterPortal` and `JobDriver_EnterPortal`.
- Automatic hauling into a portal exists: `WorkGiver_HaulToPortal` and `JobDriver_HaulToPortal`, a `JobDriver_HaulToContainer`. Items go into the portal's container.
- `Verse.PocketMapProperties` sets `destroyOnParentMapAbandoned` (default true), `preventPrisonerEscape` and `canBeCleaned`.
- `StorytellerUtility.DefaultThreatPointsNow` maps a pocket map to its source map for threat points.

## Proposed design

1. **Entrance.** A building def of class `MapPortal` in the Structure tab, placeable on any level. Only a subclass if the engine's class needs one.
2. **Interior.** One pocket per vault building (recommended). It is made with `PocketMapUtility.GeneratePocketMap` and a new generator def: flat floor, no rock, default size.
3. **Entering.** Colonists use the engine's enter job. They leave through the pocket's exit.
4. **Building inside.** Construction on the pocket map is the normal game's construction. No level code runs.
5. **Storage and automatic hauling.** The player puts items on the portal's load list (through its gizmo; to verify). Colonists haul them in with `WorkGiver_HaulToPortal`. Items sit in the portal's container, not on a map.
6. **Wealth.** Items in the portal's container are on no map, so they should not count. This must be tested, not assumed.
7. **Level registry.** The pocket is not passed to `ZLevelsManager.TryRegisterMap`. The stairs, level index and tile lists never see it.

## Open decisions for Lucas

1. **Storage.** Items inside the vault (the portal's container, recommended), or a storage building on a level? The second needs a wealth exclusion patch on those cells.
2. **Entrance level.** Any level (recommended), or the surface only?
3. **Pockets.** One pocket per vault building (recommended), or one shared pocket?

## Acceptance for v1

Each item is a runtime validation scenario, in a new colony with isolated savedata:

- a. The validator builds a vault entrance on the surface. A colonist does not build it.
- b. A colonist enters the pocket, a construction job builds a wall inside, and the colonist exits.
- c. Seven wood logs are put on the vault's load list. Colonists haul them in with no forced job. Record the counts on the source map before and after.
- d. Record `WealthTotal` before and after the haul. Items held by the vault must not count.
- e. Save and reload with a colonist inside the pocket and items held. The pocket exists, the colonist is on the pocket map at the saved cell, the wall is present, and the held items are present.
- f. Record the run folder and its `validation-results.txt`.

## Not in v1

- Power generators inside the vault, and power through the portal.
- Tubes between levels (RimAtomics or other mods).
- Raids and other threats on pocket maps. Record as untested unless a run shows one.
- Existing saves. They stay unopened, as the project rules require.

## Risks

- The mod's code assumes levels. `MapComponentZLevel` and `ZUtils` lookups may return null on a pocket map. Test before trusting them.
- `destroyOnParentMapAbandoned` defaults to true. If the source level is abandoned, the pocket and its contents can be destroyed. Set the default in the def.
- Vanilla portals target caravans and gravships. Item flow into a stationary building is unproven.
