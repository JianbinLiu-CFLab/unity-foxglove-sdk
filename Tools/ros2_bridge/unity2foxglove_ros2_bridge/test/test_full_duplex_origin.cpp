// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/ros2_bridge
// Purpose: RED-first full-duplex Bridge publisher-origin suppression tests.

#ifdef _WIN32
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <winsock2.h>
#include <ws2tcpip.h>
#endif

#include <gtest/gtest.h>

#include <algorithm>
#include <chrono>
#include <cstdint>
#include <cstring>
#include <memory>
#include <string>
#include <thread>
#include <vector>

#include "unity2foxglove_ros2_bridge/bridge_origin.hpp"
#include "unity2foxglove_ros2_bridge/bridge_writer.hpp"

#define UNITY2FOXGLOVE_ROS2_BRIDGE_TESTING
#include "../src/unity2foxglove_ros2_bridge.cpp"

namespace
{
namespace runtime = unity2foxglove::ros2_bridge::runtime;
namespace u2r2 = unity2foxglove::ros2_bridge::u2r2;

using namespace std::chrono_literals;

rmw_gid_t MakeGid(uint8_t seed)
{
  rmw_gid_t gid{};
  gid.implementation_identifier = rmw_get_implementation_identifier();
  for (size_t index = 0; index < RMW_GID_STORAGE_SIZE; ++index) {
    gid.data[index] = static_cast<uint8_t>(seed + index);
  }
  return gid;
}

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

TEST(BridgeOriginRegistry, ClassifiesLocalExternalMissingAndAmbiguousGids)
{
  runtime::BridgeOriginRegistry origins(4U);
  const auto local = MakeGid(0x10U);
  const auto external = MakeGid(0x80U);
  rmw_gid_t missing{};

  EXPECT_EQ(runtime::BridgeSampleOrigin::missing, origins.classify(missing));
  EXPECT_EQ(runtime::BridgeSampleOrigin::external, origins.classify(external));

  origins.register_local(local);
  EXPECT_EQ(1U, origins.size());
  EXPECT_EQ(runtime::BridgeSampleOrigin::local, origins.classify(local));
  EXPECT_EQ(runtime::BridgeSampleOrigin::external, origins.classify(external));

  origins.register_local(local);
  EXPECT_EQ(2U, origins.size());
  EXPECT_EQ(runtime::BridgeSampleOrigin::ambiguous, origins.classify(local));
}

TEST(BridgeOriginRegistry, ReconnectOwnsOnlyTheCurrentPublisherGeneration)
{
  const auto first_gid = MakeGid(0x20U);
  const auto second_gid = MakeGid(0x40U);
  std::weak_ptr<runtime::BridgeOriginRegistry> retired;
  {
    auto first =
      std::make_shared<runtime::BridgeOriginRegistry>(2U);
    first->register_local(first_gid);
    retired = first;
    EXPECT_EQ(runtime::BridgeSampleOrigin::local, first->classify(first_gid));
  }
  EXPECT_TRUE(retired.expired());

  runtime::BridgeOriginRegistry replacement(2U);
  replacement.register_local(second_gid);
  EXPECT_EQ(1U, replacement.size());
  EXPECT_EQ(
    runtime::BridgeSampleOrigin::external,
    replacement.classify(first_gid));
  EXPECT_EQ(
    runtime::BridgeSampleOrigin::local,
    replacement.classify(second_gid));
}

TEST(BridgeOriginRegistry, InvalidOriginCountsAreSeparateFromQueuePressure)
{
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
    "phase186d-origin-session",
    7U);
  const u2r2::ContractIdentity identity(
    u2r2::ContractKey(41U, 7U),
    u2r2::ContractDirection::subscribe,
    "/phase186/d/origin",
    "std_msgs/msg/String",
    DefaultQos());
  auto response = replay.admit(
    1U,
    {0x01U},
    64U,
    writer.scheduler());
  auto registration = contracts.begin_registration(
    identity,
    writer.scheduler(),
    replay,
    response);
  contracts.commit_ready(
    registration,
    replay,
    response,
    u2r2::OutboundFrame::control("ready", {0xa1U}));
  auto ready = writer.try_begin_write(writer_lease);
  ASSERT_TRUE(ready.has_value());
  ready->release();
  auto gate = queue.create_gate(identity);
  queue.activate(gate);

