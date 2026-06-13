// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 140G IMU covariance and serializer allocation validation.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Foxglove.Schemas;
using Google.Protobuf;
using UnityEngine;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates IMU covariance round-trip and low-allocation serializer shape.
    /// </summary>
    public static class Phase140GValidation
    {
        private static readonly double[] OrientationCovariance =
        {
            1.1d, 0.1d, 0.2d,
            0.3d, 1.2d, 0.4d,
            0.5d, 0.6d, 1.3d
        };

        private static readonly double[] AngularVelocityCovariance =
        {
            2.1d, 0.7d, 0.8d,
            0.9d, 2.2d, 1.0d,
            1.1d, 1.2d, 2.3d
        };

        private static readonly double[] LinearAccelerationCovariance =
        {
            3.1d, 1.3d, 1.4d,
            1.5d, 3.2d, 1.6d,
            1.7d, 1.8d, 3.3d
        };

        private static int _passed;

        /// <summary>Runs all Phase 140G validation checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140G: IMU covariance and allocation ===");
            _passed = 0;

            WebSocketSerializerRoundTripsConfiguredCovariance();
            OrientationDisabledKeepsUnknownCovarianceMarker();
            LegacySerializeOverloadPreservesDefaultCovarianceBehavior();
            InvalidCovarianceLengthsThrowClearErrors();
            SerializerUsesExactByteBuffer();

            Console.WriteLine($"Phase 140G: {_passed} checks passed.");
        }

        private static void WebSocketSerializerRoundTripsConfiguredCovariance()
        {
            var bytes = SerializeWithConfiguredCovariance(includeOrientation: true);
            var covariance = ReadCovariances(bytes);

            CheckSequence(covariance[6], OrientationCovariance,
                "140G-1A: WebSocket IMU orientation covariance round-trips configured values");
            CheckSequence(covariance[7], AngularVelocityCovariance,
                "140G-1B: WebSocket IMU angular-velocity covariance round-trips configured values");
            CheckSequence(covariance[8], LinearAccelerationCovariance,
                "140G-1C: WebSocket IMU linear-acceleration covariance round-trips configured values");
        }

        private static void OrientationDisabledKeepsUnknownCovarianceMarker()
        {
            var bytes = SerializeWithConfiguredCovariance(includeOrientation: false);
            var covariance = ReadCovariances(bytes);

            Check(covariance[6][0] == -1d && covariance[6].Skip(1).All(value => value == 0d),
                "140G-2A: orientation-disabled WebSocket IMU keeps unknown orientation covariance marker");
            CheckSequence(covariance[7], AngularVelocityCovariance,
                "140G-2B: orientation-disabled WebSocket IMU still writes angular-velocity covariance");
            CheckSequence(covariance[8], LinearAccelerationCovariance,
                "140G-2C: orientation-disabled WebSocket IMU still writes linear-acceleration covariance");
        }

        private static void LegacySerializeOverloadPreservesDefaultCovarianceBehavior()
        {
            var included = ImuMessageBuilder.Serialize(
                1_234_567_890UL,
                "imu",
                new Vector3 { x = 1f, y = 2f, z = 3f },
                new Vector3 { x = 4f, y = 5f, z = 6f },
                new Quaternion { x = 0f, y = 0f, z = 0f, w = 1f },
                includeOrientation: true);
            var includedCovariance = ReadCovariances(included);
            Check(includedCovariance[6].All(value => value == 0d)
                  && includedCovariance[7].All(value => value == 0d)
                  && includedCovariance[8].All(value => value == 0d),
                "140G-3A: legacy WebSocket IMU overload preserves zero covariance defaults");

            var disabled = ImuMessageBuilder.Serialize(
                1_234_567_890UL,
                "imu",
                new Vector3 { x = 1f, y = 2f, z = 3f },
                new Vector3 { x = 4f, y = 5f, z = 6f },
                new Quaternion { x = 0f, y = 0f, z = 0f, w = 1f },
                includeOrientation: false);
            var disabledCovariance = ReadCovariances(disabled);
            Check(disabledCovariance[6][0] == -1d && disabledCovariance[6].Skip(1).All(value => value == 0d),
                "140G-3B: legacy WebSocket IMU overload preserves unknown orientation default");
        }

        private static void InvalidCovarianceLengthsThrowClearErrors()
        {
            CheckThrowsArgumentException(
                () => ImuMessageBuilder.Serialize(
                    1UL,
                    "imu",
                    new Vector3(),
                    new Vector3(),
                    new Quaternion { w = 1f },
                    includeOrientation: true,
                    Array.Empty<double>(),
                    AngularVelocityCovariance,
                    LinearAccelerationCovariance),
                "140G-4A: WebSocket IMU serializer rejects invalid orientation covariance length");
            CheckThrowsArgumentException(
                () => ImuMessageBuilder.Serialize(
                    1UL,
                    "imu",
                    new Vector3(),
                    new Vector3(),
                    new Quaternion { w = 1f },
                    includeOrientation: true,
                    OrientationCovariance,
                    new[] { 1d, 2d },
                    LinearAccelerationCovariance),
                "140G-4B: WebSocket IMU serializer rejects invalid angular-velocity covariance length");
            CheckThrowsArgumentException(
                () => ImuMessageBuilder.Serialize(
                    1UL,
                    "imu",
                    new Vector3(),
                    new Vector3(),
                    new Quaternion { w = 1f },
                    includeOrientation: true,
                    OrientationCovariance,
                    AngularVelocityCovariance,
                    new[] { 1d, 2d, 3d }),
                "140G-4C: WebSocket IMU serializer rejects invalid linear-acceleration covariance length");
        }

        private static void SerializerUsesExactByteBuffer()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Builders/ImuMessageBuilder.cs");
            Check(source.Contains("new byte[ComputeSerializedSize", StringComparison.Ordinal),
                "140G-5A: WebSocket IMU serializer writes into a pre-sized byte buffer");
            Check(source.Contains("CheckNoSpaceLeft()", StringComparison.Ordinal),
                "140G-5B: WebSocket IMU serializer verifies exact protobuf buffer sizing");
            Check(!source.Contains("new System.IO.MemoryStream", StringComparison.Ordinal)
                  && !source.Contains(".ToArray()", StringComparison.Ordinal),
                "140G-5C: WebSocket IMU serializer avoids MemoryStream and ToArray allocations");

            var virtualImu = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Imu/VirtualImu.cs");
            Check(virtualImu.Contains("ImuMessageBuilder.Serialize(", StringComparison.Ordinal)
                  && virtualImu.Contains("ImuOrientationCovariance", StringComparison.Ordinal)
                  && virtualImu.Contains("ImuAngularVelocityCovariance", StringComparison.Ordinal)
                  && virtualImu.Contains("ImuLinearAccelerationCovariance", StringComparison.Ordinal),
                "140G-5D: VirtualImu WebSocket path passes configured covariance to serializer");
        }

        private static byte[] SerializeWithConfiguredCovariance(bool includeOrientation)
            => ImuMessageBuilder.Serialize(
                1_234_567_890UL,
                "imu",
                new Vector3 { x = 1f, y = 2f, z = 3f },
                new Vector3 { x = 4f, y = 5f, z = 6f },
                new Quaternion { x = 0f, y = 0f, z = 0f, w = 1f },
                includeOrientation,
                OrientationCovariance,
                AngularVelocityCovariance,
                LinearAccelerationCovariance);

        private static Dictionary<int, double[]> ReadCovariances(byte[] payload)
        {
            var result = new Dictionary<int, double[]>();
            var input = new CodedInputStream(payload);
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                var field = WireFormat.GetTagFieldNumber(tag);
                if (field >= 6 && field <= 8)
                {
                    var length = input.ReadLength();
                    if (length % sizeof(double) != 0)
                        throw new Exception("[FAIL] covariance field " + field + " has invalid packed length");

                    var values = new List<double>(length / sizeof(double));
                    for (var i = 0; i < length / sizeof(double); i++)
                        values.Add(input.ReadDouble());

                    result[field] = values.ToArray();
                    continue;
                }

                input.SkipLastField();
            }

            return result;
        }

        private static void CheckSequence(IReadOnlyList<double> actual, IReadOnlyList<double> expected, string label)
        {
            if (actual.Count != expected.Count)
                throw new Exception("[FAIL] " + label + " length");

            for (var i = 0; i < expected.Count; i++)
            {
                if (actual[i] != expected[i])
                    throw new Exception("[FAIL] " + label + " index " + i);
            }

            Pass(label);
        }

        private static string Read(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (string.IsNullOrEmpty(root))
                throw new DirectoryNotFoundException("Could not find repository root for Phase140G validation.");
            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static void CheckThrowsArgumentException(Action action, string label)
        {
            try
            {
                action();
            }
            catch (ArgumentException)
            {
                Pass(label);
                return;
            }

            throw new Exception("[FAIL] " + label);
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);
            Pass(label);
        }

        private static void Pass(string label)
        {
            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
