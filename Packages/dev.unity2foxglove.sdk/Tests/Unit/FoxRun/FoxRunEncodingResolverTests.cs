// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Reflection;
using Unity.FoxgloveSDK.Components;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed class FoxRunEncodingResolverTests
    {
        [Fact]
        public void InheritResolvesToTheManagerProtobufDefault()
        {
            Assert.Equal(
                FoxRunEncoding.Protobuf,
                FoxRunEncodingResolver.Resolve((FoxRunEncoding)0, FoxRunEncoding.Protobuf));
        }

        [Fact]
        public void ExplicitJsonWinsOverTheManagerDefault()
        {
            Assert.Equal(
                FoxRunEncoding.JSON,
                FoxRunEncodingResolver.Resolve(FoxRunEncoding.JSON, FoxRunEncoding.Protobuf));
        }

        [Fact]
        public void ManagerDefaultRejectsSourceOnlyInheritState()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                FoxRunEncodingResolver.ValidateProfileDefault((FoxRunEncoding)0));
        }

        [Fact]
        public void ManagerDefaultRejectsUnknownEnumState()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                FoxRunEncodingResolver.ValidateProfileDefault((FoxRunEncoding)99));
        }

        [Fact]
        public void WebSocketWireResolverOnlyReturnsProtobufOrJsonAndRejectsRos2Cdr()
        {
            Assert.Equal(
                "protobuf",
                FoxRunEncodingResolver.ToProtocolEncoding(FoxRunEncoding.Protobuf));
            Assert.Equal(
                "json",
                FoxRunEncodingResolver.ToProtocolEncoding(FoxRunEncoding.JSON));
            Assert.Throws<ArgumentException>(() =>
                FoxRunEncodingResolver.FromProtocolEncoding("ros2"));
            Assert.Throws<ArgumentException>(() =>
                FoxRunEncodingResolver.FromProtocolEncoding("cdr"));
        }

        [Theory]
        [InlineData(FoxRunFlow.Publish, FoxRunEncoding.Protobuf)]
        [InlineData(FoxRunFlow.Subscribe, FoxRunEncoding.JSON)]
        public void InheritResolvesUsingTheDefaultForItsFlowDirection(
            FoxRunFlow mode,
            FoxRunEncoding expected)
        {
            var resolved = InvokeDirectionalResolver(
                (FoxRunEncoding)0,
                mode,
                FoxRunEncoding.Protobuf,
                FoxRunEncoding.JSON);

            Assert.Equal(expected, resolved);
        }

        [Fact]
        public void BidirectionalInheritIsRejectedInsteadOfChoosingOneDirectionalDefault()
        {
            var exception = Assert.Throws<TargetInvocationException>(() => InvokeDirectionalResolver(
                (FoxRunEncoding)0,
                FoxRunFlow.PublishAndSubscribe,
                FoxRunEncoding.Protobuf,
                FoxRunEncoding.JSON));

            Assert.IsType<ArgumentException>(exception.InnerException);
        }

        [Fact]
        public void LegacyJsonPolicyMigratesBothDirectionalDefaultsExactlyOnce()
        {
            var first = InvokePolicyMigration(
                serializationVersion: 0,
                legacyDefault: FoxRunEncoding.JSON,
                publishDefault: FoxRunEncoding.Protobuf,
                subscriptionDefault: FoxRunEncoding.Protobuf);

            Assert.Equal(1, first.SerializationVersion);
            Assert.Equal(FoxRunEncoding.JSON, first.PublishDefault);
            Assert.Equal(FoxRunEncoding.JSON, first.SubscriptionDefault);

            var second = InvokePolicyMigration(
                serializationVersion: first.SerializationVersion,
                legacyDefault: FoxRunEncoding.Protobuf,
                publishDefault: first.PublishDefault,
                subscriptionDefault: first.SubscriptionDefault);

            Assert.Equal(1, second.SerializationVersion);
            Assert.Equal(FoxRunEncoding.JSON, second.PublishDefault);
            Assert.Equal(FoxRunEncoding.JSON, second.SubscriptionDefault);
        }

        [Theory]
        [InlineData((FoxRunEncoding)0)]
        [InlineData((FoxRunEncoding)99)]
        public void LegacyNonConcretePolicyMigratesSafelyToProtobuf(FoxRunEncoding legacyDefault)
        {
            var migrated = InvokePolicyMigration(
                serializationVersion: 0,
                legacyDefault: legacyDefault,
                publishDefault: FoxRunEncoding.JSON,
                subscriptionDefault: FoxRunEncoding.JSON);

            Assert.Equal(1, migrated.SerializationVersion);
            Assert.Equal(FoxRunEncoding.Protobuf, migrated.PublishDefault);
            Assert.Equal(FoxRunEncoding.Protobuf, migrated.SubscriptionDefault);
        }

        private static FoxRunEncoding InvokeDirectionalResolver(
            FoxRunEncoding declared,
            FoxRunFlow mode,
            FoxRunEncoding publishDefault,
            FoxRunEncoding subscriptionDefault)
        {
            var method = typeof(FoxRunEncodingResolver).GetMethod(
                "Resolve",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[]
                {
                    typeof(FoxRunEncoding),
                    typeof(FoxRunFlow),
                    typeof(FoxRunEncoding),
                    typeof(FoxRunEncoding)
                },
                modifiers: null);

            Assert.NotNull(method);
            return (FoxRunEncoding)method!.Invoke(
                null,
                new object[] { declared, mode, publishDefault, subscriptionDefault })!;
        }

        private static (int SerializationVersion, FoxRunEncoding PublishDefault, FoxRunEncoding SubscriptionDefault)
            InvokePolicyMigration(
                int serializationVersion,
                FoxRunEncoding legacyDefault,
                FoxRunEncoding publishDefault,
                FoxRunEncoding subscriptionDefault)
        {
            var type = typeof(FoxRunEncodingResolver).Assembly.GetType(
                "Unity.FoxgloveSDK.Components.FoxRunEncodingPolicyMigration");
            Assert.NotNull(type);
            var method = type!.GetMethod(
                "Migrate",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[]
                {
                    typeof(int).MakeByRefType(),
                    typeof(FoxRunEncoding),
                    typeof(FoxRunEncoding).MakeByRefType(),
                    typeof(FoxRunEncoding).MakeByRefType()
                },
                modifiers: null);

            Assert.NotNull(method);
            object[] arguments = { serializationVersion, legacyDefault, publishDefault, subscriptionDefault };
            method!.Invoke(null, arguments);
            return ((int)arguments[0], (FoxRunEncoding)arguments[2], (FoxRunEncoding)arguments[3]);
        }
    }
}
