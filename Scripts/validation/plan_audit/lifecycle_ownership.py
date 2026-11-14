"""Audit Phase197 lifecycle ownership evidence without changing product code."""

from __future__ import annotations

import csv
import fnmatch
import json
import math
import xml.etree.ElementTree as ET
import re
import shutil
import subprocess
from collections import Counter
from pathlib import Path

from . import audit_lib

TOPIC = "lifecycle_ownership"
PHASE = "197"
OUTPUT = audit_lib.BUILD_ROOT / TOPIC
PLAN = audit_lib.ROOT / "Plan" / "197_PHASE197_EVIDENCE_LED_LIFECYCLE_OWNERSHIP_PLAN.md"
SCRIPT_REL = "Scripts/validation/plan_audit/lifecycle_ownership.py"
TEST_REL = "Packages/dev.unity2foxglove.sdk/Tests/Unit/FoxRun/FoxgloveManagerTeardownTests.cs"
TEST_PROJECT_REL = "Packages/dev.unity2foxglove.sdk/Tests/Unit/FoxgloveSdk.UnitTests.csproj"
TEARDOWN_REL = "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManagerTeardownState.cs"
CANONICAL_TEST_NAMESPACE = "Unity.FoxgloveSDK.Tests.Unit.FoxRun"
CANONICAL_TEST_CLASS = "FoxgloveManagerTeardownTests"
CANONICAL_TEST_PREFIX = f"{CANONICAL_TEST_NAMESPACE}.{CANONICAL_TEST_CLASS}."
KEYWORDS = re.compile(r"fix|bug|repair|restore|regression|broken|hotfix", re.IGNORECASE)
LIFETIME_WORDS = re.compile(
    r"\b(?:OnEnable|OnDisable|OnDestroy|StartServer|StopServer|CleanupStartupAfterFailure|Dispose|Reset|Connect|Disconnect)\b"
)
EXPECTED_TEST_NAMES = (
    "DisableTeardownRunsEveryMandatoryStepInOrderAndRethrowsFirstFatal",
    "DestroyTeardownRunsEveryMandatoryStepInOrderAndRethrowsFirstFatal",
    "StopServerTailRunsAfterRuntimeStopFailureAndRethrowsFirstFatal",
    "StartupFailureCleanupPolicyRunsEveryStepAndReportsFailures",
    "StartupFailureCleanupWiringContainsEveryProductionStep",
    "PreTailCleanupRunsEveryStepAndReturnsTheFirstFailure",
    "StopServerTailRunsEveryCleanupStepAfterMultipleFailures",
    "EarlyCleanupContinuesAfterSeveralRegistrationFailures",
    "RuntimeDisposeRetryReportsTransientFailureAndReleasesReference",
    "RuntimeDisposeRetryDoesNotRepeatSuccessfulDispose",
    "RuntimeDisposeRetryRethrowsFirstFailureAndKeepsReferenceWhenBothAttemptsFail",
    "RuntimeDisposeRetryDoesNotRepeatDisposeWhenReferenceReleaseFails",
    "StopServerRunsForActiveOrPendingRuntimeOwnership",
)
EXPECTED_TEST_COUNTS = {name: 1 for name in EXPECTED_TEST_NAMES}
EXPECTED_TEST_COUNTS["StopServerRunsForActiveOrPendingRuntimeOwnership"] = 4
MUTATION_EXPECTED_TESTS = {
    "197-W1-mutation-order": ("StopServerTailRunsEveryCleanupStepAfterMultipleFailures",),
    "197-W1-mutation-cleanup": ("StopServerTailRunsEveryCleanupStepAfterMultipleFailures",),
}
OWNER_PATTERNS = {
    "FoxgloveManager": "FoxgloveManager*.cs",
    "FoxgloveSession": "FoxgloveSession*.cs",
    "ReplayController": "ReplayController*.cs",
    "Ros2BridgeWorkerLease": "Ros2BridgeWorkerLease*.cs",
}
OWNER_SUPPORT_NAMES = {
    "FoxgloveManager": ("ClientEventAdmissionState.cs", "ReplaySubscriberFanoutState.cs"),
}
OWNER_SYNC_MODES = {
    "FoxgloveManager": "composed admission and fanout locks",
    "FoxgloveSession": "lifecycle locks",
    "ReplayController": "replay locks",
    "Ros2BridgeWorkerLease": "worker lease locks",
}
KNOWN_REF_MUTATORS = ("TryCleanup", "WarningDebouncer.TryUpdateCooldown")
ATOMIC_WRITE_CALLS = (
    "Interlocked.Add",
    "Interlocked.And",
    "Interlocked.CompareExchange",
    "Interlocked.Decrement",
    "Interlocked.Exchange",
    "Interlocked.Increment",
    "Interlocked.Or",
    "Volatile.Write",
)
TYPE_DECLARATION_PATTERN = re.compile(
    r"\b(?:class|struct|record|interface)\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)"
)
FIELD_DECLARATION_PATTERN = re.compile(
    r"\b(?P<access>private|protected|internal|public)\s+"
    r"(?P<modifiers>(?:(?:static|readonly|volatile)\s+)*)"
    r"(?!(?:const|event)\b)[A-Za-z_][A-Za-z0-9_:<>,.?()\[\]\s]*\s+"
    r"(?P<name>_?[A-Za-z][A-Za-z0-9_]*)\s*(?:=|;)"
)


def _check(identifier: str, plan_ref: str, kind: str, status: str, command: str, evidence: str, detail: str) -> audit_lib.Check:
    """Create one audit result."""
    return audit_lib.Check(identifier, plan_ref, kind, status, command, evidence, detail)


def _log(identifier: str) -> Path:
    """Return an evidence log path."""
    return OUTPUT / "logs" / f"{identifier}.log"


def _run(command: list[str], identifier: str, timeout: float = 180.0, cwd: Path = audit_lib.ROOT) -> dict[str, object]:
    """Run a command and save its output."""
    return audit_lib.run(command, _log(identifier), timeout, cwd)


def _git_output(args: list[str], timeout: float = 180.0) -> str:
    """Run a read-only git command and return UTF-8 output."""
    result = subprocess.run(args, cwd=audit_lib.ROOT, env=audit_lib._audit_environment(), capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=timeout, check=True)
    return result.stdout


