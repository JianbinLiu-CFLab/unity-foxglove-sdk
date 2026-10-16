// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/ros2_bridge
// Purpose: U2R2 v2 protocol authority test cases.

#include "u2r2_protocol_test_support.hpp"

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


}
