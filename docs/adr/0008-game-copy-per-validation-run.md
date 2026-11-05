# Each validation run starts its own game copy

A validation run starts a copy of the RimWorld install, not the install itself. `New-ValidationGame` in `tools/ValidationGame.psm1` makes the copy under `%TEMP%\claude-scratch\zlevels-validation\<runId>\`. Each folder of the install except `Mods` is a junction to the install. `Assets.zip` is a hard link, and the other files are copies. The copy's own `Mods` folder holds copies of the two mods, staged from the build. Steam still supplies Harmony and Rimatomics.

A passing run removes its copy. A failing run keeps it, so the failure can be read. The install and the game's `Mods` folder are only read. Runs are serial. The game writes `Player.log` to one per-user path, so two games at once would write to the same log. The review described option C1 as allowing parallel runs. That claim is wrong.

Decided 2026-11-05. This answers D6, option C1, with that correction.