  const std::vector<uint8_t> payload{
    0x00U, 0x01U, 0x00U, 0x00U, 0x02U, 0x00U, 0x00U, 0x00U,
    0x41U, 0x00U};
  EXPECT_EQ(
    runtime::BridgeSerializedAdmission::suppressed_local,
    queue.enqueue(
      gate,
      payload.data(),
      payload.size(),
      1U,
      runtime::BridgeSampleOrigin::local));
  EXPECT_EQ(
    runtime::BridgeSerializedAdmission::invalid_origin,
    queue.enqueue(
      gate,
      payload.data(),
      payload.size(),
      2U,
      runtime::BridgeSampleOrigin::missing));
  EXPECT_EQ(
    runtime::BridgeSerializedAdmission::invalid_origin,
    queue.enqueue(
      gate,
      payload.data(),
      payload.size(),
      3U,
      runtime::BridgeSampleOrigin::ambiguous));
  EXPECT_FALSE(writer.try_begin_write(writer_lease).has_value());

  const auto stats = queue.stats();
  EXPECT_EQ(1U, stats.suppressed_local);
  EXPECT_EQ(2U, stats.invalid_origin);
  EXPECT_EQ(0U, stats.capacity_rejected);
  EXPECT_EQ(0U, stats.accepted);
}

TEST(
  BridgeOriginRegistry,
  ProcessOwnedPublisherIsSuppressedAndByteIdenticalExternalPublisherIsForwarded)
{
  auto context = std::make_shared<rclcpp::Context>();
  context->init(0, nullptr);
  bridge_runtime::ProcessRosOwner ros_owner(
    "phase186_d_full_duplex_origin_probe",
    context);

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
    "phase186d-duplex-session",
    9U);
  const std::string topic = "/phase186/d/full_duplex_origin";
  const std::string schema = "std_msgs/msg/String";
  const u2r2::ContractIdentity identity(
    u2r2::ContractKey(51U, 9U),
    u2r2::ContractDirection::subscribe,
    topic,
    schema,
    DefaultQos());
  auto response = replay.admit(
    1U,
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
  local.payload = {
    0x00U, 0x01U, 0x00U, 0x00U, 0x02U, 0x00U, 0x00U, 0x00U,
    0x41U, 0x00U};
  bridge->prepare(local);
  auto subscription = bridge->subscribe(identity, queue.callback(gate));
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
    "phase186_d_external_origin_probe",
    options);
  auto external_publisher = external->create_generic_publisher(
    topic,
    schema,
    rclcpp::QoS(10).reliable());
  ASSERT_TRUE(WaitUntil([&]() {
      return external_publisher->get_subscription_count() > 0U;
    }));
  rclcpp::SerializedMessage serialized(local.payload.size());
  auto & raw = serialized.get_rcl_serialized_message();
  ASSERT_GE(raw.buffer_capacity, local.payload.size());
  std::memcpy(raw.buffer, local.payload.data(), local.payload.size());
  raw.buffer_length = local.payload.size();
  external_publisher->publish(serialized);
  ASSERT_TRUE(WaitUntil([&]() {return queue.stats().accepted == 1U;}));

  auto message = writer.try_begin_write(writer_lease);
  ASSERT_TRUE(message.has_value());
  const auto frame = u2r2::decode_frame(message->frame().bytes());
  const auto parsed = u2r2::parse_v2(frame);
  EXPECT_EQ(u2r2::Operation::Message, parsed.operation);
  EXPECT_EQ(51U, parsed.contract_id);
  EXPECT_EQ(local.payload, frame.payload);
  message->release();

  queue.revoke(gate);
  subscription.reset();
  external_publisher.reset();
  external.reset();
  bridge.reset();
  EXPECT_TRUE(ros_owner.stop());
  context->shutdown("Phase186-D full-duplex origin probe complete");
}
}  // namespace
