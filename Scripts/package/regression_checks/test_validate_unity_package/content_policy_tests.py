from __future__ import annotations

from .foundation import *


class PackageContentPolicyTests:
    """Exercise forbidden content and validation naming policies."""

    def test_forbidden_sample_artifacts_reports_root_directory_once(self) -> None:
        """A forbidden directory should not flood diagnostics with descendants."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            library = root / "Samples~" / "BasicVisualization" / "Library"
            nested = library / "Sub"
            nested.mkdir(parents=True)
            (nested / "cache.bin").write_bytes(b"cache")

            self.validator.SAMPLES = root / "Samples~"
            results = []
            self.validator.check_forbidden_sample_artifacts(results)

        self.assertFalse(results[-1].ok)
        offenders = [item.strip() for item in results[-1].detail.split(";")]
        self.assertEqual(1, len(offenders))
        self.assertTrue(offenders[0].endswith("Samples~/BasicVisualization/Library"))

    def test_forbidden_public_content_reports_all_labels_per_file(self) -> None:
        """One public file can violate multiple public-boundary patterns."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            samples = root / "Samples~"
            docs = root / "Documentation~"
            samples.mkdir()
            docs.mkdir()
            offender = samples / "README.md"
            offender.write_text("C:/Users/Alice/private\nTODO\n", encoding="utf-8")

            self.validator.SAMPLES = samples
            self.validator.DOCS = docs
            self.validator.PACKAGE = root
            results = []
            self.validator.check_forbidden_public_content(results)

        self.assertFalse(results[-1].ok)
        self.assertIn("local Windows path", results[-1].detail)
        self.assertIn("to-do marker", results[-1].detail)

    def test_manual_phase_service_guard_ignores_commented_demo(self) -> None:
        """Commented manual smoke services remain inert and allowed."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            demo = root / "FoxRun"
            demo.mkdir()
            source = demo / "FoxService141DManualSmoke.cs"
            source.write_text('//[FoxService("/phase141d/manual_dto")]\n', encoding="utf-8")

            results = []
            self.validator.check_manual_phase_service_guards(results, list(demo.rglob("*.cs")))

        self.assertTrue(results[-1].ok)

    def test_manual_phase_service_guard_rejects_active_demo(self) -> None:
        """Phase-only manual services should not be accidentally committed active."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            demo = root / "FoxRun"
            demo.mkdir()
            source = demo / "FoxService141DManualSmoke.cs"
            source.write_text('[FoxService("/phase141d/manual_dto")]\n', encoding="utf-8")

            results = []
            self.validator.check_manual_phase_service_guards(results, list(demo.rglob("*.cs")))

        self.assertFalse(results[-1].ok)
        self.assertIn("FoxService141DManualSmoke.cs:1", results[-1].detail)

    def test_validation_naming_allows_legacy_phase_files(self) -> None:
        """Existing Phase-prefixed validation files remain grandfathered."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            runtime = root / "Tests" / "Runtime"
            runtime.mkdir(parents=True)
            legacy = runtime / "Phase164_57Validation.cs"
            legacy.write_text("// legacy validation\n", encoding="utf-8")
            legacy_variant = runtime / "Phase164_57FooValidation.cs"
            legacy_variant.write_text("// legacy validation variant\n", encoding="utf-8")

            self.validator.PACKAGE = root
            results = []
            self.validator.check_validation_naming(results)

        self.assertTrue(results[-1].ok)

    def test_validation_naming_rejects_exact_cutoff_phase_files(self) -> None:
        """The exact cutoff phase/index should not slip through an off-by-one gap."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            runtime = root / "Tests" / "Runtime"
            runtime.mkdir(parents=True)
            cutoff = runtime / "Phase164_58Validation.cs"
            cutoff.write_text("// cutoff validation\n", encoding="utf-8")
            cutoff_variant = runtime / "Phase164_58FooValidation.cs"
            cutoff_variant.write_text("// cutoff validation variant\n", encoding="utf-8")

            self.validator.PACKAGE = root
            results = []
            self.validator.check_validation_naming(results)

        self.assertFalse(results[-1].ok)
        self.assertIn("Phase164_58Validation.cs", results[-1].detail)
        self.assertIn("Phase164_58FooValidation.cs", results[-1].detail)

    def test_validation_naming_rejects_new_phase_files(self) -> None:
        """New validations should use descriptive filenames instead of Phase numbers."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            runtime = root / "Tests" / "Runtime"
            runtime.mkdir(parents=True)
            offender = runtime / "Phase164_59Validation.cs"
            offender.write_text("// new validation\n", encoding="utf-8")
            variant = runtime / "Phase164_59FooValidation.cs"
            variant.write_text("// new validation variant\n", encoding="utf-8")

            self.validator.PACKAGE = root
            results = []
            self.validator.check_validation_naming(results)

        self.assertFalse(results[-1].ok)
        self.assertIn("Phase164_59Validation.cs", results[-1].detail)
        self.assertIn("Phase164_59FooValidation.cs", results[-1].detail)


__all__ = ["PackageContentPolicyTests"]
