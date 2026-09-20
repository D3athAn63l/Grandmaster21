using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace HarmonyLib
{
    public class Harmony
    {
        public Harmony(string id) { }
        public void PatchAll(Assembly asm) { }
        public MethodInfo Patch(MethodBase original, HarmonyMethod prefix = null, HarmonyMethod postfix = null,
            HarmonyMethod transpiler = null, HarmonyMethod finalizer = null) { return null; }
    }
    public class HarmonyMethod { public HarmonyMethod(MethodInfo m) { } }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class HarmonyPatch : Attribute
    {
        public HarmonyPatch() { }
        public HarmonyPatch(Type t) { }
        public HarmonyPatch(Type t, string name) { }
        public HarmonyPatch(Type t, string name, Type[] args) { }
    }
    [AttributeUsage(AttributeTargets.Method)] public class HarmonyPrefix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class HarmonyPostfix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class HarmonyTranspiler : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class HarmonyTargetMethod : Attribute { }

    public static class AccessTools
    {
        public static FieldInfo Field(Type t, string name) { return t.GetField(name, BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance); }
        public static MethodInfo Method(Type t, string name, Type[] args = null, Type[] generics = null)
        { const BindingFlags f = BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
          return args == null ? t.GetMethod(name, f) : t.GetMethod(name, f, null, args, null); }
        public static PropertyInfo Property(Type t, string name)
        { return t.GetProperty(name, BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance); }
        public static ConstructorInfo Constructor(Type t, Type[] args = null)
        { return args == null ? t.GetConstructor(Type.EmptyTypes) : t.GetConstructor(args); }
    }

    public class CodeInstruction
    {
        public OpCode opcode;
        public object operand;
        public List<Label> labels = new List<Label>();
        public List<ExceptionBlock> blocks = new List<ExceptionBlock>();
        public CodeInstruction(OpCode op, object operand = null) { this.opcode = op; this.operand = operand; }
    }
    public class ExceptionBlock { }
}
