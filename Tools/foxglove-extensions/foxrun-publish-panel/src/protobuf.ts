// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/foxglove-extensions/foxrun-publish-panel
// Purpose: Strict, descriptor-derived scalar Protobuf encoder for direct FoxRun publication.

import type { FoxRunSubscriptionField } from "./msgpack";

export type { FoxRunSubscriptionField } from "./msgpack";

const MAX_FIELD_NUMBER = 536_870_911;
const FIRST_RESERVED_FIELD_NUMBER = 19_000;
const LAST_RESERVED_FIELD_NUMBER = 19_999;
const MAX_UNKNOWN_FIELD_NAMES = 8;

type ScalarEncoding = {
  wireType: 0 | 1 | 2 | 5;
  kind: "float64" | "float32" | "string" | "bytes" | "bool"
    | "int32" | "int64" | "uint32" | "uint64" | "sint32" | "sint64"
    | "fixed32" | "fixed64" | "sfixed32" | "sfixed64";
};

const SCALAR_ENCODINGS: Readonly<Record<string, ScalarEncoding>> = {
  double: { wireType: 1, kind: "float64" },
  float64: { wireType: 1, kind: "float64" },
  float: { wireType: 5, kind: "float32" },
  single: { wireType: 5, kind: "float32" },
  float32: { wireType: 5, kind: "float32" },
  string: { wireType: 2, kind: "string" },
  bytes: { wireType: 2, kind: "bytes" },
  bool: { wireType: 0, kind: "bool" },
  boolean: { wireType: 0, kind: "bool" },
  int: { wireType: 0, kind: "int32" },
  int32: { wireType: 0, kind: "int32" },
  int64: { wireType: 0, kind: "int64" },
  uint: { wireType: 0, kind: "uint32" },
  uint32: { wireType: 0, kind: "uint32" },
  uint64: { wireType: 0, kind: "uint64" },
  sint32: { wireType: 0, kind: "sint32" },
  sint64: { wireType: 0, kind: "sint64" },
  fixed32: { wireType: 5, kind: "fixed32" },
  fixed64: { wireType: 1, kind: "fixed64" },
  sfixed32: { wireType: 5, kind: "sfixed32" },
  sfixed64: { wireType: 1, kind: "sfixed64" },
};

/**
 * Reject a Protobuf send when Unity did not supply the selected contract's
 * descriptor. This protects the direct path from silently treating a summary
 * response as a complete Protobuf contract.
 */
export function requireProtobufDescriptor(descriptorBase64: string | undefined): void {
  if (typeof descriptorBase64 !== "string" || descriptorBase64.length === 0) {
    throw new Error("Unity did not provide the selected Protobuf descriptor.");
  }
  try {
    if (atob(descriptorBase64).length === 0) {
      throw new Error();
    }
  } catch {
    throw new Error("Unity supplied an invalid selected Protobuf descriptor.");
  }
}

export function encodeProtobufMessage(
  fields: readonly FoxRunSubscriptionField[],
  message: Record<string, unknown>,
): Uint8Array {
  if (message == undefined || typeof message !== "object" || Array.isArray(message)) {
    throw new Error("Protobuf payload must be a JSON object.");
  }

  validateContract(fields);
  validateKnownPayloadKeys(fields, message);

  const bytes: number[] = [];
  for (const field of fields) {
    const raw = message[field.name];
    if (raw == undefined || raw === null) {
      if (raw === null && !field.nullable) {
        throw new Error(`Field ${field.name} is not nullable.`);
      }
      continue;
    }

    const values = field.array ? requireArray(field.name, raw) : [raw];
    for (const value of values) {
      const wireType = wireTypeFor(field.type);
      writeVarint(bytes, (BigInt(field.protobufFieldNumber) << 3n) | BigInt(wireType));
      writeValue(bytes, field, value);
    }
  }

  return Uint8Array.from(bytes);
}

function validateContract(fields: readonly FoxRunSubscriptionField[]): void {
  for (const field of fields) {
    validateFieldNumber(field.name, field.protobufFieldNumber);
    encodingFor(field.type);
  }
}

