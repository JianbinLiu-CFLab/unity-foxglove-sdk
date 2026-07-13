// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/foxglove-extensions/foxrun-publish-panel
// Purpose: Catalog-driven FoxRun Publish panel with explicit JSON and Protobuf wire paths.

import { ExtensionContext, PanelExtensionContext } from "@foxglove/extension";
import { DirectFoxRunProtocolClient } from "./protocol";
import { encodeProtobufMessage, requireProtobufDescriptor, type FoxRunSubscriptionField } from "./protobuf";

const CATALOG_SERVICE = "/foxrun/subscription-contracts";
const DEFAULT_ENDPOINT = "ws://127.0.0.1:8765";

type SubscriptionContractSummary = {
  declaringType: string;
  topic: string;
  flowMode: string;
  encoding: "json" | "protobuf";
  schemaName: string;
  rateHz: number;
  writableFieldCount: number;
  protobufDescriptorAvailable: boolean;
  protobufDescriptorDigest: string;
};

type SubscriptionContract = SubscriptionContractSummary & {
  fields: readonly FoxRunSubscriptionField[];
  protobufDescriptorBase64?: string;
};

type SubscriptionCatalog = {
  version: number;
  subscriptionsEnabled: boolean;
  subscriptionRateLimitHz: number;
  contracts: readonly SubscriptionContractSummary[];
};

type PanelState = {
  endpoint: string;
  selectedTopic: string;
  repeat: boolean;
  requestedRateHzByTopic: Record<string, number>;
};

type PanelElements = {
  root: HTMLDivElement;
  topic: HTMLSelectElement;
  fields: HTMLDivElement;
  endpoint: HTMLInputElement;
  token: HTMLInputElement;
  repeat: HTMLInputElement;
  rate: HTMLInputElement;
  send: HTMLButtonElement;
  refresh: HTMLButtonElement;
  status: HTMLDivElement;
  limit: HTMLSpanElement;
};

export function readPanelState(initialState: unknown): PanelState {
  const stored = initialState != undefined && typeof initialState === "object"
    ? initialState as Partial<PanelState>
    : {};
  const selectedTopic = typeof stored.selectedTopic === "string" ? stored.selectedTopic : "";
  const requestedRateHzByTopic = readRequestedRates(stored);
  const legacyRequestedRateHz = typeof (stored as { requestedRateHz?: unknown }).requestedRateHz === "number"
    && Number.isFinite((stored as { requestedRateHz?: number }).requestedRateHz)
    ? Math.max(1, Math.floor((stored as { requestedRateHz?: number }).requestedRateHz ?? 1))
    : undefined;
  if (selectedTopic.length > 0 && requestedRateHzByTopic[selectedTopic] == undefined && legacyRequestedRateHz != undefined) {
    requestedRateHzByTopic[selectedTopic] = legacyRequestedRateHz;
  }

  return {
    endpoint: typeof stored.endpoint === "string" && isWebSocketUrl(stored.endpoint)
      ? stored.endpoint
      : DEFAULT_ENDPOINT,
    selectedTopic,
    repeat: stored.repeat === true,
    requestedRateHzByTopic,
  };
}

function readRequestedRates(stored: Partial<PanelState>): Record<string, number> {
  const raw = stored.requestedRateHzByTopic;
  if (raw == undefined || typeof raw !== "object" || Array.isArray(raw)) {
    return {};
  }

  const rates: Record<string, number> = {};
  for (const [topic, rate] of Object.entries(raw)) {
    if (topic.length === 0 || typeof rate !== "number" || !Number.isFinite(rate) || rate < 1) {
      continue;
    }
    rates[topic] = Math.floor(rate);
  }
  return rates;
}

export function normalizeCatalog(value: unknown): SubscriptionCatalog | undefined {
  if (value == undefined || typeof value !== "object") {
    return undefined;
  }
  const raw = value as Partial<SubscriptionCatalog>;
  if (typeof raw.version !== "number"
      || typeof raw.subscriptionsEnabled !== "boolean"
      || typeof raw.subscriptionRateLimitHz !== "number"
      || !Array.isArray(raw.contracts)) {
    return undefined;
  }

  const contracts = raw.contracts.filter(isSubscriptionContractSummary)
    .sort((left, right) => left.topic.localeCompare(right.topic));
  return {
    version: raw.version,
    subscriptionsEnabled: raw.subscriptionsEnabled,
    subscriptionRateLimitHz: Math.max(1, Math.floor(raw.subscriptionRateLimitHz)),
    contracts,
  };
}