def _origin_main_sha() -> str | None:
    """Resolve the fetched origin/main revision without fallback refs."""
    try:
        return _git_output(["git", "rev-parse", "--verify", "origin/main^{commit}"]).strip()
    except (OSError, subprocess.CalledProcessError, subprocess.TimeoutExpired):
        return None


def _mask_csharp_comments_and_strings(text: str) -> str:
    """Mask C# comments and string literals while preserving line positions."""
    chars = list(text)
    length = len(text)
    index = 0

    def mask(position: int) -> None:
        """Replace one non-newline source character with a space."""
        if chars[position] != "\n":
            chars[position] = " "

    while index < length:
        if text.startswith("//", index):
            mask(index)
            mask(index + 1)
            index += 2
            while index < length and text[index] != "\n":
                mask(index)
                index += 1
            continue
        if text.startswith("/*", index):
            mask(index)
            mask(index + 1)
            index += 2
            while index < length:
                if text.startswith("*/", index):
                    mask(index)
                    mask(index + 1)
                    index += 2
                    break
                mask(index)
                index += 1
            continue
        if text.startswith("$@\"", index) or text.startswith("@$\"", index):
            mask(index)
            mask(index + 1)
            mask(index + 2)
            index += 3
            while index < length:
                if text.startswith("\"\"", index):
                    mask(index)
                    mask(index + 1)
                    index += 2
                    continue
                if text[index] == "\"":
                    mask(index)
                    index += 1
                    break
                mask(index)
                index += 1
            continue
        if text.startswith("@\"", index):
            mask(index)
            mask(index + 1)
            index += 2
            while index < length:
                if text.startswith("\"\"", index):
                    mask(index)
                    mask(index + 1)
                    index += 2
                    continue
                if text[index] == "\"":
                    mask(index)
                    index += 1
                    break
                mask(index)
                index += 1
            continue
        if text.startswith("$\"", index):
            mask(index)
            mask(index + 1)
            index += 2
            escaped = False
            while index < length:
                character = text[index]
                if character == "\n":
                    escaped = False
                    index += 1
                    continue
                mask(index)
                if escaped:
                    escaped = False
                elif character == "\\":
                    escaped = True
                elif character == "\"":
                    index += 1
                    break
                index += 1
            continue
        if text[index] in {"\"", "'"}:
            quote = text[index]
            mask(index)
            index += 1
            escaped = False
            while index < length:
                character = text[index]
                if character == "\n":
                    escaped = False
                    index += 1
                    continue
                mask(index)
                if escaped:
                    escaped = False
                elif character == "\\":
                    escaped = True
                elif character == quote:
                    index += 1
                    break
                index += 1
            continue
        index += 1
    return "".join(chars)

def _mask_inactive_csharp_regions(masked_text: str) -> str:
    """Mask explicitly inactive C# conditional-compilation branches."""
    chars = list(masked_text)
    frames: list[tuple[bool, bool]] = []
    active = True
    offset = 0
    for line in masked_text.splitlines(keepends=True):
        directive = re.match(r"^\s*#\s*(if|elif|else|endif)\b(.*)$", line)
        if directive:
            kind, expression = directive.groups()
            if kind == "if":
                condition_false = expression.strip().lower() in {"false", "0"}
                branch_active = active and not condition_false
                frames.append((active, branch_active))
                active = branch_active
            elif frames and kind in {"elif", "else"}:
                parent, taken = frames[-1]
                condition_false = kind == "elif" and expression.strip().lower() in {"false", "0"}
                active = parent and not taken and not condition_false
                frames[-1] = (parent, True)
            elif frames:
                parent, _ = frames.pop()
                active = parent
            offset += len(line)
            continue
        if not active:
            for index in range(offset, offset + len(line)):
                if chars[index] != "\n":
                    chars[index] = " "
        offset += len(line)
    return "".join(chars)

def _mask_csharp_source(text: str) -> str:
    """Mask comments, strings, and explicitly inactive conditional regions."""
    return _mask_inactive_csharp_regions(_mask_csharp_comments_and_strings(text))

def _type_spans(text: str, masked_text: str | None = None) -> dict[str, tuple[int, int]]:
    """Return source spans for nested C# declaring types."""
    masked = masked_text if masked_text is not None else _mask_csharp_source(text)
    openings: dict[int, str] = {}
    for match in TYPE_DECLARATION_PATTERN.finditer(masked):
        opening = masked.find("{", match.end())
        if opening >= 0:
            openings[opening] = match.group("name")
    stack: list[tuple[int, str | None, str | None]] = []
    spans: dict[str, tuple[int, int]] = {}
    for index, character in enumerate(masked):
        if character == "{":
            name = openings.get(index)
            parent = next((item[2] for item in reversed(stack) if item[2]), None)
            path = f"{parent}::{name}" if parent and name else name
            stack.append((index, name, path))
        elif character == "}" and stack:
            start, _, path = stack.pop()
            if path:
                spans[path] = (start, index + 1)
    return spans

def _plan_bases(text: str) -> dict[str, list[str]]:
    """Read only the current explicit Plan base fields."""
    fields: dict[str, list[str]] = {}
    for field in ("Audit base", "Entry base"):
        pattern = re.compile(rf"^{re.escape(field)}:\s*`origin/main == ([0-9a-f]{{40}})`(?:\s*\(.*\))?\.?$")
        values = [match.group(1) for line in text.splitlines() if (match := pattern.fullmatch(line.strip()))]
        if values:
            fields[field] = values
    return fields

def _line_refs(
    text: str,
    relative: Path,
    pattern: re.Pattern[str],
    masked_text: str | None = None,
) -> list[str]:
    """Return source locations matching a lifecycle evidence pattern."""
    masked = masked_text if masked_text is not None else _mask_csharp_source(text)
    return [
        f"{relative.as_posix()}:{line_number}"
        for line_number, line in enumerate(masked.splitlines(), 1)
        if pattern.search(line)
    ]
