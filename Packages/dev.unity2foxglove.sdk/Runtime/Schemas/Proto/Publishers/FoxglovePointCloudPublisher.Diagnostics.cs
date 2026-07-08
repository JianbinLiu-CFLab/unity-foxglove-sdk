// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Point-cloud publisher diagnostic logging helpers.

using System.Globalization;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxglovePointCloudPublisher
    {
        private readonly object[] _pointCloud2NativeWorkerTimingArgs = new object[24];

        private void LogPointCloudDiagnosticMessage(string format, object[] args)
        {
            Debug.LogFormat(
                LogType.Log,
                LogOption.NoStacktrace,
                this,
                format,
                args);
        }

        private long BeginPointCloud2NativeTiming()
            => _logPerformanceDiagnostics ? Stopwatch.GetTimestamp() : 0L;

        private void LogPointCloud2NativeTiming(
            long startTimestamp,
            string stage,
            string topic,
            int pointCount,
            int byteCount)
        {
            if (!_logPerformanceDiagnostics || startTimestamp == 0L)
                return;

            Debug.LogFormat(
                LogType.Log,
                LogOption.NoStacktrace,
                this,
                "[Foxglove] PointCloud2 native timing: stage={0} topic={1} points={2} bytes={3} elapsedMs={4}",
                new object[]
                {
                    string.IsNullOrWhiteSpace(stage) ? "unknown" : stage,
                    string.IsNullOrWhiteSpace(topic) ? "(none)" : topic,
                    pointCount,
                    byteCount,
                    FormatPointCloud2NativeMilliseconds(ElapsedPointCloud2NativeMilliseconds(startTimestamp))
                });
        }

        private void LogPointCloud2NativeTiming(long startTimestamp, string stage, PointCloud2NativeFrame frame)
        {
            LogPointCloud2NativeTiming(
                startTimestamp,
                stage,
                frame == null ? string.Empty : frame.Topic,
                frame == null ? 0 : frame.ValidCount,
                frame == null || frame.Data == null ? 0 : frame.Data.Length);
        }

        private void LogPointCloud2NativeWorkerTiming(PointCloud2NativeResult result)
        {
            if (!_logPerformanceDiagnostics || result == null)
                return;

            var encodeDiagnostics = result.EncodeDiagnostics;
            var args = _pointCloud2NativeWorkerTimingArgs;
            args[0] = result.NativeFrame == null || string.IsNullOrWhiteSpace(result.NativeFrame.Topic)
                ? "(none)"
                : result.NativeFrame.Topic;
            args[1] = result.ValidCount;
            args[2] = result.PayloadBytes;
            args[3] = FormatPointCloud2NativeMilliseconds(result.RawPackMs);
            args[4] = FormatPointCloud2NativeMilliseconds(result.RawPayloadBuildMs);
            args[5] = FormatPointCloud2NativeMilliseconds(result.MotionCompensationMs);
            args[6] = FormatPointCloud2NativeMilliseconds(result.DeskewPackMs);
            args[7] = FormatPointCloud2NativeMilliseconds(result.EncodeMs);
            args[8] = result.Success;
            args[9] = FormatPointCloud2NativeMilliseconds(encodeDiagnostics.RawCountValidMs);
            args[10] = FormatPointCloud2NativeMilliseconds(encodeDiagnostics.RawBufferRentMs);
            args[11] = FormatPointCloud2NativeMilliseconds(encodeDiagnostics.RawWriteLoopMs);
            args[12] = encodeDiagnostics.RawBufferLength;
            args[13] = encodeDiagnostics.RawBufferReused;
            args[14] = FormatPointCloud2NativeMilliseconds(encodeDiagnostics.DeskewCountValidMs);
            args[15] = FormatPointCloud2NativeMilliseconds(encodeDiagnostics.DeskewBufferRentMs);
            args[16] = FormatPointCloud2NativeMilliseconds(encodeDiagnostics.DeskewWriteLoopMs);
            args[17] = encodeDiagnostics.DeskewBufferLength;
            args[18] = encodeDiagnostics.DeskewBufferReused;
            args[19] = encodeDiagnostics.GcGen0Delta;
            args[20] = encodeDiagnostics.GcGen1Delta;
            args[21] = encodeDiagnostics.GcGen2Delta;
            args[22] = encodeDiagnostics.PoolRetainedBuffers;
            args[23] = encodeDiagnostics.PoolRetainedBytes;

            Debug.LogFormat(
                LogType.Log,
                LogOption.NoStacktrace,
                this,
                "[Foxglove] PointCloud2 native worker timing: topic={0} points={1} bytes={2} rawPackMs={3} rawPayloadBuildMs={4} motionCompensationMs={5} deskewPackMs={6} encodeMs={7} success={8} " +
                "rawCountValidMs={9} rawBufRentMs={10} rawWriteLoopMs={11} rawBufLen={12} rawBufReused={13} " +
                "deskewCountValidMs={14} deskewBufRentMs={15} deskewWriteLoopMs={16} deskewBufLen={17} deskewBufReused={18} " +
                "gcDelta={19}/{20}/{21} poolRetained={22}/{23}",
                args);
        }

        private static double ElapsedPointCloud2NativeMilliseconds(long startTimestamp)
            => (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;

        private static string FormatPointCloud2NativeMilliseconds(double milliseconds)
            => milliseconds.ToString("F2", CultureInfo.InvariantCulture);
    }
}
