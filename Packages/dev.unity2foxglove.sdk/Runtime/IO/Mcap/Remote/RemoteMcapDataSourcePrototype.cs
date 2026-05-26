// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/Remote
// Purpose: Local-only prototype endpoint model for remote MCAP manifest/data behavior.

using System;
using System.IO;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>Local-file prototype for Remote Data Loader style manifest and data operations.</summary>
    public sealed class RemoteMcapDataSourcePrototype
    {
        public const long DefaultMaxInMemoryDataBytes = 16L * 1024L * 1024L;

        private readonly string _mcapPath;
        private readonly string _sourceId;
        private readonly string _manifestName;
        private readonly string _requiredBearerToken;
        private readonly string _dataRoute;
        private readonly long _maxInMemoryDataBytes;
        private readonly object _manifestCacheGate = new object();
        private RemoteMcapManifest _cachedManifest;
        private DateTime _cachedManifestLastWriteUtc;
        private long _cachedManifestLength = -1L;

        public RemoteMcapDataSourcePrototype(
            string mcapPath,
            string sourceId,
            string manifestName,
            string requiredBearerToken,
            long maxInMemoryDataBytes = DefaultMaxInMemoryDataBytes)
        {
            _mcapPath = mcapPath ?? throw new ArgumentNullException(nameof(mcapPath));
            _sourceId = string.IsNullOrEmpty(sourceId) ? "local-mcap" : sourceId;
            _manifestName = string.IsNullOrEmpty(manifestName) ? _sourceId : manifestName;
            _requiredBearerToken = requiredBearerToken ?? string.Empty;
            _dataRoute = "/data?sourceId=" + Uri.EscapeDataString(_sourceId);
            _maxInMemoryDataBytes = maxInMemoryDataBytes;
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

            return new RemoteMcapManifestResponse
            {
                Status = RemoteMcapResponseStatus.Ok,
                Authorization = authorization,
                Manifest = GetCachedManifest()
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

            var info = new FileInfo(_mcapPath);
            if (!info.Exists)
                return DataProblem(RemoteMcapResponseStatus.NotFound, "SourceFileNotFound",
                    "Requested MCAP source file is not available on disk.");

            if (_maxInMemoryDataBytes >= 0 && info.Length > _maxInMemoryDataBytes)
                return DataProblem(RemoteMcapResponseStatus.Unsupported, "DataTooLargeForInMemoryResponse",
                    "Requested MCAP data exceeds the prototype in-memory byte response cap; use GetDataStream.");

            return new RemoteMcapDataResponse
            {
                Status = RemoteMcapResponseStatus.Ok,
                Authorization = authorization,
                SourceId = _sourceId,
                Data = File.ReadAllBytes(_mcapPath)
            };
        }

        public RemoteMcapDataStreamResponse GetDataStream(RemoteMcapRequest request)
        {
            request = request ?? new RemoteMcapRequest();
            if (IsUnsupportedMultiSource(request))
                return DataStreamProblem(RemoteMcapResponseStatus.Unsupported, "UnsupportedMultiSource",
                    "Phase 119 prototype supports one local MCAP source only.");

            var authorization = Authorize(request);
            if (!authorization.Allowed)
            {
                var denied = DataStreamProblem(RemoteMcapResponseStatus.Unauthorized, "Unauthorized",
                    "Data request is not authorized for this MCAP source.");
                denied.Authorization = authorization;
                return denied;
            }

            if (!string.Equals(request.SourceId, _sourceId, StringComparison.Ordinal))
                return DataStreamProblem(RemoteMcapResponseStatus.NotFound, "SourceNotFound",
                    "Requested MCAP source id is not available in this prototype.");

            var info = new FileInfo(_mcapPath);
            if (!info.Exists)
                return DataStreamProblem(RemoteMcapResponseStatus.NotFound, "SourceFileNotFound",
                    "Requested MCAP source file is not available on disk.");

            return new RemoteMcapDataStreamResponse
            {
                Status = RemoteMcapResponseStatus.Ok,
                Authorization = authorization,
                SourceId = _sourceId,
                Length = info.Length,
                DataStream = new FileStream(_mcapPath, FileMode.Open, FileAccess.Read, FileShare.Read)
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

            return ManagedWebSocketOptions.FixedTimeEqualsUtf8(_requiredBearerToken, token)
                ? RemoteMcapAuthorizationResult.Allow()
                : RemoteMcapAuthorizationResult.Deny("Bearer token rejected.");
        }

        private RemoteMcapManifest GetCachedManifest()
        {
            var info = new FileInfo(_mcapPath);
            if (!info.Exists)
            {
                var missing = new RemoteMcapManifest { Name = _manifestName };
                var source = new RemoteMcapSource
                {
                    Id = _sourceId,
                    Name = _manifestName,
                    DataUrl = _dataRoute
                };
                source.Problems.Add(new RemoteMcapProblem(
                    RemoteMcapProblemSeverity.Error,
                    "SourceFileNotFound",
                    "Requested MCAP source file is not available on disk."));
                missing.Sources.Add(source);
                return missing;
            }

            lock (_manifestCacheGate)
            {
                if (_cachedManifest != null
                    && _cachedManifestLength == info.Length
                    && _cachedManifestLastWriteUtc == info.LastWriteTimeUtc)
                {
                    return CloneManifest(_cachedManifest);
                }

                using var loader = new McapDataLoader(_mcapPath);
                var manifest = RemoteMcapManifestMapper.FromInitialization(
                    loader.Initialize(),
                    _manifestName,
                    _sourceId,
                    _dataRoute);
                _cachedManifest = CloneManifest(manifest);
                _cachedManifestLength = info.Length;
                _cachedManifestLastWriteUtc = info.LastWriteTimeUtc;
                return CloneManifest(manifest);
            }
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
            response.Problems.Add(new RemoteMcapProblem(ToProblemSeverity(status), code, message));
            return response;
        }

        private static RemoteMcapDataResponse DataProblem(
            RemoteMcapResponseStatus status,
            string code,
            string message)
        {
            var response = new RemoteMcapDataResponse { Status = status };
            response.Problems.Add(new RemoteMcapProblem(ToProblemSeverity(status), code, message));
            return response;
        }

        private static RemoteMcapDataStreamResponse DataStreamProblem(
            RemoteMcapResponseStatus status,
            string code,
            string message)
        {
            var response = new RemoteMcapDataStreamResponse { Status = status };
            response.Problems.Add(new RemoteMcapProblem(ToProblemSeverity(status), code, message));
            return response;
        }

        private static RemoteMcapProblemSeverity ToProblemSeverity(RemoteMcapResponseStatus status)
        {
            return status == RemoteMcapResponseStatus.Ok
                ? RemoteMcapProblemSeverity.Info
                : status == RemoteMcapResponseStatus.Unsupported
                    ? RemoteMcapProblemSeverity.Warning
                    : RemoteMcapProblemSeverity.Error;
        }

        private static RemoteMcapManifest CloneManifest(RemoteMcapManifest source)
        {
            var clone = new RemoteMcapManifest { Name = source?.Name ?? string.Empty };
            if (source?.Sources == null)
                return clone;

            for (var i = 0; i < source.Sources.Count; i++)
                clone.Sources.Add(CloneSource(source.Sources[i]));
            return clone;
        }

        private static RemoteMcapSource CloneSource(RemoteMcapSource source)
        {
            var clone = new RemoteMcapSource
            {
                Id = source?.Id ?? string.Empty,
                Name = source?.Name ?? string.Empty,
                DataUrl = source?.DataUrl ?? string.Empty,
                HasTimeRange = source?.HasTimeRange ?? false,
                StartTimeNs = source?.StartTimeNs ?? 0UL,
                EndTimeNs = source?.EndTimeNs ?? 0UL
            };

            if (source?.Topics != null)
                for (var i = 0; i < source.Topics.Count; i++)
                    clone.Topics.Add(CloneTopic(source.Topics[i]));
            if (source?.Schemas != null)
                for (var i = 0; i < source.Schemas.Count; i++)
                    clone.Schemas.Add(CloneSchema(source.Schemas[i]));
            if (source?.Problems != null)
                for (var i = 0; i < source.Problems.Count; i++)
                    clone.Problems.Add(CloneProblem(source.Problems[i]));
            return clone;
        }

        private static RemoteMcapTopic CloneTopic(RemoteMcapTopic topic)
        {
            return new RemoteMcapTopic
            {
                ChannelId = topic?.ChannelId ?? 0,
                Name = topic?.Name ?? string.Empty,
                MessageEncoding = topic?.MessageEncoding ?? string.Empty,
                SchemaId = topic?.SchemaId ?? 0
            };
        }

        private static RemoteMcapSchema CloneSchema(RemoteMcapSchema schema)
        {
            return new RemoteMcapSchema
            {
                Id = schema?.Id ?? 0,
                Name = schema?.Name ?? string.Empty,
                Encoding = schema?.Encoding ?? string.Empty,
                DataBase64 = schema?.DataBase64 ?? string.Empty,
                DataLength = schema?.DataLength ?? 0
            };
        }

        private static RemoteMcapProblem CloneProblem(RemoteMcapProblem problem)
        {
            return new RemoteMcapProblem(
                problem?.Severity ?? RemoteMcapProblemSeverity.Info,
                problem?.Code ?? string.Empty,
                problem?.Message ?? string.Empty,
                problem?.Tip ?? string.Empty);
        }
    }
}
