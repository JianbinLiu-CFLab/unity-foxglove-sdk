// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Manages optional root CA distribution for local WSS development.

using System.IO;
using Unity.FoxgloveSDK.Transport;
using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxgloveManager
    {
        /// <summary>
        /// Loopback address accepted as a safe root CA distributor bind host.
        /// </summary>
        private const string LoopbackHost = "127.0.0.1";

        /// <summary>
        /// Localhost name accepted as a safe root CA distributor bind host.
        /// </summary>
        private const string LocalhostName = "localhost";

        /// <summary>
        /// Starts the root CA distributor when secure WebSocket certificate sharing is enabled.
        /// </summary>
        private void StartCertificateDistributorIfNeeded()
        {
            if (ActiveTransportMode != FoxgloveTransportMode.SecureWebSocket || !_rootCaDistributorEnabled)
            {
                return;
            }

            var rootCaPath = ResolveProjectPath(_rootCaFilePath);
            if (string.IsNullOrEmpty(rootCaPath) || !File.Exists(rootCaPath))
            {
                Debug.LogWarning("[Foxglove] Root CA distributor is enabled, but the root CA file is missing.");
                return;
            }

            if (_rootCaDistributorHost != LoopbackHost && _rootCaDistributorHost != LocalhostName)
            {
                Debug.LogWarning("[Foxglove] Root CA distributor is not bound to loopback. Only use this on trusted networks.");
            }

            StopCertificateDistributor();
            _certificateDistributor = new FoxgloveCertificateDistributor(rootCaPath, logger: new UnityLogger());
            _certificateDistributor.Start(_rootCaDistributorHost, _rootCaDistributorPort);
            Debug.Log(
                $"[Foxglove] Root CA distributor started on http://{_rootCaDistributorHost}:{_rootCaDistributorPort}/rootCA.crt "
                + $"SHA-256={_certificateDistributor.RootCaSha256Fingerprint}");
        }

        /// <summary>
        /// Stops and disposes the root CA distributor if it is active.
        /// </summary>
        private void StopCertificateDistributor()
        {
            _certificateDistributor?.Dispose();
            _certificateDistributor = null;
        }
    }
}
