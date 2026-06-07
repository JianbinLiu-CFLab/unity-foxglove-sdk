// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates Phase 140-7 protocol frame and runtime utility review fixes.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Review-driven validation for protocol frame and runtime utility defects found in Phase 140-7.
    /// </summary>
    public static class Phase140_7Validation
    {
        private const string BinaryEncodingPath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Protocol/BinaryEncoding.cs";

        private const string PointCloudQoSPath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Utilities/PointCloudQoS.cs";

        private static int _passed;

        /// <summary>Runs all Phase 140-7 protocol and utility review checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-7: Protocol frames and runtime utilities review fixes ===");
            _passed = 0;

            PlaybackStateRequestIdEncodeCapIsEnforced();
            FetchAssetSuccessUsesSharedLittleEndianWriter();
            ServerMessageDataTestDecoderIsHiddenFromPublicBrowsing();
            RosTransformMathRejectsNonFiniteInputAndNormalizesFiniteOutput();
            DropOldestBoundedQueueHasConcurrentStressCoverage();
            BackgroundEncodePipelineTimeoutInvalidatesStaleWorkerResults();
            PointCloudStrideScanShortCircuitsWhenLayoutIsKnown();
            PublisherRateStateReviewFindingStaysClosedOnCurrentHead();

            Console.WriteLine($"Phase 140-7: {_passed} checks passed.");
        }

        private static void PlaybackStateRequestIdEncodeCapIsEnforced()
        {
            var atCap = new string('a', BinaryEncoding.MaxPlaybackRequestIdBytes);
            var frame = BinaryEncoding.EncodePlaybackState(0, 123UL, 1f, false, atCap);
            var encodedLength = BinaryEncoding.ReadU32LE(frame, 15);

            Check(encodedLength == BinaryEncoding.MaxPlaybackRequestIdBytes,
                "140-7A-1: PlaybackState encodes request id exactly at the protocol cap");
            CheckThrows<ArgumentOutOfRangeException>(
                () => BinaryEncoding.EncodePlaybackState(
                    0,
                    123UL,
                    1f,
                    false,
                    new string('b', BinaryEncoding.MaxPlaybackRequestIdBytes + 1)),
                "140-7A-2: PlaybackState rejects request id above the protocol cap");
        }

        private static void FetchAssetSuccessUsesSharedLittleEndianWriter()
        {
            var source = ReadRepoText(BinaryEncodingPath);
            var success = ExtractMethodBody(source, "public static byte[] EncodeFetchAssetResponseSuccess");
            var frame = BinaryEncoding.EncodeFetchAssetResponseSuccess(42U, new byte[] { 1, 2, 3 });

            Check(BinaryEncoding.ReadU32LE(frame, 6) == 0U,
                "140-7B-1: fetchAsset success frame still encodes zero errorMessageLen");
            Check(success.Contains("WriteU32LE(frame, 6, 0u);", StringComparison.Ordinal)
                  && !success.Contains("frame[6] = 0; frame[7] = 0; frame[8] = 0; frame[9] = 0;", StringComparison.Ordinal),
                "140-7B-2: fetchAsset success frame uses shared little-endian writer for errorMessageLen");
        }

        private static void ServerMessageDataTestDecoderIsHiddenFromPublicBrowsing()
        {
            var method = typeof(BinaryEncoding).GetMethod(
                nameof(BinaryEncoding.TryDecodeServerMessageData),
                BindingFlags.Public | BindingFlags.Static);
            var attribute = method?.GetCustomAttributes(typeof(EditorBrowsableAttribute), inherit: false)
                .OfType<EditorBrowsableAttribute>()
                .SingleOrDefault();

            Check(attribute != null && attribute.State == EditorBrowsableState.Never,
                "140-7C-1: roundtrip-only ServerMessageData decoder is hidden from public API browsing");
        }

        private static void RosTransformMathRejectsNonFiniteInputAndNormalizesFiniteOutput()
        {
            CheckThrows<ArgumentOutOfRangeException>(
                () => RosTransformMath.RollPitchYawDegreesToQuaternion(double.NaN, 0d, 0d),
                "140-7D-1: ROS RPY conversion rejects NaN inputs");
            CheckThrows<ArgumentOutOfRangeException>(
                () => RosTransformMath.RollPitchYawDegreesToQuaternion(0d, double.PositiveInfinity, 0d),
                "140-7D-2: ROS RPY conversion rejects infinite inputs");

            var q = RosTransformMath.RollPitchYawDegreesToQuaternion(720d, -360d, 180d);
            var magnitude = Math.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W);
            Check(Math.Abs(magnitude - 1d) < 0.00001d,
                "140-7D-3: ROS RPY conversion returns normalized finite quaternions");
        }

        private static void DropOldestBoundedQueueHasConcurrentStressCoverage()
        {
            var queue = new DropOldestBoundedQueue<int>(32);
            var errors = new List<Exception>();
            var consumed = 0;
            var done = false;

            var producer = new Thread(() =>
            {
                try
                {
                    for (var i = 0; i < 10_000; i++)
                        queue.Enqueue(i);
                }
                catch (Exception ex)
                {
                    lock (errors)
                        errors.Add(ex);
                }
                finally
                {
                    Volatile.Write(ref done, true);
                }
            });

            var consumer = new Thread(() =>
            {
                try
                {
                    while (!Volatile.Read(ref done) || queue.Count > 0)
                    {
                        if (queue.TryDequeue(out _))
                            consumed++;
                    }
                }
                catch (Exception ex)
                {
                    lock (errors)
                        errors.Add(ex);
                }
            });

            producer.Start();
            consumer.Start();
            producer.Join();
            consumer.Join();

            Check(errors.Count == 0 && consumed > 0,
                "140-7E-1: DropOldestBoundedQueue tolerates concurrent enqueue/dequeue stress");
        }

        private static void BackgroundEncodePipelineTimeoutInvalidatesStaleWorkerResults()
        {
            var pipeline = new BackgroundEncodePipeline<Phase140_7Request, int>(
                "phase140-7-slow-worker",
                completedCapacity: 2,
                stopWaitMs: 5,
                encode: request =>
                {
                    Thread.Sleep(75);
                    return request.Value;
                });

            Check(pipeline.Enqueue(new Phase140_7Request { Value = 99 }, out _, out _),
                "140-7F-1: slow background encode request can be queued");
            Thread.Sleep(10);
            Check(!pipeline.Stop(clearCompleted: true, out var waitedForWorker) && waitedForWorker,
                "140-7F-2: slow background encode stop reports timeout after bounded wait");
            Thread.Sleep(100);
            var drained = pipeline.Drain(out var dropped);
            Check((drained == null || drained.Count == 0) && dropped == 0,
                "140-7F-3: timed-out background encode result is discarded by generation guard");
        }

        private static void PointCloudStrideScanShortCircuitsWhenLayoutIsKnown()
        {
            var source = ReadRepoText(PointCloudQoSPath);
            var method = ExtractMethodBody(source, "public static int ComputePackedStride");

            Check(method.Contains("if (hasIntensity && hasReflectivity && hasRing && hasTimeOffset)", StringComparison.Ordinal)
                  && method.Contains("break;", StringComparison.Ordinal),
                "140-7G-1: PointCloud stride scan stops once all optional fields are known");
        }

        private static void PublisherRateStateReviewFindingStaysClosedOnCurrentHead()
        {
            var source = ReadRepoText(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Publishing/FoxglovePublisherBase.cs");
            var onEnable = ExtractMethodBody(source, "protected virtual void OnEnable");

            Check(onEnable.Contains("_publishRateState = default;", StringComparison.Ordinal),
                "140-7H-1: stale indexed publisher-rate finding remains closed on current HEAD");
        }

        private sealed class Phase140_7Request : IBackgroundEncodeRequest
        {
            public int Generation { get; set; }
            public int Value { get; set; }
        }

        private static string ExtractMethodBody(string source, string signaturePrefix)
        {
            var signatureIndex = source.IndexOf(signaturePrefix, StringComparison.Ordinal);
            if (signatureIndex < 0)
                return string.Empty;
            var braceIndex = source.IndexOf('{', signatureIndex);
            if (braceIndex < 0)
                return string.Empty;

            var depth = 0;
            for (var i = braceIndex; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(braceIndex, i - braceIndex + 1);
                }
            }

            return string.Empty;
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");

            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static void CheckThrows<TException>(Action action, string message)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                _passed++;
                Console.WriteLine("[PASS] " + message);
                return;
            }

            throw new Exception("[FAIL] " + message);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new Exception("[FAIL] " + message);

            _passed++;
            Console.WriteLine("[PASS] " + message);
        }
    }
}
