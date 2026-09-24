using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Grandmaster21.Transcendent
{
    internal static class ArtifactFeedback
    {
        internal sealed class Trace
        {
            internal Map map;
            internal IntVec3 center;
            internal Vector3 wielderPosition;
            internal ArtifactPhenomenon phenomenon;
            internal ArtifactTier tier;
            internal readonly List<Vector3> targets = new List<Vector3>();
        }
        internal const int RingSegments = 16, TransferParticles = 8;
        private static readonly Color Electric = new Color(.5f, .8f, 1f), Gold = new Color(1f, .85f, .35f);
        private static readonly Color Flame = new Color(1f, .3f, .05f), Ice = new Color(.6f, .95f, 1f);
        private static readonly Color Gravity = new Color(.65f, .45f, .85f), Vampire = new Color(.85f, .08f, .25f), Spatial = new Color(.9f, .6f, 1f);
        internal static void Safely(Action action)
        {
            // Vanilla particle/sound selection may use Rand. Restore it so feedback cannot change future gameplay rolls.
            try { Rand.PushState(); try { action(); } finally { Rand.PopState(); } }
            catch (Exception ex) { Log.ErrorOnce("[Grandmaster 21] Artifact visual/audio feedback unavailable: " + ex, 213712); }
        }
        private static void Particle(Map map, Vector3 position, FleckDef def, Color color, float scale, Vector3? velocity = null)
        {
            if (map == null || def == null || !position.ShouldSpawnMotesAt(map)) return;
            FleckCreationData data = FleckMaker.GetDataStatic(position, map, def, scale);
            data.instanceColor = color; data.solidTimeOverride = .25f;
            if (velocity.HasValue) { data.velocity = velocity; data.airTimeLeft = .5f; }
            map.flecks.CreateFleck(data);
        }
        private static void Line(Map map, Vector3 from, Vector3 to, Color color, float width)
        {
            Vector3 delta = to - from;
            float length = delta.MagnitudeHorizontal();
            if (length < .01f) return;
            FleckCreationData data = FleckMaker.GetDataStatic((from + to) * .5f, map, FleckDefOf.LineEMP);
            data.exactScale = new Vector3(length, 1f, width);
            data.rotation = Mathf.Atan2(-delta.z, delta.x) * Mathf.Rad2Deg;
            data.instanceColor = color; data.solidTimeOverride = .25f;
            map.flecks.CreateFleck(data);
        }
        private static void Ring(Map map, Vector3 center, float radius, Color color)
        {
            for (int i = 0; i < RingSegments; i++)
            {
                float a = i * 2 * Mathf.PI / RingSegments, b = (i + 1) * 2 * Mathf.PI / RingSegments;
                Line(map, center + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius,
                    center + new Vector3(Mathf.Cos(b), 0, Mathf.Sin(b)) * radius, color, .12f);
            }
        }
        private static void Radial(Map map, Vector3 center, float radius, FleckDef def, Color color, bool inward)
        {
            for (int i = 0; i < RingSegments; i++)
            {
                float angle = i * 2 * Mathf.PI / RingSegments;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                Particle(map, center + direction * (inward ? radius : .5f), def, color, .8f,
                    direction * (inward ? -radius * 2 : radius * 2));
            }
        }
        private static void Sound(SoundDef sound, Trace trace, float volume = .65f)
        {
            if (sound == null) return;
            SoundInfo info = SoundInfo.InMap(new TargetInfo(trace.center, trace.map));
            info.volumeFactor = volume;
            sound.PlayOneShot(info); // One positional cue per proc; never a camera/global sound.
        }
        internal static void Show(Trace trace)
        {
            Safely(() =>
            {
                if (trace == null || trace.map == null || !trace.center.InBounds(trace.map) || !trace.center.ShouldSpawnMotesAt(trace.map)) return;
                Render(trace);
            });
        }
        private static void Render(Trace t)
        {
            Vector3 center = t.center.ToVector3Shifted();
            float intensity = 1f + .12f * (ArtifactEffects.Scale(t.tier) - 1f);
            Color color = Color.white;
            switch (t.phenomenon)
            {
                case ArtifactPhenomenon.ChainLightning:
                    color = Electric; Vector3 from = center;
                    foreach (Vector3 point in t.targets)
                    {
                        // A bent electrical arc follows the actual ordered damage recipients.
                        Vector3 mid = (from + point) * .5f + new Vector3(.2f, 0, -.15f);
                        Line(t.map, from, mid, color, .22f); Line(t.map, mid, point, color, .22f);
                        Particle(t.map, point, FleckDefOf.LightningGlow, color, 1.7f * intensity); from = point;
                    }
                    if (t.targets.Count == 0) Particle(t.map, center, FleckDefOf.LightningGlow, color, 1.7f);
                    Sound(SoundDefOf.EnergyShield_AbsorbDamage, t); break;
                case ArtifactPhenomenon.Smite:
                    color = Gold;
                    Line(t.map, center + new Vector3(0, 0, 8), center, color, .85f * intensity);
                    Particle(t.map, center, FleckDefOf.ExplosionFlash, color, 4f * intensity);
                    Ring(t.map, center, ArtifactPhenomenonInfo.SmiteRadius, color);
                    Sound(SoundDefOf.Thunder_OnMap, t, .45f); break;
                case ArtifactPhenomenon.FlameWave:
                    color = Flame;
                    Radial(t.map, center, ArtifactPhenomenonInfo.FlameRadius, FleckDefOf.FireGlow, color, false);
                    Particle(t.map, center, FleckDefOf.HeatGlow, color, 2f * intensity);
                    // Vanilla Verb_Ignite uses this as soundCast, played by Verb.TryCastNextBurstShot via PlayOneShot.
                    Sound(SoundDefOf.Interact_Ignite, t); break;
                case ArtifactPhenomenon.FrostNova:
                    color = Ice;
                    Ring(t.map, center, ArtifactPhenomenonInfo.FrostRadius, color);
                    Radial(t.map, center, ArtifactPhenomenonInfo.FrostRadius, FleckDefOf.AirPuff, color, false);
                    Sound(SoundDefOf.EnergyShield_Reset, t); break;
                case ArtifactPhenomenon.GravityCrush:
                    color = Gravity;
                    Radial(t.map, center, 2f, FleckDefOf.DustPuff, color, true);
                    Ring(t.map, center, 1.4f, color);
                    Particle(t.map, center, FleckDefOf.ShotFlash, color, 2f * intensity);
                    Sound(SoundDefOf.Pawn_Melee_Punch_HitBuilding_Generic, t); break;
                case ArtifactPhenomenon.VampiricStrike:
                    color = Vampire;
                    Vector3 travel = t.wielderPosition - center;
                    for (int i = 0; i < TransferParticles; i++)
                    {
                        Vector3 position = center + travel * (i / (float)TransferParticles);
                        Particle(t.map, position, FleckDefOf.MetaPuff, color, .45f, (t.wielderPosition - position) * 2f);
                    }
                    Particle(t.map, center, FleckDefOf.Heart, color, .7f);
                    Particle(t.map, t.wielderPosition, FleckDefOf.HealingCross, color, 1.1f);
                    Sound(SoundDefOf.Power_OnSmall, t); break;
                case ArtifactPhenomenon.SpatialSlash:
                    color = Spatial;
                    foreach (Vector3 point in t.targets)
                    {
                        Vector3 direction = (point - t.wielderPosition).normalized;
                        if (direction.sqrMagnitude < .1f) direction = new Vector3(1, 0, 1).normalized;
                        Line(t.map, point - direction * 1.7f, point + direction * 1.7f, color, .32f * intensity);
                        Particle(t.map, point, FleckDefOf.ShotFlash, color, 1.2f);
                    }
                    if (t.targets.Count == 0) Line(t.map, center - Vector3.right, center + Vector3.right, color, .3f);
                    Sound(SoundDefOf.Execute_Cut, t); break;
            }
            if (t.phenomenon == ArtifactPhenomenon.Smite || t.phenomenon == ArtifactPhenomenon.FlameWave || t.phenomenon == ArtifactPhenomenon.FrostNova)
                foreach (Vector3 point in t.targets) Particle(t.map, point, FleckDefOf.ShotFlash, color, .7f);
            MoteMaker.ThrowText(center, t.map, ArtifactIdentity.Label(t.phenomenon), color, .6f);
        }
        internal static void RegenerationPulse(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || !pawn.Spawned) return;
            Safely(() => Particle(pawn.Map, pawn.DrawPos, FleckDefOf.HealingCross, Vampire, .65f));
        }
    }
}
