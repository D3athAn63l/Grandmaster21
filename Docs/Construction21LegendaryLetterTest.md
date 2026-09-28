# Construction Grandmaster Legendary notifications — PR #12 extension

Mod Settings now has two independent controls, both **ON by default**:

- **Crafting Grandmaster Legendary notifications:** Crafting 21 recipe work.
- **Construction Grandmaster Legendary notifications:** Construction 21 frame-completed buildings.

Turning either off hides its corresponding vanilla Legendary Work letter. Items/buildings remain
Legendary. Masterwork letters and unrelated notifications remain unchanged. Changes apply to the
next completion without restart; old configs load the new setting ON. This is mod configuration,
with no save-game state or uninstall cleanup.

**Crafting runtime result: PASS.** The owner confirmed Legendary + letter with Crafting ON, and
Legendary + no letter with Crafting OFF in a real modded game. Its implementation is unchanged.
Construction's new setting still needs the focused runtime checks below.

## Focused owner checklist — Construction pending

1. Boot the latest build; confirm its current source stamp and no new red errors.
2. Construction ON: a learned Construction 21 pawn builds a bed, dresser or end table. The result
   is Legendary and the Legendary Work letter appears.
3. Construction OFF: the same pawn builds another quality-capable object. It remains Legendary,
   with no Legendary Work letter.
4. Switch Crafting OFF/ON: it must not affect Construction. Switch Construction OFF/ON: it must
   not affect Crafting. This is a small independence check, not a full Crafting retest.
5. Construction 20 still produces the usual Masterwork and its letter where applicable.
6. Turn Construction back ON: the next Legendary construction letter returns without restart.
7. Trigger an unrelated letter/message; it remains visible.

**Cube sculptures remain vanilla.** They are intentionally excluded and need no runtime validation
for this feature. Transcendent Crafting and every skill's gameplay mechanics are unchanged.

## Developer audit: supplied real RimWorld 1.6 IL

`RimWorld.Frame.CompleteConstruction(Verse.Pawn worker)` is instance `void`:

| IL | Operation |
|---|---|
| 00ff–0104 | `ThingMaker.MakeThing(entityDefToBuild, Stuff)` stored in Thing local 5 |
| 0113–011a | Get that Thing's CompQuality |
| 0120–0127 | `GenerateQualityCreatedByPawn(worker, SkillDefOf.Construction, true)` |
| 0138 | SetQuality on that CompQuality |
| 013d–0140 | `ldloc.s 5; ldarg.1; SendCraftNotification(Thing, Pawn)` |
| 0161 onward | Art author credit, materials provenance, spawn, records, tales and other completion work |

There is exactly **one** notification call, using the exact new Thing and original worker.
Local 5 has only its initial null and MakeThing assignment. Thus the transpiler replaces only the
static notification call's operand with `SendConstructionNotification(Thing, Pawn)`. It preserves
all opcodes, stack shape, labels and exception blocks, and creates no context, per-item state,
thread-local frame or polling. Ambiguous/missing/changed call shape leaves the input unchanged and
warns; construction letters remain vanilla.

The wrapper suppresses only if the Construction setting is OFF, the actual completed Thing's
cached CompQuality is **Legendary**, and `Gm21.IsGrandmaster(worker, SkillDefOf.Construction)` is true
(stored level, never aptitude-adjusted). Otherwise it invokes the normal patched notification
method. It writes no quality, gameplay or save fields. Crafting's existing thread-local context,
late prefix and void finalizer have not been changed or shared.

`JobDriver_BuildCubeSculpture.PlaceAndFinish()` instead obtains a sculpture through
`GetSculptureThing`, spawns it into local 0, uses `JobDriver.pawn`, rolls Construction with
`forcedInspired=false`, and updates `Hediff_CubeInterest` / the CubeSculpting mental state. This
Anomaly-specific path is separate from ordinary Frame completion and is conservatively excluded.
Its original notification call remains untouched.

**Compatibility:** suppressed Construction calls do not enter SendCraftNotification at all, so
third-party Harmony hooks on that method also do not run for those calls. Forwarded calls execute
that patched method normally. This avoids adding another shared-method suppression prefix or
changing Crafting's existing priority. Mods replacing Frame's call, changing the verified IL shape,
or sending their own letters are outside the guarantee. LetterStack is never patched by the mod.

## Automated results — 2026-09-28

Built with the established Roslyn 4.8 / Mono framework-reference process against the supplied
real RimWorld, Unity and Harmony 2.4.1 DLLs. Version remains **0.13.0 Beta**; the DLL/stamp build
commit follows its source commit, as in the existing PR.

| Suite | Result |
|---|---|
| Crafting + Construction notifications | 166 PASS, 0 FAIL |
| Runtime targets / Harmony parameter names | 271 PASS, 0 FAIL, 0 SKIP |
| Existing Learn IL audit | PASS |
| Unmodified-DLL live binding | 39 applied, 0 failed, 6 BLOCKED |
| Finalizer semantics / progression | 15 / 26 PASS, 0 FAIL |
| Aspirant | 91 PASS, 0 FAIL |
| Medicine | 344 PASS, 0 FAIL, 0 BLOCKED |
| Transcendent Crafting | 604 PASS, 0 FAIL, 0 BLOCKED |
| Offline core / Shooting / Melee | 58 / 74 / 295 PASS, 0 FAIL |
| Optional vanilla Data audit | NOT RUN: Data folder not supplied |
| New Construction gameplay | NOT RUNTIME TESTED |

The existing five Unity-only binding blocks remain (two SkillUI, Projectile.TickInterval and two
TendDuration targets). Frame adds one: its static initializer loads rendering assets through the
Unity player. The notification suite independently binds the real Frame completion method using
a **test-only temporary DLL** whose three-asset rendering initializer is omitted. The preparer
checks that every other method body, local and exception handler is unchanged; the IL audit uses
the pristine DLL. No prepared game DLL or third-party dependency is shipped.

The suite executes real quality rolls, SetQuality, the wrapper bound from the live patched Frame
call, vanilla notification code and ModSettings persistence. Letter reception/text and art are
fixture observations; map construction, material consumption, spawning, jobs and UI are not run
end to end. Exact one-operand IL comparison guards against changing those mechanics.

All original Crafting checks remain, with strict expected inventories extended for the one new
setting, checkbox and wrapper call. Crafting behavioral, nested-frame, thread-local, quality and
Harmony-order checks run again with Construction installed and OFF. New checks cover both setting
round trips and all four toggle combinations, stored-level/aptitude boundaries, other-mod Legendary
and Masterwork, direct/nested unrelated calls, exception isolation, live toggling, cube exclusion,
and fail-open IL drift. These headless results do not replace the owner checklist.