def _nested_receiver_names(masked_texts: dict[Path, str], declaring_type: str) -> tuple[str, ...]:
    """Infer receiver expressions whose static type matches a nested declaring type."""
    leaf = declaring_type.rsplit("::", 1)[-1]
    text = "\n".join(masked_texts.values())
    receivers = set(re.findall(rf"\b{re.escape(leaf)}\s+([A-Za-z_][A-Za-z0-9_]*)\b", text))
    receivers.update(re.findall(rf"\b([A-Za-z_][A-Za-z0-9_]*)\s*=\s*new\s+{re.escape(leaf)}\b", text))
    dictionary_value = bool(re.search(rf"\bDictionary\s*<[^;\n]*,\s*{re.escape(leaf)}\s*>", text))
    if dictionary_value:
        receivers.update(re.findall(r"\bout\s+var\s+([A-Za-z_][A-Za-z0-9_]*)\b", text))
        receivers.update(re.findall(r"\bforeach\s*\(\s*var\s+([A-Za-z_][A-Za-z0-9_]*)\s+in\s+[^)]*\.Values", text))
        receivers.update(f"{name}.Value" for name in re.findall(r"\bforeach\s*\(\s*var\s+([A-Za-z_][A-Za-z0-9_]*)\s+in\s+[^)]*\)", text))
    return tuple(sorted(receivers))
def _writer_refs(
    text: str,
    relative: Path,
    field: str,
    scope: tuple[int, int] | None = None,
    masked_text: str | None = None,
    qualified_receivers: tuple[str, ...] | None = None,
) -> list[str]:
    """Return source locations that prove a field write."""
    masked = masked_text if masked_text is not None else _mask_csharp_source(text)
    scope_start, scope_end = scope or (0, len(masked))
    scoped_text = masked[scope_start:scope_end]
    refs: set[str] = set()
    receiver_pattern = "|".join(re.escape(receiver) for receiver in qualified_receivers or ())
    field_target = rf"(?:{receiver_pattern})\.{re.escape(field)}\b" if receiver_pattern else rf"\b{re.escape(field)}\b"
    assignment_pattern = re.compile(
        rf"{field_target}\s*(?:(?<![=!<>])=(?!=)|\+=|-=|\?\?=)"
    )
    atomic_names = "|".join(re.escape(name) for name in ATOMIC_WRITE_CALLS)
    atomic_pattern = re.compile(rf"\b(?:{atomic_names})\s*\(\s*ref\s+{field_target}")
    mutator_names = "|".join(re.escape(name) for name in KNOWN_REF_MUTATORS)
    mutator_pattern = re.compile(
        rf"\b(?:{mutator_names})\s*\([^;{{}}]*?(?P<ref>\bref\s+{field_target})",
        re.DOTALL,
    )
    for pattern in (assignment_pattern, atomic_pattern):
        refs.update(
            f"{relative.as_posix()}:{text.count(chr(10), 0, scope_start + match.start()) + 1}"
            for match in pattern.finditer(scoped_text)
        )
    refs.update(
        f"{relative.as_posix()}:{text.count(chr(10), 0, scope_start + match.start('ref')) + 1}"
        for match in mutator_pattern.finditer(scoped_text)
    )
    return sorted(refs)

def _field_declarations(
    text: str,
    relative: Path,
    masked_text: str | None = None,
    spans: dict[str, tuple[int, int]] | None = None,
    ) -> list[dict[str, str]]:
    """Collect field declarations and classify configuration fields."""
    masked = masked_text if masked_text is not None else _mask_csharp_source(text)
    lines = masked.splitlines()
    spans = spans if spans is not None else _type_spans(text, masked)
    fallback_type = relative.stem.split(".", 1)[0]
    declarations: list[dict[str, str]] = []
    for match in FIELD_DECLARATION_PATTERN.finditer(masked):
        matched_text = match.group(0)
        if (
            "=>" in matched_text
            or "{" in matched_text
            or "}" in matched_text
            or (match.end() < len(text) and text[match.end()] == ">")
        ):
            continue
        line_number = masked.count("\n", 0, match.start("name")) + 1
        declaration_prefix = masked[match.start():match.start("name")]
        declaration_tail = masked[match.start("name"):match.end()]
        assignment_index = len(declaration_tail)
        for terminator in ("=", ";"):
            index = declaration_tail.find(terminator)
            if index >= 0:
                assignment_index = min(assignment_index, index)
        before_assignment = declaration_tail[:assignment_index]
        arrow_index = declaration_tail.find("=>")
        if arrow_index >= 0 and declaration_tail.find("=") == arrow_index:
            continue
        if "{" in before_assignment or "(" in before_assignment:
            continue
        if "(" in declaration_prefix and ")" not in declaration_prefix:
            continue
        line_start = masked.rfind("\n", 0, match.start()) + 1
        same_line_attributes = masked[line_start:match.start()]
        access_line_number = masked.count("\n", 0, match.start()) + 1
        previous_line = lines[access_line_number - 2].strip() if access_line_number >= 2 else ""
        access = match.group("access")
        modifiers = match.group("modifiers")
        has_serialize_field = "SerializeField" in same_line_attributes or "SerializeField" in previous_line
        configuration = (has_serialize_field or access == "public") and "static" not in modifiers
        declaring_type = min(
            (
                name
                for name, (start, end) in spans.items()
                if start < match.start() < end
            ),
            key=lambda name: spans[name][1] - spans[name][0],
            default=fallback_type,
        )
        declarations.append(
            {
                "name": match.group("name"),
                "declaring_type": declaring_type,
                "file": relative.as_posix(),
                "declaration": f"{relative.as_posix()}:{line_number}",
                "classification": "configuration" if configuration else "mutable",
            }
        )
    return declarations
