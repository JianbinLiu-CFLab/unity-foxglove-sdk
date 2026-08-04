// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/ros2_bridge
// Purpose: Frozen U2R2 first-frame, identity, and replay session integration.

#include "unity2foxglove_ros2_bridge/bridge_session.hpp"
#include "unity2foxglove_ros2_bridge/bridge_writer.hpp"

#include <algorithm>
#include <chrono>
#include <mutex>
#include <stdexcept>
#include <thread>
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
    expected.sequence != actual.sequence ||
    expected.contract_id != actual.contract_id ||
    expected.session_id != actual.session_id ||
    expected.connection_generation != actual.connection_generation ||
    expected.capabilities != actual.capabilities ||
    expected.log_time_ns != actual.log_time_ns ||
    expected.receive_time_ns != actual.receive_time_ns ||
    expected.encoding != actual.encoding ||
    expected.representation != actual.representation ||
    expected.topic != actual.topic ||
    expected.schema_name != actual.schema_name ||
    expected.qos != actual.qos)
  {
    throw u2r2::ProtocolError(
            "invalid_frame",
            "the parsed U2R2 request does not match its canonical frame",
            true);
  }
}

bool HasCapability(
  const std::vector<u2r2::Capability> & capabilities,
  u2r2::Capability capability)
{
  return std::find(
    capabilities.begin(),
    capabilities.end(),
    capability) != capabilities.end();
}

nlohmann::json NegotiatedCapabilityNames(
  const std::vector<u2r2::Capability> & capabilities)
{
  auto result = nlohmann::json::array();
  if (HasCapability(capabilities, u2r2::Capability::Publish)) {
    result.push_back("publish");
  }
  if (HasCapability(capabilities, u2r2::Capability::Subscribe)) {
    result.push_back("subscribe");
  }
  return result;
}

const char * ResponseOperationName(u2r2::Operation operation)
{
  switch (operation) {
    case u2r2::Operation::SubscriptionReady:
      return "subscription_ready";
    case u2r2::Operation::SubscriptionRemoved:
      return "subscription_removed";
    default:
      throw std::invalid_argument(
              "the Bridge response operation is unsupported");
  }
}

std::string BoundedSessionError(const std::string & message)
{
  constexpr size_t kMaximumSessionErrorBytes = 512;
  return
    message.size() <= kMaximumSessionErrorBytes
    ? message
    : message.substr(0, kMaximumSessionErrorBytes);
}
}  // namespace

struct BridgeSessionProtocol::Impl final
{
  struct SubscriptionRecord final
  {
    u2r2::ContractIdentity identity;
    std::shared_ptr<BridgeSubscriptionGate> gate;
    std::shared_ptr<void> entity;
  };

  explicit Impl(const u2r2::ProtocolLimits & value)
  : limits(value),
    writer(value),
    replay(value),
    contracts(
      value,
      [this](
        u2r2::Operation operation,
        uint64_t request_id,
        const u2r2::ProtocolError & error) {
        return semantic_error_frame(operation, request_id, error);
      })
  {
    auto lease = writer.try_attach_writer();
    if (!lease) {
      throw std::logic_error("a Bridge session requires one writer lease");
    }
    writer_lease = std::move(*lease);
  }

  ~Impl()
  {
    try {
      close();
    } catch (...) {
    }
  }

  const u2r2::ProtocolLimits limits;
  u2r2::SessionStateMachine state;
  BridgeWriterCore writer;
  BridgeWriterLease writer_lease;
  u2r2::RequestReplayAuthority replay;
  u2r2::ContractAuthority contracts;
  std::optional<u2r2::Message> hello;
  std::string session_id;
  uint64_t connection_generation{0};
  std::unique_ptr<BridgeOutboundQueue> outbound;
  std::mutex subscriptions_mutex;
  std::unordered_map<uint64_t, SubscriptionRecord> subscriptions;
  std::unordered_map<std::string, std::string> prepared_publishers;
  bool draining{false};
  bool closed{false};

