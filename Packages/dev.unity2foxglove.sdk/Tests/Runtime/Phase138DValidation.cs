// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 138D virtual IMU schema-registration contract checks.
//
// Note: VirtualImu and ImuMessageBuilder depend on UnityEngine (Rigidbody,
// Vector3, Quaternion, Physics) and are verified in Unity, not in this offline
// harness (same pattern as VirtualLidar / the 138C PointCloud2 mirror). Only the
// UnityEngine-free schema-registration path is asserted here.

using System;
using Foxglove.Schemas;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Regression coverage for the unity2foxglove.Imu protobuf schema descriptor.
    /// </summary>
    public static class Phase138DValidation
    {
        private const string ImuSchemaName = ImuSchema.SchemaName;

        /// <summary>
        /// Runs all 138D checks.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 138D: Virtual IMU Sensor ===");

            VerifyImuSchemaRegistration();

            Console.WriteLine("Phase 138D: all checks passed.");
            Console.WriteLine();
        }

        private static void VerifyImuSchemaRegistration()
        {
            var schemaRegistry = new DefaultSchemaRegistry();
            var protoRegistry = ProtobufSchemaRegistryLoader.FromBytes(ImuSchema.FileDescriptorSetData, schemaRegistry);
            protoRegistry.RegisterAll();

            Check(schemaRegistry.TryGetSchema(ImuSchemaName, "protobuf", out var schema),
                "138D-1: unity2foxglove.Imu is registered from ImuSchema");
            Check(schema.RawContent != null && schema.RawContent.Length > 0,
                "138D-2: unity2foxglove.Imu schema bytes are present");
            Check(protoRegistry.GetFileDescriptorSet(ImuSchemaName) != null,
                "138D-3: Imu descriptor subset is retrievable from protobuf registry");

            // Re-registering into a registry that already holds the schema (and its
            // bundled google.protobuf.Timestamp) must not throw — production registers
            // alongside the 46 official schemas.
            protoRegistry.RegisterAll();
            Check(schemaRegistry.TryGetSchema(ImuSchemaName, "protobuf", out _),
                "138D-4: duplicate schema registration is idempotent");
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException($"Phase 138D validation failed: {name}");
            Console.WriteLine($"[PASS] {name}");
        }
    }
}
