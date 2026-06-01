// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Utilities
// Purpose: Unity-free monotonic publish-order guard for async JPEG results.

namespace Unity.FoxgloveSDK.Util
{
    public static class CameraJpegPublishOrderPolicy
    {
        public static bool ShouldPublish(ulong captureUnixNs, ulong lastPublishedCaptureUnixNs)
            => captureUnixNs > lastPublishedCaptureUnixNs;
    }
}
