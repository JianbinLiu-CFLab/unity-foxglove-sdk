// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/foxglove-extensions/foxrun-publish-panel
// Purpose: Explicit Foxglove WebSocket publication path for raw Protobuf FoxRun payloads.

const FOXGLOVE_SUBPROTOCOL = "foxglove.sdk.v1";
const MESSAGE_DATA_OPCODE = 1;
const DIRECT_CONNECTION_TIMEOUT_MS = 10_000;

type OpenableWebSocket = Pick<WebSocket, "addEventListener" | "close">;

export function withToken(endpoint: string, token: string): string {
  const url = new URL(endpoint);
  if (token.length > 0) {
    url.searchParams.set("token", token);
  }
  return url.toString();
}

export function buildClientAdvertise(topic: string, channelId: number): string {
  validateChannel(topic, channelId);
  return JSON.stringify({
    op: "advertise",
    channels: [{ id: channelId, topic, encoding: "protobuf" }],
  });
}

export function buildClientUnadvertise(channelId: number): string {
  validateChannelId(channelId);
  return JSON.stringify({ op: "unadvertise", channelIds: [channelId] });
}

export function buildMessageDataFrame(channelId: number, payload: Uint8Array): Uint8Array {
  if (!Number.isInteger(channelId) || channelId <= 0 || channelId > 0xffffffff) {
    throw new Error("FoxRun Protobuf channel id must be a positive uint32.");
  }
  if (payload.length === 0) {
    throw new Error("FoxRun Protobuf payload must not be empty.");
  }

  const frame = new Uint8Array(5 + payload.length);
  frame[0] = MESSAGE_DATA_OPCODE;
  new DataView(frame.buffer).setUint32(1, channelId, true);
  frame.set(payload, 5);
  return frame;
}

export function waitForSocketOpen(socket: OpenableWebSocket, timeoutMs: number): Promise<void> {
  return new Promise<void>((resolve, reject) => {
    let settled = false;
    const settle = (error?: Error): void => {
      if (settled) {
        return;
      }
      settled = true;
      clearTimeout(timeout);
      if (error == undefined) {
        resolve();
      } else {
        reject(error);
      }
    };
    const fail = (): void => settle(new Error("Could not open the direct Protobuf connection to Unity."));
    const timeout = setTimeout(() => {
      settle(new Error("Timed out opening the direct Protobuf connection to Unity."));
      socket.close();
    }, Math.max(1, timeoutMs));

    socket.addEventListener("open", () => settle(), { once: true });
    socket.addEventListener("error", fail, { once: true });
    socket.addEventListener("close", fail, { once: true });
  });
}

export class DirectFoxRunProtocolClient {
  private socket: WebSocket | undefined;
  private socketUrl = "";
  private nextChannelId = 176_001;
  private readonly channels = new DirectProtobufChannelTracker(() => this.allocateChannelId());

  public async publish(endpoint: string, token: string, topic: string, payload: Uint8Array): Promise<void> {
    const socket = await this.ensureSocket(endpoint, token);
    const action = this.channels.begin(topic);
    if (action.unadvertiseChannelId != undefined) {
      socket.send(buildClientUnadvertise(action.unadvertiseChannelId));
    }
    if (action.advertise) {
      socket.send(buildClientAdvertise(topic, action.channelId));
    }
    socket.send(buildMessageDataFrame(action.channelId, payload));
  }

  public close(): void {
    const socket = this.socket;
    this.socket = undefined;
    this.socketUrl = "";
    this.channels.release();
    if (socket != undefined
        && (socket.readyState === WebSocket.OPEN || socket.readyState === WebSocket.CONNECTING)) {
      socket.close();
    }
  }

  private async ensureSocket(endpoint: string, token: string): Promise<WebSocket> {
    const url = withToken(endpoint, token);
    if (this.socket != undefined && this.socketUrl === url && this.socket.readyState === WebSocket.OPEN) {
      return this.socket;
    }

    this.close();
    const socket = new WebSocket(url, FOXGLOVE_SUBPROTOCOL);
    socket.binaryType = "arraybuffer";
    this.socket = socket;
    this.socketUrl = url;

    try {
      await waitForSocketOpen(socket, DIRECT_CONNECTION_TIMEOUT_MS);
    } catch (error) {
      if (this.socket === socket) {
        this.socket = undefined;
        this.socketUrl = "";
      }
      throw error;
    }

    return socket;
  }

  private allocateChannelId(): number {
    const channelId = this.nextChannelId;
    this.nextChannelId = this.nextChannelId >= 0xfffffff0 ? 176_001 : this.nextChannelId + 1;
    return channelId;
  }
}

export class DirectProtobufChannelTracker {
  private activeTopic: string | undefined;
  private activeChannelId: number | undefined;

  public constructor(private readonly allocateChannelId: () => number) {
  }

  public begin(topic: string): {
    channelId: number;
    advertise: boolean;
    unadvertiseChannelId: number | undefined;
  } {
    validateTopic(topic);
    if (this.activeTopic === topic && this.activeChannelId != undefined) {
      return {
        channelId: this.activeChannelId,
        advertise: false,
        unadvertiseChannelId: undefined,
      };
    }

    const unadvertiseChannelId = this.activeChannelId;
    const channelId = this.allocateChannelId();
    this.activeTopic = topic;
    this.activeChannelId = channelId;
    return { channelId, advertise: true, unadvertiseChannelId };
  }

  public release(): void {
    this.activeTopic = undefined;
    this.activeChannelId = undefined;
  }
}

function validateChannel(topic: string, channelId: number): void {
  validateTopic(topic);
  validateChannelId(channelId);
}

function validateTopic(topic: string): void {
  if (topic.trim().length === 0) {
    throw new Error("FoxRun Protobuf topic must not be empty.");
  }
}

function validateChannelId(channelId: number): void {
  if (!Number.isInteger(channelId) || channelId <= 0 || channelId > 0xffffffff) {
    throw new Error("FoxRun Protobuf channel id must be a positive uint32.");
  }
}
