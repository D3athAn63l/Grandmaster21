using System.Globalization;
using RimWorld;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// The one place the Grandmaster's real melee defence is shown to the player.
    ///
    /// WHY IT IS NEEDED. The Stats tab will keep reporting "Melee dodge chance: 50%", because that
    /// is genuinely what the StatDef resolves to -- RimWorld caps it there for every pawn in the
    /// game. For a Grandmaster that number is no longer what happens in a fight, and leaving it as
    /// the only thing the player can see is quietly misleading.
    ///
    /// WHY THE STAT ITSELF IS LEFT ALONE. Raising the StatDef's maximum would change the number for
    /// every pawn in the game and for every other mod reading it. The whole design of this feature
    /// is that levels 0-20 stay exactly vanilla, so the stat stays exactly vanilla and the
    /// exception is expressed where it actually applies: in the melee defence roll, and here, in
    /// the Grandmaster's own tooltip.
    ///
    /// IT DOES NOT LIE. The number shown is produced by calling the same Gm21Melee.DefenceChance
    /// the combat roll calls, with the same composite, so an injured Grandmaster sees their real
    /// reduced figure. A pawn who cannot defend at all -- downed, unconscious, asleep, stunned --
    /// is told exactly that, because in that state vanilla's value is the one that applies.
    /// </summary>
    internal static class Gm21MeleeTooltip
    {
        /// <summary>
        /// The extra line appended to a Melee Grandmaster's skill tooltip, or "" for every other
        /// pawn and every other skill.
        ///
        /// Returns early on the cheapest test first, so an ordinary skill tooltip costs one
        /// reference comparison.
        /// </summary>
        internal static string EffectiveDefenceLine(SkillRecord sk)
        {
            if (sk == null || sk.def != SkillDefOf.Melee) return "";
            if (!Gm21.IsGrandmaster(sk)) return "";
            if (!Gm21Melee.PassiveEnabled) return "";

            Pawn pawn = sk.Pawn;
            if (pawn == null) return "";

            float vanilla;
            try
            {
                vanilla = pawn.GetStatValue(StatDefOf.MeleeDodgeChance);
            }
            catch
            {
                // A foreign StatWorker that throws must not take the Skills tab down with it.
                return "";
            }
            if (vanilla < 0f) vanilla = 0f;

            // Exactly the gate combat uses. A pawn who cannot defend is not shown a defence.
            if (!Gm21Melee.CanAct(pawn))
            {
                return "\n\n" + "GM21_Melee_DodgeIncapable".Translate(Percent(vanilla));
            }

            float effective = Gm21Melee.DefenceChance(vanilla, Gm21Melee.Defence(pawn));
            return "\n\n" + "GM21_Melee_DodgeTooltip".Translate(Percent(vanilla), Percent(effective));
        }

        private static string Percent(float fraction)
        {
            return (fraction * 100f).ToString("0.##", CultureInfo.InvariantCulture) + "%";
        }
    }
}
