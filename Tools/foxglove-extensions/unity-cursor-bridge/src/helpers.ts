// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/foxglove-extensions/unity-cursor-bridge
// Purpose: Minimal Foxglove panel for Foxglove-owned Unity replay cursor synchronization.

import type { PanelExtensionContext, Time } from "@foxglove/extension";

export const DEFAULT_ENDPOINT = "http://127.0.0.1:8892/v1/replay-cursor";
// Default forward cursor sync rate. Matched to the typical render rate so Unity receives a
// fresh playback cursor (and advances its scene) roughly every frame instead of
// every 50 ms. 20 Hz made Unity look choppy while Foxglove played smoothly; the
// per-cursor work is cheap scene-only advance, so render-rate sync is affordable.
// This is a clock-sync cadence, not a data-sampling rate: Unity still processes
// every replay message in (lastCursor, currentCursor], so 100 Hz+ topics stay intact.
// Stage 1 (140K): this is the default for the user-configurable panel rate; the effective
// interval is derived per-render from state.maxHz.
export const DEFAULT_MAX_HZ = 60;
// Stage 2 (140K): if a cursor POST neither resolves nor rejects within this window (Unity
// stalled, half-open port, browser network-stack hiccup), abort it so in-flight backpressure
// cannot wedge the panel into a permanent "no more cursors" state. The next render retries.
export const REQUEST_TIMEOUT_MS = 2000;
// Stage 3 (140K): max replay time a single follow step may advance. Kept well under Unity's 500 ms
// external-cursor seek threshold so every step stays on the cheap forward-advance path. It also
// bounds catch-up after a stall (no one huge jump) and effectively caps playback speed relative to
// ACK latency: when Unity is heavy (ACK period > this), follow slows gracefully instead of jumping.
export const MAX_FOLLOW_STEP_MS = 150;
// Stage 3 (140K): the cursor stream to Unity runs at the full cursor rate, but seekPlayback is a
// "jump" (Foxglove reloads the frame at the target time), so calling it every cursor strobes the
// Foxglove panels (point clouds flicker). Throttle the UI catch-up seek to this interval (~10 Hz);
// Unity stays smooth, the Foxglove UI refreshes a few times per second. This is the main knob for
// the point-cloud flicker / continuity trade-off — lower = more continuous but more frame reloads.
export const SEEK_UI_INTERVAL_MS = 100;
export const WAITING_REPLAY_TIME_TEXT = "Waiting for Foxglove playback";
const NO_TOKEN_HEADERS: Record<string, string> = { "Content-Type": "application/json" };
export const RESPONSE_DIAGNOSTIC_MAX_CHARS = 200;
// Four bytes per displayed character preserves the existing diagnostic for valid UTF-8 while
// keeping the response read itself bounded. The extra byte proves that the body exceeded the cap.
export const RESPONSE_DIAGNOSTIC_MAX_BYTES = (RESPONSE_DIAGNOSTIC_MAX_CHARS * 4) + 1;

// Stage 3 (140K): seekPlayback is an undocumented PanelExtensionContext method reached via
// cast. When present it lets the panel advance the Foxglove timeline forward-only at Unity's
// ACK pace ("Follow Unity replay"). Its presence is feature-detected, not assumed.
export type MaybeSeekable = { seekPlayback?: (time: Time) => void };

export type CursorPayload = {
  source: "foxglove-unity-cursor-bridge";
  sequence: number;
  time: { sec: number; nsec: number };
  mode: "seek" | "advance";
  didSeek: boolean;
  startTime?: { sec: number; nsec: number };
  endTime?: { sec: number; nsec: number };
};

export type PanelState = {
  endpoint: string;
  token: string;
  enabled: boolean;
  // Stage 1 (140K): user-configurable cursor rate (persisted), default DEFAULT_MAX_HZ.
  maxHz: number;
  // Stage 3 (140K): opt-in forward-only ACK-paced follow (persisted), default off.
  followUnity: boolean;
};

export type SendStatus = {
  message: string;
  ok: boolean;
};

