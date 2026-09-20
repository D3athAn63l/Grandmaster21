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
        /// Raised by <see cref="Patch_SkillRecord_Learn.Transpiler"/> once, and only once, the
        /// expected IL pattern has actually been rewritten.
        ///
        /// While this is false the Learn method is running unmodified vanilla code, which
        /// contains "if (levelInt >= 20) levelInt = 20" inside its level-up loop. A pawn promoted
        /// to 21 in that environment would be silently demoted again the next time it earned XP,
        /// and the surplus-XP clamp would not apply -- so the mod refuses to create a Grandmaster
        /// at all. See <see cref="Promote"/>.
        /// </summary>
        public static bool LearnPatchApplied;

        /// <summary>Latched so the "promotion disabled" warning is logged once, not per XP event.</summary>
        private static bool promotionDisabledWarned;

        /// <summary>
        /// True when it is safe to create a Grandmaster. False means the Learn transpiler did not
        /// apply and the 20 -> 21 transition is disabled for the whole session.
        /// </summary>
        public static bool PromotionEnabled
        {
            get { return LearnPatchApplied; }
        }

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
        /// randomly rolled gene manufacture a Grandmaster -- and, in the other direction, would
        /// let a negative aptitude strip Grandmaster status from someone who earned it.
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
        /// The write is wrapped in an authorised scope so that any re-entrant assignment made by
        /// vanilla or another mod during the transition is also recognised as deliberate.
        ///
        /// FAIL-SAFE: refuses to promote unless the Learn transpiler actually applied. Creating
        /// a Grandmaster inside unmodified vanilla Learn would produce a pawn that is demoted
        /// again by vanilla's own level-up loop, so the mod stays at "no Grandmasters this
        /// session" instead. Grandmaster XP continues to be tracked and saved, so nothing is
        /// lost: the pawn promotes on the first session where the patch applies again.
        /// </summary>
        public static void Promote(SkillRecord rec)
        {
            if (rec == null || rec.levelInt != VanillaMaxLevel) return;

            if (!LearnPatchApplied)
            {
                WarnPromotionDisabledOnce();
                return;
            }

            using (Gm21Authorized.Enter())
            {
                rec.levelInt = GrandmasterLevel;
            }

            // Level 21 evaluates XpForLevelUpCurve at its clamped tail => 30000.
            // Keep xpSinceLastLevel inside [0, req-1] so XpProgressPercent stays sane and the
            // vanilla down-level loop has no reason to fire.
            ClampXpForCurrentLevel(rec);

            Pawn pawn = rec.Pawn;
            if (pawn != null && pawn.IsColonist)
            {
                Messages.Message(
                    "GM21_BecameGrandmaster".Translate(pawn.LabelShortCap, rec.def.LabelCap),
                    pawn, MessageTypeDefOf.PositiveEvent, false);
            }
        }

        /// <summary>
        /// AUTHORISED DEMOTION -- the deliberate 21 -> 20 transition.
        ///
        /// Ordinary gameplay can never reach this. It exists for exactly two callers:
        /// "Prepare Save for Uninstall" (<see cref="Gm21Uninstall"/>) and the dev-mode demote
        /// action. Both are explicit user actions.
        ///
        /// Writes <c>levelInt</c> directly. Going through the Level property would be pointless
        /// here -- the setter patch ignores writes to a Grandmaster by design -- and would also
        /// be at the mercy of any other mod's prefix on that setter. The authorised scope is
        /// still opened so the invariant "levelInt only moves inside an authorised scope" holds
        /// for the whole operation.
        /// </summary>
        internal static bool ForceDemoteForUninstall(SkillRecord rec)
        {
            if (rec == null || rec.levelInt < GrandmasterLevel) return false;

            using (Gm21Authorized.Enter())
            {
                rec.levelInt = VanillaMaxLevel;
            }

            ClampXpForCurrentLevel(rec);
            return true;
        }

        /// <summary>
        /// Keeps xpSinceLastLevel inside the band vanilla itself maintains at cap, [0, req-1],
        /// after the stored level has been changed out from under it.
        /// </summary>
        private static void ClampXpForCurrentLevel(SkillRecord rec)
        {
            float req = rec.XpRequiredForLevelUp;
            rec.xpSinceLastLevel = Mathf.Clamp(rec.xpSinceLastLevel, 0f, Mathf.Max(0f, req - 1f));
        }

        internal static void WarnPromotionDisabledOnce()
        {
            if (promotionDisabledWarned) return;
            promotionDisabledWarned = true;
            Log.Warning("[Grandmaster 21] A pawn met the Grandmaster requirement, but promotion is "
                        + "disabled this session because the SkillRecord.Learn safety patch did not "
                        + "apply. Level 21 would not be safe from vanilla's level-up handling, so no "
                        + "Grandmaster will be created. Grandmaster XP is still being tracked and "
                        + "saved. This message is logged once per session.");
        }
    }
}
