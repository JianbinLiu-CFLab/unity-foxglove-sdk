// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Locks direction-specific MCAP coordinate metadata and replay checks.

using System.Collections.Generic;
using System.IO;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{
    [Trait("Phase", "180")]
    [Trait("Domain", "Mcap")]
    public sealed class McapDirectionalCoordinateMetadataTests
    {
        [Fact]
        public void SameTopicAndSchemaUseDistinctDirectionalChannelsWithTruthfulMetadata()
        {
            using var stream = new MemoryStream();
            using var recorder = new McapRecorder(stream)
            {
                OutputCoordinateMode = "RightHand",
                InputCoordinateMode = "LeftHand"
            };

            recorder.AddChannel(
                1,
                "/phase180/pose",
                "json",
                "foxglove.Pose",
                "jsonschema",
                "{\"type\":\"object\"}");
            recorder.WriteMessage(1, 10, new byte[] { 1 });
            recorder.WriteClientMessage(
                7,
                2,
                20,
                new byte[] { 2, 3 },
                "/phase180/pose",
                "json",
                "foxglove.Pose",
                "jsonschema",
                "{\"type\":\"object\"}");
            recorder.Close();

            stream.Position = 0;
            using var reader = new McapReader(stream);
            var summary = reader.ReadSummary();

            Assert.Equal(2, summary.Channels.Count);
            var output = Assert.Single(summary.Channels, channel =>
                channel.Metadata["unity2foxglove.direction"] == "output");
            var input = Assert.Single(summary.Channels, channel =>
                channel.Metadata["unity2foxglove.direction"] == "input");
            Assert.Equal("RightHand", output.Metadata["coordinate_mode"]);
            Assert.Equal("LeftHand", input.Metadata["coordinate_mode"]);
            Assert.Equal(1UL, summary.Statistics.ChannelMessageCounts[output.Id]);
            Assert.Equal(1UL, summary.Statistics.ChannelMessageCounts[input.Id]);
        }

        [Fact]
        public void SameDirectionClientsStillReuseAnAdvertisedChannel()
        {
            using var stream = new MemoryStream();
            using var recorder = new McapRecorder(stream)
            {
                InputCoordinateMode = "RightHand"
            };

            recorder.WriteClientMessage(
                1,
                10,
                10,
                new byte[] { 1 },
                "/phase180/input",
                "json",
                "foxglove.Log",
                "jsonschema",
                "{\"type\":\"object\"}");
            recorder.WriteClientMessage(
                2,
                11,
                20,
                new byte[] { 2 },
                "/phase180/input",
                "json",
                "foxglove.Log",
                "",
                "");
            recorder.Close();

            stream.Position = 0;
            using var reader = new McapReader(stream);
            var summary = reader.ReadSummary();

            Assert.Single(summary.Channels);
            Assert.Equal("input", summary.Channels[0].Metadata["unity2foxglove.direction"]);
            Assert.Equal(2UL, summary.Statistics.ChannelMessageCounts[summary.Channels[0].Id]);
        }

        [Fact]
        public void LegacyCoordinateModeConfiguresBothDirections()
        {
            using var stream = new MemoryStream();
            using var recorder = new McapRecorder(stream)
            {
                CoordinateMode = "LeftHand"
            };

            recorder.AddChannel(1, "/phase180/output", "json", "", "", "");
            recorder.WriteClientMessage(1, 2, 20, new byte[] { 2 }, "/phase180/input");
            recorder.Close();

            stream.Position = 0;
            using var reader = new McapReader(stream);
            var summary = reader.ReadSummary();
            Assert.All(summary.Channels, channel =>
                Assert.Equal("LeftHand", channel.Metadata["coordinate_mode"]));
        }

        [Fact]
        public void LegacyRecordingControllerConfigurationConfiguresBothDirections()
        {
            using var controller = new RecordingController(null);

            controller.Enable("phase180-legacy.mcap", new McapWriterOptions(), "LeftHand");

            Assert.Equal("LeftHand", controller.CoordinateMode);
            Assert.Equal("LeftHand", controller.OutputCoordinateMode);
            Assert.Equal("LeftHand", controller.InputCoordinateMode);
        }

        [Fact]
        public void RecordingControllerKeepsIndependentDirectionalConfiguration()
        {
            using var controller = new RecordingController(null);

            controller.Enable(
                "phase180-directional.mcap",
                new McapWriterOptions(),
                "RightHand",
                "LeftHand");

            Assert.Equal("RightHand", controller.OutputCoordinateMode);
            Assert.Equal("LeftHand", controller.InputCoordinateMode);
            Assert.Equal("RightHand", controller.CoordinateMode);
        }

        [Fact]
        public void ReplayChecksCoordinateMetadataAgainstTheChannelDirection()
        {
            var channels = new[]
            {
                Channel("output", "RightHand"),
                Channel("input", "LeftHand")
            };

            Assert.Null(ReplayCoordinateModeGuard.FindMismatch(
                channels,
                "RightHand",
                "LeftHand",
                "phase180.mcap"));
            Assert.Contains(
                "input",
                ReplayCoordinateModeGuard.FindMismatch(
                    channels,
                    "RightHand",
                    "RightHand",
                    "phase180.mcap"));
        }

        [Fact]
        public void LegacyReplayChannelMustMatchBothDirectionalPolicies()
        {
            var channels = new[]
            {
                new McapChannel
                {
                    Metadata = new Dictionary<string, string>
                    {
                        ["coordinate_mode"] = "RightHand"
                    }
                }
            };

            Assert.Null(ReplayCoordinateModeGuard.FindMismatch(
                channels,
                "RightHand",
                "RightHand",
                "phase180-legacy.mcap"));
            Assert.Contains(
                "legacy",
                ReplayCoordinateModeGuard.FindMismatch(
                    channels,
                    "RightHand",
                    "LeftHand",
                    "phase180-legacy.mcap"));
        }

        private static McapChannel Channel(string direction, string coordinateMode)
        {
            return new McapChannel
            {
                Metadata = new Dictionary<string, string>
                {
                    ["coordinate_mode"] = coordinateMode,
                    ["unity2foxglove.direction"] = direction
                }
            };
        }
    }
}
