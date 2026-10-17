"""Build and artifact checks for source-generator packages."""

from __future__ import annotations

from .foundation import *
from .foundation import _CORE_TYPE_SHAPE_EXTENSION

def sha256(path: Path) -> str:
    """Return the SHA-256 hex digest for a file."""
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _shared_core_type_shape(text: str) -> str | None:
    """Remove the one intentional core-only schema-name helper."""
    if text.count(_CORE_TYPE_SHAPE_EXTENSION) != 1:
        return None
    shared, _extension = text.split(
        _CORE_TYPE_SHAPE_EXTENSION,
        1,
    )
    return shared.rstrip() + "\n}\n"


def validate_shared_source_parity() -> bool:
    """Fail when independently packaged shared analyzer semantics drift."""
    failures: list[str] = []
    for label, paths in EXACT_SHARED_SOURCE_GROUPS:
        contents: list[str] = []
        for path in paths:
            try:
                contents.append(path.read_text(encoding="utf-8"))
            except (OSError, UnicodeError) as exc:
                failures.append(f"{label}: cannot read {path}: {exc}")
        if len(contents) == len(paths) and any(
            content != contents[0]
            for content in contents[1:]
        ):
            failures.append(f"{label}: packaged copies differ")

    try:
        provider = PROVIDER_TYPE_SHAPE.read_text(encoding="utf-8")
        core = CORE_TYPE_SHAPE.read_text(encoding="utf-8")
    except (OSError, UnicodeError) as exc:
        failures.append(f"type shape: cannot read shared sources: {exc}")
    else:
        normalized_core = _shared_core_type_shape(core)
        if normalized_core is None:
            failures.append(
                "type shape: core-only extension boundary differs"
            )
        elif normalized_core != provider:
            failures.append(
                "type shape: core and Provider shared semantics differ"
            )

    if failures:
        for failure in failures:
            print(
                f"[FAIL] Analyzer shared source parity: {failure}",
                file=sys.stderr,
            )
        return False
    print("[PASS] Analyzer shared semantic sources are in parity.")
    return True


def run_build(
    build_output_dir: Path = BUILD_OUTPUT_DIR,
    msbuild_props: list[str] | None = None,
    project: Path = PROJECT,
) -> bool:
    """Build the source generator project in Release mode."""
    msbuild_props = msbuild_props or []
    command = [
        "dotnet",
        "build",
        str(project),
        *msbuild_props,
        "-c",
        "Release",
        "--no-incremental",
        "-o",
        str(build_output_dir),
        "-v:minimal",
    ]

    def output_fingerprint(path: Path) -> tuple[int, int] | None:
        """Identify one regular build output by size and modification time."""
        try:
            if not path.is_file():
                return None
            info = path.stat()
        except OSError:
            return None
        return (info.st_size, info.st_mtime_ns)

    expected_paths: tuple[Path, ...] = ()
    for candidate in TARGETS.values():
        try:
            same_project = candidate.project.resolve() == project.resolve()
        except OSError:
            same_project = candidate.project == project
        if same_project:
            expected_paths = tuple(
                build_output_dir / name
                for name in candidate.checked_in_artifacts
            )
            break
    if not expected_paths:
        expected_paths = tuple(
            build_output_dir / name
            for name in CHECKED_IN_ARTIFACTS
        )
    output_before = {
        path: output_fingerprint(path)
        for path in expected_paths
    }

    try:
        subprocess.run(command, cwd=REPO_ROOT, check=True)
    except subprocess.CalledProcessError as exc:
        print(f"[FAIL] Source generator Release build failed with exit code {exc.returncode}.", file=sys.stderr)
        print(f"       command: {' '.join(command)}", file=sys.stderr)
        return False
    except FileNotFoundError:
        print(
            "[FAIL] Source generator Release build failed: "
            "dotnet executable is unavailable.",
            file=sys.stderr,
        )
        return False

    primary_output = next(
        (path for path in expected_paths if "SourceGenerator" in path.name),
        expected_paths[0],
    )
    for path in expected_paths:
        output_after = output_fingerprint(path)
        if output_after is None:
            print(
                "[FAIL] Source generator Release build succeeded but did not "
                f"produce a regular output at {path}.",
                file=sys.stderr,
            )
            return False
        if path == primary_output and output_after == output_before[path]:
            print(
                "[FAIL] Source generator Release build left the pre-existing "
                f"output at {path} unchanged; it is not this build's output.",
                file=sys.stderr,
            )
            return False
    return True


def validate_unity_plugin_protobuf_match(analyzer_dependency: Path) -> bool:
    """Ensure the Unity runtime plug-in matches the supplied Protobuf dependency exactly."""
    if not UNITY_PLUGIN_GOOGLE_PROTOBUF.exists():
        print(
            "[FAIL] Unity runtime Google.Protobuf plug-in is missing: "
            f"{UNITY_PLUGIN_GOOGLE_PROTOBUF}",
            file=sys.stderr,
        )
        return False

    if sha256(analyzer_dependency) != sha256(UNITY_PLUGIN_GOOGLE_PROTOBUF):
        print(
            "[FAIL] Unity runtime Google.Protobuf plug-in differs from checked-in analyzer dependency.",
            file=sys.stderr,
        )
        print(f"       analyzer: {analyzer_dependency} sha256={sha256(analyzer_dependency)}", file=sys.stderr)
        print(
            f"       runtime:  {UNITY_PLUGIN_GOOGLE_PROTOBUF} "
            f"sha256={sha256(UNITY_PLUGIN_GOOGLE_PROTOBUF)}",
            file=sys.stderr,
        )
        return False

    return True



__all__ = sorted(name for name in globals() if not name.startswith("__"))
