using System.Collections.Generic;
using System.Text;
using LudeonTK;
using RimWorld;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// Dev-mode helpers for exercising Cooking 21 in a running game, in the same "Grandmaster 21" menu as
    /// the rest of the mod. Each acts on the food under the cursor (every prepared-food stack in the
    /// clicked cell). None of them weakens a rule of normal play: they only set state the real systems
    /// would otherwise take a cook and a recipe to create.
    /// </summary>
    public static class Gm21CookingDebugActions
    {
        [DebugAction("Grandmaster 21", "Cooking: mark whole stack Masterful",
            actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void MarkStackMasterful()
        {
            foreach (Thing t in FoodUnderCursor())
            {
                CompGrandmasterMeal meal = t.TryGetComp<CompGrandmasterMeal>();
                if (meal != null) meal.SetMasterful(t.stackCount);
            }
        }

        [DebugAction("Grandmaster 21", "Cooking: mark ONE more serving Masterful",
            actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void MarkOneMasterful()
        {
            foreach (Thing t in FoodUnderCursor())
            {
                CompGrandmasterMeal meal = t.TryGetComp<CompGrandmasterMeal>();
                if (meal != null) meal.SetMasterful(meal.MasterfulCount + 1);
            }
        }

        [DebugAction("Grandmaster 21", "Cooking: split one serving off the stack",
            actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SplitOneOff()
        {
            Map map = Find.CurrentMap;
            IntVec3 cell = UI.MouseCell();
            foreach (Thing t in FoodUnderCursor())
            {
                if (t.stackCount < 2) continue;
                Thing piece = t.SplitOff(1);
                GenPlace.TryPlaceThing(piece, cell, map, ThingPlaceMode.Near);
            }
        }

        [DebugAction("Grandmaster 21", "Cooking: poison food stack (100%)",
            actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void PoisonStack()
        {
            foreach (Thing t in FoodUnderCursor())
            {
                CompFoodPoisonable poison = t.TryGetComp<CompFoodPoisonable>();
                if (poison != null) poison.SetPoisoned(FoodPoisonCause.FilthyKitchen);
            }
        }

        [DebugAction("Grandmaster 21", "Cooking: purify food stack (instant)",
            actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void PurifyStack()
        {
            foreach (Thing t in FoodUnderCursor()) Gm21PurifyFood.Purify(t);
        }

        [DebugAction("Grandmaster 21", "Cooking: report food stack",
            actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ReportStack()
        {
            foreach (Thing t in FoodUnderCursor())
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Cooking 21 state for " + t.LabelCap + " (" + t.def.defName + "):");
                sb.AppendLine("  stackCount=" + t.stackCount);
                CompGrandmasterMeal meal = t.TryGetComp<CompGrandmasterMeal>();
                sb.AppendLine("  masterfulCount=" + (meal != null ? meal.MasterfulCount.ToString() : "n/a (no comp)")
                              + (meal != null ? "  ratio=" + meal.MasterfulRatio.ToString("F3")
                                                + "  rotMultiplier=" + meal.RotMultiplier.ToString("F3") : ""));
                CompRottable rot = t.TryGetComp<CompRottable>();
                if (rot != null)
                {
                    sb.AppendLine("  rotProgress=" + rot.RotProgress.ToString("F1") + " (" + rot.RotProgressPct.ToString("P1")
                                  + " of fresh)  stage=" + rot.Stage + "  ticksUntilRot=" + rot.TicksUntilRotAtCurrentTemp);
                }
                CompFoodPoisonable poison = t.TryGetComp<CompFoodPoisonable>();
                if (poison != null) sb.AppendLine("  poisonPercent=" + poison.PoisonPercent.ToString("F3") + "  cause=" + poison.Cause);
                Log.Message(sb.ToString());
            }
        }

        /// <summary>Snapshot of every ThingWithComps in the clicked cell that carries Cooking state.</summary>
        private static List<Thing> FoodUnderCursor()
        {
            List<Thing> found = new List<Thing>();
            Map map = Find.CurrentMap;
            if (map == null) return found;
            List<Thing> things = UI.MouseCell().GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                Thing t = things[i];
                if (t.TryGetComp<CompGrandmasterMeal>() != null || t.TryGetComp<CompFoodPoisonable>() != null) found.Add(t);
            }
            return found;
        }
    }
}
