// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/ros2_bridge
// Purpose: One-writer ownership and wakeup around the bounded U2R2 scheduler.

#include "unity2foxglove_ros2_bridge/bridge_writer.hpp"

#include <atomic>
#include <condition_variable>
#include <mutex>
#include <stdexcept>
#include <utility>

namespace unity2foxglove::ros2_bridge::runtime
{
namespace
{
struct WriterState final
{
  mutable std::mutex mutex;
  std::condition_variable changed;
  bool closed{false};
  bool claimed{false};
  uint64_t wake_generation{0};
};

struct WriterClaim final
{
  explicit WriterClaim(std::shared_ptr<WriterState> value)
  : state(std::move(value))
  {
  }

  ~WriterClaim()
  {
    (void)release();
  }

  bool release()
  {
    bool expected = true;
    if (!active.compare_exchange_strong(expected, false)) {
      return false;
    }
    {
      const std::lock_guard<std::mutex> lock(state->mutex);
      state->claimed = false;
      ++state->wake_generation;
    }
    state->changed.notify_all();
    return true;
  }

  std::shared_ptr<WriterState> state;
  std::atomic<bool> active{true};
};

std::shared_ptr<WriterClaim> ClaimOf(const std::shared_ptr<void> & settlement)
{
  return std::static_pointer_cast<WriterClaim>(settlement);
}
}  // namespace

struct BridgeWriterCore::Impl final
{
  explicit Impl(const u2r2::ProtocolLimits & limits)
  : scheduler(limits),
    state(std::make_shared<WriterState>())
  {
  }

