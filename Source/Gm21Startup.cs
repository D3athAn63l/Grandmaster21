using System.Reflection;
using HarmonyLib;
using Verse;

namespace Grandmaster21
{
    [StaticConstructorOnStartup]
    public static class Gm21Startup
    {
        static Gm21Startup()
        {
            Harmony harmony = new Harmony("ared.grandmaster21");
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            if (!Patch_SkillRecord_Learn.TranspilerApplied)
            {
                Log.Warning("[Grandmaster 21] SkillRecord.Learn transpiler did not apply. "
                            + "Grandmaster promotion (20 -> 21) is disabled this session. "
                            + "Vanilla skill progression is unaffected.");
            }
            else
            {
                Log.Message("[Grandmaster 21] Initialised. Grandmaster level "
                            + Gm21.GrandmasterLevel + " enabled; requirement "
                            + Gm21Mod.Settings.grandmasterXpRequirement.ToString("N0") + " XP.");
            }
        }
    }
}
