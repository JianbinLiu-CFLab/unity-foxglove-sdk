// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/Remote
// Purpose: Local-only prototype endpoint model for remote MCAP manifest/data behavior.

using System;
using System.IO;
using System.Text;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>Local-file prototype for Remote Data Loader style manifest and data operations.</summary>
    public sealed class RemoteMcapDataSourcePrototype
    {
        /// <summary>Default cap for data responses buffered fully in memory.</summary>
        public const long DefaultMaxInMemoryDataBytes = 16L * 1024L * 1024L;

        private readonly string _mcapPath;
        private readonly string _sourceId;
        private readonly string _manifestName;
        private readonly string _requiredBearerToken;
        private readonly byte[] _requiredBearerTokenBytes;
        private readonly string _dataRoute;
        private readonly string _directFileRoute;
        private readonly long _maxInMemoryDataBytes;
        private readonly object _manifestCacheGate = new object();
        private RemoteMcapManifest _cachedManifest;
        private byte[] _cachedManifestBytes;
        private DateTime _cachedManifestLastWriteUtc;
        private long _cachedManifestLength = -1L;

        private struct FileStamp
        {
            public bool Exists;
            public long Length;
            public DateTime LastWriteUtc;
        }

        /// <summary>Creates a single-file Remote Data Loader prototype around one local MCAP path.</summary>
        public RemoteMcapDataSourcePrototype(
            string mcapPath,
            string sourceId,
            string manifestName,
            string requiredBearerToken,
            long maxInMemoryDataBytes = DefaultMaxInMemoryDataBytes,
            string dataRoute = null,
            string directFileRoute = null)
        {
            _mcapPath = mcapPath ?? throw new ArgumentNullException(nameof(mcapPath));
            _sourceId = string.IsNullOrEmpty(sourceId) ? "local-mcap" : sourceId;
            _manifestName = string.IsNullOrEmpty(manifestName) ? _sourceId : manifestName;
            _requiredBearerToken = requiredBearerToken ?? string.Empty;
            _requiredBearerTokenBytes = string.IsNullOrEmpty(_requiredBearerToken)
                ? Array.Empty<byte>()
                : Encoding.UTF8.GetBytes(_requiredBearerToken);
            _dataRoute = string.IsNullOrEmpty(dataRoute)
                ? "/data?sourceId=" + Uri.EscapeDataString(_sourceId)
                : dataRoute;
            _directFileRoute = string.IsNullOrEmpty(directFileRoute)
                ? "/v1/files/" + Uri.EscapeDataString(_sourceId) + ".mcap"
                : directFileRoute;
            _maxInMemoryDataBytes = maxInMemoryDataBytes;
        }

        /// <summary>Relative direct-file route accepted by Foxglove's stock Remote files dialog.</summary>
        public string DirectFileRoute => _directFileRoute;

        /// <summary>Returns manifest metadata for the configured MCAP file.</summary>
        public RemoteMcapManifestResponse GetManifest(RemoteMcapRequest request)
        {
            request = request ?? new RemoteMcapRequest();
            var authorization = Authorize(request);
            if (!authorization.Allowed)
            {
                var denied = ManifestProblem(RemoteMcapResponseStatus.Unauthorized, "Unauthorized",
                    "Manifest request is not authorized for this MCAP source.");
                denied.Authorization = authorization;
                return denied;
            }

            if (IsUnsupportedMultiSource(request))
                return ManifestProblem(RemoteMcapResponseStatus.Unsupported, "UnsupportedMultiSource",
                    "Phase 119 prototype supports one local MCAP source only.");

            return new RemoteMcapManifestResponse
            {
                Status = RemoteMcapResponseStatus.Ok,
                Authorization = authorization,
                Manifest = GetCachedManifest()
            };
        }

        internal byte[] GetManifestBytes(RemoteMcapRequest request, out RemoteMcapManifestResponse error)
        {
            request = request ?? new RemoteMcapRequest();
            var authorization = Authorize(request);
            if (!authorization.Allowed)
            {
                error = ManifestProblem(RemoteMcapResponseStatus.Unauthorized, "Unauthorized",
                    "Manifest request is not authorized for this MCAP source.");
                error.Authorization = authorization;
                return Array.Empty<byte>();
            }

            if (IsUnsupportedMultiSource(request))
            {
                error = ManifestProblem(RemoteMcapResponseStatus.Unsupported, "UnsupportedMultiSource",
                    "Phase 119 prototype supports one local MCAP source only.");
                return Array.Empty<byte>();
            }

            error = null;
            return GetCachedManifestBytes();
        }

        /// <summary>Returns the complete MCAP file as bytes when it is within the configured memory cap.</summary>
        public RemoteMcapDataResponse GetData(RemoteMcapRequest request)
        {
            request = request ?? new RemoteMcapRequest();
            var authorization = Authorize(request);
            if (!authorization.Allowed)
            {
                var denied = DataProblem(RemoteMcapResponseStatus.Unauthorized, "Unauthorized",
                    "Data request is not authorized for this MCAP source.");
                denied.Authorization = authorization;
                return denied;
            }

            if (IsUnsupportedMultiSource(request))
                return DataProblem(RemoteMcapResponseStatus.Unsupported, "UnsupportedMultiSource",
                    "Phase 119 prototype supports one local MCAP source only.");

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

            byte[] data;
            try
            {
                data = ReadAllBytesWithinCap(_mcapPath, _maxInMemoryDataBytes);
            }
            catch (RemoteMcapRangeTooLargeException)
            {
                return DataProblem(RemoteMcapResponseStatus.Unsupported, "DataTooLargeForInMemoryResponse",
                    "Requested MCAP data exceeds the prototype in-memory byte response cap; use GetDataStream.");
            }

            return new RemoteMcapDataResponse
            {
                Status = RemoteMcapResponseStatus.Ok,
                Authorization = authorization,
                SourceId = _sourceId,
                Data = data
            };
        }

        /// <summary>Returns an owned MCAP stream for the requested inclusive log-time range.</summary>
        public RemoteMcapDataStreamResponse GetDataStream(RemoteMcapRequest request)
        {
            request = request ?? new RemoteMcapRequest();
            var authorization = Authorize(request);
            if (!authorization.Allowed)
            {
                var denied = DataStreamProblem(RemoteMcapResponseStatus.Unauthorized, "Unauthorized",
                    "Data request is not authorized for this MCAP source.");
                denied.Authorization = authorization;
                return denied;
            }

            if (IsUnsupportedMultiSource(request))
                return DataStreamProblem(RemoteMcapResponseStatus.Unsupported, "UnsupportedMultiSource",
                    "Phase 119 prototype supports one local MCAP source only.");

            if (!string.Equals(request.SourceId, _sourceId, StringComparison.Ordinal))
                return DataStreamProblem(RemoteMcapResponseStatus.NotFound, "SourceNotFound",
                    "Requested MCAP source id is not available in this prototype.");

            var info = new FileInfo(_mcapPath);
            if (!info.Exists)
                return DataStreamProblem(RemoteMcapResponseStatus.NotFound, "SourceFileNotFound",
                    "Requested MCAP source file is not available on disk.");

            MemoryStream slice;
            try
            {
                slice = RemoteMcapRangeWriter.CreateSlice(_mcapPath, request, _maxInMemoryDataBytes);
            }
            catch (RemoteMcapRangeTooLargeException ex)
            {
                return DataStreamProblem(RemoteMcapResponseStatus.Unsupported, "DataTooLargeForInMemoryResponse",
                    ex.Message);
            }
            catch (Exception ex)
            {
                return DataStreamProblem(RemoteMcapResponseStatus.Error, "RangeSliceFailed",
                    "Requested MCAP range could not be re-emitted: " + ex.Message);
            }

            if (_maxInMemoryDataBytes >= 0 && slice.Length > _maxInMemoryDataBytes)
            {
                slice.Dispose();
                return DataStreamProblem(RemoteMcapResponseStatus.Unsupported, "DataTooLargeForInMemoryResponse",
                    "Requested MCAP range exceeds the configured in-memory byte response cap.");
            }

            return new RemoteMcapDataStreamResponse
            {
                Status = RemoteMcapResponseStatus.Ok,
                Authorization = authorization,
                SourceId = _sourceId,
                Length = slice.Length,
                DataStream = slice
            };
        }

        /// <summary>Opens the configured MCAP file for direct byte-range HTTP reads.</summary>
        public RemoteMcapDataStreamResponse GetDirectFileStream(RemoteMcapRequest request)
        {
            request = request ?? new RemoteMcapRequest();
            var authorization = Authorize(request);
            if (!authorization.Allowed)
            {
                var denied = DataStreamProblem(RemoteMcapResponseStatus.Unauthorized, "Unauthorized",
                    "Direct file request is not authorized for this MCAP source.");
                denied.Authorization = authorization;
                return denied;
            }

            if (IsUnsupportedMultiSource(request))
                return DataStreamProblem(RemoteMcapResponseStatus.Unsupported, "UnsupportedMultiSource",
                    "Phase 119 prototype supports one local MCAP source only.");

            if (!string.IsNullOrEmpty(request.SourceId)
                && !string.Equals(request.SourceId, _sourceId, StringComparison.Ordinal))
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
                ContentType = "application/octet-stream",
                DataStream = new FileStream(
                    _mcapPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete)
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

            return ManagedWebSocketOptions.FixedTimeEqualsUtf8(_requiredBearerTokenBytes, token)
                ? RemoteMcapAuthorizationResult.Allow()
                : RemoteMcapAuthorizationResult.Deny("Bearer token rejected.");
        }

        private RemoteMcapManifest GetCachedManifest()
        {
            return CloneManifest(GetCachedManifestCore(ReadFileStamp(), out _));
        }

        private RemoteMcapManifest GetCachedManifestCore(FileStamp loadStamp, out FileStamp storeStamp)
        {
            storeStamp = loadStamp;
            if (!loadStamp.Exists)
                return CreateMissingManifest();

            lock (_manifestCacheGate)
            {
                if (_cachedManifest != null
                    && MatchesCachedStamp(loadStamp))
                {
                    return _cachedManifest;
                }
            }

            RemoteMcapManifest manifest;
            try
            {
                using var loader = new McapDataLoader(_mcapPath);
                manifest = RemoteMcapManifestMapper.FromInitialization(
                    loader.Initialize(),
                    _manifestName,
                    _sourceId,
                    _dataRoute);
            }
            catch (IOException)
            {
                return CreateMissingManifest();
            }

            storeStamp = ReadFileStamp();
            if (!storeStamp.Exists)
                return CreateMissingManifest();
            if (!SameStamp(loadStamp, storeStamp))
                return manifest;

            lock (_manifestCacheGate)
            {
                if (_cachedManifest != null
                    && MatchesCachedStamp(loadStamp))
                {
                    return _cachedManifest;
                }

                _cachedManifest = manifest;
                _cachedManifestBytes = null;
                _cachedManifestLength = loadStamp.Length;
                _cachedManifestLastWriteUtc = loadStamp.LastWriteUtc;
                return _cachedManifest;
            }
        }

        private byte[] GetCachedManifestBytes()
        {
            var stamp = ReadFileStamp();
            lock (_manifestCacheGate)
            {
                if (_cachedManifestBytes != null
                    && MatchesCachedStamp(stamp))
                {
                    return _cachedManifestBytes;
                }
            }

            var manifest = GetCachedManifestCore(stamp, out var storeStamp);
            var bytes = Encoding.UTF8.GetBytes(RemoteMcapOfficialManifestSerializer.Serialize(manifest));

            lock (_manifestCacheGate)
            {
                if (_cachedManifestBytes != null
                    && MatchesCachedStamp(storeStamp))
                {
                    return _cachedManifestBytes;
                }

                if (!storeStamp.Equals(stamp))
                    return bytes;

                _cachedManifestBytes = bytes;
                _cachedManifestLength = storeStamp.Length;
                _cachedManifestLastWriteUtc = storeStamp.LastWriteUtc;
                return _cachedManifestBytes;
            }
        }

        private FileStamp ReadFileStamp()
        {
            var info = new FileInfo(_mcapPath);
            return new FileStamp
            {
                Exists = info.Exists,
                Length = info.Exists ? info.Length : 0L,
                LastWriteUtc = info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue
            };
        }

        private bool MatchesCachedStamp(FileStamp stamp)
            => _cachedManifestLength == stamp.Length && _cachedManifestLastWriteUtc == stamp.LastWriteUtc;

        private static bool SameStamp(FileStamp left, FileStamp right)
            => left.Exists == right.Exists
               && left.Length == right.Length
               && left.LastWriteUtc == right.LastWriteUtc;

        private static byte[] ReadAllBytesWithinCap(string path, long maxBytes)
        {
            if (maxBytes < 0)
                return File.ReadAllBytes(path);

            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var output = new MemoryStream())
            {
                var buffer = new byte[81920];
                long total = 0;
                while (true)
                {
                    var read = input.Read(buffer, 0, buffer.Length);
                    if (read <= 0)
                        break;

                    total += read;
                    if (total > maxBytes)
                        throw new RemoteMcapRangeTooLargeException(
                            "Requested MCAP data exceeds the prototype in-memory byte response cap; use GetDataStream.");
                    output.Write(buffer, 0, read);
                }

                return output.ToArray();
            }
        }

        private RemoteMcapManifest CreateMissingManifest()
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
