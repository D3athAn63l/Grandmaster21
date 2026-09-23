using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Grandmaster21.Transcendent
{
    internal static class ArtifactEffects
    {
        internal const int CooldownTicks = 180, MaxTargets = 16, ChainTargets = 4, FrostTicks = 300;
        internal const float AreaRadius = 5f, ChainRadius = 6f, MaxHealing = 8f;
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
            return float.IsNaN(damage) || float.IsInfinity(damage) || damage <= 0 ? 0 : Math.Min(MaxHealing, damage * .35f);
        }
        internal static float Scale(ArtifactTier tier)
        {
            switch (tier) { case ArtifactTier.Magical: return 1; case ArtifactTier.Mythical: return 1.4f; case ArtifactTier.Divine: return 1.8f; case ArtifactTier.Anomaly: return 2.4f; case ArtifactTier.Null: return 3; default: return 0; }
        }
        internal static bool TryTrigger(CompArtifact artifact, Pawn wielder, Pawn target, bool developer)
        {
            if (InEffect || artifact == null || artifact.parent == null || artifact.parent.Destroyed
                || !ArtifactIdentity.Valid(artifact.tier, artifact.phenomenon, artifact.parent.def)
                || artifact.phenomenon == ArtifactPhenomenon.None || !SafeTarget(wielder, target)) return false;
            int now = Find.TickManager.TicksGame;
            if (developer && !Prefs.DevMode) return false;
            if (!developer && now < artifact.nextProcTick) return false;
            int attempt = artifact.procCounter;
            artifact.procCounter = unchecked(attempt + 1);
            if (!developer && ArtifactRolls.Unit(artifact.phenomenonSeed, unchecked(attempt + 100)) >= ArtifactRolls.Reliability(artifact.tier)) return false;
            artifact.nextProcTick = now + CooldownTicks; // Commit cooldown/counter before any callbacks.
            effectDepth++;
            try { Apply(artifact, wielder, target); }
            catch (Exception ex) { Log.ErrorOnce("[Grandmaster 21] Artifact phenomenon callback failed: " + ex, 213702); }
            finally { effectDepth--; }
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
        private static float Damage(Pawn wielder, Pawn target, DamageDef def, float amount, float penetration = .3f)
        {
            if (!SafeTarget(wielder, target)) return 0;
            // No weapon provenance on generated damage. The depth guard is a second independent barrier.
            DamageInfo info = new DamageInfo(def, amount, penetration, -1f, wielder, spawnFilth: false);
            return target.TakeDamage(info).totalDamageDealt;
        }
        private static void Apply(CompArtifact artifact, Pawn wielder, Pawn primary)
        {
            float scale = Scale(artifact.tier);
            IntVec3 center = primary.Position;
            switch (artifact.phenomenon)
            {
                case ArtifactPhenomenon.ChainLightning:
                    DamageDef lightning = DefDatabase<DamageDef>.GetNamed("GM21_ArtifactLightning");
                    HashSet<Pawn> visited = new HashSet<Pawn>();
                    Pawn next = primary;
                    for (int i = 0; i < ChainTargets && next != null; i++)
                    {
                        IntVec3 from = next.Position;
                        visited.Add(next);
                        Damage(wielder, next, lightning, (12 - i * 2) * scale);
                        next = Nearby(wielder, from, ChainRadius).FirstOrDefault(p => !visited.Contains(p));
                    }
                    break;
                case ArtifactPhenomenon.Smite:
                    // Direct pawn damage only: never explosions, terrain, fire, walls or buildings.
                    foreach (Pawn pawn in Nearby(wielder, center, AreaRadius)) Damage(wielder, pawn, DamageDefOf.Blunt, 22 * scale);
                    break;
                case ArtifactPhenomenon.FlameWave:
                    // Controlled burn injury; custom damage has no ignition/explosion worker.
                    DamageDef burn = DefDatabase<DamageDef>.GetNamed("GM21_ArtifactFlame");
                    foreach (Pawn pawn in Nearby(wielder, center, 3f)) Damage(wielder, pawn, burn, 12 * scale);
                    break;
                case ArtifactPhenomenon.FrostNova:
                    HediffDef frost = DefDatabase<HediffDef>.GetNamed("GM21_ArtifactFrost");
                    foreach (Pawn pawn in Nearby(wielder, center, 4f))
                    {
                        if (pawn.health.hediffSet.GetFirstHediffOfDef(frost) == null) pawn.health.AddHediff(frost);
                    }
                    break;
                case ArtifactPhenomenon.GravityCrush:
                    Damage(wielder, primary, DamageDefOf.Blunt, 18 * scale);
                    if (SafeTarget(wielder, primary)) primary.stances.stunner.StunFor(90, wielder, false);
                    break;
                case ArtifactPhenomenon.VampiricStrike:
                    float budget = HealingBudget(Damage(wielder, primary, DamageDefOf.Stab, 14 * scale));
                    if (wielder.Dead || wielder.health == null) break;
                    foreach (Hediff_Injury wound in wielder.health.hediffSet.hediffs.OfType<Hediff_Injury>()
                        .Where(h => !h.IsPermanent()).OrderByDescending(h => h.Severity).ToList())
                    {
                        float heal = Math.Min(budget, Math.Max(0, wound.Severity));
                        if (heal <= 0) break;
                        wound.Heal(heal); budget -= heal;
                    }
                    break;
                case ArtifactPhenomenon.SpatialSlash:
                    // Armor penetration rather than teleportation; one nearby secondary target maximum.
                    Pawn secondary = Nearby(wielder, center, 3f).FirstOrDefault(p => p != primary);
                    Damage(wielder, primary, DamageDefOf.Cut, 16 * scale, 2f);
                    if (secondary != null) Damage(wielder, secondary, DamageDefOf.Cut, 8 * scale, 2f);
                    break;
            }
        }
    }
}
