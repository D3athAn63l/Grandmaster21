// Medicine 21 headless checks, run against the REAL RimWorld 1.6, Unity and Harmony assemblies.
//
// What makes these more than logic tests: Gm21MedicinePatches.Apply is run for real, so the mod's
// actual Harmony patches are installed on the actual vanilla methods, and then those vanilla
// methods are executed -- HediffComp_TendDuration.CompTended (reached through Hediff.Tended),
// Hediff_Injury.Heal, SurgeryOutcomeEffectDef.GetOutcome, HediffSet.GetMissingPartsCommonAncestors
// and the real Scribe saver/loader. Section 21 does the same for the core bill-ceiling bridge on
// the real Bill.PawnAllowedToStartAnew.
//
// Test-process-only environment shims (never shipped, never in the mod):
//   * ContentFinder<Texture2D>.Get returns null -- HediffComp_TendDuration's static constructor loads
//     icons through Unity, which only exists inside the player;
//   * Pawn_HealthTracker.Notify_HediffChanged / RemoveHediff are reduced to list bookkeeping -- the
//     real ones re-evaluate a live pawn's capacities and death state, which needs a whole game;
//   * PawnCapacitiesHandler.CapableOf and RaceProperties.IsMechanoid answer from the fixture.
//
//   * Pawn.HealthScale is 1 and PawnCapacitiesHandler.GetLevel answers from the fixture;
//   * ThingIDMaker.GiveIDTo hands out test IDs, and Thing.RemoveAllReservationsAndDesignationsOnThis
//     and ThingOwnerUtility.ShouldRemoveDesignationsOnAddedThings skip the maps' designations, and
//     ReliquaryUtility.IsRelic (which reads the mods config file) answers false -- all
//     reach for a running game's managers -- so real medicine Things can be added, split and
//     destroyed in a real inventory ThingOwner;
//   * Find.ActiveLanguageWorker is vanilla's default worker, so formatted keys can be built;
//   * section 21 only: Bill.PawnAllowedToStartAnew's single ModsConfig.BiotechActive read answers
//     "off" (ModsConfig cannot initialise headless), installed before the unpatched baseline.
//
// NOT covered: a running map, jobs, pathing, targeting UI, save/reload of a whole game, revival.
// Those are listed as the runtime checklist in Docs/Medicine21.md.
//
//   mono MedicineChecks.exe <Grandmaster21.dll> <repo root> <scratch xml path> <Assembly-CSharp.dll>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Grandmaster21;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
using RimWorld;
using Verse;

internal static class MedicineChecks
{
    static int pass, fail, blocked;
    static readonly BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static readonly Assembly Mod = typeof(Gm21Medicine).Assembly;

