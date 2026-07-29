// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

import { describe, expect, it, vi } from "vitest";
import {
  buildClientAdvertise,
  buildMessageDataFrame,
  DirectFoxRunChannelTracker,
  DirectFoxRunProtocolClient,
  waitForSocketOpen,
  withToken,
} from "./protocol";

describe("FoxRun direct protocol helpers", () => {
  it("advertises protobuf before direct publication", () => {
    expect(JSON.parse(buildClientAdvertise("/phase176/input", 176001, "protobuf"))).toEqual({
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
    const tracker = new DirectFoxRunChannelTracker(() => nextChannelId++);

    expect(tracker.begin("/phase176/target", "protobuf")).toEqual({
      channelId: 176001,
      advertise: true,
      unadvertiseChannelId: undefined,
    });
    expect(tracker.begin("/phase176/target", "protobuf")).toEqual({
      channelId: 176001,
      advertise: false,
      unadvertiseChannelId: undefined,
    });
    expect(nextChannelId).toBe(176002);
    expect(tracker.begin("/phase176/other", "protobuf")).toEqual({
      channelId: 176002,
      advertise: true,
      unadvertiseChannelId: 176001,
    });
  });

  it("uses one shared socket and pair-keyed channels across Protobuf and MessagePack transitions", async () => {
    const factory = new ControlledSocketFactory();
    const client = new DirectFoxRunProtocolClient(factory.create);
    const endpoint = "ws://127.0.0.1:8765";
    const first = client.publish(
      endpoint,
      "session",
      "/phase185/input",
      "protobuf",
      new Uint8Array([1]),
    );
    factory.sockets[0]!.open();
    await first;
    await client.publish(
      endpoint,
      "session",
      "/phase185/input",
      "protobuf",
      new Uint8Array([2]),
    );
    await client.publish(
      endpoint,
      "session",
      "/phase185/input",
      "msgpack",
      new Uint8Array([3]),
    );
    await client.publish(
      endpoint,
      "session",
      "/phase185/input",
      "protobuf",
      new Uint8Array([4]),
    );

    expect(factory.sockets).toHaveLength(1);
    const sent = factory.sockets[0]!.sent;
    expect(JSON.parse(sent[0] as string)).toMatchObject({
      channels: [{ id: 176001, topic: "/phase185/input", encoding: "protobuf" }],
    });
    expect(JSON.parse(sent[3] as string)).toEqual({
      op: "unadvertise",
      channelIds: [176001],
    });
    expect(JSON.parse(sent[4] as string)).toMatchObject({
      channels: [{ id: 176002, topic: "/phase185/input", encoding: "msgpack" }],
    });
    expect(JSON.parse(sent[6] as string)).toEqual({
      op: "unadvertise",
      channelIds: [176002],
    });
    expect(JSON.parse(sent[7] as string)).toMatchObject({
      channels: [{ id: 176003, topic: "/phase185/input", encoding: "protobuf" }],
    });
  });

  it("closes a direct connection that does not open before its timeout", async () => {
    const socket = new PendingSocket();

    await expect(waitForSocketOpen(socket, 1)).rejects.toThrow("Timed out");
    expect(socket.closed).toBe(true);
  });

  it("shares one opening socket for same-URL concurrent publishes", async () => {
    const factory = new ControlledSocketFactory();
    const client = new DirectFoxRunProtocolClient(factory.create);
    const payload = new Uint8Array([0x0d, 0, 0, 0x20, 0x41]);
    const first = client.publish("ws://127.0.0.1:8765", "session", "/phase182/target", "protobuf", payload);
    const second = client.publish("ws://127.0.0.1:8765", "session", "/phase182/target", "protobuf", payload);
    const both = Promise.all([first, second]);

    expect(factory.sockets).toHaveLength(1);
    const socket = factory.sockets[0]!;
    socket.open();

    await expect(both).resolves.toEqual([undefined, undefined]);
    expect(JSON.parse(socket.sent[0] as string)).toEqual({
      op: "advertise",
      channels: [{ id: 176001, topic: "/phase182/target", encoding: "protobuf" }],
    });
    expect([...socket.sent[1] as Uint8Array]).toEqual([...buildMessageDataFrame(176001, payload)]);
    expect([...socket.sent[2] as Uint8Array]).toEqual([...buildMessageDataFrame(176001, payload)]);
  });

  it("keeps a replacement connection when an invalidated opener later signals open", async () => {
    const factory = new ControlledSocketFactory();
    const client = new DirectFoxRunProtocolClient(factory.create);
    const oldPublish = client.publish(
      "ws://127.0.0.1:8765", "old", "/phase182/old", "protobuf", new Uint8Array([1]),
    );
    const oldFailure = oldPublish.catch((error: unknown) => error);
    const oldSocket = factory.sockets[0]!;
    const replacement = client.publish(
      "ws://127.0.0.1:8766", "new", "/phase182/new", "protobuf", new Uint8Array([2]),
    );

    expect(factory.sockets).toHaveLength(2);
    expect(oldSocket.closeCalls).toBe(1);
    oldSocket.open();
    const replacementSocket = factory.sockets[1]!;
    replacementSocket.open();

    await expect(oldFailure).resolves.toBeInstanceOf(Error);
    await expect(replacement).resolves.toBeUndefined();
    await expect(client.publish(
      "ws://127.0.0.1:8766", "new", "/phase182/new", "protobuf", new Uint8Array([3]),
    ))
      .resolves.toBeUndefined();
    expect(factory.sockets).toHaveLength(2);
    expect(oldSocket.sent).toEqual([]);
    expect(replacementSocket.sent).toHaveLength(3);
  });

  it("makes an opening waiter fail safely after explicit close and never sends its late open", async () => {
    const factory = new ControlledSocketFactory();
    const client = new DirectFoxRunProtocolClient(factory.create);
    const publish = client.publish(
      "ws://127.0.0.1:8765", "session", "/phase182/target", "protobuf", new Uint8Array([1]),
    );
    const socket = factory.sockets[0]!;

    client.close();
    socket.open();

    await expect(publish).rejects.toThrow();
    expect(socket.sent).toEqual([]);
  });

  it("clears a failed opening attempt so a retry owns one fresh socket", async () => {
    const factory = new ControlledSocketFactory();
    const client = new DirectFoxRunProtocolClient(factory.create);
    const first = client.publish(
      "ws://127.0.0.1:8765", "session", "/phase182/target", "protobuf", new Uint8Array([1]),
    );
    const failedSocket = factory.sockets[0]!;

    failedSocket.fail();
    await expect(first).rejects.toThrow("Could not open");

    const retry = client.publish(
      "ws://127.0.0.1:8765", "session", "/phase182/target", "protobuf", new Uint8Array([2]),
    );
    expect(factory.sockets).toHaveLength(2);
    factory.sockets[1]!.open();

    await expect(retry).resolves.toBeUndefined();
  });

  it("clears a timed-out opening attempt so a retry owns one fresh socket", async () => {
    vi.useFakeTimers();
    try {
      const factory = new ControlledSocketFactory();
      const client = new DirectFoxRunProtocolClient(factory.create);
      const first = client.publish(
        "ws://127.0.0.1:8765", "session", "/phase182/target", "protobuf", new Uint8Array([1]),
      );
      const firstFailure = expect(first).rejects.toThrow("Timed out");

      await vi.advanceTimersByTimeAsync(10_000);
      await firstFailure;
      expect(factory.sockets[0]!.closeCalls).toBe(1);

      const retry = client.publish(
        "ws://127.0.0.1:8765", "session", "/phase182/target", "protobuf", new Uint8Array([2]),
      );
      expect(factory.sockets).toHaveLength(2);
      factory.sockets[1]!.open();
      await expect(retry).resolves.toBeUndefined();
    } finally {
      vi.useRealTimers();
    }
  });

  it("advertises again on a replacement after releasing the old channel", async () => {
    const factory = new ControlledSocketFactory();
    const client = new DirectFoxRunProtocolClient(factory.create);
    const first = client.publish(
      "ws://127.0.0.1:8765", "session", "/phase182/target", "protobuf", new Uint8Array([1]),
    );
    factory.sockets[0]!.open();
    await expect(first).resolves.toBeUndefined();

    const replacement = client.publish(
      "ws://127.0.0.1:8766", "session", "/phase182/target", "protobuf", new Uint8Array([2]),
    );
    const replacementSocket = factory.sockets[1]!;
    replacementSocket.open();
    await expect(replacement).resolves.toBeUndefined();

    expect(JSON.parse(replacementSocket.sent[0] as string)).toEqual({
      op: "advertise",
      channels: [{ id: 176002, topic: "/phase182/target", encoding: "protobuf" }],
    });
  });

  it("releases advertisement ownership when the shared socket closes", async () => {
    const factory = new ControlledSocketFactory();
    const client = new DirectFoxRunProtocolClient(factory.create);
    const first = client.publish(
      "ws://127.0.0.1:8765",
      "session",
      "/phase185/target",
      "msgpack",
      new Uint8Array([1]),
    );
    factory.sockets[0]!.open();
    await first;
    factory.sockets[0]!.close();

    const retry = client.publish(
      "ws://127.0.0.1:8765",
      "session",
      "/phase185/target",
      "msgpack",
      new Uint8Array([2]),
    );
    expect(factory.sockets).toHaveLength(2);
    factory.sockets[1]!.open();
    await retry;

    expect(JSON.parse(factory.sockets[1]!.sent[0] as string)).toEqual({
      op: "advertise",
      channels: [{
        id: 176002,
        topic: "/phase185/target",
        encoding: "msgpack",
      }],
    });
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

class ControlledSocket extends EventTarget {
  public readyState = 0;
  public binaryType: BinaryType = "blob";
  public closeCalls = 0;
  public readonly sent: Array<string | Uint8Array> = [];

  public constructor(public readonly url: string) {
    super();
  }

  public send(data: string | ArrayBufferLike | Blob | ArrayBufferView): void {
    if (typeof data === "string" || data instanceof Uint8Array) {
      this.sent.push(data);
      return;
    }

    throw new Error("Controlled socket only expects the panel's string and Uint8Array frames.");
  }

  public close(_code?: number, _reason?: string): void {
    this.closeCalls++;
    this.readyState = 3;
    this.dispatchEvent(new Event("close"));
  }

  public open(): void {
    this.readyState = 1;
    this.dispatchEvent(new Event("open"));
  }

  public fail(): void {
    this.readyState = 3;
    this.dispatchEvent(new Event("error"));
  }
}

class ControlledSocketFactory {
  public readonly sockets: ControlledSocket[] = [];

  public readonly create = (url: string, _protocol: string): ControlledSocket => {
    const socket = new ControlledSocket(url);
    this.sockets.push(socket);
    return socket;
  };
}
