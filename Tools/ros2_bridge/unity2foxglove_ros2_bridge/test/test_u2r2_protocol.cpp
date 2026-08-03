// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/ros2_bridge
// Purpose: Cross-language authority tests for the standalone U2R2 v2 codec.

#include <gtest/gtest.h>

#include <algorithm>
#include <cstdint>
#include <fstream>
#include <limits>
#include <stdexcept>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <vector>

#include <nlohmann/json.hpp>

#include "unity2foxglove_ros2_bridge/u2r2_protocol.hpp"

namespace
{
using unity2foxglove::ros2_bridge::u2r2::Capability;
using unity2foxglove::ros2_bridge::u2r2::ConnectionState;
using unity2foxglove::ros2_bridge::u2r2::Dialect;
using unity2foxglove::ros2_bridge::u2r2::Frame;
using unity2foxglove::ros2_bridge::u2r2::MonotonicCounter;
using unity2foxglove::ros2_bridge::u2r2::Operation;
using unity2foxglove::ros2_bridge::u2r2::ProtocolError;
using unity2foxglove::ros2_bridge::u2r2::ResponseExpectation;
using unity2foxglove::ros2_bridge::u2r2::SessionStateMachine;
using unity2foxglove::ros2_bridge::u2r2::SidecarSessionIdentityAllocator;
using unity2foxglove::ros2_bridge::u2r2::decode_frame;
using unity2foxglove::ros2_bridge::u2r2::encode_frame;
using unity2foxglove::ros2_bridge::u2r2::is_stable_error_allowed_for_response;
using unity2foxglove::ros2_bridge::u2r2::parse_legacy_v1_first_frame;
using unity2foxglove::ros2_bridge::u2r2::parse_v2;
using unity2foxglove::ros2_bridge::u2r2::try_get_stable_error_terminal;
using unity2foxglove::ros2_bridge::u2r2::validate_response_correlation;

#ifndef U2R2_PROTOCOL_FIXTURE_PATH
#error "U2R2_PROTOCOL_FIXTURE_PATH must identify the shared U2R2 authority fixture"
#endif

nlohmann::json LoadFixture()
{
  std::ifstream input(U2R2_PROTOCOL_FIXTURE_PATH, std::ios::binary);
  if (!input) {
    throw std::runtime_error("unable to open shared U2R2 authority fixture");
  }
  nlohmann::json fixture;
  input >> fixture;
  return fixture;
}

std::vector<uint8_t> HexToBytes(const std::string & hex)
{
  if (hex.size() % 2 != 0) {
    throw std::runtime_error("fixture hex has an incomplete byte");
  }
  std::vector<uint8_t> result;
  result.reserve(hex.size() / 2);
  for (size_t offset = 0; offset < hex.size(); offset += 2) {
    result.push_back(static_cast<uint8_t>(
      std::stoul(hex.substr(offset, 2), nullptr, 16)));
  }
  return result;
}

std::string BytesToHex(const std::vector<uint8_t> & bytes)
{
  constexpr char kHex[] = "0123456789abcdef";
  std::string result;
  result.reserve(bytes.size() * 2);
  for (const auto value : bytes) {
    result.push_back(kHex[(value >> 4) & 0x0f]);
    result.push_back(kHex[value & 0x0f]);
  }
  return result;
}

const nlohmann::json & Vector(
  const nlohmann::json & authority,
  const std::string & id)
{
  for (const auto & vector : authority.at("operations")) {
    if (vector.at("id").get<std::string>() == id) {
      return vector;
    }
  }
  throw std::runtime_error("missing U2R2 fixture vector " + id);
}

template<typename Callback>
void ExpectProtocolError(
  const std::string & code,
  bool terminal,
  Callback callback)
{
  try {
    callback();
    FAIL() << "expected ProtocolError";
  } catch (const ProtocolError & error) {
    EXPECT_EQ(code, error.code());
    EXPECT_EQ(terminal, error.terminal());
  }
}

void WriteU32(std::vector<uint8_t> & bytes, size_t offset, uint32_t value)
{
  bytes[offset] = static_cast<uint8_t>(value & 0xff);
  bytes[offset + 1] = static_cast<uint8_t>((value >> 8) & 0xff);
  bytes[offset + 2] = static_cast<uint8_t>((value >> 16) & 0xff);
  bytes[offset + 3] = static_cast<uint8_t>((value >> 24) & 0xff);
}

std::vector<uint8_t> BuildFrame(const std::vector<uint8_t> & header)
{
  std::vector<uint8_t> frame(16 + header.size(), 0);
  frame[0] = 'U';
  frame[1] = '2';
  frame[2] = 'R';
  frame[3] = '2';
  frame[4] = 1;
  WriteU32(frame, 8, static_cast<uint32_t>(header.size()));
  std::copy(header.begin(), header.end(), frame.begin() + 16);
  return frame;
}

std::string HeaderJson(const std::vector<uint8_t> & frame)
{
  if (frame.size() < 16) {
    throw std::runtime_error("fixture frame is shorter than its fixed header");
  }
  const auto length =
    static_cast<uint32_t>(frame[8]) |
    (static_cast<uint32_t>(frame[9]) << 8U) |
    (static_cast<uint32_t>(frame[10]) << 16U) |
    (static_cast<uint32_t>(frame[11]) << 24U);
  if (16U + static_cast<uint64_t>(length) > frame.size()) {
    throw std::runtime_error("fixture frame has an invalid header length");
  }
  return std::string(frame.begin() + 16, frame.begin() + 16 + length);
}

unity2foxglove::ros2_bridge::u2r2::Message ParseVectorMessage(
  const nlohmann::json & authority,
  const std::string & id)
{
  return parse_v2(decode_frame(HexToBytes(
      Vector(authority, id).at("frameHex").get<std::string>())));
}

unity2foxglove::ros2_bridge::u2r2::LegacyV1Message LegacyFrame(
  const nlohmann::json & fixture,
  const std::string & operation)
{
  if (operation == "health_ping") {
    return parse_legacy_v1_first_frame(HexToBytes(
        fixture.at("health").at("request").at("frameHex").get<std::string>()));
  }
  if (operation == "prepare_publisher") {
    return parse_legacy_v1_first_frame(HexToBytes(
        fixture.at("preparePublisher").at("request")
        .at("frameHex").get<std::string>()));
  }
  if (operation == "publish") {
    return parse_legacy_v1_first_frame(HexToBytes(
        fixture.at("publish").at("frame").at("frameHex").get<std::string>()));
  }
  throw std::runtime_error("unhandled legacy transition operation " + operation);
}

SessionStateMachine ActiveV2State(const nlohmann::json & authority)
{
  SessionStateMachine state;
  state.accept_v2(
    ParseVectorMessage(authority, "hello_request"),
    {Capability::Publish, Capability::Subscribe});
  return state;
}

SessionStateMachine StateFromFixture(
  const std::string & value,
  const nlohmann::json & authority)
{
  if (value == "awaiting_first_frame") {
    return SessionStateMachine{};
  }
  if (value == "v2_active") {
    return ActiveV2State(authority);
  }
  throw std::runtime_error(
          "unknown or nonconstructible fixture source state " + value);
}

Operation ParseOperation(const std::string & value)
{
  if (value == "hello") {
    return Operation::Hello;
  }
  if (value == "hello_ack") {
    return Operation::HelloAck;
  }
  if (value == "health_ping") {
    return Operation::HealthPing;
  }
  if (value == "health_pong") {
    return Operation::HealthPong;
  }
  if (value == "prepare_publisher") {
    return Operation::PreparePublisher;
  }
  if (value == "publisher_ready") {
    return Operation::PublisherReady;
  }
  if (value == "publish") {
    return Operation::Publish;
  }
  if (value == "publish_result") {
    return Operation::PublishResult;
  }
  if (value == "register_subscription") {
    return Operation::RegisterSubscription;
  }
  if (value == "subscription_ready") {
    return Operation::SubscriptionReady;
  }
  if (value == "message") {
    return Operation::Message;
  }
  if (value == "unregister_subscription") {
    return Operation::UnregisterSubscription;
  }
  if (value == "subscription_removed") {
    return Operation::SubscriptionRemoved;
  }
  if (value == "busy") {
    return Operation::Busy;
  }
  if (value == "fault") {
    return Operation::Fault;
  }
  throw std::runtime_error("unknown fixture operation " + value);
}

std::vector<Operation> ResponseOperations()
{
  return {
    Operation::HelloAck,
    Operation::HealthPong,
    Operation::PublisherReady,
    Operation::PublishResult,
    Operation::SubscriptionReady,
    Operation::SubscriptionRemoved,
    Operation::Busy,
    Operation::Fault,
  };
}

std::string OperationName(Operation operation)
{
  switch (operation) {
    case Operation::HelloAck:
      return "hello_ack";
    case Operation::HealthPong:
      return "health_pong";
    case Operation::PublisherReady:
      return "publisher_ready";
    case Operation::PublishResult:
      return "publish_result";
    case Operation::SubscriptionReady:
      return "subscription_ready";
    case Operation::SubscriptionRemoved:
      return "subscription_removed";
    case Operation::Busy:
      return "busy";
    case Operation::Fault:
      return "fault";
    default:
      throw std::invalid_argument("operation is not a response");
  }
}

nlohmann::json ErrorResponseHeader(
  Operation operation,
  const std::string & code,
  bool terminal)
{
  nlohmann::json header{
    {"op", OperationName(operation)},
    {"protocolVersion", 2},
    {"requestId", 1},
    {"status", "error"},
    {"errorCode", code},
    {"message", "fixture error"},
    {"terminal", terminal},
  };
  if (operation != Operation::Busy && operation != Operation::Fault) {
    header["sessionId"] = "5e7c4e90-b5b2-4db4-b27f-5a30e8086e1b";
    header["connectionGeneration"] = 7;
  }
  return header;
}

ConnectionState ParseConnectionState(const std::string & value)
{
  if (value == "v1_probe") {
    return ConnectionState::V1Probe;
  }
  if (value == "v1_data") {
    return ConnectionState::V1Data;
  }
  if (value == "v2_active") {
    return ConnectionState::V2Active;
  }
  if (value == "terminal") {
    return ConnectionState::Terminal;
  }
  throw std::runtime_error("unknown fixture connection state " + value);
}

Dialect ParseDialect(const std::string & value)
{
  if (value == "v1") {
    return Dialect::V1;
  }
  if (value == "v2") {
    return Dialect::V2;
  }
  throw std::runtime_error("unknown fixture dialect " + value);
}

ResponseExpectation BuildResponseExpectation(
  const nlohmann::json & request,
  const nlohmann::json & authority)
{
  const auto & request_header = request.at("header");
  if (
    request_header.at("protocolVersion").get<uint32_t>() !=
    unity2foxglove::ros2_bridge::u2r2::kProtocolVersion)
  {
    return ResponseExpectation::from_hello_request(
      request_header.at("requestId").get<uint64_t>());
  }
  return ResponseExpectation::from_request(
    ParseVectorMessage(
      authority,
      request.at("id").get<std::string>()));
}

unity2foxglove::ros2_bridge::u2r2::Message ExecuteStrictJsonVector(
  const nlohmann::json & vector)
{
  const auto action = vector.at("action").get<std::string>();
  std::string raw;
  if (action == "raw_header_json") {
    raw = vector.at("rawHeaderJson").get<std::string>();
  } else if (action == "nested_padding") {
    const auto nesting = vector.at("arrayNesting").get<size_t>();
    raw =
      "{\"capabilities\":[\"publish\",\"subscribe\"],"
      "\"clientName\":\"unity2foxglove\",\"op\":\"hello\",\"padding\":" +
      std::string(nesting, '[') + "0" + std::string(nesting, ']') +
      ",\"protocolVersion\":2,\"requestId\":11}";
  } else {
    throw std::runtime_error(
            "unhandled strict-JSON vector action " + action);
  }
  return parse_v2(decode_frame(BuildFrame(
      std::vector<uint8_t>(raw.begin(), raw.end()))));
}

void ExecuteEncodeNegative(
  const nlohmann::json & vector,
  const nlohmann::json & authority)
{
  auto header = Vector(authority, "hello_request").at("header");
  const auto action = vector.at("action").get<std::string>();
  if (action == "undefined_value") {
    header["padding"] = nlohmann::json::binary({});
  } else if (action == "nonfinite_value") {
    header["padding"] = std::numeric_limits<double>::quiet_NaN();
  } else if (action == "nested_padding") {
    nlohmann::json nested = uint64_t{0};
    const auto nesting = vector.at("arrayNesting").get<size_t>();
    for (size_t depth = 0; depth < nesting; ++depth) {
      nlohmann::json wrapper = nlohmann::json::array();
      wrapper.push_back(std::move(nested));
      nested = std::move(wrapper);
    }
    header["padding"] = std::move(nested);
  } else if (action == "invalid_utf8_key") {
    header[std::string(1, static_cast<char>(0xff))] = uint64_t{0};
  } else if (action == "invalid_utf8_value") {
    header["padding"] = std::string(1, static_cast<char>(0xff));
  } else {
    throw std::runtime_error(
            "unhandled encode-negative action " + action);
  }
  encode_frame(header, {});
}

void ExecuteStateTransition(
  const nlohmann::json & transition,
  const nlohmann::json & fixture,
  const nlohmann::json & authority)
{
  auto state = StateFromFixture(
    transition.at("from").get<std::string>(),
    authority);
  const auto operation = transition.at("operation").get<std::string>();
  const auto protocol_version = transition.at("protocolVersion").get<uint32_t>();
  auto action = [&]() {
      if (protocol_version == 2 && operation == "hello") {
        state.accept_v2(
          ParseVectorMessage(authority, "hello_request"),
          {Capability::Publish, Capability::Subscribe});
      } else if (protocol_version == 2 && operation == "fault") {
        state.fault(
          transition.at("errorCode").get<std::string>(),
          "fixture-driven terminal fault");
      } else if (protocol_version == 1) {
        state.accept_legacy(LegacyFrame(fixture, operation));
      } else {
        throw std::runtime_error("unhandled fixture state transition");
      }
    };

  if (transition.contains("errorCode")) {
    ExpectProtocolError(
      transition.at("errorCode").get<std::string>(),
      true,
      action);
  } else {
    action();
  }
  EXPECT_EQ(
    ParseConnectionState(transition.at("to").get<std::string>()),
    state.state());
  if (transition.contains("dialect")) {
    EXPECT_EQ(
      ParseDialect(transition.at("dialect").get<std::string>()),
      state.dialect());
  }
  if (transition.contains("acquiresDataLease")) {
    EXPECT_EQ(
      transition.at("acquiresDataLease").get<bool>(),
      state.acquires_data_lease());
  }
}

void ExecuteNegative(
  const nlohmann::json & negative,
  const nlohmann::json & fixture,
  const nlohmann::json & authority)
{
  const auto action = negative.at("action").get<std::string>();
  if (action == "mutate_header" || action == "remove_header") {
    const auto & source =
      Vector(authority, negative.at("baseVector").get<std::string>());
    auto header = source.at("header");
    if (action == "mutate_header") {
      header[negative.at("field").get<std::string>()] = negative.at("value");
    } else {
      header.erase(negative.at("field").get<std::string>());
    }
    parse_v2(Frame{
      std::move(header),
      HexToBytes(source.at("payloadHex").get<std::string>())});
    return;
  }
  if (action == "oversized_topic") {
    const auto & source =
      Vector(authority, negative.at("baseVector").get<std::string>());
    auto header = source.at("header");
    const auto topic_length = negative.at("topicLength").get<size_t>();
    header["topic"] = "/" + std::string(topic_length - 1, 'a');
    parse_v2(Frame{
      std::move(header),
      HexToBytes(source.at("payloadHex").get<std::string>())});
    return;
  }
  if (action == "raw_header_json") {
    const auto raw = negative.at("rawHeaderJson").get<std::string>();
    parse_v2(decode_frame(BuildFrame(
        std::vector<uint8_t>(raw.begin(), raw.end()))));
    return;
  }
  if (action == "raw_header_hex") {
    decode_frame(BuildFrame(HexToBytes(
        negative.at("headerHex").get<std::string>())));
    return;
  }
  if (action == "counter_wrap") {
    MonotonicCounter(std::numeric_limits<uint64_t>::max()).next();
    return;
  }
  if (action == "state_downgrade") {
    auto state = ActiveV2State(authority);
    state.accept_legacy(LegacyFrame(fixture, "publish"));
    return;
  }
  if (action == "missing_capability") {
    SessionStateMachine state;
    state.accept_v2(
      ParseVectorMessage(authority, "hello_missing_capability"),
      {Capability::Publish, Capability::Subscribe});
    return;
  }
  if (action == "forge_hello_identity") {
    auto header = Vector(authority, "hello_request").at("header");
    header["sessionId"] = authority.at("sessionId");
    header["connectionGeneration"] = authority.at("connectionGeneration");
    parse_v2(Frame{std::move(header), {}});
    return;
  }
  if (action == "replace_payload") {
    const auto & source =
      Vector(authority, negative.at("baseVector").get<std::string>());
    parse_v2(Frame{
      source.at("header"),
      HexToBytes(negative.at("payloadHex").get<std::string>())});
    return;
  }
  if (action == "response_status_ok") {
    const auto & source =
      Vector(authority, negative.at("baseVector").get<std::string>());
    auto header = source.at("header");
    header["status"] = "ok";
    header.erase("errorCode");
    header.erase("message");
    header.erase("terminal");
    parse_v2(Frame{
      std::move(header),
      HexToBytes(source.at("payloadHex").get<std::string>())});
    return;
  }
  throw std::runtime_error("unhandled negative-vector action " + action);
}
}  // namespace

