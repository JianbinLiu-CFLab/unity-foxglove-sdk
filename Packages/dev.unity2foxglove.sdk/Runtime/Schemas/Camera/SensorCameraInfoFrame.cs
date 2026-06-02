// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Camera
// Purpose: Core-SDK camera calibration DTO for optional native ROS2 adapters.

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Schemas.Camera
{
    /// <summary>
    /// Unity-free camera info frame matching the standard pinhole camera layout.
    /// </summary>
    public sealed class SensorCameraInfoFrame
    {
        public SensorCameraInfoFrame(
            ulong unixNs,
            string frameId,
            uint width,
            uint height,
            string distortionModel,
            IReadOnlyList<double> d,
            IReadOnlyList<double> k,
            IReadOnlyList<double> r,
            IReadOnlyList<double> p)
        {
            UnixNs = unixNs;
            FrameId = frameId ?? string.Empty;
            Width = width;
            Height = height;
            DistortionModel = string.IsNullOrWhiteSpace(distortionModel) ? "plumb_bob" : distortionModel;
            D = d ?? Array.Empty<double>();
            K = RequireLength(k, 9, nameof(k));
            R = RequireLength(r, 9, nameof(r));
            P = RequireLength(p, 12, nameof(p));
        }

        public ulong UnixNs { get; }
        public string FrameId { get; }
        public uint Width { get; }
        public uint Height { get; }
        public string DistortionModel { get; }
        public IReadOnlyList<double> D { get; }
        public IReadOnlyList<double> K { get; }
        public IReadOnlyList<double> R { get; }
        public IReadOnlyList<double> P { get; }

        private static IReadOnlyList<double> RequireLength(IReadOnlyList<double> values, int expected, string name)
        {
            if (values == null)
                throw new ArgumentNullException(name);
            if (values.Count != expected)
                throw new ArgumentException($"{name} must contain exactly {expected} values.", name);
            return values;
        }
    }
}
