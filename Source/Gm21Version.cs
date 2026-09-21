using System.Reflection;

[assembly: AssemblyTitle("Grandmaster 21")]
[assembly: AssemblyProduct("Grandmaster 21")]
[assembly: AssemblyVersion(Grandmaster21.Gm21Version.Assembly)]
[assembly: AssemblyFileVersion(Grandmaster21.Gm21Version.Assembly)]
[assembly: AssemblyInformationalVersion(
    Grandmaster21.Gm21Version.Display + " (" + Grandmaster21.Gm21BuildStamp.Stamp + ")")]

namespace Grandmaster21
{
    /// <summary>
    /// Version identity, logged once at startup so a stale DLL is obvious in Player.log.
    ///
    /// This stays at 0.x/Beta until the runtime regression checklist has actually been run in
    /// RimWorld. Compiling is not shipping.
    /// </summary>
    public static class Gm21Version
    {
        /// <summary>Must be a plain numeric assembly version -- attributes reject suffixes.</summary>
        public const string Assembly = "0.10.2.0";

        public const string Number = "0.10.2";
        public const string Stage = "Beta";
        public const string Display = Number + " " + Stage;

        /// <summary>"0.9.0 Beta (built 2026-09-20T.., commit abc1234)"</summary>
        public static string Full
        {
            get { return Display + " (" + Gm21BuildStamp.Stamp + ")"; }
        }
    }
}
