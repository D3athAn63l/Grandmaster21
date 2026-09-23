# Crafting 21: Magical craftsmanship

This experimental vertical slice adds one workstation and a separate, persistent Magical
craftsmanship marker. Every successful project produces one **vanilla Legendary** item. At
commitment it has a 50% chance of Magical craftsmanship and a 50% chance of ordinary Legendary
craftsmanship. Magical items have no additional combat powers in this release.

## Playing the slice

1. Research **Magical Craftsmanship**, after Fabrication (6,000 research).
2. Build a **magical workstation** from the Production menu. It costs 250 steel, 100 plasteel
   and 10 advanced components, requires Construction 12, and occupies three cells.
3. For this foundation release, use Development mode's **Spawn thing** action to obtain
   `GM21_MagicalCatalyst`. This is the only supplied acquisition path. Catalysts are not Stuff,
   have no resource category or trader tags, and cannot be traded.
4. Select the workstation, choose an eligible researched recipe, and choose its material when
   applicable. Supply the ordinary recipe ingredients and one catalyst.
5. Give a legitimate **Crafting 21 Grandmaster** Crafting work. They reserve the workstation,
   haul the ingredients, and commit them when everything has arrived. Recipe skill requirements
   must also be met at the start. A displayed/stat-modified 21 does not grant authorization.
6. The pawn works over time. Needs, drafting, combat and sleep can interrupt the job; another
   authorized Crafting Grandmaster can resume the same workstation project.
7. The completed item is placed near the workstation. Inspecting a Magical result shows
   **Craftsmanship: Magical** in addition to its normal Legendary quality. Its original material
   is unchanged. Ordinary Legendary fallback items have no Magical label.

Each workstation owns one project. A selection may be replaced **before commitment**; after
commitment there is no cancel or refund. The outcome is hidden until completion and is never
rerolled on resume or completion. Destroying the workstation loses the committed project and
any completed item still retained inside it. Building salvage does not refund project inputs.
An active workstation cannot be deconstructed; the definition cannot be minified/uninstalled.
Development mode has a destructive emergency abort with no refund.

## Architecture and verified API choices

The implementation was independently audited against the supplied real RimWorld 1.6
`Assembly-CSharp.dll`, Unity modules and Harmony DLL. No runtime stubs are used to compile this
feature. It starts from main at `2ef0f2579df3d811c495cb722dc4b208048789ce`, not the earlier prototype PR.

| Concern | Implementation |
|---|---|
| Player selection | `Dialog_MagicalRecipes`, workstation gizmo, native `FloatMenu` for Stuff |
| Authorization | Existing `Gm21.IsGrandmaster(pawn, SkillDefOf.Crafting)` reads stored Grandmaster state; start, work and completion each check authorization |
| Workstation | `Building_WorkTable` subclass implementing `IThingHolder`, with one `TranscendentProject` and one `ThingOwner<Thing>` |
| Scheduling | Dedicated `WorkGiver_Scanner`, `JobDef` and `JobDriver`; native reservations, pathing and carrying toils |
| Ingredient filters | One internal `Bill_Production`, persisted by the existing `BillStack`; no Bills tab or ordinary bench recipe edits |
| Ingredient search | Real private static `WorkGiver_DoBill.TryFindBestBillIngredients` and `TryFindBestBillIngredientsInSet_NoMix`, bound once to typed delegates; public `TryFindBestFixedIngredients` for the catalyst |
| Commitment | `Thing.SplitOff`, native owner staging, one `Rand.Int`, then `ClearAndDestroyContents(DestroyMode.Vanish)` |
| Output | `ThingMaker.MakeThing(product, stuff)`, `CompQuality.SetQuality(Legendary, ...)`, a real `CompArtifact`, and owner `TryDrop` |
| Research | Real `ResearchProjectDef`, Fabrication prerequisite and workstation `researchPrerequisites` |
| Deconstruction | Override of the real virtual `Building.DeconstructibleBy(Faction)`; no Harmony patch |

The normal `JobDriver_DoBill` stores long work in an `UnfinishedThing` associated with its
creator. This feature deliberately owns the state in the building so workers can change. Its
toil sequence stays structurally identical for start and resume, preserving saved toil indices.

Another audited difference matters for hauling: native `PlaceHauledThingInCell` only records
`job.placedThings` for known vanilla job definitions. This custom job instead uses
`Pawn_CarryTracker.TryDropCarriedThing` with `HaulAIUtility.UpdateJobWithPlacedThings` and a physical
reservation callback. It uses the native ingredient placement-cell search.

