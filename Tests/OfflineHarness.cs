// Offline logic harness for Grandmaster 21.
//
// Runs the REAL compiled mod assembly against a stub RimWorld API whose signatures mirror
// Assembly-CSharp. It drives the mod's Harmony patch methods directly -- the same static
// Prefix/Postfix bodies Harmony would call -- so the mod's own decision logic is genuinely
// executed, not merely inspected.
//
// LIMITS, stated plainly: this does NOT run inside RimWorld, does not exercise Harmony
// patching, real IL, real pawn generation, saving or the world. Anything depending on those
// is verified in-game, not here. See the "Runtime tests" table in the PR/report.
using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;
using Grandmaster21;

static class OfflineHarness
{
    static int pass, fail;

    static void Check(string name, bool ok, string detail = "")
    {
        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name + (detail == "" ? "" : "   [" + detail + "]"));
        if (ok) pass++; else fail++;
    }

    static SkillRecord Rec(int level, float xpSince = 0f)
    {
        SkillRecord r = new SkillRecord();
        r.def = new SkillDef { defName = "Crafting", label = "crafting" };
        r.levelInt = level;
        r.xpSinceLastLevel = xpSince;
        return r;
    }

    // Reflection into the mod's internal authorised-demotion API, which is deliberately not public.
    static readonly MethodInfo ForceDemote = typeof(Gm21).GetMethod(
        "ForceDemoteForUninstall", BindingFlags.Static | BindingFlags.NonPublic);

    static bool Demote(SkillRecord r) { return (bool)ForceDemote.Invoke(null, new object[] { r }); }

    // Sets Gm21.LearnPatchApplied, which the transpiler normally sets at patch time.
    static void SetLearnPatchApplied(bool v) { Gm21.LearnPatchApplied = v; }

    // ---------------------------------------------------------------------------------------
    // Vanilla SkillRecord.Learn, decoded from 1.6 IL, with the mod's transpiled constant in
    // place and the mod's real Prefix/Postfix wrapped around it.
    // ---------------------------------------------------------------------------------------
    static void Learn(SkillRecord r, float xp, bool direct = false, bool ignoreLearnRate = false)
    {
        if (!Patch_SkillRecord_Learn.Prefix(r, xp, direct, ignoreLearnRate)) return;

        if (xp < 0f && r.levelInt == 0) { RunPostfix(r); return; }
        if (xp > 0f && !ignoreLearnRate) xp *= r.LearnRateFactor(direct);
        r.xpSinceLastLevel += xp;

        // <-- the single transpiled site: literal 20 replaced by Gm21.LearnCapFor(this)
        if (r.levelInt == Gm21.LearnCapFor(r))
        {
            float req = r.XpRequiredForLevelUp;
            if (r.xpSinceLastLevel > req - 1f) r.xpSinceLastLevel = req - 1f;
        }
        else
        {
            while (r.xpSinceLastLevel >= r.XpRequiredForLevelUp)
            {
                r.xpSinceLastLevel -= r.XpRequiredForLevelUp;
                r.levelInt++;
                if (r.levelInt >= 20)
                {
                    r.levelInt = 20;
                    float req = r.XpRequiredForLevelUp;
                    r.xpSinceLastLevel = UnityEngine.Mathf.Clamp(r.xpSinceLastLevel, 0f, req - 1f);
                    break;
                }
            }
        }

        // NOTE: vanilla's down-level loop is OUTSIDE the if/else above and triggers at -1000,
        // not at 0. Decoded from 1.6 IL; matches Tests/Harness.cs, which was written against
        // the real assembly.
        while (r.xpSinceLastLevel <= -1000f)
        {
            r.levelInt--;
            r.xpSinceLastLevel += r.XpRequiredForLevelUp;
            if (r.levelInt <= 0) { r.levelInt = 0; r.xpSinceLastLevel = 0f; break; }
        }

        RunPostfix(r);
    }

    static void RunPostfix(SkillRecord r) { Patch_SkillRecord_Learn.Postfix(r); }

    /// <summary>Drives SkillRecord.Level = value through the mod's real setter prefix.</summary>
    static void SetLevel(SkillRecord r, int value)
    {
        if (Patch_SkillRecord_SetLevel.Prefix(r, value)) r.Level = value; // prefix always returns false
    }

    static int LevelWithAptitude(SkillRecord r)
    {
        int result = r.GetLevel(true);
        Patch_SkillRecord_GetLevel.Postfix(r, true, ref result);
        return result;
    }

    static int UiLevel(SkillRecord r)
    {
        int result = r.GetLevelForUI(true);
        Patch_SkillRecord_GetLevelForUI.Postfix(r, ref result);
        return result;
    }

    static string Descriptor(SkillRecord r)
    {
        string result = r.LevelDescriptor;
        Patch_SkillRecord_LevelDescriptor.Postfix(r, ref result);
        return result;
    }

    /// <summary>Drives QualityUtility.GenerateQualityCreatedByPawn(int, bool).</summary>
    static QualityCategory QualityByLevel(int level, bool inspired, QualityCategory vanillaRoll)
    {
        QualityCategory result = QualityCategory.Awful;
        if (Patch_QualityUtility_ByLevel.Prefix(level, ref result)) result = vanillaRoll;
        Patch_QualityUtility_ByLevel.Postfix(level, ref result);
        return result;
    }

    static QualityCategory QualityByPawn(Pawn p, SkillDef s, QualityCategory inner)
    {
        QualityCategory result = inner;
        Patch_QualityUtility_ByPawn.Postfix(p, s, ref result);
        return result;
    }

    /// <summary>The per-pawn half of Gm21Uninstall.Run, isolated from the game-only enumeration.</summary>
    static void CleanPawn(Pawn p, ref int demoted, ref int cleared)
    {
        foreach (SkillRecord rec in p.skills.skills)
        {
            if (GrandmasterStore.Clear(rec)) cleared++;
            if (Demote(rec)) demoted++;
        }
    }

    static void Main()
    {
        Gm21Mod.Settings = new Gm21Settings();
        Gm21Mod.Settings.Validate();
        SetLearnPatchApplied(true);
        LoadShippedKeys();

        Console.WriteLine("=== A. Grandmaster XP accrual and promotion ===");
        Gm21Mod.Settings.grandmasterXpRequirement = 1000.0;
        SkillRecord a = Rec(20);
        Learn(a, 400f);
        Check("L20 earns Grandmaster XP; progress increases", GrandmasterStore.Get(a) == 400.0 && a.levelInt == 20,
              "gmXp=" + GrandmasterStore.Get(a));
        Learn(a, 400f);
        Check("still 20 below threshold", a.levelInt == 20, "gmXp=" + GrandmasterStore.Get(a));
        Learn(a, 400f);
        Check("threshold reached -> becomes 21", a.levelInt == 21, "levelInt=" + a.levelInt);
        Check("xpSinceLastLevel sane after promotion",
              a.xpSinceLastLevel >= 0f && a.xpSinceLastLevel < a.XpRequiredForLevelUp,
              "xp=" + a.xpSinceLastLevel + " req=" + a.XpRequiredForLevelUp);

        Console.WriteLine("\n=== B. Learn transpiler fail-safe ===");
        SetLearnPatchApplied(false);
        SkillRecord b = Rec(20);
        GrandmasterStore.Set(b, 5000.0);
        Learn(b, 100f);
        Check("promotion disabled when Learn patch did not apply", b.levelInt == 20, "levelInt=" + b.levelInt);
        Check("XP still tracked while promotion is disabled", GrandmasterStore.Get(b) > 5000.0,
              "gmXp=" + GrandmasterStore.Get(b));
        SkillRecord b2 = Rec(20);
        Gm21.Promote(b2);
        Check("Gm21.Promote itself refuses in an unsafe environment", b2.levelInt == 20, "levelInt=" + b2.levelInt);
        SetLearnPatchApplied(true);
        Gm21.Promote(b2);
        Check("Gm21.Promote works again once the patch is applied", b2.levelInt == 21, "levelInt=" + b2.levelInt);

        Console.WriteLine("\n=== C. Grandmaster permanence via the Level setter ===");
        int[] assignments = { 22, 999, 20, 10, 5, 0, -50 };
        bool permOk = true; string permBad = "";
        foreach (int v in assignments)
        {
            SkillRecord r = Rec(21);
            SetLevel(r, v);
            if (r.levelInt != 21) { permOk = false; permBad += " Level=" + v + "->" + r.levelInt; }
        }
        Check("stored 21 survives every ordinary Level assignment", permOk, permBad);

        Console.WriteLine("\n=== D. Generation cap still holds for non-Grandmasters ===");
        int[] gen = { 20, 21, 50, 999 };
        bool genOk = true; string genBad = "";
        foreach (int v in gen)
        {
            SkillRecord r = Rec(0);
            SetLevel(r, v);
            if (r.levelInt != 20) { genOk = false; genBad += " gen " + v + "->" + r.levelInt; }
        }
        Check("generated skill 20/21/50/999 all clamp to 20", genOk, genBad);
        SkillRecord neg = Rec(5); SetLevel(neg, -3);
        Check("negative assignment clamps to 0 for a non-Grandmaster", neg.levelInt == 0, "levelInt=" + neg.levelInt);
        SkillRecord mid = Rec(5); SetLevel(mid, 12);
        Check("ordinary assignment below the cap is untouched", mid.levelInt == 12, "levelInt=" + mid.levelInt);

        Console.WriteLine("\n=== E. Decay and negative XP ===");
        SkillRecord gm = Rec(21, 100f);
        for (int i = 0; i < 5000; i++) Learn(gm, -12f);
        Check("21 survives 5000 decay-sized negative XP events", gm.levelInt == 21, "levelInt=" + gm.levelInt);
        Learn(gm, -5000000f, false, true);
        Check("21 survives one huge negative XP event", gm.levelInt == 21, "levelInt=" + gm.levelInt);
        Check("Interval prefix skips vanilla decay at 21", !Patch_SkillRecord_Interval.Prefix(Rec(21)));
        Check("Interval prefix lets vanilla decay run at 20", Patch_SkillRecord_Interval.Prefix(Rec(20)));
        SkillRecord l20 = Rec(20, 500f);
        Learn(l20, -2000f, false, true);
        Check("level 20 negative XP still behaves like vanilla (drops to 19)",
              l20.levelInt == 19, "levelInt=" + l20.levelInt + " xp=" + l20.xpSinceLastLevel);
        SkillRecord l20b = Rec(20, 500f);
        Learn(l20b, -600f, false, true);
        Check("level 20 small negative XP does not cross vanilla's -1000 threshold",
              l20b.levelInt == 20 && l20b.xpSinceLastLevel == -100f,
              "levelInt=" + l20b.levelInt + " xp=" + l20b.xpSinceLastLevel);
        // Regression: vanilla's down-level loop runs on POSITIVE XP too, so a Grandmaster
        // carrying a deeply negative xpSinceLastLevel (written by another mod, or by an older
        // save) must not be demoted by the next point of XP it earns.
        SkillRecord poisoned = Rec(21, -50000f);
        Learn(poisoned, 1f);
        Check("21 with a poisoned negative xpSinceLastLevel survives positive XP",
              poisoned.levelInt == 21 && poisoned.xpSinceLastLevel >= 0f,
              "levelInt=" + poisoned.levelInt + " xp=" + poisoned.xpSinceLastLevel);

        Console.WriteLine("\n=== F. 21 never becomes 22 ===");
        SkillRecord ceil = Rec(21);
        for (int i = 0; i < 20000; i++) Learn(ceil, 10000f);
        Check("21 stays exactly 21 after 200,000,000 XP", ceil.levelInt == 21, "levelInt=" + ceil.levelInt);

        Console.WriteLine("\n=== G. Aptitude semantics ===");
        SkillRecord apt = Rec(21); apt.aptitudeStub = -1;
        Check("stored 21 with -1 aptitude still DISPLAYS 21", UiLevel(apt) == 21, "ui=" + UiLevel(apt));
        Check("stored 21 with -1 aptitude keeps a Grandmaster descriptor",
              Descriptor(apt) == "GM21_GrandmasterDescriptor", "desc=" + Descriptor(apt));
        SkillRecord shootGm = Rec(21); shootGm.def = SkillDefOf.Shooting;
        Check("Shooting 21 gets its own title, not the generic one",
              Descriptor(shootGm) == "GM21_Descriptor_Shooting", "desc=" + Descriptor(shootGm));
        SkillRecord medGm = Rec(21); medGm.def = new SkillDef { defName = "Medicine", label = "medicine" };
        Check("a skill with no bespoke title falls back to the generic one",
              Descriptor(medGm) == "GM21_GrandmasterDescriptor", "desc=" + Descriptor(medGm));
        Check("stored 21 with -1 aptitude is still IsGrandmaster", Gm21.IsGrandmaster(apt));
        Check("stored 21 with -1 aptitude has MECHANICAL level 20 (aptitude preserved)",
              LevelWithAptitude(apt) == 20, "mech=" + LevelWithAptitude(apt));
        SkillRecord apt5 = Rec(21); apt5.aptitudeStub = -5;
        Check("stored 21 with -5 aptitude: display 21, mechanical 16",
              UiLevel(apt5) == 21 && LevelWithAptitude(apt5) == 16,
              "ui=" + UiLevel(apt5) + " mech=" + LevelWithAptitude(apt5));
        SkillRecord aptPos = Rec(21); aptPos.aptitudeStub = 4;
        Check("positive aptitude cannot exceed 21 (display or mechanical)",
              UiLevel(aptPos) == 21 && LevelWithAptitude(aptPos) == 21,
              "ui=" + UiLevel(aptPos) + " mech=" + LevelWithAptitude(aptPos));
        SkillRecord fake = Rec(20); fake.aptitudeStub = 5;
        Check("level 20 + positive aptitude is NOT a Grandmaster",
              !Gm21.IsGrandmaster(fake) && UiLevel(fake) <= 20 && Descriptor(fake) != "GM21_GrandmasterDescriptor",
              "ui=" + UiLevel(fake));

        Console.WriteLine("\n=== H. Quality: int overload, deterministic ON ===");
        Gm21Mod.Settings.deterministicQuality = true;
        int[] lv = { 0, 3, 4, 7, 8, 10, 11, 13, 14, 16, 17, 20, 21 };
        QualityCategory[] exp = {
            QualityCategory.Awful, QualityCategory.Awful, QualityCategory.Poor, QualityCategory.Poor,
            QualityCategory.Normal, QualityCategory.Normal, QualityCategory.Good, QualityCategory.Good,
            QualityCategory.Excellent, QualityCategory.Excellent, QualityCategory.Masterwork,
            QualityCategory.Masterwork, QualityCategory.Legendary };
        bool bandsOk = true; string bad = "";
        for (int i = 0; i < lv.Length; i++)
        {
            QualityCategory got = QualityByLevel(lv[i], false, QualityCategory.Normal);
            if (got != exp[i]) { bandsOk = false; bad += " L" + lv[i] + "=" + got; }
        }
        Check("all 13 deterministic band boundaries correct", bandsOk, bad);
        Check("deterministic ON, level 20, inspired -> Masterwork",
              QualityByLevel(20, true, QualityCategory.Legendary) == QualityCategory.Masterwork);
        Check("deterministic ON, level 21 -> Legendary",
              QualityByLevel(21, false, QualityCategory.Awful) == QualityCategory.Legendary);

        Console.WriteLine("\n=== I. Quality: int overload, deterministic OFF ===");
        Gm21Mod.Settings.deterministicQuality = false;
        bool randomPreserved = true; string rp = "";
        foreach (QualityCategory roll in new[] { QualityCategory.Awful, QualityCategory.Poor,
                 QualityCategory.Normal, QualityCategory.Good, QualityCategory.Excellent,
                 QualityCategory.Masterwork })
        {
            QualityCategory got = QualityByLevel(12, false, roll);
            if (got != roll) { randomPreserved = false; rp += " " + roll + "->" + got; }
        }
        Check("deterministic OFF, level 0-20: vanilla roll preserved", randomPreserved, rp);
        Check("deterministic OFF, level 20 rolling Legendary -> capped to Masterwork",
              QualityByLevel(20, true, QualityCategory.Legendary) == QualityCategory.Masterwork);
        Check("deterministic OFF, level 21 -> Legendary (the hardened edge case)",
              QualityByLevel(21, false, QualityCategory.Awful) == QualityCategory.Legendary,
              "got=" + QualityByLevel(21, false, QualityCategory.Awful));
        Check("deterministic OFF, level 25 (third-party overload call) -> Legendary",
              QualityByLevel(25, false, QualityCategory.Normal) == QualityCategory.Legendary);
        Gm21Mod.Settings.deterministicQuality = true;

        Console.WriteLine("\n=== J. Quality: Pawn overload ===");
        SkillDef craft = new SkillDef { defName = "Crafting", label = "crafting" };
        Pawn p21 = NewPawn(craft, 21);
        Pawn p20 = NewPawn(craft, 20);
        Check("Pawn overload: stored 21 -> Legendary",
              QualityByPawn(p21, craft, QualityCategory.Normal) == QualityCategory.Legendary);
        Check("Pawn overload: stored 21 with -5 aptitude -> still Legendary",
              QualityByPawn(WithAptitude(p21, craft, -5), craft, QualityCategory.Good) == QualityCategory.Legendary);
        Check("Pawn overload: level 20 Production Specialist rolling Legendary -> Masterwork",
              QualityByPawn(p20, craft, QualityCategory.Legendary) == QualityCategory.Masterwork);
        Check("Pawn overload: level 20 + positive aptitude -> never Legendary",
              QualityByPawn(WithAptitude(p20, craft, 5), craft, QualityCategory.Legendary) == QualityCategory.Masterwork);

        Console.WriteLine("\n=== K. Authorised cleanup (per-pawn half of Prepare Save for Uninstall) ===");
        Pawn clean = NewPawn(craft, 21);
        clean.skills.skills.Add(MakeRec("Cooking", 20));
        clean.skills.skills.Add(MakeRec("Mining", 7));
        GrandmasterStore.Set(clean.skills.skills[0], 999999999.0);
        GrandmasterStore.Set(clean.skills.skills[1], 12345.0);   // partial progress at level 20
        int demoted = 0, cleared = 0;
        CleanPawn(clean, ref demoted, ref cleared);
        Check("cleanup demotes 21 -> 20", clean.skills.skills[0].levelInt == 20,
              "levelInt=" + clean.skills.skills[0].levelInt);
        Check("cleanup reports exactly one demotion", demoted == 1, "demoted=" + demoted);
        Check("cleanup clears partial progress on a level 20 skill too", cleared == 2, "cleared=" + cleared);
        bool noProgress = true;
        foreach (SkillRecord r in clean.skills.skills) if (GrandmasterStore.Get(r) != 0.0) noProgress = false;
        Check("no Grandmaster progress remains on any skill", noProgress);
        Check("cleanup does not touch ordinary levels", clean.skills.skills[2].levelInt == 7);
        Check("xpSinceLastLevel is in range for level 20 after demotion",
              clean.skills.skills[0].xpSinceLastLevel >= 0f
              && clean.skills.skills[0].xpSinceLastLevel < clean.skills.skills[0].XpRequiredForLevelUp);
        int d2 = 0, c2 = 0;
        CleanPawn(clean, ref d2, ref c2);
        Check("cleanup is idempotent (second run is a no-op)", d2 == 0 && c2 == 0, "d=" + d2 + " c=" + c2);

        Console.WriteLine("\n=== L. Authorised-scope exception safety ===");
        Check("bypass is not active at rest", !AuthorizedActive());
        try
        {
            using (EnterAuthorized())
            {
                if (AuthorizedActive()) throw new InvalidOperationException("boom");
            }
        }
        catch (InvalidOperationException) { }
        Check("bypass is released after an exception inside the scope", !AuthorizedActive());
        SkillRecord afterThrow = Rec(21);
        SetLevel(afterThrow, 3);
        Check("permanence still enforced after an exception unwound a scope",
              afterThrow.levelInt == 21, "levelInt=" + afterThrow.levelInt);

        Console.WriteLine("\n=== M. double precision (unchanged architecture) ===");
        float f = 1000000000f; f += 1f;
        double d = 1000000000.0; d += 1.0;
        Check("float would lose a 1 XP increment at 1e9", f == 1000000000f);
        Check("double retains it", d == 1000000001.0);
        SkillRecord acc = Rec(20);
        for (int i = 0; i < 200000; i++) GrandmasterStore.Add(acc, 5000.0);
        Check("store accumulates exactly 1e9 over 200k increments",
              GrandmasterStore.Get(acc) == 1000000000.0, "got=" + GrandmasterStore.Get(acc).ToString("N0"));

        Console.WriteLine("\n=== N. Settings validation ===");
        Gm21Settings s = new Gm21Settings();
        s.grandmasterXpRequirement = -5; s.Validate();
        Check("below-min clamped up", s.grandmasterXpRequirement == Gm21Settings.MinRequirement);
        s.grandmasterXpRequirement = 1e30; s.Validate();
        Check("above-max clamped down", s.grandmasterXpRequirement == Gm21Settings.MaxRequirement);
        s.grandmasterXpRequirement = double.NaN; s.Validate();
        Check("NaN reset to default", s.grandmasterXpRequirement == Gm21Settings.DefaultRequirement);
        Check("removed decay toggle is gone from the settings type",
              typeof(Gm21Settings).GetField("grandmasterPreventsDecay") == null);

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

    static SkillRecord MakeRec(string defName, int level)
    {
        SkillRecord r = new SkillRecord();
        r.def = new SkillDef { defName = defName, label = defName.ToLower() };
        r.levelInt = level;
        return r;
    }

    static Pawn NewPawn(SkillDef def, int level)
    {
        Pawn p = new Pawn();
        p.skills = new Pawn_SkillTracker();
        SkillRecord r = new SkillRecord { def = def, levelInt = level };
        r.pawnStub = p;
        p.skills.skills.Add(r);
        return p;
    }

    static Pawn WithAptitude(Pawn p, SkillDef def, int aptitude)
    {
        p.skills.GetSkill(def).aptitudeStub = aptitude;
        return p;
    }

    // Gm21Authorized is internal; reach it the same way a test would reach any internal API.
    static readonly Type AuthType = typeof(Gm21).Assembly.GetType("Grandmaster21.Gm21Authorized");
    static bool AuthorizedActive()
    {
        return (bool)AuthType.GetProperty("Active", BindingFlags.Static | BindingFlags.NonPublic)
                             .GetValue(null, null);
    }
    static IDisposable EnterAuthorized()
    {
        return (IDisposable)AuthType.GetMethod("Enter", BindingFlags.Static | BindingFlags.NonPublic)
                                    .Invoke(null, null);
    }
}
