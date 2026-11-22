"""Text scanning primitives for source-text inventory."""
from __future__ import annotations
import re
from pathlib import Path
PATH_LITERAL_PATTERN = re.compile(
    r'"([^"\r\n]+(?:\.cs|\.py|\.md|\.json|\.yml|\.yaml|\.csproj|\.props|\.targets|\.asmdef|\.meta|\.txt|\.xml|\.cpp|\.h|\.dll|\.so))"'
)
PATH_LIKE_LITERAL_PATTERN = re.compile(r'"([^"\r\n]*[/\\][^"\r\n]*)"')
SEED_READER_ALTERNATIVES = (
    r"ReadRepoText|ReadRequiredRepoText|ReadText|ReadRuntimeSource|"
    r"ReadPointCloudPublisherSources|ReadCameraPublisherSources|ReadProductPublisherText|"
    r"ReadRepoDirectoryText|Text|PhaseValidationSourceHelpers\s*\.\s*Read[A-Za-z_]\w*"
    r"(?:Sources?|SourcePath|Text)|TestSources\s*\.\s*(?:Text|Runtime|ExtractMethod|"
    r"Slice|SourceGeneratorSources)|RuntimeSource"
)
READER_CALL_PATTERN = re.compile(rf"\b(?P<reader>{SEED_READER_ALTERNATIVES})\s*\(")
SEED_READER_NAME = re.compile(rf"(?:{SEED_READER_ALTERNATIVES})")
_DELEGATE_TYPE_PATTERN = r"(?:global::)?[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*(?:\s*<[^;=]+>)?\??"
_DELEGATE_METHOD_TARGET_PATTERN = r"(?:global::)?[A-Za-z_]\w*(?:\s*<[^;{}()]+>)?(?:\s*\.\s*(?:global::)?[A-Za-z_]\w*(?:\s*<[^;{}()]+>)?)*"
_KNOWN_DELEGATE_WRAPPER_NAMES = frozenset(("Run", "Ci", "DefaultOnly", "Local", "Manual", "PhaseValidationCase.Run")); _DELEGATE_TARGET_NAME_HINTS = frozenset(("Action", "Callback", "Delegate", "Factory", "Handler")); _DIRECT_DELEGATE_TARGET_NAME_HINTS = frozenset(("Action",))
_DELEGATE_DECLARATION_MODIFIERS = r"(?:(?:public|private|protected|internal|static|readonly|const|volatile|new|unsafe|async|extern|abstract|sealed|partial|virtual|override|ref|out|in)\s+)*"
_DELEGATE_ASSIGNMENT_PATTERN = re.compile(
    rf"\b{_DELEGATE_DECLARATION_MODIFIERS}(?:(?P<type>(?:{_DELEGATE_TYPE_PATTERN}|var))\s+)?"
    rf"(?P<name>[A-Za-z_]\w*)\s*=\s*(?P<target>[^;]+);"
)
DELEGATE_STATE_NON_DELEGATE = "NON_DELEGATE"
DELEGATE_STATE_DIRECT = "DIRECT"
DELEGATE_STATE_KNOWN_WRAPPER = "KNOWN_WRAPPER"
DELEGATE_STATE_LAMBDA_FORWARD = "LAMBDA_FORWARD"
DELEGATE_STATE_UNSUPPORTED = "UNSUPPORTED_DELEGATE_FLOW"
def _normalize_delegate_expression(value: str) -> str:
    """Normalize spacing that is insignificant in delegate expressions."""
    normalized = re.sub(r"\s+", " ", value).strip()
    normalized = re.sub(r"\s*\.\s*", ".", normalized)
    normalized = re.sub(r"\s*([<>?])\s*", r"\1", normalized)
    return normalized
def _strip_outer_parentheses(value: str) -> str:
    """Remove balanced parentheses that wrap a complete expression."""
    value = value.strip()
    while value.startswith("("):
        close = _balanced_call_end(value, 0)
        if close != len(value) - 1:
            break
        value = value[1:-1].strip()
    return value
def _is_delegate_like_target(value: str) -> bool:
    """Return whether an expression has a method-group-like token shape."""
    normalized = _strip_outer_parentheses(_normalize_delegate_expression(value))
    return re.fullmatch(_DELEGATE_METHOD_TARGET_PATTERN, normalized) is not None
def _delegate_type_names(text: str, masked: str) -> set[str]:
    """Return built-in, declared, and aliased delegate type names in one file."""
    names = {"Action", "Func", "Predicate"}
    names.update(
        match.group("name")
        for match in re.finditer(
            r"\bdelegate\b[^;{}()]*?\b(?P<name>[A-Za-z_]\w*)"
            r"(?:\s*<[^;{}()]*>)?\s*\(", masked
        )
    )
    aliases = {
        match.group("name"): match.group("target")
        for match in re.finditer(
            r"\busing\s+(?P<name>[A-Za-z_]\w*)\s*=\s*(?P<target>[^;]+);", masked
        )
    }
    for _ in range(len(aliases)):
        for alias, target in aliases.items():
            target_name = _normalize_delegate_expression(target).rstrip("?").split("<", 1)[0]
            target_name = target_name.rsplit(".", 1)[-1]
            if target_name in names:
                names.add(alias)
    return names