There are no new Harmony patches, global crafting-stat modifications, per-tick map/pawn scans,
or autonomous building progress. Recipe discovery and metadata-comp attachment run once at
startup. Ordinary recipe definitions and workbenches remain unchanged. The only additions to
existing output ThingDefs are `CompProperties_Artifact`, needed before new items and saved items
are instantiated; ordinary items default to no artifact tier.

## Eligible recipes

Discovery uses Def metadata and exact verified types, not recipe-name heuristics. Recipes must
use Crafting, a plain `RecipeDef`/`RecipeWorker`, exactly one primary product with count one,
and the standard volume ingredient-value getter. Products must be durable, nonstacking weapons
or apparel with `CompQuality`, using concrete `ThingWithComps`-compatible item classes (including
Apparel subclasses). Corpse, minified and unfinished-item wrappers are excluded.

The first slice rejects:

- Multi-output and batch recipes, special side products, efficiency-based output scaling,
  special workers, surgery, mech gestation and forming recipes.
- Mixing within ingredient slots, whole-stack semantics, empty/overlapping ingredient filters,
  nonpositive or nonfinite quantities, and ingredients with incompatible Thing classes or quality.
- Food, drugs, medicine, raw-material/component outputs, consumables, buildings, art and
  non-equipment. Single-use shooting verbs, destroy-on-drop/delayed-destruction items, usable
  items and charge-based apparel/equipment are excluded conservatively.
- Ambiguous Stuff recipes. A Stuff product must use `productHasIngredientStuff`; its first
  disjoint ingredient slot must contain only Stuff that can make that product. The player's
  chosen material restricts that slot. Fixed-material products must not request ingredient Stuff.
- The catalyst as an ordinary recipe ingredient.

The list is also filtered by native `RecipeDef.AvailableNow` research availability. These rules
intentionally omit some otherwise useful modded equipment. Harmony changes to ordinary product
generation and custom recipe-worker side effects are not adopted by this pipeline.

The standard `WorkTableEfficiencyFactor` populated by vanilla reference resolution is allowed;
custom efficiency stats remain excluded. The new bench explicitly sets factor 1, and commitment
rejects any non-neutral live efficiency before consuming ingredients. See
[the discovery repair audit](MagicalRecipeDiscoveryFix.md) for the zero-recipes root cause,
categorized startup diagnostics and regression coverage.

## Ingredient commitment and failure handling

Native ingredient search applies the bill filters, native quantities, search radius,
reachability, forbidden state and reservations. The catalyst is searched separately and exactly
one is added. At the bench, delivered receipts are checked against live stack counts, map and
location, then passed through the native no-mix selector again. Selected quantities may not
exceed what this job actually delivered, even if delivery merged into a larger stack.

All required quantities are split and staged in a nonmerging native owner before the random
choice. A precommit failure attempts to drop those uncommitted inputs; if placement is blocked,
the owner saves them and a recovery gizmo exposes them. No project or tier roll exists yet.

After staging, the workstation records the complete project and one locked roll, clears the
job's bill reference, removes the internal bill, and destroys the staged inputs. The project
stores a Def/count receipt, not refundable ingredients. Resumed jobs never repeat consumption.
An exception during irreversible consumption faults the project without a refund or another roll.
Arbitrary third-party exceptions inside split/spawn callbacks cannot be comprehensively tested
or guaranteed; native operations and the explicit failure boundaries were reviewed.

Completion sets Legendary quality and the locked artifact tier without calling the normal
random-quality generator. The saved owner retains the one generated output until placement
succeeds. Separate creation/delivery latches prevent regeneration when delivery is blocked.
If a placement callback throws after spawning, its owner entry is removed to avoid saving the
same item both on the map and inside the workstation. Invalid saved data or other completion
exceptions stop the project for inspection rather than refunding or rerolling it.

## Persistence

`TranscendentProject : IExposable` is deep-scribed by the workstation. It records schema version,
project ID, RecipeDef, product Def, selected Stuff Def, initiating pawn ID/name, committed
Def/count ledger, total/completed work, random seed, final tier, ingredient color, output latches
and fault state. Defs use `Scribe_Defs`; values use `Scribe_Values`; receipts use a deep collection.
The workstation owner is deep-scribed with the workstation as holder. Jobs persist their project
ID and use native target/bill serialization while gathering.

`CompArtifact.PostExposeData` saves artifact tier and origin on the item itself, independently of
`CompQuality`. This is intended to follow the normal item serialization through inventory,
equipment, apparel, stockpiles, maps and caravans. None of those transfer/reload scenarios has yet
been exercised in a running game. Changing/removing recipe or item mods during a project is
unsupported; missing Defs stop a project and can also prevent metadata injection on old items.

