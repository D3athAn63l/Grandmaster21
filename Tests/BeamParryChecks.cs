// Real-DLL integration checks. Only game-world/native services are fixture shims; the installed
// Beam Parry hooks and real WarmupComplete, TryCastNextBurstShot, TryCastShot, HitCell,
// ApplyDamage, Reset, VerbTick, Notify_DamageApplied and StunFor execute. See Docs/BeamParry.md.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml.Linq;
using System.IO;
using Grandmaster21;
using HarmonyLib;
using Mono.Cecil;
using RimWorld;
using UnityEngine;
using Verse;

class BeamParryChecks
{
    static int pass, fail, rolls, damageCalls, fires, stuns, feedback, shots;
    static float damageTotal;
    static bool rollSuccess, immune, awake = true, capable = true, clearRoute = true, hostile = true;
    static float manipulation = 1f, consciousness = 1f;
    static int aptitude, nextId=100, selectionWarnings;
    static float lastChance;
    static bool throwShot, throwStun, throwFeedback, throwSelect, throwSelectAlways, throwRoll, reenterSelect;
    static int burstCount=4;
    static string reenterPhase;
    static int reentries;
    static Verb activeVerb;
    static readonly Dictionary<Pawn, float> manipulationOf = new Dictionary<Pawn, float>();
    static readonly Type Logic = typeof(Gm21Melee).Assembly.GetType("Grandmaster21.Gm21BeamParry");
    static readonly Type Patches = typeof(Gm21Melee).Assembly.GetType("Grandmaster21.Gm21BeamParryPatches");
    static Map map;
    static ThingWithComps weapon;
    static Pawn victim, attacker, guardian;
    static readonly List<Thing> occupants = new List<Thing>();
    static readonly List<Thing> contacts = new List<Thing>();
    static readonly Dictionary<Thing, IntVec3> positions = new Dictionary<Thing, IntVec3>();
    static TickManager ticks;
    static object Call(string name, params object[] args) { return AccessTools.Method(Logic, name).Invoke(null, args); }
    static void Set(object o, string n, object v) { AccessTools.Field(o.GetType(), n).SetValue(o,v); }
    static T Empty<T>() { return (T)FormatterServices.GetUninitializedObject(typeof(T)); }
    static void Check(string n, bool ok) { Console.WriteLine((ok ? "PASS " : "FAIL ") + n); if(ok) pass++; else fail++; }
    static HarmonyMethod Hook(string n) { return new HarmonyMethod(AccessTools.Method(typeof(BeamParryChecks), n)); }
    static void Prefix(Harmony h, Type t, string n, string hook, params Type[] args)
    { h.Patch(AccessTools.Method(t,n,args.Length == 0 ? null : args), prefix:Hook(hook)); }
    static void Getter(Harmony h, Type t, string n, string hook)
    { h.Patch(AccessTools.PropertyGetter(t,n), prefix:Hook(hook)); }

