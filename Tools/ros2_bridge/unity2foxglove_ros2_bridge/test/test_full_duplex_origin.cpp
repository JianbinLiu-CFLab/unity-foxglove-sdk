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
#include <array>
#include <atomic>
#include <chrono>
#include <cstdint>
#include <cstring>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <tuple>
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

TEST(
  BridgeGenerationOwnership,
  ConsecutiveBridgeNodesRetirePublisherBeforeOriginRegistry)
{
  struct LifecycleEvidence
  {
    size_t created{0U};
    size_t retired{0U};
    rmw_gid_t gid{};
    std::weak_ptr<rclcpp::GenericPublisher> publisher;
    std::weak_ptr<runtime::BridgeOriginRegistry> registry;
    bool publisher_expired_at_retirement{false};
    bool registry_alive_at_retirement{false};
    runtime::BridgeSampleOrigin retired_classification{
      runtime::BridgeSampleOrigin::missing};
  };

  const auto observe = [](LifecycleEvidence & evidence) {
      return [&evidence](
        BridgePublisherLifecycleStage stage,
        const rmw_gid_t & gid,
        std::weak_ptr<rclcpp::GenericPublisher> publisher,
        std::weak_ptr<runtime::BridgeOriginRegistry> registry) {
          evidence.gid = gid;
          evidence.publisher = publisher;
          evidence.registry = registry;
          if (stage == BridgePublisherLifecycleStage::created) {
            ++evidence.created;
            return;
          }
          ++evidence.retired;
          evidence.publisher_expired_at_retirement = publisher.expired();
          evidence.registry_alive_at_retirement = !registry.expired();
          if (auto origin = registry.lock()) {
            evidence.retired_classification = origin->classify(gid);
          }
        };
    };
  const auto frame_for = [](const std::string & topic) {
      BridgeFrame frame;
      frame.topic = topic;
      frame.schema_name = "std_msgs/msg/String";
      frame.encoding = "cdr";
      frame.profile = "default";
      frame.reliability = "reliable";
      frame.durability = "volatile";
      frame.history = "keep_last";
      frame.depth = 10;
      frame.payload = {
        0x00U, 0x01U, 0x00U, 0x00U, 0x02U, 0x00U, 0x00U, 0x00U,
        0x41U, 0x00U};
      return frame;
    };

  auto context = std::make_shared<rclcpp::Context>();
  context->init(0, nullptr);
  bridge_runtime::ProcessRosOwner ros_owner(
    "phase187_generation_origin_probe",
    context);
  runtime::ProcessConnectionAuthority authority(
    u2r2::ProtocolLimits::defaults());

  LifecycleEvidence first_evidence;
  auto first_data =
    authority.try_acquire_role(u2r2::ConnectionRole::data_session);
  ASSERT_TRUE(first_data.has_value());
  runtime::GenerationOwnership first_generation(std::move(*first_data));
  auto first_bridge = std::make_unique<BridgeNode>(
    ros_owner.require_node(),
    PayloadFormat::CdrWithEncapsulation,
    u2r2::ProtocolLimits::defaults().max_contracts(),
    BridgeAdmissionDiagnosticSink{},
    observe(first_evidence));
  auto & first = first_generation.adopt_entities(std::move(first_bridge));
  const auto first_registry = first.origin_registry_for_testing();
  auto first_frame = frame_for("/phase187/generation/first");
  first.prepare(first_frame);
  ASSERT_EQ(1U, first_evidence.created);
  ASSERT_FALSE(first_evidence.publisher.expired());
  first.publish_prepared(first_frame);
  {
    auto registry = first_registry.lock();
    ASSERT_TRUE(registry);
    EXPECT_EQ(1U, registry->size());
    EXPECT_EQ(
      runtime::BridgeSampleOrigin::local,
      registry->classify(first_evidence.gid));
  }

  EXPECT_TRUE(first_generation.release());
  EXPECT_EQ(1U, first_evidence.retired);
  EXPECT_TRUE(first_evidence.publisher_expired_at_retirement);
  EXPECT_TRUE(first_evidence.registry_alive_at_retirement);
  EXPECT_EQ(
    runtime::BridgeSampleOrigin::local,
    first_evidence.retired_classification);
  EXPECT_TRUE(first_evidence.publisher.expired());
  EXPECT_TRUE(first_registry.expired());

  LifecycleEvidence second_evidence;
  auto second_data =
    authority.try_acquire_role(u2r2::ConnectionRole::data_session);
  ASSERT_TRUE(second_data.has_value());
  runtime::GenerationOwnership second_generation(std::move(*second_data));
  auto second_bridge = std::make_unique<BridgeNode>(
    ros_owner.require_node(),
    PayloadFormat::CdrWithEncapsulation,
    u2r2::ProtocolLimits::defaults().max_contracts(),
    BridgeAdmissionDiagnosticSink{},
    observe(second_evidence));
  auto & second = second_generation.adopt_entities(std::move(second_bridge));
  const auto second_registry = second.origin_registry_for_testing();
  {
    auto registry = second_registry.lock();
    ASSERT_TRUE(registry);
    EXPECT_EQ(0U, registry->size());
    EXPECT_EQ(
      runtime::BridgeSampleOrigin::external,
      registry->classify(first_evidence.gid));
  }

  auto second_frame = frame_for("/phase187/generation/second");
  second.prepare(second_frame);
  ASSERT_EQ(1U, second_evidence.created);
  ASSERT_FALSE(second_evidence.publisher.expired());
  EXPECT_NE(
    0,
    std::memcmp(
      first_evidence.gid.data,
      second_evidence.gid.data,
      RMW_GID_STORAGE_SIZE));
  second.publish_prepared(second_frame);
  {
    auto registry = second_registry.lock();
    ASSERT_TRUE(registry);
    EXPECT_EQ(1U, registry->size());
    EXPECT_EQ(
      runtime::BridgeSampleOrigin::external,
      registry->classify(first_evidence.gid));
    EXPECT_EQ(
      runtime::BridgeSampleOrigin::local,
      registry->classify(second_evidence.gid));
  }

  EXPECT_TRUE(second_generation.release());
  EXPECT_EQ(1U, second_evidence.retired);
  EXPECT_TRUE(second_evidence.publisher_expired_at_retirement);
  EXPECT_TRUE(second_evidence.registry_alive_at_retirement);
  EXPECT_EQ(
    runtime::BridgeSampleOrigin::local,
    second_evidence.retired_classification);
  EXPECT_TRUE(second_evidence.publisher.expired());
  EXPECT_TRUE(second_registry.expired());
  EXPECT_TRUE(ros_owner.stop());
  context->shutdown("Phase187 generation origin probe complete");
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
  BridgeAdmissionDiagnostics,
  ProductionSubscriptionCallbackReportsEveryRejectionAtBoundedIntervals)
{
  auto context = std::make_shared<rclcpp::Context>();
  context->init(0, nullptr);
  bridge_runtime::ProcessRosOwner ros_owner(
    "phase187_admission_diagnostic_probe",
    context);

  using Admission = runtime::BridgeSerializedAdmission;
  using Diagnostic = std::tuple<std::string, Admission, uint64_t>;
  std::mutex diagnostic_mutex;
  std::vector<Diagnostic> diagnostics;
  BridgeNode bridge(
    ros_owner.require_node(),
    PayloadFormat::CdrWithEncapsulation,
    u2r2::ProtocolLimits::defaults().max_contracts(),
    [&](const std::string & topic, Admission admission, uint64_t count) {
      std::lock_guard<std::mutex> lock(diagnostic_mutex);
      diagnostics.emplace_back(topic, admission, count);
    });

  const std::string topic = "/phase187/admission_diagnostics";
  const std::string schema = "std_msgs/msg/String";
  const u2r2::ContractIdentity identity(
    u2r2::ContractKey(52U, 10U),
    u2r2::ContractDirection::subscribe,
    topic,
    schema,
    DefaultQos());
  const std::array<Admission, 7U> dispositions{
    Admission::inactive,
    Admission::unsupported_representation,
    Admission::payload_too_large,
    Admission::capacity_rejected,
    Admission::suppressed_local,
    Admission::invalid_origin,
    Admission::accepted};
  constexpr size_t kSamplesPerDisposition = 5U;
  std::atomic<size_t> callback_count{0U};
  auto subscription = bridge.subscribe(
    identity,
    [&](const uint8_t *, size_t, uint64_t, runtime::BridgeSampleOrigin) {
      const auto index = callback_count.fetch_add(1U);
      return dispositions[index / kSamplesPerDisposition];
    });
  ASSERT_TRUE(subscription);

  rclcpp::NodeOptions options;
  options.context(context);
  auto external = std::make_shared<rclcpp::Node>(
    "phase187_admission_diagnostic_external",
    options);
  auto publisher = external->create_generic_publisher(
    topic,
    schema,
    rclcpp::QoS(10).reliable());
  ASSERT_TRUE(WaitUntil([&]() {
      return publisher->get_subscription_count() > 0U;
    }));

  const std::vector<uint8_t> payload{
    0x00U, 0x01U, 0x00U, 0x00U, 0x02U, 0x00U, 0x00U, 0x00U,
    0x41U, 0x00U};
  rclcpp::SerializedMessage serialized(payload.size());
  auto & raw = serialized.get_rcl_serialized_message();
  ASSERT_GE(raw.buffer_capacity, payload.size());
  std::memcpy(raw.buffer, payload.data(), payload.size());
  raw.buffer_length = payload.size();

  const auto sample_count = dispositions.size() * kSamplesPerDisposition;
  for (size_t index = 0U; index < sample_count; ++index) {
    publisher->publish(serialized);
    ASSERT_TRUE(WaitUntil([&]() {
        return callback_count.load() > index;
      }));
  }

  std::vector<Diagnostic> observed;
  {
    std::lock_guard<std::mutex> lock(diagnostic_mutex);
    observed = diagnostics;
  }
  ASSERT_EQ(18U, observed.size());
  for (size_t disposition_index = 0U;
    disposition_index + 1U < dispositions.size();
    ++disposition_index)
  {
    std::vector<uint64_t> counts;
    for (const auto & diagnostic : observed) {
      if (std::get<1>(diagnostic) == dispositions[disposition_index]) {
        EXPECT_EQ(topic, std::get<0>(diagnostic));
        counts.push_back(std::get<2>(diagnostic));
      }
    }
    EXPECT_EQ((std::vector<uint64_t>{1U, 2U, 4U}), counts);
  }
  EXPECT_TRUE(std::none_of(
      observed.begin(),
      observed.end(),
      [](const Diagnostic & diagnostic) {
        return std::get<1>(diagnostic) == Admission::accepted;
      }));

  publisher.reset();
  external.reset();
  subscription.reset();
  EXPECT_TRUE(ros_owner.stop());
  context->shutdown("Phase187 admission diagnostic probe complete");
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
