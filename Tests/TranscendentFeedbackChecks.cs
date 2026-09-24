// Real CLR/Def/IL and policy fixtures; no claim of rendered visuals, audible sounds or a running map.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Grandmaster21.Transcendent;
using Mono.Cecil;
using Mono.Cecil.Cil;
using RimWorld;
using Verse;

internal static partial class TranscendentChecks
{
    public class RecordedInjury : Hediff_Injury
    {
        public float healed;
        public override void Heal(float amount)
        {
            // Controlled native virtual callback fixture; do not initialize a fake game health runtime.
            healed += amount; SeverityFixture(this, Severity - amount);
        }
    }
    static void SeverityFixture(Hediff h, float severity) { typeof(Hediff).GetField("severityInt", Any).SetValue(h, severity); }
    static RecordedInjury Wound(float severity)
    {
        var injury = new RecordedInjury { def = new HediffDef { defName = "FixtureInjury", injuryProps = new InjuryProps() } };
        SeverityFixture(injury, severity); return injury;
    }
    static float RegenBudget(float damage) { return (float)Invoke("ArtifactHealing", "RegenerationBudget", damage); }
    static void Refresh(Hediff_ArtifactRegenerating regen, float budget) { regen.GetType().GetMethod("Refresh", Any).Invoke(regen, new object[] { budget }); }
    static void Advance(Hediff_ArtifactRegenerating regen, int ticks, Action<float> heal) { regen.GetType().GetMethod("Advance", Any).Invoke(regen, new object[] { ticks, heal }); }
    static bool Near(float a, float b) { return Math.Abs(a - b) < .0001f; }
    static void FeedbackChecks(string dll, string root, string savePath)
    {
        var text = XDocument.Load(Path.Combine(root, "Languages/English/Keyed/Transcendent.xml")).Root;
        foreach (string tier in new[] { "Magical", "Mythical", "Divine", "Anomaly", "Null" })
        foreach (string phenomenon in Enum.GetNames(Feature("ArtifactPhenomenon")).Where(p => p != "None"))
        {
            var template = text.Element("GM21_TC_Effect_" + phenomenon);
            object[] values = (object[])Invoke("ArtifactPhenomenonInfo", "DescriptionValues", Phenomenon(phenomenon), Tier(tier));
            string formatted = template == null ? "" : string.Format(CultureInfo.InvariantCulture, template.Value, values);
            Check(tier + " " + phenomenon + " has complete mechanical description", formatted.Length > 40 && !formatted.Contains("{") && !formatted.Contains("}"));
            Check(tier + " " + phenomenon + " hides creation and identity internals", !new[] { "0.01%", "0.10%", "seed", "proc counter", "roll order", "secret", "Null", "Anomaly" }.Any(s => formatted.Contains(s)));
        }
        var chain = (object[])Invoke("ArtifactPhenomenonInfo", "DescriptionValues", Phenomenon("ChainLightning"), Tier("Null"));
        Check("owned N/0 chain displays effective 36/30/24/18", chain[2].ToString() == "36 / 30 / 24 / 18");
        Check("chain text gets gameplay limits", Convert.ToInt32(chain[0]) == 4 && Convert.ToSingle(chain[1]) == 6);
        foreach (float damage in new[] { -1f, 0f, float.NaN, float.PositiveInfinity, float.NegativeInfinity, 1f, 20f, 100f, float.MaxValue })
        {
            float expected = damage > 0 && !float.IsInfinity(damage) && !float.IsNaN(damage) ? Math.Min(6, damage * .2f) : 0;
            Check("regen budget actual damage " + damage, Near(RegenBudget(damage), expected));
        }
        Check("damage 20 immediate seven plus regen four", Near((float)Invoke("ArtifactEffects", "HealingBudget", 20f), 7) && Near(RegenBudget(20), 4));
        Check("damage 100 combined capped fourteen", Near((float)Invoke("ArtifactEffects", "HealingBudget", 100f) + RegenBudget(100), 14));
        var small = Wound(2); var large = Wound(12); var middle = Wound(5); var chronic = Wound(100); chronic.def.chronic = true;
        var scar = Wound(200);
        var permanent = new HediffComp_GetsPermanent { parent = scar, props = new HediffCompProperties_GetsPermanent() };
        permanent.IsPermanent = true; scar.comps = new List<HediffComp> { permanent };
        var cancer = new Hediff { def = new HediffDef { defName = "Carcinoma" } };
        var missing = new Hediff_MissingPart { def = new HediffDef { defName = "MissingBodyPart" } };
        var disease = new Hediff { def = new HediffDef { defName = "Infection" } };
        var implant = new Hediff_AddedPart { def = new HediffDef { defName = "Implant" } };
        var arbitrary = new Hediff { def = new HediffDef { defName = "Pregnancy" } };
        foreach (Hediff excluded in new Hediff[] { scar, chronic, cancer, missing, disease, implant, arbitrary })
            Check("regen excludes " + excluded.def.defName + (ReferenceEquals(excluded, scar) ? " permanent scar" : ""), !(bool)Invoke("ArtifactHealing", "Eligible", excluded));
        var ordered = ((IEnumerable<Hediff_Injury>)Invoke("ArtifactHealing", "Ordered", (object)new Hediff[] { small, scar, large, cancer, middle, missing, chronic, disease })).ToList();
        Check("highest-severity ordinary injuries ordered first", ordered.SequenceEqual(new Hediff_Injury[] { large, middle, small }));
        foreach (float severity in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
            Check("invalid severity excluded " + severity, !(bool)Invoke("ArtifactHealing", "Eligible", Wound(severity)));
        Pawn pawn = CombatPawn(PawnHealthState.Mobile);
        pawn.health.hediffSet = new HediffSet(pawn); pawn.health.hediffSet.hediffs.AddRange(new Hediff[] { small, large, middle, scar, cancer });
        foreach (Hediff injury in pawn.health.hediffSet.hediffs) injury.pawn = pawn;
        float healed = (float)Invoke("ArtifactHealing", "Heal", pawn, 14f);
        Check("shared heal budget fills largest then next injury", Near(healed, 14) && Near(large.healed, 12) && Near(middle.healed, 2) && small.healed == 0 && scar.healed == 0);
        Set(pawn.health, "healthState", PawnHealthState.Dead);
        Check("dead pawn receives no healing", (float)Invoke("ArtifactHealing", "Heal", pawn, 8f) == 0 && small.healed == 0);
        Set(pawn.health, "healthState", PawnHealthState.Mobile);
        foreach (int interval in new[] { 1, 7, 10, 17, 60, 250 })
        {
            var regen = new Hediff_ArtifactRegenerating { pawn = pawn }; Refresh(regen, 6);
            var pulses = new List<float>(); int elapsed = 0;
            while (elapsed < 60) { Advance(regen, interval, pulses.Add); elapsed += interval; }
            Check("regen delta " + interval + " six pulses total six", pulses.Count == 6 && Near(pulses.Sum(), 6) && pulses.All(x => Near(x, 1)));
            Check("regen delta " + interval + " expires once", (int)Get(regen, "remainingTicks") == 0 && Near((float)Get(regen, "remainingBudget"), 0) && regen.ShouldRemove);
            Advance(regen, 100, pulses.Add);
            Check("expired regen delta " + interval + " cannot heal again", pulses.Count == 6);
        }
        var active = new Hediff_ArtifactRegenerating { pawn = pawn, def = new HediffDef { defName = "GM21_ArtifactRegenerating" } };
        Refresh(active, 4); var partial = new List<float>(); Advance(active, 9, partial.Add);
        Check("regen waits until first tenth tick", partial.Count == 0 && (int)Get(active, "remainingTicks") == 51);
        Advance(active, 1, partial.Add);
        Check("regen first pulse distributes one sixth", partial.Count == 1 && Near(partial[0], 4f / 6));
        Refresh(active, 1);
        Check("weaker reapplication refreshes without reducing larger remainder", (int)Get(active, "remainingTicks") == 60 && Near((float)Get(active, "remainingBudget"), 4f - 4f / 6));
        Refresh(active, 5);
        Check("stronger reapplication replaces not adds", Near((float)Get(active, "remainingBudget"), 5));
        for (int i = 0; i < 100; i++) Refresh(active, 6);
        Check("repeated DEV application cannot exceed six", Near((float)Get(active, "remainingBudget"), 6));
        var duplicate = new Hediff_ArtifactRegenerating { def = active.def }; Refresh(duplicate, 4);
        Check("duplicate native hediff merges into one budget", active.TryMergeWith(duplicate) && Near((float)Get(active, "remainingBudget"), 6));
        pawn.health.hediffSet.hediffs.Add(active);
        Invoke("ArtifactHealing", "StartRegeneration", pawn, 5f);
        Check("reapplication reuses only existing local hediff", pawn.health.hediffSet.hediffs.OfType<Hediff_ArtifactRegenerating>().Count() == 1 && Near((float)Get(active, "remainingBudget"), 6));
        Advance(active, 23, x => { });
        Scribe.saver.InitSaving(savePath, "regen"); active.ExposeData(); Scribe.saver.FinalizeSaving();
        XElement saved = XDocument.Load(savePath).Root;
        Check("regen Scribe writes exact remaining duration/budget/phase", int.Parse(saved.Element("gm21RegenTicks").Value) == 37 && int.Parse(saved.Element("gm21RegenPulse").Value) == 7 && Near(float.Parse(saved.Element("gm21RegenBudget").Value, CultureInfo.InvariantCulture), 4));
        // Explicit field reconstruction checks scheduler continuity; native Scribe readback is separately dependency-blocked.
        var resumed = new Hediff_ArtifactRegenerating { pawn = pawn };
        Set(resumed, "remainingTicks", int.Parse(saved.Element("gm21RegenTicks").Value));
        Set(resumed, "ticksToPulse", int.Parse(saved.Element("gm21RegenPulse").Value));
        Set(resumed, "remainingBudget", float.Parse(saved.Element("gm21RegenBudget").Value, CultureInfo.InvariantCulture));
        var tail = new List<float>(); Advance(resumed, 6, tail.Add); Check("restored phase does not grant early pulse", tail.Count == 0);
        Advance(resumed, 31, tail.Add); Check("restored scheduler spends only remaining four", tail.Count == 4 && Near(tail.Sum(), 4) && resumed.ShouldRemove);
        active.Notify_PawnDied(null);
        Check("death cancels buff and budget without resurrection", active.ShouldRemove && (float)Get(active, "remainingBudget") == 0);
        var throwing = new Hediff_ArtifactRegenerating(); Refresh(throwing, 6);
        bool threw = false; try { Advance(throwing, 10, x => { throw new InvalidOperationException("heal callback"); }); } catch (TargetInvocationException) { threw = true; }
        Check("pulse commits budget before throwing callback", threw && Near((float)Get(throwing, "remainingBudget"), 5) && (int)Get(throwing, "remainingTicks") == 50);
        ProvenanceFixtures(); FeedbackIl(dll, root);
    }
    static void ProvenanceFixtures()
    {
        var merged = Wound(2); var unrelated = Wound(8); var fresh = Wound(3); fresh.def = merged.def;
        var before = new Dictionary<Hediff, float> { { merged, 2 }, { unrelated, 8 } };
        SeverityFixture(merged, 5);
        var result = new DamageWorker.DamageResult { hediffs = new List<Hediff> { fresh }, totalDamageDealt = 3 };
        var affected = (List<Hediff>)Invoke("ArtifactProvenance", "Affected", new Hediff[] { merged, unrelated }, result, before);
        Check("injury provenance includes native merged survivor", affected.Contains(merged) && affected.Contains(fresh) && !affected.Contains(unrelated));
        ThingDef weapon = Def("ArtifactWeapon");
        foreach (string phenomenon in new[] { "Chain lightning", "Smite", "Flame wave", "Gravity crush", "Vampiric strike", "Spatial slash" })
        {
            Invoke("ArtifactProvenance", "Tag", affected, weapon, phenomenon);
            Check(phenomenon + " provenance uses native source fields", merged.sourceDef == weapon && merged.sourceLabel == phenomenon && merged.sourceToolLabel == null && merged.sourceHediffDef == null);
            Check(phenomenon + " visible in native injury brackets", merged.LabelInBrackets.Contains(phenomenon));
        }
        Check("provenance preserves native injury type, severity and unrelated source", merged.GetType() == typeof(RecordedInjury) && merged.Severity == 5 && unrelated.sourceDef == null);
    }
    static void FeedbackIl(string dll, string root)
    {
        var defs = Directory.GetFiles(Path.Combine(root, "Defs/Transcendent"), "*.xml").SelectMany(f => XDocument.Load(f).Root.Elements()).ToList();
        foreach (string p in Enum.GetNames(Feature("ArtifactPhenomenon")).Where(p => p != "None"))
        {
            var log = defs.Single(x => x.Name == "RulePackDef" && x.Element("defName").Value == "GM21_Log_" + p);
            string rule = log.Descendants("li").Single().Value;
            Check(p + " battle rule has audited grammar root and subject", rule.StartsWith("r_logentry->") && rule.Contains(p == "FrostNova" ? "[SUBJECT_nameDef]" : "[RECIPIENT_nameDef]"));
        }
        Check("only two existing custom DamageDefs retained", defs.Count(x => x.Name == "DamageDef") == 2);
        Check("no custom injury definition changes health behavior", defs.Where(x => x.Name == "HediffDef").All(x => x.Element("hediffClass").Value != "Hediff_Injury"));
        var frost = defs.Single(x => x.Element("defName").Value == "GM21_ArtifactFrost");
        Check("frost duration and movement match shared description values", int.Parse(frost.Descendants("disappearsAfterTicks").Single().Value) == (int)Feature("ArtifactPhenomenonInfo").GetField("FrostTicks", Any).GetRawConstantValue()
            && Near(float.Parse(frost.Descendants("MoveSpeed").Single().Value, CultureInfo.InvariantCulture), (float)Feature("ArtifactPhenomenonInfo").GetField("FrostMoveFactor", Any).GetRawConstantValue()));
        using (var a = AssemblyDefinition.ReadAssembly(dll))
        using (var native = AssemblyDefinition.ReadAssembly(typeof(Pawn).Assembly.Location))
        {
            Func<string, TypeDefinition> t = n => a.MainModule.Types.Single(x => x.Name == n);
            Func<string, string, MethodDefinition> m = (type, name) => t(type).Methods.Single(x => x.Name == name);
            var details = m("ArtifactPhenomenonInfo", "Summary");
            Check("displayed probability calls gameplay Reliability", Calls(details).Any(c => c.DeclaringType.Name == "ArtifactRolls" && c.Name == "Reliability"));
            Check("normal info routes through shared details", Calls(m("CompArtifact", "get_TierText")).Any(c => c.DeclaringType.Name == "ArtifactPhenomenonInfo" && c.Name == "Details"));
            Check("owned Null keeps unexplained existing label", m("CompArtifact", "get_IdentityText").Body.Instructions.Any(i => Equals(i.Operand, "[N/0]")));
            Check("normal information does not read seed/counter", t("ArtifactPhenomenonInfo").Methods.Where(x => x.HasBody).SelectMany(x => x.Body.Instructions).All(i => !(i.Operand is FieldReference) || !new[] { "phenomenonSeed", "procCounter" }.Contains(((FieldReference)i.Operand).Name)));
            Check("damage retains native Stab, Cut and Blunt defs", new[] { "Stab", "Cut", "Blunt" }.All(n => t("ArtifactEffects").Methods.Where(x => x.HasBody).SelectMany(x => x.Body.Instructions).Any(i => (i.Operand as FieldReference)?.DeclaringType.Name == "DamageDefOf" && ((FieldReference)i.Operand).Name == n)));
            var damage = m("ArtifactEffects", "Damage");
            Check("native damage occurs before attribution", damage.Body.Instructions.ToList().FindIndex(i => (i.Operand as MethodReference)?.Name == "TakeDamage") < damage.Body.Instructions.ToList().FindIndex(i => (i.Operand as MethodReference)?.Name == "Record"));
            Check("damage still has no generated weapon provenance", Calls(damage).Any(c => c.DeclaringType.Name == "DamageInfo" && c.Name == ".ctor") && !Calls(damage).Any(c => c.Name == "SetWeaponHediff"));
            Check("provenance uses actual BattleLog and injury association", Calls(m("ArtifactProvenance", "Record")).Any(c => c.DeclaringType.Name == "BattleLogEntry_DamageTaken" && c.Name == ".ctor") && Calls(m("ArtifactProvenance", "Record")).Any(c => c.Name == "AssociateWithLog"));
            Check("native health persists source labels and combat log text", new[] { "sourceLabel", "combatLogText", "source" }.All(n => native.MainModule.Types.Single(x => x.Name == "Hediff").Methods.Single(x => x.Name == "ExposeData").Body.Instructions.Any(i => Equals(i.Operand, n))));
            foreach (string method in new[] { "Record", "Status" })
                Check(method + " battle presentation restores RNG", m("ArtifactProvenance", method).Body.ExceptionHandlers.Any(h => h.HandlerType == ExceptionHandlerType.Finally) && new[] { "PushState", "PopState" }.All(n => Calls(m("ArtifactProvenance", method)).Any(c => c.Name == n)));
            Check("lethal Vampire recovery has native status log", m("ArtifactEffects", "Apply").Body.Instructions.Any(i => Equals(i.Operand, "GM21_Log_VampiricRecovery")));
            Check("compact inspect keeps detailed mechanics in tooltip/card", Calls(m("CompArtifact", "CompInspectStringExtra")).Any(c => c.Name == "Summary") && Calls(m("CompArtifact", "GetDescriptionPart")).Any(c => c.Name == "get_TierText"));
            Check("provenance failure caught separately", m("ArtifactProvenance", "Record").Body.ExceptionHandlers.Any(h => h.HandlerType == ExceptionHandlerType.Catch));
            Check("regen uses native interval override", m("Hediff_ArtifactRegenerating", "TickInterval").IsVirtual && Calls(m("Hediff_ArtifactRegenerating", "TickInterval")).Any(c => c.Name == "Advance"));
            Check("regen death cancels via native override", m("Hediff_ArtifactRegenerating", "Notify_PawnDied").IsVirtual);
            Check("regen persistence writes duration phase and budget", new[] { "gm21RegenTicks", "gm21RegenPulse", "gm21RegenBudget" }.All(n => m("Hediff_ArtifactRegenerating", "ExposeData").Body.Instructions.Any(i => Equals(i.Operand, n))));
            var dispatch = m("ArtifactEffects", "TryTriggerCaptured");
            Check("visual dispatch follows gameplay", dispatch.Body.Instructions.ToList().FindIndex(i => (i.Operand as MethodReference)?.Name == "Show") > dispatch.Body.Instructions.ToList().FindIndex(i => (i.Operand as MethodReference)?.Name == "Apply"));
            var safe = m("ArtifactFeedback", "Safely");
            Check("VFX errors caught with RNG restoration finally", safe.Body.ExceptionHandlers.Any(h => h.HandlerType == ExceptionHandlerType.Catch) && safe.Body.ExceptionHandlers.Any(h => h.HandlerType == ExceptionHandlerType.Finally) && new[] { "PushState", "PopState" }.All(n => Calls(safe).Any(c => c.Name == n)));
            var visualMethods = NestedTypes(t("ArtifactFeedback")).SelectMany(x => x.Methods).Where(x => x.HasBody).ToList();
            var visualCalls = visualMethods.SelectMany(Calls).ToList();
            Check("visuals cannot damage heal ignite or change terrain", !visualCalls.Any(c => new[] { "TakeDamage", "Heal", "AddHediff", "TryStartFireIn", "DoExplosion", "SetTerrain", "GeneratePawn", "MakeThing" }.Contains(c.Name)));
            Check("no global pawn collection in regen or feedback", new[] { "ArtifactFeedback", "ArtifactHealing", "Hediff_ArtifactRegenerating" }.SelectMany(n => NestedTypes(t(n))).SelectMany(x => x.Methods).Where(x => x.HasBody).SelectMany(Calls).All(c => !c.Name.Contains("AllPawns") && !c.Name.Contains("AllMaps")));
            Check("one positional audio call site and no global camera sound", visualCalls.Count(c => c.Name == "PlayOneShot") == 1 && visualCalls.Any(c => c.Name == "InMap") && !visualCalls.Any(c => c.Name == "OnCamera" || c.Name == "PlayOneShotOnCamera"));
            Check("native transient flecks used", visualCalls.Any(c => c.Name == "CreateFleck"));
            Check("bounded ring and transfer particle constants", (int)t("ArtifactFeedback").Fields.Single(x => x.Name == "RingSegments").Constant == 16 && (int)t("ArtifactFeedback").Fields.Single(x => x.Name == "TransferParticles").Constant == 8);
            foreach (var field in visualMethods.SelectMany(x => x.Body.Instructions).Select(i => i.Operand as FieldReference).Where(f => f != null && (f.DeclaringType.Name == "FleckDefOf" || f.DeclaringType.Name == "SoundDefOf")).GroupBy(f => f.FullName).Select(g => g.First()))
            {
                var real = native.MainModule.Types.Single(x => x.FullName == field.DeclaringType.FullName).Fields.SingleOrDefault(x => x.Name == field.Name);
                Check("audited Core feedback reference " + field.DeclaringType.Name + "." + field.Name, real != null && !real.CustomAttributes.Any(attr => attr.AttributeType.Name.StartsWith("MayRequire")));
            }
            var dev = NestedTypes(t("CompArtifact")).SelectMany(x => x.Methods).Where(x => x.HasBody).ToList();
            Check("VFX-only DEV command exists", dev.SelectMany(x => x.Body.Instructions).Any(i => Equals(i.Operand, "DEV: Phenomenon VFX only")));
            CleanupChecks(t("ArtifactFeedback"), t("CompArtifact"), native);
        }
    }
    static void CleanupChecks(TypeDefinition feedback, TypeDefinition artifact, AssemblyDefinition native)
    {
        var render = feedback.Methods.Single(x => x.Name == "Render");
        var sounds = render.Body.Instructions.Select(i => i.Operand as FieldReference)
            .Where(f => f != null && f.DeclaringType.Name == "SoundDefOf").Select(f => f.Name).ToArray();
        Check("Flame uses ignition one-shot and other six assignments remain unchanged", sounds.SequenceEqual(new[] {
            "EnergyShield_AbsorbDamage", "Thunder_OnMap", "Interact_Ignite", "EnergyShield_Reset",
            "Pawn_Melee_Punch_HitBuilding_Generic", "Power_OnSmall", "Execute_Cut" }));
        Check("feedback has no HissJet reference or sustainer lifecycle", NestedTypes(feedback).SelectMany(x => x.Methods).Where(x => x.HasBody)
            .SelectMany(x => x.Body.Instructions).All(i => (i.Operand as FieldReference)?.Name != "HissJet"
                && !((i.Operand as MethodReference)?.Name ?? "").Contains("Sustainer")));
        Check("render has one sound dispatch per phenomenon", Calls(render).Count(c => c.DeclaringType.Name == "ArtifactFeedback" && c.Name == "Sound") == 7);

        // Audit the supplied native assembly, not a guessed SoundDef name or a synthetic sound fixture.
        var verbs = NestedTypes(native.MainModule.Types.Single(x => x.FullName == "RimWorld.VerbDefsHardcodedNative"))
            .SelectMany(x => x.Methods).Where(x => x.HasBody).SelectMany(x => x.Body.Instructions).ToList();
        int ignition = verbs.FindIndex(i => (i.Operand as FieldReference)?.Name == "Interact_Ignite");
        Check("vanilla ignition assigns Interact_Ignite to soundCast", ignition >= 0 && ignition + 1 < verbs.Count
            && verbs[ignition + 1].OpCode == OpCodes.Stfld && (verbs[ignition + 1].Operand as FieldReference)?.Name == "soundCast");
        var shot = native.MainModule.Types.Single(x => x.FullName == "Verse.Verb").Methods.Single(x => x.Name == "TryCastNextBurstShot");
        var shotIL = shot.Body.Instructions.ToList();
        int playback = shotIL.FindIndex(i => (i.Operand as MethodReference)?.Name == "PlayOneShot");
        var soundPath = shotIL.Take(Math.Max(0, playback)).Reverse().Take(16).ToList();
        Check("vanilla soundCast reaches positional PlayOneShot", playback >= 0
            && soundPath.Any(i => (i.Operand as FieldReference)?.Name == "soundCast")
            && soundPath.Any(i => (i.Operand as MethodReference)?.DeclaringType.Name == "TargetInfo"));

        var methods = NestedTypes(artifact).SelectMany(x => x.Methods).Where(x => x.HasBody).ToList();
        var iterator = methods.Single(x => x.Body.Instructions.Any(i => Equals(i.Operand, "DEV: Artifact state")));
        var il = iterator.Body.Instructions.ToList();
        int label = il.FindIndex(i => Equals(i.Operand, "DEV: Artifact state"));
        int desc = il.FindIndex(label, i => i.OpCode == OpCodes.Stfld && (i.Operand as FieldReference)?.Name == "defaultDesc");
        string tooltip = desc > 0 ? il[desc - 1].Operand as string : null;
        Check("artifact state command has explanatory diagnostic log tooltip", tooltip != null
            && new[] { "Developer diagnostic", "prints", "Player.log", "No popup", "tier", "phenomenon", "seed", "proc counter", "cooldown", "origin" }.All(tooltip.Contains));
        int gate = il.FindIndex(i => (i.Operand as MethodReference)?.Name == "get_DevMode");
        var disabled = gate >= 0 ? il[gate + 1].Operand as Instruction : null;
        Check("DevMode off exits artifact iterator before any command", gate >= 0 && gate < label
            && (il[gate + 1].OpCode == OpCodes.Brfalse || il[gate + 1].OpCode == OpCodes.Brfalse_S)
            && disabled != null && disabled.OpCode == OpCodes.Ldc_I4_0 && disabled.Next.OpCode == OpCodes.Ret);
        var log = methods.Single(x => x.Body.Instructions.Any(i => Equals(i.Operand, " counter=")));
        var strings = log.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldstr).Select(i => (string)i.Operand).ToArray();
        Check("artifact state retains exact log text and no window call", strings.SequenceEqual(new[] {
            "[Grandmaster 21][DEV] ", " tier=", " phenomenon=", " seed=", " counter=", " cooldown=", " origin=", " / " })
            && Calls(log).Count(c => c.DeclaringType.Name == "Log" && c.Name == "Message") == 1
            && !Calls(log).Any(c => c.DeclaringType.Name == "WindowStack"));
        Check("artifact state log retains all original diagnostic fields", log.Body.Instructions.Select(i => i.Operand as FieldReference)
            .Where(f => f != null && f.DeclaringType.Name == "CompArtifact").Select(f => f.Name).SequenceEqual(new[] {
                "tier", "phenomenon", "phenomenonSeed", "procCounter", "nextProcTick", "projectId", "initiatorName" }));
    }
}
