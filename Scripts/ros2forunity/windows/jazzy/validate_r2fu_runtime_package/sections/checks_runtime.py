from __future__ import annotations
import importlib as _importlib

_foundation = _importlib.import_module(f"{__package__}.checks_foundation")
annotations = _foundation.annotations
json = _foundation.json
argparse = _foundation.argparse
os = _foundation.os
hashlib = _foundation.hashlib
re = _foundation.re
sys = _foundation.sys
subprocess = _foundation.subprocess
ElementTree = _foundation.ElementTree
dataclass = _foundation.dataclass
Path = _foundation.Path
PurePosixPath = _foundation.PurePosixPath
Iterable = _foundation.Iterable
REPO_ROOT_PARENT_DEPTH = _foundation.REPO_ROOT_PARENT_DEPTH
EXIT_SUCCESS = _foundation.EXIT_SUCCESS
EXIT_FAILURE = _foundation.EXIT_FAILURE
ROOT = _foundation.ROOT
PACKAGE_NAME = _foundation.PACKAGE_NAME
PACKAGE = _foundation.PACKAGE
ADAPTER_PACKAGE = _foundation.ADAPTER_PACKAGE
CORE_PACKAGE = _foundation.CORE_PACKAGE
RUNTIME_ROOT = _foundation.RUNTIME_ROOT
PLUGIN_ROOT = _foundation.PLUGIN_ROOT
MANIFEST = _foundation.MANIFEST
INVENTORY = _foundation.INVENTORY
ARTIFACT_NAME = _foundation.ARTIFACT_NAME
EXPECTED_ARTIFACT_SHA256 = _foundation.EXPECTED_ARTIFACT_SHA256
EXPECTED_RMW_IMPLEMENTATION = _foundation.EXPECTED_RMW_IMPLEMENTATION
CRITICAL_DLLS = _foundation.CRITICAL_DLLS
PHASE161_ADDED_DLLS = _foundation.PHASE161_ADDED_DLLS
PHASE161_SUPPLEMENTAL_RUNTIME_DLLS = _foundation.PHASE161_SUPPLEMENTAL_RUNTIME_DLLS
V083_EXCLUDED_TEST_TYPESUPPORT_DLLS = _foundation.V083_EXCLUDED_TEST_TYPESUPPORT_DLLS
PHASE161_ASSET_CRITICAL_BASELINE = _foundation.PHASE161_ASSET_CRITICAL_BASELINE
MODIFICATIONS_COPYRIGHT = _foundation.MODIFICATIONS_COPYRIGHT
LOCAL_PATCH_MARKER = _foundation.LOCAL_PATCH_MARKER
LEAKY_UPSTREAM_EXAMPLES = _foundation.LEAKY_UPSTREAM_EXAMPLES
PATCHED_VENDOR_FILES = _foundation.PATCHED_VENDOR_FILES
PUBLIC_DOCS = _foundation.PUBLIC_DOCS
INTERNAL_TOKENS = _foundation.INTERNAL_TOKENS
CheckResult = _foundation.CheckResult
rel = _foundation.rel
add = _foundation.add
iter_files = _foundation.iter_files
load_json = _foundation.load_json
read_optional_text = _foundation.read_optional_text
file_sha256 = _foundation.file_sha256
is_sha256 = _foundation.is_sha256
unity_editor_using_is_guarded = _foundation.unity_editor_using_is_guarded
check_package_metadata = _foundation.check_package_metadata
check_required_files = _foundation.check_required_files
check_ros2cs_metadata_portability = _foundation.check_ros2cs_metadata_portability
check_runtime_manifest = _foundation.check_runtime_manifest
inventory_category_counts_match = _foundation.inventory_category_counts_match
fresh_project_acceptance_status = _foundation.fresh_project_acceptance_status
published_runtime_is_not_prototype = _foundation.published_runtime_is_not_prototype
fresh_project_acceptance_passed = _foundation.fresh_project_acceptance_passed
_manifest_without_acceptance = _foundation._manifest_without_acceptance
acceptance_commit_is_ancestor_and_package_unchanged = _foundation.acceptance_commit_is_ancestor_and_package_unchanged
fresh_project_acceptance_binding_matches_manifest = _foundation.fresh_project_acceptance_binding_matches_manifest
fresh_project_acceptance_record_is_valid = _foundation.fresh_project_acceptance_record_is_valid
check_inventory = _foundation.check_inventory
def check_runtime_files(results: list[CheckResult]) -> None:
    """Validate critical runtime files and package layout."""
    for dll in CRITICAL_DLLS:
        path = PLUGIN_ROOT / dll
        add(results, f"critical DLL present: {dll}", path.exists(), rel(path))

    for runtime_path in PHASE161_SUPPLEMENTAL_RUNTIME_DLLS:
        path = PACKAGE / "Runtime" / Path(runtime_path)
        add(results, f"supplemental runtime DLL present: {Path(runtime_path).name}", path.exists(), rel(path))

    dlls = list(PLUGIN_ROOT.glob("*.dll")) if PLUGIN_ROOT.exists() else []
    managed_dlls = list((RUNTIME_ROOT / "Plugins").glob("*.dll")) if (RUNTIME_ROOT / "Plugins").exists() else []
    plugin_dlls = sorted({*dlls, *managed_dlls}, key=lambda path: path.as_posix())
    add(results, "Windows x86_64 DLL payload", len(dlls) >= 900, f"dll_count={len(dlls)}")
    plugin_meta_failures = []
    for dll in plugin_dlls:
        meta = dll.with_name(dll.name + ".meta")
        text = read_optional_text(meta)
        if "PluginImporter:" not in text:
            plugin_meta_failures.append(rel(meta))
    add(
        results,
        "Runtime DLL metas use PluginImporter",
        len(dlls) >= 900 and not plugin_meta_failures,
        ", ".join(plugin_meta_failures[:8]),
    )
    add(results, "no root zip sidecar copied", not any(PACKAGE.glob("*.zip")) and not any(PACKAGE.glob("*.sha256")), rel(PACKAGE))

    copied_paths = [path.relative_to(PACKAGE).as_posix() for path in iter_files(PACKAGE)]
    sample_hits = [path for path in copied_paths if "Phase110Ros2ForUnity" in path or "External Adapter" in path]
    add(results, "runtime package does not duplicate adapter samples", not sample_hits, ", ".join(sample_hits[:8]))

    unexpected_platforms = [
        path
        for path in copied_paths
        if path.startswith("Runtime/Ros2ForUnity/Plugins/")
        and ("/Linux/" in path or "/Mac" in path or "/macOS/" in path)
    ]
    add(results, "runtime plugin payload limited to Windows", not unexpected_platforms, ", ".join(unexpected_platforms[:8]))

    scripts = RUNTIME_ROOT / "Scripts"
    leaky_examples = [name for name in LEAKY_UPSTREAM_EXAMPLES if (scripts / name).exists()]
    add(results, "leaky upstream examples pruned", not leaky_examples, ", ".join(leaky_examples))


