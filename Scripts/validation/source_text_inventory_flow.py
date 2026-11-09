"""Control-flow and helper-call analysis for source-text inventory."""

from __future__ import annotations

import hashlib
import re
from dataclasses import dataclass

try:
    from .source_text_inventory_text import (
        _balanced_brace_end,
        _balanced_call_end,
        _strip_csharp_comments_and_strings,
    )
except ImportError:
    from source_text_inventory_text import (
        _balanced_brace_end,
        _balanced_call_end,
        _strip_csharp_comments_and_strings,
    )


def _reachability_signatures(
    lines: list[str], calls: list[dict[str, object]], text: str, masked: str
) -> dict[int, str]:
    """Return preprocessor and enclosing control-flow state for calls in one file."""
    states: list[str] = []
    directives: list[str] = []
    for line in lines:
        directive = line.strip()
        states.append("|".join(directives))
        if directive.startswith("#if"):
            directives.append("if:" + re.sub(r"\s+", " ", directive[3:].strip()))
        elif directive.startswith("#elif") and directives:
            directives[-1] = "elif:" + re.sub(r"\s+", " ", directive[5:].strip())
        elif directive.startswith("#else") and directives:
            directives[-1] = "else"
        elif directive.startswith("#endif") and directives:
            directives.pop()
    intervals: list[tuple[int, int, str]] = []
    for match in re.finditer(
        r"\b(?P<kind>if|else\s+if|while|foreach|for|switch|catch)\s*\(", masked
    ):
        open_index = masked.find("(", match.start(), match.end())
        close_index = _balanced_call_end(masked, open_index)
        if close_index is None:
            continue
        cursor = close_index + 1
        while cursor < len(masked) and masked[cursor].isspace():
            cursor += 1
        condition = re.sub(r"\s+", " ", text[open_index + 1 : close_index].strip())
        end = (
            _balanced_brace_end(masked, cursor)
            if cursor < len(masked) and masked[cursor] == "{"
            else masked.find(";", cursor)
        )
        if end is not None and end >= cursor:
            intervals.append((cursor, end, f"{match.group('kind')}({condition})"))
    exits = [
        (match.start(), match.end(), "exit:" + match.group("kind"))
        for match in re.finditer(r"\b(?P<kind>return|throw|continue|break|goto)\b[^;]*;", masked)
    ]
    result: dict[int, str] = {}
    for call in calls:
        start = int(call["start"])
        line = int(call["start_line"])
        block_start = max(masked.rfind("{", 0, start), masked.rfind("}", 0, start))
        active = [value for begin, end, value in intervals if begin <= start <= end]
        active.extend(value for begin, end, value in exits if block_start < begin and end < start)
        result[start] = "|".join(
            ([states[line]] if line < len(states) and states[line] else []) + active
        )
    return result
