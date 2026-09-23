# Internal developer notes — contains implementation spoilers

This is developer-only reference, not player progression documentation. Do not copy secret
outcomes into the public README, research, workstation descriptions, chance UI or changelog.

## Architecture and migration

All three bench ThingDefs use the historical `Building_MagicalWorkstation` CLR name. Keeping that
name, JobDef, stable toil sequence and existing save keys avoids invalidating tested 0.11 saves.
Tier configuration maps public bench DefNames to ceiling, research, catalyst and work budget.
No duplicated tier-specific building or job implementation was introduced.

Project schema 2 adds ceiling (legacy default Magical), phenomenon (legacy default None) and its
seed. Existing committed tier, work and delivery latches stay authoritative. Old items/projects
receive no retroactive phenomenon. Artifact enum numeric values remain stable. Invalid identity
disables effects; invalid project state faults without recreating outputs or refunding receipts.

The transaction still stages all quantities before exactly one `Verse.Rand.Int` call. Stateless
integer mixing derives all subsequent rolls from that committed seed. Item generation copies the
locked values. The committed receipt includes the catalyst; `CompIngredients` excludes all three
catalysts from physical-material provenance. Output creation/delivery latches and retained owner
semantics remain unchanged.

## Resolution and balance

Magical preserves its original seed-parity 50/50 rule. Mythical uses 40% top success, then 70%
Magical on failure: final 40% / 42% / 18%. Divine checks two independent secret gates before its
30% Divine → 60% Mythical → 80% Magical chain. All chances are in `ArtifactRolls`.

1. Null succeeds with 0.0001 probability (0.01%).
2. If Null fails, Anomaly succeeds with 0.001 conditional probability (0.10%).
3. Otherwise the known chain runs. The common factor is 0.9999 × 0.999 = 0.9989001.

Actual unconditional Divine-project percentages:

| Result | Probability |
| --- | ---: |
| Null | 0.010000% |
| Anomaly | 0.099990% |
| Divine | 29.967003% |
| Mythical | 41.9538042% |
| Magical | 22.37536224% |
| Ordinary Legendary | 5.59384056% |

Only Divine ceiling allows secret tiers, even through bench DEV forcing. Results never change
vanilla Legendary quality. Artifact display reveals Anomaly as `Anomalous` and Null as `[N/0]`
only on an actually created item. Commitment logs reveal results only when Dev Mode is enabled.
Reliability by tier is 8%, 18%, 30%, 60%, 85%; damage scales 1, 1.4, 1.8, 2.4, 3. Successful procs
have a 180-tick cooldown. The saved attempt counter produces deterministic future proc rolls.

Minimum work is 45,000 / 120,000 / 270,000 at multipliers 3 / 7 / 15. At 2,500 work/hour and
12 working hours/day those minima are 1.5 / 4 / 9 working days. Costs live in Progression.xml.
Proof-item gating is deferred in favor of stable native research chaining.

## Combat and hostile safety

Five narrowly scoped methods are patched under the separate `ared.grandmaster21.artifacts` owner:

- `Bullet.Impact(Thing,bool)`: uses the native saved `Projectile.equipment` reference, not whatever
  weapon the pawn holds later. No new projectile save state is needed.
- `Verb_MeleeAttackDamage.ApplyMeleeDamageToTarget(LocalTargetInfo)`: uses the verb's EquipmentSource.
- `Pawn.PostApplyDamage(DamageInfo,float)`: consumes one qualifying hit per scope with positive
  actual damage and matching instigator/weapon definition.
- `Pawn.SpawnSetup(Map,bool)`: re-registers held rare items on map entry/load.
- `Pawn_EquipmentTracker.GetGizmos()`: forwards artifact testing tools only in Dev Mode.

The hit scope restores its previous value in a finalizer and preserves original exceptions.
Phenomenon dispatch uses a second thread-local depth guard restored in `finally`, plus generated
damage has no weapon Def provenance. All secondary targets are pawn-only, hostile, standing,
alive, same-map, with colony pawns/prisoners excluded. Damage rechecks target validity immediately
before applying. Searches are cell-bounded with LOS and stable distance/ThingID ordering.

Smite uses direct pawn damage, never GenExplosion. Flame and lightning use custom AddInjury
DamageDefs with Burn/Heat and no ignition, explosion or DLC references. Frost applies one
300-tick hediff with MoveSpeed ×0.6; it does not extend/stack existing frost. Gravity uses native
stun. Vampire heals only nonpermanent injuries and caps aggregate severity at eight. Spatial
Slash uses armor penetration 2 and at most one secondary target; it never moves a pawn.
Other mods' normal damage callbacks, including the existing combat mastery hooks, can modify
actual damage. Replacement/custom combat paths that do not invoke audited methods are unsupported.

## Rare manifestations and Dev controls

Anomaly items register with a map component from spawn/equip events and a one-time map-load walk.
Every 250 ticks the registry checks at most eight items; no pawn/pathfinding or per-frame scan.
First observation schedules an appearance rather than immediately showing one. Each item's next
check is persisted, spaced by 30,000–59,999 ticks. Only items whose held position is inside Home
can manifest. The placeholder is a Smoke fleck, not a Pawn or permanent Thing. Map transfers
remove/re-register entries; pawn spawn handles carried/equipped arrivals. No registry refs are saved.
Null has strong reliability and isolated persistent identity/display; no world/Def/UI mutation.

DEV menus force next result (including None), select next phenomenon, inspect committed state,
finish work (a GM must still deliver) or abort with no refund. Overrides are one-shot/session-only,
not save data. Committed rolls cannot be changed by the bench menu. Artifact controls inspect
state, set compatible phenomenon, manually trigger against a hostile while equipped, and manifest
an existing Anomaly item in Home. Normal gameplay never sees these controls.

## Validation and adversarial review

Feature suite: 272 passes, zero failures, two dependency-blocked probes on the supplied host.
It covers deterministic fallback boundaries, 200,000 Divine seeds, secret order/ceilings, work,
legacy project validity, artifact eligibility, bounded healing, recursion short-circuit, API/IL
contracts, one-project rejection, XML fields/references, stockpile category, translations and
Scribe save-writing. Native readback remains blocked: ParseHelper initializes Steamworks types.
No substitute parser or fake feature runtime was installed. Full map save/reload is not claimed.

Existing suites: offline progression 57 / Shooting 74 / Melee 295 passes; real-DLL progression
26 passes; Learn transpiler inspection passes. Existing runtime-target checks: 168 passes and
one failure due to missing Steamworks. Live PatchAll/finalizer tests abort due to the supplied
MonoMod's ReflectionHelper calling an inaccessible .NET 8 SignatureHelper API.

Review found and fixed the equipped-gizmo forwarding gap and missing stockpile category. New
catalysts now use their own Root category, without joining ordinary resource/manufactured pools.
The base hauling/owner transaction, authorization and output-delivery code remain structurally
unchanged. There are no changes to Shooting/Melee/progression/quality implementation. Public
crafting docs/defs/translations contain no secret-tier names or probabilities.

Outstanding real-game gates: manufacture/research/load Defs; all seven effects and each forced
result; held/equipped/caravan persistence; weapon switch while projectile in flight; sparse Home
manifestation; destructive/retained-output edge cases and combat-mod interactions. Core XML, the
Unity player and Steamworks were not supplied. XML metadata/reference checks cannot replace
loading the actual installed mod list. The owner tested the prior Magical foundation, not these
new mechanics. Art, elaborate VFX, apparel effects and custom combat adapters remain deferred.
