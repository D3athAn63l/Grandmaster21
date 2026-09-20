using System;

namespace Grandmaster21
{
    /// <summary>
    /// The mod's single "this write is deliberate" marker.
    ///
    /// Grandmaster status is permanent during ordinary gameplay: <see cref="Patch_SkillRecord_SetLevel"/>
    /// silently ignores every assignment to <c>SkillRecord.Level</c> once the stored level is 21.
    /// A handful of the mod's OWN operations must still be able to move that value:
    /// the authorised 20 -> 21 promotion, the "Prepare Save for Uninstall" cleanup, and the
    /// dev-mode helpers.
    ///
    /// Those operations open a scope here first. The scope is:
    ///
    ///  * a struct, so `using` compiles to a plain try/finally with no allocation and no boxing;
    ///  * re-entrant (depth counted), so nesting cannot clear the flag early;
    ///  * [ThreadStatic], so a background thread can never observe a flag raised on the main
    ///    thread. RimWorld does touch pawn data off the main thread during some generation and
    ///    save work, and a plain static bool would be visible there.
    ///
    /// Because `using` always emits try/finally, an exception thrown inside an authorised block
    /// cannot leave the bypass latched on.
    /// </summary>
    internal static class Gm21Authorized
    {
        [ThreadStatic] private static int depth;

        /// <summary>True only inside an explicitly authorised block on this thread.</summary>
        internal static bool Active
        {
            get { return depth > 0; }
        }

        internal static Scope Enter()
        {
            depth++;
            return default(Scope);
        }

        internal struct Scope : IDisposable
        {
            public void Dispose()
            {
                if (depth > 0) depth--;
            }
        }
    }
}
