// Real-assembly headless fixtures: execute hit gating/roll/healing policy and inspect native IL.
// Captured eligibility fixtures do not simulate a live map; recipient safety and hook wiring are IL checks.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml.Linq;
using Grandmaster21.Transcendent;
using Mono.Cecil;
using Mono.Cecil.Cil;
using RimWorld;
using Verse;

internal static partial class TranscendentChecks
{
    static Pawn CombatPawn(PawnHealthState state)
    {
        var pawn = (Pawn)FormatterServices.GetUninitializedObject(typeof(Pawn));
        pawn.health = (Pawn_HealthTracker)FormatterServices.GetUninitializedObject(typeof(Pawn_HealthTracker));
        Set(pawn.health, "healthState", state);
        return pawn;
    }
    static object HitFixture(CompArtifact artifact, Pawn wielder, Pawn primary, bool valid)
    {
        object hit = Activator.CreateInstance(Feature("ArtifactCombat").GetNestedType("Hit", Any), true);
        Set(hit, "artifact", artifact); Set(hit, "wielder", wielder); Set(hit, "primary", primary);
        Set(hit, "validBeforeDamage", valid); Set(hit, "impactCell", new IntVec3(11, 0, 23));
        return hit;
    }
    static bool Consume(object hit, Pawn target, DamageInfo info, float damage)
    { return (bool)Invoke("ArtifactCombat", "ConsumeOpportunity", hit, target, info, damage); }
    static IEnumerable<TypeDefinition> NestedTypes(TypeDefinition type)
    { return new[] { type }.Concat(type.NestedTypes.SelectMany(NestedTypes)); }
    static void SurgicalChecks(string dll, string root)
    {
        ThingDef weapon = Def("SurgicalWeapon"); weapon.tools = new List<Tool> { new Tool() };
        var parent = (ThingWithComps)FormatterServices.GetUninitializedObject(typeof(ThingWithComps)); parent.def = weapon;
        var artifact = new CompArtifact { parent = parent };
        Set(artifact, "tier", Tier("Divine")); Set(artifact, "phenomenon", Phenomenon("VampiricStrike"));
        Pawn wielder = CombatPawn(PawnHealthState.Mobile), primary = CombatPawn(PawnHealthState.Mobile);
        DamageInfo info = new DamageInfo(null, 10f, instigator: wielder, weapon: weapon);
        foreach (PawnHealthState state in new[] { PawnHealthState.Mobile, PawnHealthState.Down, PawnHealthState.Dead })
        {
            Set(primary.health, "healthState", PawnHealthState.Mobile);
            object hit = HitFixture(artifact, wielder, primary, true);
            Set(primary.health, "healthState", state);
            Check("prevalid primary becomes " + state + ": opportunity survives", Consume(hit, primary, info, 10));
            Check("post-" + state + " extra packet cannot retry", !Consume(hit, primary, info, 10));
            Check("impact cell remains captured after " + state, ((IntVec3)Get(hit, "impactCell")) == new IntVec3(11, 0, 23));
        }
        Set(primary.health, "healthState", PawnHealthState.Mobile);
        foreach (string invalid in new[] { "already downed", "already dead", "friendly", "colony prisoner", "other invalid target" })
        {
            // Models the false result captured by SafeTarget, not a live faction/health transition.
            object hit = HitFixture(artifact, wielder, primary, false);
            Check("captured " + invalid + " never becomes eligible from damage", !Consume(hit, primary, info, 10) && !(bool)Get(hit, "attempted"));
        }
        foreach (float damage in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            object hit = HitFixture(artifact, wielder, primary, true);
            Check("invalid actual damage " + damage + " does not consume", !Consume(hit, primary, info, damage) && !(bool)Get(hit, "attempted"));
            Check("later actual damage after " + damage + " can consume once", Consume(hit, primary, info, 1f) && !Consume(hit, primary, info, 1f));
        }
        object scope = HitFixture(artifact, wielder, primary, true);
        Check("secondary callback cannot steal original primary opportunity", !Consume(scope, wielder, info, 10) && Consume(scope, primary, info, 10));
        scope = HitFixture(artifact, wielder, primary, true);
        Check("wrong instigator rejected", !Consume(scope, primary, new DamageInfo(null, 10, instigator: primary, weapon: weapon), 10));
        Check("wrong weapon Def rejected", !Consume(scope, primary, new DamageInfo(null, 10, instigator: wielder, weapon: Def("OtherWeapon")), 10));
        Check("generated damage without weapon provenance rejected", !Consume(scope, primary, new DamageInfo(null, 10, instigator: wielder), 10));
        Check("unrelated callbacks leave primary available", Consume(scope, primary, info, 10));
        Check("no artifact scope ignores ordinary damage", !Consume(null, primary, info, 10));
        foreach (string path in new[] { "melee multi-damage", "bullet ExtraDamages" })
        {
            scope = HitFixture(artifact, wielder, primary, true); int accepted = 0;
            for (int i = 0; i < 6; i++) if (Consume(scope, primary, info, 10)) accepted++;
            Check(path + " consumes exactly one opportunity", accepted == 1);
        }
        FieldInfo depth = Feature("ArtifactEffects").GetField("effectDepth", Any);
        scope = HitFixture(artifact, wielder, primary, true); depth.SetValue(null, 1);
        try
        {
            Check("generated effect recursion blocked before consuming", !Consume(scope, primary, info, 10) && !(bool)Get(scope, "attempted"));
            Check("captured dispatch recursion short circuits", !(bool)Invoke("ArtifactEffects", "TryTriggerCaptured", scope, 10f, false));
        }
        finally { depth.SetValue(null, 0); }
        Check("scope remains eligible after recursion unwinds", Consume(scope, primary, info, 10));
        foreach (bool success in new[] { false, true })
        {
            int seed = Enumerable.Range(0, 1000).First(s => ((double)Invoke("ArtifactRolls", "Unit", s, 100) < .3) == success);
            Set(artifact, "phenomenonSeed", seed); Set(artifact, "procCounter", 0); Set(artifact, "nextProcTick", 0);
            scope = HitFixture(artifact, wielder, primary, true);
            Check("probability " + success + " starts with one consumed opportunity", Consume(scope, primary, info, 10));
            Check("deterministic probability result " + success, (bool)Invoke("ArtifactEffects", "TryRoll", artifact, 1000, false) == success);
            Check("probability " + success + " increments counter once", (int)Get(artifact, "procCounter") == 1);
            Check("probability " + success + " cooldown result", (int)Get(artifact, "nextProcTick") == (success ? 1180 : 0));
            Check("probability " + success + " cannot retry on later packet", !Consume(scope, primary, info, 10) && (int)Get(artifact, "procCounter") == 1);
        }
        scope = HitFixture(artifact, wielder, primary, true);
        Check("cooldown still consumes attack opportunity", Consume(scope, primary, info, 10));
        Check("cooldown prevents roll/counter advancement", !(bool)Invoke("ArtifactEffects", "TryRoll", artifact, 1179, false) && (int)Get(artifact, "procCounter") == 1);
        Check("cooldown expiry within same attack cannot retry", !Consume(scope, primary, info, 10));
        int boundarySeed = Enumerable.Range(0, 1000).First(s => (double)Invoke("ArtifactRolls", "Unit", s, 101) < .3);
        Set(artifact, "phenomenonSeed", boundarySeed);
        Check("new attack may roll at exact cooldown boundary", (bool)Invoke("ArtifactEffects", "TryRoll", artifact, 1180, false));
        Check("DEV direct roll bypasses cooldown and probability", (bool)Invoke("ArtifactEffects", "TryRoll", artifact, 1181, true) && (int)Get(artifact, "procCounter") == 3);
        foreach (PawnHealthState state in new[] { PawnHealthState.Down, PawnHealthState.Dead })
        {
            Set(primary.health, "healthState", state);
            foreach (float actual in new[] { -1f, 0f, float.NaN, float.PositiveInfinity, float.NegativeInfinity, 1f, 10f, 30f, float.MaxValue })
            {
                float expected = actual > 0 && !float.IsInfinity(actual) && !float.IsNaN(actual) ? Math.Min(8f, actual * .35f) : 0;
                Check(state + " Vampire uses actual trigger " + actual, Math.Abs((float)Invoke("ArtifactEffects", "VampireBudget", wielder, primary, 3f, actual) - expected) < .0001f);
            }
        }
        Set(primary.health, "healthState", PawnHealthState.Mobile);
        Check("unavailable standing Vampire target cannot heal from trigger", (float)Invoke("ArtifactEffects", "VampireBudget", null, primary, 3f, 100f) == 0);
        Check("missing Vampire target cannot heal", (float)Invoke("ArtifactEffects", "VampireBudget", wielder, null, 3f, 100f) == 0);
        var current = Feature("ArtifactCombat").GetField("current", Any);
        object outer = HitFixture(artifact, wielder, primary, true), inner = HitFixture(artifact, wielder, primary, true);
        current.SetValue(null, inner); var originalException = new InvalidOperationException("foreign mod failure");
        Check("finalizer returns identical foreign exception", ReferenceEquals(Invoke("ArtifactCombat", "Restore", originalException, outer), originalException));
        Check("throwing nested hit restores outer state", ReferenceEquals(current.GetValue(null), outer));
        Check("successful outer finalizer returns null", Invoke("ArtifactCombat", "Restore", null, null) == null && current.GetValue(null) == null);
        Check("hit scope thread local", current.IsDefined(typeof(ThreadStaticAttribute), false));
        SurgicalXmlAndIl(dll, root);
    }
    static void SurgicalXmlAndIl(string dll, string root)
    {
        var recipes = XDocument.Load(Path.Combine(root, "Defs/Transcendent/Progression.xml")).Root.Elements("RecipeDef").ToList();
        int[] work = { 18000, 45000, 90000 }, skills = { 12, 15, 18 };
        string[] tiers = { "Magical", "Mythical", "Divine" };
        for (int i = 0; i < 3; i++)
        {
            var r = recipes.Single(x => x.Element("researchPrerequisite").Value == "GM21_" + tiers[i] + "Craftsmanship");
            Check(tiers[i] + " ordinary unfinished component", r.Element("unfinishedThingDef").Value == "UnfinishedComponent");
            Check(tiers[i] + " native 1.6 general labor stat", r.Element("workSpeedStat").Value == "GeneralLaborSpeed" && typeof(StatDefOf).GetField("GeneralLaborSpeed") != null);
            Check(tiers[i] + " original long work and sub-GM Crafting", int.Parse(r.Element("workAmount").Value) == work[i] && int.Parse(r.Element("skillRequirements").Element("Crafting").Value) == skills[i] && r.Element("workSkill").Value == "Crafting");
            Check(tiers[i] + " vanilla job only and one product", r.Element("workerClass") == null && r.Element("products").Elements().Single().Value == "1");
        }
        var unfinished = Def("UnfinishedFixture"); unfinished.thingClass = typeof(UnfinishedThing);
        unfinished.comps.Add(new CompProperties(typeof(CompQuality)));
        Check("unfinished object excluded even with quality", !(bool)Invoke("TranscendentRecipes", "CompatibleItemClass", unfinished.thingClass));
        using (var game = AssemblyDefinition.ReadAssembly(typeof(Pawn).Assembly.Location))
        using (var mod = AssemblyDefinition.ReadAssembly(dll))
        {
            Func<AssemblyDefinition, string, TypeDefinition> type = (a, n) => a.MainModule.Types.Single(t => t.Name == n);
            Func<string, string, MethodDefinition> m = (t, n) => type(mod, t).Methods.Single(x => x.Name == n);
            Func<string, string, MethodDefinition> g = (t, n) => type(game, t).Methods.Single(x => x.Name == n);
            Check("native recipe selects UFT by configured Def", g("RecipeDef", "get_UsesUnfinishedThing").Body.Instructions.Any(x => (x.Operand as FieldReference)?.Name == "unfinishedThingDef"));
            Check("native bill factory uses Bill_ProductionWithUft", Calls(g("BillUtility", "MakeNewBill")).Any(x => x.DeclaringType.Name == "Bill_ProductionWithUft" && x.Name == ".ctor"));
            var nativeToils = NestedTypes(type(game, "Toils_Recipe")).SelectMany(t => t.Methods).Where(x => x.HasBody).ToList();
            Check("native work restores and updates UFT workLeft", nativeToils.Any(x => x.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldfld && (i.Operand as FieldReference)?.FullName == "System.Single Verse.UnfinishedThing::workLeft")) && nativeToils.Any(x => x.Body.Instructions.Any(i => i.OpCode == OpCodes.Stfld && (i.Operand as FieldReference)?.FullName == "System.Single Verse.UnfinishedThing::workLeft")));
            Check("native work consumes configured work-speed stat", nativeToils.Any(x => x.Body.Instructions.Any(i => (i.Operand as FieldReference)?.Name == "workSpeedStat") && Calls(x).Any(c => c.Name == "GetStatValue")));
            Check("native UFT saves work and ingredients", new[] { "workLeft", "ingredients", "creator", "bill" }.All(n => g("UnfinishedThing", "ExposeData").Body.Instructions.Any(i => Equals(i.Operand, n))));
            Check("native resume explicitly creator locked", Calls(g("WorkGiver_DoBill", "FinishUftJob")).Any(c => c.Name == "get_Creator"));
            foreach (string prefix in new[] { "BulletPrefix", "MeleePrefix" })
            {
                string native = prefix == "BulletPrefix" ? "Bullet" : "Verb_MeleeAttackDamage";
                string target = prefix == "BulletPrefix" ? "Impact" : "ApplyMeleeDamageToTarget";
                var nativeArg = g(native, target).Parameters.First();
                Check(prefix + " binds real target argument name and type", m("ArtifactCombat", prefix).Parameters.Any(p => p.Name == nativeArg.Name && p.ParameterType.FullName == nativeArg.ParameterType.FullName));
                Check(prefix + " enters pre-hit scope", Calls(m("ArtifactCombat", prefix)).Any(c => c.Name == "Enter"));
            }
            Check("capture invokes current strict safety before damage", Calls(m("ArtifactCombat", "Capture")).Any(c => c.Name == "SafeTarget"));
            Check("capture stores map and position", new[] { "get_Map", "get_Position" }.All(n => Calls(m("ArtifactCombat", "Capture")).Any(c => c.Name == n)));
            Check("post gate never re-evaluates target life/hostility", Calls(m("ArtifactCombat", "ConsumeOpportunity")).All(c => !new[] { "SafeTarget", "get_Dead", "get_Downed", "HostileTo" }.Contains(c.Name)));
            Check("post gate forwards actual damage and captured context", Calls(m("ArtifactCombat", "AfterDamage")).Any(c => c.Name == "TryTriggerCaptured") && m("ArtifactEffects", "TryTriggerCaptured").Parameters[1].Name == "triggeringDamage");
            Check("captured dispatch no primary safety veto", !Calls(m("ArtifactEffects", "TryTriggerCaptured")).Any(c => c.Name == "SafeTarget"));
            Check("captured dispatch requires map and in-bounds cell", Calls(m("ArtifactEffects", "TryTriggerCaptured")).Any(c => c.Name == "InBounds") && m("ArtifactEffects", "TryTriggerCaptured").Body.Instructions.Any(i => (i.Operand as FieldReference)?.Name == "impactMap"));
            Check("all area effects receive captured center parameter", m("ArtifactEffects", "Apply").Parameters[3].Name == "center" && m("ArtifactEffects", "TryTriggerCaptured").Body.Instructions.Any(i => (i.Operand as FieldReference)?.Name == "impactCell"));
            Check("primary position used only for visited chain hop", Calls(m("ArtifactEffects", "Apply")).Count(c => c.Name == "get_Position") == 1);
            Check("extra damage always rechecks strict safety", Calls(m("ArtifactEffects", "Damage")).Any(c => c.Name == "SafeTarget"));
            Check("strict safety retains dead/downed/player/prisoner/map/hostility", new[] { "get_Dead", "get_Downed", "get_OfPlayer", "get_IsPrisonerOfColony", "get_Map", "HostileTo" }.All(n => Calls(m("ArtifactEffects", "SafeTarget")).Any(c => c.Name == n)));
            Check("nearby remains LOS bounded", Calls(m("ArtifactEffects", "Nearby")).Any(c => c.Name == "LineOfSight") && Calls(m("ArtifactEffects", "Nearby")).Any(c => c.Name == "InBounds"));
            Check("Vampire budget selects death/down before actual bonus damage", new[] { "get_Dead", "get_Downed", "Damage", "HealingBudget" }.All(n => Calls(m("ArtifactEffects", "VampireBudget")).Any(c => c.Name == n)));
            Check("DEV wrapper captures standing target into repaired path", Calls(m("ArtifactEffects", "TryTrigger")).Any(c => c.Name == "Capture") && Calls(m("ArtifactEffects", "TryTrigger")).Any(c => c.Name == "TryTriggerCaptured"));
            Check("DEV dispatch retains explicit DevMode gate", Calls(m("ArtifactEffects", "TryTriggerCaptured")).Any(c => c.Name == "get_DevMode"));
            Check("finalizer contains no exception suppression catch", m("ArtifactCombat", "Restore").Body.ExceptionHandlers.Count == 0);
        }
        // Core XML and Unity player are absent; named Def loading and running-map delivery/reload remain game gates.
    }
}