    // Simulates another patch that damages through the SAME beam from inside one phase of the decision.
    static void Reenter(string phase)
    {
        if(reenterPhase!=phase || activeVerb==null || reentries>=6) return;
        reentries++;
        AccessTools.Method(typeof(Verb_ShootBeam),"ApplyDamage",new[]{typeof(Thing),typeof(IntVec3),typeof(float)}).Invoke(activeVerb,new object[]{victim,new IntVec3(10,0,10),1f});
    }
    public static bool BurstCount(ref int __result) { __result=burstCount; return false; }
    public static bool Angle(ref float __result) { __result=0f; return false; }
    public static bool Skip() { return false; }
    public static bool Yes(ref bool __result) { __result=true; return false; }
    public static bool No(ref bool __result) { __result=false; return false; }
    public static bool LogText(string text) { if(text.Contains("Beam Parry guardian selection")) selectionWarnings++; Console.WriteLine("GAME " + text); return false; }
    public static bool Spawned(ref bool __result) { __result=true; return false; }
    public static bool MapValue(ref Map __result) { __result=map; return false; }
    public static bool Position(Thing __instance, ref IntVec3 __result)
    { __result=positions.ContainsKey(__instance) ? positions[__instance] : new IntVec3(10,0,10); return false; }
    public static bool Grid(IntVec3 c, ref List<Thing> __result)
    { __result=occupants.Where(t=>positions[t] == c).ToList();  return false; }
    public static bool Things(ref List<Thing> __result) { __result=contacts; return false; }
    public static bool Range(int minInclusive, ref int __result) { __result=minInclusive; return false; }
    public static bool Chance(float chance, ref bool __result)
    { if(chance>0f && chance<1f) { rolls++; lastChance=chance; if(throwRoll) throw new InvalidOperationException("fixture roll failure"); Reenter("roll"); __result=rollSuccess; } else __result=chance>=1f; return false; }
    public static bool Awake(ref bool __result) { __result=awake; return false; }
    public static bool Capable(ref bool __result) { __result=capable; return false; }
    public static bool Level(PawnCapacitiesHandler __instance, PawnCapacityDef capacity, ref float __result)
    {
        Pawn owner=(Pawn)AccessTools.Field(typeof(PawnCapacitiesHandler),"pawn").GetValue(__instance); float own;
        __result=capacity==PawnCapacityDefOf.Manipulation ? (owner!=null && manipulationOf.TryGetValue(owner,out own) ? own : manipulation) : capacity==PawnCapacityDefOf.Consciousness ? consciousness : 1f; return false;
    }
    public static bool Equip(ref ThingWithComps __result) { __result=weapon; return false; }
    public static bool Stat(StatDef stat, ref float __result) { __result=stat==StatDefOf.Mass ? 3f : 1f; return false; }
    public static bool Route(ref bool __result) { __result=clearRoute; return false; }
    public static bool Hostile(Thing a, Thing b, ref bool __result)
    {
        if(throwSelect) { throwSelect=throwSelectAlways; throw new InvalidOperationException("fixture selection failure"); }
        if(reenterSelect && activeVerb!=null && reentries<6 && !Flag(activeVerb,"Evaluated"))
        {
            // A foreign patch on a call made during Guardian selection that itself damages via the same beam.
            reentries++;
            AccessTools.Method(typeof(Verb_ShootBeam),"ApplyDamage",new[]{typeof(Thing),typeof(IntVec3),typeof(float)}).Invoke(activeVerb,new object[]{victim,new IntVec3(10,0,10),1f});
        }
        __result=hostile && (a==attacker || b==attacker); return false;
    }
    public static bool Aptitude(ref int __result) { __result=aptitude; return false; }
    public static bool Vector(ref Vector3 __result) { __result=new Vector3(10f,0f,10f); return false; }
    public static bool Last(ref IntVec3 __result) { __result=IntVec3.Invalid; return false; }
    public static bool TickManager(ref TickManager __result) { __result=ticks; return false; }
    public static bool Line(IntVec3 root, LocalTargetInfo targ, ref ShootLine resultingLine, ref bool __result)
    { resultingLine=new ShootLine(root,targ.Cell); __result=true; return false; }
    public static bool HitCell(ref IntVec3 hitCell, ref bool __result)
    { hitCell=new IntVec3(10,0,10); __result=true; return false; }
    public static bool Neighbours(ref IEnumerable<IntVec3> __result)
    { __result=new[]{new IntVec3(11,0,10),new IntVec3(12,0,10)}; return false; }
    public static bool TakeDamage(Thing __instance, DamageInfo dinfo, ref DamageWorker.DamageResult __result)
    { damageCalls++; damageTotal+=dinfo.Amount; Check("damage recipient is victim, never attacker", __instance==victim); __result=new DamageWorker.DamageResult(); return false; }
    public static bool Fire(ref bool __result) { fires++; __result=true; return false; }
    public static bool Text() { feedback++; Reenter("feedback"); if(throwFeedback) throw new InvalidOperationException("fixture feedback failure"); return false; }
    public static bool Immunity(ref bool __result) { if(throwStun) throw new InvalidOperationException("fixture stun failure"); __result=!immune; return false; }
    public static void StunAttempt(ref bool addBattleLog) { stuns++; addBattleLog=false; Reenter("stun"); }
    public static void ShotCount() { shots++; if(throwShot) throw new InvalidOperationException("fixture shot failure"); }

    // Avoid live pawn stance/rendering managers in the headless burst-completion path. The real
    // caster is still a Pawn for defence/stun/VerbTick; all beam lifecycle methods are inherited.
    class FixtureBeam : Verb_ShootBeam { public override bool CasterIsPawn { get { return false; } } }

