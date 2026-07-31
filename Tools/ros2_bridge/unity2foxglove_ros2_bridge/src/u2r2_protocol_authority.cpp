// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Pure, transport-independent U2R2 replay, ordering, and resource authority.

#include "unity2foxglove_ros2_bridge/u2r2_protocol_authority.hpp"

#include <algorithm>
#include <array>
#include <atomic>
#include <cctype>
#include <deque>
#include <limits>
#include <list>
#include <mutex>
#include <stdexcept>
#include <unordered_map>
#include <unordered_set>

#include <nlohmann/json.hpp>

namespace unity2foxglove::ros2_bridge::u2r2
{
namespace
{
constexpr std::array<const char *, 27> kLimitNames{{
  "maxConnections",
  "maxDataSessions",
  "maxProbes",
  "maxContracts",
  "maxOutstandingRequests",
  "maxReplayEntries",
  "maxReplayBytes",
  "maxTombstones",
  "fixedFrameBytes",
  "maxHeaderBytes",
  "maxPayloadBytes",
  "maxTransientBytes",
  "maxInFlightBytes",
  "maxQueuedBytes",
  "maxTotalQueueDepth",
  "maxPerContractQueueDepth",
  "maxPerContractQueueBytes",
  "reservedControlQueueDepth",
  "reservedControlQueueBytes",
  "controlBurstLimit",
  "handshakeTimeoutMs",
  "partialFrameTimeoutMs",
  "readTimeoutMs",
  "writeTimeoutMs",
  "joinTimeoutMs",
  "shutdownTimeoutMs",
  "maxJsonDepth",
}};

[[noreturn]] void InvalidConfiguration(const std::string & message)
{
  throw ProtocolError("invalid_configuration", message, true);
}

[[noreturn]] void CapacityExceeded(const std::string & message)
{
  throw ProtocolError("capacity_exceeded", message, false);
}

uint64_t ConfigurationAdd(uint64_t left, uint64_t right)
{
  if (right > std::numeric_limits<uint64_t>::max() - left) {
    InvalidConfiguration("U2R2 limit arithmetic overflowed");
  }
  return left + right;
}

void ValidateLimits(const std::map<std::string, uint64_t> & values)
{
  if (values.size() != kLimitNames.size()) {
    InvalidConfiguration(
      "the U2R2 limit snapshot must contain exactly the named limits");
  }
  for (const auto * name : kLimitNames) {
    const auto found = values.find(name);
    if (found == values.end()) {
      InvalidConfiguration(
        "the U2R2 limit snapshot must contain exactly the named limits");
    }
    if (found->second == 0) {
      InvalidConfiguration("every U2R2 limit must be nonzero");
    }
  }
  for (const auto & [name, unused] : values) {
    (void)unused;
    if (std::find(kLimitNames.begin(), kLimitNames.end(), name) ==
      kLimitNames.end())
    {
      InvalidConfiguration(
        "the U2R2 limit snapshot must contain exactly the named limits");
    }
  }
  if (values.at("maxDataSessions") != 1) {
    InvalidConfiguration("maxDataSessions must be exactly one");
  }

  (void)ConfigurationAdd(
    values.at("maxContracts"), values.at("maxTombstones"));
  const auto role_total = ConfigurationAdd(
    values.at("maxDataSessions"), values.at("maxProbes"));
  if (values.at("maxConnections") < role_total) {
    InvalidConfiguration(
      "maxConnections must contain all data-session and probe leases");
  }

  const auto maximum_frame = ConfigurationAdd(
    values.at("fixedFrameBytes"),
    ConfigurationAdd(
      values.at("maxHeaderBytes"), values.at("maxPayloadBytes")));
  if (
    values.at("maxPerContractQueueBytes") < maximum_frame ||
    values.at("maxQueuedBytes") < maximum_frame ||
    values.at("maxTransientBytes") < maximum_frame ||
    values.at("maxInFlightBytes") < maximum_frame)
  {
    InvalidConfiguration(
      "every frame-holding byte budget must contain one maximum frame");
  }
  if (
    values.at("reservedControlQueueDepth") >=
    values.at("maxTotalQueueDepth"))
  {
    InvalidConfiguration(
      "reserved control depth must leave at least one data slot");
  }
  if (
    values.at("maxTotalQueueDepth") <
    ConfigurationAdd(
      values.at("reservedControlQueueDepth"),
      values.at("maxPerContractQueueDepth")))
  {
    InvalidConfiguration(
      "the total queue depth must contain the control reserve and one contract");
  }
  if (
    values.at("maxQueuedBytes") <
    ConfigurationAdd(
      values.at("reservedControlQueueBytes"),
      values.at("maxPerContractQueueBytes")))
  {
    InvalidConfiguration(
      "the queued-byte budget must contain the control reserve and one contract");
  }
  if (
    values.at("controlBurstLimit") >
    values.at("reservedControlQueueDepth"))
  {
    InvalidConfiguration(
      "the control burst limit cannot exceed reserved control depth");
  }
  if (
    values.at("maxReplayEntries") <
    values.at("maxOutstandingRequests"))
  {
    InvalidConfiguration(
      "replay entry capacity must contain every outstanding request");
  }
}

struct LeaseSettlement final
{
  explicit LeaseSettlement(std::function<void()> callback)
  : callback_(std::move(callback))
  {
  }

  bool settle()
  {
    bool expected = false;
    if (!settled_.compare_exchange_strong(expected, true)) {
      return false;
    }
    callback_();
    return true;
  }

private:
  std::atomic<bool> settled_{false};
  std::function<void()> callback_;
};

struct ControlSettlement final
{
  ControlSettlement(
    uint64_t reserved_bytes,
    std::function<void(
      OutboundFrame,
      std::optional<ContractKey>)> commit,
    std::function<void()> cancel)
  : reserved_bytes_(reserved_bytes),
    commit_(std::move(commit)),
    cancel_(std::move(cancel))
  {
  }

  bool try_commit(
    OutboundFrame frame,
    std::optional<ContractKey> fence_contract)
  {
    std::lock_guard<std::mutex> lock(mutex_);
    if (settled_) {
      return false;
    }
    if (!frame.is_control()) {
      throw std::invalid_argument(
              "a control reservation requires a control frame");
    }
    if (frame.byte_count() > reserved_bytes_) {
      CapacityExceeded(
        "the U2R2 control response exceeds its reservation");
    }
    commit_(std::move(frame), fence_contract);
    settled_ = true;
    return true;
  }

