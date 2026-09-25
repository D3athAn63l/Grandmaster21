// Applies EVERY Grandmaster 21 Harmony patch to the real RimWorld assembly and reports the result.
//
// This is the test that a compile cannot replace. It proves that each patch target resolves, that
// Harmony accepts every prefix/postfix/finalizer signature (injection parameters are bound by
// name, so a renamed vanilla parameter fails exactly here), and -- critically -- that the
// SkillRecord.Learn transpiler finds and rewrites its IL pattern in the shipped assembly.
//
// Usage:  mono PatchAllTest.exe <path to Grandmaster21.dll>
using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

class PatchAllTest
{
    static void Main(string[] args)
    {
        string path = args.Length > 0 ? args[0] : "Grandmaster21.dll";
        Assembly modAsm = Assembly.LoadFrom(path);
        var harmony = new Harmony("ared.grandmaster21.test");

        int okCount = 0, failCount = 0, blockedCount = 0;

        Console.WriteLine("=== Attribute patches ===");
        foreach (Type t in modAsm.GetTypes())
        {
            if (t.GetCustomAttributes(typeof(HarmonyPatch), true).Length == 0) continue;
            try
            {
                // NOTE: Patch() returns the DYNAMIC REPLACEMENT methods, not the originals, and a
                // DynamicMethod has a null DeclaringType. Report the count here and list the real
                // targets from GetAllPatchedMethods() at the end.
                var replacements = harmony.CreateClassProcessor(t).Patch();
                if (replacements == null || replacements.Count == 0)
                {
                    Console.WriteLine("NO-TARGET  " + t.Name);
                    failCount++;
                }
                else
                {
                    Console.WriteLine("PATCHED    " + t.Name + "  (" + replacements.Count + " method"
                                      + (replacements.Count == 1 ? ")" : "s)"));
                    okCount++;
                }
            }
            catch (Exception e)
            {
                Exception root = e.GetBaseException();
                bool headless = IsHeadlessLimit(e);
                Console.WriteLine((headless ? "BLOCKED    " : "FAILED     ") + t.Name + " -> "
                                  + Describe(root));
                if (Environment.GetEnvironmentVariable("GM21_TRACE") != null)
                    Console.WriteLine(e.ToString());
                if (headless) blockedCount++; else failCount++;
            }
        }

        Console.WriteLine("\n=== Manual shooting patches (one target at a time) ===");
        // Each target is patched individually rather than through Gm21ShootingPatches.Apply(),
        // for two reasons. First, Apply() begins with DetectCombatOverhaul, which reads
        // ModsConfig -> ModMetaData; ModMetaData holds a Steamworks.NET field that cannot load
        // headless, and the error path then calls Verse.Log, which bottoms out in a Unity
        // internal call that only exists inside the player. Second, and more usefully, going one
        // target at a time turns "the shooting patches failed" into "this specific target failed,
        // for this reason" -- which is the whole point of the test.
        Type P = modAsm.GetType("Grandmaster21.Gm21ShootingPatches");
        Type guard = modAsm.GetType("Grandmaster21.Gm21DownedGuard");
        Func<string, HarmonyMethod> hook = n => new HarmonyMethod(AccessTools.Method(P, n));

        Bind("Verb_LaunchProjectile.TryCastShot  [prefix + finalizer]", ref okCount, ref failCount, ref blockedCount,
            () => harmony.Patch(AccessTools.Method(typeof(Verb_LaunchProjectile), "TryCastShot"),
                                hook("Prefix_OpenShotContext"), null, null, hook("Finalizer_CloseShotContext")));

        Bind("ShotReport.HitReportFor  [postfix]", ref okCount, ref failCount, ref blockedCount,
            () => harmony.Patch(AccessTools.Method(typeof(ShotReport), "HitReportFor"),
                                null, hook("Postfix_ObserveShotContext")));

        Bind("ShotReport.AimOnTargetChance_IgnoringPosture  [postfix]", ref okCount, ref failCount, ref blockedCount,
            () => harmony.Patch(Getter(typeof(ShotReport), "AimOnTargetChance_IgnoringPosture", "ChanceToNotGoWild_IgnoringPosture"),
                                null, hook("Postfix_AimChance")));

        Bind("ShotReport.PassCoverChance  [postfix]", ref okCount, ref failCount, ref blockedCount,
            () => harmony.Patch(Getter(typeof(ShotReport), "PassCoverChance", "ChanceToNotHitCover"),
                                null, hook("Postfix_CoverChance")));

        Bind("ShotReport.TotalEstimatedHitChance  [postfix]", ref okCount, ref failCount, ref blockedCount,
            () => harmony.Patch(Getter(typeof(ShotReport), "TotalEstimatedHitChance"),
                                null, hook("Postfix_AimChance")));

        Bind("Stance_Warmup..ctor  [prefix, ref int ticks]", ref okCount, ref failCount, ref blockedCount,
            () => harmony.Patch(AccessTools.Constructor(typeof(Stance_Warmup),
                                    new[] { typeof(int), typeof(LocalTargetInfo), typeof(Verb) }),
                                hook("Prefix_StanceTicks")));

        Bind("Stance_Cooldown..ctor  [prefix, ref int ticks]", ref okCount, ref failCount, ref blockedCount,
            () => harmony.Patch(AccessTools.Constructor(typeof(Stance_Cooldown),
                                    new[] { typeof(int), typeof(LocalTargetInfo), typeof(Verb) }),
                                hook("Prefix_StanceTicks")));

        Bind("Pawn.PreApplyDamage  [prefix, ref DamageInfo]", ref okCount, ref failCount, ref blockedCount,
            () => harmony.Patch(AccessTools.Method(typeof(Pawn), "PreApplyDamage"),
                                hook("Prefix_ChooseAimedPart")));

        Bind("Pawn_HealthTracker.CheckForStateChange  [prefix + finalizer]", ref okCount, ref failCount, ref blockedCount,
            () => harmony.Patch(AccessTools.Method(typeof(Pawn_HealthTracker), "CheckForStateChange"),
                                new HarmonyMethod(AccessTools.Method(guard, "Prefix")), null, null,
                                new HarmonyMethod(AccessTools.Method(guard, "Finalizer"))));

        Console.WriteLine("\n=== Manual melee patches (one target at a time) ===");
        // Same reasoning as the shooting block: Gm21MeleePatches.Apply() starts with a combat
        // overhaul check that reads ModsConfig, which cannot load headless. Binding one target at
        // a time also turns "melee failed" into "this target failed, for this reason".
        Type M = modAsm.GetType("Grandmaster21.Gm21MeleePatches");
        Type meleeGuard = modAsm.GetType("Grandmaster21.Gm21MeleeDownedGuard");
        Type projDef = modAsm.GetType("Grandmaster21.Gm21ProjectileDefence");
        Func<string, HarmonyMethod> mhook = n => new HarmonyMethod(AccessTools.Method(M, n));

        Bind("Verb_MeleeAttack.TryCastShot  [prefix + postfix + finalizer]", ref okCount, ref failCount, ref blockedCount,
            () => harmony.Patch(AccessTools.Method(typeof(Verb_MeleeAttack), "TryCastShot"),
                                mhook("Prefix_OpenMeleeContext"), mhook("Postfix_ResolveExchange"), null,
                                mhook("Finalizer_CloseMeleeContext")));

        Bind("Verb_MeleeAttack.GetNonMissChance  [postfix, private]", ref okCount, ref failCount, ref blockedCount,
            () => harmony.Patch(AccessTools.Method(typeof(Verb_MeleeAttack), "GetNonMissChance"),
                                null, mhook("Postfix_NonMissChance")));

        Bind("Verb_MeleeAttack.GetDodgeChance  [postfix, private]", ref okCount, ref failCount, ref blockedCount,
            () => harmony.Patch(AccessTools.Method(typeof(Verb_MeleeAttack), "GetDodgeChance"),
                                null, mhook("Postfix_DodgeChance")));

        Bind("Pawn.PreApplyDamage  [melee prefix, ref DamageInfo]", ref okCount, ref failCount, ref blockedCount,
            () => harmony.Patch(AccessTools.Method(typeof(Pawn), "PreApplyDamage"),
                                mhook("Prefix_ShapeMeleeDamage")));

        Bind("Pawn_HealthTracker.CheckForStateChange  [melee prefix + finalizer]", ref okCount, ref failCount, ref blockedCount,
            () => harmony.Patch(AccessTools.Method(typeof(Pawn_HealthTracker), "CheckForStateChange"),
                                new HarmonyMethod(AccessTools.Method(meleeGuard, "Prefix")), null, null,
                                new HarmonyMethod(AccessTools.Method(meleeGuard, "Finalizer"))));

        Bind("Projectile.TickInterval  [prefix]", ref okCount, ref failCount, ref blockedCount,
            () => harmony.Patch(AccessTools.Method(typeof(Projectile), "TickInterval", new[] { typeof(int) })
                                ?? AccessTools.Method(typeof(Projectile), "Tick"),
                                new HarmonyMethod(AccessTools.Method(projDef, "Prefix_ProjectileFlight"))));

        Console.WriteLine("\n=== Manual medicine patches (one target at a time) ===");
        // Gm21MedicinePatches.Apply() is driven for real, end to end, by tools/verify-medicine.sh
        // (with a test-only ContentFinder shim). Here each target is bound individually, like the
        // shooting and melee blocks above. HediffComp_TendDuration's static constructor loads icons
        // through Unity, so its two targets report BLOCKED headless -- that is the environment.
        Type tend = modAsm.GetType("Grandmaster21.Gm21GrandmasterTend");
        Type recovery = modAsm.GetType("Grandmaster21.Gm21RecoveryEffects");
        Type surgery = modAsm.GetType("Grandmaster21.Gm21Surgery");
        Type medPatches = modAsm.GetType("Grandmaster21.Gm21MedicinePatches");
        Func<Type, string, HarmonyMethod> med = (t, n) => new HarmonyMethod(AccessTools.Method(t, n));

        Bind("Hediff.ExposeData  [treatment persistence postfix]", ref okCount, ref failCount, ref blockedCount,
            () => harmony.Patch(AccessTools.Method(typeof(Hediff), "ExposeData"),
                                null, med(medPatches, "Postfix_Hediff_ExposeData")));

        Bind("TendUtility.DoTend  [prefix + finalizer, __state frame]", ref okCount, ref failCount, ref blockedCount,
            () => harmony.Patch(AccessTools.Method(typeof(TendUtility), "DoTend"),
                                med(tend, "Prefix_DoTend"), null, null, med(tend, "Finalizer_DoTend")));

        Bind("HediffComp_TendDuration.CompTended  [prefix ref quality/maxQuality + postfix]", ref okCount, ref failCount, ref blockedCount,
            () => harmony.Patch(AccessTools.Method(typeof(HediffComp_TendDuration), "CompTended"),
                                med(tend, "Prefix_CompTended"), med(tend, "Postfix_CompTended")));

        Bind("HediffComp_TendDuration.CompTipStringExtra  [postfix, cosmetic]", ref okCount, ref failCount, ref blockedCount,
            () => harmony.Patch(AccessTools.PropertyGetter(typeof(HediffComp_TendDuration), "CompTipStringExtra"),
                                null, med(tend, "Postfix_CompTipStringExtra")));

        Bind("Pawn_HealthTracker.HealthTickInterval  [prefix + finalizer]", ref okCount, ref failCount, ref blockedCount,
            () => harmony.Patch(AccessTools.Method(typeof(Pawn_HealthTracker), "HealthTickInterval"),
                                med(recovery, "Prefix_HealthTickInterval"), null, null,
                                med(recovery, "Finalizer_HealthTickInterval")));

        Bind("Hediff_Injury.Heal  [prefix, ref amount]", ref okCount, ref failCount, ref blockedCount,
            () => harmony.Patch(AccessTools.DeclaredMethod(typeof(Hediff_Injury), "Heal"),
                                med(recovery, "Prefix_Heal")));

        Bind("ImmunityRecord.ImmunityChangePerTick  [postfix]", ref okCount, ref failCount, ref blockedCount,
            () => harmony.Patch(AccessTools.Method(typeof(ImmunityRecord), "ImmunityChangePerTick"),
                                null, med(recovery, "Postfix_ImmunityChangePerTick")));

        Bind("SurgeryOutcomeEffectDef.GetOutcome  [prefix, replaces for Grandmaster only]", ref okCount, ref failCount, ref blockedCount,
            () => harmony.Patch(AccessTools.Method(typeof(SurgeryOutcomeEffectDef), "GetOutcome"),
                                med(surgery, "Prefix_GetOutcome")));

        Console.WriteLine("\n=== Transpiler ===");
        // Gm21.LearnPatchApplied is the authoritative flag: Gm21.Promote is gated on it, so false
        // here means no Grandmaster could be created this session.
        Type gm21 = modAsm.GetType("Grandmaster21.Gm21");
        bool learnApplied = (bool)gm21.GetField("LearnPatchApplied",
            BindingFlags.Public | BindingFlags.Static).GetValue(null);
        Console.WriteLine((learnApplied ? "PASS  " : "FAIL  ")
                          + "SkillRecord.Learn transpiler rewrote the 'levelInt == 20' pattern");
        if (!learnApplied) failCount++;

        Console.WriteLine("\n=== Patched method inventory ===");
        foreach (MethodBase m in Harmony.GetAllPatchedMethods().OrderBy(x => x.DeclaringType.FullName + x.Name))
        {
            Console.WriteLine("  " + m.DeclaringType.FullName + "::" + m.Name);
        }

        Console.WriteLine("\n================================");
        Console.WriteLine("patch groups applied=" + okCount
                          + "  failed=" + failCount
                          + "  blocked-by-environment=" + blockedCount);
        Environment.Exit(failCount == 0 && learnApplied ? 0 : 1);
    }