def _is_delegate_type_name(value: str, delegate_type_names: set[str]) -> bool:
    """Return whether a declared type resolves to a known delegate name."""
    normalized = _normalize_delegate_expression(value).rstrip("?").split("<", 1)[0]
    return normalized.rsplit(".", 1)[-1].removeprefix("global::") in delegate_type_names
def _has_delegate_target_hint(value: str) -> bool: """Return whether a target name carries a delegate-like suffix."""; return any(_normalize_delegate_expression(value).rsplit(".", 1)[-1].split("<", 1)[0].endswith(hint) for hint in _DELEGATE_TARGET_NAME_HINTS)
def _has_direct_delegate_target_hint(value: str) -> bool: """Return whether a target name carries a directly supported suffix."""; return any(_normalize_delegate_expression(value).rsplit(".", 1)[-1].split("<", 1)[0].endswith(hint) for hint in _DIRECT_DELEGATE_TARGET_NAME_HINTS)
def _is_delegate_assignment(
    target: str,
    declared_type: str,
    name: str,
    delegate_variables: set[str],
    delegate_methods: set[str],
    method_names: set[str],
    delegate_type_names: set[str],
    invocation_text: str,
    delegate_boundary_names: set[str] | None = None,
    delegate_wrapper_names: set[str] | None = None,
) -> str:
    """Classify one binding in the finite delegate-flow state set."""
    target_names = set(re.findall(r"\b[A-Za-z_]\w*\b", target))
    known_wrapper = (
        bool(declared_type)
        and declared_type != "var"
        and _is_delegate_type_name(declared_type, delegate_type_names)
    )
    known_wrapper = known_wrapper or (
        name in delegate_variables
        or bool(target_names & delegate_variables)
        or bool(target_names & delegate_methods)
        or _is_delegate_conversion(target, delegate_type_names)
    )
    if "=>" in target or re.search(r"\bdelegate\s*\(", target) is not None:
        return DELEGATE_STATE_LAMBDA_FORWARD
    if known_wrapper:
        return DELEGATE_STATE_KNOWN_WRAPPER
    normalized_target = _strip_outer_parentheses(_normalize_delegate_expression(target)); target_call = re.match(r"(?P<name>(?:global::)?[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*)\s*\(", normalized_target); target_call_known = bool(target_call and _has_direct_delegate_target_hint(target_call.group("name")))
    if (_is_delegate_like_target(target) or target_call_known) and (bool(target_names & method_names) or _has_direct_delegate_target_hint(target) or target_call_known):
        return DELEGATE_STATE_DIRECT
    invocation_prefix = invocation_text.split("(", 1)[0].strip()
    invocation_identity = re.sub(r"[^A-Za-z0-9_:.]+$", "", invocation_prefix)
    invocation_name = invocation_identity.rsplit(".", 1)[-1]
    wrapper_candidate = _is_delegate_like_target(target) and "=>" not in invocation_text and re.search(rf"\b{re.escape(name)}\b", invocation_text) is not None and bool(delegate_wrapper_names and invocation_identity in delegate_wrapper_names) and _has_delegate_target_hint(target)
    candidate = bool(invocation_text) and (
        bool(delegate_boundary_names and name in delegate_boundary_names)
        or invocation_name == name
        or (
            invocation_name in {"Invoke", "DynamicInvoke"}
            and _is_delegate_like_target(target)
            and _has_delegate_target_hint(target)
        )
        or re.search(rf"\b{re.escape(name)}\s*!?\s*\(", invocation_text) is not None
        or wrapper_candidate
    )
    if candidate: return DELEGATE_STATE_UNSUPPORTED
    return DELEGATE_STATE_NON_DELEGATE
_DELEGATE_INVOCATION_CACHE: dict[str, set[str]] = {}
def _delegate_invocation_names(masked: str) -> set[str]:
    """Return names used in direct delegate-style invocations."""
    names = _DELEGATE_INVOCATION_CACHE.get(masked)
    if names is None:
        names = {
            match.group("name")
            for match in re.finditer(
                r"\b(?P<name>[A-Za-z_]\w*)\s*(?:!?\s*\(|!?\s*\.\s*(?:Invoke|DynamicInvoke)\s*\()",
                masked,
            )
        }
        _DELEGATE_INVOCATION_CACHE[masked] = names
    return names
def _delegate_invocation(masked: str, start: int, name: str) -> str:
    """Return the first invocation of a binding after its assignment."""
    invocation = re.search(
        rf"\b{re.escape(name)}(?:\s*!?\s*\([^;]*?\)|\s*!?\s*\.\s*(?:Invoke|DynamicInvoke)\s*\([^;]*?\)|"
        rf"\s*\?\.\s*(?:Invoke|DynamicInvoke)\s*\([^;]*?\))|"
        rf"\b[A-Za-z_]\w*(?:\s*<[^;{{}}()]*>)?\s*\([^;]*\b(?:[A-Za-z_]\w*\s*:\s*)?{re.escape(name)}\b[^;]*\)\s*;",
        masked[start:],
    )
    return re.sub(r"\s+", " ", invocation.group()).strip() if invocation else ""
