"""Run the five Task 1 identity-gate sensitivity rehearsals."""

from __future__ import annotations

import argparse
import csv
import shutil
import sys
import tempfile
from pathlib import Path

if __package__ in (None, ""):
    sys.path.insert(0, str(Path(__file__).resolve().parents[3]))

from Scripts.validation.source_decomposition import identity_contracts


IDENTITY_SURFACE_FILES = {
    "managed": ("managed-api-surface.tsv", ["path", "line", "visibility", "declaration"]),
    "python": ("python-symbols.tsv", ["path", "all", "exports"]),
    "cpp": ("cpp-symbol-surface.tsv", ["path", "line", "symbol", "declaration"]),
    "tests": ("csharp-test-names.tsv", ["path", "test_name", "framework"]),
}


def drop_first_data_row(source: Path, target: Path) -> None:
    """Phase192 drop first data row."""
    lines = source.read_text(encoding="utf-8").splitlines(keepends=True)
    if len(lines) < 2:
        raise ValueError(f"identity surface has no data rows: {source}")
    target.write_text("".join([lines[0], *lines[2:]]), encoding="utf-8")


def source_mutation_result(kind: str, source: Path, mutate) -> int:
    """Run one identity extractor against a real source mutation and return its diff status."""
    original = source.read_text(encoding="utf-8")
    changed = mutate(original)
    extractors = {
        "managed": identity_contracts.managed_rows,
        "python": identity_contracts.python_surface,
        "cpp": identity_contracts.cpp_rows,
        "tests": identity_contracts.csharp_test_rows,
    }
    if kind not in extractors:
        raise ValueError(f"unsupported source mutation kind: {kind}")
    before = extractors[kind](source.as_posix(), original)
    after = extractors[kind](source.as_posix(), changed)
    return 0 if before == after else 1


def frozen_identity_mutation_result(
    kind: str,
    source: Path,
    mutate,
    identity_dir: Path,
    root: Path,
) -> int:
    """Compare a mutated source surface against its frozen TSV baseline."""
    try:
        filename, header = IDENTITY_SURFACE_FILES[kind]
    except KeyError as exc:
        raise ValueError(f"unsupported source mutation kind: {kind}") from exc
    baseline = identity_dir / filename
    if not baseline.is_file():
        raise FileNotFoundError(baseline)
    original = source.read_text(encoding="utf-8")
    changed = mutate(original)
    extractor = {
        "managed": identity_contracts.managed_rows,
        "python": identity_contracts.python_surface,
        "cpp": identity_contracts.cpp_rows,
        "tests": identity_contracts.csharp_test_rows,
    }[kind]
    try:
        relative = source.resolve().relative_to(root.resolve()).as_posix()
    except ValueError:
        relative = source.as_posix()
    original_rows = extractor(relative, original)
    changed_rows = extractor(relative, changed)
    with tempfile.TemporaryDirectory(prefix="phase192-identity-mutation-") as temp:
        with baseline.open(encoding="utf-8", newline="") as handle:
            reader = csv.reader(handle, delimiter="\t")
            if next(reader, None) != header:
                raise ValueError(f"identity surface header mismatch: {baseline}")
            baseline_rows = [row for row in reader if row and row[0] == relative]
        if not baseline_rows and original_rows:
            raise AssertionError(f"frozen identity baseline has no rows for {relative}")
        target_baseline = Path(temp) / f"baseline-{filename}"
        original_surface = Path(temp) / f"original-{filename}"
        changed_surface = Path(temp) / f"changed-{filename}"
        identity_contracts.write_rows(target_baseline, header, baseline_rows)
        identity_contracts.write_rows(original_surface, header, original_rows)
        if identity_contracts.compare_tsv(target_baseline, original_surface) != 0:
            raise AssertionError(f"frozen identity baseline mismatch for {relative}")
        identity_contracts.write_rows(changed_surface, header, changed_rows)
        return identity_contracts.compare_tsv(target_baseline, changed_surface)


def _real_source_mutation_targets(root: Path) -> tuple[tuple[str, Path, object], ...]:
    """Return representative tracked sources for each identity surface."""
    managed = root / "Packages/dev.unity2foxglove.ros2bridge/Tests/Unit/Phase186/Decomposed/BridgeProductSurfaceTests/BridgeProductAcceptance.cs"
    python = root / "Scripts/validation/source_decomposition/identity_contracts/surface_rows.py"
    cpp = root / "Scripts/native/draco_native/Unity2FoxgloveDracoNative.cpp"
    tests = managed
    return (
        ("managed", managed, lambda text: text.replace("BridgeProductSurfaceTests", "BridgeProductSurfaceTestsRenamed", 1)),
        ("python", python, lambda text: text.replace("def python_surface(", "def python_surface_renamed(", 1)),
        ("cpp", cpp, lambda text: text.replace("U2FDracoGetVersion(", "U2FDracoGetVersionX(", 1)),
        ("tests", tests, lambda text: text.replace("[Fact]", "", 1)),
    )


