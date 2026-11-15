# The mod targets RimWorld 1.7.5005 rev1082, the build installed here

ADR 0010 set the target to 1.7.5000 rev1275, the build installed on this PC, because Lucas chose the installed build (D10). Steam updated the unstable branch on this PC on 2026-11-10 at 22:16 local time, after the last regression of PR 8 had ended. The installed build is now 1.7.5005 rev1082, and the build stopped with "Installed RimWorld is 1.7.5005 rev1082; this build expects 1.7.5000 rev1275". No 1.7.5000 install exists here any more. This ADR replaces the value in ADR 0010 and keeps the mechanism: the version is set once, in `Directory.Build.props` as `RimWorldVersion`, and the build stops on any other install. The compile reference is `Krafs.Rimworld.Ref` `1.7.5005-unstable`, which NuGet publishes.

What changed in the game, checked on 2026-11-15:

- `Assembly-CSharp.dll`. A full decompile of each build (ilspycmd 11.1) differs in two files only. `Properties/AssemblyInfo.cs` carries the new version numbers. `Verse.Steam/WorkshopItem.cs` adds a null check on a Workshop item's folder. The mod references neither.
- The update also replaced the engine and runtime files in the game folder (`RimWorld.exe`, `RimWorld.dll`, the `Waylith` assemblies, `coreclr.dll` and the other runtime files) and the language archives. The game's `Defs` folders did not change. A decompile cannot check the engine files, so the full regression on the new build stands for them.

Decided 2026-11-15. I read D10 as "the mod targets the build installed here", as this ADR's predecessor records it. Reversal: none on this PC, because Steam keeps one build of the unstable branch.
