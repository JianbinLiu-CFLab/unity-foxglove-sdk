// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Publishers
// Purpose: Bundled native Draco point-cloud plugin validation.

using System;
using Foxglove.Schemas.PointCloud;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Editor
{
    public enum DracoPointCloudNativeStatus
    {
        NotChecked,
        Available,
        Missing,
        Invalid
    }

    public readonly struct DracoPointCloudNativeCheckResult
    {
        public DracoPointCloudNativeCheckResult(
            DracoPointCloudNativeStatus status,
            string version,
            string errorMessage,
            int payloadBytes)
        {
            Status = status;
            Version = version ?? "";
            ErrorMessage = errorMessage ?? "";
            PayloadBytes = payloadBytes;
        }

        public DracoPointCloudNativeStatus Status { get; }
        public string Version { get; }
        public string ErrorMessage { get; }
        public int PayloadBytes { get; }
    }

    public static class DracoPointCloudNativeCheck
    {
        public static DracoPointCloudNativeCheckResult Check()
        {
            if (!DracoPointCloudNativeEncoder.TryGetAvailability(out var versionOrError))
            {
                return new DracoPointCloudNativeCheckResult(
                    DracoPointCloudNativeStatus.Missing,
                    "",
                    versionOrError,
                    0);
            }

            try
            {
                var frame = CreateTinyXyzFrame();
                if (!DracoPointCloudNativeEncoder.TryEncode(frame, out var payload, out var encodeError)
                    || payload == null
                    || payload.Length == 0)
                {
                    return new DracoPointCloudNativeCheckResult(
                        DracoPointCloudNativeStatus.Invalid,
                        versionOrError,
                        string.IsNullOrWhiteSpace(encodeError) ? "Native Draco plugin did not emit a valid payload." : encodeError,
                        0);
                }

                return new DracoPointCloudNativeCheckResult(
                    DracoPointCloudNativeStatus.Available,
                    versionOrError,
                    "",
                    payload.Length);
            }
            catch (Exception ex)
            {
                return new DracoPointCloudNativeCheckResult(
                    DracoPointCloudNativeStatus.Invalid,
                    versionOrError,
                    ex.Message,
                    0);
            }
        }

        private static PointCloudFrame CreateTinyXyzFrame()
        {
            var frame = new PointCloudFrame
            {
                UnixNs = 1,
                FrameId = "draco_check"
            };
            frame.Points.Add(new PointCloudPoint(0f, 0f, 0f));
            frame.Points.Add(new PointCloudPoint(1f, 0f, 0f));
            frame.Points.Add(new PointCloudPoint(0f, 1f, 0f));
            return frame;
        }
    }
}
