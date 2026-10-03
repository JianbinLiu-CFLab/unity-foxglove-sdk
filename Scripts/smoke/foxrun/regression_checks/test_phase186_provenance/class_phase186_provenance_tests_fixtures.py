from __future__ import annotations
from .class_phase186_provenance_tests_support import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase186_provenance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _Phase186ProvenanceTests_fixtures:
    """Decomposed Phase192 implementation component."""
    def test_fixed_source_roots_discover_untracked_protocol_sources(self) -> None:
        """Filesystem discovery must not let an untracked source evade the ledger."""

        module = load_module()
        with tempfile.TemporaryDirectory() as temporary:
            repository = pathlib.Path(temporary) / "repository"
            reference = pathlib.Path(temporary) / "reference"
            repository.mkdir()
            revision = initialize_reference_checkout(reference)

            validator = (
                repository / "Scripts/smoke/foxrun/phase186_provenance.py"
            )
            validator.parent.mkdir(parents=True)
            validator.write_text("# original validator\n", encoding="utf-8")
            untracked = (
                repository
                / "Packages/dev.unity2foxglove.ros2bridge/Runtime/Protocol/Untracked.cs"
            )
            untracked.parent.mkdir(parents=True)
            untracked.write_text(
                "internal sealed class Untracked {}\n",
                encoding="utf-8",
            )
            ledger = synthetic_ledger_path(repository)
            ledger.write_text(
                json.dumps(
                    {
                        "schemaVersion": 1,
                        "reference": reference_payload(revision=revision),
                        "implementations": [
                            {
                                "path": (
                                    "Scripts/smoke/foxrun/phase186_provenance.py"
                                ),
                                "sha256": hashlib.sha256(
                                    validator.read_bytes()
                                ).hexdigest(),
                                "classification": "original",
                                "influence": "Original validator.",
                            }
                        ],
                    }
                ),
                encoding="utf-8",
            )

            errors = module.validate_repository_provenance(
                repository,
                reference,
                ledger,
            )

        self.assertTrue(
            any("Untracked.cs" in error and "no provenance record" in error
                for error in errors),
            errors,
        )
    def test_fixed_source_roots_discover_nested_untracked_protocol_sources(
        self,
    ) -> None:
        """A nested protocol source cannot hide below a fixed source root."""

        module = load_module()
        with tempfile.TemporaryDirectory() as temporary:
            repository = pathlib.Path(temporary) / "repository"
            reference = pathlib.Path(temporary) / "reference"
            repository.mkdir()
            revision = initialize_reference_checkout(reference)

            validator = (
                repository / "Scripts/smoke/foxrun/phase186_provenance.py"
            )
            validator.parent.mkdir(parents=True)
            validator.write_text("# original validator\n", encoding="utf-8")
            nested = (
                repository
                / "Packages/dev.unity2foxglove.ros2bridge/Runtime/"
                "Protocol/Internal/Hidden.cs"
            )
            nested.parent.mkdir(parents=True)
            nested.write_text(
                "internal sealed class Hidden {}\n",
                encoding="utf-8",
            )
            ledger = synthetic_ledger_path(repository)
            ledger.write_text(
                json.dumps(
                    {
                        "schemaVersion": 1,
                        "reference": reference_payload(revision=revision),
                        "implementations": [
                            {
                                "path": (
                                    "Scripts/smoke/foxrun/phase186_provenance.py"
                                ),
                                "sha256": hashlib.sha256(
                                    validator.read_bytes()
                                ).hexdigest(),
                                "classification": "original",
                                "influence": "Original validator.",
                            }
                        ],
                    }
                ),
                encoding="utf-8",
            )

            errors = module.validate_repository_provenance(
                repository,
                reference,
                ledger,
            )

        self.assertTrue(
            any(
                "Hidden.cs" in error and "no provenance record" in error
                for error in errors
            ),
            errors,
        )
    def test_fixed_source_roots_reject_symlink_directory_escape(self) -> None:
        """A fixed source root cannot hide sources behind a linked directory."""

        module = load_module()
        with tempfile.TemporaryDirectory() as temporary:
            temporary_root = pathlib.Path(temporary)
            repository = temporary_root / "repository"
            reference = temporary_root / "reference"
            outside = temporary_root / "outside"
            repository.mkdir()
            outside.mkdir()
            revision = initialize_reference_checkout(reference)

            validator = (
                repository / "Scripts/smoke/foxrun/phase186_provenance.py"
            )
            validator.parent.mkdir(parents=True)
            validator.write_text("# original validator\n", encoding="utf-8")
            hidden = outside / "Hidden.cs"
            hidden.write_text(
                "internal sealed class Hidden {}\n",
                encoding="utf-8",
            )
            linked = (
                repository
                / "Packages/dev.unity2foxglove.ros2bridge/Runtime/"
                "Protocol/Linked"
            )
            linked.parent.mkdir(parents=True)
            try:
                linked.symlink_to(outside, target_is_directory=True)
            except OSError as exc:
                self.skipTest(f"directory symlink creation is unavailable: {exc}")
            ledger = synthetic_ledger_path(repository)
            ledger.write_text(
                json.dumps(
                    {
                        "schemaVersion": 1,
                        "reference": reference_payload(revision=revision),
                        "implementations": [
                            {
                                "path": (
                                    "Scripts/smoke/foxrun/phase186_provenance.py"
                                ),
                                "sha256": hashlib.sha256(
                                    validator.read_bytes()
                                ).hexdigest(),
                                "classification": "original",
                                "influence": "Original validator.",
                            }
                        ],
                    }
                ),
                encoding="utf-8",
            )

            errors = module.validate_repository_provenance(
                repository,
                reference,
                ledger,
            )

        self.assertTrue(
            any(
                "protocol source directory must not be a symlink or reparse point"
                in error
                for error in errors
            ),
            errors,
        )
    def test_fixed_source_roots_reject_uppercase_extension_source(self) -> None:
        """Windows casing cannot hide a protocol source from the fixed scan."""

        module = load_module()
        with tempfile.TemporaryDirectory() as temporary:
            repository = pathlib.Path(temporary) / "repository"
            reference = pathlib.Path(temporary) / "reference"
            repository.mkdir()
            revision = initialize_reference_checkout(reference)

            validator = (
                repository / "Scripts/smoke/foxrun/phase186_provenance.py"
            )
            validator.parent.mkdir(parents=True)
            validator.write_text("# original validator\n", encoding="utf-8")
            hidden = (
                repository
                / "Packages/dev.unity2foxglove.ros2bridge/Runtime/"
                "Protocol/Internal/Hidden.CS"
            )
            hidden.parent.mkdir(parents=True)
            hidden.write_text(
                "internal sealed class Hidden {}\n",
                encoding="utf-8",
            )
            ledger = synthetic_ledger_path(repository)
            ledger.write_text(
                json.dumps(
                    {
                        "schemaVersion": 1,
                        "reference": reference_payload(revision=revision),
                        "implementations": [
                            {
                                "path": (
                                    "Scripts/smoke/foxrun/phase186_provenance.py"
                                ),
                                "sha256": hashlib.sha256(
                                    validator.read_bytes()
                                ).hexdigest(),
                                "classification": "original",
                                "influence": "Original validator.",
                            }
                        ],
                    }
                ),
                encoding="utf-8",
            )

            errors = module.validate_repository_provenance(
                repository,
                reference,
                ledger,
            )

        self.assertTrue(
            any(
                "Hidden.CS" in error and "no provenance record" in error
                for error in errors
            ),
            errors,
        )
    def test_fixed_source_roots_reject_broken_reparse_entry(self) -> None:
        """A broken linked directory is still an explicit fixed-root failure."""

        module = load_module()
        with tempfile.TemporaryDirectory() as temporary:
            temporary_root = pathlib.Path(temporary)
            repository = temporary_root / "repository"
            reference = temporary_root / "reference"
            repository.mkdir()
            revision = initialize_reference_checkout(reference)

            validator = (
                repository / "Scripts/smoke/foxrun/phase186_provenance.py"
            )
            validator.parent.mkdir(parents=True)
            validator.write_text("# original validator\n", encoding="utf-8")
            broken = (
                repository
                / "Packages/dev.unity2foxglove.ros2bridge/Runtime/"
                "Protocol/Broken"
            )
            broken.parent.mkdir(parents=True)
            try:
                broken.symlink_to(
                    temporary_root / "missing-directory",
                    target_is_directory=True,
                )
            except OSError as exc:
                self.skipTest(f"directory symlink creation is unavailable: {exc}")
            ledger = synthetic_ledger_path(repository)
            ledger.write_text(
                json.dumps(
                    {
                        "schemaVersion": 1,
                        "reference": reference_payload(revision=revision),
                        "implementations": [
                            {
                                "path": (
                                    "Scripts/smoke/foxrun/phase186_provenance.py"
                                ),
                                "sha256": hashlib.sha256(
                                    validator.read_bytes()
                                ).hexdigest(),
                                "classification": "original",
                                "influence": "Original validator.",
                            }
                        ],
                    }
                ),
                encoding="utf-8",
            )

            errors = module.validate_repository_provenance(
                repository,
                reference,
                ledger,
            )

        self.assertTrue(
            any(
                "protocol source entry must not be a symlink or reparse point"
                in error
                for error in errors
            ),
            errors,
        )
    def test_implementation_path_resolution_cannot_escape_repository(self) -> None:
        """A ledgered source reached through a symlink is still containment checked."""

        module = load_module()
        with tempfile.TemporaryDirectory() as temporary:
            temporary_root = pathlib.Path(temporary)
            repository = temporary_root / "repository"
            reference = temporary_root / "reference"
            repository.mkdir()
            revision = initialize_reference_checkout(reference)
            escaped = temporary_root / "Escaped.cs"
            escaped.write_text("internal sealed class Escaped {}\n", encoding="utf-8")
            link = (
                repository
                / "Packages/dev.unity2foxglove.ros2bridge/Runtime/Protocol/Escaped.cs"
            )
            link.parent.mkdir(parents=True)
            try:
                link.symlink_to(escaped)
            except OSError as exc:
                self.skipTest(f"symlink creation is unavailable: {exc}")
            ledger = synthetic_ledger_path(repository)
            ledger.write_text(
                json.dumps(
                    {
                        "schemaVersion": 1,
                        "reference": reference_payload(revision=revision),
                        "implementations": [
                            {
                                "path": (
                                    "Packages/dev.unity2foxglove.ros2bridge/"
                                    "Runtime/Protocol/Escaped.cs"
                                ),
                                "sha256": hashlib.sha256(
                                    escaped.read_bytes()
                                ).hexdigest(),
                                "classification": "original",
                                "influence": "Original implementation.",
                            }
                        ],
                    }
                ),
                encoding="utf-8",
            )

            errors = module.validate_repository_provenance(
                repository,
                reference,
                ledger,
            )

        self.assertTrue(
            any("resolves outside repository" in error for error in errors),
            errors,
        )


__all__ = [name for name in globals() if not name.startswith("__")]
