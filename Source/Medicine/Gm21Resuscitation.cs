using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Grandmaster21
{
    /// <summary>Why a corpse can, or cannot, be resuscitated. Ordered as the policy checks them.</summary>
    public enum Gm21ResuscitationVerdict : byte
    {
        Viable = 0,
        NotACorpse,
        Unavailable,
        NotFlesh,
        Supernatural,
        Hostile,
        BrainDestroyed,
        VitalAnatomyDestroyed,
        Deteriorated,
        TooLate
    }

    /// <summary>Everything the viability policy needs, gathered from a corpse once.</summary>
    public struct Gm21ResuscitationFacts
    {
        public bool isCorpse;
        /// <summary>Inner pawn present, dead, not discarded, corpse spawned and not bugged.</summary>
        public bool available;
        public bool isFlesh;
        /// <summary>Mechanoid, Anomaly entity, mutant or an unnatural corpse.</summary>
        public bool supernatural;
        public bool hostile;
        /// <summary>The race has consciousness-source anatomy and none of it remains.</summary>
        public bool brainDestroyed;
        /// <summary>Some other vital-capacity anatomy the race has is entirely gone.</summary>
        public bool vitalAnatomyDestroyed;
        public RotStage rotStage;
        /// <summary>Ticks between death and the reference tick (now, or when the work began).</summary>
        public int ticksSinceDeath;
    }

    /// <summary>
    /// Grandmaster Resuscitation: viability policy and the revival itself.
    ///
    /// VIABILITY (first pass, conservative, centralised in Decide):
    ///   * a real corpse of a flesh pawn that is actually dead, spawned, and not discarded;
    ///   * not a mechanoid, Anomaly entity, mutant or unnatural corpse -- this is medicine;
    ///   * not currently hostile to the colony (prisoners of the colony are fine). Vanilla's revival
    ///     path hands a revived hostile pawn a raid lord; that is not a medical outcome;
    ///   * the race's consciousness anatomy is not entirely destroyed ("Brain destroyed") and no
    ///     other vital anatomy the race has (blood pumping, breathing, blood filtration, metabolism --
    ///     by body part TAG, never by name) is entirely missing. Reconstruct restores anatomy on the living;
    ///     it is not smuggled in through revival;
    ///   * the body is still fresh (CompRottable stage), and
    ///   * death was no more than Gm21Medicine.ResuscitationWindowTicks (four hours) ago, measured to
    ///     the moment the Grandmaster begins the work.
    ///
    /// REVIVAL uses vanilla's ResurrectionUtility.TryResurrect -- the engine path that restores map
    /// and world state, removes the corpse and de-registers the world pawn -- with
    /// restoreMissingParts = false and no scar roll. It does NOT use TryResurrectWithSideEffects:
    /// there is no resurrection sickness, dementia, blindness or psychosis roll. This is medicine,
    /// not the serum.
    ///
    /// What the engine's revival does by itself (Pawn_HealthTracker.Notify_Resurrected), and why it
    /// stands: it clears immunizable diseases, conditions that are lethal or life-threatening, and
    /// defs flagged forceRemoveOnResurrection -- i.e. whatever killed the pawn by severity, blood
    /// loss included. Re-creating arbitrary, possibly modded, conditions afterwards is not safe, so
    /// that part of the engine path is accepted as the Grandmaster "addressing the cause of death".
    ///
    /// What is deliberately PRESERVED: the engine also erases every fresh injury, which would make
    /// resuscitation a full heal. Fresh vanilla injuries are snapshotted before revival and restored
    /// afterwards -- smallest first, each only if restoring it cannot kill the pawn again -- and each
    /// is immediately tended as the Grandmaster's no-medicine care (vanilla rules, so it stops
    /// bleeding). A wound skipped because it would be fatal is the one the Grandmaster had to close
    /// to make revival possible. Scars, missing parts, implants and chronic conditions are never
    /// removed by this path in the first place.
    /// </summary>
    public static class Gm21Resuscitation
    {
        /// <summary>
        /// Tags whose total loss makes revival impossible. Tags, not body-part names.
        ///
        /// These are exactly the tags the 1.6 capacity workers of the four non-consciousness lethal
        /// flesh capacities read (PawnCapacityDef.lethalFlesh: BloodPumping, Breathing,
        /// BloodFiltration, Metabolism). Each worker multiplies or averages tag efficiencies, so a
        /// capacity reaches zero -- and vanilla's ShouldBeDead fires -- when every part carrying one
        /// of its tags is gone. Consciousness is checked separately so it can be reported as "Brain
        /// destroyed". Verified against every vanilla BodyDef, DLCs included.
        /// </summary>
        private static BodyPartTagDef[] VitalTags
        {
            get
            {
                return new[]
                {
                    BodyPartTagDefOf.BloodPumpingSource,    // BloodPumping
                    BodyPartTagDefOf.BreathingSource,       // Breathing = source x pathway x cage
                    BodyPartTagDefOf.BreathingPathway,
                    BodyPartTagDefOf.BreathingSourceCage,
                    BodyPartTagDefOf.BloodFiltrationSource, // BloodFiltration = source, or kidney x liver
                    BodyPartTagDefOf.BloodFiltrationKidney,
                    BodyPartTagDefOf.BloodFiltrationLiver,
                    BodyPartTagDefOf.MetabolismSource       // Metabolism
                };
            }
        }

        // ---------------------------------------------------------------- pure policy

        /// <summary>The whole viability policy, in order. Pure over the facts.</summary>
        public static Gm21ResuscitationVerdict Decide(Gm21ResuscitationFacts f)
        {
            if (!f.isCorpse) return Gm21ResuscitationVerdict.NotACorpse;
            if (!f.available) return Gm21ResuscitationVerdict.Unavailable;
            if (!f.isFlesh) return Gm21ResuscitationVerdict.NotFlesh;
            if (f.supernatural) return Gm21ResuscitationVerdict.Supernatural;
            if (f.hostile) return Gm21ResuscitationVerdict.Hostile;
            if (f.brainDestroyed) return Gm21ResuscitationVerdict.BrainDestroyed;
            if (f.vitalAnatomyDestroyed) return Gm21ResuscitationVerdict.VitalAnatomyDestroyed;
            if (f.rotStage != RotStage.Fresh) return Gm21ResuscitationVerdict.Deteriorated;
            if (f.ticksSinceDeath < 0 || f.ticksSinceDeath > Gm21Medicine.ResuscitationWindowTicks)
                return Gm21ResuscitationVerdict.TooLate;
            return Gm21ResuscitationVerdict.Viable;
        }

        public static string ReasonFor(Gm21ResuscitationVerdict verdict)
        {
            switch (verdict)
            {
                case Gm21ResuscitationVerdict.Viable: return null;
                case Gm21ResuscitationVerdict.NotACorpse: return "GM21_Med_ResNotCorpse".Translate();
                case Gm21ResuscitationVerdict.Unavailable: return "GM21_Med_ResUnavailable".Translate();
                case Gm21ResuscitationVerdict.NotFlesh: return "GM21_Med_ResNotFlesh".Translate();
                case Gm21ResuscitationVerdict.Supernatural: return "GM21_Med_ResSupernatural".Translate();
                case Gm21ResuscitationVerdict.Hostile: return "GM21_Med_ResHostile".Translate();
                case Gm21ResuscitationVerdict.BrainDestroyed: return "GM21_Med_ResBrain".Translate();
                case Gm21ResuscitationVerdict.VitalAnatomyDestroyed: return "GM21_Med_ResVital".Translate();
                case Gm21ResuscitationVerdict.Deteriorated: return "GM21_Med_ResDeteriorated".Translate();
                default: return "GM21_Med_ResTooLate".Translate();
            }
        }

        // ---------------------------------------------------------------- facts

        /// <summary>
        /// Gathers facts from a corpse. <paramref name="referenceTick"/> is when the window is
        /// measured to: now for targeting, the work-start tick once the Grandmaster has begun.
        /// Never throws: an unreadable modded corpse is reported as Unavailable.
        /// </summary>
        public static Gm21ResuscitationFacts Gather(Thing thing, int referenceTick)
        {
            Gm21ResuscitationFacts f = default(Gm21ResuscitationFacts);
            f.rotStage = RotStage.Fresh;
            Corpse corpse = thing as Corpse;
            f.isCorpse = corpse != null;
            if (corpse == null) return f;

            try
            {
                Pawn inner = corpse.InnerPawn;
                f.available = inner != null && inner.Dead && !inner.Discarded && !corpse.Destroyed
                              && corpse.Spawned && !corpse.Bugged && inner.health != null
                              && inner.health.hediffSet != null && inner.RaceProps != null;
                if (!f.available) return f;

                RaceProperties race = inner.RaceProps;
                f.isFlesh = race.IsFlesh && !race.IsMechanoid;
                f.supernatural = race.IsAnomalyEntity || inner.IsMutant || corpse is UnnaturalCorpse;
                f.hostile = inner.HostileTo(Faction.OfPlayer) && !inner.IsPrisonerOfColony;

                HediffSet set = inner.health.hediffSet;
                BodyDef body = race.body;
                f.brainDestroyed = TagEntirelyMissing(set, body, BodyPartTagDefOf.ConsciousnessSource);
                BodyPartTagDef[] vital = VitalTags;
                for (int i = 0; i < vital.Length; i++)
                {
                    if (TagEntirelyMissing(set, body, vital[i])) { f.vitalAnatomyDestroyed = true; break; }
                }

                f.rotStage = corpse.GetRotStage();
                f.ticksSinceDeath = referenceTick - corpse.timeOfDeath;
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Grandmaster 21] Medicine 21 could not assess corpse " + thing.ToStringSafe()
                                + " for resuscitation; it is treated as not viable. " + e.Message,
                                thing.thingIDNumber ^ 0x6D656431);
                f.available = false;
            }
            return f;
        }

        /// <summary>
        /// True when the body has at least one part with this tag and every one of them is missing.
        /// A race without the tag at all is not judged by it (it does not rely on that capacity).
        /// </summary>
        public static bool TagEntirelyMissing(HediffSet set, BodyDef body, BodyPartTagDef tag)
        {
            if (set == null || body == null || tag == null) return false;
            List<BodyPartRecord> parts = body.AllParts;
            bool hasTag = false;
            for (int i = 0; i < parts.Count; i++)
            {
                BodyPartRecord part = parts[i];
                if (part.def == null || part.def.tags == null || !part.def.tags.Contains(tag)) continue;
                hasTag = true;
                if (!set.PartIsMissing(part)) return false;
            }
            return hasTag;
        }

        public static Gm21ResuscitationVerdict Evaluate(Thing thing, int referenceTick)
        {
            return Decide(Gather(thing, referenceTick));
        }

        /// <summary>The validator the UI and the job call. The UI only displays its answer.</summary>
        public static bool CanResuscitate(Corpse corpse, out string reason)
        {
            return CanResuscitate(corpse, Find.TickManager.TicksGame, out reason);
        }

        public static bool CanResuscitate(Corpse corpse, int referenceTick, out string reason)
        {
            Gm21ResuscitationVerdict verdict = Evaluate(corpse, referenceTick);
            reason = ReasonFor(verdict);
            return verdict == Gm21ResuscitationVerdict.Viable;
        }

        // ---------------------------------------------------------------- revival

        private struct InjurySnapshot
        {
            public HediffDef def;
            public BodyPartRecord part;
            public float severity;
            public ThingDef sourceDef;
            public BodyPartGroupDef sourceBodyPartGroup;
            public HediffDef sourceHediffDef;
            public string sourceLabel;
            public string sourceToolLabel;
        }

        /// <summary>
        /// Performs the revival. The caller has already validated viability. Returns false, having
        /// changed nothing, if vanilla refuses.
        /// </summary>
        public static bool TryResuscitate(Pawn doctor, Corpse corpse, out int woundsRestored, out int woundsClosed)
        {
            woundsRestored = 0;
            woundsClosed = 0;
            Pawn pawn = corpse.InnerPawn;
            if (pawn == null) return false;

            List<InjurySnapshot> injuries = SnapshotFreshInjuries(pawn);
            int missingBefore = CountMissingParts(pawn);

            ResurrectionParams parms = new ResurrectionParams
            {
                restoreMissingParts = false,
                gettingScarsChance = 0f,
                removeDiedThoughts = true
            };
            if (!ResurrectionUtility.TryResurrect(pawn, parms)) return false;

            if (pawn.Dead)
            {
                Log.Error("[Grandmaster 21] Medicine 21: " + pawn.ToStringSafe()
                          + " was still dead after vanilla revival; nothing further was changed.");
                return false;
            }

            if (CountMissingParts(pawn) < missingBefore)
            {
                // Vanilla's revival has a last-resort branch: if the revived body would still be
                // dead, it deletes every hediff, missing parts included. The viability policy exists
                // to keep that from ever firing; if it did, say so once rather than hide it.
                Log.Warning("[Grandmaster 21] Medicine 21: vanilla's revival safety net restored missing "
                            + "anatomy while resuscitating " + pawn.ToStringSafe() + ". The viability "
                            + "policy did not predict this body; please report the race/mod involved.");
            }

            RestoreInjuries(doctor, pawn, injuries, out woundsRestored, out woundsClosed);
            return true;
        }

        private static List<InjurySnapshot> SnapshotFreshInjuries(Pawn pawn)
        {
            List<InjurySnapshot> list = new List<InjurySnapshot>();
            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff_Injury injury = hediffs[i] as Hediff_Injury;
                // Exactly vanilla's class and exactly what Notify_Resurrected is about to erase.
                if (injury == null || injury.GetType() != typeof(Hediff_Injury)) continue;
                if (!injury.def.everCurableByItem || injury.IsPermanent() || injury.Part == null) continue;
                list.Add(new InjurySnapshot
                {
                    def = injury.def,
                    part = injury.Part,
                    severity = injury.Severity,
                    sourceDef = injury.sourceDef,
                    sourceBodyPartGroup = injury.sourceBodyPartGroup,
                    sourceHediffDef = injury.sourceHediffDef,
                    sourceLabel = injury.sourceLabel,
                    sourceToolLabel = injury.sourceToolLabel
                });
            }
            list.Sort((a, b) => a.severity.CompareTo(b.severity));
            return list;
        }

        private static void RestoreInjuries(Pawn doctor, Pawn pawn, List<InjurySnapshot> injuries,
            out int restored, out int closed)
        {
            restored = 0;
            closed = 0;
            // The doctor's own vanilla no-medicine tend quality (clamped to vanilla's 70% ceiling).
            float quality = TendUtility.CalculateBaseTendQuality(doctor, pawn, null);
            for (int i = 0; i < injuries.Count; i++)
            {
                InjurySnapshot s = injuries[i];
                if (s.def == null || s.part == null || pawn.health.hediffSet.PartIsMissing(s.part)
                    || s.severity <= 0f)
                {
                    continue;
                }
                Hediff_Injury injury = HediffMaker.MakeHediff(s.def, pawn, s.part) as Hediff_Injury;
                if (injury == null) { closed++; continue; }
                injury.Severity = s.severity;
                injury.sourceDef = s.sourceDef;
                injury.sourceBodyPartGroup = s.sourceBodyPartGroup;
                injury.sourceHediffDef = s.sourceHediffDef;
                injury.sourceLabel = s.sourceLabel;
                injury.sourceToolLabel = s.sourceToolLabel;

                if (pawn.health.WouldDieAfterAddingHediff(injury))
                {
                    closed++;
                    continue;
                }
                pawn.health.AddHediff(injury, s.part);
                if (pawn.Dead) break; // cannot happen after the check above; never keep going if it did
                if (pawn.health.hediffSet.hediffs.Contains(injury))
                {
                    injury.Tended(quality, TendUtility.NoMedicineQualityMax, 1);
                }
                restored++;
            }

            // Belt and braces: vanilla revival leaves no fresh injuries, so a restored wound has
            // nothing to merge into -- but if one ever did merge, the survivor must not be left
            // untended and bleeding. Tend whatever fresh injury is still untended.
            if (pawn.Dead) return;
            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = hediffs.Count - 1; i >= 0; i--)
            {
                Hediff_Injury injury = hediffs[i] as Hediff_Injury;
                if (injury == null || injury.IsPermanent() || injury.IsTended()) continue;
                injury.Tended(quality, TendUtility.NoMedicineQualityMax, 1);
            }
        }

        private static int CountMissingParts(Pawn pawn)
        {
            int n = 0;
            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (hediffs[i] is Hediff_MissingPart) n++;
            }
            return n;
        }
    }
}
