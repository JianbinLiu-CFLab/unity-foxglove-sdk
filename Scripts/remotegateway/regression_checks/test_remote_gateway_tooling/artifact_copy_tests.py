from __future__ import annotations

from .foundation import *


class RemoteGatewayArtifactCopyTests:
    """Exercise artifact allow-lists and committed manifest handling"""

    def test_committed_manifest_records_pinned_source_and_provenance(self) -> None:
        """The tracked trust anchor records the pinned source inputs."""
        manifest_path = ROOT / (
            "Packages/dev.unity2foxglove.remotegateway.win64/Runtime/"
            "Plugins/Windows/x86_64/foxglove-gateway-native-artifact.json"
        )
        payload = json.loads(manifest_path.read_text(encoding="utf-8"))

        self.assertEqual(self.build.EXPECTED_FOXGLOVE_COMMIT, payload["sourceCommit"])
        self.assertEqual(self.build.EXPECTED_FOXGLOVE_HEADER_SHA256, payload["cHeaderSha256"])
        self.assertEqual(self.build.EXPECTED_FOXGLOVE_CARGO_LOCK_SHA256, payload["cargoLockSha256"])
        self.assertEqual("present", payload["environment"]["cargoLock"])

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

    def test_copy_keeps_committed_manifest_unless_explicitly_updated(self) -> None:
        """A local build must not silently replace the package trust anchor."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            target = root / "target"
            release = target / "release"
            package = root / "package"
            release.mkdir(parents=True)
            package.mkdir()
            for name in self.build.APPROVED_ARTIFACTS:
                (release / name).write_bytes(name.encode("utf-8"))

            committed_manifest = b'{"sha256":"committed"}\n'
            generated_manifest = root / self.build.PACKAGE_MANIFEST_NAME
            generated_manifest.write_bytes(b'{"sha256":"generated"}\n')
            package_manifest = package / generated_manifest.name
            package_manifest.write_bytes(committed_manifest)

            with mock.patch.object(self.build, "PACKAGE_PLUGIN_DIR", package):
                self.build.copy_approved_artifacts(
                    target,
                    generated_manifest,
                    self.build.APPROVED_ARTIFACTS,
                )
                self.assertEqual(committed_manifest, package_manifest.read_bytes())

                self.build.copy_approved_artifacts(
                    target,
                    generated_manifest,
                    self.build.APPROVED_ARTIFACTS,
                    copy_manifest=True,
                )
                self.assertEqual(generated_manifest.read_bytes(), package_manifest.read_bytes())

    def test_manifest_records_the_static_crt_cxx_flag(self) -> None:
        """Native provenance must include both C and C++ CRT controls."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source, header = self._create_source_fixture(root)
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
                    environment,
                    ("foxglove.dll",),
                )
            payload = json.loads(path.read_text(encoding="utf-8"))

        self.assertEqual("/MT", payload["cxxflags"])

    def test_manifest_rejects_missing_selected_artifact(self) -> None:
        """Selected import libraries and symbols cannot disappear silently."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source, header = self._create_source_fixture(root)
            release = root / "target/release"
            release.mkdir(parents=True)
            (release / "foxglove.dll").write_bytes(b"dll")
            environment = {
                "RUSTFLAGS": "-C target-feature=+crt-static",
                "CFLAGS_x86_64_pc_windows_msvc": "/MT",
                "CXXFLAGS_x86_64_pc_windows_msvc": "/MT",
                "AWS_LC_SYS_PREBUILT_NASM": "1",
                "CARGO_TARGET_DIR": str(root / "target"),
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
            ), self.assertRaisesRegex(FileNotFoundError, "Missing selected artifact"):
                self.build.write_manifest(
                    root / "target",
                    environment,
                    self.build.APPROVED_ARTIFACTS,
                )

    def test_relative_target_dir_uses_repository_base(self) -> None:
        """Cargo and manifest paths must resolve relative targets identically."""
        resolved = self.build.resolve_target_dir("build/native-target")
        self.assertEqual(self.build.ROOT / "build/native-target", resolved)
        args = SimpleNamespace(libclang_path=None, target_dir="build/native-target")
        environment = self.build.build_environment(args)
        self.assertEqual(str(resolved), environment["CARGO_TARGET_DIR"])

    def test_target_dir_rejects_public_source_trees(self) -> None:
        """Cargo cannot write build products into package or source roots."""
        with self.assertRaisesRegex(ValueError, "inside a source tree"):
            self.build.validate_target_dir(self.build.ROOT / "Packages" / "bad-target")
        with self.assertRaisesRegex(ValueError, "inside a source tree"):
            self.build.validate_target_dir(self.build.ROOT / "third-party" / "bad-target")

    def test_stale_selected_artifact_is_rejected(self) -> None:
        """A successful no-op Cargo build cannot publish an older DLL."""
        with tempfile.TemporaryDirectory() as temp:
            release = Path(temp) / "release"
            release.mkdir(parents=True)
            artifact = release / "foxglove.dll"
            artifact.write_bytes(b"stale")
            with self.assertRaisesRegex(RuntimeError, "Stale selected artifact"):
                self.build.ensure_fresh_artifacts(Path(temp), ("foxglove.dll",), artifact.stat().st_mtime + 1)

    def test_copy_rejects_artifact_bytes_diverging_from_manifest(self) -> None:
        """Promotion verifies source and destination bytes against the manifest."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            release = root / "target/release"
            package = root / "package"
            release.mkdir(parents=True)
            package.mkdir()
            (release / "foxglove.dll").write_bytes(b"changed")
            (release / "foxglove.dll.lib").write_bytes(b"lib")
            manifest = root / "manifest.json"
            manifest.write_text(json.dumps({"sha256": hashlib.sha256(b"original").hexdigest(), "artifacts": {"foxglove.dll": {"sha256": hashlib.sha256(b"original").hexdigest()}}}), encoding="utf-8")
            with mock.patch.object(self.build, "PACKAGE_PLUGIN_DIR", package):
                with self.assertRaisesRegex(RuntimeError, "changed after manifest hashing"):
                    self.build.copy_approved_artifacts(root / "target", manifest, self.build.APPROVED_ARTIFACTS)


__all__ = ["RemoteGatewayArtifactCopyTests"]
