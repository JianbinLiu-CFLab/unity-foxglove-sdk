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
const DEFAULT_MAX_HZ = 20;

type CursorPayload = {
  source: "foxglove-unity-cursor-bridge";
  sequence: number;
  time: { sec: number; nsec: number };
  mode: "seek";
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

function escapeHtml(value: string): string {
  return value
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}

function formatReplayTimeUtc(time: Time | undefined): string {
  if (time == undefined) {
    return "Waiting for Foxglove playback";
  }

  const milliseconds = (time.sec * 1000) + Math.floor(time.nsec / 1_000_000);
  return new Date(milliseconds).toISOString().replace("T", " ").replace("Z", " UTC");
}

async function sendCursor(endpoint: string, token: string, payload: CursorPayload): Promise<SendStatus> {
  const headers: Record<string, string> = { "Content-Type": "application/json" };
  if (token.length > 0) {
    headers.Authorization = `Bearer ${token}`;
  }

  const response = await fetch(endpoint, {
    method: "POST",
    headers,
    body: JSON.stringify(payload),
  });
  const responseText = await response.text();
  return {
    ok: response.ok,
    message: response.ok
      ? "Unity is following Foxglove"
      : `Unity rejected replay time (HTTP ${response.status}): ${responseText}`,
  };
}

function buildPayload(renderState: CursorRenderState, sequence: number): CursorPayload | undefined {
  const currentTime = renderState.currentTime;
  if (currentTime == undefined) {
    return undefined;
  }

  return {
    source: "foxglove-unity-cursor-bridge",
    sequence,
    time: { sec: currentTime.sec, nsec: currentTime.nsec },
    mode: "seek",
    didSeek: renderState.didSeek === true,
    startTime: cloneTime(renderState.startTime),
    endTime: cloneTime(renderState.endTime),
  };
}

function initPanel(context: PanelExtensionContext): void {
  let state: PanelState = {
    endpoint: DEFAULT_ENDPOINT,
    token: "",
    enabled: true,
  };
  let sequence = 0;
  let lastCursorKey = "";
  let lastSentAtMs = 0;
  let status: SendStatus = {
    ok: true,
    message: "Waiting for Foxglove replay time. Keep Unity in Play Mode.",
  };

  context.watch("currentTime");
  context.watch("startTime");
  context.watch("endTime");
  context.watch("didSeek");

  context.onRender = (renderState, done) => {
    try {
      const currentTime = renderState.currentTime;
      const root = document.createElement("div");
      const statusClass = status.ok ? "ok" : "error";
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
            <input id="enabled" type="checkbox" ${state.enabled ? "checked" : ""} />
            <span>Sync Foxglove timeline to Unity</span>
          </label>
          <div class="bridge-field">
            <label for="endpoint">Unity endpoint</label>
            <input id="endpoint" value="${escapeHtml(state.endpoint)}" />
          </div>
          <div class="bridge-field">
            <label for="token">Access token (optional)</label>
            <input id="token" type="password" value="${escapeHtml(state.token)}" />
          </div>
          <div class="bridge-readout">
            <div class="bridge-row">
              <span class="bridge-label">Replay time (UTC)</span>
              <span class="bridge-value">${formatReplayTimeUtc(currentTime)}</span>
            </div>
            <div class="bridge-row">
              <span class="bridge-label">Unity status</span>
              <span class="bridge-status ${statusClass}">${escapeHtml(status.message)}</span>
            </div>
          </div>
        </div>
      `;

      const enabled = root.querySelector<HTMLInputElement>("#enabled");
      enabled?.addEventListener("change", () => {
        state = { ...state, enabled: enabled.checked };
      });

      const endpoint = root.querySelector<HTMLInputElement>("#endpoint");
      endpoint?.addEventListener("change", () => {
        state = { ...state, endpoint: endpoint.value.trim() || DEFAULT_ENDPOINT };
      });

      const token = root.querySelector<HTMLInputElement>("#token");
      token?.addEventListener("change", () => {
        state = { ...state, token: token.value };
      });

      context.panelElement.replaceChildren(root);

      if (state.enabled && currentTime != undefined) {
        const key = cursorKey(currentTime);
        const nowMs = Date.now();
        const minIntervalMs = 1000 / DEFAULT_MAX_HZ;
        if (key !== lastCursorKey && nowMs - lastSentAtMs >= minIntervalMs) {
          lastCursorKey = key;
          lastSentAtMs = nowMs;
          sequence++;

          const payload = buildPayload(renderState, sequence);
          if (payload != undefined) {
            void sendCursor(state.endpoint, state.token, payload).then(
              (result) => {
                status = result;
              },
              (error: unknown) => {
                status = {
                  ok: false,
                  message: `Cannot reach Unity. Check Play Mode and endpoint. ${String(error)}`,
                };
              },
            );
          }
        }
      }
    } finally {
      done();
    }
  };
}

export function activate(extensionContext: ExtensionContext): void {
  extensionContext.registerPanel({
    name: "Unity Replay Sync",
    initPanel,
  });
}
