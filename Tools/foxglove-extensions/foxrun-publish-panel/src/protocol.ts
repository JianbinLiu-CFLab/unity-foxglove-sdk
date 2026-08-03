// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/foxglove-extensions/foxrun-publish-panel
// Purpose: Explicit Foxglove WebSocket publication path for raw typed FoxRun payloads.

const FOXGLOVE_SUBPROTOCOL = "foxglove.sdk.v1";
const MESSAGE_DATA_OPCODE = 1;
const DIRECT_CONNECTION_TIMEOUT_MS = 10_000;
const DIRECT_SOCKET_CONNECTING = 0;
const DIRECT_SOCKET_OPEN = 1;

export type DirectFoxRunSocket = Pick<WebSocket, "addEventListener" | "binaryType" | "close" | "readyState" | "send">;
export type DirectFoxRunSocketFactory = (url: string, protocol: string) => DirectFoxRunSocket;
export type DirectFoxRunEncoding = "protobuf" | "msgpack";

type OpenableWebSocket = Pick<DirectFoxRunSocket, "addEventListener" | "close">;

type DirectConnectionAttempt = {
  url: string;
  socket: DirectFoxRunSocket;
  generation: number;
  pendingOpen: Promise<DirectFoxRunSocket> | undefined;
};

const browserSocketFactory: DirectFoxRunSocketFactory = (url, protocol) => new WebSocket(url, protocol);

export function withToken(endpoint: string, token: string): string {
  const url = new URL(endpoint);
  if (token.length > 0) {
    url.searchParams.set("token", token);
  }
  return url.toString();
}

export function buildClientAdvertise(
  topic: string,
  channelId: number,
  encoding: DirectFoxRunEncoding,
): string {
  validateChannel(topic, channelId);
  validateEncoding(encoding);
  return JSON.stringify({
    op: "advertise",
    channels: [{ id: channelId, topic, encoding }],
  });
}

export function buildClientUnadvertise(channelId: number): string {
  validateChannelId(channelId);
  return JSON.stringify({ op: "unadvertise", channelIds: [channelId] });
}

export function buildMessageDataFrame(channelId: number, payload: Uint8Array): Uint8Array {
  if (!Number.isInteger(channelId) || channelId <= 0 || channelId > 0xffffffff) {
    throw new Error("FoxRun direct channel id must be a positive uint32.");
  }
  if (payload.length === 0) {
    throw new Error("FoxRun direct payload must not be empty.");
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
    const fail = (): void => settle(new Error("Could not open the direct FoxRun connection to Unity."));
    const timeout = setTimeout(() => {
      settle(new Error("Timed out opening the direct FoxRun connection to Unity."));
      socket.close();
    }, Math.max(1, timeoutMs));

    socket.addEventListener("open", () => settle(), { once: true });
    socket.addEventListener("error", fail, { once: true });
    socket.addEventListener("close", fail, { once: true });
  });
}

export class DirectFoxRunProtocolClient {
  private attempt: DirectConnectionAttempt | undefined;
  private nextConnectionGeneration = 0;
  private nextChannelId = 176_001;
  private readonly channels = new DirectFoxRunChannelTracker(() => this.allocateChannelId());

  /** Test seam; production construction uses the browser WebSocket factory. */
  public constructor(private readonly socketFactory: DirectFoxRunSocketFactory = browserSocketFactory) {
  }

  public async publish(
    endpoint: string,
    token: string,
    topic: string,
    encoding: DirectFoxRunEncoding,
    payload: Uint8Array,
  ): Promise<void> {
    validateEncoding(encoding);
    const socket = await this.ensureSocket(endpoint, token);
    if (!this.isCurrentOpenSocket(socket)) {
      throw new Error("The direct FoxRun connection was closed or replaced before publication.");
    }

    const action = this.channels.begin(topic, encoding);
    if (action.unadvertiseChannelId != undefined) {
      socket.send(buildClientUnadvertise(action.unadvertiseChannelId));
    }
    if (action.advertise) {
      socket.send(buildClientAdvertise(topic, action.channelId, encoding));
    }
    socket.send(buildMessageDataFrame(action.channelId, payload));
  }

  public close(): void {
    this.invalidateCurrentAttempt();
  }

  private ensureSocket(endpoint: string, token: string): Promise<DirectFoxRunSocket> {
    const url = withToken(endpoint, token);
    const existing = this.attempt;
    if (existing != undefined && existing.url === url) {
      if (this.isSocketOpen(existing.socket)) {
        return Promise.resolve(existing.socket);
      }
      if (this.isSocketConnecting(existing.socket) && existing.pendingOpen != undefined) {
        return existing.pendingOpen;
      }
    }

    if (existing != undefined) {
      this.invalidateCurrentAttempt();
    }

    return this.openSocket(url);
  }