def check_package_path_patch(results: list[CheckResult]) -> None:
    """Validate the ROS2ForUnity.cs package path patch."""
    source = RUNTIME_ROOT / "Scripts" / "ROS2ForUnity.cs"
    text = source.read_text(encoding="utf-8", errors="replace") if source.exists() else ""
    required = [
        "Unity2Foxglove package path support",
        PACKAGE_NAME,
        "PackageInfo.FindForAssetPath",
        "resolvedPath",
        "unity2FoxgloveRuntimePackageAssetPath",
        "SetProcessEnvironmentPathVariable(",
        'Path.Combine(',
        '"Packages"',
        '"Runtime"',
        "Directory.Exists(packagePath)",
        "return assetPath;",
    ]
    for token in required:
        add(results, f"ROS2ForUnity.cs contains {token}", token in text, token)
    add(
        results,
        "UnityEditor using guarded",
        unity_editor_using_is_guarded(text),
        "ROS2ForUnity.cs",
    )
    add(
        results,
        "PackageManager lookup guarded",
        re.search(
            r"#if\s+UNITY_EDITOR\s+UnityEditor\.PackageManager\.PackageInfo\s+\w+\s*="
            r"\s*UnityEditor\.PackageManager\.PackageInfo\.FindForAssetPath",
            text,
            re.S,
        )
        is not None,
        "ROS2ForUnity.cs",
    )
    add(
        results,
        "standalone PATH update reaches native environment",
        "Environment.SetEnvironmentVariable(GetEnvPathVariableName()," not in text,
        "ROS2ForUnity.cs",
    )