    static void Environment(Harmony h)
    {
        Prefix(h,typeof(Log),"Warning","LogText",typeof(string));
        Prefix(h,typeof(Log),"Error","LogText",typeof(string));
        Prefix(h,typeof(DefOfHelper),"EnsureInitializedInCtor","Skip");
        foreach(var t in new[]{typeof(SkillDefOf),typeof(PawnCapacityDefOf),typeof(StatDefOf),typeof(DamageDefOf)})
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(t.TypeHandle);
        SkillDefOf.Melee=new SkillDef{defName="Melee"};
        PawnCapacityDefOf.Manipulation=new PawnCapacityDef{defName="Manipulation"};
        PawnCapacityDefOf.Consciousness=new PawnCapacityDef{defName="Consciousness"};
        PawnCapacityDefOf.Sight=new PawnCapacityDef{defName="Sight"};
        StatDefOf.Mass=new StatDef{defName="Mass"};
        DamageDefOf.Stun=new DamageDef{defName="Stun",causeStun=true};
        map=Empty<Map>(); map.thingGrid=Empty<ThingGrid>(); ticks=Empty<TickManager>();
        weapon=Empty<ThingWithComps>(); weapon.def=Empty<ThingDef>(); weapon.def.defName="FixtureSword"; weapon.def.category=ThingCategory.Item; weapon.def.thingClass=typeof(ThingWithComps); weapon.def.equipmentType=EquipmentType.Primary; weapon.def.tools=new List<Tool>{new Tool()};
        Getter(h,typeof(RaceProperties),"IsMechanoid","No");
        Getter(h,typeof(Verb),"BurstShotCount","BurstCount");
        Getter(h,typeof(IntVec3),"AngleFlat","Angle");
        Getter(h,typeof(Thing),"Spawned","Spawned"); Getter(h,typeof(Thing),"Map","MapValue");

        Getter(h,typeof(Thing),"DrawPos","Vector"); Getter(h,typeof(Pawn),"DrawPos","Vector");
        Getter(h,typeof(Verb),"EquipmentSource","Equip");
        Getter(h,typeof(Pawn_EquipmentTracker),"Primary","Equip");
        Prefix(h,typeof(PawnCapacitiesHandler),"GetLevel","Level"); Prefix(h,typeof(PawnCapacitiesHandler),"CapableOf","Capable");
        Prefix(h,typeof(RestUtility),"Awake","Awake");
        Getter(h,typeof(SkillRecord),"Aptitude","Aptitude");
        Prefix(h,typeof(StatExtension),"GetStatValue","Stat",typeof(Thing),typeof(StatDef),typeof(bool),typeof(int));
        Prefix(h,typeof(GenHostility),"HostileTo","Hostile",typeof(Thing),typeof(Thing));
        Prefix(h,typeof(GenGrid),"InBounds","Yes",typeof(IntVec3),typeof(Map));
        Prefix(h,typeof(ThingGrid),"ThingsListAtFast","Grid",typeof(IntVec3));
        Prefix(h,typeof(VerbUtility),"ThingsToHit","Things");
        Prefix(h,typeof(Gm21Melee).Assembly.GetType("Grandmaster21.Gm21Reach"),"CanDashTo","Route");
        Prefix(h,typeof(Rand),"Range","Range",typeof(int),typeof(int)); Prefix(h,typeof(Rand),"Chance","Chance");
        Prefix(h,typeof(Verb_ShootBeam),"CalculatePath","Skip");
        Getter(h,typeof(Verb_ShootBeam),"InterpolatedPosition","Vector");
        Prefix(h,typeof(Verb_ShootBeam),"TryGetHitCell","HitCell");
        Prefix(h,typeof(Verb_ShootBeam),"GetBeamHitNeighbourCells","Neighbours");
        Prefix(h,typeof(Verb),"TryFindShootLineFromTo","Line"); Prefix(h,typeof(Verb),"Available","Yes");
        Getter(h,typeof(Find),"TickManager","TickManager");
        Prefix(h,typeof(GenSight),"LastPointOnLineOfSight","Last");
        Prefix(h,typeof(Thing),"TakeDamage","TakeDamage");
        Prefix(h,typeof(DamageWorker.DamageResult),"AssociateWithLog","Skip");
        foreach(var c in typeof(BattleLogEntry_RangedImpact).GetConstructors()) h.Patch(c,prefix:Hook("Skip"));
        Prefix(h,typeof(FireUtility),"CanEverAttachFire","No"); Prefix(h,typeof(FireUtility),"TryStartFireIn","Fire");
        Prefix(h,typeof(MoteMaker),"ThrowText","Text",typeof(Vector3),typeof(Map),typeof(string),typeof(Color),typeof(float));
        Getter(h,typeof(StunHandler),"Hypnotized","No");
        Prefix(h,typeof(StunHandler),"CanBeStunnedByDamage","Immunity");
        Prefix(h,typeof(StunHandler),"CanAdaptToDamage","No");
        Prefix(h,typeof(StunHandler),"StunFor","StunAttempt");
        h.Patch(AccessTools.Method(typeof(Verb_ShootBeam),"TryCastShot"),prefix:Hook("ShotCount"));
    }
    static Pawn Pawn(int level, int x)
    {
        var p=Empty<Pawn>(); p.thingIDNumber=nextId++; p.def=Empty<ThingDef>(); p.def.race=Empty<RaceProperties>();
        p.skills=Empty<Pawn_SkillTracker>(); p.skills.skills=new List<SkillRecord>{new SkillRecord{def=SkillDefOf.Melee,levelInt=level}};
        p.health=Empty<Pawn_HealthTracker>(); Set(p.health,"pawn",p); Set(p.health,"healthState",PawnHealthState.Mobile);
        p.health.capacities=Empty<PawnCapacitiesHandler>(); Set(p.health.capacities,"pawn",p); p.equipment=Empty<Pawn_EquipmentTracker>();
        p.stances=Empty<Pawn_StanceTracker>(); p.stances.stunner=new StunHandler(p);
        positions[p]=new IntVec3(x,0,10); Set(p,"positionInt",positions[p]); return p;
    }
    static FixtureBeam Setup(bool success, bool hasGuardian=true)
    {
        occupants.Clear(); contacts.Clear(); positions.Clear();
        throwShot=throwStun=throwFeedback=throwSelect=throwSelectAlways=throwRoll=reenterSelect=false; burstCount=4; reenterPhase=null; awake=capable=clearRoute=hostile=true; manipulation=consciousness=1f; aptitude=0; immune=false;
        manipulationOf.Clear(); reentries=0; lastChance=0f; activeVerb=null;
        victim=Pawn(0,10); guardian=Pawn(21,11); attacker=Pawn(0,20);
        var faction=Empty<Faction>(); Set(victim,"factionInt",faction); Set(guardian,"factionInt",faction);
        if(hasGuardian) occupants.Add(guardian);
        occupants.Add(victim); contacts.Add(victim);
        var v=new FixtureBeam(); v.caster=attacker;
        v.verbProps=new VerbProperties{burstShotCount=4,ticksBetweenBurstShots=1,beamHitsNeighborCells=true,
            beamDamageDef=new DamageDef{defName="FixtureBeam",harmsHealth=true,defaultDamage=10},
            beamChanceToStartFire=1f,beamSetsGroundOnFire=true,muzzleFlashScale=0f};
        Set(v,"currentTarget",new LocalTargetInfo(victim));
        rolls=damageCalls=fires=stuns=feedback=shots=0; damageTotal=0; rollSuccess=success;
        Gm21Melee.BeamParryEnabled=true; return v;
    }
    static void Next(Verb v) { AccessTools.Method(typeof(Verb),"TryCastNextBurstShot").Invoke(v,null); }
    static void Complete(Verb v) { for(int i=0;i<8 && v.Bursting;i++) Next(v); }
    static float ChanceFor(Pawn p) { return (float)Call("BeamParryChance",p); }
    // The live transient Attack of a bursting verb (null once the burst completed or was reset).
    static bool Flag(Verb v, string field) { object a=Call("Find",v); return a!=null && (bool)AccessTools.Field(a.GetType(),field).GetValue(a); }