  private openSocket(url: string): Promise<DirectFoxRunSocket> {
    const socket = this.socketFactory(url, FOXGLOVE_SUBPROTOCOL);
    socket.binaryType = "arraybuffer";
    const attempt: DirectConnectionAttempt = {
      url,
      socket,
      generation: ++this.nextConnectionGeneration,
      pendingOpen: undefined,
    };
    this.attempt = attempt;
    socket.addEventListener("close", () => {
      if (!this.ownsAttempt(attempt)) {
        return;
      }
      this.attempt = undefined;
      this.nextConnectionGeneration++;
      this.channels.release();
    }, { once: true });

    const pendingOpen = waitForSocketOpen(socket, DIRECT_CONNECTION_TIMEOUT_MS).then(
      () => {
        if (!this.ownsAttempt(attempt) || !this.isSocketOpen(socket)) {
          throw new Error("The direct FoxRun connection attempt was superseded before it opened.");
        }
        attempt.pendingOpen = undefined;
        return socket;
      },
      (error: unknown) => {
        if (this.ownsAttempt(attempt)) {
          attempt.pendingOpen = undefined;
          this.attempt = undefined;
        }
        throw error;
      },
    );
    attempt.pendingOpen = pendingOpen;
    return pendingOpen;
  }

  private invalidateCurrentAttempt(): void {
    const attempt = this.attempt;
    this.attempt = undefined;
    this.nextConnectionGeneration++;
    this.channels.release();
    if (attempt != undefined && (this.isSocketOpen(attempt.socket) || this.isSocketConnecting(attempt.socket))) {
      attempt.socket.close();
    }
  }

  private ownsAttempt(attempt: DirectConnectionAttempt): boolean {
    return this.attempt?.generation === attempt.generation && this.attempt.socket === attempt.socket;
  }

  private isCurrentOpenSocket(socket: DirectFoxRunSocket): boolean {
    return this.attempt?.socket === socket && this.isSocketOpen(socket);
  }

  private isSocketOpen(socket: DirectFoxRunSocket): boolean {
    return socket.readyState === DIRECT_SOCKET_OPEN;
  }

  private isSocketConnecting(socket: DirectFoxRunSocket): boolean {
    return socket.readyState === DIRECT_SOCKET_CONNECTING;
  }

  private allocateChannelId(): number {
    const channelId = this.nextChannelId;
    this.nextChannelId = this.nextChannelId >= 0xfffffff0 ? 176_001 : this.nextChannelId + 1;
    return channelId;
  }
}

export class DirectFoxRunChannelTracker {
  private activeTopic: string | undefined;
  private activeEncoding: DirectFoxRunEncoding | undefined;
  private activeChannelId: number | undefined;

  public constructor(private readonly allocateChannelId: () => number) {
  }

  public begin(topic: string, encoding: DirectFoxRunEncoding): {
    channelId: number;
    advertise: boolean;
    unadvertiseChannelId: number | undefined;
  } {
    validateTopic(topic);
    validateEncoding(encoding);
    if (this.activeTopic === topic
        && this.activeEncoding === encoding
        && this.activeChannelId != undefined) {
      return {
        channelId: this.activeChannelId,
        advertise: false,
        unadvertiseChannelId: undefined,
      };
    }

    const unadvertiseChannelId = this.activeChannelId;
    const channelId = this.allocateChannelId();
    this.activeTopic = topic;
    this.activeEncoding = encoding;
    this.activeChannelId = channelId;
    return { channelId, advertise: true, unadvertiseChannelId };
  }

  public release(): void {
    this.activeTopic = undefined;
    this.activeEncoding = undefined;
    this.activeChannelId = undefined;
  }
}

function validateChannel(topic: string, channelId: number): void {
  validateTopic(topic);
  validateChannelId(channelId);
}

function validateTopic(topic: string): void {
  if (topic.trim().length === 0) {
    throw new Error("FoxRun direct topic must not be empty.");
  }
}

function validateChannelId(channelId: number): void {
  if (!Number.isInteger(channelId) || channelId <= 0 || channelId > 0xffffffff) {
    throw new Error("FoxRun direct channel id must be a positive uint32.");
  }
}

function validateEncoding(encoding: DirectFoxRunEncoding): void {
  if (encoding !== "protobuf" && encoding !== "msgpack") {
    throw new Error("FoxRun direct encoding must be protobuf or msgpack.");
  }
}