def check_runtime_asmdef(results: list[CheckResult]) -> None:
    """Validate the runtime assembly definition is safe for Editor and Player."""
    path = RUNTIME_ROOT / "Scripts" / "Unity2Foxglove.Ros2ForUnity.Runtime.JazzyWin64.asmdef"
    data = load_json(path, results, "runtime asmdef parses")
    add(results, "runtime asmdef name", data.get("name") == "Unity2Foxglove.Ros2ForUnity.Runtime", f"name={data.get('name')!r}")
    add(
        results,
        "runtime asmdef targets Windows runtime and editor",
        data.get("includePlatforms") == ["Editor", "WindowsStandalone64"],
        f"includePlatforms={data.get('includePlatforms')!r}",
    )
    add(results, "runtime asmdef auto-referenced", data.get("autoReferenced") is True, f"autoReferenced={data.get('autoReferenced')!r}")
    add(
        results,
        "runtime asmdef has Windows define gate",
        data.get("defineConstraints") == ["UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN"],
        f"defineConstraints={data.get('defineConstraints')!r}",
    )


def check_runtime_source_patches(results: list[CheckResult]) -> None:
    """Validate local lifecycle, time-source, and attribution patches on vendored R2FU sources."""
    scripts = RUNTIME_ROOT / "Scripts"
    for relative in PATCHED_VENDOR_FILES:
        path = scripts / relative
        text = path.read_text(encoding="utf-8", errors="replace") if path.exists() else ""
        add(results, f"patched vendored file exists: {relative}", path.exists(), rel(path))
        add(results, f"patched vendored attribution: {relative}", MODIFICATIONS_COPYRIGHT in text, relative)

    node = read_optional_text(scripts / "ROS2Node.cs")
    add(results, "ROS2Node implements IDisposable", "class ROS2Node : IDisposable" in node and "public void Dispose()" in node, "ROS2Node.cs")
    add(results, "ROS2Node avoids finalizer native cleanup", "~ROS2Node" not in node, "ROS2Node.cs")
    add(results, "ROS2Node removed UnityEditor using", "using UnityEditor;" not in node, "ROS2Node.cs")

    component = read_optional_text(scripts / "ROS2UnityComponent.cs")
    component_join = "threadToJoin.Join(1000)" in component or "threadToJoin.Join(TimeSpan.FromSeconds(2))" in component
    for token in (
        LOCAL_PATCH_MARKER,
        "private volatile bool quitting",
        "OnDestroy()",
        "OnApplicationQuit()",
        "node.TryDispose()",
        "StopExecutor()",
        "TryDetachRuntimeState",
        "QuarantineNodesAfterExecutorTimeout",
        "ReferenceEquals(executorThread, threadToJoin)",
        "nodesSnapshot.AddRange(ros2csNodes)",
        "Ros2cs.SpinOnce(nodesSnapshot, spinTimeout)",
        "runtimeShutdownRequested",
        "ROS2ForUnity.PrewarmUnityMainThreadPaths();",
        "throw new ObjectDisposedException(nameof(ROS2UnityComponent))",
        "StopAllExecutorsForRosShutdown()",
        "MarkRuntimeShutdown()",
        "private int shutdownInProgress = 0",
        "Interlocked.CompareExchange(ref shutdownInProgress, 1, 0)",
        "Volatile.Write(ref shutdownInProgress, 0)",
        "MarkRuntimeShutdownPendingExecutor()",
    ):
        add(results, f"ROS2UnityComponent lifecycle token: {token}", token in component, token)
    add(
        results,
        "ROS2UnityComponent executor thread",
        "private Thread spinThread" in component or "private Thread executorThread" in component,
        "ROS2UnityComponent.cs",
    )
    add(results, "ROS2UnityComponent bounded join", component_join, "ROS2UnityComponent.cs")
    add(results, "ROS2UnityComponent does not shutdown on ordinary disable", "OnDisable()" not in component, "ROS2UnityComponent.cs")

    core = read_optional_text(scripts / "ROS2UnityCore.cs")
    core_join = "threadToJoin.Join(1000)" in core or "threadToJoin.Join(TimeSpan.FromSeconds(2))" in core
    for token in (
        LOCAL_PATCH_MARKER,
        "IDisposable",
        "private volatile bool quitting",
        "public void Dispose()",
        "StopExecutor()",
        "TryDetachRuntimeState",
        "QuarantineNodesAfterExecutorTimeout",
        "ReferenceEquals(executorThread, threadToJoin)",
        "nodesSnapshot.AddRange(ros2csNodes)",
        "Ros2cs.SpinOnce(nodesSnapshot, spinTimeout)",
        "ROS2ForUnity.PrewarmUnityMainThreadPaths();",
    ):
        add(results, f"ROS2UnityCore lifecycle token: {token}", token in core, token)
    add(
        results,
        "ROS2UnityCore executor thread",
        "private Thread spinThread" in core or "private Thread executorThread" in core,
        "ROS2UnityCore.cs",
    )
    add(results, "ROS2UnityCore bounded join", core_join, "ROS2UnityCore.cs")

    runtime = read_optional_text(scripts / "ROS2ForUnity.cs")
    old_tokens = ("ownerCount", "ownsLifecycle", "lifecycleGate", "UnregisterCallbacks()", "editorCallbacksRegistered")
    current_tokens = ("referenceCount", "ownsReference", "initMutex", "ShutdownShared()", "editorHandlersRegistered")
    old_lifecycle = all(token in runtime for token in old_tokens)
    current_lifecycle = all(token in runtime for token in current_tokens)
    mixed_partial = (
        old_lifecycle
        and any(token in runtime for token in ("referenceCount", "ownsReference", "editorHandlersRegistered"))
        and not current_lifecycle
    )
    add(
        results,
        "ROS2ForUnity deterministic lifecycle",
        (old_lifecycle or current_lifecycle) and not mixed_partial,
        f"old_lifecycle={old_lifecycle} current_lifecycle={current_lifecycle} mixed_partial={mixed_partial}",
    )
    add(results, "ROS2ForUnity avoids finalizer shutdown", "~ROS2ForUnity" not in runtime, "ROS2ForUnity.cs")
    add(
        results,
        "ROS2ForUnity uses non-obsolete ros2cs logger callback API",
        "Ros2csLogger.SetCallback" in runtime and "Ros2csLogger.setCallback" not in runtime,
        "ROS2ForUnity.cs",
    )
    add(
        results,
        "ROS2ForUnity enforces expected RMW",
        "expectedRmwImplementation" in runtime
        and "ValidateRmwImplementation" in runtime
        and EXPECTED_RMW_IMPLEMENTATION in runtime,
        "ROS2ForUnity.cs",
    )
    env_tokens = (
        "SetProcessEnvironmentVariable",
        "_wputenv_s",
        "SetStandalonePrefixPath",
        "AMENT_PREFIX_PATH",
        "SetStandaloneRmwImplementation",
        "RMW_IMPLEMENTATION",
        "SetEnvPathVariable();",
    )
    add(
        results,
        "ROS2ForUnity configures standalone native environment before init",
        all(token in runtime for token in env_tokens)
        and "sourcedRosDistroBeforeStandalonePatch" in runtime
        and "SetStandaloneRosDistro" in runtime
        and "WarnIfStandaloneRosDistroOverride" in runtime
        and "private static void FailIntegrity" in runtime
        and "public static void PrewarmUnityMainThreadPaths()" in runtime
        and "const int ROS_BAD_RMW_CODE = 36;" in runtime
        and "Unable to suppress Ros2cs finalizer before shutdown" in runtime
        and "Debug.LogError(\"Unable to suppress Ros2cs finalizer before shutdown" in runtime
        and "CheckIntegrity(standaloneBuild ? null : sourcedRosDistroBeforeStandalonePatch)" in runtime
        and "ROS2 version in standalone process environment does not match this runtime package" not in runtime
        and "ROS2UnityComponent.StopAllExecutorsForRosShutdown()" in runtime
        and runtime.find("SetStandalonePrefixPath();") < runtime.find("Ros2cs.Init()")
        and runtime.find("SetStandaloneRmwImplementation();") < runtime.find("Ros2cs.Init()")
        and runtime.find("SetStandaloneRosDistro(currentRos2Version);") < runtime.find("Ros2cs.Init()")
        and runtime.find("SetEnvPathVariable();") < runtime.find("Ros2cs.Init()"),
        "ROS2ForUnity.cs",
    )

    dotnet_time = read_optional_text(scripts / "Time" / "DotnetTimeSource.cs")
    add(
        results,
        "DotnetTimeSource converts Stopwatch duration to seconds",
        ("Stopwatch.Frequency" in dotnet_time and "ElapsedTicks" in dotnet_time)
        or "stopwatch.Elapsed.TotalSeconds" in dotnet_time,
        "DotnetTimeSource.cs",
    )
    add(
        results,
        "DotnetTimeSource clamps backward wall-clock corrections",
        "lastEmittedSeconds" in dotnet_time
        and "wall-clock corrections cannot move time backward" in dotnet_time,
        "DotnetTimeSource.cs",
    )

    for relative in ("Time/ROS2TimeSource.cs", "Time/ROS2ScalableTimeSource.cs"):
        source = read_optional_text(scripts / relative)
        add(
            results,
            f"{relative} implements bool ITimeSource.GetTime",
            "public bool GetTime(out int seconds, out uint nanoseconds)" in source
            and "public void GetTime(out int seconds, out uint nanoseconds)" not in source,
            relative,
        )
        add(
            results,
            f"{relative} reports unavailable ROS time",
            "return false;" in source and "return true;" in source,
            relative,
        )
        add(
            results,
            f"{relative} bool contract patch is marked",
            "bool-returning ITimeSource contract" in source,
            relative,
        )
        add(
            results,
            f"{relative} locks lazy ROS clock access",
            "private readonly object clockMutex = new object();" in source
            and "lock (clockMutex)" in source,
            relative,
        )

    scalable_time = read_optional_text(scripts / "Time" / "ROS2ScalableTimeSource.cs")
    add(
        results,
        "ROS2ScalableTimeSource locks Unity time snapshot state",
        "private readonly object mutex = new object();" in scalable_time
        and "lock (mutex)" in scalable_time
        and "UpdateUnityTimeSnapshot()" in scalable_time,
        "Time/ROS2ScalableTimeSource.cs",
    )
    add(
        results,
        "ROS2ScalableTimeSource locks lazy ROS clock creation",
        "private readonly object clockMutex = new object();" in scalable_time
        and "lock (clockMutex)" in scalable_time
        and (
            "private double GetRosNowSeconds()" in scalable_time
            or "private bool TryGetRosNowSeconds(" in scalable_time
        ),
        "Time/ROS2ScalableTimeSource.cs",
    )
    add(
        results,
        "ROS2ScalableTimeSource uses ManagedThreadId main-thread guard",
        "private int mainThreadId;" in scalable_time
        and "Thread.CurrentThread.ManagedThreadId" in scalable_time
        and "Thread mainThread" not in scalable_time,
        "Time/ROS2ScalableTimeSource.cs",
    )
    add(
        results,
        "ROS2ScalableTimeSource deduplicates timeScale change log",
        "private bool timeScaleChangeLogged" in scalable_time
        and "if (!timeScaleChangeLogged)" in scalable_time,
        "Time/ROS2ScalableTimeSource.cs",
    )

    time_utils = read_optional_text(scripts / "Time" / "TimeUtils.cs")
    add(
        results,
        "TimeUtils normalizes nanoseconds",
        "Math.Floor(secondsIn)" in time_utils
        and (
            "normalizedNanoseconds < 0" in time_utils
            or "wholeNanoseconds >= 1000000000" in time_utils
            or "wholeNanoseconds >= NanosecondsPerSecond" in time_utils
        ),
        "TimeUtils.cs",
    )
    add(results, "TimeUtils does not cast modulo directly", "(uint)(nanosec % 1e9)" not in time_utils and "(uint)(nanosec % 1000000000)" not in time_utils, "TimeUtils.cs")
    add(
        results,
        "TimeUtils rejects NaN and infinity",
        "Double.IsNaN(secondsIn)" in time_utils and "Double.IsInfinity(secondsIn)" in time_utils,
        "TimeUtils.cs",
    )
    add(
        results,
        "TimeUtils guards Int32 seconds range",
        "Int32.MinValue" in time_utils
        and "Int32.MaxValue" in time_utils
        and "ROS time seconds exceed Int32 range" in time_utils,
        "TimeUtils.cs",
    )

    sensor = read_optional_text(scripts / "Sensor.cs")
    add(results, "Sensor uses short-circuit publisher guard", "publisherOwnership != null && publishing" in sensor, "Sensor.cs")
    readings_guard_index = sensor.find("if (readings != null)")
    readings_deref_index = sensor.find("readings.SetHeaderFrame")
    sensor_null_guard = (
        (readings_guard_index >= 0 and readings_deref_index >= 0 and readings_guard_index < readings_deref_index)
        or ("if (acquiredReading == null)" in sensor and "acquiredReading.SetHeaderFrame" in sensor)
    )
    add(results, "Sensor checks readings before dereference", sensor_null_guard, "Sensor.cs")
    add(results, "Sensor unregisters executable action", "UnregisterExecutable" in sensor, "Sensor.cs")
    add(
        results,
        "Sensor publishes outside its readings lock with deferred retirement",
        "publisherToUse.Publish(readingToPublish);" in sensor
        and "ownershipToRetire.Retired = true;" in sensor
        and "CompletePublisherCall(ownershipToUse);" in sensor,
        "Sensor.cs",
    )


