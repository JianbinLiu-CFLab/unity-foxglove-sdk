from __future__ import annotations
from .class_phase186_provenance_tests_runtime import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase186_provenance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _Phase186ProvenanceTests_cleanup:
    """Decomposed Phase192 implementation component."""
    def test_protocol_docs_reject_limit_value_and_error_mapping_drift(self) -> None:
        """Rehashing a document cannot rewrite frozen table semantics."""

        module = load_module()
        ledger = json.loads(LEDGER_PATH.read_text(encoding="utf-8"))
        relevant_paths = [
            FIXTURE_PATH,
            (
                "Packages/dev.unity2foxglove.ros2bridge/"
                "Documentation~/en/U2R2_PROTOCOL.md"
            ),
            "Tools/ros2_bridge/unity2foxglove_ros2_bridge/U2R2_PROTOCOL.md",
        ]
        records = {
            record["path"]: record
            for record in ledger["implementations"]
            if record["path"] in relevant_paths
        }
        baseline_sources = {
            path: (ROOT / pathlib.PurePosixPath(path)).read_text(encoding="utf-8")
            for path in relevant_paths
        }
        reference_sources = {
            path: (
                ROOT
                / "third-party"
                / "ROS-TCP-Connector"
                / pathlib.PurePosixPath(path)
            ).read_text(encoding="utf-8")
            for path in ledger["reference"]["inspectedFiles"]
        }

        cases = [
            (
                "limit_value",
                "| `maxConnections` | 9 |",
                "| `maxConnections` | 999 |",
                "limit table",
            ),
            (
                "error_terminal_and_allowed_response",
                "| `busy` | yes | `busy` |",
                "| `busy` | no | `publisher_ready` |",
                "error table",
            ),
        ]
        document_path = (
            "Tools/ros2_bridge/unity2foxglove_ros2_bridge/U2R2_PROTOCOL.md"
        )
        for case_id, before, after, expected in cases:
            with self.subTest(case_id=case_id):
                payload = {
                    "schemaVersion": 1,
                    "reference": json.loads(json.dumps(ledger["reference"])),
                    "implementations": [
                        json.loads(json.dumps(records[path]))
                        for path in relevant_paths
                    ],
                }
                sources = dict(baseline_sources)
                self.assertIn(before, sources[document_path])
                sources[document_path] = sources[document_path].replace(
                    before,
                    after,
                    1,
                )
                for record in payload["implementations"]:
                    if record["path"] == document_path:
                        record["sha256"] = sha256_text(sources[document_path])

                errors = module.validate_ledger_payload(
                    payload,
                    actual_revision=REFERENCE_REVISION,
                    implementation_sources=sources,
                    reference_sources=reference_sources,
                )

                self.assertTrue(
                    any(expected in error for error in errors),
                    errors,
                )

        with self.subTest(case_id="hidden_authority_tables"):
            payload = {
                "schemaVersion": 1,
                "reference": json.loads(json.dumps(ledger["reference"])),
                "implementations": [
                    json.loads(json.dumps(records[path]))
                    for path in relevant_paths
                ],
            }
            sources = dict(baseline_sources)
            hidden = sources[document_path]
            error_header = "| Error code | Terminal | Allowed wire response |"
            error_last_row = "| `timeout` | yes | local only |"
            error_start = hidden.index(error_header)
            error_end = hidden.index(error_last_row, error_start) + len(
                error_last_row
            )
            hidden = (
                hidden[:error_start]
                + "<!--\n"
                + hidden[error_start:error_end]
                + "\n-->"
                + hidden[error_end:]
            )
            limit_header = "| Limit | Value |"
            limit_last_row = "| `maxJsonDepth` | 64 |"
            limit_start = hidden.index(limit_header)
            limit_end = hidden.index(limit_last_row, limit_start) + len(
                limit_last_row
            )
            hidden = (
                hidden[:limit_start]
                + "```text\n"
                + hidden[limit_start:limit_end]
                + "\n```"
                + hidden[limit_end:]
            )
            heading = "## Frozen limits and implementation status"
            hidden = hidden.replace(
                heading,
                heading
                + "\n\nVisible conflicting summary: `maxConnections` is 999; "
                + "`busy` is nonterminal and responds with `publisher_ready`.",
                1,
            )
            sources[document_path] = hidden
            for record in payload["implementations"]:
                if record["path"] == document_path:
                    record["sha256"] = sha256_text(hidden)

            errors = module.validate_ledger_payload(
                payload,
                actual_revision=REFERENCE_REVISION,
                implementation_sources=sources,
                reference_sources=reference_sources,
            )

            self.assertTrue(
                any("visible authority" in error for error in errors),
                errors,
            )

        def validate_hidden_document(mutated: str) -> list[str]:
            """Validate one hidden-document mutation against baseline authority."""

            payload = {
                "schemaVersion": 1,
                "reference": json.loads(json.dumps(ledger["reference"])),
                "implementations": [
                    json.loads(json.dumps(records[path]))
                    for path in relevant_paths
                ],
            }
            sources = dict(baseline_sources)
            sources[document_path] = mutated
            for record in payload["implementations"]:
                if record["path"] == document_path:
                    record["sha256"] = sha256_text(mutated)
            return module.validate_ledger_payload(
                payload,
                actual_revision=REFERENCE_REVISION,
                implementation_sources=sources,
                reference_sources=reference_sources,
            )

        with self.subTest(case_id="hidden_authority_tables_fake_close"):
            fake_close = baseline_sources[document_path].replace(
                "## Frozen limits and implementation status",
                "```text\n```fake-close\n"
                "## Frozen limits and implementation status",
                1,
            )
            errors = validate_hidden_document(fake_close)
            self.assertTrue(
                any("visible authority" in error for error in errors),
                errors,
            )

        with self.subTest(case_id="hidden_authority_tables_indented_code"):
            indented = baseline_sources[document_path]
            for header, last_row in (
                (
                    "| Error code | Terminal | Allowed wire response |",
                    "| `timeout` | yes | local only |",
                ),
                (
                    "| Limit | Value |",
                    "| `maxJsonDepth` | 64 |",
                ),
            ):
                start = indented.index(header)
                end = indented.index(last_row, start) + len(last_row)
                block = indented[start:end]
                indented_block = "\n".join(
                    "    " + line for line in block.splitlines()
                )
                indented = indented[:start] + indented_block + indented[end:]
            errors = validate_hidden_document(indented)
            self.assertTrue(
                any("visible authority" in error for error in errors),
                errors,
            )

        with self.subTest(case_id="hidden_authority_tables_raw_html"):
            raw_html = baseline_sources[document_path]
            for header, last_row in (
                (
                    "| Error code | Terminal | Allowed wire response |",
                    "| `timeout` | yes | local only |",
                ),
                (
                    "| Limit | Value |",
                    "| `maxJsonDepth` | 64 |",
                ),
            ):
                start = raw_html.index(header)
                end = raw_html.index(last_row, start) + len(last_row)
                raw_html = (
                    raw_html[:start]
                    + '<div hidden="hidden">\n'
                    + raw_html[start:end]
                    + "\n</div>"
                    + raw_html[end:]
                )
            errors = validate_hidden_document(raw_html)
            self.assertTrue(
                any("visible authority" in error for error in errors),
                errors,
            )

        with self.subTest(case_id="raw_html_wraps_authority_section"):
            wrapped = baseline_sources[document_path].replace(
                "## Frozen limits and implementation status",
                '<div hidden="hidden">\n'
                "## Frozen limits and implementation status",
                1,
            )
            wrapped += "\n\n## Hidden wrapper terminator\n</div>\n"
            errors = validate_hidden_document(wrapped)
            self.assertTrue(
                any("visible authority" in error for error in errors),
                errors,
            )

        with self.subTest(case_id="third_renamed_authority_table"):
            extra_table = baseline_sources[document_path]
            extra_table += (
                "\n\n| Renamed authority | Value |\n"
                "| --- | --- |\n"
                "| `maxSecretConnections` | 1 |\n"
                "| `surprise_error` | yes |\n"
            )
            errors = validate_hidden_document(extra_table)
            self.assertTrue(
                any(
                    "exactly the two canonical tables" in error
                    for error in errors
                ),
                errors,
            )

        with self.subTest(case_id="authority_row_has_extra_boundary_pipes"):
            extra_pipes = baseline_sources[document_path].replace(
                "| `maxConnections` | 9 |",
                "|| `maxConnections` | 9 ||",
                1,
            )
            errors = validate_hidden_document(extra_pipes)
            self.assertTrue(
                any(
                    "exactly one leading and trailing pipe" in error
                    for error in errors
                ),
                errors,
            )

        with self.subTest(case_id="third_table_omits_boundary_pipes"):
            boundaryless = baseline_sources[document_path]
            boundaryless += (
                "\n\n## Alternate limits\n\n"
                "Renamed authority | Alternate value\n"
                "--- | ---\n"
                "`maxConnections` | 999\n"
            )
            errors = validate_hidden_document(boundaryless)
            self.assertTrue(
                any(
                    "exactly the two canonical tables" in error
                    for error in errors
                ),
                errors,
            )
    def test_v1_top_level_authority_rejects_byte_or_state_drift(self) -> None:
        """The immutable pre-v2 fixture rejects changed legacy bytes and states."""

        module = load_module()
        baseline = {
            "fixtureVersion": 1,
            "protocol": {"magic": "U2R2"},
            "limits": {"maxPayloadBytes": 8},
            "health": {
                "request": {"frameHex": "0102"},
                "stateTransitions": ["disconnected", "healthy"],
            },
            "preparePublisher": {"request": {"frameHex": "0304"}},
            "publish": {"request": {"frameHex": "0506"}},
            "negativeVectors": [{"id": "bad_magic", "terminal": True}],
        }
        current = json.loads(json.dumps(baseline))
        current["health"]["request"]["frameHex"] = "ffff"
        current["health"]["stateTransitions"] = ["disconnected", "faulted"]
        current["v2"] = {"protocolVersion": 2}
        compatibility = {
            "capturedFromHead": "a" * 40,
            "fixturePath": FIXTURE_PATH,
            "topLevelKeys": V1_TOP_LEVEL_KEYS,
            "canonicalSha256": canonical_json_sha256(baseline),
        }
        payload = {
            "schemaVersion": 1,
            "reference": reference_payload(),
            "v1Compatibility": compatibility,
            "implementations": [
                {
                    "path": FIXTURE_PATH,
                    "sha256": sha256_text(json.dumps(current)),
                    "classification": "original",
                    "influence": "Original shared fixture.",
                }
            ],
        }
        errors = module.validate_ledger_payload(
            payload,
            actual_revision=REFERENCE_REVISION,
            implementation_sources={FIXTURE_PATH: json.dumps(current)},
            reference_sources={
                relative: "internal sealed class UpstreamReference {}\n"
                for relative in reference_payload()["inspectedFiles"]
            },
        )
        self.assertTrue(any("frozen v1" in error for error in errors), errors)
class Phase186ProvenanceTests(_Phase186ProvenanceTests_support, _Phase186ProvenanceTests_fixtures, _Phase186ProvenanceTests_validation, _Phase186ProvenanceTests_runtime, _Phase186ProvenanceTests_cleanup, unittest.TestCase):
    """Lock fail-closed provenance and exact pre-move inventory behavior."""
    pass


__all__ = [name for name in globals() if not name.startswith("__")]
