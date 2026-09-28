// Crafting Grandmaster Legendary letter toggle -- headless checks against the REAL RimWorld 1.6,
// Unity and Harmony assemblies.
//
// What makes these more than logic tests: the mod's real Patch_CraftingLegendaryNotification.Apply
// and its real quality/level patches are installed on the real vanilla methods, and then vanilla's
// own GenRecipe.PostProcessProduct (quality roll, SetQuality, art credit, tale, letter) and
// QualityUtility.SendCraftNotification are executed. Settings go through the real
// LoadedModManager.WriteModSettings / ReadModSettings. Section 1 re-derives the audit from the real
// Assembly-CSharp IL with Mono.Cecil, so a game update that moves the letter is caught here.
//
// Test-process-only environment shims (never shipped, never in the mod). None of them touch the
// decision under test; they stand in for a running game's text, maps and managers:
//   * LetterStack.ReceiveLetter (the 10-argument overload SendCraftNotification calls) RECORDS the
//     letter instead of queueing it -- that recording is the observable;
//   * Translator.Translate returns the key, the 4/5-argument Formatted returns its input, and
//     Thing/Pawn.LabelShort return fixed text -- so a recorded letter reads e.g.
//     "LetterCraftedLegendaryLabel|LetterCraftedLegendaryMessage";
//   * CompArt.InitializeArt / JustCreatedBy / GenerateImageDescription and TaleRecorder.RecordTale
//     COUNT their calls instead of generating art and tales (they need a live game);
//   * Pawn.InspirationDef answers from the fixture, InspirationHandler.EndInspiration counts, and
//     RaceProperties.IsMechanoid is false;
//   * GenerateQualityCreatedByPawn(Pawn,..)'s one ModsConfig.IdeologyActive read answers "off"
//     (ModsConfig cannot initialise headless; even patching its getter runs its constructor);
//   * LoadedModManager.GetSettingsFilename points at a scratch file, and GenTypes resolves
//     "Grandmaster21.Gm21Settings" to this mod's assembly, as it does once the mod is loaded.
//     (Vanilla BackCompatibility's one-time GenTypes.AllTypes scan then logs that NAudio -- shipped
//     beside Assembly-CSharp in a real install -- is absent; that line is environment, not a finding.)
//
// Stand-ins for OTHER mods (test fixtures, clearly named "OtherMod*"): a quality postfix that runs
// after GM21's and forces Legendary for one pawn (the only way a non-Grandmaster can reach
// Legendary with GM21 installed) or Masterwork for another, a Thing.PostQualitySet postfix that
// performs extra calls in the middle of a craft, and (section 7b only) prefixes and a postfix on
// SendCraftNotification at several priorities.
//
// NOT covered: a running map, the settings window itself, jobs, Frame.CompleteConstruction and the
// cube sculpture driver executing end to end (their IL is audited in section 1 and their call
// sequence emulated in section 6). See Docs/Crafting21LegendaryLetterTest.md.
//
// Construction coverage adds a temporary loader fixture omitting only Frame's three rendering
// asset loads; every other game method body is checked unchanged, and Audit reads the pristine DLL.
//
//   mono CraftingNotificationChecks.exe <Grandmaster21.dll> <Assembly-CSharp.dll> <scratch dir> <repo root>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using System.Threading;
using System.Xml.Linq;
using Grandmaster21;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
using RimWorld;
using Verse;
using OpCodes = System.Reflection.Emit.OpCodes;
using Code = Mono.Cecil.Cil.Code;

