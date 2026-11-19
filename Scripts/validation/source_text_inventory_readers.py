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

READ_PRIMITIVE_PATTERN = re.compile(
    r"\bFile\s*\.\s*(?:ReadAllText|ReadAllLines|ReadLines|OpenText)\s*\("
    r"|\bnew\s+(?:[A-Za-z_]\w*\s*\.\s*)*StreamReader\s*\("
)
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


def _return_type(masked: str, method: object) -> str:
    """Return the declared return type written immediately before a method name."""
    prefix = masked[max(0, method.start - 200) : method.body_start]
    matches = list(
        re.finditer(
            rf"(?P<type>[A-Za-z_][\w.]*(?:\s*<[^<>;{{}}()=]*>)?(?:\s*\[\s*\])?)\s+{re.escape(method.name)}\s*\(",
            prefix,
        )
    )
    return re.sub(r"\s+", "", matches[-1].group("type")) if matches else ""


def _bare_names_pattern(names: set[str]) -> re.Pattern[str] | None:
    """Match unqualified calls to discovered readers while excluding member access."""
    if not names:
        return None
    alternatives = "|".join(re.escape(name) for name in sorted(names, key=len, reverse=True))
    return re.compile(rf"(?<![\w.])(?:{alternatives})\s*\(")


def _qualified_alternatives(readers: set[tuple[tuple[tuple[str, ...], str], str]]) -> str:
    """Return regex alternatives for Type.Reader calls of every discovered reader."""
    pairs = sorted({(owner.rsplit(".", 1)[-1], name) for (_, owner), name in readers})
    return "|".join(rf"{re.escape(owner)}\s*\.\s*{re.escape(name)}" for owner, name in pairs)


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
    candidates: list[tuple[tuple[tuple[str, ...], str], str, str]] = []
    declared: dict[Path, set[tuple[tuple[str, ...], str]]] = {}
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
        for method in methods:
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
            candidates.append(((package, owner[2]), method.name, masked[method.body_start : method.body_end + 1]))
    readers: set[tuple[tuple[tuple[str, ...], str], str]] = set()
    changed = True
    while changed:
        changed = False
        qualified = _qualified_alternatives(readers)
        qualified_pattern = re.compile(rf"\b(?:{qualified})\s*\(") if qualified else None
        bare_patterns: dict[tuple[tuple[str, ...], str], re.Pattern[str] | None] = {}
        for key, name, body in candidates:
            if (key, name) in readers:
                continue
            if key not in bare_patterns:
                bare_patterns[key] = _bare_names_pattern(_visible_names(readers, key))
            bare = bare_patterns[key]
            if (
                READ_PRIMITIVE_PATTERN.search(body)
                or _text_module.READER_CALL_PATTERN.search(body)
                or (qualified_pattern is not None and qualified_pattern.search(body))
                or (bare is not None and bare.search(body))
            ):
                readers.add((key, name))
                changed = True
    qualified = _qualified_alternatives(readers)
    compiled: dict[tuple[str, ...], re.Pattern[str]] = {}
    for path, keys in declared.items():
        names = sorted(
            name
            for name in {name for key in keys for name in _visible_names(readers, key)}
            if not _text_module.SEED_READER_NAME.fullmatch(name)
        )
        cache_key = tuple(names)
        if cache_key not in compiled:
            extra = []
            if names:
                extra.append("(?<![\\w.])(?:" + "|".join(re.escape(name) for name in sorted(names, key=len, reverse=True)) + ")")
            if qualified:
                extra.append(f"(?:{qualified})")
            compiled[cache_key] = (
                re.compile(rf"\b(?P<reader>{_text_module.SEED_READER_ALTERNATIVES}|{'|'.join(extra)})\s*\(")
                if extra
                else _text_module.READER_CALL_PATTERN
            )
        _text_module._PATH_READER_PATTERNS[str(path)] = compiled[cache_key]