    static void Check(string name, bool ok, string detail = "")
    {
        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name + (detail == "" ? "" : "   [" + detail + "]"));
        if (ok) pass++; else fail++;
    }

    static void Blocked(string name, Exception e)
    {
        Console.WriteLine("BLOCKED  " + name + "   [" + e.GetBaseException().GetType().Name + ": "
                          + e.GetBaseException().Message + "]");
        string trace = (e.GetBaseException().StackTrace ?? "") + "\n" + (e.StackTrace ?? "");
        foreach (string line in trace.Split('\n').Take(24)) Console.WriteLine("           " + line.Trim());
        blocked++;
    }

    static T Uninit<T>() { return (T)FormatterServices.GetUninitializedObject(typeof(T)); }
    static void Set(object o, string field, object value) { AccessTools.Field(o.GetType(), field).SetValue(o, value); }
    static object Get(object o, string field) { return AccessTools.Field(o.GetType(), field).GetValue(o); }
    static object CallStatic(Type t, string name, params object[] args) { return AccessTools.Method(t, name).Invoke(null, args); }

    // ------------------------------------------------------------------ environment

    static bool capable = true;
    static readonly List<Hediff> removedLog = new List<Hediff>();

    public static bool NullTexture(ref UnityEngine.Texture2D __result) { __result = null; return false; }
    public static bool SkipNotify() { return false; }
    public static bool ListOnlyRemove(Pawn_HealthTracker __instance, Hediff hediff)
    {
        __instance.hediffSet.hediffs.Remove(hediff);
        removedLog.Add(hediff);
        return false;
    }
    public static bool FixtureCapable(ref bool __result) { __result = capable; return false; }
    public static bool NotMechanoid(ref bool __result) { __result = false; return false; }
    public static bool UnitHealthScale(ref float __result) { __result = 1f; return false; }
    static float manipulationLevel = 1f;
    public static bool FixtureLevel(ref float __result) { __result = manipulationLevel; return false; }
    public static bool NoMapDesignations(ref bool __result) { __result = false; return false; }
    static readonly LanguageWorker defaultLanguageWorker = new LanguageWorker_Default();
    public static bool DefaultLanguageWorker(ref LanguageWorker __result) { __result = defaultLanguageWorker; return false; }
    static int nextThingId = 5000;
    public static bool FixtureThingId(Thing t) { t.thingIDNumber = nextThingId++; return false; }
    public static readonly List<string> logged = new List<string>();
    public static bool ConsoleLog(string text)
    {
        logged.Add(text);
        if (quietMissingLanguage && text.StartsWith("No active language!")) return false;
        Console.WriteLine("        [game log] " + text);
        return false;
    }
    static bool quietMissingLanguage;   // section 21 formats hundreds of refusal reasons with no language loaded

    static HarmonyMethod Stub(string n) { return new HarmonyMethod(typeof(MedicineChecks).GetMethod(n)); }

    static void InstallEnvironment(Harmony h)
    {
        h.Patch(AccessTools.Method(typeof(ContentFinder<UnityEngine.Texture2D>), "Get"), Stub("NullTexture"));
        h.Patch(AccessTools.Method(typeof(Pawn_HealthTracker), "Notify_HediffChanged"), Stub("SkipNotify"));
        h.Patch(AccessTools.Method(typeof(Pawn_HealthTracker), "RemoveHediff"), Stub("ListOnlyRemove"));
        h.Patch(AccessTools.Method(typeof(PawnCapacitiesHandler), "CapableOf"), Stub("FixtureCapable"));
        h.Patch(AccessTools.PropertyGetter(typeof(RaceProperties), "IsMechanoid"), Stub("NotMechanoid"));
        h.Patch(AccessTools.PropertyGetter(typeof(Pawn), "HealthScale"), Stub("UnitHealthScale"));
        h.Patch(AccessTools.Method(typeof(PawnCapacitiesHandler), "GetLevel"), Stub("FixtureLevel"));
        h.Patch(AccessTools.Method(typeof(ThingIDMaker), "GiveIDTo"), Stub("FixtureThingId"));
        h.Patch(AccessTools.Method(typeof(Thing), "RemoveAllReservationsAndDesignationsOnThis"), Stub("SkipNotify"));
        h.Patch(AccessTools.Method(typeof(ThingOwnerUtility), "ShouldRemoveDesignationsOnAddedThings"), Stub("NoMapDesignations"));
        // Stacking asks whether either Thing is an Ideology relic, which reads the mods config file.
        h.Patch(AccessTools.Method(typeof(ReliquaryUtility), "IsRelic"), Stub("NoMapDesignations"));
        // Formatted translations ask the active language for its grammar worker; there is no language headless.
        h.Patch(AccessTools.PropertyGetter(typeof(Find), "ActiveLanguageWorker"), Stub("DefaultLanguageWorker"));
        // Verse.Log bottoms out in Unity's logger; route it to the console so messages are visible.
        foreach (string level in new[] { "Message", "Warning", "Error" })
            h.Patch(AccessTools.Method(typeof(Log), level, new[] { typeof(string) }), Stub("ConsoleLog"));

        // Bind the DefOf classes the mod reads, under the same guard the game's loader uses.
        FieldInfo binding = typeof(DefOfHelper).GetField("bindingNow", Any);
        binding.SetValue(null, true);
        try
        {
            foreach (Type t in new[] { typeof(SkillDefOf), typeof(HediffDefOf), typeof(PawnCapacityDefOf),
                                       typeof(BodyPartTagDefOf), typeof(StatDefOf) })
                System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(t.TypeHandle);
        }
        finally { binding.SetValue(null, false); }
        SkillDefOf.Medicine = new SkillDef { defName = "Medicine", label = "medicine" };
        PawnCapacityDefOf.Consciousness = new PawnCapacityDef { defName = "Consciousness" };
        PawnCapacityDefOf.Manipulation = new PawnCapacityDef { defName = "Manipulation" };
        HediffDefOf.FoodPoisoning = Disease("FoodPoisoning");
        HediffDefOf.MissingBodyPart = new HediffDef { defName = "MissingBodyPart", hediffClass = typeof(Hediff_MissingPart) };
        StatDefOf.MedicalPotency = new StatDef { defName = "MedicalPotency", label = "medical potency" };
        // The vital-anatomy tags, bound to the fixture's own non-vanilla tag defs.
        BodyPartTagDefOf.ConsciousnessSource = ConsciousnessTag;
        BodyPartTagDefOf.BloodPumpingSource = PumpTag;
        BodyPartTagDefOf.BloodFiltrationKidney = KidneyTag;
        BodyPartTagDefOf.BloodFiltrationLiver = LiverTag;
        BodyPartTagDefOf.BloodFiltrationSource = FilterSourceTag;
        BodyPartTagDefOf.BreathingSource = LungTag;
        BodyPartTagDefOf.BreathingPathway = PathwayTag;
        BodyPartTagDefOf.BreathingSourceCage = CageTag;
        BodyPartTagDefOf.MetabolismSource = MetabolismTag;
    }

    // ------------------------------------------------------------------ fixtures

    /// immunizable: builds immunity like flu/malaria. severityOnlyImmunizable: carries the comp only
    /// to drive severity and never builds immunity, like vanilla asthma or Alzheimer's.
    static HediffDef Disease(string name, bool immunizable = false, bool chronic = false, bool tendable = false,
                             bool sick = false, bool severityOnlyImmunizable = false)
    {
        HediffDef d = new HediffDef { defName = name, label = name, hediffClass = typeof(HediffWithComps), chronic = chronic,
                                      makesSickThought = sick };
        d.comps = new List<HediffCompProperties>();
        if (immunizable) d.comps.Add(new HediffCompProperties_Immunizable { immunityPerDaySick = 0.3f });
        if (severityOnlyImmunizable) d.comps.Add(new HediffCompProperties_Immunizable());
        if (tendable) d.comps.Add(new HediffCompProperties_TendDuration());
        return d;
    }

    static List<BodyPartRecord> parts;
    static BodyPartRecord Part(string name, BodyPartRecord parent, params BodyPartTagDef[] tags)
    {
        BodyPartDef def = new BodyPartDef { defName = name, label = name };
        def.tags.AddRange(tags);
        BodyPartRecord r = new BodyPartRecord { def = def, parent = parent };
        if (parent != null) parent.parts.Add(r);
        parts.Add(r);
        return r;
    }

    sealed class Body
    {
        public BodyDef def;
        public BodyPartRecord torso, neck, head, brain, lShoulder, lArm, lHand, lFinger, rShoulder, rArm, rHand,
                              heart, lKidney, rKidney;
    }

    static readonly BodyPartTagDef ConsciousnessTag = new BodyPartTagDef { defName = "TestConsciousnessSource" };
    static readonly BodyPartTagDef PumpTag = new BodyPartTagDef { defName = "TestBloodPumpingSource" };
    static readonly BodyPartTagDef KidneyTag = new BodyPartTagDef { defName = "TestBloodFiltrationKidney" };
    static readonly BodyPartTagDef UnusedTag = new BodyPartTagDef { defName = "TestTagNobodyHas" };
    static readonly BodyPartTagDef LiverTag = new BodyPartTagDef { defName = "TestBloodFiltrationLiver" };
    static readonly BodyPartTagDef FilterSourceTag = new BodyPartTagDef { defName = "TestBloodFiltrationSource" };
    static readonly BodyPartTagDef LungTag = new BodyPartTagDef { defName = "TestBreathingSource" };
    static readonly BodyPartTagDef PathwayTag = new BodyPartTagDef { defName = "TestBreathingPathway" };
    static readonly BodyPartTagDef CageTag = new BodyPartTagDef { defName = "TestBreathingSourceCage" };
    static readonly BodyPartTagDef MetabolismTag = new BodyPartTagDef { defName = "TestMetabolismSource" };

    /// Generic biped with deliberately non-human names, so nothing can match on names.
    static Body MakeBody()
    {
        parts = new List<BodyPartRecord>();
        Body b = new Body();
        b.torso = Part("core", null);
        b.heart = Part("pump", b.torso, PumpTag);
        b.lKidney = Part("filterA", b.torso, KidneyTag);
        b.rKidney = Part("filterB", b.torso, KidneyTag);
        b.neck = Part("stalk", b.torso);
        b.head = Part("dome", b.neck);
        b.brain = Part("mind", b.head, ConsciousnessTag);
        b.lShoulder = Part("limbRootA", b.torso);
        b.lArm = Part("limbA", b.lShoulder);
        b.lHand = Part("graspA", b.lArm);
        b.lFinger = Part("digitA", b.lHand);
        b.rShoulder = Part("limbRootB", b.torso);
        b.rArm = Part("limbB", b.rShoulder);
        b.rHand = Part("graspB", b.rArm);
        b.def = new BodyDef { defName = "TestBody", corePart = b.torso };
        ((List<BodyPartRecord>)Get(b.def, "cachedAllParts")).AddRange(parts);
        foreach (BodyPartRecord p in parts) p.body = b.def;
        return b;
    }

    static Pawn MakePawn(int medicineLevel, Body body = null)
    {
        Pawn p = Uninit<Pawn>();
        p.Name = new NameSingle("Fixture" + medicineLevel);
        ThingDef race = Uninit<ThingDef>();
        race.defName = "TestRace";
        RaceProperties props = Uninit<RaceProperties>();
        props.body = (body ?? MakeBody()).def;
        race.race = props;
        p.def = race;

        Pawn_SkillTracker skills = Uninit<Pawn_SkillTracker>();
        skills.skills = new List<SkillRecord> { new SkillRecord { def = SkillDefOf.Medicine, levelInt = medicineLevel } };
        p.skills = skills;

        Pawn_HealthTracker health = Uninit<Pawn_HealthTracker>();
        Set(health, "healthState", PawnHealthState.Mobile);
        Set(health, "pawn", p);
        health.hediffSet = new HediffSet(p);
        health.capacities = Uninit<PawnCapacitiesHandler>();
        p.health = health;
        return p;
    }

    static T Attach<T>(Pawn pawn, HediffDef def, BodyPartRecord part = null, bool addToSet = true) where T : Hediff, new()
    {
        T h = new T();
        h.def = def;
        h.pawn = pawn;
        h.loadID = nextLoadId++;
        if (part != null) h.Part = part;
        HediffWithComps withComps = h as HediffWithComps;
        if (withComps != null)
        {
            withComps.comps = new List<HediffComp>();
            if (def.comps != null)
                foreach (HediffCompProperties cp in def.comps)
                {
                    HediffComp c = (HediffComp)Activator.CreateInstance(cp.compClass);
                    c.parent = withComps;
                    c.props = cp;
                    withComps.comps.Add(c);
                }
        }
        if (addToSet) pawn.health.hediffSet.hediffs.Add(h);
        return h;
    }
    static int nextLoadId = 1000;

    /// Gm21TreatmentStore.ExposeTreatment is internal: it is the Hediff.ExposeData postfix body.
    static void ExposeTreatment(Hediff h)
    {
        try { typeof(Gm21TreatmentStore).GetMethod("ExposeTreatment", Any).Invoke(null, new object[] { h }); }
        catch (TargetInvocationException e) { throw e.InnerException; }
    }

    static HediffComp_TendDuration Tend(Hediff h) { return ((HediffWithComps)h).TryGetComp<HediffComp_TendDuration>(); }

    static void SetFrame(bool grandmaster, float target)
    {
        FieldInfo current = typeof(Gm21GrandmasterTend).GetField("current", Any);
        current.SetValue(null, new Gm21TendFrame { grandmaster = grandmaster, target = target });
    }

    // ------------------------------------------------------------------ 1. medicine tiers

    static float Target(float cap, params float[] loaded)
    {
        return Gm21MedicineTiers.ResolveTarget(cap, Gm21MedicineTiers.DistinctAscending(loaded));
    }

    static bool Near(float a, float b) { return Math.Abs(a - b) < 1e-5f; }

    static void Tiers()
    {
        Console.WriteLine("=== 1. Grandmaster medicine tiers ===");
        Check("meaningful-tier multiplier is 1.30", Gm21Medicine.MeaningfulTierMultiplier == 1.30f);
        Check("top-tier fallback is +0.30", Gm21Medicine.TopTierFallbackBonus == 0.30f);

        float[] vanilla = { 0.70f, 1.00f, 1.30f };
        Check("vanilla herbal 70% -> 100%", Near(Target(0.70f, vanilla), 1.00f), Target(0.70f, vanilla).ToString());
        Check("vanilla industrial 100% -> 130%", Near(Target(1.00f, vanilla), 1.30f), Target(1.00f, vanilla).ToString());
        Check("vanilla glitterworld 130% -> 160% (fallback)", Near(Target(1.30f, vanilla), 1.60f), Target(1.30f, vanilla).ToString());

        float[] bridge = { 0.70f, 0.85f, 1.00f, 1.30f };
        Check("bridge 70 -> 100 (0.85 does not satisfy 0.91)", Near(Target(0.70f, bridge), 1.00f));
        Check("bridge 85 -> 130 (1.00 does not satisfy 1.105)", Near(Target(0.85f, bridge), 1.30f));
        Check("bridge 100 -> 130", Near(Target(1.00f, bridge), 1.30f));
        Check("bridge 130 -> 160 fallback", Near(Target(1.30f, bridge), 1.60f));

        float[] withQualifying = { 0.70f, 0.85f, 1.00f, 1.15f, 1.30f };
        Check("a genuinely qualifying 1.15 tier IS used for 0.85", Near(Target(0.85f, withQualifying), 1.15f));
        Check("  ...and does not disturb 0.70 -> 1.00", Near(Target(0.70f, withQualifying), 1.00f));

        float[] dupes = { 1.30f, 0.70f, 1.00f, 0.70f, 1.30f, 1.00f, 1.30f, 0.7000001f };
        Check("duplicates collapse to three distinct tiers",
              Gm21MedicineTiers.DistinctAscending(dupes).Length == 3,
              string.Join(",", Gm21MedicineTiers.DistinctAscending(dupes).Select(x => x.ToString()).ToArray()));
        Check("duplicates do not change any result",
              Near(Target(0.70f, dupes), 1.00f) && Near(Target(1.00f, dupes), 1.30f) && Near(Target(1.30f, dupes), 1.60f));
        Check("NaN, infinite, zero and negative caps are ignored",
              Gm21MedicineTiers.DistinctAscending(new[] { float.NaN, float.PositiveInfinity, 0f, -1f, 1f }).Length == 1);
        Check("XML-parsed 1.3 satisfies 1.0 x 1.30 exactly at the boundary",
              Near(Gm21MedicineTiers.ResolveTarget(1.0f, new[] { float.Parse("1.3", System.Globalization.CultureInfo.InvariantCulture) }), 1.3f));
        Check("empty hierarchy falls back to +0.30", Near(Gm21MedicineTiers.ResolveTarget(0.5f, new float[0]), 0.8f));
    }

    // ------------------------------------------------------------------ 2. treatment curve

    static void Curve()
    {
        Console.WriteLine("\n=== 2. Grandmaster Treatment curve (provisional tuning) ===");
        float[,] table = { { 0.70f, 1.00f }, { 1.00f, 1.25f }, { 1.30f, 1.50f }, { 1.60f, 1.75f }, { 1.90f, 2.00f } };
        for (int i = 0; i < table.GetLength(0); i++)
        {
            float got = Gm21Medicine.RecoveryMultiplierFor(table[i, 0]);
            Check("quality " + table[i, 0].ToString("0.00") + " -> x" + table[i, 1].ToString("0.00"), Near(got, table[i, 1]), got.ToString());
        }
        Check("never slower than vanilla (quality 0.2 -> x1.00)", Near(Gm21Medicine.RecoveryMultiplierFor(0.2f), 1f));
        Check("absurd modded quality is capped (x3.00)", Near(Gm21Medicine.RecoveryMultiplierFor(50f), 3f));
        Check("NaN quality is harmless (x1.00)", Near(Gm21Medicine.RecoveryMultiplierFor(float.NaN), 1f));
    }

    // ------------------------------------------------------------------ 3. real patches install

    static void InstallPatches()
    {
        Console.WriteLine("\n=== 3. Gm21MedicinePatches.Apply against the real methods ===");
        Gm21MedicinePatches_Apply();
        Check("deterministic tend patches applied", Gm21Medicine.TendEnabled);
        Check("effective-quality propagation transpiler applied, at exactly one call site",
              Gm21Medicine.TendPropagationEnabled && Gm21GrandmasterTend.PropagationSites == 1);
        Check("treatment (tend + Hediff.ExposeData persistence) applied", Gm21Medicine.TreatmentEnabled);
        Check("injury recovery patches applied", Gm21Medicine.RecoveryEnabled);
        Check("immunity patch applied", Gm21Medicine.ImmunityEnabled);
        Check("surgery patch applied", Gm21Medicine.SurgeryEnabled);
        HashSet<string> patched = new HashSet<string>(Harmony.GetAllPatchedMethods()
            .Where(m => Harmony.GetPatchInfo(m).Owners.Contains("ared.grandmaster21"))
            .Select(m => m.DeclaringType.Name + "." + m.Name));
        foreach (string target in new[] { "Hediff.ExposeData", "TendUtility.DoTend", "HediffComp_TendDuration.CompTended",
                                          "HediffComp_TendDuration.get_CompTipStringExtra",
                                          "Pawn_HealthTracker.HealthTickInterval", "Hediff_Injury.Heal",
                                          "ImmunityRecord.ImmunityChangePerTick", "SurgeryOutcomeEffectDef.GetOutcome" })
            Check("  patched: " + target, patched.Contains(target));
        Patches doTendInfo = Harmony.GetPatchInfo(AccessTools.Method(typeof(TendUtility), "DoTend"));
        Check("  patched: TendUtility.DoTend transpiler (effective quality)",
              doTendInfo != null && doTendInfo.Transpilers.Any(tp => tp.owner == "ared.grandmaster21"));
        Check("no global medicine-stat or WorkGiver patch was installed",
              !patched.Any(p => p.StartsWith("StatWorker") || p.StartsWith("WorkGiver") || p.StartsWith("Hediff.TendableNow")
                                || p.Contains("TendableNow")));
    }

    static void Gm21MedicinePatches_Apply()
    {
        Type t = Mod.GetType("Grandmaster21.Gm21MedicinePatches");
        t.GetMethod("Apply", Any).Invoke(null, new object[] { new Harmony("ared.grandmaster21") });
    }

    // ------------------------------------------------------------------ 4. deterministic tending

    static void DeterministicTend()
    {
        Console.WriteLine("\n=== 4. Deterministic Grandmaster tend (real Hediff.Tended -> CompTended) ===");
        Check("vanilla tend variance constant is still 0.25 (the determinism margin relies on it)",
              HediffComp_TendDuration.TendQualityRandomVariance == 0.25f);

        Pawn patient = MakePawn(0);
        HediffDef wound = Disease("TestWound", tendable: true);

        foreach (float target in new[] { 1.00f, 1.30f, 1.60f, 0.70f })
        {
            int exact = 0;
            for (int i = 0; i < 5000; i++)
            {
                HediffWithComps h = Attach<HediffWithComps>(patient, wound);
                SetFrame(true, target);
                // The vanilla maxQuality handed in by DoTend is the medicine's own cap, e.g. 0.70.
                h.Tended(0.4f, 0.70f, 1);
                if (Tend(h).tendQuality == target) exact++;
                patient.health.hediffSet.hediffs.Remove(h);
            }
            SetFrame(false, 0f);
            Check("GM target " + (target * 100).ToString("0") + "%: 5,000 tends, every one exactly the target",
                  exact == 5000, exact + "/5000");
        }

        float min = 9f, max = -9f;
        for (int i = 0; i < 5000; i++)
        {
            HediffWithComps h = Attach<HediffWithComps>(patient, wound);
            h.Tended(0.80f, 1.00f, 1);
            min = Math.Min(min, Tend(h).tendQuality);
            max = Math.Max(max, Tend(h).tendQuality);
            patient.health.hediffSet.hediffs.Remove(h);
        }
        Check("ordinary tend keeps vanilla's +-25% roll", min < 0.60f && max > 0.99f && min >= 0.55f && max <= 1.00f,
              "range " + min.ToString("0.000") + ".." + max.ToString("0.000"));

        // Frame decision, with real pawns and a cached medicine target.
        ThingDef herbal = Uninit<ThingDef>(); herbal.defName = "TestHerbal";
        ((Dictionary<ThingDef, float>)typeof(Gm21MedicineTiers).GetField("TargetByDef", Any).GetValue(null))[herbal] = 1.00f;
        Medicine med = Uninit<Medicine>(); med.def = herbal;
        Pawn gm = MakePawn(21), twenty = MakePawn(20), other = MakePawn(0);
        Check("Medicine 21 + medicine opens a Grandmaster frame at the cached target",
              Gm21GrandmasterTend.Frame(gm, other, med).grandmaster && Gm21GrandmasterTend.Frame(gm, other, med).target == 1.00f);
        Check("Medicine 20 opens NO frame (vanilla)", !Gm21GrandmasterTend.Frame(twenty, other, med).grandmaster);
        Check("no medicine opens NO frame (vanilla no-medicine rules)", !Gm21GrandmasterTend.Frame(gm, other, null).grandmaster);
        Check("self-tend keeps vanilla's x0.7, deterministically (100% -> 70%)",
              Near(Gm21GrandmasterTend.Frame(gm, gm, med).target, 1.00f * TendUtility.SelfTendQualityFactor));
        capable = false;
        Check("a Grandmaster who cannot manipulate/think opens NO frame", !Gm21GrandmasterTend.Frame(gm, other, med).grandmaster);
        capable = true;

        // Frame nesting through the real prefix/finalizer bodies.
        Type T = typeof(Gm21GrandmasterTend);
        object[] args = { gm, other, med, null };
        T.GetMethod("Prefix_DoTend", Any).Invoke(null, args);
        bool open = Gm21GrandmasterTend.Current.grandmaster;
        T.GetMethod("Finalizer_DoTend", Any).Invoke(null, new[] { args[3] });
        Check("DoTend prefix opens and the finalizer restores the enclosing frame",
              open && !Gm21GrandmasterTend.Current.grandmaster);
    }

    // ------------------------------------------------------------------ 5. treatment state

    static void Treatment()
    {
        Console.WriteLine("\n=== 5. Grandmaster Treatment: per-condition, refresh, replace ===");
        Pawn patient = MakePawn(0);
        HediffDef woundDef = Disease("TestWound2", tendable: true);
        HediffWithComps torso = Attach<HediffWithComps>(patient, woundDef);
        HediffWithComps arm = Attach<HediffWithComps>(patient, woundDef);
        HediffWithComps malaria = Attach<HediffWithComps>(patient, Disease("TestMalaria", immunizable: true, tendable: true));
        HediffWithComps carcinoma = Attach<HediffWithComps>(patient, Disease("TestCarcinoma", chronic: true, tendable: true));
        HediffWithComps scar = Attach<HediffWithComps>(patient, Disease("TestScar"));

        SetFrame(true, 1.00f);
        torso.Tended(0.5f, 0.7f, 1); arm.Tended(0.5f, 0.7f, 1); malaria.Tended(0.5f, 0.7f, 1);
        SetFrame(false, 0f);

        Gm21Treatment t;
        Check("treated gunshot (torso) has Grandmaster Treatment", Gm21TreatmentStore.TryGetActive(torso, out t) && t.quality == 1.00f);
        Check("treated gunshot (arm) has Grandmaster Treatment", Gm21TreatmentStore.TryGetActive(arm, out t));
        Check("treated malaria has Grandmaster Treatment", Gm21TreatmentStore.TryGetActive(malaria, out t));
        Check("untreated carcinoma on the same pawn has none", !Gm21TreatmentStore.TryGet(carcinoma, out t));
        Check("untreated scar on the same pawn has none", !Gm21TreatmentStore.TryGet(scar, out t));

        // Refresh, never stack: re-tend by the Grandmaster at a higher target.
        Tend(malaria).tendTicksLeft = 0;
        SetFrame(true, 1.30f); malaria.Tended(0.5f, 0.7f, 1);
        SetFrame(true, 1.30f); malaria.Tended(0.5f, 0.7f, 1);
        SetFrame(false, 0f);
        float m;
        Gm21TreatmentStore.TryGetRecoveryMultiplier(malaria, out m);
        Check("re-tending refreshes quality (130%)", Gm21TreatmentStore.TryGet(malaria, out t) && t.quality == 1.30f);
        Check("  ...and the multiplier is x1.50, not a product of regimens", Near(m, 1.50f), m.ToString());

        // A normal doctor re-tends: regimen replaced.
        Tend(torso).tendTicksLeft = 0;
        torso.Tended(0.9f, 1.0f, 1);
        Check("a non-Grandmaster re-tend clears that condition's treatment", !Gm21TreatmentStore.TryGet(torso, out t));
        Check("  ...and leaves the other treated wound alone", Gm21TreatmentStore.TryGetActive(arm, out t));

        // Lapse: the vanilla tend underneath expired.
        Tend(arm).tendTicksLeft = 0;
        Check("a lapsed tend makes the regimen inactive", !Gm21TreatmentStore.TryGetActive(arm, out t)
              && !Gm21TreatmentStore.TryGetRecoveryMultiplier(arm, out m));

        Check("effect: injury class -> Recovery", Gm21TreatmentStore.EffectFor(new Hediff_Injury { def = woundDef }) == Gm21TreatmentEffect.Recovery);
        Check("effect: immunizable -> Immunity", Gm21TreatmentStore.EffectFor(malaria) == Gm21TreatmentEffect.Immunity);
        Check("effect: anything else -> None (state only, fails safe)", Gm21TreatmentStore.EffectFor(carcinoma) == Gm21TreatmentEffect.None);
        HediffWithComps asthma = Attach<HediffWithComps>(patient, Disease("TestAsthmaLike", chronic: true, tendable: true, severityOnlyImmunizable: true));
        Check("effect: an immunizable comp that never builds immunity (vanilla asthma) -> None, not a false 'immunity' promise",
              Gm21TreatmentStore.EffectFor(asthma) == Gm21TreatmentEffect.None);
    }

    // ------------------------------------------------------------------ 6. recovery / immunity

    static void Recovery()
    {
        Console.WriteLine("\n=== 6. Injury recovery (real Hediff_Injury.Heal) and immunity ===");
        Pawn patient = MakePawn(0);
        HediffDef cut = new HediffDef { defName = "TestCut", hediffClass = typeof(Hediff_Injury), comps = new List<HediffCompProperties> { new HediffCompProperties_TendDuration() } };
        Hediff_Injury treated = Attach<Hediff_Injury>(patient, cut);
        Hediff_Injury vanilla = Attach<Hediff_Injury>(patient, cut);
        Set(treated, "severityInt", 10f); Set(vanilla, "severityInt", 10f);
        SetFrame(true, 1.00f); treated.Tended(0.5f, 0.7f, 1);
        SetFrame(false, 0f); vanilla.Tended(0.5f, 0.7f, 1);

        Type R = typeof(Gm21RecoveryEffects);
        R.GetMethod("Prefix_HealthTickInterval", Any).Invoke(null, null);
        try { treated.Heal(1f); vanilla.Heal(1f); }
        finally { R.GetMethod("Finalizer_HealthTickInterval", Any).Invoke(null, null); }
        Check("GM-treated injury heals x1.25 in the health tick (100% treatment)", Near(treated.Severity, 8.75f), treated.Severity.ToString());
        Check("the same injury tended by an ordinary doctor heals x1.00", Near(vanilla.Severity, 9f), vanilla.Severity.ToString());

        treated.Heal(1f);
        Check("a heal from OUTSIDE the health tick is not scaled", Near(treated.Severity, 7.75f), treated.Severity.ToString());

        // Faster in aggregate, per the brief's wording: over 20 vanilla heal events.
        Hediff_Injury a = Attach<Hediff_Injury>(patient, cut), b = Attach<Hediff_Injury>(patient, cut);
        Set(a, "severityInt", 30f); Set(b, "severityInt", 30f);
        SetFrame(true, 1.60f); a.Tended(0.5f, 0.7f, 1); SetFrame(false, 0f); b.Tended(0.5f, 0.7f, 1);
        R.GetMethod("Prefix_HealthTickInterval", Any).Invoke(null, null);
        try { for (int i = 0; i < 20; i++) { a.Heal(0.5f); b.Heal(0.5f); } }
        finally { R.GetMethod("Finalizer_HealthTickInterval", Any).Invoke(null, null); }
        Check("160% treatment: 20 heal events recover x1.75 the vanilla amount",
              Near(30f - a.Severity, 1.75f * (30f - b.Severity)), (30f - a.Severity) + " vs " + (30f - b.Severity));

        HediffWithComps malaria = Attach<HediffWithComps>(patient, Disease("TestMalaria2", immunizable: true, tendable: true));
        HediffWithComps plague = Attach<HediffWithComps>(patient, Disease("TestPlague", immunizable: true, tendable: true));
        SetFrame(true, 1.30f); malaria.Tended(0.5f, 0.7f, 1); SetFrame(false, 0f);
        MethodInfo post = R.GetMethod("Postfix_ImmunityChangePerTick", Any);
        Func<bool, Hediff, float, float> Imm = (sick, h, v) => { object[] args = { sick, h, v }; post.Invoke(null, args); return (float)args[2]; };
        Check("GM-treated disease gains immunity x1.50 (130% treatment)", Near(Imm(true, malaria, 0.001f), 0.0015f));
        Check("an untreated disease on the same pawn is unchanged", Near(Imm(true, plague, 0.001f), 0.001f));
        Check("not-sick immunity drift is unchanged", Near(Imm(false, malaria, 0.001f), 0.001f));
        Check("a non-positive change is never amplified", Near(Imm(true, malaria, -0.002f), -0.002f));
    }

    // ------------------------------------------------------------------ 7. surgery

    static void Surgery()
    {
        Console.WriteLine("\n=== 7. Surgery (real SurgeryOutcomeEffectDef.GetOutcome, patched) ===");
        Pawn gm = MakePawn(21), twenty = MakePawn(20), patient = MakePawn(0);
        RecipeDef recipe = new RecipeDef { defName = "TestSurgery" };
        SurgeryOutcomeSuccess success = new SurgeryOutcomeSuccess();
        SurgeryOutcome_Failure minor = new SurgeryOutcome_Failure { chance = 1f, failure = true };
        SurgeryOutcome_Death death = new SurgeryOutcome_Death { failure = true };
        // Worst case: failures listed FIRST, and a comp that clamps quality to zero.
        SurgeryOutcomeEffectDef def = new SurgeryOutcomeEffectDef
        {
            defName = "TestOutcomes",
            comps = new List<SurgeryOutcomeComp> { new SurgeryOutcomeComp_ClampToRange { range = new FloatRange(0f, 0f) } },
            outcomes = new List<SurgeryOutcome> { minor, death, success }
        };

        int ok = 0;
        for (int i = 0; i < 1000; i++)
            if (def.GetOutcome(recipe, gm, patient, null, null, null) == success) ok++;
        Check("Medicine 21: 1,000 surgeries at clamped-to-zero quality, failures listed first -> 1,000 successes", ok == 1000, ok + "/1000");

        SurgeryOutcome vanillaResult = def.GetOutcome(recipe, twenty, patient, null, null, null);
        Check("Medicine 20: the same surgery takes vanilla's failure branch", vanillaResult == minor);

        capable = false;
        Check("an incapable Grandmaster falls back to vanilla", def.GetOutcome(recipe, gm, patient, null, null, null) == minor);
        capable = true;

        SurgeryOutcomeEffectDef failuresOnly = new SurgeryOutcomeEffectDef
        { defName = "OnlyFailures", comps = new List<SurgeryOutcomeComp>(), outcomes = new List<SurgeryOutcome> { minor, death } };
        Check("only-failure outcome lists resolve to null (CheckSurgeryFail's success)",
              failuresOnly.GetOutcome(recipe, gm, patient, null, null, null) == null);

        Check("IsFailureOutcome: flagged", Gm21Surgery.IsFailureOutcome(new SurgeryOutcomeSuccess { failure = true }));
        Check("IsFailureOutcome: Failure class even if unflagged", Gm21Surgery.IsFailureOutcome(new SurgeryOutcome_Failure()));
        Check("IsFailureOutcome: Death class even if unflagged", Gm21Surgery.IsFailureOutcome(new SurgeryOutcome_Death()));
        Check("IsFailureOutcome: success is not", !Gm21Surgery.IsFailureOutcome(new SurgeryOutcomeSuccess()));
    }

    // ------------------------------------------------------------------ 8. cure filtering

    public class ModdedDiseaseClass : HediffWithComps { }

    static void Cure()
    {
        Console.WriteLine("\n=== 8. Cure candidates ===");
        Body body = MakeBody();
        Pawn p = MakePawn(0, body);
        HediffWithComps food = Attach<HediffWithComps>(p, HediffDefOf.FoodPoisoning);
        Hediff_Injury gunshot = Attach<Hediff_Injury>(p, new HediffDef { defName = "TestGunshot", hediffClass = typeof(Hediff_Injury) }, body.lArm);
        Hediff_AddedPart bionic = Attach<Hediff_AddedPart>(p, new HediffDef { defName = "TestBionicArm", hediffClass = typeof(Hediff_AddedPart), isBad = false }, body.rShoulder);
        Hediff_MissingPart missing = Attach<Hediff_MissingPart>(p, HediffDefOf.MissingBodyPart, body.lShoulder);
        HediffWithComps psychic = Attach<HediffWithComps>(p, Disease("TestPsychicHangover"));
        HediffWithComps buff = Attach<HediffWithComps>(p, new HediffDef { defName = "TestBuff", isBad = false, comps = new List<HediffCompProperties>() });

        List<Hediff> menu = Gm21CureCandidates.GetCureCandidates(p);
        Check("mixed patient: the Cure menu is exactly [food poisoning]", menu.Count == 1 && menu[0] == food,
              string.Join(",", menu.Select(h => h.def.defName).ToArray()));
        Check("bionic part: never a Cure candidate", Gm21CureCandidates.Classify(bionic) != Gm21CureVerdict.Candidate);
        Check("missing part: routed to Reconstruct, not Cure", Gm21CureCandidates.Classify(missing) == Gm21CureVerdict.MissingPart);
        Check("normal wound: left to ordinary tending", Gm21CureCandidates.Classify(gunshot) == Gm21CureVerdict.Injury);
        Check("isBad with no pathology signal is not offered", Gm21CureCandidates.Classify(psychic) == Gm21CureVerdict.NoPathologySignal);
        Check("beneficial state is not offered", Gm21CureCandidates.Classify(buff) == Gm21CureVerdict.NotBad);

        Pawn q = MakePawn(0, body);
        Check("immunizable disease is a candidate", Gm21CureCandidates.IsCureCandidate(Attach<HediffWithComps>(q, Disease("TestFlu", immunizable: true))));
        Check("chronic illness is a candidate", Gm21CureCandidates.IsCureCandidate(Attach<HediffWithComps>(q, Disease("TestAsthma", chronic: true))));
        Check("tendable sickness (vanilla lung rot / gut worms shape) is a candidate",
              Gm21CureCandidates.IsCureCandidate(Attach<HediffWithComps>(q, Disease("TestLungRot", tendable: true, sick: true))));
        Check("sickness that is not tendable (cryptosleep / biosculpting shape) is not",
              Gm21CureCandidates.Classify(Attach<HediffWithComps>(q, Disease("TestCryptoSick", sick: true))) == Gm21CureVerdict.NoPathologySignal);
        Check("tendable but not a sickness is not",
              Gm21CureCandidates.Classify(Attach<HediffWithComps>(q, Disease("TestTendableOnly", tendable: true))) == Gm21CureVerdict.NoPathologySignal);
        HediffDef noItem = Disease("TestUncurable", immunizable: true); noItem.everCurableByItem = false;
        Check("everCurableByItem=false is respected", Gm21CureCandidates.Classify(Attach<HediffWithComps>(q, noItem)) == Gm21CureVerdict.NotCurableByItem);
        HediffDef addiction = Disease("TestAddiction", chronic: true); addiction.hediffClass = typeof(Hediff_Addiction);
        Check("addiction is a chemical dependency, not a disease", Gm21CureCandidates.Classify(Attach<HediffWithComps>(q, addiction)) == Gm21CureVerdict.ChemicalDependency);
        HediffDef pregnant = Disease("TestPregnant", chronic: true); pregnant.pregnant = true;
        Check("pregnancy is never offered", Gm21CureCandidates.Classify(Attach<HediffWithComps>(q, pregnant)) == Gm21CureVerdict.Reproduction);
        HediffDef implant = Disease("TestImplantLike", chronic: true); implant.countsAsAddedPartOrImplant = true;
        Check("countsAsAddedPartOrImplant is never offered", Gm21CureCandidates.Classify(Attach<HediffWithComps>(q, implant)) == Gm21CureVerdict.AddedPartOrImplant);
        Check("a modded Hediff subclass is omitted, not guessed at",
              Gm21CureCandidates.Classify(Attach<ModdedDiseaseClass>(q, Disease("TestModded", immunizable: true))) == Gm21CureVerdict.CustomClass);
        HediffDef anomaly = Disease("TestVoidState", immunizable: true);
        ModContentPack pack = Uninit<ModContentPack>(); Set(pack, "packageIdInt", "Ludeon.RimWorld.Anomaly");
        anomaly.modContentPack = pack;
        Check("an Anomaly-pack condition is supernatural, not medicine", Gm21CureCandidates.Classify(Attach<HediffWithComps>(q, anomaly)) == Gm21CureVerdict.Supernatural);
        HediffDef hidden = Disease("TestHidden", immunizable: true);
        hidden.stages = new List<HediffStage> { new HediffStage { becomeVisible = false } };
        Check("an undiagnosed (invisible) condition is not offered", Gm21CureCandidates.Classify(Attach<HediffWithComps>(q, hidden)) == Gm21CureVerdict.NotVisible);

        removedLog.Clear();
        Gm21CureCandidates.ApplyCure(p, food);
        Check("completing Cure removes ONLY the selected condition",
              removedLog.Count == 1 && removedLog[0] == food && p.health.hediffSet.hediffs.Contains(gunshot)
              && p.health.hediffSet.hediffs.Contains(bionic) && p.health.hediffSet.hediffs.Contains(missing)
              && p.health.hediffSet.hediffs.Contains(psychic));

        HediffDef allAtOnce = Disease("TestMechanites", chronic: true); allAtOnce.cureAllAtOnceIfCuredByItem = true;
        Pawn r = MakePawn(0, body);
        HediffWithComps m1 = Attach<HediffWithComps>(r, allAtOnce), m2 = Attach<HediffWithComps>(r, allAtOnce);
        HediffWithComps flu = Attach<HediffWithComps>(r, Disease("TestFlu2", immunizable: true));
        removedLog.Clear();
        Gm21CureCandidates.ApplyCure(r, m1);
        Check("cureAllAtOnceIfCuredByItem removes every instance of THAT condition only",
              removedLog.Count == 2 && !r.health.hediffSet.hediffs.Contains(m2) && r.health.hediffSet.hediffs.Contains(flu));
    }

    // ------------------------------------------------------------------ 9. reconstruct filtering

    static void Reconstruct()
    {
        Console.WriteLine("\n=== 9. Reconstruct candidates (real HediffSet) ===");
        HediffDef missingDef = HediffDefOf.MissingBodyPart;

        Body b = MakeBody();
        Pawn p = MakePawn(0, b);
        Hediff_MissingPart shoulder = Attach<Hediff_MissingPart>(p, missingDef, b.lShoulder);
        Attach<Hediff_MissingPart>(p, missingDef, b.lArm);
        Attach<Hediff_MissingPart>(p, missingDef, b.lHand);
        Attach<Hediff_MissingPart>(p, missingDef, b.lFinger);
        List<Hediff_MissingPart> c = Gm21ReconstructCandidates.GetReconstructionCandidates(p);
        Check("missing natural limb, no replacement: offered once, at its root", c.Count == 1 && c[0] == shoulder,
              string.Join(",", c.Select(x => x.Part.def.defName).ToArray()));

        b = MakeBody(); p = MakePawn(0, b);
        Attach<Hediff_AddedPart>(p, new HediffDef { defName = "TestBionic", hediffClass = typeof(Hediff_AddedPart), isBad = false }, b.lShoulder);
        Check("same location replaced by a bionic: nothing offered", Gm21ReconstructCandidates.GetReconstructionCandidates(p).Count == 0);

        b = MakeBody(); p = MakePawn(0, b);
        Attach<Hediff_AddedPart>(p, new HediffDef { defName = "TestProsthetic", hediffClass = typeof(Hediff_AddedPart), isBad = false }, b.lShoulder);
        Hediff_MissingPart underBionic = Attach<Hediff_MissingPart>(p, missingDef, b.lFinger);
        Check("a missing child under an artificial parent is never offered (no fingers inside a bionic arm)",
              Gm21ReconstructCandidates.Classify(p.health.hediffSet, underBionic) == Gm21ReconstructVerdict.ReplacedByArtificialPart
              && Gm21ReconstructCandidates.GetReconstructionCandidates(p).Count == 0);

        b = MakeBody(); p = MakePawn(0, b);
        Hediff_MissingPart gap = Attach<Hediff_MissingPart>(p, missingDef, b.rShoulder);
        Attach<Hediff_MissingPart>(p, missingDef, b.rArm);
        Attach<HediffWithComps>(p, new HediffDef { defName = "TestStrayImplant", isBad = false, comps = new List<HediffCompProperties>() }, b.rHand);
        Check("a subtree holding anything but missing markers is not offered (RestorePart would delete it)",
              Gm21ReconstructCandidates.Classify(p.health.hediffSet, gap) == Gm21ReconstructVerdict.OtherStateInSubtree);

        b = MakeBody(); p = MakePawn(0, b);
        Attach<Hediff_MissingPart>(p, missingDef, b.lShoulder);
        Hediff_MissingPart child = Attach<Hediff_MissingPart>(p, missingDef, b.lArm);
        Check("a missing child under a missing parent is not topmost", Gm21ReconstructCandidates.Classify(p.health.hediffSet, child) == Gm21ReconstructVerdict.NotTopmost);
        Hediff_MissingPart core = Attach<Hediff_MissingPart>(p, missingDef, b.torso, false);
        p.health.hediffSet.hediffs.Add(core);
        Check("the core part is never offered", Gm21ReconstructCandidates.Classify(p.health.hediffSet, core) == Gm21ReconstructVerdict.CorePart);

        b = MakeBody(); p = MakePawn(0, b);
        Hediff_MissingPart stale = Attach<Hediff_MissingPart>(p, missingDef, b.lHand, false);
        Check("a part that is no longer missing fails cleanly (NotPresent)", Gm21ReconstructCandidates.Classify(p.health.hediffSet, stale) == Gm21ReconstructVerdict.NotPresent);
    }

    public class ModdedMissingPart : Hediff_MissingPart { }

    // ------------------------------------------------------------------ 10. resuscitation policy

    static void Resuscitation()
    {
        Console.WriteLine("\n=== 10. Resuscitation viability policy ===");
        Gm21ResuscitationFacts ok = new Gm21ResuscitationFacts { isCorpse = true, available = true, isFlesh = true, rotStage = RotStage.Fresh, rotProgress = 100f };
        Func<Gm21ResuscitationFacts, Gm21ResuscitationVerdict> D = Gm21Resuscitation.Decide;
        Check("fresh, intact, recent, non-hostile flesh corpse -> viable", D(ok) == Gm21ResuscitationVerdict.Viable);
        Gm21ResuscitationFacts f;
        f = ok; f.isCorpse = false; Check("not a corpse -> rejected", D(f) == Gm21ResuscitationVerdict.NotACorpse);
        f = ok; f.available = false; Check("unavailable/invalid corpse -> rejected", D(f) == Gm21ResuscitationVerdict.Unavailable);
        f = ok; f.isFlesh = false; Check("non-flesh -> rejected", D(f) == Gm21ResuscitationVerdict.NotFlesh);
        f = ok; f.supernatural = true; Check("entity/mutant/unnatural -> rejected", D(f) == Gm21ResuscitationVerdict.Supernatural);
        f = ok; f.hostile = true; Check("hostile -> VIABLE (not a refusal; the order confirms, the pawn stays hostile)", D(f) == Gm21ResuscitationVerdict.Viable);
        Check("the verdict enum no longer has a Hostile refusal",
              !Enum.GetNames(typeof(Gm21ResuscitationVerdict)).Any(n => n.IndexOf("Hostile", StringComparison.OrdinalIgnoreCase) >= 0));
        f = ok; f.brainDestroyed = true; Check("destroyed consciousness anatomy -> 'Brain destroyed' (hard boundary)", D(f) == Gm21ResuscitationVerdict.BrainDestroyed);
        f = ok; f.vitalRebuilds = 2; Check("destroyed but rebuildable vital anatomy -> viable", D(f) == Gm21ResuscitationVerdict.Viable);
        f = ok; f.vitalUnrebuildable = true; Check("vital anatomy that cannot be rebuilt minimally -> rejected", D(f) == Gm21ResuscitationVerdict.VitalAnatomyUnrebuildable);
        f = ok; f.brainDestroyed = true; f.vitalUnrebuildable = true; Check("brain destruction is reported before vital anatomy", D(f) == Gm21ResuscitationVerdict.BrainDestroyed);
        f = ok; f.rotStage = RotStage.Rotting; Check("rotting -> 'deteriorated beyond recovery'", D(f) == Gm21ResuscitationVerdict.Deteriorated);
        f = ok; f.rotStage = RotStage.Dessicated; Check("dessicated -> 'deteriorated beyond recovery'", D(f) == Gm21ResuscitationVerdict.Deteriorated);
        float max = Gm21Medicine.MaxResuscitationRotProgress;
        Check("GM decay threshold is RotProgress 10,000 (provisional)", max == 10000f);
        f = ok; f.rotProgress = 0f; Check("no decay yet -> viable", D(f) == Gm21ResuscitationVerdict.Viable);
        f = ok; f.rotProgress = max; Check("decay exactly at the threshold -> viable", D(f) == Gm21ResuscitationVerdict.Viable);
        f = ok; f.rotProgress = max + 1f; Check("decay past the threshold -> 'deteriorated beyond recoverable limits'", D(f) == Gm21ResuscitationVerdict.TooDecayed);
        f = ok; f.rotProgress = 60000f; Check("vanilla still calls it Fresh (to 150,000) but the stricter GM threshold rejects it",
                                              f.rotStage == RotStage.Fresh && D(f) == Gm21ResuscitationVerdict.TooDecayed);
        f = ok; f.rotProgress = float.PositiveInfinity; Check("decay that cannot be judged (no rot comp) -> rejected", D(f) == Gm21ResuscitationVerdict.TooDecayed);
        f = ok; f.rotProgress = float.NaN; Check("not-a-number decay -> rejected", D(f) == Gm21ResuscitationVerdict.TooDecayed);
        f = ok; f.rotProgress = max + 5000f; f.decayCommitted = true;
        Check("COMMITTED (work begun while recoverable): decay past the threshold no longer fails it", D(f) == Gm21ResuscitationVerdict.Viable);
        f = ok; f.rotStage = RotStage.Rotting; f.decayCommitted = true;
        Check("COMMITTED: even a stage change during the work does not fail it", D(f) == Gm21ResuscitationVerdict.Viable);
        f = ok; f.decayCommitted = true; f.brainDestroyed = true;
        Check("COMMITTED: structural checks still apply", D(f) == Gm21ResuscitationVerdict.BrainDestroyed);
        f = ok; f.brainDestroyed = true; f.rotStage = RotStage.Dessicated; f.rotProgress = 999999f;
        Check("structural rejection is reported before decay", D(f) == Gm21ResuscitationVerdict.BrainDestroyed);
        f = ok; f.rotStage = RotStage.Rotting; f.rotProgress = 200000f;
        Check("rotting is reported as rotting, not merely over the threshold", D(f) == Gm21ResuscitationVerdict.Deteriorated);

        // Time is not a criterion any more.
        Check("the viability facts carry no time-since-death field",
              !typeof(Gm21ResuscitationFacts).GetFields().Any(fi => Regex.IsMatch(fi.Name, "tick|death|since|window|timeOf|^age", RegexOptions.IgnoreCase)));
        Check("the old four-hour window constant is gone", typeof(Gm21Medicine).GetField("ResuscitationWindowTicks") == null);
        using (AssemblyDefinition mod = AssemblyDefinition.ReadAssembly(Mod.Location))
        {
            MethodDefinition gather = mod.MainModule.GetType("Grandmaster21.Gm21Resuscitation").Methods.First(m => m.Name == "Gather");
            string[] reads = gather.Body.Instructions.Select(i => i.Operand as MemberReference).Where(r => r != null).Select(r => r.Name).ToArray();
            Check("viability reads the corpse's RotProgress, never its time of death or age",
                  mod.MainModule.GetType("Grandmaster21.Gm21Resuscitation").Methods.First(m => m.Name == "ReadDecay")
                     .Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "get_RotProgress")
                  && !reads.Contains("timeOfDeath") && !reads.Contains("get_Age"));
        }

        // The real CompRottable: what ReadDecay sees.
        try
        {
            Corpse corpse = Uninit<Corpse>();
            CompRottable rot = new CompRottable();
            rot.parent = corpse;
            rot.props = new CompProperties_Rottable { daysToRotStart = 2.5f, daysToDessicated = 5f };
            AccessTools.Field(typeof(ThingWithComps), "comps").SetValue(corpse, new List<ThingComp> { rot });
            float progress; RotStage stage;
            Set(rot, "rotProgressInt", 4000f);
            Gm21Resuscitation.ReadDecay(corpse, out progress, out stage);
            Check("real CompRottable: 4,000 decay reads as 4,000, Fresh -> recoverable",
                  progress == 4000f && stage == RotStage.Fresh && Gm21Resuscitation.WithinDecayLimit(progress));
            Set(rot, "rotProgressInt", 200000f);
            Gm21Resuscitation.ReadDecay(corpse, out progress, out stage);
            Check("real CompRottable: 200,000 decay reads as Rotting", stage == RotStage.Rotting);
            Corpse bare = Uninit<Corpse>();
            AccessTools.Field(typeof(ThingWithComps), "comps").SetValue(bare, null); // vanilla: no comps = null list
            Gm21Resuscitation.ReadDecay(bare, out progress, out stage);
            Check("a corpse with no rot comp reads as unjudgeable decay (not recoverable)", float.IsPositiveInfinity(progress));
        }
        catch (Exception e) { Blocked("real CompRottable decay read", e); }

        // Vanilla's rot rate: what the threshold means in the world.
        Func<float, float> rate = GenTemperature.RotRateAtTemperature;
        Check("vanilla rot rate: 1/tick from 10 C up (heat never speeds it further), 0.5 at 5 C, 0 at or below 0 C",
              rate(10f) == 1f && rate(21f) == 1f && rate(45f) == 1f && rate(5f) == 0.5f && rate(0f) == 0f && rate(-8f) == 0f);
        Check("so: unpreserved, the threshold is four in-game hours of decay (10,000 ticks)",
              Math.Abs(max / rate(21f) - 4 * GenDate.TicksPerHour) < 0.5f);
        Check("refrigerated at 5 C it is eight hours; frozen, decay never reaches it",
              Math.Abs(max / rate(5f) - 8 * GenDate.TicksPerHour) < 0.5f && rate(-5f) == 0f);

        Body b = MakeBody();
        Pawn p = MakePawn(0, b);
        HediffSet set = p.health.hediffSet;
        Check("intact brain -> not destroyed", !Gm21Resuscitation.TagEntirelyMissing(set, b.def, ConsciousnessTag));
        Attach<Hediff_MissingPart>(p, HediffDefOf.MissingBodyPart, b.lKidney);
        Check("one of two filtration organs lost -> still viable", !Gm21Resuscitation.TagEntirelyMissing(set, b.def, KidneyTag));
        Attach<Hediff_MissingPart>(p, HediffDefOf.MissingBodyPart, b.rKidney);
        Check("both lost -> vital anatomy destroyed", Gm21Resuscitation.TagEntirelyMissing(set, b.def, KidneyTag));
        Attach<Hediff_MissingPart>(p, HediffDefOf.MissingBodyPart, b.head);
        Attach<Hediff_MissingPart>(p, HediffDefOf.MissingBodyPart, b.brain);
        Check("destroyed head (brain marker beneath it) -> brain destroyed", Gm21Resuscitation.TagEntirelyMissing(set, b.def, ConsciousnessTag));
        Check("a race without a tag is never judged by it", !Gm21Resuscitation.TagEntirelyMissing(set, b.def, UnusedTag));
    }

    // ------------------------------------------------------------------ 14. tend quality propagation

    /// Records exactly what Hediff.Tended received -- stands in for any condition-specific override.
    public class RecordingCondition : HediffWithComps
    {
        public float gotQuality = -1f, gotMax = -1f;
        public override void Tended(float quality, float maxQuality, int batchPosition = 0)
        {
            gotQuality = quality; gotMax = maxQuality;
            base.Tended(quality, maxQuality, batchPosition);
        }
    }

    static void Propagation()
    {
        Console.WriteLine("\n=== 14. Effective tend quality reaches Hediff.Tended (DoTend transpiler) ===");
        MethodInfo doTend = AccessTools.Method(typeof(TendUtility), "DoTend");
        MethodInfo tended = AccessTools.Method(typeof(Hediff), "Tended", new[] { typeof(float), typeof(float), typeof(int) });
        MethodInfo effective = AccessTools.Method(typeof(Gm21GrandmasterTend), "TendedEffective");
        try
        {
            List<CodeInstruction> live = PatchProcessor.GetCurrentInstructions(doTend);
            int toEffective = live.Count(i => (i.opcode == System.Reflection.Emit.OpCodes.Call) && Equals(i.operand, effective));
            int toTended = live.Count(i => (i.opcode == System.Reflection.Emit.OpCodes.Call || i.opcode == System.Reflection.Emit.OpCodes.Callvirt)
                                            && Equals(i.operand, tended));
            Check("patched DoTend calls TendedEffective exactly once and Hediff.Tended directly never",
                  toEffective == 1 && toTended == 0, "effective=" + toEffective + " tended=" + toTended);
            List<CodeInstruction> original = PatchProcessor.GetOriginalInstructions(doTend);
            Check("unpatched DoTend has exactly one Hediff.Tended call (the premise)",
                  original.Count(i => (i.opcode == System.Reflection.Emit.OpCodes.Callvirt || i.opcode == System.Reflection.Emit.OpCodes.Call)
                                      && Equals(i.operand, tended)) == 1);
        }
        catch (Exception e) { Blocked("reading DoTend's live IL", e); }

        // Robustness: two call sites -> the transpiler changes nothing and reports 0 sites.
        MethodInfo transpiler = typeof(Gm21GrandmasterTend).GetMethod("Transpiler_DoTend", Any);
        List<CodeInstruction> twice = new List<CodeInstruction>
        {
            new CodeInstruction(System.Reflection.Emit.OpCodes.Callvirt, tended),
            new CodeInstruction(System.Reflection.Emit.OpCodes.Callvirt, tended),
            new CodeInstruction(System.Reflection.Emit.OpCodes.Ret)
        };
        List<CodeInstruction> result = ((IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { twice })).ToList();
        Check("an unexpected IL shape (two Tended calls) is left untouched and reported (0 sites)",
              Gm21GrandmasterTend.PropagationSites == 0 && result.Count(i => Equals(i.operand, tended)) == 2);
        List<CodeInstruction> once = new List<CodeInstruction>
        {
            new CodeInstruction(System.Reflection.Emit.OpCodes.Callvirt, tended),
            new CodeInstruction(System.Reflection.Emit.OpCodes.Ret)
        };
        ((IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { once })).ToList();
        Check("...and the single-site shape is rewritten again (1 site)", Gm21GrandmasterTend.PropagationSites == 1);

        Pawn patient = MakePawn(0);
        HediffDef recordDef = Disease("TestRecorder", tendable: true);
        recordDef.hediffClass = typeof(RecordingCondition);
        foreach (float target in new[] { 1.00f, 1.30f, 1.60f })
        {
            RecordingCondition c = Attach<RecordingCondition>(patient, recordDef);
            SetFrame(true, target);
            Gm21GrandmasterTend.TendedEffective(c, 0.42f, 0.70f, 1);
            SetFrame(false, 0f);
            Check("GM frame " + target.ToString("0.00") + ": the condition's own Tended receives quality = max = target",
                  c.gotQuality == target && c.gotMax == target && Tend(c).tendQuality == target,
                  "got " + c.gotQuality + "/" + c.gotMax);
        }
        RecordingCondition plain = Attach<RecordingCondition>(patient, recordDef);
        Gm21GrandmasterTend.TendedEffective(plain, 0.42f, 0.70f, 1);
        Check("no frame (ordinary doctor, or no medicine): the original values pass through untouched",
              plain.gotQuality == 0.42f && plain.gotMax == 0.70f);

        // Regression: vanilla Hediff_HeartAttack.Tended rolls 0.65 x quality against Rand.Value.
        HediffDef heartDef = Disease("TestHeartAttack", tendable: true);
        heartDef.hediffClass = typeof(Hediff_HeartAttack);
        Func<bool, float, float, int, float> successRate = (grandmaster, target, vanillaQuality, n) =>
        {
            int ok = 0;
            for (int i = 0; i < n; i++)
            {
                Hediff_HeartAttack h = Attach<Hediff_HeartAttack>(patient, heartDef);
                Set(h, "severityInt", 0.6f);
                SetFrame(grandmaster, target);
                Gm21GrandmasterTend.TendedEffective(h, vanillaQuality, 1.00f, 1);
                SetFrame(false, 0f);
                if (h.Severity < 0.55f) ok++;
                patient.health.hediffSet.hediffs.Remove(h);
            }
            return ok / (float)n;
        };
        const int N = 6000;
        float vanilla = successRate(false, 0f, 1.00f, N);
        float gm100 = successRate(true, 1.00f, 1.00f, N);
        float gm130 = successRate(true, 1.30f, 1.00f, N);
        float gm160 = successRate(true, 1.60f, 1.00f, N);
        Check("heart attack, ordinary doctor at 100%: vanilla ~65% treatment success", Math.Abs(vanilla - 0.65f) < 0.03f, vanilla.ToString("0.000"));
        Check("heart attack, Grandmaster at 100% effective: ~65% (same as the quality says)", Math.Abs(gm100 - 0.65f) < 0.03f, gm100.ToString("0.000"));
        Check("heart attack, Grandmaster at 130% effective (industrial): ~84.5%, not 65%", Math.Abs(gm130 - 0.845f) < 0.03f, gm130.ToString("0.000"));
        Check("heart attack, Grandmaster at 160% effective (glitterworld): 100%", gm160 == 1f, gm160.ToString("0.000"));
    }

    // ------------------------------------------------------------------ 15. intervention medicine

    /// ThingDef's constructor reaches Unity's shader database, so the def is built field by field.
    static ThingDef MedDef(string name, float potency)
    {
        ThingDef d = Uninit<ThingDef>();
        d.defName = name; d.label = name; d.thingClass = typeof(ThingWithComps); d.category = ThingCategory.Item;
        d.stackLimit = 25; d.useHitPoints = false; d.destroyable = true;
        d.comps = new List<CompProperties>();
        d.statBases = new List<StatModifier> { new StatModifier { stat = StatDefOf.MedicalPotency, value = potency } };
        return d;
    }

    static Thing Stack(ThingDef def, int count)
    {
        Thing t = ThingMaker.MakeThing(def);
        t.stackCount = count;
        return t;
    }

    static int Units(Pawn doctor, ThingDef def)
    {
        return doctor.inventory.innerContainer.Where(t => t.def == def).Sum(t => t.stackCount);
    }

    static float Potency(Pawn doctor)
    {
        return doctor.inventory.innerContainer.Sum(t => Gm21MedicineSupplies.PotencyOf(t.def) * t.stackCount);
    }

    static Gm21SupplyStack S(float potency, int available, bool inventory = false, float dist = 0f)
    {
        return new Gm21SupplyStack { potency = potency, available = available, inInventory = inventory, distanceSquared = dist };
    }

    static int[] PlanOf(float budget, params Gm21SupplyStack[] stacks)
    {
        int[] take = new int[stacks.Length];
        bool ok = Gm21MedicineSupplies.Plan(stacks.Select(x => x.potency).ToList(), stacks.Select(x => x.available).ToList(), budget, take);
        return ok ? take : null;
    }

    static string Show(int[] take) { return take == null ? "refused" : string.Join(",", take.Select(x => x.ToString()).ToArray()); }

    static void InterventionMedicine()
    {
        Console.WriteLine("\n=== 15. Intervention medicine: potency budgets, planning, consumption ===");
        Check("budgets: Cure 1.0 < Reconstruct 2.0 < Resuscitate 3.0 (provisional)",
              Gm21Medicine.CurePotencyBudget == 1f && Gm21Medicine.ReconstructPotencyBudget == 2f
              && Gm21Medicine.ResuscitatePotencyBudget == 3f
              && Gm21MedicineSupplies.BudgetFor(Gm21MedicineMode.Cure) == 1f
              && Gm21MedicineSupplies.BudgetFor(Gm21MedicineMode.Reconstruct) == 2f
              && Gm21MedicineSupplies.BudgetFor(Gm21MedicineMode.Resuscitate) == 3f);

        int[] t;
        t = PlanOf(1f, S(1f, 5)); Check("Cure, industrial x5 available: takes exactly 1", Show(t) == "1", Show(t));
        t = PlanOf(1f, S(0.6f, 5)); Check("Cure, herbal only: takes 2 (1.2 >= 1.0)", Show(t) == "2", Show(t));
        t = PlanOf(1f, S(1.6f, 3)); Check("Cure, glitterworld only: takes 1 (potency sets QUANTITY only)", Show(t) == "1", Show(t));
        t = PlanOf(3f, S(1.6f, 1), S(1f, 1), S(0.6f, 4));
        Check("Resuscitate from a mixture: glitterworld 1 + industrial 1 + herbal 1 = 3.2", Show(t) == "1,1,1", Show(t));
        t = PlanOf(3f, S(0.6f, 10)); Check("Resuscitate from herbal: exactly 5 (float-safe 5 x 0.6 = 3.0)", Show(t) == "5", Show(t));
        t = PlanOf(1f, S(0.35f, 10)); Check("modded medicine at potency 0.35 participates: 3 units", Show(t) == "3", Show(t));
        t = PlanOf(2f, S(0.6f, 3)); Check("insufficient (herbal x3 = 1.8 for Reconstruct 2.0): refused, nothing taken", t == null, Show(t));
        t = PlanOf(1f, S(0f, 9), S(1f, 0), S(0.6f, 2)); Check("zero-potency and empty stacks are skipped", Show(t) == "0,0,2", Show(t));

        List<Gm21SupplyStack> order = new List<Gm21SupplyStack> { S(0.6f, 1, false, 4f), S(1f, 1, false, 100f), S(1f, 1, true, 900f), S(1f, 1, false, 1f), S(1.6f, 1, false, 50f) };
        Gm21MedicineSupplies.SortByPreference(order);
        Check("preference: best potency first, inventory before map at equal potency, then nearest",
              order[0].potency == 1.6f && order[1].inInventory && order[2].distanceSquared == 1f && order[3].distanceSquared == 100f
              && order[4].potency == 0.6f);

        List<KeyValuePair<Gm21SupplyStack, int>> plan = new List<KeyValuePair<Gm21SupplyStack, int>>();
        float avail;
        Check("TryPlan reports the available potency when it refuses",
              !Gm21MedicineSupplies.TryPlan(new List<Gm21SupplyStack> { S(0.6f, 3) }, 2f, plan, out avail) && Near(avail, 1.8f) && plan.Count == 0,
              avail.ToString());

        // Inventory first: potency sets only the quantity, so carried medicine that meets the budget
        // is never left behind for "better" medicine on the map.
        Func<float, int, float, int, Gm21SupplyStack> M = (pot, n, dist, id) =>
            new Gm21SupplyStack { potency = pot, available = n, distanceSquared = dist, id = id };
        Func<float, int, int, Gm21SupplyStack> I = (pot, n, id) =>
            new Gm21SupplyStack { potency = pot, available = n, inInventory = true, id = id };
        Func<List<KeyValuePair<Gm21SupplyStack, int>>, string> P = pl =>
            string.Join(" ", pl.Select(kv => (kv.Key.inInventory ? "inv" : "map") + kv.Key.potency + "x" + kv.Value).ToArray());
        List<KeyValuePair<Gm21SupplyStack, int>> ip = new List<KeyValuePair<Gm21SupplyStack, int>>();
        float ia;
        bool okPlan = Gm21MedicineSupplies.PlanInventoryFirst(new List<Gm21SupplyStack> { I(1f, 3, 1) },
            new List<Gm21SupplyStack> { M(1.6f, 5, 1f, 2), M(1.6f, 5, 900f, 3) }, 3f, ip, out ia);
        Check("Resuscitate, 3 industrial carried, glitterworld on the map: all from the inventory, no map trip",
              okPlan && P(ip) == "inv1x3", P(ip));
        okPlan = Gm21MedicineSupplies.PlanInventoryFirst(new List<Gm21SupplyStack> { I(0.6f, 2, 1), I(1f, 2, 2) },
            new List<Gm21SupplyStack> { M(1.6f, 9, 1f, 3) }, 3f, ip, out ia);
        Check("mixed carried medicine (industrial 2 + herbal 2 = 3.2) meets Resuscitate: no map trip",
              okPlan && P(ip) == "inv1x2 inv0.6x2", P(ip));
        okPlan = Gm21MedicineSupplies.PlanInventoryFirst(new List<Gm21SupplyStack> { I(0.6f, 2, 1) },
            new List<Gm21SupplyStack> { M(1.6f, 9, 2f, 3) }, 1f, ip, out ia);
        Check("Cure: two carried herbal beat a glitterworld next door (lower potency, already in hand)",
              okPlan && P(ip) == "inv0.6x2", P(ip));
        okPlan = Gm21MedicineSupplies.PlanInventoryFirst(new List<Gm21SupplyStack> { I(0.6f, 1, 1) },
            new List<Gm21SupplyStack> { M(1f, 5, 1f, 4), M(1.6f, 2, 100f, 5) }, 3f, ip, out ia);
        Check("carried medicine short of the budget: all of it is used first, the map completes the budget (best first)",
              okPlan && P(ip) == "inv0.6x1 map1.6x2", P(ip));
        List<Gm21SupplyStack> mapA = new List<Gm21SupplyStack> { M(1f, 1, 4f, 30), M(1f, 1, 4f, 10), M(1f, 1, 4f, 20), M(1f, 1, 1f, 40) };
        List<Gm21SupplyStack> mapB = new List<Gm21SupplyStack>(mapA); mapB.Reverse();
        List<KeyValuePair<Gm21SupplyStack, int>> pa = new List<KeyValuePair<Gm21SupplyStack, int>>(), pb = new List<KeyValuePair<Gm21SupplyStack, int>>();
        Gm21MedicineSupplies.PlanInventoryFirst(new List<Gm21SupplyStack>(), mapA, 3f, pa, out ia);
        Gm21MedicineSupplies.PlanInventoryFirst(new List<Gm21SupplyStack>(), mapB, 3f, pb, out ia);
        Check("equal map candidates: nearest first, then by ID -- the same plan whatever order they were found in",
              string.Join(",", pa.Select(kv => kv.Key.id.ToString()).ToArray()) == "40,10,20"
              && pa.Select(kv => kv.Key.id).SequenceEqual(pb.Select(kv => kv.Key.id)),
              string.Join(",", pa.Select(kv => kv.Key.id.ToString()).ToArray()));
        okPlan = Gm21MedicineSupplies.PlanInventoryFirst(new List<Gm21SupplyStack> { I(0.6f, 1, 1) },
            new List<Gm21SupplyStack>(), 2f, ip, out ia);
        Check("carried and map medicine together still short: refused, reporting what exists",
              !okPlan && ip.Count == 0 && Near(ia, 0.6f));
        using (AssemblyDefinition mod = AssemblyDefinition.ReadAssembly(Mod.Location))
        {
            MethodDefinition orderPlan = mod.MainModule.GetType("Grandmaster21.Gm21MedicineSupplies").Methods.First(m => m.Name == "TryPlanOrder");
            string[] calls = orderPlan.Body.Instructions.Select(x => x.Operand as MethodReference).Where(r => r != null).Select(r => r.Name).ToArray();
            Check("the order plan uses PlanInventoryFirst, and the map is searched only when the carried medicine falls short",
                  calls.Contains("PlanInventoryFirst") && calls.Contains("MapStacks") && calls.Contains("InventoryStacks")
                  && !calls.Contains("Gather"));
        }

        // Medical care.
        Pawn noSettings = MakePawn(0);
        Check("no patient (Resuscitate) -> no medical-care restriction", !Gm21MedicineSupplies.CareFor(null).HasValue);
        Check("a patient with no setting and no running game -> no restriction (headless fallback)",
              !Gm21MedicineSupplies.CareFor(noSettings).HasValue);
        Pawn noMeds = MakePawn(0);
        noMeds.playerSettings = Uninit<Pawn_PlayerSettings>(); noMeds.playerSettings.medCare = MedicalCareCategory.NoMeds;
        Pawn best = MakePawn(0);
        best.playerSettings = Uninit<Pawn_PlayerSettings>(); best.playerSettings.medCare = MedicalCareCategory.Best;
        ThingDef industrial = MedDef("TestIndustrialMedicine", 1.0f), herbal = MedDef("TestHerbalMedicine", 0.6f),
                 modded = MedDef("TestModdedSalve", 0.35f), notMedicine = MedDef("TestSteel", 0f);
        notMedicine.statBases.Clear();
        Check("the patient's own medical care is respected: 'no medicine' allows none, 'best' allows all",
              !Gm21MedicineSupplies.Allowed(noMeds, industrial) && Gm21MedicineSupplies.Allowed(best, industrial)
              && Gm21MedicineSupplies.Allowed(null, industrial));

        // Real potency read (the same MedicalPotency stat vanilla uses), including a modded medicine.
        Check("real stat read: potency of medicine defs by their loaded MedicalPotency (no DefNames)",
              Near(Gm21MedicineSupplies.PotencyOf(industrial), 1f) && Near(Gm21MedicineSupplies.PotencyOf(herbal), 0.6f)
              && Near(Gm21MedicineSupplies.PotencyOf(modded), 0.35f) && Gm21MedicineSupplies.PotencyOf(notMedicine) == 0f,
              Gm21MedicineSupplies.PotencyOf(industrial) + "/" + Gm21MedicineSupplies.PotencyOf(herbal) + "/" + Gm21MedicineSupplies.PotencyOf(modded));

        // Consumption through real Things in a real inventory ThingOwner.
        try
        {
            Pawn doctor = MakePawn(21);
            doctor.inventory = new Pawn_InventoryTracker(doctor);
            doctor.inventory.innerContainer.TryAdd(Stack(industrial, 2));
            doctor.inventory.innerContainer.TryAdd(Stack(herbal, 5));
            string reason;
            float before = Potency(doctor);

            plan.Clear();
            bool planned = Gm21MedicineSupplies.TryPlanFromInventory(doctor, best, Gm21Medicine.CurePotencyBudget, plan, out reason);
            Check("an interrupted intervention consumes nothing: planning alone leaves the inventory intact",
                  planned && Near(Potency(doctor), before) && Units(doctor, industrial) == 2 && Units(doctor, herbal) == 5);
            int used = Gm21MedicineSupplies.Consume(plan);
            Check("a completed Cure consumes exactly one industrial medicine, nothing else",
                  used == 1 && Units(doctor, industrial) == 1 && Units(doctor, herbal) == 5 && Near(Potency(doctor), before - 1f),
                  "used=" + used + " industrial=" + Units(doctor, industrial) + " herbal=" + Units(doctor, herbal));

            plan.Clear();
            planned = Gm21MedicineSupplies.TryPlanFromInventory(doctor, best, Gm21Medicine.ResuscitatePotencyBudget, plan, out reason);
            used = Gm21MedicineSupplies.Consume(plan);
            Check("a completed Resuscitation combines stacks: industrial 1 + herbal 4 (3.4 >= 3.0), whole stack removed",
                  planned && used == 5 && Units(doctor, industrial) == 0 && Units(doctor, herbal) == 1
                  && !doctor.inventory.innerContainer.Any(x => x.def == industrial),
                  "used=" + used + " industrial=" + Units(doctor, industrial) + " herbal=" + Units(doctor, herbal));

            plan.Clear();
            planned = Gm21MedicineSupplies.TryPlanFromInventory(doctor, best, Gm21Medicine.ReconstructPotencyBudget, plan, out reason);
            Check("not enough left for Reconstruct (0.6 of 2.0): refused, with the reason, nothing consumed",
                  !planned && plan.Count == 0 && Units(doctor, herbal) == 1 && reason != null);

            plan.Clear();
            Check("medical care is applied at completion too: 'no medicine' patient -> refused",
                  !Gm21MedicineSupplies.TryPlanFromInventory(doctor, noMeds, 0.5f, plan, out reason));

            doctor.inventory.innerContainer.TryAdd(Stack(modded, 3));
            plan.Clear();
            planned = Gm21MedicineSupplies.TryPlanFromInventory(doctor, best, 1f, plan, out reason);
            used = Gm21MedicineSupplies.Consume(plan);
            Check("modded medicine is consumed by its own potency (herbal 1 + salve 2 = 1.3 >= 1.0)",
                  planned && used == 3 && Units(doctor, herbal) == 0 && Units(doctor, modded) == 1,
                  "used=" + used + " herbal=" + Units(doctor, herbal) + " salve=" + Units(doctor, modded));
        }
        catch (Exception e) { Blocked("real medicine Things in a real inventory", e); }

        // Lifecycle, by IL: the only consumption site, and it sits behind a successful apply.
        using (AssemblyDefinition mod = AssemblyDefinition.ReadAssembly(Mod.Location))
        {
            List<string> consumeSites = new List<string>();
            List<string> destroySites = new List<string>();
            foreach (TypeDefinition type in AllTypes(mod.MainModule))
                foreach (MethodDefinition m in type.Methods.Where(x => x.HasBody))
                    foreach (Instruction i in m.Body.Instructions)
                    {
                        MethodReference r = i.Operand as MethodReference;
                        if (r == null) continue;
                        if (r.Name == "Consume" && r.DeclaringType.Name == "Gm21MedicineSupplies") consumeSites.Add(type.Name + "." + m.Name);
                        if ((r.Name == "Destroy" || r.Name == "SplitOff") && type.FullName.Contains("JobDriver_Gm21")) destroySites.Add(type.Name + "." + m.Name);
                    }
            Check("medicine is consumed at exactly ONE site: JobDriver_Gm21Intervention.Finish",
                  consumeSites.Count == 1 && consumeSites[0] == "JobDriver_Gm21Intervention.Finish", string.Join(",", consumeSites.ToArray()));
            Check("no intervention driver destroys or splits a Thing itself", destroySites.Count == 0, string.Join(",", destroySites.ToArray()));

            MethodDefinition finish = mod.MainModule.GetType("Grandmaster21.JobDriver_Gm21Intervention").Methods.First(x => x.Name == "Finish");
            List<Instruction> ins = finish.Body.Instructions.ToList();
            int complete = ins.FindIndex(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "Complete");
            int planInv = ins.FindIndex(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "TryPlanFromInventory");
            int validate = ins.FindIndex(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "Validate");
            int consume = ins.FindIndex(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "Consume");
            bool guarded = complete >= 0 && complete + 1 < ins.Count
                           && (ins[complete + 1].OpCode == OpCodes.Brtrue || ins[complete + 1].OpCode == OpCodes.Brtrue_S
                               || ins[complete + 1].OpCode == OpCodes.Brfalse || ins[complete + 1].OpCode == OpCodes.Brfalse_S);
            Check("Finish order: validate -> plan from inventory -> apply -> consume, consumption guarded by the apply's result",
                  validate >= 0 && validate < planInv && planInv < complete && complete < consume && guarded,
                  validate + "<" + planInv + "<" + complete + "<" + consume + " guarded=" + guarded);

            TypeDefinition driver = mod.MainModule.GetType("Grandmaster21.JobDriver_Gm21Intervention");
            string[] toils = driver.NestedTypes.SelectMany(n => n.Methods).Concat(driver.Methods).Where(x => x.HasBody)
                .SelectMany(x => x.Body.Instructions).Select(i => i.Operand as MethodReference).Where(r => r != null)
                .Select(r => r.DeclaringType.Name + "." + r.Name).Distinct().ToArray();
            Check("medicine is acquired through vanilla toils (queue extraction, TakeToInventory, stack reservation)",
                  toils.Contains("Toils_JobTransforms.ExtractNextTargetFromQueue") && toils.Contains("Toils_Haul.TakeToInventory")
                  && toils.Contains("ReservationManager.CanReserveStack"));
        }
    }

    static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module)
    {
        foreach (TypeDefinition t in module.Types)
        {
            yield return t;
            foreach (TypeDefinition n in t.NestedTypes) yield return n;
        }
    }

    // ------------------------------------------------------------------ 16. work time, self, hostile, no limits

    static void WorkAndAccess(string root)
    {
        Console.WriteLine("\n=== 16. Work time, self-intervention, hostile corpses, no cooldowns ===");
        Func<int, float, int> W = Gm21Medicine.WorkTicksForSpeed;
        Check("base work: Cure 2500 < Reconstruct 6000 < Resuscitate 7500 (provisional)",
              Gm21Medicine.CureWorkTicks == 2500 && Gm21Medicine.ReconstructWorkTicks == 6000 && Gm21Medicine.ResuscitateWorkTicks == 7500);
        Check("speed 1 -> base work", W(2500, 1f) == 2500 && W(7500, 1f) == 7500);
        Check("speed scales work exactly (x2 -> half, x0.5 -> double, x1.6 -> /1.6)",
              W(2500, 2f) == 1250 && W(2500, 0.5f) == 5000 && W(6000, 1.6f) == 3750);
        Check("every speed from 0.1 to 200 divides the work exactly, down to the 60-tick floor",
              Enumerable.Range(1, 2000).All(k => W(10000, k / 10f) == Math.Max(60, (int)Math.Round(10000 / (double)(k / 10f), MidpointRounding.ToEven))));
        Check("NO 10x ceiling: speed 10 -> base/10, 25 -> base/25, 50 -> base/50",
              W(7500, 10f) == 750 && W(7500, 25f) == 300 && W(7500, 50f) == 150);
        Check("a high enough speed bottoms out at MinWorkTicks (60): 100 on Cure, 1000, 1e30",
              W(2500, 100f) == 60 && W(7500, 1000f) == 60 && W(7500, 1e30f) == 60);
        Check("zero / negative / tiny speeds count as the 0.1 minimum (work x10, never stalls or inverts)",
              W(2500, 0f) == 25000 && W(2500, -3f) == 25000 && W(2500, 1e-9f) == 25000);
        Check("non-finite speeds count as 1", W(2500, float.NaN) == 2500 && W(2500, float.PositiveInfinity) == 2500
                                               && W(2500, float.NegativeInfinity) == 2500);
        Check("the old upper clamp constant is gone", typeof(Gm21Medicine).GetField("MaxWorkSpeed") == null);

        // Self-intervention.
        Pawn gm = MakePawn(21);
        string reason;
        manipulationLevel = 1f;
        Check("self-Reconstruct allowed with full manipulation", Gm21MedicineOrders.CanReconstructOn(gm, gm, out reason));
        manipulationLevel = 0.5f;
        Check("self-Reconstruct allowed at 50% manipulation (one arm lost)", Gm21MedicineOrders.CanReconstructOn(gm, gm, out reason));
        manipulationLevel = 0.3f;
        Check("self-Reconstruct refused below 50% manipulation, with a reason",
              !Gm21MedicineOrders.CanReconstructOn(gm, gm, out reason) && !string.IsNullOrEmpty(reason));
        Pawn other = MakePawn(0);
        Check("...while Reconstruct on SOMEONE ELSE needs only the ordinary ability to practise",
              Gm21MedicineOrders.CanReconstructOn(gm, other, out reason));
        manipulationLevel = 1f;
        capable = false;
        Check("a Grandmaster who cannot practise cannot self-Reconstruct", !Gm21Medicine.CanSelfReconstruct(gm));
        capable = true;

        using (AssemblyDefinition mod = AssemblyDefinition.ReadAssembly(Mod.Location))
        {
            TypeDefinition orders = mod.MainModule.GetType("Grandmaster21.Gm21MedicineOrders");
            string[] strings = orders.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions)
                .Where(i => i.OpCode == OpCodes.Ldstr).Select(i => (string)i.Operand).ToArray();
            Check("the self ban is gone (no 'NotSelf' rejection anywhere in the orders)", !strings.Any(x => x.Contains("NotSelf")));
            MethodDefinition patientParams = orders.Methods.First(m => m.Name == "PatientParameters");
            List<Instruction> pi = patientParams.Body.Instructions.ToList();
            int store = pi.FindIndex(i => i.OpCode == OpCodes.Stfld && ((FieldReference)i.Operand).Name == "canTargetSelf");
            Check("targeting allows the Grandmaster themself", store > 0 && pi[store - 1].OpCode == OpCodes.Ldc_I4_1);
            Check("a hostile corpse asks for confirmation (vanilla confirmation box, 'will remain hostile')",
                  strings.Contains("GM21_Med_ResHostileWarning")
                  && orders.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions)
                     .Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "CreateConfirmation"));
            string[] resStrings = mod.MainModule.GetType("Grandmaster21.Gm21Resuscitation").Methods.Where(m => m.HasBody)
                .SelectMany(m => m.Body.Instructions).Where(i => i.OpCode == OpCodes.Ldstr).Select(i => (string)i.Operand).ToArray();
            Check("resuscitation never refuses for hostility (no ResHostile reason)", !resStrings.Any(x => x == "GM21_Med_ResHostile"));
            bool setsNoLord = mod.MainModule.GetType("Grandmaster21.Gm21Resuscitation").Methods.Where(m => m.HasBody)
                .SelectMany(m => m.Body.Instructions).Any(i => i.OpCode == OpCodes.Stfld && ((FieldReference)i.Operand).Name == "noLord");
            string[] revivalCalls = new[] { "Gm21Resuscitation", "Gm21Trauma" }
                .Select(n => mod.MainModule.GetType("Grandmaster21." + n)).SelectMany(t => t.Methods.Concat(t.NestedTypes.SelectMany(x => x.Methods)))
                .Where(m => m.HasBody).SelectMany(m => m.Body.Instructions)
                .Select(i => i.Operand as MethodReference).Where(r => r != null).Select(r => r.Name).Distinct().ToArray();
            string[] allegiance = revivalCalls.Where(n => n == "SetFaction" || n == "DoRecruit" || n == "SetGuestStatus"
                                                          || n.Contains("Recruit") || n == "MakeNewLord").ToArray();
            Check("faction and hostility are left to vanilla's revival (no noLord, no faction change, no recruitment)",
                  !setsNoLord && allegiance.Length == 0, string.Join(",", allegiance));
        }

        // No cooldown, charge or rate limit anywhere: time and medicine are the whole price.
        string[] banned = { "cooldown", "charge", "lastuse", "lastintervention", "perday", "daily", "usesleft", "usestoday" };
        List<string> offenders = new List<string>();
        foreach (Type type in Mod.GetTypes().Where(x => x.Namespace == "Grandmaster21"))
        {
            if (!(type.Name.Contains("Med") || type.Name.Contains("Gm21Intervention") || type.Name.Contains("Resuscitat")
                  || type.Name.Contains("Cure") || type.Name.Contains("Reconstruct") || type.Name.Contains("Trauma")
                  || type.Name.Contains("Supply") || type.Name.Contains("Treatment"))) continue;
            foreach (FieldInfo fi in type.GetFields(Any))
                if (banned.Any(b => fi.Name.ToLowerInvariant().Contains(b))) offenders.Add(type.Name + "." + fi.Name);
        }
        Check("no cooldown / charge / per-day field exists on any Medicine type", offenders.Count == 0, string.Join(",", offenders.ToArray()));

        string source = string.Join("\n", Directory.GetFiles(Path.Combine(root, "Source/Medicine"), "*.cs").Select(File.ReadAllText).ToArray());
        string[] keys = Regex.Matches(source, "Scribe_[A-Za-z]+\\.Look\\(ref [^,]+, \"([^\"]+)\"").Cast<Match>().Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x).ToArray();
        string[] expected = { "gm21HeldPatient", "gm21MedicineMode", "gm21TreatmentQuality", "gm21TreatmentTick", "gm21WorkStartedTick", "pathEndMode" };
        Check("persisted state is exactly: treatment, mode, and the driver's work clock (no limit counters)",
              keys.SequenceEqual(expected), string.Join(",", keys));
    }

    // ------------------------------------------------------------------ 17. vital anatomy

    sealed class VitalBody
    {
        public BodyDef def;
        public BodyPartRecord torso, pump, filterA, filterB, liver, lungA, lungB, stalk, dome, mind, chest, pump2, limb, hand, dualOrgan, gut;
    }

    /// Non-human names throughout; tags decide everything.
    static VitalBody MakeVitalBody(bool chestPump = false, bool dual = false)
    {
        parts = new List<BodyPartRecord>();
        VitalBody b = new VitalBody();
        b.torso = Part("core", null);
        if (!chestPump && !dual) b.pump = Part("pump", b.torso, PumpTag);
        b.filterA = Part("filterA", b.torso, KidneyTag);
        b.filterB = Part("filterB", b.torso, KidneyTag);
        b.liver = Part("processor", b.torso, LiverTag, dual ? null : MetabolismTag);
        b.lungA = Part("bellowsA", b.torso, LungTag);
        b.lungB = Part("bellowsB", b.torso, LungTag);
        b.stalk = Part("stalk", b.torso, PathwayTag);
        b.dome = Part("dome", b.stalk);
        b.mind = Part("mind", b.dome, ConsciousnessTag);
        if (chestPump)
        {
            b.chest = Part("casing", b.torso);
            b.pump2 = Part("pumpInCasing", b.chest, PumpTag);
        }
        if (dual)
        {
            b.dualOrgan = Part("dualOrgan", b.torso, PumpTag, MetabolismTag);
            b.gut = Part("gut", b.torso, MetabolismTag);
        }
        b.limb = Part("limb", b.torso);
        b.hand = Part("grasp", b.limb);
        foreach (BodyPartRecord r in parts) r.def.tags.RemoveAll(x => x == null);
        b.def = new BodyDef { defName = "TestVitalBody", corePart = b.torso };
        ((List<BodyPartRecord>)Get(b.def, "cachedAllParts")).AddRange(parts);
        foreach (BodyPartRecord r in parts) r.body = b.def;
        return b;
    }

    static Pawn PawnOn(BodyDef body)
    {
        return MakePawn(0, new Body { def = body });
    }

    static Hediff_MissingPart Lose(Pawn p, BodyPartRecord part, HediffDef lastInjury = null)
    {
        Hediff_MissingPart m = Attach<Hediff_MissingPart>(p, HediffDefOf.MissingBodyPart, part);
        m.lastInjury = lastInjury;
        foreach (BodyPartRecord child in part.parts) Lose(p, child, lastInjury);
        return m;
    }

    static string Names(List<Gm21VitalRebuild> plan) { return string.Join(",", plan.Select(r => r.part.def.defName).ToArray()); }

    static void VitalAnatomy()
    {
        Console.WriteLine("\n=== 17. Minimum viable vital reconstruction (tags, never names) ===");
        HediffDef gunshot = Injury("TestGunshotV", true);
        List<Gm21VitalRebuild> plan = new List<Gm21VitalRebuild>();

        VitalBody b = MakeVitalBody();
        Pawn p = PawnOn(b.def);
        Check("required tags mirror the workers: kidney x liver when the body has kidneys, never the source tag too",
              Gm21Resuscitation.RequiredVitalTags(b.def).Contains(KidneyTag) && Gm21Resuscitation.RequiredVitalTags(b.def).Contains(LiverTag)
              && !Gm21Resuscitation.RequiredVitalTags(b.def).Contains(FilterSourceTag));
        Check("intact body: nothing to rebuild", Gm21Resuscitation.PlanVitalRebuild(p.health.hediffSet, b.def, plan) && plan.Count == 0);

        Lose(p, b.pump, gunshot);
        Lose(p, b.limb);
        Check("destroyed pump + lost limb: rebuild the pump ONLY; the limb stays missing",
              Gm21Resuscitation.PlanVitalRebuild(p.health.hediffSet, b.def, plan) && Names(plan) == "pump" && plan[0].lastInjury == gunshot,
              Names(plan));

        b = MakeVitalBody(); p = PawnOn(b.def);
        Lose(p, b.filterA); Lose(p, b.filterB);
        Check("both filtration organs lost: rebuild ONE (the first), not both",
              Gm21Resuscitation.PlanVitalRebuild(p.health.hediffSet, b.def, plan) && Names(plan) == "filterA", Names(plan));

        b = MakeVitalBody(); p = PawnOn(b.def);
        Lose(p, b.lungA);
        Check("one of two breathing organs lost: still viable, nothing rebuilt",
              Gm21Resuscitation.PlanVitalRebuild(p.health.hediffSet, b.def, plan) && plan.Count == 0);

        b = MakeVitalBody(); p = PawnOn(b.def);
        Lose(p, b.dome);
        Check("consciousness anatomy lost: never planned for rebuild (the hard boundary is Decide's)",
              Gm21Resuscitation.PlanVitalRebuild(p.health.hediffSet, b.def, plan) && plan.Count == 0
              && Gm21Resuscitation.TagEntirelyMissing(p.health.hediffSet, b.def, BodyPartTagDefOf.ConsciousnessSource));

        b = MakeVitalBody(chestPump: true); p = PawnOn(b.def);
        Lose(p, b.chest);
        Check("the only pump sits inside a lost casing: refused (rebuilding it would drag the casing back)",
              !Gm21Resuscitation.PlanVitalRebuild(p.health.hediffSet, b.def, plan));

        b = MakeVitalBody(); p = PawnOn(b.def);
        Lose(p, b.stalk);
        Check("a lost breathing pathway whose subtree holds the mind: brain destroyed is what decides",
              Gm21Resuscitation.TagEntirelyMissing(p.health.hediffSet, b.def, BodyPartTagDefOf.ConsciousnessSource));

        b = MakeVitalBody(); p = PawnOn(b.def);
        Attach<Hediff_AddedPart>(p, new HediffDef { defName = "TestArtificialCore", hediffClass = typeof(Hediff_AddedPart), isBad = false }, b.torso);
        Lose(p, b.pump);
        Check("a vital part under an artificial part is not rebuilt biologically: refused",
              !Gm21Resuscitation.PlanVitalRebuild(p.health.hediffSet, b.def, plan));

        b = MakeVitalBody(dual: true); p = PawnOn(b.def);
        Lose(p, b.dualOrgan); Lose(p, b.gut); Lose(p, b.liver);
        // Lost tags: pump (dualOrgan only), liver, metabolism (dualOrgan, gut, processor).
        Check("one part covering two lost tags is preferred (fewest rebuilds): dualOrgan + processor",
              Gm21Resuscitation.PlanVitalRebuild(p.health.hediffSet, b.def, plan) && Names(plan) == "dualOrgan,processor", Names(plan));
    }

    // ------------------------------------------------------------------ 18. death trauma

    static HediffDef Injury(string name, bool scarrable)
    {
        HediffDef d = new HediffDef { defName = name, label = name, hediffClass = typeof(Hediff_Injury), injuryProps = new InjuryProps() };
        d.comps = new List<HediffCompProperties> { new HediffCompProperties_TendDuration() };
        if (scarrable) d.comps.Add(new HediffCompProperties_GetsPermanent());
        return d;
    }

    static Gm21TraumaEvidence E(int index, float severity, float max = 40f, bool scar = true, bool destroyed = false,
                                bool vital = false, bool blow = false)
    {
        return new Gm21TraumaEvidence { partIndex = index, severity = severity, partMaxHealth = max, scarrable = scar,
                                        destroyed = destroyed, vital = vital, lethalBlow = blow, scarSourceSeverity = severity };
    }

    static string RankOf(params Gm21TraumaEvidence[] e)
    {
        return string.Join(",", Gm21Trauma.Rank(e, Gm21Medicine.MaxTraumaScars).Select(i => e[i].partIndex.ToString()).ToArray());
    }

    static void Trauma()
    {
        Console.WriteLine("\n=== 18. Death trauma: ranking and permanent scars ===");
        Check("one catastrophic wound -> one scar", RankOf(E(3, 30f)) == "3");
        Check("shredded by many wounds -> the three worst locations, worst first",
              RankOf(E(1, 8f), E(2, 30f), E(3, 12f), E(4, 20f), E(5, 5f)) == "2,4,3");
        Check("only scratches (under 10% of the part) -> no scars invented", RankOf(E(1, 2f), E(2, 3f)) == "");
        Check("no evidence at all (death by disease) -> no scars", RankOf() == "");
        Check("a wound vanilla never scars (bruise-like) is skipped, not substituted",
              RankOf(E(1, 30f, scar: false), E(2, 10f)) == "2");
        Check("a destroyed-and-rebuilt vital part outranks a heavy torso wound",
              RankOf(E(1, 36f), E(7, 0f, 15f, destroyed: true, vital: true)) == "7,1");
        Check("vital-part damage outranks equal relative damage elsewhere",
              RankOf(E(1, 10f, 40f), E(2, 5f, 20f, vital: true)) == "2,1");
        Check("the battle log's death-blow location qualifies even when light, and ranks up",
              RankOf(E(1, 2f, 40f, blow: true)) == "1" && RankOf(E(1, 8f), E(2, 8f, blow: true)) == "2,1");
        Check("ties break by body order, deterministically", RankOf(E(9, 10f), E(4, 10f)) == "4,9");
        Check("one scar per location, never duplicates", RankOf(E(5, 30f), E(5, 20f), E(6, 10f)) == "5,6");
        Check("never more than three", Gm21Trauma.Rank(Enumerable.Range(0, 10).Select(i => E(i, 10f + i)).ToList(), 3).Count == 3);

        Check("scar severity: the wound's own, capped at 40% of the part",
              Near(Gm21Trauma.ScarSeverity(E(1, 10f, 40f)), 10f) && Near(Gm21Trauma.ScarSeverity(E(1, 30f, 40f)), 16f));
        Check("scar on a rebuilt vital part: 40% of it (meaningful, not destroying it again)",
              Near(Gm21Trauma.ScarSeverity(E(1, 0f, 15f, destroyed: true, vital: true)), 6f));

        // Evidence from a real body state.
        HediffDef gunshot = Injury("TestGunshotT", true), bruise = Injury("TestBruiseT", false), cut = Injury("TestCutT", true);
        VitalBody b = MakeVitalBody();
        Pawn p = PawnOn(b.def);
        foreach (BodyPartRecord r in b.def.AllParts) r.def.hitPoints = 30;
        b.pump.def.hitPoints = 15;
        Lose(p, b.pump, gunshot);
        Lose(p, b.limb, gunshot);
        List<Gm21InjurySnapshot> injuries = new List<Gm21InjurySnapshot>
        {
            Snap(p, gunshot, b.torso, 9f), Snap(p, bruise, b.torso, 12f), Snap(p, cut, b.torso, 3f),
            Snap(p, cut, b.lungA, 1f)
        };
        List<Gm21VitalRebuild> rebuild = new List<Gm21VitalRebuild>();
        Gm21Resuscitation.PlanVitalRebuild(p.health.hediffSet, b.def, rebuild);
        List<Gm21TraumaEvidence> ev = Gm21Trauma.Collect(p, injuries, rebuild, null);
        Gm21TraumaEvidence torso = ev.First(x => x.part == b.torso), pumpEv = ev.First(x => x.part == b.pump);
        Check("evidence is one entry per location; the lost limb (non-vital) contributes none",
              ev.Count == 3 && !ev.Any(x => x.part == b.limb || x.part == b.hand), string.Join(",", ev.Select(x => x.part.def.defName).ToArray()));
        Check("location severity sums its fresh wounds; the scar takes the worst SCARRABLE wound's type",
              Near(torso.severity, 24f) && torso.scarDef == gunshot && Near(torso.scarSourceSeverity, 9f));
        Check("the rebuilt pump is destroyed + vital evidence, scarred as what destroyed it",
              pumpEv.destroyed && pumpEv.vital && pumpEv.scarDef == gunshot && pumpEv.scarrable);
        List<int> chosen = Gm21Trauma.Rank(ev, 3);
        Check("real body: rebuilt pump first, then the torso; the 1-point lung graze is not scar material",
              chosen.Count == 2 && ev[chosen[0]].part == b.pump && ev[chosen[1]].part == b.torso);

        // Applying: the restored wound itself becomes the vanilla permanent injury.
        Hediff_Injury restored = Attach<Hediff_Injury>(p, gunshot, b.torso);
        Set(restored, "severityInt", 9f);
        Hediff_Injury scar = Gm21Trauma.ApplyScar(p, torso, injuries[0]);
        Check("the location's restored gunshot is converted, not duplicated: permanent, severity kept (under the cap)",
              scar == restored && restored.IsPermanent() && Near(restored.Severity, 9f)
              && p.health.hediffSet.hediffs.Count(h => h is Hediff_Injury && h.Part == b.torso) == 1);
        Hediff_Injury heavy = Attach<Hediff_Injury>(p, cut, b.lungB);
        Set(heavy, "severityInt", 25f);
        Gm21TraumaEvidence lungEv = new Gm21TraumaEvidence { part = b.lungB, partIndex = b.lungB.Index, severity = 25f, partMaxHealth = 30f,
                                                             scarrable = true, scarDef = cut, scarSourceSeverity = 25f };
        Gm21Trauma.ApplyScar(p, lungEv, null);
        Check("a heavy wound's scar is capped at 40% of the part (12 of 30), never heavier than the wound",
              heavy.IsPermanent() && Near(heavy.Severity, 12f), heavy.Severity.ToString());
        Check("a location that is missing again is never scarred", Gm21Trauma.ApplyScar(p, new Gm21TraumaEvidence
              { part = b.limb, scarrable = true, scarDef = gunshot, partMaxHealth = 30f, scarSourceSeverity = 10f, severity = 10f }, null) == null);

        Pawn armoured = PawnOn(b.def);
        Attach<Hediff_AddedPart>(armoured, new HediffDef { defName = "TestPlating", hediffClass = typeof(Hediff_AddedPart), isBad = false }, b.limb);
        List<Gm21TraumaEvidence> ev2 = Gm21Trauma.Collect(armoured, new List<Gm21InjurySnapshot> { Snap(armoured, gunshot, b.hand, 20f) },
                                                          new List<Gm21VitalRebuild>(), null);
        Check("a wound under an artificial part is never scar material (as vanilla)", ev2.Count == 1 && !ev2[0].scarrable);
        Check("no battle log (none headless): the death-blow lookup answers null without throwing",
              Gm21Trauma.TryFindDeathBlowPart(p, 0) == null);
        Check("...and ranking from the body alone still selects the same scars",
              Gm21Trauma.Rank(Gm21Trauma.Collect(p, injuries, rebuild, null), 3).Count == 2);
        Check("the death blow flag lands on the named location only",
              Gm21Trauma.Collect(p, injuries, rebuild, b.torso).Count(x => x.lethalBlow) == 1
              && Gm21Trauma.Collect(p, injuries, rebuild, b.torso).First(x => x.lethalBlow).part == b.torso);
    }

    static Gm21InjurySnapshot Snap(Pawn p, HediffDef def, BodyPartRecord part, float severity)
    {
        Hediff_Injury h = Attach<Hediff_Injury>(p, def, part, false);
        Set(h, "severityInt", severity);
        h.sourceLabel = "test rifle";
        return Gm21InjurySnapshot.Of(h);
    }

    // ------------------------------------------------------------------ 19. driver save state

    static void DriverSave(string path)
    {
        Console.WriteLine("\n=== 19. Intervention driver save state (real Scribe) ===");
        try
        {
            JobDriver_Gm21Cure driver = Uninit<JobDriver_Gm21Cure>();
            Set(driver, "workStartedTick", 123456);
            Set(driver, "heldPatient", true);
            Set(driver, "pathEndMode", Verse.AI.PathEndMode.OnCell);
            Scribe.saver.InitSaving(path, "driver"); driver.ExposeData(); Scribe.saver.FinalizeSaving();
            XElement root = XDocument.Load(path).Root;
            Check("the work clock, held-patient flag and path mode are written",
                  root.Element("gm21WorkStartedTick").Value == "123456" && root.Element("gm21HeldPatient").Value == "True"
                  && root.Element("pathEndMode").Value == "OnCell", root.ToString());
            JobDriver_Gm21Cure back = Uninit<JobDriver_Gm21Cure>();
            logged.Clear();
            try
            {
                Scribe.loader.InitLoading(path); back.ExposeData(); Scribe.loader.FinalizeLoading();
                if (LoadBlocked()) throw new InvalidOperationException("ParseHelper unavailable");
                Check("...and read back: a reload mid-work keeps the clock (resuscitation window) and the release duty",
                      (int)Get(back, "workStartedTick") == 123456 && (bool)Get(back, "heldPatient")
                      && (Verse.AI.PathEndMode)Get(back, "pathEndMode") == Verse.AI.PathEndMode.OnCell);
            }
            catch (Exception e) { Scribe.ForceStop(); Blocked("driver load", e); }
        }
        catch (Exception e) { Scribe.ForceStop(); Blocked("driver save", e); }
        // A death-trauma scar is an ordinary vanilla permanent injury: vanilla saves it, GM21 adds nothing.
        try
        {
            HediffDef scarDef = Injury("TestScarSave", true);
            DefDatabase<HediffDef>.Add(scarDef);
            Pawn p = MakePawn(0);
            Hediff_Injury scar = Attach<Hediff_Injury>(p, scarDef);
            Set(scar, "severityInt", 6f);
            scar.TryGetComp<HediffComp_GetsPermanent>().isPermanentInt = true;
            Hediff h = scar;
            Scribe.saver.InitSaving(path, "root"); Scribe_Deep.Look(ref h, "hediff"); Scribe.saver.FinalizeSaving();
            XElement node = XDocument.Load(path).Root.Element("hediff");
            Check("a death scar saves as plain vanilla state (isPermanent, no GM21 element)",
                  node.Element("isPermanent") != null && node.Element("isPermanent").Value == "True"
                  && !node.Elements().Any(e => e.Name.LocalName.StartsWith("gm21")), node.ToString());
            logged.Clear();
            try
            {
                Hediff back = null;
                Scribe.loader.InitLoading(path); Scribe_Deep.Look(ref back, "hediff"); Scribe.loader.FinalizeLoading();
                if (LoadBlocked()) throw new InvalidOperationException("ParseHelper unavailable");
                Check("...and loads back permanent, at its severity", back != null && back.IsPermanent() && Near(back.Severity, 6f));
            }
            catch (Exception e) { Scribe.ForceStop(); Blocked("scar load", e); }
        }
        catch (Exception e) { Scribe.ForceStop(); Blocked("scar save", e); }

        Check("the medicine plan is vanilla job state (targetQueueB/countQueue), saved by Job itself",
              typeof(Verse.AI.Job).GetField("targetQueueB") != null && typeof(Verse.AI.Job).GetField("countQueue") != null);
    }

    // ------------------------------------------------------------------ 20. decay commitment and resuscitation shock

    static HediffDef ShockDef(float setMax)
    {
        HediffDef d = new HediffDef { defName = "GM21_ResuscitationShock", label = "resuscitation shock",
                                      hediffClass = typeof(HediffWithComps), everCurableByItem = false };
        d.comps = new List<HediffCompProperties>
        {
            new HediffCompProperties_Disappears
            {
                disappearsAfterTicks = new IntRange(Gm21Medicine.ResuscitationShockTicks, Gm21Medicine.ResuscitationShockTicks),
                showRemainingTime = true
            }
        };
        d.stages = new List<HediffStage>
        {
            new HediffStage { capMods = new List<PawnCapacityModifier> { new PawnCapacityModifier { capacity = PawnCapacityDefOf.Consciousness, setMax = setMax } } }
        };
        return d;
    }

    static void CommitAndShock(string root, string path)
    {
        Console.WriteLine("\n=== 20. Decay commitment, and Grandmaster Resuscitation Shock ===");
        using (AssemblyDefinition mod = AssemblyDefinition.ReadAssembly(Mod.Location))
        {
            // When decay is judged, and when the procedure commits.
            TypeDefinition driver = mod.MainModule.GetType("Grandmaster21.JobDriver_Gm21Intervention");
            Func<TypeDefinition, IEnumerable<TypeDefinition>> nested = null;
            nested = t => new[] { t }.Concat(t.NestedTypes.SelectMany(n => nested(n)));
            MethodDefinition preInit = nested(driver).SelectMany(t => t.Methods).Where(m => m.HasBody)
                .FirstOrDefault(m => m.Name != ".ctor"
                                     && m.Body.Instructions.Any(i => i.OpCode == OpCodes.Stfld && ((FieldReference)i.Operand).Name == "workStartedTick"));
            List<Instruction> pi = preInit == null ? new List<Instruction>() : preInit.Body.Instructions.ToList();
            int validate = pi.FindIndex(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "Validate");
            int commit = pi.FindIndex(i => i.OpCode == OpCodes.Stfld && ((FieldReference)i.Operand).Name == "workStartedTick");
            Check("work start: the full validation (decay included) runs at the corpse, THEN the work is committed",
                  validate >= 0 && commit > validate, "validate=" + validate + " commit=" + commit);
            MethodDefinition resValidate = mod.MainModule.GetType("Grandmaster21.JobDriver_Gm21Resuscitate").Methods.First(m => m.Name == "Validate");
            MethodReference call = resValidate.Body.Instructions.Select(i => i.Operand as MethodReference)
                .First(r => r != null && r.Name == "CanResuscitate");
            Check("the job judges decay until the work has begun (committed = workStartedTick >= 0)",
                  call.Parameters.Count == 3 && call.Parameters[1].ParameterType.Name == "Boolean"
                  && resValidate.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldfld && ((FieldReference)i.Operand).Name == "workStartedTick"));
            MethodReference orderCall = mod.MainModule.GetType("Grandmaster21.Gm21MedicineOrders").Methods.Where(m => m.HasBody)
                .SelectMany(m => m.Body.Instructions).Select(i => i.Operand as MethodReference)
                .First(r => r != null && r.Name == "CanResuscitate");
            Check("the order judges decay now (uncommitted)", orderCall.Parameters.Count == 2);
            string[] strings = mod.MainModule.Types.SelectMany(t => t.Methods.Concat(t.NestedTypes.SelectMany(n => n.Methods)))
                .Where(m => m.HasBody && m.DeclaringType.FullName.Contains("Gm21")).SelectMany(m => m.Body.Instructions)
                .Where(i => i.OpCode == OpCodes.Ldstr).Select(i => (string)i.Operand).ToArray();
            Check("the Grandmaster works on the corpse where it lies: no hauling, carrying or bed logic in the Medicine code",
                  !mod.MainModule.GetType("Grandmaster21.JobDriver_Gm21Resuscitate").Methods.Concat(driver.Methods)
                      .Concat(driver.NestedTypes.SelectMany(n => n.Methods)).Where(m => m.HasBody).SelectMany(m => m.Body.Instructions)
                      .Select(i => i.Operand as MethodReference).Where(r => r != null)
                      .Any(r => r.Name == "StartCarryThing" || r.Name == "CarryToCell" || r.Name.Contains("FindBedFor") || r.Name == "PlaceHauledThingInCell"));

            // The shock, as shipped.
            XElement def = XDocument.Load(Path.Combine(root, "Defs/Medicine/Medicine21.xml")).Root.Elements("HediffDef").Single();
            XElement disappears = def.Element("comps").Elements("li").Single(li => (string)li.Attribute("Class") == "HediffCompProperties_Disappears");
            string ticks = Gm21Medicine.ResuscitationShockTicks.ToString();
            Check("shipped shock: a fixed six-hour timer (15000~15000 = Gm21Medicine.ResuscitationShockTicks), shown to the player",
                  Gm21Medicine.ResuscitationShockTicks == 6 * GenDate.TicksPerHour && Gm21Medicine.ResuscitationShockTicks == 15000
                  && disappears.Element("disappearsAfterTicks").Value == ticks + "~" + ticks
                  && disappears.Element("showRemainingTime").Value == "true" && disappears.Element("messageOnDisappear") != null);
            XElement[] caps = def.Element("stages").Elements("li").SelectMany(st => st.Element("capMods").Elements("li")).ToArray();
            float setMax = float.Parse(caps.Length == 1 ? caps[0].Element("setMax").Value : "-1", System.Globalization.CultureInfo.InvariantCulture);
            Check("shipped shock: one stage, one capacity modifier -- Consciousness setMax 0.1 (vanilla psychic coma's mechanism)",
                  def.Element("stages").Elements("li").Count() == 1 && caps.Length == 1
                  && caps[0].Element("capacity").Value == "Consciousness" && setMax == 0.1f);
            Check("shipped shock: not curable by items, not a scenario start, no severity drift, no other effects",
                  def.Element("everCurableByItem").Value == "false" && def.Element("scenarioCanAdd").Value == "false"
                  && def.Element("comps").Elements("li").Count() == 1 && def.Descendants("hediffGivers").Count() == 0);
            Check("shipped shock: no vanilla resurrection sickness, dementia, blindness or psychosis anywhere in Medicine",
                  !File.ReadAllText(Path.Combine(root, "Defs/Medicine/Medicine21.xml")).Contains("ResurrectionSickness")
                  && !strings.Any(x => x.Contains("ResurrectionSickness") || x == "Dementia" || x == "Blindness" || x.Contains("ResurrectionPsychosis")));

            // Why 0.1 incapacitates but cannot kill.
            TypeDefinition caps2 = mod.MainModule.AssemblyResolver.Resolve(new AssemblyNameReference("Assembly-CSharp", null)).MainModule.GetType("Verse.PawnCapacitiesHandler");
            MethodDefinition awake = caps2.Methods.First(m => m.Name == "get_CanBeAwake");
            Check("IL premise: vanilla's awake line is Consciousness >= 0.3, and 0.1 is below it (downed, unconscious)",
                  awake.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldc_R4 && (float)i.Operand == 0.3f) && setMax < 0.3f);
            Check("lethal Consciousness is 'not capable' = level <= minForCapable (0 in vanilla), and 0.1 is above it",
                  new PawnCapacityDef().minForCapable == 0f && setMax > 0f);
        }

        // Vanilla's capacity rule (PawnCapacityUtility.CalculateCapacityLevel), checked on its IL:
        //   level = worker level; if level > 0: level = min(level x factors, lowest setMax); then
        //   max(level, capacity.minValue), rounded to hundredths.
        // So a setMax ceiling only ever LOWERS a living level to the ceiling (0.1), never to 0: it
        // cannot be the cause of death. (Running it live needs ModsConfig, which needs the player.)
        HediffDef shockDef = ShockDef(0.1f);
        using (AssemblyDefinition acs = AssemblyDefinition.ReadAssembly(Path.Combine(Path.GetDirectoryName(Mod.Location), "Assembly-CSharp.dll")))
        {
            MethodDefinition calc = acs.MainModule.GetType("Verse.PawnCapacityUtility").Methods
                .First(m => m.Name == "CalculateCapacityLevel");
            List<Instruction> ci = calc.Body.Instructions.ToList();
            int guard = Enumerable.Range(0, ci.Count - 1).FirstOrDefault(k => ci[k].OpCode == OpCodes.Ldc_R4
                && (float)ci[k].Operand == 0f && ci[k + 1].OpCode.FlowControl == FlowControl.Cond_Branch);
            int setMaxCall = ci.FindIndex(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "EvaluateSetMax");
            int min = ci.FindIndex(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "Min"
                                        && ((MethodReference)i.Operand).DeclaringType.Name == "Mathf");
            bool branch = guard >= 0 && guard + 1 < ci.Count && ci[guard + 1].OpCode.FlowControl == FlowControl.Cond_Branch;
            Check("IL premise: capacity modifiers apply only to a level already above 0, and setMax is a min() ceiling",
                  branch && guard < setMaxCall && setMaxCall < min, "guard=" + guard + " setMax=" + setMaxCall + " min=" + min);
        }
        Pawn cap = MakePawn(0);
        Check("real PawnCapacityModifier.EvaluateSetMax of the shock's modifier is exactly 0.1",
              shockDef.stages[0].capMods[0].EvaluateSetMax(cap) == 0.1f);

        // Expiry, deterministic.
        Pawn q = MakePawn(0);
        HediffWithComps shock = Attach<HediffWithComps>(q, shockDef);
        HediffComp_Disappears timer = shock.TryGetComp<HediffComp_Disappears>();
        timer.SetDuration(Gm21Medicine.ResuscitationShockTicks);
        float adj = 0f;
        for (int i = 0; i < Gm21Medicine.ResuscitationShockTicks - 1; i++) timer.CompPostTick(ref adj);
        bool stillIn = !timer.CompShouldRemove;
        timer.CompPostTick(ref adj);
        Check("the shock lasts exactly six hours, then vanilla's Disappears comp ends it", stillIn && timer.CompShouldRemove);

        using (AssemblyDefinition mod = AssemblyDefinition.ReadAssembly(Mod.Location))
        {
            TypeDefinition res = mod.MainModule.GetType("Grandmaster21.Gm21Resuscitation");
            List<Instruction> apply = res.Methods.First(m => m.Name == "ApplyShock").Body.Instructions.ToList();
            int setDuration = apply.FindIndex(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "SetDuration");
            int wouldDie = apply.FindIndex(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "WouldDieAfterAddingHediff");
            int add = apply.FindIndex(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "AddHediff");
            Check("ApplyShock: exact duration set, vanilla's would-it-kill check, THEN added",
                  setDuration >= 0 && setDuration < wouldDie && wouldDie < add
                  && apply.Any(i => i.OpCode == OpCodes.Ldc_I4 && (int)i.Operand == Gm21Medicine.ResuscitationShockTicks));
            List<Instruction> revive = res.Methods.First(m => m.Name == "TryResuscitate").Body.Instructions.ToList();
            Func<string, int> at = n => revive.FindIndex(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == n);
            Check("revival order: TryResurrect -> restore wounds -> scars -> close wounds -> shock",
                  at("TryResurrect") < at("RestoreInjuries") && at("RestoreInjuries") < at("ApplyScar")
                  && at("ApplyScar") < at("TendRemaining") && at("TendRemaining") < at("ApplyShock"));
        }

        // Save / load with the shock active.
        try
        {
            DefDatabase<HediffDef>.Add(shockDef);
            Pawn r = MakePawn(0);
            HediffWithComps active = Attach<HediffWithComps>(r, shockDef);
            HediffComp_Disappears t = active.TryGetComp<HediffComp_Disappears>();
            t.SetDuration(Gm21Medicine.ResuscitationShockTicks);
            float a2 = 0f;
            for (int i = 0; i < 2655; i++) t.CompPostTick(ref a2);
            Hediff h = active;
            Scribe.saver.InitSaving(path, "root"); Scribe_Deep.Look(ref h, "hediff"); Scribe.saver.FinalizeSaving();
            XElement node = XDocument.Load(path).Root.Element("hediff");
            Check("a shock saves as a plain hediff of the GM21 def with its remaining time (12,345 ticks)",
                  node.Element("def").Value == "GM21_ResuscitationShock" && node.Descendants("ticksToDisappear").Single().Value == "12345",
                  node.ToString());
            logged.Clear();
            try
            {
                Hediff back = null;
                Scribe.loader.InitLoading(path); Scribe_Deep.Look(ref back, "hediff"); Scribe.loader.FinalizeLoading();
                if (LoadBlocked()) throw new InvalidOperationException("ParseHelper unavailable");
                HediffComp_Disappears bt = back.TryGetComp<HediffComp_Disappears>();
                Check("...and reloads with the same remaining time and its consciousness ceiling",
                      back != active && bt != null && bt.ticksToDisappear == 12345 && bt.disappearsAfterTicks == Gm21Medicine.ResuscitationShockTicks
                      && back.CapMods != null && back.CapMods.Single().setMax == 0.1f);
            }
            catch (Exception e) { Scribe.ForceStop(); Blocked("shock load", e); }
        }
        catch (Exception e) { Scribe.ForceStop(); Blocked("shock save", e); }

        // Prepare Save for Uninstall removes it.
        Gm21MedicineDefOf.GM21_ResuscitationShock = shockDef;
        Pawn u = MakePawn(0);
        HediffWithComps uShock = Attach<HediffWithComps>(u, shockDef);
        HediffWithComps other = Attach<HediffWithComps>(u, Disease("TestUnrelated"));
        removedLog.Clear();
        int cleared = (int)Mod.GetType("Grandmaster21.Gm21MedicineUninstall").GetMethod("CleanPawn", Any).Invoke(null, new object[] { u });
        Check("Prepare Save for Uninstall removes the shock (the pawn simply wakes) and nothing else",
              cleared == 1 && removedLog.Count == 1 && removedLog[0] == uShock && !u.health.hediffSet.hediffs.Contains(uShock)
              && u.health.hediffSet.hediffs.Contains(other), "cleared=" + cleared);
        Hediff[] leftover = u.health.hediffSet.hediffs.Where(x => x.def.defName.StartsWith("GM21")).ToArray();
        Check("...so no GM21 HediffDef is left for a mod-less load to trip over", leftover.Length == 0);
    }

    // ------------------------------------------------------------------ 11. persistence

    static void Persistence(string path)
    {
        Console.WriteLine("\n=== 11. Save state (real Scribe) ===");
        Pawn p = MakePawn(0);
        HediffWithComps treated = Attach<HediffWithComps>(p, Disease("TestSaveWound", tendable: true));
        SetFrame(true, 1.30f); treated.Tended(0.5f, 0.7f, 1); SetFrame(false, 0f);
        Gm21Treatment t;
        Gm21TreatmentStore.TryGet(treated, out t);
        t.appliedTick = 424242;

        Scribe.saver.InitSaving(path, "hediff"); ExposeTreatment(treated); Scribe.saver.FinalizeSaving();
        XElement saved = XDocument.Load(path).Root;
        // RimWorld writes floats round-trip exact: 1.3f is written as 1.29999995.
        Check("active treatment writes quality and tick into the hediff's node",
              saved.Element("gm21TreatmentQuality") != null
              && float.Parse(saved.Element("gm21TreatmentQuality").Value, System.Globalization.CultureInfo.InvariantCulture) == 1.30f
              && saved.Element("gm21TreatmentTick").Value == "424242", saved.ToString());

        HediffWithComps loaded = Attach<HediffWithComps>(p, treated.def);
        Tend(loaded).tendTicksLeft = 1;
        logged.Clear();
        try
        {
            Scribe.loader.InitLoading(path); ExposeTreatment(loaded); Scribe.loader.FinalizeLoading();
            if (LoadBlocked()) throw new InvalidOperationException("ParseHelper unavailable (Steamworks.NET missing; see tools/stubs/SteamworksShim.cs)");
            Check("real Scribe load restores the regimen onto the loaded hediff",
                  Gm21TreatmentStore.TryGetActive(loaded, out t) && t.quality == 1.30f && t.appliedTick == 424242);
        }
        catch (Exception e) { Scribe.ForceStop(); Blocked("real Scribe load of the regimen", e); }

        HediffWithComps untreated = Attach<HediffWithComps>(p, treated.def);
        Scribe.saver.InitSaving(path, "hediff"); ExposeTreatment(untreated); Scribe.saver.FinalizeSaving();
        Check("an untreated hediff writes nothing", !XDocument.Load(path).Root.Elements().Any());
        Tend(treated).tendTicksLeft = 0;
        Scribe.saver.InitSaving(path, "hediff"); ExposeTreatment(treated); Scribe.saver.FinalizeSaving();
        Check("a lapsed regimen is not persisted", !XDocument.Load(path).Root.Elements().Any());

        // The whole hediff through Scribe_Deep, with the REAL patched Hediff.ExposeData.
        try
        {
            HediffDef deepDef = Disease("TestDeepSave", tendable: true);
            DefDatabase<HediffDef>.Add(deepDef);
            HediffWithComps deep = Attach<HediffWithComps>(p, deepDef);
            SetFrame(true, 1.60f); deep.Tended(0.5f, 0.7f, 1); SetFrame(false, 0f);
            Hediff asHediff = deep;
            Scribe.saver.InitSaving(path, "root"); Scribe_Deep.Look(ref asHediff, "hediff"); Scribe.saver.FinalizeSaving();
            XElement node = XDocument.Load(path).Root.Element("hediff");
            Check("Scribe_Deep: the element lands INSIDE that hediff's own save node",
                  node.Element("gm21TreatmentQuality") != null
                  && float.Parse(node.Element("gm21TreatmentQuality").Value, System.Globalization.CultureInfo.InvariantCulture) == 1.60f
                  && node.Element("def").Value == "TestDeepSave");

            logged.Clear();
            try
            {
                Hediff back = null;
                Scribe.loader.InitLoading(path);
                Scribe_Deep.Look(ref back, "hediff");
                Scribe.loader.FinalizeLoading();
                if (LoadBlocked()) throw new InvalidOperationException("ParseHelper unavailable (Steamworks.NET missing; see tools/stubs/SteamworksShim.cs)");
                Gm21Treatment bt;
                Check("Scribe_Deep round trip: a NEW hediff object carries the regimen and its vanilla tend",
                      back != null && back != deep && Gm21TreatmentStore.TryGet(back, out bt) && bt.quality == 1.60f
                      && Tend(back) != null && Tend(back).tendQuality == 1.60f && Tend(back).tendTicksLeft > 0);
            }
            catch (Exception e) { Scribe.ForceStop(); Blocked("Scribe_Deep hediff load", e); }
        }
        catch (Exception e) { Scribe.ForceStop(); Blocked("Scribe_Deep hediff save", e); }

        // Medicine mode persistence through the real Pawn.ExposeData postfix body.
        Pawn gm = MakePawn(21);
        Gm21MedicineModeStore.Set(gm, Gm21MedicineMode.Resuscitate);
        Scribe.saver.InitSaving(path, "pawn"); Patch_Pawn_ExposeData_MedicineMode.Postfix(gm); Scribe.saver.FinalizeSaving();
        Check("non-default mode is written", XDocument.Load(path).Root.Element("gm21MedicineMode").Value == "Resuscitate");
        Pawn gm2 = MakePawn(21);
        logged.Clear();
        try
        {
            Scribe.loader.InitLoading(path); Patch_Pawn_ExposeData_MedicineMode.Postfix(gm2); Scribe.loader.FinalizeLoading();
            if (LoadBlocked()) throw new InvalidOperationException("ParseHelper unavailable (Steamworks.NET missing; see tools/stubs/SteamworksShim.cs)");
            Check("mode round-trips", Gm21MedicineModeStore.Get(gm2) == Gm21MedicineMode.Resuscitate);
        }
        catch (Exception e) { Scribe.ForceStop(); Blocked("real Scribe load of the mode", e); }
        Gm21MedicineModeStore.Set(gm, Gm21MedicineMode.Cure);
        Scribe.saver.InitSaving(path, "pawn"); Patch_Pawn_ExposeData_MedicineMode.Postfix(gm); Scribe.saver.FinalizeSaving();
        Check("default mode writes nothing", XDocument.Load(path).Root.Element("gm21MedicineMode") == null);

        // Uninstall cleaner.
        Pawn animal = MakePawn(0);
        HediffWithComps bite = Attach<HediffWithComps>(animal, Disease("TestBite", tendable: true));
        SetFrame(true, 1.0f); bite.Tended(0.5f, 0.7f, 1); SetFrame(false, 0f);
        Gm21MedicineModeStore.Set(animal, Gm21MedicineMode.Reconstruct);
        int cleared = (int)Mod.GetType("Grandmaster21.Gm21MedicineUninstall").GetMethod("CleanPawn", Any).Invoke(null, new object[] { animal });
        Check("uninstall cleaner removes treatments and mode (animals included)",
              cleared == 2 && !Gm21TreatmentStore.TryGet(bite, out t) && Gm21MedicineModeStore.Get(animal) == Gm21MedicineMode.Cure,
              "cleared=" + cleared);
        Scribe.saver.InitSaving(path, "hediff"); ExposeTreatment(bite); Scribe.saver.FinalizeSaving();
        Check("  ...so a cleaned hediff writes no GM21 element", !XDocument.Load(path).Root.Elements().Any());
    }

    // ------------------------------------------------------------------ 12. structure, XML, IL

    static void Structure(string root, string assemblyCSharp)
    {
        Console.WriteLine("\n=== 12. Scope, XML, keys and IL premises ===");
        string medDir = Path.Combine(root, "Source/Medicine");
        string source = string.Join("\n", Directory.GetFiles(medDir, "*.cs").Select(File.ReadAllText).ToArray());
        foreach (string name in new[] { "\"MedicineHerbal\"", "\"MedicineIndustrial\"", "\"MedicineUltratech\"",
                                        "\"FoodPoisoning\"", "\"Brain\"", "\"Arm\"", "\"Leg\"", "\"Heart\"", "\"Shoulder\"" })
            Check("no DefName/body-part string literal " + name + " in Medicine source", !source.Contains(name));
        // Reading a hediff's own TendableNow() (closing a stump after revival) is fine; naming it as a
        // patch target is not.
        Check("no TendableNow patch target in Medicine source",
              !Regex.IsMatch(source, "\"TendableNow\"|nameof\\([^)]*TendableNow\\)"));

        XDocument defs = XDocument.Load(Path.Combine(root, "Defs/Medicine/Medicine21.xml"));
        string[] kinds = defs.Root.Elements().Select(e => e.Name.LocalName).Distinct().ToArray();
        Check("Medicine ships three JobDefs and one HediffDef (the shock) -- no research, items, buildings, recipes, WorkGivers",
              kinds.OrderBy(k => k).SequenceEqual(new[] { "HediffDef", "JobDef" })
              && defs.Root.Elements("JobDef").Count() == 3 && defs.Root.Elements("HediffDef").Count() == 1
              && defs.Root.Element("HediffDef").Element("defName").Value == "GM21_ResuscitationShock", string.Join(",", kinds));
        foreach (XElement job in defs.Root.Elements("JobDef"))
        {
            Type driver = Mod.GetType(job.Element("driverClass").Value);
            Check("JobDef " + job.Element("defName").Value + " -> driver resolves to a JobDriver",
                  driver != null && typeof(Verse.AI.JobDriver).IsAssignableFrom(driver) && !driver.IsAbstract);
        }
        FieldInfo[] defOf = typeof(Gm21MedicineDefOf).GetFields(BindingFlags.Public | BindingFlags.Static);
        Check("every Gm21MedicineDefOf field has a shipped Def of its own type",
              defOf.All(fi => defs.Root.Elements(fi.FieldType.Name).Any(j => j.Element("defName").Value == fi.Name)));

        HashSet<string> keys = new HashSet<string>();
        foreach (string file in Directory.GetFiles(Path.Combine(root, "Languages/English/Keyed"), "*.xml"))
            foreach (XElement e in XDocument.Load(file).Root.Elements()) keys.Add(e.Name.LocalName);
        string[] used = Regex.Matches(source, "\"(GM21_[A-Za-z0-9_]+)\"").Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToArray();
        string[] missingKeys = used.Where(k => !keys.Contains(k)).ToArray();
        Check("every translation key the Medicine code uses is shipped (" + used.Length + " keys)",
              missingKeys.Length == 0, string.Join(",", missingKeys));
        Check("Medicine skill title and achieved tooltip are shipped",
              keys.Contains("GM21_Descriptor_Medicine") && keys.Contains("GM21_Achieved_Medicine"));

        using (AssemblyDefinition asm = AssemblyDefinition.ReadAssembly(assemblyCSharp))
        {
            MethodDefinition compTended = asm.MainModule.GetType("Verse.HediffComp_TendDuration").Methods.First(m => m.Name == "CompTended");
            var ins = compTended.Body.Instructions;
            bool rand = ins.Any(i => i.OpCode == OpCodes.Call && ((MethodReference)i.Operand).DeclaringType.Name == "Rand" && ((MethodReference)i.Operand).Name == "Range");
            bool clamp = ins.Any(i => i.OpCode == OpCodes.Call && ((MethodReference)i.Operand).DeclaringType.Name == "Mathf" && ((MethodReference)i.Operand).Name == "Clamp");
            bool plusMinus = ins.Any(i => i.OpCode == OpCodes.Ldc_R4 && (float)i.Operand == -0.25f) && ins.Any(i => i.OpCode == OpCodes.Ldc_R4 && (float)i.Operand == 0.25f);
            Check("IL premise: CompTended still rolls Rand.Range(-0.25, 0.25) and clamps with Mathf.Clamp", rand && clamp && plusMinus);

            MethodDefinition doTend = asm.MainModule.GetType("RimWorld.TendUtility").Methods.First(m => m.Name == "DoTend");
            Check("IL premise: DoTend passes the medicine's MedicalQualityMax to Hediff.Tended",
                  doTend.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldsfld && ((FieldReference)i.Operand).Name == "MedicalQualityMax")
                  && doTend.Body.Instructions.Any(i => i.OpCode == OpCodes.Callvirt && ((MethodReference)i.Operand).Name == "Tended"));

            MethodDefinition heal = asm.MainModule.GetType("Verse.Pawn_HealthTracker").Methods.First(m => m.Name == "HealthTickInterval");
            Check("IL premise: natural injury healing still goes through Hediff_Injury.Heal inside HealthTickInterval",
                  heal.Body.Instructions.Any(i => (i.OpCode == OpCodes.Callvirt || i.OpCode == OpCodes.Call) && ((MethodReference)i.Operand).Name == "Heal"));
        }
    }

    static bool LoadBlocked()
    {
        return logged.Any(l => l.Contains("ParseHelper") || l.Contains("steamworks"));
    }

    // ------------------------------------------------------------------ 13. vanilla data audit

    static readonly string[] Packs = { "Core", "Royalty", "Ideology", "Biotech", "Anomaly", "Odyssey" };

    /// Loads one Def type from vanilla XML with RimWorld's Name/ParentName inheritance: child
    /// elements override, list children (li) are appended unless Inherit="False".
    static List<KeyValuePair<string, XElement>> LoadDefs(string data, string tag)
    {
        Dictionary<string, XElement> named = new Dictionary<string, XElement>();
        List<KeyValuePair<string, XElement>> concrete = new List<KeyValuePair<string, XElement>>();
        foreach (string pack in Packs)
        {
            string dir = Path.Combine(data, pack, "Defs");
            if (!Directory.Exists(dir)) continue;
            foreach (string file in Directory.GetFiles(dir, "*.xml", SearchOption.AllDirectories))
            {
                XDocument doc;
                try { doc = XDocument.Load(file); } catch { continue; }
                foreach (XElement el in doc.Root.Elements(tag))
                {
                    if (el.Attribute("Name") != null) named[el.Attribute("Name").Value] = el;
                    if ((string)el.Attribute("Abstract") != "True" && (string)el.Attribute("Abstract") != "true")
                        concrete.Add(new KeyValuePair<string, XElement>(pack, el));
                }
            }
        }
        return concrete.Select(kv => new KeyValuePair<string, XElement>(kv.Key, Resolve(kv.Value, named, 0))).ToList();
    }

    static XElement Resolve(XElement el, Dictionary<string, XElement> named, int depth)
    {
        XAttribute parent = el.Attribute("ParentName");
        if (parent == null || !named.ContainsKey(parent.Value) || depth > 20) return el;
        return Merge(Resolve(named[parent.Value], named, depth + 1), el);
    }

    static XElement Merge(XElement parent, XElement child)
    {
        XElement result = new XElement(parent);
        foreach (XElement c in child.Elements())
        {
            XElement existing = result.Element(c.Name);
            bool inherit = (string)c.Attribute("Inherit") != "False";
            bool list = c.Elements().Any() && c.Elements().All(x => x.Name == "li");
            if (existing != null && inherit && list && existing.Elements().All(x => x.Name == "li"))
                foreach (XElement li in c.Elements()) existing.Add(new XElement(li));
            else if (existing != null && inherit && c.Elements().Any() && existing.Elements().Any() && !list)
                existing.ReplaceWith(Merge(existing, c));
            else
            {
                if (existing != null) existing.Remove();
                result.Add(new XElement(c));
            }
        }
        return result;
    }

    static Type VanillaType(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        name = name.Contains(".") ? name.Substring(name.LastIndexOf('.') + 1) : name;
        return typeof(Hediff).Assembly.GetType("Verse." + name) ?? typeof(Hediff).Assembly.GetType("RimWorld." + name);
    }

    static bool Flag(XElement d, string name, bool fallback)
    {
        string v = (string)d.Element(name);
        return v == null ? fallback : v.Trim().ToLowerInvariant() == "true";
    }

    static float Num(XElement d, string name, float fallback)
    {
        string v = (string)d.Element(name);
        return v == null ? fallback : float.Parse(v.Trim(), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// Builds a HediffDef and an instance of its real hediffClass from resolved vanilla XML, with
    /// exactly the fields Gm21CureCandidates reads, so the REAL C# Classify judges vanilla data.
    static Hediff VanillaHediff(string pack, XElement d, Pawn pawn)
    {
        HediffDef def = new HediffDef
        {
            defName = (string)d.Element("defName"),
            isBad = Flag(d, "isBad", true),
            everCurableByItem = Flag(d, "everCurableByItem", true),
            chronic = Flag(d, "chronic", false),
            makesSickThought = Flag(d, "makesSickThought", false),
            countsAsAddedPartOrImplant = Flag(d, "countsAsAddedPartOrImplant", false),
            pregnant = Flag(d, "pregnant", false),
            hediffClass = VanillaType((string)d.Element("hediffClass")) ?? typeof(Hediff),
            comps = new List<HediffCompProperties>()
        };
        if (d.Element("chemicalNeed") != null) def.chemicalNeed = new NeedDef { defName = (string)d.Element("chemicalNeed") };
        ModContentPack mcp = Uninit<ModContentPack>();
        Set(mcp, "packageIdInt", pack == "Core" ? "ludeon.rimworld" : "ludeon.rimworld." + pack.ToLowerInvariant());
        def.modContentPack = mcp;
        XElement comps = d.Element("comps");
        if (comps != null)
            foreach (XElement li in comps.Elements("li"))
            {
                Type t = VanillaType((string)li.Attribute("Class"));
                if (t != null && typeof(HediffCompProperties_Immunizable).IsAssignableFrom(t))
                    def.comps.Add(new HediffCompProperties_Immunizable
                    { immunityPerDaySick = Num(li, "immunityPerDaySick", 0f), immunityPerDayNotSick = Num(li, "immunityPerDayNotSick", 0f) });
                else if (t != null && typeof(HediffCompProperties_TendDuration).IsAssignableFrom(t))
                    def.comps.Add(new HediffCompProperties_TendDuration());
                else
                    def.comps.Add(new HediffCompProperties());
            }

        Hediff h = (Hediff)FormatterServices.GetUninitializedObject(def.hediffClass);
        h.def = def;
        h.pawn = pawn;
        Set(h, "visible", true); // judged as diagnosed: visibility is a runtime property, tested separately
        if (h is HediffWithComps) ((HediffWithComps)h).comps = new List<HediffComp>();
        return h;
    }

    static void VanillaData(string data)
    {
        Console.WriteLine("\n=== 13. Vanilla data audit (real XML, real C# rules) ===");
        if (string.IsNullOrEmpty(data) || !Directory.Exists(Path.Combine(data, "Core", "Defs")))
        {
            Console.WriteLine("NOT RUN  no RimWorld Data directory given (4th argument of tools/verify-medicine.sh)");
            return;
        }

        // Cure: the complete vanilla candidate list, every DLC loaded.
        string[] expected = {
            "Alzheimers", "Animal_Flu", "Animal_Plague", "Asthma", "BadBack", "Blindness", "BloodRot", "Carcinoma",
            "Cataract", "Dementia", "FibrousMechanites", "Flu", "FoodPoisoning", "Frail", "GutWorms", "HearingLoss",
            "HeartArteryBlockage", "InfantIllness", "LungRot", "Malaria", "MuscleParasites", "OrganDecay", "Plague",
            "ScariaInfection", "SensoryMechanites", "SleepingSickness", "ToxicBuildup", "WoundInfection" };
        HediffDef realFoodPoisoning = HediffDefOf.FoodPoisoning;
        Pawn p = MakePawn(0);
        List<string> candidates = new List<string>();
        Dictionary<string, Gm21CureVerdict> verdicts = new Dictionary<string, Gm21CureVerdict>();
        int judged = 0, unbuildable = 0;
        foreach (KeyValuePair<string, XElement> kv in LoadDefs(data, "HediffDef"))
        {
            Hediff h;
            try { h = VanillaHediff(kv.Key, kv.Value, p); }
            catch (Exception) { unbuildable++; continue; }
            if (h.def.defName == "FoodPoisoning") HediffDefOf.FoodPoisoning = h.def; // the DefOf the adapter compares against
            Gm21CureVerdict v;
            try { v = Gm21CureCandidates.Classify(h); }
            catch (Exception e) { Check("Classify never throws on vanilla " + h.def.defName, false, e.GetType().Name); continue; }
            judged++;
            verdicts[h.def.defName] = v;
            if (v == Gm21CureVerdict.Candidate) candidates.Add(h.def.defName);
        }
        HediffDefOf.FoodPoisoning = realFoodPoisoning;
        candidates.Sort();
        string[] extra = candidates.Except(expected).ToArray(), missing = expected.Except(candidates).ToArray();
        Check("judged every vanilla HediffDef with the real Classify (" + judged + " defs, all DLCs)", judged > 300 && unbuildable == 0,
              "unbuildable=" + unbuildable);
        Check("Cure candidates are exactly the audited 28 vanilla illnesses", extra.Length == 0 && missing.Length == 0,
              "extra=" + string.Join(",", extra) + " missing=" + string.Join(",", missing));
        foreach (string[] pair in new[] {
            new[] { "Gunshot", "Injury" }, new[] { "MissingBodyPart", "MissingPart" }, new[] { "AlcoholAddiction", "ChemicalDependency" },
            new[] { "PregnancyLabor", "Reproduction" }, new[] { "BloodLoss", "NoPathologySignal" }, new[] { "Hypothermia", "NoPathologySignal" },
            new[] { "CryptosleepSickness", "NoPathologySignal" }, new[] { "HeartAttack", "CustomClass" }, new[] { "PsychicShock", "NoPathologySignal" },
            new[] { "CrumblingMind", "Supernatural" }, new[] { "Ghoul", "NotCurableByItem" }, new[] { "PsychicSuppression", "NotCurableByItem" } })
        {
            Gm21CureVerdict v;
            Check("vanilla " + pair[0] + " -> " + pair[1], verdicts.TryGetValue(pair[0], out v) && v.ToString() == pair[1],
                  verdicts.ContainsKey(pair[0]) ? v.ToString() : "absent");
        }

        // Medicine tiers from the real vanilla medicine ThingDefs.
        Dictionary<string, float> caps = new Dictionary<string, float>();
        foreach (KeyValuePair<string, XElement> kv in LoadDefs(data, "ThingDef"))
        {
            XElement stats = kv.Value.Element("statBases");
            if (stats == null || stats.Element("MedicalPotency") == null) continue;
            caps[(string)kv.Value.Element("defName")] = Num(stats, "MedicalQualityMax", 1f);
        }
        float[] loaded = Gm21MedicineTiers.DistinctAscending(caps.Values);
        Check("vanilla ships exactly three medicines (all DLCs)", caps.Count == 3, string.Join(",", caps.Keys.ToArray()));
        foreach (KeyValuePair<string, float> kv in caps)
            Console.WriteLine("        " + kv.Key + ": cap " + kv.Value + " -> Grandmaster " + Gm21MedicineTiers.ResolveTarget(kv.Value, loaded));
        float[] targets = caps.Values.Select(c => Gm21MedicineTiers.ResolveTarget(c, loaded)).OrderBy(x => x).ToArray();
        Check("vanilla caps resolve to 100% / 130% / 160%",
              targets.Length == 3 && Near(targets[0], 1.00f) && Near(targets[1], 1.30f) && Near(targets[2], 1.60f),
              string.Join(",", targets.Select(x => x.ToString()).ToArray()));

        // Surgery: every vanilla outcome the Grandmaster path skips is a failure, and the only
        // non-failure outcomes are success and xenogerm coma duration.
        List<string> evaluated = new List<string>();
        int skipped = 0;
        foreach (KeyValuePair<string, XElement> kv in LoadDefs(data, "SurgeryOutcomeEffectDef"))
        {
            XElement outs = kv.Value.Element("outcomes");
            if (outs == null) continue;
            foreach (XElement li in outs.Elements("li"))
            {
                Type t = VanillaType((string)li.Attribute("Class"));
                SurgeryOutcome o = (SurgeryOutcome)Activator.CreateInstance(t);
                o.failure = Flag(li, "failure", false);
                if (Gm21Surgery.IsFailureOutcome(o)) skipped++;
                else evaluated.Add(kv.Value.Element("defName").Value + ":" + t.Name);
            }
        }
        Check("vanilla surgery: the Grandmaster never evaluates a failure/death outcome (" + skipped + " skipped)",
              skipped >= 7 && evaluated.All(e => e.EndsWith(":SurgeryOutcomeSuccess") || e.EndsWith(":SurgeryOutcome_HediffWithDuration")),
              string.Join(",", evaluated.ToArray()));
        Check("vanilla SurgeryOutcomeBase lists success first", evaluated.Any(e => e == "SurgeryOutcomeBase:SurgeryOutcomeSuccess"));

        // Resuscitation: every vanilla flesh body has a consciousness source for the brain check.
        List<string> noBrain = new List<string>();
        Dictionary<string, List<string>> partTags = LoadDefs(data, "BodyPartDef").ToDictionary(
            kv => (string)kv.Value.Element("defName"),
            kv => kv.Value.Element("tags") == null ? new List<string>() : kv.Value.Element("tags").Elements("li").Select(x => x.Value).ToList());
        foreach (KeyValuePair<string, XElement> kv in LoadDefs(data, "BodyDef"))
        {
            bool brain = kv.Value.Descendants("def").Any(x => partTags.ContainsKey(x.Value) && partTags[x.Value].Contains("ConsciousnessSource"));
            if (!brain) noBrain.Add((string)kv.Value.Element("defName"));
        }
        Check("every vanilla BodyDef has consciousness-source anatomy (the 'Brain destroyed' check is meaningful)",
              noBrain.Count == 0, string.Join(",", noBrain.ToArray()));

        // Minimum vital reconstruction: in every vanilla FLESH body, a vital organ hangs directly off
        // the core part (so its parent is never missing), and the only vital parts with sub-parts
        // hold the consciousness source beneath them (their loss is 'Brain destroyed' anyway). So
        // RestorePart on a planned organ restores that organ and nothing else.
        HashSet<string> vitalTags = new HashSet<string> { "BloodPumpingSource", "BreathingSource", "BreathingPathway",
            "BreathingSourceCage", "BloodFiltrationKidney", "BloodFiltrationLiver", "BloodFiltrationSource", "MetabolismSource" };
        HashSet<string> fleshBodies = new HashSet<string>();
        foreach (KeyValuePair<string, XElement> kv in LoadDefs(data, "ThingDef"))
        {
            XElement race = kv.Value.Element("race");
            if (race == null || race.Element("body") == null) continue;
            if ((string)race.Element("fleshType") == "Mechanoid") continue;
            fleshBodies.Add((string)race.Element("body"));
        }
        List<string> unsafeParts = new List<string>();
        int vitalParts = 0;
        foreach (KeyValuePair<string, XElement> kv in LoadDefs(data, "BodyDef"))
        {
            string body = (string)kv.Value.Element("defName");
            if (!fleshBodies.Contains(body)) continue;
            XElement core = kv.Value.Element("corePart");
            foreach (XElement node in core.Descendants("li").Where(x => x.Element("def") != null))
            {
                string def = (string)node.Element("def");
                if (!partTags.ContainsKey(def) || !partTags[def].Any(vitalTags.Contains)) continue;
                vitalParts++;
                bool holdsMind = node.Descendants("def").Any(x => partTags.ContainsKey(x.Value) && partTags[x.Value].Contains("ConsciousnessSource"));
                bool hasChildren = node.Element("parts") != null && node.Element("parts").Elements("li").Any();
                bool underCore = node.Parent != null && node.Parent.Parent == core;
                if (holdsMind) continue;
                if (hasChildren || !underCore) unsafeParts.Add(body + ":" + def);
            }
        }
        Check("vanilla flesh bodies: every vital organ outside the mind's subtree is a leaf directly under the core ("
              + vitalParts + " vital parts, " + fleshBodies.Count + " flesh bodies)",
              vitalParts > 0 && unsafeParts.Count == 0, string.Join(",", unsafeParts.ToArray()));

        // Resuscitation Shock copies a vanilla mechanism, and vanilla's Consciousness has no raised
        // death line: psychic coma is setMax 0.1 on a fixed Disappears timer, and it does not kill.
        XElement coma = LoadDefs(data, "HediffDef").Select(kv => kv.Value).FirstOrDefault(d => (string)d.Element("defName") == "PsychicComa");
        XElement consciousness = LoadDefs(data, "PawnCapacityDef").Select(kv => kv.Value)
            .FirstOrDefault(d => (string)d.Element("defName") == "Consciousness");
        Check("vanilla psychic coma: Consciousness setMax 0.1 on a fixed Disappears timer (the shock's pattern)",
              coma != null && coma.Descendants("capacity").Any(c => c.Value == "Consciousness")
              && coma.Descendants("setMax").Any(m => m.Value == "0.1")
              && coma.Descendants("li").Any(li => (string)li.Attribute("Class") == "HediffCompProperties_Disappears"));
        Check("vanilla Consciousness is lethal but sets no minForCapable (death only at 0)",
              consciousness != null && consciousness.Element("lethalFlesh").Value == "true" && consciousness.Element("minForCapable") == null);

        // What the potency budgets cost in vanilla medicine, one kind at a time.
        Dictionary<string, float> potency = new Dictionary<string, float>();
        foreach (KeyValuePair<string, XElement> kv in LoadDefs(data, "ThingDef"))
        {
            XElement stats = kv.Value.Element("statBases");
            if (stats != null && stats.Element("MedicalPotency") != null)
                potency[(string)kv.Value.Element("defName")] = Num(stats, "MedicalPotency", 0f);
        }
        List<string> costs = new List<string>();
        foreach (Gm21MedicineMode mode in new[] { Gm21MedicineMode.Cure, Gm21MedicineMode.Reconstruct, Gm21MedicineMode.Resuscitate })
            foreach (KeyValuePair<string, float> kv in potency.OrderBy(x => x.Value))
            {
                int[] take = new int[1];
                Gm21MedicineSupplies.Plan(new List<float> { kv.Value }, new List<int> { 99 }, Gm21MedicineSupplies.BudgetFor(mode), take);
                costs.Add(mode + ":" + kv.Key + "=" + take[0]);
                Console.WriteLine("        " + mode + " (" + Gm21MedicineSupplies.BudgetFor(mode) + ") with " + kv.Key
                                  + " (potency " + kv.Value + "): " + take[0] + " unit(s)");
            }
        string[] expectedCosts =
        {
            "Cure:MedicineHerbal=2", "Cure:MedicineIndustrial=1", "Cure:MedicineUltratech=1",
            "Reconstruct:MedicineHerbal=4", "Reconstruct:MedicineIndustrial=2", "Reconstruct:MedicineUltratech=2",
            "Resuscitate:MedicineHerbal=5", "Resuscitate:MedicineIndustrial=3", "Resuscitate:MedicineUltratech=2"
        };
        Check("vanilla medicine per intervention (herbal / industrial / glitterworld): Cure 2/1/1, Reconstruct 4/2/2, Resuscitate 5/3/2",
              expectedCosts.All(costs.Contains), string.Join(" ", costs.ToArray()));

        // Section 21's fixture is vanilla's own "remove part" operation, which is what the Operations
        // tab offers for a prosthetic or bionic ("Remove prosthetic arm" is its added-part label).
        List<XElement> recipes = LoadDefs(data, "RecipeDef").Select(kv => kv.Value).ToList();
        XElement removeBodyPart = recipes.FirstOrDefault(d => (string)d.Element("defName") == "RemoveBodyPart");
        Check("vanilla RemoveBodyPart: Recipe_RemoveBodyPart, workSkill Medicine, a surgery outcome effect (section 21's fixture)",
              removeBodyPart != null && (string)removeBodyPart.Element("workerClass") == "Recipe_RemoveBodyPart"
              && (string)removeBodyPart.Element("workSkill") == "Medicine" && removeBodyPart.Element("surgeryOutcomeEffect") != null);
        Dictionary<string, int> bySkill = recipes.Where(d => d.Element("workSkill") != null)
            .GroupBy(d => (string)d.Element("workSkill")).ToDictionary(g => g.Key, g => g.Count());
        Console.WriteLine("        vanilla RecipeDefs with a work skill (recipeMaker-generated recipes not counted): "
                          + string.Join(", ", bySkill.OrderBy(kv => kv.Key).Select(kv => kv.Key + " " + kv.Value).ToArray()));
        Check("vanilla recipes with a work skill span more than Medicine (the ceiling bug was never medical-only)",
              bySkill.ContainsKey("Medicine") && bySkill.Keys.Count(k => k != "Medicine") >= 2);
    }

    // ------------------------------------------------------------------ 21. vanilla bill skill ceiling

    // Test-process-only, this section: Bill.PawnAllowedToStartAnew ends with a ModsConfig.BiotechActive
    // read (the mechanitor-only recipe check), and ModsConfig's static constructor reads the mods
    // config file and scans the installed mods -- it cannot run headless, and even patching the
    // getter runs it. So that one call, inside the method under test, is pointed at "Biotech off".
    // The shim is installed BEFORE the unpatched baseline is measured, so "before" and "after"
    // differ by GM21's transpiler alone.
    public static bool BiotechInactive() { return false; }
    static int biotechCallsShimmed;
    public static IEnumerable<CodeInstruction> ShimBiotechActive(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo getter = AccessTools.PropertyGetter(typeof(ModsConfig), "BiotechActive");
        biotechCallsShimmed = 0;
        foreach (CodeInstruction i in instructions)
        {
            if (Equals(i.operand, getter))
            {
                i.operand = AccessTools.Method(typeof(MedicineChecks), "BiotechInactive");
                biotechCallsShimmed++;
            }
            yield return i;
        }
    }
    // Test-process-only: stands in for some OTHER mod that pushes one skill's reported level past 20
    // without GM21 storage.
    static SkillRecord externalRecord;
    static int externalLevel;
    public static void ExternalLevel(SkillRecord __instance, ref int __result) { if (__instance == externalRecord) __result = externalLevel; }

    static SkillRecord Skill(Pawn p, SkillDef def, int levelInt, int aptitude = 0)
    {
        SkillRecord rec = new SkillRecord { def = def, levelInt = levelInt };
        Set(rec, "pawn", p);
        // The lazy caches GetLevel reads; computing them needs work tags, genes, traits and ModsConfig.
        Set(rec, "cachedTotallyDisabled", BoolUnknown.False);
        Set(rec, "cachedPermanentlyDisabled", BoolUnknown.False);
        Set(rec, "aptitudeCached", (int?)aptitude);
        return rec;
    }

    static Pawn Worker(string name, int medicine, int crafting, int medicineAptitude = 0)
    {
        Pawn p = MakePawn(0);
        p.Name = new NameSingle(name);
        p.skills.skills = new List<SkillRecord>
        {
            Skill(p, SkillDefOf.Medicine, medicine, medicineAptitude),
            Skill(p, billCrafting, crafting)
        };
        return p;
    }

    static readonly SkillDef billCrafting = new SkillDef { defName = "TestCrafting", label = "crafting" };

    static T BillFor<T>(RecipeDef recipe, int min, int max) where T : Bill, new()
    {
        T bill = new T();                 // the parameterless constructor: vanilla's own 0..20 default
        bill.recipe = recipe;
        // A 0..20 request keeps the range the constructor made; anything else is the player's edit.
        if (bill.allowedSkillRange.min != min || bill.allowedSkillRange.max != max)
            bill.allowedSkillRange = new IntRange(min, max);
        Bill_Medical medical = bill as Bill_Medical;
        if (medical != null && billPatient != null)
        {
            // On the patient's own bill stack, targeting the prosthetic -- what the Operations tab builds.
            billPatient.health.surgeryBills.AddBill(medical);
            medical.Part = billPatientProsthetic;
        }
        return bill;
    }
    static Pawn billPatient;
    static BodyPartRecord billPatientProsthetic;

    static string Attempt(Bill bill, Pawn pawn)
    {
        IntRange before = bill.allowedSkillRange;
        Verse.AI.JobFailReason.Clear();
        bool ok = bill.PawnAllowedToStartAnew(pawn);
        string reason = Verse.AI.JobFailReason.Reason;
        lastBillLabel = Verse.AI.JobFailReason.CustomJobString;
        if (bill.allowedSkillRange.min != before.min || bill.allowedSkillRange.max != before.max) billMutated = true;
        return ok ? "allowed" : "rejected:" + (reason ?? "(no reason)");
    }
    static bool billMutated;
    static string lastBillLabel;

    static void BillSkillCeiling()
    {
        Console.WriteLine("\n=== 21. Vanilla bill skill ceiling (real Bill.PawnAllowedToStartAnew, core GM21 compatibility) ===");
        Harmony env = new Harmony("gm21.medicine.test-environment.bills");
        // Bill_Production's field initialisers read these DefOfs; bind them as the game's loader would.
        FieldInfo binding = typeof(DefOfHelper).GetField("bindingNow", Any);
        binding.SetValue(null, true);
        try
        {
            foreach (Type t in new[] { typeof(BillRepeatModeDefOf), typeof(BillStoreModeDefOf) })
                System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(t.TypeHandle);
        }
        finally { binding.SetValue(null, false); }
        MethodInfo target = AccessTools.Method(typeof(Bill), "PawnAllowedToStartAnew", new[] { typeof(Pawn) });
        env.Patch(target, transpiler: Stub("ShimBiotechActive"));
        Check("test environment: the one ModsConfig.BiotechActive read in the method answers \"off\"", biotechCallsShimmed == 1,
              biotechCallsShimmed + " call(s)");
        // GM21's real reported level (SkillRecord.GetLevel ceiling 21 for stored Grandmasters), which
        // is what made the bug: the shipped postfix, installed exactly as PatchAll installs it.
        Harmony gm21 = new Harmony("ared.grandmaster21");
        gm21.CreateClassProcessor(typeof(Patch_SkillRecord_GetLevel)).Patch();
        env.Patch(AccessTools.Method(typeof(SkillRecord), "GetLevel"), postfix: Stub("ExternalLevel"));

        MethodInfo transpiler = AccessTools.Method(typeof(Patch_BillSkillCeiling), "Transpiler");

        // Vanilla's own default, from the real constructor, before anything is patched.
        Check("vanilla: a new Bill_Medical's allowedSkillRange is 0..20",
              new Bill_Medical().allowedSkillRange.min == 0 && new Bill_Medical().allowedSkillRange.max == 20);

        // "Remove artificial part": a real Bill_Medical on a Medicine recipe whose worker is vanilla's
        // Recipe_RemoveBodyPart (a Recipe_Surgery). A Crafting workbench bill is the generic case.
        SurgeryOutcomeSuccess success = new SurgeryOutcomeSuccess();
        SurgeryOutcome_Failure minor = new SurgeryOutcome_Failure { chance = 1f, failure = true };
        SurgeryOutcome_Death death = new SurgeryOutcome_Death { failure = true };
        RecipeDef removePart = new RecipeDef
        {
            defName = "TestRemoveBodyPart", label = "remove artificial part", workSkill = SkillDefOf.Medicine,
            workerClass = typeof(Recipe_RemoveBodyPart),
            surgeryOutcomeEffect = new SurgeryOutcomeEffectDef
            {
                defName = "TestSurgeryOutcomes",
                comps = new List<SurgeryOutcomeComp> { new SurgeryOutcomeComp_ClampToRange { range = new FloatRange(0f, 0f) } },
                outcomes = new List<SurgeryOutcome> { minor, death, success }
            }
        };
        RecipeDef workbench = new RecipeDef { defName = "TestMakeSomething", label = "make something", workSkill = billCrafting };
        Body patientBody = MakeBody();
        billPatient = MakePawn(0, patientBody);
        billPatient.health.surgeryBills = new BillStack(billPatient);
        billPatientProsthetic = patientBody.lArm;
        Attach<Hediff_AddedPart>(billPatient, new HediffDef
        {
            defName = "TestProstheticArm", label = "prosthetic arm", hediffClass = typeof(Hediff_AddedPart), isBad = false,
            countsAsAddedPartOrImplant = true
        }, billPatientProsthetic);
        quietMissingLanguage = true;
        RecipeDef unskilled = new RecipeDef { defName = "TestUnskilled", label = "unskilled work" };

        Pawn gm = Worker("MedicineGM", 21, 8);
        Pawn twenty = Worker("Medicine20", 20, 8);
        Pawn craftGm = Worker("CraftingGM", 12, 21);
        Pawn novice = Worker("Novice", 5, 5);
        Pawn gmBadGene = Worker("MedicineGMAptitudeMinus6", 21, 8, -6);   // reports Medicine 15
        Pawn external = Worker("ExternalMedicine21", 20, 8);              // stored 20, another mod reports 21
        Pawn externalGm = Worker("GMExternal22", 21, 8);                  // stored GM, another mod reports 22
        Pawn other = Worker("SomeoneElse", 21, 21);

        var cases = new List<KeyValuePair<string, Func<string>>>();
        Action<string, Func<string>> add = (n, f) => cases.Add(new KeyValuePair<string, Func<string>>(n, f));
        add("GM21 Medicine 21, Bill_Medical remove-artificial-part 0..20", () => Attempt(BillFor<Bill_Medical>(removePart, 0, 20), gm));
        add("Medicine 20, 0..20", () => Attempt(BillFor<Bill_Medical>(removePart, 0, 20), twenty));
        add("GM21 Medicine 21, 0..15", () => Attempt(BillFor<Bill_Medical>(removePart, 0, 15), gm));
        add("GM21 Medicine 21, 5..10", () => Attempt(BillFor<Bill_Medical>(removePart, 5, 10), gm));
        add("GM21 Medicine 21, 20..20", () => Attempt(BillFor<Bill_Medical>(removePart, 20, 20), gm));
        add("Medicine 20, 0..15", () => Attempt(BillFor<Bill_Medical>(removePart, 0, 15), twenty));
        add("Medicine 5, 10..20 (below minimum)", () => Attempt(BillFor<Bill_Medical>(removePart, 10, 20), novice));
        add("GM21 reporting 15 (aptitude -6), 18..20 (below minimum)", () => Attempt(BillFor<Bill_Medical>(removePart, 18, 20), gmBadGene));
        add("GM21 Crafting 21, Bill_Production 0..20", () => Attempt(BillFor<Bill_Production>(workbench, 0, 20), craftGm));
        add("GM21 Crafting 21, Bill_Production 0..15", () => Attempt(BillFor<Bill_Production>(workbench, 0, 15), craftGm));
        add("Crafting GM (Medicine 12) on the Medicine bill 0..10", () => Attempt(BillFor<Bill_Medical>(removePart, 0, 10), craftGm));
        add("Medicine GM (Crafting 8) on the Crafting bill 0..5", () => Attempt(BillFor<Bill_Production>(workbench, 0, 5), gm));
        add("no work skill, GM21 pawn", () => Attempt(BillFor<Bill_Production>(unskilled, 0, 20), gm));
        add("external non-GM Medicine 21 (stored 20), 0..20", () =>
        {
            externalRecord = external.skills.GetSkill(SkillDefOf.Medicine); externalLevel = 21;
            try { return Attempt(BillFor<Bill_Medical>(removePart, 0, 20), external); } finally { externalRecord = null; }
        });
        add("GM21 that another mod reports as 22, 0..20", () =>
        {
            externalRecord = externalGm.skills.GetSkill(SkillDefOf.Medicine); externalLevel = 22;
            try { return Attempt(BillFor<Bill_Medical>(removePart, 0, 20), externalGm); } finally { externalRecord = null; }
        });
        add("GM21, bill restricted to another pawn", () =>
        {
            Bill_Medical b = BillFor<Bill_Medical>(removePart, 0, 20);
            Set(b, "pawnRestriction", other);
            return Attempt(b, gm);
        });
        add("GM21, slaves-only bill (GM is not a slave)", () =>
        {
            Bill_Medical b = BillFor<Bill_Medical>(removePart, 0, 20);
            Set(b, "slavesOnly", true);
            return Attempt(b, gm);
        });

        // Every ordinary level against every range the dialog can produce a sample of.
        IntRange[] ranges = { new IntRange(0, 20), new IntRange(0, 15), new IntRange(5, 10), new IntRange(10, 20), new IntRange(20, 20), new IntRange(0, 0) };
        Func<string> ordinary = () =>
        {
            List<string> r = new List<string>();
            for (int level = 0; level <= 20; level++)
                foreach (IntRange range in ranges)
                {
                    Pawn p = Worker("Ordinary" + level, level, level);
                    r.Add(level + "@" + range.min + ".." + range.max + "=" + Attempt(BillFor<Bill_Medical>(removePart, range.min, range.max), p)
                          + "|" + Attempt(BillFor<Bill_Production>(workbench, range.min, range.max), p));
                }
            return string.Join(";", r.ToArray());
        };

        // ---- before the fix: the runtime bug, reproduced on the real method
        Check("before Apply: the bridge reports not applied", !Patch_BillSkillCeiling.Applied);
        Dictionary<string, string> before = new Dictionary<string, string>();
        try
        {
            foreach (var c in cases) before[c.Key] = c.Value();
            before["ordinary"] = ordinary();
        }
        catch (Exception e) { Blocked("vanilla Bill.PawnAllowedToStartAnew headless", e); return; }
        cases[0].Value();
        string removalLabel = lastBillLabel;
        Console.WriteLine("        vanilla (unpatched): GM21 on \"" + removalLabel + "\" -> " + before[cases[0].Key]);
        Check("the Bill_Medical is vanilla's remove-artificial-part bill: its label comes from Recipe_RemoveBodyPart's "
              + "added-part branch (\"RemovePart\" + the prosthetic)", removalLabel != null && removalLabel.StartsWith("RemovePart"),
              removalLabel ?? "null");
        Check("BUG REPRODUCED (unpatched vanilla): GM21 Medicine 21 refused a 0..20 Bill_Medical, \"Above allowed skill 20\"",
              before[cases[0].Key].StartsWith("rejected:") && before[cases[0].Key].Contains("AboveAllowedSkill"),
              before[cases[0].Key]);
        Check("  ...and the Crafting Grandmaster refused a 0..20 Bill_Production the same way (not medical-only)",
              before["GM21 Crafting 21, Bill_Production 0..20"].Contains("AboveAllowedSkill"));

        // ---- the fix, exactly as Gm21Startup applies it
        Patch_BillSkillCeiling.Apply(gm21);
        Patches info = Harmony.GetPatchInfo(target);
        Check("Patch_BillSkillCeiling applied (re-run by Harmony after the environment's shim, still exactly one site)",
              Patch_BillSkillCeiling.Applied && biotechCallsShimmed == 1);
        Check("  one transpiler on Bill.PawnAllowedToStartAnew(Pawn) owned by GM21, no prefix or postfix",
              Patch_BillSkillCeiling.Applied && info != null && info.Transpilers.Count(t => t.owner == "ared.grandmaster21") == 1
              && info.Prefixes.All(t => t.owner != "ared.grandmaster21") && info.Postfixes.All(t => t.owner != "ared.grandmaster21"));
        Check("no subclass override is patched (Bill_Medical / Bill_Production reach it through base)",
              Harmony.GetPatchInfo(AccessTools.Method(typeof(Bill_Medical), "PawnAllowedToStartAnew")) == null);

        Dictionary<string, string> after = new Dictionary<string, string>();
        foreach (var c in cases) after[c.Key] = c.Value();
        after["ordinary"] = ordinary();
        foreach (var c in cases) Console.WriteLine("        " + c.Key + ": " + before[c.Key] + " -> " + after[c.Key]);

        Func<string, string> a = k => after[k];
        Check("FIXED: GM21 Medicine 21 + real Bill_Medical (Recipe_RemoveBodyPart) 0..20 -> allowed",
              a("GM21 Medicine 21, Bill_Medical remove-artificial-part 0..20") == "allowed");
        Check("generic: GM21 Crafting 21 + Bill_Production 0..20 -> allowed", a("GM21 Crafting 21, Bill_Production 0..20") == "allowed");
        Check("skill 20, max 20 -> allowed (unchanged)", a("Medicine 20, 0..20") == "allowed" && before["Medicine 20, 0..20"] == "allowed");
        Check("intentional cap: GM21 on 0..15 -> rejected, \"Above allowed skill 15\"",
              a("GM21 Medicine 21, 0..15").Contains("AboveAllowedSkill") && a("GM21 Medicine 21, 0..15") == before["GM21 Medicine 21, 0..15"]);
        Check("intentional cap: GM21 on 5..10 -> rejected", a("GM21 Medicine 21, 5..10").Contains("AboveAllowedSkill"));
        Check("intentional cap: Crafting GM21 on a 0..15 production bill -> rejected",
              a("GM21 Crafting 21, Bill_Production 0..15").Contains("AboveAllowedSkill"));
        Check("GM21 on 20..20 -> allowed (the minimum is met; the max is vanilla's own)", a("GM21 Medicine 21, 20..20") == "allowed");
        Check("minimum: Medicine 5 on 10..20 -> rejected \"Under allowed skill\", as vanilla",
              a("Medicine 5, 10..20 (below minimum)").Contains("UnderAllowedSkill")
              && a("Medicine 5, 10..20 (below minimum)") == before["Medicine 5, 10..20 (below minimum)"]);
        Check("minimum: a Grandmaster whose aptitude reports 15 is still refused a 18..20 bill",
              a("GM21 reporting 15 (aptitude -6), 18..20 (below minimum)").Contains("UnderAllowedSkill"));
        Check("exact work skill: a Crafting GM on a Medicine bill and a Medicine GM on a Crafting bill are pure vanilla",
              a("Crafting GM (Medicine 12) on the Medicine bill 0..10") == before["Crafting GM (Medicine 12) on the Medicine bill 0..10"]
              && a("Crafting GM (Medicine 12) on the Medicine bill 0..10").Contains("AboveAllowedSkill")
              && a("Medicine GM (Crafting 8) on the Crafting bill 0..5") == before["Medicine GM (Crafting 8) on the Crafting bill 0..5"]
              && a("Medicine GM (Crafting 8) on the Crafting bill 0..5").Contains("AboveAllowedSkill"));
        Check("no work skill: unchanged", a("no work skill, GM21 pawn") == "allowed" && before["no work skill, GM21 pawn"] == "allowed");
        Check("external non-GM skill 21 (stored 20, raised by another mod) -> still rejected, vanilla",
              a("external non-GM Medicine 21 (stored 20), 0..20").Contains("AboveAllowedSkill")
              && a("external non-GM Medicine 21 (stored 20), 0..20") == before["external non-GM Medicine 21 (stored 20), 0..20"]);
        Check("the bound only becomes 21: a Grandmaster some other mod reports as 22 -> still rejected",
              a("GM21 that another mod reports as 22, 0..20").Contains("AboveAllowedSkill"));
        Check("pawn restriction still refuses the Grandmaster", a("GM21, bill restricted to another pawn") == "rejected:(no reason)");
        Check("slaves-only still refuses the Grandmaster", a("GM21, slaves-only bill (GM is not a slave)") == "rejected:(no reason)");
        Check("ordinary pawns 0-20 x 6 ranges x Bill_Medical/Bill_Production: identical results and reasons, before and after",
              after["ordinary"] == before["ordinary"], before["ordinary"] == after["ordinary"] ? (21 * ranges.Length * 2) + " attempts" : "DIFFERENT");
        string[] changed = { cases[0].Key, "GM21 Crafting 21, Bill_Production 0..20", "GM21 Medicine 21, 20..20" };
        Check("only the three Grandmaster-in-the-recipe-skill-at-max-20 cases changed; every other case is identical to vanilla",
              cases.Where(c => !changed.Contains(c.Key)).All(c => before[c.Key] == after[c.Key])
              && changed.All(k => before[k].Contains("AboveAllowedSkill") && after[k] == "allowed"));
        Check("Bill state never mutated: allowedSkillRange identical after every one of the attempts", !billMutated);

        // The rule itself, pure.
        Bill_Medical probe = BillFor<Bill_Medical>(removePart, 0, 20);
        Check("UpperBoundFor: GM21 in the recipe skill at max 20 -> 21", Patch_BillSkillCeiling.UpperBoundFor(20, probe, gm) == 21);
        Check("UpperBoundFor: max 15 / 19 / 21 are returned untouched",
              Patch_BillSkillCeiling.UpperBoundFor(15, probe, gm) == 15 && Patch_BillSkillCeiling.UpperBoundFor(19, probe, gm) == 19
              && Patch_BillSkillCeiling.UpperBoundFor(21, probe, gm) == 21);
        Check("UpperBoundFor: Medicine 20 / external / Crafting-only GM -> 20",
              Patch_BillSkillCeiling.UpperBoundFor(20, probe, twenty) == 20 && Patch_BillSkillCeiling.UpperBoundFor(20, probe, external) == 20
              && Patch_BillSkillCeiling.UpperBoundFor(20, probe, craftGm) == 20);
        Check("UpperBoundFor: null bill / null recipe / no work skill / null pawn -> max",
              Patch_BillSkillCeiling.UpperBoundFor(20, null, gm) == 20 && Patch_BillSkillCeiling.UpperBoundFor(20, new Bill_Medical(), gm) == 20
              && Patch_BillSkillCeiling.UpperBoundFor(20, BillFor<Bill_Production>(unskilled, 0, 20), gm) == 20
              && Patch_BillSkillCeiling.UpperBoundFor(20, probe, null) == 20);
        Check("UpperBoundFor never writes the bill", probe.allowedSkillRange.min == 0 && probe.allowedSkillRange.max == 20);

        // The rewrite, instruction for instruction: vanilla's IL plus exactly three inserted
        // instructions, right after the one ldfld IntRange::max that feeds the comparison.
        List<CodeInstruction> original = PatchProcessor.GetOriginalInstructions(target);
        List<CodeInstruction> rewritten = ((IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { original.Select(i => i.Clone()) })).ToList();
        int at = rewritten.FindIndex(i => i.opcode == System.Reflection.Emit.OpCodes.Call && Equals(i.operand, AccessTools.Method(typeof(Patch_BillSkillCeiling), "UpperBoundFor")));
        bool sameElsewhere = rewritten.Count == original.Count + 3 && at >= 3;
        for (int i = 0; sameElsewhere && i < original.Count; i++)
        {
            CodeInstruction o = original[i], r = rewritten[i < at - 2 ? i : i + 3];
            sameElsewhere = o.opcode == r.opcode && Equals(o.operand, r.operand);
        }
        Check("transpiler: vanilla IL + exactly [ldarg.0, ldarg.1, call UpperBoundFor], after the comparison's ldfld max",
              sameElsewhere && rewritten[at - 2].opcode == System.Reflection.Emit.OpCodes.Ldarg_0
              && rewritten[at - 1].opcode == System.Reflection.Emit.OpCodes.Ldarg_1
              && rewritten[at - 3].opcode == System.Reflection.Emit.OpCodes.Ldfld
              && rewritten[at + 1].opcode.FlowControl == System.Reflection.Emit.FlowControl.Cond_Branch,
              "original=" + original.Count + " rewritten=" + rewritten.Count + " at=" + at);
        Check("  ...so the min check, pawn/slave/mech restrictions and the mechanitor check are the original instructions",
              sameElsewhere);
        FieldInfo maxField = AccessTools.Field(typeof(IntRange), "max");
        List<int> maxLoads = Enumerable.Range(0, rewritten.Count)
            .Where(i => rewritten[i].opcode == System.Reflection.Emit.OpCodes.Ldfld && Equals(rewritten[i].operand, maxField)).ToList();
        Check("  ...and the \"Above allowed skill {max}\" message still boxes the bill's REAL max (second ldfld max untouched)",
              maxLoads.Count == 2 && maxLoads[0] == at - 3 && rewritten[maxLoads[1] + 1].operand is MethodInfo
              && ((MethodInfo)rewritten[maxLoads[1] + 1].operand).Name == "op_Implicit"
              && ((MethodInfo)rewritten[maxLoads[1] + 1].operand).DeclaringType == typeof(NamedArgument),
              maxLoads.Count == 2 ? rewritten[maxLoads[1] + 1].ToString() : maxLoads.Count + " loads");

        // Fail-safe: a method with no such comparison, or two, is returned unchanged.
        List<CodeInstruction> none = original.Where(i => !(i.opcode == System.Reflection.Emit.OpCodes.Ldfld
                                                            && Equals(i.operand, AccessTools.Field(typeof(IntRange), "max")))).Select(i => i.Clone()).ToList();
        List<CodeInstruction> noneOut = ((IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { none })).ToList();
        bool noneApplied = Patch_BillSkillCeiling.Applied;
        List<CodeInstruction> two = original.Select(i => i.Clone()).Concat(original.Select(i => i.Clone())).ToList();
        List<CodeInstruction> twoOut = ((IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { two })).ToList();
        bool twoApplied = Patch_BillSkillCeiling.Applied;
        Check("fail-safe: zero or two upper-bound comparisons -> IL returned unchanged, bridge reported off",
              noneOut.Count == none.Count && twoOut.Count == two.Count && !noneApplied && !twoApplied);
        transpiler.Invoke(null, new object[] { original.Select(i => i.Clone()) });   // restore the flag for the live method
        Check("  (flag restored by re-running on the real IL)", Patch_BillSkillCeiling.Applied);

        // After eligibility: the job the bill starts is vanilla's Recipe_RemoveBodyPart, whose failure
        // roll is Recipe_Surgery.CheckSurgeryFail -> SurgeryOutcomeEffectDef.GetOutcome -- the method
        // Grandmaster Surgery patches. Nothing about that chain was touched.
        Check("Perfect Surgery still installed: SurgeryOutcomeEffectDef.GetOutcome carries GM21's prefix",
              Harmony.GetPatchInfo(AccessTools.Method(typeof(SurgeryOutcomeEffectDef), "GetOutcome")) != null
              && Harmony.GetPatchInfo(AccessTools.Method(typeof(SurgeryOutcomeEffectDef), "GetOutcome")).Prefixes.Any(p => p.owner == "ared.grandmaster21"));
        Check("the eligible bill's worker is vanilla's Recipe_RemoveBodyPart, a Recipe_Surgery",
              removePart.Worker is Recipe_RemoveBodyPart && removePart.Worker is Recipe_Surgery);
        List<CodeInstruction> checkFail = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(Recipe_Surgery), "CheckSurgeryFail"));
        List<CodeInstruction> apply = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(Recipe_RemoveBodyPart), "ApplyOnPawn"));
        Check("Recipe_RemoveBodyPart.ApplyOnPawn -> CheckSurgeryFail -> SurgeryOutcomeEffectDef.GetOutcome (real IL)",
              apply.Any(i => i.operand is MethodInfo && ((MethodInfo)i.operand).Name == "CheckSurgeryFail")
              && checkFail.Any(i => i.operand is MethodInfo && ((MethodInfo)i.operand).Name == "GetOutcome"
                                    && ((MethodInfo)i.operand).DeclaringType == typeof(SurgeryOutcomeEffectDef)));
        int ok = 0;
        for (int i = 0; i < 200; i++)
            if (removePart.surgeryOutcomeEffect.GetOutcome(removePart, gm, billPatient, null, billPatientProsthetic, probe) == success) ok++;
        Check("the Grandmaster who passed eligibility gets Perfect Surgery on that same recipe, patient, part and bill (200/200)",
              ok == 200, ok + "/200");
        Check("  ...and Medicine 20 still takes vanilla's failure branch there",
              removePart.surgeryOutcomeEffect.GetOutcome(removePart, twenty, billPatient, null, billPatientProsthetic, probe) == minor);
        quietMissingLanguage = false;
        billPatient = null;
    }

    static int Main(string[] args)
    {
        FieldInfo prefs = typeof(Prefs).GetField("data", Any);
        prefs.SetValue(null, FormatterServices.GetUninitializedObject(prefs.FieldType));
        string root = args.Length > 1 ? args[1] : ".";
        string xml = args.Length > 2 ? args[2] : Path.GetTempFileName();
        string acs = args.Length > 3 ? args[3] : "Assembly-CSharp.dll";
        try
        {
            Harmony env = new Harmony("gm21.medicine.test-environment");
            InstallEnvironment(env);
            Tiers();
            Curve();
            InstallPatches();
            DeterministicTend();
            Treatment();
            Recovery();
            Surgery();
            Cure();
            Reconstruct();
            Resuscitation();
            Persistence(xml);
            Structure(root, acs);
            Propagation();
            InterventionMedicine();
            WorkAndAccess(root);
            VitalAnatomy();
            Trauma();
            DriverSave(xml);
            CommitAndShock(root, xml);
            BillSkillCeiling();
            VanillaData(args.Length > 4 ? args[4] : null);
        }
        catch (Exception e) { Console.WriteLine("FAIL  unhandled: " + e); fail++; }
        Console.WriteLine("\nNOT RUN HERE (needs the Unity player and a loaded game): tending jobs, the gizmo, targeting,"
                          + " intervention jobs (collection, interruption, pathing), revival itself (TryResurrect,"
                          + " hostile lords), whole-game save/reload. See Docs/Medicine21.md.");
        Console.WriteLine("================================");
        Console.WriteLine("PASS: " + pass + "   FAIL: " + fail + "   BLOCKED: " + blocked);
        return fail == 0 ? 0 : 1;
    }
}
