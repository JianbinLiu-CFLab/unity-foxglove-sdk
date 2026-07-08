// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Unity.FoxgloveSDK.Core;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Replay
{
    public sealed class ReplayPropertyCacheTests
    {
        [Fact]
        public void ResolveReturnsSamePropertyInfoForRepeatedKey()
        {
            var first = ReplayPropertyCache.Resolve(
                typeof(PropertyFixture),
                nameof(PropertyFixture.Value),
                BindingFlags.Public | BindingFlags.Instance);
            var second = ReplayPropertyCache.Resolve(
                typeof(PropertyFixture),
                nameof(PropertyFixture.Value),
                BindingFlags.Public | BindingFlags.Instance);

            Assert.Same(first, second);
        }

        [Fact]
        public void ResolveKeepsDifferentBindingFlagsIsolated()
        {
            var instanceProperty = ReplayPropertyCache.Resolve(
                typeof(PropertyFixture),
                nameof(PropertyFixture.Value),
                BindingFlags.Public | BindingFlags.Instance);
            var staticProperty = ReplayPropertyCache.Resolve(
                typeof(PropertyFixture),
                nameof(PropertyFixture.Value),
                BindingFlags.Public | BindingFlags.Static);

            Assert.NotNull(instanceProperty);
            Assert.Null(staticProperty);
        }

        [Fact]
        public void ResolveHandlesNullTypeWithoutThrowing()
        {
            var first = ReplayPropertyCache.Resolve(
                null,
                nameof(PropertyFixture.Value),
                BindingFlags.Public | BindingFlags.Instance);
            var second = ReplayPropertyCache.Resolve(
                null,
                nameof(PropertyFixture.Value),
                BindingFlags.Public | BindingFlags.Instance);

            Assert.Null(first);
            Assert.Null(second);
        }

        private sealed class PropertyFixture
        {
            public int Value { get; set; }
        }
    }
}
