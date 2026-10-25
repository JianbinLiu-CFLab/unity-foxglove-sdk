from __future__ import annotations

from .foundation import *


class SourceGeneratorParityTests:
    """Exercise source-generator identity and semantic parity contracts."""

    def test_validator_exposes_the_locked_provider_contract_gate(self) -> None:
        """Freshness must include dependencies, ledgers, IDs, hint parity, and analyzer sets."""
        self.assertTrue(self.validator.validate_analyzer_contracts(("core", "r2fu", "ros2bridge")))

    def test_provider_descriptor_ids_scan_every_compiled_source(self) -> None:
        """A Provider diagnostic ID cannot evade the ledger gate through its filename."""
        with tempfile.TemporaryDirectory() as temp:
            source = Path(temp) / "FoxRunR2fuEmitter.cs"
            source.write_text(
                'internal const string DiagnosticId = "FOXR2F999";\n',
                encoding="utf-8",
            )

            self.assertEqual(
                {"FOXR2F999"},
                self.validator._provider_descriptor_ids([source], "FOXR2F"),
            )

    def test_shared_identifier_utilities_are_parity_guarded(self) -> None:
        """All three independently packaged identifier helpers must share one gate."""
        groups = dict(self.validator.EXACT_SHARED_SOURCE_GROUPS)
        expected = {
            ROOT / "Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/IdentifierUtils.cs",
            ROOT / "Packages/dev.unity2foxglove.ros2forunity/Editor/SourceGenerators/src/Shared/IdentifierUtils.cs",
            ROOT / "Packages/dev.unity2foxglove.ros2bridge/Editor/SourceGenerators/src/Shared/IdentifierUtils.cs",
        }

        self.assertEqual(expected, set(groups["Identifier utility"]))

    def test_verbatim_provider_copies_of_core_sources_are_parity_guarded(self) -> None:
        """Every core source copied verbatim into a provider analyzer must be exact-parity gated."""
        groups = dict(self.validator.EXACT_SHARED_SOURCE_GROUPS)
        shared = "Editor/SourceGenerators/src/Shared/"
        validator_group = {
            ROOT / "Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunGenerationModelValidator.cs",
            ROOT / f"Packages/dev.unity2foxglove.ros2forunity/{shared}FoxRunGenerationModelValidator.cs",
            ROOT / f"Packages/dev.unity2foxglove.ros2bridge/{shared}FoxRunGenerationModelValidator.cs",
        }

        self.assertEqual(validator_group, set(groups["Generation model validator"]))
        self.assertEqual(2, len(groups["Type expression emitter"]))
        for label, paths in self.validator.EXACT_SHARED_SOURCE_GROUPS:
            for path in paths:
                self.assertTrue(path.is_file(), f"{label}: {path}")
        self.assertTrue(self.validator.validate_shared_source_parity())

        guarded = {
            (core, copy)
            for _label, paths in self.validator.EXACT_SHARED_SOURCE_GROUPS
            for core in paths
            for copy in paths
        }
        sdk = ROOT / "Packages/dev.unity2foxglove.sdk"
        cores = {
            path.name: path
            for folder in ("Editor/Shared", "Editor/SourceGenerators/src")
            for path in (sdk / folder).rglob("*.cs")
        }
        known_unguarded_variants = {
            "FoxgloveLogSourceGenerator.cs",
            "FoxgloveLogSourceGenerator.Models.cs",
            "MemberExtraction.cs",
            "Utilities.cs",
            "FoxRunGenerationModel.cs",
            "FoxRunTypeShape.cs",
        }
        for package in ("ros2forunity", "ros2bridge"):
            for copy in (ROOT / f"Packages/dev.unity2foxglove.{package}/{shared}").rglob("*.cs"):
                core = cores.get(copy.name)
                if core is None or copy.name in known_unguarded_variants:
                    continue
                self.assertTrue((core, copy) in guarded, f"{package} copy {copy.name} is not parity guarded")

    def test_verbatim_provider_copy_drift_fails_actual_parity_gate(self) -> None:
        """Canonical and service provider copies must fail the real parity validator when drifted."""
        groups = tuple(self.validator.EXACT_SHARED_SOURCE_GROUPS)
        expected = {
            "Canonical type normalizer": (
                "FoxRunCanonicalTypeNormalizer.cs",
                "FoxRunCanonicalTypeNormalizer.cs",
                "FoxRunCanonicalTypeNormalizer.cs",
            ),
            "Service DTO rules": (
                "FoxServiceDtoRules.cs",
                "FoxServiceDtoRules.cs",
                "FoxServiceDtoRules.cs",
            ),
        }
        by_label = dict(groups)
        for label, expected_names in expected.items():
            with self.subTest(label=label):
                self.assertIn(label, by_label)
                source_paths = tuple(by_label[label])
                self.assertEqual(expected_names, tuple(path.name for path in source_paths))
                with tempfile.TemporaryDirectory() as temp:
                    root = Path(temp)
                    cloned_groups = []
                    mutated_copy = None
                    for group_label, paths in groups:
                        clones = []
                        for index, source in enumerate(paths):
                            clone = root / group_label / f"{index}-{source.name}"
                            clone.parent.mkdir(parents=True, exist_ok=True)
                            clone.write_bytes(source.read_bytes())
                            clones.append(clone)
                        if group_label == label:
                            mutated_copy = clones[-1]
                        cloned_groups.append((group_label, tuple(clones)))
                    assert mutated_copy is not None
                    mutated_copy.write_text(
                        mutated_copy.read_text(encoding="utf-8") + "// isolated parity drift\n",
                        encoding="utf-8",
                    )
                    with mock.patch.object(
                        self.validator,
                        "EXACT_SHARED_SOURCE_GROUPS",
                        tuple(cloned_groups),
                    ), mock.patch("sys.stderr"):
                        self.assertFalse(self.validator.validate_shared_source_parity())

    def test_shared_analyzer_source_parity_rejects_one_copy_drift(self) -> None:
        """The independently packaged analyzers must fail on shared semantic drift."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            builder_paths = tuple(
                root / f"builder-{index}.cs"
                for index in range(3)
            )
            provider_paths = tuple(
                root / f"shape-{index}.cs"
                for index in range(2)
            )
            core_path = root / "shape-core.cs"
            for path in builder_paths:
                path.write_text(
                    "internal sealed class Builder {}\n",
                    encoding="utf-8",
                )
            provider_text = (
                "namespace Shared\n"
                "{\n"
                "    internal sealed class Shape {}\n"
                "}\n"
            )
            for path in provider_paths:
                path.write_text(provider_text, encoding="utf-8")
            core_path.write_text(
                "namespace Shared\n"
                "{\n"
                "    internal sealed class Shape {}\n\n"
                "    internal static class FoxRunLogicalSchemaNameResolver\n"
                "    {\n"
                "    }\n"
                "}\n",
                encoding="utf-8",
            )

            with mock.patch.object(
                self.validator,
                "EXACT_SHARED_SOURCE_GROUPS",
                (
                    ("builder", builder_paths),
                    ("provider shape", provider_paths),
                ),
            ), mock.patch.object(
                self.validator,
                "CORE_TYPE_SHAPE",
                core_path,
            ), mock.patch.object(
                self.validator,
                "PROVIDER_TYPE_SHAPE",
                provider_paths[0],
            ):
                self.assertTrue(
                    self.validator.validate_shared_source_parity()
                )
                provider_paths[1].write_text(
                    provider_text.replace("Shape", "DriftedShape"),
                    encoding="utf-8",
                )
                with mock.patch("sys.stderr"):
                    self.assertFalse(
                        self.validator.validate_shared_source_parity()
                    )

    def test_missing_analyzer_dependency_is_reported_before_hash_comparison(self) -> None:
        """A source generator dependency must ship beside the analyzer DLL for Unity to load it."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            build = root / "build"
            build.mkdir()
            (build / "FoxgloveLogSourceGenerator.dll").write_bytes(b"generator")

            with mock.patch.object(self.validator, "run_build", return_value=True):
                with mock.patch.object(
                    self.validator,
                    "CHECKED_IN_ARTIFACTS",
                    {
                        "FoxgloveLogSourceGenerator.dll": root / "checked" / "FoxgloveLogSourceGenerator.dll",
                        "Google.Protobuf.dll": root / "checked" / "Google.Protobuf.dll",
                    },
                ):
                    with mock.patch("sys.stderr") as stderr:
                        result = self.validator.validate_or_update(False, build, [])

        self.assertEqual(1, result)
        written = "".join(call.args[0] for call in stderr.write.call_args_list if call.args)
        self.assertIn("Google.Protobuf.dll", written)
        self.assertIn("did not produce", written)


__all__ = ["SourceGeneratorParityTests"]
