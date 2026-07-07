// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/Remote
// Purpose: DTOs for the local prototype remote MCAP data-source boundary.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>Prototype response status for local remote-MCAP endpoint modeling.</summary>
    public enum RemoteMcapResponseStatus
    {
        /// <summary>The request completed successfully.</summary>
        Ok,

        /// <summary>The request lacked the configured bearer token.</summary>
        Unauthorized,

        /// <summary>The requested source or local file was not available.</summary>
        NotFound,

        /// <summary>The request shape is outside the current local prototype scope.</summary>
        Unsupported,

        /// <summary>The request failed while reading or re-emitting MCAP data.</summary>
        Error
    }

    /// <summary>Severity assigned to a manifest or data-source problem.</summary>
    public enum RemoteMcapProblemSeverity
    {
        /// <summary>Informational note that does not prevent data access.</summary>
        Info,

        /// <summary>Recoverable limitation that callers may be able to work around.</summary>
        Warning,

        /// <summary>Blocking problem that prevents the requested response.</summary>
        Error
    }

    /// <summary>Authorization decision supplied to the prototype manifest/data endpoints.</summary>
    public sealed class RemoteMcapAuthorizationResult
    {
        /// <summary>True when the request is allowed to continue.</summary>
        public bool Allowed;

        /// <summary>Human-readable denial reason, or empty when allowed.</summary>
        public string Reason;

        /// <summary>Creates a denied authorization result with an empty reason.</summary>
        public RemoteMcapAuthorizationResult()
        {
            Reason = string.Empty;
        }

        /// <summary>Creates an authorization result that allows the request.</summary>
        public static RemoteMcapAuthorizationResult Allow()
        {
            return new RemoteMcapAuthorizationResult { Allowed = true };
        }

        /// <summary>Creates an authorization result that rejects the request.</summary>
        public static RemoteMcapAuthorizationResult Deny(string reason)
        {
            return new RemoteMcapAuthorizationResult
            {
                Allowed = false,
                Reason = reason ?? string.Empty
            };
        }
    }

    /// <summary>Request shape for the local prototype manifest/data operations.</summary>
    public sealed class RemoteMcapRequest
    {
        /// <summary>Raw Authorization header value supplied by the HTTP request.</summary>
        public string BearerToken;

        /// <summary>Requested source id, usually from recordingId or sourceId query parameters.</summary>
        public string SourceId;

        /// <summary>Inclusive lower log-time bound in Unix nanoseconds.</summary>
        public ulong StartTimeNs;

        /// <summary>Inclusive upper log-time bound in Unix nanoseconds.</summary>
        public ulong EndTimeNs;

        /// <summary>True when the caller requested a multi-source response.</summary>
        public bool RequestMultipleSources;

        /// <summary>Explicit source ids requested by a future multi-source contract.</summary>
        public List<string> RequestedSourceIds;

        /// <summary>Creates an unrestricted single-source request.</summary>
        public RemoteMcapRequest()
        {
            BearerToken = string.Empty;
            SourceId = string.Empty;
            EndTimeNs = ulong.MaxValue;
            RequestedSourceIds = new List<string>();
        }
    }

    /// <summary>Manifest response returned by the local prototype endpoint model.</summary>
    public sealed class RemoteMcapManifestResponse
    {
        /// <summary>Prototype status for the manifest operation.</summary>
        public RemoteMcapResponseStatus Status;

        /// <summary>Authorization decision applied to the manifest request.</summary>
        public RemoteMcapAuthorizationResult Authorization;

        /// <summary>Manifest payload returned when the operation succeeds.</summary>
        public RemoteMcapManifest Manifest;

        /// <summary>Problems collected while producing the manifest.</summary>
        public List<RemoteMcapProblem> Problems;

        /// <summary>Creates an empty denied manifest response.</summary>
        public RemoteMcapManifestResponse()
        {
            Authorization = RemoteMcapAuthorizationResult.Deny(string.Empty);
            Manifest = new RemoteMcapManifest();
            Problems = new List<RemoteMcapProblem>();
        }
    }

    /// <summary>Data response returned by the local prototype endpoint model.</summary>
    public sealed class RemoteMcapDataResponse
    {
        /// <summary>Prototype status for the in-memory data operation.</summary>
        public RemoteMcapResponseStatus Status;

        /// <summary>Authorization decision applied to the data request.</summary>
        public RemoteMcapAuthorizationResult Authorization;

        /// <summary>Source id that produced the MCAP data.</summary>
        public string SourceId;

        /// <summary>Complete MCAP bytes returned by the in-memory operation.</summary>
        public byte[] Data;

        /// <summary>Problems collected while producing the data response.</summary>
        public List<RemoteMcapProblem> Problems;

        /// <summary>Creates an empty denied data response.</summary>
        public RemoteMcapDataResponse()
        {
            Authorization = RemoteMcapAuthorizationResult.Deny(string.Empty);
            SourceId = string.Empty;
            Data = Array.Empty<byte>();
            Problems = new List<RemoteMcapProblem>();
        }
    }

    /// <summary>Stream response returned by the local prototype data operation for larger MCAP files.</summary>
    public sealed class RemoteMcapDataStreamResponse : IDisposable
    {
        /// <summary>Prototype status for the streaming data operation.</summary>
        public RemoteMcapResponseStatus Status;

        /// <summary>Authorization decision applied to the streaming data request.</summary>
        public RemoteMcapAuthorizationResult Authorization;

        /// <summary>Source id that produced the MCAP stream.</summary>
        public string SourceId;

        /// <summary>Owned MCAP stream returned to the HTTP router.</summary>
        public Stream DataStream;

        /// <summary>Length of <see cref="DataStream"/> in bytes, or negative if unknown.</summary>
        public long Length;

        /// <summary>HTTP content type used for the data stream.</summary>
        public string ContentType;

        /// <summary>Problems collected while producing the stream response.</summary>
        public List<RemoteMcapProblem> Problems;

        /// <summary>Creates an empty denied stream response.</summary>
        public RemoteMcapDataStreamResponse()
        {
            Authorization = RemoteMcapAuthorizationResult.Deny(string.Empty);
            SourceId = string.Empty;
            ContentType = "application/octet-stream";
            Problems = new List<RemoteMcapProblem>();
        }

        /// <summary>Closes the owned response stream, if one was opened.</summary>
        public void Dispose()
        {
            Interlocked.Exchange(ref DataStream, null)?.Dispose();
        }
    }

    /// <summary>Manifest-style description of one or more local MCAP sources.</summary>
    public sealed class RemoteMcapManifest
    {
        /// <summary>Optional display name for the manifest.</summary>
        public string Name;

        /// <summary>Sources advertised to the Remote Data Loader client.</summary>
        public List<RemoteMcapSource> Sources;

        /// <summary>Creates an empty manifest.</summary>
        public RemoteMcapManifest()
        {
            Name = string.Empty;
            Sources = new List<RemoteMcapSource>();
        }
    }

    /// <summary>One MCAP source entry in a prototype remote manifest.</summary>
    public sealed class RemoteMcapSource
    {
        /// <summary>Stable source id used for cache keys and data requests.</summary>
        public string Id;

        /// <summary>Human-readable source name.</summary>
        public string Name;

        /// <summary>Relative or absolute URL used to request MCAP data for this source.</summary>
        public string DataUrl;

        /// <summary>True when the source has a finite log-time range.</summary>
        public bool HasTimeRange;

        /// <summary>First source log time in Unix nanoseconds.</summary>
        public ulong StartTimeNs;

        /// <summary>Last source log time in Unix nanoseconds.</summary>
        public ulong EndTimeNs;

        /// <summary>Topics advertised for this source.</summary>
        public List<RemoteMcapTopic> Topics;

        /// <summary>Schemas advertised for this source.</summary>
        public List<RemoteMcapSchema> Schemas;

        /// <summary>Problems associated with this source.</summary>
        public List<RemoteMcapProblem> Problems;

        /// <summary>Creates an empty source entry.</summary>
        public RemoteMcapSource()
        {
            Id = string.Empty;
            Name = string.Empty;
            DataUrl = string.Empty;
            Topics = new List<RemoteMcapTopic>();
            Schemas = new List<RemoteMcapSchema>();
            Problems = new List<RemoteMcapProblem>();
        }
    }

    /// <summary>Topic metadata mapped from a local MCAP channel.</summary>
    public sealed class RemoteMcapTopic
    {
        /// <summary>Original MCAP channel id.</summary>
        public ushort ChannelId;

        /// <summary>Topic name advertised in the manifest.</summary>
        public string Name;

        /// <summary>Message encoding advertised for this topic.</summary>
        public string MessageEncoding;

        /// <summary>Schema id referenced by this topic, or zero when schema-less.</summary>
        public ushort SchemaId;

        /// <summary>Creates an empty topic entry.</summary>
        public RemoteMcapTopic()
        {
            Name = string.Empty;
            MessageEncoding = string.Empty;
        }
    }

    /// <summary>Schema metadata mapped from a local MCAP schema record.</summary>
    public sealed class RemoteMcapSchema
    {
        /// <summary>Original MCAP schema id.</summary>
        public ushort Id;

        /// <summary>Schema name advertised in the manifest.</summary>
        public string Name;

        /// <summary>Schema encoding advertised in the manifest.</summary>
        public string Encoding;

        /// <summary>Base64-encoded schema data.</summary>
        public string DataBase64;

        /// <summary>Original decoded schema data length in bytes.</summary>
        public int DataLength;

        /// <summary>Creates an empty schema entry.</summary>
        public RemoteMcapSchema()
        {
            Name = string.Empty;
            Encoding = string.Empty;
            DataBase64 = string.Empty;
        }
    }

    /// <summary>Boundary-level problem surfaced by manifest or data operations.</summary>
    public sealed class RemoteMcapProblem
    {
        /// <summary>Problem severity.</summary>
        public RemoteMcapProblemSeverity Severity;

        /// <summary>Stable problem code for tests and callers.</summary>
        public string Code;

        /// <summary>Human-readable problem message.</summary>
        public string Message;

        /// <summary>Optional remediation hint.</summary>
        public string Tip;

        /// <summary>Creates an informational empty problem.</summary>
        public RemoteMcapProblem()
        {
            Severity = RemoteMcapProblemSeverity.Info;
            Code = string.Empty;
            Message = string.Empty;
            Tip = string.Empty;
        }

        /// <summary>Creates a problem with a severity, stable code, message, and optional hint.</summary>
        public RemoteMcapProblem(RemoteMcapProblemSeverity severity, string code, string message, string tip = "")
        {
            Severity = severity;
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
            Tip = tip ?? string.Empty;
        }
    }
}
