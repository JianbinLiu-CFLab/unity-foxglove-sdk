from __future__ import annotations
from .foundation import *
class Phase181CustomTypesupportRefreshTests(unittest.TestCase):
    """Keep drift detection and the bounded build/sync/validate sequence explicit."""

    def test_inspection_reports_runtime_manifest_drift_without_a_build(self) -> None:
        """A changed base runtime manifest marks only that add-on as stale."""
        with self._fixture() as fixture:
            fixture.write_addon(runtime_manifest_sha="0" * 64)

            state = inspect_addon_state(fixture.root, "humble")

            self.assertFalse(state.current)
            self.assertEqual(("runtime-manifest",), state.reasons)

    def test_current_addon_runs_only_the_final_validator(self) -> None:
        """A current package avoids an unnecessary native rebuild and sync."""
        with self._fixture() as fixture:
            fixture.write_addon(runtime_manifest_sha=fixture.runtime_manifest_sha)
            commands: list[tuple[str, ...]] = []

            result = run_refresh(
                AddonRefreshRequest(root=fixture.root, distros=("humble",), apply=False),
                runner=self._recording_runner(commands),
            )

            self.assertTrue(result[0].current)
            self.assertEqual(1, len(commands))
            self.assertIn("validate_foxrun_custom_typesupport_addon.py", commands[0][1])

    def test_apply_rebuilds_and_syncs_only_the_stale_distro(self) -> None:
        """An apply run orders build, verified sync, then validation for a stale package."""
        with self._fixture() as fixture:
            fixture.write_addon(runtime_manifest_sha="0" * 64)
            commands: list[tuple[str, ...]] = []
            request = AddonRefreshRequest(
                root=fixture.root,
                distros=("humble",),
                apply=True,
                ros2cs_source=fixture.ros2cs_source,
                r2fu_source=fixture.r2fu_source,
                unity=fixture.unity,
            )

            result = run_refresh(request, runner=self._recording_runner(commands))

            self.assertFalse(result[0].current)
            self.assertEqual(3, len(commands))
            self.assertIn("build_foxrun_custom_typesupport_addon.py", commands[0][1])
            self.assertIn("sync_foxrun_custom_typesupport_addon.py", commands[1][1])
            self.assertIn("validate_foxrun_custom_typesupport_addon.py", commands[2][1])
            self.assertIn(str(fixture.root / "build"), commands[0])

    def test_apply_validates_the_external_toolchain_once_per_build(self) -> None:
        """One candidate build should perform one expensive idle/install preflight."""
        with self._fixture() as fixture:
            fixture.write_addon(runtime_manifest_sha="stale")
            request = AddonRefreshRequest(
                root=fixture.root,
                distros=("humble",),
                apply=True,
                ros2cs_source=fixture.ros2cs_source,
                r2fu_source=fixture.r2fu_source,
                unity=fixture.unity,
            )
            commands: list[tuple[str, ...]] = []
            original = refresh._require_apply_toolchain
            with patch.object(
                refresh,
                "_require_apply_toolchain",
                wraps=original,
            ) as require:
                run_refresh(request, runner=self._recording_runner(commands))

        self.assertEqual(1, require.call_count)

    def test_apply_forwards_the_explicit_distro_ros2_root_to_the_builder(self) -> None:
        """An isolated worktree must not depend on ignored repo-local ROS junctions."""
        with self._fixture() as fixture:
            fixture.write_addon(runtime_manifest_sha="0" * 64)
            ros2_root = fixture.root / "external" / "ros2_humble"
            ros2_root.mkdir(parents=True)
            commands: list[tuple[str, ...]] = []
            request = AddonRefreshRequest(
                root=fixture.root,
                distros=("humble",),
                apply=True,
                ros2cs_source=fixture.ros2cs_source,
                r2fu_source=fixture.r2fu_source,
                unity=fixture.unity,
                ros2_roots=(("humble", ros2_root),),
            )

            run_refresh(request, runner=self._recording_runner(commands))

            build = commands[0]
            self.assertIn("--ros2-root", build)
            option = build.index("--ros2-root")
            self.assertEqual(str(ros2_root.resolve()), build[option + 1])

    def test_apply_reuses_a_matching_validated_candidate_before_rebuilding(self) -> None:
        """A prior successful candidate is synchronized instead of rebuilding native code again."""
        with self._fixture() as fixture:
            fixture.write_addon(runtime_manifest_sha="0" * 64)
            fixture.write_candidate(runtime_manifest_sha=fixture.runtime_manifest_sha)
            commands: list[tuple[str, ...]] = []
            request = AddonRefreshRequest(
                root=fixture.root,
                distros=("humble",),
                apply=True,
                ros2cs_source=fixture.ros2cs_source,
                r2fu_source=fixture.r2fu_source,
                unity=fixture.unity,
            )

            run_refresh(request, runner=self._recording_runner(commands))

            self.assertEqual(2, len(commands))
            self.assertIn("sync_foxrun_custom_typesupport_addon.py", commands[0][1])
            self.assertIn("validate_foxrun_custom_typesupport_addon.py", commands[1][1])

    def test_apply_rebuilds_when_candidate_payload_is_not_exactly_inventoried(self) -> None:
        """A candidate with an unrecorded PluginImporter file cannot bypass a fresh build."""
        with self._fixture() as fixture:
            fixture.write_addon(runtime_manifest_sha="0" * 64)
            fixture.write_candidate(runtime_manifest_sha=fixture.runtime_manifest_sha)
            extra = (
                fixture.root
                / "build"
                / "phase181"
                / "humble"
                / "candidate"
                / "package"
                / "Runtime"
                / "Ros2ForUnity"
                / "Plugins"
                / "Windows"
                / "x86_64"
                / "unexpected.dll.meta"
            )
            extra.parent.mkdir(parents=True)
            extra.write_text("PluginImporter:\n", encoding="utf-8")
            commands: list[tuple[str, ...]] = []
            request = AddonRefreshRequest(
                root=fixture.root,
                distros=("humble",),
                apply=True,
                ros2cs_source=fixture.ros2cs_source,
                r2fu_source=fixture.r2fu_source,
                unity=fixture.unity,
            )

            run_refresh(request, runner=self._recording_runner(commands))

            self.assertEqual(3, len(commands))
            self.assertIn("build_foxrun_custom_typesupport_addon.py", commands[0][1])

    def test_apply_rebuilds_when_candidate_validation_hash_is_stale(self) -> None:
        """A proof record for different candidate bytes cannot authorize reuse."""
        with self._fixture() as fixture:
            fixture.write_addon(runtime_manifest_sha="0" * 64)
            fixture.write_candidate(runtime_manifest_sha=fixture.runtime_manifest_sha)
            evidence = fixture.root / "build/phase181/humble/candidate/e/candidate-validation.json"
            payload = json.loads(evidence.read_text(encoding="utf-8"))
            payload["candidatePackageSha256"] = "0" * 64
            evidence.write_text(json.dumps(payload), encoding="utf-8")
            commands: list[tuple[str, ...]] = []
            request = AddonRefreshRequest(
                root=fixture.root,
                distros=("humble",),
                apply=True,
                ros2cs_source=fixture.ros2cs_source,
                r2fu_source=fixture.r2fu_source,
                unity=fixture.unity,
            )

            run_refresh(request, runner=self._recording_runner(commands))

            self.assertEqual(3, len(commands))
            self.assertIn("build_foxrun_custom_typesupport_addon.py", commands[0][1])

    def test_apply_refuses_an_incomplete_ros2cs_install_before_any_child_command(self) -> None:
        """A concurrent ros2cs rebuild must not race an add-on candidate build."""
        with self._fixture() as fixture:
            fixture.write_addon(runtime_manifest_sha="0" * 64)
            (fixture.ros2cs_source / "install-humble" / "lib" / "dotnet" / "builtin_interfaces_assembly.dll").unlink()
            commands: list[tuple[str, ...]] = []
            request = AddonRefreshRequest(
                root=fixture.root,
                distros=("humble",),
                apply=True,
                ros2cs_source=fixture.ros2cs_source,
                r2fu_source=fixture.r2fu_source,
                unity=fixture.unity,
            )

            with self.assertRaisesRegex(AddonRefreshError, "wait-for-complete-ros2cs-install"):
                run_refresh(request, runner=self._recording_runner(commands))

            self.assertEqual([], commands)

    def test_apply_refuses_a_ros2cs_overlay_without_the_required_native_rosidl_header(self) -> None:
        """The C# overlay cannot hide a half-installed native builtin_interfaces package."""
        with self._fixture() as fixture:
            fixture.write_addon(runtime_manifest_sha="0" * 64)
            header = (
                fixture.ros2cs_source
                / "install-humble"
                / "include"
                / "builtin_interfaces"
                / "builtin_interfaces"
                / "msg"
                / "detail"
                / "time__struct.hpp"
            )
            header.unlink()
            commands: list[tuple[str, ...]] = []
            request = AddonRefreshRequest(
                root=fixture.root,
                distros=("humble",),
                apply=True,
                ros2cs_source=fixture.ros2cs_source,
                r2fu_source=fixture.r2fu_source,
                unity=fixture.unity,
            )

            with self.assertRaisesRegex(AddonRefreshError, "wait-for-complete-ros2cs-install"):
                run_refresh(request, runner=self._recording_runner(commands))

            self.assertEqual([], commands)

    def test_apply_refuses_a_running_ros2cs_colcon_build_before_any_child_command(self) -> None:
        """A live external install transaction has priority over this add-on refresh."""
        with self._fixture() as fixture:
            fixture.write_addon(runtime_manifest_sha="0" * 64)
            commands: list[tuple[str, ...]] = []
            request = AddonRefreshRequest(
                root=fixture.root,
                distros=("humble",),
                apply=True,
                ros2cs_source=fixture.ros2cs_source,
                r2fu_source=fixture.r2fu_source,
                unity=fixture.unity,
            )

            with patch.object(refresh, "_ros2cs_install_build_is_active", return_value=True):
                with self.assertRaisesRegex(AddonRefreshError, "wait-for-active-ros2cs-colcon-build"):
                    run_refresh(request, runner=self._recording_runner(commands))

            self.assertEqual([], commands)

    def test_apply_never_syncs_a_candidate_when_the_ros2cs_install_changes_during_build(self) -> None:
        """A toolchain mutation after preflight invalidates the candidate rather than publishing it."""
        with self._fixture() as fixture:
            fixture.write_addon(runtime_manifest_sha="0" * 64)
            commands: list[tuple[str, ...]] = []
            request = AddonRefreshRequest(
                root=fixture.root,
                distros=("humble",),
                apply=True,
                ros2cs_source=fixture.ros2cs_source,
                r2fu_source=fixture.r2fu_source,
                unity=fixture.unity,
            )

            def mutating_runner(command, **_kwargs):
                """Record the build command and mutate the install after preflight."""

                commands.append(tuple(command))
                if "build_foxrun_custom_typesupport_addon.py" in command[1]:
                    (fixture.ros2cs_source / "install-humble" / "lib" / "dotnet" / "builtin_interfaces_assembly.dll").write_bytes(
                        b"changed-toolchain"
                    )
                return subprocess.CompletedProcess(command, 0, "", "")

            with patch.object(refresh, "_ros2cs_install_build_is_active", return_value=False):
                with self.assertRaisesRegex(AddonRefreshError, "wait-for-stable-ros2cs-install"):
                    run_refresh(request, runner=mutating_runner)

            self.assertEqual(1, len(commands))
            self.assertIn("build_foxrun_custom_typesupport_addon.py", commands[0][1])

    def test_multiple_active_runtimes_fail_before_any_child_command(self) -> None:
        """The orchestrator refuses the duplicate-runtime state that breaks Unity compilation."""
        with self._fixture() as fixture:
            fixture.write_addon(runtime_manifest_sha=fixture.runtime_manifest_sha)
            fixture.add_runtime_dependency("jazzy")
            commands: list[tuple[str, ...]] = []

            with self.assertRaisesRegex(AddonRefreshError, "select-exactly-one-runtime"):
                run_refresh(
                    AddonRefreshRequest(root=fixture.root, distros=("humble",), apply=False),
                    runner=self._recording_runner(commands),
                )

            self.assertEqual([], commands)

    def test_stale_runtime_lock_entry_fails_before_any_child_command(self) -> None:
        """A removed manifest runtime must not remain resolved in Unity's lock file."""
        with self._fixture() as fixture:
            fixture.write_addon(runtime_manifest_sha=fixture.runtime_manifest_sha)
            fixture.add_runtime_lock_entry("jazzy")
            commands: list[tuple[str, ...]] = []

            with self.assertRaisesRegex(AddonRefreshError, "select-exactly-one-runtime"):
                run_refresh(
                    AddonRefreshRequest(root=fixture.root, distros=("humble",), apply=False),
                    runner=self._recording_runner(commands),
                )

            self.assertEqual([], commands)

    @staticmethod
    def _recording_runner(commands: list[tuple[str, ...]]):
        """Return a completed-process runner that exposes the exact child argv."""

        def run(command, **_kwargs):
            """Capture one child argv and report a successful completed process."""

            commands.append(tuple(command))
            return subprocess.CompletedProcess(command, 0, "", "")

        return run

    def _fixture(self) -> "_Fixture":
        """Create a minimal repository-shaped refresh fixture."""
        return _Fixture()


__all__ = [name for name in globals() if not name.startswith("__")]
