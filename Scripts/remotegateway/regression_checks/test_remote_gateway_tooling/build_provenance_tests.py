from __future__ import annotations

from .foundation import *


class RemoteGatewayBuildProvenanceTests:
    """Exercise pinned build target and source provenance"""

    def test_build_pins_x64_target_and_manifest_provenance(self) -> None:
        """Native output must identify the explicit Cargo target and inputs."""
        args = SimpleNamespace(libclang_path=None, target_dir="phase187-target")
        with mock.patch.dict(os.environ, {"RUSTUP_TOOLCHAIN": "stable-msvc"}, clear=False):
            environment = self.build.build_environment(args)
        self.assertEqual("x86_64-pc-windows-msvc", environment["CARGO_BUILD_TARGET"])

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source, header = self._create_source_fixture(root)
            release = root / "target/release"
            release.mkdir(parents=True)
            (release / "foxglove.dll").write_bytes(b"dll")
            staging = root / "staging"
            manifest_environment = {
                "RUSTFLAGS": "-C target-feature=+crt-static",
                "CFLAGS_x86_64_pc_windows_msvc": "/MT",
                "CXXFLAGS_x86_64_pc_windows_msvc": "/MT",
                "AWS_LC_SYS_PREBUILT_NASM": "1",
                "CARGO_TARGET_DIR": str(root / "target"),
                "CARGO_BUILD_TARGET": "x86_64-pc-windows-msvc",
                "RUSTUP_TOOLCHAIN": "stable-msvc",
            }
            with mock.patch.object(
                self.build,
                "foxglove_source_revision",
                return_value=self.build.EXPECTED_FOXGLOVE_COMMIT,
            ), mock.patch.object(
                self.build,
                "sha256_pinned_source",
                side_effect=[
                    self.build.EXPECTED_FOXGLOVE_HEADER_SHA256,
                    self.build.EXPECTED_FOXGLOVE_CARGO_LOCK_SHA256,
                ],
            ), mock.patch.multiple(
                self.build,
                FOXGLOVE_ROOT=source,
                FOXGLOVE_HEADER=header,
            ), mock.patch.object(self.build, "STAGING", staging):
                path = self.build.write_manifest(
                    root / "target",
                    manifest_environment,
                    ("foxglove.dll",),
                )
            payload = json.loads(path.read_text(encoding="utf-8"))
        self.assertEqual("x86_64-pc-windows-msvc", payload["target"])
        self.assertEqual("stable-msvc", payload["environment"]["RUSTUP_TOOLCHAIN"])
        self.assertEqual("present", payload["environment"]["cargoLock"])
        self.assertEqual(self.build.EXPECTED_FOXGLOVE_COMMIT, payload["sourceCommit"])
        self.assertEqual(64, len(payload["cHeaderSha256"]))
        self.assertEqual(64, len(payload["cargoLockSha256"]))

    def test_build_rejects_an_unpinned_foxglove_revision(self) -> None:
        """Native builds must not consume an ABI-incompatible SDK checkout."""
        with mock.patch.object(
            self.build.subprocess,
            "run",
            return_value=SimpleNamespace(stdout="deadbeef\n"),
        ):
            with self.assertRaisesRegex(RuntimeError, "revision mismatch"):
                self.build.foxglove_source_revision()


__all__ = ["RemoteGatewayBuildProvenanceTests"]
