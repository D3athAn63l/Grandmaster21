# Grandmaster 21

**RimWorld 1.6** — skills normally end at 20. This mod adds exactly one more level: **21, Grandmaster**.

Level 21 cannot be randomly generated. It must be earned by accumulating an enormous amount of
experience *after* a pawn has already reached level 20. Level 21 represents Grandmaster mastery
and is the only pawn skill level capable of producing **Legendary**-quality work.

Level 21 is the absolute hard maximum. Level 22 cannot occur.

**Grandmaster permanence:** once earned, level 21 does not decay and cannot be removed by ordinary
gameplay. The only supported way to turn a Grandmaster back into a level-20 pawn is the explicit
[Prepare Save for Uninstall](#removing-grandmaster-21-safely) maintenance action in Mod Settings.

---

## How it works

### Earning Grandmaster

A pawn must first reach level 20 normally. From then on, every point of XP they earn in that
skill is banked toward Grandmaster — **1,000,000,000 XP** by default, configurable in Mod Settings.

The banked XP is *real earned XP*, not a counter incremented per action. It is the same effective
value vanilla would have applied, obtained by calling RimWorld's own `LearnRateFactor`, so
passion, `GlobalLearningFactor`, `AnimalsLearningFactor`, implants, genes, traits and the daily
learning-saturation penalty all continue to matter exactly as they normally do.

When the threshold is met, the skill is promoted 20 → 21.

To get a feel for the default: a pawn earning the maximum full-rate 4,000 XP per day with a burning
passion needs on the order of **many in-game centuries**. It is meant to be close to unreachable
without deliberate, long-term investment. Lower it in Mod Settings if you want it achievable.

### Keeping Grandmaster

Level 21 is an achieved state, not a level the pawn is currently holding on to. Once stored, it is
immune to every ordinary way a skill level can fall:

| Path | Result at level 21 |
|---|---|
| Vanilla periodic skill decay (`SkillRecord.Interval`) | No effect — decay is skipped entirely |
| Negative XP from any source (`Learn` with `xp < 0`) | Swallowed; no XP is subtracted |
| `skill.Level = 20` (or `10`, `5`, `0`, `-1`) from any mod | Ignored; stays 21 |
| `skill.Level = 22` (or higher) | Ignored; stays 21 |
| Negative aptitude from a gene, trait or hediff | Stored level unchanged; still displays 21 |

Skills at **level 20 and below are untouched** and decay exactly as in vanilla.

There is no setting for this. The earlier "Grandmaster skills never decay" toggle has been removed:
permanence is now part of what level 21 means.

### Aptitude and Grandmasters

RimWorld 1.6 keeps three separate numbers, and the mod treats them differently:

| Number | Where it lives | What the mod does |
|---|---|---|
| **Stored level** | `SkillRecord.levelInt` | The record of achievement. Only the mod's own authorised paths ever write 21 here. |
| **Displayed level** | `GetLevelForUI()` → the Skills tab, `LevelDescriptor` | A stored 21 **always displays as 21 ★ Grandmaster**, whatever the aptitude. |
| **Mechanical level** | `GetLevel(true)`, i.e. the `Level` property → stat workers, work speed, success chances | Aptitude still applies normally. |

So a Grandmaster with **−5 aptitude** shows `21 — Grandmaster` in the UI and performs at an
effective skill of 16, exactly as a level-20 pawn with −5 aptitude performs at 15. Aptitude keeps
its ordinary mechanical bite; what it cannot do is rewrite the fact that the pawn reached
Grandmaster.

Three consequences worth stating explicitly:

* **Legendary crafting is not lost to aptitude.** Quality is gated on the stored level, so a
  Grandmaster with negative aptitude still produces Legendary work.
* **Positive aptitude cannot manufacture a Grandmaster.** A level-20 pawn with +5 aptitude is not a
  Grandmaster, displays at most 20, and cannot make Legendary items.
* **Aptitude never touches the stored level**, so it can never trigger a demotion.

### Quality bands

Pawn-created quality becomes deterministic by skill band:

| Skill | Result |
|------:|--------|
| 0–3   | Awful |
| 4–7   | Poor |
| 8–10  | Normal |
| 11–13 | Good |
| 14–16 | Excellent |
| 17–20 | Masterwork |
| **21** | **Legendary** |

**Only level 21 can produce Legendary.** A level-20 pawn tops out at Masterwork, always.

This applies to pawn-created quality only: crafting (`GenRecipe.PostProcessProduct`),
construction (`Frame.CompleteConstruction`) and the Anomaly cube sculpture.

#### With deterministic quality turned off

Turning deterministic quality off restores vanilla quality randomness for levels 0–20, but
**Legendary remains exclusive to level 21**:

| Skill | Deterministic **on** | Deterministic **off** |
|------:|---|---|
| 0–20 | Fixed band from the table above | Vanilla random roll, **capped at Masterwork** |
| **21** | **Legendary** | **Legendary** |

Level 21 is decided before the setting is even consulted — the setting governs how levels 0–20 are
rolled, it is not a switch for the Grandmaster rule. Inspired Creativity, the Ideology Production
Specialist offset and any other quality bonus can raise a level-20 pawn's roll, but never past
Masterwork.

### What is *not* changed

Legendary items from **quests, traders, map generation, special rewards, existing saves and other
mods remain Legendary.** Nothing is ever downgraded. Pawn *gear* generation
(`PawnGenerator.PostProcessGeneratedGear`) is untouched, so a raider can still spawn carrying a
Legendary weapon — they just can't *make* one.

---

## Settings

| Setting | Default | Notes |
|---|---|---|
| Grandmaster XP requirement | 1,000,000,000 | Range 1,000 – 1,000,000,000,000. Invalid input is clamped; NaN resets to default. |
| Deterministic quality | On | Off restores vanilla's random roll for levels 0–20, capped at Masterwork. Level 21 is Legendary either way. |
| Show Grandmaster progress | On | Tooltip progress line plus a ★ beside a Grandmaster skill. |
| Clamp generated pawns to 20 | On | Should normally stay on — it is what makes 21 mean something. |

Grandmaster decay is **not** a setting. Level 21 never decays; there is no option to restore
vanilla's behaviour for it.

The bottom of Mod Settings also holds a **Maintenance / Save cleanup** section containing the
`Prepare Save for Uninstall` action — see [Removing Grandmaster 21 safely](#removing-grandmaster-21-safely).

---

## Why randomly generated pawns can never be 21

This is structural, not a heuristic, and it is worth explaining because it is the heart of the mod.

In RimWorld 1.6 the two things are already separated by code path:

* **Pawn generation assigns levels through the `SkillRecord.Level` property setter.** Verified
  callers: `PawnGenerator.GenerateSkills`, `CreepJoinerUtility.ApplySkillOverrides`,
  `TraitUtility.ApplySkillGainFromTrait`.
* **Save/load restores levels by writing the `levelInt` field directly** via `Scribe_Values`,
  never touching the property.

So the mod leaves the property setter clamped at 20 and leaves the field alone. Every generated
pawn — colonist, raider, visitor, trader, refugee, quest pawn, ancient, faction leader, slave,
prisoner, world pawn, and any modded faction pawn that uses the normal pipeline — is capped at 20
no matter how extreme its backstory, trait, gene or faction bonuses. An existing Grandmaster
loading from a save is restored untouched, because loading never calls the setter.

There is no need to guess whether a pawn is "new" or "being loaded".

A second subtlety: `SkillRecord.Level` is not the stored level — it is
`Clamp(levelInt + Aptitude, 0, 20)`, and **Aptitude comes from genes, traits and hediffs**, all of
which can be randomly rolled. The mod therefore raises that ceiling to 21 *only when the stored
`levelInt` is already 21*. A lucky gene can never manufacture a Grandmaster, and Legendary quality
is gated on the stored level for the same reason.

### Known limitation

A mod that writes `SkillRecord.levelInt` **directly** (rather than through the `Level` property)
could set 21 on a generated pawn. Intercepting every field write would require patching unrelated
internals and would break save loading and Grandmaster restoration, so it is deliberately not done.
No vanilla generation path does this. `GameComponent_PawnDuplicator.CopySkills` does copy
`levelInt` directly — so duplicating a Grandmaster (Anomaly) copies level 21, though not the
banked Grandmaster XP of unfinished skills.

The same limitation runs in the other direction: a mod that writes `levelInt` directly can also
*demote* a Grandmaster, because the permanence guarantee is enforced on the `Level` property setter
and on `Learn`, not on the raw field. Mods that replace `SkillRecord` progression wholesale, or that
substitute their own quality generation, are outside what the mod can protect.

Dev Mode and save/XML editing are outside the threat model by design; developer tools may set 21
deliberately. Dev Mode actions are provided for both directions — *Promote all level 20 skills to
Grandmaster* (still refuses if the `Learn` patch did not apply) and *Demote Grandmaster skills to
20*, which uses the same authorised path as the uninstall cleanup.

---

## Storage and save compatibility

Grandmaster progress is stored per `SkillRecord` as a **`double`** and written from a postfix on
`SkillRecord.ExposeData`, so it lives inside the pawn's own save data. It therefore survives
save/load, caravans, map transitions, despawn/respawn and world-pawn conversion automatically,
and there is no global registry that could leak state when a map is removed.

`double` is required, not a preference: `xpSinceLastLevel` is a `float`, and a float's ULP at 1e9
is **64** — a 1 XP increment at that magnitude is silently discarded. A `double` (ULP ≈ 2.4e-7 at
1e9) keeps every increment. `Verse.ParseHelper` registers a `System.Double` parser, so
`Scribe_Values.Look<double>` round-trips correctly.

Loading an older save works. Existing level-20 pawns simply start at Grandmaster XP 0.

---

## Removing Grandmaster 21 safely

> **Do not remove the mod while a save still contains level 21 skills.**

Unknown save elements are ignored by RimWorld, so banked Grandmaster XP is discarded harmlessly.
A stored level of **21 is a different matter**: vanilla `SkillRecord.ExposeData` reads it straight
back into `levelInt`, above vanilla's own cap. Without the mod's `Learn` patch, vanilla then
increments 21 → 22 and snaps the skill back to 20 the first time it gains XP, and
`LevelDescriptor` has no entry for 21 in the meantime.

Mod Settings therefore provides an explicit cleanup action, at the bottom under
**Maintenance / Save cleanup**:

### `Prepare Save for Uninstall`

1. **Load the save** you intend to clean. The button refuses to run from the main menu, with a
   message telling you to load a game first — it never reports a cleanup it did not perform, and
   it does not touch any global state.
2. Open **Mod Settings → Grandmaster 21**.
3. Scroll to **Maintenance / Save cleanup** and click **Prepare Save for Uninstall**.
4. **Confirm** in the dialog. (Cancelling changes nothing at all.)
5. Read the result dialog, which reports how many skills were demoted, how many skill records had
   Grandmaster progress cleared, and how many pawns were processed.
6. **Save to a new / manual save slot.** The cleanup deliberately does *not* save for you, so you
   can keep the original.
7. **Exit RimWorld.**
8. **Disable / remove Grandmaster 21.**
9. **Reload the cleaned save.**

#### What the cleanup does

For every skill record on every relevant pawn in the currently loaded game:

* a stored level of **21 is demoted to 20** through the mod's authorised demotion path — the one
  route permitted to move a Grandmaster down;
* **all banked Grandmaster XP is dropped**, including partial progress on skills that never reached
  21. The weak-table entry is removed rather than zeroed, so `SkillRecord.ExposeData` omits the
  `grandmasterXp` element from the save entirely rather than writing a `0`.

Afterwards the save contains no meaningful Grandmaster 21 state. The operation is idempotent:
running it twice reports zero on the second pass.

#### Which pawns are scanned

Pawns are collected into a `HashSet<Pawn>` from five deliberately overlapping sources, so a pawn
reachable by more than one route is processed exactly once. Each source is guarded independently —
one unavailable container cannot abort the run.

| Source | Adds |
|---|---|
| `PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead` | The broad sweep: all maps (spawned *and* unspawned, including pawns inside cryptosleep caskets, containers and transport pods), all world pawns alive and dead, caravan members, pawns aboard travelling transporters, and the "temporary" list where quest-held and mid-generation pawns live |
| `Find.Maps` → `map.mapPawns.AllPawns` | Explicit per-map pass (colonists, prisoners, slaves, persistent visitors, animals) |
| `Find.WorldPawns.AllPawnsAliveOrDead` | Quest-held pawns, relatives, ex-colonists, faction leaders, and dead pawns whose records are still resurrectable |
| `Find.WorldObjects.Caravans` → `caravan.PawnsListForReading` | Explicit caravan pass |
| `map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse)` → `Corpse.InnerPawn` | Corpses still carry a full skill list; resurrection would otherwise restore a level-21 skill |

Sources 2–5 are redundant with source 1 by design: if `PawnsFinder`'s composition ever changes,
maps, world pawns, caravans and corpses are still covered directly.

#### Session status

After a successful run, Mod Settings shows `Current game prepared for uninstall ✓`. This is tracked
through a `WeakReference` to the specific `Game` object that was cleaned, so it disappears when you
load a different save and is never stored as a global preference. It is a statement about the game
currently loaded, not a claim that every save is clean.

#### Uninstall safety — what is and isn't claimed

The cleanup logic is covered by the offline test suite (demotion, progress clearing, idempotency).
**A full remove-the-mod-and-reload cycle has not been run in RimWorld**, so no claim of end-to-end
uninstall safety is made here. Keep a backup save.

---

## Compatibility

**Incompatible** with mods that also remove or replace the skill cap or replace `SkillRecord`
progression — for example *Endless Growth* and other unlimited-skill mods. Two different
skill-progression philosophies cannot be merged; do not run them together.

Mods that only *read* skill levels are fine. Vanilla's own stat formulas were audited and accept
21 safely:

* `SkillNeed_Direct` bounds-checks its list and returns the last entry.
* `SkillNeed_Curve` uses a `SimpleCurve`, which clamps outside its range.
* `SkillNeed_BaseBonus` is arithmetic, not indexed.

So level 21 produces a small natural improvement over 20 wherever vanilla supports it, rather than
being faked back down to 20.

Two vanilla methods genuinely index by skill level and are patched for that reason:
`QualityUtility.GenerateQualityCreatedByPawn(int, bool)` (21 cases, 0–20 — level 21 would
otherwise have rolled *worse than level 0*) and `SkillRecord.LevelDescriptor` (21 cases, `Skill0`–
`Skill20`). `SkillRecord.Interval`, the decay driver, is a `switch(levelInt - 10)` covering 10–20,
so level 21 already falls through to its default and never decays; the mod patches it anyway so the
guarantee is explicit rather than an accident of the jump table's size.

**Tested against:** RimWorld 1.6, `Assembly-CSharp.dll` build supplied by the mod author.
No other version is claimed. No compatibility with any specific third-party mod is claimed,
because none was tested.

---

## Harmony patches

| Target | Kind | Purpose |
|---|---|---|
| `SkillRecord.set_Level` | Prefix (replaces) | Keeps generation clamped at 20; **ignores every ordinary assignment once `levelInt` is 21** |
| `SkillRecord.GetLevel` | Postfix | Raises ceiling to 21 only when `levelInt >= 21`; aptitude still applies |
| `SkillRecord.GetLevelForUI` | Postfix | A stored 21 always displays 21, regardless of aptitude |
| `SkillRecord.get_LevelDescriptor` | Postfix | "Grandmaster" for 21 (vanilla switch has no case) |
| `SkillRecord.Learn` | Prefix + Postfix + Transpiler | Banks XP at 20; blocks all negative XP at 21; authorised 20 → 21, gated on the transpiler |
| `SkillRecord.Interval` | Prefix | Unconditional decay immunity at 21 |
| `SkillRecord.ExposeData` | Postfix | Persists `grandmasterXp` |
| `QualityUtility.GenerateQualityCreatedByPawn(int, bool)` | Prefix + Postfix | Deterministic bands; Legendary only at 21 |
| `QualityUtility.GenerateQualityCreatedByPawn(Pawn, SkillDef, bool)` | Postfix | Re-clamps after the Production Specialist offset |
| `SkillUI.GetSkillDescription` | Postfix | Progress / achieved text |
| `SkillUI.DrawSkill` | Postfix | ★ marker (cosmetic) |

### The single transpiler

Exactly one instruction is changed, in `SkillRecord.Learn`. Vanilla:

```
ldarg.0
ldfld    int32 RimWorld.SkillRecord::levelInt
ldc.i4.s 20            <-- replaced with: ldarg.0 ; call Gm21.LearnCapFor
bne.un   IL_013f
```

`LearnCapFor` returns 21 for a stored Grandmaster and 20 for everyone else, so:

* level 19 → `19 != cap` → vanilla level-up loop, unchanged
* level 20 → `20 == 20` → vanilla at-cap XP clamp, unchanged
* level 21 → `21 == 21` → at-cap clamp, so vanilla's loop **never runs**

That last line is the point. Left alone, vanilla would enter the loop at 21, increment to 22, hit
`if (levelInt >= 20) levelInt = 20` and silently demote the Grandmaster. It also guarantees no
level 22: the only writer of 21 is the authorised promotion, and the loop that could produce 22 is
unreachable from 21. The literal `20` in that ceiling check is deliberately left untouched so
ordinary levelling still stops at 20.

The transpiler matches on the `ldfld levelInt` / `ldc.i4 20` / `bne.un` shape rather than an
instruction index, and this predicate was verified to select **exactly one of the three** literal
`20`s in the method.

#### Fail-safe

The transpiler sets `Gm21.LearnPatchApplied` only after it has actually rewritten the instruction.
**`Gm21.Promote` refuses to run unless that flag is set**, so if RimWorld's IL ever changes shape:

* the IL is returned untouched and a clear error is logged at startup;
* Grandmaster XP keeps accruing and keeps saving — nothing banked is lost;
* the 20 → 21 promotion is **disabled for the session**, and the first blocked promotion logs one
  warning (once, not per XP event);
* existing Grandmasters keep their stored level 21 and their permanence;
* pawns promote normally again in the first session where the patch applies.

Creating a level 21 inside unmodified vanilla `Learn` would produce a pawn that vanilla's own
level-up loop demotes on the next XP tick, so the mod declines to create one at all.

#### One more `Learn` subtlety

Vanilla's *down*-level loop

```csharp
while (xpSinceLastLevel <= -1000f) { levelInt--; xpSinceLastLevel += XpRequiredForLevelUp; }
```

sits **outside** the `levelInt == cap` if/else, so it also runs on a *positive* XP event. Swallowing
negative XP at level 21 is therefore not sufficient on its own: a Grandmaster carrying an already
deeply negative `xpSinceLastLevel` — written directly by another mod, or inherited from a save made
under different rules — would be walked back down by the next point of XP it earned. The `Learn`
prefix normalises `xpSinceLastLevel` to `0` on that branch, which makes the loop unreachable at 21
by construction, for the cost of one float comparison on a branch only Grandmasters reach.

### Performance

No per-tick scanning, no global pawn or `SkillRecord` sweeps, no LINQ and no reflection in the XP
path. The `Learn` prefix exits on an integer field comparison for any pawn below level 20. Storage
is a `ConditionalWeakTable` lookup that only occurs at level 20 or above, with a cached value
factory so the hot path does not allocate.

---

## Building

> **`Assemblies/Grandmaster21.dll` in this repository is stale.** It predates the Beta repair pass
> and still contains the Alpha behaviour. Rebuild it before playing, or the mod will silently run
> the old rules.

```bash
./build.sh /path/to/RimWorld/RimWorldWin64_Data/Managed /path/to/0Harmony.dll
```

On Linux/Mono the `netstandard 2.1` facade is required (`mono-devel` provides it at
`/usr/lib/mono/4.5/Facades/netstandard.dll`); without it `mcs` fails with CS0012 on
`System.ValueType`.

### Tests

`Tests/Harness.cs` runs the compiled assembly against RimWorld's real `SkillRecord` type and needs
a RimWorld install.

`Tests/OfflineHarness.cs` needs no RimWorld install. It drives the mod's real Harmony
prefix/postfix bodies against a stub API whose signatures mirror `Assembly-CSharp`, covering
promotion, the transpiler fail-safe, permanence, the generation cap, decay, aptitude semantics,
both quality overloads, the per-pawn cleanup and the authorised-scope's exception safety. It does
**not** run inside RimWorld and does not exercise Harmony patching, real IL, pawn generation or
saving — those are verified in-game.

---

## Credits

Harmony by Andreas Pardeike (MIT). RimWorld by Ludeon Studios. MIT licensed — see `LICENSE`.