def _delegate_invocation_needed(
    target: str,
    declared_type: str,
    name: str,
    delegate_variables: set[str],
    method_names: set[str],
    delegate_boundary_names: set[str] | None = None,
) -> bool:
    """Return whether a binding needs an invocation scan for forwarding."""
    target_names = set(re.findall(r"\b[A-Za-z_]\w*\b", target))
    return (
        name in delegate_variables
        or bool(delegate_boundary_names and name in delegate_boundary_names)
        or bool(target_names & method_names)
        or target.lstrip().startswith(("(", "new "))
        or "." in target
        or any(marker in target for marker in ("<", "?", "=>", "delegate"))
    )
def _delegate_invocation_text(
    masked: str,
    start: int,
    target: str,
    declared_type: str,
    name: str,
    delegate_variables: set[str],
    method_names: set[str],
    delegate_boundary_names: set[str] | None = None,
    scope_start: int = 0,
    scope_end: int | None = None,
) -> str:
    """Return the normalized invocation evidence for a delegate binding."""
    scope_end = len(masked) if scope_end is None else scope_end
    scoped_masked = masked[scope_start:scope_end]
    scoped_start = max(start - scope_start, 0)
    if (
        name not in _delegate_invocation_names(scoped_masked)
        and not _delegate_invocation_needed(
            target,
            declared_type,
            name,
            delegate_variables,
            method_names,
            delegate_boundary_names,
        )
        and re.search(rf"\b[A-Za-z_]\w*\s*\([^;]*\b{re.escape(name)}\b[^;]*\)", scoped_masked[scoped_start:]) is None
    ):
        return ""
    return _delegate_invocation(scoped_masked, scoped_start, name)
def _is_delegate_conversion(target: str, delegate_type_names: set[str]) -> bool:
    """Return whether a target uses an allowlisted delegate cast or construction."""
    normalized = _strip_outer_parentheses(_normalize_delegate_expression(target))
    cast = re.fullmatch(
        rf"\(\s*(?P<type>{_DELEGATE_TYPE_PATTERN})\s*\)\s*"
        rf"(?P<target>.+)",
        normalized,
    )
    if cast and _is_delegate_type_name(cast.group("type"), delegate_type_names):
        return _is_delegate_like_target(cast.group("target"))
    constructor = re.fullmatch(
        rf"new\s+(?P<type>{_DELEGATE_TYPE_PATTERN})\s*\(\s*"
        rf"(?P<target>.+?)\s*\)",
        normalized,
    )
    return bool(constructor and _is_delegate_type_name(constructor.group("type"), delegate_type_names) and _is_delegate_like_target(constructor.group("target")))
def _normalize_delegate_value(value: str, delegate_type_names: set[str]) -> str:
    """Normalize a delegate argument to its innermost expression."""
    normalized = re.sub(r"^\s*[A-Za-z_]\w*\s*:\s*", "", _normalize_delegate_expression(value)).strip()
    normalized = _strip_outer_parentheses(normalized).rstrip("!").strip()
    cast = re.fullmatch(rf"\(\s*(?P<type>{_DELEGATE_TYPE_PATTERN})\s*\)\s*(?P<target>.+)", normalized)
    if cast and _is_delegate_type_name(cast.group("type"), delegate_type_names):
        return _normalize_delegate_value(cast.group("target"), delegate_type_names)
    constructor = re.fullmatch(rf"new\s+(?P<type>{_DELEGATE_TYPE_PATTERN})\s*\(\s*(?P<target>.+?)\s*\)", normalized)
    if constructor and _is_delegate_type_name(constructor.group("type"), delegate_type_names):
        return _normalize_delegate_value(constructor.group("target"), delegate_type_names)
    return normalized
_ACTIVE_READER_PATTERN: list[re.Pattern[str]] = [READER_CALL_PATTERN]
_PATH_READER_PATTERNS: dict[str, re.Pattern[str]] = {}
VARIABLE_READER_PATTERN = re.compile(
    r"(?:var|string)\s+(?P<name>[A-Za-z_]\w*)\s*=\s*"
    r"(?P<reader>ReadRepoText|ReadRequiredRepoText|PhaseValidationSourceHelpers\."
    r"ReadRequiredRepoText|TestSources\.Text)\s*\(\s*(?P<argument>[^)]*?)\)",
    re.DOTALL,
)
ARRAY_PATTERN = re.compile(
    r"(?:var|string\[\])\s+(?P<name>[A-Za-z_]\w*)\s*=\s*new\[\]\s*\{(?P<body>[^}]*)\}",
    re.DOTALL,
)
CONSTANT_PATH_PATTERN = re.compile(
    r"(?:const\s+string|private\s+const\s+string)\s+(?P<name>[A-Za-z_]\w*)\s*=\s*\"(?P<path>[^\"]+)\"\s*;?",
    re.DOTALL,
)
STATIC_READONLY_PATH_PATTERN = re.compile(
    r"(?:static\s+readonly\s+string|readonly\s+static\s+string)\s+"
    r"(?P<name>[A-Za-z_]\w*)\s*=\s*\"(?P<path>[^\"]+)\"\s*;?",
    re.DOTALL,
)
RECEIVER_PATTERN = re.compile(
    r"(?P<receiver>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\.(?:Contains|IndexOf|DoesNotContain)\s*\("
)
ASSERTION_CALL_PATTERN = re.compile(r"\bAssert\.(?:Contains|DoesNotContain)\s*\(")
DERIVED_METHOD_PATTERN = re.compile(r"\.(?:Contains|IndexOf|DoesNotContain)\s*\(")
HARNESS_CALL_PATTERN = re.compile(r"\b(?:Check|Assert\.(?:True|False))\s*\(")
PHASE_LABEL_PATTERN = re.compile(r'"(?P<label>\d+[A-Za-z]?[-A-Za-z0-9]*-[A-Za-z0-9]+:[^"]+)"')
_MASK_CACHE: dict[str, str] = {}
_COMMENT_CACHE: dict[str, str] = {}
_REPO_FILE_CACHE: dict[tuple[str, str], Path | None] = {}
_BRACE_CACHE: dict[str, list[tuple[int, ...]]] = {}
_LINE_START_CACHE: dict[str, list[int]] = {}
_PRODUCT_TYPE_CACHE: dict[tuple[str, str], set[str]] = {}
def _clear_text_caches() -> None:
    """Clear text and repository caches before scanning a tree."""
    _MASK_CACHE.clear()
    _COMMENT_CACHE.clear()
    _REPO_FILE_CACHE.clear()
    _BRACE_CACHE.clear()
    _LINE_START_CACHE.clear()
    _PRODUCT_TYPE_CACHE.clear()
    _DELEGATE_INVOCATION_CACHE.clear()
    _PATH_READER_PATTERNS.clear()
    _ACTIVE_READER_PATTERN[0] = READER_CALL_PATTERN
