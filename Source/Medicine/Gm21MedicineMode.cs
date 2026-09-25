using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

namespace Grandmaster21
{
    /// <summary>The Grandmaster Medicine intervention selected on the gizmo. Default is Cure.</summary>
    public enum Gm21MedicineMode : byte
    {
        /// <summary>Cure one pathological condition ordinary medicine cannot properly address.</summary>
        Cure = 0,
        /// <summary>Restore one missing natural body part.</summary>
        Reconstruct = 1,
        /// <summary>Bring a medically viable, recently dead pawn back to life.</summary>
        Resuscitate = 2
    }

    internal sealed class Gm21MedicineModeBox
    {
        public Gm21MedicineMode mode;
    }

    /// <summary>
    /// Per-pawn Medicine mode. The same architecture as Gm21AimModeStore and Gm21MeleeDoctrineStore
    /// -- a ConditionalWeakTable keyed on the pawn, persisted from a postfix on Pawn.ExposeData -- and
    /// deliberately a SEPARATE table, so no skill's control can perturb another's.
    /// </summary>
    public static class Gm21MedicineModeStore
    {
        private static readonly ConditionalWeakTable<Pawn, Gm21MedicineModeBox> Table =
            new ConditionalWeakTable<Pawn, Gm21MedicineModeBox>();

        private static readonly ConditionalWeakTable<Pawn, Gm21MedicineModeBox>.CreateValueCallback Factory =
            _ => new Gm21MedicineModeBox();

        public static Gm21MedicineMode Get(Pawn pawn)
        {
            Gm21MedicineModeBox box;
            return (pawn != null && Table.TryGetValue(pawn, out box)) ? box.mode : Gm21MedicineMode.Cure;
        }

        public static void Set(Pawn pawn, Gm21MedicineMode mode)
        {
            if (pawn == null) return;
            if (mode == Gm21MedicineMode.Cure)
            {
                // The default; storing nothing keeps it out of the save entirely.
                Clear(pawn);
                return;
            }
            Table.GetValue(pawn, Factory).mode = mode;
        }

        /// <summary>Drops the entry. Returns true if one existed. Used by uninstall cleanup.</summary>
        public static bool Clear(Pawn pawn)
        {
            if (pawn == null) return false;
            Gm21MedicineModeBox box;
            if (!Table.TryGetValue(pawn, out box)) return false;
            box.mode = Gm21MedicineMode.Cure;
            Table.Remove(pawn);
            return true;
        }
    }

    /// <summary>
    /// Persistence. Writes "gm21MedicineMode" into the pawn's own save node. Default Cure with
    /// forceSave=false, so every pawn not retuned to another mode adds nothing to the save, and a
    /// save without the mod simply ignores the element.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.ExposeData))]
    public static class Patch_Pawn_ExposeData_MedicineMode
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn __instance)
        {
            Gm21MedicineMode mode = Gm21MedicineModeStore.Get(__instance);
            Scribe_Values.Look(ref mode, "gm21MedicineMode", Gm21MedicineMode.Cure, false);
            if (Scribe.mode == LoadSaveMode.LoadingVars && mode != Gm21MedicineMode.Cure)
            {
                Gm21MedicineModeStore.Set(__instance, mode);
            }
        }
    }
}
