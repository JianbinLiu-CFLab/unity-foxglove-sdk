"""Inventory source-text assertions in runtime and unit validation files."""
from __future__ import annotations
import argparse, csv
from collections import Counter
import importlib.util
import re
import subprocess, tarfile, tempfile
from pathlib import Path
PATH_LITERAL_PATTERN = re.compile(
    r'"([^"\r\n]+(?:\.cs|\.py|\.md|\.json|\.yml|\.yaml|\.csproj|\.props|\.targets|\.asmdef|\.meta|\.txt|\.xml|\.cpp|\.h|\.dll|\.so))"'
)
PATH_LIKE_LITERAL_PATTERN = re.compile(r'"([^"\r\n]*[/\\][^"\r\n]*)"')
READER_CALL_PATTERN = re.compile(
    r"\b(?P<reader>ReadRepoText|ReadRequiredRepoText|ReadText|ReadRuntimeSource|ReadPointCloudPublisherSources|ReadCameraPublisherSources|ReadProductPublisherText|ReadRepoDirectoryText|Text|"
    r"PhaseValidationSourceHelpers\s*\.\s*(?:ReadRequiredRepoText|ReadFoxRunRos2SubscriptionBindingSources|ReadFoxgloveLogSourceGeneratorSources|ReadSplitPythonSource)|TestSources\s*\.\s*(?:Text|Runtime|ExtractMethod|Slice|SourceGeneratorSources)|RuntimeSource)\s*\("
)
VARIABLE_READER_PATTERN = re.compile(
    r"(?:var|string)\s+(?P<name>[A-Za-z_]\w*)\s*=\s*(?P<reader>ReadRepoText|ReadRequiredRepoText|PhaseValidationSourceHelpers\.ReadRequiredRepoText|TestSources\.Text)\s*\(\s*(?P<argument>[^)]*?)\)",
    re.DOTALL,
)
ARRAY_PATTERN = re.compile(r"(?:var|string\[\])\s+(?P<name>[A-Za-z_]\w*)\s*=\s*new\[\]\s*\{(?P<body>[^}]*)\}", re.DOTALL)
CONSTANT_PATH_PATTERN = re.compile(r"(?:const\s+string|private\s+const\s+string)\s+(?P<name>[A-Za-z_]\w*)\s*=\s*\"(?P<path>[^\"]+)\"\s*;?")
RECEIVER_PATTERN = re.compile(
    r"(?P<receiver>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\.(?:Contains|IndexOf|DoesNotContain)\s*\("
)
ASSERTION_CALL_PATTERN = re.compile(r"\bAssert\.(?:Contains|DoesNotContain)\s*\(")
DERIVED_METHOD_PATTERN = re.compile(r"\.(?:Contains|IndexOf|DoesNotContain)\s*\(")
PHASE_LABEL_PATTERN = re.compile(r"\"(?P<label>\d+[A-Za-z]?[-A-Za-z0-9]*-[A-Za-z0-9]+:[^\"]+)\"")
def classify(target: str, line: str) -> str:
    """Classify one source-text assertion as boundary, wiring, or implementation."""
    if not target or "<unresolved>" in target:
        return "UNRESOLVED"
    if "<directory>" in target:
        return "UNKNOWN"
    if re.search(r"!\s*(?:[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*\s*\.\s*(?:Contains|IndexOf|DoesNotContain)|Assert\.Contains)\s*\(", line):
        return "A"
    if re.search(r"\bAssert\.DoesNotContain\s*\(", line):
        return "A"
    if target.startswith(".github/") or target.startswith("Scripts/release/run_ci"):
        return "B"
    if target.endswith(".md") or target.endswith("package.json") or target.endswith("/Tests/Runtime/PhaseValidationRegistry.cs"):
        return "B"
    return "C"
def _normalise_path(value: str) -> str:
    """Normalise a repository-relative path for stable reports."""
    return value.strip().strip('"').replace("\\", "/")
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
    return None
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
    return [0] + [index + 1 for index, char in enumerate(text) if char == "\n"]
def _line_for_offset(starts: list[int], offset: int) -> int:
    """Map a character offset to a zero-based line number."""
    line = 0
    for index, start in enumerate(starts):
        if start > offset:
            break
        line = index
    return line
