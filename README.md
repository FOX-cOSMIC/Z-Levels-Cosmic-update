<img src="assets/icon.svg" width="64" height="64" alt="">

# rimworld modding

Created in [T3 Code](https://t3.codes).

## Z-Levels for RimWorld 1.7

This repository holds a port of **Z-Levels Beta** (by RamRod, package `foxcosmic.zlevels17`, formerly `ZLevels1.1`) to RimWorld 1.7.4998 rev1299, the Steam unstable branch. It is a GitHub fork of [RamRod6198/Z-Levels](https://github.com/RamRod6198/Z-Levels). The mod is in `ZLevels/`. Its 1.1, 1.2 and 1.3 folders are the upstream files, unchanged. `ZLevels/LoadFolders.xml` loads `Common` and `1.7` for RimWorld 1.7.

`docs/validation-report.md` lists what was run, what passed, and what was not tested.

### Provenance and licence

- Original project: [RamRod6198/Z-Levels](https://github.com/RamRod6198/Z-Levels), master at commit `7cf66ce417d8fc4928afa7b713f925e9a6494239`. This repository is a GitHub fork of it. The original files were imported under `ZLevels/` in commit `a1b072d`.
- The original project has no licence file, and this port grants no redistribution rights. The original author could not be reached before the Steam Workshop release. Keep the original author credit (RamRod) wherever the mod is shown.
- The Steam Workshop item is planned as a Private upload first. Its link is added here once it exists. The Steam item ships only the 1.7 files. No modpack or multiplayer compatibility is claimed.

### Requirements

- RimWorld 1.7.4998 rev1299, Steam unstable branch. Not for 1.6.
- Harmony, Steam Workshop item `2009463077`. The build reads its `1.7/Assemblies/0Harmony.dll`.
- Optional: Dubs Rimatomics, for the cooling module only. Rimatomics++ works only together with Dubs.
- .NET SDK 10.0.301 or a compatible 10.0 SDK, to build.
- Windows PowerShell, to run the validation harness.

### Build

```
dotnet build ZLevels/Source/ZLevels/ZLevels/ZLevels.csproj -c Release
```

Development builds write each project's `bin/Release/` folder, which git ignores. `tools/Publish-Mod.ps1` copies `ZLevels.dll`, `ZLevelsRimatomics.dll` and `0MultiplayerAPI.dll` into `ZLevels/1.7/Assemblies/`, the tracked release folder, and writes `build-info.txt` beside them with the source commit and the hashes. The `.pdb` and `.deps.json` files are build output and are git-ignored. The build ships no game DLL and no second copy of Harmony. `HarmonyPatches/JobPatches - Copy.cs` is excluded from the build.

If RimWorld or Harmony is installed somewhere else, override the two properties:

```
dotnet build ZLevels/Source/ZLevels/ZLevels/ZLevels.csproj -c Release -p:RimWorldDirectory="D:/Games/RimWorld" -p:HarmonyAssembly="D:/Games/steamapps/workshop/content/294100/2009463077/1.7/Assemblies/0Harmony.dll"
```

Defaults: `RimWorldDirectory` is set in `Directory.Build.props` to `C:/SteamLibrary/steamapps/common/RimWorld`. `HarmonyAssembly` is the Workshop copy under the same `steamapps` folder.

### Install

1. Copy the `ZLevels` folder into the game's `Mods` folder. A junction works too. The validation script runs from its own game copy and leaves your `Mods` folder alone.
2. In the mod list, enable Harmony and Z-Levels. `About/About.xml` declares Harmony as a dependency.
3. Start a **new colony**. The mod's description says it requires a new save. Existing saves were not opened or tested.

Stairs are in the Structure tab. Upstream's notes in `ZLevels/About/About.xml` about floor collapse and the natural-hole bug still apply.

### Validate in isolation

Close RimWorld first. The harness refuses to start while it is running. Then run:

```
powershell -ExecutionPolicy Bypass -File tools\Invoke-RuntimeValidation.ps1
powershell -ExecutionPolicy Bypass -File tools\Invoke-RuntimeValidation.ps1 -Visual
```

Parameters: `-Visual` takes screenshots. `-HarmonyAssembly <path>` overrides the Harmony DLL for the build. `-RimWorldDirectory <path>` sets the game folder. `-TimeoutSeconds` defaults to 900.

The script builds the mods and stages them in the run folder. It then copies the game into `%TEMP%\claude-scratch\zlevels-validation\<runId>\`, and that copy has its own `Mods` folder. The copy links each install folder except `Mods` with a junction, hard-links `Assets.zip`, and copies the other files. The script starts RimWorld from the copy, with `-savedatafolder` set to the new run folder. It writes nothing to the install or to your `Mods` folder. It then plays a scripted colony. The run keeps its files under `.artifacts/runs/<yyyyMMdd-HHmmss>/`: `Config/`, `staged/`, `Saves/`, `Player.log`, `validation-results.txt`, `power-samples.txt`, and `Screenshots/` with `-Visual`. The run uses its own config and saves, so the normal game's saves and mod list are not used.

The run passes when every scenario reports PASS, `core validation | PASS` is present, and the log has no ERROR line except duplicate-package messages for other mods. The last line is `Runtime validation passed. Evidence: <run folder>`. A passing run removes its game copy. A failing run keeps the copy and prints its path.

The first five lines of `validation-results.txt` form an identity header. They name the commit, whether the tree was dirty, the RimWorld version, and the SHA-256 of the built and the staged `ZLevels.dll`. A run fails if a header line is missing or malformed. It also fails if the staged hash differs from the built hash, or the built hash differs from `bin/Release/ZLevels.dll`.

Earlier versions of the script added two junctions to the game's `Mods` folder, `ZLevels-Local17` and `ZLevels-Validation`. The current script adds none. To remove leftover junctions, run `cmd /c rmdir "<RimWorld>\Mods\ZLevels-Local17"` and the same for `ZLevels-Validation`. Do not use `Remove-Item -Recurse` on a junction, because it can delete the contents of the folder it links to.

### Limitations

- Validated on RimWorld Core with Harmony and Z-Levels, in a new colony. No DLC and no other mods were active.
- Not tested: multiplayer (`ZLevels/Source/ZLevels/ZLevels/MultiplayerSupport.cs` is optional and unverified), existing saves, float-menu options that reach other levels, deliberate combat across levels, hauling or power after a reload, and walking or digging beyond the lower stairs.
- One run stopped on a refuel job error that was not explained. See the report.
- Some lower levels log `RandomAnimalSpawnCell_MapGen failed` warnings from the vanilla game, once per spawn call. The mod then places each animal on a walkable cell. In the one caves run whose animals were checked, none could reach the lower stairs. See the report.
- The lower stairs open into a pocket of one cell, walled in by natural rock. A colonist can use the stairs, but must dig out before walking anywhere else. Digging was not tested. See the report.
- Underground rivers are random, so not every run has one.
- Compiling is not playability. The validation report lists what was exercised in game.

### Layout

- `ZLevels/`: the mod. `Common/` holds the shared defs. `1.7/` holds the 1.7 assemblies and patches. `1.1`, `1.2` and `1.3` are upstream.
- `ZLevels/Source/ZLevels/ZLevels/`: the C# source.
- `tools/`: the validation mod (`RuntimeValidation/`) and the harness (`Invoke-RuntimeValidation.ps1`).
- `docs/`: game API findings, the validation report, and the design spec and plan under `docs/superpowers/`.