  bool try_cancel()
  {
    std::lock_guard<std::mutex> lock(mutex_);
    if (settled_) {
      return false;
    }
    cancel_();
    settled_ = true;
    return true;
  }

private:
  std::mutex mutex_;
  uint64_t reserved_bytes_;
  bool settled_{false};
  std::function<void(
    OutboundFrame,
    std::optional<ContractKey>)> commit_;
  std::function<void()> cancel_;
};

template<typename T>
std::shared_ptr<T> SettlementAs(const std::shared_ptr<void> & value)
{
  return std::static_pointer_cast<T>(value);
}

struct ContractKeyHash final
{
  size_t operator()(const ContractKey & key) const noexcept
  {
    const auto left = std::hash<uint64_t>{}(key.contract_id);
    const auto right = std::hash<uint64_t>{}(key.generation);
    return left ^ (right + 0x9e3779b9U + (left << 6U) + (left >> 2U));
  }
};

bool IsAsciiAlpha(char value)
{
  return
    (value >= 'a' && value <= 'z') ||
    (value >= 'A' && value <= 'Z');
}

bool IsAsciiAlnumOrUnderscore(char value)
{
  return IsAsciiAlpha(value) ||
         (value >= '0' && value <= '9') ||
         value == '_';
}

[[noreturn]] void InvalidContract(const std::string & message)
{
  throw ProtocolError("invalid_contract", message, false);
}

void ValidateTopic(const std::string & topic)
{
  if (topic.size() < 2 || topic.front() != '/' || topic.back() == '/') {
    InvalidContract("a U2R2 topic must be a canonical absolute name");
  }
  size_t segment_start = 1;
  for (size_t index = 1; index <= topic.size(); ++index) {
    if (index != topic.size() && topic[index] != '/') {
      if (!IsAsciiAlnumOrUnderscore(topic[index])) {
        InvalidContract("a U2R2 topic contains a non-canonical character");
      }
      continue;
    }
    if (
      index == segment_start ||
      (!IsAsciiAlpha(topic[segment_start]) &&
      topic[segment_start] != '_'))
    {
      InvalidContract(
        "every U2R2 topic segment must start with an ASCII letter");
    }
    segment_start = index + 1;
  }
}

bool IsCanonicalPackage(const std::string & package)
{
  if (
    package.size() < 2 || package.size() > 255 ||
    package.front() < 'a' || package.front() > 'z' ||
    package.back() == '_' ||
    package.find("__") != std::string::npos)
  {
    return false;
  }
  return std::all_of(
    package.begin(),
    package.end(),
    [](char value) {
      return
        (value >= 'a' && value <= 'z') ||
        (value >= '0' && value <= '9') ||
        value == '_';
    });
}

bool IsCanonicalType(const std::string & type)
{
  if (
    type.empty() || type.size() > 255 ||
    type.front() < 'A' || type.front() > 'Z' ||
    type.front() == '_' || type.back() == '_' ||
    type.find("__") != std::string::npos)
  {
    return false;
  }
  return std::all_of(type.begin(), type.end(), IsAsciiAlpha) ||
         std::all_of(
    type.begin(),
    type.end(),
    [](char value) {
      return IsAsciiAlpha(value) || (value >= '0' && value <= '9');
    });
}

void ValidateSchemaName(const std::string & schema_name)
{
  const auto first = schema_name.find('/');
  const auto second =
    first == std::string::npos
    ? std::string::npos
    : schema_name.find('/', first + 1);
  if (
    first == std::string::npos ||
    second == std::string::npos ||
    schema_name.find('/', second + 1) != std::string::npos ||
    schema_name.substr(first + 1, second - first - 1) != "msg" ||
    !IsCanonicalPackage(schema_name.substr(0, first)) ||
    !IsCanonicalType(schema_name.substr(second + 1)))
  {
    InvalidContract(
      "a U2R2 schemaName must be canonical package/msg/Type");
  }
}

std::string RequiredContractString(
  const nlohmann::json & header,
  const std::string & name)
{
  const auto found = header.find(name);
  if (
    found == header.end() ||
    !found->is_string() ||
    found->get<std::string>().empty())
  {
    InvalidContract(
      "U2R2 contract field " + name + " must be a nonempty string");
  }
  return found->get<std::string>();
}

std::string RequiredQosString(
  const nlohmann::json & qos,
  const std::string & name,
  std::initializer_list<const char *> allowed)
{
  const auto found = qos.find(name);
  if (found == qos.end() || !found->is_string()) {
    InvalidContract("U2R2 qos field " + name + " must be a string");
  }
  const auto value = found->get<std::string>();
  if (std::find(allowed.begin(), allowed.end(), value) == allowed.end()) {
    InvalidContract("U2R2 qos field " + name + " is invalid");
  }
  return value;
}
}  // namespace

ProtocolLimits::ProtocolLimits(std::map<std::string, uint64_t> values)
: values_(std::move(values))
{
  ValidateLimits(values_);
}

ProtocolLimits ProtocolLimits::defaults()
{
  constexpr uint64_t fixed = 16;
  constexpr uint64_t header = 64U * 1024U;
  constexpr uint64_t payload = 64U * 1024U * 1024U;
  constexpr uint64_t maximum_frame = fixed + header + payload;
  return ProtocolLimits({{
    {"maxConnections", 9},
    {"maxDataSessions", 1},
    {"maxProbes", 8},
    {"maxContracts", 64},
    {"maxOutstandingRequests", 8},
    {"maxReplayEntries", 16},
    {"maxReplayBytes", 4U * 1024U * 1024U},
    {"maxTombstones", 32},
    {"fixedFrameBytes", fixed},
    {"maxHeaderBytes", header},
    {"maxPayloadBytes", payload},
    {"maxTransientBytes", maximum_frame * 2U},
    {"maxInFlightBytes", maximum_frame * 2U},
    {"maxQueuedBytes", maximum_frame * 4U},
    {"maxTotalQueueDepth", 128},
    {"maxPerContractQueueDepth", 8},
    {"maxPerContractQueueBytes", maximum_frame * 2U},
    {"reservedControlQueueDepth", 8},
    {"reservedControlQueueBytes", 1024U * 1024U},
    {"controlBurstLimit", 2},
    {"handshakeTimeoutMs", 5000},
    {"partialFrameTimeoutMs", 2000},
    {"readTimeoutMs", 5000},
    {"writeTimeoutMs", 5000},
    {"joinTimeoutMs", 5000},
    {"shutdownTimeoutMs", 10000},
    {"maxJsonDepth", 64},
  }});
}

ProtocolLimits ProtocolLimits::from_diagnostic_snapshot(
  const std::map<std::string, uint64_t> & values)
{
  return ProtocolLimits(values);
}

ProtocolLimits ProtocolLimits::with(
  std::initializer_list<std::pair<const std::string, uint64_t>> overrides) const
{
  auto values = values_;
  for (const auto & [name, value] : overrides) {
    if (values.find(name) == values.end()) {
      InvalidConfiguration("unknown U2R2 limit: " + name);
    }
    values[name] = value;
  }
  return ProtocolLimits(std::move(values));
}

std::map<std::string, uint64_t> ProtocolLimits::to_diagnostic_snapshot() const
{
  return values_;
}

uint64_t ProtocolLimits::value(const std::string & name) const
{
  return values_.at(name);
}

#define U2R2_LIMIT_ACCESSOR(method, name) \
  uint64_t ProtocolLimits::method() const {return value(name);}
U2R2_LIMIT_ACCESSOR(max_connections, "maxConnections")
U2R2_LIMIT_ACCESSOR(max_data_sessions, "maxDataSessions")
U2R2_LIMIT_ACCESSOR(max_probes, "maxProbes")
U2R2_LIMIT_ACCESSOR(max_contracts, "maxContracts")
U2R2_LIMIT_ACCESSOR(max_outstanding_requests, "maxOutstandingRequests")
U2R2_LIMIT_ACCESSOR(max_replay_entries, "maxReplayEntries")
U2R2_LIMIT_ACCESSOR(max_replay_bytes, "maxReplayBytes")
U2R2_LIMIT_ACCESSOR(max_tombstones, "maxTombstones")
U2R2_LIMIT_ACCESSOR(fixed_frame_bytes, "fixedFrameBytes")
U2R2_LIMIT_ACCESSOR(max_header_bytes, "maxHeaderBytes")
U2R2_LIMIT_ACCESSOR(max_payload_bytes, "maxPayloadBytes")
U2R2_LIMIT_ACCESSOR(max_transient_bytes, "maxTransientBytes")
U2R2_LIMIT_ACCESSOR(max_in_flight_bytes, "maxInFlightBytes")
U2R2_LIMIT_ACCESSOR(max_queued_bytes, "maxQueuedBytes")
U2R2_LIMIT_ACCESSOR(max_total_queue_depth, "maxTotalQueueDepth")
U2R2_LIMIT_ACCESSOR(max_per_contract_queue_depth, "maxPerContractQueueDepth")
U2R2_LIMIT_ACCESSOR(max_per_contract_queue_bytes, "maxPerContractQueueBytes")
U2R2_LIMIT_ACCESSOR(reserved_control_queue_depth, "reservedControlQueueDepth")
U2R2_LIMIT_ACCESSOR(reserved_control_queue_bytes, "reservedControlQueueBytes")
U2R2_LIMIT_ACCESSOR(control_burst_limit, "controlBurstLimit")
U2R2_LIMIT_ACCESSOR(handshake_timeout_ms, "handshakeTimeoutMs")
U2R2_LIMIT_ACCESSOR(partial_frame_timeout_ms, "partialFrameTimeoutMs")
U2R2_LIMIT_ACCESSOR(read_timeout_ms, "readTimeoutMs")
U2R2_LIMIT_ACCESSOR(write_timeout_ms, "writeTimeoutMs")
U2R2_LIMIT_ACCESSOR(join_timeout_ms, "joinTimeoutMs")
U2R2_LIMIT_ACCESSOR(shutdown_timeout_ms, "shutdownTimeoutMs")
U2R2_LIMIT_ACCESSOR(max_json_depth, "maxJsonDepth")
#undef U2R2_LIMIT_ACCESSOR

uint64_t checked_add(
  uint64_t current,
  uint64_t increment,
  uint64_t limit,
  const std::string & budget_name)
{
  if (current > limit || increment > limit - current) {
    CapacityExceeded(
      "the U2R2 " +
      (budget_name.empty() ? std::string("budget") : budget_name) +
      " is exhausted");
  }
  return current + increment;
}

FrameSize::FrameSize(
  uint64_t header_bytes,
  uint64_t payload_bytes,
  uint64_t total_bytes) noexcept
: header_bytes_(header_bytes),
  payload_bytes_(payload_bytes),
  total_bytes_(total_bytes)
{
}

FrameSize FrameSize::create(
  const ProtocolLimits & limits,
  uint64_t header_bytes,
  uint64_t payload_bytes)
{
  if (
    header_bytes > limits.max_header_bytes() ||
    payload_bytes > limits.max_payload_bytes())
  {
    CapacityExceeded(
      "the U2R2 frame exceeds its header or payload budget");
  }
  const auto variable = checked_add(
    header_bytes,
    payload_bytes,
    std::numeric_limits<uint64_t>::max(),
    "frame");
  return FrameSize(
    header_bytes,
    payload_bytes,
    checked_add(
      limits.fixed_frame_bytes(),
      variable,
      std::numeric_limits<uint64_t>::max(),
      "frame"));
}

uint64_t FrameSize::header_bytes() const noexcept {return header_bytes_;}
uint64_t FrameSize::payload_bytes() const noexcept {return payload_bytes_;}
uint64_t FrameSize::total_bytes() const noexcept {return total_bytes_;}

struct CapacityCounter::Impl final
{
  explicit Impl(uint64_t capacity_value)
  : capacity(capacity_value)
  {
  }
  mutable std::mutex mutex;
  uint64_t capacity;
  uint64_t count{0};
};

CapacityCounter::CapacityCounter(uint64_t capacity)
: impl_(std::make_unique<Impl>(capacity))
{
  if (capacity == 0) {
    throw std::invalid_argument("capacity must be nonzero");
  }
}

CapacityCounter::~CapacityCounter() = default;

bool CapacityCounter::try_acquire()
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  if (impl_->count == impl_->capacity) {
    return false;
  }
  ++impl_->count;
  return true;
}

void CapacityCounter::release()
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  if (impl_->count == 0) {
    throw std::logic_error("the U2R2 capacity counter is already empty");
  }
  --impl_->count;
}

uint64_t CapacityCounter::count() const
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  return impl_->count;
}

ResourceLease::ResourceLease(std::shared_ptr<void> settlement)
: settlement_(std::move(settlement))
{
}

ResourceLease::~ResourceLease()
{
  release();
}

ResourceLease & ResourceLease::operator=(ResourceLease && other) noexcept
{
  if (this != &other) {
    try {
      release();
    } catch (...) {
      std::terminate();
    }
    settlement_ = std::move(other.settlement_);
  }
  return *this;
}

bool ResourceLease::release()
{
  if (!settlement_) {
    return false;
  }
  return SettlementAs<LeaseSettlement>(settlement_)->settle();
}

struct SessionResourceAuthority::Impl final
{
  explicit Impl(const ProtocolLimits & value)
  : limits(value)
  {
  }
  mutable std::mutex mutex;
  ProtocolLimits limits;
  uint64_t connections{0};
  uint64_t data_sessions{0};
  uint64_t probes{0};
};

SessionResourceAuthority::SessionResourceAuthority(const ProtocolLimits & limits)
: impl_(std::make_shared<Impl>(limits))
{
}

SessionResourceAuthority::~SessionResourceAuthority() = default;

