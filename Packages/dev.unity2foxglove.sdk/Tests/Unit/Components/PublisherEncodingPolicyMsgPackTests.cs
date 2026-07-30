// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: MsgPack publisher encoding policy coverage.

using Newtonsoft.Json;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Protocol;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{
    [Trait("Phase", "168")]
    [Trait("Domain", "Publishing")]
    public class PublisherEncodingPolicyMsgPackTests
    {
        [Fact]
        public void MsgPackEnumValuesAppendWithoutRenumberingExistingValues()
        {
            Assert.Equal(0, (int)GlobalEncoding.Json);
            Assert.Equal(1, (int)GlobalEncoding.Protobuf);
            Assert.Equal(3, (int)GlobalEncoding.MsgPack);
            Assert.DoesNotContain("Ros2", System.Enum.GetNames(typeof(GlobalEncoding)));

            Assert.Equal(0, (int)PublisherEncodingOverride.UseManager);
            Assert.Equal(1, (int)PublisherEncodingOverride.Json);
            Assert.Equal(2, (int)PublisherEncodingOverride.Protobuf);
            Assert.Equal(4, (int)PublisherEncodingOverride.MsgPack);
            Assert.DoesNotContain(
                "Ros2",
                System.Enum.GetNames(typeof(PublisherEncodingOverride)));

            Assert.Equal(0, (int)PublisherEffectiveEncoding.Json);
            Assert.Equal(1, (int)PublisherEffectiveEncoding.Protobuf);
            Assert.Equal(2, (int)PublisherEffectiveEncoding.Unsupported);
            Assert.Equal(4, (int)PublisherEffectiveEncoding.MsgPack);
            Assert.DoesNotContain(
                "Ros2",
                System.Enum.GetNames(typeof(PublisherEffectiveEncoding)));
        }

        [Fact]
        public void MsgPackResolvesFromManagerDefaultAndPublisherOverride()
        {
            var managerDefault = PublisherEncodingPolicy.Resolve(
                GlobalEncoding.MsgPack,
                allowPublisherOverride: false,
                PublisherEncodingOverride.Json,
                supportsJson: true,
                supportsProtobuf: true,
                supportsMsgPack: true);

            Assert.Equal(PublisherEffectiveEncoding.MsgPack, managerDefault.Requested);
            Assert.Equal(PublisherEffectiveEncoding.MsgPack, managerDefault.Effective);
            Assert.False(managerDefault.FellBack);

            var publisherOverride = PublisherEncodingPolicy.Resolve(
                GlobalEncoding.Json,
                allowPublisherOverride: true,
                PublisherEncodingOverride.MsgPack,
                supportsJson: true,
                supportsProtobuf: false,
                supportsMsgPack: true);

            Assert.Equal(PublisherEffectiveEncoding.MsgPack, publisherOverride.Requested);
            Assert.Equal(PublisherEffectiveEncoding.MsgPack, publisherOverride.Effective);
            Assert.False(publisherOverride.FellBack);
        }

        [Fact]
        public void MsgPackFallsBackBeforeJsonWhenUnsupported()
        {
            var resolution = PublisherEncodingPolicy.Resolve(
                GlobalEncoding.Protobuf,
                allowPublisherOverride: false,
                PublisherEncodingOverride.UseManager,
                supportsJson: true,
                supportsProtobuf: false,
                supportsMsgPack: true);

            Assert.Equal(PublisherEffectiveEncoding.Protobuf, resolution.Requested);
            Assert.Equal(PublisherEffectiveEncoding.MsgPack, resolution.Effective);
            Assert.True(resolution.FellBack);
        }

        [Fact]
        public void JsonIsUsedWhenItIsTheOnlySupportedFallback()
        {
            var resolution = PublisherEncodingPolicy.Resolve(
                GlobalEncoding.Protobuf,
                allowPublisherOverride: false,
                PublisherEncodingOverride.UseManager,
                supportsJson: true,
                supportsProtobuf: false,
                supportsMsgPack: false);

            Assert.Equal(PublisherEffectiveEncoding.Protobuf, resolution.Requested);
            Assert.Equal(PublisherEffectiveEncoding.Json, resolution.Effective);
            Assert.True(resolution.FellBack);
        }

        [Fact]
        public void MsgPackLabelsUseSchemalessProtocolEncoding()
        {
            Assert.Equal("MsgPack", PublisherEncodingPolicy.ToDisplayEncoding(PublisherEffectiveEncoding.MsgPack));
            Assert.Equal("msgpack", PublisherEncodingPolicy.ToProtocolEncoding(PublisherEffectiveEncoding.MsgPack));
            Assert.Equal("", PublisherEncodingPolicy.ToSchemaEncoding(PublisherEffectiveEncoding.MsgPack));
        }

        [Fact]
        public void MsgPackCustomClientAdvertiseShapeOmitsSchemaEncoding()
        {
            var advertise = new Advertise();
            advertise.Channels.Add(new AdvertiseChannel
            {
                Id = 1,
                Topic = "/custom/msgpack",
                Encoding = "msgpack",
                SchemaName = "",
                Schema = ""
            });

            var json = JsonConvert.SerializeObject(advertise);

            Assert.Contains("\"encoding\":\"msgpack\"", json);
            Assert.Contains("\"schemaName\":\"\"", json);
            Assert.Contains("\"schema\":\"\"", json);
            Assert.DoesNotContain("schemaEncoding", json);
        }
    }
}
