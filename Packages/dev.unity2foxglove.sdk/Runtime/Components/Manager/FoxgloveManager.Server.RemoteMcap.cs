// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Owns FoxgloveManager Remote MCAP resource lifecycle.

using System.IO;
using Unity.FoxgloveSDK.IO;
using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxgloveManager
    {
        private RemoteMcapHttpServer _remoteMcapFileServer;
        private bool _remoteMcapFileServerConfigKnown;
        private bool _remoteMcapFileServerKnownEnabled;
        private string _remoteMcapFileServerKnownHost;
        private int _remoteMcapFileServerKnownPort;
        private string _remoteMcapFileServerKnownPath;
        private string _remoteMcapFileServerKnownSourceId;
        private string _remoteMcapFileServerKnownToken;
        private bool _warnedRemoteMcapFileServerWithoutToken;

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
            var token = ResolveRemoteMcapFileServerToken();
            if (_remoteMcapFileServerConfigKnown
                && _remoteMcapFileServerKnownEnabled == _enableRemoteMcapFileServer
                && string.Equals(_remoteMcapFileServerKnownHost, _remoteMcapFileServerHost, System.StringComparison.Ordinal)
                && _remoteMcapFileServerKnownPort == _remoteMcapFileServerPort
                && string.Equals(_remoteMcapFileServerKnownPath, path, System.StringComparison.Ordinal)
                && string.Equals(_remoteMcapFileServerKnownSourceId, _remoteMcapFileServerSourceId, System.StringComparison.Ordinal)
                && string.Equals(_remoteMcapFileServerKnownToken, token, System.StringComparison.Ordinal))
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
                RequiredBearerToken = ResolveRemoteMcapFileServerToken(),
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
            _remoteMcapFileServerKnownToken = ResolveRemoteMcapFileServerToken();
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

        private void StopRemoteMcapFileServer()
        {
            _remoteMcapFileServer?.Dispose();
            _remoteMcapFileServer = null;
            _warnedRemoteMcapFileServerWithoutToken = false;
        }
    }
}
