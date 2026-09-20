using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// Central constants and helpers. Everything here is deliberately tiny and allocation-free:
    /// several of these are called from SkillRecord.Learn, which is a hot path.
    /// </summary>
    public static class Gm21
    {
        /// <summary>The one exceptional level this mod adds. Absolute hard maximum.</summary>
        public const int GrandmasterLevel = 21;

        /// <summary>Vanilla maximum. SkillRecord.MaxLevel is a const in Assembly-CSharp; mirrored here.</summary>
        public const int VanillaMaxLevel = 20;

        public const int MinLevel = 0;

        /// <summary>
        /// Cap used by the <see cref="SkillRecord.Learn"/> transpiler in place of the inlined
        /// literal 20 in "if (levelInt == 20)".
        ///
        /// Returning 21 for a Grandmaster makes vanilla take its own "at cap" branch, which
        /// clamps surplus XP and performs no level change. That is what stops vanilla's
        /// level-up loop from incrementing 21 -> 22 and then snapping the pawn back to 20.
        /// </summary>
        public static int LearnCapFor(SkillRecord rec)
        {
            return (rec != null && rec.levelInt >= GrandmasterLevel) ? GrandmasterLevel : VanillaMaxLevel;
        }

        /// <summary>
        /// True only for a legitimately earned Grandmaster.
        ///
        /// Deliberately reads the stored <c>levelInt</c> rather than the Level property:
        /// Level is Clamp(levelInt + Aptitude, ...) and Aptitude comes from genes, traits and
        /// hediffs, all of which can be randomly generated. Using Level here would let a
        /// randomly rolled gene manufacture a Grandmaster.
        /// </summary>
        public static bool IsGrandmaster(SkillRecord rec)
        {
            return rec != null && rec.levelInt >= GrandmasterLevel;
        }

        public static bool IsGrandmaster(Pawn pawn, SkillDef skill)
        {
            if (pawn == null || skill == null) return false;
            // Mechanoids have no SkillRecord at all; they use RaceProps.mechFixedSkillLevel.
            if (pawn.RaceProps != null && pawn.RaceProps.IsMechanoid) return false;
            Pawn_SkillTracker tracker = pawn.skills;
            if (tracker == null) return false;
            SkillRecord rec = tracker.GetSkill(skill);
            return rec != null && rec.levelInt >= GrandmasterLevel;
        }

        /// <summary>
        /// Deterministic quality band. 21 is the only level that yields Legendary.
        /// </summary>
        public static QualityCategory BandFor(int skillLevel)
        {
            if (skillLevel >= GrandmasterLevel) return QualityCategory.Legendary;
            if (skillLevel >= 17) return QualityCategory.Masterwork;
            if (skillLevel >= 14) return QualityCategory.Excellent;
            if (skillLevel >= 11) return QualityCategory.Good;
            if (skillLevel >= 8) return QualityCategory.Normal;
            if (skillLevel >= 4) return QualityCategory.Poor;
            return QualityCategory.Awful;
        }

        /// <summary>
        /// The authorised 20 -> 21 transition. This is the ONLY code path in the mod that
        /// raises a level to 21.
        ///
        /// It writes the field directly rather than using the Level property, because the
        /// property setter is exactly what we keep clamped at 20 to block pawn generation.
        /// </summary>
        public static void Promote(SkillRecord rec)
        {
            if (rec == null || rec.levelInt != VanillaMaxLevel) return;

            rec.levelInt = GrandmasterLevel;

            // Level 21 evaluates XpForLevelUpCurve at its clamped tail => 30000.
            // Keep xpSinceLastLevel inside [0, req-1] so XpProgressPercent stays sane and the
            // vanilla down-level loop has no reason to fire.
            float req = rec.XpRequiredForLevelUp;
            rec.xpSinceLastLevel = Mathf.Clamp(rec.xpSinceLastLevel, 0f, Mathf.Max(0f, req - 1f));

            Pawn pawn = rec.Pawn;
            if (pawn != null && pawn.IsColonist)
            {
                Messages.Message(
                    "GM21_BecameGrandmaster".Translate(pawn.LabelShortCap, rec.def.LabelCap),
                    pawn, MessageTypeDefOf.PositiveEvent, false);
            }
        }
    }
}
