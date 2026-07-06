// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Utilities
// Purpose: Unity-free health policy for camera capture admission.

namespace Unity.FoxgloveSDK.Util
{
    /// <summary>
    /// Operator-selectable camera health admission mode.
    /// </summary>
    internal enum CameraPipelineHealthMode
    {
        Off = 0,
        Conservative = 1,
        Balanced = 2,
        Aggressive = 3
    }

    /// <summary>
    /// Reasons that explain why the shared camera health policy skipped capture.
    /// </summary>
    internal enum CameraPipelineHealthSkipReason
    {
        None = 0,
        CadenceBudget,
        ReadbackQueueFull,
        EncodeQueueFull,
        CompletedQueueFull,
        VideoOutputQueueFull,
        PixelBudgetExceeded,
        RenderPressureCooldown
    }

    /// <summary>
    /// Input snapshot for camera capture admission.
    /// </summary>
    internal struct CameraPipelineHealthInput
    {
        public CameraPipelineHealthMode Mode;
        public bool CadenceAllowed;
        public int PendingReadbacks;
        public int MaxPendingReadbacks;
        public int EncodeQueueDepth;
        public int MaxEncodeQueueDepth;
        public int CompletedQueueDepth;
        public int MaxCompletedQueueDepth;
        public int VideoOutputQueueDepth;
        public int MaxVideoOutputQueueDepth;
        public int Width;
        public int Height;
        public int MaxPixelsPerFrame;
        public bool RenderPressureCooldownActive;
    }

    /// <summary>
    /// Evaluation outcome for camera health admission.
    /// </summary>
    internal struct CameraPipelineHealthResult
    {
        public bool AllowCapture;
        public CameraPipelineHealthSkipReason SkipReason;
    }

    /// <summary>
    /// Pure policy that decides whether a camera capture should enter the render/readback path.
    /// </summary>
    internal static class CameraPipelineHealthPolicy
    {
        public static CameraPipelineHealthResult Evaluate(CameraPipelineHealthInput input)
        {
            if (!input.CadenceAllowed)
                return Skip(CameraPipelineHealthSkipReason.CadenceBudget);

            var maxReadbacks = PositiveOrDefault(input.MaxPendingReadbacks);
            if (input.PendingReadbacks >= maxReadbacks)
                return Skip(CameraPipelineHealthSkipReason.ReadbackQueueFull);

            if (input.MaxPixelsPerFrame > 0)
            {
                var width = PositiveOrDefault(input.Width);
                var height = PositiveOrDefault(input.Height);
                if ((long)width * height > input.MaxPixelsPerFrame)
                    return Skip(CameraPipelineHealthSkipReason.PixelBudgetExceeded);
            }

            if (input.Mode == CameraPipelineHealthMode.Off)
                return Allow();

            if (input.RenderPressureCooldownActive)
                return Skip(CameraPipelineHealthSkipReason.RenderPressureCooldown);

            if (QueueHasPressure(input.EncodeQueueDepth, input.MaxEncodeQueueDepth, input.Mode))
                return Skip(CameraPipelineHealthSkipReason.EncodeQueueFull);

            if (QueueHasPressure(input.CompletedQueueDepth, input.MaxCompletedQueueDepth, input.Mode))
                return Skip(CameraPipelineHealthSkipReason.CompletedQueueFull);

            if (QueueHasPressure(input.VideoOutputQueueDepth, input.MaxVideoOutputQueueDepth, input.Mode))
                return Skip(CameraPipelineHealthSkipReason.VideoOutputQueueFull);

            return Allow();
        }

        private static bool QueueHasPressure(int depth, int maxDepth, CameraPipelineHealthMode mode)
        {
            var max = PositiveOrDefault(maxDepth);
            if (depth >= max)
                return true;

            if (mode == CameraPipelineHealthMode.Aggressive)
                return false;

            if (mode == CameraPipelineHealthMode.Balanced)
                return depth > 0;

            return depth > 0;
        }

        private static int PositiveOrDefault(int value)
            => value > 0 ? value : 1;

        private static CameraPipelineHealthResult Allow()
            => new CameraPipelineHealthResult
            {
                AllowCapture = true,
                SkipReason = CameraPipelineHealthSkipReason.None
            };

        private static CameraPipelineHealthResult Skip(CameraPipelineHealthSkipReason reason)
            => new CameraPipelineHealthResult
            {
                AllowCapture = false,
                SkipReason = reason
            };
    }
}
