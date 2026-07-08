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
        /// <summary>
        /// Host used by the embedded HTTP listener. The default loopback host needs no
        /// extra setup; a non-loopback host may require a Windows http.sys URL ACL.
        /// </summary>
        public string Host = "127.0.0.1";

        /// <summary>
        /// TCP port used by the embedded HTTP listener. Must be set to a value in
        /// [1, 65535] before starting the server; there is no safe default because
        /// callers must choose a port that matches their local workflow.
        /// </summary>
        public int Port;

        /// <summary>Absolute or caller-resolved path to the MCAP file served by this backend.</summary>
        public string McapPath = string.Empty;

        /// <summary>Stable source id advertised in the manifest and accepted as recordingId/sourceId.</summary>
        public string SourceId = "local-mcap";

        /// <summary>Display name advertised in the Remote Data Loader manifest.</summary>
        public string ManifestName = "Unity2Foxglove MCAP";

        /// <summary>
        /// Optional bearer token required for manifest and data requests. Leave empty
        /// only for trusted local workflows because wildcard CORS lets browser origins
        /// read the served loopback MCAP while the endpoint is enabled.
        /// </summary>
        public string RequiredBearerToken = string.Empty;

        /// <summary>Maximum MCAP response size buffered in memory before the request is rejected.</summary>
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

        /// <summary>Relative data route advertised by the manifest for this source.</summary>
        internal string DataRoute
        {
            get
            {
                return "/v1/data?recordingId=" + Uri.EscapeDataString(
                    string.IsNullOrEmpty(SourceId) ? "local-mcap" : SourceId);
            }
        }

        /// <summary>Relative direct-file route accepted by Foxglove's stock Remote files dialog.</summary>
        internal string DirectFileRoute
        {
            get
            {
                return "/v1/files/" + Uri.EscapeDataString(
                    string.IsNullOrEmpty(SourceId) ? "local-mcap" : SourceId) + ".mcap";
            }
        }
    }
}
