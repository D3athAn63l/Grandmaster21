using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grandmaster21.Transcendent
{
    public sealed class Dialog_MagicalRecipes : Window
    {
        private readonly Building_MagicalWorkstation bench;
        private Vector2 scroll;
        private string search = "";
        public override Vector2 InitialSize { get { return new Vector2(700f, 640f); } }
        public Dialog_MagicalRecipes(Building_MagicalWorkstation bench)
        {
            this.bench = bench;
            doCloseX = true; doCloseButton = true; absorbInputAroundWindow = true; forcePause = true;
        }
        public override void DoWindowContents(Rect rect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, rect.width - 30, 35), "GM21_TC_Choose".Translate());
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0, 40, rect.width, 72), "GM21_TC_DialogHelpTiers".Translate(bench.Config.label, bench.Config.Catalyst.LabelCap));
            search = Widgets.TextField(new Rect(0, 114, rect.width, 30), search);
            List<RecipeDef> recipes = TranscendentRecipes.Eligible.Where(r => r.AvailableNow
                && (search.Length == 0 || r.label.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0)).ToList();
            Rect outer = new Rect(0, 155, rect.width, rect.height - 198);
            Rect inner = new Rect(0, 0, outer.width - 18, recipes.Count * 38f);
            Widgets.BeginScrollView(outer, ref scroll, inner);
            for (int i = 0; i < recipes.Count; i++)
            {
                RecipeDef recipe = recipes[i];
                Rect row = new Rect(0, i * 38f, inner.width, 34);
                if (Widgets.ButtonText(row, recipe.LabelCap)) Select(recipe);
                TooltipHandler.TipRegion(row, string.Join("\n", recipe.ingredients.Select(x => x.SummaryFor(recipe)).ToArray())
                    + "\n+ 1 " + bench.Config.Catalyst.LabelCap);
            }
            Widgets.EndScrollView();
        }
        private void Select(RecipeDef recipe)
        {
            if (recipe.products[0].thingDef.MadeFromStuff)
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();
                foreach (ThingDef material in TranscendentRecipes.Materials(recipe))
                {
                    ThingDef captured = material;
                    options.Add(new FloatMenuOption(material.LabelCap, () => { bench.Choose(recipe, captured); Close(); }));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
            else { bench.Choose(recipe, null); Close(); }
        }
    }
}
