# Roadmap

Updated 2026-10-29. Order matters: finish a stage before starting the next.

## Now

- **Level differences (answered 2026-10-29).** See `docs/levels-and-events.md`.
- **Vault v1, a pocket dimension.** Design in `docs/superpowers/specs/2026-10-29-vault-pocket-dimension.md`. Lucas decided on 2026-10-29 that items live inside the vault, the entrance goes on the surface only, and one pocket is shared. On 2026-10-30 Lucas confirmed the vault is not a numbered level, and only one vault level exists.
- **Colonist autonomy (requirement 3).** Not met. Run `20261028-145345`, stage 20: a cross-level haul job starts and ends at once, 124 times. Cause unknown.

## Next

- **Smooth transitions (requirement 1).** Define "smooth" first. Today a colonist walks to the stairs and is then teleported on arrival.
- **Infestation insects.** The letter says the bugs tunnel up. The code places them on the level below, next to the natural hole, and nothing moves them up. Decide whether they should come up, then build it or change the letter.
- **Vault v2, hauling out and cross-level storage.** Depends on autonomy (requirement 3).

## Later

- **Power generators inside the vault**, and power carried from the vault to a main level.
- **Tubes between levels** (RimAtomics or similar), so structures on one level can use resources or power from another.
- **Other mods.** Check which mod features touch levels, pocket maps or portals before building for them.

## Not goals now

- Multiplayer (optional, unverified).
- Existing saves. They are not opened.
