// Extends the existing real-DLL Crafting fixture without changing its runtime-tested patch.
// Audits Frame's actual IL, installs the production transpiler on the real method, proves exactly
// one operand changes, and executes the resulting notification call with real quality/letter code.
// Full Frame completion (map, spawning, resources, jobs) still requires the Unity player.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Xml.Linq;
using Grandmaster21;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
using RimWorld;
using Verse;
using OpCodes = System.Reflection.Emit.OpCodes;
using Code = Mono.Cecil.Cil.Code;

internal static partial class CraftingNotificationChecks
{
    static Action<Thing, Pawn> constructionCall;
    static readonly MethodInfo Complete = AccessTools.Method(typeof(Frame), "CompleteConstruction", new[] { typeof(Pawn) });
    static readonly MethodInfo ConstructionWrapper = AccessTools.Method(typeof(Patch_ConstructionLegendaryNotification), "SendConstructionNotification");
    static List<CodeInstruction> ConstructionTranspile(List<CodeInstruction> il)
    {
        return ((IEnumerable<CodeInstruction>)AccessTools.Method(typeof(Patch_ConstructionLegendaryNotification), "Transpiler")
            .Invoke(null, new object[] { il })).ToList();
    }

    static void ConstructionAuditAndInstall(string acsPath, string modPath)
    {
        Console.WriteLine("\n=== 9. Construction: real IL audit, fail-open behavior and live binding ===");
        using (ModuleDefinition acs = ModuleDefinition.ReadModule(acsPath))
        {
            MethodDefinition frame = acs.GetType("RimWorld.Frame").Methods.Single(m => m.Name == "CompleteConstruction");
            var body = frame.Body.Instructions;
            Check("Frame.CompleteConstruction is instance void(Pawn worker)", !frame.IsStatic && frame.ReturnType.FullName == "System.Void"
                && frame.Parameters.Count == 1 && frame.Parameters[0].ParameterType.FullName == "Verse.Pawn");
            Instruction make = body.Single(i => IsCall(i, "Verse.ThingMaker", "MakeThing"));
            Check("finished Thing is created by MakeThing and stored in local 5", make.Next.OpCode.Code == Code.Stloc_S
                && ((VariableDefinition)make.Next.Operand).Index == 5 && frame.Body.Variables[5].VariableType.FullName == "Verse.Thing");
            var stores = body.Where(i => i.OpCode.Code == Code.Stloc_S && ((VariableDefinition)i.Operand).Index == 5).ToList();
            Check("local 5 has only its initial null and MakeThing assignment", stores.Count == 2
                && stores[0].Previous.OpCode.Code == Code.Ldnull && stores[1] == make.Next);
            var sends = body.Where(i => IsCall(i, "RimWorld.QualityUtility", "SendCraftNotification")).ToList();
            Check("Frame has exactly one notification call", sends.Count == 1);
            Instruction send = sends.Single();
            Check("notification takes the finished Thing and original worker immediately after SetQuality",
                send.Previous.OpCode.Code == Code.Ldarg_1 && send.Previous.Previous.OpCode.Code == Code.Ldloc_S
                && ((VariableDefinition)send.Previous.Previous.Operand).Index == 5
                && IsCall(send.Previous.Previous.Previous, "RimWorld.CompQuality", "SetQuality"));
            Instruction roll = body.Single(i => IsCall(i, "RimWorld.QualityUtility", "GenerateQualityCreatedByPawn"));
            Check("Frame quality roll uses worker, Construction, true", roll.Previous.OpCode.Code == Code.Ldc_I4_1
                && ((FieldReference)roll.Previous.Previous.Operand).Name == "Construction"
                && roll.Previous.Previous.Previous.OpCode.Code == Code.Ldarg_1);
            Check("notification precedes art credit, spawning and construction record: all remain in vanilla body",
                send.Offset < body.Single(i => IsCall(i, "RimWorld.CompArt", "JustCreatedBy")).Offset
                && send.Offset < body.Single(i => IsCall(i, "Verse.GenSpawn", "Spawn")).Offset
                && send.Offset < body.Single(i => IsCall(i, "RimWorld.Pawn_RecordsTracker", "Increment")).Offset);
            MethodDefinition cube = acs.GetType("RimWorld.JobDriver_BuildCubeSculpture").Methods.Single(m => m.Name == "PlaceAndFinish");
            Instruction cubeSend = cube.Body.Instructions.Single(i => IsCall(i, "RimWorld.QualityUtility", "SendCraftNotification"));
            Check("cube is a separate job path using local 0 and JobDriver.pawn, with CubeInterest bookkeeping",
                cubeSend.Previous.OpCode.Code == Code.Ldfld && ((FieldReference)cubeSend.Previous.Operand).Name == "pawn"
                && cubeSend.Previous.Previous.Previous.OpCode.Code == Code.Ldloc_0
                && cube.Body.Instructions.Any(i => IsCall(i, "Verse.Hediff_CubeInterest", "Notify_BuiltSculpture")));
        }

        var original = PatchProcessor.GetOriginalInstructions(Complete).ToList();
        // Copy each instruction: check mutation precisely, including all labels and exception blocks.
        var rewritten = ConstructionTranspile(original.Select(i => new CodeInstruction(i)).ToList());
        int site = original.FindIndex(i => Equals(i.operand, Notify));
        Check("production transpiler changes exactly one operand and no opcode, labels or exception blocks",
            Patch_ConstructionLegendaryNotification.Applied && original.Count == rewritten.Count
            && original.Select((i, n) => i.opcode == rewritten[n].opcode && i.labels.SequenceEqual(rewritten[n].labels)
                && i.blocks.SequenceEqual(rewritten[n].blocks) && (n == site ? Equals(rewritten[n].operand, ConstructionWrapper)
                    : Equals(i.operand, rewritten[n].operand))).All(x => x));

        foreach (string broken in new[] { "missing", "duplicate", "wrong worker", "wrong Thing", "wrong quality call" })
        {
            var input = original.Select(i => new CodeInstruction(i)).ToList();
            if (broken == "missing") input[site].operand = ConstructionWrapper;
            if (broken == "duplicate") input.Add(new CodeInstruction(OpCodes.Call, Notify));
            if (broken == "wrong worker") input[site - 1].opcode = OpCodes.Ldarg_0;
            if (broken == "wrong Thing") input[site - 2].opcode = OpCodes.Ldloc_0;
            if (broken == "wrong quality call") input[site - 3].operand = Notify;
            var before = input.Select(i => new CodeInstruction(i)).ToList();
            int logsBefore = logged.Count;
            var output = ConstructionTranspile(input);
            Check("fail open on " + broken + ": unchanged IL and diagnostic", !Patch_ConstructionLegendaryNotification.Applied
                && output.Count == before.Count && output.Select((i, n) => i.opcode == before[n].opcode && Equals(i.operand, before[n].operand)).All(x => x)
                && logged.Count == logsBefore + 1);
        }

        int warnBefore = logged.Count;
        Patch_ConstructionLegendaryNotification.Apply(new Harmony(Gm21Id));
        Check("production Apply patches real Frame silently", Patch_ConstructionLegendaryNotification.Applied && logged.Count == warnBefore);
        Patches patches = Harmony.GetPatchInfo(Complete);
        Check("Frame gets only one GM21 transpiler, no context prefix/postfix/finalizer", patches.Transpilers.Count(p => p.owner == Gm21Id) == 1
            && !patches.Prefixes.Concat(patches.Postfixes).Concat(patches.Finalizers).Any(p => p.owner == Gm21Id));
        var current = PatchProcessor.GetCurrentInstructions(Complete).ToList();
        Check("live Frame body has one wrapper call and no remaining SendCraftNotification call",
            current.Count(i => Equals(i.operand, ConstructionWrapper)) == 1 && !current.Any(i => Equals(i.operand, Notify)));
        MethodInfo bound = (MethodInfo)current.Single(i => Equals(i.operand, ConstructionWrapper)).operand;
        constructionCall = (Action<Thing, Pawn>)Delegate.CreateDelegate(typeof(Action<Thing, Pawn>), bound);
        MethodInfo cubeMethod = AccessTools.Method(typeof(JobDriver_BuildCubeSculpture), "PlaceAndFinish");
        Check("cube call remains vanilla and carries no GM21 patch",
            PatchProcessor.GetCurrentInstructions(cubeMethod).Count(i => Equals(i.operand, Notify)) == 1
            && (Harmony.GetPatchInfo(cubeMethod) == null || !Harmony.GetPatchInfo(cubeMethod).Owners.Contains(Gm21Id)));
        Check("recipe notification call remains vanilla", PatchProcessor.GetCurrentInstructions(PostProcess).Count(i => Equals(i.operand, Notify)) == 1);
        Check("Construction adds no notification prefix to the shared method", Harmony.GetPatchInfo(Notify).Prefixes.Count(p => p.owner == Gm21Id) == 1);
        using (var mod = ModuleDefinition.ReadModule(modPath))
        {
            var wrapper = mod.GetType(typeof(Patch_ConstructionLegendaryNotification).FullName).Methods.Single(m => m.Name == "SendConstructionNotification");
            Check("wrapper has no field stores and calls no quality, art, construction or gameplay writer",
                !wrapper.Body.Instructions.Any(i => i.OpCode.Code == Code.Stfld || i.OpCode.Code == Code.Stsfld)
                && wrapper.Body.Instructions.Where(i => i.Operand is MethodReference).All(i =>
                    new[] { "get_Quality", "IsGrandmaster", "SendCraftNotification" }.Contains(((MethodReference)i.Operand).Name)));
        }
    }

