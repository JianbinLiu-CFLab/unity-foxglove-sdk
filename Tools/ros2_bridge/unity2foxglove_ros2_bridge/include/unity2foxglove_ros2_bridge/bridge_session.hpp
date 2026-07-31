// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/ros2_bridge
// Purpose: Frozen U2R2 first-frame, identity, and replay session integration.

#pragma once

#include <chrono>
#include <cstdint>
#include <functional>
#include <memory>
#include <optional>
#include <string>
#include <vector>

#include "unity2foxglove_ros2_bridge/bridge_outbound_queue.hpp"
#include "unity2foxglove_ros2_bridge/u2r2_protocol.hpp"
#include "unity2foxglove_ros2_bridge/u2r2_protocol_authority.hpp"

namespace unity2foxglove::ros2_bridge::runtime
{
enum class FirstFrameRole
{
  data_session = 1,
  probe = 2,
};

struct FirstFrameClassification
{
  u2r2::Dialect dialect{u2r2::Dialect::None};
  FirstFrameRole role{FirstFrameRole::data_session};
  bool one_shot{false};
  std::optional<u2r2::Message> v2_message;
  std::optional<u2r2::LegacyV1Message> legacy_message;
};

struct ReplayMutationResult
{
  std::vector<uint8_t> exact_response;
  bool is_error{false};

  static ReplayMutationResult success(std::vector<uint8_t> exact_response);
  static ReplayMutationResult error(std::vector<uint8_t> exact_response);
};

enum class BridgeSubscriptionCommand
{
  applied = 1,
  replayed = 2,
  rejected = 3,
};

using BridgeSubscriptionFactory = std::function<std::shared_ptr<void>(
    const u2r2::ContractIdentity &,
    BridgeSerializedCallback)>;

class BridgeSessionProtocol final
{
public:
  explicit BridgeSessionProtocol(const u2r2::ProtocolLimits & limits);
  ~BridgeSessionProtocol();
  BridgeSessionProtocol(BridgeSessionProtocol &&) noexcept;
  BridgeSessionProtocol & operator=(BridgeSessionProtocol &&) noexcept;
  BridgeSessionProtocol(const BridgeSessionProtocol &) = delete;
  BridgeSessionProtocol & operator=(const BridgeSessionProtocol &) = delete;

  FirstFrameClassification accept_first_frame(
    const std::vector<uint8_t> & wire_bytes);
  void bind_v2_identity(u2r2::SessionIdentity identity);
  u2r2::Message parse_v2_request(
    const std::vector<uint8_t> & wire_bytes) const;

  const std::string & session_id() const;
  uint64_t connection_generation() const;
  u2r2::Dialect dialect() const noexcept;

  void require_publisher_capacity(
    const u2r2::Message & preparation) const;
  void mark_publisher_ready(const u2r2::Message & preparation);
  void require_publisher_ready(const u2r2::Message & publish) const;

  u2r2::ReplayDecision execute_replayable(
    const std::vector<uint8_t> & request_wire,
    const u2r2::Message & request,
    uint64_t maximum_response_bytes,
    const std::function<ReplayMutationResult()> & mutation);

  BridgeSubscriptionCommand register_subscription(
    const std::vector<uint8_t> & request_wire,
    const u2r2::Message & request,
    uint64_t maximum_response_bytes,
    const BridgeSubscriptionFactory & factory);
  BridgeSubscriptionCommand unregister_subscription(
    const std::vector<uint8_t> & request_wire,
    const u2r2::Message & request,
    uint64_t maximum_response_bytes);

  std::optional<u2r2::ControlReservation> try_reserve_control(uint64_t bytes);
  std::optional<u2r2::ByteLease> try_reserve_transient(uint64_t bytes);
  std::optional<u2r2::ByteLease> try_begin_read(uint64_t bytes);
  void enqueue_control(std::string token, std::vector<uint8_t> exact_frame);
  std::optional<u2r2::WriteLease> try_begin_write();
  uint64_t wake_generation() const;
  bool wait_for_writer_change(
    uint64_t observed_generation,
    std::chrono::milliseconds timeout);
  void close();
  uint64_t transient_bytes() const;
  uint64_t in_flight_bytes() const;
  const u2r2::ProtocolLimits & limits() const noexcept;

private:
  struct Impl;
  std::unique_ptr<Impl> impl_;
};

std::vector<uint8_t> make_v2_busy_response(
  uint64_t request_id,
  const u2r2::ProtocolLimits & limits);
}  // namespace unity2foxglove::ros2_bridge::runtime
