# Crafting Grandmaster Legendary letter: owner check

Mod Settings → **Crafting Grandmaster Legendary notifications** (default **on**). Off hides vanilla's
"Legendary Work" letter for items a stored Crafting 21 Grandmaster makes with the Crafting skill.
Nothing else changes: the item is still Legendary, and every other letter and message is untouched.

## Runtime checklist

Use Dev Mode. `Grandmaster 21 → Promote all level 20 skills to Grandmaster` promotes a pawn's
level-20 skills; vanilla's dev skill setter puts a skill at 20 first.

1. Boot the game. Player.log shows the current build stamp and no new red errors.
2. **Setting on.** A Crafting 21 pawn crafts ordinary equipment (e.g. a gladius or a parka). The
   product is Legendary and vanilla's **Legendary Work** letter appears.
3. **Setting off.** The same pawn crafts another item. It is still Legendary, and **no** Legendary
   Work letter appears.
4. **Setting still off. Other pawns keep their letters:**
   * a Crafting 20 pawn crafts equipment: it is Masterwork and the vanilla **Masterwork** letter
     appears (with GM21, only a Grandmaster reaches Legendary through vanilla crafting);
   * a pawn with Artistic 21 but Crafting below 20 makes a sculpture: it is Legendary and the
     Legendary letter **appears**. A pawn who is a Grandmaster in both Artistic and Crafting gets it
     for a sculpture too. The setting follows the recipe's skill, not the pawn's other skills.
5. **Setting still off.** Trigger an unrelated letter or message, such as a raid or another dev
   promotion ("… has become a Grandmaster of …"). It appears as normal.
6. Turn the setting back on. The next Crafting Grandmaster craft shows the letter again. No
   restart is needed.

**Transcendent Crafting needs no check.** It never sends the vanilla letter (it sets Legendary
directly and reports with its own completion message), so the setting cannot affect it. Its
completion message, phenomena and feedback are unchanged. No full Crafting 21 torture test is
required.

## What was audited (real RimWorld 1.6 assembly)

* `QualityUtility.SendCraftNotification(Thing, Pawn)` is the only method that uses the
  `LetterCraftedLegendary*` keys. It sends exactly one letter and does nothing else.
* It has three callers: `GenRecipe.PostProcessProduct` (quality rolled with the recipe's work
  skill), `Frame.CompleteConstruction`, and `JobDriver_BuildCubeSculpture.PlaceAndFinish`. The last
  two both roll with Construction.
* The setting skips vanilla's letter only when all of these hold: the call is for the product and
  worker of the crafting call in progress, that recipe's skill is Crafting, the item is Legendary,
  and the worker's stored (not aptitude-adjusted) Crafting is 21. `LetterStack` is never patched.
* Automated coverage: `tools/verify-crafting-notification.sh` (see `Assemblies/README.md`).

**Compatibility (best effort).** GM21 runs its suppression prefix at Harmony `Priority.Last`, so
other mods' prefixes normally receive the call first. When it suppresses, it returns `false` to skip
vanilla's `SendCraftNotification` body. A bool-returning prefix that is still ordered after GM21 (for
example another `Priority.Last`, or an explicit `after` constraint) is then skipped too. With the
tested Harmony 2.4.1, void prefixes, postfixes and finalizers still run. No compatibility is claimed
with a mod that replaces this notification path, or that sends its own Legendary letter. If either
patch target ever fails to resolve, the setting does nothing, vanilla letters stay, and one warning
is logged.
