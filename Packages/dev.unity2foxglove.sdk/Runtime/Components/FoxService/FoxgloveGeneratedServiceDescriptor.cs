// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxService
// Purpose: Runtime descriptor for generated declarative Foxglove services.

using System;
using Newtonsoft.Json.Linq;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Descriptor emitted by generated <c>[FoxService]</c> wrappers.
    /// </summary>
    public sealed class FoxgloveGeneratedServiceDescriptor
    {
        public FoxgloveGeneratedServiceDescriptor(
            string name,
            string type,
            string description,
            string requestSchemaName,
            string responseSchemaName,
            Func<JToken, JToken> handler)
            : this(name, type, description, requestSchemaName, responseSchemaName, string.Empty, string.Empty, handler)
        {
        }

        public FoxgloveGeneratedServiceDescriptor(
            string name,
            string type,
            string description,
            string requestSchemaName,
            string responseSchemaName,
            string requestSchema,
            string responseSchema,
            Func<JToken, JToken> handler)
        {
            Name = name ?? string.Empty;
            Type = type ?? string.Empty;
            Description = description ?? string.Empty;
            RequestSchemaName = requestSchemaName ?? string.Empty;
            ResponseSchemaName = responseSchemaName ?? string.Empty;
            RequestSchema = requestSchema ?? string.Empty;
            ResponseSchema = responseSchema ?? string.Empty;
            Handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public string Name { get; }
        public string Type { get; }
        public string Description { get; }
        public string RequestSchemaName { get; }
        public string ResponseSchemaName { get; }
        public string RequestSchema { get; }
        public string ResponseSchema { get; }

        /// <summary>
        /// Generated service handler. Implementations must return a non-null
        /// response token; thrown exceptions are converted by the service dispatch
        /// layer into structured service failures.
        /// </summary>
        public Func<JToken, JToken> Handler { get; }
    }
}
