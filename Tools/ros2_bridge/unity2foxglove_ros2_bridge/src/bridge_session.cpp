// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/ros2_bridge
// Purpose: Frozen U2R2 first-frame, identity, and replay session integration.

#include "unity2foxglove_ros2_bridge/bridge_session.hpp"

#include <stdexcept>
#include <unordered_map>
#include <utility>

#include <nlohmann/json.hpp>

namespace unity2foxglove::ros2_bridge::runtime
{
namespace
{
bool IsJsonIntegerOne(const nlohmann::json & value)
{
  if (value.is_number_unsigned()) {
    return value.get<uint64_t>() == 1U;
  }
  if (value.is_number_integer()) {
    return value.get<int64_t>() == 1;
  }
  return false;
}

bool DeclaresV2(const u2r2::Frame & frame)
{
  const auto operation = frame.header.find("op");
  if (
    operation != frame.header.end() &&
    operation->is_string() &&
    operation->get<std::string>() == "hello")
  {
    return true;
  }
  const auto version = frame.header.find("protocolVersion");
  return version != frame.header.end() && !IsJsonIntegerOne(*version);
}

std::string PublisherSignature(const u2r2::Message & message)
{
  if (
    message.topic.empty() ||
    message.schema_name.empty() ||
    !message.qos)
  {
    throw u2r2::ProtocolError(
            "invalid_frame",
            "a publisher contract requires topic, type, cdr, and QoS",
            true);
  }
  const auto & qos = *message.qos;
  return
    message.schema_name + "\n" +
    qos.profile + "\n" +
    qos.reliability + "\n" +
    qos.durability + "\n" +
    qos.history + "\n" +
    std::to_string(qos.depth);
}

void ValidateSameRequest(
  const u2r2::Message & expected,
  const u2r2::Message & actual)
{
  if (
    expected.operation != actual.operation ||
    expected.request_id != actual.request_id ||
    expected.message_id != actual.message_id ||
    expected.session_id != actual.session_id ||
    expected.connection_generation != actual.connection_generation)
  {
    throw u2r2::ProtocolError(
            "invalid_frame",
            "the parsed U2R2 request does not match its canonical frame",
            true);
  }
}
}  // namespace

struct BridgeSessionProtocol::Impl final
{
  explicit Impl(const u2r2::ProtocolLimits & value)
  : limits(value),
    scheduler(value),
    replay(value)
  {
  }

  const u2r2::ProtocolLimits limits;
  u2r2::SessionStateMachine state;
  u2r2::BoundedOutboundScheduler scheduler;
  u2r2::RequestReplayAuthority replay;
  std::optional<u2r2::Message> hello;
  std::string session_id;
  uint64_t connection_generation{0};
  std::unordered_map<std::string, std::string> prepared_publishers;

