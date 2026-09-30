// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Schemas
// Purpose: Regression coverage for ROS 2 schema canonicalization.

using System;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.IO;
using Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg;
using Xunit;

namespace Unity2Foxglove.Ros2Bridge.UnitTests
{
    [Trait("Phase", "Module8")]
    [Trait("Domain", "Schemas")]
    public sealed class SchemaSerializationP3Tests
    {
        [Fact]
        public void EquivalentRos2MsgWhitespaceAndCommentsAreAccepted()
        {
            Assert.True(
                FoxgloveRos2MsgSchemaCatalog.TryGet(
                    "foxglove_msgs/msg/Log",
                    out var catalogEntry));
            var equivalent = "\uFEFF" + string.Join(
                    "\r\n",
                    catalogEntry.Content
                        .Split('\n')
                        .Select(line => line + "   "))
                + "\r\n   \r\n# generated trailing comment\r\n";

            var decoder = new McapRos2CdrTypedDecoderFactory().TryCreate(
                new McapSchema
                {
                    Name = catalogEntry.SchemaName,
                    Encoding = FoxgloveRos2MsgSchemaCatalog.SchemaEncoding,
                    Data = Encoding.UTF8.GetBytes(equivalent)
                },
                new McapChannel { Topic = "/log", MessageEncoding = "cdr" });

            Assert.NotNull(decoder);
        }

        [Fact]
        public void RealRos2MsgFieldChangeIsRejected()
        {
            Assert.True(
                FoxgloveRos2MsgSchemaCatalog.TryGet(
                    "foxglove_msgs/msg/Log",
                    out var catalogEntry));
            var changed = catalogEntry.Content.Replace(
                "string message",
                "string changed_message",
                StringComparison.Ordinal);

            var decoder = new McapRos2CdrTypedDecoderFactory().TryCreate(
                new McapSchema
                {
                    Name = catalogEntry.SchemaName,
                    Encoding = FoxgloveRos2MsgSchemaCatalog.SchemaEncoding,
                    Data = Encoding.UTF8.GetBytes(changed)
                },
                new McapChannel { Topic = "/log", MessageEncoding = "cdr" });

            Assert.Null(decoder);
        }
    }
}
