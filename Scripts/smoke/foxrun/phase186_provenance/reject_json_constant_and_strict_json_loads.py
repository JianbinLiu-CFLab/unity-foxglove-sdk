from __future__ import annotations
from .foundation import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase186_provenance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
def _reject_json_constant(value: str) -> object:
    """Reject non-finite constants while parsing strict JSON."""

    raise ValueError(f"non-finite JSON number: {value}")
def _strict_json_loads(source: str | bytes) -> object:
    """Parse strict JSON: unique keys and finite RFC-compatible numbers."""

    return json.loads(
        source,
        object_pairs_hook=_strict_json_object,
        parse_constant=_reject_json_constant,
        parse_float=_strict_json_float,
    )
def _absolute_lexical_path(path: pathlib.Path) -> pathlib.Path:
    """Make a path absolute without following links or erasing ``..`` aliases."""

    return path if path.is_absolute() else pathlib.Path.cwd() / path
def _is_reparse_point(stat_result: os.stat_result) -> bool:
    """Return whether an lstat result denotes a symlink/junction/reparse point."""

    reparse_attribute = getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400)
    return stat.S_ISLNK(stat_result.st_mode) or bool(
        getattr(stat_result, "st_file_attributes", 0) & reparse_attribute
    )
def _canonical_json_sha256(value: object) -> str:
    """Hash a JSON value using the repository's canonical encoding."""

    encoded = json.dumps(
        value,
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
    ).encode("utf-8")
    return _sha256_bytes(encoded)
def path_inventory_digest(paths: Iterable[str]) -> str:
    """Hash one canonical sorted path inventory."""

    canonical = sorted({_normal_path(path) for path in paths if _normal_path(path)})
    payload = "".join(path + "\n" for path in canonical).encode("utf-8")
    return hashlib.sha256(payload).hexdigest()
def _subprocess_lines(command: Sequence[str], cwd: pathlib.Path) -> list[str]:
    """Run a command and return its UTF-8 stdout as non-empty lines."""

    completed = subprocess.run(
        list(command),
        cwd=cwd,
        check=False,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="strict",
    )
    if completed.returncode != 0:
        detail = (completed.stderr or completed.stdout).strip()
        raise RuntimeError(detail or f"command failed with exit code {completed.returncode}")
    return [line.strip() for line in completed.stdout.splitlines() if line.strip()]
def _substantial_lines(source: str) -> list[str]:
    """Extract normalized implementation lines suitable for overlap checks."""

    lines: list[str] = []
    for raw in source.splitlines():
        line = re.sub(r"\s+", " ", raw.strip())
        if (
            not line
            or line in {"{", "}", "};"}
            or line.startswith("using ")
            or line.startswith("namespace ")
            or "SPDX-License-Identifier:" in line
            or "Copyright (c)" in line
        ):
            continue
        lines.append(line)
    return lines
def _distinctive_windows(source: str) -> set[tuple[str, ...]]:
    """Return sufficiently substantial consecutive source-line windows."""

    lines = _substantial_lines(source)
    windows: set[tuple[str, ...]] = set()
    for index in range(0, len(lines) - _OVERLAP_LINE_COUNT + 1):
        window = tuple(lines[index : index + _OVERLAP_LINE_COUNT])
        if sum(len(line) for line in window) >= _OVERLAP_MIN_CHARS:
            windows.add(window)
    return windows
def _visible_markdown_source(source: str) -> str:
    """Remove HTML comments and fenced blocks from one Markdown document."""

    without_comments = re.sub(r"<!--.*?-->", "", source, flags=re.DOTALL)
    if "<!--" in without_comments or "-->" in without_comments:
        raise ValueError("visible authority Markdown has an unclosed HTML comment")

    visible_lines: list[str] = []
    fence_character: str | None = None
    fence_length = 0
    for line in without_comments.splitlines():
        if fence_character is not None:
            closing = re.fullmatch(
                rf" {{0,3}}{re.escape(fence_character)}"
                rf"{{{fence_length},}}[ \t]*",
                line,
            )
            if closing is not None:
                fence_character = None
                fence_length = 0
            continue
        opening = re.fullmatch(r" {0,3}(`{3,}|~{3,})(.*)", line)
        if opening is not None:
            marker = opening.group(1)
            info = opening.group(2)
            if marker[0] == "`" and "`" in info:
                visible_lines.append(line)
                continue
            fence_character = marker[0]
            fence_length = len(marker)
            continue
        visible_lines.append(line)
    if fence_character is not None:
        raise ValueError("visible authority Markdown has an unclosed fenced block")
    return "\n".join(visible_lines)
def _gfm_table_separator_cells(line: str) -> list[str] | None:
    """Recognize a GFM delimiter row with optional outer pipes."""

    candidate = line.strip()
    if "|" not in candidate:
        return None
    if candidate.startswith("|"):
        candidate = candidate[1:]
    if candidate.endswith("|"):
        candidate = candidate[:-1]
    cells = [cell.strip() for cell in candidate.split("|")]
    if len(cells) < 2 or any(
        re.fullmatch(r":?-{3,}:?", cell) is None for cell in cells
    ):
        return None
    return cells
