using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grandmaster21
{
    /// <summary>Marker for "this projectile has already had its one interception chance".</summary>
    internal sealed class Gm21ProjectileMark
    {
        public bool evaluated;
    }

    /// <summary>
    /// Projectile interception, deflection, return-to-sender and safe redirection.
    ///
    /// THE PIPELINE, IN ORDER. Each stage is a separate question with its own stats, and failing a
    /// later stage never undoes an earlier one:
    ///
    ///   1. REACH       Can the Grandmaster get to it at all?      Movement speed vs projectile difficulty.
    ///   2. DEFLECT     Can they actually turn it?                 Precision and what is in their hands.
    ///   3. RETURN      Can they send it back to the shooter?      Finesse, opposed by the projectile.
    ///   4. SAFE VECTOR If not, where is the least bad place?      Local threat scoring, then strength.
    ///
    /// A failure at stage 3 does NOT mean the defence failed -- it means the object goes somewhere
    /// harmless instead of somewhere useful, which is the brief's explicit requirement.
    ///
    /// WHEN IT IS EVALUATED, AND WHY THAT MATTERS. Once per projectile, at the moment it first
    /// comes within the protective radius of where it is going -- computed from the projectile's
    /// own speed, so a grenade is caught fifteen ticks out and a bullet three. Evaluating at
    /// IMPACT would have been simpler and wrong: a grenade intercepted at the instant of impact has
    /// no fuse left to preserve, and the brief is specific that a returned grenade keeps its
    /// remaining fuse. Evaluating at LAUNCH would have been wrong too -- the Grandmaster would have
    /// to decide before the shot had travelled anywhere.
    ///
    /// COST. One weak-table lookup and one integer comparison per projectile per tick; everything
    /// else happens once, for the single tick a projectile spends entering the zone, and only when
    /// a Grandmaster is actually standing in it.
    ///
    /// NO EXPLOSION IMMUNITY, ANYWHERE. A redirected warhead is still armed, a redirected rocket
    /// still detonates where it lands, and if a blast radius is larger than the distance the
    /// Grandmaster managed to buy, the Grandmaster dies. That is intended.
    /// </summary>
    internal static class Gm21ProjectileDefence
    {
        // ---------------------------------------------------------------- tuning

        /// <summary>
        /// How much harder deflecting is than merely reaching. Feeds Gm21Melee.Opposed.
        /// </summary>
        private const float DeflectHardness = 0.6f;

        /// <summary>
        /// Returning is much harder than deflecting: anyone can knock a thing away, putting it
        /// back down the line it came from is a different skill entirely.
        /// </summary>
        private const float ReturnHardness = 1.5f;

        private const float MaxDeflectChance = 0.99f;
        private const float MaxReturnChance = 0.95f;

        /// <summary>
        /// Explosion radius, in tiles, that doubles a projectile's interception difficulty. A
        /// warhead is bigger, heavier, armed and unstable; this is how that is expressed without a
        /// single hardcoded weapon name.
        /// </summary>
        private const float ExplosionDifficultyReference = 4f;

        /// <summary>
        /// Chance that an explosive whose deflection FAILED detonates right there, against the
        /// Grandmaster, rather than continuing on its original path. The brief's "rough
        /// deflection": they got a hand to it, and that was the problem.
        /// </summary>
        private const float RoughDetonationChance = 0.5f;

        /// <summary>
        /// Base distance, in tiles, that a deflected slow object is sent, before physical power
        /// and weapon leverage multiply it.
        /// </summary>
        private const float BaseRedirectDistance = 4f;

        /// <summary>Weapon mass, in kg, worth one extra unit of leverage when swatting something away.</summary>
        private const float LeverageMassReference = 4f;

        /// <summary>
        /// Above this speed, in cells per second, a projectile is not "thrown" anywhere -- it is
        /// deflected, and what changes is its ANGLE, not how far a strong arm can hurl it. The
        /// brief is explicit that strength should not simply decide a bullet's travel distance.
        /// </summary>
        private const float FastProjectileSpeed = 30f;

        /// <summary>How far a fast deflected projectile is sent along its new vector.</summary>
        private const float FastRedirectDistance = 40f;

        /// <summary>Absolute ceiling, so an absurd pawn cannot produce a nonsensical destination.</summary>
        private const float MaxRedirectDistance = 200f;

        /// <summary>Minimum evaluation window, in ticks, so an extremely fast projectile still gets one.</summary>
        private const int MinWindowTicks = 2;

        private const int MaxWindowTicks = 240;

        // ---------------------------------------------------------------- reflection

        private static FieldInfo equipmentField;
        private static FieldInfo equipmentDefField;
        private static FieldInfo equipmentQualityField;
        private static FieldInfo destinationField;
        private static FieldInfo ticksToImpactField;
        private static FieldInfo ticksToDetonationField;

        private static readonly ConditionalWeakTable<Thing, Gm21ProjectileMark> Marks =
            new ConditionalWeakTable<Thing, Gm21ProjectileMark>();

        private static readonly ConditionalWeakTable<Thing, Gm21ProjectileMark>.CreateValueCallback MarkFactory =
            _ => new Gm21ProjectileMark();

        /// <summary>
        /// Resolves the projectile internals this feature needs and patches the flight tick.
        ///
        /// TickInterval is preferred over Tick because 1.6 drives things through the interval
        /// form; Tick is the fallback for an environment where it is not present. Only ONE of them
        /// is patched, so a subclass that overrides one and delegates to the other cannot be
        /// evaluated twice -- though the once-only mark would make that harmless anyway.
        ///
        /// Patching the BASE Projectile method rather than every subclass override is deliberate:
        /// every subclass's flight tick delegates to the base one to actually move, so the base is
        /// the single point every projectile in the game passes through, including modded ones
        /// this mod has never seen. A hypothetical subclass that reimplements flight from scratch
        /// simply would not be interceptable, which is a graceful degradation rather than a crash.
        /// </summary>
        internal static bool Apply(Harmony harmony, Func<Type, string, HarmonyMethod> hook)
        {
            equipmentField = AccessTools.Field(typeof(Projectile), "equipment");
            equipmentDefField = AccessTools.Field(typeof(Projectile), "equipmentDef");
            equipmentQualityField = AccessTools.Field(typeof(Projectile), "equipmentQuality");
            destinationField = AccessTools.Field(typeof(Projectile), "destination");
            ticksToImpactField = AccessTools.Field(typeof(Projectile), "ticksToImpact");
            ticksToDetonationField = AccessTools.Field(typeof(Projectile_Explosive), "ticksToDetonation");

            if (ticksToImpactField == null || destinationField == null)
            {
                Log.Warning("[Grandmaster 21] Projectile internals not found (ticksToImpact="
                            + (ticksToImpactField != null) + ", destination="
                            + (destinationField != null) + "); projectile defence is disabled.");
                return false;
            }

            MethodInfo tick = AccessTools.Method(typeof(Projectile), "TickInterval", new[] { typeof(int) })
                           ?? AccessTools.Method(typeof(Projectile), "Tick");
            if (tick == null)
            {
                Log.Warning("[Grandmaster 21] Projectile flight tick not found; projectile defence "
                            + "is disabled.");
                return false;
            }

            try
            {
                harmony.Patch(tick, prefix: hook(typeof(Gm21ProjectileDefence), nameof(Prefix_ProjectileFlight)));
            }
            catch (Exception e)
            {
                Log.Error("[Grandmaster 21] Failed to patch projectile flight: " + e);
                return false;
            }

            if (ticksToDetonationField == null)
            {
                Log.Warning("[Grandmaster 21] Projectile_Explosive.ticksToDetonation not found; a "
                            + "redirected explosive will not carry its remaining fuse across the "
                            + "redirection. Everything else about projectile defence is unaffected.");
            }
            return true;
        }

        // ---------------------------------------------------------------- flight hook

        /// <summary>
        /// The per-tick gate. Three cheap tests reject every projectile in the game that is not,
        /// right now, entering a Grandmaster's protective zone.
        /// </summary>
        internal static void Prefix_ProjectileFlight(Projectile __instance)
        {
            if (!Gm21Melee.ProjectileDefenceEnabled) return;

            Gm21ProjectileMark mark;
            if (Marks.TryGetValue(__instance, out mark) && mark.evaluated) return;

            try
            {
                Evaluate(__instance);
            }
            catch (Exception e)
            {
                // A projectile tick runs for every shot in the game. Nothing in here is worth
                // breaking combat over, so a failure marks the projectile handled and lets it fly.
                Marks.GetValue(__instance, MarkFactory).evaluated = true;
                Log.Error("[Grandmaster 21] Projectile defence failed; the projectile continues "
                          + "normally: " + e);
            }
        }

        private static void Evaluate(Projectile proj)
        {
            if (!proj.Spawned) return;
            Map map = proj.Map;
            if (map == null) return;

            ThingDef def = proj.def;
            ProjectileProperties props = def == null ? null : def.projectile;
            if (props == null) return;

            // Overhead shells arrive from above, outside anything a hand weapon can reach. Vanilla
            // does not allow them to be intercepted in flight either.
            if (props.flyOverhead) return;

            // Every cheap def read happens BEFORE the reflective field read, because this runs
            // once per projectile per tick for every shot in flight on the map and the reflection
            // is by far the most expensive line in it.
            float perTick = props.SpeedTilesPerTick;
            if (perTick <= 0f) return;

            object ticksObj = ticksToImpactField.GetValue(proj);
            if (!(ticksObj is int)) return;
            int ticksToImpact = (int)ticksObj;

            int window = Mathf.CeilToInt(Gm21Melee.ProtectiveRadius / perTick);
            if (window < MinWindowTicks) window = MinWindowTicks;
            if (window > MaxWindowTicks) window = MaxWindowTicks;

            // Not in the zone yet. Left unmarked on purpose: it will be asked again next tick.
            if (ticksToImpact > window) return;

            Marks.GetValue(proj, MarkFactory).evaluated = true;

            Pawn guardian;
            float distance;
            if (!FindGuardian(proj, map, out guardian, out distance)) return;

            float difficulty = Difficulty(props);

            // ---- stage 1: reach ----------------------------------------------------
            float reach = Gm21InterceptCurve.Evaluate(
                              Gm21Melee.MoveSpeed(guardian) * Gm21Melee.Reaction(guardian) / difficulty)
                        * Gm21InterceptCurve.DistanceFactor(distance);
            if (!Rand.Chance(reach)) return;

            // ---- stage 2: deflect --------------------------------------------------
            float quality = DeflectionQuality(guardian);
            if (!Rand.Chance(Gm21Melee.Opposed(quality, difficulty, DeflectHardness, MaxDeflectChance)))
            {
                RoughDeflection(proj, props, guardian);
                return;
            }

            // ---- stage 3: return to sender ----------------------------------------
            if (Rand.Chance(Gm21Melee.Opposed(quality, difficulty, ReturnHardness, MaxReturnChance))
                && TryReturnToSender(proj, guardian, map))
            {
                Announce(guardian, "GM21_Melee_Returned");
                return;
            }

            // ---- stage 4: safe deflection -----------------------------------------
            if (TrySafeDeflection(proj, props, guardian, map))
            {
                Announce(guardian, "GM21_Melee_Deflected");
            }
        }

        // ---------------------------------------------------------------- difficulty

        /// <summary>
        /// How hard this projectile is to get a weapon onto, on the same scale as a pawn's
        /// effective movement speed.
        ///
        /// SPEED is the primary term, measured against Gm21Melee.ReferenceProjectileSpeed -- which
        /// is a thrown grenade's speed, doubled, the doubling being the Grandmaster's mastery
        /// itself expressed as one number instead of scattered bonuses.
        ///
        /// EXPLOSION RADIUS is the secondary term, and it is what makes a rocket harder than a
        /// bullet despite being slower: it stands in for size, mass, and the fact that the thing
        /// is armed. It is also what makes a doomsday rocket nearly untouchable without any
        /// special case for doomsday rockets.
        ///
        /// Everything here is read from the projectile's own def, so a modded hypersonic round is
        /// automatically extreme and a modded thrown rock is automatically easy, with no list to
        /// maintain.
        /// </summary>
        internal static float Difficulty(ProjectileProperties props)
        {
            float speed = props.speed;
            if (speed <= 0f) speed = 1f;

            float radius = props.explosionRadius;
            if (radius < 0f) radius = 0f;

            float difficulty = (speed / Gm21Melee.ReferenceProjectileSpeed)
                             * (1f + radius / ExplosionDifficultyReference);
            return difficulty < 0.1f ? 0.1f : difficulty;
        }

        /// <summary>
        /// The Grandmaster's ability to turn a flying object: fine control, awareness, and
        /// something rigid to do it with. Bare hands score badly enough to make slow thrown
        /// objects plausible and bullets essentially not, which is what the brief asks for. Zero
        /// Manipulation scores zero, so a pawn with no usable arms deflects nothing at all.
        /// </summary>
        internal static float DeflectionQuality(Pawn pawn)
        {
            return Gm21Melee.Precision(pawn)
                 * Gm21Melee.Consciousness(pawn)
                 * Gm21Melee.DeflectionImplement(pawn);
        }

        // ---------------------------------------------------------------- guardian search

        /// <summary>
        /// Finds a Grandmaster standing within the protective radius of where this projectile is
        /// going, on the right side of the fight, with an unobstructed view of the impact point.
        /// </summary>
        private static bool FindGuardian(Projectile proj, Map map, out Pawn guardian, out float distance)
        {
            guardian = null;
            distance = 0f;

            Thing launcher = proj.Launcher;
            IntVec3 impact = DestinationCellOf(proj);
            if (!impact.IsValid || !impact.InBounds(map)) return false;

            int radius = Mathf.CeilToInt(Gm21Melee.ProtectiveRadius);

            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int distSquared = dx * dx + dz * dz;
                    if (distSquared > Gm21Melee.ProtectiveRadiusSquared) continue;

                    IntVec3 cell = new IntVec3(impact.x + dx, impact.y, impact.z + dz);
                    if (!cell.InBounds(map)) continue;

                    List<Thing> things = map.thingGrid.ThingsListAtFast(cell);
                    if (things == null) continue;

                    for (int i = 0; i < things.Count; i++)
                    {
                        Pawn candidate = things[i] as Pawn;
                        if (candidate == null) continue;
                        if (!Gm21Melee.IsMeleeGrandmaster(candidate)) continue;
                        if (!Gm21Melee.CanAct(candidate)) continue;
                        if (!IsWorthDefending(candidate, launcher, proj)) continue;

                        // No reaching through walls, here or anywhere else in this package.
                        if (!GenSight.LineOfSight(candidate.Position, impact, map)) continue;

                        guardian = candidate;
                        distance = Mathf.Sqrt(distSquared);
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Where this projectile is going.
        ///
        /// Projectile.DestinationCell is PROTECTED, so it cannot be read from outside the class
        /// hierarchy -- which is exactly the kind of thing that compiles against a guess and
        /// fails against the real assembly. The underlying `destination` field is read instead,
        /// through the handle already resolved for redirection, and converted the same way the
        /// property does.
        /// </summary>
        private static IntVec3 DestinationCellOf(Projectile proj)
        {
            object value = destinationField.GetValue(proj);
            if (!(value is Vector3)) return IntVec3.Invalid;
            return ((Vector3)value).ToIntVec3();
        }

        /// <summary>
        /// A Grandmaster defends against incoming fire, not their own side's outgoing fire.
        ///
        /// Both halves are checked because either alone is wrong: a projectile from a friendly
        /// shooter is never swatted even if it is heading somewhere awkward, and a projectile
        /// aimed at an enemy is never swatted even if it passes close by.
        /// </summary>
        private static bool IsWorthDefending(Pawn guardian, Thing launcher, Projectile proj)
        {
            if (launcher == guardian) return false;
            if (launcher != null && !GenHostility.HostileTo(launcher, guardian)) return false;

            Thing intended = proj.intendedTarget.Thing;
            if (intended != null && intended != guardian && GenHostility.HostileTo(intended, guardian))
            {
                return false;
            }
            return true;
        }

        // ---------------------------------------------------------------- outcomes

        /// <summary>
        /// The Grandmaster got a weapon onto it and could not turn it.
        ///
        /// For an inert projectile that is simply a failure and it continues untouched. For an
        /// explosive it is the brief's "rough deflection": contact with an armed warhead is itself
        /// a way to set it off, and when that happens it goes off HERE -- next to the Grandmaster
        /// who reached for it. Nothing protects them from their own blast radius.
        /// </summary>
        private static void RoughDeflection(Projectile proj, ProjectileProperties props, Pawn guardian)
        {
            if (props.explosionRadius <= 0f) return;
            if (!Rand.Chance(RoughDetonationChance)) return;

            DetonateHere(proj);
            Announce(guardian, "GM21_Melee_RoughDeflection");
        }

        /// <summary>
        /// Brings the impact forward to where the projectile is right now, using the engine's own
        /// impact path rather than calling an explosion directly -- so the warhead, its damage,
        /// its fire chance and everything else about it behave exactly as they always would.
        /// </summary>
        private static void DetonateHere(Projectile proj)
        {
            destinationField.SetValue(proj, proj.ExactPosition);
            ticksToImpactField.SetValue(proj, 1);
        }

        /// <summary>
        /// RETURN TO SENDER. The projectile itself is redirected at the thing that fired it -- not
        /// deleted and not replaced by damage applied directly to the shooter, so it travels, can
        /// be blocked, can miss, and arrives carrying exactly what it left with.
        /// </summary>
        private static bool TryReturnToSender(Projectile proj, Pawn guardian, Map map)
        {
            Thing sender = proj.Launcher;
            if (sender == null || sender.Destroyed || !sender.Spawned) return false;
            if (sender.Map != map) return false;

            return Redirect(proj, guardian, sender, sender.Position);
        }

        /// <summary>
        /// SAFE DEFLECTION. The return failed, so the object goes somewhere it can do the least
        /// harm. This is still a success: the Grandmaster and everyone near them are out of its way.
        /// </summary>
        private static bool TrySafeDeflection(Projectile proj, ProjectileProperties props,
                                              Pawn guardian, Map map)
        {
            float distance = RedirectDistance(props, guardian);
            IntVec3 target = Gm21SafeVector.Choose(proj, props, guardian, map, distance);
            if (!target.IsValid) return false;

            return Redirect(proj, guardian, null, target);
        }

        /// <summary>
        /// How far a deflected object is sent.
        ///
        /// SLOW OBJECTS -- a thrown grenade, a rock -- are genuinely thrown, and the distance is
        /// physical power times weapon leverage. A normal colonist manages a handful of tiles; a
        /// superhuman one with a long heavy weapon manages an absurd number, which is intended.
        ///
        /// FAST OBJECTS are not thrown anywhere. A bullet is deflected: what changes is its ANGLE,
        /// and its remaining travel is a property of the bullet, not the arm. The brief is explicit
        /// about this, and it is why strength does not appear in this branch at all.
        /// </summary>
        internal static float RedirectDistance(ProjectileProperties props, Pawn guardian)
        {
            if (props.speed >= FastProjectileSpeed) return FastRedirectDistance;

            float leverage = 1f + Gm21Melee.WeaponMass(guardian) / LeverageMassReference;
            float distance = BaseRedirectDistance * Gm21Melee.Power(guardian) * leverage;

            if (distance < 1f) distance = 1f;
            return distance > MaxRedirectDistance ? MaxRedirectDistance : distance;
        }

        /// <summary>
        /// Re-launches the SAME projectile object on a new trajectory.
        ///
        /// WHY THE LAUNCHER BECOMES THE GRANDMASTER. A projectile cannot hit its own launcher, so
        /// leaving the original shooter as the launcher would make return-to-sender silently
        /// impossible. Handing the projectile to the Grandmaster is also simply true: they are the
        /// one who sent it where it is now going, and anything it hits is theirs.
        ///
        /// WHAT IS PRESERVED, AND WHY BY HAND. Launch overwrites the equipment fields, and those
        /// fields are what Projectile.DamageAmount and ArmorPenetration are computed from. Saving
        /// and restoring them is what makes "the projectile retains its original damage" literally
        /// true rather than approximately true -- the Grandmaster redirects the shot, they do not
        /// improve it. The explosive fuse is restored for the same reason: a returned grenade
        /// keeps the fuse it had, so catching one late is dangerous, exactly as it should be.
        /// </summary>
        private static bool Redirect(Projectile proj, Pawn guardian, Thing targetThing, IntVec3 targetCell)
        {
            object savedEquipment = equipmentField != null ? equipmentField.GetValue(proj) : null;
            object savedEquipmentDef = equipmentDefField != null ? equipmentDefField.GetValue(proj) : null;
            object savedQuality = equipmentQualityField != null ? equipmentQualityField.GetValue(proj) : null;
            object savedFuse = null;
            if (ticksToDetonationField != null && proj is Projectile_Explosive)
            {
                savedFuse = ticksToDetonationField.GetValue(proj);
            }

            LocalTargetInfo target = targetThing != null
                ? new LocalTargetInfo(targetThing)
                : new LocalTargetInfo(targetCell);

            try
            {
                proj.Launch(guardian, proj.ExactPosition, target, target, proj.HitFlags,
                            false, savedEquipment as Thing, null);
            }
            catch (Exception e)
            {
                Log.Error("[Grandmaster 21] Could not redirect a projectile; it continues on its "
                          + "original path: " + e);
                return false;
            }

            if (equipmentDefField != null && savedEquipmentDef != null)
            {
                equipmentDefField.SetValue(proj, savedEquipmentDef);
            }
            if (equipmentQualityField != null && savedQuality != null)
            {
                equipmentQualityField.SetValue(proj, savedQuality);
            }
            if (savedFuse != null)
            {
                ticksToDetonationField.SetValue(proj, savedFuse);
            }
            return true;
        }

        private static void Announce(Pawn guardian, string key)
        {
            if (guardian == null || guardian.Map == null) return;
            MoteMaker.ThrowText(guardian.DrawPos, guardian.Map, key.Translate(), 3.8f);
        }
    }
}
