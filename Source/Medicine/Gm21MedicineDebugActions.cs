using System.Collections.Generic;
using System.Text;
using LudeonTK;
using RimWorld;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// Dev-mode helpers for runtime-testing Medicine 21. Reports are event-based -- one Log.Message
    /// per click -- and nothing here changes the rules for ordinary play.
    ///
    /// The existing "Grandmaster 21" actions (Max all skills to 20, Promote all level 20 skills)
    /// still work; "Make Medicine Grandmaster" is a shortcut for exactly that on one skill.
    /// </summary>
    public static class Gm21MedicineDebugActions
    {
        private const string Category = "Grandmaster 21 - Medicine";

        [DebugAction(Category, "Make Medicine Grandmaster",
            actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void MakeMedicineGrandmaster(Pawn p)
        {
            SkillRecord rec = p?.skills?.GetSkill(SkillDefOf.Medicine);
            if (rec == null || rec.TotallyDisabled)
            {
                Messages.Message("Medicine is disabled for " + p?.LabelShortCap, MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (!Gm21.PromotionEnabled)
            {
                Messages.Message("Grandmaster 21: promotion is disabled this session (Learn patch did not apply).",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (rec.levelInt < Gm21.VanillaMaxLevel) rec.Level = Gm21.VanillaMaxLevel;
            Gm21.Promote(rec);
            Messages.Message(p.LabelShortCap + " Medicine levelInt=" + rec.levelInt, p, MessageTypeDefOf.NeutralEvent, false);
        }

        [DebugAction(Category, "Report medicine tiers",
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void ReportTiers()
        {
            Log.Message("[Grandmaster 21] Medicine 21 tiers: " + Gm21MedicineTiers.Describe()
                        + "\n  rule: target = lowest loaded cap >= cap x " + Gm21Medicine.MeaningfulTierMultiplier
                        + ", else cap + " + Gm21Medicine.TopTierFallbackBonus
                        + "\n  treatment curve: 0.70->" + Gm21Medicine.RecoveryMultiplierFor(0.7f).ToString("0.00")
                        + "x 1.00->" + Gm21Medicine.RecoveryMultiplierFor(1f).ToString("0.00")
                        + "x 1.30->" + Gm21Medicine.RecoveryMultiplierFor(1.3f).ToString("0.00")
                        + "x 1.60->" + Gm21Medicine.RecoveryMultiplierFor(1.6f).ToString("0.00")
                        + "x 1.90->" + Gm21Medicine.RecoveryMultiplierFor(1.9f).ToString("0.00") + "x"
                        + "\n  flags: tend=" + Gm21Medicine.TendEnabled + " treatment=" + Gm21Medicine.TreatmentEnabled
                        + " recovery=" + Gm21Medicine.RecoveryEnabled + " immunity=" + Gm21Medicine.ImmunityEnabled
                        + " surgery=" + Gm21Medicine.SurgeryEnabled);
        }

        [DebugAction(Category, "Report pawn medicine state",
            actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ReportPawn(Pawn p)
        {
            if (p?.health == null) return;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[Grandmaster 21] Medicine 21 state for " + p.LabelShortCap + ":");
            sb.AppendLine("  Medicine Grandmaster=" + Gm21Medicine.IsMedicineGrandmaster(p)
                          + "  practising=" + Gm21Medicine.CanPractise(p)
                          + "  mode=" + Gm21MedicineModeStore.Get(p));

            sb.AppendLine("  Hediffs (Grandmaster Treatment / Cure verdict):");
            foreach (Hediff h in p.health.hediffSet.hediffs)
            {
                Gm21Treatment t;
                string treatment = Gm21TreatmentStore.TryGet(h, out t)
                    ? "treatment q=" + t.quality.ToStringPercent() + " x" + Gm21Medicine.RecoveryMultiplierFor(t.quality).ToString("0.00")
                      + " effect=" + Gm21TreatmentStore.EffectFor(h) + " active=" + Gm21TreatmentStore.TendStillInForce(h)
                      + " tick=" + t.appliedTick
                    : "no treatment";
                sb.AppendLine("    " + h.def.defName + (h.Part != null ? " [" + h.Part.Label + "]" : "")
                              + "  " + h.GetType().Name + "  " + treatment
                              + "  cure=" + Gm21CureCandidates.Classify(h)
                              + (h is Hediff_MissingPart
                                  ? "  reconstruct=" + Gm21ReconstructCandidates.Classify(p.health.hediffSet, h)
                                  : ""));
            }

            List<Hediff> cure = Gm21CureCandidates.GetCureCandidates(p);
            List<Hediff_MissingPart> rec = Gm21ReconstructCandidates.GetReconstructionCandidates(p);
            sb.AppendLine("  Cure candidates: " + cure.Count + "   Reconstruct candidates: " + rec.Count);
            Log.Message(sb.ToString().TrimEnd());
        }

        [DebugAction(Category, "Report corpse resuscitation viability",
            actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ReportCorpse()
        {
            foreach (Thing t in Find.CurrentMap.thingGrid.ThingsAt(UI.MouseCell()))
            {
                Corpse corpse = t as Corpse;
                if (corpse == null) continue;
                int now = Find.TickManager.TicksGame;
                Gm21ResuscitationFacts f = Gm21Resuscitation.Gather(corpse, now);
                Gm21ResuscitationVerdict v = Gm21Resuscitation.Decide(f);
                Log.Message("[Grandmaster 21] Resuscitation " + corpse.LabelShortCap + ": " + v
                            + (v == Gm21ResuscitationVerdict.Viable ? "" : " (" + Gm21Resuscitation.ReasonFor(v) + ")")
                            + "\n  flesh=" + f.isFlesh + " supernatural=" + f.supernatural + " hostile=" + f.hostile
                            + " brainDestroyed=" + f.brainDestroyed + " vitalDestroyed=" + f.vitalAnatomyDestroyed
                            + " rot=" + f.rotStage + " sinceDeath=" + f.ticksSinceDeath.ToStringTicksToPeriod()
                            + " (" + f.ticksSinceDeath + " / window " + Gm21Medicine.ResuscitationWindowTicks + " ticks)");
            }
        }

        [DebugAction(Category, "Age corpse past resuscitation window",
            actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void AgeCorpse()
        {
            foreach (Thing t in Find.CurrentMap.thingGrid.ThingsAt(UI.MouseCell()))
            {
                Corpse corpse = t as Corpse;
                if (corpse == null) continue;
                corpse.Age = Gm21Medicine.ResuscitationWindowTicks + 1;
                Messages.Message(corpse.LabelShortCap + " aged to " + corpse.Age + " ticks", corpse,
                    MessageTypeDefOf.NeutralEvent, false);
            }
        }

        [DebugAction(Category, "Report surgery outcome defs",
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void ReportSurgeryDefs()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[Grandmaster 21] Surgery outcome defs as seen by a Medicine Grandmaster (failures are never evaluated):");
            foreach (SurgeryOutcomeEffectDef def in DefDatabase<SurgeryOutcomeEffectDef>.AllDefsListForReading)
            {
                int failures = 0, other = 0;
                if (def.outcomes != null)
                {
                    foreach (SurgeryOutcome o in def.outcomes)
                    {
                        if (Gm21Surgery.IsFailureOutcome(o)) failures++; else other++;
                    }
                }
                sb.AppendLine("  " + def.defName + ": " + failures + " failure outcome(s) skipped, "
                              + other + " non-failure outcome(s) evaluated at quality >= 100%");
            }
            Log.Message(sb.ToString().TrimEnd());
        }
    }
}
