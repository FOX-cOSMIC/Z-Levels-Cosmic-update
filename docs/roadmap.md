# Roadmap

Updated 2026-10-31. Order matters: finish a stage before starting the next. Port plan boxes refer to `docs/superpowers/plans/2026-10-27-zlevels-17.md`.

## Now

- **Level differences (answered 2026-10-29).** See `docs/levels-and-events.md`.
- **Vault v1, a pocket dimension. Done 2026-10-31.** Acceptance a to h pass in isolated saves; the run folders are in `docs/validation-report.md`. Open: the test places the storage zone itself, so the check with the real zone tool is still to do (see Test gaps). Design in `docs/superpowers/specs/2026-10-29-vault-pocket-dimension.md`. Lucas decided on 2026-10-29 that items live inside the vault, the entrance goes on the surface only, and one pocket is shared. On 2026-10-30 Lucas confirmed the vault is not a numbered level, and only one vault level exists. On 2026-10-31 Lucas decided that a colonist inside the vault still counts as wealth.
- **Colonist autonomy (requirement 3). Hauling works. Building and mining by free colonists are not tested.** Port plan box 74 stays open until they are. Free colonists haul across levels in runs `20261031-193842` to `194425`, and a colonist other than the validation colonist takes the haul in `194231`, `194328`, and `194425`. Most earlier stage 20 failures came from the harness (sealed room, walled zone, rest, wrong colonist). Run `202312` is still unexplained. The 124 starts and ends at once (run `20261028-145345`) came from an older code state. Plan: `docs/superpowers/plans/2026-10-31-colonist-autonomy.md`. Vault v2 waits on this.
- **Reachability patch bugs.** `CanReach_Patch.Prefix` in `HarmonyPatches/ReachAndReservationPatches.cs` threw `NullReferenceException` at line 85 in run `193352`. A throw can leave a pawn on the other map. A haul to an unreachable cell is offered again about every second (run `192636`). The zone there was walled off by rock, which the harness did not check. Whether the mod's reachability patch causes the retry is not shown. Neither bug is fixed. Details: `docs/validation-report.md`, Open issues.

## Next

- **Smooth transitions (requirement 1).** Define "smooth" first. Today a colonist walks to the stairs and is then teleported on arrival. Port plan box 75.
- **Infestation insects.** The letter says the bugs tunnel up. The code places them on the level below, next to the natural hole, and nothing moves them up. Decide whether they should come up, then build it or change the letter.
- **Vault v2, hauling out and cross-level storage.** Depends on autonomy (requirement 3).
- **Test gaps.** Each needs a scenario, a check, or an audit.
  - The storage zone made with the real zone tool. You allowed mouse and keyboard control for testing, so this can be driven in the game.
  - Hauling, power, and wood after a save and reload. The reload checks only the level and cell.
  - Every Harmony signature. Names are checked, and only the points in `docs/game-api-findings.md` have their signatures checked. Port plan box 56.
  - Walking past the lower stairs cell and digging out. Port plan box 69.
  - A repeat of the refuel pause from run `032903`. It was seen once and has not come back.

## Decisions for Lucas

- Should the lower level's start open onto a cave? The lower stairs open onto sealed rock today, and the code does not say (validation report, Open issues).
- Should spilled logs stay on their level? In run `165858`, a blocker haul moved 10 logs to the surface.
- Should the rebuilt `ZLevels/1.7/Assemblies/ZLevels.dll` be committed? The committed copy is from `0222951`. The rebuild has the same size and different bytes. The regression runs loaded the rebuild.

## Later

- **Power generators inside the vault**, and power carried from the vault to a main level.
- **Tubes between levels** (RimAtomics or similar), so structures on one level can use resources or power from another.
- **Other mods.** Check which mod features touch levels, pocket maps or portals before building for them.

## Not goals now

- Multiplayer (optional, unverified).
- Existing saves. They are not opened.