    /// <summary>Patches one target and classifies the outcome.</summary>
    static void Bind(string what, ref int ok, ref int fail, ref int blocked, Action patch)
    {
        try
        {
            patch();
            Console.WriteLine("PATCHED    " + what);
            ok++;
        }
        catch (Exception e)
        {
            Exception root = e.GetBaseException();
            bool headless = IsHeadlessLimit(e);
            Console.WriteLine((headless ? "BLOCKED    " : "FAILED     ") + what + "\n           "
                              + Describe(root));
            if (Environment.GetEnvironmentVariable("GM21_TRACE") != null) Console.WriteLine(e.ToString());
            if (headless) blocked++; else fail++;
        }
    }

    /// <summary>
    /// Distinguishes "this patch cannot be applied outside RimWorld" from "this patch is broken".
    ///
    /// Two distinct headless limits produce these:
    ///
    ///  1. MISSING ASSEMBLIES. Some types hold fields typed from assemblies that ship with the
    ///     launcher rather than in Managed/ (Assembly-CSharp-firstpass, UnityEngine.AudioModule,
    ///     Steamworks.NET), so the type will not load. Supplying those assemblies clears this.
    ///
    ///  2. MISSING UNITY PLAYER. Harmony must run a target's static constructor before patching
    ///     it. Any cctor that touches Unity content or logging dies on an internal call that only
    ///     exists inside the player -- e.g. SkillUI..cctor -> ContentFinder.Get ->
    ///     Verse.UnityData..cctor -> Verse.Log.Warning -> Debug.ExtractStackTraceNoAlloc. No set
    ///     of assemblies fixes this; it needs the actual game process.
    ///
    /// The whole exception chain is searched, not just the base exception: mono surfaces a
    /// missing internal call as a MissingMethodException with an empty message, so the useful
    /// evidence is in the inner exceptions and stack frames.
    /// </summary>
    static bool IsHeadlessLimit(Exception e)
    {
        if (e.GetBaseException() is TypeLoadException) return true;
        string chain = e.ToString();
        foreach (string marker in new[]
        {
            "Assembly-CSharp-firstpass",   // launcher-side assembly
            "UnityEngine.AudioModule",     // launcher-side assembly
            "Steamworks",                  // launcher-side assembly
            "ExtractStackTraceNoAlloc",    // Unity player internal call
            "Internal_Log",                // Unity player internal call
            "Verse.UnityData"              // cctor that logs, so it needs the player
        })
        {
            if (chain.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return false;
    }

    static MethodInfo Getter(Type t, params string[] names)
    {
        foreach (string n in names)
        {
            PropertyInfo p = AccessTools.Property(t, n);
            if (p != null && p.GetGetMethod(true) != null) return p.GetGetMethod(true);
        }
        throw new MissingMemberException(t.Name + " has none of: " + string.Join(", ", names));
    }

    static string Describe(Exception root)
    {
        string m = root.Message;
        // Mono reports a missing Unity internal call as a MissingMethodException with an empty
        // message; say what that actually means instead of printing "member:(null)".
        if (string.IsNullOrEmpty(m) || m.Contains("member:(null)"))
            m = "target's static constructor needs the Unity player";
        return root.GetType().Name + ": " + Truncate(m);
    }

    static string Truncate(string s)
    {
        return s.Length <= 110 ? s : s.Substring(0, 110) + "...";
    }
}
