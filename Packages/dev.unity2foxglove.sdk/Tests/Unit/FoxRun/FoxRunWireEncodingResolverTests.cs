// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Reflection;
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

        [Fact]
        public void ManagerDefaultRejectsUnknownEnumState()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                FoxRunWireEncodingResolver.ValidateManagerDefault((FoxRunWireEncoding)99));
        }

        [Fact]
        public void WebSocketWireResolverOnlyReturnsProtobufOrJsonAndRejectsRos2Cdr()
        {
            Assert.Equal(
                "protobuf",
                FoxRunWireEncodingResolver.ToProtocolEncoding(FoxRunWireEncoding.Protobuf));
            Assert.Equal(
                "json",
                FoxRunWireEncodingResolver.ToProtocolEncoding(FoxRunWireEncoding.Json));
            Assert.Throws<ArgumentException>(() =>
                FoxRunWireEncodingResolver.FromProtocolEncoding("ros2"));
            Assert.Throws<ArgumentException>(() =>
                FoxRunWireEncodingResolver.FromProtocolEncoding("cdr"));
        }

        [Theory]
        [InlineData(FoxRunMode.PublishOnly, FoxRunWireEncoding.Protobuf)]
        [InlineData(FoxRunMode.SubscribeOnly, FoxRunWireEncoding.Json)]
        public void InheritResolvesUsingTheDefaultForItsFlowDirection(
            FoxRunMode mode,
            FoxRunWireEncoding expected)
        {
            var resolved = InvokeDirectionalResolver(
                FoxRunWireEncoding.Inherit,
                mode,
                FoxRunWireEncoding.Protobuf,
                FoxRunWireEncoding.Json);

            Assert.Equal(expected, resolved);
        }

        [Fact]
        public void BidirectionalInheritIsRejectedInsteadOfChoosingOneDirectionalDefault()
        {
            var exception = Assert.Throws<TargetInvocationException>(() => InvokeDirectionalResolver(
                FoxRunWireEncoding.Inherit,
                FoxRunMode.PublishAndSubscribe,
                FoxRunWireEncoding.Protobuf,
                FoxRunWireEncoding.Json));

            Assert.IsType<ArgumentException>(exception.InnerException);
        }

        [Fact]
        public void LegacyJsonPolicyMigratesBothDirectionalDefaultsExactlyOnce()
        {
            var first = InvokePolicyMigration(
                serializationVersion: 0,
                legacyDefault: FoxRunWireEncoding.Json,
                publishDefault: FoxRunWireEncoding.Protobuf,
                subscriptionDefault: FoxRunWireEncoding.Protobuf);

            Assert.Equal(1, first.SerializationVersion);
            Assert.Equal(FoxRunWireEncoding.Json, first.PublishDefault);
            Assert.Equal(FoxRunWireEncoding.Json, first.SubscriptionDefault);

            var second = InvokePolicyMigration(
                serializationVersion: first.SerializationVersion,
                legacyDefault: FoxRunWireEncoding.Protobuf,
                publishDefault: first.PublishDefault,
                subscriptionDefault: first.SubscriptionDefault);

            Assert.Equal(1, second.SerializationVersion);
            Assert.Equal(FoxRunWireEncoding.Json, second.PublishDefault);
            Assert.Equal(FoxRunWireEncoding.Json, second.SubscriptionDefault);
        }

        [Theory]
        [InlineData(FoxRunWireEncoding.Inherit)]
        [InlineData((FoxRunWireEncoding)99)]
        public void LegacyNonConcretePolicyMigratesSafelyToProtobuf(FoxRunWireEncoding legacyDefault)
        {
            var migrated = InvokePolicyMigration(
                serializationVersion: 0,
                legacyDefault: legacyDefault,
                publishDefault: FoxRunWireEncoding.Json,
                subscriptionDefault: FoxRunWireEncoding.Json);

            Assert.Equal(1, migrated.SerializationVersion);
            Assert.Equal(FoxRunWireEncoding.Protobuf, migrated.PublishDefault);
            Assert.Equal(FoxRunWireEncoding.Protobuf, migrated.SubscriptionDefault);
        }

        private static FoxRunWireEncoding InvokeDirectionalResolver(
            FoxRunWireEncoding declared,
            FoxRunMode mode,
            FoxRunWireEncoding publishDefault,
            FoxRunWireEncoding subscriptionDefault)
        {
            var method = typeof(FoxRunWireEncodingResolver).GetMethod(
                "Resolve",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[]
                {
                    typeof(FoxRunWireEncoding),
                    typeof(FoxRunMode),
                    typeof(FoxRunWireEncoding),
                    typeof(FoxRunWireEncoding)
                },
                modifiers: null);

            Assert.NotNull(method);
            return (FoxRunWireEncoding)method!.Invoke(
                null,
                new object[] { declared, mode, publishDefault, subscriptionDefault })!;
        }

        private static (int SerializationVersion, FoxRunWireEncoding PublishDefault, FoxRunWireEncoding SubscriptionDefault)
            InvokePolicyMigration(
                int serializationVersion,
                FoxRunWireEncoding legacyDefault,
                FoxRunWireEncoding publishDefault,
                FoxRunWireEncoding subscriptionDefault)
        {
            var type = typeof(FoxRunWireEncodingResolver).Assembly.GetType(
                "Unity.FoxgloveSDK.Components.FoxRunWireEncodingPolicyMigration");
            Assert.NotNull(type);
            var method = type!.GetMethod(
                "Migrate",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[]
                {
                    typeof(int).MakeByRefType(),
                    typeof(FoxRunWireEncoding),
                    typeof(FoxRunWireEncoding).MakeByRefType(),
                    typeof(FoxRunWireEncoding).MakeByRefType()
                },
                modifiers: null);

            Assert.NotNull(method);
            object[] arguments = { serializationVersion, legacyDefault, publishDefault, subscriptionDefault };
            method!.Invoke(null, arguments);
            return ((int)arguments[0], (FoxRunWireEncoding)arguments[2], (FoxRunWireEncoding)arguments[3]);
        }
    }
}
