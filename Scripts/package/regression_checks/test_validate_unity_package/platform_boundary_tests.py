from __future__ import annotations

from .foundation import *


class PackagePlatformBoundaryTests:
    """Exercise platform guards and release-gate wiring."""

    def test_ros2_player_staging_is_atomic_and_matches_player_layout(self) -> None:
        """Build staging must fail for missing metadata and target StreamingAssets."""
        staging = (ROOT / "Packages" / "dev.unity2foxglove.ros2forunity" / "Editor" / "Ros2ForUnityRuntimePlayerStaging.cs").read_text(encoding="utf-8")
        self.assertIn("CopyRequired", staging)
        self.assertNotIn("CopyIfPresent(Path.Combine(source, \"metadata_ros2_for_unity.xml\")", staging)
        self.assertIn("Ros2ForUnityRuntimeSelection.RepositoryPackagesDirectory(project)", staging)
        self.assertNotIn('Path.Combine(project, "Packages", packageName)', staging)
        self.assertIn("StreamingAssets", staging)

        runtime_files = list((ROOT / "Packages").glob("dev.unity2foxglove.ros2forunity.runtime.*.win64/Runtime/Ros2ForUnity/Scripts/ROS2ForUnity.cs"))
        self.assertEqual(3, len(runtime_files))
        for path in runtime_files:
            source = path.read_text(encoding="utf-8")
            self.assertIn("Application.streamingAssetsPath", source)
            self.assertIn(
                'string ros2csMetadataPath = GetRos2ForUnityPath() + separator + "metadata_ros2cs.xml"',
                source,
            )
            self.assertNotIn(
                'string ros2csMetadataPath = GetPluginPath() + separator + "metadata_ros2cs.xml"',
                source,
            )

    def test_project_ros_guards_bootstrap_and_reconcile_windows_only(self) -> None:
        """The first import exposes the bootstrap symbol and the installer removes it off Windows."""
        project = (ROOT / "Unity2Foxglove" / "ProjectSettings" / "ProjectSettings.asset").read_text(encoding="utf-8")
        self.assertIn("Standalone: UNITY2FOXGLOVE_ROS2_FOR_UNITY", project)
        offenders = []
        tracked_assets = [
            ROOT / relative
            for relative in subprocess.run(
                ["git", "ls-files", "-z", "--", "Unity2Foxglove/Assets"],
                cwd=ROOT,
                check=True,
                stdout=subprocess.PIPE,
            ).stdout.decode("utf-8").split("\0")
            if relative.endswith(".cs")
        ]
        for path in tracked_assets:
            lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
            for line_number, line in enumerate(lines, 1):
                if (line.lstrip().startswith("#if") or line.lstrip().startswith("#elif")) and "UNITY2FOXGLOVE_ROS2_FOR_UNITY" in line:
                    if "UNITY_EDITOR_WIN" not in line and "UNITY_STANDALONE_WIN" not in line:
                        offenders.append(f"{path}:{line_number}")
        self.assertEqual([], offenders)

        negative_guards = {
            "Phase179FoxRunRos2NativeSubscribeAcceptance.cs": "#if !(UNITY2FOXGLOVE_ROS2_FOR_UNITY && (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN))",
            "Phase181FoxRunCustomRos2InterfaceAcceptance.cs": "#if !(UNITY2FOXGLOVE_ROS2_FOR_UNITY && UNITY2FOXGLOVE_FOXRUN_CUSTOM_ROS2_INTERFACES && (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN))",
        }
        for name, guard in negative_guards.items():
            matches = [path for path in tracked_assets if path.name == name]
            self.assertEqual(1, len(matches), name)
            self.assertIn(guard, matches[0].read_text(encoding="utf-8"))

        installer = (ROOT / "Packages" / "dev.unity2foxglove.ros2forunity" / "Editor" / "Ros2ForUnityRuntimeDefineInstaller.cs").read_text(encoding="utf-8")
        self.assertIn("activeBuildTarget == BuildTarget.StandaloneWindows64", installer)
        self.assertIn("var enableRuntime = status.HasSelection && isWindowsStandalone;", installer)
        self.assertIn("RemoveSymbol(parts, Ros2ForUnityRuntimeSelection.BaseCompileSymbol)", installer)
        self.assertNotIn("activeBuildTarget == BuildTarget.StandaloneWindows;", installer)

    def test_native_acceptance_sources_have_non_windows_safe_transport_contracts(self) -> None:
        """Native acceptance helpers must keep IDs available without native type references."""
        assets = ROOT / "Unity2Foxglove" / "Assets"
        phase162 = (assets / "Editor" / "Phase162LocalZenohPlaySetup.cs").read_text(encoding="utf-8")
        self.assertTrue(phase162.startswith("#if UNITY2FOXGLOVE_ROS2_FOR_UNITY && UNITY_EDITOR_WIN"))
        self.assertTrue(phase162.rstrip().endswith("#endif"))

        phase181 = (assets / "Scripts" / "ManualAcceptance" / "Phase181FoxRunCustomRos2InterfaceAcceptance.cs").read_text(encoding="utf-8")
        phase181_sample = next((assets / "Samples").rglob("Phase181FoxRunCustomRos2Interface.cs")).read_text(encoding="utf-8")
        phase184 = (assets / "Scripts" / "ManualAcceptance" / "Phase184FoxRunProfileAcceptance.cs").read_text(encoding="utf-8")
        phase184_builder = (assets / "Editor" / "ManualAcceptance" / "Phase184FoxRunProfileAcceptanceBuilder.cs").read_text(encoding="utf-8")

        self.assertIn(
            "#if UNITY2FOXGLOVE_ROS2_FOR_UNITY && (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN)\n"
            "using Unity2Foxglove.Ros2ForUnity.Native;\n"
            "#endif",
            phase181,
        )
        self.assertIn(
            "#if UNITY2FOXGLOVE_ROS2_FOR_UNITY && (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN)\n"
            "using Unity2Foxglove.Ros2ForUnity.Native;\n"
            "#endif",
            phase184,
        )
        for source in (phase181, phase181_sample, phase184, phase184_builder):
            self.assertNotIn("FoxRunRos2TransportProvider.IdValue", source)
        self.assertIn("NativeTransportId", phase181)
        self.assertIn("NativeTransportId", phase181_sample)
        self.assertIn("public const string NativeTransportId", phase184)
        self.assertIn("Phase184FoxRunProfileAcceptance.NativeTransportId", phase184_builder)

    def test_ros2_runtime_assemblies_require_a_windows_symbol(self) -> None:
        """Win64 ROS2 assemblies must not compile in a non-Windows Editor."""
        import json

        runtime_asmdefs = list(
            (ROOT / "Packages").glob(
                "dev.unity2foxglove.ros2forunity.runtime.*.win64/Runtime/Ros2ForUnity/Scripts/*.asmdef"
            )
        )
        self.assertEqual(3, len(runtime_asmdefs))
        for path in runtime_asmdefs:
            data = json.loads(path.read_text(encoding="utf-8"))
            self.assertIn("UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN", data["defineConstraints"])

        native = json.loads(
            (
                ROOT / "Packages" / "dev.unity2foxglove.ros2forunity" / "Runtime" / "Native"
                / "Unity2Foxglove.Ros2ForUnity.Native.asmdef"
            ).read_text(encoding="utf-8")
        )
        self.assertIn("UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN", native["defineConstraints"])

        native_editor = json.loads(
            (
                ROOT / "Packages" / "dev.unity2foxglove.ros2forunity" / "Editor" / "Native"
                / "Unity2Foxglove.Ros2ForUnity.Native.Editor.asmdef"
            ).read_text(encoding="utf-8")
        )
        self.assertIn("UNITY_EDITOR_WIN", native_editor["defineConstraints"])

    def test_ros2_native_sources_and_typesupport_are_windows_guarded(self) -> None:
        """Asmdef platform constraints own package availability while source guards own feature selection."""
        native_root = ROOT / "Packages" / "dev.unity2foxglove.ros2forunity"
        runtime_sources = list((native_root / "Runtime" / "Native").rglob("*.cs"))
        for path in runtime_sources:
            first_guard = next(
                (line.strip() for line in path.read_text(encoding="utf-8", errors="replace").splitlines()
                 if line.strip().startswith("#if") and "UNITY2FOXGLOVE_ROS2_FOR_UNITY" in line),
                "",
            )
            if first_guard:
                self.assertEqual("#if UNITY2FOXGLOVE_ROS2_FOR_UNITY", first_guard, str(path))

        inspector = native_root / "Editor" / "Native" / "FoxRunRos2SubscriptionDiagnosticsInspector.cs"
        self.assertIn(
            "#if UNITY_EDITOR && UNITY2FOXGLOVE_ROS2_FOR_UNITY",
            inspector.read_text(encoding="utf-8"),
        )

        addon_asmdefs = list(
            ROOT.glob("Packages/dev.unity2foxglove.foxrun.ros2.interfaces.typesupport.*.win64/Runtime/FoxRun/Generated/*.asmdef")
        )
        self.assertEqual(3, len(addon_asmdefs))
        for path in addon_asmdefs:
            data = json.loads(path.read_text(encoding="utf-8"))
            self.assertIn("UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN", data["defineConstraints"])

    def test_release_gate_workflow_invokes_all_runtime_validators(self) -> None:
        """The explicit publication lane must run every distro release gate."""
        workflow = ROOT / ".github" / "workflows" / "r2fu-release-gate.yml"
        text = workflow.read_text(encoding="utf-8")
        self.assertIn("R2FU_EXPECTED_COMMIT_SHA: ${{ github.sha }}", text)
        for distro in ("humble", "jazzy", "lyrical"):
            self.assertIn(f"{distro}/validate_r2fu_runtime_package.py --release-gate", text)

    def test_package_check_enforces_release_gate_for_published_candidates(self) -> None:
        """The required package check must not let a published candidate bypass release validation."""
        workflow = (ROOT / ".github" / "workflows" / "package-check.yml").read_text(encoding="utf-8")
        self.assertIn("R2FU_EXPECTED_COMMIT_SHA: ${{ github.event.pull_request.head.sha || github.sha }}", workflow)
        self.assertIn("--release-gate", workflow)
        self.assertIn("redistributionStatus", workflow)
        self.assertIn("distributionLevel", workflow)
        self.assertIn('event["pull_request"]["base"]["sha"]', workflow)
        self.assertIn('event.get("before")', workflow)
        self.assertIn('os.environ["GITHUB_SHA"]', workflow)
        self.assertIn('"git", "diff", "--name-only", "--no-renames", before, after', workflow)
        self.assertIn('"git", "diff-tree", "--root", "--no-commit-id", "--name-only", "-r", "--no-renames", after', workflow)
        self.assertNotIn('"HEAD^1", "HEAD"', workflow)
        self.assertIn("No runtime candidate package changed; release gate not required.", workflow)
        self.assertIn("for distro in candidates:", workflow)
        self.assertNotIn("for distro in distros:\n              subprocess.check_call", workflow)


__all__ = ["PackagePlatformBoundaryTests"]
