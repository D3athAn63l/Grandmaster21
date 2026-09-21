using System;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Grandmaster21
{
    /// <summary>
    /// The micro-dash, as the player sees it.
    ///
    /// THE FICTION. The Grandmaster crossed the gap, met the projectile, and was back before the
    /// game's movement could represent any of it. What is shown is the afterimage of that: a faint
    /// streak from where they stand to where the catch happened, a spark at contact, and a
    /// metallic ring.
    ///
    /// THE MECHANICS, WHICH ARE THE POINT. The pawn does not move. Not one cell, not for one tick.
    /// Their position, job, path reservation and melee engagement are untouched, so a Grandmaster
    /// standing in a doorway is still in the doorway, a Grandmaster locked in melee is still
    /// locked in melee, and a minigun burst cannot strobe them across the map. Movement speed
    /// still decides whether the dash SUCCEEDS -- it simply never decides where they end up.
    ///
    /// PRESENTATION NEVER BREAKS GAMEPLAY. Every def is null-checked and the whole body is wrapped,
    /// because a mod that removes a vanilla fleck or sound def must cost the player a visual
    /// effect and nothing else. If any of it fails, the interception has already happened and
    /// stands.
    /// </summary>
    internal static class Gm21GuardianFx
    {
        /// <summary>Width of the afterimage streak. Thin: it is a suggestion, not a beam.</summary>
        private const float TrailWidth = 0.18f;

        private const float SparkSize = 0.55f;

        /// <summary>
        /// Draws one interception. <paramref name="victim"/> may be the Grandmaster themselves,
        /// in which case there is no gap to streak across and only the contact is shown.
        /// </summary>
        internal static void MicroDash(Pawn guardian, Pawn victim, Projectile proj)
        {
            if (guardian == null || proj == null) return;
            Map map = guardian.Map;
            if (map == null) return;

            try
            {
                Vector3 from = guardian.DrawPos;
                Vector3 contact = proj.ExactPosition;

                // The streak out. Skipped when the Grandmaster is defending their own cell, where
                // a zero-length line would be a smear rather than a dash.
                if (victim != null && victim != guardian
                    && FleckDefOf.LineEMP != null
                    && (contact - from).magnitude > 1f)
                {
                    FleckMaker.ConnectingLine(from, contact, FleckDefOf.LineEMP, map, TrailWidth);
                }

                // The contact itself: a bright point where weapon met projectile.
                FleckMaker.ThrowLightningGlow(contact, map, SparkSize);
                if (FleckDefOf.MicroSparksFast != null)
                {
                    FleckMaker.Static(contact, map, FleckDefOf.MicroSparksFast, 1f);
                }

                // The ring. MetalHitImportant is the closest vanilla has to a parried round.
                if (SoundDefOf.MetalHitImportant != null)
                {
                    SoundDefOf.MetalHitImportant.PlayOneShot(
                        SoundInfo.InMap(new TargetInfo(contact.ToIntVec3(), map, false)));
                }
            }
            catch (Exception e)
            {
                // Cosmetic only. The interception itself already succeeded and is unaffected.
                Log.WarningOnce("[Grandmaster 21] Guardian interception effects failed; the "
                                + "interception itself is unaffected: " + e.Message, 0x6D21FA11);
            }
        }
    }
}
