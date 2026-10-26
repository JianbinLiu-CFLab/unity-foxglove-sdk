from __future__ import annotations

from .foundation import *
from .process import *
from .jobs import *

def restore_with_ignoring_failed_sources(
    project: str,
    label: str,
    msbuild_props: list[str] | None = None,
    *,
    fatal: bool = True,
) -> bool:
    """Restore a project with strict source-availability semantics.

    The historical helper name is retained for call-site compatibility, but
    no tolerance flag is appended.  Local and remote restore commands now
    report the same outage result; cache hits cannot hide an unavailable feed.
    """
    msbuild_props = msbuild_props or []
    cmd = ["dotnet", "restore", project, *msbuild_props, *IGNORE_FAILED_SOURCES_OPTION]
    return run(cmd, label, fatal=fatal)


_RESTORE_STATE_MARKERS = (
    "project.assets.json",
    "NETSDK1004",
    "NETSDK1005",
    "NETSDK1047",
    "MSB4019",
)


def _is_restore_state_failure(result: CapturedCommandResult) -> bool:
    """Return true only for failures that indicate missing/stale restore state."""
    output = f"{result.stdout}\n{result.stderr}"
    return any(marker in output for marker in _RESTORE_STATE_MARKERS)


def run_with_restore_fallback(
    project_cmd: list[str],
    fallback_cmd: list[str],
    label: str,
) -> bool:
    """Retry a no-restore command only when its failure is restore-state specific."""
    first = run_captured(project_cmd, label)
    if first.ok:
        print(green(f"{PASS} {label} ({first.elapsed_seconds:.1f}s)"))
        return True

    if first.stdout:
        print(first.stdout, end="" if first.stdout.endswith("\n") else "\n")
    if first.stderr:
        print(first.stderr, end="" if first.stderr.endswith("\n") else "\n", file=sys.stderr)
    if first.timeout_seconds is not None:
        print(red(f"{FAIL} {label} timed out after {first.timeout_seconds}s ({first.elapsed_seconds:.1f}s)"))
        return False
    print(red(f"{FAIL} {label} (exit {first.returncode}) ({first.elapsed_seconds:.1f}s)"))
    if not _is_restore_state_failure(first):
        print(red(f"{FAIL} {label}: refusing restore retry for non-restore failure"))
        return False

    return run(fallback_cmd, f"{label} (retry with restore)", fatal=False)


def run_mediafoundation_native_smoke(
    project_cmd: list[str],
    fallback_cmd: list[str],
    label: str,
) -> tuple[bool, bool]:
    """Run the native smoke and return (passed, environment_skipped)."""
    def finish(result: CapturedCommandResult) -> tuple[bool, bool]:
        """Echo captured output and classify the smoke result."""
        if result.stdout:
            print(result.stdout, end="" if result.stdout.endswith("\n") else "\n")
        if result.stderr:
            print(result.stderr, end="" if result.stderr.endswith("\n") else "\n", file=sys.stderr)
        if not result.ok:
            if result.timeout_seconds is not None:
                print(red(f"{FAIL} {label} timed out after {result.timeout_seconds}s ({result.elapsed_seconds:.1f}s)"))
            else:
                print(red(f"{FAIL} {label} (exit {result.returncode}) ({result.elapsed_seconds:.1f}s)"))
            return False, False

        output = f"{result.stdout}\n{result.stderr}"
        if "NATIVE_SMOKE_SKIPPED:" in output:
            print(f"{SKIP} {label} ({result.elapsed_seconds:.1f}s)")
            return True, True

        print(green(f"{PASS} {label} ({result.elapsed_seconds:.1f}s)"))
        return True, False

    first = run_captured(project_cmd, label)
    if first.ok:
        return finish(first)
    if first.stdout:
        print(first.stdout, end="" if first.stdout.endswith("\n") else "\n")
    if first.stderr:
        print(first.stderr, end="" if first.stderr.endswith("\n") else "\n", file=sys.stderr)
    if first.timeout_seconds is not None:
        print(red(f"{FAIL} {label} timed out after {first.timeout_seconds}s ({first.elapsed_seconds:.1f}s)"))
        return False, False
    if not _is_restore_state_failure(first):
        print(red(f"{FAIL} {label}: refusing restore retry for non-restore failure"))
        return False, False
    return finish(run_captured(fallback_cmd, f"{label} (retry with restore)"))

def _check_boundary() -> bool:
    """Verify no tracked Plan/ or Developer/ files (matches repository-boundary-check)."""
    try:
        root_private = subprocess.run(
        ["git", "ls-files", "--", "Plan/**", "Developer/**"],
        capture_output=True, text=True, cwd=REPO_ROOT,
            timeout=command_timeout_seconds(),
        )
    except subprocess.TimeoutExpired:
        print(f"\n{red('FAIL')} git ls-files timed out while checking Plan/Developer/")
        return False
    if root_private.returncode != 0:
        print(f"\n{red('FAIL')} git ls-files failed while checking Plan/Developer/:")
        print(root_private.stderr.strip())
        return False
    if root_private.stdout.strip():
        print(f"\n{red('FAIL')} Plan/ or Developer/ files are tracked and must not be:")
        print(root_private.stdout)
        return False

    try:
        all_tracked = subprocess.run(
        ["git", "ls-files"],
        capture_output=True, text=True, cwd=REPO_ROOT,
            timeout=command_timeout_seconds(),
        )
    except subprocess.TimeoutExpired:
        print(f"\n{red('FAIL')} git ls-files timed out while checking tracked files")
        return False
    if all_tracked.returncode != 0:
        print(f"\n{red('FAIL')} git ls-files failed while checking nested Developer/ files:")
        print(all_tracked.stderr.strip())
        return False
    nested_dev = [
        path for path in all_tracked.stdout.splitlines()
        if path == "Developer.meta"
        or "/Developer/" in path
        or path.endswith("/Developer.meta")
    ]
    if nested_dev:
        print(f"\n{red('FAIL')} Developer workspace files are tracked:")
        print("\n".join(nested_dev))
        return False

    print(f"\n{green(PASS)} Boundary check (no tracked Plan/Developer/)")
    return True


def _check_changelog_verified_stubs() -> bool:
    """Fail if release changelog verification stubs were not replaced."""
    stub = "should be run before tagging this release"
    path = REPO_ROOT / "CHANGELOG.md"
    text = path.read_text(encoding="utf-8")
    if stub not in text:
        print(f"\n{green(PASS)} Changelog verified sections contain no release stubs")
        return True

    print(f"\n{red('FAIL')} CHANGELOG.md still contains release verification stub text:")
    for index, line in enumerate(text.splitlines(), start=1):
        if stub in line:
            print(f"  CHANGELOG.md:{index}: {line}")
    return False

__all__ = ['_RESTORE_STATE_MARKERS', '_check_boundary', '_check_changelog_verified_stubs', '_is_restore_state_failure', 'restore_with_ignoring_failed_sources', 'run_mediafoundation_native_smoke', 'run_with_restore_fallback']