export function readContractDetail(value: unknown, topic: string): SubscriptionContract | undefined {
  const catalog = normalizeCatalog(value);
  const summary = catalog?.contracts.find((contract) => contract.topic === topic);
  if (summary == undefined || value == undefined || typeof value !== "object") {
    return undefined;
  }

  const rawContracts = (value as { contracts?: unknown }).contracts;
  if (!Array.isArray(rawContracts)) {
    return undefined;
  }
  const rawDetail = rawContracts.find((contract) => contract != undefined
    && typeof contract === "object"
    && (contract as { topic?: unknown }).topic === topic) as {
      fields?: unknown;
      protobufDescriptorBase64?: unknown;
    } | undefined;
  if (rawDetail == undefined || !Array.isArray(rawDetail.fields) || !rawDetail.fields.every(isSubscriptionField)) {
    return undefined;
  }

  const descriptor = typeof rawDetail.protobufDescriptorBase64 === "string"
    ? rawDetail.protobufDescriptorBase64
    : undefined;
  return { ...summary, fields: rawDetail.fields, protobufDescriptorBase64: descriptor };
}

export function clampRequestedRateHz(requestedRateHz: number, limitHz: number): number {
  const requested = Number.isFinite(requestedRateHz) ? Math.floor(requestedRateHz) : 1;
  const limit = Math.max(1, Math.floor(limitHz));
  return Math.min(Math.max(1, requested), limit);
}

export class JsonTopicAdvertisementTracker {
  private activeTopic: string | undefined;

  public begin(topic: string): { advertise: string | undefined; unadvertise: string | undefined } {
    if (this.activeTopic === topic) {
      return { advertise: undefined, unadvertise: undefined };
    }

    const unadvertise = this.activeTopic;
    this.activeTopic = topic;
    return { advertise: topic, unadvertise };
  }

  public release(): string | undefined {
    const topic = this.activeTopic;
    this.activeTopic = undefined;
    return topic;
  }
}

