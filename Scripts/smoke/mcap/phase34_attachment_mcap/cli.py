"""Command-line generation and self-check orchestration for Phase34."""

from __future__ import annotations

try:
    from Scripts.smoke.atomic_output import atomic_write_bytes, atomic_write_text
except ModuleNotFoundError:
    import sys as _sys
    from pathlib import Path as _Path
    _sys.path.insert(0, str(_Path(__file__).resolve().parents[4]))
    from Scripts.smoke.atomic_output import atomic_write_bytes, atomic_write_text

import argparse
from pathlib import Path

from .fixture_builder import *
from .fixture_constants import *
from .fixture_validator import *


def resolve_output(output: str | None) -> Path:
    """Resolve an optional output path relative to the repository root."""
    repo_root = Path(__file__).resolve().parents[REPO_ROOT_PARENT_DEPTH]
    candidate = Path(output) if output else DEFAULT_OUTPUT
    if not candidate.is_absolute():
        candidate = repo_root / candidate
    return candidate.resolve()


def main() -> int:
    """Generate the fixture, self-check it, and print output details."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", "-o", help="Output MCAP path. Defaults to build/test_mcap/phase34_attachment_smoke.mcap.")
    args = parser.parse_args()

    output = resolve_output(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)

    data, attachment_data = build_smoke_file()
    atomic_write_bytes(output, data)
    self_check(output, attachment_data)

    print(f"[phase34-smoke] Generated: {output}")
    print(f"[phase34-smoke] Size:      {output.stat().st_size} bytes")
    print(
        "[phase34-smoke] Self-check: leading/trailing magic, summary_crc, "
        "ChunkIndex, AttachmentIndex, payload, and attachment CRC passed."
    )
    return EXIT_SUCCESS


if __name__ == "__main__":
    raise SystemExit(main())

__all__ = [name for name in globals() if not name.startswith("__")]
