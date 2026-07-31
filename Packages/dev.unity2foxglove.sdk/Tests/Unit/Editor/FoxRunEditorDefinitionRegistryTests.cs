// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System.Linq;
using Unity.FoxgloveSDK.Editor;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Editor
{
    public sealed class FoxRunEditorDefinitionRegistryTests
    {
        [Fact]
        public void CaptureOrdersByExplicitOrderThenStableId()
        {
            var registry = CreateRegistry();
            var last = new Definition("z.provider", 20);
            var second = new Definition("b.provider", 10);
            var first = new Definition("a.provider", 10);

            Assert.Equal(
                FoxRunEditorDefinitionRegistrationResult.Added,
                registry.Register(last));
            Assert.Equal(
                FoxRunEditorDefinitionRegistrationResult.Added,
                registry.Register(second));
            Assert.Equal(
                FoxRunEditorDefinitionRegistrationResult.Added,
                registry.Register(first));

            Assert.Equal(
                new[] { first, second, last },
                registry.Capture().ToArray());
        }

        [Fact]
        public void DuplicateIdIsConflictedAndNoCandidateRemainsSelectable()
        {
            var registry = CreateRegistry();
            var original = new Definition("same.provider", 10);
            var duplicate = new Definition("same.provider", 20);

            Assert.Equal(
                FoxRunEditorDefinitionRegistrationResult.Added,
                registry.Register(original));
            Assert.Equal(
                FoxRunEditorDefinitionRegistrationResult.AlreadyRegistered,
                registry.Register(original));
            Assert.Equal(
                FoxRunEditorDefinitionRegistrationResult.Conflict,
                registry.Register(duplicate));

            Assert.True(registry.IsConflicted("same.provider"));
            Assert.Empty(registry.Capture());
        }

        [Fact]
        public void RegistrationFreezesIdentityAndOrder()
        {
            var registry = CreateRegistry();
            var definition = new Definition("stable.provider", 10);
            registry.Register(definition);

            definition.Id = "mutated.provider";
            definition.Order = -100;

            var captured = Assert.Single(registry.CaptureEntries());
            Assert.Equal("stable.provider", captured.Id);
            Assert.Equal(10, captured.Order);
            Assert.Same(definition, captured.Definition);
        }

        private static FoxRunEditorDefinitionRegistry<Definition>
            CreateRegistry()
            => new FoxRunEditorDefinitionRegistry<Definition>(
                definition => definition.Id,
                definition => definition.Order);

        private sealed class Definition
        {
            internal Definition(string id, int order)
            {
                Id = id;
                Order = order;
            }

            internal string Id { get; set; }
            internal int Order { get; set; }
        }
    }
}