TEST(U2R2ProtocolV2, SharedAuthorityMatchesCanonicalCppFramesAndCorrelation)
{
  const auto authority = LoadFixture().at("v2");
  EXPECT_EQ(
    unity2foxglove::ros2_bridge::u2r2::kProtocolVersion,
    authority.at("protocolVersion").get<uint32_t>());
  EXPECT_EQ(
    unity2foxglove::ros2_bridge::u2r2::kEnvelopeVersion,
    authority.at("envelopeVersion").get<uint16_t>());
  EXPECT_EQ(64, authority.at("jsonMaxDepth").get<int>());
  EXPECT_EQ(
    "utf8_byte_ordinal",
    authority.at("canonicalKeyOrder").get<std::string>());
  ASSERT_EQ(21U, authority.at("operations").size());

  std::unordered_map<std::string, nlohmann::json> vectors;
  for (const auto & vector : authority.at("operations")) {
    vectors.emplace(vector.at("id").get<std::string>(), vector);
    const auto payload = HexToBytes(vector.at("payloadHex").get<std::string>());
    const auto kind = vector.at("kind").get<std::string>();
    const auto direction = vector.at("direction").get<std::string>();
    EXPECT_TRUE(kind == "request" || kind == "response" || kind == "event");
    EXPECT_EQ(
      kind == "request" ? "client_to_sidecar" : "sidecar_to_client",
      direction);
    EXPECT_EQ(payload.size(), vector.at("payloadLength").get<size_t>());
    EXPECT_EQ(kind == "response", vector.contains("correlatesTo"));
    EXPECT_EQ(
      kind == "response" &&
      vector.at("header").value("status", std::string{}) == "error"
      ? vector.at("header").at("terminal").get<bool>()
      : false,
      vector.at("terminal").get<bool>());
    const auto encoded = encode_frame(vector.at("header"), payload);
    EXPECT_EQ(vector.at("frameHex").get<std::string>(), BytesToHex(encoded));

    const auto decoded = decode_frame(encoded);
    EXPECT_EQ(vector.at("header"), decoded.header);
    EXPECT_EQ(payload, decoded.payload);
    EXPECT_EQ(
      vector.at("headerJson").get<std::string>(),
      HeaderJson(encoded));
    EXPECT_EQ(encoded, encode_frame(decoded.header, decoded.payload));

    if (vector.at("id").get<std::string>() != "hello_unsupported_version") {
      const auto message = parse_v2(decoded);
      EXPECT_EQ(kind == "request", message.is_request);
      EXPECT_EQ(kind == "response", message.is_response);
      EXPECT_EQ(vector.at("terminal").get<bool>(), message.terminal);
      if (message.is_request) {
        EXPECT_NE(0U, message.request_id);
      }
      if (vector.at("header").contains("sessionId")) {
        EXPECT_EQ(
          authority.at("sessionId").get<std::string>(),
          message.session_id);
        EXPECT_EQ(
          authority.at("connectionGeneration").get<uint64_t>(),
          message.connection_generation);
      }
      if (message.is_response) {
        EXPECT_EQ(
          vector.at("header").at("status").get<std::string>(),
          message.status);
        EXPECT_EQ(
          vector.at("header").value("errorCode", std::string{}),
          message.error_code);
        EXPECT_EQ(
          vector.at("header").value("message", std::string{}),
          message.error_message);
        if (message.status == "error") {
          EXPECT_EQ(
            vector.at("header").at("terminal").get<bool>(),
            message.terminal);
        }
      }
      if (message.operation == Operation::Publish) {
        EXPECT_EQ(
          vector.at("header").at("logTimeNs").get<uint64_t>(),
          message.log_time_ns);
        EXPECT_EQ(
          vector.at("header").at("sequence").get<uint64_t>(),
          message.sequence);
        EXPECT_EQ(0U, message.receive_time_ns);
      }
      if (message.operation == Operation::Message) {
        EXPECT_FALSE(vector.at("header").contains("logTimeNs"));
        EXPECT_EQ(
          vector.at("header").at("receiveTimeNs").get<uint64_t>(),
          message.receive_time_ns);
        EXPECT_EQ(
          vector.at("header").at("sequence").get<uint64_t>(),
          message.sequence);
        EXPECT_EQ(0U, message.log_time_ns);
        EXPECT_EQ("cdr", message.encoding);
        EXPECT_EQ("xcdr1-le", message.representation);
      }
    }
  }
  const auto & hello_header_json =
    vectors.at("hello_request").at("headerJson").get_ref<const std::string &>();
  EXPECT_LT(
    hello_header_json.find("\\ue000\":1"),
    hello_header_json.find("\\ud83d\\ude00\":2"));
  EXPECT_NE(
    std::string::npos,
    hello_header_json.find(
      "\"unicodeSample\":\"\\u0085\\u2028\\u2029"
      "\\ud83d\\ude00\\ue000\""));

  for (const auto & response : authority.at("operations")) {
    if (response.at("kind").get<std::string>() != "response") {
      continue;
    }
    const auto & request =
      vectors.at(response.at("correlatesTo").get<std::string>());
    const auto expected =
      BuildResponseExpectation(request, authority);
    const auto parsed = parse_v2(decode_frame(
      HexToBytes(response.at("frameHex").get<std::string>())));
    EXPECT_NO_THROW(validate_response_correlation(expected, parsed));
  }

  ASSERT_EQ(2U, authority.at("unsigned64Vectors").size());
  for (const auto & vector : authority.at("unsigned64Vectors")) {
    const auto encoded = encode_frame(vector.at("header"), {});
    EXPECT_EQ(vector.at("frameHex").get<std::string>(), BytesToHex(encoded));
    const auto decoded = decode_frame(encoded);
    EXPECT_EQ(vector.at("headerJson").get<std::string>(), HeaderJson(encoded));
    EXPECT_EQ(encoded, encode_frame(decoded.header, decoded.payload));
    const auto message = parse_v2(decoded);
    EXPECT_EQ(
      std::stoull(vector.at("valueDecimal").get<std::string>()),
      message.request_id);
  }

  ASSERT_EQ(3U, authority.at("strictJsonVectors").size());
  for (const auto & vector : authority.at("strictJsonVectors")) {
    if (vector.at("valid").get<bool>()) {
      const auto message = ExecuteStrictJsonVector(vector);
      EXPECT_EQ(Operation::Hello, message.operation);
    } else {
      ExpectProtocolError(
        vector.at("expectedErrorCode").get<std::string>(),
        vector.at("terminal").get<bool>(),
      [&]() {ExecuteStrictJsonVector(vector);});
    }
  }

  ASSERT_EQ(5U, authority.at("encodeNegativeVectors").size());
  for (const auto & vector : authority.at("encodeNegativeVectors")) {
    ExpectProtocolError(
      vector.at("expectedErrorCode").get<std::string>(),
      vector.at("terminal").get<bool>(),
      [&]() {ExecuteEncodeNegative(vector, authority);});
  }
}

