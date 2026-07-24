// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

import { describe, expect, it } from "vitest";
import {
  JsonTopicAdvertisementTracker,
  clampRequestedRateHz,
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
      writableFieldCount: 1,
      protobufDescriptorAvailable: false,
      protobufDescriptorDigest: "",
    },
  ],
};

describe("FoxRun Publish catalog state", () => {
  it("sorts summary contracts without requiring detail fields", () => {
    expect(normalizeCatalog(summary)?.contracts.map((contract) => contract.topic)).toEqual(["/alpha", "/zeta"]);
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
    expect(parseFieldValue(field("uint64"), "18446744073709551615")).toBe("18446744073709551615");
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
});
