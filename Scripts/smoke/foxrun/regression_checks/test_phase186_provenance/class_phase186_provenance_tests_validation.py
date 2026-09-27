from __future__ import annotations
from .class_phase186_provenance_tests_fixtures import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase186_provenance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _Phase186ProvenanceTests_validation:
    def test_required_authority_rejects_contained_symlink_alias(self) -> None:
        """Authority bytes belong to the lexical Git path, never its target."""

        module = load_module()
        with tempfile.TemporaryDirectory() as temporary:
            temporary_root = pathlib.Path(temporary)
            repository = temporary_root / "repository"
            reference = temporary_root / "reference"
            repository.mkdir()
            revision = initialize_reference_checkout(reference)
            validator = (
                repository / "Scripts/smoke/foxrun/phase186_provenance.py"
            )
            validator.parent.mkdir(parents=True)
            validator.write_text("# original validator\n", encoding="utf-8")
            target = repository / "contained-test-target.py"
            target.write_text("# target bytes\n", encoding="utf-8")
            authority = (
                repository
                / "Scripts/smoke/foxrun/regression_checks/"
                "test_phase186_provenance.py"
            )
            authority.parent.mkdir(parents=True)
            try:
                authority.symlink_to(target)
            except OSError as exc:
                self.skipTest(f"symlink creation is unavailable: {exc}")
            ledger = synthetic_ledger_path(repository)
            ledger.write_text(
                json.dumps(
                    {
                        "schemaVersion": 1,
                        "reference": reference_payload(revision=revision),
                        "implementations": [
                            {
                                "path": (
                                    "Scripts/smoke/foxrun/phase186_provenance.py"
                                ),
                                "sha256": hashlib.sha256(
                                    validator.read_bytes()
                                ).hexdigest(),
                                "classification": "original",
                                "influence": "Original validator.",
                            },
                            {
                                "path": (
                                    "Scripts/smoke/foxrun/regression_checks/"
                                    "test_phase186_provenance.py"
                                ),
                                "sha256": hashlib.sha256(
                                    target.read_bytes()
                                ).hexdigest(),
                                "classification": "original",
                                "influence": "Original tests.",
                            },
                        ],
                    }
                ),
                encoding="utf-8",
            )

            errors = module.validate_repository_provenance(
                repository,
                reference,
                ledger,
            )

        self.assertTrue(
            any(
                "required Phase186B authority must be a regular "
                "non-symlink file" in error
                for error in errors
            ),
            errors,
        )
    def test_reference_status_includes_untracked_files(self) -> None:
        """An untracked file invalidates the supposedly pinned reference checkout."""

        module = load_module()
        with tempfile.TemporaryDirectory() as temporary:
            temporary_root = pathlib.Path(temporary)
            repository = temporary_root / "repository"
            reference = temporary_root / "reference"
            repository.mkdir()
            revision = initialize_reference_checkout(reference)
            implementation = (
                repository
                / "Packages/dev.unity2foxglove.ros2bridge/Runtime/Protocol/Test.cs"
            )
            implementation.parent.mkdir(parents=True)
            implementation.write_text(
                "internal sealed class Test {}\n",
                encoding="utf-8",
            )
            (reference / "untracked.txt").write_text("dirty\n", encoding="utf-8")
            ledger = synthetic_ledger_path(repository)
            ledger.write_text(
                json.dumps(
                    {
                        "schemaVersion": 1,
                        "reference": reference_payload(revision=revision),
                        "implementations": [
                            {
                                "path": (
                                    "Packages/dev.unity2foxglove.ros2bridge/"
                                    "Runtime/Protocol/Test.cs"
                                ),
                                "sha256": hashlib.sha256(
                                    implementation.read_bytes()
                                ).hexdigest(),
                                "classification": "original",
                                "influence": "Original implementation.",
                            }
                        ],
                    }
                ),
                encoding="utf-8",
            )

            errors = module.validate_repository_provenance(
                repository,
                reference,
                ledger,
            )

        self.assertTrue(
            any("untracked" in error for error in errors),
            errors,
        )
    def test_reference_root_must_be_exact_git_toplevel(self) -> None:
        """A nested directory cannot borrow its parent clone's Git identity."""

        module = load_module()
        with tempfile.TemporaryDirectory() as temporary:
            temporary_root = pathlib.Path(temporary)
            repository = temporary_root / "repository"
            reference = temporary_root / "reference"
            repository.mkdir()
            revision = initialize_reference_checkout(reference)
            implementation = (
                repository
                / "Packages/dev.unity2foxglove.ros2bridge/Runtime/Protocol/Test.cs"
            )
            implementation.parent.mkdir(parents=True)
            implementation.write_text(
                "internal sealed class Test {}\n",
                encoding="utf-8",
            )
            ledger = synthetic_ledger_path(repository)
            ledger.write_text(
                json.dumps(
                    {
                        "schemaVersion": 1,
                        "reference": reference_payload(revision=revision),
                        "implementations": [
                            {
                                "path": (
                                    "Packages/dev.unity2foxglove.ros2bridge/"
                                    "Runtime/Protocol/Test.cs"
                                ),
                                "sha256": hashlib.sha256(
                                    implementation.read_bytes()
                                ).hexdigest(),
                                "classification": "original",
                                "influence": "Original implementation.",
                            }
                        ],
                    }
                ),
                encoding="utf-8",
            )
            nested_reference = (
                reference
                / "com.unity.robotics.ros-tcp-connector"
                / "Runtime"
                / "TcpConnector"
            )

            errors = module.validate_repository_provenance(
                repository,
                nested_reference,
                ledger,
            )

        self.assertTrue(
            any("reference_root must be the exact Git top-level" in error
                for error in errors),
            errors,
        )
    def test_reference_sources_are_read_from_the_pinned_git_object(self) -> None:
        """Dirty checkout bytes cannot manufacture a source-overlap finding."""

        module = load_module()
        distinctive = "\n".join(
            [
                "private readonly Queue<OutgoingMessage> pendingMessages;",
                "public void QueueMessage(string topic, byte[] payload)",
                "pendingMessages.Enqueue(new OutgoingMessage(topic, payload));",
                "SignalSenderThreadWithoutBlockingTheUnityMainThread();",
            ]
        )
        with tempfile.TemporaryDirectory() as temporary:
            temporary_root = pathlib.Path(temporary)
            repository = temporary_root / "repository"
            reference = temporary_root / "reference"
            repository.mkdir()
            revision = initialize_reference_checkout(reference)
            implementation = (
                repository
                / "Packages/dev.unity2foxglove.ros2bridge/Runtime/Protocol/Test.cs"
            )
            implementation.parent.mkdir(parents=True)
            implementation.write_text(distinctive, encoding="utf-8")
            inspected = reference_payload()["inspectedFiles"][0]
            (reference / pathlib.PurePosixPath(str(inspected))).write_text(
                distinctive,
                encoding="utf-8",
            )
            ledger = synthetic_ledger_path(repository)
            ledger.write_text(
                json.dumps(
                    {
                        "schemaVersion": 1,
                        "reference": reference_payload(revision=revision),
                        "implementations": [
                            {
                                "path": (
                                    "Packages/dev.unity2foxglove.ros2bridge/"
                                    "Runtime/Protocol/Test.cs"
                                ),
                                "sha256": hashlib.sha256(
                                    implementation.read_bytes()
                                ).hexdigest(),
                                "classification": "original",
                                "influence": "Original implementation.",
                            }
                        ],
                    }
                ),
                encoding="utf-8",
            )

            errors = module.validate_repository_provenance(
                repository,
                reference,
                ledger,
            )

        self.assertTrue(
            any("tracked modifications" in error for error in errors),
            errors,
        )
        self.assertFalse(
            any("unexplained distinctive overlap" in error for error in errors),
            errors,
        )
    def test_reference_license_requires_the_exact_pinned_blob(self) -> None:
        """Two identifying substrings are not an Apache-2.0 license proof."""

        module = load_module()
        with tempfile.TemporaryDirectory() as temporary:
            temporary_root = pathlib.Path(temporary)
            repository = temporary_root / "repository"
            reference = temporary_root / "reference"
            repository.mkdir()
            revision = initialize_reference_checkout(reference)
            implementation = (
                repository
                / "Packages/dev.unity2foxglove.ros2bridge/Runtime/Protocol/Test.cs"
            )
            implementation.parent.mkdir(parents=True)
            implementation.write_text(
                "internal sealed class Test {}\n",
                encoding="utf-8",
            )
            ledger = synthetic_ledger_path(repository)
            ledger.write_text(
                json.dumps(
                    {
                        "schemaVersion": 1,
                        "reference": reference_payload(revision=revision),
                        "implementations": [
                            {
                                "path": (
                                    "Packages/dev.unity2foxglove.ros2bridge/"
                                    "Runtime/Protocol/Test.cs"
                                ),
                                "sha256": hashlib.sha256(
                                    implementation.read_bytes()
                                ).hexdigest(),
                                "classification": "original",
                                "influence": "Original implementation.",
                            }
                        ],
                    }
                ),
                encoding="utf-8",
            )

            errors = module.validate_repository_provenance(
                repository,
                reference,
                ledger,
            )

        self.assertTrue(
            any("LICENSE blob SHA-256 mismatch" in error for error in errors),
            errors,
        )
    def test_material_copy_summary_matches_per_record_classification(self) -> None:
        """The clean-room summary and per-file classifications cannot disagree."""

        module = load_module()
        for summary, classification in (
            (False, "materially_copied"),
            (True, "original"),
        ):
            with self.subTest(summary=summary, classification=classification):
                reference = reference_payload()
                reference["materialCopied"] = summary
                record = {
                    "path": "Runtime/Protocol.cs",
                    "sha256": sha256_text("source\n"),
                    "classification": classification,
                    "influence": "Declared provenance.",
                }
                if classification == "materially_copied":
                    record.update(
                        {
                            "referenceFiles": [
                                reference_payload()["inspectedFiles"][0]
                            ],
                            "licenseNotice": "Apache-2.0 material copied.",
                        }
                    )
                errors = module.validate_ledger_payload(
                    {
                        "schemaVersion": 1,
                        "reference": reference,
                        "implementations": [record],
                    },
                    actual_revision=REFERENCE_REVISION,
                    implementation_sources={"Runtime/Protocol.cs": "source\n"},
                    reference_sources={
                        relative: "internal sealed class UpstreamReference {}\n"
                        for relative in reference_payload()["inspectedFiles"]
                    },
                )
                self.assertTrue(
                    any("materialCopied" in error for error in errors),
                    errors,
                )
    def test_pre_move_inventory_reads_the_captured_git_tree(self) -> None:
        """Extraction after capture cannot invalidate an immutable inventory."""

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
            captured_tree = run_git(repository, "show", "-s", "--format=%T", captured)

            tracked.unlink()
            run_git(repository, "add", "-A")
            run_git(repository, "commit", "--quiet", "-m", "extract")
            inventory = repository / "inventory.json"
            inventory.write_text(
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

            errors = module.validate_pre_move_inventory(repository, inventory)

        self.assertEqual([], errors)


__all__ = [name for name in globals() if not name.startswith("__")]
