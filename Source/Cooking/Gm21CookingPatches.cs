using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// The Cooking 21 Harmony hooks, applied by hand, one INDEPENDENT group per feature, each under its
    /// own Harmony owner id. A target that cannot be resolved, or an IL shape that has changed, throws
    /// while that group binds; the group then unpatches only its own owner, logs once, and its flag stays
    /// down. Nothing else in the mod (and no other Cooking feature) is affected, and a disabled group
    /// simply leaves vanilla behaviour in place -- the fail-safe direction.
    ///
    ///   hygiene   CompFoodPoisonable.Notify_RecipeProduced  prefix     -- Perfect Hygiene
    ///   ingest    Thing.Ingested                            prefix+postfix -- Masterful Meal reward
    ///   freshness CompRottable.TickInterval                 transpiler -- the ONE rot-advance site
    ///   estimate  CompRottable.TicksUntilRotAtTemp          transpiler -- the "days until rot" figure
    ///
    /// Not patched, on purpose: CompRottable itself, RotProgress, any temperature calculation,
    /// GenTemperature, CompFoodPoisonable's stack semantics, or any ThingDef.
    /// </summary>
    internal static class Gm21CookingPatches
    {
        private const string OwnerPrefix = "Grandmaster21.Cooking.";

        internal static void Apply()
        {
            Gm21Cooking.HygieneEnabled = Group("Hygiene", "Perfect Hygiene", delegate(Harmony h)
            {
                // The override itself, not the ThingComp base: a future version that dropped it would
                // resolve to null here and disable the group instead of patching the wrong method.
                MethodInfo target = Need(AccessTools.DeclaredMethod(typeof(CompFoodPoisonable),
                    "Notify_RecipeProduced", new[] { typeof(Pawn) }), "CompFoodPoisonable.Notify_RecipeProduced(Pawn)");
                h.Patch(target, prefix: Hook(nameof(Prefix_NotifyRecipeProduced)));
            });

            Gm21Cooking.IngestionEnabled = Group("Ingest", "Masterful Meal ingestion", delegate(Harmony h)
            {
                MethodInfo target = Need(AccessTools.DeclaredMethod(typeof(Thing), "Ingested",
                    new[] { typeof(Pawn), typeof(float) }), "Thing.Ingested(Pawn, float)");
                h.Patch(target, prefix: Hook(nameof(Prefix_Ingested)), postfix: Hook(nameof(Postfix_Ingested)));
            });

            Gm21Cooking.FreshnessEnabled = Group("Freshness", "Masterful freshness", delegate(Harmony h)
            {
                MethodInfo target = Need(AccessTools.DeclaredMethod(typeof(CompRottable), "TickInterval",
                    new[] { typeof(int) }), "CompRottable.TickInterval(int)");
                h.Patch(target, transpiler: Hook(nameof(Transpiler_ScaleRotRate)));
            });

            Gm21Cooking.FreshnessEstimateEnabled = Group("Estimate", "Masterful freshness estimate", delegate(Harmony h)
            {
                MethodInfo target = Need(AccessTools.DeclaredMethod(typeof(CompRottable), "TicksUntilRotAtTemp",
                    new[] { typeof(float) }), "CompRottable.TicksUntilRotAtTemp(float)");
                h.Patch(target, transpiler: Hook(nameof(Transpiler_ScaleRotRate)));
            });
        }

        // ---------------------------------------------------------------- Perfect Hygiene

        /// <summary>
        /// Skips vanilla's cooking-contamination roll (the FilthyKitchen and IncompetentCook chances)
        /// ONLY when the worker is a Cooking Grandmaster. For anyone else this returns true and vanilla
        /// runs untouched. Rotten food, dangerous food types and every other source of poison are
        /// decided elsewhere and are not affected.
        /// </summary>
        internal static bool Prefix_NotifyRecipeProduced(Pawn pawn)
        {
            return !Gm21Cooking.IsCookingGrandmaster(pawn);
        }

        // ---------------------------------------------------------------- Masterful Meal ingestion

        /// <summary>
        /// Thing.Ingested splits the eaten portion off and DISCARDS it, then fires PostIngested on the
        /// remainder, so no comp callback can see how many Masterful servings were eaten. The count
        /// before the call and the count left after it can: the difference is exactly what was eaten.
        /// </summary>
        internal static void Prefix_Ingested(Thing __instance, out int __state)
        {
            __state = 0;
            try
            {
                ThingWithComps thing = __instance as ThingWithComps;
                CompGrandmasterMeal meal = thing != null ? thing.GetComp<CompGrandmasterMeal>() : null;
                if (meal != null) __state = meal.MasterfulCount;
            }
            catch (Exception e) { WarnOnce("ingest (before)", e); }
        }

        internal static void Postfix_Ingested(Thing __instance, Pawn ingester, int __state)
        {
            if (__state <= 0) return;
            try
            {
                int remaining = 0;
                if (!__instance.Destroyed)
                {
                    ThingWithComps thing = __instance as ThingWithComps;
                    CompGrandmasterMeal meal = thing != null ? thing.GetComp<CompGrandmasterMeal>() : null;
                    if (meal != null) remaining = meal.MasterfulCount;
                }
                int eaten = __state - remaining;
                if (eaten > 0) Gm21MasterfulMeal.Consumed(ingester, eaten);
            }
            catch (Exception e) { WarnOnce("ingest (after)", e); }
        }

        // ---------------------------------------------------------------- freshness

        /// <summary>
        /// Rewrites the single call to GenTemperature.RotRateAtTemperature so its result passes through
        /// <see cref="Gm21MasterfulRot.ScaleRate"/>. Everything else in the method -- temperature
        /// lookup, the RotProgress update, destruction, daily rot damage -- runs as vanilla. Exactly one
        /// call site is required; anything else throws and disables the group.
        /// </summary>
        internal static IEnumerable<CodeInstruction> Transpiler_ScaleRotRate(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            MethodInfo rate = Need(AccessTools.Method(typeof(GenTemperature), "RotRateAtTemperature",
                new[] { typeof(float) }), "GenTemperature.RotRateAtTemperature(float)");
            int index = UniqueCall(code, rate);
            code.InsertRange(index + 1, new[]
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Gm21MasterfulRot), nameof(Gm21MasterfulRot.ScaleRate)))
            });
            return code;
        }

        private static int UniqueCall(List<CodeInstruction> code, MethodInfo method)
        {
            int[] found = Enumerable.Range(0, code.Count).Where(i =>
                (code[i].opcode == OpCodes.Call || code[i].opcode == OpCodes.Callvirt)
                && Equals(code[i].operand, method)).ToArray();
            if (found.Length != 1)
                throw new InvalidOperationException("expected exactly one audited call to " + method + ", found " + found.Length);
            return found[0];
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>Binds one feature under its own owner id; on any failure removes only that owner.</summary>
        private static bool Group(string id, string feature, Action<Harmony> bind)
        {
            string owner = OwnerPrefix + id;
            Harmony harmony = new Harmony(owner);
            try
            {
                bind(harmony);
                return true;
            }
            catch (Exception e)
            {
                try { harmony.UnpatchAll(owner); } catch (Exception) { }
                Exception cause = e.GetBaseException();
                Log.Warning("[Grandmaster 21] Cooking 21: " + feature + " is disabled this session; could not bind the "
                            + "audited vanilla method (" + cause.GetType().Name + ": " + cause.Message
                            + "). Vanilla behaviour is unchanged. Other Cooking features remain available.");
                return false;
            }
        }

        private static MethodInfo Need(MethodInfo method, string what)
        {
            if (method == null) throw new MissingMethodException(what + " not found");
            return method;
        }

        private static HarmonyMethod Hook(string name)
        {
            MethodInfo m = AccessTools.Method(typeof(Gm21CookingPatches), name);
            if (m == null) throw new MissingMethodException("Gm21CookingPatches." + name + " not found");
            return new HarmonyMethod(m);
        }

        private static readonly HashSet<string> Warned = new HashSet<string>();

        internal static void WarnOnce(string stage, Exception e)
        {
            if (!Warned.Add(stage)) return;
            Log.Warning("[Grandmaster 21] Cooking 21 " + stage + ": " + e.GetType().Name + ": " + e.Message);
        }
    }

    /// <summary>
    /// Freshness. The vanilla rot rate for the current temperature is multiplied by the stack's
    /// <see cref="CompGrandmasterMeal.RotMultiplier"/>: 1 - masterfulRatio * PreservationStrength.
    /// One CompRottable timer per stack is kept and its RotProgress is only advanced by the (scaled)
    /// vanilla step -- no parallel timer, no per-serving age, no rewrite of RotProgress from outside.
    ///
    /// A frozen stack has rate 0 and stays at 0. Refrigeration, freezing, heat and any modded
    /// temperature environment work exactly as before, because only the multiplication is new.
    /// Food without Masterful servings takes the early exit and returns the vanilla rate unchanged.
    /// </summary>
    internal static class Gm21MasterfulRot
    {
        internal static float ScaleRate(float rate, CompRottable rot)
        {
            try
            {
                if (rate <= 0f || rot == null) return rate;
                ThingWithComps thing = rot.parent;
                if (thing == null) return rate;
                CompGrandmasterMeal meal = thing.GetComp<CompGrandmasterMeal>();
                if (meal == null) return rate;
                float multiplier = meal.RotMultiplier;
                return multiplier >= 1f ? rate : rate * multiplier;
            }
            catch (Exception e)
            {
                // This runs for every rotting thing on every map: it must never throw.
                Gm21CookingPatches.WarnOnce("freshness", e);
                return rate;
            }
        }
    }

    /// <summary>The Masterful Meal reward. Small on purpose, and tuned entirely in the ThoughtDef.</summary>
    internal static class Gm21MasterfulMeal
    {
        /// <summary>
        /// Called once per Thing.Ingested that consumed at least one Masterful serving. The memory is
        /// gained once per meal, and only because a Masterful serving was actually eaten: the count was
        /// taken out of the stack, so an ordinary serving can never earn it, and one Masterful serving
        /// among nine ordinary ones gives exactly one reward across the whole stack's lifetime.
        /// </summary>
        internal static void Consumed(Pawn ingester, int masterfulServings)
        {
            if (masterfulServings <= 0 || ingester == null) return;
            ThoughtDef def = Gm21CookingDefOf.GM21_MasterfulMeal;
            if (def == null) return;
            MemoryThoughtHandler memories = ingester.needs != null && ingester.needs.mood != null
                ? ingester.needs.mood.thoughts.memories : null;
            if (memories != null) memories.TryGainMemory(def);
        }
    }
}
