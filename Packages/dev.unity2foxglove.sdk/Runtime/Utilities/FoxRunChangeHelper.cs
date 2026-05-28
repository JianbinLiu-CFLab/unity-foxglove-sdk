// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Shared change-detection helpers used by generated FoxRun code.
    /// Replaces the per-class __foxrun_float_changed / __foxrun_double_changed
    /// inline methods previously emitted by FoxgloveSourceEmitter.
    /// NaN semantics: two NaN values compare as equal (no change).
    /// </summary>
    public static class FoxRunChangeHelper
    {
        public static bool FloatChanged(float current, float last, float epsilon)
        {
            if (float.IsNaN(current) || float.IsNaN(last))
                return !(float.IsNaN(current) && float.IsNaN(last));
            return Math.Abs(current - last) > epsilon;
        }

        public static bool DoubleChanged(double current, double last, double epsilon)
        {
            if (double.IsNaN(current) || double.IsNaN(last))
                return !(double.IsNaN(current) && double.IsNaN(last));
            return Math.Abs(current - last) > epsilon;
        }
    }
}
