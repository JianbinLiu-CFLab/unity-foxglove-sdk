# Source Layout Validation

`source_layout.py` provides the shared helpers used by regression tests that
load split facades and source sections.

- `load_fresh_module` loads a facade in an isolated module environment.
- `read_split_source` and `read_split_csharp_source` compose facade sources.
- `read_included_source` expands relative C/C++ include fragments.
- `load_test_suite` loads an out-of-tree regression module with its package root.

The regression tests for these helpers live in
`regression_checks/test_source_layout.py`.
