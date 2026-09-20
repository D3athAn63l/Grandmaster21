# Assemblies

`Grandmaster21.dll` is built by `build.sh` and committed here, stamped with its build time and the
source commit it was built from. That stamp is logged at startup:

```
[Grandmaster 21] 0.9.0 Beta (built 2026-09-20T13:52Z, commit de860da)  |  shooting: passive=True targeting=True
```

If that line ever reads `unstamped build`, the DLL was not produced by `build.sh`.

The commit in the stamp is **the last commit that touched `Source/`**, not `HEAD` — documentation
and test commits do not change the assembly, so stamping `HEAD` would make a current DLL look
stale. That makes the staleness check exact:

```bash
git log -1 --format=%h -- Source/     # differs from the stamp? rebuild.
```

## Rebuilding

```bash
./build.sh /path/to/RimWorld/RimWorldWin64_Data/Managed /path/to/0Harmony.dll
```

On Linux/Mono the `netstandard 2.1` facade is required (`mono-devel` provides it at
`/usr/lib/mono/4.5/Facades/netstandard.dll`); without it `mcs` fails with CS0012 on
`System.ValueType`.

## Verifying a build

```bash
./tools/verify-real.sh /path/to/Managed /path/to/0Harmony.dll
```

Checks every reflectively-resolved member, every Harmony injection parameter name, the `Learn`
transpiler's IL pattern, and the progression suite against the real `SkillRecord`.

## No RimWorld install?

`./tools/build-stubs.sh` compile-checks the source and runs the offline logic suites against
reference stubs. It does **not** produce a usable assembly — see `tools/stubs/README.md`.
