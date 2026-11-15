# Build Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix build-foundation findings 1-5, and carry out decisions D5 (DLLs stay tracked, written only by a release script), D6 (each validation run uses its own game copy) and D7 (retire the 1.1 and 1.3 folders).

**Architecture:** One version source, `Directory.Build.props`, read by the project files, the runner and a docs check. Dev and validation builds write `bin/`. `tools/Publish-Mod.ps1` alone writes the tracked DLLs, with path mapping so the bytes hold no machine paths. The runner stages the built mods into a game copy under `%TEMP%\claude-scratch\zlevels-validation\<runId>\`, and writes the commit and DLL hashes at the top of the results file. The gate refuses results whose hashes do not match.

**Tech Stack:** C# (.NET 10 mod for RimWorld 1.7), MSBuild, NuGet `Krafs.Rimworld.Ref 1.7.4998-unstable`, PowerShell 5.1, git.

**Spec:** the Summary and Decisions sections of the build-foundation review (`.artifacts/review/build-foundation.md`, gitignored). Decisions are recorded in `docs/adr/0006` to `0009` by Task 8.

## Global Constraints

- Target RimWorld `1.7.4998 rev1299`, Steam unstable branch. Not 1.6.
- Keep the namespace `ZLevels` and the package id `foxcosmic.zlevels17`.
- Ship no game DLLs and no second Harmony. `0MultiplayerAPI.dll` ships in `ZLevels/1.7/Assemblies`.
- `HarmonyPatches/JobPatches - Copy.cs` stays out of the build.
- Validate only in isolated savedata and a new colony. Never write the user's `Mods` folder or ModsConfig, and never open an existing save. Runs are serial: the game writes `Player.log` to one per-user path.
- Keep attribution to RamRod in README and CLAUDE.md.
- Work in the worktree `%TEMP%\claude-scratch\build-foundation\wt` on branch `chore/build-foundation`, cut from `origin/zlevels-1.7`. Leave the main checkout's uncommitted changes alone. Push the branch only. Never push `master`, never force-push.
- Commits: `git add` each new file first, then `git commit -- <paths>` with the same paths. Commit only the paths a task names.
- Gameplay code does not change. Its proof is a validation run that passes the gate.
- Test first means a check that fails before its fix and passes after it. There is no unit-test suite. Scratch checks live under `%TEMP%\claude-scratch\build-foundation\` and are not committed.
- Until Task 4 moves outputs to `bin/`, build checks pass `-p:OutputPath=<scratch>\out\` so tracked DLLs stay unchanged.

## Review Focus

1. The game updates, so `Version.txt` no longer matches the pin. The build stops and names both versions. (Task 1, step 1)
2. A release is cut with uncommitted changes. `Publish-Mod.ps1` refuses and changes no DLL. (Task 4, step 4)
3. A game copy is left by an interrupted run. `New-ValidationGame` refuses a folder it did not create and leaves its contents alone. (Task 5, step 1)
4. The game path has spaces and parentheses, as `Program Files (x86)` does. Copy and link work, and removing a copy never deletes the source. (Task 5, step 1)
5. A results file has no identity header, from an older runner or an edit. The gate refuses it. (Task 6, step 1)

---

### Task 1: Version source and compile guard (finding 1, part of 3)

**Files:**
- Create: `Directory.Build.props`, `RimWorld.targets` (repository root)
- Modify: `ZLevels/Source/ZLevels/ZLevels/ZLevels.csproj` (line 13 delete, line 18 pin, add import), `ZLevels/Source/ZLevels/ZLevelsRimatomics/ZLevelsRimatomics.csproj` (line 12 delete, line 18 pin, add import), `tools/RuntimeValidation/RuntimeValidation.csproj` (line 12 pin, add import)

**Interfaces:**
- Produces: `RimWorldVersion` (`1.7.4998 rev1299`), `RimWorldPackageVersion` (derived), `RimWorldDirectory` (default install path). Consumed by Tasks 2, 4 and 6.

- [ ] **Step 1: Write the failing check.** Create `%TEMP%\claude-scratch\build-foundation\fake-game\Version.txt` containing `1.7.0 rev1`. Build `ZLevels.csproj -c Release` with `-p:RimWorldDirectory=<fake-game>`, `-p:HarmonyAssembly=C:/SteamLibrary/steamapps/workshop/content/294100/2009463077/1.7/Assemblies/0Harmony.dll` and `-p:OutputPath=<scratch>\out\`. Expected before the fix: exit 0. That is red, because nothing compares the game version.

- [ ] **Step 2: Implement.** `Directory.Build.props`:

  ```xml
  <Project>
    <PropertyGroup>
      <RimWorldVersion>1.7.4998 rev1299</RimWorldVersion>
      <RimWorldPackageVersion>$(RimWorldVersion.Split(' ')[0])-unstable</RimWorldPackageVersion>
      <RimWorldDirectory Condition="'$(RimWorldDirectory)' == ''">C:/SteamLibrary/steamapps/common/RimWorld</RimWorldDirectory>
    </PropertyGroup>
  </Project>
  ```

  `RimWorld.targets` holds a target `CheckRimWorldVersion`, `BeforeTargets="ResolveReferences"`. It reads `$(RimWorldDirectory)/Version.txt` with `ReadLinesFromFile` into `InstalledRimWorldVersion`. It errors when the file is missing, and when `$(InstalledRimWorldVersion.Trim())` differs from `$(RimWorldVersion)`. The error text is: `Installed RimWorld is $(InstalledRimWorldVersion.Trim()); this build expects $(RimWorldVersion). Update Directory.Build.props, or point RimWorldDirectory at the matching install.`

  In each of the three projects: add `<Import Project="$([MSBuild]::GetPathOfFileAbove('RimWorld.targets', '$(MSBuildThisFileDirectory)'))" />` after the first PropertyGroup, and change the Krafs pin to `Version="$(RimWorldPackageVersion)"`. Delete the `RimWorldDirectory` default line from `ZLevels.csproj` and `ZLevelsRimatomics.csproj`, since the props file now holds it.

- [ ] **Step 3: Run the check again.** Expected: exit 1, with a message that names `1.7.0 rev1` and `1.7.4998 rev1299`. Green.

- [ ] **Step 4: Normal build.** `dotnet build tools/RuntimeValidation/RuntimeValidation.csproj -c Release -t:Rebuild -p:OutputPath=<scratch>\out\`. Expected: `0 Error(s)`. Record the warning count. The baseline is 10.

- [ ] **Step 5: Commit.** `git add Directory.Build.props RimWorld.targets`, then `git commit -- Directory.Build.props RimWorld.targets ZLevels/Source/ZLevels/ZLevels/ZLevels.csproj ZLevels/Source/ZLevels/ZLevelsRimatomics/ZLevelsRimatomics.csproj tools/RuntimeValidation/RuntimeValidation.csproj`.

### Task 2: Docs follow the version source (finding 3)

**Files:**
- Create: `tools/Check-Docs.ps1`
- Modify: `README.md` (lines 9 and 21 name the version; the paragraph near line 41 that documents the `RimWorldDirectory` and `HarmonyAssembly` defaults)
- Regenerate: `AGENTS.md` from `CLAUDE.md`

**Interfaces:**
- Consumes: `RimWorldVersion` from Task 1, read by regex from `Directory.Build.props`.
- Produces: `tools/Check-Docs.ps1 [-Fix]`. It exits 1 when `README.md` or `CLAUDE.md` contains a `1.7.NNNN rev N` string other than `RimWorldVersion`, or when `AGENTS.md` differs from `CLAUDE.md` after line endings are normalized. With `-Fix`, it copies `CLAUDE.md` to `AGENTS.md`. `AGENTS.md` is a copy, not a pointer, because Codex reads `AGENTS.md` and does not follow links.

- [ ] **Step 1: Write the checker and run it on the current docs.** Expected: exit 1, naming README lines 9 and 21 (4990) and `AGENTS.md` (differs). Red.

- [ ] **Step 2: Fix the README.** Lines 9 and 21 name `1.7.4998 rev1299`. The defaults paragraph names `Directory.Build.props` as the source of `RimWorldDirectory`. Use the `technical-writing` skill for these edits.

- [ ] **Step 3: Regenerate AGENTS.md.** Load `writing-for-agents` before editing `CLAUDE.md` or `AGENTS.md`. Run `powershell -NoProfile -File tools\Check-Docs.ps1 -Fix`.

- [ ] **Step 4: Run the checker.** Expected: exit 0. Green.

- [ ] **Step 5: Commit.** `git add tools/Check-Docs.ps1`, then `git commit -- README.md AGENTS.md tools/Check-Docs.ps1`.

### Task 3: Attribute rules (finding 4, corrected)

The root `.gitignore` already exists on `origin/zlevels-1.7` (`**/bin/`, `**/obj/`, `.artifacts/`), so the ignore half of finding 4 is done. The open part is the attribute file.

**Files:**
- Create: `.gitattributes` (repository root)
- Delete: `ZLevels/.gitattributes` (its only rule, `* text=auto`, moves to the root)

- [ ] **Step 1: Write the failing check.** `git check-attr binary -- ZLevels/1.7/Assemblies/ZLevels.dll`. Expected before the fix: `unspecified`. Red.

- [ ] **Step 2: Implement.** The root `.gitattributes` holds exactly `* text=auto`, `*.dll binary` and `*.png binary`. Run `git rm ZLevels/.gitattributes`.

- [ ] **Step 3: Run the check again.** Expected: `binary: set`. Then `git status --short` lists only the two files. Any other file with line-ending churn means the rules need a fix before commit. Green.

- [ ] **Step 4: Commit.** `git add .gitattributes`, then `git commit -- .gitattributes ZLevels/.gitattributes`.

### Task 4: DLLs built to `bin/`, released by a script (finding 2, D5)

**Files:**
- Modify: `Directory.Build.props` (add `PathMap`); `ZLevels/Source/ZLevels/ZLevels/ZLevels.csproj` (delete line 10, `OutputPath`); `ZLevels/Source/ZLevels/ZLevelsRimatomics/ZLevelsRimatomics.csproj` (delete line 10); `tools/RuntimeValidation/RuntimeValidation.csproj` (delete line 6)
- Create: `tools/Publish-Mod.ps1`
- Regenerate, through the script only: `ZLevels/1.7/Assemblies/ZLevels.dll`, `ZLevelsRimatomics.dll`, `0MultiplayerAPI.dll`, and the new `build-info.txt` beside them

**Interfaces:**
- Produces: `tools/Publish-Mod.ps1`, with no parameters. It refuses unless `git status --porcelain --untracked-files=normal -- ZLevels Directory.Build.props RimWorld.targets ':!ZLevels/1.7/Assemblies'` prints nothing. It builds `ZLevelsRimatomics.csproj` with `-c Release -t:Rebuild`, which also builds `ZLevels.csproj`. It copies the three DLLs from `bin/Release/` into `ZLevels/1.7/Assemblies/`. It writes `build-info.txt` with the source commit, the RimWorld version, the SHA-256 of each DLL and the .NET SDK version. It fails when a DLL contains `G4M3R` or the byte sequence `:\`.
- Every project's dev and validation output is `<project>/bin/Release/`.

- [ ] **Step 1: Write the failing determinism check.** Add a detached worktree at HEAD in `%TEMP%\claude-scratch\build-foundation\wt2`. Build `ZLevels.csproj -c Release -t:Rebuild` in both worktrees with `-p:OutputPath=<scratch>\out1\` and `out2\`. Compare the SHA-256 of `ZLevels.dll` from each, and search both for `G4M3R`. Expected before the fix: the hashes differ and `G4M3R` is found. Red.

- [ ] **Step 2: Implement and commit the code.** Add to `Directory.Build.props`: `<PathMap>$(MSBuildThisFileDirectory)=/_/</PathMap>`. Delete the `OutputPath` lines listed under Files. Write `tools/Publish-Mod.ps1` with the behavior under Interfaces. `git add tools/Publish-Mod.ps1`, then `git commit -- Directory.Build.props ZLevels/Source/ZLevels/ZLevels/ZLevels.csproj ZLevels/Source/ZLevels/ZLevelsRimatomics/ZLevelsRimatomics.csproj tools/RuntimeValidation/RuntimeValidation.csproj tools/Publish-Mod.ps1`.

- [ ] **Step 3: Run the determinism check again.** Update `wt2` to the commit from Step 2 with `git -C <wt2> checkout --detach <sha>`. Rebuild in both worktrees. Expected: equal hashes and no `G4M3R`. If the hashes still differ, the embedded path comes from another route. Try `<ContinuousIntegrationBuild>true</ContinuousIntegrationBuild>` in `Directory.Build.props`, commit it, and record which setting cleared it. Green.

- [ ] **Step 4: Test the dirty-tree refusal.** Append a newline to `ZLevels/Changelog.txt`, then run `tools\Publish-Mod.ps1`. Expected: exit 1, with the three tracked DLLs unchanged (compare hashes before and after). Revert with `git checkout -- ZLevels/Changelog.txt`, and confirm `git status --short` is empty.

- [ ] **Step 5: Publish.** Run `tools\Publish-Mod.ps1` on the clean tree. Expected: exit 0. Confirm that the `build-info.txt` hashes equal the `bin/Release` hashes, and that the three committed DLLs contain no `G4M3R`.

- [ ] **Step 6: Commit the release.** `git add ZLevels/1.7/Assemblies/build-info.txt`, then `git commit -- ZLevels/1.7/Assemblies`.

- [ ] **Step 7: Remove the scratch worktree** `wt2` with `git worktree remove --force` and `git worktree prune`.

### Task 5: A game copy per validation run (D6, C1)

**Files:**
- Create: `tools/ValidationGame.psm1`

**Interfaces:**
- `New-ValidationGame -SourceGame <string> -Destination <string> -RunId <string> -ModFolders <string[]>` returns `[pscustomobject]@{ Root; Executable }`. It refuses a `Destination` that exists without a marker file `zlevels-validation-game.json` whose `RunId` matches, with the message `Existing folder is not a validation game copy from this run: <path>`. It creates these items in `Destination`:
  - `Data`, `Rendering` and `vulkan` as junctions to the source (`cmd /c mklink /J`).
  - `Assets.zip` as a hard link (`cmd /c mklink /H`), falling back to a copy if the link fails.
  - Every other top-level file of the source, as a copy. This includes `RimWorld.exe`, `steam_appid.txt` and `Version.txt`.
  - `Mods\`, a real folder that holds a copy of each folder in `-ModFolders`, under its leaf name.
  - The marker file.
- `Remove-ValidationGame -Destination <string>` refuses without the marker. It removes each junction with `cmd /c rmdir "<path>"` (no `/S`), after checking that its target lies under the recorded source. Then it removes the rest with `Remove-Item -Recurse -Force`. Junctions are gone by then, so the removal cannot reach the source.
- Harmony and Rimatomics are not copied. Steam supplies the subscribed workshop items, as in the current runs.

- [ ] **Step 1: Write the failing checks.** In `%TEMP%\claude-scratch\build-foundation\check-validation-game.ps1`, three checks run before the module exists, so all three fail.
  - (a) Snapshot the names and link targets of `C:\SteamLibrary\steamapps\common\RimWorld\Mods`. Create a copy of the real install under `%TEMP%\claude-scratch\build-foundation\check-copy\` with the module, then snapshot again. The two snapshots must match. Remove the copy with `Remove-ValidationGame` afterwards.
  - (b) A destination folder that holds a sentinel file and no marker must be refused, and the sentinel must survive.
  - (c) A fake source at `%TEMP%\claude-scratch\build-foundation\fake game (copy test)\` holds `Version.txt`, `RimWorld.exe` (a placeholder), `Assets.zip` (a placeholder), `Data\sentinel.txt`, `Rendering\` and `vulkan\`. Create the copy and remove it. Then `Data\sentinel.txt` in the fake source must still exist.

- [ ] **Step 2: Implement the module.**

- [ ] **Step 3: Run the checks again.** Expected: all three pass. Record the size of the copied items, which is the copy's real cost.

- [ ] **Step 4: Commit.** `git add tools/ValidationGame.psm1`, then `git commit -- tools/ValidationGame.psm1`.

### Task 6: Runner identity, gate, copy and rebuild (findings 5 and 10)

**Files:**
- Modify: `tools/Invoke-RuntimeValidation.ps1`
  - Build at lines 446-449: add `-t:Rebuild`.
  - Junction block at lines 451-465: delete. It is replaced by the game copy.
  - Line 470: the `<version>` value is read from `Directory.Build.props` by regex.
  - Launch at lines 526 and 552: use the copy's `Executable` and copy root as the working directory.
  - Results at lines 586-601: add the identity gate.

**Order of operations in a run:** build (`-t:Rebuild`), then hash `ZLevels/Source/ZLevels/ZLevels/bin/Release/ZLevels.dll` as the built hash, then stage the mods, then create the game copy at `%TEMP%\claude-scratch\zlevels-validation\<runId>\` with `New-ValidationGame`, then hash `Mods\ZLevels\1.7\Assemblies\ZLevels.dll` inside the copy as the staged hash, then write the header, then launch. On success the runner removes the copy with `Remove-ValidationGame`. On failure it keeps the copy and prints its path.
- Staging: `ZLevels` gets `About`, `Common`, `1.7` without `1.7\Assemblies`, and `LoadFolders.xml`, plus the built `ZLevels.dll`, `ZLevelsRimatomics.dll` and `0MultiplayerAPI.dll` in `1.7\Assemblies`. `ZLevels-Validation` gets `About` and the built `RuntimeValidation.dll` in `Assemblies`. The folder names `ZLevels` and `ZLevels-Validation` become the names under `Mods`.

**Interfaces:**
- Produces: `Get-IdentityProblems -Results <string[]> -BuiltDllSha256 <string>` returns `[string[]]`. It reports each missing `identity` key, a staged hash that differs from the built hash, and a built hash that differs from `-BuiltDllSha256`.
- Header, written before launch as the first lines of `validation-results.txt`, one key per line:
  - `identity commit: <sha>`
  - `identity dirty: <True|False>`, from `git status --porcelain --untracked-files=normal -- ZLevels tools/RuntimeValidation Directory.Build.props RimWorld.targets`
  - `identity rimworld: <RimWorldVersion>`
  - `identity built ZLevels.dll sha256: <hex>`
  - `identity staged ZLevels.dll sha256: <hex>`
  The `identity ` prefix keeps these lines out of the row count in `Assert-ResultsMatchLog` and out of the `| FAIL:` match.
- Write the header with `[System.IO.File]::WriteAllLines($path, $lines, (New-Object System.Text.UTF8Encoding $false))`. PowerShell 5.1's `Set-Content -Encoding UTF8` writes a BOM, which would hide the first key from the match.
- The gate adds `@(Get-IdentityProblems -Results $results -BuiltDllSha256 <SHA-256 of the bin ZLevels.dll, uppercase>)` to `$gateProblems`.

- [ ] **Step 1: Write the failing check.** Parse the runner with `[System.Management.Automation.Language.Parser]::ParseFile`, extract the `Get-IdentityProblems` definition from the AST, and load it with `[scriptblock]::Create`. Before the change the function does not exist, so the check fails. Use three fixtures in the scratch folder: (i) a results file without the header; (ii) a header whose staged hash differs from its built hash; (iii) a header whose built hash differs from `-BuiltDllSha256`. Then a valid fixture must give no problems.

- [ ] **Step 2: Implement** the header, the staging, the copy launch, the `-t:Rebuild` build, the version read and the gate, in the order above. The `Get-GateProblems` function and the existing row checks stay as they are.

- [ ] **Step 3: Run the check again.** Expected: fixtures (i) to (iii) each return a problem, and the valid fixture returns none. Green.

- [ ] **Step 4: Commit.** `git commit -- tools/Invoke-RuntimeValidation.ps1`.

### Task 7: Retire ZLevels/1.1 and 1.3 (D7)

**Files:**
- Delete: the four tracked DLLs under `ZLevels/1.1/Assemblies/` and `ZLevels/1.3/Assemblies/`
- Modify: `ZLevels/LoadFolders.xml` (keep one `v1.7` block with `Common` and `1.7`; delete the `v1.1`, `v1.2` and `v1.3` blocks); `ZLevels/.gitignore` (delete line 252, `/1.2/Assemblies`); `README.md` (lines 9 and 80, which describe the 1.1, 1.2 and 1.3 folders); `CLAUDE.md` (line 14, the bullet that starts "Leave `ZLevels/1.1`", and the "Ship no game DLLs" bullet, which ends "ships as the older versions do")
- Regenerate: `AGENTS.md`

- [ ] **Step 1: Write the failing check.** `git ls-files ZLevels/1.1 ZLevels/1.2 ZLevels/1.3` lists four files. README lines 9 and 80 and CLAUDE.md line 14 still describe the 1.1, 1.2 and 1.3 folders. Red.

- [ ] **Step 2: Implement.** `git rm -r ZLevels/1.1 ZLevels/1.3`. Edit the files listed. The new CLAUDE.md bullet reads: `The 1.1 and 1.3 folders were retired on 2026-11-05 (ADR-0009). Their history stays in git. Only ZLevels/1.7 ships. ZLevels/LoadFolders.xml loads Common and 1.7.` Load `writing-for-agents` first. Then run `Check-Docs.ps1 -Fix`.

- [ ] **Step 3: Run the check again.** Expected: `git ls-files ZLevels/1.1 ZLevels/1.2 ZLevels/1.3` prints nothing. `Check-Docs.ps1` exits 0. Green. Loading is proven in Task 9.

- [ ] **Step 4: Commit.** `git commit -- <the paths in Files>`. Record the commit before this one, which is the last one with the folders, for ADR-0009.

### Task 8: Records (ADRs and review file)

**Files:**
- Create: `docs/adr/0006-...md` to `0009-...md`. Load `mattpocock-skills:domain-modeling` first. Use the ADR format of the existing files: a title sentence, then a paragraph.
- Update, not committed: `.artifacts/review/build-foundation.md` (in the main checkout, gitignored)

- [ ] **Step 1: Write the four ADRs.**
  - 0006: the mod targets RimWorld `1.7.4998 rev1299`, read from `Directory.Build.props`. The build stops on any other install. This answers D2.
  - 0007: release DLLs are tracked and written only by `tools/Publish-Mod.ps1`. Dev and validation builds write `bin/`. The output is deterministic and carries no machine paths. This answers D3 and D5 (option A1).
  - 0008: each validation run uses its own game copy. The copy holds copies of the mods, junctions for `Data`, `Rendering` and `vulkan`, and a hard link for `Assets.zip`. Steam supplies Harmony and Rimatomics. Runs are serial. This answers D6 (option C1), with the correction that runs cannot be parallel.
  - 0009: the 1.1 and 1.3 folders are retired. The last commit that holds them is recorded. This answers D7.
  Each ADR states its date as 2026-11-05.

- [ ] **Step 2: Update the review file.** Summary items 1-5 each get a status line, with the commit and the evidence. Add three corrections: the root `.gitignore` already exists; the parallel-run claim under C1 is wrong; the 1.1 and 1.3 folders hold four DLLs and no sources. D4-D7 each get the answer and the date. Add a "Decisions I made" section.

- [ ] **Step 3: Commit the ADRs.** Run `git diff --check` first. `git add docs/adr/0006-*.md docs/adr/0007-*.md docs/adr/0008-*.md docs/adr/0009-*.md`, then `git commit -- docs/adr`.

### Task 9: Validation proof (on the final commit)

- [ ] **Step 1:** `Get-Process -Name RimWorld`. If the game runs, stop and ask the user to quit it with the question tool. Do not kill it.
- [ ] **Step 2:** Snapshot the user's `Mods` folder (names and link targets).
- [ ] **Step 3:** Run `tools\Invoke-RuntimeValidation.ps1`, the core run. Expected: `Runtime validation passed. Evidence: .artifacts\runs\<id>`.
- [ ] **Step 4:** Run `tools\Invoke-RuntimeValidation.ps1 -SocialRelaxQueue`. Expected: the same line.
- [ ] **Step 5:** Check each run's `validation-results.txt`. Its identity header must be complete. Its `staged` hash must equal the SHA-256 of the committed `ZLevels/1.7/Assemblies/ZLevels.dll`, compared in uppercase. The Mods snapshot from Step 2 must match. The game copy must be gone after each success.
- [ ] **Step 6:** If a run fails, record the failure in the review file and diagnose it with `systematic-debugging`. Do not weaken a gate to get a pass. If Harmony does not load from the game copy, stop and report it. Do not link the user's `Mods` folder to fix it.

### Task 10: Review, pull request and merge

- [ ] **Step 1:** Load `superpowers:requesting-code-review`. Give one reviewer (a native Agent, with no model override) the diff `git diff origin/zlevels-1.7...HEAD` and the Global Constraints and Review Focus. Do not give it my conclusions. Fix each finding, or answer it in writing.
- [ ] **Step 2:** `git fetch`, then `git push -u origin chore/build-foundation`.
- [ ] **Step 3:** Open a pull request against `zlevels-1.7`. Not a draft. The title explains the problem. The body states the problem, the fix and the proof. Proof lines name run ids only, with no local paths and no personal names. The body ends with `Created with Claude Haiku 5.5 in Claude Code.` and then `🤖 Generated with [Claude Code](https://claude.com/claude-code)`. Call `link_pull_request`.
- [ ] **Step 4:** Merge when the gate passes. Run `git fetch` and recheck the head. Then `gh pr merge <n> --merge --match-head-commit <sha>`. No `--admin`.
- [ ] **Step 5:** Delete the scratch fake game, the check copy and the `wt2` worktree by literal path. Call `unwatch_pull_request` before handing back.

## Out of scope

Decision D1 (queued SocialRelax contention). Decision D4 (master or `zlevels-1.7` as the working line) is pending, and this plan works on `zlevels-1.7`. Refuel pause, run 032903. Findings 7 to 9 and 11 to 15 of the review. Findings 6 and 10 are covered by Tasks 5 and 6. The `LoadFolders.xml` 1.2 entry is covered by Task 7. `JobPatches - Copy.cs` and `FogOfWar1 (2).png` stay as they are. The two junctions in the user's game `Mods` folder stay until the user says otherwise. Historical docs (`validation-report.md`, plans, specs, `game-api-findings.md`) keep the build they were checked against. PR #4.
