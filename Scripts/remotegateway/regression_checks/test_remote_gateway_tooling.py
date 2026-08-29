#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for remote-gateway build and acceptance helpers.

from __future__ import annotations

import datetime as dt
import hashlib
import importlib.util
import json
import os
import sys
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest import mock


ROOT = Path(__file__).resolve().parents[3]
BUILD_PATH = ROOT / "Scripts/remotegateway/build_foxglove_c_win64.py"
ACCEPTANCE_PATH = ROOT / "Scripts/remotegateway/run_cloud_acceptance.py"


def load_module(name: str, path: Path):
    """Load one repository script as an isolated module."""
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


class RemoteGatewayToolingTests(unittest.TestCase):
    """Lock artifact allow-lists, provenance, and acceptance identity."""

    def setUp(self) -> None:
        """Load fresh build and acceptance modules."""
        self.build = load_module("remote_gateway_build_under_test", BUILD_PATH)
        self.acceptance = load_module(
            "remote_gateway_acceptance_under_test",
            ACCEPTANCE_PATH,
        )

    def test_copy_rejects_an_unreviewed_artifact_name(self) -> None:
        """Callers cannot widen the package-copy allow-list."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            target = root / "target"
            release = target / "release"
            package = root / "package"
            release.mkdir(parents=True)
            (release / "unreviewed.dll").write_bytes(b"unreviewed")
            manifest = root / "manifest.json"
            manifest.write_text("{}", encoding="utf-8")
            with mock.patch.object(self.build, "PACKAGE_PLUGIN_DIR", package):
                with self.assertRaisesRegex(ValueError, "unapproved artifact"):
                    self.build.copy_approved_artifacts(
                        target,
                        manifest,
                        ("unreviewed.dll",),
                    )

        self.assertFalse((package / "unreviewed.dll").exists())

    def test_copy_removes_a_stale_unselected_pdb(self) -> None:
        """A later non-debug copy must not retain an older symbol artifact."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            target = root / "target"
            release = target / "release"
            package = root / "package"
            release.mkdir(parents=True)
            package.mkdir()
            for name in self.build.APPROVED_ARTIFACTS:
                (release / name).write_bytes(name.encode("utf-8"))
            (package / self.build.PDB_ARTIFACT).write_bytes(b"stale")
            manifest = root / "manifest.json"
            manifest.write_text("{}", encoding="utf-8")
            with mock.patch.object(self.build, "PACKAGE_PLUGIN_DIR", package):
                self.build.copy_approved_artifacts(
                    target,
                    manifest,
                    self.build.APPROVED_ARTIFACTS,
                )

            self.assertFalse((package / self.build.PDB_ARTIFACT).exists())

    def test_manifest_records_the_static_crt_cxx_flag(self) -> None:
        """Native provenance must include both C and C++ CRT controls."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            release = root / "target/release"
            release.mkdir(parents=True)
            (release / "foxglove.dll").write_bytes(b"dll")
            staging = root / "staging"
            environment = {
                "RUSTFLAGS": "-C target-feature=+crt-static",
                "CFLAGS_x86_64_pc_windows_msvc": "/MT",
                "CXXFLAGS_x86_64_pc_windows_msvc": "/MT",
                "AWS_LC_SYS_PREBUILT_NASM": "1",
                "CARGO_TARGET_DIR": str(root / "target"),
            }
            with mock.patch.object(self.build, "STAGING", staging):
                path = self.build.write_manifest(
                    root / "target",
                    environment,
                    ("foxglove.dll",),
                )
            payload = json.loads(path.read_text(encoding="utf-8"))

        self.assertEqual("/MT", payload["cxxflags"])

    def test_token_is_trimmed_before_it_is_inherited(self) -> None:
        """Whitespace used for validation cannot survive into Unity's token."""
        with mock.patch.dict(
            os.environ,
            {self.acceptance.TOKEN_ENV: "  device-token  "},
            clear=False,
        ):
            self.acceptance.ensure_token_env()
            self.assertEqual(
                "device-token",
                os.environ[self.acceptance.TOKEN_ENV],
            )

    def test_native_acceptance_build_does_not_inherit_device_token(self) -> None:
        """The Cargo build child must not receive the Unity Cloud credential."""
        sentinel = "phase187-d05-nonsecret"
        with mock.patch.dict(
            os.environ,
            {self.acceptance.TOKEN_ENV: sentinel},
            clear=False,
        ):
            with mock.patch.object(self.acceptance.subprocess, "run") as run:
                self.acceptance.build_and_copy_native()

            child_environment = run.call_args.kwargs["env"]
            self.assertNotIn(self.acceptance.TOKEN_ENV, child_environment)
            self.assertEqual(sentinel, os.environ[self.acceptance.TOKEN_ENV])

    def test_direct_native_build_environment_does_not_inherit_device_token(self) -> None:
        """Direct invocations of the native build helper apply the same boundary."""
        sentinel = "phase187-d05-direct-build"
        arguments = SimpleNamespace(libclang_path=None, target_dir="phase187-target")
        with mock.patch.dict(
            os.environ,
            {"FOXGLOVE_DEVICE_TOKEN": sentinel},
            clear=False,
        ):
            child_environment = self.build.build_environment(arguments)

        self.assertNotIn("FOXGLOVE_DEVICE_TOKEN", child_environment)

    def test_skip_build_requires_manifest_identity_match(self) -> None:
        """The skip-build path rejects a name, digest, or size mismatch."""
        payload = b"phase187-d05-004-native"
        actual_hash = hashlib.sha256(payload).hexdigest()
        cases = (
            ("artifact", "other.dll", "artifact name"),
            ("sha256", "0" * 64, "sha256"),
            ("sizeBytes", len(payload) + 1, "sizeBytes"),
        )

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            dll = root / "foxglove.dll"
            dll.write_bytes(payload)
            manifest = root / "foxglove-gateway-native-artifact.json"

            for field, value, message in cases:
                declared = {
                    "artifact": "foxglove.dll",
                    "sha256": actual_hash,
                    "sizeBytes": len(payload),
                }
                declared[field] = value
                manifest.write_text(json.dumps(declared), encoding="utf-8")
                with self.subTest(field=field):
                    with mock.patch.object(self.acceptance, "ROOT", root), mock.patch.object(
                        self.acceptance,
                        "PLUGIN_DIR",
                        root,
                    ):
                        with self.assertRaisesRegex(SystemExit, message):
                            self.acceptance.ensure_native_artifact()

    def test_skip_build_accepts_matching_manifest_identity(self) -> None:
        """A manifest bound to the exact DLL bytes remains accepted."""
        payload = b"phase187-d05-004-native-valid"
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            dll = root / "foxglove.dll"
            dll.write_bytes(payload)
            (root / "foxglove-gateway-native-artifact.json").write_text(
                json.dumps(
                    {
                        "artifact": dll.name,
                        "sha256": hashlib.sha256(payload).hexdigest(),
                        "sizeBytes": len(payload),
                    }
                ),
                encoding="utf-8",
            )
            with mock.patch.object(self.acceptance, "ROOT", root), mock.patch.object(
                self.acceptance,
                "PLUGIN_DIR",
                root,
            ):
                self.acceptance.ensure_native_artifact()

    def test_skip_build_accepts_builder_emitted_uppercase_manifest_identity(self) -> None:
        """The validator accepts the uppercase digest emitted by the build helper."""
        payload = b"phase187-d05-004-native-uppercase"
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            dll = root / "foxglove.dll"
            dll.write_bytes(payload)
            (root / "foxglove-gateway-native-artifact.json").write_text(
                json.dumps(
                    {
                        "artifact": dll.name,
                        "sha256": hashlib.sha256(payload).hexdigest().upper(),
                        "sizeBytes": len(payload),
                    }
                ),
                encoding="utf-8",
            )
            with mock.patch.object(self.acceptance, "ROOT", root), mock.patch.object(
                self.acceptance,
                "PLUGIN_DIR",
                root,
            ):
                self.acceptance.ensure_native_artifact()

    def test_same_timestamp_still_creates_distinct_run_directories(self) -> None:
        """Concurrent acceptance launches must never share evidence output."""
        class FixedDateTime(dt.datetime):
            """Return one stable timestamp for both calls."""

            @classmethod
            def now(cls, tz=None):
                """Return the fixed test time."""
                return cls(2026, 8, 6, 7, 8, 9)

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            with mock.patch.object(self.acceptance, "ROOT", root), mock.patch.object(
                self.acceptance.dt,
                "datetime",
                FixedDateTime,
            ):
                first = self.acceptance.create_run_dir()
                second = self.acceptance.create_run_dir()

        self.assertNotEqual(first, second)


if __name__ == "__main__":
    unittest.main()