export function initPanel(context: PanelExtensionContext): () => void {
  let state = readPanelState(context.initialState);
  let catalog: SubscriptionCatalog | undefined;
  let selectedDetail: SubscriptionContract | undefined;
  let inFlight = false;
  let skippedTicks = 0;
  let repeatTimer: ReturnType<typeof setInterval> | undefined;
  let mounted = true;
  const directClient = new DirectFoxRunProtocolClient();
  const jsonAdvertisement = new JsonTopicAdvertisementTracker();
  const panel = buildPanel();
  context.panelElement.appendChild(panel.root);

  function saveState(): void {
    // Shared tokens are deliberately omitted from persisted panel state.
    context.saveState({
      endpoint: state.endpoint,
      selectedTopic: state.selectedTopic,
      repeat: state.repeat,
      requestedRateHzByTopic: state.requestedRateHzByTopic,
    });
  }

  function selectedContract(): SubscriptionContract | undefined {
    return selectedDetail?.topic === state.selectedTopic ? selectedDetail : undefined;
  }

  function effectiveRateHz(): number {
    const limit = catalog?.subscriptionRateLimitHz ?? 1;
    return clampRequestedRateHz(requestedRateHzForSelectedTopic(), limit);
  }

  function requestedRateHzForSelectedTopic(): number {
    return state.selectedTopic.length > 0
      ? state.requestedRateHzByTopic[state.selectedTopic] ?? 10
      : 10;
  }

  function setRequestedRateHzForSelectedTopic(rateHz: number): void {
    if (state.selectedTopic.length === 0) {
      return;
    }

    const normalized = clampRequestedRateHz(rateHz, catalog?.subscriptionRateLimitHz ?? 1);
    state = {
      ...state,
      requestedRateHzByTopic: {
        ...state.requestedRateHzByTopic,
        [state.selectedTopic]: normalized,
      },
    };
  }

  function setStatus(message: string, kind: "ok" | "warn" | "error" = "ok"): void {
    panel.status.textContent = message;
    panel.status.dataset.kind = kind;
  }

  function renderCatalog(): void {
    panel.topic.replaceChildren();
    if (catalog == undefined) {
      panel.topic.disabled = true;
      panel.send.disabled = true;
      panel.limit.textContent = "Waiting for Unity subscription catalog";
      return;
    }

    panel.limit.textContent = `Unity subscription limit: ${catalog.subscriptionRateLimitHz} Hz per topic`;
    if (!catalog.subscriptionsEnabled) {
      panel.topic.disabled = true;
      panel.send.disabled = true;
      setStatus("Unity subscriptions are disabled or not authorized for this connection.", "warn");
      return;
    }

    for (const contract of catalog.contracts) {
      const option = document.createElement("option");
      option.value = contract.topic;
      option.textContent = `${contract.topic} (${contract.encoding})`;
      panel.topic.appendChild(option);
    }
    if (catalog.contracts.length === 0) {
      panel.topic.disabled = true;
      panel.send.disabled = true;
      setStatus("Unity has no generated FoxRun subscription contracts.", "warn");
      return;
    }

    if (!catalog.contracts.some((contract) => contract.topic === state.selectedTopic)) {
      state = { ...state, selectedTopic: catalog.contracts[0].topic };
    }
    panel.topic.value = state.selectedTopic;
    panel.rate.value = String(effectiveRateHz());
    panel.topic.disabled = false;
    panel.send.disabled = true;
  }

  function renderFieldForm(contract: SubscriptionContract): void {
    panel.fields.replaceChildren();
    for (const field of contract.fields) {
      const fieldRoot = document.createElement("div");
      fieldRoot.className = "foxrun-field";
      const label = document.createElement("label");
      label.textContent = `${field.name} (${fieldTypeLabel(field)})`;
      const control = createFieldControl(field);
      control.dataset.foxrunField = field.name;
      fieldRoot.append(label, control);
      panel.fields.appendChild(fieldRoot);
    }
  }

  function readFieldMessage(contract: SubscriptionContract): Record<string, unknown> {
    const message: Record<string, unknown> = {};
    for (const field of contract.fields) {
      const control = Array.from(panel.fields.querySelectorAll<HTMLInputElement | HTMLTextAreaElement>("[data-foxrun-field]"))
        .find((candidate) => candidate.dataset.foxrunField === field.name);
      if (control == undefined) {
        throw new Error(`The ${field.name} field editor is unavailable.`);
      }
      message[field.name] = readFieldValue(field, control);
    }
    return message;
  }

  function stopRepeat(): void {
    if (repeatTimer != undefined) {
      clearInterval(repeatTimer);
      repeatTimer = undefined;
    }
  }

  function releaseJsonAdvertisement(): void {
    const topic = jsonAdvertisement.release();
    if (topic != undefined) {
      context.unadvertise?.(topic);
    }
  }

  function ensureJsonAdvertisement(contract: SubscriptionContract): void {
    const action = jsonAdvertisement.begin(contract.topic);
    if (action.unadvertise != undefined) {
      context.unadvertise?.(action.unadvertise);
    }
    if (action.advertise != undefined) {
      context.advertise?.(action.advertise, contract.schemaName);
    }
  }

  function startRepeat(): void {
    stopRepeat();
    if (!state.repeat) {
      return;
    }
    const intervalMs = Math.max(1, Math.round(1000 / effectiveRateHz()));
    repeatTimer = setInterval(() => {
      void send(true);
    }, intervalMs);
  }

  async function loadSelectedContractDetail(): Promise<void> {
    const topic = state.selectedTopic;
    selectedDetail = undefined;
    panel.send.disabled = true;
    if (catalog?.subscriptionsEnabled !== true || topic.length === 0 || context.callService == undefined) {
      return;
    }

    setStatus(`Loading contract detail for ${topic}...`);
    try {
      const detail = readContractDetail(
        await context.callService(CATALOG_SERVICE, { topic, includeDescriptor: true }),
        topic,
      );
      if (!mounted || state.selectedTopic !== topic) {
        return;
      }
      if (detail == undefined) {
        throw new Error("Unity returned invalid or incomplete contract detail.");
      }
      selectedDetail = detail;
      renderFieldForm(detail);
      panel.send.disabled = false;
      setStatus(`Ready to send ${detail.encoding.toUpperCase()} to ${topic}. Sends are fire-and-forget; Unity diagnostics are authoritative.`);
    } catch (error) {
      if (mounted && state.selectedTopic === topic) {
        setStatus(`Could not load contract detail: ${String(error)}`, "error");
      }
    }
  }

  async function refreshCatalog(): Promise<void> {
    if (context.callService == undefined) {
      setStatus("This Foxglove data source does not expose Call Service.", "error");
      return;
    }
    stopRepeat();
    releaseJsonAdvertisement();
    selectedDetail = undefined;
    panel.refresh.disabled = true;
    setStatus("Loading Unity subscription contracts...");
    try {
      const response = normalizeCatalog(await context.callService(CATALOG_SERVICE, {}));
      if (response == undefined) {
        throw new Error("Unity returned an invalid subscription catalog.");
      }
      catalog = response;
      renderCatalog();
      if (response.subscriptionsEnabled && response.contracts.length > 0) {
        await loadSelectedContractDetail();
        if (selectedContract() != undefined) {
          startRepeat();
        }
      }
    } catch (error) {
      setStatus(`Could not load Unity subscription contracts: ${String(error)}`, "error");
    } finally {
      panel.refresh.disabled = false;
    }
  }

  async function send(fromRepeat: boolean): Promise<void> {
    if (inFlight) {
      if (fromRepeat) {
        skippedTicks += 1;
        setStatus(`Skipped repeat tick while a send is in flight (${skippedTicks} skipped).`, "warn");
      }
      return;
    }

    const contract = selectedContract();
    if (contract == undefined || catalog?.subscriptionsEnabled !== true) {
      setStatus("Choose an enabled Unity subscription contract first.", "warn");
      return;
    }

    let message: Record<string, unknown>;
    try {
      message = readFieldMessage(contract);
    } catch (error) {
      setStatus(`Message field is invalid: ${String(error)}`, "error");
      return;
    }

    inFlight = true;
    panel.send.disabled = true;
    try {
      if (contract.encoding === "json") {
        if (context.advertise == undefined || context.publish == undefined) {
          throw new Error("This Foxglove data source does not expose client publishing.");
        }
        ensureJsonAdvertisement(contract);
        context.publish(contract.topic, message);
      } else {
        requireProtobufDescriptor(contract.protobufDescriptorBase64);
        const payload = encodeProtobufMessage(contract.fields, message);
        await directClient.publish(state.endpoint, panel.token.value, contract.topic, payload);
      }
      setStatus(`Sent ${contract.encoding.toUpperCase()} to ${contract.topic}. Fire-and-forget: confirm acceptance in Unity diagnostics.`);
    } catch (error) {
      setStatus(`Send failed: ${String(error)}`, "error");
    } finally {
      inFlight = false;
      panel.send.disabled = catalog?.subscriptionsEnabled !== true;
    }
  }

  panel.topic.addEventListener("change", () => {
    stopRepeat();
    releaseJsonAdvertisement();
    state = { ...state, selectedTopic: panel.topic.value };
    panel.rate.value = String(effectiveRateHz());
    saveState();
    void loadSelectedContractDetail().then(() => {
      if (selectedContract() != undefined) {
        startRepeat();
      }
    });
  });
  panel.endpoint.addEventListener("change", () => {
    if (!isWebSocketUrl(panel.endpoint.value)) {
      setStatus("Direct Protobuf endpoint must use ws:// or wss://.", "error");
      panel.endpoint.value = state.endpoint;
      return;
    }
    state = { ...state, endpoint: panel.endpoint.value.trim() };
    saveState();
  });
  panel.repeat.addEventListener("change", () => {
    state = { ...state, repeat: panel.repeat.checked };
    saveState();
    startRepeat();
  });
  panel.rate.addEventListener("change", () => {
    const requested = Number(panel.rate.value);
    setRequestedRateHzForSelectedTopic(Number.isFinite(requested) ? requested : 1);
    panel.rate.value = String(effectiveRateHz());
    saveState();
    startRepeat();
  });
  panel.send.addEventListener("click", () => { void send(false); });
  panel.refresh.addEventListener("click", () => { void refreshCatalog(); });

  panel.endpoint.value = state.endpoint;
  panel.repeat.checked = state.repeat;
  panel.rate.value = String(requestedRateHzForSelectedTopic());
  void refreshCatalog();

  return () => {
    mounted = false;
    stopRepeat();
    releaseJsonAdvertisement();
    panel.token.value = "";
    directClient.close();
  };
}

