# Medicine 21 — Grandmaster Physician

> **Experimental vertical slice, new in 0.12.0 Beta. Not yet verified in a running game.**
> It builds against the real RimWorld 1.6, Unity and Harmony assemblies. Its logic is checked
> headlessly, including against every vanilla Def in all six content packs. No gameplay session has
> exercised it. Every tuning number below is **provisional**.

*"I do not need miraculous technology to practice miraculous medicine. I am the Grandmaster."*

The extraordinary capability belongs to the **pawn**. Medicine 21 adds no research, items,
buildings, implants, techprints or consumables. The only Defs it ships are three JobDefs for the
Grandmaster's personal interventions. A Medicine Grandmaster is a pawn with **stored** Medicine
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
doctor and the medicine. While a Grandmaster frame is open, a prefix on `CompTended` rewrites only
that comp's two arguments: `maxQuality = target`, `quality = target + 0.25 + 0.05`. Vanilla's own
clamp then produces **exactly** the target for every possible roll. Vanilla still writes
`tendQuality`, accumulates `totalTendQuality`, sets `tendTicksLeft` and throws its
"Tended … Quality 100%" mote, all with the exact value.

* Herbal → exactly 100%, industrial → exactly 130%, glitterworld → exactly 160%.
  *The Grandmaster is not lucky. The Grandmaster is correct.*
* **Ordinary doctors are untouched.** No frame opens and the prefix is a single bool test.
* **Self-tend** keeps vanilla's ×0.7 self-tend factor, applied to the target and still exact
  (herbal 100% → 70%). Treating yourself is a physical limitation.
* The bed's `MedicalTendQualityOffset` is not added. Vanilla adds it before clamping to the ceiling,
  and the Grandmaster result *is* the ceiling.
* Other comps and hediff-specific overrides receive vanilla's arguments. That includes the
  heart-attack treatment roll in `Hediff_HeartAttack.Tended` and other mods' comps. Only the tend
  quality itself is deterministic.
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
`GM21_MedicineResuscitate`), modelled on `JobDriver_TendPatient`. The Grandmaster reserves the
target, walks to it, and performs timed work with the vanilla tend sound, a progress bar and
Medicine as the active skill. The result is applied in one final instant step, after re-running the
full validation. A standing patient is held with vanilla `PawnUtility.ForceWait`, as a drafted tend
does. A downed or bedded patient is treated where they lie.

* **Interruption is safe.** Nothing is applied until the final step, and an interrupted
  intervention can simply be ordered again.
* The selected condition travels in `Job.source`, vanilla's `ILoadReferenceable` slot. Hediffs are
  cross-referenceable save objects, so a job saved mid-intervention reloads pointing at the same
  condition.
* There is **no WorkGiver**. Ordinary doctors can never receive these jobs, and the doctor must be
  a practising Medicine Grandmaster on every tick of the work.
* A Grandmaster cannot perform an intervention on themselves (conservative first pass).
* **Patients:** living flesh pawns on the same map that belong to the colony, prisoners of the
  colony, or anyone downed. Never mechanoids, mutants or Anomaly entities.
* **Durations** at MedicalTendSpeed 1: Cure 1,200 ticks, Reconstruct 4,000, Resuscitate 1,250.
  They are scaled by the doctor's MedicalTendSpeed, clamped to 0.25–4 and floored at 60 ticks.
* **No medicine is consumed** by interventions in this pass. The brief makes consumption optional
  ("if medicine is consumed…"). Collecting medicine means vanilla's hauling and reservation toils,
  which would add failure surfaces without adding mastery. The tier resolver is public, so a later
  pass can add consumption without redesign.

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
| not currently hostile to the colony (prisoners of the colony are fine) | hostile |
| consciousness-source anatomy not entirely destroyed | **"Brain destroyed."** |
| no other lethal capacity's anatomy entirely destroyed | "Vital anatomy destroyed." |
| body still Fresh (CompRottable) | **"Body has deteriorated beyond recovery."** |
| death no more than the window before the work began | **"Too much time has passed."** |

"Vital anatomy" is checked by **body-part tag**, exactly the tags the 1.6 capacity workers of the
lethal flesh capacities read:

* blood pumping;
* breathing: source × pathway × cage;
* blood filtration: source, or kidney × liver;
* metabolism.

A capacity reaches zero, and vanilla's `ShouldBeDead` fires, only when every part carrying one of
its tags is gone. A race without a tag is never judged by it. Audited: every vanilla BodyDef has
consciousness-source anatomy. Structural reasons are reported before rot and time.

**Window: four in-game hours** (`Gm21Medicine.ResuscitationWindowTicks` = 10,000 ticks, about 2m47s
of real time at speed 1). It is provisional and deliberately conservative. It is long enough to
finish a fight and cross a map, and short enough that yesterday's corpse, or one kept fresh in a
freezer, stays dead. The window is measured to the moment the Grandmaster **begins** the work, so a
resuscitation started in time is not failed by the clock while it is being performed. Vanilla
corpses start rotting after 2.5 days, so time, not rot, is the normal limit.