def _reader_call_pattern() -> re.Pattern[str]:
    """Return the reader-call pattern active for the file being scanned."""
    return _ACTIVE_READER_PATTERN[0]
def _activate_reader_pattern(path: Path | None = None) -> None:
    """Select the discovered reader pattern for one validation file, or the seed pattern."""
    _ACTIVE_READER_PATTERN[0] = _PATH_READER_PATTERNS.get(str(path), READER_CALL_PATTERN) if path else READER_CALL_PATTERN
def classify(target: str, line: str) -> str:
    """Classify one source-text assertion as boundary, wiring, or implementation."""
    if not target or "<unresolved>" in target:
        return "UNRESOLVED"
    if "<directory>" in target:
        return "UNKNOWN"
    if re.search(
        r"!\s*(?:[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*\s*\.\s*(?:Contains|IndexOf|DoesNotContain)|Assert\.Contains)\s*\(",
        line,
    ):
        return "A"
    if re.search(r"\bAssert\.DoesNotContain\s*\(", line):
        return "A"
    if target.startswith(".github/") or target.startswith("Scripts/release/run_ci"):
        return "B"
    if (
        target.endswith(".md")
        or target.endswith("package.json")
        or target.endswith("/Tests/Runtime/PhaseValidationRegistry.cs")
    ):
        return "B"
    return "C"
def _normalise_path(value: str) -> str:
    """Normalise a repository-relative path for stable reports."""
    value = value.strip().strip('"').replace("\\", "/")
    return (
        ""
        if value.startswith("/") or re.match(r"^[A-Za-z]:", value) or ".." in value.split("/")
        else value
    )
def _repo_file(root: Path, value: str) -> Path | None:
    """Return a bounded repository file, rejecting links and oversized inputs."""
    cache_key = (str(root.resolve()), value)
    if cache_key in _REPO_FILE_CACHE:
        return _REPO_FILE_CACHE[cache_key]
    if not value or value.startswith("<"):
        _REPO_FILE_CACHE[cache_key] = None
        return None
    candidate = (root / value).resolve()
    try:
        candidate.relative_to(root.resolve())
    except ValueError:
        _REPO_FILE_CACHE[cache_key] = None
        return None
    result = (
        candidate if candidate.is_file() and candidate.stat().st_size <= 8 * 1024 * 1024 else None
    )
    _REPO_FILE_CACHE[cache_key] = result
    return result
def _extract_argument_path(argument: str, paths: dict[str, str]) -> str | None:
    """Resolve a reader argument to a repository path or a documented path set."""
    argument = argument.strip()
    literal = PATH_LITERAL_PATTERN.fullmatch(argument)
    if literal:
        return _normalise_path(literal.group(1))
    if argument in paths:
        return paths[argument]
    suffix = re.fullmatch(r"(?P<name>[A-Za-z_]\w*)\s*\+\s*\"(?P<suffix>[^\"]*)\"", argument)
    if suffix and suffix.group("name") in paths:
        suffix_value = suffix.group("suffix").replace("\\", "/")
        return ";".join(
            value.rstrip("/") + "/" + suffix_value.lstrip("/")
            for value in paths[suffix.group("name")].split(";")
        )
    literal = PATH_LITERAL_PATTERN.search(argument)
    if literal and argument == literal.group(0):
        return _normalise_path(literal.group(1))
    path_like = PATH_LIKE_LITERAL_PATTERN.fullmatch(argument)
    if path_like:
        return _normalise_path(path_like.group(1))
    if "," in argument:
        return _extract_argument_path(argument.split(",", 1)[1], paths)
    return None
