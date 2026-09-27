using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// What an active Grandmaster Treatment does for its condition. Two mechanisms, both keyed on
    /// the SPECIFIC treated Hediff and both a multiplier on something vanilla already computes:
    ///
    ///  * INJURY -> recovery. Vanilla heals injuries in Pawn_HealthTracker.HealthTickInterval: every
    ///    600 ticks one tended injury is picked at random and Hediff_Injury.Heal(amount) is called
    ///    on it. When the injury picked carries an active regimen, amount is multiplied. That makes
    ///    THAT injury's expected recovery rate m times vanilla's, and leaves every other injury on
    ///    the same pawn exactly as it was. Nothing heals instantly.
    ///
    ///  * IMMUNIZABLE DISEASE -> immunity. ImmunityRecord.ImmunityChangePerTick receives the actual
    ///    disease instance. A positive gain against an instance with an active regimen is
    ///    multiplied. The patient's body still has to win; it simply gets there sooner. Unrelated
    ///    diseases on the same pawn are not touched.
    ///
    /// SCOPE OF "NATURAL" RECOVERY. Heal is only scaled inside the health tick (a prefix/finalizer
    /// pair counts the depth), so heals issued by psycasts, serums, dev tools or other mods from
    /// outside it are left alone. Inside the tick the multiplier covers the body's own recovery of
    /// that injury: tended natural healing and, with Anomaly, hediff-driven regeneration.
    ///
    /// Both paths are allocation-free and cost an untreated condition one weak-table miss.
    /// </summary>
    public static class Gm21RecoveryEffects
    {
        [System.ThreadStatic] private static int healthTickDepth;

        /// <summary>True while Pawn_HealthTracker.HealthTickInterval is running on this thread.</summary>
        public static bool InHealthTick
        {
            get { return healthTickDepth > 0; }
        }

        /// <summary>Pawn_HealthTracker.HealthTickInterval prefix.</summary>
        internal static void Prefix_HealthTickInterval()
        {
            healthTickDepth++;
        }

        /// <summary>
        /// Pawn_HealthTracker.HealthTickInterval finalizer. Void on purpose -- it restores a counter
        /// and must never alter exception state (see Gm21ShootingPatches.Finalizer_CloseShotContext).
        /// </summary>
        internal static void Finalizer_HealthTickInterval()
        {
            if (healthTickDepth > 0) healthTickDepth--;
        }

        /// <summary>Hediff_Injury.Heal prefix.</summary>
        internal static void Prefix_Heal(Hediff_Injury __instance, ref float amount)
        {
            if (healthTickDepth == 0 || amount <= 0f) return;
            float m;
            if (!Gm21TreatmentStore.TryGetRecoveryMultiplier(__instance, out m)) return;
            amount *= m;
        }

        /// <summary>ImmunityRecord.ImmunityChangePerTick postfix.</summary>
        internal static void Postfix_ImmunityChangePerTick(bool sick, Hediff diseaseInstance, ref float __result)
        {
            if (!sick || diseaseInstance == null || __result <= 0f) return;
            float m;
            if (!Gm21TreatmentStore.TryGetRecoveryMultiplier(diseaseInstance, out m)) return;
            __result *= m;
        }
    }
}
