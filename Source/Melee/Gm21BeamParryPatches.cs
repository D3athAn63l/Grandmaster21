using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace Grandmaster21
{
    internal static class Gm21BeamParryPatches
    {
        private static MethodInfo Method(Type type, string name, params Type[] args)
        { return AccessTools.Method(type, name, args); }
        private static HarmonyMethod Hook(string name)
        { return new HarmonyMethod(AccessTools.Method(typeof(Gm21BeamParryPatches), name)); }

        internal static bool Apply(Harmony unused)
        {
            // Own owner ID permits rollback of this feature only, even after a partial bind.
            var harmony = new Harmony("Grandmaster21.BeamParry");
            try
            {
                var warmup = Method(typeof(Verb_ShootBeam), "WarmupComplete");
                var next = Method(typeof(Verb), "TryCastNextBurstShot");
                var shot = Method(typeof(Verb), "TryCastShot");
                var hit = Method(typeof(Verb_ShootBeam), "HitCell", typeof(IntVec3), typeof(IntVec3), typeof(float));
                var damage = Method(typeof(Verb_ShootBeam), "ApplyDamage", typeof(Thing), typeof(IntVec3), typeof(float));
                var reset = Method(typeof(Verb), "Reset");
                if (warmup == null || next == null || shot == null || hit == null || damage == null || reset == null
                    || warmup.ReturnType != typeof(void) || shot.ReturnType != typeof(bool)
                    || damage.ReturnType != typeof(void)) throw new MissingMethodException("audited beam lifecycle/damage signatures");
                harmony.Patch(warmup, prefix: Hook("Begin"), finalizer: Hook("FinishWarmup"));
                harmony.Patch(next, prefix: Hook("Enter"), transpiler: Hook("EndDefendedShot"), finalizer: Hook("FinishShot"));
                harmony.Patch(damage, prefix: Hook("Damage"));
                harmony.Patch(hit, prefix: Hook("Hit"), transpiler: Hook("StopGroundFire"));
                harmony.Patch(reset, prefix: Hook("Capture"), finalizer: Hook("FinishReset"));
                return true;
            }
            catch (Exception e)
            {
                harmony.UnpatchAll("Grandmaster21.BeamParry");
                Exception cause = e.GetBaseException();
                Log.Warning("[Grandmaster 21] Beam Parry disabled: could not bind the audited beam pipeline ("
                            + cause.GetType().Name + ": " + cause.Message + "). Other Melee features remain available.");
                return false;
            }
        }
        private static void Begin(Verb_ShootBeam __instance, out Gm21BeamParry.Attack __state)
        { __state = Gm21Melee.BeamParryEnabled ? Gm21BeamParry.Begin(__instance) : null; }
        private static Exception FinishWarmup(Verb_ShootBeam __instance, Gm21BeamParry.Attack __state, Exception __exception)
        {
            if (__exception != null || !__instance.Bursting) Gm21BeamParry.End(__instance, __state);
            return __exception;
        }
        private static void Enter(Verb __instance, out Gm21BeamParry.Scope __state)
        { __state = __instance is Verb_ShootBeam ? Gm21BeamParry.Enter(__instance) : null; }
        private static Exception FinishShot(Verb __instance, Gm21BeamParry.Scope __state, Exception __exception)
        {
            if (__state != null)
            {
                if (__exception != null || !__instance.Bursting) Gm21BeamParry.End(__instance, __state.Attack);
                Gm21BeamParry.Exit(__state);
            }
            return __exception;
        }
        private static void Capture(Verb __instance, out Gm21BeamParry.Attack __state)
        { __state = __instance is Verb_ShootBeam ? Gm21BeamParry.Find(__instance) : null; }
        private static Exception FinishReset(Verb __instance, Gm21BeamParry.Attack __state, Exception __exception)
        {
            Gm21BeamParry.End(__instance, __state);
            return __exception;
        }
        private static bool Damage(Verb_ShootBeam __instance, Thing thing, float damageFactor)
        { return Gm21BeamParry.ShouldDamage(__instance, thing, damageFactor); }
        private static bool Hit(Verb_ShootBeam __instance)
        { return !Gm21BeamParry.Blocked(__instance); }

        internal static IEnumerable<CodeInstruction> EndDefendedShot(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var shot = Method(typeof(Verb), "TryCastShot");
            int index = UniqueCall(code, shot);
            code.InsertRange(index + 1, new[] {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Gm21BeamParry), "ShotResult"))
            });
            return code;
        }
        internal static IEnumerable<CodeInstruction> StopGroundFire(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var code = new List<CodeInstruction>(instructions);
            var damage = Method(typeof(Verb_ShootBeam), "ApplyDamage", typeof(Thing), typeof(IntVec3), typeof(float));
            int index = UniqueCall(code, damage);
            if (index + 1 >= code.Count) throw new InvalidOperationException("beam HitCell has no continuation");
            Label proceed = generator.DefineLabel();
            code[index + 1].labels.Add(proceed);
            code.InsertRange(index + 1, new[] {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Gm21BeamParry), "Blocked")),
                new CodeInstruction(OpCodes.Brfalse, proceed),
                new CodeInstruction(OpCodes.Ret)
            });
            return code;
        }
        private static int UniqueCall(List<CodeInstruction> code, MethodInfo method)
        {
            var indexes = Enumerable.Range(0, code.Count).Where(i =>
                (code[i].opcode == OpCodes.Call || code[i].opcode == OpCodes.Callvirt)
                && Equals(code[i].operand, method)).ToArray();
            if (indexes.Length != 1) throw new InvalidOperationException("expected one audited call to " + method);
            return indexes[0];
        }
    }
}
