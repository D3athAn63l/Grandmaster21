using RimWorld;
using Verse;

namespace Grandmaster21
{
    /// <summary>Marker properties for the runtime-injected comp; nothing is configurable per def.</summary>
    public class CompProperties_GrandmasterMeal : CompProperties
    {
        public CompProperties_GrandmasterMeal()
        {
            compClass = typeof(CompGrandmasterMeal);
        }
    }

    /// <summary>
    /// Masterful provenance: how many servings of THIS stack were cooked by a Grandmaster Cook.
    ///
    ///   0 &lt;= masterfulCount &lt;= parent.stackCount
    ///
    /// A count, never a boolean, so a mixed stack stays representable: ten Masterful meals merged with
    /// five ordinary ones are fifteen meals of which ten are Masterful.
    ///
    /// SIMPLE STATE ONLY. The comp never ticks and holds no reference to any pawn (the cook's identity
    /// is not needed), so it adds nothing to the per-tick cost of food. It is attached at startup, from
    /// <see cref="Gm21CookingStartup"/>, to ingestible things that carry vanilla's CompFoodPoisonable --
    /// found through the comp, never a DefName list, so modded prepared food is covered by the same rule.
    ///
    /// HOW THE COUNT SURVIVES VANILLA STACK OPERATIONS (audited against the real 1.6 assembly):
    ///
    ///   MERGE -- ThingWithComps.TryAbsorbStack calls every comp's PreAbsorbStack(other, count) BEFORE
    ///   any stack count changes. The receiving stack takes <see cref="Gm21MasterfulMath.Share"/> of the
    ///   Masterful servings among the <c>count</c> being transferred, and the donor loses exactly the
    ///   same number.
    ///
    ///   SPLIT -- ThingWithComps.SplitOff calls every comp's PostSplitOff(piece) AFTER the stack counts
    ///   changed (parent already holds the remainder). Before the split the stack had
    ///   parent.stackCount + piece.stackCount servings; the piece receives its proportional share and
    ///   the parent keeps the rest. A whole-stack split returns the same object (piece == parent) and
    ///   is a no-op here.
    ///
    ///   Both directions move the same integer out of one stack and into the other, so the Masterful
    ///   total is conserved through any number of splits and merges.
    ///
    /// The count is written into the Thing's own save node as "gm21MasterfulCount" and only when
    /// non-zero, so an old save loads as zero and a save without the mod simply ignores the element.
    /// </summary>
    public class CompGrandmasterMeal : ThingComp
    {
        private int masterfulCount;

        /// <summary>
        /// Masterful servings in this stack, always within [0, stackCount]. The clamp guards against
        /// another mod shrinking stackCount directly; every vanilla path goes through split or merge.
        /// </summary>
        public int MasterfulCount
        {
            get
            {
                int stack = parent != null ? parent.stackCount : 0;
                if (masterfulCount <= 0 || stack <= 0) return 0;
                return masterfulCount < stack ? masterfulCount : stack;
            }
        }

        /// <summary>Masterful fraction of the stack, in [0, 1].</summary>
        public float MasterfulRatio
        {
            get { return Gm21MasterfulMath.Ratio(MasterfulCount, parent.stackCount); }
        }

        /// <summary>The stack's future-rot multiplier: exactly 1 when no serving is Masterful.</summary>
        public float RotMultiplier
        {
            get
            {
                return Gm21MasterfulMath.RotMultiplier(MasterfulCount, parent.stackCount,
                    Gm21Cooking.PreservationStrength);
            }
        }

        /// <summary>Sets the count, clamped to the stack. Used by the dev actions and by creation.</summary>
        internal void SetMasterful(int count)
        {
            int stack = parent != null ? parent.stackCount : 0;
            masterfulCount = count < 0 ? 0 : (count > stack ? stack : count);
        }

        // ---------------------------------------------------------------- creation

        /// <summary>
        /// Vanilla GenRecipe.MakeRecipeProducts sets the product's stackCount and THEN calls
        /// Notify_RecipeProduced(worker) on every comp, so the stack size is already final here. Every
        /// serving cooked by a Grandmaster Cook is Masterful. Only real recipe products reach this
        /// callback (butchery and smelting products do not), which is the "cooked" boundary.
        /// </summary>
        public override void Notify_RecipeProduced(Pawn pawn)
        {
            base.Notify_RecipeProduced(pawn);
            if (Gm21Cooking.IsCookingGrandmaster(pawn)) SetMasterful(parent.stackCount);
        }

        // ---------------------------------------------------------------- merge / split

        public override void PreAbsorbStack(Thing otherStack, int count)
        {
            base.PreAbsorbStack(otherStack, count);
            if (count <= 0 || otherStack == null) return;
            CompGrandmasterMeal other = otherStack.TryGetComp<CompGrandmasterMeal>();
            if (other == null) return;

            // Nothing has moved yet: both stackCounts are still their pre-merge values.
            int otherMarked = other.MasterfulCount;
            int moved = Gm21MasterfulMath.Share(otherStack.stackCount, otherMarked, count);
            other.masterfulCount = otherMarked - moved;
            masterfulCount = MasterfulCount + moved;
        }

        public override void PostSplitOff(Thing piece)
        {
            base.PostSplitOff(piece);
            if (piece == null || ReferenceEquals(piece, parent)) return; // whole stack: same object
            CompGrandmasterMeal split = piece.TryGetComp<CompGrandmasterMeal>();
            if (split == null) return;

            // The split has already happened: parent holds the remainder, piece holds what left.
            int take = piece.stackCount;
            int before = parent.stackCount + take;
            int marked = masterfulCount < 0 ? 0 : (masterfulCount > before ? before : masterfulCount);
            int given = Gm21MasterfulMath.Share(before, marked, take);
            split.masterfulCount = given;
            masterfulCount = marked - given;
        }

        // ---------------------------------------------------------------- persistence / display

        public override void PostExposeData()
        {
            base.PostExposeData();
            // Default 0 and forceSave false: ordinary food adds nothing to the save.
            Scribe_Values.Look(ref masterfulCount, "gm21MasterfulCount", 0, false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && masterfulCount != 0)
            {
                masterfulCount = MasterfulCount; // never trust a stored count beyond the stack
            }
        }

        public override string CompInspectStringExtra()
        {
            int count = MasterfulCount;
            if (count <= 0) return null;
            if (Gm21Cooking.FreshnessEnabled && parent.GetComp<CompRottable>() != null)
            {
                return "GM21_Cook_MasterfulInspectRot".Translate(count, parent.stackCount,
                    RotMultiplier.ToStringPercent()).ToString();
            }
            return "GM21_Cook_MasterfulInspect".Translate(count, parent.stackCount).ToString();
        }
    }
}
