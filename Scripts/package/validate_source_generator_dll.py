#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Rebuild the checked-in Roslyn source generator DLL and verify freshness.
# Usage: python Scripts/package/validate_source_generator_dll.py
#        python Scripts/package/validate_source_generator_dll.py --update

"""Validate that the checked-in source generator DLL matches a fresh Release build."""

from __future__ import annotations

import argparse
import hashlib
import re
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
PROJECT = REPO_ROOT / "Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/FoxgloveLogSourceGenerator.csproj"
ANALYZER_DIRECTORY = REPO_ROOT / "Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/analyzers/dotnet/cs"
UNITY_PLUGIN_GOOGLE_PROTOBUF = REPO_ROOT / "Packages/dev.unity2foxglove.sdk/Plugins/Google.Protobuf/Google.Protobuf.dll"
CHECKED_IN_ARTIFACTS = {
    "FoxgloveLogSourceGenerator.dll": ANALYZER_DIRECTORY / "FoxgloveLogSourceGenerator.dll",
    "Google.Protobuf.dll": ANALYZER_DIRECTORY / "Google.Protobuf.dll",
}
BUILD_OUTPUT_DIR = REPO_ROOT / "build/SourceGenerators/Release/netstandard2.0"


@dataclass(frozen=True)
class AnalyzerTarget:
    """One independently packaged controlled analyzer."""

    name: str
    project: Path
    checked_in_artifacts: dict[str, Path]
    build_output_dir: Path
    validate_runtime_protobuf: bool = False


TARGETS = {
    "core": AnalyzerTarget(
        "core",
        PROJECT,
        CHECKED_IN_ARTIFACTS,
        BUILD_OUTPUT_DIR,
        validate_runtime_protobuf=True,
    ),
    "r2fu": AnalyzerTarget(
        "r2fu",
        REPO_ROOT
        / "Packages/dev.unity2foxglove.ros2forunity/Editor/SourceGenerators/FoxRunR2fuSourceGenerator.csproj",
        {
            "Unity2Foxglove.Ros2ForUnity.FoxRunSourceGenerator.dll":
                REPO_ROOT
                / "Packages/dev.unity2foxglove.ros2forunity/Editor/SourceGenerators/analyzers/dotnet/cs/Unity2Foxglove.Ros2ForUnity.FoxRunSourceGenerator.dll",
        },
        REPO_ROOT / "build/SourceGenerators/R2FU/validator/Release/netstandard2.0",
    ),
    "ros2bridge": AnalyzerTarget(
        "ros2bridge",
        REPO_ROOT
        / "Packages/dev.unity2foxglove.ros2bridge/Editor/SourceGenerators/FoxRunBridgeSourceGenerator.csproj",
        {
            "Unity2Foxglove.Ros2Bridge.FoxRunSourceGenerator.dll":
                REPO_ROOT
                / "Packages/dev.unity2foxglove.ros2bridge/Editor/SourceGenerators/analyzers/dotnet/cs/Unity2Foxglove.Ros2Bridge.FoxRunSourceGenerator.dll",
        },
        REPO_ROOT / "build/SourceGenerators/Ros2Bridge/validator/Release/netstandard2.0",
    ),
}

