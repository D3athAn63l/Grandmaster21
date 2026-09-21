using System.Collections.Generic;
using Verse;

namespace Grandmaster21
{
    internal enum Gm21ActionKind : byte
    {
        /// <summary>A counterattack owed to a Grandmaster who just turned an attack aside.</summary>
        Riposte = 0,
        /// <summary>A follow-through strike against a second enemy in reach.</summary>
        Cleave = 1
    }

    internal struct Gm21ScheduledAction
    {
        public Pawn actor;
        public Thing target;
        public Gm21ActionKind kind;
        public int fireTick;
    }

    /// <summary>
    /// The melee reaction scheduler: the one piece of architecture the brief is most specific
    /// about.
    ///
    /// THE PROBLEM. Gameplay recursion is wanted -- A attacks, B parries and ripostes, A parries
    /// and counter-ripostes, indefinitely, until something external ends it. C# recursion is NOT:
    /// resolving a riposte inside the attack it answers means Attack -> Riposte -> Riposte ->
    /// ... on one synchronous call stack, and a hundred exchanges would overflow the stack and
    /// take RimWorld down with it. The two requirements look contradictory only until the
    /// counterattack stops being a CALL and becomes an EVENT.
    ///
    /// THE SHAPE. A parry does not attack anyone. It appends an entry to a queue and returns, so
    /// the original attack's call stack unwinds completely. One tick later this component drains
    /// the queue and resolves each entry as an independent, top-level melee attack. If that
    /// attack is itself parried, the parry appends another entry -- to the NEXT tick, never this
    /// one -- and the cycle repeats at constant stack depth forever.
    ///
    /// WHY NEXT TICK, SPECIFICALLY. Draining an entry is allowed to enqueue more work, and if
    /// that work were eligible in the same drain the loop would become unbounded within a single
    /// tick: a perfectly matched pair of Grandmasters would spin the game thread instead of
    /// fighting. Stamping every new entry with "current tick + 1" makes the queue strictly
    /// generational, so each tick performs a bounded amount of work no matter how long the
    /// exchange runs.
    ///
    /// WHAT THIS IS NOT. There is no counter limit, no fatigue, no escalating penalty and no
    /// "someone eventually loses" rule. Two identical Grandmasters will trade parries and
    /// ripostes until an explosion, a psychic lance, a third combatant, a fire or anything else
    /// outside the duel resolves it. That is the intended gameplay.
    ///
    /// NOTHING HERE IS SAVED. ExposeData is deliberately not overridden: a queue of in-flight
    /// counterattacks is transient combat state, and a game reloaded mid-exchange should start
    /// the next swing clean rather than restore a half-finished one.
    /// </summary>
    public class Gm21CombatScheduler : GameComponent
    {
        /// <summary>
        /// Hard ceiling on queued reactions. This is an ENGINE-STABILITY guard, not a gameplay
        /// limit: in normal play the queue holds at most one entry per melee exchange currently
        /// in progress on the map, which is a handful. It exists only so that a pathological
        /// interaction with another mod cannot grow the queue without bound and exhaust memory.
        /// Reaching it is logged once and is a bug report, not a balance decision.
        /// </summary>
        private const int MaxQueued = 512;

        private static readonly List<Gm21ScheduledAction> pending = new List<Gm21ScheduledAction>(32);
        private static readonly List<Gm21ScheduledAction> draining = new List<Gm21ScheduledAction>(32);
        private static bool overflowWarned;

        public Gm21CombatScheduler(Game game)
        {
            // A new Game means a new fight. Anything left over belongs to the previous one.
            Reset();
        }

        internal static void Reset()
        {
            pending.Clear();
            draining.Clear();
        }

        public override void StartedNewGame() { Reset(); }

        public override void LoadedGame() { Reset(); }

        internal static int QueuedCount { get { return pending.Count; } }

        internal static void ScheduleRiposte(Pawn actor, Thing target)
        {
            Schedule(actor, target, Gm21ActionKind.Riposte);
        }

        internal static void ScheduleCleave(Pawn actor, Thing target)
        {
            Schedule(actor, target, Gm21ActionKind.Cleave);
        }

        private static void Schedule(Pawn actor, Thing target, Gm21ActionKind kind)
        {
            if (actor == null || target == null || actor == target) return;
            if (!Gm21MeleeAction.CanReact(actor, target)) return;

            if (pending.Count >= MaxQueued)
            {
                if (!overflowWarned)
                {
                    overflowWarned = true;
                    Log.Warning("[Grandmaster 21] Melee reaction queue hit its " + MaxQueued
                                + "-entry safety ceiling and is dropping reactions. This is a "
                                + "stability guard, not a balance rule -- please report it. "
                                + "Logged once per session.");
                }
                return;
            }

            Gm21ScheduledAction action;
            action.actor = actor;
            action.target = target;
            action.kind = kind;
            // Strictly the NEXT tick. See the class comment: this is what keeps an endless
            // exchange bounded per tick instead of spinning inside one.
            action.fireTick = CurrentTick() + 1;
            pending.Add(action);
        }

        private static int CurrentTick()
        {
            // Find.TickManager is null outside a running game; scheduling then is meaningless
            // but must not throw.
            return (Find.TickManager != null) ? Find.TickManager.TicksGame : 0;
        }

        public override void GameComponentTick()
        {
            if (pending.Count == 0) return;

            int now = CurrentTick();

            // Split due entries out first, so executing them can safely append to `pending`
            // without disturbing the iteration or becoming eligible this tick.
            draining.Clear();
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (pending[i].fireTick <= now)
                {
                    draining.Add(pending[i]);
                    pending.RemoveAt(i);
                }
            }

            for (int i = 0; i < draining.Count; i++)
            {
                Gm21ScheduledAction action = draining[i];
                try
                {
                    Gm21MeleeAction.Execute(action.actor, action.target, action.kind);
                }
                catch (System.Exception e)
                {
                    // One bad reaction must not stop the rest of the queue, and must not take
                    // the game tick down with it.
                    Log.Error("[Grandmaster 21] Melee reaction (" + action.kind + ") failed: " + e);
                }
            }
            draining.Clear();
        }
    }
}
