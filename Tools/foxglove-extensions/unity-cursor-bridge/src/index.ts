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

export function buildPayload(
  renderState: CursorRenderState,
  sequence: number,
  suppressSeekEcho = false,
): CursorPayload | undefined {
  const currentTime = renderState.currentTime;
  if (currentTime == undefined) {
    return undefined;
  }

  // Stage 3 echo suppression (140K): a programmatic forward seekPlayback step shows up on the
  // next render as didSeek=true. Relabel that single echo as "advance" so Unity stays on its
  // cheap forward path instead of taking the expensive latest-at snapshot. Real user seeks are
  // not echoes and keep mode "seek".
  const mode = renderState.didSeek === true ? "seek" : "advance";
  return {
    source: "foxglove-unity-cursor-bridge",
    sequence,
    time: { sec: currentTime.sec, nsec: currentTime.nsec },
    mode: suppressSeekEcho ? "advance" : mode,
    didSeek: suppressSeekEcho ? false : renderState.didSeek === true,
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
  // Stage 2 (140K): at most one cursor POST outstanding. The forward path waits for Unity's
  // 202 ACK before sending the next cursor, so Foxglove's POST cadence adapts to Unity's
  // processing speed instead of aborting and re-flooding.
  let inFlight = false;
  // Retained only so cleanup can abort an outstanding request on unmount.
  let cursorController: AbortController | undefined;
  // Stage 3 (140K): time of the last programmatic seekPlayback step, used to recognize and
  // suppress its didSeek echo. -1/-1 means "no echo pending".
  let pendingSeekEchoSec = -1;
  let pendingSeekEchoNsec = -1;

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
    if (follow != undefined) {
      state = { ...state, followUnity: follow.checked };
      savePanelState(context, state);
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
      panel.replayTime.textContent = formatReplayTimeUtc(currentTime, replayTimeCache);
      panel.unityStatus.textContent = status.message;
      panel.unityStatus.classList.toggle("ok", status.ok);
      panel.unityStatus.classList.toggle("error", !status.ok);

      const nowMs = Date.now();
      const minIntervalMs = 1000 / state.maxHz;
      if (
        !inFlight &&
        shouldSendCursor(state.enabled, currentTime, lastCursorSec, lastCursorNsec, lastSentAtMs, nowMs, minIntervalMs)
      ) {
        const isSeekEcho =
          currentTime != undefined &&
          currentTime.sec === pendingSeekEchoSec &&
          currentTime.nsec === pendingSeekEchoNsec;
        const payload = buildPayload(renderState, sequence + 1, isSeekEcho);
        if (payload != undefined && currentTime != undefined) {
          sequence = payload.sequence;
          lastCursorSec = currentTime.sec;
          lastCursorNsec = currentTime.nsec;
          lastSentAtMs = nowMs;
          if (isSeekEcho) {
            pendingSeekEchoSec = -1;
            pendingSeekEchoNsec = -1;
          }

          inFlight = true;
          const controller = new AbortController();
          cursorController = controller;
          const sentSec = payload.time.sec;
          const sentNsec = payload.time.nsec;

          void sendCursor(state.endpoint, state.token, payload, controller.signal).then(
            (result) => {
              if (!mounted || controller.signal.aborted) {
                return;
              }
              inFlight = false;
              status = result;
              // Stage 3: ACK-paced forward step. Only after Unity accepts the cursor do we
              // advance Foxglove forward by one rate step (< 500 ms => Unity's cheap forward
              // path, never backward). The resulting render sends the next cursor, so Unity's
              // ACK latency sets the pace and Foxglove can never outrun it.
              if (state.followUnity && result.ok && seekPlayback != undefined) {
                const stepMs = 1000 / state.maxHz;
                const totalNsec = sentNsec + Math.round(stepMs * 1_000_000);
                const target: Time = {
                  sec: sentSec + Math.floor(totalNsec / 1_000_000_000),
                  nsec: totalNsec % 1_000_000_000,
                };
                pendingSeekEchoSec = target.sec;
                pendingSeekEchoNsec = target.nsec;
                seekPlayback(target);
              }
            },
            (error: unknown) => {
              if (!mounted || controller.signal.aborted) {
                return;
              }
              inFlight = false;
              status = {
                ok: false,
                message: `Cannot reach Unity. Check Play Mode and endpoint. ${String(error)}`,
              };
            },
          );
        }
      }
    } finally {
      done();
    }
  };

  return () => {
    mounted = false;
    cursorController?.abort();
  };
}

export function activate(extensionContext: ExtensionContext): void {
  extensionContext.registerPanel({
    name: "Unity Replay Sync",
    initPanel,
  });
}
