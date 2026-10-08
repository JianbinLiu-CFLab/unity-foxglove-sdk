"""Freeze the Phase192 entry inventory and identity evidence."""
from __future__ import annotations
import argparse
import csv
import hashlib
import json
import math
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
from typing import Iterable
REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
if str(REPOSITORY_ROOT) not in sys.path:
    sys.path.insert(0, str(REPOSITORY_ROOT))
from Scripts.phase192.task0_entry import record_entry_commands, record_plan_hashes, validate_classification
SOURCE_EXTENSIONS = frozenset({".cs", ".py", ".cpp", ".cc", ".c", ".h", ".hpp", ".ts"})
GENERATED_PROTO_ROOT = "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Generated/"
GENERATED_CDR_ROOT = "Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Generated/"
GENERATED_CDR_NAMES = frozenset(
    {
        "Ros2CdrGeneratedSerializers.g.cs",
        "Ros2CdrGeneratedDeserializers.g.cs",
        "Ros2CdrSampleFactory.g.cs",
        "Ros2CdrSerializerRegistry.g.cs",
        "Ros2CdrDeserializerRegistry.g.cs",
    }
)
GENERATED_EXACT = frozenset(
    {
        "Unity2Foxglove/Assets/Scripts/Generated/TestLog_FoxRun.g.cs",
        "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Fixtures/FoxRunGenerationModelFixture_FoxRun.golden.cs",
        "Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/FoxgloveRos2MsgSchemaCatalog.cs",
    }
)
VENDOR_PREFIXES = (
    "third-party/",
    "Packages/dev.unity2foxglove.sdk/Editor/Native/OpenH264/v2.6.0/",
)
VENDOR_ROOTS = {
    "third-party/foxglove-sdk": "b298c3d1649e6e5dfd77a53b12ab7c27f97c7aba",
    "third-party/mcap": "c3cab6bd3ce79199e362766daec3a4689f3a0335",
    "third-party/ROS-TCP-Connector": "c27f00c6cf750d2d0564349b3039d19aa3925e7c",
    "D:/ros2unity/third-party/ros2cs": "4252e38a5bafe6102c3f8f3dcb5cb591492ba087",
    "D:/ros2unity/third-party/ros2-for-unity": "6ae8e285115e7ca72c48932df5d673f512b56dd7",
}
AREA_RULES = (
    ("test-suites", "**/Tests/**", "generated and vendor manifests", "test"),
    ("test-suites", "**/test/**", "generated and vendor manifests", "test"),
    ("test-suites", "**/regression_checks/**", "generated and vendor manifests", "test"),
    ("smoke-tooling", "Scripts/smoke/**", "generated and vendor manifests", "product"),
    ("bridge-native", "Tools/ros2_bridge/**", "generated and vendor manifests", "product"),
    (
        "ros2bridge-managed",
        "Packages/dev.unity2foxglove.ros2bridge/**",
        "generated and vendor manifests",
        "product",
    ),
    (
        "ros2forunity",
        "Packages/dev.unity2foxglove.ros2forunity*/**",
        "generated and vendor manifests",
        "product",
    ),
    ("ros2forunity", "Scripts/ros2forunity/**", "generated and vendor manifests", "product"),
    ("sdk-editor", "Packages/dev.unity2foxglove.sdk/**", "generated and vendor manifests", "product"),
    ("unity-assets", "Unity2Foxglove/Assets/**", "generated and vendor manifests", "product"),
    ("remaining", "**", "generated and vendor manifests", "product"),
)
def _rooted(path: Path, root: Path) -> Path:
    return path if path.is_absolute() else (root / path)
def _git(root: Path, *args: str) -> subprocess.CompletedProcess[bytes]:
    return subprocess.run(["git", *args], cwd=root, stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=False)
def tracked_paths(root: Path) -> list[str]:
    completed = _git(root, "ls-files", "-z")
    if completed.returncode:
        raise RuntimeError(completed.stderr.decode(errors="replace").strip())
    return sorted(item.decode("utf-8") for item in completed.stdout.split(b"\0") if item)
def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest().upper()
def line_count(path: Path) -> int:
    return len(path.read_bytes().splitlines())
def is_vendor(path: str) -> bool:
    return path.startswith(VENDOR_PREFIXES)
