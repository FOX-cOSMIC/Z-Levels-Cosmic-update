# The mod targets RimWorld 1.7.5011 rev24, compiled against the 1.7.5005 reference

ADR 0011 set the target to 1.7.5005 rev1082. Steam updated the unstable branch on this PC again on 2026-11-16 at 00:50 local time, in the middle of a regression, and every build after that stopped with "Installed RimWorld is 1.7.5011 rev24; this build expects 1.7.5005 rev1082". This ADR replaces the value in ADR 0011, under the same reading of D10: the mod targets the build installed here.

NuGet has no `Krafs.Rimworld.Ref` for 1.7.5011 yet; the newest is `1.7.5005-unstable`. `Directory.Build.props` now sets `RimWorldPackageVersion` to `1.7.5005-unstable` by hand, while `RimWorldVersion`, which the build checks against the install, names 1.7.5011 rev24. That is a change to the mechanism of ADR 0006: the compile reference can now lag the target. When the 1.7.5011 package exists, the override goes back to `$(RimWorldVersion.Split(' ')[0])-unstable`.

What changed in the game, checked on 2026-11-16:

- `Assembly-CSharp.dll`. A full decompile of each build (ilspycmd 11.1) differs in 11 files: `Properties/AssemblyInfo.cs`, `RimWorld/GlobalControlsUtility.cs`, `RimWorld/ResolutionUtility.cs`, `Verse/MemoryUsageUtility.cs`, `Verse/ModMetaData.cs`, `Verse/Prefs.cs`, and five files in `Verse.ProfilerUI`. They cover the memory readout, UI scaling, the frame profiler, and how a mod's per-version metadata is chosen. The mod source and the validation harness name none of these types.
- The engine files changed again (`RimWorld.dll`, `RimWorld.exe`, the `Waylith` assemblies, `UnityStubs.dll`, `PsdSharp.dll`, Steamworks, `RimWorld.deps.json`), and `Assets.zip`. No file under `Data/*/Defs` is dated after 2026-11-16 00:00 (checked by file date).
- Compiling against the 1.7.5005 reference is safe for the mod only while the members it uses exist in 1.7.5011. The decompile shows no change to them. The full regression on the new build stands for the engine files and for that.

Decided 2026-11-16. Reversal: none on this PC, because Steam keeps one build of the unstable branch.
