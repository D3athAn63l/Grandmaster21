using System;
using System.Runtime.CompilerServices;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grandmaster21
{
    // One entry belongs to one WarmupComplete, never to a pawn or a time window.
    internal static class Gm21BeamParry
    {
        internal const int CounterStunTicks = 120;
        internal sealed class Attack
        {
            // Evaluated: an eligible Guardian was selected and its one roll is committed (final).
            // Selecting: Guardian selection is running; only ever true inside ShouldDamage.
            internal bool Evaluated;
            internal bool Defended;
            internal bool Selecting;
        }
        internal sealed class Scope
        {
            internal Verb Verb;
            internal Attack Attack;
            internal Scope Previous;
        }
        private static readonly ConditionalWeakTable<Verb, Attack> attacks =
            new ConditionalWeakTable<Verb, Attack>();
        [ThreadStatic] private static Scope current;

        internal static Attack Begin(Verb verb)
        {
            var attack = new Attack();
            attacks.Remove(verb);
            attacks.Add(verb, attack);
            return attack;
        }
        internal static Attack Find(Verb verb)
        {
            Attack attack;
            return verb != null && attacks.TryGetValue(verb, out attack) ? attack : null;
        }
        internal static void End(Verb verb, Attack attack)
        {
            if (attack != null && ReferenceEquals(Find(verb), attack)) attacks.Remove(verb);
        }
        internal static Scope Enter(Verb verb)
        {
            var scope = new Scope { Verb = verb, Attack = Find(verb), Previous = current };
            current = scope;
            return scope;
        }
        internal static void Exit(Scope scope)
        {
            if (scope != null) current = scope.Previous;
        }
        private static Attack Executing(Verb verb)
        {
            for (Scope s = current; s != null; s = s.Previous)
                if (ReferenceEquals(s.Verb, verb)) return s.Attack;
            return null;
        }
        internal static bool Blocked(Verb verb)
        {
            Attack attack = Executing(verb);
            return attack != null && attack.Defended;
        }
        // Called immediately after TryCastShot. False follows vanilla's own burst completion,
        // cooldown and callback path, without resetting another verb or cancelling a job.
        internal static bool ShotResult(bool result, Verb verb)
        {
            return result && (!(verb is Verb_ShootBeam) || !Blocked(verb));
        }

        internal static float BeamParryChance(Pawn guardian)
        {
            if (!Gm21Melee.IsMeleeGrandmaster(guardian) || !Gm21Melee.CanAct(guardian)) return 0f;
            if (Gm21Melee.Capacity(guardian, PawnCapacityDefOf.Manipulation) <= 0f) return 0f;
            ThingWithComps weapon = Gm21Melee.EquippedWeapon(guardian);
            if (weapon == null || weapon.def == null || !weapon.def.IsWeapon) return 0f;
            float quality = Gm21Melee.Precision(guardian) * Gm21Melee.Consciousness(guardian)
                          * Gm21Melee.DeflectionImplement(guardian);
            if (float.IsNaN(quality) || float.IsInfinity(quality) || quality <= 0f) return 0f;
            // Fixed anticipatory difficulty: readable, independent of beam propagation speed.
            return Mathf.Min(0.95f, quality / (quality + 1f));
        }

        internal static Pawn SelectGuardian(Pawn victim, Thing caster, out float chance)
        {
            chance = 0f;
            Pawn best = null;
            Map map = victim.Map;
            if (!victim.Spawned || map == null) return null;
            IntVec3 centre = victim.Position;
            int radius = Mathf.CeilToInt(Gm21Melee.ProtectiveRadius);
            // Same radius, protection, clear route and best-candidate policy as projectile
            // Guardians. Only scoring is beam-specific; projectile selection is untouched.
            for (int dz = -radius; dz <= radius; dz++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dz * dz > Gm21Melee.ProtectiveRadiusSquared) continue;
                var cell = new IntVec3(centre.x + dx, centre.y, centre.z + dz);
                if (!cell.InBounds(map)) continue;
                var things = map.thingGrid.ThingsListAtFast(cell);
                if (things == null) continue;
                for (int i = 0; i < things.Count; i++)
                {
                    Pawn candidate = things[i] as Pawn;
                    if (candidate == null || candidate == caster || !Gm21GuardianThreat.Protects(candidate, victim)) continue;
                    float score = BeamParryChance(candidate);
                    if (score <= chance || !Gm21Reach.CanDashTo(map, candidate.Position, victim.Position)) continue;
                    best = candidate;
                    chance = score;
                }
            }
            return best;
        }

        internal static bool ShouldDamage(Verb_ShootBeam verb, Thing thing, float damageFactor)
        {
            Attack attack = Executing(verb);
            if (attack == null) return true; // Loaded mid-burst / custom lifecycle: no new roll.
            if (attack.Defended) return false;
            if (!Gm21Melee.BeamParryEnabled || attack.Evaluated || attack.Selecting) return true;
            Pawn victim = thing as Pawn;
            var props = verb.verbProps;
            if (victim == null || victim.Dead || props == null || props.beamDamageDef == null
                || !props.beamDamageDef.harmsHealth || !(damageFactor > 0f)
                || !(props.beamTotalDamage > 0f || props.beamDamageDef.defaultDamage > 0)) return true;

            // One beam attack = one parry attempt, and an attempt exists only once an eligible
            // Guardian has been selected: no Guardian, no attempt. An unprotected contact, or a
            // protected one nobody can act for, proceeds vanilla and leaves the attempt available.
            // Selecting closes the selection window itself: a contact provoked from inside
            // selection (a foreign patch on a stat, hostility or route call) proceeds vanilla, so
            // it can neither recurse into selection nor resolve a roll this frame would repeat.
            Pawn guardian;
            float chance = 0f;
            attack.Selecting = true;
            try { guardian = SelectGuardian(victim, verb.Caster, out chance); }
            catch (Exception e)
            {
                // Selection failed before any Guardian was resolved: nothing was attempted, so
                // nothing is spent. No RNG runs on this path and a roll still requires a later
                // successful selection, so this cannot fish; this contact proceeds vanilla.
                Warn("guardian selection (this contact proceeds vanilla)", e);
                return true;
            }
            finally { attack.Selecting = false; }
            if (guardian == null) return true;

            // A Guardian exists and is about to roll: commit BEFORE the RNG and every
            // counter-effect, so nothing they trigger can fish for a second roll. Final from here.
            attack.Evaluated = true;
            try
            {
                if (!Rand.Chance(chance)) return true;
                attack.Defended = true;
                // Effects are independent; neither an immune caster nor a cosmetic error can
                // undo the already successful defence.
                try { CounterStun(verb, guardian); }
                catch (Exception e) { Warn("counter-stun", e); }
                try
                {
                    MoteMaker.ThrowText(guardian.DrawPos, guardian.Map,
                        "GM21_BeamParried".Translate(), Color.white);
                }
                catch (Exception e) { Warn("feedback", e); }
                return false;
            }
            catch (Exception e)
            {
                // The attempt was already spent on the selected Guardian; fail open, no retry.
                Warn("defence (this attack proceeds vanilla)", e);
                return !attack.Defended;
            }
        }

        internal static void CounterStun(Verb verb, Pawn guardian)
        {
            Pawn attacker = verb.Caster as Pawn;
            if (attacker == null || attacker.stances == null || attacker.stances.stunner == null) return;
            // Existing direct-fire philosophy: deliberate aim at a protected pawn is hostile;
            // accidental allied/neutral stray fire can be stopped, but never counter-stunned.
            Pawn intended = verb.CurrentTarget.Thing as Pawn;
            if (!GenHostility.HostileTo(attacker, guardian)
                && (intended == null || !Gm21GuardianThreat.Protects(guardian, intended))) return;
            if (DamageDefOf.Stun == null) return;
            // StunFor alone bypasses CanBeStunnedByDamage and resistance. Use the normal
            // notification pipeline, without calling TakeDamage or any damage worker.
            // Audited StunHandler scales Amount by 30 ticks, then applies immunity/resistance.
            var stun = new DamageInfo(DamageDefOf.Stun, CounterStunTicks / 30f,
                                      instigator: guardian);
            attacker.stances.stunner.Notify_DamageApplied(stun);
        }
        private static void Warn(string stage, Exception e)
        {
            Log.WarningOnce("[Grandmaster 21] Beam Parry " + stage + ": " + e.GetType().Name
                            + ": " + e.Message, 21216001 + (stage == "feedback" ? 1 : stage == "counter-stun" ? 2
                            : stage.StartsWith("guardian selection") ? 3 : 0));
        }
    }
}
