# Level-20 aspirant learning — 0.13.0 Beta

## Architecture and verified vanilla behavior

The existing `SkillRecord.Learn` prefix still selects positive XP on a non-disabled skill whose
stored `levelInt` is exactly 20. It calls `Gm21AspirantLearning.Capture`, which adds ordinary XP,
then at most one Insight bonus, to the existing **double** `GrandmasterStore`. The existing Learn
postfix performs the sole promotion check. Persistence, level-21 permanence and capstones are
unchanged. There is no aspirant flag, component, tick polling or Parametric dependency.

Audited the supplied RimWorld 1.6 implementation and IL:

- `LearningSaturatedToday` is `xpSinceMidnight > 4000f` (strictly greater).
- Non-direct `LearnRateFactor` composes passion, GlobalLearningFactor and, for Animals,
  AnimalsLearningFactor, then multiplies by **0.2** when saturated.
- Direct learning skips those stats and saturation; `ignoreLearnRate` uses raw XP.
- `DebugSettings.fastLearning` returns 200 early.
- Vanilla Learn updates `xpSinceMidnight` only when non-direct.

A thread-local, exception-safe scope wraps only the GM bookkeeping rate query. A targeted
LearnRateFactor transpiler replaces the saturation condition immediately guarding the verified
0.2 multiplication. Only the same skill at stored 20 receives the exemption inside that scope.
The real rate method and other mods' rate patches still run. Vanilla's own Learn invocation,
other skills, counters, saturation getter and UI remain outside that scope. In particular, an
additive rate postfix is not accidentally multiplied by five by dividing the final rate by 0.2.

If the exact branch cannot be found once, the transpiler warns and leaves its input unchanged;
Insights are disabled and existing ordinary XP banking/promotion remain. Mods that replace the
rate body may therefore prevent this feature. Mods replacing Learn, skipping its original body,
or independently overriding saturation require integration testing; universal compatibility is
not claimed. The pre-existing double rate query (GM bookkeeping plus vanilla) remains.

Stored 19 with positive aptitude is ineligible; stored 20 with negative aptitude is eligible.
Level-20 decay remains vanilla, preserves the bank, and pauses support below 20. A Learn call
that begins at 19 and reaches 20 keeps the old capture timing: support starts on the next event.

## Insight formulas and RNG

For finite positive eligible XP `x` (raw XP if ignoreLearnRate, otherwise XP times the resolved
rate without vanilla saturation):

```
p = 1 - exp(-x / 100000)
bonus = configuredGrandmasterXpRequirement * 0.05
```

At the default one-billion requirement, each Insight adds **50,000,000 GM XP**. Ordinary XP is
banked first, the bonus second, and the existing postfix can promote in the same invocation.
Tiny probabilities use a third-order expansion to avoid subtraction cancellation. Nonpositive,
NaN and infinite XP/rates cannot trigger Insights or poison the bank. Bonus arithmetic is double.

