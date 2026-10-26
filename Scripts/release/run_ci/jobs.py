from __future__ import annotations

from .foundation import *
from .process import *

def build_dotnet_ci_jobs() -> list[CiJob]:
    """Build the independent runtime and xUnit lane self-subcommands."""
    script = str(Path(__file__).resolve())
    return [
        CiJob(
            "dotnet-runtime",
            [sys.executable, script, "--only", "dotnet-runtime"],
            exclusive_group=DOTNET_CI_EXCLUSIVE_GROUP,
        ),
        CiJob(
            "mediafoundation-native-smoke",
            [sys.executable, script, "--only", "mediafoundation-native-smoke"],
            exclusive_group=DOTNET_CI_EXCLUSIVE_GROUP,
        ),
        CiJob(
            "xunit",
            [sys.executable, script, "--only", "xunit"],
            exclusive_group=DOTNET_CI_EXCLUSIVE_GROUP,
        ),
        CiJob(
            "xunit-adapter",
            [sys.executable, script, "--only", "xunit-adapter"],
            exclusive_group=DOTNET_CI_EXCLUSIVE_GROUP,
        ),
        CiJob(
            "xunit-native",
            [sys.executable, script, "--only", "xunit-native"],
            exclusive_group=DOTNET_CI_EXCLUSIVE_GROUP,
        ),
        CiJob(
            "xunit-manager-boundary",
            [sys.executable, script, "--only", "xunit-manager-boundary"],
            exclusive_group=DOTNET_CI_EXCLUSIVE_GROUP,
        ),
        CiJob(
            "ros2bridge-xunit",
            [sys.executable, script, "--only", "ros2bridge-xunit"],
            exclusive_group=DOTNET_CI_EXCLUSIVE_GROUP,
        ),
        CiJob(
            "performance-regression",
            [sys.executable, script, "--only", "performance-regression"],
            exclusive_group=DOTNET_CI_EXCLUSIVE_GROUP,
        ),
    ]


def build_default_ci_jobs(args: argparse.Namespace) -> list[CiJob]:
    """Build independent self-subcommands for the default local CI run."""
    script = str(Path(__file__).resolve())
    jobs: list[CiJob] = []
    if not args.skip_analyzer:
        jobs.append(
            CiJob(
                "analyzer",
                [sys.executable, script, "--only", "analyzer"],
                exclusive_group=DOTNET_CI_EXCLUSIVE_GROUP,
            )
        )
    jobs.extend(build_dotnet_ci_jobs())
    jobs.extend(
        [
            CiJob("foxrun-publish-panel", [sys.executable, script, "--only", "foxrun-publish-panel"]),
            CiJob(
                "phase179-ros2-regression",
                [sys.executable, script, "--only", "phase179-ros2-regression"],
            ),
            CiJob(
                "phase181-ros2-regression",
                [sys.executable, script, "--only", "phase181-ros2-regression"],
            ),
            CiJob(
                "phase184-acceptance-tooling",
                [sys.executable, script, "--only", "phase184-acceptance-tooling"],
                disable_timeout=True,
                exclusive_group=DOTNET_CI_EXCLUSIVE_GROUP,
            ),
            CiJob(
                "phase186-bridge-tooling",
                [sys.executable, script, "--only", "phase186-bridge-tooling"],
                exclusive_group=DOTNET_CI_EXCLUSIVE_GROUP,
            ),
            CiJob(
                "mcap-conformance",
                [sys.executable, script, "--only", "mcap-conformance"],
                exclusive_group=DOTNET_CI_EXCLUSIVE_GROUP,
            ),
            CiJob(
                "packages",
                [sys.executable, script, "--only", "packages"],
                exclusive_group=DOTNET_CI_EXCLUSIVE_GROUP,
            ),
            CiJob("boundary", [sys.executable, script, "--only", "boundary"]),
        ]
    )
    return jobs


def _job_log_tail(log_path: Path, *, max_chars: int = 8000) -> str:
    """Return the tail of a job log for concise failure diagnostics."""
    try:
        text = log_path.read_text(encoding="utf-8", errors="replace")
    except OSError as exc:
        return f"<failed to read {log_path}: {exc}>"
    if len(text) <= max_chars:
        return text
    return "... <log truncated to tail> ...\n" + text[-max_chars:]


