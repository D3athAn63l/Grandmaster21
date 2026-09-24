using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Grandmaster21.Transcendent
{
    internal static class ArtifactHealing
    {
        internal static float Budget(float damage, float fraction, float cap)
        { return ArtifactEffects.PositiveFinite(damage) ? Math.Min(cap, damage * fraction) : 0; }
        internal static float RegenerationBudget(float damage)
        { return Budget(damage, ArtifactPhenomenonInfo.RegenFraction, ArtifactPhenomenonInfo.RegenCap); }
        internal static bool Eligible(Hediff hediff)
        {
            Hediff_Injury injury = hediff as Hediff_Injury;
            return injury != null && injury.def != null && injury.def.injuryProps != null && !injury.def.chronic
                && ArtifactEffects.PositiveFinite(injury.Severity) && injury.CanHealNaturally();
        }
        internal static IEnumerable<Hediff_Injury> Ordered(IEnumerable<Hediff> hediffs)
        { return hediffs.Where(Eligible).Cast<Hediff_Injury>().OrderByDescending(h => h.Severity); }
        internal static float Heal(Pawn pawn, float budget)
        {
            if (pawn == null || pawn.Dead || pawn.health == null || !ArtifactEffects.PositiveFinite(budget)) return 0;
            float spent = 0;
            foreach (Hediff_Injury wound in Ordered(pawn.health.hediffSet.hediffs).ToList())
            {
                if (pawn.Dead) break;
                if (!Eligible(wound)) continue;
                float amount = Math.Min(budget - spent, wound.Severity);
                if (amount <= 0) break;
                // Native Heal retains health notifications, tending/scar behavior and mod callbacks.
                wound.Heal(amount); spent += amount;
            }
            return spent;
        }
        internal static void StartRegeneration(Pawn pawn, float budget)
        {
            if (pawn == null || pawn.Dead || pawn.health == null || !ArtifactEffects.PositiveFinite(budget)) return;
            var active = pawn.health.hediffSet.hediffs.OfType<Hediff_ArtifactRegenerating>().FirstOrDefault();
            if (active != null) { active.Refresh(budget); return; }
            active = (Hediff_ArtifactRegenerating)HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("GM21_ArtifactRegenerating"), pawn);
            active.Refresh(budget);
            pawn.health.AddHediff(active);
        }
    }

    public sealed class Hediff_ArtifactRegenerating : Hediff
    {
        internal int remainingTicks, ticksToPulse;
        internal float remainingBudget;
        public override bool ShouldRemove { get { return remainingTicks <= 0 || pawn == null || pawn.Dead; } }
        public override string LabelInBrackets { get { return (remainingTicks / 60f).ToString("0.0") + "s"; } }
        public override string TipStringExtra { get { return "GM21_TC_RegenRemaining".Translate(ArtifactPhenomenonInfo.Number(remainingBudget)); } }
        internal void Refresh(float budget)
        {
            if (!ArtifactEffects.PositiveFinite(budget)) return;
            remainingBudget = Math.Min(ArtifactPhenomenonInfo.RegenCap, Math.Max(remainingBudget, budget));
            remainingTicks = ArtifactPhenomenonInfo.RegenTicks;
            ticksToPulse = ArtifactPhenomenonInfo.RegenPulseTicks;
        }
        public override bool TryMergeWith(Hediff other)
        {
            var regen = other as Hediff_ArtifactRegenerating;
            if (regen == null || other.def != def) return false;
            Refresh(regen.remainingBudget); return true;
        }
        // Also handles batched 1.6 interval ticks. At most six pulses, no global scan or timer.
        internal void Advance(int delta, Action<float> heal)
        {
            delta = Math.Max(0, Math.Min(delta, remainingTicks));
            while (delta > 0 && remainingTicks > 0)
            {
                int step = Math.Min(delta, Math.Max(1, ticksToPulse));
                remainingTicks -= step; ticksToPulse -= step; delta -= step;
                if (ticksToPulse <= 0 || remainingTicks <= 0)
                {
                    int pulses = 1 + (remainingTicks + ArtifactPhenomenonInfo.RegenPulseTicks - 1) / ArtifactPhenomenonInfo.RegenPulseTicks;
                    float allowance = remainingBudget / pulses;
                    remainingBudget = Math.Max(0, remainingBudget - allowance); // Commit before callback/save.
                    ticksToPulse = ArtifactPhenomenonInfo.RegenPulseTicks;
                    if (allowance > 0) heal(allowance); // Unused pulse allowance expires; no stored healing windfall.
                }
            }
        }
        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (pawn == null || pawn.Dead) { remainingTicks = 0; remainingBudget = 0; return; }
            Advance(delta, allowance =>
            {
                ArtifactHealing.Heal(pawn, allowance);
                ArtifactFeedback.RegenerationPulse(pawn);
            });
        }
        public override void Notify_PawnDied(DamageInfo? dinfo, Hediff culprit = null)
        { remainingTicks = 0; remainingBudget = 0; base.Notify_PawnDied(dinfo, culprit); }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref remainingTicks, "gm21RegenTicks");
            Scribe_Values.Look(ref ticksToPulse, "gm21RegenPulse");
            Scribe_Values.Look(ref remainingBudget, "gm21RegenBudget");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                remainingTicks = Math.Max(0, Math.Min(ArtifactPhenomenonInfo.RegenTicks, remainingTicks));
                ticksToPulse = Math.Max(1, Math.Min(ArtifactPhenomenonInfo.RegenPulseTicks, ticksToPulse));
                remainingBudget = ArtifactEffects.PositiveFinite(remainingBudget) ? Math.Min(ArtifactPhenomenonInfo.RegenCap, remainingBudget) : 0;
            }
        }
    }
}
