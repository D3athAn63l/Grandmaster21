// Cooking 21 real-DLL checks. Only game-world/native services are fixture shims; the installed Cooking
// hooks and the REAL vanilla methods they attach to execute: CompFoodPoisonable.Notify_RecipeProduced /
// PreAbsorbStack / PostSplitOff, ThingWithComps.TryAbsorbStack / SplitOff, Thing.Ingested,
// CompRottable.TickInterval / TicksUntilRotAtTemp, GenRecipe.MakeRecipeProducts and the real Scribe
// saver/loader. See Docs/Cooking21.md.
//
//   mono cooking.exe <Assembly-CSharp.dll> <scratch dir>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using Grandmaster21;
using HarmonyLib;
using Mono.Cecil;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

class CookingChecks
{
    static int pass, fail;
    static readonly Type Startup = typeof(Gm21Mod).Assembly.GetType("Grandmaster21.Gm21CookingStartup");
    static readonly Type Patches = typeof(Gm21Mod).Assembly.GetType("Grandmaster21.Gm21CookingPatches");
    static readonly Type Cooking = typeof(Gm21Mod).Assembly.GetType("Grandmaster21.Gm21Cooking");
    static readonly Type Purify = typeof(Gm21Mod).Assembly.GetType("Grandmaster21.Gm21PurifyFood");
    static string scratch;

    // ---- fixture state
    static int nextId = 1000, randCalls, memories;
    static readonly Queue<bool> chanceScript = new Queue<bool>();
    static bool chanceDefault;
    static float ambient = 21f, statValue;
    static readonly List<ThoughtDef> gained = new List<ThoughtDef>();
    static readonly HashSet<Thing> spawned = new HashSet<Thing>();
    static Map map, otherMap;
    static readonly HashSet<Thing> elsewhere = new HashSet<Thing>(); // spawned, but on the other map
    static ThingDef mealDef;

    static void Check(string n, bool ok) { Console.WriteLine((ok ? "PASS " : "FAIL ") + n); if (ok) pass++; else fail++; }
    static T Empty<T>() { return (T)FormatterServices.GetUninitializedObject(typeof(T)); }
    static object Call(Type t, string name, params object[] args) { return AccessTools.Method(t, name).Invoke(null, args); }
    static void Set(object o, string n, object v) { AccessTools.Field(o.GetType(), n).SetValue(o, v); }
    static object Get(object o, string n) { return AccessTools.Field(o.GetType(), n).GetValue(o); }
    static HarmonyMethod Hook(string n) { return new HarmonyMethod(AccessTools.Method(typeof(CookingChecks), n)); }
    static void Prefix(Harmony h, Type t, string n, string hook, params Type[] args)
    { h.Patch(AccessTools.Method(t, n, args.Length == 0 ? null : args), prefix: Hook(hook)); }
    static void Getter(Harmony h, Type t, string n, string hook)
    { h.Patch(AccessTools.PropertyGetter(t, n), prefix: Hook(hook)); }

    // ---- environment shims (test process only; none touches a decision under test)
    public static bool Skip() { return false; }
    public static bool No(ref bool __result) { __result = false; return false; }
    public static bool GiveId(Thing t) { t.thingIDNumber = nextId++; return false; }
    public static bool Chance(float chance, ref bool __result)
    { randCalls++; __result = chanceScript.Count > 0 ? chanceScript.Dequeue() : chanceDefault; return false; }
    public static bool Stat(StatDef stat, ref float __result) { __result = statValue; return false; }
    public static bool Room(ref Room __result) { __result = null; return false; }
    static int aptitudeShim;
    public static bool AptitudeStub(ref int __result) { __result = aptitudeShim; return false; }
    public static bool TraverseStub(ref TraverseParms __result) { __result = default(TraverseParms); return false; }
    public static bool DrawPosStub(ref Vector3 __result) { __result = Vector3.zero; return false; }
    public static bool LogText(string text)
    {
        if (!text.StartsWith("Could not load shader ")) Console.WriteLine("GAME " + text); // the shader database's headless fallback
        return false;
    }
    static int errors;
    public static bool LogError(string text) { errors++; Console.WriteLine("GAME ERROR " + text); return false; }
    public static bool LogErrorOnce(string text, int key) { errors++; Console.WriteLine("GAME ERROR " + text); return false; }
    public static bool Ambient(ref float __result) { __result = ambient; return false; }
    public static bool SpawnedGet(Thing __instance, ref bool __result) { __result = spawned.Contains(__instance); return false; }
    public static bool MapGet(Thing __instance, ref Map __result) { __result = elsewhere.Contains(__instance) ? otherMap : spawned.Contains(__instance) ? map : null; return false; }
    public static bool Ticks(ref TickManager __result) { __result = Empty<TickManager>(); return false; }
    static FactionManager factions;
    public static bool Factions(ref FactionManager __result)
    {
        if (factions == null) { factions = Empty<FactionManager>(); Set(factions, "allFactions", new List<Faction>()); }
        __result = factions; return false;
    }
    public static bool Gain(ThoughtDef def) { gained.Add(def); memories++; return false; }
    public static bool Thoughts(ref List<FoodUtility.ThoughtFromIngesting> __result)
    { __result = new List<FoodUtility.ThoughtFromIngesting>(); return false; }
    public static bool Nutrition(ref float __result) { __result = 1f; return false; }
    public static bool ZeroFactor(ref float __result) { __result = 0f; return false; }
    public static bool NoOverride(ref float poisonChanceOverride, ref bool __result)
    { poisonChanceOverride = 0f; __result = false; return false; }
    public static bool IdeologyInactive() { return false; }
    public static IEnumerable<CodeInstruction> ShimIdeology(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo getter = AccessTools.PropertyGetter(typeof(ModsConfig), "IdeologyActive");
        foreach (CodeInstruction i in instructions)
        {
            if (Equals(i.operand, getter)) { i.opcode = OpCodes.Call; i.operand = AccessTools.Method(typeof(CookingChecks), "IdeologyInactive"); }
            yield return i;
        }
    }

