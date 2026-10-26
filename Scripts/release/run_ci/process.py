from __future__ import annotations

from .foundation import *

def _run_owned_command(
    cmd: list[str],
    *,
    capture_output: bool,
    timeout_seconds: int | None,
) -> OwnedCommandResult:
    """Run a command and own every descendant until it is quiescent."""
    popen_kwargs = {}
    if capture_output:
        popen_kwargs.update(
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            errors="replace",
        )
    tree = None
    try:
        tree = start_owned_process(cmd, REPO_ROOT, **popen_kwargs)
        try:
            stdout, stderr = tree.process.communicate(timeout=timeout_seconds)
        except subprocess.TimeoutExpired as expired:
            partial_stdout = expired.stdout.decode(errors="replace") if isinstance(expired.stdout, bytes) else (expired.stdout or "")
            partial_stderr = expired.stderr.decode(errors="replace") if isinstance(expired.stderr, bytes) else (expired.stderr or "")
            residual = tuple(tree.terminate())
            try:
                tail_stdout, tail_stderr = tree.process.communicate(timeout=2)
            except (OSError, subprocess.TimeoutExpired):
                tail_stdout, tail_stderr = "", ""
            return OwnedCommandResult(
                124,
                partial_stdout + (tail_stdout or ""),
                partial_stderr + (tail_stderr or ""),
                timed_out=True,
                residual_pids=residual,
            )

        returncode = tree.process.returncode
        residual = tuple(await_tree_quiescence(tree, 1.0))
        if residual:
            residual = tuple(tree.terminate())
            if residual:
                return OwnedCommandResult(
                    125,
                    stdout or "",
                    stderr or "",
                    residual_pids=residual,
                )
        return OwnedCommandResult(returncode, stdout or "", stderr or "")
    except OSError as exc:
        return OwnedCommandResult(125, "", str(exc))
    finally:
        if tree is not None:
            tree.close()


def run(
    cmd: list[str],
    label: str,
    *,
    fatal: bool = False,
    timeout_seconds: int | None = None,
    disable_timeout: bool = False,
) -> bool:
    """Run a subprocess command and return True on success."""
    print(f"\n{cyan('--- ' + label + ' ---')}")
    effective_timeout = (
        None
        if disable_timeout
        else command_timeout_seconds() if timeout_seconds is None else max(1, timeout_seconds)
    )
    start = time.monotonic()
    result = _run_owned_command(
        cmd,
        capture_output=False,
        timeout_seconds=effective_timeout,
    )
    if result.timed_out:
        elapsed = time.monotonic() - start
        print(red(f"{FAIL} {label} timed out after {effective_timeout}s ({elapsed:.1f}s)"))
        if result.residual_pids:
            print(red(f"{FAIL} {label} owned descendants remain: {result.residual_pids}"))
        if fatal:
            raise SystemExit(124)
        return False
    elapsed = time.monotonic() - start
    ok = result.returncode == 0 and not result.residual_pids
    if ok:
        print(green(f"{PASS} {label} ({elapsed:.1f}s)"))
    else:
        print(red(f"{FAIL} {label} (exit {result.returncode}) ({elapsed:.1f}s)"))
        if result.residual_pids:
            print(red(f"{FAIL} {label} owned descendants remain: {result.residual_pids}"))
        if fatal:
            raise SystemExit(result.returncode)
    return ok


def _bound_captured_output(value: str, limit: int = MAX_CAPTURED_OUTPUT_CHARS) -> str:
    """Bound retained validator output while preserving an explicit truncation marker."""
    if len(value) <= limit:
        return value
    marker = f"\n... <output truncated at {limit} characters> ...\n"
    return value[: max(0, limit - len(marker))] + marker


def run_captured(cmd: list[str], label: str) -> CapturedCommandResult:
    """Run a subprocess and capture output for later ordered replay."""
    effective_timeout = command_timeout_seconds()
    start = time.monotonic()
    result = _run_owned_command(
        cmd,
        capture_output=True,
        timeout_seconds=effective_timeout,
    )
    if result.timed_out:
        elapsed = time.monotonic() - start
        stderr = result.stderr
        if result.residual_pids:
            stderr += f"\n{FAIL} {label} owned descendants remain: {result.residual_pids}\n"
        return CapturedCommandResult(
            label,
            False,
            124,
            elapsed,
            _bound_captured_output(result.stdout),
            _bound_captured_output(stderr),
            timeout_seconds=effective_timeout,
        )
    elapsed = time.monotonic() - start
    return CapturedCommandResult(
        label,
        result.returncode == 0 and not result.residual_pids,
        result.returncode,
        elapsed,
        _bound_captured_output(result.stdout),
        _bound_captured_output(result.stderr),
    )


def run_parallel(commands: list[tuple[str, list[str]]]) -> dict[str, bool]:
    """Run independent commands concurrently and replay their output in declaration order."""
    labels = [label for label, _ in commands]
    if len(labels) != len(set(labels)):
        raise ValueError("parallel CI command labels must be unique")
    print(f"\n{cyan('--- package validators (parallel) ---')}")
    results_by_label: dict[str, CapturedCommandResult] = {}
    with ThreadPoolExecutor(max_workers=len(commands)) as executor:
        futures = {
            executor.submit(run_captured, cmd, label): label
            for label, cmd in commands
        }
        for future in as_completed(futures):
            result = future.result()
            results_by_label[result.label] = result

    ordered_results: dict[str, bool] = {}
    for label, _ in commands:
        result = results_by_label[label]
        print(f"\n{cyan('--- ' + label + ' ---')}")
        if result.stdout:
            print(result.stdout, end="" if result.stdout.endswith("\n") else "\n")
        if result.stderr:
            print(result.stderr, end="" if result.stderr.endswith("\n") else "\n", file=sys.stderr)
        if result.ok:
            print(green(f"{PASS} {label} ({result.elapsed_seconds:.1f}s)"))
        elif result.timeout_seconds is not None:
            print(
                red(
                    f"{FAIL} {label} timed out after {result.timeout_seconds}s "
                    f"({result.elapsed_seconds:.1f}s)"
                )
            )
        else:
            print(red(f"{FAIL} {label} (exit {result.returncode}) ({result.elapsed_seconds:.1f}s)"))
        ordered_results[label] = result.ok
    return ordered_results

__all__ = ['_bound_captured_output', '_run_owned_command', 'run', 'run_captured', 'run_parallel']
