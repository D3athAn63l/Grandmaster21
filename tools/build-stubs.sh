#!/usr/bin/env bash
# Compile-check and run the offline test suites WITHOUT a RimWorld install.
#
# Builds throwaway reference stubs whose signatures mirror Assembly-CSharp / UnityEngine /
# 0Harmony, compiles the mod against them, then runs the offline harnesses.
#
# WHAT THIS PROVES: the source is syntactically valid, internally consistent, and the mod's own
# decision logic behaves correctly.
# WHAT IT DOES NOT PROVE: that every RimWorld member named in the source exists with that exact
# signature in 1.6. Only a real build against the game's assemblies proves that.
#
#   ./tools/build-stubs.sh
set -euo pipefail
cd "$(dirname "$0")/.."
OUT="${GM21_STUB_DIR:-$(mktemp -d)}"
echo "stub dir: $OUT"
mcs -target:library -out:"$OUT/UnityEngine.dll"     tools/stubs/Unity.cs
mcs -target:library -out:"$OUT/0Harmony.dll"        tools/stubs/Harmony.cs
mcs -target:library -out:"$OUT/Assembly-CSharp.dll" -r:"$OUT/UnityEngine.dll" tools/stubs/Rim.cs
mcs -target:library -out:"$OUT/Grandmaster21.dll" -optimize+ -warn:2 \
    -r:"$OUT/Assembly-CSharp.dll" -r:"$OUT/UnityEngine.dll" -r:"$OUT/0Harmony.dll" \
    Source/*.cs Source/Shooting/*.cs
echo "compile OK"
FAILED=0
for t in OfflineHarness OfflineHarness_Shooting; do
  mcs -out:"$OUT/$t.exe" -r:"$OUT/Assembly-CSharp.dll" -r:"$OUT/UnityEngine.dll" \
      -r:"$OUT/0Harmony.dll" -r:"$OUT/Grandmaster21.dll" -r:System.Xml.dll "Tests/$t.cs"
  echo "=== $t ==="
  mono "$OUT/$t.exe" || FAILED=1
done
exit $FAILED