def _reader_target(reader: dict[str, object], paths: dict[str, str]) -> str | None:
    """Resolve a recognized reader using its documented path argument position."""
    arguments = _split_arguments(str(reader["argument"]))
    name = re.sub(r"\s+", "", str(reader.get("name", "")))
    path_index = 1 if name.endswith("ReadText") and len(arguments) > 1 else 0
    if "ReadRepoDirectoryText" in name:
        path_index = 0
    if path_index >= len(arguments):
        return None
    target = _extract_argument_path(arguments[path_index], paths)
    if target is None and not SEED_READER_NAME.fullmatch(name):
        target = next((value for value in map(lambda item: _extract_argument_path(item, paths), arguments) if value), None)
    return target
def _split_arguments(argument: str) -> list[str]:
    """Split top-level C# call arguments while preserving nested expressions."""
    parts: list[str] = []
    depth = 0
    mode = ""
    start = 0
    index = 0
    while index < len(argument):
        char = argument[index]
        next_char = argument[index + 1] if index + 1 < len(argument) else ""
        if mode == "verbatim":
            if char == '"':
                if next_char == '"':
                    index += 2
                    continue
                mode = ""
            index += 1
            continue
        if mode in {"string", "char"}:
            if char == "\\":
                index += 2
                continue
            if (mode == "string" and char == '"') or (mode == "char" and char == "'"):
                mode = ""
            index += 1
            continue
        if char == "@" and next_char == '"':
            mode = "verbatim"
            index += 2
            continue
        if char == '"':
            mode = "string"
        elif char == "'":
            mode = "char"
        elif char in "([{":
            depth += 1
        elif char in ")]}":
            depth = max(0, depth - 1)
        elif char == "," and depth == 0:
            parts.append(argument[start:index].strip())
            start = index + 1
        index += 1
    parts.append(argument[start:].strip())
    return parts if any(parts) else []
def _balanced_call_end(text: str, open_index: int) -> int | None:
    """Return the closing parenthesis for a call while respecting C# literals."""
    depth = 0
    mode = ""
    index = open_index
    while index < len(text):
        char = text[index]
        next_char = text[index + 1] if index + 1 < len(text) else ""
        if mode == "line_comment":
            if char == "\n":
                mode = ""
            index += 1
            continue
        if mode == "block_comment":
            if char == "*" and next_char == "/":
                mode = ""
                index += 2
            else:
                index += 1
            continue
        if mode == "verbatim":
            if char == '"':
                if next_char == '"':
                    index += 2
                    continue
                mode = ""
            index += 1
            continue
        if mode in {"string", "char"}:
            if char == "\\":
                index += 2
                continue
            if (mode == "string" and char == '"') or (mode == "char" and char == "'"):
                mode = ""
            index += 1
            continue
        if char == "/" and next_char == "/":
            mode = "line_comment"
            index += 2
            continue
        if char == "/" and next_char == "*":
            mode = "block_comment"
            index += 2
            continue
        if char == "@" and next_char == '"':
            mode = "verbatim"
            index += 2
            continue
        if char == '"':
            mode = "string"
            index += 1
            continue
        if char == "'":
            mode = "char"
            index += 1
            continue
        if char == "(":
            depth += 1
        elif char == ")":
            depth -= 1
            if depth == 0:
                return index
        index += 1
    return None
def _line_starts(text: str) -> list[int]:
    """Return character offsets for each line in a source buffer."""
    cached = _LINE_START_CACHE.get(text)
    if cached is not None:
        return cached
    result = [0] + [index + 1 for index, char in enumerate(text) if char == "\n"]
    _LINE_START_CACHE[text] = result
    return result
def _line_for_offset(starts: list[int], offset: int) -> int:
    """Map a character offset to a zero-based line number."""
    line = 0
    for index, start in enumerate(starts):
        if start > offset:
            break
        line = index
    return line
def _call_spans(
    lines: list[str],
    pattern: re.Pattern[str],
    text: str | None = None,
    searchable: str | None = None,
) -> list[dict[str, object]]:
    """Return balanced call spans for a regular-expression call pattern."""
    text = text if text is not None else "\n".join(lines)
    searchable = searchable if searchable is not None else _strip_csharp_comments_and_strings(text)
    starts = _line_starts(text)
    calls: list[dict[str, object]] = []
    for match in pattern.finditer(searchable):
        open_index = searchable.find("(", match.start(), match.end())
        close_index = _balanced_call_end(text, open_index)
        if close_index is None:
            continue
        calls.append(
            {
                "start": match.start(),
                "end": close_index + 1,
                "start_line": _line_for_offset(starts, match.start()),
                "end_line": _line_for_offset(starts, close_index),
                "name": match.groupdict().get("reader", match.group(0)),
                "receiver": match.groupdict().get("receiver"),
                "argument": text[open_index + 1 : close_index],
                "text": text[match.start() : close_index + 1],
            }
        )
    return calls
def _split_first_argument(argument: str) -> str:
    """Return the first top-level argument from a C# call."""
    depth = 0
    mode = ""
    index = 0
    while index < len(argument):
        char = argument[index]
        next_char = argument[index + 1] if index + 1 < len(argument) else ""
        if mode == "verbatim":
            if char == '"':
                if next_char == '"':
                    index += 2
                    continue
                mode = ""
            index += 1
            continue
        if mode in {"string", "char"}:
            if char == "\\":
                index += 2
                continue
            if (mode == "string" and char == '"') or (mode == "char" and char == "'"):
                mode = ""
            index += 1
            continue
        if char == "@" and next_char == '"':
            mode = "verbatim"
            index += 2
            continue
        if char == '"':
            mode = "string"
        elif char == "'":
            mode = "char"
        elif char in "([{":
            depth += 1
        elif char in ")]}" and depth:
            depth -= 1
        elif char == "," and depth == 0:
            return argument[:index].strip()
        index += 1
    return argument.strip()
