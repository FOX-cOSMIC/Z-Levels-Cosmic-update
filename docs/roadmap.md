# Roadmap

Updated 2026-11-03. Order matters: finish a stage before starting the next. Port plan boxes refer to `docs/superpowers/plans/2026-10-27-zlevels-17.md`.

## Now

- **Level differences (answered 2026-10-29).** See `docs/levels-and-events.md`.
- **Vault v1, a pocket dimension. Done 2026-10-31.** Acceptance a to h pass in isolated saves; the run folders are in `docs/validation-report.md`. Open: the test places the storage zone itself, so the check with the real zone tool is still to do (see Test gaps). Design in `docs/superpowers/specs/2026-10-29-vault-pocket-dimension.md`. Lucas decided on 2026-10-29 that items live inside the vault, the entrance goes on the surface only, and one pocket is shared. On 2026-10-30 Lucas confirmed the vault is not a numbered level, and only one vault level exists. On 2026-10-31 Lucas decided that a colonist inside the vault still counts as wealth.
- **Colonist autonomy (requirement 3). Done 2026-10-31.** With the validation colonist's work off, a colonist other than it hauls across levels, mines below, and builds the campfire by itself (runs `20261031-201436`, `201557`, `201704`). Port plan box 74 is ticked. Most earlier stage 20 failures came from the harness. Run `202312` is still unexplained. The 124 starts and ends at once (run `20261028-145345`) came from an older code state. Plan: `docs/superpowers/plans/2026-10-31-colonist-autonomy.md`. Vault v2 depends on this, and it is now unblocked.
- **Reachability bugs.** The retry is fixed for storage cells on the colonist's own level. The storage search now skips cells the colonist cannot reach. The cause was the storage search, not the reachability patch. Red and green runs: `20261031-212210` and `212346`. Debug check: `220806`. The regression set and harness notes are in `docs/validation-report.md`, "Storage reachability". Still open: `CanReach_Patch.Prefix` can throw and leave a pawn on the other map (run `193352`). It is not reproduced.
- **Multiple builds below, with the logs on the surface. Done 2026-11-01.** Three walls below were built from logs carried down from four surface piles (runs `20261101-105213`, `105259`, `105358`). The logs-below variant, with surface hauling stopped, also passes (run `105502`). Details: `docs/validation-report.md`, "Multiple builds below with the logs on the surface".
- **Staircase destroyed by damage. Done 2026-11-01.** A destroyed staircase no longer throws, and its partner stays standing (decision 4). Red run `20261101-112308`, green run `20261101-113033`, core regression `20261101-113126`. Details: `docs/validation-report.md`, "Staircase destroyed by damage".

## Next

- **Cross-level storage reachability.** The storage search does not check storage cells on another level. Checking them needs the stairs route. Plan: `docs/superpowers/plans/2026-10-31-storage-reachability.md`, Ruling 1.
- **Smooth transitions (requirement 1).** Define "smooth" first. Today a colonist walks to the stairs and is then teleported on arrival. Port plan box 75.
- **Infestation insects.** The letter says the bugs tunnel up. The code places them on the level below, next to the natural hole, and nothing moves them up. Decide whether they should come up, then build it or change the letter.
- **Vault v2, hauling out and cross-level storage.** Depends on autonomy (requirement 3).
- **Test gaps.** Each needs a scenario, a check, or an audit.
  - The storage zone made with the real zone tool. You allowed mouse and keyboard control for testing, so this can be driven in the game.
  - Hauling, power, and wood after a save and reload. The reload checks only the level and cell.
  - Every Harmony signature. Names are checked, and only the points in `docs/game-api-findings.md` have their signatures checked. Port plan box 56.
  - Walking past the lower stairs cell and digging out. Port plan box 69.
  - A repeat of the refuel pause from run `032903`. It was seen once and has not come back.
  - The stage 20 timeout in `-OtherColonistHauls` (run `20261031-215012`). No colonist hauled the logs in the wait. The cause is not known.
  - Debug runs fail on an expected reservation error (run `20261031-214306`). See `docs/validation-report.md`, Open issues.

## Decisions for Lucas

- Should the lower level's start open onto a cave? The lower stairs open onto sealed rock today, and the code does not say (validation report, Open issues).
- Should spilled logs stay on their level? In run `165858`, a blocker haul moved 10 logs to the surface.
- Should the rebuilt `ZLevels/1.7/Assemblies/ZLevels.dll` be committed? The committed copy is from `0222951`. The rebuild has the same size and different bytes. The regression runs loaded the rebuild. Answered 2026-11-01: yes. It is committed with the stairs fix.
- When a staircase is destroyed, should its partner be destroyed too? Today the partner takes the same damage, and the code throws `NullReferenceException` once the staircase is gone (run `230657`). I recommend yes: the partner is destroyed too, so the pair stays consistent. Answered 2026-11-01: no. The partner stays standing, and the code skips the partner hit once the staircase is gone.

## Later

- **Power generators inside the vault**, and power carried from the vault to a main level.
- **Tubes between levels** (RimAtomics or similar), so structures on one level can use resources or power from another. Cooling across levels is done, as of 2026-11-03. Runs: `20261103-134331` for the Task 6 green run, `20261103-131411` for the round-2 proof, and `20261103-135031` for the Rimatomics++ cooled run. Resources and power are still open.
- **Other mods.** Check which mod features touch levels, pocket maps or portals before building for them.

## Not goals now

- Multiplayer (optional, unverified).
- Existing saves. They are not opened.
