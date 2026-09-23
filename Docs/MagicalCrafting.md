# Crafting 21: Transcendent craftsmanship

Magical, Mythical and Divine share one workstation-owned project system. They use the same
eligible recipe pool and require authoritative stored Crafting 21 for commitment, work and
completion. Research belongs to the colony and survives the Grandmaster's death.

## Progression

| Research | Prerequisite | Research cost | Workstation | Catalyst |
| --- | --- | ---: | --- | --- |
| Magical Craftsmanship | Fabrication | 6,000 | Magical workstation | Magical Catalyst |
| Mythical Craftsmanship | Magical Craftsmanship | 8,000 | Mythical workstation | Mythical Matrix |
| Divine Craftsmanship | Mythical Craftsmanship | 12,000 | Divine workstation | Divine Essence |

Build workstations from Production. Manufacture catalysts at a **fabrication bench** using its
ordinary bills after finishing the corresponding research. No DLC is required. Catalysts are
not Stuff, do not replace equipment material and are not stocked by traders. Their separate
stockpile category is **Transcendent catalysts**, outside vanilla resource ingredient categories.

| One manufactured catalyst | Materials | Crafting skill | Recipe work |
| --- | --- | ---: | ---: |
| Magical Catalyst | 50 plasteel, 10 gold, 2 advanced components | 12 | 18,000 |
| Mythical Matrix | 2 Magical Catalysts, 100 plasteel, 25 gold, 4 advanced components | 15 | 45,000 |
| Divine Essence | 3 Mythical Matrices, 200 plasteel, 75 gold, 10 advanced components | 18 | 90,000 |

Catalyst manufacture uses normal bill work speed. Only the subsequent transcendent project
requires a Grandmaster. Costs are provisional and centralized in `Defs/Transcendent/Progression.xml`.
Proof-item research gates are deferred; vanilla research prerequisites provide progression.

## Projects and outcomes

1. Choose a researched equipment recipe and its Stuff at the desired workstation.
2. Enable Crafting work for a legitimate Crafting Grandmaster. The pawn reserves the bench,
   hauls the normal ingredients and exactly one matching catalyst, then commits them.
3. Work can stop for needs, drafting or danger. Another valid Crafting Grandmaster can resume it.
4. Completion delivers one ordinary RimWorld item, always **Legendary**, retaining the selected
   material and original initiator's provenance. Craftsmanship and phenomenon are separate data.

| Ceiling | Nominal top roll | Downward fallback | Final nominal known distribution |
| --- | --- | --- | --- |
| Magical | 50% Magical | Legendary | 50% Magical / 50% Legendary |
| Mythical | 40% Mythical | On failure: 70% Magical, otherwise Legendary | 40% Mythical / 42% Magical / 18% Legendary |
| Divine | 30% Divine | On failure: 60% Mythical; then 80% Magical; otherwise Legendary | 30% Divine / 42% Mythical / 22.4% Magical / 5.6% Legendary |

All choices are made once at commitment. Pause, reload, worker change and completion never
reroll them. Legendary fallback has no phenomenon. One active project occupies each workstation.
Committed ingredients cannot be cancelled, refunded, salvaged or moved. Destruction loses the
project. Deconstruction is blocked while a project or retained items exist; benches cannot be
minified. A DEV abort destroys contents without refund.

## Work calibration

`max(original recipe work × multiplier, minimum)` uses the selected Stuff and native recipe
work-speed stats. Combined pawn and bench speed at or below 1 is unchanged; above 1 its square
root is applied only to project work. Invalid/nonfinite speeds stall.

| Tier | Multiplier | Minimum work | Hours at speed 1 | Twelve-hour working days |
| --- | ---: | ---: | ---: | ---: |
| Magical | 3 | 45,000 | 18 | 1.5 |
| Mythical | 7 | 120,000 | 48 | 4 |
| Divine | 15 | 270,000 | 108 | 9 |

These are working hours at 2,500 ticks per game hour, not elapsed world days. Travel, sleep and
needs add calendar time. Expensive recipes may exceed these minimum durations.

## Weapon phenomena

Each new transcendent weapon receives one compatible identity at commitment. Magical / Mythical /
Divine valid hits have **8% / 18% / 30%** manifestation chance, with a **180-tick successful-proc
cooldown**. One attack gets one opportunity even when it deals multiple damage packets. Identity,
seed, attempt count and cooldown persist on the actual item.

