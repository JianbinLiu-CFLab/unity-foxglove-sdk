// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Owns FoxgloveManager server lifecycle and transport selection.

using System;
using System.Globalization;
using System.IO;
using System.Runtime.ExceptionServices;
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
            if (!IsRunning && HasRetainedRuntimeForwarders())
                throw new InvalidOperationException(
                    "A previous server session still owns callbacks; complete its cleanup before restarting.");

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

                AttachRuntimeForwarders();
            }
            catch
            {
                CleanupStartupAfterFailure();
                throw;
            }

            _warningDebounceState.ResetNotRunning();

            Debug.Log(StatusTextBuilder.CreateServerStartedMessage(BuildConnectionUrl(redactToken: true)));
        }

        private bool HasRetainedRuntimeForwarders()
            => _clientConnectedForwarder != null
               || _clientDisconnectedForwarder != null
               || _clientMessageForwarder != null
               || _replayForwarder != null
               || _replayContextForwarder != null
               || _replayBatchForwarder != null;

        private void CleanupStartupAfterFailure()
        {
            TryCleanupStartupStep(CleanupPendingRecordingSidecar, "cleanup pending recording sidecar");
            TryCleanupStartupStep(StopRemoteMcapFileServer, "stop remote MCAP file server");
            TryCleanupStartupStep(StopReplayCursorEndpoint, "stop replay cursor endpoint");
            TryCleanupStartupStep(StopCertificateDistributor, "stop certificate distributor");
            TryCleanupStartupStep(UnregisterFoxRunSubscriptionCatalogService, "unregister FoxRun subscription catalog service");
            TryCleanupStartupStep(
                () => DetachTransportForwarders(_runtime?.CleanupSession),
                "detach transport forwarders after failed startup");
            TryCleanupStartupStep(
                () => DetachRuntimeForwarders(_runtime?.CleanupSession),
                "detach runtime forwarders after failed startup");
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

        private void AttachRuntimeForwarders()
        {
            _replayForwarder = (topic, data) => OnReplayMessage?.Invoke(topic, data);
            _replayContextForwarder = context => OnReplayMessageContext?.Invoke(context);
            _replayBatchForwarder = context => OnReplayBatchCompleted?.Invoke(context);
            _runtime.OnReplayMessage += _replayForwarder;
            _runtime.OnReplayMessageContext += _replayContextForwarder;
            _runtime.OnReplayBatchCompleted += _replayBatchForwarder;

            AdvanceChannelSessionGeneration();
            var transport = _runtime.Session?.Transport;
            if (transport == null)
                return;

            var generation = _connectionState.ChannelSessionGeneration;
            _clientConnectedForwarder = id =>
                EnqueueClientLifecycleEvent(ClientEvent.Connect(generation, id));
            _clientDisconnectedForwarder = id =>
                EnqueueClientLifecycleEvent(ClientEvent.Disconnect(generation, id));
            _clientMessageForwarder = (cid, chId, topic, encoding, payload) =>
                EnqueueClientMessageEvent(ClientEvent.Message(
                    generation, cid, chId, topic, encoding, payload));
            transport.OnClientConnected += _clientConnectedForwarder;
            transport.OnClientDisconnected += _clientDisconnectedForwarder;
            _runtime.Session.OnClientMessageWithEncoding += _clientMessageForwarder;
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
            var cleanupSession = _runtime?.CleanupSession;
            ExceptionDispatchInfo firstFailure = null;
            if (!IsRunning)
            {
                var earlyFailure = RunStopPreTailCleanup(
                    StopRemoteMcapFileServer,
                    StopReplayCursorEndpoint,
                    StopCertificateDistributor,
                    () => DetachTransportForwarders(cleanupSession),
                    () => DetachRuntimeForwarders(_runtime?.Session));
                firstFailure ??= earlyFailure;
                if (!FoxgloveManagerTeardownState.ShouldRunStopServer(
                        IsRunning,
                        _runtime?.Session != null,
                        _runtime?.HasPendingSessionCleanup ?? false))
                {
                    firstFailure?.Throw();
                    return;
                }
            }

            try
            {
                // Capture and detach manager callbacks before runtime Stop
                // clears the active Session and would otherwise hide the
                // Transport reference. Each pre-tail operation is guarded
                // independently so a failing event accessor cannot skip the
                // centralized runtime Stop tail.
                var preTailFailure = RunStopPreTailCleanup(
                    () => DetachTransportForwarders(cleanupSession),
                    () => DetachRuntimeForwarders(cleanupSession),
                    AdvanceChannelSessionGeneration,
                    UnregisterFoxRunSubscriptionCatalogService);
                firstFailure ??= preTailFailure;

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
            catch (Exception exception)
            {
                firstFailure ??= ExceptionDispatchInfo.Capture(exception);
            }

            firstFailure?.Throw();
        }

        private static ExceptionDispatchInfo RunStopPreTailCleanup(params Action[] steps)
        {
            var failure = FoxgloveManagerTeardownState.RunCleanupReturningFirstFailure(steps);
            if (failure != null)
            {
                try
                {
                    Debug.LogWarning(
                        "[Foxglove] StopServer pre-tail cleanup reported a failure: "
                        + failure.SourceException.Message);
                }
                catch
                {
                    // Diagnostics must not prevent the mandatory stop tail.
                }
            }

            return failure;
        }

        private void DetachTransportForwarders(FoxgloveSession session)
        {
            session ??= _runtime?.CleanupSession;
            if (session == null)
                return;

            var transport = session.Transport;
            ExceptionDispatchInfo firstFailure = null;
            if (_clientConnectedForwarder != null)
            {
                TryDetach(
                    () => transport.OnClientConnected -= _clientConnectedForwarder,
                    () => _clientConnectedForwarder = null,
                    ref firstFailure);
            }
            if (_clientDisconnectedForwarder != null)
            {
                TryDetach(
                    () => transport.OnClientDisconnected -= _clientDisconnectedForwarder,
                    () => _clientDisconnectedForwarder = null,
                    ref firstFailure);
            }
            firstFailure?.Throw();
        }

        private void DetachRuntimeForwarders(FoxgloveSession session)
        {
            session ??= _runtime?.CleanupSession;
            ExceptionDispatchInfo firstFailure = null;
            if (session != null && _clientMessageForwarder != null)
            {
                TryDetach(
                    () => session.OnClientMessageWithEncoding -= _clientMessageForwarder,
                    () => _clientMessageForwarder = null,
                    ref firstFailure);
            }

            if (_runtime != null)
            {
                if (_replayForwarder != null)
                    TryDetach(
                        () => _runtime.OnReplayMessage -= _replayForwarder,
                        () => _replayForwarder = null,
                        ref firstFailure);
                if (_replayContextForwarder != null)
                    TryDetach(
                        () => _runtime.OnReplayMessageContext -= _replayContextForwarder,
                        () => _replayContextForwarder = null,
                        ref firstFailure);
                if (_replayBatchForwarder != null)
                    TryDetach(
                        () => _runtime.OnReplayBatchCompleted -= _replayBatchForwarder,
                        () => _replayBatchForwarder = null,
                        ref firstFailure);
            }

            firstFailure?.Throw();
        }

        private static void TryDetach(
            Action detach,
            Action clearReference,
            ref ExceptionDispatchInfo firstFailure)
        {
            if (detach == null)
                return;

            try
            {
                detach();
                clearReference?.Invoke();
            }
            catch (Exception exception)
            {
                firstFailure ??= ExceptionDispatchInfo.Capture(exception);
            }
        }

    }
}
