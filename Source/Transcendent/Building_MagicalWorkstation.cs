using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using Verse.AI;

namespace Grandmaster21.Transcendent
{
    // Historical CLR name retained for existing saves; all three benches use this implementation.
    public sealed partial class Building_MagicalWorkstation : Building_WorkTable, IThingHolder
    {
        private TranscendentProject project;
        private ThingDef pendingStuff;
        private int nextProjectId;
        // One native owner holds a transaction's detached inputs OR one completed output.
        // Ordinary active work holds no Things at all: inputs have already been destroyed.
        private ThingOwner<Thing> contents;
        internal int nextSearchTick;

        public Building_MagicalWorkstation() { contents = new ThingOwner<Thing>(this, false, LookMode.Deep); }
        internal TranscendentTierConfig Config { get { return TranscendentTierConfig.ForBench(def); } }
        internal TranscendentProject Project { get { return project; } }
        internal ThingDef PendingStuff { get { return pendingStuff; } }
        internal Bill_Production Pending { get { return billStack.Bills.FirstOrDefault() as Bill_Production; } }
        internal bool HasRecovery { get { return project == null && contents.Count != 0; } }
        public ThingOwner GetDirectlyHeldThings() { return contents; }
        public void GetChildHolders(List<IThingHolder> outChildren) { ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, contents); }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref project, "gm21Project");
            Scribe_Defs.Look(ref pendingStuff, "gm21PendingStuff");
            Scribe_Values.Look(ref nextProjectId, "gm21NextProjectId");
            Scribe_Deep.Look(ref contents, "gm21Contents", this);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && contents == null) contents = new ThingOwner<Thing>(this, false, LookMode.Deep);
        }

        internal bool Usable
        {
            get
            {
                return Spawned && Faction == Faction.OfPlayer && !this.IsBurning() && CurrentlyUsableForBills()
                    && Config != null && Config.Catalyst != null && Config.Research != null && Config.Research.IsFinished
                    && Map.designationManager.DesignationOn(this, DesignationDefOf.Deconstruct) == null
                    && Map.designationManager.DesignationOn(this, DesignationDefOf.Uninstall) == null;
            }
        }

        internal void Choose(RecipeDef recipe, ThingDef stuff)
        {
            if (project != null || HasRecovery || !Usable || !TranscendentRecipes.EligibleSet.Contains(recipe) || !recipe.AvailableNow) return;
            if (recipe.products[0].thingDef.MadeFromStuff && !TranscendentRecipes.Materials(recipe).Contains(stuff)) return;
            billStack.Clear();
            Bill_Production bill = new Bill_Production(recipe);
            // This internal bill supplies vanilla ingredient filters/reference persistence only.
            // The bench has no Bills tab and is not in any vanilla WorkGiver's fixed giver list.
            bill.allowedSkillRange = new IntRange(0, int.MaxValue);
            if (stuff != null)
                foreach (ThingDef d in recipe.ingredients[0].filter.AllowedThingDefs) bill.ingredientFilter.SetAllow(d, d == stuff);
            billStack.AddBill(bill);
            pendingStuff = stuff;
            nextSearchTick = 0;
        }

        internal bool TryCommit(Pawn pawn, Job job)
        {
            if (project != null || HasRecovery || !Usable || !TranscendentRecipes.MeetsRecipe(pawn, Pending == null ? null : Pending.recipe)
                || job.bill != Pending || !Pending.recipe.AvailableNow) return false;
            string reason;
            if (!TranscendentRecipes.IsSupported(Pending.recipe, out reason)) return false;
            // Vanilla populates this stat on normal recipes. Non-neutral modded values would
            // change output quantity; reject before staging/consumption or the one-time roll.
            if (!TranscendentRecipes.HasNeutralEfficiency(Pending.recipe, this)) return false;
            List<ThingCount> selected;
            Thing dominant;
            if (!TranscendentIngredients.TryValidatePlaced(this, pawn, job, out selected, out dominant)) return false;
            RecipeDef recipe = Pending.recipe;
            double total = Config.Work(recipe.WorkAmountForStuff(pendingStuff));
            if (double.IsNaN(total) || double.IsInfinity(total) || total <= 0) return false;
            TranscendentProject prepared = new TranscendentProject
            {
                recipe = recipe, product = recipe.products[0].thingDef, stuff = pendingStuff, ceiling = Config.ceiling,
                initiatorId = pawn.GetUniqueLoadID(), initiatorName = pawn.LabelShortCap.ToString(),
                totalWork = total, applyColor = recipe.useIngredientsForColor, color = dominant.DrawColor
            };
            foreach (ThingCount c in selected) prepared.ingredients.Add(new CommittedIngredient(c.Thing.def, c.Count));

            // No irreversible consumption or random choice until every selected quantity is held.
            Thing detached = null;
            try
            {
                foreach (ThingCount c in selected)
                {
                    // Keep the whole-stack reference even if a despawn callback throws.
                    detached = c.Count == c.Thing.stackCount ? c.Thing : null;
                    detached = c.Thing.SplitOff(c.Count);
                    if (detached.Spawned) detached.DeSpawn();
                    if (!contents.TryAdd(detached, false))
                        throw new InvalidOperationException("Could not stage ingredient " + detached.def.defName);
                    detached = null;
                }
            }
            catch (Exception ex)
            {
                // Native unbounded ThingOwner accepts these resource items. Preserve any piece
                // detached before a callback failed, without deep-saving an already spawned item.
                if (detached != null && !detached.Destroyed && !detached.Spawned && detached.holdingOwner == null)
                    contents.TryAdd(detached, false);
                // Uncommitted leftovers remain saved in contents if no drop cell is available.
                RecoverUncommitted();
                Log.Error("[Grandmaster 21] Transcendent commitment rolled back before tier selection: " + ex);
                return false;
            }
            prepared.id = GetUniqueLoadID() + ":" + (++nextProjectId);
            prepared.seed = Rand.Int;
            prepared.finalTier = ArtifactRolls.Roll(prepared.ceiling, prepared.seed);
            prepared.phenomenonSeed = unchecked(prepared.seed ^ 0x473231);
            prepared.phenomenon = ArtifactIdentity.Assign(prepared.product, prepared.finalTier, prepared.phenomenonSeed);
            ApplyDeveloperOverride(prepared);
            project = prepared; // Persistence boundary; from here onward nothing is refundable.
            job.bill = null; // Remove the job's reference BEFORE deleting its internal bill.
            job.placedThings = null;
            billStack.Clear();
            pendingStuff = null;
            try { contents.ClearAndDestroyContents(DestroyMode.Vanish); }
            catch (Exception ex) { Fault("consuming committed inputs", ex); return false; }
            return true;
        }

        internal void RecoverUncommitted()
        {
            if (project == null && Spawned) contents.TryDropAll(InteractionCell, Map, ThingPlaceMode.Near);
        }

        internal bool Work(Pawn pawn, string id, int ticks)
        {
            if (!Usable || !TranscendentRecipes.CanCraft(pawn) || project == null || project.id != id
                || !project.Valid || project.faulted || ticks <= 0) return false;
            RecipeDef r = project.recipe;
            double actual = r.workSpeedStat == null ? 1d : pawn.GetStatValue(r.workSpeedStat);
            if (r.workTableSpeedStat != null) actual *= this.GetStatValue(r.workTableSpeedStat);
            double speed = TranscendentMath.EffectiveSpeed(actual);
            project.completedWork = Math.Min(project.totalWork, project.completedWork + speed * ticks);
            return true;
        }

        internal bool TryComplete(Pawn pawn, string id)
        {
            if (!Usable || !TranscendentRecipes.CanCraft(pawn) || project == null || project.id != id
                || !project.Valid || project.faulted || project.completedWork < project.totalWork) return false;
            TranscendentProject p = project;
            Thing result = null;
            try
            {
                if (!p.outputCreated)
                {
                    if (contents.Count != 0) throw new InvalidOperationException("Committed ingredients were not fully consumed");
                    result = ThingMaker.MakeThing(p.product, p.stuff);
                    result.stackCount = 1;
                    CompQuality quality = result.TryGetComp<CompQuality>();
                    CompArtifact artifact = result.TryGetComp<CompArtifact>();
                    if (quality == null || artifact == null) throw new InvalidOperationException("Output lacks required components");
                    quality.SetQuality(QualityCategory.Legendary, ArtGenerationContext.Colony);
                    artifact.tier = p.finalTier;
                    artifact.projectId = p.id;
                    artifact.initiatorName = p.initiatorName;
                    artifact.phenomenon = p.phenomenon;
                    artifact.phenomenonSeed = p.phenomenonSeed;
                    if (p.applyColor) result.SetColor(p.color, false);
                    CompIngredients provenance = result.TryGetComp<CompIngredients>();
                    if (provenance != null)
                        foreach (CommittedIngredient ing in p.ingredients)
                            if (ing.def != null && !TranscendentTierConfig.IsCatalyst(ing.def)) provenance.RegisterIngredient(ing.def);
                    if (pawn.Ideo != null) result.StyleDef = pawn.Ideo.GetStyleFor(result.def);
                    result.TryGetComp<CompArt>()?.JustCreatedBy(pawn);
                    if (!contents.TryAdd(result, false)) throw new InvalidOperationException("Cannot retain completed output");
                    p.outputCreated = true;
                }
                if (!p.outputDelivered)
                {
                    if (contents.Count != 1) throw new InvalidOperationException("Completed output is missing; refusing to regenerate it");
                    result = contents[0];
                    Thing delivered;
                    bool placed = contents.TryDrop(result, InteractionCell, Map, ThingPlaceMode.Near, out delivered,
                        (thing, count) => { p.outputDelivered = true; });
                    if (!placed && !p.outputDelivered)
                    {
                        nextSearchTick = Find.TickManager.TicksGame + 300;
                        return false; // Same saved item retries; no generation/roll.
                    }
                }
                if (result != null && contents.Contains(result)) contents.Remove(result);
                project = null;
                Messages.Message("GM21_TC_Completed".Translate(p.product.LabelCap), this, MessageTypeDefOf.PositiveEvent, false);
                return true;
            }
            catch (Exception ex)
            {
                // A mod callback can throw after spawning. The placement latch prevents a duplicate.
                if (p.outputDelivered || (p.outputCreated && result != null && result.Spawned))
                {
                    // GenDrop can throw in a sound/mod callback after placement but before removing
                    // the owner entry. Never serialize a map item a second time as a held item.
                    if (result != null && contents.Contains(result)) contents.Remove(result);
                    project = null;
                    Log.Warning("[Grandmaster 21] Output was delivered, but a completion callback failed: " + ex.Message);
                    return true;
                }
                if (result != null && !p.outputCreated && !result.Destroyed) result.Destroy(DestroyMode.Vanish);
                Fault("completing project", ex);
                return false;
            }
        }

        private void Fault(string phase, Exception ex)
        {
            if (project != null) project.faulted = true;
            Log.Error("[Grandmaster 21] Transcendent project paused after error " + phase + "; state retained, no refund/reroll: " + ex);
        }

        public override AcceptanceReport DeconstructibleBy(Faction faction)
        {
            if (project != null || contents.Count != 0) return "GM21_TC_NoDeconstruct".Translate().ToString();
            return base.DeconstructibleBy(faction);
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            // Destroy before base callbacks/salvage can see any project contents.
            contents.ClearAndDestroyContents(DestroyMode.Vanish);
            project = null;
            billStack.Clear();
            base.Destroy(mode);
        }

        public override string GetInspectString()
        {
            StringBuilder text = new StringBuilder(base.GetInspectString());
            if (text.Length > 0) text.AppendLine();
            if (project != null)
            {
                text.Append("GM21_TC_Progress".Translate(project.product == null ? "?" : project.product.LabelCap.ToString(),
                    project.stuff == null ? "GM21_TC_NoStuff".Translate().ToString() : project.stuff.LabelCap.ToString(), project.Progress.ToStringPercent(), project.ceiling.ToString()));
                if (!project.Valid || project.faulted) text.AppendLine().Append("GM21_TC_Faulted".Translate());
                else if (project.outputCreated) text.AppendLine().Append("GM21_TC_OutputWaiting".Translate());
            }
            else if (HasRecovery) text.Append("GM21_TC_Recovery".Translate());
            else if (Pending != null) text.Append("GM21_TC_Queued".Translate(Pending.recipe.LabelCap));
            else text.Append("GM21_TC_Idle".Translate());
            return text.ToString().TrimEnd();
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos()) yield return g;
            if (Faction != Faction.OfPlayer) yield break;
            foreach (Gizmo dev in DeveloperGizmos()) yield return dev;
            if (HasRecovery)
                yield return new Command_Action
                {
                    defaultLabel = "GM21_TC_Recover".Translate(), defaultDesc = "GM21_TC_Recovery".Translate(),
                    action = RecoverUncommitted
                };
            if (project == null && !HasRecovery)
            {
                Command_Action choose = new Command_Action
                {
                    defaultLabel = "GM21_TC_Choose".Translate(), defaultDesc = "GM21_TC_ChooseDesc".Translate(),
                    icon = def.uiIcon, action = () => Find.WindowStack.Add(new Dialog_MagicalRecipes(this))
                };
                if (!Usable || !TranscendentIngredients.Available) choose.Disable("GM21_TC_Unavailable".Translate());
                yield return choose;
            }
            if (Prefs.DevMode && project != null)
                yield return new Command_Action
                {
                    defaultLabel = "DEV: Destroy project", defaultDesc = "Destroys the committed project and any pending output. No refund.",
                    action = () => { contents.ClearAndDestroyContents(); project = null; }
                };
        }
    }
}
