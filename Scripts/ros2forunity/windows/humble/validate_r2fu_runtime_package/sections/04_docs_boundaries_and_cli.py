def check_public_docs(results: list[CheckResult], manifest: dict) -> None:
    """Validate public runtime docs avoid internal planning names."""
    combined = ""
    for path in PUBLIC_DOCS:
        combined += "\n" + path.read_text(encoding="utf-8", errors="replace") if path.exists() else ""
    hits = sorted({token for token in INTERNAL_TOKENS if token in combined})
    add(results, "runtime public docs avoid internal planning names", not hits, ", ".join(hits) if hits else "clean")

    readme = (PACKAGE / "README.md").read_text(encoding="utf-8", errors="replace") if (PACKAGE / "README.md").exists() else ""
    add(
        results,
        "README documents standalone and combined behavior",
        "runtime.humble.win64" in readme and "adapter" in readme and "combined Unity2Foxglove workflow" in readme,
        "README.md",
    )
    add(
        results,
        "README documents one-runtime policy",
        "Install only one" in readme and ("ros2forunity.runtime." in readme or "runtime packages" in readme),
        "README.md",
    )
    artifact_sha = str(manifest.get("artifactSha256", "")).strip()
    add(
        results,
        "README documents artifact SHA-256",
        is_sha256(artifact_sha) and artifact_sha in readme,
        "README.md",
    )
    notices = (PACKAGE / "THIRD_PARTY_NOTICES.md").read_text(encoding="utf-8", errors="replace") if (PACKAGE / "THIRD_PARTY_NOTICES.md").exists() else ""
    add(
        results,
        "THIRD_PARTY_NOTICES documents artifact SHA-256",
        is_sha256(artifact_sha) and artifact_sha in notices,
        "THIRD_PARTY_NOTICES.md",
    )
    add(
        results,
        "README documents WSL2 NAT topology limit",
        "WSL2 NAT" in readme and "diagnostic-only" in readme and "Windows Defender Firewall" in readme,
        "README.md",
    )
    add(
        results,
        "README documents OpenSSL 1.1.x artifact debt",
        "OpenSSL 1.1.x" in readme and "OpenSSL 3.x" in readme and "artifact refresh requirement" in readme,
        "README.md",
    )
    add(
        results,
        "THIRD_PARTY_NOTICES documents OpenSSL 1.1.x artifact debt",
        "OpenSSL 1.1.x" in notices and "OpenSSL 3.x" in notices and "release-hardening" in notices,
        "THIRD_PARTY_NOTICES.md",
    )


def check_package_boundaries(results: list[CheckResult]) -> None:
    """Validate the SDK and adapter package dependency boundaries."""
    sdk_package = load_json(CORE_PACKAGE / "package.json", results, "core package.json parses")
    adapter_package = load_json(ADAPTER_PACKAGE / "package.json", results, "adapter package.json parses")

    sdk_deps = json.dumps(sdk_package.get("dependencies", {}), sort_keys=True)
    adapter_deps = json.dumps(adapter_package.get("dependencies", {}), sort_keys=True)
    add(results, "core SDK does not depend on runtime package", PACKAGE_NAME not in sdk_deps, sdk_deps)
    add(results, "adapter does not hard-depend on runtime package", PACKAGE_NAME not in adapter_deps, adapter_deps)

    add(
        results,
        "core SDK runtime remains ROS2 For Unity free",
        not core_runtime_has_forbidden_tokens(),
        "core runtime scan",
    )


def core_runtime_has_forbidden_tokens() -> bool:
    """Return True when the core SDK Runtime contains R2FU-only tokens."""
    tokens = ("ROS2UnityComponent", "ros2forunity.runtime")
    for path in iter_files(CORE_PACKAGE / "Runtime"):
        text = path.read_text(encoding="utf-8", errors="ignore")
        if any(token in text for token in tokens):
            return True
    return False


def run_checks(release_gate: bool = False, skip_dll_hash: bool = False) -> list[CheckResult]:
    """Run all runtime package checks."""
    results: list[CheckResult] = []
    check_package_metadata(results)
    check_required_files(results)
    check_ros2cs_metadata_portability(results)
    check_message_package_floor(results)
    manifest = load_json(MANIFEST, results, "runtime manifest parses")
    check_runtime_manifest(results, manifest)
    check_inventory(results, manifest, release_gate=release_gate, skip_dll_hash=skip_dll_hash)
    check_runtime_files(results)
    check_package_path_patch(results)
    check_runtime_asmdef(results)
    check_runtime_source_patches(results)
    check_generator_alignment(results)
    check_public_docs(results, manifest)
    check_package_boundaries(results)
    return results


def print_results(results: list[CheckResult]) -> None:
    """Print validation results in a compact PASS/FAIL format."""
    for result in results:
        status = "PASS" if result.ok else "FAIL"
        detail = f": {result.detail}" if result.detail else ""
        print(f"[{status}] {result.name}{detail}")


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    """Parse validator command-line arguments."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--release-gate",
        action="store_true",
        help="Require redistributionStatus=published before release publication.",
    )
    parser.add_argument(
        "--fast",
        action="store_true",
        help="Skip per-DLL SHA-256 verification for faster routine validation; ignored by --release-gate.",
    )
    parser.add_argument(
        "--skip-dll-hash",
        action="store_true",
        help="Alias for --fast.",
    )
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    """Run validation and return a process exit code."""
    args = parse_args(argv)
    skip_dll_hash = (args.fast or args.skip_dll_hash) and not args.release_gate
    results = run_checks(release_gate=args.release_gate, skip_dll_hash=skip_dll_hash)
    print_results(results)
    failures = [result for result in results if not result.ok]
    if failures:
        print(f"\n{len(failures)} check(s) failed.", file=sys.stderr)
        return EXIT_FAILURE
    print(f"\nRuntime package validation passed: {len(results)} checks.")
    return EXIT_SUCCESS
