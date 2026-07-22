"""Public regression checks for the Phase181 source-package digest contract."""

import json
import unittest
from pathlib import Path

from Scripts.test_support.phase181_scratch import temporary_directory

from Scripts.ros2forunity.interfaces.interface_digest import (
    DigestInput,
    INTERFACE_SCHEMA_VERSION,
    compute,
    normalize_relative_path,
    verify_package,
)


class InterfaceDigestTests(unittest.TestCase):
    """Represent InterfaceDigestTests."""
    def test_cross_host_vector_is_stable(self) -> None:
        """Verify cross host vector is stable."""
        value = compute(
            INTERFACE_SCHEMA_VERSION,
            (
                DigestInput("Ros2Package~/msg/Example.msg", "int32 count\r\n"),
                DigestInput("package.json", '{"name":"example"}\n'),
            ),
        )
        self.assertEqual(
            "518773aa5ba89143600cc1111d371b19bfa54d11bd4874ff3e154e4048de9bdd",
            value,
        )
        self.assertEqual(
            value,
            compute(
                INTERFACE_SCHEMA_VERSION,
                (
                    DigestInput("package.json", '{"name":"example"}\r\n'),
                    DigestInput("Ros2Package~\\msg\\Example.msg", "int32 count\n"),
                ),
            ),
        )

    def test_single_byte_and_duplicate_path_are_not_ignored(self) -> None:
        """Verify single byte and duplicate path are not ignored."""
        baseline = compute(
            INTERFACE_SCHEMA_VERSION,
            (DigestInput("msg/Example.msg", "int32 count\n"),),
        )
        self.assertNotEqual(
            baseline,
            compute(
                INTERFACE_SCHEMA_VERSION,
                (DigestInput("msg/Example.msg", "int32 count\n#\n"),),
            ),
        )
        with self.assertRaises(ValueError):
            compute(
                INTERFACE_SCHEMA_VERSION,
                (
                    DigestInput("msg/Example.msg", "a\n"),
                    DigestInput("msg\\Example.msg", "b\n"),
                ),
            )

    def test_path_and_version_rejections_are_explicit(self) -> None:
        """Verify path and version rejections are explicit."""
        with self.assertRaises(ValueError):
            normalize_relative_path("../escape.msg")
        with self.assertRaises(ValueError):
            compute(99, (DigestInput("msg/Example.msg", "a\n"),))

    def test_checked_in_source_package_matches_its_lock(self) -> None:
        """Verify checked in source package matches its lock."""
        root = Path(__file__).resolve().parents[4]
        package_root = root / "Packages" / "dev.unity2foxglove.foxrun.ros2.interfaces"
        self.assertEqual(
            "120864853239fae290b5199cd02dbf02f107299bccd8972b06d8cf59fc7594fd",
            verify_package(package_root),
        )

    def test_nested_message_bytes_are_part_of_the_source_package_digest(self) -> None:
        """Verify nested message bytes are part of the source package digest."""
        with temporary_directory("interface-digest-") as temporary_root:
            root = Path(temporary_root)
            files = {
                "package.json": "{}\n",
                "README.md": "source package\n",
                "RuntimeSupport/foxrun-ros2-interface-settings.json": "{}\n",
                "Ros2Package~/package.xml": "<package/>\n",
                "Ros2Package~/CMakeLists.txt": "cmake_minimum_required(VERSION 3.8)\n",
                "Ros2Package~/msg/Payload.msg": "int32 value\n",
                "Ros2Package~/msg/PayloadEnvelope.msg": "Payload payload\n",
                "Ros2Package~/msg/Nested.msg": "string label\n",
            }
            for relative_path, content in files.items():
                target = root / relative_path
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_text(content, encoding="utf-8", newline="\n")

            digest = compute(
                INTERFACE_SCHEMA_VERSION,
                tuple(DigestInput(relative_path, content) for relative_path, content in files.items()),
            )
            lock_path = root / "RuntimeSupport" / "foxrun-ros2-interface-lock.json"
            lock_path.write_text(
                json.dumps(
                    {
                        "interfaceSchemaVersion": INTERFACE_SCHEMA_VERSION,
                        "interfaceDigest": digest,
                        "contracts": [
                            {
                                "payloadMessageName": "Payload",
                                "envelopeMessageName": "PayloadEnvelope",
                            }
                        ],
                    },
                ),
                encoding="utf-8",
                newline="\n",
            )

            self.assertEqual(digest, verify_package(root))
            (root / "Ros2Package~" / "msg" / "Nested.msg").write_text(
                "string changed\n",
                encoding="utf-8",
                newline="\n",
            )
            with self.assertRaises(ValueError):
                verify_package(root)


if __name__ == "__main__":
    unittest.main()
