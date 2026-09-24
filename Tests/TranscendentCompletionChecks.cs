using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml.Linq;
using Grandmaster21.Transcendent;
using Mono.Cecil;
using Mono.Cecil.Cil;
using RimWorld;
using Verse;

internal static partial class TranscendentChecks
{
    static Type Feature(string name) { return Mod.GetType("Grandmaster21.Transcendent." + name); }
    static object Tier(string name) { return Enum.Parse(Feature("ArtifactTier"), name); }
    static object Phenomenon(string name) { return Enum.Parse(Feature("ArtifactPhenomenon"), name); }
    static object Invoke(string type, string method, params object[] args) { return Feature(type).GetMethod(method, Any).Invoke(null, args); }
    static void Set(object obj, string field, object value) { obj.GetType().GetField(field, Any).SetValue(obj, value); }
    static object Get(object obj, string field) { return obj.GetType().GetField(field, Any).GetValue(obj); }
    static void TierChecks()
    {
        foreach (string ceiling in new[] { "Magical", "Mythical", "Divine" })
        {
            int[] counts = new int[6];
            for (int top = 0; top < 10; top++) for (int mid = 0; mid < 10; mid++) for (int low = 0; low < 10; low++)
                counts[Convert.ToInt32(Invoke("ArtifactRolls", "Resolve", Tier(ceiling), 1d, 1d, top / 10d, mid / 10d, low / 10d))]++;
            int[] expected = ceiling == "Magical" ? new[] { 500, 500, 0, 0, 0, 0 } : ceiling == "Mythical" ? new[] { 180, 420, 400, 0, 0, 0 } : new[] { 56, 224, 420, 300, 0, 0 };
            Check(ceiling + " exact fallback grid", counts.SequenceEqual(expected));
        }
        Check("first secret wins even when every roll succeeds", Invoke("ArtifactRolls", "Resolve", Tier("Divine"), 0d, 0d, 0d, 0d, 0d).ToString() == "Null");
        Check("second secret precedes normal Divine success", Invoke("ArtifactRolls", "Resolve", Tier("Divine"), 1d, 0d, 0d, 0d, 0d).ToString() == "Anomaly");
        Check("secret exact upper boundaries fail", Invoke("ArtifactRolls", "Resolve", Tier("Divine"), .0001, .001, 1d, 1d, 1d).ToString() == "None");
        foreach (string ceiling in new[] { "Magical", "Mythical" })
            Check(ceiling + " ignores successful secret rolls", Convert.ToInt32(Invoke("ArtifactRolls", "Resolve", Tier(ceiling), 0d, 0d, 0d, 0d, 0d)) <= Convert.ToInt32(Tier(ceiling)));
        int[] distribution = new int[6];
        MethodInfo roll = Feature("ArtifactRolls").GetMethod("Roll", Any);
        for (int seed = 0; seed < 200000; seed++) distribution[Convert.ToInt32(roll.Invoke(null, new[] { Tier("Divine"), (object)seed }))]++;
        double known = (1 - .0001) * (1 - .001);
        double[] probabilities = { known * .056, known * .224, known * .42, known * .3, (1 - .0001) * .001, .0001 };
        for (int i = 0; i < 6; i++) Check("seeded Divine distribution bucket " + i, Math.Abs(distribution[i] / 200000d - probabilities[i]) < (i >= 4 ? .0003 : .006));
        foreach (int seed in new[] { int.MinValue, -100, 0, 100, int.MaxValue })
        {
            object a = Invoke("ArtifactRolls", "Roll", Tier("Divine"), seed), b = Invoke("ArtifactRolls", "Roll", Tier("Divine"), seed);
            Check("stable roll seed " + seed, a.Equals(b));
        }
        foreach (var row in new[] { new { Name = "Magical", Days = 1.5, Mult = 3d }, new { Name = "Mythical", Days = 4d, Mult = 7d }, new { Name = "Divine", Days = 9d, Mult = 15d } })
        {
            object config = Invoke("TranscendentTierConfig", "For", Tier(row.Name));
            var work = config.GetType().GetMethod("Work", Any);
            Check(row.Name + " working-day minimum", (double)work.Invoke(config, new object[] { 1f }) == row.Days * 12 * 2500);
            Check(row.Name + " expensive recipe multiplier", (double)work.Invoke(config, new object[] { 100000f }) == row.Mult * 100000);
            Check(row.Name + " can force own result", (bool)Invoke("Building_MagicalWorkstation", "CanForce", Tier(row.Name), Tier(row.Name)));
            Check(row.Name + " secret forcing ceiling", (bool)Invoke("Building_MagicalWorkstation", "CanForce", Tier(row.Name), Tier("Null")) == (row.Name == "Divine"));
        }
        Check("invalid project ceiling has no configuration", Invoke("TranscendentTierConfig", "For", Tier("Null")) == null);
    }
    static void PhenomenonChecks()
    {
        ThingDef weapon = Def("Weapon"), apparel = Def("Coat"); weapon.tools = new List<Tool> { new Tool() }; apparel.apparel = new ApparelProperties();
        foreach (object p in Enum.GetValues(Feature("ArtifactPhenomenon")))
        {
            Check("weapon accepts " + p, (bool)Invoke("ArtifactIdentity", "Eligible", weapon, p));
            Check("apparel offensive eligibility " + p, (bool)Invoke("ArtifactIdentity", "Eligible", apparel, p) == (p.ToString() == "None"));
            Check("ordinary fallback eligibility " + p, (bool)Invoke("ArtifactIdentity", "Valid", Tier("None"), p, weapon) == (p.ToString() == "None"));
        }
        HashSet<string> seen = new HashSet<string>();
        for (int seed = 0; seed < 100; seed++)
        {
            seen.Add(Invoke("ArtifactIdentity", "Assign", weapon, Tier("Divine"), seed).ToString());
            if (Invoke("ArtifactIdentity", "Assign", apparel, Tier("Divine"), seed).ToString() != "None") throw new Exception("Apparel assigned offensive phenomenon");
        }
        Check("all seven weapon phenomena reachable", seen.Count == 7 && !seen.Contains("None"));
        Check("ordinary fallback has no phenomenon", Invoke("ArtifactIdentity", "Assign", weapon, Tier("None"), 99).ToString() == "None");
        Check("Divine reliability limited to 30 percent", (double)Invoke("ArtifactRolls", "Reliability", Tier("Divine")) == .3);
        Check("reliability increases with known tier", (double)Invoke("ArtifactRolls", "Reliability", Tier("Magical")) < (double)Invoke("ArtifactRolls", "Reliability", Tier("Mythical")));
        foreach (float damage in new[] { -1f, 0f, float.NaN, float.PositiveInfinity }) Check("invalid healing " + damage, (float)Invoke("ArtifactEffects", "HealingBudget", damage) == 0);
        Check("healing bounded for huge damage", (float)Invoke("ArtifactEffects", "HealingBudget", float.MaxValue) == 8f);
        Check("healing depends on actual damage", Math.Abs((float)Invoke("ArtifactEffects", "HealingBudget", 10f) - 3.5f) < .001);
        Check("absent wielder cannot trigger", !(bool)Invoke("ArtifactEffects", "SafeTarget", null, null));
        FieldInfo depth = Feature("ArtifactEffects").GetField("effectDepth", Any);
        depth.SetValue(null, 1);
        try { Check("recursive dispatch short circuits before engine/target access", !(bool)Invoke("ArtifactEffects", "TryTrigger", null, null, null, false)); }
        finally { depth.SetValue(null, 0); }
        Check("guard thread local", depth.IsDefined(typeof(ThreadStaticAttribute), false));
    }
    static void ProgressionChecks(string root)
    {
        var defs = Directory.GetFiles(Path.Combine(root, "Defs/Transcendent"), "*.xml").SelectMany(f => XDocument.Load(f).Root.Elements()).ToList();
        Check("unique typed DefNames", defs.GroupBy(x => x.Name + ":" + x.Element("defName").Value).All(g => g.Count() == 1));
        foreach (string tier in new[] { "Magical", "Mythical", "Divine" })
        {
            XElement bench = defs.Single(x => x.Element("defName").Value == "GM21_" + tier + "Workstation");
            Check(tier + " uses shared saved CLR type", bench.Element("thingClass").Value == typeof(Building_MagicalWorkstation).FullName);
            Check(tier + " construction gated by research", bench.Element("researchPrerequisites").Element("li").Value == "GM21_" + tier + "Craftsmanship");
            Check(tier + " no minify/bills tab", bench.Element("minifiedDef") == null && bench.Element("inspectorTabs") == null);
            object config = Invoke("TranscendentTierConfig", "For", Tier(tier));
            string catalyst = (string)Get(config, "catalystName");
            XElement c = defs.Single(x => x.Element("defName").Value == catalyst);
            Check(tier + " catalyst has no Stuff/trader tags", c.Element("stuffProps") == null && c.Element("tradeTags") == null && c.Element("tradeability").Value == "None");
            XElement r = defs.Single(x => x.Name == "RecipeDef" && x.Element("products").Elements().Single().Name == catalyst);
            Check(tier + " manufacture research and Core bench", r.Element("researchPrerequisite").Value == "GM21_" + tier + "Craftsmanship" && r.Element("recipeUsers").Element("li").Value == "FabricationBench");
            Check(tier + " catalyst manufacture below GM requirement", int.Parse(r.Element("skillRequirements").Element("Crafting").Value) < 21);
            Check(tier + " all quantities positive", r.Element("ingredients").Elements().All(i => int.Parse(i.Element("count").Value) > 0));
        }
        Check("research exact chain", defs.Where(x => x.Name == "ResearchProjectDef").Select(x => x.Element("prerequisites").Element("li").Value).OrderBy(x => x)
            .SequenceEqual(new[] { "Fabrication", "GM21_MagicalCraftsmanship", "GM21_MythicalCraftsmanship" }.OrderBy(x => x)));
        Check("catalysts get isolated stockpile category", defs.Single(x => x.Name == "ThingCategoryDef").Element("parent").Value == "Root"
            && defs.Where(x => x.Name == "ThingDef" && x.Element("category").Value == "Item").All(x => x.Element("thingCategories").Element("li").Value == "GM21_Catalysts"));
        HashSet<string> names = new HashSet<string>(defs.Select(x => x.Element("defName").Value));
        Check("all local GM21 XML references resolve", defs.SelectMany(x => x.Descendants()).Where(x => !x.HasElements && x.Name != "defName" && x.Value.StartsWith("GM21_")).All(x => names.Contains(x.Value)));
        var translations = XDocument.Load(Path.Combine(root, "Languages/English/Keyed/Transcendent.xml")).Root.Elements().Select(x => x.Name.LocalName).ToHashSetCompat();
        foreach (string p in Enum.GetNames(Feature("ArtifactPhenomenon")).Where(x => x != "None")) Check("phenomenon label translation " + p, translations.Contains("GM21_TC_Phenomenon_" + p));
        Check("no hidden research or workstation definitions", defs.All(x => !new[] { "Anomaly", "Null", "Watcher" }.Any(s => x.Element("defName").Value.Contains(s))));
        foreach (XElement d in defs.Where(x => x.Name == "DamageDef"))
            Check(d.Element("defName").Value + " no explosion/ignition", d.Element("workerClass").Value == "DamageWorker_AddInjury" && !d.Elements().Any(x => x.Name.LocalName.StartsWith("ignite") || x.Name.LocalName.StartsWith("explosion")));
        XElement frost = defs.Single(x => x.Name == "HediffDef");
        Check("frost expires and has bounded slowdown", frost.Descendants("disappearsAfterTicks").Single().Value == "300" && frost.Descendants("MoveSpeed").Single().Value == "0.6");
        string[] playerFiles = { "README.md", "About/About.xml", "Languages/English/Keyed/Transcendent.xml" };
        foreach (string file in playerFiles)
        {
            string text = File.ReadAllText(Path.Combine(root, file));
            // Existing README also discusses the vanilla DLC by name; only crafting prose is in scope.
            if (file == "README.md") text = text.Substring(text.IndexOf("## Crafting 21"));
            Check("public secret isolation " + file, !new[] { "Anomaly", "Anomalous", "Null", "Watcher", "0.01%", "0.10%" }.Any(s => text.Contains(s)));
        }
    }
    static IEnumerable<MethodReference> Calls(MethodDefinition m) { return m.Body.Instructions.Select(i => i.Operand as MethodReference).Where(x => x != null); }
    static void CombatContracts(string dll)
    {
        Check("bullet equipment reference is native and persisted", typeof(Projectile).GetField("equipment", Any).FieldType == typeof(Thing));
        Check("audited Bullet Impact signature", typeof(Bullet).GetMethod("Impact", Any, null, new[] { typeof(Thing), typeof(bool) }, null) != null);
        Check("audited melee signature", typeof(Verb_MeleeAttackDamage).GetMethod("ApplyMeleeDamageToTarget", Any, null, new[] { typeof(LocalTargetInfo) }, null) != null);
        Check("audited post-damage signature", typeof(Pawn).GetMethod("PostApplyDamage", Any, null, new[] { typeof(DamageInfo), typeof(float) }, null) != null);
        Check("audited pawn map-entry signature", typeof(Pawn).GetMethod("SpawnSetup", Any, null, new[] { typeof(Map), typeof(bool) }, null) != null);
        Check("audited equipment gizmo hook", typeof(Pawn_EquipmentTracker).GetMethod("GetGizmos", Any).ReturnType == typeof(IEnumerable<Gizmo>));
        using (var a = AssemblyDefinition.ReadAssembly(dll))
        {
            Func<string, TypeDefinition> type = n => a.MainModule.Types.Single(t => t.Name == n);
            Func<string, string, MethodDefinition> method = (t, n) => type(t).Methods.Single(m => m.Name == n);
            var effect = method("ArtifactEffects", "TryTriggerCaptured");
            Check("proc guard restored in finally", effect.Body.ExceptionHandlers.Any(h => h.HandlerType == ExceptionHandlerType.Finally));
            Check("generated damage has no weapon provenance", Calls(method("ArtifactEffects", "Damage")).Any(c => c.Name == ".ctor" && c.DeclaringType.Name == "DamageInfo"));
            Check("all effect damage targets Pawn only", method("ArtifactEffects", "Damage").Parameters[1].ParameterType.FullName == "Verse.Pawn");
            Check("effects do not call explosions/fire/terrain/destroy", type("ArtifactEffects").Methods.Where(m => m.HasBody).SelectMany(Calls).All(c => !c.DeclaringType.Name.Contains("Explosion") && !c.DeclaringType.Name.Contains("FireUtility") && c.Name != "Destroy" && !c.DeclaringType.Name.Contains("TerrainGrid")));
            Check("target check requires hostility", Calls(method("ArtifactEffects", "SafeTarget")).Any(c => c.Name == "HostileTo"));
            Check("target check excludes downed and prisoners", new[] { "get_Downed", "get_IsPrisonerOfColony" }.All(n => Calls(method("ArtifactEffects", "SafeTarget")).Any(c => c.Name == n)));
            Check("nearby targets use radial cells not global pawn collection", Calls(method("ArtifactEffects", "Nearby")).Any(c => c.Name == "RadialCellsAround") && !Calls(method("ArtifactEffects", "Nearby")).Any(c => c.Name.Contains("AllPawns")));
            Check("chain has visited-target set", Calls(method("ArtifactEffects", "Apply")).Any(c => c.Name == "Add" && c.DeclaringType.Name.StartsWith("HashSet")));
            Check("chain target bound four", (int)type("ArtifactEffects").Fields.Single(f => f.Name == "ChainTargets").Constant == 4);
            Check("area target cap sixteen", (int)type("ArtifactEffects").Fields.Single(f => f.Name == "MaxTargets").Constant == 16);
            Check("watcher per-map budget eight", (int)type("ArtifactWatcher").Fields.Single(f => f.Name == "Budget").Constant == 8);
            Check("watcher never creates pawn or Thing", type("ArtifactWatcher").Methods.Where(m => m.HasBody).SelectMany(Calls).All(c => c.Name != "GeneratePawn" && c.Name != "MakeThing"));
            Check("work/completion never roll", new[] { "Work", "TryComplete" }.All(n => Calls(method("Building_MagicalWorkstation", n)).All(c => c.DeclaringType.Name != "ArtifactRolls" && c.DeclaringType.Name != "Rand")));
            Check("completion locks vanilla Legendary", method("Building_MagicalWorkstation", "TryComplete").Body.Instructions.Any(i => i.OpCode == OpCodes.Ldc_I4_6));
            Check("commit/start guard one active project", method("Building_MagicalWorkstation", "TryCommit").Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "project"));
            Check("active projects prevent deconstruction", Calls(method("Building_MagicalWorkstation", "DeconstructibleBy")).Any(c => c.Name == "get_Count"));
            Check("destroy purges retained contents", Calls(method("Building_MagicalWorkstation", "Destroy")).Any(c => c.Name == "ClearAndDestroyContents"));
            Check("DEV override checks DevMode", Calls(method("Building_MagicalWorkstation", "ApplyDeveloperOverride")).Any(c => c.Name == "get_DevMode"));
            Check("equipment developer controls gated at entry", Calls(method("ArtifactCombat", "EquipmentGizmos")).Any(c => c.Name == "get_DevMode"));
            Check("project output copies committed phenomenon without assignment", Calls(method("Building_MagicalWorkstation", "TryComplete")).All(c => c.DeclaringType.Name != "ArtifactIdentity"));
            Check("artifact proc state all scribed", new[] { "phenomenon", "phenomenonSeed", "procCounter", "nextProcTick", "nextWatcherTick" }.All(n => method("CompArtifact", "PostExposeData").Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == n)));
        }
    }
    static void NewPersistence(string path)
    {
        ThingDef weapon = Def("SavedWeapon"); weapon.tools = new List<Tool> { new Tool() };
        var parent = (ThingWithComps)FormatterServices.GetUninitializedObject(typeof(ThingWithComps)); parent.def = weapon;
        CompArtifact item = new CompArtifact { parent = parent };
        Set(item, "tier", Tier("Divine")); Set(item, "phenomenon", Phenomenon("Smite")); Set(item, "phenomenonSeed", 123);
        Set(item, "procCounter", 7); Set(item, "nextProcTick", 999); Set(item, "nextWatcherTick", 87654);
        Scribe.saver.InitSaving(path, "artifact"); item.PostExposeData(); Scribe.saver.FinalizeSaving();
        XElement written = XDocument.Load(path).Root;
        Check("Scribe writes phenomenon and deterministic seed", written.Element("gm21Phenomenon").Value == "Smite" && written.Element("gm21PhenomenonSeed").Value == "123");
        Check("Scribe writes cooldown/counter and manifestation schedule", written.Element("gm21ProcCounter").Value == "7" && written.Element("gm21NextProcTick").Value == "999" && written.Element("gm21NextWatcherTick").Value == "87654");
        bool canLoad = true;
        try { System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(ParseHelper).TypeHandle); }
        catch (TypeInitializationException ex)
        {
            Exception root = ex; while (root.InnerException != null) root = root.InnerException;
            if (!(root is FileNotFoundException) || !root.Message.Contains("steamworks")) throw;
            canLoad = false; blocked++; Console.WriteLine("BLOCKED native Scribe readback: real ParseHelper initializer needs missing Steamworks.NET; no substitute parser installed.");
        }
        var loaded = new CompArtifact { parent = parent };
        if (canLoad)
        {
            Scribe.loader.InitLoading(path); loaded.PostExposeData(); Scribe.loader.FinalizeLoading();
            foreach (string field in new[] { "tier", "phenomenon", "phenomenonSeed", "procCounter", "nextProcTick", "nextWatcherTick" }) Check("real Scribe artifact value roundtrip " + field, Get(item, field).Equals(Get(loaded, field)));
        }
        var project = new TranscendentProject { id = "test", product = weapon, recipe = new RecipeDef(), totalWork = 270000, completedWork = 12456, seed = 11, phenomenonSeed = 123 };
        Set(project, "ceiling", Tier("Divine")); Set(project, "finalTier", Tier("Null")); Set(project, "phenomenon", Phenomenon("Smite"));
        Check("new committed project accepts secret identity", project.Valid);
        project.recipe.defName = "SavedRecipe";
        Scribe.saver.InitSaving(path, "test"); Scribe_Deep.Look(ref project, "project"); Scribe.saver.FinalizeSaving();
        XElement savedProject = XDocument.Load(path).Root.Element("project");
        Check("Scribe writes locked ceiling/final tier/phenomenon", savedProject.Element("ceiling").Value == "Divine" && savedProject.Element("finalTier").Value == "Null" && savedProject.Element("phenomenon").Value == "Smite");
        Check("Scribe writes schema two and locked phenomenon seed", savedProject.Element("schemaVersion").Value == "2" && savedProject.Element("phenomenonSeed").Value == "123");
        var busy = (Building_MagicalWorkstation)FormatterServices.GetUninitializedObject(typeof(Building_MagicalWorkstation));
        Set(busy, "project", project);
        Check("busy bench rejects second commit before touching inputs or RNG", !(bool)typeof(Building_MagicalWorkstation).GetMethod("TryCommit", Any).Invoke(busy, new object[] { null, null }));
        typeof(Building_MagicalWorkstation).GetMethod("Choose", Any).Invoke(busy, new object[] { null, null });
        Check("recipe selection cannot replace committed project", ReferenceEquals(Get(busy, "project"), project));
        Set(project, "ceiling", Tier("Magical")); Check("invalid lower-ceiling secret rejected", !project.Valid);
        Set(project, "finalTier", Tier("Magical")); Set(project, "phenomenon", Phenomenon("None")); project.schemaVersion = 1;
        Check("legacy Magical project remains valid without reroll", project.Valid);
        project.schemaVersion = 99; Check("future unknown schema faults", !project.Valid);
        // Primitive legacy load: omitted fields default, never assign a new phenomenon.
        File.WriteAllText(path, "<artifact><gm21ArtifactTier>Magical</gm21ArtifactTier><gm21ProjectId>old:1</gm21ProjectId></artifact>");
        loaded = new CompArtifact { parent = parent };
        if (canLoad)
        {
            Scribe.loader.InitLoading(path); loaded.PostExposeData(); Scribe.loader.FinalizeLoading();
            Check("legacy item load preserves tier and no retroactive phenomenon", Get(loaded, "tier").ToString() == "Magical" && Get(loaded, "phenomenon").ToString() == "None" && (string)Get(loaded, "projectId") == "old:1");
        }
    }
    static HashSet<T> ToHashSetCompat<T>(this IEnumerable<T> source) { return new HashSet<T>(source); }
}
