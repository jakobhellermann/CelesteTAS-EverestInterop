#!/usr/bin/env bash
set -euo pipefail


cd "$(dirname "$0")/.."

OUT="thunderstore/build/studio-release"
STUDIO_SUFFIX="$(date +%Y%m%d-%H%M)"
STUDIO_VERSION="$(perl -nle 'print $1 if /<Version>(\S+)<\/Version>/' Studio/CelesteStudio/CelesteStudio.csproj)-$STUDIO_SUFFIX"

TARGETS=(
    "windows-x64:Studio/CelesteStudio.WPF/CelesteStudio.WPF.csproj:win-x64"
    "linux-x64:Studio/CelesteStudio.GTK/CelesteStudio.GTK.csproj:linux-x64"
    "macos-x64:Studio/CelesteStudio.Mac/CelesteStudio.Mac.csproj:osx-x64"
    "macos-arm64:Studio/CelesteStudio.Mac/CelesteStudio.Mac.csproj:osx-arm64"
)

rm -rf "$OUT"
mkdir -p "$OUT"

STUDIO_CS="Studio/CelesteStudio/Studio.cs"
cp "$STUDIO_CS" "$STUDIO_CS.bak"
trap 'mv "$STUDIO_CS.bak" "$STUDIO_CS"' EXIT
sed -i "s/-dev/-$STUDIO_SUFFIX/" "$STUDIO_CS"

manifest="$OUT/manifest.env"
echo "STUDIO_VERSION=$STUDIO_VERSION" >> "$manifest"

for entry in "${TARGETS[@]}"; do
    IFS=":" read -r label proj rid <<< "$entry"

    echo "==> publishing $proj ($rid)"
    dotnet publish "$proj" -c Release -r "$rid" --self-contained true -p:EnableWindowsTargeting=true --output "$OUT/build-$label"

    case "$label" in
        windows-x64) mv "$OUT/build-$label/CelesteStudio.WPF.exe" "$OUT/build-$label/CelesteStudio.exe" ;;
        linux-x64)   mv "$OUT/build-$label/CelesteStudio.GTK" "$OUT/build-$label/CelesteStudio" ;;
        macos-*)
            mv "$OUT/build-$label/CelesteStudio.Mac.app" "$OUT/build-$label/CelesteStudio.app"
            mv "$OUT/build-$label/CelesteStudio.app/Contents/MacOS/CelesteStudio.Mac" "$OUT/build-$label/CelesteStudio.app/Contents/MacOS/CelesteStudio"
            mv "$OUT/build-$label/CelesteStudio.app/Contents/MacOS/CelesteStudio.Mac.app" "$OUT/build-$label/CelesteStudio.app/Contents/MacOS/CelesteStudio.app" 2>/dev/null || true
            sed -i "s/CelesteStudio.Mac/CelesteStudio/" "$OUT/build-$label/CelesteStudio.app/Contents/Info.plist"
            ;;
    esac

    zip="$OUT/CelesteStudio-$label.zip"
    (cd "$OUT/build-$label" && rm -f ./*.pdb && zip -r -q "$OLDPWD/$zip" .)
    rm -rf "$OUT/build-$label"

    md5="$(md5sum "$zip" | cut -d' ' -f1)"
    token_label="$(echo "$label" | tr '[:lower:]-' '[:upper:]_')"
    echo "FILENAME_${token_label}=CelesteStudio-$label.zip" >> "$manifest"
    echo "CHECKSUM_${token_label}=$md5" >> "$manifest"
done

echo "==> release artifacts in $OUT"
cat "$manifest"