def _visible_markdown_authority_section(source: str) -> tuple[str, str]:
    """Return visible Markdown and its one authoritative protocol section."""

    raw_lines = source.splitlines()
    heading = "## Frozen limits and implementation status"
    for line in raw_lines:
        if line.strip() and (line.startswith("    ") or line.startswith("\t")):
            raise ValueError(
                "visible authority document must not contain indented code"
            )
        if re.match(r"^ {0,3}(`{3,}|~{3,})", line):
            raise ValueError(
                "visible authority document must not contain fenced code"
            )
        if "<!--" in line or "-->" in line or re.search(
            r"</?[A-Za-z][^>]*>",
            line,
        ):
            raise ValueError(
                "visible authority document must not contain raw HTML"
            )
    raw_heading_indexes = [
        index for index, line in enumerate(raw_lines) if line == heading
    ]
    if len(raw_heading_indexes) != 1:
        raise ValueError(
            "visible authority must contain exactly one canonical column-0 "
            f"{heading!r} section, observed {len(raw_heading_indexes)}"
        )
    raw_start = raw_heading_indexes[0]
    raw_end = len(raw_lines)
    for index in range(raw_start + 1, len(raw_lines)):
        if raw_lines[index].startswith("## "):
            raw_end = index
            break
    raw_section_lines = raw_lines[raw_start:raw_end]
    for line in raw_section_lines:
        if line.strip() and (line.startswith("    ") or line.startswith("\t")):
            raise ValueError(
                "visible authority section must not contain indented code"
            )
        if re.match(r"^ {0,3}(`{3,}|~{3,})", line):
            raise ValueError(
                "visible authority section must not contain fenced code"
            )
        if "<!--" in line or "-->" in line or re.search(
            r"</?[A-Za-z][^>]*>",
            line,
        ):
            raise ValueError(
                "visible authority section must not contain raw HTML"
            )

    visible_source = _visible_markdown_source(source)
    visible_lines = visible_source.splitlines()
    heading_indexes = [
        index
        for index, line in enumerate(visible_lines)
        if line == heading
    ]
    if len(heading_indexes) != 1:
        raise ValueError(
            "visible authority must contain exactly one "
            f"{heading!r} section, observed {len(heading_indexes)}"
        )
    start = heading_indexes[0]
    end = len(visible_lines)
    for index in range(start + 1, len(visible_lines)):
        if visible_lines[index].startswith("## "):
            end = index
            break
    section_source = "\n".join(visible_lines[start:end])
    section_lines = section_source.splitlines()
    for line in visible_lines:
        if (
            (line.startswith("|") or line.endswith("|"))
            and (
                line != line.strip()
                or not line.startswith("|")
                or line.startswith("||")
                or not line.endswith("|")
                or line.endswith("||")
            )
        ):
            raise ValueError(
                "visible authority table lines must have exactly one leading "
                "and trailing pipe at canonical column 0"
            )
    expected_table_blocks = (
        (
            "| Offset | Width | Meaning |",
            "| --- | ---: | --- |",
        ),
        (
            "| Error code | Terminal | Allowed wire response |",
            "| --- | --- | --- |",
        ),
        (
            "| Limit | Value |",
            "| --- | ---: |",
        ),
    )
    observed_table_blocks: list[tuple[str, str]] = []
    observed_separator_indexes: list[int] = []
    for separator_index in range(1, len(visible_lines)):
        separator = visible_lines[separator_index]
        if _gfm_table_separator_cells(separator) is None:
            continue
        observed_table_blocks.append(
            (visible_lines[separator_index - 1], separator)
        )
        observed_separator_indexes.append(separator_index)
    expected_authority_membership = [False, True, True]
    observed_authority_membership = [
        start < index < end for index in observed_separator_indexes
    ]
    if (
        observed_table_blocks != list(expected_table_blocks)
        or observed_authority_membership != expected_authority_membership
    ):
        raise ValueError(
            "visible authority document must contain the one frozen envelope "
            "table plus exactly the two canonical tables inside the authority "
            "section, with no renamed or additional GFM table"
        )
    expected_table_headers = tuple(
        header for header, _ in expected_table_blocks[1:]
    )
    for table_header in expected_table_headers:
        global_count = sum(line == table_header for line in visible_lines)
        section_count = sum(line == table_header for line in section_lines)
        if global_count != 1 or section_count != 1:
            raise ValueError(
                "visible authority table must occur exactly once inside the "
                f"authority section: {table_header!r}"
            )
    return visible_source, section_source
