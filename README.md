# Grandmaster 21

**RimWorld 1.6** — skills normally end at 20. This mod adds exactly one more level: **21, Grandmaster**.

Level 21 cannot be randomly generated. It must be earned by accumulating an enormous amount of
experience *after* a pawn has already reached level 20. Level 21 represents Grandmaster mastery
and is the only pawn skill level capable of producing **Legendary**-quality work.

Level 21 is the absolute hard maximum. Level 22 cannot occur.

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
| Deterministic quality | On | Off restores vanilla's random roll, but Legendary still requires level 21. |
| Grandmaster skills never decay | On | Skills below 21 always decay exactly as in vanilla. |
| Show Grandmaster progress | On | Tooltip progress line plus a ★ beside a Grandmaster skill. |
| Clamp generated pawns to 20 | On | Should normally stay on — it is what makes 21 mean something. |

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

Dev Mode and save/XML editing are outside the threat model by design; developer tools may set 21
deliberately.

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

**Removing the mod:** unknown save elements are ignored, so banked Grandmaster XP is discarded
harmlessly. However, a pawn with a stored level of **21 will still be 21 in the save**, and vanilla
will then behave unpredictably for that skill — vanilla's `Learn` would increment 21 → 22 and then
snap it back to 20, and `LevelDescriptor` has no entry for 21. In practice the skill collapses to
20 the first time it gains XP. Demote your Grandmasters before uninstalling if you care.

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
so level 21 already falls through to its default and never decays.

**Tested against:** RimWorld 1.6, `Assembly-CSharp.dll` build supplied by the mod author.
No other version is claimed. No compatibility with any specific third-party mod is claimed,
because none was tested.

---

## Harmony patches

| Target | Kind | Purpose |
|---|---|---|
| `SkillRecord.set_Level` | Prefix (replaces) | Keeps generation clamped at 20; protects an earned 21 from demotion |
| `SkillRecord.GetLevel` | Postfix | Raises ceiling to 21 only when `levelInt >= 21` |
| `SkillRecord.GetLevelForUI` | Postfix | Same, UI path |
| `SkillRecord.get_LevelDescriptor` | Postfix | "Grandmaster" for 21 (vanilla switch has no case) |
| `SkillRecord.Learn` | Prefix + Postfix + Transpiler | Banks XP at 20; blocks decay at 21; authorised 20 → 21 |
| `SkillRecord.Interval` | Prefix | Explicit decay immunity at 21 |
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
`20`s in the method. If the pattern is ever absent the IL is returned untouched and a clear error
is logged — the mod degrades to "no Grandmaster promotion" instead of corrupting skill progression.

### Performance

No per-tick scanning, no global pawn or `SkillRecord` sweeps, no LINQ and no reflection in the XP
path. The `Learn` prefix exits on an integer field comparison for any pawn below level 20. Storage
is a `ConditionalWeakTable` lookup that only occurs at level 20 or above, with a cached value
factory so the hot path does not allocate.

---

## Building

```bash
./build.sh /path/to/RimWorld/RimWorldWin64_Data/Managed /path/to/0Harmony.dll
```

On Linux/Mono the `netstandard 2.1` facade is required (`mono-devel` provides it at
`/usr/lib/mono/4.5/Facades/netstandard.dll`); without it `mcs` fails with CS0012 on
`System.ValueType`.

---

## Credits

Harmony by Andreas Pardeike (MIT). RimWorld by Ludeon Studios. MIT licensed — see `LICENSE`.
