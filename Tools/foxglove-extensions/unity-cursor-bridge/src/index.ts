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
// Forward cursor sync rate. Matched to the typical render rate so Unity receives a
// fresh playback cursor (and advances its scene) roughly every frame instead of
// every 50 ms. 20 Hz made Unity look choppy while Foxglove played smoothly; the
// per-cursor work is cheap scene-only advance, so render-rate sync is affordable.
// This is a clock-sync cadence, not a data-sampling rate: Unity still processes
// every replay message in (lastCursor, currentCursor], so 100 Hz+ topics stay intact.
const DEFAULT_MAX_HZ = 60;

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

function cloneTime(time: Time | undefined): { sec: number; nsec: number } | undefined {
  if (time == undefined) {
    return undefined;
  }

  return { sec: time.sec, nsec: time.nsec };
}

function cursorKey(time: Time): string {
  return `${time.sec}.${time.nsec}`;
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

function formatReplayTimeUtc(time: Time | undefined): string {
  if (time == undefined) {
    return "Waiting for Foxglove playback";
  }

  const milliseconds = (time.sec * 1000) + Math.floor(time.nsec / 1_000_000);
  return new Date(milliseconds).toISOString().replace("T", " ").replace("Z", " UTC");
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
  return { endpoint, token: "", enabled };
}

function savePanelState(context: PanelExtensionContext, state: PanelState): void {
  context.saveState({
    endpoint: state.endpoint,
    enabled: state.enabled,
  });
}

export function shouldSendCursor(
  enabled: boolean,
  currentTime: Time | undefined,
  lastKey: string,
  lastSentAtMs: number,
  nowMs: number,
  minIntervalMs: number,
): boolean {
  if (!enabled || currentTime == undefined) {
    return false;
  }

  return cursorKey(currentTime) !== lastKey && nowMs - lastSentAtMs >= minIntervalMs;
}

function buildPanelDom(state: PanelState): {
  root: HTMLDivElement;
  enabledInput: HTMLInputElement;
  endpointInput: HTMLInputElement;
  tokenInput: HTMLInputElement;
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
  const replayTime = root.querySelector<HTMLSpanElement>("#replay-time");
  const unityStatus = root.querySelector<HTMLSpanElement>("#unity-status");
  if (
    enabledInput == undefined ||
    endpointInput == undefined ||
    tokenInput == undefined ||
    replayTime == undefined ||
    unityStatus == undefined
  ) {
    throw new Error("Unity Replay Sync panel template is missing required elements.");
  }

  enabledInput.checked = state.enabled;
  endpointInput.value = state.endpoint;
  tokenInput.value = state.token;
  return { root, enabledInput, endpointInput, tokenInput, replayTime, unityStatus };
}

export function initPanel(context: PanelExtensionContext): void | (() => void) {
  let state = readPanelState(context.initialState);
  let sequence = 0;
  let lastCursorKey = "";
  let lastSentAtMs = 0;
  let mounted = true;
  let activeCursorController: AbortController | undefined;
  let requestGeneration = 0;
  let status: SendStatus = {
    ok: true,
    message: "Waiting for Foxglove replay time. Keep Unity in Play Mode.",
  };
  const panel = buildPanelDom(state);

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

  context.panelElement.replaceChildren(panel.root);

  context.watch("currentTime");
  context.watch("startTime");
  context.watch("endTime");
  context.watch("didSeek");

  context.onRender = (renderState, done) => {
    try {
      const currentTime = renderState.currentTime;
      panel.enabledInput.checked = state.enabled;
      panel.replayTime.textContent = formatReplayTimeUtc(currentTime);
      panel.unityStatus.textContent = status.message;
      panel.unityStatus.classList.toggle("ok", status.ok);
      panel.unityStatus.classList.toggle("error", !status.ok);

      const nowMs = Date.now();
      const minIntervalMs = 1000 / DEFAULT_MAX_HZ;
      if (shouldSendCursor(state.enabled, currentTime, lastCursorKey, lastSentAtMs, nowMs, minIntervalMs)) {
        const payload = buildPayload(renderState, sequence + 1);
        if (payload != undefined && currentTime != undefined) {
          sequence = payload.sequence;
          lastCursorKey = cursorKey(currentTime);
          lastSentAtMs = nowMs;

          activeCursorController?.abort();
          const controller = new AbortController();
          activeCursorController = controller;
          const generation = ++requestGeneration;

          void sendCursor(state.endpoint, state.token, payload, controller.signal).then(
            (result) => {
              if (mounted && requestGeneration === generation && !controller.signal.aborted) {
                status = result;
              }
            },
            (error: unknown) => {
              if (mounted && requestGeneration === generation && !controller.signal.aborted) {
                status = {
                  ok: false,
                  message: `Cannot reach Unity. Check Play Mode and endpoint. ${String(error)}`,
                };
              }
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
    activeCursorController?.abort();
  };
}

export function activate(extensionContext: ExtensionContext): void {
  extensionContext.registerPanel({
    name: "Unity Replay Sync",
    initPanel,
  });
}
