from __future__ import annotations
from .filesystem_and_executable_lease import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase184_foxglove_cli_install.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE


def _resolve_receipt_path(path: object) -> str:
    """Resolve an absolute or repository-relative receipt path safely."""

    try:
        value = os.fspath(path)
    except TypeError as exc:
        raise _fail(
            "Foxglove CLI receipt path must be a Windows path."
        ) from exc
    if (
        not isinstance(value, str)
        or not value
        or len(value) > _MAX_WINDOWS_PATH_CHARACTERS
        or "\x00" in value
        or "\r" in value
        or "\n" in value
    ):
        raise _fail("Foxglove CLI receipt path must be a Windows path.")

    drive, _ = ntpath.splitdrive(value)
    if ntpath.isabs(value):
        return _validated_windows_path(value, "Foxglove CLI receipt path")
    if drive:
        raise _fail(
            "Foxglove CLI receipt path must be absolute or repository-relative."
        )

    repository_root = _validated_windows_path(
        REPOSITORY_ROOT,
        "Foxglove CLI repository root",
    )
    resolved = _validated_windows_path(
        ntpath.join(repository_root, value),
        "Foxglove CLI receipt path",
    )
    try:
        common = ntpath.commonpath(
            (
                protocol.windows_path_key(
                    repository_root,
                    label="Foxglove CLI repository root",
                ),
                protocol.windows_path_key(
                    resolved,
                    label="Foxglove CLI receipt path",
                ),
            )
        )
    except ValueError as exc:
        raise _fail(
            "Relative Foxglove CLI receipt path is outside the repository."
        ) from exc
    if common != protocol.windows_path_key(
        repository_root,
        label="Foxglove CLI repository root",
    ):
        raise _fail(
            "Relative Foxglove CLI receipt path is outside the repository."
        )
    return resolved


def _require_distinct_windows_paths(
    paths: Sequence[tuple[str, object]],
) -> None:
    """Reject transaction paths that alias under Windows path semantics."""

    seen: set[str] = set()
    for label, path in paths:
        key = protocol.windows_path_key(path, label=label)
        if key in seen:
            raise _fail(
                "Foxglove CLI transaction paths must be Windows-distinct."
            )
        seen.add(key)


def _revision_slug(revision: object) -> str:
    """Return a bounded filesystem-safe release revision component."""

    if not isinstance(revision, str):
        return "unknown"
    candidate = revision.strip()
    candidate = re.sub(r"[^A-Za-z0-9._-]+", "-", candidate)
    candidate = re.sub(r"[-_.]{2,}", "-", candidate).strip("-_.")
    if not candidate:
        return "unknown"
    return candidate[:_MAX_BACKUP_REVISION_CHARACTERS]


def build_backup_path(
    install_path: os.PathLike[str] | str,
    previous_revision: object,
    previous_sha256: object,
) -> str:
    """Build one deterministic revision/hash-qualified sibling backup path."""

    target = _validated_windows_path(install_path, "Foxglove CLI install path")
    digest = protocol.validate_sha256(previous_sha256)
    directory = ntpath.dirname(target)
    filename = ntpath.basename(target)
    stem, extension = ntpath.splitext(filename)
    if not stem:
        raise _fail("Foxglove CLI install filename is invalid.")
    backup = ntpath.join(
        directory,
        (
            f"{stem}.{_revision_slug(previous_revision)}-"
            f"{digest[:_BACKUP_HASH_CHARACTERS]}{extension}"
        ),
    )
    if len(backup) > _MAX_WINDOWS_PATH_CHARACTERS:
        raise _fail("Foxglove CLI backup path is too long.")
    if protocol.windows_paths_equal(backup, target):
        raise _fail("Foxglove CLI backup path must differ from the install path.")
    return backup


