from __future__ import annotations
from .unity_contract_and_binding import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase186_bridge_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE


def install_unity_run_binding(
    project: pathlib.Path, config: Mapping[str, Any]
) -> InstalledUnityRunBinding:
    """Install one exact ignored source without overwriting foreign content."""

    root = pathlib.Path(project).resolve()
    expected_project = pathlib.Path(str(config.get("projectPath", ""))).resolve()
    if root != expected_project:
        raise AcceptanceFailure(
            "FAIL_PREFLIGHT", "Unity binding project differs from run authority"
        )
    target = root / _UNITY_BINDING_RELATIVE_PATH
    source = render_unity_run_binding(config)
    encoded = source.encode("utf-8")
    digest = hashlib.sha256(encoded).hexdigest()
    if target.exists():
        try:
            existing = target.read_bytes()
        except OSError as exc:
            raise AcceptanceFailure(
                "FAIL_PREFLIGHT", "existing Unity run binding cannot be read"
            ) from exc
        if existing != encoded:
            raise AcceptanceFailure(
                "FAIL_PREFLIGHT", "refusing to overwrite a foreign Unity run binding"
            )
        return InstalledUnityRunBinding(target, digest)

    target.parent.mkdir(parents=True, exist_ok=True)
    temporary: pathlib.Path | None = None
    try:
        with tempfile.NamedTemporaryFile(
            mode="wb",
            dir=target.parent,
            prefix=target.name + ".",
            suffix=".tmp",
            delete=False,
        ) as stream:
            stream.write(encoded)
            temporary = pathlib.Path(stream.name)
        os.replace(temporary, target)
    finally:
        if temporary is not None and temporary.exists():
            temporary.unlink()
    return InstalledUnityRunBinding(target, digest)


def cleanup_unity_run_binding(installed: InstalledUnityRunBinding) -> None:
    """Remove only the exact source (and Unity-owned meta) installed above."""

    target = pathlib.Path(installed.path)
    if not target.is_file() or sha256_file(target) != installed.sha256:
        raise AcceptanceFailure(
            "FAIL_CLEANUP", "Unity run binding changed after installation"
        )
    meta = pathlib.Path(str(target) + ".meta")
    if meta.exists():
        try:
            meta_text = meta.read_text(encoding="utf-8")
        except (OSError, UnicodeDecodeError) as exc:
            raise AcceptanceFailure(
                "FAIL_CLEANUP", "Unity run binding meta cannot be verified"
            ) from exc
        if (
            len(meta_text) > 4096
            or "fileFormatVersion: 2" not in meta_text
            or re.search(r"(?m)^guid: [0-9a-f]{32}$", meta_text) is None
        ):
            raise AcceptanceFailure(
                "FAIL_CLEANUP", "Unity run binding meta is foreign or malformed"
            )
        meta.unlink()
    target.unlink()


def validate_static_authority(repository: pathlib.Path) -> dict[str, Any]:
    """Lock tracked protocol, fixture, harness, and analyzer inputs."""

    root = pathlib.Path(repository)
    fixture = (
        root
        / "Tools"
        / "ros2_bridge"
        / "unity2foxglove_ros2_bridge"
        / "test"
        / "fixtures"
        / "u2r2_protocol_vectors.json"
    )
    bridge_source = (
        root
        / "Tools"
        / "ros2_bridge"
        / "unity2foxglove_ros2_bridge"
        / "src"
        / "unity2foxglove_ros2_bridge.cpp"
    )
    analyzer = (
        root
        / "Packages"
        / "dev.unity2foxglove.sdk"
        / "Editor"
        / "SourceGenerators"
        / "analyzers"
        / "dotnet"
        / "cs"
        / "FoxgloveLogSourceGenerator.dll"
    )
    for label, path in (
        ("U2R2 fixture", fixture),
        ("Bridge source", bridge_source),
        ("FoxRun analyzer", analyzer),
    ):
        if not path.is_file():
            raise LivePrerequisiteMissing(
                "NOT_RUN_TRACKED_AUTHORITY", f"{label} is absent: {path}"
            )
    return {
        "fixturePath": str(fixture.resolve()),
        "fixtureSha256": sha256_file(fixture),
        "bridgeSourcePath": str(bridge_source.resolve()),
        "bridgeSourceSha256": sha256_file(bridge_source),
        "analyzerPath": str(analyzer.resolve()),
        "analyzerSha256": sha256_file(analyzer),
        "interfaceType": protocol.INTERFACE_TYPE,
        "interfaceDigest": protocol.INTERFACE_DIGEST,
    }


def find_current_manual_marker(
    lines: Sequence[str],
    *,
    case_id: str,
    run_id: str,
    token: str,
    head: str,
) -> str:
    """Return only the exact current-run Unity completion marker."""

    scanned = 0
    for line in reversed(tuple(lines)):
        scanned += len(line.encode("utf-8", errors="replace"))
        if scanned > MAX_RESCUE_LOG_BYTES:
            break
        candidate = line.strip()
        if not candidate.startswith(protocol.MANUAL_COMPLETE_PREFIX + " "):
            continue
        try:
            protocol.parse_manual_completion_marker(
                candidate,
                case_id=case_id,
                run_id=run_id,
                token=token,
                head=head,
            )
        except protocol.ProtocolFailure:
            continue
        return candidate
    raise AcceptanceFailure(
        "FAIL_TERMINAL", "no exact current-run manual completion marker was found"
    )


