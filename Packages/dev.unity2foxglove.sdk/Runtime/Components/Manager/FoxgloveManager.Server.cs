// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Owns FoxgloveManager server lifecycle and transport selection.

using System;
using System.Globalization;
using System.IO;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Transport;
using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxgloveManager
    {
        /// <summary>
        /// Transport mode used for listener operations when output is enabled.
        /// </summary>
        private FoxgloveTransportMode ActiveTransportMode
            => _transportMode == FoxgloveTransportMode.None ? FoxgloveTransportMode.WebSocket : _transportMode;

        /// <summary>
        /// Starts the WebSocket server and wires transport callbacks into the Unity main-thread queue.
        /// </summary>
        public void StartServer()
        {
            if (!BeginFoxRunTransportSessionIfNeeded())
            {
                _startServerAfterTransportCapture = true;
                return;
            }

            _startServerAfterTransportCapture = false;
            BeginFoxRunPublishSessionIfNeeded();
            BeginFoxRunSubscriptionSessionIfNeeded();

            if (IsRunning)
            {
                Debug.LogWarning("[Foxglove] Server already running.");
                return;
            }

            if (!_foxgloveOutputEnabled)
            {
                return;
            }

            if (!ValidateTransportConfiguration())
            {
                return;
            }

            EnsureRuntimeCreated();

            try
            {
                FoxRunSchemaInfoRegistry.RegisterGeneratedSchemas(_runtime.Schemas);
                RegisterAssetRoots();
                SetupPlaybackControl();
                if (!SetupRecording())
                {
                    CleanupStartupAfterFailure();
                    return;
                }

                if (!SetupReplay())
                {
                    CleanupStartupAfterFailure();
                    return;
                }

                SetupAllowedOrigins();
                StartCertificateDistributorIfNeeded();
                RegisterFoxRunSubscriptionCatalogService();
                _runtime.Start(_serverName, _host, _port);
                StartRemoteMcapFileServerIfNeeded();
                StartReplayCursorEndpointIfNeeded();
                if (!PublishPendingRecordingSidecar())
                {
                    StopServer();
                    return;
                }
            }
            catch
            {
                CleanupStartupAfterFailure();
                throw;
            }

            _replayForwarder = (topic, data) => OnReplayMessage?.Invoke(topic, data);
            _replayContextForwarder = context => OnReplayMessageContext?.Invoke(context);
            _replayBatchForwarder = context => OnReplayBatchCompleted?.Invoke(context);
            _runtime.OnReplayMessage += _replayForwarder;
            _runtime.OnReplayMessageContext += _replayContextForwarder;
            _runtime.OnReplayBatchCompleted += _replayBatchForwarder;
            _warningDebounceState.ResetNotRunning();
            AdvanceChannelSessionGeneration();

            var transport = _runtime.Session?.Transport;
            if (transport != null)
            {
                transport.OnClientConnected += EnqueueConnect;
                transport.OnClientDisconnected += EnqueueDisconnect;
                _clientMessageForwarder = (cid, chId, topic, encoding, payload) =>
                    EnqueueClientMessageEvent(ClientEvent.Message(cid, chId, topic, encoding, payload));
                _runtime.Session.OnClientMessageWithEncoding += _clientMessageForwarder;
            }

            Debug.Log(StatusTextBuilder.CreateServerStartedMessage(BuildConnectionUrl(redactToken: true)));
        }

        private void CleanupStartupAfterFailure()
        {
            CleanupPendingRecordingSidecar();
            StopRemoteMcapFileServer();
            StopReplayCursorEndpoint();
            StopCertificateDistributor();
            UnregisterFoxRunSubscriptionCatalogService();
            TryCleanupStartupStep(() => _runtime?.Stop(), "stop runtime after failed startup");
            TryCleanupStartupStep(() => _runtime?.DisableReplay(), "disable replay after failed startup");
            TryCleanupStartupStep(() => _runtime?.DisableRecording(), "disable recording after failed startup");
            TryCleanupStartupStep(RestoreLivePublishers, "restore live publishers after failed startup");
        }

        private static void TryCleanupStartupStep(System.Action cleanup, string description)
        {
            try
            {
                cleanup?.Invoke();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[Foxglove] Failed to " + description + ": " + ex.Message);
            }
        }

        /// <summary>
        /// Creates the selected plain or secure transport from Inspector settings.
        /// </summary>
        /// <param name="logger">Logger used by the managed transport backend.</param>
        /// <returns>The configured Foxglove transport.</returns>
        private IFoxgloveTransport CreateTransport(Core.IFoxgloveLogger logger)
        {
            var options = new ManagedWebSocketOptions
            {
                SharedToken = ResolveSharedToken()
            };

            if (ActiveTransportMode == FoxgloveTransportMode.SecureWebSocket)
            {
                var tlsOptions = new FoxgloveTlsOptions
                {
                    CertificatePfxPath = ResolveProjectPath(_certificatePfxPath),
                    CertificatePassword = ResolveCertificatePassword()
                };
                return new ManagedWssBackend(tlsOptions, options, logger);
            }

            return new ManagedWsBackend(options, logger);
        }

        /// <summary>
        /// Validates transport-specific Inspector settings before mutating runtime startup state.
        /// </summary>
        /// <returns>True when the configured transport can be started.</returns>
        private bool ValidateTransportConfiguration()
        {
            if (!_foxgloveOutputEnabled)
            {
                return true;
            }

            if (!ManagerConfigValidator.IsValidTcpPort(_port))
            {
                Debug.LogError($"[Foxglove] Server port must be between 1 and 65535. Current value: {_port}");
                return false;
            }

            if (!ManagerConfigValidator.IsSupportedBindHost(_host))
            {
                Debug.LogError($"[Foxglove] Unsupported bind host '{_host}'. Use an IP address, localhost, 0.0.0.0, *, or ::.");
                return false;
            }

            if (_rootCaDistributorEnabled && !ManagerConfigValidator.IsValidTcpPort(_rootCaDistributorPort))
            {
                Debug.LogError($"[Foxglove] Root CA distributor port must be between 1 and 65535. Current value: {_rootCaDistributorPort}");
                return false;
            }

            if (ActiveTransportMode != FoxgloveTransportMode.SecureWebSocket)
                return true;

            var pfxPath = ResolveProjectPath(_certificatePfxPath);
            if (string.IsNullOrWhiteSpace(pfxPath))
            {
                Debug.LogError("[Foxglove] SecureWebSocket requires Certificate Pfx Path. Set a .pfx file in Security / WSS or switch Transport Mode to WebSocket.");
                return false;
            }

            if (!File.Exists(pfxPath))
            {
                Debug.LogError($"[Foxglove] SecureWebSocket certificate PFX was not found: {pfxPath}");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Builds the browser connection URL for the current manager settings.
        /// </summary>
        /// <param name="redactToken">Whether the shared token should be redacted in the returned URL.</param>
        /// <returns>A WebSocket URL suitable for Foxglove clients.</returns>
        private string BuildConnectionUrl(bool redactToken)
        {
            return FoxgloveAppUrl.BuildWebSocketEndpoint(
                _host,
                _port,
                ActiveTransportMode == FoxgloveTransportMode.SecureWebSocket,
                ResolveSharedToken(),
                redactToken);
        }

        /// <summary>
        /// Stops the WebSocket server and restores live publishers.
        /// </summary>
        public void StopServer() => StopServer(restoreLivePublishers: true);

        /// <summary>
        /// Stops the WebSocket server while preserving runtime cleanup and MCAP finalization order.
        /// </summary>
        /// <param name="restoreLivePublishers">Whether live publishers should be restored after shutdown.</param>
        private void StopServer(bool restoreLivePublishers)
        {
            _startServerAfterTransportCapture = false;
            if (!IsRunning)
            {
                StopRemoteMcapFileServer();
                StopReplayCursorEndpoint();
                StopCertificateDistributor();
                DetachRuntimeForwarders(_runtime?.Session);
                if (_runtime?.Session == null)
                    return;
            }

            // Capture and detach manager callbacks before runtime Stop clears
            // the active Session and would otherwise hide the Transport reference.
            var transport = _runtime.Session?.Transport;
            if (transport != null)
            {
                transport.OnClientConnected -= EnqueueConnect;
                transport.OnClientDisconnected -= EnqueueDisconnect;
            }

            DetachRuntimeForwarders(_runtime?.Session);

            AdvanceChannelSessionGeneration();
            UnregisterFoxRunSubscriptionCatalogService();
            FoxgloveManagerTeardownState.RunStopServer(
                _runtime.Stop,
                _sharedSensorClock.Reset,
                StopRemoteMcapFileServer,
                StopReplayCursorEndpoint,
                StopCertificateDistributor,
                () =>
                {
                    _channelCache.Clear();
                    _foxRunRecordingChannelCache.Clear();
                    _foxRunRawRecordingChannelCache.Clear();
                },
                ClearClientEvents,
                () => _connectionState.ResetChannelIds(FirstAutoChannelId),
                restoreLivePublishers ? RestoreLivePublishers : null);
        }

        private void DetachRuntimeForwarders(FoxgloveSession session)
        {
            if (session != null && _clientMessageForwarder != null)
                session.OnClientMessageWithEncoding -= _clientMessageForwarder;
            _clientMessageForwarder = null;

            if (_runtime != null)
            {
                if (_replayForwarder != null)
                    _runtime.OnReplayMessage -= _replayForwarder;
                if (_replayContextForwarder != null)
                    _runtime.OnReplayMessageContext -= _replayContextForwarder;
                if (_replayBatchForwarder != null)
                    _runtime.OnReplayBatchCompleted -= _replayBatchForwarder;
            }

            _replayForwarder = null;
            _replayContextForwarder = null;
            _replayBatchForwarder = null;
        }

    }
}
