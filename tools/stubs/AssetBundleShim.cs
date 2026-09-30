// TEST-ONLY. Never shipped, never referenced by the mod.
//
// Verse.ReservationManager (the real vanilla reservation table the Cooking checks run against) has a
// static initialiser that builds a debug Material, which touches Verse.ShaderDatabase; that class's
// shader loader falls back to the mod asset bundles, whose lookup method mentions UnityEngine.AssetBundle.
// UnityEngine.AssetBundleModule ships with the game, not in the Managed/ subset headless tools use, and
// Harmony cannot patch (or even inspect) a method whose locals name a missing assembly. This is the one
// type that method mentions, compiled under the assembly name the game references, so it can load. The
// checks never call it: the method is replaced by a "no shader" answer before it would run.
namespace UnityEngine
{
    public class AssetBundle : Object
    {
        public Object LoadAsset(string name, System.Type type) { return null; }
    }
}
