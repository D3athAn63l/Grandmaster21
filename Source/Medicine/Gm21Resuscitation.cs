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
        BrainDestroyed,
        VitalAnatomyUnrebuildable,
        Deteriorated,
        TooDecayed
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
        /// <summary>
        /// Hostile to the colony. NOT a refusal: the pawn is revived and stays hostile; the order
        /// asks the player to confirm first.
        /// </summary>
        public bool hostile;
        /// <summary>The race has consciousness-source anatomy and none of it remains. A hard boundary.</summary>
        public bool brainDestroyed;
        /// <summary>Destroyed vital parts the revival must rebuild for the body to live (0: none).</summary>
        public int vitalRebuilds;
        /// <summary>Some vital anatomy is entirely gone and cannot be rebuilt minimally and safely.</summary>
        public bool vitalUnrebuildable;
        /// <summary>Vanilla rot stage of the corpse (Fresh / Rotting / Dessicated).</summary>
        public RotStage rotStage;
        /// <summary>
        /// The corpse's CompRottable.RotProgress: biological decay, not time. Positive infinity when
        /// the corpse has no rot comp at all -- decay that cannot be judged is not recoverable.
        /// </summary>
        public float rotProgress;
        /// <summary>
        /// The Grandmaster has begun the timed work on a body that was recoverable at that moment.
        /// The procedure is committed: decay no longer fails it.
        /// </summary>
        public bool decayCommitted;
    }

    /// <summary>
    /// Grandmaster Resuscitation: viability policy and the revival itself.
    ///
    /// VIABILITY (centralised in Decide):
    ///   * a real corpse of a flesh pawn that is actually dead, spawned, and not discarded;
    ///   * not a mechanoid, Anomaly entity, mutant or unnatural corpse -- this is medicine;
    ///   * the race's consciousness anatomy (ConsciousnessSource, by TAG, never a body-part name) is not
    ///     entirely destroyed. This is the HARD BOUNDARY: the substrate that carried the person is
    ///     gone, and it is never rebuilt;
    ///   * any other vital anatomy that is entirely gone can be rebuilt minimally and safely (see
    ///     PlanVitalRebuild); a body where it cannot is refused;
    ///   * the body is biologically recoverable: vanilla rot stage Fresh AND its RotProgress no more
    ///     than Gm21Medicine.MaxResuscitationRotProgress. Decay, not time: refrigeration slows it,
    ///     freezing stops it, so a chronologically old corpse can be medically fresh. Checked when
    ///     the order is given and again when the work begins; once the work has begun on a
    ///     recoverable body the procedure is committed and decay never fails it. The Grandmaster
    ///     works on the corpse wherever it lies -- a battlefield, a freezer, a hospital.
    /// Hostility is NOT a refusal. A hostile pawn is revived exactly as vanilla revives one (the
    /// same TryResurrect the resurrector mech serum uses): it keeps its faction and, on a map,
    /// vanilla gives it an assault lord. The order asks the player to confirm first.
    ///
    /// REVIVAL:
    ///   1. snapshot the fresh injuries and plan the minimum vital rebuild -- the evidence is read
    ///      before anything changes the body;
    ///   2. rank the death trauma (Gm21Trauma) from that snapshot;
    ///   3. rebuild only the planned vital parts, with vanilla's RestorePart, so the engine's own
    ///      "would still be dead" safety net never has cause to fire;
    ///   4. vanilla ResurrectionUtility.TryResurrect with restoreMissingParts = false and no scar
    ///      roll -- NOT TryResurrectWithSideEffects: no resurrection sickness, dementia, blindness or
    ///      psychosis lottery. Non-vital missing anatomy stays missing (Reconstruct is for that);
    ///   5. restore the snapshotted fresh wounds (smallest first, each only if it cannot kill), each
    ///      tended as the Grandmaster's no-medicine care so it stops bleeding;
    ///   6. turn up to three of the worst traumatic locations into vanilla permanent scars;
    ///   7. close anything still open, fresh stumps included;
    ///   8. apply Grandmaster Resuscitation Shock (see ApplyShock).
    ///
    /// ENGINE CLEANUP, accepted and documented: Pawn_HealthTracker.Notify_Resurrected removes
    /// immunizable diseases, curable conditions that are lethal or life-threatening, and defs
    /// flagged forceRemoveOnResurrection -- so an unrelated malaria can vanish too. Re-creating
    /// arbitrary, possibly modded, conditions afterwards is not safe, and no cause-of-death analyser
    /// is attempted. The permanent death-trauma scars are the intended lasting cost.
    /// </summary>
    public static class Gm21Resuscitation
    {
        /// <summary>
        /// Every non-consciousness tag a lethal flesh capacity reads. Tags, never body-part names.
        ///
        /// These are exactly the tags the 1.6 capacity workers of the four non-consciousness lethal
        /// flesh capacities read (PawnCapacityDef.lethalFlesh: BloodPumping, Breathing,
        /// BloodFiltration, Metabolism). Each worker multiplies or averages tag efficiencies, so a
        /// capacity reaches zero -- and vanilla's ShouldBeDead fires -- when every part carrying one
        /// of its tags is gone. Consciousness is separate: it is the hard boundary ("Brain
        /// destroyed"), never rebuilt. Verified against every vanilla BodyDef, DLCs included.
        /// </summary>
        public static BodyPartTagDef[] AllVitalTags
        {
            get
            {
                return new[]
                {
                    BodyPartTagDefOf.BloodPumpingSource,    // BloodPumping
                    BodyPartTagDefOf.BreathingSource,       // Breathing = source x pathway x cage
                    BodyPartTagDefOf.BreathingPathway,
                    BodyPartTagDefOf.BreathingSourceCage,
                    BodyPartTagDefOf.BloodFiltrationSource, // BloodFiltration = kidney x liver, or source
                    BodyPartTagDefOf.BloodFiltrationKidney,
                    BodyPartTagDefOf.BloodFiltrationLiver,
                    BodyPartTagDefOf.MetabolismSource       // Metabolism
                };
            }
        }

        /// <summary>
        /// The vital tags this body actually depends on, mirroring the capacity workers: blood
        /// filtration reads kidney x liver when the body has kidneys, and its own source tag
        /// otherwise -- never both.
        /// </summary>
        public static List<BodyPartTagDef> RequiredVitalTags(BodyDef body)
        {
            List<BodyPartTagDef> tags = new List<BodyPartTagDef>
            {
                BodyPartTagDefOf.BloodPumpingSource,
                BodyPartTagDefOf.BreathingSource,
                BodyPartTagDefOf.BreathingPathway,
                BodyPartTagDefOf.BreathingSourceCage
            };
            if (body != null && body.HasPartWithTag(BodyPartTagDefOf.BloodFiltrationKidney))
            {
                tags.Add(BodyPartTagDefOf.BloodFiltrationKidney);
                tags.Add(BodyPartTagDefOf.BloodFiltrationLiver);
            }
            else
            {
                tags.Add(BodyPartTagDefOf.BloodFiltrationSource);
            }
            tags.Add(BodyPartTagDefOf.MetabolismSource);
            return tags;
        }

        // ---------------------------------------------------------------- pure policy

        /// <summary>The whole viability policy, in order. Pure over the facts.</summary>
        public static Gm21ResuscitationVerdict Decide(Gm21ResuscitationFacts f)
        {
            if (!f.isCorpse) return Gm21ResuscitationVerdict.NotACorpse;
            if (!f.available) return Gm21ResuscitationVerdict.Unavailable;
            if (!f.isFlesh) return Gm21ResuscitationVerdict.NotFlesh;
            if (f.supernatural) return Gm21ResuscitationVerdict.Supernatural;
            if (f.brainDestroyed) return Gm21ResuscitationVerdict.BrainDestroyed;
            if (f.vitalUnrebuildable) return Gm21ResuscitationVerdict.VitalAnatomyUnrebuildable;
            if (f.decayCommitted) return Gm21ResuscitationVerdict.Viable;
            if (f.rotStage != RotStage.Fresh) return Gm21ResuscitationVerdict.Deteriorated;
            if (!WithinDecayLimit(f.rotProgress)) return Gm21ResuscitationVerdict.TooDecayed;
            return Gm21ResuscitationVerdict.Viable;
        }

        /// <summary>Whether this much decay is still recoverable. Not-a-number and infinity are not.</summary>
        public static bool WithinDecayLimit(float rotProgress)
        {
            return !float.IsNaN(rotProgress) && rotProgress <= Gm21Medicine.MaxResuscitationRotProgress;
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
                case Gm21ResuscitationVerdict.BrainDestroyed: return "GM21_Med_ResBrain".Translate();
                case Gm21ResuscitationVerdict.VitalAnatomyUnrebuildable: return "GM21_Med_ResVital".Translate();
                case Gm21ResuscitationVerdict.Deteriorated: return "GM21_Med_ResDeteriorated".Translate();
                default: return "GM21_Med_ResDecayed".Translate();
            }
        }

        // ---------------------------------------------------------------- facts

        /// <summary>
        /// Gathers facts from a corpse. <paramref name="decayCommitted"/>: the Grandmaster has already
        /// begun the work on a recoverable body, so decay is reported but no longer decides.
        /// Never throws: an unreadable modded corpse is reported as Unavailable.
        /// </summary>
        public static Gm21ResuscitationFacts Gather(Thing thing, bool decayCommitted)
        {
            Gm21ResuscitationFacts f = default(Gm21ResuscitationFacts);
            f.rotStage = RotStage.Fresh;
            f.decayCommitted = decayCommitted;
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
                List<Gm21VitalRebuild> rebuild = new List<Gm21VitalRebuild>();
                f.vitalUnrebuildable = !PlanVitalRebuild(set, body, rebuild);
                f.vitalRebuilds = rebuild.Count;

                ReadDecay(corpse, out f.rotProgress, out f.rotStage);
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

        /// <summary>
        /// MINIMUM VIABLE RECONSTRUCTION. For every vital tag this body depends on and has entirely
        /// lost, choose ONE destroyed part to rebuild -- the part covering the most lost tags, then
        /// the first in body order -- so the capacity is no longer zero. Nothing else is chosen: a
        /// destroyed heart is rebuilt, the arm and leg lost in the same fight stay missing.
        ///
        /// A part qualifies only if rebuilding it touches nothing else: its parent is present (a
        /// part lost only because its parent was lost would drag the parent's whole subtree back),
        /// and no ancestor carries a bionic or other added part. The rebuild is vanilla's own
        /// RestorePart on that one part (with its own sub-parts, if a modded race gives the organ any).
        ///
        /// Returns false -- the corpse cannot be resuscitated -- if some lost vital tag has no such
        /// part. Consciousness anatomy is never planned here: its loss is the hard boundary.
        /// </summary>
        public static bool PlanVitalRebuild(HediffSet set, BodyDef body, List<Gm21VitalRebuild> plan)
        {
            plan.Clear();
            if (set == null || body == null) return true;
            List<BodyPartTagDef> lost = new List<BodyPartTagDef>();
            List<BodyPartTagDef> required = RequiredVitalTags(body);
            for (int i = 0; i < required.Count; i++)
            {
                if (required[i] != null && !lost.Contains(required[i]) && TagEntirelyMissing(set, body, required[i]))
                    lost.Add(required[i]);
            }

            List<BodyPartRecord> parts = body.AllParts;
            while (lost.Count > 0)
            {
                BodyPartRecord best = null;
                int bestCovers = 0;
                for (int i = 0; i < parts.Count; i++)
                {
                    BodyPartRecord part = parts[i];
                    int covers = CountTags(part, lost);
                    if (covers <= bestCovers || !IsSafeRebuild(set, part)) continue;
                    best = part;
                    bestCovers = covers;
                }
                if (best == null) return false;

                plan.Add(new Gm21VitalRebuild { part = best, lastInjury = LastInjuryOn(set, best) });
                for (int i = lost.Count - 1; i >= 0; i--)
                {
                    if (best.def.tags.Contains(lost[i])) lost.RemoveAt(i);
                }
            }
            return true;
        }

        private static int CountTags(BodyPartRecord part, List<BodyPartTagDef> tags)
        {
            if (part.def == null || part.def.tags == null) return 0;
            int n = 0;
            for (int i = 0; i < tags.Count; i++)
            {
                if (part.def.tags.Contains(tags[i])) n++;
            }
            return n;
        }

        /// <summary>A destroyed part whose rebuild touches nothing but itself.</summary>
        private static bool IsSafeRebuild(HediffSet set, BodyPartRecord part)
        {
            if (!set.PartIsMissing(part)) return false;
            if (part.parent != null && set.PartIsMissing(part.parent)) return false;
            return !set.PartOrAnyAncestorHasDirectlyAddedParts(part);
        }

        private static HediffDef LastInjuryOn(HediffSet set, BodyPartRecord part)
        {
            List<Hediff> hediffs = set.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff_MissingPart missing = hediffs[i] as Hediff_MissingPart;
                if (missing != null && missing.Part == part) return missing.lastInjury;
            }
            return null;
        }

        /// <summary>
        /// The corpse's decay, read from vanilla's own CompRottable: its RotProgress and rot stage.
        /// No rot comp: RotProgress is positive infinity (not recoverable) and the stage Fresh.
        /// </summary>
        public static void ReadDecay(ThingWithComps corpse, out float rotProgress, out RotStage stage)
        {
            CompRottable rot = corpse == null ? null : corpse.GetComp<CompRottable>();
            if (rot == null)
            {
                rotProgress = float.PositiveInfinity;
                stage = RotStage.Fresh;
                return;
            }
            rotProgress = rot.RotProgress;
            stage = rot.Stage;
        }

        public static Gm21ResuscitationVerdict Evaluate(Thing thing, bool decayCommitted)
        {
            return Decide(Gather(thing, decayCommitted));
        }

        /// <summary>
        /// The validator the UI calls when the order is given (decay decides). The UI only displays
        /// its answer.
        /// </summary>
        public static bool CanResuscitate(Corpse corpse, out string reason)
        {
            return CanResuscitate(corpse, false, out reason);
        }

        /// <summary>
        /// The validator the job calls: before the work, decay decides; once the work has begun on a
        /// recoverable body (<paramref name="decayCommitted"/>), only the structural checks remain.
        /// </summary>
        public static bool CanResuscitate(Corpse corpse, bool decayCommitted, out string reason)
        {
            Gm21ResuscitationVerdict verdict = Evaluate(corpse, decayCommitted);
            reason = ReasonFor(verdict);
            return verdict == Gm21ResuscitationVerdict.Viable;
        }

        // ---------------------------------------------------------------- revival

        /// <summary>
        /// Performs the revival. The caller has already validated viability. Returns false if it
        /// could not be done; the corpse is then left as it was.
        /// </summary>
        public static bool TryResuscitate(Pawn doctor, Corpse corpse, out Gm21ResuscitationOutcome outcome)
        {
            outcome = new Gm21ResuscitationOutcome();
            Pawn pawn = corpse.InnerPawn;
            if (pawn == null) return false;
            HediffSet set = pawn.health.hediffSet;

            // ---- the evidence, read from the body before anything changes it
            List<Gm21InjurySnapshot> injuries = SnapshotFreshInjuries(pawn);
            List<Gm21VitalRebuild> rebuilds = new List<Gm21VitalRebuild>();
            if (!PlanVitalRebuild(set, pawn.RaceProps.body, rebuilds))
            {
                Log.Warning("[Grandmaster 21] Medicine 21: " + pawn.ToStringSafe() + " has destroyed vital "
                            + "anatomy that cannot be rebuilt minimally; resuscitation was not attempted.");
                return false;
            }
            BodyPartRecord deathBlow = Gm21Trauma.TryFindDeathBlowPart(pawn, corpse.timeOfDeath);
            List<Gm21TraumaEvidence> evidence = Gm21Trauma.Collect(pawn, injuries, rebuilds, deathBlow);
            List<int> chosen = Gm21Trauma.Rank(evidence, Gm21Medicine.MaxTraumaScars);

            // ---- minimum viable reconstruction, before the engine judges the body
            int missingBefore = CountMissingParts(pawn);
            int rebuiltMarkers = 0;
            for (int i = 0; i < rebuilds.Count; i++)
            {
                rebuiltMarkers += CountMissingInSubtree(set, rebuilds[i].part);
                pawn.health.RestorePart(rebuilds[i].part, null, false);
            }

            ResurrectionParams parms = new ResurrectionParams
            {
                restoreMissingParts = false,
                gettingScarsChance = 0f,
                removeDiedThoughts = true
            };
            if (!ResurrectionUtility.TryResurrect(pawn, parms))
            {
                RollBackRebuilds(pawn, rebuilds);
                return false;
            }

            if (pawn.Dead)
            {
                Log.Error("[Grandmaster 21] Medicine 21: " + pawn.ToStringSafe()
                          + " was still dead after vanilla revival; nothing further was changed.");
                return false;
            }

            if (CountMissingParts(pawn) < missingBefore - rebuiltMarkers)
            {
                // Vanilla's revival has a last-resort branch: if the revived body would still be
                // dead, it deletes every hediff, missing parts included. The minimum-reconstruction
                // plan exists to keep that from ever firing; if it did, say so once rather than hide it.
                Log.Warning("[Grandmaster 21] Medicine 21: vanilla's revival safety net restored missing "
                            + "anatomy while resuscitating " + pawn.ToStringSafe() + ". The vital "
                            + "reconstruction plan did not predict this body; please report the race/mod involved.");
            }

            for (int i = 0; i < rebuilds.Count; i++) outcome.rebuilt.Add(rebuilds[i].part);

            RestoreInjuries(doctor, pawn, injuries, out outcome.woundsRestored, out outcome.woundsClosed);

            // ---- death trauma: up to three permanent scars, each on a real traumatic location
            for (int i = 0; i < chosen.Count && !pawn.Dead; i++)
            {
                Gm21TraumaEvidence e = evidence[chosen[i]];
                Hediff_Injury scar = Gm21Trauma.ApplyScar(pawn, e, SourceFor(injuries, e));
                if (scar != null) outcome.scars.Add(scar);
            }

            TendRemaining(doctor, pawn);

            // ---- alive and stabilised, but not getting up: Grandmaster Resuscitation Shock
            if (!pawn.Dead) outcome.shock = ApplyShock(pawn);
            return !pawn.Dead;
        }

        /// <summary>
        /// Grandmaster Resuscitation Shock: the revived pawn stays unconscious -- downed, unable to
        /// move, work or fight, but carriable and able to rest in bed -- for exactly
        /// Gm21Medicine.ResuscitationShockTicks, then wakes. A GM21 HediffDef on vanilla's psychic-coma
        /// pattern (Consciousness ceiling 0.1 plus a Disappears timer), never vanilla's resurrection
        /// sickness and never a side-effect lottery. Faction and hostility are untouched: a revived
        /// raider is a downed raider. Returns the shock, or null if it could not be applied safely.
        /// </summary>
        public static Hediff ApplyShock(Pawn pawn)
        {
            HediffDef def = Gm21MedicineDefOf.GM21_ResuscitationShock;
            if (def == null || pawn == null || pawn.Dead || pawn.health == null) return null;
            Hediff shock = HediffMaker.MakeHediff(def, pawn);
            HediffComp_Disappears timer = shock.TryGetComp<HediffComp_Disappears>();
            if (timer != null) timer.SetDuration(Gm21Medicine.ResuscitationShockTicks);
            if (pawn.health.WouldDieAfterAddingHediff(shock))
            {
                // A ceiling cannot bring a living consciousness to zero, so this should never fire.
                Log.Warning("[Grandmaster 21] Medicine 21: resuscitation shock was not applied to "
                            + pawn.ToStringSafe() + " because vanilla predicted it would be fatal.");
                return null;
            }
            pawn.health.AddHediff(shock);
            return pawn.health.hediffSet.hediffs.Contains(shock) ? shock : pawn.health.hediffSet.GetFirstHediffOfDef(def);
        }

        /// <summary>Exactly the fresh injuries vanilla's revival erases, smallest first.</summary>
        public static List<Gm21InjurySnapshot> SnapshotFreshInjuries(Pawn pawn)
        {
            List<Gm21InjurySnapshot> list = new List<Gm21InjurySnapshot>();
            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff_Injury injury = hediffs[i] as Hediff_Injury;
                // Exactly vanilla's class and exactly what Notify_Resurrected is about to erase.
                if (injury == null || injury.GetType() != typeof(Hediff_Injury)) continue;
                if (!injury.def.everCurableByItem || injury.IsPermanent() || injury.Part == null) continue;
                list.Add(Gm21InjurySnapshot.Of(injury));
            }
            list.Sort((a, b) => a.severity.CompareTo(b.severity));
            return list;
        }

        /// <summary>The worst snapshot wound of the scar's type at the scar's location, for provenance.</summary>
        private static Gm21InjurySnapshot? SourceFor(List<Gm21InjurySnapshot> injuries, Gm21TraumaEvidence e)
        {
            Gm21InjurySnapshot? best = null;
            for (int i = 0; i < injuries.Count; i++)
            {
                Gm21InjurySnapshot s = injuries[i];
                if (s.part != e.part || s.def != e.scarDef) continue;
                if (!best.HasValue || s.severity > best.Value.severity) best = s;
            }
            return best;
        }

        private static void RestoreInjuries(Pawn doctor, Pawn pawn, List<Gm21InjurySnapshot> injuries,
            out int restored, out int closed)
        {
            restored = 0;
            closed = 0;
            // The doctor's own vanilla no-medicine tend quality (clamped to vanilla's 70% ceiling).
            float quality = TendUtility.CalculateBaseTendQuality(doctor, pawn, null);
            for (int i = 0; i < injuries.Count; i++)
            {
                Gm21InjurySnapshot s = injuries[i];
                if (s.def == null || s.part == null || pawn.health.hediffSet.PartIsMissing(s.part)
                    || s.severity <= 0f)
                {
                    continue;
                }
                Hediff_Injury injury = HediffMaker.MakeHediff(s.def, pawn, s.part) as Hediff_Injury;
                if (injury == null) { closed++; continue; }
                injury.Severity = s.severity;
                s.CopyProvenanceTo(injury);

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
        }

        /// <summary>
        /// Belt and braces, and the stumps: vanilla revival leaves no fresh injuries, so a restored
        /// wound has nothing to merge into -- but if one ever did merge, the survivor must not be
        /// left untended and bleeding. A limb lost in the fatal fight stays lost, and its fresh
        /// stump is closed here the way a tend closes it.
        /// </summary>
        private static void TendRemaining(Pawn doctor, Pawn pawn)
        {
            if (pawn.Dead) return;
            float quality = TendUtility.CalculateBaseTendQuality(doctor, pawn, null);
            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = hediffs.Count - 1; i >= 0; i--)
            {
                Hediff h = hediffs[i];
                Hediff_Injury injury = h as Hediff_Injury;
                if (injury != null)
                {
                    if (injury.IsPermanent() || injury.IsTended()) continue;
                    injury.Tended(quality, TendUtility.NoMedicineQualityMax, 1);
                    continue;
                }
                Hediff_MissingPart stump = h as Hediff_MissingPart;
                if (stump != null && stump.TendableNow()) stump.Tended(quality, TendUtility.NoMedicineQualityMax, 1);
            }
        }

        /// <summary>Puts rebuilt vital parts back as destroyed if vanilla refused the revival.</summary>
        private static void RollBackRebuilds(Pawn pawn, List<Gm21VitalRebuild> rebuilds)
        {
            for (int i = 0; i < rebuilds.Count; i++)
            {
                Hediff_MissingPart missing = HediffMaker.MakeHediff(HediffDefOf.MissingBodyPart, pawn, rebuilds[i].part)
                    as Hediff_MissingPart;
                if (missing == null) continue;
                missing.lastInjury = rebuilds[i].lastInjury;
                pawn.health.hediffSet.AddDirect(missing);
            }
        }

        private static int CountMissingInSubtree(HediffSet set, BodyPartRecord root)
        {
            int n = 0;
            List<Hediff> hediffs = set.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff_MissingPart missing = hediffs[i] as Hediff_MissingPart;
                if (missing == null || missing.Part == null) continue;
                for (BodyPartRecord p = missing.Part; p != null; p = p.parent)
                {
                    if (p == root) { n++; break; }
                }
            }
            return n;
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

    /// <summary>One destroyed vital part the revival rebuilds, and the wound type that destroyed it.</summary>
    public struct Gm21VitalRebuild
    {
        public BodyPartRecord part;
        public HediffDef lastInjury;
    }

    /// <summary>A fresh injury as it was on the corpse, with its provenance.</summary>
    public struct Gm21InjurySnapshot
    {
        public HediffDef def;
        public BodyPartRecord part;
        public float severity;
        public ThingDef sourceDef;
        public BodyPartGroupDef sourceBodyPartGroup;
        public HediffDef sourceHediffDef;
        public string sourceLabel;
        public string sourceToolLabel;
        public string combatLogText;
        public Verse.WeakReference<LogEntry> combatLogEntry;

        public static Gm21InjurySnapshot Of(Hediff_Injury injury)
        {
            return new Gm21InjurySnapshot
            {
                def = injury.def,
                part = injury.Part,
                severity = injury.Severity,
                sourceDef = injury.sourceDef,
                sourceBodyPartGroup = injury.sourceBodyPartGroup,
                sourceHediffDef = injury.sourceHediffDef,
                sourceLabel = injury.sourceLabel,
                sourceToolLabel = injury.sourceToolLabel,
                combatLogText = injury.combatLogText,
                combatLogEntry = injury.combatLogEntry
            };
        }

        public void CopyProvenanceTo(Hediff_Injury injury)
        {
            injury.sourceDef = sourceDef;
            injury.sourceBodyPartGroup = sourceBodyPartGroup;
            injury.sourceHediffDef = sourceHediffDef;
            injury.sourceLabel = sourceLabel;
            injury.sourceToolLabel = sourceToolLabel;
            injury.combatLogText = combatLogText;
            injury.combatLogEntry = combatLogEntry;
        }
    }

    /// <summary>What a resuscitation did, for the result message and dev reports.</summary>
    public sealed class Gm21ResuscitationOutcome
    {
        public int woundsRestored;
        public int woundsClosed;
        /// <summary>Vital parts rebuilt so the body could live.</summary>
        public readonly List<BodyPartRecord> rebuilt = new List<BodyPartRecord>();
        /// <summary>The permanent death-trauma scars, best first.</summary>
        public readonly List<Hediff_Injury> scars = new List<Hediff_Injury>();
        /// <summary>The Resuscitation Shock, or null if it could not be applied.</summary>
        public Hediff shock;
    }
}
