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
        /// visualization points; raw PackedPointCloud Native uses acquisition-time points.
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

            var output = new VirtualLidarPointData[pointCount];
            if (!TryCompensateVirtualLidarInto(
                    source,
                    pointCount,
                    scanStartUnixNs,
                    request,
                    output,
                    out var outputPointCount,
                    out var referenceUnixNs,
                    out error))
            {
                return false;
            }

            result = new PointCloudMotionCompensationResult(output, outputPointCount, referenceUnixNs);
            return true;
        }

        /// <summary>
        /// Builds a deskewed snapshot into a caller-owned buffer.
        /// </summary>
        public static bool TryCompensateVirtualLidarInto(
            IReadOnlyList<VirtualLidarPointData> source,
            int pointCount,
            ulong scanStartUnixNs,
            PointCloudMotionCompensationRequest request,
            VirtualLidarPointData[] output,
            out int outputPointCount,
            out ulong referenceUnixNs,
            out string error)
        {
            outputPointCount = 0;
            referenceUnixNs = scanStartUnixNs;
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
            if (output == null)
            {
                error = "output buffer is missing";
                return false;
            }
            if (pointCount < 0 || pointCount > source.Count)
            {
                error = "point count is outside the source buffer";
                return false;
            }
            if (pointCount > output.Length)
            {
                error = "output buffer is smaller than the requested point count";
                return false;
            }

            if (!TryResolveReferenceTimeRange(
                    source,
                    pointCount,
                    scanStartUnixNs,
                    request,
                    out var firstUnixNs,
                    out var lastUnixNs,
                    out referenceUnixNs,
                    out var hasValidPoints,
                    out error))
            {
                return false;
            }

            if (!hasValidPoints)
            {
                CopyReferenceFramePoints(source, pointCount, output);
                outputPointCount = pointCount;
                return true;
            }

            if (request.InputConvention == PointCloudMotionCompensationInputConvention.ScanReferenceSensorFrame)
            {
                CopyReferenceFramePoints(source, pointCount, output);
                outputPointCount = pointCount;
                return true;
            }

            if (request.PoseSamples.Length < 2
                || !SensorMotionPoseHistoryMath.TryInterpolate(request.PoseSamples, firstUnixNs, out _)
                || !SensorMotionPoseHistoryMath.TryInterpolate(request.PoseSamples, lastUnixNs, out _)
                || !SensorMotionPoseHistoryMath.TryInterpolate(request.PoseSamples, referenceUnixNs, out var referencePose))
            {
                error = "pose history does not cover the whole scan interval";
                return false;
            }

            if (!Matrix4x4.Invert(LocalToWorld(referencePose), out var worldToReference))
            {
                error = "reference pose is not invertible";
                return false;
            }

            var poseSearchIndex = 0;
            var hasLastTransform = false;
            var lastOffsetNs = 0U;
            var lastSensorToReference = Matrix4x4.Identity;
            for (var i = 0; i < pointCount; i++)
            {
                var point = source[i];
                if (point.IsValid == 0)
                {
                    output[i] = point;
                    continue;
                }

                if (!TryTimeOffsetSecondsToNanoseconds(
                        point.TimeOffsetSeconds,
                        out var offsetNs,
                        out error))
                {
                    return false;
                }
                Matrix4x4 sensorToReference;
                if (hasLastTransform && offsetNs == lastOffsetNs)
                {
                    sensorToReference = lastSensorToReference;
                }
                else
                {
                    var pointUnixNs = AddNanoseconds(scanStartUnixNs, offsetNs);
                    if (!SensorMotionPoseHistoryMath.TryInterpolateMonotonic(
                            request.PoseSamples,
                            pointUnixNs,
                            ref poseSearchIndex,
                            out var pointPose))
                    {
                        error = "pose history does not cover a point timestamp";
                        return false;
                    }

                    sensorToReference = LocalToWorld(pointPose) * worldToReference;
                    hasLastTransform = true;
                    lastOffsetNs = offsetNs;
                    lastSensorToReference = sensorToReference;
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

            outputPointCount = pointCount;
            return true;
        }

        /// <summary>Resolve the deskewed output timestamp without materializing a second point snapshot.</summary>
        public static bool TryResolveReferenceUnixNs(
            IReadOnlyList<VirtualLidarPointData> source,
            int pointCount,
            ulong scanStartUnixNs,
            PointCloudMotionCompensationRequest request,
            out ulong referenceUnixNs,
            out string error)
        {
            return TryResolveReferenceTimeRange(
                source,
                pointCount,
                scanStartUnixNs,
                request,
                out _,
                out _,
                out referenceUnixNs,
                out _,
                out error);
        }

        private static bool TryResolveReferenceTimeRange(
            IReadOnlyList<VirtualLidarPointData> source,
            int pointCount,
            ulong scanStartUnixNs,
            PointCloudMotionCompensationRequest request,
            out ulong firstUnixNs,
            out ulong lastUnixNs,
            out ulong referenceUnixNs,
            out bool hasValidPoints,
            out string error)
        {
            firstUnixNs = scanStartUnixNs;
            lastUnixNs = scanStartUnixNs;
            referenceUnixNs = scanStartUnixNs;
            hasValidPoints = false;
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
            if (!TryGetTimeRange(
                    source,
                    pointCount,
                    scanStartUnixNs,
                    out firstUnixNs,
                    out lastUnixNs,
                    out hasValidPoints,
                    out error))
            {
                return false;
            }
            if (!hasValidPoints)
                return true;

            referenceUnixNs = ResolveReferenceUnixNs(firstUnixNs, lastUnixNs, request.ReferenceTime);
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
            out ulong lastUnixNs,
            out bool found,
            out string error)
        {
            firstUnixNs = scanStartUnixNs;
            lastUnixNs = scanStartUnixNs;
            found = false;
            error = null;
            for (var i = 0; i < pointCount; i++)
            {
                var point = source[i];
                if (point.IsValid == 0)
                    continue;

                if (!TryTimeOffsetSecondsToNanoseconds(
                        point.TimeOffsetSeconds,
                        out var offsetNs,
                        out error))
                {
                    return false;
                }

                var pointUnixNs = AddNanoseconds(scanStartUnixNs, offsetNs);
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

            return true;
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

        private static bool TryTimeOffsetSecondsToNanoseconds(
            float seconds,
            out uint nanoseconds,
            out string error)
        {
            if (!float.IsNaN(seconds)
                && !float.IsInfinity(seconds)
                && seconds < 0f)
            {
                nanoseconds = 0U;
                error = "point time offsets must be non-negative";
                return false;
            }

            nanoseconds = PointCloudPackedDataBuilder.TimeOffsetSecondsToNanoseconds(seconds);
            error = null;
            return true;
        }

        private static ulong AddNanoseconds(ulong unixNs, uint offsetNs)
            => offsetNs == 0U ? unixNs : checked(unixNs + offsetNs);
    }
}
