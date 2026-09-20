#!/usr/bin/env bash
# Build Grandmaster 21 against a RimWorld 1.6 install.
#   ./build.sh /path/to/RimWorld/RimWorldWin64_Data/Managed /path/to/0Harmony.dll
set -euo pipefail
MANAGED="${1:?usage: build.sh <Managed dir> <0Harmony.dll>}"
HARMONY="${2:?usage: build.sh <Managed dir> <0Harmony.dll>}"
MONO=/usr/lib/mono/4.5
mkdir -p Assemblies
mcs -target:library -out:Assemblies/Grandmaster21.dll -optimize+ -nostdlib -noconfig -warn:2 \
  -r:"$MONO/mscorlib.dll" -r:"$MONO/System.dll" -r:"$MONO/System.Core.dll" \
  -r:"$MONO/Facades/netstandard.dll" \
  -r:"$MANAGED/Assembly-CSharp.dll" \
  -r:"$MANAGED/UnityEngine.dll" -r:"$MANAGED/UnityEngine.CoreModule.dll" \
  -r:"$MANAGED/UnityEngine.IMGUIModule.dll" -r:"$MANAGED/UnityEngine.TextRenderingModule.dll" \
  -r:"$HARMONY" \
  Source/*.cs
echo "Built Assemblies/Grandmaster21.dll"
