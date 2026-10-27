# Z-Levels RimWorld 1.7 port

## Agreed scope

Lucas selected 1.7, authorized permanently switching Steam to unstable, and
asked to continue. Preserve existing gameplay. Validate using a new, disposable
colony, without opening existing saves or changing the user's mod list. Do not
publish to Workshop or claim arbitrary modpack/multiplayer compatibility.

## Ground truth

- Upstream: FOX-cOSMIC/Z-Levels-Cosmic-update, SHA
  7cf66ce417d8fc4928afa7b713f925e9a6494239, imported under ZLevels/.
- Upstream targets 1.3/.NET Framework 4.7.2. Unmodified build fails MSB3644.
  No automated test suite or upstream license file exists.
- Active game: C:/SteamLibrary/steamapps/common/RimWorld, 1.7.4990 rev1069,
  Steam build 26222807, unstable. Runtime and matching reference package target
  .NET 10; Waylith supplies Unity compatibility types.
- Harmony's installed Workshop package includes 1.7 assemblies.

## Design

Preserve namespace, package identity, XML types and subsystem boundaries.
Use SDK-style .NET 10 with pinned references and the existing assembly metadata.
Exclude the duplicate backup source. Put the new DLL in 1.7/Assemblies and add
explicit 1.7 load mapping; retain all historical directories unchanged. Do not
ship game reference assemblies or a second Harmony. Retain attribution; local
porting does not imply redistribution permission.

Use compiler/runtime evidence and targeted inspection of installed 1.7 code.
Retain level creation, stairs, cross-map jobs/pathfinding, lower-level rendering,
roofs/floors, power, weather, combat and persistence. Unresolved behavior is a
failure to report, not a reason to silently disable patches or hide errors.
Keep multiplayer integration optional and explicitly unverified.

## Acceptance

Build Release against 1.7. Load the actual port in 1.7 with Harmony and isolated
savedata. Resolve mod-caused definition, patch and startup errors. Exercise a
disposable colony: both directions of stairs, movement, cross-level work and
hauling, rendering, power and save/reload. Record evidence, failures and untested
cases accurately; compilation alone is not playability. Deliver reproducible
build/install/test instructions and limitations.
