// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/Remote
// Purpose: DTOs for the local prototype remote MCAP data-source boundary.

using System;
using System.Collections.Generic;
using System.IO;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>Prototype response status for local remote-MCAP endpoint modeling.</summary>
    public enum RemoteMcapResponseStatus
    {
        Ok,
        Unauthorized,
        NotFound,
        Unsupported,
        Error
    }

    /// <summary>Authorization decision supplied to the prototype manifest/data endpoints.</summary>
    public sealed class RemoteMcapAuthorizationResult
    {
        public bool Allowed;
        public string Reason;

        public RemoteMcapAuthorizationResult()
        {
            Reason = string.Empty;
        }

        public static RemoteMcapAuthorizationResult Allow()
        {
            return new RemoteMcapAuthorizationResult { Allowed = true };
        }

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
        public string BearerToken;
        public string SourceId;
        public bool RequestMultipleSources;
        public List<string> RequestedSourceIds;

        public RemoteMcapRequest()
        {
            BearerToken = string.Empty;
            SourceId = string.Empty;
            RequestedSourceIds = new List<string>();
        }
    }

    /// <summary>Manifest response returned by the local prototype endpoint model.</summary>
    public sealed class RemoteMcapManifestResponse
    {
        public RemoteMcapResponseStatus Status;
        public RemoteMcapAuthorizationResult Authorization;
        public RemoteMcapManifest Manifest;
        public List<RemoteMcapProblem> Problems;

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
        public RemoteMcapResponseStatus Status;
        public RemoteMcapAuthorizationResult Authorization;
        public string SourceId;
        public byte[] Data;
        public List<RemoteMcapProblem> Problems;

        public RemoteMcapDataResponse()
        {
            Authorization = RemoteMcapAuthorizationResult.Deny(string.Empty);
            SourceId = string.Empty;
            Data = new byte[0];
            Problems = new List<RemoteMcapProblem>();
        }
    }

    /// <summary>Stream response returned by the local prototype data operation for larger MCAP files.</summary>
    public sealed class RemoteMcapDataStreamResponse : IDisposable
    {
        public RemoteMcapResponseStatus Status;
        public RemoteMcapAuthorizationResult Authorization;
        public string SourceId;
        public Stream DataStream;
        public long Length;
        public string ContentType;
        public List<RemoteMcapProblem> Problems;

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
            DataStream?.Dispose();
            DataStream = null;
        }
    }

    /// <summary>Manifest-style description of one or more local MCAP sources.</summary>
    public sealed class RemoteMcapManifest
    {
        public string Name;
        public List<RemoteMcapSource> Sources;

        public RemoteMcapManifest()
        {
            Name = string.Empty;
            Sources = new List<RemoteMcapSource>();
        }
    }

    /// <summary>One MCAP source entry in a prototype remote manifest.</summary>
    public sealed class RemoteMcapSource
    {
        public string Id;
        public string Name;
        public string DataUrl;
        public bool HasTimeRange;
        public ulong StartTimeNs;
        public ulong EndTimeNs;
        public List<RemoteMcapTopic> Topics;
        public List<RemoteMcapSchema> Schemas;
        public List<RemoteMcapProblem> Problems;

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
        public ushort ChannelId;
        public string Name;
        public string MessageEncoding;
        public ushort SchemaId;

        public RemoteMcapTopic()
        {
            Name = string.Empty;
            MessageEncoding = string.Empty;
        }
    }

    /// <summary>Schema metadata mapped from a local MCAP schema record.</summary>
    public sealed class RemoteMcapSchema
    {
        public ushort Id;
        public string Name;
        public string Encoding;
        public string DataBase64;
        public int DataLength;

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
        public string Severity;
        public string Code;
        public string Message;
        public string Tip;

        public RemoteMcapProblem()
        {
            Severity = string.Empty;
            Code = string.Empty;
            Message = string.Empty;
            Tip = string.Empty;
        }

        public RemoteMcapProblem(string severity, string code, string message, string tip = "")
        {
            Severity = severity ?? string.Empty;
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
            Tip = tip ?? string.Empty;
        }
    }
}