def _run_ci_job(job: CiJob, log_dir: Path) -> CiJobResult:
    """Run one CI suite as a captured self-subcommand."""
    log_path = log_dir / f"{job.name}.log"
    env = os.environ.copy()
    env["UNITY2FOXGLOVE_CI_RUN_ID"] = RUN_ID
    env.setdefault("PYTHONUNBUFFERED", "1")
    start = time.monotonic()
    effective_timeout = None if job.disable_timeout else job_timeout_seconds()
    tree = None
    try:
        # subprocess.run(timeout=...) only waits for the root process and can
        # strand a compiler/test descendant. Keep the whole lane in the same
        # owned job/process group so timeout cleanup is bounded and explicit.
        tree = start_owned_process(
            job.command,
            REPO_ROOT,
            env=env,
            text=True,
            encoding="utf-8",
            errors="replace",
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
        )
        try:
            # communicate() drains stdout/stderr concurrently with process
            # completion. Waiting first can deadlock once a noisy lane fills
            # the OS pipe buffer before it exits.
            stdout, _ = tree.process.communicate(timeout=effective_timeout)
        except subprocess.TimeoutExpired:
            residual = tree.terminate()
            try:
                stdout, _ = tree.process.communicate(timeout=5)
            except subprocess.TimeoutExpired:
                stdout = ""
            elapsed = time.monotonic() - start
            timeout_description = (
                "without a configured wall-clock deadline"
                if effective_timeout is None else f"after {effective_timeout}s"
            )
            timeout_message = f"\n{FAIL} {job.name} timed out {timeout_description} ({elapsed:.1f}s elapsed)\n"
            if residual:
                timeout_message += f"{FAIL} owned descendants remain: {residual}\n"
            log_path.write_text(_bound_captured_output((stdout or "") + timeout_message), encoding="utf-8")
            return CiJobResult(job.name, False, 124, elapsed, log_path)
        residual = await_tree_quiescence(tree, 30)
        if residual:
            residual = tree.terminate()
            if residual:
                elapsed = time.monotonic() - start
                log_path.write_text(
                    _bound_captured_output(
                        f"{FAIL} {job.name} exited with owned descendants: {residual}\n"
                    ), encoding="utf-8")
                return CiJobResult(job.name, False, 124, elapsed, log_path)
        elapsed = time.monotonic() - start
        returncode = tree.process.returncode
        log_path.write_text(_bound_captured_output(stdout or ""), encoding="utf-8")
        return CiJobResult(job.name, returncode == 0, returncode, elapsed, log_path)
    except OSError as ex:
        elapsed = time.monotonic() - start
        log_path.write_text(f"{FAIL} {job.name} could not start: {ex}\n", encoding="utf-8")
        return CiJobResult(job.name, False, 125, elapsed, log_path)
    finally:
        if tree is not None:
            tree.close()


def run_ci_jobs(jobs: list[CiJob], max_workers: int) -> dict[str, bool]:
    """Run top-level CI suites in parallel through captured self-subcommands."""
    if not jobs:
        return {}
    names = [job.name for job in jobs]
    if len(names) != len(set(names)):
        raise ValueError("CI job names must be unique")

    worker_count = max(1, min(max_workers, len(jobs)))
    log_dir = CI_ROOT / "logs"
    log_dir.mkdir(parents=True, exist_ok=True)

    print(f"\n{cyan('--- local CI jobs (parallel) ---')}")
    print(f"Run id: {RUN_ID}")
    print(f"Logs: {log_dir}")
    print(f"Workers: {worker_count}")
    for job in jobs:
        print(f"  - {job.name}: {' '.join(job.command)}")

    results_by_name: dict[str, CiJobResult] = {}
    with ThreadPoolExecutor(max_workers=worker_count) as executor:
        pending = list(jobs)
        running: dict[Future[CiJobResult], CiJob] = {}
        active_groups: set[str] = set()

        while pending or running:
            while len(running) < worker_count:
                compatible_index = next(
                    (
                        index
                        for index, job in enumerate(pending)
                        if job.exclusive_group is None or job.exclusive_group not in active_groups
                    ),
                    None,
                )
                if compatible_index is None:
                    break

                job = pending.pop(compatible_index)
                if job.exclusive_group is not None:
                    active_groups.add(job.exclusive_group)
                running[executor.submit(_run_ci_job, job, log_dir)] = job

            if not running:
                raise RuntimeError("CI scheduler found no compatible pending job")

            completed, _ = wait(running, return_when=FIRST_COMPLETED)
            for future in completed:
                job = running.pop(future)
                try:
                    result = future.result()
                finally:
                    if job.exclusive_group is not None:
                        active_groups.remove(job.exclusive_group)

                results_by_name[result.name] = result
                status = PASS if result.ok else FAIL
                colour = green if result.ok else red
                print(
                    colour(
                        f"{status} {result.name} "
                        f"({result.elapsed_seconds:.1f}s, log: {result.log_path})"
                    )
                )
                if not result.ok:
                    print(_job_log_tail(result.log_path), file=sys.stderr)

    return {job.name: results_by_name[job.name].ok for job in jobs}


def report_ci_job_results(
    results: dict[str, bool],
    skipped: tuple[str, ...] = (),
) -> int:
    """Print the top-level CI aggregate summary and return its exit code."""
    print(f"\n{'=' * 60}")
    for name, ok in results.items():
        print(f"  {green(PASS) if ok else red(FAIL)} {name}")

    for name in skipped:
        print(f"  {SKIP} {name}")
    if skipped:
        print(f"SKIPPED_LANES={','.join(skipped)}")

    if not results:
        print(f"\n{red('No CI checks were executed.')}")
        return 1

    failed = [n for n, ok in results.items() if not ok]
    if failed:
        print(f"\n{red('Failed: ' + ', '.join(failed))}")
        return 1

    if skipped:
        print(f"\n{green('All executed CI checks passed.')}")
    else:
        print(f"\n{green('All CI checks passed.')}")
    return 0

__all__ = ['_job_log_tail', '_run_ci_job', 'build_default_ci_jobs', 'build_dotnet_ci_jobs', 'report_ci_job_results', 'run_ci_jobs']
