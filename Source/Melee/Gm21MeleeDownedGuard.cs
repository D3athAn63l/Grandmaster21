using System.Reflection;
using HarmonyLib;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// Suppresses ONLY the storyteller's artificial "downed pawns sometimes just die anyway" roll,
    /// and only for a downing caused by a Grandmaster striking under the Downed doctrine.
    ///
    /// This is the melee counterpart of Gm21DownedGuard, which serves the Shooting package. They
    /// are deliberately separate classes patching the same method rather than one shared class
    /// with a mode flag: the shooting guard has already passed runtime validation, its gate is
    /// written in terms of ranged weapons and intended targets, and melee's gate is a completely
    /// different question. Sharing one would have meant editing working, validated code to add a
    /// branch it does not need. Both guards check whether forceDowned is already set before
    /// touching it, so running both on the same damage event is safe in either order.
    ///
    /// THIS IS NOT IMMORTALITY. CheckForStateChange tests ShouldBeDead() first and only reaches
    /// the downed branch when that is false, so a genuinely lethal wound still kills. Blood loss,
    /// destroyed organs, fire and untreated injuries all still kill normally afterwards.
    ///
    /// WHY THE MELEE FRAME AND NOT THE DamageInfo. A DamageInfo cannot reliably say "this was a
    /// melee blow": an unarmed strike's Weapon is the pawn's own race def, and a rifle used as a
    /// club reports a ranged weapon. The open melee frame says it exactly, because it exists only
    /// while a melee attack is being resolved and it knows whose attack it is.
    /// </summary>
    internal static class Gm21MeleeDownedGuard
    {
        internal static readonly FieldInfo ForceDownedField =
            AccessTools.Field(typeof(Pawn_HealthTracker), "forceDowned");

        /// <summary>
        /// Pawn_HealthTracker.pawn is private and is the only way to ask "whose tracker is this?",
        /// which is what makes the intended-target check below possible.
        /// </summary>
        internal static readonly FieldInfo PawnField =
            AccessTools.Field(typeof(Pawn_HealthTracker), "pawn");

        internal static void Prefix(Pawn_HealthTracker __instance, DamageInfo? dinfo, out bool __state)
        {
            __state = false;
            if (ForceDownedField == null) return;
            if (!Gm21Melee.DoctrineEnabled) return;
            if (!dinfo.HasValue) return;

            Gm21MeleeFrame frame = Gm21MeleeContext.Current;
            if (frame == null || !frame.attackerIsGm) return;
            if (frame.doctrine != Gm21MeleeDoctrine.Downed) return;

            DamageInfo info = dinfo.Value;
            if (info.Instigator != frame.attacker) return;

            // The pawn going down must be the pawn the Grandmaster was actually striking. Damage
            // propagating onto a neighbour, or anything else that merely shares an instigator, is
            // not a deliberate incapacitation and has no business being spared the roll.
            if (PawnField != null)
            {
                Pawn victim = PawnField.GetValue(__instance) as Pawn;
                if (victim == null || victim != frame.target) return;
            }

            object current = ForceDownedField.GetValue(__instance);
            if (current is bool && (bool)current) return;   // already forced by something else

            ForceDownedField.SetValue(__instance, true);
            __state = true;
        }

        /// <summary>
        /// Restores forceDowned. Void for the same reason as every other finalizer in this mod:
        /// an Exception-returning finalizer REPLACES the pending exception rather than reporting
        /// it, which silently swallows anything CheckForStateChange threw -- including other mods'
        /// errors, on a path that runs for every damage event in the game. Cleanup only;
        /// exception state untouched.
        /// </summary>
        internal static void Finalizer(Pawn_HealthTracker __instance, bool __state)
        {
            if (__state && ForceDownedField != null)
            {
                ForceDownedField.SetValue(__instance, false);
            }
        }
    }
}
