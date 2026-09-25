#!/usr/bin/env bash
# Medicine 21 headless checks against the REAL RimWorld 1.6, Unity and Harmony assemblies.
# No stubs: the mod's real Harmony patches are installed on the real vanilla methods, which are then
# executed (CompTended, Hediff_Injury.Heal, SurgeryOutcomeEffectDef.GetOutcome, HediffSet, Scribe).
# Does not launch RimWorld and does not test jobs, UI, revival or whole-game reload.
#
#   ./tools/verify-medicine.sh <Managed> <0Harmony.dll> <Mono.Cecil.dll> [<RimWorld Data dir>]
#
# With the optional Data directory (the game's Data/ folder: Core, Royalty, ... each with Defs/),
# section 13 parses the real vanilla XML with inheritance and runs the REAL C# Cure, tier and
# surgery rules over every vanilla def, asserting the audited results.
#
# Build the mod first (./build.sh). CSC, FRAMEWORK_REFS and RUNNER mirror verify-transcendent.sh.
set -euo pipefail
cd "$(dirname "$0")/.."
MANAGED="$(realpath "${1:?Managed directory required}")"
HARMONY="$(realpath "${2:?Harmony DLL required}")"
CECIL="$(realpath "${3:?Mono.Cecil DLL required}")"
FRAMEWORK="${FRAMEWORK_REFS:-/usr/lib/mono/4.5}"
COMPILER="${CSC:-mcs}"
RUNNER="${RUNNER:-mono}"
[ -f Assemblies/Grandmaster21.dll ] || { echo "build first: ./build.sh <Managed> <0Harmony.dll>" >&2; exit 1; }
OUT="$(mktemp -d)"
trap 'rm -rf "$OUT"' EXIT
cp "$MANAGED"/*.dll "$OUT/"
cp "$HARMONY" "$CECIL" Assemblies/Grandmaster21.dll "$OUT/"
# Test-only: lets vanilla ParseHelper initialise so real Scribe LOADS can run. See the shim's header.
[ -f "$OUT/com.rlabrecque.steamworks.net.dll" ] || \
  "$COMPILER" -nologo -target:library -out:"$OUT/com.rlabrecque.steamworks.net.dll" tools/stubs/SteamworksShim.cs
"$COMPILER" -nologo -target:exe -out:"$OUT/medicine.exe" -nostdlib -noconfig -warn:2 \
  -r:"$FRAMEWORK/mscorlib.dll" -r:"$FRAMEWORK/System.dll" -r:"$FRAMEWORK/System.Core.dll" \
  -r:"$FRAMEWORK/System.Xml.dll" -r:"$FRAMEWORK/System.Xml.Linq.dll" -r:"$FRAMEWORK/Facades/netstandard.dll" \
  -r:"$OUT/Assembly-CSharp.dll" -r:"$OUT/UnityEngine.CoreModule.dll" -r:"$OUT/0Harmony.dll" \
  -r:"$OUT/Mono.Cecil.dll" -r:"$OUT/Grandmaster21.dll" Tests/MedicineChecks.cs
DATA="${4:-}"
[ -z "$DATA" ] || DATA="$(realpath "$DATA")"
( cd "$OUT" && "$RUNNER" medicine.exe Grandmaster21.dll "$OLDPWD" "$OUT/medicine-save.xml" Assembly-CSharp.dll "$DATA" )