std::optional<ResourceLease> SessionResourceAuthority::try_acquire(
  ConnectionRole role)
{
  auto state = impl_;
  {
    std::lock_guard<std::mutex> lock(state->mutex);
    if (state->connections == state->limits.max_connections()) {
      return std::nullopt;
    }
    if (role == ConnectionRole::data_session) {
      if (state->data_sessions == state->limits.max_data_sessions()) {
        return std::nullopt;
      }
      ++state->data_sessions;
    } else if (role == ConnectionRole::probe) {
      if (state->probes == state->limits.max_probes()) {
        return std::nullopt;
      }
      ++state->probes;
    } else {
      throw std::invalid_argument("unknown U2R2 connection role");
    }
    ++state->connections;
  }
  auto settlement = std::make_shared<LeaseSettlement>(
    [state, role]() {
      std::lock_guard<std::mutex> lock(state->mutex);
      if (state->connections == 0) {
        throw std::logic_error("no U2R2 connection lease is active");
      }
      if (role == ConnectionRole::data_session) {
        if (state->data_sessions == 0) {
          throw std::logic_error("no U2R2 data lease is active");
        }
        --state->data_sessions;
      } else {
        if (state->probes == 0) {
          throw std::logic_error("no U2R2 probe lease is active");
        }
        --state->probes;
      }
      --state->connections;
    });
  return ResourceLease(settlement);
}

uint64_t SessionResourceAuthority::connection_count() const
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  return impl_->connections;
}

RequestIdCounter::RequestIdCounter(uint64_t current) noexcept
: current_(current)
{
}

uint64_t RequestIdCounter::next()
{
  std::lock_guard<std::mutex> lock(mutex_);
  if (faulted_ || current_ == std::numeric_limits<uint64_t>::max()) {
    faulted_ = true;
    throw ProtocolError(
            "request_id_exhausted",
            "the U2R2 request ID counter is exhausted",
            true);
  }
  return ++current_;
}

bool RequestIdCounter::is_faulted() const
{
  std::lock_guard<std::mutex> lock(mutex_);
  return faulted_;
}

ContractKey::ContractKey(uint64_t id, uint64_t generation_value)
: contract_id(id),
  generation(generation_value)
{
  if (contract_id == 0 || generation == 0) {
    InvalidContract("a U2R2 contract identity must be nonzero");
  }
}

ContractIdentity::ContractIdentity(
  ContractKey key_value,
  ContractDirection direction_value,
  std::string topic_value,
  std::string schema_name_value,
  Qos qos_value)
: key(key_value),
  direction(direction_value),
  topic(std::move(topic_value)),
  schema_name(std::move(schema_name_value)),
  qos(std::move(qos_value))
{
  if (
    direction != ContractDirection::publish &&
    direction != ContractDirection::subscribe)
  {
    InvalidContract("the U2R2 contract direction is invalid");
  }
  ValidateTopic(topic);
  ValidateSchemaName(schema_name);
  nlohmann::json header{
    {"topic", topic},
    {"schemaName", schema_name},
    {"encoding", "cdr"},
    {"qos", {
      {"profile", qos.profile},
      {"reliability", qos.reliability},
      {"durability", qos.durability},
      {"history", qos.history},
      {"depth", qos.depth},
    }},
  };
  std::string parsed_topic;
  std::string parsed_schema;
  std::optional<Qos> parsed_qos;
  parse_contract_fields(
    header,
    direction == ContractDirection::publish
    ? Operation::PreparePublisher
    : Operation::RegisterSubscription,
    parsed_topic,
    parsed_schema,
    parsed_qos);
}

OutboundFrame::OutboundFrame(
  std::string token,
  bool is_control,
  ContractKey contract,
  uint64_t sequence,
  std::vector<uint8_t> bytes)
: token_(std::move(token)),
  is_control_(is_control),
  contract_(contract),
  sequence_(sequence),
  bytes_(std::move(bytes))
{
}

OutboundFrame OutboundFrame::control(
  std::string token,
  std::vector<uint8_t> bytes)
{
  return OutboundFrame(
    std::move(token), true, ContractKey(1, 1), 0, std::move(bytes));
}

OutboundFrame OutboundFrame::data(
  std::string token,
  ContractKey contract,
  uint64_t sequence,
  std::vector<uint8_t> bytes)
{
  if (sequence == 0) {
    throw ProtocolError(
            "contract_sequence_fault",
            "a U2R2 data sequence must be nonzero",
            false);
  }
  return OutboundFrame(
    std::move(token), false, contract, sequence, std::move(bytes));
}

const std::string & OutboundFrame::token() const noexcept {return token_;}
bool OutboundFrame::is_control() const noexcept {return is_control_;}
const ContractKey & OutboundFrame::contract() const noexcept {return contract_;}
uint64_t OutboundFrame::sequence() const noexcept {return sequence_;}
const std::vector<uint8_t> & OutboundFrame::bytes() const noexcept {return bytes_;}
uint64_t OutboundFrame::byte_count() const noexcept
{
  return static_cast<uint64_t>(bytes_.size());
}

ControlReservation::ControlReservation(std::shared_ptr<void> settlement)
: settlement_(std::move(settlement))
{
}

ControlReservation::~ControlReservation()
{
  try_cancel();
}

ControlReservation & ControlReservation::operator=(
  ControlReservation && other) noexcept
{
  if (this != &other) {
    try {
      try_cancel();
    } catch (...) {
      std::terminate();
    }
    settlement_ = std::move(other.settlement_);
  }
  return *this;
}

void ControlReservation::commit(OutboundFrame frame)
{
  if (!try_commit(std::move(frame))) {
    throw std::logic_error("the control reservation is settled");
  }
}

bool ControlReservation::try_commit(OutboundFrame frame)
{
  if (!settlement_) {
    return false;
  }
  return SettlementAs<ControlSettlement>(settlement_)->try_commit(
    std::move(frame), std::nullopt);
}

bool ControlReservation::try_commit_fenced(
  OutboundFrame frame,
  const ContractKey & fence_contract)
{
  if (!settlement_) {
    return false;
  }
  return SettlementAs<ControlSettlement>(settlement_)->try_commit(
    std::move(frame), fence_contract);
}

bool ControlReservation::try_cancel()
{
  if (!settlement_) {
    return false;
  }
  return SettlementAs<ControlSettlement>(settlement_)->try_cancel();
}

ByteLease::ByteLease(std::shared_ptr<void> settlement)
: settlement_(std::move(settlement))
{
}

ByteLease::~ByteLease()
{
  release();
}

ByteLease & ByteLease::operator=(ByteLease && other) noexcept
{
  if (this != &other) {
    try {
      release();
    } catch (...) {
      std::terminate();
    }
    settlement_ = std::move(other.settlement_);
  }
  return *this;
}

bool ByteLease::release()
{
  if (!settlement_) {
    return false;
  }
  return SettlementAs<LeaseSettlement>(settlement_)->settle();
}

WriteLease::WriteLease(
  OutboundFrame frame,
  std::shared_ptr<void> settlement)
: frame_(std::make_shared<OutboundFrame>(std::move(frame))),
  settlement_(std::move(settlement))
{
}

WriteLease::~WriteLease()
{
  release();
}

WriteLease & WriteLease::operator=(WriteLease && other) noexcept
{
  if (this != &other) {
    try {
      release();
    } catch (...) {
      std::terminate();
    }
    frame_ = std::move(other.frame_);
    settlement_ = std::move(other.settlement_);
  }
  return *this;
}

const OutboundFrame & WriteLease::frame() const
{
  if (!frame_) {
    throw std::logic_error("the U2R2 writer lease has no frame");
  }
  return *frame_;
}

bool WriteLease::release()
{
  if (!settlement_) {
    return false;
  }
  return SettlementAs<LeaseSettlement>(settlement_)->settle();
}

struct BoundedOutboundScheduler::Impl final
{
  struct ControlEntry
  {
    OutboundFrame frame;
    std::optional<ContractKey> fence_contract;
  };

  explicit Impl(const ProtocolLimits & value)
  : limits(value)
  {
  }

  mutable std::mutex mutex;
  ProtocolLimits limits;
  std::deque<ControlEntry> control;
  std::unordered_map<
    ContractKey,
    std::deque<OutboundFrame>,
    ContractKeyHash> data;
  std::deque<ContractKey> round_robin;
  std::unordered_set<ContractKey, ContractKeyHash> active;
  std::unordered_set<ContractKey, ContractKeyHash> revoked;
  std::unordered_set<ContractKey, ContractKeyHash> retire_when_drained;
  uint64_t control_depth_used{0};
  uint64_t control_bytes_used{0};
  uint64_t data_queued_depth{0};
  uint64_t data_queued_bytes{0};
  uint64_t transient_bytes{0};
  uint64_t in_flight_bytes{0};
  uint64_t control_burst{0};
  bool reader_active{false};
  bool writer_active{false};
  std::optional<ContractKey> writer_contract;
  bool closed{false};

  void activate(const ContractKey & key)
  {
    if (active.insert(key).second) {
      round_robin.push_back(key);
    }
  }

  void remove_from_round_robin(const ContractKey & key)
  {
    active.erase(key);
    round_robin.erase(
      std::remove(round_robin.begin(), round_robin.end(), key),
      round_robin.end());
  }

  bool is_drained(const ContractKey & key) const
  {
    return
      data.find(key) == data.end() &&
      (!writer_active ||
      !writer_contract.has_value() ||
      *writer_contract != key);
  }

  void ensure_revoked_admission(const ContractKey & key) const
  {
    if (revoked.find(key) != revoked.end()) {
      return;
    }
    const auto bound =
      limits.max_contracts() + limits.max_tombstones();
    if (static_cast<uint64_t>(revoked.size()) >= bound) {
      throw std::logic_error(
              "the U2R2 revoked-contract lifecycle exceeded its bound");
    }
  }

  void revoke(const ContractKey & key)
  {
    ensure_revoked_admission(key);
    revoked.insert(key);
    const auto found = data.find(key);
    if (found != data.end()) {
      for (const auto & frame : found->second) {
        if (
          data_queued_depth == 0 ||
          data_queued_bytes < frame.byte_count())
        {
          throw std::logic_error("data queue accounting underflow");
        }
        --data_queued_depth;
        data_queued_bytes -= frame.byte_count();
      }
      data.erase(found);
    }
    remove_from_round_robin(key);
  }

  void try_forget_retired(const ContractKey & key)
  {
    if (
      retire_when_drained.find(key) == retire_when_drained.end() ||
      !is_drained(key))
    {
      return;
    }
    retire_when_drained.erase(key);
    revoked.erase(key);
  }

