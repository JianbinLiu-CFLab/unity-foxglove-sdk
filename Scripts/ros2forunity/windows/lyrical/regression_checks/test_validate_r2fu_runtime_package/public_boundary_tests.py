from __future__ import annotations

from .foundation import *


class RuntimePublicBoundaryTests:
    """Exercise artifact hash and public-boundary documentation checks"""

    def test_expected_artifact_hash_is_full_sha256(self) -> None:
        """Pinned artifact hash is not accidentally truncated."""
        self.assertEqual(64, len(self.validator.EXPECTED_ARTIFACT_SHA256))

    def test_core_runtime_missing_reports_failed_boundary_check(self) -> None:
        """A missing core Runtime folder must fail instead of silently passing."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            core = root / "core"
            adapter = root / "adapter"
            core.mkdir()
            adapter.mkdir()
            (core / "package.json").write_text("{}", encoding="utf-8")
            (adapter / "package.json").write_text("{}", encoding="utf-8")
            self.validator.CORE_PACKAGE = core
            self.validator.ADAPTER_PACKAGE = adapter
            results = []

            self.validator.check_package_boundaries(results)

        by_name = {result.name: result for result in results}
        self.assertFalse(by_name["core Runtime folder exists"].ok)
        self.assertFalse(by_name["core SDK runtime remains ROS2 For Unity free"].ok)

    def test_unity_editor_using_guard_removes_guarded_occurrence(self) -> None:
        """The UnityEditor using check should inspect the post-substitution text."""
        self.assertTrue(self.validator.guarded_unity_editor_using("#if UNITY_EDITOR\nusing UnityEditor;\n#endif\n"))
        self.assertFalse(
            self.validator.guarded_unity_editor_using(
                "#if UNITY_EDITOR\nusing UnityEditor;\n#endif\nusing UnityEditor;\n"
            )
        )

    def test_public_docs_accept_specific_runtime_package_token(self) -> None:
        """One-runtime docs check should not depend on a literal runtime.* token."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp)
            readme = package / "README.md"
            notices = package / "THIRD_PARTY_NOTICES.md"
            package_json = package / "package.json"
            manifest = package / "runtime-manifest.json"
            artifact_sha = "b" * 64

            readme.write_text(
                "runtime.lyrical.win64 adapter combined Unity2Foxglove workflow\n"
                "Install only one dev.unity2foxglove.ros2forunity.runtime.lyrical.win64 package\n"
                "WSL2 NAT diagnostic-only Windows Defender Firewall\n"
                + artifact_sha,
                encoding="utf-8",
            )
            notices.write_text(artifact_sha, encoding="utf-8")
            package_json.write_text("{}", encoding="utf-8")
            manifest.write_text(f'{{"artifactSha256":"{artifact_sha}"}}', encoding="utf-8")
            self.validator.PACKAGE = package
            self.validator.PUBLIC_DOCS = (readme, notices, package_json, manifest)
            self.validator.MANIFEST = manifest
            results = []

            self.validator.check_public_docs(results)

        one_runtime = [result for result in results if result.name == "README documents one-runtime policy"]
        self.assertEqual(1, len(one_runtime))
        self.assertTrue(one_runtime[0].ok)


__all__ = ["RuntimePublicBoundaryTests"]