_SURFACE_SPECS = {
    "managed-public-member": (
        "managed-paths.tsv",
        "managed-api-surface.tsv",
        ["path", "line", "visibility", "declaration"],
        identity_contracts.managed_rows,
    ),
    "python-export": (
        "python-paths.tsv",
        "python-symbols.tsv",
        ["path", "all", "exports"],
        identity_contracts.python_surface,
    ),
    "cpp-symbol": (
        "cpp-paths.tsv",
        "cpp-symbol-surface.tsv",
        ["path", "line", "symbol", "declaration"],
        identity_contracts.cpp_rows,
    ),
    "test-name": (
        "csharp-test-paths.tsv",
        "csharp-test-names.tsv",
        ["path", "test_name", "framework"],
        identity_contracts.csharp_test_rows,
    ),
}


def _manifest_paths(path: Path) -> list[str]:
    """Read repository-relative paths from a Task 0 path manifest."""
    with path.open(encoding="utf-8", newline="") as handle:
        return [row[0] for row in csv.reader(handle, delimiter="\t") if row and row[0] != "path"]


def _mutate_source(text: str, label: str) -> str:
    """Apply one source-level mutation that the real extractor must observe."""
    if label == "managed-public-member":
        for visibility in ("public", "internal", "protected"):
            marker = f"{visibility} "
            if marker in text:
                return text.replace(marker, "private ", 1)
        raise AssertionError("managed mutation found no visible declaration")
    if label == "python-export":
        return text + "\n\ndef sensitivity_probe_export():\n    return None\n"
    if label == "cpp-symbol":
        return text + "\nstruct Phase192SensitivityProbe {};\n"
    if label == "test-name":
        return text + "\n[Fact]\npublic void Phase192SensitivityProbe() { }\n"
    raise AssertionError(f"unknown sensitivity mutation: {label}")


def _run_real_surface_mutation(
    label: str,
    identity_root: Path,
    repository_root: Path,
    temp_root: Path,
) -> int:
    """Mutate a real source copy, re-extract it, and compare its surface."""
    del temp_root
    paths_name, _, _, _ = _SURFACE_SPECS[label]
    paths = _manifest_paths(identity_root / paths_name)
    if not paths:
        raise AssertionError(f"{label} path manifest is empty")
    relative = paths[0]
    source = repository_root / relative
    if not source.is_file():
        raise AssertionError(f"{label} source is missing: {relative}")
    kind = {
        "managed-public-member": "managed",
        "python-export": "python",
        "cpp-symbol": "cpp",
        "test-name": "tests",
    }[label]
    return frozen_identity_mutation_result(
        kind,
        source,
        lambda text: _mutate_source(text, label),
        identity_root,
        repository_root,
    )


def generated_byte_mutation(manifest: Path, root: Path) -> int:
    """Phase192 generated byte mutation."""
    with manifest.open(encoding="utf-8", newline="") as handle:
        rows = list(csv.DictReader(handle, delimiter="\t"))
    if not rows:
        raise AssertionError("generated manifest has no rows")
    baseline_exit = identity_contracts.check_hash_manifest(manifest, root)
    if baseline_exit != 0:
        raise AssertionError("generated hash manifest baseline is not valid")
    with tempfile.TemporaryDirectory(prefix="phase192-generated-mutation-") as temp:
        temp_root = Path(temp)
        for row in rows:
            relative = Path(row["path"])
            if relative.is_absolute():
                raise AssertionError(f"generated manifest path must be relative: {relative}")
            source = root / relative
            target = temp_root / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source, target)
        mutated_path = temp_root / rows[0]["path"]
        original = mutated_path.read_bytes()
        mutated = bytes([original[0] ^ 1]) + original[1:] if original else bytes([0])
        mutated_path.write_bytes(mutated)
        return identity_contracts.check_hash_manifest(manifest, temp_root)


def main(argv: list[str] | None = None) -> int:
    """Phase192 main."""
    parser = argparse.ArgumentParser()
    parser.add_argument("--identity", type=Path, default=Path("build/phase192/baseline/identity"))
    parser.add_argument("--generated", type=Path, default=Path("build/phase192/baseline/generated-hash-manifest.tsv"))
    parser.add_argument("--root", type=Path, default=Path("."))
    parser.add_argument("--report", type=Path, required=True)
    args = parser.parse_args(argv)
    if not args.identity.is_dir():
        raise FileNotFoundError(args.identity)
    results: list[str] = []
    with tempfile.TemporaryDirectory(prefix="phase192-sensitivity-") as temp:
        temp_root = Path(temp)
        for label in _SURFACE_SPECS:
            exit_code = _run_real_surface_mutation(label, args.identity, args.root, temp_root)
            if exit_code != 1:
                raise AssertionError(f"{label} mutation was not RED")
            results.append(f"MUTATION={label} SOURCE_EXTRACTOR=REAL RESULT=RED EXIT=1")
    generated_exit = generated_byte_mutation(args.generated, args.root)
    if generated_exit != 1:
        raise AssertionError("generated byte mutation was not RED")
    results.append("MUTATION=generated-line RESULT=RED EXIT=1")
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text("\n".join(results) + "\nSENSITIVITY_PROOF=5/5_KILLED\n", encoding="utf-8")
    print("SENSITIVITY_PROOF=5/5_KILLED")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
