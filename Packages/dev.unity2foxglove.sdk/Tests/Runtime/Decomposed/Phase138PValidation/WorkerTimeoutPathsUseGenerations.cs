// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 138P code-review remediation regression coverage.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Foxglove.Schemas.Video;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.PointCloud;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Regression checks for Phase 138P code-review remediation.
    /// </summary>
    public static partial class Phase138PValidation
    {
        private static void WorkerTimeoutPathsUseGenerations()
        {
            var camera = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");
            var cameraPublishPipeline = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraJpegPublishPipeline.cs");
            var cameraPipeline = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraJpegPipeline.cs");
            var pointcloud = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");
            var pointcloudPipeline = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudEncodePipeline.cs");
            var pipeline = Read("Packages/dev.unity2foxglove.sdk/Runtime/Utilities/BackgroundEncodePipeline.cs");

            Check(camera.Contains("CameraJpegPublishPipeline _jpegPublishPipeline", StringComparison.Ordinal)
                  && cameraPublishPipeline.Contains("CameraJpegPipeline _jpegPipeline", StringComparison.Ordinal)
                  && cameraPipeline.Contains("_workerGeneration", StringComparison.Ordinal)
                  && cameraPipeline.Contains("WorkerGeneration", StringComparison.Ordinal)
                  && cameraPipeline.Contains("Interlocked.Increment(ref _workerGeneration)", StringComparison.Ordinal)
                  && cameraPipeline.Contains("request.Generation != _currentCaptureGeneration()", StringComparison.Ordinal)
                  && cameraPipeline.Contains("request.JpegWorkerGeneration != workerGeneration", StringComparison.Ordinal),
                "138P-15A: camera JPEG worker timeout/restart is generation-guarded");
            Check(pipeline.Contains("_worker.ShouldStopLocked(workerGeneration)", StringComparison.Ordinal)
                  && pipeline.Contains("request.Generation", StringComparison.Ordinal)
                  && pipeline.Contains("_worker.InvalidateTimedOutWorkerLocked()", StringComparison.Ordinal)
                  && pointcloudPipeline.Contains("BackgroundEncodePipeline<TRequest, TResult> _pipeline", StringComparison.Ordinal)
                  && pointcloud.Contains("PointCloudEncodePipeline<DracoEncodeRequest, DracoEncodeResult> _dracoEncodePipeline", StringComparison.Ordinal)
                  && pointcloud.Contains("PointCloudEncodePipeline<PackedPointCloudRequest, PackedPointCloudResult> _packedPointCloudPipeline", StringComparison.Ordinal),
                "138P-15B: pointcloud workers timeout/restart are generation-guarded");

            var lifecycle = Read("Packages/dev.unity2foxglove.sdk/Runtime/Utilities/BackgroundWorkerLifecycle.cs");
            Check(lifecycle.Contains("internal sealed class BackgroundWorkerLifecycle", StringComparison.Ordinal)
                  && lifecycle.Contains("StartOrReuseLocked", StringComparison.Ordinal)
                  && lifecycle.Contains("InvalidateTimedOutWorkerLocked", StringComparison.Ordinal)
                  && pipeline.Contains("private readonly BackgroundWorkerLifecycle _worker", StringComparison.Ordinal)
                  && pipeline.Contains("StartOrReuseLocked(out startWorker)", StringComparison.Ordinal)
                  && pipeline.Contains("InvalidateTimedOutWorkerLocked()", StringComparison.Ordinal)
                  && !pointcloud.Contains("_dracoEncodeWorkerGeneration", StringComparison.Ordinal)
                  && !pointcloud.Contains("_packedPointCloudWorkerGeneration", StringComparison.Ordinal)
                  && !pointcloud.Contains("_dracoEncodeWorkerRunning", StringComparison.Ordinal)
                  && !pointcloud.Contains("_packedPointCloudWorkerRunning", StringComparison.Ordinal),
                "138P-15E: pointcloud worker lifecycle state is centralized in a small helper");
        }

        private static void CameraReadbackAndWorkerSignalsHaveExplicitLifecycleBoundaries()
        {
            var camera = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");
            var cameraPipeline = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraJpegPipeline.cs");
            var readback = ExtractMethod(camera, "private void OnReadbackComplete");
            var completeCount = CountOccurrences(readback, "CompletePendingReadback(identity.Generation)");
            var completeIndex = readback.IndexOf("CompletePendingReadback(identity.Generation)", StringComparison.Ordinal);
            var finallyIndex = readback.IndexOf("finally", StringComparison.Ordinal);
            var submitIndex = readback.IndexOf("SubmitVideoFrame", StringComparison.Ordinal);
            var queueIndex = readback.IndexOf("QueueJpegFrame", StringComparison.Ordinal);
            var publishIndex = readback.IndexOf("PublishJpegFrame", StringComparison.Ordinal);
            Check(readback.Contains("finally", StringComparison.Ordinal)
                  && completeCount == 1
                  && completeIndex >= 0 && finallyIndex >= 0 && submitIndex >= 0 && queueIndex >= 0 && publishIndex >= 0
                  && completeIndex > finallyIndex
                  && completeIndex > submitIndex
                  && completeIndex > queueIndex
                  && completeIndex > publishIndex,
                "138P-15C: camera readback drains pending count after request data is consumed");

            var ensureWorker = ExtractMethod(cameraPipeline, "public bool Start");
            var stopWorker = ExtractMethod(camera, "private void StopJpegWorker");
            var loop = ExtractMethod(cameraPipeline, "private void EncodeJpegWorkerLoop");
            Check(ensureWorker.Contains("new AutoResetEvent(false)", StringComparison.Ordinal)
                  && ensureWorker.Contains("EncodeJpegWorkerLoop(workerGeneration, workerSignal)", StringComparison.Ordinal)
                  && loop.Contains("AutoResetEvent workerSignal", StringComparison.Ordinal)
                  && loop.Contains("workerSignal.WaitOne", StringComparison.Ordinal)
                  && loop.Contains("finally", StringComparison.Ordinal)
                  && loop.Contains("workerSignal.Dispose()", StringComparison.Ordinal)
                  && !loop.Contains("_workerSignal?.WaitOne", StringComparison.Ordinal)
                  && !stopWorker.Contains("_workerSignal.Dispose()", StringComparison.Ordinal),
                "138P-15D: camera JPEG worker signal is owned by one worker generation");
        }

        private static void PackedPointCloudTfRotationConventionIsExplicit()
        {
            var publisher = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");
            var bridge = Read("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityPackedPointCloudBridge.cs");

            Check(publisher.Contains("TF anchor rotation in ROS roll/pitch/yaw degrees", StringComparison.Ordinal)
                  && publisher.Contains("PackedPointCloudTfRotationRos", StringComparison.Ordinal),
                "138P-16A: PointCloud2 native TF anchor rotation has an explicit ROS convention");
            Check(bridge.Contains("PackedPointCloudTfRotationRos", StringComparison.Ordinal),
                "138P-16B: R2FU PointCloud2 bridge publishes the explicit ROS TF anchor rotation");
        }

        private static void VerifyPacketizerLimit(string typeName, byte[] oversizedStream, string label)
        {
            var type = Type.GetType(typeName);
            Check(type != null, label + " type exists");
            var ctor = type.GetConstructor(new[] { typeof(int) });
            Check(ctor != null, label + " constructor exists");
            var packetizer = ctor.Invoke(new object[] { 16 });
            var append = type.GetMethod("Append", new[] { typeof(byte[]) });
            Check(append != null, label + " byte-array Append overload exists");
            append.Invoke(packetizer, new object[] { oversizedStream });

            var dropped = Convert.ToInt64(type.GetProperty("DroppedAccessUnits")?.GetValue(packetizer) ?? 0L);
            var lastError = Convert.ToString(type.GetProperty("LastError")?.GetValue(packetizer) ?? "");
            Check(dropped > 0 && lastError.Contains("exceeds", StringComparison.OrdinalIgnoreCase), label);
        }

        private static SourceHygieneScanResult ScanTrackedSourceHygiene(string[] pollutionRoots, string[] forbiddenTokens)
        {
            var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".cs", ".py", ".md"
            };
            var scanRoots = new[] { "Packages", "Scripts", "Unity2Foxglove/Assets/Samples" };
            var result = new SourceHygieneScanResult();
            foreach (var root in scanRoots)
            {
                if (!Directory.Exists(root))
                    continue;

                foreach (var path in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    if (!extensions.Contains(Path.GetExtension(path)))
                        continue;

                    var normalizedPath = path.Replace('\\', '/');
                    var shouldCheckPollution = IsUnderAnyRoot(normalizedPath, pollutionRoots);
                    var file = ReadSourceHygieneFile(path, shouldCheckPollution);
                    if (file.HasUtf8Bom)
                        result.Utf8BomFiles.Add(normalizedPath);
                    if (shouldCheckPollution
                        && forbiddenTokens.Any(token => file.Text.Contains(token, StringComparison.Ordinal)))
                    {
                        result.Polluted.Add(normalizedPath);
                    }
                }
            }

            return result;
        }

        private static bool IsUnderAnyRoot(string normalizedPath, string[] roots)
        {
            foreach (var root in roots)
            {
                var normalizedRoot = root.Replace('\\', '/').TrimEnd('/');
                if (normalizedPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                    || normalizedPath.StartsWith(normalizedRoot + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private sealed class SourceHygieneScanResult
        {
            public readonly List<string> Polluted = new List<string>();
            public readonly List<string> Utf8BomFiles = new List<string>();
        }

        private readonly struct SourceHygieneFile
        {
            public SourceHygieneFile(bool hasUtf8Bom, string text)
            {
                HasUtf8Bom = hasUtf8Bom;
                Text = text;
            }

            public bool HasUtf8Bom { get; }
            public string Text { get; }
        }

        private static MemoryStream CreateMcapWithPatchedSummaryStart(ulong summaryStart)
        {
            var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase138p-summary-start");
                writer.WriteDataEnd();
                var validSummaryStart = (ulong)writer.Position;
                writer.WriteSchema(1, "phase138p.Schema", "jsonschema", Encoding.UTF8.GetBytes("{}"));
                writer.WriteFooter(validSummaryStart, 0, 0);
                writer.WriteMagic();
            }

            var bytes = stream.ToArray();
            var footerOffset = bytes.Length
                               - McapWriter.MagicLength
                               - McapWriter.RecordHeaderLength
                               - McapWriter.FooterContentLength;
            WriteU64LE(bytes, footerOffset + 1 + sizeof(ulong), summaryStart);
            return new MemoryStream(bytes);
        }

        private static MemoryStream CreateStreamingMcap(
            int attachmentCount,
            int attachmentBytes,
            int metadataCount,
            int metadataValueBytes,
            bool insideChunk = false)
        {
            var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase138p-streaming-limits");
                if (insideChunk)
                {
                    using var inner = new MemoryStream();
                    using (var innerWriter = new McapWriter(inner, leaveOpen: true))
                        WriteStreamingLimitRecords(innerWriter, attachmentCount, attachmentBytes, metadataCount, metadataValueBytes);
                    var records = inner.ToArray();
                    writer.WriteChunk(0, 0, (ulong)records.Length, 0, "", (ulong)records.Length, records);
                }
                else
                {
                    WriteStreamingLimitRecords(writer, attachmentCount, attachmentBytes, metadataCount, metadataValueBytes);
                }

                writer.WriteDataEnd();
                writer.WriteFooter(0, 0, 0);
                writer.WriteMagic();
            }

            stream.Position = 0;
            return stream;
        }

        private static void WriteStreamingLimitRecords(
            McapWriter writer,
            int attachmentCount,
            int attachmentBytes,
            int metadataCount,
            int metadataValueBytes)
        {
            for (var i = 0; i < attachmentCount; i++)
                writer.WriteAttachment((ulong)i, (ulong)i, "attachment" + i + ".bin", "application/octet-stream", new byte[attachmentBytes]);
            for (var i = 0; i < metadataCount; i++)
                writer.WriteMetadata("metadata" + i, new Dictionary<string, string> { ["value"] = new string('x', metadataValueBytes) });
        }

        private static void SetField(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field == null)
                throw new MissingFieldException(target.GetType().FullName, name);
            field.SetValue(target, value);
        }

        private static byte[] Nal(byte type, params byte[] payload)
            => Concat(new byte[] { 0, 0, 0, 1, type }, payload);

        private static byte[] H265Nal(byte type, params byte[] payload)
            => Concat(new byte[] { 0, 0, 0, 1, (byte)(type << 1), 1 }, payload);

        private static byte[] Repeat(byte value, int count)
        {
            var bytes = new byte[count];
            for (var i = 0; i < bytes.Length; i++)
                bytes[i] = value;
            return bytes;
        }

        private static byte[] Concat(params byte[][] parts)
        {
            var length = parts.Sum(part => part?.Length ?? 0);
            var result = new byte[length];
            var offset = 0;
            foreach (var part in parts)
            {
                if (part == null)
                    continue;
                Buffer.BlockCopy(part, 0, result, offset, part.Length);
                offset += part.Length;
            }

            return result;
        }

        private static int CountOccurrences(string source, string value)
        {
            var count = 0;
            var index = 0;
            while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
        }

        private static float ReadSingle(byte[] data, int offset)
            => BitConverter.ToSingle(data, offset);

        private static uint ReadUInt32(byte[] data, int offset)
            => BitConverter.ToUInt32(data, offset);

        private static uint ExpectedNanoseconds(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0f)
                return 0u;

            var nanoseconds = Math.Round((double)seconds * 1_000_000_000d, MidpointRounding.AwayFromZero);
            return nanoseconds >= uint.MaxValue ? uint.MaxValue : (uint)nanoseconds;
        }

        private static void WriteU64LE(byte[] data, int offset, ulong value)
        {
            for (var i = 0; i < 8; i++)
                data[offset + i] = (byte)(value >> (8 * i));
        }

        private static bool Throws<T>(Action action) where T : Exception
        {
            try
            {
                action();
                return false;
            }
            catch (TargetInvocationException ex) when (ex.InnerException is T)
            {
                return true;
            }
            catch (T)
            {
                return true;
            }
        }

        private static bool ThrowsWithMessage<T>(Action action, string expectedMessagePart) where T : Exception
        {
            try
            {
                action();
                return false;
            }
            catch (T ex)
            {
                return ex.Message.IndexOf(expectedMessagePart, StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        private static bool NearlyZero(System.Numerics.Vector3 value)
            => value.Length() < 1e-4f;

        private static bool NearlyEqual(System.Numerics.Vector3 left, System.Numerics.Vector3 right)
            => System.Numerics.Vector3.Distance(left, right) < 1e-4f;

        private static bool NearlyIdentity(System.Numerics.Quaternion value)
            => Math.Abs(Math.Abs(System.Numerics.Quaternion.Dot(value, System.Numerics.Quaternion.Identity)) - 1f) < 1e-4f;

        private static string ExtractMethod(string source, string signatureStart)
        {
            var start = source.IndexOf(signatureStart, StringComparison.Ordinal);
            if (start < 0)
                return "";

            var brace = source.IndexOf('{', start);
            if (brace < 0)
                return "";

            var depth = 0;
            for (var i = brace; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(start, i - start + 1);
                }
            }

            return string.Empty;
        }

        private static string Read(string path)
            => File.ReadAllText(path);

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
