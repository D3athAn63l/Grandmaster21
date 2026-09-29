# Melee 21: Beam Parry — 0.14.0 Beta

A Melee Grandmaster cannot outrun light. They do not need to. By reading the shooter's stance,
weapon alignment and firing line before emission, they place their weapon at the moment of
discharge. The reflected flash overloads optics or senses; it does **not** reflect damage.

**One attack, one defensive decision.** Success stops the attack and attempts a short counter-stun.
Failure leaves the beam's damage, fire and burst behavior vanilla, without another attempt.

## Rules and tuning

- Legitimate stored Melee 21, `Gm21Melee.CanAct`, positive manipulation and consciousness, and an
  equipped weapon are required. Aptitude cannot manufacture or remove Grandmaster status.
  Sleeping, downed, unconscious or stunned Guardians cannot act. An unarmed pawn cannot beam-parry.
- Reuses `Gm21GuardianThreat.Protects`, the existing **three-tile** radius and `Gm21Reach.CanDashTo`.
  Self, same-faction pawns and formal allies qualify; neutral bystanders do not automatically qualify.
- Uses the existing best-candidate policy with beam-specific scoring. One local grid search selects
  the highest chance; additional Guardians do not create additional rolls. It does not move pawns.
- `quality = Precision × Consciousness × DeflectionImplement`; `chance = min(0.95, quality / (quality + 1))`.
  Fixed difficulty 1 models prediction and weapon placement. With ordinary capacities, a 3 kg melee
  weapon gives 60%; a 3 kg ranged implement gives about 45.2%. Better capacities improve the chance;
  nonfinite quality fails open. These are initial tuning values, not gameplay-validated balance.
- Only a positive, health-harming beam contact on a living pawn can consume the decision. The **first
  such pawn contact** consumes it even if no Guardian is available. No later target or newly arrived
  Guardian can fish for another roll in that burst. Objects/empty cells before this contact remain
  vanilla; previously resolved effects are not undone.
- Successful defence blocks the current contact and all remaining contacts in that attack, including
  neighbour cells and ground fire at the defended contact. It is not timed beam immunity.
- Counter-stun target is **120 ticks** before normal immunity/resistance/definition adjustments.
  Hostile shooters or shooters deliberately targeting a protected pawn receive the attempt. Accidental
  allied/neutral stray fire is defended without counter-stun. No biological-eye check exists.
- Localized `GM21_BeamParried` text provides feedback. No new settings, scheduler, global scan,
  save state, migration, or GameComponent. Projectile Defence formulas and implementation are unchanged.

## Audit of the supplied real assemblies

Audit performed before implementation using Mono.Cecil, then checked by `Tests/BeamParryChecks.cs`.
The executable tests use the supplied assemblies, not replacement combat stubs.

| Input | SHA-256 |
|---|---|
| Assembly-CSharp.dll | `5cf1b5be399d5b1c9c56ca72c9d35b4ecf307feacf5859d04ac5a1aa5926356a` |
| 0Harmony.dll (2.4.1) | `353daafec180bb8e7bbe4da78f2a7cdc78067392e3a4e79dc8e7af295f2371e6` |

`Verse.Verb_ShootBeam` and `Verse.Verb` are in **Assembly-CSharp.dll**, as is `RimWorld.StunHandler`.
There is no separate Biotech beam assembly or DLC gate on these methods. Weapon definitions select
which verbs use them; the implementation does not require a weapon defName or DLC whitelist.

| Actual method | Observed behavior |
|---|---|
| `Verb_ShootBeam.WarmupComplete()` | Sets `burstShotsLeft = ShotsPerBurst`, `state = Bursting`, initial target and path; clears hit cells; creates optional mote; calls `TryCastNextBurstShot`; then prepares beam sound/effect bookkeeping. |
| `Verb.TryCastNextBurstShot()` | Calls virtual `TryCastShot`. True decrements remaining shots. False sets remaining shots to zero. Completion sets state Idle, applies normal cooldown where applicable, invokes the completion callback and handles burst fuel. |
| `Verb.VerbTick()` | During Bursting, despawn or a stunned pawn calls virtual `Reset`; otherwise advances shot timer, calls Next when due, then calls `BurstingTick`. It does not recheck state before that final cosmetic tick. |
| `Verb_ShootBeam.TryCastShot()` | Resolves line/hit cell, calls `HitCell` for the principal cell and optional neighbours. `hitCells` deduplicates neighbours across the burst. Damage can occur repeatedly across shots and more than once per shot. |
| `Verb_ShootBeam.HitCell(IntVec3, IntVec3, float)` | Chooses a Thing through `VerbUtility.ThingsToHit` and `RandomElementWithFallback`, calls `ApplyDamage`, then may start ground fire. |
| `Verb_ShootBeam.ApplyDamage(Thing, IntVec3, float)` | Actual victim is the Thing argument. Builds DamageInfo from `caster`, `currentTarget.Thing`, beam DamageDef and equipment. Total/path-cell or default damage is multiplied by the damage factor, then `Thing.TakeDamage` is called. It may attach/start fire afterward. |
| `Verb_ShootBeam.BurstingTick()` | Maintains mote, end effecter, flecks and sustainer. **No HitCell/ApplyDamage calls** in this method. Visuals and sound are independent of damage. |
| `Verb.Reset()` | Clears state, current targets, shot counts/timer and callback. ShootBeam does not override it. Calling this in the middle of the current damage loop would invalidate state that the caller still uses. |
| `StunHandler.Notify_DamageApplied(DamageInfo)` | Checks `CanBeStunnedByDamage`, normal adaptation and stun resistance, then calls `StunFor`. With no definition override, amount is multiplied by 30 ticks. |
| `StunHandler.StunFor(...)` | Sets stun ticks and feedback/log fields. It neither resets verbs immediately nor checks damage stun immunity itself. |

