using RimWorld;
using Verse;

namespace Grandmaster21
{
    /// <summary>What an open TendUtility.DoTend call means for the comps it is about to tend.</summary>
    public struct Gm21TendFrame
    {
        /// <summary>This tend is a Grandmaster tend with actual medicine.</summary>
        public bool grandmaster;

        /// <summary>The exact tend quality it must produce.</summary>
        public float target;
    }

    /// <summary>
    /// Deterministic Grandmaster Tend Quality.
    ///
    /// WHERE THE RANDOMNESS LIVES. In 1.6, TendUtility.DoTend computes one base quality, then calls
    /// Hediff.Tended(quality, maxQuality) on each condition it treats, and the vanilla tend state is
    /// written by HediffComp_TendDuration.CompTended:
    ///
    ///     tendQuality = Clamp(quality + Rand.Range(-0.25, 0.25), 0, maxQuality)
    ///
    /// maxQuality is the medicine's MedicalQualityMax. So a Grandmaster needs two things only:
    /// the ceiling raised to the Grandmaster target, and the roll unable to land anywhere else.
    ///
    /// HOW. DoTend is wrapped in a frame (prefix/finalizer) that knows the doctor and the medicine.
    /// While a Grandmaster frame is open, a prefix on CompTended rewrites ONLY that comp's two
    /// arguments: maxQuality = target, quality = target + 0.25 + margin. Vanilla's own clamp then
    /// yields exactly target for every possible roll -- the lowest roll is still above the ceiling.
    /// Vanilla still writes tendQuality, still accumulates totalTendQuality, still sets
    /// tendTicksLeft and still throws its "Tended ... Quality 100%" mote, all with the exact value.
    ///
    /// Nothing else sees the rewritten arguments. Hediff.Tended and every other comp receive
    /// vanilla's inputs, which keeps condition-specific tend side effects -- vanilla's heart-attack
    /// treatment roll, other mods' comps -- on their own vanilla footing.
    ///
    /// ORDINARY DOCTORS are untouched: no frame is opened for them, and the prefix is one bool
    /// test. A Grandmaster tending WITHOUT medicine is also vanilla (see Frame for the full rule).
    /// </summary>
    public static class Gm21GrandmasterTend
    {
        /// <summary>
        /// Keeps the lowest possible vanilla roll strictly above the ceiling, so float rounding in
        /// (target + 0.25) - 0.25 can never put the result a ULP below target.
        /// </summary>
        public const float DeterminismMargin = 0.05f;

        [System.ThreadStatic] private static Gm21TendFrame current;

        /// <summary>The frame of the DoTend currently executing on this thread.</summary>
        public static Gm21TendFrame Current
        {
            get { return current; }
        }

        /// <summary>
        /// Decides what a DoTend call is.
        ///
        /// A Grandmaster frame requires ALL of: a doctor who is a practising Medicine Grandmaster,
        /// actual medicine that is not destroyed, and a cached target for that medicine's Def.
        ///
        /// NO MEDICINE -> vanilla. The brief forbids inventing a bare-hands tier, so a Grandmaster
        /// tending without medicine gets vanilla's random roll under vanilla's 70% no-medicine
        /// ceiling, and -- because that tend replaces the condition's regimen -- any Grandmaster
        /// Treatment on the conditions it touches is cleared, exactly as for an ordinary doctor.
        ///
        /// SELF-TEND keeps vanilla's self-tend factor (TendUtility.SelfTendQualityFactor, 0.7),
        /// applied to the Grandmaster target and still deterministic: herbal 100% becomes exactly
        /// 70%. Treating yourself is a physical limitation, and mastery does not remove physical
        /// limitations anywhere else in this mod.
        ///
        /// The bed's MedicalTendQualityOffset is not added: vanilla adds it before clamping to the
        /// medicine ceiling, and the Grandmaster result IS the ceiling.
        /// </summary>
        public static Gm21TendFrame Frame(Pawn doctor, Pawn patient, Medicine medicine)
        {
            Gm21TendFrame frame = default(Gm21TendFrame);
            if (!Gm21Medicine.TendEnabled) return frame;
            if (doctor == null || medicine == null || medicine.Destroyed || medicine.def == null) return frame;
            if (!Gm21Medicine.CanPractise(doctor)) return frame;

            float target;
            if (!Gm21MedicineTiers.TryGetTarget(medicine.def, out target)) return frame;
            if (doctor == patient) target *= TendUtility.SelfTendQualityFactor;

            frame.grandmaster = target > 0f;
            frame.target = target;
            return frame;
        }

