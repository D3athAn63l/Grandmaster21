# Grandmaster 21

**RimWorld 1.6** — skills normally end at 20. This mod adds exactly one more level: **21, Grandmaster**.

**Version 0.12.0 Beta.** Adds the experimental **Medicine 21 — Grandmaster Physician** vertical
slice. The Shooting capstone has been verified in real RimWorld 1.6 gameplay. Melee, Magical
craftsmanship and Medicine have **not** — see [Release status](#release-status).

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

Two things are deliberately **not** settings, because both would switch off a defining rule:

* **Grandmaster decay.** Level 21 never decays. There is no option to restore vanilla's behaviour.
* **The generated-pawn cap.** Newly generated pawns can never receive level 21, unconditionally.

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

So the mod leaves the property setter clamped at 20 — unconditionally, with no setting to relax
it — and leaves the field alone. Every generated
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

## Shooting 21 — Grandmaster Marksman

The first skill-specific capstone. Unchanged in 0.10.0 — the melee work deliberately did not
refactor it.

> Shooting 20 is an elite marksman.
> Shooting 21 is a pawn who decides where the bullet goes and what the shot is meant to accomplish.

This is intentionally overpowered. At the default billion-XP requirement, nobody earns it casually.

### What mastery covers — and what it doesn't

A Grandmaster has near-perfect command of everything **the shooter** controls. Nothing here touches
what **the projectile** controls.

| The marksman controls | Untouched |
|---|---|
| Accuracy and aim compensation | Weapon range — a pistol is still a pistol |
| Cover compensation | Weapon damage, armour penetration, ammunition |
| Environmental compensation | Line of sight — walls still block shots |
| Warmup and recovery speed | Burst timing, magazines, reloads |
| Anatomical shot placement | Friendly fire — bullets do not phase through allies |

### Passive bonuses

Active whenever a pawn has a legitimate **stored** Shooting 21 — read from `levelInt`, so aptitude
can neither grant nor remove marksman status.

**1. Near-perfect accuracy compensation.** Not a flat +99 points, which would push a 30% pistol and
a 90% rifle both to 100% and erase weapon differentiation. Instead 99% of the *remaining miss
chance* is removed:

```
final = 1 − (1 − base) × 0.01
```

| Base | Grandmaster |
|---|---|
| 30% | 99.3% |
| 60% | 99.6% |
| 90% | 99.9% |

The better weapon stays strictly better, and a 0% base is still not a guaranteed hit.

**2. Cover is ignored.** `ShotReport.PassCoverChance` becomes 1 — sandbags, barricades and doorway
edges contribute nothing. This is an *accuracy* term only: line of sight is resolved separately by
`TryFindShootLineFromTo`, which is untouched. No shot path still means no shot.

**3. Environmental penalties are compensated — through the accuracy formula, not by zeroing
modifiers.** Darkness, weather, rain, fog, smoke, range and target size have all already been
folded into the number the accuracy compensation acts on, so removing 99% of the remaining miss
compensates for every one of them in a single place. This is deliberate: hunting down and zeroing
individual modifiers is where double-application and mod conflicts come from. Anything a future
mod models as a *physical obstruction* rather than an accuracy penalty is unaffected, which is the
correct outcome.

**4 & 5. Warmup and cooldown mastery.** 99% off both, floored at one tick so the engine never gets
a zero- or negative-tick stance. Implemented by prefixing the `Stance_Warmup` and `Stance_Cooldown`
constructors — the narrowest possible intervention. Burst timing (`ticksBetweenBurstShots`),
magazines and reloads are separate mechanisms and are left alone. Gated to `Verb_LaunchProjectile`,
so a Grandmaster's psycasts and melee are unaffected.

**6. The body still matters.** The passive package requires the pawn to be capable of both
`Consciousness` and `Manipulation`. Without that gate the compensation curve would erase every
capacity penalty, because it works on whatever miss chance is left regardless of where it came
from — a pawn with no functional arms would shoot perfectly. A pawn who cannot work a weapon falls
straight back to vanilla accuracy. The mastery is extraordinary; the pawn is not telekinetic.

### Grandmaster Aim modes

A Shooting Grandmaster gets one vanilla-style gizmo — **Grandmaster Aim** — opening a three-entry
float menu. The gizmo appears only for player-faction colonists with a stored Shooting 21.

| Mode | Behaviour |
|---|---|
| **Normal** (default) | Vanilla body-part resolution. All passive bonuses still apply. |
| **Killer** | Deliberately aim for the anatomy most likely to kill quickly. |
| **Downed** | Deliberately aim to cripple, avoiding vital organs where possible. |

Newly promoted pawns start on **Normal**, so a colonist reaching 21 does not silently begin
executing every enemy. Killer and Downed are opt-in, per pawn.

#### How a body part is chosen

Scoring is generic, by `BodyPartTagDef`, never by part name. RimWorld ships humanlikes, animals and
mechanoids, and mods add races with multiple hearts, no head, six legs, or anatomy nobody
anticipated; hardcoding `Brain`/`Heart`/`LeftLeg` would work for colonists and silently no-op or
crash for everything else. Tags are how RimWorld itself expresses "this is what makes the creature
conscious / pumps its blood / moves it", and every body def uses them.

**Killer** prefers, in order: the consciousness source (brain) → the part *containing* it (the
"head", derived structurally, not by name) → breathing pathway (neck) → blood pump (heart) →
breathing source (lungs) → blood filtration. Ties break toward the larger, easier-to-hit part.

**Downed** is *least-lethal targeting*, not "aim at the legs". For every projectile it asks: of
everything still attached, which single part most reduces this creature's ability to fight or flee
while being least likely to kill it?

| Tier | Target |
|---|---|
| 1 | Mobility limbs — core → segment → digit |
| 2 | Manipulation limbs — core → segment → digit |
| 3 | External extremities — a leaf part with nothing attached beyond it: ear, nose, tail, horn, digit, genitalia |
| 4 | Any other external non-vital part |
| 5 | Pelvis/spine, then any other internal non-vital part |

Ties break toward the *healthiest* candidate: shooting a working leg removes more Moving than
finishing off one already ruined, and it stops a burst being wasted on a limb that is nearly off.

**Internal parts rank last, even mobility-critical ones.** A spine is non-vital and wrecks Moving,
which by function alone would put it near the top — but a bullet into the torso cavity can spill
damage onto the parent part on the way, and the torso is where bleeding out happens. External
anatomy carries no such risk, so outside beats inside whenever "most disabling" and "least lethal"
disagree.

**Exclusion is one generic rule**: any part whose own subtree contains a vital organ. No name
lists. On a human that removes the brain, the head holding it, the neck and the entire torso; on a
modded six-legged creature with three hearts it removes exactly the parts wrapping those hearts. It
is also what stops Downed mode ever choosing centre mass, which is the whole point.

Killer returns "no opinion" if the creature has no vital anatomy at all — a mechanoid with no
organs — and vanilla resolution takes over unchanged. Nothing crashes because a modded creature
lacks a brain.

Downed only runs out when a creature has **no non-vital part anywhere**, and then it does not hand
back to vanilla: the Grandmaster stops firing instead. The player's doctrine is "down this target",
not "down this target unless it gets inconvenient, then shoot centre mass".

#### Burst discipline

Targeting is re-evaluated for **every projectile**, never chosen once and reused, because the
anatomy changes between shots. A Grandmaster also releases the trigger once the objective is met:

```
Bullet 1  front-left leg      -> destroyed
Bullet 2  re-evaluate         -> front-right leg
Bullet 3  target now downed   -> rest of the burst withheld
```

The burst is stopped by making `TryCastShot` return false, which is not a hack — it is the same
outcome vanilla produces whenever a shot line is lost mid-burst. `TryCastNextBurstShot` zeroes
`burstShotsLeft` and falls through to the ordinary end-of-burst path, so the cooldown stance is
still applied, the completion callback still fires, and the verb still returns to Idle. No burst
counter is poked directly and no lifecycle step is skipped.

**The opening shot of a burst is never withheld.** If a Grandmaster could decline to fire at all, a
job that keeps re-issuing the attack would spin — aim, decline, aim, decline. Requiring at least
one shot per burst guarantees forward progress while still delivering the behaviour that matters,
which is about not emptying the rest of a magazine into someone already on the ground. Single-shot
weapons therefore never hold fire, which is correct: there is no "rest of the burst" to withhold.

Killer mode shares the same machinery and stops once the target is dead, purely as an ammo
courtesy.

#### Killer mode is not guaranteed death

No bonus damage is added. The Grandmaster chooses *where* to shoot; the weapon still decides what
that does. A weak pistol round into a heavily armoured skull may simply not kill, and that is
correct. Every shot still flows through RimWorld's normal armour, damage, hediff, part-destruction
and death/downing systems — the patch only sets `dinfo.HitPart`. No fake impacts, no direct health
edits.

#### Death-on-downed suppression

RimWorld may convert a downed hostile into a death. Downed mode would be pointless if a successful
deliberate incapacitation were then rolled into a corpse, so that roll is suppressed — **narrowly**:
only while resolving a state change caused by a Grandmaster's ranged shot in Downed mode, by setting
`Pawn_HealthTracker.forceDowned` for the duration of that one call and restoring it in a finalizer.

This is **not immortality**, and that is not an assumption — it is visible in 1.6's IL.
`CheckForStateChange` tests `ShouldBeDead()` first and only reaches the downed branch if that is
false, where `forceDowned` short-circuits past the death roll straight to `MakeDowned`. Blood loss,
destroyed organs, fire and untreated wounds all still kill normally, afterwards. Global
death-on-downed is untouched for everything else.

The suppression also requires that **the pawn going down is the pawn that was actually aimed at**
(`dinfo.IntendedTarget`). Without that check it keyed only on "a Downed-mode Grandmaster fired a
ranged weapon", which also matches a stray round or a friendly caught in the line — neither of
which is a deliberate incapacitation, and neither of which has any business being spared the
storyteller's roll. `DamageInfo` already carries everything needed, so there is no attack-context
object and nothing to clean up.

### Friendly fire, range and damage

Friendly fire is **not** disabled. Grandmaster accuracy governs whether a shot goes where it was
aimed; it does not make allies transparent. Anatomical targeting explicitly refuses to redirect any
hit whose `IntendedTarget` is not the pawn being damaged, so a stray or friendly-fire hit is never
turned into a headshot.

Range and damage are untouched, full stop. A Grandmaster with a pistol is near-perfect *inside
pistol range* and cannot reach sniper distances.

### Hunting

Normal and Killer behave sensibly for hunting — Killer naturally prioritises lethal anatomy, which
is what a hunter wants. Downed mode on a hunt will tend to cripple rather than kill, which is
usually not what you want; it is left enabled rather than special-cased, because the mode is an
explicit per-pawn choice the player made. Switch a hunter to Normal or Killer.

### Storage

The aim mode is one byte per pawn in a `ConditionalWeakTable`, persisted from a postfix on
`Pawn.ExposeData` — the same architecture as Grandmaster XP. It therefore rides along with the pawn
through save/load, map transitions, caravans, world-pawn conversion and despawn/respawn with no
extra code, no manager and nothing to prune. Normal is the default and is never written, so a pawn
in Normal mode adds nothing at all to the save file.

---

## Melee 21 — Grandmaster of Combat

The second skill-specific capstone, new in **0.10.0** and **not yet runtime tested**; its Guardian
projectile doctrine was tightened in **0.10.1** and its friendly-explosive ruling simplified in
**0.10.2**, its protected-pawn safety made a hard constraint in **0.10.3**, and its dodge chance
given the Level-21 exception in **0.10.4**. Deliberately
overpowered, and deliberately *different in kind* from Melee 20. Skills other than Shooting,
Melee and Crafting have no Level 21 ability yet.

> Shooting 21 means the Grandmaster controls **the shot**.
> Melee 21 means the Grandmaster controls **the fight**.

If an enemy enters their reach, attacks are read, openings are exploited, counters happen
instantly, weapons are stripped away, nearby allies are protected, and incoming projectiles may be
caught and sent back.

### Grandmastery amplifies the pawn — it does not replace them

No two Melee Grandmasters fight alike, because every ability is driven by the pawn's own stats.
The design rule for every composite is the same: **a healthy vanilla human scores exactly 1.0**, so
a number below 1 is an impaired Grandmaster and a number above 1 is a superhuman one.

| Composite | Built from | Drives |
|---|---|---|
| **Precision** | Manipulation × (0.5 + 0.5 × Sight) | Disarm, critical chance, deflection, return-to-sender, pulled strikes |
| **Awareness** | Sight × Consciousness | Hit chance, defence-ignore, critical chance |
| **Reaction** | Consciousness × (0.5 + 0.5 × Sight) | Both interception systems |
| **Defence** | Consciousness × (0.35·Sight + 0.35·Manipulation + 0.30·Move) × weapon readiness | Parry |
| **Power** | MeleeDamageFactor × √BodySize | Cleave, critical multiplier, deflection distance |

So a **fast** Grandmaster excels at interception and projectile defence; a **precise, perceptive**
one at disarming, criticals and returning projectiles to their sender; a **physically powerful**
one at cleaving and at hurling deflected explosives absurd distances. A heavily modded pawn becomes
correspondingly absurd. That is intended.

**Optional modded stats.** If a `Strength`, `Intelligence`/`Perception`/`CombatAwareness` or
`Finesse`/`Dexterity` stat exists, it is folded in automatically, normalised against its own
`defaultBaseValue` and clamped to ×8 so two stacked frameworks cannot produce NaNs in combat maths.
**No modded stat is required** — in pure vanilla every such factor is exactly 1.0 and costs one
null check.

### What mastery covers — and what it doesn't

| The fighter controls | Untouched |
|---|---|
| Strike accuracy and defensive reading | Armour — armour is still armour |
| Parry, riposte, disarm | Weapon damage and damage type |
| Strike placement and strike force | Blast radius — no explosion immunity |
| Reaching an ally or a projectile in time | Walls — nothing phases through solid terrain |
| Which direction a deflected object goes | Physics — a returned grenade keeps its own fuse |

### Passive abilities

Active whenever a pawn has a legitimate **stored** Melee 21 *and* a body that can deliver the
ability. Downed, dead, unconscious, asleep, stunned or unspawned pawns get nothing at all;
blindness, ruined hands and immobility degrade the composites rather than switching them off.

**1. Near-perfect execution.** The same proportional rule the marksman package uses, applied at the
one value RimWorld rolls against: `final = 1 − (1 − base) × retained`, with
`retained = 0.01 / Awareness`. A 60% strike becomes 99.6%. It is *not* a flat +99 points, which
would erase weapon differentiation entirely.

**2. Near-perfect parry.** See
[Melee dodge chance: the one level that crosses the cap](#melee-dodge-chance-the-one-level-that-crosses-the-cap)
— a whole Grandmaster reaches RimWorld's **99.9%** ceiling; an impaired one falls measurably short;
a downed, unconscious, asleep or stunned one does not parry at all.

#### Melee dodge chance: the one level that crosses the cap

RimWorld caps `MeleeDodgeChance` at roughly **50%** for every pawn in the game. That cap holds no
matter what feeds the stat — Moving, Sight, traits, equipment, health, or a modded DEX stat that
resolves to 164%. The stat is computed, then clamped.

| Melee level | Dodge |
|---|---|
| **0–20** | Entirely vanilla: vanilla calculation, vanilla modifiers, **vanilla 50% cap** |
| **21** | Grandmaster effective melee defence, up to **99.9%** |

Level 20 is the peak normal combatant, and stays bound by normal combat rules — a heavily modded
Melee-20 pawn does **not** get Grandmaster defence because some framework handed them enormous
stats. Level 21 is the one level that crosses the boundary.

```text
gmDodge = 1 − (1 − vanillaDodge) × (0.002 / Defence)      capped at 0.999
```

| Vanilla final dodge | Grandmaster effective |
|---:|---:|
| 10% | 99.82% |
| 25% | 99.85% |
| 40% | 99.88% |
| **50%** (the vanilla cap) | **99.90%** |

**It transforms the resolved value rather than rebuilding it.** Moving, Sight, traits, health,
equipment, modded DEX and any other framework's post-process curve have all had their say by the
time the number reaches the roll; Grandmaster mastery then acts on the result. That is what keeps
this working with stat frameworks the mod has never seen, and it is why the raw pre-cap 164% is
deliberately *not* used — the design is "Level 21 transforms the normal resolved probability", not
"bypass every upstream cap and trust arbitrary values".

**The StatDef is never touched.** Raising its maximum would change the number for every pawn and
every mod reading it. The exception lives in the melee defence roll, where it applies, and nowhere
else.

**Why 99.9% and not 100%.** A literal certainty would make ordinary melee mathematically incapable
of ever touching the pawn. One failure in a thousand is not a balance lever — it is the difference
between "very nearly untouchable" and "untouchable", and only the first is a fighter.

**Saturation, stated honestly.** Those two numbers meet exactly: 99.9% at the vanilla ceiling *is*
the hard cap. So a healthy Grandmaster defending at vanilla's 50% is already at the maximum, and a
superhuman composite cannot push past them there. The composite is visible below the ceiling and in
the other direction — an impaired Grandmaster falls measurably short of it. This is where the design
deliberately stops rewarding stacked stats.

**Capability gates are the existing ones**, not a second health system. A downed, dead, unconscious,
asleep or stunned pawn never reaches this calculation at all — `Gm21Melee.CanAct` excludes them and
vanilla's value stands. Softer impairment scales the **Defence** composite (Consciousness × [Sight,
Manipulation, Movement] × weapon readiness) rather than switching anything off, so one damaged limb
does not collapse a Grandmaster back to 10%.

