// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/ros2_bridge
// Purpose: Protocol logic tests for the Unity2Foxglove ROS 2 bridge sidecar.

#include <gtest/gtest.h>

#include <array>
#include <limits>

// Include the production translation unit directly to exercise internal parser helpers.
#define UNITY2FOXGLOVE_ROS2_BRIDGE_TESTING
#include "../src/unity2foxglove_ros2_bridge.cpp"

namespace
{
struct WireQosContract
{
  std::string profile = "default";
  std::string reliability = "reliable";
  std::string durability = "volatile";
  std::string history = "keep_last";
  int depth = 10;
};

RawFrame MakePublishRawFrame(
  const std::string & topic = "/unity/tf",
  const std::string & schema_name = "foxglove_msgs/msg/FrameTransform",
  const WireQosContract & qos = WireQosContract{})
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
      {"profile", qos.profile},
      {"reliability", qos.reliability},
      {"durability", qos.durability},
      {"history", qos.history},
      {"depth", qos.depth}
    }}
  };
  raw.payload = {0x00, 0x01, 0x00, 0x00, 0x10, 0x20};
  return raw;
}

rmw_qos_profile_t MakeRmwQosProfile(const WireQosContract & contract)
{
  const auto frame = parse_publish_frame(
    MakePublishRawFrame("/unity/qos", "foxglove_msgs/msg/FrameTransform", contract));
  return make_qos(frame).get_rmw_qos_profile();
}

