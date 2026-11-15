# The mod targets RimWorld 1.7.4998 rev1299 and stops on any other install

The version is set once, in `Directory.Build.props`, as `RimWorldVersion`. The build reads `Version.txt` in the game folder before it compiles. If the install reports a different version, `RimWorld.targets` stops the build and names both versions. Every project reads the same property, and `tools/Check-Docs.ps1` checks the docs against it. A game update needs an edit in `Directory.Build.props`, and `tools/Check-Docs.ps1` names each doc line that still shows the old version.

The game updated after the version was pinned, and the old build did not notice. A build against the wrong game can compile and then fail in play. A stop at build time names the mismatch on the first run. To build against another install on purpose, set `RimWorldDirectory` to a matching install.

Decided 2026-11-05. This answers D2. Its value, 1.7.4998 rev1299, was replaced on 2026-11-08 by ADR-0010. The mechanism above still applies.