def _collect_owner_fields(matched: list[Path]) -> tuple[dict[str, dict[str, object]], list[str]]:
    """Collect all declarations first, then aggregate writers across partial files."""
    texts = {
        relative: (audit_lib.ROOT / relative).read_text(encoding="utf-8", errors="replace")
        for relative in matched
    }
    masked_texts = {relative: _mask_csharp_source(text) for relative, text in texts.items()}
    type_spans = {relative: _type_spans(text, masked_texts[relative]) for relative, text in texts.items()}
    fields: dict[str, dict[str, object]] = {}
    for relative, text in texts.items():
        for declaration in _field_declarations(text, relative, masked_texts[relative], type_spans[relative]):
            name = declaration["name"]
            declaring_type = declaration["declaring_type"]
            field_key = f"{declaring_type}::{name}"
            field_data = fields.setdefault(
                field_key,
                {
                    "name": name,
                    "declaring_type": declaring_type,
                    "files": [],
                    "declarations": [],
                    "assignments": 0,
                    "writers": [],
                    "classification": declaration["classification"],
                },
            )
            field_data["files"].append(declaration["file"])
            field_data["declarations"].append(declaration["declaration"])
            if declaration["classification"] == "mutable":
                field_data["classification"] = "mutable"
    missing_writers: list[str] = []
    for field_key, field_data in fields.items():
        name = str(field_data["name"])
        declaring_type = str(field_data["declaring_type"])
        nested_type = "::" in declaring_type
        nested_receivers = _nested_receiver_names(masked_texts, declaring_type) if nested_type else ()
        for relative, text in texts.items():
            exact_scope = type_spans[relative].get(declaring_type)
            parent_scope = None
            if nested_type:
                parent_type = declaring_type.rsplit("::", 1)[0]
                parent_scope = type_spans[relative].get(parent_type)
            scope = exact_scope if nested_type else exact_scope or parent_scope
            if scope is None:
                if nested_type and parent_scope and nested_receivers:
                    writer_refs = _writer_refs(
                        text,
                        relative,
                        name,
                        parent_scope,
                        masked_texts[relative],
                        qualified_receivers=nested_receivers,
                    )
                    field_data["assignments"] = int(field_data["assignments"]) + len(writer_refs)
                    field_data["writers"].extend(writer_refs)
                    continue
                fallback_type = relative.stem.split(".", 1)[0]
                if declaring_type == fallback_type:
                    scope = (0, len(text))
                else:
                    continue
            writer_refs = _writer_refs(text, relative, name, scope, masked_texts[relative])
            if nested_type and parent_scope and not writer_refs and nested_receivers:
                writer_refs = _writer_refs(
                    text,
                    relative,
                    name,
                    parent_scope,
                    masked_texts[relative],
                    qualified_receivers=nested_receivers,
                )
            field_data["assignments"] = int(field_data["assignments"]) + len(writer_refs)
            field_data["writers"].extend(writer_refs)
        field_data["files"] = sorted(set(field_data["files"]))
        field_data["declarations"] = sorted(set(field_data["declarations"]))
        field_data["writers"] = sorted(set(field_data["writers"]))
        if field_data["classification"] == "mutable":
            if not field_data["writers"]:
                missing_writers.append(field_key)
            field_data["reason"] = "writer references required across owner partials"
        else:
            field_data["reason"] = "Unity-serialized or public configuration field"
    return fields, sorted(missing_writers)


def _is_production_source(path: str) -> bool:
    """Return whether a source path belongs to a tracked Runtime tree."""
    normalized = path.replace("\\", "/")
    parts = normalized.split("/")
    excluded = {"Editor", "Tests", "Fixtures", "Harness"}
    return normalized.endswith(".cs") and "Runtime" in parts and not any(part in excluded for part in parts)


def _source_paths_valid(value: object) -> bool:
    """Return whether a path list contains only production source paths."""
    return (
        isinstance(value, list)
        and bool(value)
        and all(isinstance(path, str) and _is_production_source(path) for path in value)
    )


def _source_refs_valid(value: object) -> bool:
    """Return whether source references use a production path and positive line number."""
    if not isinstance(value, list) or not value:
        return False
    for reference in value:
        if not isinstance(reference, str):
            return False
        match = re.fullmatch(r"(.+):([1-9][0-9]*)", reference)
        if not match or not _is_production_source(match.group(1)):
            return False
    return True


def _owner_evidence_valid(data: object) -> bool:
    """Return whether an owner payload contains traceable lifecycle evidence."""
    if not isinstance(data, dict) or not _source_paths_valid(data.get("files")) or not data.get("fields"):
        return False
    fields = data["fields"]
    missing_writers = data.get("missing_writers")
    owner = data.get("owner")
    synchronization = data.get("synchronization")
    if (
        not isinstance(fields, dict)
        or not fields
        or not isinstance(missing_writers, list)
        or missing_writers
        or owner not in OWNER_SYNC_MODES
        or not _source_refs_valid(data.get("locks"))
        or not _source_refs_valid(data.get("lifetime"))
        or not isinstance(synchronization, dict)
        or synchronization.get("mode") != OWNER_SYNC_MODES[owner]
        or not _source_refs_valid(synchronization.get("refs"))
    ):
        return False
    for value in fields.values():
        if not isinstance(value, dict):
            return False
        if value.get("classification") not in {"mutable", "configuration"}:
            return False
        if not _source_paths_valid(value.get("files")) or not _source_refs_valid(value.get("declarations")):
            return False
        writers = value.get("writers")
        if writers and not _source_refs_valid(writers):
            return False
        if value["classification"] == "mutable" and (
            not writers or int(value.get("assignments", 0)) <= 0
        ):
            return False
    return True


def _normalise_test_name(name: str) -> str:
    """Reduce a TRX test name to its method name."""
    return name.split("(", 1)[0].rsplit(".", 1)[-1]


def _trx_outcomes(path: Path) -> dict[str, list[str]]:
    """Return normalized TRX test names and all observed outcomes."""
    if not path.is_file():
        return {}
    try:
        root = ET.fromstring(path.read_text(encoding="utf-8", errors="replace"))
    except ET.ParseError:
        return {}
    outcomes: dict[str, list[str]] = {}
    for result in root.iter():
        if not result.tag.endswith("UnitTestResult"):
            continue
        name = result.attrib.get("testName", "")
        outcome = result.attrib.get("outcome", "")
        if name and outcome:
            outcomes.setdefault(_normalise_test_name(name), []).append(outcome)
    return outcomes


def _trx_canonical_test_issues(path: Path) -> list[str]:
    """Return lifecycle results that do not belong to the canonical test class."""
    if not path.is_file():
        return ["TRX_FILE_MISSING"]
    try:
        root = ET.fromstring(path.read_text(encoding="utf-8", errors="replace"))
    except ET.ParseError:
        return ["TRX_PARSE_ERROR"]
    issues: list[str] = []
    for result in root.iter():
        if not result.tag.endswith("UnitTestResult"):
            continue
        name = result.attrib.get("testName", "")
        if _normalise_test_name(name) in EXPECTED_TEST_NAMES and not name.startswith(CANONICAL_TEST_PREFIX):
            issues.append(name)
    return sorted(set(issues))


