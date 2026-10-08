from __future__ import annotations

from .foundation import *


class RemoteGatewayManifestValidationTests:
    """Exercise manifest edge cases and run-directory identity"""

    def test_manifest_validator_rejects_edge_shaped_inputs(self) -> None:
        """The validator rejects type, length, truncation, and filesystem near-misses."""
        payload = b"phase187-d05-manifest-edge"
        actual_hash = hashlib.sha256(payload).hexdigest()

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            dll = root / "foxglove.dll"
            dll.write_bytes(payload)
            manifest = root / "foxglove-gateway-native-artifact.json"

            def validate(declared: object, expected: str) -> None:
                """Write one manifest variant and assert that validation rejects it."""
                manifest.write_text(json.dumps(declared), encoding="utf-8")
                with mock.patch.object(self.acceptance, "ROOT", root), mock.patch.object(
                    self.acceptance,
                    "PLUGIN_DIR",
                    root,
                ):
                    with self.assertRaisesRegex(SystemExit, expected):
                        self.acceptance.ensure_native_artifact()

            base = {"artifact": dll.name, "sha256": actual_hash, "sizeBytes": len(payload)}
            validate({**base, "sha256": actual_hash[:-1]}, "sha256")
            validate({**base, "sha256": "z" * 64}, "sha256")
            near_miss_digit = "0" if actual_hash[8] != "0" else "1"
            near_miss = actual_hash[:8] + near_miss_digit + actual_hash[9:]
            validate({**base, "sha256": near_miss}, "sha256 does not match")
            validate({**base, "sizeBytes": len(payload) - 1}, "sizeBytes")
            validate({**base, "sizeBytes": True}, "sizeBytes")
            validate({**base, "sizeBytes": -1}, "sizeBytes")

            manifest.write_text("[1]", encoding="utf-8")
            with mock.patch.object(self.acceptance, "ROOT", root), mock.patch.object(
                self.acceptance,
                "PLUGIN_DIR",
                root,
            ):
                with self.assertRaisesRegex(SystemExit, "JSON object"):
                    self.acceptance.ensure_native_artifact()

            manifest.write_text("{", encoding="utf-8")
            with mock.patch.object(self.acceptance, "ROOT", root), mock.patch.object(
                self.acceptance,
                "PLUGIN_DIR",
                root,
            ):
                with self.assertRaisesRegex(SystemExit, "valid JSON"):
                    self.acceptance.ensure_native_artifact()

            manifest.unlink()
            manifest.mkdir()
            with mock.patch.object(self.acceptance, "ROOT", root), mock.patch.object(
                self.acceptance,
                "PLUGIN_DIR",
                root,
            ):
                with self.assertRaisesRegex(SystemExit, "missing"):
                    self.acceptance.ensure_native_artifact()

            manifest.rmdir()
            dll.unlink()
            dll.mkdir()
            manifest.write_text(json.dumps(base), encoding="utf-8")
            with mock.patch.object(self.acceptance, "ROOT", root), mock.patch.object(
                self.acceptance,
                "PLUGIN_DIR",
                root,
            ):
                with self.assertRaisesRegex(SystemExit, "missing"):
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


__all__ = ["RemoteGatewayManifestValidationTests"]
