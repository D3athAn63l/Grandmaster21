// Verifies every RimWorld member Grandmaster 21 resolves AT RUNTIME rather than at compile time.
//
// Compiling against Assembly-CSharp proves the directly-referenced API exists. It proves nothing
// about:
//   * members looked up reflectively (AccessTools.Method/Property/Field/Constructor)
//   * Harmony injection parameter NAMES -- Harmony binds prefix/postfix arguments by name, so a
//     renamed vanilla parameter compiles perfectly and then throws at patch time
//   * BodyPartTagDef defNames, which live in XML; checked here against BodyPartTagDefOf, whose
//     field names must match the defNames it loads
//
// Run against the real game assemblies. Exits non-zero on any failure.
using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

static class VerifyRuntimeTargets
{
    static int pass, fail;

    static int skipped;

    static void Ok(string what, bool good, string detail = "")
    {
        Console.WriteLine((good ? "PASS  " : "FAIL  ") + what + (detail == "" ? "" : "   [" + detail + "]"));
        if (good) pass++; else fail++;
    }

    /// <summary>
    /// Some RimWorld types cannot be reflected over outside the game -- ModMetaData, for one,
    /// has a Steamworks.NET field, and that assembly ships with the game launcher rather than in
    /// Managed/. A TypeLoadException here is an artefact of running headless, not a finding, so it
    /// is reported as SKIP rather than counted either way.
    /// </summary>
    static void Try(string what, Func<bool> check, Func<string> detail = null)
    {
        try { Ok(what, check(), detail == null ? "" : detail()); }
        catch (TypeLoadException e)
        {
            Console.WriteLine("SKIP  " + what + "   [not reflectable headless: "
                              + e.Message.Split('.')[0] + "]");
            skipped++;
        }
        catch (Exception e)
        {
            Ok(what, false, e.GetType().Name + ": " + e.Message);
        }
    }