def _call_spans(lines: list[str], pattern: re.Pattern[str]) -> list[dict[str, object]]:
    """Return balanced call spans for a regular-expression call pattern."""
    text = "\n".join(lines)
    searchable = _strip_csharp_comments_and_strings(text)
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
    text = "\n".join(lines)
    start = int(call["start"])
    end = int(call["end"])
    masked = _strip_csharp_comments_and_strings(text)
    statement_start = max(masked.rfind(";", 0, start), masked.rfind("{", 0, start), masked.rfind("}", 0, start)) + 1
    return text[statement_start:end]
def _source_targets(lines: list[str], include_variables: bool = True) -> dict[str, str]:
    """Build a local symbol-to-source-path map from reader calls and path arrays."""
    paths: dict[str, str] = {}
    text = "\n".join(lines)
    for match in ARRAY_PATTERN.finditer(text):
        values = [_normalise_path(value) for value in PATH_LITERAL_PATTERN.findall(match.group("body"))]
        if values:
            paths[match.group("name")] = ";".join(values)
    for match in CONSTANT_PATH_PATTERN.finditer(text):
        value = _normalise_path(match.group("path"))
        if "/" in value or "\\" in value:
            paths[match.group("name")] = value
    if not include_variables:
        for _ in range(3):
            changed = False
            for match in re.finditer(r"const\s+string\s+(?P<name>[A-Za-z_]\w*)\s*=\s*(?P<expression>[^;]+)", text):
                expression = match.group("expression")
                targets: list[str] = []
                for name, base_value in paths.items():
                    if not re.search(rf"\b{re.escape(name)}\b", expression):
                        continue
                    suffix_match = re.search(rf"\b{re.escape(name)}\s*\+\s*\"([^\"]*)\"", expression)
                    if suffix_match:
                        suffix = suffix_match.group(1).replace("\\", "/")
                        targets.extend(item.rstrip("/") + "/" + suffix.lstrip("/") for item in base_value.split(";"))
                    else:
                        targets.extend(base_value.split(";"))
                if targets and paths.get(match.group("name")) != ";".join(dict.fromkeys(targets)):
                    paths[match.group("name")] = ";".join(dict.fromkeys(targets))
                    changed = True
            if not changed:
                break
        return paths
    for _ in range(3):
        changed = False
        for match in VARIABLE_READER_PATTERN.finditer(text):
            target = _extract_argument_path(match.group("argument"), paths)
            target = target or "<unresolved>"
            if paths.get(match.group("name")) != target:
                paths[match.group("name")] = target
                changed = True
        for match in re.finditer(r"(?:var|string)\s+(?P<name>[A-Za-z_]\w*)\s*=\s*(?P<expression>[^;]+)", text):
            expression = match.group("expression")
            expression_without_literals = re.sub(r'@?"(?:""|\\.|[^"\\])*"', '""', expression)
            referenced = re.findall(r"\b[A-Za-z_]\w*\b", expression_without_literals)
            targets = []
            for name in referenced:
                if name not in paths:
                    continue
                suffix_match = re.search(rf"\b{re.escape(name)}\s*\+\s*\"([^\"]*)\"", expression)
                if suffix_match:
                    suffix = suffix_match.group(1).replace("\\", "/")
                    targets.extend(
                        value.rstrip("/") + "/" + suffix.lstrip("/")
                        for value in paths[name].split(";")
                    )
                else:
                    targets.extend(paths[name].split(";"))
            target = ";".join(dict.fromkeys(";".join(targets).split(";"))) if targets else None
            if target and paths.get(match.group("name")) != target:
                paths[match.group("name")] = target
                changed = True
        if not changed:
            break
    return paths
def _brace_paths(lines: list[str]) -> list[tuple[int, ...]]:
    """Return a lexical brace scope for each source line."""
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
    return scopes
