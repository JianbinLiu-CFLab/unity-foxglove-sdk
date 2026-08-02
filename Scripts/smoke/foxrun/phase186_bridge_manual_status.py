#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Concise, stoppable operator status for a Phase186-H manual run."""

from __future__ import annotations

import threading
import time
from collections.abc import Callable


Clock = Callable[[], float]
Sink = Callable[[str], None]
Wait = Callable[[threading.Event, float], bool]


def _default_wait(stop: threading.Event, seconds: float) -> bool:
    return stop.wait(seconds)


class ManualStatusReporter:
    """Emit one transition, then bounded heartbeats for the current stage."""

    def __init__(
        self,
        *,
        clock: Clock = time.monotonic,
        sink: Sink = print,
        heartbeat_seconds: float = 10.0,
        wait: Wait = _default_wait,
    ) -> None:
        if heartbeat_seconds <= 0:
            raise ValueError("heartbeat_seconds must be positive")
        self._clock = clock
        self._sink = sink
        self._heartbeat_seconds = heartbeat_seconds
        self._wait = wait
        self._started = clock()
        self._stage: str | None = None
        self._message: str | None = None
        self._last_transition: tuple[str, str] | None = None
        self._lock = threading.Lock()
        self._stop = threading.Event()
        self._thread = threading.Thread(
            target=self._run_heartbeats,
            name="phase186-manual-status",
            daemon=True,
        )
        self._thread.start()

    @property
    def is_alive(self) -> bool:
        """Expose the worker state for deterministic regression checks."""

        return self._thread.is_alive()

    def _elapsed(self) -> str:
        return f"{int(max(0.0, self._clock() - self._started))}s"

    def _emit(self, event: str, text: str) -> None:
        self._sink(f"PHASE186_MANUAL_STATUS {event} {text}")

    def transition(self, stage: str, message: str) -> None:
        """Announce one coordinator transition and make it the heartbeat stage."""

        clean_stage = str(stage).strip()
        clean_message = str(message).strip()
        if not clean_stage or not clean_message:
            raise ValueError("manual status transitions require a stage and message")
        with self._lock:
            if self._last_transition == (clean_stage, clean_message):
                return
            self._stage = clean_stage
            self._message = clean_message
            self._last_transition = (clean_stage, clean_message)
        self._emit(
            "transition",
            f"stage={clean_stage} elapsed={self._elapsed()} message={clean_message}",
        )

    def unity_prepare(self, label: str) -> None:
        """Request scene preparation without implying Play is safe yet."""

        del label
        self._sink(
            "UNITY ACTION 1: Foxglove > Manual Acceptance > Phase186 > "
            "Prepare Current Bridge Run"
        )

    def unity_play_ready(self, label: str) -> None:
        """Request Play only after Unity reports a stable generated schema."""

        del label
        self._sink("UNITY ACTION 2: Enter Play Mode once")

    def detail(self, message: str) -> None:
        """Emit concise supplemental context without changing the stage."""

        clean_message = str(message).strip()
        if clean_message:
            with self._lock:
                if self._stage is not None:
                    self._message = clean_message
            self._emit("detail", clean_message)

    def terminal(self, verdict: str, reason: str, evidence_root: str) -> None:
        """Emit the concise operator handoff before the machine terminal line."""

        clean_verdict = str(verdict).strip()
        clean_reason = str(reason).strip()
        clean_evidence = str(evidence_root).strip()
        if clean_verdict not in {"PASS", "FAIL", "NOT RUN"}:
            raise ValueError("manual terminal verdict is invalid")
        if not clean_reason or not clean_evidence:
            raise ValueError("manual terminal reason and evidence are required")
        self._sink(
            f"PHASE186 MANUAL VERDICT: {clean_verdict} - {clean_reason}"
        )
        self._sink(f"PHASE186 MANUAL EVIDENCE: {clean_evidence}")
        if clean_verdict == "PASS":
            next_action = (
                "Exit Play Mode only after terminal cleanup finished; "
                "move to the next suite."
            )
        else:
            next_action = (
                "Review the evidence, fix the named cause, then rerun "
                "the same one-line suite."
            )
        self._sink("PHASE186 MANUAL NEXT: " + next_action)

    def _run_heartbeats(self) -> None:
        while not self._wait(self._stop, self._heartbeat_seconds):
            with self._lock:
                stage = self._stage
                message = self._message
            if stage is not None and message is not None:
                self._emit(
                    "heartbeat",
                    f"stage={stage} elapsed={self._elapsed()} message={message}",
                )

    def close(self) -> None:
        """Stop and join the heartbeat worker; safe to invoke repeatedly."""

        self._stop.set()
        if self._thread is not threading.current_thread():
            self._thread.join()

    def __enter__(self) -> "ManualStatusReporter":
        return self

    def __exit__(self, _type: object, _value: object, _traceback: object) -> None:
        self.close()


class NullManualStatusReporter:
    """A status-compatible reporter for quiet noninteractive callers."""

    def transition(self, _stage: str, _message: str) -> None:
        return None

    def unity_prepare(self, _label: str) -> None:
        return None

    def unity_play_ready(self, _label: str) -> None:
        return None

    def detail(self, _message: str) -> None:
        return None

    def terminal(
        self, _verdict: str, _reason: str, _evidence_root: str
    ) -> None:
        return None

    def close(self) -> None:
        return None

    def __enter__(self) -> "NullManualStatusReporter":
        return self

    def __exit__(self, _type: object, _value: object, _traceback: object) -> None:
        self.close()
