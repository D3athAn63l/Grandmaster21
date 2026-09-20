#!/usr/bin/env bash
# Verify Grandmaster 21 against the REAL RimWorld assemblies.
#
#   ./tools/verify-real.sh /path/to/RimWorld/.../Managed /path/to/0Harmony.dll
#
# Runs three checks that a compile cannot replace:
#
#   1. VerifyRuntimeTargets -- every member the mod resolves reflectively at runtime, plus every
#      Harmony injection parameter NAME. Harmony binds prefix/postfix arguments by name, so a
#      renamed vanilla parameter compiles perfectly and throws at patch time. This catches that.
#   2. VerifyTranspiler -- confirms the SkillRecord.Learn IL pattern still matches exactly one
#      site in the shipped assembly, and that the level-up ceiling is left alone.
#   3. Harness -- the progression suite, driven against the real SkillRecord and the real XP curve.
#
# Not covered here: live Harmony patching, which needs the full Unity runtime (Burst, Mathematics,
# SharedInternalsModule) that does not ship in Managed/. That is in-game testing.
set -euo pipefail
cd "$(dirname "$0")/.."
MANAGED="${1:?usage: verify-real.sh <Managed dir> <0Harmony.dll>}"
HARMONY="${2:?usage: verify-real.sh <Managed dir> <0Harmony.dll>}"
[ -f "Assemblies/Grandmaster21.dll" ] || { echo "build first: ./build.sh $MANAGED $HARMONY" >&2; exit 1; }

OUT="$(mktemp -d)"
FACADE=/usr/lib/mono/4.5/Facades/netstandard.dll
CECIL="$(find /usr/lib/mono/gac/Mono.Cecil -name 'Mono.Cecil.dll' | sort | tail -1)"
cp "$MANAGED"/*.dll "$HARMONY" Assemblies/Grandmaster21.dll "$OUT/"
[ -n "$CECIL" ] && cp "$CECIL" "$OUT/"

REFS="-r:$FACADE -r:$OUT/Assembly-CSharp.dll -r:$OUT/UnityEngine.dll -r:$OUT/UnityEngine.CoreModule.dll -r:$OUT/UnityEngine.IMGUIModule.dll -r:$OUT/0Harmony.dll -r:$OUT/Grandmaster21.dll"

echo "=== 1. Runtime targets and Harmony parameter names ==="
mcs -out:"$OUT/verify.exe" $REFS Tests/VerifyRuntimeTargets.cs
( cd "$OUT" && mono verify.exe )

if [ -n "$CECIL" ]; then
  echo; echo "=== 2. Learn transpiler IL pattern ==="
  mcs -out:"$OUT/vt.exe" -r:"$OUT/Mono.Cecil.dll" Tests/VerifyTranspiler.cs
  ( cd "$OUT" && mono vt.exe Assembly-CSharp.dll )
else
  echo "=== 2. Learn transpiler IL pattern: SKIPPED (Mono.Cecil not installed) ==="
fi

echo; echo "=== 3. Progression suite against the real SkillRecord ==="
mcs -out:"$OUT/harness.exe" $REFS Tests/Harness.cs
( cd "$OUT" && mono harness.exe )
