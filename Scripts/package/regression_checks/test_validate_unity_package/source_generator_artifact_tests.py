from __future__ import annotations

from .foundation import *


class SourceGeneratorArtifactTests:
    """Exercise source-generator artifact identity and rollback behavior."""

    def test_runtime_protobuf_plugin_must_match_checked_in_analyzer_dependency(self) -> None:
        """The Unity runtime plug-in must stay in lockstep with analyzer Google.Protobuf."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            build = root / "build"
            checked = root / "checked"
            build.mkdir()
            checked.mkdir()
            (build / "FoxgloveLogSourceGenerator.dll").write_bytes(b"generator")
            (build / "Google.Protobuf.dll").write_bytes(b"protobuf-3.29.3")
            (checked / "FoxgloveLogSourceGenerator.dll").write_bytes(b"generator")
            (checked / "Google.Protobuf.dll").write_bytes(b"protobuf-3.29.3")
            runtime_plugin = root / "Google.Protobuf.runtime.dll"
            runtime_plugin.write_bytes(b"protobuf-mismatch")

            with mock.patch.object(self.validator, "run_build", return_value=True):
                with mock.patch.object(
                    self.validator,
                    "CHECKED_IN_ARTIFACTS",
                    {
                        "FoxgloveLogSourceGenerator.dll": checked / "FoxgloveLogSourceGenerator.dll",
                        "Google.Protobuf.dll": checked / "Google.Protobuf.dll",
                    },
                ):
                    with mock.patch.object(
                        self.validator,
                        "UNITY_PLUGIN_GOOGLE_PROTOBUF",
                        runtime_plugin,
                        create=True,
                    ):
                        with mock.patch("sys.stderr") as stderr:
                            result = self.validator.validate_or_update(False, build, [])

        self.assertEqual(1, result)
        written = "".join(call.args[0] for call in stderr.write.call_args_list if call.args)
        self.assertIn("Unity runtime Google.Protobuf plug-in", written)

    def test_runtime_protobuf_mismatch_blocks_analyzer_update_before_copy(self) -> None:
        """A mismatched runtime plug-in must not leave an --update half-applied."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            build = root / "build"
            checked = root / "checked"
            build.mkdir()
            checked.mkdir()
            (build / "FoxgloveLogSourceGenerator.dll").write_bytes(b"fresh-generator")
            (build / "Google.Protobuf.dll").write_bytes(b"fresh-protobuf")
            checked_generator = checked / "FoxgloveLogSourceGenerator.dll"
            checked_generator.write_bytes(b"old-generator")
            (checked / "Google.Protobuf.dll").write_bytes(b"old-protobuf")
            runtime_plugin = root / "Google.Protobuf.runtime.dll"
            runtime_plugin.write_bytes(b"protobuf-mismatch")

            with mock.patch.object(self.validator, "run_build", return_value=True):
                with mock.patch.object(
                    self.validator,
                    "CHECKED_IN_ARTIFACTS",
                    {
                        "FoxgloveLogSourceGenerator.dll": checked_generator,
                        "Google.Protobuf.dll": checked / "Google.Protobuf.dll",
                    },
                ):
                    with mock.patch.object(
                        self.validator,
                        "UNITY_PLUGIN_GOOGLE_PROTOBUF",
                        runtime_plugin,
                    ):
                        with mock.patch("sys.stderr"):
                            result = self.validator.validate_or_update(True, build, [])
            persisted_generator = checked_generator.read_bytes()

        self.assertEqual(1, result)
        self.assertEqual(b"old-generator", persisted_generator)

    def test_analyzer_update_rolls_back_when_a_later_artifact_copy_fails(self) -> None:
        """A failed multi-artifact update must leave the checked-in set unchanged."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            build = root / "build"
            checked = root / "checked"
            build.mkdir()
            checked.mkdir()
            (build / "FoxgloveLogSourceGenerator.dll").write_bytes(b"fresh-generator")
            (build / "Google.Protobuf.dll").write_bytes(b"fresh-protobuf")
            checked_generator = checked / "FoxgloveLogSourceGenerator.dll"
            checked_protobuf = checked / "Google.Protobuf.dll"
            checked_generator.write_bytes(b"old-generator")
            checked_protobuf.write_bytes(b"old-protobuf")
            artifacts = {
                "FoxgloveLogSourceGenerator.dll": checked_generator,
                "Google.Protobuf.dll": checked_protobuf,
            }
            calls: list[Path] = []
            original_copy2 = self.validator.shutil.copy2

            def fail_on_protobuf_copy(source: Path, destination: Path, *args, **kwargs):
                """Fail only when the controlled protobuf destination is copied."""
                calls.append(Path(destination))
                if Path(destination) == checked_protobuf:
                    raise OSError("CONTROLLED_SECOND_COPY_FAILURE")
                return original_copy2(source, destination, *args, **kwargs)

            with mock.patch.object(self.validator, "REPO_ROOT", root):
                with mock.patch.object(self.validator, "run_build", return_value=True):
                    with mock.patch.object(
                        self.validator,
                        "CHECKED_IN_ARTIFACTS",
                        artifacts,
                    ):
                        with mock.patch.object(
                            self.validator,
                            "validate_unity_plugin_protobuf_match",
                            return_value=True,
                        ):
                            with mock.patch.object(
                                self.validator.shutil,
                                "copy2",
                                side_effect=fail_on_protobuf_copy,
                            ):
                                try:
                                    result = self.validator.validate_or_update(True, build, [])
                                except OSError:
                                    result = None

            persisted = [
                checked_generator.read_bytes(),
                checked_protobuf.read_bytes(),
            ]

        self.assertEqual(1, result)
        self.assertEqual([b"old-generator", b"old-protobuf"], persisted)
        self.assertIn(checked_protobuf, calls)

    def test_preexisting_equal_artifact_without_current_build_write_is_rejected(self) -> None:
        """A successful build must not authenticate equal-hash residue from an earlier run."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            build = root / "build"
            checked = root / "checked"
            build.mkdir()
            checked.mkdir()
            expected = b"stale-authenticated-bytes"
            built = build / "a.dll"
            checked_file = checked / "a.dll"
            built.write_bytes(expected)
            checked_file.write_bytes(expected)
            project = root / "Generator.csproj"
            project.write_text("<Project />\n", encoding="utf-8")
            target = self.validator.AnalyzerTarget(
                "core",
                project,
                {"a.dll": checked_file},
                build,
                False,
            )
            calls: list[tuple[object, ...]] = []

            def successful_no_output_build(*args, **kwargs):
                """Simulate a successful build invocation with no changed output."""
                calls.append(args)
                return True

            with mock.patch.object(self.validator, "REPO_ROOT", root), \
                mock.patch.object(self.validator, "TARGETS", {"core": target}), \
                mock.patch.object(
                    self.validator,
                    "CHECKED_IN_ARTIFACTS",
                    {"a.dll": checked_file},
                ), \
                mock.patch.object(
                    self.validator.subprocess,
                    "run",
                    side_effect=successful_no_output_build,
                ):
                    result = self.validator.validate_or_update(False, build, [], "core")
            persisted = built.read_bytes()

        self.assertEqual(1, result)
        self.assertEqual(1, len(calls))
        self.assertEqual(expected, persisted)


__all__ = ["SourceGeneratorArtifactTests"]
