from __future__ import annotations
from .row_execution import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase186_bridge_build.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    """Parse Bridge build-matrix command-line arguments."""

    parser = argparse.ArgumentParser(description=__doc__)
    selection = parser.add_mutually_exclusive_group(required=True)
    selection.add_argument("--row", choices=tuple(ROWS))
    selection.add_argument("--all-supported-rows", action="store_true")
    parser.add_argument("--run-tests", action="store_true")
    parser.add_argument(
        "--output-root",
        type=pathlib.Path,
        default=None,
    )
    return parser.parse_args(argv)


def main(argv: Sequence[str] | None = None) -> int:
    """Run the selected Bridge build rows and write their evidence."""

    args = parse_args(argv)
    repository = repository_root()
    output_root = (
        args.output_root
        if args.output_root is not None
        else repository / "build" / "phase186" / "bridge"
    )
    selected = tuple(ROWS) if args.all_supported_rows else (args.row,)
    exit_code = 0
    for row_id in selected:
        row = require_row(row_id)
        print("[phase186-build] starting " + row.row_id, flush=True)
        result = run_row(
            repository,
            row,
            output_root,
            run_tests=args.run_tests,
        )
        print(
            "[phase186-build] "
            + row.row_id
            + " => "
            + str(result.get("verdict")),
            flush=True,
        )
        exit_code = max(exit_code, verdict_exit_code(result.get("verdict")))
    return exit_code


if __name__ == "__main__":
    raise SystemExit(main())


__all__ = [name for name in globals() if not name.startswith("__")]
