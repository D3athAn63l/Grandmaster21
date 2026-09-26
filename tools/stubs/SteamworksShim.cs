// TEST-ONLY. Never shipped, never referenced by the mod.
//
// Verse.ParseHelper's static constructor registers a parser for Steamworks.PublishedFileId_t, and
// every Scribe LOAD parses its values through ParseHelper. Steamworks.NET ships with the game
// launcher, not in Managed/, so headless tools cannot load a save without it. This is the one member
// ParseHelper touches (its constructor), compiled under the assembly name the game references,
// com.rlabrecque.steamworks.net. Vanilla's own int/float/enum parsers -- the ones the checks
// actually exercise -- are untouched.
namespace Steamworks
{
    public struct PublishedFileId_t
    {
        public ulong m_PublishedFileId;
        public PublishedFileId_t(ulong value) { m_PublishedFileId = value; }
    }
}