function buildPanel(): PanelElements {
  const root = document.createElement("div");
  root.className = "foxrun-publish";
  root.innerHTML = `
    <style>
      .foxrun-publish { box-sizing: border-box; color: #e7e9ed; display: grid; font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; gap: 12px; padding: 12px; }
      .foxrun-publish label { color: #b8bec8; font-size: 12px; font-weight: 600; }
      .foxrun-publish select, .foxrun-publish input, .foxrun-publish textarea { background: #17191e; border: 1px solid #3c424d; border-radius: 4px; box-sizing: border-box; color: #f7f8fb; font: 13px ui-monospace, SFMono-Regular, Consolas, monospace; min-width: 0; padding: 7px 8px; width: 100%; }
      .foxrun-publish textarea { min-height: 88px; resize: vertical; }
      .foxrun-row { align-items: center; display: grid; gap: 8px; grid-template-columns: 1fr auto; }
      .foxrun-repeat { align-items: center; display: flex; gap: 7px; }
      .foxrun-repeat input { height: 16px; margin: 0; width: 16px; }
      .foxrun-controls { display: grid; gap: 10px; grid-template-columns: 1fr 110px; }
      .foxrun-publish button { background: #6254c7; border: 0; border-radius: 4px; color: #fff; cursor: pointer; font: 600 13px -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; min-height: 32px; padding: 7px 12px; }
      .foxrun-publish button:disabled { cursor: default; opacity: .5; }
      .foxrun-status { border-left: 3px solid #4c9ad3; color: #c7cbd3; font-size: 12px; line-height: 1.4; padding-left: 8px; }
      .foxrun-status[data-kind="warn"] { border-color: #d9a441; }
      .foxrun-status[data-kind="error"] { border-color: #d45959; color: #ffb9b9; }
      .foxrun-limit { color: #9ba4b4; font-size: 12px; }
      .foxrun-field { display: grid; gap: 5px; }
      .foxrun-fields { display: grid; gap: 10px; }
    </style>
    <div class="foxrun-row"><label for="topic">Unity subscription topic</label><button id="refresh" type="button">Refresh</button></div>
    <select id="topic" disabled></select>
    <span id="limit" class="foxrun-limit"></span>
    <div id="fields" class="foxrun-fields"></div>
    <div class="foxrun-field"><label for="endpoint">Direct Protobuf endpoint</label><input id="endpoint" type="text" /></div>
    <div class="foxrun-field"><label for="token">Shared token for direct Protobuf (memory only)</label><input id="token" type="password" autocomplete="off" /></div>
    <div class="foxrun-controls"><label class="foxrun-repeat"><input id="repeat" type="checkbox" />Repeat</label><div class="foxrun-field"><label for="rate">Rate Hz</label><input id="rate" type="number" min="1" step="1" /></div></div>
    <button id="send" type="button" disabled>Send once</button>
    <div id="status" class="foxrun-status">Loading Unity subscription contracts...</div>
  `;

  const topic = required<HTMLSelectElement>(root, "#topic");
  const fields = required<HTMLDivElement>(root, "#fields");
  const endpoint = required<HTMLInputElement>(root, "#endpoint");
  const token = required<HTMLInputElement>(root, "#token");
  const repeat = required<HTMLInputElement>(root, "#repeat");
  const rate = required<HTMLInputElement>(root, "#rate");
  const send = required<HTMLButtonElement>(root, "#send");
  const refresh = required<HTMLButtonElement>(root, "#refresh");
  const status = required<HTMLDivElement>(root, "#status");
  const limit = required<HTMLSpanElement>(root, "#limit");
  return { root, topic, fields, endpoint, token, repeat, rate, send, refresh, status, limit };
}