function validateFieldNumber(name: string, protobufFieldNumber: number): void {
  if (!Number.isInteger(protobufFieldNumber)
      || protobufFieldNumber < 1
      || protobufFieldNumber > MAX_FIELD_NUMBER
      || (protobufFieldNumber >= FIRST_RESERVED_FIELD_NUMBER && protobufFieldNumber <= LAST_RESERVED_FIELD_NUMBER)) {
    throw new Error(`Field ${name} has an invalid Protobuf field number.`);
  }
}

function validateKnownPayloadKeys(
  fields: readonly FoxRunSubscriptionField[],
  message: Record<string, unknown>,
): void {
  const fieldNames = new Set(fields.map((field) => field.name));
  const unknownNames = Object.keys(message).filter((name) => !fieldNames.has(name));
  if (unknownNames.length === 0) {
    return;
  }

  const displayedNames = unknownNames
    .slice(0, MAX_UNKNOWN_FIELD_NAMES)
    .map((name) => JSON.stringify(name));
  const hiddenCount = unknownNames.length - displayedNames.length;
  if (unknownNames.length === 1) {
    throw new Error(`Unknown Protobuf payload field ${displayedNames[0]}.`);
  }

  const hiddenSuffix = hiddenCount > 0 ? ` and ${hiddenCount} more` : "";
  throw new Error(`Unknown Protobuf payload fields ${displayedNames.join(", ")}${hiddenSuffix}.`);
}

function requireArray(name: string, value: unknown): readonly unknown[] {
  if (!Array.isArray(value)) {
    throw new Error(`Field ${name} must be an array.`);
  }
  return value;
}

function wireTypeFor(type: string): number {
  return encodingFor(type).wireType;
}

function writeValue(bytes: number[], field: FoxRunSubscriptionField, value: unknown): void {
  switch (encodingFor(field.type).kind) {
    case "float64":
      writeFloat64(bytes, value, field.name);
      return;
    case "float32":
      writeFloat32(bytes, value, field.name);
      return;
    case "string":
      writeLengthDelimited(bytes, new TextEncoder().encode(requireString(field.name, value)));
      return;
    case "bytes":
      writeLengthDelimited(bytes, decodeBase64(field.name, value));
      return;
    case "bool":
      writeVarint(bytes, requireBoolean(field.name, value) ? 1n : 0n);
      return;
    case "int32":
      writeVarint(bytes, BigInt.asUintN(64, requireSignedInteger(field.name, value, 32)));
      return;
    case "int64":
      writeVarint(bytes, BigInt.asUintN(64, requireSignedInteger(field.name, value, 64)));
      return;
    case "uint32":
      writeVarint(bytes, requireUnsignedInteger(field.name, value, 32));
      return;
    case "uint64":
      writeVarint(bytes, requireUnsignedInteger(field.name, value, 64));
      return;
    case "sint32": {
      const signed = requireSignedInteger(field.name, value, 32);
      writeVarint(bytes, zigZag(signed, 32));
      return;
    }
    case "sint64": {
      const signed = requireSignedInteger(field.name, value, 64);
      writeVarint(bytes, zigZag(signed, 64));
      return;
    }
    case "fixed32":
      writeFixed32(bytes, requireUnsignedInteger(field.name, value, 32));
      return;
    case "sfixed32":
      writeFixed32(bytes, BigInt.asUintN(32, requireSignedInteger(field.name, value, 32)));
      return;
    case "fixed64":
      writeFixed64(bytes, requireUnsignedInteger(field.name, value, 64));
      return;
    case "sfixed64":
      writeFixed64(bytes, BigInt.asUintN(64, requireSignedInteger(field.name, value, 64)));
      return;
  }
}

function encodingFor(type: string): ScalarEncoding {
  const normalizedType = normalizeType(type);
  if (!Object.prototype.hasOwnProperty.call(SCALAR_ENCODINGS, normalizedType)) {
    throw new Error(`FoxRun Publish does not support Protobuf type ${type || "(empty)"}.`);
  }
  return SCALAR_ENCODINGS[normalizedType]!;
}

