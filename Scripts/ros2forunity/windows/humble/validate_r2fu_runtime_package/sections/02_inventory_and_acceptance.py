def check_runtime_manifest(results: list[CheckResult], data: dict) -> None:
    """Validate the runtime support manifest."""
    if not data:
        return

    expected = {
        "schemaVersion": 1,
        "runtimeId": "r2fu-humble-win64",
        "packageName": PACKAGE_NAME,
        "packageVersion": "0.1.0-preview.1",
        "rosDistro": "humble",
        "unityVersion": "6000.3.14f1",
        "platform": "win64",
        "unityPlatform": "Windows",
        "architecture": "x86_64",
        "buildType": "standalone",
        "rmwImplementation": EXPECTED_RMW_IMPLEMENTATION,
        "artifactName": ARTIFACT_NAME,
        "inventoryFile": "RuntimeSupport/r2fu-humble-win64-runtime-inventory.json",
        "runtimeRoot": "Runtime/Ros2ForUnity",
        "pluginPath": "Runtime/Ros2ForUnity/Plugins/Windows/x86_64",
        "supportLevel": "Recommended",
        "activeRuntimePolicy": "one_runtime_package_per_project",
    }
    for key, value in expected.items():
        add(results, f"runtime manifest {key}", data.get(key) == value, f"expected {value!r}, got {data.get(key)!r}")
    acceptance = data.get("freshProjectAcceptance")
    add(
        results,
        "runtime manifest freshProjectAcceptance",
        fresh_project_acceptance_record_is_valid(data),
        f"freshProjectAcceptance={acceptance!r}",
    )
    distribution_level = data.get("distributionLevel")
    add(
        results,
        "runtime manifest distributionLevel",
        distribution_level in {"Prototype", "Supported", "Recommended"},
        f"distributionLevel={distribution_level!r}",
    )

    artifact_sha = data.get("artifactSha256")
    artifact_size = data.get("artifactSize")
    inventory_file_count = data.get("inventoryFileCount")
    add(
        results,
        "runtime manifest artifactSha256",
        is_sha256(artifact_sha),
        f"artifactSha256={artifact_sha!r}",
    )
    add(results, "runtime manifest artifactSize", isinstance(artifact_size, int) and artifact_size > 0, f"artifactSize={artifact_size!r}")
    add(
        results,
        "runtime manifest inventoryFileCount",
        isinstance(inventory_file_count, int) and inventory_file_count > 0,
        f"inventoryFileCount={inventory_file_count!r}",
    )

    source_basis = str(data.get("sourceBasis", ""))
    add(
        results,
        "runtime manifest source basis public",
        "Humble" in source_basis and "Phase" not in source_basis and "phase" not in source_basis,
        source_basis,
    )

    critical = data.get("criticalRuntimeFiles", [])
    add(
        results,
        "runtime manifest critical DLLs",
        isinstance(critical, list) and set(CRITICAL_DLLS).issubset(set(critical)),
        f"criticalRuntimeFiles={critical!r}",
    )

    patch = data.get("packagePathPatch", {})
    add(
        results,
        "runtime manifest package path patch",
        isinstance(patch, dict)
        and patch.get("modifiedFile") == "Runtime/Ros2ForUnity/Scripts/ROS2ForUnity.cs"
        and patch.get("keepsAssetFolderFallback") is True,
        f"packagePathPatch={patch!r}",
    )


def inventory_category_counts_match(categories: object, files: object) -> bool:
    """Return whether declared inventory categories exactly match file rows."""
    if not isinstance(categories, dict) or not isinstance(files, list):
        return False

    declared: dict[str, int] = {}
    for category, count in categories.items():
        if (
            not isinstance(category, str)
            or not category
            or not isinstance(count, int)
            or isinstance(count, bool)
            or count < 0
        ):
            return False
        declared[category] = count

    actual: dict[str, int] = {}
    for item in files:
        if not isinstance(item, dict):
            return False
        category = item.get("category")
        if not isinstance(category, str) or not category:
            return False
        actual[category] = actual.get(category, 0) + 1
    return actual == declared


def fresh_project_acceptance_status(manifest: dict) -> str:
    """Return the normalized fresh-project acceptance status."""
    acceptance = manifest.get("freshProjectAcceptance")
    if isinstance(acceptance, dict):
        return str(acceptance.get("status", ""))
    return str(acceptance or "")


def published_runtime_is_not_prototype(manifest: dict) -> bool:
    """Return whether the manifest is not marked as a Prototype release."""
    return str(manifest.get("distributionLevel", "")) != "Prototype"


def fresh_project_acceptance_passed(manifest: dict) -> bool:
    """Return whether fresh-project acceptance passed."""
    return fresh_project_acceptance_status(manifest) == "passed"



