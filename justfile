assets := "CelesteTAS-EverestInterop/Assets"

test *test:
	python trace-diff/test_runner.py {{ test }}

# Regenerate the hero-only idle-normalization fixture from a full savestate capture JSON.
regen-idle src:
	cp "{{ src }}" "{{ assets }}/idle-normalization.base.json"
	jq -f "{{ assets }}/idle-normalization.jq" "{{ assets }}/idle-normalization.base.json" > "{{ assets }}/idle-normalization.json"
	@echo "regenerated idle-normalization.base.json + idle-normalization.json from {{ src }}"
