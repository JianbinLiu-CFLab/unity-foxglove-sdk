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

        private const string Crc32HelperPath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Utilities/Crc32Helper.cs";

        private const string DebugOverlayEnvelopePath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Utilities/FoxgloveDebugOverlayEnvelope.cs";

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
            BackgroundEncodePipelineDrainsIntoReusableList();
            PointCloudStrideScanShortCircuitsWhenLayoutIsKnown();
            StreamCrcUsesPooledBuffer();
            SingleValueDebugEnvelopeAvoidsIntermediateDictionary();
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
            using var encodeStarted = new ManualResetEventSlim(false);
            var pipeline = new BackgroundEncodePipeline<Phase140_7Request, int>(
                "phase140-7-slow-worker",
                completedCapacity: 2,
                stopWaitMs: 5,
                encode: request =>
                {
                    encodeStarted.Set();
                    Thread.Sleep(75);
                    return request.Value;
                });

            Check(pipeline.Enqueue(new Phase140_7Request { Value = 99 }, out _, out _),
                "140-7F-1: slow background encode request can be queued");
            Check(encodeStarted.Wait(1000),
                "140-7F-2: slow background encode worker enters the encode delegate");
            Check(!pipeline.Stop(clearCompleted: true, out var waitedForWorker) && waitedForWorker,
                "140-7F-3: slow background encode stop reports timeout after bounded wait");
            Thread.Sleep(100);
            var drained = new List<int>();
            pipeline.Drain(drained, out var dropped);
            Check(drained.Count == 0 && dropped == 0,
                "140-7F-4: timed-out background encode result is discarded by generation guard");
        }

        private static void BackgroundEncodePipelineDrainsIntoReusableList()
        {
            using var encodeCompleted = new ManualResetEventSlim(false);
            var pipeline = new BackgroundEncodePipeline<Phase140_7Request, int>(
                "phase140-7-reusable-drain",
                completedCapacity: 2,
                stopWaitMs: 100,
                encode: request =>
                {
                    encodeCompleted.Set();
                    return request.Value;
                });
            var results = new List<int> { -1 };

            Check(pipeline.Enqueue(new Phase140_7Request { Value = 42 }, out _, out _)
                  && encodeCompleted.Wait(1000),
                "140-7G-1: reusable drain pipeline produces a result");
            SpinWait.SpinUntil(() =>
            {
                pipeline.Drain(results, out _);
                return results.Count > 0;
            }, 1000);

            Check(results.Count == 1 && results[0] == 42,
                "140-7G-2: reusable drain clears and fills the caller-owned list");
            pipeline.Stop(clearCompleted: true, out _);
        }

        private static void PointCloudStrideScanShortCircuitsWhenLayoutIsKnown()
        {
            var source = ReadRepoText(PointCloudQoSPath);
            var method = ExtractMethodBody(source, "public static int ComputePackedStride");

            Check(method.Contains("if (hasIntensity && hasReflectivity && hasRing && hasTimeOffset)", StringComparison.Ordinal)
                  && method.Contains("break;", StringComparison.Ordinal),
                "140-7H-1: PointCloud stride scan stops once all optional fields are known");
        }

        private static void StreamCrcUsesPooledBuffer()
        {
            var source = ReadRepoText(Crc32HelperPath);
            var method = ExtractMethodBody(source, "public static uint Compute(Stream stream, long length)");
            var data = Enumerable.Range(0, 100_000).Select(i => (byte)i).ToArray();
            using var stream = new MemoryStream(data);

            Check(Crc32Helper.Compute(stream, data.Length) == Crc32Helper.Compute(data),
                "140-7I-1: pooled stream CRC remains byte-equivalent");
            Check(method.Contains("ArrayPool<byte>.Shared.Rent", StringComparison.Ordinal)
                  && method.Contains("ArrayPool<byte>.Shared.Return", StringComparison.Ordinal)
                  && method.Contains("finally", StringComparison.Ordinal)
                  && method.Contains("Math.Min(StreamBufferSize, remaining)", StringComparison.Ordinal),
                "140-7I-2: stream CRC returns its rented buffer in a finally block");
        }

        private static void SingleValueDebugEnvelopeAvoidsIntermediateDictionary()
        {
            var source = ReadRepoText(DebugOverlayEnvelopePath);
            var method = ExtractMethodBody(source, "public static bool TryCreateValue");

            Check(FoxgloveDebugOverlayEnvelope.TryCreateValue(
                    "/debug/phase140-7",
                    "Phase140_7Validation",
                    "value",
                    42,
                    null,
                    out var envelope)
                  && envelope.Values.Count == 1
                  && (int)envelope.Values["value"] == 42,
                "140-7J-1: single-value debug envelope preserves observable values");
            Check(!method.Contains("new Dictionary<string, object>", StringComparison.Ordinal),
                "140-7J-2: single-value debug envelope avoids an intermediate Dictionary");
        }

        private static void PublisherRateStateReviewFindingStaysClosedOnCurrentHead()
        {
            var source = ReadRepoText(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Publishing/FoxglovePublisherBase.cs");
            var onEnable = ExtractMethodBody(source, "protected virtual void OnEnable");

            Check(onEnable.Contains("_publishRateState = default;", StringComparison.Ordinal),
                "140-7K-1: stale indexed publisher-rate finding remains closed on current HEAD");
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
