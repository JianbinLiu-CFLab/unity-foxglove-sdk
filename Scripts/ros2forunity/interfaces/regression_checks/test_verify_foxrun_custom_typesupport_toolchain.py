"""Regression checks for Phase181 custom-typesupport toolchain preflight."""

from __future__ import annotations

import json
import unittest
from pathlib import Path

from Scripts.test_support.phase181_scratch import temporary_directory

from Scripts.ros2forunity.interfaces.verify_foxrun_custom_typesupport_toolchain import (
    ProcessResult,
    ToolchainPreflightError,
    ToolchainPreflightRequest,
    preflight_toolchain,
)


class _FakeRunner:
    """Represent FakeRunner."""
    def __init__(self, visual_studio_root: Path, generators: tuple[str, ...] = ("Visual Studio 17 2022",), modules=None):
        """Initialize this object."""
        self.visual_studio_root = visual_studio_root
        self.generators = generators
        self.modules = modules or {
            "rosidl_adapter": True,
            "rosidl_generator_c": True,
            "rosidl_generator_cpp": True,
            "rosidl_typesupport_c": True,
        }
        self.calls: list[tuple[tuple[str, ...], dict[str, str]]] = []

    def __call__(self, argv, environment):
        """Implement the internal call step."""
        argv = tuple(str(value) for value in argv)
        self.calls.append((argv, dict(environment)))
        joined = " ".join(argv)
        if "vswhere" in argv[0].lower():
            return ProcessResult(0, str(self.visual_studio_root) + "\n", "")
        if argv[0].lower().endswith("cl.exe"):
            # MSVC emits its version banner but returns 2 without a source
            # filename. The preflight must recognize that documented probe.
            return ProcessResult(2, "", "Microsoft (R) C/C++ Optimizing Compiler Version 19.40\n")
        if argv[0].lower().endswith("msbuild.exe"):
            return ProcessResult(0, "17.10.0\n", "")
        if argv[-1:] == ("--version",):
            return ProcessResult(0, "cmake version 3.28.3\n", "")
        if argv[-2:] == ("-E", "capabilities"):
            return ProcessResult(0, json.dumps({"generators": [{"name": name} for name in self.generators]}), "")
        if argv[-1:] == ("--help",):
            return ProcessResult(0, "usage: colcon\n", "")
        if "importlib.util" in joined:
            return ProcessResult(0, json.dumps(self.modules, sort_keys=True), "")
        raise AssertionError("unexpected probe: " + repr(argv))


