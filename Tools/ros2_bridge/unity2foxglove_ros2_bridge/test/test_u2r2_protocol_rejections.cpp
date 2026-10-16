// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/ros2_bridge
// Purpose: U2R2 v2 protocol authority test cases.

#include "u2r2_protocol_test_support.hpp"

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

TEST(U2R2ProtocolV2, RecoverableReplayErrorsAllowHealthPongResponses)
{
  const std::array<const char *, 3U> recoverable_errors{
    "request_in_flight",
    "stale_request",
    "capacity_exceeded"};
  for (const auto * error_code : recoverable_errors) {
    SCOPED_TRACE(error_code);
    EXPECT_TRUE(
      is_stable_error_allowed_for_response(
        error_code,
        Operation::HealthPong));
    EXPECT_NO_THROW(
      (void)parse_v2(decode_frame(encode_frame(
          ErrorResponseHeader(Operation::HealthPong, error_code, false),
          {}))));
  }
}