def _canonical_test_source_issues(base_sha: str) -> list[str]:
    """Return source or project changes that could replace the canonical suite."""
    issues: list[str] = []
    source_path = audit_lib.ROOT / TEST_REL
    project_path = audit_lib.ROOT / TEST_PROJECT_REL
    if not source_path.is_file():
        issues.append(f"missing:{TEST_REL}")
    if not project_path.is_file():
        issues.append(f"missing:{TEST_PROJECT_REL}")
    if issues:
        return issues
    source_text = source_path.read_text(encoding="utf-8", errors="replace")
    project_text = project_path.read_text(encoding="utf-8", errors="replace")
    if not re.search(rf"\bnamespace\s+{re.escape(CANONICAL_TEST_NAMESPACE)}\b", source_text):
        issues.append("canonical namespace missing")
    if not re.search(rf"\bclass\s+{re.escape(CANONICAL_TEST_CLASS)}\b", source_text):
        issues.append("canonical test class missing")
    missing_methods = [name for name in EXPECTED_TEST_NAMES if not re.search(rf"\b{re.escape(name)}\s*\(", source_text)]
    if missing_methods:
        issues.append(f"canonical methods missing={missing_methods}")
    if '**/*.cs' not in project_text:
        issues.append("unit project does not include the canonical test tree")
    try:
        base_source = _git_output(["git", "show", f"{base_sha}:{TEST_REL}"])
        base_project = _git_output(["git", "show", f"{base_sha}:{TEST_PROJECT_REL}"])
    except (OSError, subprocess.CalledProcessError, subprocess.TimeoutExpired) as exc:
        issues.append(f"canonical base unavailable:{exc}")
    else:
        if source_text != base_source:
            issues.append(f"canonical source changed:{TEST_REL}")
        if project_text != base_project:
            issues.append(f"canonical project changed:{TEST_PROJECT_REL}")
    return sorted(set(issues))


def _missing_expected_tests(path: Path) -> list[str]:
    """Return expected lifecycle tests without exclusively passing TRX results."""
    outcomes = _trx_outcomes(path)
    return sorted(
        name
        for name in EXPECTED_TEST_NAMES
        if len(outcomes.get(name, [])) != EXPECTED_TEST_COUNTS[name]
        or any(outcome != "Passed" for outcome in outcomes.get(name, []))
    )


def _expected_test_failure(path: Path, expected_names: tuple[str, ...] | None = None) -> bool:
    """Return whether a named lifecycle test failed in a mutation run."""
    outcomes = _trx_outcomes(path)
    names = expected_names or EXPECTED_TEST_NAMES
    return any("Failed" in outcomes.get(name, []) or "Error" in outcomes.get(name, []) for name in names)


def _candidate_metadata_issues(candidates: list[dict[str, str]]) -> list[str]:
    """Return candidate rows that cannot be independently reviewed."""
    issues: list[str] = []
    for row in candidates:
        pr = row.get("pr", "unknown")
        if not row.get("title"):
            issues.append(f"{pr}:missing-title")
        if not re.fullmatch(r"[0-9a-f]{40}", row.get("merge_sha", "")):
            issues.append(f"{pr}:invalid-merge-sha")
        if not row.get("surface") or row.get("surface") == "unknown":
            issues.append(f"{pr}:missing-surface")
    return sorted(set(issues))


def _plan_check(base_sha: str) -> audit_lib.Check:
    """Check that the plan records the current-base decision state."""
    plan_log = _log("197-PLAN")
    plan_log.parent.mkdir(parents=True, exist_ok=True)
    if not PLAN.is_file():
        plan_log.write_text("PLAN_FILE_MISSING\n", encoding="utf-8")
        return _check("197-PLAN", "Plan/197... § header and Appendix A", "environment", "NOT_RUN", "read Plan/197...", str(plan_log), "Plan197 is ignored local input and is unavailable in this checkout")
    text = PLAN.read_text(encoding="utf-8")
    plan_log.write_text(text, encoding="utf-8")
    status = next((line for line in text.splitlines() if line.startswith("Status:")), "")
    status_lower = status.lower()
    accepted_stop = "phase stops after w1" in status_lower and "below the 20% decision gate" in status_lower
    pending_review = "w1" in status_lower and ("pending" in status_lower or "regenerat" in status_lower)
    appendix_note = len(_appendix_labels()) > 0
    bases = _plan_bases(text)
    origin_main = _origin_main_sha()
    if origin_main is None:
        return _check("197-PLAN", "Plan/197... § header and Appendix A", "environment", "NOT_RUN", "git rev-parse --verify origin/main; read Plan/197...", str(plan_log), "origin/main is unavailable in this checkout")
    recorded_match = origin_main == base_sha and bases.get("Audit base") == [origin_main] and bases.get("Entry base") == [origin_main]
    okay = recorded_match and appendix_note and (accepted_stop or pending_review)
    detail = f"base={base_sha}; plan_bases={bases}; recorded_base_match={recorded_match}; accepted_stop={accepted_stop}; pending_review={pending_review}; appendix_rows={len(_appendix_labels())}"
    return _check("197-PLAN", "Plan/197... § header and Appendix A", "history", "PASS" if okay else "FAIL", "git rev-parse origin/main; read Plan/197...", "build/plan-audit/lifecycle_ownership/plan.log", detail)


def _pr_titles() -> dict[str, str]:
    """Load merged pull-request titles once from GitHub."""
    result = _run(["gh", "pr", "list", "--repo", "JianbinLiu-CFLab/unity-foxglove-sdk", "--state", "merged", "--limit", "1000", "--json", "number,title,mergedAt"], "197-gh-pr-list", 180.0)
    if result["exit_code"] != 0:
        raise RuntimeError(str(result["output"]))
    payload = json.loads(str(result["output"]))
    return {str(row["number"]): str(row["title"]) for row in payload}


