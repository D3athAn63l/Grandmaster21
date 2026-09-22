// Headless checks against the real game assemblies, not runtime stubs.
// ThingDef fixture constructors are bypassed because BuildableDef loads Unity shaders.
// These are policy/API/schema/save-writing checks, NOT a running-map or reload test.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml.Linq;
using Grandmaster21;
using Grandmaster21.Transcendent;
using Mono.Cecil;
using Mono.Cecil.Cil;
using RimWorld;
using Verse;

internal static class TranscendentChecks
{
    static int pass, fail;
    static readonly Assembly Mod = typeof(TranscendentProject).Assembly;
    static readonly BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static void Check(string name, bool result)
    {
        Console.WriteLine((result ? "PASS " : "FAIL ") + name);
        if (result) pass++; else fail++;
    }
    static object CallMath(string name, object arg)
    {
        return Mod.GetType("Grandmaster21.Transcendent.TranscendentMath").GetMethod(name, Any).Invoke(null, new[] { arg });
    }
    static ThingDef Def(string name)
    {
        ThingDef d = (ThingDef)FormatterServices.GetUninitializedObject(typeof(ThingDef));
        d.defName = name; d.label = name; d.category = ThingCategory.Item;
        d.thingClass = typeof(ThingWithComps); d.comps = new List<CompProperties>();
        d.virtualDefs = new List<ThingDef>(); d.stackLimit = 75; d.useHitPoints = true; d.statBases = new List<StatModifier>();
        return d;
    }
    static IngredientCount Ingredient(ThingDef def, int count)
    {
        IngredientCount c = new IngredientCount(); c.filter.SetAllow(def, true); c.SetBaseCount(count); return c;
    }
    static RecipeDef Recipe(SkillDef skill, ThingDef product, ThingDef ingredient)
    {
        RecipeDef r = new RecipeDef { defName = "TestRecipe", workSkill = skill, workAmount = 6000 };
        r.products.Add(new ThingDefCountClass(product, 1));
        r.ingredients.Add(Ingredient(ingredient, 10));
        r.fixedIngredientFilter.SetAllow(ingredient, true);
        return r;
    }
    static bool Supported(RecipeDef r, SkillDef skill, ThingDef catalyst)
    {
        object[] args = { r, skill, catalyst, null };
        return (bool)Mod.GetType("Grandmaster21.Transcendent.TranscendentRecipes")
            .GetMethod("IsSupported", Any, null, new[] { typeof(RecipeDef), typeof(SkillDef), typeof(ThingDef), typeof(string).MakeByRefType() }, null)
            .Invoke(null, args);
    }
    static void MathAndAuthorization()
    {
        foreach (double speed in new[] { 0d, 0.25, 0.5, 1d, 4d, 100d, 1000000d })
        {
            double expected = speed <= 1 ? speed : Math.Sqrt(speed);
            Check("speed " + speed, Math.Abs((double)CallMath("EffectiveSpeed", speed) - expected) < 1e-8);
        }
        foreach (double invalid in new[] { -1d, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Check("invalid speed stalls: " + invalid, (double)CallMath("EffectiveSpeed", invalid) == 0);
        Check("minimum is 1.5 twelve-hour working days", (double)CallMath("WorkAmount", 6000f) == 2500 * 12 * 1.5);
        Check("expensive recipe uses x3", (double)CallMath("WorkAmount", 90000f) == 270000);
        int magical = 0, ordinary = 0, unexpected = 0;
        for (int i = -5000; i < 5000; i++)
        {
            string tier = CallMath("RollFromSeed", i).ToString();
            if (tier == "Magical") magical++; else if (tier == "None") ordinary++; else unexpected++;
        }
        Check("10,000 seeds: exactly 50/50, no other outputs", magical == 5000 && ordinary == 5000 && unexpected == 0);
        Check("null skill never authorizes", !Gm21.IsGrandmaster((SkillRecord)null));
        Check("null pawn never authorizes", !Gm21.IsGrandmaster(null, new SkillDef()));
        Check("stored 20 is not authorized", !Gm21.IsGrandmaster(new SkillRecord { levelInt = 20 }));
        Check("stored 21 is authorized", Gm21.IsGrandmaster(new SkillRecord { levelInt = 21 }));
        Check("vanilla quality enum unchanged", Enum.GetNames(typeof(QualityCategory)).Length == 7 && (int)QualityCategory.Legendary == 6);
    }
    static void RecipePolicy()
    {
        // Bind only this fixture dependency under the same guard used by the game's Def loader.
        FieldInfo binding = typeof(DefOfHelper).GetField("bindingNow", Any);
        binding.SetValue(null, true);
        try { System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(StatDefOf).TypeHandle); }
        finally { binding.SetValue(null, false); }
        StatDefOf.MedicalPotency = new StatDef { defName = "TestMedicalPotency" };
        SkillDef crafting = new SkillDef { defName = "TestCrafting" };
        ThingDef catalyst = Def("TestCatalyst"), steel = Def("TestSteel"), product = Def("TestWeapon");
        product.stackLimit = 1; product.tools = new List<Tool> { new Tool() };
        product.comps.Add(new CompProperties { compClass = typeof(CompQuality) });
        Func<RecipeDef> make = () => Recipe(crafting, product, steel);
        Check("durable single-output crafting weapon accepted", Supported(make(), crafting, catalyst));
        RecipeDef r = make(); r.products.Add(new ThingDefCountClass(product, 1));
        Check("multiple outputs rejected", !Supported(r, crafting, catalyst));
        r = make(); r.products[0].count = 2;
        Check("batch outputs rejected", !Supported(r, crafting, catalyst));
        r = make(); r.specialProducts = new List<SpecialProductType> { default(SpecialProductType) };
        Check("side products rejected", !Supported(r, crafting, catalyst));
        r = make(); r.workSkill = new SkillDef { defName = "Artistic" };
        Check("art skill rejected", !Supported(r, crafting, catalyst));
        r = make(); r.workSkill = new SkillDef { defName = "Construction" };
        Check("construction skill rejected", !Supported(r, crafting, catalyst));
        r = make(); r.allowMixingIngredients = true;
        Check("mixed ingredients rejected", !Supported(r, crafting, catalyst));
        r = make(); r.ignoreIngredientCountTakeEntireStacks = true;
        Check("whole-stack recipes rejected", !Supported(r, crafting, catalyst));
        r = make(); r.ingredients.Add(Ingredient(steel, 5));
        Check("overlapping ingredient slots rejected", !Supported(r, crafting, catalyst));
        r = make(); r.ingredients.Add(Ingredient(catalyst, 1));
        Check("catalyst cannot also be a recipe ingredient", !Supported(r, crafting, catalyst));
        r = make(); r.efficiencyStat = new StatDef();
        Check("variable output quantity rejected", !Supported(r, crafting, catalyst));
        product.destroyOnDrop = true;
        Check("destroy-on-drop outputs rejected", !Supported(make(), crafting, catalyst)); product.destroyOnDrop = false;
        product.comps.Clear();
        Check("no quality rejected", !Supported(make(), crafting, catalyst));
        product.comps.Add(new CompProperties { compClass = typeof(CompQuality) });
        product.category = ThingCategory.Building;
        Check("building rejected", !Supported(make(), crafting, catalyst)); product.category = ThingCategory.Item;
        product.tools.Clear();
        Check("non-equipment rejected", !Supported(make(), crafting, catalyst));
        product.apparel = new ApparelProperties(); product.thingClass = typeof(Apparel);
        Check("durable apparel accepted", Supported(make(), crafting, catalyst));
        product.comps.Add(new CompProperties { compClass = typeof(CompApparelReloadable) });
        Check("charge-based apparel rejected conservatively", !Supported(make(), crafting, catalyst)); product.comps.RemoveAt(1);

        StuffCategoryDef metal = new StuffCategoryDef { defName = "TestMetal" };
        steel.stuffProps = new StuffProperties { categories = new List<StuffCategoryDef> { metal } };
        product.stuffCategories = new List<StuffCategoryDef> { metal };
        r = make(); r.productHasIngredientStuff = true;
        Check("material-first stuff recipe accepted", Supported(r, crafting, catalyst));
        r.productHasIngredientStuff = false;
        Check("ambiguous stuff recipe rejected", !Supported(r, crafting, catalyst));

        // Exercise the actual private 1.6 quantity selector used by the feature, with real
        // unspawned Thing instances. This tests counts/filters; map search/hauling remain in-game.
        r = make(); r.productHasIngredientStuff = true;
        Bill_Production bill = (Bill_Production)FormatterServices.GetUninitializedObject(typeof(Bill_Production));
        bill.recipe = r; bill.ingredientFilter = new ThingFilter(); bill.ingredientFilter.SetAllow(steel, true);
        var pileA = new ThingWithComps { def = steel, stackCount = 6 };
        var pileB = new ThingWithComps { def = steel, stackCount = 7 };
        var chosen = new List<ThingCount>();
        MethodInfo select = typeof(WorkGiver_DoBill).GetMethod("TryFindBestBillIngredientsInSet_NoMix", Any);
        Func<List<Thing>, bool> selectFrom = available => (bool)select.Invoke(null,
            new object[] { available, bill, chosen, new IntVec3(0, 0, 0), true, null });
        Check("native selector takes exactly 10 across split stacks", selectFrom(new List<Thing> { pileA, pileB }) && chosen.Sum(x => x.Count) == 10);
        Check("native selector does not mutate stacks", pileA.stackCount == 6 && pileB.stackCount == 7);
        pileB.stackCount = 3;
        Check("native selector rejects insufficient quantity", !selectFrom(new List<Thing> { pileA, pileB }));
        var wrong = new ThingWithComps { def = catalyst, stackCount = 100 };
        Check("native selector rejects wrong ingredient", !selectFrom(new List<Thing> { wrong }));
    }
    static void ApiAndIl(string dll)
    {
        Type wg = typeof(WorkGiver_DoBill);
        Type tc = typeof(List<ThingCount>), ic = typeof(List<IngredientCount>);
        Check("native ingredient finder exact signature", wg.GetMethod("TryFindBestBillIngredients", Any, null,
            new[] { typeof(Bill), typeof(Pawn), typeof(Thing), tc, ic }, null) != null);
        Check("native no-mix selector exact signature", wg.GetMethod("TryFindBestBillIngredientsInSet_NoMix", Any, null,
            new[] { typeof(List<Thing>), typeof(Bill), tc, typeof(IntVec3), typeof(bool), ic }, null) != null);
        using (AssemblyDefinition a = AssemblyDefinition.ReadAssembly(dll))
        {
            var types = a.MainModule.Types.Where(t => t.Namespace == "Grandmaster21.Transcendent").ToList();
            var project = types.Single(t => t.Name == "TranscendentProject");
            var save = project.Methods.Single(m => m.Name == "ExposeData");
            Check("all persistent project fields scribed", project.Fields.Where(f => !f.IsStatic)
                .All(f => save.Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == f.Name)));
            var calls = save.Body.Instructions.Select(i => i.Operand as MethodReference).Where(m => m != null).ToList();
            Check("project definitions use Scribe_Defs", calls.Count(m => m.DeclaringType.Name == "Scribe_Defs") == 3);
            Check("ledger uses deep collection serializer", calls.Any(m => m.DeclaringType.Name == "Scribe_Collections"));
            var bench = types.Single(t => t.Name == "Building_MagicalWorkstation");
            int randomSites = types.SelectMany(t => t.Methods).Where(m => m.HasBody)
                .SelectMany(m => m.Body.Instructions).Count(i => i.Operand is MethodReference
                    && ((MethodReference)i.Operand).DeclaringType.FullName == "Verse.Rand");
            Check("one RNG site in feature", randomSites == 1);
            var commit = bench.Methods.Single(m => m.Name == "TryCommit");
            Check("RNG site is commitment", commit.Body.Instructions.Any(i => i.Operand is MethodReference
                && ((MethodReference)i.Operand).FullName.Contains("Verse.Rand::get_Int")));
            Check("no new Harmony patches", types.All(t => !t.CustomAttributes.Any(x => x.AttributeType.Namespace == "HarmonyLib")));
            Check("building project deep-scribed", bench.Methods.Single(m => m.Name == "ExposeData").Body.Instructions.Any(i =>
                i.Operand is GenericInstanceMethod && ((GenericInstanceMethod)i.Operand).GenericArguments.Any(x => x.Name == "TranscendentProject")));
            Check("artifact component serialization exists", types.Single(t => t.Name == "CompArtifact").Methods.Any(m => m.Name == "PostExposeData"));
        }
    }
    static void XmlSchema(string root)
    {
        foreach (string path in Directory.GetFiles(root, "*.xml", SearchOption.AllDirectories)) XDocument.Load(path);
        Check("all repository XML well formed", true);
        XDocument defs = XDocument.Load(Path.Combine(root, "Defs/Transcendent/MagicalCrafting.xml"));
        foreach (XElement el in defs.Root.Elements())
        {
            Type type = typeof(ThingDef).Assembly.GetType("Verse." + el.Name.LocalName) ?? typeof(ThingDef).Assembly.GetType("RimWorld." + el.Name.LocalName);
            bool valid = type != null;
            foreach (XElement child in el.Elements())
            {
                FieldInfo field = type == null ? null : type.GetField(child.Name.LocalName, Any);
                if (field == null) { valid = false; Console.WriteLine("Unknown field: " + el.Name + "." + child.Name); continue; }
                if (field.FieldType.IsEnum) Enum.Parse(field.FieldType, child.Value);
                if (field.FieldType == typeof(Type))
                    valid &= (Mod.GetType(child.Value) ?? typeof(ThingDef).Assembly.GetType(child.Value) ?? typeof(ThingDef).Assembly.GetType("Verse." + child.Value)) != null;
            }
            Check("real Def schema: " + el.Element("defName").Value, valid);
        }
        var thingDefs = defs.Root.Elements("ThingDef").ToList();
        var catalyst = thingDefs.Single(x => x.Element("defName").Value == "GM21_MagicalCatalyst");
        Check("catalyst is not Stuff or trader stock", catalyst.Element("stuffProps") == null && catalyst.Element("tradeability").Value == "None");
        var bench = thingDefs.Single(x => x.Element("defName").Value == "GM21_MagicalWorkstation");
        Check("bench cannot be minified", bench.Element("minifiedDef") == null);
        Check("blueprint has graphic data before generation", bench.Element("graphicData") != null);
        Check("no ordinary bench/recipe XML patches", !Directory.Exists(Path.Combine(root, "Patches")));
    }
    static void SaveWriting(string path)
    {
        RecipeDef r = new RecipeDef { defName = "SaveRecipe" };
        ThingDef p = Def("SaveProduct"), s = Def("SaveSteel");
        TranscendentProject state = new TranscendentProject { id = "bench:1", recipe = r, product = p, stuff = s,
            totalWork = 45000, completedWork = 12345.25, seed = 42, initiatorId = "Pawn_1", initiatorName = "Crafter" };
        FieldInfo tier = typeof(TranscendentProject).GetField("finalTier", Any);
        tier.SetValue(state, Enum.Parse(tier.FieldType, "Magical"));
        state.ingredients.Add(new CommittedIngredient(s, 100));
        Scribe.saver.InitSaving(path, "test"); Scribe_Deep.Look(ref state, "project"); Scribe.saver.FinalizeSaving();
        XElement saved = XDocument.Load(path).Root.Element("project");
        Check("real Scribe writes recipe/material", saved.Element("recipe").Value == r.defName && saved.Element("stuff").Value == s.defName);
        Check("real Scribe writes partial progress and locked tier", saved.Element("completedWork").Value == "12345.25" && saved.Element("finalTier").Value == "Magical");
        Check("real Scribe writes ingredient receipt", saved.Element("ingredients").Element("li").Element("count").Value == "100");

        CompArtifact artifact = new CompArtifact();
        FieldInfo artifactTier = typeof(CompArtifact).GetField("tier", Any);
        artifactTier.SetValue(artifact, Enum.Parse(artifactTier.FieldType, "Magical"));
        typeof(CompArtifact).GetField("projectId", Any).SetValue(artifact, state.id);
        Scribe.saver.InitSaving(path, "artifact"); artifact.PostExposeData(); Scribe.saver.FinalizeSaving();
        saved = XDocument.Load(path).Root;
        Check("artifact metadata serialized separately", saved.Element("gm21ArtifactTier").Value == "Magical"
            && saved.Element("gm21ProjectId").Value == "bench:1");
        artifactTier.SetValue(artifact, Enum.Parse(artifactTier.FieldType, "None"));
        Scribe.saver.InitSaving(path, "artifact"); artifact.PostExposeData(); Scribe.saver.FinalizeSaving();
        saved = XDocument.Load(path).Root;
        Check("ordinary fallback keeps origin but no Magical tier", saved.Element("gm21ArtifactTier") == null && saved.Element("gm21ProjectId") != null);
        Console.WriteLine("NOT RUN: full reload and map gameplay require Unity player + game data. Save-writing alone is not a reload pass.");
    }
    static int Main(string[] args)
    {
        try
        {
            MathAndAuthorization(); RecipePolicy(); ApiAndIl(args[0]); XmlSchema(args[1]); SaveWriting(args[2]);
        }
        catch (Exception e) { Console.WriteLine(e); fail++; }
        Console.WriteLine("PASS: " + pass + " FAIL: " + fail);
        return fail == 0 ? 0 : 1;
    }
}
