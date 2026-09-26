# Medicine 21 — Grandmaster Physician

> **Experimental vertical slice, new in 0.12.0 Beta (second design pass plus the final pre-runtime
> pass). Not yet verified in a running game.**
> It builds against the real RimWorld 1.6, Unity and Harmony assemblies. Its logic is checked
> headlessly, including against every vanilla Def in all six content packs. The first real-game
> session found one bug, a vanilla bill refusing the Grandmaster ("Above allowed skill 20"), now
> fixed in the core mod (§4) but not yet re-tested in game; the rest is unverified at runtime.
> Every tuning number below is **provisional**.

*"I do not need miraculous technology to practice miraculous medicine. I am the Grandmaster."*

The extraordinary capability belongs to the **pawn**. Medicine 21 adds no research, items,
buildings, implants, techprints or special consumables. The only Defs it ships are three JobDefs for
the Grandmaster's personal interventions, which use ordinary medicine and the Grandmaster's time, and
one HediffDef, the shock a resuscitated pawn wakes from. A Medicine Grandmaster is a pawn with **stored** Medicine
level 21, read through the same `Gm21.IsGrandmaster` rule as every other capstone. Positive aptitude
cannot create one, and negative aptitude cannot remove one. A Grandmaster who cannot currently use
their hands or is unconscious falls back to vanilla medicine, as Shooting 21 does.

---

## 1. Grandmaster Medicine Utilization

At startup (`Gm21MedicineStartup`) every loaded ThingDef that vanilla itself counts as medicine is
read once. That is `ThingDef.IsMedicine`: `MedicalPotency` in its stat bases. Each one's
`MedicalQualityMax` is recorded. That stat is the ceiling `TendUtility.DoTend` clamps tend quality
to, and RimWorld represents it as a fraction (0.70 = 70%). The Grandmaster target for each medicine
is computed once and cached in a `Dictionary<ThingDef, float>`, so tending costs one lookup. Nothing
is keyed on DefNames and no foreign Def is modified.

**The rule**, for a medicine of ordinary cap `C`:

```
required = C × 1.30                         (Gm21Medicine.MeaningfulTierMultiplier)
target   = lowest distinct loaded cap ≥ required
           otherwise C + 0.30               (Gm21Medicine.TopTierFallbackBonus)
```

Caps within 0.001 of each other count as one tier, so duplicates cannot matter.

| Hierarchy | Medicine | Ordinary cap | Required | Grandmaster target |
|---|---|---|---|---|
| Vanilla (audited: all six packs ship exactly these three) | Herbal | 70% | 91% | **100%** |
| | Industrial | 100% | 130% | **130%** |
| | Glitterworld | 130% | 169% | **160%** (fallback) |
| Modded 70 / 85 / 100 / 130 | 70 | 70% | 91% | **100%** — 85 is not a meaningful step |
| | 85 | 85% | 110.5% | **130%** — 100 does not qualify |
| | 100 | 100% | 130% | **130%** |
| | 130 | 130% | 169% | **160%** (fallback) |
| A genuine 115 tier exists | 85 | 85% | 110.5% | **115%** — a qualifying tier is used |

**No medicine.** No bare-hands tier is invented. A Grandmaster tending without medicine gets
vanilla's random roll under vanilla's 70% no-medicine ceiling, exactly like any doctor. That tend
replaces the condition's regimen, so any Grandmaster Treatment on the conditions it touches is
cleared.

## 2. Deterministic Grandmaster Tend Quality

In 1.6 the only randomness in a tend lives in `HediffComp_TendDuration.CompTended`:

```
tendQuality = Clamp(quality + Rand.Range(-0.25, 0.25), 0, maxQuality)
```

`TendUtility.DoTend` is wrapped in a frame (prefix and void finalizer, nesting-safe) that knows the
doctor and the medicine. Two things then happen while a Grandmaster frame is open:

1. **Effective quality reaches the condition itself.** `DoTend` calls `Hediff.Tended(quality,
   maxQuality, batchPosition)` at exactly one site (verified against the 1.6 IL). A transpiler
   rewrites that single call to `Gm21GrandmasterTend.TendedEffective`, which passes
   `quality = maxQuality = target` in a Grandmaster frame and the original arguments otherwise.
   So every consumer of the tend — condition-specific overrides such as `Hediff_HeartAttack.Tended`
   and other mods' comps — sees **100 / 130 / 160%**, not the medicine's ordinary ceiling. If the IL
   ever has anything but exactly one such call, the transpiler changes nothing and the feature
   disables itself with one log line.
2. **No roll.** A prefix on `CompTended` rewrites only that comp's two arguments:
   `maxQuality = target`, `quality = target + 0.25 + 0.05`. Vanilla's own clamp then produces
   **exactly** the target for every possible roll. Vanilla still writes `tendQuality`, accumulates
   `totalTendQuality`, sets `tendTicksLeft` and throws its "Tended … Quality 100%" mote, all with
   the exact value.

* Herbal → exactly 100%, industrial → exactly 130%, glitterworld → exactly 160%.
  *The Grandmaster is not lucky. The Grandmaster is correct.*
* **Ordinary doctors are untouched.** No frame opens and the prefix is a single bool test.
* **Self-tend** keeps vanilla's ×0.7 self-tend factor, applied to the target and still exact
  (herbal 100% → 70%). Treating yourself is a physical limitation.
* The bed's `MedicalTendQualityOffset` is not added. Vanilla adds it before clamping to the ceiling,
  and the Grandmaster result *is* the ceiling.
* **Heart attack** (`Hediff_HeartAttack.Tended` rolls `0.65 × quality`): herbal 100% → 65%,
  industrial 130% → 84.5%, glitterworld 160% → certain success. Measured headlessly over 6,000
  real tends each. The heart-attack roll itself stays vanilla's; only its input is the Grandmaster
  quality.
* **No medicine, or an ordinary doctor:** no frame, so the call passes vanilla's arguments through
  untouched.
* Tend quality above 100% keeps its natural vanilla consequences. Diseases whose tended severity
  change is `severityPerDayTended × tendQuality` regress faster at 130–160%.

## 3. Grandmaster Treatment

A condition actually tended by a Grandmaster **with medicine** gets a Grandmaster Treatment. The
state belongs to **that specific Hediff**, not to the pawn:

* **Refresh, never stack.** A Grandmaster re-tend overwrites the one entry. It never multiplies
  regimens.
* **Replace.** Any other tend of that Hediff clears it: an ordinary doctor, a Grandmaster without
  medicine, the Coagulate psycast, Biotech clotting. The regimen is the *current* treatment, not a
  blessing.
