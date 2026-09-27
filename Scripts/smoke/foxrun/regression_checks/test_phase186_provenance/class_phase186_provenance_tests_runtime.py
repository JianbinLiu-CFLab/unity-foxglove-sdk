from __future__ import annotations
from .class_phase186_provenance_tests_validation import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase186_provenance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _Phase186ProvenanceTests_runtime:
    def test_canonical_inventory_rejects_symlink_alias_even_when_target_is_contained(
        self,
    ) -> None:
        """The release inventory path itself must be one regular file."""

        module = load_module()
        with tempfile.TemporaryDirectory() as temporary:
            repository = pathlib.Path(temporary)
            initialize_git_repository(repository)
            tracked = repository / "Legacy/Bridge.cs"
            tracked.parent.mkdir(parents=True)
            tracked.write_text("legacy\n", encoding="utf-8")
            run_git(repository, "add", ".")
            run_git(repository, "commit", "--quiet", "-m", "capture")
            captured = run_git(repository, "rev-parse", "HEAD")
            captured_tree = run_git(
                repository,
                "show",
                "-s",
                "--format=%T",
                captured,
            )
            alternate = repository / "alternate_inventory.json"
            alternate.write_text(
                json.dumps(
                    {
                        "schemaVersion": 1,
                        "capturedFromHead": captured,
                        "capturedTree": captured_tree,
                        "scopes": [
                            {
                                "id": "legacy",
                                "action": "move_to_bridge",
                                "exactPaths": ["Legacy/Bridge.cs"],
                                "pathCount": 1,
                                "pathDigestSha256": module.path_inventory_digest(
                                    ["Legacy/Bridge.cs"]
                                ),
                            }
                        ],
                        "totalPathCount": 1,
                        "totalPathDigestSha256": module.path_inventory_digest(
                            ["Legacy/Bridge.cs"]
                        ),
                    }
                ),
                encoding="utf-8",
            )
            canonical = (
                repository
                / "Packages/dev.unity2foxglove.sdk/Tests/Unit/Phase186/"
                "Fixtures/pre_move_sdk_ros_inventory.json"
            )
            canonical.parent.mkdir(parents=True)
            try:
                canonical.symlink_to(alternate)
            except OSError as exc:
                self.skipTest(f"symlink creation is unavailable: {exc}")

            errors = module.validate_pre_move_inventory(repository, canonical)

        self.assertTrue(
            any("canonical inventory must be a regular non-symlink file" in error
                for error in errors),
            errors,
        )
    def test_fixed_inventory_rejects_scope_identity_action_selector_and_overlap_drift(
        self,
    ) -> None:
        """The canonical seven-scope inventory is immutable, unique, and disjoint."""

        module = load_module()
        baseline = json.loads(INVENTORY_PATH.read_text(encoding="utf-8"))
        first_nested_path = (
            "Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/"
            "Diagnostics/IRos2BridgeCommandRunner.cs"
        )

        cases: list[tuple[str, dict[str, object], str]] = []

        allowed_action_drift = json.loads(json.dumps(baseline))
        allowed_action_drift["scopes"][0]["action"] = "delete_from_sdk"
        cases.append(
            (
                "allowed_action_drift",
                allowed_action_drift,
                "fixed inventory scope authority mismatch",
            )
        )

        duplicate_scope_id = json.loads(json.dumps(baseline))
        duplicate_scope_id["scopes"][1]["id"] = duplicate_scope_id["scopes"][0]["id"]
        cases.append(
            (
                "duplicate_scope_id",
                duplicate_scope_id,
                "duplicate inventory scope id",
            )
        )

        duplicate_overlapping_scope = json.loads(json.dumps(baseline))
        duplicate = json.loads(
            json.dumps(duplicate_overlapping_scope["scopes"][0])
        )
        duplicate["id"] = "duplicate_bridge_runtime_tree"
        duplicate_overlapping_scope["scopes"].append(duplicate)
        cases.append(
            (
                "duplicate_overlapping_scope",
                duplicate_overlapping_scope,
                "cross-scope overlap",
            )
        )

        selector_drift = json.loads(json.dumps(baseline))
        selector_drift["scopes"][0]["exactPaths"].append(first_nested_path)
        cases.append(
            (
                "selector_drift",
                selector_drift,
                "fixed inventory scope authority mismatch",
            )
        )

        missing_purpose = json.loads(json.dumps(baseline))
        del missing_purpose["purpose"]
        cases.append(
            (
                "missing_purpose",
                missing_purpose,
                "fixed inventory top-level authority mismatch",
            )
        )

        purpose_drift = json.loads(json.dumps(baseline))
        purpose_drift["purpose"] = "A different inventory purpose."
        cases.append(
            (
                "purpose_drift",
                purpose_drift,
                "fixed inventory top-level authority mismatch",
            )
        )

        unknown_top_level = json.loads(json.dumps(baseline))
        unknown_top_level["unknownAuthority"] = True
        cases.append(
            (
                "unknown_top_level",
                unknown_top_level,
                "fixed inventory top-level authority mismatch",
            )
        )

        schema_version_boolean = json.loads(json.dumps(baseline))
        schema_version_boolean["schemaVersion"] = True
        cases.append(
            (
                "schema_version_boolean",
                schema_version_boolean,
                "schemaVersion must be exactly the JSON integer 1",
            )
        )

        schema_version_float = json.loads(json.dumps(baseline))
        schema_version_float["schemaVersion"] = 1.0
        cases.append(
            (
                "schema_version_float",
                schema_version_float,
                "schemaVersion must be exactly the JSON integer 1",
            )
        )

        scope_count_float = json.loads(json.dumps(baseline))
        scope_count_float["scopes"][0]["pathCount"] = 36.0
        cases.append(
            (
                "scope_count_float",
                scope_count_float,
                "fixed inventory scope authority mismatch",
            )
        )

        total_count_float = json.loads(json.dumps(baseline))
        total_count_float["totalPathCount"] = 156.0
        cases.append(
            (
                "total_count_float",
                total_count_float,
                "totalPathCount must remain exactly the JSON integer 156",
            )
        )

        for case_id, payload, expected in cases:
            with self.subTest(case_id=case_id):
                with mock.patch.object(module, "_read_json", return_value=payload):
                    errors = module.validate_pre_move_inventory(
                        ROOT,
                        INVENTORY_PATH,
                    )
                self.assertTrue(
                    any(expected in error for error in errors),
                    errors,
                )
    def test_ledger_numeric_authority_requires_exact_json_integers(self) -> None:
        """Boolean and float lookalikes cannot satisfy integer authorities."""

        module = load_module()
        for value in (True, 1.0):
            with self.subTest(field="schemaVersion", value=value):
                payload = {
                    "schemaVersion": value,
                    "reference": reference_payload(),
                    "implementations": [
                        {
                            "path": "Runtime/Protocol.cs",
                            "sha256": sha256_text("source\n"),
                            "classification": "original",
                            "influence": "Original implementation.",
                        }
                    ],
                }
                errors = module.validate_ledger_payload(
                    payload,
                    actual_revision=REFERENCE_REVISION,
                    implementation_sources={
                        "Runtime/Protocol.cs": "source\n",
                    },
                    reference_sources={
                        relative: "internal sealed class UpstreamReference {}\n"
                        for relative in reference_payload()["inspectedFiles"]
                    },
                )
                self.assertTrue(
                    any(
                        "schemaVersion must be exactly the JSON integer 1"
                        in error
                        for error in errors
                    ),
                    errors,
                )

        payload = json.loads(LEDGER_PATH.read_text(encoding="utf-8"))
        payload["introducedSourceCommits"][0]["sourceCount"] = 6.0
        reference = ROOT / "third-party" / "ROS-TCP-Connector"
        with mock.patch.object(module, "_read_json", return_value=payload):
            errors = module.validate_repository_provenance(
                ROOT,
                reference,
                LEDGER_PATH,
            )
        self.assertTrue(
            any(
                "introducedSourceCommits must match" in error
                for error in errors
            ),
            errors,
        )
    def test_strict_json_loader_rejects_duplicate_keys_and_nonfinite_numbers(
        self,
    ) -> None:
        """Ledger, inventory, and fixture JSON use one strict parser."""

        module = load_module()
        invalid_sources = (
            (
                "duplicate",
                '{"schemaVersion": 1, "schemaVersion": 2}',
                "duplicate JSON key",
            ),
            (
                "nonfinite",
                '{"schemaVersion": NaN}',
                "non-finite JSON number",
            ),
        )
        with tempfile.TemporaryDirectory() as temporary:
            temporary_root = pathlib.Path(temporary)
            for consumer in ("PROVENANCE.json", "pre_move_inventory.json"):
                for case_id, source, expected in invalid_sources:
                    with self.subTest(
                        consumer=consumer,
                        case_id=case_id,
                    ):
                        path = temporary_root / consumer
                        path.write_text(source, encoding="utf-8")
                        with self.assertRaisesRegex(ValueError, expected):
                            module._read_json(path)

        fixture_sources = (
            (
                "duplicate",
                '{"v2": {}, "v2": {}}',
                "duplicate JSON key",
            ),
            (
                "nonfinite",
                '{"v2": {"commit2": {"limits": '
                '{"maxConnections": NaN}}, "errorCodes": []}}',
                "non-finite JSON number",
            ),
        )
        for case_id, source, expected in fixture_sources:
            with self.subTest(consumer="fixture", case_id=case_id):
                _, _, errors = module._fixture_document_authority(
                    {FIXTURE_PATH: source}
                )
                self.assertTrue(
                    any(expected in error for error in errors),
                    errors,
                )
    def test_canonical_ledger_rejects_nested_schema_and_order_drift(
        self,
    ) -> None:
        """Every canonical ledger object has a closed, ordered schema."""

        module = load_module()
        payload = json.loads(LEDGER_PATH.read_text(encoding="utf-8"))
        payload["unexpectedTopLevel"] = True
        payload["reference"]["unexpectedReferenceField"] = True
        payload["v1Compatibility"]["unexpectedCompatibilityField"] = True
        payload["introducedSourceCommits"][0]["unexpectedCommitField"] = True
        payload["implementations"][0]["referenceFiles"] = [
            payload["reference"]["inspectedFiles"][0]
        ]
        del payload["implementations"][1]["introducedIn"]
        payload["implementations"][0], payload["implementations"][1] = (
            payload["implementations"][1],
            payload["implementations"][0],
        )
        reference = ROOT / "third-party" / "ROS-TCP-Connector"
        with mock.patch.object(module, "_read_json", return_value=payload):
            errors = module.validate_repository_provenance(
                ROOT,
                reference,
                LEDGER_PATH,
            )

        expected = (
            "canonical ledger top-level schema",
            "canonical ledger reference schema",
            "canonical ledger v1Compatibility schema",
            "canonical ledger introducedSourceCommits[0] schema",
            "canonical ledger implementation schema",
            "canonical ledger implementations must be sorted",
        )
        missing = [
            diagnostic
            for diagnostic in expected
            if not any(diagnostic in error for error in errors)
        ]
        self.assertEqual([], missing, errors)
    def test_protocol_docs_require_exact_clean_room_and_ledger_anchors(self) -> None:
        """A rehashed document cannot erase the legal/provenance declaration."""

        module = load_module()
        path = (
            "Tools/ros2_bridge/unity2foxglove_ros2_bridge/U2R2_PROTOCOL.md"
        )
        payload = {
            "schemaVersion": 1,
            "reference": reference_payload(),
            "implementations": [
                {
                    "path": path,
                    "sha256": sha256_text("placeholder\n"),
                    "classification": "original",
                    "influence": "Original protocol documentation.",
                }
            ],
        }
        errors = module.validate_ledger_payload(
            payload,
            actual_revision=REFERENCE_REVISION,
            implementation_sources={path: "placeholder\n"},
            reference_sources={
                relative: "internal sealed class UpstreamReference {}\n"
                for relative in reference_payload()["inspectedFiles"]
            },
        )
        for expected in (
            REFERENCE_REVISION,
            "Apache-2.0",
            "original",
            "no implementation code",
            "PROVENANCE.json",
        ):
            self.assertTrue(
                any(expected in error for error in errors),
                (expected, errors),
            )


__all__ = [name for name in globals() if not name.startswith("__")]
