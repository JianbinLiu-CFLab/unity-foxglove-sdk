// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/foxglove-extensions/unity-cursor-bridge
// Purpose: Minimal Foxglove panel for Foxglove-owned Unity replay cursor synchronization.

import {
  ExtensionContext,
  PanelExtensionContext,
  Time,
} from "@foxglove/extension";

const DEFAULT_ENDPOINT = "http://127.0.0.1:8892/v1/replay-cursor";
// Default forward cursor sync rate. Matched to the typical render rate so Unity receives a
// fresh playback cursor (and advances its scene) roughly every frame instead of
// every 50 ms. 20 Hz made Unity look choppy while Foxglove played smoothly; the
// per-cursor work is cheap scene-only advance, so render-rate sync is affordable.
// This is a clock-sync cadence, not a data-sampling rate: Unity still processes
// every replay message in (lastCursor, currentCursor], so 100 Hz+ topics stay intact.
// Stage 1 (140K): this is the default for the user-configurable panel rate; the effective
// interval is derived per-render from state.maxHz.
const DEFAULT_MAX_HZ = 60;
// Stage 2 (140K): if a cursor POST neither resolves nor rejects within this window (Unity
// stalled, half-open port, browser network-stack hiccup), abort it so in-flight backpressure
// cannot wedge the panel into a permanent "no more cursors" state. The next render retries.
const REQUEST_TIMEOUT_MS = 2000;
// Stage 3 (140K): max replay time a single follow step may advance. Kept under Unity's 500 ms
// external-cursor seek threshold so every step stays on the cheap forward-advance path. It also
// bounds catch-up after a stall: instead of one huge jump, follow advances at most this much per
// step and lets real time re-accumulate.
const MAX_FOLLOW_STEP_MS = 400;
// Stage 3 (140K): the cursor stream to Unity runs at the full cursor rate, but seekPlayback is a
// "jump" (Foxglove reloads the frame at the target time), so calling it every cursor strobes the
// Foxglove panels (point clouds flicker). Throttle the UI catch-up seek to this interval; Unity
// stays smooth, the Foxglove UI just refreshes a few times per second.
const SEEK_UI_INTERVAL_MS = 200;
const WAITING_REPLAY_TIME_TEXT = "Waiting for Foxglove playback";

// Stage 3 (140K): seekPlayback is an undocumented PanelExtensionContext method reached via
// cast. When present it lets the panel advance the Foxglove timeline forward-only at Unity's
// ACK pace ("Follow Unity replay"). Its presence is feature-detected, not assumed.
type MaybeSeekable = { seekPlayback?: (time: Time) => void };

type CursorPayload = {
  source: "foxglove-unity-cursor-bridge";
  sequence: number;
  time: { sec: number; nsec: number };
  mode: "seek" | "advance";
  didSeek: boolean;
  startTime?: { sec: number; nsec: number };
  endTime?: { sec: number; nsec: number };
};

type PanelState = {
  endpoint: string;
  token: string;
  enabled: boolean;
  // Stage 1 (140K): user-configurable cursor rate (persisted), default DEFAULT_MAX_HZ.
  maxHz: number;
  // Stage 3 (140K): opt-in forward-only ACK-paced follow (persisted), default off.
  followUnity: boolean;
};

type SendStatus = {
  message: string;
  ok: boolean;
};

type CursorRenderState = {
  readonly currentTime?: Time;
  readonly didSeek?: boolean;
  readonly startTime?: Time;
  readonly endTime?: Time;
};

type ReplayTimeDisplayCache = {
  lastSec: number | undefined;
  lastNsec: number | undefined;
  text: string;
};

function cloneTime(time: Time | undefined): { sec: number; nsec: number } | undefined {
  if (time == undefined) {
    return undefined;
  }

  return { sec: time.sec, nsec: time.nsec };
}

export function escapeHtml(value: string): string {
  return value
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}