  std::vector<uint8_t> response_bytes(
    u2r2::Operation operation,
    uint64_t request_id,
    const std::string & status,
    uint64_t contract_id,
    const u2r2::ProtocolError * error = nullptr) const
  {
    nlohmann::json header{
      {"op", ResponseOperationName(operation)},
      {"protocolVersion", u2r2::kProtocolVersion},
      {"requestId", request_id},
      {"status", status},
      {"sessionId", session_id},
      {"connectionGeneration", connection_generation},
    };
    if (error == nullptr) {
      header["contractId"] = contract_id;
    } else {
      header["errorCode"] = error->code();
      header["message"] = error->what();
      header["terminal"] = error->terminal();
    }
    return u2r2::encode_frame(header, {}, limits);
  }

  u2r2::OutboundFrame semantic_error_frame(
    u2r2::Operation operation,
    uint64_t request_id,
    const u2r2::ProtocolError & error) const
  {
    return u2r2::OutboundFrame::control(
      std::string("error:") + std::to_string(request_id),
      response_bytes(operation, request_id, "error", 0, &error));
  }

  void begin_drain()
  {
    if (draining || closed) {
      return;
    }
    draining = true;
    if (outbound) {
      outbound->close();
    }
    writer.begin_drain();
    std::vector<SubscriptionRecord> retained;
    {
      std::lock_guard<std::mutex> lock(subscriptions_mutex);
      retained.reserve(subscriptions.size());
      for (auto & [unused, record] : subscriptions) {
        (void)unused;
        retained.push_back(std::move(record));
      }
      subscriptions.clear();
    }
    for (auto & record : retained) {
      if (outbound) {
        outbound->revoke(record.gate);
      }
      record.entity.reset();
    }
  }

  void close()
  {
    if (closed) {
      return;
    }
    begin_drain();
    closed = true;
    contracts.close(writer.scheduler(), replay);
    writer.close();
  }

