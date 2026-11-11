"""Text scanning primitives for source-text inventory."""

from __future__ import annotations

import re
from pathlib import Path

PATH_LITERAL_PATTERN = re.compile(
    r'"([^"\r\n]+(?:\.cs|\.py|\.md|\.json|\.yml|\.yaml|\.csproj|\.props|\.targets|\.asmdef|\.meta|\.txt|\.xml|\.cpp|\.h|\.dll|\.so))"'
)
PATH_LIKE_LITERAL_PATTERN = re.compile(r'"([^"\r\n]*[/\\][^"\r\n]*)"')
READER_CALL_PATTERN = re.compile(
    r"\b(?P<reader>ReadRepoText|ReadRequiredRepoText|ReadText|ReadRuntimeSource|"
    r"ReadPointCloudPublisherSources|ReadCameraPublisherSources|ReadProductPublisherText|"
    r"ReadRepoDirectoryText|Text|PhaseValidationSourceHelpers\s*\.\s*Read[A-Za-z_]\w*"
    r"(?:Sources?|SourcePath|Text)|TestSources\s*\.\s*(?:Text|Runtime|ExtractMethod|"
    r"Slice|SourceGeneratorSources)|RuntimeSource)\s*\("
)
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
    return _extract_argument_path(arguments[path_index], paths)


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
