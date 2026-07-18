"""Regression checks for Phase181 custom typesupport candidate packaging."""

from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from Scripts.ros2forunity.interfaces.build_foxrun_custom_typesupport_addon import (
    CandidateBuildError,
    CandidateBuildRequest,
    _runtime_rmws,
    candidate_package_root,
    select_candidate_native_libraries,
    verify_candidate_source_lock,
)
from Scripts.ros2forunity.interfaces.foxrun_custom_typesupport_common import (
    ROS_PACKAGE_NAME,
    compute_static_interface_digest,
)


class CustomTypesupportCandidateBuildTests(unittest.TestCase):
    def test_candidate_package_is_constrained_to_its_distro_build_root(self) -> None:
        request = self._request()
        self.assertEqual(
            request.build_root / "phase181" / "humble" / "candidate" / "package",
            candidate_package_root(request),
        )

        unsafe = CandidateBuildRequest(
            distro="humble",
            static_interface_package=request.static_interface_package,
            base_runtime_package=request.base_runtime_package,
            ros2_root=request.ros2_root,
            ros2cs_source=request.ros2cs_source,
            ros2cs_install=request.ros2cs_install,
            r2fu_source=request.r2fu_source,
            build_root=request.build_root.parent,
        )
        with self.assertRaises(CandidateBuildError):
            candidate_package_root(unsafe)

    def test_source_lock_drift_fails_before_build(self) -> None:
        with self._fixture() as fixture:
            request = fixture.request
            self.assertEqual(fixture.digest, verify_candidate_source_lock(request))
            (fixture.static / "Ros2Package~" / "msg" / "State.msg").write_text("string changed\n", encoding="utf-8")
            with self.assertRaises(CandidateBuildError):
                verify_candidate_source_lock(request)

    def test_native_allowlist_excludes_python_generator_and_preserves_required_closure(self) -> None:
        root = Path("candidate") / "i" / "bin"
        paths = (
            root / (ROS_PACKAGE_NAME + "__rosidl_generator_c.dll"),
            root / (ROS_PACKAGE_NAME + "__rosidl_generator_py.dll"),
            root / (ROS_PACKAGE_NAME + "__rosidl_typesupport_c.dll"),
            root / (ROS_PACKAGE_NAME + "_state__rosidl_typesupport_c_native.dll"),
            root / "unrelated.dll",
        )
        selected = select_candidate_native_libraries(paths)
        self.assertEqual(
            (
                paths[0],
                paths[2],
                paths[3],
            ),
            selected,
        )

    def test_runtime_rmw_capability_uses_explicit_modes_then_legacy_manifest_fields(self) -> None:
        self.assertEqual(
            ("rmw_fastrtps_cpp",),
            _runtime_rmws({"rmwImplementation": "rmw_fastrtps_cpp"}, "humble"),
        )
        self.assertEqual(
            ("rmw_fastrtps_cpp", "rmw_zenoh_cpp"),
            _runtime_rmws(
                {
                    "communicationModes": [
                        {"rmwImplementation": "rmw_zenoh_cpp"},
                        {"rmwImplementation": "rmw_fastrtps_cpp"},
                    ]
                },
                "lyrical",
            ),
        )
        with self.assertRaises(CandidateBuildError):
            _runtime_rmws({"supportedRmwImplementations": []}, "jazzy")

    def _request(self) -> CandidateBuildRequest:
        root = Path("D:/phase181-test")
        return CandidateBuildRequest(
            distro="humble",
            static_interface_package=root / "Packages" / "dev.unity2foxglove.foxrun.ros2.interfaces",
            base_runtime_package=root / "Packages" / "dev.unity2foxglove.ros2forunity.runtime.humble.win64",
            ros2_root=root / "ros2-windows" / "ros2_humble",
            ros2cs_source=root / "ros2cs",
            ros2cs_install=root / "ros2cs" / "install-humble",
            r2fu_source=root / "ros2-for-unity",
            build_root=root / "build",
        )

    def _fixture(self) -> "_Fixture":
        return _Fixture()


class _Fixture:
    def __init__(self) -> None:
        self._temporary = tempfile.TemporaryDirectory()
        self.root = Path(self._temporary.name)
        self.static = self.root / "Packages" / "dev.unity2foxglove.foxrun.ros2.interfaces"
        (self.static / "Ros2Package~" / "msg").mkdir(parents=True)
        (self.static / "Ros2Package~" / "msg" / "State.msg").write_text("string value\n", encoding="utf-8")
        self.digest = compute_static_interface_digest(self.static)
        (self.static / "RuntimeSupport").mkdir()
        (self.static / "RuntimeSupport" / "foxrun-ros2-interface-lock.json").write_text(
            json.dumps(
                {
                    "unityPackageId": "dev.unity2foxglove.foxrun.ros2.interfaces",
                    "rosPackageName": ROS_PACKAGE_NAME,
                    "interfaceRevision": 1,
                    "interfaceDigest": self.digest,
                }
            ),
            encoding="utf-8",
        )
        self.request = CandidateBuildRequest(
            distro="humble",
            static_interface_package=self.static,
            base_runtime_package=self.root / "Packages" / "dev.unity2foxglove.ros2forunity.runtime.humble.win64",
            ros2_root=self.root / "ros2-windows" / "ros2_humble",
            ros2cs_source=self.root / "ros2cs",
            ros2cs_install=self.root / "ros2cs" / "install-humble",
            r2fu_source=self.root / "ros2-for-unity",
            build_root=self.root / "build",
        )

    def __enter__(self) -> "_Fixture":
        return self

    def __exit__(self, exc_type, exc_value, traceback) -> None:
        self._temporary.cleanup()


if __name__ == "__main__":
    unittest.main()
