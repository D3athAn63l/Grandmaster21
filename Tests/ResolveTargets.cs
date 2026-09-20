using System; using System.Linq; using System.Reflection;
using HarmonyLib; using RimWorld; using Verse;
class ResolveTargets {
  static int ok=0, bad=0;
  static void T(string label, MethodBase m, string expectSig){
    if (m==null){ Console.WriteLine("UNRESOLVED  "+label); bad++; return; }
    string sig = m.DeclaringType.FullName+"::"+m.Name+"("+string.Join(",", m.GetParameters().Select(p=>p.ParameterType.Name).ToArray())+")";
    bool match = expectSig==null || sig==expectSig;
    Console.WriteLine((match?"OK          ":"MISMATCH    ")+label+"  ->  "+sig);
    if(match) ok++; else bad++;
  }
  static void Main(){
    T("SkillRecord.set_Level",  AccessTools.Method(typeof(SkillRecord), "set_Level"), "RimWorld.SkillRecord::set_Level(Int32)");
    T("SkillRecord.GetLevel",   AccessTools.Method(typeof(SkillRecord), "GetLevel"), "RimWorld.SkillRecord::GetLevel(Boolean)");
    T("SkillRecord.GetLevelForUI", AccessTools.Method(typeof(SkillRecord), "GetLevelForUI"), "RimWorld.SkillRecord::GetLevelForUI(Boolean)");
    T("SkillRecord.get_LevelDescriptor", AccessTools.Method(typeof(SkillRecord), "get_LevelDescriptor"), "RimWorld.SkillRecord::get_LevelDescriptor()");
    T("SkillRecord.Learn",      AccessTools.Method(typeof(SkillRecord), "Learn"), "RimWorld.SkillRecord::Learn(Single,Boolean,Boolean)");
    T("SkillRecord.Interval",   AccessTools.Method(typeof(SkillRecord), "Interval"), "RimWorld.SkillRecord::Interval()");
    T("SkillRecord.ExposeData", AccessTools.Method(typeof(SkillRecord), "ExposeData"), "RimWorld.SkillRecord::ExposeData()");
    T("QualityUtility.GQCBP(int,bool)", AccessTools.Method(typeof(QualityUtility), "GenerateQualityCreatedByPawn", new[]{typeof(int),typeof(bool)}),
      "RimWorld.QualityUtility::GenerateQualityCreatedByPawn(Int32,Boolean)");
    T("QualityUtility.GQCBP(Pawn,SkillDef,bool)", AccessTools.Method(typeof(QualityUtility), "GenerateQualityCreatedByPawn", new[]{typeof(Pawn),typeof(SkillDef),typeof(bool)}),
      "RimWorld.QualityUtility::GenerateQualityCreatedByPawn(Pawn,SkillDef,Boolean)");
    T("SkillUI.GetSkillDescription (private)", AccessTools.Method(typeof(SkillUI), "GetSkillDescription", new[]{typeof(SkillRecord)}),
      "RimWorld.SkillUI::GetSkillDescription(SkillRecord)");
    T("SkillUI.DrawSkill(rec,Rect,mode,string)", AccessTools.Method(typeof(SkillUI), "DrawSkill",
        new[]{typeof(SkillRecord), typeof(UnityEngine.Rect), typeof(SkillUI.SkillDrawMode), typeof(string)}),
      "RimWorld.SkillUI::DrawSkill(SkillRecord,Rect,SkillDrawMode,String)");
    T("SkillRecord.levelInt field", null==AccessTools.Field(typeof(SkillRecord),"levelInt")?null:AccessTools.Method(typeof(SkillRecord),"GetUnclampedLevel"), null);
    Console.WriteLine("\nresolved="+ok+"  problems="+bad);
    Environment.Exit(bad==0?0:1);
  }
}