  uint64_t contract_bytes(
    const std::deque<OutboundFrame> & queue) const
  {
    uint64_t result = 0;
    for (const auto & frame : queue) {
      result = checked_add(
        result,
        frame.byte_count(),
        limits.max_per_contract_queue_bytes(),
        "per-contract queued bytes");
    }
    return result;
  }

  bool can_fit_data(
    const std::deque<OutboundFrame> & queue,
    uint64_t incoming_bytes,
    uint64_t removing_depth,
    uint64_t removing_bytes) const
  {
    const auto queue_depth = static_cast<uint64_t>(queue.size());
    if (removing_depth > queue_depth ||
      removing_bytes > data_queued_bytes)
    {
      throw std::logic_error("U2R2 queue accounting underflow");
    }
    const auto contract_depth = queue_depth - removing_depth;
    const auto current_contract_bytes = contract_bytes(queue);
    if (removing_bytes > current_contract_bytes) {
      throw std::logic_error("U2R2 contract queue accounting underflow");
    }
    const auto retained_contract_bytes =
      current_contract_bytes - removing_bytes;
    const auto data_depth_limit =
      limits.max_total_queue_depth() -
      limits.reserved_control_queue_depth();
    const auto data_byte_limit =
      limits.max_queued_bytes() -
      limits.reserved_control_queue_bytes();
    const auto retained_data_depth = data_queued_depth - removing_depth;
    const auto retained_data_bytes = data_queued_bytes - removing_bytes;
    return
      contract_depth < limits.max_per_contract_queue_depth() &&
      retained_contract_bytes <= limits.max_per_contract_queue_bytes() &&
      incoming_bytes <=
      limits.max_per_contract_queue_bytes() - retained_contract_bytes &&
      retained_data_depth < data_depth_limit &&
      retained_data_bytes <= data_byte_limit &&
      incoming_bytes <= data_byte_limit - retained_data_bytes;
  }
};

BoundedOutboundScheduler::BoundedOutboundScheduler(
  const ProtocolLimits & limits)
: impl_(std::make_shared<Impl>(limits))
{
}

BoundedOutboundScheduler::~BoundedOutboundScheduler() = default;

std::optional<ControlReservation>
BoundedOutboundScheduler::try_reserve_control(uint64_t bytes)
{
  auto state = impl_;
  {
    std::lock_guard<std::mutex> lock(state->mutex);
    if (
      state->closed ||
      state->control_depth_used >=
      state->limits.reserved_control_queue_depth() ||
      state->control_bytes_used >
      state->limits.reserved_control_queue_bytes() ||
      bytes >
      state->limits.reserved_control_queue_bytes() -
      state->control_bytes_used)
    {
      return std::nullopt;
    }
    ++state->control_depth_used;
    state->control_bytes_used += bytes;
  }

  auto settlement = std::make_shared<ControlSettlement>(
    bytes,
    [state, bytes](
      OutboundFrame frame,
      std::optional<ContractKey> fence_contract) {
      std::lock_guard<std::mutex> lock(state->mutex);
      if (state->closed) {
        throw std::logic_error(
                "the U2R2 outbound scheduler is closed");
      }
      if (
        state->control_depth_used == 0 ||
        state->control_bytes_used < bytes)
      {
        throw std::logic_error(
                "the U2R2 control reservation is not active");
      }
      state->control_bytes_used -= bytes - frame.byte_count();
      Impl::ControlEntry entry{
        std::move(frame),
        std::move(fence_contract)};
      if (entry.fence_contract.has_value()) {
        const auto insertion = std::find_if(
          state->control.begin(),
          state->control.end(),
          [](const Impl::ControlEntry & queued) {
            return !queued.fence_contract.has_value();
          });
        state->control.insert(insertion, std::move(entry));
      } else {
        state->control.push_back(std::move(entry));
      }
    },
    [state, bytes]() {
      std::lock_guard<std::mutex> lock(state->mutex);
      if (state->closed) {
        return;
      }
      if (
        state->control_depth_used == 0 ||
        state->control_bytes_used < bytes)
      {
        throw std::logic_error(
                "the U2R2 control reservation is not active");
      }
      --state->control_depth_used;
      state->control_bytes_used -= bytes;
    });
  return ControlReservation(settlement);
}

EnqueueDisposition BoundedOutboundScheduler::enqueue_data(
  OutboundFrame frame,
  QueueOverflowPolicy policy)
{
  if (
    policy != QueueOverflowPolicy::reject &&
    policy != QueueOverflowPolicy::drop_oldest &&
    policy != QueueOverflowPolicy::replace_latest)
  {
    InvalidContract("the U2R2 queue overflow policy is invalid");
  }
  if (frame.is_control()) {
    throw std::invalid_argument("a data queue requires a data frame");
  }
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  if (state->closed) {
    return EnqueueDisposition::rejected;
  }
  const auto key = frame.contract();
  if (state->revoked.find(key) != state->revoked.end()) {
    return EnqueueDisposition::rejected;
  }
  auto [found, inserted] = state->data.try_emplace(key);
  auto & queue = found->second;
  if (state->can_fit_data(queue, frame.byte_count(), 0, 0)) {
    queue.push_back(std::move(frame));
    ++state->data_queued_depth;
    state->data_queued_bytes += queue.back().byte_count();
    state->activate(key);
    return EnqueueDisposition::accepted;
  }
  if (policy == QueueOverflowPolicy::reject || queue.empty()) {
    if (queue.empty()) {
      state->data.erase(key);
    }
    return EnqueueDisposition::rejected;
  }

  const auto victim_bytes =
    policy == QueueOverflowPolicy::drop_oldest
    ? queue.front().byte_count()
    : queue.back().byte_count();
  if (!state->can_fit_data(
      queue,
      frame.byte_count(),
      1,
      victim_bytes))
  {
    return EnqueueDisposition::rejected;
  }
  if (policy == QueueOverflowPolicy::drop_oldest) {
    queue.pop_front();
  } else {
    queue.pop_back();
  }
  --state->data_queued_depth;
  state->data_queued_bytes -= victim_bytes;
  queue.push_back(std::move(frame));
  ++state->data_queued_depth;
  state->data_queued_bytes += queue.back().byte_count();
  state->activate(key);
  return
    policy == QueueOverflowPolicy::drop_oldest
    ? EnqueueDisposition::dropped_oldest
    : EnqueueDisposition::replaced_latest;
}

std::optional<ByteLease>
BoundedOutboundScheduler::try_reserve_transient(uint64_t bytes)
{
  auto state = impl_;
  {
    std::lock_guard<std::mutex> lock(state->mutex);
    if (
      state->closed ||
      state->transient_bytes > state->limits.max_transient_bytes() ||
      bytes >
      state->limits.max_transient_bytes() - state->transient_bytes)
    {
      return std::nullopt;
    }
    state->transient_bytes += bytes;
  }
  auto settlement = std::make_shared<LeaseSettlement>(
    [state, bytes]() {
      std::lock_guard<std::mutex> lock(state->mutex);
      if (state->transient_bytes < bytes) {
        throw std::logic_error("transient byte accounting underflow");
      }
      state->transient_bytes -= bytes;
    });
  return ByteLease(settlement);
}

std::optional<ByteLease>
BoundedOutboundScheduler::try_begin_read(uint64_t bytes)
{
  auto state = impl_;
  {
    std::lock_guard<std::mutex> lock(state->mutex);
    if (
      state->closed ||
      state->reader_active ||
      state->in_flight_bytes > state->limits.max_in_flight_bytes() ||
      bytes >
      state->limits.max_in_flight_bytes() - state->in_flight_bytes)
    {
      return std::nullopt;
    }
    state->reader_active = true;
    state->in_flight_bytes += bytes;
  }
  auto settlement = std::make_shared<LeaseSettlement>(
    [state, bytes]() {
      std::lock_guard<std::mutex> lock(state->mutex);
      if (!state->reader_active || state->in_flight_bytes < bytes) {
        throw std::logic_error("reader byte accounting underflow");
      }
      state->reader_active = false;
      state->in_flight_bytes -= bytes;
    });
  return ByteLease(settlement);
}

std::optional<WriteLease> BoundedOutboundScheduler::try_begin_write()
{
  auto state = impl_;
  std::optional<OutboundFrame> selected;
  {
    std::lock_guard<std::mutex> lock(state->mutex);
    if (state->closed || state->writer_active) {
      return std::nullopt;
    }
    bool choose_control = false;
    std::optional<ContractKey> selected_contract;
    if (state->control.empty()) {
      if (state->round_robin.empty()) {
        return std::nullopt;
      }
      selected_contract = state->round_robin.front();
    } else if (
      state->round_robin.empty() ||
      state->control_burst < state->limits.control_burst_limit())
    {
      choose_control = true;
    } else {
      const auto eligible = std::find_if(
        state->round_robin.begin(),
        state->round_robin.end(),
        [&](const ContractKey & candidate) {
          return std::none_of(
            state->control.begin(),
            state->control.end(),
            [&](const Impl::ControlEntry & control) {
              return
                control.fence_contract.has_value() &&
                *control.fence_contract == candidate;
            });
        });
      if (eligible == state->round_robin.end()) {
        choose_control = true;
      } else {
        selected_contract = *eligible;
      }
    }

    const auto candidate_bytes =
      choose_control
      ? state->control.front().frame.byte_count()
      : state->data.at(*selected_contract).front().byte_count();
    if (
      state->in_flight_bytes > state->limits.max_in_flight_bytes() ||
      candidate_bytes >
      state->limits.max_in_flight_bytes() - state->in_flight_bytes)
    {
      return std::nullopt;
    }

    if (choose_control) {
      selected.emplace(std::move(state->control.front().frame));
      state->control.pop_front();
      --state->control_depth_used;
      state->control_bytes_used -= candidate_bytes;
      ++state->control_burst;
      state->writer_contract.reset();
    } else {
      const auto key = *selected_contract;
      const auto scheduled = std::find(
        state->round_robin.begin(),
        state->round_robin.end(),
        key);
      if (
        scheduled == state->round_robin.end() ||
        state->active.erase(key) != 1)
      {
        throw std::logic_error(
                "the selected U2R2 contract is not scheduled");
      }
      state->round_robin.erase(scheduled);
      auto & queue = state->data.at(key);
      selected.emplace(std::move(queue.front()));
      queue.pop_front();
      --state->data_queued_depth;
      state->data_queued_bytes -= candidate_bytes;
      if (queue.empty()) {
        state->data.erase(key);
      } else {
        state->activate(key);
      }
      state->control_burst = 0;
      state->writer_contract = key;
    }
    state->writer_active = true;
    state->in_flight_bytes += candidate_bytes;
  }

  const auto bytes = selected->byte_count();
  auto settlement = std::make_shared<LeaseSettlement>(
    [state, bytes]() {
      std::lock_guard<std::mutex> lock(state->mutex);
      if (!state->writer_active || state->in_flight_bytes < bytes) {
        throw std::logic_error("writer byte accounting underflow");
      }
      const auto completed_contract = state->writer_contract;
      state->writer_active = false;
      state->writer_contract.reset();
      state->in_flight_bytes -= bytes;
      if (completed_contract.has_value()) {
        state->try_forget_retired(*completed_contract);
      }
    });
  return WriteLease(std::move(*selected), settlement);
}