    static void ConstructionSettings(string scratch, string root, string modPath)
    {
        Console.WriteLine("\n=== 10. Construction setting: real persistence, live reads and UI ===");
        Check("new settings default Construction to ON", new Gm21Settings().showConstructionGrandmasterLegendaryNotifications);
        settingsPath = Path.Combine(scratch, "construction-settings.xml");
        File.WriteAllText(settingsPath, "<SettingsBlock><ModSettings Class=\"Grandmaster21.Gm21Settings\"><showCraftingGrandmasterLegendaryNotifications>False</showCraftingGrandmasterLegendaryNotifications></ModSettings></SettingsBlock>");
        var old = LoadedModManager.ReadModSettings<Gm21Settings>("Grandmaster21", "Gm21Mod");
        Check("old Crafting-OFF config missing Construction key loads Construction ON", old.showConstructionGrandmasterLegendaryNotifications
            && !old.showCraftingGrandmasterLegendaryNotifications);
        foreach (bool craft in new[] { true, false }) foreach (bool build in new[] { true, false })
        {
            old.showCraftingGrandmasterLegendaryNotifications = craft;
            old.showConstructionGrandmasterLegendaryNotifications = build;
            LoadedModManager.WriteModSettings("Grandmaster21", "Gm21Mod", old);
            var read = LoadedModManager.ReadModSettings<Gm21Settings>("Grandmaster21", "Gm21Mod");
            Check("real settings round trip Crafting=" + craft + "/Construction=" + build,
                read.showCraftingGrandmasterLegendaryNotifications == craft && read.showConstructionGrandmasterLegendaryNotifications == build
                && File.ReadAllText(settingsPath).Contains("<showConstructionGrandmasterLegendaryNotifications>False") == !build);
        }
        var keyed = XDocument.Load(Path.Combine(root, "Languages/English/Keyed/Grandmaster21.xml")).Root;
        Check("Construction label and precise learned-level/frame tooltip shipped", (string)keyed.Element("GM21_Setting_ConstructionLegendaryLetters")
            == "Construction Grandmaster Legendary notifications" && ((string)keyed.Element("GM21_Setting_ConstructionLegendaryLettersDesc")).Contains("learned Construction level is 21"));
        using (var mod = ModuleDefinition.ReadModule(modPath))
        {
            var draw = mod.GetType("Grandmaster21.Gm21Mod").Methods.Single(m => m.Name == "DoSettingsWindowContents");
            var keys = draw.Body.Instructions.Where(i => i.OpCode.Code == Code.Ldstr).Select(i => (string)i.Operand).ToList();
            int d = keys.IndexOf("GM21_Setting_DeterministicQuality"), c = keys.IndexOf("GM21_Setting_CraftingLegendaryLetters"),
                b = keys.IndexOf("GM21_Setting_ConstructionLegendaryLetters"), p = keys.IndexOf("GM21_Setting_ShowProgress");
            Check("settings order: deterministic, Crafting, Construction, progress", d >= 0 && d < c && c < b && b < p
                && keys.Contains("GM21_Setting_ConstructionLegendaryLettersDesc"));
        }
    }

