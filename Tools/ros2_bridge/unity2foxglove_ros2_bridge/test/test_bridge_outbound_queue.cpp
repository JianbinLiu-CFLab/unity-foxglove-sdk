// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/ros2_bridge
// Purpose: RED-first bounded serialized subscription admission tests.

#include <cstdint>
#include <memory>
#include <string>
#include <utility>
#include <vector>

#include <gtest/gtest.h>

#include "unity2foxglove_ros2_bridge/bridge_outbound_queue.hpp"
#include "unity2foxglove_ros2_bridge/bridge_writer.hpp"
#include "unity2foxglove_ros2_bridge/u2r2_protocol.hpp"
#include "unity2foxglove_ros2_bridge/u2r2_protocol_authority.hpp"

namespace
{
namespace runtime = unity2foxglove::ros2_bridge::runtime;
namespace u2r2 = unity2foxglove::ros2_bridge::u2r2;

u2r2::Qos DefaultQos()
{
  return u2r2::Qos{
    "default",
    "reliable",
    "volatile",
    "keep_last",
    10};
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

class OutboundQueueHarness final
{
public:
  OutboundQueueHarness()
  : limits(u2r2::ProtocolLimits::defaults()),
    writer(limits),
    replay(limits),
    contracts(limits, SemanticErrorFrame),
    queue(limits, writer, contracts, "phase186d-session", 7U)
  {
    auto attached = writer.try_attach_writer();
    if (!attached) {
      throw std::logic_error("test writer lease was unavailable");
    }
    writer_lease = std::move(*attached);
  }

  std::shared_ptr<runtime::BridgeSubscriptionGate> Register(
    uint64_t contract_id,
    const std::string & topic)
  {
    const u2r2::ContractIdentity identity(
      u2r2::ContractKey(contract_id, 7U),
      u2r2::ContractDirection::subscribe,
      topic,
      "std_msgs/msg/String",
      DefaultQos());
    const auto request_id = next_request_id++;
    auto response = replay.admit(
      request_id,
      {static_cast<uint8_t>(request_id)},
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
      u2r2::OutboundFrame::control(
        "ready:" + std::to_string(contract_id),
        {0xa1U}));
    auto ready = writer.try_begin_write(writer_lease);
    if (!ready) {
      throw std::logic_error("registration response was not queued");
    }
    ready->release();
    auto gate = queue.create_gate(identity);
    queue.activate(gate);
    return gate;
  }

  std::optional<u2r2::WriteLease> TryWrite()
  {
    return writer.try_begin_write(writer_lease);
  }

  const u2r2::ProtocolLimits limits;
  runtime::BridgeWriterCore writer;
  runtime::BridgeWriterLease writer_lease;
  u2r2::RequestReplayAuthority replay;
  u2r2::ContractAuthority contracts;
  runtime::BridgeOutboundQueue queue;
  uint64_t next_request_id{1U};
};

const std::vector<uint8_t> kPayload{
  0x00U, 0x01U, 0x00U, 0x00U, 0x02U, 0x00U, 0x00U, 0x00U,
  0x41U, 0x00U};

TEST(BridgeOutboundQueue, CopiesExactCdrIntoCanonicalMessageFrame)
{
  OutboundQueueHarness harness;
  auto gate = harness.Register(41U, "/phase186/v2/input");

  EXPECT_EQ(
    runtime::BridgeSerializedAdmission::accepted,
    harness.queue.enqueue(
      gate,
      kPayload.data(),
      kPayload.size(),
      1700186000000000100ULL));

  auto write = harness.TryWrite();
  ASSERT_TRUE(write.has_value());
  const auto frame = u2r2::decode_frame(write->frame().bytes());
  const auto message = u2r2::parse_v2(frame);
  EXPECT_EQ(u2r2::Operation::Message, message.operation);
  EXPECT_EQ("phase186d-session", message.session_id);
  EXPECT_EQ(7U, message.connection_generation);
  EXPECT_EQ(41U, message.contract_id);
  EXPECT_EQ(1U, message.message_id);
  EXPECT_EQ(1U, message.sequence);
  EXPECT_EQ(1700186000000000100ULL, message.receive_time_ns);
  EXPECT_EQ("/phase186/v2/input", message.topic);
  EXPECT_EQ("std_msgs/msg/String", message.schema_name);
  EXPECT_EQ("cdr", message.encoding);
  EXPECT_EQ("xcdr1-le", message.representation);
  EXPECT_EQ(kPayload, frame.payload);
  write->release();
}

TEST(BridgeOutboundQueue, RejectsInactiveOversizeAndUnsupportedBeforeQueueing)
{
  OutboundQueueHarness harness;
  const u2r2::ContractIdentity identity(
    u2r2::ContractKey(42U, 7U),
    u2r2::ContractDirection::subscribe,
    "/phase186/v2/rejected",
    "std_msgs/msg/String",
    DefaultQos());
  auto inactive = harness.queue.create_gate(identity);

  EXPECT_EQ(
    runtime::BridgeSerializedAdmission::inactive,
    harness.queue.enqueue(
      inactive,
      kPayload.data(),
      kPayload.size(),
      1U));

  auto active = harness.Register(43U, "/phase186/v2/active");
  const std::vector<uint8_t> unsupported{
    0x00U, 0x00U, 0x00U, 0x00U, 0x01U};
  EXPECT_EQ(
    runtime::BridgeSerializedAdmission::unsupported_representation,
    harness.queue.enqueue(
      active,
      unsupported.data(),
      unsupported.size(),
      2U));
  EXPECT_EQ(
    runtime::BridgeSerializedAdmission::payload_too_large,
    harness.queue.enqueue(
      active,
      nullptr,
      harness.limits.max_payload_bytes() + 1U,
      3U));
  EXPECT_FALSE(harness.TryWrite().has_value());

  const auto stats = harness.queue.stats();
  EXPECT_EQ(1U, stats.inactive);
  EXPECT_EQ(1U, stats.unsupported_representation);
  EXPECT_EQ(1U, stats.payload_too_large);
}

TEST(BridgeOutboundQueue, CapacityRejectionDoesNotConsumeContractSequence)
{
  OutboundQueueHarness harness;
  auto gate = harness.Register(44U, "/phase186/v2/pressure");
  const auto depth = harness.limits.max_per_contract_queue_depth();
  for (uint64_t index = 0; index < depth; ++index) {
    ASSERT_EQ(
      runtime::BridgeSerializedAdmission::accepted,
      harness.queue.enqueue(
        gate,
        kPayload.data(),
        kPayload.size(),
        index + 1U));
  }
  EXPECT_EQ(
    runtime::BridgeSerializedAdmission::capacity_rejected,
    harness.queue.enqueue(
      gate,
      kPayload.data(),
      kPayload.size(),
      depth + 1U));

  uint64_t last_sequence = 0;
  for (uint64_t index = 0; index < depth; ++index) {
    auto write = harness.TryWrite();
    ASSERT_TRUE(write.has_value());
    const auto parsed = u2r2::parse_v2(
      u2r2::decode_frame(write->frame().bytes()));
    EXPECT_EQ(index + 1U, parsed.sequence);
    last_sequence = parsed.sequence;
    write->release();
  }
  ASSERT_EQ(depth, last_sequence);

  ASSERT_EQ(
    runtime::BridgeSerializedAdmission::accepted,
    harness.queue.enqueue(
      gate,
      kPayload.data(),
      kPayload.size(),
      depth + 2U));
  auto write = harness.TryWrite();
  ASSERT_TRUE(write.has_value());
  EXPECT_EQ(
    depth + 1U,
    u2r2::parse_v2(
      u2r2::decode_frame(write->frame().bytes())).sequence);
  write->release();
}

TEST(BridgeOutboundQueue, EveryRejectedAdmissionPreservesAcceptedSequence)
{
  OutboundQueueHarness harness;
  const u2r2::ContractIdentity inactive_identity(
    u2r2::ContractKey(47U, 7U),
    u2r2::ContractDirection::subscribe,
    "/phase187/v2/inactive",
    "std_msgs/msg/String",
    DefaultQos());
  auto inactive = harness.queue.create_gate(inactive_identity);
  auto active = harness.Register(48U, "/phase187/v2/sequence");
  const std::vector<uint8_t> unsupported{
    0x00U, 0x00U, 0x00U, 0x00U, 0x01U};

  EXPECT_EQ(
    runtime::BridgeSerializedAdmission::inactive,
    harness.queue.enqueue(
      inactive,
      kPayload.data(),
      kPayload.size(),
      1U));
  EXPECT_EQ(
    runtime::BridgeSerializedAdmission::unsupported_representation,
    harness.queue.enqueue(
      active,
      unsupported.data(),
      unsupported.size(),
      2U));
  EXPECT_EQ(
    runtime::BridgeSerializedAdmission::payload_too_large,
    harness.queue.enqueue(
      active,
      nullptr,
      harness.limits.max_payload_bytes() + 1U,
      3U));
  EXPECT_EQ(
    runtime::BridgeSerializedAdmission::suppressed_local,
    harness.queue.enqueue(
      active,
      kPayload.data(),
      kPayload.size(),
      4U,
      runtime::BridgeSampleOrigin::local));
  EXPECT_EQ(
    runtime::BridgeSerializedAdmission::invalid_origin,
    harness.queue.enqueue(
      active,
      kPayload.data(),
      kPayload.size(),
      5U,
      runtime::BridgeSampleOrigin::missing));

  const auto depth = harness.limits.max_per_contract_queue_depth();
  for (uint64_t index = 0U; index < depth; ++index) {
    ASSERT_EQ(
      runtime::BridgeSerializedAdmission::accepted,
      harness.queue.enqueue(
        active,
        kPayload.data(),
        kPayload.size(),
        index + 6U));
  }
  EXPECT_EQ(
    runtime::BridgeSerializedAdmission::capacity_rejected,
    harness.queue.enqueue(
      active,
      kPayload.data(),
      kPayload.size(),
      depth + 6U));

  for (uint64_t sequence = 1U; sequence <= depth; ++sequence) {
    auto write = harness.TryWrite();
    ASSERT_TRUE(write.has_value());
    EXPECT_EQ(
      sequence,
      u2r2::parse_v2(
        u2r2::decode_frame(write->frame().bytes())).sequence);
    write->release();
  }
  ASSERT_EQ(
    runtime::BridgeSerializedAdmission::accepted,
    harness.queue.enqueue(
      active,
      kPayload.data(),
      kPayload.size(),
      depth + 7U));
  auto write = harness.TryWrite();
  ASSERT_TRUE(write.has_value());
  EXPECT_EQ(
    depth + 1U,
    u2r2::parse_v2(
      u2r2::decode_frame(write->frame().bytes())).sequence);
  write->release();
}

TEST(BridgeOutboundQueue, PreservesPerContractFifoAndRoundRobinFairness)
{
  OutboundQueueHarness harness;
  auto first = harness.Register(45U, "/phase186/v2/first");
  auto second = harness.Register(46U, "/phase186/v2/second");
  ASSERT_EQ(
    runtime::BridgeSerializedAdmission::accepted,
    harness.queue.enqueue(first, kPayload.data(), kPayload.size(), 1U));
  ASSERT_EQ(
    runtime::BridgeSerializedAdmission::accepted,
    harness.queue.enqueue(first, kPayload.data(), kPayload.size(), 2U));
  ASSERT_EQ(
    runtime::BridgeSerializedAdmission::accepted,
    harness.queue.enqueue(second, kPayload.data(), kPayload.size(), 3U));
  ASSERT_EQ(
    runtime::BridgeSerializedAdmission::accepted,
    harness.queue.enqueue(second, kPayload.data(), kPayload.size(), 4U));

  std::vector<std::pair<uint64_t, uint64_t>> order;
  for (int index = 0; index < 4; ++index) {
    auto write = harness.TryWrite();
    ASSERT_TRUE(write.has_value());
    const auto parsed = u2r2::parse_v2(
      u2r2::decode_frame(write->frame().bytes()));
    order.emplace_back(parsed.contract_id, parsed.sequence);
    write->release();
  }
  EXPECT_EQ(
    (std::vector<std::pair<uint64_t, uint64_t>>{
      {45U, 1U},
      {46U, 1U},
      {45U, 2U},
      {46U, 2U}}),
    order);
}
}  // namespace