def _scoped_source_symbols(lines: list[str], shared_paths: dict[str, str]) -> list[tuple[int, tuple[int, ...], str, str]]:
    """Collect reader symbols with method and block scope boundaries."""
    scopes = _brace_paths(lines)
    symbols: list[tuple[int, tuple[int, ...], str, str]] = [(-1, (), name, target) for name, target in shared_paths.items()]
    text = "\n".join(lines)
    line_starts = _line_starts(text)
    local_paths = dict(shared_paths)
    definitions: list[tuple[int, tuple[int, ...], str, str]] = []
    for call in _call_spans(lines, READER_CALL_PATTERN):
        start = int(call["start"])
        line = int(call["start_line"])
        prefix = text[max(0, start - 256) : start]
        assignment = re.search(r"(?:var|string)\s+(?P<name>[A-Za-z_]\w*)\s*=\s*$", prefix)
        if not assignment:
            continue
        argument = _split_first_argument(str(call["argument"]))
        target = _extract_argument_path(argument, local_paths) or "<unresolved>"
        definition = (line, scopes[line], assignment.group("name"), target)
        definitions.append(definition)
        local_paths[assignment.group("name")] = target
    for match in re.finditer(
        r"foreach\s*\(\s*var\s+(?P<name>[A-Za-z_]\w*)\s+in\s+(?P<expression>[^)]*)\)",
        text,
        re.DOTALL,
    ):
        line = _line_for_offset(line_starts, match.start())
        scope = scopes[line]
        targets: list[str] = []
        for reference in re.findall(r"\b[A-Za-z_]\w*\b", _strip_csharp_comments_and_strings(match.group("expression"))):
            target = next(
                (
                    value
                    for definition_line, definition_scope, name, value in reversed(definitions)
                    if name == reference
                    and definition_line < line
                    and len(definition_scope) <= len(scope)
                    and tuple(scope[:len(definition_scope)]) == definition_scope
                ),
                None,
            )
            if target and target != "<unresolved>":
                targets.extend(target.split(";"))
        if targets:
            definitions.append((line, scope, match.group("name"), ";".join(dict.fromkeys(targets))))
    assignment_pattern = re.compile(
        r"(?:var|string)\s+(?P<name>[A-Za-z_]\w*)\s*=\s*(?P<expression>[^;]+);",
        re.DOTALL,
    )
    assignment_expressions: dict[tuple[int, str], str] = {}
    direct_keys: set[tuple[int, str]] = set()
    for match in assignment_pattern.finditer(text):
        line = _line_for_offset(line_starts, match.start())
        assignment_expressions[(line, match.group("name"))] = match.group("expression")
        if any(existing_line == line and existing_name == match.group("name") for existing_line, _, existing_name, _ in definitions):
            direct_keys.add((line, match.group("name")))
    for definition in definitions:
        direct_keys.add((definition[0], definition[2]))
    assignments = sorted(assignment_expressions.items(), key=lambda item: item[0][0])
    indexed: dict[str, list[tuple[int, tuple[int, ...], str]]] = {}
    for line, scope, name, target in definitions:
        indexed.setdefault(name, []).append((line, scope, target))
    def lookup(name: str, line: int, scope: tuple[int, ...]) -> str | None:
        """Resolve a symbol to the nearest definition in the active scope."""
        for definition_line, definition_scope, target in reversed(indexed.get(name, [])):
            if definition_line < line and len(definition_scope) <= len(scope) and tuple(scope[:len(definition_scope)]) == definition_scope:
                return target
        return None
    for (line, name), expression in assignments:
        if (line, name) in direct_keys:
            continue
        scope = scopes[line]
        references = re.findall(r"\b[A-Za-z_]\w*\b", expression)
        targets: list[str] = []
        for reference in references:
            reference_target = lookup(reference, line, scope)
            if reference_target:
                targets.extend(reference_target.split(";"))
        unresolved_reader = False
        for reader in _call_spans(expression.splitlines(), READER_CALL_PATTERN):
            reader_target = _extract_argument_path(_split_first_argument(str(reader["argument"])), local_paths)
            if reader_target:
                targets.extend(reader_target.split(";"))
            else:
                unresolved_reader = True
        if targets or unresolved_reader:
            target = ";".join(dict.fromkeys(targets)) or "<unresolved>"
            definitions.append((line, scope, name, target))
            indexed.setdefault(name, []).append((line, scope, target))
    symbols.extend(definitions)
    return symbols
def _lookup_symbol(symbols: list[tuple[int, tuple[int, ...], str, str]], name: str, line: int, scope: tuple[int, ...]) -> str | None:
    """Resolve the nearest symbol visible from a source line."""
    candidates = [
        (definition_line, definition_scope, target)
        for definition_line, definition_scope, symbol_name, target in symbols
        if symbol_name == name
        and definition_line <= line
        and len(definition_scope) <= len(scope)
        and tuple(scope[:len(definition_scope)]) == definition_scope
    ]
    if not candidates:
        return None
    _, _, target = max(candidates, key=lambda value: (len(value[1]), value[0]))
    return target