TEST(U2R2ProtocolV2, DataOperationsRequireNonzeroSequence)
{
  const auto authority = LoadFixture().at("v2");
  auto header = Vector(authority, "message").at("header");
  const auto payload = HexToBytes(
    Vector(authority, "message").at("payloadHex").get<std::string>());

  header.erase("sequence");
  ExpectProtocolError(
    "invalid_frame",
    true,
    [&]() {parse_v2({header, payload});});

  header["sequence"] = 0;
  ExpectProtocolError(
    "invalid_frame",
    true,
    [&]() {parse_v2({header, payload});});

  auto publish_header = Vector(authority, "publish").at("header");
  const auto publish_payload = HexToBytes(
    Vector(authority, "publish").at("payloadHex").get<std::string>());
  publish_header.erase("sequence");
  ExpectProtocolError(
    "invalid_frame",
    true,
    [&]() {parse_v2({publish_header, publish_payload});});

  publish_header["sequence"] = 0;
  ExpectProtocolError(
    "invalid_frame",
    true,
    [&]() {parse_v2({publish_header, publish_payload});});
}

TEST(
  U2R2ProtocolV2,
  ResponseCorrelationIsDerivedOnlyFromRequestAndChecksEveryContextDimension)
{
  const auto authority = LoadFixture().at("v2");
  const auto hello_expectation = ResponseExpectation::from_request(
    ParseVectorMessage(authority, "hello_request"));
  EXPECT_EQ(
    (std::vector<Operation>{
      Operation::HelloAck,
      Operation::Busy,
      Operation::Fault}),
    hello_expectation.allowed_response_operations());
  EXPECT_NO_THROW(validate_response_correlation(
      hello_expectation,
      ParseVectorMessage(authority, "hello_ack")));
  EXPECT_NO_THROW(validate_response_correlation(
      hello_expectation,
      ParseVectorMessage(authority, "busy")));

  const auto unsupported_hello = ResponseExpectation::from_hello_request(
    Vector(authority, "hello_unsupported_version")
    .at("header").at("requestId").get<uint64_t>());
  EXPECT_NO_THROW(validate_response_correlation(
      unsupported_hello,
      ParseVectorMessage(authority, "protocol_rejected")));

  const auto health_request =
    ParseVectorMessage(authority, "health_ping");
  const auto health_expectation =
    ResponseExpectation::from_request(health_request);
  EXPECT_EQ(
    (std::vector<Operation>{
      Operation::HealthPong,
      Operation::Busy,
      Operation::Fault}),
    health_expectation.allowed_response_operations());
  const auto health_response = ParseVectorMessage(authority, "health_pong");
  EXPECT_NO_THROW(validate_response_correlation(
      health_expectation,
      health_response));

  EXPECT_THROW(
    ResponseExpectation::from_request(health_response),
    std::invalid_argument);

  auto mismatched_request = health_request;
  mismatched_request.request_id = 3;
  ExpectProtocolError(
    "response_mismatch",
    true,
    [&]() {
      validate_response_correlation(
        ResponseExpectation::from_request(mismatched_request),
        health_response);
    });

  mismatched_request = health_request;
  mismatched_request.operation = Operation::PreparePublisher;
  ExpectProtocolError(
    "response_mismatch",
    true,
    [&]() {
      validate_response_correlation(
        ResponseExpectation::from_request(mismatched_request),
        health_response);
    });

  mismatched_request = health_request;
  mismatched_request.session_id = "different-session";
  ExpectProtocolError(
    "response_mismatch",
    true,
    [&]() {
      validate_response_correlation(
        ResponseExpectation::from_request(mismatched_request),
        health_response);
    });

  mismatched_request = health_request;
  ++mismatched_request.connection_generation;
  ExpectProtocolError(
    "response_mismatch",
    true,
    [&]() {
      validate_response_correlation(
        ResponseExpectation::from_request(mismatched_request),
        health_response);
    });

  const auto subscription_response =
    ParseVectorMessage(authority, "subscription_ready");
  auto subscription_request =
    ParseVectorMessage(authority, "register_subscription");
  subscription_request.contract_id = 42;
  ExpectProtocolError(
    "response_mismatch",
    true,
    [&]() {
      validate_response_correlation(
        ResponseExpectation::from_request(subscription_request),
        subscription_response);
    });

  const auto publish_response =
    ParseVectorMessage(authority, "publish_result");
  auto publish_request = ParseVectorMessage(authority, "publish");
  publish_request.message_id = 2;
  ExpectProtocolError(
    "response_mismatch",
    true,
    [&]() {
      validate_response_correlation(
        ResponseExpectation::from_request(publish_request),
        publish_response);
    });

  auto forged_busy = ParseVectorMessage(authority, "busy");
  forged_busy.session_id = authority.at("sessionId").get<std::string>();
  forged_busy.connection_generation =
    authority.at("connectionGeneration").get<uint64_t>();
  ExpectProtocolError(
    "response_mismatch",
    true,
    [&]() {validate_response_correlation(hello_expectation, forged_busy);});

  const auto terminal_expectation = ResponseExpectation::from_request(
    ParseVectorMessage(authority, "terminal_fault_request"));
  auto unbound_fault = ParseVectorMessage(authority, "terminal_fault");
  unbound_fault.session_id.clear();
  unbound_fault.connection_generation = 0;
  ExpectProtocolError(
    "response_mismatch",
    true,
    [&]() {validate_response_correlation(terminal_expectation, unbound_fault);});
}

