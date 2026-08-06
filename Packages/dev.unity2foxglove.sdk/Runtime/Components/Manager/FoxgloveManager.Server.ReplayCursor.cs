// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Owns FoxgloveManager replay cursor endpoint lifecycle.

using System.Globalization;
using Unity.FoxgloveSDK.Core;
using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxgloveManager
    {
        private bool _replayCursorEndpointConfigKnown;
        private bool _replayCursorEndpointKnownEnabled;
        private string _replayCursorEndpointKnownHost;
        private int _replayCursorEndpointKnownPort;
        private string _replayCursorEndpointKnownToken;

        private void StartReplayCursorEndpointIfNeeded()
        {
            if (_runtime == null)
            {
                return;
            }

            var shouldRunEndpoint = ShouldRunReplayCursorEndpoint();
            _runtime.SetExternalReplayCursorEnabled(shouldRunEndpoint);
            if (!shouldRunEndpoint)
            {
                StopReplayCursorEndpoint();
                RememberReplayCursorEndpointConfig();
                return;
            }

            _replayCursorEndpoint ??= new UnityReplayCursorEndpoint(new UnityLogger());
            var options = new UnityReplayCursorEndpointOptions(
                enabled: true,
                host: _replayCursorBridgeHost,
                port: _replayCursorBridgePort,
                path: "/v1/replay-cursor",
                bearerToken: ResolveReplayCursorBridgeToken(),
                maxBodyBytes: UnityReplayCursorEndpointOptions.Default.MaxBodyBytes);
            try
            {
                _replayCursorEndpointLoggedFirstCursor = false;
                _replayCursorEndpointLoggedUnavailable = false;
                _replayCursorEndpoint.Start(options, QueueExternalReplayCursor, GetExternalReplayCursorState);
                Debug.Log("[Foxglove] Replay cursor endpoint ready: http://"
                          + options.Host
                          + ":"
                          + options.Port.ToString(CultureInfo.InvariantCulture)
                          + options.Path);
                RememberReplayCursorEndpointConfig();
            }
            catch (System.Exception ex)
            {
                _runtime.SetExternalReplayCursorEnabled(false);
                _replayCursorEndpoint.Stop();
                ClearReplayCursorEndpointConfig();
                Debug.LogWarning("[Foxglove] Replay cursor bridge disabled: " + ex.Message);
            }
        }

        /// <summary>
        /// Applies Inspector changes to the optional replay cursor endpoint while Play Mode is running.
        /// </summary>
        private void RefreshReplayCursorEndpointIfNeeded()
        {
            if (!IsRunning)
            {
                if (_replayCursorEndpointConfigKnown)
                {
                    StopReplayCursorEndpoint();
                    ClearReplayCursorEndpointConfig();
                }

                return;
            }

            var shouldRunEndpoint = ShouldRunReplayCursorEndpoint();
            var token = ResolveReplayCursorBridgeToken();
            if (_replayCursorEndpointConfigKnown
                && _replayCursorEndpointKnownEnabled == shouldRunEndpoint
                && string.Equals(_replayCursorEndpointKnownHost, _replayCursorBridgeHost, System.StringComparison.Ordinal)
                && _replayCursorEndpointKnownPort == _replayCursorBridgePort
                && string.Equals(_replayCursorEndpointKnownToken, token, System.StringComparison.Ordinal))
            {
                return;
            }

            StartReplayCursorEndpointIfNeeded();
        }

        private void RememberReplayCursorEndpointConfig()
        {
            _replayCursorEndpointConfigKnown = true;
            _replayCursorEndpointKnownEnabled = ShouldRunReplayCursorEndpoint();
            _replayCursorEndpointKnownHost = _replayCursorBridgeHost;
            _replayCursorEndpointKnownPort = _replayCursorBridgePort;
            _replayCursorEndpointKnownToken = ResolveReplayCursorBridgeToken();
        }

        private bool ShouldRunReplayCursorEndpoint()
            => _enableReplayCursorBridge || _remoteMcapFileServer != null;

        private void ClearReplayCursorEndpointConfig()
        {
            _replayCursorEndpointConfigKnown = false;
            _replayCursorEndpointKnownEnabled = false;
            _replayCursorEndpointKnownHost = null;
            _replayCursorEndpointKnownPort = 0;
            _replayCursorEndpointKnownToken = null;
        }

        private UnityReplayCursorEndpointQueueResult QueueExternalReplayCursor(ReplayCursorRequest request)
        {
            if (_runtime == null)
            {
                return new UnityReplayCursorEndpointQueueResult(false, "Runtime is not available.");
            }

            var result = _runtime.TryEnqueueExternalReplayCursor(request, out var message);
            if (!_replayCursorEndpointLoggedUnavailable
                && result == ExternalReplayCursorEnqueueResult.ReplayUnavailable)
            {
                _replayCursorEndpointLoggedUnavailable = true;
                Debug.LogWarning("[Foxglove] Foxglove timeline sync is on but external cursor control is unavailable. "
                                 + message);
            }

            if (!_replayCursorEndpointLoggedFirstCursor
                && (result == ExternalReplayCursorEnqueueResult.Accepted
                    || result == ExternalReplayCursorEnqueueResult.Duplicate))
            {
                _replayCursorEndpointLoggedFirstCursor = true;
                Debug.Log("[Foxglove] Replay cursor bridge received cursor from "
                          + (string.IsNullOrWhiteSpace(request.Source) ? "unknown" : request.Source)
                          + " seq=" + request.Sequence.ToString(CultureInfo.InvariantCulture)
                          + " time=" + request.Sec.ToString(CultureInfo.InvariantCulture)
                          + "." + request.Nsec.ToString("D9", CultureInfo.InvariantCulture));
            }

            return new UnityReplayCursorEndpointQueueResult(
                result == ExternalReplayCursorEnqueueResult.Accepted
                || result == ExternalReplayCursorEnqueueResult.Duplicate,
                message);
        }

        private ReplayCursorState GetExternalReplayCursorState()
            => _runtime?.GetExternalReplayCursorState()
               ?? ReplayCursorState.Unavailable("Runtime is not available.");

        private void StopReplayCursorEndpoint()
        {
            _runtime?.SetExternalReplayCursorEnabled(false);
            _replayCursorEndpointLoggedFirstCursor = false;
            _replayCursorEndpointLoggedUnavailable = false;
            _replayCursorEndpoint?.Stop();
        }
    }
}