def _history_candidates(base_sha: str) -> tuple[list[dict[str, str]], str]:
    """Build the complete W1.1 candidate set through the audited main revision."""
    titles = _pr_titles()
    raw = _git_output(["git", "log", "--first-parent", "--merges", "--since=2026-09-01", "--format=%H%x09%P%x09%ad%x09%s", "--date=iso-strict", base_sha])
    candidates: list[dict[str, str]] = []
    for line in raw.splitlines():
        merge, parents, merged_at, merge_subject = line.split("\t", 3)
        parent_list = parents.split()
        if len(parent_list) < 2:
            continue
        pr_match = re.search(r"#(\d+)", merge_subject)
        pr = pr_match.group(1) if pr_match else "unknown"
        title = titles.get(pr, merge_subject)
        subjects = _git_output(["git", "log", "--format=%s", f"{parent_list[0]}..{parent_list[1]}"])
        haystack = "\n".join((title, merge_subject, subjects))
        if not KEYWORDS.search(haystack):
            continue
        paths = _git_output(["git", "diff-tree", "-m", "--no-commit-id", "--name-only", "-r", merge], 180.0).splitlines()
        surface = ";".join(sorted({path.split("/")[0] for path in paths if path})) or "unknown"
        suggested, reason = _suggest_label(title, subjects, paths)
        candidates.append({"pr": pr, "title": title, "merge_sha": merge, "merged_at": merged_at, "surface": surface, "suggested_label": suggested, "reason": reason})
    output = OUTPUT / "fix-candidates.csv"
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=["pr", "title", "merge_sha", "merged_at", "surface", "suggested_label", "reason"])
        writer.writeheader()
        writer.writerows(candidates)
    (OUTPUT / "history-base.txt").write_text(base_sha + "\n", encoding="utf-8")
    return candidates, str(output.relative_to(audit_lib.ROOT)).replace("\\", "/")


def _suggest_label(title: str, subjects: str, paths: list[str]) -> tuple[str, str]:
    """Suggest a root-cause class from reviewable history evidence."""
    text = f"{title}\n{subjects}".lower()
    if any("manager" in path.lower() or "teardown" in path.lower() for path in paths) and any(token in text for token in ("lifecycle", "session", "stop", "ownership", "ledger")):
        return "cross-lifecycle", "manager or teardown ownership surface with lifecycle wording"
    if any("manager" in path.lower() or "session" in path.lower() for path in paths) and any(token in text for token in ("state", "ledger", "generation", "catalog", "registry", "identity")):
        return "cross-state", "shared state, ledger, generation, or registry wording on a lifecycle surface"
    if any(path.startswith((".github/", "Scripts/")) for path in paths) or "ci" in text or "validator" in text or "identity" in text:
        return "tooling", "workflow, validation, identity, or release surface"
    if any("Tests/" in path or "test" in path.lower() for path in paths) and not any(path.startswith("Packages/dev.unity2foxglove.sdk/Runtime") for path in paths):
        return "test-brittle", "test or fixture surface dominates the merge"
    if any(token in text for token in ("config", "contract", "name guard", "schema")):
        return "config", "configuration or contract wording"
    return "local", "single-surface fallback pending human classification"


def _appendix_labels() -> list[tuple[str, str]]:
    """Read the historical Appendix A labels when present."""
    text = PLAN.read_text(encoding="utf-8") if PLAN.is_file() else ""
    labels = []
    for line in text.splitlines():
        match = re.match(r"\|\s*#(\d+|unknown)\s*\|.*\|\s*(local|cross-lifecycle|cross-state|config|test-brittle|tooling)\s*\|", line, re.IGNORECASE)
        if match:
            labels.append((match.group(1), match.group(2).lower()))
    return labels


def _appendix_prs() -> list[str]:
    """Return the Appendix A candidate identifiers in recorded order."""
    return [pr for pr, _ in _appendix_labels()]


def _gate_check(candidates: list[dict[str, str]]) -> audit_lib.Check:
    """Recompute the historical and regenerated decision-gate ratios."""
    appendix = _appendix_labels()
    appendix_cross = sum(label in {"cross-lifecycle", "cross-state"} for _, label in appendix)
    candidate_cross = sum(row["suggested_label"] in {"cross-lifecycle", "cross-state"} for row in candidates)
    threshold = math.ceil(len(candidates) * 0.2) if candidates else 0
    appendix_counts = Counter(pr for pr, _ in appendix)
    candidate_counts = Counter(row["pr"] for row in candidates)
    labels_complete = bool(candidates) and appendix_counts == candidate_counts
    threshold_reached = appendix_cross >= threshold if candidates else False
    detail = f"appendix={appendix_cross}/{len(appendix)}; regenerated={candidate_cross}/{len(candidates)}; threshold={threshold}; threshold_reached={threshold_reached}; labels_complete={labels_complete}; cross_fix_needed_for_20_percent={max(0, threshold - appendix_cross)}"
    plan_text = PLAN.read_text(encoding="utf-8") if PLAN.is_file() else ""
    plan_status = next((line for line in plan_text.splitlines() if line.startswith("Status:")), "").lower()
    pending = "pending" in plan_status or "regenerat" in plan_status or "provisional" in plan_text.lower()
    status = "NOT_RUN" if not PLAN.is_file() else "FAIL" if not labels_complete else "MANUAL" if pending else "PASS" if not threshold_reached else "FAIL"
    return _check("197-W1-gate", "Plan/197... § Decision gate", "history", status, "git log --first-parent; gh pr list; compare Appendix A with fix-candidates.csv", "build/plan-audit/lifecycle_ownership/fix-candidates.csv", detail)


def _scope_check(candidates: list[dict[str, str]], evidence: str, base_sha: str) -> audit_lib.Check:
    """Check that the regenerated candidate identity multiset matches Appendix A."""
    appendix_prs = _appendix_prs()
    candidate_prs = [row["pr"] for row in candidates]
    appendix_counts = Counter(appendix_prs)
    candidate_counts = Counter(candidate_prs)
    duplicate_appendix = sorted(pr for pr, count in appendix_counts.items() if pr != "unknown" and count > 1)
    duplicate_candidates = sorted(pr for pr, count in candidate_counts.items() if pr != "unknown" and count > 1)
    missing = sorted(set(appendix_prs) - set(candidate_prs))
    extra = sorted(set(candidate_prs) - set(appendix_prs))
    unknown_appendix = appendix_counts.get("unknown", 0)
    unknown_candidates = candidate_counts.get("unknown", 0)
    identity_match = appendix_counts == candidate_counts
    unknown_explicit = unknown_appendix == unknown_candidates
    metadata_issues = _candidate_metadata_issues(candidates)
    okay = bool(candidates) and identity_match and not duplicate_appendix and not duplicate_candidates and unknown_explicit and not metadata_issues
    detail = (
        f"regenerated_candidates={len(candidates)}; appendix_rows={len(appendix_prs)}; base={base_sha}; "
        f"identity_match={identity_match}; duplicate_appendix={duplicate_appendix}; "
        f"duplicate_regenerated={duplicate_candidates}; missing={missing}; extra={extra}; "
        f"unknown_appendix={unknown_appendix}; unknown_regenerated={unknown_candidates}; "
        f"unknown_explicit={unknown_explicit}; metadata_issues={metadata_issues or 'none'}"
    )
    status = "PASS" if okay else "FAIL"
    return _check("197-W1-scope", "Plan/197... § 2 W1.1", "history", status, "python -B -m Scripts.validation.plan_audit.lifecycle_ownership", evidence, detail)


