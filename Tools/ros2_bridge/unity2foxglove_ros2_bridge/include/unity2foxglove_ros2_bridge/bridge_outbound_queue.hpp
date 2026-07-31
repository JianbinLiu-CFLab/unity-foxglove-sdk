// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/ros2_bridge
// Purpose: Bounded ROS 2 serialized-message admission into U2R2.

#pragma once

#include <cstddef>
#include <cstdint>
#include <functional>
#include <memory>
#include <string>

#include "unity2foxglove_ros2_bridge/bridge_origin.hpp"
#include "unity2foxglove_ros2_bridge/u2r2_protocol_authority.hpp"

namespace unity2foxglove::ros2_bridge::runtime
{
class BridgeWriterCore;

enum class BridgeSerializedAdmission
{
  accepted = 1,
  inactive = 2,
  unsupported_representation = 3,
  payload_too_large = 4,
  capacity_rejected = 5,
  suppressed_local = 6,
  invalid_origin = 7,
};

struct BridgeOutboundQueueStats
{
  uint64_t accepted{0};
  uint64_t inactive{0};
  uint64_t unsupported_representation{0};
  uint64_t payload_too_large{0};
  uint64_t capacity_rejected{0};
  uint64_t suppressed_local{0};
  uint64_t invalid_origin{0};
};

class BridgeSubscriptionGate final
{
public:
  ~BridgeSubscriptionGate();
  BridgeSubscriptionGate(const BridgeSubscriptionGate &) = delete;
  BridgeSubscriptionGate & operator=(const BridgeSubscriptionGate &) = delete;

private:
  friend class BridgeOutboundQueue;
  explicit BridgeSubscriptionGate(std::shared_ptr<void> state);
  std::shared_ptr<void> state_;
};

using BridgeSerializedCallback = std::function<BridgeSerializedAdmission(
    const uint8_t *,
    size_t,
    uint64_t,
    BridgeSampleOrigin)>;

class BridgeOutboundQueue final
{
public:
  BridgeOutboundQueue(
    const u2r2::ProtocolLimits & limits,
    BridgeWriterCore & writer,
    u2r2::ContractAuthority & contracts,
    std::string session_id,
    uint64_t connection_generation);
  ~BridgeOutboundQueue();
  BridgeOutboundQueue(const BridgeOutboundQueue &) = delete;
  BridgeOutboundQueue & operator=(const BridgeOutboundQueue &) = delete;

  std::shared_ptr<BridgeSubscriptionGate> create_gate(
    const u2r2::ContractIdentity & identity);
  void activate(const std::shared_ptr<BridgeSubscriptionGate> & gate);
  void revoke(const std::shared_ptr<BridgeSubscriptionGate> & gate);
  BridgeSerializedCallback callback(
    const std::shared_ptr<BridgeSubscriptionGate> & gate);
  BridgeSerializedAdmission enqueue(
    const std::shared_ptr<BridgeSubscriptionGate> & gate,
    const uint8_t * payload,
    size_t payload_size,
    uint64_t receive_time_ns,
    BridgeSampleOrigin origin = BridgeSampleOrigin::external);
  BridgeOutboundQueueStats stats() const;
  void close();

private:
  struct State;
  static std::shared_ptr<void> GateStateOf(
    const std::shared_ptr<BridgeSubscriptionGate> & gate);
  static BridgeSerializedAdmission Enqueue(
    const std::shared_ptr<State> & state,
    const std::shared_ptr<BridgeSubscriptionGate> & gate,
    const uint8_t * payload,
    size_t payload_size,
    uint64_t receive_time_ns,
    BridgeSampleOrigin origin);
  std::shared_ptr<State> state_;
};
}  // namespace unity2foxglove::ros2_bridge::runtime
