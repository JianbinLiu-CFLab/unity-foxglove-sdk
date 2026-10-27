// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

#include "unity2foxglove_ros2_bridge/u2r2_protocol.hpp"

#include <algorithm>
#include <array>
#include <iomanip>
#include <limits>
#include <random>
#include <sstream>
#include <utility>

namespace unity2foxglove::ros2_bridge::u2r2
{
namespace
{
[[noreturn]] void InvalidFrame(const std::string & message)
{
  throw ProtocolError("invalid_frame", message);
}

bool TryGetSuccessResponse(Operation request_operation, Operation & response_operation)
{
  switch (request_operation) {
    case Operation::Hello: response_operation = Operation::HelloAck; return true;
    case Operation::HealthPing: response_operation = Operation::HealthPong; return true;
    case Operation::PreparePublisher: response_operation = Operation::PublisherReady; return true;
    case Operation::Publish: response_operation = Operation::PublishResult; return true;
    case Operation::RegisterSubscription: response_operation = Operation::SubscriptionReady; return true;
    case Operation::UnregisterSubscription: response_operation = Operation::SubscriptionRemoved; return true;
    default: response_operation = Operation::Unknown; return false;
  }
}

bool IsRequest(Operation operation)
{
  Operation response_operation;
  return TryGetSuccessResponse(operation, response_operation);
}
}  // namespace

namespace
{
std::string GenerateSessionId()
{
  std::array<uint8_t, 16> bytes{};
  std::random_device random;
  for (auto & value : bytes) {
    value = static_cast<uint8_t>(random());
  }
  bytes[6] = static_cast<uint8_t>((bytes[6] & 0x0fU) | 0x40U);
  bytes[8] = static_cast<uint8_t>((bytes[8] & 0x3fU) | 0x80U);

  std::ostringstream result;
  result << std::hex << std::setfill('0');
  for (size_t index = 0; index < bytes.size(); ++index) {
    if (index == 4 || index == 6 || index == 8 || index == 10) {
      result << '-';
    }
    result << std::setw(2) << static_cast<unsigned int>(bytes[index]);
  }
  return result.str();
}
}  // namespace

Operation ResponseExpectation::request_operation() const noexcept
{
  return request_operation_;
}

uint64_t ResponseExpectation::request_id() const noexcept
{
  return request_id_;
}

Operation ResponseExpectation::success_response_operation() const noexcept
{
  return success_response_operation_;
}

const std::vector<Operation> &
ResponseExpectation::allowed_response_operations() const noexcept
{
  return allowed_response_operations_;
}

const std::string & ResponseExpectation::session_id() const noexcept
{
  return session_id_;
}

uint64_t ResponseExpectation::connection_generation() const noexcept
{
  return connection_generation_;
}

uint64_t ResponseExpectation::contract_id() const noexcept
{
  return contract_id_;
}

uint64_t ResponseExpectation::message_id() const noexcept
{
  return message_id_;
}

bool ResponseExpectation::assigns_session_identity() const noexcept
{
  return request_operation_ == Operation::Hello;
}

MonotonicCounter::MonotonicCounter(uint64_t current) noexcept
: current_(current)
{
}

uint64_t MonotonicCounter::next()
{
  if (faulted_ || current_ == std::numeric_limits<uint64_t>::max()) {
    faulted_ = true;
    throw ProtocolError(
            "counter_exhausted",
            "the U2R2 uint64 identifier counter is exhausted");
  }
  ++current_;
  return current_;
}

bool MonotonicCounter::faulted() const noexcept
{
  return faulted_;
}

void SessionStateMachine::accept_v2(
  const Message & hello,
  std::initializer_list<Capability> required_capabilities)
{
  if (state_ != ConnectionState::AwaitingFirstFrame) {
    throw_terminal("dialect_downgrade", "a socket cannot change U2R2 dialect");
  }
  if (hello.operation != Operation::Hello || !hello.is_request) {
    throw_terminal("invalid_frame", "the first U2R2 v2 frame must be hello");
  }

  for (const auto required : required_capabilities) {
    if (std::find(
        hello.capabilities.begin(),
        hello.capabilities.end(),
        required) == hello.capabilities.end())
    {
      throw_terminal(
        "missing_capability",
        "a required U2R2 capability was not offered");
    }
  }

  dialect_ = Dialect::V2;
  state_ = ConnectionState::V2Active;
  acquires_data_lease_ = true;
}

void SessionStateMachine::accept_legacy(const LegacyV1Message & first_frame)
{
  if (state_ != ConnectionState::AwaitingFirstFrame) {
    throw_terminal("dialect_downgrade", "a socket cannot change U2R2 dialect");
  }
  dialect_ = Dialect::V1;
  acquires_data_lease_ = first_frame.acquires_data_lease;
  state_ = acquires_data_lease_
    ? ConnectionState::V1Data
    : ConnectionState::V1Probe;
}

void SessionStateMachine::fault(
  const std::string & code,
  const std::string & message)
{
  throw_terminal(code, message);
}

Dialect SessionStateMachine::dialect() const noexcept
{
  return dialect_;
}

ConnectionState SessionStateMachine::state() const noexcept
{
  return state_;
}

bool SessionStateMachine::acquires_data_lease() const noexcept
{
  return acquires_data_lease_;
}

void SessionStateMachine::throw_terminal(
  const std::string & code,
  const std::string & message)
{
  state_ = ConnectionState::Terminal;
  acquires_data_lease_ = false;
  throw ProtocolError(code, message);
}
ProtocolError::ProtocolError(
  std::string code,
  std::string message,
  bool terminal)
: std::runtime_error(std::move(message)),
  code_(code.empty() ? "invalid_frame" : std::move(code)),
  terminal_(terminal)
{
}

const std::string & ProtocolError::code() const noexcept
{
  return code_;
}

bool ProtocolError::terminal() const noexcept
{
  return terminal_;
}

SessionIdentity::SessionIdentity(
  std::string session_id,
  uint64_t connection_generation)
: session_id_(std::move(session_id)),
  connection_generation_(connection_generation)
{
}

const std::string & SessionIdentity::session_id() const noexcept
{
  return session_id_;
}

uint64_t SessionIdentity::connection_generation() const noexcept
{
  return connection_generation_;
}

SidecarSessionIdentityAllocator::SidecarSessionIdentityAllocator(
  uint64_t current_generation)
: generation_(current_generation)
{
}

SessionIdentity SidecarSessionIdentityAllocator::allocate()
{
  return SessionIdentity(GenerateSessionId(), generation_.next());
}
ResponseExpectation::ResponseExpectation(
  Operation request_operation,
  uint64_t request_id,
  std::string session_id,
  uint64_t connection_generation,
  uint64_t contract_id,
  uint64_t message_id)
: request_operation_(request_operation),
  request_id_(request_id),
  success_response_operation_(Operation::Unknown),
  session_id_(std::move(session_id)),
  connection_generation_(connection_generation),
  contract_id_(contract_id),
  message_id_(message_id)
{
  if (request_id_ == 0) {
    throw ProtocolError(
            "invalid_request_id",
            "a correlated U2R2 request ID must be nonzero");
  }
  if (!TryGetSuccessResponse(
      request_operation_,
      success_response_operation_))
  {
    throw std::invalid_argument(
            "request_operation must identify a U2R2 request");
  }
  if (session_id_.empty() != (connection_generation_ == 0)) {
    InvalidFrame(
      "expected U2R2 session identity fields must be present together");
  }
  allowed_response_operations_ = {
    success_response_operation_,
    Operation::Busy,
    Operation::Fault};
}

ResponseExpectation ResponseExpectation::from_request(const Message & request)
{
  if (!request.is_request || !IsRequest(request.operation)) {
    throw std::invalid_argument(
            "request must identify a parsed U2R2 request");
  }
  return ResponseExpectation(
    request.operation,
    request.request_id,
    request.session_id,
    request.connection_generation,
    request.contract_id,
    request.message_id);
}

ResponseExpectation ResponseExpectation::from_hello_request(uint64_t request_id)
{
  return ResponseExpectation(
    Operation::Hello,
    request_id,
    {},
    0,
    0,
    0);
}
}  // namespace unity2foxglove::ros2_bridge::u2r2
