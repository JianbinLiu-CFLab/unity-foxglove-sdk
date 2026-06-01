// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/PointCloud
// Purpose: P/Invoke wrapper for the bundled Draco point-cloud native plugin.

using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.PointCloud;

namespace Foxglove.Schemas.PointCloud
{
    /// <summary>Encodes point-cloud XYZ data through the bundled native Draco plugin.</summary>
    public static class DracoPointCloudNativeEncoder
    {
        public const string NativeLibraryName = "Unity2FoxgloveDracoNative";
        public const int XyzBytesPerPoint = 3 * sizeof(float);
        public const int MaxInputBytes = PointCloudPackedDataBuilder.MaxPackedDataBytes;
        public const int MaxInputPoints = MaxInputBytes / XyzBytesPerPoint;

        private const int MaxPayloadBytes = 64 * 1024 * 1024;
        private const int OutputOverheadBytes = 1024;
        private const int ResultOk = 0;
        private const int ResultOutputTooSmall = -4;

        /// <summary>Return true when the native Draco plugin can be loaded and queried.</summary>
        public static bool TryGetAvailability(out string versionOrError)
        {
            versionOrError = "";

            try
            {
                var buffer = new byte[256];
                var result = NativeMethods.U2FDracoGetVersion(buffer, buffer.Length);
                if (result < 0)
                {
                    versionOrError = DescribeResult(result);
                    return false;
                }

                var length = Array.IndexOf(buffer, (byte)0);
                if (length < 0)
                    length = buffer.Length;

                versionOrError = Encoding.ASCII.GetString(buffer, 0, length);
                return true;
            }
            catch (DllNotFoundException ex)
            {
                versionOrError = "Native Draco plugin DLL was not found: " + ex.Message;
                return false;
            }
            catch (EntryPointNotFoundException ex)
            {
                versionOrError = "Native Draco plugin entry point is missing: " + ex.Message;
                return false;
            }
            catch (BadImageFormatException ex)
            {
                versionOrError = "Native Draco plugin has an incompatible binary format: " + ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                versionOrError = ex.Message;
                return false;
            }
        }

        /// <summary>Encode one frame as Draco bytes suitable for CompressedPointCloud.data.</summary>
        public static bool TryEncode(PointCloudFrame frame, out byte[] dracoPayload, out string error)
        {
            dracoPayload = null;
            error = "";

            var pointCount = frame.GetPointCount();
            if (frame == null || pointCount == 0)
            {
                error = "Draco point-cloud frame is empty.";
                return false;
            }

            if (!ValidateInputBudget(pointCount, out error))
                return false;

            var xyz = BuildXyzArray(frame);
            var initialCapacity = checked(Math.Min(
                MaxPayloadBytes,
                Math.Max(4096, xyz.Length * sizeof(float) + OutputOverheadBytes)));

            return TryEncodeWithCapacity(xyz, pointCount, initialCapacity, out dracoPayload, out error);
        }

        internal static bool TryEncodeVirtualLidarPoints(
            VirtualLidarPointData[] points,
            int pointCount,
            out byte[] dracoPayload,
            out string error,
            out int validCount)
        {
            dracoPayload = null;
            error = "";
            validCount = 0;

            if (points == null || pointCount <= 0)
            {
                error = "Draco virtual LiDAR point snapshot is empty.";
                return false;
            }

            pointCount = Math.Min(pointCount, points.Length);
            var xyz = new float[checked(pointCount * 3)];
            for (var i = 0; i < pointCount; i++)
            {
                var point = points[i];
                if (point.IsValid == 0)
                    continue;

                var offset = validCount * 3;
                xyz[offset] = point.X;
                xyz[offset + 1] = point.Y;
                xyz[offset + 2] = point.Z;
                validCount++;
            }

            if (validCount == 0)
            {
                error = "Draco virtual LiDAR point snapshot has no valid points.";
                return false;
            }

            if (!ValidateInputBudget(validCount, out error))
                return false;

            var initialCapacity = checked(Math.Min(
                MaxPayloadBytes,
                Math.Max(4096, validCount * XyzBytesPerPoint + OutputOverheadBytes)));

            return TryEncodeWithCapacity(xyz, validCount, initialCapacity, out dracoPayload, out error);
        }

