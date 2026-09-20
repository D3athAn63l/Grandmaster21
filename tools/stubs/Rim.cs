using System;
using System.Collections.Generic;
using UnityEngine;

namespace Verse
{
    public struct TaggedString
    {
        private string s;
        public TaggedString(string v) { s = v; }
        public static implicit operator TaggedString(string v) { return new TaggedString(v); }
        public static implicit operator string(TaggedString v) { return v.s; }
        public override string ToString() { return s; }
        public string RawText { get { return s; } }
    }

    public static class Translator
    {
        // Test hook: populated from the real Languages XML by the harnesses, so CanTranslate
        // answers the same question RimWorld would -- "did this mod actually ship this key?"
        public static readonly HashSet<string> KnownKeys = new HashSet<string>();
        public static bool CanTranslate(this string key) { return KnownKeys.Contains(key); }
        public static TaggedString Translate(this string key) { return new TaggedString(key); }
        public static TaggedString Translate(this string key, params NamedArgument[] args) { return new TaggedString(key); }
    }
    public struct NamedArgument
    {
        public static implicit operator NamedArgument(string s) { return default(NamedArgument); }
        public static implicit operator NamedArgument(int s) { return default(NamedArgument); }
        public static implicit operator NamedArgument(TaggedString s) { return default(NamedArgument); }
    }

    public static class Log
    {
        public static void Message(string s) { Console.WriteLine("[msg] " + s); }
        public static void Warning(string s) { Console.WriteLine("[warn] " + s); }
        public static void Error(string s) { Console.WriteLine("[err] " + s); }
    }

    public class Def { public string defName; public string label; public TaggedString LabelCap { get { return label; } } }

    public class Thing { public ThingDef def; public bool Destroyed; public virtual void PreApplyDamage(ref DamageInfo dinfo, out bool absorbed) { absorbed = false; } }
    public class ThingDef : Def { public bool IsRangedWeapon; }
    public class RaceProperties { public bool IsMechanoid; public bool Animal; public bool Humanlike; }

    public class BodyPartTagDef : Def { }
    public class BodyPartDef : Def { public List<BodyPartTagDef> tags; public float hitPoints; }
    public enum BodyPartDepth { Undefined, Outside, Inside }
    public class BodyPartRecord
    {
        public BodyPartDef def; public BodyPartRecord parent;
        public List<BodyPartRecord> parts = new List<BodyPartRecord>();
        public BodyPartDepth depth = BodyPartDepth.Outside;
        public float coverageAbsWithChildren;
        public float healthStub = 10f;
        public bool missingStub;
    }
    public class DamageDef : Def { public bool harmsHealth; }
    public struct DamageInfo
    {
        public DamageDef Def { get; set; }
        public Thing Instigator { get; set; }
        public ThingDef Weapon { get; set; }
        public Thing IntendedTarget { get; set; }
        public BodyPartRecord HitPart { get; private set; }
        public void SetHitPart(BodyPartRecord part) { HitPart = part; }
    }
    public class Hediff { }
    public class HediffSet
    {
        public List<BodyPartRecord> parts = new List<BodyPartRecord>();
        public IEnumerable<BodyPartRecord> GetNotMissingParts()
        { foreach (var p in parts) if (!p.missingStub) yield return p; }
        public float GetPartHealth(BodyPartRecord p) { return p.healthStub; }
        public bool PartIsMissing(BodyPartRecord p) { return p.missingStub; }
    }

    public class Faction { public bool IsPlayer; }

    public class Gizmo { }
    public class Command : Gizmo { public string defaultLabel; public string defaultDesc; public Texture2D icon; }
    public class Command_Action : Command { public System.Action action; }
    public class FloatMenuOption { public FloatMenuOption(string label, System.Action action) { } }
    public class FloatMenu : Window { public FloatMenu(List<FloatMenuOption> options) { } }
    public static class BaseContent { public static Texture2D BadTex = new Texture2D(); }
    public static class ContentFinder<T> where T : class, new() { public static T Get(string path, bool reportFailure = true) { return new T(); } }