**Normal stun ends the beam on its next VerbTick, not during the current synchronous shot.** It is
insufficient for the remaining neighbour hits, and cannot terminate a stun-immune attack. Therefore
Beam Parry makes the *current shot result* false after a successful defence and lets vanilla's own
Next method complete the burst. It never resets every verb, cancels jobs, or edits attacker AI.

### Gun_BeamGraser data boundary

**BLOCKED in this environment:** the supplied archive contains assemblies, not the game `Data/`
folder. A DLL cannot prove the XML binding of `Gun_BeamGraser`. The implementation contains no
weapon-name special case. `verify-beam-parry.sh` accepts an optional fourth argument pointing to an
installed `RimWorld/Data` and asserts that the actual weapon definition declares `Verb_ShootBeam`.
This check and the in-game graser checklist below remain necessary before claiming that specific
weapon was verified. Third-party logs are not treated as proof of the owner's installed definition.

## Hooks and transient state

The independent Harmony owner is `Grandmaster21.BeamParry`. Startup enables it after the existing
Melee passive group, using its own `BeamParryEnabled` flag, also printed in the startup log. Missing
signatures or unexpected required call counts roll back **only this owner's hooks** and log a warning.
The Projectile Defence flag and patches are not changed.

| Hook | Purpose |
|---|---|
| ShootBeam `WarmupComplete` prefix + finalizer | Creates a fresh Attack object in a `ConditionalWeakTable<Verb, Attack>`; removes it on setup failure/completion. |
| Verb `TryCastNextBurstShot` prefix + finalizer | Opens/restores a thread-local linked scope for that verb and attack; removes the entry on completion or exception. Non-beam verbs allocate no scope. |
| Verb `TryCastNextBurstShot` transpiler | Requires exactly one call to virtual `TryCastShot`, then passes its bool result through `ShotResult`. Only a defended beam converts true to false. Other verbs retain their exact result. |
| ShootBeam `ApplyDamage` prefix | Resolves the first relevant contact and one roll before any original damage/attached fire. Evaluated is set **before** Guardian resolution, RNG or counter-effects. Defended contacts skip the original method. |
| ShootBeam `HitCell` prefix + transpiler | Skips subsequent hit cells after success; after the unique ApplyDamage call, returns before ground fire if the attack was defended. |
| Verb `Reset` prefix + finalizer | Captures and removes the current entry on external interruption. |

The unique attack identity is the **Attack object created by one WarmupComplete**, not pawn, target,
weapon, tick number or damage event. Both successful and failed decisions survive repeated contacts.
Weak keys cannot retain unused verbs; values contain only outcome flags. Scopes restore in finalizers.
Removal checks reference identity, so an older completion cannot delete an attack started reentrantly
by a completion callback, even on the same verb. Exceptions during a failed selection consume the
decision and fail open. Stun/FX exceptions cannot undo an already successful defence.

No entry is created opportunistically inside ApplyDamage. A loaded mid-burst verb with no transient
entry proceeds vanilla until its next new WarmupComplete; it never gains a retry by loading a save.
No serialization is added. Natural completion and Reset release the state; weak ownership is the
backstop for abandoned verbs.

### Interruption, stun and visuals

On success the ApplyDamage prefix blocks damage and attached fire. HitCell's continuation guard
blocks that cell's ground fire. Its prefix blocks all subsequent cells in the current shot. Next then
sees a false shot result and performs **vanilla burst completion**. Future VerbTicks cannot fire the
completed burst. The normal completion callback/cooldown/fuel path is retained.

