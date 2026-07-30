// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/foxglove-extensions/foxrun-publish-panel
// Purpose: Bounded, catalog-driven typed MessagePack encoder for FoxRun input.

export const MESSAGEPACK_COMPATIBILITY_LIMITS = Object.freeze({
  maxDepth: 34,
  maxAggregateContainerItems: 16_384,
});

const MAX_DEPTH = MESSAGEPACK_COMPATIBILITY_LIMITS.maxDepth;
const MAX_CONTAINER_ITEMS =
  MESSAGEPACK_COMPATIBILITY_LIMITS.maxAggregateContainerItems;
const MAX_STRING_BYTES = 1_048_576;
const MAX_BINARY_BYTES = 1_048_576;
const MAX_BASE64_INPUT_CHARACTERS = Math.ceil(MAX_BINARY_BYTES / 3) * 4;
const MAX_OUTPUT_BYTES = 4_194_304;
const MAX_UNKNOWN_FIELD_NAMES = 8;
const MAX_FLOAT32 = 3.4028234663852886e38;

export type FoxRunEnumValue = {
  name: string;
  number: number;
};

export type FoxRunTypeField = {
  jsonName: string;
  memberName: string;
  repeated: boolean;
  collectionKind: string;
  canAssign: boolean;
  nullable: boolean;
  typeShape: FoxRunTypeShape;
};

export type FoxRunTypeShape = {
  kind: "Canonical" | "Enum" | "Collection" | "Object";
  typeName: string;
  canonicalType: string;
  nullable: boolean;
  isValueType: boolean;
  collectionKind: "None" | "Array" | "List" | "Binary";
  binary: boolean;
  canConstruct: boolean;
  elementShape: FoxRunTypeShape | null;
  fields: readonly FoxRunTypeField[];
  enumValues: readonly FoxRunEnumValue[];
};

export type FoxRunSubscriptionField = {
  name: string;
  type: string;
  nullable: boolean;
  array: boolean;
  protobufFieldNumber: number;
  typeShape?: FoxRunTypeShape;
};

type ContainerBudget = {
  used: number;
};

export function encodeMessagePackMessage(
  fields: readonly FoxRunSubscriptionField[],
  message: Record<string, unknown>,
): Uint8Array {
  if (!isRecord(message)) {
    throw new Error("MessagePack payload must be an object.");
  }

  validateKnownKeys(fields.map((field) => field.name), message, "MessagePack payload");
  const writer = new BoundedMessagePackWriter();
  const containerBudget: ContainerBudget = { used: 0 };
  consumeContainerItems(
    containerBudget,
    fields.length,
    "MessagePack payload");
  writer.writeMapHeader(fields.length);
  for (const field of fields) {
    if (field.typeShape == undefined) {
      throw new Error(`MessagePack field ${field.name} is missing typeShape metadata.`);
    }
    if (!Object.prototype.hasOwnProperty.call(message, field.name)) {
      throw new Error(`MessagePack payload is missing field ${JSON.stringify(field.name)}.`);
    }
    writer.writeString(field.name);
    writeShape(
      writer,
      field.typeShape,
      message[field.name],
      field.name,
      1,
      containerBudget,
      field.nullable);
  }
  return writer.toUint8Array();
}

function writeShape(
  writer: BoundedMessagePackWriter,
  shape: FoxRunTypeShape,
  value: unknown,
  path: string,
  parentContainerDepth: number,
  containerBudget: ContainerBudget,
  nullableOverride = false,
): void {
  if (value === null) {
    if (!canWriteNil(shape, nullableOverride)) {
      throw new Error(`Field ${path} is not nullable.`);
    }
    writer.writeNil();
    return;
  }
  if (value === undefined) {
    throw new Error(`Field ${path} is missing.`);
  }

  switch (shape.kind) {
    case "Canonical":
      writeCanonical(writer, shape.canonicalType || shape.typeName, value, path);
      return;
    case "Enum":
      writer.writeInt(requireEnumValue(shape, value, path));
      return;
    case "Collection":
      writeCollection(
        writer,
        shape,
        value,
        path,
        shape.binary || shape.collectionKind === "Binary"
          ? parentContainerDepth
          : enterContainerDepth(parentContainerDepth, path),
        containerBudget);
      return;
    case "Object":
      writeObject(
        writer,
        shape,
        value,
        path,
        enterContainerDepth(parentContainerDepth, path),
        containerBudget);
      return;
  }
}

