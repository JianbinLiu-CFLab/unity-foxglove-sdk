// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Explicit helper for schemaless FoxRun debug overlay topics.

using System.Collections.Generic;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Publishes explicit <c>/debug/...</c> JSON diagnostics through a FoxgloveManager.
    /// </summary>
    public static class FoxgloveDebugOverlay
    {
        public static bool Publish(
            FoxgloveManager manager,
            string topic,
            string source,
            IReadOnlyDictionary<string, object> values,
            string label = null,
            ulong? logTimeNs = null)
        {
            if (manager == null)
                return false;

            if (!FoxgloveDebugOverlayEnvelope.TryCreate(topic, source, values, label, out var envelope))
                return false;

            if (manager.SuppressLivePublishersForReplay || !manager.IsRunning)
                return false;

            var timestamp = logTimeNs ?? manager.NowNs;
            manager.PublishJson(topic, "", envelope, timestamp);
            return true;
        }

        public static bool PublishValue(
            FoxgloveManager manager,
            string topic,
            string source,
            string key,
            object value,
            string label = null,
            ulong? logTimeNs = null)
        {
            if (manager == null)
                return false;

            if (!FoxgloveDebugOverlayEnvelope.TryCreateValue(topic, source, key, value, label, out var envelope))
                return false;

            if (manager.SuppressLivePublishersForReplay || !manager.IsRunning)
                return false;

            var timestamp = logTimeNs ?? manager.NowNs;
            manager.PublishJson(topic, "", envelope, timestamp);
            return true;
        }
    }
}