def _resolve_assertion_target(
    call: dict[str, object],
    paths: dict[str, str],
    symbols: list[tuple[int, tuple[int, ...], str, str]],
    scopes: list[tuple[int, ...]],
) -> str | None:
    """Resolve a source-text variable used as an assertion argument."""
    line = int(call["start_line"])
    scope = scopes[line]
    call_text = str(call["text"])
    code = _strip_csharp_comments_and_strings(call_text)
    names = {name for _, _, name, _ in symbols if name != "__last_reader__"}
    names.update(name for name in paths if name != "__last_reader__")
    targets: list[str] = []
    for name in names:
        if not re.search(rf"\b{re.escape(name)}\b", code):
            continue
        target = _lookup_symbol(symbols, name, line, scope)
        if target:
            targets.extend(target.split(";"))
    if targets:
        return ";".join(dict.fromkeys(targets))
    reader_targets: list[str] = []
    for reader in _call_spans(call_text.splitlines(), READER_CALL_PATTERN):
        target = _extract_argument_path(_split_first_argument(str(reader["argument"])), paths)
        if target:
            reader_targets.extend(target.split(";"))
    return ";".join(dict.fromkeys(reader_targets)) or None
def _resolve_derived_target(
    lines: list[str],
    call: dict[str, object],
    paths: dict[str, str],
    symbols: list[tuple[int, tuple[int, ...], str, str]],
    scopes: list[tuple[int, ...]],
) -> str | None:
    """Resolve source origins for receiver expressions that contain method calls."""
    line = int(call["start_line"])
    text = "\n".join(lines)
    start = int(call["start"])
    masked = _strip_csharp_comments_and_strings(text)
    statement_start = max(masked.rfind(";", 0, start), masked.rfind("{", 0, start), masked.rfind("}", 0, start)) + 1
    expression = text[statement_start:start]
    code = _strip_csharp_comments_and_strings(expression)
    targets: list[str] = []
    unresolved = False
    names = {name for _, _, name, _ in symbols if name != "__last_reader__"}
    for name in names:
        if not re.search(rf"\b{re.escape(name)}\b", code):
            continue
        target = _lookup_symbol(symbols, name, line, scopes[line])
        if target:
            targets.extend(target.split(";"))
        else:
            unresolved = True
    for reader in _call_spans(expression.splitlines(), READER_CALL_PATTERN):
        target = _extract_argument_path(_split_first_argument(str(reader["argument"])), paths)
        if target:
            targets.extend(target.split(";"))
        else:
            unresolved = True
    if targets:
        return ";".join(dict.fromkeys(targets))
    return "<unresolved>" if unresolved else None
def _shared_scope_paths(files: list[Path]) -> dict[Path, dict[str, str]]:
    """Build shared const and reader symbols for partial validation classes."""
    groups: dict[str, list[Path]] = {}
    class_pattern = re.compile(r"\b(?:partial\s+)?(?:class|struct)\s+([A-Za-z_]\w*)")
    for path in files:
        text = path.read_text(encoding="utf-8", errors="replace")
        names = class_pattern.findall(text)
        key = names[0] if names else path.stem
        groups.setdefault(key, []).append(path)
    result: dict[Path, dict[str, str]] = {}
    for group in groups.values():
        lines = []
        for path in group:
            lines.extend(path.read_text(encoding="utf-8", errors="replace").splitlines())
        shared = _source_targets(lines, include_variables=False)
        local_symbols = {
            path: _scoped_source_symbols(path.read_text(encoding="utf-8", errors="replace").splitlines(), shared)
            for path in group
        }
        caller_texts = {path: path.read_text(encoding="utf-8", errors="replace") for path in group}
        caller_lines = {path: text.splitlines() for path, text in caller_texts.items()}
        caller_scopes = {path: _brace_paths(lines) for path, lines in caller_lines.items()}
        parameter_targets: dict[str, set[str]] = {}
        declaration_pattern = re.compile(r"\b(?P<name>[A-Za-z_]\w*)\s*\((?P<parameters>[^)]*)\)\s*\{")
        for callee_path in group:
            callee_text = caller_texts[callee_path]
            for declaration in declaration_pattern.finditer(callee_text):
                parameters = [
                    match.group(1)
                    for match in re.finditer(r"\b[A-Za-z_]\w*\s+([A-Za-z_]\w*)\s*(?:,|$)", declaration.group("parameters"))
                ]
                if not parameters:
                    continue
                callee_name = declaration.group("name")
                for caller_path in group:
                    caller_text = caller_texts[caller_path]
                    for call in re.finditer(rf"\b{re.escape(callee_name)}\s*\((?P<arguments>[^)]*)\)", caller_text):
                        arguments = [item.strip() for item in call.group("arguments").split(",")]
                        line = caller_text.count("\n", 0, call.start())
                        scope = caller_scopes[caller_path][line]
                        for parameter, argument in zip(parameters, arguments):
                            target = None
                            if re.fullmatch(r"[A-Za-z_]\w*", argument):
                                target = _lookup_symbol(local_symbols[caller_path], argument, line, scope)
                            else:
                                reader = READER_CALL_PATTERN.search(argument)
                                if reader:
                                    first = _split_first_argument(argument[argument.find("(", reader.start()) + 1 :])
                                    target = _extract_argument_path(first, shared)
                                    target = target or _lookup_symbol(local_symbols[caller_path], first, line, scope)
                            if target and target != "<unresolved>":
                                parameter_targets.setdefault(parameter, set()).update(target.split(";"))
        for name, targets in parameter_targets.items():
            if name not in shared:
                shared[name] = next(iter(targets)) if len(targets) == 1 else "<unresolved>"
        for path in group:
            result[path] = shared
    return result
