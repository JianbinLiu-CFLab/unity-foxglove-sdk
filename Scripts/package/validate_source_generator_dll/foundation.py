"""Shared paths and contracts for source-generator validation."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
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

META_GUID_PATTERN = re.compile(r"(?mi)^guid:\s*([0-9a-f]{32})\s*$")


def _validate_analyzer_meta(meta: Path, owner: str, seen_guids: dict[str, str]) -> list[str]:
    """Validate Unity analyzer meta identity, importer kind, and GUID uniqueness."""
    failures: list[str] = []
    if not meta.is_file():
        return [f"{owner}: analyzer .meta missing: {meta if not meta.is_relative_to(REPO_ROOT) else meta.relative_to(REPO_ROOT)}"]
    try:
        text = meta.read_text(encoding="utf-8")
    except OSError as exc:
        return [f"{owner}: analyzer .meta unreadable: {exc}"]
    match = META_GUID_PATTERN.search(text)
    if match is None:
        failures.append(f"{owner}: analyzer .meta GUID is missing or malformed: {meta if not meta.is_relative_to(REPO_ROOT) else meta.relative_to(REPO_ROOT)}")
    else:
        guid = match.group(1).lower()
        prior = seen_guids.get(guid)
        if prior is not None:
            failures.append(f"{owner}: analyzer .meta GUID duplicates {prior}: {meta if not meta.is_relative_to(REPO_ROOT) else meta.relative_to(REPO_ROOT)}")
        else:
            seen_guids[guid] = owner
    if "PluginImporter:" not in text:
        failures.append(f"{owner}: analyzer .meta lacks PluginImporter: {meta if not meta.is_relative_to(REPO_ROOT) else meta.relative_to(REPO_ROOT)}")
    if not re.search(r"(?m)^-\s+RoslynAnalyzer\s*$", text):
        failures.append(f"{owner}: analyzer .meta lacks RoslynAnalyzer label: {meta if not meta.is_relative_to(REPO_ROOT) else meta.relative_to(REPO_ROOT)}")
    return failures


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
# Core-tested sources copied verbatim into the provider analyzers: (label, core path, ros2bridge copy exists).
_VERBATIM_PROVIDER_COPIES = (
    ("Canonical type normalizer", "Editor/Shared/FoxRunDescriptor/FoxRunCanonicalTypeNormalizer.cs", True),
    ("Emission type name formatter", "Editor/Shared/FoxRunDescriptor/FoxRunEmissionTypeNameFormatter.cs", True),
    ("Generation descriptor constants", "Editor/Shared/FoxRunDescriptor/FoxRunGenerationDescriptorConstants.cs", True),
    ("Generation model validator", "Editor/Shared/FoxRunDescriptor/FoxRunGenerationModelValidator.cs", True),
    ("Generation member decomposition", "Editor/Shared/FoxRunDescriptor/FoxRunGenerationMemberContracts.cs", True),
    ("Protobuf field number", "Editor/Shared/FoxRunDescriptor/FoxRunProtobufFieldNumber.cs", True),
    ("Roslyn generation model lowerer", "Editor/SourceGenerators/src/FoxRunRoslynGenerationModelLowerer.cs", True),
    ("Service DTO rules", "Editor/Shared/FoxServiceDtoValidation/FoxServiceDtoRules.cs", True),
    ("Service DTO type names", "Editor/Shared/FoxServiceDtoValidation/FoxServiceDtoTypeNames.cs", True),
    ("Service Roslyn type helpers", "Editor/SourceGenerators/src/FoxService/FoxServiceRoslynTypeHelpers.cs", True),
    ("String literal emitter", "Editor/Shared/FoxgloveSourceEmitter/StringLiteralEmitter.cs", False),
    ("Type expression emitter", "Editor/Shared/FoxgloveSourceEmitter/TypeExprEmitter.cs", False),
)
EXACT_SHARED_SOURCE_GROUPS = EXACT_SHARED_SOURCE_GROUPS + tuple(
    (
        label,
        (REPO_ROOT / "Packages/dev.unity2foxglove.sdk" / core,)
        + tuple(
            REPO_ROOT
            / f"Packages/dev.unity2foxglove.{package}/Editor/SourceGenerators/src/Shared"
            / Path(core).name
            for package in (("ros2forunity", "ros2bridge") if in_bridge else ("ros2forunity",))
        ),
    )
    for label, core, in_bridge in _VERBATIM_PROVIDER_COPIES
)
EXACT_SHARED_SOURCE_GROUPS = EXACT_SHARED_SOURCE_GROUPS + (
    (
        "Foxglove log candidate discovery",
        (
            REPO_ROOT / "Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/src/Decomposed/FoxgloveLogSourceGenerator/CandidateDiscovery.cs",
            REPO_ROOT / "Packages/dev.unity2foxglove.ros2forunity/Editor/SourceGenerators/src/Shared/Decomposed/FoxgloveLogSourceGenerator/CandidateDiscovery.cs",
            REPO_ROOT / "Packages/dev.unity2foxglove.ros2bridge/Editor/SourceGenerators/src/Shared/Decomposed/FoxgloveLogSourceGenerator/CandidateDiscovery.cs",
        ),
    ),
    (
        "Foxglove log generation diagnostics",
        (
            REPO_ROOT / "Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/src/Decomposed/FoxgloveLogSourceGenerator/GenerationAndDiagnostics.cs",
            REPO_ROOT / "Packages/dev.unity2foxglove.ros2forunity/Editor/SourceGenerators/src/Shared/Decomposed/FoxgloveLogSourceGenerator/GenerationAndDiagnostics.cs",
            REPO_ROOT / "Packages/dev.unity2foxglove.ros2bridge/Editor/SourceGenerators/src/Shared/Decomposed/FoxgloveLogSourceGenerator/GenerationAndDiagnostics.cs",
        ),
    ),
    (
        "Foxglove log member extraction",
        (
            REPO_ROOT / "Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/src/Decomposed/FoxgloveLogSourceGenerator/MemberExtraction.cs",
            REPO_ROOT / "Packages/dev.unity2foxglove.ros2bridge/Editor/SourceGenerators/src/Shared/Decomposed/FoxgloveLogSourceGenerator/MemberExtraction.cs",
        ),
    ),
    (
        "Foxglove log services",
        (
            REPO_ROOT / "Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/src/Decomposed/FoxgloveLogSourceGenerator/Services.cs",
            REPO_ROOT / "Packages/dev.unity2foxglove.ros2forunity/Editor/SourceGenerators/src/Shared/Decomposed/FoxgloveLogSourceGenerator/Services.cs",
            REPO_ROOT / "Packages/dev.unity2foxglove.ros2bridge/Editor/SourceGenerators/src/Shared/Decomposed/FoxgloveLogSourceGenerator/Services.cs",
        ),
    ),
    (
        "Foxglove log utilities",
        (
            REPO_ROOT / "Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/src/Decomposed/FoxgloveLogSourceGenerator/Utilities.cs",
            REPO_ROOT / "Packages/dev.unity2foxglove.ros2bridge/Editor/SourceGenerators/src/Shared/Decomposed/FoxgloveLogSourceGenerator/Utilities.cs",
        ),
    ),
    (
        "Generation model validator rules",
        (
            REPO_ROOT / "Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/Decomposed/FoxRunGenerationModelValidator/ValidationRules.cs",
            REPO_ROOT / "Packages/dev.unity2foxglove.ros2forunity/Editor/SourceGenerators/src/Shared/Decomposed/FoxRunGenerationModelValidator/ValidationRules.cs",
            REPO_ROOT / "Packages/dev.unity2foxglove.ros2bridge/Editor/SourceGenerators/src/Shared/Decomposed/FoxRunGenerationModelValidator/ValidationRules.cs",
        ),
    ),
    (
        "Generation model validator topology",
        (
            REPO_ROOT / "Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/Decomposed/FoxRunGenerationModelValidator/ValidationTopology.cs",
            REPO_ROOT / "Packages/dev.unity2foxglove.ros2forunity/Editor/SourceGenerators/src/Shared/Decomposed/FoxRunGenerationModelValidator/ValidationTopology.cs",
            REPO_ROOT / "Packages/dev.unity2foxglove.ros2bridge/Editor/SourceGenerators/src/Shared/Decomposed/FoxRunGenerationModelValidator/ValidationTopology.cs",
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



__all__ = [
    "_VERBATIM_PROVIDER_COPIES",
    "_validate_analyzer_meta",
    "ANALYZER_DIRECTORY",
    "AnalyzerTarget",
    "BUILD_OUTPUT_DIR",
    "CHECKED_IN_ARTIFACTS",
    "COMPOSITION_TEST",
    "CORE_TYPE_SHAPE",
    "DIAGNOSTIC_PREFIXES",
    "ET",
    "EXACT_SHARED_SOURCE_GROUPS",
    "HINT_CONTRACTS",
    "META_GUID_PATTERN",
    "PACKAGE_ROOTS",
    "PHYSICAL_HINT_CONTRACTS",
    "PROJECT",
    "PROVIDER_DEPENDENCIES",
    "PROVIDER_TYPE_SHAPE",
    "Path",
    "REPO_ROOT",
    "TARGETS",
    "UNITY_PLUGIN_GOOGLE_PROTOBUF",
    "argparse",
    "dataclass",
    "hashlib",
    "json",
    "os",
    "re",
    "shutil",
    "subprocess",
    "sys",
    "tempfile",
]
