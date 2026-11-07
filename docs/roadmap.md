# Roadmap

Updated 2026-11-07. Order matters: finish a stage before starting the next. Port plan boxes refer to `docs/superpowers/plans/2026-10-27-zlevels-17.md`.

## Now

- **Level differences (answered 2026-10-29).** See `docs/levels-and-events.md`.
- **Vault v1, a pocket dimension. Done 2026-10-31.** Acceptance a to h pass in isolated saves; the run folders are in `docs/validation-report.md`. Open: the test places the storage zone itself, so the check with the real zone tool is still to do (see Test gaps). Design in `docs/superpowers/specs/2026-10-29-vault-pocket-dimension.md`. Lucas decided on 2026-10-29 that items live inside the vault, the entrance goes on the surface only, and one pocket is shared. On 2026-10-30 Lucas confirmed the vault is not a numbered level, and only one vault level exists. On 2026-10-31 Lucas decided that a colonist inside the vault still counts as wealth.
- **Colonist autonomy (requirement 3). Done 2026-10-31.** With the validation colonist's work off, a colonist other than it hauls across levels, mines below, and builds the campfire by itself (runs `20261031-201436`, `201557`, `201704`). Port plan box 74 is ticked. Most earlier stage 20 failures came from the harness. Run `202312` is still unexplained. The 124 starts and ends at once (run `20261028-145345`) came from an older code state. Plan: `docs/superpowers/plans/2026-10-31-colonist-autonomy.md`. Vault v2 depends on this, and it is now unblocked.
- **Reachability bugs.** The retry is fixed for storage cells on the colonist's own level. The storage search now skips cells the colonist cannot reach. The cause was the storage search, not the reachability patch. Red and green runs: `20261031-212210` and `212346`. Debug check: `220806`. The regression set and harness notes are in `docs/validation-report.md`, "Storage reachability". Still open: `CanReach_Patch.Prefix` can throw and leave a pawn on the other map (run `193352`). It is not reproduced.
- **Multiple builds below, with the logs on the surface. Done 2026-11-01.** Three walls below were built from logs carried down from four surface piles (runs `20261101-105213`, `105259`, `105358`). The logs-below variant, with surface hauling stopped, also passes (run `105502`). Details: `docs/validation-report.md`, "Multiple builds below with the logs on the surface".
- **Staircase destroyed by damage. Done 2026-11-01.** A destroyed staircase no longer throws, and its partner stays standing (decision 4). Red run `20261101-112308`, green run `20261101-113033`, core regression `20261101-113126`. Details: `docs/validation-report.md`, "Staircase destroyed by damage".

## Multifloors ideas (plan of 2026-11-05)

Each task of `docs/superpowers/plans/2026-11-05-multifloors-ideas.md` is one commit on `master`. Its red and green run folders are in `docs/validation-report.md`.

Built:

- Phase A, bugs and small fixes. Tasks 1 to 5: rest search null checks, the trader's return, the About text, a refused reservation that retries quietly, and the N and M floor keys.
- Phase B, speed and own floor first. Tasks 6 to 23, except Task 24. Stay on this floor and work on this floor (6, 7), needs cool-down (8), idle search pause (9), stale claims (10), floor guard (11), one storage index (12), stair routes (13), add-ons load on demand (14), spread chores (15), nothing extra until floors exist (16), view the connected floor (17), stair placement messages (18), search timing baseline (19), lighter update on extra floors (20), own floor first (21), background item marking (22), check the job on arrival (23).
- Phase C, raids, exits, events and fights. Tasks 25 to 28: raids land on the ground, leaving walks to the ground first, game events, and fighting across floors.
- Phase D, player features. Tasks 29 to 35: the Floors tab, the work filter per floor, haul to a floor, the workbench floor link, call colonists here, heat and gas through stairs, and the in-game guide.
- Phase E, more ways between floors and a stair window. Tasks 36 to 38: small and large ladders and large stairs (decision D3, option a), pipes between floors that move one chosen resource at a fixed rate (decision D4, option a), and the stair transfer window that sends a chosen list of colonists and items now (decision D5, option a). Plan: `docs/superpowers/plans/2026-11-06-multifloors-phase-e.md`.
- Task 39, the final regression. Done 2026-11-07, on commit `58668d1`. Two defects found in review are fixed: a floor pipe on three floors made three pipes instead of two (`c57e5f1`), and a checked item that left the floor threw in the transfer window (`58668d1`). Of the 59 runs, 55 pass after one rerun. The other four are explained in `docs/validation-report.md`, "Final regression, every scenario": `-VaultInfestation` (a harness helper the vault session owns), `-UnreachableZone` (flaky, cause open), `-CoolingNoColumns` (a negative control that fails by design), and `-CrossFloorFight` (decision D6).