export type CursorRenderState = {
  readonly currentTime?: Time;
  readonly didSeek?: boolean;
  readonly startTime?: Time;
  readonly endTime?: Time;
};

export type ReplayTimeDisplayCache = {
  lastSec: number | undefined;
  lastNsec: number | undefined;
  text: string;
};

export type ReplayTime = {
  sec: number;
  nsec: number;
};

export type PendingCursor = {
  payload: CursorPayload;
  sentSec: number;
  sentNsec: number;
  didSeek: boolean;
};


export function normalizeEndpoint(endpoint: unknown): string {
  if (typeof endpoint !== "string") {
    return DEFAULT_ENDPOINT;
  }

  const trimmed = endpoint.trim();
  if (trimmed.length === 0) {
    return DEFAULT_ENDPOINT;
  }

  try {
    const url = new URL(trimmed);
    return url.protocol === "http:" || url.protocol === "https:" ? url.toString() : DEFAULT_ENDPOINT;
  } catch {
    return DEFAULT_ENDPOINT;
  }
}

function isLocalEndpoint(endpoint: string): boolean {
  try {
    const url = new URL(endpoint);
    return url.hostname === "localhost" || url.hostname === "127.0.0.1" || url.hostname === "[::1]" || url.hostname === "::1";
  } catch {
    return false;
  }
}

export function canSendTokenToEndpoint(endpoint: string): boolean {
  if (isLocalEndpoint(endpoint)) {
    return true;
  }

  try {
    return new URL(endpoint).protocol === "https:";
  } catch {
    return false;
  }
}

export function tokenEndpointWarningMessage(endpoint: string, token: string): string {
  return token.length > 0 && !canSendTokenToEndpoint(endpoint)
    ? "Access tokens are only sent to localhost or HTTPS endpoints."
    : "";
}

export function cloneTime(time: Time | undefined): { sec: number; nsec: number } | undefined {
  if (time == undefined) {
    return undefined;
  }

  return { sec: time.sec, nsec: time.nsec };
}

export function cloneTimeIfChanged(
  current: { sec: number; nsec: number } | undefined,
  next: Time | undefined,
): { sec: number; nsec: number } | undefined {
  if (next == undefined) {
    return undefined;
  }
  if (current?.sec === next.sec && current.nsec === next.nsec) {
    return current;
  }
  return { sec: next.sec, nsec: next.nsec };
}

export function isBeforeTime(left: ReplayTime, right: ReplayTime): boolean {
  return left.sec < right.sec || (left.sec === right.sec && left.nsec < right.nsec);
}

export function escapeHtml(value: string): string {
  return value
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}

export function summarizeResponseText(responseText: string, maxLength = RESPONSE_DIAGNOSTIC_MAX_CHARS): string {
  if (responseText.length <= maxLength) {
    return responseText;
  }

  return `${responseText.slice(0, maxLength)}…`;
}

async function readResponseDiagnostic(response: Response): Promise<string> {
  const reader = response.body?.getReader();
  if (reader == undefined) {
    return "";
  }

  const decoder = new TextDecoder();
  let bytesRead = 0;
  let responseText = "";
  let cancelReader = false;
  try {
    while (bytesRead < RESPONSE_DIAGNOSTIC_MAX_BYTES) {
      const chunk = await reader.read();
      if (chunk.done) {
        responseText += decoder.decode();
        return summarizeResponseText(responseText);
      }

      if (chunk.value.byteLength === 0) {
        continue;
      }

      const remaining = RESPONSE_DIAGNOSTIC_MAX_BYTES - bytesRead;
      const prefix = chunk.value.subarray(0, remaining);
      bytesRead += prefix.byteLength;
      responseText += decoder.decode(prefix, { stream: true });
      if (prefix.byteLength < chunk.value.byteLength || bytesRead === RESPONSE_DIAGNOSTIC_MAX_BYTES) {
        cancelReader = true;
        responseText += decoder.decode();
        const summary = summarizeResponseText(responseText);
        return summary.length > RESPONSE_DIAGNOSTIC_MAX_CHARS
          ? summary
          : `${summary}…`;
      }
    }

    throw new Error("Response diagnostic byte limit was not enforced.");
  } finally {
    if (cancelReader) {
      try {
        await reader.cancel();
      } catch {
        // The response may already be aborted; cancellation must not hide the HTTP status.
      }
    }
    reader.releaseLock();
  }
}