def _call_context(lines: list[str], call: dict[str, object]) -> str:
    """Return the surrounding statement for polarity and source-origin checks."""
    text = str(call.get("text_file") or "\n".join(lines))
    start = int(call["start"])
    end = int(call["end"])
    masked = str(call.get("masked") or _strip_csharp_comments_and_strings(text))
    statement_start = (
        max(
            masked.rfind(";", 0, start),
            masked.rfind("{", 0, start),
            masked.rfind("}", 0, start),
            masked.rfind("&&", 0, start) + 1,
            masked.rfind("||", 0, start) + 1,
        )
        + 1
    )
    return text[statement_start:end]
def _balanced_brace_end(text: str, open_index: int) -> int | None:
    """Return the matching closing brace in masked source text."""
    depth = 0
    for index in range(open_index, len(text)):
        depth += (text[index] == "{") - (text[index] == "}")
        if depth == 0:
            return index
    return None
def _brace_paths(lines: list[str]) -> list[tuple[int, ...]]:
    """Return a lexical brace scope for each source line."""
    cache_key = "\n".join(lines)
    cached = _BRACE_CACHE.get(cache_key)
    if cached is not None:
        return cached
    scopes: list[tuple[int, ...]] = []
    stack: list[int] = []
    next_id = 0
    mode = ""
    block_comment = False
    for line in lines:
        scopes.append(tuple(stack))
        index = 0
        while index < len(line):
            char = line[index]
            next_char = line[index + 1] if index + 1 < len(line) else ""
            if block_comment:
                if char == "*" and next_char == "/":
                    block_comment = False
                    index += 2
                else:
                    index += 1
                continue
            if mode:
                if mode == "verbatim" and char == '"':
                    if next_char == '"':
                        index += 2
                        continue
                    mode = ""
                elif mode in {"normal", "char"} and char == "\\":
                    index += 2
                    continue
                elif (mode == "normal" and char == '"') or (mode == "char" and char == "'"):
                    mode = ""
                index += 1
                continue
            if char == "/" and next_char == "/":
                break
            if char == "/" and next_char == "*":
                block_comment = True
                index += 2
                continue
            if char == "@" and next_char == '"':
                mode = "verbatim"
                index += 2
                continue
            if char == '"':
                mode = "normal"
                index += 1
                continue
            if char == "'":
                mode = "char"
                index += 1
                continue
            if char == "{":
                stack.append(next_id)
                next_id += 1
            elif char == "}" and stack:
                stack.pop()
            index += 1
    _BRACE_CACHE[cache_key] = scopes
    return scopes
def _normalize_csharp_expression(text: str) -> str:
    """Normalize code spacing and comments while retaining every literal character."""
    if '"""' in text or re.search(r'\$@?"|@\$', text):
        return text.strip()
    pattern = (
        r"""(?P<literal>@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*')"""
        r"|(?://[^\n]*|/\*.*?\*/|\s+)+"
    )
    return re.sub(
        pattern, lambda match: match.group("literal") or " ", text, flags=re.DOTALL
    ).strip()
def _strip_csharp_comments_and_strings(text: str) -> str:
    """Remove C# comments and string literals before symbol matching."""
    cached = _MASK_CACHE.get(text)
    if cached is not None:
        return cached
    output: list[str] = []
    index = 0
    mode = ""
    while index < len(text):
        char = text[index]
        next_char = text[index + 1] if index + 1 < len(text) else ""
        if mode == "line":
            output.append(char if char == "\n" else " ")
            mode = "" if char == "\n" else mode
            index += 1
            continue
        if mode == "block":
            if char == "*" and next_char == "/":
                output.extend((" ", " "))
                index += 2
                mode = ""
            else:
                output.append("\n" if char == "\n" else " ")
                index += 1
            continue
        if mode in {"string", "verbatim", "char"}:
            if mode == "verbatim" and char == '"':
                if next_char == '"':
                    output.extend((" ", " "))
                    index += 2
                    continue
                mode = ""
            elif mode in {"string", "char"} and char == "\\":
                output.extend((" ", " "))
                index += 2
                continue
            elif (mode == "string" and char == '"') or (mode == "char" and char == "'"):
                mode = ""
            output.append("\n" if char == "\n" else " ")
            index += 1
            continue
        if char == "/" and next_char == "/":
            output.extend((" ", " "))
            index += 2
            mode = "line"
            continue
        if char == "/" and next_char == "*":
            output.extend((" ", " "))
            index += 2
            mode = "block"
            continue
        if char == "@" and next_char == '"':
            output.extend((" ", " "))
            index += 2
            mode = "verbatim"
            continue
        if char == '"':
            output.append(" ")
            index += 1
            mode = "string"
            continue
        if char == "'":
            output.append(" ")
            index += 1
            mode = "char"
            continue
        output.append(char)
        index += 1
    result = "".join(output)
    _MASK_CACHE[text] = result
    return result
