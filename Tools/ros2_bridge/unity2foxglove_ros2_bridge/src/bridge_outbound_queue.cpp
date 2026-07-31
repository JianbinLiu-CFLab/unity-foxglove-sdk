// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/ros2_bridge
// Purpose: Bounded ROS 2 serialized-message admission into U2R2.

#include "unity2foxglove_ros2_bridge/bridge_outbound_queue.hpp"

#include <algorithm>
#include <limits>
#include <mutex>
#include <stdexcept>
#include <utility>
#include <vector>

#include <nlohmann/json.hpp>

#include "unity2foxglove_ros2_bridge/bridge_writer.hpp"
#include "unity2foxglove_ros2_bridge/u2r2_protocol.hpp"

namespace unity2foxglove::ros2_bridge::runtime
{
namespace
{
struct GateState final
{
  GateState(
    std::weak_ptr<void> owner_value,
    u2r2::ContractIdentity identity_value)
  : owner(std::move(owner_value)),
    identity(std::move(identity_value))
  {
  }

  std::weak_ptr<void> owner;
  u2r2::ContractIdentity identity;
  std::mutex mutex;
  uint64_t last_sequence{0};
  bool active{false};
  bool revoked{false};
};

bool HasXcdr1LittleEndianPrefix(
  const uint8_t * payload,
  size_t payload_size) noexcept
{
  return
    payload != nullptr &&
    payload_size >= 4 &&
    payload[0] == 0x00U &&
    payload[1] == 0x01U &&
    payload[2] == 0x00U &&
    payload[3] == 0x00U;
}
}  // namespace

struct BridgeOutboundQueue::State final
{
  State(
    const u2r2::ProtocolLimits & limits_value,
    BridgeWriterCore & writer_value,
    u2r2::ContractAuthority & contracts_value,
    std::string session_id_value,
    uint64_t generation_value)
  : limits(limits_value),
    writer(&writer_value),
    contracts(&contracts_value),
    session_id(std::move(session_id_value)),
    generation(generation_value)
  {
  }

