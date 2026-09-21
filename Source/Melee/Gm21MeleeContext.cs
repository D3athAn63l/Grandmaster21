using Verse;

namespace Grandmaster21
{
    /// <summary>
    /// One melee exchange in progress on this thread.
    ///
    /// WHY THIS EXISTS. RimWorld resolves a melee attack across several methods that do not share
    /// an object: Verb_MeleeAttack.TryCastShot rolls the hit and the dodge, and
    /// Pawn.PreApplyDamage -- a completely separate call, on the VICTIM -- is where the body part
    /// and the damage amount can still be changed. By the time PreApplyDamage runs there is no way
    /// to ask "was this a Grandmaster's deliberate strike, and did it critical?" from the
    /// DamageInfo alone: an unarmed strike's Weapon is the pawn's own def, and nothing carries the
    /// verb. The frame below is the missing link, and it is opened exactly where the caster IS
    /// known.
    ///
    /// Everything expensive is computed ONCE when the frame opens, so the per-damage-event
    /// question is a field read. An ordinary pawn's attack opens a frame whose first field says
    /// "not a Grandmaster" and never touches anything else.
    /// </summary>
    internal sealed class Gm21MeleeFrame
    {
        public Pawn attacker;
        public Thing target;

        /// <summary>The attacker is a legitimate, currently-capable Melee Grandmaster.</summary>
        public bool attackerIsGm;

        /// <summary>The defender is a legitimate, currently-capable Melee Grandmaster.</summary>
        public bool defenderIsGm;

        /// <summary>Doctrine in force for this strike. Meaningless unless attackerIsGm.</summary>
        public Gm21MeleeDoctrine doctrine;

        // Composites, computed ONCE when the frame opens and only for the side that is a
        // Grandmaster. Each one costs several GetStatValue/capacity reads, and the hooks that
        // consume them (dodge chance, damage application, on-hit effects) can run more than once
        // per strike -- recomputing them there would put stat evaluation on the hottest path in
        // melee. A value of 0 means "not computed, because that side is not a Grandmaster".
        public float atkAwareness;
        public float atkPrecision;
        public float atkPower;
        public float defDefence;

        /// <summary>
        /// Set by the GetDodgeChance postfix. Vanilla only consults the dodge chance once the hit
        /// roll has already succeeded, so "dodge chance was consulted AND the attack returned
        /// false" identifies a DODGE rather than a whiff -- which is what a riposte answers.
        /// </summary>
        public bool dodgeConsulted;

        /// <summary>
        /// Critical multiplier for this strike, 1 for none. Decided once, before damage is
        /// applied, so a strike that spreads over several damage events crits consistently
        /// instead of re-rolling per event.
        /// </summary>
        public float critMultiplier;

        /// <summary>
        /// The critical roll happens at most once per strike. A melee strike can produce several
        /// damage events (a weapon's extra damages, damage propagation), and re-rolling per event
        /// would let one swing be simultaneously critical and not.
        /// </summary>
        public bool critRolled;

        /// <summary>Guards against the same strike applying its on-hit effects twice.</summary>
        public bool onHitResolved;

        /// <summary>
        /// This attack is itself a scheduled follow-through, so it does not follow through again.
        ///
        /// A cleave is a real melee attack, which means it lands, rolls its own critical, can
        /// disarm -- and, without this, would roll its OWN cleave and spread off a spread. That is
        /// not what a follow-through is: one swing carries into the enemies standing around the
        /// one it hit, it does not start a new swing that does the same. A riposte is a genuine
        /// fresh swing and is deliberately NOT marked, so it cleaves normally.
        /// </summary>
        public bool isFollowThrough;

        public void Clear()
        {
            attacker = null;
            target = null;
            attackerIsGm = false;
            defenderIsGm = false;
            doctrine = Gm21MeleeDoctrine.Normal;
            atkAwareness = 0f;
            atkPrecision = 0f;
            atkPower = 0f;
            defDefence = 0f;
            dodgeConsulted = false;
            critMultiplier = 1f;
            critRolled = false;
            onHitResolved = false;
            isFollowThrough = false;
        }
    }

    /// <summary>
    /// The per-thread stack of melee frames.
    ///
    /// A STACK, not a single slot: another mod's patch, a damage-triggered counter-verb or a
    /// thorn-style retaliation can legitimately start a melee attack while one is already
    /// resolving, and a single slot would hand the inner attack the outer attack's attacker. The
    /// stack is a fixed-size array allocated once per thread, so the depth costs nothing.
    ///
    /// Past MaxDepth the frames stop being tracked and Current returns null, which every caller
    /// already treats as "not a Grandmaster strike -- let vanilla resolve it". Deep nesting is
    /// pathological, and falling back to vanilla is the correct answer to it.
    ///
    /// [ThreadStatic] because RimWorld touches pawn data off the main thread during some
    /// generation and save work; a plain static would let those observe combat state.
    /// </summary>
    internal static class Gm21MeleeContext
    {
        private const int MaxDepth = 8;

        [System.ThreadStatic] private static Gm21MeleeFrame[] stack;
        [System.ThreadStatic] private static int depth;

        /// <summary>The frame being resolved, or null when there is none (or nesting is too deep).</summary>
        internal static Gm21MeleeFrame Current
        {
            get
            {
                int d = depth;
                return (d > 0 && d <= MaxDepth) ? stack[d - 1] : null;
            }
        }

        /// <summary>
        /// Set by Gm21MeleeAction immediately before it drives a scheduled follow-through, and
        /// consumed by the very next frame that opens. A thread-static hand-off is used because
        /// the frame is opened inside RimWorld's own TryCastShot, which takes no argument this
        /// could ride on.
        /// </summary>
        [System.ThreadStatic] internal static bool NextIsFollowThrough;

        /// <summary>
        /// Opens a frame. ALWAYS balanced by Close from a Harmony finalizer, so an exception
        /// thrown anywhere inside the attack cannot leave the stack wedged.
        /// </summary>
        internal static Gm21MeleeFrame Open(Pawn attacker, Thing target)
        {
            depth++;
            if (depth > MaxDepth) return null;

            Gm21MeleeFrame[] s = stack;
            if (s == null)
            {
                s = stack = new Gm21MeleeFrame[MaxDepth];
            }
            Gm21MeleeFrame frame = s[depth - 1];
            if (frame == null) frame = s[depth - 1] = new Gm21MeleeFrame();

            frame.Clear();
            frame.attacker = attacker;
            frame.target = target;
            frame.isFollowThrough = NextIsFollowThrough;
            NextIsFollowThrough = false;   // consumed by exactly one frame
            return frame;
        }

        internal static void Close()
        {
            if (depth > 0) depth--;
        }
    }
}