    // A real install ships NAudio beside Assembly-CSharp; the headless copy does not, so a few types fail to load.
    static IEnumerable<Type> LoadableTypes(Assembly a)
    {
        try { return a.GetTypes(); }
        catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null).ToArray(); }
    }

    // ---- job-simulation shims: pathing, reservations and the map search are game-world services; the JobDriver
    // machinery (DriverTick, toils, JumpToToil, Notify_PatherFailed) and every Cooking decision run for real.
    static Faction playerFaction;
    static UniqueIDsManager uniqueIds;
    // RESERVATIONS ARE THE REAL VANILLA ReservationManager (Reserve, CanReserve, the playerForced takeover, the
    // RespectsReservationsOf rule, ReleaseClaimedBy). Only two things about them are shimmed: the manager's
    // static constructor (it loads one Unity debug icon) and, for the tests that need a claim to be genuinely
    // impossible, a veto gate in front of the utility wrappers.
    static readonly HashSet<Thing> unreachable = new HashSet<Thing>();
    static readonly HashSet<Thing> vetoClaim = new HashSet<Thing>();   // Reserve/CanReserve refuse these, whatever the rules say
    static readonly List<Thing> field = new List<Thing>();
    static readonly List<string> messages = new List<string>();
    static readonly List<Thing> walked = new List<Thing>();
    static readonly List<Faction[]> hostilePairs = new List<Faction[]>();
    static readonly List<KeyValuePair<Job, JobCondition>> endLog = new List<KeyValuePair<Job, JobCondition>>();
    static int scans, pathStarts, endCount, thrown, reserveCalls;
    static JobCondition endedWith;
    static Thing pathTarget, raceOnce;
    static Pawn raceHolder;
    static bool pathPending, capable = true, failEveryReserve;
    static Job ordered;
    static JobDef ingestDef, haulDef;

    public static bool Player(ref Faction __result) { __result = playerFaction; return false; }
    public static bool UniqueIds(ref UniqueIDsManager __result) { if (uniqueIds == null) uniqueIds = Empty<UniqueIDsManager>(); __result = uniqueIds; return false; }
    public static bool Capable(ref bool __result) { __result = capable; return false; }
    public static bool Label(ref string __result) { __result = "Label"; return false; }
    public static bool Busy(ref bool __result) { __result = false; return false; }
    public static bool CanReachStub(object[] __args, ref bool __result)
    {
        var dest = __args[1] is LocalTargetInfo ? (LocalTargetInfo)__args[1] : (LocalTargetInfo)__args[2];
        __result = dest.Thing == null || !unreachable.Contains(dest.Thing); return false;
    }
    // Vanilla's Faction.HostileTo reads the relation table; the fixture states which factions are hostile.
    public static bool HostileStub(Faction fac, Faction other, ref bool __result)
    { __result = hostilePairs.Any(p => (p[0] == fac && p[1] == other) || (p[0] == other && p[1] == fac)); return false; }
    // In front of the REAL Reserve / CanReserve: count the calls and let a test make a claim impossible. Returning true runs vanilla.
    public static bool ReserveGate(object[] __args, ref bool __result)
    {
        var t = ((LocalTargetInfo)__args[1]).Thing; reserveCalls++;
        if (failEveryReserve || (t != null && vetoClaim.Contains(t))) { __result = false; return false; }
        return true;
    }
    public static bool CanReserveGate(object[] __args, ref bool __result)
    {
        var t = ((LocalTargetInfo)__args[1]).Thing;
        if (t != null && vetoClaim.Contains(t)) { __result = false; return false; }
        return true;
    }
    // Vanilla's Thing.Destroy releases every reservation on the thing (Find.Maps is not available here; the fixture has one map).
    public static bool ReleaseOnDestroy(Thing __instance)
    {
        if (map != null && map.reservationManager != null) map.reservationManager.ReleaseAllForTarget(__instance);
        return false;
    }
    public static bool NoResource(ref UnityEngine.Object __result) { __result = null; return false; }
    public static bool NoShader(ref Shader __result) { __result = null; return false; }
    public static bool MaterialStub(ref Material __result) { __result = null; return false; } // ReservationManager's static debug icon
    public static bool Closest(object[] __args, ref Thing __result)
    {
        scans++;
        var root = (IntVec3)__args[0]; var validator = (Predicate<Thing>)__args[6];
        Thing best = null; int bestDistance = int.MaxValue;
        foreach (Thing t in field)
        {
            if (t.Destroyed || unreachable.Contains(t)) continue;
            if (validator != null && !validator(t)) continue;
            int d = (t.Position - root).LengthHorizontalSquared;
            if (d < bestDistance) { bestDistance = d; best = t; }
        }
        if (best != null && best == raceOnce) { Occupy(raceHolder, best, "haul", false); raceOnce = null; } // an ordinary pawn claims it right after the search
        __result = best; return false;
    }
    public static bool StartPathStub(LocalTargetInfo dest) { pathStarts++; pathTarget = dest.Thing; walked.Add(dest.Thing); pathPending = true; return false; }
    public static bool EndJobStub(Pawn_JobTracker __instance, JobCondition condition)
    {
        // What vanilla's CleanupCurrentJob does that matters here: the driver is marked ended BEFORE it is dropped
        // (TryActuallyStartNextToil checks it), and the job's reservations are released through the real manager.
        Job job = __instance.curJob;
        Pawn owner = (Pawn)AccessTools.Field(typeof(Pawn_JobTracker), "pawn").GetValue(__instance);
        endLog.Add(new KeyValuePair<Job, JobCondition>(job, condition));
        if (job != null && Gm21CookingDefOf.IsPurifyJob(job.def)) { endCount++; endedWith = condition; }
        if (__instance.curDriver != null) __instance.curDriver.ended = true;
        if (job != null) map.reservationManager.ReleaseClaimedBy(owner, job);
        __instance.curJob = null; __instance.curDriver = null; return false;
    }
    public static bool OrderedStub(object[] __args, ref bool __result) { ordered = (Job)__args[0]; __result = true; return false; }
    public static bool ToilPassThrough(object[] __args, ref Toil __result) { __result = (Toil)__args[0]; return false; }
    public static bool MoteStub() { thrown++; return false; }
    // FloatMenuOption's constructor measures text with Unity's Text class; the entry's label and action are what matter.
    static readonly List<KeyValuePair<string, Action>> menuEntries = new List<KeyValuePair<string, Action>>();
    public static bool MenuEntryStub(object[] __args) { menuEntries.Add(new KeyValuePair<string, Action>((string)__args[0], (Action)__args[1])); return false; }
    // The language worker is a live-game service: a formatted translation reads as its key (plus arguments elsewhere).
    public static bool TranslateStub(object[] __args, ref TaggedString __result) { __result = (string)__args[0]; return false; }
    public static bool MessageStub(object[] __args) { messages.Add(__args[0] as string); return false; }
    static bool RemovedMemories;
    public static bool RemoveMemoriesStub(MemoryThoughtHandler __instance, ThoughtDef def)
    {
        var list = (List<Thought_Memory>)AccessTools.Field(typeof(MemoryThoughtHandler), "memories").GetValue(__instance);
        list.RemoveAll(m => m.def == def); RemovedMemories = true; return false;
    }

    static void PurifyEnvironment(Harmony h)
    {
        Getter(h, typeof(Faction), "OfPlayer", "Player");
        Getter(h, typeof(Find), "UniqueIDsManager", "UniqueIds");
        Getter(h, typeof(Pawn_StanceTracker), "FullBodyBusy", "Busy");
        Getter(h, typeof(Entity), "LabelShortCap", "Label");
        Prefix(h, typeof(PawnCapacitiesHandler), "CapableOf", "Capable", typeof(PawnCapacityDef));
        foreach (MethodInfo m in typeof(ReachabilityUtility).GetMethods().Where(x => x.Name == "CanReach")) h.Patch(m, prefix: Hook("CanReachStub"));
        h.Patch(AccessTools.Method(typeof(ReservationUtility), "CanReserve"), prefix: Hook("CanReserveGate"));
        h.Patch(AccessTools.Method(typeof(ReservationUtility), "Reserve"), prefix: Hook("ReserveGate"));
        h.Patch(AccessTools.Method(typeof(FactionUtility), "HostileTo", new[] { typeof(Faction), typeof(Faction) }), prefix: Hook("HostileStub"));
        h.Patch(AccessTools.Method(typeof(GenClosest), "ClosestThingReachable"), prefix: Hook("Closest"));
        h.Patch(typeof(TraverseParms).GetMethods().First(m => m.Name == "For" && m.GetParameters()[0].ParameterType == typeof(Pawn)), prefix: Hook("TraverseStub"));
        h.Patch(AccessTools.Method(typeof(Pawn_PathFollower), "StartPath"), prefix: Hook("StartPathStub"));
        h.Patch(AccessTools.Method(typeof(Pawn_PathFollower), "StopDead"), prefix: Hook("Skip"));
        h.Patch(AccessTools.Method(typeof(Pawn_JobTracker), "EndCurrentJob"), prefix: Hook("EndJobStub"));
        h.Patch(AccessTools.Method(typeof(Pawn_JobTracker), "TryTakeOrderedJob"), prefix: Hook("OrderedStub"));
        foreach (MethodInfo m in typeof(ToilEffects).GetMethods().Where(x => x.Name.StartsWith("WithProgressBar") || x.Name == "PlaySustainerOrSound"))
            h.Patch(m, prefix: Hook("ToilPassThrough"));
        foreach (MethodInfo m in typeof(MoteMaker).GetMethods().Where(x => x.Name == "ThrowText")) h.Patch(m, prefix: Hook("MoteStub"));
        foreach (ConstructorInfo c in typeof(FloatMenuOption).GetConstructors().Where(x => x.GetParameters().Length >= 2 && x.GetParameters()[0].ParameterType == typeof(string)))
            h.Patch(c, prefix: Hook("MenuEntryStub"));
        foreach (MethodInfo m in typeof(Messages).GetMethods().Where(x => x.Name == "Message" && x.GetParameters()[0].ParameterType == typeof(string)))
            h.Patch(m, prefix: Hook("MessageStub"));
        h.Patch(AccessTools.Method(typeof(MemoryThoughtHandler), "RemoveMemoriesOfDef"), prefix: Hook("RemoveMemoriesStub"));
        foreach (MethodInfo m in typeof(TranslatorFormattedStringExtensions).GetMethods().Where(x => x.Name == "Translate" && x.GetParameters().Length >= 2))
            h.Patch(m, prefix: Hook("TranslateStub"));
        h.Patch(AccessTools.Method(typeof(Translator), "Translate", new[] { typeof(string) }), prefix: Hook("TranslateStub"));
        SoundDefOf.Interact_CleanFilth = new SoundDef { defName = "Interact_CleanFilth" };
    }

    static void Environment(Harmony h)
    {
        // Vanilla ReservationManager's static initialiser builds one debug Material from a shader; neither exists
        // headless. Both shader sources (Unity's native resource loader and the mod asset bundles) answer "no
        // shader", which the shader database falls back from, and the one material lookup is stubbed. That lets
        // the REAL manager run. (Patching ShaderDatabase itself would run its static initialiser first.)
        h.Patch(AccessTools.Method(typeof(Resources), "Load", new[] { typeof(string), typeof(System.Type) }), prefix: Hook("NoResource"));
        h.Patch(AccessTools.Method(typeof(ContentFinder<Shader>), "TryFindAssetInModBundles"), prefix: Hook("NoShader"));
        h.Patch(AccessTools.Method(typeof(MaterialPool), "MatFrom", new[] { typeof(string), typeof(Shader) }), prefix: Hook("MaterialStub"));
        Prefix(h, typeof(Log), "Message", "LogText", typeof(string));
        Prefix(h, typeof(Log), "Warning", "LogText", typeof(string));
        Prefix(h, typeof(Log), "Error", "LogError", typeof(string));
        Prefix(h, typeof(Log), "ErrorOnce", "LogErrorOnce", typeof(string), typeof(int));
        Prefix(h, typeof(DefOfHelper), "EnsureInitializedInCtor", "Skip");
        foreach (var t in new[] { typeof(SkillDefOf), typeof(PawnCapacityDefOf), typeof(StatDefOf), typeof(RoomStatDefOf), typeof(DamageDefOf) })
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(t.TypeHandle);
        SkillDefOf.Cooking = new SkillDef { defName = "Cooking" };
        SkillDefOf.Melee = new SkillDef { defName = "Melee" };
        PawnCapacityDefOf.Manipulation = new PawnCapacityDef { defName = "Manipulation" };
        PawnCapacityDefOf.Consciousness = new PawnCapacityDef { defName = "Consciousness" };
        StatDefOf.FoodPoisonChance = new StatDef { defName = "FoodPoisonChance" };
        RoomStatDefOf.FoodPoisonChance = new RoomStatDef { defName = "FoodPoisonChance", roomlessScore = 0.1f };

        Prefix(h, typeof(ThingIDMaker), "GiveIDTo", "GiveId");
        Prefix(h, typeof(Rand), "Chance", "Chance", typeof(float));
        Prefix(h, typeof(StatExtension), "GetStatValue", "Stat", typeof(Thing), typeof(StatDef), typeof(bool), typeof(int));
        Getter(h, typeof(RaceProperties), "IsMechanoid", "No");
        Prefix(h, typeof(ReliquaryUtility), "IsRelic", "No", typeof(Thing)); // ModsConfig cannot initialise headless
        h.Patch(AccessTools.Method(typeof(Thing), "RemoveAllReservationsAndDesignationsOnThis"), prefix: Hook("ReleaseOnDestroy")); // Destroy: one map here
        h.Patch(AccessTools.Method(typeof(DeepProfiler), "Start"), prefix: Hook("Skip")); // Scribe's loader profiles itself
        h.Patch(AccessTools.Method(typeof(DeepProfiler), "End"), prefix: Hook("Skip"));
        Getter(h, typeof(Thing), "AmbientTemperature", "Ambient");
        Getter(h, typeof(Thing), "Spawned", "SpawnedGet");
        Getter(h, typeof(Thing), "Map", "MapGet");
        Getter(h, typeof(Find), "TickManager", "Ticks");
        Getter(h, typeof(SkillRecord), "Aptitude", "AptitudeStub");
        Getter(h, typeof(Thing), "DrawPos", "DrawPosStub");
        Getter(h, typeof(Find), "FactionManager", "Factions"); // Thing.ExposeData resolves its faction reference here
        // pawn.GetRoom(): a pawn standing nowhere in particular has no room, so vanilla uses the roomless score.
        MethodInfo getRoom = LoadableTypes(typeof(Thing).Assembly).SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .First(m => m.Name == "GetRoom" && m.GetParameters().Length >= 1 && m.GetParameters()[0].ParameterType == typeof(Thing));
        h.Patch(getRoom, prefix: Hook("Room"));
        // Ingestion environment: the thought pipeline and stats are outside the decision under test.
        Prefix(h, typeof(MemoryThoughtHandler), "TryGainMemory", "Gain", typeof(ThoughtDef), typeof(Pawn), typeof(Precept));
        Prefix(h, typeof(FoodUtility), "ThoughtsFromIngesting", "Thoughts");
        Prefix(h, typeof(FoodUtility), "NutritionForEater", "Nutrition");
        Prefix(h, typeof(FoodUtility), "GetFoodPoisonChanceFactor", "ZeroFactor");
        Prefix(h, typeof(FoodUtility), "TryGetFoodPoisoningChanceOverrideFromTraits", "NoOverride");
        h.Patch(AccessTools.Method(typeof(Thing), "Ingested"), transpiler: Hook("ShimIdeology"));
        PurifyEnvironment(h);
    }

    // ---- fixtures
    static ThingDef MakeDef(string name, int stack, bool poisonable = true, bool rottable = true, bool ingestible = true,
                            Type poisonComp = null, Type thingClass = null, int maxIngest = 1)
    {
        // ThingDef's constructor loads Unity shaders; the uninitialised object plus the fields the code reads is enough.
        var def = Empty<ThingDef>();
        def.defName = name; def.category = ThingCategory.Item; def.thingClass = thingClass ?? typeof(ThingWithComps);
        def.stackLimit = stack; def.useHitPoints = false; def.destroyable = true; def.comps = new List<CompProperties>();
        if (poisonable)
        {
            var poison = new CompProperties_FoodPoisonable();
            if (poisonComp != null) poison.compClass = poisonComp;
            def.comps.Add(poison);
        }
        if (rottable) def.comps.Add(new CompProperties_Rottable { daysToRotStart = 4f, daysToDessicated = 8f, rotDestroys = false });
        if (ingestible) def.ingestible = new IngestibleProperties { foodType = FoodTypeFlags.Meal, maxNumToIngestAtOnce = maxIngest };
        DefDatabase<ThingDef>.Add(def);
        return def;
    }

    class ModdedPoison : CompFoodPoisonable { }

    static RecipeDef Recipe(string name, params ThingDefCountClass[] products)
    {
        var r = Empty<RecipeDef>(); r.defName = name; r.products = products.ToList();
        return r;
    }

    static ThingWithComps Meal(ThingDef def, int count, int masterful = 0)
    {
        var t = (ThingWithComps)ThingMaker.MakeThing(def);
        t.stackCount = count;
        SetMasterful(t, masterful);
        return t;
    }
    static CompGrandmasterMeal MealComp(Thing t) { return t.TryGetComp<CompGrandmasterMeal>(); }
    static void SetMasterful(Thing t, int n)
    {
        CompGrandmasterMeal c = MealComp(t);
        if (c != null) AccessTools.Method(typeof(CompGrandmasterMeal), "SetMasterful").Invoke(c, new object[] { n });
    }
    static int M(Thing t) { return MealComp(t).MasterfulCount; }
    static CompRottable Rot(Thing t) { return t.TryGetComp<CompRottable>(); }

    static Pawn Cook(int cookingLevel)
    {
        var p = Empty<Pawn>();
        p.thingIDNumber = nextId++;
        p.def = Empty<ThingDef>(); p.def.race = Empty<RaceProperties>();
        p.skills = Empty<Pawn_SkillTracker>();
        p.skills.skills = new List<SkillRecord> { new SkillRecord { def = SkillDefOf.Cooking, levelInt = cookingLevel } };
        return p;
    }

    static Pawn Eater()
    {
        var p = Empty<Pawn>();
        p.thingIDNumber = nextId++;
        p.def = Empty<ThingDef>(); p.def.race = Empty<RaceProperties>();
        p.mindState = Empty<Pawn_MindState>();
        p.needs = Empty<Pawn_NeedsTracker>(); Set(p.needs, "needs", new List<Need>());
        p.needs.mood = Empty<Need_Mood>(); p.needs.mood.thoughts = Empty<ThoughtHandler>(); p.needs.mood.thoughts.memories = Empty<MemoryThoughtHandler>();
        p.health = Empty<Pawn_HealthTracker>(); Set(p.health, "pawn", p); Set(p.health, "healthState", PawnHealthState.Mobile);
        p.health.hediffSet = Empty<HediffSet>(); p.health.hediffSet.hediffs = new List<Hediff>();
        p.carryTracker = Empty<Pawn_CarryTracker>(); p.carryTracker.innerContainer = new ThingOwner<Thing>(p.carryTracker);
        return p;
    }

    static bool IsPoisoned(Thing t) { return t.TryGetComp<CompFoodPoisonable>().PoisonPercent > 0f; }

    // ================================================================= sections
    static void Audit(string dll)
    {
        using (var module = ModuleDefinition.ReadModule(dll))
        {
            TypeDefinition T(string n) { return module.GetType(n); }
            MethodDefinition Mth(TypeDefinition t, string n) { return t.Methods.Single(x => x.Name == n); }
            int Calls(MethodDefinition m, string name) { return m.Body.Instructions.Count(i => i.Operand is MethodReference && ((MethodReference)i.Operand).FullName.Contains(name)); }
            bool Order(MethodDefinition m, string first, string second)
            {
                var ins = m.Body.Instructions.ToList();
                int a = ins.FindIndex(i => i.Operand is MethodReference && ((MethodReference)i.Operand).FullName.Contains(first));
                int b = ins.FindIndex(i => i.Operand is MethodReference && ((MethodReference)i.Operand).FullName.Contains(second));
                return a >= 0 && b > a;
            }
            var poison = T("RimWorld.CompFoodPoisonable"); var rot = T("RimWorld.CompRottable");
            var twc = T("Verse.ThingWithComps"); var thing = T("Verse.Thing"); var gen = T("Verse.GenRecipe");
            Check("audit: CompFoodPoisonable overrides Notify_RecipeProduced and rolls twice (kitchen, cook)", poison.Methods.Any(m => m.Name == "Notify_RecipeProduced" && m.IsVirtual) && Calls(Mth(poison, "Notify_RecipeProduced"), "Rand::Chance") == 2);
            Check("audit: poison fields are private float poisonPct and enum cause", poison.Fields.Any(f => f.Name == "poisonPct" && f.FieldType.FullName == "System.Single") && poison.Fields.Any(f => f.Name == "cause" && f.FieldType.FullName == "RimWorld.FoodPoisonCause"));
            Check("audit: vanilla exposes SetPoisoned but no clearing method", poison.Methods.Any(m => m.Name == "SetPoisoned") && !poison.Methods.Any(m => m.Name.IndexOf("Clear", StringComparison.OrdinalIgnoreCase) >= 0 || m.Name.IndexOf("Cure", StringComparison.OrdinalIgnoreCase) >= 0));
            Check("audit: TryAbsorbStack runs every comp's PreAbsorbStack BEFORE base.TryAbsorbStack", Order(Mth(twc, "TryAbsorbStack"), "ThingComp::PreAbsorbStack", "Thing::TryAbsorbStack"));
            Check("audit: SplitOff runs base.SplitOff BEFORE every comp's PostSplitOff", Order(Mth(twc, "SplitOff"), "Thing::SplitOff", "ThingComp::PostSplitOff"));
            Check("audit: base SplitOff returns this for a whole-stack split (piece == parent)", Mth(thing, "SplitOff").Body.Instructions.Any(i => i.OpCode.Code == Mono.Cecil.Cil.Code.Ldarg_0) && Calls(Mth(thing, "SplitOff"), "ThingMaker::MakeThing") == 1);
            Check("audit: Thing.Destroy zeroes stackCount of a destroyed non-pawn", Mth(thing, "Destroy").Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "stackCount"));
            var ing = Mth(thing, "Ingested");
            Check("audit: Ingested calls SplitOff for a partial meal and PostIngested last", Calls(ing, "Thing::SplitOff") == 1 && Order(ing, "Thing::SplitOff", "Thing::PostIngested"));
            Check("audit: CompRottable.TickInterval calls RotRateAtTemperature exactly once", Calls(Mth(rot, "TickInterval"), "GenTemperature::RotRateAtTemperature") == 1);
            Check("audit: CompRottable.TicksUntilRotAtTemp calls RotRateAtTemperature exactly once", Calls(Mth(rot, "TicksUntilRotAtTemp"), "GenTemperature::RotRateAtTemperature") == 1);
            Check("audit: only TickInterval advances RotProgress from a rate", rot.Methods.Where(m => m.HasBody && m.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "set_RotProgress")).Select(m => m.Name).OrderBy(n => n).SequenceEqual(new[] { "PostSplitOff", "PreAbsorbStack", "RotImmediately", "TickInterval" }));
            var moveNext = gen.NestedTypes.SelectMany(n => n.Methods).Where(m => m.Name == "MoveNext" && m.HasBody).ToList();
            Check("audit: MakeRecipeProducts sets stackCount, THEN Notify_RecipeProduced, in the recipe-products loop", moveNext.Any(m => Order(m, "ThingMaker::MakeThing", "Notify_RecipeProduced") && m.Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "stackCount")));

            // ---- reservations: the vanilla facts the forced-Purify design rests on (1.6 assembly, pinned by hash in the verify scripts)
            IEnumerable<TypeDefinition> Flatten(TypeDefinition t) { yield return t; foreach (var n in t.NestedTypes.SelectMany(Flatten)) yield return n; }
            var everyType = module.Types.SelectMany(Flatten).ToList();
            var resMgr = module.GetType("Verse.AI.ReservationManager"); var jobTracker = module.GetType("Verse.AI.Pawn_JobTracker");
            var reserve = resMgr.Methods.Single(m => m.Name == "Reserve");
            Check("audit: ReservationManager.Reserve takes over for a playerForced job (reads Job.playerForced, re-asks CanReserve ignoring others, ends the other job via EndCurrentOrQueuedJob)",
                reserve.Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "playerForced")
                && Calls(reserve, "ReservationManager::CanReserve") >= 2 && Calls(reserve, "Pawn_JobTracker::EndCurrentOrQueuedJob") >= 1 && Calls(reserve, "ReservationManager::RespectsReservationsOf") >= 1);
            Check("audit: Pawn_JobTracker.TryTakeOrderedJob itself sets playerForced and reserves (TryMakePreToilReservations) at order time",
                jobTracker.Methods.Single(m => m.Name == "TryTakeOrderedJob").Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "playerForced" && i.OpCode.Code == Mono.Cecil.Cil.Code.Stfld)
                && Calls(jobTracker.Methods.Single(m => m.Name == "TryTakeOrderedJob"), "Job::TryMakePreToilReservations") >= 1);
            var releasers = new SortedSet<string>();
            foreach (var t in everyType)
                foreach (var m in t.Methods.Where(x => x.HasBody))
                    foreach (var i in m.Body.Instructions)
                    {
                        var r = i.Operand as MethodReference;
                        if (r != null && r.DeclaringType.Name == "ReservationManager" && r.Name.StartsWith("Release"))
                            releasers.Add(r.Name + " <- " + (t.DeclaringType != null ? t.DeclaringType.FullName : t.FullName) + "." + (t.DeclaringType != null || m.Name.StartsWith("<") ? "(closure)" : m.Name));
                    }
            var expectedReleasers = new[]
            {
                // The owning job's own releases (pick-up toils, Toils_Reserve.Release*), a job's end/cleanup, a pawn's
                // clear-all, a destroyed Thing, and two Deliver...ToAltar/Cell pre-toil reservations of a PAWN target.
                "Release <- RimWorld.JobDriver_TransferBetweenEntityHolders.(closure)",
                "Release <- RimWorld.Toils_Ingest.(closure)",
                "Release <- RimWorld.Toils_Tend.(closure)",
                "Release <- Verse.AI.JobDriver_HaulMechToCharger.(closure)",
                "Release <- Verse.AI.Pawn_JobTracker.ReleaseReservations",
                "Release <- Verse.AI.Toils_Haul.(closure)",
                "Release <- Verse.AI.Toils_Reserve.(closure)",
                "ReleaseAllClaimedBy <- Verse.Pawn.ClearAllReservations",
                "ReleaseAllForTarget <- RimWorld.JobDriver_DeliverPawnToAltar.TryMakePreToilReservations",
                "ReleaseAllForTarget <- RimWorld.JobDriver_DeliverPawnToCell.TryMakePreToilReservations",
                "ReleaseAllForTarget <- Verse.Thing.RemoveAllReservationsAndDesignationsOnThis",
                "ReleaseClaimedBy <- Verse.Pawn.ClearReservationsForJob",
            };
            Check("audit: the ONLY code in the 1.6 assembly that removes a reservation is the owning job's own releases, a job/pawn cleanup, a destroyed Thing, and a forced Reserve -- none silently drops a running job's reservation, so Purify needs no per-tick ownership check (" + releasers.Count + " releaser sites)",
                expectedReleasers.All(x => releasers.Contains(x)) && releasers.All(x => expectedReleasers.Contains(x)));
            foreach (var r in releasers.Where(x => !expectedReleasers.Contains(x))) Console.WriteLine("  (unexpected releaser site: " + r + ")");
            foreach (var r in expectedReleasers.Where(x => !releasers.Contains(x))) Console.WriteLine("  (missing releaser site: " + r + ")");
        }
        using (var module = ModuleDefinition.ReadModule(typeof(Gm21Mod).Assembly.Location))
        {
            var cooking = module.Types.Where(t => t.FullName.StartsWith("Grandmaster21.") && (t.Name.StartsWith("Gm21Cooking") || t.Name.StartsWith("CompGrandmasterMeal") || t.Name.StartsWith("Gm21Purify") || t.Name.StartsWith("JobDriver_Gm21Purify") || t.Name.StartsWith("Gm21Masterful") || t.Name.StartsWith("Command_Gm21Purify") || t.Name.StartsWith("Gm21PurifyGizmo")));
            var methods = cooking.SelectMany(t => t.Methods).Where(m => m.HasBody).ToList();
            Check("production Cooking logic never reads a DefName (the dev report's display text aside)", !methods.Where(m => m.DeclaringType.Name != "Gm21CookingDebugActions").SelectMany(m => m.Body.Instructions).Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "defName"));
            Check("production Cooking code never touches ThingDef comps outside the one startup attach", methods.Where(m => m.DeclaringType.Name != "Gm21CookingStartup").SelectMany(m => m.Body.Instructions).All(i => !(i.Operand is FieldReference) || ((FieldReference)i.Operand).Name != "comps"));
            var seam = cooking.SelectMany(t => t.Methods).First(m => m.DeclaringType.Name == "Gm21PurifyFood" && m.Name == "Release");
            var seamCalls = seam.Body.Instructions.Where(i => i.Operand is MethodReference).Select(i => ((MethodReference)i.Operand).DeclaringType.Name + "::" + ((MethodReference)i.Operand).Name).ToList();
            Check("production release seam releases only a reservation this pawn holds for this job (ReservedBy, then Release, on vanilla's manager)",
                seamCalls.IndexOf("ReservationManager::ReservedBy") >= 0 && seamCalls.IndexOf("ReservationManager::Release") > seamCalls.IndexOf("ReservationManager::ReservedBy"));
            IEnumerable<TypeDefinition> Nest(TypeDefinition t) { yield return t; foreach (var n in t.NestedTypes.SelectMany(Nest)) yield return n; }
            var everyMethod = cooking.SelectMany(Nest).SelectMany(t => t.Methods).Where(m => m.HasBody).ToList();
            string Where(MethodDefinition m) { return (m.DeclaringType.DeclaringType ?? m.DeclaringType).Name + "." + m.Name; }
            var canReserveSites = everyMethod.SelectMany(m => m.Body.Instructions.Select((ins, ix) => new { m, ins, ix })).Where(x => x.ins.Operand is MethodReference && ((MethodReference)x.ins.Operand).Name == "CanReserve").ToList();
            Check("production: the ONLY reservation query is Gm21PurifyFood.CanClaim, and it always asks with ignoreOtherReservations = true (vanilla's forced-reservation capability; no other bypass)",
                canReserveSites.Count == 1 && Where(canReserveSites[0].m) == "Gm21PurifyFood.CanClaim" && canReserveSites[0].m.Body.Instructions[canReserveSites[0].ix - 1].OpCode.Code == Mono.Cecil.Cil.Code.Ldc_I4_1);
            var reserveSites = everyMethod.SelectMany(m => m.Body.Instructions.Select((ins, ix) => new { m, ins, ix })).Where(x => x.ins.Operand is MethodReference && ((MethodReference)x.ins.Operand).Name == "Reserve").ToList();
            Check("production: every Reserve is the plain pawn.Reserve of the job driver with ignoreOtherReservations = false (the takeover comes from the job's playerForced flag, never from an 'ignore everything' argument) (" + reserveSites.Count + " sites)",
                reserveSites.Count >= 2 && reserveSites.All(x => ((MethodReference)x.ins.Operand).DeclaringType.Name == "ReservationUtility" && Where(x.m).StartsWith("JobDriver_Gm21Purify") && x.m.Body.Instructions[x.ix - 1].OpCode.Code == Mono.Cecil.Cil.Code.Ldc_I4_0));
            var playerForcedUses = everyMethod.SelectMany(m => m.Body.Instructions.Select(ins => new { m, ins })).Where(x => x.ins.Operand is FieldReference && ((FieldReference)x.ins.Operand).Name == "playerForced").ToList();
            Check("production: playerForced is WRITTEN in exactly one place (Gm21PurifyFood.MakeJob) and never READ -- no custom priority between forced jobs",
                playerForcedUses.Count == 1 && playerForcedUses[0].ins.OpCode.Code == Mono.Cecil.Cil.Code.Stfld && Where(playerForcedUses[0].m) == "Gm21PurifyFood.MakeJob");
            var jobTrackerCalls = everyMethod.SelectMany(m => m.Body.Instructions.Select(ins => new { m, ins })).Where(x => x.ins.Operand is MethodReference && ((MethodReference)x.ins.Operand).DeclaringType.Name == "Pawn_JobTracker").ToList();
            var uninstallEnd = jobTrackerCalls.Where(x => ((MethodReference)x.ins.Operand).Name == "EndCurrentJob").ToList();
            var endBody = uninstallEnd.Count == 1 ? uninstallEnd[0].m.Body.Instructions.ToList() : new List<Mono.Cecil.Cil.Instruction>();
            Check("production: the only job-tracker calls are TryTakeOrderedJob for the Grandmaster's own order, and -- in Prepare Save for Uninstall only -- ending that pawn's own Purify job (behind an IsPurifyJob test); GM21 never ends, queues or starts another pawn's job",
                jobTrackerCalls.Count == 3
                && jobTrackerCalls.Where(x => ((MethodReference)x.ins.Operand).Name == "TryTakeOrderedJob").All(x => Where(x.m).StartsWith("Gm21PurifyOrders.Order"))
                && uninstallEnd.Count == 1 && Where(uninstallEnd[0].m) == "Gm21CookingUninstall.EndPurifyJobs"
                && endBody.FindLastIndex(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "IsPurifyJob") >= 0
                && endBody.FindLastIndex(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "IsPurifyJob") < endBody.FindIndex(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "EndCurrentJob"));
            var releaseCalls = everyMethod.SelectMany(m => m.Body.Instructions.Select(ins => new { m, ins })).Where(x => x.ins.Operand is MethodReference
                && (((MethodReference)x.ins.Operand).DeclaringType.Name == "ReservationManager" && ((MethodReference)x.ins.Operand).Name.StartsWith("Release") || ((MethodReference)x.ins.Operand).Name == "ClearAllReservations" || ((MethodReference)x.ins.Operand).Name == "ClearReservationsForJob")).ToList();
            Check("production: the only reservation release is the job's own (ReservedBy-guarded) release seam; no reservation is ever cleared wholesale",
                releaseCalls.Count == 1 && ((MethodReference)releaseCalls[0].ins.Operand).Name == "Release" && Where(releaseCalls[0].m) == "Gm21PurifyFood.Release");
            var special = everyMethod.SelectMany(m => m.Body.Instructions).Select(ins => ins.Operand).Where(o => o is MemberReference).Cast<MemberReference>()
                .Where(r => new[] { "Guest", "Quest", "Lodger", "HostFaction", "Prisoner", "Slave" }.Any(w => r.Name.IndexOf(w, StringComparison.Ordinal) >= 0 || (r.DeclaringType != null && r.DeclaringType.Name.IndexOf(w, StringComparison.Ordinal) >= 0)))
                .Select(r => r.DeclaringType.Name + "::" + r.Name).Distinct().ToList();
            Check("production: no guest, quest, lodger, prisoner or slave logic anywhere in Cooking -- displaced guests are handled by vanilla reservations alone" + (special.Count > 0 ? " (found " + string.Join(", ", special) + ")" : ""), special.Count == 0);
            Check("production: no GameComponent / MapComponent / WorldComponent and no comp tick in Cooking (nothing polls; Auto is one finite job)",
                !cooking.SelectMany(Nest).Any(t => { for (var b = t.BaseType; b != null; ) { if (new[] { "GameComponent", "MapComponent", "WorldComponent" }.Contains(b.Name)) return true; try { b = b.Resolve() == null ? null : b.Resolve().BaseType; } catch { b = null; } } return false; })
                && !everyMethod.Any(m => m.IsVirtual && m.DeclaringType.Name == "CompGrandmasterMeal" && (m.Name == "CompTick" || m.Name == "CompTickRare" || m.Name == "CompTickLong" || m.Name == "CompTickInterval")));
            Check("production Cooking code never writes RotProgress or calls TakeDamage / SetPoisoned",
                !methods.Where(m => m.DeclaringType.Name != "Gm21CookingDebugActions").SelectMany(m => m.Body.Instructions).Any(i => i.Operand is MethodReference
                    && (((MethodReference)i.Operand).Name == "set_RotProgress" || ((MethodReference)i.Operand).Name == "TakeDamage" || ((MethodReference)i.Operand).Name == "SetPoisoned")));
        }
    }

    static bool Flag(string name) { return (bool)AccessTools.Field(Cooking, name).GetValue(null); }

    static void Bindings()
    {
        Check("production startup: Perfect Hygiene group bound to the real CompFoodPoisonable", Flag("HygieneEnabled"));
        Check("production startup: Masterful ingestion group bound to the real Thing.Ingested", Flag("IngestionEnabled"));
        Check("production startup: freshness group bound to the real CompRottable.TickInterval", Flag("FreshnessEnabled"));
        Check("production startup: freshness-estimate group bound to the real CompRottable.TicksUntilRotAtTemp", Flag("FreshnessEstimateEnabled"));
        Check("production startup: Purify Food resolved the real private poison fields", Flag("PurifyEnabled"));
        var owned = Harmony.GetAllPatchedMethods().Where(m => { var i = Harmony.GetPatchInfo(m); return i.Owners.Any(o => o.StartsWith("Grandmaster21.Cooking.") && !o.Contains(".Tests.")); })
            .Select(m => m.DeclaringType.Name + "." + m.Name).OrderBy(n => n).ToArray();
        Check("narrowness: Cooking patches exactly four vanilla methods and no others (" + string.Join(", ", owned) + ")",
            owned.SequenceEqual(new[] { "CompFoodPoisonable.Notify_RecipeProduced", "CompRottable.TickInterval", "CompRottable.TicksUntilRotAtTemp", "Thing.Ingested" }));
        Check("narrowness: CompRottable itself is not replaced (no prefix or finalizer on its methods)", !Harmony.GetAllPatchedMethods().Where(m => m.DeclaringType == typeof(CompRottable))
            .Any(m => Harmony.GetPatchInfo(m).Prefixes.Any(p => p.owner.StartsWith("Grandmaster21.Cooking.") && !p.owner.Contains(".Tests.")) || Harmony.GetPatchInfo(m).Finalizers.Any(p => p.owner.StartsWith("Grandmaster21.Cooking."))));
        Check("narrowness: no Cooking patch touches JobDriver_Ingest, ReservationManager, ReservationUtility or Pawn_JobTracker (displaced pawns are left entirely to vanilla)",
            !Harmony.GetAllPatchedMethods().Where(m => new[] { "JobDriver_Ingest", "ReservationManager", "ReservationUtility", "Pawn_JobTracker", "Toils_Ingest" }.Contains(m.DeclaringType.Name))
                .Any(m => { var i = Harmony.GetPatchInfo(m); return i.Owners.Any(o => o.StartsWith("Grandmaster21.Cooking.") && !o.Contains(".Tests.")); }));
        Gm21CookingDefOf.GM21_MasterfulMeal = new ThoughtDef { defName = "GM21_MasterfulMeal" };
        Gm21CookingDefOf.GM21_PurifyFood = new JobDef { defName = "GM21_PurifyFood", driverClass = typeof(JobDriver_Gm21PurifyFood) };
        Gm21CookingDefOf.GM21_PurifyFoodAuto = new JobDef { defName = "GM21_PurifyFoodAuto", driverClass = typeof(JobDriver_Gm21AutoPurifyFood) };
    }

    static void Binding()
    {
        int before = DefDatabase<ThingDef>.AllDefsListForReading.Count;
        mealDef = MakeDef("FixtureMeal", 10);
        var freshDef = MakeDef("FixtureNoRot", 10, rottable: false);
        var rawDef = MakeDef("FixtureRawFood", 75, poisonable: false);
        var toolDef = MakeDef("FixtureNotFood", 1, poisonable: true, ingestible: false);
        var moddedDef = MakeDef("ModdedStew", 5, poisonComp: typeof(ModdedPoison));
        var plainDef = MakeDef("FixtureBareItem", 1, poisonable: true, ingestible: true, thingClass: typeof(Thing));
        Check("startup attach: prepared food (poisonable + ingestible + ThingWithComps) is a candidate", (bool)Call(Startup, "IsMasterfulCandidate", mealDef) && (bool)Call(Startup, "IsMasterfulCandidate", freshDef));
        Check("startup attach: raw ingredient, non-ingestible poisonable item and non-comp Thing class are not", !(bool)Call(Startup, "IsMasterfulCandidate", rawDef) && !(bool)Call(Startup, "IsMasterfulCandidate", toolDef) && !(bool)Call(Startup, "IsMasterfulCandidate", plainDef));
        Check("startup attach: a modded CompFoodPoisonable subclass counts, with no DefName rule", (bool)Call(Startup, "IsMasterfulCandidate", moddedDef));
        int attached = (int)Call(Startup, "AttachMasterfulComp");
        Check("startup attach: exactly the three candidates got the comp", attached == 3 && mealDef.comps.OfType<CompProperties_GrandmasterMeal>().Count() == 1
            && freshDef.comps.OfType<CompProperties_GrandmasterMeal>().Count() == 1 && moddedDef.comps.OfType<CompProperties_GrandmasterMeal>().Count() == 1
            && !rawDef.comps.OfType<CompProperties_GrandmasterMeal>().Any() && !toolDef.comps.OfType<CompProperties_GrandmasterMeal>().Any());
        Check("startup attach: idempotent (a second pass attaches nothing)", (int)Call(Startup, "AttachMasterfulComp") == 0 && mealDef.comps.OfType<CompProperties_GrandmasterMeal>().Count() == 1);
        Check("startup attach: no existing vanilla comp was replaced or removed", mealDef.comps.Count == 3 && mealDef.comps[0] is CompProperties_FoodPoisonable && mealDef.comps[1] is CompProperties_Rottable);
        Check("startup attach: a made thing gets a real, non-ticking comp", MealComp(Meal(mealDef, 1)) != null && MealComp(Meal(rawDef, 1)) == null);
    }

    static void Hygiene()
    {
        // A. Perfect Hygiene -- vanilla's REAL Notify_RecipeProduced, with the roll forced to hit.
        var gm = Cook(21); var l20 = Cook(20);
        chanceDefault = true; randCalls = 0;
        var m = Meal(mealDef, 10); m.Notify_RecipeProduced(l20);
        var poison = m.TryGetComp<CompFoodPoisonable>();
        Check("A level-20 cook: vanilla kitchen roll runs and poisons (FilthyKitchen)", randCalls == 1 && poison.PoisonPercent == 1f && poison.Cause == FoodPoisonCause.FilthyKitchen);
        chanceScript.Clear(); chanceDefault = false; chanceScript.Enqueue(false); chanceScript.Enqueue(true); statValue = 0.3f; randCalls = 0;
        m = Meal(mealDef, 10); m.Notify_RecipeProduced(l20); poison = m.TryGetComp<CompFoodPoisonable>();
        Check("A level-20 cook: vanilla incompetent-cook roll still poisons (IncompetentCook)", randCalls == 2 && poison.PoisonPercent == 1f && poison.Cause == FoodPoisonCause.IncompetentCook);
        chanceDefault = true; randCalls = 0;
        m = Meal(mealDef, 10); m.Notify_RecipeProduced(gm); poison = m.TryGetComp<CompFoodPoisonable>();
        Check("A Grandmaster in a filthy kitchen (roll would hit): never cooking-poisoned", poison.PoisonPercent == 0f && poison.Cause == FoodPoisonCause.Unknown);
        Check("A Grandmaster: vanilla's cooking rolls are skipped entirely (no RNG consumed)", randCalls == 0);
        m = Meal(mealDef, 10); m.Notify_RecipeProduced(Cook(20));
        Check("A the very next level-20 cook is poisoned again: no state leaked from the Grandmaster call", IsPoisoned(m));
        // Only cooking contamination is affected: an externally applied poison on a Grandmaster's meal survives.
        m = Meal(mealDef, 10); m.Notify_RecipeProduced(gm); m.TryGetComp<CompFoodPoisonable>().SetPoisoned(FoodPoisonCause.DangerousFoodType);
        Check("A external contamination is untouched: SetPoisoned still works on a Grandmaster's meal", m.TryGetComp<CompFoodPoisonable>().PoisonPercent == 1f && m.TryGetComp<CompFoodPoisonable>().Cause == FoodPoisonCause.DangerousFoodType);
        // Stored 21 versus aptitude: aptitude can neither manufacture nor remove Cooking Grandmaster status.
        aptitudeShim = 5; var geneBoosted = Cook(20);
        Check("A Cooking Grandmaster is the STORED level: stored 20 with +5 aptitude is not one", !(bool)Call(Cooking, "IsCookingGrandmaster", geneBoosted));
        aptitudeShim = -5; var geneDrained = Cook(21);
        Check("A Cooking Grandmaster is the STORED level: stored 21 with -5 aptitude still is", (bool)Call(Cooking, "IsCookingGrandmaster", geneDrained));
        aptitudeShim = 0;
        Check("A a null pawn is never a Cooking Grandmaster", !(bool)Call(Cooking, "IsCookingGrandmaster", new object[] { null }));
        var lucky = Cook(20); lucky.skills.skills[0].levelInt = 20;
        m = Meal(mealDef, 10); chanceDefault = true; m.Notify_RecipeProduced(lucky);
        Check("A stored 20 (even with positive aptitude) is not a Grandmaster: still poisoned", IsPoisoned(m));
        chanceDefault = false; chanceScript.Clear();
    }

    static void Creation()
    {
        // B. Masterful creation through the REAL GenRecipe.MakeRecipeProducts.
        var gm = Cook(21); var normal = Cook(20);
        var recipe = Recipe("FixtureCook", new ThingDefCountClass(mealDef, 10));
        var made = GenRecipe.MakeRecipeProducts(recipe, gm, new List<Thing>(), null, null).ToList();
        Check("B Grandmaster cooks a stack of 10: stackCount 10, masterfulCount 10", made.Count == 1 && made[0].stackCount == 10 && M(made[0]) == 10);
        made = GenRecipe.MakeRecipeProducts(recipe, normal, new List<Thing>(), null, null).ToList();
        Check("B normal cook: stack of 10 with masterfulCount 0", made.Count == 1 && made[0].stackCount == 10 && M(made[0]) == 0);
        var bigRecipe = Recipe("FixtureCook2", new ThingDefCountClass(mealDef, 4), new ThingDefCountClass(mealDef, 6));
        made = GenRecipe.MakeRecipeProducts(bigRecipe, gm, new List<Thing>(), null, null).ToList();
        Check("B every recipe product of a Grandmaster is fully Masterful (4 and 6)", made.Count == 2 && M(made[0]) == 4 && M(made[1]) == 6);
        var raw = MakeDef("FixtureRawProduct", 75, poisonable: false);
        var rawRecipe = Recipe("FixtureRaw", new ThingDefCountClass(raw, 5));
        made = GenRecipe.MakeRecipeProducts(rawRecipe, gm, new List<Thing>(), null, null).ToList();
        Check("B a product with no Masterful state (not prepared food) is created untouched", made.Count == 1 && made[0].stackCount == 5 && MealComp(made[0]) == null);
        var traded = Meal(mealDef, 7);
        Check("B food that never came from a recipe (trader, quest, dev) starts with zero Masterful servings", M(traded) == 0);
    }

    static void Stacks()
    {
        // C/D. Real TryAbsorbStack / SplitOff, then a long deterministic random walk of splits and merges.
        var big = MakeDef("FixtureBigStack", 75); Call(Startup, "AttachMasterfulComp");
        var a = Meal(big, 10, 10); var b = Meal(big, 10, 0);
        a.TryAbsorbStack(b.SplitOff(5), true);
        Check("C 10 Masterful + 5 ordinary (five taken from a stack of 10): total 15, Masterful 10, NOT all 15", a.stackCount == 15 && M(a) == 10 && b.stackCount == 5 && M(b) == 0);
        a = Meal(mealDef, 10, 10); b = Meal(mealDef, 10, 0);
        Check("C a full stack (limit 10) absorbs nothing and nothing moves", !a.TryAbsorbStack(b, true) && a.stackCount == 10 && M(a) == 10 && b.stackCount == 10 && M(b) == 0);
        a = Meal(mealDef, 10, 10); b = Meal(mealDef, 10, 0);
        a.stackCount = 5; SetMasterful(a, 5); // stack limit is 10: absorb only what fits
        a.TryAbsorbStack(b, true);
        Check("C partial absorption respecting the stack limit: 5 taken, donor keeps 5, Masterful stays exact", a.stackCount == 10 && M(a) == 5 && b.stackCount == 5 && M(b) == 0);
        a = Meal(big, 10, 10); b = Meal(big, 10, 0); a.TryAbsorbStack(b, true);
        Check("C 10 Masterful merged with 10 ordinary (limit 75): total 20, Masterful 10", a.stackCount == 20 && M(a) == 10 && b.Destroyed);
        // Mixed donor: proportional, conserving.
        a = Meal(mealDef, 4, 0); b = Meal(mealDef, 10, 6);
        a.TryAbsorbStack(b, true);
        Check("C mixed donor: 6 of 10 Masterful, 6 servings fit -> proportion moves, Masterful conserved", M(a) + M(b) == 6 && a.stackCount == 10 && b.stackCount == 4 && M(a) <= a.stackCount && M(b) <= b.stackCount);
        // Whole-stack split returns the same object: nothing may change.
        a = Meal(mealDef, 6, 4); var whole = a.SplitOff(6);
        Check("D whole-stack split (piece is the same object) leaves the count alone", ReferenceEquals(whole, a) && M(a) == 4 && a.stackCount == 6);
        // Deterministic split table.
        a = Meal(mealDef, 10, 4); var piece = a.SplitOff(5);
        Check("D split 5 from 10 with 4 Masterful: 2 go, 2 stay", M(piece) == 2 && M(a) == 2 && piece.stackCount == 5 && a.stackCount == 5);
        a = Meal(mealDef, 10, 10); piece = a.SplitOff(3);
        Check("D split from an all-Masterful stack: every piece is all-Masterful", M(piece) == 3 && M(a) == 7);
        a = Meal(mealDef, 10, 0); piece = a.SplitOff(3);
        Check("D split from an ordinary stack: no Masterful servings appear", M(piece) == 0 && M(a) == 0);
        a = Meal(mealDef, 10, 1); piece = a.SplitOff(1);
        Check("D 1 Masterful of 10, split one off: rounds to the ordinary side, count conserved", M(piece) + M(a) == 1);
        a = Meal(mealDef, 3, 1); piece = a.SplitOff(2);
        Check("D never more Masterful than servings, never negative (1 of 3, split 2)", M(piece) >= 0 && M(piece) <= 2 && M(a) >= 0 && M(a) <= 1 && M(piece) + M(a) == 1);

        // Random walk: many stacks, random splits and merges; invariants after EVERY operation.
        var rng = new System.Random(1234); var stacks = new List<ThingWithComps>();
        int total = 0, servings = 0; bool ok = true; string why = "";
        for (int i = 0; i < 6; i++) { int n = rng.Next(1, 11), k = rng.Next(0, n + 1); stacks.Add(Meal(mealDef, n, k)); total += k; servings += n; }
        for (int step = 0; step < 4000 && ok; step++)
        {
            if (rng.Next(2) == 0 && stacks.Count > 0)
            {
                var s = stacks[rng.Next(stacks.Count)];
                if (s.stackCount < 2) continue;
                var p = (ThingWithComps)s.SplitOff(rng.Next(1, s.stackCount)); stacks.Add(p);
            }
            else if (stacks.Count > 1)
            {
                var x = stacks[rng.Next(stacks.Count)]; var y = stacks[rng.Next(stacks.Count)];
                if (ReferenceEquals(x, y)) continue;
                x.TryAbsorbStack(y, true);
                if (y.Destroyed) stacks.Remove(y);
            }
            int sum = 0;
            foreach (var s in stacks)
            {
                int c = M(s);
                sum += c;
                if (c < 0 || c > s.stackCount) { ok = false; why = "bound step " + step; }
            }
            if (sum != total) { ok = false; why = "conservation step " + step + " " + sum + "!=" + total; }
        }
        Check("C/D 4000 random splits and merges: Masterful conserved exactly and always within 0..stackCount (" + why + ")", ok);
        Check("C/D the walk never lost or created servings (" + servings + " in, " + stacks.Sum(s => s.stackCount) + " out)", stacks.Sum(s => s.stackCount) == servings && stacks.All(s => !s.Destroyed));
    }

    static void Ingestion()
    {
        var feast = MakeDef("FixtureFeast", 10, maxIngest: 20); Call(Startup, "AttachMasterfulComp");
        var eater = Eater();
        // F. A pawn carries ONE serving at a time: split it off the stack, eat it. Real Thing.Ingested.
        foreach (int marked in new[] { 0, 1, 5, 10 })
        {
            var stack = Meal(mealDef, 10, marked); gained.Clear(); int meals = 0;
            for (int i = 0; i < 10; i++)
            {
                var piece = stack.SplitOff(1);
                piece.Ingested(eater, 1f);
                meals++;
            }
            Check("F " + marked + " Masterful of 10 eaten one by one: exactly " + marked + " rewards (never ten)", meals == 10 && gained.Count == marked);
            Check("F " + marked + " Masterful of 10: the stack is consumed and its provenance with it", stack.Destroyed && M(stack) == 0);
        }
        Check("F the reward is the tunable Masterful Meal thought, nothing else", gained.All(g => g == Gm21CookingDefOf.GM21_MasterfulMeal));

        // Multi-serving meals eaten straight from the stack (partial split, remainder keeps the rest).
        var s = Meal(feast, 10, 4); gained.Clear(); int consumed = 0, events = 0;
        while (!s.Destroyed)
        {
            int before = s.Destroyed ? 0 : M(s), stackBefore = s.stackCount;
            s.Ingested(eater, 3f);
            int after = s.Destroyed ? 0 : M(s);
            consumed += before - after; events++;
            Check("F remainder stays valid after eating from " + stackBefore + " (Masterful " + after + " of " + (s.Destroyed ? 0 : s.stackCount) + ")", s.Destroyed || (after >= 0 && after <= s.stackCount));
        }
        Check("F 4 Masterful servings eaten in total across " + events + " meals: all accounted for, none free", consumed == 4);
        Check("F one reward per meal that contained a Masterful serving, never one per serving", gained.Count <= events && gained.Count >= 1 && gained.Count <= 4);
        s = Meal(feast, 10, 4); gained.Clear(); s.Ingested(eater, 10f);
        Check("F an entire mixed stack eaten at once: one reward, all four Masterful servings consumed", gained.Count == 1 && s.Destroyed);
        s = Meal(feast, 10, 1); gained.Clear(); s.Ingested(eater, 3f);
        Check("F a Masterful serving that stays behind in the remainder earns nothing yet (containing is not eating)", gained.Count == 0 && M(s) == 1 && s.stackCount == 7);
        s = Meal(feast, 10, 0); gained.Clear(); s.Ingested(eater, 3f);
        Check("F an ordinary stack never earns the reward, and vanilla still removes exactly the eaten servings", gained.Count == 0 && s.stackCount == 7);
        var poisoned = Meal(mealDef, 1, 1); poisoned.TryGetComp<CompFoodPoisonable>().SetPoisoned(FoodPoisonCause.FilthyKitchen);
        randCalls = 0; gained.Clear(); poisoned.Ingested(eater, 1f);
        Check("F ingesting poisoned food still runs vanilla's own poisoning roll (nothing is intercepted)", randCalls >= 1 && gained.Count == 1);
    }

    static void Freshness()
    {
        // G. The REAL CompRottable ticks; only the multiplication is new. Same ambient temperature for every stack.
        ambient = 21f;
        float rate = GenTemperature.RotRateAtTemperature(21f);
        Check("G baseline: room temperature is a positive vanilla rot rate", rate > 0f);
        Func<int, int, float> Advance = (count, marked) =>
        {
            var m = Meal(mealDef, count, marked);
            m.TryGetComp<CompRottable>().CompTickInterval(2500);
            return Rot(m).RotProgress;
        };
        float expected = rate * 2500f;
        Check("G 0% Masterful: exactly vanilla (bit-for-bit) " + expected, Advance(10, 0) == expected);
        Check("G 25% Masterful: 0.80x", Mathf.Abs(Advance(4, 1) - expected * 0.80f) < 0.01f);
        Check("G 50% Masterful: 0.60x", Mathf.Abs(Advance(10, 5) - expected * 0.60f) < 0.01f);
        Check("G 75% Masterful: 0.40x", Mathf.Abs(Advance(4, 3) - expected * 0.40f) < 0.01f);
        Check("G 100% Masterful: 0.20x (five times the freshness)", Mathf.Abs(Advance(10, 10) - expected * 0.20f) < 0.01f);
        var quick = Meal(mealDef, 10, 10); quick.TryGetComp<CompRottable>().CompTickRare();
        Check("G the 250-tick rare path is scaled the same way", Mathf.Abs(Rot(quick).RotProgress - rate * 250f * 0.2f) < 0.01f);
        // Temperature behaviour is vanilla: refrigerated slows proportionally, frozen stays frozen.
        ambient = 3f; float cold = GenTemperature.RotRateAtTemperature(3f);
        float coldOrdinary = Advance(10, 0), coldMasterful = Advance(10, 10);
        Check("G refrigerated (3C): ordinary vanilla " + cold * 2500f + ", Masterful 0.20x of that", cold > 0f && cold < rate && coldOrdinary == cold * 2500f && Mathf.Abs(coldMasterful - cold * 2500f * 0.2f) < 0.01f);
        ambient = -10f;
        Check("G frozen (-10C): no rot for ordinary or Masterful stacks", Advance(10, 0) == 0f && Advance(10, 10) == 0f);
        ambient = 21f;
        // Vanilla stack semantics are untouched: RotProgress still averages on merge, and the ratio then governs.
        var big = MakeDef("FixtureRotBig", 75); Call(Startup, "AttachMasterfulComp");
        var old = Meal(big, 10, 10); Rot(old).RotProgress = 1000f;
        var fresh = Meal(big, 10, 0);
        old.TryAbsorbStack(fresh, true);
        Check("G vanilla merge still averages RotProgress (10 aged + 10 fresh -> 500) and Masterful is 10 of 20", Mathf.Abs(Rot(old).RotProgress - 500f) < 0.01f && M(old) == 10 && old.stackCount == 20);
        float before = Rot(old).RotProgress; Rot(old).CompTickInterval(2500);
        Check("G the merged stack now rots at 0.60x (ratio 0.50)", Mathf.Abs((Rot(old).RotProgress - before) - expected * 0.6f) < 0.01f);
        var piece = old.SplitOff(10);
        Check("G vanilla split still copies RotProgress; each piece keeps its own ratio", Mathf.Abs(Rot(piece).RotProgress - Rot(old).RotProgress) < 0.01f && M(piece) + M(old) == 10);
        // The days-until-rot estimate follows the same multiplier.
        var normal = Meal(mealDef, 10, 0); var fully = Meal(mealDef, 10, 10);
        int tOrdinary = Rot(normal).TicksUntilRotAtTemp(21f), tMasterful = Rot(fully).TicksUntilRotAtTemp(21f);
        Check("G estimate: a fully Masterful stack shows five times the ordinary time (" + tOrdinary + " -> " + tMasterful + ")", tOrdinary > 0 && Mathf.Abs(tMasterful - tOrdinary * 5f) <= 6f);
        Check("G estimate: frozen still reads 'never' for both", Rot(normal).TicksUntilRotAtTemp(-10f) == Rot(fully).TicksUntilRotAtTemp(-10f) && Rot(normal).TicksUntilRotAtTemp(-10f) > 1000000);
        // A rot scaling that cannot fail: non-Masterful stacks and things with no comp take the untouched path.
        object scaler = Call(typeof(Gm21Mod).Assembly.GetType("Grandmaster21.Gm21MasterfulRot"), "ScaleRate", 0.75f, Rot(normal));
        Check("G ScaleRate returns a non-Masterful stack's vanilla rate unchanged", (float)scaler == 0.75f);
        Check("G ScaleRate tolerates a null comp", (float)Call(typeof(Gm21Mod).Assembly.GetType("Grandmaster21.Gm21MasterfulRot"), "ScaleRate", 0.75f, null) == 0.75f);
        var corpseLike = Meal(MakeDef("FixtureCorpseLike", 1, poisonable: false), 1);
        Check("G a rottable thing with no Masterful comp (corpse, raw meat) is untouched", (float)Call(typeof(Gm21Mod).Assembly.GetType("Grandmaster21.Gm21MasterfulRot"), "ScaleRate", 0.5f, Rot(corpseLike)) == 0.5f);
    }

    static string SaveAndLoad(ThingWithComps thing, string tag, Func<string, string> edit, out ThingWithComps loaded)
    {
        string path = Path.Combine(scratch, "cooking-" + tag + ".xml");
        Scribe.saver.InitSaving(path, "savegame");
        Thing saving = thing;
        Scribe_Deep.Look(ref saving, "meal");
        Scribe.saver.FinalizeSaving();
        string xml = File.ReadAllText(path);
        if (edit != null) { xml = edit(xml); File.WriteAllText(path, xml); }
        Scribe.loader.InitLoading(path);
        Thing read = null;
        Scribe_Deep.Look(ref read, "meal");
        Scribe.loader.FinalizeLoading();
        loaded = read as ThingWithComps;
        return xml;
    }

    static void SaveLoad()
    {
        // E. The real Scribe saver and loader, through Scribe_Deep exactly as the game saves a food Thing.
        ThingWithComps loaded;
        var mixed = Meal(mealDef, 15, 10); mixed.stackCount = 10; SetMasterful(mixed, 7);
        string xml = SaveAndLoad(mixed, "mixed", null, out loaded);
        Check("E a mixed Masterful stack is written into the Thing's own node as gm21MasterfulCount", xml.Contains("<gm21MasterfulCount>7</gm21MasterfulCount>"));
        Check("E it reloads with the exact count, stack and comp", loaded != null && loaded.stackCount == 10 && MealComp(loaded) != null && M(loaded) == 7);
        var ordinary = Meal(mealDef, 6, 0);
        xml = SaveAndLoad(ordinary, "ordinary", null, out loaded);
        Check("E ordinary food writes no Cooking element at all (default zero is not saved)", !xml.Contains("gm21"));
        Check("E ordinary food reloads with zero Masterful servings", loaded != null && M(loaded) == 0);
        xml = SaveAndLoad(mixed, "old", x => x.Replace("<gm21MasterfulCount>7</gm21MasterfulCount>", ""), out loaded);
        Check("E an old save with no Cooking element loads safely as zero", loaded != null && loaded.stackCount == 10 && M(loaded) == 0);
        SaveAndLoad(mixed, "hostile", x => x.Replace("<gm21MasterfulCount>7</gm21MasterfulCount>", "<gm21MasterfulCount>99</gm21MasterfulCount>"), out loaded);
        Check("E a stored count beyond the stack is clamped to the stack, never trusted", loaded != null && M(loaded) == 10);
        SaveAndLoad(mixed, "negative", x => x.Replace("<gm21MasterfulCount>7</gm21MasterfulCount>", "<gm21MasterfulCount>-4</gm21MasterfulCount>"), out loaded);
        Check("E a negative stored count loads as zero", loaded != null && M(loaded) == 0);
        // Round trip preserves everything vanilla saves alongside it.
        var rotting = Meal(mealDef, 10, 6); Rot(rotting).RotProgress = 12345f; rotting.TryGetComp<CompFoodPoisonable>().SetPoisoned(FoodPoisonCause.IncompetentCook);
        SaveAndLoad(rotting, "full", null, out loaded);
        Check("E rot progress, poison and cause round-trip beside the Masterful count", loaded != null && Rot(loaded).RotProgress == 12345f && loaded.TryGetComp<CompFoodPoisonable>().PoisonPercent == 1f
              && loaded.TryGetComp<CompFoodPoisonable>().Cause == FoodPoisonCause.IncompetentCook && M(loaded) == 6);
        // The uninstall story: a save with the mod's element but loaded by a def WITHOUT the comp keeps loading.
        var noComp = MakeDef("FixtureNoCompLater", 10, poisonable: true);
        var later = Meal(noComp, 4, 0);
        Check("E (uninstall safety) a food def the mod never touched has no Cooking comp and saves/loads exactly as vanilla", MealComp(later) == null);
    }

    static Pawn CookPawn(int level, int x)
    {
        var p = Cook(level);
        p.health = Empty<Pawn_HealthTracker>(); Set(p.health, "pawn", p); Set(p.health, "healthState", PawnHealthState.Mobile);
        p.health.capacities = Empty<PawnCapacitiesHandler>();
        p.jobs = Empty<Pawn_JobTracker>(); Set(p.jobs, "pawn", p); p.jobs.jobQueue = new JobQueue();
        p.pather = Empty<Pawn_PathFollower>(); p.stances = Empty<Pawn_StanceTracker>();
        Set(p, "factionInt", playerFaction); Set(p, "positionInt", new IntVec3(x, 0, 0));
        spawned.Add(p);
        return p;
    }

    static ThingWithComps Poisoned(int x, ThingDef def = null, int count = 10, int masterful = 0, float rot = 0f, bool poison = true)
    {
        var t = Meal(def ?? mealDef, count, masterful);
        Set(t, "positionInt", new IntVec3(x, 0, 0)); spawned.Add(t); field.Add(t);
        if (rot > 0f) Rot(t).RotProgress = rot;
        if (poison) t.TryGetComp<CompFoodPoisonable>().SetPoisoned(FoodPoisonCause.FilthyKitchen);
        return t;
    }

    /// <summary>
    /// An ordinary pawn's job that holds a reservation on food, exactly as vanilla makes it: eating reserves a
    /// PARTIAL stack shared by up to ten pawns (Reserve(source, job, 10, count)); hauling reserves the whole
    /// thing for one. The pawn is left running that job, so a displacement is visible as an ended job.
    /// </summary>
    static Job Occupy(Pawn holder, Thing food, string kind, bool forced)
    {
        if (ingestDef == null) { ingestDef = new JobDef { defName = "Ingest" }; haulDef = new JobDef { defName = "HaulToCell" }; }
        Job j = JobMaker.MakeJob(kind == "ingest" ? ingestDef : haulDef, food);
        j.playerForced = forced;
        bool ok = kind == "ingest" ? holder.Reserve(food, j, 10, 1, null, false) : holder.Reserve(food, j, 1, -1, null, false);
        if (!ok) throw new InvalidOperationException("fixture could not give the ordinary pawn its reservation");
        holder.jobs.curJob = j;
        return j;
    }
    static Pawn Holder(Faction faction, int x)
    {
        Pawn p = CookPawn(20, x);
        if (faction != null) Set(p, "factionInt", faction);
        return p;
    }
    static bool HeldBy(Thing t, Pawn p) { return map.reservationManager.ReservedBy(t, p, null); }
    static int Reservations() { return map.reservationManager.ReservationsReadOnly.Count; }
    static bool Ended(Job j, JobCondition c) { return endLog.Any(e => e.Key == j && e.Value == c); }
    static int EndedCount(Job j) { return endLog.Count(e => e.Key == j); }

    /// <summary>A stack that leaves the world: no longer spawned, then destroyed (vanilla zeroes its stackCount).</summary>
    static void Kill(Thing t) { spawned.Remove(t); field.Remove(t); t.Destroy(); }

    static void ResetWorld()
    {
        field.Clear(); elsewhere.Clear(); unreachable.Clear(); vetoClaim.Clear(); hostilePairs.Clear(); endLog.Clear(); messages.Clear(); walked.Clear();
        scans = pathStarts = endCount = thrown = reserveCalls = 0;
        pathPending = false; capable = true; failEveryReserve = false; raceOnce = null; raceHolder = null; ordered = null; pathTarget = null;
        // A fresh REAL manager for a fresh world.
        map.reservationManager = new ReservationManager(map);
        map.physicalInteractionReservationManager = new PhysicalInteractionReservationManager();
        map.events = new MapEvents(map);
    }

    static JobDriver StartJob(Pawn p, Job job, out bool started, bool errorOnFailed = false)
    {
        var driver = (JobDriver)Activator.CreateInstance(job.def.driverClass);
        driver.pawn = p; driver.job = job; p.jobs.curJob = job; p.jobs.curDriver = driver;
        started = driver.TryMakePreToilReservations(errorOnFailed);
        if (!started) { p.jobs.curJob = null; p.jobs.curDriver = null; return driver; }
        AccessTools.Method(typeof(JobDriver), "SetupToils").Invoke(driver, null);
        return driver;
    }

    /// <summary>Ticks the real driver. A started path "arrives" on the following tick, as the pather would report.</summary>
    static int RunJob(Pawn p, JobDriver d, int maxTicks, Action<int> beforeTick = null)
    {
        int tick = 0;
        for (; tick < maxTicks && p.jobs.curJob != null; tick++)
        {
            if (beforeTick != null) beforeTick(tick);
            if (p.jobs.curJob == null) break;
            d.DriverTick();
            Toil current = (Toil)AccessTools.Property(typeof(JobDriver), "CurToil").GetValue(d, null);
            if (pathPending && p.jobs.curJob != null && current != null && current.defaultCompleteMode == ToilCompleteMode.PatherArrival)
            {
                pathPending = false;
                if (pathTarget != null && !pathTarget.Destroyed) Set(p, "positionInt", pathTarget.Position);
                d.Notify_PatherArrived();
            }
        }
        return tick;
    }

    static string AutoDoneCount() { return messages.Count(m => m != null && m.Contains("GM21_Cook_AutoDone")).ToString(); }

    static void PurifyFood()
    {
        playerFaction = Empty<Faction>();
        var cook = CookPawn(21, 0); var rookie = CookPawn(20, 0);
        string reason;
        ResetWorld();

        // ---- H. what is a target, and what purifying does
        var a = Poisoned(2, count: 10, masterful: 6, rot: 3000f);
        Check("H a poisoned, spawned prepared-food stack is a target", (bool)Call(Purify, "IsPurifyTarget", a));
        Check("H a clean stack is not a target", !(bool)Call(Purify, "IsPurifyTarget", Poisoned(3, poison: false)));
        var gone = Poisoned(3); spawned.Remove(gone);
        Check("H an unspawned stack is not a target", !(bool)Call(Purify, "IsPurifyTarget", gone));
        var dead = Poisoned(3); Kill(dead);
        Check("H a destroyed stack is not a target", !(bool)Call(Purify, "IsPurifyTarget", dead));
        Check("H null is not a target", !(bool)Call(Purify, "IsPurifyTarget", new object[] { null }));
        var rawThing = Meal(MakeDef("FixturePlainRaw", 5, poisonable: false), 1); spawned.Add(rawThing);
        Check("H a thing without CompFoodPoisonable is not a target", !(bool)Call(Purify, "IsPurifyTarget", rawThing));
        // Merge first (a stack in a real map is merged while spawned; here the fake world spawns it afterwards).
        var diluted = Meal(mealDef, 1); diluted.TryGetComp<CompFoodPoisonable>().SetPoisoned(FoodPoisonCause.FilthyKitchen);
        var clean9 = Meal(mealDef, 9); clean9.TryAbsorbStack(diluted, false);
        Set(clean9, "positionInt", new IntVec3(4, 0, 0)); spawned.Add(clean9); field.Add(clean9);
        Check("H a stack carrying only a trace of poison (vanilla's proportional stack) still counts", (bool)Call(Purify, "IsPurifyTarget", clean9) && clean9.TryGetComp<CompFoodPoisonable>().PoisonPercent > 0f && clean9.TryGetComp<CompFoodPoisonable>().PoisonPercent < 1f);

        int stack = a.stackCount; float rot = Rot(a).RotProgress; int mast = M(a);
        bool changed = (bool)Call(Purify, "Purify", a);
        var poison = a.TryGetComp<CompFoodPoisonable>();
        Check("H purify clears contamination: poison 0 and cause back to Unknown", changed && poison.PoisonPercent == 0f && poison.Cause == FoodPoisonCause.Unknown);
        Check("H purify keeps the meal: stack count unchanged, not destroyed", a.stackCount == stack && !a.Destroyed);
        Check("H purify leaves rot progress exactly as it was", Rot(a).RotProgress == rot);
        Check("H purify leaves Masterful provenance exactly as it was", M(a) == mast);
        Check("H purifying again (or a clean stack) changes nothing and reports so", !(bool)Call(Purify, "Purify", a) && !(bool)Call(Purify, "Purify", clean9) == false);
        Check("H the trace-poison stack was fully cleansed", clean9.TryGetComp<CompFoodPoisonable>().PoisonPercent == 0f);
        var modded = Poisoned(6, ThingByName("ModdedStew"), 5);
        Check("H modded food built on CompFoodPoisonable is recognised and purified with no DefName rule", (bool)Call(Purify, "IsPurifyTarget", modded) && (bool)Call(Purify, "Purify", modded) && modded.TryGetComp<CompFoodPoisonable>().PoisonPercent == 0f);

        // ---- order-time checks and their reasons
        ResetWorld(); var t1 = Poisoned(2);
        object[] args = { cook, t1, null };
        Check("H a practising Grandmaster can order a reachable, free stack", (bool)Call(Purify, "CanOrder", args) && args[2] == null);
        args = new object[] { rookie, t1, null };
        Check("H a level-20 cook cannot (not a Grandmaster)", !(bool)Call(Purify, "CanOrder", args) && args[2] != null);
        capable = false; args = new object[] { cook, t1, null };
        Check("H an incapacitated Grandmaster cannot", !(bool)Call(Purify, "CanOrder", args)); capable = true;
        args = new object[] { cook, Poisoned(3, poison: false), null };
        Check("H a clean stack is refused with a reason", !(bool)Call(Purify, "CanOrder", args) && args[2] != null);
        var far = Poisoned(20); unreachable.Add(far); args = new object[] { cook, far, null };
        Check("H an unreachable stack is refused with a reason", !(bool)Call(Purify, "CanOrder", args) && args[2] != null);
        var taken = Poisoned(21); Occupy(Holder(playerFaction, 5), taken, "ingest", false); args = new object[] { cook, taken, null };
        Check("H a stack an ordinary pawn has reserved is NOT refused: Purify Food is a player order (ordinary CanReserve is false, the forced claim is not)",
            !cook.CanReserve(taken, 1, -1, null, false) && (bool)Call(Purify, "CanOrder", args) && args[2] == null);
        var impossible = Poisoned(22); vetoClaim.Add(impossible); args = new object[] { cook, impossible, null };
        Check("H a stack that cannot legally be claimed even under forced semantics is refused with a reason", !(bool)Call(Purify, "CanOrder", args) && args[2] != null);

        // ---- S. single Purify Food, driven through the real JobDriver
        ResetWorld(); cook = CookPawn(21, 0);
        var target = Poisoned(2, count: 10, masterful: 4, rot: 500f); var other = Poisoned(5);
        Job job = (Job)Call(Purify, "MakeJob", target, false);
        bool started; var driver = StartJob(cook, job, out started);
        Check("S single: the job starts and reserves its target", started && HeldBy(target, cook) && job.def == Gm21CookingDefOf.GM21_PurifyFood);
        int ticks = RunJob(cook, driver, 1000);
        Check("S single: the pawn walks to the food first (one path, to that stack)", pathStarts == 1 && walked[0] == target);
        Check("S single: performs the timed work (at least " + Gm21Cooking_PurifyTicks() + " ticks), then finishes with success", ticks >= Gm21Cooking_PurifyTicks() && endCount == 1 && endedWith == JobCondition.Succeeded);
        Check("S single: contamination removed, meal kept, rot and Masterful state unchanged", !IsPoisoned(target) && target.stackCount == 10 && M(target) == 4 && Rot(target).RotProgress == 500f);
        Check("S single: only that stack was purified", IsPoisoned(other));
        Check("S single: reservation released, one message, no scan of the map", Reservations() == 0 && messages.Count == 1 && scans == 0);

        ResetWorld(); cook = CookPawn(21, 0); target = Poisoned(2);
        job = (Job)Call(Purify, "MakeJob", target, false); driver = StartJob(cook, job, out started);
        Call(Purify, "Purify", target); RunJob(cook, driver, 1000);
        Check("S single: a stack already cleaned by someone else ends the job without error or duplicate credit", endCount == 1 && endedWith == JobCondition.Incompletable && messages.Count == 0);

        ResetWorld(); cook = CookPawn(21, 0); target = Poisoned(2);
        job = (Job)Call(Purify, "MakeJob", target, false); driver = StartJob(cook, job, out started);
        RunJob(cook, driver, 1000, t => { if (t == 3) Kill(target); });
        Check("S single: target destroyed mid-walk ends the job cleanly", endCount == 1 && endedWith == JobCondition.Incompletable);

        ResetWorld(); cook = CookPawn(21, 0); target = Poisoned(2);
        job = (Job)Call(Purify, "MakeJob", target, false); driver = StartJob(cook, job, out started);
        RunJob(cook, driver, 2, null); driver.Notify_PatherFailed();
        Check("S single: an unreachable target ends the job the vanilla way (pather error)", endCount == 1 && endedWith == JobCondition.ErroredPather);

        ResetWorld(); cook = CookPawn(21, 0); target = Poisoned(2);
        job = (Job)Call(Purify, "MakeJob", target, false); driver = StartJob(cook, job, out started);
        RunJob(cook, driver, 1000, t => { if (t == 100) capable = false; });
        Check("S single: an incapacitated Grandmaster stops the job and nothing is purified", endCount == 1 && endedWith == JobCondition.Incompletable && IsPoisoned(target));

        ResetWorld(); cook = CookPawn(21, 0); target = Poisoned(2);
        job = (Job)Call(Purify, "MakeJob", target, false); driver = StartJob(cook, job, out started);
        RunJob(cook, driver, 1000, t => { if (t == 100) Call(Purify, "Purify", target); });
        Check("S single: purified by someone else mid-work: job ends, no double message", endCount == 1 && endedWith == JobCondition.Incompletable && messages.Count == 0);

        // ---- I. Auto Purify: a finite cleanup
        ResetWorld(); cook = CookPawn(21, 0);
        var D = Poisoned(1); unreachable.Add(D);                         // nearest, but unreachable
        var A = Poisoned(2);
        var E = Poisoned(3); var eHolder = Holder(playerFaction, 6); var eJob = Occupy(eHolder, E, "ingest", false); // an ordinary pawn is about to eat it
        var F = Poisoned(4, poison: false);                              // clean
        var B = Poisoned(5); var G = Poisoned(7, ThingByName("ModdedStew"), 5); var C = Poisoned(9);
        Gm21PurifyOrders.OrderAuto(cook);
        Check("I Auto: starts with the nearest reachable, unreserved contaminated stack", ordered != null && ordered.def == Gm21CookingDefOf.GM21_PurifyFoodAuto && ordered.targetA.Thing == A && scans == 1);
        job = ordered; driver = StartJob(cook, job, out started);
        RunJob(cook, driver, 20000);
        Check("I Auto: cleaned the stacks in nearest-first order (A, E, B, G, C), including the one an ordinary pawn had reserved", walked.SequenceEqual(new Thing[] { A, E, B, G, C }));
        Check("I Auto: every reachable contaminated stack is clean afterwards", !IsPoisoned(A) && !IsPoisoned(E) && !IsPoisoned(B) && !IsPoisoned(G) && !IsPoisoned(C));
        Check("I Auto: the unreachable and the clean stack were left alone", IsPoisoned(D) && !IsPoisoned(F));
        Check("I Auto: the ordinary pawn's reservation was taken over by vanilla, ending its job once", Ended(eJob, JobCondition.InterruptForced) && EndedCount(eJob) == 1 && !HeldBy(E, eHolder));
        Check("I Auto: stops by itself, successfully, when none remain (one end, no restart)", endCount == 1 && endedWith == JobCondition.Succeeded && cook.jobs.curJob == null);
        Check("I Auto: only the order-time scan plus one search per stack finished (6), none after", scans == 6);
        Check("I Auto: one summary message for the whole run", messages.Count(m => m != null && m.Contains("GM21_Cook_AutoDone")) == 1);
        Check("I Auto: no reservation is left behind", Reservations() == 0);
        int scansAtEnd = scans; RunJob(cook, driver, 50);
        Check("I Auto: nothing scans or acts after the job ended (no standing loop)", scans == scansAtEnd && endCount == 1 && pathStarts == 5);

        ResetWorld(); cook = CookPawn(21, 0); ordered = null;
        Gm21PurifyOrders.OrderAuto(cook);
        Check("I Auto: with nothing to purify no job is started at all", ordered == null && messages.Count == 1);
        ResetWorld(); cook = CookPawn(21, 0); Poisoned(3); rookie = CookPawn(20, 0); ordered = null;
        Gm21PurifyOrders.OrderAuto(rookie);
        Check("I Auto: a level-20 cook cannot start it", ordered == null);
    }

    // Starts an Auto run through the production order path and returns its driver.
    static JobDriver AutoRun(Pawn cook)
    {
        ordered = null; Gm21PurifyOrders.OrderAuto(cook);
        bool ok; return ordered == null ? null : StartJob(cook, ordered, out ok);
    }

    static void AutoInterruptions()
    {
        // ---- I2. Each ordinary way a target can vanish drops THAT stack only; the run goes on.
        ResetWorld(); var cook = CookPawn(21, 0);
        var a = Poisoned(2); var b = Poisoned(4);
        var d = AutoRun(cook);
        RunJob(cook, d, 20000, t => { if (t == 3) Kill(a); });
        Check("I Auto: a target destroyed/eaten/hauled away mid-walk is dropped and the run continues", !IsPoisoned(b) && endCount == 1 && endedWith == JobCondition.Succeeded && walked[0] == a && walked.Contains(b));
        Check("I Auto: no reservation survives the vanished target", Reservations() == 0);

        ResetWorld(); cook = CookPawn(21, 0); a = Poisoned(2); b = Poisoned(4);
        d = AutoRun(cook);
        RunJob(cook, d, 20000, t => { if (t == 100) Call(Purify, "Purify", a); });
        Check("I Auto: a stack cleaned by someone else mid-work is dropped without credit; the run continues", !IsPoisoned(b) && endedWith == JobCondition.Succeeded && messages.Count(m => m != null && m.Contains("GM21_Cook_AutoDone")) == 1);

        ResetWorld(); cook = CookPawn(21, 0); a = Poisoned(2);
        d = AutoRun(cook);
        RunJob(cook, d, 20000, t => { if (t == 100) Call(Purify, "Purify", a); });
        Check("I Auto: if everything was cleaned by others there is nothing to report and the job simply ends", endCount == 1 && endedWith == JobCondition.Succeeded && messages.Count == 0);

        // ---- I3. Unreachable mid-walk: skipped for the rest of the run, never retried.
        ResetWorld(); cook = CookPawn(21, 0); a = Poisoned(2); b = Poisoned(4);
        d = AutoRun(cook);
        RunJob(cook, d, 2, null); d.Notify_PatherFailed();
        RunJob(cook, d, 20000);
        Check("I Auto: a stack the pather gives up on is skipped and never retried this run", IsPoisoned(a) && !IsPoisoned(b) && walked.Count(w => w == a) == 1 && endedWith == JobCondition.Succeeded && endCount == 1);

        // ---- I4. An ordinary pawn claims a stack between the search and the claim. Under forced semantics that no
        //          longer costs the stack: vanilla takes the reservation over (the full scenario set is in ForcedReservation).
        ResetWorld(); cook = CookPawn(21, 0); a = Poisoned(2); b = Poisoned(4); var c = Poisoned(6);
        d = AutoRun(cook); raceOnce = b; raceHolder = Holder(playerFaction, 7);
        RunJob(cook, d, 20000);
        Check("I Auto: a stack an ordinary pawn claims during the search is still purified (taken over, not skipped)", !IsPoisoned(a) && !IsPoisoned(b) && !IsPoisoned(c) && walked.Contains(b) && endedWith == JobCondition.Succeeded);

        // ---- I4b. A stack that LOOKS free but whose claim keeps failing is remembered, so it costs one attempt, not the budget.
        ResetWorld(); cook = CookPawn(21, 0); a = Poisoned(2); b = Poisoned(4); c = Poisoned(6);
        vetoClaim.Add(b);
        d = AutoRun(cook); RunJob(cook, d, 20000);
        Check("I Auto: a stack whose claim keeps failing is tried once and dropped; the rest of the run still completes", IsPoisoned(b) && !IsPoisoned(a) && !IsPoisoned(c) && reserveCalls <= 4 && endedWith == JobCondition.Succeeded);

        // ---- I5. The Grandmaster is incapacitated mid-run: the job stops, only finished stacks are clean.
        ResetWorld(); cook = CookPawn(21, 0); a = Poisoned(2); b = Poisoned(4); c = Poisoned(6);
        d = AutoRun(cook);
        RunJob(cook, d, 20000, t => { if (t == 260) capable = false; });
        Check("I Auto: an incapacitated Grandmaster ends the run at once; finished work stays, unfinished is untouched", endCount == 1 && endedWith == JobCondition.Incompletable && !IsPoisoned(a) && IsPoisoned(c));
        Check("I Auto: at most the stack being worked is still reserved (released by vanilla with the job)", Reservations() <= 1);

        // ---- I6. Interrupted from outside (drafted, ordered elsewhere): nothing stale is left for the next run.
        ResetWorld(); cook = CookPawn(21, 0); a = Poisoned(2); b = Poisoned(4);
        d = AutoRun(cook);
        RunJob(cook, d, 60);
        cook.jobs.EndCurrentJob(JobCondition.InterruptForced, true, true);
        Check("I Auto: interrupted from outside, the old driver holds nothing the next order depends on", endCount == 1 && cook.jobs.curJob == null && IsPoisoned(a));
        map.reservationManager = new ReservationManager(map); walked.Clear(); endCount = 0;
        d = AutoRun(cook); RunJob(cook, d, 20000);
        Check("I Auto: a fresh Auto after an interruption cleans everything (no stale skip list, no stuck state)", !IsPoisoned(a) && !IsPoisoned(b) && endedWith == JobCondition.Succeeded);

        // ---- I7. Scale and termination guards.
        ResetWorld(); cook = CookPawn(21, 0);
        var many = new List<ThingWithComps>(); for (int i = 0; i < 40; i++) many.Add(Poisoned(2 + i));
        d = AutoRun(cook); RunJob(cook, d, 400000);
        Check("I Auto: 40 stacks in one run all cleaned, ended once", many.All(t => !IsPoisoned(t)) && endCount == 1 && endedWith == JobCondition.Succeeded);
        Check("I Auto: exactly one search per stack plus the last empty one (41), one claim per stack (40)", scans == 41 && reserveCalls == 40);

        ResetWorld(); cook = CookPawn(21, 0); var real = Poisoned(2);
        for (int i = 0; i < 100; i++) unreachable.Add(Poisoned(3 + i));
        d = AutoRun(cook); RunJob(cook, d, 20000);
        Check("I Auto: 100 unreachable stacks are never visited or retried; the one reachable is cleaned", !IsPoisoned(real) && walked.Count == 1 && scans == 2 && endedWith == JobCondition.Succeeded);

        ResetWorld(); cook = CookPawn(21, 0); a = Poisoned(2);
        for (int i = 0; i < 40; i++) Poisoned(4 + i);
        d = AutoRun(cook); RunJob(cook, d, 20000, t => { if (t == 10) failEveryReserve = true; });
        Check("I Auto: if every claim keeps failing the run gives up after a bounded number of tries (no infinite loop)", endCount == 1 && endedWith == JobCondition.Succeeded && reserveCalls <= 1 + 32 && !IsPoisoned(a));
    }

    // ================================================================= FR: forced reservation semantics
    // Everything below runs against the REAL vanilla ReservationManager (Reserve, CanReserve, the playerForced
    // takeover, RespectsReservationsOf, ReleaseClaimedBy, Pawn_JobTracker.EndCurrentOrQueuedJob). Only the pather,
    // the map search and Pawn_JobTracker.EndCurrentJob (which needs a live game) are stand-ins.
    static Faction guestFaction, hostileFaction;

    static Job OrderOne(Pawn cook, Thing target)
    {
        ordered = null; Gm21PurifyOrders.OrderSingle(cook, target); return ordered;
    }
    static Job OrderAll(Pawn cook)
    {
        ordered = null; Gm21PurifyOrders.OrderAuto(cook); return ordered;
    }
    static string Reason(Pawn cook, Thing t)
    {
        object[] a = { cook, t, null };
        return (bool)Call(Purify, "CanOrder", a) ? null : ((string)a[2] ?? "(no reason)");
    }
    static bool CleanNoOneElse(Thing food) { return !IsPoisoned(food) && food.stackCount == 10; }

    static void ForcedReservation()
    {
        playerFaction = Empty<Faction>(); guestFaction = Empty<Faction>(); hostileFaction = Empty<Faction>();
        errors = 0;
        bool started; Pawn cook; Thing food; Job job; JobDriver driver;

        // ---- FR-A. Job flags: BOTH Purify jobs are playerForced, set by the one job-making seam.
        ResetWorld(); cook = CookPawn(21, 0); food = Poisoned(2);
        Job single = (Job)Call(Purify, "MakeJob", food, false), auto = (Job)Call(Purify, "MakeJob", food, true);
        Check("FR-A the Single job is playerForced, on the Single def, aimed at the stack", single.playerForced && single.def == Gm21CookingDefOf.GM21_PurifyFood && single.targetA.Thing == food);
        Check("FR-A the Auto job is playerForced, on the Auto def, aimed at the stack", auto.playerForced && auto.def == Gm21CookingDefOf.GM21_PurifyFoodAuto && auto.targetA.Thing == food);
        job = OrderOne(cook, food);
        Check("FR-A the production Single order hands vanilla an already-playerForced job (TryTakeOrderedJob is a stand-in here, so the flag can only come from the job)", job != null && job.playerForced && job.def == Gm21CookingDefOf.GM21_PurifyFood);
        job = OrderAll(cook);
        Check("FR-A the production Auto order hands vanilla an already-playerForced job", job != null && job.playerForced && job.def == Gm21CookingDefOf.GM21_PurifyFoodAuto);
        driver = StartJob(cook, job, out started); RunJob(cook, driver, 20000);
        Check("FR-A the flag survives a whole Auto run (it is one Job for the run)", started && endedWith == JobCondition.Succeeded && job.playerForced);

        // ---- FR-B. Manual Purify against ordinary AI reservations: NOT refused, and vanilla takes the stack over.
        var holders = new[]
        {
            new { name = "a colonist about to eat it (partial-stack ingest reservation)", kind = "ingest", faction = playerFaction },
            new { name = "a colonist hauling it (whole-stack reservation)", kind = "haul", faction = playerFaction },
            new { name = "a quest-guest-like pawn of another, non-hostile faction about to eat it", kind = "ingest", faction = guestFaction },
        };
        foreach (var k in holders)
        {
            ResetWorld(); cook = CookPawn(21, 0); food = Poisoned(2, count: 10, masterful: 5, rot: 400f);
            Pawn holder = Holder(k.faction, 6); Job theirs = Occupy(holder, food, k.kind, false);
            Check("FR-B [" + k.name + "] ordinary CanReserve is false (this is what wrongly refused the order before)", !cook.CanReserve(food, 1, -1, null, false));
            Check("FR-B [" + k.name + "] the forced claim is possible, and the order is accepted with no refusal", cook.CanReserve(food, 1, -1, null, true) && Reason(cook, food) == null);
            job = OrderOne(cook, food);
            Check("FR-B [" + k.name + "] the order is issued, playerForced, at the stack", job != null && job.playerForced && job.targetA.Thing == food);
            Check("FR-B [" + k.name + "] ordering displaces nobody: vanilla acts only when the job reserves", !Ended(theirs, JobCondition.InterruptForced) && HeldBy(food, holder));
            driver = StartJob(cook, job, out started);
            Check("FR-B [" + k.name + "] the job starts and the cook holds the stack", started && HeldBy(food, cook));
            Check("FR-B [" + k.name + "] vanilla ended the other pawn's job exactly once, as InterruptForced, and that pawn holds nothing", Ended(theirs, JobCondition.InterruptForced) && EndedCount(theirs) == 1 && !HeldBy(food, holder) && holder.jobs.curJob == null);
            Check("FR-B [" + k.name + "] GM21 did not recreate, queue or restart the displaced job", holder.jobs.jobQueue.Count == 0 && ordered == job);
            RunJob(cook, driver, 2000);
            Check("FR-B [" + k.name + "] the stack is purified; count, Masterful servings and rot are exactly as before", !IsPoisoned(food) && food.stackCount == 10 && M(food) == 5 && Rot(food).RotProgress == 400f && endedWith == JobCondition.Succeeded);
            Check("FR-B [" + k.name + "] nothing is left reserved, the run logged no vanilla error", Reservations() == 0 && errors == 0);
            Check("FR-B [" + k.name + "] the displaced pawn can simply claim the (clean) stack again as ordinary work", holder.CanReserve(food, 10, 1, null, false) && holder.Reserve(food, JobMaker.MakeJob(ingestDef, food), 10, 1, null, false));
        }

        // ---- FR-B2. Fundamentally invalid targets are still refused, each with its own reason.
        ResetWorld(); cook = CookPawn(21, 0); var rookie = CookPawn(20, 0);
        var clean = Poisoned(2, poison: false); var dead = Poisoned(3); Kill(dead);
        var unspawned = Poisoned(4); spawned.Remove(unspawned); field.Remove(unspawned);
        var abroad = Poisoned(5); elsewhere.Add(abroad);
        var far = Poisoned(6); unreachable.Add(far);
        var vetoed = Poisoned(7); vetoClaim.Add(vetoed);
        var okay = Poisoned(8);
        Check("FR-B2 a clean stack is refused: not contaminated", Reason(cook, clean) == "GM21_Cook_NotContaminated");
        Check("FR-B2 a destroyed stack is refused: not contaminated", Reason(cook, dead) == "GM21_Cook_NotContaminated");
        Check("FR-B2 a despawned stack is refused: not contaminated", Reason(cook, unspawned) == "GM21_Cook_NotContaminated");
        Check("FR-B2 a stack on another map is refused", Reason(cook, abroad) == "GM21_Cook_NotContaminated");
        Check("FR-B2 an unreachable stack is refused: cannot reach", Reason(cook, far) == "GM21_Cook_CannotReach");
        Check("FR-B2 a stack that cannot legally be claimed even under forced semantics is refused: cannot claim", Reason(cook, vetoed) == "GM21_Cook_CannotClaim");
        Check("FR-B2 a cook who is not a practising Grandmaster is refused", Reason(rookie, okay) == "GM21_Cook_NotPractising");
        capable = false; Check("FR-B2 a Grandmaster who cannot use their hands is refused", Reason(cook, okay) == "GM21_Cook_NotPractising"); capable = true;
        var heldFar = Poisoned(10); unreachable.Add(heldFar); Occupy(Holder(playerFaction, 9), heldFar, "haul", false);
        Check("FR-B2 an ordinary reservation does not hide a real problem: held AND unreachable is still refused as unreachable", Reason(cook, heldFar) == "GM21_Cook_CannotReach");
        Check("FR-B2 the valid stack is accepted", Reason(cook, okay) == null);

        // ---- FR-C. Auto against ordinary AI reservations.
        ResetWorld(); cook = CookPawn(21, 0);
        var free = Poisoned(2);
        var eaten = Poisoned(3); var hEat = Holder(playerFaction, 12); var jEat = Occupy(hEat, eaten, "ingest", false);
        var hauled = Poisoned(4); var hHaul = Holder(playerFaction, 13); var jHaul = Occupy(hHaul, hauled, "haul", false);
        var guestMeal = Poisoned(5); var hGuest = Holder(guestFaction, 14); var jGuest = Occupy(hGuest, guestMeal, "ingest", false);
        var notDirty = Poisoned(6, poison: false); var farOff = Poisoned(7); unreachable.Add(farOff);
        var veto = Poisoned(8); vetoClaim.Add(veto); var overseas = Poisoned(9); elsewhere.Add(overseas);
        Check("FR-C the search skips a free stack only when told to, and then prefers the nearest ordinarily-held one (ordinary reservations do not exclude a candidate)",
            Call(Purify, "FindNearest", cook, null) == free && Call(Purify, "FindNearest", cook, new List<int> { free.thingIDNumber }) == eaten
            && Call(Purify, "FindNearest", cook, new List<int> { free.thingIDNumber, eaten.thingIDNumber, hauled.thingIDNumber }) == guestMeal);
        Check("FR-C the search never returns: a clean stack, an unreachable one, one that cannot be claimed even under forced semantics, or one on another map",
            Call(Purify, "FindNearest", cook, new List<int> { free.thingIDNumber, eaten.thingIDNumber, hauled.thingIDNumber, guestMeal.thingIDNumber }) == null);
        scans = 0; job = OrderAll(cook);
        Check("FR-C the Auto order is accepted although the nearest stacks are ordinarily reserved, and starts on the nearest one", job != null && job.playerForced && job.targetA.Thing == free && scans == 1);
        driver = StartJob(cook, job, out started); RunJob(cook, driver, 20000);
        Check("FR-C Auto cleaned the free stack AND every ordinarily reserved one, nearest first", walked.SequenceEqual(new Thing[] { free, eaten, hauled, guestMeal }) && !IsPoisoned(free) && !IsPoisoned(eaten) && !IsPoisoned(hauled) && !IsPoisoned(guestMeal));
        Check("FR-C Auto left alone the clean, the unreachable, the un-claimable and the other-map stacks", !IsPoisoned(notDirty) && IsPoisoned(farOff) && IsPoisoned(veto) && IsPoisoned(overseas));
        Check("FR-C each ordinary pawn's job was ended by vanilla exactly once, as InterruptForced", new[] { jEat, jHaul, jGuest }.All(j => Ended(j, JobCondition.InterruptForced) && EndedCount(j) == 1));
        Check("FR-C the displaced pawns hold nothing and were not given anything to do by GM21", !HeldBy(eaten, hEat) && !HeldBy(hauled, hHaul) && !HeldBy(guestMeal, hGuest) && new[] { hEat, hHaul, hGuest }.All(p => p.jobs.curJob == null && p.jobs.jobQueue.Count == 0));
        Check("FR-C Auto ended once, successfully, with no reservation left and no vanilla error", endCount == 1 && endedWith == JobCondition.Succeeded && Reservations() == 0 && errors == 0);
        Check("FR-C the searches are the order's, one per following stack, and the final empty one (5); nothing polled", scans == 5);

        // ---- FR-D. The initial Auto target is taken by ordinary AI between the order and the start: the run survives.
        foreach (var k in holders)
        {
            ResetWorld(); cook = CookPawn(21, 0); var first = Poisoned(2); var second = Poisoned(5);
            job = OrderAll(cook);
            Pawn late = Holder(k.faction, 8); Job theirs = Occupy(late, first, k.kind, false); // right after the order, before the job runs
            driver = StartJob(cook, job, out started);
            Check("FR-D [" + k.name + "] the Auto job starts although its first target was just taken, and holds it", started && HeldBy(first, cook) && job.targetA.Thing == first);
            Check("FR-D [" + k.name + "] vanilla displaced the other pawn once", Ended(theirs, JobCondition.InterruptForced) && EndedCount(theirs) == 1 && !HeldBy(first, late));
            RunJob(cook, driver, 20000);
            Check("FR-D [" + k.name + "] the whole run completes: both stacks clean, one clean end, nothing left reserved, no error", !IsPoisoned(first) && !IsPoisoned(second) && endCount == 1 && endedWith == JobCondition.Succeeded && Reservations() == 0 && errors == 0 && job.playerForced);
        }
        ResetWorld(); cook = CookPawn(21, 0); food = Poisoned(2);
        job = OrderOne(cook, food); Occupy(Holder(playerFaction, 8), food, "haul", false);
        driver = StartJob(cook, job, out started); RunJob(cook, driver, 2000);
        Check("FR-D a SINGLE order whose stack is taken meanwhile also just takes it over and finishes", started && !IsPoisoned(food) && endedWith == JobCondition.Succeeded && Reservations() == 0);

        // ---- FR-E. The initial target becomes genuinely impossible: Auto discards it and acquires normally; Single fails normally.
        var breakers = new Dictionary<string, Action<Thing>>
        {
            { "destroyed", t => Kill(t) },
            { "despawned", t => { spawned.Remove(t); field.Remove(t); } },
            { "moved to another map", t => { elsewhere.Add(t); } },
            { "purified by someone else", t => { Call(Purify, "Purify", t); } },
            { "impossible to claim even under forced semantics", t => { vetoClaim.Add(t); } },
        };
        foreach (var b in breakers)
        {
            ResetWorld(); cook = CookPawn(21, 0); var bad = Poisoned(2); var next = Poisoned(5); var afterThat = Poisoned(8);
            job = OrderAll(cook); b.Value(bad); errors = 0;
            driver = StartJob(cook, job, out started, errorOnFailed: true); // errorOnFailed as vanilla's order path passes it
            Check("FR-E Auto [" + b.Key + "]: the job still starts, and vanilla is not asked to report a failed reservation", started && errors == 0);
            RunJob(cook, driver, 20000);
            Check("FR-E Auto [" + b.Key + "]: the dead pick was discarded (never walked to, never purified, not held); normal acquisition then cleaned the other stacks in order",
                !walked.Contains(bad) && !HeldBy(bad, cook) && !IsPoisoned(next) && !IsPoisoned(afterThat) && walked.SequenceEqual(new Thing[] { next, afterThat }));
            Check("FR-E Auto [" + b.Key + "]: the run ended once, successfully; nothing left reserved; no vanilla error", endCount == 1 && endedWith == JobCondition.Succeeded && Reservations() == 0 && errors == 0);

            ResetWorld(); cook = CookPawn(21, 0); bad = Poisoned(2);
            job = OrderOne(cook, bad); b.Value(bad); errors = 0;
            driver = StartJob(cook, job, out started);
            // Vanilla refuses a reservation on a destroyed / other-map / un-claimable target, so the order does not start; a
            // despawned or already-clean stack can still be reserved and is dropped by the job's first toil. Either way, it just ends.
            bool refusedUpFront = b.Key != "despawned" && b.Key != "purified by someone else";
            Check("FR-E Single [" + b.Key + "]: the order " + (refusedUpFront ? "is refused by vanilla's reservation, so it never starts" : "starts and is dropped by its first toil"), started == !refusedUpFront);
            if (started) RunJob(cook, driver, 2000);
            Check("FR-E Single [" + b.Key + "]: an explicit target that became impossible simply fails: no job left, nothing reserved, no message, no error",
                cook.jobs.curJob == null && Reservations() == 0 && messages.Count == 0 && errors == 0 && (b.Key == "purified by someone else" || IsPoisoned(bad)));
        }
        ResetWorld(); cook = CookPawn(21, 0); var lone = Poisoned(2);
        job = OrderAll(cook); Kill(lone); errors = 0;
        driver = StartJob(cook, job, out started, errorOnFailed: true); RunJob(cook, driver, 2000);
        Check("FR-E Auto: if the discarded first pick was the only one, the run ends cleanly with nothing done and no message", started && endCount == 1 && endedWith == JobCondition.Succeeded && messages.Count == 0 && errors == 0 && Reservations() == 0);

        // ---- FR-F. Cleanup, and the displaced pawn simply carries on.
        ResetWorld(); cook = CookPawn(21, 0); food = Poisoned(2); var other = Poisoned(9);
        Pawn eater = Holder(playerFaction, 5); Job eating = Occupy(eater, food, "ingest", false);
        job = OrderOne(cook, food); driver = StartJob(cook, job, out started); RunJob(cook, driver, 60);
        Check("FR-F while the cook works, an ordinary pawn cannot take the stack back (the cook's forced reservation is respected)",
            HeldBy(food, cook) && !eater.CanReserve(food, 10, 1, null, false) && !eater.Reserve(food, JobMaker.MakeJob(ingestDef, food), 10, 1, null, false) && HeldBy(food, cook) && cook.jobs.curJob == job);
        RunJob(cook, driver, 2000);
        Check("FR-F when the work is done, every reservation is gone", endedWith == JobCondition.Succeeded && Reservations() == 0 && !HeldBy(food, cook));
        Check("FR-F the displaced pawn takes a different meal exactly as any pawn would", eater.CanReserve(other, 10, 1, null, false) && eater.Reserve(other, JobMaker.MakeJob(ingestDef, other), 10, 1, null, false) && HeldBy(other, eater));
        ResetWorld(); cook = CookPawn(21, 0); food = Poisoned(2); Occupy(Holder(playerFaction, 5), food, "haul", false);
        job = OrderOne(cook, food); driver = StartJob(cook, job, out started); RunJob(cook, driver, 60);
        cook.jobs.EndCurrentJob(JobCondition.InterruptForced, true, true);
        Check("FR-F if the cook is interrupted after taking a stack over, vanilla's job cleanup releases it and the stack is free for anyone", Reservations() == 0 && IsPoisoned(food) && Ended(job, JobCondition.InterruptForced));
        Pawn anyone = Holder(playerFaction, 6);
        Check("FR-F ... including an ordinary pawn", anyone.CanReserve(food, 1, -1, null, false) && anyone.Reserve(food, JobMaker.MakeJob(haulDef, food), 1, -1, null, false));

        // ---- FR-G. Quest-guest-like autonomous ingest (a pawn of a non-hostile foreign faction): emergent, no guest code.
        ResetWorld(); cook = CookPawn(21, 0); food = Poisoned(2, count: 4); Pawn guest = Holder(guestFaction, 6);
        Job guestEating = Occupy(guest, food, "ingest", false);
        Check("FR-G the guest's reservation is respected by ordinary AI: vanilla treats a non-hostile faction like anyone (CanReserve false)", !cook.CanReserve(food, 1, -1, null, false) && !Holder(playerFaction, 7).CanReserve(food, 1, -1, null, false));
        job = OrderOne(cook, food); driver = StartJob(cook, job, out started); RunJob(cook, driver, 2000);
        Check("FR-G the guest's ingest is interrupted by vanilla (InterruptForced, once), the stack is purified, no error, nothing left reserved",
            Ended(guestEating, JobCondition.InterruptForced) && EndedCount(guestEating) == 1 && !IsPoisoned(food) && errors == 0 && Reservations() == 0 && endedWith == JobCondition.Succeeded);
        ResetWorld(); cook = CookPawn(21, 0); food = Poisoned(2); Pawn mixedA = Holder(playerFaction, 5), mixedB = Holder(guestFaction, 6);
        Job ja = Occupy(mixedA, food, "ingest", false), jb = Occupy(mixedB, food, "ingest", false);
        job = OrderOne(cook, food); driver = StartJob(cook, job, out started);
        Check("FR-G a colonist and a guest sharing one stack's ingest reservation are BOTH displaced, once each", started && Ended(ja, JobCondition.InterruptForced) && Ended(jb, JobCondition.InterruptForced) && EndedCount(ja) == 1 && EndedCount(jb) == 1 && HeldBy(food, cook));
        ResetWorld(); cook = CookPawn(21, 0); food = Poisoned(2); Pawn foe = Holder(hostileFaction, 6); hostilePairs.Add(new[] { playerFaction, hostileFaction });
        Job foeJob = Occupy(foe, food, "haul", false);
        job = OrderOne(cook, food); driver = StartJob(cook, job, out started);
        Check("FR-G a pawn of a HOSTILE faction is not respected by vanilla, so it is neither asked about nor interrupted (vanilla's rule, untouched)", started && !Ended(foeJob, JobCondition.InterruptForced) && HeldBy(food, cook) && HeldBy(food, foe));

        // ---- FR-H. Player-forced conflicts: vanilla's rule, no custom hierarchy.
        ResetWorld(); cook = CookPawn(21, 0); food = Poisoned(2); Pawn forcedHolder = Holder(playerFaction, 6);
        Job forcedIngest = Occupy(forcedHolder, food, "ingest", true);
        Check("FR-H an explicitly ordered meal is still an accepted target (vanilla forced-versus-forced: the newer explicit order wins)", Reason(cook, food) == null);
        job = OrderOne(cook, food); driver = StartJob(cook, job, out started);
        Check("FR-H Purify takes it from another pawn's PLAYER-FORCED job exactly as any forced order would: that job ended once, InterruptForced", started && Ended(forcedIngest, JobCondition.InterruptForced) && EndedCount(forcedIngest) == 1 && HeldBy(food, cook));

        ResetWorld(); cook = CookPawn(21, 0); var cook2 = CookPawn(21, 0); food = Poisoned(2); var second2 = Poisoned(9);
        Job j1 = OrderOne(cook, food); var d1 = StartJob(cook, j1, out started); RunJob(cook, d1, 60);
        Job j2 = OrderOne(cook2, food);
        Check("FR-H a second Grandmaster may order the stack the first is purifying (accepted, not refused)", j2 != null && j2.playerForced);
        var d2 = StartJob(cook2, j2, out started);
        Check("FR-H the later order wins, by vanilla: the first Purify job ends once as InterruptForced; only the second holds the stack", started && Ended(j1, JobCondition.InterruptForced) && EndedCount(j1) == 1 && HeldBy(food, cook2) && !HeldBy(food, cook));
        RunJob(cook2, d2, 2000);
        Check("FR-H the second finishes; the stack is purified once (one message), nothing is left reserved", !IsPoisoned(food) && messages.Count(m => m != null && m.Contains("GM21_Cook_Purified")) == 1 && Reservations() == 0 && errors == 0);

        ResetWorld(); cook = CookPawn(21, 0); cook2 = CookPawn(21, 0); var s1 = Poisoned(2); var s2 = Poisoned(4); var s3 = Poisoned(6);
        j1 = OrderAll(cook); d1 = StartJob(cook, j1, out started); RunJob(cook, d1, 60);
        j2 = OrderAll(cook2); d2 = StartJob(cook2, j2, out started);
        Check("FR-H two Auto runs: the later takes the stack the earlier is working on; the earlier run ENDS (vanilla ends a displaced job whole, it is not resumed)", Ended(j1, JobCondition.InterruptForced) && EndedCount(j1) == 1 && HeldBy(s1, cook2) && cook.jobs.curJob == null);
        int guard = RunJob(cook2, d2, 100000);
        Check("FR-H ... and there is no ping-pong: the later run cleans everything, ends once, and the simulation stops long before the guard", guard < 100000 && !IsPoisoned(s1) && !IsPoisoned(s2) && !IsPoisoned(s3) && EndedCount(j2) == 1 && Reservations() == 0);

        ResetWorld(); cook = CookPawn(21, 0); food = Poisoned(2);
        job = OrderOne(cook, food); driver = StartJob(cook, job, out started); RunJob(cook, driver, 60);
        Pawn hauler = Holder(playerFaction, 6); Job forcedHaul = JobMaker.MakeJob(haulDef, food); forcedHaul.playerForced = true;
        bool took = hauler.Reserve(food, forcedHaul, 1, -1, null, false);
        Check("FR-H a later player-forced order for someone else takes the stack from a working Purify by the same vanilla rule (Purify job ended once, InterruptForced, cook holds nothing)",
            took && Ended(job, JobCondition.InterruptForced) && EndedCount(job) == 1 && !HeldBy(food, cook) && HeldBy(food, hauler) && IsPoisoned(food));
        Check("FR-H nothing in GM21 fights back: the interrupted driver is not resumed (no further Purify activity)", RunJob(cook, driver, 300) == 0 && IsPoisoned(food));

        // ---- FR-I. Perfect Hygiene end to end through the real recipe path (regression; not broadened).
        var gm = Cook(21); var l20 = Cook(20);
        var recipe = Recipe("FixtureHygiene", new ThingDefCountClass(mealDef, 10));
        chanceScript.Clear(); chanceDefault = true; randCalls = 0; statValue = 0.3f;
        var fresh = GenRecipe.MakeRecipeProducts(recipe, gm, new List<Thing>(), null, null).ToList();
        Check("FR-I a fresh meal from a Cooking Grandmaster has no FilthyKitchen poisoning even when the kitchen roll would hit (no rolls consumed)", fresh.Count == 1 && !IsPoisoned(fresh[0]) && randCalls == 0 && fresh[0].TryGetComp<CompFoodPoisonable>().Cause == FoodPoisonCause.Unknown);
        chanceScript.Clear(); chanceDefault = false; chanceScript.Enqueue(false); chanceScript.Enqueue(true); randCalls = 0;
        fresh = GenRecipe.MakeRecipeProducts(recipe, gm, new List<Thing>(), null, null).ToList();
        Check("FR-I ... nor IncompetentCook poisoning when only the incompetent-cook roll would hit", fresh.Count == 1 && !IsPoisoned(fresh[0]) && randCalls == 0);
        chanceScript.Clear(); chanceDefault = true; randCalls = 0;
        fresh = GenRecipe.MakeRecipeProducts(recipe, l20, new List<Thing>(), null, null).ToList();
        Check("FR-I a level-20 cook through the same path is still poisoned by vanilla (Perfect Hygiene is not broadened)", fresh.Count == 1 && IsPoisoned(fresh[0]) && randCalls >= 1);
        chanceDefault = false; chanceScript.Clear(); statValue = 0f; randCalls = 0;
    }

    static void UninstallAndGizmo()
    {
        // ---- U. Prepare Save for Uninstall: nothing that names a Cooking Def may survive in a pawn.
        ResetWorld(); var cook = CookPawn(21, 0);
        var eater = cook;
        eater.needs = Empty<Pawn_NeedsTracker>(); eater.needs.mood = Empty<Need_Mood>(); eater.needs.mood.thoughts = Empty<ThoughtHandler>();
        eater.needs.mood.thoughts.memories = Empty<MemoryThoughtHandler>();
        var mem = new List<Thought_Memory>();
        for (int i = 0; i < 2; i++) { var m = Empty<Thought_Memory>(); m.def = Gm21CookingDefOf.GM21_MasterfulMeal; mem.Add(m); }
        var other = Empty<Thought_Memory>(); other.def = Empty<ThoughtDef>(); other.def.defName = "AteFineMeal"; mem.Add(other);
        Set(eater.needs.mood.thoughts.memories, "memories", mem);
        var a = Poisoned(2); Job running = (Job)Call(Purify, "MakeJob", a, true); bool ok; StartJob(cook, running, out ok);
        var queuedPurify = (Job)Call(Purify, "MakeJob", Poisoned(3), false);
        cook.jobs.jobQueue.EnqueueLast(queuedPurify, JobTag.Misc);
        var queuedOther = JobMaker.MakeJob(new JobDef { defName = "SomethingElse" });
        cook.jobs.jobQueue.EnqueueLast(queuedOther, JobTag.Misc);
        int cleared = (int)Call(typeof(Gm21Mod).Assembly.GetType("Grandmaster21.Gm21CookingUninstall"), "CleanPawn", cook);
        Check("U every Masterful Meal memory is removed and other memories are untouched", mem.Count == 1 && mem[0] == other && RemovedMemories);
        Check("U a Purify job in progress is ended", endCount >= 1 && cook.jobs.curJob == null);
        Check("U a queued Purify job is dropped and an unrelated queued job is left alone", !cook.jobs.jobQueue.Any(q => q.job == queuedPurify) && cook.jobs.jobQueue.Any(q => q.job == queuedOther));
        Check("U the count reports everything removed (2 memories + 1 running + 1 queued)", cleared == 4);
        Check("U a null pawn is harmless", (int)Call(typeof(Gm21Mod).Assembly.GetType("Grandmaster21.Gm21CookingUninstall"), "CleanPawn", new object[] { null }) == 0);

        // ---- Gizmo: right-click is vanilla's float-menu route and offers exactly Auto Purify.
        var cmd = new Command_Gm21PurifyFood(); cmd.cook = cook;
        ResetWorld(); cook = CookPawn(21, 0); cmd.cook = cook; var toClean = Poisoned(2);
        menuEntries.Clear(); var options = cmd.RightClickFloatMenuOptions.ToList();
        Check("gizmo: right-click offers exactly one entry, Auto Purify", options.Count == 1 && menuEntries.Count == 1 && menuEntries[0].Key.Contains("GM21_Cook_AutoPurify"));
        ordered = null; menuEntries[0].Value();
        Check("gizmo: choosing Auto Purify starts the finite Auto job on the nearest contaminated stack", ordered != null && ordered.def == Gm21CookingDefOf.GM21_PurifyFoodAuto && ordered.targetA.Thing == toClean);
        Check("gizmo: not groupable (each Grandmaster acts on their own behalf)", !cmd.GroupsWith(new Command_Gm21PurifyFood { cook = cook }));
        menuEntries.Clear();
        Check("gizmo: without a Grandmaster there is no menu entry", new Command_Gm21PurifyFood().RightClickFloatMenuOptions.Count() == 0 && menuEntries.Count == 0);
    }

    static ThingDef ThingByName(string name) { return DefDatabase<ThingDef>.GetNamedSilentFail(name); }
    static int Gm21Cooking_PurifyTicks() { return (int)AccessTools.Field(Cooking, "PurifyWorkTicks").GetValue(null); }

    // ================================================================= main
    static int Main(string[] args)
    {
        try
        {
            scratch = args.Length > 1 ? args[1] : Path.GetTempPath();
            Audit(args[0]);
            var h = new Harmony("Grandmaster21.Cooking.Tests.Environment"); Environment(h);
            map = Empty<Map>(); otherMap = Empty<Map>();
            // The production startup class: its static constructor applies every real Harmony group to the
            // pristine vanilla methods and binds Purify. Defs are attached afterwards, by the fixture, through
            // the same production AttachMasterfulComp.
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(Startup.TypeHandle);
            Bindings();
            Binding();
            Hygiene();
            Creation();
            Stacks();
            Ingestion();
            Freshness();
            SaveLoad();
            PurifyFood();
            AutoInterruptions();
            ForcedReservation();
            UninstallAndGizmo();
        }
        catch (Exception e) { Console.WriteLine("FAIL unhandled fixture: " + e); fail++; }
        Console.WriteLine("Cooking: " + pass + " PASS, " + fail + " FAIL");
        return fail == 0 ? 0 : 1;
    }
}
