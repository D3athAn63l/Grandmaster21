using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Grandmaster21
{
    /// <summary>
    /// Cooking 21 startup, self-contained like Medicine 21's. It runs once, after every Def is loaded and
    /// resolved:
    ///
    ///   1. attaches CompGrandmasterMeal to prepared-food ThingDefs (see <see cref="IsMasterfulCandidate"/>);
    ///   2. binds the independent Harmony groups (each disables only itself if its target changed);
    ///   3. resolves the two private poison fields Purify Food needs;
    ///   4. registers the Cooking cleaner with "Prepare Save for Uninstall".
    ///
    /// One summary line is logged; nothing is logged per meal, per tick or per pawn.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class Gm21CookingStartup
    {
        static Gm21CookingStartup()
        {
            int attached = 0;
            try
            {
                attached = AttachMasterfulComp();
            }
            catch (Exception e)
            {
                Log.Warning("[Grandmaster 21] Cooking 21: could not attach Masterful Meal state to food ("
                            + e.GetType().Name + ": " + e.Message + "). Food is unchanged; Perfect Hygiene "
                            + "and Purify Food do not depend on it.");
            }
            Gm21Cooking.MasterfulEnabled = attached > 0;

            Gm21CookingPatches.Apply();
            Gm21Cooking.PurifyEnabled = Gm21PurifyFood.Bind();
            Gm21Uninstall.RegisterPawnCleaner(Gm21CookingUninstall.CleanPawn);

            Log.Message("[Grandmaster 21] Cooking 21  |  hygiene=" + Gm21Cooking.HygieneEnabled
                        + " masterful=" + Gm21Cooking.MasterfulEnabled + " (" + attached + " food defs)"
                        + " ingestion=" + Gm21Cooking.IngestionEnabled
                        + " freshness=" + Gm21Cooking.FreshnessEnabled
                        + " estimate=" + Gm21Cooking.FreshnessEstimateEnabled
                        + " purify=" + Gm21Cooking.PurifyEnabled);
        }

        /// <summary>
        /// Adds the comp to every eligible ThingDef and returns how many. Idempotent: a def that already
        /// has it is skipped. Comps are instantiated from ThingDef.comps when a Thing is made, so this
        /// covers every food created or loaded from now on, and a save made before the mod (or with the
        /// element absent) simply starts each stack at zero Masterful servings.
        /// </summary>
        internal static int AttachMasterfulComp()
        {
            int attached = 0;
            List<ThingDef> defs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef def = defs[i];
                if (!IsMasterfulCandidate(def)) continue;
                def.comps.Add(new CompProperties_GrandmasterMeal());
                attached++;
            }
            return attached;
        }

        /// <summary>
        /// Masterful state belongs on ingestible items that a Cooking recipe can logically produce. The
        /// marker for that is vanilla's own CompFoodPoisonable -- the comp whose Notify_RecipeProduced
        /// vanilla calls for a cooked product -- so the rule is "an ingestible ThingWithComps item that
        /// carries CompFoodPoisonable (or a subclass)", found from the comp list, never from a DefName.
        /// Raw ingredients, drugs, corpses and anything without the comp are left alone.
        /// </summary>
        internal static bool IsMasterfulCandidate(ThingDef def)
        {
            if (def == null || def.comps == null || def.category != ThingCategory.Item || !def.IsIngestible) return false;
            if (def.thingClass == null || !typeof(ThingWithComps).IsAssignableFrom(def.thingClass)) return false;
            bool poisonable = false;
            for (int i = 0; i < def.comps.Count; i++)
            {
                CompProperties props = def.comps[i];
                if (props == null || props.compClass == null) continue;
                if (typeof(CompGrandmasterMeal).IsAssignableFrom(props.compClass)) return false; // already attached
                if (typeof(CompFoodPoisonable).IsAssignableFrom(props.compClass)) poisonable = true;
            }
            return poisonable;
        }
    }

    /// <summary>
    /// Cooking's part of "Prepare Save for Uninstall", called once per collected pawn:
    ///
    ///   * every Masterful Meal memory is removed -- a save that must load without the mod cannot
    ///     reference this mod's ThoughtDef;
    ///   * a Purify Food job in progress or queued is ended -- likewise it cannot reference this mod's
    ///     JobDefs.
    ///
    /// Masterful servings themselves are stored as a single "gm21MasterfulCount" element inside each food
    /// Thing's own node. Vanilla ignores an element it has no field for, so that needs no cleanup.
    /// Returns the number of entries removed.
    /// </summary>
    internal static class Gm21CookingUninstall
    {
        internal static int CleanPawn(Pawn pawn)
        {
            if (pawn == null) return 0;
            return RemoveMemories(pawn) + EndPurifyJobs(pawn);
        }

        private static int RemoveMemories(Pawn pawn)
        {
            ThoughtDef def = Gm21CookingDefOf.GM21_MasterfulMeal;
            if (def == null || pawn.needs == null || pawn.needs.mood == null || pawn.needs.mood.thoughts == null) return 0;
            MemoryThoughtHandler memories = pawn.needs.mood.thoughts.memories;
            if (memories == null) return 0;
            int found = 0;
            List<Thought_Memory> all = memories.Memories;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i].def == def) found++;
            }
            if (found > 0) memories.RemoveMemoriesOfDef(def);
            return found;
        }

        private static int EndPurifyJobs(Pawn pawn)
        {
            Pawn_JobTracker jobs = pawn.jobs;
            if (jobs == null) return 0;
            int ended = 0;

            if (jobs.jobQueue != null)
            {
                List<Job> queued = new List<Job>();
                foreach (QueuedJob q in jobs.jobQueue)
                {
                    if (q != null && q.job != null && Gm21CookingDefOf.IsPurifyJob(q.job.def)) queued.Add(q.job);
                }
                for (int i = 0; i < queued.Count; i++)
                {
                    jobs.jobQueue.Extract(queued[i]);
                    ended++;
                }
            }

            if (jobs.curJob != null && Gm21CookingDefOf.IsPurifyJob(jobs.curJob.def))
            {
                jobs.EndCurrentJob(JobCondition.InterruptForced);
                ended++;
            }
            return ended;
        }
    }
}
