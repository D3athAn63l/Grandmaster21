using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Grandmaster21
{
    /// <summary>One stack the planner may draw from.</summary>
    public struct Gm21SupplyStack
    {
        public Thing thing;
        /// <summary>MedicalPotency of one unit.</summary>
        public float potency;
        /// <summary>Units this doctor may take from it (reservable, not the whole stack if shared).</summary>
        public int available;
        /// <summary>Already in the doctor's inventory: no trip needed.</summary>
        public bool inInventory;
        /// <summary>Path distance proxy for tie-breaking (squared straight-line).</summary>
        public float distanceSquared;
    }

    /// <summary>
    /// The ordinary medicine a Grandmaster intervention consumes.
    ///
    /// THE MEDICINE IS NOT THE MIRACLE. An intervention needs a potency BUDGET of ordinary loaded
    /// medicine -- the same MedicalPotency stat vanilla uses to rate medicine -- and the Grandmaster's
    /// time. Any medicine ThingDef participates by its loaded potency, so herbal (0.60), industrial
    /// (1.00), glitterworld (1.60) and modded medicine combine naturally: several weak units, or fewer
    /// strong ones. No DefName is named anywhere.
    ///
    /// SELECTION follows vanilla's own preference (HealthAIUtility.FindBestMedicine): best allowed
    /// medicine first, the doctor's inventory before the map at equal potency, nearer before farther.
    /// Forbidden medicine is never touched.
    ///
    /// MEDICAL CARE is respected for a living patient: their own medical-care setting, or -- for a
    /// patient the colony has no setting for yet (a downed visitor or raider) -- the vanilla default
    /// for their group from the Medical Defaults dialog. Lower it to make the Grandmaster use cheaper
    /// medicine. The ONE override is Resuscitate: a corpse's medical-care setting cannot be edited
    /// (the health tab hides it once a pawn is dead), so a stale "no medicine" would make the dead
    /// impossible to save. Resuscitation therefore may use any unforbidden medicine; forbid the
    /// stacks you want kept out of it.
    ///
    /// LIFECYCLE: nothing is consumed when the order is given. The job reserves the planned map stacks,
    /// carries the planned counts into the Grandmaster's inventory, performs the work, and only on a
    /// successful, committed intervention destroys exactly one budget's worth from that inventory. An
    /// interrupted intervention leaves the collected medicine in the Grandmaster's inventory -- never
    /// destroyed, never duplicated -- where vanilla tending can use it too.
    /// </summary>
    public static class Gm21MedicineSupplies
    {
        /// <summary>A potency budget is met when the remainder is within this.</summary>
        public const float BudgetTolerance = 0.0001f;

        /// <summary>At most this many separate map stacks are collected for one intervention.</summary>
        public const int MaxMapStacks = 8;

        private static readonly Dictionary<ThingDef, float> PotencyByDef = new Dictionary<ThingDef, float>();

        // ---------------------------------------------------------------- potency

        /// <summary>
        /// MedicalPotency of one unit of this medicine, read once per Def and cached (the same stat,
        /// read the same way, as vanilla's medicine priority). 0 for anything that is not medicine.
        /// </summary>
        public static float PotencyOf(ThingDef def)
        {
            if (def == null || !def.IsMedicine) return 0f;
            float potency;
            if (PotencyByDef.TryGetValue(def, out potency)) return potency;
            try
            {
                potency = def.GetStatValueAbstract(StatDefOf.MedicalPotency);
            }
            catch (Exception)
            {
                potency = 0f;
            }
            if (float.IsNaN(potency) || float.IsInfinity(potency) || potency < 0f) potency = 0f;
            PotencyByDef[def] = potency;
            return potency;
        }

        /// <summary>The potency budget for an intervention. Provisional tuning, see Gm21Medicine.</summary>
        public static float BudgetFor(Gm21MedicineMode mode)
        {
            switch (mode)
            {
                case Gm21MedicineMode.Reconstruct: return Gm21Medicine.ReconstructPotencyBudget;
                case Gm21MedicineMode.Resuscitate: return Gm21Medicine.ResuscitatePotencyBudget;
                default: return Gm21Medicine.CurePotencyBudget;
            }
        }

        // ---------------------------------------------------------------- pure planning

        /// <summary>
        /// Greedy plan over stacks already in preference order: take from each only as many units as
        /// the remaining budget still needs. Pure; <paramref name="take"/> receives the count per stack.
        /// Returns false, taking nothing, if the stacks together cannot meet the budget.
        /// </summary>
        public static bool Plan(IList<float> potency, IList<int> available, float budget, int[] take)
        {
            for (int i = 0; i < take.Length; i++) take[i] = 0;
            if (budget <= BudgetTolerance) return true;
            float remaining = budget;
            for (int i = 0; i < potency.Count && remaining > BudgetTolerance; i++)
            {
                float p = potency[i];
                if (p <= 0f || available[i] <= 0) continue;
                int need = Mathf.CeilToInt((remaining - BudgetTolerance) / p);
                int n = Math.Min(available[i], Math.Max(1, need));
                take[i] = n;
                remaining -= n * p;
            }
            if (remaining > BudgetTolerance)
            {
                for (int i = 0; i < take.Length; i++) take[i] = 0;
                return false;
            }
            return true;
        }

        /// <summary>Sorts candidates into vanilla's preference order (see the class summary).</summary>
        public static void SortByPreference(List<Gm21SupplyStack> stacks)
        {
            stacks.Sort(delegate(Gm21SupplyStack a, Gm21SupplyStack b)
            {
                int byPotency = b.potency.CompareTo(a.potency);
                if (byPotency != 0) return byPotency;
                if (a.inInventory != b.inInventory) return a.inInventory ? -1 : 1;
                return a.distanceSquared.CompareTo(b.distanceSquared);
            });
        }

        /// <summary>Plans over typed stacks; <paramref name="plan"/> receives (stack, count) pairs.</summary>
        public static bool TryPlan(List<Gm21SupplyStack> stacks, float budget, List<KeyValuePair<Gm21SupplyStack, int>> plan,
            out float availablePotency)
        {
            plan.Clear();
            availablePotency = 0f;
            float[] potency = new float[stacks.Count];
            int[] available = new int[stacks.Count];
            for (int i = 0; i < stacks.Count; i++)
            {
                potency[i] = stacks[i].potency;
                available[i] = stacks[i].available;
                availablePotency += potency[i] * available[i];
            }
            int[] take = new int[stacks.Count];
            if (!Plan(potency, available, budget, take)) return false;
            for (int i = 0; i < take.Length; i++)
            {
                if (take[i] > 0) plan.Add(new KeyValuePair<Gm21SupplyStack, int>(stacks[i], take[i]));
            }
            return true;
        }

        // ---------------------------------------------------------------- the world

        /// <summary>
        /// The medical-care category that governs this patient's medicine, or null for no
        /// restriction (no patient: the corpse override, see the class summary). A pawn with its own
        /// setting uses it; one without uses vanilla's default for its group, mirroring
        /// Pawn_PlayerSettings.ResetMedicalCare.
        /// </summary>
        public static MedicalCareCategory? CareFor(Pawn patient)
        {
            if (patient == null) return null;
            if (patient.playerSettings != null) return patient.playerSettings.medCare;
            if (Verse.Current.Game == null || Verse.Current.Game.playSettings == null) return null;
            PlaySettings settings = Find.PlaySettings;
            if (patient.IsPrisoner) return settings.defaultCareForPrisoner;
            Faction faction = patient.Faction;
            bool animal = patient.RaceProps != null && patient.RaceProps.Animal;
            if (faction == null) return animal ? settings.defaultCareForWildlife : settings.defaultCareForNoFaction;
            if (faction == Faction.OfPlayer)
            {
                if (animal) return settings.defaultCareForTamedAnimal;
                return patient.IsSlave ? settings.defaultCareForSlave : settings.defaultCareForColonist;
            }
            switch (faction.RelationKindWith(Faction.OfPlayer))
            {
                case FactionRelationKind.Ally: return settings.defaultCareForFriendlyFaction;
                case FactionRelationKind.Neutral: return settings.defaultCareForNeutralFaction;
                default: return settings.defaultCareForHostileFaction;
            }
        }

        /// <summary>
        /// Whether the patient's medical care allows this medicine. <paramref name="carePatient"/> is
        /// the living patient, or null for no restriction (Resuscitate).
        /// </summary>
        public static bool Allowed(Pawn carePatient, ThingDef medicine)
        {
            MedicalCareCategory? care = CareFor(carePatient);
            return !care.HasValue || care.Value.AllowsMedicine(medicine);
        }

        /// <summary>Allowed medicine in the doctor's inventory, as supply stacks.</summary>
        public static List<Gm21SupplyStack> InventoryStacks(Pawn doctor, Pawn carePatient)
        {
            List<Gm21SupplyStack> result = new List<Gm21SupplyStack>();
            if (doctor == null || doctor.inventory == null) return result;
            ThingOwner<Thing> inventory = doctor.inventory.innerContainer;
            for (int i = 0; i < inventory.Count; i++)
            {
                Thing t = inventory[i];
                float potency = t == null ? 0f : PotencyOf(t.def);
                if (potency <= 0f || !Allowed(carePatient, t.def)) continue;
                result.Add(new Gm21SupplyStack { thing = t, potency = potency, available = t.stackCount, inInventory = true });
            }
            return result;
        }

        /// <summary>
        /// Everything this doctor could use: allowed inventory medicine, plus allowed, unforbidden,
        /// reservable, reachable medicine on the doctor's map. Called when an order is given or
        /// re-validated -- never per tick.
        /// </summary>
        public static List<Gm21SupplyStack> Gather(Pawn doctor, Pawn carePatient)
        {
            List<Gm21SupplyStack> result = InventoryStacks(doctor, carePatient);
            Map map = doctor == null ? null : doctor.MapHeld;
            if (map != null && doctor.Spawned)
            {
                List<Thing> medicine = map.listerThings.ThingsInGroup(ThingRequestGroup.Medicine);
                for (int i = 0; i < medicine.Count; i++)
                {
                    Thing t = medicine[i];
                    if (t == null || !t.Spawned) continue;
                    float potency = PotencyOf(t.def);
                    if (potency <= 0f || !Allowed(carePatient, t.def) || t.IsForbidden(doctor)) continue;
                    int reservable = map.reservationManager.CanReserveStack(doctor, t, 10);
                    if (reservable <= 0) continue;
                    if (!doctor.CanReach(t, PathEndMode.ClosestTouch, Danger.Deadly)) continue;
                    result.Add(new Gm21SupplyStack
                    {
                        thing = t, potency = potency, available = Math.Min(reservable, t.stackCount),
                        distanceSquared = (t.Position - doctor.Position).LengthHorizontalSquared
                    });
                }
            }
            SortByPreference(result);
            return result;
        }

        /// <summary>
        /// The order-time plan: can this doctor gather a budget's worth? Map stacks go into the job's
        /// target queue; inventory stacks need no trip. <paramref name="reason"/> explains a refusal.
        /// </summary>
        public static bool TryPlanOrder(Pawn doctor, Pawn carePatient, float budget,
            List<KeyValuePair<Gm21SupplyStack, int>> plan, out string reason)
        {
            reason = null;
            List<Gm21SupplyStack> stacks = Gather(doctor, carePatient);
            float availablePotency;
            if (!TryPlan(stacks, budget, plan, out availablePotency))
            {
                reason = "GM21_Med_NotEnoughMedicine".Translate(budget.ToString("0.##"), availablePotency.ToString("0.##"));
                return false;
            }
            int mapStacks = 0;
            foreach (KeyValuePair<Gm21SupplyStack, int> kv in plan) if (!kv.Key.inInventory) mapStacks++;
            if (mapStacks > MaxMapStacks)
            {
                reason = "GM21_Med_MedicineTooScattered".Translate(MaxMapStacks);
                return false;
            }
            return true;
        }

        /// <summary>Puts the plan's map stacks into the job's collection queue.</summary>
        public static void QueueCollection(Job job, List<KeyValuePair<Gm21SupplyStack, int>> plan)
        {
            job.targetQueueB = new List<LocalTargetInfo>();
            job.countQueue = new List<int>();
            foreach (KeyValuePair<Gm21SupplyStack, int> kv in plan)
            {
                if (kv.Key.inInventory) continue;
                job.targetQueueB.Add(kv.Key.thing);
                job.countQueue.Add(kv.Value);
            }
        }

        /// <summary>
        /// The completion-time plan, drawn only from what the Grandmaster now carries. Nothing is
        /// consumed here; Consume does that, once, after the intervention has been applied.
        /// </summary>
        public static bool TryPlanFromInventory(Pawn doctor, Pawn carePatient, float budget,
            List<KeyValuePair<Gm21SupplyStack, int>> plan, out string reason)
        {
            reason = null;
            List<Gm21SupplyStack> stacks = InventoryStacks(doctor, carePatient);
            SortByPreference(stacks);
            float availablePotency;
            if (TryPlan(stacks, budget, plan, out availablePotency)) return true;
            reason = "GM21_Med_NotEnoughMedicine".Translate(budget.ToString("0.##"), availablePotency.ToString("0.##"));
            return false;
        }

        /// <summary>Destroys exactly the planned counts. Called once, after a successful intervention.</summary>
        public static int Consume(List<KeyValuePair<Gm21SupplyStack, int>> plan)
        {
            int units = 0;
            foreach (KeyValuePair<Gm21SupplyStack, int> kv in plan)
            {
                Thing t = kv.Key.thing;
                int n = Math.Min(kv.Value, t == null || t.Destroyed ? 0 : t.stackCount);
                if (n <= 0) continue;
                t.SplitOff(n).Destroy();
                units += n;
            }
            return units;
        }

        /// <summary>"industrial medicine x2, herbal medicine x1" -- read before Consume destroys them.</summary>
        public static string Describe(List<KeyValuePair<Gm21SupplyStack, int>> plan)
        {
            List<string> parts = new List<string>();
            foreach (KeyValuePair<Gm21SupplyStack, int> kv in plan)
            {
                Thing t = kv.Key.thing;
                if (t == null || kv.Value <= 0) continue;
                parts.Add(t.def.label + " x" + kv.Value);
            }
            return string.Join(", ", parts.ToArray());
        }

        /// <summary>Total allowed potency this doctor could gather, for dev reports.</summary>
        public static float AvailablePotency(Pawn doctor, Pawn carePatient)
        {
            float total = 0f;
            foreach (Gm21SupplyStack s in Gather(doctor, carePatient)) total += s.potency * s.available;
            return total;
        }
    }
}
