// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Editor
// Purpose: Preserve selector exceptions while keeping Inspector text bounded.

using System;

namespace Unity2Foxglove.Ros2ForUnity.Editor
{
    internal static class Ros2ForUnityEditorDiagnostics
    {
        internal const string SelectorFailureMessage =
            "ROS2 For Unity runtime selection failed. Check the Unity package manifest and installed runtime packages.";

        internal static Action<Exception> ExceptionSink { get; set; }

        internal static void ReportSelectorFailure(
            Exception exception,
            Action<Exception> fallback)
        {
            if (exception == null)
                return;

            var sink = ExceptionSink;
            if (sink != null)
            {
                sink(exception);
                return;
            }

            fallback?.Invoke(exception);
        }
    }
}
