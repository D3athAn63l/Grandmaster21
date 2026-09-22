using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;
using Verse.AI;

namespace Grandmaster21.Transcendent
{
    internal static class TranscendentIngredients
    {
        private delegate bool FindIngredients(Bill bill, Pawn pawn, Thing giver, List<ThingCount> chosen, List<IngredientCount> missing);
        private delegate bool SelectIngredients(List<Thing> available, Bill bill, List<ThingCount> chosen, IntVec3 root, bool sorted, List<IngredientCount> missing);
        private static readonly FindIngredients Find = Bind<FindIngredients>("TryFindBestBillIngredients");
        private static readonly SelectIngredients Select = Bind<SelectIngredients>("TryFindBestBillIngredientsInSet_NoMix");
        internal static bool Available { get { return Find != null && Select != null; } }

        private static T Bind<T>(string name) where T : class
        {
            // Real 1.6 methods, audited against the supplied Assembly-CSharp, not a Harmony patch.
            try
            {
                MethodInfo method = typeof(WorkGiver_DoBill).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
                return Delegate.CreateDelegate(typeof(T), method) as T;
            }
            catch (Exception ex)
            {
                Log.Error("[Grandmaster 21] Magical crafting disabled: cannot bind " + name + ": " + ex.Message);
                return null;
            }
        }

        internal static bool TryFind(Building_MagicalWorkstation bench, Pawn pawn, Bill bill, out List<ThingCount> chosen)
        {
            chosen = new List<ThingCount>();
            if (!Available || !Find(bill, pawn, bench, chosen, null)) return false;
            IngredientCount catalyst = new IngredientCount();
            catalyst.filter.SetAllow(TranscendentDefOf.GM21_MagicalCatalyst, true);
            catalyst.SetBaseCount(1);
            List<ThingCount> extra = new List<ThingCount>();
            if (!WorkGiver_DoBill.TryFindBestFixedIngredients(new List<IngredientCount> { catalyst }, pawn, bench, extra, bill.ingredientSearchRadius)) return false;
            if (extra.Count != 1 || extra[0].Count != 1) return false;
            chosen.Add(extra[0]);
            return chosen.All(c => c.Thing != null && c.Count > 0 && c.Count <= c.Thing.stackCount);
        }

        internal static bool TryValidatePlaced(Building_MagicalWorkstation bench, Pawn pawn, Job job,
            out List<ThingCount> chosen, out Thing dominant)
        {
            chosen = new List<ThingCount>();
            dominant = null;
            Bill bill = job.bill;
            if (!Available || bill == null || bill != bench.Pending || job.placedThings.NullOrEmpty()) return false;
            Dictionary<Thing, int> delivered = new Dictionary<Thing, int>();
            foreach (ThingCountClass c in job.placedThings)
            {
                Thing t = c.thing;
                if (t == null || t.Destroyed || !t.Spawned || t.Map != bench.Map || t.IsForbidden(pawn)
                    || c.Count <= 0 || !t.Position.InHorDistOf(bench.Position, 6)) return false;
                int existing;
                delivered.TryGetValue(t, out existing);
                delivered[t] = existing + c.Count;
            }
            if (delivered.Any(kv => kv.Value > kv.Key.stackCount)) return false;
            List<Thing> available = delivered.Keys.Where(t => t.def != TranscendentDefOf.GM21_MagicalCatalyst
                && bill.IsFixedOrAllowedIngredient(t)).ToList();
            if (!Select(available, bill, chosen, bench.InteractionCell, false, null)) return false;
            if (chosen.Count == 0 || chosen.Any(c => c.Count <= 0 || c.Count > delivered[c.Thing])) return false;
            if (bill.recipe.products[0].thingDef.MadeFromStuff)
            {
                // Eligible stuff recipes have a disjoint, first ingredient slot and no mixing.
                dominant = chosen.FirstOrDefault(c => bill.recipe.ingredients[0].filter.Allows(c.Thing)).Thing;
                if (dominant == null || dominant.def != bench.PendingStuff || !dominant.def.stuffProps.CanMake(bill.recipe.products[0].thingDef)) return false;
            }
            else dominant = chosen.OrderByDescending(c => c.Count).First().Thing;
            Thing catalyst = delivered.Keys.FirstOrDefault(t => t.def == TranscendentDefOf.GM21_MagicalCatalyst && delivered[t] >= 1);
            if (catalyst == null) return false;
            chosen.Add(new ThingCount(catalyst, 1));
            return true;
        }
    }
}