PROVIDER_DEPENDENCIES = {
    "Microsoft.CodeAnalysis.Analyzers",
    "Microsoft.CodeAnalysis.CSharp",
}
PACKAGE_ROOTS = {
    name: target.project.parents[2].resolve()
    for name, target in TARGETS.items()
}
DIAGNOSTIC_PREFIXES = {
    "core": ("FOXRUN", 0, 699),
    "r2fu": ("FOXR2F", 0, 199),
    "ros2bridge": ("FOXBRG", 0, 199),
}
HINT_CONTRACTS = {
    "core": (
        REPO_ROOT
        / "Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/FoxgloveSourceEmitter.cs",
        "_FoxRun.g.cs",
    ),
    "r2fu": (
        REPO_ROOT
        / "Packages/dev.unity2foxglove.ros2forunity/Editor/SourceGenerators/src/FoxRunR2fuAnalyzerPipeline.cs",
        "_unity2foxglove_r2fu_typed_ros2_FoxRun.g.cs",
    ),
    "ros2bridge": (
        REPO_ROOT
        / "Packages/dev.unity2foxglove.ros2bridge/Editor/FoxRun/Ros2CustomCdrEmitter.cs",
        "_unity2foxglove_ros2bridge_typed_cdr_FoxRun.g.cs",
    ),
}
PHYSICAL_HINT_CONTRACTS = {
    "r2fu": (
        REPO_ROOT
        / "Packages/dev.unity2foxglove.ros2forunity/Editor/Native/FoxRunR2fuEmitterContribution.cs",
        'HintNameSuffix => "typed-ros2"',
    ),
    "ros2bridge": (
        REPO_ROOT
        / "Packages/dev.unity2foxglove.ros2bridge/Editor/FoxRun/FoxRunBridgeEmitterContribution.cs",
        'HintNameSuffix => "typed-cdr"',
    ),
}
COMPOSITION_TEST = (
    REPO_ROOT
    / "Packages/dev.unity2foxglove.sdk/Tests/Unit/Ros2ForUnity/"
      "FoxRunAnalyzerCompositionContractTests.cs"
)
EXACT_SHARED_SOURCE_GROUPS = (
    (
        "Identifier utility",
        (
            REPO_ROOT
            / "Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/IdentifierUtils.cs",
            REPO_ROOT
            / "Packages/dev.unity2foxglove.ros2forunity/Editor/SourceGenerators/src/Shared/IdentifierUtils.cs",
            REPO_ROOT
            / "Packages/dev.unity2foxglove.ros2bridge/Editor/SourceGenerators/src/Shared/IdentifierUtils.cs",
        ),
    ),
    (
        "Roslyn type-shape builder",
        (
            REPO_ROOT
            / "Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/src/FoxRunRoslynTypeShapeBuilder.cs",
            REPO_ROOT
            / "Packages/dev.unity2foxglove.ros2forunity/Editor/SourceGenerators/src/Shared/FoxRunRoslynTypeShapeBuilder.cs",
            REPO_ROOT
            / "Packages/dev.unity2foxglove.ros2bridge/Editor/SourceGenerators/src/Shared/FoxRunRoslynTypeShapeBuilder.cs",
        ),
    ),
    (
        "Provider type shape",
        (
            REPO_ROOT
            / "Packages/dev.unity2foxglove.ros2forunity/Editor/SourceGenerators/src/Shared/FoxRunTypeShape.cs",
            REPO_ROOT
            / "Packages/dev.unity2foxglove.ros2bridge/Editor/SourceGenerators/src/Shared/FoxRunTypeShape.cs",
        ),
    ),
)
PROVIDER_TYPE_SHAPE = (
    REPO_ROOT
    / "Packages/dev.unity2foxglove.ros2bridge/Editor/SourceGenerators/src/Shared/FoxRunTypeShape.cs"
)
CORE_TYPE_SHAPE = (
    REPO_ROOT
    / "Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunTypeShape.cs"
)
_CORE_TYPE_SHAPE_EXTENSION = (
    "\n    internal static class FoxRunLogicalSchemaNameResolver\n"
)


def sha256(path: Path) -> str:
    """Return the SHA-256 hex digest for a file."""
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _shared_core_type_shape(text: str) -> str | None:
    """Remove the one intentional core-only schema-name helper."""
    if text.count(_CORE_TYPE_SHAPE_EXTENSION) != 1:
        return None
    shared, _extension = text.split(
        _CORE_TYPE_SHAPE_EXTENSION,
        1,
    )
    return shared.rstrip() + "\n}\n"