  void require_capability(u2r2::Operation operation) const
  {
    std::optional<u2r2::Capability> required;
    switch (operation) {
      case u2r2::Operation::PreparePublisher:
      case u2r2::Operation::Publish:
        required = u2r2::Capability::Publish;
        break;
      case u2r2::Operation::RegisterSubscription:
      case u2r2::Operation::UnregisterSubscription:
        required = u2r2::Capability::Subscribe;
        break;
      default:
        return;
    }
    if (!hello || !HasCapability(hello->capabilities, *required)) {
      throw u2r2::ProtocolError(
              "missing_capability",
              "the U2R2 operation requires an unnegotiated capability",
              true);
    }
  }

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
      message.operation != u2r2::Operation::Publish &&
      message.operation != u2r2::Operation::RegisterSubscription &&
      message.operation != u2r2::Operation::UnregisterSubscription))
    {
      throw u2r2::ProtocolError(
              "invalid_frame",
              "the U2R2 operation is not valid in an active session",
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
    require_capability(message.operation);
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
    impl_->state.accept_v2(message, {});
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
  impl_->outbound = std::make_unique<BridgeOutboundQueue>(
    impl_->limits,
    impl_->writer,
    impl_->contracts,
    impl_->session_id,
    impl_->connection_generation);
  const auto response = u2r2::encode_frame(
    {
      {"op", "hello_ack"},
      {"protocolVersion", 2},
      {"requestId", impl_->hello->request_id},
      {"status", "ok"},
      {"sessionId", impl_->session_id},
      {"connectionGeneration", impl_->connection_generation},
      {
        "capabilities",
        NegotiatedCapabilityNames(impl_->hello->capabilities),
      },
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
    impl_->writer.scheduler());
  if (admission.decision() == u2r2::ReplayDecision::replay_cached) {
    impl_->writer.notify();
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
    impl_->writer.notify();
  } catch (...) {
    try {
      impl_->replay.cancel_pending(admission);
    } catch (...) {
    }
    throw;
  }
  return admission.decision();
}

BridgeSubscriptionCommand BridgeSessionProtocol::register_subscription(
  const std::vector<uint8_t> & request_wire,
  const u2r2::Message & request,
  uint64_t maximum_response_bytes,
  const BridgeSubscriptionFactory & factory)
{
  if (!factory) {
    throw std::invalid_argument(
            "a Bridge subscription factory is required");
  }
  const auto frame = u2r2::decode_frame(request_wire, impl_->limits);
  const auto parsed = impl_->parse_active_v2_request(frame);
  ValidateSameRequest(request, parsed);
  if (
    parsed.operation != u2r2::Operation::RegisterSubscription ||
    !parsed.qos ||
    !impl_->outbound)
  {
    throw std::invalid_argument(
            "register_subscription requires an active parsed contract");
  }
  const u2r2::ContractIdentity identity(
    u2r2::ContractKey(
      parsed.contract_id,
      impl_->connection_generation),
    u2r2::ContractDirection::subscribe,
    parsed.topic,
    parsed.schema_name,
    *parsed.qos);
  const auto canonical =
    u2r2::encode_frame(frame.header, frame.payload, impl_->limits);
  auto response = impl_->replay.admit(
    parsed.request_id,
    canonical,
    maximum_response_bytes,
    impl_->writer.scheduler());
  if (response.decision() == u2r2::ReplayDecision::replay_cached) {
    impl_->writer.notify();
    return BridgeSubscriptionCommand::replayed;
  }

  u2r2::RegistrationAdmission registration;
  try {
    registration = impl_->contracts.begin_registration(
      identity,
      impl_->writer.scheduler(),
      impl_->replay,
      response);
  } catch (const u2r2::ProtocolError &) {
    impl_->writer.notify();
    return BridgeSubscriptionCommand::rejected;
  }

  std::shared_ptr<BridgeSubscriptionGate> gate;
  std::shared_ptr<void> entity;
  try {
    gate = impl_->outbound->create_gate(identity);
    entity = factory(identity, impl_->outbound->callback(gate));
    if (!entity) {
      throw std::runtime_error(
              "the ROS 2 subscription factory returned no entity");
    }
    {
      std::lock_guard<std::mutex> lock(impl_->subscriptions_mutex);
      const auto inserted = impl_->subscriptions.emplace(
        parsed.contract_id,
        Impl::SubscriptionRecord{identity, gate, entity});
      if (!inserted.second) {
        throw std::logic_error(
                "the Bridge subscription record already exists");
      }
    }
    auto ready = u2r2::OutboundFrame::control(
      "subscription_ready:" + std::to_string(parsed.request_id),
      impl_->response_bytes(
        u2r2::Operation::SubscriptionReady,
        parsed.request_id,
        "ok",
        parsed.contract_id));
    impl_->contracts.commit_ready(
      registration,
      impl_->replay,
      response,
      std::move(ready));
    impl_->outbound->activate(gate);
    impl_->writer.notify();
    return BridgeSubscriptionCommand::applied;
  } catch (const std::exception & error) {
    if (gate) {
      impl_->outbound->revoke(gate);
    }
    {
      std::lock_guard<std::mutex> lock(impl_->subscriptions_mutex);
      impl_->subscriptions.erase(parsed.contract_id);
    }
    entity.reset();
    const u2r2::ProtocolError semantic(
      "invalid_contract",
      BoundedSessionError(
        std::string("the ROS 2 subscription could not be created: ") +
        error.what()),
      false);
    impl_->contracts.abort_registration(
      registration,
      impl_->writer.scheduler(),
      impl_->replay,
      response,
      semantic);
    impl_->writer.notify();
    return BridgeSubscriptionCommand::rejected;
  } catch (...) {
    if (gate) {
      impl_->outbound->revoke(gate);
    }
    {
      std::lock_guard<std::mutex> lock(impl_->subscriptions_mutex);
      impl_->subscriptions.erase(parsed.contract_id);
    }
    entity.reset();
    const u2r2::ProtocolError semantic(
      "invalid_contract",
      "the ROS 2 subscription could not be created",
      false);
    impl_->contracts.abort_registration(
      registration,
      impl_->writer.scheduler(),
      impl_->replay,
      response,
      semantic);
    impl_->writer.notify();
    return BridgeSubscriptionCommand::rejected;
  }
}

BridgeSubscriptionCommand BridgeSessionProtocol::unregister_subscription(
  const std::vector<uint8_t> & request_wire,
  const u2r2::Message & request,
  uint64_t maximum_response_bytes)
{
  const auto frame = u2r2::decode_frame(request_wire, impl_->limits);
  const auto parsed = impl_->parse_active_v2_request(frame);
  ValidateSameRequest(request, parsed);
  if (
    parsed.operation != u2r2::Operation::UnregisterSubscription ||
    !impl_->outbound)
  {
    throw std::invalid_argument(
            "unregister_subscription requires an active parsed contract");
  }
  const auto canonical =
    u2r2::encode_frame(frame.header, frame.payload, impl_->limits);
  auto response = impl_->replay.admit(
    parsed.request_id,
    canonical,
    maximum_response_bytes,
    impl_->writer.scheduler());
  if (response.decision() == u2r2::ReplayDecision::replay_cached) {
    impl_->writer.notify();
    return BridgeSubscriptionCommand::replayed;
  }

  std::optional<Impl::SubscriptionRecord> record;
  {
    std::lock_guard<std::mutex> lock(impl_->subscriptions_mutex);
    const auto found = impl_->subscriptions.find(parsed.contract_id);
    if (found != impl_->subscriptions.end()) {
      record = found->second;
    }
  }
  if (!record) {
    const u2r2::ProtocolError error(
      "unknown_contract",
      "the U2R2 unregister request references no live subscription",
      true);
    impl_->replay.abort(
      response,
      impl_->response_bytes(
        u2r2::Operation::SubscriptionRemoved,
        parsed.request_id,
        "error",
        0,
        &error));
    impl_->writer.notify();
    return BridgeSubscriptionCommand::rejected;
  }

  impl_->outbound->revoke(record->gate);
  u2r2::RemovalAdmission removal;
  try {
    removal = impl_->contracts.begin_unregister(
      record->identity,
      impl_->writer.scheduler(),
      impl_->replay,
      response);
  } catch (const u2r2::ProtocolError &) {
    impl_->writer.notify();
    return BridgeSubscriptionCommand::rejected;
  }
  {
    std::lock_guard<std::mutex> lock(impl_->subscriptions_mutex);
    impl_->subscriptions.erase(parsed.contract_id);
  }
  record->entity.reset();

  auto removed = u2r2::OutboundFrame::control(
    "subscription_removed:" + std::to_string(parsed.request_id),
    impl_->response_bytes(
      u2r2::Operation::SubscriptionRemoved,
      parsed.request_id,
      "ok",
      parsed.contract_id));
  const auto deadline =
    std::chrono::steady_clock::now() +
    std::chrono::milliseconds(impl_->limits.join_timeout_ms());
  while (!impl_->contracts.try_commit_removed(
      removal,
      impl_->writer.scheduler(),
      impl_->replay,
      response,
      u2r2::OutboundFrame::control(
        removed.token(),
        removed.bytes())))
  {
    if (std::chrono::steady_clock::now() >= deadline) {
      const u2r2::ProtocolError error(
        "invalid_contract",
        "the removed subscription did not drain before its deadline",
        false);
      impl_->contracts.abort_removal(
        removal,
        impl_->writer.scheduler(),
        impl_->replay,
        response,
        error);
      impl_->writer.notify();
      return BridgeSubscriptionCommand::rejected;
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(1));
  }
  impl_->writer.notify();
  return BridgeSubscriptionCommand::applied;
}

std::optional<u2r2::ControlReservation>
BridgeSessionProtocol::try_reserve_control(uint64_t bytes)
{
  return impl_->writer.try_reserve_control(bytes);
}

std::optional<u2r2::ByteLease>
BridgeSessionProtocol::try_reserve_transient(uint64_t bytes)
{
  return impl_->writer.try_reserve_transient(bytes);
}

std::optional<u2r2::ByteLease>
BridgeSessionProtocol::try_begin_read(uint64_t bytes)
{
  return impl_->writer.try_begin_read(bytes);
}

void BridgeSessionProtocol::enqueue_control(
  std::string token,
  std::vector<uint8_t> exact_frame)
{
  impl_->writer.enqueue_control(
    std::move(token),
    std::move(exact_frame));
}

std::optional<u2r2::WriteLease> BridgeSessionProtocol::try_begin_write()
{
  return impl_->writer.try_begin_write(impl_->writer_lease);
}

uint64_t BridgeSessionProtocol::wake_generation() const
{
  return impl_->writer.wake_generation();
}

bool BridgeSessionProtocol::wait_for_writer_change(
  uint64_t observed_generation,
  std::chrono::milliseconds timeout)
{
  return impl_->writer.wait_for_change(
    impl_->writer_lease,
    observed_generation,
    timeout);
}

void BridgeSessionProtocol::begin_drain()
{
  impl_->begin_drain();
}

void BridgeSessionProtocol::close()
{
  impl_->close();
}

uint64_t BridgeSessionProtocol::transient_bytes() const
{
  return impl_->writer.transient_bytes();
}

uint64_t BridgeSessionProtocol::in_flight_bytes() const
{
  return impl_->writer.in_flight_bytes();
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