        internal static bool ValidateInputBudget(int pointCount, out string error)
        {
            var inputBytes = (long)pointCount * XyzBytesPerPoint;
            if (pointCount > MaxInputPoints || inputBytes > MaxInputBytes)
            {
                error = "Draco point-cloud XYZ input exceeds "
                    + MaxInputBytes
                    + " bytes ("
                    + inputBytes
                    + " requested).";
                return false;
            }

            error = "";
            return true;
        }

        private static bool TryEncodeWithCapacity(
            float[] xyz,
            int pointCount,
            int outputCapacity,
            out byte[] dracoPayload,
            out string error)
        {
            dracoPayload = null;
            error = "";

            var capacity = outputCapacity;
            for (var attempt = 0; attempt < 2; ++attempt)
            {
                var output = ArrayPool<byte>.Shared.Rent(capacity);
                var xyzHandle = default(GCHandle);
                var outputHandle = default(GCHandle);
                try
                {
                    xyzHandle = GCHandle.Alloc(xyz, GCHandleType.Pinned);
                    outputHandle = GCHandle.Alloc(output, GCHandleType.Pinned);

                    var result = NativeMethods.U2FDracoEncodePointCloud(
                        xyzHandle.AddrOfPinnedObject(),
                        pointCount,
                        outputHandle.AddrOfPinnedObject(),
                        output.Length,
                        out var bytesWritten);

                    if (result == ResultOk)
                    {
                        if (bytesWritten <= 0 || bytesWritten > output.Length)
                        {
                            error = "Native Draco plugin returned an invalid payload length: " + bytesWritten;
                            return false;
                        }

                        dracoPayload = new byte[bytesWritten];
                        Buffer.BlockCopy(output, 0, dracoPayload, 0, bytesWritten);
                        return true;
                    }

                    if (result == ResultOutputTooSmall
                        && bytesWritten > capacity
                        && bytesWritten <= MaxPayloadBytes)
                    {
                        capacity = bytesWritten;
                        continue;
                    }

                    error = DescribeResult(result);
                    return false;
                }
                catch (DllNotFoundException ex)
                {
                    error = "Native Draco plugin DLL was not found: " + ex.Message;
                    return false;
                }
                catch (EntryPointNotFoundException ex)
                {
                    error = "Native Draco plugin entry point is missing: " + ex.Message;
                    return false;
                }
                catch (BadImageFormatException ex)
                {
                    error = "Native Draco plugin has an incompatible binary format: " + ex.Message;
                    return false;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
                finally
                {
                    if (outputHandle.IsAllocated)
                        outputHandle.Free();
                    if (xyzHandle.IsAllocated)
                        xyzHandle.Free();
                    ArrayPool<byte>.Shared.Return(output);
                }
            }

            error = "Native Draco plugin required a larger output buffer than allowed.";
            return false;
        }

        private static float[] BuildXyzArray(PointCloudFrame frame)
        {
            var pointCount = frame.GetPointCount();
            var xyz = new float[checked(pointCount * 3)];
            var index = 0;
            for (var i = 0; i < pointCount; i++)
            {
                var point = frame.Points[i];
                xyz[index++] = point.X;
                xyz[index++] = point.Y;
                xyz[index++] = point.Z;
            }

            return xyz;
        }

        private static string DescribeResult(int result)
        {
            switch (result)
            {
                case -1:
                    return "Native Draco plugin received invalid arguments.";
                case -2:
                    return "Native Draco plugin rejected the point count as too large.";
                case -3:
                    return "Native Draco plugin failed to encode the point cloud.";
                case -4:
                    return "Native Draco plugin output buffer was too small.";
                case -5:
                    return "Native Draco plugin threw an unhandled native exception.";
                default:
                    return "Native Draco plugin failed with result code " + result + ".";
            }
        }

        private static class NativeMethods
        {
            [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
            public static extern int U2FDracoEncodePointCloud(
                IntPtr xyz,
                int pointCount,
                IntPtr output,
                int outputCapacity,
                out int bytesWritten);

            [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
            public static extern int U2FDracoGetVersion(
                [Out] byte[] buffer,
                int capacity);
        }
    }
}