* **Active** only while the vanilla tend underneath is in force (`tendTicksLeft > 0`). Vanilla
  injuries are permanently tended until healed. Vanilla diseases re-tend every 12 hours, and a
  lapsed regimen does nothing.
* Unrelated conditions on the same pawn (the carcinoma next to the treated gunshot) get nothing.

**Effects, decided by mechanism, never by DefName:**

| Mechanism | Effect |
|---|---|
| `Hediff_Injury` | Recovery. Vanilla heals one random tended injury every 600 ticks through `Hediff_Injury.Heal`. When the chosen injury has an active regimen the amount is multiplied, so *that* injury's expected recovery rate is m× vanilla. Nothing heals instantly. |
| Immunizable disease that can build immunity (vanilla's `PossibleToDevelopImmunityNaturally`) | Immunity. `ImmunityRecord.ImmunityChangePerTick` receives the disease instance, and a positive gain against that instance is multiplied. The body still has to win. |
| Anything else (e.g. asthma, whose Immunizable comp only drives severity) | State only. It fails safe with no invented effect. |

**Provisional curve** (`Gm21Medicine.RecoveryMultiplierFor`, one straight line, clamped to ×1.0–×3.0):

| Effective quality | 70% | 100% | 130% | 160% | 190% |
|---|---|---|---|---|---|
| Recovery / immunity | ×1.00 | ×1.25 | ×1.50 | ×1.75 | ×2.00 |

"Natural" recovery means heals issued from inside `Pawn_HealthTracker.HealthTickInterval`: tended
healing, plus Anomaly's hediff-driven regeneration of that same injury. Heals from psycasts, serums,
dev tools or other mods outside the health tick are not scaled.

A tended injury already stops bleeding in vanilla, so no separate stabilisation system was added.

The condition's tooltip shows the active regimen, e.g.
*"Grandmaster Treatment (130%): this injury recovers ×1.50 faster"*.

## 4. Perfect Grandmaster Surgery

`Recipe_Surgery.CheckSurgeryFail` asks the recipe's `SurgeryOutcomeEffectDef` for an outcome, and a
failure has already happened by the time the answer comes back. So the hook is
`SurgeryOutcomeEffectDef.GetOutcome` itself. For a practising Medicine Grandmaster it becomes an
equivalent walk that:

* computes quality and runs comp `PreApply` exactly as vanilla does (an Inspired Surgery inspiration
  is still consumed);
* treats quality as at least 100%;
* **never evaluates a failure outcome**: anything flagged `failure`, any `SurgeryOutcome_Failure`
  (including `FailureWithHediff`), any `SurgeryOutcome_Death`;
* returns the first non-failure outcome that applies, or null, which `CheckSurgeryFail` treats as
  success.

Audited against vanilla data: `SurgeryOutcomeBase` (103 recipes) lists success first, and its six
failure and death outcomes are all flagged `failure`. `SurgeryOutcomeMinorFailure` is the same. The
only other non-failure outcome in vanilla is Biotech's xenogerm-implant coma, whose duration curve
runs from 3 days at 0% quality to 1 day at 100%, so the Grandmaster gets vanilla's own best case.

**Untouched:** bills, recipe availability, part and patient validity, ingredients and their
consumption, the surgery job and its work time, anaesthesia, medical-care restrictions, and every
successful consequence the recipe worker applies. A Medicine 20 surgeon, and every other surgeon,
reaches vanilla `GetOutcome` unchanged, including the 98% cap.

**Getting to the operation at all.** Vanilla bills allow skills 0–20, and `Bill.PawnAllowedToStartAnew`
refused a Medicine 21 surgeon with *"Above allowed skill 20"* before any surgery job existed — the
first real-game session found this on "Remove prosthetic". That gate belongs to every bill type, not
to Medicine, so it is bridged in the core mod (`Patch_BillSkillCeiling`, see the README's *Vanilla
bills and level 21*): a Grandmaster in the recipe's work skill passes a max-20 bill; a bill capped
below 20 still refuses them; no bill is modified. Nothing above changed.

## 5. The Grandmaster Medicine command

One vanilla `Command` per Medicine Grandmaster: a player-faction colonist with stored Medicine 21.

* **Left-click** opens a three-entry FloatMenu to choose **Cure / Reconstruct / Resuscitate**.
  Choosing a mode works the same way on all three GM21 gizmos.
* **Right-click** activates the selected mode and enters vanilla targeting. This is vanilla's own
  route: a Command without right-click menu options receives the right-click event in
  `ProcessInput`, as vanilla's `Designator_Plan_Add` does.
* The mode is saved per pawn (`gm21MedicineMode`, default Cure, omitted from the save when default).
* The command is disabled, with a reason, while the Grandmaster cannot practise.

The UI holds no medical logic. It displays exactly what `Gm21CureCandidates`,
`Gm21ReconstructCandidates` and `Gm21Resuscitation` return, and hands the choice to a job.

### Intervention jobs

The Grandmaster owns the job (`GM21_MedicineCure`, `GM21_MedicineReconstruct`,
`GM21_MedicineResuscitate`), modelled on `JobDriver_TendPatient`. There is **no WorkGiver**:
ordinary doctors can never receive these jobs, and the doctor must be a practising Medicine
Grandmaster on every tick of the work. **Patients:** living flesh pawns on the same map that belong
to the colony (the Grandmaster included), prisoners of the colony, or anyone downed. Never
mechanoids, mutants or Anomaly entities. The selected condition travels in `Job.source`, vanilla's
`ILoadReferenceable` slot, so a job saved mid-intervention reloads pointing at the same condition.

**The price of an intervention is the Grandmaster's time and ordinary medicine. Nothing else.**
There is no cooldown, no charge, no per-pawn or per-day limit (a test asserts that no such field
exists and that the only persisted state is the treatment, the mode and the driver's work clock).
A Grandmaster who spends the day curing is a Grandmaster who did not spend it tending or operating.

#### Medicine cost

Each intervention needs a **potency budget** of ordinary medicine, measured in `MedicalPotency`, the
stat vanilla already uses to rate medicine. All numbers are provisional and live in `Gm21Medicine`.

| Intervention | Budget | Herbal (0.6) | Industrial (1.0) | Glitterworld (1.6) |
|---|---|---|---|---|
| Cure | 1.0 | 2 | 1 | 1 |
| Reconstruct | 2.0 | 4 | 2 | 2 |
| Resuscitate | 3.0 | 5 | 3 | 2 |

(Unit counts audited from the real vanilla medicine Defs.) Stacks combine — industrial 1 + herbal 4
meets 3.0 — and any modded medicine takes part by its own loaded potency. Potency decides **how much**
medicine is used, never how well the intervention works.

* **Carried medicine first.** Potency changes only the quantity, so better medicine elsewhere is no
  reason to leave the patient. If what the Grandmaster already carries meets the budget, the whole
  plan comes from the inventory and the map is not even searched — three carried industrial
  medicine resuscitate without a trip past the glitterworld in the next room. Only when the carried
  medicine falls short is **all** of it used and the rest gathered from the map
  (`Gm21MedicineSupplies.PlanInventoryFirst`).
* **Within** the inventory, and within the map, vanilla's `HealthAIUtility.FindBestMedicine` order
  applies: best allowed medicine first, nearer before farther, then by thing ID, so the plan is
  deterministic. Forbidden medicine is never touched; at most 8 map stacks are gathered for one
  order.
* **Medical care is respected** for a living patient: their own setting, or — for a patient the
  colony has no setting for yet, such as a downed raider — vanilla's default for their group from the
  Medical Defaults dialog. Lower it to make the Grandmaster use cheaper medicine.
* **One documented override: Resuscitate.** A corpse's medical-care setting cannot be edited (the
  health tab hides it once a pawn is dead), so a stale "no medicine" would make a dead colonist
  impossible to save. Resuscitation may use any unforbidden medicine; forbid the stacks you want
  kept out of it.

**Lifecycle** (`JobDriver_Gm21Intervention`):

1. **validate** — the order and `TryMakePreToilReservations` run the full validation, and the order
   plans the medicine first: an order that cannot gather its budget is refused with the reason
   ("needs 3 medicine potency, 1.8 available") before the Grandmaster takes a step;
2. **reserve** — the patient or corpse, and every planned stack with vanilla's stack-count
   reservation (the same `maxPawns` vanilla tending uses);
3. **acquire** — walk to each planned stack and take exactly the planned count into the Grandmaster's
   inventory (`Toils_JobTransforms.ExtractNextTargetFromQueue` → `Toils_Haul.TakeToInventory`), then
   confirm the carried medicine meets the budget before going on;
4. **approach** the patient (a Grandmaster treating themself stays put);
5. **work** — timed, with the vanilla tend sound, a progress bar and Medicine as the active skill;
6. **apply** — re-validate everything, re-plan from the carried medicine, apply the intervention;
7. **consume** — only after a successful apply, destroy exactly one budget's worth
   (`Thing.SplitOff(n).Destroy()`), once. The result message lists it ("Medicine used: industrial
   medicine x1").

**Interruption is safe.** Nothing is applied or consumed before step 7. Medicine already collected
stays in the Grandmaster's inventory — never destroyed, never duplicated — where vanilla tending can
use it and a new order uses it first. `Consume` has exactly one call site in the assembly, behind the
apply's result (asserted on the IL).

#### Work time

The main balance lever. Base work at `MedicalTendSpeed` 1 (vanilla tend is 600 ticks; a heart or
cataract operation 4,500 work):

| Intervention | Base ticks | In-game time at speed 1 | A Grandmaster at 1.6 speed |
|---|---|---|---|
| Cure | 2,500 | 1 h | ~37 min |
| Reconstruct | 6,000 | 2.4 h | 1.5 h |
| Resuscitate | 7,500 | 3 h | ~1.9 h |

`ticks = base ÷ MedicalTendSpeed`, exactly, with **no upper clamp**: speed 10 is base ÷ 10, speed
25 is base ÷ 25, and every point of tend speed the player invests keeps paying off until the work
reaches the **60-tick floor** (`Gm21Medicine.MinWorkTicks`), the only effective limit. Safety only at
the bottom: a zero, negative or tiny speed counts as 0.1 (work ×10, never stalled or inverted), and a
non-finite speed counts as 1. The command's tooltip shows the cost for that Grandmaster.

#### Self-intervention

* **Self-Cure** is allowed whenever the Grandmaster can practise (conscious, able to manipulate).
* **Self-Reconstruct** additionally needs **at least 50% manipulation**
  (`Gm21Medicine.SelfReconstructMinManipulation`). Losing one arm leaves exactly 50%, so a one-armed
  Grandmaster can rebuild the other arm; with no working hands, no.
* It uses vanilla's own self-tend pattern: the job path ends on the Grandmaster's own cell, and the
  facing and reach checks are skipped for self. Vanilla's `selfTend` toggle is not consulted — that
  setting governs automatic self-tending, and these are explicit orders.
* **Self-Resuscitate** is impossible: the doctor is dead.

## 6. Cure

Cure removes **one** selected pathological condition; the rest of the patient is untouched. Vanilla's
`cureAllAtOnceIfCuredByItem` is honoured, so every instance of *that* condition goes. Filtering is
deliberately conservative: **false negatives over dangerous false positives**, and `isBad` is never
taken to mean "disease".

**Excluded:** hidden (undiagnosed) conditions; not bad; `everCurableByItem = false`; injuries
(ordinary Grandmaster tending); missing parts (Reconstruct); implants, added parts, bionics and
prosthetics; addictions and chemical dependencies; pregnancy, labour and reproduction; everything
the Anomaly pack defines; and any condition whose runtime class is a custom Hediff subclass, which
GM21 cannot interpret.

**Offered only with a positive signal vanilla itself defines:**

1. immunizable-disease machinery (`HediffCompProperties_Immunizable`);
2. `HediffDef.chronic`, vanilla's chronic-illness flag;
3. a *tendable sickness*: `makesSickThought` **and** `HediffCompProperties_TendDuration`;
4. a narrow vanilla adapter, currently exactly one: **Food Poisoning** (`HediffDefOf.FoodPoisoning`).
   The audited XML confirms it has no immunizable comp, no `chronic` flag and no sick flag, so no
   generic signal can see it.

**Audited vanilla result** (all 324 vanilla HediffDefs, all six packs, judged by the real C# rule).
Cure offers exactly these 28:

> Alzheimer's, animal flu, animal plague, asthma, bad back, blindness, blood rot, carcinoma, cataract,
> dementia, fibrous mechanites, flu, **food poisoning**, frail, gut worms, hearing loss, heart artery
> blockage, infant illness, lung rot, malaria, muscle parasites, organ decay, plague, scaria infection,
> sensory mechanites, sleeping sickness, toxic buildup, wound infection.

Cross-check: vanilla Ideology's biosculpter "conditions to possibly cure" list (asthma, bad back,
cataract, blindness, frail, hearing loss, heart artery blockage) is entirely contained in it.

**Known conservative omissions:**

* cirrhosis and chemical damage (no generic signal);
* heart attack (custom class, acute and vanilla-tendable);
* blood loss, hypothermia, heatstroke and malnutrition (environmental, or driven by bleeding);
* psychic states, cryptosleep/biosculpting/deathrest "sicknesses" (game mechanics, not tendable);
* Anomaly creepjoiner organ decay and crumbling mind (supernatural pack).

## 7. Reconstruct

Reconstruct restores **one** missing natural body part through vanilla's own
`Pawn_HealthTracker.RestorePart`, the call vanilla's natural-part installation and healer serum use.

In 1.6, destroying a part adds a `Hediff_MissingPart` to it and to every descendant. Installing a
bionic first restores the part, so no missing markers exist under an added part. The candidates
start from vanilla's `HediffSet.GetMissingPartsCommonAncestors`: the topmost marker of each lost
subtree, skipping subtrees under an added part. A part is then offered only if:

* the marker's class is exactly `Hediff_MissingPart`, it is not the core part, and its def is
  curable by item;
* its parent is present (restore the parent, never a child floating under a gap);
* no directly added part sits on it or on any ancestor. A bionic left arm means there is no
  "reconstruct left arm", and no biological fingers inside a bionic hand;
* every hediff in its subtree is a missing marker, or a def vanilla keeps on restoration (psylink,
  mechlink and the like). `RestorePart` deletes everything in the subtree, so anything else there
  makes the part **not offered**, rather than deleted silently.

Only `BodyPartRecord` structure is used. No left/right/arm/leg names, no human assumptions, so an
animal's leg or a modded race's part qualifies the same way. If the part is no longer a candidate
when the work finishes, the job fails cleanly and changes nothing.

## 8. Resuscitate

**Viability** (`Gm21Resuscitation.Decide`, one ordered, centralised policy):

| Check | Rejection reason |
|---|---|
| a real, spawned, not-discarded corpse of a dead flesh pawn | not a corpse / cannot be reached / only flesh bodies |
| not a mechanoid, Anomaly entity, mutant or unnatural corpse | "This is not a medical death." |
| consciousness-source anatomy not entirely destroyed | **"Brain destroyed."** — the hard boundary |
| any other entirely destroyed vital anatomy can be rebuilt minimally and safely | "Vital anatomy destroyed beyond what a minimal reconstruction can safely rebuild." |
| vanilla rot stage still Fresh | **"The body is rotting. Nothing recoverable is left."** |
| decay no more than the Grandmaster threshold | **"The body has deteriorated beyond recoverable limits."** |

Structural reasons are reported before decay.

### Biological decay, not a clock

The Grandmaster does not beat a clock; they beat death while enough of the body remains. Viability
is the corpse's own **`CompRottable.RotProgress`** — vanilla's decay counter — at most
**`Gm21Medicine.MaxResuscitationRotProgress` = 10,000** (provisional). No GM21 timer exists, and time
since death is not consulted.

What vanilla does with that counter (verified in `GenTemperature.RotRateAtTemperature`): a corpse
gains 1 RotProgress per tick at 10 °C and above — heat never makes it faster — a linear fraction
between 0 and 10 °C, and nothing below 0 °C. Toxic fallout adds rot to unroofed corpses.

| Where the body is | Recoverable for |
|---|---|
| Ordinary temperature (≥ 10 °C) | about **4 in-game hours** |
| Refrigerated at 5 °C | about 8 hours |
| Frozen (below 0 °C) | as long as it stays frozen — a chronologically old corpse can be medically fresh |

Vanilla's own "Fresh" stage lasts to 150,000 (2.5 days); the Grandmaster threshold is far stricter,
and a vanilla Rotting or Dessicated corpse is always refused. A corpse with no rot comp (a modded
race) has decay that cannot be judged and is refused.

**When decay is judged:** when the order is given, and again when the Grandmaster reaches the body
and **begins the timed work** — after fetching medicine and walking there. A body that crosses the
threshold on the way is refused at that moment, and nothing is consumed. Once the work has begun on a
recoverable body the procedure is **committed** (the driver's saved `gm21WorkStartedTick`): decay
during the work never fails it, and a reload mid-work stays committed.

**Where:** the Grandmaster works on the corpse **wherever it lies** — on the battlefield, in the
freezer, in a hospital. No hauling, no bed, no room requirement (asserted: no carry or bed calls in
the Medicine jobs).

### The brain boundary

If the race has consciousness-source anatomy (by **tag**, `ConsciousnessSource`, never a name) and all
of it is destroyed, resuscitation is impossible. The substrate that carried the person is gone, and
it is never rebuilt. Audited: every vanilla BodyDef has consciousness-source anatomy.

### Hostile corpses

Hostility is **not** a refusal. The Grandmaster may save an enemy. The order asks first, with a
vanilla confirmation box: *"{pawn} will remain hostile after resuscitation … Resuscitate anyway?"*.
The revival is vanilla's own `TryResurrect` — the path the resurrector mech serum takes on an enemy
corpse — so the pawn keeps its faction, and on a map vanilla gives a revived hostile pawn a fresh
assault lord. No recruitment, pacification or faction change happens (asserted on the IL: no
`noLord`, no `SetFaction`, nothing recruiting). **Resuscitation Shock** (below) leaves them
unconscious for six hours: capture, rescue or ignore them; when it passes, their faction's behaviour
resumes.

### Minimum viable vital reconstruction

Destroyed non-consciousness vital anatomy no longer makes a corpse invalid. The Grandmaster rebuilds
**just enough of the body to make death stop being true** (`PlanVitalRebuild`):

* The vital tags a body depends on mirror the 1.6 capacity workers of the lethal flesh capacities:
  blood pumping; breathing (source × pathway × cage); blood filtration (kidney × liver when the body
  has kidneys, its own source tag otherwise — never both); metabolism.
* For every one of those tags whose parts are **all** destroyed, ONE part is rebuilt — the part
  covering the most lost tags, then the first in body order. Both kidneys lost → one kidney back.
  Heart, left arm and right leg lost → the heart comes back; the arm and leg stay missing.
* A part qualifies only if rebuilding it touches nothing else: its parent is present, and no
  ancestor carries a bionic or other added part. Otherwise the corpse is refused, and the reason is
  logged.
* The rebuild is vanilla's `Pawn_HealthTracker.RestorePart` on that part, done **before** the
  engine judges the body, so vanilla's "would still be dead" safety net never has cause to fire.
* Audited against vanilla: in all 34 vanilla flesh bodies, every one of the 210 vital parts is
  either a leaf directly under the core part, or holds the brain beneath it (a neck — whose loss is
  "Brain destroyed" anyway). So a planned rebuild restores that organ and nothing else.

### Missing anatomy

Non-vital missing anatomy **stays missing** — `restoreMissingParts = false`, and only planned vital
parts are rebuilt. A missing arm or eye does not use a scar slot; Reconstruct is for that. A limb
lost in the fatal fight keeps its stump, which is closed after revival the way a tend closes it.

### Death trauma: up to three permanent scars

The Grandmaster is not rewinding time. The revived body keeps evidence of what killed it
(`Gm21Trauma`).

**Evidence** is read from the corpse before anything changes it, one entry per body location:

* every fresh injury — its severity, its part, that part's max health, its wound type and source;
* every vital part the revival is about to rebuild (destroyed, with the wound type that destroyed it,
  `Hediff_MissingPart.lastInjury`).

**Score** of a location:

```
score = relative severity            (fresh injury severity at the part / part max health, <= 1;
                                       a destroyed-and-rebuilt part counts as 1)
      + 1.0  if the part was destroyed and rebuilt for life
      + 0.5  if the part carries a lethal-capacity tag (consciousness, blood pumping, breathing, ...)
      + 0.5  if the battle log names it as the target of the death blow
```

A location **qualifies** only if vanilla could scar its wound type (`HediffComp_GetsPermanent`: not a
bruise), it is not under a bionic or other added part, the pawn's genes allow permanent wounds, and
it was destroyed, took the death blow, or has at least **10%** relative severity. The best
qualifying locations are chosen, one per location, at most **three**; ties break by body order.
**Nothing is invented to reach three**: one catastrophic wound gives one scar, a death by disease
gives none.

**The battle log is corroboration only.** When the death entry
(`BattleLogEntry_StateTransition`) still exists, its target part gets the death-blow bonus; if it is
absent, pruned or unreadable, the ranking runs from the body alone. The injury's own combat-log link
and text are carried onto the restored wound and the scar as provenance.

**Scars** use vanilla's permanent-injury machinery (`HediffComp_GetsPermanent`, including vanilla's
own pain-category roll):

* the location's restored wound of that type **becomes** the scar — it is converted, not duplicated;
* its severity is its own, capped at **40%** of the part's max health — never heavier than the wound
  was, never destroying the part again;
* a rebuilt vital part gets a new permanent wound of the type that destroyed it at 40% of its max
  health (a "scarred" heart works at 60%);
* a new scar is added only if it can neither kill nor destroy the part (checked with vanilla's
  `WouldDieAfterAddingHediff` / `WouldLosePartAfterAddingHediff`, halving the severity if needed).

### Revival order

1. Snapshot the fresh injuries, plan the vital rebuild, rank the trauma.
2. Rebuild the planned vital parts.
3. Vanilla `ResurrectionUtility.TryResurrect` with `restoreMissingParts = false` and no scar roll —
   **not** `TryResurrectWithSideEffects`: no resurrection sickness, dementia, blindness or psychosis
   lottery. The engine restores map and world state, destroys the corpse and de-registers the world
   pawn, so no duplicate pawn is created.
4. Restore the snapshotted fresh wounds, smallest first, each only if it cannot kill the pawn again,
   each tended as the Grandmaster's no-medicine care so it stops bleeding. A wound skipped because
   restoring it would be fatal is the one the Grandmaster had to close; it is counted in the dev log.
5. Apply the scars.
6. Close anything still open, fresh stumps included.
7. Apply **Grandmaster Resuscitation Shock**.

If vanilla refuses the revival, the rebuilt parts are put back as destroyed. If vanilla's own
last-resort branch (delete every hediff when the body would still be dead) ever fires anyway, a
warning is logged once rather than hidden.

### Grandmaster Resuscitation Shock

A revived pawn is alive and stabilised, but does not get up and walk away. Every successful
Grandmaster Resuscitation ends with **`GM21_ResuscitationShock`**, a GM21 HediffDef:

* **Mechanism — vanilla psychic coma's.** One stage with a Consciousness `setMax` of **0.1**, and a
  `HediffComp_Disappears` timer. Vanilla's awake line is Consciousness ≥ 0.3, so the pawn is
  **unconscious and downed**: cannot move, manipulate, work or fight; can be carried, rescued or
  captured, and rests in bed. Lethal Consciousness means a level of 0 (vanilla sets no higher death
  line), and vanilla applies capacity modifiers only to a level already above 0, with `setMax` as a
  `min()` ceiling — so the shock lowers a living level to 0.1 and **can never be the cause of death**
  (asserted on vanilla's IL). The code still asks vanilla's `WouldDieAfterAddingHediff` first.
* **Duration: exactly six in-game hours** (`Gm21Medicine.ResuscitationShockTicks` = 15,000 ticks;
  the code sets it exactly). Not scaled by medicine, scars, death, skill, temperature or faction. A
  vanilla message says when the pawn recovers; the health tab shows the time left.
* **Not** vanilla's resurrection sickness and not `TryResurrectWithSideEffects`: no psychosis,
  dementia or blindness, no random range. Not curable by items.
* Applied last: alive → scars → wounds closed → shock → downed → shock expires → normal behaviour.
* Hostile pawns stay hostile throughout; the shock only keeps them down.

### Engine cleanup — accepted limitation

`Pawn_HealthTracker.Notify_Resurrected` removes, by itself, immunizable diseases, curable conditions
that are lethal or life-threatening, and defs flagged `forceRemoveOnResurrection`. **Grandmaster
Resuscitation inherits that cleanup**, so an unrelated malaria can vanish during revival too.
Re-creating arbitrary, possibly modded, conditions afterwards is not safe, and no cause-of-death
analyser is attempted. The permanent death-trauma scars are the intended lasting cost.

### Grandmaster Resuscitation and the resurrector mech serum

They are different tools; the serum is not nerfed or touched.

| | Grandmaster Resuscitation | Resurrector mech serum |
|---|---|---|
| Needs | a practising Medicine 21 pawn, personally working on the body | the item |
| Cost | 3.0 of ordinary medicine potency and ~2–3 hours of the Grandmaster's work | the serum; no one's time |
| Freshness | biological decay under a strict threshold; refrigeration and freezing preserve it | any unrotted corpse, vanilla rules |
| Brain destroyed | impossible | vanilla rules |
| Vital organs | only what life requires is rebuilt | vanilla rules |
| Missing limbs | stay missing | vanilla restores them |
| Afterwards | six hours of deterministic Resuscitation Shock | vanilla's resurrection sickness |
| Lasting cost | up to three permanent scars from the death trauma | vanilla's rot-scaled dementia / blindness / psychosis lottery |

## 9. Save and uninstall safety

| State | Where it lives | Default written? |
|---|---|---|
| Grandmaster Treatment | inside **that Hediff's own node**: `gm21TreatmentQuality`, `gm21TreatmentTick` | never. Only an active regimen is written; every other hediff is byte-for-byte vanilla |
| Medicine mode | inside the pawn's node: `gm21MedicineMode` | never (default Cure) |
| Intervention in progress | the pawn's job: a `GM21_Medicine*` JobDef, `Job.source` → the Hediff; the medicine plan in vanilla's `targetQueueB`/`countQueue`; the driver's `gm21WorkStartedTick`, `gm21HeldPatient`, `pathEndMode` | only while the job runs |
| Collected medicine | ordinary medicine in the Grandmaster's inventory | vanilla items |
| Death-trauma scars | ordinary vanilla permanent injuries | vanilla state; GM21 adds nothing to them |
| Resuscitation Shock | an ordinary hediff of the GM21 def `GM21_ResuscitationShock`, remaining time in vanilla's `ticksToDisappear` | while the pawn is in shock |

Both stores are `ConditionalWeakTable`s keyed on the owning object, the same architecture as
`GrandmasterStore`. State follows the condition through caravans, world pawns, map transitions and
corpses, and there is no global registry to prune or leak.

**Prepare Save for Uninstall** now also, for every collected pawn (animals and corpses' inner pawns
included):

* removes every Grandmaster Treatment;
* removes the Medicine mode;
* removes **Resuscitation Shock** — the pawn simply wakes — so no GM21 HediffDef is left for a
  mod-less load to trip over;
* ends any intervention in progress or queued, so no GM21 JobDef is left in the save. Medicine the
  Grandmaster had already collected stays in their inventory as ordinary items.

Without the mod, RimWorld ignores the unknown elements anyway. The cleanup makes the save contain
none.

## 10. Compatibility

The brief's hard rules, and how they are met:

* No foreign Def is mutated; the tier table is separate runtime state.
* No medicine, body part or disease DefName is hardcoded. A test scans the source for DefName and
  body-part string literals. The one vanilla adapter is a `HediffDefOf` reference.
* `Hediff.TendableNow` is not patched, no WorkGiver is touched, and ordinary doctors never see a
  Grandmaster-only condition as tendable.
* Every patch is narrow and resolved by hand in independent groups. A missing target disables one
  feature and logs once (`Gm21MedicinePatches`).
* Tier discovery runs once. Tending is a dictionary lookup, and the health-tick hooks are a counter
  plus one weak-table lookup for a treated condition only. There is no per-tick allocation and no
  global scan.
* Unknown custom Hediff classes are omitted from Cure. Unknown shapes get no secondary Treatment
  effect. An unreadable modded corpse is reported as not viable. Unknown anatomy without the vital
  tags is not judged by them.
* Medicine is found through `ThingRequestGroup.Medicine` and rated by `MedicalPotency`, so modded
  medicine needs no support code.
* The DoTend transpiler rewrites one call in place (labels and exception blocks preserved) and only
  when the IL has exactly one `Hediff.Tended` call.

**Risks and unsupported cases:**

* A mod that replaces `CompTended`, the health-tick heal or the surgery outcome walk wholesale will
  bypass the corresponding feature.
* A modded failure outcome that is neither flagged `failure` nor derived from the vanilla failure
  classes, listed *before* success, would still be evaluated for a Grandmaster.
* **Exotic custom lethal capacities.** Vanilla's `ShouldBeDeadFromRequiredCapacity` judges every
  loaded `PawnCapacityDef` marked lethal. The vital-rebuild plan mirrors the five vanilla lethal
  capacities only; a race mod that adds its own lethal capacity (or reads its own tags) is not
  understood by it. Vanilla's last-resort revival branch would then restore the body wholesale, and
  that is detected and logged as a warning. Universal modded-race support is **not** claimed; no
  pre-revival guard for unknown capacities was added in this pass, because a dead pawn's capacities
  cannot be evaluated reliably without mutating the corpse.
* A modded flesh corpse without `CompRottable` has decay that cannot be judged and is refused.
* A mod that also transpiles `DoTend`'s `Hediff.Tended` call may leave zero or two call sites; the
  propagation group then disables itself (tending stays exact, the override just sees the ceiling).
* Medical overhauls that replace tending or revival are untested.

## 11. Harmony hooks

| Target | Kind | Why |
|---|---|---|
| `TendUtility.DoTend` | Prefix + void Finalizer (`__state` frame) | Knows doctor and medicine; opens/closes the Grandmaster frame, nesting-safe |
| `TendUtility.DoTend` | Transpiler (one call site) | The single `Hediff.Tended` call → `TendedEffective`: effective quality reaches condition-specific overrides |
| `HediffComp_TendDuration.CompTended` | Prefix (`ref quality`, `ref maxQuality`) + Postfix | Exact tend quality via vanilla's own clamp; starts, refreshes or clears the regimen |
| `HediffComp_TendDuration.CompTipStringExtra` | Postfix (cosmetic) | Treatment line on the condition tooltip |
| `Hediff.ExposeData` | Postfix | Persists an active regimen inside that hediff's node |
| `Pawn_HealthTracker.HealthTickInterval` | Prefix + void Finalizer | Marks natural recovery (a counter) |
| `Hediff_Injury.Heal` | Prefix (`ref amount`) | Treated injury recovers m× inside the health tick |
| `ImmunityRecord.ImmunityChangePerTick` | Postfix | Treated disease instance gains immunity m× |
| `SurgeryOutcomeEffectDef.GetOutcome` | Prefix (replaces for a practising Grandmaster only) | No failure outcome is ever evaluated |
| `Pawn.GetGizmos` / `Pawn.ExposeData` | Postfix (attribute) | Medicine command and mode persistence |

Not a Medicine hook, but what lets a Grandmaster surgeon start a vanilla operation: the core
`Bill.PawnAllowedToStartAnew` transpiler (one comparison operand; README, *Vanilla bills and level 21*).

## 12. Dev-mode tools

Category **"Grandmaster 21 - Medicine"**:

* **Make Medicine Grandmaster:** raises Medicine to 20 and runs the authorised promotion.
* **Report medicine tiers:** the loaded caps, every medicine's target, the curve, the intervention
  budgets and base work, and the feature flags.
* **Report pawn medicine state:** Grandmaster status, mode, tend speed and this pawn's intervention
  work times, self-Reconstruct eligibility, the medicine potency they could gather, every hediff
  with its regimen and its Cure/Reconstruct verdict, plus the candidate counts.
* **Report corpse resuscitation viability:** verdict, reason and every fact behind it: rot stage,
  RotProgress, the GM threshold, *currently viable yes/no*, the local rot rate and how long the body
  stays recoverable at it (time since death is shown but does not decide), the planned vital
  rebuild, the battle log's death blow, and the full trauma ranking with the scars it would produce.
* **Decay corpse past resuscitation threshold:** sets RotProgress just over the threshold, for the
  rejection test.
* **Report surgery outcome defs:** per def, how many failure outcomes a Grandmaster skips.

All reports are event-based; nothing logs per tick.

## 13. Tests

```bash
./build.sh <Managed> <0Harmony.dll>
./tools/verify-medicine.sh <Managed> <0Harmony.dll> <Mono.Cecil.dll> [<RimWorld Data dir>]
./tools/verify-real.sh <Managed> <0Harmony.dll>
```

`verify-medicine.sh` runs `Tests/MedicineChecks.cs` against the **real** assemblies. It installs the
mod's actual patches with `Gm21MedicinePatches.Apply` and then executes the real vanilla methods:
`Hediff.Tended` → `CompTended`, `Hediff_Injury.Heal`, `SurgeryOutcomeEffectDef.GetOutcome`,
`HediffSet.GetMissingPartsCommonAncestors`, and the real Scribe saver and loader. Test-process-only
shims (never shipped) cover what needs a live game: icon loading, live-pawn state re-evaluation, and
Steamworks for `ParseHelper` (`tools/stubs/SteamworksShim.cs`).

**Results in this branch: 369 PASS, 0 FAIL, 0 BLOCKED** with the vanilla `Data/` directory given
(344 without it). Highlights:

* 5,000 real tends per target at 100/130/160/70%: every one exact. Ordinary tends keep vanilla's
  ±25% spread.
* **Propagation:** the live, patched `DoTend` IL calls `TendedEffective` once and `Hediff.Tended`
  never; an unexpected IL shape is left untouched. A condition's own `Tended` receives exactly
  100/130/160%, and the original arguments without a frame. Real `Hediff_HeartAttack` over 6,000
  tends each: ordinary 64%, Grandmaster 100% → 64%, 130% → 85%, 160% → 100%.
* Treatment only on tended conditions; refresh gives ×1.50, not a product of regimens; a non-GM
  re-tend clears it; a lapse deactivates it. Real `Heal` and immunity multipliers as documented.
* Surgery: 1,000 Grandmaster surgeries with failures listed first and quality clamped to zero give
  1,000 successes; Medicine 20 and an incapable Grandmaster take vanilla's failure branch.
* Cure on the brief's mixed patient offers exactly food poisoning; Reconstruct filtering unchanged.
* **Medicine:** budget planning (single kinds, mixtures, float-exact herbal ×5 = 3.0, modded
  potency 0.35, refusal with the available potency); vanilla's preference order; medical care
  respected; the real `MedicalPotency` stat read; real medicine Things in a real inventory
  `ThingOwner` consumed exactly as planned (a stack emptied is removed, nothing else touched); a
  refused plan consumes nothing; `Consume` has one call site, after the apply's result; acquisition
  uses the vanilla queue, `TakeToInventory` and stack-reservation toils.
* **Carried medicine first:** carried industrial alone covers Resuscitate — no glitterworld trip;
  mixed carried medicine covers it — no trip; two carried herbal beat a glitterworld next door for
  Cure; a short inventory is used whole and the map completes it; equal map candidates give the same
  plan in any input order; the order plan searches the map only when the inventory falls short.
* **Work time:** exact scaling for every speed 0.1–200 with no 10× ceiling (10, 25, 50 keep
  dividing), bottoming out at the 60-tick floor (speed 100, 1000, 1e30); zero, negative, tiny and
  non-finite speeds are safe; no cooldown/charge/per-day field on any Medicine type.
* **Decay viability:** 0 / at / over the 10,000 threshold; vanilla-Fresh-but-over rejected;
  Rotting/Dessicated rejected; no rot comp and NaN rejected; committed work is not failed by decay
  or a stage change but still by the brain; the facts carry no time field and viability reads
  `RotProgress`, never time of death; a real `CompRottable` read; vanilla's rot-rate curve (4 h
  unpreserved, 8 h at 5 °C, frozen never); the job judges decay at work start then commits, the order
  judges it now; no hauling or bed logic.
* **Resuscitation Shock:** the shipped def (fixed 15,000-tick timer, one Consciousness `setMax 0.1`,
  nothing else); vanilla's 0.3 awake line and 0 death line; vanilla's capacity rule applies modifiers
  only above 0 with `setMax` as a ceiling (IL); expiry at exactly six hours; `ApplyShock` sets the
  duration, asks vanilla whether it would kill, then adds; revival order ends with the shock; save
  and reload keep the remaining time; Prepare Save for Uninstall removes it and nothing else; the
  vanilla data audit confirms psychic coma's identical mechanism.
* **Self and hostile:** self-Reconstruct at 100% / 50% / 30% manipulation; the self ban and the
  hostile refusal are gone; the hostile confirmation exists; no faction change or recruitment.
* **Vital anatomy:** heart lost + limb lost → heart only; both kidneys → one; one lung → nothing;
  brain never planned; an organ inside a lost casing, or under an artificial part → refused; a part
  covering two lost tags preferred.
* **Trauma:** one catastrophic wound → one scar; many → the three worst locations; scratches → none;
  unscarrable wound types skipped; rebuilt vitals rank first; death-blow bonus; deterministic ties;
  one scar per location; severity caps; evidence from a real body state (the lost limb contributes
  nothing); the restored wound converted in place; no battle log → same selection.
* **Save:** regimens, mode, the intervention driver's work clock and a death scar round-trip through
  the real Scribe; untreated hediffs and scars carry no GM21 element; the uninstall cleaner.
* **Vanilla data audit:** the 28 Cure candidates, 100/130/160% tiers, surgery outcomes, every body's
  consciousness anatomy, every flesh body's vital-organ layout, and the unit cost of each budget.
* **Bill skill ceiling (core, section 21):** the real `Bill.PawnAllowedToStartAnew` on a real
  `Bill_Medical` on a patient's bill stack targeting a prosthetic (`Recipe_RemoveBodyPart`, whose
  added-part label branch runs). Unpatched, the bug reproduces: a Medicine 21 Grandmaster is refused
  with `AboveAllowedSkill`, and so is a Crafting Grandmaster on a `Bill_Production`. Patched: both
  allowed; skill 20 / max 20 allowed; Grandmaster on 0–15 and 5–10 refused; below-minimum refused
  (including a Grandmaster whose aptitude reports 15); another skill's Grandmaster, a non-GM that
  another mod reports as 21, and a Grandmaster reported as 22 all refused as vanilla; pawn and
  slave restrictions intact; 252 ordinary attempts (levels 0–20 × 6 ranges × 2 bill types) identical
  before and after, reasons included; `allowedSkillRange` never written. The rewritten IL is
  vanilla's plus exactly three instructions; the failure message still reads the real max; zero or
  two comparison sites leave the method untouched. After eligibility, `Recipe_RemoveBodyPart` →
  `CheckSurgeryFail` → the patched `GetOutcome` (IL) gives the same Grandmaster 200/200 successes on
  that recipe, patient, part and bill. The data audit confirms vanilla `RemoveBodyPart` is
  `Recipe_RemoveBodyPart` with work skill Medicine. One test-only shim for this section: the method's
  single `ModsConfig.BiotechActive` read answers "off" (ModsConfig cannot initialise headless),
  installed before the unpatched baseline is measured.

`verify-real.sh` checks every Medicine target, Harmony parameter name and vanilla member the
Medicine passes rely on, decay and shock members included, plus the bill-ceiling members (257 PASS, 0 FAIL, 1 SKIP), and binds each Medicine patch live, the DoTend
transpiler included, and the bill-ceiling transpiler (one site rewritten on the real method). The two `CompTended` targets are BLOCKED there because their static constructor
needs the Unity player; `verify-medicine.sh` binds and executes them. The finalizer audit (14 PASS)
and the progression suite (26 PASS) are unchanged.

## 14. Runtime checklist (NOT RUN — needs a real game)

The first real-game session. Use the dev tools above: *Report pawn medicine state* and *Report corpse
resuscitation viability* show every number the code decides by. Check the player log throughout.

| # | Case | Expected |
|---|---|---|
| 1 | GM tends a **heart attack** with herbal, industrial, glitterworld (several times each) | mote exactly 100 / 130 / 160%; treatment succeeds ~65% / ~85% / always |
| 2 | Cure food poisoning with enough medicine **already in the GM's inventory** | no trip; cures in place; exactly one budget consumed from the inventory |
| 3 | Cure when extra medicine must be gathered (inventory short or empty) | GM fetches the missing medicine, then cures; "needs X, Y available" if there is not enough anywhere |
| 4 | GM carries 3 industrial, glitterworld sits elsewhere on the map; order Resuscitate | GM does **not** leave for the glitterworld |
| 5 | Cure / Reconstruct with very high MedicalTendSpeed (drugs, bionics, dev stat; try >10) | duration keeps shrinking past 10×, down to about a second of real time; tooltip cost matches |
| 6 | Kill a pawn; leave the corpse at normal temperature | viable at first |
| 7 | Watch *Report corpse resuscitation viability* over time | RotProgress climbs ~1/tick; "currently viable: no" after ~4 in-game hours |
| 8 | Put a newly dead pawn into a refrigerated room (0–10 °C) | RotProgress climbs slower |
| 9 | …and check how long it stays viable | longer than 4 hours, in proportion to the temperature |
| 10 | Freeze a corpse (below 0 °C) | RotProgress stops |
| 11 | Advance significant time (days) with it frozen | still viable while RotProgress stays under 10,000 |
| 12 | Resuscitate it **inside the freezer** | the GM walks in and works there |
| 13 | Throughout Resuscitation | no corpse hauling, no bed requirement |
| 14 | On success | Resuscitation Shock appears immediately (health tab, ~6 h remaining) |
| 15 | The revived pawn | downed and unconscious; can be carried to bed; does not fight, work or walk off |
| 16 | Wait ~6 in-game hours | "has recovered from resuscitation shock" message; normal behaviour returns |
| 17 | Resuscitate a hostile raider's corpse | confirmation box first; revived still hostile, but down in shock |
| 18 | …capture, rescue or leave them; if left, wait for the shock to end | they stay hostile the whole time; once awake they act with their faction again |
| 19 | Gunshot death, then resuscitate | up to three of the worst wound locations become permanent scars of their own type; the message lists them |
| 20 | Destroy a heart **and** a limb (dev tools), then resuscitate | only the heart returns (with a scar); the limb stays missing |
| 21 | Destroy the brain / head | "Brain destroyed"; cannot be ordered |
| 22 | Interrupt medicine collection (draft the GM mid-fetch) | nothing applied or consumed; carried medicine stays in the inventory; re-order uses it first |
| 23 | Save and reload during collection, and during the work | the job continues or fails cleanly; no medicine lost or duplicated; a committed resuscitation is not failed by decay |
| 24 | Save and reload while Resuscitation Shock is active | shock still there with the same remaining time |
| 25 | Prepare Save for Uninstall, save, remove the mod, reload | loads cleanly; no GM21 elements, JobDefs or shock hediff remain; scars remain as vanilla scars |
| 26 | Throughout | no red errors in the player log |
| 27 | Give a pawn a prosthetic or bionic part (dev tools or an install bill); order vanilla **Remove [part]** on them, with a stored **Medicine 21** Grandmaster as the only capable doctor | the bill shows **no** "Above allowed skill 20"; the Grandmaster takes the operation job |
| 28 | …let the operation finish | it completes successfully: the part is removed as vanilla does; no failure outcome |
| 29 | Set that bill's skill range to 0–15, same Grandmaster | refused with "Above allowed skill 15" — an intentional cap is kept |
| 30 | Same removal with a Medicine 20 doctor instead | unchanged vanilla: allowed at 0–20, vanilla success chance |
| 31 | *If easy:* a Crafting (or Cooking) Grandmaster on an ordinary workbench bill at the default 0–20 | takes the bill — the bridge is generic, not medical-only |

Also worth a look when convenient: an ordinary Medicine ≤ 20 doctor (vanilla, unchanged); modded
medicine and a modded race if available; self-Cure and self-Reconstruct with one arm missing.

## 15. Known limitations

* Nothing has been observed in a running game.
* Resuscitation inherits the engine's cleanup of immunizable and life-threatening conditions
  (see §8).
* Resuscitation ignores the dead pawn's medical-care setting (see §5).
* Exotic race mods with custom lethal capacities are not understood by the vital-rebuild plan (see
  §10); a modded corpse without a rot comp is refused.
* A scarred brain (when the brain itself was badly hurt in the death) costs consciousness, as any
  vanilla brain scar does.
* Custom Hediff classes are never offered for Cure; see §6 for the audited omissions.
* Budgets, work times, the decay threshold, the shock duration, the curve and the trauma scoring are
  first-pass tuning.