    static void Integration()
    {

        var v=Setup(true); v.WarmupComplete();
        Check("success: exactly one roll",rolls==1);
        Check("success: real ApplyDamage suppressed",damageCalls==0 && damageTotal==0);
        Check("success: current and neighbour ground fire suppressed",fires==0);
        Check("success: real burst ends",!v.Bursting);
        Check("success: real normal stun is 120 ticks",stuns==1 && (int)AccessTools.Field(typeof(StunHandler),"stunTicksLeft").GetValue(attacker.stances.stunner)==120);
        Check("success: one localized feedback",feedback==1);
        Check("success: transient entry removed",Call("Find",v)==null);
        for(int i=0;i<8;i++) v.VerbTick();
        Check("success: later ticks neither shoot nor roll",shots==1 && rolls==1 && damageCalls==0);

        v=Setup(false); v.WarmupComplete();
        Check("failure: first shot stays active",v.Bursting && rolls==1 && damageCalls==3);
        rollSuccess=true; Complete(v);
        Check("failure: no retry despite later guaranteed success",rolls==1 && stuns==0);
        Check("failure: vanilla six contacts (neighbours deduplicated across burst) at original factors",damageCalls==6 && damageTotal==50f);
        Check("failure: vanilla fire unchanged",fires==12);
        Check("failure: normal completion removes state",!v.Bursting && Call("Find",v)==null);
        v.WarmupComplete(); Check("next attack gets a fresh successful attempt",rolls==2 && !v.Bursting && stuns==1);

        v=Setup(true); immune=true; v.WarmupComplete();
        Check("immune: attack blocked and ended",damageCalls==0 && !v.Bursting && rolls==1);
        Check("immune: no forced StunFor and no damage reflection",stuns==0 && !attacker.stances.stunner.Stunned);

        v=Setup(true); victim=guardian; contacts.Clear(); contacts.Add(victim); Set(v,"currentTarget",new LocalTargetInfo(victim)); v.WarmupComplete();
        Check("self defence",rolls==1 && damageCalls==0 && !v.Bursting);
        v=Setup(true); hostile=false; Set(v,"currentTarget",new LocalTargetInfo(new IntVec3(10,0,10))); v.WarmupComplete();
        Check("friendly accidental fire defended without counter-stun",rolls==1 && damageCalls==0 && stuns==0);
        v=Setup(true); hostile=false; v.WarmupComplete();
        Check("deliberate targeting of protected ally preserves hostile intent",stuns==1);
        v=Setup(true); v.verbProps.beamDamageDef.harmsHealth=false; v.WarmupComplete(); Complete(v);
        Check("harmless beam never parried",rolls==0 && stuns==0 && damageCalls==6);
        v=Setup(true); v.verbProps.beamDamageDef=null; v.WarmupComplete(); Complete(v);
        Check("visual-only beam never parried",rolls==0 && stuns==0 && damageCalls==0);
        v=Setup(false); v.WarmupComplete(); v.Reset(); Check("external Reset clears attempt",Call("Find",v)==null);
        Set(v,"currentTarget",new LocalTargetInfo(victim)); rollSuccess=true; v.WarmupComplete(); Check("reset does not poison next cast",rolls==2 && !v.Bursting);
        v=Setup(true); Set(v,"state",VerbState.Bursting); Set(v,"burstShotsLeft",2); Next(v); Complete(v);
        Check("loaded mid-burst with no transient entry fails open",rolls==0 && damageCalls==4);
        v=Setup(true); Gm21Melee.BeamParryEnabled=false; v.WarmupComplete(); Complete(v);
        Check("feature disabled leaves vanilla burst intact",rolls==0 && damageCalls==6);
        v=Setup(true); throwShot=true; bool threw=false;
        try { v.WarmupComplete(); } catch(InvalidOperationException) { threw=true; }
        Check("exception preserved and transient attack removed",threw && Call("Find",v)==null);
        throwShot=false; v.WarmupComplete();
        Check("exception finalizers restore context for next attack",rolls==1 && !v.Bursting);
        v=Setup(true); throwStun=throwFeedback=true; v.WarmupComplete();
        Check("stun and cosmetic exceptions cannot undo defence",damageCalls==0 && !v.Bursting && rolls==1);
        v=Setup(true); var weaker=Pawn(21,12); Set(weaker,"factionInt",guardian.Faction); occupants.Add(weaker);
        v.WarmupComplete(); Check("multiple guardians still produce only one roll",rolls==1 && damageCalls==0);
        v=Setup(true); int completed=0;
        v.castCompleteCallback=()=>{ completed++; v.castCompleteCallback=null; rollSuccess=false; v.WarmupComplete(); };
        v.WarmupComplete();
        Check("reentrant callback starts independent attack on same verb",completed==1 && rolls==2 && v.Bursting && Call("Find",v)!=null);
        rollSuccess=true; Complete(v);
        Check("old completion does not erase nested failure decision",rolls==2 && damageCalls==6 && Call("Find",v)==null);
    }
    // One beam attack = one parry attempt. An attempt exists only once an eligible Guardian has been
    // selected and is about to roll: no Guardian, no attempt; a Guardian's roll is final for the attack.
    static void Pair(int x, out Pawn protectedPawn, out Pawn protector)
    {
        protectedPawn=Pawn(0,x); protector=Pawn(21,x+1);
        Set(protectedPawn,"factionInt",guardian.Faction); Set(protector,"factionInt",guardian.Faction);
        occupants.Add(protectedPawn); occupants.Add(protector);
    }
    static void AttemptConsumption()
    {
        // 1. Unprotected contact first, protected contact later in the SAME attack (replaces the old
        //    "first pawn contact spends the attempt even with no Guardian" expectation).
        var v=Setup(true,false); v.WarmupComplete();
        Check("1 unprotected first contact: vanilla damage, no roll, no stun",rolls==0 && damageCalls==3 && stuns==0);
        Check("1 unprotected first contact: attempt not evaluated and still available",v.Bursting && Call("Find",v)!=null && !Flag(v,"Evaluated") && !Flag(v,"Defended"));
        occupants.Add(guardian); Complete(v);
        Check("1 protected later contact: exactly one roll, defended, later shots suppressed",rolls==1 && damageCalls==3 && shots==2 && !v.Bursting);
        Check("1 protected later contact: normal 120-tick stun attempted once",stuns==1 && (int)AccessTools.Field(typeof(StunHandler),"stunTicksLeft").GetValue(attacker.stances.stunner)==120 && feedback==1);
        for(int i=0;i<8;i++) v.VerbTick();
        Check("1 success final: no later shot or roll, state released",shots==2 && rolls==1 && damageCalls==3 && Call("Find",v)==null);
        v=Setup(false,false); v.WarmupComplete(); occupants.Add(guardian); Next(v);
        Check("1 late Guardian failing its one roll: vanilla, attempt now evaluated",rolls==1 && damageCalls==4 && stuns==0 && v.Bursting && Flag(v,"Evaluated") && !Flag(v,"Defended"));
        rollSuccess=true; Complete(v);
        Check("1 late failed roll stays final: no retry, unchanged vanilla burst",rolls==1 && stuns==0 && damageCalls==6 && fires==12 && !v.Bursting);

        // 2. A FAILED real attempt stays final, even against another protected victim and Guardian.
        v=Setup(false); v.WarmupComplete();
        Check("2 first eligible Guardian fails: one roll, vanilla damage, attempt evaluated",rolls==1 && damageCalls==3 && v.Bursting && Flag(v,"Evaluated") && !Flag(v,"Defended"));
        Pawn other, otherGuardian; Pair(30,out other,out otherGuardian);
        contacts.Clear(); contacts.Add(other); victim=other; rollSuccess=true; Next(v);
        Check("2 another protected victim with another Guardian: no second roll, vanilla",rolls==1 && damageCalls==4 && stuns==0 && v.Bursting);
        Complete(v);
        Check("2 no retry for the rest of the burst despite guaranteed success",rolls==1 && damageCalls==6 && stuns==0 && !v.Bursting && Call("Find",v)==null);
        v=Setup(true); v.WarmupComplete(); Check("2 next attack is a fresh attempt",rolls==1 && damageCalls==0 && stuns==1);

        // 3. Several contacts with no eligible Guardian never spend the attempt; the first later
        //    contact that has one gets the attack's single roll. Three flavours of "no Guardian":
        //    unprotected pawns far from any Guardian, and a protected pawn whose Guardian cannot act.
        v=Setup(true); burstCount=6;
        Pawn stray=Pawn(0,30), stray2=Pawn(0,40), protectedOne=victim;
        contacts.Clear(); contacts.Add(stray); victim=stray; v.WarmupComplete();
        Check("3 unprotected pawn (three contacts): vanilla, no roll, not evaluated",rolls==0 && damageCalls==3 && !Flag(v,"Evaluated"));
        contacts[0]=stray2; victim=stray2; Next(v);
        Check("3 second unprotected pawn: vanilla, no roll, not evaluated",rolls==0 && damageCalls==4 && !Flag(v,"Evaluated"));
        contacts[0]=protectedOne; victim=protectedOne; clearRoute=false; Next(v);
        Check("3 protected pawn but Guardian cannot reach: vanilla, no roll, not evaluated",rolls==0 && damageCalls==5 && !Flag(v,"Evaluated"));
        clearRoute=true; Next(v);
        Check("3 first contact with an eligible Guardian: exactly one roll, defended, burst ended early",rolls==1 && damageCalls==5 && stuns==1 && shots==4 && !v.Bursting);
        v=Setup(false); burstCount=6; stray=Pawn(0,30); protectedOne=victim; contacts.Clear(); contacts.Add(stray); victim=stray; v.WarmupComplete();
        contacts[0]=protectedOne; victim=protectedOne; clearRoute=false; Next(v); clearRoute=true; Next(v);
        Check("3 same route, forced failure: one roll, vanilla, evaluated",rolls==1 && damageCalls==5 && stuns==0 && v.Bursting && Flag(v,"Evaluated"));
        rollSuccess=true; Complete(v);
        Check("3 forced failure stays final: no retry through the last shots",rolls==1 && damageCalls==8 && stuns==0 && !v.Bursting);

        // Non-pawn, zero-factor and dead-end contacts before any Guardian also leave the attempt open.
        v=Setup(true); object attack=Call("Begin",v), scope=Call("Enter",v);
        Check("3 object contact is vanilla and spends nothing",(bool)Call("ShouldDamage",v,Empty<Thing>(),1f) && rolls==0 && !Flag(v,"Evaluated"));
        Check("3 zero damage factor is vanilla and spends nothing",(bool)Call("ShouldDamage",v,victim,0f) && rolls==0 && !Flag(v,"Evaluated"));
        Check("3 first real Guardian contact: one roll, defended",!(bool)Call("ShouldDamage",v,victim,1f) && rolls==1 && Flag(v,"Evaluated") && Flag(v,"Defended"));
        Check("3 defended attack never rolls again",!(bool)Call("ShouldDamage",v,victim,1f) && rolls==1);
        Call("Exit",scope); Call("End",v,attack);

        // 5. Several eligible Guardians at one contact: best chance wins, regardless of scan order, one roll.
        v=Setup(true); Pawn better=Pawn(21,9), best=Pawn(21,12);
        Set(better,"factionInt",guardian.Faction); Set(best,"factionInt",guardian.Faction);
        occupants.Add(better); occupants.Add(best); manipulationOf[better]=2f; manipulationOf[best]=3f;
        object[] pick={victim,attacker,0f};
        Check("5 best Guardian selected (scanned before and after weaker ones)",Call("SelectGuardian",pick)==best && (float)pick[2]==ChanceFor(best) && ChanceFor(best)>ChanceFor(better) && ChanceFor(better)>ChanceFor(guardian));
        v.WarmupComplete();
        Check("5 exactly one roll, made at the best Guardian's chance",rolls==1 && lastChance==ChanceFor(best) && damageCalls==0 && stuns==1);

        // 6. Exception paths. Failure before any Guardian exists spends nothing and fails open;
        //    failure after a Guardian is selected has already spent the attempt.
        v=Setup(true); throwSelect=true; int seen=selectionWarnings; v.WarmupComplete();
        Check("6 selection throws at first contact: contact proceeds vanilla, warns once",damageCalls==1 && selectionWarnings==seen+1);
        Check("6 selection failure spent nothing: the next contact gets the one roll and defends",rolls==1 && stuns==1 && !v.Bursting && shots==1);
        v=Setup(true); throwSelect=throwSelectAlways=true; seen=selectionWarnings; v.WarmupComplete(); Complete(v);
        Check("6 persistent selection failure: whole burst vanilla, no roll, no crash, warning not repeated",rolls==0 && stuns==0 && damageCalls==6 && !v.Bursting && Call("Find",v)==null && selectionWarnings==seen);
        v=Setup(true); v.WarmupComplete();
        Check("6 no state leaks into the next attack: fresh attempt succeeds",rolls==1 && damageCalls==0 && stuns==1);
        v=Setup(true); throwRoll=true; v.WarmupComplete(); throwRoll=false; rollSuccess=true; Complete(v);
        Check("6 roll throws after Guardian selected: attempt spent, fail open, no retry",rolls==1 && stuns==0 && damageCalls==6 && !v.Bursting);
        v=Setup(true); activeVerb=v; reenterSelect=true; v.WarmupComplete();
        Check("6 contact provoked inside Guardian selection cannot recurse or roll twice (success)",reentries==1 && rolls==1 && damageCalls==1 && stuns==1 && !v.Bursting);
        v=Setup(false); activeVerb=v; reenterSelect=true; v.WarmupComplete(); Complete(v);
        Check("6 contact provoked inside Guardian selection cannot roll twice (failure)",reentries==1 && rolls==1 && damageCalls==7 && stuns==0 && !v.Bursting);
        v=Setup(true); v.WarmupComplete(); Check("6 selection guard does not leak into the next attack",rolls==1 && !v.Bursting && stuns==1);

        // 7. The commitment is made after Guardian selection and BEFORE the RNG and every counter-effect:
        //    a beam contact provoked from inside any of them can neither roll again nor slip damage past a defence.
        v=Setup(false); activeVerb=v; reenterPhase="roll"; v.WarmupComplete(); Complete(v);
        Check("7 contact provoked inside a failing roll: vanilla, no second roll",reentries==1 && rolls==1 && damageCalls==7 && stuns==0 && !v.Bursting);
        v=Setup(true); activeVerb=v; reenterPhase="roll"; v.WarmupComplete();
        Check("7 contact provoked inside a succeeding roll: that contact vanilla, no second roll, then defended",reentries==1 && rolls==1 && damageCalls==1 && stuns==1 && !v.Bursting);
        v=Setup(true); activeVerb=v; reenterPhase="stun"; v.WarmupComplete();
        Check("7 contact provoked inside the counter-stun stays defended",reentries==1 && rolls==1 && damageCalls==0 && stuns==1 && !v.Bursting);
        v=Setup(true); activeVerb=v; reenterPhase="feedback"; v.WarmupComplete();
        Check("7 contact provoked inside the feedback stays defended",reentries==1 && rolls==1 && damageCalls==0 && feedback==1 && !v.Bursting);
    }
    static void Eligibility()
    {
        Setup(true);
        Check("stored 21 eligible",ChanceFor(guardian)>0f);
        guardian.skills.skills[0].levelInt=20; Check("stored 20 ineligible",ChanceFor(guardian)==0f);
        aptitude=1; Check("stored 20 plus aptitude ineligible",ChanceFor(guardian)==0f);
        guardian.skills.skills[0].levelInt=21; aptitude=-2; Check("stored 21 negative aptitude eligible",ChanceFor(guardian)>0f);
        awake=false; Check("sleeping ineligible",ChanceFor(guardian)==0f); awake=true;
        capable=false; Check("unconscious ineligible",ChanceFor(guardian)==0f); capable=true;
        Set(guardian.health,"healthState",PawnHealthState.Down); Check("downed ineligible",ChanceFor(guardian)==0f); Set(guardian.health,"healthState",PawnHealthState.Mobile);
        Set(guardian.stances.stunner,"stunTicksLeft",10); Check("stunned ineligible",ChanceFor(guardian)==0f); Set(guardian.stances.stunner,"stunTicksLeft",0);
        manipulation=0; Check("no manipulation ineligible",ChanceFor(guardian)==0f); manipulation=1;
        consciousness=0; Check("no consciousness ineligible",ChanceFor(guardian)==0f); consciousness=1;
        var save=weapon; weapon=null; Check("no implement ineligible",ChanceFor(guardian)==0f); weapon=save;
        manipulation=float.NaN; Check("nonfinite capacity fails open",ChanceFor(guardian)==0f); manipulation=1;
        float baseline=ChanceFor(guardian); manipulation=2; Check("precision raises chance",ChanceFor(guardian)>baseline); manipulation=1;
        clearRoute=false; object[] args={victim,attacker,0f}; Check("sealed route cannot protect ally",Call("SelectGuardian",args)==null); clearRoute=true;
        positions[guardian]=new IntVec3(14,0,10); Set(guardian,"positionInt",positions[guardian]); args=new object[]{victim,attacker,0f}; Check("outside existing radius cannot protect",Call("SelectGuardian",args)==null);
        positions[guardian]=new IntVec3(13,0,10); Set(guardian,"positionInt",positions[guardian]); args=new object[]{victim,attacker,0f}; Check("existing radius boundary protects",Call("SelectGuardian",args)==guardian);
        Set(victim,"factionInt",null); args=new object[]{victim,attacker,0f}; Check("neutral pawn not automatically protected",Call("SelectGuardian",args)==null);
    }
    static void Lifecycle()
    {
        var a=Setup(false); var b=new FixtureBeam();
        object first=Call("Begin",a), outer=Call("Enter",a); Set(first,"Evaluated",true);
        object second=Call("Begin",b), inner=Call("Enter",b); Set(second,"Defended",true);
        Check("nested verbs retain distinct outcomes",!(bool)Call("Blocked",a) && (bool)Call("Blocked",b));
        Call("Exit",inner); Check("nested scope restores outer context",!(bool)Call("Blocked",b) && !(bool)Call("Blocked",a));
        object replacement=Call("Begin",a); Call("End",a,first);
        Check("stale completion cannot remove replacement attack",ReferenceEquals(Call("Find",a),replacement));
        Call("Exit",outer); Call("End",a,replacement); Call("End",b,second);
        Check("all scoped state removed",Call("Find",a)==null && Call("Find",b)==null);
        var src=new List<CodeInstruction>{new CodeInstruction(System.Reflection.Emit.OpCodes.Ret)};
        bool rejected=false;
        try { AccessTools.Method(Patches,"EndDefendedShot").Invoke(null,new object[]{src}); } catch(TargetInvocationException) { rejected=true; }
        Check("changed shot IL rejected rather than partially patched",rejected);
    }
    public static IEnumerable<CodeInstruction> RemoveShotCall(IEnumerable<CodeInstruction> input)
    {
        var shot=AccessTools.Method(typeof(Verb),"TryCastShot");
        foreach(var i in input)
        {
            if(Equals(i.operand,shot))
            {
                var pop=new CodeInstruction(System.Reflection.Emit.OpCodes.Pop); pop.labels.AddRange(i.labels);
                yield return pop; yield return new CodeInstruction(System.Reflection.Emit.OpCodes.Ldc_I4_0);
            }
            else yield return i;
        }
    }
    static void MissingPipeline()
    {
        new Harmony("Grandmaster21.BeamParry").UnpatchAll("Grandmaster21.BeamParry");
        var h=new Harmony("Grandmaster21.BeamParry.Tests.ChangedPipeline");
        h.Patch(AccessTools.Method(typeof(Verb),"TryCastNextBurstShot"),transpiler:Hook("RemoveShotCall"));
        Gm21Melee.ProjectileDefenceEnabled=true;
        Gm21Melee.BeamParryEnabled=(bool)AccessTools.Method(Patches,"Apply").Invoke(null,new object[]{h});
        Check("changed pipeline disables only Beam Parry",!Gm21Melee.BeamParryEnabled && Gm21Melee.ProjectileDefenceEnabled);
        Check("partial beam binding rolled back",!Harmony.GetAllPatchedMethods().Any(m=>Harmony.GetPatchInfo(m).Owners.Contains("Grandmaster21.BeamParry")));
        Check("independent environment patches retained",Harmony.GetPatchInfo(AccessTools.Method(typeof(Thing),"TakeDamage")).Owners.Contains("Grandmaster21.BeamParry.Tests.Environment"));
        h.UnpatchAll("Grandmaster21.BeamParry.Tests.ChangedPipeline");
    }
    static void Audit(string dll, string data)
    {
        using(var module=ModuleDefinition.ReadModule(dll))
        {
            var beam=module.GetType("Verse.Verb_ShootBeam"); var verb=module.GetType("Verse.Verb");
            Func<TypeDefinition,string,MethodDefinition> m=(t,n)=>t.Methods.Single(x=>x.Name==n);
            Func<MethodDefinition,string,bool> calls=(x,n)=>x.Body.Instructions.Any(i=>i.Operand is MethodReference && ((MethodReference)i.Operand).FullName.Contains(n));
            Check("real Assembly-CSharp owns beam",beam!=null && beam.BaseType.FullName=="Verse.Verb");
            Check("Warmup starts burst and calls Next",m(beam,"WarmupComplete").Body.Instructions.Any(i=>i.Operand is FieldReference && ((FieldReference)i.Operand).Name=="burstShotsLeft") && calls(m(beam,"WarmupComplete"),"TryCastNextBurstShot"));
            Check("VerbTick repeats Next and BurstingTick",calls(m(verb,"VerbTick"),"TryCastNextBurstShot") && calls(m(verb,"VerbTick"),"BurstingTick"));
            Check("VerbTick checks stun and resets",calls(m(verb,"VerbTick"),"get_Stunned") && calls(m(verb,"VerbTick"),"::Reset"));
            Check("Next uses virtual shot result",calls(m(verb,"TryCastNextBurstShot"),"::TryCastShot"));
            Check("shot hits primary and neighbouring cells",m(beam,"TryCastShot").Body.Instructions.Count(i=>i.Operand is MethodReference && ((MethodReference)i.Operand).Name=="HitCell")==2);
            Check("HitCell resolves Thing then calls ApplyDamage",calls(m(beam,"HitCell"),"ThingsToHit") && calls(m(beam,"HitCell"),"ApplyDamage"));
            Check("ApplyDamage calls actual Thing.TakeDamage",calls(m(beam,"ApplyDamage"),"Verse.Thing::TakeDamage"));
            Check("damage retrieves caster and currentTarget",new[]{"caster","currentTarget"}.All(n=>m(beam,"ApplyDamage").Body.Instructions.Any(i=>i.Operand is FieldReference && ((FieldReference)i.Operand).Name==n)));
            Check("BurstingTick is visual/audio only",!calls(m(beam,"BurstingTick"),"ApplyDamage") && !calls(m(beam,"BurstingTick"),"HitCell") && calls(m(beam,"BurstingTick"),"Maintain"));
            Check("ApplyDamage and HitCell nonvirtual chokepoints",!m(beam,"ApplyDamage").IsVirtual && !m(beam,"HitCell").IsVirtual);
            Check("beam has no custom Reset",!beam.Methods.Any(x=>x.Name=="Reset"));
            Check("stun pipeline applies immunity before StunFor",calls(m(module.GetType("RimWorld.StunHandler"),"Notify_DamageApplied"),"CanBeStunnedByDamage") && calls(m(module.GetType("RimWorld.StunHandler"),"Notify_DamageApplied"),"StunFor"));
        }
        using(var module=ModuleDefinition.ReadModule(typeof(Gm21Melee).Assembly.Location))
        {
            var methods=module.Types.Where(t=>t.Name.StartsWith("Gm21BeamParry")).SelectMany(t=>t.Methods).Where(m=>m.HasBody);
            Check("production Beam Parry never calls TakeDamage or creates Projectile",!methods.SelectMany(m=>m.Body.Instructions).Any(i=>i.Operand is MethodReference && (((MethodReference)i.Operand).Name=="TakeDamage" || ((MethodReference)i.Operand).DeclaringType.Name=="Projectile")));
            Check("production has no weapon defName reads",!methods.SelectMany(m=>m.Body.Instructions).Any(i=>i.Operand is FieldReference && ((FieldReference)i.Operand).Name=="defName"));
        }
        if(string.IsNullOrEmpty(data)) Console.WriteLine("BLOCKED Gun_BeamGraser XML binding: supplied assemblies do not include the game's Data folder; rerun with fourth argument <RimWorld/Data>.");
        else
        {
            var defs=Directory.GetFiles(data,"*.xml",SearchOption.AllDirectories).SelectMany(p=>{try{return XDocument.Load(p).Descendants("ThingDef").ToArray();}catch{return new XElement[0];}});
            var graser=defs.FirstOrDefault(x=>(string)x.Element("defName")=="Gun_BeamGraser");
            Check("installed Gun_BeamGraser uses audited pipeline",graser!=null && graser.Descendants("verbClass").Any(x=>x.Value=="Verb_ShootBeam" || x.Value=="Verse.Verb_ShootBeam"));
        }
    }
    static int Main(string[] args)
    {
        try
        {
            Audit(args[0],args.Length>1?args[1]:null);
            var h=new Harmony("Grandmaster21.BeamParry.Tests.Environment"); Environment(h);
            bool applied=(bool)AccessTools.Method(Patches,"Apply").Invoke(null,new object[]{h});
            Check("actual production Harmony group binds real methods",applied);
            if(applied) { Integration(); AttemptConsumption(); Eligibility(); Lifecycle(); MissingPipeline(); }
        }
        catch(Exception e) { Console.WriteLine("FAIL unhandled fixture: "+e); fail++; }
        Console.WriteLine("Beam Parry: "+pass+" PASS, "+fail+" FAIL"); return fail==0?0:1;
    }
}
