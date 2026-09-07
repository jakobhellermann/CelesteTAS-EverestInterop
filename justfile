_default:
    just --list

test *test:
    python trace-diff/test_runner.py {{ test }}