def _owner_map() -> audit_lib.Check:
    """Build a compact field, writer, lock, and lifetime map for four owners."""
    files = [Path(path) for path in _git_output(["git", "ls-files", "*.cs"]).splitlines() if _is_production_source(path)]
    payload: dict[str, object] = {}
    for owner, pattern in OWNER_PATTERNS.items():
        matched = [path for path in files if fnmatch.fnmatch(path.name, pattern) or (owner == "Ros2BridgeWorkerLease" and "Ros2BridgeWorkerLease" in str(path))]
        support_names = OWNER_SUPPORT_NAMES.get(owner, ())
        matched.extend(path for path in files if path.name in support_names and path not in matched)
        locks: list[str] = []
        lifetime: list[str] = []
        for relative in matched:
            text = (audit_lib.ROOT / relative).read_text(encoding="utf-8", errors="replace")
            masked = _mask_csharp_source(text)
            locks.extend(_line_refs(text, relative, re.compile(r"\block\s*\("), masked))
            lifetime.extend(_line_refs(text, relative, LIFETIME_WORDS, masked))
        fields, missing_writers = _collect_owner_fields(matched)
        payload[owner] = {
            "files": [str(path).replace("\\", "/") for path in matched],
            "fields": fields,
            "missing_writers": missing_writers,
            "owner": owner,
            "locks": locks,
            "lifetime": lifetime,
            "synchronization": {"mode": OWNER_SYNC_MODES[owner], "refs": locks},
        }
    (OUTPUT / "owner-map.json").write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    missing = [owner for owner, data in payload.items() if not _owner_evidence_valid(data)]
    status = "FAIL" if missing else "PASS"
    return _check("197-W1-ownermap", "Plan/197... § 2 W1.2 and Appendix B", "probe", status, "git ls-files '*.cs'; parse four lifecycle owner surfaces", "build/plan-audit/lifecycle_ownership/owner-map.json", f"owners={len(payload) - len(missing)}/4; missing={missing or 'none'}")


def _fresh_results_dir(identifier: str) -> Path:
    """Create an empty per-run TRX directory under the audit output."""
    path = OUTPUT / "TestResults" / identifier
    if path.exists():
        shutil.rmtree(path)
    path.mkdir(parents=True, exist_ok=True)
    return path


def _trx_passed(path: Path) -> int:
    """Count passed test results in a TRX file."""
    return sum(outcome == "Passed" for outcomes in _trx_outcomes(path).values() for outcome in outcomes)


def _tests_check(base_sha: str) -> audit_lib.Check:
    """Run Manager boundary tests and audit characterization coverage style."""
    source_issues = _canonical_test_source_issues(base_sha)
    if source_issues:
        return _check(
            "197-W1-tests",
            "Plan/197... § 2 W1.3",
            "auto",
            "FAIL",
            "verify canonical FoxgloveManagerTeardownTests source and project against audited base",
            "build/plan-audit/lifecycle_ownership/owner-map.json",
            f"canonical_test_issues={source_issues}",
        )
    restore = _run(["dotnet", "restore", "Packages/dev.unity2foxglove.sdk/Tests/Unit/FoxgloveSdk.UnitTests.csproj", "-p:IncludeManagerProductionBoundary=true"], "197-manager-restore", 300.0)
    if restore["exit_code"] != 0:
        return _check("197-W1-tests", "Plan/197... § 2 W1.3", "auto", "NOT_RUN", "dotnet restore Packages/dev.unity2foxglove.sdk/Tests/Unit/FoxgloveSdk.UnitTests.csproj -p:IncludeManagerProductionBoundary=true", restore["log"], "restore failed; Manager boundary test was not run")
    result_dir = _fresh_results_dir("focused")
    command = ["dotnet", "test", "--no-restore", "Packages/dev.unity2foxglove.sdk/Tests/Unit/FoxgloveSdk.UnitTests.csproj", "-p:IncludeManagerProductionBoundary=true", "--filter", "FullyQualifiedName~FoxgloveManagerTeardownTests", "--logger", "trx;LogFileName=lifecycle-ownership.trx", "--results-directory", str(result_dir.relative_to(audit_lib.ROOT))]
    result = _run(command, "197-W1-tests", 600.0)
    result_file = result_dir / "lifecycle-ownership.trx"
    passed = _trx_passed(result_file)
    missing = _missing_expected_tests(result_file)
    canonical_issues = _trx_canonical_test_issues(result_file)
    okay = result["exit_code"] == 0 and result_file.is_file() and not missing and not canonical_issues
    detail = f"test_exit={result['exit_code']}; passed_tests={passed}; missing_expected_tests={missing or 'none'}; canonical_test_issues={canonical_issues or 'none'}; pr717_test={TEST_REL}"
    return _check("197-W1-tests", "Plan/197... § 2 W1.3", "auto", "PASS" if okay else "FAIL", "dotnet restore ... -p:IncludeManagerProductionBoundary=true; dotnet test --no-restore ... --filter FullyQualifiedName~FoxgloveManagerTeardownTests", result["log"], detail)


