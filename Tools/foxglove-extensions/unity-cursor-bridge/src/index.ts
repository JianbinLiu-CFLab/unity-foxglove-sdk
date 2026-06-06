// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/foxglove-extensions/unity-cursor-bridge
// Purpose: Minimal Foxglove panel for bidirectional Unity replay cursor synchronization.

import {
  ExtensionContext,
  PanelExtensionContext,
  RenderState,
  Time,
} from "@foxglove/extension";

const DEFAULT_ENDPOINT = "http://127.0.0.1:8892/v1/replay-cursor";
const DEFAULT_MAX_HZ = 20;
const FOLLOW_POLL_INTERVAL_MS = 1000 / DEFAULT_MAX_HZ;
const ECHO_SUPPRESS_MS = 250;

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
  followUnity: boolean;
};

type SendStatus = {
  message: string;
  ok: boolean;
};

type UnityReplayState = {
  available: boolean;
  replayEnabled: boolean;
  playbackEnabled: boolean;
  playing: boolean;
  ended: boolean;
  speed: number;
  time: { sec: number; nsec: number };
  startTime?: { sec: number; nsec: number };
  endTime?: { sec: number; nsec: number };
  message?: string;
};

type SeekPlaybackContext = PanelExtensionContext & {
  seekPlayback?: (time: Time) => void | Promise<void>;
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
      ? `Sent sequence ${payload.sequence} to Unity (${response.status})`
      : `Unity rejected sequence ${payload.sequence} (${response.status}): ${responseText}`,
  };
}

async function fetchUnityState(endpoint: string, token: string): Promise<UnityReplayState> {
  const headers: Record<string, string> = {};
  if (token.length > 0) {
    headers.Authorization = `Bearer ${token}`;
  }

  const response = await fetch(endpoint, {
    method: "GET",
    headers,
  });
  const responseText = await response.text();
  if (!response.ok) {
    throw new Error(`Unity state rejected (${response.status}): ${responseText}`);
  }

  return JSON.parse(responseText) as UnityReplayState;
}

function buildPayload(renderState: RenderState, sequence: number): CursorPayload | undefined {
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
    enabled: false,
    followUnity: false,
  };
  let sequence = 0;
  let lastCursorKey = "";
  let lastFollowedKey = "";
  let lastSentAtMs = 0;
  let lastPollAtMs = 0;
  let suppressForwardUntilMs = 0;
  let status: SendStatus = {
    ok: true,
    message: "Disabled by default. Enable a direction only while Unity replay is loaded.",
  };

  context.watch("currentTime");
  context.watch("startTime");
  context.watch("endTime");
  context.watch("didSeek");

  context.onRender = (renderState, done) => {
    try {
      const currentTime = renderState.currentTime;
      const root = document.createElement("div");
      root.style.fontFamily = "sans-serif";
      root.style.padding = "12px";
      root.innerHTML = `
        <h3>Unity Cursor Bridge</h3>
        <label>
          <input id="enabled" type="checkbox" ${state.enabled ? "checked" : ""} />
          Forward Foxglove currentTime to Unity
        </label>
        <br />
        <label>
          <input id="followUnity" type="checkbox" ${state.followUnity ? "checked" : ""} />
          Follow Unity replay in Foxglove
        </label>
        <p>Endpoint</p>
        <input id="endpoint" style="width: 100%" value="${state.endpoint}" />
        <p>Bearer token (optional)</p>
        <input id="token" style="width: 100%" type="password" value="${state.token}" />
        <p>Current time: ${currentTime ? `${currentTime.sec}.${currentTime.nsec}` : "none"}</p>
        <p>Status: <span style="color: ${status.ok ? "#2b8a3e" : "#c92a2a"}">${status.message}</span></p>
      `;

      const enabled = root.querySelector<HTMLInputElement>("#enabled");
      enabled?.addEventListener("change", () => {
        state = { ...state, enabled: enabled.checked };
      });

      const followUnity = root.querySelector<HTMLInputElement>("#followUnity");
      followUnity?.addEventListener("change", () => {
        state = { ...state, followUnity: followUnity.checked };
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

      if (state.followUnity) {
        const nowMs = Date.now();
        if (nowMs - lastPollAtMs >= FOLLOW_POLL_INTERVAL_MS) {
          lastPollAtMs = nowMs;
          void fetchUnityState(state.endpoint, state.token).then(
            async (unityState) => {
              if (!unityState.available) {
                status = {
                  ok: false,
                  message: unityState.message ?? "Unity replay cursor state is not available.",
                };
                return;
              }

              const key = cursorKey(unityState.time);
              if (key === lastFollowedKey) {
                status = {
                  ok: true,
                  message: `Following Unity at ${key}`,
                };
                return;
              }

              const seekPlayback = (context as SeekPlaybackContext).seekPlayback;
              if (seekPlayback == undefined) {
                status = {
                  ok: false,
                  message: "Foxglove extension API does not expose seekPlayback in this build.",
                };
                return;
              }

              lastFollowedKey = key;
              suppressForwardUntilMs = Date.now() + ECHO_SUPPRESS_MS;
              await seekPlayback(unityState.time);
              status = {
                ok: true,
                message: `Moved Foxglove to Unity replay time ${key}`,
              };
            },
            (error: unknown) => {
              status = {
                ok: false,
                message: `Failed to fetch Unity state: ${String(error)}`,
              };
            },
          );
        }
      }

      if (state.enabled && currentTime != undefined) {
        const key = cursorKey(currentTime);
        const nowMs = Date.now();
        const minIntervalMs = 1000 / DEFAULT_MAX_HZ;
        if (nowMs < suppressForwardUntilMs) {
          status = {
            ok: true,
            message: "Suppressing echo after Unity -> Foxglove seek.",
          };
        } else if (key !== lastCursorKey && nowMs - lastSentAtMs >= minIntervalMs) {
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
                  message: `Failed to send cursor: ${String(error)}`,
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
    name: "Unity Cursor Bridge",
    initPanel,
  });
}
