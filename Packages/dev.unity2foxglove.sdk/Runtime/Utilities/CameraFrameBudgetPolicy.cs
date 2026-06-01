// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Utilities
// Purpose: Unity-free resource budget policy for camera capture scheduling.

namespace Unity.FoxgloveSDK.Util
{
    public enum CameraFrameBudgetSkipReason
    {
        None = 0,
        ReadbackQueueFull,
        EncodeQueueFull,
        CompletedQueueFull,
        PixelBudgetExceeded
    }

    public struct CameraFrameBudgetInput
    {
        public int PendingReadbacks;
        public int MaxPendingReadbacks;
        public int EncodeQueueDepth;
        public int MaxEncodeQueueDepth;
        public int CompletedQueueDepth;
        public int MaxCompletedQueueDepth;
        public int Width;
        public int Height;
        public int MaxPixelsPerFrame;
    }

    public struct CameraFrameBudgetResult
    {
        public bool AllowCapture;
        public CameraFrameBudgetSkipReason SkipReason;
    }

    public static class CameraFrameBudgetPolicy
    {
        public static CameraFrameBudgetResult Evaluate(CameraFrameBudgetInput input)
        {
            var maxReadbacks = input.MaxPendingReadbacks > 0 ? input.MaxPendingReadbacks : 1;
            if (input.PendingReadbacks >= maxReadbacks)
                return Skip(CameraFrameBudgetSkipReason.ReadbackQueueFull);

            var maxEncodeQueue = input.MaxEncodeQueueDepth > 0 ? input.MaxEncodeQueueDepth : 1;
            if (input.EncodeQueueDepth >= maxEncodeQueue)
                return Skip(CameraFrameBudgetSkipReason.EncodeQueueFull);

            var maxCompletedQueue = input.MaxCompletedQueueDepth > 0 ? input.MaxCompletedQueueDepth : 1;
            if (input.CompletedQueueDepth >= maxCompletedQueue)
                return Skip(CameraFrameBudgetSkipReason.CompletedQueueFull);

            if (input.MaxPixelsPerFrame > 0)
            {
                var width = input.Width > 0 ? input.Width : 1;
                var height = input.Height > 0 ? input.Height : 1;
                if ((long)width * height > input.MaxPixelsPerFrame)
                    return Skip(CameraFrameBudgetSkipReason.PixelBudgetExceeded);
            }

            return new CameraFrameBudgetResult
            {
                AllowCapture = true,
                SkipReason = CameraFrameBudgetSkipReason.None
            };
        }

        private static CameraFrameBudgetResult Skip(CameraFrameBudgetSkipReason reason)
        {
            return new CameraFrameBudgetResult
            {
                AllowCapture = false,
                SkipReason = reason
            };
        }
    }
}
