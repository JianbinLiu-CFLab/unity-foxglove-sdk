// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

import { describe, expect, it } from "vitest";
import { encodeProtobufMessage, requireProtobufDescriptor, type FoxRunSubscriptionField } from "./protobuf";

describe("encodeProtobufMessage", () => {
  it("writes a field-1 float with canonical fixed32 wire bytes", () => {
    const fields: readonly FoxRunSubscriptionField[] = [
      { name: "targetValue", type: "float32", nullable: false, array: false, protobufFieldNumber: 1 },
    ];

    expect([...encodeProtobufMessage(fields, { targetValue: 10 })]).toEqual([0x0d, 0x00, 0x00, 0x20, 0x41]);
  });

  it("rejects nested descriptors instead of silently sending JSON", () => {
    const fields: readonly FoxRunSubscriptionField[] = [
      { name: "nested", type: "Demo.Nested", nullable: false, array: false, protobufFieldNumber: 1 },
    ];

    expect(() => encodeProtobufMessage(fields, { nested: {} })).toThrow("does not support Protobuf type");
  });

  it("requires a selected descriptor before the direct Protobuf path can send", () => {
    expect(() => requireProtobufDescriptor(undefined)).toThrow("did not provide");
    expect(() => requireProtobufDescriptor("not-base64")).toThrow("invalid");
    expect(() => requireProtobufDescriptor("AQ==")).not.toThrow();
  });
});
