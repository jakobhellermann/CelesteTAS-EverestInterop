set windows-shell := ["powershell.exe", "-NoLogo", "-Command"]

_default:
    just --list

test *test:
    python trace-diff/test_runner.py {{ test }}

# Build Studio release archives + manifest.env (does not upload anything).
[linux]
studio-release:
    ./tools/build-studio-release.sh

# Upload the built Studio release archives to the share host and verify they are publicly reachable.
[linux]
studio-release-upload:
    #!/usr/bin/env bash
    set -euo pipefail

    if [ ! -f thunderstore/build/studio-release/manifest.env ]; then
        echo "error: no manifest — run 'just studio-release' first" >&2
        exit 1
    fi
    source thunderstore/build/studio-release/manifest.env

    upload_base="http://files.mel.home/public/celestetastudio/ninesols/$STUDIO_VERSION"
    public_base="https://share.jjakobh.me/celestetastudio/ninesols/$STUDIO_VERSION"
    mkcol() {
        code="$(curl -sS -X MKCOL "$1" -o /dev/null -w "%{http_code}")"
        if [ "$code" != "201" ] && [ "$code" != "405" ]; then
            echo "error: MKCOL $1 returned $code" >&2
            exit 1
        fi
    }
    mkcol "http://files.mel.home/public/celestetastudio/ninesols"
    mkcol "$upload_base"

    for zip in thunderstore/build/studio-release/CelesteStudio-*.zip; do
        echo "==> uploading $zip"
        curl -fsS -T "$zip" "$upload_base/$(basename "$zip")" >/dev/null
    done

    curl -fsSI "$public_base/CelesteStudio-linux-x64.zip" >/dev/null

    echo "==> release $STUDIO_VERSION uploaded"

# Build the mod with the download URLs / checksums filled in (INSTALL_STUDIO), like upstream's
# CI does, deploying it to the game profile via the usual copy target.
[linux]
mod-release:
    #!/usr/bin/env bash
    set -euo pipefail

    if [ ! -f thunderstore/build/studio-release/manifest.env ]; then
        echo "error: no manifest — run 'just studio-release' first" >&2
        exit 1
    fi
    source thunderstore/build/studio-release/manifest.env
    public_base="https://share.jjakobh.me/celestetastudio/ninesols/$STUDIO_VERSION"

    helper="CelesteTAS-EverestInterop/Source/EverestInterop/StudioHelper.cs"
    cp "$helper" "$helper.bak"
    trap 'mv "$helper.bak" "$helper"' EXIT

    sed -i "s/##STUDIO_VERSION##/$STUDIO_VERSION/" "$helper"
    for pair in "WINDOWS_x64:WINDOWS_X64" "LINUX_x64:LINUX_X64" "MACOS_x64:MACOS_X64" "MACOS_ARM64:MACOS_ARM64"; do
        token="${pair%%:*}"
        var="${pair##*:}"
        lower="$(echo "$token" | tr '[:upper:]_' '[:lower:]-')"
        url="$public_base/CelesteStudio-$lower.zip"
        checksum="CHECKSUM_$var"
        sed -i -e "s|##URL_$token##|$url|" \
               -e "s|##FILENAME_$token##|CelesteStudio-$lower.zip|" \
               -e "s|##CHECKSUM_$token##|${!checksum}|" "$helper"
    done
    if grep -q '##' "$helper"; then
        echo "error: StudioHelper.cs still contains unfilled tokens" >&2
        exit 1
    fi

    echo "==> building mod with INSTALL_STUDIO"
    dotnet publish CelesteTAS-EverestInterop -c Debug -p:DefineConstants=INSTALL_STUDIO -p:UseSymlinks=false

    if strings -el CelesteTAS-EverestInterop/bin/Debug/netstandard2.1/TasTools.dll | grep "$public_base" >/dev/null; then :; else
        echo "error: deployed TasTools.dll does not contain the download URLs" >&2
        exit 1
    fi

    deploy_dir="$(dotnet msbuild CelesteTAS-EverestInterop/CelesteTAS-EverestInterop.csproj -getProperty:CopyDir -p:Configuration=Debug)"
    mod_version="$(grep -oP 'versionNumber = "\K[^"]*' thunderstore/thunderstore.toml)"
    zip="thunderstore/build/jakobhellermann-TasStudio-$mod_version.zip"
    if [ ! -f "$zip" ]; then
        echo "error: expected package $zip was not built" >&2
        exit 1
    fi
    echo "==> mod release $mod_version (studio $STUDIO_VERSION) packaged to $zip, deployed to $deploy_dir"
