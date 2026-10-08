from __future__ import annotations

from .foundation import *


class RuntimeReleaseGateTests:
    """Exercise inventory and release-gate acceptance contracts"""

    def test_inventory_category_counts_must_match_file_entries(self) -> None:
        """A declared native count cannot be satisfied by DLLs from other categories."""
        files = [
            {"path": "Ros2ForUnity/native.dll", "category": "native_libraries"},
            {"path": "Ros2ForUnity/generated.dll", "category": "generated_message_assemblies"},
        ]

        self.assertFalse(
            self.validator.inventory_category_counts_match(
                {"native_libraries": 2},
                files,
            )
        )
        self.assertTrue(
            self.validator.inventory_category_counts_match(
                {"native_libraries": 1, "generated_message_assemblies": 1},
                files,
            )
        )

    def test_ros2cs_plugin_metadata_requires_portable_roots(self) -> None:
        """Both packaged plugin inventories must use a package-relative root."""
        with tempfile.TemporaryDirectory() as temp:
            runtime_root = Path(temp) / "Runtime" / "Ros2ForUnity"
            plugin_root = runtime_root / "Plugins" / "Windows" / "x86_64"
            metadata_files = (
                runtime_root / "Plugins" / "metadata_ros2cs.xml",
                plugin_root / "metadata_ros2cs.xml",
            )
            for path in metadata_files:
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text('<ros2cs><plugins root="D:\\producer\\plugins" /></ros2cs>', encoding="utf-8")
            self.validator.RUNTIME_ROOT = runtime_root
            self.validator.PLUGIN_ROOT = plugin_root
            results = []

            self.validator.check_ros2cs_metadata_portability(results)

            self.assertEqual(2, len(results))
            self.assertTrue(all(not result.ok for result in results))

    def test_release_gate_blocks_candidate_runtime_inventory(self) -> None:
        """Release gate fails while redistributionStatus is candidate_not_published."""
        exit_code = self.validator.main(["--release-gate"])

        self.assertEqual(self.validator.EXIT_FAILURE, exit_code)

    def test_release_gate_requires_fresh_project_acceptance(self) -> None:
        """Published runtime cannot pass while fresh-project acceptance is deferred."""
        self.assertFalse(self.validator.fresh_project_acceptance_passed({"freshProjectAcceptance": {"status": "deferred"}}))
        self.assertTrue(self.validator.fresh_project_acceptance_passed({"freshProjectAcceptance": {"status": "passed"}}))
        self.assertTrue(self.validator.fresh_project_acceptance_passed({"freshProjectAcceptance": "passed"}))

    def test_release_gate_requires_exact_candidate_acceptance_binding(self) -> None:
        """A passed acceptance record must identify the published candidate lineage."""
        manifest = {
            "runtimeId": "r2fu-lyrical-win64",
            "packageName": "dev.unity2foxglove.ros2forunity.runtime.lyrical.win64",
            "packageVersion": "0.1.0-preview.1",
            "rosDistro": "lyrical",
            "artifactName": "Ros2ForUnity_lyrical_standalone_windows_x86_64.zip",
            "artifactSha256": "a" * 64,
            "artifactSize": 123,
            "inventoryFileCount": 7,
            "unityVersion": "6000.3.14f1",
            "freshProjectAcceptance": {
                "status": "passed",
                "runtimeId": "r2fu-lyrical-win64",
                "packageName": "dev.unity2foxglove.ros2forunity.runtime.lyrical.win64",
                "packageVersion": "0.1.0-preview.1",
                "rosDistro": "lyrical",
                "artifactName": "Ros2ForUnity_lyrical_standalone_windows_x86_64.zip",
                "artifactSha256": "a" * 64,
                "artifactSize": 123,
                "inventoryFileCount": 7,
                "unityVersion": "6000.3.14f1",
                "commitSha": "b" * 40,
                "workflowRunId": 42,
            },
        }
        self.assertTrue(self.validator.fresh_project_acceptance_binding_matches_manifest(manifest))
        self.assertTrue(self.validator.fresh_project_acceptance_record_is_valid(manifest))
        with mock.patch.dict(self.validator.os.environ, {"R2FU_EXPECTED_COMMIT_SHA": "c" * 40}, clear=False):
            self.assertFalse(self.validator.fresh_project_acceptance_binding_matches_manifest(manifest))
        with mock.patch.dict(self.validator.os.environ, {"R2FU_EXPECTED_COMMIT_SHA": "b" * 40}, clear=False):
            with mock.patch.object(self.validator, "acceptance_commit_is_ancestor_and_package_unchanged", return_value=True):
                self.assertTrue(self.validator.fresh_project_acceptance_binding_matches_manifest(manifest))
        manifest.pop("unityVersion")
        self.assertFalse(self.validator.fresh_project_acceptance_binding_matches_manifest(manifest))
        manifest["unityVersion"] = "6000.3.14f1"
        manifest["freshProjectAcceptance"]["artifactSha256"] = "c" * 64
        self.assertFalse(self.validator.fresh_project_acceptance_binding_matches_manifest(manifest))
        manifest["freshProjectAcceptance"]["artifactSha256"] = manifest["artifactSha256"]
        manifest["freshProjectAcceptance"]["inventoryFileCount"] = 8
        self.assertFalse(self.validator.fresh_project_acceptance_binding_matches_manifest(manifest))
        self.assertFalse(self.validator.fresh_project_acceptance_record_is_valid(manifest))

    def test_release_gate_accepts_unchanged_runtime_from_ancestor_acceptance(self) -> None:
        """A published package may reuse acceptance from an ancestor while its payload is unchanged."""
        manifest = {
            "runtimeId": "r2fu-lyrical-win64",
            "packageName": "dev.unity2foxglove.ros2forunity.runtime.lyrical.win64",
            "packageVersion": "0.1.0-preview.1",
            "rosDistro": "lyrical",
            "artifactName": "Ros2ForUnity_lyrical_standalone_windows_x86_64.zip",
            "artifactSha256": "a" * 64,
            "artifactSize": 123,
            "inventoryFileCount": 7,
            "unityVersion": "6000.3.14f1",
            "freshProjectAcceptance": {
                "status": "passed",
                "runtimeId": "r2fu-lyrical-win64",
                "packageName": "dev.unity2foxglove.ros2forunity.runtime.lyrical.win64",
                "packageVersion": "0.1.0-preview.1",
                "rosDistro": "lyrical",
                "artifactName": "Ros2ForUnity_lyrical_standalone_windows_x86_64.zip",
                "artifactSha256": "a" * 64,
                "artifactSize": 123,
                "inventoryFileCount": 7,
                "unityVersion": "6000.3.14f1",
                "commitSha": "b" * 40,
                "workflowRunId": 42,
            },
        }
        with mock.patch.dict(self.validator.os.environ, {"R2FU_EXPECTED_COMMIT_SHA": "c" * 40}, clear=False):
            with mock.patch.object(self.validator, "acceptance_commit_is_ancestor_and_package_unchanged", return_value=True):
                self.assertTrue(self.validator.fresh_project_acceptance_binding_matches_manifest(manifest))
            with mock.patch.object(self.validator, "acceptance_commit_is_ancestor_and_package_unchanged", return_value=False):
                self.assertFalse(self.validator.fresh_project_acceptance_binding_matches_manifest(manifest))

    def test_ancestor_acceptance_helper_checks_real_git_history_and_package_delta(self) -> None:
        """The ancestor acceptance rule must reject a changed runtime package."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            package = root / "Packages" / "runtime"
            package.mkdir(parents=True)

            def git(*args: str) -> str:
                """Run a git command in the temporary repository."""
                result = subprocess.run(
                    ["git", *args],
                    cwd=root,
                    check=True,
                    text=True,
                    capture_output=True,
                )
                return result.stdout.strip()

            git("init", "-q")
            git("config", "user.name", "validator-test")
            git("config", "user.email", "validator-test@example.invalid")
            (package / "payload.txt").write_text("accepted\n", encoding="utf-8")
            (package / "RuntimeSupport").mkdir()
            (package / "RuntimeSupport/runtime-manifest.json").write_text(
                '{"payloadVersion":1}\n', encoding="utf-8"
            )
            git("add", ".")
            git("commit", "-q", "-m", "acceptance")
            acceptance_sha = git("rev-parse", "HEAD")
            (root / "README.md").write_text("unrelated\n", encoding="utf-8")
            git("add", ".")
            git("commit", "-q", "-m", "unrelated")
            unchanged_candidate_sha = git("rev-parse", "HEAD")

            self.validator.ROOT = root
            self.validator.PACKAGE = package
            self.assertTrue(
                self.validator.acceptance_commit_is_ancestor_and_package_unchanged(
                    acceptance_sha,
                    unchanged_candidate_sha,
                )
            )

            (package / "RuntimeSupport/runtime-manifest.json").write_text(
                '{"payloadVersion":1,"freshProjectAcceptance":{"status":"passed"}}\n',
                encoding="utf-8",
            )
            git("add", ".")
            git("commit", "-q", "-m", "record-acceptance")
            acceptance_record_candidate_sha = git("rev-parse", "HEAD")
            self.assertTrue(
                self.validator.acceptance_commit_is_ancestor_and_package_unchanged(
                    acceptance_sha,
                    acceptance_record_candidate_sha,
                )
            )

            (package / "payload.txt").write_text("changed\n", encoding="utf-8")
            git("add", ".")
            git("commit", "-q", "-m", "runtime-change")
            changed_candidate_sha = git("rev-parse", "HEAD")
            self.assertFalse(
                self.validator.acceptance_commit_is_ancestor_and_package_unchanged(
                    acceptance_sha,
                    changed_candidate_sha,
                )
            )


__all__ = ["RuntimeReleaseGateTests"]