**One defence resolution, not two.** This *replaces* the value vanilla computed, at the single point
vanilla rolls against it. No extra defensive roll is layered on top: a Grandmaster defends once,
like everybody else. The ally-interception parry goes through the same function, so there is one
coherent Grandmaster melee defence rather than two with separate numbers.

**Ranged dodge is untouched.** `VEF_RangedDodgeChance` and every other ranged-dodge stat are left
exactly as they are. A Melee Grandmaster's answer to projectiles is the
[Guardian doctrine](#projectile-interception--the-guardian-doctrine) — they do not dodge bullets,
they intercept them.

**The Stats tab still shows 50%**, because that is genuinely what the StatDef resolves to. Since
that is misleading for this pawn, their Melee skill tooltip shows both figures — and computes the
second one by calling the same function combat calls, so an injured Grandmaster sees their real
reduced number and one who cannot defend is told so rather than shown 99.9%.

**3. Ignore ~99% of skill-based defence.** The opponent's dodge chance is *scaled down* by
`0.01 / Awareness`. This is not armour penetration — armour is never touched anywhere in this
package. The Grandmaster is exploiting stance, timing, balance and prediction, none of which a
breastplate cares about.

Attacker first, then defender, so **Grandmaster versus Grandmaster** resolves in the defender's
favour, producing a parry and a riposte that swaps the roles. The stalemate the design calls for
falls out of the arithmetic rather than being special-cased.

### Riposte — and why there is no recursion limit

When a Grandmaster turns a blow aside, they immediately owe the attacker a counterattack. It uses
their real equipped weapon, flows through normal damage/armour/body-part resolution, ignores the
weapon cooldown already in progress, and **can itself be parried** — by another Grandmaster, who
then ripostes in turn, forever.

Gameplay recursion is wanted. **C# recursion is not.** A parry does not attack anyone: it appends
an entry to a queue and returns, so the original attack's call stack unwinds completely. One tick
later `Gm21CombatScheduler` (a `GameComponent`) drains the queue and resolves each entry as an
independent, top-level melee attack. Every newly queued entry is stamped `current tick + 1`, so the
queue is strictly **generational** — an endless exchange performs a bounded amount of work per tick
instead of spinning inside one, at constant stack depth.

There is no counter limit, no fatigue and no escalating penalty. Two identical Grandmasters trade
parries and ripostes until something external — an explosion, a psychic lance, a third combatant,
fire — resolves it. *(A 512-entry ceiling exists purely as a memory guard against a pathological
mod interaction; reaching it is logged once and is a bug report, not a balance decision.)*

The automated suite drives 300 real counter-exchanges and asserts the call stack never grows by a
single frame.

### On-hit: disarm

Every landed strike may knock the opponent's weapon out of their hands. The weapon is **dropped**
via the engine's own `TryDropEquipment`, so it lands as an ordinary `Thing` with ordinary ownership
and forbidden state and can be picked up again.

```text
chance = 0.25 × Precision / grip        (×2.5 on a critical, capped at 0.75)
grip   = max(0.05, targetManipulation) × (1 + weaponMass / 4)   (×4 if psychically bonded)
```

Never attempted on an unarmed target, on a weapon with `destroyOnDrop` (integrated/mech armaments),
on an undroppable weapon, or by a Grandmaster with no manipulation. A weapon another mod keeps
outside the equipment tracker is simply never seen.

### On-hit: critical strikes

```text
chance     = 0.20 × Precision × Awareness      (capped at 0.75)
multiplier = weighted draw from 2×…10×, each tier's weight × bias^(tier−2)
bias       = clamp(Power, 0.5, 2.5)
```

Base weights `40 / 22 / 14 / 9 / 6 / 4 / 2.5 / 1.5 / 1` for 2×…10×. At ordinary power 2× is the
common outcome and 10× is exceptional; at high power the bias compounds up the ladder and 10×
becomes the *most* likely critical — the Isekai clause, with no special case for modded pawns.

The multiplier scales the attack's **real** damage before armour resolves. A 10× knife and a 10×
persona monosword remain very different things.

### Cleave

A landed strike may carry through into other hostiles in reach.

```text
weaponFactor = clamp(weaponMass / 2, 0.25, 4)
chance       = clamp(0.25 × Power × weaponFactor, 0, 0.90)
maxTargets   = clamp(floor(Power × weaponFactor), 1, 4)
```

| Weapon | Chance (ordinary power) |
|---|---|
| Dagger (0.4 kg) | 6% |
| Longsword (2.2 kg) | 28% |
| Warhammer (6 kg) | 75% |
| Huge two-hander (20 kg) | 90% |

Secondary targets receive a **real melee attack**, scheduled through the same queue as ripostes —
hit and dodge are rolled, armour resolves, a body part is chosen, and the target may parry. It is
not a fake AoE pulse. Only pawns hostile to the attacker are ever candidates, so a cleave cannot
become friendly fire no matter how crowded the melee is.

A cleave does **not** cleave again. One swing carries into the enemies standing around the one it
hit; it does not start a new swing that spreads further. A riposte is a genuine fresh swing and does
cleave normally.

### Ally melee interception — the 3-tile zone

When an enemy swings at an ally within three tiles, a Grandmaster may take the attack instead.

```text
effective = MoveSpeed × Reaction
chance    = InterceptCurve(effective) × distanceFactor        (capped at 0.99)
```

The curve is the design brief's benchmark, stated once and shared with projectile interception:

| MoveSpeed | 0 | 2 | 4 | 6 | **8** | 10 | 14 | 20 |
|---|---|---|---|---|---|---|---|---|
| Chance | 0 | 0.15 | 0.45 | 0.72 | **0.90** | 0.97 | 0.995 | 0.999 |

`distanceFactor` is 1.00 at one tile, 0.95 at two and 0.90 at three, so **8 m/s → 90%** holds at
one tile and is still 81% at the edge of the radius. A vanilla colonist at 4.6 m/s intercepts about
half the time: the zone is transformative for a *fast* Grandmaster, not free for every Grandmaster.

Interception needs a real route. The Grandmaster must be conscious, undowned, capable of **moving**,
hostile to the attacker, friendly to the victim, and have unobstructed **walkable** line of sight to
them — `GenSight.LineOfSight` with a walkability validator, so neither a wall nor impassable terrain
can be crossed. The attack must be genuinely hostile; social fights and training are never
intercepted.

On success the ally is not struck and the Grandmaster is owed a riposte against the attacker. **No
pawn is moved**: the brief explicitly permits a logical representation where physically relocating a
pawn mid-attack would be unsafe, and it would be — fighting the job, reservation and stance systems
at once is how colonists get stuck. Every *physical constraint* is still enforced.

### Projectile interception — the Guardian doctrine

> **The Guardian does not intercept every projectile within three tiles.**
> It reacts only to projectiles that credibly threaten the Grandmaster or a protected ally.

A Melee Grandmaster is not a CIWS turret and does not fight projectiles simply because they exist
nearby. An enemy minigun burst of a hundred rounds — ninety missing harmlessly, four hitting other
raiders, three hitting nearby colonists and three hitting the Grandmaster — produces exactly **six**
Guardian reactions. The other ninety-four cost a single grid lookup each and fly on untouched.

#### Stage 1 — is this projectile worth reacting to?

Threat is read from **RimWorld's own resolution**, not from a second trajectory simulator.
`Verb_LaunchProjectile` decides hit, miss or cover at cast time and launches the projectile at the
result, so the projectile already carries both answers: `usedTarget` is where it is *actually*
going, `intendedTarget` is who the shooter *meant* to hit. That is cheaper than re-simulating and
agrees with the engine by construction rather than by approximation.

| Projectile | Threatened |
|---|---|
| Direct, resolved onto a pawn | that pawn |
| Direct, resolved onto a cell | whoever is standing on it — usually nobody |
| Explosive | every pawn inside `explosionRadius` of the impact cell |

So a rocket aimed at the dirt beside Bob **does** count, because Bob is inside the blast. A bullet
resolved onto bare ground counts against nobody. Ignored outright: rounds that will miss everyone,
hit terrain, hit an enemy, or merely pass through the zone.

*Deliberate limit:* RimWorld can also clip a bystander through its own probabilistic free-intercept
roll along the flight path. Predicting that needs exactly the trajectory simulator the design rules
out, so a Guardian does not react to it. The error is always toward doing nothing.

#### Who is protected

The Grandmaster themselves, plus pawns within **3 tiles** who are genuinely on their side — same
faction, or a faction they are formally allied with (`FactionRelationKind.Ally`). "Not hostile" is
deliberately *not* enough: that would sweep in every neutral trader, visitor and wild animal that
wandered past.

This is symmetric. A raider Melee Grandmaster protects raiders, and will return a colonist's bullet.

#### Stage 2 — which Grandmaster answers?

Not whichever one grid iteration reached first. Every eligible Guardian is scored by its **real
interception probability** — `reachChance × deflectChance`, which already folds in movement speed,
reaction, distance, manipulation, sight, consciousness, what is in their hands, and the projectile's
difficulty. The highest score answers. A much faster Grandmaster three tiles away therefore beats a
sluggish one standing adjacent, because they really are more likely to make the catch.

**One Guardian attempts, and a failure is a failure.** Letting five Grandmasters each roll against
the same bullet until one succeeds would make defence a function of headcount rather than skill.

#### Stage 3 — the micro-dash

> The Grandmaster crossed the gap, met the projectile, and was back before the game's movement
> could represent any of it.

**The pawn does not move.** Not one cell, not for one tick. Position, job, path reservation and
melee engagement are all untouched, so a Grandmaster in a doorway stays in the doorway, one locked
in melee stays locked in melee, and a minigun burst cannot strobe them across the map. Movement
speed still decides whether the dash *succeeds* — it simply never decides where they end up.

The route still has to exist. `Gm21Reach` walks a walkability-validated line from the Grandmaster to
the threatened pawn, so nobody is protected through a sealed granite wall. It is shared with ally
melee interception so both Guardian systems answer reachability identically.

Chance is the shared interception curve — **8 m/s → 90%** at one tile, ~81% at three — on
`MoveSpeed × Reaction ÷ projectile difficulty`.

#### Stage 5 — how the redirect is chosen

**For direct shots, intent outranks faction identity. For explosives, intent is not consulted at
all.**

| Shooter | Payload | Intended target | Actual threat | Guardian behaviour |
|---|---|---|---|---|
| Hostile | any | anyone | GM / protected ally | **Return to Sender** → Safe Deflection |
| Hostile | any | other target | harmless miss | *ignored* |
| Friendly | **direct** | a hostile enemy | GM / protected ally, accidentally | **Friendly Recovery** → original enemy → Safe Deflection |
| Friendly | **direct** | a hostile enemy | still hits that enemy | *ignored* |
| Friendly | **direct** | GM / protected ally, deliberately | GM / protected ally | **treated as hostile** → Return to Sender |
| Friendly | **explosive** | *anything at all* | GM / protected ally | **Friendly Explosive Recovery** → best hostile destination → Safe Disposal |
| Friendly | any | anything | nobody | *ignored* |

##### Friendly direct fire — intent-aware

**Friendly Recovery** is corrective, not offensive. An ally's mis-resolved shot is salvaged back
toward the **original intended hostile target** — never returned to the ally who fired it, and never
handed to some other convenient enemy. Redirecting toward a better target would turn the Grandmaster
into a free targeting computer for low-Shooting pawns, which is precisely what the anti-abuse rule
exists to prevent. If the original target is dead, gone or no longer hostile, there is nothing to
salvage toward and the shot falls through to Safe Deflection.

Recovery difficulty is **angular**:

```text
correction = (1 − cos θ) / 2                     θ = angle between current heading and the enemy
factor     = 0.35 + 2.65 × correction            0.35 aligned … 3.00 exactly backwards
p          = Opposed(quality, difficulty × factor, ReturnHardness, 0.95)
```

A nearly-correct shot needs a nudge and is *easier* to save than a return-to-sender. A round flying
in completely the wrong direction has to be turned around and is far harder.

**The anti-abuse rule, and it still applies to bullets.** A friendly who deliberately aims a *direct
shot* at the Grandmaster or a protected ally is not having an accident, whatever faction they belong
to. Order Bob to force-attack Mark with a rifle and Mark classifies it as hostile intent — **Return
to Sender applies**. Trying to exploit a Guardian with gunfire should be a very bad idea.

##### Friendly explosives — intent-agnostic

**A friendly explosive that threatens a protected pawn is *always* Friendly Explosive Recovery, and
is never returned to the ally who launched it.** Not when the blast merely overlaps protected pawns,
not when the intended cell contains the Grandmaster, not when the player deliberately force-targeted
the ground next to them, not when a grenade was thrown straight at them.

Why the rule differs: an area weapon has no single aim point worth reasoning about. Whether a
grenade landing two tiles from the Grandmaster was malice, a misthrow, a force-targeted cell or a
perfectly reasonable shot at a raider standing just past them is **genuinely ambiguous** — and the
heuristics needed to guess were ambiguous too, which is why they are gone. The immediate problem is
not whether Bob deserves to be doom-rocketed. It is that there is a live warhead next to people the
Grandmaster is protecting.

Classification is therefore a cheap early decision: *friendly launcher + explosive payload +
protected pawn threatened* → done. No intended-cell inspection, no weighing nearby hostiles against
nearby friendlies, no reconstruction of what the thrower "really meant".

Explosive is read from `props.explosionRadius > 0`, so a thrown grenade, a launched grenade, a
rocket, an explosive shell and any modded projectile with a real payload all qualify — no weapon-name
list anywhere.

**None of this is automatic.** Reaching it (stage 3) and getting hold of it (stage 4) can still
fail, the redirect roll can still fail, the fuse keeps running throughout, a rocket can still
detonate during interception, and the blast radius is still the blast radius. A successfully
redirected doom rocket can still kill the Grandmaster who redirected it.

##### Hostile destination scoring

Unlike direct recovery, an explosive does **not** go back to the original intended target — there is
no shot to reconstruct. The Grandmaster picks the best place for it to go off instead.

Candidates are the hostiles already on the map, capped at 24. Each is first **vetoed or not**, and
only the survivors are scored. There is no cell-by-cell search and no global optimisation.

**Protected-pawn safety is a hard constraint, not a score.** See
[Guardian safety is not an economic calculation](#guardian-safety-is-not-an-economic-calculation)
below for why, and what it replaced.

| Stage | Rule |
|---|---|
| **Veto 1** | Any protected pawn within `explosionRadius + 1.5` tiles of the destination → **rejected, unscored** |
| **Veto 2** | Any protected pawn on the redirected flight path, for a direct-flight projectile → **rejected, unscored** |
| Score | +30 per hostile caught in the blast — and nothing else |

There is deliberately no protected-pawn term in the score. Protected pawns are not weighed against
raiders at any point; they are removed from consideration before weighing happens. Clustering needs
no special pass — counting hostiles in the blast *is* the cluster search, so three raiders together
outscore one isolated raider automatically, **among destinations that are already safe**.

A destination must also be within the Grandmaster's actual throw range (`RedirectDistance`, already
strength-scaled for thrown objects and fixed for rockets) and have line of sight from the
interception point, so nothing is ever lobbed through a wall and a weak colonist cannot hurl a
grenade across the map.

If no destination survives the vetoes, **there is no hostile redirect** and the explosive goes to
safe disposal. The Guardian's hierarchy is *protect people → exploit a safe hostile redirect →
dispose safely*, in that order.

##### Guardian safety is not an economic calculation

A protected pawn is not a large negative number. They are **do not intentionally hit this person**.

Through 0.10.2 the redirect chooser scored candidates: a hostile in the blast was worth +30, a
protected pawn in the blast −400, one on the flight path −120. Those numbers were compared, which
means they could be *traded* — and the trade was reachable:

```text
14 hostiles × +30  =  +420
 1 colonist        =  −400
                      ------
                       +20   -> "killing Alice is acceptable, enough raiders die too"
```

That is a weighted-utility answer to a question that is not economic, and it inverted the entire
point of a Guardian. The fix is not a bigger negative number — that is the same bug with a higher
threshold — and it is not `float.MinValue`, which is the same bug plus NaN risk.

Safety is now a **predicate evaluated before scoring**. A candidate that would endanger one of ours
is not a bad candidate; it is **not a candidate**. Priority is lexicographic, not weighted:

1. do not knowingly endanger a protected pawn
2. among the choices that survive that, do the most good

Structurally, the veto function cannot see hostiles at all — no hostile count can enter it, because
it is not a parameter. The test suite asserts that directly, alongside a 20-raider-around-one-
colonist scenario re-run at 60 raiders to show the answer does not move.

`Gm21ProtectedSafety` holds the whole rule and is shared by explosive disposal and safe deflection,
so the definition of "protected" cannot drift between them. It is the same
`Gm21GuardianThreat.Protects` predicate threat detection uses: the Grandmaster themselves, their
faction, and formal allies.

**Two vetoes.** *Blast:* any protected pawn within `explosionRadius + 1.5` tiles of the destination.
The margin exists because a pawn standing exactly on the edge of a blast is not "clear" — they may
step, explosions resolve over cells rather than points, and the Guardian is *choosing* this outcome
rather than having it happen to them. *Path:* any protected pawn on the trajectory, walked exactly
with `GenSight.BresenhamCellsBetween` rather than sampled, because a coarse sample down a forty-tile
line can step over a single pawn standing in it.

The path veto applies only to **direct-flight** projectiles (`!flyOverhead && arcHeightFactor <= 0`).
A thrown grenade arcs *over* the pawns between thrower and landing point — that is what
`arcHeightFactor` means — so vetoing it for them would paralyse the Guardian whenever an ally stood
next to the Grandmaster, on the strength of a collision that physically cannot happen. A projectile
whose def declares no arc is treated as direct, which is the conservative direction.

##### Safe Disposal

If no hostile destination scores acceptably — none in range, none without our own people nearby,
none with a clear line — the explosive falls through to the existing **safe-vector** logic and goes
to open ground away from the Grandmaster and everyone they protect.

Safe disposal does not nullify anything. The fuse, blast radius, damage, projectile def and
launcher attribution all survive; the Guardian is deciding *where* it goes off, not *whether*.

#### Attribution

The original launcher is **preserved** everywhere it can be. If Bob fired the shot, Bob remains its
launcher — Bob keeps the kill, the XP and whatever any third-party mod reads off the projectile. The
Grandmaster bent a trajectory; they did not fire Bob's weapon, and pretending otherwise would quietly
rewrite attribution across every mod that inspects a projectile.

Exactly one case overrides that: **return-to-sender**, because a projectile cannot hit its own
launcher and leaving the sender in place would make the manoeuvre silently impossible. A friendly
explosive is never returned, so it always keeps its original launcher. Either way the
Grandmaster is recorded in a separate weak table, so the information is kept without being forged
into the projectile.

#### Visuals

Purely presentational, and null-checked throughout so that a mod removing a vanilla def costs an
effect and nothing else: an afterimage streak (`FleckDefOf.LineEMP`) from the Grandmaster to the
interception point, a contact spark (`ThrowLightningGlow` + `MicroSparksFast`), and a metallic ring
(`SoundDefOf.MetalHitImportant`). No streak is drawn when the Grandmaster is defending their own
cell, where a zero-length line would be a smear. The whole thing is wrapped — if it fails, the
interception has already happened and stands.

### Stage 4 and 6 — deflection and safe redirection

Each stage has its own stats. Failing a later stage never undoes an earlier one.

```text
quality = Precision × Consciousness × implement
implement: melee weapon 1.0 × (1 + 0.5 × min(1, mass/3));  ranged 0.55×;  bare hands 0.35
p       = quality / (quality + difficulty × hardness)
          hardness 0.6 for deflection (cap 0.99), 1.5 for return (cap 0.95)
```

| | Longsword | Bare hands |
|---|---|---|
| Deflect a grenade | 73% | 40% |
| Deflect a bullet | 44% | 17% |
| Return a bullet | 24% | 8% |

Bare hands work on slow thrown objects and mostly not on bullets, exactly as the design calls for.
Zero Manipulation deflects nothing at all.

**Return to sender and Friendly Recovery** both redirect the *same projectile object* — never direct
damage to anyone. The `equipmentDef`, `equipmentQuality` and explosive fuse are saved and restored
around the relaunch, which is what makes *"the projectile retains its original damage and fuse"*
literally true rather than approximately true: the Grandmaster redirects the shot, they do not
improve it.

**Safe deflection** is what happens when the precision redirect fails, and it is still a success:
the Grandmaster and everyone near them are out of the object's way. Sixteen bearings are tried, each
**vetoed or not** by the same hard rule explosive disposal uses, and only the survivors are scored.

| Stage | Rule |
|---|---|
| **Veto 1** | Protected pawn within `explosionRadius + 1.5` tiles of the landing cell → **rejected, unscored** |
| **Veto 2** | Protected pawn on the flight path, for a direct-flight projectile → **rejected, unscored** |
| Score | +25 per hostile in the landing area, +3 per tile from the pawn just rescued, +2 per tile from the Grandmaster |

Priority therefore comes out as the design specifies: *away from the threatened pawn → away from the
Grandmaster → away from other friendlies → a clear flight path → open space → hostile space where
safe*. Nobody swats a grenade into the hospital, and nobody fires a deflected round through the
surgeon to reach an empty field.

**The one fallback, and why it is not the same bug.** If *every* bearing is vetoed, safe deflection
still returns the least dangerous of them. Declining to choose here does not mean nothing happens —
it means the projectile carries on to the destination that was already about to hurt the people the
Guardian is protecting. So the fallback is pure damage minimisation: hostiles are **not counted in
it at all**, and it can never express "hit one of ours to hit more of theirs". Offensive value never
reaches across the veto; only harm reduction does. Explosive *hostile redirect* has no such
fallback — there, no safe destination simply means no redirect.

```text
slow objects (< 30 c/s):  distance = 4 × Power × (1 + weaponMass / 4)     capped at 200 tiles
fast objects (≥ 30 c/s):  fixed 40 tiles — the lever is the ANGLE, not the arm
```

An ordinary colonist hurls a caught grenade 6 tiles; a strong one with a warhammer, 54; an Isekai
pawn with a colossal blade hits the 200-tile ceiling. Strength deliberately does **not** decide how
far a deflected bullet travels.

### Explosives

Three outcomes, and none of them is immunity:

* **Perfect deflection** — the warhead survives redirection and travels away or back, still armed.
* **Rough deflection** — the Grandmaster got a hand to it and could not turn it. For an explosive
  that is itself a way to set it off: a 50% chance the impact is brought forward to the
  interception point, through the engine's own impact path, right next to the Grandmaster.
* **Failed interception** — it continues on its original path, untouched.

A returned grenade keeps the fuse it had, so catching one late is dangerous. **A successfully
redirected doomsday rocket can still kill the Grandmaster who redirected it** if its blast radius
exceeds the distance they managed to buy. Mastery cannot reverse time, and physics remains physics.

### Melee doctrines — Normal, Killer, Downed

Per-pawn, saved, defaulting to Normal, and **entirely separate from the Shooting aim mode** — a pawn
who is a Grandmaster at both gets two gizmos, each naming its own skill, each reading and writing its
own store.

**Killer** uses the same shared anatomical ranking as the marksman package, plus one thing only melee
knows: how hard *this* blow is about to land. If any vital organ has less remaining health than the
strike carries, it goes there instead — destroying a 6 HP heart now beats a partial hit on a
full-health brain. Anatomy is scored by `BodyPartTagDef`, never by part name, so it works on modded
races with three hearts or no head.

**Downed** uses the shared least-lethal ladder unchanged — mobility, then manipulation, then non-vital
external anatomy, never a vital organ or anything wrapping one — and then adds **controlled force**,
which is the major distinction from Shooting Downed:

```text
margin = clamp(0.15 / Precision, 0.02, 0.60)
damage = min(damage, partHealth × (1 − margin))       floored at 1
```

A bullet delivers whatever energy it was carrying; a blade is under continuous control all the way
in. Capping just below the part's remaining health matters because *destroying* a limb is the single
most lethal thing a less-lethal strike can accidentally do — the part is gone, damage spills toward
the parent, and the bleed rate jumps. Keeping it attached but wrecked removes the capacity without
any of that.

Precision decides how *fine* the cut is, never whether it is safe: a superb Grandmaster stops within
2% of the part's remaining health, a barely-capable one leaves 60% and simply achieves less. Worse
precision is never more dangerous.

This is not immortality. The target can still die of blood loss, of wounds it already had, of
infection, of the next blow, or because its anatomy left no safe option — it is just substantially
safer than the shooting equivalent.

### Storage

One byte per pawn, in a `ConditionalWeakTable`, persisted from a postfix on `Pawn.ExposeData` —
the same architecture as Grandmaster XP and the aim mode, in a **separate table** so neither skill's
UI or save format can perturb the other's. Normal is the default and is never written.

Nothing else is saved. Riposte queues, interception decisions, critical rolls and deflection context
are transient by construction: a game reloaded mid-swing starts the next exchange clean.

---

## Crafting 21 — Magical craftsmanship

New in **0.11.0 Beta**, and **not yet verified in a running game**.

Research **Magical Craftsmanship** after Fabrication, then build a **magical workstation** from
the Production menu. Choose an eligible existing weapon/apparel recipe and its material. A
legitimate Crafting 21 pawn gathers the real ingredients plus **one Magical Catalyst** and
permanently commits the project. Enable Crafting work for the pawn.

The workstation owns the recipe, material, partial progress and one locked 50/50 outcome. Work
can pause or move to another Crafting Grandmaster. Every result is **vanilla Legendary**, with
either separate **Magical** craftsmanship or ordinary Legendary fallback. Magical currently
adds persistent metadata and an inspect label; no combat powers are implemented.

At combined work speed 1, the minimum is **18 working hours** (1.5 twelve-hour working days).
Expensive recipes take longer. The recipe's actual pawn and bench work-speed stats apply, with
square-root scaling above 1. Ordinary crafting speed and quality rules are unchanged.

After commitment there is **no cancel, refund or reroll**. Active benches cannot be deconstructed
or uninstalled. Destroying a bench destroys its project. Unsupported recipes, including
multi-output, consumable, art and ambiguous material recipes, are excluded conservatively.

**Catalyst acquisition is dev/test-only for this foundation:** enable Development mode and use
Spawn thing to obtain `GM21_MagicalCatalyst`. There is no manufacturing recipe or trader supply
yet. See [the feature guide](Docs/MagicalCrafting.md) for the full flow, audited APIs, persistence,
recipe exclusions, work calculation, test results and required in-game checklist.

---

## Medicine 21 — Grandmaster Physician

New in **0.12.0 Beta**, **experimental**, and **not yet verified in a running game**. Every tuning
number is provisional. Full reference: [Docs/Medicine21.md](Docs/Medicine21.md).

*"I do not need miraculous technology to practice miraculous medicine. I am the Grandmaster."*
No research, items, buildings, implants or special consumables are added — only three JobDefs for
the Grandmaster's own interventions, which cost ordinary medicine and the Grandmaster's time. Status is stored Medicine 21, read like every other capstone, and
a Grandmaster who cannot use their hands or is unconscious falls back to vanilla medicine.

* **Grandmaster Medicine Utilization.** Medicine tiers are discovered once at startup from every
  loaded medicine's `MedicalQualityMax` — no DefNames. A medicine of cap `C` is promoted to the
  lowest loaded cap ≥ `C × 1.30`, else `C + 0.30`: herbal 70% → **100%**, industrial 100% →
  **130%**, glitterworld 130% → **160%**. A small bridge tier cannot consume the bonus; a genuinely
  qualifying modded tier is used.
* **Deterministic tending.** A Grandmaster tend with medicine lands exactly on that target, every
  time — vanilla's own clamp does it, so the mote, tooltip and tend state all agree. The same
  effective quality reaches the condition itself, so condition-specific treatment such as a heart
  attack's success roll sees 100 / 130 / 160%. Ordinary doctors keep vanilla's roll. Without
  medicine, a Grandmaster tends by vanilla rules.
* **Grandmaster Treatment.** Each condition the Grandmaster actually tends carries its own regimen:
  that injury recovers faster, or immunity to that disease builds faster (100% → ×1.25, 130% →
  ×1.50, 160% → ×1.75). It refreshes, never stacks, lapses with the tend, and is replaced by
  anyone else's tend. Unrelated conditions get nothing.
* **Perfect surgery.** A Medicine Grandmaster's surgery never evaluates a failure or death outcome.
  Every requirement of the operation — bills, parts, ingredients, work, anaesthesia — is untouched.
* **Grandmaster Medicine command.** Left-click chooses **Cure / Reconstruct / Resuscitate**;
  right-click performs it through vanilla targeting and a medical job the Grandmaster carries out
  personally. **The price is time and ordinary medicine — no cooldowns, no charges.** Each
  intervention needs a medicine-potency budget (Cure 1.0, Reconstruct 2.0, Resuscitate 3.0: one,
  two, three industrial medicine, or herbal/glitterworld/modded medicine by potency) — taken from
  what the Grandmaster already carries whenever that is enough, otherwise completed from the map
  with vanilla hauling, under the patient's medical-care setting, and consumed only when the
  intervention succeeds; and hours of work (Cure 1 h, Reconstruct 2.4 h, Resuscitate 3 h at tend
  speed 1, divided by the Grandmaster's tend speed with no upper limit, down to a 60-tick floor). Interruption applies and consumes nothing. Self-Cure is
  allowed; self-Reconstruct needs at least 50% manipulation.
  * **Cure** one pathological condition ordinary medicine cannot properly address — immunizable
    diseases, chronic illness, tendable sicknesses and food poisoning. Wounds, missing parts,
    implants, addictions, pregnancy, supernatural and unknown modded states are never offered.
    Against the full vanilla data set that is exactly 28 conditions.
  * **Reconstruct** one missing natural body part, found structurally; a location already
    replaced by a bionic or prosthetic is never offered.
  * **Resuscitate** a corpse whose brain is intact while the body is still **biologically
    recoverable**: judged by the corpse's own rot progress, not a clock — about four in-game hours
    unpreserved, longer refrigerated, indefinitely frozen. The Grandmaster works wherever the body
    lies. Destroyed vital organs are rebuilt only as far as life requires; lost limbs stay lost.
    Wounds are preserved and bandaged, not erased, and up to **three** of the worst traumatic
    locations become permanent scars. The revived pawn then lies unconscious in **Resuscitation
    Shock** for six hours. No resurrection-sickness lottery. A hostile pawn can be saved — after a
    confirmation — and stays hostile. The resurrector mech serum is untouched and remains a
    different tool.

Before uninstalling, run **Prepare Save for Uninstall** — it now also removes Medicine treatments,
modes and Resuscitation Shock and stops interventions in progress.

---

## Removing Grandmaster 21 safely

> **Do not remove the mod while a save still contains level 21 skills or Magical crafting objects.**

From 0.11.0, remove every magical workstation and catalyst across maps, inventories and caravans
before running the skill cleanup below. Destroying an active workstation loses its project.
The cleanup action does not remove these objects or artifact metadata. Finished equipment keeps
its original item Def; its Magical metadata belongs to this mod and is lost on removal. The full
removal/reload sequence still needs an in-game test; keep a backup.

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
  `grandmasterXp` element from the save entirely rather than writing a `0`;
* **the Grandmaster aim mode is cleared**, for the same reason — a cleaned save should contain no
  `gm21AimMode` element either;
* **Medicine 21 state is removed** from every collected pawn, animals and corpses included: each
  hediff's Grandmaster Treatment, the Medicine mode, and any Grandmaster intervention in progress or
  queued — so no `gm21Treatment*` / `gm21MedicineMode` element and no `GM21_Medicine*` JobDef is
  left in the save. The result dialog reports the count.

Afterwards the scanned pawns contain no meaningful Grandmaster skill/progression state. The operation is idempotent:
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

#### No "prepared" status is shown

Deliberately. Cleanup is a point-in-time operation, not a property of the save: the moment you keep
playing, a pawn can bank new Grandmaster XP or earn a new level 21, and any "prepared ✓" marker
becomes a lie. The result dialog reports what was done and tells you to save and quit — that is the
whole contract. If you play on after cleaning, run it again before you save for real.

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

### Combat overhauls

**Combat Extended and the Shooting Grandmaster are mutually exclusive, by design.** CE replaces the
shooting pipeline these patches depend on. If a mod whose package id contains `combatextended` is
active, the *entire* marksman package — accuracy compensation, cover negation, warmup/cooldown
mastery and Killer/Downed targeting — is not applied at all, and a warning is logged. Level 21
itself, its permanence and the quality rules are unaffected.

**The same applies to the Melee Grandmaster package, separately.** It detects
`combatextended` and `yayo.combat` by package id and, if either is active, applies *none* of its
patches — no parry, riposte, disarm, criticals, cleave, interception or projectile deflection.
Melee detection is deliberately its own code path rather than shared with the marksman package:
the two touch different pipelines and must be able to reach different verdicts, and the shooting
package has already passed runtime validation and is not to be perturbed.

This is graceful degradation, not tested compatibility. **No CE compatibility is claimed** — the
interaction has not been run. The same applies to any other combat overhaul: only the ids above are
detected by name, so a different overhaul would not trigger the opt-out and the patches would be
applied on top of it.

The melee package touches melee hit resolution, dodge, verbs, projectile flight, explosions and
health. Melee overhauls, weapon frameworks and RPG stat systems were **audited in design** — modded
stats are optional and normalised, weapons are read by mass and def flags rather than by name,
anatomy is scored by `BodyPartTagDef` rather than part name, a weapon kept outside the equipment
tracker is simply never disarmed, and a projectile subclass that does not delegate to
`Projectile.TickInterval` is simply not interceptable. **None of that is tested compatibility
either.**

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
| `SkillUI.GetSkillDescription` | Postfix | Progress / per-skill achieved text |
| `SkillUI.DrawSkill` | Postfix | ★ marker (cosmetic) |
| `Pawn.ExposeData` | Postfix ×3 | Persists the Grandmaster aim mode, the melee doctrine and the Medicine mode, in separate tables |
| `Pawn.GetGizmos` | Postfix ×3 | The Grandmaster Aim, Grandmaster Melee and Grandmaster Medicine commands |

### Shooting Grandmaster patches

These are applied **manually**, after `PatchAll`, rather than by attribute. They target combat
internals, several of them private, and a couple were renamed across versions (`ShotReport`'s
chance properties were `ChanceToNotGoWild`/`ChanceToNotHitCover` before 1.3). An attribute patch
whose target cannot be resolved throws out of `PatchAll` and takes the *whole mod* down with it,
including level 21 permanence, which has nothing to do with shooting. Resolving each target by hand
lets a missing method disable one feature, log once, and leave everything else running.

| Target | Kind | Why |
|---|---|---|
| `Verb_LaunchProjectile.TryCastShot` | Prefix + **Finalizer** | Opens/closes the shot context. A finalizer, not a postfix, so the context cannot be left open if the cast throws |
| `ShotReport.HitReportFor` | Postfix | Sets the shot context for the targeting UI, which builds a report and reads it immediately |
| `ShotReport.AimOnTargetChance_IgnoringPosture` | Postfix | The accuracy compensation — applied at the single value `TryCastShot` rolls against, so it cannot double-apply |
| `ShotReport.PassCoverChance` | Postfix | Cover negation |
| `ShotReport.TotalEstimatedHitChance` | Postfix | Cosmetic: keeps the targeting readout consistent |
| `Stance_Warmup..ctor` | Prefix (`ref int ticks`) | Warmup mastery, without touching burst or reload timing |
| `Stance_Cooldown..ctor` | Prefix (`ref int ticks`) | Cooldown mastery, same |
| `Pawn.PreApplyDamage` | Prefix (`ref DamageInfo`) | Killer/Downed shot placement — sets `HitPart` only, before armour, so the shot still flows through every normal system |
| `Pawn_HealthTracker.CheckForStateChange` | Prefix + Finalizer | Narrow death-on-downed suppression |

**Why a shot context is needed at all:** `ShotReport` is a struct of precomputed factors. By the
time its hit-chance properties are read, the caster is no longer reachable from the report, so a
postfix on those properties cannot tell whose shot it is looking at. The context is set at the two
places that *do* know the caster, and the "is a Grandmaster" answer is computed once per shot, so
the property postfixes are a single bool read. An ordinary shooter pays essentially nothing.

One known wart: after the targeting UI builds a report, the observed flag stays set until the next
shot or hover. That can only affect a *displayed* hit chance, never a rolled one — real shots always
recompute the context — but it is a heuristic, not a guarantee.

### Melee Grandmaster patches

Applied **manually** too, and for the same reason: `Verb_MeleeAttack.GetNonMissChance` and
`GetDodgeChance` are *private* in 1.6, and an attribute patch on a private member that gets renamed
is a startup crash rather than a logged warning. They are also applied in independent groups, so
losing one target disables one feature instead of cascading:

| Target | Kind | Why |
|---|---|---|
| `Verb_MeleeAttack.TryCastShot` | Prefix + Postfix + **Finalizer** | Opens/closes the melee frame; runs ally interception before the attack; resolves on-hit effects and schedules ripostes after it. A finalizer so the frame stack cannot wedge if the attack throws |
| `Verb_MeleeAttack.GetNonMissChance` | Postfix (private) | Near-perfect execution, at the single value `TryCastShot` rolls against |
| `Verb_MeleeAttack.GetDodgeChance` | Postfix (private) | Both sides of the defence roll: the attacker's defence-ignore and the defender's parry, in that order |
| `Pawn.PreApplyDamage` | Prefix (`ref DamageInfo`) | Critical multiplier, Killer/Downed strike placement and controlled force — the last point where both the part and the amount can change, and still before armour |
| `Pawn_HealthTracker.CheckForStateChange` | Prefix + Finalizer | Melee death-on-downed suppression, gated on the open melee frame |
| `Projectile.TickInterval` | Prefix | Projectile interception, evaluated once per projectile as it enters the protective radius |
| `Pawn.ExposeData` | Postfix | Persists the melee doctrine |
| `Pawn.GetGizmos` | Postfix | The Grandmaster Melee gizmo |

`Gm21CombatScheduler` is a `GameComponent`, which RimWorld instantiates by type scan — no patch and
no XML. The Guardian doctrine (`Gm21GuardianThreat`, `Gm21Reach`, `Gm21GuardianFx`) adds **no new
Harmony targets at all**: it is pure decision logic hanging off the existing
`Projectile.TickInterval` prefix, which is why it can tighten behaviour without widening the mod's
patch surface.

**Why a melee frame is needed at all:** RimWorld resolves a melee attack across methods that share
no object. `TryCastShot` rolls the hit and the dodge; `Pawn.PreApplyDamage` — a separate call, on
the *victim* — is where the body part and damage can still change. By then nothing in the
`DamageInfo` can answer "was this a Grandmaster's deliberate strike, and did it critical?": an
unarmed strike's `Weapon` is the pawn's own race def, and a rifle used as a club reports a ranged
weapon. The frame is opened where the caster *is* known, computes every composite once, and makes
the per-damage-event question a field read.

It is a **stack**, not a single slot, because another mod's patch or a retaliation effect can
legitimately start a melee attack inside one already resolving. Past eight levels of nesting the
frames stop being tracked and everything falls back to vanilla, which is the right answer to
pathological nesting.

**Distinguishing a parry from a whiff:** vanilla only consults the dodge chance *after* the hit roll
has succeeded, so "dodge chance was consulted **and** the attack still failed" identifies a dodge
rather than a miss. A riposte answers a parry, not a stumble. When vanilla skips the dodge roll
entirely — an immobile target, a surprise attack — nothing is scheduled.

**Why `Projectile.TickInterval` and not every subclass:** every projectile subclass delegates to the
base method to actually move, so the base is the single point every projectile in the game passes
through, modded ones included. A hypothetical subclass that reimplements flight from scratch would
simply not be interceptable — graceful degradation, not a crash. A once-only weak-table mark makes a
duplicate call harmless in any case.

### Medicine Grandmaster patches

Applied **manually**, in independent groups, by `Gm21MedicinePatches` — a missing target disables
one Medicine feature and logs once. Why each hook sits where it does is in
[Docs/Medicine21.md](Docs/Medicine21.md#11-harmony-hooks).

| Target | Kind | Why |
|---|---|---|
| `TendUtility.DoTend` | Prefix + **void Finalizer** | Opens/closes the Grandmaster tend frame (doctor + medicine), nesting-safe |
| `TendUtility.DoTend` | **Transpiler** (one call site) | Its single `Hediff.Tended` call goes through `TendedEffective`, so condition-specific overrides see the Grandmaster quality; any other IL shape is left untouched and the feature disables itself |
| `HediffComp_TendDuration.CompTended` | Prefix (`ref quality`, `ref maxQuality`) + Postfix | Exact tend quality through vanilla's own clamp; starts, refreshes or clears the condition's regimen |
| `HediffComp_TendDuration.CompTipStringExtra` | Postfix | Cosmetic treatment line |
| `Hediff.ExposeData` | Postfix | Persists an active regimen inside that hediff's own node |
| `Pawn_HealthTracker.HealthTickInterval` | Prefix + **void Finalizer** | Scopes "natural recovery" |
| `Hediff_Injury.Heal` | Prefix (`ref amount`) | A treated injury recovers faster inside the health tick |
| `ImmunityRecord.ImmunityChangePerTick` | Postfix | A treated disease instance builds immunity faster |
| `SurgeryOutcomeEffectDef.GetOutcome` | Prefix (replaces for a practising Grandmaster only) | No failure/death outcome is ever evaluated |

### The progression transpiler

Exactly one instruction is changed, in `SkillRecord.Learn`. (Medicine 21's `DoTend` transpiler is
the only other one; it is described with the Medicine patches above.) Vanilla:

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

**Melee.** Every gate is `Gm21.IsGrandmaster`, which is an integer field comparison, so an ordinary
pawn's attack costs a handful of comparisons. Composites are computed **once** per attack when the
frame opens and only for whichever side is a Grandmaster; the hooks that consume them are field
reads. No LINQ anywhere in the melee or projectile paths, and no allocation on a landed strike
beyond what vanilla already does.

* **Ally interception** scans 37 cells around the victim, and only when a genuinely hostile melee
  attack is being made against a pawn who is not themselves a Grandmaster. It is bounded by the
  attack rate, not by the tick rate.
* **Cleave** scans nine cells, only on a landed Grandmaster strike that already rolled a cleave.
* **Projectile defence** is one weak-table lookup and a few field reads per projectile per tick,
  with every cheap `ThingDef` check ordered ahead of the single reflective read. Evaluation happens
  **once per projectile**, for the one tick it spends entering the radius, and then runs the
  Guardian fast path:

  ```text
  if nothing is going to be hit:                 return          # 1 grid lookup
  if no Grandmaster protects who will be hit:    return          # 37 grid lookups
  ```

  A harmless round therefore costs a single `ThingsListAtFast` call and nothing else — no Guardian
  scan, no reach maths, no safe-vector work, no return-chance calculation. This is *cheaper* than
  the behaviour it replaced, which ran a Guardian scan for every projectile whose destination
  happened to land near a Grandmaster. Only a round that will actually connect with a pawn pays for
  the 37-cell scan, and only one that connects with a **protected** pawn pays for anything beyond it.
* **Safe-vector scoring** is 16 bearings × a small neighbourhood, and runs only at the moment a
  deflection actually happens. No pathfinding, no map scan.
* **The reaction queue** holds at most one entry per melee exchange in progress. Draining it is
  proportional to what is queued, never to the number of pawns or projectiles on the map.

---

## Building

`Assemblies/Grandmaster21.dll` is built by this script and committed, stamped with its build time
and source commit — see `Assemblies/README.md`.

```bash
./build.sh /path/to/RimWorld/RimWorldWin64_Data/Managed /path/to/0Harmony.dll
```

On Linux/Mono the `netstandard 2.1` facade is required (`mono-devel` provides it at
`/usr/lib/mono/4.5/Facades/netstandard.dll`); without it `mcs` fails with CS0012 on
`System.ValueType`.

### Tests

`Tests/Harness.cs` runs the compiled assembly against RimWorld's real `SkillRecord` type and needs
a RimWorld install.

`Tests/OfflineHarness.cs`, `Tests/OfflineHarness_Shooting.cs` and
`Tests/OfflineHarness_Melee.cs` need no RimWorld install:

```bash
./tools/build-stubs.sh
```

That compile-checks the older source groups and runs all three suites against reference stubs whose signatures
mirror `Assembly-CSharp`. Between them they cover promotion, the transpiler fail-safe, permanence,
the generation cap, decay, aptitude semantics, both quality overloads, the per-pawn cleanup, the
authorised scope's exception safety, the accuracy and delay maths, aim-mode storage, Killer/Downed
part selection across several anatomies, the targeting patch's gating, and that every translation
key the code looks up is actually shipped.

Medicine 21 is excluded from the stub build too, and verified against the real game DLLs instead:

```bash
./tools/verify-medicine.sh /path/to/Managed /path/to/0Harmony.dll /path/to/Mono.Cecil.dll [/path/to/RimWorld/Data]
```

It installs the mod's real Medicine patches on the real vanilla methods and executes them —
`Hediff.Tended`→`CompTended`, the patched `DoTend` IL and `Hediff_HeartAttack.Tended`,
`Hediff_Injury.Heal`, `SurgeryOutcomeEffectDef.GetOutcome`, `HediffSet`, real medicine Things in a
real inventory, and the real Scribe saver/loader — plus, given the game's `Data/` folder, runs the real
C# Cure, tier and surgery rules over every vanilla Def in all six content packs. Test-process-only
shims cover what needs a live game (icon loading, live-pawn re-evaluation, and Steamworks for
`ParseHelper` via `tools/stubs/SteamworksShim.cs`); none of them ship. Details and results:
[Docs/Medicine21.md](Docs/Medicine21.md#13-tests).

The Magical crafting source is excluded from the stub build. Build it against the real game DLLs
and run `tools/verify-transcendent.sh`; setup and overrides are documented in
[Assemblies/README.md](Assemblies/README.md).

The melee suite additionally drives the **real patch bodies** for hit chance and defence, every
stat composite, the capability gates, the reaction scheduler — including 300 counter-exchanges with
an assertion that the call stack never grows by a single frame — disarm gating and odds, the
critical ladder's range and distribution at three power levels, cleave scaling and its
never-hit-allies rule, the 8 m/s interception benchmark and its range/wall/mobility constraints, the
projectile difficulty ordering, the deflection and return curves, redirection distance, the
safe-vector chooser, and the controlled-force clamp.

It also drives the **Guardian doctrine** end to end against the real threat model: the hundred-round
minigun case (asserting exactly six reactions and ninety-four ignores), explosive blast threat,
who counts as protected across five faction relationships, all six rows of the intent table
including both deliberate-friendly-attack cases, best-Guardian selection in both directions, the
sealed-wall and downed-Guardian rejections, the angular recovery curve, recovery attribution, the
path-aware safe vector, and a hundred consecutive micro-dashes asserting the pawn never moves.

Friendly-explosive handling is covered separately: every way a friendly warhead can arrive resolving
to the same classification, an enemy warhead still resolving to hostile, cluster preference,
the refusal to accept any protected pawn as collateral, throw-range and wall bounds, the fall-through
to safe disposal when no destination qualifies, and the guarantee that the ally who launched it is
never the target — alongside explicit regression guards that Bob's *rifle* still classifies as
hostile and his stray bullet still as an accident.

The protected-pawn veto has its own regression set, written to assert **categorical rejection**
rather than score magnitude — a test checking `score < 0` would have passed against the bug it
exists to prevent. It covers 20 raiders packed around one colonist (re-run at 60 to show the answer
does not move), an unsafe horde losing to two clean raiders, a rocket refusing to fly through a
colonist while a thrown grenade correctly arcs over one, every raider being shielded producing no
hostile redirect at all, safe deflection rejecting a raider-filled bearing that also holds one of
ours, and the structural proof that the veto function takes no hostile input and so cannot be
outvoted by one.

The Level-21 dodge exception has its own set: the formula against the specification's table, the
99.9% hard cap proved to be a real clamp rather than decoration, a Melee-20 pawn with enormous
modded capacities still held at the vanilla 50%, downed / unconscious / stunned Grandmasters
falling back to the vanilla value while an immobile-but-conscious one still defends, the tooltip
reporting the computed figure rather than a hardcoded one, the whiff-versus-parry distinction that
keeps a natural miss from earning a riposte, and a structural check that no member of the mod names
a ranged-dodge stat.

They do **not** run inside RimWorld. They do not exercise Harmony patching, real IL, combat,
projectiles, pawn generation, the gizmo or saving — and critically, they **cannot prove that the
RimWorld members named in the source exist with those signatures**, because the stubs are
hand-written approximations. Only a real build does that. See `tools/stubs/README.md`.

---

## Release status

**0.12.0 Beta.** Medicine 21 builds against the real RimWorld 1.6/Unity/Harmony assemblies with
zero warnings/errors. After the second design pass (tend-quality propagation, medicine cost, work
time, self-intervention, hostile resuscitation, minimum vital reconstruction, death-trauma scars)
and the final pre-runtime pass (carried medicine first, no tend-speed ceiling, decay-based
resuscitation viability, Resuscitation Shock), `tools/verify-medicine.sh` reports **325 PASS, 0 FAIL,
0 BLOCKED** with the vanilla `Data/`
folder, including real Scribe save **and load** round trips, real medicine Things consumed in a real
inventory, and an audit of every vanilla Def. **Medicine 21 has had
NO runtime gameplay testing** — see the runtime checklist in
[Docs/Medicine21.md](Docs/Medicine21.md#14-runtime-checklist-not-run--needs-a-real-game).
The Shooting, Melee and Crafting implementations are unchanged in this version.

**0.11.0 Beta.** The Magical slice builds against real RimWorld 1.6/Unity/Harmony assemblies with
zero warnings/errors. Its 114 headless policy/API/XML/save-writing checks pass, with one additional
implicit-work stat probe blocked by a missing Steamworks DLL. The existing
runtime-target harness reports 168 passes and one failure caused by a missing Steamworks DLL in
the supplied assembly set. Full save/reload and map gameplay are **not runtime verified**.
See [Magical craftsmanship](Docs/MagicalCrafting.md) for architecture, limits and the runtime checklist.
The [empty-recipe discovery repair](Docs/MagicalRecipeDiscoveryFix.md) documents the real
reference-resolution regression, subtype compatibility fixes and bounded rejection diagnostics.

**The Melee Grandmaster package has had NO runtime gameplay testing.** Every RimWorld member it
touches is confirmed present with the right signature and parameter names against the real 1.6
assembly metadata, and its decision logic is covered by 295 offline checks — but patches resolving
is not patches binding, and patches binding is not patches behaving. Treat Melee as untested in
play, and keep a backup save.

The Shooting and Melee implementation is unchanged in this version. Shooting retains its 0.9.0
runtime result below.

### Runtime test — PASS (RimWorld 1.6, 0.9.0) — Shooting only

Verified in an actual gameplay session:

| Test | Result |
|---|---|
| Level 21 survives saving | **PASS** |
| Level 21 survives loading | **PASS** |
| Grandmaster Shooting passives activate in combat | **PASS** |
| Killer mode targets lethal anatomy | **PASS** — brain; target killed very quickly |
| Downed mode targets mobility anatomy | **PASS** — assault rifle vs. raccoon, front-left leg destroyed |

#### Known issue found by that test, fixed in 0.9.1

In the same raccoon test, only the *first* projectile of the burst stayed under Downed-mode
control. The rest resolved through vanilla body-part selection, hit the body, and killed the
target — the opposite of what Downed mode is for.

0.9.1 addresses it with per-projectile re-evaluation, a much deeper less-lethal ladder, a refusal
to ever hand back to vanilla while in Downed mode, and burst discipline that stops firing once the
target is down. **That new burst behaviour is `NOT YET RUNTIME TESTED`** — it is covered by the
offline suites and the patches bind against the real assembly, but no gameplay session has
exercised it yet.

### Verified against the real 1.6 assemblies

```bash
./tools/verify-real.sh /path/to/Managed /path/to/0Harmony.dll
```

0.10.0 was built and checked against RimWorld 1.6 **reference assemblies** — full public/protected
metadata for `Assembly-CSharp 1.6.9676.17735`, with method bodies stripped. That is enough to prove
every signature, and not enough to run IL. Results are split accordingly, and the three checks that
need real bodies are reported `NOT RUN`, not passed.

| Check | 0.10.0 result |
|---|---|
| Release build against 1.6 | **PASS** — clean, no warnings |
| 1. Runtime targets and Harmony parameter names | **PASS** — 169/169, 0 skipped |
| 2. `Learn` transpiler IL pattern | **NOT RUN** — needs method bodies |
| 3. Live Harmony patch binding | **NOT RUN** — needs method bodies |
| 4. Finalizer semantics | **PASS** — 12/12, all four shipped finalizers |
| 5. Progression suite vs. real `SkillRecord` | **NOT RUN** — needs method bodies |
| Offline logic suites | **PASS** — 426/426 (57 core + 74 shooting + 295 melee) |

Checks 2, 3 and 5 all fail with `Method has zero rva` against reference assemblies. That is the
environment, not a finding: **run `verify-real.sh` against a real RimWorld install to clear them.**
They passed against real assemblies at 0.9.2 for everything that existed then; the melee patch
groups added to `Tests/PatchAllTest.cs` in this version compile but have never been bound.

**169/169 runtime targets resolve**, including every member looked up reflectively and every
Harmony injection *parameter name*. This matters more than it sounds: Harmony binds prefix/postfix
arguments by name, so a renamed vanilla parameter compiles perfectly and throws at patch time. Newly
confirmed for melee: `Verb_MeleeAttack.GetNonMissChance` / `GetDodgeChance(LocalTargetInfo target)`
— both **private** — `Pawn_MeleeVerbs.TryMeleeAttack(Thing, Verb, bool)`,
`Pawn_EquipmentTracker.TryDropEquipment` and `bondedWeapon`, `ThingDef.destroyOnDrop` /
`destroyable`, `DamageInfo.SetAmount`, `Pawn_StanceTracker.SetStance` / `curStance` / `stunner`,
`StunHandler.Stunned`, `RestUtility.Awake(Pawn)`, `Projectile.TickInterval(int delta)`, the 8-arg
`Projectile.Launch`, the protected `equipment` / `equipmentDef` / `equipmentQuality` / `destination`
/ `ticksToImpact` fields, the private `Projectile_Explosive.ticksToDetonation`,
`ProjectileProperties.speed` / `explosionRadius` / `flyOverhead` / `SpeedTilesPerTick`,
`StatDefOf.MoveSpeed` / `Mass` / `MeleeDamageFactor`, `PawnCapacityDefOf.Sight` / `Moving`,
`StatDef.defaultBaseValue`, `GenSight.LineOfSight` (both overloads), `GenGrid.Walkable`,
`ThingGrid.ThingsListAtFast`, `GenHostility.HostileTo` and `MoteMaker.ThrowText`. Added for the
Guardian doctrine in 0.10.1: `Projectile.usedTarget` and `intendedTarget`, the protected
`Projectile.origin`, `LocalTargetInfo.Thing` / `.Cell`, `Faction.RelationKindWith`,
`FactionRelationKind.Ally`, and the cosmetic `FleckMaker.ConnectingLine` /
`ThrowLightningGlow` / `Static`, `FleckDefOf.LineEMP` / `MicroSparksFast`,
`SoundDefOf.MetalHitImportant`, `SoundInfo.InMap`, `SoundStarter.PlayOneShot` and
`Log.WarningOnce`.

One melee design assumption is worth stating plainly, because it could not be confirmed from
metadata alone: `Projectile.DestinationCell` turned out to be **protected**, which a guess would
have got wrong, so the destination is read from the `destination` field the redirect logic already
resolves.

Two design assumptions confirmed directly in the shipped IL at 0.9.2, still current:

* `Verb_LaunchProjectile.TryCastShot` rolls `Rand.Chance` against
  `AimOnTargetChance_IgnoringPosture` (IL_02cc→02d1) and `PassCoverChance` (IL_03ce→03d3) — exactly
  the two values the accuracy and cover patches modify, so the compensation lands on the real
  hit/miss decision and cannot double-apply.
* `SkillRecord.Interval` is `switch(levelInt - 10)` over 10–20 with `ret` as its default, so level
  21 already falls through — the prefix makes that explicit rather than relying on the jump table's
  size.
* The down-level loop at `IL_0190` is `xpSinceLastLevel <= -1000f`, and **both** the at-cap branch
  (`IL_013d`) and the level-up branch (`IL_0150`) reach it. It therefore runs on *positive* XP too,
  which is exactly the hazard the `Learn` prefix normalises away at level 21.

### Cleanup finalizers never swallow exceptions

Both Harmony finalizers in the mod exist purely to undo temporary state: closing the shot context,
and restoring `forceDowned`. Both are **void**.

That matters more than it looks. A Harmony finalizer that returns `Exception` does not *report*
the exception — its return value **replaces** the pending one, so `return null` means "there is no
exception any more". Through 0.9.1 both finalizers did exactly that, which silently ate anything
thrown inside `TryCastShot` or `CheckForStateChange`, whether by RimWorld or by another mod's
patch on the same method. A mod that hides other mods' errors is close to undebuggable.

A void finalizer cannot alter exception state at all, which is exactly what cleanup wants: clean
up, change nothing. `Tests/VerifyFinalizerSemantics.cs` pins the contract down by execution rather
than by comment — it patches throwing methods with each finalizer shape and asserts which ones
propagate — and then audits the shipped assembly to confirm every registered finalizer is either
void or returns `__exception` unchanged.

The melee package adds two more finalizers, for closing the melee frame and restoring the melee
`forceDowned` guard, and both are void for the same reason. The audit **discovers** finalizers by
name rather than reading from a list, because a hardcoded list quietly stops covering the mod the
moment a new one is added — which is exactly what would have happened here. Medicine 21 adds two
more — closing the Grandmaster tend frame around `TendUtility.DoTend` and the natural-recovery
counter around `HealthTickInterval` — both void, and the audit picked them up without being told.
All six are covered.

### Two patch targets cannot be bound outside the game

`SkillUI.GetSkillDescription` and `SkillUI.DrawSkill` are reported BLOCKED, not failed, and no set
of assemblies will clear them. Harmony must run a target's static constructor before patching it,
and `SkillUI..cctor` loads textures:

```
SkillUI..cctor -> ContentFinder.Get -> Verse.UnityData..cctor -> Verse.Log.Warning
               -> UnityEngine.Debug.ExtractStackTraceNoAlloc   (internal call, player only)
```

That needs the actual game process. Both patches are cosmetic — the tooltip text and the ★ marker —
and both target members were confirmed present with the right signatures and parameter names by
check 1, so what is unverified is the bind step alone.

### Not yet verified — requires actually playing

Patches resolving is not patches binding, and patches binding is not patches behaving.

**All of Melee 21 is in this category.** Nothing below has been observed in a running game:

| Melee test | Status |
|---|---|
| Melee 20 gets no new ability | `NOT RUN` (offline: PASS) |
| Melee 21 activates the passive package | `NOT RUN` (offline: PASS) |
| Doctrine survives save/load | `NOT RUN` (offline: store round-trip PASS) |
| GM attacks a normal pawn / normal pawn attacks a GM / GM vs GM | `NOT RUN` (offline: patch bodies PASS) |
| Parry → riposte, riposte countered, long chain without overflow | `NOT RUN` (offline: 300 exchanges at constant stack depth PASS) |
| Disarm: armed / unarmed / integrated weapon | `NOT RUN` (offline: PASS) |
| Criticals 2×–10×, armour still applies | `NOT RUN` (offline: range and distribution PASS) |
| Cleave at 1 / 2 / crowded, light vs heavy weapon | `NOT RUN` (offline: PASS) |
| Ally intercept at 1 / 2 / 3 / >3 tiles, varying speeds, 8 m/s ≈ 90% | `NOT RUN` (offline: PASS) |
| Projectile deflection: arrow, bullet, grenade, rocket, doomsday | `NOT RUN` (offline: difficulty model PASS) |
| Return to sender actually travels toward the sender | `NOT RUN` — needs a live projectile |
| Safe deflection moves away from the GM and allies | `NOT RUN` (offline: vector chooser PASS) |
| Weak vs very strong GM: grenade redirect distance differs | `NOT RUN` (offline: PASS) |
| Explosives: fuse preserved, rocket may detonate on interception, blast can still kill the GM | `NOT RUN` — needs a live projectile |
| Killer prefers vital anatomy / Downed prefers mobility with reduced force | `NOT RUN` (offline: PASS) |
| Physical incapacity degrades abilities | `NOT RUN` (offline: PASS) |
| Two identical Grandmasters sustain a counter chain without instability | `NOT RUN` (offline: PASS) |

Guardian doctrine, added in 0.10.1 — likewise none of it observed in a running game:

| Guardian test | Status |
|---|---|
| Enemy minigun: only threatening rounds invoke Guardian logic | `NOT RUN` (offline: 6 of 100 PASS) |
| Enemy harmless miss near the GM is ignored | `NOT RUN` (offline: PASS) |
| Enemy shot toward a protected ally is intercepted | `NOT RUN` (offline: PASS) |
| Projectile that will hit an enemy is ignored | `NOT RUN` (offline: PASS) |
| Accidental friendly fire → recovery toward the original enemy | `NOT RUN` (offline: PASS) |
| Correct friendly shot at an enemy → Guardian does nothing | `NOT RUN` (offline: PASS) |
| Harmless friendly miss → ignored | `NOT RUN` (offline: PASS) |
| Deliberate friendly force-attack on the GM **with a direct shot** → Return to Sender | `NOT RUN` (offline: PASS) |
| Deliberate friendly force-attack on a protected ally **with a direct shot** → hostile handling | `NOT RUN` (offline: PASS) |
| Friendly rocket drifting onto the GM → recovery, never returned to the ally | `NOT RUN` (offline: PASS) |
| Deliberate friendly rocket at the GM → still Friendly Explosive Recovery, never returned | `NOT RUN` (offline: PASS) |
| Multiple Guardians: best candidate selected, not grid order | `NOT RUN` (offline: PASS both directions) |
| Redirected shot avoids passing through protected friendlies | `NOT RUN` (offline: PASS) |
| GM and ally separated by a wall → no micro-dash | `NOT RUN` (offline: PASS) |
| 8 m/s ≈ 90%, distance modifies difficulty | `NOT RUN` (offline: PASS) |
| Micro-dash never relocates the pawn, over a sustained burst | `NOT RUN` (offline: 100 dashes, 0 movement, PASS) |
| Guardian visual effects appear and sound plays | `NOT RUN` — cosmetic; offline asserts the calls are made |
| No new persistent Guardian state; melee doctrine still saves | `NOT RUN` (offline: store round-trip PASS) |

Friendly-explosive ruling, added in 0.10.2 — likewise nothing observed in a running game:

| Explosive test | Status |
|---|---|
| Friendly grenade threatening the GM with raiders nearby → redirect at hostiles, never returned | `NOT RUN` (offline: PASS) |
| Friendly grenade with no valid hostile → safe disposal, never returned | `NOT RUN` (offline: PASS) |
| Grenade thrown deliberately at the GM → still Friendly Explosive Recovery | `NOT RUN` (offline: PASS) |
| Rocket force-fired deliberately at the GM → still Friendly Explosive Recovery | `NOT RUN` (offline: PASS) |
| Friendly rocket with an enemy cluster → favours the cluster, minimises friendly risk | `NOT RUN` (offline: PASS) |
| Friendly rocket with no enemies → safe disposal | `NOT RUN` (offline: PASS) |
| Friendly explosive threatening nobody protected → ignored | `NOT RUN` (offline: PASS) |
| Enemy rocket → unchanged Return-to-Sender → Safe Deflection | `NOT RUN` (offline: PASS) |
| **Regression:** Bob force-attacking with a rifle → still hostile, Return-to-Sender | `NOT RUN` (offline: PASS) |
| **Regression:** Bob's stray bullet → still Friendly Recovery toward the original raider | `NOT RUN` (offline: PASS) |
| Redirected warhead keeps fuse, radius, damage, def and launcher | `NOT RUN` (offline: def/launcher PASS; fuse preserved by the same code path as 0.10.0) |

Protected-pawn hard veto, added in 0.10.3 — likewise nothing observed in a running game:

| Safety test | Status |
|---|---|
| 20 raiders around one colonist → destination rejected, colonist not collateral | `NOT RUN` (offline: PASS) |
| Re-run at 60 raiders → answer does not move | `NOT RUN` (offline: PASS) |
| 20-raider unsafe cluster vs 2 clean raiders → the clean pair wins | `NOT RUN` (offline: PASS) |
| Rocket never redirected *through* a colonist; clear-line raider chosen instead | `NOT RUN` (offline: PASS) |
| Thrown grenade arcs over an in-line ally and is **not** vetoed for them | `NOT RUN` (offline: PASS) |
| Every raider shielded → no hostile redirect at all → safe disposal | `NOT RUN` (offline: PASS) |
| Safe deflection rejects a bearing landing on colonists, even one full of raiders | `NOT RUN` (offline: PASS) |
| Safety and threat detection agree on who counts as protected | `NOT RUN` (offline: PASS) |

Melee dodge chance, added in 0.10.4 — likewise nothing observed in a running game:

| Dodge test | Status |
|---|---|
| Melee 20 with enormous modded stats → vanilla 50% cap holds, GM21 does nothing | `NOT RUN` (offline: PASS) |
| Melee 21 at a vanilla 50% → ~99.9% effective | `NOT RUN` (offline: PASS) |
| Melee 21 at vanilla 10% / 25% / 40% → 99.82 / 99.85 / 99.88% | `NOT RUN` (offline: PASS) |
| Effective defence never exceeds 99.9%, even at a superhuman composite | `NOT RUN` (offline: PASS) |
| Natural attacker miss → no GM dodge event, no riposte from the miss | `NOT RUN` (offline: PASS) |
| Real GM dodge → attack fails, defence recorded, one riposte scheduled | `NOT RUN` (offline: PASS) |
| Downed / unconscious / stunned GM → vanilla value, no 99.9% | `NOT RUN` (offline: PASS) |
| Immobile but conscious GM → still defends, measurably worse | `NOT RUN` (offline: PASS) |
| Skill tooltip reports the real computed figure, not a hardcoded one | `NOT RUN` (offline: PASS) |
| Ranged dodge stats unchanged | `NOT RUN` (offline: structural PASS — no member names one) |
| Isekai RPG Duelist Counter Strike still observes the failed attack | `NOT RUN` — not installed in this environment |

Also still unexercised, from previous versions:

* **the 0.9.1 burst behaviour** — per-projectile re-evaluation in a live burst, burst
  cancellation once the target is down, and the "no safe target → hold fire" path
* the cleanup → uninstall → reload-without-the-mod cycle
* death-on-downed suppression, and its new intended-target narrowing
* `Player.log` free of Harmony patch failures in a real load
* Combat Extended detection and opt-out, for either package

The `Learn` transpiler's fail-safe is unchanged and still degrades to "no new Grandmasters" rather
than corrupting progression.

---

## Credits

Harmony by Andreas Pardeike (MIT). RimWorld by Ludeon Studios. MIT licensed — see `LICENSE`.
