using System; using System.Collections.Generic; using System.Linq;
using Mono.Cecil; using Mono.Cecil.Cil;
class Refs {
    static AssemblyDefinition asm;
    static void Main(string[] a) {
        asm = AssemblyDefinition.ReadAssembly(a[0]);
        string mode = a[1], pat = a[2];
        foreach (var t in AllTypes())
        foreach (var m in t.Methods) {
            if (!m.HasBody) continue;
            foreach (var i in m.Body.Instructions) {
                bool hit = false;
                if (mode == "calls") {
                    var mr = i.Operand as MethodReference;
                    if (mr != null && (mr.DeclaringType.FullName + "::" + mr.Name).IndexOf(pat, StringComparison.OrdinalIgnoreCase) >= 0) hit = true;
                } else if (mode == "field") {
                    var fr = i.Operand as FieldReference;
                    if (fr != null && (fr.DeclaringType.FullName + "::" + fr.Name).IndexOf(pat, StringComparison.OrdinalIgnoreCase) >= 0) hit = true;
                }
                if (hit) { Console.WriteLine(t.FullName + "::" + m.Name + "  @" + i.Offset + "  [" + i.OpCode + "]"); break; }
            }
        }
    }
    static IEnumerable<TypeDefinition> AllTypes() {
        foreach (var m in asm.Modules) foreach (var t in m.Types) { yield return t; foreach (var n in Nested(t)) yield return n; }
    }
    static IEnumerable<TypeDefinition> Nested(TypeDefinition t) { foreach (var n in t.NestedTypes) { yield return n; foreach (var x in Nested(n)) yield return x; } }
}