export function summarizeResponseText(responseText: string, maxLength = 200): string {
  if (responseText.length <= maxLength) {
    return responseText;
  }

  return `${responseText.slice(0, maxLength)}…`;
}

function formatReplayTimeUtc(time: Time | undefined, cache: ReplayTimeDisplayCache): string {
  if (time == undefined) {
    if (cache.lastSec !== undefined || cache.lastNsec !== undefined || cache.text.length === 0) {
      cache.lastSec = undefined;
      cache.lastNsec = undefined;
      cache.text = WAITING_REPLAY_TIME_TEXT;
    }
    return cache.text;
  }

  if (cache.lastSec === time.sec && cache.lastNsec === time.nsec) {
    return cache.text;
  }

  const milliseconds = (time.sec * 1000) + Math.floor(time.nsec / 1_000_000);
  const iso = new Date(milliseconds).toISOString();
  cache.lastSec = time.sec;
  cache.lastNsec = time.nsec;
  cache.text = `${iso.slice(0, 10)} ${iso.slice(11, iso.length - 1)} UTC`;
  return cache.text;
}

async function sendCursor(
  endpoint: string,
  token: string,
  payload: CursorPayload,
  signal?: AbortSignal,
): Promise<SendStatus> {
  const headers: Record<string, string> = { "Content-Type": "application/json" };
  if (token.length > 0) {
    headers.Authorization = `Bearer ${token}`;
  }

  const response = await fetch(endpoint, {
    method: "POST",
    headers,
    body: JSON.stringify(payload),
    signal,
  });
  const responseText = await response.text();
  return {
    ok: response.ok,
    message: response.ok
      ? "Unity is following Foxglove"
      : `Unity rejected replay time (HTTP ${response.status}): ${summarizeResponseText(responseText)}`,
  };
}

export function buildPayload(renderState: CursorRenderState, sequence: number): CursorPayload | undefined {
  const currentTime = renderState.currentTime;
  if (currentTime == undefined) {
    return undefined;
  }

  return {
    source: "foxglove-unity-cursor-bridge",
    sequence,
    time: { sec: currentTime.sec, nsec: currentTime.nsec },
    mode: renderState.didSeek === true ? "seek" : "advance",
    didSeek: renderState.didSeek === true,
    startTime: cloneTime(renderState.startTime),
    endTime: cloneTime(renderState.endTime),
  };
}

export function readPanelState(initialState: unknown): PanelState {
  const defaults: PanelState = {
    endpoint: DEFAULT_ENDPOINT,
    token: "",
    enabled: true,
    maxHz: DEFAULT_MAX_HZ,
    followUnity: false,
  };

  if (initialState == undefined || typeof initialState !== "object") {
    return defaults;
  }

  const stored = initialState as Partial<PanelState>;
  const endpoint =
    typeof stored.endpoint === "string" && stored.endpoint.trim().length > 0
      ? stored.endpoint.trim()
      : DEFAULT_ENDPOINT;
  const enabled = typeof stored.enabled === "boolean" ? stored.enabled : defaults.enabled;
  const maxHz =
    typeof stored.maxHz === "number" && isFinite(stored.maxHz) && stored.maxHz > 0
      ? stored.maxHz
      : defaults.maxHz;
  const followUnity =
    typeof stored.followUnity === "boolean" ? stored.followUnity : defaults.followUnity;
  return { endpoint, token: "", enabled, maxHz, followUnity };
}

function savePanelState(context: PanelExtensionContext, state: PanelState): void {
  context.saveState({
    endpoint: state.endpoint,
    enabled: state.enabled,
    maxHz: state.maxHz,
    followUnity: state.followUnity,
  });
}

export function shouldSendCursor(
  enabled: boolean,
  currentTime: Time | undefined,
  lastSec: number,
  lastNsec: number,
  lastSentAtMs: number,
  nowMs: number,
  minIntervalMs: number,
): boolean {
  if (!enabled || currentTime == undefined) {
    return false;
  }

  return (currentTime.sec !== lastSec || currentTime.nsec !== lastNsec) && nowMs - lastSentAtMs >= minIntervalMs;
}

