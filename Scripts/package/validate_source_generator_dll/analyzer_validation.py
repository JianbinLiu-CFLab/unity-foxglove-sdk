"""Analyzer packaging, ledger, and composition validation."""

from __future__ import annotations

from .foundation import *
from .artifact_validation import validate_shared_source_parity
from .project_contracts import (
    _active_test_attributes,
    _composition_contract_failures,
    _evaluated_msbuild_items,
    _ledger_ids,
    _project_sources,
    _provider_descriptor_ids,
)

def validate_analyzer_contracts(target_names: tuple[str, ...]) -> bool:
    """Validate independent packaging, ledgers, IDs, hint parity, and set coverage."""
    if not validate_shared_source_parity():
        return False
    failures: list[str] = []
    assembly_names: dict[str, str] = {}
    ledger_owners: dict[str, str] = {}
    hint_tokens: dict[str, str] = {}
    analyzer_meta_guids: dict[str, str] = {}
    core_diagnostic_ids = _ledger_ids(TARGETS["core"].project)

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
            if artifact.name == "Google.Protobuf.dll":
                if not meta.is_file():
                    failures.append(f"{name}: dependency .meta missing: {meta if not meta.is_relative_to(REPO_ROOT) else meta.relative_to(REPO_ROOT)}")
            else:
                failures.extend(
                    _validate_analyzer_meta(
                        meta,
                        f"{name}:{artifact.name}",
                        analyzer_meta_guids,
                    )
                )

        try:
            sources = _project_sources(target.project)
            evaluated = _evaluated_msbuild_items(target.project)
            evaluated_sources = {
                Path(item.get("FullPath", item.get("Identity", ""))).resolve()
                for item in evaluated.get("Compile", [])
                if item.get("FullPath", item.get("Identity", ""))
            }
            explicit_sources = set(sources)
            if evaluated_sources != explicit_sources:
                failures.append(
                    f"{name}: evaluated MSBuild Compile items differ from explicit project ownership "
                    f"(evaluated={len(evaluated_sources)}, explicit={len(explicit_sources)})"
                )
            if name != "core":
                evaluated_packages = {
                    item.get("Identity", item.get("Include", ""))
                    for item in evaluated.get("PackageReference", [])
                }
                unexpected_packages = evaluated_packages - PROVIDER_DEPENDENCIES - {"NETStandard.Library"}
                missing_packages = PROVIDER_DEPENDENCIES - evaluated_packages
                if unexpected_packages or missing_packages:
                    failures.append(
                        f"{name}: evaluated PackageReference set is not Roslyn-only: "
                        f"unexpected={sorted(unexpected_packages)}, missing={sorted(missing_packages)}"
                    )
                if evaluated.get("ProjectReference"):
                    failures.append(f"{name}: evaluated ProjectReference is forbidden")
                imported_references = [
                    item for item in evaluated.get("Reference", [])
                    if item.get("DefiningProjectFullPath", "").lower().startswith(str(REPO_ROOT).lower())
                    and Path(item.get("DefiningProjectFullPath", "")).resolve() != target.project.resolve()
                ]
                if imported_references:
                    failures.append(f"{name}: imported assembly Reference is forbidden")
        except (OSError, ET.ParseError, ValueError, RuntimeError) as exc:
            failures.append(f"{name}: cannot resolve evaluated MSBuild ownership: {exc}")
            sources = []
        missing_sources = [source for source in sources if not source.exists()]
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
                    if _compile_include_has_wildcard(normalized):
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
            wrong_namespace = {
                diagnostic_id
                for diagnostic_id in source_ids
                if not diagnostic_id.startswith(prefix)
                and diagnostic_id not in core_diagnostic_ids
            }
            if wrong_namespace:
                failures.append(
                    f"{name}: diagnostic IDs use another provider namespace: "
                    f"{sorted(wrong_namespace)}"
                )
            undeclared = {
                diagnostic_id
                for diagnostic_id in source_ids
                if diagnostic_id.startswith(prefix)
            } - ledgers
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
    project = REPO_ROOT / "Packages/dev.unity2foxglove.sdk/Tests/Unit/FoxgloveSdk.UnitTests.csproj"
    restore_command = ["dotnet", "restore", str(project), *msbuild_props]
    try:
        subprocess.run(restore_command, cwd=REPO_ROOT, check=True)
    except (subprocess.CalledProcessError, FileNotFoundError) as exc:
        print(
            "[FAIL] Analyzer composition/parity test restore failed: "
            f"{exc}",
            file=sys.stderr,
        )
        return False
    command = [
        "dotnet",
        "test",
        str(project),
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



__all__ = [
    "_active_test_attributes",
    "_composition_contract_failures",
    "_evaluated_msbuild_items",
    "_ledger_ids",
    "_project_sources",
    "_provider_descriptor_ids",
    "run_analyzer_composition_tests",
    "validate_analyzer_contracts",
    "validate_shared_source_parity",
]