class ToolchainPreflightTests(unittest.TestCase):
    """Represent ToolchainPreflightTests."""
    def test_explicit_roots_select_pinned_tools_and_write_redacted_provenance(self) -> None:
        """Verify explicit roots select pinned tools and write redacted provenance."""
        with temporary_directory("toolchain-") as temporary_root:
            root = Path(temporary_root)
            request, visual_studio_root = self._make_request(root)
            runner = _FakeRunner(visual_studio_root)

            result = preflight_toolchain(request, runner=runner)

            self.assertTrue(result.ready)
            self.assertEqual("humble", result.distro)
            self.assertEqual("Visual Studio 17 2022", result.generator)
            self.assertTrue(any(call[0][-2:] == ("-E", "capabilities") for call in runner.calls))
            self.assertTrue(any(call[1]["ROS_DISTRO"] == "humble" for call in runner.calls))
            self.assertTrue(any(".pixi" in call[1]["PATH"] for call in runner.calls))
            compiler_probe = next(call for call in runner.calls if call[0][0].lower().endswith("cl.exe"))
            self.assertEqual(("/Bv",), compiler_probe[0][-1:])
            self.assertTrue(any(call[0][0].lower().endswith("msbuild.exe") for call in runner.calls))
            self.assertFalse(any(call[0][0].lower().endswith("cmd.exe") for call in runner.calls))

            provenance = root / "build" / "phase181" / "humble" / "provenance" / "toolchain.json"
            payload = json.loads(provenance.read_text(encoding="utf-8"))
            self.assertEqual("humble", payload["distro"])
            self.assertEqual("Visual Studio 17 2022", payload["generator"])
            self.assertNotIn(str(root), json.dumps(payload, sort_keys=True))
            self.assertTrue(all("label" in item and "status" in item for item in payload["requirements"]))

    def test_unavailable_generator_fails_with_one_bounded_code_and_remediation(self) -> None:
        """Verify unavailable generator fails with one bounded code and remediation."""
        with temporary_directory("toolchain-") as temporary_root:
            root = Path(temporary_root)
            request, visual_studio_root = self._make_request(root)
            runner = _FakeRunner(visual_studio_root, generators=("NMake Makefiles",))

            with self.assertRaises(ToolchainPreflightError) as raised:
                preflight_toolchain(request, runner=runner)

            self.assertEqual("FOXRUN_TOOLCHAIN001", raised.exception.code)
            self.assertEqual("select-supported-cmake-generator", raised.exception.remediation)
            self.assertNotIn(str(root), str(raised.exception))

    def test_missing_rosidl_module_fails_before_any_candidate_build(self) -> None:
        """Verify missing rosidl module fails before any candidate build."""
        with temporary_directory("toolchain-") as temporary_root:
            root = Path(temporary_root)
            request, visual_studio_root = self._make_request(root)
            modules = {
                "rosidl_adapter": True,
                "rosidl_generator_c": False,
                "rosidl_generator_cpp": True,
                "rosidl_typesupport_c": True,
            }

            with self.assertRaises(ToolchainPreflightError) as raised:
                preflight_toolchain(request, runner=_FakeRunner(visual_studio_root, modules=modules))

            self.assertEqual("FOXRUN_TOOLCHAIN001", raised.exception.code)
            self.assertEqual("repair-rosidl-python-modules", raised.exception.remediation)

    def test_missing_pinned_openssl_fails_before_candidate_build(self) -> None:
        """Verify missing pinned openssl fails before candidate build."""
        with temporary_directory("toolchain-") as temporary_root:
            root = Path(temporary_root)
            request, visual_studio_root = self._make_request(root)
            (request.ros2_root / ".pixi/envs/default/Library/include/openssl/ssl.h").unlink()

            with self.assertRaises(ToolchainPreflightError) as raised:
                preflight_toolchain(request, runner=_FakeRunner(visual_studio_root))

            self.assertEqual("FOXRUN_TOOLCHAIN001", raised.exception.code)
            self.assertEqual("repair-pinned-openssl", raised.exception.remediation)

    def test_missing_explicit_source_input_is_bounded_and_does_not_write_provenance(self) -> None:
        """Verify missing explicit source input is bounded and does not write provenance."""
        with temporary_directory("toolchain-") as temporary_root:
            root = Path(temporary_root)
            request, visual_studio_root = self._make_request(root)
            request = ToolchainPreflightRequest(
                distro=request.distro,
                ros2_root=request.ros2_root,
                ros2cs_source=root / "missing-ros2cs",
                r2fu_source=request.r2fu_source,
                build_root=request.build_root,
                generator=request.generator,
                vswhere=request.vswhere,
            )

            with self.assertRaises(ToolchainPreflightError) as raised:
                preflight_toolchain(request, runner=_FakeRunner(visual_studio_root))

            self.assertEqual("FOXRUN_TOOLCHAIN001", raised.exception.code)
            self.assertEqual("provide-ros2cs-source", raised.exception.remediation)
            self.assertFalse((root / "build" / "phase181" / "humble" / "provenance" / "toolchain.json").exists())

    @staticmethod
    def _make_request(root: Path) -> tuple[ToolchainPreflightRequest, Path]:
        """Implement the internal make request step."""
        ros2_root = root / "ros2_humble"
        for relative_path in (
            ".pixi/envs/default/python.exe",
            ".pixi/envs/default/Scripts/colcon.exe",
            ".pixi/envs/default/Library/bin/cmake.exe",
            ".pixi/envs/default/Library/include/openssl/ssl.h",
            ".pixi/envs/default/Library/lib/libcrypto.lib",
            "Lib/site-packages/rosidl_adapter/__init__.py",
            "Lib/site-packages/rosidl_generator_c/__init__.py",
            "Lib/site-packages/rosidl_generator_cpp/__init__.py",
            "Lib/site-packages/rosidl_typesupport_c/__init__.py",
        ):
            target = ros2_root / relative_path
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text("fixture", encoding="utf-8")

        ros2cs_source = root / "ros2cs"
        r2fu_source = root / "ros2-for-unity"
        (ros2cs_source / "src").mkdir(parents=True)
        (r2fu_source / "src").mkdir(parents=True)

        vswhere = root / "vswhere.exe"
        vswhere.write_text("fixture", encoding="utf-8")
        visual_studio_root = root / "VisualStudio"
        vsdevcmd = visual_studio_root / "Common7" / "Tools" / "VsDevCmd.bat"
        vsdevcmd.parent.mkdir(parents=True)
        vsdevcmd.write_text("fixture", encoding="utf-8")
        compiler = visual_studio_root / "VC" / "Tools" / "MSVC" / "14.51.36231" / "bin" / "Hostx64" / "x64" / "cl.exe"
        compiler.parent.mkdir(parents=True)
        compiler.write_text("fixture", encoding="utf-8")
        msbuild = visual_studio_root / "MSBuild" / "Current" / "Bin" / "MSBuild.exe"
        msbuild.parent.mkdir(parents=True)
        msbuild.write_text("fixture", encoding="utf-8")

        return (
            ToolchainPreflightRequest(
                distro="humble",
                ros2_root=ros2_root,
                ros2cs_source=ros2cs_source,
                r2fu_source=r2fu_source,
                build_root=root / "build",
                generator="Visual Studio 17 2022",
                vswhere=vswhere,
            ),
            visual_studio_root,
        )


if __name__ == "__main__":
    unittest.main()