    // Real quality roll and SetQuality, then the actual wrapper operand bound from patched Frame IL.
    // This isolates the audited notification sequence; it does not simulate spawning a full building.
    static List<string> CompleteNotification(ThingWithComps thing, Pawn worker)
    {
        letters.Clear();
        thing.compQuality.SetQuality(QualityUtility.GenerateQualityCreatedByPawn(worker, construction, true), ArtGenerationContext.Colony);
        constructionCall(thing, worker);
        return new List<string>(letters);
    }

    static void ConstructionBehavior()
    {
        Console.WriteLine("\n=== 11. Construction behavior and independent toggle matrix ===");
        Pawn both = MakePawn("Both", 21, artisticLevel: 21, constructionLevel: 21);
        foreach (bool craft in new[] { true, false }) foreach (bool build in new[] { true, false })
        {
            Show = craft;
            Gm21Mod.Settings.showConstructionGrandmasterLegendaryNotifications = build;
            var equipment = Product(); var building = Product(); var art = Product(true);
            var c = Craft(equipment, Recipe("TestEquipment", crafting), both);
            var b = CompleteNotification(building, both);
            var a = Craft(art, Recipe("TestSculpture", artistic), both);
            Check("independent letters Crafting=" + craft + "/Construction=" + build,
                (craft ? Only(c, Legendary) : c.Count == 0) && (build ? Only(b, Legendary) : b.Count == 0));
            Check("matrix preserves both Legendary products and Artistic letter", QualityOf(equipment) == QualityCategory.Legendary
                && QualityOf(building) == QualityCategory.Legendary && QualityOf(art) == QualityCategory.Legendary && Only(a, LegendaryArt));
        }
        Show = false; Gm21Mod.Settings.showConstructionGrandmasterLegendaryNotifications = false;
        Pawn builder = MakePawn("BuilderOnly", 5, constructionLevel: 21);
        Check("Construction 21 / Crafting 5 follows Construction toggle", CompleteNotification(Product(), builder).Count == 0);
        Pawn crafter = MakePawn("CrafterOnly", 21, constructionLevel: 20);
        Check("Crafting 21 / Construction 20 keeps Masterwork letter", Only(CompleteNotification(Product(), crafter), Masterwork));
        otherModLegendaryFor = crafter;
        Check("other mod's Legendary at stored Construction 20 keeps letter", Only(CompleteNotification(Product(), crafter), Legendary));
        Set(crafter.skills.GetSkill(construction), "aptitudeCached", (int?)1);
        Check("stored Construction 20 + aptitude 1 is not eligible", crafter.skills.GetSkill(construction).levelInt == 20
            && !Gm21.IsGrandmaster(crafter, construction) && Only(CompleteNotification(Product(), crafter), Legendary));
        otherModLegendaryFor = null;
        Set(builder.skills.GetSkill(construction), "aptitudeCached", (int?)(-5));
        Check("stored Construction 21 with negative aptitude is eligible", builder.skills.GetSkill(construction).Level == 16
            && CompleteNotification(Product(), builder).Count == 0);
        otherModMasterworkFor = builder;
        var changed = Product();
        Check("other mod changes GM completion to Masterwork: letter kept", Only(CompleteNotification(changed, builder), Masterwork)
            && QualityOf(changed) == QualityCategory.Masterwork);
        otherModMasterworkFor = null;
        var direct = Product(); CompleteNotification(direct, both);
        Check("unrelated direct Legendary notification remains vanilla", Only(Announce(direct, both), Legendary));
        var withArt = Product(true);
        Check("Legendary construction with art suppresses only the letter", CompleteNotification(withArt, both).Count == 0
            && QualityOf(withArt) == QualityCategory.Legendary);
        Gm21Mod.Settings.showConstructionGrandmasterLegendaryNotifications = true;
        Check("turning Construction ON restores next letter without restart", Only(CompleteNotification(Product(), builder), Legendary));
        Gm21Mod.Settings.showConstructionGrandmasterLegendaryNotifications = false;

        // Another item's direct notification inside SetQuality must not be captured by construction.
        otherModDuringCraft = () => QualityUtility.SendCraftNotification(direct, both);
        var nested = CompleteNotification(Product(), both);
        Check("unrelated same-worker Legendary notification during completion is not swallowed", Only(nested, Legendary));
        otherModDuringCraft = () => { throw new InvalidOperationException("test completion exception"); };
        try { CompleteNotification(Product(), both); Check("completion exception propagates", false); }
        catch (InvalidOperationException) { Check("completion exception propagates", true); }
        Check("no stale suppression after exception", Only(Announce(direct, both), Legendary));
        // Re-run all existing Crafting isolation/order/quality tests with Construction OFF and installed.
        Grandmaster(); NotGrandmaster(); OtherSkills(); Isolation(); HarmonyOrdering(); QualityUntouched();

        var saved = Gm21Mod.Settings;
        Gm21Mod.Settings = null; letters.Clear(); constructionCall(direct, both);
        Check("missing settings forwards to vanilla", Only(letters, Legendary));
        Gm21Mod.Settings = saved;
        letters.Clear(); constructionCall(null, both); constructionCall(direct, null); constructionCall(Uninit<Thing>(), both);
        Check("null worker/Thing and quality-less Thing retain vanilla no-letter behavior", letters.Count == 0);
    }
}
