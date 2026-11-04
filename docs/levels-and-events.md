# How the levels differ

Checked against the code and the installed 1.7 game files on 2026-10-29. Not measured in game unless stated.

| | Surface (0) | Lower levels (-1, -2 ...) | Upper levels (+1 ...) |
|---|---|---|---|
| Sunlight | Normal | Permanent no-sunlight condition (`ZLevelsManager.cs:1607-1610`) | Normal |
| Wild animals | Surface biome | Only when the surface tile has caves below (`AnimalPatches.cs:34-40`). List: Megascarab 1.0, Megaspider 0.3, Spelopede 0.15. Density 3.7 (`Common/Defs/BiomeDef/UndergroundBiome.xml`) | None placed at generation: density 0 (`UpperBiome.xml`) |
| Caves below | Rolled once per map: 50% on SmallHills, LargeHills, Mountainous or Impassable tiles, never on other tiles (`MapComponentZLevel.cs:24-31, 60-66`) | Caves and rock come from the underground generator | Not applicable |
| Infestation ("bugs") | Vanilla incident, patched (see below) | Insects are placed when the level is generated, within 100 cells of the infestation spot (`GenStep_Animals.cs:33-46`) | None |
| Rock | Normal | Rock chunks and caves from `Underground.xml` | Floor copies the rock layout of the level below (`AdjustUpperMapGeneration`) |

## Infestations ("bugs")

- The vanilla incident has base chance 2.7 and at most one every 20 days (`minRefireDays`), in `Core/Defs/Storyteller/Incidents_Map_Threats.xml`. The storyteller picks among all incidents by weight, so the real rate depends on the storyteller and threat level. Not measured.
- The mod patches it on any map with caves below (`AnimalPatches.cs`, `Patch_Infestation_TryExecuteWorker`). It opens a natural hole at a food spot on that map and records the infestation. Insects are placed on lower levels near that spot when those levels are generated.
- Nothing moves the insects up. The vanilla letter says they "tunnel towards you". In run `20261028-135155` none of the 90 lower animals could reach the stairs.
- No infestation starts on level five or deeper, or on a pocket map. `ZUtils.InfestationAllowedAt` and the `IsPocketMap` check stop the patch, and `GenStep_Animals.cs` skips directed placement on those levels. Validated by run `20261030-215956`. The pocket-map case is validated by `-VaultInfestation`: runs `20261031-150455` and `20261031-150625` pass, and run `20261031-150543` fails with the check removed.

## Incidents on other levels

- Every map is an incident target: `Storyteller.AllIncidentTargets` lists all maps.
- The threat points for a lower level come from the colonists' gear on that level. The surface uses the wealth of all levels (`Map.PlayerWealthForStoryteller`), and the vault pocket is left out of that wealth (see the pocket section).
- Only infestations are patched. Raids and other incidents on lower levels were not tested in game.

## The vault pocket map

- One pocket is shared by every vault entrance. It is not a numbered level: `ZLevelsManager.GetZIndexFor` returns 0 for it, and it is not in `ZLevelsTracker`. The cross-level job search never includes it, so `TryIssueJobPackagePatch` returns to the vanilla search on a pocket map.
- Terrain: the pocket is solid rock with the underground's ore lumps, and no caves. `GenStep_VaultRock` fills it, and reads the rock types from the surface tile that the entrance stands on, since a pocket's own tile is not a world tile. A room of radius 10 at the centre is open concrete. The exit stands at the centre, and later exits take open room cells. Colonists mine out through the rock. Run `20261104-134919` (`-VaultRock`) passes, and the red run `20261104-134847` fails on the open cells, the exit position, and the missing ore.
- `MapComponentZLevel` does not roll caves or read the world grid on a pocket map.
- No infestation starts on the pocket (the `IsPocketMap` check in the infestation patch). Run `20261031-150543` shows the case fails without the check.
- Wealth: vanilla `WealthWatcher.ForceRecount` adds a pocket's wealth to its source map. `ForceRecountVaultPocketPatch` subtracts the vault pocket's items, buildings, and floors, so logs and walls inside the vault leave the surface total. Pawns stay counted. Runs `20261031-145150` (red) and `20261031-145522` (green).
- Load list: colonists haul items in with vanilla `WorkGiver_HaulToPortal`. Items land in the pocket's storage zone, which the test places beside the exit. Runs `20261031-144723`, `144819`, `144908`, and `144946`.
- Save and reload keep the pocket, the entrance's link to it, the colonist's cell, the wall, and the items on the floor. Runs `20261031-150003`, `150102`, and `150149`.
