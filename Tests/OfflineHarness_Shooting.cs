// Offline logic harness for the Shooting Grandmaster (Grandmaster 21 Beta).
//
// Drives the mod's REAL body-part scoring, aim-mode store, accuracy/delay maths and the real
// anatomical-targeting patch body against a stub RimWorld API whose signatures mirror
// Assembly-CSharp. See OfflineHarness.cs for the same caveats: this is NOT RimWorld. It does not
// exercise Harmony, real combat, projectiles, save/load or the gizmo.
using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;
using Grandmaster21;

static class ShootingHarness
{
    static int pass, fail;

    static void Check(string name, bool ok, string detail = "")
    {
        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name + (detail == "" ? "" : "   [" + detail + "]"));
        if (ok) pass++; else fail++;
    }

    // ---- reflection into the mod's internal surface -------------------------------------
    static readonly Assembly Mod = typeof(Gm21).Assembly;
    static readonly Type TBody = Mod.GetType("Grandmaster21.Gm21BodyTargeting");
    static readonly Type TPatches = Mod.GetType("Grandmaster21.Gm21ShootingPatches");

    static BodyPartRecord Lethal(Pawn p) => (BodyPartRecord)TBody
        .GetMethod("ChooseLethalPart", BindingFlags.Static | BindingFlags.NonPublic)
        .Invoke(null, new object[] { p });

    static BodyPartRecord Incap(Pawn p) => (BodyPartRecord)TBody
        .GetMethod("ChooseIncapacitatingPart", BindingFlags.Static | BindingFlags.NonPublic)
        .Invoke(null, new object[] { p });

    static DamageInfo RunTargetingPrefix(Pawn victim, DamageInfo dinfo)
    {
        object[] args = { victim, dinfo };
        TPatches.GetMethod("Prefix_ChooseAimedPart", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, args);
        return (DamageInfo)args[1];
    }

    // ---- anatomy fixtures ----------------------------------------------------------------
    static readonly Dictionary<string, BodyPartTagDef> Tags = new Dictionary<string, BodyPartTagDef>();

    static void RegisterTags()
    {
        foreach (string n in new[] { "ConsciousnessSource", "BreathingPathway", "BloodPumpingSource",
                                     "BreathingSource", "BloodFiltrationSource",
                                     "MovingLimbCore", "MovingLimbSegment", "MovingLimbDigit" })
        {
            BodyPartTagDef t = new BodyPartTagDef { defName = n };
            Tags[n] = t;
            DefDatabase<BodyPartTagDef>.Registry[n] = t;
        }
    }

    static BodyPartRecord Part(string name, float coverage, BodyPartRecord parent, params string[] tags)
    {
        List<BodyPartTagDef> list = new List<BodyPartTagDef>();
        foreach (string t in tags) list.Add(Tags[t]);
        return new BodyPartRecord
        {
            def = new BodyPartDef { defName = name, tags = list },
            parent = parent,
            coverageAbsWithChildren = coverage
        };
    }

    static Pawn MakePawn(params BodyPartRecord[] parts)
    {
        Pawn p = new Pawn { health = new Pawn_HealthTracker() };
        p.health.hediffSet.parts.AddRange(parts);
        return p;
    }

    /// <summary>Torso/head/brain/neck/heart/lung + two legs with feet and a toe.</summary>
    static Pawn Humanlike(out BodyPartRecord brain, out BodyPartRecord head, out BodyPartRecord neck,
                          out BodyPartRecord heart, out BodyPartRecord leftLeg, out BodyPartRecord rightLeg,
                          out BodyPartRecord foot, out BodyPartRecord toe)
    {
        BodyPartRecord torso = Part("Torso", 0.40f, null);
        head = Part("Head", 0.10f, torso);
        brain = Part("Brain", 0.01f, head, "ConsciousnessSource");
        neck = Part("Neck", 0.04f, torso, "BreathingPathway");
        heart = Part("Heart", 0.03f, torso, "BloodPumpingSource");
        BodyPartRecord lung = Part("Lung", 0.05f, torso, "BreathingSource");
        leftLeg = Part("LeftLeg", 0.10f, torso, "MovingLimbCore");
        rightLeg = Part("RightLeg", 0.10f, torso, "MovingLimbCore");
        foot = Part("LeftFoot", 0.03f, leftLeg, "MovingLimbSegment");
        toe = Part("LeftToe", 0.01f, foot, "MovingLimbDigit");
        return MakePawn(torso, head, brain, neck, heart, lung, leftLeg, rightLeg, foot, toe);
    }

    static Pawn ShootingGrandmaster(Gm21AimMode mode)
    {
        Pawn p = new Pawn { health = new Pawn_HealthTracker(), skills = new Pawn_SkillTracker() };
        p.skills.skills.Add(new SkillRecord { def = SkillDefOf.Shooting, levelInt = 21 });
        Gm21AimModeStore.Set(p, mode);
        return p;
    }

    static ThingDef Rifle = new ThingDef { defName = "Rifle", IsRangedWeapon = true };
    static DamageDef Bullet = new DamageDef { defName = "Bullet", harmsHealth = true };

    static DamageInfo Shot(Pawn shooter, Pawn victim)
    {
        return new DamageInfo
        {
            Def = Bullet, Instigator = shooter, Weapon = Rifle, IntendedTarget = victim
        };
    }

    static void Main()
    {
        Gm21Mod.Settings = new Gm21Settings();
        Gm21Mod.Settings.Validate();
        Gm21.LearnPatchApplied = true;
        RegisterTags();
        LoadShippedKeys();

        Console.WriteLine("=== S1. Accuracy compensation curve ===");
        Check("30% -> 99.3%", Math.Abs(Gm21Shooting.CompensateChance(0.30f) - 0.993f) < 0.0005f,
              Gm21Shooting.CompensateChance(0.30f).ToString("P2"));
        Check("60% -> 99.6%", Math.Abs(Gm21Shooting.CompensateChance(0.60f) - 0.996f) < 0.0005f,
              Gm21Shooting.CompensateChance(0.60f).ToString("P2"));
        Check("90% -> 99.9%", Math.Abs(Gm21Shooting.CompensateChance(0.90f) - 0.999f) < 0.0005f,
              Gm21Shooting.CompensateChance(0.90f).ToString("P2"));
        Check("better weapon stays strictly better after compensation",
              Gm21Shooting.CompensateChance(0.90f) > Gm21Shooting.CompensateChance(0.60f)
              && Gm21Shooting.CompensateChance(0.60f) > Gm21Shooting.CompensateChance(0.30f));
        Check("never exceeds 1.0", Gm21Shooting.CompensateChance(1f) == 1f
              && Gm21Shooting.CompensateChance(0.99999f) <= 1f);
        Check("a 0% base is still not a guaranteed hit", Gm21Shooting.CompensateChance(0f) < 1f,
              Gm21Shooting.CompensateChance(0f).ToString("P2"));

        Console.WriteLine("\n=== S2. Warmup / cooldown reduction ===");
        Check("100 ticks -> 1", Gm21Shooting.ReduceDelayTicks(100) == 1, "=" + Gm21Shooting.ReduceDelayTicks(100));
        Check("600 ticks -> 6", Gm21Shooting.ReduceDelayTicks(600) == 6, "=" + Gm21Shooting.ReduceDelayTicks(600));
        Check("60 ticks floors at 1 (never 0)", Gm21Shooting.ReduceDelayTicks(60) == 1);
        Check("1 tick stays 1", Gm21Shooting.ReduceDelayTicks(1) == 1);
        Check("0 ticks stays 0 (nothing to reduce)", Gm21Shooting.ReduceDelayTicks(0) == 0);
        bool neverBelowOne = true;
        for (int t = 1; t <= 5000; t++) if (Gm21Shooting.ReduceDelayTicks(t) < 1) neverBelowOne = false;
        Check("no tick count 1..5000 ever produces a zero/negative stance", neverBelowOne);

        Console.WriteLine("\n=== S3. Aim mode storage ===");
        Pawn a = ShootingGrandmaster(Gm21AimMode.Normal);
        Check("default mode is Normal", Gm21AimModeStore.Get(a) == Gm21AimMode.Normal);
        Gm21AimModeStore.Set(a, Gm21AimMode.Killer);
        Pawn b = ShootingGrandmaster(Gm21AimMode.Downed);
        Check("modes are per-pawn, not global",
              Gm21AimModeStore.Get(a) == Gm21AimMode.Killer && Gm21AimModeStore.Get(b) == Gm21AimMode.Downed);
        Gm21AimModeStore.Clear(a);
        Check("cleared pawn falls back to Normal", Gm21AimModeStore.Get(a) == Gm21AimMode.Normal);
        Check("unknown pawn reads Normal without allocating state",
              Gm21AimModeStore.Get(new Pawn()) == Gm21AimMode.Normal);
        Gm21AimModeStore.Set(b, Gm21AimMode.Normal);
        Check("setting Normal drops the entry (nothing serialised)",
              Gm21AimModeStore.Get(b) == Gm21AimMode.Normal);

        Console.WriteLine("\n=== S4. Killer targeting ===");
        BodyPartRecord brain, head, neck, heart, lleg, rleg, foot, toe;
        Pawn human = Humanlike(out brain, out head, out neck, out heart, out lleg, out rleg, out foot, out toe);
        Check("humanlike -> brain", Lethal(human) == brain, "got=" + Name(Lethal(human)));

        // Brain destroyed: the structural "head" (parent of the brain) is gone too, so the next
        // lethal tier must be chosen rather than crashing.
        Pawn noBrain = MakePawn(Part("Torso", 0.4f, null), neck, heart);
        Check("no brain -> falls to the breathing pathway (neck)", Lethal(noBrain) == neck,
              "got=" + Name(Lethal(noBrain)));
        Pawn heartOnly = MakePawn(Part("Torso", 0.4f, null), heart);
        Check("no brain or neck -> heart", Lethal(heartOnly) == heart, "got=" + Name(Lethal(heartOnly)));

        Pawn headless = MakePawn(Part("Chassis", 0.6f, null), Part("Tread", 0.2f, null, "MovingLimbCore"));
        Check("headless mechanoid with no vitals -> null (no crash, vanilla resolves)",
              Lethal(headless) == null, "got=" + Name(Lethal(headless)));
        Pawn empty = MakePawn();
        Check("creature with no parts at all -> null", Lethal(empty) == null);
        Check("pawn with no health tracker -> null", Lethal(new Pawn()) == null);

        BodyPartRecord bigHeart = Part("Heart", 0.09f, null, "BloodPumpingSource");
        BodyPartRecord smallHeart = Part("Heart2", 0.02f, null, "BloodPumpingSource");
        Pawn twoHearts = MakePawn(Part("Torso", 0.4f, null), smallHeart, bigHeart);
        Check("two hearts -> the larger/easier one", Lethal(twoHearts) == bigHeart,
              "got=" + Name(Lethal(twoHearts)));

        Console.WriteLine("\n=== S5. Downed targeting ===");
        Pawn human2 = Humanlike(out brain, out head, out neck, out heart, out lleg, out rleg, out foot, out toe);
        BodyPartRecord picked = Incap(human2);
        Check("humanlike -> a moving-limb core (leg)", picked == lleg || picked == rleg,
              "got=" + Name(picked));
        Check("never a vital organ", picked != brain && picked != heart && picked != neck);

        // One leg ruined: the Grandmaster should switch to the other, not keep shooting the wreck.
        lleg.healthStub = 0f;
        Check("ruined leg is skipped; the functional leg is chosen", Incap(human2) == rleg,
              "got=" + Name(Incap(human2)));
        rleg.healthStub = 0f;
        Check("both legs ruined -> drops to the next mobility tier (foot)", Incap(human2) == foot,
              "got=" + Name(Incap(human2)));
        foot.healthStub = 0f;
        Check("then the digit", Incap(human2) == toe, "got=" + Name(Incap(human2)));
        toe.healthStub = 0f;
        Check("no mobility anatomy left -> null (vanilla resolves), never a vital",
              Incap(human2) == null, "got=" + Name(Incap(human2)));

        BodyPartRecord[] legs = new BodyPartRecord[6];
        List<BodyPartRecord> insect = new List<BodyPartRecord> { Part("Thorax", 0.5f, null) };
        for (int i = 0; i < 6; i++) { legs[i] = Part("Leg" + i, 0.05f, null, "MovingLimbCore"); insect.Add(legs[i]); }
        insect.Add(Part("InsectHeart", 0.02f, null, "BloodPumpingSource"));
        Pawn bug = MakePawn(insect.ToArray());
        Check("six-legged creature -> a leg, not the heart",
              Array.IndexOf(legs, Incap(bug)) >= 0, "got=" + Name(Incap(bug)));
        Check("creature with no mobility tags -> null",
              Incap(MakePawn(Part("Blob", 1f, null))) == null);

        Console.WriteLine("\n=== S6. Targeting patch gating ===");
        Gm21Shooting.AnatomicalTargetingEnabled = true;
        Pawn killer = ShootingGrandmaster(Gm21AimMode.Killer);
        Pawn victim = Humanlike(out brain, out head, out neck, out heart, out lleg, out rleg, out foot, out toe);
        Check("Killer Grandmaster shot picks the brain",
              RunTargetingPrefix(victim, Shot(killer, victim)).HitPart == brain);

        Pawn downer = ShootingGrandmaster(Gm21AimMode.Downed);
        BodyPartRecord dPick = RunTargetingPrefix(victim, Shot(downer, victim)).HitPart;
        Check("Downed Grandmaster shot picks a leg", dPick == lleg || dPick == rleg, "got=" + Name(dPick));

        Pawn normal = ShootingGrandmaster(Gm21AimMode.Normal);
        Check("Normal mode leaves part selection to vanilla",
              RunTargetingPrefix(victim, Shot(normal, victim)).HitPart == null);

        Pawn lvl20 = new Pawn { health = new Pawn_HealthTracker(), skills = new Pawn_SkillTracker() };
        lvl20.skills.skills.Add(new SkillRecord { def = SkillDefOf.Shooting, levelInt = 20 });
        Gm21AimModeStore.Set(lvl20, Gm21AimMode.Killer);
        Check("level 20 pawn gets no anatomical targeting even if a mode is set",
              RunTargetingPrefix(victim, Shot(lvl20, victim)).HitPart == null);

        DamageInfo stray = Shot(killer, victim);
        stray.IntendedTarget = new Pawn();
        Check("a stray / friendly-fire hit is never redirected",
              RunTargetingPrefix(victim, stray).HitPart == null);

        DamageInfo melee = Shot(killer, victim);
        melee.Weapon = new ThingDef { defName = "Knife", IsRangedWeapon = false };
        Check("melee damage is not redirected", RunTargetingPrefix(victim, melee).HitPart == null);

        DamageInfo preset = Shot(killer, victim);
        preset.SetHitPart(toe);
        Check("an explicitly chosen hit part is respected, not overwritten",
              RunTargetingPrefix(victim, preset).HitPart == toe);

        DamageInfo harmless = Shot(killer, victim);
        harmless.Def = new DamageDef { defName = "Stun", harmsHealth = false };
        Check("non-health-harming damage is not redirected",
              RunTargetingPrefix(victim, harmless).HitPart == null);

        Gm21Shooting.AnatomicalTargetingEnabled = false;
        Check("feature disabled (e.g. combat overhaul) -> no redirection at all",
              RunTargetingPrefix(victim, Shot(killer, victim)).HitPart == null);
        Gm21Shooting.AnatomicalTargetingEnabled = true;

        Console.WriteLine("\n=== S7. Generated-pawn cap is now unconditional ===");
        Check("clampGeneratedPawns setting no longer exists",
              typeof(Gm21Settings).GetField("clampGeneratedPawns") == null);
        bool capOk = true; string capBad = "";
        foreach (int v in new[] { 20, 21, 50, 999 })
        {
            SkillRecord r = new SkillRecord { def = SkillDefOf.Shooting, levelInt = 0 };
            Patch_SkillRecord_SetLevel.Prefix(r, v);
            if (r.levelInt != 20) { capOk = false; capBad += " " + v + "->" + r.levelInt; }
        }
        Check("generated 20/21/50/999 all clamp to 20 with no setting to relax it", capOk, capBad);
        SkillRecord gm = new SkillRecord { def = SkillDefOf.Shooting, levelInt = 21 };
        Patch_SkillRecord_SetLevel.Prefix(gm, 20);
        Check("an earned 21 is still immune to the setter", gm.levelInt == 21);

        Console.WriteLine("\n=== S8. Uninstall cleanup clears aim mode ===");
        Pawn cleanupPawn = ShootingGrandmaster(Gm21AimMode.Killer);
        Gm21CleanupReport rep = Gm21Uninstall.Run();  // no game loaded: enumerates nothing
        Gm21AimModeStore.Clear(cleanupPawn);
        Check("aim mode is cleared alongside the demotion",
              Gm21AimModeStore.Get(cleanupPawn) == Gm21AimMode.Normal, "pawns=" + rep.pawnsProcessed);

        Console.WriteLine("\n=== S9. Shipped translation keys ===");
        string[] required = {
            "GM21_Descriptor_Shooting", "GM21_Achieved_Shooting", "GM21_Achieved_Default",
            "GM21_Achieved_Crafting", "GM21_AimMode_GizmoLabel", "GM21_AimMode_GizmoDesc",
            "GM21_AimMode_Normal", "GM21_AimMode_NormalDesc", "GM21_AimMode_Killer",
            "GM21_AimMode_KillerDesc", "GM21_AimMode_Downed", "GM21_AimMode_DownedDesc" };
        bool keysOk = true; string missing = "";
        foreach (string k in required) if (!k.CanTranslate()) { keysOk = false; missing += " " + k; }
        Check("every key the shooting code looks up is actually shipped", keysOk, missing);
        Check("removed keys are gone",
              !"GM21_Setting_ClampGenerated".CanTranslate()
              && !"GM21_Maintenance_Prepared".CanTranslate()
              && !"GM21_TooltipAchieved".CanTranslate());
        Check("Shooting's Grandmaster text does not mention Legendary crafting",
              !System.IO.File.ReadAllText("Languages/English/Keyed/Grandmaster21.xml")
                  .Split(new[]{"<GM21_Achieved_Shooting>"}, StringSplitOptions.None)[1]
                  .Split(new[]{"</GM21_Achieved_Shooting>"}, StringSplitOptions.None)[0]
                  .Contains("Legendary"));

        Console.WriteLine("\n================================");
        Console.WriteLine("PASS: " + pass + "   FAIL: " + fail);
        Environment.Exit(fail == 0 ? 0 : 1);
    }


    /// <summary>
    /// Loads the mod's real shipped translation keys into the stub, so CanTranslate answers the
    /// same question RimWorld would and the per-skill lookup fallbacks are genuinely exercised.
    /// </summary>
    static void LoadShippedKeys()
    {
        string path = "Languages/English/Keyed/Grandmaster21.xml";
        if (!System.IO.File.Exists(path))
        {
            Console.WriteLine("WARN  could not find " + path + "; translation-key tests are meaningless");
            return;
        }
        System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
        doc.Load(path);
        foreach (System.Xml.XmlNode n in doc.DocumentElement.ChildNodes)
        {
            if (n.NodeType == System.Xml.XmlNodeType.Element) Verse.Translator.KnownKeys.Add(n.Name);
        }
    }

    static string Name(BodyPartRecord p) { return p == null ? "null" : p.def.defName; }
}
