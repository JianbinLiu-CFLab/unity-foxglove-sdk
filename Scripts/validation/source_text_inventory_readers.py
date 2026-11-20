"""Discover source-text reader helpers from what their bodies read instead of their names."""

from __future__ import annotations

import re
from pathlib import Path

try:
    from . import source_text_inventory_flow as _flow_module
    from . import source_text_inventory_text as _text_module
except ImportError:
    import source_text_inventory_flow as _flow_module
    import source_text_inventory_text as _text_module

FILE_READ_MEMBERS = r"(?:ReadAllText|ReadAllLines|ReadLines|OpenText|OpenRead)(?:Async)?"
FILE_TYPE_NAMES = frozenset({"File", "IO.File", "System.IO.File"})
GENERIC_ARGUMENTS = r"<[^<>;{}()=]*(?:<[^<>;{}()=]*>[^<>;{}()=]*)*>"
GENERIC_DECLARATION_PATTERN = re.compile(
    rf"(?<![\w.])(?P<name>[A-Za-z_]\w*)\s*{GENERIC_ARGUMENTS}\s*\([^;{{}}]*\)\s*(?:where\s+[^{{;=]*)?(?P<open>\{{|=>)"
)
_PRIMITIVE_CACHE: dict[tuple[str, ...], re.Pattern[str]] = {}
TEXT_RETURN_TYPES = frozenset(
    {
        "string",
        "string[]",
        "IEnumerable<string>",
        "IReadOnlyList<string>",
        "IReadOnlyCollection<string>",
        "IList<string>",
        "ICollection<string>",
        "List<string>",
    }
)


def _active_lines(masked: str) -> list[bool]:
    """Return whether each masked line lies outside literal-false preprocessor branches."""
    active: list[bool] = []
    branches: list[tuple[bool, bool]] = []
    for line in masked.splitlines() or [""]:
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
        active.append(all(state for state, _ in branches))
    return active


def _normalize_return_type(value: str) -> str:
    """Normalize C# aliases, global qualifiers, collection namespaces and nullable markers."""
    value = re.sub(r"\s+", "", value).replace("global::", "").replace("?", "")
    value = re.sub(r"\bSystem\.Collections\.Generic\.", "", value)
    return re.sub(r"\b(?:System\.)?String\b", "string", value)


def _return_type(masked: str, method: object) -> str:
    """Return the normalized return type written immediately before a method name."""
    prefix = masked[max(0, method.start - 200) : method.body_start]
    matches = list(
        re.finditer(
            r"(?P<type>(?:global\s*::\s*)?[A-Za-z_][\w.]*(?:\s*<[^<>;{}()=]*>)?(?:\s*\?)?(?:\s*\[\s*\](?:\s*\?)?)?)\s+"
            + re.escape(method.name)
            + rf"\s*(?:{GENERIC_ARGUMENTS})?\s*\(",
            prefix,
        )
    )
    return _normalize_return_type(matches[-1].group("type")) if matches else ""


def _generic_method_infos(masked: str) -> list[object]:
    """Discover generic method declarations that the shared method parser does not see."""
    methods: list[object] = []
    for match in GENERIC_DECLARATION_PATTERN.finditer(masked):
        open_index = match.end() - len(match.group("open"))
        if match.group("open") == "{":
            body_end = _text_module._balanced_brace_end(masked, open_index)
        else:
            body_end = masked.find(";", open_index)
        if body_end is None or body_end < 0:
            continue
        methods.append(_flow_module.MethodInfo(match.group("name"), match.start("name"), open_index, body_end, ""))
    return methods


def _aliases(imports: list[tuple[str, str, str]]) -> tuple[tuple[str, ...], tuple[tuple[str, str], ...]]:
    """Return File aliases and other type aliases declared by using directives."""
    targets = [(alias, name.removeprefix("global::")) for static, alias, name in imports if alias and not static]
    return (
        tuple(sorted(alias for alias, target in targets if target in FILE_TYPE_NAMES)),
        tuple(sorted((alias, target) for alias, target in targets if target not in FILE_TYPE_NAMES)),
    )


