using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Grandmaster21.Transcendent
{
    internal static class ArtifactEffects
    {
        internal const int CooldownTicks = ArtifactPhenomenonInfo.CooldownTicks, MaxTargets = ArtifactPhenomenonInfo.MaxTargets, ChainTargets = ArtifactPhenomenonInfo.ChainTargets, FrostTicks = ArtifactPhenomenonInfo.FrostTicks;
        internal const float AreaRadius = ArtifactPhenomenonInfo.SmiteRadius, ChainRadius = ArtifactPhenomenonInfo.ChainRadius, MaxHealing = ArtifactPhenomenonInfo.ImmediateCap;
        [ThreadStatic] private static int effectDepth;
        internal static bool InEffect { get { return effectDepth != 0; } }
        internal static bool SafeTarget(Pawn wielder, Pawn target)
        {
            return wielder != null && !wielder.Dead && wielder.Spawned && target != null && target != wielder
                && target.Spawned && !target.Dead && !target.Downed && target.Map == wielder.Map
                && target.HostileTo(wielder) && target.Faction != Faction.OfPlayer && !target.IsPrisonerOfColony;
        }
        internal static float HealingBudget(float damage)
        {
            return ArtifactHealing.Budget(damage, ArtifactPhenomenonInfo.ImmediateFraction, MaxHealing);
        }
        internal static bool PositiveFinite(float damage) { return damage > 0 && !float.IsInfinity(damage) && !float.IsNaN(damage); }
        internal static float Scale(ArtifactTier tier)
        {
            switch (tier) { case ArtifactTier.Magical: return 1; case ArtifactTier.Mythical: return 1.4f; case ArtifactTier.Divine: return 1.8f; case ArtifactTier.Anomaly: return 2.4f; case ArtifactTier.Null: return 3; default: return 0; }
        }
        internal static bool TryTrigger(CompArtifact artifact, Pawn wielder, Pawn target, bool developer)
        {
            // Direct DEV testing starts from a standing hostile and uses real bonus-strike damage.
            if (InEffect) return false;
            return TryTriggerCaptured(ArtifactCombat.Capture(artifact, wielder, target), 0f, developer);
        }
        internal static bool TryTriggerCaptured(ArtifactCombat.Hit hit, float triggeringDamage, bool developer)
        {
            if (InEffect || hit == null || !hit.validBeforeDamage) return false;
            CompArtifact artifact = hit.artifact;
            Pawn wielder = hit.wielder;
            if (artifact == null || artifact.parent == null || artifact.parent.Destroyed
                || !ArtifactIdentity.Valid(artifact.tier, artifact.phenomenon, artifact.parent.def)
                || artifact.phenomenon == ArtifactPhenomenon.None || wielder == null || wielder.Dead
                || !wielder.Spawned || hit.impactMap == null || wielder.Map != hit.impactMap
                || !hit.impactCell.InBounds(hit.impactMap)) return false;
            if (developer && !Prefs.DevMode) return false;
            if (!TryRoll(artifact, Find.TickManager.TicksGame, developer)) return false;
            var trace = new ArtifactFeedback.Trace { map = hit.impactMap, center = hit.impactCell, wielderPosition = wielder.Position.ToVector3Shifted(), phenomenon = artifact.phenomenon, tier = artifact.tier };
            effectDepth++;
            try { Apply(artifact, wielder, hit.primary, hit.impactCell, triggeringDamage, trace); }
            catch (Exception ex) { Log.ErrorOnce("[Grandmaster 21] Artifact phenomenon callback failed: " + ex, 213702); }
            finally { effectDepth--; }
            ArtifactFeedback.Show(trace);
            return true;
        }
        internal static bool TryRoll(CompArtifact artifact, int now, bool developer)
        {
            if (!developer && now < artifact.nextProcTick) return false;
            int attempt = artifact.procCounter;
            artifact.procCounter = unchecked(attempt + 1);
            if (!developer && ArtifactRolls.Unit(artifact.phenomenonSeed, unchecked(attempt + 100)) >= ArtifactRolls.Reliability(artifact.tier)) return false;
            artifact.nextProcTick = now + CooldownTicks; // Commit cooldown/counter before any callbacks.
            return true;
        }
        // Cell-bounded queries, stable ordering, never a map-wide pawn search.
        internal static List<Pawn> Nearby(Pawn wielder, IntVec3 center, float radius)
        {
            List<Pawn> result = new List<Pawn>();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, radius, true))
            {
                if (!cell.InBounds(wielder.Map)) continue;
                foreach (Thing thing in cell.GetThingList(wielder.Map))
                {
                    Pawn pawn = thing as Pawn;
                    if (SafeTarget(wielder, pawn) && GenSight.LineOfSight(center, pawn.Position, wielder.Map)) result.Add(pawn);
                }
            }
            return result.Distinct().OrderBy(p => p.Position.DistanceToSquared(center)).ThenBy(p => p.thingIDNumber).Take(MaxTargets).ToList();
        }
        private static float Damage(Pawn wielder, Pawn target, DamageDef def, float amount, float penetration = ArtifactPhenomenonInfo.NormalPenetration, CompArtifact artifact = null, ArtifactFeedback.Trace trace = null)
        {
            if (!SafeTarget(wielder, target)) return 0;
            // No weapon provenance on generated damage. The depth guard is a second independent barrier.
            DamageInfo info = new DamageInfo(def, amount, penetration, -1f, wielder, spawnFilth: false);
            if (trace != null && trace.targets.Count < MaxTargets) trace.targets.Add(target.Position.ToVector3Shifted());
            var before = ArtifactProvenance.Snapshot(target);
            DamageWorker.DamageResult result = target.TakeDamage(info);
            if (artifact != null) ArtifactProvenance.Record(artifact, wielder, target, result, before);
            return result.totalDamageDealt;
        }
        private static float VampireDamage(Pawn wielder, Pawn primary, float scale, float triggeringDamage, CompArtifact artifact, ArtifactFeedback.Trace trace)
        {
            // Only death/downing unlocks the triggering-hit fallback. Other newly unsafe targets do not.
            if (primary == null) return 0;
            if (primary.Dead || primary.Downed) return PositiveFinite(triggeringDamage) ? triggeringDamage : 0;
            if (!SafeTarget(wielder, primary)) return 0;
            return Damage(wielder, primary, DamageDefOf.Stab, ArtifactPhenomenonInfo.VampireDamage * scale, artifact: artifact, trace: trace);
        }
        private static void Apply(CompArtifact artifact, Pawn wielder, Pawn primary, IntVec3 center, float triggeringDamage, ArtifactFeedback.Trace trace)
        {
            float scale = Scale(artifact.tier);
            switch (artifact.phenomenon)
            {
                case ArtifactPhenomenon.ChainLightning:
                    DamageDef lightning = DefDatabase<DamageDef>.GetNamed("GM21_ArtifactLightning");
                    HashSet<Pawn> visited = new HashSet<Pawn>();
                    Pawn next = SafeTarget(wielder, primary) ? primary : Nearby(wielder, center, ChainRadius).FirstOrDefault();
                    for (int i = 0; i < ChainTargets && next != null; i++)
                    {
                        IntVec3 from = next.Position;
                        visited.Add(next);
                        Damage(wielder, next, lightning, ArtifactPhenomenonInfo.DamageAtJump(i, scale), artifact: artifact, trace: trace);
                        next = Nearby(wielder, from, ChainRadius).FirstOrDefault(p => !visited.Contains(p));
                    }
                    break;
                case ArtifactPhenomenon.Smite:
                    // Direct pawn damage only: never explosions, terrain, fire, walls or buildings.
                    foreach (Pawn pawn in Nearby(wielder, center, AreaRadius)) Damage(wielder, pawn, DamageDefOf.Blunt, ArtifactPhenomenonInfo.SmiteDamage * scale, artifact: artifact, trace: trace);
                    break;
                case ArtifactPhenomenon.FlameWave:
                    // Controlled burn injury; custom damage has no ignition/explosion worker.
                    DamageDef burn = DefDatabase<DamageDef>.GetNamed("GM21_ArtifactFlame");
                    foreach (Pawn pawn in Nearby(wielder, center, ArtifactPhenomenonInfo.FlameRadius)) Damage(wielder, pawn, burn, ArtifactPhenomenonInfo.FlameDamage * scale, artifact: artifact, trace: trace);
                    break;
                case ArtifactPhenomenon.FrostNova:
                    HediffDef frost = DefDatabase<HediffDef>.GetNamed("GM21_ArtifactFrost");
                    foreach (Pawn pawn in Nearby(wielder, center, ArtifactPhenomenonInfo.FrostRadius))
                    {
                        if (SafeTarget(wielder, pawn) && pawn.health.hediffSet.GetFirstHediffOfDef(frost) == null)
                        {
                            pawn.health.AddHediff(frost);
                            trace.targets.Add(pawn.Position.ToVector3Shifted());
                            ArtifactProvenance.Status(wielder, pawn, "GM21_Log_FrostNova");
                        }
                    }
                    break;
                case ArtifactPhenomenon.GravityCrush:
                    Damage(wielder, primary, DamageDefOf.Blunt, ArtifactPhenomenonInfo.GravityDamage * scale, artifact: artifact, trace: trace);
                    if (SafeTarget(wielder, primary)) primary.stances.stunner.StunFor(ArtifactPhenomenonInfo.GravityStunTicks, wielder, false);
                    break;
                case ArtifactPhenomenon.VampiricStrike:
                    float actual = VampireDamage(wielder, primary, scale, triggeringDamage, artifact, trace);
                    ArtifactHealing.Heal(wielder, HealingBudget(actual));
                    ArtifactHealing.StartRegeneration(wielder, ArtifactHealing.RegenerationBudget(actual));
                    if (trace.targets.Count == 0 && PositiveFinite(actual) && !wielder.Dead)
                        ArtifactProvenance.Status(primary, wielder, "GM21_Log_VampiricRecovery");
                    break;
                case ArtifactPhenomenon.SpatialSlash:
                    // Armor penetration rather than teleportation; one nearby secondary target maximum.
                    Pawn secondary = Nearby(wielder, center, ArtifactPhenomenonInfo.SpatialRadius).FirstOrDefault(p => p != primary);
                    Damage(wielder, primary, DamageDefOf.Cut, ArtifactPhenomenonInfo.SpatialDamage * scale, ArtifactPhenomenonInfo.SpatialPenetration, artifact, trace);
                    if (secondary != null) Damage(wielder, secondary, DamageDefOf.Cut, ArtifactPhenomenonInfo.SpatialSecondaryDamage * scale, ArtifactPhenomenonInfo.SpatialPenetration, artifact, trace);
                    break;
            }
        }
    }
}