  u2r2::Message parse_active_v2_request(
    const u2r2::Frame & frame) const
  {
    if (
      state.dialect() != u2r2::Dialect::V2 ||
      session_id.empty() ||
      connection_generation == 0)
    {
      throw u2r2::ProtocolError(
              "dialect_downgrade",
              "the socket is not an active U2R2 v2 session",
              true);
    }
    const auto message = u2r2::parse_v2(frame);
    if (
      !message.is_request ||
      message.operation == u2r2::Operation::Hello ||
      (message.operation != u2r2::Operation::HealthPing &&
      message.operation != u2r2::Operation::PreparePublisher &&
      message.operation != u2r2::Operation::Publish))
    {
      throw u2r2::ProtocolError(
              "invalid_frame",
              "the U2R2 operation is not valid in a publish session",
              true);
    }
    if (
      message.session_id != session_id ||
      message.connection_generation != connection_generation)
    {
      throw u2r2::ProtocolError(
              "invalid_frame",
              "the U2R2 request has stale or foreign session identity",
              true);
    }
    if (message.request_id == hello->request_id) {
      throw u2r2::ProtocolError(
              "request_id_conflict",
              "a normal U2R2 request reused the hello request ID",
              true);
    }
    if (message.request_id < hello->request_id) {
      throw u2r2::ProtocolError(
              "stale_request",
              "the U2R2 request ID is below the hello high-water mark",
              false);
    }
    return message;
  }
};

ReplayMutationResult ReplayMutationResult::success(
  std::vector<uint8_t> exact_response)
{
  return ReplayMutationResult{std::move(exact_response), false};
}

ReplayMutationResult ReplayMutationResult::error(
  std::vector<uint8_t> exact_response)
{
  return ReplayMutationResult{std::move(exact_response), true};
}

BridgeSessionProtocol::BridgeSessionProtocol(
  const u2r2::ProtocolLimits & limits)
: impl_(std::make_unique<Impl>(limits))
{
}

BridgeSessionProtocol::~BridgeSessionProtocol() = default;
BridgeSessionProtocol::BridgeSessionProtocol(BridgeSessionProtocol &&) noexcept =
  default;
BridgeSessionProtocol & BridgeSessionProtocol::operator=(
  BridgeSessionProtocol &&) noexcept = default;

FirstFrameClassification BridgeSessionProtocol::accept_first_frame(
  const std::vector<uint8_t> & wire_bytes)
{
  auto frame = u2r2::decode_frame(wire_bytes, impl_->limits);
  FirstFrameClassification result;
  if (DeclaresV2(frame)) {
    auto message = u2r2::parse_v2(frame);
    impl_->state.accept_v2(message, {u2r2::Capability::Publish});
    impl_->hello = message;
    result.dialect = u2r2::Dialect::V2;
    result.role = FirstFrameRole::data_session;
    result.one_shot = false;
    result.v2_message = std::move(message);
    return result;
  }

  auto legacy = u2r2::parse_legacy_v1_first_frame(wire_bytes);
  impl_->state.accept_legacy(legacy);
  result.dialect = u2r2::Dialect::V1;
  result.role = legacy.acquires_data_lease
    ? FirstFrameRole::data_session
    : FirstFrameRole::probe;
  result.one_shot = !legacy.acquires_data_lease;
  result.legacy_message = std::move(legacy);
  return result;
}

void BridgeSessionProtocol::bind_v2_identity(u2r2::SessionIdentity identity)
{
  if (
    impl_->state.dialect() != u2r2::Dialect::V2 ||
    !impl_->hello ||
    !impl_->session_id.empty())
  {
    throw std::logic_error(
            "v2 identity can only be bound once after hello");
  }
  impl_->session_id = identity.session_id();
  impl_->connection_generation = identity.connection_generation();
  const auto response = u2r2::encode_frame(
    {
      {"op", "hello_ack"},
      {"protocolVersion", 2},
      {"requestId", impl_->hello->request_id},
      {"status", "ok"},
      {"sessionId", impl_->session_id},
      {"connectionGeneration", impl_->connection_generation},
      {"capabilities", nlohmann::json::array({"publish"})},
    },
    {},
    impl_->limits);
  enqueue_control("hello_ack", response);
}

u2r2::Message BridgeSessionProtocol::parse_v2_request(
  const std::vector<uint8_t> & wire_bytes) const
{
  const auto frame = u2r2::decode_frame(wire_bytes, impl_->limits);
  return impl_->parse_active_v2_request(frame);
}

const std::string & BridgeSessionProtocol::session_id() const
{
  return impl_->session_id;
}

uint64_t BridgeSessionProtocol::connection_generation() const
{
  return impl_->connection_generation;
}

u2r2::Dialect BridgeSessionProtocol::dialect() const noexcept
{
  return impl_->state.dialect();
}

void BridgeSessionProtocol::require_publisher_capacity(
  const u2r2::Message & preparation) const
{
  if (preparation.operation != u2r2::Operation::PreparePublisher) {
    throw std::invalid_argument(
            "only publisher preparation can require publisher capacity");
  }
  if (
    impl_->prepared_publishers.find(preparation.topic) ==
    impl_->prepared_publishers.end() &&
    impl_->prepared_publishers.size() >= impl_->limits.max_contracts())
  {
    throw u2r2::ProtocolError(
            "capacity_exceeded",
            "the publisher contract limit is exhausted",
            false);
  }
}

void BridgeSessionProtocol::mark_publisher_ready(
  const u2r2::Message & preparation)
{
  if (preparation.operation != u2r2::Operation::PreparePublisher) {
    throw std::invalid_argument(
            "only publisher preparation can mark a publisher ready");
  }
  impl_->prepared_publishers[preparation.topic] =
    PublisherSignature(preparation);
}

void BridgeSessionProtocol::require_publisher_ready(
  const u2r2::Message & publish) const
{
  if (publish.operation != u2r2::Operation::Publish) {
    throw std::invalid_argument(
            "only publish can require publisher readiness");
  }
  const auto found = impl_->prepared_publishers.find(publish.topic);
  if (
    found == impl_->prepared_publishers.end() ||
    found->second != PublisherSignature(publish))
  {
    throw u2r2::ProtocolError(
            "contract_not_ready",
            "publish requires an exact prepared publisher contract",
            true);
  }
}

u2r2::ReplayDecision BridgeSessionProtocol::execute_replayable(
  const std::vector<uint8_t> & request_wire,
  const u2r2::Message & request,
  uint64_t maximum_response_bytes,
  const std::function<ReplayMutationResult()> & mutation)
{
  if (!mutation) {
    throw std::invalid_argument("replayable mutation callback is required");
  }
  const auto frame = u2r2::decode_frame(request_wire, impl_->limits);
  const auto parsed = impl_->parse_active_v2_request(frame);
  ValidateSameRequest(request, parsed);
  const auto canonical =
    u2r2::encode_frame(frame.header, frame.payload, impl_->limits);
  auto admission = impl_->replay.admit(
    parsed.request_id,
    canonical,
    maximum_response_bytes,
    impl_->scheduler);
  if (admission.decision() == u2r2::ReplayDecision::replay_cached) {
    return admission.decision();
  }

  try {
    auto result = mutation();
    if (result.exact_response.empty()) {
      throw std::invalid_argument(
              "a replayable command requires an exact response");
    }
    if (result.is_error) {
      impl_->replay.abort(admission, result.exact_response);
    } else {
      impl_->replay.complete(admission, result.exact_response);
    }
  } catch (...) {
    try {
      impl_->replay.cancel_pending(admission);
    } catch (...) {
    }
    throw;
  }
  return admission.decision();
}

std::optional<u2r2::ControlReservation>
BridgeSessionProtocol::try_reserve_control(uint64_t bytes)
{
  return impl_->scheduler.try_reserve_control(bytes);
}

std::optional<u2r2::ByteLease>
BridgeSessionProtocol::try_reserve_transient(uint64_t bytes)
{
  return impl_->scheduler.try_reserve_transient(bytes);
}

std::optional<u2r2::ByteLease>
BridgeSessionProtocol::try_begin_read(uint64_t bytes)
{
  return impl_->scheduler.try_begin_read(bytes);
}

void BridgeSessionProtocol::enqueue_control(
  std::string token,
  std::vector<uint8_t> exact_frame)
{
  auto reservation = impl_->scheduler.try_reserve_control(
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
}

std::optional<u2r2::WriteLease> BridgeSessionProtocol::try_begin_write()
{
  return impl_->scheduler.try_begin_write();
}

uint64_t BridgeSessionProtocol::transient_bytes() const
{
  return impl_->scheduler.transient_bytes();
}

uint64_t BridgeSessionProtocol::in_flight_bytes() const
{
  return impl_->scheduler.in_flight_bytes();
}

const u2r2::ProtocolLimits & BridgeSessionProtocol::limits() const noexcept
{
  return impl_->limits;
}

std::vector<uint8_t> make_v2_busy_response(
  uint64_t request_id,
  const u2r2::ProtocolLimits & limits)
{
  return u2r2::encode_frame(
    {
      {"op", "busy"},
      {"protocolVersion", 2},
      {"requestId", request_id},
      {"status", "error"},
      {"errorCode", "busy"},
      {"message", "data session already leased"},
      {"terminal", true},
    },
    {},
    limits);
}
}  // namespace unity2foxglove::ros2_bridge::runtime