def _primitive_pattern(file_aliases: tuple[str, ...]) -> re.Pattern[str]:
    """Match file-read primitives, including File aliases and FileInfo or stream readers."""
    if file_aliases not in _PRIMITIVE_CACHE:
        owners = "|".join(["File", *(re.escape(alias) for alias in file_aliases)])
        _PRIMITIVE_CACHE[file_aliases] = re.compile(
            rf"(?<![\w])(?:{owners})\s*\.\s*{FILE_READ_MEMBERS}\s*\("
            r"|\.\s*OpenText\s*\("
            r"|\bnew\s+(?:[A-Za-z_]\w*\s*\.\s*)*(?:StreamReader|FileStream)\s*\("
        )
    return _PRIMITIVE_CACHE[file_aliases]


def _calls_seed(body: str) -> bool:
    """Return whether a body calls a seed reader other than through an instance member access."""
    for match in _text_module.READER_CALL_PATTERN.finditer(body):
        prefix = body[: match.start()].rstrip()
        if "." in match.group("reader") or not prefix.endswith("."):
            return True
        if re.search(r"(?<![\w.])this\s*$", prefix[:-1]):
            return True
    return False


def _dotted(identity: str) -> str:
    """Return a regex for a dotted identity that tolerates whitespace around dots."""
    return r"\s*\.\s*".join(re.escape(part) for part in identity.split("."))


def _call_forms(
    readers: set[tuple[tuple[tuple[str, ...], str], str]],
    short_visible: set[tuple[tuple[tuple[str, ...], str], str]] | None,
    type_aliases: tuple[tuple[str, str], ...],
) -> list[str]:
    """Return fully qualified, visible short, and alias call forms for discovered readers."""
    forms: list[str] = []
    for reader in readers:
        (_, owner), name = reader
        method = re.escape(name)
        simple = owner.rsplit(".", 1)[-1]
        prefix = r"(?:global\s*::\s*)?" if "." in owner else r"global\s*::\s*"
        forms.append(rf"(?<![\w.]){prefix}{_dotted(owner)}\s*\.\s*{method}")
        if short_visible is None or reader in short_visible:
            forms.append(rf"(?<![\w.]){re.escape(simple)}\s*\.\s*{method}")
        forms.extend(
            rf"(?<![\w.]){re.escape(alias)}\s*\.\s*{method}"
            for alias, target in type_aliases
            if target == owner or target == simple or owner.endswith("." + target)
        )
    return sorted(set(forms), key=len, reverse=True)


def _bare_form(names: set[str]) -> str:
    """Return a regex alternative for unqualified or this-qualified calls to named readers."""
    alternatives = "|".join(re.escape(name) for name in sorted(names, key=len, reverse=True))
    return rf"(?<![\w.])(?:this\s*\.\s*)?(?:{alternatives})" if names else ""


def _parent(identity: str) -> str:
    """Return the namespace or enclosing type that declares one type identity."""
    return identity.rsplit(".", 1)[0] if "." in identity else ""


def _visible_qualified_readers(
    readers: set[tuple[tuple[tuple[str, ...], str], str]],
    package: tuple[str, ...],
    keys: set[tuple[tuple[str, ...], str]],
    imports: list[tuple[str, str, str]],
    declarations: dict[tuple[tuple[str, ...], str], set[str]],
) -> set[tuple[tuple[tuple[str, ...], str], str]]:
    """Keep readers whose type is visible from one file, preferring same-package declarations of that type name."""
    scopes = {""}
    for _, identity in keys:
        parts = identity.split(".")
        scopes.update(".".join(parts[:size]) for size in range(1, len(parts) + 1))
    scopes.update(name.removeprefix("global::") for static, alias, name in imports if not static and not alias)
    visible = {(key, name) for key, name in readers if _parent(key[1]) in scopes}
    return {
        ((reader_package, owner), name)
        for (reader_package, owner), name in visible
        if reader_package == package
        or not any(_parent(identity) in scopes for identity in declarations.get((package, owner.rsplit(".", 1)[-1]), ()))
    }


def _visible_names(
    readers: set[tuple[tuple[tuple[str, ...], str], str]], key: tuple[tuple[str, ...], str]
) -> set[str]:
    """Return reader names callable unqualified from one type and its enclosing types."""
    package, identity = key
    parts = identity.split(".")
    owners = {".".join(parts[:size]) for size in range(1, len(parts) + 1)}
    return {name for (reader_package, owner), name in readers if reader_package == package and owner in owners}


