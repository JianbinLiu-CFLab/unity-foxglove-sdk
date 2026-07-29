// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
// @vitest-environment jsdom

import type { PanelExtensionContext } from "@foxglove/extension";
import { readFileSync } from "node:fs";
import { describe, expect, it, vi } from "vitest";
import {
  JsonTopicAdvertisementTracker,
  clampRequestedRateHz,
  initPanel,
  normalizeRequestedRateHz,
  normalizeCatalog,
  parseFieldValue,
  readContractDetail,
  readPanelState,
} from "./index";

const summary = {
  version: 1,
  subscriptionsEnabled: true,
  subscriptionRateLimitHz: 12,
  contracts: [
    {
      declaringType: "Demo.Input",
      topic: "/zeta",
      flow: "Subscribe",
      encoding: "protobuf",
      schemaName: "unity2foxglove.foxrun.Demo_Input",
      hz: 10,
      isStream: true,
      writableFieldCount: 1,
      protobufDescriptorAvailable: true,
      protobufDescriptorDigest: "abc",
    },
    {
      declaringType: "Demo.Input",
      topic: "/alpha",
      flow: "Subscribe",
      encoding: "json",
      schemaName: "Demo.Input",
      hz: 10,
      isStream: false,
      writableFieldCount: 1,
      protobufDescriptorAvailable: false,
      protobufDescriptorDigest: "",
    },
  ],
};

const int32Shape = {
  kind: "Canonical",
  typeName: "",
  canonicalType: "int32",
  nullable: false,
  collectionKind: "None",
  binary: false,
  canConstruct: true,
  elementShape: null,
  fields: [],
  enumValues: [],
};

