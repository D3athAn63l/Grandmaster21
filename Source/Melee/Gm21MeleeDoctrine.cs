using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// The Grandmaster's melee doctrine. Deliberately the same three-state concept as the Shooting
    /// aim mode, and deliberately a SEPARATE value: a pawn who is a Grandmaster at both should be
    /// able to shoot to kill and strike to disable, or the reverse.
    /// </summary>
    public enum Gm21MeleeDoctrine : byte
    {
        /// <summary>Vanilla strike placement at full force. All passive mastery still applies.</summary>
        Normal = 0,
        /// <summary>Strike the anatomy that ends the fight fastest, at full force.</summary>
        Killer = 1,
        /// <summary>Strike to disable, avoiding vitals, and pull the blow so it does not kill.</summary>
        Downed = 2
    }

    internal sealed class Gm21MeleeDoctrineBox
    {
        public Gm21MeleeDoctrine doctrine;
    }

    /// <summary>
    /// Per-pawn melee doctrine.
    ///
    /// Same architecture as Gm21AimModeStore and GrandmasterStore, for the same reasons: a
    /// ConditionalWeakTable keyed on the pawn, persisted from a postfix on the pawn's own
    /// ExposeData, so the value rides inside the pawn's save node through map transitions,
    /// caravans, world-pawn conversion and despawn/respawn with nothing to prune and no manager.
    ///
    /// It is a separate table from the aim mode rather than a shared two-field box, so that
    /// neither skill's UI or save format can perturb the other's -- the brief's "do not confuse
    /// their doctrine controls" applies to the stored state as much as to the gizmo.
    /// </summary>
    public static class Gm21MeleeDoctrineStore
    {
        private static readonly ConditionalWeakTable<Pawn, Gm21MeleeDoctrineBox> Table =
            new ConditionalWeakTable<Pawn, Gm21MeleeDoctrineBox>();

        private static readonly ConditionalWeakTable<Pawn, Gm21MeleeDoctrineBox>.CreateValueCallback Factory =
            _ => new Gm21MeleeDoctrineBox();

        public static Gm21MeleeDoctrine Get(Pawn pawn)
        {
            Gm21MeleeDoctrineBox box;
            return (pawn != null && Table.TryGetValue(pawn, out box))
                ? box.doctrine
                : Gm21MeleeDoctrine.Normal;
        }

        public static void Set(Pawn pawn, Gm21MeleeDoctrine doctrine)
        {
            if (pawn == null) return;
            if (doctrine == Gm21MeleeDoctrine.Normal)
            {
                // Normal is the default; storing nothing keeps it out of the save entirely.
                Clear(pawn);
                return;
            }
            Table.GetValue(pawn, Factory).doctrine = doctrine;
        }

        /// <summary>Drops the entry. Used by authorised uninstall cleanup.</summary>
        public static void Clear(Pawn pawn)
        {
            if (pawn == null) return;
            Gm21MeleeDoctrineBox box;
            if (!Table.TryGetValue(pawn, out box)) return;
            box.doctrine = Gm21MeleeDoctrine.Normal;
            Table.Remove(pawn);
        }
    }

    /// <summary>
    /// Persistence. Writes "gm21MeleeDoctrine" into the pawn's own save node.
    ///
    /// Default Normal with forceSave=false, so every pawn that is not a retuned Melee Grandmaster
    /// -- which is very nearly all of them -- adds nothing at all to the save file. Removing the
    /// mod leaves an unknown element RimWorld ignores.
    ///
    /// This is the ONLY melee state that is serialised. Riposte queues, interception decisions,
    /// crit rolls and deflection context are all transient by construction and are deliberately
    /// not written: a save reloaded mid-swing simply starts the next exchange clean.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.ExposeData))]
    public static class Patch_Pawn_ExposeData_MeleeDoctrine
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn __instance)
        {
            Gm21MeleeDoctrine doctrine = Gm21MeleeDoctrineStore.Get(__instance);
            Scribe_Values.Look(ref doctrine, "gm21MeleeDoctrine", Gm21MeleeDoctrine.Normal, false);
            if (Scribe.mode == LoadSaveMode.LoadingVars && doctrine != Gm21MeleeDoctrine.Normal)
            {
                Gm21MeleeDoctrineStore.Set(__instance, doctrine);
            }
        }
    }
}
