from __future__ import annotations
from .runtime_probe import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase186_bridge_capability_probe.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    """Parse Bridge capability-probe command-line arguments."""

    parser = argparse.ArgumentParser(description=__doc__)
    selection = parser.add_mutually_exclusive_group(required=True)
    selection.add_argument("--row", choices=tuple(build.ROWS))
    selection.add_argument("--all-supported-rows", action="store_true")
    parser.add_argument(
        "--output-root",
        type=pathlib.Path,
        default=None,
    )
    parser.add_argument(
        "--timeout-seconds",
        type=float,
        default=60.0,
    )
    parser.add_argument(
        "--skip-build",
        action="store_true",
        help="Require an already passing exact-row build summary.",
    )
    return parser.parse_args(argv)


def main(argv: Sequence[str] | None = None) -> int:
    """Run selected capability rows and validate the resulting matrix."""

    args = parse_args(argv)
    if args.timeout_seconds <= 0 or args.timeout_seconds > 300:
        raise SystemExit("--timeout-seconds must be in (0, 300]")
    repository = build.repository_root()
    output_root = (
        args.output_root
        if args.output_root is not None
        else repository / "build" / "phase186" / "bridge"
    )
    selected = (
        tuple(build.ROWS)
        if args.all_supported_rows
        else (args.row,)
    )
    results: dict[str, Mapping[str, object]] = {}
    exit_code = 0
    for row_id in selected:
        row = build.require_row(row_id)
        if not args.skip_build:
            built = build.run_row(
                repository,
                row,
                output_root,
                run_tests=True,
            )
            if built.get("verdict") != "PASS":
                results[row_id] = built
                exit_code = max(
                    exit_code,
                    build.verdict_exit_code(built.get("verdict")),
                )
                continue
        print("[phase186-probe] starting " + row_id, flush=True)
        result = run_row(
            repository,
            row,
            output_root,
            timeout_seconds=args.timeout_seconds,
        )
        results[row_id] = result
        print(
            "[phase186-probe] "
            + row_id
            + " => "
            + str(result.get("verdict")),
            flush=True,
        )
        exit_code = max(
            exit_code,
            build.verdict_exit_code(result.get("verdict")),
        )
    if args.all_supported_rows:
        matrix_path = pathlib.Path(output_root) / "capability-matrix.json"
        try:
            matrix = validate_matrix(results)
            matrix = {
                **matrix,
                "rowEvidence": {
                    row_id: str(
                        pathlib.Path(output_root)
                        / row_id
                        / "capability-result.json"
                    )
                    for row_id in build.ROWS
                },
                "finishedAt": build.timestamp(),
            }
        except ProbeFailure as exc:
            matrix = {
                "schemaVersion": SUMMARY_SCHEMA_VERSION,
                "verdict": "FAIL",
                "failure": str(exc),
                "rows": list(results),
                "selectedMechanism": "",
                "canonicalType": build.INTERFACE_TYPE,
                "interfaceDigest": build.INTERFACE_DIGEST,
                "finishedAt": build.timestamp(),
            }
            exit_code = max(exit_code, 1)
        _write_json_atomic(matrix_path, matrix)
    return exit_code


if __name__ == "__main__":
    raise SystemExit(main())


__all__ = [name for name in globals() if not name.startswith("__")]
