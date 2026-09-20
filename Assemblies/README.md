# Build the assembly before playing

This folder is intentionally empty of binaries.

`Grandmaster21.dll` is **not** checked in. The binary that used to live here was built from the
Alpha source and no longer matches this repository; leaving it would have meant the mod silently
ran old rules (demotable Grandmasters, a generated-pawn toggle, no Shooting Grandmaster) with
nothing to indicate anything was wrong. A missing assembly fails loudly instead, which is the
safer of the two.

## Building

```bash
./build.sh /path/to/RimWorld/RimWorldWin64_Data/Managed /path/to/0Harmony.dll
```

That writes `Assemblies/Grandmaster21.dll` and stamps it with the build time and git commit. The
stamp is logged at startup:

```
[Grandmaster 21] 0.9.0 Beta (built 2026-09-20T12:00Z, commit 1a2b3c4)  |  shooting: passive=True targeting=True
```

If that line ever reads `unstamped build`, the DLL was not produced by `build.sh`.

## No RimWorld install?

`./tools/build-stubs.sh` compile-checks the source and runs the offline test suites against
reference stubs. It does **not** produce a usable assembly — see `tools/stubs/README.md`.
