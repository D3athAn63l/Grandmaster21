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
        /// Guardian selection has to compare candidates by their real interception probability,
        /// which means it needs the same two numbers stage 4 will use. Exposed rather than
        /// duplicated, so the score a Guardian is chosen on and the roll they then make cannot
        /// drift apart.
        /// </summary>
        internal const float DeflectHardnessValue = DeflectHardness;
        internal const float MaxDeflectChanceValue = MaxDeflectChance;

        /// <summary>
        /// How much easier a nearly-correct friendly shot is to salvage than a wildly wrong one.
        ///
        /// A round already travelling more or less at the enemy it was aimed at needs a nudge, and
        /// the factor bottoms out well below 1 -- easier than returning a shot to its sender. A
        /// round travelling in completely the wrong direction needs to be turned around, and the
        /// factor tops out at 3 -- considerably harder. The brief is explicit that angular
        /// correction should dominate this roll, and this is the term that makes it do so.
        /// </summary>
        private const float RecoveryMinFactor = 0.35f;
        private const float RecoveryMaxFactor = 3.0f;

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
        private static FieldInfo originField;
        private static FieldInfo destinationField;
        private static FieldInfo ticksToImpactField;
        private static FieldInfo ticksToDetonationField;

        private static readonly ConditionalWeakTable<Thing, Gm21ProjectileMark> Marks =
            new ConditionalWeakTable<Thing, Gm21ProjectileMark>();

        private static readonly ConditionalWeakTable<Thing, Gm21ProjectileMark>.CreateValueCallback MarkFactory =
            _ => new Gm21ProjectileMark();

        /// <summary>
        /// Which Grandmaster last altered a given projectile's course.
        ///
        /// Kept HERE rather than on the projectile because the projectile's launcher is deliberately
        /// left alone wherever possible: if Bob fired the shot, Bob remains its launcher, so Bob
        /// keeps the kill, the XP and whatever a third-party mod reads off it. The Grandmaster
        /// changed a trajectory; they did not fire Bob's weapon, and pretending otherwise would
        /// quietly rewrite attribution across every mod that inspects a projectile.
        ///
        /// Weak, so a redirected projectile's entry disappears with the projectile, and never
        /// serialised -- it is transient combat state like everything else in this package.
        /// </summary>
        private static readonly ConditionalWeakTable<Thing, Pawn> Redirectors =
            new ConditionalWeakTable<Thing, Pawn>();

        /// <summary>The Grandmaster who redirected this projectile, or null. Read by the test suite.</summary>
        internal static Pawn RedirectorOf(Thing projectile)
        {
            Pawn guardian;
            return (projectile != null && Redirectors.TryGetValue(projectile, out guardian)) ? guardian : null;
        }

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
        /// <summary>
        /// Resolves every projectile internal this feature reaches for.
        ///
        /// Split out of Apply so it can be driven independently. Apply is the game's entry point
        /// and does far more than this -- it patches -- but the offline logic suite needs the
        /// handles without the patching, and a suite running against null handles would silently
        /// exercise the wrong code paths rather than the real ones.
        /// </summary>
        internal static void ResolveFields()
        {
            equipmentField = AccessTools.Field(typeof(Projectile), "equipment");
            equipmentDefField = AccessTools.Field(typeof(Projectile), "equipmentDef");
            equipmentQualityField = AccessTools.Field(typeof(Projectile), "equipmentQuality");
            originField = AccessTools.Field(typeof(Projectile), "origin");
            destinationField = AccessTools.Field(typeof(Projectile), "destination");
            ticksToImpactField = AccessTools.Field(typeof(Projectile), "ticksToImpact");
            ticksToDetonationField = AccessTools.Field(typeof(Projectile_Explosive), "ticksToDetonation");
        }

        internal static bool Apply(Harmony harmony, Func<Type, string, HarmonyMethod> hook)
        {
            ResolveFields();

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

            // Marked BEFORE the threat test, and deliberately so. A projectile that threatens
            // nobody would answer the same question identically on every later tick, and one this
            // package has just REDIRECTED must not be caught again by a second Grandmaster near
            // its new destination -- that way lies a projectile ping-ponging between Guardians.
            // One projectile, one Guardian, one attempt.
            Marks.GetValue(proj, MarkFactory).evaluated = true;

            IntVec3 impactCell = DestinationCellOf(proj);
            if (!impactCell.IsValid || !impactCell.InBounds(map)) return;

            // ---- STAGE 1: is this projectile worth reacting to at all? ---------------
            //
            // Everything below this line is the expensive part, and none of it runs for a round
            // that threatens nobody the Guardian protects. This is the whole doctrine: a Melee
            // Grandmaster does not fight projectiles because they exist nearby, only when one
            // matters. See Gm21GuardianThreat.
            //
            // ---- STAGE 2: which Grandmaster answers it? ------------------------------
            //
            // Resolved together, because choosing the best Guardian means comparing their real
            // interception probabilities, which requires the threat's difficulty.
            Gm21Threat threat;
            if (!Gm21GuardianThreat.TryResolve(proj, props, map, impactCell, out threat)) return;

            Pawn guardian = threat.guardian;
            float difficulty = Difficulty(props);

            // ---- STAGE 3: can they get there in time? --------------------------------
            //
            // Movement speed dominant, already computed during selection so it is not rolled
            // against a different number than the one the Guardian was chosen on.
            if (!Rand.Chance(threat.reachChance)) return;

            // ---- STAGE 4: can they actually turn it? ---------------------------------
            float quality = DeflectionQuality(guardian);
            if (!Rand.Chance(Gm21Melee.Opposed(quality, difficulty, DeflectHardness, MaxDeflectChance)))
            {
                RoughDeflection(proj, props, guardian);
                return;
            }

            // The catch itself succeeded, so show it: the pawn never leaves their cell, but the
            // dash and the contact are real events and deserve to be visible.
            Gm21GuardianFx.MicroDash(guardian, threat.victim, proj);

            // ---- STAGE 5: precision redirect, chosen by INTENT ------------------------
            if (threat.intent == Gm21ThreatIntent.Hostile)
            {
                // Hostile fire, or a friendly who deliberately aimed a DIRECT shot at someone
                // protected. Either way it goes back where it came from.
                if (Rand.Chance(Gm21Melee.Opposed(quality, difficulty, ReturnHardness, MaxReturnChance))
                    && TryReturnToSender(proj, guardian, map))
                {
                    Announce(guardian, "GM21_Melee_Returned");
                    return;
                }
            }
            else if (threat.intent == Gm21ThreatIntent.FriendlyExplosive)
            {
                // An ally's live warhead. Never returned to them; put at the enemy if one is
                // reachable, and at open ground if not.
                if (TryExplosiveRecovery(proj, props, guardian, map, quality, difficulty))
                {
                    Announce(guardian, "GM21_Melee_Recovered");
                    return;
                }
            }
            else if (TryFriendlyRecovery(proj, props, guardian, map, quality, difficulty))
            {
                Announce(guardian, "GM21_Melee_Recovered");
                return;
            }

            // ---- STAGE 6: safe deflection --------------------------------------------
            if (TrySafeDeflection(proj, props, guardian, threat.victim, map))
            {
                Announce(guardian, "GM21_Melee_Deflected");
            }

            // ---- STAGE 7: whatever happens, the payload is still the payload. --------
            // Nothing above disarms a warhead, shortens a fuse or reduces damage.
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

            // The ONE case where the launcher has to change: a projectile cannot hit its own
            // launcher, so leaving the original shooter there would make return-to-sender
            // silently impossible. The Grandmaster genuinely did send this one.
            return Redirect(proj, guardian, guardian, sender, sender.Position);
        }

        /// <summary>
        /// FRIENDLY RECOVERY -- salvaging an ally's mis-resolved shot back toward the enemy it was
        /// aimed at.
        ///
        /// This is corrective, not offensive, and the distinction has teeth. The round is never
        /// sent back to the ally who fired it, and it is never handed to some other convenient
        /// enemy either: it goes to the ORIGINAL INTENDED TARGET or nowhere. Allowing a
        /// Grandmaster to pick a better enemy would turn them into a free targeting computer,
        /// which is precisely what the anti-abuse rules exist to prevent.
        ///
        /// Bob remains the launcher throughout, so Bob keeps the shot, the kill and the XP. The
        /// Grandmaster bent a trajectory; they did not fire Bob's weapon.
        ///
        /// DIFFICULTY IS ANGULAR. A round already flying more or less at the right enemy needs a
        /// nudge and is easier to save than a return-to-sender; a round travelling in completely
        /// the wrong direction has to be turned around and is much harder. That is the whole
        /// physical intuition, and CorrectionFactor is where it lives.
        /// </summary>
        private static bool TryFriendlyRecovery(Projectile proj, ProjectileProperties props,
                                                Pawn guardian, Map map, float quality, float difficulty)
        {
            Thing intended = proj.intendedTarget.Thing;
            if (intended == null || intended.Destroyed || !intended.Spawned) return false;
            if (intended.Map != map) return false;

            // The original target must still be a legitimate enemy. If the raider it was meant for
            // has since been downed and captured, there is nothing to salvage the shot toward.
            if (!GenHostility.HostileTo(intended, guardian)) return false;

            float corrected = difficulty * CorrectionFactor(proj, intended);
            if (!Rand.Chance(Gm21Melee.Opposed(quality, corrected, ReturnHardness, MaxReturnChance)))
            {
                return false;
            }

            // Launcher deliberately preserved -- see the summary above.
            return Redirect(proj, guardian, proj.Launcher, intended, intended.Position);
        }

        /// <summary>
        /// FRIENDLY EXPLOSIVE RECOVERY -- getting an ally's live warhead away from our own people.
        ///
        /// Deliberately NOT the direct-fire rule. A stray bullet is salvaged toward the specific
        /// enemy it was aimed at, so that a Grandmaster cannot be used as free aim correction. An
        /// explosive has no such shot to reconstruct: it is an area weapon, and the Grandmaster
        /// simply picks the best place for it to go off. Whether the ally meant it is not asked
        /// here and was not asked when it was classified -- see Gm21GuardianThreat.ClassifyIntent.
        ///
        /// It is never sent back at the launcher, so the ally is never punished for a grenade,
        /// however it was thrown.
        ///
        /// NOTHING HERE IS AUTOMATIC. Reaching it and getting hold of it were stages 3 and 4, and
        /// both could have failed. This roll can fail too, and then the explosive falls through to
        /// safe disposal. The fuse keeps running throughout, the payload is untouched, and a
        /// warhead that was going to be lethal still is.
        /// </summary>
        private static bool TryExplosiveRecovery(Projectile proj, ProjectileProperties props,
                                                 Pawn guardian, Map map, float quality, float difficulty)
        {
            IntVec3 from = proj.ExactPosition.ToIntVec3();
            if (!from.InBounds(map)) from = guardian.Position;

            IntVec3 destination = Gm21ExplosiveDisposal.ChooseHostileDestination(
                guardian, map, from, props.explosionRadius, RedirectDistance(props, guardian));

            // No hostile worth throwing at, or none reachable. The caller falls through to safe
            // disposal, which is a success too -- it is still away from us.
            if (!destination.IsValid) return false;

            float corrected = difficulty * CorrectionFactorToCell(proj, destination);
            if (!Rand.Chance(Gm21Melee.Opposed(quality, corrected, ReturnHardness, MaxReturnChance)))
            {
                return false;
            }

            // Launcher preserved. The ally threw it; the Grandmaster only decided where it lands.
            return Redirect(proj, guardian, proj.Launcher, null, destination);
        }

        /// <summary>
        /// How far off course the shot already is, as a multiplier on its difficulty.
        ///
        /// 0 degrees of correction bottoms out at RecoveryMinFactor, 180 degrees tops out at
        /// RecoveryMaxFactor, and everything between interpolates on the cosine -- which is the
        /// natural measure here, because what actually matters is how much of the projectile's
        /// existing momentum points the right way.
        /// </summary>
        internal static float CorrectionFactor(Projectile proj, Thing intended)
        {
            return CorrectionFactorToCell(proj, intended.Position);
        }

        /// <summary>Same measure, against a bare cell -- what explosive redirection aims at.</summary>
        internal static float CorrectionFactorToCell(Projectile proj, IntVec3 destination)
        {
            Vector3 heading = Heading(proj);
            Vector3 wanted = new Vector3(destination.x - proj.ExactPosition.x, 0f,
                                         destination.z - proj.ExactPosition.z);

            float hm = heading.magnitude, wm = wanted.magnitude;
            if (hm <= 0.0001f || wm <= 0.0001f)
            {
                // Degenerate geometry: treat it as a middling correction rather than a free one.
                return (RecoveryMinFactor + RecoveryMaxFactor) * 0.5f;
            }

            float cos = (heading.x * wanted.x + heading.z * wanted.z) / (hm * wm);
            if (cos > 1f) cos = 1f;
            if (cos < -1f) cos = -1f;

            float correction = (1f - cos) * 0.5f;   // 0 = already aimed right, 1 = exactly backwards
            return RecoveryMinFactor + (RecoveryMaxFactor - RecoveryMinFactor) * correction;
        }

        /// <summary>The direction this projectile is travelling, from its launch geometry.</summary>
        private static Vector3 Heading(Projectile proj)
        {
            if (originField == null || destinationField == null) return Vector3.zero;
            object o = originField.GetValue(proj);
            object d = destinationField.GetValue(proj);
            if (!(o is Vector3) || !(d is Vector3)) return Vector3.zero;
            Vector3 origin = (Vector3)o, destination = (Vector3)d;
            return new Vector3(destination.x - origin.x, 0f, destination.z - origin.z);
        }

        /// <summary>
        /// SAFE DEFLECTION. The return failed, so the object goes somewhere it can do the least
        /// harm. This is still a success: the Grandmaster and everyone near them are out of its way.
        /// </summary>
        private static bool TrySafeDeflection(Projectile proj, ProjectileProperties props,
                                              Pawn guardian, Pawn saved, Map map)
        {
            float distance = RedirectDistance(props, guardian);
            IntVec3 target = Gm21SafeVector.Choose(proj, props, guardian, saved, map, distance);
            if (!target.IsValid) return false;

            // Launcher preserved: a deflection into open space is not the Grandmaster's shot, and
            // whatever it eventually lands on belongs to whoever pulled the trigger.
            return Redirect(proj, guardian, proj.Launcher, null, target);
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
        /// WHO THE LAUNCHER BECOMES IS THE CALLER'S DECISION, and it is an attribution decision.
        /// The default everywhere is to PRESERVE the original launcher, so the shot stays the
        /// shooter's for kills, XP and any mod that reads a projectile's origin. Exactly one case
        /// overrides that -- return-to-sender -- because a projectile cannot hit its own launcher
        /// and leaving the sender in place would make the whole manoeuvre silently impossible.
        /// Either way the Grandmaster is recorded in Redirectors, so the information is kept
        /// without being forged into the projectile.
        ///
        /// WHAT IS PRESERVED, AND WHY BY HAND. Launch overwrites the equipment fields, and those
        /// fields are what Projectile.DamageAmount and ArmorPenetration are computed from. Saving
        /// and restoring them is what makes "the projectile retains its original damage" literally
        /// true rather than approximately true -- the Grandmaster redirects the shot, they do not
        /// improve it. The explosive fuse is restored for the same reason: a returned grenade
        /// keeps the fuse it had, so catching one late is dangerous, exactly as it should be.
        /// </summary>
        private static bool Redirect(Projectile proj, Pawn guardian, Thing newLauncher,
                                     Thing targetThing, IntVec3 targetCell)
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
                proj.Launch(newLauncher, proj.ExactPosition, target, target, proj.HitFlags,
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

            Redirectors.Remove(proj);
            Redirectors.Add(proj, guardian);
            return true;
        }

        private static void Announce(Pawn guardian, string key)
        {
            if (guardian == null || guardian.Map == null) return;
            MoteMaker.ThrowText(guardian.DrawPos, guardian.Map, key.Translate(), 3.8f);
        }
    }
}
