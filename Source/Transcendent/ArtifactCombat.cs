using System;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grandmaster21.Transcendent
{
    // Only the audited vanilla bullet/melee paths establish provenance. Unknown mod verbs fail closed.
    internal static class ArtifactCombat
    {
        internal sealed class Hit
        {
            internal CompArtifact artifact;
            internal Pawn wielder;
            internal bool attempted;
        }
        [ThreadStatic] internal static Hit current;
        private static readonly FieldInfo Equipment = AccessTools.Field(typeof(Projectile), "equipment");
        internal static void Install()
        {
            Harmony harmony = new Harmony("ared.grandmaster21.artifacts");
            try
            {
                MethodInfo bullet = AccessTools.DeclaredMethod(typeof(Bullet), "Impact", new[] { typeof(Thing), typeof(bool) });
                MethodInfo melee = AccessTools.DeclaredMethod(typeof(Verb_MeleeAttackDamage), "ApplyMeleeDamageToTarget", new[] { typeof(LocalTargetInfo) });
                MethodInfo damage = AccessTools.DeclaredMethod(typeof(Pawn), "PostApplyDamage", new[] { typeof(DamageInfo), typeof(float) });
                if (Equipment == null || Equipment.FieldType != typeof(Thing) || bullet == null || melee == null || damage == null)
                    throw new MissingMethodException("Artifact combat API contract changed");
                harmony.Patch(bullet, prefix: Method("BulletPrefix"), finalizer: Method("Restore"));
                harmony.Patch(melee, prefix: Method("MeleePrefix"), finalizer: Method("Restore"));
                harmony.Patch(damage, postfix: Method("AfterDamage"));
                harmony.Patch(AccessTools.DeclaredMethod(typeof(Pawn), "SpawnSetup", new[] { typeof(Map), typeof(bool) }), postfix: Method("PawnSpawned"));
                harmony.Patch(AccessTools.DeclaredMethod(typeof(Pawn_EquipmentTracker), "GetGizmos"), postfix: Method("EquipmentGizmos"));
            }
            catch (Exception ex)
            {
                harmony.UnpatchAll(harmony.Id);
                Log.Error("[Grandmaster 21] Artifact combat disabled; crafting and saved items remain available: " + ex.Message);
            }
        }
        private static HarmonyMethod Method(string name) { return new HarmonyMethod(typeof(ArtifactCombat), name); }
        private static void PawnSpawned(Pawn __instance) { ArtifactWatcher.TrackPawn(__instance); }
        private static void EquipmentGizmos(Pawn_EquipmentTracker __instance, ref IEnumerable<Gizmo> __result)
        {
            // Equipment doesn't forward arbitrary ThingComp gizmos in vanilla.
            if (Prefs.DevMode) __result = WithArtifactTools(__result, __instance);
        }
        private static IEnumerable<Gizmo> WithArtifactTools(IEnumerable<Gizmo> original, Pawn_EquipmentTracker equipment)
        {
            foreach (Gizmo gizmo in original) yield return gizmo;
            if (!Prefs.DevMode) yield break;
            foreach (Thing weapon in equipment.AllEquipmentListForReading)
            {
                CompArtifact artifact = weapon.TryGetComp<CompArtifact>();
                if (artifact != null) foreach (Gizmo gizmo in artifact.CompGetGizmosExtra()) yield return gizmo;
            }
        }
        private static void Enter(Thing weapon, Pawn wielder)
        {
            CompArtifact comp = weapon == null || weapon.Destroyed ? null : weapon.TryGetComp<CompArtifact>();
            current = comp == null || comp.phenomenon == ArtifactPhenomenon.None || ArtifactEffects.InEffect ? null
                : new Hit { artifact = comp, wielder = wielder };
        }
        private static void BulletPrefix(Bullet __instance, out Hit __state)
        {
            __state = current;
            Enter(Equipment.GetValue(__instance) as Thing, __instance.Launcher as Pawn);
        }
        private static void MeleePrefix(Verb_MeleeAttackDamage __instance, out Hit __state)
        {
            __state = current;
            Enter(__instance.EquipmentSource, __instance.CasterPawn);
        }
        private static Exception Restore(Exception __exception, Hit __state)
        {
            current = __state;
            return __exception; // Never suppress another mod's/game's exception.
        }
        private static void AfterDamage(Pawn __instance, DamageInfo dinfo, float totalDamageDealt)
        {
            Hit hit = current;
            if (ArtifactEffects.InEffect || hit == null || hit.attempted || totalDamageDealt <= 0
                || dinfo.Instigator != hit.wielder || dinfo.Weapon != hit.artifact.parent.def) return;
            hit.attempted = true; // Multi-damage tools/bullets get one opportunity per hit.
            try { ArtifactEffects.TryTrigger(hit.artifact, hit.wielder, __instance, false); }
            catch (Exception ex) { Log.ErrorOnce("[Grandmaster 21] Artifact trigger failed; effect suppressed: " + ex, 213701); }
        }
    }
}
