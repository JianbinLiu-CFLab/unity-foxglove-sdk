// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

import { describe, expect, it } from "vitest";
import {
  encodeMessagePackMessage,
  type FoxRunSubscriptionField,
  type FoxRunTypeShape,
} from "./msgpack";

const canonical = (canonicalType: string, nullable = false): FoxRunTypeShape => ({
  kind: "Canonical",
  typeName: "",
  canonicalType,
  nullable,
  collectionKind: "None",
  binary: false,
  canConstruct: true,
  elementShape: null,
  fields: [],
  enumValues: [],
});

const field = (
  name: string,
  shape: FoxRunTypeShape,
  nullable = false,
): FoxRunSubscriptionField => ({
  name,
  type: shape.typeName || shape.canonicalType,
  nullable,
  array: shape.kind === "Collection",
  protobufFieldNumber: 0,
  typeShape: shape,
});

describe("encodeMessagePackMessage", () => {
  it("matches official scalar, string, binary, array, and map vectors", () => {
    expect([...encodeMessagePackMessage(
      [
        field("nil", canonical("int32", true), true),
        field("truth", canonical("bool")),
        field("negative", canonical("int32")),
        field("positive", canonical("uint32")),
        field("single", canonical("float32")),
        field("text", canonical("string")),
        field("bytes", {
          ...canonical("byte[]"),
          kind: "Collection",
          collectionKind: "Binary",
          binary: true,
          elementShape: canonical("uint8"),
        }),
        field("items", {
          ...canonical("int32[]"),
          kind: "Collection",
          collectionKind: "Array",
          elementShape: canonical("int32"),
        }),
      ],
      {
        nil: null,
        truth: true,
        negative: -33,
        positive: 128,
        single: 1,
        text: "a",
        bytes: "/w==",
        items: [1, 2],
      },
    )]).toEqual([
      0x88,
      0xa3, 0x6e, 0x69, 0x6c, 0xc0,
      0xa5, 0x74, 0x72, 0x75, 0x74, 0x68, 0xc3,
      0xa8, 0x6e, 0x65, 0x67, 0x61, 0x74, 0x69, 0x76, 0x65, 0xd0, 0xdf,
      0xa8, 0x70, 0x6f, 0x73, 0x69, 0x74, 0x69, 0x76, 0x65, 0xcc, 0x80,
      0xa6, 0x73, 0x69, 0x6e, 0x67, 0x6c, 0x65, 0xca, 0x3f, 0x80, 0x00, 0x00,
      0xa4, 0x74, 0x65, 0x78, 0x74, 0xa1, 0x61,
      0xa5, 0x62, 0x79, 0x74, 0x65, 0x73, 0xc4, 0x01, 0xff,
      0xa5, 0x69, 0x74, 0x65, 0x6d, 0x73, 0x92, 0x01, 0x02,
    ]);
  });

  it("encodes signed and unsigned 64-bit values without a number round-trip", () => {
    const bytes = encodeMessagePackMessage(
      [
        field("signed", canonical("int64")),
        field("unsigned", canonical("uint64")),
      ],
      {
        signed: -9_007_199_254_740_993n,
        unsigned: 18_446_744_073_709_551_615n,
      },
    );

    expect([...bytes]).toEqual([
      0x82,
      0xa6, 0x73, 0x69, 0x67, 0x6e, 0x65, 0x64,
      0xd3, 0xff, 0xdf, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
      0xa8, 0x75, 0x6e, 0x73, 0x69, 0x67, 0x6e, 0x65, 0x64,
      0xcf, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
    ]);
  });

  it("uses catalog order for nested DTO, enum, Unity value, and collection fields", () => {
    const mode: FoxRunTypeShape = {
      ...canonical("Demo.Mode"),
      kind: "Enum",
      typeName: "Demo.Mode",
      canonicalType: "int32",
      enumValues: [
        { name: "Idle", number: 0 },
        { name: "Run", number: 2 },
      ],
    };
    const vector: FoxRunTypeShape = {
      ...canonical("UnityEngine.Vector3"),
      kind: "Object",
      typeName: "UnityEngine.Vector3",
      fields: [
        { jsonName: "x", memberName: "x", repeated: false, collectionKind: "None", canAssign: true, nullable: false, typeShape: canonical("float32") },
        { jsonName: "y", memberName: "y", repeated: false, collectionKind: "None", canAssign: true, nullable: false, typeShape: canonical("float32") },
        { jsonName: "z", memberName: "z", repeated: false, collectionKind: "None", canAssign: true, nullable: false, typeShape: canonical("float32") },
      ],
    };
    const nested: FoxRunTypeShape = {
      ...canonical("Demo.Command"),
      kind: "Object",
      typeName: "Demo.Command",
      fields: [
        { jsonName: "mode", memberName: "Mode", repeated: false, collectionKind: "None", canAssign: true, nullable: false, typeShape: mode },
        { jsonName: "target", memberName: "Target", repeated: false, collectionKind: "None", canAssign: true, nullable: false, typeShape: vector },
      ],
    };

    const encoded = encodeMessagePackMessage(
      [field("command", nested)],
      { command: { mode: "Run", target: { x: 1, y: 2, z: 3 } } },
    );

    expect(encoded[0]).toBe(0x81);
    expect(new TextDecoder().decode(encoded)).toContain("command");
    expect(new TextDecoder().decode(encoded)).toContain("mode");
    expect(new TextDecoder().decode(encoded)).toContain("target");
  });

  it("rejects missing shapes, unknown values, unsafe numbers, and integer overflow", () => {
    const missingShape = {
      name: "value",
      type: "int32",
      nullable: false,
      array: false,
      protobufFieldNumber: 0,
    } as FoxRunSubscriptionField;

    expect(() => encodeMessagePackMessage([missingShape], { value: 1 }))
      .toThrow("typeShape");
    expect(() => encodeMessagePackMessage([field("value", canonical("int32"))], { typo: 1 }))
      .toThrow("Unknown MessagePack payload");
    expect(() => encodeMessagePackMessage([field("value", canonical("int64"))], { value: Number.MAX_SAFE_INTEGER + 1 }))
      .toThrow("safe integer");
    expect(() => encodeMessagePackMessage([field("value", canonical("uint8"))], { value: 256 }))
      .toThrow("uint8");
  });
});
