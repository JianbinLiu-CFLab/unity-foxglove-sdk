"""Project ownership and source-contract helpers."""

from __future__ import annotations

from .foundation import *

def _normalize_compile_include(include: str) -> str:
    """Normalize an MSBuild Compile Include for the current filesystem host."""
    return include.replace("\\", "/")


def _compile_include_has_wildcard(include: str) -> bool:
    """Return whether a Compile Include uses any glob wildcard."""
    return "*" in include or "?" in include


def _project_sources(project: Path) -> list[Path]:
    """Resolve every explicit Compile item in one controlled analyzer project."""
    root = ET.parse(project).getroot()
    excluded: list[str] = []
    for node in root.findall(".//Compile"):
        excluded.extend(
            _normalize_compile_include(value.strip())
            for value in node.attrib.get("Exclude", "").split(";")
            if value.strip()
        )

    def is_excluded(path: Path) -> bool:
        """Return whether a source path matches an explicit Compile exclusion."""
        relative = os.path.relpath(path.resolve(), project.parent.resolve()).replace("\\", "/")
        return any(
            relative == pattern
            or Path(relative).match(pattern)
            for pattern in excluded
        )

    sources: list[Path] = []
    for node in root.findall(".//Compile"):
        for include in node.attrib.get("Include", "").split(";"):
            include = include.strip()
            if include:
                normalized = _normalize_compile_include(include)
                if _compile_include_has_wildcard(normalized):
                    matches = [
                        path.resolve()
                        for path in project.parent.glob(
                            normalized
                        )
                        if path.is_file() and not is_excluded(path)
                    ]
                    if not matches:
                        raise ValueError(
                            f"{project}: Compile glob matched no files: "
                            f"{include}"
                        )
                    sources.extend(matches)
                else:
                    path = (project.parent / normalized).resolve()
                    if not is_excluded(path):
                        sources.append(path)
    return sources


def _evaluated_msbuild_items(project: Path) -> dict[str, list[dict[str, str]]]:
    """Read effective MSBuild items, including imported props/targets."""
    command = [
        "dotnet",
        "msbuild",
        str(project),
        "-getItem:Compile;PackageReference;ProjectReference;Reference",
        "-nologo",
    ]
    try:
        result = subprocess.run(
            command,
            cwd=REPO_ROOT,
            check=False,
            capture_output=True,
            text=True,
            timeout=120,
        )
    except (OSError, subprocess.TimeoutExpired) as exc:
        raise RuntimeError(f"MSBuild evaluation failed for {project}: {exc}") from exc
    if result.returncode != 0:
        raise RuntimeError(
            f"MSBuild evaluation failed for {project}: exit {result.returncode}: {result.stderr.strip()}"
        )
    try:
        payload = json.loads(result.stdout)
    except json.JSONDecodeError as exc:
        raise RuntimeError(f"MSBuild evaluation returned non-JSON for {project}: {exc}") from exc
    return payload.get("Items", {})


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
    """Return Provider-owned diagnostic IDs referenced by compiled sources."""
    ids: set[str] = set()
    pattern = re.compile(r"\b((?:FOXRUN|FOXR2F|FOXBRG)\d{3})\b")
    constructed_pattern = re.compile(
        r"[\"']((?:FOXRUN|FOXR2F|FOXBRG))[\"']\s*\+\s*[\"'](\d{3})[\"']"
    )
    for source in sources:
        if not source.exists():
            continue
        text = source.read_text(encoding="utf-8")
        ids.update(pattern.findall(text))
        ids.update(prefix + number for prefix, number in constructed_pattern.findall(text))
    return ids



__all__ = sorted(name for name in globals() if not name.startswith("__"))
