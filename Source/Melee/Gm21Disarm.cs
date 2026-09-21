using RimWorld;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// On-hit disarm: the strike that also takes the weapon out of the opponent's hands.
    ///
    /// WHAT A DISARM IS HERE. The weapon is DROPPED through the engine's own
    /// Pawn_EquipmentTracker.TryDropEquipment, so it lands on the ground as an ordinary Thing with
    /// ordinary ownership and forbidden state, can be picked up again by anyone including its
    /// original owner, and is hauled, traded, degraded and saved exactly like any other item. It
    /// is not destroyed, not teleported, and not removed from the world.
    ///
    /// WHAT IT IS NOT. It does not reach into another mod's weapon system. If a pawn's "weapon" is
    /// not in the equipment tracker at all -- a natural attack, an implanted gun, a framework that
    /// keeps armaments somewhere else -- this code simply never sees it and does nothing, which is
    /// the correct failure mode.
    /// </summary>
    internal static class Gm21Disarm
    {
        /// <summary>Disarm chance for a healthy Grandmaster against a healthy, lightly armed foe.</summary>
        private const float BaseChance = 0.25f;

        /// <summary>Ceiling: even a perfect Grandmaster does not strip every weapon every swing.</summary>
        private const float MaxChance = 0.75f;

        /// <summary>
        /// Weapon mass, in kg, that doubles the defender's grip. A heavy two-hander is genuinely
        /// harder to knock away than a knife -- more of it is held, and more of it is braced.
        /// </summary>
        private const float GripMassReference = 4f;

        /// <summary>A critical strike is a much better disarming opportunity.</summary>
        private const float CriticalFactor = 2.5f;

        /// <summary>
        /// How much harder a psychically bonded weapon is to remove. A persona weapon is not
        /// biologically integrated -- vanilla lets it be dropped -- but the bond is exactly the
        /// kind of "grip" the brief asks to respect, so it resists rather than being immune.
        /// </summary>
        private const float BondedGripFactor = 4f;

        /// <summary>Manipulation floor, so a zero-Manipulation target is not an automatic disarm.</summary>
        private const float MinGripManipulation = 0.05f;

        /// <summary>
        /// Attempts the disarm. Returns true only if a weapon actually left the target's hands.
        ///
        /// A Grandmaster who cannot grip cannot disarm: the attacker's precision composite is
        /// Manipulation-dominated, so zero Manipulation gives zero chance without a special case.
        /// </summary>
        internal static bool TryDisarm(Pawn attacker, Pawn victim, float precision, bool critical)
        {
            if (attacker == null || victim == null || precision <= 0f) return false;

            Pawn_EquipmentTracker eq = victim.equipment;
            if (eq == null) return false;

            ThingWithComps weapon = eq.Primary;
            if (weapon == null || weapon.def == null) return false;
            if (!weapon.def.IsWeapon) return false;

            // Integrated armament. Dropping it destroys it, which means it was never a separable
            // object -- a mechanoid's built-in gun, an implanted weapon. Not a disarm target.
            if (weapon.def.destroyOnDrop) return false;
            if (!weapon.def.destroyable) return false;

            if (!victim.Spawned || victim.Map == null) return false;

            float grip = Grip(victim, weapon, eq);
            if (grip <= 0f) return false;

            float chance = BaseChance * precision / grip;
            if (critical) chance *= CriticalFactor;
            if (chance <= 0f) return false;
            if (chance > MaxChance) chance = MaxChance;

            if (!Rand.Chance(chance)) return false;

            ThingWithComps dropped;
            bool ok;
            try
            {
                ok = eq.TryDropEquipment(weapon, out dropped, victim.Position, false);
            }
            catch
            {
                // Another mod's equipment system may refuse in ways vanilla never does. A failed
                // disarm is a non-event; it must never interrupt the strike that caused it.
                return false;
            }

            if (ok && dropped != null)
            {
                MoteMaker.ThrowText(victim.DrawPos, victim.Map,
                    "GM21_Melee_Disarmed".Translate(), 3.6f);
                return true;
            }
            return false;
        }

        /// <summary>
        /// How firmly the weapon is held: the defender's own manipulation, scaled by how much
        /// weapon there is to hold on to and by any psychic bond.
        /// </summary>
        private static float Grip(Pawn victim, ThingWithComps weapon, Pawn_EquipmentTracker eq)
        {
            float manip = Gm21Melee.Manipulation(victim);
            if (manip < MinGripManipulation) manip = MinGripManipulation;

            float mass = weapon.GetStatValue(StatDefOf.Mass);
            if (mass < 0f) mass = 0f;

            float grip = manip * (1f + mass / GripMassReference);

            if (eq.bondedWeapon == weapon)
            {
                grip *= BondedGripFactor;
            }
            return grip;
        }
    }
}