void BoundedOutboundScheduler::revoke_contract(const ContractKey & key)
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  if (state->closed) {
    return;
  }
  state->revoke(key);
}

bool BoundedOutboundScheduler::is_contract_revoked_and_drained(
  const ContractKey & key) const
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  return
    state->revoked.find(key) != state->revoked.end() &&
    state->data.find(key) == state->data.end() &&
    (!state->writer_active ||
    !state->writer_contract.has_value() ||
    *state->writer_contract != key);
}

void BoundedOutboundScheduler::activate_contract(const ContractKey & key)
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  if (state->closed) {
    return;
  }
  state->revoked.erase(key);
  state->retire_when_drained.erase(key);
}

void BoundedOutboundScheduler::retire_contract(const ContractKey & key)
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  if (state->closed) {
    return;
  }
  state->revoke(key);
  state->retire_when_drained.insert(key);
  state->try_forget_retired(key);
}

void BoundedOutboundScheduler::forget_contract(const ContractKey & key)
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  if (state->closed) {
    return;
  }
  if (!state->is_drained(key)) {
    state->retire_when_drained.insert(key);
    return;
  }
  state->retire_when_drained.erase(key);
  state->revoked.erase(key);
}

void BoundedOutboundScheduler::close()
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  if (state->closed) {
    return;
  }
  state->closed = true;
  state->control.clear();
  state->data.clear();
  state->round_robin.clear();
  state->active.clear();
  state->revoked.clear();
  state->retire_when_drained.clear();
  state->control_depth_used = 0;
  state->control_bytes_used = 0;
  state->data_queued_depth = 0;
  state->data_queued_bytes = 0;
  state->control_burst = 0;
}

uint64_t BoundedOutboundScheduler::queued_bytes() const
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  return impl_->control_bytes_used + impl_->data_queued_bytes;
}

uint64_t BoundedOutboundScheduler::total_queued_depth() const
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  return impl_->control_depth_used + impl_->data_queued_depth;
}

uint64_t BoundedOutboundScheduler::data_queued_depth() const
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  return impl_->data_queued_depth;
}

uint64_t BoundedOutboundScheduler::transient_bytes() const
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  return impl_->transient_bytes;
}

uint64_t BoundedOutboundScheduler::in_flight_bytes() const
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  return impl_->in_flight_bytes;
}

uint64_t BoundedOutboundScheduler::revoked_contract_count() const
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  return static_cast<uint64_t>(impl_->revoked.size());
}

bool BoundedOutboundScheduler::is_closed() const
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  return impl_->closed;
}

ReplayAdmission::ReplayAdmission(
  std::shared_ptr<void> owner,
  uint64_t request_id,
  ReplayDecision decision,
  std::vector<uint8_t> cached_response)
: owner_(std::move(owner)),
  request_id_(request_id),
  decision_(decision),
  cached_response_(std::move(cached_response))
{
}

ReplayDecision ReplayAdmission::decision() const noexcept
{
  return decision_;
}

uint64_t ReplayAdmission::request_id() const noexcept
{
  return request_id_;
}

const std::vector<uint8_t> & ReplayAdmission::cached_response() const noexcept
{
  return cached_response_;
}

struct RequestReplayAuthority::Impl final
{
  struct Entry
  {
    std::vector<uint8_t> request;
    uint64_t reserved_response_bytes;
    std::vector<uint8_t> response;
    bool completed{false};
    bool claimed{false};
    std::shared_ptr<void> scheduler_identity;
    std::shared_ptr<ControlReservation> reservation;
  };

  explicit Impl(const ProtocolLimits & value)
  : limits(value)
  {
  }

  mutable std::mutex mutex;
  ProtocolLimits limits;
  std::unordered_map<uint64_t, Entry> entries;
  std::list<uint64_t> completed_order;
  uint64_t high_water_mark{0};
  uint64_t outstanding_requests{0};
  uint64_t replay_bytes{0};
  bool closed{false};

  void evict(uint64_t request_id)
  {
    const auto found = entries.find(request_id);
    if (found == entries.end() || !found->second.completed) {
      throw std::logic_error("the U2R2 replay eviction target is invalid");
    }
    const auto bytes =
      static_cast<uint64_t>(found->second.request.size()) +
      static_cast<uint64_t>(found->second.response.size());
    if (replay_bytes < bytes) {
      throw std::logic_error("replay byte accounting underflow");
    }
    replay_bytes -= bytes;
    entries.erase(found);
    completed_order.remove(request_id);
  }
};

RequestReplayAuthority::RequestReplayAuthority(
  const ProtocolLimits & limits)
: impl_(std::make_shared<Impl>(limits))
{
}

RequestReplayAuthority::~RequestReplayAuthority() = default;

ReplayAdmission RequestReplayAuthority::admit(
  uint64_t request_id,
  const std::vector<uint8_t> & canonical_request,
  uint64_t maximum_response_bytes,
  BoundedOutboundScheduler & scheduler)
{
  if (request_id == 0) {
    throw ProtocolError(
            "invalid_request_id",
            "a U2R2 request ID must be nonzero",
            true);
  }
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  if (state->closed) {
    throw std::logic_error("the U2R2 replay authority is closed");
  }

  const auto retained = state->entries.find(request_id);
  if (retained != state->entries.end()) {
    if (canonical_request != retained->second.request) {
      throw ProtocolError(
              "request_id_conflict",
              "a retained U2R2 request ID has different canonical bytes",
              true);
    }
    if (!retained->second.completed) {
      throw ProtocolError(
              "request_in_flight",
              "the identical U2R2 request is still in flight",
              false);
    }
    if (retained->second.scheduler_identity.get() != scheduler.impl_.get()) {
      throw std::logic_error(
              "the retained U2R2 response belongs to another scheduler");
    }
    auto replay_reservation = scheduler.try_reserve_control(
      static_cast<uint64_t>(retained->second.response.size()));
    if (!replay_reservation) {
      CapacityExceeded("no control capacity remains for exact replay");
    }
    replay_reservation->commit(
      OutboundFrame::control(
        "replay:" + std::to_string(request_id),
        retained->second.response));
    ReplayAdmission result(
      state,
      request_id,
      ReplayDecision::replay_cached,
      retained->second.response);
    result.settled_ = true;
    return result;
  }

  if (request_id <= state->high_water_mark) {
    throw ProtocolError(
            "stale_request",
            "the U2R2 request ID is below the retained session high-water mark",
            false);
  }
  if (
    state->outstanding_requests ==
    state->limits.max_outstanding_requests())
  {
    CapacityExceeded("the outstanding U2R2 request limit is exhausted");
  }

  const auto request_bytes = static_cast<uint64_t>(canonical_request.size());
  if (
    request_bytes > state->limits.max_replay_bytes() ||
    maximum_response_bytes >
    state->limits.max_replay_bytes() - request_bytes)
  {
    CapacityExceeded(
      "the request and response reservation exceed replay bytes");
  }
  const auto requested_replay_bytes =
    request_bytes + maximum_response_bytes;

  auto response_reservation =
    scheduler.try_reserve_control(maximum_response_bytes);
  if (!response_reservation) {
    CapacityExceeded(
      "no control capacity remains for the required response");
  }

  std::vector<uint64_t> evictions;
  auto projected_count = static_cast<uint64_t>(state->entries.size());
  auto projected_bytes = state->replay_bytes;
  auto cursor = state->completed_order.begin();
  while (
    projected_count >= state->limits.max_replay_entries() ||
    projected_bytes > state->limits.max_replay_bytes() ||
    requested_replay_bytes >
    state->limits.max_replay_bytes() - projected_bytes)
  {
    if (cursor == state->completed_order.end()) {
      response_reservation->try_cancel();
      CapacityExceeded(
        "the bounded replay cache cannot admit this request");
    }
    const auto entry = state->entries.find(*cursor);
    if (entry == state->entries.end() || !entry->second.completed) {
      throw std::logic_error("the U2R2 replay order is inconsistent");
    }
    const auto entry_bytes =
      static_cast<uint64_t>(entry->second.request.size()) +
      static_cast<uint64_t>(entry->second.response.size());
    evictions.push_back(*cursor);
    --projected_count;
    projected_bytes -= entry_bytes;
    ++cursor;
  }
  for (const auto evicted : evictions) {
    state->evict(evicted);
  }

  auto reservation = std::make_shared<ControlReservation>(
    std::move(*response_reservation));
  typename Impl::Entry entry{
    canonical_request,
    maximum_response_bytes,
    {},
    false,
    false,
    scheduler.impl_,
    std::move(reservation)};
  state->entries.emplace(request_id, std::move(entry));
  state->replay_bytes += requested_replay_bytes;
  ++state->outstanding_requests;
  state->high_water_mark = request_id;
  return ReplayAdmission(
    state,
    request_id,
    ReplayDecision::begin_mutation,
    {});
}

void RequestReplayAuthority::complete(
  ReplayAdmission & admission,
  const std::vector<uint8_t> & exact_response)
{
  finish(
    admission,
    OutboundFrame::control(
      "response:" + std::to_string(admission.request_id()),
      exact_response),
    false,
    false);
}

void RequestReplayAuthority::abort(
  ReplayAdmission & admission,
  const std::vector<uint8_t> & exact_error_response)
{
  finish(
    admission,
    OutboundFrame::control(
      "error:" + std::to_string(admission.request_id()),
      exact_error_response),
    false,
    false);
}

