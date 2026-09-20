using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

namespace Grandmaster21
{
    /// <summary>The Grandmaster's aiming doctrine. Default is deliberately Normal.</summary>
    public enum Gm21AimMode : byte
    {
        /// <summary>Vanilla body-part resolution. Passive marksman bonuses still apply.</summary>
        Normal = 0,
        /// <summary>Aim for the anatomy most likely to kill.</summary>
        Killer = 1,
        /// <summary>Aim for the anatomy most likely to incapacitate, avoiding vitals.</summary>
        Downed = 2
    }

    internal sealed class Gm21AimModeBox
    {
        public Gm21AimMode mode;
    }

    /// <summary>
    /// Per-pawn aim mode.
    ///
    /// Deliberately the SAME architecture as GrandmasterStore: a ConditionalWeakTable keyed on the
    /// pawn, persisted from a postfix on the pawn's own ExposeData. That means the value lands
    /// inside the pawn's save node, so map transitions, caravans, world-pawn conversion and
    /// despawn/respawn all carry it with no extra code and nothing to prune. No manager, no
    /// ticking, no global dictionary.
    ///
    /// A ThingComp would have required injecting CompProperties into every humanlike ThingDef,
    /// which is far more invasive for one byte of state.
    /// </summary>
    public static class Gm21AimModeStore
    {
        private static readonly ConditionalWeakTable<Pawn, Gm21AimModeBox> Table =
            new ConditionalWeakTable<Pawn, Gm21AimModeBox>();

        private static readonly ConditionalWeakTable<Pawn, Gm21AimModeBox>.CreateValueCallback Factory =
            _ => new Gm21AimModeBox();

        public static Gm21AimMode Get(Pawn pawn)
        {
            Gm21AimModeBox box;
            return (pawn != null && Table.TryGetValue(pawn, out box)) ? box.mode : Gm21AimMode.Normal;
        }

        public static void Set(Pawn pawn, Gm21AimMode mode)
        {
            if (pawn == null) return;
            if (mode == Gm21AimMode.Normal)
            {
                // Normal is the default; storing nothing keeps it out of the save entirely.
                Clear(pawn);
                return;
            }
            Table.GetValue(pawn, Factory).mode = mode;
        }

        /// <summary>
        /// Drops the entry. Called when a pawn stops being a Shooting Grandmaster -- currently
        /// only through authorised uninstall cleanup, where the mode is meaningless anyway.
        /// </summary>
        public static void Clear(Pawn pawn)
        {
            if (pawn == null) return;
            Gm21AimModeBox box;
            if (!Table.TryGetValue(pawn, out box)) return;
            box.mode = Gm21AimMode.Normal;
            Table.Remove(pawn);
        }
    }

    /// <summary>
    /// Persistence. Writes "gm21AimMode" into the pawn's own save node, beside vanilla's fields.
    ///
    /// Default is Normal with forceSave=false, so a pawn in Normal mode -- which is every pawn
    /// that is not a Grandmaster, and every Grandmaster the player has not retuned -- adds nothing
    /// at all to the save file. Removing the mod leaves an unknown element RimWorld ignores.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.ExposeData))]
    public static class Patch_Pawn_ExposeData_AimMode
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn __instance)
        {
            Gm21AimMode mode = Gm21AimModeStore.Get(__instance);
            Scribe_Values.Look(ref mode, "gm21AimMode", Gm21AimMode.Normal, false);
            if (Scribe.mode == LoadSaveMode.LoadingVars && mode != Gm21AimMode.Normal)
            {
                Gm21AimModeStore.Set(__instance, mode);
            }
        }
    }
}
