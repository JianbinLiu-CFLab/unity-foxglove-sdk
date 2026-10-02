// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>Resolves and filters publishers for a Manager component-session capture.</summary>
    internal static class ComponentPublisherSessionCaptureState
    {
        internal static IEnumerable<TPublisher> SelectOwned<TPublisher>(
            IEnumerable<TPublisher> candidates,
            Func<TPublisher, bool> resolveForSession,
            Func<TPublisher, bool> hasValidTopic)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            if (resolveForSession == null) throw new ArgumentNullException(nameof(resolveForSession));
            if (hasValidTopic == null) throw new ArgumentNullException(nameof(hasValidTopic));

            foreach (var candidate in candidates)
            {
                if (resolveForSession(candidate) && hasValidTopic(candidate))
                    yield return candidate;
            }
        }
    }
}