void RequestReplayAuthority::finish(
  ReplayAdmission & admission,
  OutboundFrame exact_response,
  bool priority_fence,
  bool require_claimed,
  std::optional<ContractKey> fence_contract)
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  if (
    admission.owner_.get() != state.get() ||
    admission.decision_ != ReplayDecision::begin_mutation ||
    admission.settled_)
  {
    throw std::logic_error(
            "the U2R2 replay admission is not pending");
  }
  const auto found = state->entries.find(admission.request_id_);
  if (found == state->entries.end() || found->second.completed) {
    throw std::logic_error("the U2R2 replay entry is not pending");
  }
  auto & entry = found->second;
  if (entry.claimed != require_claimed) {
    throw std::logic_error(
            require_claimed ?
            "the U2R2 replay entry is not claimed by a contract" :
            "the U2R2 replay entry is owned by a contract transaction");
  }
  if (exact_response.byte_count() > entry.reserved_response_bytes) {
    CapacityExceeded(
      "the exact response exceeds its pre-mutation reservation");
  }
  const auto exact_bytes = exact_response.bytes();
  const auto exact_byte_count = exact_response.byte_count();
  if (priority_fence != fence_contract.has_value()) {
    throw std::logic_error(
            "a fenced U2R2 response requires exactly one contract");
  }
  const auto committed = priority_fence
    ? entry.reservation->try_commit_fenced(
      std::move(exact_response),
      *fence_contract)
    : entry.reservation->try_commit(std::move(exact_response));
  if (!committed) {
    throw std::logic_error("the U2R2 response reservation is settled");
  }
  entry.response = exact_bytes;
  entry.completed = true;
  if (entry.reserved_response_bytes < exact_byte_count) {
    throw std::logic_error("replay reservation accounting underflow");
  }
  state->replay_bytes -=
    entry.reserved_response_bytes - exact_byte_count;
  if (state->outstanding_requests == 0) {
    throw std::logic_error("outstanding request accounting underflow");
  }
  --state->outstanding_requests;
  state->completed_order.push_back(admission.request_id_);
  admission.settled_ = true;
}

void RequestReplayAuthority::cancel_pending(ReplayAdmission & admission)
{
  cancel(admission, false);
}

void RequestReplayAuthority::cancel(
  ReplayAdmission & admission,
  bool require_claimed)
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  if (admission.owner_.get() != state.get()) {
    throw std::logic_error(
            "the U2R2 replay admission belongs to another authority");
  }
  if (admission.settled_) {
    return;
  }
  if (admission.decision_ != ReplayDecision::begin_mutation) {
    throw std::logic_error("the U2R2 replay admission is not pending");
  }
  const auto found = state->entries.find(admission.request_id_);
  if (
    found == state->entries.end() ||
    found->second.completed ||
    found->second.claimed != require_claimed)
  {
    throw std::logic_error("the U2R2 replay admission is not pending");
  }
  auto & entry = found->second;
  entry.reservation->try_cancel();
  const auto reserved =
    static_cast<uint64_t>(entry.request.size()) +
    entry.reserved_response_bytes;
  if (
    state->replay_bytes < reserved ||
    state->outstanding_requests == 0)
  {
    throw std::logic_error("replay cancellation accounting underflow");
  }
  state->replay_bytes -= reserved;
  --state->outstanding_requests;
  state->entries.erase(found);
  admission.settled_ = true;
}

bool RequestReplayAuthority::try_claim_for_contract(
  ReplayAdmission & admission,
  const BoundedOutboundScheduler & scheduler)
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  if (
    admission.owner_.get() != state.get() ||
    admission.decision_ != ReplayDecision::begin_mutation ||
    admission.settled_)
  {
    return false;
  }
  const auto found = state->entries.find(admission.request_id_);
  if (
    found == state->entries.end() ||
    found->second.completed ||
    found->second.claimed ||
    found->second.scheduler_identity.get() != scheduler.impl_.get())
  {
    return false;
  }
  found->second.claimed = true;
  return true;
}

void RequestReplayAuthority::release_contract_claim(
  ReplayAdmission & admission,
  const BoundedOutboundScheduler & scheduler)
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  if (
    admission.owner_.get() != state.get() ||
    admission.decision_ != ReplayDecision::begin_mutation ||
    admission.settled_)
  {
    throw std::logic_error(
            "the U2R2 replay admission has no matching contract claim");
  }
  const auto found = state->entries.find(admission.request_id_);
  if (
    found == state->entries.end() ||
    found->second.completed ||
    !found->second.claimed ||
    found->second.scheduler_identity.get() != scheduler.impl_.get())
  {
    throw std::logic_error(
            "the U2R2 replay admission has no matching contract claim");
  }
  found->second.claimed = false;
}

bool RequestReplayAuthority::is_cached_for(
  const ReplayAdmission & admission,
  const BoundedOutboundScheduler & scheduler) const
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  if (
    admission.owner_.get() != state.get() ||
    admission.decision_ != ReplayDecision::replay_cached ||
    !admission.settled_)
  {
    return false;
  }
  const auto found = state->entries.find(admission.request_id_);
  return
    found != state->entries.end() &&
    found->second.completed &&
    found->second.scheduler_identity.get() == scheduler.impl_.get();
}

uint64_t RequestReplayAuthority::high_water_mark() const
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  return impl_->high_water_mark;
}

uint64_t RequestReplayAuthority::outstanding_requests() const
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  return impl_->outstanding_requests;
}

uint64_t RequestReplayAuthority::retained_entries() const
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  return static_cast<uint64_t>(impl_->entries.size());
}

uint64_t RequestReplayAuthority::replay_bytes() const
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  return impl_->replay_bytes;
}

bool RequestReplayAuthority::is_closed() const
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  return impl_->closed;
}

void RequestReplayAuthority::close()
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  if (state->closed) {
    return;
  }
  for (auto & [request_id, entry] : state->entries) {
    (void)request_id;
    if (!entry.completed) {
      entry.reservation->try_cancel();
    }
  }
  state->entries.clear();
  state->completed_order.clear();
  state->outstanding_requests = 0;
  state->replay_bytes = 0;
  state->closed = true;
}

ContractSequence::ContractSequence(uint64_t starting_sequence) noexcept
: last_accepted_(starting_sequence)
{
}

void ContractSequence::admit(uint64_t sequence)
{
  if (faulted_) {
    throw ProtocolError(
            "contract_sequence_fault",
            "the U2R2 contract sequence is already faulted",
            false);
  }
  if (last_accepted_ == std::numeric_limits<uint64_t>::max()) {
    faulted_ = true;
    throw ProtocolError(
            "contract_sequence_exhausted",
            "the U2R2 contract sequence exhausted before wrap",
            false);
  }
  if (sequence != last_accepted_ + 1) {
    faulted_ = true;
    throw ProtocolError(
            "contract_sequence_fault",
            "the U2R2 contract sequence is not strictly monotonic",
            false);
  }
  last_accepted_ = sequence;
}

uint64_t ContractSequence::last_accepted() const noexcept
{
  return last_accepted_;
}

bool ContractSequence::is_faulted() const noexcept
{
  return faulted_;
}

RegistrationAdmission::RegistrationAdmission(
  std::shared_ptr<void> owner,
  ContractIdentity identity,
  std::shared_ptr<void> scheduler,
  std::shared_ptr<void> replay,
  uint64_t response_request_id)
: owner_(std::move(owner)),
  identity_(std::move(identity)),
  scheduler_(std::move(scheduler)),
  replay_(std::move(replay)),
  response_request_id_(response_request_id)
{
}

bool RegistrationAdmission::replayed() const noexcept
{
  return replayed_;
}

RemovalAdmission::RemovalAdmission(
  std::shared_ptr<void> owner,
  ContractIdentity identity,
  std::shared_ptr<void> scheduler,
  std::shared_ptr<void> replay,
  uint64_t response_request_id)
: owner_(std::move(owner)),
  identity_(std::move(identity)),
  scheduler_(std::move(scheduler)),
  replay_(std::move(replay)),
  response_request_id_(response_request_id)
{
}

bool RemovalAdmission::replayed() const noexcept
{
  return replayed_;
}

struct ContractAuthority::Impl final
{
  enum class State
  {
    registering = 1,
    ready = 2,
    removing = 3,
  };

  struct Entry
  {
    ContractIdentity identity;
    State state;
    ContractSequence sequence;
  };

  Impl(
    const ProtocolLimits & value,
    ContractAuthority::SemanticErrorFrameFactory error_frame_factory)
  : limits(value),
    semantic_error_frame_factory(std::move(error_frame_factory))
  {
  }

  mutable std::mutex mutex;
  ProtocolLimits limits;
  ContractAuthority::SemanticErrorFrameFactory semantic_error_frame_factory;
  std::unordered_map<ContractKey, Entry, ContractKeyHash> contracts;
  std::unordered_map<ContractKey, ContractIdentity, ContractKeyHash> tombstones;
  std::deque<ContractKey> tombstone_order;
  std::shared_ptr<void> bound_scheduler;
  std::shared_ptr<void> bound_replay;
  bool closed{false};

  void ensure_authority_pair(
    const std::shared_ptr<void> & scheduler,
    const std::shared_ptr<void> & replay) const
  {
    if (!bound_scheduler && !bound_replay) {
      return;
    }
    if (
      bound_scheduler.get() != scheduler.get() ||
      bound_replay.get() != replay.get())
    {
      throw std::logic_error(
              "the U2R2 contract authority belongs to another scheduler and replay authority");
    }
  }

  void bind_authority_pair(
    const std::shared_ptr<void> & scheduler,
    const std::shared_ptr<void> & replay)
  {
    ensure_authority_pair(scheduler, replay);
    if (bound_scheduler) {
      return;
    }
    bound_scheduler = scheduler;
    bound_replay = replay;
  }

  void add_tombstone(
    const ContractIdentity & identity,
    BoundedOutboundScheduler & scheduler)
  {
    if (tombstones.emplace(identity.key, identity).second) {
      tombstone_order.push_back(identity.key);
    }
    while (
      static_cast<uint64_t>(tombstones.size()) >
      limits.max_tombstones())
    {
      const auto evicted = tombstone_order.front();
      tombstones.erase(evicted);
      tombstone_order.pop_front();
      scheduler.forget_contract(evicted);
    }
  }