  u2r2::BoundedOutboundScheduler scheduler;
  std::shared_ptr<WriterState> state;
};

BridgeWriterLease::BridgeWriterLease(std::shared_ptr<void> settlement)
: settlement_(std::move(settlement))
{
}

bool BridgeWriterLease::release()
{
  if (!settlement_) {
    return false;
  }
  auto claim = std::static_pointer_cast<WriterClaim>(settlement_);
  const auto released = claim->release();
  settlement_.reset();
  return released;
}

bool BridgeWriterLease::valid() const noexcept
{
  if (!settlement_) {
    return false;
  }
  return std::static_pointer_cast<WriterClaim>(settlement_)->active.load();
}

BridgeWriterCore::BridgeWriterCore(const u2r2::ProtocolLimits & limits)
: impl_(std::make_unique<Impl>(limits))
{
}

BridgeWriterCore::~BridgeWriterCore()
{
  close();
}

std::optional<BridgeWriterLease> BridgeWriterCore::try_attach_writer()
{
  const std::lock_guard<std::mutex> lock(impl_->state->mutex);
  if (impl_->state->closed || impl_->state->claimed) {
    return std::nullopt;
  }
  impl_->state->claimed = true;
  return BridgeWriterLease(
    std::make_shared<WriterClaim>(impl_->state));
}

uint64_t BridgeWriterCore::wake_generation() const
{
  const std::lock_guard<std::mutex> lock(impl_->state->mutex);
  return impl_->state->wake_generation;
}

bool BridgeWriterCore::wait_for_change(
  const BridgeWriterLease & writer,
  uint64_t observed_generation,
  std::chrono::milliseconds timeout)
{
  if (timeout.count() < 0) {
    throw std::invalid_argument("writer wait timeout cannot be negative");
  }
  const auto claim = ClaimOf(writer.settlement_);
  if (!claim || claim->state.get() != impl_->state.get() || !claim->active.load()) {
    throw std::logic_error("writer wait requires the active session writer lease");
  }
  std::unique_lock<std::mutex> lock(impl_->state->mutex);
  (void)impl_->state->changed.wait_for(
    lock,
    timeout,
    [&]() {
      return
        impl_->state->closed ||
        !claim->active.load() ||
        impl_->state->wake_generation != observed_generation;
    });
  return
    claim->active.load() &&
    !impl_->state->closed &&
    impl_->state->wake_generation != observed_generation;
}

void BridgeWriterCore::notify()
{
  {
    const std::lock_guard<std::mutex> lock(impl_->state->mutex);
    if (impl_->state->closed) {
      return;
    }
    ++impl_->state->wake_generation;
  }
  impl_->state->changed.notify_one();
}

void BridgeWriterCore::close()
{
  {
    const std::lock_guard<std::mutex> lock(impl_->state->mutex);
    if (impl_->state->closed) {
      return;
    }
    impl_->state->closed = true;
    ++impl_->state->wake_generation;
  }
  impl_->state->changed.notify_all();
}

bool BridgeWriterCore::is_closed() const
{
  const std::lock_guard<std::mutex> lock(impl_->state->mutex);
  return impl_->state->closed;
}

std::optional<u2r2::ControlReservation>
BridgeWriterCore::try_reserve_control(uint64_t bytes)
{
  const std::lock_guard<std::mutex> lock(impl_->state->mutex);
  if (impl_->state->closed) {
    return std::nullopt;
  }
  return impl_->scheduler.try_reserve_control(bytes);
}

std::optional<u2r2::DataReservation>
BridgeWriterCore::try_reserve_data(
  const u2r2::ContractKey & key,
  uint64_t bytes)
{
  const std::lock_guard<std::mutex> lock(impl_->state->mutex);
  if (impl_->state->closed) {
    return std::nullopt;
  }
  return impl_->scheduler.try_reserve_data(key, bytes);
}

void BridgeWriterCore::enqueue_control(
  std::string token,
  std::vector<uint8_t> exact_frame)
{
  auto reservation = try_reserve_control(
    static_cast<uint64_t>(exact_frame.size()));
  if (!reservation) {
    throw u2r2::ProtocolError(
            "capacity_exceeded",
            "no control capacity remains for the sidecar response",
            false);
  }
  reservation->commit(
    u2r2::OutboundFrame::control(
      std::move(token),
      std::move(exact_frame)));
  notify();
}

u2r2::EnqueueDisposition BridgeWriterCore::enqueue_data(
  u2r2::OutboundFrame frame,
  u2r2::QueueOverflowPolicy policy)
{
  {
    const std::lock_guard<std::mutex> lock(impl_->state->mutex);
    if (impl_->state->closed) {
      return u2r2::EnqueueDisposition::rejected;
    }
  }
  const auto disposition =
    impl_->scheduler.enqueue_data(std::move(frame), policy);
  if (disposition != u2r2::EnqueueDisposition::rejected) {
    notify();
  }
  return disposition;
}

std::optional<u2r2::ByteLease>
BridgeWriterCore::try_reserve_transient(uint64_t bytes)
{
  const std::lock_guard<std::mutex> lock(impl_->state->mutex);
  if (impl_->state->closed) {
    return std::nullopt;
  }
  return impl_->scheduler.try_reserve_transient(bytes);
}

std::optional<u2r2::ByteLease>
BridgeWriterCore::try_begin_read(uint64_t bytes)
{
  const std::lock_guard<std::mutex> lock(impl_->state->mutex);
  if (impl_->state->closed) {
    return std::nullopt;
  }
  return impl_->scheduler.try_begin_read(bytes);
}

std::optional<u2r2::WriteLease> BridgeWriterCore::try_begin_write(
  const BridgeWriterLease & writer)
{
  const auto claim = ClaimOf(writer.settlement_);
  if (!claim || claim->state.get() != impl_->state.get() || !claim->active.load()) {
    throw std::logic_error("write requires the active session writer lease");
  }
  {
    const std::lock_guard<std::mutex> lock(impl_->state->mutex);
    if (impl_->state->closed) {
      return std::nullopt;
    }
  }
  return impl_->scheduler.try_begin_write();
}

uint64_t BridgeWriterCore::transient_bytes() const
{
  return impl_->scheduler.transient_bytes();
}

uint64_t BridgeWriterCore::in_flight_bytes() const
{
  return impl_->scheduler.in_flight_bytes();
}

u2r2::BoundedOutboundScheduler & BridgeWriterCore::scheduler() noexcept
{
  return impl_->scheduler;
}
}  // namespace unity2foxglove::ros2_bridge::runtime
