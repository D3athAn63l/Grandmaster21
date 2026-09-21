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

            // Applied by hand, after PatchAll, so that an unresolvable combat internal disables
            // one shooting feature instead of throwing out of PatchAll and taking the entire mod
            // -- level 21 permanence included -- down with it.
            Gm21ShootingPatches.Apply(harmony);
            Gm21MeleePatches.Apply(harmony);

            Log.Message("[Grandmaster 21] " + Gm21Version.Full
                        + "  |  shooting: passive=" + Gm21Shooting.PassiveBonusesEnabled
                        + " targeting=" + Gm21Shooting.AnatomicalTargetingEnabled
                        + "  |  melee: passive=" + Gm21Melee.PassiveEnabled
                        + " reactions=" + Gm21Melee.ReactionsEnabled
                        + " doctrine=" + Gm21Melee.DoctrineEnabled
                        + " ally=" + Gm21Melee.AllyInterceptEnabled
                        + " projectile=" + Gm21Melee.ProjectileDefenceEnabled);

            if (!Gm21.LearnPatchApplied)
            {
                Log.Warning("[Grandmaster 21] The SkillRecord.Learn safety patch did not apply. "
                            + "Grandmaster promotion (20 -> 21) is DISABLED this session: creating a "
                            + "level 21 pawn would not be safe from vanilla's level-up handling. "
                            + "Grandmaster XP is still tracked and saved, and existing Grandmasters "
                            + "keep their level. Vanilla skill progression is unaffected.");
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