function normalizeType(type: string): string {
  return type.trim().replace(/^System\./i, "").toLowerCase();
}

function requireString(name: string, value: unknown): string {
  if (typeof value !== "string") {
    throw new Error(`Field ${name} must be a string.`);
  }
  return value;
}

function requireBoolean(name: string, value: unknown): boolean {
  if (typeof value !== "boolean") {
    throw new Error(`Field ${name} must be a boolean.`);
  }
  return value;
}

function requireInteger(name: string, value: unknown): bigint {
  try {
    const parsed = typeof value === "bigint"
      ? value
      : typeof value === "number" && Number.isSafeInteger(value)
        ? BigInt(value)
        : typeof value === "string" && /^-?\d+$/.test(value)
          ? BigInt(value)
          : undefined;
    if (parsed == undefined) {
      throw new Error();
    }
    return parsed;
  } catch {
    throw new Error(`Field ${name} must be a safe integer number or integer string.`);
  }
}

function requireSignedInteger(name: string, value: unknown, bits: 32 | 64): bigint {
  const parsed = requireInteger(name, value);
  const limit = 1n << BigInt(bits - 1);
  return requireIntegerInRange(name, parsed, -limit, limit - 1n, `int${bits}`);
}

function requireUnsignedInteger(name: string, value: unknown, bits: 32 | 64): bigint {
  const parsed = requireInteger(name, value);
  if (parsed < 0n) {
    throw new Error(`Field ${name} must be unsigned.`);
  }
  const maximum = (1n << BigInt(bits)) - 1n;
  return requireIntegerInRange(name, parsed, 0n, maximum, `uint${bits}`);
}

function requireIntegerInRange(name: string, value: bigint, minimum: bigint, maximum: bigint, type: string): bigint {
  if (value < minimum || value > maximum) {
    throw new Error(`Field ${name} is outside the Protobuf ${type} range.`);
  }
  return value;
}

function zigZag(value: bigint, bits: 32 | 64): bigint {
  return (value << 1n) ^ (value >> BigInt(bits - 1));
}

function requireFiniteNumber(name: string, value: unknown): number {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    throw new Error(`Field ${name} must be a finite number.`);
  }
  return value;
}

function writeFloat32(bytes: number[], value: unknown, name: string): void {
  const buffer = new ArrayBuffer(4);
  new DataView(buffer).setFloat32(0, requireFiniteNumber(name, value), true);
  bytes.push(...new Uint8Array(buffer));
}

function writeFloat64(bytes: number[], value: unknown, name: string): void {
  const buffer = new ArrayBuffer(8);
  new DataView(buffer).setFloat64(0, requireFiniteNumber(name, value), true);
  bytes.push(...new Uint8Array(buffer));
}

function writeFixed32(bytes: number[], value: bigint): void {
  const buffer = new ArrayBuffer(4);
  new DataView(buffer).setUint32(0, Number(value), true);
  bytes.push(...new Uint8Array(buffer));
}

function writeFixed64(bytes: number[], value: bigint): void {
  const buffer = new ArrayBuffer(8);
  new DataView(buffer).setBigUint64(0, value, true);
  bytes.push(...new Uint8Array(buffer));
}

function writeLengthDelimited(bytes: number[], value: Uint8Array): void {
  writeVarint(bytes, BigInt(value.length));
  bytes.push(...value);
}

function writeVarint(bytes: number[], value: bigint): void {
  let remaining = value;
  while (remaining >= 0x80n) {
    bytes.push(Number((remaining & 0x7fn) | 0x80n));
    remaining >>= 7n;
  }
  bytes.push(Number(remaining));
}

function decodeBase64(name: string, value: unknown): Uint8Array {
  if (value instanceof Uint8Array) {
    return value;
  }
  if (typeof value !== "string") {
    throw new Error(`Field ${name} must be a base64 string.`);
  }
  try {
    const binary = atob(value);
    return Uint8Array.from(binary, (character) => character.charCodeAt(0));
  } catch {
    throw new Error(`Field ${name} must be a valid base64 string.`);
  }
}
