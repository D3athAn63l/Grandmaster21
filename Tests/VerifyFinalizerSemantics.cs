// Proves, against the real Harmony assembly, what a finalizer's return value does to a pending
// exception -- and therefore that Grandmaster 21's cleanup finalizers do not swallow errors.
//
// This exists because the mistake it guards against is invisible by inspection: a finalizer that
// returns null reads like "I am not changing anything", and in fact means "there is no exception
// any more". A mod that silently eats RimWorld's and other mods' exceptions is close to
// undebuggable, so the contract is pinned down by execution rather than by comment.
//
// Harmony's contract:
//   void-returning finalizer            -> cannot alter exception state; the original propagates
//   Exception-returning finalizer       -> the RETURN VALUE REPLACES the pending exception
//       return null                     -> exception suppressed
//       return __exception              -> exception preserved
using System;
using System.Reflection;
using HarmonyLib;

static class VerifyFinalizerSemantics
{
    static int pass, fail;

    static void Check(string name, bool ok, string detail = "")
    {
        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name + (detail == "" ? "" : "   [" + detail + "]"));
        if (ok) pass++; else fail++;
    }

    // Three distinct targets: a Harmony patch is permanent for the method it is applied to.
    public static class Targets
    {
        public static void ThrowsA() { throw new InvalidOperationException("boom-A"); }
        public static void ThrowsB() { throw new InvalidOperationException("boom-B"); }
        public static void ThrowsC() { throw new InvalidOperationException("boom-C"); }
    }

    public static bool cleanupRan;

    // The WRONG shape -- what Grandmaster 21 used to ship.
    public static Exception Finalizer_ReturnsNull() { cleanupRan = true; return null; }

    // The shape it ships now.
    public static void Finalizer_Void() { cleanupRan = true; }

    // The acceptable alternative, if a finalizer must keep the Exception return type.
    public static Exception Finalizer_ReturnsException(Exception __exception)
    {
        cleanupRan = true;
        return __exception;
    }

    static Exception Invoke(Action a)
    {
        try { a(); return null; }
        catch (Exception e) { return e.GetBaseException(); }
    }

    static void Main()
    {
        Harmony h = new Harmony("gm21.finalizer.semantics");

        Console.WriteLine("=== Harmony finalizer contract, verified by execution ===");

        // 1. Baseline: unpatched, the exception escapes.
        Check("unpatched target throws", Invoke(Targets.ThrowsC) != null);

        // 2. The bug: an Exception-returning finalizer that returns null SUPPRESSES.
        h.Patch(AccessTools.Method(typeof(Targets), "ThrowsA"), null, null, null,
                new HarmonyMethod(AccessTools.Method(typeof(VerifyFinalizerSemantics), "Finalizer_ReturnsNull")));
        cleanupRan = false;
        Exception a = Invoke(Targets.ThrowsA);
        Check("cleanup ran under the 'return null' finalizer", cleanupRan);
        Check("'return null' SWALLOWS the exception (this is the bug)", a == null,
              a == null ? "suppressed, as expected for this shape" : "propagated: " + a.Message);

        // 3. The fix: a void finalizer cleans up and leaves the exception alone.
        h.Patch(AccessTools.Method(typeof(Targets), "ThrowsB"), null, null, null,
                new HarmonyMethod(AccessTools.Method(typeof(VerifyFinalizerSemantics), "Finalizer_Void")));
        cleanupRan = false;
        Exception b = Invoke(Targets.ThrowsB);
        Check("cleanup ran under the void finalizer", cleanupRan);
        Check("void finalizer PRESERVES the exception", b != null && b.Message == "boom-B",
              b == null ? "swallowed!" : b.Message);

        // 4. The alternative: returning __exception unchanged also preserves it.
        h.Patch(AccessTools.Method(typeof(Targets), "ThrowsC"), null, null, null,
                new HarmonyMethod(AccessTools.Method(typeof(VerifyFinalizerSemantics), "Finalizer_ReturnsException")));
        cleanupRan = false;
        Exception c = Invoke(Targets.ThrowsC);
        Check("cleanup ran under the 'return __exception' finalizer", cleanupRan);
        Check("'return __exception' PRESERVES the exception", c != null && c.Message == "boom-C",
              c == null ? "swallowed!" : c.Message);

        Console.WriteLine("\n=== Grandmaster 21's shipped finalizers ===");
        AuditShippedFinalizers();

        Console.WriteLine("\n================================");
        Console.WriteLine("PASS: " + pass + "   FAIL: " + fail);
        Environment.Exit(fail == 0 ? 0 : 1);
    }

    /// <summary>
    /// Every method the mod registers as a Harmony finalizer must be void-returning, or must
    /// return Exception AND take __exception so it can hand it straight back. Anything else is a
    /// silent suppressor.
    /// </summary>
    static void AuditShippedFinalizers()
    {
        Assembly mod = Assembly.LoadFrom("Grandmaster21.dll");

        // DISCOVERED, not listed. A hardcoded list silently stops covering the mod the moment a
        // new finalizer is added -- which is exactly what happened when the melee package landed.
        // Every method whose name marks it as a finalizer is audited, so a new one is covered the
        // day it is written.
        const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance
                               | BindingFlags.Public | BindingFlags.NonPublic
                               | BindingFlags.DeclaredOnly;

        int found = 0;
        foreach (Type t in mod.GetTypes())
        {
            foreach (MethodInfo m in t.GetMethods(Any))
            {
                if (m.Name.IndexOf("Finalizer", StringComparison.Ordinal) < 0) continue;
                found++;

                bool isVoid = m.ReturnType == typeof(void);
                bool returnsExceptionSafely = m.ReturnType == typeof(Exception)
                    && Array.Exists(m.GetParameters(), p => p.Name == "__exception");

                Check(t.FullName + ":" + m.Name + " does not suppress exceptions",
                      isVoid || returnsExceptionSafely,
                      "returns " + m.ReturnType.Name
                      + (isVoid ? " (void: cannot alter exception state)"
                                : returnsExceptionSafely ? " with __exception"
                                : " WITHOUT __exception -- would suppress"));
            }
        }

        // Six are expected today: the shooting shot context and downed guard, the melee frame and
        // downed guard, and Medicine's DoTend frame and health-tick counter. Fewer means discovery
        // broke, not that the mod got safer.
        Check("all six shipped finalizers were discovered", found >= 6, "found=" + found);
    }
}