  void ensure_open() const
  {
    if (closed) {
      throw std::logic_error("the U2R2 contract authority is closed");
    }
  }
};

ContractAuthority::ContractAuthority(
  const ProtocolLimits & limits,
  SemanticErrorFrameFactory semantic_error_frame_factory)
: impl_(std::make_shared<Impl>(
    limits,
    std::move(semantic_error_frame_factory)))
{
  if (!impl_->semantic_error_frame_factory) {
    throw std::invalid_argument(
            "the semantic error frame factory is required");
  }
}

ContractAuthority::~ContractAuthority() = default;

RegistrationAdmission ContractAuthority::begin_registration(
  const ContractIdentity & identity,
  BoundedOutboundScheduler & scheduler,
  RequestReplayAuthority & replay,
  ReplayAdmission & response)
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  state->ensure_open();
  state->ensure_authority_pair(scheduler.impl_, replay.impl_);
  if (response.decision() == ReplayDecision::replay_cached) {
    if (!replay.is_cached_for(response, scheduler)) {
      throw std::logic_error(
              "the replayed registration response belongs elsewhere");
    }
    state->bind_authority_pair(scheduler.impl_, replay.impl_);
    RegistrationAdmission result(
      state,
      identity,
      scheduler.impl_,
      replay.impl_,
      response.request_id());
    result.replayed_ = true;
    result.settled_ = true;
    return result;
  }
  if (!replay.try_claim_for_contract(response, scheduler)) {
    throw std::logic_error(
            "registration requires the pending command response transaction");
  }
  state->bind_authority_pair(scheduler.impl_, replay.impl_);
  if (identity.direction != ContractDirection::subscribe) {
    const ProtocolError error(
      "invalid_contract",
      "a register_subscription command requires subscribe direction",
      false);
    auto frame = state->semantic_error_frame_factory(
      Operation::SubscriptionReady,
      response.request_id(),
      error);
    if (!frame.is_control()) {
      throw std::logic_error(
              "the semantic error factory must return a control frame");
    }
    replay.finish(response, std::move(frame), false, true);
    throw error;
  }
  const auto active = state->contracts.find(identity.key);
  const auto removed = state->tombstones.find(identity.key);
  if (active != state->contracts.end() || removed != state->tombstones.end()) {
    const ProtocolError error(
      "invalid_contract",
      "the U2R2 contract ID and generation are already bound",
      false);
    auto frame = state->semantic_error_frame_factory(
      Operation::SubscriptionReady,
      response.request_id(),
      error);
    if (!frame.is_control()) {
      throw std::logic_error(
              "the semantic error factory must return a control frame");
    }
    replay.finish(response, std::move(frame), false, true);
    throw error;
  }
  if (
    static_cast<uint64_t>(state->contracts.size()) ==
    state->limits.max_contracts())
  {
    const ProtocolError error(
      "capacity_exceeded",
      "the U2R2 contract limit is exhausted",
      false);
    auto frame = state->semantic_error_frame_factory(
      Operation::SubscriptionReady,
      response.request_id(),
      error);
    if (!frame.is_control()) {
      throw std::logic_error(
              "the semantic error factory must return a control frame");
    }
    replay.finish(response, std::move(frame), false, true);
    throw error;
  }
  state->contracts.emplace(
    identity.key,
    typename Impl::Entry{
      identity,
      Impl::State::registering,
      ContractSequence()});
  scheduler.activate_contract(identity.key);
  return RegistrationAdmission(
    state,
    identity,
    scheduler.impl_,
    replay.impl_,
    response.request_id());
}

void ContractAuthority::commit_ready(
  RegistrationAdmission & admission,
  RequestReplayAuthority & replay,
  ReplayAdmission & response,
  OutboundFrame exact_ready_frame)
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  if (admission.replayed_) {
    if (
      admission.owner_.get() != state.get() ||
      admission.replay_.get() != replay.impl_.get() ||
      admission.response_request_id_ != response.request_id() ||
      response.decision() != ReplayDecision::replay_cached)
    {
      throw std::logic_error(
              "the U2R2 replayed registration belongs elsewhere");
    }
    return;
  }
  if (
    admission.owner_.get() != state.get() ||
    admission.replay_.get() != replay.impl_.get() ||
    admission.response_request_id_ != response.request_id() ||
    admission.settled_ ||
    !admission.identity_.has_value())
  {
    throw std::logic_error(
            "the U2R2 registration admission is not pending");
  }
  const auto found = state->contracts.find(admission.identity_->key);
  if (
    found == state->contracts.end() ||
    found->second.state != Impl::State::registering ||
    found->second.identity != *admission.identity_)
  {
    throw std::logic_error(
            "the U2R2 registration admission is not pending");
  }
  replay.finish(
    response,
    std::move(exact_ready_frame),
    true,
    true,
    admission.identity_->key);
  found->second.state = Impl::State::ready;
  admission.settled_ = true;
}

void ContractAuthority::cancel_registration(
  RegistrationAdmission & admission,
  BoundedOutboundScheduler & scheduler,
  RequestReplayAuthority & replay,
  ReplayAdmission & response)
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  if (
    admission.owner_.get() != state.get() ||
    admission.scheduler_.get() != scheduler.impl_.get())
  {
    throw std::logic_error(
            "the U2R2 registration admission belongs elsewhere");
  }
  if (
    admission.replay_.get() != replay.impl_.get() ||
    admission.response_request_id_ != response.request_id())
  {
    throw std::logic_error(
            "the U2R2 registration response transaction belongs elsewhere");
  }
  if (admission.settled_) {
    return;
  }
  if (!admission.identity_.has_value()) {
    throw std::logic_error(
            "the U2R2 registration admission is not pending");
  }
  const auto found = state->contracts.find(admission.identity_->key);
  if (
    found == state->contracts.end() ||
    found->second.state != Impl::State::registering)
  {
    throw std::logic_error(
            "the U2R2 registration admission is not pending");
  }
  replay.cancel(response, true);
  state->contracts.erase(found);
  scheduler.retire_contract(admission.identity_->key);
  admission.settled_ = true;
}

void ContractAuthority::abort_registration(
  RegistrationAdmission & admission,
  BoundedOutboundScheduler & scheduler,
  RequestReplayAuthority & replay,
  ReplayAdmission & response,
  const ProtocolError & error)
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  if (
    admission.owner_.get() != state.get() ||
    admission.scheduler_.get() != scheduler.impl_.get())
  {
    throw std::logic_error(
            "the U2R2 registration admission belongs elsewhere");
  }
  if (
    admission.replay_.get() != replay.impl_.get() ||
    admission.response_request_id_ != response.request_id())
  {
    throw std::logic_error(
            "the U2R2 registration response transaction belongs elsewhere");
  }
  if (admission.settled_) {
    return;
  }
  if (!admission.identity_.has_value()) {
    throw std::logic_error(
            "the U2R2 registration admission is not pending");
  }
  const auto found = state->contracts.find(admission.identity_->key);
  if (
    found == state->contracts.end() ||
    found->second.state != Impl::State::registering)
  {
    throw std::logic_error(
            "the U2R2 registration admission is not pending");
  }
  auto frame = state->semantic_error_frame_factory(
    Operation::SubscriptionReady,
    response.request_id(),
    error);
  if (!frame.is_control()) {
    throw std::logic_error(
            "the semantic error factory must return a control frame");
  }
  replay.finish(response, std::move(frame), false, true);
  state->contracts.erase(found);
  scheduler.retire_contract(admission.identity_->key);
  admission.settled_ = true;
}

MessageAdmission ContractAuthority::admit_message(
  const ContractIdentity & identity,
  uint64_t sequence)
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  state->ensure_open();
  const auto tombstone = state->tombstones.find(identity.key);
  if (tombstone != state->tombstones.end()) {
    if (tombstone->second != identity) {
      throw ProtocolError(
              "contract_identity_mismatch",
              "the U2R2 message identity does not match its frozen contract",
              true);
    }
    return MessageAdmission::late_tombstone;
  }
  const auto found = state->contracts.find(identity.key);
  if (found == state->contracts.end()) {
    throw ProtocolError(
            "unknown_contract",
            "the U2R2 message references an unknown contract generation",
            true);
  }
  if (found->second.identity != identity) {
    throw ProtocolError(
            "contract_identity_mismatch",
            "the U2R2 message identity does not match its frozen contract",
            true);
  }
  if (found->second.state == Impl::State::registering) {
    throw ProtocolError(
            "contract_not_ready",
            "the U2R2 subscription_ready response is not committed",
            true);
  }
  if (found->second.state == Impl::State::removing) {
    return MessageAdmission::late_tombstone;
  }
  found->second.sequence.admit(sequence);
  return MessageAdmission::accepted;
}

RemovalAdmission ContractAuthority::begin_unregister(
  const ContractIdentity & identity,
  BoundedOutboundScheduler & scheduler,
  RequestReplayAuthority & replay,
  ReplayAdmission & response)
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  state->ensure_open();
  state->ensure_authority_pair(scheduler.impl_, replay.impl_);
  if (response.decision() == ReplayDecision::replay_cached) {
    if (!replay.is_cached_for(response, scheduler)) {
      throw std::logic_error(
              "the replayed unregister response belongs elsewhere");
    }
    state->bind_authority_pair(scheduler.impl_, replay.impl_);
    RemovalAdmission result(
      state,
      identity,
      scheduler.impl_,
      replay.impl_,
      response.request_id());
    result.replayed_ = true;
    result.settled_ = true;
    return result;
  }
  if (!replay.try_claim_for_contract(response, scheduler)) {
    throw std::logic_error(
            "unregister requires the pending command response transaction");
  }
  state->bind_authority_pair(scheduler.impl_, replay.impl_);
  const auto found = state->contracts.find(identity.key);
  if (
    found == state->contracts.end() ||
    found->second.state != Impl::State::ready)
  {
    const ProtocolError error(
      "unknown_contract",
      "the U2R2 unregister request references no ready contract",
      true);
    auto frame = state->semantic_error_frame_factory(
      Operation::SubscriptionRemoved,
      response.request_id(),
      error);
    if (!frame.is_control()) {
      throw std::logic_error(
              "the semantic error factory must return a control frame");
    }
    replay.finish(response, std::move(frame), false, true);
    throw error;
  }
  if (found->second.identity != identity) {
    const ProtocolError error(
      "invalid_contract",
      "the U2R2 unregister identity conflicts with the registered contract",
      false);
    auto frame = state->semantic_error_frame_factory(
      Operation::SubscriptionRemoved,
      response.request_id(),
      error);
    if (!frame.is_control()) {
      throw std::logic_error(
              "the semantic error factory must return a control frame");
    }
    replay.finish(response, std::move(frame), false, true);
    throw error;
  }
  try {
    scheduler.revoke_contract(identity.key);
  } catch (...) {
    replay.release_contract_claim(response, scheduler);
    throw;
  }
  found->second.state = Impl::State::removing;
  return RemovalAdmission(
    state,
    identity,
    scheduler.impl_,
    replay.impl_,
    response.request_id());
}

