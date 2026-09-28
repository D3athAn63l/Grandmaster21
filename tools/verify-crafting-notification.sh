#!/usr/bin/env bash
# Crafting Grandmaster Legendary letter toggle: headless checks against the REAL RimWorld 1.6, Unity
# and Harmony assemblies. Re-derives the vanilla letter path from the real IL (Mono.Cecil), installs
# the mod's real patches on the real vanilla methods, executes vanilla's GenRecipe.PostProcessProduct
# and QualityUtility.SendCraftNotification, and round-trips the setting through the real
# LoadedModManager settings reader/writer. Does not launch RimWorld.
#
#   ./tools/verify-crafting-notification.sh <Managed> <0Harmony.dll> <Mono.Cecil.dll>
#
# Build the mod first (./build.sh). CSC, FRAMEWORK_REFS and RUNNER mirror verify-medicine.sh.
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
# Test-only: lets vanilla ParseHelper initialise so the real settings reader can run. See the shim's header.
[ -f "$OUT/com.rlabrecque.steamworks.net.dll" ] || \
  "$COMPILER" -nologo -target:library -out:"$OUT/com.rlabrecque.steamworks.net.dll" tools/stubs/SteamworksShim.cs
"$COMPILER" -nologo -target:exe -out:"$OUT/crafting-notification.exe" -nostdlib -noconfig -warn:2 \
  -r:"$FRAMEWORK/mscorlib.dll" -r:"$FRAMEWORK/System.dll" -r:"$FRAMEWORK/System.Core.dll" \
  -r:"$FRAMEWORK/System.Xml.dll" -r:"$FRAMEWORK/System.Xml.Linq.dll" -r:"$FRAMEWORK/Facades/netstandard.dll" \
  -r:"$OUT/Assembly-CSharp.dll" -r:"$OUT/UnityEngine.CoreModule.dll" -r:"$OUT/0Harmony.dll" \
  -r:"$OUT/Mono.Cecil.dll" -r:"$OUT/Grandmaster21.dll" Tests/CraftingNotificationChecks.cs
( cd "$OUT" && "$RUNNER" crafting-notification.exe Grandmaster21.dll Assembly-CSharp.dll "$OUT" "$OLDPWD" )
