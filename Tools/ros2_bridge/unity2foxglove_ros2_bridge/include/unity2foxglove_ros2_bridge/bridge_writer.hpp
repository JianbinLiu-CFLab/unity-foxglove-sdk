// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/ros2_bridge
// Purpose: One-writer ownership and wakeup around the bounded U2R2 scheduler.

#pragma once

#include <chrono>
#include <cstdint>
#include <memory>
#include <optional>
#include <string>
#include <vector>

#include "unity2foxglove_ros2_bridge/u2r2_protocol_authority.hpp"

namespace unity2foxglove::ros2_bridge::runtime
{
class BridgeWriterLease final
{
public:
  BridgeWriterLease() = default;
  ~BridgeWriterLease() = default;
  BridgeWriterLease(BridgeWriterLease &&) noexcept = default;
  BridgeWriterLease & operator=(BridgeWriterLease &&) noexcept = default;
  BridgeWriterLease(const BridgeWriterLease &) = delete;
  BridgeWriterLease & operator=(const BridgeWriterLease &) = delete;

  bool release();
  bool valid() const noexcept;

private:
  friend class BridgeWriterCore;
  explicit BridgeWriterLease(std::shared_ptr<void> settlement);
  std::shared_ptr<void> settlement_;
};

class BridgeWriterCore final
{
public:
  explicit BridgeWriterCore(const u2r2::ProtocolLimits & limits);
  ~BridgeWriterCore();
  BridgeWriterCore(const BridgeWriterCore &) = delete;
  BridgeWriterCore & operator=(const BridgeWriterCore &) = delete;

  std::optional<BridgeWriterLease> try_attach_writer();
  uint64_t wake_generation() const;
  bool wait_for_change(
    const BridgeWriterLease & writer,
    uint64_t observed_generation,
    std::chrono::milliseconds timeout);
  void notify();
  void begin_drain();
  void close();
  bool is_closed() const;

  std::optional<u2r2::ControlReservation> try_reserve_control(uint64_t bytes);
  std::optional<u2r2::DataReservation> try_reserve_data(
    const u2r2::ContractKey & key,
    uint64_t bytes);
  void enqueue_control(std::string token, std::vector<uint8_t> exact_frame);
  u2r2::EnqueueDisposition enqueue_data(
    u2r2::OutboundFrame frame,
    u2r2::QueueOverflowPolicy policy);
  std::optional<u2r2::ByteLease> try_reserve_transient(uint64_t bytes);
  std::optional<u2r2::ByteLease> try_begin_read(uint64_t bytes);
  std::optional<u2r2::WriteLease> try_begin_write(
    const BridgeWriterLease & writer);

  uint64_t transient_bytes() const;
  uint64_t in_flight_bytes() const;
  u2r2::BoundedOutboundScheduler & scheduler() noexcept;

private:
  struct Impl;
  std::unique_ptr<Impl> impl_;
};
}  // namespace unity2foxglove::ros2_bridge::runtime
