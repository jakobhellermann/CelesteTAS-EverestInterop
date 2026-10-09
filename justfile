_default:
	just --list

# Regenerate the NuGet lockfile
regen-nuget-deps:
    nix build '.#celeste-studio.fetch-deps' -o /tmp/fetch-deps
    /tmp/fetch-deps "$PWD/nuget-deps.json"
