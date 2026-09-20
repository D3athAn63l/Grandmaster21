using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

class Inspect {
    static AssemblyDefinition asm;
    static void Main(string[] a) {
        asm = AssemblyDefinition.ReadAssembly(a[0]);
        string mode = a[1];
        if (mode == "type") { foreach (var t in a.Skip(2)) DumpType(t); }
        else if (mode == "find") { Find(a[2]); }
        else if (mode == "method") { DumpMethod(a[2], a[3]); }
    }
    static IEnumerable<TypeDefinition> AllTypes() {
        foreach (var m in asm.Modules)
            foreach (var t in m.Types) { yield return t; foreach (var n in Nested(t)) yield return n; }
    }
    static IEnumerable<TypeDefinition> Nested(TypeDefinition t) {
        foreach (var n in t.NestedTypes) { yield return n; foreach (var x in Nested(n)) yield return x; }
    }
    static void Find(string pat) {
        foreach (var t in AllTypes())
            if (t.FullName.IndexOf(pat, StringComparison.OrdinalIgnoreCase) >= 0)
                Console.WriteLine(t.FullName);
    }
    static TypeDefinition Get(string name) =>
        AllTypes().FirstOrDefault(t => t.FullName == name)
        ?? AllTypes().FirstOrDefault(t => t.Name == name);

    static void DumpType(string name) {
        var t = Get(name);
        if (t == null) { Console.WriteLine("!! NOT FOUND: " + name); return; }
        Console.WriteLine("================ TYPE " + t.FullName + " : " + (t.BaseType?.Name ?? "-") + " ================");
        foreach (var f in t.Fields)
            Console.WriteLine("  FIELD  " + (f.IsStatic?"static ":"") + (f.IsInitOnly?"readonly ":"") + f.FieldType.Name + " " + f.Name
                + (f.HasConstant ? " = " + f.Constant : ""));
        foreach (var p in t.Properties)
            Console.WriteLine("  PROP   " + p.PropertyType.Name + " " + p.Name + " {" + (p.GetMethod!=null?"get;":"") + (p.SetMethod!=null?"set;":"") + "}");
        foreach (var m in t.Methods)
            Console.WriteLine("  METHOD " + (m.IsStatic?"static ":"") + (m.IsVirtual?"virtual ":"") + m.ReturnType.Name + " " + m.Name
                + "(" + string.Join(", ", m.Parameters.Select(x => x.ParameterType.Name + " " + x.Name)) + ")"
                + (m.HasBody ? "  [il=" + m.Body.Instructions.Count + "]" : " [abstract]"));
        Console.WriteLine();
    }
    static void DumpMethod(string type, string meth) {
        var t = Get(type);
        if (t == null) { Console.WriteLine("!! TYPE NOT FOUND " + type); return; }
        foreach (var m in t.Methods.Where(x => x.Name == meth)) {
            Console.WriteLine("======== " + t.FullName + "::" + m.Name
                + "(" + string.Join(", ", m.Parameters.Select(x => x.ParameterType.Name + " " + x.Name)) + ") -> " + m.ReturnType.Name + " ========");
            if (!m.HasBody) { Console.WriteLine("  (no body)"); continue; }
            foreach (var v in m.Body.Variables) Console.WriteLine("  .local V_" + v.Index + " " + v.VariableType.Name);
            foreach (var i in m.Body.Instructions) Console.WriteLine("  " + i);
            Console.WriteLine();
        }
    }
}