def _mutation_checks() -> list[audit_lib.Check]:
    """Ensure order and cleanup mutations make passing baseline tests fail."""
    path = audit_lib.ROOT / TEARDOWN_REL
    baseline_dir = _fresh_results_dir("baseline-mutation")
    baseline_command = [
        "dotnet", "test", "--no-restore", "Packages/dev.unity2foxglove.sdk/Tests/Unit/FoxgloveSdk.UnitTests.csproj",
        "-p:IncludeManagerProductionBoundary=true", "--filter", "FullyQualifiedName~FoxgloveManagerTeardownTests",
        "--logger", "trx;LogFileName=baseline-mutation.trx", "--results-directory",
        str(baseline_dir.relative_to(audit_lib.ROOT)),
    ]
    baseline = _run(baseline_command, "197-W1-mutation-baseline", 600.0)
    baseline_file = baseline_dir / "baseline-mutation.trx"
    baseline_passed = _trx_passed(baseline_file)
    baseline_missing = _missing_expected_tests(baseline_file)
    checks: list[audit_lib.Check] = []
    for identifier, old, new in (
        ("197-W1-mutation-order", "stopRuntime,\n                resetSensorClock,", "resetSensorClock,\n                stopRuntime,"),
        ("197-W1-mutation-cleanup", "clearClientEvents,\n                resetChannelIds,", "() => { },\n                resetChannelIds,"),
    ):
        try:
            mutation_dir = _fresh_results_dir(identifier)
            with audit_lib.mutate(path, old, new):
                result = _run(
                    [
                        "dotnet", "test", "--no-restore", "Packages/dev.unity2foxglove.sdk/Tests/Unit/FoxgloveSdk.UnitTests.csproj",
                        "-p:IncludeManagerProductionBoundary=true", "--filter", "FullyQualifiedName~FoxgloveManagerTeardownTests",
                        "--logger", f"trx;LogFileName={identifier}.trx", "--results-directory",
                        str(mutation_dir.relative_to(audit_lib.ROOT)),
                    ],
                    identifier,
                    600.0,
                )
            mutation_file = mutation_dir / f"{identifier}.trx"
            expected_names = MUTATION_EXPECTED_TESTS[identifier]
            expected_failure = _expected_test_failure(mutation_file, expected_names)
            okay = baseline["exit_code"] == 0 and not baseline_missing and result["exit_code"] != 0 and expected_failure
            status = "PASS" if okay else "FAIL"
            detail = f"baseline_exit={baseline['exit_code']}; baseline_passed={baseline_passed}; baseline_missing={baseline_missing or 'none'}; mutated_exit={result['exit_code']}; expected_tests={expected_names}; expected_test_failure={expected_failure}"
            evidence = result["log"]
        except Exception as exc:
            status = "FAIL"
            detail = str(exc)
            evidence = f"build/plan-audit/lifecycle_ownership/logs/{identifier}.log"
        checks.append(_check(identifier, "Plan/197... § 2 W1.3 mutation", "mutation", status, "dotnet test ... --filter FullyQualifiedName~FoxgloveManagerTeardownTests", evidence, detail))
    return checks


def _stop_check() -> audit_lib.Check:
    """Record that W2 and W3 remain stopped until the corrected gate is reviewed."""
    text = PLAN.read_text(encoding="utf-8") if PLAN.is_file() else ""
    status_line = next((line for line in text.splitlines() if line.startswith("Status:")), "")
    pending = "pending" in status_line.lower() or "regenerat" in status_line.lower()
    stopped = "W2/W3 remain stopped" in status_line or "W2/W3 are not started" in text
    status = "PASS" if stopped else "FAIL"
    detail = "decision gate stops after W1 pending manual review" if pending and stopped else "decision gate stops after W1" if stopped else "W2/W3 are not started, but the plan does not record that stop"
    return _check("197-W2-W3", "Plan/197... § Decision gate", "history", status, "read Plan/197... decision gate", "build/plan-audit/lifecycle_ownership/plan.log", detail)


def _manual_notes(checks: list[audit_lib.Check]) -> None:
    """Write detailed manual review steps required by the plan."""
    path = OUTPUT / "manual.md"
    existing = path.read_text(encoding="utf-8") if path.is_file() else ""
    extra = """
## Human review required

1. Review every row in `build/plan-audit/lifecycle_ownership/fix-candidates.csv`. Confirm the PR title, touched surface, and suggested root-cause label against the merged diff. Record reclassifications in the Plan197 appendix.
2. Confirm whether the current-main candidate count and cross-lifecycle plus cross-state ratio justify stopping after W1. The old 264/14 snapshot is not decision evidence.
3. If the reviewed ratio reaches 20 percent or higher, decide whether to start W2. Do not start W2 or W3 from this audit PR.
4. If the Manager characterization tests remain source-text based or lack startup-failure and residue behavior coverage, record the missing test names and whether a separate test PR is required.
"""
    path.write_text(existing.rstrip() + "\n" + extra, encoding="utf-8")


def main() -> int:
    """Run the complete Phase197 audit and write evidence artifacts."""
    OUTPUT.mkdir(parents=True, exist_ok=True)
    before, before_sha = audit_lib.worktree_snapshot(exclude=("build/", SCRIPT_REL))
    (OUTPUT / "state-before.txt").write_text(before, encoding="utf-8")
    base_sha = audit_lib.resolve_revision()
    checks: list[audit_lib.Check] = [_plan_check(base_sha)]
    try:
        candidates, evidence = _history_candidates(base_sha)
        checks.extend([_scope_check(candidates, evidence, base_sha), _gate_check(candidates), _owner_map(), _tests_check(base_sha)])
        checks.extend(_mutation_checks())
    except Exception as exc:
        checks.append(_check("197-W1-scope", "Plan/197... § 2 W1.1", "history", "NOT_RUN", "python -B -m Scripts.validation.plan_audit.lifecycle_ownership", "build/plan-audit/lifecycle_ownership/logs/exception.log", str(exc)))
        (OUTPUT / "logs" / "exception.log").write_text(str(exc) + "\n", encoding="utf-8")
    checks.append(_stop_check())
    after, after_sha = audit_lib.worktree_snapshot(exclude=("build/", SCRIPT_REL))
    (OUTPUT / "state-after.txt").write_text(after, encoding="utf-8")
    unchanged = before_sha == after_sha
    audit_lib.write_verdict(TOPIC, PHASE, checks, base_sha, OUTPUT, unchanged)
    _manual_notes(checks)
    return 0 if unchanged and all(check.status == "PASS" for check in checks) else 1


if __name__ == "__main__":
    raise SystemExit(main())
