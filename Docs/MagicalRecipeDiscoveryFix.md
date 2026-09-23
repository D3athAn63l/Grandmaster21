# PR #8: empty recipe discovery repair

Version remains **0.11.0 Beta**. This is a focused repair on the existing PR branch, not a new
crafting architecture or expansion of RecipeWorker support.

## Root cause and reproduction

The original predicate rejected every recipe with a non-empty `workTableEfficiencyStat`.
In the supplied **real RimWorld 1.6 assembly**, `RecipeDef.ResolveReferences()` fills that field
with `StatDefOf.WorkTableEfficiencyFactor` when it is initially empty. Consequently ordinary
resolved recipes were rejected before their equipment/ingredient checks even ran.

The earlier tests constructed fresh `RecipeDef` objects without calling `ResolveReferences()`.
Changing those fixtures to execute the real resolver against the original PR DLL reproduced
three failures: the otherwise-valid weapon, apparel and material-first recipe cases. A separate
real-DLL probe observed the field change from absent to `WorkTableEfficiencyFactor`.

`RecipeDefGenerator.CreateRecipeDefFromMaker` was also audited in that same assembly. Ordinary
`ThingDef.recipeMaker` recipes are plain `RecipeDef` instances with the default `RecipeWorker`.
The generator's `SetIngredients` creates a first Stuff slot and sets `productHasIngredientStuff`
for material products, or creates fixed ingredient slots from `CostList`. Reference resolution
then supplies the standard bench speed and efficiency stats. Exact CLR item-class equality was
a separate compatibility bug, not the universal cause of zero vanilla recipes.

## Filter audit and precise changes

| Gate / diagnostic category | Decision |
|---|---|
| `recipeClass`, `customWorker` | Keep exact plain `RecipeDef` / `RecipeWorker`; unsupported custom recipe behavior remains excluded. XML inheritance and ordinary implied recipes are not CLR subclasses. |
| `wrongSkill` | Keep Crafting-only; reject absent skill references and Artistic/Construction recipes. |
| `productCount`, `specialProducts` | Keep exactly one primary product of count one, no special/byproducts. |
| `efficiency` | Reject pawn efficiency stats and custom bench efficiency stats. **Allow the standard `WorkTableEfficiencyFactor` inserted by vanilla.** |
| Live bench efficiency | New bench explicitly declares factor 1. Commitment checks the actual stat is **exactly 1** before splitting, consuming or rolling; changed/nonfinite values cannot create an unsupported quantity. No existing Def is modified. |
| `productClass`, `ingredientClass` | Replace exact `ThingWithComps`/`Apparel` equality with guarded `IsAssignableFrom`. Require a concrete, closed, default-constructible class; reject corpse, minified and unfinished-item wrappers. Apparel subclasses qualify naturally. |
| `nonEquipment`, `notDurable`, `noQuality` | Keep weapon/apparel-only, hit points, stack limit one, and `CompQuality`. Components/resources/ammunition, buildings and artwork do not qualify as ordinary output. |
| `consumable` | Keep ingestible/medicine/drug/Stuff-output, destroy-on-drop, single-use shooting, usable, delayed-destruction and reloadable-equipment exclusions. |
| `surgery`, `mechRecipe` | Keep surgery, mechanitor, gestation and forming exclusions. |
| `mixedIngredients`, `wholeStacks`, `ingredientSemantics` | Keep no-mix/exact-count semantics and the exact standard volume value getter. |
| `ingredientFilter`, `ingredientCount` | Keep nonempty valid filters and finite positive requirements; missing entries are explicitly categorized. |
| `overlappingIngredients`, `catalystIngredient`, `ingredientKind` | Keep disjoint slots, separate catalyst commitment, and exclusion of quality-bearing/food/drug/medicine ingredients. |
| `stuffSemantics` | Keep first-slot compatible Stuff and matching `productHasIngredientStuff`; fixed-material outputs cannot request ingredient Stuff. |
| `workAmount` | Keep finite nonnegative resolved work; vanilla's default `workAmount = -1` still delegates to the product's `WorkToMake` through the native API. |
| Availability | Research is evaluated by the selection dialog, not at static startup before a game is loaded. Unresearched recipes are not permanently removed from the discovery registry. |

Compatible item subclasses do not imply support for arbitrary third-party recipe or item
callbacks. Those compatibility limits remain as documented for the foundation.

## Diagnostics and injection

Startup emits one summary with total scanned, supported, and **first rejection reason** counts,
sorted by category. For example (illustrative counts, not a measured colony):

```text
[Grandmaster 21] Magical recipe discovery: scanned=21 supported=1; rejected: customWorker=20; research checked in selection dialog; vanilla recipes unchanged.
```

Development mode additionally emits at most **eight samples total**, at most **two per category**:

```text
[Grandmaster 21][Magical][debug] Make_Example rejected: ingredientClass
```

Unexpected per-recipe exceptions receive an `exception` count and a bounded dev sample with the
exception type, instead of an unbounded warning per Def. There is no per-tick logging. Recipes
are scanned in DefName order for reproducible samples. Startup remains one-time; registration
deduplicates both recipes and the actual `CompProperties_Artifact` on shared product ThingDefs.

## Verification and limits

- **114 passed, zero failed, one environment-blocked** in the real-DLL feature suite. New coverage
  includes native `ResolveReferences`, accepted equipment/apparel/ingredient subclasses, invalid
  classes, custom worker/efficiency rejection, categorized/bounded diagnostics, one-time shared
  product comp injection, and the neutral-efficiency guard before ingredient staging.
- Six representative **fixtures** for longsword, knife, revolver, assault rifle, duster and power
  armor pass after the real `RecipeDefGenerator.SetIngredients` and `RecipeDef.ResolveReferences`.
  None is intentionally excluded. These are equivalent metadata cases, **not loaded Core XML**;
  the attachments contain DLLs, not the installed Core Def database. No recipes are hardcoded in
  the implementation.
- The native implicit `WorkToMake` probe is **blocked by missing Steamworks.NET** in this headless
  DLL set. The six policy fixtures therefore supply an explicit work amount after that probe;
  no replacement stat implementation or fake runtime assembly is used.
- Existing offline suites pass: **57 progression, 74 Shooting, 295 Melee**. They use the existing
  repository stub fixtures for those older systems only, not for compiling the new feature.
- Existing real-DLL progression harness: **26 passed**. Native Learn IL-pattern check: **passed**.
  Runtime-target harness: **168 passed, one failure** due to the missing Steamworks.NET dependency.
- Live Harmony patching and finalizer execution were attempted, but **aborted** when the supplied
  Harmony/MonoMod runtime tried to access a reflection API unavailable on this .NET 8 host.
  These suites are not reported as passing. No unrelated runtime source was changed to mask this.
- The real RimWorld/Unity/Harmony DLL build has zero compiler warnings/errors; XML validates.

The user confirmed the original build loads, the research/bench exist, the bench renders, and
the gizmo/dialog open. **The repaired discovery has not been run in a Unity game session here.**
Expected next check: restart RimWorld with the rebuilt DLL, verify a nonzero supported count,
complete the relevant research and inspect the recipe list. Then run the commitment, worker
change, save/reload and exactly-once completion checklist in `MagicalCrafting.md` before merging.
