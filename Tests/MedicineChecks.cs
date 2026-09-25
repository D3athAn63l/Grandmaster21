// Medicine 21 headless checks, run against the REAL RimWorld 1.6, Unity and Harmony assemblies.
//
// What makes these more than logic tests: Gm21MedicinePatches.Apply is run for real, so the mod's
// actual Harmony patches are installed on the actual vanilla methods, and then those vanilla
// methods are executed -- HediffComp_TendDuration.CompTended (reached through Hediff.Tended),
// Hediff_Injury.Heal, SurgeryOutcomeEffectDef.GetOutcome, HediffSet.GetMissingPartsCommonAncestors
// and the real Scribe saver/loader.
//
// Test-process-only environment shims (never shipped, never in the mod):
//   * ContentFinder<Texture2D>.Get returns null -- HediffComp_TendDuration's static constructor loads
//     icons through Unity, which only exists inside the player;
//   * Pawn_HealthTracker.Notify_HediffChanged / RemoveHediff are reduced to list bookkeeping -- the
//     real ones re-evaluate a live pawn's capacities and death state, which needs a whole game;
//   * PawnCapacitiesHandler.CapableOf and RaceProperties.IsMechanoid answer from the fixture.
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
    public static readonly List<string> logged = new List<string>();
    public static bool ConsoleLog(string text) { logged.Add(text); Console.WriteLine("        [game log] " + text); return false; }

    static HarmonyMethod Stub(string n) { return new HarmonyMethod(typeof(MedicineChecks).GetMethod(n)); }

    static void InstallEnvironment(Harmony h)
    {
        h.Patch(AccessTools.Method(typeof(ContentFinder<UnityEngine.Texture2D>), "Get"), Stub("NullTexture"));
        h.Patch(AccessTools.Method(typeof(Pawn_HealthTracker), "Notify_HediffChanged"), Stub("SkipNotify"));
        h.Patch(AccessTools.Method(typeof(Pawn_HealthTracker), "RemoveHediff"), Stub("ListOnlyRemove"));
        h.Patch(AccessTools.Method(typeof(PawnCapacitiesHandler), "CapableOf"), Stub("FixtureCapable"));
        h.Patch(AccessTools.PropertyGetter(typeof(RaceProperties), "IsMechanoid"), Stub("NotMechanoid"));
        // Verse.Log bottoms out in Unity's logger; route it to the console so messages are visible.
        foreach (string level in new[] { "Message", "Warning", "Error" })
            h.Patch(AccessTools.Method(typeof(Log), level, new[] { typeof(string) }), Stub("ConsoleLog"));

        // Bind the DefOf classes the mod reads, under the same guard the game's loader uses.
        FieldInfo binding = typeof(DefOfHelper).GetField("bindingNow", Any);
        binding.SetValue(null, true);
        try
        {
            foreach (Type t in new[] { typeof(SkillDefOf), typeof(HediffDefOf), typeof(PawnCapacityDefOf) })
                System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(t.TypeHandle);
        }
        finally { binding.SetValue(null, false); }
        SkillDefOf.Medicine = new SkillDef { defName = "Medicine", label = "medicine" };
        PawnCapacityDefOf.Consciousness = new PawnCapacityDef { defName = "Consciousness" };
        PawnCapacityDefOf.Manipulation = new PawnCapacityDef { defName = "Manipulation" };
        HediffDefOf.FoodPoisoning = Disease("FoodPoisoning");
        HediffDefOf.MissingBodyPart = new HediffDef { defName = "MissingBodyPart", hediffClass = typeof(Hediff_MissingPart) };
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
        Gm21ResuscitationFacts ok = new Gm21ResuscitationFacts { isCorpse = true, available = true, isFlesh = true, rotStage = RotStage.Fresh, ticksSinceDeath = 100 };
        Func<Gm21ResuscitationFacts, Gm21ResuscitationVerdict> D = Gm21Resuscitation.Decide;
        Check("fresh, intact, recent, non-hostile flesh corpse -> viable", D(ok) == Gm21ResuscitationVerdict.Viable);
        Gm21ResuscitationFacts f;
        f = ok; f.isCorpse = false; Check("not a corpse -> rejected", D(f) == Gm21ResuscitationVerdict.NotACorpse);
        f = ok; f.available = false; Check("unavailable/invalid corpse -> rejected", D(f) == Gm21ResuscitationVerdict.Unavailable);
        f = ok; f.isFlesh = false; Check("non-flesh -> rejected", D(f) == Gm21ResuscitationVerdict.NotFlesh);
        f = ok; f.supernatural = true; Check("entity/mutant/unnatural -> rejected", D(f) == Gm21ResuscitationVerdict.Supernatural);
        f = ok; f.hostile = true; Check("hostile -> rejected", D(f) == Gm21ResuscitationVerdict.Hostile);
        f = ok; f.brainDestroyed = true; Check("destroyed consciousness anatomy -> 'Brain destroyed'", D(f) == Gm21ResuscitationVerdict.BrainDestroyed);
        f = ok; f.vitalAnatomyDestroyed = true; Check("destroyed vital anatomy -> rejected", D(f) == Gm21ResuscitationVerdict.VitalAnatomyDestroyed);
        f = ok; f.rotStage = RotStage.Rotting; Check("rotting -> 'deteriorated beyond recovery'", D(f) == Gm21ResuscitationVerdict.Deteriorated);
        f = ok; f.rotStage = RotStage.Dessicated; Check("dessicated -> 'deteriorated beyond recovery'", D(f) == Gm21ResuscitationVerdict.Deteriorated);
        f = ok; f.ticksSinceDeath = Gm21Medicine.ResuscitationWindowTicks; Check("exactly at the window edge -> viable", D(f) == Gm21ResuscitationVerdict.Viable);
        f = ok; f.ticksSinceDeath = Gm21Medicine.ResuscitationWindowTicks + 1; Check("one tick past the window -> 'Too much time has passed'", D(f) == Gm21ResuscitationVerdict.TooLate);
        f = ok; f.ticksSinceDeath = -5; Check("a death in the future is invalid -> rejected", D(f) == Gm21ResuscitationVerdict.TooLate);
        Check("window is four in-game hours (10,000 ticks)", Gm21Medicine.ResuscitationWindowTicks == 10000);
        f = ok; f.brainDestroyed = true; f.rotStage = RotStage.Dessicated; f.ticksSinceDeath = 999999;
        Check("structural rejection is reported before time/rot", D(f) == Gm21ResuscitationVerdict.BrainDestroyed);

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
        Check("no TendableNow patch in Medicine source", !source.Contains("TendableNow"));

        XDocument defs = XDocument.Load(Path.Combine(root, "Defs/Medicine/Medicine21.xml"));
        string[] kinds = defs.Root.Elements().Select(e => e.Name.LocalName).Distinct().ToArray();
        Check("Medicine ships JobDefs only (no research, items, buildings, recipes, WorkGivers)",
              kinds.Length == 1 && kinds[0] == "JobDef", string.Join(",", kinds));
        foreach (XElement job in defs.Root.Elements("JobDef"))
        {
            Type driver = Mod.GetType(job.Element("driverClass").Value);
            Check("JobDef " + job.Element("defName").Value + " -> driver resolves to a JobDriver",
                  driver != null && typeof(Verse.AI.JobDriver).IsAssignableFrom(driver) && !driver.IsAbstract);
        }
        FieldInfo[] defOf = typeof(Gm21MedicineDefOf).GetFields(BindingFlags.Public | BindingFlags.Static);
        Check("every Gm21MedicineDefOf field has a shipped JobDef",
              defOf.All(fi => defs.Root.Elements("JobDef").Any(j => j.Element("defName").Value == fi.Name)));

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
            VanillaData(args.Length > 4 ? args[4] : null);
        }
        catch (Exception e) { Console.WriteLine("FAIL  unhandled: " + e); fail++; }
        Console.WriteLine("\nNOT RUN HERE (needs the Unity player and a loaded game): tending jobs, the gizmo, targeting,"
                          + " intervention jobs, revival, whole-game save/reload. See Docs/Medicine21.md.");
        Console.WriteLine("================================");
        Console.WriteLine("PASS: " + pass + "   FAIL: " + fail + "   BLOCKED: " + blocked);
        return fail == 0 ? 0 : 1;
    }
}
