from __future__ import annotations
from .foundation import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase186_provenance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _Phase186ProvenanceTests_support:
    """Decomposed Phase192 implementation component."""
    def test_repository_ledger_and_pre_move_inventory_are_current(self) -> None:
        """The checked-in ledgers must describe the current pre-extraction tree."""

        module = load_module()
        reference = ROOT / "third-party" / "ROS-TCP-Connector"
        provenance_errors = module.validate_repository_provenance(
            ROOT,
            reference,
            LEDGER_PATH,
        )
        inventory_errors = module.validate_pre_move_inventory(
            ROOT,
            INVENTORY_PATH,
        )
        self.assertEqual([], provenance_errors)
        self.assertEqual([], inventory_errors)
    def test_decomposed_authority_sections_are_all_ledgered_with_exact_hashes(self) -> None:
        """Every split implementation and test section must have a matching ledger record."""

        module = load_module()
        ledger = json.loads(LEDGER_PATH.read_text(encoding="utf-8"))
        records = {record["path"]: record for record in ledger["implementations"]}
        from Scripts.smoke.foxrun.phase186_provenance import foundation as _provenance_foundation
        expected = getattr(_provenance_foundation, "__decomposed_authority_paths")(ROOT)
        self.assertTrue(expected)
        self.assertEqual([], [path for path in expected if path not in records])
        for relative in expected:
            path = ROOT / pathlib.PurePosixPath(relative)
            observed = hashlib.sha256(
                module._canonical_source_bytes(path.read_bytes())
            ).hexdigest()
            self.assertEqual(observed, records[relative]["sha256"], relative)
    def test_dotnet_ci_materializes_exact_provenance_authorities(self) -> None:
        """The Linux release gate must own history and both pinned references."""

        workflow = (ROOT / ".github/workflows/dotnet-tests.yml").read_text(
            encoding="utf-8"
        )
        test_job = workflow.split("  optional-ros2-adapter:", maxsplit=1)[0]
        self.assertIn("fetch-depth: 0", test_job)
        self.assertIn("repository: foxglove/foxglove-sdk", test_job)
        self.assertIn(
            "ref: b298c3d1649e6e5dfd77a53b12ab7c27f97c7aba",
            test_job,
        )
        self.assertIn("path: third-party/foxglove-sdk", test_job)
        self.assertIn(
            "repository: Unity-Technologies/ROS-TCP-Connector",
            test_job,
        )
        self.assertIn(f"ref: {REFERENCE_REVISION}", test_job)
        self.assertIn("path: third-party/ROS-TCP-Connector", test_job)
        self.assertIn(
            "git -C third-party/ROS-TCP-Connector remote set-url origin "
            + REFERENCE_REMOTE,
            test_job,
        )
        self.assertRegex(
            test_job,
            r"uses: actions/setup-python@(?:v5|[0-9a-f]{40})(?:\s|#)",
        )
        self.assertIn('python-version: "3.12"', test_job)
        self.assertIn(
            "python3 -m pip install --disable-pip-version-check --no-input "
            "psutil==7.0.0",
            test_job,
        )
    def test_repository_rejects_noncanonical_ledger_path_even_when_payload_claims_canonical(
        self,
    ) -> None:
        """Ledger identity comes from the resolved file, not a payload claim."""

        module = load_module()
        payload = json.loads(LEDGER_PATH.read_text(encoding="utf-8"))
        reference = ROOT / "third-party" / "ROS-TCP-Connector"
        with mock.patch.object(module, "_read_json", return_value=payload):
            errors = module.validate_repository_provenance(
                ROOT,
                reference,
                INVENTORY_PATH,
            )

        self.assertTrue(
            any("ledger path must be the canonical release authority" in error
                for error in errors),
            errors,
        )
    def test_canonical_ledger_rejects_symlink_alias_even_when_target_is_contained(
        self,
    ) -> None:
        """The release ledger path itself must be one regular file."""

        module = load_module()
        with tempfile.TemporaryDirectory() as temporary:
            repository = pathlib.Path(temporary) / "repository"
            repository.mkdir()
            alternate = repository / "alternate_provenance.json"
            alternate.write_text("{}\n", encoding="utf-8")
            canonical = synthetic_ledger_path(repository)
            try:
                canonical.symlink_to(alternate)
            except OSError as exc:
                self.skipTest(f"symlink creation is unavailable: {exc}")

            errors = module.validate_repository_provenance(
                repository,
                repository,
                canonical,
            )

        self.assertTrue(
            any(
                "canonical ledger must be a regular non-symlink file" in error
                for error in errors
            ),
            errors,
        )
    def test_unexplained_distinctive_copy_is_rejected(self) -> None:
        """Four substantial consecutive upstream lines require an explicit ledger entry."""

        module = load_module()
        distinctive = "\n".join(
            [
                "private readonly Queue<OutgoingMessage> pendingMessages;",
                "public void QueueMessage(string topic, byte[] payload)",
                "pendingMessages.Enqueue(new OutgoingMessage(topic, payload));",
                "SignalSenderThreadWithoutBlockingTheUnityMainThread();",
            ]
        )
        payload = {
            "schemaVersion": 1,
            "reference": {
                "repository": "https://github.com/Unity-Technologies/ROS-TCP-Connector.git",
                "revision": "a" * 40,
                "license": "Apache-2.0",
                "inspectedFiles": ["Runtime/OutgoingMessageSender.cs"],
            },
            "implementations": [
                {
                    "path": "Runtime/Original.cs",
                    "classification": "original",
                    "influence": "No upstream implementation reused.",
                }
            ],
        }
        errors = module.validate_ledger_payload(
            payload,
            actual_revision="a" * 40,
            implementation_sources={"Runtime/Original.cs": distinctive},
            reference_sources={"Runtime/OutgoingMessageSender.cs": distinctive},
        )
        self.assertTrue(
            any("unexplained distinctive overlap" in error for error in errors),
            errors,
        )
    def test_unexplained_distinctive_comment_copy_is_rejected(self) -> None:
        """Substantial copied comments are evidence, not ignorable whitespace."""

        module = load_module()
        distinctive = "\n".join(
            [
                "// This sender owns the only transition from queued work into socket output.",
                "// Callers transfer the payload once and must never mutate it after this point.",
                "// The worker preserves topic order while allowing unrelated topics to progress.",
                "// Shutdown drains the accepted prefix before publishing the terminal state.",
            ]
        )
        payload = {
            "schemaVersion": 1,
            "reference": reference_payload(),
            "implementations": [
                {
                    "path": "Runtime/Original.cs",
                    "sha256": sha256_text(distinctive),
                    "classification": "original",
                    "influence": "No upstream implementation reused.",
                }
            ],
        }
        errors = module.validate_ledger_payload(
            payload,
            actual_revision=REFERENCE_REVISION,
            implementation_sources={"Runtime/Original.cs": distinctive},
            reference_sources={
                "com.unity.robotics.ros-tcp-connector/Runtime/"
                "TcpConnector/OutgoingMessageSender.cs": distinctive,
                **{
                    relative: "internal sealed class UpstreamReference {}\n"
                    for relative in reference_payload()["inspectedFiles"]
                    if not relative.endswith("OutgoingMessageSender.cs")
                },
            },
        )
        self.assertTrue(
            any("unexplained distinctive overlap" in error for error in errors),
            errors,
        )
    def test_revision_drift_and_material_copy_without_notice_are_rejected(self) -> None:
        """Revision identity and license notice requirements are fail-closed."""

        module = load_module()
        payload = {
            "schemaVersion": 1,
            "reference": {
                "repository": "https://github.com/Unity-Technologies/ROS-TCP-Connector.git",
                "revision": "a" * 40,
                "license": "Apache-2.0",
                "inspectedFiles": ["Runtime/ROSConnection.cs"],
            },
            "implementations": [
                {
                    "path": "Runtime/Derived.cs",
                    "classification": "materially_copied",
                    "influence": "Copied implementation.",
                }
            ],
        }
        errors = module.validate_ledger_payload(
            payload,
            actual_revision="b" * 40,
            implementation_sources={"Runtime/Derived.cs": "internal sealed class Derived {}"},
            reference_sources={"Runtime/ROSConnection.cs": "internal sealed class Reference {}"},
        )
        self.assertTrue(any("revision mismatch" in error for error in errors), errors)
        self.assertTrue(any("licenseNotice" in error for error in errors), errors)
    def test_inventory_digest_changes_when_a_scoped_path_changes(self) -> None:
        """The compact inventory digest represents the complete sorted path set."""

        module = load_module()
        first = module.path_inventory_digest(["Runtime/B.cs", "Runtime/A.cs"])
        second = module.path_inventory_digest(
            ["Runtime/B.cs", "Runtime/A.cs", "Runtime/C.cs"]
        )
        self.assertEqual(
            module.path_inventory_digest(["Runtime/A.cs", "Runtime/B.cs"]),
            first,
        )
        self.assertNotEqual(first, second)
    def test_implementation_sha256_must_match_exact_file_bytes(self) -> None:
        """Updating a ledger digest cannot hide different implementation bytes."""

        module = load_module()
        path = "Runtime/Protocol.cs"
        payload = {
            "schemaVersion": 1,
            "reference": reference_payload(),
            "implementations": [
                {
                    "path": path,
                    "sha256": sha256_text("original\n"),
                    "classification": "original",
                    "influence": "Original implementation.",
                }
            ],
        }
        errors = module.validate_ledger_payload(
            payload,
            actual_revision=REFERENCE_REVISION,
            implementation_sources={path: "tampered\n"},
            reference_sources={
                relative: "internal sealed class UpstreamReference {}\n"
                for relative in reference_payload()["inspectedFiles"]
            },
        )
        self.assertTrue(any("sha256 mismatch" in error for error in errors), errors)
    def test_implementation_paths_reject_escape_and_casefold_duplicates(self) -> None:
        """Ledger paths are canonical repository-relative identities."""

        module = load_module()
        paths = [
            "C:/absolute/Protocol.cs",
            "../outside/Protocol.cs",
            "Runtime/Protocol.cs",
            "runtime/protocol.cs",
        ]
        payload = {
            "schemaVersion": 1,
            "reference": reference_payload(),
            "implementations": [
                {
                    "path": path,
                    "sha256": sha256_text(path),
                    "classification": "original",
                    "influence": "Original implementation.",
                }
                for path in paths
            ],
        }
        errors = module.validate_ledger_payload(
            payload,
            actual_revision=REFERENCE_REVISION,
            implementation_sources={path: path for path in paths},
            reference_sources={
                relative: "internal sealed class UpstreamReference {}\n"
                for relative in reference_payload()["inspectedFiles"]
            },
        )
        self.assertTrue(any("absolute" in error for error in errors), errors)
        self.assertTrue(any("parent traversal" in error for error in errors), errors)
        self.assertTrue(
            any("case-insensitive duplicate" in error for error in errors),
            errors,
        )
    def test_reference_paths_reject_escape_aliases_and_casefold_duplicates(self) -> None:
        """Inspected upstream paths use one portable canonical identity."""

        module = load_module()
        reference = reference_payload()
        reference["inspectedFiles"] = [
            "C:/absolute/Reference.cs",
            "../outside/Reference.cs",
            "Runtime/./Reference.cs",
            "Runtime/Reference.cs",
            "runtime/reference.cs",
        ]
        payload = {
            "schemaVersion": 1,
            "reference": reference,
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
            implementation_sources={"Runtime/Protocol.cs": "source\n"},
            reference_sources={path: "reference\n" for path in reference["inspectedFiles"]},
        )
        self.assertTrue(any("absolute" in error for error in errors), errors)
        self.assertTrue(any("parent traversal" in error for error in errors), errors)
        self.assertTrue(any("canonical alias" in error for error in errors), errors)
        self.assertTrue(
            any("case-insensitive duplicate" in error for error in errors),
            errors,
        )
    def test_inventory_selectors_reject_noncanonical_or_duplicate_paths(self) -> None:
        """Inventory selectors cannot escape or alias their captured Git tree."""

        module = load_module()
        invalid_scopes = [
            (
                {"prefixes": ["C:/absolute"], "exactPaths": [], "globs": []},
                "absolute",
            ),
            (
                {"prefixes": [], "exactPaths": ["../outside.cs"], "globs": []},
                "parent traversal",
            ),
            (
                {"prefixes": [], "exactPaths": ["A/./B.cs"], "globs": []},
                "canonical alias",
            ),
            (
                {
                    "prefixes": [],
                    "exactPaths": ["Runtime/A.cs", "runtime/a.cs"],
                    "globs": [],
                },
                "case-insensitive duplicate",
            ),
        ]
        for scope, expected in invalid_scopes:
            with self.subTest(expected=expected):
                with self.assertRaisesRegex(ValueError, expected):
                    module._scope_paths([], scope)
    def test_reference_metadata_and_clean_room_claim_are_exact(self) -> None:
        """Date, subject, origin, files, ideas, and copy status cannot drift."""

        module = load_module()
        reference = reference_payload(
            commit_date="2026-01-01T00:00:00Z",
            subject="Different subject",
        )
        reference["origin"] = "https://example.invalid/fork.git"
        reference["inspectedFiles"] = reference["inspectedFiles"][:-1]
        reference["ideasReviewed"] = reference["ideasReviewed"][:-1]
        reference["materialCopied"] = True
        payload = {
            "schemaVersion": 1,
            "reference": reference,
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
            implementation_sources={"Runtime/Protocol.cs": "source\n"},
            reference_sources={
                relative: "internal sealed class UpstreamReference {}\n"
                for relative in reference_payload()["inspectedFiles"]
            },
        )
        for expected in (
            "commitDate",
            "subject",
            "origin",
            "inspectedFiles",
            "ideasReviewed",
            "materialCopied",
        ):
            self.assertTrue(
                any(expected in error for error in errors),
                (expected, errors),
            )


__all__ = [name for name in globals() if not name.startswith("__")]
