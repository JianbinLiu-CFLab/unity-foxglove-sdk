// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/Remote
// Purpose: Configuration for the embedded Remote Data Loader HTTP backend.

using System;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>Options for serving one local MCAP file through the Remote Data Loader HTTP contract.</summary>
    public sealed class RemoteMcapHttpOptions
    {
        public string Host = "127.0.0.1";
        public int Port;
        public string McapPath = string.Empty;
        public string SourceId = "local-mcap";
        public string ManifestName = "Unity2Foxglove MCAP";
        public string RequiredBearerToken = string.Empty;
        public long MaxInMemoryDataBytes = RemoteMcapDataSourcePrototype.DefaultMaxInMemoryDataBytes;

        /// <summary>Returns the normalized loopback base URL used by <see cref="RemoteMcapHttpServer"/>.</summary>
        public string BaseUrl
        {
            get
            {
                var host = string.IsNullOrEmpty(Host) ? "127.0.0.1" : Host.Trim();
                return "http://" + host + ":" + Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        internal string DataRoute
        {
            get
            {
                return "/v1/data?recordingId=" + Uri.EscapeDataString(
                    string.IsNullOrEmpty(SourceId) ? "local-mcap" : SourceId);
            }
        }
    }
}
