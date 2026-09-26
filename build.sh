#!/usr/bin/env bash
# Build Grandmaster 21 against a RimWorld 1.6 install.
#   ./build.sh /path/to/RimWorld/RimWorldWin64_Data/Managed /path/to/0Harmony.dll
#
# Stamps Source/Gm21BuildStamp.cs with the build time and git commit before compiling, so the
# assembly reports its own provenance in Player.log. If you ever see "unstamped build" there, the
# DLL was not produced by this script.
set -euo pipefail
MANAGED="${1:?usage: build.sh <Managed dir> <0Harmony.dll>}"
HARMONY="${2:?usage: build.sh <Managed dir> <0Harmony.dll>}"
MONO="${FRAMEWORK_REFS:-/usr/lib/mono/4.5}"
COMPILER="${CSC:-mcs}"

[ -f "$MANAGED/Assembly-CSharp.dll" ] || { echo "error: no Assembly-CSharp.dll in $MANAGED" >&2; exit 1; }
[ -f "$HARMONY" ] || { echo "error: no Harmony assembly at $HARMONY" >&2; exit 1; }

# Stamp with the last commit that changed real source, not HEAD, and not counting this file --
# which every build rewrites, and which would otherwise make the reference point to itself.
# Doc and test commits do not change the assembly, so stamping HEAD would make a current DLL look
# stale. The result is an exact staleness check; see Assemblies/README.md.
SRC=(Source ':!Source/Gm21BuildStamp.cs')
STAMP="$(date -u +%Y-%m-%dT%H:%MZ)"
COMMIT="$(git log -1 --format=%h -- "${SRC[@]}" 2>/dev/null)"
[ -n "$COMMIT" ] || COMMIT='no-git'
if ! git diff --quiet HEAD -- "${SRC[@]}" 2>/dev/null; then COMMIT="$COMMIT-dirty"; fi

cat > Source/Gm21BuildStamp.cs <<STAMPEOF
namespace Grandmaster21
{
    /// <summary>
    /// Overwritten by build.sh on every build. The checked-in value is "unstamped build" -- if you
    /// see that in Player.log, the assembly was not produced by build.sh.
    /// </summary>
    public static class Gm21BuildStamp
    {
        public const string Stamp = "built $STAMP, commit $COMMIT";
    }
}
STAMPEOF

mkdir -p Assemblies
"$COMPILER" -target:library -out:Assemblies/Grandmaster21.dll -optimize+ -nostdlib -noconfig -warn:2 \
  -r:"$MONO/mscorlib.dll" -r:"$MONO/System.dll" -r:"$MONO/System.Core.dll" \
  -r:"$MONO/Facades/netstandard.dll" \
  -r:"$MANAGED/Assembly-CSharp.dll" \
  -r:"$MANAGED/UnityEngine.dll" -r:"$MANAGED/UnityEngine.CoreModule.dll" \
  -r:"$MANAGED/UnityEngine.IMGUIModule.dll" -r:"$MANAGED/UnityEngine.TextRenderingModule.dll" \
  -r:"$HARMONY" \
  Source/*.cs Source/Shooting/*.cs Source/Melee/*.cs Source/Medicine/*.cs Source/Transcendent/*.cs

echo "Built Assemblies/Grandmaster21.dll  ($STAMP, commit $COMMIT)"