def select_release_asset(release: object) -> ReleaseAsset:
    """Select exactly one official Windows amd64 asset from one release."""

    if not isinstance(release, Mapping):
        raise _fail("Foxglove CLI release response is invalid.")
    release_tag = release.get("tag_name")
    release_version = protocol.normalize_semantic_version(release_tag)
    assets = release.get("assets")
    if (
        not isinstance(assets, list)
        or len(assets) > MAX_RELEASE_ASSETS
    ):
        raise _fail("Foxglove CLI release assets are invalid.")

    matches: list[Mapping[str, object]] = []
    for asset in assets:
        asset_name = asset.get("name") if isinstance(asset, Mapping) else None
        if (
            isinstance(asset, Mapping)
            and isinstance(asset_name, str)
            and asset_name in protocol.CLI_ASSET_NAMES
        ):
            matches.append(asset)
    if len(matches) != 1:
        raise _fail("Foxglove CLI release must contain exactly one Windows asset.")

    selected_asset = matches[0]
    asset_url = selected_asset.get("browser_download_url")
    protocol.validate_official_asset_url(
        asset_url,
        expected_release_version=release_version,
    )
    selected_name = str(selected_asset["name"])
    if not isinstance(asset_url, str) or asset_url.rsplit("/", 1)[-1] != selected_name:
        raise _fail("Foxglove CLI release asset name and URL do not match.")
    asset_size = selected_asset.get("size")
    if (
        isinstance(asset_size, bool)
        or not isinstance(asset_size, int)
        or asset_size < 1
        or asset_size > MAX_DOWNLOAD_BYTES
    ):
        raise _fail("Foxglove CLI release asset size is invalid.")
    asset_digest = selected_asset.get("digest")
    digest_match = (
        _GITHUB_ASSET_DIGEST.fullmatch(asset_digest)
        if isinstance(asset_digest, str)
        else None
    )
    if digest_match is None:
        raise _fail("Foxglove CLI release asset digest is invalid.")
    return ReleaseAsset(
        release_tag=str(release_tag),
        release_version=release_version,
        asset_name=selected_name,
        asset_url=str(asset_url),
        asset_size=asset_size,
        asset_sha256=digest_match.group(1).upper(),
    )


def validate_official_download_hop_url(url: object) -> str:
    """Allow only the canonical asset route and exact HTTPS GitHub CDN hosts."""

    if (
        not isinstance(url, str)
        or not url
        or len(url) > _MAX_DOWNLOAD_HOP_URL_CHARACTERS
        or url != url.strip()
    ):
        raise _fail("Foxglove CLI download URL is invalid.")
    try:
        parsed = urllib.parse.urlsplit(url)
        port = parsed.port
    except ValueError as exc:
        raise _fail("Foxglove CLI download URL is invalid.") from exc
    if (
        parsed.scheme != "https"
        or parsed.netloc not in _OFFICIAL_DOWNLOAD_HOSTS
        or parsed.hostname not in _OFFICIAL_DOWNLOAD_HOSTS
        or parsed.username is not None
        or parsed.password is not None
        or port is not None
        or parsed.fragment
        or not parsed.path.startswith("/")
        or parsed.path == "/"
    ):
        raise _fail("Foxglove CLI download URL is not an official HTTPS route.")
    if parsed.netloc == "github.com":
        protocol.validate_official_asset_url(url)
    return url


class OfficialReleaseRedirectHandler(urllib.request.HTTPRedirectHandler):
    """Reject redirect hops outside exact HTTPS GitHub release hosts."""

    def redirect_request(
        self,
        request: urllib.request.Request,
        fp: Any,
        code: int,
        message: str,
        headers: Any,
        newurl: str,
    ) -> urllib.request.Request | None:
        """Validate a redirect destination before urllib follows it."""

        validate_official_download_hop_url(newurl)
        return super().redirect_request(
            request,
            fp,
            code,
            message,
            headers,
            newurl,
        )


