// Real-DLL integration checks. Learn, LearnRateFactor, saturation, XP fields, GM patches,
// promotion and native Scribe serialization execute for real. Only environment/stat providers,
// presentation, and deterministic test draws are substituted. No running Unity game is claimed.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml.Linq;
using Grandmaster21;
using HarmonyLib;
using RimWorld;
using Verse;

static class AspirantChecks
{
    static int pass, fail, nextId = 2000, messages, promotions;
    static float globalFactor = 2f, animalFactor = 3f;
    static double? fixedDraw = 0.99;
    static bool modifyRate, throwRate, nestedQuery;
    static SkillRecord otherSkill;
    static float nestedRate;
    static Pawn shownPawn;
    static bool shownColonist = true, shownSpawned = true;
    static SkillDef crafting;
    static readonly List<string> logs = new List<string>();
    static readonly Type Support = typeof(Gm21).Assembly.GetType("Grandmaster21.Gm21AspirantLearning");
    static readonly Func<double, double> Chance = Bind<Func<double, double>>("Chance");
    static readonly Func<double, double> Bonus = Bind<Func<double, double>>("Bonus");
    static readonly Func<int, string, double, double> Roll = Bind<Func<int, string, double, double>>("Roll");
    static readonly Func<SkillRecord, bool, float> Rate = Bind<Func<SkillRecord, bool, float>>("ResolvedRate");
    static readonly Func<SkillRecord, bool> Saturated = Bind<Func<SkillRecord, bool>>("SaturatedForRate");
    static readonly Action<SkillRecord> Force = Bind<Action<SkillRecord>>("ForceOneInsight");

    static T Bind<T>(string name) where T : class
    { return Delegate.CreateDelegate(typeof(T), AccessTools.Method(Support, name)) as T; }
    static T Uninit<T>() { return (T)FormatterServices.GetUninitializedObject(typeof(T)); }
    static HarmonyMethod Hook(string name) { return new HarmonyMethod(AccessTools.Method(typeof(AspirantChecks), name)); }
    static void Check(string name, bool ok)
    { Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (ok) pass++; else fail++; }
    static bool Near(double a, double b) { return Math.Abs(a - b) <= Math.Max(1e-7, Math.Abs(b) * 1e-6); }
    static void Set(object obj, string name, object value) { AccessTools.Field(obj.GetType(), name).SetValue(obj, value); }

    static SkillRecord Skill(int level, Passion passion = Passion.Minor, float midnight = 0f, int aptitude = 0, SkillDef def = null, Pawn owner = null)
    {
        var pawn = owner ?? Uninit<Pawn>();
        if (owner == null)
        {
            pawn.thingIDNumber = nextId++;
            pawn.def = Uninit<ThingDef>(); pawn.def.race = new RaceProperties { intelligence = Intelligence.Humanlike };
            pawn.Name = new NameSingle("Aspirant");
            pawn.health = new Pawn_HealthTracker(pawn);
            pawn.skills = Uninit<Pawn_SkillTracker>();
            pawn.skills.skills = new List<SkillRecord>();
            Set(pawn.skills, "pawn", pawn);
            Set(pawn, "mapIndexOrState", (sbyte)-1);
        }
        var skill = new SkillRecord(pawn, def ?? crafting) { levelInt = level, passion = passion, xpSinceMidnight = midnight };
        Set(skill, "cachedTotallyDisabled", BoolUnknown.False);
        Set(skill, "cachedPermanentlyDisabled", BoolUnknown.False);
        Set(skill, "aptitudeCached", (int?)aptitude);
        pawn.skills.skills.Add(skill);
        return skill;
    }

    // Only the values returned by the stat provider are controlled. The real LearnRateFactor
    // composes passion, direct, both stats, saturation and the simulated other-mod postfix.
    static bool StatValue(StatDef stat, ref float __result)
    {
        __result = stat == StatDefOf.GlobalLearningFactor ? globalFactor : animalFactor;
        return false;
    }
    static bool Colonist(Pawn __instance, ref bool __result) { __result = __instance == shownPawn && shownColonist; return false; }
    static bool Spawned(Thing __instance, ref bool __result) { __result = __instance == shownPawn && shownSpawned; return false; }
    static bool PawnLabel(ref string __result) { __result = "Aspirant"; return false; }
    static bool SkillLabel(ref TaggedString __result) { __result = "Crafting"; return false; }
    static bool LogText(string text) { logs.Add(text); return false; }
    static bool Skip() { return false; }
    static bool False(ref bool __result) { __result = false; return false; }
    static bool Translation(string key, ref TaggedString __result) { __result = key; return false; }
    static bool Message(string text)
    {
        if (text == "GM21_GrandmasterInsight") messages++;
        if (text == "GM21_BecameGrandmaster") promotions++;
        // Intentionally consume RNG in presentation; the Insight notification scope must undo it.
        float unused = Rand.Value;
        return false;
    }
    static bool TestDraw(ref double __result)
    { if (!fixedDraw.HasValue) return true; __result = fixedDraw.Value; return false; }
    static void ModdedRate(SkillRecord __instance, ref float __result)
    {
        if (throwRate) throw new InvalidOperationException("test modifier threw");
        if (nestedQuery && __instance != otherSkill) nestedRate = otherSkill.LearnRateFactor(false);
        if (modifyRate) __result = __result * 2f + 7f;
    }

