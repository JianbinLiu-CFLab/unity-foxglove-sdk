// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/ros2_bridge
// Purpose: Protocol logic tests for the Unity2Foxglove ROS 2 bridge sidecar.

#include <gtest/gtest.h>

#define UNITY2FOXGLOVE_ROS2_BRIDGE_TESTING
#include "../src/unity2foxglove_ros2_bridge.cpp"

namespace
{
RawFrame MakePublishRawFrame(
  const std::string & topic = "/unity/tf",
  const std::string & schema_name = "foxglove_msgs/msg/FrameTransform")
{
  RawFrame raw;
  raw.header = {
    {"op", "publish"},
    {"topic", topic},
    {"schemaName", schema_name},
    {"encoding", "cdr"},
    {"logTimeNs", 1234},
    {"sequence", 7},
    {"qos", {
      {"reliability", "reliable"},
      {"durability", "volatile"},
      {"depth", 10}
    }}
  };
  raw.payload = {0x00, 0x01, 0x00, 0x00, 0x10, 0x20};
  return raw;
}
}  // namespace

TEST(Unity2FoxgloveRos2BridgeProtocol, ValidatesTopicNames)
{
  EXPECT_TRUE(is_valid_ros2_topic_name("/unity/tf"));
  EXPECT_TRUE(is_valid_ros2_topic_name("/unity2foxglove/point_cloud_2"));
  EXPECT_FALSE(is_valid_ros2_topic_name(""));
  EXPECT_FALSE(is_valid_ros2_topic_name("unity/tf"));
  EXPECT_FALSE(is_valid_ros2_topic_name("/unity//tf"));
  EXPECT_FALSE(is_valid_ros2_topic_name("/unity/tf-with-dash"));
}

TEST(Unity2FoxgloveRos2BridgeProtocol, RejectsNonFoxgloveSchemas)
{
  auto raw = MakePublishRawFrame("/unity/image", "sensor_msgs/msg/Image");
  EXPECT_THROW(parse_publish_frame(raw), std::runtime_error);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, ForwardsEncapsulatedPayloadByView)
{
  auto frame = parse_publish_frame(MakePublishRawFrame());
  std::vector<uint8_t> scratch;
  const auto payload = payload_for_publish(frame, PayloadFormat::CdrWithEncapsulation, scratch);

  EXPECT_TRUE(scratch.empty());
  ASSERT_EQ(frame.payload.size(), payload.size);
  EXPECT_EQ(frame.payload.data(), payload.data);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, PrependsEncapsulationForBodyOnlyPayload)
{
  auto frame = parse_publish_frame(MakePublishRawFrame());
  frame.payload = {0x10, 0x20, 0x30};
  std::vector<uint8_t> scratch;
  const auto payload = payload_for_publish(frame, PayloadFormat::CdrBodyOnly, scratch);

  const std::vector<uint8_t> expected = {0x00, 0x01, 0x00, 0x00, 0x10, 0x20, 0x30};
  ASSERT_EQ(expected.size(), payload.size);
  EXPECT_EQ(expected, scratch);
  EXPECT_EQ(scratch.data(), payload.data);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, RejectsEncapsulatedBodyOnlyPayload)
{
  auto frame = parse_publish_frame(MakePublishRawFrame());
  std::vector<uint8_t> scratch;

  EXPECT_THROW(payload_for_publish(frame, PayloadFormat::CdrBodyOnly, scratch), std::runtime_error);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, QoSSignatureCapturesSchemaAndProfile)
{
  auto frame = parse_publish_frame(MakePublishRawFrame());
  const auto reliable = qos_signature(frame);

  frame.reliability = "best_effort";
  EXPECT_NE(reliable, qos_signature(frame));

  frame.reliability = "reliable";
  frame.durability = "transient_local";
  EXPECT_NE(reliable, qos_signature(frame));
}