def validate_shared_source_parity() -> bool:
    """Fail when independently packaged shared analyzer semantics drift."""
    failures: list[str] = []
    for label, paths in EXACT_SHARED_SOURCE_GROUPS:
        contents: list[str] = []
        for path in paths:
            try:
                contents.append(path.read_text(encoding="utf-8"))
            except (OSError, UnicodeError) as exc:
                failures.append(f"{label}: cannot read {path}: {exc}")
        if len(contents) == len(paths) and any(
            content != contents[0]
            for content in contents[1:]
        ):
            failures.append(f"{label}: packaged copies differ")

    try:
        provider = PROVIDER_TYPE_SHAPE.read_text(encoding="utf-8")
        core = CORE_TYPE_SHAPE.read_text(encoding="utf-8")
    except (OSError, UnicodeError) as exc:
        failures.append(f"type shape: cannot read shared sources: {exc}")
    else:
        normalized_core = _shared_core_type_shape(core)
        if normalized_core is None:
            failures.append(
                "type shape: core-only extension boundary differs"
            )
        elif normalized_core != provider:
            failures.append(
                "type shape: core and Provider shared semantics differ"
            )

    if failures:
        for failure in failures:
            print(
                f"[FAIL] Analyzer shared source parity: {failure}",
                file=sys.stderr,
            )
        return False
    print("[PASS] Analyzer shared semantic sources are in parity.")
    return True


def run_build(
    build_output_dir: Path = BUILD_OUTPUT_DIR,
    msbuild_props: list[str] | None = None,
    project: Path = PROJECT,
) -> bool:
    """Build the source generator project in Release mode."""
    msbuild_props = msbuild_props or []
    command = [
        "dotnet",
        "build",
        str(project),
        *msbuild_props,
        "-c",
        "Release",
        "-o",
        str(build_output_dir),
        "-v:minimal",
    ]
    try:
        subprocess.run(command, cwd=REPO_ROOT, check=True)
    except subprocess.CalledProcessError as exc:
        print(f"[FAIL] Source generator Release build failed with exit code {exc.returncode}.", file=sys.stderr)
        print(f"       command: {' '.join(command)}", file=sys.stderr)
        return False
    except FileNotFoundError:
        print(
            "[FAIL] Source generator Release build failed: "
            "dotnet executable is unavailable.",
            file=sys.stderr,
        )
        return False
    return True


def validate_unity_plugin_protobuf_match(analyzer_dependency: Path) -> bool:
    """Ensure the Unity runtime plug-in matches the supplied Protobuf dependency exactly."""
    if not UNITY_PLUGIN_GOOGLE_PROTOBUF.exists():
        print(
            "[FAIL] Unity runtime Google.Protobuf plug-in is missing: "
            f"{UNITY_PLUGIN_GOOGLE_PROTOBUF}",
            file=sys.stderr,
        )
        return False

    if sha256(analyzer_dependency) != sha256(UNITY_PLUGIN_GOOGLE_PROTOBUF):
        print(
            "[FAIL] Unity runtime Google.Protobuf plug-in differs from checked-in analyzer dependency.",
            file=sys.stderr,
        )
        print(f"       analyzer: {analyzer_dependency} sha256={sha256(analyzer_dependency)}", file=sys.stderr)
        print(
            f"       runtime:  {UNITY_PLUGIN_GOOGLE_PROTOBUF} "
            f"sha256={sha256(UNITY_PLUGIN_GOOGLE_PROTOBUF)}",
            file=sys.stderr,
        )
        return False

    return True


def _normalize_compile_include(include: str) -> str:
    """Normalize an MSBuild Compile Include for the current filesystem host."""
    return include.replace("\\", "/")


def _project_sources(project: Path) -> list[Path]:
    """Resolve every explicit Compile item in one controlled analyzer project."""
    root = ET.parse(project).getroot()
    sources: list[Path] = []
    for node in root.findall(".//Compile"):
        for include in node.attrib.get("Include", "").split(";"):
            include = include.strip()
            if include:
                normalized = _normalize_compile_include(include)
                if "*" in normalized or "?" in normalized:
                    matches = [
                        path.resolve()
                        for path in project.parent.glob(
                            normalized
                        )
                        if path.is_file()
                    ]
                    if not matches:
                        raise ValueError(
                            f"{project}: Compile glob matched no files: "
                            f"{include}"
                        )
                    sources.extend(matches)
                else:
                    sources.append(
                        (project.parent / normalized).resolve()
                    )
    return sources


