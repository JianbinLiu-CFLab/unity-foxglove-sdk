from __future__ import annotations
from .run_typed_worker_and_default_unity_editor_log_path import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase181_custom_ros2_peer.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
def _release_subst_mapping(subst: pathlib.Path, mapped_drive: str, failure_code: str) -> None:
    """Remove one owned subst mapping and require command success plus disappearance."""

    result = subprocess.run(
        (str(subst), mapped_drive + ":", "/D"),
        shell=False,
        capture_output=True,
        text=True,
        errors="replace",
        check=False,
    )
    if result.returncode != 0 or pathlib.Path(mapped_drive + ":\\").exists():
        raise PeerFailure(failure_code, "The owned short-path subst mapping could not be released cleanly.")
def _require_player_path(player: pathlib.Path | None) -> pathlib.Path:
    """Accept only one existing absolute WindowsStandalone64 Player executable."""

    if player is None:
        raise PeerFailure("FAIL_PLAYER_BUILD", "The Windows Player path is required for Player acceptance.")
    candidate = pathlib.Path(player)
    if not candidate.is_absolute() or not candidate.is_file():
        raise PeerFailure("FAIL_PLAYER_BUILD", "The Windows Player path must identify an existing absolute executable.")
    return candidate.resolve()
def _require_unity_editor_path(editor: pathlib.Path | None) -> pathlib.Path:
    """Accept one explicit installed Unity Editor executable for a helper-owned Batch process."""

    if editor is None:
        raise PeerFailure("FAIL_EDITOR_BATCH", "Editor Batch acceptance requires an explicit Unity Editor executable path.")
    candidate = pathlib.Path(editor)
    if not candidate.is_absolute() or not candidate.is_file():
        raise PeerFailure("FAIL_EDITOR_BATCH", "The Unity Editor path must identify an existing absolute executable.")
    return candidate.resolve()
def run_windows_local_editor(
    args: argparse.Namespace,
    *,
    zenoh_session_config: pathlib.Path | None = None,
) -> int:
    """Run the owned Windows-local Editor proof for one explicit profile."""

    if args.surface != "editor":
        raise PeerFailure("FAIL_ARGUMENTS", "The Windows-local Editor path requires the editor surface.")
    return _run_windows_surface(args, surface="editor", zenoh_session_config=zenoh_session_config)
def run_windows_player(
    args: argparse.Namespace,
    *,
    zenoh_session_config: pathlib.Path | None = None,
) -> int:
    """Run the same correlated protocol against one helper-owned Player."""

    if args.surface != "player":
        raise PeerFailure("FAIL_ARGUMENTS", "The Windows Player path requires the player surface.")
    return _run_windows_surface(args, surface="player", zenoh_session_config=zenoh_session_config)


__all__ = [name for name in globals() if not name.startswith("__")]
