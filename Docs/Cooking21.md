# Cooking 21: Grandmaster Cook — 0.15.0 Beta (first pass)

*"A Grandmaster does not cook around the danger. There is none to cook around."*

The first Grandmaster Cooking mechanics. Five things, and only these:

| # | Feature | Where |
|---|---|---|
| 1 | **Perfect Hygiene** — a Grandmaster Cook's meals are never cooking-poisoned | `Gm21CookingPatches.Prefix_NotifyRecipeProduced` |
| 2 | **Masterful Meals** — persistent per-serving provenance | `CompGrandmasterMeal` |
| 3 | **Longer freshness** — Masterful servings slow their stack's rot | `Gm21MasterfulRot` + one transpiler |
| 4 | **Purify Food** — an active command that removes contamination from prepared food | `Command_Gm21PurifyFood`, `JobDriver_Gm21PurifyFood` |
| 5 | **Auto Purify** — a finite cleanup of every reachable contaminated stack | `JobDriver_Gm21AutoPurifyFood` |

Status: builds against the real RimWorld 1.6 assemblies, and 169 headless checks execute the **real
vanilla methods** these features attach to. **Not verified in a running game.** Tuning numbers are
provisional. See [the owner runtime checklist](#owner-runtime-checklist-not-run).

## Boundaries: what this deliberately does not do

* **No global food-poisoning or stack fix.** Vanilla `CompFoodPoisonable` keeps its proportional poison
  percentage per stack; normal cooks, rotten food, dangerous food types and other mods' poison are
  untouched. A future Parametric feature may change poisoned-stack semantics; Grandmaster 21 neither
  depends on it nor anticipates it.
* **No new rot system.** `CompRottable` is not replaced, not re-timed and not patched wholesale. There is
  one rot timer per stack, exactly as in vanilla.
* **Out of scope** (may come later): Grandmaster-exclusive recipes, Perfect Yield, ingredient
  substitution, cooking-speed or work-speed buffs, a perfect-meal proc, banquets, quality/nutrition changes.
* **No new hard dependency, no settings, no save-wide state, no polling.**

## Audit of the real 1.6 assemblies

Performed before any code was written, with the ILSpy decompiler engine on the supplied assemblies, then
re-derived mechanically by `Tests/CookingChecks.cs` (section "audit", Mono.Cecil), so a game update that
moves any of it fails a check rather than silently changing behaviour.

| Input | SHA-256 |
|---|---|
| Assembly-CSharp.dll | `5cf1b5be399d5b1c9c56ca72c9d35b4ecf307feacf5859d04ac5a1aa5926356a` |
| 0Harmony.dll (2.4.1) | `353daafec180bb8e7bbe4da78f2a7cdc78067392e3a4e79dc8e7af295f2371e6` |

| Vanilla member | Observed behaviour | Consequence for the design |
|---|---|---|
| `CompFoodPoisonable.Notify_RecipeProduced(Pawn)` | Rolls `Rand.Chance` twice: the room's `FoodPoisonChance` (→ `FilthyKitchen`), else the worker's `FoodPoisonChance` stat (→ `IncompetentCook`); each hit calls `SetPoisoned`. | Perfect Hygiene is a prefix that skips exactly this method for a Grandmaster worker, and nothing else. |
| `CompFoodPoisonable.SetPoisoned` / fields | `SetPoisoned` sets `poisonPct = 1` and the cause. **No method clears it.** Private fields `float poisonPct`, `FoodPoisonCause cause`. | Purify writes those two fields through cached `AccessTools.FieldRefAccess` delegates, resolved once at startup. |
| `CompFoodPoisonable.PreAbsorbStack` / `PostSplitOff` | Poison is a **weighted average** across merges and copied to split pieces. | Left alone. A stack can carry a fractional poison percentage; Purify treats any value above 0 as contaminated. |
| `GenRecipe.MakeRecipeProducts` | For each entry of `recipeDef.products` it makes the Thing, sets `stackCount`, **then** calls `Notify_RecipeProduced(worker)`. Butchery/smelting products do not go through it. | A comp can read the final stack size in its own `Notify_RecipeProduced`, so Masterful creation needs no Harmony patch, and only real recipe products are "cooked". |
| `ThingWithComps.TryAbsorbStack` | Calls every comp's `PreAbsorbStack(other, count)` **before** `base.TryAbsorbStack` changes any count. | On merge, both stacks still hold their pre-merge sizes: the receiver can take its share from the donor and the donor can give it up in one place. |
| `ThingWithComps.SplitOff` | Calls `base.SplitOff` first, then every comp's `PostSplitOff(piece)`. `base.SplitOff` has **already reduced** `stackCount`, and for a whole-stack split returns `this`, in which case `PostSplitOff(this)` still runs. | The parent holds the remainder when the callback fires; a whole-stack split (`piece == parent`) must be a no-op. |
| `Thing.Destroy` | Zeroes `stackCount` of a non-pawn. | A destroyed stack reports zero Masterful servings. |
| `Thing.Ingested(Pawn, float)` | Splits the eaten part off and **discards** the piece, then calls `PostIngested` on the *remainder* (or on the destroyed original for a whole meal). | No comp callback can see how many servings were eaten. The count before and after `Ingested` can: the difference is exactly what was eaten. |
| `CompRottable.TickInterval(int)` | The **only** place a rate advances `RotProgress`: `RotProgress += GenTemperature.RotRateAtTemperature(temp) * delta`. Exactly one call to `RotRateAtTemperature`. `CompTickInterval` and `CompTickRare` both go through it. | One transpiler site scales the rate. Temperature, frozen/refrigerated behaviour, destruction and daily rot damage are untouched. |
| `CompRottable.TicksUntilRotAtTemp(float)` | The "days until rot" figure; exactly one `RotRateAtTemperature` call. `CompInspectStringExtra` calls `RotRateAtTemperature` a third time only to label frozen/refrigerated. | A second, independent transpiler scales the estimate so the readout is honest. The inspect-string call is deliberately not touched, so "frozen" still means the temperature is frozen. |
| `CompRottable.PreAbsorbStack` / `PostSplitOff` | `RotProgress` is a weighted average on merge and copied to a split piece. | Accepted as-is. Only *future* rot follows the current Masterful ratio; no per-serving age is simulated. |
| `Gizmo` / `GizmoGridDrawer` | A right-click opens a float menu whenever `Gizmo.RightClickFloatMenuOptions` is non-empty, otherwise calls `ProcessInput`. | "Auto Purify" is a normal `RightClickFloatMenuOptions` entry. No custom window. |
| `JobDriver.DriverTick` | Runs `preTickActions`, and after each one returns if the toil changed or `wantBeginNextToil` is set. `Toils_Goto.GotoThing` bakes in `FailOnDespawnedOrNull`, which would end the whole job. | Auto skips a stack by `JumpToToil` from a pre-tick action (vanilla-supported) and uses its own goto toil. |
| `Pawn_PathFollower.PatherFailed` | `StopDead()` then `JobDriver.Notify_PatherFailed()` (virtual; base ends the job with `ErroredPather`). | Auto overrides `Notify_PatherFailed` to drop just that stack. |

## 1. Perfect Hygiene

A meal whose recipe is completed by a **stored** Cooking 21 pawn never receives ordinary cooking
poisoning. The check is `Gm21Cooking.IsCookingGrandmaster`, a one-line wrapper over the existing central
`Gm21.IsGrandmaster(Pawn, SkillDef)`; nothing in the Cooking package compares a skill level itself. The
stored level is used, not the aptitude-adjusted one, so a gene can neither create nor remove it.

The patch is a prefix on `CompFoodPoisonable.Notify_RecipeProduced` that returns `false` only for a
Grandmaster worker. Everyone else falls straight through to vanilla. As a consequence a Grandmaster cook
in a filthy kitchen consumes no RNG and produces clean food; a level-20 cook in the same kitchen is poisoned
exactly as in vanilla. Rotten-food poisoning, dangerous-food-type poisoning and any poison applied by
other code are separate paths and are not affected (a Grandmaster's meal can still be poisoned by other
means, and Purify Food can clear it).

## 2. Masterful Meals

### The component

`CompGrandmasterMeal` stores one integer, `masterfulCount`, with the invariant
`0 <= masterfulCount <= stackCount`. It never ticks and holds no reference to a pawn (the cook's identity
is not needed). It is written into the Thing's own save node as `gm21MasterfulCount`, only when non-zero.

**Attachment is runtime and rule-based.** At startup, after every Def is resolved,
`Gm21CookingStartup.AttachMasterfulComp` adds `CompProperties_GrandmasterMeal` to each ThingDef that is
an ingestible item, whose `thingClass` is a `ThingWithComps`, and that carries `CompFoodPoisonable` (or a
subclass). `CompFoodPoisonable` is the marker vanilla itself uses for a cooked product, so the rule is found
in the def's comp list, never a DefName: a modded meal built on the vanilla comp is covered by the same rule
with no compatibility patch, and raw ingredients, drugs, corpses and non-food are left alone. It is idempotent,
edits no existing comp, and edits no XML.

### Creation

`CompGrandmasterMeal.Notify_RecipeProduced` runs after vanilla has set the stack size. If the worker is a
Cooking Grandmaster the whole product stack is Masterful (`masterfulCount = stackCount`); otherwise it stays
0. Food that never came from a recipe (traders, quests, dev spawns, nutrient paste) starts at 0. **The
recipe's work skill is not inspected**, because vanilla does not pass the recipe to `Notify_RecipeProduced`;
a Cooking Grandmaster working any recipe whose product is eligible food therefore counts.

### Merging and splitting (conservation)

All movement of Masterful servings is one pure integer function, `Gm21MasterfulMath.Share(total, marked, take)`:
of the `take` servings leaving a stack of `total` containing `marked` Masterful ones, the number that are
Masterful is `round-half-up(take * marked / total)`, then clamped to the only feasible range
`[max(0, take - (total - marked)), min(take, marked)]`. The clamp is exactly the condition that leaves the
source with `0 <= marked' <= total'`. There is no float and no drift.

* **Merge** (`PreAbsorbStack`, before any count changes): the receiver adds `Share(donor.stack, donor.marked, count)`
  and the donor loses exactly the same number.
* **Split** (`PostSplitOff`, after the counts changed): with `before = parent.stack + piece.stack`, the piece
  receives `Share(before, marked, piece.stack)` and the parent keeps the rest. `piece == parent` (whole-stack
  split) does nothing.

Both directions move the *same integer* from one stack to the other, so the Masterful total across any
sequence of splits and merges is conserved exactly.

| Case | Result |
|---|---|
| 10 Masterful + 5 ordinary taken from a stack of 10 | 15 servings, **10** Masterful (not 15) |
| 10 Masterful + 10 ordinary | 20 servings, 10 Masterful |
| stack of 10 with 4 Masterful, split 5 off | piece 5 with 2, remainder 5 with 2 |
| 1 Masterful in 10, split 1 off | rounds to the ordinary side (piece 0, remainder still 1); the last serving carries it |
| whole-stack split | unchanged |

`Tests/CookingChecks.cs` runs 4000 random splits and merges through the **real** vanilla `SplitOff` and
`TryAbsorbStack` and checks conservation and the bound `0..stackCount` after every operation.

A mod that lowers `stackCount` directly (bypassing `SplitOff`) cannot be observed. The count is then
clamped to the new stack size on read, which can only skew a mixed stack toward Masterful. This is a known
limitation, not a corruption: the count never exceeds the stack.

### Save and load

Normal Verse serialization: `Scribe_Values.Look(ref masterfulCount, "gm21MasterfulCount", 0, false)`. An
old save with no element loads as 0; a stored value above the stack size or below 0 is clamped on load. Food
that is not Masterful writes nothing. **Uninstall:** a save with the element loaded without the mod ignores
it (vanilla ignores unknown elements). The only Cooking state that names a mod Def is a pawn's Masterful Meal
memory and a Purify job, which Prepare Save for Uninstall removes.

## 3. Ingestion

`Thing.Ingested` is wrapped by a prefix (records the Masterful count) and a postfix (reads it again). The
difference is exactly the number of Masterful servings that were eaten. If it is above zero the eater gains
the **Masterful Meal** memory once for that meal.

* Eaten servings leave the stack, and their Masterful share leaves with them (a whole meal: the count goes
  with the destroyed Thing; a partial meal: the split already moved the share to the discarded piece).
* One Masterful serving among nine ordinary ones yields exactly one reward over the stack's lifetime, never ten:
  the reward needs a Masterful serving to have actually been eaten.
* A Masterful serving that merely stays in the remainder earns nothing yet.
* Vanilla ingestion is otherwise untouched (poisoning rolls, thoughts, nutrition all run as before).

The reward is the vanilla `ThoughtDef` `GM21_MasterfulMeal` in `Defs/Cooking/Cooking21.xml`: **+4 mood for
one day, stack limit 1** (vanilla's fine meal is +5, lavish +12). Everything about it is tuned in that Def.

## 4. Longer freshness

```
masterfulRatio = masterfulCount / stackCount
rotMultiplier  = 1 - masterfulRatio * PreservationStrength      (PreservationStrength = 0.80)
```

| Masterful | 0% | 25% | 50% | 75% | 100% |
|---|---|---|---|---|---|
| rot speed | 1.00x | 0.80x | 0.60x | 0.40x | 0.20x |

The multiplier is applied to the vanilla rate for the current temperature, in the one place vanilla advances
`RotProgress`. Consequently:

* frozen food (rate 0) stays frozen; refrigeration, heat and any modded temperature environment behave as
  vanilla, because only a multiplication is new;
* a stack with **no** Masterful servings takes an early exit and returns the vanilla rate unchanged: its rot is
  bit-for-bit vanilla (verified by exact float comparison);
* `RotProgress` is still averaged on merge and copied on split by vanilla; only future rot follows the ratio;
* the "days until rot" estimate uses the same multiplier (a fully Masterful stack reads five times longer), while the
  frozen/refrigerated wording still reflects the actual temperature.

`Gm21MasterfulRot.ScaleRate` runs for every rotting thing on every map, so it cannot throw: any exception is
contained, logged once, and the vanilla rate is returned.

## 5. Purify Food

A Grandmaster Cook gets a **Purify Food** command (player colonists with a stored Cooking 21 only).

* **Left-click** starts vanilla targeting; only contaminated stacks are valid targets.
* **Right-click** opens the vanilla float menu with one entry, **Auto Purify**.
* Disabled with the reason if the Grandmaster is unconscious or has no working hands. Never shown for enemies.

**A valid target** is a Thing that exists, is spawned, is on the pawn's map, is reachable, can be reserved, carries
`CompFoodPoisonable`, and has `PoisonPercent > 0`. No DefName is consulted. Forbidden status is not part of the
rule, so a stack the player forbade to quarantine it can still be purified.

**Result:** poison percentage → 0, cause → `Unknown`. Stack count, rot progress, Masterful count and every other
comp are unchanged; the food is never destroyed.

**Job flow** (one `JobDriver`, four toils):

```
acquire  -> go  -> work (180 ticks, progress bar, Cooking is the active skill) -> finish
   ^                                                                              |
   +---------------------- Auto only: jump back --------------------------------+
```

| Step | Behaviour |
|---|---|
| order | Full validation with a player-facing reason; a Job is only created if the target is valid now. |
| start | `TryMakePreToilReservations` reserves the first stack. |
| acquire | Keeps the current target if still valid; otherwise (Auto) searches once for the nearest reachable, unreserved contaminated stack and reserves it. |
| go / work | A per-tick check runs **first**; if the stack has vanished or been cleaned it is dropped. |
| finish | Purifies, mote text, releases the reservation. Single: message, job ends. Auto: jump back to acquire. |

## 6. Auto Purify: a finite cleanup

Auto Purify is **not** a toggle and not a background scan. It is one Job that ends by itself:

1. find the nearest valid contaminated stack, reserve it, walk, purify;
2. search again; repeat;
3. when the search finds nothing reachable and unreserved, the job ends with success and the pawn returns to
   normal behaviour, with one summary message if anything was cleaned.

The search is one region-based `GenClosest` query, run only to acquire the next target. Nothing runs while the
pawn walks or works, nothing runs after the job ends, and the driver holds no static state.

| Event | Result |
|---|---|
| target destroyed, eaten, hauled away, or cleaned by someone else | that stack is dropped; Auto continues, Single ends |
| pather gives up (unreachable) | Auto: stack skipped for the rest of the run and never retried; Single: vanilla `ErroredPather` |
| another pawn claims a stack between search and claim | that stack is skipped; the next candidate is tried |
| a claim keeps failing | bounded at 32 attempts per acquisition, then the run ends (no infinite loop) |
| Grandmaster drafted, downed, unconscious, handless, dead, ordered elsewhere | the job ends the vanilla way; a per-tick "still a practising Cooking Grandmaster" check backs it up |
| map change | vanilla ends the job |
| a cosmetic failure (mote, message) | contained and logged once; the purification already happened |

Only the stack being worked is reserved, and it is released the moment it is done, so a long run never keeps
meals reserved that colonists want to eat. No global fail condition, no Reset patch and no job-cancellation
patch is involved.

## Hooks

Applied by hand, one **independent** Harmony owner per feature. If a target cannot be resolved or an IL shape
has changed, that group unpatches only itself, logs once, and its flag stays down; vanilla behaviour is left
in place and the other features keep working.

| Owner | Target | Kind | Purpose |
|---|---|---|---|
| `Grandmaster21.Cooking.Hygiene` | `CompFoodPoisonable.Notify_RecipeProduced(Pawn)` | Prefix | Perfect Hygiene |
| `Grandmaster21.Cooking.Ingest` | `Thing.Ingested(Pawn, float)` | Prefix + Postfix | Count Masterful servings eaten |
| `Grandmaster21.Cooking.Freshness` | `CompRottable.TickInterval(int)` | Transpiler (one call site) | Scale the rot rate |
| `Grandmaster21.Cooking.Estimate` | `CompRottable.TicksUntilRotAtTemp(float)` | Transpiler (one call site) | Scale the estimate |
| `ared.grandmaster21` (attribute) | `Pawn.GetGizmos` | Postfix | Purify Food command |

`Tests/CookingChecks.cs` asserts that Cooking patches **exactly these four vanilla methods and no others**, and
that `CompRottable` has no Cooking prefix or finalizer.

## Compatibility and the fail-safe rule

* Found through comps, never DefNames: modded prepared food on `CompFoodPoisonable` gets Masterful state, is a
  Purify target and follows the freshness rule with no patch.
* Food whose def lacks `CompFoodPoisonable` (or is not ingestible / not a `ThingWithComps`) is never touched.
* A missing comp is never an error: every lookup is a null-checked `TryGetComp`/`GetComp`.
* Another mod's patches on the same vanilla methods run alongside; the transpilers require exactly one audited
  call site and otherwise disable their group instead of guessing.
* Anything the package cannot bind or cannot resolve leaves vanilla behaviour in place.

## Performance

No per-tick scan. The comp does not tick. Freshness adds one `GetComp` and a couple of float operations per rotting
Thing per rot tick (an early exit for non-Masterful food). Ingestion adds two comp lookups per meal eaten. Auto Purify
performs one region search per stack cleaned. Field accessors are cached delegates; no reflection runs on a hot path
(the only reflection is a one-time resolution at startup).

## Dev tools

Under **Dev mode → Debug actions → Grandmaster 21** (act on the food under the cursor): *mark whole stack Masterful*,
*mark ONE more serving Masterful*, *split one serving off the stack*, *poison food stack (100%)*, *purify food stack
(instant)*, *report food stack* (stack, Masterful count and ratio, rot multiplier, rot progress and stage, poison and cause).

## Tuning

| Knob | Where | Default |
|---|---|---|
| Rot reduction at 100% Masterful | `Gm21Cooking.PreservationStrength` | 0.80 (0.20x rot speed) |
| Purify work per stack | `Gm21Cooking.PurifyWorkTicks` | 180 ticks |
| Masterful Meal mood | `Defs/Cooking/Cooking21.xml` `GM21_MasterfulMeal` | +4, 1 day |
| Auto claim retries per acquisition | `JobDriver_Gm21PurifyFoodBase.MaxAcquireAttempts` | 32 |

## Verification (2026-09-30)

`./tools/verify-cooking.sh <Managed> <0Harmony.dll> <Mono.Cecil.dll>` — **169 PASS, 0 FAIL**. It runs the mod's real
patches on the real vanilla methods and executes them: `Notify_RecipeProduced`, `GenRecipe.MakeRecipeProducts`,
`ThingWithComps.TryAbsorbStack`/`SplitOff`, `Thing.Ingested`, `CompRottable`, the real Scribe saver and loader,
and the real `JobDriver` (`DriverTick`, toils, `JumpToToil`, `Notify_PatherFailed`).

Headless shims stand in for game-world services only (id generation, RNG, room/stat lookups, ambient temperature,
spawn/map, pathing, the reservation table, the map search, messages/motes, the language worker, Unity text/shaders).
Every Cooking decision runs for real. Some vanilla types cannot initialise headless (`ModsConfig`, `ReservationManager`,
`FloatMenuOption`); those are answered at the smallest seam and the reason is recorded in the test.

**Mutation-checked.** Each rule was broken on purpose and the suite had to notice: hygiene for everyone / for no one,
whole Masterful count to a split piece, donor not debited on merge, every absorbed serving Masterful (a boolean),
reward for containing rather than eating, ratio ignored, Purify also resetting rot or leaving the cause, no failed-stack
memory, no reservation release, attach rule too wide, unguarded whole-stack split, estimate unscaled. All 19 were caught.

| Existing suite | Result |
|---|---:|
| Offline core / Shooting / Melee | 58 / 74 / 295 PASS, 0 FAIL |
| Real runtime targets | 271 PASS, 0 FAIL |
| Real binding | 40 applied, 0 failed, 6 environment-blocked |
| Skill Learn transpiler | PASS |
| Finalizer semantics / progression | 15 / 26 PASS, 0 FAIL |
| Aspirant | 91 PASS, 0 FAIL |
| Medicine | 344 PASS, 0 FAIL |
| Transcendent Crafting | 604 PASS, 0 FAIL |
| Crafting/Construction Legendary notifications | 166 PASS, 0 FAIL |

Real binding is 40 (was 39): the new gizmo patch. The six blocks are the existing Unity-initialisation ones.

## Known limitations and follow-ups

* Masterful provenance is per stack, not per serving age. Rot is averaged by vanilla; only the future rate follows the ratio.
* The Masterful reward is one memory per meal that contained a Masterful serving, not one per serving.
* Recipe work skill is not checked (vanilla does not supply it to `Notify_RecipeProduced`).
* A mod that changes `stackCount` without `SplitOff` can skew a mixed stack toward Masterful (never above the stack).
* Auto Purify looks at the current map only and at spawned food; food in inventories, caravans or containers that are
  not spawned Things is not a target.
* The Purify Food gizmo and the Auto loop have had no in-game test; the shims above cannot exercise the UI or a live pather.

## Owner runtime checklist (NOT RUN)

Use **Dev mode** and *Grandmaster 21 → Promote all level 20 skills to Grandmaster* to make a Cooking Grandmaster.

1. **Boot 0.15.0 Beta.** Player.log shows `Cooking 21 | hygiene=True masterful=True (N food defs) ingestion=True freshness=True estimate=True purify=True`, and no red errors.
2. **A. Perfect Hygiene.** Dirty the kitchen; have the Grandmaster cook a stack. Report the food: poison 0. Repeat with a level-20 cook: vanilla poisoning still occurs.
3. **B. Creation.** Grandmaster stack of 10: inspect pane says *Masterful servings: 10 / 10*. A normal cook's stack shows no such line.
4. **C/D. Merge and split.** Use *split one serving off* repeatedly, haul stacks back together, and *report food stack* each time: the Masterful total never changes and never exceeds the stack.
5. **E. Save/load.** Save with mixed stacks; reload; counts identical.
6. **F. Ingestion.** *Mark ONE more serving Masterful* on a stack of ten, let colonists eat it all: exactly one *masterful meal* memory in total.
7. **G. Freshness.** Two identical stacks in the same room, one *marked Masterful*: after a day, *report* both: rot progress ratio about 0.2. Move one to a freezer and a fridge: vanilla behaviour, scaled.
8. **H. Purify.** *Poison food stack*, then Purify Food: the Grandmaster walks over, works about 3 seconds, poison is 0, count, rot and Masterful count unchanged.
9. **I. Auto.** Poison several stacks around the colony, right-click Purify Food → Auto Purify: they are cleaned nearest first, the job then ends by itself, and the Grandmaster resumes normal work.
10. **J. Modded food.** If a mod adds cooked food using `CompFoodPoisonable`: it shows Masterful servings when a Grandmaster cooks it and can be purified.
11. **Uninstall.** Run **Prepare Save for Uninstall** after eating Masterful meals: the result dialog counts the cleared memories and Purify jobs.
