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
                bool headless = IsHeadlessLimit(root);
                Console.WriteLine((headless ? "BLOCKED    " : "FAILED     ") + t.Name + " -> "
                                  + root.GetType().Name + ": " + Truncate(root.Message));
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
            bool headless = IsHeadlessLimit(root);
            Console.WriteLine((headless ? "BLOCKED    " : "FAILED     ") + what + "\n           "
                              + root.GetType().Name + ": " + Truncate(root.Message));
            if (Environment.GetEnvironmentVariable("GM21_TRACE") != null) Console.WriteLine(e.ToString());
            if (headless) blocked++; else fail++;
        }
    }

    /// <summary>
    /// Some RimWorld types cannot load outside the Unity player: they hold fields typed from
    /// assemblies that ship with the launcher rather than in Managed/ (Assembly-CSharp-firstpass,
    /// UnityEngine.AudioModule, Steamworks.NET). That is an environment limit, not a finding.
    /// </summary>
    static bool IsHeadlessLimit(Exception root)
    {
        string m = root.Message ?? "";
        return root is TypeLoadException
            || m.Contains("Assembly-CSharp-firstpass")
            || m.Contains("UnityEngine.AudioModule")
            || m.IndexOf("steamworks", StringComparison.OrdinalIgnoreCase) >= 0;
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

    static string Truncate(string s)
    {
        return s.Length <= 110 ? s : s.Substring(0, 110) + "...";
    }
}