def _manifest_without_acceptance(commit_sha: str, manifest_relative: str) -> dict | None:
    """Read a committed runtime manifest without its mutable acceptance record."""
    result = subprocess.run(
        ["git", "show", f"{commit_sha}:{manifest_relative}"],
        cwd=ROOT,
        check=False,
        capture_output=True,
        text=True,
    )
    if result.returncode != 0:
        return None
    try:
        manifest = json.loads(result.stdout)
    except (TypeError, ValueError, json.JSONDecodeError):
        return None
    if not isinstance(manifest, dict):
        return None
    manifest.pop("freshProjectAcceptance", None)
    return manifest


def acceptance_commit_is_ancestor_and_package_unchanged(acceptance_sha: str, candidate_sha: str) -> bool:
    """Return whether an accepted commit is an ancestor with an unchanged runtime payload."""
    if re.fullmatch(r"[0-9a-fA-F]{40}", acceptance_sha or "") is None:
        return False
    if re.fullmatch(r"[0-9a-fA-F]{40}", candidate_sha or "") is None:
        return False
    try:
        ancestor = subprocess.run(
            ["git", "merge-base", "--is-ancestor", acceptance_sha, candidate_sha],
            cwd=ROOT,
            check=False,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
        if ancestor.returncode != 0:
            return False
        package_relative = PACKAGE.relative_to(ROOT).as_posix()
        manifest_relative = f"{package_relative}/RuntimeSupport/runtime-manifest.json"
        changed = subprocess.run(
            ["git", "diff", "--no-renames", "--name-only", acceptance_sha, candidate_sha, "--", package_relative],
            cwd=ROOT,
            check=False,
            capture_output=True,
            text=True,
        )
        if changed.returncode != 0:
            return False
        changed_paths = {line.replace("\\", "/") for line in changed.stdout.splitlines() if line}
        if any(path != manifest_relative for path in changed_paths):
            return False
        acceptance_manifest = _manifest_without_acceptance(acceptance_sha, manifest_relative)
        candidate_manifest = _manifest_without_acceptance(candidate_sha, manifest_relative)
        return acceptance_manifest is not None and acceptance_manifest == candidate_manifest
    except (OSError, ValueError):
        return False


def fresh_project_acceptance_binding_matches_manifest(manifest: dict) -> bool:
    """Return whether a passed acceptance record remains valid for this candidate."""
    acceptance = manifest.get("freshProjectAcceptance")
    if not isinstance(acceptance, dict) or acceptance.get("status") != "passed":
        return False

    for key in (
        "runtimeId",
        "packageName",
        "packageVersion",
        "rosDistro",
        "unityVersion",
        "artifactName",
        "artifactSha256",
        "artifactSize",
        "inventoryFileCount",
    ):
        if acceptance.get(key) != manifest.get(key):
            return False

    artifact_sha = acceptance.get("artifactSha256")
    if not isinstance(artifact_sha, str) or re.fullmatch(r"[0-9a-fA-F]{64}", artifact_sha) is None:
        return False
    artifact_size = acceptance.get("artifactSize")
    if not isinstance(artifact_size, int) or isinstance(artifact_size, bool) or artifact_size <= 0:
        return False
    inventory_file_count = acceptance.get("inventoryFileCount")
    if not isinstance(inventory_file_count, int) or isinstance(inventory_file_count, bool) or inventory_file_count <= 0:
        return False
    if manifest.get("unityVersion") != "6000.3.14f1" or acceptance.get("unityVersion") != manifest.get("unityVersion"):
        return False
    commit_sha = acceptance.get("commitSha")
    if not isinstance(commit_sha, str) or re.fullmatch(r"[0-9a-fA-F]{40}", commit_sha) is None:
        return False
    expected_commit_sha = os.environ.get("R2FU_EXPECTED_COMMIT_SHA")
    if expected_commit_sha and not acceptance_commit_is_ancestor_and_package_unchanged(
        commit_sha,
        expected_commit_sha,
    ):
        return False
    workflow_run_id = acceptance.get("workflowRunId")
    if isinstance(workflow_run_id, bool):
        return False
    try:
        if int(workflow_run_id) <= 0:
            return False
    except (TypeError, ValueError):
        return False
    return True


def fresh_project_acceptance_record_is_valid(manifest: dict) -> bool:
    """Return whether the manifest carries a deferred or bound acceptance record."""
    acceptance = manifest.get("freshProjectAcceptance")
    if acceptance == "deferred_to_install_acceptance":
        return True
    return fresh_project_acceptance_binding_matches_manifest(manifest)

def check_inventory(results: list[CheckResult], manifest: dict, release_gate: bool = False, skip_dll_hash: bool = False) -> None:
    """Validate the copied runtime inventory."""
    data = load_json(INVENTORY, results, "runtime inventory parses")
    if not data:
        return

    expected = {
        "schemaVersion": 1,
        "runtimeId": "r2fu-humble-win64",
        "artifactName": ARTIFACT_NAME,
        "rosDistro": "humble",
        "defaultRmwImplementation": EXPECTED_RMW_IMPLEMENTATION,
        "platform": "win64",
        "buildType": "standalone",
    }
    for key, value in expected.items():
        add(results, f"runtime inventory {key}", data.get(key) == value, f"expected {value!r}, got {data.get(key)!r}")

    add(
        results,
        "runtime inventory supported RMW implementations",
        data.get("supportedRmwImplementations") == [EXPECTED_RMW_IMPLEMENTATION],
        f"supportedRmwImplementations={data.get('supportedRmwImplementations')!r}",
    )
    add(
        results,
        "runtime inventory sha256 matches manifest",
        data.get("sha256") == manifest.get("artifactSha256"),
        f"inventory={data.get('sha256')!r}, manifest={manifest.get('artifactSha256')!r}",
    )
    add(
        results,
        "runtime inventory artifactSize matches manifest",
        data.get("artifactSize") == manifest.get("artifactSize"),
        f"inventory={data.get('artifactSize')!r}, manifest={manifest.get('artifactSize')!r}",
    )
    add(
        results,
        "runtime inventory fileCount matches manifest",
        data.get("fileCount") == manifest.get("inventoryFileCount"),
        f"inventory={data.get('fileCount')!r}, manifest={manifest.get('inventoryFileCount')!r}",
    )

    redistribution_status = str(data.get("redistributionStatus", ""))
    add(
        results,
        "runtime inventory redistributionStatus recorded",
        redistribution_status in {"candidate_not_published", "published"},
        f"redistributionStatus={redistribution_status!r}",
    )
    if release_gate:
        add(
            results,
            "release gate: runtime redistributionStatus is published",
            redistribution_status == "published",
            f"redistributionStatus={redistribution_status!r}",
        )
        fresh_status = fresh_project_acceptance_status(manifest)
        add(
            results,
            "release gate: fresh-project acceptance is passed",
            fresh_project_acceptance_passed(manifest),
            f"freshProjectAcceptance.status={fresh_status!r}",
        )
        expected_commit_sha = os.environ.get("R2FU_EXPECTED_COMMIT_SHA", "")
        add(
            results,
            "release gate: expected commit SHA is supplied",
            re.fullmatch(r"[0-9a-fA-F]{40}", expected_commit_sha) is not None,
            "R2FU_EXPECTED_COMMIT_SHA must identify the checked-out candidate commit",
        )
        add(
            results,
            "release gate: fresh-project acceptance binds unchanged candidate lineage",
            fresh_project_acceptance_binding_matches_manifest(manifest),
            "freshProjectAcceptance must bind artifact, package, commit, Unity version, and workflow run",
        )
        add(
            results,
            "release gate: published runtime is not Prototype",
            published_runtime_is_not_prototype(manifest),
            f"distributionLevel={manifest.get('distributionLevel')!r}",
        )

    categories = data.get("categoryCounts", {})
    native_library_count = int(categories.get("native_libraries", 0)) if isinstance(categories, dict) else 0
    add(
        results,
        "runtime inventory native library count",
        native_library_count >= 700,
        f"categoryCounts={categories!r}",
    )

    critical = data.get("knownCriticalFiles", [])
    present = {
        item.get("name")
        for item in critical
        if isinstance(item, dict) and item.get("present") is True
    }
    add(
        results,
        "runtime inventory critical files present",
        set(CRITICAL_DLLS).issubset(present),
        f"present={sorted(present)!r}",
    )

    files = data.get("files", [])
    add(
        results,
        "runtime inventory category counts match file entries",
        inventory_category_counts_match(categories, files),
        f"categoryCounts={categories!r}",
    )
    malformed: list[str] = []
    missing: list[str] = []
    mismatched: list[str] = []
    checked_dlls = 0
    should_hash_dlls = release_gate or not skip_dll_hash
    if isinstance(files, list):
        for item in files:
            if not isinstance(item, dict):
                malformed.append(repr(item))
                continue
            path_text = str(item.get("path", ""))
            if not path_text.lower().endswith(".dll"):
                continue
            checked_dlls += 1
            expected_hash = str(item.get("sha256", "")).lower()
            parts = PurePosixPath(path_text).parts
            if len(parts) < 2 or parts[0] != "Ros2ForUnity":
                malformed.append(path_text)
                continue
            package_path = RUNTIME_ROOT.joinpath(*parts[1:])
            if not package_path.is_file():
                missing.append(path_text)
                continue
            if should_hash_dlls and expected_hash and file_sha256(package_path) != expected_hash:
                mismatched.append(path_text)

    add(
        results,
        "runtime inventory DLL files exist on disk",
        isinstance(files, list) and checked_dlls >= native_library_count >= 700 and not malformed and not missing,
        f"checked_dlls={checked_dlls} malformed={malformed[:8]!r} missing={missing[:8]!r}",
    )
    add(
        results,
        "runtime inventory DLL hashes match disk",
        isinstance(files, list) and checked_dlls >= native_library_count >= 700 and (not should_hash_dlls or not mismatched),
        (
            "skipped by fast validation; use --release-gate for full DLL hash verification"
            if not should_hash_dlls
            else f"checked_dlls={checked_dlls} mismatched={mismatched[:8]!r}"
        ),
    )