def _discover_reader_patterns(root: Path, files: list[Path], units: dict[Path, dict[str, object]]) -> None:
    """Register per-file reader patterns for text-returning helpers that read files or other readers."""
    candidates: list[tuple[tuple[tuple[str, ...], str], str, str, Path]] = []
    declared: dict[Path, set[tuple[tuple[str, ...], str]]] = {}
    aliases = {path: _aliases(list(units[path].get("imports", ()))) for path in files}
    for path in files:
        unit = units[path]
        text, masked, regions = str(unit["text"]), str(unit["masked"]), unit["regions"]
        methods = unit.get("methods")
        if methods is None:
            methods = _flow_module._method_infos(text, masked)
            unit["methods"] = methods
        package = path.relative_to(root).parts[:2]
        declared[path] = {(package, name) for _, _, name in regions}
        active = _active_lines(masked)
        starts = _text_module._line_starts(masked)
        for method in [*methods, *_generic_method_infos(masked)]:
            line = _text_module._line_for_offset(starts, method.start)
            if line < len(active) and not active[line]:
                continue
            owner = min(
                (region for region in regions if region[0] < method.body_start < region[1]),
                key=lambda region: region[1] - region[0],
                default=None,
            )
            if owner is None or _return_type(masked, method) not in TEXT_RETURN_TYPES:
                continue
            candidates.append(((package, owner[2]), method.name, masked[method.body_start : method.body_end + 1], path))
    declarations: dict[tuple[tuple[str, ...], str], set[str]] = {}
    for keys in declared.values():
        for package, identity in keys:
            declarations.setdefault((package, identity.rsplit(".", 1)[-1]), set()).add(identity)
    readers: set[tuple[tuple[tuple[str, ...], str], str]] = set()
    changed = True
    while changed:
        changed = False
        by_name: dict[str, set[tuple[tuple[tuple[str, ...], str], str]]] = {}
        for reader in readers:
            by_name.setdefault(reader[1], set()).add(reader)
        call_patterns: dict[tuple[object, ...], re.Pattern[str] | None] = {}
        for key, name, body, path in candidates:
            if (key, name) in readers:
                continue
            file_aliases, type_aliases = aliases[path]
            hit = bool(_primitive_pattern(file_aliases).search(body) or _calls_seed(body))
            present = frozenset(reader_name for reader_name in by_name if reader_name in body) if not hit else frozenset()
            if present:
                pattern_key = (key, path, present)
                if pattern_key not in call_patterns:
                    subset = set().union(*(by_name[reader_name] for reader_name in present))
                    visible = _visible_qualified_readers(
                        subset, key[0], declared[path], list(units[path].get("imports", ())), declarations
                    )
                    forms = _call_forms(subset, visible, type_aliases)
                    bare = _bare_form(_visible_names(subset, key))
                    forms.extend([bare] if bare else [])
                    call_patterns[pattern_key] = (
                        re.compile(rf"(?:{'|'.join(forms)})\s*(?:{GENERIC_ARGUMENTS})?\s*\(") if forms else None
                    )
                calls = call_patterns[pattern_key]
                hit = calls is not None and calls.search(body) is not None
            if hit:
                readers.add((key, name))
                changed = True
    compiled: dict[tuple[object, ...], re.Pattern[str]] = {}
    for path, keys in declared.items():
        masked = str(units[path]["masked"])
        present = {reader for reader in readers if reader[1] in masked}
        names = {
            name
            for name in {name for key in keys for name in _visible_names(present, key)}
            if not _text_module.SEED_READER_NAME.fullmatch(name)
        }
        package = path.relative_to(root).parts[:2]
        visible = _visible_qualified_readers(present, package, keys, list(units[path].get("imports", ())), declarations)
        forms = _call_forms(present, visible, aliases[path][1])
        bare = _bare_form(names)
        forms.extend([bare] if bare else [])
        cache_key = tuple(forms)
        if cache_key not in compiled:
            compiled[cache_key] = (
                re.compile(
                    rf"(?P<reader>\b(?:{_text_module.SEED_READER_ALTERNATIVES})|{'|'.join(forms)})"
                    rf"(?:\s*{GENERIC_ARGUMENTS})?\s*\("
                )
                if forms
                else _text_module.READER_CALL_PATTERN
            )
        _text_module._PATH_READER_PATTERNS[str(path)] = compiled[cache_key]
