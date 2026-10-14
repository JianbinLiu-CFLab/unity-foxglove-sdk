from __future__ import annotations

from .foundation import *


class RemoteGatewaySkipBuildTests:
    """Exercise skip-build trust-anchor identity checks"""

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

    def test_skip_build_rejects_manifest_that_is_not_the_committed_trust_anchor(self) -> None:
        """Skip-build validation must fail closed when the tracked manifest is dirty."""
        payload = b"phase187-d05-004-native-trust-anchor"
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            dll = root / "foxglove.dll"
            dll.write_bytes(payload)
            manifest = root / "foxglove-gateway-native-artifact.json"
            manifest.write_text(
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
            ), mock.patch.object(
                self.acceptance.subprocess,
                "run",
                return_value=SimpleNamespace(
                    returncode=0,
                    stdout=b'{"committed":true}\n',
                    stderr=b"",
                ),
            ):
                with self.assertRaisesRegex(SystemExit, "committed trust anchor"):
                    self.acceptance.ensure_native_artifact(require_committed=True)

    def test_skip_build_accepts_manifest_matching_the_committed_trust_anchor(self) -> None:
        """A clean checkout can validate the package DLL against its committed manifest."""
        payload = b"phase187-d05-004-native-trust-anchor-valid"
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            dll = root / "foxglove.dll"
            dll.write_bytes(payload)
            manifest = root / "foxglove-gateway-native-artifact.json"
            manifest.write_text(
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
            ), mock.patch.object(
                self.acceptance.subprocess,
                "run",
                return_value=SimpleNamespace(
                    returncode=0,
                    stdout=manifest.read_bytes(),
                    stderr=b"",
                ),
            ) as run:
                self.acceptance.ensure_native_artifact(require_committed=True)

            command = run.call_args.args[0]
            self.assertEqual("git", command[0])
            self.assertIn("show", command)


__all__ = ["RemoteGatewaySkipBuildTests"]
