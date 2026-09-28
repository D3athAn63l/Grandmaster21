// Test-only loader preparation, never shipped. Frame's static constructor loads three rendering
// assets via Unity player APIs. Replace only that constructor in the temporary test DLL; preserve
// and audit the pristine DLL separately. All gameplay method bodies must round-trip unchanged.
using System;
using System.Linq;
using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;

static class PrepareConstructionFixture
{
    static string Body(MethodDefinition m)
    {
        if (!m.HasBody) return "<no body>";
        return m.Body.InitLocals + "|" + string.Join(";", m.Body.Variables.Select(v => v.VariableType.FullName)) + "|"
            + string.Join(";", m.Body.Instructions.Select(i => i.ToString())) + "|"
            + string.Join(";", m.Body.ExceptionHandlers.Select(h => h.HandlerType + ":" + h.TryStart + ":" + h.TryEnd
                + ":" + h.HandlerStart + ":" + h.HandlerEnd + ":" + h.FilterStart + ":" + h.CatchType));
    }
    static void Main(string[] args)
    {
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0])));
        resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location));
        using (var original = ModuleDefinition.ReadModule(args[0], new ReaderParameters { AssemblyResolver = resolver }))
        {
            var methods = original.GetTypes().SelectMany(t => t.Methods).Select(m => m.FullName + "|" + Body(m)).ToList();
            var ctor = original.GetType("RimWorld.Frame").Methods.Single(m => m.Name == ".cctor");
            var stores = ctor.Body.Instructions.Where(i => i.OpCode.Code == Code.Stsfld).Select(i => (FieldReference)i.Operand).ToList();
            if (!ctor.Body.Instructions.Select(i => i.OpCode.Code).SequenceEqual(new[] {
                    Code.Ldstr, Code.Ldsfld, Code.Call, Code.Stsfld, Code.Ldstr, Code.Ldc_I4_1, Code.Call,
                    Code.Stsfld, Code.Ldstr, Code.Ldc_I4_1, Code.Call, Code.Stsfld, Code.Ret })
                || ctor.Body.ExceptionHandlers.Count != 0 || stores.Count != 3 || !stores.Select(f => f.Name).SequenceEqual(new[] { "UnderfieldMat", "CornerTex", "TileTex" })
                || stores.Any(f => f.DeclaringType.FullName != "RimWorld.Frame" || !new[] { "UnityEngine.Material", "UnityEngine.Texture2D" }.Contains(f.FieldType.FullName))
                || ctor.Body.Instructions.Where(i => i.Operand is MethodReference).Any(i => {
                    var m = (MethodReference)i.Operand;
                    return !(m.DeclaringType.FullName == "Verse.MaterialPool" && m.Name == "MatFrom")
                        && !(m.DeclaringType.FullName.StartsWith("Verse.ContentFinder`1") && m.Name == "Get");
                })) throw new Exception("Frame rendering initializer changed; cannot prepare fixture safely");
            ctor.Body.Instructions.Clear();
            ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            original.Write(args[1]);
            using (var prepared = ModuleDefinition.ReadModule(args[1]))
            {
                var preparedMethods = prepared.GetTypes().SelectMany(t => t.Methods).ToList();
                if (preparedMethods.Count != methods.Count) throw new Exception("Fixture changed the method inventory");
                var changed = preparedMethods.Where((m, i) => methods[i] != m.FullName + "|" + Body(m)).Select(m => m.FullName).ToList();
                if (changed.Count != 1 || changed[0] != ctor.FullName) throw new Exception("Fixture altered methods beyond Frame rendering initializer");
            }
        }
        Console.WriteLine("FIXTURE: only Frame's three-asset rendering initializer omitted; all other method bodies verified unchanged.");
    }
}