def _build_xunit_index(root: Path) -> dict[str, list[str]]:
    """Index unit-test files by the type names they reference."""
    index: dict[str, list[str]] = {}
    unit_files = sorted(root.glob("Packages/*/Tests/Unit/**/*.cs"))
    texts = [(path, _strip_csharp_comments_and_strings(path.read_text(encoding="utf-8", errors="replace"))) for path in unit_files]
    product_types: set[str] = set()
    declaration = re.compile(r"\b(?:class|struct|record|interface|enum)\s+([A-Za-z_]\w*)")
    for path in root.glob("Packages/**/*.cs"):
        if "\\Tests\\" in str(path) or "/Tests/" in path.as_posix():
            continue
        text = path.read_text(encoding="utf-8", errors="replace")
        product_types.update(declaration.findall(text))
    for path, text in texts:
        referenced = set(re.findall(r"\b[A-Za-z_]\w*\b", text)) & product_types
        for stem in referenced:
            index.setdefault(stem, []).append(path.relative_to(root).as_posix())
    return index
def _strip_csharp_comments_and_strings(text: str) -> str:
    """Remove C# comments and string literals before symbol matching."""
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
                output.append(" ")
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
        if char == '@' and next_char == '"':
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
    return "".join(output)
def _csharp_comment_text(text: str) -> str:
    """Return C# line and block comment text while ignoring strings and chars."""
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
    return "".join(output)
def _target_comment_only(target: str, token: str, root: Path, cache: dict[str, tuple[str, str]]) -> bool:
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
    for value in target.split(";"):
        path = root / value
        if not path.is_file():
            continue
        key = path.as_posix()
        if key not in cache:
            text = path.read_text(encoding="utf-8", errors="replace")
            cache[key] = (_csharp_comment_text(text), _strip_csharp_comments_and_strings(text))
        comments, code = cache[key]
        for literal in literals:
            if literal in comments and literal not in code:
                return True
    return False
def _xunit_coverage(target: str, root: Path, xunit_index: dict[str, list[str]]) -> str:
    """Return indexed unit-test files whose text references the target type name."""
    targets = [value for value in target.split(";") if value]
    names: set[str] = set()
    for value in targets:
        names.add(Path(value).stem)
        product = root / value
        if product.is_file():
            text = product.read_text(encoding="utf-8", errors="replace")
            names.update(re.findall(r"\b(?:class|struct|record|interface|enum)\s+([A-Za-z_]\w*)", text))
        names.add(value)
    matches = sorted({match for name in names for match in xunit_index.get(name, [])})
    return ";".join(matches)
