# Reference stubs (test-only — never shipped)

Hand-written stand-ins for `Assembly-CSharp.dll`, `UnityEngine.dll` and `0Harmony.dll`, used by
`tools/build-stubs.sh` to compile-check the mod and run the offline harnesses on a machine with no
RimWorld install.

**These are not a substitute for a real build.** Signatures here were written to mirror RimWorld
1.6, but they are an approximation: a member whose real signature differs would compile here and
fail at runtime. Anything built against these stubs must never be shipped as `Assemblies/`.

A few members carry extra `*Stub` fields (`SkillRecord.aptitudeStub`, `BodyPartRecord.healthStub`,
`Translator.KnownKeys`, `DefDatabase<T>.Registry`) that do not exist in RimWorld. They exist only
so tests can set up state the real game would build from XML and pawn generation. They are purely
additive, so they cannot mask a signature mismatch in the members the mod actually calls.
