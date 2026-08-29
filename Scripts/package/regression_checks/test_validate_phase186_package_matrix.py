#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for the Phase186 package-matrix validator.

from __future__ import annotations

import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock


ROOT = Path(__file__).resolve().parents[3]
VALIDATOR_PATH = ROOT / "Scripts/package/validate_phase186_package_matrix.py"


def load_module(name: str, path: Path):
    """Load one validation script as an isolated module."""
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


class Phase186PackageMatrixTests(unittest.TestCase):
    """Lock failure evidence and assembly-reference boundary behavior."""

    def setUp(self) -> None:
        """Load a fresh validator for every test."""
        self.validator = load_module(
            "phase186_package_matrix_under_test",
            VALIDATOR_PATH,
        )

    def test_compile_failure_still_records_the_complete_matrix(self) -> None:
        """One failed composition must not suppress the remaining evidence."""
        outcomes = [
            subprocess.CompletedProcess([], 0, stdout="sdk ok"),
            subprocess.CompletedProcess([], 7, stdout="r2fu failed"),
            subprocess.CompletedProcess([], 0, stdout="bridge ok"),
            subprocess.CompletedProcess([], 0, stdout="all ok"),
        ]
        with tempfile.TemporaryDirectory() as temp:
            report = Path(temp) / "report.json"
            with mock.patch.object(self.validator, "REPORT", report), mock.patch.object(
                self.validator.subprocess,
                "run",
                side_effect=outcomes,
            ), mock.patch.object(
                self.validator,
                "validate_boundaries",
                return_value=["boundary"],
            ):
                self.assertEqual(1, self.validator.main())
            payload = json.loads(report.read_text(encoding="utf-8"))

        self.assertEqual("FAIL", payload["verdict"])
        self.assertEqual(4, len(payload["compileGates"]))
        self.assertEqual([0, 7, 0, 0], [row["exitCode"] for row in payload["compileGates"]])

    def test_guid_asmdef_reference_resolves_to_forbidden_assembly(self) -> None:
        """GUID-form references must enforce the same package boundary as names."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            forbidden_root = root / "bridge"
            forbidden_root.mkdir()
            forbidden = forbidden_root / "Bridge.asmdef"
            forbidden.write_text(
                '{"name":"Unity2Foxglove.Ros2Bridge"}',
                encoding="utf-8",
            )
            Path(str(forbidden) + ".meta").write_text(
                "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\n",
                encoding="utf-8",
            )
            consumer = root / "Consumer.asmdef"
            consumer.write_text(
                '{"name":"Consumer","references":'
                '["GUID:0123456789abcdef0123456789abcdef"]}',
                encoding="utf-8",
            )

            self.assertTrue(
                self.validator._references_forbidden_assembly(
                    consumer,
                    "Unity2Foxglove.Ros2Bridge",
                    forbidden_root,
                )
            )

    def test_named_child_asmdef_reference_resolves_to_forbidden_package(self) -> None:
        """Named references to a forbidden package's child assembly must be rejected."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            forbidden_root = root / "bridge"
            forbidden_root.mkdir()
            child = forbidden_root / "Bridge.Editor.asmdef"
            child.write_text(
                '{"name":"Unity2Foxglove.Ros2Bridge.Editor"}',
                encoding="utf-8",
            )
            consumer = root / "Consumer.asmdef"
            consumer.write_text(
                '{"name":"Consumer","references":'
                '["Unity2Foxglove.Ros2Bridge.Editor"]}',
                encoding="utf-8",
            )

            self.assertTrue(
                self.validator._references_forbidden_assembly(
                    consumer,
                    "Unity2Foxglove.Ros2Bridge",
                    forbidden_root,
                )
            )

    def test_guid_child_asmdef_reference_resolves_to_forbidden_package(self) -> None:
        """GUID references to a forbidden package's child assembly must be rejected."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            forbidden_root = root / "bridge"
            forbidden_root.mkdir()
            for filename, name, guid in (
                (
                    "Bridge.asmdef",
                    "Unity2Foxglove.Ros2Bridge",
                    "0123456789abcdef0123456789abcdef",
                ),
                (
                    "Bridge.Editor.asmdef",
                    "Unity2Foxglove.Ros2Bridge.Editor",
                    "fedcba9876543210fedcba9876543210",
                ),
            ):
                asmdef = forbidden_root / filename
                asmdef.write_text(json.dumps({"name": name}), encoding="utf-8")
                Path(str(asmdef) + ".meta").write_text(
                    f"fileFormatVersion: 2\nguid: {guid}\n",
                    encoding="utf-8",
                )
            consumer = root / "Consumer.asmdef"
            consumer.write_text(
                '{"name":"Consumer","references":'
                '["GUID:fedcba9876543210fedcba9876543210"]}',
                encoding="utf-8",
            )

            self.assertTrue(
                self.validator._references_forbidden_assembly(
                    consumer,
                    "Unity2Foxglove.Ros2Bridge",
                    forbidden_root,
                )
            )

    def test_public_boundary_rejects_sibling_packaged_r2fu_guid_reference(self) -> None:
        """The public matrix gate resolves R2FU child GUIDs across package roots."""

        def write_asmdef(
            path: Path,
            name: str,
            guid: str,
            references: list[str] | None = None,
        ) -> None:
            payload = {"name": name}
            if references is not None:
                payload["references"] = references
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(json.dumps(payload), encoding="utf-8")
            Path(str(path) + ".meta").write_text(
                f"fileFormatVersion: 2\nguid: {guid}\n",
                encoding="utf-8",
            )

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            packages = root / "Packages"
            sdk = packages / "dev.unity2foxglove.sdk"
            r2fu = packages / "dev.unity2foxglove.ros2forunity"
            bridge = packages / "dev.unity2foxglove.ros2bridge"
            gateway = packages / "dev.unity2foxglove.remotegateway.win64"
            sibling = packages / "dev.unity2foxglove.ros2forunity.runtime.humble.win64"
            unrelated = packages / "unrelated.package"
            for package in (sdk, r2fu, bridge, gateway, sibling, unrelated):
                package.mkdir(parents=True)

            (sdk / "package.json").write_text(
                json.dumps(
                    {
                        "name": "dev.unity2foxglove.sdk",
                        "version": "1.9.6",
                        "dependencies": {
                            "com.unity.nuget.newtonsoft-json": "3.2.1",
                            "com.unity.burst": "1.8.18",
                            "com.unity.collections": "2.5.5",
                            "com.unity.mathematics": "1.3.2",
                        },
                    }
                ),
                encoding="utf-8",
            )
            (r2fu / "package.json").write_text(
                json.dumps(
                    {
                        "name": "dev.unity2foxglove.ros2forunity",
                        "version": "0.1.0-preview.1",
                        "dependencies": {"dev.unity2foxglove.sdk": "1.9.6"},
                    }
                ),
                encoding="utf-8",
            )
            (bridge / "package.json").write_text(
                json.dumps(
                    {
                        "name": "dev.unity2foxglove.ros2bridge",
                        "version": "0.1.0-preview.1",
                        "dependencies": {"dev.unity2foxglove.sdk": "1.9.6"},
                    }
                ),
                encoding="utf-8",
            )
            (gateway / "package.json").write_text(
                json.dumps(
                    {
                        "name": "dev.unity2foxglove.remotegateway.win64",
                        "version": "0.1.0-preview.1",
                        "dependencies": {"dev.unity2foxglove.sdk": "1.9.6"},
                    }
                ),
                encoding="utf-8",
            )
            (sibling / "package.json").write_text(
                json.dumps(
                    {
                        "name": "dev.unity2foxglove.ros2forunity.runtime.humble.win64",
                        "version": "0.1.0-preview.1",
                        "dependencies": {},
                    }
                ),
                encoding="utf-8",
            )
            (unrelated / "package.json").write_text(
                json.dumps({"name": "unrelated.package", "version": "1.0.0"}),
                encoding="utf-8",
            )

            write_asmdef(
                r2fu / "Runtime.asmdef",
                "Unity2Foxglove.Ros2ForUnity",
                "11111111111111111111111111111111",
            )
            write_asmdef(
                r2fu / "Editor.asmdef",
                "Unity2Foxglove.Ros2ForUnity.Editor",
                "55555555555555555555555555555555",
            )
            write_asmdef(
                sibling / "RuntimeChild.asmdef",
                "Unity2Foxglove.Ros2ForUnity.Runtime",
                "22222222222222222222222222222222",
            )
            write_asmdef(
                unrelated / "Other.asmdef",
                "Unity2Foxglove.Ros2ForUnity.Decoy",
                "44444444444444444444444444444444",
            )
            write_asmdef(
                sdk / "Sdk.asmdef",
                "Unity2Foxglove.Sdk",
                "66666666666666666666666666666666",
            )

            for package, dll_name, project_name in (
                (sdk, "FoxgloveLogSourceGenerator.dll", "FoxgloveLogSourceGenerator.csproj"),
                (r2fu, "Unity2Foxglove.Ros2ForUnity.FoxRunSourceGenerator.dll", "FoxRunR2fuSourceGenerator.csproj"),
                (bridge, "Unity2Foxglove.Ros2Bridge.FoxRunSourceGenerator.dll", "FoxRunBridgeSourceGenerator.csproj"),
            ):
                analyzer = package / "Editor/SourceGenerators/analyzers/dotnet/cs" / dll_name
                analyzer.parent.mkdir(parents=True, exist_ok=True)
                analyzer.write_bytes(b"dll")
                Path(str(analyzer) + ".meta").write_text(
                    "fileFormatVersion: 2\nguid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n",
                    encoding="utf-8",
                )
                (package / "Editor/SourceGenerators" / project_name).write_text(
                    "<Project />\n", encoding="utf-8"
                )

            bridge_descriptor = bridge / "Bridge.asmdef"
            cases = (
                (
                    "sibling GUID",
                    ["GUID:22222222222222222222222222222222"],
                    True,
                ),
                (
                    "sibling name",
                    ["Unity2Foxglove.Ros2ForUnity.Runtime"],
                    True,
                ),
                (
                    "unrelated GUID",
                    ["GUID:44444444444444444444444444444444"],
                    False,
                ),
                (
                    "co-located child GUID",
                    ["GUID:55555555555555555555555555555555"],
                    True,
                ),
                ("clean", [], False),
            )
            with mock.patch.object(self.validator, "ROOT", root):
                for label, references, should_fail in cases:
                    with self.subTest(label=label):
                        write_asmdef(
                            bridge_descriptor,
                            "Bridge",
                            "33333333333333333333333333333333",
                            references,
                        )
                        if should_fail:
                            with self.assertRaisesRegex(
                                RuntimeError,
                                r"Bridge\.asmdef references Unity2Foxglove\.Ros2ForUnity",
                            ):
                                self.validator.validate_boundaries()
                        else:
                            checked = self.validator.validate_boundaries()
                            self.assertIn("Packages/dev.unity2foxglove.ros2bridge/Bridge.asmdef", checked)

    def test_public_boundary_authenticates_the_complete_package_matrix(self) -> None:
        """The public matrix gate rejects identity, version, and dependency drift."""

        expected_packages = {
            "sdk": (
                "dev.unity2foxglove.sdk",
                "1.9.6",
                {
                    "com.unity.nuget.newtonsoft-json": "3.2.1",
                    "com.unity.burst": "1.8.18",
                    "com.unity.collections": "2.5.5",
                    "com.unity.mathematics": "1.3.2",
                },
            ),
            "r2fu": (
                "dev.unity2foxglove.ros2forunity",
                "0.1.0-preview.1",
                {"dev.unity2foxglove.sdk": "1.9.6"},
            ),
            "bridge": (
                "dev.unity2foxglove.ros2bridge",
                "0.1.0-preview.1",
                {"dev.unity2foxglove.sdk": "1.9.6"},
            ),
            "remote_gateway": (
                "dev.unity2foxglove.remotegateway.win64",
                "0.1.0-preview.1",
                {"dev.unity2foxglove.sdk": "1.9.6"},
            ),
        }

        def write_manifest(path: Path, key: str) -> None:
            name, version, dependencies = expected_packages[key]
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(
                json.dumps(
                    {
                        "name": name,
                        "version": version,
                        "dependencies": dependencies,
                    }
                ),
                encoding="utf-8",
            )

        def write_analyzer_assets(package: Path, dll_name: str, project_name: str) -> None:
            analyzer = package / "Editor/SourceGenerators/analyzers/dotnet/cs" / dll_name
            analyzer.parent.mkdir(parents=True, exist_ok=True)
            analyzer.write_bytes(b"dll")
            Path(str(analyzer) + ".meta").write_text(
                "fileFormatVersion: 2\nguid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n",
                encoding="utf-8",
            )
            (package / "Editor/SourceGenerators" / project_name).write_text(
                "<Project />\n", encoding="utf-8"
            )

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            packages_root = root / "Packages"
            package_paths = {
                key: packages_root / relative
                for key, relative in {
                    "sdk": "dev.unity2foxglove.sdk",
                    "r2fu": "dev.unity2foxglove.ros2forunity",
                    "bridge": "dev.unity2foxglove.ros2bridge",
                    "remote_gateway": "dev.unity2foxglove.remotegateway.win64",
                }.items()
            }
            for key, package in package_paths.items():
                package.mkdir(parents=True)
                write_manifest(package / "package.json", key)

            for key, dll_name, project_name in (
                ("sdk", "FoxgloveLogSourceGenerator.dll", "FoxgloveLogSourceGenerator.csproj"),
                ("r2fu", "Unity2Foxglove.Ros2ForUnity.FoxRunSourceGenerator.dll", "FoxRunR2fuSourceGenerator.csproj"),
                ("bridge", "Unity2Foxglove.Ros2Bridge.FoxRunSourceGenerator.dll", "FoxRunBridgeSourceGenerator.csproj"),
            ):
                write_analyzer_assets(package_paths[key], dll_name, project_name)

            mutations = (
                (
                    "bridge dependency value",
                    "bridge",
                    lambda data: data["dependencies"].update({"dev.unity2foxglove.sdk": "9.9.9"}),
                    r"package matrix bridge dependencies",
                ),
                (
                    "bridge unexpected dependency",
                    "bridge",
                    lambda data: data["dependencies"].update({"com.example.unexpected": "1.0.0"}),
                    r"package matrix bridge dependencies",
                ),
                (
                    "r2fu identity",
                    "r2fu",
                    lambda data: data.update({"name": "dev.unity2foxglove.ros2forunity.moved"}),
                    r"package matrix r2fu name",
                ),
                (
                    "r2fu dependency missing",
                    "r2fu",
                    lambda data: data["dependencies"].pop("dev.unity2foxglove.sdk"),
                    r"package matrix r2fu dependencies",
                ),
                (
                    "r2fu dependency value",
                    "r2fu",
                    lambda data: data["dependencies"].update({"dev.unity2foxglove.sdk": "9.9.9"}),
                    r"package matrix r2fu dependencies",
                ),
                (
                    "remote gateway version",
                    "remote_gateway",
                    lambda data: data.update({"version": "9.9.9"}),
                    r"package matrix remote_gateway version",
                ),
                (
                    "remote gateway unexpected dependency",
                    "remote_gateway",
                    lambda data: data["dependencies"].update({"com.example.unexpected": "1.0.0"}),
                    r"package matrix remote_gateway dependencies",
                ),
                (
                    "sdk dependency set",
                    "sdk",
                    lambda data: data["dependencies"].update({"com.unity.burst": "9.9.9"}),
                    r"package matrix sdk dependencies",
                ),
                (
                    "sdk dependency missing",
                    "sdk",
                    lambda data: data["dependencies"].pop("com.unity.collections"),
                    r"package matrix sdk dependencies",
                ),
                (
                    "sdk unexpected dependency",
                    "sdk",
                    lambda data: data["dependencies"].update({"com.example.unexpected": "1.0.0"}),
                    r"package matrix sdk dependencies",
                ),
                (
                    "missing remote gateway manifest",
                    "remote_gateway",
                    lambda data: None,
                    r"package matrix remote_gateway manifest",
                ),
            )
            with mock.patch.object(self.validator, "ROOT", root):
                for label, key, mutate, expected_error in mutations:
                    with self.subTest(label=label):
                        manifest = package_paths[key] / "package.json"
                        if label == "missing remote gateway manifest":
                            manifest.unlink()
                        else:
                            data = json.loads(manifest.read_text(encoding="utf-8"))
                            mutate(data)
                            manifest.write_text(json.dumps(data), encoding="utf-8")
                        with self.assertRaisesRegex(RuntimeError, expected_error):
                            self.validator.validate_boundaries()
                        if not manifest.exists():
                            write_manifest(manifest, key)
                        else:
                            write_manifest(manifest, key)



if __name__ == "__main__":
    unittest.main()