## Work calibration

The audited native `Toils_Recipe.DoRecipeWork` subtracts the recipe's pawn `workSpeedStat`, times
the bench's `workTableSpeedStat` when present, times elapsed ticks. At combined speed 1 this is
one work unit per tick. A RimWorld day has 60,000 ticks, or 2,500 per hour.

```text
totalWork = max(recipe.WorkAmountForStuff(selectedStuff) * 3, 45000)
actualSpeed = (recipe pawn workSpeedStat, or 1) * (recipe bench workTableSpeedStat, or 1)
effectiveSpeed = actualSpeed <= 1 ? actualSpeed : sqrt(actualSpeed)
completedWork += effectiveSpeed * elapsedTicks
```

45,000 work means 18 working hours: 1.5 days at a 12-hour work schedule, before meals, hauling,
travel and sleep. Expensive base recipes can exceed that floor. Combined speeds 0.5, 1, 4, 100
and 1,000,000 become 0.5, 1, 2, 10 and 1,000. Invalid/nonfinite speeds stall safely. This uses
each recipe's actual stats, not a hardcoded general-labor stat, and changes no global stats.
The custom job does not implement vanilla debug fast-crafting, recipe effects/sounds, bill
repeat/storage controls, recipe completion quests/tales or additional skill XP.

## Validation and remaining runtime gate

- Real-DLL optimized build: **passed, zero warnings/errors**, using Roslyn with real .NET
  Framework 4.7.2 reference assemblies and the supplied RimWorld/Unity/Harmony assemblies.
- Updated headless suite: **114 passed, zero failed, one environment-blocked** (native implicit
  WorkToMake evaluation requires the missing Steamworks.NET DLL). It covers authorization primitives, work
  calibration, recipe rejection cases, actual native ingredient selection, reflected API
  signatures, emitted IL/schema, XML, and real Scribe save-writing for projects and item metadata.
- XML is well formed and new top-level Def fields/types match real assembly metadata. Built-in
  texture paths are supplied in XML before blueprint generation; startup copies graphics from
  the loaded machining-table/advanced-component Defs. Asset rendering still needs the game.
- Existing runtime-target harness: **168 passed; one environment-blocked check**, reported as a
  failure by that harness because `com.rlabrecque.steamworks.net` is absent from the supplied DLL
  set. This is not a claim that the whole existing harness passed.
- Full Scribe **reload**, Def loading with game data, map hauling, gameplay and rendered UI:
  **not verified**. The Unity player is unavailable. Save-writing is not a reload test.

Run `tools/verify-transcendent.sh` as described in `Assemblies/README.md`. The fixtures use real
game classes, but bypass Def constructors that require Unity shaders; they are not a simulated
game or replacement runtime assemblies.

Before merging, run this in-game checklist on a disposable save:

| Scenario | Required result |
|---|---|
| Startup/research/selection | No Def or texture errors; research unlocks bench; compatible researched recipes and materials appear |
| Unauthorized pawn and missing inputs | Stored Crafting 20 (including aptitude-boosted pawns) cannot start/work/finish; missing material or catalyst consumes nothing |
| Commitment | Exact real recipe quantities and exactly one catalyst disappear once; no refund/cancel option |
| Pause/resume/worker change | Draft, combat, needs and sleep interrupt; another stored Crafting 21 continues the same work and locked roll |
| Save/exit/reload | Recipe, Stuff, seed/tier, total work and partial work match; continue and finish exactly once |
| Output blockage/reload | Clear nearby space after saving/reloading; exactly one retained Legendary item is delivered |
| Both outcomes | Magical and ordinary Legendary fallback occur; material remains unchanged; no later tier can roll |
| Item transfer/reload | Marker survives equipment, apparel, inventory, stockpile, map transfer and caravan round trips |
| Destruction/deconstruction | Active bench cannot deconstruct or uninstall; destruction/emergency abort loses all project value |
| Unrelated behavior | Ordinary crafting, quality rules, Shooting, Melee and Grandmaster progression retain main's behavior |

## Removal and deferred work

The existing **Prepare Save for Uninstall** action still cleans pawn skill/progression state. It
does not delete new workstations, catalysts or artifact metadata. Before using it, remove every
magical workstation and catalyst across maps, inventories and caravans; destroying active benches
loses their projects. Finished equipment uses its original item Def but loses this mod's metadata
when the mod is removed. The full cleanup/removal/reload cycle remains unverified; keep a backup.

Mythical and Divine gameplay, further research, proof/key items, phenomena, artifact powers,
custom art, catalyst manufacturing and broader recipe adapters are intentionally deferred.
