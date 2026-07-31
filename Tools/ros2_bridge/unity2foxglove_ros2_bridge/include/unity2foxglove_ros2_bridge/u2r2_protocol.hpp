// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Strict, transport-independent U2R2 v2 protocol authority.

#pragma once

#include <cstdint>
#include <initializer_list>
#include <optional>
#include <stdexcept>
#include <string>
#include <vector>

#include <nlohmann/json.hpp>

namespace unity2foxglove::ros2_bridge::u2r2
{
class ProtocolLimits;

constexpr uint16_t kEnvelopeVersion = 1;
constexpr uint32_t kProtocolVersion = 2;

enum class Capability
{
  Publish = 1,
  Subscribe = 2,
};

enum class Dialect
{
  None = 0,
  V1 = 1,
  V2 = 2,
};

enum class ConnectionState
{
  AwaitingFirstFrame = 0,
  V1Probe = 1,
  V1Data = 2,
  V2Active = 3,
  Terminal = 4,
};

enum class Operation
{
  Unknown = 0,
  Hello,
  HelloAck,
  HealthPing,
  HealthPong,
  PreparePublisher,
  PublisherReady,
  Publish,
  PublishResult,
  RegisterSubscription,
  SubscriptionReady,
  Message,
  UnregisterSubscription,
  SubscriptionRemoved,
  Busy,
  Fault,
};

class ProtocolError final : public std::runtime_error
{
public:
  ProtocolError(std::string code, std::string message, bool terminal = true);

  const std::string & code() const noexcept;
  bool terminal() const noexcept;

private:
  std::string code_;
  bool terminal_;
};

struct Frame
{
  nlohmann::json header;
  std::vector<uint8_t> payload;
};

struct Qos
{
  std::string profile;
  std::string reliability;
  std::string durability;
  std::string history;
  uint32_t depth{0};

  bool operator==(const Qos &) const noexcept = default;
};

struct Message
{
  Operation operation{Operation::Unknown};
  std::string operation_name;
  bool is_request{false};
  bool is_response{false};
  bool terminal{false};
  uint64_t request_id{0};
  uint64_t message_id{0};
  uint64_t contract_id{0};
  std::string session_id;
  uint64_t connection_generation{0};
  std::vector<Capability> capabilities;
  std::string status;
  std::string error_code;
  std::string error_message;
  uint64_t log_time_ns{0};
  uint64_t receive_time_ns{0};
  std::string encoding;
  std::string representation;
  std::string topic;
  std::string schema_name;
  std::optional<Qos> qos;
};

class ResponseExpectation final
{
public:
  static ResponseExpectation from_request(const Message & request);
  static ResponseExpectation from_hello_request(uint64_t request_id);

  Operation request_operation() const noexcept;
  uint64_t request_id() const noexcept;
  Operation success_response_operation() const noexcept;
  const std::vector<Operation> & allowed_response_operations() const noexcept;
  const std::string & session_id() const noexcept;
  uint64_t connection_generation() const noexcept;
  uint64_t contract_id() const noexcept;
  uint64_t message_id() const noexcept;
  bool assigns_session_identity() const noexcept;

private:
  ResponseExpectation(
    Operation request_operation,
    uint64_t request_id,
    std::string session_id,
    uint64_t connection_generation,
    uint64_t contract_id,
    uint64_t message_id);

  Operation request_operation_;
  uint64_t request_id_;
  Operation success_response_operation_;
  std::vector<Operation> allowed_response_operations_;
  std::string session_id_;
  uint64_t connection_generation_;
  uint64_t contract_id_;
  uint64_t message_id_;
};

struct LegacyV1Message
{
  std::string operation;
  std::string request_id;
  bool acquires_data_lease{false};
};

class MonotonicCounter final
{
public:
  explicit MonotonicCounter(uint64_t current = 0) noexcept;

  uint64_t next();
  bool faulted() const noexcept;

private:
  uint64_t current_;
  bool faulted_{false};
};

class SessionIdentity final
{
public:
  const std::string & session_id() const noexcept;
  uint64_t connection_generation() const noexcept;

private:
  friend class SidecarSessionIdentityAllocator;
  SessionIdentity(std::string session_id, uint64_t connection_generation);

  std::string session_id_;
  uint64_t connection_generation_;
};

class SidecarSessionIdentityAllocator final
{
public:
  explicit SidecarSessionIdentityAllocator(uint64_t current_generation = 0);

  SessionIdentity allocate();

private:
  MonotonicCounter generation_;
};

class SessionStateMachine final
{
public:
  void accept_v2(
    const Message & hello,
    std::initializer_list<Capability> required_capabilities);
  void accept_legacy(const LegacyV1Message & first_frame);
  [[noreturn]] void fault(const std::string & code, const std::string & message);

  Dialect dialect() const noexcept;
  ConnectionState state() const noexcept;
  bool acquires_data_lease() const noexcept;

private:
  [[noreturn]] void throw_terminal(
    const std::string & code,
    const std::string & message);

  Dialect dialect_{Dialect::None};
  ConnectionState state_{ConnectionState::AwaitingFirstFrame};
  bool acquires_data_lease_{false};
};

std::vector<uint8_t> encode_frame(
  const nlohmann::json & header,
  const std::vector<uint8_t> & payload);
std::vector<uint8_t> encode_frame(
  const nlohmann::json & header,
  const std::vector<uint8_t> & payload,
  const ProtocolLimits & limits);
Frame decode_frame(const std::vector<uint8_t> & bytes);
Frame decode_frame(
  const std::vector<uint8_t> & bytes,
  const ProtocolLimits & limits);
Message parse_v2(const Frame & frame);
LegacyV1Message parse_legacy_v1_first_frame(
  const std::vector<uint8_t> & bytes);
bool try_get_stable_error_terminal(
  const std::string & error_code,
  bool & terminal) noexcept;
bool is_stable_error_allowed_for_response(
  const std::string & error_code,
  Operation operation) noexcept;
void validate_response_correlation(
  const ResponseExpectation & expected,
  const Message & response);
}  // namespace unity2foxglove::ros2_bridge::u2r2
