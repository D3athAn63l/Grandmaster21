// Reference stubs for the RimWorld API the Melee Grandmaster package consumes.
//
// THESE ARE NOT RIMWORLD. Every signature here was copied from the real 1.6 assembly's metadata
// (see Tests/VerifyRuntimeTargets.cs, which checks the same members against the shipped game), but
// the BODIES are test scaffolding. Compiling against these proves the source is internally
// consistent and lets the offline harnesses exercise the mod's own decision logic; it proves
// nothing about RimWorld's behaviour. Only a real build and real gameplay does that.
//
// Anything named "...Stub" is a test hook with no counterpart in RimWorld.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Verse
{
    public struct IntVec3 : IEquatable<IntVec3>
    {
        public int x, y, z;
        public IntVec3(int x, int y, int z) { this.x = x; this.y = y; this.z = z; }
        public static IntVec3 Invalid { get { return new IntVec3(int.MinValue, 0, 0); } }
        public bool IsValid { get { return x != int.MinValue; } }
        public bool Equals(IntVec3 o) { return x == o.x && y == o.y && z == o.z; }
        public override bool Equals(object o) { return o is IntVec3 && Equals((IntVec3)o); }
        public override int GetHashCode() { return x * 73856093 ^ z * 19349663; }
        public static bool operator ==(IntVec3 a, IntVec3 b) { return a.Equals(b); }
        public static bool operator !=(IntVec3 a, IntVec3 b) { return !a.Equals(b); }
        public override string ToString() { return "(" + x + "," + z + ")"; }
        public Vector3 ToVector3() { return new Vector3(x, y, z); }
    }

    public static class IntVec3Utility
    {
        public static float DistanceTo(this IntVec3 a, IntVec3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
        public static IntVec3 ToIntVec3(this Vector3 v)
        {
            return new IntVec3(Mathf.FloorToInt(v.x), Mathf.FloorToInt(v.y), Mathf.FloorToInt(v.z));
        }
    }

    public static class GenGrid
    {
        public static bool InBounds(this IntVec3 c, Map map)
        {
            if (map == null) return false;
            return c.IsValid && c.x >= 0 && c.z >= 0 && c.x < map.Size.x && c.z < map.Size.z;
        }
        public static bool Walkable(this IntVec3 c, Map map)
        {
            return c.InBounds(map) && !map.blockedStub.Contains(c);
        }
    }

    public static class GenSight
    {
        /// <summary>
        /// Mirrors the real one closely enough for the safety veto: the cells a straight line
        /// crosses, endpoints included. RimWorld reuses a static buffer here; so does this, so a
        /// test that holds the result across calls fails the same way the game would.
        /// </summary>
        private static readonly List<IntVec3> tmpCells = new List<IntVec3>();
        public static List<IntVec3> BresenhamCellsBetween(IntVec3 a, IntVec3 b)
        {
            tmpCells.Clear();
            int steps = Mathf.Max(System.Math.Abs(b.x - a.x), System.Math.Abs(b.z - a.z));
            for (int i = 0; i <= steps; i++)
            {
                float t = steps == 0 ? 0f : (float)i / steps;
                tmpCells.Add(new IntVec3(
                    a.x + Mathf.RoundToInt((b.x - a.x) * t), a.y,
                    a.z + Mathf.RoundToInt((b.z - a.z) * t)));
            }
            return tmpCells;
        }

        /// <summary>Straight-line sampling, mirroring what the real Bresenham walk answers.</summary>
        public static bool LineOfSight(IntVec3 start, IntVec3 end, Map map)
        {
            return LineOfSight(start, end, map, false, null, 0, 0);
        }

        public static bool LineOfSight(IntVec3 start, IntVec3 end, Map map, bool skipFirstCell,
                                       Func<IntVec3, bool> validator, int halfXOffset, int halfZOffset)
        {
            if (map == null) return false;
            int steps = Mathf.Max(System.Math.Abs(end.x - start.x), System.Math.Abs(end.z - start.z));
            for (int i = skipFirstCell ? 1 : 0; i <= steps; i++)
            {
                float t = steps == 0 ? 0f : (float)i / steps;
                IntVec3 c = new IntVec3(
                    start.x + Mathf.RoundToInt((end.x - start.x) * t), start.y,
                    start.z + Mathf.RoundToInt((end.z - start.z) * t));
                if (map.blockedStub.Contains(c)) return false;
                if (validator != null && !validator(c)) return false;
            }
            return true;
        }
    }

    public class Pawn_EquipmentTracker
    {
        public Pawn pawn;
        public Thing bondedWeapon;
        public ThingWithComps Primary { get; set; }

        public Pawn_EquipmentTracker() { }
        public Pawn_EquipmentTracker(Pawn newPawn) { pawn = newPawn; }

        /// <summary>Test hook: records that a drop happened, so a harness can assert on it.</summary>
        public int dropsStub;

        public bool TryDropEquipment(ThingWithComps eq, out ThingWithComps resultingEq, IntVec3 pos, bool forbid = true)
        {
            resultingEq = null;
            if (eq == null || Primary != eq) return false;
            Primary = null;
            resultingEq = eq;
            dropsStub++;
            return true;
        }
    }

    public class Pawn_StanceTracker
    {
        public Pawn pawn;
        public Stance curStance = new Stance_Mobile();
        public RimWorld.StunHandler stunner = new RimWorld.StunHandler();

        public Pawn_StanceTracker() { }
        public Pawn_StanceTracker(Pawn newPawn) { pawn = newPawn; }

        public bool FullBodyBusy { get { return curStance is Stance_Busy; } }
        public void SetStance(Stance s) { curStance = s; }
    }

    public enum ProjectileHitFlags { None = 0, IntendedTarget = 1, NonTargetPawns = 2, NonTargetWorld = 4, All = 7 }

    public class ProjectileProperties
    {
        public float speed = 20f;
        public float explosionRadius;
        public bool flyOverhead;
        public float arcHeightFactor;   // > 0 means the projectile arcs over pawns in its way
        public DamageDef damageDef;
        public float SpeedTilesPerTick { get { return speed / 60f; } }
    }

    public class Projectile : ThingWithComps
    {
        protected Thing launcher;
        protected Thing equipment;
        protected ThingDef equipmentDef;
        protected RimWorld.QualityCategory equipmentQuality;
        protected Vector3 origin;
        protected Vector3 destination;
        protected int ticksToImpact;
        protected bool preventFriendlyFire;
        protected ThingDef targetCoverDef;

        public LocalTargetInfo intendedTarget;
        public LocalTargetInfo usedTarget;

        public Thing Launcher { get { return launcher; } }
        public ProjectileHitFlags HitFlags { get; set; }
        public Vector3 ExactPosition { get { return new Vector3(Position.x, 0f, Position.z); } }

        /// <summary>Test hook: how many times this projectile has been re-launched.</summary>
        public int launchCountStub;

        public virtual void Launch(Thing launcher, Vector3 origin, LocalTargetInfo usedTarget,
                                   LocalTargetInfo intendedTarget, ProjectileHitFlags hitFlags,
                                   bool preventFriendlyFire = false, Thing equipment = null,
                                   ThingDef targetCoverDef = null)
        {
            this.launcher = launcher;
            this.origin = origin;
            this.usedTarget = usedTarget;
            this.intendedTarget = intendedTarget;
            this.HitFlags = hitFlags;
            this.preventFriendlyFire = preventFriendlyFire;
            this.equipment = equipment;
            // Mirrors the real Launch, which overwrites these from the equipment argument -- the
            // exact behaviour the mod saves and restores around a redirect.
            this.equipmentDef = equipment == null ? null : equipment.def;
            this.targetCoverDef = targetCoverDef;

            IntVec3 cell = usedTarget.Thing != null ? usedTarget.Thing.Position : usedTarget.Cell;
            this.destination = new Vector3(cell.x, 0f, cell.z);
            this.ticksToImpact = Mathf.Max(1, Mathf.CeilToInt(
                (new Vector3(cell.x - Position.x, 0f, cell.z - Position.z)).magnitude
                / Mathf.Max(0.0001f, def.projectile.SpeedTilesPerTick)));
            launchCountStub++;
        }

        protected virtual void TickInterval(int delta) { }

        // Test accessors: the mod reaches these fields reflectively, exactly as it does in game.
        public Vector3 DestinationStub { get { return destination; } }
        public int TicksToImpactStub { get { return ticksToImpact; } set { ticksToImpact = value; } }
        public ThingDef EquipmentDefStub { get { return equipmentDef; } }
        public void SetLauncherStub(Thing t) { launcher = t; }
        public void SetEquipmentStub(Thing t, ThingDef d) { equipment = t; equipmentDef = d; }
        public void SetDestinationStub(Vector3 v) { destination = v; }
    }

    public class Projectile_Explosive : Projectile
    {
        private int ticksToDetonation;
        public int TicksToDetonationStub { get { return ticksToDetonation; } set { ticksToDetonation = value; } }
    }
}

namespace Verse
{
    public struct TargetInfo
    {
        public Thing Thing; public IntVec3 Cell; public Map Map;
        public TargetInfo(Thing t) { Thing = t; Cell = t == null ? IntVec3.Invalid : t.Position; Map = t == null ? null : t.Map; }
        public TargetInfo(IntVec3 c, Map map, bool allowNullMap) { Thing = null; Cell = c; Map = map; }
    }

    public class FleckDef : Def { }

    /// <summary>Test hook: every effect is recorded instead of drawn, so tests can assert on it.</summary>
    public static class FleckMaker
    {
        public static int connectingLinesStub, glowsStub, staticsStub;
        public static void ConnectingLine(Vector3 start, Vector3 end, FleckDef def, Map map, float width) { connectingLinesStub++; }
        public static void ThrowLightningGlow(Vector3 loc, Map map, float size) { glowsStub++; }
        public static void Static(Vector3 loc, Map map, FleckDef def, float scale) { staticsStub++; }
        public static void Static(IntVec3 cell, Map map, FleckDef def, float scale) { staticsStub++; }
        public static void ResetStub() { connectingLinesStub = 0; glowsStub = 0; staticsStub = 0; }
    }
}

namespace Verse.Sound
{
    using Verse;
    public enum MaintenanceType { None, PerTick, PerFrame }
    public struct SoundInfo
    {
        public TargetInfo Maker;
        public static SoundInfo InMap(TargetInfo maker, MaintenanceType maint = MaintenanceType.None)
        { SoundInfo i; i.Maker = maker; return i; }
    }
    public static class SoundStarter
    {
        public static int shotsStub;
        public static void PlayOneShot(this RimWorld.SoundDef def, SoundInfo info) { shotsStub++; }
    }
}

namespace RimWorld
{
    using Verse;

    public enum FactionRelationKind { Hostile, Neutral, Ally }

    public class SoundDef : Def { }
    public static class SoundDefOf { public static SoundDef MetalHitImportant = new SoundDef(); }

    public static class FleckDefOf
    {
        public static FleckDef LineEMP = new FleckDef();
        public static FleckDef MicroSparksFast = new FleckDef();
    }

    public class StatDef : Def { public float defaultBaseValue = 1f; }

    public static class StatDefOf
    {
        public static StatDef MoveSpeed = new StatDef { defName = "MoveSpeed", defaultBaseValue = 4.6f };
        public static StatDef Mass = new StatDef { defName = "Mass", defaultBaseValue = 1f };
        public static StatDef MeleeDamageFactor = new StatDef { defName = "MeleeDamageFactor" };
        public static StatDef MeleeDodgeChance = new StatDef { defName = "MeleeDodgeChance" };
        public static StatDef MeleeHitChance = new StatDef { defName = "MeleeHitChance" };
    }

    public static class StatExtension
    {
        /// <summary>Reads the test dictionary. Unset stats return the def's own baseline.</summary>
        public static float GetStatValue(this Thing thing, StatDef stat, bool applyPostProcess = true,
                                         int cacheStaleAfterTicks = -1)
        {
            if (thing == null || stat == null) return 0f;
            float v;
            return thing.statsStub.TryGetValue(stat, out v) ? v : stat.defaultBaseValue;
        }
    }

    public class StunHandler { public bool Stunned { get; set; } }

    public static class RestUtility { public static bool Awake(Pawn p) { return p != null && p.awakeStub; } }

    public static class GenHostility
    {
        /// <summary>
        /// Mirrors the real one: hostility is a FACTION RELATION, not faction identity. Two
        /// different factions are hostile only if their relation says so, which is what makes an
        /// allied faction non-hostile.
        /// </summary>
        public static bool HostileTo(this Thing a, Thing b)
        {
            if (a == null || b == null) return false;
            if (a.Faction == null || b.Faction == null) return false;
            if (a.Faction == b.Faction) return false;
            return a.Faction.RelationKindWith(b.Faction) == FactionRelationKind.Hostile;
        }
    }

    public static class MoteMaker
    {
        public static void ThrowText(Vector3 loc, Map map, string text, float timeBeforeStartFadeout) { }
        public static void ThrowText(Vector3 loc, Map map, TaggedString text, float timeBeforeStartFadeout) { }
    }

    public class Verb_MeleeAttack : Verb
    {
        protected virtual bool TryCastShot() { return true; }
        private float GetNonMissChance(LocalTargetInfo target) { return 1f; }
        private float GetDodgeChance(LocalTargetInfo target) { return 0f; }
    }
    public class Verb_MeleeAttackDamage : Verb_MeleeAttack { }

    public class Pawn_MeleeVerbs
    {
        private Pawn pawn;
        public Pawn_MeleeVerbs() { }
        public Pawn_MeleeVerbs(Pawn p) { pawn = p; }

        /// <summary>
        /// Test hooks: the verb handed out, a log of every attack requested, and a callback the
        /// harness uses to make an attack schedule the NEXT counterattack -- which is how the
        /// endless riposte chain is driven without RimWorld.
        /// </summary>
        public Verb verbStub;
        public readonly List<Thing> attacksStub = new List<Thing>();
        public Action<Pawn, Thing> onAttackStub;

        public Verb TryGetMeleeVerb(Thing target) { return verbStub; }

        public bool TryMeleeAttack(Thing target, Verb verbToUse = null, bool surpriseAttack = false)
        {
            attacksStub.Add(target);
            if (onAttackStub != null) onAttackStub(pawn, target);
            return true;
        }
    }
}
