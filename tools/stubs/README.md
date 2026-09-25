# Reference stubs (test-only — never shipped)

Hand-written stand-ins for `Assembly-CSharp.dll`, `UnityEngine.dll` and `0Harmony.dll`, used by
`tools/build-stubs.sh` to compile-check the mod and run the offline harnesses on a machine with no
RimWorld install.

`Rim.cs` covers the core skill/quality/UI surface; `RimMelee.cs` covers the combat surface the
Melee Grandmaster package consumes — `IntVec3`, the grid and sight helpers, equipment and stance
trackers, projectiles, stats, faction relations, flecks, sounds and the melee verbs.

A stub that is subtly *wrong* is worse than one that is missing, because it invents failures the
game would not have. `GenHostility.HostileTo` originally compared faction identity here; the real
one compares faction RELATIONS, and the difference made an allied faction read as hostile. It now
mirrors the real rule.

**These are not a substitute for a real build.** Signatures here were written to mirror RimWorld
1.6, but they are an approximation: a member whose real signature differs would compile here and
fail at runtime. Anything built against these stubs must never be shipped as `Assemblies/`.

A few members carry extra `*Stub` fields (`SkillRecord.aptitudeStub`, `BodyPartRecord.healthStub`,
`Thing.statsStub`, `PawnCapacitiesHandler.levelsStub`, `Pawn_MeleeVerbs.attacksStub`,
`Projectile.TicksToImpactStub`, `Rand.ForcedValueStub`, `Translator.KnownKeys`,
`DefDatabase<T>.Registry`) that do not exist in RimWorld. They exist only so tests can set up state
the real game would build from XML, pawn generation and its own RNG. They are purely additive, so
they cannot mask a signature mismatch in the members the mod actually calls.

Every signature in `RimMelee.cs` was taken from the real 1.6 assembly's metadata and is
independently re-checked against the shipped game by `Tests/VerifyRuntimeTargets.cs`, which is what
actually closes the "the stub could be wrong" gap.

## `SteamworksShim.cs` — a different kind of shim

Not part of the stub build. `tools/verify-medicine.sh` compiles it into its throwaway test
directory as `com.rlabrecque.steamworks.net.dll` so that vanilla `Verse.ParseHelper` can initialise
headless: its static constructor registers a parser for `Steamworks.PublishedFileId_t`, and every
Scribe **load** goes through `ParseHelper`. It contains that one struct and its constructor — the only
member `ParseHelper` touches — and is only ever used alongside the REAL game assemblies. Steamworks.NET
itself ships with the game launcher, not in `Managed/`. It is never shipped and never referenced by
the mod.
