# The mod targets RimWorld 1.7.5000 rev1275, the build installed here

ADR 0006 set the target to 1.7.4998 rev1299. This ADR replaces that value and keeps the mechanism: the version is set once, in `Directory.Build.props` as `RimWorldVersion`, and the build stops on any other install.

The Steam unstable branch on this PC has 1.7.5000 rev1275 installed. No 1.7.4998 rev1299 install exists here, so a build against 4998 cannot run on this PC, and the mod's DLLs cannot be published from here. The earliest run folder whose log names 1.7.5000 rev1275 is `20261105-232257`. The latest run folder whose log names 1.7.4998 rev1299 is `20261105-231837`.

The checks in `docs/game-api-findings.md` were made against the 4998 decompile. On 2026-11-08 the names were checked again against 1.7.5000 rev1275, and the note at the top of that file says what was checked and what was not.

Decided 2026-11-08. This answers D10. Lucas chose the installed build, the recommended option.
