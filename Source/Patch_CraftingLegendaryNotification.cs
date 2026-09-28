using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// The crafting call in flight: what GenRecipe.PostProcessProduct is about to hand to
    /// QualityUtility.SendCraftNotification, and the skill its recipe rolled quality with.
    /// </summary>
    public struct Gm21CraftingFrame
    {
        public Thing product;
        public Pawn worker;
        public SkillDef workSkill;
    }

    /// <summary>
    /// Mod setting "Crafting Grandmaster Legendary notifications". A stored Crafting 21 Grandmaster
    /// makes Legendary work every time, so vanilla's "Legendary Work" letter turns into spam; the
    /// player may switch off THAT letter and nothing else. Presentation only.
    ///
    /// AUDITED AGAINST THE REAL 1.6 ASSEMBLY:
    ///   * QualityUtility.SendCraftNotification(Thing, Pawn) is the only method that loads the
    ///     LetterCraftedLegendary* keys. Its entire body is: return if there is no worker or no
    ///     compQuality, then send exactly one letter -- Masterwork or Legendary, art or plain --
    ///     and return. No tale, record, history or quality work happens there.
    ///   * It is not told the skill. Three vanilla sites call it: GenRecipe.PostProcessProduct
    ///     (quality rolled with recipeDef.workSkill), Frame.CompleteConstruction and
    ///     JobDriver_BuildCubeSculpture.PlaceAndFinish (both hard-coded SkillDefOf.Construction).
    ///   * Transcendent Crafting never reaches it: Building_MagicalWorkstation.TryComplete calls
    ///     CompQuality.SetQuality directly, which sends no letter, and reports completion with its
    ///     own GM21_TC_Completed message.
    ///   * LetterStack.ReceiveLetter has hundreds of callers and is never touched here.
    ///
    /// HOW: PostProcessProduct opens a frame recording (product, worker, recipe work skill) and a
    /// void finalizer restores the previous one. A prefix on SendCraftNotification skips the
    /// original -- which, for a Legendary item, sends only the Legendary letter -- when ALL hold:
    ///   * the setting is off;
    ///   * the call is for exactly the product and worker of the open crafting frame, so
    ///     construction, the cube sculpture, and any other mod's direct call are never matched;
    ///   * that recipe's work skill is Crafting (a sculpture's Artistic roll does not count, even
    ///     for a pawn who is also a Crafting Grandmaster);
    ///   * the item's quality, read from the same compQuality field vanilla branches on, is
    ///     Legendary (every Masterwork letter is left alone);
    ///   * the worker is a legitimate stored Crafting Grandmaster, Gm21.IsGrandmaster(worker,
    ///     Crafting) -- levelInt, never the aptitude-adjusted level.
    /// Any other case runs vanilla. Other mods' prefixes and postfixes on SendCraftNotification
    /// still run either way; only the vanilla letter is skipped.
    ///
    /// FAIL-SAFE: if either target no longer resolves, nothing suppresses, vanilla letters are shown,
    /// and one warning is logged. No save data, no ticks, no per-pawn or per-item state.
    /// </summary>
    public static class Patch_CraftingLegendaryNotification
    {
        /// <summary>True once both the crafting frame and the notification prefix are installed.</summary>
        public static bool Applied { get; private set; }

        private static Gm21CraftingFrame current;

        /// <summary>Called from Gm21Startup after PatchAll, like the bill ceiling bridge.</summary>
        public static void Apply(Harmony harmony)
        {
            Applied = false;
            try
            {
                MethodInfo craft = AccessTools.Method(typeof(GenRecipe), "PostProcessProduct");
                if (!IsCraftingSite(craft))
                    throw new MissingMethodException("GenRecipe.PostProcessProduct(Thing, RecipeDef, Pawn, ...) not found");
                MethodInfo notify = AccessTools.Method(typeof(QualityUtility), nameof(QualityUtility.SendCraftNotification),
                    new[] { typeof(Thing), typeof(Pawn) });
                if (notify == null)
                    throw new MissingMethodException("QualityUtility.SendCraftNotification(Thing, Pawn) not found");

                // Frame first: a notification prefix with no frame to match never suppresses anything.
                harmony.Patch(craft, Hook(nameof(Prefix_PostProcessProduct)), null, null,
                    Hook(nameof(Finalizer_PostProcessProduct)));
                harmony.Patch(notify, Hook(nameof(Prefix_SendCraftNotification)), null, null, null);
                Applied = true;
            }
            catch (Exception e)
            {
                Log.Warning("[Grandmaster 21] The \"Crafting Grandmaster Legendary notifications\" setting is "
                            + "unavailable this session (" + e.Message + "). Vanilla Legendary letters are shown "
                            + "as normal; nothing else is affected.");
            }
        }

        private static bool IsCraftingSite(MethodInfo m)
        {
            if (m == null || !m.IsStatic || m.ReturnType != typeof(Thing)) return false;
            ParameterInfo[] p = m.GetParameters();
            return p.Length >= 3 && p[0].ParameterType == typeof(Thing)
                && p[1].ParameterType == typeof(RecipeDef) && p[2].ParameterType == typeof(Pawn);
        }

        private static HarmonyMethod Hook(string name)
        {
            return new HarmonyMethod(AccessTools.Method(typeof(Patch_CraftingLegendaryNotification), name));
        }

        /// <summary>
        /// The decision, pure and allocation-free. True means "skip vanilla's letter for this call".
        /// </summary>
        public static bool ShouldSuppress(Thing thing, Pawn worker)
        {
            Gm21Settings settings = Gm21Mod.Settings;
            if (settings == null || settings.showCraftingGrandmasterLegendaryNotifications) return false;
            if (thing == null || worker == null) return false;
            if (!ReferenceEquals(thing, current.product) || !ReferenceEquals(worker, current.worker)) return false;

            SkillDef crafting = SkillDefOf.Crafting;
            if (crafting == null || current.workSkill != crafting) return false;

            ThingWithComps withComps = thing as ThingWithComps;
            CompQuality quality = withComps == null ? null : withComps.compQuality;
            if (quality == null || quality.Quality != QualityCategory.Legendary) return false;

            return Gm21.IsGrandmaster(worker, crafting);
        }

        // ---------------------------------------------------------------- patch bodies

        /// <summary>GenRecipe.PostProcessProduct prefix. Saves the enclosing frame so nesting restores.</summary>
        internal static void Prefix_PostProcessProduct(Thing product, RecipeDef recipeDef, Pawn worker,
            out Gm21CraftingFrame __state)
        {
            __state = current;
            current.product = product;
            current.worker = worker;
            current.workSkill = recipeDef == null ? null : recipeDef.workSkill;
        }

        /// <summary>
        /// GenRecipe.PostProcessProduct finalizer. VOID, for the reason Gm21ShootingPatches documents:
        /// an Exception-returning finalizer replaces the pending exception. This only restores state.
        /// </summary>
        internal static void Finalizer_PostProcessProduct(Gm21CraftingFrame __state)
        {
            current = __state;
        }

        /// <summary>QualityUtility.SendCraftNotification prefix. False skips only vanilla's letter.</summary>
        internal static bool Prefix_SendCraftNotification(Thing thing, Pawn worker)
        {
            return !ShouldSuppress(thing, worker);
        }
    }
}
