using System;
using System.Reflection;
using RimWorld;
using Verse;
using Grandmaster21;

// Executes the COMPILED Grandmaster21.dll against RimWorld's real SkillRecord type.
// Vanilla Learn() cannot be invoked directly here (it dereferences Pawn for TotallyDisabled /
// LearnRateFactor, and there is no game world), so SimulateLearn below re-implements the
// vanilla algorithm exactly as decoded from 1.6 IL, driving the REAL Gm21.LearnCapFor,
// REAL GrandmasterStore and REAL Gm21.Promote from the built assembly.
class Harness
{
    static int pass = 0, fail = 0;

    static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name + (detail == "" ? "" : "   [" + detail + "]"));
        if (ok) pass++; else fail++;
    }

    static SkillRecord NewRec(int level, float xpSince)
    {
        SkillRecord r = new SkillRecord();
        r.levelInt = level;
        r.xpSinceLastLevel = xpSince;
        return r;
    }

    // Faithful re-implementation of vanilla SkillRecord.Learn WITH the mod's transpiler applied
    // (literal 20 in "levelInt == 20" replaced by Gm21.LearnCapFor(this)) and the mod's
    // prefix/postfix semantics. learnFactor stands in for LearnRateFactor(direct).
    static void SimulateLearn(SkillRecord r, float xp, float learnFactor, bool ignoreLearnRate)
    {
        // --- mod PREFIX ---
        if (r.levelInt >= 21)
        {
            if (xp < 0f && Gm21Mod.Settings.grandmasterPreventsDecay) return; // swallow decay
        }
        else if (r.levelInt == 20 && xp > 0f)
        {
            float eff0 = ignoreLearnRate ? xp : xp * learnFactor;
            if (eff0 > 0f) GrandmasterStore.Add(r, eff0);
        }

        // --- vanilla body (decoded), with the one transpiled constant ---
        if (xp < 0f && r.levelInt == 0) return;
        if (xp > 0f && !ignoreLearnRate) xp *= learnFactor;
        r.xpSinceLastLevel += xp;

        if (r.levelInt == Gm21.LearnCapFor(r))                 // <-- transpiled site
        {
            float req0 = r.XpRequiredForLevelUp;
            if (r.xpSinceLastLevel > req0 - 1f) r.xpSinceLastLevel = req0 - 1f;
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
                    float req1 = r.XpRequiredForLevelUp;
                    r.xpSinceLastLevel = UnityEngine.Mathf.Clamp(r.xpSinceLastLevel, 0f, req1 - 1f);
                    break;
                }
            }
        }
        while (r.xpSinceLastLevel <= -1000f)
        {
            r.levelInt--;
            r.xpSinceLastLevel += r.XpRequiredForLevelUp;
            if (r.levelInt <= 0) { r.levelInt = 0; r.xpSinceLastLevel = 0f; break; }
        }

        // --- mod POSTFIX ---
        if (r.levelInt == 20 && GrandmasterStore.Get(r) >= Gm21Mod.Settings.grandmasterXpRequirement)
            Gm21.Promote(r);
    }

    static void Main()
    {
        Gm21Mod.Settings = new Gm21Settings();
        Gm21Mod.Settings.Validate();

        Console.WriteLine("=== environment ===");
        Console.WriteLine("SkillRecord.MaxLevel (vanilla const) = " + SkillRecord.MaxLevel);
        Console.WriteLine("Gm21.GrandmasterLevel                = " + Gm21.GrandmasterLevel);
        Console.WriteLine("default requirement                  = " + Gm21Mod.Settings.grandmasterXpRequirement.ToString("N0"));
        Console.WriteLine("XpRequiredToLevelUpFrom(19/20/21)    = "
            + SkillRecord.XpRequiredToLevelUpFrom(19) + " / "
            + SkillRecord.XpRequiredToLevelUpFrom(20) + " / "
            + SkillRecord.XpRequiredToLevelUpFrom(21));
        Console.WriteLine();

        Console.WriteLine("=== T1  numeric safety: float vs double at 1e9 ===");
        float f = 1000000000f; f += 1f;
        double d = 1000000000.0; d += 1.0;
        Check("float loses a 1 XP increment at 1e9", f == 1000000000f, "float 1e9+1 = " + f.ToString("F0"));
        Check("double retains a 1 XP increment at 1e9", d == 1000000001.0, "double 1e9+1 = " + d.ToString("F0"));
        // accumulate a billion in realistic increments into the real store
        SkillRecord acc = NewRec(20, 0f);
        for (int i = 0; i < 200000; i++) GrandmasterStore.Add(acc, 5000.0);
        Check("store accumulates exactly 1e9 over 200k increments",
              GrandmasterStore.Get(acc) == 1000000000.0, "got " + GrandmasterStore.Get(acc).ToString("N0"));
        Console.WriteLine();

        Console.WriteLine("=== T2  LearnCapFor (the transpiled comparison) ===");
        Check("level 19 -> cap 20 (normal level-up loop)", Gm21.LearnCapFor(NewRec(19,0)) == 20, "");
        Check("level 20 -> cap 20 (vanilla at-cap clamp)", Gm21.LearnCapFor(NewRec(20,0)) == 20, "");
        Check("level 21 -> cap 21 (at-cap clamp, loop skipped)", Gm21.LearnCapFor(NewRec(21,0)) == 21, "");
        Console.WriteLine();

        Console.WriteLine("=== T3  progression 20 -> 21 with a low threshold ===");
        Gm21Mod.Settings.grandmasterXpRequirement = 1000.0;
        SkillRecord r = NewRec(20, 0f);
        SimulateLearn(r, 400f, 1.0f, false);
        Check("still 20 below threshold", r.levelInt == 20, "gmXp=" + GrandmasterStore.Get(r));
        SimulateLearn(r, 400f, 1.0f, false);
        Check("still 20 below threshold (800)", r.levelInt == 20, "gmXp=" + GrandmasterStore.Get(r));
        SimulateLearn(r, 400f, 1.0f, false);
        Check("promoted to 21 at threshold", r.levelInt == 21, "gmXp=" + GrandmasterStore.Get(r));
        Check("xpSinceLastLevel sane after promotion",
              r.xpSinceLastLevel >= 0f && r.xpSinceLastLevel < r.XpRequiredForLevelUp,
              "xp=" + r.xpSinceLastLevel + " req=" + r.XpRequiredForLevelUp);
        Console.WriteLine();

        Console.WriteLine("=== T4  level 21 never becomes 22 ===");
        for (int i = 0; i < 5000; i++) SimulateLearn(r, 10000f, 1.5f, false);
        Check("after 5000 x 10000 XP still exactly 21", r.levelInt == 21, "levelInt=" + r.levelInt);
        Console.WriteLine();

        Console.WriteLine("=== T5  level 21 does not decay ===");
        SkillRecord gm = NewRec(21, 100f);
        for (int i = 0; i < 2000; i++) SimulateLearn(gm, -12f, 1f, false);   // vanilla lvl-20 decay rate
        Check("21 survives 2000 decay ticks", gm.levelInt == 21, "levelInt=" + gm.levelInt);
        SimulateLearn(gm, -500000f, 1f, true);
        Check("21 survives a huge negative XP event", gm.levelInt == 21, "levelInt=" + gm.levelInt);
        // and with the setting off, vanilla-style decay is allowed again
        Gm21Mod.Settings.grandmasterPreventsDecay = false;
        SkillRecord gm2 = NewRec(21, 0f);
        SimulateLearn(gm2, -200000f, 1f, true);
        Check("with preventDecay OFF, 21 can fall (setting honoured)", gm2.levelInt < 21, "levelInt=" + gm2.levelInt);
        Gm21Mod.Settings.grandmasterPreventsDecay = true;
        Console.WriteLine();

        Console.WriteLine("=== T6  ordinary levelling still stops at 20 ===");
        SkillRecord norm = NewRec(0, 0f);
        Gm21Mod.Settings.grandmasterXpRequirement = 1000000000.0;
        for (int i = 0; i < 100000; i++) SimulateLearn(norm, 4000f, 1.5f, false);
        Check("never exceeds 20 without meeting the requirement", norm.levelInt == 20, "levelInt=" + norm.levelInt);
        Check("but Grandmaster XP did accumulate at 20", GrandmasterStore.Get(norm) > 0, "gmXp=" + GrandmasterStore.Get(norm).ToString("N0"));
        Console.WriteLine();

        Console.WriteLine("=== T7  deterministic quality bands ===");
        int[] lv  = {0,3,4,7,8,10,11,13,14,16,17,20,21};
        QualityCategory[] exp = {
            QualityCategory.Awful, QualityCategory.Awful,
            QualityCategory.Poor, QualityCategory.Poor,
            QualityCategory.Normal, QualityCategory.Normal,
            QualityCategory.Good, QualityCategory.Good,
            QualityCategory.Excellent, QualityCategory.Excellent,
            QualityCategory.Masterwork, QualityCategory.Masterwork,
            QualityCategory.Legendary };
        bool bandsOk = true; string bad = "";
        for (int i = 0; i < lv.Length; i++)
        {
            QualityCategory got = Gm21.BandFor(lv[i]);
            if (got != exp[i]) { bandsOk = false; bad += " L" + lv[i] + "=" + got; }
        }
        Check("all 13 band boundaries correct", bandsOk, bad);
        Check("only level 21 yields Legendary",
              Gm21.BandFor(20) != QualityCategory.Legendary && Gm21.BandFor(21) == QualityCategory.Legendary, "");
        Console.WriteLine();

        Console.WriteLine("=== T8  IsGrandmaster reads stored level, not aptitude-inflated Level ===");
        Check("levelInt 20 is not a Grandmaster", !Gm21.IsGrandmaster(NewRec(20,0)), "");
        Check("levelInt 21 is a Grandmaster", Gm21.IsGrandmaster(NewRec(21,0)), "");
        Check("null-safe", !Gm21.IsGrandmaster((SkillRecord)null), "");
        Console.WriteLine();

        Console.WriteLine("=== T9  independent per-skill progression ===");
        SkillRecord a = NewRec(20,0), b = NewRec(20,0);
        Gm21Mod.Settings.grandmasterXpRequirement = 1000.0;
        for (int i=0;i<3;i++) SimulateLearn(a, 400f, 1f, false);
        Check("skill A promoted", a.levelInt == 21, "");
        Check("skill B unaffected", b.levelInt == 20 && GrandmasterStore.Get(b) == 0.0, "gmXpB=" + GrandmasterStore.Get(b));
        Console.WriteLine();

        Console.WriteLine("=== T10 settings validation ===");
        Gm21Settings s = new Gm21Settings();
        s.grandmasterXpRequirement = -5; s.Validate();
        Check("below-min clamped up", s.grandmasterXpRequirement == Gm21Settings.MinRequirement, "=" + s.grandmasterXpRequirement);
        s.grandmasterXpRequirement = 1e30; s.Validate();
        Check("above-max clamped down", s.grandmasterXpRequirement == Gm21Settings.MaxRequirement, "=" + s.grandmasterXpRequirement);
        s.grandmasterXpRequirement = double.NaN; s.Validate();
        Check("NaN reset to default", s.grandmasterXpRequirement == Gm21Settings.DefaultRequirement, "=" + s.grandmasterXpRequirement);
        Console.WriteLine();

        Console.WriteLine("================================");
        Console.WriteLine("PASS: " + pass + "   FAIL: " + fail);
        Environment.Exit(fail == 0 ? 0 : 1);
    }
}
