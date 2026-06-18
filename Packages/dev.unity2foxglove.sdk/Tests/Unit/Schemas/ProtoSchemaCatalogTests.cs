// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Protobuf schema catalog lookup behavior.

using System;
using System.Linq;
using Foxglove.Schemas;
using Unity.FoxgloveSDK.Schemas;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{
    [Trait("Phase", "150")]
    [Trait("Domain", "Schemas")]
    public sealed class ProtoSchemaCatalogTests
    {
        [Fact]
        public void TryGetByClrTypeResolvesBundledFoxgloveMessage()
        {
            Assert.True(FoxgloveProtoSchemaCatalog.TryGetByClrType(typeof(Foxglove.SceneUpdate), out var entry));
            Assert.Equal(FoxgloveSchemaDefinitions.SceneUpdateSchemaName, entry.SchemaName);
        }

        [Fact]
        public void TryGetByClrTypeTreatsNullAndUnknownTypesAsMisses()
        {
            Assert.False(FoxgloveProtoSchemaCatalog.TryGetByClrType(null, out var nullEntry));
            Assert.Null(nullEntry);

            Assert.False(FoxgloveProtoSchemaCatalog.TryGetByClrType(typeof(string), out var unknownEntry));
            Assert.Null(unknownEntry);
        }

        [Fact]
        public void CatalogClrTypesAreUnique()
        {
            var duplicate = FoxgloveProtoSchemaCatalog.Entries
                .GroupBy(entry => entry.ClrType)
                .FirstOrDefault(group => group.Count() > 1);

            Assert.Null(duplicate);
        }
    }
}