internal static partial class CraftingNotificationChecks
{
    const string Gm21Id = "ared.grandmaster21";
    static int pass, fail, nextId = 7000;
    static readonly BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    static void Check(string name, bool ok, string detail = "")
    {
        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name + (detail == "" ? "" : "   [" + detail + "]"));
        if (ok) pass++; else fail++;
    }

    static T Uninit<T>() { return (T)FormatterServices.GetUninitializedObject(typeof(T)); }
    static void Set(object o, string field, object value) { AccessTools.Field(o.GetType(), field).SetValue(o, value); }
    static HarmonyMethod Stub(string n) { return new HarmonyMethod(AccessTools.Method(typeof(CraftingNotificationChecks), n)); }

    // ------------------------------------------------------------------ environment

    static readonly List<string> letters = new List<string>();
    static readonly List<string> logged = new List<string>();
    static readonly HashSet<Pawn> inspired = new HashSet<Pawn>();
    static int tales, artCredits, artInits, inspirationsEnded, ideologyShimmed;
    static string settingsPath;

    public static bool RecordLetter(TaggedString __0, TaggedString __1) { letters.Add(__0.RawText + "|" + __1.RawText); return false; }
    public static bool TranslateKey(string key, ref TaggedString __result) { __result = key; return false; }
    public static bool FormattedAsIs(TaggedString __0, ref TaggedString __result) { __result = __0; return false; }
    public static bool FixedLabel(ref string __result) { __result = "label"; return false; }
    public static bool NotMechanoid(ref bool __result) { __result = false; return false; }
    public static bool FixtureInspiration(Pawn __instance, ref InspirationDef __result)
    { __result = inspired.Contains(__instance) ? InspirationDefOf.Inspired_Creativity : null; return false; }
    public static bool CountInspirationEnded() { inspirationsEnded++; return false; }
    public static bool CountTale(ref Tale __result) { tales++; __result = null; return false; }
    public static bool CountArtCredit() { artCredits++; return false; }
    public static bool CountArtInit() { artInits++; return false; }
    public static bool ArtDescription(ref TaggedString __result) { __result = "art"; return false; }
    public static bool ScratchSettingsFile(ref string __result) { __result = settingsPath; return false; }
    public static void LoadedModType(string typeName, ref Type __result)
    { if (__result == null && typeName == typeof(Gm21Settings).FullName) __result = typeof(Gm21Settings); }
    public static bool ConsoleLog(string text)
    {
        logged.Add(text);
        Console.WriteLine("        [game log] " + text);
        return false;
    }
    public static bool IdeologyInactive() { return false; }
    public static IEnumerable<CodeInstruction> ShimIdeologyActive(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo getter = AccessTools.PropertyGetter(typeof(ModsConfig), "IdeologyActive");
        ideologyShimmed = 0; // Harmony re-runs transpilers whenever the method is patched again
        foreach (CodeInstruction i in instructions)
        {
            if (Equals(i.operand, getter))
            {
                i.operand = AccessTools.Method(typeof(CraftingNotificationChecks), "IdeologyInactive");
                ideologyShimmed++;
            }
            yield return i;
        }
    }

    // ---- stand-ins for other mods
    static Pawn otherModLegendaryFor, otherModMasterworkFor;
    public static void OtherModForcesQuality(Pawn pawn, ref QualityCategory __result)
    {
        if (pawn == null) return;
        if (pawn == otherModLegendaryFor) __result = QualityCategory.Legendary;
        if (pawn == otherModMasterworkFor) __result = QualityCategory.Masterwork;
    }
    static Action otherModDuringCraft;
    public static void OtherModDuringCraft()
    {
        Action a = otherModDuringCraft;
        otherModDuringCraft = null; // once: the hook can itself craft
        if (a != null) a();
    }

    static void InstallEnvironment()
    {
        Harmony h = new Harmony("gm21.crafting-notification.test-environment");
        foreach (string level in new[] { "Message", "Warning", "Error" })
            h.Patch(AccessTools.Method(typeof(Log), level, new[] { typeof(string) }), Stub("ConsoleLog"));
        h.Patch(ReceiveLetter10, Stub("RecordLetter"));
        h.Patch(AccessTools.Method(typeof(Translator), "Translate", new[] { typeof(string) }), Stub("TranslateKey"));
        foreach (MethodInfo f in typeof(GrammarResolverSimpleStringExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            ParameterInfo[] p = f.GetParameters();
            if (f.Name == "Formatted" && p.Length >= 5 && p[0].ParameterType == typeof(TaggedString)
                && p.Skip(1).All(x => x.ParameterType == typeof(NamedArgument)))
                h.Patch(f, Stub("FormattedAsIs"));
        }
        h.Patch(AccessTools.DeclaredPropertyGetter(typeof(Thing), "LabelShort"), Stub("FixedLabel"));
        h.Patch(AccessTools.DeclaredPropertyGetter(typeof(Pawn), "LabelShort"), Stub("FixedLabel"));
        h.Patch(AccessTools.PropertyGetter(typeof(RaceProperties), "IsMechanoid"), Stub("NotMechanoid"));
        h.Patch(AccessTools.PropertyGetter(typeof(Pawn), "InspirationDef"), Stub("FixtureInspiration"));
        h.Patch(AccessTools.Method(typeof(InspirationHandler), "EndInspiration", new[] { typeof(InspirationDef) }), Stub("CountInspirationEnded"));
        h.Patch(AccessTools.Method(typeof(TaleRecorder), "RecordTale"), Stub("CountTale"));
        h.Patch(AccessTools.Method(typeof(CompArt), "JustCreatedBy"), Stub("CountArtCredit"));
        h.Patch(AccessTools.Method(typeof(CompArt), "InitializeArt", new[] { typeof(ArtGenerationContext) }), Stub("CountArtInit"));
        h.Patch(AccessTools.Method(typeof(CompArt), "GenerateImageDescription"), Stub("ArtDescription"));
        h.Patch(QualityByPawn, null, null, Stub("ShimIdeologyActive"));
        h.Patch(AccessTools.Method(typeof(LoadedModManager), "GetSettingsFilename"), Stub("ScratchSettingsFile"));
        h.Patch(AccessTools.Method(typeof(GenTypes), "GetTypeInAnyAssemblyRaw"), null, Stub("LoadedModType"));
        // Other mods: ordered after GM21's quality postfix, exactly like a later-loading mod's patch.
        HarmonyMethod legendary = Stub("OtherModForcesQuality");
        legendary.priority = Priority.Last;
        legendary.after = new[] { Gm21Id };
        h.Patch(QualityByPawn, null, legendary);
        h.Patch(AccessTools.Method(typeof(Thing), "PostQualitySet"), null, Stub("OtherModDuringCraft"));

        // Bind the DefOf classes the path reads, under the same guard the game's loader uses.
        FieldInfo binding = typeof(DefOfHelper).GetField("bindingNow", Any);
        binding.SetValue(null, true);
        try
        {
            foreach (Type t in new[] { typeof(SkillDefOf), typeof(LetterDefOf), typeof(InspirationDefOf), typeof(TaleDefOf) })
                System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(t.TypeHandle);
        }
        finally { binding.SetValue(null, false); }
        SkillDefOf.Crafting = crafting = new SkillDef { defName = "Crafting", label = "crafting" };
        SkillDefOf.Artistic = artistic = new SkillDef { defName = "Artistic", label = "artistic" };
        SkillDefOf.Construction = construction = new SkillDef { defName = "Construction", label = "construction" };
        LetterDefOf.PositiveEvent = Named<LetterDef>("PositiveEvent");
        LetterDefOf.ThreatBig = Named<LetterDef>("ThreatBig");
        InspirationDefOf.Inspired_Creativity = Named<InspirationDef>("Inspired_Creativity");
        TaleDefOf.CraftedArt = Named<TaleDef>("CraftedArt");

        Game game = Uninit<Game>();
        Set(game, "letterStack", Uninit<LetterStack>());
        Current.Game = game;
    }

    static T Named<T>(string defName) where T : Def { T d = Uninit<T>(); d.defName = defName; return d; }

    // ------------------------------------------------------------------ fixtures

    static SkillDef crafting, artistic, construction;
    static readonly MethodInfo PostProcess = AccessTools.Method(typeof(GenRecipe), "PostProcessProduct");
    static readonly MethodInfo Notify = AccessTools.Method(typeof(QualityUtility), "SendCraftNotification", new[] { typeof(Thing), typeof(Pawn) });
    static readonly MethodInfo QualityByPawn = AccessTools.Method(typeof(QualityUtility), "GenerateQualityCreatedByPawn",
        new[] { typeof(Pawn), typeof(SkillDef), typeof(bool) });
    static readonly MethodInfo ReceiveLetter10 = typeof(LetterStack).GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .Single(m => m.Name == "ReceiveLetter" && m.GetParameters().Length == 10 && m.GetParameters()[0].ParameterType == typeof(TaggedString));

    static void AddSkill(Pawn p, SkillDef def, int levelInt, int aptitude)
    {
        SkillRecord rec = new SkillRecord(p, def) { levelInt = levelInt };
        // The lazy caches GetLevel reads; computing them needs work tags, genes, traits and ModsConfig.
        Set(rec, "cachedTotallyDisabled", BoolUnknown.False);
        Set(rec, "cachedPermanentlyDisabled", BoolUnknown.False);
        Set(rec, "aptitudeCached", (int?)aptitude);
        p.skills.skills.Add(rec);
    }

    static Pawn MakePawn(string name, int craftingLevel, int craftingAptitude = 0, int artisticLevel = 5, int constructionLevel = 5)
    {
        Pawn p = Uninit<Pawn>();
        p.thingIDNumber = nextId++;
        p.def = Uninit<ThingDef>();
        p.def.race = new RaceProperties { intelligence = Intelligence.Humanlike };
        p.Name = new NameSingle(name);
        p.skills = Uninit<Pawn_SkillTracker>();
        p.skills.skills = new List<SkillRecord>();
        Set(p.skills, "pawn", p);
        Set(p, "mapIndexOrState", (sbyte)-1);
        p.mindState = Uninit<Verse.AI.Pawn_MindState>();
        p.mindState.inspirationHandler = Uninit<InspirationHandler>();
        AddSkill(p, crafting, craftingLevel, craftingAptitude);
        AddSkill(p, artistic, artisticLevel, 0);
        AddSkill(p, construction, constructionLevel, 0);
        return p;
    }

    static ThingWithComps Product(bool art = false)
    {
        ThingWithComps t = Uninit<ThingWithComps>();
        t.thingIDNumber = nextId++;
        t.def = Uninit<ThingDef>();                 // not minifiable, no random style
        CompQuality q = new CompQuality { parent = t };
        List<ThingComp> comps = new List<ThingComp> { q };
        if (art) comps.Add(new CompArt { parent = t, props = new CompProperties_Art() });
        Set(t, "comps", comps);
        t.compQuality = q;
        return t;
    }

    static RecipeDef Recipe(string defName, SkillDef skill)
    {
        RecipeDef r = Uninit<RecipeDef>();
        r.defName = defName;
        r.workSkill = skill;
        return r;
    }

    static bool Show { set { Gm21Mod.Settings.showCraftingGrandmasterLegendaryNotifications = value; } }

    /// <summary>Runs vanilla's real GenRecipe.PostProcessProduct and returns the letters it produced.</summary>
    static List<string> Craft(ThingWithComps product, RecipeDef recipe, Pawn worker)
    {
        letters.Clear();
        try { PostProcess.Invoke(null, new object[] { product, recipe, worker, null, null, null }); }
        catch (TargetInvocationException e) { throw e.InnerException; }
        return new List<string>(letters);
    }

    /// <summary>Calls vanilla's real SendCraftNotification directly, outside any crafting frame.</summary>
    static List<string> Announce(Thing thing, Pawn worker)
    {
        letters.Clear();
        QualityUtility.SendCraftNotification(thing, worker);
        return new List<string>(letters);
    }

    static QualityCategory QualityOf(ThingWithComps t) { return t.compQuality.Quality; }
    static string Show1(List<string> l) { return l.Count == 0 ? "no letter" : string.Join(", ", l.ToArray()); }

    const string Legendary = "LetterCraftedLegendaryLabel|LetterCraftedLegendaryMessage";
    const string LegendaryArt = "LetterCraftedLegendaryLabel|LetterCraftedLegendaryMessageArt";
    const string Masterwork = "LetterCraftedMasterworkLabel|LetterCraftedMasterworkMessage";

    static bool Only(List<string> l, string letter) { return l.Count == 1 && l[0] == letter; }

    // ------------------------------------------------------------------ 1. audit, from the real IL

    static void Audit(string acsPath, string modPath)
    {
        Console.WriteLine("\n=== 1. Audit: the vanilla crafted-Legendary letter path, re-derived from the real 1.6 IL ===");
        ModuleDefinition acs = ModuleDefinition.ReadModule(acsPath);
        ModuleDefinition mod = ModuleDefinition.ReadModule(modPath);
        List<MethodDefinition> all = acs.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody).ToList();

        List<string> keyUsers = all.Where(m => m.Body.Instructions.Any(i => i.OpCode.Code == Code.Ldstr
                && ((string)i.Operand).StartsWith("LetterCraftedLegendary", StringComparison.Ordinal)))
            .Select(m => m.DeclaringType.FullName + "::" + m.Name).Distinct().ToList();
        Check("only QualityUtility.SendCraftNotification loads a LetterCraftedLegendary* key",
              keyUsers.Count == 1 && keyUsers[0] == "RimWorld.QualityUtility::SendCraftNotification", string.Join(", ", keyUsers.ToArray()));

        MethodDefinition send = acs.GetType("RimWorld.QualityUtility").Methods.Single(m => m.Name == "SendCraftNotification");
        Check("SendCraftNotification(Thing thing, Pawn worker): no SkillDef or recipe is passed to it",
              send.Parameters.Count == 2 && send.Parameters[0].ParameterType.FullName == "Verse.Thing"
              && send.Parameters[1].ParameterType.FullName == "Verse.Pawn"
              && send.Parameters[0].Name == "thing" && send.Parameters[1].Name == "worker");
        List<Instruction> body = send.Body.Instructions.ToList();
        List<Instruction> letterCalls = body.Where(i => IsCall(i, "Verse.LetterStack", "ReceiveLetter")).ToList();
        Check("its body sends one of four letters (Masterwork/Legendary x plain/art), each immediately followed by ret",
              letterCalls.Count == 4 && letterCalls.All(i => i.Next != null && i.Next.OpCode.Code == Code.Ret), letterCalls.Count + " ReceiveLetter calls");
        Check("its Legendary branch reads CompQuality.Quality through the cached ThingWithComps.compQuality field",
              body.Any(i => i.OpCode.Code == Code.Ldfld && ((FieldReference)i.Operand).FullName.Contains("ThingWithComps::compQuality"))
              && body.Count(i => IsCall(i, "RimWorld.CompQuality", "get_Quality")) == 4);
        string[] presentation = { "TryGetComp", "get_Props", "get_Quality", "get_LetterStack", "Translate", "get_LabelShort",
                                  "op_Implicit", "Named", "Formatted", "ReceiveLetter", "GenerateImageDescription" };
        List<string> sideCalls = body.Where(i => i.Operand is MethodReference && !presentation.Contains(((MethodReference)i.Operand).Name))
            .Select(i => ((MethodReference)i.Operand).FullName).ToList();
        Check("it calls nothing but presentation (no tale, record, history or quality write) and stores no field",
              sideCalls.Count == 0 && !body.Any(i => i.OpCode.Code == Code.Stfld || i.OpCode.Code == Code.Stsfld),
              sideCalls.Count == 0 ? "clean" : string.Join(", ", sideCalls.ToArray()));

        List<string> callers = all.Where(m => m.Body.Instructions.Any(i => IsCall(i, "RimWorld.QualityUtility", "SendCraftNotification")))
            .Select(m => m.DeclaringType.FullName + "::" + m.Name).OrderBy(s => s).ToList();
        Check("exactly three vanilla call sites: crafting, construction, cube sculpture",
              callers.SequenceEqual(new[] { "RimWorld.Frame::CompleteConstruction", "RimWorld.JobDriver_BuildCubeSculpture::PlaceAndFinish",
                                            "Verse.GenRecipe::PostProcessProduct" }), string.Join(", ", callers.ToArray()));

        MethodDefinition ppp = acs.GetType("Verse.GenRecipe").Methods.Single(m => m.Name == "PostProcessProduct");
        Instruction roll = ppp.Body.Instructions.Single(i => IsCall(i, "RimWorld.QualityUtility", "GenerateQualityCreatedByPawn"));
        Check("PostProcessProduct rolls quality with ldarg.2 (worker) and recipeDef.workSkill",
              roll.Previous.Previous.OpCode.Code == Code.Ldfld && ((FieldReference)roll.Previous.Previous.Operand).Name == "workSkill"
              && roll.Previous.Previous.Previous.OpCode.Code == Code.Ldarg_1 && roll.Previous.Previous.Previous.Previous.OpCode.Code == Code.Ldarg_2);
        Instruction sendSite = ppp.Body.Instructions.Single(i => IsCall(i, "RimWorld.QualityUtility", "SendCraftNotification"));
        Check("...and announces with (ldarg.0 product, ldarg.2 worker): the same objects the frame prefix records",
              sendSite.Previous.OpCode.Code == Code.Ldarg_2 && sendSite.Previous.Previous.OpCode.Code == Code.Ldarg_0
              && ppp.Parameters[0].Name == "product" && ppp.Parameters[1].Name == "recipeDef" && ppp.Parameters[2].Name == "worker");
        Check("...and PostProcessProduct is never re-entered by the vanilla methods it calls (single-level frame)",
              !ppp.Body.Instructions.Any(i => IsCall(i, "Verse.GenRecipe", "PostProcessProduct")));

        foreach (string site in new[] { "RimWorld.Frame::CompleteConstruction", "RimWorld.JobDriver_BuildCubeSculpture::PlaceAndFinish" })
        {
            string[] parts = site.Split(new[] { "::" }, StringSplitOptions.None);
            MethodDefinition m = acs.GetType(parts[0]).Methods.Single(x => x.Name == parts[1]);
            Instruction q = m.Body.Instructions.Single(i => IsCall(i, "RimWorld.QualityUtility", "GenerateQualityCreatedByPawn"));
            Instruction skill = q.Previous.Previous;
            Check(site + " rolls with hard-coded SkillDefOf.Construction (never Crafting)",
                  skill.OpCode.Code == Code.Ldsfld && ((FieldReference)skill.Operand).FullName == "RimWorld.SkillDef RimWorld.SkillDefOf::Construction");
        }

        MethodDefinition setQuality = acs.GetType("RimWorld.CompQuality").Methods.Single(m => m.Name == "SetQuality");
        MethodDefinition postQualitySet = acs.GetType("Verse.Thing").Methods.Single(m => m.Name == "PostQualitySet");
        Check("CompQuality.SetQuality sends no letter (Transcendent Crafting's completion call)",
              !setQuality.Body.Instructions.Any(i => IsCall(i, "Verse.LetterStack", "ReceiveLetter") || IsCall(i, "RimWorld.QualityUtility", "SendCraftNotification"))
              && postQualitySet.Body.Instructions.Count == 1);

        List<MethodDefinition> modMethods = mod.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody).ToList();
        MethodDefinition tryComplete = mod.GetType("Grandmaster21.Transcendent.Building_MagicalWorkstation").Methods.Single(m => m.Name == "TryComplete");
        Check("Transcendent completion sets Legendary directly with CompQuality.SetQuality",
              tryComplete.Body.Instructions.Any(i => IsCall(i, "RimWorld.CompQuality", "SetQuality")));
        List<string> modLetterCallers = modMethods.Where(m => m.Body.Instructions.Any(i =>
                IsCall(i, "RimWorld.QualityUtility", "SendCraftNotification") || IsCall(i, "Verse.GenRecipe", "PostProcessProduct")
                || IsCall(i, "Verse.LetterStack", "ReceiveLetter")))
            .Select(m => m.DeclaringType.Name + "::" + m.Name).ToList();
        Check("only the Construction notification wrapper calls SendCraftNotification; no other notification pipeline callers",
              modLetterCallers.SequenceEqual(new[] { "Patch_ConstructionLegendaryNotification::SendConstructionNotification" }),
              string.Join(", ", modLetterCallers.ToArray()));

        int receiveCallers = all.Count(m => m.Body.Instructions.Any(i => IsCall(i, "Verse.LetterStack", "ReceiveLetter")));
        Check("LetterStack.ReceiveLetter is a global chokepoint (why it is NOT patched)", receiveCallers > 100, receiveCallers + " calling methods");
    }

    static bool IsCall(Instruction i, string type, string name)
    {
        MethodReference m = i.Operand as MethodReference;
        return m != null && (i.OpCode.Code == Code.Call || i.OpCode.Code == Code.Callvirt)
               && m.DeclaringType.FullName == type && m.Name == name;
    }

    // ------------------------------------------------------------------ 2. settings

    static void Settings(string scratch)
    {
        Console.WriteLine("\n=== 2. Setting: default, old config, real settings serializer ===");
        Check("a new Gm21Settings defaults the letter setting to ON", new Gm21Settings().showCraftingGrandmasterLegendaryNotifications);

        // A 0.13.0 config file: every setting that existed then, non-default where possible, and no new key.
        settingsPath = Path.Combine(scratch, "Mod_Grandmaster21_Gm21Mod.xml");
        File.WriteAllText(settingsPath,
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<SettingsBlock>\n  <ModSettings Class=\"Grandmaster21.Gm21Settings\">\n"
            + "    <grandmasterXpRequirement>5000</grandmasterXpRequirement>\n    <deterministicQuality>False</deterministicQuality>\n"
            + "    <showGrandmasterProgress>False</showGrandmasterProgress>\n  </ModSettings>\n</SettingsBlock>\n");
        Gm21Settings old = LoadedModManager.ReadModSettings<Gm21Settings>("Grandmaster21", "Gm21Mod");
        Check("an old config file with no key loads the setting as ON (real LoadedModManager.ReadModSettings)",
              old != null && old.showCraftingGrandmasterLegendaryNotifications);
        Check("  ...and the settings it did contain still load", old != null && old.grandmasterXpRequirement == 5000
              && !old.deterministicQuality && !old.showGrandmasterProgress);

        old.showCraftingGrandmasterLegendaryNotifications = false;
        LoadedModManager.WriteModSettings("Grandmaster21", "Gm21Mod", old);
        string xml = File.ReadAllText(settingsPath);
        Check("OFF is written by the real LoadedModManager.WriteModSettings",
              xml.Contains("<showCraftingGrandmasterLegendaryNotifications>False</showCraftingGrandmasterLegendaryNotifications>"));
        Gm21Settings off = LoadedModManager.ReadModSettings<Gm21Settings>("Grandmaster21", "Gm21Mod");
        Check("  ...and reads back OFF", off != null && !off.showCraftingGrandmasterLegendaryNotifications);

        off.showCraftingGrandmasterLegendaryNotifications = true;
        LoadedModManager.WriteModSettings("Grandmaster21", "Gm21Mod", off);
        Gm21Settings on = LoadedModManager.ReadModSettings<Gm21Settings>("Grandmaster21", "Gm21Mod");
        Check("ON round-trips too (default value: Scribe omits the element, so the file reads like an old one)",
              on != null && on.showCraftingGrandmasterLegendaryNotifications
              && !File.ReadAllText(settingsPath).Contains("showCraftingGrandmasterLegendaryNotifications"));
        // The one line that may appear here is vanilla BackCompatibility's one-time GenTypes.AllTypes scan
        // failing to load NAudio, which ships beside Assembly-CSharp in a real install but is not needed
        // headless. The failures that would matter are the reader's own.
        List<string> settingsErrors = logged.Where(l => l.Contains("Caught exception while loading mod settings")
            || l.Contains("Could not find class") || l.Contains("Exception loading") || l.Contains("Scribe")).ToList();
        Check("the real reader reported no settings failure (no fallback to fresh settings)", settingsErrors.Count == 0,
              settingsErrors.Count == 0 ? logged.Count + " unrelated environment line(s)" : settingsErrors[0]);
        Check("the setting is a ModSettings field only: Gm21Settings has no other new state",
              typeof(Gm21Settings).GetFields(BindingFlags.Public | BindingFlags.Instance).Select(f => f.Name).OrderBy(s => s)
                  .SequenceEqual(new[] { "deterministicQuality", "grandmasterXpRequirement", "requirementBuffer",
                                         "showConstructionGrandmasterLegendaryNotifications", "showCraftingGrandmasterLegendaryNotifications", "showGrandmasterProgress" }));
    }

    static void SettingsUi(string root, string modPath)
    {
        Console.WriteLine("\n=== 2b. Setting: shipped keys and placement in the existing Mod Settings page ===");
        XElement keyed = XDocument.Load(Path.Combine(root, "Languages/English/Keyed/Grandmaster21.xml")).Root;
        string label = (string)keyed.Element("GM21_Setting_CraftingLegendaryLetters");
        string desc = (string)keyed.Element("GM21_Setting_CraftingLegendaryLettersDesc");
        Check("label and tooltip keys ship in Languages/English/Keyed/Grandmaster21.xml",
              label == "Crafting Grandmaster Legendary notifications" && !string.IsNullOrEmpty(desc) && desc.Contains("Legendary"), label);

        MethodDefinition draw = ModuleDefinition.ReadModule(modPath).GetType("Grandmaster21.Gm21Mod").Methods.Single(m => m.Name == "DoSettingsWindowContents");
        List<string> keys = draw.Body.Instructions.Where(i => i.OpCode.Code == Code.Ldstr).Select(i => (string)i.Operand).ToList();
        int det = keys.IndexOf("GM21_Setting_DeterministicQuality"), mine = keys.IndexOf("GM21_Setting_CraftingLegendaryLetters"),
            progress = keys.IndexOf("GM21_Setting_ShowProgress");
        Check("drawn on the existing page, between Deterministic quality and Show Grandmaster progress",
              det >= 0 && det < mine && mine < progress && keys.Contains("GM21_Setting_CraftingLegendaryLettersDesc"));
        Check("every settings key the page draws is shipped", keys.Where(k => k.StartsWith("GM21_", StringComparison.Ordinal))
              .All(k => keyed.Element(k) != null));
        Check("the page has four checkboxes: deterministic quality, both independent notification settings, and progress",
              draw.Body.Instructions.Count(i => IsCall(i, "Verse.Listing_Standard", "CheckboxLabeled")) == 4);
    }

    // ------------------------------------------------------------------ 3. installation

    static void Install()
    {
        Console.WriteLine("\n=== 3. The shipped patches, installed on the real vanilla methods ===");
        Harmony gm = new Harmony(Gm21Id);
        foreach (Type t in new[] { typeof(Patch_QualityUtility_ByLevel), typeof(Patch_QualityUtility_ByPawn), typeof(Patch_SkillRecord_GetLevel) })
            gm.CreateClassProcessor(t).Patch();
        int warningsBefore = logged.Count;
        Patch_CraftingLegendaryNotification.Apply(gm);
        Check("Patch_CraftingLegendaryNotification.Apply installed both halves, silently", Patch_CraftingLegendaryNotification.Applied
              && logged.Count == warningsBefore);
        Check("test environment: the one ModsConfig.IdeologyActive read in GenerateQualityCreatedByPawn answers \"off\"", ideologyShimmed == 1,
              ideologyShimmed + " site(s)");

        Patches craft = Harmony.GetPatchInfo(PostProcess);
        Patches notify = Harmony.GetPatchInfo(Notify);
        Check("GenRecipe.PostProcessProduct: one GM21 prefix + one GM21 finalizer, nothing else from GM21",
              craft != null && craft.Prefixes.Count(p => p.owner == Gm21Id) == 1 && craft.Finalizers.Count(p => p.owner == Gm21Id) == 1
              && craft.Postfixes.Count(p => p.owner == Gm21Id) == 0 && craft.Transpilers.Count(p => p.owner == Gm21Id) == 0);
        Check("QualityUtility.SendCraftNotification: one GM21 prefix, nothing else from GM21",
              notify != null && notify.Prefixes.Count(p => p.owner == Gm21Id) == 1 && notify.Postfixes.Count(p => p.owner == Gm21Id) == 0
              && notify.Transpilers.Count(p => p.owner == Gm21Id) == 0 && notify.Finalizers.Count(p => p.owner == Gm21Id) == 0);
        Check("the finalizer is void: it cannot swallow an exception thrown inside the craft",
              AccessTools.Method(typeof(Patch_CraftingLegendaryNotification), "Finalizer_PostProcessProduct").ReturnType == typeof(void));

        HarmonyLib.Patch suppress = notify.Prefixes.Single(p => p.owner == Gm21Id);
        Check("the SendCraftNotification suppression prefix is registered at Priority.Last (Harmony's own patch info)",
              suppress.priority == Priority.Last && suppress.PatchMethod.Name == "Prefix_SendCraftNotification", "priority=" + suppress.priority);
        List<string> reordered = new List<string>();
        foreach (MethodBase m in Harmony.GetAllPatchedMethods())
        {
            Patches info = Harmony.GetPatchInfo(m);
            foreach (HarmonyLib.Patch p in info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers))
                if (p.owner == Gm21Id && p.PatchMethod != suppress.PatchMethod && p.priority != Priority.Normal)
                    reordered.Add(m.DeclaringType.Name + "." + m.Name + ":" + p.PatchMethod.Name + "=" + p.priority);
        }
        Check("every other GM21 patch here keeps Harmony's default priority (PostProcessProduct frame, quality, level)",
              reordered.Count == 0, reordered.Count == 0 ? "all Normal" : string.Join(", ", reordered.ToArray()));
        FieldInfo frame = typeof(Patch_CraftingLegendaryNotification).GetField("current", BindingFlags.NonPublic | BindingFlags.Static);
        Check("the in-flight crafting frame field carries [ThreadStatic]",
              frame != null && frame.FieldType == typeof(Gm21CraftingFrame) && frame.IsDefined(typeof(ThreadStaticAttribute), false));

        List<string> gmPatched = Harmony.GetAllPatchedMethods().Where(m => Harmony.GetPatchInfo(m).Owners.Contains(Gm21Id))
            .Select(m => m.DeclaringType.Name + "." + m.Name).OrderBy(s => s).ToList();
        Check("every method GM21 patched in this process is a quality/level patch or one of the two notification targets",
              gmPatched.SequenceEqual(new[] { "GenRecipe.PostProcessProduct", "QualityUtility.GenerateQualityCreatedByPawn",
                                              "QualityUtility.GenerateQualityCreatedByPawn", "QualityUtility.SendCraftNotification",
                                              "SkillRecord.GetLevel" }), string.Join(", ", gmPatched.ToArray()));
        List<string> letterish = new List<string>();
        foreach (Type t in new[] { typeof(LetterStack), typeof(Messages), typeof(LetterMaker), typeof(Letter), typeof(ChoiceLetter) })
            foreach (MethodInfo m in t.GetMethods(Any | BindingFlags.DeclaredOnly))
            {
                Patches p = Harmony.GetPatchInfo(m);
                if (p != null && p.Owners.Contains(Gm21Id)) letterish.Add(t.Name + "." + m.Name);
            }
        Check("no LetterStack, Messages, LetterMaker or Letter method carries a GM21 patch", letterish.Count == 0,
              letterish.Count == 0 ? "none" : string.Join(", ", letterish.ToArray()));
    }

    // ------------------------------------------------------------------ 4. the Crafting Grandmaster

    static void Grandmaster()
    {
        Console.WriteLine("\n=== 4. Crafting Grandmaster (stored Crafting 21), real PostProcessProduct ===");
        Pawn gm = MakePawn("Grandmaster", 21);
        RecipeDef gladius = Recipe("TestMakeGladius", crafting);
        Check("fixture: Gm21.IsGrandmaster(pawn, Crafting)", Gm21.IsGrandmaster(gm, SkillDefOf.Crafting));

        Show = true;
        ThingWithComps a = Product();
        List<string> onLetters = Craft(a, gladius, gm);
        Check("setting ON: the product is Legendary", QualityOf(a) == QualityCategory.Legendary, QualityOf(a).ToString());
        Check("setting ON: vanilla's Legendary letter is sent, exactly once", Only(onLetters, Legendary), Show1(onLetters));

        Show = false;
        ThingWithComps b = Product();
        List<string> offLetters = Craft(b, gladius, gm);
        Check("setting OFF: the product is still Legendary", QualityOf(b) == QualityCategory.Legendary, QualityOf(b).ToString());
        Check("setting OFF: the Legendary letter is suppressed", offLetters.Count == 0, Show1(offLetters));

        Console.WriteLine("  -- a Crafting recipe whose product carries art (vanilla's art-variant letter)");
        Show = true;
        tales = artCredits = artInits = 0;
        ThingWithComps c = Product(art: true);
        List<string> artOn = Craft(c, gladius, gm);
        int talesOn = tales, creditsOn = artCredits, initsOn = artInits;
        Check("ON: Legendary art-variant letter sent", Only(artOn, LegendaryArt), Show1(artOn));
        tales = artCredits = artInits = 0;
        Show = false;
        ThingWithComps d = Product(art: true);
        List<string> artOff = Craft(d, gladius, gm);
        Check("OFF: Legendary art-variant letter suppressed, item Legendary", artOff.Count == 0 && QualityOf(d) == QualityCategory.Legendary, Show1(artOff));
        Check("the CraftedArt tale, art generation and author credit happen identically ON and OFF",
              talesOn == 1 && tales == 1 && creditsOn == 1 && artCredits == 1 && initsOn == 1 && artInits == 1,
              "tales " + talesOn + "/" + tales + ", credit " + creditsOn + "/" + artCredits + ", art " + initsOn + "/" + artInits);

        otherModMasterworkFor = gm;
        ThingWithComps lowered = Product();
        List<string> loweredLetters = Craft(lowered, gladius, gm);
        otherModMasterworkFor = null;
        Check("OFF, and another mod lowers the Grandmaster's product to Masterwork: the Masterwork letter still shows",
              QualityOf(lowered) == QualityCategory.Masterwork && Only(loweredLetters, Masterwork), Show1(loweredLetters));

        Console.WriteLine("  -- immediate effect: the setting is read at each letter, not cached");
        List<string> seq = new List<string>();
        foreach (bool show in new[] { false, true, false, true })
        {
            Show = show;
            seq.Add(Craft(Product(), gladius, gm).Count.ToString());
        }
        Check("OFF, ON, OFF, ON on successive crafts -> 0, 1, 0, 1 letters", string.Join(",", seq.ToArray()) == "0,1,0,1", string.Join(",", seq.ToArray()));
    }

    // ------------------------------------------------------------------ 5. not a Grandmaster / aptitude

    static void NotGrandmaster()
    {
        Console.WriteLine("\n=== 5. Non-Grandmasters and aptitude (setting OFF throughout) ===");
        Show = false;
        RecipeDef gladius = Recipe("TestMakeGladius", crafting);

        Pawn twenty = MakePawn("Twenty", 20);
        ThingWithComps mw = Product();
        List<string> mwLetters = Craft(mw, gladius, twenty);
        Check("stored Crafting 20: GM21 caps at Masterwork and vanilla's Masterwork letter is untouched",
              QualityOf(mw) == QualityCategory.Masterwork && Only(mwLetters, Masterwork), QualityOf(mw) + ", " + Show1(mwLetters));

        otherModLegendaryFor = twenty;
        ThingWithComps viaMod = Product();
        List<string> modLetters = Craft(viaMod, gladius, twenty);
        otherModLegendaryFor = null;
        Check("stored Crafting 20 made Legendary by another mod: the Legendary letter still shows",
              QualityOf(viaMod) == QualityCategory.Legendary && Only(modLetters, Legendary), Show1(modLetters));

        Pawn muse = MakePawn("Inspired", 20);
        inspired.Add(muse);
        inspirationsEnded = 0;
        ThingWithComps insp = Product();
        List<string> inspLetters = Craft(insp, gladius, muse);
        inspired.Remove(muse);
        Check("stored Crafting 20 with Inspired Creativity: Masterwork (GM21 cap), inspiration consumed, Masterwork letter shown",
              QualityOf(insp) == QualityCategory.Masterwork && inspirationsEnded == 1 && Only(inspLetters, Masterwork),
              QualityOf(insp) + ", ended " + inspirationsEnded + ", " + Show1(inspLetters));

        Pawn gifted = MakePawn("Gifted", 20, craftingAptitude: 1);
        Check("stored Crafting 20 with +1 aptitude is NOT a Grandmaster", !Gm21.IsGrandmaster(gifted, SkillDefOf.Crafting),
              "Level=" + gifted.skills.GetSkill(crafting).Level);
        otherModLegendaryFor = gifted;
        ThingWithComps giftedItem = Product();
        List<string> giftedLetters = Craft(giftedItem, gladius, gifted);
        otherModLegendaryFor = null;
        Check("  ...and its Legendary (via another mod) keeps the vanilla letter", Only(giftedLetters, Legendary), Show1(giftedLetters));

        Pawn humbled = MakePawn("Humbled", 21, craftingAptitude: -5);
        Check("stored Crafting 21 with -5 aptitude IS a Grandmaster", Gm21.IsGrandmaster(humbled, SkillDefOf.Crafting),
              "Level=" + humbled.skills.GetSkill(crafting).Level);
        ThingWithComps humbledOff = Product();
        List<string> humbledOffLetters = Craft(humbledOff, gladius, humbled);
        Show = true;
        ThingWithComps humbledOn = Product();
        List<string> humbledOnLetters = Craft(humbledOn, gladius, humbled);
        Show = false;
        Check("  ...its product is Legendary and the toggle applies (OFF: none, ON: one)",
              QualityOf(humbledOff) == QualityCategory.Legendary && QualityOf(humbledOn) == QualityCategory.Legendary
              && humbledOffLetters.Count == 0 && Only(humbledOnLetters, Legendary), Show1(humbledOffLetters) + " / " + Show1(humbledOnLetters));
    }

    // ------------------------------------------------------------------ 6. other skills and call sites

    static void OtherSkills()
    {
        Console.WriteLine("\n=== 6. Other skills and other call sites (setting OFF throughout) ===");
        Show = false;
        RecipeDef sculpture = Recipe("TestMakeSculptureSmall", artistic);

        Pawn artist = MakePawn("Artist", 5, artisticLevel: 21);
        ThingWithComps s1 = Product(art: true);
        List<string> l1 = Craft(s1, sculpture, artist);
        Check("stored Artistic 21 (Crafting 5) sculpture: Legendary, letter shown", QualityOf(s1) == QualityCategory.Legendary
              && Only(l1, LegendaryArt), Show1(l1));

        Pawn both = MakePawn("Polymath", 21, artisticLevel: 21, constructionLevel: 21);
        ThingWithComps s2 = Product(art: true);
        List<string> l2 = Craft(s2, sculpture, both);
        Check("Crafting 21 AND Artistic 21, Artistic recipe: letter shown (the recipe's skill is not Crafting)",
              QualityOf(s2) == QualityCategory.Legendary && Only(l2, LegendaryArt), Show1(l2));

        Pawn craftsman = MakePawn("Craftsman", 21, artisticLevel: 20);
        ThingWithComps s3 = Product(art: true);
        List<string> l3 = Craft(s3, sculpture, craftsman);
        Check("Crafting 21 at an Artistic recipe with Artistic 20: Masterwork, Masterwork art letter shown",
              QualityOf(s3) == QualityCategory.Masterwork && l3.Count == 1 && l3[0].StartsWith("LetterCraftedMasterworkLabel|"), Show1(l3));

        // Frame.CompleteConstruction and JobDriver_BuildCubeSculpture.PlaceAndFinish, in the exact order
        // their IL performs it (audited in section 1): roll with Construction, SetQuality, announce.
        Console.WriteLine("  -- the construction/cube-sculpture call sequence, outside any crafting frame");
        ThingWithComps building = Product(art: true);
        building.compQuality.SetQuality(QualityUtility.GenerateQualityCreatedByPawn(both, SkillDefOf.Construction, true), ArtGenerationContext.Colony);
        List<string> l5 = Announce(building, both);
        Check("Construction 21 + Crafting 21 builder: Legendary, construction's letter shown",
              QualityOf(building) == QualityCategory.Legendary && Only(l5, LegendaryArt), Show1(l5));

        ThingWithComps madeEarlier = Product();
        Craft(madeEarlier, Recipe("TestMakeGladius", crafting), both);
        List<string> l6 = Announce(madeEarlier, both);
        Check("a Crafting Grandmaster's own Legendary item announced by some other code after the craft: shown",
              QualityOf(madeEarlier) == QualityCategory.Legendary && Only(l6, Legendary), Show1(l6));
    }

    // ------------------------------------------------------------------ 7. other letters, frames, exceptions

    static void Isolation()
    {
        Console.WriteLine("\n=== 7. Other letters, nested frames and exceptions (setting OFF, Crafting Grandmaster) ===");
        Show = false;
        Pawn gm = MakePawn("Grandmaster", 21);
        RecipeDef gladius = Recipe("TestMakeGladius", crafting);

        List<string> during = null;
        otherModDuringCraft = delegate
        {
            Find.LetterStack.ReceiveLetter((TaggedString)"TestRaidLabel", (TaggedString)"TestRaidText", LetterDefOf.ThreatBig,
                (LookTargets)null, (Faction)null, (Quest)null, (List<ThingDef>)null, (string)null, 0, true);
            ThingWithComps other = Product();
            other.compQuality.SetQuality(QualityCategory.Legendary, null);
            QualityUtility.SendCraftNotification(other, gm);
            during = new List<string>(letters);
        };
        ThingWithComps product = Product();
        List<string> all = Craft(product, gladius, gm);
        Check("mid-craft, an unrelated letter goes through", during != null && during.Count >= 1 && during[0] == "TestRaidLabel|TestRaidText",
              during == null ? "hook did not run" : Show1(during));
        Check("mid-craft, another Legendary item by the same Grandmaster (not this craft's product) keeps its letter",
              during != null && during.Count == 2 && during[1] == Legendary, during == null ? "" : Show1(during));
        Check("...and this craft's own Legendary letter is still suppressed", all.Count == 2 && QualityOf(product) == QualityCategory.Legendary, Show1(all));

        ThingWithComps inner = Product();
        List<string> innerLetters = null;
        otherModDuringCraft = delegate
        {
            List<string> saved = new List<string>(letters);
            innerLetters = Craft(inner, Recipe("TestMakeSculptureSmall", artistic), MakePawn("Artist", 5, artisticLevel: 21));
            letters.Clear(); letters.AddRange(saved);
        };
        ThingWithComps outer = Product();
        List<string> outerLetters = Craft(outer, gladius, gm);
        Check("a craft nested inside a craft: the inner (Artistic) letter shows", innerLetters != null && Only(innerLetters, Legendary),
              innerLetters == null ? "hook did not run" : Show1(innerLetters));
        Check("  ...and the outer Crafting Grandmaster's frame is restored, so its letter is still suppressed", outerLetters.Count == 0, Show1(outerLetters));

        otherModDuringCraft = delegate { throw new InvalidOperationException("other mod failed mid-craft"); };
        ThingWithComps doomed = Product();
        Exception thrown = null;
        try { Craft(doomed, gladius, gm); }
        catch (Exception e) { thrown = e; }
        Check("an exception inside the craft propagates unchanged (the finalizer does not swallow it)",
              thrown is InvalidOperationException && thrown.Message == "other mod failed mid-craft", thrown == null ? "swallowed" : thrown.GetType().Name);
        List<string> afterThrow = Announce(doomed, gm);
        Check("  ...and the frame was closed: the same item announced later is not suppressed", Only(afterThrow, Legendary), Show1(afterThrow));

        ThingWithComps local = Product();
        bool onCraftingThread = false, onOtherThread = true;
        otherModDuringCraft = delegate
        {
            onCraftingThread = Patch_CraftingLegendaryNotification.ShouldSuppress(local, gm);
            Thread other = new Thread(() => onOtherThread = Patch_CraftingLegendaryNotification.ShouldSuppress(local, gm));
            other.Start();
            other.Join();
        };
        Craft(local, gladius, gm);
        Check("the frame is thread-local: mid-craft, this product matches on the crafting thread and not on another thread",
              onCraftingThread && !onOtherThread, "crafting thread " + onCraftingThread + ", other thread " + onOtherThread);

        Gm21Settings saved2 = Gm21Mod.Settings;
        Gm21Mod.Settings = null;
        bool noSettings = Patch_CraftingLegendaryNotification.ShouldSuppress(product, gm);
        Gm21Mod.Settings = saved2;
        Check("no settings object -> never suppress", !noSettings);
        Check("null thing / null worker -> never suppress",
              !Patch_CraftingLegendaryNotification.ShouldSuppress(null, gm) && !Patch_CraftingLegendaryNotification.ShouldSuppress(product, null));
    }

    // ------------------------------------------------------------------ 7b. Harmony ordering

    static readonly List<string> otherModCalls = new List<string>();
    public static bool OtherModBoolPrefix() { otherModCalls.Add("bool prefix"); return true; }
    public static void OtherModVoidPrefix() { otherModCalls.Add("void prefix"); }
    public static bool OtherModLateBoolPrefix() { otherModCalls.Add("late bool prefix"); return true; }
    public static void OtherModLateVoidPrefix() { otherModCalls.Add("late void prefix"); }
    public static void OtherModPostfix() { otherModCalls.Add("postfix"); }

    static void HarmonyOrdering()
    {
        Console.WriteLine("\n=== 7b. Harmony ordering: other mods' patches on the real SendCraftNotification (Crafting Grandmaster) ===");
        const string OtherId = "othermod.test";
        Harmony other = new Harmony(OtherId);
        HarmonyMethod voidPrefix = Stub("OtherModVoidPrefix");
        voidPrefix.priority = Priority.Low;
        HarmonyMethod lateBool = Stub("OtherModLateBoolPrefix");
        lateBool.priority = Priority.Last;
        lateBool.after = new[] { Gm21Id };
        HarmonyMethod lateVoid = Stub("OtherModLateVoidPrefix");
        lateVoid.priority = Priority.Last;
        lateVoid.after = new[] { Gm21Id };
        other.Patch(Notify, Stub("OtherModBoolPrefix"));   // default (Normal) priority
        other.Patch(Notify, voidPrefix);
        other.Patch(Notify, lateBool);
        other.Patch(Notify, lateVoid);
        other.Patch(Notify, null, Stub("OtherModPostfix"));

        Pawn gm = MakePawn("Grandmaster", 21);
        RecipeDef gladius = Recipe("TestMakeGladius", crafting);

        Show = false;
        otherModCalls.Clear();
        List<string> off = Craft(Product(), gladius, gm);
        string offCalls = string.Join(" > ", otherModCalls.ToArray());
        Check("OFF: the letter is still suppressed with other mods' patches present", off.Count == 0, Show1(off));
        Check("  ...prefixes at ordinary priorities (Normal bool, Low void) run first, in order", otherModCalls.Count >= 2
              && otherModCalls[0] == "bool prefix" && otherModCalls[1] == "void prefix", offCalls);
        Check("  ...a void prefix ordered after GM21, and the postfix, still run",
              otherModCalls.Contains("late void prefix") && otherModCalls.Contains("postfix"), offCalls);
        Check("  ...a bool prefix explicitly ordered after GM21 is skipped (the documented caveat)",
              !otherModCalls.Contains("late bool prefix"), offCalls);

        Show = true;
        otherModCalls.Clear();
        List<string> on = Craft(Product(), gladius, gm);
        string onCalls = string.Join(" > ", otherModCalls.ToArray());
        Check("ON: the letter is sent and every one of them runs, GM21's decision coming after the ordinary prefixes",
              Only(on, Legendary) && onCalls == "bool prefix > void prefix > late bool prefix > late void prefix > postfix", onCalls);

        Show = false;
        other.UnpatchAll(OtherId);
        Patches after = Harmony.GetPatchInfo(Notify);
        Check("stand-ins removed; GM21's prefix is untouched", !after.Owners.Contains(OtherId)
              && after.Prefixes.Count(p => p.owner == Gm21Id && p.priority == Priority.Last) == 1);
    }

    // ------------------------------------------------------------------ 8. quality untouched

    static void QualityUntouched()
    {
        Console.WriteLine("\n=== 8. Quality is untouched by the setting ===");
        RecipeDef gladius = Recipe("TestMakeGladius", crafting);
        bool same = true;
        string rows = "";
        foreach (bool deterministic in new[] { true, false })
        {
            Gm21Mod.Settings.deterministicQuality = deterministic;
            for (int level = 0; level <= 21; level++)
            {
                Pawn p = MakePawn("L" + level, level);
                QualityCategory[] r = new QualityCategory[2];
                for (int k = 0; k < 2; k++)
                {
                    Show = k == 0;
                    Rand.PushState(1234 + level);
                    ThingWithComps t = Product();
                    Craft(t, gladius, p);
                    Rand.PopState();
                    r[k] = QualityOf(t);
                }
                if (r[0] != r[1]) same = false;
                if (deterministic && (level == 0 || level == 10 || level == 20 || level == 21)) rows += level + ":" + r[0] + " ";
            }
        }
        Gm21Mod.Settings.deterministicQuality = true;
        Show = false;
        Check("levels 0-21, deterministic and random quality: identical product quality with the setting ON and OFF", same, rows.Trim());
    }

    static int Main(string[] args)
    {
        FieldInfo prefs = typeof(Prefs).GetField("data", Any);
        prefs.SetValue(null, FormatterServices.GetUninitializedObject(prefs.FieldType));
        string modPath = args.Length > 0 ? args[0] : "Grandmaster21.dll";
        string acsPath = args.Length > 1 ? args[1] : "Assembly-CSharp.dll";
        string scratch = args.Length > 2 ? args[2] : Path.GetTempPath();
        string root = args.Length > 3 ? args[3] : ".";
        try
        {
            Audit(acsPath, modPath);
            InstallEnvironment();
            Gm21Mod.Settings = new Gm21Settings();
            Gm21Mod.Settings.Validate();
            Settings(scratch);
            SettingsUi(root, modPath);
            Install();
            Grandmaster();
            NotGrandmaster();
            OtherSkills();
            Isolation();
            HarmonyOrdering();
            QualityUntouched();
            ConstructionAuditAndInstall(acsPath, modPath);
            ConstructionSettings(scratch, root, modPath);
            ConstructionBehavior();
        }
        catch (Exception e) { Console.WriteLine("FAIL  unhandled: " + e); fail++; }
        Console.WriteLine("\nNOT RUN HERE (needs the Unity player and a loaded game): the settings window, a real bill job,"
                          + " the letter stack UI, Frame.CompleteConstruction / the cube sculpture end to end."
                          + " See Docs/Crafting21LegendaryLetterTest.md.");
        Console.WriteLine("================================");
        Console.WriteLine("PASS: " + pass + "   FAIL: " + fail);
        return fail == 0 ? 0 : 1;
    }
}