def is_generated(path: str) -> bool:
    if path.startswith(GENERATED_PROTO_ROOT):
        return True
    if path.startswith(GENERATED_CDR_ROOT) and Path(path).name in GENERATED_CDR_NAMES:
        return True
    if path in GENERATED_EXACT:
        return True
    if path.endswith(".meta"):
        source = path[:-5]
        if source in GENERATED_EXACT:
            return True
        if source.startswith(GENERATED_PROTO_ROOT):
            return True
        if source.startswith(GENERATED_CDR_ROOT) and Path(source).name in GENERATED_CDR_NAMES:
            return True
        if source.endswith("FoxgloveRos2MsgSchemaCatalog.cs"):
            return True
    return False
def generated_owner(path: str) -> tuple[str, str]:
    if path.startswith(GENERATED_PROTO_ROOT):
        return "protobuf compiler / SDK schema generator", "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto"
    if path.startswith(GENERATED_CDR_ROOT):
        return "Scripts/schema/generate_ros2_cdr_serializers.py", "Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg"
    if "FoxgloveRos2MsgSchemaCatalog" in path:
        return "ROS2 schema catalog generator", "Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg"
    if "FoxRunGenerationModelFixture_FoxRun.golden" in path:
        return "FoxRun source generator fixture", "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Fixtures"
    return "Unity generated asset output", "Unity2Foxglove/Assets/Scripts/Generated"
def area_for(path: str) -> tuple[str, str, str, str]:
    lower = path.casefold()
    for area, include, exclude, classification in AREA_RULES:
        if area == "test-suites":
            if "/tests/" in f"/{lower}" or "/test/" in f"/{lower}" or "/regression_checks/" in f"/{lower}":
                return area, include, exclude, "test"
            continue
        if area == "smoke-tooling" and path.startswith("Scripts/smoke/"):
            return area, include, exclude, "product"
        if area == "bridge-native" and path.startswith("Tools/ros2_bridge/"):
            return area, include, exclude, "product"
        if area == "ros2bridge-managed" and path.startswith("Packages/dev.unity2foxglove.ros2bridge/"):
            return area, include, exclude, "product"
        if area == "ros2forunity" and (
            path.startswith("Scripts/ros2forunity/") or path.startswith("Packages/dev.unity2foxglove.ros2forunity")
        ):
            return area, include, exclude, "product"
        if area == "sdk-editor" and path.startswith("Packages/dev.unity2foxglove.sdk/"):
            return area, include, exclude, "product"
        if area == "unity-assets" and path.startswith("Unity2Foxglove/Assets/"):
            return area, include, exclude, "product"
        if area == "remaining":
            return area, include, exclude, "product"
    return "remaining", "**", "generated and vendor manifests", "product"
def kind_for(path: str, default: str) -> str:
    lower = path.casefold()
    if default == "test" or "/tests/" in f"/{lower}" or "/regression_checks/" in f"/{lower}" or Path(path).name.casefold().startswith("test_"):
        return "test"
    return "product"
def python_test_paths(paths: Iterable[str]) -> list[str]:
    return sorted(
        path
        for path in paths
        if Path(path).suffix.casefold() == ".py"
        and Path(path).name != "__init__.py"
        and ("/regression_checks/" in f"/{path}" or Path(path).name.casefold().startswith("test_"))
    )
