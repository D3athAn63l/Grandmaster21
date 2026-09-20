using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grandmaster21
{
    /// <summary>Mutable box so the weak table can hold a reference type.</summary>
    public sealed class GmProgress
    {
        public double xp;
    }

    /// <summary>
    /// Per-SkillRecord Grandmaster XP.
    ///
    /// WHY A WEAK TABLE KEYED ON SkillRecord, and not a GameComponent dictionary of pawns:
    ///
    ///  * Persistence rides along with the pawn automatically. The value is written from a
    ///    postfix on SkillRecord.ExposeData, so it lands inside the pawn's own save node.
    ///    That means caravans, world pawns, map transitions and despawn/respawn all carry it
    ///    with zero extra code, because the pawn carries its own SkillRecords.
    ///  * No global registry to prune, so a removed map cannot leak skill state and a dead
    ///    pawn's entry is simply collected.
    ///
    /// WHY double: the default requirement is 1e9. A float's ULP at 1e9 is 64, so a 1 XP
    /// increment would vanish entirely. double has a 52-bit mantissa (ULP ~2.4e-7 at 1e9), so
    /// every increment lands. Verse.ParseHelper registers a System.Double parser, so
    /// Scribe_Values.Look&lt;double&gt; round-trips through save/load correctly.
    /// </summary>
    public static class GrandmasterStore
    {
        private static readonly ConditionalWeakTable<SkillRecord, GmProgress> Table =
            new ConditionalWeakTable<SkillRecord, GmProgress>();

        // Cached so the hot path does not allocate a delegate per call.
        private static readonly ConditionalWeakTable<SkillRecord, GmProgress>.CreateValueCallback Factory =
            _ => new GmProgress();

        public static double Get(SkillRecord rec)
        {
            GmProgress p;
            return (rec != null && Table.TryGetValue(rec, out p)) ? p.xp : 0.0;
        }

        public static void Set(SkillRecord rec, double value)
        {
            if (rec == null) return;
            Table.GetValue(rec, Factory).xp = value;
        }

        public static void Add(SkillRecord rec, double amount)
        {
            if (rec == null || amount <= 0.0) return;
            GmProgress p = Table.GetValue(rec, Factory);
            p.xp += amount;
        }

        /// <summary>
        /// Drops the record's Grandmaster progress entirely.
        ///
        /// Removes the weak-table entry rather than zeroing it, so a cleaned SkillRecord holds
        /// no mod state at all. ExposeData writes "grandmasterXp" with a default of 0.0 and
        /// forceSave=false, so after this call the element is omitted from the save file
        /// completely -- which is what "Prepare Save for Uninstall" needs.
        ///
        /// Returns true if there was progress to remove.
        /// </summary>
        public static bool Clear(SkillRecord rec)
        {
            if (rec == null) return false;
            GmProgress p;
            if (!Table.TryGetValue(rec, out p)) return false;
            bool had = p.xp > 0.0;
            // Zero the box first: anything still holding a reference to it sees 0, not stale XP.
            p.xp = 0.0;
            Table.Remove(rec);
            return had;
        }

        /// <summary>Fraction of the configured requirement, 0..1.</summary>
        public static double ProgressPercent(SkillRecord rec)
        {
            double req = Gm21Mod.Settings.grandmasterXpRequirement;
            if (req <= 0.0) return 1.0;
            double cur = Get(rec);
            return cur <= 0.0 ? 0.0 : (cur >= req ? 1.0 : cur / req);
        }
    }

    /// <summary>
    /// Serialisation. Appending to SkillRecord.ExposeData writes "grandmasterXp" into the same
    /// XML node as vanilla's "level" and "xpSinceLastLevel".
    ///
    /// Save-removal behaviour: if this mod is uninstalled, RimWorld simply ignores the unknown
    /// "grandmasterXp" element. See README for what happens to a stored level of 21.
    /// </summary>
    [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.ExposeData))]
    public static class Patch_SkillRecord_ExposeData
    {
        [HarmonyPostfix]
        public static void Postfix(SkillRecord __instance)
        {
            double xp = GrandmasterStore.Get(__instance);
            Scribe_Values.Look(ref xp, "grandmasterXp", 0.0, false);
            if (Scribe.mode == LoadSaveMode.LoadingVars && xp > 0.0)
            {
                GrandmasterStore.Set(__instance, xp);
            }
        }
    }
}
