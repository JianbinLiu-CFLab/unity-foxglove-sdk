"""Regression checks for Phase181 validated add-on sync discipline."""

from __future__ import annotations

import json
import unittest
from pathlib import Path

from Scripts.test_support.phase181_scratch import temporary_directory

from Scripts.ros2forunity.interfaces.sync_foxrun_custom_typesupport_addon import (
    AddonSyncError,
    AddonSyncRequest,
    allowed_inventory_paths,
    sync_addon,
    verify_sync_ready,
)
from Scripts.ros2forunity.interfaces.foxrun_custom_typesupport_common import (
    AddonValidationRequest,
    compute_static_interface_digest,
)
from Scripts.ros2forunity.interfaces.build_foxrun_custom_typesupport_addon import (
    MANAGED_ASSEMBLY_FILE,
)


class CustomTypesupportSyncTests(unittest.TestCase):
    """Represent CustomTypesupportSyncTests."""
    def test_sync_refuses_candidate_without_validation_proof(self) -> None:
        """Verify sync refuses candidate without validation proof."""
        with self._fixture() as fixture:
            with self.assertRaises(AddonSyncError):
                verify_sync_ready(fixture.request, validator=lambda _request: None)

    def test_sync_accepts_only_validated_inventory_paths(self) -> None:
        """Verify sync accepts only validated inventory paths."""
        with self._fixture(validated=True) as fixture:
            paths = allowed_inventory_paths(fixture.request)
            self.assertEqual(
                (
                    "LICENSE",
                    "package.json",
                    "Runtime/Ros2ForUnity/Plugins/" + MANAGED_ASSEMBLY_FILE,
                    "Runtime/Ros2ForUnity/Plugins/Windows/x86_64/custom.dll",
                    "RuntimeSupport/typesupport-inventory.json",
                    "RuntimeSupport/typesupport-manifest.json",
                ),
                paths,
            )

    def test_sync_rejects_unexpected_candidate_payload(self) -> None:
        """Verify sync rejects unexpected candidate payload."""
        with self._fixture(validated=True) as fixture:
            (fixture.candidate / "unexpected.dll").write_bytes(b"unexpected")
            with self.assertRaises(AddonSyncError):
                verify_sync_ready(fixture.request, validator=lambda _request: None)

    def test_sync_replaces_only_the_legacy_platform_managed_assembly(self) -> None:
        """Verify an old layout cannot block the repaired managed assembly layout."""
        with self._fixture(validated=True) as fixture:
            legacy = (
                fixture.target
                / "Runtime/Ros2ForUnity/Plugins/Windows/x86_64"
                / MANAGED_ASSEMBLY_FILE
            )
            legacy.parent.mkdir(parents=True)
            legacy.write_bytes(b"legacy")
            legacy.with_name(legacy.name + ".meta").write_text("legacy meta\n", encoding="utf-8")
            generated_meta = fixture.target / "Runtime.meta"
            generated_meta.write_text("unity generated meta\n", encoding="utf-8")

            sync_addon(fixture.request, validator=lambda _request: None)

            self.assertFalse(legacy.exists())
            self.assertFalse(legacy.with_name(legacy.name + ".meta").exists())
            self.assertTrue(
                (
                    fixture.target
                    / "Runtime/Ros2ForUnity/Plugins"
                    / MANAGED_ASSEMBLY_FILE
                ).is_file()
            )
            self.assertTrue(generated_meta.is_file())

    def _fixture(self, *, validated: bool = False) -> "_Fixture":
        """Implement the internal fixture step."""
        return _Fixture(validated=validated)


class _Fixture:
    """Represent Fixture."""
    def __init__(self, *, validated: bool = False) -> None:
        """Initialize this object."""
        self._temporary = temporary_directory("typesupport-sync-")
        self.root = Path(self._temporary.name)
        self.static = self.root / "Packages/dev.unity2foxglove.foxrun.ros2.interfaces"
        self.base = self.root / "Packages/dev.unity2foxglove.ros2forunity.runtime.humble.win64"
        self.candidate = self.root / "build/phase181/humble/candidate/package"
        self.target = self.root / "Packages/dev.unity2foxglove.foxrun.ros2.interfaces.typesupport.humble.win64"
        (self.static / "RuntimeSupport").mkdir(parents=True)
        (self.static / "source.txt").write_text("locked\n", encoding="utf-8")
        digest = compute_static_interface_digest(self.static)
        (self.static / "RuntimeSupport/foxrun-ros2-interface-lock.json").write_text(
            json.dumps(
                {
                    "unityPackageId": "dev.unity2foxglove.foxrun.ros2.interfaces",
                    "rosPackageName": "unity2foxglove_foxrun_interfaces_v1",
                    "interfaceRevision": 1,
                    "interfaceDigest": digest,
                }
            ),
            encoding="utf-8",
        )
        (self.base / "RuntimeSupport").mkdir(parents=True)
        (self.base / "RuntimeSupport/runtime-manifest.json").write_text("{}", encoding="utf-8")
        self.candidate.mkdir(parents=True)
        expected = (
            "LICENSE",
            "Runtime/Ros2ForUnity/Plugins/" + MANAGED_ASSEMBLY_FILE,
            "Runtime/Ros2ForUnity/Plugins/Windows/x86_64/custom.dll",
            "RuntimeSupport/typesupport-manifest.json",
            "package.json",
        )
        for relative in expected:
            path = self.candidate / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(relative, encoding="utf-8")
        (self.candidate / "RuntimeSupport/typesupport-inventory.json").write_text(
            json.dumps({"schemaVersion": 1, "entries": [{"path": item} for item in expected]}),
            encoding="utf-8",
        )
        if validated:
            evidence = self.root / "build/phase181/humble/candidate/e"
            evidence.mkdir(parents=True)
            (evidence / "candidate-validation.json").write_text(
                json.dumps({"schemaVersion": 1, "distro": "humble", "validated": True}),
                encoding="utf-8",
            )
        self.request = AddonSyncRequest(
            distro="humble",
            candidate_package=self.candidate,
            target_package=self.target,
            validation_request=AddonValidationRequest(
                distro="humble",
                addon_package=self.candidate,
                static_interface_package=self.static,
                base_runtime_package=self.base,
            ),
        )

    def __enter__(self) -> "_Fixture":
        """Enter this fixture scope."""
        return self

    def __exit__(self, exc_type, exc_value, traceback) -> None:
        """Release this fixture scope."""
        self._temporary.cleanup()


if __name__ == "__main__":
    unittest.main()
