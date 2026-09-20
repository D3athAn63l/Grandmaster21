using System; using System.Collections.Generic; using System.Linq;
using Mono.Cecil; using Mono.Cecil.Cil;
// Replicates the mod's transpiler predicate against the REAL shipped IL and reports
// how the rewritten comparison behaves at levels 19/20/21.
class VerifyTranspiler {
  static void Main(string[] a){
    var asm=AssemblyDefinition.ReadAssembly(a[0]);
    var t=asm.MainModule.Types.First(x=>x.FullName=="RimWorld.SkillRecord");
    var m=t.Methods.First(x=>x.Name=="Learn");
    var code=m.Body.Instructions.ToList();
    var hits=new List<int>();
    for(int i=1;i<code.Count-1;i++){
      if(!IsLdc(code[i],20)) continue;
      if(code[i-1].OpCode!=OpCodes.Ldfld) continue;
      var fr=code[i-1].Operand as FieldReference;
      if(fr==null||fr.Name!="levelInt"||fr.DeclaringType.FullName!="RimWorld.SkillRecord") continue;
      if(code[i+1].OpCode!=OpCodes.Bne_Un && code[i+1].OpCode!=OpCodes.Bne_Un_S) continue;
      hits.Add(i);
    }
    Console.WriteLine("Matches for [ldfld levelInt / ldc.i4 20 / bne.un]: "+hits.Count);
    foreach(var h in hits) Console.WriteLine("  at IL_"+code[h].Offset.ToString("x4")+"  -> branch target IL_"+((Instruction)code[h+1].Operand).Offset.ToString("x4"));
    // Also count every ldc.i4 20 in the method, to show how selective the predicate is.
    int all=code.Count(c=>IsLdc(c,20));
    Console.WriteLine("Total 'ldc.i4 20' literals in Learn: "+all+"  (predicate selects "+hits.Count+")");
    Console.WriteLine(hits.Count==1 ? "RESULT: PASS - exactly one unambiguous patch site" : "RESULT: FAIL - ambiguous or missing");
    // Show the level-up ceiling site we intentionally leave alone
    Console.WriteLine("\nSites intentionally NOT patched (levelInt>=20 -> levelInt=20):");
    for(int i=0;i<code.Count-1;i++)
      if(IsLdc(code[i],20)&&!hits.Contains(i))
        Console.WriteLine("  IL_"+code[i].Offset.ToString("x4")+"  next="+code[i+1].OpCode);
  }
  static bool IsLdc(Instruction i,int v){
    if(i.OpCode==OpCodes.Ldc_I4_S) return Convert.ToInt32(i.Operand)==v;
    if(i.OpCode==OpCodes.Ldc_I4) return Convert.ToInt32(i.Operand)==v;
    return false; }
}
