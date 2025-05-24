#!/usr/bin/env bash

files=(
    EverestInterop/Hotkeys.cs
    GlobalUsings.cs
    Module/CelesteTasSettings.cs
    Playback/PopupToast.cs
    Playback/SavestateManager.cs
    TAS/Input/Command.cs
    TAS/Input/Comment.cs
    TAS/Input/FastForward.cs
    TAS/Input/ITasCommandMeta.cs
    TAS/Input/InputController.cs
    TAS/Input/InputFrame.cs
    TAS/InputHelper.cs
    TAS/Manager.cs
    Utils/AttributeUtils.cs
    Utils/Extensions.cs
)

for file in "${files[@]}"; do
    src_path="CelesteTAS-EverestInterop/Source/$file"
    dest_path="Studio/CelesteStudio/Communication/LibTAS/$file"

    if [ -f "$src_path" ]; then
        mkdir -p "$(dirname "$dest_path")"
        cp "$src_path" "$dest_path"
    else
        echo "File $src_path does not exist, skipping."
    fi
done

