# Regenerate the NuGet lockfile (nuget-deps.json)
# Run after changing any PackageReference / ProjectReference / TargetFramework.
nuget-deps:
    nix build '.#celeste-studio-gtk.fetch-deps' -o /tmp/fetch-deps
    /tmp/fetch-deps "$PWD/nuget-deps.json"

# Build CelesteStudio.GTK via the nix flake (result symlink).
build:
    nix build .#celeste-studio-gtk

run: build
    ./result/bin/CelesteStudio.GTK
