// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using Unity.FoxgloveSDK.Components;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed class FoxRunWireEncodingResolverTests
    {
        [Fact]
        public void InheritResolvesToTheManagerProtobufDefault()
        {
            Assert.Equal(
                FoxRunWireEncoding.Protobuf,
                FoxRunWireEncodingResolver.Resolve(FoxRunWireEncoding.Inherit, FoxRunWireEncoding.Protobuf));
        }

        [Fact]
        public void ExplicitJsonWinsOverTheManagerDefault()
        {
            Assert.Equal(
                FoxRunWireEncoding.Json,
                FoxRunWireEncodingResolver.Resolve(FoxRunWireEncoding.Json, FoxRunWireEncoding.Protobuf));
        }

        [Fact]
        public void ManagerDefaultRejectsSourceOnlyInheritState()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                FoxRunWireEncodingResolver.ValidateManagerDefault(FoxRunWireEncoding.Inherit));
        }
    }
}
