// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxServiceSchema
// Purpose: Small schema model for generated FoxService request/response previews.

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Editor
{
    public sealed class FoxServiceSchemaModel
    {
        private FoxServiceSchemaModel(
            string jsonType,
            IReadOnlyList<FoxServiceSchemaProperty> properties,
            FoxServiceSchemaModel element,
            FoxServiceSchemaModel additionalProperties)
        {
            JsonType = jsonType ?? "object";
            Properties = properties ?? Array.Empty<FoxServiceSchemaProperty>();
            Element = element;
            AdditionalProperties = additionalProperties;
        }

        public string JsonType { get; }
        public IReadOnlyList<FoxServiceSchemaProperty> Properties { get; }
        public FoxServiceSchemaModel Element { get; }
        public FoxServiceSchemaModel AdditionalProperties { get; }

        public static FoxServiceSchemaModel Scalar(string jsonType)
        {
            if (string.IsNullOrWhiteSpace(jsonType))
                throw new ArgumentException("FoxServiceSchemaModel.JsonType must be non-empty.", nameof(jsonType));

            return new FoxServiceSchemaModel(jsonType, Array.Empty<FoxServiceSchemaProperty>(), null, null);
        }

        public static FoxServiceSchemaModel Object(IReadOnlyList<FoxServiceSchemaProperty> properties)
            => new FoxServiceSchemaModel("object", properties, null, null);

        public static FoxServiceSchemaModel ArrayOf(FoxServiceSchemaModel element)
            => new FoxServiceSchemaModel("array", Array.Empty<FoxServiceSchemaProperty>(), element, null);

        public static FoxServiceSchemaModel Dictionary(FoxServiceSchemaModel value)
            => new FoxServiceSchemaModel("object", Array.Empty<FoxServiceSchemaProperty>(), null, value);
    }

    public sealed class FoxServiceSchemaProperty
    {
        public FoxServiceSchemaProperty(string name, FoxServiceSchemaModel schema)
        {
            Name = name ?? string.Empty;
            Schema = schema ?? FoxServiceSchemaModel.Object(Array.Empty<FoxServiceSchemaProperty>());
        }

        public string Name { get; }
        public FoxServiceSchemaModel Schema { get; }
    }
}
