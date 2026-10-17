"""Command-line validation and checked-in artifact updates."""

from __future__ import annotations

from .foundation import *
from .artifact_validation import (
    run_build,
    sha256,
    validate_unity_plugin_protobuf_match,
)
from .analyzer_validation import (
    run_analyzer_composition_tests,
    validate_analyzer_contracts,
)

def validate_or_update(
    update: bool,
    build_output_dir: Path,
    msbuild_props: list[str],
    target: str = "core",
) -> int:
    """Validate or update the checked-in analyzer assembly and its dependencies."""
    selected = TARGETS[target]
    project = selected.project
    checked_in_artifacts = (
        CHECKED_IN_ARTIFACTS
        if target == "core"
        else selected.checked_in_artifacts
    )
    if not project.exists():
        print(f"[FAIL] Source generator project missing: {project}", file=sys.stderr)
        return 1

    if not run_build(build_output_dir, msbuild_props, project):
        return 1

    built_artifacts = {}
    for name in checked_in_artifacts:
        built = build_output_dir / name
        if not built.exists():
            print(f"[FAIL] Release build did not produce {built}", file=sys.stderr)
            return 1
        built_artifacts[name] = built

    if update:
        if (selected.validate_runtime_protobuf
                and not validate_unity_plugin_protobuf_match(
                    built_artifacts["Google.Protobuf.dll"])):
            return 1
        with tempfile.TemporaryDirectory(
            prefix=".validate-source-generator-rollback-",
            dir=str(REPO_ROOT),
        ) as backup_dir_name:
            backup_dir = Path(backup_dir_name)
            backups: dict[str, Path | None] = {}
            try:
                for name, checked_in in checked_in_artifacts.items():
                    if checked_in.exists():
                        backup = backup_dir / name
                        backup.parent.mkdir(parents=True, exist_ok=True)
                        shutil.copy2(checked_in, backup)
                        backups[name] = backup
                    else:
                        backups[name] = None

                for name, checked_in in checked_in_artifacts.items():
                    shutil.copy2(built_artifacts[name], checked_in)
                    print(f"[PASS] Updated checked-in source generator artifact: {checked_in.relative_to(REPO_ROOT)}")
                    print(f"       sha256={sha256(checked_in)}")
            except (OSError, shutil.Error) as exc:
                rollback_errors: list[str] = []
                for name, checked_in in reversed(tuple(checked_in_artifacts.items())):
                    if name not in backups:
                        continue
                    backup = backups.get(name)
                    try:
                        if backup is None:
                            if checked_in.exists():
                                checked_in.unlink()
                        else:
                            backup.replace(checked_in)
                    except (OSError, shutil.Error) as rollback_exc:
                        rollback_errors.append(f"{name}: {rollback_exc}")
                print(
                    "[FAIL] Analyzer update rolled back after an artifact copy failed: "
                    f"{exc}",
                    file=sys.stderr,
                )
                if rollback_errors:
                    print(
                        "[FAIL] Analyzer update rollback encountered errors: "
                        + "; ".join(rollback_errors),
                        file=sys.stderr,
                    )
                return 1
        return 0

    for name, checked_in in checked_in_artifacts.items():
        if not checked_in.exists():
            print(f"[FAIL] Checked-in analyzer artifact missing: {checked_in.relative_to(REPO_ROOT)}", file=sys.stderr)
            return 1

        built_hash = sha256(built_artifacts[name])
        checked_hash = sha256(checked_in)
        if built_hash != checked_hash:
            print(f"[FAIL] Checked-in source generator artifact is stale: {name}", file=sys.stderr)
            print(
                f"       built:   {built_artifacts[name].relative_to(REPO_ROOT)} sha256={built_hash}",
                file=sys.stderr,
            )
            print(f"       checked: {checked_in.relative_to(REPO_ROOT)} sha256={checked_hash}", file=sys.stderr)
            print("       Run: python Scripts/package/validate_source_generator_dll.py --update", file=sys.stderr)
            return 1

    if (selected.validate_runtime_protobuf
            and not validate_unity_plugin_protobuf_match(
                checked_in_artifacts["Google.Protobuf.dll"])):
        return 1

    print(
        f"[PASS] Checked-in {selected.name} source generator artifacts "
        "match a fresh Release build."
    )
    for checked_in in checked_in_artifacts.values():
        print(f"       {checked_in.name}: sha256={sha256(checked_in)}")
    return 0


def main() -> int:
    """Parse command-line arguments and return a process exit code."""
    parser = argparse.ArgumentParser(description="Validate the checked-in source generator DLL.")
    parser.add_argument(
        "--target",
        choices=("all", *tuple(TARGETS)),
        default="all",
        help="Controlled analyzer package(s) to build and validate.",
    )
    parser.add_argument(
        "--update",
        action="store_true",
        help="Copy the fresh Release build over the checked-in analyzer DLL.",
    )
    parser.add_argument(
        "--build-output-dir",
        type=Path,
        default=None,
        help="Directory for the fresh Release build output.",
    )
    parser.add_argument(
        "--msbuild-prop",
        action="append",
        default=[],
        help="Additional MSBuild property argument to pass to dotnet build, such as -p:BaseOutputPath=...",
    )
    args = parser.parse_args()
    selected_names = (
        tuple(TARGETS)
        if args.target == "all"
        else (args.target,)
    )
    if not validate_analyzer_contracts(selected_names):
        return 1

    for name in selected_names:
        build_output_dir = (
            args.build_output_dir / name
            if args.build_output_dir is not None
            and args.target == "all"
            else args.build_output_dir
            if args.build_output_dir is not None
            else TARGETS[name].build_output_dir
        )
        result = validate_or_update(
            args.update,
            build_output_dir,
            args.msbuild_prop,
            name,
        )
        if result != 0:
            return result

    if args.target == "all" and not run_analyzer_composition_tests(
        args.msbuild_prop
    ):
        return 1
    return 0

__all__ = sorted(name for name in globals() if not name.startswith("__"))