def _top_level_type_declarations(text: str) -> list[tuple[str, str]]:
    """Return namespace and name pairs for top-level types outside literal-false preprocessor branches."""
    kept: list[str] = []
    branches: list[tuple[bool, bool]] = []
    for line in _strip_csharp_comments_and_strings(text).splitlines(keepends=True):
        directive = line.strip()
        if directive.startswith("#if"):
            condition = directive[3:].strip()
            branches.append((condition != "false", condition == "true"))
        elif directive.startswith("#elif") and branches:
            taken = branches[-1][1]
            condition = directive[5:].strip()
            branches[-1] = (not taken and condition != "false", taken or condition == "true")
        elif directive.startswith("#else") and branches:
            branches[-1] = (not branches[-1][1], True)
        elif directive.startswith("#endif") and branches:
            branches.pop()
        elif all(active for active, _ in branches):
            kept.append(line)
            continue
        kept.append("\n")
    masked = "".join(kept)
    token = re.compile(
        r"\bnamespace\s+(?P<namespace>[A-Za-z_][\w.]*)\s*(?P<terminator>[{;])"
        r"|\b(?:(?:record\s+)?(?:class|struct)|interface|record)\s+(?P<type>[A-Za-z_]\w*)"
        r"|(?P<brace>[{}])"
    )
    namespaces: list[tuple[str, int]] = []
    file_namespace = ""
    depth = 0
    declarations: list[tuple[str, str]] = []
    for match in token.finditer(masked):
        if match.group("namespace"):
            if match.group("terminator") == ";":
                file_namespace = match.group("namespace")
            else:
                namespaces.append((match.group("namespace"), depth))
                depth += 1
        elif match.group("type"):
            if depth == (namespaces[-1][1] + 1 if namespaces else 0):
                namespace = ".".join(name for name, _ in namespaces) if namespaces else file_namespace
                declarations.append((namespace, match.group("type")))
        elif match.group("brace") == "{":
            depth += 1
        else:
            depth -= 1
            if namespaces and depth == namespaces[-1][1]:
                namespaces.pop()
    return declarations
def _registry_validation_entry(root: Path, source_file: str) -> tuple[str, str] | None:
    """Resolve a validation file to its registry flag through its stem or exactly one declared-type entry."""
    registry = root / "Packages" / "dev.unity2foxglove.sdk" / "Tests" / "Runtime" / "PhaseValidationRegistry.cs"
    if not registry.is_file():
        return None
    text = registry.read_text(encoding="utf-8", errors="replace")
    entry_start = re.compile(r"^\s*(?P<kind>Ci|Local|DefaultOnly)\(\"(?P<flag>--[A-Za-z0-9-]+)\"", re.MULTILINE)
    matches = list(entry_start.finditer(text))
    blocks = [
        (match, text[match.start() : matches[index + 1].start() if index + 1 < len(matches) else len(text)])
        for index, match in enumerate(matches)
    ]
    def registered(type_name: str) -> list[tuple[str, str]]:
        """Return every registry entry that dispatches to one of a type's Validate methods."""
        entries: list[tuple[str, str]] = []
        for match, block in blocks:
            entrypoint = re.search(rf"\b{re.escape(type_name)}\.(?P<entry>Validate\w*)\b", block)
            if entrypoint:
                default = "false" if re.search(r"\bincludeInDefault\s*:\s*false\b", block) else "true"
                entries.append((match.group("flag"), f"{match.group('kind')}|{default}|{entrypoint.group('entry')}"))
        return entries
    stem_entries = registered(Path(source_file).stem)
    source_path = root / source_file
    if stem_entries or "/Tests/Runtime/" not in f"/{source_file}" or not source_path.is_file():
        return stem_entries[0] if stem_entries else None
    namespace = re.search(r"\bnamespace\s+([A-Za-z_][\w.]*)", _strip_csharp_comments_and_strings(text))
    parts = namespace.group(1).split(".") if namespace else []
    visible = {".".join(parts[:size]) for size in range(len(parts) + 1)}
    declared = {
        entry
        for type_namespace, type_name in _top_level_type_declarations(
            source_path.read_text(encoding="utf-8", errors="replace")
        )
        if type_namespace in visible
        for entry in registered(type_name)
    }
    return declared.pop() if len(declared) == 1 else None
def _csharp_comment_text(text: str) -> str:
    """Return C# line and block comment text while ignoring strings and chars."""
    cached = _COMMENT_CACHE.get(text)
    if cached is not None:
        return cached
    output: list[str] = []
    index = 0
    mode = ""
    while index < len(text):
        char = text[index]
        next_char = text[index + 1] if index + 1 < len(text) else ""
        if mode == "line":
            if char == "\n":
                output.append(char)
                mode = ""
            else:
                output.append(char)
            index += 1
            continue
        if mode == "block":
            if char == "*" and next_char == "/":
                output.extend(("*", "/"))
                index += 2
                mode = ""
            else:
                output.append(char)
                index += 1
            continue
        if mode in {"string", "verbatim", "char"}:
            if mode == "verbatim" and char == '"':
                if next_char == '"':
                    index += 2
                    continue
                mode = ""
            elif mode in {"string", "char"} and char == "\\":
                index += 2
                continue
            elif (mode == "string" and char == '"') or (mode == "char" and char == "'"):
                mode = ""
            index += 1
            continue
        if char == "/" and next_char == "/":
            output.extend(("/", "/"))
            index += 2
            mode = "line"
            continue
        if char == "/" and next_char == "*":
            output.extend(("/", "*"))
            index += 2
            mode = "block"
            continue
        if char == "@" and next_char == '"':
            index += 2
            mode = "verbatim"
            continue
        if char == '"':
            index += 1
            mode = "string"
            continue
        if char == "'":
            index += 1
            mode = "char"
            continue
        index += 1
    result = "".join(output)
    _COMMENT_CACHE[text] = result
    return result
