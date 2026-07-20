// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

import { describe, expect, it } from "vitest";
import { encodeProtobufMessage, requireProtobufDescriptor, type FoxRunSubscriptionField } from "./protobuf";

function field(type: string, protobufFieldNumber = 1): FoxRunSubscriptionField {
  return { name: "value", type, nullable: false, array: false, protobufFieldNumber };
}

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

  it("rejects prototype-like unknown scalar spellings", () => {
    expect(() => encodeProtobufMessage([field("__proto__")], { value: 1 }))
      .toThrow("does not support Protobuf type");
  });

  it.each([
    [18_999, true],
    [19_000, false],
    [19_999, false],
    [20_000, true],
  ])("accepts only non-reserved Protobuf field number %i", (protobufFieldNumber, isValid) => {
    const encode = () => encodeProtobufMessage([field("int32", protobufFieldNumber)], { value: 1 });

    if (isValid) {
      expect(encode).not.toThrow();
    } else {
      expect(encode).toThrow("invalid Protobuf field number");
    }
  });

  it.each([
    ["int32", -2_147_483_648, 2_147_483_647, -2_147_483_649, 2_147_483_648],
    ["uint32", 0, 4_294_967_295, -1, 4_294_967_296],
    ["sint32", -2_147_483_648, 2_147_483_647, -2_147_483_649, 2_147_483_648],
    ["fixed32", 0, 4_294_967_295, -1, 4_294_967_296],
    ["sfixed32", -2_147_483_648, 2_147_483_647, -2_147_483_649, 2_147_483_648],
  ])("enforces the declared 32-bit range for %s", (type, minimum, maximum, belowMinimum, aboveMaximum) => {
    const fields = [field(type)];

    expect(() => encodeProtobufMessage(fields, { value: minimum })).not.toThrow();
    expect(() => encodeProtobufMessage(fields, { value: maximum })).not.toThrow();
    expect(() => encodeProtobufMessage(fields, { value: belowMinimum })).toThrow();
    expect(() => encodeProtobufMessage(fields, { value: aboveMaximum })).toThrow();
  });

  it.each([
    ["int64", "-9223372036854775808", "9223372036854775807", "-9223372036854775809", "9223372036854775808"],
    ["uint64", "0", "18446744073709551615", "-1", "18446744073709551616"],
    ["sint64", "-9223372036854775808", "9223372036854775807", "-9223372036854775809", "9223372036854775808"],
    ["fixed64", "0", "18446744073709551615", "-1", "18446744073709551616"],
    ["sfixed64", "-9223372036854775808", "9223372036854775807", "-9223372036854775809", "9223372036854775808"],
  ])("enforces the declared 64-bit range for %s", (type, minimum, maximum, belowMinimum, aboveMaximum) => {
    const fields = [field(type)];

    expect(() => encodeProtobufMessage(fields, { value: minimum })).not.toThrow();
    expect(() => encodeProtobufMessage(fields, { value: maximum })).not.toThrow();
    expect(() => encodeProtobufMessage(fields, { value: belowMinimum })).toThrow();
    expect(() => encodeProtobufMessage(fields, { value: aboveMaximum })).toThrow();
  });

  it("keeps legacy int and uint aliases at their declared 32-bit widths", () => {
    expect(() => encodeProtobufMessage([field("int")], { value: 2_147_483_648 })).toThrow("outside the Protobuf");
    expect(() => encodeProtobufMessage([field("uint")], { value: 4_294_967_296 })).toThrow("outside the Protobuf");
  });

  it("uses matching ZigZag widths for signed integer endpoints", () => {
    expect([...encodeProtobufMessage([field("sint32")], { value: -2_147_483_648 })])
      .toEqual([0x08, 0xff, 0xff, 0xff, 0xff, 0x0f]);
    expect([...encodeProtobufMessage([field("sint64")], { value: "-9223372036854775808" })])
      .toEqual([0x08, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x01]);
  });

  it("rejects unknown own payload keys before serializing the selected contract", () => {
    const fields = [field("int32")];

    expect(() => encodeProtobufMessage(fields, { value: 1, typoField: 2 }))
      .toThrow('Unknown Protobuf payload field "typoField".');
  });

  it("ignores inherited payload keys when checking the selected contract", () => {
    const message = Object.create({ inheritedTypo: 2 }) as Record<string, unknown>;
    message.value = 1;

    expect(() => encodeProtobufMessage([field("int32")], message)).not.toThrow();
  });

  it("requires a selected descriptor before the direct Protobuf path can send", () => {
    expect(() => requireProtobufDescriptor(undefined)).toThrow("did not provide");
    expect(() => requireProtobufDescriptor("not-base64")).toThrow("invalid");
    expect(() => requireProtobufDescriptor("AQ==")).not.toThrow();
  });
});