function canWriteNil(
  shape: FoxRunTypeShape,
  nullableOverride: boolean,
): boolean {
  if (nullableOverride || shape.nullable) {
    return true;
  }
  if (shape.kind === "Collection") {
    return true;
  }
  if (shape.kind === "Canonical") {
    return normalizeCanonicalType(shape.canonicalType || shape.typeName) === "string";
  }
  return shape.kind === "Object" && !shape.isValueType;
}

function writeCanonical(
  writer: BoundedMessagePackWriter,
  canonicalType: string,
  value: unknown,
  path: string,
): void {
  switch (normalizeCanonicalType(canonicalType)) {
    case "bool":
      if (typeof value !== "boolean") {
        throw new Error(`Field ${path} must be a boolean.`);
      }
      writer.writeBool(value);
      return;
    case "string":
      if (typeof value !== "string") {
        throw new Error(`Field ${path} must be a string.`);
      }
      writer.writeString(value);
      return;
    case "int8":
      writer.writeInt(requireIntegerInRange(path, value, -128n, 127n, "int8"));
      return;
    case "uint8":
      writer.writeUInt(requireIntegerInRange(path, value, 0n, 255n, "uint8"));
      return;
    case "int16":
      writer.writeInt(requireIntegerInRange(path, value, -32_768n, 32_767n, "int16"));
      return;
    case "uint16":
      writer.writeUInt(requireIntegerInRange(path, value, 0n, 65_535n, "uint16"));
      return;
    case "int32":
      writer.writeInt(requireIntegerInRange(path, value, -2_147_483_648n, 2_147_483_647n, "int32"));
      return;
    case "uint32":
      writer.writeUInt(requireIntegerInRange(path, value, 0n, 4_294_967_295n, "uint32"));
      return;
    case "int64":
      writer.writeInt(requireIntegerInRange(
        path,
        value,
        -9_223_372_036_854_775_808n,
        9_223_372_036_854_775_807n,
        "int64",
      ));
      return;
    case "uint64":
      writer.writeUInt(requireIntegerInRange(
        path,
        value,
        0n,
        18_446_744_073_709_551_615n,
        "uint64",
      ));
      return;
    case "float32":
      writer.writeFloat32(requireFloat32(path, value));
      return;
    case "float64":
      writer.writeFloat64(requireFiniteNumber(path, value));
      return;
    default:
      throw new Error(
        `Field ${path} has unsupported MessagePack canonical type ${canonicalType || "(empty)"}.`,
      );
  }
}

function writeCollection(
  writer: BoundedMessagePackWriter,
  shape: FoxRunTypeShape,
  value: unknown,
  path: string,
  depth: number,
  containerBudget: ContainerBudget,
): void {
  if (shape.binary || shape.collectionKind === "Binary") {
    writer.writeBinary(decodeBase64(path, value));
    return;
  }
  if (!Array.isArray(value)) {
    throw new Error(`Field ${path} must be an array.`);
  }
  if (shape.elementShape == undefined) {
    throw new Error(`Field ${path} is missing its MessagePack element shape.`);
  }
  if (value.length > MAX_CONTAINER_ITEMS) {
    throw new Error(`Field ${path} exceeds the MessagePack collection item limit.`);
  }
  consumeContainerItems(containerBudget, value.length, path);
  writer.writeArrayHeader(value.length);
  for (let index = 0; index < value.length; index++) {
    writeShape(
      writer,
      shape.elementShape,
      value[index],
      `${path}[${index}]`,
      depth,
      containerBudget);
  }
}