    public struct LocalTargetInfo { public Thing Thing; }
    public class Verb
    {
        public Thing caster; public Pawn CasterPawn; public VerbProperties verbProps;
        public LocalTargetInfo CurrentTarget;
        protected internal int burstShotsLeft;          // protected in RimWorld; read reflectively
        protected internal virtual int ShotsPerBurst { get { return shotsPerBurstStub; } }
        public int shotsPerBurstStub = 1;               // test hook
    }
    public class VerbProperties { public float warmupTime; public bool IsMeleeAttack; }
    public class Verb_LaunchProjectile : Verb { }
    public class Stance { }
    public class Stance_Busy : Stance { public Stance_Busy(int ticks, LocalTargetInfo focusTarg, Verb verb) { } }
    public class Stance_Warmup : Stance_Busy { public Stance_Warmup(int ticks, LocalTargetInfo focusTarg, Verb verb) : base(ticks, focusTarg, verb) { } }
    public class Stance_Cooldown : Stance_Busy { public Stance_Cooldown(int ticks, LocalTargetInfo focusTarg, Verb verb) : base(ticks, focusTarg, verb) { } }

    public class ModMetaData { public string PackageId; }
    public static class ModsConfig { public static IEnumerable<ModMetaData> ActiveModsInLoadOrder { get { return new List<ModMetaData>(); } } }

    public static class DefDatabase<T> where T : Def, new()
    {
        // Test hook: the real DefDatabase is populated from XML at load.
        public static readonly Dictionary<string, T> Registry = new Dictionary<string, T>();
        public static T GetNamedSilentFail(string defName)
        { T v; return Registry.TryGetValue(defName, out v) ? v : null; }
    }

    public class Pawn : Thing
    {
        public RimWorld.Pawn_SkillTracker skills;
        public RimWorld.Pawn_HealthTracker health;
        public RaceProperties RaceProps;
        public bool IsColonist;
        public bool Dead;
        public bool Downed;
        public Faction Faction;
        public TaggedString LabelShortCap { get { return "pawn"; } }
        public override void PreApplyDamage(ref DamageInfo dinfo, out bool absorbed) { absorbed = false; }
        public virtual IEnumerable<Gizmo> GetGizmos() { return new List<Gizmo>(); }
        public virtual void ExposeData() { }
    }

    public class Corpse : Thing { public Pawn InnerPawn; }

    public struct LookTargets
    {
        public static implicit operator LookTargets(Thing t) { return default(LookTargets); }
    }

    public class MessageTypeDef : Def { }
    public static class MessageTypeDefOf
    {
        public static MessageTypeDef PositiveEvent = new MessageTypeDef();
        public static MessageTypeDef NeutralEvent = new MessageTypeDef();
        public static MessageTypeDef RejectInput = new MessageTypeDef();
    }
    public static class Messages
    {
        public static void Message(TaggedString t, LookTargets l, MessageTypeDef d, bool historical = true) { }
        public static void Message(string t, LookTargets l, MessageTypeDef d, bool historical = true) { }
        public static void Message(string t, MessageTypeDef d, bool historical = true) { }
        public static void Message(TaggedString t, MessageTypeDef d, bool historical = true) { }
    }

    public enum ThingRequestGroup { Undefined, Corpse, ThingHolder }
    public class ListerThings { public List<Thing> ThingsInGroup(ThingRequestGroup g) { return new List<Thing>(); } }
    public class MapPawns { public List<Pawn> AllPawns { get { return new List<Pawn>(); } } }
    public class Map { public MapPawns mapPawns; public ListerThings listerThings; }

    public class Game { }
    public enum ProgramState { Entry, MapInitializing, Playing }
    public static class Current
    {
        public static Game Game;
        public static ProgramState ProgramState;
    }

    public class Window { }
    public class WindowStack { public void Add(Window w) { } }
    public class Dialog_MessageBox : Window
    {
        public Dialog_MessageBox(TaggedString text, string buttonAText = null, Action buttonAAction = null,
            string buttonBText = null, Action buttonBAction = null, string title = null,
            bool buttonADestructive = false, Action acceptAction = null, Action cancelAction = null) { }
    }

    public static class Find
    {
        public static List<Map> Maps { get { return new List<Map>(); } }
        public static RimWorld.Planet.WorldPawns WorldPawns { get { return null; } }
        public static RimWorld.Planet.WorldObjectsHolder WorldObjects { get { return null; } }
        public static WindowStack WindowStack = new WindowStack();
    }

