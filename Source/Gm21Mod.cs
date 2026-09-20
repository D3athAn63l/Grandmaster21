using System;
using System.Globalization;
using UnityEngine;
using Verse;

namespace Grandmaster21
{
    public class Gm21Settings : ModSettings
    {
        public const double DefaultRequirement = 1000000000.0; // 1,000,000,000
        public const double MinRequirement = 1000.0;
        public const double MaxRequirement = 1000000000000.0;  // 1e12

        public double grandmasterXpRequirement = DefaultRequirement;
        public bool deterministicQuality = true;
        public bool showGrandmasterProgress = true;

        // NOTE: two former settings are gone, and neither is coming back.
        //   "grandmasterPreventsDecay" -- level 21 never decays; it is an achieved state.
        //   "clampGeneratedPawns"      -- generated pawns are capped at 20, unconditionally.
        // Both were options that could switch off a defining rule of the mod. Old config files
        // still containing those elements load fine; Scribe ignores elements with no field.

        /// <summary>Edit buffer for the requirement field; not saved.</summary>
        [Unsaved] public string requirementBuffer;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref grandmasterXpRequirement, "grandmasterXpRequirement", DefaultRequirement);
            Scribe_Values.Look(ref deterministicQuality, "deterministicQuality", true);
            Scribe_Values.Look(ref showGrandmasterProgress, "showGrandmasterProgress", true);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                Validate();
            }
        }

        public void Validate()
        {
            if (double.IsNaN(grandmasterXpRequirement) || double.IsInfinity(grandmasterXpRequirement))
            {
                grandmasterXpRequirement = DefaultRequirement;
            }
            grandmasterXpRequirement = Math.Max(MinRequirement, Math.Min(MaxRequirement, grandmasterXpRequirement));
            requirementBuffer = null;
        }
    }

    public class Gm21Mod : Mod
    {
        public static Gm21Settings Settings;

        public Gm21Mod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<Gm21Settings>();
            Settings.Validate();
        }

        public override string SettingsCategory()
        {
            return "GM21_ModTitle".Translate();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard list = new Listing_Standard();
            list.Begin(inRect);

            list.Label("GM21_Setting_Requirement".Translate());
            if (Settings.requirementBuffer == null)
            {
                Settings.requirementBuffer = Settings.grandmasterXpRequirement.ToString("F0", CultureInfo.InvariantCulture);
            }
            string edited = list.TextEntry(Settings.requirementBuffer);
            if (edited != Settings.requirementBuffer)
            {
                Settings.requirementBuffer = edited;
                double parsed;
                if (double.TryParse(edited, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                    && !double.IsNaN(parsed) && !double.IsInfinity(parsed))
                {
                    Settings.grandmasterXpRequirement =
                        Math.Max(Gm21Settings.MinRequirement, Math.Min(Gm21Settings.MaxRequirement, parsed));
                }
            }
            list.Label("GM21_Setting_RequirementDesc".Translate(
                Settings.grandmasterXpRequirement.ToString("N0", CultureInfo.InvariantCulture),
                Gm21Settings.MinRequirement.ToString("N0", CultureInfo.InvariantCulture),
                Gm21Settings.MaxRequirement.ToString("N0", CultureInfo.InvariantCulture)));

            if (list.ButtonText("GM21_Setting_ResetRequirement".Translate()))
            {
                Settings.grandmasterXpRequirement = Gm21Settings.DefaultRequirement;
                Settings.requirementBuffer = null;
            }

            list.GapLine();

            list.CheckboxLabeled("GM21_Setting_DeterministicQuality".Translate(),
                ref Settings.deterministicQuality, "GM21_Setting_DeterministicQualityDesc".Translate());

            list.CheckboxLabeled("GM21_Setting_ShowProgress".Translate(),
                ref Settings.showGrandmasterProgress, "GM21_Setting_ShowProgressDesc".Translate());

            list.Label("GM21_Setting_PermanenceNote".Translate());

            DrawMaintenanceSection(list);

            list.End();
            base.DoSettingsWindowContents(inRect);
        }

        /// <summary>
        /// The destructive maintenance section, kept at the very bottom behind its own heading.
        ///
        /// The button is deliberately awkward to hit by accident: it sits below everything else,
        /// is narrower than the full settings width, and always routes through a confirmation
        /// dialog before touching anything.
        /// </summary>
        private static void DrawMaintenanceSection(Listing_Standard list)
        {
            list.GapLine(24f);

            Text.Font = GameFont.Medium;
            list.Label("GM21_Maintenance_Header".Translate());
            Text.Font = GameFont.Small;

            GUI.color = new Color(0.75f, 0.75f, 0.75f);
            list.Label("GM21_Maintenance_Desc".Translate());
            GUI.color = Color.white;

            list.Gap(6f);

            Rect row = list.GetRect(32f);
            Rect button = new Rect(row.x, row.y, Mathf.Min(300f, row.width), row.height);
            if (Widgets.ButtonText(button, "GM21_Uninstall_Button".Translate()))
            {
                Gm21Uninstall.PromptFromSettings();
            }
        }
    }
}