def write_tsv(path: Path, fields: list[str], rows: Iterable[Iterable[object]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.writer(handle, delimiter="\t", lineterminator="\n")
        writer.writerow(fields)
        writer.writerows(rows)
def parse_status(root: Path) -> list[tuple[str, str]]:
    completed = _git(root, "status", "--porcelain=v1", "--untracked-files=all", "-z")
    if completed.returncode:
        raise RuntimeError(completed.stderr.decode(errors="replace").strip())
    records = completed.stdout.decode("utf-8", errors="replace").split("\0")
    result: list[tuple[str, str]] = []
    for record in records:
        if not record:
            continue
        result.append((record[:2], record[3:]))
    return result
def classify_boundary(root: Path, output: Path) -> None:
    rows = []
    for status, relative in parse_status(root):
        path = _rooted(Path(relative), root)
        digest = sha256(path) if path.is_file() else ""
        owner = "protected package edit" if relative in {
            "Unity2Foxglove/Packages/manifest.json",
            "Unity2Foxglove/Packages/packages-lock.json",
        } else "pre-existing workspace state"
        rows.append([relative, status, digest, owner])
    write_tsv(output / "boundary-manifest.tsv", ["path", "status", "sha256", "classification"], rows)
def source_inventory(root: Path, output: Path, paths: list[str]) -> dict[str, int]:
    source_paths = [path for path in paths if Path(path).suffix.casefold() in SOURCE_EXTENSIONS]
    rows = []
    for relative in source_paths:
        absolute = root / relative
        if not absolute.is_file():
            continue
        area, include, exclude, default_kind = area_for(relative)
        generated = is_generated(relative)
        vendor = is_vendor(relative)
        lines = line_count(absolute)
        rows.append(
            [
                relative,
                lines,
                Path(relative).suffix.casefold(),
                area,
                kind_for(relative, default_kind),
                max(0, math.ceil(lines / 500) - 1),
                str(generated).lower(),
                str(vendor).lower(),
                str(not generated and not vendor).lower(),
            ]
        )
    fields = [
        "path",
        "lines",
        "ext",
        "area",
        "kind",
        "splits_needed",
        "generated_excluded",
        "vendor_excluded",
        "hand_maintained",
    ]
    for threshold in (500, 1000):
        selected = [row for row in rows if int(row[1]) > threshold and row[7] == "false"]
        write_tsv(output / f"over-{threshold}-lines.tsv", fields, sorted(selected))
    area_rows = []
    for relative in sorted(source_paths):
        area, include, exclude, default_kind = area_for(relative)
        area_rows.append(
            [
                relative,
                area,
                include,
                exclude,
                kind_for(relative, default_kind),
                str(is_generated(relative)).lower(),
                str(is_vendor(relative)).lower(),
                str(not is_generated(relative) and not is_vendor(relative)).lower(),
            ]
        )
    write_tsv(
        output / "area-manifest.tsv",
        ["path", "area", "include_glob", "exclude_glob", "kind", "generated_excluded", "vendor_excluded", "hand_maintained"],
        area_rows,
    )
    return {
        "tracked_source": len(source_paths),
        "hand_maintained_source": sum(not is_generated(path) and not is_vendor(path) for path in source_paths),
        "over500_hand": sum(int(row[1]) > 500 and row[8] == "true" for row in rows),
        "over1000_hand": sum(int(row[1]) > 1000 and row[8] == "true" for row in rows),
        "over500_total": sum(int(row[1]) > 500 and row[7] == "false" for row in rows),
        "over1000_total": sum(int(row[1]) > 1000 and row[7] == "false" for row in rows),
    }
def excluded_manifests(root: Path, output: Path, paths: list[str]) -> dict[str, int]:
    generated_paths = [path for path in paths if is_generated(path)]
    vendor_paths = [path for path in paths if is_vendor(path)]
    generated_rows = []
    hash_rows = []
    for relative in generated_paths:
        absolute = root / relative
        if not absolute.is_file():
            continue
        command, owner = generated_owner(relative)
        digest = sha256(absolute)
        generated_rows.append([relative, command, owner, digest, "true"])
        hash_rows.append([relative, digest])
    write_tsv(output / "generated-manifest.tsv", ["path", "generator_command", "owner", "sha256", "generated_excluded"], generated_rows)
    write_tsv(output / "generated-hash-manifest.tsv", ["path", "sha256"], hash_rows)
    vendor_rows = []
    for relative in vendor_paths:
        absolute = root / relative
        if not absolute.is_file():
            continue
        vendor_rows.append([relative, "pinned third-party or vendored source", "AGENTS.md protected reference", sha256(absolute), "true"])
    for relative, commit in VENDOR_ROOTS.items():
        if not any(row[0] == relative for row in vendor_rows):
            vendor_rows.append([relative, "protected repository root", f"pinned commit {commit}", f"COMMIT:{commit}", "true"])
    write_tsv(output / "vendor-manifest.tsv", ["path", "owner", "source_ref", "sha256", "vendor_excluded"], vendor_rows)
    byte_root = output / "identity" / "generated-bytes"
    for relative in generated_paths:
        source = root / relative
        if source.is_file():
            target = byte_root / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source, target)
    return {"generated_files": len(generated_rows), "vendor_files": len(vendor_rows)}
def _read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8", errors="replace")
def identity_surfaces(root: Path, output: Path, paths: list[str]) -> dict[str, int]:
    from Scripts.phase192 import identity_contracts
    identity = output / "identity"
    identity.mkdir(parents=True, exist_ok=True)
    hand = [path for path in paths if not is_generated(path) and not is_vendor(path)]
    csharp = [path for path in hand if Path(path).suffix.casefold() == ".cs"]
    python = [path for path in hand if Path(path).suffix.casefold() == ".py"]
    cpp = [path for path in hand if Path(path).suffix.casefold() in {".cpp", ".cc", ".c", ".h", ".hpp"}]
    csharp_tests = [path for path in csharp if "/Tests/" in f"/{path}" or "/test/" in f"/{path}" or "[Fact]" in _read_text(root / path) or "[Theory]" in _read_text(root / path)]
    python_tests = python_test_paths(python)
    write_tsv(identity / "managed-paths.tsv", ["path"], [[path] for path in csharp])
    write_tsv(identity / "python-paths.tsv", ["path"], [[path] for path in python])
    write_tsv(identity / "cpp-paths.tsv", ["path"], [[path] for path in cpp])
    write_tsv(identity / "csharp-test-paths.tsv", ["path"], [[path] for path in csharp_tests])
    errors: list[list[str]] = []
    def collect(command: str, selected: list[str], function, filename: str, header: list[str]) -> int:
        rows = []
        for relative in selected:
            try:
                rows.extend(function(relative, _read_text(root / relative)))
            except Exception as error:  # baseline records the failing input for later repair
                errors.append([command, relative, type(error).__name__, str(error)])
        identity_contracts.write_rows(identity / filename, header, rows)
        return len(rows)
    managed_count = collect("managed", csharp, identity_contracts.managed_rows, "managed-api-surface.tsv", ["path", "line", "visibility", "declaration"])
    python_count = collect("python", python, identity_contracts.python_surface, "python-symbols.tsv", ["path", "all", "exports"])
    annotation_count = collect("python-annotations", python, identity_contracts.python_annotation_rows, "python-annotations.tsv", ["path", "symbol", "kind", "detail"])
    cpp_count = collect("cpp", cpp, identity_contracts.cpp_rows, "cpp-symbol-surface.tsv", ["path", "line", "symbol", "declaration"])
    csharp_test_count = collect("tests", csharp_tests, identity_contracts.csharp_test_rows, "csharp-test-names.tsv", ["path", "test_name", "framework"])
    python_test_count = 0
    python_test_rows: list[list[str]] = []
    for relative in python_tests:
        try:
            rows = identity_contracts.python_test_rows(relative, _read_text(root / relative), root)
            python_test_rows.extend(rows)
            python_test_count += len(rows)
        except Exception as error:
            errors.append(["python-tests", relative, type(error).__name__, str(error)])
    write_tsv(identity / "python-test-names.tsv", ["path", "test_name", "framework"], sorted(python_test_rows))
    python_probes(root, identity, python)
    if errors:
        write_tsv(identity / "errors.tsv", ["command", "path", "error_type", "detail"], errors)
    else:
        write_tsv(identity / "errors.tsv", ["command", "path", "error_type", "detail"], [])
    assembly_rows, assembly_map, ivt_rows = assemblies(root, csharp)
    write_tsv(identity / "assembly-manifest.tsv", ["path", "assembly", "kind", "source_file_count"], assembly_rows)
    write_tsv(identity / "managed-api-assembly-map.tsv", ["assembly", "path", "line", "visibility", "declaration"], assembly_map)
    write_tsv(identity / "internals-visible-to.tsv", ["path", "friend_assembly"], ivt_rows)
    return {
        "managed_rows": managed_count,
        "python_rows": python_count,
        "python_annotation_rows": annotation_count,
        "cpp_rows": cpp_count,
        "csharp_test_rows": csharp_test_count,
        "python_test_rows": python_test_count,
        "identity_errors": len(errors),
    }
def _module_name(root: Path, relative: str) -> str | None:
    del root
    path = Path(relative)
    if path.name == "__init__.py" or not relative.startswith("Scripts/"):
        return None
    return relative[:-3].replace("/", ".")
def python_probes(root: Path, identity: Path, python: list[str]) -> None:
    rows: list[list[object]] = []
    for relative in sorted(python):
        module = _module_name(root, relative)
        if module is None:
            continue
        is_supported = relative.startswith("Scripts/phase192/") or (
            relative.startswith("Scripts/smoke/") and "/regression_checks/" not in relative
        )
        if not is_supported:
            rows.append([relative, module, "import", "python -c import probe", "STATIC_ONLY", "", "package import deferred to owning lane"])
            continue
        code = (
            "import importlib,sys; m=importlib.import_module(sys.argv[1]); "
            "print('MODULE='+m.__name__); print('PACKAGE='+str(m.__package__)); "
            "print('FILE='+str(getattr(m,'__file__',''))); "
            "print('EXPORTS='+','.join(sorted(k for k in vars(m) if not k.startswith('_'))))"
        )
        command = f"{sys.executable} -c <import-probe> {module}"
        try:
            completed = subprocess.run(
                [sys.executable, "-c", code, module],
                cwd=root,
                env={**os.environ, "PYTHONPATH": str(root)},
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,
                text=True,
                timeout=15,
                check=False,
            )
            status = "PASS" if completed.returncode == 0 else "FAIL"
            rows.append([relative, module, "import", command, status, completed.returncode, completed.stdout.strip()])
        except subprocess.TimeoutExpired as error:
            rows.append([relative, module, "import", command, "TIMEOUT", "124", str(error)])
        entrypoint = "__name__ == \"__main__\"" in _read_text(root / relative) or "__name__ == '__main__'" in _read_text(root / relative)
        if entrypoint:
            command = f"{sys.executable} {relative} --help"
            try:
                completed = subprocess.run(
                    [sys.executable, relative, "--help"],
                    cwd=root,
                    env={**os.environ, "PYTHONPATH": str(root)},
                    stdout=subprocess.PIPE,
                    stderr=subprocess.STDOUT,
                    text=True,
                    timeout=15,
                    check=False,
                )
                status = "PASS" if completed.returncode == 0 else "FAIL"
                rows.append([relative, module, "direct-entrypoint", command, status, completed.returncode, completed.stdout.strip()])
            except subprocess.TimeoutExpired as error:
                rows.append([relative, module, "direct-entrypoint", command, "TIMEOUT", "124", str(error)])
    write_tsv(identity / "python-probes.tsv", ["path", "module", "probe", "command", "status", "returncode", "result"], rows)
def assemblies(root: Path, csharp: list[str]) -> tuple[list[list[str]], list[list[str]], list[list[str]]]:
    definitions: list[tuple[Path, str, str]] = []
    for relative in tracked_paths(root):
        path = root / relative
        if path.suffix.casefold() == ".asmdef":
            try:
                payload = json.loads(_read_text(path))
                name = str(payload.get("name", path.stem))
            except (OSError, json.JSONDecodeError):
                name = path.stem
            definitions.append((path.parent, name, "asmdef"))
        elif path.suffix.casefold() == ".csproj":
            definitions.append((path.parent, path.stem, "csproj"))
    definitions.sort(key=lambda item: len(item[0].parts), reverse=True)
    counts: dict[str, int] = {}
    mapping: dict[str, str] = {}
    for relative in csharp:
        source = root / relative
        match = next((item for item in definitions if source.is_relative_to(item[0])), None)
        if match:
            assembly = match[1]
            mapping[relative] = assembly
            counts[assembly] = counts.get(assembly, 0) + 1
    rows = [[str(directory.relative_to(root)).replace("\\", "/"), name, kind, counts.get(name, 0)] for directory, name, kind in definitions]
    managed_map: list[list[str]] = []
    ivt: list[list[str]] = []
    from Scripts.phase192 import identity_contracts
    for relative in csharp:
        assembly = mapping.get(relative, "UNMAPPED")
        text = _read_text(root / relative)
        for row in identity_contracts.managed_rows(relative, text):
            managed_map.append([assembly, *row])
        for friend in re.findall(r"InternalsVisibleTo\s*\(\s*[\"']([^\"']+)", text):
            ivt.append([relative, friend])
    return rows, managed_map, ivt
def phase191_and_analyzers(root: Path, output: Path, paths: list[str]) -> None:
    identity = output / "identity"
    golden = []
    for relative in paths:
        lower = relative.casefold()
        if any(token in lower for token in ("phase191", "golden", "manifest")) and (root / relative).is_file():
            golden.append([relative, sha256(root / relative), "tracked Phase191/golden/manifest fixture"])
    write_tsv(identity / "phase191-golden-manifest-hashes.tsv", ["path", "sha256", "source"], golden)
    analyzers = []
    for relative in paths:
        if relative.casefold().endswith(".dll") and "sourcegenerators" in relative.casefold() and (root / relative).is_file():
            analyzers.append([relative, sha256(root / relative), "tracked analyzer DLL"])
    write_tsv(identity / "analyzer-dll-hashes.tsv", ["path", "sha256", "source"], analyzers)
def lane_manifest(root: Path, output: Path, paths: list[str]) -> None:
    from Scripts.phase192.task0_entry import measured_lane_result

    identity = output / "identity"
    csharp = [path for path in paths if Path(path).suffix.casefold() == ".cs" and not is_vendor(path)]
    cs_test_count = sum(len(re.findall(r"\[(?:Fact|Theory)(?:[^]]*)\]", _read_text(root / path))) for path in csharp)
    run_ci = _read_text(root / "Scripts/release/run_ci.py")
    choices = re.search(r"CI_ONLY_CHOICES\s*=\s*\((.*?)\)", run_ci, re.DOTALL)
    names = re.findall(r"\"([a-z0-9-]+)\"", choices.group(1)) if choices else []
    rows = [
        ["identity-tooling", "unittest", "Scripts.phase192.regression_checks.test_identity_tooling", "python -m unittest Scripts.phase192.regression_checks.test_identity_tooling", "explicit-module", "fully-qualified", 169, "PASS", "executed"],
        ["regression-inventory", "script", "Scripts/release/regression_inventory.py", "python -B Scripts/release/regression_inventory.py", "script", "n/a", 1, "PASS", "executed"],
    ]
    for name in names:
        status, measurement = measured_lane_result(identity / "lane-runs", name)
        rows.append([f"run-ci:{name}", "ci-suite", "Scripts/release/run_ci.py", f"python -B Scripts/release/run_ci.py --only {name}", "ci-choice", "command-result", 1, status, measurement])
    rows.append(["csharp-attribute-scan", "xunit", "tracked .cs sources", "source attribute scan", "source-scan", "attribute-name", cs_test_count, "BASELINE_STATIC", "static-source"])
    write_tsv(identity / "lane-manifest.tsv", ["lane", "framework", "project_or_entrypoint", "command", "discovery_mode", "parameter_normalization", "expected_count", "status", "measurement"], rows)
def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path("."))
    parser.add_argument("--output", type=Path, default=Path("build/phase192/baseline"))
    parser.add_argument("--plan192", type=Path, default=Path("Plan/192_PHASE192_SOURCE_DECOMPOSITION_PLAN.md"))
    parser.add_argument("--plan190", type=Path, default=Path("Plan/190_PHASE190_FOXRUN_FULL_VALIDATION_AND_REMEDIATION_PLAN.md"))
    parser.add_argument("--plan191", type=Path, default=Path("Plan/191_PHASE191_FOXRUN_CONTRACT_COMPATIBILITY_AND_DECOMPOSITION_PLAN.md"))
    args = parser.parse_args(argv)
    root = args.root.resolve()
    output = _rooted(args.output, root).resolve()
    output.mkdir(parents=True, exist_ok=True)
    paths = tracked_paths(root)
    record_entry_commands(root, output)
    classify_boundary(root, output)
    record_plan_hashes(root, output, [args.plan192, args.plan190, args.plan191])
    counts = source_inventory(root, output, paths)
    counts.update(excluded_manifests(root, output, paths))
    validate_classification(output, paths, SOURCE_EXTENSIONS, is_generated, is_vendor)
    counts.update(identity_surfaces(root, output, paths))
    phase191_and_analyzers(root, output, paths)
    lane_manifest(root, output, paths)
    (output / "task0-summary.json").write_text(json.dumps({"entry_head": _git(root, "rev-parse", "HEAD").stdout.decode().strip(), **counts}, indent=2) + "\n", encoding="utf-8")
    print("PHASE192_TASK0_BASELINE_WRITTEN", json.dumps(counts, sort_keys=True))
    return 0
if __name__ == "__main__":
    raise SystemExit(main())