    static string Params(MethodBase m)
    {
        return m == null ? "<null>" : string.Join(", ",
            m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name).ToArray());
    }

    /// <summary>A Harmony injection binds by name: the vanilla parameter must actually be called this.</summary>
    static void NeedParam(string what, MethodBase m, string paramName, string expectedType = null)
    {
        if (m == null) { Ok(what + " -> param '" + paramName + "'", false, "method not found"); return; }
        ParameterInfo p = m.GetParameters().FirstOrDefault(x => x.Name == paramName);
        bool good = p != null && (expectedType == null || p.ParameterType.Name.Contains(expectedType));
        Ok(what + " has parameter '" + paramName + "'", good, Params(m));
    }

    static MethodInfo Getter(Type t, params string[] names)
    {
        foreach (string n in names)
        {
            PropertyInfo p = AccessTools.Property(t, n);
            if (p != null && p.GetGetMethod(true) != null) return p.GetGetMethod(true);
        }
        return null;
    }

    static void Main()
    {
        Console.WriteLine("=== Reflection targets: shooting ===");

        MethodInfo tryCast = AccessTools.Method(typeof(Verb_LaunchProjectile), "TryCastShot");
        Ok("Verb_LaunchProjectile.TryCastShot resolves", tryCast != null, Params(tryCast));

        MethodInfo hitReport = AccessTools.Method(typeof(ShotReport), "HitReportFor");
        Ok("ShotReport.HitReportFor resolves", hitReport != null, Params(hitReport));
        NeedParam("ShotReport.HitReportFor", hitReport, "caster", "Thing");

        MethodInfo aim = Getter(typeof(ShotReport), "AimOnTargetChance_IgnoringPosture", "ChanceToNotGoWild_IgnoringPosture");
        Ok("ShotReport aim-chance property resolves", aim != null, aim == null ? "" : aim.Name + " -> " + aim.ReturnType.Name);
        Ok("  ...and returns float", aim != null && aim.ReturnType == typeof(float));

        MethodInfo cover = Getter(typeof(ShotReport), "PassCoverChance", "ChanceToNotHitCover");
        Ok("ShotReport cover-chance property resolves", cover != null, cover == null ? "" : cover.Name);
        Ok("  ...and returns float", cover != null && cover.ReturnType == typeof(float));

        MethodInfo total = Getter(typeof(ShotReport), "TotalEstimatedHitChance");
        Ok("ShotReport.TotalEstimatedHitChance resolves (cosmetic)", total != null);

        ConstructorInfo warmup = AccessTools.Constructor(typeof(Stance_Warmup),
            new[] { typeof(int), typeof(LocalTargetInfo), typeof(Verb) });
        Ok("Stance_Warmup(int, LocalTargetInfo, Verb) resolves", warmup != null, Params(warmup));
        NeedParam("Stance_Warmup..ctor", warmup, "ticks", "Int32");
        NeedParam("Stance_Warmup..ctor", warmup, "verb", "Verb");

        ConstructorInfo cooldown = AccessTools.Constructor(typeof(Stance_Cooldown),
            new[] { typeof(int), typeof(LocalTargetInfo), typeof(Verb) });
        Ok("Stance_Cooldown(int, LocalTargetInfo, Verb) resolves", cooldown != null, Params(cooldown));
        NeedParam("Stance_Cooldown..ctor", cooldown, "ticks", "Int32");
        NeedParam("Stance_Cooldown..ctor", cooldown, "verb", "Verb");

        MethodInfo preApply = AccessTools.Method(typeof(Pawn), "PreApplyDamage");
        Ok("Pawn.PreApplyDamage resolves", preApply != null, Params(preApply));
        NeedParam("Pawn.PreApplyDamage", preApply, "dinfo", "DamageInfo");

        MethodInfo checkState = AccessTools.Method(typeof(Pawn_HealthTracker), "CheckForStateChange");
        Ok("Pawn_HealthTracker.CheckForStateChange resolves", checkState != null, Params(checkState));
        NeedParam("Pawn_HealthTracker.CheckForStateChange", checkState, "dinfo");

        FieldInfo forceDowned = AccessTools.Field(typeof(Pawn_HealthTracker), "forceDowned");
        Ok("Pawn_HealthTracker.forceDowned resolves", forceDowned != null,
           forceDowned == null ? "" : forceDowned.FieldType.Name);
        Ok("  ...and is a bool", forceDowned != null && forceDowned.FieldType == typeof(bool));

        Console.WriteLine("\n=== Burst discipline (protected members, read reflectively) ===");
        FieldInfo burstLeft = AccessTools.Field(typeof(Verb), "burstShotsLeft");
        Ok("Verb.burstShotsLeft resolves", burstLeft != null, burstLeft == null ? "" : burstLeft.FieldType.Name);
        Ok("  ...and is an int", burstLeft != null && burstLeft.FieldType == typeof(int));
        MethodInfo shotsPerBurst = AccessTools.PropertyGetter(typeof(Verb), "ShotsPerBurst");
        Ok("Verb.ShotsPerBurst getter resolves", shotsPerBurst != null);
        Ok("  ...and returns int", shotsPerBurst != null && shotsPerBurst.ReturnType == typeof(int));
        Ok("Verb.CurrentTarget exists", AccessTools.Property(typeof(Verb), "CurrentTarget") != null);
        FieldInfo trackerPawn = AccessTools.Field(typeof(Pawn_HealthTracker), "pawn");
        Ok("Pawn_HealthTracker.pawn resolves (intended-target check)", trackerPawn != null);
        Ok("  ...and is a Pawn", trackerPawn != null && trackerPawn.FieldType == typeof(Pawn));
        Ok("Pawn_HealthTracker.Downed exists", AccessTools.Property(typeof(Pawn_HealthTracker), "Downed") != null);
        Ok("BodyPartRecord.parts exists (leaf detection)", typeof(BodyPartRecord).GetField("parts") != null);
        Ok("BodyPartRecord.depth exists", typeof(BodyPartRecord).GetField("depth") != null);

        Console.WriteLine("\n=== Body part tags (defNames, via BodyPartTagDefOf) ===");
        Type tagDefOf = AccessTools.TypeByName("RimWorld.BodyPartTagDefOf");
        Ok("BodyPartTagDefOf exists", tagDefOf != null);
        foreach (string tag in new[] { "ConsciousnessSource", "BreathingPathway", "BloodPumpingSource",
                                       "BreathingSource", "BloodFiltrationSource",
                                       "MovingLimbCore", "MovingLimbSegment", "MovingLimbDigit",
                                       "Pelvis", "Spine",
                                       "ManipulationLimbCore", "ManipulationLimbSegment",
                                       "ManipulationLimbDigit" })
        {
            bool good = tagDefOf != null && tagDefOf.GetField(tag) != null;
            Ok("  tag defName '" + tag + "'", good);
        }

        Console.WriteLine("\n=== Harmony injection parameter names: core patches ===");
        MethodInfo learn = AccessTools.Method(typeof(SkillRecord), "Learn");
        Ok("SkillRecord.Learn resolves", learn != null, Params(learn));
        NeedParam("SkillRecord.Learn", learn, "xp");
        NeedParam("SkillRecord.Learn", learn, "direct");
        NeedParam("SkillRecord.Learn", learn, "ignoreLearnRate");

        MethodInfo getLevel = AccessTools.Method(typeof(SkillRecord), "GetLevel");
        NeedParam("SkillRecord.GetLevel", getLevel, "includeAptitudes");

        MethodInfo setLevel = AccessTools.Method(typeof(SkillRecord), "set_Level");
        Ok("SkillRecord.set_Level resolves", setLevel != null, Params(setLevel));
        NeedParam("SkillRecord.set_Level", setLevel, "value");

        Ok("SkillRecord.GetLevelForUI resolves", AccessTools.Method(typeof(SkillRecord), "GetLevelForUI") != null);
        Ok("SkillRecord.get_LevelDescriptor resolves", AccessTools.Method(typeof(SkillRecord), "get_LevelDescriptor") != null);
        Ok("SkillRecord.Interval resolves", AccessTools.Method(typeof(SkillRecord), "Interval") != null);
        Ok("SkillRecord.ExposeData resolves", AccessTools.Method(typeof(SkillRecord), "ExposeData") != null);
        Ok("SkillRecord.levelInt field is public", typeof(SkillRecord).GetField("levelInt") != null);

        MethodInfo qInt = AccessTools.Method(typeof(QualityUtility), "GenerateQualityCreatedByPawn",
            new[] { typeof(int), typeof(bool) });
        Ok("QualityUtility.GenerateQualityCreatedByPawn(int, bool) resolves", qInt != null, Params(qInt));
        NeedParam("GenerateQualityCreatedByPawn(int,bool)", qInt, "relevantSkillLevel");

        MethodInfo qPawn = AccessTools.Method(typeof(QualityUtility), "GenerateQualityCreatedByPawn",
            new[] { typeof(Pawn), typeof(SkillDef), typeof(bool) });
        Ok("QualityUtility.GenerateQualityCreatedByPawn(Pawn, SkillDef, bool) resolves", qPawn != null, Params(qPawn));
        NeedParam("GenerateQualityCreatedByPawn(Pawn,..)", qPawn, "pawn");
        NeedParam("GenerateQualityCreatedByPawn(Pawn,..)", qPawn, "relevantSkill");

        MethodInfo skillDesc = AccessTools.Method(typeof(SkillUI), "GetSkillDescription", new[] { typeof(SkillRecord) });
        Ok("SkillUI.GetSkillDescription resolves", skillDesc != null, Params(skillDesc));
        NeedParam("SkillUI.GetSkillDescription", skillDesc, "sk");

        MethodInfo drawSkill = AccessTools.Method(typeof(SkillUI), "DrawSkill",
            new[] { typeof(SkillRecord), typeof(UnityEngine.Rect), typeof(SkillUI.SkillDrawMode), typeof(string) });
        Ok("SkillUI.DrawSkill(SkillRecord, Rect, SkillDrawMode, string) resolves", drawSkill != null, Params(drawSkill));
        NeedParam("SkillUI.DrawSkill", drawSkill, "skill");
        NeedParam("SkillUI.DrawSkill", drawSkill, "holdingRect");
        NeedParam("SkillUI.DrawSkill", drawSkill, "mode");

        Ok("Pawn.GetGizmos resolves", AccessTools.Method(typeof(Pawn), "GetGizmos") != null);
        Ok("Pawn.ExposeData resolves", AccessTools.Method(typeof(Pawn), "ExposeData") != null);

        Console.WriteLine("\n=== Misc runtime API ===");
        Ok("DamageInfo.SetHitPart exists", AccessTools.Method(typeof(DamageInfo), "SetHitPart") != null);
        Try("ModsConfig.ActiveModsInLoadOrder exists",
            () => AccessTools.Property(typeof(ModsConfig), "ActiveModsInLoadOrder") != null);
        Try("ModMetaData.PackageId exists",
            () => AccessTools.Property(typeof(ModMetaData), "PackageId") != null);
        Ok("PawnCapacityDefOf.Manipulation exists", typeof(PawnCapacityDefOf).GetField("Manipulation") != null);
        Ok("PawnCapacityDefOf.Consciousness exists", typeof(PawnCapacityDefOf).GetField("Consciousness") != null);
        Ok("PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead exists",
           AccessTools.Property(typeof(PawnsFinder), "AllMapsWorldAndTemporary_AliveOrDead") != null);
        Ok("WorldPawns.AllPawnsAliveOrDead exists",
           AccessTools.Property(typeof(RimWorld.Planet.WorldPawns), "AllPawnsAliveOrDead") != null);
        Ok("Caravan.PawnsListForReading exists",
           AccessTools.Property(typeof(RimWorld.Planet.Caravan), "PawnsListForReading") != null);
        Ok("Corpse.InnerPawn exists", AccessTools.Property(typeof(Corpse), "InnerPawn") != null);
        Ok("HediffSet.GetNotMissingParts exists", AccessTools.Method(typeof(HediffSet), "GetNotMissingParts") != null);
        Ok("HediffSet.GetPartHealth exists", AccessTools.Method(typeof(HediffSet), "GetPartHealth") != null);
        Ok("BodyPartRecord.coverageAbsWithChildren exists",
           typeof(BodyPartRecord).GetField("coverageAbsWithChildren") != null);

        Console.WriteLine("\n================================");
        Console.WriteLine("PASS: " + pass + "   FAIL: " + fail + "   SKIP: " + skipped);
        Environment.Exit(fail == 0 ? 0 : 1);
    }
}