        // ---------------------------------------------------------------- patch bodies

        /// <summary>TendUtility.DoTend prefix. Saves the enclosing frame so nesting restores cleanly.</summary>
        internal static void Prefix_DoTend(Pawn doctor, Pawn patient, Medicine medicine, out Gm21TendFrame __state)
        {
            __state = current;
            current = Frame(doctor, patient, medicine);
        }

        /// <summary>
        /// TendUtility.DoTend finalizer. VOID, for the reason Gm21ShootingPatches documents: an
        /// Exception-returning finalizer replaces the pending exception. This only restores state.
        /// </summary>
        internal static void Finalizer_DoTend(Gm21TendFrame __state)
        {
            current = __state;
        }

        /// <summary>
        /// HediffComp_TendDuration.CompTended prefix: make vanilla's own clamp produce exactly target.
        /// </summary>
        internal static void Prefix_CompTended(ref float quality, ref float maxQuality)
        {
            if (!current.grandmaster) return;
            maxQuality = current.target;
            quality = current.target + HediffComp_TendDuration.TendQualityRandomVariance + DeterminismMargin;
        }

        /// <summary>
        /// HediffComp_TendDuration.CompTended postfix: the regimen follows the tend that just
        /// happened to THIS condition. A Grandmaster tend starts or refreshes it at the quality
        /// vanilla actually recorded; any other tend replaces it.
        /// </summary>
        internal static void Postfix_CompTended(HediffComp_TendDuration __instance)
        {
            Hediff hediff = __instance.parent;
            if (hediff == null) return;
            if (current.grandmaster && Gm21Medicine.TreatmentEnabled)
            {
                // Find.TickManager dereferences Current.Game, so test for the game, not the manager.
                int tick = Verse.Current.Game != null ? Find.TickManager.TicksGame : -1;
                Gm21TreatmentStore.Apply(hediff, __instance.tendQuality, tick);
            }
            else
            {
                Gm21TreatmentStore.Clear(hediff);
            }
        }

        /// <summary>
        /// HediffComp_TendDuration.CompTipStringExtra postfix: one line on the condition's tooltip
        /// while a regimen is active, so the state is visible without dev mode.
        /// </summary>
        internal static void Postfix_CompTipStringExtra(HediffComp_TendDuration __instance, ref string __result)
        {
            Hediff hediff = __instance.parent;
            Gm21Treatment t;
            if (!Gm21TreatmentStore.TryGetActive(hediff, out t)) return;

            string line;
            float m = Gm21Medicine.RecoveryMultiplierFor(t.quality);
            switch (Gm21TreatmentStore.EffectFor(hediff))
            {
                case Gm21TreatmentEffect.Recovery:
                    line = "GM21_Med_TreatmentRecovery".Translate(t.quality.ToStringPercent(), m.ToString("0.00"));
                    break;
                case Gm21TreatmentEffect.Immunity:
                    line = "GM21_Med_TreatmentImmunity".Translate(t.quality.ToStringPercent(), m.ToString("0.00"));
                    break;
                default:
                    line = "GM21_Med_TreatmentOnly".Translate(t.quality.ToStringPercent());
                    break;
            }
            __result = __result.NullOrEmpty() ? line : __result + "\n" + line;
        }
    }
}
