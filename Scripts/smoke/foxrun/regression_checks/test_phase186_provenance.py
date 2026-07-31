#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Regression checks for the Phase186 provenance and pre-move inventory gate."""

from __future__ import annotations

import importlib.util
import hashlib
import json
import pathlib
import subprocess
import sys
import tempfile
import unittest
from unittest import mock


ROOT = pathlib.Path(__file__).resolve().parents[4]
MODULE_PATH = ROOT / "Scripts/smoke/foxrun/phase186_provenance.py"
LEDGER_PATH = (
    ROOT
    / "Tools"
    / "ros2_bridge"
    / "unity2foxglove_ros2_bridge"
    / "PROVENANCE.json"
)
INVENTORY_PATH = (
    ROOT
    / "Packages"
    / "dev.unity2foxglove.sdk"
    / "Tests"
    / "Unit"
    / "Phase186"
    / "Fixtures"
    / "pre_move_sdk_ros_inventory.json"
)
REFERENCE_REMOTE = "https://github.com/Unity-Technologies/ROS-TCP-Connector.git"
REFERENCE_REVISION = "c27f00c6cf750d2d0564349b3039d19aa3925e7c"
REFERENCE_DATE = "2022-02-02T09:25:06-08:00"
REFERENCE_SUBJECT = "Release 0.7.0"
FIXTURE_PATH = (
    "Tools/ros2_bridge/unity2foxglove_ros2_bridge/"
    "test/fixtures/u2r2_protocol_vectors.json"
)
V1_TOP_LEVEL_KEYS = [
    "fixtureVersion",
    "protocol",
    "limits",
    "health",
    "preparePublisher",
    "publish",
    "negativeVectors",
]


def load_module():
    """Load the provenance gate from its repository path."""

    spec = importlib.util.spec_from_file_location("phase186_provenance", MODULE_PATH)
    if spec is None or spec.loader is None:
        raise RuntimeError("Could not load the Phase186 provenance gate.")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


def sha256_text(value: str) -> str:
    """Return the exact UTF-8 SHA-256 used by synthetic ledger records."""

    return hashlib.sha256(value.encode("utf-8")).hexdigest()


def canonical_json_sha256(value: object) -> str:
    """Return the canonical JSON digest used by the v1 authority record."""

    encoded = json.dumps(
        value,
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
    ).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def reference_payload(
    *,
    revision: str = REFERENCE_REVISION,
    commit_date: str = REFERENCE_DATE,
    subject: str = REFERENCE_SUBJECT,
) -> dict[str, object]:
    """Create one complete synthetic copy of the locked upstream metadata."""

    return {
        "repository": REFERENCE_REMOTE,
        "origin": REFERENCE_REMOTE,
        "revision": revision,
        "commitDate": commit_date,
        "subject": subject,
        "license": "Apache-2.0",
        "inspectedFiles": [
            "com.unity.robotics.ros-tcp-connector/Runtime/TcpConnector/ROSConnection.cs",
            "com.unity.robotics.ros-tcp-connector/Runtime/TcpConnector/OutgoingMessageSender.cs",
            "com.unity.robotics.ros-tcp-connector/Runtime/TcpConnector/MessagePool.cs",
            "com.unity.robotics.ros-tcp-connector/Runtime/TcpConnector/TopicMessageSender.cs",
            "com.unity.robotics.ros-tcp-connector/Editor/MessageGeneration/MessageParser.cs",
        ],
        "ideasReviewed": [
            "one component owns a connection lifecycle",
            "outbound work crosses a bounded sender boundary",
            "payload ownership can be pooled explicitly",
            "topic routing has a stable per-topic sender identity",
            "code generation separates parsing from emitted artifacts",
        ],
        "materialCopied": False,
    }


def run_git(repository: pathlib.Path, *arguments: str) -> str:
    """Run one deterministic local Git command for a temporary fixture."""

    completed = subprocess.run(
        ["git", *arguments],
        cwd=repository,
        check=True,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="strict",
    )
    return completed.stdout.strip()


def initialize_git_repository(repository: pathlib.Path) -> None:
    """Initialize one temporary repository without using global identity."""

    run_git(repository, "init", "--quiet")
    run_git(repository, "config", "user.name", "Phase186 Test")
    run_git(repository, "config", "user.email", "phase186@example.invalid")


