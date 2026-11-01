# Conformance Validation

Repository-wide claim, adjudication, and managed conformance validation.

- `run_conformance.py` executes the managed conformance vectors.
- `validate_adjudication.py` validates finding dispositions and evidence bounds.
- `validate_claim_inventory.py` validates the claim inventory and its identifiers.
- `regression_checks/` contains the regression tests for these entry points.

These checks validate repository evidence and managed controls; they do not replace the Unity, ROS2, or IL2CPP matrix lanes.
