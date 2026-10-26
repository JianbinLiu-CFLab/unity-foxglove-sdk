#!/usr/bin/env python3
from __future__ import annotations
from foundation import *
from il2cpp_logging import *
from process import *
def non_negative_int(value: str) -> int:
    """Parse an integer whose only timeout-disable sentinel is zero."""

    parsed = int(value)
    if parsed < 0:
        raise argparse.ArgumentTypeError("must be zero or greater")
    return parsed


def parse_args() -> argparse.Namespace:
    """Parse CLI arguments for the build script."""
    parser = argparse.ArgumentParser(
        description="Run Unity batchmode IL2CPP build for the Foxglove demo project."
    )
    parser.add_argument(
        "--target",
        choices=TARGETS,
        default=default_target(),
        help="Build target. Defaults to the current host platform.",
    )
    parser.add_argument(
        "--unity",
        help="Path to the Unity executable. Defaults to UNITY_EXE/UNITY_PATH or Unity Hub discovery.",
    )
    parser.add_argument(
        "--project",
        default="Unity2Foxglove",
        help="Unity project path relative to the workspace root.",
    )
    parser.add_argument(
        "--log",
        help="Log path relative to the workspace root. Defaults to <build-dir>/build.log.",
    )
    parser.add_argument(
        "--build-dir",
        help="Build run directory relative to the workspace root. Defaults to build/Unity/<target>-il2cpp-<timestamp>/.",
    )
    parser.add_argument(
        "--output",
        help="Player output path relative to the workspace root. Defaults inside <build-dir>.",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Print the resolved project, target, and log path without starting Unity.",
    )
    parser.add_argument(
        "--allow-missing-unity",
        action="store_true",
        help="Allow dry-run path validation when Unity is not installed. Valid only with --dry-run.",
    )
    parser.add_argument(
        "--progress-interval",
        type=int,
        default=DEFAULT_PROGRESS_INTERVAL_SECONDS,
        help="Seconds between progress heartbeats while Unity is running.",
    )
    parser.add_argument(
        "--timeout-minutes",
        type=non_negative_int,
        default=DEFAULT_BUILD_TIMEOUT_MINUTES,
        help="Maximum Unity build runtime before terminating. Use 0 to disable the timeout.",
    )
    return parser.parse_args()


def _interrupt_build(_signum: int, _frame: object) -> None:
    """Convert termination signals into the controlled cleanup path."""
    raise KeyboardInterrupt


def main() -> int:
    """Main entry: parse args, build command, run Unity, report result."""
    args = parse_args()
    root = repo_root()

    if args.allow_missing_unity and not args.dry_run:
        print(
            "[build_unity_il2cpp] --allow-missing-unity is only valid with --dry-run.",
            file=sys.stderr,
        )
        return EXIT_USAGE_ERROR

    try:
        cmd, project_path, log_path, output_path = build_command(args)
    except (OSError, ValueError) as exc:
        print(f"[build_unity_il2cpp] {exc}", file=sys.stderr)
        return EXIT_USAGE_ERROR

    print(f"[build_unity_il2cpp] Unity:    {cmd[UNITY_EXECUTABLE_COMMAND_INDEX]}")
    print(f"[build_unity_il2cpp] Project:   {relative_to_root(project_path, root)}")
    print(f"[build_unity_il2cpp] Target:    {args.target}")
    print(f"[build_unity_il2cpp] Log:       {relative_to_root(log_path, root)}")
    print(f"[build_unity_il2cpp] Output:    {relative_to_root(output_path, root)}")

    generated_failures = validate_generated_artifacts(root)
    if generated_failures:
        print("[build_unity_il2cpp] Generated artifact preflight failed:", file=sys.stderr)
        for failure in generated_failures:
            print(f"  {failure}", file=sys.stderr)
        print(
            "[build_unity_il2cpp] Regenerate schema/source-generator artifacts before invoking Unity.",
            file=sys.stderr,
        )
        return EXIT_PREFLIGHT_FAILURE

    if args.dry_run:
        print("[build_unity_il2cpp] Dry run only; Unity was not started.")
        return EXIT_SUCCESS

    log_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_before = output_fingerprint(output_path)

    print("[build_unity_il2cpp] Starting Unity batchmode build...")

    previous_sigint = signal.getsignal(signal.SIGINT)
    previous_sigterm = signal.getsignal(signal.SIGTERM)

    signal.signal(signal.SIGINT, _interrupt_build)
    signal.signal(signal.SIGTERM, _interrupt_build)
    try:
        try:
            returncode = run_with_progress(
                cmd,
                root,
                log_path,
                max(MIN_PROGRESS_INTERVAL_SECONDS, args.progress_interval),
                args.timeout_minutes,
            )
        except KeyboardInterrupt:
            print("[build_unity_il2cpp] Build interrupted; owned process tree cleanup requested.", file=sys.stderr)
            return EXIT_PREFLIGHT_FAILURE
    except OSError as exc:
        print(f"[build_unity_il2cpp] Unity could not be started: {exc}", file=sys.stderr)
        return EXIT_PREFLIGHT_FAILURE
    finally:
        signal.signal(signal.SIGINT, previous_sigint)
        signal.signal(signal.SIGTERM, previous_sigterm)
    if returncode == EXIT_SUCCESS:
        output_after = output_fingerprint(output_path)
        if output_after is None:
            print(
                "[build_unity_il2cpp] Unity reported success but produced no Player at "
                f"{relative_to_root(output_path, root)}.",
                file=sys.stderr,
            )
            return EXIT_PREFLIGHT_FAILURE
        if output_after == output_before:
            print(
                "[build_unity_il2cpp] Unity reported success but left the pre-existing Player at "
                f"{relative_to_root(output_path, root)} unchanged; it is not this build's output.",
                file=sys.stderr,
            )
            return EXIT_PREFLIGHT_FAILURE
        print("[build_unity_il2cpp] Build command completed successfully.")
    else:
        print(
            f"[build_unity_il2cpp] Build failed with exit code {returncode}. "
            f"See log: {relative_to_root(log_path, root)}",
            file=sys.stderr,
        )

    return returncode

__all__ = ['non_negative_int', 'parse_args', '_interrupt_build', 'main']