def _strip_csharp_comments_and_literals(source: str) -> str:
    """Replace comments and literals while preserving executable structure."""
    output: list[str] = []
    index = 0
    state = "code"
    while index < len(source):
        current = source[index]
        following = source[index + 1] if index + 1 < len(source) else ""
        if state == "code":
            if current == "/" and following == "/":
                output.extend((" ", " "))
                index += 2
                state = "line-comment"
                continue
            if current == "/" and following == "*":
                output.extend((" ", " "))
                index += 2
                state = "block-comment"
                continue
            if current == '"':
                output.append(" ")
                index += 1
                state = "verbatim-string" if index >= 2 and source[index - 2] == "@" else "string"
                continue
            if current == "'":
                output.append(" ")
                index += 1
                state = "character"
                continue
            output.append(current)
            index += 1
            continue
        if current == "\n":
            output.append("\n")
            index += 1
            if state == "line-comment":
                state = "code"
            continue
        if state == "block-comment" and current == "*" and following == "/":
            output.extend((" ", " "))
            index += 2
            state = "code"
            continue
        if state == "verbatim-string" and current == '"':
            if following == '"':
                output.extend((" ", " "))
                index += 2
                continue
            output.append(" ")
            index += 1
            state = "code"
            continue
        if state in {"string", "character"} and current == "\\":
            output.append(" ")
            if following:
                output.append("\n" if following == "\n" else " ")
                index += 2
            else:
                index += 1
            continue
        if state == "string" and current == '"':
            output.append(" ")
            index += 1
            state = "code"
            continue
        if state == "character" and current == "'":
            output.append(" ")
            index += 1
            state = "code"
            continue
        output.append(" ")
        index += 1
    return "".join(output)


def _method_body(source: str, method_name: str) -> str | None:
    """Return one balanced C# method body from sanitized source."""
    match = re.search(
        rf"\b{re.escape(method_name)}\s*\([^)]*\)\s*\{{",
        source,
    )
    if match is None:
        return None
    opening = source.find("{", match.start())
    depth = 0
    for index in range(opening, len(source)):
        if source[index] == "{":
            depth += 1
        elif source[index] == "}":
            depth -= 1
            if depth == 0:
                return source[opening + 1:index]
    return None


def _active_test_attributes(source: str, method_name: str) -> str | None:
    """Return attributes immediately attached to one public test method."""
    match = re.search(
        rf"(?P<attributes>(?:\s*\[[^\]]+\])+\s*)"
        rf"public\s+void\s+{re.escape(method_name)}\s*\(",
        source,
    )
    return match.group("attributes") if match is not None else None


def _composition_contract_failures(source: str) -> list[str]:
    """Return missing active composition-test contracts."""
    code = _strip_csharp_comments_and_literals(source)
    failures: list[str] = []
    body = _method_body(code, "AnalyzerSets") or ""
    required_calls = (
        "CoreOnly()",
        "R2fuOnly()",
        "BridgeOnly()",
        "AllProviders()",
    )
    if any(call not in body for call in required_calls):
        failures.append("active analyzer-set calls")

    theory = _active_test_attributes(
        code,
        "IndependentAnalyzerSetsEmitOnlyOwnedUniqueHints",
    ) or ""
    if (
        "[Theory]" not in theory
        or "[MemberData(nameof(AnalyzerSets))]" not in theory
        or "Skip" in theory
    ):
        failures.append("active analyzer-set theory")

    parity = _active_test_attributes(
        code,
        "PhysicalAndRoslynProviderEmittersStayEquivalent",
    ) or ""
    if "[Fact]" not in parity or "Skip" in parity:
        failures.append("active physical/Roslyn parity fact")
    return failures


def _ledger_ids(project: Path) -> set[str]:
    """Return diagnostic IDs declared by both shipped and unshipped ledgers."""
    result: set[str] = set()
    for ledger in project.parent.glob("AnalyzerReleases.*.md"):
        result.update(
            re.findall(
                r"\b(?:FOXRUN|FOXR2F|FOXBRG)\d{3}\b",
                ledger.read_text(encoding="utf-8"),
            )
        )
    return result


