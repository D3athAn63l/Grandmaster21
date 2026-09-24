# Artifact feedback: focused owner check (0.12.0 Beta)

The owner already confirmed all seven phenomena mechanically execute, Vampiric healing works,
forced results work, and owned artifact identity displays. This pass needs a new presentation and
regeneration check, not another natural-crafting grind. Use a copied save and note the build stamp.

1. **Item information.** Equip a forced known-tier weapon. Selected-item inspection should show
   craftsmanship, phenomenon, actual manifestation chance and 180-tick (~3-second at 1×) cooldown.
   Hover/open the info card for the mechanical description, effective damage, range/targets and
   safety rules. Cycle all seven with `DEV: Set phenomenon`. Check an already-owned unusual item:
   its established label and actual chance are readable, without creation odds, seed or origin lore.
   Check apparel/legacy None/ordinary weapons for unwanted text or errors. Look for clipped text.
2. **Preview visuals/audio without damage.** Use `DEV: Phenomenon VFX only` on a cell or pawn;
   it must leave health, RNG counters and cooldown unchanged. Then use the ordinary DEV trigger
   on standing hostiles to check actual ordered chain/secondary targets. Each proc should show a
   brief phenomenon-name callout. Check these distinct cues at normal zoom and 1× speed:

   | Phenomenon | Visual | Local sound reused |
   | --- | --- | --- |
   | Chain Lightning | Blue bent arcs along actual chain order, impact glows | EnergyShield_AbsorbDamage |
   | Smite | Gold vertical-looking beam, large flash and radius-five ring | Thunder_OnMap, reduced volume |
   | Flame Wave | Orange outward glow particles and victim flashes | HissJet |
   | Frost Nova | Pale cyan ring, outward mist and victim flashes | EnergyShield_Reset |
   | Gravity Crush | Purple inward dust, compression ring and impact | Pawn_Melee_Punch_HitBuilding_Generic |
   | Vampiric Strike | Crimson particles converging from victim to wielder; recovery crosses | Power_OnSmall |
   | Spatial Slash | Violet narrow rifts at primary/secondary impacts | Execute_Cut |

   The first Chain impact can be a flash without an arc if it coincides with the captured origin.
   Visual rings are feedback only. No camera shake, global sound, new art/shader or gameplay explosion
   is used. Judge color, scale, visibility, timing, audio suitability and excessive clutter in game.
3. **Health and battle provenance.** Deal real phenomenon damage. Health should retain native injury
   names with an artifact qualifier, e.g. `bruise (Smite)` or `cut (Spatial slash)`. Hover the wound
   and check battle history for the named phenomenon. Check repeat hits merging into an existing
   wound; only that affected wound should gain the qualifier. Merged wounds show the latest artifact
   contribution, while battle entries retain event history. Frost gets a named status/event; lethal
   Vampire recovery gets an event without pretending an extra stab occurred. Fully absorbed bonus
   damage must not produce a false injury entry. Logs should have one concise entry per damaged pawn.
4. **Immediate Vampire healing.** Use measured actual bonus damage, or the triggering packet when
   it already downs/kills the target. Damage 20 permits 7 immediate severity; damage 100 caps at 8.
   Armor-absorbed bonus damage grants none. No theoretical damage or extra-packet summing. Confirm
   immediate healing goes to the highest-severity ordinary wound first.
5. **Regenerating.** Immediately after a positive Vampire proc, the wielder gets one `Regenerating`
   status for 60 ticks. Damage 20 supplies 4 further healing total; damage 100 caps this at 6.
   Normally six pulses, every 10 ticks; each reselects the most severe eligible wound. No scar,
   missing-part, cancer, infection, disease, implant or other condition removal; no resurrection.
   Healthy pawns still get the brief indicator but unused pulse allowances expire. Test with
   RegenNanites disabled first, then enabled, so its independent healing is not attributed to GM21.
6. **Refresh and saves.** While paused/in slow motion, repeat the DEV Vampire trigger during the
   buff. There must remain one status; duration resets to 60 and budget becomes the larger remainder,
   capped at 6, not their sum. Remember each DEV proc also legitimately applies its immediate heal.
   Save immediately after a proc and midway between pulses; quit/reload. Check remaining duration,
   pulse timing and budget, with no refill/extra pulse. Death cancels the buff. A vanished cosmetic
   flash after reload is acceptable.
7. **Safety.** Put friendlies, colony prisoners, walls and furniture in each area. No new collateral
   damage, ignition, snow/temperature changes, teleportation or persistent objects. Repeat downing
   and lethal primary hits, map-edge impacts, dense crowds and rapid DEV previewing. Check bounded
   effects, no recursive proc chain, no error spam and no lingering motes. Compare Core/Harmony/GM21
   with the normal mod list; custom attack-path support has not expanded.
8. **Natural combat, Dev Mode off.** Observe several genuine procs with a native bullet weapon and
   a native melee weapon. Normal chance/cooldown apply; do not count a probability failure as a
   missing visual. Once a proc occurs, can you immediately identify that a phenomenon happened,
   without opening health or Player.log? Confirm DEV buttons disappear. This is the acceptance gate.

Please report the phenomenon/tier, zoom and speed, whether the sound was audible, clipped/misleading
text, the observed healing totals, and a screenshot/short clip if an effect is hard to recognize.
The host tests do not establish visual quality or native save-readback success. Keep PR #9 unmerged.
