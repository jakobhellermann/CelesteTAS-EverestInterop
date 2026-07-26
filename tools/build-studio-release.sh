#!/usr/bin/env bash
set -euo pipefail

# Publishes CelesteStudio for every supported platform/arch and packs each into a release zip with its MD5.
# The zips are what the download host (e.g. GitHub Releases) serves; the printed MD5s fill the ##CHECKSUM_*##
# tokens in StudioHelper.cs (URLs/filenames fill the ##URL_*## / ##FILENAME_*## tokens). See StudioInstaller.cs
# for how the mod consumes them at runtime.
#
# EnableWindowsTargeting lets the WPF (Windows) build cross-publish from Linux/macOS, so this runs on one machine.

cd "$(dirname "$0")/.."

OUT="thunderstore/build/studio-release"

# label : project : rid : tfm
TARGETS=(
    "windows_x64:Studio/CelesteStudio.WPF/CelesteStudio.WPF.csproj:win-x64:net8.0-windows"
    "linux_x64:Studio/CelesteStudio.GTK/CelesteStudio.GTK.csproj:linux-x64:net8.0"
    "macos_x64:Studio/CelesteStudio.Mac/CelesteStudio.Mac.csproj:osx-x64:net8.0"
    "macos_arm64:Studio/CelesteStudio.Mac/CelesteStudio.Mac.csproj:osx-arm64:net8.0"
)

rm -rf "$OUT"
mkdir -p "$OUT"

for entry in "${TARGETS[@]}"; do
    IFS=":" read -r label proj rid tfm <<< "$entry"

    echo "==> publishing $proj ($rid)"
    dotnet publish "$proj" -c Release -r "$rid" --self-contained true -p:EnableWindowsTargeting=true

    src="$(dirname "$proj")/bin/Release/$tfm/$rid/publish"
    zip="$OUT/CelesteStudio-$label.zip"
    # Zip the publish dir contents flat; .pdb is debug symbols only, drop it to keep the archive smaller.
    (cd "$src" && rm -f ./*.pdb && zip -r -q "$OLDPWD/$zip" .)

    md5="$(md5sum "$zip" | cut -d' ' -f1)"
    echo "$md5  CelesteStudio-$label.zip" >> "$OUT/checksums.txt"
done

echo "==> release artifacts in $OUT"
cat "$OUT/checksums.txt"
