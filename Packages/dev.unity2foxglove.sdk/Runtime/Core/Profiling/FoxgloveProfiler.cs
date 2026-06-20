// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core
// Purpose: Global profiler hook for optional runtime instrumentation.

using System;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Global profiler hook used by Unity-neutral runtime code.
    /// </summary>
    public static class FoxgloveProfiler
    {
        private static readonly object Gate = new object();
        private static volatile IFoxgloveProfiler _global = NullProfiler.Instance;
        private static object _owner;

        public static IFoxgloveProfiler Global
        {
            get => _global;
            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException(nameof(value));
                }

                lock (Gate)
                {
                    _owner = null;
                    _global = value;
                }
            }
        }

        public static void SetGlobal(object owner, IFoxgloveProfiler profiler)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            if (profiler == null)
            {
                throw new ArgumentNullException(nameof(profiler));
            }

            lock (Gate)
            {
                _owner = owner;
                _global = profiler;
            }
        }

        public static void ResetGlobal()
        {
            lock (Gate)
            {
                _owner = null;
                _global = NullProfiler.Instance;
            }
        }

        public static void ResetGlobal(object owner)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            lock (Gate)
            {
                if (!ReferenceEquals(_owner, owner))
                {
                    return;
                }

                _owner = null;
                _global = NullProfiler.Instance;
            }
        }
    }
}