export function formatReplayTimeUtc(time: Time | undefined, cache: ReplayTimeDisplayCache): string {
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

export async function sendCursor(
  endpoint: string,
  token: string,
  payload: CursorPayload,
  signal?: AbortSignal,
): Promise<SendStatus> {
  if (token.length > 0 && !canSendTokenToEndpoint(endpoint)) {
    return {
      ok: false,
      message: "Access tokens are only sent to localhost or HTTPS endpoints.",
    };
  }

  const headers: Record<string, string> =
    token.length > 0 ? { "Content-Type": "application/json", Authorization: `Bearer ${token}` } : NO_TOKEN_HEADERS;

  const response = await fetch(endpoint, {
    method: "POST",
    headers,
    body: JSON.stringify(payload),
    signal,
  });
  if (response.ok) {
    try {
      await response.body?.cancel();
    } catch {
      // The ACK is authoritative; best-effort body cleanup must not turn it into a failure.
    }
    return {
      ok: true,
      message: "Unity is following Foxglove",
    };
  }

  const responseText = await readResponseDiagnostic(response);
  return {
    ok: false,
    message: `Unity rejected replay time (HTTP ${response.status}): ${summarizeResponseText(responseText)}`,
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
  const endpoint = normalizeEndpoint(stored.endpoint);
  const enabled = typeof stored.enabled === "boolean" ? stored.enabled : defaults.enabled;
  const maxHz =
    typeof stored.maxHz === "number" && isFinite(stored.maxHz) && stored.maxHz > 0
      ? stored.maxHz
      : defaults.maxHz;
  const followUnity =
    typeof stored.followUnity === "boolean" ? stored.followUnity : defaults.followUnity;
  return { endpoint, token: "", enabled, maxHz, followUnity };
}


export function shouldSendCursor(
  enabled: boolean,
  currentTime: Time | undefined,
  lastSec: number,
  lastNsec: number,
  lastSentAtMs: number,
  nowMs: number,
  minIntervalMs: number,
  forceSeek = false,
): boolean {
  if (!enabled || currentTime == undefined) {
    return false;
  }

  if (forceSeek) {
    return true;
  }

  return (currentTime.sec !== lastSec || currentTime.nsec !== lastNsec)
    && nowMs - lastSentAtMs >= minIntervalMs;
}


export function buildFollowPayload(
  sequence: number,
  sentSec: number,
  sentNsec: number,
  startTime: { sec: number; nsec: number } | undefined,
  endTime: { sec: number; nsec: number } | undefined,
): CursorPayload & { sequence: number } {
  const nextSequence = sequence + 1;
  return {
    source: "foxglove-unity-cursor-bridge",
    sequence: nextSequence,
    time: { sec: sentSec, nsec: sentNsec },
    mode: "advance",
    didSeek: false,
    startTime,
    endTime,
  };
}

export function advanceFollowClockAfterAck(
  sentSec: number,
  sentNsec: number,
  previousAckWallMs: number,
): { time: ReplayTime; ackWallMs: number } {
  const nowWall = Date.now();
  let deltaMs = previousAckWallMs >= 0 ? nowWall - previousAckWallMs : 0;
  if (deltaMs < 0) {
    deltaMs = 0;
  }
  if (deltaMs > MAX_FOLLOW_STEP_MS) {
    deltaMs = MAX_FOLLOW_STEP_MS;
  }
  const totalNsec = sentNsec + Math.round(deltaMs * 1_000_000);
  return {
    ackWallMs: nowWall,
    time: {
      sec: sentSec + Math.floor(totalNsec / 1_000_000_000),
      nsec: totalNsec % 1_000_000_000,
    },
  };
}

export function savePanelState(context: PanelExtensionContext, state: PanelState): void {
  context.saveState({
    endpoint: state.endpoint,
    enabled: state.enabled,
    maxHz: state.maxHz,
    followUnity: state.followUnity,
  });
}