    public static class PawnsFinder
    {
        public static List<Pawn> AllMapsWorldAndTemporary_AliveOrDead { get { return new List<Pawn>(); } }
    }

    public enum LoadSaveMode { Inactive, Saving, LoadingVars, ResolvingCrossRefs, PostLoadInit }
    public static class Scribe { public static LoadSaveMode mode; }
    public static class Scribe_Values
    {
        public static void Look<T>(ref T value, string label, T defaultValue = default(T), bool forceSave = false) { }
    }
    [AttributeUsage(AttributeTargets.Field)] public class UnsavedAttribute : Attribute { public UnsavedAttribute(bool allowLoading = false) { } }
    [AttributeUsage(AttributeTargets.Class)] public class StaticConstructorOnStartupAttribute : Attribute { }

    public class ModContentPack { }
    public class ModSettings { public virtual void ExposeData() { } }
    public class Mod
    {
        public Mod(ModContentPack content) { }
        public T GetSettings<T>() where T : ModSettings, new() { return new T(); }
        public virtual string SettingsCategory() { return ""; }
        public virtual void DoSettingsWindowContents(Rect inRect) { }
    }

    public enum GameFont { Tiny, Small, Medium }
    public static class Text { public static TextAnchor Anchor; public static GameFont Font; }
    public static class Widgets
    {
        public static void Label(Rect r, string s) { }
        public static void Label(Rect r, TaggedString s) { }
        public static bool ButtonText(Rect r, string s, bool drawBackground = true, bool doMouseoverSound = true, bool active = true) { return false; }
        public static bool ButtonText(Rect r, TaggedString s, bool drawBackground = true, bool doMouseoverSound = true, bool active = true) { return false; }
    }
    public class Listing_Standard
    {
        public void Begin(Rect r) { }
        public void End() { }
        public void Label(string s, float maxHeight = -1f, string tooltip = null) { }
        public void Label(TaggedString s, float maxHeight = -1f, string tooltip = null) { }
        public string TextEntry(string text, int lineCount = 1) { return text; }
        public bool ButtonText(string label, string highlightTag = null) { return false; }
        public bool ButtonText(TaggedString label, string highlightTag = null) { return false; }
        public void CheckboxLabeled(string label, ref bool checkOn, string tooltip = null) { }
        public void CheckboxLabeled(TaggedString label, ref bool checkOn, TaggedString tooltip = default(TaggedString)) { }
        public void GapLine(float gapHeight = 12f) { }
        public void Gap(float gapHeight = 12f) { }
        public Rect GetRect(float height, float widthPct = 1f) { return new Rect(0,0,100,height); }
    }
}

namespace RimWorld
{
    using Verse;

    public class SkillDef : Def { }
    public static class SkillDefOf { public static SkillDef Shooting = new SkillDef { defName = "Shooting" }; }

    public class PawnCapacityDef : Def { }
    public static class PawnCapacityDefOf
    {
        public static PawnCapacityDef Consciousness = new PawnCapacityDef();
        public static PawnCapacityDef Manipulation = new PawnCapacityDef();
        public static PawnCapacityDef Moving = new PawnCapacityDef();
    }
    public class PawnCapacitiesHandler { public bool CapableOf(PawnCapacityDef d) { return true; } }
    public class Pawn_HealthTracker
    {
        public PawnCapacitiesHandler capacities = new PawnCapacitiesHandler();
        public HediffSet hediffSet = new HediffSet();
        public bool forceDowned;
        private Pawn pawn;                                  // private in RimWorld; read reflectively
        public bool Downed { get; set; }
        public Pawn_HealthTracker() { }
        public Pawn_HealthTracker(Pawn p) { pawn = p; }
        private void CheckForStateChange(DamageInfo? dinfo, Hediff hediff) { }
    }

    public struct ShotReport
    {
        public float AimOnTargetChance_IgnoringPosture { get { return 1f; } }
        public float PassCoverChance { get { return 1f; } }
        public float TotalEstimatedHitChance { get { return 1f; } }
        public static ShotReport HitReportFor(Thing caster, Verb verb, LocalTargetInfo target) { return default(ShotReport); }
    }