Not built:

- Task 24, "leave the busiest questions alone". Not built. Its plan gate asks for per-patch timing, which does not exist yet, because the Task 19 numbers time the whole search. The patches it would remove touch combat and reservation logic. Reverse: say so, and it is built one patch at a time, with a row for each, after the timing is taken.
- The powered elevator and the freight elevator (idea "more ways up"). Decision D3 chose option a, so only ladders and stair sizes are built.
- The standing "keep a set amount on the next floor" option of the stair transfer. Decision D5 chose option a, so the window sends a chosen list now only.
- Power and fluids through the connectors. Decision D4 chose one chosen resource at a fixed rate. The "tubes between levels" line stays open for those.

Decisions made under "implement all of them" (plan 2026-11-06, DM14 to DM20):

- DM14: a large way is one 2 by 2 building. Its origin is the bottom-left cell, and its partner covers the same four cells on the other floor. Large ways are not rotatable.
- DM15: costs and strength of the new ways (balance only).
- DM16: a large way needs four cells in one room of at least 25 cells, and four standable empty cells on the other floor.
- DM17: a floor pipe is placed on the lower floor and has its other end on the floor above. Removing one end removes the other, and a gizmo flips the sending end. One pipe is one pair of ends: the other end makes none of its own.
- DM18: a pipe moves 60 items of its resource per game day, in steps of 250 ticks. The items land next to the receiving end, and the normal haul logic stores them.
- DM19: the resource choices are steel first, then the raw resources with a stack limit above 1, by label.
- DM20: the stair transfer is a window from a button on each stairs piece. Checked colonists walk to the landing cell through `CallColonistsTargeter.CallTo`. Checked items are marked with `MarkItemsForFloor`, so an item the other floor does not need is counted and not sent.

Waiting:

- D6 is open and does not block the work. Its default (a) is applied as of 2026-11-07: the rule stays, and the shot row of `-CrossFloorFight` expects the refusal. Lucas can overrule it (see the decisions list).

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
- D3 (Task 36). Answered 2026-11-06: ladders and stair sizes (the recommended option). No powered elevator and no freight elevator are built. The decisions made under it are DM14 to DM16 in the Phase E plan.
- D4 (Task 37). Answered 2026-11-06: one chosen resource per pipe, at a fixed rate (the recommended option). The decisions made under it are DM17 to DM19 in the Phase E plan. The "Tubes between levels" line stays open for power and fluids.
- D5 (Task 38). Answered 2026-11-06: send a chosen list of colonists and items now (the recommended option). The standing "keep a set amount" option is not built. The decision made under it is DM20 in the Phase E plan.
- D6 (open, not blocking; default applied 2026-11-07). May a shot from a lower floor reach a surface target over solid ground? Default (a), applied: keep the rule, so the shot is refused. The row "a shot from the lower floor across solid ground is refused, as the rule says" expects the refusal. Reversal: option (b) changes the rule in `CombatPatches.cs` and this row. On 2026-11-07 Lucas wrote "D1 a", which I read as D1. If he meant D6, D6 is answered (a).
- D7 (answered 2026-11-07: a). Keep the Royalty flag on `-FloorsTab` only. Lucas's words: "No mandatory DLC please." The default validation mod list does not load Royalty.
- D1 (answered 2026-11-07: A). Measure, then rebuild the job search in place. Lucas's words: "D1 a". The measurement is Task 24's per-patch timing, in `docs/superpowers/plans/2026-11-07-task24-patch-timing.md`.
- D2 was answered on 2026-11-05: "all of them". D1 was read as rebuild in place until Lucas answered it on 2026-11-07.

## Later

- **Power generators inside the vault**, and power carried from the vault to a main level.
- **Tubes between levels** (RimAtomics or similar), so structures on one level can use resources or power from another. Cooling across levels is done, as of 2026-11-03. Runs: `20261103-134331` for the Task 6 green run, `20261103-131411` for the round-2 proof, and `20261103-135031` for the Rimatomics++ cooled run. Resources and power are still open.
- **Other mods.** Check which mod features touch levels, pocket maps or portals before building for them.

## Not goals now

- Multiplayer (optional, unverified).
- Existing saves. They are not opened.
