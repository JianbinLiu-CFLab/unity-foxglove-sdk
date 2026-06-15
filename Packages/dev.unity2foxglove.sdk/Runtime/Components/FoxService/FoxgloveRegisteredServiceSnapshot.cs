// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxService
// Purpose: Read-only Inspector snapshot for generated FoxService registrations.

namespace Unity.FoxgloveSDK.Components
{
    public readonly struct FoxgloveRegisteredServiceSnapshot
    {
        public FoxgloveRegisteredServiceSnapshot(
            uint serviceId,
            string name,
            string type,
            string requestSchemaName,
            string responseSchemaName,
            string source)
        {
            ServiceId = serviceId;
            Name = name ?? string.Empty;
            Type = type ?? string.Empty;
            RequestSchemaName = requestSchemaName ?? string.Empty;
            ResponseSchemaName = responseSchemaName ?? string.Empty;
            Source = source ?? string.Empty;
        }

        public uint ServiceId { get; }
        public string Name { get; }
        public string Type { get; }
        public string RequestSchemaName { get; }
        public string ResponseSchemaName { get; }
        public string Source { get; }
        public bool IsRegistered => ServiceId != 0;
    }
}