Counter-stun calls the attacker's `stances.stunner.Notify_DamageApplied` with a **Stun** DamageInfo,
amount `120 / 30`, and Guardian as instigator. This is a notification to the normal stun handler:
**no TakeDamage, damage worker, reflected projectile, or copied beam is invoked**. Vanilla/modded
immunity, resistance, adaptation and StunFor patches remain effective. A non-pawn caster can still
have its attack stopped but has no pawn stun handler to notify.

Vanilla may finish one cosmetic BurstingTick after a shot completes, or prepare its maintainer-based
sound after the first shot. Existing beam visuals/sound expire normally when no longer maintained,
just as on vanilla completion; no copied visual teardown or perfect reflected beam is attempted.
Visual timing requires owner verification in Unity.

### Compatibility limits

Weapons with any defName using these methods are supported. Subclasses that retain/call the base
WarmupComplete, Next, HitCell and ApplyDamage pipeline share the same hooks. A fully custom verb,
an override that bypasses the burst lifecycle/damage path, or damage applied directly outside the
scoped Next call remains vanilla. Other mods that replace these method bodies or change callback
semantics may need a dedicated adapter. No universal compatibility claim is made. The existing
combat-overhaul/passive Melee gate applies to Beam Parry as well.

## Verification (2026-09-29)

Build with the established Roslyn compiler against the real supplied RimWorld/Unity/Harmony DLLs.
Run:

```bash
./tools/verify-beam-parry.sh "$GM21_MANAGED" "$GM21_HARMONY" "$CECIL_DLL" [RimWorld/Data]
```

**110 PASS, 0 FAIL; one XML binding check BLOCKED (Data absent).** Includes real IL inspection and
actual production Harmony installation on pristine game methods. The harness executes real beam
WarmupComplete, Next, TryCastShot, HitCell, ApplyDamage, Reset, VerbTick and the normal stun
notification/StunFor path. It verifies success, failure, one roll, exact unchanged failure damage
and fire, no reflected damage, subsequent tick suppression, fresh casts, immune attackers,
self/Guardian defence, accidental versus deliberate friendly fire, harmless beams, incapacitation,
aptitude/stored skill, radius/routes, multiple Guardians, nested verbs, same-verb completion
callbacks, exceptions, loaded mid-burst behavior and isolated rollback on changed IL.

Headless shims provide map/grid contents, capacities, equipment stats, deterministic RNG, beam path
geometry, visual/log services and damage recording. The real Thing.TakeDamage boundary is recorded,
not a running health simulation. CanBeStunnedByDamage is controlled for an immune-caster fixture;
normal notification/StunFor run. A test subclass only overrides CasterIsPawn to avoid live stance
managers: real shot-count/state completion executes; live pawn cooldown presentation is IL-audited
and remains a runtime check. Test shims are never shipped. Unity native-call warnings during Harmony
JIT are expected headlessly; they are not evidence of gameplay verification.

| Existing suite | Result |
|---|---:|
| Offline core / Shooting / Melee (all projectile/other Melee regressions) | 58 / 74 / 295 PASS, 0 FAIL |
| Real runtime target resolution | 271 PASS, 0 FAIL |
| Real binding | 39 applied, 0 failed, 6 environment-blocked |
| Skill Learn transpiler | PASS |
| Finalizer semantics / progression | 15 / 26 PASS, 0 FAIL |
| Aspirant | 91 PASS, 0 FAIL |
| Medicine | 344 PASS, 0 FAIL (optional Data audit not run) |
| Transcendent Crafting | 604 PASS, 0 FAIL |
| Crafting/Construction Legendary notifications | 166 PASS, 0 FAIL |

The existing six binding blocks are two Skill UI methods, Projectile.TickInterval, two tend-duration
methods, and Frame.CompleteConstruction requiring Unity initialization. Beam Parry's own real binding
and execution are checked by its separate suite. No existing suite was deleted or weakened.

## Short owner runtime checklist — NOT RUN here

1. Boot the shipped **0.14.0 Beta** build. Confirm its source stamp against the PR's source commit,
   `beam=True`, and no new red errors in Player.log.
2. With Biotech active, use a vanilla `Gun_BeamGraser` attacker against an armed, awake stored Melee
   21 Grandmaster. Observe a successful “Beam parried”: the attack ends, the GM avoids its remaining
   damage, and the attacker visibly stuns/recalibrates. Check beam/sound expiry.
3. Observe a failed defence: ordinary damage continues, with no later parry in that same burst.
4. Target an ally within three tiles of an eligible Guardian; observe Guardian defence. Down/stun
   or remove the GM and confirm vanilla beam behavior. If available, use a stun-immune attacker:
   successful defence must still end its beam.
5. Fire an ordinary projectile weapon and confirm existing interception/return behavior.
6. Check Player.log for Beam Parry errors. No large torture-test matrix is required.
