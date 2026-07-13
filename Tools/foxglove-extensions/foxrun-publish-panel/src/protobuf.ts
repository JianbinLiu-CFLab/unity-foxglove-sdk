// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/foxglove-extensions/foxrun-publish-panel
// Purpose: Strict, descriptor-derived scalar Protobuf encoder for direct FoxRun publication.

export type FoxRunSubscriptionField = {
  name: string;
  type: string;
  nullable: boolean;
  array: boolean;
  protobufFieldNumber: number;
};

const MAX_FIELD_NUMBER = 536_870_911;

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

  const bytes: number[] = [];
  for (const field of fields) {
    validateField(field);
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
      writeValue(bytes, field, value, wireType);
    }
  }

  return Uint8Array.from(bytes);
}

function validateField(field: FoxRunSubscriptionField): void {
  if (!Number.isInteger(field.protobufFieldNumber)
      || field.protobufFieldNumber < 1
      || field.protobufFieldNumber > MAX_FIELD_NUMBER) {
    throw new Error(`Field ${field.name} has an invalid Protobuf field number.`);
  }
}

function requireArray(name: string, value: unknown): readonly unknown[] {
  if (!Array.isArray(value)) {
    throw new Error(`Field ${name} must be an array.`);
  }
  return value;
}

function wireTypeFor(type: string): number {
  switch (normalizeType(type)) {
    case "double":
    case "float64":
      return 1;
    case "float":
    case "single":
    case "float32":
      return 5;
    case "string":
    case "bytes":
      return 2;
    case "bool":
    case "boolean":
    case "int":
    case "int32":
    case "int64":
    case "uint":
    case "uint32":
    case "uint64":
    case "sint32":
    case "sint64":
      return 0;
    case "fixed32":
    case "sfixed32":
      return 5;
    case "fixed64":
    case "sfixed64":
      return 1;
    default:
      throw new Error(`FoxRun Publish does not support Protobuf type ${type || "(empty)"}.`);
  }
}

function writeValue(bytes: number[], field: FoxRunSubscriptionField, value: unknown, wireType: number): void {
  switch (normalizeType(field.type)) {
    case "double":
    case "float64":
      writeFloat64(bytes, value, field.name);
      return;
    case "float":
    case "single":
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
    case "boolean":
      writeVarint(bytes, requireBoolean(field.name, value) ? 1n : 0n);
      return;
    case "int":
    case "int32":
    case "int64":
      writeVarint(bytes, BigInt.asUintN(64, requireInteger(field.name, value)));
      return;
    case "uint":
    case "uint32":
    case "uint64":
      writeVarint(bytes, requireUnsignedInteger(field.name, value));
      return;
    case "sint32":
    case "sint64": {
      const signed = requireInteger(field.name, value);
      writeVarint(bytes, (signed << 1n) ^ (signed >> 63n));
      return;
    }
    case "fixed32":
      writeFixed32(bytes, requireUnsignedInteger(field.name, value));
      return;
    case "sfixed32":
      writeFixed32(bytes, BigInt.asUintN(32, requireInteger(field.name, value)));
      return;
    case "fixed64":
      writeFixed64(bytes, requireUnsignedInteger(field.name, value));
      return;
    case "sfixed64":
      writeFixed64(bytes, BigInt.asUintN(64, requireInteger(field.name, value)));
      return;
    default:
      throw new Error(`FoxRun Publish does not support Protobuf type ${field.type || "(empty)"}.`);
  }
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

function requireUnsignedInteger(name: string, value: unknown): bigint {
  const parsed = requireInteger(name, value);
  if (parsed < 0n) {
    throw new Error(`Field ${name} must be unsigned.`);
  }
  return parsed;
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
  if (value > 0xffffffffn) {
    throw new Error("fixed32 value exceeds the Protobuf uint32 range.");
  }
  const buffer = new ArrayBuffer(4);
  new DataView(buffer).setUint32(0, Number(value), true);
  bytes.push(...new Uint8Array(buffer));
}

function writeFixed64(bytes: number[], value: bigint): void {
  if (value > 0xffffffffffffffffn) {
    throw new Error("fixed64 value exceeds the Protobuf uint64 range.");
  }
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
