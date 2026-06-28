// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Unity-free point-cloud visualization motion compensation.

using System;
using System.Collections.Generic;
using System.Numerics;
using Unity.FoxgloveSDK.Schemas.PointCloud;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>Coordinate convention for input point snapshots.</summary>
    internal enum PointCloudMotionCompensationInputConvention
    {
        /// <summary>Each raw point is expressed in the sensor frame at its acquisition time.</summary>
        AcquisitionTimeSensorFrame,
        /// <summary>
        /// Each raw point is already expressed in one scan reference sensor frame.
        /// This is kept for legacy callers that intentionally publish pre-aligned
        /// visualization points; raw PointCloud2 Native uses acquisition-time points.
        /// </summary>
        ScanReferenceSensorFrame
    }

    /// <summary>Motion-compensation request data cloned for background workers.</summary>
    internal sealed class PointCloudMotionCompensationRequest
    {
        /// <summary>Create a worker-safe motion-compensation request.</summary>
        public PointCloudMotionCompensationRequest(
            string topic,
            PointCloudMotionCompensationReferenceTime referenceTime,
            PointCloudMotionCompensationInputConvention inputConvention,
            SensorMotionPoseSample[] poseSamples)
        {
            Topic = PointCloudMotionCompensationOptions.NormalizeTopic(
                topic,
                PointCloudMotionCompensationOptions.DefaultDeskewedTopic);
            ReferenceTime = referenceTime;
            InputConvention = inputConvention;
            PoseSamples = poseSamples ?? Array.Empty<SensorMotionPoseSample>();
        }

        /// <summary>Output topic for the deskewed visualization frame.</summary>
        public string Topic { get; }

        /// <summary>Reference timestamp policy for the deskewed output.</summary>
        public PointCloudMotionCompensationReferenceTime ReferenceTime { get; }

        /// <summary>Coordinate convention used by the input point snapshot.</summary>
        public PointCloudMotionCompensationInputConvention InputConvention { get; }

        /// <summary>Cloned pose samples used by background workers.</summary>
        public SensorMotionPoseSample[] PoseSamples { get; }
    }

    /// <summary>Deskewed point snapshot and reference timestamp.</summary>
    internal sealed class PointCloudMotionCompensationResult
    {
        /// <summary>Create a completed motion-compensation result.</summary>
        public PointCloudMotionCompensationResult(VirtualLidarPointData[] points, int pointCount, ulong referenceUnixNs)
        {
            Points = points ?? throw new ArgumentNullException(nameof(points));
            PointCount = pointCount;
            ReferenceUnixNs = referenceUnixNs;
        }

        /// <summary>Deskewed or scan-reference point snapshot.</summary>
        public VirtualLidarPointData[] Points { get; }

        /// <summary>Number of valid source slots to read from <see cref="Points"/>.</summary>
        public int PointCount { get; }

        /// <summary>Reference timestamp assigned to the deskewed frame, in Unix nanoseconds.</summary>
        public ulong ReferenceUnixNs { get; }
    }

    /// <summary>
    /// Re-expresses rolling LiDAR points in one reference sensor frame for
    /// visualization. Raw VirtualLidar output remains the sensor-truth stream for
    /// SLAM front ends that perform their own IMU/time-offset deskew.
    /// </summary>
    internal static class PointCloudMotionCompensator
    {
        private const double NanosecondsPerSecond = 1_000_000_000d;

        /// <summary>
        /// Builds a deskewed VirtualLidar snapshot in one reference sensor frame.
        /// </summary>
        public static bool TryCompensateVirtualLidar(
            IReadOnlyList<VirtualLidarPointData> source,
            int pointCount,
            ulong scanStartUnixNs,
            PointCloudMotionCompensationRequest request,
            out PointCloudMotionCompensationResult result,
            out string error)
        {
            result = null;
            error = null;
            if (source == null)
            {
                error = "source points are missing";
                return false;
            }
            if (request == null)
            {
                error = "motion compensation request is missing";
                return false;
            }
            if (pointCount < 0 || pointCount > source.Count)
            {
                error = "point count is outside the source buffer";
                return false;
            }

            if (!TryGetTimeRange(source, pointCount, scanStartUnixNs, out var firstUnixNs, out var lastUnixNs))
            {
                error = "valid point time offsets are absent";
                return false;
            }

            if (request.InputConvention == PointCloudMotionCompensationInputConvention.ScanReferenceSensorFrame)
            {
                var referenceOutput = new VirtualLidarPointData[pointCount];
                CopyReferenceFramePoints(source, pointCount, referenceOutput);
                var scanReferenceUnixNs = ResolveReferenceUnixNs(firstUnixNs, lastUnixNs, request.ReferenceTime);
                result = new PointCloudMotionCompensationResult(referenceOutput, pointCount, scanReferenceUnixNs);
                return true;
            }

            var referenceUnixNs = ResolveReferenceUnixNs(firstUnixNs, lastUnixNs, request.ReferenceTime);
            if (request.PoseSamples.Length < 2
                || !SensorMotionPoseHistoryMath.TryInterpolate(request.PoseSamples, firstUnixNs, out _)
                || !SensorMotionPoseHistoryMath.TryInterpolate(request.PoseSamples, lastUnixNs, out _)
                || !SensorMotionPoseHistoryMath.TryInterpolate(request.PoseSamples, referenceUnixNs, out var referencePose))
            {
                error = "pose history does not cover the whole scan interval";
                return false;
            }

            var output = new VirtualLidarPointData[pointCount];

            if (!Matrix4x4.Invert(LocalToWorld(referencePose), out var worldToReference))
            {
                error = "reference pose is not invertible";
                return false;
            }

            var transformsByOffsetNs = new Dictionary<uint, Matrix4x4>();
            for (var i = 0; i < pointCount; i++)
            {
                var point = source[i];
                if (point.IsValid == 0)
                {
                    output[i] = point;
                    continue;
                }

                var offsetNs = TimeOffsetSecondsToNanoseconds(point.TimeOffsetSeconds);
                if (!transformsByOffsetNs.TryGetValue(offsetNs, out var sensorToReference))
                {
                    var pointUnixNs = AddNanoseconds(scanStartUnixNs, offsetNs);
                    if (!SensorMotionPoseHistoryMath.TryInterpolate(request.PoseSamples, pointUnixNs, out var pointPose))
                    {
                        error = "pose history does not cover a point timestamp";
                        return false;
                    }

                    sensorToReference = LocalToWorld(pointPose) * worldToReference;
                    transformsByOffsetNs[offsetNs] = sensorToReference;
                }

                var transformed = Vector3.Transform(GetAcquisitionPoint(point), sensorToReference);
                point.X = transformed.X;
                point.Y = transformed.Y;
                point.Z = transformed.Z;
                point.AcquisitionX = transformed.X;
                point.AcquisitionY = transformed.Y;
                point.AcquisitionZ = transformed.Z;
                point.TimeOffsetSeconds = 0f;
                point.HasAcquisitionFrame = 0;
                output[i] = point;
            }

            result = new PointCloudMotionCompensationResult(output, pointCount, referenceUnixNs);
            return true;
        }

        private static void CopyReferenceFramePoints(
            IReadOnlyList<VirtualLidarPointData> source,
            int pointCount,
            VirtualLidarPointData[] output)
        {
            for (var i = 0; i < pointCount; i++)
            {
                var point = source[i];
                if (point.IsValid != 0)
                {
                    point.TimeOffsetSeconds = 0f;
                    point.HasAcquisitionFrame = 0;
                }
                output[i] = point;
            }
        }

        private static Vector3 GetAcquisitionPoint(VirtualLidarPointData point)
        {
            if (point.HasAcquisitionFrame == 0)
                return new Vector3(point.X, point.Y, point.Z);

            return new Vector3(point.AcquisitionX, point.AcquisitionY, point.AcquisitionZ);
        }

        private static bool TryGetTimeRange(
            IReadOnlyList<VirtualLidarPointData> source,
            int pointCount,
            ulong scanStartUnixNs,
            out ulong firstUnixNs,
            out ulong lastUnixNs)
        {
            firstUnixNs = scanStartUnixNs;
            lastUnixNs = scanStartUnixNs;
            var found = false;
            for (var i = 0; i < pointCount; i++)
            {
                var point = source[i];
                if (point.IsValid == 0)
                    continue;

                var pointUnixNs = AddNanoseconds(scanStartUnixNs, TimeOffsetSecondsToNanoseconds(point.TimeOffsetSeconds));
                if (!found)
                {
                    firstUnixNs = pointUnixNs;
                    lastUnixNs = pointUnixNs;
                    found = true;
                    continue;
                }

                if (pointUnixNs < firstUnixNs)
                    firstUnixNs = pointUnixNs;
                if (pointUnixNs > lastUnixNs)
                    lastUnixNs = pointUnixNs;
            }

            return found;
        }

        private static ulong ResolveReferenceUnixNs(
            ulong firstUnixNs,
            ulong lastUnixNs,
            PointCloudMotionCompensationReferenceTime referenceTime)
        {
            switch (referenceTime)
            {
                case PointCloudMotionCompensationReferenceTime.ScanStart:
                    return firstUnixNs;
                case PointCloudMotionCompensationReferenceTime.ScanEnd:
                    return lastUnixNs;
                case PointCloudMotionCompensationReferenceTime.ScanMidpoint:
                default:
                    return firstUnixNs + ((lastUnixNs - firstUnixNs) / 2UL);
            }
        }

        private static Matrix4x4 LocalToWorld(SensorMotionPoseSample pose)
            => Matrix4x4.CreateFromQuaternion(pose.Rotation)
               * Matrix4x4.CreateTranslation(pose.Translation);

        private static uint TimeOffsetSecondsToNanoseconds(float seconds)
        {
            if (float.IsNaN(seconds) || seconds <= 0f)
                return 0U;

            var ns = Math.Round(seconds * NanosecondsPerSecond, MidpointRounding.AwayFromZero);
            if (ns <= 0d)
                return 0U;
            if (ns >= uint.MaxValue)
                return uint.MaxValue;
            return (uint)ns;
        }

        private static ulong AddNanoseconds(ulong unixNs, uint offsetNs)
            => offsetNs == 0U ? unixNs : checked(unixNs + offsetNs);
    }
}