def _fetch_release_production(endpoint: str) -> object:
    """Fetch bounded release metadata from the official GitHub endpoint."""

    if endpoint != RELEASE_ENDPOINT:
        raise _fail("Foxglove CLI release endpoint is not official.")
    request = urllib.request.Request(
        endpoint,
        headers={
            "Accept": "application/vnd.github+json",
            "X-GitHub-Api-Version": GITHUB_API_VERSION,
            "User-Agent": "Unity2Foxglove-Phase184H",
        },
        method="GET",
    )
    try:
        with urllib.request.urlopen(
            request,
            timeout=NETWORK_TIMEOUT_SECONDS,
        ) as response:
            raw = response.read(MAX_RELEASE_BYTES + 1)
    except (OSError, ValueError) as exc:
        raise _fail("Foxglove CLI release metadata could not be fetched.") from exc
    if not raw or len(raw) > MAX_RELEASE_BYTES:
        raise _fail("Foxglove CLI release metadata size is invalid.")
    try:
        return json.loads(raw.decode("utf-8"))
    except (UnicodeError, ValueError, RecursionError) as exc:
        raise _fail("Foxglove CLI release metadata is malformed.") from exc


def _download_production(asset_url: str, destination: str) -> None:
    """Download one bounded official asset into an exclusive destination."""

    protocol.validate_official_asset_url(asset_url)
    request = urllib.request.Request(
        asset_url,
        headers={"User-Agent": "Unity2Foxglove-Phase184H"},
        method="GET",
    )
    destination_path = pathlib.Path(destination)
    total = 0
    opener = urllib.request.build_opener(OfficialReleaseRedirectHandler())
    try:
        with opener.open(
            request,
            timeout=NETWORK_TIMEOUT_SECONDS,
        ) as response:
            validate_official_download_hop_url(response.geturl())
            with destination_path.open("xb") as output_stream:
                while True:
                    chunk = response.read(min(1024 * 1024, MAX_DOWNLOAD_BYTES - total + 1))
                    if not chunk:
                        break
                    total += len(chunk)
                    if total > MAX_DOWNLOAD_BYTES:
                        raise _fail("Foxglove CLI download exceeds the size bound.")
                    output_stream.write(chunk)
                output_stream.flush()
                os.fsync(output_stream.fileno())
    except protocol.AcceptanceFailure:
        with contextlib.suppress(OSError):
            destination_path.unlink()
        raise
    except (OSError, ValueError) as exc:
        with contextlib.suppress(OSError):
            destination_path.unlink()
        raise _fail("Foxglove CLI asset download failed.") from exc
    if total < 1:
        with contextlib.suppress(OSError):
            destination_path.unlink()
        raise _fail("Foxglove CLI asset download is empty.")


def _terminate_and_reap(process: subprocess.Popen[bytes]) -> None:
    """Terminate a child process tree if needed and always reap the root."""

    if process.poll() is None:
        if os.name == "nt" and isinstance(process, _REAL_POPEN_TYPE):
            # ``Popen.terminate`` only signals the root process on Windows;
            # taskkill /T retires descendants owned by that root before reap.
            taskkill = os.path.join(
                os.environ.get("SystemRoot", r"C:\Windows"),
                "System32",
                "taskkill.exe",
            )
            with contextlib.suppress(OSError, subprocess.SubprocessError):
                os.spawnv(
                    os.P_WAIT,
                    taskkill,
                    [taskkill, "/PID", str(process.pid), "/T", "/F"],
                )
        with contextlib.suppress(OSError):
            process.terminate()
        try:
            process.wait(timeout=1)
        except subprocess.TimeoutExpired:
            if os.name == "nt":
                # Terminate descendants without routing through subprocess.run/Popen;
                # the bounded-process harness patches those APIs to observe the
                # owned root and must not mistake tree cleanup for a second launch.
                with contextlib.suppress(OSError):
                    os.system(f"taskkill /PID {int(process.pid)} /T /F >NUL 2>&1")
            with contextlib.suppress(OSError):
                process.kill()
            try:
                process.wait(timeout=5)
            except (OSError, subprocess.SubprocessError) as exc:
                raise _fail(
                    "Bounded Foxglove CLI helper could not be reaped."
                ) from exc
        except OSError as exc:
            raise _fail(
                "Bounded Foxglove CLI helper could not be reaped."
            ) from exc
    else:
        try:
            process.wait(timeout=0)
        except (OSError, subprocess.SubprocessError) as exc:
            raise _fail(
                "Bounded Foxglove CLI helper could not be reaped."
            ) from exc


__all__ = [name for name in globals() if not name.startswith("__")]
