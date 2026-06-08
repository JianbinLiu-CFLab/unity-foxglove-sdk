// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

// @vitest-environment jsdom

import { afterEach, describe, expect, test, vi } from "vitest";
import type { PanelExtensionContext } from "@foxglove/extension";

import {
  buildPayload,
  escapeHtml,
  initPanel,
  readPanelState,
  shouldSendCursor,
  summarizeResponseText,
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
    });
  });

  test("shouldSendCursor rate-limits duplicate and too-fast cursor updates", () => {
    expect(shouldSendCursor(true, { sec: 1, nsec: 2 }, "1.2", 100, 110, 16)).toBe(false);
    expect(shouldSendCursor(true, { sec: 1, nsec: 3 }, "1.2", 100, 110, 16)).toBe(false);
    expect(shouldSendCursor(true, { sec: 1, nsec: 3 }, "1.2", 100, 120, 16)).toBe(true);
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
    });
    expect(context.saveState).toHaveBeenLastCalledWith({
      endpoint: "http://127.0.0.1:9000/custom",
      enabled: false,
    });
    for (const call of vi.mocked(context.saveState).mock.calls) {
      expect(call[0]).not.toHaveProperty("token");
    }
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
});
