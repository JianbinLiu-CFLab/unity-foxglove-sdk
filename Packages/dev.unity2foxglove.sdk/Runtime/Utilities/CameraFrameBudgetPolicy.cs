// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Utilities
// Purpose: Unity-free resource budget policy for camera capture scheduling.

namespace Unity.FoxgloveSDK.Util
{
    /// <summary>
    /// Reasons that explain why a camera frame capture was skipped.
    /// </summary>
    public enum CameraFrameBudgetSkipReason
    {
        /// <summary>
        /// No skip; capture is allowed.
        /// </summary>
        None = 0,
        /// <summary>
        /// Too many readbacks are already in flight.
        /// </summary>
        ReadbackQueueFull,
        /// <summary>
        /// JPEG encode queue is already full.
        /// </summary>
        EncodeQueueFull,
        /// <summary>
        /// Completed JPEG frame queue is already full.
        /// </summary>
        CompletedQueueFull,
        /// <summary>
        /// Pixel budget for this frame exceeded <see cref="CameraFrameBudgetInput.MaxPixelsPerFrame" />.
        /// </summary>
        PixelBudgetExceeded
    }

    /// <summary>
    /// Input snapshot for budget evaluation.
    /// </summary>
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

    /// <summary>
    /// Evaluation outcome for the current capture budget snapshot.
    /// </summary>
    public struct CameraFrameBudgetResult
    {
        public bool AllowCapture;
        public CameraFrameBudgetSkipReason SkipReason;
    }

    /// <summary>
    /// Pure, deterministic policy for deciding whether a camera capture should proceed
    /// in the current frame and why it should be skipped when throttled.
    /// </summary>
    public static class CameraFrameBudgetPolicy
    {
        /// <summary>
        /// Evaluates runtime state (queues and optional pixel budget) and returns a binary
        /// scheduling decision with a skip reason when disabled.
        /// </summary>
        /// <param name="input">Budget counters and limits for this decision.</param>
        /// <returns>Whether capture is allowed and skip classification when it is not.</returns>
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

        /// <summary>
        /// Creates a denied capture decision with the skip reason preserved for diagnostics.
        /// </summary>
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
