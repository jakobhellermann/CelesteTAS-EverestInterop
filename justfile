_default:
	just --list

# Regenerate the NuGet lockfile
regen-nuget-deps:
    nix build '.#celeste-studio.fetch-deps' -o /tmp/fetch-deps
    /tmp/fetch-deps "$PWD/nuget-deps.json"

test *test:
    python trace-diff/test_runner.py {{ test }}

assets := "CelesteTAS-EverestInterop/Assets"

# Regenerate the hero-only idle-normalization fixture from a full savestate capture JSON.
regen-idle src:
	cp "{{ src }}" "{{ assets }}/idle-normalization.base.json"
	jq -f "{{ assets }}/idle-normalization.jq" "{{ assets }}/idle-normalization.base.json" > "{{ assets }}/idle-normalization.json"
	@echo "regenerated idle-normalization.base.json + idle-normalization.json from {{ src }}"