The local draw hashes stable pawn ID, skill defName and the new saved GM XP balance (including
this event's ordinary XP), using FNV-1a and SplitMix64 mixing, then maps 53 bits into [0,1).
No global Rand consumption, tick-only seed, wall clock, new saved seed or invocation counter is
needed. Different positive increments in one tick produce different keys; reloading the same
saved identity and balance reproduces the next draw. At extreme sub-ULP increments, double
precision may not advance the bank or seed; this is the existing numeric storage limit.

For independent draws, splitting total eligible XP into chunks preserves the probability of
**at least one** Insight: the product of no-event probabilities is `exp(-sum(x)/100000)`.
This is not a claim that a large single event and many smaller events yield identical bonus
counts: the specified mechanic allows at most one bonus per invocation. There is no fixed
per-call chance; zero and negative events do not roll. The hash is a deterministic local
pseudorandom strategy, not an adversarially unpredictable generator.

Feedback is one localized positive message for a living, spawned player colonist. NPCs, animals
and world pawns remain silent. Its presentation runs in Rand.PushState/PopState, following the
existing artifact-feedback convention. Dev mode includes **Force one Insight (first level-20
skill)** under Grandmaster 21: it adds one XP through real Learn and forces only that event's
Insight. It neither directly promotes nor exposes a normal gameplay control.

## Automated verification (2026-09-27)

Built with Roslyn 4.8, .NET Framework 4.7.2 reference assemblies and the supplied real RimWorld,
Unity and Harmony DLLs, using `build.sh` and its source-commit stamp. No external DLLs are shipped.
Headless execution uses Mono. A test-only Steamworks identity shim supplies missing launcher
metadata; it is not part of the mod. The aspirant harness controls environment/stat providers,
presentation and selected RNG draws, while running real Learn, LearnRateFactor, saturation,
GM patches, promotion and native Scribe save/load.

| Check | Result |
|---|---|
| Release build | PASS, zero warnings/errors |
| `tools/verify-aspirant.sh` | 91 PASS, 0 FAIL |
| Offline core / Shooting / Melee | 58 / 74 / 295 PASS, 0 FAIL |
| Real runtime targets / parameter names | 258 PASS, 0 FAIL, 0 SKIP |
| Existing Learn transpiler IL audit | PASS |
| Live Harmony patch groups | 38 applied, 0 failed, 5 BLOCKED |
| Real finalizer exception semantics | 14 PASS, 0 FAIL |
| Existing real progression harness | 26 PASS, 0 FAIL |
| Crafting / Transcendent suites | 604 PASS, 0 FAIL, 0 BLOCKED |
| Medicine suite | 344 PASS, 0 FAIL, 0 BLOCKED |
| Optional Medicine vanilla Data audit | NOT RUN: game Data folder not supplied |
| Gameplay | NOT RUNTIME TESTED |

The five blocked patch groups are both SkillUI patches, Projectile.TickInterval, and two
HediffComp_TendDuration patches. Their type initialization requires Unity content/player APIs.
The projectile shader-loading failure was reproduced with unchanged main's shipped DLL at
`eb95ae4`. PatchAllTest now recognizes that precise missing Unity resource call as an environment
limit. This does not turn those bindings into passes.

Aspirant coverage includes passions/Animals/stats, additive and multiplicative modded rates,
direct/ignoreLearnRate/debug paths, sibling skills, stored-level/aptitude eligibility, scoped
exception cleanup, decay/resumption, invalid inputs, exact probability composition, local RNG
isolation/replay, one-shot feedback, threshold crossing, no level 22, double precision and native
Scribe round trips, no new save state, uninstall clearing and missing-branch fail-safe behavior.

## Focused runtime checklist — pending

1. Load a normal save; confirm clean startup and both learning transpilers applied. Check existing
   Shooting, Melee, Crafting and Medicine features still initialize.
2. Train a learned-19 skill past daily saturation: confirm ordinary vanilla behavior and no GM bank
   or Insight. Repeat with positive aptitude displaying 20.
3. Train learned 20 before/after saturation: GM XP should retain its rate while the ordinary daily
   counter/display stays vanilla. Check passion, learning modifiers and a second skill separately.
4. Allow 20 to decay to 19: bank retained, support paused. Return to learned 20 and verify resumption.
5. Use the dev one-shot on a colonist with a learned-20 skill: one ordinary XP plus 5% requirement,
   one readable positive Insight message. Repeat on a non-colonist and verify no message.
6. Place a learned-20 bank within one bonus of the requirement and force one Insight. Confirm the
   usual immediate promotion and message, no duplicate promotion, and the absolute cap at 21.
7. Verify an existing 21 retains permanence, negative-XP protection, aptitude behavior and capstone.
8. Save/reload before and after an Insight and promotion. Confirm exact bank/level retention, no
   load-time bonus/message and no new errors. Check Prepare Save for Uninstall on a disposable save.

Review scope: no passion system, capstone, generation, quality, permanence, or persistent-store
changes; no polling, Parametric references, new dependency or general saturation override.
