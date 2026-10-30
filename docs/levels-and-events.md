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
- No infestation starts on level five or deeper, or on a pocket map. `ZUtils.InfestationAllowedAt` and the `IsPocketMap` check stop the patch, and `GenStep_Animals.cs` skips directed placement on those levels. Validated by run `20261030-215956`. The pocket-map case is untested until the vault exists.

## Incidents on other levels

- Every map is an incident target: `Storyteller.AllIncidentTargets` lists all maps.
- The threat points for a lower level come from the colonists' gear on that level. The surface uses the wealth of all levels (`Map.PlayerWealthForStoryteller`).
- Only infestations are patched. Raids and other incidents on lower levels were not tested in game.
