// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Owns FoxgloveManager server lifecycle and transport selection.

using System.IO;
using Unity.FoxgloveSDK.Transport;
using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxgloveManager
    {
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

            if (!ValidateTransportConfiguration())
            {
                return;
            }

            EnsureRuntimeCreated();
            RegisterAssetRoots();
            SetupPlaybackControl();
            if (!SetupRecording())
            {
                return;
            }

            if (!SetupReplay())
            {
                return;
            }

            SetupAllowedOrigins();

            try
            {
                StartCertificateDistributorIfNeeded();
                _runtime.Start(_serverName, _host, _port, enableCdrClientPublish: false);
                if (!PublishPendingRecordingSidecar())
                {
                    StopServer();
                    return;
                }
            }
            catch
            {
                CleanupPendingRecordingSidecar();
                StopCertificateDistributor();
                throw;
            }

            _replayForwarder = (topic, data) => OnReplayMessage?.Invoke(topic, data);
            _replayContextForwarder = context => OnReplayMessageContext?.Invoke(context);
            _replayBatchForwarder = context => OnReplayBatchCompleted?.Invoke(context);
            _runtime.OnReplayMessage += _replayForwarder;
            _runtime.OnReplayMessageContext += _replayContextForwarder;
            _runtime.OnReplayBatchCompleted += _replayBatchForwarder;
            _warnedNotRunning = false;

            var transport = _runtime.Session?.Transport;
            if (transport != null)
            {
                transport.OnClientConnected += EnqueueConnect;
                transport.OnClientDisconnected += EnqueueDisconnect;
                _clientMessageForwarder = (cid, chId, topic, payload) =>
                    EnqueueClientMessageEvent(new ClientEvent
                    {
                        ClientId = cid,
                        ChannelId = chId,
                        Topic = topic,
                        Payload = payload,
                        IsConnect = false,
                        IsMessage = true
                    });
                _runtime.Session.OnClientMessage += _clientMessageForwarder;
            }

            Debug.Log($"[Foxglove] Server started on {BuildConnectionUrl(redactToken: true)}");
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
                SharedToken = _sharedToken ?? string.Empty
            };

            if (_transportMode == FoxgloveTransportMode.SecureWebSocket)
            {
                var tlsOptions = new FoxgloveTlsOptions
                {
                    CertificatePfxPath = ResolveProjectPath(_certificatePfxPath),
                    CertificatePassword = _certificatePassword ?? string.Empty
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
            if (_transportMode != FoxgloveTransportMode.SecureWebSocket)
            {
                return true;
            }

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
                _transportMode == FoxgloveTransportMode.SecureWebSocket,
                _sharedToken,
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
                StopCertificateDistributor();
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

            if (_runtime.Session != null && _clientMessageForwarder != null)
            {
                _runtime.Session.OnClientMessage -= _clientMessageForwarder;
                _clientMessageForwarder = null;
            }

            if (_replayForwarder != null)
            {
                _runtime.OnReplayMessage -= _replayForwarder;
                _replayForwarder = null;
            }
            if (_replayContextForwarder != null)
            {
                _runtime.OnReplayMessageContext -= _replayContextForwarder;
                _replayContextForwarder = null;
            }
            if (_replayBatchForwarder != null)
            {
                _runtime.OnReplayBatchCompleted -= _replayBatchForwarder;
                _replayBatchForwarder = null;
            }

            _runtime.Stop();
            StopCertificateDistributor();
            _channelCache.Clear();
            ClearClientEvents();
            _nextChannelId = FirstAutoChannelId;
            if (restoreLivePublishers)
            {
                RestoreLivePublishers();
            }
        }

        private void ClearClientEvents()
        {
            _clientLifecycleEvents.Clear();
            _clientMessageEvents.Clear();
        }
    }
}
