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
        PixelBudgetExceeded,
        /// <summary>
        /// A previous slow camera pipeline stage put capture scheduling into a short cooldown.
        /// </summary>
        PipelineCooldown
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
        public bool RequireIdlePipeline;
        public bool PipelineCooldownActive;
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
            if (input.PipelineCooldownActive)
                return Skip(CameraFrameBudgetSkipReason.PipelineCooldown);

            var maxReadbacks = input.MaxPendingReadbacks > 0 ? input.MaxPendingReadbacks : 1;
            if (input.PendingReadbacks >= maxReadbacks
                || (input.RequireIdlePipeline && input.PendingReadbacks > 0))
                return Skip(CameraFrameBudgetSkipReason.ReadbackQueueFull);

            var maxEncodeQueue = input.MaxEncodeQueueDepth > 0 ? input.MaxEncodeQueueDepth : 1;
            if (input.EncodeQueueDepth >= maxEncodeQueue
                || (input.RequireIdlePipeline && input.EncodeQueueDepth > 0))
                return Skip(CameraFrameBudgetSkipReason.EncodeQueueFull);

            var maxCompletedQueue = input.MaxCompletedQueueDepth > 0 ? input.MaxCompletedQueueDepth : 1;
            if (input.CompletedQueueDepth >= maxCompletedQueue
                || (input.RequireIdlePipeline && input.CompletedQueueDepth > 0))
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

    /// <summary>
    /// Pure, deterministic gate that requires a few healthy main-loop frames before
    /// scheduling another camera capture after a slow frame.
    /// </summary>
    public static class CameraFrameHealthGatePolicy
    {
        /// <summary>
        /// Decides whether the camera may capture on the current frame.
        /// </summary>
        /// <param name="stableFramesRemaining">Mutable count of healthy frames still required before capture resumes.</param>
        /// <param name="frameDeltaMs">Current frame delta in milliseconds.</param>
        /// <param name="maxHealthyFrameDeltaMs">Maximum frame delta considered healthy; 0 disables the gate.</param>
        /// <param name="stableFramesRequired">Healthy frames required after a slow frame; 0 disables the gate.</param>
        /// <returns>Whether capture can proceed.</returns>
        public static bool ShouldCapture(
            ref int stableFramesRemaining,
            double frameDeltaMs,
            double maxHealthyFrameDeltaMs,
            int stableFramesRequired)
        {
            if (maxHealthyFrameDeltaMs <= 0d || stableFramesRequired <= 0)
            {
                stableFramesRemaining = 0;
                return true;
            }

            if (frameDeltaMs > maxHealthyFrameDeltaMs)
            {
                stableFramesRemaining = stableFramesRequired;
                return false;
            }

            if (stableFramesRemaining <= 0)
                return true;

            stableFramesRemaining--;
            return false;
        }
    }
}
