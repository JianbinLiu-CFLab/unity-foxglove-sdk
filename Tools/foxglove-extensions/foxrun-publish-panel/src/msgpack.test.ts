// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

import { readFileSync } from "node:fs";
import { afterEach, describe, expect, it, vi } from "vitest";
import {
  encodeMessagePackMessage,
  MESSAGEPACK_COMPATIBILITY_LIMITS,
  type FoxRunSubscriptionField,
  type FoxRunTypeShape,
} from "./msgpack";

type SharedMessagePackContract = {
  limits: {
    maxDepth: number;
    maxAggregateContainerItems: number;
  };
  vectors: {
    scalarMap: { expectedHex: string };
    utf8StringMap: { value: string; expectedHex: string };
  };
};

const sharedContract = JSON.parse(
  readFileSync("messagepack-contract-v1.json", "utf8"),
) as SharedMessagePackContract;

const hex = (value: Uint8Array): string =>
  [...value].map((byte) => byte.toString(16).padStart(2, "0")).join("");

afterEach(() => {
  vi.restoreAllMocks();
});

const canonical = (canonicalType: string, nullable = false): FoxRunTypeShape => ({
  kind: "Canonical",
  typeName: "",
  canonicalType,
  nullable,
  isValueType: canonicalType.toLowerCase() !== "string",
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
  it("matches the shared scalar, string, binary, array, and map vector", () => {
    expect(hex(encodeMessagePackMessage(
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
          isValueType: false,
          collectionKind: "Binary",
          binary: true,
          elementShape: canonical("uint8"),
        }),
        field("items", {
          ...canonical("int32[]"),
          kind: "Collection",
          isValueType: false,
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
    ))).toBe(sharedContract.vectors.scalarMap.expectedHex);
  });

  it("shares UTF-8 byte-length and bounded-limit contracts with C#", () => {
    expect(MESSAGEPACK_COMPATIBILITY_LIMITS).toEqual(sharedContract.limits);
    expect(hex(encodeMessagePackMessage(
      [field("text", canonical("string"))],
      { text: sharedContract.vectors.utf8StringMap.value },
    ))).toBe(sharedContract.vectors.utf8StringMap.expectedHex);
  });

  it("writes nil for nested reference DTO, string, list, and list elements", () => {
    const child: FoxRunTypeShape = {
      ...canonical("Demo.Child"),
      kind: "Object",
      typeName: "Demo.Child",
      isValueType: false,
      fields: [
        { jsonName: "value", memberName: "Value", repeated: false, collectionKind: "None", canAssign: true, nullable: false, typeShape: canonical("int32") },
      ],
    };
    const strings: FoxRunTypeShape = {
      ...canonical("string[]"),
      kind: "Collection",
      isValueType: false,
      collectionKind: "List",
      elementShape: canonical("string"),
    };
    const payload: FoxRunTypeShape = {
      ...canonical("Demo.NullableReferences"),
      kind: "Object",
      typeName: "Demo.NullableReferences",
      isValueType: false,
      fields: [
        { jsonName: "child", memberName: "Child", repeated: false, collectionKind: "None", canAssign: true, nullable: false, typeShape: child },
        { jsonName: "label", memberName: "Label", repeated: false, collectionKind: "None", canAssign: true, nullable: false, typeShape: canonical("string") },
        { jsonName: "list", memberName: "List", repeated: false, collectionKind: "List", canAssign: true, nullable: false, typeShape: strings },
        { jsonName: "items", memberName: "Items", repeated: false, collectionKind: "List", canAssign: true, nullable: false, typeShape: strings },
      ],
    };

    expect(hex(encodeMessagePackMessage(
      [field("payload", payload)],
      {
        payload: {
          child: null,
          label: null,
          list: null,
          items: [null],
        },
      },
    ))).toBe(
      "81a77061796c6f616484a56368696c64c0a56c6162656cc0a46c697374c0a56974656d7391c0");
  });

  it("rejects nil for a non-nullable Unity value object", () => {
    const vector: FoxRunTypeShape = {
      ...canonical("UnityEngine.Vector3"),
      kind: "Object",
      typeName: "UnityEngine.Vector3",
      isValueType: true,
      fields: [],
    };

    expect(() => encodeMessagePackMessage(
      [field("position", vector)],
      { position: null },
    )).toThrow("not nullable");
  });

  it("uses explicit type semantics for ordinary reference and value objects", () => {
    const referenceDto = {
      ...canonical("Demo.ReferenceDto"),
      kind: "Object" as const,
      typeName: "Demo.ReferenceDto",
      isValueType: false,
      fields: [],
    };
    const valueStruct = {
      ...canonical("Demo.ValueStruct"),
      kind: "Object" as const,
      typeName: "Demo.ValueStruct",
      isValueType: true,
      fields: [],
    };

    expect(hex(encodeMessagePackMessage(
      [field("value", referenceDto)],
      { value: null },
    ))).toBe("81a576616c7565c0");
    expect(() => encodeMessagePackMessage(
      [field("value", valueStruct)],
      { value: null },
    )).toThrow("not nullable");
  });

  it("enforces the shared aggregate container budget across sibling collections", () => {
    const items: FoxRunTypeShape = {
      ...canonical("int32[]"),
      kind: "Collection",
      isValueType: false,
      collectionKind: "Array",
      elementShape: canonical("int32"),
    };
    const pair: FoxRunTypeShape = {
      ...canonical("Demo.CollectionPair"),
      kind: "Object",
      typeName: "Demo.CollectionPair",
      isValueType: false,
      fields: [
        { jsonName: "left", memberName: "Left", repeated: false, collectionKind: "None", canAssign: true, nullable: false, typeShape: items },
        { jsonName: "right", memberName: "Right", repeated: false, collectionKind: "None", canAssign: true, nullable: false, typeShape: items },
      ],
    };

    expect(() => encodeMessagePackMessage(
      [field("pair", pair)],
      {
        pair: {
          left: new Array(8_191).fill(0),
          right: new Array(8_191).fill(0),
        },
      },
    )).toThrow("aggregate container item limit");
  });

  it("counts map entries once at the shared aggregate boundary", () => {
    const makeFields = (count: number): FoxRunSubscriptionField[] =>
      Array.from(
        { length: count },
        (_, index) => field(`f${index}`, canonical("bool")));
    const makeMessage = (
      fields: readonly FoxRunSubscriptionField[],
    ): Record<string, unknown> =>
      Object.fromEntries(fields.map((item) => [item.name, true]));
    const exactLimitFields = makeFields(
      MESSAGEPACK_COMPATIBILITY_LIMITS.maxAggregateContainerItems);
    const overLimitFields = makeFields(
      MESSAGEPACK_COMPATIBILITY_LIMITS.maxAggregateContainerItems + 1);

    expect(() => encodeMessagePackMessage(
      exactLimitFields,
      makeMessage(exactLimitFields),
    )).not.toThrow();
    expect(() => encodeMessagePackMessage(
      overLimitFields,
      makeMessage(overLimitFields),
    )).toThrow("aggregate container item limit");
  });

  it("counts the root topic map in the shared wire-depth boundary", () => {
    const nestedArrayShape = (levels: number): FoxRunTypeShape => {
      let shape = canonical("bool");
      for (let index = 0; index < levels; index++) {
        shape = {
          ...canonical(`nested-${index}`),
          kind: "Collection",
          isValueType: false,
          collectionKind: "Array",
          elementShape: shape,
        };
      }
      return shape;
    };
    const nestedArrayValue = (levels: number): unknown => {
      let value: unknown = true;
      for (let index = 0; index < levels; index++) {
        value = [value];
      }
      return value;
    };
    const encodeAtWireDepth = (wireDepth: number): Uint8Array => {
      const nestedContainers = wireDepth - 1;
      return encodeMessagePackMessage(
        [field("value", nestedArrayShape(nestedContainers))],
        { value: nestedArrayValue(nestedContainers) });
    };

    expect(() => encodeAtWireDepth(33)).not.toThrow();
    expect(() => encodeAtWireDepth(34)).not.toThrow();
    expect(() => encodeAtWireDepth(35)).toThrow(
      "MessagePack depth limit");
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
      isValueType: true,
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
      isValueType: true,
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
      isValueType: false,
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

  it("rejects finite values outside the float32 range before writing them", () => {
    const writeFloat32 = vi.spyOn(DataView.prototype, "setFloat32");
    const positiveOverflow = 3.4028234663852886e38 * 2;
    const negativeOverflow = -positiveOverflow;

    expect(() => encodeMessagePackMessage(
      [field("value", canonical("float32"))],
      { value: positiveOverflow },
    )).toThrow("float32 range");
    expect(() => encodeMessagePackMessage(
      [field("value", canonical("float32"))],
      { value: negativeOverflow },
    )).toThrow("float32 range");
    expect(writeFloat32).not.toHaveBeenCalled();
  });

  it("rejects oversized base64 binary before decoding it", () => {
    const binary: FoxRunTypeShape = {
      ...canonical("byte[]"),
      kind: "Collection",
      isValueType: false,
      collectionKind: "Binary",
      binary: true,
      elementShape: canonical("uint8"),
    };
    const maximumEncodedLength = Math.ceil(1_048_576 / 3) * 4;
    const oversizedBase64 = "A".repeat(maximumEncodedLength);
    const decode = vi.spyOn(globalThis, "atob");

    expect(() => encodeMessagePackMessage(
      [field("bytes", binary)],
      { bytes: oversizedBase64 },
    )).toThrow("binary exceeds the client-side byte limit");
    expect(decode).not.toHaveBeenCalled();
  });

  it("rejects whitespace-heavy base64 before normalizing or decoding it", () => {
    const binary: FoxRunTypeShape = {
      ...canonical("byte[]"),
      kind: "Collection",
      isValueType: false,
      collectionKind: "Binary",
      binary: true,
      elementShape: canonical("uint8"),
    };
    const maximumEncodedLength = Math.ceil(1_048_576 / 3) * 4;
    const whitespaceHeavy = " ".repeat(maximumEncodedLength + 1);
    const decode = vi.spyOn(globalThis, "atob");

    expect(() => encodeMessagePackMessage(
      [field("bytes", binary)],
      { bytes: whitespaceHeavy },
    )).toThrow("base64 input exceeds the client-side character limit");
    expect(decode).not.toHaveBeenCalled();
  });

  it("rejects an oversized UTF-16 string before UTF-8 encoding it", () => {
    const oversized = "a".repeat(1_048_576 + 1);
    const encode = vi.spyOn(TextEncoder.prototype, "encode");

    expect(() => encodeMessagePackMessage(
      [field("text", canonical("string"))],
      { text: oversized },
    )).toThrow("string exceeds the client-side byte limit");
    expect(encode).toHaveBeenCalledTimes(1);
    expect(encode).toHaveBeenCalledWith("text");
  });
});
