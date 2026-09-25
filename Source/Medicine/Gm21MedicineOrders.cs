using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Grandmaster21
{
    /// <summary>
    /// Targeting and ordering for the Grandmaster Medicine gizmo.
    ///
    /// The UI holds NO medical logic: it asks Gm21CureCandidates, Gm21ReconstructCandidates and
    /// Gm21Resuscitation what is valid and displays exactly that, then hands the choice to a job.
    /// Vanilla targeting and a vanilla FloatMenu are the only interface.
    /// </summary>
    public static class Gm21MedicineOrders
    {
        /// <summary>Right-click on the gizmo: start targeting for the doctor's current mode.</summary>
        public static void BeginTargeting(Pawn doctor)
        {
            if (!Gm21Medicine.CanPractise(doctor))
            {
                Reject("GM21_Med_NotPractising".Translate(doctor.LabelShortCap));
                return;
            }

            Gm21MedicineMode mode = Gm21MedicineModeStore.Get(doctor);
            Texture2D icon = Gm21MedicineGizmo.Icon;
            if (mode == Gm21MedicineMode.Resuscitate)
            {
                Find.Targeter.BeginTargeting(CorpseParameters(doctor),
                    delegate(LocalTargetInfo t) { OnCorpseChosen(doctor, t.Thing as Corpse); },
                    doctor, null, icon);
                return;
            }

            Find.Targeter.BeginTargeting(PatientParameters(doctor),
                delegate(LocalTargetInfo t) { OnPatientChosen(doctor, mode, t.Thing as Pawn); },
                doctor, null, icon);
        }

        private static TargetingParameters PatientParameters(Pawn doctor)
        {
            return new TargetingParameters
            {
                canTargetPawns = true,
                canTargetBuildings = false,
                canTargetItems = false,
                canTargetMechs = false,
                canTargetSelf = false,
                mapObjectTargetsMustBeAutoAttackable = false,
                validator = delegate(TargetInfo t)
                {
                    Pawn p = t.Thing as Pawn;
                    string ignored;
                    return p != null && p != doctor && CanTreatPatient(doctor, p, false, out ignored);
                }
            };
        }

        private static TargetingParameters CorpseParameters(Pawn doctor)
        {
            return new TargetingParameters
            {
                canTargetPawns = false,
                canTargetBuildings = false,
                canTargetItems = true,
                canTargetCorpses = true,
                onlyTargetCorpses = true,
                canTargetMechs = false,
                mapObjectTargetsMustBeAutoAttackable = false,
                // Every corpse is selectable, so a non-viable one can explain WHY on click.
                validator = delegate(TargetInfo t) { return t.Thing is Corpse; }
            };
        }

        // ---------------------------------------------------------------- patients

        /// <summary>
        /// Who a Grandmaster may treat with Cure or Reconstruct. Conservative:
        /// a living, spawned flesh pawn on the doctor's map, never the doctor themself, never a
        /// mutant or Anomaly entity, and one the colony is entitled to treat -- its own pawns and
        /// animals, prisoners of the colony, or anyone who is down. A standing hostile, or a
        /// standing visitor under another faction's control, is not a patient.
        /// <paramref name="forOrder"/> additionally checks reachability and reservation.
        /// </summary>
        public static bool CanTreatPatient(Pawn doctor, Pawn patient, bool forOrder, out string reason)
        {
            reason = null;
            if (patient == null || patient.Dead || !patient.Spawned || patient.health == null)
            {
                reason = "GM21_Med_NoPatient".Translate();
                return false;
            }
            if (doctor == null || patient.MapHeld != doctor.MapHeld)
            {
                reason = "GM21_Med_NoPatient".Translate();
                return false;
            }
            if (patient == doctor)
            {
                reason = "GM21_Med_NotSelf".Translate();
                return false;
            }
            if (patient.RaceProps == null || !patient.RaceProps.IsFlesh || patient.RaceProps.IsMechanoid
                || patient.RaceProps.IsAnomalyEntity || patient.IsMutant)
            {
                reason = "GM21_Med_NotMedicalPatient".Translate(patient.LabelShortCap);
                return false;
            }
            bool ours = patient.Faction == Faction.OfPlayer || patient.IsPrisonerOfColony;
            if (!ours && !patient.Downed)
            {
                reason = "GM21_Med_NotOurPatient".Translate(patient.LabelShortCap);
                return false;
            }
            if (forOrder)
            {
                if (!doctor.CanReach(patient, PathEndMode.ClosestTouch, Danger.Deadly))
                {
                    reason = "GM21_Med_CannotReach".Translate(patient.LabelShortCap);
                    return false;
                }
                if (!doctor.CanReserve(patient, 1, -1, null, true))
                {
                    reason = "GM21_Med_Reserved".Translate(patient.LabelShortCap);
                    return false;
                }
            }
            return true;
        }

        private static void OnPatientChosen(Pawn doctor, Gm21MedicineMode mode, Pawn patient)
        {
            string reason;
            if (!CanTreatPatient(doctor, patient, true, out reason))
            {
                Reject(reason);
                return;
            }

            List<FloatMenuOption> options = new List<FloatMenuOption>();
            if (mode == Gm21MedicineMode.Reconstruct)
            {
                List<Hediff_MissingPart> parts = Gm21ReconstructCandidates.GetReconstructionCandidates(patient);
                if (parts.Count == 0)
                {
                    Reject("GM21_Med_NoReconstructCandidates".Translate(patient.LabelShortCap));
                    return;
                }
                for (int i = 0; i < parts.Count; i++)
                {
                    Hediff_MissingPart missing = parts[i];
                    options.Add(new FloatMenuOption(
                        "GM21_Med_ReconstructOption".Translate(missing.Part.LabelCap),
                        delegate { Order(doctor, Gm21InterventionJobs.MakeReconstruct(patient, missing)); }));
                }
            }
            else
            {
                List<Hediff> conditions = Gm21CureCandidates.GetCureCandidates(patient);
                if (conditions.Count == 0)
                {
                    Reject("GM21_Med_NoCureCandidates".Translate(patient.LabelShortCap));
                    return;
                }
                for (int i = 0; i < conditions.Count; i++)
                {
                    Hediff condition = conditions[i];
                    string label = condition.Part != null
                        ? "GM21_Med_CureOptionPart".Translate(condition.LabelCap, condition.Part.Label)
                        : "GM21_Med_CureOption".Translate(condition.LabelCap);
                    options.Add(new FloatMenuOption(label,
                        delegate { Order(doctor, Gm21InterventionJobs.MakeCure(patient, condition)); }));
                }
            }

            if (options.Count == 1)
            {
                options[0].action();
                return;
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        // ---------------------------------------------------------------- corpses

        private static void OnCorpseChosen(Pawn doctor, Corpse corpse)
        {
            string reason;
            if (!Gm21Resuscitation.CanResuscitate(corpse, out reason))
            {
                Reject(reason);
                return;
            }
            if (!doctor.CanReach(corpse, PathEndMode.Touch, Danger.Deadly))
            {
                Reject("GM21_Med_CannotReach".Translate(corpse.LabelShortCap));
                return;
            }
            if (!doctor.CanReserve(corpse, 1, -1, null, true))
            {
                Reject("GM21_Med_Reserved".Translate(corpse.LabelShortCap));
                return;
            }
            Order(doctor, Gm21InterventionJobs.MakeResuscitate(corpse));
        }

        // ---------------------------------------------------------------- helpers

        private static void Order(Pawn doctor, Job job)
        {
            doctor.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        private static void Reject(string reason)
        {
            if (reason.NullOrEmpty()) return;
            Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
        }
    }
}
