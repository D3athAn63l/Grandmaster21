using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// The evidence one body LOCATION carries of the death: the trauma ranker's only input.
    /// Built from the corpse's own health state; the battle log adds at most one flag.
    /// </summary>
    public struct Gm21TraumaEvidence
    {
        /// <summary>The location. Null only in pure tests.</summary>
        public BodyPartRecord part;

        /// <summary>The part's index in its body: deterministic tie-break, identity in tests.</summary>
        public int partIndex;

        /// <summary>Summed severity of the fresh injuries at this location when the pawn died.</summary>
        public float severity;

        /// <summary>The part's max health for this pawn.</summary>
        public float partMaxHealth;

        /// <summary>Destroyed in the death and rebuilt so the pawn could live again.</summary>
        public bool destroyed;

        /// <summary>The part carries a lethal-capacity tag (consciousness, blood pumping, breathing, ...).</summary>
        public bool vital;

        /// <summary>The battle log names this part as the target of the blow that killed the pawn.</summary>
        public bool lethalBlow;

        /// <summary>A vanilla permanent-injury def is available to scar this location with.</summary>
        public bool scarrable;

        /// <summary>The injury def the scar would take (the location's worst scarrable wound).</summary>
        public HediffDef scarDef;

        /// <summary>Severity of that worst wound (the scar is never heavier than it).</summary>
        public float scarSourceSeverity;
    }

    /// <summary>
    /// Death trauma: which locations of a resuscitated body carry a permanent scar.
    ///
    /// The Grandmaster is not rewinding time. They make a ruined body live again, and the body
    /// keeps evidence of what killed it: up to three of the worst traumatic locations become
    /// ordinary vanilla permanent injuries (HediffComp_GetsPermanent) of the wound type that was
    /// actually there -- an old gunshot where the bullet went in, a scarred heart where the heart
    /// had to be rebuilt.
    ///
    /// PRIMARY SOURCE is the corpse's own health state, snapshotted before revival: every fresh
    /// injury, its severity, its body part and that part's max health, its source metadata, and the
    /// destroyed vital anatomy the revival has to rebuild. The BATTLE LOG is never required: it
    /// only adds a bonus to the location of the death blow when a death entry still exists, and the
    /// injury's own combat-log link is carried onto the scar as provenance.
    ///
    /// NOT scar material: missing non-vital anatomy (it stays missing and does not use a slot --
    /// Reconstruct is for that), bruises and other wound types vanilla never scars, parts under
    /// a bionic or other added part, and pawns whose genes prevent permanent wounds.
    /// </summary>
    public static class Gm21Trauma
    {
        // ---------------------------------------------------------------- pure ranking

        public static float RelativeSeverity(Gm21TraumaEvidence e)
        {
            if (e.destroyed) return 1f;
            if (e.partMaxHealth <= 0f || e.severity <= 0f || float.IsNaN(e.severity)) return 0f;
            return Mathf.Min(1f, e.severity / e.partMaxHealth);
        }

        /// <summary>The documented score (see Gm21Medicine, "resuscitation trauma").</summary>
        public static float Score(Gm21TraumaEvidence e)
        {
            float score = RelativeSeverity(e);
            if (e.destroyed) score += Gm21Medicine.DestroyedTraumaBonus;
            if (e.vital) score += Gm21Medicine.VitalTraumaBonus;
            if (e.lethalBlow) score += Gm21Medicine.LethalBlowTraumaBonus;
            return score;
        }

        /// <summary>Legitimate trauma that can carry a vanilla scar. Nothing else is ever scarred.</summary>
        public static bool Qualifies(Gm21TraumaEvidence e)
        {
            if (!e.scarrable) return false;
            return e.destroyed || e.lethalBlow
                   || RelativeSeverity(e) >= Gm21Medicine.MinTraumaRelativeSeverity - 0.0001f;
        }

        /// <summary>
        /// Indices of the locations to scar, best first: qualifying only, one per location, at most
        /// <paramref name="max"/>. Order: score, then relative severity, then body order. Pure.
        /// </summary>
        public static List<int> Rank(IList<Gm21TraumaEvidence> evidence, int max)
        {
            List<int> order = new List<int>();
            for (int i = 0; i < evidence.Count; i++)
            {
                if (Qualifies(evidence[i])) order.Add(i);
            }
            order.Sort(delegate(int a, int b)
            {
                Gm21TraumaEvidence ea = evidence[a], eb = evidence[b];
                int c = Score(eb).CompareTo(Score(ea));
                if (c != 0) return c;
                c = RelativeSeverity(eb).CompareTo(RelativeSeverity(ea));
                if (c != 0) return c;
                c = ea.partIndex.CompareTo(eb.partIndex);
                return c != 0 ? c : a.CompareTo(b);
            });

            List<int> chosen = new List<int>();
            HashSet<int> locations = new HashSet<int>();
            for (int i = 0; i < order.Count && chosen.Count < max; i++)
            {
                if (!locations.Add(evidence[order[i]].partIndex)) continue; // one scar per location
                chosen.Add(order[i]);
            }
            return chosen;
        }

        /// <summary>
        /// Scar severity: the wound's own severity capped at MaxScarPartFraction of the part (a
        /// rebuilt vital part: RebuiltVitalScarFraction of it). Never heavier than the wound was.
        /// </summary>
        public static float ScarSeverity(Gm21TraumaEvidence e)
        {
            if (e.partMaxHealth <= 0f) return 0f;
            if (e.destroyed) return e.partMaxHealth * Gm21Medicine.RebuiltVitalScarFraction;
            return Mathf.Min(e.scarSourceSeverity, e.partMaxHealth * Gm21Medicine.MaxScarPartFraction);
        }

        // ---------------------------------------------------------------- the body

        /// <summary>Whether vanilla could ever make this injury def permanent.</summary>
        public static bool IsScarrableDef(HediffDef def)
        {
            return def != null && def.injuryProps != null && def.CompProps<HediffCompProperties_GetsPermanent>() != null;
        }

        /// <summary>Genes that forbid permanent wounds (Biotech) forbid death scars too.</summary>
        public static bool PawnCanScar(Pawn pawn)
        {
            if (pawn == null || pawn.genes == null) return true;
            List<Gene> genes = pawn.genes.GenesListForReading;
            for (int i = 0; i < genes.Count; i++)
            {
                if (genes[i] != null && genes[i].def != null && genes[i].def.preventPermanentWounds) return false;
            }
            return true;
        }

        /// <summary>Whether any of this part's tags feeds a lethal flesh capacity.</summary>
        public static bool IsVitalPart(BodyPartRecord part)
        {
            if (part == null || part.def == null || part.def.tags == null) return false;
            List<BodyPartTagDef> tags = part.def.tags;
            for (int i = 0; i < tags.Count; i++)
            {
                if (tags[i] == BodyPartTagDefOf.ConsciousnessSource) return true;
                if (Array.IndexOf(Gm21Resuscitation.AllVitalTags, tags[i]) >= 0) return true;
            }
            return false;
        }

        /// <summary>
        /// Builds one evidence entry per location from the pre-revival snapshot: every location with
        /// a fresh injury, plus every vital part the revival is about to rebuild.
        /// </summary>
        public static List<Gm21TraumaEvidence> Collect(Pawn pawn, IList<Gm21InjurySnapshot> injuries,
            IList<Gm21VitalRebuild> rebuilds, BodyPartRecord deathBlowPart)
        {
            List<Gm21TraumaEvidence> result = new List<Gm21TraumaEvidence>();
            Dictionary<BodyPartRecord, int> at = new Dictionary<BodyPartRecord, int>();
            bool canScar = PawnCanScar(pawn);
            HediffSet set = pawn.health.hediffSet;

            for (int i = 0; i < rebuilds.Count; i++)
            {
                Gm21VitalRebuild r = rebuilds[i];
                Gm21TraumaEvidence e = NewEvidence(pawn, r.part, deathBlowPart);
                e.destroyed = true;
                e.severity = e.partMaxHealth;
                e.scarDef = r.lastInjury;
                e.scarSourceSeverity = e.partMaxHealth;
                e.scarrable = canScar && IsScarrableDef(r.lastInjury);
                at[r.part] = result.Count;
                result.Add(e);
            }

            for (int i = 0; i < injuries.Count; i++)
            {
                Gm21InjurySnapshot s = injuries[i];
                if (s.part == null || s.severity <= 0f) continue;
                int index;
                if (!at.TryGetValue(s.part, out index))
                {
                    Gm21TraumaEvidence fresh = NewEvidence(pawn, s.part, deathBlowPart);
                    index = result.Count;
                    at[s.part] = index;
                    result.Add(fresh);
                }
                Gm21TraumaEvidence e = result[index];
                if (e.destroyed) continue; // a rebuilt part's evidence is its destruction
                e.severity += s.severity;
                bool scarrable = canScar && IsScarrableDef(s.def) && !set.PartOrAnyAncestorHasDirectlyAddedParts(s.part);
                if (scarrable && (!e.scarrable || s.severity > e.scarSourceSeverity))
                {
                    e.scarrable = true;
                    e.scarDef = s.def;
                    e.scarSourceSeverity = s.severity;
                }
                result[index] = e;
            }
            return result;
        }

        private static Gm21TraumaEvidence NewEvidence(Pawn pawn, BodyPartRecord part, BodyPartRecord deathBlowPart)
        {
            return new Gm21TraumaEvidence
            {
                part = part,
                partIndex = part.Index,
                partMaxHealth = part.def.GetMaxHealth(pawn),
                vital = IsVitalPart(part),
                lethalBlow = deathBlowPart != null && deathBlowPart == part
            };
        }

        /// <summary>
        /// Turns one chosen location into a vanilla permanent injury on the revived pawn. The
        /// location's own restored wound of the scar's type becomes the scar when there is one;
        /// otherwise (the wound was closed because restoring it would have killed, or the part was
        /// rebuilt) a new injury of that type is added -- only if adding it cannot kill. Returns the
        /// scar, or null if none could be made safely.
        /// </summary>
        public static Hediff_Injury ApplyScar(Pawn pawn, Gm21TraumaEvidence e, Gm21InjurySnapshot? source)
        {
            if (!e.scarrable || e.scarDef == null || e.part == null) return null;
            if (pawn.health.hediffSet.PartIsMissing(e.part)) return null;
            float severity = ScarSeverity(e);
            if (severity < Gm21Medicine.MinScarSeverity) return null;

            // Convert the restored wound itself when it is there.
            Hediff_Injury best = null;
            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff_Injury injury = hediffs[i] as Hediff_Injury;
                if (injury == null || injury.Part != e.part || injury.def != e.scarDef || injury.IsPermanent()) continue;
                if (best == null || injury.Severity > best.Severity) best = injury;
            }
            if (best != null)
            {
                HediffComp_GetsPermanent comp = best.TryGetComp<HediffComp_GetsPermanent>();
                if (comp == null) return null;
                comp.IsPermanent = true;
                best.Severity = Mathf.Min(best.Severity, severity);
                return best;
            }

            // Otherwise a new permanent wound of the same kind, halved until it is safe: it must not
            // kill, and must not destroy the part again.
            for (int attempt = 0; attempt < 4 && severity >= Gm21Medicine.MinScarSeverity; attempt++, severity *= 0.5f)
            {
                if (pawn.health.WouldLosePartAfterAddingHediff(e.scarDef, e.part, severity)) continue;
                Hediff_Injury scar = HediffMaker.MakeHediff(e.scarDef, pawn, e.part) as Hediff_Injury;
                if (scar == null) return null;
                HediffComp_GetsPermanent comp = scar.TryGetComp<HediffComp_GetsPermanent>();
                if (comp == null) return null;
                scar.Severity = severity;
                if (source.HasValue) source.Value.CopyProvenanceTo(scar);
                // Permanent before it is added, so it can never merge into a fresh wound; the
                // setter's pain roll runs below, once the scar belongs to the pawn.
                comp.isPermanentInt = true;
                if (pawn.health.WouldDieAfterAddingHediff(scar)) continue;
                pawn.health.AddHediff(scar, e.part);
                if (!pawn.health.hediffSet.hediffs.Contains(scar)) return null;
                // Exactly the roll HediffComp_GetsPermanent.IsPermanent's setter makes.
                comp.SetPainCategory(HealthTuning.InjuryPainCategories
                    .RandomElementByWeight(c => c.weight).category);
                return scar;
            }
            return null;
        }

        // ---------------------------------------------------------------- battle log (corroboration only)

        // Private fields of a vanilla log entry, read by reflection once per resuscitation -- never
        // per tick. If a future version renames them, the lookup fails once and the ranking simply
        // proceeds from the body alone.
        private static FieldInfo subjectPawnField;
        private static FieldInfo culpritTargetPartField;
        private static FieldInfo culpritHediffTargetPartField;
        private static bool logUnreadable;

        /// <summary>
        /// The body part the battle log records as the target of the blow that killed this pawn, if
        /// the death entry still exists; null otherwise. Never throws, never required.
        /// </summary>
        public static BodyPartRecord TryFindDeathBlowPart(Pawn pawn, int deathTick)
        {
            if (logUnreadable || pawn == null || Verse.Current.Game == null || Find.BattleLog == null) return null;
            try
            {
                if (subjectPawnField == null)
                {
                    Type type = typeof(BattleLogEntry_StateTransition);
                    culpritTargetPartField = Need(AccessTools.Field(type, "culpritTargetPart"));
                    culpritHediffTargetPartField = Need(AccessTools.Field(type, "culpritHediffTargetPart"));
                    subjectPawnField = Need(AccessTools.Field(type, "subjectPawn"));
                }
                int deathAbs = deathTick + (Find.TickManager.TicksAbs - Find.TickManager.TicksGame);
                List<Battle> battles = Find.BattleLog.Battles;
                for (int b = 0; b < battles.Count; b++)
                {
                    Battle battle = battles[b];
                    if (battle.LastEntryTimestamp < deathAbs - 1 || !battle.Concerns(pawn)) continue;
                    List<LogEntry> entries = battle.Entries;
                    for (int i = entries.Count - 1; i >= 0; i--)
                    {
                        BattleLogEntry_StateTransition death = entries[i] as BattleLogEntry_StateTransition;
                        if (death == null || Math.Abs(death.Tick - deathAbs) > 1) continue;
                        if (!ReferenceEquals(subjectPawnField.GetValue(death), pawn)) continue;
                        BodyPartRecord part = (culpritTargetPartField.GetValue(death) as BodyPartRecord)
                                              ?? (culpritHediffTargetPartField.GetValue(death) as BodyPartRecord);
                        if (part != null) return part;
                    }
                }
            }
            catch (Exception e)
            {
                logUnreadable = true;
                Log.Warning("[Grandmaster 21] Medicine 21: the battle log could not be read for death-trauma "
                            + "corroboration; scars are ranked from the body alone. " + e.Message);
            }
            return null;
        }

        private static FieldInfo Need(FieldInfo field)
        {
            if (field == null) throw new MissingFieldException("BattleLogEntry_StateTransition layout changed");
            return field;
        }
    }
}
