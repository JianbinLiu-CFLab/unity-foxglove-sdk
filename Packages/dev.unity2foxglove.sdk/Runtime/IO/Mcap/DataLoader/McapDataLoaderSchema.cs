// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/DataLoader
// Purpose: Schema DTO exposed by the local MCAP DataLoader facade.

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>Schema summary and raw schema payload exposed by local MCAP initialization.</summary>
    public sealed class McapDataLoaderSchema
    {
        /// <summary>MCAP schema ID.</summary>
        public ushort SchemaId;

        /// <summary>Schema name recorded in the MCAP file.</summary>
        public string Name;

        /// <summary>Schema encoding recorded in the MCAP file.</summary>
        public string Encoding;

        /// <summary>Raw serialized schema payload bytes.</summary>
        public byte[] Data;

        /// <summary>Creates an empty schema summary.</summary>
        public McapDataLoaderSchema()
        {
            Name = string.Empty;
            Encoding = string.Empty;
            Data = new byte[0];
        }
    }
}
