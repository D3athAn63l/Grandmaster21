# Crafting 21: ordered owner torture test (0.12.0 Beta)

Use disposable copies, keep Player.log, and record build stamp, mod list, item IDs, seeds/counters,
observed damage and before/after work. Headless checks are not gameplay approval. Stop on a new
startup, save or duplication error before moving to combat. Run first with Core + Harmony + GM21,
then repeat the relevant checks with the owner's normal mod list. Required dependency DLCs for
unrelated mods do not establish a Core-only pass.

1. **Prepare upgrades before installing.** Back up saves and the prior DLL. In any earlier PR #9
   test build, interrupt catalyst jobs, record bill settings, delete all three catalyst recipe bills
   across all fabrication benches and save. Recreate them after updating. Old Bill_Production
   objects are not auto-converted to Bill_ProductionWithUft; old non-UFT work cannot be recovered.
   Keep active artifact workstation projects for migration checks; they use a separate system.
2. **Startup and Def loading.** Check 0.12.0 Beta and the expected source commit in Player.log.
   No missing UnfinishedComponent/GeneralLaborSpeed, bad recipe references, XML/Harmony errors,
   duplicate Defs or feature-disabled warning. Confirm all three recipes create ordinary bills.
3. **Save compatibility before progression.** Load a prepared PR #9 save and a 0.11/schema-one
   Magical save. Save, quit fully, reload. Retain active project's exact work/outcome, ingredients,
   selected Stuff and origin; no free second output. Old artifacts/committed legacy projects gain
   no retroactive phenomenon. Check ordinary colonists, weapons and skill values too.
4. **Progression and manufacture access.** Research Fabrication → Magical → Mythical → Divine.
   Verify each next recipe/bench unlock, the original costs, one catalyst product and Crafting
   requirements 12/15/18. A qualified non-GM may manufacture; an underqualified pawn may not.
   Catalysts have their isolated stockpile category, are not Stuff and are not trader stock.
5. **Each long catalyst bill, especially Divine Essence.** Disable fast crafting. Start, note
   work remaining and ingredients, draft/undraft, force food/sleep, interrupt with another job,
   suspend/resume the bill and temporarily cut bench power. The same UFT and its reduced work
   must return, not a new full-work job. Save/quit/reload both during work and while interrupted.
   Finish: exactly one product, exactly one ingredient charge, UFT gone, correct bill count.
6. **Vanilla UFT ownership/edges.** Give another skill-qualified crafter the same bill: they must
   not take over the original crafter's UFT. Re-enable the original crafter and confirm resume.
   Test moving/forbidding the UFT and lost bench access; no duplicate ingredients/output. Cancel
   a disposable UFT and verify normal vanilla behavior, separately from artifact no-refund rules.
   UFTs must not appear in Transcendent equipment discovery or acquire artifact metadata.
7. **Artifact authorization and persistence.** Level 20 or merely displayed/inspired 21 cannot
   commit/work/deliver; real stored Crafting 21 can. Test all three stations, material selection,
   one catalyst consumed, one project per bench, needs interruptions and another legitimate GM
   resuming. Save/quit/reload mid-work and near completion: fixed outcome/seed/work, one Legendary
   output. Block placement, reload and unblock: retained output delivered once. Confirm active
   deconstruction denial, destructive loss and DEV abort without refund; no cloning via those paths.
8. **Create combat fixtures cheaply.** Enable Dev Mode. At a bench use `DEV: Next result` and
   `DEV: Next phenomenon` before commitment; choose a known permitted tier. Use `DEV: Finish work`,
   then let a legitimate GM deliver. Make one native bullet weapon and one native melee weapon;
   include ordinary Legendary fallback and apparel controls. Equip a weapon and use `DEV: Set
   phenomenon` to cycle all seven effects and `DEV: Artifact state` to log counter/cooldown.
   `DEV: Trigger phenomenon` bypasses probability/cooldown for effect inspection against a standing
   hostile. It does NOT exercise the original weapon hit or prove the lethal-hit fix. Turning Dev
   Mode off removes these controls; normal UI must reveal no secret progression information.
