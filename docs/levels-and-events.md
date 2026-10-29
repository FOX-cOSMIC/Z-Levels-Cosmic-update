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
- The mod patches it only on surface maps with caves below (`AnimalPatches.cs:82-124`). It opens a natural hole on the surface at a food spot and records the infestation. The insects appear on the level below, near that spot.
- Nothing moves the insects up. The vanilla letter says they "tunnel towards you". In run `20261028-135155` none of the 90 lower animals could reach the stairs.

## Incidents on other levels

- Every map is an incident target: `Storyteller.AllIncidentTargets` lists all maps.
- The threat points for a lower level come from the colonists' gear on that level. The surface uses the wealth of all levels (`Map.PlayerWealthForStoryteller`).
- Only infestations are patched. Raids and other incidents on lower levels were not tested in game.