| Phenomenon | Provisional mechanics |
| --- | --- |
| Chain Lightning | Up to four distinct hostile pawns, six-cell maximum per jump, decreasing electrical burn injury |
| Smite | Direct blunt damage to hostile pawns within five cells; at most sixteen targets |
| Flame Wave | Controlled burn injuries within three cells; creates no fire |
| Frost Nova | Within four cells, movement ×0.6 for 300 ticks; does not stack |
| Gravity Crush | Extra blunt damage and a 90-tick stun if the target remains standing |
| Vampiric Strike | Extra stab damage, then heals nonpermanent wounds for 35% of damage dealt, capped at eight severity |
| Spatial Slash | High-penetration cut plus at most one nearby secondary hostile; no teleportation |

Targets must be standing, alive, hostile and on the wielder's map. Colony pawns and colony
prisoners are protected. Secondary targets need line of sight from the effect center; searches
use bounded map cells. Smite never attacks buildings or terrain. Generated damage cannot trigger
more phenomena. Other mods' normal damage/armor callbacks still apply.

Triggers support the audited vanilla melee-damage verb and `Bullet.Impact` paths, including
subclasses that call those implementations. Custom lasers, explosive projectile workers and
replacement combat verbs require adapters; merely discovering a modded weapon recipe does not
prove its custom attack path can trigger a phenomenon. Dead/downed victims do not start effects.
Apparel retains craftsmanship metadata but receives no offensive phenomenon in this version.

## Saves and compatibility

Existing bench DefNames, CLR type, job/toil order and save keys are retained. Schema-one Magical
projects preserve progress and outcome; old artifacts and already committed old projects do
**not** gain a newly rolled phenomenon. New schema-two projects also save their ceiling and
phenomenon seed. Invalid projects retain their state and fault without refunds/rerolls.

The artifact component travels with the normal equipment Thing, including holders, inventory,
caravans and transfers. Output-created and output-delivered latches retain the same item if
placement fails. Changed/removed item or recipe mods mid-project remain unsupported.

Recipe discovery retains its conservative checks and compact rejection summary. Ordinary recipes,
workstations, quality enum, Shooting, Melee and Grandmaster progression implementation are unchanged.
See [the discovery audit](MagicalRecipeDiscoveryFix.md) for the historical filter repair.

## Development testing and current evidence

DEV workstation menus can force the next permitted tier/phenomenon, inspect committed state,
finish work, or destroy a project without refund. Finish-work still requires a real Crafting
Grandmaster to complete/deliver it. Test overrides are session-only and consumed on commitment.
DEV artifact controls inspect state, set a compatible phenomenon and manually trigger it using an
equipped weapon and a hostile target. None of these controls appear in normal gameplay.

The owner runtime-tested the original Magical foundation in a heavily modded environment:
1,600 scanned recipes / 217 supported; hauling, catalyst consumption, active crafting save → quit
→ reload → resume → completion; Legendary plasteel equipment with separate Magical metadata.
That validates the base, not the new combat and progression additions.

The expanded headless suite uses real RimWorld/Unity/Harmony assemblies for probability, policy,
schema, XML, API/IL and Scribe save-writing checks. Steamworks is absent from the supplied set,
blocking native implicit-work evaluation and Scribe readback initialization. Live Harmony tests
cannot execute on this host's .NET 8 runtime with the supplied MonoMod build. No replacement
runtime stubs are used to claim feature correctness.

Before release, use a copied save to test each tier and each weapon phenomenon in game. Include:

- Catalyst bills and stockpile filters with Core only; research progression and each workstation.
- Active-work reload, changing Grandmasters, and a failed/blocked output placement followed by reload.
- Destruction and DEV abort without refunds, and deconstruction denial.
- Ordinary Legendary fallback with no power; Stuff and original initiator retained.
- Allies/prisoners/buildings inside Smite and Flame Wave areas; verify zero collateral effects.
- Burst fire, multiple damage packets, dead/downed victims, and weapon switching before bullet impact.
- Vampire healing on wounded/healthy/dead wielders and repeated procs without recursive chains.
- Equipped/inventory/apparel/caravan/map-transfer persistence and cooldown continuity.
- Existing Shooting/Melee behavior and ordinary nonartifact weapons.

Cosmetics, apparel powers, proof-item research gates, and custom combat adapters are deferred.
Balance is provisional. Final art, sound and elaborate visuals are intentionally absent.

## Removal

Remove all transcendent workstations and catalysts across maps and holders before preparing a
save for uninstall. Destroying active benches loses their projects. Skill cleanup does not remove
crafting objects or artifact data. Finished equipment keeps its original Def when this mod is
removed; its separate metadata disappears. Keep a backup for the untested full uninstall cycle.