9. **Primary-hit matrix on both supported paths.** With genuine weapon attacks, strike standing
   hostiles that (a) remain standing, (b) go down, (c) die. Outside cooldown each positive finite
   first packet advances counter once; successful procs commit cooldown even if no extra recipient
   remains. Prepare fragile hostiles to obtain real downing/lethal hits. Repeat against already
   downed/dead targets, allies, player pawns and colony prisoners: no proc. Fully absorbed/zero
   damage must not advance counter. Chance failure is expected; use logged state, not absence of
   visual effects, to distinguish an attempted roll from no opportunity.
10. **Seven-effect matrix after survival, downing and death.** Place standing hostiles, allies,
    prisoners, buildings and walls around the impact location; include targets just outside range.

    | Effect | Required observation |
    | --- | --- |
    | Chain Lightning | Primary first if safe, otherwise a nearby hostile from impact cell; at most four distinct recipients, six cells per hop, LOS; no second hit on dead/downed primary |
    | Smite | Captured impact center, radius five, at most sixteen current safe pawns; no buildings/terrain/allies/prisoners |
    | Flame Wave | Impact center, radius three, only controlled injuries; no world fire/ignition/collateral |
    | Frost Nova | Impact center, radius four, safe standing hostiles only; ×0.6 movement, 300 ticks; no stack or timer refresh on repeat |
    | Gravity Crush | Bonus blunt and 90-tick stun only while primary valid; no corpse/downed damage/stun, but successful proc still commits cooldown |
    | Vampiric Strike | Survival uses actual bonus stab damage; primary already killed/downed uses actual first triggering packet instead; see exact-budget step |
    | Spatial Slash | Skip dead/downed primary, retain at most one safe secondary within three cells of impact; no teleportation |

11. **Exact Vampire budgets.** Wound the wielder with several nonpermanent injuries. For actual
    eligible damage 10, total healing is at most 3.5 severity; for 30 or more it is at most 8,
    shared across all wounds, further limited by available wounds. On a surviving primary, use
    observed bonus stab damage after armor; zero absorbed bonus means zero healing even if the
    original hit dealt damage. On downing/lethal primary, use observed triggering packet damage,
    never theoretical weapon damage or the sum of extra packets. Check permanent scars, healthy
    wielders and dead wielders: no scar removal, fabricated wounds or resurrection. Bonus stab
    itself killing/downing still uses actual bonus damage. Zero/negative/nonfinite inputs are
    covered headlessly; only inject them in a controlled compatible test mod if available.
12. **Packet, cooldown and provenance torture.** Exercise multi-damage melee tools and bullet
    ExtraDamages: first positive qualifying packet consumes the one opportunity, including failed
    rolls and cooldown rejection. Wait through the 180-tick successful cooldown; later attacks
    resume normal attempts. Burst projectiles are separate impacts, not one attempt per burst.
    Switch/drop weapons while a projectile flies: provenance follows its saved launch weapon.
    No callback for another pawn may steal the primary's opportunity. Effects must never generate
    recursive procs. Check counter/cooldown continuity after save/quit/reload and item transfers.
13. **Spatial and interruption edges.** Repeat at map edges, corners, behind walls/doors, with
    sparse targets and a crowd larger than sixteen. Killed primary may despawn; effects must still
    center on its captured cell. Test wielder death/despawn/map departure before impact: fail safely.
    If using a diagnostic mod, throw in an original/nested attack: preserve its exception and restore
    prior scope; the next ordinary hit must not inherit stale artifact context. Without such a mod,
    record the live exception scenario as untested; the host only executes direct finalizer checks.
14. **Held items, existing skills and normal mod list.** Save/reload while equipped, in inventory,
    worn and on the ground; caravan/map transfer, then inspect identity/seed/counter/cooldown.
    Verify ordinary equipment and vanilla crafting unchanged; Shooting21 and Melee21 retain their
    behavior, including Downed doctrine without any new artifact-specific suppression. Repeat the
    combat safety matrix with normal armor/damage mods. Custom lasers/explosive workers/replacement
    verbs remain unsupported adapters; no proc on such a path is not proof the vanilla repair failed.
    If testing rare outcomes, use the existing Divine bench DEV menu and internal developer notes;
    test their held-item persistence/manifestation without exposing them in ordinary progression UI.
15. **Finish the evidence.** Turn Dev Mode off, save, quit and reload once more. Attach Player.log,
    expected/actual results, counter/cooldown snapshots and a minimal failing save for any defect.
    Keep PR #9 unmerged until runtime gates are reviewed. Do not mark an unperformed row passed.
