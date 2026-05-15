// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Camera output mode and profile metadata for camera publishers.

using Foxglove.Schemas.Video;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// User-facing camera output modes supported by <see cref="FoxgloveCameraPublisher"/>.
    /// </summary>
    public enum CameraOutputMode
    {
        Jpeg = 0,
        H264Ffmpeg = 1,
        H265Ffmpeg = 2,
        H264MediaFoundationExperimental = 3
    }

    internal enum CameraVideoCodec
    {
        None = 0,
        H264 = 1,
        H265 = 2
    }

    internal enum CameraVideoEncoderBackend
    {
        None = 0,
        FfmpegH264 = 1,
        FfmpegH265 = 2,
        MediaFoundationH264 = 3
    }

    /// <summary>
    /// Resolved camera output settings for schemas, topics, encoding support,
    /// and future codec-specific video behavior.
    /// </summary>
    public readonly struct CameraVideoOutputProfile
    {
        internal CameraVideoOutputProfile(
            CameraOutputMode mode,
            CameraVideoCodec codec,
            CameraVideoEncoderBackend encoderBackend,
            string displayName,
            string defaultTopic,
            string schemaName,
            string videoFormat,
            bool supportsJson,
            bool supportsProtobuf)
        {
            Mode = mode;
            Codec = codec;
            EncoderBackend = encoderBackend;
            DisplayName = displayName ?? "";
            DefaultTopic = defaultTopic ?? "";
            SchemaName = schemaName ?? "";
            VideoFormat = videoFormat ?? "";
            SupportsJson = supportsJson;
            SupportsProtobuf = supportsProtobuf;
        }

        public CameraOutputMode Mode { get; }
        internal CameraVideoCodec Codec { get; }
        internal CameraVideoEncoderBackend EncoderBackend { get; }
        public string DisplayName { get; }
        public string DefaultTopic { get; }
        public string SchemaName { get; }
        public string VideoFormat { get; }
        public bool SupportsJson { get; }
        public bool SupportsProtobuf { get; }
        public bool IsVideo => Codec != CameraVideoCodec.None;

        internal ICameraVideoEncoderSidecar CreateSidecar()
        {
            switch (EncoderBackend)
            {
                case CameraVideoEncoderBackend.FfmpegH264:
                    return new FfmpegH264EncoderSidecar();
                case CameraVideoEncoderBackend.FfmpegH265:
                    return new FfmpegH265EncoderSidecar();
                case CameraVideoEncoderBackend.MediaFoundationH264:
                    return new MediaFoundationH264EncoderSidecar();
                case CameraVideoEncoderBackend.None:
                default:
                    return null;
            }
        }

        public static CameraVideoOutputProfile ForMode(CameraOutputMode mode)
        {
            switch (mode)
            {
                case CameraOutputMode.H264Ffmpeg:
                    return new CameraVideoOutputProfile(
                        mode,
                        CameraVideoCodec.H264,
                        CameraVideoEncoderBackend.FfmpegH264,
                        "H.264 (FFmpeg)",
                        CameraOutputModeDefaults.H264Topic,
                        CameraOutputModeDefaults.H264Schema,
                        Foxglove.Schemas.CameraCompressedVideoBuilder.H264Format,
                        supportsJson: false,
                        supportsProtobuf: true);

                case CameraOutputMode.H265Ffmpeg:
                    return new CameraVideoOutputProfile(
                        mode,
                        CameraVideoCodec.H265,
                        CameraVideoEncoderBackend.FfmpegH265,
                        "H.265 / HEVC (FFmpeg)",
                        CameraOutputModeDefaults.H265Topic,
                        CameraOutputModeDefaults.H265Schema,
                        Foxglove.Schemas.CameraCompressedVideoBuilder.H265Format,
                        supportsJson: false,
                        supportsProtobuf: true);

                case CameraOutputMode.H264MediaFoundationExperimental:
                    return new CameraVideoOutputProfile(
                        mode,
                        CameraVideoCodec.H264,
                        CameraVideoEncoderBackend.MediaFoundationH264,
                        "H.264 (Windows Native, Experimental)",
                        CameraOutputModeDefaults.H264Topic,
                        CameraOutputModeDefaults.H264Schema,
                        Foxglove.Schemas.CameraCompressedVideoBuilder.H264Format,
                        supportsJson: false,
                        supportsProtobuf: true);

                case CameraOutputMode.Jpeg:
                default:
                    return new CameraVideoOutputProfile(
                        CameraOutputMode.Jpeg,
                        CameraVideoCodec.None,
                        CameraVideoEncoderBackend.None,
                        "JPEG",
                        CameraOutputModeDefaults.JpegTopic,
                        CameraOutputModeDefaults.JpegSchema,
                        "jpeg",
                        supportsJson: true,
                        supportsProtobuf: true);
            }
        }
    }

    /// <summary>
    /// Camera output mode constants shared by runtime and Inspector code.
    /// </summary>
    public static class CameraOutputModeDefaults
    {
        public const string JpegTopic = "/unity/camera";
        public const string H264Topic = "/unity/camera";
        public const string H265Topic = "/unity/camera";
        public const string JpegSchema = "foxglove.CompressedImage";
        public const string H264Schema = "foxglove.CompressedVideo";
        public const string H265Schema = "foxglove.CompressedVideo";
    }
}
