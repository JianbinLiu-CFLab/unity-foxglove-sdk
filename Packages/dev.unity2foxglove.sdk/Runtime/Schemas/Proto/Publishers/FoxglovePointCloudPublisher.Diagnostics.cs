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

        private static double ElapsedPointCloud2NativeMilliseconds(long startTimestamp)
            => (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;

        private static string FormatPointCloud2NativeMilliseconds(double milliseconds)
            => milliseconds.ToString("F2", CultureInfo.InvariantCulture);
    }
}
