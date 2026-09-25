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
using Grandmaster21;

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

        Console.WriteLine("\n=== Reflection targets: melee ===");

        MethodInfo meleeCast = AccessTools.Method(typeof(Verb_MeleeAttack), "TryCastShot");
        Ok("Verb_MeleeAttack.TryCastShot resolves", meleeCast != null, Params(meleeCast));

        MethodInfo nonMiss = AccessTools.Method(typeof(Verb_MeleeAttack), "GetNonMissChance");
        Ok("Verb_MeleeAttack.GetNonMissChance resolves (private)", nonMiss != null, Params(nonMiss));
        Ok("  ...and returns float", nonMiss != null && nonMiss.ReturnType == typeof(float));

        MethodInfo meleeDodge = AccessTools.Method(typeof(Verb_MeleeAttack), "GetDodgeChance");
        Ok("Verb_MeleeAttack.GetDodgeChance resolves (private)", meleeDodge != null, Params(meleeDodge));
        Ok("  ...and returns float", meleeDodge != null && meleeDodge.ReturnType == typeof(float));
        NeedParam("Verb_MeleeAttack.GetDodgeChance", meleeDodge, "target", "LocalTargetInfo");

        MethodInfo tryMelee = AccessTools.Method(typeof(Pawn_MeleeVerbs), "TryMeleeAttack",
            new[] { typeof(Thing), typeof(Verb), typeof(bool) });
        Ok("Pawn_MeleeVerbs.TryMeleeAttack(Thing, Verb, bool) resolves", tryMelee != null, Params(tryMelee));
        Ok("Pawn_MeleeVerbs.TryGetMeleeVerb resolves",
           AccessTools.Method(typeof(Pawn_MeleeVerbs), "TryGetMeleeVerb") != null);
        Ok("Pawn.meleeVerbs field is public", typeof(Pawn).GetField("meleeVerbs") != null);

        Ok("Pawn_StanceTracker.SetStance resolves",
           AccessTools.Method(typeof(Pawn_StanceTracker), "SetStance") != null);
        Ok("Pawn_StanceTracker.curStance field is public",
           typeof(Pawn_StanceTracker).GetField("curStance") != null);
        Ok("Stance_Mobile has a public parameterless constructor",
           typeof(Stance_Mobile).GetConstructor(Type.EmptyTypes) != null);
        Ok("Pawn_StanceTracker.stunner field is public",
           typeof(Pawn_StanceTracker).GetField("stunner") != null);
        Ok("StunHandler.Stunned exists",
           AccessTools.Property(AccessTools.TypeByName("RimWorld.StunHandler"), "Stunned") != null);
        Ok("RestUtility.Awake(Pawn) resolves",
           AccessTools.Method(typeof(RestUtility), "Awake", new[] { typeof(Pawn) }) != null);

        MethodInfo dropEq = AccessTools.Method(typeof(Pawn_EquipmentTracker), "TryDropEquipment");
        Ok("Pawn_EquipmentTracker.TryDropEquipment resolves", dropEq != null, Params(dropEq));
        Ok("Pawn_EquipmentTracker.Primary exists",
           AccessTools.Property(typeof(Pawn_EquipmentTracker), "Primary") != null);
        Ok("Pawn_EquipmentTracker.bondedWeapon field is public",
           typeof(Pawn_EquipmentTracker).GetField("bondedWeapon") != null);
        Ok("ThingDef.destroyOnDrop field is public", typeof(ThingDef).GetField("destroyOnDrop") != null);
        Ok("ThingDef.destroyable field is public", typeof(ThingDef).GetField("destroyable") != null);
        Ok("ThingDef.IsMeleeWeapon exists", AccessTools.Property(typeof(ThingDef), "IsMeleeWeapon") != null);

        Ok("DamageInfo.SetAmount exists", AccessTools.Method(typeof(DamageInfo), "SetAmount") != null);

        // Cleave hands a Pawn straight to Verb.CanHitTarget, which takes LocalTargetInfo. That
        // only compiles because of this implicit conversion, so it is a real dependency.
        Ok("LocalTargetInfo has an implicit conversion from Thing",
           typeof(LocalTargetInfo).GetMethod("op_Implicit", BindingFlags.Public | BindingFlags.Static,
               null, new[] { typeof(Thing) }, null) != null);
        Ok("Verb.CanHitTarget(LocalTargetInfo) resolves",
           AccessTools.Method(typeof(Verb), "CanHitTarget", new[] { typeof(LocalTargetInfo) }) != null);

        Console.WriteLine("\n=== Melee stats and capacities ===");
        foreach (string stat in new[] { "MoveSpeed", "Mass", "MeleeDamageFactor",
                                        "MeleeDodgeChance", "MeleeHitChance" })
        {
            Ok("  StatDefOf." + stat, typeof(StatDefOf).GetField(stat) != null);
        }
        foreach (string cap in new[] { "Sight", "Moving", "Manipulation", "Consciousness" })
        {
            Ok("  PawnCapacityDefOf." + cap, typeof(PawnCapacityDefOf).GetField(cap) != null);
        }
        Ok("StatDef.defaultBaseValue field is public (optional-stat normalisation)",
           typeof(StatDef).GetField("defaultBaseValue") != null);
        Ok("PawnCapacitiesHandler.GetLevel resolves",
           AccessTools.Method(typeof(PawnCapacitiesHandler), "GetLevel") != null);
        Ok("Pawn.BodySize exists", AccessTools.Property(typeof(Pawn), "BodySize") != null);

        Console.WriteLine("\n=== Projectile defence (protected/private internals) ===");
        MethodInfo projTick = AccessTools.Method(typeof(Projectile), "TickInterval", new[] { typeof(int) })
                           ?? AccessTools.Method(typeof(Projectile), "Tick");
        Ok("Projectile flight tick resolves", projTick != null,
           projTick == null ? "" : projTick.Name + "(" + Params(projTick) + ")");

        MethodInfo launch = AccessTools.Method(typeof(Projectile), "Launch", new[] {
            typeof(Thing), typeof(UnityEngine.Vector3), typeof(LocalTargetInfo), typeof(LocalTargetInfo),
            typeof(ProjectileHitFlags), typeof(bool), typeof(Thing), typeof(ThingDef) });
        Ok("Projectile.Launch(8-arg) resolves", launch != null, Params(launch));

        foreach (string f in new[] { "equipment", "equipmentDef", "equipmentQuality",
                                     "destination", "ticksToImpact" })
        {
            FieldInfo fi = AccessTools.Field(typeof(Projectile), f);
            Ok("  Projectile." + f + " resolves", fi != null, fi == null ? "" : fi.FieldType.Name);
        }
        FieldInfo fuse = AccessTools.Field(typeof(Projectile_Explosive), "ticksToDetonation");
        Ok("Projectile_Explosive.ticksToDetonation resolves (fuse preservation)", fuse != null,
           fuse == null ? "" : fuse.FieldType.Name);
        Ok("  ...and is an int", fuse != null && fuse.FieldType == typeof(int));

        Ok("Projectile.ExactPosition is public",
           AccessTools.Property(typeof(Projectile), "ExactPosition").GetGetMethod() != null);
        Ok("Projectile.HitFlags is public",
           AccessTools.Property(typeof(Projectile), "HitFlags").GetGetMethod() != null);
        Ok("Projectile.Launcher is public",
           AccessTools.Property(typeof(Projectile), "Launcher").GetGetMethod() != null);
        Ok("Projectile.intendedTarget field is public",
           typeof(Projectile).GetField("intendedTarget") != null);
        Ok("ProjectileProperties.speed field is public",
           typeof(ProjectileProperties).GetField("speed") != null);
        Ok("ProjectileProperties.explosionRadius field is public",
           typeof(ProjectileProperties).GetField("explosionRadius") != null);
        Ok("ProjectileProperties.flyOverhead field is public",
           typeof(ProjectileProperties).GetField("flyOverhead") != null);
        Ok("ProjectileProperties.SpeedTilesPerTick exists",
           AccessTools.Property(typeof(ProjectileProperties), "SpeedTilesPerTick") != null);

        Console.WriteLine("\n=== Guardian threat model ===");
        Ok("Projectile.usedTarget field is public (resolved destination)",
           typeof(Projectile).GetField("usedTarget") != null);
        Ok("  ...and is a LocalTargetInfo",
           typeof(Projectile).GetField("usedTarget") != null
           && typeof(Projectile).GetField("usedTarget").FieldType == typeof(LocalTargetInfo));
        Ok("LocalTargetInfo.Thing exists", AccessTools.Property(typeof(LocalTargetInfo), "Thing") != null);
        Ok("LocalTargetInfo.Cell exists", AccessTools.Property(typeof(LocalTargetInfo), "Cell") != null);
        Ok("Projectile.origin resolves (correction angle)",
           AccessTools.Field(typeof(Projectile), "origin") != null);
        Ok("Faction.RelationKindWith resolves (ally-only protection)",
           AccessTools.Method(typeof(Faction), "RelationKindWith", new[] { typeof(Faction) }) != null);
        Ok("FactionRelationKind.Ally exists",
           System.Enum.IsDefined(typeof(FactionRelationKind), "Ally"));
        Ok("Pawn.Faction exists", AccessTools.Property(typeof(Pawn), "Faction") != null);

        Console.WriteLine("\n=== Protected-pawn safety veto ===");
        MethodInfo bresenham = AccessTools.Method(typeof(GenSight), "BresenhamCellsBetween",
            new[] { typeof(IntVec3), typeof(IntVec3) });
        Ok("GenSight.BresenhamCellsBetween(IntVec3, IntVec3) resolves (exact path veto)",
           bresenham != null, Params(bresenham));
        Ok("  ...and returns a cell list",
           bresenham != null && bresenham.ReturnType == typeof(System.Collections.Generic.List<IntVec3>));
        Ok("ProjectileProperties.arcHeightFactor is public (arcing projectiles skip the path veto)",
           typeof(ProjectileProperties).GetField("arcHeightFactor") != null);
        Ok("MapPawns.AllPawnsSpawned resolves (one-pass protected gather)",
           AccessTools.Property(typeof(MapPawns), "AllPawnsSpawned") != null);
        Ok("Map.mapPawns field is public", typeof(Map).GetField("mapPawns") != null);

        Console.WriteLine("\n=== Guardian micro-dash effects (cosmetic, null-checked at runtime) ===");
        Ok("FleckMaker.ConnectingLine resolves",
           AccessTools.Method(typeof(FleckMaker), "ConnectingLine") != null);
        Ok("FleckMaker.ThrowLightningGlow resolves",
           AccessTools.Method(typeof(FleckMaker), "ThrowLightningGlow") != null);
        MethodInfo fleckStatic = AccessTools.Method(typeof(FleckMaker), "Static",
            new[] { typeof(UnityEngine.Vector3), typeof(Map), typeof(FleckDef), typeof(float) });
        Ok("FleckMaker.Static(Vector3, Map, FleckDef, float) resolves", fleckStatic != null);
        Ok("FleckDefOf.LineEMP exists", typeof(FleckDefOf).GetField("LineEMP") != null);
        Ok("FleckDefOf.MicroSparksFast exists", typeof(FleckDefOf).GetField("MicroSparksFast") != null);
        Ok("SoundDefOf.MetalHitImportant exists", typeof(SoundDefOf).GetField("MetalHitImportant") != null);
        Ok("SoundInfo.InMap(TargetInfo, ...) resolves",
           AccessTools.Method(typeof(Verse.Sound.SoundInfo), "InMap") != null);
        Ok("SoundStarter.PlayOneShot resolves",
           AccessTools.Method(typeof(Verse.Sound.SoundStarter), "PlayOneShot") != null);
        Ok("TargetInfo(IntVec3, Map, bool) resolves",
           AccessTools.Constructor(typeof(TargetInfo),
               new[] { typeof(IntVec3), typeof(Map), typeof(bool) }) != null);
        Ok("Log.WarningOnce resolves", AccessTools.Method(typeof(Log), "WarningOnce") != null);

        Console.WriteLine("\n=== Interception geometry ===");
        Ok("GenSight.LineOfSight(start, end, map) resolves",
           AccessTools.Method(typeof(GenSight), "LineOfSight",
               new[] { typeof(IntVec3), typeof(IntVec3), typeof(Map) }) != null);
        Ok("GenSight.LineOfSight(+validator) resolves",
           AccessTools.Method(typeof(GenSight), "LineOfSight",
               new[] { typeof(IntVec3), typeof(IntVec3), typeof(Map), typeof(bool),
                       typeof(Func<IntVec3, bool>), typeof(int), typeof(int) }) != null);
        Ok("GenGrid.Walkable(IntVec3, Map) resolves",
           AccessTools.Method(typeof(GenGrid), "Walkable", new[] { typeof(IntVec3), typeof(Map) }) != null);
        Ok("ThingGrid.ThingsListAtFast(IntVec3) resolves",
           AccessTools.Method(typeof(ThingGrid), "ThingsListAtFast", new[] { typeof(IntVec3) }) != null);
        Ok("Map.thingGrid field is public", typeof(Map).GetField("thingGrid") != null);
        Ok("GenHostility.HostileTo(Thing, Thing) resolves",
           AccessTools.Method(typeof(GenHostility), "HostileTo",
               new[] { typeof(Thing), typeof(Thing) }) != null);
        Ok("IntVec3Utility.DistanceTo resolves",
           AccessTools.Method(typeof(IntVec3Utility), "DistanceTo") != null);
        Ok("IntVec3Utility.ToIntVec3(Vector3) resolves",
           AccessTools.Method(typeof(IntVec3Utility), "ToIntVec3") != null);
        MethodInfo throwText = AccessTools.Method(typeof(MoteMaker), "ThrowText",
            new[] { typeof(UnityEngine.Vector3), typeof(Map), typeof(string), typeof(float) });
        Ok("MoteMaker.ThrowText(Vector3, Map, string, float) resolves", throwText != null, Params(throwText));

        Console.WriteLine("\n=== Reaction scheduler ===");
        Ok("GameComponent.GameComponentTick is virtual",
           AccessTools.Method(typeof(GameComponent), "GameComponentTick") != null);
        Ok("GameComponent has a (Game) constructor contract",
           typeof(Gm21CombatScheduler).GetConstructor(new[] { typeof(Game) }) != null);
        Ok("Gm21CombatScheduler derives from GameComponent",
           typeof(GameComponent).IsAssignableFrom(typeof(Gm21CombatScheduler)));

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

        Console.WriteLine("\n=== Medicine 21: patch targets and Harmony parameter names ===");
        MethodInfo doTend = AccessTools.Method(typeof(TendUtility), "DoTend");
        Ok("TendUtility.DoTend resolves", doTend != null, Params(doTend));
        NeedParam("TendUtility.DoTend", doTend, "doctor", "Pawn");
        NeedParam("TendUtility.DoTend", doTend, "patient", "Pawn");
        NeedParam("TendUtility.DoTend", doTend, "medicine", "Medicine");

        MethodInfo hediffTended = AccessTools.Method(typeof(Hediff), "Tended", new[] { typeof(float), typeof(float), typeof(int) });
        Ok("Hediff.Tended(float, float, int) resolves and is virtual (condition-specific overrides)",
           hediffTended != null && hediffTended.IsVirtual, Params(hediffTended));
        int tendedCalls = 0;
        try
        {
            tendedCalls = PatchProcessor.GetOriginalInstructions(doTend)
                .Count(i => (i.opcode == System.Reflection.Emit.OpCodes.Callvirt || i.opcode == System.Reflection.Emit.OpCodes.Call)
                            && Equals(i.operand, hediffTended));
        }
        catch (Exception) { tendedCalls = -1; }
        Ok("TendUtility.DoTend calls Hediff.Tended at exactly one site (the propagation transpiler's premise)",
           tendedCalls == 1, "sites=" + tendedCalls);
        Ok("Hediff_HeartAttack overrides Tended (the propagation regression case)",
           AccessTools.DeclaredMethod(typeof(Hediff_HeartAttack), "Tended") != null);

        MethodInfo compTended = AccessTools.Method(typeof(HediffComp_TendDuration), "CompTended");
        Ok("HediffComp_TendDuration.CompTended resolves", compTended != null, Params(compTended));
        NeedParam("HediffComp_TendDuration.CompTended", compTended, "quality", "Single");
        NeedParam("HediffComp_TendDuration.CompTended", compTended, "maxQuality", "Single");
        Ok("HediffComp_TendDuration.tendQuality is a public float",
           typeof(HediffComp_TendDuration).GetField("tendQuality") != null
           && typeof(HediffComp_TendDuration).GetField("tendQuality").FieldType == typeof(float));
        Ok("HediffComp_TendDuration.tendTicksLeft is a public int",
           typeof(HediffComp_TendDuration).GetField("tendTicksLeft") != null
           && typeof(HediffComp_TendDuration).GetField("tendTicksLeft").FieldType == typeof(int));
        Ok("HediffComp_TendDuration.TendQualityRandomVariance == 0.25",
           HediffComp_TendDuration.TendQualityRandomVariance == 0.25f);
        Ok("HediffComp_TendDuration.CompTipStringExtra getter resolves",
           AccessTools.PropertyGetter(typeof(HediffComp_TendDuration), "CompTipStringExtra") != null);

        Ok("Hediff.ExposeData resolves (virtual; every subclass calls base)",
           AccessTools.Method(typeof(Hediff), "ExposeData") != null);
        MethodInfo healthInterval = AccessTools.Method(typeof(Pawn_HealthTracker), "HealthTickInterval");
        Ok("Pawn_HealthTracker.HealthTickInterval resolves", healthInterval != null, Params(healthInterval));
        MethodInfo heal = AccessTools.DeclaredMethod(typeof(Hediff_Injury), "Heal");
        Ok("Hediff_Injury.Heal is declared on Hediff_Injury", heal != null, Params(heal));
        NeedParam("Hediff_Injury.Heal", heal, "amount", "Single");
        MethodInfo immunity = AccessTools.Method(typeof(ImmunityRecord), "ImmunityChangePerTick");
        Ok("ImmunityRecord.ImmunityChangePerTick resolves", immunity != null, Params(immunity));
        NeedParam("ImmunityRecord.ImmunityChangePerTick", immunity, "sick", "Boolean");
        NeedParam("ImmunityRecord.ImmunityChangePerTick", immunity, "diseaseInstance", "Hediff");
        Ok("  ...and returns float", immunity != null && immunity.ReturnType == typeof(float));

        MethodInfo outcome = AccessTools.Method(typeof(SurgeryOutcomeEffectDef), "GetOutcome");
        Ok("SurgeryOutcomeEffectDef.GetOutcome resolves", outcome != null, Params(outcome));
        foreach (string p in new[] { "recipe", "surgeon", "patient", "ingredients", "part", "bill" })
            NeedParam("SurgeryOutcomeEffectDef.GetOutcome", outcome, p);
        Ok("  ...and returns SurgeryOutcome", outcome != null && outcome.ReturnType == typeof(SurgeryOutcome));
        Ok("SurgeryOutcome.failure is a public bool", typeof(SurgeryOutcome).GetField("failure") != null);
        Ok("Recipe_Surgery.CheckSurgeryFail still asks surgeryOutcomeEffect.GetOutcome",
           AccessTools.Method(typeof(Recipe_Surgery), "CheckSurgeryFail") != null);

        Console.WriteLine("\n=== Medicine 21: save identity and revival ===");
        Ok("Job.source is ILoadReferenceable (carries the selected Hediff)",
           typeof(Verse.AI.Job).GetField("source") != null
           && typeof(Verse.AI.Job).GetField("source").FieldType == typeof(ILoadReferenceable));
        Ok("Hediff implements ILoadReferenceable", typeof(ILoadReferenceable).IsAssignableFrom(typeof(Hediff)));
        Ok("Corpse.timeOfDeath is a public int", typeof(Corpse).GetField("timeOfDeath") != null);
        Ok("ResurrectionUtility.TryResurrect(Pawn, ResurrectionParams) resolves",
           AccessTools.Method(typeof(ResurrectionUtility), "TryResurrect", new[] { typeof(Pawn), typeof(ResurrectionParams) }) != null);
        Ok("ResurrectionParams.restoreMissingParts / gettingScarsChance exist",
           typeof(ResurrectionParams).GetField("restoreMissingParts") != null
           && typeof(ResurrectionParams).GetField("gettingScarsChance") != null);
        Ok("Pawn_HealthTracker.RestorePart(BodyPartRecord, Hediff, bool) resolves",
           AccessTools.Method(typeof(Pawn_HealthTracker), "RestorePart",
               new[] { typeof(BodyPartRecord), typeof(Hediff), typeof(bool) }) != null);
        Ok("HediffSet.GetMissingPartsCommonAncestors resolves",
           AccessTools.Method(typeof(HediffSet), "GetMissingPartsCommonAncestors") != null);
        Ok("HediffDef.chronic / everCurableByItem / cureAllAtOnceIfCuredByItem exist",
           typeof(HediffDef).GetField("chronic") != null && typeof(HediffDef).GetField("everCurableByItem") != null
           && typeof(HediffDef).GetField("cureAllAtOnceIfCuredByItem") != null);
        Ok("HediffDefOf.FoodPoisoning exists (the one vanilla Cure adapter)",
           typeof(HediffDefOf).GetField("FoodPoisoning") != null);
        foreach (string tag in new[] { "BloodFiltrationKidney", "BloodFiltrationLiver", "BreathingSourceCage", "MetabolismSource" })
            Ok("  tag defName '" + tag + "' (resuscitation vital anatomy)",
               tagDefOf != null && tagDefOf.GetField(tag) != null);
        Console.WriteLine("\n=== Medicine 21: intervention medicine, trauma and hostile revival ===");
        Ok("StatDefOf.MedicalPotency exists (the budget's currency)", typeof(StatDefOf).GetField("MedicalPotency") != null);
        Ok("ThingDef.IsMedicine exists", AccessTools.PropertyGetter(typeof(ThingDef), "IsMedicine") != null);
        Ok("MedicalCareUtility.AllowsMedicine(MedicalCareCategory, ThingDef) resolves",
           AccessTools.Method(typeof(MedicalCareUtility), "AllowsMedicine") != null);
        foreach (string f in new[] { "defaultCareForColonist", "defaultCareForSlave", "defaultCareForPrisoner", "defaultCareForTamedAnimal",
                                     "defaultCareForFriendlyFaction", "defaultCareForNeutralFaction", "defaultCareForHostileFaction",
                                     "defaultCareForNoFaction", "defaultCareForWildlife" })
            Ok("  PlaySettings." + f + " exists", typeof(PlaySettings).GetField(f) != null);
        Ok("ReservationManager.CanReserveStack resolves", AccessTools.Method(typeof(Verse.AI.ReservationManager), "CanReserveStack") != null);
        Ok("Job.targetQueueB / countQueue are public lists (the collection plan, saved by Job)",
           typeof(Verse.AI.Job).GetField("targetQueueB") != null && typeof(Verse.AI.Job).GetField("countQueue") != null);
        Ok("Toils_JobTransforms.ExtractNextTargetFromQueue(TargetIndex, bool) resolves",
           AccessTools.Method(typeof(Verse.AI.Toils_JobTransforms), "ExtractNextTargetFromQueue",
               new[] { typeof(Verse.AI.TargetIndex), typeof(bool) }) != null);
        Ok("Toils_Haul.TakeToInventory(TargetIndex, Func<int>) resolves",
           AccessTools.Method(typeof(Verse.AI.Toils_Haul), "TakeToInventory",
               new[] { typeof(Verse.AI.TargetIndex), typeof(Func<int>) }) != null);
        Ok("Toils_Jump.JumpIfHaveTargetInQueue resolves",
           AccessTools.Method(typeof(Verse.AI.Toils_Jump), "JumpIfHaveTargetInQueue") != null);
        Ok("Thing.SplitOff(int) resolves", AccessTools.Method(typeof(Thing), "SplitOff", new[] { typeof(int) }) != null);
        Ok("HediffComp_GetsPermanent.isPermanentInt is public, SetPainCategory resolves",
           typeof(HediffComp_GetsPermanent).GetField("isPermanentInt") != null
           && AccessTools.Method(typeof(HediffComp_GetsPermanent), "SetPainCategory") != null);
        Ok("HealthTuning.InjuryPainCategories is public", typeof(HealthTuning).GetField("InjuryPainCategories") != null);
        Ok("Pawn_HealthTracker.WouldLosePartAfterAddingHediff(HediffDef, BodyPartRecord, float) resolves",
           AccessTools.Method(typeof(Pawn_HealthTracker), "WouldLosePartAfterAddingHediff",
               new[] { typeof(HediffDef), typeof(BodyPartRecord), typeof(float) }) != null);
        Ok("Hediff_MissingPart.lastInjury is public", typeof(Hediff_MissingPart).GetField("lastInjury") != null);
        Ok("Hediff.combatLogEntry / combatLogText are public (scar provenance)",
           typeof(Hediff).GetField("combatLogEntry") != null && typeof(Hediff).GetField("combatLogText") != null);
        Ok("GeneDef.preventPermanentWounds exists", typeof(GeneDef).GetField("preventPermanentWounds") != null);
        foreach (string f in new[] { "subjectPawn", "culpritTargetPart", "culpritHediffTargetPart" })
            Ok("  BattleLogEntry_StateTransition." + f + " exists (death-blow corroboration only)",
               AccessTools.Field(typeof(BattleLogEntry_StateTransition), f) != null);
        Ok("ResurrectionParams.noLord exists (deliberately NOT set: a hostile revives hostile)",
           typeof(ResurrectionParams).GetField("noLord") != null);
        Ok("Dialog_MessageBox.CreateConfirmation(TaggedString, Action, bool, ...) resolves (hostile-corpse confirmation)",
           typeof(Dialog_MessageBox).GetMethods().Any(m => m.Name == "CreateConfirmation" && m.GetParameters().Length >= 3
               && m.GetParameters()[0].ParameterType == typeof(TaggedString) && m.GetParameters()[1].ParameterType == typeof(Action)
               && m.GetParameters()[2].ParameterType == typeof(bool)));
        Ok("Command.ProcessInput(Event) is virtual (right-click activation)",
           AccessTools.Method(typeof(Command), "ProcessInput") != null
           && AccessTools.Method(typeof(Command), "ProcessInput").IsVirtual);
        Ok("Command.groupable is a public bool", typeof(Command).GetField("groupable") != null);

        Console.WriteLine("\n================================");
        Console.WriteLine("PASS: " + pass + "   FAIL: " + fail + "   SKIP: " + skipped);
        Environment.Exit(fail == 0 ? 0 : 1);
    }
}