function required<T extends Element>(root: ParentNode, selector: string): T {
  const element = root.querySelector<T>(selector);
  if (element == undefined) {
    throw new Error(`FoxRun Publish panel is missing ${selector}.`);
  }
  return element;
}

function isSubscriptionContractSummary(value: unknown): value is SubscriptionContractSummary {
  if (value == undefined || typeof value !== "object") {
    return false;
  }
  const contract = value as Partial<SubscriptionContractSummary>;
  return typeof contract.declaringType === "string"
    && typeof contract.topic === "string"
    && typeof contract.flowMode === "string"
    && (contract.encoding === "json" || contract.encoding === "protobuf")
    && typeof contract.schemaName === "string"
    && typeof contract.rateHz === "number"
    && typeof contract.writableFieldCount === "number"
    && typeof contract.protobufDescriptorAvailable === "boolean"
    && typeof contract.protobufDescriptorDigest === "string";
}

function isSubscriptionField(value: unknown): value is FoxRunSubscriptionField {
  if (value == undefined || typeof value !== "object") {
    return false;
  }
  const field = value as Partial<FoxRunSubscriptionField>;
  return typeof field.name === "string"
    && typeof field.type === "string"
    && typeof field.nullable === "boolean"
    && typeof field.array === "boolean"
    && Number.isInteger(field.protobufFieldNumber);
}