    static void Main(string[] args)
    {
        var env = new Harmony("gm21.aspirant.environment");
        foreach (string name in new[] { "Error", "Warning", "Message" })
            env.Patch(AccessTools.Method(typeof(Log), name, new[] { typeof(string) }), Hook("LogText"));
        env.Patch(AccessTools.Method(typeof(Log), "ErrorOnce", new[] { typeof(string), typeof(int) }), Hook("LogText"));
        env.Patch(AccessTools.Method(typeof(StatExtension), "GetStatValue", new[] { typeof(Thing), typeof(StatDef), typeof(bool), typeof(int) }), Hook("StatValue"));
        env.Patch(AccessTools.PropertyGetter(typeof(Pawn), "IsColonist"), Hook("Colonist"));
        env.Patch(AccessTools.PropertyGetter(typeof(Thing), "Spawned"), Hook("Spawned"));
        env.Patch(AccessTools.PropertyGetter(typeof(Entity), "LabelShortCap"), Hook("PawnLabel"));
        env.Patch(AccessTools.PropertyGetter(typeof(Def), "LabelCap"), Hook("SkillLabel"));
        env.Patch(AccessTools.Method(typeof(DeepProfiler), "Start"), Hook("Skip"));
        env.Patch(AccessTools.Method(typeof(DeepProfiler), "End"), Hook("Skip"));
        env.Patch(AccessTools.PropertyGetter(typeof(Prefs), "DevMode"), Hook("False"));
        // This headless fixture supplies its own defs instead of running the game loader.
        env.Patch(AccessTools.Method(typeof(DefOfHelper), "EnsureInitializedInCtor"), Hook("Skip"));
        env.Patch(AccessTools.Method(typeof(TranslatorFormattedStringExtensions), "Translate", new[] { typeof(string), typeof(NamedArgument), typeof(NamedArgument), typeof(NamedArgument) }), Hook("Translation"));
        env.Patch(AccessTools.Method(typeof(TranslatorFormattedStringExtensions), "Translate", new[] { typeof(string), typeof(NamedArgument), typeof(NamedArgument) }), Hook("Translation"));
        env.Patch(AccessTools.Method(typeof(Messages), "Message", new[] { typeof(string), typeof(LookTargets), typeof(MessageTypeDef), typeof(bool) }), Hook("Message"));
        env.Patch(AccessTools.Method(Support, "Roll"), Hook("TestDraw"));
        env.Patch(AccessTools.Method(typeof(SkillRecord), "LearnRateFactor"), postfix: Hook("ModdedRate"));

        Gm21Mod.Settings = new Gm21Settings(); Gm21Mod.Settings.Validate();
        crafting = new SkillDef { defName = "TestCrafting", label = "crafting" };
        DefDatabase<SkillDef>.Add(crafting);
        SkillDefOf.Animals = new SkillDef { defName = "TestAnimals", label = "animals" };
        DefDatabase<SkillDef>.Add(SkillDefOf.Animals);
        StatDefOf.GlobalLearningFactor = new StatDef { defName = "TestGlobalLearning" };
        StatDefOf.AnimalsLearningFactor = new StatDef { defName = "TestAnimalsLearning" };
        MessageTypeDefOf.PositiveEvent = new MessageTypeDef { defName = "TestPositive" };

        // Baseline comes from the real unmodified saturation/learning bodies.
        var baseline = Skill(20, Passion.Major, 5000f, 0, SkillDefOf.Animals);
        Check("real vanilla rate: Major * global * animals * 0.2", Near(baseline.LearnRateFactor(false), 1.5 * 2 * 3 * 0.2));
        baseline.xpSinceMidnight = 4000f;
        Check("real saturation boundary is strictly greater than 4000", !baseline.LearningSaturatedToday);
        baseline.xpSinceMidnight = 4001f;
        Check("real saturation getter true at 4001", baseline.LearningSaturatedToday);

        var gm = new Harmony("gm21.aspirant.production");
        foreach (Type type in new[] { typeof(Patch_SkillRecord_LearnRateFactor), typeof(Patch_SkillRecord_Learn),
            typeof(Patch_SkillRecord_ExposeData), typeof(Patch_SkillRecord_SetLevel), typeof(Patch_SkillRecord_GetLevel) })
            gm.CreateClassProcessor(type).Patch();
        Check("both real transpilers applied", Gm21.LearnPatchApplied && Patch_SkillRecord_LearnRateFactor.Applied);
        Check("daily getter remains truthful outside capture", baseline.LearningSaturatedToday && Saturated(baseline));
        Check("ordinary resolved rate outside capture remains vanilla", Near(baseline.LearnRateFactor(false), 1.8));

        foreach (Passion passion in new[] { Passion.None, Passion.Minor, Passion.Major })
        foreach (bool animal in new[] { false, true })
        {
            var a = Skill(20, passion, 0f, 0, animal ? SkillDefOf.Animals : crafting);
            var b = Skill(20, passion, 5000f, 0, a.def);
            double expected = 100.0 * (passion == Passion.None ? 0.35f : passion == Passion.Minor ? 1f : 1.5f) * 2 * (animal ? 3 : 1);
            a.Learn(100f); b.Learn(100f);
            Check("L20 saturation-free bank " + passion + "/animals=" + animal, Near(GrandmasterStore.Get(a), expected) && Near(GrandmasterStore.Get(b), expected));
            Check("vanilla midnight counter still saturated " + passion + "/animals=" + animal, Near(b.xpSinceMidnight, 5000.0 + expected * 0.2));
        }
        modifyRate = true;
        var modded = Skill(20, Passion.Major, 5000f, 0, SkillDefOf.Animals);
        modded.Learn(10f);
        Check("multiplicative AND additive postfix preserved (no divide-final-rate shortcut)", Near(GrandmasterStore.Get(modded), 250));
        Check("vanilla counter sees normal modded saturated rate", Near(modded.xpSinceMidnight, 5106));
        modifyRate = false;

        var direct = Skill(20, Passion.Major, 5000f);
        direct.Learn(10f, true, false);
        Check("direct keeps passion but omits stats/saturation and midnight update", GrandmasterStore.Get(direct) == 15 && direct.xpSinceMidnight == 5000);
        direct.Learn(10f, false, true);
        Check("ignoreLearnRate banks raw XP and preserves vanilla counter semantics", GrandmasterStore.Get(direct) == 25 && direct.xpSinceMidnight == 5010);
        DebugSettings.fastLearning = true;
        var debug = Skill(20, midnight: 5000f); debug.Learn(1f);
        Check("fast-learning early return remains 200", GrandmasterStore.Get(debug) == 200);
        DebugSettings.fastLearning = false;

        var level19 = Skill(19, Passion.Major, 5000, aptitude: 1);
        var sibling = Skill(20, Passion.Major, 5000, aptitude: -5, owner: level19.Pawn);
        level19.Learn(10f); sibling.Learn(10f);
        Check("stored 19 + aptitude = displayed 20 still vanilla", level19.Level == 20 && GrandmasterStore.Get(level19) == 0 && Near(level19.xpSinceLastLevel, 6));
        Check("stored 20 with negative aptitude gets support", sibling.Level == 15 && GrandmasterStore.Get(sibling) == 30);
        Check("other skill on SAME pawn remains saturated", Near(level19.LearnRateFactor(false), 0.6));
        otherSkill = level19; nestedQuery = true;
        Check("nested different-skill rate query cannot inherit bypass", Near(Rate(sibling, false), 3) && Near(nestedRate, 0.6));
        nestedQuery = false;
        throwRate = true;
        try { Rate(sibling, false); Check("rate exception propagates", false); }
        catch (InvalidOperationException) { Check("rate exception propagates", true); }
        finally { throwRate = false; }
        Check("rate scope restored after exception", Saturated(sibling) && Near(sibling.LearnRateFactor(false), 0.6));

        var decay = Skill(20, midnight: 5000f);
        GrandmasterStore.Set(decay, 123.5);
        decay.Learn(-1001f, true, true);
        Check("level-20 decay to 19 retains bank", decay.levelInt == 19 && GrandmasterStore.Get(decay) == 123.5);
        fixedDraw = 0; // would force any positive chance, but 19 must not roll at all
        decay.Learn(1f);
        Check("19 suspends both support mechanics", decay.levelInt == 19 && GrandmasterStore.Get(decay) == 123.5 && Near(decay.LearnRateFactor(false), 0.4));
        decay.xpSinceLastLevel = decay.XpRequiredForLevelUp - 1;
        decay.Learn(2f, true, true);
        Check("crossing from 19 to 20 retains existing capture-start semantics", decay.levelInt == 20 && GrandmasterStore.Get(decay) == 123.5);
        fixedDraw = 0.99; decay.Learn(1f);
        Check("support resumes on next level-20 event with old bank intact", Near(GrandmasterStore.Get(decay), 125.5));

        var disabled = Skill(20); Set(disabled, "cachedTotallyDisabled", BoolUnknown.True);
        fixedDraw = 0;
        disabled.Learn(100f);
        var nonpositive = Skill(20); nonpositive.Learn(0f); nonpositive.Learn(-1f);
        var grandmaster = Skill(21, midnight: 5000f); GrandmasterStore.Set(grandmaster, 777);
        grandmaster.Learn(1f); grandmaster.Learn(-100000f);
        Check("disabled and nonpositive events never award XP/Insight", GrandmasterStore.Get(disabled) == 0 && GrandmasterStore.Get(nonpositive) == 0);
        Check("21 banking, saturation and permanence unchanged", grandmaster.levelInt == 21 && GrandmasterStore.Get(grandmaster) == 777 && Near(grandmaster.LearnRateFactor(false), 0.4));
        var invalid = Skill(20);
        // Do not invoke vanilla's NaN/Infinity arithmetic: test the actual banking prefix only.
        Patch_SkillRecord_Learn.Prefix(invalid, float.NaN, false, false);
        Patch_SkillRecord_Learn.Prefix(invalid, float.PositiveInfinity, false, true);
        globalFactor = float.MaxValue; Patch_SkillRecord_Learn.Prefix(invalid, float.MaxValue, false, false); globalFactor = 2;
        Check("invalid/overflowed resolved XP cannot poison GM store", GrandmasterStore.Get(invalid) == 0);
        fixedDraw = 0.99;

        foreach (double x in new[] { -1.0, 0.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Check("invalid/nonpositive chance is zero: " + x, Chance(x) == 0);
        foreach (double x in new[] { double.Epsilon, 1e-12, 0.001, 1.0, 100.0, 1000.0, 10000.0, 1e9, double.MaxValue })
        {
            double p = Chance(x);
            Check("chance finite and bounded: " + x, !double.IsNaN(p) && p >= 0 && p <= 1);
        }
        Check("tiny XP avoids subtraction cancellation", Chance(1e-12) > 0);
        Check("chance examples and huge finite limit", Near(Chance(100), 0.000999500166625) && Near(Chance(1000), 0.009950166250832)
            && Near(Chance(10000), 0.09516258196404) && Chance(double.MaxValue) == 1);
        foreach (double total in new[] { 1.0, 100.0, 1000.0, 10000.0, 100000.0 })
        foreach (int chunks in new[] { 2, 100, 10000 })
            Check("fragmented probability total=" + total + "/chunks=" + chunks,
                Math.Abs(1 - Math.Pow(1 - Chance(total / chunks), chunks) - Chance(total)) < 1e-10);
        foreach (double req in new[] { 1e6, 1e9, 1e10 }) Check("bonus is 5% of " + req, Bonus(req) == req * 0.05);
        foreach (double req in new[] { -1.0, 0.0, double.NaN, double.PositiveInfinity }) Check("invalid bonus safe: " + req, Bonus(req) == 0);

        fixedDraw = null;
        double roll = Roll(123, "Crafting", 1000000123.25);
        Check("local RNG repeatable from saved identity and XP", roll == Roll(123, "Crafting", 1000000123.25) && roll >= 0 && roll < 1);
        Check("distinct skill/pawn/events get distinct opportunities", roll != Roll(124, "Crafting", 1000000123.25)
            && roll != Roll(123, "Shooting", 1000000123.25) && roll != Roll(123, "Crafting", 1000000124.25));
        Rand.PushState(2468); float expectedNext = Rand.Value; Rand.PopState();
        Rand.PushState(2468);
        var rng = Skill(20); rng.Learn(100f, true, true);
        float actualNext = Rand.Value; Rand.PopState();
        Check("actual learning/Insight RNG leaves global Rand untouched", expectedNext == actualNext);

        Gm21Mod.Settings.grandmasterXpRequirement = 1000;
        var promote = Skill(20); GrandmasterStore.Set(promote, 950);
        shownPawn = promote.Pawn; shownColonist = shownSpawned = true; messages = promotions = 0;
        Force(promote);
        Check("dev one-shot goes through ordinary XP + bonus + existing promotion", GrandmasterStore.Get(promote) == 1001 && promote.levelInt == 21);
        Check("one positive Insight message and one existing promotion message", messages == 1 && promotions == 1);
        promote.Learn(1000000f, true, true);
        Check("no duplicate promotion, Insight, or level 22", promote.levelInt == 21 && GrandmasterStore.Get(promote) == 1001 && messages == 1 && promotions == 1);
        Gm21Mod.Settings.grandmasterXpRequirement = 1e9;
        var notify = Skill(20); shownPawn = notify.Pawn; messages = 0;
        Rand.PushState(1357); expectedNext = Rand.Value; Rand.PopState();
        Rand.PushState(1357); Force(notify); actualNext = Rand.Value; Rand.PopState();
        Check("Insight feedback also restores global RNG", messages == 1 && actualNext == expectedNext);
        shownColonist = false; Force(notify);
        shownColonist = true; shownSpawned = false; Force(notify);
        Check("NPC/world pawn Insights are silent", messages == 1);
        shownPawn = null;

        // Real native SkillRecord + GM ExposeData round trip, including sub-XP precision and replay seed.
        string path = args[1];
        var saved = Skill(20); GrandmasterStore.Set(saved, 900000123.25);
        saved.xpSinceMidnight = 5001;
        Scribe.saver.InitSaving(path, "skill"); saved.ExposeData(); Scribe.saver.FinalizeSaving();
        string xml = File.ReadAllText(path);
        var loaded = Skill(20); loaded.Pawn.thingIDNumber = saved.Pawn.thingIDNumber;
        Scribe.loader.InitLoading(path); loaded.ExposeData(); Scribe.loader.FinalizeLoading();
        Check("GM XP retains double precision through actual Scribe", GrandmasterStore.Get(loaded) == 900000123.25);
        Check("no Insight on save/load and no new saved aspirant state", GrandmasterStore.Get(saved) == GrandmasterStore.Get(loaded)
            && !xml.Contains("aspirant") && !xml.Contains("insight") && !xml.Contains("random"));
        Check("save replay preserves the next local draw", Roll(loaded.Pawn.thingIDNumber, loaded.def.defName, GrandmasterStore.Get(loaded) + 1)
            == Roll(saved.Pawn.thingIDNumber, saved.def.defName, GrandmasterStore.Get(saved) + 1));
        var xpBefore = GrandmasterStore.Get(loaded); fixedDraw = 0.99; loaded.Learn(0.25f, true, true);
        Check("small XP still increments a near-billion bank", GrandmasterStore.Get(loaded) == xpBefore + 0.25);
        GrandmasterStore.Clear(loaded);
        Scribe.saver.InitSaving(path, "skill"); loaded.ExposeData(); Scribe.saver.FinalizeSaving();
        Check("uninstall Clear leaves no additional progression state", GrandmasterStore.Get(loaded) == 0 && !File.ReadAllText(path).Contains("grandmasterXp"));
        var key = XDocument.Load(Path.Combine(args[0], "Languages/English/Keyed/Grandmaster21.xml")).Root.Element("GM21_GrandmasterInsight");
        Check("Insight localization key contains all message arguments", key != null && key.Value.Contains("{0}") && key.Value.Contains("{1}") && key.Value.Contains("{2}"));
        foreach (string log in logs) Console.WriteLine("CAPTURED LOG: " + log);
        Check("no production errors during normal support", logs.Count == 0);

        // Fail closed when a different transpiler removes/changes the verified saturation site.
        var none = Patch_SkillRecord_LearnRateFactor.Transpiler(new List<CodeInstruction> { new CodeInstruction(System.Reflection.Emit.OpCodes.Ret) }).ToList();
        Check("missing branch disables support without changing input IL", !Patch_SkillRecord_LearnRateFactor.Applied && none.Count == 1);
        gm.Unpatch(AccessTools.Method(typeof(SkillRecord), "LearnRateFactor"), HarmonyPatchType.Transpiler, gm.Id);
        gm.CreateClassProcessor(typeof(Patch_SkillRecord_LearnRateFactor)).Patch();
        Check("verified reapplication restores support", Patch_SkillRecord_LearnRateFactor.Applied);
        Console.WriteLine("PASS: " + pass + " FAIL: " + fail);
        Environment.Exit(fail == 0 ? 0 : 1);
    }
}