TEST(U2R2ProtocolV2, StrictDecoderRejectsEnvelopeUtf8DuplicateAndTrailingRoot)
{
  const auto authority = LoadFixture().at("v2");
  const auto hello = HexToBytes(
    Vector(authority, "hello_request").at("frameHex").get<std::string>());

  auto bad_magic = hello;
  bad_magic[0] = 'X';
  ExpectProtocolError("invalid_frame", true, [&]() {decode_frame(bad_magic);});

  auto bad_version = hello;
  bad_version[4] = 2;
  ExpectProtocolError("invalid_frame", true, [&]() {decode_frame(bad_version);});

  auto bad_flags = hello;
  bad_flags[6] = 1;
  ExpectProtocolError("invalid_frame", true, [&]() {decode_frame(bad_flags);});

  ExpectProtocolError(
    "invalid_frame",
    true,
    [&]() {decode_frame(BuildFrame({0xff}));});

  const std::string duplicate =
    "{\"op\":\"hello\",\"op\":\"hello\",\"protocolVersion\":2,\"requestId\":1}";
  ExpectProtocolError(
    "invalid_frame",
    true,
    [&]() {
      decode_frame(BuildFrame(
          std::vector<uint8_t>(duplicate.begin(), duplicate.end())));
    });

  const std::string trailing = "{}{}";
  ExpectProtocolError(
    "invalid_frame",
    true,
    [&]() {
      decode_frame(BuildFrame(
          std::vector<uint8_t>(trailing.begin(), trailing.end())));
    });

  const std::string comment =
    "{\"op\":\"hello\",/*comment*/\"protocolVersion\":2,\"requestId\":1}";
  ExpectProtocolError(
    "invalid_frame",
    true,
    [&]() {
      decode_frame(BuildFrame(
          std::vector<uint8_t>(comment.begin(), comment.end())));
    });

  auto extra = hello;
  extra.push_back(0);
  ExpectProtocolError("invalid_frame", true, [&]() {decode_frame(extra);});
}

