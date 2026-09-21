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
#   3. PatchAllTest -- LIVE Harmony patching: every patch is applied to the real RimWorld method
#      it targets, one at a time, and the transpiler is confirmed to have rewritten its IL.
#   4. VerifyFinalizerSemantics -- proves by execution that the mod's cleanup finalizers do not
#      suppress exceptions thrown by RimWorld or by other mods' patches on the same methods.
#   5. Harness -- the progression suite, driven against the real SkillRecord and the real XP curve.
#
# Checks 2, 3 and 5 need real method BODIES. Reference assemblies (for example the
# Krafs.Rimworld.Ref NuGet package) carry full metadata but no IL, so those three fail with
# "Method has zero rva" against them -- that is the environment, not a finding. Checks 1 and 4
# work against reference assemblies and cover every reflective target and Harmony parameter name.
#
# Targets that cannot be bound outside the game are reported as BLOCKED rather than failed. Two
# causes: a declaring type holding fields typed from a launcher-side assembly
# (Assembly-CSharp-firstpass, UnityEngine.AudioModule, Steamworks.NET) -- copy those in alongside
# Managed/ to clear it -- or a static constructor that touches Unity content or logging, which
# needs the actual player process and cannot be cleared by any set of assemblies.
#
# Not covered here at all: gameplay. Patches binding is not patches behaving.
set -euo pipefail
cd "$(dirname "$0")/.."
MANAGED="${1:?usage: verify-real.sh <Managed dir> <0Harmony.dll>}"
HARMONY="${2:?usage: verify-real.sh <Managed dir> <0Harmony.dll>}"
[ -f "Assemblies/Grandmaster21.dll" ] || { echo "build first: ./build.sh $MANAGED $HARMONY" >&2; exit 1; }

OUT="$(mktemp -d)"
FACADE=/usr/lib/mono/4.5/Facades/netstandard.dll
# sort -V, not plain sort: a lexical sort puts 0.9.5.0 AFTER 0.11.0.0 and picks the ancient
# Cecil, whose ReaderParameters has no ReadWrite/InMemory and fails to compile the IL checker.
CECIL="$(find /usr/lib/mono/gac/Mono.Cecil -name 'Mono.Cecil.dll' | sort -V | tail -1)"
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

echo; echo "=== 3. Live Harmony patching against real RimWorld methods ==="
mcs -out:"$OUT/patchall.exe" $REFS Tests/PatchAllTest.cs
( cd "$OUT" && mono patchall.exe Grandmaster21.dll 2>&1 \
    | grep -vE 'out of sync|update one from git|the other too|Do not report this|you probably have|If you see other|and you need to fix|Your mono runtime|The out of sync|cant resolve internal call|^$' )

echo; echo "=== 4. Harmony finalizer semantics (no exception suppression) ==="
mcs -out:"$OUT/fin.exe" $REFS Tests/VerifyFinalizerSemantics.cs
( cd "$OUT" && mono fin.exe )

echo; echo "=== 5. Progression suite against the real SkillRecord ==="
mcs -out:"$OUT/harness.exe" $REFS Tests/Harness.cs
( cd "$OUT" && mono harness.exe )
