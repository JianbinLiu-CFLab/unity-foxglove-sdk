// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: ChannelRegistry descriptor ownership and snapshot immutability regressions.

using System;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Protocol;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Core
{
    [Trait("Phase", "187")]
    [Trait("Domain", "Registry")]
    public sealed class ChannelRegistryOwnershipTests
    {
        [Fact]
        public void RegisterCapturesDescriptorInsteadOfRetainingCallerObject()
        {
            var registry = new ChannelRegistry();
            var original = Channel(7, "/phase187/original", "schema-original");

            registry.Register(original);
            original.Id = 99;
            original.Topic = "/phase187/mutated";
            original.Encoding = "mutated";
            original.SchemaName = "Mutated.Schema";
            original.SchemaEncoding = "mutated-schema";
            original.Schema = "schema-mutated";

            var registered = registry.Get(7);
            Assert.NotNull(registered);
            Assert.NotSame(original, registered);
            Assert.Equal(7U, registered.Id);
            Assert.Equal("/phase187/original", registered.Topic);
            Assert.Equal("json", registered.Encoding);
            Assert.Equal("Phase187.Original", registered.SchemaName);
            Assert.Equal("jsonschema", registered.SchemaEncoding);
            Assert.Equal("schema-original", registered.Schema);
            Assert.Null(registry.Get(99));
        }

        [Fact]
        public void GetAndGetAllCannotMutateRegisteredDescriptor()
        {
            var registry = new ChannelRegistry();
            registry.Register(Channel(7, "/phase187/original", "schema-original"));

            TryMutate(() => registry.Get(7).Topic = "/phase187/direct-mutation");
            TryMutate(() => registry.GetAll()[0].Schema = "list-mutation");

            var registered = registry.Get(7);
            Assert.Equal(7U, registered.Id);
            Assert.Equal("/phase187/original", registered.Topic);
            Assert.Equal("schema-original", registered.Schema);
        }

        private static void TryMutate(Action mutation)
        {
            try
            {
                mutation();
            }
            catch (InvalidOperationException)
            {
                // Immutable registry snapshots may reject mutation directly.
            }
        }

        private static AdvertiseChannel Channel(uint id, string topic, string schema)
        {
            return new AdvertiseChannel
            {
                Id = id,
                Topic = topic,
                Encoding = "json",
                SchemaName = "Phase187.Original",
                SchemaEncoding = "jsonschema",
                Schema = schema
            };
        }
    }
}
