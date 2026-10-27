@dataclass(frozen=True)
class BuildPaths:
    """Resolved input and output paths for package generation."""

    artifact: Path
    inventory: Path
    package: Path
    ros2_bin: Path


@dataclass(frozen=True)
class RuntimeArtifact:
    """Identity of the vetted runtime artifact being packaged."""

    name: str
    sha256: str
    size: int
    inventory_file_count: int


def parse_args(argv: list[str]) -> BuildPaths:
    """Parse command-line arguments into build paths."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--zip", type=Path, default=DEFAULT_ARTIFACT, help="Runtime zip artifact to package.")
    parser.add_argument("--inventory", type=Path, default=DEFAULT_INVENTORY, help="Runtime inventory JSON.")
    parser.add_argument("--package", type=Path, default=DEFAULT_PACKAGE, help="Runtime package output directory.")
    parser.add_argument("--ros2-bin", type=Path, default=DEFAULT_ROS2_BIN, help="ROS 2 bin directory for supplemental DLLs.")
    args = parser.parse_args(argv)
    return BuildPaths(args.zip.resolve(), args.inventory.resolve(), args.package.resolve(), args.ros2_bin.resolve())


def rel(path: Path) -> str:
    """Format a path relative to the repository root when possible."""
    try:
        return path.resolve().relative_to(ROOT.resolve()).as_posix()
    except ValueError:
        return str(path)


def sha256_file(path: Path) -> str:
    """Return the SHA-256 digest for a local file."""
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def require_inputs(paths: BuildPaths) -> tuple[dict[str, object], RuntimeArtifact]:
    """Validate inputs and return the parsed runtime inventory and artifact identity."""
    if not paths.artifact.exists():
        raise FileNotFoundError(f"Missing runtime artifact: {paths.artifact}")
    if paths.artifact.name != ARTIFACT_NAME:
        raise ValueError(f"Unexpected artifact name: {paths.artifact.name}")

    artifact_hash = sha256_file(paths.artifact)
    artifact_size = paths.artifact.stat().st_size
    if artifact_hash != EXPECTED_ARTIFACT_SHA256:
        raise ValueError(f"Runtime artifact sha256 does not match pinned Jazzy handoff: {artifact_hash} != {EXPECTED_ARTIFACT_SHA256}")

    if not paths.inventory.exists():
        raise FileNotFoundError(f"Missing runtime inventory: {paths.inventory}")
    inventory = json.loads(paths.inventory.read_text(encoding="utf-8"))
    if inventory.get("runtimeId") != RUNTIME_ID:
        raise ValueError(f"Unexpected inventory runtimeId: {inventory.get('runtimeId')!r}")
    if inventory.get("sha256") != artifact_hash:
        raise ValueError("Inventory sha256 does not match the runtime artifact.")
    if inventory.get("artifactSize") not in (None, artifact_size):
        raise ValueError(f"Inventory artifactSize does not match the runtime artifact: {inventory.get('artifactSize')!r}")
    inventory_file_count = int(inventory.get("fileCount") or 0)
    if inventory_file_count <= 0:
        raise ValueError(f"Unexpected inventory fileCount: {inventory.get('fileCount')!r}")
    artifact = RuntimeArtifact(
        name=paths.artifact.name,
        sha256=artifact_hash,
        size=artifact_size,
        inventory_file_count=inventory_file_count,
    )
    return inventory, artifact


def validate_package_target(package: Path) -> Path:
    """Authorize the exact repository package target without mutating it."""
    expected_parent = (ROOT / "Packages").resolve()
    resolved = package.resolve()
    if resolved.name != PACKAGE_NAME or resolved.parent != expected_parent:
        raise ValueError(f"Refusing unauthorized package target: {resolved}")
    return resolved
