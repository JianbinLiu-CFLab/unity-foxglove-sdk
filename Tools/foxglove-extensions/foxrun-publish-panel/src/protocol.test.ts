// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

import { describe, expect, it } from "vitest";
import {
  buildClientAdvertise,
  buildMessageDataFrame,
  DirectProtobufChannelTracker,
  waitForSocketOpen,
  withToken,
} from "./protocol";

describe("FoxRun direct protocol helpers", () => {
  it("advertises protobuf before direct publication", () => {
    expect(JSON.parse(buildClientAdvertise("/phase176/input", 176001))).toEqual({
      op: "advertise",
      channels: [{ id: 176001, topic: "/phase176/input", encoding: "protobuf" }],
    });
  });

  it("writes the client MessageData opcode and little-endian channel id", () => {
    expect([...buildMessageDataFrame(176001, new Uint8Array([0x0d, 0, 0, 0x20, 0x41]))]).toEqual([
      1,
      0x81,
      0xaf,
      0x02,
      0x00,
      0x0d,
      0,
      0,
      0x20,
      0x41,
    ]);
  });

  it("adds an in-memory shared token to the direct connection URL", () => {
    const sessionToken = String.fromCharCode(115, 101, 115, 115, 105, 111, 110);
    const url = new URL(withToken("ws://127.0.0.1:8765", sessionToken));
    expect(url.searchParams.get("token")).toBe(sessionToken);
  });

  it("reuses one advertised channel for repeat sends and releases it before changing topics", () => {
    let nextChannelId = 176001;
    const tracker = new DirectProtobufChannelTracker(() => nextChannelId++);

    expect(tracker.begin("/phase176/target")).toEqual({
      channelId: 176001,
      advertise: true,
      unadvertiseChannelId: undefined,
    });
    expect(tracker.begin("/phase176/target")).toEqual({
      channelId: 176001,
      advertise: false,
      unadvertiseChannelId: undefined,
    });
    expect(nextChannelId).toBe(176002);
    expect(tracker.begin("/phase176/other")).toEqual({
      channelId: 176002,
      advertise: true,
      unadvertiseChannelId: 176001,
    });
  });

  it("closes a direct connection that does not open before its timeout", async () => {
    const socket = new PendingSocket();

    await expect(waitForSocketOpen(socket, 1)).rejects.toThrow("Timed out");
    expect(socket.closed).toBe(true);
  });
});

class PendingSocket extends EventTarget {
  public readyState = 0;
  public closed = false;

  public close(): void {
    this.closed = true;
    this.readyState = 3;
    this.dispatchEvent(new Event("close"));
  }
}
