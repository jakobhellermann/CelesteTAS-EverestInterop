# Determinism tests

`test_runner.py` auto-discovers every `tests/*.tas` and runs it against the running game (DevServer + tracer enabled).
**Add a test by dropping a `.tas` file — no runner change.**

```
python3 test_runner.py            # run all
python3 test_runner.py load_move  # run one (filename without .tas)
```

A test is configured by header lines — `#! key=value` / `#! each var=v1,v2` — each a `#` TAS comment the engine
ignores. In the body, `{scratch}` is replaced with a per-case `/tmp` path prefix (for savestate files) and `{var}`
with the current value of an `each` variable.

## `Mode=assert` — default (omitted)

Self-contained TAS with an assert command (e.g. `AssertEqualSavestates`). Passes iff it completes without
aborting. Single run, fast — use for most tests and regressions.

## `#! Mode=compareTraces`

Runs every combination of the `each` variables and asserts their traces are identical — the result must be
invariant under those variables. Wrap the compared part in `BeginTrace, scenario` / `EndTrace` (setup frames
outside it don't count; segments are aligned by position). Used for prior-independence, e.g.:

```
#! Mode=compareTraces
#! each prior=idle,airborne
Read, priors/{prior}.tas
BeginTrace, scenario
load Tut_01 47.73 4.57
   5,R
EndTrace
```

## `priors/`

Shared setups referenced by `each prior=…` — each a small TAS that leaves the hero in a state (`idle.tas`, …).