def check_generator_alignment(results: list[CheckResult]) -> None:
    """Validate the generator knows about the lifecycle-patched package shape."""
    generator_path = ROOT / "Scripts" / "ros2forunity" / "windows" / "jazzy" / "build_r2fu_runtime_package.py"
    try:
        generator = generator_path.read_text(encoding="utf-8", errors="replace")
    except OSError as exc:
        add(results, "runtime package generator script readable", False, f"{rel(generator_path)}: {exc}")
        return

    add(results, "runtime package generator script readable", True, rel(generator_path))
    required = (
        "collect_local_patch_overlays",
        "apply_local_patch_overlays",
        "collect_meta_overlays",
        "apply_meta_overlays",
        "LOCAL_PATCH_OVERLAY_FILES",
        "patch_ros_time_source_contract",
        "LEAKY_UPSTREAM_EXAMPLES",
        "runtime_asmdef",
        "make_writable",
        "windows_long_path",
        "PackageInfo.FindForAssetPath",
        "UNITY_EDITOR",
        "Application.streamingAssetsPath",
    )
    for token in required:
        add(results, f"runtime package generator token: {token}", token in generator, token)


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
        "runtime.jazzy.win64" in readme and "adapter" in readme and "combined Unity2Foxglove workflow" in readme,
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