def _target_comment_only(
    target: str, token: str, root: Path, cache: dict[str, tuple[str, str]]
) -> bool:
    """Return whether a checked literal exists only in target-file comments."""
    literals = [
        match.group(1)
        for match in re.finditer(
            r"(?:\b[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*\.)?(?:Contains|IndexOf|DoesNotContain)\s*\(\s*\"((?:\\.|[^\"\\])*)\"",
            token,
        )
    ]
    if not literals:
        return False
    files: list[str] = []
    for value in target.split(";"):
        path = _repo_file(root, value)
        if path is None:
            continue
        key = path.as_posix()
        files.append(key)
        if key not in cache:
            text = path.read_text(encoding="utf-8", errors="replace")
            cache[key] = (_csharp_comment_text(text), _strip_csharp_comments_and_strings(text))
    for literal in literals:
        if files and all(
            literal in cache[key][0] and literal not in cache[key][1] for key in files
        ):
            return True
    return False
def _xunit_coverage(target: str, root: Path, xunit_index: dict[str, list[str]]) -> str:
    """Return indexed unit-test files whose text references the target type name."""
    targets = [value for value in target.split(";") if value]
    names: set[str] = set()
    for value in targets:
        names.add(Path(value).stem)
        cache_key = (str(root.resolve()), value)
        product_names = _PRODUCT_TYPE_CACHE.get(cache_key)
        if product_names is None:
            product = _repo_file(root, value)
            product_names = (
                set(
                    re.findall(
                        r"\b(?:class|struct|record|interface|enum)\s+([A-Za-z_]\w*)",
                        product.read_text(encoding="utf-8", errors="replace"),
                    )
                )
                if product is not None
                else set()
            )
            _PRODUCT_TYPE_CACHE[cache_key] = product_names
        names.update(product_names)
        names.add(value)
    matches = sorted({match for name in names for match in xunit_index.get(name, [])})
    return ";".join(matches)

def _split_arguments(text: str) -> list[str]:
    """Split a masked C# argument or parameter list at top-level commas."""
    parts: list[str] = []
    start = 0
    depth = 0
    for index, character in enumerate(text):
        if character in "(<[{":
            depth += 1
        elif character in ")>]}" and depth:
            depth -= 1
        elif character == "," and depth == 0:
            parts.append(text[start:index].strip())
            start = index + 1
    tail = text[start:].strip()
    if tail:
        parts.append(tail)
    return parts

def _normalize_type(value: str) -> str:
    """Normalize common C# aliases for lightweight overload matching."""
    normalized = re.sub(r"\s+", "", value).replace("global::", "").rstrip("?")
    aliases = {
        "System.String": "string",
        "System.Int32": "int",
        "System.Boolean": "bool",
        "System.Object": "object",
    }
    return aliases.get(normalized, normalized)

def _parameter_types(parameter_text: str) -> tuple[str, ...]:
    """Return normalized parameter types from a method declaration."""
    result: list[str] = []
    for parameter in _split_arguments(parameter_text):
        parameter = re.sub(r"\s*=.*$", "", parameter).strip()
        parameter = re.sub(r"\[[^\]]*\]\s*", "", parameter)
        tokens = parameter.split()
        tokens = [token for token in tokens if token not in {"this", "ref", "out", "in", "params"}]
        if len(tokens) >= 2:
            result.append(_normalize_type(" ".join(tokens[:-1])))
        elif tokens:
            result.append(_normalize_type(tokens[0]))
    return tuple(result)
def _method_parameter_types(masked: str, start: int, body_start: int) -> tuple[str, ...]:
    """Extract a method's parameter types from its declaration span."""
    open_index = masked.find("(", start, body_start)
    close_index = masked.rfind(")", start, body_start)
    if open_index < 0 or close_index < open_index:
        return ()
    return _parameter_types(masked[open_index + 1 : close_index])
def _method_return_type(signature: str, name: str) -> str:
    """Extract a simple declared return type from a method signature."""
    match = re.search(
        rf"(?P<return>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)?(?:<[^>]+>)?(?:\[\])?)\s+"
        rf"{re.escape(name)}\s*\(",
        signature,
    )
    return _normalize_type(match.group("return")) if match else ""
def _constant_values(text: str, masked: str) -> dict[str, str]:
    """Collect simple boolean, integer, and string initializers for branch selectors."""
    values: dict[str, str] = {}
    for match in re.finditer(
        r"\b(?:(?:bool|int|string|var)\s+)?(?P<name>[A-Za-z_]\w*)\s*=\s*"
        r"(?P<value>true|false|-?\d+|\"(?:\\.|[^\"\\])*\")\s*;",
        masked,
    ):
        values[match.group("name")] = _normalize_csharp_expression(text[match.start("value") : match.end("value")])
    return values
