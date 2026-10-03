from __future__ import annotations
from .fake_job_owner import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase184_foxglove_desktop_live_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _CoordinatorHarness_support:
    """Decomposed Phase192 implementation component."""
    def __init__(self, repository: pathlib.Path, *, mode: str = "success"):
        """Initialize the coordinator harness."""

        self.repository = repository.resolve()
        self.mode = mode
        self.run_id = "phase184g-20260727-desktop01"
        self.token = "p184g_A1b2C3d4E5f6"
        self.port = 8765
        self.events: list[str] = []
        self.launches: list[tuple[str, dict[str, object]]] = []
        self.clock = FakeClock()
        self.owner: FakeJobOwner | None = None
        self.second_seen = False
        self.summary_writes: list[pathlib.Path] = []
        self.barrier_payloads: list[dict[str, object]] = []
        self._noted_events: set[str] = set()
        self.pre_desktop_log_reads = 0
        self.exit_verifier_inputs: list[
            tuple[job_owner.ProcessIdentity, ...]
        ] = []
        self.desktop_lease_active = False
        self.desktop_lease_snapshot_count = 0
        self.base_ready_at: float | None = None
        self.context_ready_at: float | None = None
        self._prepare_files()
    @property
    def output(self) -> pathlib.Path:
        """Handle the output step."""

        return (
            self.repository
            / "build"
            / "phase184"
            / "acceptance"
            / self.run_id
        )
    @property
    def coordinator_output(self) -> pathlib.Path:
        """Handle the coordinator output step."""

        return (
            self.repository
            / "build"
            / "phase184"
            / "desktop-live"
            / self.run_id
        )
    @property
    def barrier(self) -> pathlib.Path:
        """Handle the barrier step."""

        return self.output / live_protocol.DESKTOP_CLIENT_BARRIER_FILENAME
    @property
    def base_ready(self) -> pathlib.Path:
        """Handle the base ready step."""

        return self.output / "ready" / "foxglove-client.json"
    def _write_file(self, relative: str, payload: bytes = b"x") -> pathlib.Path:
        """Write file."""

        path = self.repository / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(payload)
        return path
    def _prepare_files(self) -> None:
        """Prepare files."""

        self.unity = self._write_file("tools/Unity.exe")
        self.cli = self._write_file("tools/foxglove.exe", b"cli")
        self.desktop = self._write_file(
            "tools/Foxglove.exe",
            b"desktop",
        )
        self.receipt = self._write_file(
            "build/phase184/tooling/foxglove-cli-install-receipt.json",
            b"{}",
        )
        self._write_file(
            "Scripts/smoke/foxrun/phase184_profile_acceptance.py",
            b"# fake base runner\n",
        )
    def args(self) -> argparse.Namespace:
        """Handle the args step."""

        return argparse.Namespace(
            unity_editor=self.unity,
            foxglove_cli=self.cli,
            desktop_executable=self.desktop,
            cli_receipt=self.receipt,
            foxglove_port=self.port,
            run_id=self.run_id,
        )
    def create_base_run(self) -> None:
        """Handle the create base run step."""

        self.output.mkdir(parents=True, exist_ok=False)
        (self.output / "ready").mkdir()
        (self.output / "results").mkdir()
        config = base_run_config(
            self.repository,
            self.run_id,
            self.token,
            self.port,
        )
        base_protocol.validate_run_config(config, self.repository)
        (self.output / "run-config.json").write_text(
            json.dumps(config, sort_keys=True),
            encoding="utf-8",
        )
        pathlib.Path(str(config["unityLog"])).write_text(
            "",
            encoding="utf-8",
        )
        if self.mode == "delayed-base-ready":
            self.base_ready_at = (
                self.clock.value
                + coordinator.CONNECTION_TIMEOUT_SECONDS
                + 30.0
            )
        elif self.mode != "missing-base-ready":
            self.write_base_ready(config)
        if self.mode == "delayed-context":
            self.context_ready_at = (
                self.clock.value
                + coordinator.CONNECTION_TIMEOUT_SECONDS
                + 30.0
            )
    def write_base_ready(
        self,
        config: dict[str, object] | None = None,
    ) -> None:
        """Write base ready."""

        if self.base_ready.exists():
            return
        if config is None:
            config = json.loads(
                (self.output / "run-config.json").read_text(
                    encoding="utf-8"
                )
            )
        token_digest = base_protocol.token_sha256(
            str(config["token"])
        )
        if self.mode == "stale-base-ready":
            token_digest = "f" * 64
        details: object = {
            "state": "connect-loop-ready",
            "host": "loopback",
            "topicCount": len(config["topics"]),
        }
        if self.mode == "malformed-base-ready":
            details = {"state": "scene-builder-running"}
        payload = {
            "schemaVersion": (
                True
                if self.mode == "boolean-base-ready-schema"
                else 1
            ),
            "runId": config["runId"],
            "case": config["case"],
            "role": "foxglove-client",
            "tokenSha256": token_digest,
            "ready": True,
            "details": details,
        }
        self.base_ready.write_text(
            json.dumps(payload, sort_keys=True),
            encoding="utf-8",
        )
        self.events.append("base:ready")
    def write_base_summary(self) -> None:
        """Write base summary."""

        path = self.output / "summary.json"
        if path.exists():
            return
        config = json.loads(
            (self.output / "run-config.json").read_text(encoding="utf-8")
        )
        summary = base_pass_summary(config)
        if self.mode == "base-summary-fail":
            summary["verdict"] = "FAIL_TERMINAL"
            summary["foxglove"]["deliveryObserved"] = False
        path.write_text(
            json.dumps(summary, sort_keys=True),
            encoding="utf-8",
        )
    def read_log_lines(self, path: pathlib.Path, max_bytes: int):
        """Read log lines."""

        self.events.append("log:read")
        if (
            self.mode
            in {"delayed-base-ready", "missing-base-ready"}
            and not self.base_ready.exists()
        ):
            return ()
        if self.mode == "missing-context":
            return ()
        if (
            self.mode == "delayed-context"
            and self.context_ready_at is not None
            and self.clock.value < self.context_ready_at
        ):
            return ()
        case = "foxglove-profile"
        token = self.token
        if self.mode == "wrong-token":
            token = "p184g_Z9y8X7w6V5u4"
        if self.mode == "wrong-case":
            case = "multi-target"
        lines = [
            (
                f"PHASE184G_CONTEXT_READY case={case} token={token} "
                f"tokenDigest={hashlib.sha256(token.encode()).hexdigest()[:12]}"
            ),
            (
                f"{live_protocol.TRANSPORT_CLIENTS_MARKER} "
                f"case={case} token={token} active=0 accepted=0"
            ),
        ]
        if self.mode == "context-after-initial":
            lines.reverse()
        if self.owner is not None and not self.owner.desktop_launched:
            self.pre_desktop_log_reads += 1
            if (
                self.mode == "unstable-initial"
                and self.pre_desktop_log_reads >= 2
            ):
                lines.append(
                    f"{live_protocol.TRANSPORT_CLIENTS_MARKER} "
                    f"case={case} token={token} active=1 accepted=1"
                )
        self._event_once("marker:context")
        self._event_once("marker:initial")
        if self.owner is not None and self.owner.desktop_launched:
            if self.mode == "overflow":
                lines.append(
                    f"{live_protocol.TRANSPORT_CLIENTS_OVERFLOW_MARKER} "
                    f"case={case} token={token} active=1 accepted=1"
                )
            elif self.mode == "wrong-order":
                lines.append(
                    f"{live_protocol.TRANSPORT_CLIENTS_MARKER} "
                    f"case={case} token={token} active=2 accepted=2"
                )
            elif self.mode != "missing-first":
                lines.append(
                    f"{live_protocol.TRANSPORT_CLIENTS_MARKER} "
                    f"case={case} token={token} active=1 accepted=1"
                )
                self._event_once("marker:first")
                if self.mode == "transport-regression":
                    lines.append(
                        f"{live_protocol.TRANSPORT_CLIENTS_MARKER} "
                        f"case={case} token={token} active=0 accepted=0"
                    )
        if self.barrier.is_file():
            lines.append(
                f"{live_protocol.TRANSPORT_CLIENTS_MARKER} "
                f"case={case} token={token} active=2 accepted=3"
            )
            self.second_seen = True
            self._event_once("marker:second")
        return tuple(lines)
    def _event_once(self, event: str) -> None:
        """Handle the event once step."""

        if event not in self._noted_events:
            self._noted_events.add(event)
            self.events.append(event)
    def load_json_snapshot(self, path: pathlib.Path, max_bytes: int):
        """Load JSON snapshot."""

        self.events.append(
            "json:read-summary"
            if pathlib.Path(path).name == "summary.json"
            else "json:read-config"
        )
        raw = pathlib.Path(path).read_bytes()
        if not raw or len(raw) > max_bytes:
            raise ValueError("bounded JSON read failed")
        return json.loads(raw.decode("utf-8"))
    def write_json_atomic(
        self,
        path: pathlib.Path,
        payload,
        *,
        max_bytes: int,
    ) -> None:
        """Write JSON atomic."""

        path = pathlib.Path(path)
        self.events.append(
            "summary:write"
            if path.name == coordinator.DESKTOP_LIVE_SUMMARY_FILENAME
            else "barrier:write"
        )
        if path.name != coordinator.DESKTOP_LIVE_SUMMARY_FILENAME:
            self.barrier_payloads.append(dict(payload))
        live_protocol.write_json_atomic(
            path,
            payload,
            max_bytes=max_bytes,
        )
        if path.name == coordinator.DESKTOP_LIVE_SUMMARY_FILENAME:
            self.summary_writes.append(path)
    def remove_owned_file(self, path: pathlib.Path) -> bool:
        """Remove owned file."""

        self.events.append("barrier:remove")
        try:
            pathlib.Path(path).unlink()
        except FileNotFoundError:
            pass
        return not pathlib.Path(path).exists()
    def verify_cli(self, install_path, receipt_path):
        """Handle the verify CLI step."""

        self.events.append("cli:verify")
        if self.mode == "cli-fail":
            raise live_protocol.AcceptanceFailure(
                live_protocol.FAIL_CLI_PROVENANCE,
                "injected CLI provenance failure",
            )
        return cli_install.VerifiedCliIdentity(
            installed_path=str(install_path),
            installed_version="1.2.3",
            installed_sha256=live_protocol.sha256_file(install_path),
            release_tag="v1.2.3",
            asset_url=(
                "https://github.com/foxglove/foxglove-cli/releases/"
                "download/v1.2.3/foxglove-windows-amd64.exe"
            ),
            architecture="windows-amd64",
            receipt_path=str(receipt_path),
        )
    def read_desktop_version(self, _path):
        """Read desktop version."""

        if self.mode == "desktop-version-fail":
            raise OSError("injected version failure")
        if self.mode == "desktop-version-race":
            self.desktop.write_bytes(b"desktop replaced after version")
        return "2.9.0.0"
    def hash_file(self, path):
        """Handle the hash file step."""

        if (
            self.mode == "desktop-hash-fail"
            and pathlib.Path(path) == self.desktop
        ):
            raise OSError("injected hash failure")
        return live_protocol.sha256_file(path)
    def read_uri_handler(self):
        """Read URI handler."""

        if self.mode == "desktop-handler-fail":
            return r'"D:\Other\Foxglove.exe" "%1"'
        if self.mode == "desktop-handler-race":
            self.desktop.write_bytes(b"desktop replaced after handler")
        return f'"{self.desktop}" "%1"'


__all__ = [name for name in globals() if not name.startswith("__")]
