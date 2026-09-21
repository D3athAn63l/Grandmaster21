// Offline logic harness for the Melee Grandmaster (Grandmaster 21 Beta).
//
// Drives the mod's REAL stat composites, defence maths, critical ladder, cleave scoring, disarm
// odds, interception curves, projectile-difficulty model, controlled-force clamp, safe-vector
// chooser and reaction scheduler -- including the real Harmony patch bodies where they can be
// invoked directly -- against a stub RimWorld API whose signatures mirror Assembly-CSharp.
//
// THIS IS NOT RIMWORLD. It does not exercise Harmony binding, real melee resolution, real
// projectile flight, save/load or the gizmo. Those are runtime concerns and are reported
// separately. What it does prove is that the mod's own decision logic behaves as designed.
using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;
using Grandmaster21;

static class MeleeHarness
{
    static int pass, fail;

    static void Check(string name, bool ok, string detail = "")
    {
        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name + (detail == "" ? "" : "   [" + detail + "]"));
        if (ok) pass++; else fail++;
    }

    static void Near(string name, float actual, float expected, float tolerance)
    {
        Check(name, Mathf.Abs(actual - expected) <= tolerance,
              "got " + actual.ToString("0.0000") + ", want " + expected.ToString("0.0000")
              + " +/- " + tolerance);
    }

    // ---- reflection into the mod's internal surface --------------------------------------
    static readonly Assembly Mod = typeof(Gm21).Assembly;
    static Type T(string n) { return Mod.GetType("Grandmaster21." + n); }

    const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance
                           | BindingFlags.Public | BindingFlags.NonPublic;

    static object Call(string type, string method, params object[] args)
    {
        MethodInfo m = T(type).GetMethod(method, Any);
        if (m == null) throw new MissingMethodException(type + "." + method);
        return m.Invoke(null, args);
    }

    static object CallExact(string type, string method, Type[] sig, params object[] args)
    {
        MethodInfo m = T(type).GetMethod(method, Any, null, sig, null);
        if (m == null) throw new MissingMethodException(type + "." + method);
        return m.Invoke(null, args);
    }

    static float F(object o) { return (float)o; }

    // ---- fixtures -------------------------------------------------------------------------

    static Map TheMap;
    static Faction Colony, Raiders;

    static Pawn MakePawn(string name, int meleeLevel, IntVec3 pos, Faction faction)
    {
        SkillRecord melee = new SkillRecord { def = SkillDefOf.Melee, levelInt = meleeLevel };
        Pawn p = new Pawn
        {
            def = new ThingDef { defName = name },
            skills = new Pawn_SkillTracker(),
            health = new Pawn_HealthTracker(),
            RaceProps = new RaceProperties { Humanlike = true },
            Position = pos,
            Map = TheMap,
            Spawned = true,
            Faction = faction,
            IsColonist = faction == Colony
        };
        p.skills.skills.Add(melee);
        melee.pawnStub = p;
        p.equipment = new Pawn_EquipmentTracker(p);
        p.stances = new Pawn_StanceTracker(p);
        p.meleeVerbs = new Pawn_MeleeVerbs(p);
        p.meleeVerbs.verbStub = new Verb { CasterPawn = p };
        TheMap.thingGrid.Register(p);
        return p;
    }

    static void SetCapacity(Pawn p, PawnCapacityDef cap, float value)
    {
        p.health.capacities.levelsStub[cap] = value;
    }

    static ThingWithComps Weapon(string name, float mass, bool melee = true)
    {
        ThingWithComps w = new ThingWithComps
        {
            def = new ThingDef { defName = name, IsWeapon = true, IsMeleeWeapon = melee, destroyable = true }
        };
        w.statsStub[StatDefOf.Mass] = mass;
        return w;
    }

    static void Equip(Pawn p, ThingWithComps w) { p.equipment.Primary = w; }

    static ThingDef ProjectileDef(string name, float speed, float explosionRadius = 0f)
    {
        return new ThingDef
        {
            defName = name,
            projectile = new ProjectileProperties { speed = speed, explosionRadius = explosionRadius }
        };
    }

    // =======================================================================================
    static void Main()
    {
        TheMap = new Map();
        Colony = new Faction { IsPlayer = true };
        Raiders = new Faction();
        // Hostility is a relation, as it is in game -- not "different faction".
        Colony.relationsStub[Raiders] = FactionRelationKind.Hostile;
        Raiders.relationsStub[Colony] = FactionRelationKind.Hostile;

        // The projectile package resolves its reflection handles in Apply(), which only the game
        // calls. Without this every handle is null and the correction-angle maths silently reads
        // a zero heading -- so the harness initialises them the same way the game does.
        T("Gm21ProjectileDefence").GetMethod("ResolveFields", Any).Invoke(null, null);

        // Every melee feature is gated on these; the game sets them at startup after patching.
        Gm21Melee.PassiveEnabled = true;
        Gm21Melee.ReactionsEnabled = true;
        Gm21Melee.DoctrineEnabled = true;
        Gm21Melee.AllyInterceptEnabled = true;
        Gm21Melee.ProjectileDefenceEnabled = true;
        Gm21.LearnPatchApplied = true;

        Basic();
        Capability();
        Composites();
        HitAndDefence();
        Riposte();
        Disarm();
        Criticals();
        Cleave();
        AllyIntercept();
        ProjectileDefence();
        SafeDeflection();
        Doctrines();
        GuardianThreatFiltering();
        GuardianProtection();
        GuardianIntent();
        GuardianSelection();
        FriendlyRecovery();
        GuardianSafePath();
        GuardianMicroDash();
        TranslationKeys();

        Console.WriteLine("\n================================");
        Console.WriteLine("PASS: " + pass + "   FAIL: " + fail);
        Environment.Exit(fail == 0 ? 0 : 1);
    }

    // ---- M1. BASIC ------------------------------------------------------------------------
    static void Basic()
    {
        Console.WriteLine("=== M1. Level gate: 20 gets nothing, 21 activates ===");

        Pawn twenty = MakePawn("L20", 20, new IntVec3(10, 0, 10), Colony);
        Pawn twentyOne = MakePawn("L21", 21, new IntVec3(12, 0, 10), Colony);

        Check("Melee 20 is not a Grandmaster", !Gm21Melee.IsMeleeGrandmaster(twenty));
        Check("Melee 20 has no passive package", !Gm21Melee.IsActiveGrandmaster(twenty));
        Check("Melee 21 is a Grandmaster", Gm21Melee.IsMeleeGrandmaster(twentyOne));
        Check("Melee 21 has the passive package", Gm21Melee.IsActiveGrandmaster(twentyOne));

        // Shooting 21 must not confer melee mastery, and vice versa.
        Pawn shooter = MakePawn("Shooter", 20, new IntVec3(14, 0, 10), Colony);
        shooter.skills.skills.Add(new SkillRecord { def = SkillDefOf.Shooting, levelInt = 21, pawnStub = shooter });
        Check("Shooting 21 does not grant melee mastery", !Gm21Melee.IsMeleeGrandmaster(shooter));

        Console.WriteLine("\n=== M2. Doctrine store ===");
        Check("default doctrine is Normal",
              Gm21MeleeDoctrineStore.Get(twentyOne) == Gm21MeleeDoctrine.Normal);
        Gm21MeleeDoctrineStore.Set(twentyOne, Gm21MeleeDoctrine.Killer);
        Check("Killer is stored", Gm21MeleeDoctrineStore.Get(twentyOne) == Gm21MeleeDoctrine.Killer);
        Gm21MeleeDoctrineStore.Set(twentyOne, Gm21MeleeDoctrine.Downed);
        Check("Downed replaces Killer", Gm21MeleeDoctrineStore.Get(twentyOne) == Gm21MeleeDoctrine.Downed);
        Gm21MeleeDoctrineStore.Set(twentyOne, Gm21MeleeDoctrine.Normal);
        Check("setting Normal clears the entry (nothing written to the save)",
              Gm21MeleeDoctrineStore.Get(twentyOne) == Gm21MeleeDoctrine.Normal);

        Gm21MeleeDoctrineStore.Set(twentyOne, Gm21MeleeDoctrine.Killer);
        Gm21MeleeDoctrineStore.Clear(twentyOne);
        Check("uninstall cleanup drops the doctrine",
              Gm21MeleeDoctrineStore.Get(twentyOne) == Gm21MeleeDoctrine.Normal);

        Check("melee doctrine and shooting aim mode are independent stores",
              Gm21AimModeStore.Get(twentyOne) == Gm21AimMode.Normal);
        Gm21AimModeStore.Set(twentyOne, Gm21AimMode.Killer);
        Gm21MeleeDoctrineStore.Set(twentyOne, Gm21MeleeDoctrine.Downed);
        Check("  ...setting one never moves the other",
              Gm21AimModeStore.Get(twentyOne) == Gm21AimMode.Killer
              && Gm21MeleeDoctrineStore.Get(twentyOne) == Gm21MeleeDoctrine.Downed);
        Gm21AimModeStore.Clear(twentyOne);
        Gm21MeleeDoctrineStore.Clear(twentyOne);
    }

    // ---- M3. PHYSICAL CAPABILITY ----------------------------------------------------------
    static void Capability()
    {
        Console.WriteLine("\n=== M3. Physical capability gates ===");

        Pawn gm = MakePawn("Cap", 21, new IntVec3(20, 0, 20), Colony);
        Check("healthy Grandmaster can act", Gm21Melee.CanAct(gm));

        gm.health.Downed = true;
        Check("downed Grandmaster cannot act", !Gm21Melee.CanAct(gm));
        gm.health.Downed = false;

        SetCapacity(gm, PawnCapacityDefOf.Consciousness, 0f);
        Check("unconscious Grandmaster cannot act", !Gm21Melee.CanAct(gm));
        SetCapacity(gm, PawnCapacityDefOf.Consciousness, 1f);

        gm.awakeStub = false;
        Check("sleeping Grandmaster cannot act", !Gm21Melee.CanAct(gm));
        gm.awakeStub = true;

        gm.stances.stunner.Stunned = true;
        Check("stunned Grandmaster cannot act", !Gm21Melee.CanAct(gm));
        gm.stances.stunner.Stunned = false;

        gm.Dead = true;
        Check("dead Grandmaster cannot act", !Gm21Melee.CanAct(gm));
        gm.Dead = false;

        gm.Spawned = false;
        Check("unspawned Grandmaster cannot act", !Gm21Melee.CanAct(gm));
        gm.Spawned = true;

        Check("...and is capable again once restored", Gm21Melee.CanAct(gm));

        // Zero manipulation is deliberately NOT a hard gate -- it degrades the composites.
        SetCapacity(gm, PawnCapacityDefOf.Manipulation, 0f);
        Check("no usable arms still allows acting (it degrades, not disables)", Gm21Melee.CanAct(gm));
        Check("  ...but precision collapses to zero", Gm21Melee.Precision(gm) == 0f);
        Check("  ...so projectile deflection is impossible",
              F(Call("Gm21ProjectileDefence", "DeflectionQuality", gm)) == 0f);
        SetCapacity(gm, PawnCapacityDefOf.Manipulation, 1f);
    }

    // ---- M4. STAT COMPOSITES --------------------------------------------------------------
    static void Composites()
    {
        Console.WriteLine("\n=== M4. Stat composites (healthy human == 1.0) ===");

        Pawn gm = MakePawn("Comp", 21, new IntVec3(30, 0, 30), Colony);
        Equip(gm, Weapon("Longsword", 2.2f));

        Near("healthy Precision is 1.0", Gm21Melee.Precision(gm), 1f, 0.001f);
        Near("healthy Awareness is 1.0", Gm21Melee.Awareness(gm), 1f, 0.001f);
        Near("healthy Reaction is 1.0", Gm21Melee.Reaction(gm), 1f, 0.001f);
        Near("healthy Defence is 1.0 (armed)", Gm21Melee.Defence(gm), 1f, 0.001f);
        Near("healthy Power is 1.0", Gm21Melee.Power(gm), 1f, 0.001f);
        Near("healthy MoveFactor is 1.0", Gm21Melee.MoveFactor(gm), 1f, 0.001f);

        float whole = Gm21Melee.Defence(gm);

        SetCapacity(gm, PawnCapacityDefOf.Sight, 0f);
        Check("blindness lowers Defence but does not remove it",
              Gm21Melee.Defence(gm) < whole && Gm21Melee.Defence(gm) > 0f,
              Gm21Melee.Defence(gm).ToString("0.00"));
        SetCapacity(gm, PawnCapacityDefOf.Sight, 1f);

        SetCapacity(gm, PawnCapacityDefOf.Manipulation, 0f);
        Check("no arms lowers Defence but does not remove it",
              Gm21Melee.Defence(gm) < whole && Gm21Melee.Defence(gm) > 0f,
              Gm21Melee.Defence(gm).ToString("0.00"));
        SetCapacity(gm, PawnCapacityDefOf.Manipulation, 1f);

        gm.statsStub[StatDefOf.MoveSpeed] = 0f;
        Check("immobility lowers Defence but does not remove it",
              Gm21Melee.Defence(gm) < whole && Gm21Melee.Defence(gm) > 0f,
              Gm21Melee.Defence(gm).ToString("0.00"));
        gm.statsStub.Remove(StatDefOf.MoveSpeed);

        // Superhuman pawns exceed 1.0 -- that is what makes the whole system scale.
        Pawn isekai = MakePawn("Isekai", 21, new IntVec3(34, 0, 30), Colony);
        SetCapacity(isekai, PawnCapacityDefOf.Manipulation, 2f);
        SetCapacity(isekai, PawnCapacityDefOf.Sight, 2f);
        isekai.statsStub[StatDefOf.MeleeDamageFactor] = 4f;
        isekai.bodySizeStub = 2.25f;
        Check("superhuman Precision exceeds 1", Gm21Melee.Precision(isekai) > 2f,
              Gm21Melee.Precision(isekai).ToString("0.00"));
        Check("superhuman Power exceeds 1", Gm21Melee.Power(isekai) > 5f,
              Gm21Melee.Power(isekai).ToString("0.00"));

        Console.WriteLine("\n=== M5. Weapon readiness and deflection implement ===");
        Pawn bare = MakePawn("Bare", 21, new IntVec3(38, 0, 30), Colony);
        Check("bare hands deflect far worse than a sword",
              F(Call("Gm21ProjectileDefence", "DeflectionQuality", bare))
              < F(Call("Gm21ProjectileDefence", "DeflectionQuality", gm)) * 0.5f);

        Pawn heavy = MakePawn("Heavy", 21, new IntVec3(42, 0, 30), Colony);
        Equip(heavy, Weapon("Warhammer", 6f));
        Check("a heavier weapon is a better interception surface",
              Gm21Melee.DeflectionImplement(heavy) > Gm21Melee.DeflectionImplement(gm));
    }

    // ---- M6. HIT / DEFENCE ----------------------------------------------------------------
    static void HitAndDefence()
    {
        Console.WriteLine("\n=== M6. The 99% rule ===");

        Near("60% base -> 99.6%", Gm21Melee.Compensate(0.60f, 1f), 0.996f, 0.0005f);
        Near("20% base -> 99.2%", Gm21Melee.Compensate(0.20f, 1f), 0.992f, 0.0005f);
        Check("weapon differentiation survives compensation",
              Gm21Melee.Compensate(0.90f, 1f) > Gm21Melee.Compensate(0.30f, 1f));
        Check("a certainty stays a certainty", Gm21Melee.Compensate(1f, 1f) == 1f);

        Check("a better composite retains less failure",
              Gm21Melee.RetainedFor(2f) < Gm21Melee.RetainedFor(1f));
        Check("an impaired composite retains more failure",
              Gm21Melee.RetainedFor(0.5f) > Gm21Melee.RetainedFor(1f));
        Check("retained failure never reaches zero", Gm21Melee.RetainedFor(10000f) > 0f);
        Check("a zero composite retains everything", Gm21Melee.RetainedFor(0f) == 1f);

        Console.WriteLine("\n=== M7. Real patch bodies: hit chance and defence ===");

        Pawn attacker = MakePawn("Atk", 21, new IntVec3(50, 0, 50), Colony);
        Pawn defender = MakePawn("Def", 20, new IntVec3(51, 0, 50), Raiders);

        // GM attacker vs ordinary defender.
        OpenFrame(attacker, defender);
        float nonMiss = 0.60f;
        InvokePostfix("Postfix_NonMissChance", ref nonMiss);
        Near("GM attacker: 60% hit chance -> 99.6%", nonMiss, 0.996f, 0.0005f);

        float dodge = 0.50f;
        InvokeDodgePostfix(ref dodge, defender);
        Check("GM attacker ignores ~99% of the defender's dodge", dodge <= 0.006f,
              dodge.ToString("0.0000"));
        CloseFrame();

        // Ordinary attacker vs GM defender.
        Pawn plain = MakePawn("Plain", 20, new IntVec3(55, 0, 50), Raiders);
        Pawn guard = MakePawn("Guard", 21, new IntVec3(56, 0, 50), Colony);
        Equip(guard, Weapon("Longsword", 2.2f));

        OpenFrame(plain, guard);
        float hit = 0.80f;
        InvokePostfix("Postfix_NonMissChance", ref hit);
        Near("ordinary attacker's hit chance is untouched", hit, 0.80f, 0.0001f);

        float parry = 0.20f;
        InvokeDodgePostfix(ref parry, guard);
        Check("GM defender parries almost everything", parry >= 0.99f, parry.ToString("0.0000"));
        CloseFrame();

        // Grandmaster versus Grandmaster: the defender's answer comes second and wins.
        Pawn gmA = MakePawn("GM-A", 21, new IntVec3(60, 0, 50), Colony);
        Pawn gmB = MakePawn("GM-B", 21, new IntVec3(61, 0, 50), Raiders);
        Equip(gmA, Weapon("Longsword", 2.2f));
        Equip(gmB, Weapon("Longsword", 2.2f));

        OpenFrame(gmA, gmB);
        float duel = 0.20f;
        InvokeDodgePostfix(ref duel, gmB);
        Check("GM vs GM: the defender still parries (stalemate is the intended outcome)",
              duel >= 0.98f, duel.ToString("0.0000"));
        CloseFrame();

        // An impaired Grandmaster defends measurably worse than a whole one.
        Pawn hurt = MakePawn("Hurt", 21, new IntVec3(65, 0, 50), Colony);
        SetCapacity(hurt, PawnCapacityDefOf.Sight, 0.2f);
        SetCapacity(hurt, PawnCapacityDefOf.Manipulation, 0.3f);

        OpenFrame(plain, guard);
        float whole = 0.20f;
        InvokeDodgePostfix(ref whole, guard);
        CloseFrame();
        OpenFrame(plain, hurt);
        float broken = 0.20f;
        InvokeDodgePostfix(ref broken, hurt);
        CloseFrame();
        Check("an injured Grandmaster parries measurably worse", broken < whole,
              broken.ToString("0.0000") + " < " + whole.ToString("0.0000"));

        // No frame open at all: an ordinary fight between ordinary pawns is untouched.
        float untouched = 0.33f;
        InvokePostfix("Postfix_NonMissChance", ref untouched);
        Near("no Grandmaster involved: nothing changes", untouched, 0.33f, 0.0001f);
    }

    // ---- frame plumbing --------------------------------------------------------------------
    static object OpenFrame(Pawn attacker, Thing target)
    {
        object frame = Call("Gm21MeleeContext", "Open", attacker, target);
        Type tf = T("Gm21MeleeFrame");
        if (Gm21Melee.IsActiveGrandmaster(attacker))
        {
            tf.GetField("attackerIsGm").SetValue(frame, true);
            tf.GetField("atkAwareness").SetValue(frame, Gm21Melee.Awareness(attacker));
            tf.GetField("atkPrecision").SetValue(frame, Gm21Melee.Precision(attacker));
            tf.GetField("atkPower").SetValue(frame, Gm21Melee.Power(attacker));
            tf.GetField("doctrine").SetValue(frame, Gm21MeleeDoctrineStore.Get(attacker));
        }
        Pawn def = target as Pawn;
        if (def != null && Gm21Melee.IsActiveGrandmaster(def))
        {
            tf.GetField("defenderIsGm").SetValue(frame, true);
            tf.GetField("defDefence").SetValue(frame, Gm21Melee.Defence(def));
        }
        return frame;
    }

    static void CloseFrame() { Call("Gm21MeleeContext", "Close"); }

    static void InvokePostfix(string name, ref float value)
    {
        object[] args = { value };
        T("Gm21MeleePatches").GetMethod(name, Any).Invoke(null, args);
        value = (float)args[0];
    }

    static void InvokeDodgePostfix(ref float value, Thing target)
    {
        object[] args = { value, new LocalTargetInfo(target) };
        T("Gm21MeleePatches").GetMethod("Postfix_DodgeChance", Any).Invoke(null, args);
        value = (float)args[0];
    }

    // ---- M8. RIPOSTE SCHEDULING -----------------------------------------------------------
    static void Riposte()
    {
        Console.WriteLine("\n=== M8. Riposte scheduling (no call-stack recursion) ===");

        Gm21CombatScheduler scheduler = new Gm21CombatScheduler(null);
        Find.TickManager.TicksGame = 1000;

        Pawn gm = MakePawn("Riposter", 21, new IntVec3(70, 0, 70), Colony);
        Pawn foe = MakePawn("Foe", 20, new IntVec3(71, 0, 70), Raiders);
        Equip(gm, Weapon("Longsword", 2.2f));

        Call("Gm21CombatScheduler", "ScheduleRiposte", gm, foe);
        Check("a parry queues exactly one reaction", QueueCount() == 1);

        scheduler.GameComponentTick();
        Check("the reaction does NOT resolve on the tick it was queued",
              gm.meleeVerbs.attacksStub.Count == 0 && QueueCount() == 1);

        Find.TickManager.TicksGame = 1001;
        scheduler.GameComponentTick();
        Check("it resolves on the next tick", gm.meleeVerbs.attacksStub.Count == 1
              && gm.meleeVerbs.attacksStub[0] == foe);
        Check("and the queue is empty afterwards", QueueCount() == 0);

        Console.WriteLine("\n=== M9. Endless counter-chain stays flat ===");

        // The attack callback schedules the counter-counterattack, exactly as a parry does in
        // game. This is the real chain: A ripostes, B answers, A answers, forever.
        Pawn a = MakePawn("Duel-A", 21, new IntVec3(80, 0, 80), Colony);
        Pawn b = MakePawn("Duel-B", 21, new IntVec3(81, 0, 80), Raiders);
        Equip(a, Weapon("Longsword", 2.2f));
        Equip(b, Weapon("Longsword", 2.2f));

        int exchanges = 0;
        var depths = new List<int>();
        Action<Pawn, Thing> counter = (actor, target) =>
        {
            exchanges++;
            depths.Add(new System.Diagnostics.StackTrace(false).FrameCount);
            if (exchanges < 300)
            {
                Call("Gm21CombatScheduler", "ScheduleRiposte", target as Pawn, actor);
            }
        };
        a.meleeVerbs.onAttackStub = counter;
        b.meleeVerbs.onAttackStub = counter;

        Call("Gm21CombatScheduler", "ScheduleRiposte", a, b);

        int tick = Find.TickManager.TicksGame;
        int maxQueue = 0;
        for (int i = 0; i < 1000 && exchanges < 300; i++)
        {
            tick++;
            Find.TickManager.TicksGame = tick;
            scheduler.GameComponentTick();
            if (QueueCount() > maxQueue) maxQueue = QueueCount();
        }

        Check("300 counter-exchanges resolved", exchanges >= 300, "exchanges=" + exchanges);
        Check("each exchange took exactly one tick (strictly generational)",
              QueueCount() <= 1 && maxQueue <= 1, "maxQueue=" + maxQueue);

        int minDepth = int.MaxValue, maxDepth = 0;
        for (int i = 0; i < depths.Count; i++)
        {
            if (depths[i] < minDepth) minDepth = depths[i];
            if (depths[i] > maxDepth) maxDepth = depths[i];
        }
        Check("call-stack depth is CONSTANT across 300 exchanges (no C# recursion)",
              maxDepth - minDepth == 0, "depth " + minDepth + ".." + maxDepth);

        a.meleeVerbs.onAttackStub = null;
        b.meleeVerbs.onAttackStub = null;
        Call("Gm21CombatScheduler", "Reset");

        Console.WriteLine("\n=== M10. Reaction validity is re-checked at fire time ===");
        Pawn dying = MakePawn("Dying", 21, new IntVec3(90, 0, 90), Colony);
        Pawn victim = MakePawn("Victim", 20, new IntVec3(91, 0, 90), Raiders);
        Call("Gm21CombatScheduler", "ScheduleRiposte", dying, victim);
        dying.health.Downed = true;                 // downed in the intervening tick
        Find.TickManager.TicksGame = tick + 2;
        scheduler.GameComponentTick();
        Check("a Grandmaster downed before their riposte fires does not attack",
              dying.meleeVerbs.attacksStub.Count == 0);

        Call("Gm21CombatScheduler", "Reset");
        Pawn ok = MakePawn("Ok", 21, new IntVec3(95, 0, 95), Colony);
        Pawn gone = MakePawn("Gone", 20, new IntVec3(96, 0, 95), Raiders);
        Call("Gm21CombatScheduler", "ScheduleRiposte", ok, gone);
        gone.Destroyed = true;
        Find.TickManager.TicksGame = tick + 4;
        scheduler.GameComponentTick();
        Check("a destroyed target is not attacked", ok.meleeVerbs.attacksStub.Count == 0);

        Console.WriteLine("\n=== M11. Cooldown bypass ===");
        Call("Gm21CombatScheduler", "Reset");
        Pawn recovering = MakePawn("Recovering", 21, new IntVec3(100, 0, 100), Colony);
        Pawn opponent = MakePawn("Opponent", 20, new IntVec3(101, 0, 100), Raiders);
        recovering.stances.SetStance(new Stance_Cooldown(60, new LocalTargetInfo(opponent), null));
        Call("Gm21CombatScheduler", "ScheduleRiposte", recovering, opponent);
        Find.TickManager.TicksGame = tick + 6;
        scheduler.GameComponentTick();
        Check("a riposte fires through weapon cooldown",
              recovering.meleeVerbs.attacksStub.Count == 1);
        Check("  ...and the cooldown stance was replaced with a mobile one, not nulled",
              recovering.stances.curStance is Stance_Mobile);

        Call("Gm21CombatScheduler", "Reset");
        Pawn winding = MakePawn("Winding", 21, new IntVec3(105, 0, 105), Colony);
        Pawn other = MakePawn("Other", 20, new IntVec3(106, 0, 105), Raiders);
        winding.stances.SetStance(new Stance_Warmup(60, new LocalTargetInfo(other), null));
        Call("Gm21CombatScheduler", "ScheduleRiposte", winding, other);
        Find.TickManager.TicksGame = tick + 8;
        scheduler.GameComponentTick();
        Check("a deliberate wind-up is NOT cancelled by a reaction",
              winding.meleeVerbs.attacksStub.Count == 0
              && winding.stances.curStance is Stance_Warmup);

        Call("Gm21CombatScheduler", "Reset");
    }

    static int QueueCount()
    {
        return (int)T("Gm21CombatScheduler")
            .GetProperty("QueuedCount", Any).GetValue(null, null);
    }

    // ---- M12. DISARM ----------------------------------------------------------------------
    static void Disarm()
    {
        Console.WriteLine("\n=== M12. Disarm ===");

        Pawn gm = MakePawn("Disarmer", 21, new IntVec3(110, 0, 110), Colony);
        Rand.ForcedValueStub = 0f;   // every chance roll succeeds; we are testing the GATES

        Pawn armed = MakePawn("Armed", 20, new IntVec3(111, 0, 110), Raiders);
        Equip(armed, Weapon("Knife", 0.4f));
        bool ok = (bool)Call("Gm21Disarm", "TryDisarm", gm, armed, 1f, false);
        Check("an armed target is disarmed", ok && armed.equipment.Primary == null);
        Check("  ...and the weapon was DROPPED, not destroyed", armed.equipment.dropsStub == 1);

        Pawn unarmed = MakePawn("Unarmed", 20, new IntVec3(112, 0, 110), Raiders);
        Check("an unarmed target cannot be disarmed",
              !(bool)Call("Gm21Disarm", "TryDisarm", gm, unarmed, 1f, false));

        Pawn mech = MakePawn("Mech", 20, new IntVec3(113, 0, 110), Raiders);
        ThingWithComps integrated = Weapon("IntegratedGun", 5f, false);
        integrated.def.destroyOnDrop = true;
        Equip(mech, integrated);
        Check("an integrated weapon is never disarmed",
              !(bool)Call("Gm21Disarm", "TryDisarm", gm, mech, 1f, false)
              && mech.equipment.Primary == integrated);

        Pawn indestructible = MakePawn("Indestructible", 20, new IntVec3(114, 0, 110), Raiders);
        ThingWithComps undroppable = Weapon("Undroppable", 2f);
        undroppable.def.destroyable = false;
        Equip(indestructible, undroppable);
        Check("a weapon that cannot legally be dropped is never disarmed",
              !(bool)Call("Gm21Disarm", "TryDisarm", gm, indestructible, 1f, false));

        Check("a Grandmaster with no precision cannot disarm",
              !(bool)Call("Gm21Disarm", "TryDisarm", gm, armed, 0f, false));

        Rand.ForcedValueStub = null;

        // Odds, measured by sampling the real code path.
        Console.WriteLine("\n=== M13. Disarm odds respond to the right stats ===");
        Rand.SeedStub(7);
        float light = DisarmRate(gm, 0.4f, 1f, 1f, false);
        float heavy = DisarmRate(gm, 10f, 1f, 1f, false);
        Check("a heavy weapon is harder to knock away", heavy < light,
              heavy.ToString("0.00") + " < " + light.ToString("0.00"));

        float strongGrip = DisarmRate(gm, 2f, 1f, 1f, false);
        float weakGrip = DisarmRate(gm, 2f, 0.3f, 1f, false);
        Check("a target with ruined hands is easier to disarm", weakGrip > strongGrip,
              weakGrip.ToString("0.00") + " > " + strongGrip.ToString("0.00"));

        float ordinary = DisarmRate(gm, 2f, 1f, 1f, false);
        float precise = DisarmRate(gm, 2f, 1f, 2f, false);
        Check("a more precise Grandmaster disarms more often", precise > ordinary,
              precise.ToString("0.00") + " > " + ordinary.ToString("0.00"));

        float critical = DisarmRate(gm, 2f, 1f, 1f, true);
        Check("a critical strike disarms far more often", critical > ordinary * 1.5f,
              critical.ToString("0.00") + " vs " + ordinary.ToString("0.00"));

        float bonded = DisarmRateBonded(gm, 2f, 1f, 1f);
        Check("a psychically bonded weapon strongly resists", bonded < ordinary * 0.5f,
              bonded.ToString("0.00") + " vs " + ordinary.ToString("0.00"));
    }

    static float DisarmRate(Pawn gm, float weaponMass, float targetManip, float precision, bool crit)
    {
        return DisarmRateInner(gm, weaponMass, targetManip, precision, crit, false);
    }

    static float DisarmRateBonded(Pawn gm, float weaponMass, float targetManip, float precision)
    {
        return DisarmRateInner(gm, weaponMass, targetManip, precision, false, true);
    }

    static float DisarmRateInner(Pawn gm, float weaponMass, float targetManip, float precision,
                                 bool crit, bool bonded)
    {
        const int trials = 4000;
        int hits = 0;
        for (int i = 0; i < trials; i++)
        {
            Pawn t = new Pawn
            {
                def = new ThingDef { defName = "T" }, health = new Pawn_HealthTracker(),
                skills = new Pawn_SkillTracker(), Position = new IntVec3(200, 0, 200),
                Map = TheMap, Spawned = true, Faction = Raiders
            };
            t.equipment = new Pawn_EquipmentTracker(t);
            SetCapacity(t, PawnCapacityDefOf.Manipulation, targetManip);
            ThingWithComps w = Weapon("W", weaponMass);
            Equip(t, w);
            if (bonded) t.equipment.bondedWeapon = w;
            if ((bool)Call("Gm21Disarm", "TryDisarm", gm, t, precision, crit)) hits++;
        }
        return (float)hits / trials;
    }

    // ---- M14. CRITICALS --------------------------------------------------------------------
    static void Criticals()
    {
        Console.WriteLine("\n=== M14. Critical strikes ===");
        Rand.SeedStub(11);

        int low = int.MaxValue, high = 0;
        var counts = new Dictionary<int, int>();
        for (int i = 0; i < 40000; i++)
        {
            int m = (int)F(Call("Gm21Critical", "RollMultiplier", 1f));
            if (m < low) low = m;
            if (m > high) high = m;
            counts[m] = counts.ContainsKey(m) ? counts[m] + 1 : 1;
        }
        Check("multiplier never falls below 2x", low == 2, "min=" + low);
        Check("multiplier never exceeds 10x", high == 10, "max=" + high);
        Check("every tier 2..10 occurs", counts.Count == 9, "tiers=" + counts.Count);
        Check("2x is the most common tier for ordinary power",
              counts[2] > counts[3] && counts[3] > counts[4],
              "2x=" + counts[2] + " 3x=" + counts[3] + " 4x=" + counts[4]);
        Check("10x is the rarest for ordinary power", counts[10] < counts[9],
              "9x=" + counts[9] + " 10x=" + counts[10]);

        // Physical power bends the ladder upward. This is the Isekai clause.
        var strong = new Dictionary<int, int>();
        for (int i = 0; i < 40000; i++)
        {
            int m = (int)F(Call("Gm21Critical", "RollMultiplier", 3f));
            strong[m] = strong.ContainsKey(m) ? strong[m] + 1 : 1;
        }
        Check("an extremely strong Grandmaster's criticals favour the top of the ladder",
              strong[10] > strong[2], "10x=" + strong[10] + " vs 2x=" + strong[2]);
        Check("  ...and 2x..10x is still the whole range",
              !strong.ContainsKey(1) && !strong.ContainsKey(11));

        var weak = new Dictionary<int, int>();
        for (int i = 0; i < 40000; i++)
        {
            int m = (int)F(Call("Gm21Critical", "RollMultiplier", 0.2f));
            weak[m] = weak.ContainsKey(m) ? weak[m] + 1 : 1;
        }
        Check("a weak Grandmaster is pushed toward the bottom",
              weak[2] > counts[2], "weak 2x=" + weak[2] + " vs ordinary " + counts[2]);

        Console.WriteLine("\n=== M15. Critical chance ===");
        Near("healthy Grandmaster crits 20% of landed strikes",
             F(Call("Gm21Critical", "Chance", 1f, 1f)), 0.20f, 0.0001f);
        Check("precision and awareness both raise it",
              F(Call("Gm21Critical", "Chance", 2f, 1f)) > 0.20f
              && F(Call("Gm21Critical", "Chance", 1f, 2f)) > 0.20f);
        Check("it is capped well below certainty",
              F(Call("Gm21Critical", "Chance", 50f, 50f)) <= 0.75f + 0.0001f,
              F(Call("Gm21Critical", "Chance", 50f, 50f)).ToString("0.00"));
        Check("a blind, unaware pawn never crits",
              F(Call("Gm21Critical", "Chance", 0f, 0f)) == 0f);
    }

    // ---- M16. CLEAVE -----------------------------------------------------------------------
    static void Cleave()
    {
        Console.WriteLine("\n=== M16. Cleave scales with power and weapon ===");

        float dagger = F(Call("Gm21Cleave", "Chance", 1f, 0.4f));
        float longsword = F(Call("Gm21Cleave", "Chance", 1f, 2.2f));
        float warhammer = F(Call("Gm21Cleave", "Chance", 1f, 6f));
        float huge = F(Call("Gm21Cleave", "Chance", 1f, 20f));

        Check("dagger < longsword < warhammer < huge two-hander",
              dagger < longsword && longsword < warhammer && warhammer < huge,
              dagger.ToString("0.00") + " " + longsword.ToString("0.00") + " "
              + warhammer.ToString("0.00") + " " + huge.ToString("0.00"));
        Check("a dagger rarely cleaves", dagger < 0.15f, dagger.ToString("0.00"));
        Check("a huge two-hander usually does", huge >= 0.85f, huge.ToString("0.00"));
        Check("cleave chance is capped below certainty",
              F(Call("Gm21Cleave", "Chance", 50f, 50f)) <= 0.90f + 0.0001f);

        Check("physical power raises cleave chance",
              F(Call("Gm21Cleave", "Chance", 3f, 2.2f)) > longsword);
        Check("a powerless pawn never cleaves", F(Call("Gm21Cleave", "Chance", 0f, 6f)) == 0f);

        Console.WriteLine("\n=== M17. Cleave target count ===");
        Check("an ordinary pawn with a dagger reaches one extra enemy",
              (int)Call("Gm21Cleave", "MaxTargets", 1f, 0.4f) == 1);
        Check("a warhammer reaches more",
              (int)Call("Gm21Cleave", "MaxTargets", 1f, 6f) >= 2);
        Check("the count is capped at four",
              (int)Call("Gm21Cleave", "MaxTargets", 50f, 50f) == 4);
        Check("bare hands still reach at least one",
              (int)Call("Gm21Cleave", "MaxTargets", 1f, 0f) == 1);

        Console.WriteLine("\n=== M18. Cleave never strikes allies ===");
        Gm21CombatScheduler scheduler = new Gm21CombatScheduler(null);
        Find.TickManager.TicksGame = 5000;

        Map crowd = new Map();
        Map saved = TheMap;
        TheMap = crowd;
        Pawn gm = MakePawn("Cleaver", 21, new IntVec3(20, 0, 20), Colony);
        Equip(gm, Weapon("Greatsword", 20f));
        Pawn primary = MakePawn("Primary", 20, new IntVec3(21, 0, 20), Raiders);
        Pawn enemy2 = MakePawn("Enemy2", 20, new IntVec3(19, 0, 20), Raiders);
        Pawn enemy3 = MakePawn("Enemy3", 20, new IntVec3(20, 0, 21), Raiders);
        Pawn friend = MakePawn("Friend", 20, new IntVec3(20, 0, 19), Colony);
        Verb verb = new Verb { CasterPawn = gm };

        Rand.ForcedValueStub = 0f;   // cleave always triggers
        Call("Gm21Cleave", "TryCleave", gm, primary, verb, 5f);
        Rand.ForcedValueStub = null;

        Find.TickManager.TicksGame = 5001;
        scheduler.GameComponentTick();

        List<Thing> struck = gm.meleeVerbs.attacksStub;
        Check("cleave reached the other enemies", struck.Contains(enemy2) && struck.Contains(enemy3));
        Check("cleave never struck the ally", !struck.Contains(friend));
        Check("cleave never re-struck the primary target", !struck.Contains(primary));

        // A follow-through does not follow through again. Executing a scheduled cleave must mark
        // the frame it opens, so the on-hit package skips TryCleave for it.
        Call("Gm21CombatScheduler", "Reset");
        Call("Gm21MeleeAction", "Execute", gm, enemy2, Enum.Parse(T("Gm21ActionKind"), "Cleave"));
        Check("a scheduled cleave leaves no follow-through flag latched",
              !(bool)T("Gm21MeleeContext").GetField("NextIsFollowThrough", Any).GetValue(null));

        object cleaveFrame = OpenFrame(gm, enemy2);
        T("Gm21MeleeFrame").GetField("isFollowThrough").SetValue(cleaveFrame, true);
        int before = QueueCount();
        Rand.ForcedValueStub = 0f;
        Call("Gm21OnHit", "Resolve", cleaveFrame, verb);
        Rand.ForcedValueStub = null;
        CloseFrame();
        Check("a cleave never cleaves again", QueueCount() == before, "queued=" + QueueCount());

        object riposteFrame = OpenFrame(gm, enemy2);
        before = QueueCount();
        Rand.ForcedValueStub = 0f;
        Call("Gm21OnHit", "Resolve", riposteFrame, verb);
        Rand.ForcedValueStub = null;
        CloseFrame();
        Check("a riposte is a real swing and still cleaves", QueueCount() > before,
              "queued=" + QueueCount());

        Call("Gm21CombatScheduler", "Reset");
        TheMap = saved;
    }

    // ---- M19. ALLY INTERCEPTION -------------------------------------------------------------
    static void AllyIntercept()
    {
        Console.WriteLine("\n=== M19. The 8 m/s benchmark ===");

        Near("8 m/s at 1 tile -> ~90%", F(Call("Gm21AllyIntercept", "Chance", 8f, 1f, 1f)),
             0.90f, 0.005f);
        Near("8 m/s at 2 tiles", F(Call("Gm21AllyIntercept", "Chance", 8f, 1f, 2f)), 0.855f, 0.005f);
        Near("8 m/s at 3 tiles", F(Call("Gm21AllyIntercept", "Chance", 8f, 1f, 3f)), 0.81f, 0.005f);

        Check("below 4 m/s is low", F(Call("Gm21AllyIntercept", "Chance", 2f, 1f, 1f)) < 0.2f);
        Check("4-6 m/s is moderate",
              F(Call("Gm21AllyIntercept", "Chance", 5f, 1f, 1f)) > 0.45f
              && F(Call("Gm21AllyIntercept", "Chance", 5f, 1f, 1f)) < 0.72f);
        Check("above 8 m/s keeps climbing toward the ceiling",
              F(Call("Gm21AllyIntercept", "Chance", 10f, 1f, 1f))
                  > F(Call("Gm21AllyIntercept", "Chance", 8f, 1f, 1f))
              && F(Call("Gm21AllyIntercept", "Chance", 14f, 1f, 1f)) >= 0.99f,
              F(Call("Gm21AllyIntercept", "Chance", 10f, 1f, 1f)).ToString("0.000") + " then "
              + F(Call("Gm21AllyIntercept", "Chance", 14f, 1f, 1f)).ToString("0.000"));
        Check("it never reaches certainty -- something can always go wrong",
              F(Call("Gm21AllyIntercept", "Chance", 1000f, 1f, 1f)) <= 0.99f);
        Check("an immobile pawn never intercepts",
              F(Call("Gm21AllyIntercept", "Chance", 0f, 1f, 1f)) == 0f);
        Check("an unaware pawn never intercepts",
              F(Call("Gm21AllyIntercept", "Chance", 8f, 0f, 1f)) == 0f);
        Check("poor reaction lowers a fast pawn's interception",
              F(Call("Gm21AllyIntercept", "Chance", 8f, 0.5f, 1f)) < 0.6f);

        Console.WriteLine("\n=== M20. Interception range and obstruction ===");
        Map arena = new Map();
        Map saved = TheMap;
        TheMap = arena;
        Find.TickManager.TicksGame = 9000;
        Gm21CombatScheduler scheduler = new Gm21CombatScheduler(null);

        Pawn researcher = MakePawn("Researcher", 0, new IntVec3(50, 0, 50), Colony);
        Pawn raider = MakePawn("Raider", 10, new IntVec3(51, 0, 50), Raiders);

        Rand.ForcedValueStub = 0f;   // every roll succeeds; we are testing REACH, not odds

        MakePawn("Far", 21, new IntVec3(56, 0, 50), Colony);   // 6 tiles away
        Check("a Grandmaster beyond 3 tiles cannot intercept",
              !(bool)Call("Gm21AllyIntercept", "TryIntercept", raider, researcher));

        for (int d = 1; d <= 3; d++)
        {
            Map ring = new Map();
            TheMap = ring;
            Pawn r = MakePawn("R" + d, 0, new IntVec3(50, 0, 50), Colony);
            Pawn attacker = MakePawn("A" + d, 10, new IntVec3(51, 0, 50), Raiders);
            MakePawn("G" + d, 21, new IntVec3(50 - d, 0, 50), Colony);
            Check("interception works at " + d + " tile" + (d == 1 ? "" : "s"),
                  (bool)Call("Gm21AllyIntercept", "TryIntercept", attacker, r));
        }

        // A wall between them: no phasing.
        Map walled = new Map();
        TheMap = walled;
        Pawn r2 = MakePawn("R-wall", 0, new IntVec3(50, 0, 50), Colony);
        Pawn a2 = MakePawn("A-wall", 10, new IntVec3(51, 0, 50), Raiders);
        MakePawn("G-wall", 21, new IntVec3(47, 0, 50), Colony);
        walled.blockedStub.Add(new IntVec3(48, 0, 50));
        walled.blockedStub.Add(new IntVec3(49, 0, 50));
        Check("a wall blocks interception (no phasing through solid terrain)",
              !(bool)Call("Gm21AllyIntercept", "TryIntercept", a2, r2));

        // An immobile Grandmaster cannot cross the gap.
        Map stuck = new Map();
        TheMap = stuck;
        Pawn r3 = MakePawn("R-stuck", 0, new IntVec3(50, 0, 50), Colony);
        Pawn a3 = MakePawn("A-stuck", 10, new IntVec3(51, 0, 50), Raiders);
        Pawn g3 = MakePawn("G-stuck", 21, new IntVec3(48, 0, 50), Colony);
        SetCapacity(g3, PawnCapacityDefOf.Moving, 0f);
        Check("a Grandmaster who cannot move never intercepts",
              !(bool)Call("Gm21AllyIntercept", "TryIntercept", a3, r3));

        // A hostile Grandmaster does not shield your colonist.
        Map wrongSide = new Map();
        TheMap = wrongSide;
        Pawn r4 = MakePawn("R-side", 0, new IntVec3(50, 0, 50), Colony);
        Pawn a4 = MakePawn("A-side", 10, new IntVec3(51, 0, 50), Raiders);
        MakePawn("G-enemy", 21, new IntVec3(48, 0, 50), Raiders);
        Check("an enemy Grandmaster does not protect your pawn",
              !(bool)Call("Gm21AllyIntercept", "TryIntercept", a4, r4));

        // Non-hostile melee (social fight, training) is never intercepted.
        Map friendly = new Map();
        TheMap = friendly;
        Pawn c1 = MakePawn("C1", 0, new IntVec3(50, 0, 50), Colony);
        Pawn c2 = MakePawn("C2", 10, new IntVec3(51, 0, 50), Colony);
        MakePawn("G-friendly", 21, new IntVec3(49, 0, 50), Colony);
        Check("a non-hostile melee is never intercepted",
              !(bool)Call("Gm21AllyIntercept", "TryIntercept", c2, c1));

        // A successful interception owes the Grandmaster a riposte.
        Map riposteMap = new Map();
        TheMap = riposteMap;
        Find.TickManager.TicksGame = 9100;
        Call("Gm21CombatScheduler", "Reset");
        Pawn r5 = MakePawn("R-rip", 0, new IntVec3(50, 0, 50), Colony);
        Pawn a5 = MakePawn("A-rip", 10, new IntVec3(51, 0, 50), Raiders);
        Pawn g5 = MakePawn("G-rip", 21, new IntVec3(49, 0, 50), Colony);
        bool intercepted = (bool)Call("Gm21AllyIntercept", "TryIntercept", a5, r5);
        Find.TickManager.TicksGame = 9101;
        scheduler.GameComponentTick();
        Check("interception schedules a riposte against the attacker",
              intercepted && g5.meleeVerbs.attacksStub.Count == 1
              && g5.meleeVerbs.attacksStub[0] == a5);

        Rand.ForcedValueStub = null;
        Call("Gm21CombatScheduler", "Reset");
        TheMap = saved;
    }

    // ---- M21. PROJECTILE DEFENCE --------------------------------------------------------------
    static void ProjectileDefence()
    {
        Console.WriteLine("\n=== M21. Projectile difficulty ordering ===");

        float grenade = Difficulty(12f, 2.9f);
        float arrow = Difficulty(45f, 0f);
        float bullet = Difficulty(70f, 0f);
        float rocket = Difficulty(40f, 3.9f);
        float doomsday = Difficulty(40f, 14.9f);
        float hypersonic = Difficulty(200f, 0f);

        Check("grenade < arrow < bullet < rocket < doomsday < hypersonic",
              grenade < arrow && arrow < bullet && bullet < rocket
              && rocket < doomsday && doomsday < hypersonic,
              "g=" + grenade.ToString("0.00") + " a=" + arrow.ToString("0.00")
              + " b=" + bullet.ToString("0.00") + " r=" + rocket.ToString("0.00")
              + " d=" + doomsday.ToString("0.00") + " h=" + hypersonic.ToString("0.00"));
        Check("a thrown grenade is the easiest thing to catch", grenade < 1f);
        Check("difficulty has a floor (no free interception of a zero-speed def)",
              Difficulty(0f, 0f) > 0f);

        Console.WriteLine("\n=== M22. Stage 1: reach scales with movement speed ===");

        Pawn ordinary = MakePawn("Ord", 21, new IntVec3(150, 0, 150), Colony);
        Equip(ordinary, Weapon("Longsword", 2.2f));
        Pawn fast = MakePawn("Fast", 21, new IntVec3(152, 0, 150), Colony);
        Equip(fast, Weapon("Longsword", 2.2f));
        fast.statsStub[StatDefOf.MoveSpeed] = 8f;
        Pawn isekai = MakePawn("Blur", 21, new IntVec3(154, 0, 150), Colony);
        Equip(isekai, Weapon("Longsword", 2.2f));
        isekai.statsStub[StatDefOf.MoveSpeed] = 20f;

        float ordinaryBullet = Reach(ordinary, bullet);
        float fastBullet = Reach(fast, bullet);
        float isekaiBullet = Reach(isekai, bullet);
        Check("faster pawns reach bullets more often",
              ordinaryBullet < fastBullet && fastBullet < isekaiBullet,
              ordinaryBullet.ToString("0.00") + " < " + fastBullet.ToString("0.00")
              + " < " + isekaiBullet.ToString("0.00"));
        Check("an ordinary-speed Grandmaster rarely catches a bullet", ordinaryBullet < 0.25f,
              ordinaryBullet.ToString("0.00"));
        Check("a superhuman one routinely does", isekaiBullet > 0.7f, isekaiBullet.ToString("0.00"));
        Check("grenades are much easier than bullets for the same pawn",
              Reach(ordinary, grenade) > ordinaryBullet * 2f);
        Check("a doomsday rocket is nearly untouchable for an ordinary Grandmaster",
              Reach(ordinary, doomsday) < 0.1f, Reach(ordinary, doomsday).ToString("0.00"));

        Console.WriteLine("\n=== M23. Stage 2/3: deflection and return ===");

        float deflectSword = Opposed(ordinary, bullet, 0.6f, 0.99f);
        Pawn bare = MakePawn("Barehand", 21, new IntVec3(156, 0, 150), Colony);
        float deflectBare = Opposed(bare, bullet, 0.6f, 0.99f);
        Check("bare hands deflect a bullet far worse than a sword",
              deflectBare < deflectSword * 0.5f,
              deflectBare.ToString("0.00") + " vs " + deflectSword.ToString("0.00"));
        Check("bare hands can still deflect a slow thrown object",
              Opposed(bare, grenade, 0.6f, 0.99f) > 0.3f,
              Opposed(bare, grenade, 0.6f, 0.99f).ToString("0.00"));

        float returnBullet = Opposed(ordinary, bullet, 1.5f, 0.95f);
        Check("returning is harder than deflecting", returnBullet < deflectSword,
              returnBullet.ToString("0.00") + " < " + deflectSword.ToString("0.00"));

        Pawn precise = MakePawn("Precise", 21, new IntVec3(158, 0, 150), Colony);
        Equip(precise, Weapon("Longsword", 2.2f));
        SetCapacity(precise, PawnCapacityDefOf.Manipulation, 1.8f);
        SetCapacity(precise, PawnCapacityDefOf.Sight, 1.8f);
        Check("a finesse Grandmaster returns projectiles far more often",
              Opposed(precise, bullet, 1.5f, 0.95f) > returnBullet * 1.5f);

        Check("a pawn with no arms cannot deflect anything",
              Opposed(NoArms(), grenade, 0.6f, 0.99f) == 0f);

        Console.WriteLine("\n=== M24. Deflection distance: strength vs angle ===");

        Pawn strong = MakePawn("Strong", 21, new IntVec3(160, 0, 150), Colony);
        Equip(strong, Weapon("Warhammer", 8f));
        strong.statsStub[StatDefOf.MeleeDamageFactor] = 3f;
        strong.bodySizeStub = 2.25f;

        ProjectileProperties slow = new ProjectileProperties { speed = 12f, explosionRadius = 2.9f };
        ProjectileProperties fastProps = new ProjectileProperties { speed = 70f };

        float weakThrow = F(Call("Gm21ProjectileDefence", "RedirectDistance", slow, ordinary));
        float strongThrow = F(Call("Gm21ProjectileDefence", "RedirectDistance", slow, strong));
        Check("a strong Grandmaster hurls a grenade much further",
              strongThrow > weakThrow * 3f,
              weakThrow.ToString("0.0") + " -> " + strongThrow.ToString("0.0") + " tiles");
        Check("an ordinary pawn manages a handful of tiles",
              weakThrow >= 4f && weakThrow <= 12f, weakThrow.ToString("0.0"));

        float weakBullet = F(Call("Gm21ProjectileDefence", "RedirectDistance", fastProps, ordinary));
        float strongBullet = F(Call("Gm21ProjectileDefence", "RedirectDistance", fastProps, strong));
        Check("strength does NOT decide how far a bullet travels (angle is the lever)",
              weakBullet == strongBullet, weakBullet.ToString("0.0"));

        Pawn absurd = MakePawn("Absurd", 21, new IntVec3(162, 0, 150), Colony);
        Equip(absurd, Weapon("ColossalBlade", 40f));
        absurd.statsStub[StatDefOf.MeleeDamageFactor] = 10f;
        absurd.bodySizeStub = 4f;
        float absurdThrow = F(Call("Gm21ProjectileDefence", "RedirectDistance", slow, absurd));
        Check("an Isekai pawn's throw is absurd, but bounded",
              absurdThrow > strongThrow && absurdThrow <= 200f, absurdThrow.ToString("0.0"));
    }

    static Pawn NoArms()
    {
        Pawn p = MakePawn("NoArms", 21, new IntVec3(170, 0, 170), Colony);
        SetCapacity(p, PawnCapacityDefOf.Manipulation, 0f);
        return p;
    }

    static float Difficulty(float speed, float radius)
    {
        return F(Call("Gm21ProjectileDefence", "Difficulty",
                      new ProjectileProperties { speed = speed, explosionRadius = radius }));
    }

    static float Reach(Pawn p, float difficulty)
    {
        return F(CallExact("Gm21InterceptCurve", "Evaluate", new[] { typeof(float) },
                           Gm21Melee.MoveSpeed(p) * Gm21Melee.Reaction(p) / difficulty))
             * F(CallExact("Gm21InterceptCurve", "DistanceFactor", new[] { typeof(float) }, 1f));
    }

    static float Opposed(Pawn p, float difficulty, float hardness, float max)
    {
        float q = F(Call("Gm21ProjectileDefence", "DeflectionQuality", p));
        return Gm21Melee.Opposed(q, difficulty, hardness, max);
    }

    // ---- M25. SAFE DEFLECTION -----------------------------------------------------------------
    static void SafeDeflection()
    {
        Console.WriteLine("\n=== M25. Safe vector selection ===");

        Map field = new Map();
        Map saved = TheMap;
        TheMap = field;

        Pawn gm = MakePawn("Swatter", 21, new IntVec3(100, 0, 100), Colony);

        // Allies packed to the west, enemies to the east. The grenade must go east.
        for (int i = 0; i < 6; i++) MakePawn("Ally" + i, 0, new IntVec3(90 - i, 0, 100), Colony);
        for (int i = 0; i < 6; i++) MakePawn("Foe" + i, 0, new IntVec3(110 + i, 0, 100), Raiders);

        ThingDef grenadeDef = ProjectileDef("Grenade", 12f, 2.9f);
        Projectile grenade = new Projectile { def = grenadeDef, Position = gm.Position, Map = field, Spawned = true };

        IntVec3 chosen = (IntVec3)Call("Gm21SafeVector", "Choose",
                                       grenade, grenadeDef.projectile, gm, null, field, 10f);

        Check("a landing point was found", chosen.IsValid, chosen.ToString());
        Check("it is well away from the Grandmaster",
              chosen.DistanceTo(gm.Position) >= 2f, chosen.DistanceTo(gm.Position).ToString("0.0"));
        Check("it is sent toward the enemies, not the allies", chosen.x > gm.Position.x,
              chosen.ToString());

        // With allies on every side but one, it must find the gap.
        Map boxed = new Map();
        TheMap = boxed;
        Pawn gm2 = MakePawn("Boxed", 21, new IntVec3(100, 0, 100), Colony);
        for (int i = -12; i <= 12; i++)
        {
            if (i == 0) continue;
            MakePawn("W" + i, 0, new IntVec3(90, 0, 100 + i), Colony);
            MakePawn("N" + i, 0, new IntVec3(100 + i, 0, 110), Colony);
            MakePawn("S" + i, 0, new IntVec3(100 + i, 0, 90), Colony);
        }
        Projectile g2 = new Projectile { def = grenadeDef, Position = gm2.Position, Map = boxed, Spawned = true };
        IntVec3 gap = (IntVec3)Call("Gm21SafeVector", "Choose",
                                    g2, grenadeDef.projectile, gm2, null, boxed, 10f);
        Check("with allies on three sides it finds the open one", gap.IsValid && gap.x > gm2.Position.x,
              gap.ToString());

        // Near the map edge every bearing is clamped back in bounds rather than discarded.
        Map corner = new Map();
        TheMap = corner;
        Pawn gm3 = MakePawn("Corner", 21, new IntVec3(2, 0, 2), Colony);
        Projectile g3 = new Projectile { def = grenadeDef, Position = gm3.Position, Map = corner, Spawned = true };
        IntVec3 edge = (IntVec3)Call("Gm21SafeVector", "Choose",
                                     g3, grenadeDef.projectile, gm3, null, corner, 30f);
        Check("a Grandmaster in a map corner still finds a bearing",
              edge.IsValid && edge.InBounds(corner), edge.ToString());

        TheMap = saved;
    }

    // ---- M26. DOCTRINES -----------------------------------------------------------------------
    static void Doctrines()
    {
        Console.WriteLine("\n=== M26. Controlled force (Downed doctrine) ===");

        Pawn victim = MakePawn("Target", 10, new IntVec3(180, 0, 180), Raiders);
        BodyPartRecord leg = new BodyPartRecord
        {
            def = new BodyPartDef { defName = "LeftLeg", tags = new List<BodyPartTagDef>() },
            healthStub = 30f
        };
        victim.health.hediffSet.parts.Add(leg);

        float pulled = F(Call("Gm21MeleeDamage", "PullStrike", victim, leg, 200f, 1f));
        Check("an overwhelming blow is pulled below the limb's remaining health",
              pulled < 30f, pulled.ToString("0.0") + " vs 30 HP");
        Check("  ...but is still a serious wound", pulled > 20f, pulled.ToString("0.0"));

        float precise = F(Call("Gm21MeleeDamage", "PullStrike", victim, leg, 200f, 3f));
        Check("a more precise Grandmaster cuts it finer (more disabling, still safe)",
              precise > pulled && precise < 30f, precise.ToString("0.0"));

        float clumsy = F(Call("Gm21MeleeDamage", "PullStrike", victim, leg, 200f, 0.2f));
        Check("a clumsy Grandmaster holds back much more", clumsy < pulled, clumsy.ToString("0.0"));
        Check("  ...and never overshoots into destroying the limb", clumsy < 30f);

        float small = F(Call("Gm21MeleeDamage", "PullStrike", victim, leg, 5f, 1f));
        Check("a blow that was never going to destroy the limb is not reduced", small == 5f);

        Check("with no part chosen there is nothing to pull against",
              F(Call("Gm21MeleeDamage", "PullStrike", victim, null, 200f, 1f)) == 200f);

        BodyPartRecord ruined = new BodyPartRecord
        {
            def = new BodyPartDef { defName = "Ruined", tags = new List<BodyPartTagDef>() },
            healthStub = 0f
        };
        Check("a part with no health left is not pulled against",
              F(Call("Gm21MeleeDamage", "PullStrike", victim, ruined, 50f, 1f)) == 50f);

        Console.WriteLine("\n=== M27. Killer doctrine prefers a finishable vital ===");

        foreach (string n in new[] { "ConsciousnessSource", "BreathingPathway", "BloodPumpingSource",
                                     "BreathingSource", "BloodFiltrationSource", "MovingLimbCore",
                                     "MovingLimbSegment", "MovingLimbDigit", "Pelvis", "Spine",
                                     "ManipulationLimbCore", "ManipulationLimbSegment",
                                     "ManipulationLimbDigit" })
        {
            if (!DefDatabase<BodyPartTagDef>.Registry.ContainsKey(n))
                DefDatabase<BodyPartTagDef>.Registry[n] = new BodyPartTagDef { defName = n };
        }

        Pawn body = MakePawn("Body", 10, new IntVec3(185, 0, 185), Raiders);
        BodyPartRecord torso = MakePart("Torso", 0.5f, null);
        BodyPartRecord head = MakePart("Head", 0.1f, torso);
        BodyPartRecord brain = MakePart("Brain", 0.02f, head, "ConsciousnessSource");
        BodyPartRecord heart = MakePart("Heart", 0.04f, torso, "BloodPumpingSource");
        brain.healthStub = 100f;
        heart.healthStub = 6f;
        foreach (BodyPartRecord p in new[] { torso, head, brain, heart })
            body.health.hediffSet.parts.Add(p);

        BodyPartRecord chosen = (BodyPartRecord)Call("Gm21MeleeTargeting", "ChooseLethalPart", body, 40f);
        Check("a blow that can destroy the heart goes to the heart, not the brain",
              chosen == heart, chosen == null ? "null" : chosen.def.defName);

        BodyPartRecord weakBlow = (BodyPartRecord)Call("Gm21MeleeTargeting", "ChooseLethalPart", body, 2f);
        Check("a blow that can finish nothing falls back to the shared lethal ranking",
              weakBlow == brain, weakBlow == null ? "null" : weakBlow.def.defName);

        // Downed targeting must be the SHARED ladder, unchanged -- same answer as Shooting.
        BodyPartRecord leftLeg = MakePart("LeftLeg", 0.08f, body.health.hediffSet.parts[0], "MovingLimbCore");
        body.health.hediffSet.parts.Add(leftLeg);
        BodyPartRecord incap = (BodyPartRecord)Call("Gm21MeleeTargeting", "ChooseIncapacitatingPart", body);
        Check("Downed targeting returns the shared ladder's answer, unchanged",
              incap == leftLeg, incap == null ? "null" : incap.def.defName);
        Check("  ...and it is the same object the Shooting chooser returns",
              incap == Call("Gm21BodyTargeting", "ChooseIncapacitatingPart", body));
    }

    static BodyPartRecord MakePart(string name, float coverage, BodyPartRecord parent, params string[] tags)
    {
        List<BodyPartTagDef> list = new List<BodyPartTagDef>();
        foreach (string t in tags) list.Add(DefDatabase<BodyPartTagDef>.Registry[t]);
        BodyPartRecord r = new BodyPartRecord
        {
            def = new BodyPartDef { defName = name, tags = list },
            parent = parent,
            coverageAbsWithChildren = coverage,
            depth = BodyPartDepth.Outside,
            healthStub = 20f
        };
        if (parent != null) parent.parts.Add(r);
        return r;
    }

    // =======================================================================================
    //  GUARDIAN DOCTRINE
    //
    //  The Guardian does not intercept every projectile inside three tiles. It reacts only to a
    //  projectile that credibly threatens the Grandmaster or a protected ally. These sections
    //  drive the real threat model, the real intent classifier and the real Guardian selector.
    // =======================================================================================

    static Faction Allies, Neutrals;

    /// <summary>Builds a projectile the way RimWorld does: a resolved target and an intended one.</summary>
    static Projectile Shot(ThingDef def, Map map, IntVec3 at, Thing launcher,
                           LocalTargetInfo used, LocalTargetInfo intended)
    {
        Projectile p = new Projectile { def = def, Position = at, Map = map, Spawned = true };
        p.SetLauncherStub(launcher);
        p.usedTarget = used;
        p.intendedTarget = intended;
        return p;
    }

    static bool TryResolveThreat(Projectile proj, ThingDef def, Map map, IntVec3 impact, out object threat)
    {
        object[] args = { proj, def.projectile, map, impact, null };
        bool ok = (bool)T("Gm21GuardianThreat")
            .GetMethod("TryResolve", Any).Invoke(null, args);
        threat = args[4];
        return ok;
    }

    static object ThreatField(object threat, string name)
    {
        return T("Gm21Threat").GetField(name, Any).GetValue(threat);
    }

    // ---- M29. THREAT FILTERING ---------------------------------------------------------------
    static void GuardianThreatFiltering()
    {
        Console.WriteLine("\n=== M29. Threat filtering: the Guardian is not a CIWS turret ===");

        Map field = new Map();
        Map saved = TheMap;
        TheMap = field;

        Pawn gm = MakePawn("Guard", 21, new IntVec3(50, 0, 50), Colony);
        Equip(gm, Weapon("Longsword", 2.2f));
        Pawn ally = MakePawn("Researcher", 0, new IntVec3(52, 0, 50), Colony);
        Pawn raider = MakePawn("Raider", 10, new IntVec3(60, 0, 50), Raiders);
        Pawn shooter = MakePawn("Shooter", 10, new IntVec3(70, 0, 50), Raiders);

        ThingDef bullet = ProjectileDef("Bullet", 70f);
        object threat;

        // A round resolved onto bare ground that nobody is standing on threatens nobody.
        IntVec3 empty = new IntVec3(51, 0, 53);
        Projectile miss = Shot(bullet, field, new IntVec3(55, 0, 51), shooter,
                               new LocalTargetInfo(empty), new LocalTargetInfo(ally));
        Check("a harmless miss landing beside the Grandmaster is ignored",
              !TryResolveThreat(miss, bullet, field, empty, out threat));

        // A round resolved onto the Grandmaster themselves.
        Projectile atGm = Shot(bullet, field, new IntVec3(55, 0, 50), shooter,
                               new LocalTargetInfo(gm), new LocalTargetInfo(gm));
        Check("a round resolved onto the Grandmaster is a threat",
              TryResolveThreat(atGm, bullet, field, gm.Position, out threat));
        Check("  ...and the Grandmaster guards themselves",
              threat != null && ThreatField(threat, "guardian") == gm
              && ThreatField(threat, "victim") == gm);

        // A round resolved onto a protected ally inside the radius.
        Projectile atAlly = Shot(bullet, field, new IntVec3(55, 0, 50), shooter,
                                 new LocalTargetInfo(ally), new LocalTargetInfo(ally));
        Check("a round resolved onto a protected ally is a threat",
              TryResolveThreat(atAlly, bullet, field, ally.Position, out threat));
        Check("  ...and the nearby Grandmaster answers it",
              threat != null && ThreatField(threat, "guardian") == gm
              && ThreatField(threat, "victim") == ally);

        // A round that will hit an enemy is never the Guardian's business.
        Projectile atRaider = Shot(bullet, field, new IntVec3(65, 0, 50), shooter,
                                   new LocalTargetInfo(raider), new LocalTargetInfo(raider));
        Check("a round that will hit an enemy is ignored",
              !TryResolveThreat(atRaider, bullet, field, raider.Position, out threat));

        // An ally outside the three-tile radius is not protected by this Grandmaster.
        Pawn distant = MakePawn("Distant", 0, new IntVec3(58, 0, 50), Colony);
        Projectile atDistant = Shot(bullet, field, new IntVec3(65, 0, 50), shooter,
                                    new LocalTargetInfo(distant), new LocalTargetInfo(distant));
        Check("an ally beyond the Guardian radius is not protected",
              !TryResolveThreat(atDistant, bullet, field, distant.Position, out threat));

        Console.WriteLine("\n=== M30. The minigun case ===");

        // Ninety-four harmless rounds and six that connect. Only the six may reach the Guardian.
        int reacted = 0, ignored = 0;
        for (int i = 0; i < 100; i++)
        {
            Projectile round;
            IntVec3 impact;
            if (i < 90)
            {
                impact = new IntVec3(48 + (i % 5), 0, 47 + (i % 4));   // empty dirt near the GM
                if (impact == gm.Position || impact == ally.Position) impact = new IntVec3(45, 0, 45);
                round = Shot(bullet, field, new IntVec3(60, 0, 50), shooter,
                             new LocalTargetInfo(impact), new LocalTargetInfo(ally));
            }
            else if (i < 94)
            {
                impact = raider.Position;                               // hits another raider
                round = Shot(bullet, field, new IntVec3(62, 0, 50), shooter,
                             new LocalTargetInfo(raider), new LocalTargetInfo(raider));
            }
            else if (i < 97)
            {
                impact = ally.Position;                                 // hits the protected ally
                round = Shot(bullet, field, new IntVec3(60, 0, 50), shooter,
                             new LocalTargetInfo(ally), new LocalTargetInfo(ally));
            }
            else
            {
                impact = gm.Position;                                   // hits the Grandmaster
                round = Shot(bullet, field, new IntVec3(60, 0, 50), shooter,
                             new LocalTargetInfo(gm), new LocalTargetInfo(gm));
            }

            if (TryResolveThreat(round, bullet, field, impact, out threat)) reacted++;
            else ignored++;
        }

        Check("exactly the 6 threatening rounds invoke Guardian logic", reacted == 6,
              "reacted=" + reacted);
        Check("the other 94 are ignored", ignored == 94, "ignored=" + ignored);

        Console.WriteLine("\n=== M31. Explosive blast threat ===");

        ThingDef rocket = ProjectileDef("Rocket", 40f, 3.9f);

        // Aimed at the dirt beside the ally, who is still inside the blast.
        IntVec3 beside = new IntVec3(ally.Position.x + 2, 0, ally.Position.z);
        Projectile nearMiss = Shot(rocket, field, new IntVec3(60, 0, 50), shooter,
                                   new LocalTargetInfo(beside), new LocalTargetInfo(beside));
        Check("a rocket aimed at the ground still threatens a pawn inside its blast",
              TryResolveThreat(nearMiss, rocket, field, beside, out threat));

        // Far enough away that nobody protected is inside the radius.
        IntVec3 farOff = new IntVec3(ally.Position.x + 20, 0, ally.Position.z + 20);
        Projectile harmlessRocket = Shot(rocket, field, new IntVec3(60, 0, 50), shooter,
                                         new LocalTargetInfo(farOff), new LocalTargetInfo(farOff));
        Check("a rocket landing outside everyone's blast radius is ignored",
              !TryResolveThreat(harmlessRocket, rocket, field, farOff, out threat));

        TheMap = saved;
    }

    // ---- M32. PROTECTION RULES ----------------------------------------------------------------
    static void GuardianProtection()
    {
        Console.WriteLine("\n=== M32. Who a Guardian protects ===");

        if (Allies == null) { Allies = new Faction(); Neutrals = new Faction(); }
        Colony.relationsStub[Allies] = FactionRelationKind.Ally;
        Allies.relationsStub[Colony] = FactionRelationKind.Ally;
        Colony.relationsStub[Neutrals] = FactionRelationKind.Neutral;
        Neutrals.relationsStub[Colony] = FactionRelationKind.Neutral;

        Map m = new Map();
        Map saved = TheMap;
        TheMap = m;

        Pawn gm = MakePawn("P-Guard", 21, new IntVec3(20, 0, 20), Colony);
        Pawn sameFaction = MakePawn("P-Colonist", 0, new IntVec3(21, 0, 20), Colony);
        Pawn alliedPawn = MakePawn("P-Ally", 0, new IntVec3(22, 0, 20), Allies);
        Pawn neutralPawn = MakePawn("P-Neutral", 0, new IntVec3(19, 0, 20), Neutrals);
        Pawn hostilePawn = MakePawn("P-Raider", 0, new IntVec3(18, 0, 20), Raiders);
        Pawn wildAnimal = MakePawn("P-Wild", 0, new IntVec3(20, 0, 21), null);

        Func<Pawn, bool> protects = p =>
            (bool)Call("Gm21GuardianThreat", "Protects", gm, p);

        Check("a Grandmaster protects themselves", protects(gm));
        Check("  ...and their own faction", protects(sameFaction));
        Check("  ...and a formally allied faction", protects(alliedPawn));
        Check("a neutral pawn is NOT automatically protected", !protects(neutralPawn));
        Check("a hostile pawn is never protected", !protects(hostilePawn));
        Check("a factionless wild animal is not protected", !protects(wildAnimal));

        TheMap = saved;
    }

    // ---- M33. INTENT --------------------------------------------------------------------------
    static void GuardianIntent()
    {
        Console.WriteLine("\n=== M33. Intent outranks faction identity ===");

        Map m = new Map();
        Map saved = TheMap;
        TheMap = m;

        Pawn mark = MakePawn("Mark", 21, new IntVec3(30, 0, 30), Colony);       // the Grandmaster
        Equip(mark, Weapon("Longsword", 2.2f));
        Pawn alice = MakePawn("Alice", 0, new IntVec3(31, 0, 30), Colony);      // protected ally
        Pawn bob = MakePawn("Bob", 5, new IntVec3(40, 0, 30), Colony);          // friendly shooter
        Pawn raiderA = MakePawn("RaiderA", 10, new IntVec3(50, 0, 30), Raiders);
        Pawn enemyShooter = MakePawn("EnemyShooter", 10, new IntVec3(55, 0, 30), Raiders);

        ThingDef bullet = ProjectileDef("Bullet", 70f);
        object threat;

        Func<Projectile, ThingDef, IntVec3, string> intentOf = (proj, def, impact) =>
        {
            object t;
            if (!TryResolveThreat(proj, def, m, impact, out t)) return "IGNORED";
            return ThreatField(t, "intent").ToString();
        };

        // Hostile shooter, hits the Grandmaster.
        Check("an enemy's round is hostile",
              intentOf(Shot(bullet, m, new IntVec3(45, 0, 30), enemyShooter,
                            new LocalTargetInfo(mark), new LocalTargetInfo(mark)),
                       bullet, mark.Position) == "Hostile");

        // Bob aimed at RaiderA; the shot mis-resolved onto Mark. Accident.
        Check("an ally's shot that mis-resolves onto the Grandmaster is an ACCIDENT",
              intentOf(Shot(bullet, m, new IntVec3(42, 0, 30), bob,
                            new LocalTargetInfo(mark), new LocalTargetInfo(raiderA)),
                       bullet, mark.Position) == "AccidentalFriendlyFire");

        // Bob aimed at RaiderA; mis-resolved onto Alice. Also an accident.
        Check("  ...and onto a protected ally, likewise an accident",
              intentOf(Shot(bullet, m, new IntVec3(42, 0, 30), bob,
                            new LocalTargetInfo(alice), new LocalTargetInfo(raiderA)),
                       bullet, alice.Position) == "AccidentalFriendlyFire");

        // Bob was ORDERED to force-attack Mark. Same faction, hostile intent.
        Check("a friendly force-attacking the Grandmaster is HOSTILE intent",
              intentOf(Shot(bullet, m, new IntVec3(42, 0, 30), bob,
                            new LocalTargetInfo(mark), new LocalTargetInfo(mark)),
                       bullet, mark.Position) == "Hostile");

        // Bob was ordered to force-attack Alice, whom Mark protects.
        Check("a friendly force-attacking a protected ally is HOSTILE intent",
              intentOf(Shot(bullet, m, new IntVec3(42, 0, 30), bob,
                            new LocalTargetInfo(alice), new LocalTargetInfo(alice)),
                       bullet, alice.Position) == "Hostile");

        // Bob shoots RaiderA and will actually hit RaiderA. Nothing to do.
        Check("an ally's correctly-resolved shot at an enemy is ignored entirely",
              intentOf(Shot(bullet, m, new IntVec3(45, 0, 30), bob,
                            new LocalTargetInfo(raiderA), new LocalTargetInfo(raiderA)),
                       bullet, raiderA.Position) == "IGNORED");

        // Bob's harmless miss into empty dirt near Mark.
        IntVec3 dirt = new IntVec3(33, 0, 33);
        Check("an ally's harmless miss is ignored entirely",
              intentOf(Shot(bullet, m, new IntVec3(42, 0, 30), bob,
                            new LocalTargetInfo(dirt), new LocalTargetInfo(raiderA)),
                       bullet, dirt) == "IGNORED");

        Console.WriteLine("\n=== M34. Explosive intent ===");
        ThingDef rocket = ProjectileDef("Rocket", 40f, 3.9f);

        // Bob fires a rocket at RaiderA; it will land next to Alice. Accident.
        IntVec3 besideAlice = new IntVec3(alice.Position.x + 2, 0, alice.Position.z);
        Check("an ally's rocket that drifts onto a protected pawn is an ACCIDENT",
              intentOf(Shot(rocket, m, new IntVec3(42, 0, 30), bob,
                            new LocalTargetInfo(besideAlice), new LocalTargetInfo(raiderA)),
                       rocket, besideAlice) == "AccidentalFriendlyFire");

        // Bob was ordered to drop a rocket on the ground right next to Mark. Aiming a warhead at a
        // cell whose blast covers a protected pawn is aiming at that pawn.
        IntVec3 besideMark = new IntVec3(mark.Position.x + 1, 0, mark.Position.z);
        Check("an ally's rocket deliberately aimed beside the Grandmaster is HOSTILE intent",
              intentOf(Shot(rocket, m, new IntVec3(42, 0, 30), bob,
                            new LocalTargetInfo(besideMark), new LocalTargetInfo(besideMark)),
                       rocket, besideMark) == "Hostile");

        // A launcher-less projectile (trap, orphaned turret shot) is treated as a threat.
        Check("a projectile with no launcher is treated as hostile",
              intentOf(Shot(bullet, m, new IntVec3(42, 0, 30), null,
                            new LocalTargetInfo(mark), new LocalTargetInfo(mark)),
                       bullet, mark.Position) == "Hostile");

        TheMap = saved;
    }

    // ---- M35. BEST GUARDIAN -------------------------------------------------------------------
    static void GuardianSelection()
    {
        Console.WriteLine("\n=== M35. The best Guardian answers, not the first one in grid order ===");

        Map m = new Map();
        Map saved = TheMap;
        TheMap = m;

        Pawn victim = MakePawn("Victim", 0, new IntVec3(40, 0, 40), Colony);
        Pawn shooter = MakePawn("Sniper", 10, new IntVec3(60, 0, 40), Raiders);

        // Registered in grid order such that the SLOW one is encountered first.
        Pawn slowAdjacent = MakePawn("Slow", 21, new IntVec3(39, 0, 39), Colony);
        Equip(slowAdjacent, Weapon("Longsword", 2.2f));
        slowAdjacent.statsStub[StatDefOf.MoveSpeed] = 3f;

        Pawn fastFarther = MakePawn("Fast", 21, new IntVec3(43, 0, 40), Colony);   // 3 tiles
        Equip(fastFarther, Weapon("Longsword", 2.2f));
        fastFarther.statsStub[StatDefOf.MoveSpeed] = 16f;

        ThingDef bullet = ProjectileDef("Bullet", 70f);
        object threat;
        bool found = TryResolveThreat(
            Shot(bullet, m, new IntVec3(50, 0, 40), shooter,
                 new LocalTargetInfo(victim), new LocalTargetInfo(victim)),
            bullet, m, victim.Position, out threat);

        Check("a Guardian was found", found);
        Check("the much faster Grandmaster is chosen over the nearer slow one",
              found && ThreatField(threat, "guardian") == fastFarther,
              found ? ((Pawn)ThreatField(threat, "guardian")).def.defName : "none");

        // ...and the reverse, so the test is not just asserting "pick the far one".
        Map m2 = new Map();
        TheMap = m2;
        Pawn victim2 = MakePawn("Victim2", 0, new IntVec3(40, 0, 40), Colony);
        MakePawn("Sniper2", 10, new IntVec3(60, 0, 40), Raiders);
        Pawn fastAdjacent = MakePawn("FastNear", 21, new IntVec3(39, 0, 39), Colony);
        Equip(fastAdjacent, Weapon("Longsword", 2.2f));
        fastAdjacent.statsStub[StatDefOf.MoveSpeed] = 16f;
        Pawn slowFarther = MakePawn("SlowFar", 21, new IntVec3(43, 0, 40), Colony);
        Equip(slowFarther, Weapon("Longsword", 2.2f));
        slowFarther.statsStub[StatDefOf.MoveSpeed] = 3f;

        Pawn shooter2 = MakePawn("Sniper3", 10, new IntVec3(61, 0, 40), Raiders);
        bool found2 = TryResolveThreat(
            Shot(bullet, m2, new IntVec3(50, 0, 40), shooter2,
                 new LocalTargetInfo(victim2), new LocalTargetInfo(victim2)),
            bullet, m2, victim2.Position, out threat);
        Check("when the fast one is also the near one, they are chosen",
              found2 && ThreatField(threat, "guardian") == fastAdjacent);

        // Only ONE Guardian is handed the attempt.
        Check("exactly one Guardian is selected per threat",
              threat != null && ThreatField(threat, "guardian") != null);

        Console.WriteLine("\n=== M36. Guardian reachability ===");

        Map walled = new Map();
        TheMap = walled;
        Pawn sealedAlly = MakePawn("Sealed", 0, new IntVec3(40, 0, 40), Colony);
        Pawn outside = MakePawn("Outside", 21, new IntVec3(43, 0, 40), Colony);
        Equip(outside, Weapon("Longsword", 2.2f));
        Pawn shooter3 = MakePawn("Sniper4", 10, new IntVec3(60, 0, 40), Raiders);
        walled.blockedStub.Add(new IntVec3(41, 0, 40));
        walled.blockedStub.Add(new IntVec3(42, 0, 40));

        Check("a Grandmaster cannot protect someone through sealed granite",
              !TryResolveThreat(
                  Shot(bullet, walled, new IntVec3(50, 0, 40), shooter3,
                       new LocalTargetInfo(sealedAlly), new LocalTargetInfo(sealedAlly)),
                  bullet, walled, sealedAlly.Position, out threat));

        // An incapable Grandmaster is no Guardian at all.
        Map m3 = new Map();
        TheMap = m3;
        Pawn v3 = MakePawn("V3", 0, new IntVec3(40, 0, 40), Colony);
        Pawn downedGm = MakePawn("DownedGM", 21, new IntVec3(41, 0, 40), Colony);
        Equip(downedGm, Weapon("Longsword", 2.2f));
        downedGm.health.Downed = true;
        Pawn shooter4 = MakePawn("Sniper5", 10, new IntVec3(60, 0, 40), Raiders);
        Check("a downed Grandmaster is never selected as Guardian",
              !TryResolveThreat(
                  Shot(bullet, m3, new IntVec3(50, 0, 40), shooter4,
                       new LocalTargetInfo(v3), new LocalTargetInfo(v3)),
                  bullet, m3, v3.Position, out threat));

        TheMap = saved;
    }

    // ---- M37. FRIENDLY RECOVERY ---------------------------------------------------------------
    static void FriendlyRecovery()
    {
        Console.WriteLine("\n=== M37. Friendly Recovery difficulty is angular ===");

        Map m = new Map();
        Map saved = TheMap;
        TheMap = m;

        ThingDef bullet = ProjectileDef("Bullet", 70f);
        Pawn bob = MakePawn("RecBob", 5, new IntVec3(10, 0, 50), Colony);
        Pawn raiderA = MakePawn("RecRaiderA", 10, new IntVec3(60, 0, 50), Raiders);

        // A shot already flying east, with the enemy due east: almost no correction needed.
        Projectile aligned = Shot(bullet, m, new IntVec3(30, 0, 50), bob,
                                  new LocalTargetInfo(new IntVec3(50, 0, 50)),
                                  new LocalTargetInfo(raiderA));
        aligned.Launch(bob, new Vector3(10, 0, 50),
                       new LocalTargetInfo(new IntVec3(50, 0, 50)),
                       new LocalTargetInfo(raiderA), ProjectileHitFlags.All, false, null, null);
        aligned.Position = new IntVec3(30, 0, 50);
        float alignedFactor = F(Call("Gm21ProjectileDefence", "CorrectionFactor", aligned, raiderA));

        // A shot flying west while the enemy is east: it has to be turned right around.
        Projectile reversed = Shot(bullet, m, new IntVec3(30, 0, 50), bob,
                                   new LocalTargetInfo(new IntVec3(5, 0, 50)),
                                   new LocalTargetInfo(raiderA));
        reversed.Launch(bob, new Vector3(50, 0, 50),
                        new LocalTargetInfo(new IntVec3(5, 0, 50)),
                        new LocalTargetInfo(raiderA), ProjectileHitFlags.All, false, null, null);
        reversed.Position = new IntVec3(30, 0, 50);
        float reversedFactor = F(Call("Gm21ProjectileDefence", "CorrectionFactor", reversed, raiderA));

        // A shot flying north while the enemy is east: a right-angle correction.
        Projectile perpendicular = Shot(bullet, m, new IntVec3(30, 0, 50), bob,
                                        new LocalTargetInfo(new IntVec3(30, 0, 70)),
                                        new LocalTargetInfo(raiderA));
        perpendicular.Launch(bob, new Vector3(30, 0, 30),
                             new LocalTargetInfo(new IntVec3(30, 0, 70)),
                             new LocalTargetInfo(raiderA), ProjectileHitFlags.All, false, null, null);
        perpendicular.Position = new IntVec3(30, 0, 50);
        float perpFactor = F(Call("Gm21ProjectileDefence", "CorrectionFactor", perpendicular, raiderA));

        Check("a nearly-correct shot is the easiest to salvage",
              alignedFactor < perpFactor && perpFactor < reversedFactor,
              alignedFactor.ToString("0.00") + " < " + perpFactor.ToString("0.00")
              + " < " + reversedFactor.ToString("0.00"));
        Check("  ...easier even than returning a shot to its sender", alignedFactor < 1f,
              alignedFactor.ToString("0.00"));
        Check("  ...and a wildly wrong shot is much harder", reversedFactor > 2.5f,
              reversedFactor.ToString("0.00"));

        Console.WriteLine("\n=== M38. Friendly Recovery preserves attribution ===");

        // Bob's shot is salvaged back toward Raider A. Bob keeps the shot.
        Projectile recovered = Shot(bullet, m, new IntVec3(30, 0, 50), bob,
                                    new LocalTargetInfo(new IntVec3(31, 0, 51)),
                                    new LocalTargetInfo(raiderA));
        recovered.Launch(bob, new Vector3(10, 0, 50),
                         new LocalTargetInfo(new IntVec3(31, 0, 51)),
                         new LocalTargetInfo(raiderA), ProjectileHitFlags.All, false, null, null);
        recovered.Position = new IntVec3(30, 0, 50);

        Pawn mark = MakePawn("RecMark", 21, new IntVec3(31, 0, 51), Colony);
        Equip(mark, Weapon("Longsword", 2.2f));
        SetCapacity(mark, PawnCapacityDefOf.Manipulation, 4f);
        SetCapacity(mark, PawnCapacityDefOf.Sight, 4f);

        Rand.ForcedValueStub = 0f;   // every roll succeeds; we are testing WHERE it goes and WHOSE it is
        bool ok = (bool)T("Gm21ProjectileDefence")
            .GetMethod("TryFriendlyRecovery", Any)
            .Invoke(null, new object[] { recovered, bullet.projectile, mark, m, 10f, 1f });
        Rand.ForcedValueStub = null;

        Check("the shot is salvaged", ok);
        Check("  ...toward the ORIGINAL intended enemy, not a convenient one",
              ok && recovered.usedTarget.Thing == raiderA,
              recovered.usedTarget.Thing == null ? "cell" : recovered.usedTarget.Thing.def.defName);
        Check("  ...with Bob still the launcher, so Bob keeps the shot and the XP",
              ok && recovered.Launcher == bob,
              recovered.Launcher == null ? "null" : recovered.Launcher.def.defName);
        Check("  ...and the Grandmaster recorded separately, not forged into the projectile",
              Call("Gm21ProjectileDefence", "RedirectorOf", recovered) == mark);

        // The original target being gone means there is nothing to salvage toward.
        Projectile orphan = Shot(bullet, m, new IntVec3(30, 0, 50), bob,
                                 new LocalTargetInfo(new IntVec3(31, 0, 51)),
                                 new LocalTargetInfo(raiderA));
        raiderA.Destroyed = true;
        Rand.ForcedValueStub = 0f;
        bool orphanOk = (bool)T("Gm21ProjectileDefence")
            .GetMethod("TryFriendlyRecovery", Any)
            .Invoke(null, new object[] { orphan, bullet.projectile, mark, m, 10f, 1f });
        Rand.ForcedValueStub = null;
        raiderA.Destroyed = false;
        Check("a destroyed original target means no recovery (safe deflection instead)", !orphanOk);

        // An original target that is no longer hostile is not a valid recovery target either.
        Pawn formerEnemy = MakePawn("Captured", 0, new IntVec3(62, 0, 50), Colony);
        Projectile stale = Shot(bullet, m, new IntVec3(30, 0, 50), bob,
                                new LocalTargetInfo(new IntVec3(31, 0, 51)),
                                new LocalTargetInfo(formerEnemy));
        Rand.ForcedValueStub = 0f;
        bool staleOk = (bool)T("Gm21ProjectileDefence")
            .GetMethod("TryFriendlyRecovery", Any)
            .Invoke(null, new object[] { stale, bullet.projectile, mark, m, 10f, 1f });
        Rand.ForcedValueStub = null;
        Check("an original target that is no longer hostile is not salvaged toward", !staleOk);

        TheMap = saved;
    }

    // ---- M39. SAFE PATH -----------------------------------------------------------------------
    static void GuardianSafePath()
    {
        Console.WriteLine("\n=== M39. Safe deflection avoids the flight path, not just the landing cell ===");

        Map m = new Map();
        Map saved = TheMap;
        TheMap = m;

        Pawn gm = MakePawn("PathGuard", 21, new IntVec3(100, 0, 100), Colony);
        Pawn rescued = MakePawn("Rescued", 0, new IntVec3(101, 0, 100), Colony);

        // A corridor of colonists due east, with nobody at the far end; open ground due west.
        for (int i = 3; i <= 12; i++) MakePawn("Corridor" + i, 0, new IntVec3(100 + i, 0, 100), Colony);

        ThingDef bullet = ProjectileDef("Bullet", 70f);
        Projectile proj = new Projectile { def = bullet, Position = gm.Position, Map = m, Spawned = true };

        IntVec3 chosen = (IntVec3)Call("Gm21SafeVector", "Choose",
                                       proj, bullet.projectile, gm, rescued, m, 20f);

        Check("a bearing was chosen", chosen.IsValid, chosen.ToString());
        Check("it does not fire down a corridor of colonists to reach empty ground",
              chosen.IsValid && chosen.x < gm.Position.x, chosen.ToString());
        Check("and it is sent away from the pawn just rescued",
              chosen.IsValid && chosen.DistanceTo(rescued.Position) > 2f,
              chosen.DistanceTo(rescued.Position).ToString("0.0"));

        TheMap = saved;
    }

    // ---- M40. MICRO-DASH ----------------------------------------------------------------------
    static void GuardianMicroDash()
    {
        Console.WriteLine("\n=== M40. The micro-dash never moves the pawn ===");

        Map m = new Map();
        Map saved = TheMap;
        TheMap = m;

        Pawn gm = MakePawn("Dasher", 21, new IntVec3(70, 0, 70), Colony);
        Equip(gm, Weapon("Longsword", 2.2f));
        Pawn ally = MakePawn("Dashed", 0, new IntVec3(73, 0, 70), Colony);

        ThingDef bullet = ProjectileDef("Bullet", 70f);
        Projectile proj = new Projectile { def = bullet, Position = ally.Position, Map = m, Spawned = true };

        IntVec3 before = gm.Position;
        Stance stanceBefore = gm.stances.curStance;

        FleckMaker.ResetStub();
        Call("Gm21GuardianFx", "MicroDash", gm, ally, proj);

        Check("the Grandmaster is still in their own cell", gm.Position == before, gm.Position.ToString());
        Check("their stance is untouched", gm.stances.curStance == stanceBefore);
        Check("an afterimage was drawn toward the interception point",
              FleckMaker.connectingLinesStub == 1, "lines=" + FleckMaker.connectingLinesStub);
        Check("a contact spark was drawn", FleckMaker.glowsStub == 1);
        Check("a metallic ring was played", Verse.Sound.SoundStarter.shotsStub > 0);

        // Defending their own cell: there is no gap to streak across.
        FleckMaker.ResetStub();
        Projectile atSelf = new Projectile { def = bullet, Position = gm.Position, Map = m, Spawned = true };
        Call("Gm21GuardianFx", "MicroDash", gm, gm, atSelf);
        Check("no streak is drawn when the Grandmaster defends their own cell",
              FleckMaker.connectingLinesStub == 0);
        Check("  ...but the contact is still shown", FleckMaker.glowsStub == 1);
        Check("and the pawn still has not moved", gm.Position == before);

        // A hundred rounds must not strobe the pawn anywhere.
        for (int i = 0; i < 100; i++) Call("Gm21GuardianFx", "MicroDash", gm, ally, proj);
        Check("100 interceptions leave the pawn exactly where they started", gm.Position == before);

        TheMap = saved;
    }

    // ---- M28. TRANSLATION KEYS ------------------------------------------------------------------
    static void TranslationKeys()
    {
        Console.WriteLine("\n=== M28. Shipped translation keys ===");

        var shipped = new HashSet<string>();
        try
        {
            var doc = new System.Xml.XmlDocument();
            doc.Load("Languages/English/Keyed/Grandmaster21.xml");
            foreach (System.Xml.XmlNode n in doc.DocumentElement.ChildNodes)
            {
                if (n.NodeType == System.Xml.XmlNodeType.Element) shipped.Add(n.Name);
            }
        }
        catch (Exception e)
        {
            Check("could read the shipped language file", false, e.Message);
            return;
        }

        string[] needed = {
            "GM21_Melee_GizmoLabel", "GM21_Melee_GizmoDesc",
            "GM21_Melee_Normal", "GM21_Melee_NormalDesc",
            "GM21_Melee_Killer", "GM21_Melee_KillerDesc",
            "GM21_Melee_Downed", "GM21_Melee_DownedDesc",
            "GM21_Melee_Disarmed", "GM21_Melee_Intercepted",
            "GM21_Melee_Returned", "GM21_Melee_Recovered",
            "GM21_Melee_Deflected", "GM21_Melee_RoughDeflection",
            "GM21_Achieved_Melee", "GM21_Descriptor_Melee"
        };
        bool all = true;
        foreach (string k in needed) if (!shipped.Contains(k)) { all = false; Console.WriteLine("  missing: " + k); }
        Check("every key the melee code looks up is actually shipped", all);

        Check("the Shooting gizmo label now names its skill",
              shipped.Contains("GM21_AimMode_GizmoLabel"));
    }
}
