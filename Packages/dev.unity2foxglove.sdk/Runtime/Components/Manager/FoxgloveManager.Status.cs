// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Provides FoxgloveManager diagnostics status helpers for the
// official Foxglove WebSocket status/removeStatus messages.

using Unity.FoxgloveSDK.Protocol;
using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// FoxgloveManager partial that exposes explicit Problems-panel diagnostics helpers.
    /// </summary>
    public partial class FoxgloveManager
    {
        /// <summary>
        /// Warning emitted when status publishing is requested before the server starts.
        /// </summary>
        private const string PublishStatusNotRunningWarning =
            "[Foxglove] PublishStatus called but server is not running.";

        /// <summary>
        /// Warning emitted when status removal is requested before the server starts.
        /// </summary>
        private const string RemoveStatusNotRunningWarning =
            "[Foxglove] RemoveStatus called but server is not running.";

        /// <summary>
        /// Publishes an explicit diagnostics status message to the Foxglove Problems panel.
        /// </summary>
        /// <param name="level">Status severity encoded with official Foxglove numeric values.</param>
        /// <param name="message">Human-readable diagnostic message.</param>
        /// <param name="id">Optional stable status identifier for later removal.</param>
        public void PublishStatus(FoxgloveStatusLevel level, string message, string id = null)
        {
            if (!IsRunning)
            {
                WarnStatusNotRunning(PublishStatusNotRunningWarning);
                return;
            }

            _runtime.PublishStatus(level, message, id);
        }

        /// <summary>
        /// Publishes an informational diagnostics status message.
        /// </summary>
        /// <param name="message">Human-readable diagnostic message.</param>
        /// <param name="id">Optional stable status identifier for later removal.</param>
        public void PublishInfoStatus(string message, string id = null)
            => PublishStatus(FoxgloveStatusLevel.Info, message, id);

        /// <summary>
        /// Publishes a warning diagnostics status message.
        /// </summary>
        /// <param name="message">Human-readable diagnostic message.</param>
        /// <param name="id">Optional stable status identifier for later removal.</param>
        public void PublishWarningStatus(string message, string id = null)
            => PublishStatus(FoxgloveStatusLevel.Warning, message, id);

        /// <summary>
        /// Publishes an error diagnostics status message.
        /// </summary>
        /// <param name="message">Human-readable diagnostic message.</param>
        /// <param name="id">Optional stable status identifier for later removal.</param>
        public void PublishErrorStatus(string message, string id = null)
            => PublishStatus(FoxgloveStatusLevel.Error, message, id);

        /// <summary>
        /// Removes one or more status messages from the Foxglove Problems panel.
        /// </summary>
        /// <param name="ids">Stable status identifiers to remove.</param>
        public void RemoveStatus(params string[] ids)
        {
            if (!IsRunning)
            {
                WarnStatusNotRunning(RemoveStatusNotRunningWarning);
                return;
            }

            _runtime.RemoveStatus(ids);
        }

        /// <summary>
        /// Emits one warning for status operations attempted before the server is running.
        /// </summary>
        /// <param name="message">Warning text to emit.</param>
        private void WarnStatusNotRunning(string message)
        {
            if (_warnedNotRunning)
            {
                return;
            }

            Debug.LogWarning(message);
            _warnedNotRunning = true;
        }
    }
}
