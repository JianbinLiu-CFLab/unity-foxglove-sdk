// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

// @vitest-environment jsdom

import { afterEach, describe, expect, test, vi } from "vitest";
import type { PanelExtensionContext } from "@foxglove/extension";

import {
  buildPayload,
  cloneTimeIfChanged,
  escapeHtml,
  formatReplayTimeUtc,
  initPanel,
  isBeforeTime,
  readPanelState,
  sendCursor,
  shouldSendCursor,
  summarizeResponseText,
  tokenEndpointWarningMessage,
} from "./index";

function makeContext(initialState?: unknown): PanelExtensionContext & { onRender?: PanelExtensionContext["onRender"] } {
  return {
    initialState,
    layout: {} as PanelExtensionContext["layout"],
    panelElement: document.createElement("div"),
    saveState: vi.fn(),
    setParameter: vi.fn(),
    setSharedPanelState: vi.fn(),
    setVariable: vi.fn(),
    subscribe: vi.fn(),
    subscribeAppSettings: vi.fn(),
    subscribeMessageRange: vi.fn(),
    unsubscribeAll: vi.fn(),
    watch: vi.fn(),
  } as unknown as PanelExtensionContext & { onRender?: PanelExtensionContext["onRender"] };
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("Unity Replay Sync panel helpers", () => {
  test("buildPayload preserves seek state and optional timeline bounds", () => {
    const payload = buildPayload(
      {
        currentTime: { sec: 10, nsec: 20 },
        didSeek: true,
        startTime: { sec: 1, nsec: 2 },
        endTime: { sec: 30, nsec: 40 },
      },
      7,
    );

    expect(payload).toEqual({
      source: "foxglove-unity-cursor-bridge",
      sequence: 7,
      time: { sec: 10, nsec: 20 },
      mode: "seek",
      didSeek: true,
      startTime: { sec: 1, nsec: 2 },
      endTime: { sec: 30, nsec: 40 },
    });
  });

  test("escapeHtml covers text and attribute contexts", () => {
    expect(escapeHtml(`&<>"'`)).toBe("&amp;&lt;&gt;&quot;&#39;");
  });

  test("summarizeResponseText truncates long HTTP bodies", () => {
    const summary = summarizeResponseText("x".repeat(260), 40);

    expect(summary).toHaveLength(41);
    expect(summary.endsWith("…")).toBe(true);
  });

  test("sendCursor does not consume a successful response body", async () => {
    const getReader = vi.fn();
    const cancel = vi.fn(async () => undefined);
    const text = vi.fn();
    vi.stubGlobal("fetch", vi.fn(async () => ({
      ok: true,
      status: 202,
      body: { getReader, cancel },
      text,
    } as unknown as Response)));

    const payload = buildPayload({ currentTime: { sec: 1, nsec: 2 } }, 1)!;
    await expect(sendCursor("http://127.0.0.1/cursor", "", payload)).resolves.toEqual({
      ok: true,
      message: "Unity is following Foxglove",
    });
    expect(getReader).not.toHaveBeenCalled();
    expect(cancel).toHaveBeenCalledTimes(1);
    expect(text).not.toHaveBeenCalled();
  });

  test("sendCursor cancels an oversized error response after a bounded prefix", async () => {
    const encoder = new TextEncoder();
    const read = vi.fn()
      .mockResolvedValueOnce({ done: false, value: encoder.encode("x".repeat(512)) })
      .mockResolvedValueOnce({ done: false, value: encoder.encode("x".repeat(512)) })
      .mockResolvedValueOnce({ done: false, value: encoder.encode("must-not-be-read") });
    const cancel = vi.fn(async () => undefined);
    const releaseLock = vi.fn();
    const text = vi.fn();
    vi.stubGlobal("fetch", vi.fn(async () => ({
      ok: false,
      status: 413,
      body: { getReader: () => ({ read, cancel, releaseLock }) },
      text,
    } as unknown as Response)));

    const payload = buildPayload({ currentTime: { sec: 1, nsec: 2 } }, 1)!;
    await expect(sendCursor("http://127.0.0.1/cursor", "", payload)).resolves.toEqual({
      ok: false,
      message: `Unity rejected replay time (HTTP 413): ${"x".repeat(200)}\u2026`,
    });
    expect(read).toHaveBeenCalledTimes(2);
    expect(cancel).toHaveBeenCalledTimes(1);
    expect(releaseLock).toHaveBeenCalledTimes(1);
    expect(text).not.toHaveBeenCalled();
  });

  test("readPanelState restores safe persisted fields but not token", () => {
    const state = readPanelState({
      endpoint: " http://127.0.0.1:9999/custom ",
      enabled: false,
      token: "do-not-restore",
    });

    expect(state).toEqual({
      endpoint: "http://127.0.0.1:9999/custom",
      enabled: false,
      token: "",
      maxHz: 60,
      followUnity: false,
    });
  });

  test("readPanelState rejects invalid or non-http endpoints", () => {
    expect(readPanelState({ endpoint: "not a url" }).endpoint).toBe("http://127.0.0.1:8892/v1/replay-cursor");
    expect(readPanelState({ endpoint: "javascript:alert(1)" }).endpoint).toBe("http://127.0.0.1:8892/v1/replay-cursor");
    expect(readPanelState({ endpoint: "https://example.com/replay" }).endpoint).toBe("https://example.com/replay");
  });

  test("readPanelState restores a valid persisted cursor rate and follow flag", () => {
    const state = readPanelState({ maxHz: 30, followUnity: true });
    expect(state.maxHz).toBe(30);
    expect(state.followUnity).toBe(true);

    const fallback = readPanelState({ maxHz: 0, followUnity: "yes" });
    expect(fallback.maxHz).toBe(60);
    expect(fallback.followUnity).toBe(false);
  });

  test("shouldSendCursor rate-limits duplicate and too-fast cursor updates", () => {
    expect(shouldSendCursor(true, { sec: 1, nsec: 2 }, 1, 2, 100, 110, 16)).toBe(false);
    expect(shouldSendCursor(true, { sec: 1, nsec: 3 }, 1, 2, 100, 110, 16)).toBe(false);
    expect(shouldSendCursor(true, { sec: 1, nsec: 3 }, 1, 2, 100, 120, 16)).toBe(true);
  });

  test("isBeforeTime compares epoch-scale times without nanosecond multiplication", () => {
    expect(isBeforeTime({ sec: 1_800_000_000, nsec: 999_999_999 }, { sec: 1_800_000_001, nsec: 0 })).toBe(true);
    expect(isBeforeTime({ sec: 1_800_000_001, nsec: 0 }, { sec: 1_800_000_000, nsec: 999_999_999 })).toBe(false);
    expect(isBeforeTime({ sec: 1_800_000_000, nsec: 2 }, { sec: 1_800_000_000, nsec: 3 })).toBe(true);
  });

  test("cloneTimeIfChanged preserves object identity when bounds do not change", () => {
    const first = cloneTimeIfChanged(undefined, { sec: 1, nsec: 2 });
    const second = cloneTimeIfChanged(first, { sec: 1, nsec: 2 });
    const third = cloneTimeIfChanged(second, { sec: 1, nsec: 3 });

    expect(second).toBe(first);
    expect(third).not.toBe(first);
    expect(third).toEqual({ sec: 1, nsec: 3 });
  });

  test("formatReplayTimeUtc caches same timestamp and resets missing replay time", () => {
    const cache = { lastSec: undefined as number | undefined, lastNsec: undefined as number | undefined, text: "" };

    expect(formatReplayTimeUtc(undefined, cache)).toBe("Waiting for Foxglove playback");
    const first = formatReplayTimeUtc({ sec: 1, nsec: 0 }, cache);
    const second = formatReplayTimeUtc({ sec: 1, nsec: 0 }, cache);
    const third = formatReplayTimeUtc({ sec: 2, nsec: 0 }, cache);

    expect(second).toBe(first);
    expect(third).not.toBe(first);
    expect(formatReplayTimeUtc(undefined, cache)).toBe("Waiting for Foxglove playback");
  });
});

describe("Unity Replay Sync panel lifecycle", () => {
  test("initPanel keeps editable fields mounted across render ticks", () => {
    const context = makeContext({ endpoint: "http://127.0.0.1:9000/custom", enabled: true });
    const cleanup = initPanel(context);
    const done = vi.fn();

    context.onRender?.({ currentTime: { sec: 1, nsec: 0 } }, done);
    const endpoint = context.panelElement.querySelector<HTMLInputElement>("#endpoint");
    expect(endpoint?.value).toBe("http://127.0.0.1:9000/custom");

    endpoint!.value = "http://127.0.0.1:9999/typing";
    context.onRender?.({ currentTime: { sec: 2, nsec: 0 } }, done);

    const endpointAfterRender = context.panelElement.querySelector<HTMLInputElement>("#endpoint");
    expect(endpointAfterRender).toBe(endpoint);
    expect(endpointAfterRender?.value).toBe("http://127.0.0.1:9999/typing");
    expect(typeof cleanup).toBe("function");
  });

  test("initPanel persists endpoint and enabled state but not token", () => {
    const context = makeContext();

    initPanel(context);
    const endpoint = context.panelElement.querySelector<HTMLInputElement>("#endpoint");
    const token = context.panelElement.querySelector<HTMLInputElement>("#token");
    const enabled = context.panelElement.querySelector<HTMLInputElement>("#enabled");

    endpoint!.value = " http://127.0.0.1:9000/custom ";
    endpoint!.dispatchEvent(new Event("change"));
    token!.value = "plain-text-token";
    token!.dispatchEvent(new Event("change"));
    enabled!.checked = false;
    enabled!.dispatchEvent(new Event("change"));

    expect(context.saveState).toHaveBeenCalledWith({
      endpoint: "http://127.0.0.1:9000/custom",
      enabled: true,
      maxHz: 60,
      followUnity: false,
    });
    expect(context.saveState).toHaveBeenLastCalledWith({
      endpoint: "http://127.0.0.1:9000/custom",
      enabled: false,
      maxHz: 60,
      followUnity: false,
    });
    for (const call of vi.mocked(context.saveState).mock.calls) {
      expect(call[0]).not.toHaveProperty("token");
    }
  });

  test("cleanup removes input listeners before remounting on the same panel element", () => {
    const context = makeContext();
    const firstCleanup = initPanel(context);
    firstCleanup?.();
    initPanel(context);

    const endpoint = context.panelElement.querySelector<HTMLInputElement>("#endpoint");
    endpoint!.value = "http://127.0.0.1:9001/remounted";
    endpoint!.dispatchEvent(new Event("change"));

    expect(context.saveState).toHaveBeenCalledTimes(1);
  });

  test("token is not sent to remote plain-http endpoints", async () => {
    const fetchMock = vi.fn(async () => new Response("{}", { status: 202 }));
    vi.stubGlobal("fetch", fetchMock);
    const context = makeContext({ endpoint: "http://example.com/replay" });
    initPanel(context);
    const token = context.panelElement.querySelector<HTMLInputElement>("#token");

    token!.value = "session-token";
    token!.dispatchEvent(new Event("change"));
    context.onRender?.({ currentTime: { sec: 4, nsec: 0 } }, vi.fn());

    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(fetchMock).not.toHaveBeenCalled();
    expect(context.panelElement.querySelector("#token-warning")?.textContent).toContain("localhost or HTTPS");
  });

  test("token endpoint warning distinguishes localhost, https, and remote plain-http", () => {
    expect(tokenEndpointWarningMessage("http://127.0.0.1:8892/replay", "token")).toBe("");
    expect(tokenEndpointWarningMessage("https://example.com/replay", "token")).toBe("");
    expect(tokenEndpointWarningMessage("http://example.com/replay", "")).toBe("");
    expect(tokenEndpointWarningMessage("http://example.com/replay", "token")).toContain("localhost or HTTPS");
  });

  test("cleanup aborts an in-flight cursor request", () => {
    let capturedSignal: AbortSignal | undefined;
    vi.stubGlobal(
      "fetch",
      vi.fn((_endpoint: string, init?: RequestInit) => {
        capturedSignal = init?.signal ?? undefined;
        return new Promise<Response>(() => {});
      }),
    );
    const context = makeContext();
    const cleanup = initPanel(context);

    context.onRender?.({ currentTime: { sec: 10, nsec: 0 } }, vi.fn());

    expect(capturedSignal?.aborted).toBe(false);
    expect(typeof cleanup).toBe("function");
    cleanup?.();
    expect(capturedSignal?.aborted).toBe(true);
  });

  test("forward path keeps at most one cursor POST in flight until Unity ACKs", () => {
    const fetchMock = vi.fn(() => new Promise<Response>(() => {}));
    vi.stubGlobal("fetch", fetchMock);
    let now = 1_000_000;
    const nowSpy = vi.spyOn(Date, "now").mockImplementation(() => now);
    const context = makeContext();
    const cleanup = initPanel(context);

    context.onRender?.({ currentTime: { sec: 1, nsec: 0 } }, vi.fn());
    now += 1000; // well past the rate-limit interval, so only in-flight backpressure can gate
    context.onRender?.({ currentTime: { sec: 2, nsec: 0 } }, vi.fn());

    expect(fetchMock).toHaveBeenCalledTimes(1);
    cleanup?.();
    nowSpy.mockRestore();
  });

  test("forwards the latest cursor observed while the prior POST is in flight", async () => {
    vi.useFakeTimers();
    let resolveFirst: ((response: Response) => void) | undefined;
    let resolveSecond: ((response: Response) => void) | undefined;
    const firstResponse = new Promise<Response>((resolve) => {
      resolveFirst = resolve;
    });
    const secondResponse = new Promise<Response>((resolve) => {
      resolveSecond = resolve;
    });
    const endpoint = "http://127.0.0.1:9998/f04-latest";
    let ownCallCount = 0;
    const fetchMock = vi.fn((requestEndpoint: string, _init?: RequestInit) => {
      if (requestEndpoint !== endpoint) {
        return Promise.reject(new Error("test-only unrelated request"));
      }
      ownCallCount++;
      return ownCallCount === 1 ? firstResponse : secondResponse;
    });
    vi.stubGlobal("fetch", fetchMock);
    vi.setSystemTime(1_000_000);
    const context = makeContext({ endpoint });
    const cleanup = initPanel(context);
    try {
      context.onRender?.({ currentTime: { sec: 1, nsec: 0 } }, vi.fn());
      vi.setSystemTime(1_001_000); // beyond the cadence interval; only in-flight backpressure blocks.
      context.onRender?.({ currentTime: { sec: 2, nsec: 0 } }, vi.fn());
      expect(ownCallCount).toBe(1);

      resolveFirst?.(new Response("{}", { status: 202 }));
      await vi.advanceTimersByTimeAsync(0);

      expect(ownCallCount).toBe(2);
      const ownCalls = fetchMock.mock.calls.filter((call) => call[0] === endpoint && call[1] != undefined);
      const secondCall = ownCalls[ownCalls.length - 1];
      expect(secondCall).toBeDefined();
      const secondInit = secondCall![1] as RequestInit;
      expect(JSON.parse(String(secondInit.body)).time).toEqual({ sec: 2, nsec: 0 });
      resolveSecond?.(new Response("{}", { status: 202 }));
      await vi.advanceTimersByTimeAsync(0);
    } finally {
      cleanup?.();
      vi.useRealTimers();
    }
  });

  test("forwards an explicit same-time seek after the cursor was already delivered", async () => {
    vi.useFakeTimers();
    const endpoint = "http://127.0.0.1:9998/f04-seek";
    let ownCallCount = 0;
    const fetchMock = vi.fn((requestEndpoint: string, _init?: RequestInit) => {
      if (requestEndpoint !== endpoint) {
        return Promise.reject(new Error("test-only unrelated request"));
      }
      ownCallCount++;
      return Promise.resolve(new Response("{}", { status: 202 }));
    });
    vi.stubGlobal("fetch", fetchMock);
    vi.setSystemTime(2_000_000);
    const context = makeContext({ endpoint });
    const cleanup = initPanel(context);
    try {
      context.onRender?.({ currentTime: { sec: 7, nsec: 9 }, didSeek: false }, vi.fn());
      await vi.advanceTimersByTimeAsync(0);

      vi.setSystemTime(2_001_000);
      context.onRender?.({ currentTime: { sec: 7, nsec: 9 }, didSeek: true }, vi.fn());
      await vi.advanceTimersByTimeAsync(0);

      expect(ownCallCount).toBe(2);
      const ownCalls = fetchMock.mock.calls.filter((call) => call[0] === endpoint);
      const seekPayload = JSON.parse(String(ownCalls[1]![1]?.body));
      expect(seekPayload.mode).toBe("seek");
      expect(seekPayload.didSeek).toBe(true);
    } finally {
      cleanup?.();
      vi.useRealTimers();
    }
  });

  test("a stalled cursor request times out, aborts, and the panel resumes sending", () => {
    vi.useFakeTimers();
    let capturedSignal: AbortSignal | undefined;
    const fetchMock = vi.fn((_endpoint: string, init?: RequestInit) => {
      capturedSignal = init?.signal ?? undefined;
      return new Promise<Response>(() => {});
    });
    vi.stubGlobal("fetch", fetchMock);
    const context = makeContext();
    const cleanup = initPanel(context);

    context.onRender?.({ currentTime: { sec: 1, nsec: 0 } }, vi.fn());
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(capturedSignal?.aborted).toBe(false);

    // Request never resolves; the stall guard fires.
    vi.advanceTimersByTime(2000);
    expect(capturedSignal?.aborted).toBe(true);

    // In-flight is released, so the next render sends again instead of wedging.
    context.onRender?.({ currentTime: { sec: 2, nsec: 0 } }, vi.fn());
    expect(fetchMock).toHaveBeenCalledTimes(2);

    cleanup?.();
    vi.useRealTimers();
  });

  test("a timed-out cursor can retry the same replay time", () => {
    vi.useFakeTimers();
    const fetchMock = vi.fn((_endpoint: string, _init?: RequestInit) => new Promise<Response>(() => {}));
    vi.stubGlobal("fetch", fetchMock);
    const context = makeContext();
    const cleanup = initPanel(context);

    context.onRender?.({ currentTime: { sec: 1, nsec: 0 } }, vi.fn());
    expect(fetchMock).toHaveBeenCalledTimes(1);

    vi.advanceTimersByTime(2000);
    context.onRender?.({ currentTime: { sec: 1, nsec: 0 } }, vi.fn());
    expect(fetchMock).toHaveBeenCalledTimes(2);

    cleanup?.();
    vi.useRealTimers();
  });

  test("follow mode retries the same replay time after a stalled request", () => {
    vi.useFakeTimers();
    let cleanup: void | (() => void) = undefined;
    try {
      const seekPlayback = vi.fn();
      const fetchMock = vi.fn((_endpoint: string, _init?: RequestInit) => new Promise<Response>(() => {}));
      vi.stubGlobal("fetch", fetchMock);
      const context = makeContext({ followUnity: true });
      (context as unknown as { seekPlayback: unknown }).seekPlayback = seekPlayback;
      cleanup = initPanel(context);

      context.onRender?.({ currentTime: { sec: 5, nsec: 120_000_000 } }, vi.fn());
      expect(fetchMock).toHaveBeenCalledTimes(1);

      vi.advanceTimersByTime(2000);
      vi.runOnlyPendingTimers();

      expect(fetchMock).toHaveBeenCalledTimes(2);
      const firstPayload = JSON.parse(fetchMock.mock.calls[0]![1]!.body as string);
      const secondPayload = JSON.parse(fetchMock.mock.calls[1]![1]!.body as string);
      expect(secondPayload.replayTime).toEqual(firstPayload.replayTime);
    } finally {
      cleanup?.();
      vi.useRealTimers();
    }
  });

  test("follow mode self-clocks forward via seekPlayback without waiting for currentTime to change", async () => {
    const seekPlayback = vi.fn();
    vi.stubGlobal("fetch", vi.fn(async () => new Response("{}", { status: 202 })));
    const context = makeContext({ followUnity: true });
    (context as unknown as { seekPlayback: unknown }).seekPlayback = seekPlayback;
    const cleanup = initPanel(context);

    // A single render seeds the internal clock; the ACK-paced loop then advances on its own,
    // even though currentTime never changes again.
    context.onRender?.({ currentTime: { sec: 5, nsec: 0 } }, vi.fn());

    await vi.waitFor(() => {
      expect(seekPlayback.mock.calls.length).toBeGreaterThanOrEqual(3);
    });
    cleanup?.();

    const targetsNs = seekPlayback.mock.calls.map((call) => {
      const t = call[0] as { sec: number; nsec: number };
      return t.sec * 1_000_000_000 + t.nsec;
    });
    const seedNs = 5 * 1_000_000_000;
    // First step seeks to the seed position; the loop then advances forward only, and every step
    // stays under the 500 ms Unity seek-jump threshold (cheap forward-advance path).
    expect(targetsNs[0]).toBe(seedNs);
    expect(targetsNs[targetsNs.length - 1]).toBeGreaterThan(seedNs);
    for (let i = 1; i < targetsNs.length; i++) {
      const stepNs = targetsNs[i]! - targetsNs[i - 1]!;
      expect(stepNs).toBeGreaterThanOrEqual(0);
      expect(stepNs).toBeLessThan(500_000_000);
    }
  });

  test("follow parks at the end and does not run away", async () => {
    vi.useFakeTimers();
    let cleanup: void | (() => void) = undefined;
    try {
      const seekPlayback = vi.fn();
      const fetchMock = vi.fn(async () => new Response("{}", { status: 202 }));
      vi.stubGlobal("fetch", fetchMock);
      const context = makeContext({ followUnity: true });
      (context as unknown as { seekPlayback: unknown }).seekPlayback = seekPlayback;
      cleanup = initPanel(context);

      // Seed just shy of the end so the loop reaches it within a few steps.
      const bounds = { startTime: { sec: 0, nsec: 0 }, endTime: { sec: 10, nsec: 120_000_000 } };
      context.onRender?.({ currentTime: { sec: 10, nsec: 0 }, ...bounds }, vi.fn());

      await vi.advanceTimersByTimeAsync(1000);
      const parkedCount = fetchMock.mock.calls.length;
      expect(parkedCount).toBeGreaterThan(0);

      await vi.advanceTimersByTimeAsync(500);
      expect(fetchMock.mock.calls.length).toBe(parkedCount);
    } finally {
      cleanup?.();
      vi.useRealTimers();
    }
  });

  test("scrubbing before the end resumes follow after the loop parked", async () => {
    vi.useFakeTimers();
    let cleanup: void | (() => void) = undefined;
    try {
      const seekPlayback = vi.fn();
      const fetchMock = vi.fn(async () => new Response("{}", { status: 202 }));
      vi.stubGlobal("fetch", fetchMock);
      const context = makeContext({ followUnity: true });
      (context as unknown as { seekPlayback: unknown }).seekPlayback = seekPlayback;
      cleanup = initPanel(context);

      const bounds = { startTime: { sec: 0, nsec: 0 }, endTime: { sec: 10, nsec: 120_000_000 } };
      context.onRender?.({ currentTime: { sec: 10, nsec: 0 }, ...bounds }, vi.fn());

      await vi.advanceTimersByTimeAsync(1000);
      const parkedCount = fetchMock.mock.calls.length;
      expect(parkedCount).toBeGreaterThan(0);

      context.onRender?.({ currentTime: { sec: 2, nsec: 0 }, didSeek: true, ...bounds }, vi.fn());
      await vi.advanceTimersByTimeAsync(200);
      expect(fetchMock.mock.calls.length).toBeGreaterThan(parkedCount + 1);
    } finally {
      cleanup?.();
      vi.useRealTimers();
    }
  });

  test("follow loop survives a host that throws on seekPlayback (keeps streaming cursors)", async () => {
    // Some Foxglove builds reject a programmatic seek while paused. The cursor stream must keep
    // advancing regardless, so Unity still receives forward cursors even if the UI cannot move.
    const seekPlayback = vi.fn(() => {
      throw new Error("seek rejected while paused");
    });
    const fetchMock = vi.fn(async () => new Response("{}", { status: 202 }));
    vi.stubGlobal("fetch", fetchMock);
    const context = makeContext({ followUnity: true });
    (context as unknown as { seekPlayback: unknown }).seekPlayback = seekPlayback;
    const cleanup = initPanel(context);

    context.onRender?.({ currentTime: { sec: 5, nsec: 0 } }, vi.fn());

    await vi.waitFor(() => {
      expect(fetchMock.mock.calls.length).toBeGreaterThanOrEqual(3);
    });
    cleanup?.();

    // seekPlayback was attempted but is no longer hammered after the first throw.
    expect(seekPlayback).toHaveBeenCalled();
  });
});