def _provider_descriptor_ids(sources: list[Path], prefix: str) -> set[str]:
    """Return Provider-owned descriptor IDs created by compiled diagnostic sources."""
    ids: set[str] = set()
    pattern = re.compile(rf'\b({re.escape(prefix)}\d{{3}})\b')
    for source in sources:
        if "Diagnostic" not in source.name or not source.exists():
            continue
        ids.update(pattern.findall(source.read_text(encoding="utf-8")))
    return ids


def validate_analyzer_contracts(target_names: tuple[str, ...]) -> bool:
    """Validate independent packaging, ledgers, IDs, hint parity, and set coverage."""
    if not validate_shared_source_parity():
        return False
    failures: list[str] = []
    assembly_names: dict[str, str] = {}
    ledger_owners: dict[str, str] = {}
    hint_tokens: dict[str, str] = {}

    for name in target_names:
        if name not in TARGETS:
            failures.append(f"unknown analyzer target: {name}")
            continue
        target = TARGETS[name]
        if not target.project.exists():
            failures.append(f"{name}: project missing: {target.project}")
            continue

        project_xml = ET.parse(target.project).getroot()
        assembly_node = project_xml.find(".//AssemblyName")
        assembly_name = (
            assembly_node.text.strip()
            if assembly_node is not None and assembly_node.text
            else target.project.stem
        )
        if assembly_name in assembly_names:
            failures.append(
                f"{name}: duplicate analyzer assembly name {assembly_name} "
                f"also owned by {assembly_names[assembly_name]}"
            )
        assembly_names[assembly_name] = name

        for artifact in target.checked_in_artifacts.values():
            if artifact.suffix.lower() != ".dll":
                continue
            meta = Path(str(artifact) + ".meta")
            if not meta.exists():
                failures.append(
                    f"{name}: analyzer .meta missing: "
                    f"{meta.relative_to(REPO_ROOT)}"
                )

        try:
            sources = _project_sources(target.project)
        except (OSError, ET.ParseError, ValueError) as exc:
            failures.append(f"{name}: cannot resolve compiled sources: {exc}")
            sources = []
        missing_sources = [
            source for source in sources if not source.exists()
        ]
        for source in missing_sources:
            failures.append(
                f"{name}: compiled source missing: {source}"
            )

        if name != "core":
            dependencies = {
                node.attrib.get("Include", "")
                for node in project_xml.findall(
                    ".//PackageReference"
                )
            }
            if dependencies != PROVIDER_DEPENDENCIES:
                failures.append(
                    f"{name}: dependencies must be Roslyn-only; "
                    f"found {sorted(dependencies)}"
                )
            if project_xml.findall(".//ProjectReference"):
                failures.append(
                    f"{name}: ProjectReference is forbidden"
                )
            if project_xml.findall(".//Reference"):
                failures.append(
                    f"{name}: assembly Reference is forbidden"
                )
            package_root = PACKAGE_ROOTS[name]
            for node in project_xml.findall(".//Compile"):
                includes = node.attrib.get("Include", "").split(";")
                for include in includes:
                    include = include.strip()
                    if not include:
                        continue
                    normalized = _normalize_compile_include(include)
                    if "*" in normalized:
                        failures.append(
                            f"{name}: wildcard Compile item is forbidden: "
                            f"{include}"
                        )
                        continue
                    source = (
                        target.project.parent / normalized
                    ).resolve()
                    if not source.is_relative_to(package_root):
                        failures.append(
                            f"{name}: non-owned compiled source: "
                            f"{include}"
                        )

        prefix, minimum, maximum = DIAGNOSTIC_PREFIXES[name]
        ledgers = _ledger_ids(target.project)
        for diagnostic_id in ledgers:
            match = re.fullmatch(
                rf"{re.escape(prefix)}(\d{{3}})",
                diagnostic_id,
            )
            if not match:
                failures.append(
                    f"{name}: out-of-namespace ledger ID "
                    f"{diagnostic_id}"
                )
                continue
            number = int(match.group(1))
            if not minimum <= number <= maximum:
                failures.append(
                    f"{name}: out-of-range ledger ID "
                    f"{diagnostic_id}"
                )
            previous = ledger_owners.get(diagnostic_id)
            if previous is not None and previous != name:
                failures.append(
                    f"{name}: diagnostic ID {diagnostic_id} "
                    f"duplicates {previous}"
                )
            ledger_owners[diagnostic_id] = name

        if name != "core":
            source_ids = _provider_descriptor_ids(
                sources,
                prefix,
            )
            undeclared = source_ids - ledgers
            if undeclared:
                failures.append(
                    f"{name}: diagnostic IDs missing from release "
                    f"ledger: {sorted(undeclared)}"
                )

        hint_source, hint_token = HINT_CONTRACTS[name]
        if not hint_source.exists() or hint_token not in (
            hint_source.read_text(encoding="utf-8")
            if hint_source.exists()
            else ""
        ):
            failures.append(
                f"{name}: analyzer hint contract missing "
                f"{hint_token}"
            )
        if hint_token in hint_tokens:
            failures.append(
                f"{name}: hint namespace duplicates "
                f"{hint_tokens[hint_token]}"
            )
        hint_tokens[hint_token] = name

        if name in PHYSICAL_HINT_CONTRACTS:
            physical_source, physical_token = (
                PHYSICAL_HINT_CONTRACTS[name]
            )
            physical_text = (
                physical_source.read_text(encoding="utf-8")
                if physical_source.exists()
                else ""
            )
            if physical_token not in physical_text:
                failures.append(
                    f"{name}: physical hint contract missing "
                    f"{physical_token}"
                )
            normalized = (
                physical_token.split('"')[1]
                .replace("-", "_")
            )
            if normalized not in hint_token:
                failures.append(
                    f"{name}: Roslyn/physical hint mismatch: "
                    f"{physical_token} vs {hint_token}"
                )

    if set(target_names) == set(TARGETS):
        composition_text = (
            COMPOSITION_TEST.read_text(encoding="utf-8")
            if COMPOSITION_TEST.exists()
            else ""
        )
        for missing in _composition_contract_failures(composition_text):
            failures.append(
                "all: analyzer composition/parity test lacks "
                f"{missing}"
            )

    if failures:
        for failure in failures:
            print(
                f"[FAIL] Analyzer contract: {failure}",
                file=sys.stderr,
            )
        return False

    print(
        "[PASS] Analyzer packaging, dependencies, ledgers, IDs, "
        "hint parity, and composition-set contracts are locked."
    )
    return True