__all__ = ['ADAPTER_PACKAGE', 'ARTIFACT_NAME', 'CORE_PACKAGE', 'CRITICAL_DLLS', 'CheckResult', 'EXIT_FAILURE', 'EXIT_SUCCESS', 'EXPECTED_ARTIFACT_SHA256', 'EXPECTED_RMW_IMPLEMENTATION', 'ElementTree', 'INTERNAL_TOKENS', 'INVENTORY', 'Iterable', 'LEAKY_UPSTREAM_EXAMPLES', 'LOCAL_PATCH_MARKER', 'MANIFEST', 'MODIFICATIONS_COPYRIGHT', 'PACKAGE', 'PACKAGE_NAME', 'PATCHED_VENDOR_FILES', 'PHASE161_ADDED_DLLS', 'PHASE161_ASSET_CRITICAL_BASELINE', 'PHASE161_SUPPLEMENTAL_RUNTIME_DLLS', 'PLUGIN_ROOT', 'PUBLIC_DOCS', 'Path', 'PurePosixPath', 'REPO_ROOT_PARENT_DEPTH', 'ROOT', 'RUNTIME_ROOT', 'V083_EXCLUDED_TEST_TYPESUPPORT_DLLS', '_manifest_without_acceptance', 'acceptance_commit_is_ancestor_and_package_unchanged', 'add', 'annotations', 'argparse', 'check_generator_alignment', 'check_inventory', 'check_package_boundaries', 'check_package_metadata', 'check_package_path_patch', 'check_public_docs', 'check_required_files', 'check_ros2cs_metadata_portability', 'check_runtime_asmdef', 'check_runtime_files', 'check_runtime_manifest', 'check_runtime_source_patches', 'core_runtime_has_forbidden_tokens', 'dataclass', 'file_sha256', 'fresh_project_acceptance_binding_matches_manifest', 'fresh_project_acceptance_passed', 'fresh_project_acceptance_record_is_valid', 'fresh_project_acceptance_status', 'hashlib', 'inventory_category_counts_match', 'is_sha256', 'iter_files', 'json', 'load_json', 'main', 'os', 'parse_args', 'print_results', 'published_runtime_is_not_prototype', 're', 'read_optional_text', 'rel', 'run_checks', 'subprocess', 'sys', 'unity_editor_using_is_guarded']
