using System; using System.Linq; using System.Reflection;
using HarmonyLib;
class PatchAllTest {
  static void Main(){
    Assembly modAsm = Assembly.LoadFrom("/home/claude/Grandmaster21/Assemblies/Grandmaster21.dll");
    var harmony = new Harmony("ared.grandmaster21.test");
    int okCount=0, failCount=0;
    foreach (Type t in modAsm.GetTypes()) {
      if (t.GetCustomAttributes(typeof(HarmonyPatch), true).Length == 0) continue;
      try {
        var processor = harmony.CreateClassProcessor(t);
        var patched = processor.Patch();
        if (patched == null || patched.Count == 0) { Console.WriteLine("NO-TARGET  " + t.Name); failCount++; }
        else { foreach (var m in patched) Console.WriteLine("PATCHED    " + m.DeclaringType.FullName + "::" + m.Name); okCount++; }
      } catch (Exception e) {
        Console.WriteLine("FAILED     " + t.Name + " -> " + e.GetBaseException().GetType().Name + ": " + e.GetBaseException().Message);
        failCount++;
      }
    }
    Console.WriteLine();
    // Did the transpiler actually fire on the real method?
    Type learnPatch = modAsm.GetType("Grandmaster21.Patch_SkillRecord_Learn");
    var flag = learnPatch.GetField("TranspilerApplied", BindingFlags.Public|BindingFlags.Static);
    Console.WriteLine("TranspilerApplied = " + flag.GetValue(null));
    Console.WriteLine("patch classes applied=" + okCount + "  failed=" + failCount);
    Environment.Exit(failCount==0 && (bool)flag.GetValue(null) ? 0 : 1);
  }
}
