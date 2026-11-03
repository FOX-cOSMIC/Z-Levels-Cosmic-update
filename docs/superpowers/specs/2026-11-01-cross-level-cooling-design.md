# Cross-level cooling design

**Status:** approved 2026-11-01 in a grilling session (rounds 1 to 5). Amended 2026-11-02: decision 1 is now the same-cell rule for cooling, decision 2 is reworded, and decision 14 adds the partner highlight. Amended 2026-11-03: decision 2 now says that a column that is not usable forms no loop, so only its own loop drops, and the other loops of its net still join. Terms are in `GLOSSARY.md`. Decisions are in `docs/adr/0001` to `0005`.

## Goal

A Rimatomics reactor on one level can be cooled by coolers on another level. Cooling reaches the other level through a pair of cooling columns, one on each level.

## Decisions

1. **Mechanism.** Each level gets a cooling column. A cooling column pairs only with the cooling column on the same cell of the level directly above or below it (ADR-0005). Power transmitter columns keep the rule of less than 3 cells apart (ADR-0001).
2. **Chains.** Paired columns form one loop across the levels they connect. Build order does not matter. A column that is not usable forms no loop, so only its own loop drops, and the other loops of its net still join.
3. **What joins.** The whole local cooling network on each side joins the loop. Rimatomics computes the cooling ratio over the joined coolers and turbines. The mod does not change the math.
4. **Like a pipe.** A cooling column is a Rimatomics coolant pipe. It connects to adjacent coolant pipes on its level and has no capacity limit. The link moves capacity, not water (ADR-0003).
5. **Coolers stay local.** Each cooler keeps its own requirements, such as surface water for water coolers and no roof for towers.
6. **Breaking.** Destroying or deconstructing a column splits the loop at once.
7. **Build rules.** Same as the Z-Levels power column: 60 metal units, 750 work, holds a roof, no research.
8. **Look.** A vertical coolant pipe with a red wheel on the front face. The wheel is decoration only. Connections use Rimatomics' linked pipe overlay. The look is static.
9. **Removal.** The Rimatomics "Remove Cooling" designator removes a column.
10. **Build tab.** Preferred: the Rimatomics build tab (`designationCategory` Rimatomics). If that is not practical, the Z-Levels power category is the fallback.
11. **Scope.** Radiation, fallout, and meltdown stay on their level (ADR-0004).
12. **Dependency.** Optional. The cooling module loads only when Rimatomics is active. It targets Dubs Rimatomics and Rimatomics++ (ADR-0002).
13. **Validation.** A scenario with Rimatomics in the isolated mod list. A lower-level reactor and turbine with no local coolers are joined to surface coolers. It fails without the column pair and passes with it. Pass criteria: the turbine's cooling loop ratio stays at or below 1 for a set time, with no cooling alert and no meltdown.
14. **Partner highlight.** When the player views a level, each cell that matches a cooling column on an adjacent level is outlined. The outline is green when a cooling column stands on that cell, and amber when the cell is empty. It shows only on the level being viewed, because a line cannot be drawn across two levels on screen.

## Out of scope

- Water flow through the column.
- Radiation, fallout, or meltdown crossing levels.
- Valve behavior for the red wheel.
- Color changes on the column that show loop state. The outline in decision 14 is the only color change.
