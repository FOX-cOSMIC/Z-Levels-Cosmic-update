# Release DLLs are tracked and written only by the publish script

The mod's release DLLs stay in git, in `ZLevels/1.7/Assemblies/`. Only `tools/Publish-Mod.ps1` writes them. The script refuses to run while the mod sources have uncommitted changes. It rebuilds `ZLevels.csproj` and `ZLevelsRimatomics.csproj` with `-t:Rebuild`, copies the three DLLs, and writes `build-info.txt`. That file names the source commit, the RimWorld version, the .NET SDK version, and the SHA-256 of each DLL. Development and validation builds write to each project's `bin/Release/` and never touch the tracked files.

The output depends only on the sources and the toolchain. Three settings keep it that way. `PathMap` in `Directory.Build.props` removes the checkout path. `ContinuousIntegrationBuild` is on. `EnableSourceLink` is off in both mod projects. Without that last setting, the SDK writes the HEAD commit into the PDB, and the DLL changes on every commit. The script also refuses a DLL that holds the user's profile path or a drive path.

Decided 2026-11-05. This answers D3 and D5, option A1.
