# Cross-level supply plan

Date: 2026-10-31
Status: done. Checks S1 to S9 pass (run `20261031-175919`). The main chain passes with the campfire at 20 logs (run `20261031-175236`). No mod change was needed. The details and open items are in `docs/validation-report.md`, under stage 22 and cross-level supply.

## Decisions (Lucas, 2026-10-31)

1. Colonists inside the vault still count as wealth.
2. The storage zone in the vault is made by the test, as a stand-in for the player. A check with the real zone tool comes later.
3. Option A: a job may take materials from storage on another level. This applies in both directions, and to building, crafting, and refuelling.
4. The campfire test's first build fails on purpose in every run.

## Problem

- Known stall: a build on a lower level cannot take materials stored on the surface. Runs `20261031-141006`, `142258`, and `143044` show the rebuild's search looking at the surface and finding nothing.
- Not yet tested: surface builds from lower storage, crafting and refuelling in both directions.

## Scenario `-zlevelssupply` (runner switch `-Supply`, pass label `cross-level supply`)

Setup: the validation colonist walks down the stairs, as in the main chain. Other colonists get no work. Before each test, wood and granite on both levels are cleared, so only the test's own items can supply it.

| Test | Job | Item stored | Item location | Pass when |
|---|---|---|---|---|
| S1 | build on lower | 20 logs | surface zone | campfire built on lower |
| S2 | build on surface | 20 granite blocks | lower zone | wall built on surface |
| S3 | craft on surface | 20 granite chunks | lower zone | granite blocks appear on surface |
| S4 | craft on lower | 20 granite chunks | surface zone | granite blocks appear on lower |
| S5 | refuel on surface | 20 logs | lower zone | campfire fuel above zero |
| S6 | refuel on lower | 20 logs | surface zone | campfire fuel above zero |

Each test waits at most four minutes. The stored items must be used up (counts drop) as well as the building appearing.

## Order of work

1. Red: write the scenario. Run it. Record which tests fail first.
2. For each red test, debug with the mod's debug log (systematic debugging). Find the cause before changing code.
3. Fix in the mod, one cause at a time. Re-run the failing test, then the whole scenario.
4. Main chain: after the fix, return the campfire to 20 logs on the lower level. The rebuild after the forced failure then takes the missing logs from the surface stock. This is the end-to-end check of decision 3.
5. Regressions: main chain, `-VaultEnter`, `-VaultLoad`, `-VaultSave`, `-VaultInfestation`, `-InfestationGuard`, `-StaleLookup`, `-VaultPlacement`, `-VaultPocket`.
6. Commit each fix with its runs. Update `docs/validation-report.md`.

## Risks

- The cross-level search checks materials per level. A fix may need to count across levels, which costs time for each job search.
- Clearing surface wood and granite changes the colony only for the run in progress.
- S4 and S6 (lower jobs from surface storage) may already work, or may need the same fix as S1.

## Open

- None blocking. If a test needs a change outside the job search, I will stop and report before making it.

## Rulings made while writing the scenario

- A failed test is recorded as FAIL and the next test still runs, so one run shows all six results. Cost: a fully red run can take up to about 25 minutes, so the runner is started with `-TimeoutSeconds 1800`.
- S1 stores 40 logs, not 20. Cost: none for the pass check (at least 20 must be used). A first build that fails by chance can still finish.
- Before each test, the scenario also removes campfires, walls, stonecutter tables, blueprints, frames, and anything the colonist carries. Things that existed before the first test (a baseline taken then) stay. Cost: none for the throwaway colony. Without this, the stonecutter left by S3 would turn S4's chunks into surface blocks and skew S4.
- Zone and cell search starts from a free cell beside the stairs, because the stairs cell itself may have no region to path from. Cost: in an unusual layout a setup can fail with "No free cell", which is a visible failure, not a false pass.