describe("FoxRun Publish catalog state", () => {
  it("sorts summary contracts without requiring detail fields", () => {
    expect(normalizeCatalog(summary)?.contracts.map((contract) => contract.topic)).toEqual(["/alpha", "/zeta"]);
  });

  it("keeps only canonical subscribe rows and accepts MessagePack input metadata", () => {
    const contracts = [
      {
        ...summary.contracts[0],
        topic: "/duplex",
        flow: "Publish",
        encoding: "json",
      },
      {
        ...summary.contracts[0],
        topic: "/duplex",
        flow: "Subscribe",
        encoding: "msgpack",
        schemaName: "",
        wireSchemaName: "",
        logicalSchemaName: "Demo.Input",
        subscribeAvailable: true,
        unavailableDiagnosticId: "",
        unavailableReason: "",
        protobufDescriptorAvailable: false,
        protobufDescriptorDigest: "",
      },
      {
        ...summary.contracts[0],
        topic: "/output-only",
        flow: "Publish",
        encoding: "protobuf",
      },
    ];

    expect(normalizeCatalog({ ...summary, contracts })?.contracts).toEqual([
      expect.objectContaining({
        topic: "/duplex",
        flow: "Subscribe",
        encoding: "msgpack",
      }),
    ]);
  });

  it("retains one unavailable MessagePack input reason without codec fallback", () => {
    const contracts = [{
      ...summary.contracts[0],
      flow: "Subscribe",
      encoding: "msgpack",
      schemaName: "",
      wireSchemaName: "",
      logicalSchemaName: "Demo.Input",
      subscribeAvailable: false,
      unavailableDiagnosticId: "FOXRUN618",
      unavailableReason: "mixed ordinary and stream input",
      protobufDescriptorAvailable: false,
      protobufDescriptorDigest: "",
    }];

    expect(normalizeCatalog({ ...summary, contracts })?.contracts).toEqual([
      expect.objectContaining({
        encoding: "msgpack",
        subscribeAvailable: false,
        unavailableDiagnosticId: "FOXRUN618",
        unavailableReason: "mixed ordinary and stream input",
      }),
    ]);
  });

  it("reads recursive MessagePack type shapes while keeping wire and logical schema identities separate", () => {
    const catalog = {
      ...summary,
      contracts: [{
        ...summary.contracts[0],
        flow: "Subscribe",
        encoding: "msgpack",
        schemaName: "",
        wireSchemaName: "",
        logicalSchemaName: "Demo.Input",
        subscribeAvailable: true,
        unavailableDiagnosticId: "",
        unavailableReason: "",
        protobufDescriptorAvailable: false,
        protobufDescriptorDigest: "",
        fields: [{
          name: "value",
          type: "int32",
          nullable: false,
          array: false,
          protobufFieldNumber: 0,
          typeShape: int32Shape,
        }],
      }],
    };

    expect(readContractDetail(catalog, "/zeta")).toEqual(
      expect.objectContaining({
        encoding: "msgpack",
        schemaName: "",
        wireSchemaName: "",
        logicalSchemaName: "Demo.Input",
        fields: [
          expect.objectContaining({
            typeShape: expect.objectContaining({ canonicalType: "int32" }),
          }),
        ],
      }),
    );
  });

  it("locks installed extension metadata to JSON, Protobuf, and MessagePack", () => {
    const metadata = JSON.parse(
      readFileSync("package.json", "utf8"),
    ) as { description?: string };

    expect(metadata.description).toContain("JSON");
    expect(metadata.description).toContain("Protobuf");
    expect(metadata.description).toContain("MessagePack");
  });

  it("rejects the retired flowMode and rateHz catalog aliases", () => {
    const stale = {
      ...summary,
      contracts: summary.contracts.map(({ flow, hz, ...contract }) => ({
        ...contract,
        flowMode: flow,
        rateHz: hz,
      })),
    };

    expect(normalizeCatalog(stale)?.contracts).toEqual([]);
  });

  it("reads field and descriptor detail only for the selected topic", () => {
    const detail = {
      ...summary,
      contracts: [
        {
          ...summary.contracts[0],
          fields: [{ name: "targetValue", type: "float32", nullable: false, array: false, protobufFieldNumber: 1 }],
          protobufDescriptorBase64: "AQ==",
        },
      ],
    };

    expect(readContractDetail(detail, "/zeta")?.fields).toHaveLength(1);
    expect(readContractDetail(summary, "/zeta")).toBeUndefined();
    expect(readContractDetail(detail, "/alpha")).toBeUndefined();
  });

  it("keeps repeat rates inside Unity's advertised hard limit", () => {
    expect(clampRequestedRateHz(20.4, 12)).toBe(12);
    expect(clampRequestedRateHz(-5, 12)).toBe(1);
    expect(clampRequestedRateHz(Number.NaN, 12)).toBe(1);
  });

  it("bypasses only the ordinary topic limit for catalog-declared bounded streams", () => {
    expect(normalizeRequestedRateHz(640, 60, true)).toBe(640);
    expect(normalizeRequestedRateHz(640, 60, false)).toBe(60);
    expect(normalizeRequestedRateHz(Number.NaN, 60, true)).toBe(1);
  });

  it("rejects catalog entries that omit the stream semantic", () => {
    const missing = {
      ...summary,
      contracts: summary.contracts.map(({ isStream: _isStream, ...contract }) => contract),
    };

    expect(normalizeCatalog(missing)?.contracts).toEqual([]);
  });

  it("keeps requested repeat rates separately for each catalog topic", () => {
    const state = readPanelState({
      selectedTopic: "/alpha",
      requestedRateHz: 9,
      requestedRateHzByTopic: {
        "/alpha": 3,
        "/zeta": 17,
        "/invalid": 0,
      },
    });

    expect(state.requestedRateHzByTopic).toEqual({ "/alpha": 3, "/zeta": 17 });
    expect(readPanelState({ selectedTopic: "/legacy", requestedRateHz: 9 }).requestedRateHzByTopic)
      .toEqual({ "/legacy": 9 });
  });

  it("parses typed scalar, wide-integer, array, and checkbox field editors", () => {
    const field = (type: string, array = false) => ({
      name: "value",
      type,
      nullable: false,
      array,
      protobufFieldNumber: 1,
    });

    expect(parseFieldValue(field("float32"), "10.5")).toBe(10.5);
    expect(parseFieldValue(
      field("uint64"),
      "18446744073709551615",
      undefined,
      "msgpack",
    )).toBe(18_446_744_073_709_551_615n);
    expect(parseFieldValue(field("uint64"), "18446744073709551615"))
      .toBe("18446744073709551615");
    expect(parseFieldValue(field("int32", true), "[1, 2]")).toEqual([1, 2]);
    expect(parseFieldValue(field("bool"), "", true)).toBe(true);
    expect(() => parseFieldValue(field("float32"), "not-a-number")).toThrow("finite number");
  });

  it("does not recover tokens from saved state", () => {
    const tokenProperty = ["to", "ken"].join("");
    const sessionToken = String.fromCharCode(115, 101, 115, 115, 105, 111, 110);
    expect(readPanelState({ endpoint: "ws://127.0.0.1:8765", [tokenProperty]: sessionToken })).not.toHaveProperty(tokenProperty);
  });

  it("advertises each JSON topic once and releases it before switching topics", () => {
    const tracker = new JsonTopicAdvertisementTracker();

    expect(tracker.begin("/alpha")).toEqual({ advertise: "/alpha", unadvertise: undefined });
    expect(tracker.begin("/alpha")).toEqual({ advertise: undefined, unadvertise: undefined });
    expect(tracker.begin("/zeta")).toEqual({ advertise: "/zeta", unadvertise: "/alpha" });
    expect(tracker.release()).toBe("/zeta");
    expect(tracker.release()).toBeUndefined();
  });

  it("waits for the first topic snapshot before calling the Unity catalog service", async () => {
    const callService = vi.fn().mockResolvedValue({
      ...summary,
      contracts: [],
    });
    const context = {
      panelElement: document.createElement("div"),
      initialState: undefined,
      saveState: vi.fn(),
      watch: vi.fn(),
      callService,
    } as unknown as PanelExtensionContext & { onRender?: PanelExtensionContext["onRender"] };

    const cleanup = initPanel(context);

    expect(context.watch).toHaveBeenCalledWith("topics");
    expect(callService).not.toHaveBeenCalled();

    const emptyDone = vi.fn();
    context.onRender?.({ topics: [] }, emptyDone);
    expect(emptyDone).toHaveBeenCalledOnce();
    expect(callService).not.toHaveBeenCalled();

    const readyDone = vi.fn();
    context.onRender?.({
      topics: [{
        name: "/unity/status",
        datatype: "foxglove.Log",
        schemaName: "foxglove.Log",
      }],
    }, readyDone);
    expect(readyDone).toHaveBeenCalledOnce();
    await vi.waitFor(() => {
      expect(callService).toHaveBeenCalledTimes(1);
    });

    cleanup();
  });
});
