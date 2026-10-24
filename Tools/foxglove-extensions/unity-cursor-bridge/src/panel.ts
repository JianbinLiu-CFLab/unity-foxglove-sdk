// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/foxglove-extensions/unity-cursor-bridge
// Purpose: Unity replay cursor panel implementation.

import type { PanelExtensionContext, Time } from "@foxglove/extension";
import { buildPanelDom } from "./panel_dom";
import {
  DEFAULT_ENDPOINT,
  DEFAULT_MAX_HZ,
  REQUEST_TIMEOUT_MS,
  SEEK_UI_INTERVAL_MS,
  CursorPayload,
  CursorRenderState,
  MaybeSeekable,
  PanelState,
  PendingCursor,
  ReplayTime,
  ReplayTimeDisplayCache,
  SendStatus,
  advanceFollowClockAfterAck,
  buildFollowPayload,
  buildPayload,
  cloneTimeIfChanged,
  formatReplayTimeUtc,
  isBeforeTime,
  normalizeEndpoint,
  readPanelState,
  savePanelState,
  sendCursor,
  shouldSendCursor,
  tokenEndpointWarningMessage,
} from "./helpers";

export function initPanel(context: PanelExtensionContext): void | (() => void) {
  let state = readPanelState(context.initialState);
  let sequence = 0;
  let lastCursorSec = -1;
  let lastCursorNsec = -1;
  let lastSentAtMs = 0;
  let mounted = true;
  // Stage 2 (140K): at most one cursor POST outstanding (ACK backpressure). The next cursor is
  // not sent until Unity acknowledges (or the stall guard fires), so cadence tracks Unity.
  let inFlight = false;
  // Retained so cleanup can abort an outstanding request, and so handlers can tell whether they
  // still own the active request (identity guard against late callbacks).
  let cursorController: AbortController | undefined;
  let cursorTimeout: ReturnType<typeof setTimeout> | undefined;
  let inFlightCursor: PendingCursor | undefined;
  let pendingCursor: PendingCursor | undefined;
  // Stage 3 (140K): "Follow Unity replay" is self-clocked. The panel API exposes no play/pause
  // control (only seekPlayback), so follow cannot wait for Foxglove's own currentTime to advance:
  // paused it never moves, playing it fights free-run. Instead the panel owns an internal clock —
  // each Unity ACK advances followClock by one rate step, sends that as a forward "advance"
  // cursor, and calls seekPlayback best-effort to drag the Foxglove UI along. Use follow INSTEAD
  // of pressing Foxglove's play button.
  let followActive = false;
  let followClockSec = -1;
  let followClockNsec = -1;
  // Wall-clock time of the previous follow step, used to advance the internal clock by real
  // elapsed time (so playback runs at ~1x regardless of ACK latency). -1 means "not started".
  let followLastAckWallMs = -1;
  let followPumpHandle: ReturnType<typeof setTimeout> | undefined;
  // Throttle bookkeeping for the best-effort UI seek.
  let lastSeekWallMs = -1;
  // Set once follow advances to the end of the replay. The loop parks until the user scrubs back
  // before the end, at which point follow resumes from the new position.
  let followReachedEnd = false;
  // Some hosts reject a programmatic seek while paused (it throws). seekPlayback is best-effort UI
  // only; after the first failure we stop calling it but keep pumping the cursor stream to Unity.
  let seekUsable = true;
  let lastRenderSec = -1;
  let lastRenderNsec = -1;
  let lastStartTime: { sec: number; nsec: number } | undefined;
  let lastEndTime: { sec: number; nsec: number } | undefined;
  let minIntervalMs = 1000 / state.maxHz;
  const listenerController = new AbortController();
  const listenerOptions = { signal: listenerController.signal };

  const seekPlayback = (context as unknown as MaybeSeekable).seekPlayback;
  const canFollow = typeof seekPlayback === "function";
  if (!canFollow && state.followUnity) {
    // seekPlayback unavailable in this Foxglove build: force follow off so the toggle never
    // claims a capability the host does not expose.
    state = { ...state, followUnity: false };
  }

  const replayTimeCache: ReplayTimeDisplayCache = {
    lastSec: undefined,
    lastNsec: undefined,
    text: "",
  };
  let status: SendStatus = {
    ok: true,
    message: "Waiting for Foxglove replay time. Keep Unity in Play Mode.",
  };
  const panel = buildPanelDom(state, canFollow);

  function updateTokenWarning(): void {
    panel.tokenWarning.textContent = tokenEndpointWarningMessage(state.endpoint, state.token);
  }

  function stopFollow(): void {
    followActive = false;
    followLastAckWallMs = -1;
    if (followPumpHandle != undefined) {
      clearTimeout(followPumpHandle);
      followPumpHandle = undefined;
    }
  }

  // Best-effort, throttled UI catch-up. force=true bypasses the throttle (used for the final seek
  // to the end).
  function seekUi(sec: number, nsec: number, force: boolean): void {
    if (!seekUsable || seekPlayback == undefined) {
      return;
    }
    const nowWall = Date.now();
    if (!force && lastSeekWallMs >= 0 && nowWall - lastSeekWallMs < SEEK_UI_INTERVAL_MS) {
      return;
    }
    lastSeekWallMs = nowWall;
    try {
      seekPlayback({ sec, nsec });
    } catch {
      seekUsable = false;
    }
  }

  // Send one cursor and route every terminal outcome through onSettled(ok, delivered):
  //   ok=true        -> Unity accepted the cursor (HTTP 2xx)
  //   delivered=true -> an HTTP response came back (2xx or not); the cursor reached Unity
  //   delivered=false -> stall-timeout or network failure; the cursor never landed (safe to retry)
  function dispatchCursor(payload: CursorPayload, onSettled: (ok: boolean, delivered: boolean) => void): void {
    inFlight = true;
    lastSentAtMs = Date.now();
    const controller = new AbortController();
    cursorController = controller;
    const dispatched: PendingCursor = {
      payload,
      sentSec: payload.time.sec,
      sentNsec: payload.time.nsec,
      didSeek: payload.didSeek,
    };
    inFlightCursor = dispatched;

    // Stage 2 stall guard: if neither handler fires within REQUEST_TIMEOUT_MS, abort and release
    // in-flight so the panel keeps trying instead of wedging forever.
    cursorTimeout = setTimeout(() => {
      cursorTimeout = undefined;
      if (!mounted || cursorController !== controller) {
        return;
      }
      controller.abort();
      inFlight = false;
      inFlightCursor = undefined;
      status = { ok: false, message: `Unity did not respond within ${REQUEST_TIMEOUT_MS} ms. Retrying.` };
      onSettled(false, false);
      flushPendingCursor();
    }, REQUEST_TIMEOUT_MS);

    void sendCursor(state.endpoint, state.token, payload, controller.signal).then(
      (result) => {
        // Ignore callbacks from a request a newer send or cleanup has superseded.
        if (cursorController !== controller) {
          return;
        }
        if (cursorTimeout != undefined) {
          clearTimeout(cursorTimeout);
          cursorTimeout = undefined;
        }
        // Timed-out/aborted request: the timeout handler already settled it.
        if (!mounted || controller.signal.aborted) {
          return;
        }
        inFlight = false;
        inFlightCursor = undefined;
        status = result;
        onSettled(result.ok, true);
        flushPendingCursor();
      },
      (error: unknown) => {
        if (cursorController !== controller) {
          return;
        }
        if (cursorTimeout != undefined) {
          clearTimeout(cursorTimeout);
          cursorTimeout = undefined;
        }
        if (!mounted || controller.signal.aborted) {
          return;
        }
        inFlight = false;
        inFlightCursor = undefined;
        status = { ok: false, message: `Cannot reach Unity. Check Play Mode and endpoint. ${String(error)}` };
        onSettled(false, false);
        flushPendingCursor();
      },
    );
  }

  function flushPendingCursor(): void {
    const pending = pendingCursor;
    pendingCursor = undefined;
    if (!mounted || !state.enabled || state.followUnity || pending == undefined || inFlight) {
      return;
    }

    dispatchCursor(pending.payload, (_ok, delivered) => {
      if (delivered) {
        lastCursorSec = pending.sentSec;
        lastCursorNsec = pending.sentNsec;
      }
    });
  }

  function queueLatestCursorIfNeeded(currentTime: Time, renderState: CursorRenderState): void {
    if (!state.enabled || state.followUnity || currentTime == undefined) {
      pendingCursor = undefined;
      return;
    }

    const didSeek = renderState.didSeek === true;
    if (
      (inFlightCursor?.sentSec === currentTime.sec
        && inFlightCursor.sentNsec === currentTime.nsec
        && inFlightCursor.didSeek === didSeek)
      || (pendingCursor?.sentSec === currentTime.sec
        && pendingCursor.sentNsec === currentTime.nsec
        && pendingCursor.didSeek === didSeek)
    ) {
      return;
    }

    const payload = buildPayload(renderState, sequence + 1);
    if (payload == undefined) {
      return;
    }
    sequence = payload.sequence;
    pendingCursor = {
      payload,
      sentSec: currentTime.sec,
      sentNsec: currentTime.nsec,
      didSeek: payload.didSeek,
    };
  }

  function scheduleFollowPump(): void {
    if (followPumpHandle != undefined) {
      return;
    }
    const delay = Math.max(0, minIntervalMs - (Date.now() - lastSentAtMs));
    followPumpHandle = setTimeout(() => {
      followPumpHandle = undefined;
      pumpFollow();
    }, delay);
  }

  function parkFollowAtEnd(endTime: ReplayTime): void {
    followClockSec = endTime.sec;
    followClockNsec = endTime.nsec;
    seekUi(endTime.sec, endTime.nsec, true);
    followReachedEnd = true;
    stopFollow();
    status = { ok: true, message: "Reached end of replay. Scrub earlier to follow from there." };
  }

  function resumeFollowFromScrubIfNeeded(renderState: CursorRenderState, currentTime: Time | undefined): void {
    if (
      !state.enabled ||
      !state.followUnity ||
      !canFollow ||
      !followReachedEnd ||
      renderState.didSeek !== true ||
      currentTime == undefined ||
      lastEndTime == undefined ||
      !isBeforeTime(currentTime, lastEndTime)
    ) {
      return;
    }

    followReachedEnd = false;
    followActive = false;
    followLastAckWallMs = -1;
    lastCursorSec = -1;
    lastCursorNsec = -1;
    status = { ok: true, message: "Following Unity replay from scrubbed time." };
  }

  function pumpFollow(): void {
    if (!mounted || !state.enabled || !state.followUnity || seekPlayback == undefined) {
      stopFollow();
      return;
    }
    if (inFlight) {
      scheduleFollowPump();
      return;
    }

    const sentSec = followClockSec;
    const sentNsec = followClockNsec;
    const payload = buildFollowPayload(sequence, sentSec, sentNsec, lastStartTime, lastEndTime);
    sequence = payload.sequence;

    dispatchCursor(payload, (ok, delivered) => {
      if (!mounted || !state.enabled || !state.followUnity) {
        stopFollow();
        return;
      }
      if (ok) {
        lastCursorSec = sentSec;
        lastCursorNsec = sentNsec;

        // Advance the internal clock by the real wall time elapsed since the previous step (1x),
        // clamped under Unity's seek threshold. Real-time mapping keeps playback at normal speed
        // even when ACK latency exceeds one rate step; without it, each ACK advanced only a fixed
        // 1/maxHz, so a slow ACK made playback crawl and stutter.
        const followStep = advanceFollowClockAfterAck(sentSec, sentNsec, followLastAckWallMs);
        followLastAckWallMs = followStep.ackWallMs;
        const nextTime = followStep.time;

        // Stop at the end of the replay so the user can scrub the timeline again. Park the loop
        // (no more seeks) and do one final seek to the exact end.
        if (lastEndTime != undefined && !isBeforeTime(nextTime, lastEndTime)) {
          parkFollowAtEnd(lastEndTime);
          return;
        }
        followClockSec = nextTime.sec;
        followClockNsec = nextTime.nsec;

        // Keep the loop alive FIRST, so a throwing/slow seekPlayback cannot stall the cursor stream.
        scheduleFollowPump();
        // Throttled best-effort UI catch-up to the position Unity just received.
        seekUi(sentSec, sentNsec, false);
        return;
      }
      if (delivered) {
        // Unity rejected the cursor (auth/validation). Retrying the same step would just spam, so
        // stop and surface the status until the user fixes it or toggles follow off and on.
        stopFollow();
        return;
      }
      // Stall or network failure: retry the same followClock step.
      scheduleFollowPump();
    });
  }

  function maybeStartFollow(): void {
    if (!mounted || !state.enabled || !state.followUnity || !canFollow || followActive || followReachedEnd || lastRenderSec < 0) {
      return;
    }
    followActive = true;
    followClockSec = lastRenderSec;
    followClockNsec = lastRenderNsec;
    followLastAckWallMs = Date.now();
    pumpFollow();
  }

  panel.enabledInput.addEventListener("change", () => {
    state = { ...state, enabled: panel.enabledInput.checked };
    if (!state.enabled) {
      pendingCursor = undefined;
      stopFollow();
    } else if (state.followUnity) {
      maybeStartFollow();
    }
    savePanelState(context, state);
  }, listenerOptions);

  panel.endpointInput.addEventListener("change", () => {
    state = { ...state, endpoint: normalizeEndpoint(panel.endpointInput.value) };
    panel.endpointInput.value = state.endpoint;
    updateTokenWarning();
    savePanelState(context, state);
  }, listenerOptions);

  panel.tokenInput.addEventListener("change", () => {
    state = { ...state, token: panel.tokenInput.value };
    updateTokenWarning();
  }, listenerOptions);

  panel.maxHzInput.addEventListener("change", () => {
    const parsed = Number.parseFloat(panel.maxHzInput.value);
    const maxHz = Number.isFinite(parsed) && parsed > 0 ? parsed : DEFAULT_MAX_HZ;
    state = { ...state, maxHz };
    minIntervalMs = 1000 / state.maxHz;
    panel.maxHzInput.value = String(state.maxHz);
    savePanelState(context, state);
  }, listenerOptions);

  panel.followInput?.addEventListener("change", () => {
    const follow = panel.followInput;
    if (follow == undefined) {
      return;
    }
    state = { ...state, followUnity: follow.checked };
    pendingCursor = undefined;
    savePanelState(context, state);
    if (state.followUnity) {
      followReachedEnd = false; // a fresh enable should follow from the current position again
      maybeStartFollow();
    } else {
      stopFollow();
    }
  }, listenerOptions);

  context.panelElement.replaceChildren(panel.root);

  context.watch("currentTime");
  context.watch("startTime");
  context.watch("endTime");
  context.watch("didSeek");

  context.onRender = (renderState, done) => {
    try {
      const currentTime = renderState.currentTime;
      if (panel.enabledInput.checked !== state.enabled) {
        panel.enabledInput.checked = state.enabled;
      }
      // While following, the panel-owned clock is the source of truth (the Foxglove playhead may
      // stay frozen if the host ignores programmatic seeks), so show that instead — it lets the
      // user confirm the loop is actually advancing without reading the Unity console.
      const displayTime =
        followActive && followClockSec >= 0 ? { sec: followClockSec, nsec: followClockNsec } : currentTime;
      panel.replayTime.textContent = formatReplayTimeUtc(displayTime, replayTimeCache);
      panel.unityStatus.textContent = status.message;
      panel.unityStatus.classList.toggle("ok", status.ok);
      panel.unityStatus.classList.toggle("error", !status.ok);

      if (currentTime != undefined) {
        lastRenderSec = currentTime.sec;
        lastRenderNsec = currentTime.nsec;
      }
      lastStartTime = cloneTimeIfChanged(lastStartTime, renderState.startTime);
      lastEndTime = cloneTimeIfChanged(lastEndTime, renderState.endTime);

      resumeFollowFromScrubIfNeeded(renderState, currentTime);

      if (state.enabled && state.followUnity && canFollow && !followReachedEnd) {
        // Stage 3: the self-clocked pump owns sending while following; just keep it running.
        maybeStartFollow();
      } else {
        // Follow off, unavailable, or parked at the end: plain currentTime-driven sync. While
        // parked this lets the user scrub freely (each scrub syncs Unity once) without the loop
        // running away — re-check Follow to resume Unity-paced playback.
        const nowMs = Date.now();
        if (inFlight) {
          if (state.enabled && currentTime != undefined) {
            queueLatestCursorIfNeeded(currentTime, renderState);
          }
        } else if (
          shouldSendCursor(
            state.enabled,
            currentTime,
            lastCursorSec,
            lastCursorNsec,
            lastSentAtMs,
            nowMs,
            minIntervalMs,
            renderState.didSeek === true,
          )
        ) {
          const payload = buildPayload(renderState, sequence + 1);
          if (payload != undefined && currentTime != undefined) {
            sequence = payload.sequence;
            const sentSec = currentTime.sec;
            const sentNsec = currentTime.nsec;
            dispatchCursor(payload, (_ok, delivered) => {
              // De-dupe on any delivered response (2xx or 4xx) so a rejection does not resend at
              // full rate; only a stall/network failure leaves the cursor so the next render retries.
              if (delivered) {
                lastCursorSec = sentSec;
                lastCursorNsec = sentNsec;
              }
            });
          }
        }
      }
    } finally {
      done();
    }
  };

  return () => {
    mounted = false;
    listenerController.abort();
    stopFollow();
    pendingCursor = undefined;
    inFlightCursor = undefined;
    if (cursorTimeout != undefined) {
      clearTimeout(cursorTimeout);
      cursorTimeout = undefined;
    }
    cursorController?.abort();
  };
}