bool ContractAuthority::try_commit_removed(
  RemovalAdmission & admission,
  BoundedOutboundScheduler & scheduler,
  RequestReplayAuthority & replay,
  ReplayAdmission & response,
  OutboundFrame exact_removed_frame)
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  if (admission.replayed_) {
    if (
      admission.owner_.get() != state.get() ||
      admission.scheduler_.get() != scheduler.impl_.get() ||
      admission.replay_.get() != replay.impl_.get() ||
      admission.response_request_id_ != response.request_id() ||
      response.decision() != ReplayDecision::replay_cached)
    {
      throw std::logic_error(
              "the U2R2 replayed removal belongs elsewhere");
    }
    return true;
  }
  if (
    admission.owner_.get() != state.get() ||
    admission.scheduler_.get() != scheduler.impl_.get() ||
    admission.replay_.get() != replay.impl_.get() ||
    admission.response_request_id_ != response.request_id() ||
    admission.settled_ ||
    !admission.identity_.has_value())
  {
    throw std::logic_error(
            "the U2R2 removal admission is not pending");
  }
  const auto found = state->contracts.find(admission.identity_->key);
  if (
    found == state->contracts.end() ||
    found->second.state != Impl::State::removing ||
    found->second.identity != *admission.identity_)
  {
    throw std::logic_error(
            "the U2R2 removal admission is not pending");
  }
  if (!scheduler.is_contract_revoked_and_drained(admission.identity_->key)) {
    return false;
  }
  replay.finish(
    response,
    std::move(exact_removed_frame),
    true,
    true,
    admission.identity_->key);
  const auto identity = found->second.identity;
  state->contracts.erase(found);
  state->add_tombstone(identity, scheduler);
  admission.settled_ = true;
  return true;
}

void ContractAuthority::cancel_removal(
  RemovalAdmission & admission,
  BoundedOutboundScheduler & scheduler,
  RequestReplayAuthority & replay,
  ReplayAdmission & response)
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  if (
    admission.owner_.get() != state.get() ||
    admission.scheduler_.get() != scheduler.impl_.get())
  {
    throw std::logic_error(
            "the U2R2 removal admission belongs elsewhere");
  }
  if (
    admission.replay_.get() != replay.impl_.get() ||
    admission.response_request_id_ != response.request_id())
  {
    throw std::logic_error(
            "the U2R2 removal response transaction belongs elsewhere");
  }
  if (admission.settled_) {
    return;
  }
  if (!admission.identity_.has_value()) {
    throw std::logic_error(
            "the U2R2 removal admission is not pending");
  }
  const auto found = state->contracts.find(admission.identity_->key);
  if (
    found == state->contracts.end() ||
    found->second.state != Impl::State::removing)
  {
    throw std::logic_error(
            "the U2R2 removal admission is not pending");
  }
  replay.cancel(response, true);
  state->contracts.erase(found);
  scheduler.retire_contract(admission.identity_->key);
  admission.settled_ = true;
}

void ContractAuthority::abort_removal(
  RemovalAdmission & admission,
  BoundedOutboundScheduler & scheduler,
  RequestReplayAuthority & replay,
  ReplayAdmission & response,
  const ProtocolError & error)
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  if (
    admission.owner_.get() != state.get() ||
    admission.scheduler_.get() != scheduler.impl_.get())
  {
    throw std::logic_error(
            "the U2R2 removal admission belongs elsewhere");
  }
  if (
    admission.replay_.get() != replay.impl_.get() ||
    admission.response_request_id_ != response.request_id())
  {
    throw std::logic_error(
            "the U2R2 removal response transaction belongs elsewhere");
  }
  if (admission.settled_) {
    return;
  }
  if (!admission.identity_.has_value()) {
    throw std::logic_error(
            "the U2R2 removal admission is not pending");
  }
  const auto found = state->contracts.find(admission.identity_->key);
  if (
    found == state->contracts.end() ||
    found->second.state != Impl::State::removing)
  {
    throw std::logic_error(
            "the U2R2 removal admission is not pending");
  }
  auto frame = state->semantic_error_frame_factory(
    Operation::SubscriptionRemoved,
    response.request_id(),
    error);
  if (!frame.is_control()) {
    throw std::logic_error(
            "the semantic error factory must return a control frame");
  }
  replay.finish(response, std::move(frame), false, true);
  state->contracts.erase(found);
  scheduler.retire_contract(admission.identity_->key);
  admission.settled_ = true;
}

void ContractAuthority::close(
  BoundedOutboundScheduler & scheduler,
  RequestReplayAuthority & replay)
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  state->ensure_authority_pair(scheduler.impl_, replay.impl_);
  state->bind_authority_pair(scheduler.impl_, replay.impl_);
  if (state->closed) {
    return;
  }
  replay.close();
  scheduler.close();
  state->contracts.clear();
  state->tombstones.clear();
  state->tombstone_order.clear();
  state->closed = true;
}

uint64_t ContractAuthority::contract_count() const
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  return static_cast<uint64_t>(impl_->contracts.size());
}

uint64_t ContractAuthority::tombstone_count() const
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  return static_cast<uint64_t>(impl_->tombstones.size());
}

bool ContractAuthority::is_closed() const
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  return impl_->closed;
}

void parse_contract_fields(
  const nlohmann::json & header,
  Operation operation,
  std::string & topic,
  std::string & schema_name,
  std::optional<Qos> & qos)
{
  topic.clear();
  schema_name.clear();
  qos.reset();
  const auto has_contract_shape =
    operation == Operation::PreparePublisher ||
    operation == Operation::Publish ||
    operation == Operation::RegisterSubscription ||
    operation == Operation::Message;
  if (!has_contract_shape) {
    return;
  }

  topic = RequiredContractString(header, "topic");
  schema_name = RequiredContractString(header, "schemaName");
  ValidateTopic(topic);
  ValidateSchemaName(schema_name);
  if (operation == Operation::Message) {
    return;
  }

  if (RequiredContractString(header, "encoding") != "cdr") {
    InvalidContract("a U2R2 ROS contract requires cdr encoding");
  }
  const auto found = header.find("qos");
  if (
    found == header.end() ||
    !found->is_object() ||
    found->size() != 5 ||
    !found->contains("profile") ||
    !found->contains("reliability") ||
    !found->contains("durability") ||
    !found->contains("history") ||
    !found->contains("depth"))
  {
    InvalidContract("U2R2 qos must be an exact five-axis object");
  }
  static const std::unordered_set<std::string> expected_fields{
    "profile", "reliability", "durability", "history", "depth"};
  for (const auto & [name, unused] : found->items()) {
    (void)unused;
    if (expected_fields.find(name) == expected_fields.end()) {
      InvalidContract("U2R2 qos must be an exact five-axis object");
    }
  }

  Qos parsed{
    RequiredQosString(
      *found, "profile", {"default", "sensor_data", "system_default"}),
    RequiredQosString(
      *found, "reliability",
      {"reliable", "best_effort", "system_default"}),
    RequiredQosString(
      *found, "durability",
      {"volatile", "transient_local", "system_default"}),
    RequiredQosString(
      *found, "history", {"keep_last", "keep_all", "system_default"}),
    0};
  const auto depth = found->find("depth");
  if (
    depth == found->end() ||
    !depth->is_number_unsigned() ||
    depth->get<uint64_t>() > std::numeric_limits<uint32_t>::max())
  {
    InvalidContract(
      "U2R2 qos depth must be an unsigned 32-bit integer");
  }
  parsed.depth = depth->get<uint32_t>();
  if (
    (parsed.history == "keep_last" && parsed.depth == 0) ||
    (parsed.history != "keep_last" && parsed.depth != 0))
  {
    InvalidContract(
      "U2R2 qos depth is positive only for keep_last history");
  }
  qos = std::move(parsed);
}

PureSessionLifecycle::PureSessionLifecycle(const ProtocolLimits & limits)
: limits_(limits)
{
}

uint64_t PureSessionLifecycle::limit_for(TimeoutKind kind) const
{
  if (!limits_) {
    throw std::logic_error("this lifecycle has no limit snapshot");
  }
  switch (kind) {
    case TimeoutKind::handshake:
      return limits_->handshake_timeout_ms();
    case TimeoutKind::partial_frame:
      return limits_->partial_frame_timeout_ms();
    case TimeoutKind::read:
      return limits_->read_timeout_ms();
    case TimeoutKind::write:
      return limits_->write_timeout_ms();
    case TimeoutKind::join:
      return limits_->join_timeout_ms();
    case TimeoutKind::shutdown:
      return limits_->shutdown_timeout_ms();
    default:
      throw std::invalid_argument("unknown U2R2 timeout kind");
  }
}

bool PureSessionLifecycle::has_timed_out(
  TimeoutKind kind,
  uint64_t elapsed_ms) const
{
  return elapsed_ms >= limit_for(kind);
}

void PureSessionLifecycle::timeout(
  TimeoutKind kind,
  uint64_t elapsed_ms)
{
  if (!has_timed_out(kind, elapsed_ms)) {
    return;
  }
  state_ = PureSessionState::closed;
  throw ProtocolError(
          "timeout",
          "the U2R2 session exceeded its timeout",
          true);
}

void PureSessionLifecycle::peer_closed()
{
  state_ = PureSessionState::closed;
  throw ProtocolError(
          "peer_closed",
          "the U2R2 peer closed the session",
          true);
}

PureSessionState PureSessionLifecycle::state() const noexcept
{
  return state_;
}
}  // namespace unity2foxglove::ros2_bridge::u2r2