function buildPanelDom(state: PanelState, canFollow: boolean): {
  root: HTMLDivElement;
  enabledInput: HTMLInputElement;
  endpointInput: HTMLInputElement;
  tokenInput: HTMLInputElement;
  maxHzInput: HTMLInputElement;
  followInput: HTMLInputElement | undefined;
  replayTime: HTMLSpanElement;
  unityStatus: HTMLSpanElement;
} {
  const root = document.createElement("div");
  root.innerHTML = `
    <style>
      .bridge-panel {
        box-sizing: border-box;
        color: #f3f4f6;
        display: grid;
        font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
        gap: 14px;
        line-height: 1.35;
        padding: 14px;
      }

      .bridge-sync {
        align-items: center;
        border: 1px solid #343942;
        border-radius: 6px;
        display: grid;
        gap: 10px;
        grid-template-columns: auto 1fr;
        padding: 10px;
      }

      .bridge-sync input {
        height: 16px;
        margin: 0;
        width: 16px;
      }

      .bridge-sync span {
        font-size: 14px;
        font-weight: 600;
      }

      .bridge-field {
        display: grid;
        gap: 6px;
      }

      .bridge-field label {
        color: #d1d5db;
        font-size: 12px;
        font-weight: 600;
      }

      .bridge-field input {
        background: #111318;
        border: 1px solid #3a404a;
        border-radius: 4px;
        box-sizing: border-box;
        color: #f9fafb;
        font: 13px ui-monospace, "SFMono-Regular", Consolas, monospace;
        min-width: 0;
        padding: 7px 8px;
        width: 100%;
      }

      .bridge-readout {
        display: grid;
        gap: 8px;
      }

      .bridge-row {
        align-items: start;
        display: grid;
        gap: 8px;
        grid-template-columns: 88px 1fr;
      }

      .bridge-label {
        color: #aeb4bd;
        font-size: 12px;
        font-weight: 600;
      }

      .bridge-value {
        color: #f3f4f6;
        font: 12px ui-monospace, "SFMono-Regular", Consolas, monospace;
        min-width: 0;
        overflow-wrap: anywhere;
      }

      .bridge-status {
        border-radius: 4px;
        display: inline;
        font: 12px ui-monospace, "SFMono-Regular", Consolas, monospace;
        overflow-wrap: anywhere;
      }

      .bridge-status.ok {
        color: #22c55e;
      }

      .bridge-status.error {
        color: #f87171;
      }
    </style>
    <div class="bridge-panel">
      <label class="bridge-sync">
        <input id="enabled" type="checkbox" />
        <span>Sync Foxglove timeline to Unity</span>
      </label>
      <div class="bridge-field">
        <label for="endpoint">Unity endpoint</label>
        <input id="endpoint" />
      </div>
      <div class="bridge-field">
        <label for="token">Access token (optional)</label>
        <input id="token" type="password" />
      </div>
      <div class="bridge-field">
        <label for="maxhz">Cursor rate (Hz)</label>
        <input id="maxhz" type="number" min="1" max="120" step="1" />
      </div>
      ${canFollow ? `
      <label class="bridge-sync">
        <input id="follow" type="checkbox" />
        <span>Follow Unity replay</span>
      </label>` : ""}
      <div class="bridge-readout">
        <div class="bridge-row">
          <span class="bridge-label">Replay time (UTC)</span>
          <span id="replay-time" class="bridge-value"></span>
        </div>
        <div class="bridge-row">
          <span class="bridge-label">Unity status</span>
          <span id="unity-status" class="bridge-status"></span>
        </div>
      </div>
    </div>
  `;

  const enabledInput = root.querySelector<HTMLInputElement>("#enabled");
  const endpointInput = root.querySelector<HTMLInputElement>("#endpoint");
  const tokenInput = root.querySelector<HTMLInputElement>("#token");
  const maxHzInput = root.querySelector<HTMLInputElement>("#maxhz");
  const followInput = canFollow ? root.querySelector<HTMLInputElement>("#follow") ?? undefined : undefined;
  const replayTime = root.querySelector<HTMLSpanElement>("#replay-time");
  const unityStatus = root.querySelector<HTMLSpanElement>("#unity-status");
  if (
    enabledInput == undefined ||
    endpointInput == undefined ||
    tokenInput == undefined ||
    maxHzInput == undefined ||
    replayTime == undefined ||
    unityStatus == undefined ||
    (canFollow && followInput == undefined)
  ) {
    throw new Error("Unity Replay Sync panel template is missing required elements.");
  }

  enabledInput.checked = state.enabled;
  endpointInput.value = state.endpoint;
  tokenInput.value = state.token;
  maxHzInput.value = String(state.maxHz);
  if (followInput != undefined) {
    followInput.checked = state.followUnity;
  }
  return { root, enabledInput, endpointInput, tokenInput, maxHzInput, followInput, replayTime, unityStatus };
}

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
  // Set once follow advances to the end of the replay. The loop parks and the panel falls back to
  // plain currentTime-driven sync, so the user can scrub freely; re-checking Follow resumes it.
  let followReachedEnd = false;
  // Some hosts reject a programmatic seek while paused (it throws). seekPlayback is best-effort UI
  // only; after the first failure we stop calling it but keep pumping the cursor stream to Unity.
  let seekUsable = true;
  let lastRenderSec = -1;
  let lastRenderNsec = -1;
  let lastStartTime: { sec: number; nsec: number } | undefined;
  let lastEndTime: { sec: number; nsec: number } | undefined;

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

    // Stage 2 stall guard: if neither handler fires within REQUEST_TIMEOUT_MS, abort and release
    // in-flight so the panel keeps trying instead of wedging forever.
    cursorTimeout = setTimeout(() => {
      cursorTimeout = undefined;
      if (!mounted || cursorController !== controller) {
        return;
      }
      controller.abort();
      inFlight = false;
      status = { ok: false, message: `Unity did not respond within ${REQUEST_TIMEOUT_MS} ms. Retrying.` };
      onSettled(false, false);
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
        status = result;
        onSettled(result.ok, true);
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
        status = { ok: false, message: `Cannot reach Unity. Check Play Mode and endpoint. ${String(error)}` };
        onSettled(false, false);
      },
    );
  }

  function scheduleFollowPump(): void {
    if (followPumpHandle != undefined) {
      return;
    }
    const delay = Math.max(0, 1000 / state.maxHz - (Date.now() - lastSentAtMs));
    followPumpHandle = setTimeout(() => {
      followPumpHandle = undefined;
      pumpFollow();
    }, delay);
  }

  function pumpFollow(): void {
    if (!mounted || !state.followUnity || seekPlayback == undefined) {
      stopFollow();
      return;
    }
    if (inFlight) {
      scheduleFollowPump();
      return;
    }

    const sentSec = followClockSec;
    const sentNsec = followClockNsec;
    sequence += 1;
    const payload: CursorPayload = {
      source: "foxglove-unity-cursor-bridge",
      sequence,
      time: { sec: sentSec, nsec: sentNsec },
      mode: "advance",
      didSeek: false,
      startTime: lastStartTime != undefined ? { ...lastStartTime } : undefined,
      endTime: lastEndTime != undefined ? { ...lastEndTime } : undefined,
    };

    dispatchCursor(payload, (ok, delivered) => {
      if (!mounted || !state.followUnity) {
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
        const nowWall = Date.now();
        let deltaMs = followLastAckWallMs >= 0 ? nowWall - followLastAckWallMs : 0;
        if (deltaMs < 0) {
          deltaMs = 0;
        }
        if (deltaMs > MAX_FOLLOW_STEP_MS) {
          deltaMs = MAX_FOLLOW_STEP_MS;
        }
        followLastAckWallMs = nowWall;
        const totalNsec = sentNsec + Math.round(deltaMs * 1_000_000);
        let nextSec = sentSec + Math.floor(totalNsec / 1_000_000_000);
        let nextNsec = totalNsec % 1_000_000_000;

        // Stop at the end of the replay so the user can scrub the timeline again. Park the loop
        // (no more seeks) and do one final seek to the exact end.
        if (lastEndTime != undefined) {
          const endNs = lastEndTime.sec * 1_000_000_000 + lastEndTime.nsec;
          if (nextSec * 1_000_000_000 + nextNsec >= endNs) {
            nextSec = lastEndTime.sec;
            nextNsec = lastEndTime.nsec;
            followClockSec = nextSec;
            followClockNsec = nextNsec;
            seekUi(nextSec, nextNsec, true);
            followReachedEnd = true;
            stopFollow();
            status = { ok: true, message: "Reached end of replay. Re-check Follow Unity replay to follow again." };
            return;
          }
        }
        followClockSec = nextSec;
        followClockNsec = nextNsec;

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
    if (!mounted || !state.followUnity || !canFollow || followActive || followReachedEnd || lastRenderSec < 0) {
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
    savePanelState(context, state);
  });

  panel.endpointInput.addEventListener("change", () => {
    state = { ...state, endpoint: panel.endpointInput.value.trim() || DEFAULT_ENDPOINT };
    panel.endpointInput.value = state.endpoint;
    savePanelState(context, state);
  });

  panel.tokenInput.addEventListener("change", () => {
    state = { ...state, token: panel.tokenInput.value };
  });

  panel.maxHzInput.addEventListener("change", () => {
    const parsed = Number.parseFloat(panel.maxHzInput.value);
    const maxHz = Number.isFinite(parsed) && parsed > 0 ? parsed : DEFAULT_MAX_HZ;
    state = { ...state, maxHz };
    panel.maxHzInput.value = String(state.maxHz);
    savePanelState(context, state);
  });

  panel.followInput?.addEventListener("change", () => {
    const follow = panel.followInput;
    if (follow == undefined) {
      return;
    }
    state = { ...state, followUnity: follow.checked };
    savePanelState(context, state);
    if (state.followUnity) {
      followReachedEnd = false; // a fresh enable should follow from the current position again
      maybeStartFollow();
    } else {
      stopFollow();
    }
  });

  context.panelElement.replaceChildren(panel.root);

  context.watch("currentTime");
  context.watch("startTime");
  context.watch("endTime");
  context.watch("didSeek");

  context.onRender = (renderState, done) => {
    try {
      const currentTime = renderState.currentTime;
      panel.enabledInput.checked = state.enabled;
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
      lastStartTime = cloneTime(renderState.startTime);
      lastEndTime = cloneTime(renderState.endTime);

      if (state.followUnity && canFollow && !followReachedEnd) {
        // Stage 3: the self-clocked pump owns sending while following; just keep it running.
        maybeStartFollow();
      } else {
        // Follow off, unavailable, or parked at the end: plain currentTime-driven sync. While
        // parked this lets the user scrub freely (each scrub syncs Unity once) without the loop
        // running away — re-check Follow to resume Unity-paced playback.
        const nowMs = Date.now();
        const minIntervalMs = 1000 / state.maxHz;
        if (
          !inFlight &&
          shouldSendCursor(state.enabled, currentTime, lastCursorSec, lastCursorNsec, lastSentAtMs, nowMs, minIntervalMs)
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
    stopFollow();
    if (cursorTimeout != undefined) {
      clearTimeout(cursorTimeout);
      cursorTimeout = undefined;
    }
    cursorController?.abort();
  };
}

export function activate(extensionContext: ExtensionContext): void {
  extensionContext.registerPanel({
    name: "Unity Replay Sync",
    initPanel,
  });
}
