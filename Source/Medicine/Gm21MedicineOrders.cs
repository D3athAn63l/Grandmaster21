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
    /// The UI holds NO medical logic: it asks Gm21CureCandidates, Gm21ReconstructCandidates,
    /// Gm21Resuscitation and Gm21MedicineSupplies what is valid and displays exactly that, then hands
    /// the choice to a job. Vanilla targeting, a vanilla FloatMenu and a vanilla confirmation box are
    /// the only interface.
    ///
    /// Every order is planned before it is given: the medicine the intervention will use is chosen
    /// and put in the job's collection queue, and an order that cannot gather its potency budget is
    /// refused with the reason, before the Grandmaster takes a step.
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
                canTargetSelf = true,
                mapObjectTargetsMustBeAutoAttackable = false,
                validator = delegate(TargetInfo t)
                {
                    Pawn p = t.Thing as Pawn;
                    string ignored;
                    return p != null && CanTreatPatient(doctor, p, false, out ignored);
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
        /// a living, spawned flesh pawn on the doctor's map, never a mutant or Anomaly entity, and
        /// one the colony is entitled to treat -- its own pawns and animals (the Grandmaster
        /// themself included), prisoners of the colony, or anyone who is down. A standing hostile,
        /// or a standing visitor under another faction's control, is not a patient.
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

        /// <summary>
        /// Reconstruct on oneself needs real hands (Gm21Medicine.SelfReconstructMinManipulation);
        /// on anyone else, the ordinary ability to practise is enough.
        /// </summary>
        public static bool CanReconstructOn(Pawn doctor, Pawn patient, out string reason)
        {
            reason = null;
            if (patient != doctor || Gm21Medicine.CanSelfReconstruct(doctor)) return true;
            reason = "GM21_Med_SelfReconstructHands".Translate(doctor.LabelShortCap,
                Gm21Medicine.SelfReconstructMinManipulation.ToStringPercent());
            return false;
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
                if (!CanReconstructOn(doctor, patient, out reason))
                {
                    Reject(reason);
                    return;
                }
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
                        delegate { Order(doctor, Gm21InterventionJobs.MakeReconstruct(patient, missing), mode, patient); }));
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
                        delegate { Order(doctor, Gm21InterventionJobs.MakeCure(patient, condition), mode, patient); }));
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
            if (!CanOrderResuscitation(doctor, corpse, out reason))
            {
                Reject(reason);
                return;
            }

            // Hostility is not a refusal -- the Grandmaster may save an enemy -- but the player is
            // told plainly what they are about to do.
            Gm21ResuscitationFacts facts = Gm21Resuscitation.Gather(corpse, false);
            if (facts.hostile)
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "GM21_Med_ResHostileWarning".Translate(corpse.InnerPawn.LabelShortCap),
                    delegate
                    {
                        // The world may have moved while the box was open.
                        if (CanOrderResuscitation(doctor, corpse, out reason)) OrderResuscitation(doctor, corpse);
                        else Reject(reason);
                    },
                    true));
                return;
            }
            OrderResuscitation(doctor, corpse);
        }

        private static bool CanOrderResuscitation(Pawn doctor, Corpse corpse, out string reason)
        {
            if (!Gm21Medicine.CanPractise(doctor))
            {
                reason = "GM21_Med_NotPractising".Translate(doctor.LabelShortCap);
                return false;
            }
            if (!Gm21Resuscitation.CanResuscitate(corpse, out reason)) return false;
            if (!doctor.CanReach(corpse, PathEndMode.Touch, Danger.Deadly))
            {
                reason = "GM21_Med_CannotReach".Translate(corpse.LabelShortCap);
                return false;
            }
            if (!doctor.CanReserve(corpse, 1, -1, null, true))
            {
                reason = "GM21_Med_Reserved".Translate(corpse.LabelShortCap);
                return false;
            }
            return true;
        }

        private static void OrderResuscitation(Pawn doctor, Corpse corpse)
        {
            Order(doctor, Gm21InterventionJobs.MakeResuscitate(corpse), Gm21MedicineMode.Resuscitate, null);
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>
        /// Plans the medicine, queues its collection, and gives the order -- or refuses with the
        /// reason. <paramref name="carePatient"/> is whose medical care limits the medicine (null:
        /// none, for Resuscitate). Everything is re-checked when the job starts.
        /// </summary>
        private static void Order(Pawn doctor, Job job, Gm21MedicineMode mode, Pawn carePatient)
        {
            List<KeyValuePair<Gm21SupplyStack, int>> plan = new List<KeyValuePair<Gm21SupplyStack, int>>();
            string reason;
            if (!Gm21MedicineSupplies.TryPlanOrder(doctor, carePatient, Gm21MedicineSupplies.BudgetFor(mode), plan, out reason))
            {
                Reject(reason);
                return;
            }
            Gm21MedicineSupplies.QueueCollection(job, plan);
            doctor.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        private static void Reject(string reason)
        {
            if (reason.NullOrEmpty()) return;
            Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
        }
    }
}