def scan_file(path: Path, root: Path, xunit_index: dict[str, list[str]], shared_paths: dict[str, str] | None = None, comment_cache: dict[str, tuple[str, str]] | None = None) -> list[dict[str, object]]:
    """Collect source-text assertions from one validation file."""
    lines = path.read_text(encoding="utf-8").splitlines()
    paths = dict(shared_paths or {})
    local_symbols = _scoped_source_symbols(lines, paths)
    scopes = _brace_paths(lines)
    comment_cache = comment_cache if comment_cache is not None else {}
    for _, _, name, target in local_symbols:
        paths[name] = target
    assertion_calls = _call_spans(lines, ASSERTION_CALL_PATTERN)
    receiver_calls = _call_spans(lines, RECEIVER_PATTERN)
    derived_calls = _call_spans(lines, DERIVED_METHOD_PATTERN)
    receiver_ends = {int(call["end"]) for call in receiver_calls}
    candidates: list[tuple[int, dict[str, object], str]] = []
    for call in receiver_calls:
        if call.get("receiver") == "Assert":
            continue
        line = int(call["start_line"])
        receiver_name = str(call["receiver"]).split(".", 1)[0]
        target = _lookup_symbol(local_symbols, receiver_name, line, scopes[line])
        if target is None and receiver_name in {name for _, _, name, _ in local_symbols}:
            target = "<unresolved>"
        if target is not None:
            candidates.append((int(call["start"]), call, target))
    for call in derived_calls:
        if int(call["end"]) in receiver_ends:
            continue
        target = _resolve_derived_target(lines, call, paths, local_symbols, scopes)
        if target is not None:
            candidates.append((int(call["start"]), call, target))
    for call in assertion_calls:
        line = int(call["start_line"])
        target = _resolve_assertion_target(call, paths, local_symbols, scopes)
        if target is not None:
            candidates.append((int(call["start"]), call, target))
    rows: list[dict[str, object]] = []
    for _, call, target in sorted(candidates, key=lambda item: item[0]):
        index = int(call["start_line"])
        token_text = str(call["text"]).strip()
        classification_text = _call_context(lines, call)
        target_parts = []
        for part in target.split(";"):
            candidate = (root / part).resolve() if part and part != "<unresolved>" else None
            if candidate is not None and candidate.is_dir():
                target_parts.append("<directory>")
            elif part:
                target_parts.append(part)
        target = ";".join(dict.fromkeys(target_parts)) or "<unresolved>"
        label_match = PHASE_LABEL_PATTERN.search("".join(lines[max(0, index - 5) : index + 1]))
        rows.append(
            {
                "file": path.relative_to(root).as_posix(),
                "line": index + 1,
                "label": label_match.group("label") if label_match else "",
                "validation_flag": _validation_flag(root, path.relative_to(root).as_posix()),
                "class": classify(target, classification_text),
                "target": target,
                "xunit_coverage": _xunit_coverage(target, root, xunit_index),
                "token": token_text,
                "comment_only": _target_comment_only(target, token_text, root, comment_cache),
            }
        )
    return rows
def inventory(root: Path) -> list[dict[str, object]]:
    """Scan runtime and unit C# validation files under every package."""
    rows: list[dict[str, object]] = []
    xunit_index = _build_xunit_index(root)
    files = sorted({path for pattern in ("Packages/*/Tests/Runtime/**/*.cs", "Packages/*/Tests/Unit/**/*.cs") for path in root.glob(pattern)})
    scopes = _shared_scope_paths(files)
    comment_cache: dict[str, tuple[str, str]] = {}
    for path in files:
        rows.extend(scan_file(path, root, xunit_index, scopes.get(path), comment_cache))
    return rows
def _inventory_at_revision(root: Path, revision: str, use_current_parser: bool = False) -> list[dict[str, object]]:
    """Materialize a Git revision under build/ and scan it with the current inventory rules."""
    build_root = root / "build" / "review"
    build_root.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="source-text-inventory-base-", dir=build_root) as directory:
        process = subprocess.Popen(
            ["git", "archive", revision],
            cwd=root,
            stdout=subprocess.PIPE,
        )
        assert process.stdout is not None
        with tarfile.open(fileobj=process.stdout, mode="r|") as archive:
            archive.extractall(directory)
        if process.wait() != 0:
            raise RuntimeError(f"git archive failed for {revision}")
        script_path = Path(directory) / "Scripts" / "validation" / "source_text_inventory.py"
        spec = importlib.util.spec_from_file_location("source_text_inventory_base", script_path)
        if spec is None or spec.loader is None:
            raise RuntimeError(f"base inventory script is missing at {script_path}")
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        return inventory(Path(directory)) if use_current_parser else module.inventory(Path(directory))
def _token_signatures(token: str) -> set[tuple[str, str]]:
    """Return method/literal anchors that survive line and parser changes."""
    signatures: set[tuple[str, str]] = set()
    for match in re.finditer(r"\.(Contains|IndexOf|DoesNotContain)\s*\(", token):
        literal = re.match(r"\s*\"((?:\\.|[^\"\\])*)\"", token[match.end() :])
        signatures.add((match.group(1), literal.group(1) if literal else ""))
    nonempty = {item for item in signatures if item[1]}
    return nonempty or signatures or {("", re.sub(r"\s+", " ", token).strip())}