def _markdown_table_rows(
    source: str,
    *,
    header: str,
    column_count: int,
    table_name: str,
) -> list[list[str]]:
    """Parse one uniquely headed Markdown table into validated cell rows."""

    lines = source.splitlines()
    header_indexes = [index for index, line in enumerate(lines) if line == header]
    if len(header_indexes) != 1:
        raise ValueError(
            f"{table_name} must contain exactly one header, "
            f"observed {len(header_indexes)}"
        )
    header_index = header_indexes[0]
    if header_index + 1 >= len(lines):
        raise ValueError(f"{table_name} has no separator row")
    separator_line = lines[header_index + 1]
    if (
        separator_line != separator_line.strip()
        or not separator_line.startswith("|")
        or separator_line.startswith("||")
        or not separator_line.endswith("|")
        or separator_line.endswith("||")
    ):
        raise ValueError(
            f"{table_name} separator must have exactly one leading and "
            "trailing pipe at canonical column 0"
        )
    separator_cells = [
        cell.strip() for cell in separator_line[1:-1].split("|")
    ]
    if len(separator_cells) != column_count or any(
        re.fullmatch(r":?-{3,}:?", cell) is None for cell in separator_cells
    ):
        raise ValueError(f"{table_name} has an invalid separator row")

    rows: list[list[str]] = []
    for line in lines[header_index + 2 :]:
        if not line.startswith("|"):
            break
        if (
            line != line.strip()
            or line.startswith("||")
            or not line.endswith("|")
            or line.endswith("||")
        ):
            raise ValueError(
                f"{table_name} row must have exactly one leading and "
                "trailing pipe at canonical column 0"
            )
        cells = [cell.strip() for cell in line[1:-1].split("|")]
        if len(cells) != column_count:
            raise ValueError(
                f"{table_name} row has {len(cells)} columns, "
                f"expected {column_count}: {line!r}"
            )
        rows.append(cells)
    return rows
def _validate_authority_table_row_uniqueness(
    section_source: str,
    *,
    identifiers: Sequence[str],
    table_name: str,
) -> list[str]:
    """Reject renamed/extra tables that repeat an authority row identity."""

    counts = {identifier: 0 for identifier in identifiers}
    for line in section_source.splitlines():
        if (
            line != line.strip()
            or not line.startswith("|")
            or line.startswith("||")
            or not line.endswith("|")
            or line.endswith("||")
        ):
            continue
        cells = [cell.strip() for cell in line[1:-1].split("|")]
        if not cells:
            continue
        match = re.fullmatch(r"`([^`]+)`", cells[0])
        if match is not None and match.group(1) in counts:
            counts[match.group(1)] += 1
    invalid = [
        f"{identifier}={count}"
        for identifier, count in counts.items()
        if count != 1
    ]
    if invalid:
        return [
            f"{table_name} has missing, extra, duplicate, or conflicting "
            "authority rows: " + ", ".join(invalid)
        ]
    return []
def _fixture_document_authority(
    implementation_sources: Mapping[str, str],
) -> tuple[list[tuple[str, str]], list[tuple[str, str, str]], list[str]]:
    """Read the exact limits/error documentation authority from the fixture."""

    errors: list[str] = []
    fixture_source = implementation_sources.get(_FIXTURE_RELATIVE)
    if fixture_source is None:
        return [], [], errors
    try:
        fixture = _strict_json_loads(fixture_source)
        limits = fixture["v2"]["commit2"]["limits"]
        error_codes = fixture["v2"]["errorCodes"]
        if not isinstance(limits, dict) or list(limits) != list(_LIMIT_NAMES):
            raise ValueError("fixture limit identities/order are not the frozen 27")
        if any(
            not isinstance(value, int) or isinstance(value, bool) or value <= 0
            for value in limits.values()
        ):
            raise ValueError("fixture limit values must be positive integers")
        limit_authority = [(name, str(value)) for name, value in limits.items()]
        if (
            not isinstance(error_codes, list)
            or len(error_codes) != len(_ERROR_CODES)
        ):
            raise ValueError("fixture errorCodes are not the frozen 23")
        error_authority: list[tuple[str, str, str]] = []
        for index, raw in enumerate(error_codes):
            if not isinstance(raw, dict):
                raise ValueError(f"fixture errorCodes[{index}] is not an object")
            code = raw.get("code")
            terminal = raw.get("terminal")
            response_ops = raw.get("responseOps")
            if (
                code != _ERROR_CODES[index]
                or not isinstance(terminal, bool)
                or not isinstance(response_ops, list)
                or any(not isinstance(operation, str) for operation in response_ops)
            ):
                raise ValueError(
                    f"fixture errorCodes[{index}] has invalid identity or mapping"
                )
            terminal_text = "yes" if terminal else "no"
            response_text = (
                ", ".join(f"`{operation}`" for operation in response_ops)
                if response_ops
                else "local only"
            )
            error_authority.append((f"`{code}`", terminal_text, response_text))
        return limit_authority, error_authority, errors
    except (KeyError, TypeError, ValueError, json.JSONDecodeError) as exc:
        errors.append(f"could not derive protocol document authority: {exc}")
        return [], [], errors


__all__ = [name for name in globals() if not name.startswith("__")]