function createFieldControl(field: FoxRunSubscriptionField): HTMLInputElement | HTMLTextAreaElement {
  const type = normalizeFieldType(field.type);
  if (field.array || !isScalarFieldType(type)) {
    const input = document.createElement("textarea");
    input.spellcheck = false;
    input.value = field.array ? "[]" : field.nullable ? "" : "{}";
    return input;
  }
  if (isBooleanFieldType(type)) {
    const input = document.createElement("input");
    input.type = "checkbox";
    input.checked = false;
    return input;
  }
  if (isNumericFieldType(type)) {
    const input = document.createElement("input");
    input.type = isWideIntegerFieldType(type) ? "text" : "number";
    input.step = isIntegerFieldType(type) ? "1" : "any";
    input.value = field.nullable ? "" : "0";
    return input;
  }

  const input = document.createElement("input");
  input.type = "text";
  input.value = "";
  return input;
}

function readFieldValue(
  field: FoxRunSubscriptionField,
  control: HTMLInputElement | HTMLTextAreaElement,
): unknown {
  return parseFieldValue(
    field,
    control.value,
    control instanceof HTMLInputElement && control.type === "checkbox" ? control.checked : undefined,
  );
}

export function parseFieldValue(
  field: FoxRunSubscriptionField,
  rawValue: string,
  checked: boolean | undefined = undefined,
): unknown {
  const type = normalizeFieldType(field.type);
  if (isBooleanFieldType(type) && checked != undefined) {
    return checked;
  }

  const raw = rawValue.trim();
  if (raw.length === 0 && field.nullable) {
    return null;
  }
  if (field.array || !isScalarFieldType(type)) {
    let parsed: unknown;
    try {
      parsed = JSON.parse(raw);
    } catch {
      throw new Error(`${field.name} must contain valid JSON.`);
    }
    if (field.array && !Array.isArray(parsed)) {
      throw new Error(`${field.name} must be a JSON array.`);
    }
    return parsed;
  }
  if (isNumericFieldType(type)) {
    if (raw.length === 0) {
      throw new Error(`${field.name} must not be empty.`);
    }
    if (isWideIntegerFieldType(type)) {
      const unsigned = type === "uint64" || type === "fixed64";
      if (!(unsigned ? /^\d+$/ : /^-?\d+$/).test(raw)) {
        throw new Error(`${field.name} must be an integer string.`);
      }
      return raw;
    }
    const value = Number(raw);
    if (!Number.isFinite(value) || (isIntegerFieldType(type) && !Number.isSafeInteger(value))) {
      throw new Error(`${field.name} must be a ${isIntegerFieldType(type) ? "safe integer" : "finite number"}.`);
    }
    return value;
  }
  return rawValue;
}

function fieldTypeLabel(field: FoxRunSubscriptionField): string {
  return field.type + (field.array ? "[]" : "") + (field.nullable ? "?" : "");
}

function normalizeFieldType(type: string): string {
  return type.trim().replace(/^System\./i, "").toLowerCase();
}

function isScalarFieldType(type: string): boolean {
  return isBooleanFieldType(type) || isNumericFieldType(type) || type === "string" || type === "bytes";
}

function isBooleanFieldType(type: string): boolean {
  return type === "bool" || type === "boolean";
}

function isNumericFieldType(type: string): boolean {
  return type === "double" || type === "float64" || type === "float" || type === "single" || type === "float32"
    || type === "int" || type === "int32" || type === "int64" || type === "uint" || type === "uint32" || type === "uint64"
    || type === "sint32" || type === "sint64" || type === "fixed32" || type === "sfixed32" || type === "fixed64" || type === "sfixed64";
}

function isIntegerFieldType(type: string): boolean {
  return isNumericFieldType(type) && type !== "double" && type !== "float64" && type !== "float" && type !== "single" && type !== "float32";
}

function isWideIntegerFieldType(type: string): boolean {
  return type === "int64" || type === "uint64" || type === "sint64" || type === "fixed64" || type === "sfixed64";
}

function isWebSocketUrl(value: string): boolean {
  try {
    const url = new URL(value.trim());
    return url.protocol === "ws:" || url.protocol === "wss:";
  } catch {
    return false;
  }
}

export function activate(extensionContext: ExtensionContext): void {
  extensionContext.registerPanel({ name: "FoxRunPublish", initPanel });
}