  u2r2::ProtocolLimits limits;
  BridgeWriterCore * writer;
  u2r2::ContractAuthority * contracts;
  std::string session_id;
  uint64_t generation;
  mutable std::mutex mutex;
  std::vector<std::weak_ptr<BridgeSubscriptionGate>> gates;
  BridgeOutboundQueueStats stats;
  uint64_t next_message_id{0};
  bool closed{false};
};

BridgeSubscriptionGate::BridgeSubscriptionGate(std::shared_ptr<void> state)
: state_(std::move(state))
{
}

BridgeSubscriptionGate::~BridgeSubscriptionGate() = default;

std::shared_ptr<void> BridgeOutboundQueue::GateStateOf(
  const std::shared_ptr<BridgeSubscriptionGate> & gate)
{
  return gate ? gate->state_ : nullptr;
}

BridgeOutboundQueue::BridgeOutboundQueue(
  const u2r2::ProtocolLimits & limits,
  BridgeWriterCore & writer,
  u2r2::ContractAuthority & contracts,
  std::string session_id,
  uint64_t connection_generation)
: state_(std::make_shared<State>(
    limits,
    writer,
    contracts,
    std::move(session_id),
    connection_generation))
{
  if (state_->session_id.empty() || connection_generation == 0) {
    throw std::invalid_argument(
            "a Bridge outbound queue requires frozen session identity");
  }
}

BridgeOutboundQueue::~BridgeOutboundQueue()
{
  close();
}

std::shared_ptr<BridgeSubscriptionGate>
BridgeOutboundQueue::create_gate(
  const u2r2::ContractIdentity & identity)
{
  if (
    identity.direction != u2r2::ContractDirection::subscribe ||
    identity.key.generation != state_->generation)
  {
    throw u2r2::ProtocolError(
            "invalid_contract",
            "a Bridge subscription gate requires this subscribe generation",
            false);
  }
  std::lock_guard<std::mutex> lock(state_->mutex);
  if (state_->closed) {
    throw std::logic_error("the Bridge outbound queue is closed");
  }
  state_->gates.erase(
    std::remove_if(
      state_->gates.begin(),
      state_->gates.end(),
      [](const auto & candidate) {return candidate.expired();}),
    state_->gates.end());
  if (
    static_cast<uint64_t>(state_->gates.size()) >=
    state_->limits.max_contracts())
  {
    throw u2r2::ProtocolError(
            "capacity_exceeded",
            "the Bridge subscription gate limit is exhausted",
            false);
  }
  auto gate = std::shared_ptr<BridgeSubscriptionGate>(
    new BridgeSubscriptionGate(
      std::make_shared<GateState>(
        std::weak_ptr<void>(state_),
        identity)));
  state_->gates.emplace_back(gate);
  return gate;
}

void BridgeOutboundQueue::activate(
  const std::shared_ptr<BridgeSubscriptionGate> & gate)
{
  const auto state = state_;
  const auto gate_state =
    std::static_pointer_cast<GateState>(GateStateOf(gate));
  if (!gate_state || gate_state->owner.lock().get() != state.get()) {
    throw std::invalid_argument(
            "the Bridge subscription gate belongs to another queue");
  }
  std::lock_guard<std::mutex> lock(gate_state->mutex);
  if (gate_state->revoked) {
    throw std::logic_error("a revoked Bridge subscription cannot reactivate");
  }
  {
    std::lock_guard<std::mutex> state_lock(state->mutex);
    if (state->closed) {
      throw std::logic_error("the Bridge outbound queue is closed");
    }
  }
  gate_state->active = true;
}

void BridgeOutboundQueue::revoke(
  const std::shared_ptr<BridgeSubscriptionGate> & gate)
{
  const auto gate_state =
    std::static_pointer_cast<GateState>(GateStateOf(gate));
  if (!gate_state || gate_state->owner.lock().get() != state_.get()) {
    throw std::invalid_argument(
            "the Bridge subscription gate belongs to another queue");
  }
  std::lock_guard<std::mutex> lock(gate_state->mutex);
  gate_state->active = false;
  gate_state->revoked = true;
}

BridgeSerializedCallback BridgeOutboundQueue::callback(
  const std::shared_ptr<BridgeSubscriptionGate> & gate)
{
  const auto state = state_;
  const auto gate_state =
    std::static_pointer_cast<GateState>(GateStateOf(gate));
  if (!gate_state || gate_state->owner.lock().get() != state.get()) {
    throw std::invalid_argument(
            "the Bridge subscription gate belongs to another queue");
  }
  return [state, gate](
           const uint8_t * payload,
           size_t payload_size,
           uint64_t receive_time_ns,
           BridgeSampleOrigin origin) {
           return Enqueue(
             state,
             gate,
             payload,
             payload_size,
             receive_time_ns,
             origin);
         };
}

BridgeSerializedAdmission BridgeOutboundQueue::enqueue(
  const std::shared_ptr<BridgeSubscriptionGate> & gate,
  const uint8_t * payload,
  size_t payload_size,
  uint64_t receive_time_ns,
  BridgeSampleOrigin origin)
{
  return Enqueue(
    state_,
    gate,
    payload,
    payload_size,
    receive_time_ns,
    origin);
}

BridgeSerializedAdmission BridgeOutboundQueue::Enqueue(
  const std::shared_ptr<State> & state,
  const std::shared_ptr<BridgeSubscriptionGate> & gate,
  const uint8_t * payload,
  size_t payload_size,
  uint64_t receive_time_ns,
  BridgeSampleOrigin origin)
{
  const auto gate_state =
    std::static_pointer_cast<GateState>(GateStateOf(gate));
  if (!gate_state || gate_state->owner.lock().get() != state.get()) {
    throw std::invalid_argument(
            "the Bridge subscription gate belongs to another queue");
  }
  std::lock_guard<std::mutex> gate_lock(gate_state->mutex);
  {
    std::lock_guard<std::mutex> lock(state->mutex);
    if (state->closed || !gate_state->active || gate_state->revoked) {
      ++state->stats.inactive;
      return BridgeSerializedAdmission::inactive;
    }
  }
  if (origin == BridgeSampleOrigin::local) {
    std::lock_guard<std::mutex> lock(state->mutex);
    ++state->stats.suppressed_local;
    return BridgeSerializedAdmission::suppressed_local;
  }
  if (
    origin == BridgeSampleOrigin::missing ||
    origin == BridgeSampleOrigin::ambiguous)
  {
    std::lock_guard<std::mutex> lock(state->mutex);
    ++state->stats.invalid_origin;
    return BridgeSerializedAdmission::invalid_origin;
  }
  if (origin != BridgeSampleOrigin::external) {
    throw std::invalid_argument(
            "the Bridge sample origin classification is invalid");
  }
  if (payload_size > state->limits.max_payload_bytes()) {
    std::lock_guard<std::mutex> lock(state->mutex);
    ++state->stats.payload_too_large;
    return BridgeSerializedAdmission::payload_too_large;
  }
  if (!HasXcdr1LittleEndianPrefix(payload, payload_size)) {
    std::lock_guard<std::mutex> lock(state->mutex);
    ++state->stats.unsupported_representation;
    return BridgeSerializedAdmission::unsupported_representation;
  }
  if (gate_state->last_sequence == std::numeric_limits<uint64_t>::max()) {
    throw u2r2::ProtocolError(
            "contract_sequence_exhausted",
            "the Bridge subscription sequence is exhausted",
            false);
  }
  const auto sequence = gate_state->last_sequence + 1U;
  uint64_t message_id = 0;
  {
    std::lock_guard<std::mutex> lock(state->mutex);
    if (state->closed || state->next_message_id ==
      std::numeric_limits<uint64_t>::max())
    {
      if (state->closed) {
        ++state->stats.inactive;
        return BridgeSerializedAdmission::inactive;
      }
      throw u2r2::ProtocolError(
              "counter_exhausted",
              "the Bridge outbound message ID is exhausted",
              true);
    }
    message_id = ++state->next_message_id;
  }

  nlohmann::json header{
    {"op", "message"},
    {"protocolVersion", u2r2::kProtocolVersion},
    {"sessionId", state->session_id},
    {"connectionGeneration", state->generation},
    {"contractId", gate_state->identity.key.contract_id},
    {"messageId", message_id},
    {"sequence", sequence},
    {"receiveTimeNs", receive_time_ns},
    {"encoding", "cdr"},
    {"representation", "xcdr1-le"},
    {"topic", gate_state->identity.topic},
    {"schemaName", gate_state->identity.schema_name},
  };
  const auto frame_bytes = u2r2::encoded_frame_size(
    header,
    static_cast<uint64_t>(payload_size),
    state->limits);
  auto transient = state->writer->try_reserve_transient(frame_bytes);
  if (!transient) {
    std::lock_guard<std::mutex> lock(state->mutex);
    ++state->stats.capacity_rejected;
    return BridgeSerializedAdmission::capacity_rejected;
  }
  auto reservation = state->writer->try_reserve_data(
    gate_state->identity.key,
    frame_bytes);
  if (!reservation) {
    std::lock_guard<std::mutex> lock(state->mutex);
    ++state->stats.capacity_rejected;
    return BridgeSerializedAdmission::capacity_rejected;
  }
  auto bytes = u2r2::encode_frame(
    header,
    payload,
    payload_size,
    state->limits);
  const auto authority = state->contracts->admit_message(
    gate_state->identity,
    sequence);
  if (authority != u2r2::MessageAdmission::accepted) {
    std::lock_guard<std::mutex> lock(state->mutex);
    ++state->stats.inactive;
    return BridgeSerializedAdmission::inactive;
  }
  if (!reservation->try_commit(
      u2r2::OutboundFrame::data(
        "message:" + std::to_string(message_id),
        gate_state->identity.key,
        sequence,
        std::move(bytes))))
  {
    throw std::logic_error(
            "an admitted Bridge message lost its reserved queue capacity");
  }
  gate_state->last_sequence = sequence;
  {
    std::lock_guard<std::mutex> lock(state->mutex);
    ++state->stats.accepted;
  }
  state->writer->notify();
  return BridgeSerializedAdmission::accepted;
}

BridgeOutboundQueueStats BridgeOutboundQueue::stats() const
{
  std::lock_guard<std::mutex> lock(state_->mutex);
  return state_->stats;
}

void BridgeOutboundQueue::close()
{
  std::vector<std::shared_ptr<BridgeSubscriptionGate>> gates;
  {
    std::lock_guard<std::mutex> lock(state_->mutex);
    if (state_->closed) {
      return;
    }
    state_->closed = true;
    for (const auto & candidate : state_->gates) {
      if (auto gate = candidate.lock()) {
        gates.push_back(std::move(gate));
      }
    }
    state_->gates.clear();
  }
  for (const auto & gate : gates) {
    const auto gate_state =
      std::static_pointer_cast<GateState>(GateStateOf(gate));
    std::lock_guard<std::mutex> lock(gate_state->mutex);
    gate_state->active = false;
    gate_state->revoked = true;
  }
}
}  // namespace unity2foxglove::ros2_bridge::runtime