void ExpectPublisherContractConflictRejectedWithoutMutation(
  const BridgeFrame & registered,
  const BridgeFrame & conflicting)
{
  PublisherContractRegistry registry;

  EXPECT_EQ(
    PublisherContractDisposition::CreatePublisher,
    registry.register_or_validate(registered));
  EXPECT_THROW(registry.register_or_validate(conflicting), std::runtime_error);
  EXPECT_THROW(registry.register_or_validate(conflicting), std::runtime_error);
  EXPECT_EQ(
    PublisherContractDisposition::ReusePublisher,
    registry.register_or_validate(registered));
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

TEST(Unity2FoxgloveRos2BridgeProtocol, ParsesDefaultQosContract)
{
  const WireQosContract expected;
  const auto frame = parse_publish_frame(MakePublishRawFrame());

  EXPECT_EQ(expected.profile, frame.profile);
  EXPECT_EQ(expected.reliability, frame.reliability);
  EXPECT_EQ(expected.durability, frame.durability);
  EXPECT_EQ(expected.history, frame.history);
  EXPECT_EQ(expected.depth, frame.depth);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, ParsesSensorDataQosContract)
{
  const WireQosContract expected{
    "sensor_data", "best_effort", "volatile", "keep_last", 5};
  const auto frame = parse_publish_frame(
    MakePublishRawFrame("/unity/qos", "foxglove_msgs/msg/FrameTransform", expected));

  EXPECT_EQ(expected.profile, frame.profile);
  EXPECT_EQ(expected.reliability, frame.reliability);
  EXPECT_EQ(expected.durability, frame.durability);
  EXPECT_EQ(expected.history, frame.history);
  EXPECT_EQ(expected.depth, frame.depth);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, ParsesSystemDefaultQosContract)
{
  const WireQosContract expected{
    "system_default", "system_default", "system_default", "system_default", 0};
  const auto frame = parse_publish_frame(
    MakePublishRawFrame("/unity/qos", "foxglove_msgs/msg/FrameTransform", expected));

  EXPECT_EQ(expected.profile, frame.profile);
  EXPECT_EQ(expected.reliability, frame.reliability);
  EXPECT_EQ(expected.durability, frame.durability);
  EXPECT_EQ(expected.history, frame.history);
  EXPECT_EQ(expected.depth, frame.depth);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, ParsesKeepAllQosContractWithZeroDepth)
{
  const WireQosContract expected{
    "default", "reliable", "transient_local", "keep_all", 0};
  const auto frame = parse_publish_frame(
    MakePublishRawFrame("/unity/qos", "foxglove_msgs/msg/FrameTransform", expected));

  EXPECT_EQ(expected.profile, frame.profile);
  EXPECT_EQ(expected.reliability, frame.reliability);
  EXPECT_EQ(expected.durability, frame.durability);
  EXPECT_EQ(expected.history, frame.history);
  EXPECT_EQ(expected.depth, frame.depth);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, ParsesKeepLastQosContractWithNonDefaultDepth)
{
  const WireQosContract expected{
    "default", "best_effort", "transient_local", "keep_last", 37};
  const auto frame = parse_publish_frame(
    MakePublishRawFrame("/unity/qos", "foxglove_msgs/msg/FrameTransform", expected));

  EXPECT_EQ(expected.profile, frame.profile);
  EXPECT_EQ(expected.reliability, frame.reliability);
  EXPECT_EQ(expected.durability, frame.durability);
  EXPECT_EQ(expected.history, frame.history);
  EXPECT_EQ(expected.depth, frame.depth);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, RejectsInvalidQosProfile)
{
  auto raw = MakePublishRawFrame();
  raw.header["qos"]["profile"] = "unknown_profile";

  EXPECT_THROW(parse_publish_frame(raw), std::runtime_error);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, RejectsInvalidQosReliability)
{
  auto raw = MakePublishRawFrame();
  raw.header["qos"]["reliability"] = "sometimes_reliable";

  EXPECT_THROW(parse_publish_frame(raw), std::runtime_error);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, RejectsInvalidQosDurability)
{
  auto raw = MakePublishRawFrame();
  raw.header["qos"]["durability"] = "persistent";

  EXPECT_THROW(parse_publish_frame(raw), std::runtime_error);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, RejectsInvalidQosHistory)
{
  auto raw = MakePublishRawFrame();
  raw.header["qos"]["history"] = "keep_some";

  EXPECT_THROW(parse_publish_frame(raw), std::runtime_error);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, RejectsNonIntegerQosDepthTypes)
{
  const std::array<nlohmann::json, 3> invalid_depths = {
    nlohmann::json("10"),
    nlohmann::json(10.0),
    nlohmann::json(10.5)
  };
  for (const auto & invalid_depth : invalid_depths) {
    SCOPED_TRACE("depth=" + invalid_depth.dump());
    auto raw = MakePublishRawFrame();
    raw.header["qos"]["depth"] = invalid_depth;
    EXPECT_THROW(parse_publish_frame(raw), std::runtime_error);
  }
}

TEST(Unity2FoxgloveRos2BridgeProtocol, RejectsOutOfRangeQosDepth)
{
  const auto above_int_max =
    static_cast<int64_t>(std::numeric_limits<int>::max()) + 1;
  const auto below_int_min =
    static_cast<int64_t>(std::numeric_limits<int>::min()) - 1;
  const std::array<nlohmann::json, 2> invalid_depths = {
    nlohmann::json(above_int_max),
    nlohmann::json(below_int_min)
  };
  for (const auto & invalid_depth : invalid_depths) {
    SCOPED_TRACE("depth=" + invalid_depth.dump());
    auto raw = MakePublishRawFrame();
    raw.header["qos"]["depth"] = invalid_depth;
    EXPECT_THROW(parse_publish_frame(raw), std::runtime_error);
  }
}

TEST(Unity2FoxgloveRos2BridgeProtocol, RejectsNonPositiveKeepLastDepth)
{
  auto zero_depth = MakePublishRawFrame();
  zero_depth.header["qos"]["depth"] = 0;
  EXPECT_THROW(parse_publish_frame(zero_depth), std::runtime_error);

  auto negative_depth = MakePublishRawFrame();
  negative_depth.header["qos"]["depth"] = -1;
  EXPECT_THROW(parse_publish_frame(negative_depth), std::runtime_error);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, RejectsNonzeroDepthForNonKeepLastHistory)
{
  const std::array<std::string, 2> histories = {"keep_all", "system_default"};
  for (const auto & history : histories) {
    SCOPED_TRACE("history=" + history);
    auto raw = MakePublishRawFrame();
    raw.header["qos"]["history"] = history;
    raw.header["qos"]["depth"] = 1;
    EXPECT_THROW(parse_publish_frame(raw), std::runtime_error);
  }
}

TEST(Unity2FoxgloveRos2BridgeProtocol, RejectsMissingRequiredQosFields)
{
  const std::array<std::string, 5> required_fields = {
    "profile", "reliability", "durability", "history", "depth"};
  for (const auto & field : required_fields) {
    SCOPED_TRACE("field=" + field);
    auto raw = MakePublishRawFrame();
    raw.header["qos"].erase(field);
    EXPECT_THROW(parse_publish_frame(raw), std::runtime_error);
  }
}

TEST(Unity2FoxgloveRos2BridgeProtocol, DefaultsMissingQosObjectForLegacyPublishers)
{
  const WireQosContract expected;
  auto raw = MakePublishRawFrame();
  raw.header.erase("qos");

  const auto frame = parse_publish_frame(raw);
  EXPECT_EQ(expected.profile, frame.profile);
  EXPECT_EQ(expected.reliability, frame.reliability);
  EXPECT_EQ(expected.durability, frame.durability);
  EXPECT_EQ(expected.history, frame.history);
  EXPECT_EQ(expected.depth, frame.depth);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, DefaultsNullQosObjectForLegacyPublishers)
{
  const WireQosContract expected;
  auto raw = MakePublishRawFrame();
  raw.header["qos"] = nullptr;

  const auto frame = parse_publish_frame(raw);
  EXPECT_EQ(expected.profile, frame.profile);
  EXPECT_EQ(expected.reliability, frame.reliability);
  EXPECT_EQ(expected.durability, frame.durability);
  EXPECT_EQ(expected.history, frame.history);
  EXPECT_EQ(expected.depth, frame.depth);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, RejectsNonObjectQosValue)
{
  auto raw = MakePublishRawFrame();
  raw.header["qos"] = "default";

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

TEST(Unity2FoxgloveRos2BridgeProtocol, PublisherReuseSignatureCapturesSchemaAndEveryQosField)
{
  const auto baseline = parse_publish_frame(MakePublishRawFrame());
  const auto baseline_signature = qos_signature(baseline);

  auto changed = baseline;
  changed.schema_name = "foxglove_msgs/msg/CompressedImage";
  EXPECT_NE(baseline_signature, qos_signature(changed));

  changed = baseline;
  changed.profile = "sensor_data";
  EXPECT_NE(baseline_signature, qos_signature(changed));

  changed = baseline;
  changed.reliability = "best_effort";
  EXPECT_NE(baseline_signature, qos_signature(changed));

  changed = baseline;
  changed.durability = "transient_local";
  EXPECT_NE(baseline_signature, qos_signature(changed));

  const auto keep_all = parse_publish_frame(
    MakePublishRawFrame(
      "/unity/tf",
      "foxglove_msgs/msg/FrameTransform",
      WireQosContract{"default", "reliable", "volatile", "keep_all", 0}));
  const auto system_default_history = parse_publish_frame(
    MakePublishRawFrame(
      "/unity/tf",
      "foxglove_msgs/msg/FrameTransform",
      WireQosContract{"default", "reliable", "volatile", "system_default", 0}));
  EXPECT_EQ(keep_all.topic, system_default_history.topic);
  EXPECT_EQ(keep_all.schema_name, system_default_history.schema_name);
  EXPECT_NE(qos_signature(keep_all), qos_signature(system_default_history));

  changed = baseline;
  changed.depth = 37;
  EXPECT_NE(baseline_signature, qos_signature(changed));
}

TEST(Unity2FoxgloveRos2BridgeProtocol, PublisherContractRegistryReusesIdenticalContract)
{
  PublisherContractRegistry registry;
  const auto first = parse_publish_frame(MakePublishRawFrame());
  auto repeated_raw = MakePublishRawFrame();
  repeated_raw.header["logTimeNs"] = 5678;
  repeated_raw.header["sequence"] = 8;
  repeated_raw.payload = {0x00, 0x01, 0x00, 0x00, 0x30, 0x40};
  const auto repeated = parse_publish_frame(repeated_raw);

  EXPECT_EQ(
    PublisherContractDisposition::CreatePublisher,
    registry.register_or_validate(first));
  EXPECT_EQ(
    PublisherContractDisposition::ReusePublisher,
    registry.register_or_validate(repeated));
}

TEST(Unity2FoxgloveRos2BridgeProtocol, PublisherContractRegistryKeepsTopicsIndependent)
{
  PublisherContractRegistry registry;
  const auto topic_a = parse_publish_frame(MakePublishRawFrame("/unity/topic_a"));
  const auto topic_b = parse_publish_frame(
    MakePublishRawFrame(
      "/unity/topic_b",
      "foxglove_msgs/msg/FrameTransform",
      WireQosContract{"sensor_data", "best_effort", "volatile", "keep_last", 5}));

  EXPECT_EQ(
    PublisherContractDisposition::CreatePublisher,
    registry.register_or_validate(topic_a));
  EXPECT_EQ(
    PublisherContractDisposition::CreatePublisher,
    registry.register_or_validate(topic_b));
  EXPECT_EQ(
    PublisherContractDisposition::ReusePublisher,
    registry.register_or_validate(topic_a));
  EXPECT_EQ(
    PublisherContractDisposition::ReusePublisher,
    registry.register_or_validate(topic_b));
}

TEST(
  Unity2FoxgloveRos2BridgeProtocol,
  PublisherContractRegistryRejectsSchemaAndEveryQosConflictWithoutMutation)
{
  const auto baseline = parse_publish_frame(MakePublishRawFrame());

  auto changed_schema = baseline;
  changed_schema.schema_name = "foxglove_msgs/msg/CompressedImage";
  ExpectPublisherContractConflictRejectedWithoutMutation(baseline, changed_schema);

  const auto changed_profile = parse_publish_frame(
    MakePublishRawFrame(
      "/unity/tf",
      "foxglove_msgs/msg/FrameTransform",
      WireQosContract{"sensor_data", "reliable", "volatile", "keep_last", 10}));
  ExpectPublisherContractConflictRejectedWithoutMutation(baseline, changed_profile);

  const auto changed_reliability = parse_publish_frame(
    MakePublishRawFrame(
      "/unity/tf",
      "foxglove_msgs/msg/FrameTransform",
      WireQosContract{"default", "best_effort", "volatile", "keep_last", 10}));
  ExpectPublisherContractConflictRejectedWithoutMutation(baseline, changed_reliability);

  const auto changed_durability = parse_publish_frame(
    MakePublishRawFrame(
      "/unity/tf",
      "foxglove_msgs/msg/FrameTransform",
      WireQosContract{"default", "reliable", "transient_local", "keep_last", 10}));
  ExpectPublisherContractConflictRejectedWithoutMutation(baseline, changed_durability);

  const auto keep_all = parse_publish_frame(
    MakePublishRawFrame(
      "/unity/tf",
      "foxglove_msgs/msg/FrameTransform",
      WireQosContract{"default", "reliable", "volatile", "keep_all", 0}));
  const auto system_default_history = parse_publish_frame(
    MakePublishRawFrame(
      "/unity/tf",
      "foxglove_msgs/msg/FrameTransform",
      WireQosContract{"default", "reliable", "volatile", "system_default", 0}));
  ExpectPublisherContractConflictRejectedWithoutMutation(keep_all, system_default_history);

  const auto changed_depth = parse_publish_frame(
    MakePublishRawFrame(
      "/unity/tf",
      "foxglove_msgs/msg/FrameTransform",
      WireQosContract{"default", "reliable", "volatile", "keep_last", 37}));
  ExpectPublisherContractConflictRejectedWithoutMutation(baseline, changed_depth);
}

TEST(
  Unity2FoxgloveRos2BridgeProtocol,
  ProcessClientOwnsFreshPublisherSession)
{
  using ProcessClientSession =
    void (*)(int, const rclcpp::Node::SharedPtr &, PayloadFormat);
  ProcessClientSession process = &process_client;

  EXPECT_NE(nullptr, process);
}

TEST(
  Unity2FoxgloveRos2BridgeProtocol,
  NewClientSessionAcceptsReplacementQosForTheSameTopic)
{
  const auto first = parse_publish_frame(MakePublishRawFrame());
  const auto replacement = parse_publish_frame(
    MakePublishRawFrame(
      "/unity/tf",
      "foxglove_msgs/msg/FrameTransform",
      WireQosContract{"sensor_data", "best_effort", "volatile", "keep_last", 5}));

  {
    PublisherContractRegistry first_client;
    EXPECT_EQ(
      PublisherContractDisposition::CreatePublisher,
      first_client.register_or_validate(first));
    EXPECT_THROW(
      first_client.register_or_validate(replacement),
      std::runtime_error);
  }

  {
    PublisherContractRegistry replacement_client;
    EXPECT_EQ(
      PublisherContractDisposition::CreatePublisher,
      replacement_client.register_or_validate(replacement));
  }
}

TEST(Unity2FoxgloveRos2BridgeProtocol, MakesCanonicalDefaultQos)
{
  const auto qos = MakeRmwQosProfile(WireQosContract{});

  EXPECT_EQ(RMW_QOS_POLICY_RELIABILITY_RELIABLE, qos.reliability);
  EXPECT_EQ(RMW_QOS_POLICY_DURABILITY_VOLATILE, qos.durability);
  EXPECT_EQ(RMW_QOS_POLICY_HISTORY_KEEP_LAST, qos.history);
  EXPECT_EQ(10U, qos.depth);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, MakesSensorDataQos)
{
  const auto qos = MakeRmwQosProfile(
    WireQosContract{"sensor_data", "best_effort", "volatile", "keep_last", 5});

  EXPECT_EQ(RMW_QOS_POLICY_RELIABILITY_BEST_EFFORT, qos.reliability);
  EXPECT_EQ(RMW_QOS_POLICY_DURABILITY_VOLATILE, qos.durability);
  EXPECT_EQ(RMW_QOS_POLICY_HISTORY_KEEP_LAST, qos.history);
  EXPECT_EQ(5U, qos.depth);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, MakesSystemDefaultQosWithoutDowngrade)
{
  const auto qos = MakeRmwQosProfile(
    WireQosContract{
      "system_default", "system_default", "system_default", "system_default", 0});

  EXPECT_EQ(RMW_QOS_POLICY_RELIABILITY_SYSTEM_DEFAULT, qos.reliability);
  EXPECT_EQ(RMW_QOS_POLICY_DURABILITY_SYSTEM_DEFAULT, qos.durability);
  EXPECT_EQ(RMW_QOS_POLICY_HISTORY_SYSTEM_DEFAULT, qos.history);
  EXPECT_EQ(0U, qos.depth);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, MakesDefaultProfileWithSystemDefaultOverrides)
{
  const auto qos = MakeRmwQosProfile(
    WireQosContract{
      "default", "system_default", "system_default", "system_default", 0});

  EXPECT_EQ(RMW_QOS_POLICY_RELIABILITY_SYSTEM_DEFAULT, qos.reliability);
  EXPECT_EQ(RMW_QOS_POLICY_DURABILITY_SYSTEM_DEFAULT, qos.durability);
  EXPECT_EQ(RMW_QOS_POLICY_HISTORY_SYSTEM_DEFAULT, qos.history);
  EXPECT_EQ(0U, qos.depth);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, MakesSystemDefaultProfileWithExplicitOverrides)
{
  const auto qos = MakeRmwQosProfile(
    WireQosContract{
      "system_default", "reliable", "transient_local", "keep_last", 37});

  EXPECT_EQ(RMW_QOS_POLICY_RELIABILITY_RELIABLE, qos.reliability);
  EXPECT_EQ(RMW_QOS_POLICY_DURABILITY_TRANSIENT_LOCAL, qos.durability);
  EXPECT_EQ(RMW_QOS_POLICY_HISTORY_KEEP_LAST, qos.history);
  EXPECT_EQ(37U, qos.depth);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, MakesKeepAllQosWithoutSynthesizingDepth)
{
  const auto qos = MakeRmwQosProfile(
    WireQosContract{"default", "reliable", "transient_local", "keep_all", 0});

  EXPECT_EQ(RMW_QOS_POLICY_RELIABILITY_RELIABLE, qos.reliability);
  EXPECT_EQ(RMW_QOS_POLICY_DURABILITY_TRANSIENT_LOCAL, qos.durability);
  EXPECT_EQ(RMW_QOS_POLICY_HISTORY_KEEP_ALL, qos.history);
  EXPECT_EQ(0U, qos.depth);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, MakesKeepLastQosWithNonDefaultDepth)
{
  const auto qos = MakeRmwQosProfile(
    WireQosContract{"default", "best_effort", "volatile", "keep_last", 37});

  EXPECT_EQ(RMW_QOS_POLICY_RELIABILITY_BEST_EFFORT, qos.reliability);
  EXPECT_EQ(RMW_QOS_POLICY_DURABILITY_VOLATILE, qos.durability);
  EXPECT_EQ(RMW_QOS_POLICY_HISTORY_KEEP_LAST, qos.history);
  EXPECT_EQ(37U, qos.depth);
}