Hostile corpses are refused because vanilla's revival path hands a revived hostile pawn a raid lord,
and that is not a medical outcome.

**Revival** goes through vanilla `ResurrectionUtility.TryResurrect` with
`restoreMissingParts = false` and no scar roll. That engine path restores map and world state,
destroys the corpse and de-registers the world pawn, so no duplicate pawn is created. It is **not**
`TryResurrectWithSideEffects`: there is no resurrection sickness, dementia, blindness or psychosis
roll. This is medicine, not the serum.

* **Cleared by the engine's revival**, accepted as the Grandmaster addressing the cause of death:
  immunizable diseases, conditions that are lethal or life-threatening (blood loss included), and
  defs flagged `forceRemoveOnResurrection`. Re-creating arbitrary modded conditions afterwards is
  not safe.
* **Preserved on purpose:** the engine would also erase every fresh injury, which would make
  resuscitation a full heal. Fresh vanilla injuries are snapshotted (audited: all 26 vanilla injury
  defs are exactly `Hediff_Injury`) and restored afterwards, smallest first, each only if it cannot
  kill the pawn again. Each is immediately tended as the Grandmaster's no-medicine care under
  vanilla rules, so it stops bleeding.
* A wound skipped because restoring it would be fatal is the one the Grandmaster had to close to
  make revival possible. It is reported in the dev log.
* Scars, missing parts, implants and chronic conditions are never removed by this path. Reconstruct
  remains the structural ability.
* No coma or trauma debuff is added.
* Vanilla's revival has a last-resort branch that deletes every hediff if the body would still be
  dead. The viability policy exists to keep it from firing. If it ever does fire, a warning is
  logged once rather than hidden.

## 9. Save and uninstall safety

| State | Where it lives | Default written? |
|---|---|---|
| Grandmaster Treatment | inside **that Hediff's own node**: `gm21TreatmentQuality`, `gm21TreatmentTick` | never. Only an active regimen is written; every other hediff is byte-for-byte vanilla |
| Medicine mode | inside the pawn's node: `gm21MedicineMode` | never (default Cure) |
| Intervention in progress | the pawn's job: a `GM21_Medicine*` JobDef, `Job.source` → the Hediff | only while the job runs |

Both stores are `ConditionalWeakTable`s keyed on the owning object, the same architecture as
`GrandmasterStore`. State follows the condition through caravans, world pawns, map transitions and
corpses, and there is no global registry to prune or leak.

**Prepare Save for Uninstall** now also, for every collected pawn (animals and corpses' inner pawns
included):

* removes every Grandmaster Treatment;
* removes the Medicine mode;
* ends any intervention in progress or queued, so no GM21 JobDef is left in the save.

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

**Risks and unsupported cases:**

* A mod that replaces `CompTended`, the health-tick heal or the surgery outcome walk wholesale will
  bypass the corresponding feature.
* A modded failure outcome that is neither flagged `failure` nor derived from the vanilla failure
  classes, listed *before* success, would still be evaluated for a Grandmaster.
* A mod whose lethal capacity uses its own tags is not covered by the vital-anatomy pre-check.
  Vanilla's revival safety net would then fire, and it is logged.
* Medical overhauls that replace tending or revival are untested.

## 11. Harmony hooks

| Target | Kind | Why |
|---|---|---|
| `TendUtility.DoTend` | Prefix + void Finalizer (`__state` frame) | Knows doctor and medicine; opens/closes the Grandmaster frame, nesting-safe |
| `HediffComp_TendDuration.CompTended` | Prefix (`ref quality`, `ref maxQuality`) + Postfix | Exact tend quality via vanilla's own clamp; starts, refreshes or clears the regimen |
| `HediffComp_TendDuration.CompTipStringExtra` | Postfix (cosmetic) | Treatment line on the condition tooltip |
| `Hediff.ExposeData` | Postfix | Persists an active regimen inside that hediff's node |
| `Pawn_HealthTracker.HealthTickInterval` | Prefix + void Finalizer | Marks natural recovery (a counter) |
| `Hediff_Injury.Heal` | Prefix (`ref amount`) | Treated injury recovers m× inside the health tick |
| `ImmunityRecord.ImmunityChangePerTick` | Postfix | Treated disease instance gains immunity m× |
| `SurgeryOutcomeEffectDef.GetOutcome` | Prefix (replaces for a practising Grandmaster only) | No failure outcome is ever evaluated |
| `Pawn.GetGizmos` / `Pawn.ExposeData` | Postfix (attribute) | Medicine command and mode persistence |

## 12. Dev-mode tools

Category **"Grandmaster 21 - Medicine"**:

* **Make Medicine Grandmaster:** raises Medicine to 20 and runs the authorised promotion.
* **Report medicine tiers:** the loaded caps, every medicine's target, the curve and the feature flags.
* **Report pawn medicine state:** Grandmaster status, mode, every hediff with its regimen (quality,
  multiplier, effect, active, tick) and its Cure/Reconstruct verdict, plus the candidate counts.
* **Report corpse resuscitation viability:** verdict, reason and every fact behind it, including
  time since death against the window.
* **Age corpse past resuscitation window:** for the rejection test.
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

**Results in this branch: 177 PASS, 0 FAIL, 0 BLOCKED** (with the vanilla `Data/` directory given).
Highlights:

* 5,000 real tends per target at 100/130/160/70%: every one exact. Ordinary tends keep vanilla's
  ±25% spread.
* Treatment only on tended conditions; refresh gives ×1.50, not a product of regimens; a non-GM
  re-tend clears it; a lapse deactivates it.
* Real `Heal`: a treated injury heals ×1.25 in the health tick, an untreated one ×1.00, and a heal
  from outside the tick is not scaled. Over 20 heals at 160%, treated recovery is exactly ×1.75
  vanilla.
* Immunity ×1.50 at 130%; unrelated disease unchanged.
* Surgery: 1,000 Grandmaster surgeries with failures listed first and quality clamped to zero give
  1,000 successes. The same surgery by Medicine 20 takes vanilla's failure branch, and so does an
  incapable Grandmaster.
* Cure on the brief's mixed patient (food poisoning, wound, bionic, missing part, others) offers
  exactly food poisoning, and completing it removes only that.
* Reconstruct: a lost limb is offered once, at its root. A bionic location offers nothing. No
  fingers under a bionic parent. A subtree with other state is refused.
* Resuscitation policy: every rejection, both window edges, and brain and vital-tag detection.
* Real Scribe: a regimen and a mode round-trip through save **and load**. A whole hediff through
  `Scribe_Deep` comes back as a new object carrying its regimen and vanilla tend. Lapsed and
  untreated hediffs write nothing, and the uninstall cleaner leaves no GM21 element.
* Vanilla data audit: the real C# rules over every vanilla Def give the 28 Cure candidates above,
  100/130/160% from the real medicine defs, the audited surgery outcomes and every body's
  consciousness anatomy.

`verify-real.sh` gains Medicine targets and Harmony parameter names (212 PASS, 0 FAIL, 1 SKIP) and
live binding of each Medicine patch. The two `CompTended` targets are BLOCKED there, because their
static constructor needs the Unity player; `verify-medicine.sh` binds and executes them. The
finalizer audit discovers both new finalizers and proves they cannot swallow exceptions.

## 14. Runtime checklist (NOT RUN — needs a real game)

| Case | Expected |
|---|---|
| Promote a colonist with *Make Medicine Grandmaster*; log shows the Medicine startup line | tiers `70%->100%, 100%->130%, 130%->160%`, all flags true |
| GM tends a gunshot with herbal, repeatedly on fresh wounds | mote and tooltip read exactly 100% every time |
| Medicine 20 doctor tends the same | vanilla random qualities |
| Treated vs untreated gunshot on one pawn | treated heals visibly faster; tooltip line only on the treated one |
| GM treats malaria; untreated plague on the same pawn | malaria immunity rises faster; plague unchanged |
| Ordinary doctor re-tends a GM-treated disease after the tend lapses | treatment line disappears |
| Repeated valid surgeries by the GM (with and without an Inspired Surgery) | no failure letter ever; ingredients, work and results normal |
| Same surgeries by Medicine 20 | vanilla failure odds |
| Cure on food poisoning + wound + bionic + missing leg | menu shows food poisoning only; job runs; only that is removed |
| Reconstruct on a missing natural leg; then on a location with a bionic | leg offered and restored; bionic location never offered |
| Interrupt each intervention mid-work (draft, damage, move the patient) | nothing applied; can be re-ordered |
| Resuscitate a fresh colonist corpse | pawn returns with wounds bandaged; no sickness roll; no duplicate world pawn |
| Corpse with destroyed head/brain; corpse aged past the window (dev tool) | "Brain destroyed" / "Too much time has passed" |
| Save and reload with an active treatment, a non-default mode and an intervention in progress | all restored; the job still points at the same condition |
| Prepare Save for Uninstall, save, remove the mod, reload | loads cleanly; no GM21 elements or JobDefs remain |

## 15. Known limitations

* Nothing has been observed in a running game.
* Interventions consume no medicine (see §5).
* A Grandmaster cannot perform interventions on themselves.
* Hostile corpses cannot be resuscitated.
* Custom Hediff classes are never offered for Cure; see §6 for the audited omissions.
* Resuscitation clears the non-lethal immunizable diseases the engine's revival clears.
* The window, durations and curve are first-pass tuning.
