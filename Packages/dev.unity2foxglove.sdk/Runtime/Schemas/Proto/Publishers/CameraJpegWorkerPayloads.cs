// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers

using System;
using Foxglove.Schemas;
using Google.Protobuf;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.Camera;
using Unity.FoxgloveSDK.Util;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Worker-side JPEG encode path operating only on owned buffers and pure
    /// managed serializers; Unity APIs must stay out of this type.
    /// </summary>
    internal static class CameraJpegWorkerEncoder
    {
        public static JpegEncodeResult EncodeJpegRequest(JpegEncodeRequest request)
        {
            var encodeStart = Stopwatch.GetTimestamp();
            byte[] jpeg;
            try
            {
                // AsyncGPUReadback delivers rows in Unity texture order; JPEG viewers expect top-first rows.
                jpeg = ManagedJpegEncoder.EncodeRgb24(
                    request.Rgb24,
                    request.Width,
                    request.Height,
                    request.Quality,
                    flipVertical: true);
            }
            catch (Exception ex)
            {
                return JpegEncodeResult.Failure(request, ex.Message, ElapsedMs(encodeStart));
            }

            var encodeMs = ElapsedMs(encodeStart);
            if (jpeg == null || jpeg.Length == 0)
                return JpegEncodeResult.Failure(request, "JPEG encoder returned no bytes.", encodeMs);

            if (request.MaxEncodedBytes > 0 && jpeg.Length > request.MaxEncodedBytes)
                return JpegEncodeResult.EncodedBudgetDrop(request, jpeg.Length, encodeMs);

            var serializeStart = Stopwatch.GetTimestamp();
            byte[] webSocketPayload = null;
            CompressedImageMessage jsonMessage = null;
            Foxglove.CompressedImage protobufMessage = null;

            try
            {
                if (request.PublishWebSocket && request.WebSocketEncoding == PublisherEffectiveEncoding.Protobuf)
                {
                    protobufMessage = CameraCompressedImageBuilder.Create(
                        request.CaptureUnixNs,
                        request.FrameId,
                        jpeg,
                        "jpeg");
                    webSocketPayload = protobufMessage.ToByteArray();
                }
                else if (request.PublishWebSocket)
                {
                    jsonMessage = new CompressedImageMessage
                    {
                        Timestamp = FoxgloveTimeUtil.ToFoxgloveTime(request.CaptureUnixNs),
                        FrameId = request.FrameId,
                        Data = Convert.ToBase64String(jpeg),
                        Format = "jpeg"
                    };
                }

                if (request.PublishProvider && !request.UseStandardSensorCompressedImage)
                {
                    protobufMessage ??= CameraCompressedImageBuilder.Create(
                        request.CaptureUnixNs,
                        request.FrameId,
                        jpeg,
                        "jpeg");
                }

                SensorCompressedImageFrame sensorFrame = null;
                if (request.PublishNativeFrame
                    || (request.PublishProvider && request.UseStandardSensorCompressedImage))
                    sensorFrame = new SensorCompressedImageFrame(request.CaptureUnixNs, request.FrameId, jpeg, "jpeg");

                return JpegEncodeResult.Completed(
                    request,
                    webSocketPayload,
                    jsonMessage,
                    protobufMessage,
                    sensorFrame,
                    jpeg.Length,
                    encodeMs,
                    ElapsedMs(serializeStart));
            }
            catch (Exception ex)
            {
                return JpegEncodeResult.Failure(
                    request,
                    "Unable to serialize JPEG camera payload off thread: " + ex.Message,
                    encodeMs,
                    ElapsedMs(serializeStart),
                    jpeg.Length);
            }
        }

        private static double ElapsedMs(long startTicks)
            => (Stopwatch.GetTimestamp() - startTicks) * 1000d / Stopwatch.Frequency;
    }

    internal sealed class JpegEncodeRequest
    {
        public JpegEncodeRequest(
            byte[] rgb24,
            int width,
            int height,
            int quality,
            ulong captureUnixNs,
            string frameId,
            bool publishWebSocket,
            bool publishProvider,
            bool publishNativeFrame,
            bool useStandardSensorCompressedImage,
            PublisherEffectiveEncoding webSocketEncoding,
            int maxEncodedBytes,
            int generation,
            int jpegWorkerGeneration)
        {
            Rgb24 = rgb24;
            Width = width;
            Height = height;
            Quality = quality;
            CaptureUnixNs = captureUnixNs;
            FrameId = frameId ?? "";
            PublishWebSocket = publishWebSocket;
            PublishProvider = publishProvider;
            PublishNativeFrame = publishNativeFrame;
            UseStandardSensorCompressedImage = useStandardSensorCompressedImage;
            WebSocketEncoding = webSocketEncoding;
            MaxEncodedBytes = maxEncodedBytes;
            Generation = generation;
            JpegWorkerGeneration = jpegWorkerGeneration;
        }

        public byte[] Rgb24 { get; }
        public int Width { get; }
        public int Height { get; }
        public int Quality { get; }
        public ulong CaptureUnixNs { get; }
        public string FrameId { get; }
        public bool PublishWebSocket { get; }
        public bool PublishProvider { get; }
        public bool PublishNativeFrame { get; }
        public bool UseStandardSensorCompressedImage { get; }
        public PublisherEffectiveEncoding WebSocketEncoding { get; }
        public int MaxEncodedBytes { get; }
        public int Generation { get; }
        public int JpegWorkerGeneration { get; }
    }

    internal sealed class JpegEncodeResult
    {
        private JpegEncodeResult(
            JpegEncodeRequest request,
            bool success,
            bool droppedByEncodedBudget,
            byte[] webSocketPayload,
            CompressedImageMessage jsonMessage,
            Foxglove.CompressedImage protobufMessage,
            SensorCompressedImageFrame sensorFrame,
            int jpegBytes,
            string error,
            double encodeMs,
            double serializeMs)
        {
            Request = request;
            Success = success;
            DroppedByEncodedBudget = droppedByEncodedBudget;
            WebSocketPayload = webSocketPayload;
            JsonMessage = jsonMessage;
            ProtobufMessage = protobufMessage;
            SensorFrame = sensorFrame;
            JpegBytes = jpegBytes;
            Error = error;
            EncodeMs = encodeMs;
            SerializeMs = serializeMs;
        }

        public JpegEncodeRequest Request { get; }
        public bool Success { get; }
        public bool DroppedByEncodedBudget { get; }
        public byte[] WebSocketPayload { get; }
        public CompressedImageMessage JsonMessage { get; }
        public Foxglove.CompressedImage ProtobufMessage { get; }
        public SensorCompressedImageFrame SensorFrame { get; }
        public int JpegBytes { get; }
        public string Error { get; }
        public double EncodeMs { get; }
        public double SerializeMs { get; }

        public static JpegEncodeResult Completed(
            JpegEncodeRequest request,
            byte[] webSocketPayload,
            CompressedImageMessage jsonMessage,
            Foxglove.CompressedImage protobufMessage,
            SensorCompressedImageFrame sensorFrame,
            int jpegBytes,
            double encodeMs,
            double serializeMs)
            => new JpegEncodeResult(
                request,
                success: true,
                droppedByEncodedBudget: false,
                webSocketPayload,
                jsonMessage,
                protobufMessage,
                sensorFrame,
                jpegBytes,
                error: null,
                encodeMs,
                serializeMs);

        public static JpegEncodeResult Failure(
            JpegEncodeRequest request,
            string error,
            double encodeMs,
            double serializeMs = 0,
            int jpegBytes = 0)
            => new JpegEncodeResult(
                request,
                success: false,
                droppedByEncodedBudget: false,
                webSocketPayload: null,
                jsonMessage: null,
                protobufMessage: null,
                sensorFrame: null,
                jpegBytes,
                error,
                encodeMs,
                serializeMs);

        /// <summary>
        /// Records an encoded payload that was produced successfully but intentionally
        /// dropped because it exceeded the configured byte budget.
        /// </summary>
        public static JpegEncodeResult EncodedBudgetDrop(JpegEncodeRequest request, int jpegBytes, double encodeMs)
            => new JpegEncodeResult(
                request,
                success: false,
                droppedByEncodedBudget: true,
                webSocketPayload: null,
                jsonMessage: null,
                protobufMessage: null,
                sensorFrame: null,
                jpegBytes,
                error: null,
                encodeMs,
                serializeMs: 0);
    }
}
