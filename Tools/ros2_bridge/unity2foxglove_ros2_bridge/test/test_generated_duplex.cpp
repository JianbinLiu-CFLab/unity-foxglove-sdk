// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/ros2_bridge
// Purpose: Live generated-standard and Phase181 typed duplex certification.

#ifdef _WIN32
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <winsock2.h>
#include <ws2tcpip.h>
#endif

#include <gtest/gtest.h>

#include <chrono>
#include <cstdint>
#include <cstring>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

#include <rclcpp/serialization.hpp>
#include <foxglove_msgs/msg/log.hpp>
#include <unity2foxglove_foxrun_interfaces_v1/msg/phase181_state48_d288_ed82_f1_envelope.hpp>

#include "unity2foxglove_ros2_bridge/bridge_writer.hpp"

#define UNITY2FOXGLOVE_ROS2_BRIDGE_TESTING
#include "../src/unity2foxglove_ros2_bridge.cpp"

namespace
{
namespace runtime = unity2foxglove::ros2_bridge::runtime;
namespace u2r2 = unity2foxglove::ros2_bridge::u2r2;

using namespace std::chrono_literals;

u2r2::Qos DefaultQos()
{
  return u2r2::Qos{
    "default",
    "reliable",
    "volatile",
    "keep_last",
    10U};
}

u2r2::OutboundFrame SemanticErrorFrame(
  u2r2::Operation,
  uint64_t request_id,
  const u2r2::ProtocolError &)
{
  return u2r2::OutboundFrame::control(
    "error:" + std::to_string(request_id),
    {0xeeU});
}

template<typename Predicate>
bool WaitUntil(Predicate predicate)
{
  const auto deadline = std::chrono::steady_clock::now() + 5s;
  while (std::chrono::steady_clock::now() < deadline) {
    if (predicate()) {
      return true;
    }
    std::this_thread::sleep_for(10ms);
  }
  return predicate();
}

template<typename MessageT>
std::vector<uint8_t> SerializeMessage(const MessageT & message)
{
  rclcpp::Serialization<MessageT> serializer;
  rclcpp::SerializedMessage serialized;
  serializer.serialize_message(&message, &serialized);
  const auto & raw = serialized.get_rcl_serialized_message();
  return std::vector<uint8_t>(raw.buffer, raw.buffer + raw.buffer_length);
}

template<typename MessageT>
MessageT DeserializeMessage(const std::vector<uint8_t> & payload)
{
  rclcpp::Serialization<MessageT> serializer;
  rclcpp::SerializedMessage serialized(payload.size());
  auto & raw = serialized.get_rcl_serialized_message();
  std::memcpy(raw.buffer, payload.data(), payload.size());
  raw.buffer_length = payload.size();
  MessageT message;
  serializer.deserialize_message(&serialized, &message);
  return message;
}

void ExpectExactPayload(
  const std::vector<uint8_t> & expected,
  const std::vector<uint8_t> & actual)
{
  ASSERT_EQ(expected.size(), actual.size());
  for (std::size_t index = 0; index < expected.size(); ++index) {
    ASSERT_EQ(expected[index], actual[index]) << "payload byte index " << index;
  }
}

template<typename MessageT>
void VerifyTypedDuplex(
  const std::string & node_suffix,
  const std::string & topic,
  const std::string & schema,
  uint64_t contract_id,
  const MessageT & message)
{
  auto context = std::make_shared<rclcpp::Context>();
  context->init(0, nullptr);
  bridge_runtime::ProcessRosOwner ros_owner(
    "phase186_f_bridge_" + node_suffix,
    context);

  const auto expected_payload = SerializeMessage(message);
  ASSERT_GE(expected_payload.size(), 4U);
  EXPECT_EQ(0x00U, expected_payload[0]);
  EXPECT_EQ(0x01U, expected_payload[1]);

  const auto limits = u2r2::ProtocolLimits::defaults();
  runtime::BridgeWriterCore writer(limits);
  auto attached = writer.try_attach_writer();
  ASSERT_TRUE(attached.has_value());
  auto writer_lease = std::move(*attached);
  u2r2::RequestReplayAuthority replay(limits);
  u2r2::ContractAuthority contracts(limits, SemanticErrorFrame);
  runtime::BridgeOutboundQueue queue(
    limits,
    writer,
    contracts,
    "phase186f-generated-duplex-" + node_suffix,
    186U);
  const u2r2::ContractIdentity identity(
    u2r2::ContractKey(contract_id, 186U),
    u2r2::ContractDirection::subscribe,
    topic,
    schema,
    DefaultQos());
  auto response = replay.admit(
    contract_id,
    {0x01U},
    64U,
    writer.scheduler());
  auto registration = contracts.begin_registration(
    identity,
    writer.scheduler(),
    replay,
    response);
  auto gate = queue.create_gate(identity);

  auto bridge = std::make_unique<BridgeNode>(
    ros_owner.require_node(),
    PayloadFormat::CdrWithEncapsulation);
  BridgeFrame local;
  local.topic = topic;
  local.schema_name = schema;
  local.encoding = "cdr";
  local.profile = "default";
  local.reliability = "reliable";
  local.durability = "volatile";
  local.history = "keep_last";
  local.depth = 10;
  local.payload = expected_payload;
  bridge->prepare(local);
  std::mutex captured_payload_gate;
  std::vector<uint8_t> captured_external_payload;
  auto queue_callback = queue.callback(gate);
  auto capture_and_queue =
    [&captured_payload_gate, &captured_external_payload, queue_callback](
    const uint8_t * payload,
    size_t payload_size,
    uint64_t receive_time_ns,
    runtime::BridgeSampleOrigin origin) {
      if (origin == runtime::BridgeSampleOrigin::external) {
        std::lock_guard<std::mutex> lock(captured_payload_gate);
        captured_external_payload.assign(payload, payload + payload_size);
      }
      return queue_callback(payload, payload_size, receive_time_ns, origin);
    };
  auto subscription = bridge->subscribe(identity, std::move(capture_and_queue));
  ASSERT_TRUE(subscription);
  contracts.commit_ready(
    registration,
    replay,
    response,
    u2r2::OutboundFrame::control("ready", {0xa1U}));
  auto ready = writer.try_begin_write(writer_lease);
  ASSERT_TRUE(ready.has_value());
  ready->release();
  queue.activate(gate);

  ASSERT_TRUE(WaitUntil([&]() {
      bridge->publish_prepared(local);
      return queue.stats().suppressed_local > 0U;
    }));
  EXPECT_FALSE(writer.try_begin_write(writer_lease).has_value());

  rclcpp::NodeOptions options;
  options.context(context);
  auto external = std::make_shared<rclcpp::Node>(
    "phase186_f_external_" + node_suffix,
    options);
  auto external_publisher = external->create_publisher<MessageT>(
    topic,
    rclcpp::QoS(10).reliable());
  ASSERT_TRUE(WaitUntil([&]() {
      return external_publisher->get_subscription_count() > 0U;
    }));
  external_publisher->publish(message);
  ASSERT_TRUE(WaitUntil([&]() {return queue.stats().accepted == 1U;}));

  auto forwarded = writer.try_begin_write(writer_lease);
  ASSERT_TRUE(forwarded.has_value());
  const auto frame = u2r2::decode_frame(forwarded->frame().bytes());
  const auto parsed = u2r2::parse_v2(frame);
  EXPECT_EQ(u2r2::Operation::Message, parsed.operation);
  EXPECT_EQ(contract_id, parsed.contract_id);
  std::vector<uint8_t> captured_payload;
  {
    std::lock_guard<std::mutex> lock(captured_payload_gate);
    captured_payload = captured_external_payload;
  }
  ASSERT_FALSE(captured_payload.empty());
  ExpectExactPayload(captured_payload, frame.payload);
  EXPECT_EQ(message, DeserializeMessage<MessageT>(captured_payload));
  forwarded->release();

  queue.revoke(gate);
  subscription.reset();
  external_publisher.reset();
  external.reset();
  bridge.reset();
  EXPECT_TRUE(ros_owner.stop());
  context->shutdown("Phase186-F generated duplex probe complete");
}

TEST(GeneratedDuplex, GeneratedFoxgloveLogIsSuppressedLocallyAndForwardedExternally)
{
  foxglove_msgs::msg::Log message;
  message.timestamp.sec = 186;
  message.timestamp.nanosec = 123456789U;
  message.level = foxglove_msgs::msg::Log::INFO;
  message.message = "phase186-generated-standard";
  message.name = "phase186_f_external_standard";
  message.file = "test_generated_duplex.cpp";
  message.line = 186U;

  VerifyTypedDuplex(
    "generated_standard",
    "/phase186/f/generated_standard",
    "foxglove_msgs/msg/Log",
    18601U,
    message);
}

TEST(GeneratedDuplex, Phase181EnvelopeIsSuppressedLocallyAndForwardedExternally)
{
  using Phase181 = unity2foxglove_foxrun_interfaces_v1::msg::
    Phase181State48D288ED82F1Envelope;
  Phase181 message;
  message.foxrun_origin_id = "phase186-f-external";
  message.foxrun_sequence = 18602U;
  message.foxrun_stamp.sec = 186;
  message.foxrun_stamp.nanosec = 987654321U;
  message.payload.bytes = {0x18U, 0x6fU};
  message.payload.foxrun_has_bytes = true;
  message.payload.count = 186;
  message.payload.kind = 2U;
  message.payload.message = "phase181-duplex";
  message.payload.foxrun_has_message = true;
  message.payload.nested.enabled = true;
  message.payload.nested.label = "generated-cdr";
  message.payload.nested.foxrun_has_label = true;
  message.payload.foxrun_has_nested = true;
  message.payload.optional_count = 7;
  message.payload.foxrun_has_optional_count = true;
  message.payload.optional_text = "exact";
  message.payload.foxrun_has_optional_text = true;
  message.payload.values = {-1, 0, 1};
  message.payload.foxrun_has_values = true;

  VerifyTypedDuplex(
    "phase181",
    "/phase186/f/phase181",
    "unity2foxglove_foxrun_interfaces_v1/msg/"
    "Phase181State48D288ED82F1Envelope",
    18602U,
    message);
}
}  // namespace
