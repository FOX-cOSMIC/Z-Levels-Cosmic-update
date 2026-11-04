# Z-Levels for RimWorld 1.7

Port of Z-Levels (RamRod, package `foxcosmic.zlevels17`) to RimWorld 1.7.4990 rev1069. One checkout: this folder, branch `master`. Remote `origin` is https://github.com/FOX-cOSMIC/Z-Levels-Cosmic-update (public GitHub fork of RamRod6198/Z-Levels).

## Documents

- `docs/superpowers/specs/2026-10-27-zlevels-17-design.md`: scope, design, acceptance. Read it for any scope question.
- `docs/superpowers/plans/2026-10-27-zlevels-17.md`: task queue. Read it when you start or resume a task. Its checkboxes lag the code: before redoing a task, check `git log` and `.artifacts/runs/` for its evidence.

## Constraints

- Target RimWorld 1.7.4990 rev1069 on the Steam unstable branch (authorized permanently). Not 1.6.
- Keep the namespace, the types XML uses, and the subsystem boundaries. Package id is `foxcosmic.zlevels17` (changed from `ZLevels1.1` on 2026-11-04 so the Steam item does not collide with upstream's Workshop item; `About.xml` marks `ZLevels1.1` incompatible).
- Leave `ZLevels/1.1`, `1.2` and `1.3` unchanged. 1.7 loads through `ZLevels/LoadFolders.xml`.
- Ship no game DLLs and no second Harmony. `0MultiplayerAPI.dll` ships as the older versions do.
- Keep `HarmonyPatches/JobPatches - Copy.cs` out of the build.
- Multiplayer stays optional and unverified.
- Validate only in isolated savedata and a new colony. Never open existing saves or change the user's mod list.
- Steam Workshop: Lucas decided on 2026-11-04 to publish with credit to RamRod. The original author could not be reached and has published no license. Upload as Private first; Lucas switches it to Public after testing. Claim no modpack or multiplayer compatibility.
- Keep attribution. Upstream has no license file, and this port grants no redistribution rights.

## Commands

- Build: `dotnet build ZLevels/Source/ZLevels/ZLevels/ZLevels.csproj -c Release`. Output: `ZLevels/1.7/Assemblies/`. The Harmony DLL defaults to the Steam Workshop path; override with `-p:HarmonyAssembly=<path>`.
- Runtime validation: close RimWorld, then run `tools/Invoke-RuntimeValidation.ps1` (`-Visual` adds screenshots). Evidence: the run folder under `.artifacts/runs/`.
- Decompile installed game code: `ilspycmd`, a dotnet global tool. Write output under `.artifacts/`.

## Verification

- Compiling is not playability. A gameplay change is proven by a validation run that exercises it. Proof: the script prints `Runtime validation passed. Evidence: <run folder>`.
- Reproduce a bug in a validation run before you fix it.
- Test-first here means a validation scenario or in-game check that fails before the fix and passes after. There is no unit-test suite, and mock tests are not gameplay proof.
- Record failures and untested cases as they are. Never disable a patch or hide an error to get a green run.
- Check every Harmony attribute and reflected name against the installed 1.7 code.

## Git

- Commit only your own paths: `git commit -- <paths>`. The tree may hold other sessions' changes; check `git status` and leave changes you did not make alone.
- Stay on `master`. Push only when Lucas asks, and only to a branch on `origin`. Do not push to `origin`'s `master`. Never force-push.

## Routes

Before your first action on a task, match it against these routes and invoke the matching skill yourself. In Claude, call the Skill tool. In Codex, open the skill's `SKILL.md` and follow it.

The general routes come from your global instructions. These rows cover this project's work.

| When the task is | Use | Done when |
|---|---|---|
| Doing the next unfinished task in the port plan | `executing-plans`, inline in this session. Not `subagent-driven-development`: the plan requires inline execution. | each task checkbox is ticked, with its evidence |
| Confirming the name, field, or signature that RimWorld 1.7 actually uses | `reverse-engineering` (Codex). In Claude, which lacks it, decompile with `ilspycmd`. | the decompiled source or a log line confirms it; the finding goes in `docs/game-api-findings.md` (create it on the first finding) |
| Finding out what external documentation says | `research` | the answer cites its source and is saved as Markdown in the repo |
| Frames dropping, or working out why something got slower | `diagnosing-bugs` | the cause is proven with before-and-after numbers |
| Checking what changed against the design spec | `code-review` | each spec requirement is checked against the change |
| Two problems that are independent and live in different files | `dispatching-parallel-agents`, one agent per file group | each group is fixed, and the combined result passes a validation run |
| A reviewer's comments arrive on a change | `receiving-code-review` | each comment is checked against the code, then fixed or answered |
| Changing error handling, a try/catch, or a Harmony patch guard | `silent-failure-hunter` agent (Claude). In Codex, check the diff yourself for caught-and-ignored exceptions. | the diff is checked for swallowed errors |
| Finding every caller, definition, or implementation of a C# symbol | the LSP tool, backed by `csharp-lsp` (Claude only) | the references come from the language server, not from text search |

## Terms

- Surface: the normal map. Lower: the underground level below it (`MapGenerator/Underground.xml`). Upper: the level built above it (`MapGenerator/Upper.xml`).
- Stairs: `Buildings/Stairs.xml`, `Building_StairsDown` and `Building_StairsUp`. They connect the levels.
