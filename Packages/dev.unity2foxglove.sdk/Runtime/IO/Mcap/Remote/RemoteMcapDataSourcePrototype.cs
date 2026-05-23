// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/Remote
// Purpose: Local-only prototype endpoint model for remote MCAP manifest/data behavior.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>Local-file prototype for Remote Data Loader style manifest and data operations.</summary>
    public sealed class RemoteMcapDataSourcePrototype
    {
        private readonly string _mcapPath;
        private readonly string _sourceId;
        private readonly string _manifestName;
        private readonly string _requiredBearerToken;
        private readonly string _dataRoute;

        public RemoteMcapDataSourcePrototype(
            string mcapPath,
            string sourceId,
            string manifestName,
            string requiredBearerToken)
        {
            _mcapPath = mcapPath ?? throw new ArgumentNullException(nameof(mcapPath));
            _sourceId = string.IsNullOrEmpty(sourceId) ? "local-mcap" : sourceId;
            _manifestName = string.IsNullOrEmpty(manifestName) ? _sourceId : manifestName;
            _requiredBearerToken = requiredBearerToken ?? string.Empty;
            _dataRoute = "/data?sourceId=" + Uri.EscapeDataString(_sourceId);
        }

        public RemoteMcapManifestResponse GetManifest(RemoteMcapRequest request)
        {
            request = request ?? new RemoteMcapRequest();
            if (IsUnsupportedMultiSource(request))
                return ManifestProblem(RemoteMcapResponseStatus.Unsupported, "UnsupportedMultiSource",
                    "Phase 119 prototype supports one local MCAP source only.");

            var authorization = Authorize(request);
            if (!authorization.Allowed)
            {
                var denied = ManifestProblem(RemoteMcapResponseStatus.Unauthorized, "Unauthorized",
                    "Manifest request is not authorized for this MCAP source.");
                denied.Authorization = authorization;
                return denied;
            }

            using var loader = new McapDataLoader(_mcapPath);
            var init = loader.Initialize();
            return new RemoteMcapManifestResponse
            {
                Status = RemoteMcapResponseStatus.Ok,
                Authorization = authorization,
                Manifest = RemoteMcapManifestMapper.FromInitialization(init, _manifestName, _sourceId, _dataRoute)
            };
        }

        public RemoteMcapDataResponse GetData(RemoteMcapRequest request)
        {
            request = request ?? new RemoteMcapRequest();
            if (IsUnsupportedMultiSource(request))
                return DataProblem(RemoteMcapResponseStatus.Unsupported, "UnsupportedMultiSource",
                    "Phase 119 prototype supports one local MCAP source only.");

            var authorization = Authorize(request);
            if (!authorization.Allowed)
            {
                var denied = DataProblem(RemoteMcapResponseStatus.Unauthorized, "Unauthorized",
                    "Data request is not authorized for this MCAP source.");
                denied.Authorization = authorization;
                return denied;
            }

            if (!string.Equals(request.SourceId, _sourceId, StringComparison.Ordinal))
                return DataProblem(RemoteMcapResponseStatus.NotFound, "SourceNotFound",
                    "Requested MCAP source id is not available in this prototype.");

            return new RemoteMcapDataResponse
            {
                Status = RemoteMcapResponseStatus.Ok,
                Authorization = authorization,
                SourceId = _sourceId,
                Data = File.ReadAllBytes(_mcapPath)
            };
        }

        private RemoteMcapAuthorizationResult Authorize(RemoteMcapRequest request)
        {
            if (string.IsNullOrEmpty(_requiredBearerToken))
                return RemoteMcapAuthorizationResult.Allow();

            var token = request.BearerToken ?? string.Empty;
            const string bearerPrefix = "Bearer ";
            if (token.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
                token = token.Substring(bearerPrefix.Length);

            return string.Equals(token, _requiredBearerToken, StringComparison.Ordinal)
                ? RemoteMcapAuthorizationResult.Allow()
                : RemoteMcapAuthorizationResult.Deny("Bearer token rejected.");
        }

        private static bool IsUnsupportedMultiSource(RemoteMcapRequest request)
        {
            return request.RequestMultipleSources
                || (request.RequestedSourceIds != null && request.RequestedSourceIds.Count > 1);
        }

        private static RemoteMcapManifestResponse ManifestProblem(
            RemoteMcapResponseStatus status,
            string code,
            string message)
        {
            var response = new RemoteMcapManifestResponse { Status = status };
            response.Problems.Add(new RemoteMcapProblem(status.ToString(), code, message));
            return response;
        }

        private static RemoteMcapDataResponse DataProblem(
            RemoteMcapResponseStatus status,
            string code,
            string message)
        {
            var response = new RemoteMcapDataResponse { Status = status };
            response.Problems.Add(new RemoteMcapProblem(status.ToString(), code, message));
            return response;
        }
    }
}
