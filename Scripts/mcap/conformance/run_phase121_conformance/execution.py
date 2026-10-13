#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

from __future__ import annotations

from . import configuration as _configuration
from . import checkout_reporting as _checkout
from . import overlay as _overlay
from . import measurement as _measurement
from .configuration import *
from .checkout_reporting import *
from .overlay import *
from .measurement import *

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "run_phase121_conformance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

def run_conformance(release_blocking: bool, explicit_official_root: str | None) -> int:
    """Run the full Phase 121 measured conformance baseline."""

    _configuration.BUILD_ROOT.mkdir(parents=True, exist_ok=True)

    checkout_error = _checkout.prepare_official_checkout(explicit_official_root)
    if checkout_error:
        _checkout.write_skipped_report(checkout_error)
        return 1 if release_blocking else 0

    if not _configuration.OFFICIAL_CONFORMANCE.exists():
        _checkout.write_skipped_report(f"Official conformance harness was not found at {_configuration.OFFICIAL_CONFORMANCE}.")
        return 1 if release_blocking else 0

    if shutil.which("dotnet") is None:
        _checkout.write_skipped_report("dotnet was not found.")
        return 1 if release_blocking else 0

    build = _configuration.invoke_command_capture(["dotnet", "build", str(_configuration.PROJECT_PATH), "-c", "Release"])
    if build.exit_code != 0:
        print("C# conformance console build failed.", file=sys.stderr)
        print(build.stdout + build.stderr, file=sys.stderr)
        return build.exit_code

    manager = _measurement.package_manager_command()
    if shutil.which("node") is None or manager is None:
        _checkout.write_skipped_report("Node and Yarn are required for the official foxglove/mcap conformance harness.")
        return 1 if release_blocking else 0

    package_command, package_label = manager
    node_version = _configuration.invoke_command_capture(["node", "--version"]).stdout.strip()
    yarn_version = _configuration.invoke_command_capture(package_command + ["--version"]).stdout.strip()

    _overlay.copy_directory_without_local_state(_configuration.OFFICIAL_ROOT, _configuration.OVERLAY_ROOT)
    _overlay.add_csharp_runner_overlay()
    _overlay.prepare_data_directory()

    env = {"U2F_MCAP_CONFORMANCE_DLL": str(_measurement.resolve_conformance_dll_path())}

    install = _measurement.run_package_manager(package_command, ["install", "--immutable"], cwd=_configuration.OVERLAY_ROOT)
    if install.exit_code != 0:
        _checkout.write_skipped_report("Yarn dependencies are unavailable: " + _configuration.first_lines(install.stdout + install.stderr))
        return 1 if release_blocking else 0

    generate = _measurement.run_package_manager(
        package_command,
        ["workspace", "@foxglove/mcap-conformance", "generate-inputs", "--data-dir", str(_configuration.DATA_DIR)],
        cwd=_configuration.OVERLAY_ROOT,
    )
    if generate.exit_code != 0:
        _checkout.write_skipped_report("Official fixture generation failed: " + _configuration.first_lines(generate.stdout + generate.stderr))
        return 1 if release_blocking else 0

    runner_reports: list[dict[str, object]] = []
    runner_specs = [
        ("csharp-streamed-reader", "streamed-reader"),
        ("csharp-indexed-reader", "indexed-reader"),
        ("csharp-writer", "writer"),
    ]
    for runner_name, kind in runner_specs:
        result = _configuration.invoke_command_capture(
            package_command
            + [
                "workspace",
                "@foxglove/mcap-conformance",
                "run-tests",
                "--data-dir",
                str(_configuration.DATA_DIR),
                "--runner",
                runner_name,
            ],
            cwd=_configuration.OVERLAY_ROOT,
            env=env,
        )
        runner_reports.append(_measurement.measure_runner_output(runner_name, kind, result))

    failed = sum(int(report["failed"]) for report in runner_reports)
    verdict = "MEASURED BASELINE WITH FAILURES" if failed > 0 else "PASS WITH MEASURED BASELINE"
    _checkout.write_phase121_report(
        external_tooling_status="available",
        verdict=verdict,
        generated_variant_count=_measurement.count_generated_variants(),
        runners=runner_reports,
        tooling=[
            {"name": "dotnet-build", "status": "passed", "details": build.stdout.strip()},
            {"name": "fixture-generation", "status": "passed", "details": generate.stdout.strip()},
        ],
        node_version=node_version,
        package_manager_version=f"yarn {yarn_version}" if package_label == "corepack" else f"{package_label} {yarn_version}",
    )

    print(f"Phase 121 conformance report: {_configuration.REPORT_PATH}")
    if release_blocking and failed > 0:
        return 1
    return 0

__all__ = [name for name in globals() if not name.startswith("__")]