def _row_prefix(row: dict[str, object]) -> tuple[str, str]:
    """Return the file and validation flag portion of a row identity."""
    return str(row["file"]), str(row["validation_flag"])
def _signatures_overlap(base: set[tuple[str, str]], head: set[tuple[str, str]]) -> bool:
    """Match multiline base tokens whose literal is on a continuation line."""
    return any(
        base_method == head_method and (not base_literal or not head_literal or base_literal == head_literal)
        for base_method, base_literal in base
        for head_method, head_literal in head
    )
def _targets_overlap(base: dict[str, object], head: dict[str, object]) -> bool:
    """Match legacy rows whose token shape changed but target stayed stable."""
    base_targets = {value for value in str(base["target"]).split(";") if value and value != "<unresolved>"}
    head_targets = {value for value in str(head["target"]).split(";") if value and value != "<unresolved>"}
    return bool(base_targets and base_targets & head_targets)
def _match_base_rows(
    base_rows: list[dict[str, object]], head_rows: list[dict[str, object]]
) -> tuple[list[tuple[dict[str, object], dict[str, object]]], list[dict[str, object]]]:
    """Match base rows to head rows without relying on physical line numbers."""
    available = set(range(len(head_rows)))
    matches: list[tuple[dict[str, object], dict[str, object]]] = []
    pending = list(base_rows)
    for mode in range(3):
        next_pending: list[dict[str, object]] = []
        for base in pending:
            base_signatures = _token_signatures(str(base["token"]))
            base_token = re.sub(r"\s+", " ", str(base["token"]).strip())
            if mode == 0:
                candidate = next((index for index in sorted(available) if str(base["file"]) == str(head_rows[index]["file"]) and base_token == re.sub(r"\s+", " ", str(head_rows[index]["token"]).strip())), None)
            elif mode == 1:
                candidate = next((index for index in sorted(available) if _row_prefix(base) == _row_prefix(head_rows[index]) and _signatures_overlap(base_signatures, _token_signatures(str(head_rows[index]["token"])))), None)
            else:
                candidate = next((index for index in sorted(available) if _row_prefix(base) == _row_prefix(head_rows[index]) and _targets_overlap(base, head_rows[index])), None)
            if candidate is None:
                next_pending.append(base)
            else:
                available.remove(candidate)
                matches.append((base, head_rows[candidate]))
        pending = next_pending
    missing = pending
    base_counts = Counter(_row_prefix(row) for row in base_rows)
    head_counts = Counter(_row_prefix(row) for row in head_rows)
    return matches, [row for row in missing if str(row["target"]) != "<unresolved>" and head_counts[_row_prefix(row)] < base_counts[_row_prefix(row)] and not re.search(r"\b(?:warning|logLines)\b", str(row["token"]))]
def _verify_base_inventory(root: Path, rows: list[dict[str, object]], revision: str) -> int:
    """Reject silent row loss or new unresolved targets relative to a trusted base."""
    base_rows = _inventory_at_revision(root, revision)
    base_status_rows = _inventory_at_revision(root, revision, use_current_parser=True)
    _, missing = _match_base_rows(base_rows, rows)
    status_matches, _ = _match_base_rows(base_status_rows, rows)
    metadata_changed = [_row_prefix(base) for base, head in status_matches if tuple(str(base[field]) for field in ("target", "class", "comment_only", "xunit_coverage")) != tuple(str(head[field]) for field in ("target", "class", "comment_only", "xunit_coverage"))]
    base_unresolved = sum(row["class"] == "UNRESOLVED" for row in base_status_rows)
    head_unresolved = sum(row["class"] == "UNRESOLVED" for row in rows)
    base_unknown = sum(row["class"] == "UNKNOWN" for row in base_status_rows)
    head_unknown = sum(row["class"] == "UNKNOWN" for row in rows)
    print(
        f"SOURCE_TEXT_INVENTORY_BASE_OK base_rows={len(base_rows)} head_rows={len(rows)} "
        f"missing={len(missing)} base_unresolved={base_unresolved} head_unresolved={head_unresolved} "
        f"base_unknown={base_unknown} head_unknown={head_unknown} metadata_changed={len(metadata_changed)}"
    )
    if missing or metadata_changed or head_unresolved > base_unresolved or head_unknown > base_unknown:
        if missing:
            print(
                "SOURCE_TEXT_INVENTORY_BASE_MISSING sample="
                f"{[(row['file'], row['line'], row['label']) for row in missing[:5]]}"
            )
        if metadata_changed:
            print(f"SOURCE_TEXT_INVENTORY_METADATA_CHANGED sample={metadata_changed[:5]}")
        return 1
    return 0