def run_analyzer_composition_tests(msbuild_props: list[str]) -> bool:
    """Execute the four analyzer sets plus physical/Roslyn parity fixture."""
    command = [
        "dotnet",
        "test",
        str(
            REPO_ROOT
            / "Packages/dev.unity2foxglove.sdk/Tests/Unit/"
              "FoxgloveSdk.UnitTests.csproj"
        ),
        "--no-restore",
        "-p:IncludeRos2ForUnityNative=true",
        "-p:IncludeRos2Bridge=true",
        *msbuild_props,
        "--filter",
        "FullyQualifiedName~FoxRunAnalyzerCompositionContractTests",
        "--verbosity",
        "minimal",
    ]
    try:
        subprocess.run(
            command,
            cwd=REPO_ROOT,
            check=True,
        )
    except subprocess.CalledProcessError as exc:
        print(
            "[FAIL] Analyzer composition/parity tests failed "
            f"with exit code {exc.returncode}.",
            file=sys.stderr,
        )
        return False
    except FileNotFoundError:
        print(
            "[FAIL] Analyzer composition/parity tests failed: "
            "dotnet executable is unavailable.",
            file=sys.stderr,
        )
        return False
    return True


def validate_or_update(
    update: bool,
    build_output_dir: Path,
    msbuild_props: list[str],
    target: str = "core",
) -> int:
    """Validate or update the checked-in analyzer assembly and its dependencies."""
    selected = TARGETS[target]
    project = selected.project
    checked_in_artifacts = (
        CHECKED_IN_ARTIFACTS
        if target == "core"
        else selected.checked_in_artifacts
    )
    if not project.exists():
        print(f"[FAIL] Source generator project missing: {project}", file=sys.stderr)
        return 1

    if not run_build(build_output_dir, msbuild_props, project):
        return 1

    built_artifacts = {}
    for name in checked_in_artifacts:
        built = build_output_dir / name
        if not built.exists():
            print(f"[FAIL] Release build did not produce {built}", file=sys.stderr)
            return 1
        built_artifacts[name] = built

    if update:
        if (selected.validate_runtime_protobuf
                and not validate_unity_plugin_protobuf_match(
                    built_artifacts["Google.Protobuf.dll"])):
            return 1
        for name, checked_in in checked_in_artifacts.items():
            shutil.copy2(built_artifacts[name], checked_in)
            print(f"[PASS] Updated checked-in source generator artifact: {checked_in.relative_to(REPO_ROOT)}")
            print(f"       sha256={sha256(checked_in)}")
        return 0

    for name, checked_in in checked_in_artifacts.items():
        if not checked_in.exists():
            print(f"[FAIL] Checked-in analyzer artifact missing: {checked_in.relative_to(REPO_ROOT)}", file=sys.stderr)
            return 1

        built_hash = sha256(built_artifacts[name])
        checked_hash = sha256(checked_in)
        if built_hash != checked_hash:
            print(f"[FAIL] Checked-in source generator artifact is stale: {name}", file=sys.stderr)
            print(
                f"       built:   {built_artifacts[name].relative_to(REPO_ROOT)} sha256={built_hash}",
                file=sys.stderr,
            )
            print(f"       checked: {checked_in.relative_to(REPO_ROOT)} sha256={checked_hash}", file=sys.stderr)
            print("       Run: python Scripts/package/validate_source_generator_dll.py --update", file=sys.stderr)
            return 1

    if (selected.validate_runtime_protobuf
            and not validate_unity_plugin_protobuf_match(
                checked_in_artifacts["Google.Protobuf.dll"])):
        return 1

    print(
        f"[PASS] Checked-in {selected.name} source generator artifacts "
        "match a fresh Release build."
    )
    for checked_in in checked_in_artifacts.values():
        print(f"       {checked_in.name}: sha256={sha256(checked_in)}")
    return 0


