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

class PatchAllTest
{
    static void Main(string[] args)
    {
        string path = args.Length > 0 ? args[0] : "Grandmaster21.dll";
        Assembly modAsm = Assembly.LoadFrom(path);
        var harmony = new Harmony("ared.grandmaster21.test");

        int okCount = 0, failCount = 0;

        Console.WriteLine("=== Attribute patches ===");
        foreach (Type t in modAsm.GetTypes())
        {
            if (t.GetCustomAttributes(typeof(HarmonyPatch), true).Length == 0) continue;
            try
            {
                var patched = harmony.CreateClassProcessor(t).Patch();
                if (patched == null || patched.Count == 0)
                {
                    Console.WriteLine("NO-TARGET  " + t.Name);
                    failCount++;
                }
                else
                {
                    foreach (var m in patched)
                        Console.WriteLine("PATCHED    " + m.DeclaringType.FullName + "::" + m.Name);
                    okCount++;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("FAILED     " + t.Name + " -> "
                                  + e.GetBaseException().GetType().Name + ": "
                                  + e.GetBaseException().Message);
                failCount++;
            }
        }

        Console.WriteLine("\n=== Manual shooting patches ===");
        Type patcher = modAsm.GetType("Grandmaster21.Gm21ShootingPatches");
        Type shooting = modAsm.GetType("Grandmaster21.Gm21Shooting");
        try
        {
            patcher.GetMethod("Apply", BindingFlags.Static | BindingFlags.NonPublic)
                   .Invoke(null, new object[] { harmony });
            bool passive = (bool)shooting.GetField("PassiveBonusesEnabled").GetValue(null);
            bool targeting = (bool)shooting.GetField("AnatomicalTargetingEnabled").GetValue(null);
            Console.WriteLine("PassiveBonusesEnabled     = " + passive);
            Console.WriteLine("AnatomicalTargetingEnabled = " + targeting);
            if (!passive || !targeting) failCount++; else okCount++;
        }
        catch (Exception e)
        {
            Console.WriteLine("FAILED     Gm21ShootingPatches.Apply -> " + e.GetBaseException());
            failCount++;
        }

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
        Console.WriteLine("patch groups applied=" + okCount + "  failed=" + failCount);
        Environment.Exit(failCount == 0 && learnApplied ? 0 : 1);
    }
}
