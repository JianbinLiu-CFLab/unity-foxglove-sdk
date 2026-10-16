// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/ros2_bridge
// Purpose: Cross-language authority tests for the standalone U2R2 v2 codec.

#pragma once

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