TEST(U2R2ProtocolV2, ModelLocksCountersCapabilitiesAndOneDialectPerSocket)
{
  const auto fixture = LoadFixture();
  const auto authority = fixture.at("v2");

  auto zero = Vector(authority, "hello_request").at("header");
  zero["requestId"] = 0;
  ExpectProtocolError(
    "invalid_request_id",
    true,
    [&]() {parse_v2(Frame{zero, {}});});

  ExpectProtocolError(
    "unsupported_protocol",
    true,
    [&]() {
      parse_v2(decode_frame(HexToBytes(
          Vector(authority, "hello_unsupported_version")
          .at("frameHex").get<std::string>())));
    });

  auto whitespace_operation =
    Vector(authority, "hello_request").at("header");
  whitespace_operation["op"] = " \t ";
  ExpectProtocolError(
    "invalid_frame",
    true,
    [&]() {parse_v2(Frame{whitespace_operation, {}});});

  auto whitespace_session =
    Vector(authority, "terminal_fault_request").at("header");
  whitespace_session["sessionId"] = " \t ";
  ExpectProtocolError(
    "invalid_frame",
    true,
    [&]() {parse_v2(Frame{whitespace_session, {}});});

  MonotonicCounter counter(std::numeric_limits<uint64_t>::max() - 1);
  EXPECT_EQ(std::numeric_limits<uint64_t>::max(), counter.next());
  ExpectProtocolError("counter_exhausted", true, [&]() {counter.next();});
  EXPECT_TRUE(counter.faulted());

  SidecarSessionIdentityAllocator allocator;
  const auto first_identity = allocator.allocate();
  const auto second_identity = allocator.allocate();
  EXPECT_FALSE(first_identity.session_id().empty());
  EXPECT_FALSE(second_identity.session_id().empty());
  EXPECT_NE(first_identity.session_id(), second_identity.session_id());
  EXPECT_EQ(1U, first_identity.connection_generation());
  EXPECT_EQ(2U, second_identity.connection_generation());

  SessionStateMachine state;
  state.accept_v2(
    parse_v2(decode_frame(HexToBytes(
        Vector(authority, "hello_request").at("frameHex").get<std::string>()))),
    {Capability::Publish, Capability::Subscribe});
  EXPECT_EQ(Dialect::V2, state.dialect());
  EXPECT_EQ(ConnectionState::V2Active, state.state());
  EXPECT_TRUE(state.acquires_data_lease());

  const auto legacy_publish = parse_legacy_v1_first_frame(
    HexToBytes(fixture.at("publish").at("frame").at("frameHex").get<std::string>()));
  ExpectProtocolError(
    "dialect_downgrade",
    true,
    [&]() {state.accept_legacy(legacy_publish);});
  EXPECT_EQ(ConnectionState::Terminal, state.state());

  SessionStateMachine insufficient;
  ExpectProtocolError(
    "missing_capability",
    true,
    [&]() {
      insufficient.accept_v2(
        parse_v2(decode_frame(HexToBytes(
            Vector(authority, "hello_missing_capability")
            .at("frameHex").get<std::string>()))),
        {Capability::Publish, Capability::Subscribe});
    });
  EXPECT_EQ(ConnectionState::Terminal, insufficient.state());

  const auto legacy_health = parse_legacy_v1_first_frame(
    HexToBytes(fixture.at("health").at("request").at("frameHex").get<std::string>()));
  EXPECT_EQ("health_ping", legacy_health.operation);
  EXPECT_EQ(
    fixture.at("health").at("requestId").get<std::string>(),
    legacy_health.request_id);
  EXPECT_FALSE(legacy_health.acquires_data_lease);
}

