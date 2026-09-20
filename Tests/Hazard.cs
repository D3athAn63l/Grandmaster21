using System; using System.Collections.Generic; using System.Linq;
using Mono.Cecil; using Mono.Cecil.Cil;
class Hazard {
  static void Main(string[] a){
    var asm=AssemblyDefinition.ReadAssembly(a[0]);
    foreach(var t in All(asm)) foreach(var m in t.Methods){
      if(!m.HasBody) continue;
      bool touchesSkill=false, hasSwitch=false, hasArr=false;
      foreach(var i in m.Body.Instructions){
        var mr=i.Operand as MethodReference;
        if(mr!=null){ var n=mr.DeclaringType.FullName+"::"+mr.Name;
          if(n=="RimWorld.SkillRecord::get_Level"||n=="RimWorld.SkillRecord::GetLevel"||n=="RimWorld.SkillRecord::GetLevelForUI"||n=="RimWorld.SkillRecord::GetUnclampedLevel") touchesSkill=true; }
        var fr=i.Operand as FieldReference;
        if(fr!=null && fr.DeclaringType.FullName=="RimWorld.SkillRecord" && fr.Name=="levelInt") touchesSkill=true;
        if(i.OpCode==OpCodes.Switch) hasSwitch=true;
        if(i.OpCode.Name!=null && (i.OpCode.Name.StartsWith("ldelem")||i.OpCode.Name.StartsWith("stelem"))) hasArr=true;
      }
      if(touchesSkill && (hasSwitch||hasArr))
        Console.WriteLine((hasSwitch?"[SWITCH]":"        ")+(hasArr?"[ARRAY]":"       ")+" "+t.FullName+"::"+m.Name);
    }
  }
  static IEnumerable<TypeDefinition> All(AssemblyDefinition a){ foreach(var m in a.Modules) foreach(var t in m.Types){ yield return t; foreach(var n in Nest(t)) yield return n; } }
  static IEnumerable<TypeDefinition> Nest(TypeDefinition t){ foreach(var n in t.NestedTypes){ yield return n; foreach(var x in Nest(n)) yield return x; } }
}