def validate_cleanup_evidence(value: Mapping[str, Any]) -> None:
    """Require all owned resources to be absent after teardown."""

    expected = {
        "complete",
        "cleanupErrors",
        "residualProcesses",
        "residualPorts",
        "residualOverlays",
        "residualTemporaryProjects",
    }
    if not isinstance(value, Mapping) or set(value) != expected:
        raise AcceptanceFailure("FAIL_CLEANUP", "cleanup evidence keys differ")
    if value["complete"] is not True:
        raise AcceptanceFailure("FAIL_CLEANUP", "cleanup did not complete")
    for key in expected - {"complete"}:
        if not isinstance(value[key], list) or value[key]:
            raise AcceptanceFailure("FAIL_CLEANUP", f"cleanup retained {key}")


def load_cleanup_evidence_if_present(
    output: pathlib.Path,
) -> Mapping[str, Any] | None:
    """Load bounded live cleanup evidence without assuming it is clean."""

    target = pathlib.Path(output) / "cleanup.json"
    if not target.is_file():
        return None
    value = _read_json_object(target, "cleanup evidence")
    expected = set(protocol.clean_cleanup_evidence())
    if set(value) != expected or not isinstance(value.get("complete"), bool):
        raise AcceptanceFailure("FAIL_CLEANUP", "cleanup evidence shape differs")
    for key in expected - {"complete"}:
        if not isinstance(value[key], list) or len(value[key]) > 256:
            raise AcceptanceFailure("FAIL_CLEANUP", f"cleanup {key} is invalid")
    return value


def promote_build_to_live_summary(_build_summary: Mapping[str, Any]) -> None:
    """Reject the forbidden build-PASS to live-PASS conversion by construction."""

    raise AcceptanceFailure(
        "FAIL_EVIDENCE", "build/tooling evidence cannot be promoted to a live PASS"
    )


def write_json_atomic(path: pathlib.Path, value: Mapping[str, Any]) -> None:
    """Persist one JSON object by atomic replacement within its owned directory."""

    target = pathlib.Path(path)
    target.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(
        mode="w",
        encoding="utf-8",
        newline="\n",
        dir=target.parent,
        prefix=target.name + ".",
        suffix=".tmp",
        delete=False,
    ) as stream:
        json.dump(value, stream, indent=2, sort_keys=True)
        stream.write("\n")
        temporary = pathlib.Path(stream.name)
    os.replace(temporary, target)


def persist_not_run(
    output: pathlib.Path,
    *,
    run_id: str,
    token: str,
    case_id: str,
    head: str,
    prerequisite: str,
) -> dict[str, Any]:
    """Persist an honest blocking result after prerequisite preflight."""

    root = pathlib.Path(output).resolve()
    result = protocol.make_not_run_summary(
        run_id=run_id,
        token=token,
        case_id=case_id,
        head=head,
        prerequisite=prerequisite,
        evidence_root=str(root),
    )
    write_json_atomic(root / "terminal-summary.json", result)
    (root / "terminal-marker.txt").write_text(
        protocol.format_terminal_line(result) + "\n", encoding="utf-8"
    )
    return result


def persist_terminal(
    output: pathlib.Path,
    result: Mapping[str, Any],
) -> Mapping[str, Any]:
    """Persist one already validated PASS or FAIL terminal result."""

    validated = protocol.validate_terminal_summary(result)
    root = pathlib.Path(output).resolve()
    write_json_atomic(root / "terminal-summary.json", validated)
    (root / "terminal-marker.txt").write_text(
        protocol.format_terminal_line(validated) + "\n", encoding="utf-8"
    )
    return validated


def _new_run_identity(case_id: str, requested_run_id: str | None) -> tuple[str, str]:
    """Create run identity."""
    token = "p186h_" + secrets.token_hex(16)
    if requested_run_id is not None:
        return protocol.require_run_id(requested_run_id), token
    suffix = secrets.token_hex(6)
    case_slug = case_id.replace("manual-", "")[:28]
    return protocol.require_run_id(f"phase186h-{case_slug}-{suffix}"), token


def _owned_run_root(repository: pathlib.Path, requested: pathlib.Path, run_id: str) -> pathlib.Path:
    """Handle owned run root for Phase186 acceptance."""
    root = pathlib.Path(requested)
    if not root.is_absolute():
        root = repository / root
    root = root.resolve()
    phase_root = (repository / "build" / "phase186").resolve()
    if root != phase_root and phase_root not in root.parents:
        raise AcceptanceFailure(
            "FAIL_PREFLIGHT", "output root must stay below repository build/phase186"
        )
    run_root = root / run_id
    if run_root.exists() and any(run_root.iterdir()):
        raise AcceptanceFailure(
            "FAIL_PREFLIGHT", "owned run directory already exists and is not empty"
        )
    run_root.mkdir(parents=True, exist_ok=True)
    return run_root


__all__ = [name for name in globals() if not name.startswith("__")]
