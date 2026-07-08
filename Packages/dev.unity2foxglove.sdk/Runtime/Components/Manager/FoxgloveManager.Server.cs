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
        private bool _replayCursorEndpointConfigKnown;
        private bool _replayCursorEndpointKnownEnabled;
        private string _replayCursorEndpointKnownHost;
        private int _replayCursorEndpointKnownPort;
        private string _replayCursorEndpointKnownToken;
        private RemoteMcapHttpServer _remoteMcapFileServer;
        private bool _remoteMcapFileServerConfigKnown;
        private bool _remoteMcapFileServerKnownEnabled;
        private string _remoteMcapFileServerKnownHost;
        private int _remoteMcapFileServerKnownPort;
        private string _remoteMcapFileServerKnownPath;
        private string _remoteMcapFileServerKnownSourceId;
        private string _remoteMcapFileServerKnownToken;
        private bool _warnedRemoteMcapFileServerWithoutToken;

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
                _runtime.Start(_serverName, _host, _port, enableCdrClientPublish: false);
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
                _clientMessageForwarder = (cid, chId, topic, payload) =>
                    EnqueueClientMessageEvent(ClientEvent.Message(cid, chId, topic, payload));
                _runtime.Session.OnClientMessage += _clientMessageForwarder;
            }

            Debug.Log(StatusTextBuilder.CreateServerStartedMessage(BuildConnectionUrl(redactToken: true)));
        }

        private void CleanupStartupAfterFailure()
        {
            CleanupPendingRecordingSidecar();
            StopRemoteMcapFileServer();
            StopReplayCursorEndpoint();
            StopCertificateDistributor();
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
            if (!IsRunning)
            {
                StopRemoteMcapFileServer();
                StopReplayCursorEndpoint();
                StopCertificateDistributor();
                DetachRuntimeForwarders(_runtime?.Session);
                if (_runtime?.Session == null)
                {
                    return;
                }
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
            _runtime.Stop();
            _sharedSensorClock.Reset();
            StopRemoteMcapFileServer();
            StopReplayCursorEndpoint();
            StopCertificateDistributor();
            _channelCache.Clear();
            ClearClientEvents();
            _connectionState.ResetChannelIds(FirstAutoChannelId);
            if (restoreLivePublishers)
            {
                RestoreLivePublishers();
            }
        }

        private void StartRemoteMcapFileServerIfNeeded()
        {
            if (!_enableRemoteMcapFileServer || !_enableReplay || string.IsNullOrWhiteSpace(_replayFilePath))
            {
                StopRemoteMcapFileServer();
                RememberRemoteMcapFileServerConfig(null);
                return;
            }

            var path = ResolveReplayFilePathCached();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                StopRemoteMcapFileServer();
                RememberRemoteMcapFileServerConfig(path);
                return;
            }

            var options = BuildRemoteMcapFileServerOptions(path);
            try
            {
                _remoteMcapFileServer?.Dispose();
                _remoteMcapFileServer = RemoteMcapHttpServer.Start(options);
                Debug.Log("[Foxglove] Remote MCAP file URL ready: " + BuildRemoteMcapFileUrl(options));
                WarnIfRemoteMcapFileServerHasNoToken(options);
            }
            catch (System.Exception ex)
            {
                StopRemoteMcapFileServer();
                Debug.LogWarning("[Foxglove] Remote MCAP file URL disabled: " + ex.Message);
            }

            RememberRemoteMcapFileServerConfig(path);
        }

        private void RefreshRemoteMcapFileServerIfNeeded()
        {
            if (!IsRunning)
            {
                if (_remoteMcapFileServerConfigKnown)
                {
                    StopRemoteMcapFileServer();
                    ClearRemoteMcapFileServerConfig();
                }

                return;
            }

            var path = _enableReplay && !string.IsNullOrWhiteSpace(_replayFilePath)
                ? ResolveReplayFilePathCached()
                : null;
            if (_remoteMcapFileServerConfigKnown
                && _remoteMcapFileServerKnownEnabled == _enableRemoteMcapFileServer
                && string.Equals(_remoteMcapFileServerKnownHost, _remoteMcapFileServerHost, System.StringComparison.Ordinal)
                && _remoteMcapFileServerKnownPort == _remoteMcapFileServerPort
                && string.Equals(_remoteMcapFileServerKnownPath, path, System.StringComparison.Ordinal)
                && string.Equals(_remoteMcapFileServerKnownSourceId, _remoteMcapFileServerSourceId, System.StringComparison.Ordinal)
                && string.Equals(_remoteMcapFileServerKnownToken, _remoteMcapFileServerToken, System.StringComparison.Ordinal))
            {
                return;
            }

            StartRemoteMcapFileServerIfNeeded();
        }

        private string ResolveReplayFilePathCached()
        {
            if (string.Equals(_replayState.CachedReplayFilePathInput, _replayFilePath, System.StringComparison.Ordinal)
                && _replayState.CachedResolvedReplayFilePath != null)
            {
                return _replayState.CachedResolvedReplayFilePath;
            }

            _replayState.CachedReplayFilePathInput = _replayFilePath;
            _replayState.CachedResolvedReplayFilePath = ResolveProjectPath(_replayFilePath);
            return _replayState.CachedResolvedReplayFilePath;
        }

        private RemoteMcapHttpOptions BuildRemoteMcapFileServerOptions(string resolvedPath)
        {
            return new RemoteMcapHttpOptions
            {
                Host = string.IsNullOrWhiteSpace(_remoteMcapFileServerHost) ? "127.0.0.1" : _remoteMcapFileServerHost.Trim(),
                Port = _remoteMcapFileServerPort,
                McapPath = resolvedPath,
                SourceId = string.IsNullOrWhiteSpace(_remoteMcapFileServerSourceId) ? "local-mcap" : _remoteMcapFileServerSourceId.Trim(),
                RequiredBearerToken = string.IsNullOrWhiteSpace(_remoteMcapFileServerToken) ? string.Empty : _remoteMcapFileServerToken.Trim(),
                ManifestName = Path.GetFileName(resolvedPath)
            };
        }

        private static string BuildRemoteMcapFileUrl(RemoteMcapHttpOptions options)
            => options.BaseUrl + options.DirectFileRoute;

        private void WarnIfRemoteMcapFileServerHasNoToken(RemoteMcapHttpOptions options)
        {
            if (options == null || !string.IsNullOrEmpty(options.RequiredBearerToken))
            {
                _warnedRemoteMcapFileServerWithoutToken = false;
                return;
            }

            if (_warnedRemoteMcapFileServerWithoutToken)
                return;

            _warnedRemoteMcapFileServerWithoutToken = true;
            Debug.LogWarning("[Foxglove] Remote MCAP file URL is running without a bearer token. "
                             + "Because the endpoint uses wildcard CORS for Foxglove Remote files, "
                             + "any browser origin on this machine can read the served loopback MCAP while it is enabled.");
        }

        private void RememberRemoteMcapFileServerConfig(string resolvedPath)
        {
            _remoteMcapFileServerConfigKnown = true;
            _remoteMcapFileServerKnownEnabled = _enableRemoteMcapFileServer;
            _remoteMcapFileServerKnownHost = _remoteMcapFileServerHost;
            _remoteMcapFileServerKnownPort = _remoteMcapFileServerPort;
            _remoteMcapFileServerKnownPath = resolvedPath;
            _remoteMcapFileServerKnownSourceId = _remoteMcapFileServerSourceId;
            _remoteMcapFileServerKnownToken = _remoteMcapFileServerToken;
        }

        private void ClearRemoteMcapFileServerConfig()
        {
            _remoteMcapFileServerConfigKnown = false;
            _remoteMcapFileServerKnownEnabled = false;
            _remoteMcapFileServerKnownHost = null;
            _remoteMcapFileServerKnownPort = 0;
            _remoteMcapFileServerKnownPath = null;
            _remoteMcapFileServerKnownSourceId = null;
            _remoteMcapFileServerKnownToken = null;
        }

        private void DetachRuntimeForwarders(FoxgloveSession session)
        {
            if (session != null && _clientMessageForwarder != null)
                session.OnClientMessage -= _clientMessageForwarder;
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

        private void StopRemoteMcapFileServer()
        {
            _remoteMcapFileServer?.Dispose();
            _remoteMcapFileServer = null;
            _warnedRemoteMcapFileServerWithoutToken = false;
        }

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
            }
            catch (System.Exception ex)
            {
                _runtime.SetExternalReplayCursorEnabled(false);
                _replayCursorEndpoint.Stop();
                Debug.LogWarning("[Foxglove] Replay cursor bridge disabled: " + ex.Message);
            }

            RememberReplayCursorEndpointConfig();
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

        private string ResolveSharedToken()
            => ResolveSecretValue(SharedTokenEnvironmentVariable, _sharedToken);

        private string ResolveCertificatePassword()
            => ResolveSecretValue(CertificatePasswordEnvironmentVariable, _certificatePassword);

        private string ResolveReplayCursorBridgeToken()
            => ResolveSecretValue(ReplayCursorBridgeTokenEnvironmentVariable, _replayCursorBridgeToken);

        private static string ResolveSecretValue(string environmentVariable, string serializedValue)
        {
            var environmentValue = Environment.GetEnvironmentVariable(environmentVariable);
            return string.IsNullOrEmpty(environmentValue)
                ? serializedValue ?? string.Empty
                : environmentValue;
        }

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