def initialize_reference_checkout(reference: pathlib.Path) -> str:
    """Create a minimal official-origin reference checkout for gate tests."""

    reference.mkdir(parents=True)
    initialize_git_repository(reference)
    (reference / "LICENSE").write_text(
        "Apache License\nVersion 2.0, January 2004\n",
        encoding="utf-8",
    )
    inspected = reference_payload()["inspectedFiles"]
    for relative in inspected:
        target = reference / pathlib.PurePosixPath(str(relative))
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(
            "internal sealed class UpstreamReference {}\n",
            encoding="utf-8",
        )
    run_git(reference, "add", ".")
    run_git(reference, "commit", "--quiet", "-m", REFERENCE_SUBJECT)
    run_git(reference, "remote", "add", "origin", REFERENCE_REMOTE)
    return run_git(reference, "rev-parse", "HEAD")


def synthetic_ledger_path(repository: pathlib.Path) -> pathlib.Path:
    """Create the canonical release-ledger parent path in a synthetic repo."""

    ledger = (
        repository
        / "Tools"
        / "ros2_bridge"
        / "unity2foxglove_ros2_bridge"
        / "PROVENANCE.json"
    )
    ledger.parent.mkdir(parents=True, exist_ok=True)
    return ledger


class Phase186ProvenanceTests(unittest.TestCase):
    """Lock fail-closed provenance and exact pre-move inventory behavior."""

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

    def test_fixed_source_roots_discover_untracked_protocol_sources(self) -> None:
        """Filesystem discovery must not let an untracked source evade the ledger."""

        module = load_module()
        with tempfile.TemporaryDirectory() as temporary:
            repository = pathlib.Path(temporary) / "repository"
            reference = pathlib.Path(temporary) / "reference"
            repository.mkdir()
            revision = initialize_reference_checkout(reference)

            validator = (
                repository / "Scripts/smoke/foxrun/phase186_provenance.py"
            )
            validator.parent.mkdir(parents=True)
            validator.write_text("# original validator\n", encoding="utf-8")
            untracked = (
                repository
                / "Packages/dev.unity2foxglove.ros2bridge/Runtime/Protocol/Untracked.cs"
            )
            untracked.parent.mkdir(parents=True)
            untracked.write_text(
                "internal sealed class Untracked {}\n",
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
                                    "Scripts/smoke/foxrun/phase186_provenance.py"
                                ),
                                "sha256": hashlib.sha256(
                                    validator.read_bytes()
                                ).hexdigest(),
                                "classification": "original",
                                "influence": "Original validator.",
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
            any("Untracked.cs" in error and "no provenance record" in error
                for error in errors),
            errors,
        )

    def test_fixed_source_roots_discover_nested_untracked_protocol_sources(
        self,
    ) -> None:
        """A nested protocol source cannot hide below a fixed source root."""

        module = load_module()
        with tempfile.TemporaryDirectory() as temporary:
            repository = pathlib.Path(temporary) / "repository"
            reference = pathlib.Path(temporary) / "reference"
            repository.mkdir()
            revision = initialize_reference_checkout(reference)

            validator = (
                repository / "Scripts/smoke/foxrun/phase186_provenance.py"
            )
            validator.parent.mkdir(parents=True)
            validator.write_text("# original validator\n", encoding="utf-8")
            nested = (
                repository
                / "Packages/dev.unity2foxglove.ros2bridge/Runtime/"
                "Protocol/Internal/Hidden.cs"
            )
            nested.parent.mkdir(parents=True)
            nested.write_text(
                "internal sealed class Hidden {}\n",
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
                                    "Scripts/smoke/foxrun/phase186_provenance.py"
                                ),
                                "sha256": hashlib.sha256(
                                    validator.read_bytes()
                                ).hexdigest(),
                                "classification": "original",
                                "influence": "Original validator.",
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
            any(
                "Hidden.cs" in error and "no provenance record" in error
                for error in errors
            ),
            errors,
        )

    def test_fixed_source_roots_reject_symlink_directory_escape(self) -> None:
        """A fixed source root cannot hide sources behind a linked directory."""

        module = load_module()
        with tempfile.TemporaryDirectory() as temporary:
            temporary_root = pathlib.Path(temporary)
            repository = temporary_root / "repository"
            reference = temporary_root / "reference"
            outside = temporary_root / "outside"
            repository.mkdir()
            outside.mkdir()
            revision = initialize_reference_checkout(reference)

            validator = (
                repository / "Scripts/smoke/foxrun/phase186_provenance.py"
            )
            validator.parent.mkdir(parents=True)
            validator.write_text("# original validator\n", encoding="utf-8")
            hidden = outside / "Hidden.cs"
            hidden.write_text(
                "internal sealed class Hidden {}\n",
                encoding="utf-8",
            )
            linked = (
                repository
                / "Packages/dev.unity2foxglove.ros2bridge/Runtime/"
                "Protocol/Linked"
            )
            linked.parent.mkdir(parents=True)
            try:
                linked.symlink_to(outside, target_is_directory=True)
            except OSError as exc:
                self.skipTest(f"directory symlink creation is unavailable: {exc}")
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
            any(
                "protocol source directory must not be a symlink or reparse point"
                in error
                for error in errors
            ),
            errors,
        )

    def test_fixed_source_roots_reject_uppercase_extension_source(self) -> None:
        """Windows casing cannot hide a protocol source from the fixed scan."""

        module = load_module()
        with tempfile.TemporaryDirectory() as temporary:
            repository = pathlib.Path(temporary) / "repository"
            reference = pathlib.Path(temporary) / "reference"
            repository.mkdir()
            revision = initialize_reference_checkout(reference)

            validator = (
                repository / "Scripts/smoke/foxrun/phase186_provenance.py"
            )
            validator.parent.mkdir(parents=True)
            validator.write_text("# original validator\n", encoding="utf-8")
            hidden = (
                repository
                / "Packages/dev.unity2foxglove.ros2bridge/Runtime/"
                "Protocol/Internal/Hidden.CS"
            )
            hidden.parent.mkdir(parents=True)
            hidden.write_text(
                "internal sealed class Hidden {}\n",
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
                                    "Scripts/smoke/foxrun/phase186_provenance.py"
                                ),
                                "sha256": hashlib.sha256(
                                    validator.read_bytes()
                                ).hexdigest(),
                                "classification": "original",
                                "influence": "Original validator.",
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
            any(
                "Hidden.CS" in error and "no provenance record" in error
                for error in errors
            ),
            errors,
        )

    def test_fixed_source_roots_reject_broken_reparse_entry(self) -> None:
        """A broken linked directory is still an explicit fixed-root failure."""

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
            broken = (
                repository
                / "Packages/dev.unity2foxglove.ros2bridge/Runtime/"
                "Protocol/Broken"
            )
            broken.parent.mkdir(parents=True)
            try:
                broken.symlink_to(
                    temporary_root / "missing-directory",
                    target_is_directory=True,
                )
            except OSError as exc:
                self.skipTest(f"directory symlink creation is unavailable: {exc}")
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
            any(
                "protocol source entry must not be a symlink or reparse point"
                in error
                for error in errors
            ),
            errors,
        )

    def test_implementation_path_resolution_cannot_escape_repository(self) -> None:
        """A ledgered source reached through a symlink is still containment checked."""

        module = load_module()
        with tempfile.TemporaryDirectory() as temporary:
            temporary_root = pathlib.Path(temporary)
            repository = temporary_root / "repository"
            reference = temporary_root / "reference"
            repository.mkdir()
            revision = initialize_reference_checkout(reference)
            escaped = temporary_root / "Escaped.cs"
            escaped.write_text("internal sealed class Escaped {}\n", encoding="utf-8")
            link = (
                repository
                / "Packages/dev.unity2foxglove.ros2bridge/Runtime/Protocol/Escaped.cs"
            )
            link.parent.mkdir(parents=True)
            try:
                link.symlink_to(escaped)
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
                                    "Packages/dev.unity2foxglove.ros2bridge/"
                                    "Runtime/Protocol/Escaped.cs"
                                ),
                                "sha256": hashlib.sha256(
                                    escaped.read_bytes()
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
            any("resolves outside repository" in error for error in errors),
            errors,
        )

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


if __name__ == "__main__":
    unittest.main()