    public class SkillRecord
    {
        public const int MaxLevel = 20;
        public SkillDef def;
        public int levelInt;
        public float xpSinceLastLevel;
        // --- test hooks: additive fields that do not exist in RimWorld. They only back the
        //     properties below, whose SIGNATURES match the real API exactly. ---
        public Pawn pawnStub;
        public int aptitudeStub;
        public bool totallyDisabledStub;
        public bool permanentlyDisabledStub;

        public Pawn Pawn { get { return pawnStub; } }
        public int Aptitude { get { return aptitudeStub; } }
        public bool TotallyDisabled { get { return totallyDisabledStub; } }
        public bool PermanentlyDisabled { get { return permanentlyDisabledStub; } }
        public float XpRequiredForLevelUp { get { return XpRequiredToLevelUpFrom(levelInt); } }
        public string LevelDescriptor { get { return "Skill" + GetLevelForUI(); } }
        public int Level { get { return GetLevel(); } set { levelInt = Mathf.Clamp(value, 0, MaxLevel); } }
        public int GetLevel(bool includeAptitudes = true) { return Mathf.Clamp(levelInt + (includeAptitudes ? Aptitude : 0), 0, MaxLevel); }
        public int GetLevelForUI(bool includeAptitudes = true) { return GetLevel(includeAptitudes); }
        public float LearnRateFactor(bool direct = false) { return 1f; }
        public void Learn(float xp, bool direct = false, bool ignoreLearnRate = false) { }
        public void Interval() { }
        public void ExposeData() { }
        // Mirrors vanilla's XpForLevelUpCurve: (0,1000) (9,10000) (19,30000), clamped tails.
        public static float XpRequiredToLevelUpFrom(int startingLevel)
        {
            if (startingLevel <= 0) return 1000f;
            if (startingLevel >= 19) return 30000f;
            if (startingLevel <= 9) return 1000f + (10000f - 1000f) * (startingLevel / 9f);
            return 10000f + (30000f - 10000f) * ((startingLevel - 9) / 10f);
        }
    }

    public class Pawn_SkillTracker
    {
        public List<SkillRecord> skills = new List<SkillRecord>();
        public SkillRecord GetSkill(SkillDef def)
        {
            for (int i = 0; i < skills.Count; i++) if (skills[i].def == def) return skills[i];
            return null;
        }
    }

    public enum QualityCategory : byte { Awful, Poor, Normal, Good, Excellent, Masterwork, Legendary }

    public static class QualityUtility
    {
        public static QualityCategory GenerateQualityCreatedByPawn(int relevantSkillLevel, bool inspired) { return QualityCategory.Normal; }
        public static QualityCategory GenerateQualityCreatedByPawn(Pawn pawn, SkillDef relevantSkill, bool forcedInspired = false) { return QualityCategory.Normal; }
    }

    public static class SkillUI
    {
        public enum SkillDrawMode { Gameplay, Menu }
        public static void DrawSkill(SkillRecord skill, Rect holdingRect, SkillDrawMode mode = SkillDrawMode.Gameplay, string tooltipPrefix = "") { }
        private static string GetSkillDescription(SkillRecord sk) { return ""; }
    }
}

namespace RimWorld.Planet
{
    using Verse;
    public class WorldPawns { public List<Pawn> AllPawnsAliveOrDead { get { return new List<Pawn>(); } } }
    public class Caravan { public List<Pawn> PawnsListForReading { get { return new List<Pawn>(); } } }
    public class WorldObjectsHolder { public List<Caravan> Caravans { get { return new List<Caravan>(); } } }
}

namespace LudeonTK
{
    using System;
    public enum DebugActionType { Action, ToolMap, ToolMapForPawns, ToolWorld }
    [Flags] public enum AllowedGameStates { Invalid = 0, Entry = 1, Playing = 2, PlayingOnMap = 6, PlayingOnWorld = 10, IsCurrentlyOnMap = 4, HasGameCondition = 8 }
    [AttributeUsage(AttributeTargets.Method)]
    public class DebugActionAttribute : Attribute
    {
        public DebugActionType actionType;
        public AllowedGameStates allowedGameStates;
        public DebugActionAttribute(string category, string name = null) { }
    }
}
