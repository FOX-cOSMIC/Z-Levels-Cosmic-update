# Vault: a pocket dimension (design)

Status: decisions answered on 2026-10-29 and 2026-10-30. No code written yet. The v1 plan is next.
Date: 2026-10-29

## Decisions from Lucas

- Items live inside the vault. Lucas said the vault is "also another level where things can be built and such".
- The entrance goes on the surface only. It cannot be placed on another level.
- All vault buildings share one pocket. Lucas chose this over one pocket per building.
- Colonists can enter the vault and build inside it.
- Automatic hauling into the vault stays. Lucas reversed an earlier "no automatic hauling".
- The vault is a pocket dimension, outside the level stack. The number of levels is unlimited, so the vault does not count as one. Lucas confirmed on 2026-10-30 that the vault is not a numbered level, and that only one vault level exists.
- Power generators in the vault, and tubes (RimAtomics or similar) between levels, are not goals now. They are on `docs/roadmap.md`.

## Answered question

- Is the vault a numbered level? No. Lucas answered on 2026-10-30 that the vault is not a numbered level and that only one vault level exists. The vault stays outside the level stack, with no level index, no stairs, and its own pocket.

## What the game already provides

Checked in the installed 1.7 `Assembly-CSharp.dll` and the decompiles in `.artifacts/ilspy/`:

- `Verse.PocketMapUtility.GeneratePocketMap(size, generatorDef, extraGenStepDefs, sourceMap)` makes a pocket map. `DestroyPocketMap(map)` removes one.
- `RimWorld.MapPortal : Building` is an entrance. It has `PocketMap`, `exit`, `leftToLoad`, `containerProxy`, `IsEnterable`, `GetOtherMap`, `GetDestinationLocation` and `OnEntered`.
- Each portal makes its own pocket map by default. `MapPortal.SpawnSetup` calls `GeneratePocketMap` when `PocketMap` is null, through `GeneratePocketMapInt`. A shared pocket needs v1 to change that step.
- `RimWorld.PocketMapExit : MapPortal` is the way back out, placed inside the pocket.
- `RimWorld.MapPortalProperties` is the portal's def data: `pocketMapGenerator`, `exitDef`, `pocketMapSize` (default 100), and the entry letter.
- Colonists enter with `JobGiver_EnterPortal` and `JobDriver_EnterPortal`.
- Automatic hauling into a portal exists: `WorkGiver_HaulToPortal` and `JobDriver_HaulToPortal`, a `JobDriver_HaulToContainer`. Items do not go into a container. `PortalContainerProxy.TryAdd` drops them on the other map, at the exit.
- `Verse.PocketMapProperties` sets `destroyOnParentMapAbandoned` (default true), `preventPrisonerEscape` and `canBeCleaned`.
- `StorytellerUtility.DefaultThreatPointsNow` maps a pocket map to its source map for threat points.

## Proposed design

1. **Entrance.** A building def of class `MapPortal` in the Structure tab. Placeable on the surface only. The check that refuses other levels is part of v1. Only a subclass if the engine's class needs one.
2. **Interior.** One pocket for all vault buildings. The first entrance creates it, and every later entrance uses it. Each entrance gets its own exit inside the pocket, because `PocketMapExit` links only to the portal that is being generated (`PocketMapUtility.currentlyGeneratingPortal`).
3. **Entering.** Colonists use the engine's enter job. They leave through the exit of the entrance they used.
4. **Building inside.** Construction on the pocket map is the normal game's construction. No level code runs.
5. **Storage and automatic hauling.** The player opens the entrance's enter dialog (its gizmo) and puts items on the load list. Colonists haul them in with `WorkGiver_HaulToPortal`. Each item is dropped on the pocket map next to the exit (`PortalContainerProxy.TryAdd`). Storage is a stockpile zone inside the pocket.
6. **Wealth.** Items on the pocket map are on a map the mod does not register, so its wealth patches skip them. Vanilla surface wealth should skip them too. This must be tested, not assumed.
7. **Level registry.** The pocket is not passed to `ZLevelsManager.TryRegisterMap`. The stairs, level index and tile lists never see it.
8. **Infestations.** No infestation starts on a pocket map. The infestation patch refuses pocket maps (`IsPocketMap`), and it refuses levels five and deeper (`ZUtils.InfestationAllowedAt`).

## Acceptance for v1

Each item is a runtime validation scenario, in a new colony with isolated savedata:

- a. The validator builds a vault entrance on the surface. A colonist does not build it.
- b. A second entrance on the surface enters the same pocket as the first. The validator tries to place an entrance on a lower level, and the placement is refused.
- c. A colonist enters the pocket, a construction job builds a wall inside, and the colonist exits.
- d. Seven wood logs are put on the vault's load list. Colonists haul them in with no forced job. Record the counts on the source map and on the pocket map before and after.
- e. Record the surface's wealth before and after the haul. The seven logs must leave the total once they are on the pocket map.
- f. Save and reload with a colonist inside the pocket and items on its floor. The pocket exists, the colonist is on the pocket map at the saved cell, the wall is present, and the items are present on the pocket map.
- g. An infestation incident that targets the pocket map records no infestation and places no hole. Use the style of the depth guard, `RunInfestationGuard` in `tools/RuntimeValidation/RuntimeValidation.cs`.
- h. Record the run folder and its `validation-results.txt`.

## Not in v1

- Power generators inside the vault, and power through the portal.
- Tubes between levels (RimAtomics or other mods).
- Raids and other threats on pocket maps. Record as untested unless a run shows one.
- Existing saves. They stay unopened, as the project rules require.

## Risks

- The mod's code assumes levels. `MapComponentZLevel` and `ZUtils` lookups may return null on a pocket map. Test before trusting them.
- `destroyOnParentMapAbandoned` defaults to true. If the source level is abandoned, the pocket and its contents can be destroyed. Set the default in the def.
- One shared pocket means every entrance depends on it. Losing the pocket loses every vault item.
- Overriding the engine's per-portal pocket creation is new code on an engine path. Check it against the 1.7 code before relying on it.
- Vanilla portals target caravans and gravships. Item flow into a stationary building is unproven.

## Open for the v1 plan

- Whether the pocket starts with a stockpile zone, or the player places one. Recommended: the player places one.
- Whether one vault entrance or several is the v1 scope. Acceptance b assumes several. Recommended: several, since each costs one exit.
