using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Grandmaster21
{
    /// <summary>Counters returned by a cleanup run, for the result dialog.</summary>
    public struct Gm21CleanupReport
    {
        public int pawnsProcessed;
        public int skillsDemoted;
        public int progressCleared;
    }

    /// <summary>
    /// "Prepare Save for Uninstall".
    ///
    /// Converts every piece of Grandmaster21-specific state in the CURRENTLY LOADED game back
    /// into something vanilla can read without the mod installed:
    ///
    ///   * every stored level 21 becomes level 20 (via the authorised demotion path, which is
    ///     the only thing permitted to move a Grandmaster down);
    ///   * every SkillRecord's banked Grandmaster XP is dropped from the weak table entirely,
    ///     so SkillRecord.ExposeData no longer writes a "grandmasterXp" element at all.
    ///
    /// Nothing is saved automatically -- the player decides which slot to write.
    ///
    /// WHY THIS IS NEEDED: RimWorld ignores the unknown "grandmasterXp" element on load without
    /// the mod, but a stored "level 21" is read back by vanilla SkillRecord.ExposeData directly
    /// into levelInt, above vanilla's own cap. That pawn then sits at an out-of-range level until
    /// something happens to renormalise it.
    /// </summary>
    public static class Gm21Uninstall
    {
        // DELIBERATELY NOT TRACKED: there is no "this game is prepared for uninstall" status.
        // Cleanup is a point-in-time operation, not a property of the save. The moment the player
        // keeps playing, a pawn can bank new Grandmaster XP or earn a new level 21, and any stored
        // "prepared" marker becomes a lie. The result dialog reports what was done and tells the
        // player to save and quit; that is the whole contract.

        /// <summary>A playable save is loaded, so there is something to clean.</summary>
        public static bool GameIsLoaded
        {
            get { return Current.ProgramState == ProgramState.Playing && Current.Game != null; }
        }

        /// <summary>
        /// Entry point from Mod Settings. Refuses politely on the main menu; otherwise opens the
        /// confirmation dialog. Nothing is modified until the player confirms.
        /// </summary>
        public static void PromptFromSettings()
        {
            if (!GameIsLoaded)
            {
                Find.WindowStack.Add(new Dialog_MessageBox(
                    "GM21_Uninstall_NoGame".Translate(),
                    null, null, null, null,
                    "GM21_Uninstall_NoGameTitle".Translate()));
                return;
            }

            Find.WindowStack.Add(new Dialog_MessageBox(
                "GM21_Uninstall_ConfirmBody".Translate(),
                "GM21_Uninstall_ConfirmYes".Translate(), RunAndReport,
                "GM21_Uninstall_ConfirmNo".Translate(), null,
                "GM21_Uninstall_ConfirmTitle".Translate(),
                true));
        }

        private static void RunAndReport()
        {
            // Re-check: the player could conceivably have quit to the menu with the dialog open.
            if (!GameIsLoaded)
            {
                Find.WindowStack.Add(new Dialog_MessageBox(
                    "GM21_Uninstall_NoGame".Translate(),
                    null, null, null, null,
                    "GM21_Uninstall_NoGameTitle".Translate()));
                return;
            }

            Gm21CleanupReport report = Run();

            Log.Message("[Grandmaster 21] Prepare Save for Uninstall: "
                        + report.pawnsProcessed + " pawns processed, "
                        + report.skillsDemoted + " skills demoted 21 -> 20, "
                        + report.progressCleared + " skill records had Grandmaster progress cleared.");

            Find.WindowStack.Add(new Dialog_MessageBox(
                "GM21_Uninstall_DoneBody".Translate(
                    report.skillsDemoted, report.progressCleared, report.pawnsProcessed),
                null, null, null, null,
                "GM21_Uninstall_DoneTitle".Translate()));
        }

        /// <summary>
        /// The cleanup itself. Public so a dev action can drive it, but it does not prompt --
        /// callers are responsible for confirmation.
        /// </summary>
        public static Gm21CleanupReport Run()
        {
            HashSet<Pawn> pawns = new HashSet<Pawn>();
            CollectPawns(pawns);

            Gm21CleanupReport report = new Gm21CleanupReport();

            foreach (Pawn pawn in pawns)
            {
                if (pawn == null) continue;
                Pawn_SkillTracker tracker = pawn.skills;
                if (tracker == null || tracker.skills == null) continue;

                report.pawnsProcessed++;

                // Aim mode and melee doctrine are both Grandmaster-only state; once the pawn is
                // back to level 20 they are meaningless, and leaving either behind would write a
                // gm21AimMode or gm21MeleeDoctrine element into a save that is meant to contain no
                // Grandmaster21 state at all.
                Gm21AimModeStore.Clear(pawn);
                Gm21MeleeDoctrineStore.Clear(pawn);

                List<SkillRecord> records = tracker.skills;
                for (int i = 0; i < records.Count; i++)
                {
                    SkillRecord rec = records[i];
                    if (rec == null) continue;

                    // Progress is cleared for EVERY skill, not just Grandmasters: a level-20
                    // skill part-way to Grandmaster also carries mod-only state.
                    if (GrandmasterStore.Clear(rec)) report.progressCleared++;

                    if (Gm21.ForceDemoteForUninstall(rec)) report.skillsDemoted++;
                }
            }

            return report;
        }

        /// <summary>
        /// Enumerates every pawn container that can persist a SkillRecord into the save file.
        /// Deduplicated through the caller's HashSet, because these sources overlap heavily
        /// (a caravan member is also a world pawn, and so on).
        ///
        /// Sources, and what each one adds:
        ///
        ///  1. PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead
        ///     The broad sweep. Covers all maps (spawned and unspawned, including pawns inside
        ///     cryptosleep caskets, containers and transport pods), all world pawns alive and
        ///     dead, caravan members, pawns aboard travelling transporters, and PawnsFinder's
        ///     "temporary" list -- which is where pawns held by quests and by in-progress
        ///     generation live.
        ///  2. Find.Maps -> map.mapPawns.AllPawns
        ///     Explicit per-map pass. Redundant with (1) by design: if PawnsFinder's shape ever
        ///     changes, the maps are still covered.
        ///  3. Find.WorldPawns.AllPawnsAliveOrDead
        ///     Every pawn the world is keeping: quest-held pawns, relatives, ex-colonists,
        ///     faction leaders, and dead pawns whose records are still resurrectable.
        ///  4. Find.WorldObjects.Caravans -> caravan.PawnsListForReading
        ///     Explicit caravan pass, for the same belt-and-braces reason as (2).
        ///  5. Corpses lying on any map -> Corpse.InnerPawn
        ///     A corpse still carries the pawn's full SkillRecord list, and resurrection
        ///     restores it. A Grandmaster corpse left at 21 would reanimate over the cap.
        ///
        /// Each source is guarded independently so a single failure cannot abort the run.
        /// </summary>
        private static void CollectPawns(HashSet<Pawn> into)
        {
            AddRange(into, "PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead",
                () => PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead);

            Collect(into, "Find.Maps", delegate
            {
                List<Map> maps = Find.Maps;
                if (maps == null) return;
                for (int i = 0; i < maps.Count; i++)
                {
                    Map map = maps[i];
                    if (map == null || map.mapPawns == null) continue;
                    AddAll(into, map.mapPawns.AllPawns);
                }
            });

            AddRange(into, "Find.WorldPawns.AllPawnsAliveOrDead",
                () => Find.WorldPawns != null ? Find.WorldPawns.AllPawnsAliveOrDead : null);

            Collect(into, "Find.WorldObjects.Caravans", delegate
            {
                if (Find.WorldObjects == null) return;
                List<Caravan> caravans = Find.WorldObjects.Caravans;
                if (caravans == null) return;
                for (int i = 0; i < caravans.Count; i++)
                {
                    if (caravans[i] == null) continue;
                    AddAll(into, caravans[i].PawnsListForReading);
                }
            });

            Collect(into, "map corpses", delegate
            {
                List<Map> maps = Find.Maps;
                if (maps == null) return;
                for (int i = 0; i < maps.Count; i++)
                {
                    Map map = maps[i];
                    if (map == null || map.listerThings == null) continue;
                    List<Thing> corpses = map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse);
                    if (corpses == null) continue;
                    for (int j = 0; j < corpses.Count; j++)
                    {
                        Corpse corpse = corpses[j] as Corpse;
                        if (corpse != null && corpse.InnerPawn != null) into.Add(corpse.InnerPawn);
                    }
                }
            });
        }

        private static void AddRange(HashSet<Pawn> into, string label, Func<List<Pawn>> source)
        {
            Collect(into, label, delegate { AddAll(into, source()); });
        }

        private static void AddAll(HashSet<Pawn> into, List<Pawn> list)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null) into.Add(list[i]);
            }
        }

        /// <summary>
        /// Runs one collection source, logging and swallowing any failure. One unavailable
        /// container must not stop the other four from being cleaned.
        /// </summary>
        private static void Collect(HashSet<Pawn> into, string label, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Log.Warning("[Grandmaster 21] Uninstall cleanup could not enumerate " + label
                            + "; continuing with the remaining sources. " + e);
            }
        }
    }
}