def main() -> int:
    """Parse command-line arguments and return a process exit code."""
    parser = argparse.ArgumentParser(description="Validate the checked-in source generator DLL.")
    parser.add_argument(
        "--target",
        choices=("all", *tuple(TARGETS)),
        default="all",
        help="Controlled analyzer package(s) to build and validate.",
    )
    parser.add_argument(
        "--update",
        action="store_true",
        help="Copy the fresh Release build over the checked-in analyzer DLL.",
    )
    parser.add_argument(
        "--build-output-dir",
        type=Path,
        default=None,
        help="Directory for the fresh Release build output.",
    )
    parser.add_argument(
        "--msbuild-prop",
        action="append",
        default=[],
        help="Additional MSBuild property argument to pass to dotnet build, such as -p:BaseOutputPath=...",
    )
    args = parser.parse_args()
    selected_names = (
        tuple(TARGETS)
        if args.target == "all"
        else (args.target,)
    )
    if not validate_analyzer_contracts(selected_names):
        return 1

    for name in selected_names:
        build_output_dir = (
            args.build_output_dir / name
            if args.build_output_dir is not None
            and args.target == "all"
            else args.build_output_dir
            if args.build_output_dir is not None
            else TARGETS[name].build_output_dir
        )
        result = validate_or_update(
            args.update,
            build_output_dir,
            args.msbuild_prop,
            name,
        )
        if result != 0:
            return result

    if args.target == "all" and not run_analyzer_composition_tests(
        args.msbuild_prop
    ):
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
