// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

#include "unity2foxglove_ros2_bridge/u2r2_protocol.hpp"

#include <algorithm>
#include <array>
#include <iomanip>
#include <limits>
#include <random>
#include <sstream>
#include <unordered_map>
#include <unordered_set>
#include <utility>

namespace unity2foxglove::ros2_bridge::u2r2
{
namespace
{
constexpr size_t kFixedHeaderBytes = 16;
constexpr uint32_t kMaxJsonHeaderBytes = 64U * 1024U;
constexpr uint32_t kMaxPayloadBytes = 64U * 1024U * 1024U;
constexpr int kMaxJsonDepth = 64;
constexpr std::array<uint8_t, 4> kMagic{{'U', '2', 'R', '2'}};

const std::unordered_map<std::string, Operation> kOperations{
  {"hello", Operation::Hello},
  {"hello_ack", Operation::HelloAck},
  {"health_ping", Operation::HealthPing},
  {"health_pong", Operation::HealthPong},
  {"prepare_publisher", Operation::PreparePublisher},
  {"publisher_ready", Operation::PublisherReady},
  {"publish", Operation::Publish},
  {"publish_result", Operation::PublishResult},
  {"register_subscription", Operation::RegisterSubscription},
  {"subscription_ready", Operation::SubscriptionReady},
  {"message", Operation::Message},
  {"unregister_subscription", Operation::UnregisterSubscription},
  {"subscription_removed", Operation::SubscriptionRemoved},
  {"busy", Operation::Busy},
  {"fault", Operation::Fault},
};

struct StableErrorRule
{
  bool terminal;
  std::unordered_set<Operation> response_operations;
};

const std::unordered_map<std::string, StableErrorRule> kStableErrors{
  {"busy", {true, {Operation::Busy}}},
  {"unsupported_protocol", {true, {Operation::Fault}}},
  {"missing_capability", {true, {Operation::Fault}}},
  {"invalid_frame", {true, {Operation::Fault}}},
  {"invalid_contract", {false, {Operation::PublisherReady}}},
  {"publisher_unavailable", {false, {Operation::PublisherReady}}},
};

[[noreturn]] void InvalidFrame(const std::string & message)
{
  throw ProtocolError("invalid_frame", message);
}

uint32_t ReadU32(const std::vector<uint8_t> & bytes, size_t offset)
{
  return static_cast<uint32_t>(bytes[offset])
         | (static_cast<uint32_t>(bytes[offset + 1]) << 8U)
         | (static_cast<uint32_t>(bytes[offset + 2]) << 16U)
         | (static_cast<uint32_t>(bytes[offset + 3]) << 24U);
}

void WriteU32(std::vector<uint8_t> & bytes, size_t offset, uint32_t value)
{
  bytes[offset] = static_cast<uint8_t>(value & 0xffU);
  bytes[offset + 1] = static_cast<uint8_t>((value >> 8U) & 0xffU);
  bytes[offset + 2] = static_cast<uint8_t>((value >> 16U) & 0xffU);
  bytes[offset + 3] = static_cast<uint8_t>((value >> 24U) & 0xffU);
}

void ValidateJsonValueDomain(
  const nlohmann::json & value,
  int container_depth)
{
  if (value.is_object()) {
    if (container_depth > kMaxJsonDepth) {
      InvalidFrame("the U2R2 JSON header exceeds its depth limit");
    }
    for (const auto & item : value.items()) {
      ValidateJsonValueDomain(item.value(), container_depth + 1);
    }
    return;
  }
  if (value.is_array()) {
    if (container_depth > kMaxJsonDepth) {
      InvalidFrame("the U2R2 JSON header exceeds its depth limit");
    }
    for (const auto & item : value) {
      ValidateJsonValueDomain(item, container_depth + 1);
    }
    return;
  }
  if (
    value.is_string() ||
    value.is_boolean() ||
    value.is_null() ||
    value.is_number_unsigned())
  {
    return;
  }
  if (value.is_number_integer() && value.get<int64_t>() >= 0) {
    return;
  }
  InvalidFrame(
    "the U2R2 JSON header supports only strings, Booleans, null, "
    "and nonnegative uint64 integers");
}

void ValidateUnsignedIntegerLexemes(const std::string & text)
{
  bool in_string = false;
  bool escaped = false;
  for (size_t index = 0; index < text.size(); ++index) {
    const auto character = text[index];
    if (in_string) {
      if (escaped) {
        escaped = false;
      } else if (character == '\\') {
        escaped = true;
      } else if (character == '"') {
        in_string = false;
      }
      continue;
    }
    if (character == '"') {
      in_string = true;
      continue;
    }
    if (character == '-') {
      InvalidFrame(
        "the U2R2 JSON header permits only unsigned decimal integer tokens");
    }
    if (character < '0' || character > '9') {
      continue;
    }

    const auto first = character;
    auto end = index + 1;
    while (end < text.size() && text[end] >= '0' && text[end] <= '9') {
      ++end;
    }
    if (
      (first == '0' && end != index + 1) ||
      (end < text.size() &&
      (text[end] == '.' || text[end] == 'e' || text[end] == 'E')))
    {
      InvalidFrame(
        "the U2R2 JSON header permits only unsigned decimal integer tokens");
    }
    index = end - 1;
  }
}

nlohmann::json ParseStrictObject(const std::string & text)
{
  if (
    text.size() >= 3 &&
    static_cast<uint8_t>(text[0]) == 0xefU &&
    static_cast<uint8_t>(text[1]) == 0xbbU &&
    static_cast<uint8_t>(text[2]) == 0xbfU)
  {
    InvalidFrame("the U2R2 JSON header cannot contain a UTF-8 BOM");
  }
  ValidateUnsignedIntegerLexemes(text);
  bool duplicate_key = false;
  std::vector<std::unordered_set<std::string>> object_keys;
  try {
    auto value = nlohmann::json::parse(
      text,
      [&](int depth, nlohmann::json::parse_event_t event, nlohmann::json & parsed) {
        if (
          depth >= kMaxJsonDepth &&
          (event == nlohmann::json::parse_event_t::object_start ||
          event == nlohmann::json::parse_event_t::array_start))
        {
          InvalidFrame("the U2R2 JSON header exceeds its depth limit");
        }
        if (event == nlohmann::json::parse_event_t::object_start) {
          object_keys.emplace_back();
        } else if (event == nlohmann::json::parse_event_t::key) {
          if (object_keys.empty() ||
            !object_keys.back().insert(parsed.get<std::string>()).second)
          {
            duplicate_key = true;
          }
        } else if (
          event == nlohmann::json::parse_event_t::object_end &&
          !object_keys.empty())
        {
          object_keys.pop_back();
        }
        return true;
      },
      true,
      false);
    if (duplicate_key) {
      InvalidFrame("the U2R2 JSON header contains a duplicate property");
    }
    if (!value.is_object()) {
      InvalidFrame("the U2R2 JSON header must be an object");
    }
    ValidateJsonValueDomain(value, 1);
    return value;
  } catch (const ProtocolError &) {
    throw;
  } catch (const nlohmann::json::exception &) {
    InvalidFrame("the U2R2 JSON header is invalid");
  }
}

bool IsEmptyOrAsciiWhitespace(const std::string & value)
{
  if (value.empty()) {
    return true;
  }
  return std::all_of(
    value.begin(),
    value.end(),
    [](char character) {
      return character == ' ' || character == '\t' ||
             character == '\r' || character == '\n';
    });
}

std::string RequiredString(const nlohmann::json & header, const char * name)
{
  const auto iterator = header.find(name);
  if (iterator == header.end() || !iterator->is_string()) {
    InvalidFrame(std::string("the U2R2 ") + name + " must be a string");
  }
  const auto value = iterator->get<std::string>();
  if (IsEmptyOrAsciiWhitespace(value)) {
    InvalidFrame(std::string("the U2R2 ") + name + " must be nonempty");
  }
  return value;
}

std::string OptionalString(const nlohmann::json & header, const char * name)
{
  const auto iterator = header.find(name);
  if (iterator == header.end()) {
    return {};
  }
  if (!iterator->is_string()) {
    InvalidFrame(std::string("the U2R2 ") + name + " must be a string");
  }
  const auto value = iterator->get<std::string>();
  if (!value.empty() && IsEmptyOrAsciiWhitespace(value)) {
    InvalidFrame(std::string("the U2R2 ") + name + " cannot be whitespace");
  }
  return value;
}

uint64_t ReadUnsigned(const nlohmann::json & value, const char * name)
{
  if (value.is_number_unsigned()) {
    return value.get<uint64_t>();
  }
  if (value.is_number_integer()) {
    const auto signed_value = value.get<int64_t>();
    if (signed_value >= 0) {
      return static_cast<uint64_t>(signed_value);
    }
  }
  InvalidFrame(std::string("the U2R2 ") + name + " must be an unsigned integer");
}

uint64_t RequiredUnsigned(
  const nlohmann::json & header,
  const char * name,
  bool allow_zero = false)
{
  const auto iterator = header.find(name);
  if (iterator == header.end()) {
    InvalidFrame(std::string("the U2R2 ") + name + " is required");
  }
  const auto value = ReadUnsigned(*iterator, name);
  if (!allow_zero && value == 0) {
    if (std::string(name) == "requestId") {
      throw ProtocolError(
              "invalid_request_id",
              "the U2R2 requestId must be nonzero");
    }
    InvalidFrame(std::string("the U2R2 ") + name + " must be nonzero");
  }
  return value;
}

uint64_t OptionalUnsigned(
  const nlohmann::json & header,
  const char * name,
  bool allow_zero = false)
{
  if (!header.contains(name)) {
    return 0;
  }
  return RequiredUnsigned(header, name, allow_zero);
}

bool RequiredBoolean(const nlohmann::json & header, const char * name)
{
  const auto iterator = header.find(name);
  if (iterator == header.end()) {
    InvalidFrame(std::string("the U2R2 ") + name + " is required");
  }
  if (!iterator->is_boolean()) {
    InvalidFrame(std::string("the U2R2 ") + name + " must be Boolean");
  }
  return iterator->get<bool>();
}

bool TryGetSuccessResponse(
  Operation request_operation,
  Operation & response_operation)
{
  switch (request_operation) {
    case Operation::Hello:
      response_operation = Operation::HelloAck;
      return true;
    case Operation::HealthPing:
      response_operation = Operation::HealthPong;
      return true;
    case Operation::PreparePublisher:
      response_operation = Operation::PublisherReady;
      return true;
    case Operation::Publish:
      response_operation = Operation::PublishResult;
      return true;
    case Operation::RegisterSubscription:
      response_operation = Operation::SubscriptionReady;
      return true;
    case Operation::UnregisterSubscription:
      response_operation = Operation::SubscriptionRemoved;
      return true;
    default:
      response_operation = Operation::Unknown;
      return false;
  }
}

bool IsRequest(Operation operation)
{
  Operation response_operation;
  return TryGetSuccessResponse(operation, response_operation);
}

bool IsResponse(Operation operation)
{
  switch (operation) {
    case Operation::HelloAck:
    case Operation::HealthPong:
    case Operation::PublisherReady:
    case Operation::PublishResult:
    case Operation::SubscriptionReady:
    case Operation::SubscriptionRemoved:
    case Operation::Busy:
    case Operation::Fault:
      return true;
    default:
      return false;
  }
}

bool HasMessageId(Operation operation)
{
  return operation == Operation::Publish ||
         operation == Operation::PublishResult ||
         operation == Operation::Message;
}

bool MayDeclareEncoding(Operation operation)
{
  return operation == Operation::PreparePublisher ||
         operation == Operation::Publish ||
         operation == Operation::RegisterSubscription ||
         operation == Operation::Message;
}

bool HasContractId(Operation operation)
{
  return operation == Operation::RegisterSubscription ||
         operation == Operation::SubscriptionReady ||
         operation == Operation::Message ||
         operation == Operation::UnregisterSubscription ||
         operation == Operation::SubscriptionRemoved;
}

std::vector<Capability> ReadCapabilities(
  const nlohmann::json & header,
  Operation operation)
{
  if (operation != Operation::Hello && operation != Operation::HelloAck) {
    if (header.contains("capabilities")) {
      InvalidFrame("capabilities are only valid during U2R2 hello");
    }
    return {};
  }

  const auto iterator = header.find("capabilities");
  if (iterator == header.end() || !iterator->is_array() || iterator->empty()) {
    InvalidFrame("U2R2 hello requires capabilities");
  }

  std::vector<Capability> capabilities;
  std::unordered_set<int> seen;
  for (const auto & value : *iterator) {
    if (!value.is_string()) {
      InvalidFrame("a U2R2 capability must be a string");
    }
    Capability capability;
    const auto name = value.get<std::string>();
    if (name == "publish") {
      capability = Capability::Publish;
    } else if (name == "subscribe") {
      capability = Capability::Subscribe;
    } else {
      InvalidFrame("the U2R2 capability is unknown");
    }
    if (!seen.insert(static_cast<int>(capability)).second) {
      InvalidFrame("the U2R2 capability list contains duplicates");
    }
    capabilities.push_back(capability);
  }
  return capabilities;
}

void ValidatePayload(Operation operation, size_t payload_size)
{
  const bool carries_payload =
    operation == Operation::Publish || operation == Operation::Message;
  if (carries_payload && payload_size == 0) {
    InvalidFrame("the U2R2 data operation requires a payload");
  }
  if (!carries_payload && payload_size != 0) {
    InvalidFrame("this U2R2 operation cannot carry a payload");
  }
}

void ReadResponseStatus(
  const nlohmann::json & header,
  Operation operation,
  bool is_response,
  std::string & status,
  std::string & error_code,
  std::string & error_message,
  bool & terminal)
{
  terminal = false;
  if (!is_response) {
    if (
      header.contains("status") ||
      header.contains("errorCode") ||
      header.contains("message") ||
      header.contains("terminal"))
    {
      InvalidFrame("only U2R2 responses may contain response metadata");
    }
    return;
  }

  status = RequiredString(header, "status");
  if (status != "ok" && status != "error") {
    InvalidFrame("the U2R2 response status is invalid");
  }
  if (status == "ok") {
    if (operation == Operation::Busy || operation == Operation::Fault) {
      InvalidFrame("busy and fault U2R2 responses must use error status");
    }
    if (
      header.contains("errorCode") ||
      header.contains("message") ||
      header.contains("terminal"))
    {
      InvalidFrame("a successful U2R2 response cannot contain error metadata");
    }
    return;
  }

  error_code = RequiredString(header, "errorCode");
  error_message = RequiredString(header, "message");
  terminal = RequiredBoolean(header, "terminal");
  const auto rule = kStableErrors.find(error_code);
  if (rule == kStableErrors.end()) {
    InvalidFrame("the U2R2 response errorCode is unknown");
  }
  if (terminal != rule->second.terminal) {
    InvalidFrame(
      "the U2R2 response terminal classification does not match its errorCode");
  }
  if (
    rule->second.response_operations.find(operation) ==
    rule->second.response_operations.end())
  {
    InvalidFrame("the U2R2 errorCode is invalid for this response operation");
  }
}

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

std::vector<uint8_t> encode_frame(
  const nlohmann::json & header,
  const std::vector<uint8_t> & payload)
{
  if (!header.is_object()) {
    InvalidFrame("the U2R2 JSON header must be an object");
  }
  ValidateJsonValueDomain(header, 1);
  std::string json;
  try {
    json = header.dump(
      -1,
      ' ',
      true,
      nlohmann::json::error_handler_t::strict);
  } catch (const nlohmann::json::exception &) {
    InvalidFrame("the U2R2 JSON header is invalid");
  }
  if (json.empty() || json.size() > kMaxJsonHeaderBytes) {
    InvalidFrame("the U2R2 JSON header length is out of range");
  }
  if (payload.size() > kMaxPayloadBytes) {
    InvalidFrame("the U2R2 payload length is out of range");
  }

  std::vector<uint8_t> frame(kFixedHeaderBytes + json.size() + payload.size(), 0);
  std::copy(kMagic.begin(), kMagic.end(), frame.begin());
  frame[4] = static_cast<uint8_t>(kEnvelopeVersion);
  WriteU32(frame, 8, static_cast<uint32_t>(json.size()));
  WriteU32(frame, 12, static_cast<uint32_t>(payload.size()));
  std::copy(json.begin(), json.end(), frame.begin() + kFixedHeaderBytes);
  std::copy(
    payload.begin(),
    payload.end(),
    frame.begin() + kFixedHeaderBytes + json.size());
  return frame;
}

Frame decode_frame(const std::vector<uint8_t> & bytes)
{
  if (bytes.size() < kFixedHeaderBytes) {
    InvalidFrame("the U2R2 frame is shorter than its fixed header");
  }
  if (!std::equal(kMagic.begin(), kMagic.end(), bytes.begin())) {
    InvalidFrame("the U2R2 frame magic is invalid");
  }
  if (bytes[4] != kEnvelopeVersion ||
    bytes[5] != 0 || bytes[6] != 0 || bytes[7] != 0)
  {
    InvalidFrame("the U2R2 envelope version or reserved flags are invalid");
  }

  const auto header_length = ReadU32(bytes, 8);
  const auto payload_length = ReadU32(bytes, 12);
  if (header_length == 0 || header_length > kMaxJsonHeaderBytes) {
    InvalidFrame("the U2R2 JSON header length is out of range");
  }
  if (payload_length > kMaxPayloadBytes) {
    InvalidFrame("the U2R2 payload length is out of range");
  }
  const uint64_t expected_length =
    kFixedHeaderBytes +
    static_cast<uint64_t>(header_length) +
    static_cast<uint64_t>(payload_length);
  if (expected_length != bytes.size()) {
    InvalidFrame("the U2R2 frame has truncated or trailing bytes");
  }

  const std::string json(
    bytes.begin() + kFixedHeaderBytes,
    bytes.begin() + kFixedHeaderBytes + header_length);
  Frame frame;
  frame.header = ParseStrictObject(json);
  frame.payload.assign(
    bytes.begin() + kFixedHeaderBytes + header_length,
    bytes.end());
  return frame;
}

Message parse_v2(const Frame & frame)
{
  const auto operation_name = RequiredString(frame.header, "op");
  const auto operation_iterator = kOperations.find(operation_name);
  if (operation_iterator == kOperations.end()) {
    InvalidFrame("the U2R2 operation is unknown");
  }
  const auto operation = operation_iterator->second;
  if (RequiredUnsigned(frame.header, "protocolVersion") != kProtocolVersion) {
    throw ProtocolError(
            "unsupported_protocol",
            "U2R2 protocolVersion 2 is required");
  }

  Message message;
  message.operation = operation;
  message.operation_name = operation_name;
  message.is_request = IsRequest(operation);
  message.is_response = IsResponse(operation);
  if (!message.is_request &&
    !message.is_response &&
    operation != Operation::Message)
  {
    InvalidFrame("the U2R2 operation kind is invalid");
  }
  const auto status_iterator = frame.header.find("status");
  const bool is_error_response =
    message.is_response &&
    status_iterator != frame.header.end() &&
    status_iterator->is_string() &&
    status_iterator->get<std::string>() == "error";

  if (message.is_request || message.is_response) {
    message.request_id = RequiredUnsigned(frame.header, "requestId");
  } else if (frame.header.contains("requestId")) {
    InvalidFrame(
      "requestId is only valid on U2R2 requests and responses");
  }
  if (HasMessageId(operation) && !is_error_response) {
    message.message_id = RequiredUnsigned(frame.header, "messageId");
  } else if (frame.header.contains("messageId")) {
    InvalidFrame("messageId is not valid for this U2R2 operation");
  }
  if (HasContractId(operation) && !is_error_response) {
    message.contract_id = RequiredUnsigned(frame.header, "contractId");
  } else if (frame.header.contains("contractId")) {
    InvalidFrame("contractId is not valid for this U2R2 operation");
  }

  if (operation != Operation::Publish && frame.header.contains("logTimeNs")) {
    InvalidFrame("logTimeNs is only valid on a publish request");
  }
  if (
    operation != Operation::Message &&
    (frame.header.contains("receiveTimeNs") ||
    frame.header.contains("representation")))
  {
    InvalidFrame(
      "receiveTimeNs and representation are only valid on an inbound message");
  }
  if (!MayDeclareEncoding(operation) && frame.header.contains("encoding")) {
    InvalidFrame("encoding is not valid for this U2R2 operation");
  }
  if (operation == Operation::Publish) {
    message.log_time_ns =
      RequiredUnsigned(frame.header, "logTimeNs", true);
  } else if (operation == Operation::Message) {
    message.receive_time_ns =
      RequiredUnsigned(frame.header, "receiveTimeNs", true);
    message.encoding = RequiredString(frame.header, "encoding");
    message.representation = RequiredString(frame.header, "representation");
    if (
      message.encoding != "cdr" ||
      message.representation != "xcdr1-le")
    {
      InvalidFrame(
        "an inbound message requires cdr with its encapsulation header");
    }
  }

  if (
    operation == Operation::Hello &&
    (frame.header.contains("sessionId") ||
    frame.header.contains("connectionGeneration")))
  {
    InvalidFrame("a client hello cannot provide sidecar-owned session identity");
  }
  message.session_id = OptionalString(frame.header, "sessionId");
  message.connection_generation =
    OptionalUnsigned(frame.header, "connectionGeneration");
  const bool session_required =
    operation != Operation::Hello &&
    operation != Operation::Busy &&
    operation != Operation::Fault;
  if (session_required &&
    (message.session_id.empty() || message.connection_generation == 0))
  {
    InvalidFrame(
      "this U2R2 operation requires sessionId and connectionGeneration");
  }
  if (message.session_id.empty() != (message.connection_generation == 0)) {
    InvalidFrame("U2R2 session identity fields must be present together");
  }

  message.capabilities = ReadCapabilities(frame.header, operation);
  ValidatePayload(operation, frame.payload.size());
  if (operation == Operation::Message) {
    if (
      frame.payload.size() < 4 ||
      frame.payload[0] != 0x00U ||
      frame.payload[1] != 0x01U ||
      frame.payload[2] != 0x00U ||
      frame.payload[3] != 0x00U)
    {
      InvalidFrame(
        "an inbound message requires the XCDR1 little-endian "
        "encapsulation prefix 00 01 00 00");
    }
  }
  ReadResponseStatus(
    frame.header,
    operation,
    message.is_response,
    message.status,
    message.error_code,
    message.error_message,
    message.terminal);
  return message;
}

LegacyV1Message parse_legacy_v1_first_frame(
  const std::vector<uint8_t> & bytes)
{
  const auto frame = decode_frame(bytes);
  const auto operation = RequiredString(frame.header, "op");
  if (operation == "health_ping" || operation == "prepare_publisher") {
    if (RequiredUnsigned(frame.header, "protocolVersion") != 1) {
      InvalidFrame("a legacy control frame requires protocolVersion 1");
    }
    const auto request_id = RequiredString(frame.header, "requestId");
    if (request_id.find_first_of("\r\n") != std::string::npos) {
      InvalidFrame("a legacy requestId cannot contain line breaks");
    }
    return LegacyV1Message{
      operation,
      request_id,
      operation == "prepare_publisher"};
  }
  if (operation == "publish") {
    return LegacyV1Message{operation, {}, true};
  }
  InvalidFrame(
    "the first legacy U2R2 frame must be health_ping, prepare_publisher, or publish");
}

bool try_get_stable_error_terminal(
  const std::string & error_code,
  bool & terminal) noexcept
{
  const auto rule = kStableErrors.find(error_code);
  if (rule == kStableErrors.end()) {
    terminal = false;
    return false;
  }
  terminal = rule->second.terminal;
  return true;
}

bool is_stable_error_allowed_for_response(
  const std::string & error_code,
  Operation operation) noexcept
{
  const auto rule = kStableErrors.find(error_code);
  return rule != kStableErrors.end() &&
         rule->second.response_operations.find(operation) !=
         rule->second.response_operations.end();
}

void validate_response_correlation(
  const ResponseExpectation & expected,
  const Message & response)
{
  if (!response.is_response) {
    InvalidFrame("the correlated U2R2 frame is not a response");
  }
  if (
    std::find(
      expected.allowed_response_operations().begin(),
      expected.allowed_response_operations().end(),
      response.operation) == expected.allowed_response_operations().end())
  {
    throw ProtocolError(
            "response_mismatch",
            "the U2R2 response operation is not valid for its request");
  }
  const bool is_success =
    response.operation == expected.success_response_operation() &&
    response.status == "ok";
  bool identity_matches;
  if (expected.assigns_session_identity()) {
    identity_matches = is_success
      ? !response.session_id.empty() && response.connection_generation != 0
      : response.session_id.empty() && response.connection_generation == 0;
  } else {
    identity_matches =
      response.session_id == expected.session_id() &&
      response.connection_generation == expected.connection_generation();
  }
  const bool identifiers_match = is_success
    ? response.contract_id == expected.contract_id() &&
      response.message_id == expected.message_id()
    : response.contract_id == 0 && response.message_id == 0;
  if (
    response.request_id != expected.request_id() ||
    !identity_matches ||
    !identifiers_match)
  {
    throw ProtocolError(
            "response_mismatch",
            "the U2R2 response does not match its exact request context");
  }
}
}  // namespace unity2foxglove::ros2_bridge::u2r2
