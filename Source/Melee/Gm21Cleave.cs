using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// Cleave: one swing that carries through into a second enemy standing too close to the first.
    ///
    /// NOT AN AREA-OF-EFFECT PULSE. Each secondary target receives a REAL melee attack, scheduled
    /// through Gm21CombatScheduler and resolved by the same verb path as any other swing, so hit
    /// and dodge are rolled, armour resolves, a body part is chosen, the combat log records it and
    /// the target may parry -- including with a riposte, if the target is a Grandmaster too.
    /// Applying flat damage in a radius would have been a fraction of the code and none of the
    /// behaviour.
    ///
    /// DRIVEN BY PHYSICAL POWER AND BY THE WEAPON. A dagger has neither the mass nor the arc to
    /// carry through a second body; a two-handed hammer has both. The weapon term is mass-based
    /// rather than a list of weapon names, so a modded greatsword nobody has ever seen cleaves
    /// correctly the first time, and a modded dagger does not.
    /// </summary>
    internal static class Gm21Cleave
    {
        /// <summary>Cleave chance for an ordinary-power Grandmaster with a reference-mass weapon.</summary>
        private const float BaseChance = 0.25f;

        private const float MaxChance = 0.90f;

        /// <summary>
        /// Weapon mass, in kg, treated as "one unit of arc". Roughly a longsword. Bare hands and
        /// knives fall well below it, warhammers and greatswords well above.
        /// </summary>
        private const float ReferenceWeaponMass = 2f;

        private const float MinWeaponFactor = 0.25f;   // bare hands still carry a little through
        private const float MaxWeaponFactor = 4f;      // past this, more mass buys nothing

        /// <summary>
        /// Ceiling on secondary targets per swing. Not a balance dial so much as a bound on how
        /// much work one strike can schedule; four additional real melee resolutions from a single
        /// swing is already extraordinary.
        /// </summary>
        private const int MaxSecondaryTargets = 4;

        /// <summary>Reused per call on the main thread; cleave never runs concurrently with itself.</summary>
        [System.ThreadStatic] private static List<Pawn> candidateBuffer;

        internal static float WeaponFactor(float weaponMass)
        {
            return Mathf.Clamp(weaponMass / ReferenceWeaponMass, MinWeaponFactor, MaxWeaponFactor);
        }

        internal static float Chance(float power, float weaponMass)
        {
            float chance = BaseChance * power * WeaponFactor(weaponMass);
            if (chance <= 0f) return 0f;
            return chance > MaxChance ? MaxChance : chance;
        }

        internal static int MaxTargets(float power, float weaponMass)
        {
            int n = Mathf.FloorToInt(power * WeaponFactor(weaponMass));
            if (n < 1) n = 1;
            return n > MaxSecondaryTargets ? MaxSecondaryTargets : n;
        }

        /// <summary>
        /// Rolls for cleave and schedules the follow-through strikes.
        ///
        /// The roll is made once for the swing, not once per candidate: a cleave either carries
        /// through or it does not, and how many enemies it reaches is then a property of how much
        /// power and weapon is behind it.
        /// </summary>
        internal static void TryCleave(Pawn attacker, Thing primaryTarget, Verb verb, float power)
        {
            if (attacker == null || verb == null) return;
            if (!attacker.Spawned || attacker.Map == null) return;

            float weaponMass = Gm21Melee.WeaponMass(attacker);
            float chance = Chance(power, weaponMass);
            if (chance <= 0f || !Rand.Chance(chance)) return;

            int wanted = MaxTargets(power, weaponMass);

            List<Pawn> candidates = candidateBuffer;
            if (candidates == null) candidates = candidateBuffer = new List<Pawn>(8);
            candidates.Clear();

            CollectAdjacentHostiles(attacker, primaryTarget, verb, candidates);

            int taken = 0;
            for (int i = 0; i < candidates.Count && taken < wanted; i++)
            {
                Gm21CombatScheduler.ScheduleCleave(attacker, candidates[i]);
                taken++;
            }
            candidates.Clear();
        }

        /// <summary>
        /// Everything hostile the swing could physically reach, excluding the pawn it already hit.
        ///
        /// Scans the attacker's own cell and its eight neighbours by hand rather than through
        /// GenRadial: melee reach is one cell, the loop is nine ThingGrid lookups with no
        /// allocation and no enumerator, and this runs on every landed Grandmaster strike.
        /// Whether the swing can actually connect is then the verb's decision, not a distance
        /// guess, so a modded long-reach weapon and a modded reach-1 weapon both behave correctly.
        ///
        /// ALLIES ARE NEVER CANDIDATES. Only pawns hostile to the attacker are collected, so a
        /// cleave cannot turn into friendly fire no matter how crowded the melee is.
        /// </summary>
        private static void CollectAdjacentHostiles(Pawn attacker, Thing primaryTarget, Verb verb,
                                                    List<Pawn> into)
        {
            Map map = attacker.Map;
            IntVec3 centre = attacker.Position;

            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    IntVec3 cell = new IntVec3(centre.x + dx, centre.y, centre.z + dz);
                    if (!cell.InBounds(map)) continue;

                    List<Thing> things = map.thingGrid.ThingsListAtFast(cell);
                    if (things == null) continue;

                    for (int i = 0; i < things.Count; i++)
                    {
                        Pawn candidate = things[i] as Pawn;
                        if (candidate == null) continue;
                        if (candidate == attacker || candidate == primaryTarget) continue;
                        if (candidate.Dead || !candidate.Spawned) continue;
                        if (!GenHostility.HostileTo(candidate, attacker)) continue;
                        if (!verb.CanHitTarget(candidate)) continue;
                        if (into.Contains(candidate)) continue;

                        into.Add(candidate);
                    }
                }
            }
        }
    }
}