TEST(U2R2ProtocolV2, SharedLedgersExecuteEveryErrorTransitionAndNegativeVector)
{
  const auto fixture = LoadFixture();
  const auto authority = fixture.at("v2");

  std::unordered_set<std::string> seen_codes;
  ASSERT_EQ(23U, authority.at("errorCodes").size());
  for (const auto & entry : authority.at("errorCodes")) {
    const auto code = entry.at("code").get<std::string>();
    const auto wire = entry.at("wire").get<bool>();
    ASSERT_TRUE(seen_codes.insert(code).second);
    bool terminal = false;
    EXPECT_TRUE(try_get_stable_error_terminal(code, terminal));
    EXPECT_EQ(entry.at("terminal").get<bool>(), terminal);

    std::vector<Operation> allowed;
    for (const auto & operation : entry.at("responseOps")) {
      allowed.push_back(ParseOperation(operation.get<std::string>()));
    }
    EXPECT_EQ(wire, !allowed.empty());
    for (const auto operation : allowed) {
      EXPECT_TRUE(is_stable_error_allowed_for_response(code, operation));
      const auto parsed = parse_v2(decode_frame(encode_frame(
          ErrorResponseHeader(operation, code, terminal),
          {})));
      EXPECT_EQ(operation, parsed.operation);
      EXPECT_EQ(code, parsed.error_code);
      EXPECT_EQ(terminal, parsed.terminal);
    }
    for (const auto operation : ResponseOperations()) {
      if (std::find(allowed.begin(), allowed.end(), operation) == allowed.end()) {
        EXPECT_FALSE(is_stable_error_allowed_for_response(code, operation));
      }
    }
    if (!wire) {
      ExpectProtocolError("invalid_frame", true, [&]() {
        (void)parse_v2(decode_frame(encode_frame(
              ErrorResponseHeader(Operation::Fault, code, terminal),
              {})));
      });
    }
  }
  bool terminal = false;
  EXPECT_FALSE(try_get_stable_error_terminal("not_in_authority", terminal));

  ASSERT_EQ(6U, authority.at("stateTransitions").size());
  for (const auto & transition : authority.at("stateTransitions")) {
    ExecuteStateTransition(transition, fixture, authority);
  }
  auto mistyped_transition = authority.at("stateTransitions").front();
  mistyped_transition["from"] = "v2_actvie";
  EXPECT_THROW(
    ExecuteStateTransition(mistyped_transition, fixture, authority),
    std::runtime_error);

  std::unordered_set<std::string> seen_negatives;
  ASSERT_EQ(51U, authority.at("negativeVectors").size());
  for (const auto & negative : authority.at("negativeVectors")) {
    ASSERT_TRUE(
      seen_negatives.insert(negative.at("id").get<std::string>()).second);
    ExpectProtocolError(
      negative.at("expectedErrorCode").get<std::string>(),
      negative.at("terminal").get<bool>(),
      [&]() {ExecuteNegative(negative, fixture, authority);});
  }
}