function writeObject(
  writer: BoundedMessagePackWriter,
  shape: FoxRunTypeShape,
  value: unknown,
  path: string,
  depth: number,
  containerBudget: ContainerBudget,
): void {
  if (!isRecord(value)) {
    throw new Error(`Field ${path} must be an object.`);
  }
  if (shape.fields.length > MAX_CONTAINER_ITEMS) {
    throw new Error(`Field ${path} exceeds the MessagePack object field limit.`);
  }
  consumeContainerItems(
    containerBudget,
    shape.fields.length,
    path);
  validateKnownKeys(
    shape.fields.map((field) => field.jsonName),
    value,
    `MessagePack object ${path}`);
  writer.writeMapHeader(shape.fields.length);
  for (const field of shape.fields) {
    if (!Object.prototype.hasOwnProperty.call(value, field.jsonName)) {
      throw new Error(
        `MessagePack object ${path} is missing field ${JSON.stringify(field.jsonName)}.`);
    }
    writer.writeString(field.jsonName);
    writeShape(
      writer,
      field.typeShape,
      value[field.jsonName],
      `${path}.${field.jsonName}`,
      depth,
      containerBudget,
      field.nullable);
  }
}

function enterContainerDepth(parentDepth: number, path: string): number {
  const depth = parentDepth + 1;
  if (depth > MAX_DEPTH) {
    throw new Error(`Field ${path} exceeds the MessagePack depth limit.`);
  }
  return depth;
}

function consumeContainerItems(
  budget: ContainerBudget,
  childValues: number,
  path: string,
): void {
  if (!Number.isSafeInteger(childValues) || childValues < 0) {
    throw new Error(`Field ${path} has an invalid MessagePack container size.`);
  }
  if (budget.used > MAX_CONTAINER_ITEMS - childValues) {
    throw new Error(
      `Field ${path} exceeds the MessagePack aggregate container item limit.`);
  }
  budget.used += childValues;
}

function requireEnumValue(
  shape: FoxRunTypeShape,
  value: unknown,
  path: string,
): bigint {
  if (typeof value === "string" && !/^-?\d+$/.test(value)) {
    const match = shape.enumValues.find((candidate) => candidate.name === value);
    if (match == undefined) {
      throw new Error(`Field ${path} is not a declared enum name.`);
    }
    return BigInt(match.number);
  }
  const numeric = requireInteger(path, value);
  if (numeric < -2_147_483_648n || numeric > 2_147_483_647n) {
    throw new Error(`Field ${path} is outside the MessagePack enum Int32 range.`);
  }
  if (!shape.enumValues.some((candidate) => BigInt(candidate.number) === numeric)) {
    throw new Error(`Field ${path} is not a declared enum value.`);
  }
  return numeric;
}

function requireInteger(path: string, value: unknown): bigint {
  if (typeof value === "bigint") {
    return value;
  }
  if (typeof value === "number") {
    if (!Number.isSafeInteger(value)) {
      throw new Error(`Field ${path} must be a safe integer number, bigint, or integer string.`);
    }
    return BigInt(value);
  }
  if (typeof value === "string" && /^-?\d+$/.test(value)) {
    return BigInt(value);
  }
  throw new Error(`Field ${path} must be a safe integer number, bigint, or integer string.`);
}

function requireIntegerInRange(
  path: string,
  value: unknown,
  minimum: bigint,
  maximum: bigint,
  type: string,
): bigint {
  const parsed = requireInteger(path, value);
  if (parsed < minimum || parsed > maximum) {
    throw new Error(`Field ${path} is outside the MessagePack ${type} range.`);
  }
  return parsed;
}

function requireFiniteNumber(path: string, value: unknown): number {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    throw new Error(`Field ${path} must be a finite number.`);
  }
  return value;
}

function requireFloat32(path: string, value: unknown): number {
  const number = requireFiniteNumber(path, value);
  if (Math.abs(number) > MAX_FLOAT32) {
    throw new Error(`Field ${path} is outside the MessagePack float32 range.`);
  }
  return number;
}