def _validation_flag(root: Path, source_file: str) -> str:
    """Resolve a runtime validation file to its PhaseValidationRegistry flag."""
    stem = Path(source_file).stem
    registry = root / "Packages" / "dev.unity2foxglove.sdk" / "Tests" / "Runtime" / "PhaseValidationRegistry.cs"
    if not registry.is_file():
        return ""
    text = registry.read_text(encoding="utf-8", errors="replace")
    entry_start = re.compile(r"^\s*(?:Ci|Local|DefaultOnly)\(\"(?P<flag>--[A-Za-z0-9-]+)\"", re.MULTILINE)
    matches = list(entry_start.finditer(text))
    for index, match in enumerate(matches):
        end = matches[index + 1].start() if index + 1 < len(matches) else len(text)
        if re.search(rf"\b{re.escape(stem)}\.Validate\w*\b", text[match.start() : end]):
            return match.group("flag")
    return ""
def write_report(rows: list[dict[str, object]], output: Path) -> None:
    """Write CSV and Markdown summary files for the inventory."""
    output.mkdir(parents=True, exist_ok=True)
    csv_path = output / "source_text_inventory.csv"
    with csv_path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=("file", "line", "label", "validation_flag", "class", "target", "xunit_coverage", "token", "comment_only"))
        writer.writeheader()
        writer.writerows(rows)
    counts = {label: sum(row["class"] == label for row in rows) for label in ("A", "B", "C", "UNKNOWN", "UNRESOLVED")}
    comments = sum(bool(row["comment_only"]) for row in rows)
    by_flag: dict[str, dict[str, int]] = {}
    for row in rows:
        flag = str(row["validation_flag"] or "<unregistered>")
        summary = by_flag.setdefault(flag, {"total": 0, "class_c": 0, "comment_only": 0, "covered": 0})
        summary["total"] += 1
        summary["class_c"] += int(row["class"] == "C")
        summary["comment_only"] += int(bool(row["comment_only"]))
        summary["covered"] += int(bool(row["xunit_coverage"]))
    ranking = "\n## Validation ranking\n\n| Flag | Tokens | Class C | xUnit-covered targets | Comment-only |\n|---|---:|---:|---:|---:|\n"
    for flag, summary in sorted(by_flag.items(), key=lambda item: (-item[1]["class_c"], item[0])):
        ranking += f"| `{flag}` | {summary['total']} | {summary['class_c']} | {summary['covered']} | {summary['comment_only']} |\n"
    file_targets = sum(
        bool(row["target"])
        and "<directory>" not in str(row["target"])
        and "<unresolved>" not in str(row["target"])
        for row in rows
    )
    directory_targets = sum("<directory>" in str(row["target"]) for row in rows)
    unresolved_targets = sum("<unresolved>" in str(row["target"]) for row in rows)
    (output / "SUMMARY.md").write_text(
        "# Source-text assertion inventory\n\n"
        f"- Total tokens: {len(rows)}\n"
        f"- Class A boundary: {counts['A']}\n"
        f"- Class B wiring: {counts['B']}\n"
        f"- Class C implementation: {counts['C']}\n"
        f"- Unknown targets: {counts['UNKNOWN']}\n"
        f"- Unresolved targets: {counts['UNRESOLVED']}\n"
        f"- Comment-only tokens: {comments}\n"
        f"- File targets: {file_targets} / {len(rows)}\n"
        f"- Directory targets: {directory_targets}\n"
        + ranking,
        encoding="utf-8",
    )
def main() -> int:
    """Run the source-text inventory command."""
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path.cwd())
    parser.add_argument("--output", type=Path, default=Path("build/review/source-text-inventory"))
    parser.add_argument("--base-ref")
    args = parser.parse_args()
    root = args.root.resolve()
    rows = inventory(root)
    write_report(rows, args.output)
    print(
        f"SOURCE_TEXT_INVENTORY_OK rows={len(rows)} "
        f"unknown={sum(row['class'] == 'UNKNOWN' for row in rows)} "
        f"unresolved={sum(row['class'] == 'UNRESOLVED' for row in rows)}"
    )
    if args.base_ref:
        return _verify_base_inventory(root, rows, args.base_ref)
    return 0
if __name__ == "__main__":
    raise SystemExit(main())
