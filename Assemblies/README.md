# Assemblies

`Grandmaster21.dll` is built by `build.sh` and committed here, stamped with its build time and the
source commit it was built from. That stamp is logged at startup, in this shape:

```
[Grandmaster 21] <version> Beta (built <timestamp>, commit <sha>)  |  shooting: passive=True targeting=True
```

No concrete version or SHA is written down here on purpose: any example would be wrong again after
the next build. To read the values the shipped binary actually carries:

```bash
# from Player.log -- the startup line above
grep 'Grandmaster 21' Player.log

# or straight from the assembly, without launching the game
monodis --assembly Assemblies/Grandmaster21.dll | grep -i version
```

If the startup line ever reads `unstamped build`, the DLL was not produced by `build.sh`.

The commit in the stamp is **the last commit that changed real source** — not `HEAD`, and not
counting `Source/Gm21BuildStamp.cs`, which every build rewrites. Documentation and test commits do
not change the assembly, so stamping `HEAD` would make a current DLL look stale. That makes the
staleness check exact:

```bash
git log -1 --format=%h -- Source ':!Source/Gm21BuildStamp.cs'   # differs from the stamp? rebuild.
```

## Rebuilding

```bash
./build.sh /path/to/RimWorld/RimWorldWin64_Data/Managed /path/to/0Harmony.dll
```

On Linux/Mono the `netstandard 2.1` facade is required (`mono-devel` provides it at
`/usr/lib/mono/4.5/Facades/netstandard.dll`); without it `mcs` fails with CS0012 on
`System.ValueType`.

`CSC` may point to a compiler executable/wrapper and `FRAMEWORK_REFS` to a real framework
reference-assembly directory (including `Facades/netstandard.dll`). Defaults remain `mcs` and
`/usr/lib/mono/4.5`. Transcendent Crafting was also built with Roslyn and .NET Framework 4.7.2
reference assemblies; the game references must still be the real RimWorld/Unity/Harmony DLLs.

## Verifying a build

```bash
./tools/verify-real.sh /path/to/Managed /path/to/0Harmony.dll
```

Checks every reflectively-resolved member, every Harmony injection parameter name, the `Learn`
transpiler's IL pattern, and the progression suite against the real `SkillRecord`.

For all Transcendent Crafting tiers, after building the mod:

```bash
./tools/verify-transcendent.sh /path/to/Managed /path/to/0Harmony.dll /path/to/Mono.Cecil.dll
```

This additional suite requires Mono.Cecil 0.11.x. It accepts the same `CSC`/`FRAMEWORK_REFS`
overrides and a `RUNNER` executable (default `mono`, also supports .NET 8+ `dotnet`). It checks
policy, real API signatures/ingredient selection, XML and save-writing. It does not launch the
game or verify reload, hauling, needs, output placement or item transfers. See
[Transcendent craftsmanship](../Docs/MagicalCrafting.md) for results and the remaining runtime gate.

For Medicine 21, after building the mod:

```bash
./tools/verify-medicine.sh /path/to/Managed /path/to/0Harmony.dll /path/to/Mono.Cecil.dll [/path/to/RimWorld/Data]
```

It installs the real Medicine patches on the real vanilla methods and executes them, round-trips
state through the real Scribe saver and loader, and — given the game's `Data/` directory — audits the
Cure, tier and surgery rules against every vanilla Def. It does not launch the game. See
[Medicine 21](../Docs/Medicine21.md#13-tests).

## No RimWorld install?

`./tools/build-stubs.sh` compile-checks the older source groups and runs their offline logic suites
against reference stubs. It excludes `Source/Transcendent` and `Source/Medicine` and does **not**
validate Transcendent craftsmanship or Medicine 21, or produce a usable assembly — see `tools/stubs/README.md`.