function validateKnownKeys(
  names: readonly string[],
  value: Record<string, unknown>,
  context: string,
): void {
  const known = new Set(names);
  const unknown = Object.keys(value).filter((name) => !known.has(name));
  if (unknown.length === 0) {
    return;
  }
  const displayed = unknown
    .slice(0, MAX_UNKNOWN_FIELD_NAMES)
    .map((name) => JSON.stringify(name));
  const hidden = unknown.length - displayed.length;
  throw new Error(
    `Unknown ${context} field${unknown.length === 1 ? "" : "s"} `
    + displayed.join(", ")
    + (hidden > 0 ? ` and ${hidden} more` : "")
    + ".");
}

function normalizeCanonicalType(type: string): string {
  const normalized = type.trim().replace(/^System\./i, "").toLowerCase();
  switch (normalized) {
    case "boolean": return "bool";
    case "sbyte": return "int8";
    case "byte": return "uint8";
    case "short": return "int16";
    case "ushort": return "uint16";
    case "int": return "int32";
    case "uint": return "uint32";
    case "long": return "int64";
    case "ulong": return "uint64";
    case "float":
    case "single": return "float32";
    case "double": return "float64";
    default: return normalized;
  }
}

function decodeBase64(path: string, value: unknown): Uint8Array {
  if (value instanceof Uint8Array) {
    return value;
  }
  if (typeof value !== "string") {
    throw new Error(`Field ${path} must be a base64 string.`);
  }
  if (value.length > MAX_BASE64_INPUT_CHARACTERS) {
    throw new Error(
      "MessagePack base64 input exceeds the client-side character limit.");
  }
  const normalized = value.replace(/[\t\n\f\r ]/g, "");
  const padding = normalized.endsWith("==")
    ? 2
    : normalized.endsWith("=")
      ? 1
      : 0;
  const decodedLength = Math.floor(normalized.length * 3 / 4) - padding;
  if (decodedLength > MAX_BINARY_BYTES) {
    throw new Error("MessagePack binary exceeds the client-side byte limit.");
  }
  try {
    const binary = atob(normalized);
    return Uint8Array.from(binary, (character) => character.charCodeAt(0));
  } catch {
    throw new Error(`Field ${path} must be a valid base64 string.`);
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value != undefined && typeof value === "object" && !Array.isArray(value);
}

class BoundedMessagePackWriter {
  private readonly bytes: number[] = [];
  private readonly utf8 = new TextEncoder();

  public toUint8Array(): Uint8Array {
    return Uint8Array.from(this.bytes);
  }

  public writeNil(): void {
    this.writeByte(0xc0);
  }

  public writeBool(value: boolean): void {
    this.writeByte(value ? 0xc3 : 0xc2);
  }

  public writeInt(value: bigint): void {
    if (value >= 0n) {
      this.writeUInt(value);
    } else if (value >= -32n) {
      this.writeByte(Number(256n + value));
    } else if (value >= -128n) {
      this.writeByte(0xd0);
      this.writeByte(Number(BigInt.asUintN(8, value)));
    } else if (value >= -32_768n) {
      this.writeByte(0xd1);
      this.writeBigEndian(BigInt.asUintN(16, value), 2);
    } else if (value >= -2_147_483_648n) {
      this.writeByte(0xd2);
      this.writeBigEndian(BigInt.asUintN(32, value), 4);
    } else {
      this.writeByte(0xd3);
      this.writeBigEndian(BigInt.asUintN(64, value), 8);
    }
  }

  public writeUInt(value: bigint): void {
    if (value <= 0x7fn) {
      this.writeByte(Number(value));
    } else if (value <= 0xffn) {
      this.writeByte(0xcc);
      this.writeByte(Number(value));
    } else if (value <= 0xffffn) {
      this.writeByte(0xcd);
      this.writeBigEndian(value, 2);
    } else if (value <= 0xffff_ffffn) {
      this.writeByte(0xce);
      this.writeBigEndian(value, 4);
    } else {
      this.writeByte(0xcf);
      this.writeBigEndian(value, 8);
    }
  }

  public writeFloat32(value: number): void {
    const buffer = new ArrayBuffer(4);
    new DataView(buffer).setFloat32(0, value, false);
    this.writeByte(0xca);
    this.writeBytes(new Uint8Array(buffer));
  }

  public writeFloat64(value: number): void {
    const buffer = new ArrayBuffer(8);
    new DataView(buffer).setFloat64(0, value, false);
    this.writeByte(0xcb);
    this.writeBytes(new Uint8Array(buffer));
  }

  public writeString(value: string): void {
    if (value.length > MAX_STRING_BYTES) {
      throw new Error("MessagePack string exceeds the client-side byte limit.");
    }
    validateUnicode(value);
    const encoded = this.utf8.encode(value);
    if (encoded.length > MAX_STRING_BYTES) {
      throw new Error("MessagePack string exceeds the client-side byte limit.");
    }
    if (encoded.length <= 31) {
      this.writeByte(0xa0 | encoded.length);
    } else if (encoded.length <= 0xff) {
      this.writeByte(0xd9);
      this.writeByte(encoded.length);
    } else if (encoded.length <= 0xffff) {
      this.writeByte(0xda);
      this.writeBigEndian(BigInt(encoded.length), 2);
    } else {
      this.writeByte(0xdb);
      this.writeBigEndian(BigInt(encoded.length), 4);
    }
    this.writeBytes(encoded);
  }

  public writeBinary(value: Uint8Array): void {
    if (value.length > MAX_BINARY_BYTES) {
      throw new Error("MessagePack binary exceeds the client-side byte limit.");
    }
    if (value.length <= 0xff) {
      this.writeByte(0xc4);
      this.writeByte(value.length);
    } else if (value.length <= 0xffff) {
      this.writeByte(0xc5);
      this.writeBigEndian(BigInt(value.length), 2);
    } else {
      this.writeByte(0xc6);
      this.writeBigEndian(BigInt(value.length), 4);
    }
    this.writeBytes(value);
  }

  public writeArrayHeader(count: number): void {
    this.writeContainerHeader(count, 0x90, 0xdc, 0xdd);
  }

  public writeMapHeader(count: number): void {
    this.writeContainerHeader(count, 0x80, 0xde, 0xdf);
  }

  private writeContainerHeader(
    count: number,
    fixedBase: number,
    marker16: number,
    marker32: number,
  ): void {
    if (!Number.isInteger(count) || count < 0 || count > MAX_CONTAINER_ITEMS) {
      throw new Error("MessagePack container exceeds the client-side item limit.");
    }
    if (count <= 15) {
      this.writeByte(fixedBase | count);
    } else if (count <= 0xffff) {
      this.writeByte(marker16);
      this.writeBigEndian(BigInt(count), 2);
    } else {
      this.writeByte(marker32);
      this.writeBigEndian(BigInt(count), 4);
    }
  }

  private writeBigEndian(value: bigint, width: number): void {
    for (let index = width - 1; index >= 0; index--) {
      this.writeByte(Number((value >> BigInt(index * 8)) & 0xffn));
    }
  }

  private writeBytes(values: Uint8Array): void {
    this.ensureCapacity(values.length);
    for (const value of values) {
      this.bytes.push(value);
    }
  }

  private writeByte(value: number): void {
    this.ensureCapacity(1);
    this.bytes.push(value);
  }

  private ensureCapacity(additional: number): void {
    if (this.bytes.length + additional > MAX_OUTPUT_BYTES) {
      throw new Error("MessagePack payload exceeds the client-side output limit.");
    }
  }
}

function validateUnicode(value: string): void {
  for (let index = 0; index < value.length; index++) {
    const code = value.charCodeAt(index);
    if (code >= 0xd800 && code <= 0xdbff) {
      const next = value.charCodeAt(index + 1);
      if (next < 0xdc00 || next > 0xdfff) {
        throw new Error("MessagePack string contains invalid UTF-16.");
      }
      index++;
    } else if (code >= 0xdc00 && code <= 0xdfff) {
      throw new Error("MessagePack string contains invalid UTF-16.");
    }
  }
}
