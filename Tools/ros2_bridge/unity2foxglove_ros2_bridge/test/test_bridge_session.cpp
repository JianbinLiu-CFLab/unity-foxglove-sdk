// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/ros2_bridge
// Purpose: RED-first first-frame, v2 identity, and replay integration tests.

#include <cstdint>
#include <string>
#include <vector>

#include <gtest/gtest.h>
#include <nlohmann/json.hpp>

#include "unity2foxglove_ros2_bridge/bridge_lifecycle.hpp"
#include "unity2foxglove_ros2_bridge/bridge_session.hpp"

namespace
{
namespace runtime = unity2foxglove::ros2_bridge::runtime;
namespace u2r2 = unity2foxglove::ros2_bridge::u2r2;

std::vector<uint8_t> Encode(const nlohmann::json & header)
{
  return u2r2::encode_frame(header, {});
}

std::vector<uint8_t> Hello(uint64_t request_id = 1)
{
  return Encode({
      {"op", "hello"},
      {"protocolVersion", 2},
      {"requestId", request_id},
      {"clientName", "phase186c-test"},
      {"capabilities", nlohmann::json::array({"publish"})},
    });
}

std::vector<uint8_t> LegacyHealth(const std::string & request_id = "health-1")
{
  return Encode({
      {"op", "health_ping"},
      {"protocolVersion", 1},
      {"requestId", request_id},
    });
}

std::vector<uint8_t> LegacyPrepare(const std::string & request_id = "prepare-1")
{
  return Encode({
      {"op", "prepare_publisher"},
      {"protocolVersion", 1},
      {"requestId", request_id},
      {"topic", "/phase186/v1/state"},
      {"schemaName", "std_msgs/msg/String"},
      {"encoding", "cdr"},
      {"qos", {
          {"profile", "default"},
          {"reliability", "reliable"},
          {"durability", "volatile"},
          {"history", "keep_last"},
          {"depth", 10},
        }},
    });
}

nlohmann::json SessionHeader(
  const runtime::BridgeSessionProtocol & session,
  const char * operation,
  uint64_t request_id)
{
  return {
    {"op", operation},
    {"protocolVersion", 2},
    {"requestId", request_id},
    {"sessionId", session.session_id()},
    {"connectionGeneration", session.connection_generation()},
  };
}

std::vector<uint8_t> V2Prepare(
  const runtime::BridgeSessionProtocol & session,
  uint64_t request_id,
  const std::string & topic = "/phase186/v2/state")
{
  auto header = SessionHeader(session, "prepare_publisher", request_id);
  header["topic"] = topic;
  header["schemaName"] = "std_msgs/msg/String";
  header["encoding"] = "cdr";
  header["qos"] = {
    {"profile", "default"},
    {"reliability", "reliable"},
    {"durability", "volatile"},
    {"history", "keep_last"},
    {"depth", 10},
  };
  return Encode(header);
}

std::vector<uint8_t> V2Publish(
  const runtime::BridgeSessionProtocol & session,
  uint64_t request_id,
  uint64_t message_id,
  const std::string & topic = "/phase186/v2/state")
{
  auto header = SessionHeader(session, "publish", request_id);
  header["messageId"] = message_id;
  header["topic"] = topic;
  header["schemaName"] = "std_msgs/msg/String";
  header["encoding"] = "cdr";
  header["logTimeNs"] = 186;
  header["sequence"] = message_id;
  header["qos"] = {
    {"profile", "default"},
    {"reliability", "reliable"},
    {"durability", "volatile"},
    {"history", "keep_last"},
    {"depth", 10},
  };
  return u2r2::encode_frame(header, {0x00, 0x01, 0x00, 0x00, 0x01});
}

runtime::BridgeSessionProtocol ActiveV2Session(
  runtime::ProcessConnectionAuthority & process,
  const u2r2::ProtocolLimits & limits = u2r2::ProtocolLimits::defaults())
{
  runtime::BridgeSessionProtocol session(limits);
  const auto first = session.accept_first_frame(Hello());
  EXPECT_EQ(runtime::FirstFrameRole::data_session, first.role);
  session.bind_v2_identity(process.allocate_session_identity());
  auto hello_ack = session.try_begin_write();
  EXPECT_TRUE(hello_ack.has_value());
  if (hello_ack) {
    hello_ack->release();
  }
  return session;
}

TEST(BridgeFirstFrame, ClassifiesOneShotV1ProbeAndSharedDataRoles)
{
  runtime::BridgeSessionProtocol health(u2r2::ProtocolLimits::defaults());
  const auto probe = health.accept_first_frame(LegacyHealth());
  EXPECT_EQ(u2r2::Dialect::V1, probe.dialect);
  EXPECT_EQ(runtime::FirstFrameRole::probe, probe.role);
  EXPECT_TRUE(probe.one_shot);

  runtime::BridgeSessionProtocol legacy_data(u2r2::ProtocolLimits::defaults());
  const auto prepare = legacy_data.accept_first_frame(LegacyPrepare());
  EXPECT_EQ(u2r2::Dialect::V1, prepare.dialect);
  EXPECT_EQ(runtime::FirstFrameRole::data_session, prepare.role);
  EXPECT_FALSE(prepare.one_shot);

  runtime::BridgeSessionProtocol v2(u2r2::ProtocolLimits::defaults());
  const auto hello = v2.accept_first_frame(Hello());
  EXPECT_EQ(u2r2::Dialect::V2, hello.dialect);
  EXPECT_EQ(runtime::FirstFrameRole::data_session, hello.role);
  EXPECT_FALSE(hello.one_shot);
}

TEST(BridgeFirstFrame, V1AndV2ContendForTheSameDataLease)
{
  runtime::ProcessConnectionAuthority process(
    u2r2::ProtocolLimits::defaults());
  runtime::BridgeSessionProtocol legacy(u2r2::ProtocolLimits::defaults());
  runtime::BridgeSessionProtocol v2(u2r2::ProtocolLimits::defaults());
  ASSERT_EQ(
    runtime::FirstFrameRole::data_session,
    legacy.accept_first_frame(LegacyPrepare()).role);
  ASSERT_EQ(
    runtime::FirstFrameRole::data_session,
    v2.accept_first_frame(Hello()).role);

  auto legacy_lease =
    process.try_acquire_role(u2r2::ConnectionRole::data_session);
  ASSERT_TRUE(legacy_lease.has_value());
  EXPECT_FALSE(
    process.try_acquire_role(u2r2::ConnectionRole::data_session).has_value());
  legacy_lease->release();
  EXPECT_TRUE(
    process.try_acquire_role(u2r2::ConnectionRole::data_session).has_value());
}

TEST(BridgeV2Session, HelloAckFreezesSidecarIdentityAndPublishCapability)
{
  runtime::ProcessConnectionAuthority process(
    u2r2::ProtocolLimits::defaults());
  runtime::BridgeSessionProtocol session(u2r2::ProtocolLimits::defaults());
  const auto hello = session.accept_first_frame(Hello(11));
  ASSERT_EQ(runtime::FirstFrameRole::data_session, hello.role);
  session.bind_v2_identity(process.allocate_session_identity());

  auto write = session.try_begin_write();
  ASSERT_TRUE(write.has_value());
  const auto response = u2r2::parse_v2(
    u2r2::decode_frame(write->frame().bytes()));
  EXPECT_EQ(u2r2::Operation::HelloAck, response.operation);
  EXPECT_EQ(11U, response.request_id);
  EXPECT_EQ(session.session_id(), response.session_id);
  EXPECT_EQ(
    session.connection_generation(),
    response.connection_generation);
  ASSERT_EQ(1U, response.capabilities.size());
  EXPECT_EQ(u2r2::Capability::Publish, response.capabilities[0]);
  write->release();
}

TEST(BridgeV2Session, HelloRequestIdIsTheInitialSessionHighWaterMark)
{
  runtime::ProcessConnectionAuthority process(
    u2r2::ProtocolLimits::defaults());
  runtime::BridgeSessionProtocol session(u2r2::ProtocolLimits::defaults());
  ASSERT_EQ(
    runtime::FirstFrameRole::data_session,
    session.accept_first_frame(Hello(9)).role);
  session.bind_v2_identity(process.allocate_session_identity());
  auto hello_ack = session.try_begin_write();
  ASSERT_TRUE(hello_ack.has_value());
  hello_ack->release();

  try {
    (void)session.parse_v2_request(V2Prepare(session, 9));
    FAIL() << "a request reused the hello request ID";
  } catch (const u2r2::ProtocolError & error) {
    EXPECT_EQ("request_id_conflict", error.code());
    EXPECT_TRUE(error.terminal());
  }

  try {
    (void)session.parse_v2_request(V2Prepare(session, 8));
    FAIL() << "a request fell below the hello request ID";
  } catch (const u2r2::ProtocolError & error) {
    EXPECT_EQ("stale_request", error.code());
    EXPECT_FALSE(error.terminal());
  }

  EXPECT_NO_THROW(
    (void)session.parse_v2_request(V2Prepare(session, 10)));
}

TEST(BridgeV2Session, WrongSessionIdentityIsTerminalBeforeDispatch)
{
  runtime::ProcessConnectionAuthority process(
    u2r2::ProtocolLimits::defaults());
  auto session = ActiveV2Session(process);
  auto wrong = SessionHeader(session, "prepare_publisher", 2);
  wrong["sessionId"] = "wrong-session";
  wrong["topic"] = "/phase186/v2/state";
  wrong["schemaName"] = "std_msgs/msg/String";
  wrong["encoding"] = "cdr";
  wrong["qos"] = {
    {"profile", "default"},
    {"reliability", "reliable"},
    {"durability", "volatile"},
    {"history", "keep_last"},
    {"depth", 10},
  };

  try {
    (void)session.parse_v2_request(Encode(wrong));
    FAIL() << "wrong session identity was accepted";
  } catch (const u2r2::ProtocolError & error) {
    EXPECT_EQ("invalid_frame", error.code());
    EXPECT_TRUE(error.terminal());
  }
}

TEST(BridgeV2Session, PublisherMustBePreparedBeforePublish)
{
  runtime::ProcessConnectionAuthority process(
    u2r2::ProtocolLimits::defaults());
  auto session = ActiveV2Session(process);
  const auto preparation = session.parse_v2_request(V2Prepare(session, 2));
  const auto publish = session.parse_v2_request(V2Publish(session, 3, 1));

  EXPECT_THROW(session.require_publisher_ready(publish), u2r2::ProtocolError);
  session.mark_publisher_ready(preparation);
  EXPECT_NO_THROW(session.require_publisher_ready(publish));

  const auto other_topic =
    session.parse_v2_request(V2Publish(session, 4, 2, "/phase186/v2/other"));
  EXPECT_THROW(
    session.require_publisher_ready(other_topic),
    u2r2::ProtocolError);
}

TEST(BridgeV2Session, PublisherCapacityFailsBeforeAnotherContractCanMutate)
{
  const auto limits = u2r2::ProtocolLimits::defaults().with({
    {"maxContracts", 1},
  });
  runtime::ProcessConnectionAuthority process(limits);
  auto session = ActiveV2Session(process, limits);
  const auto first =
    session.parse_v2_request(V2Prepare(session, 2));
  session.require_publisher_capacity(first);
  session.mark_publisher_ready(first);

  const auto second = session.parse_v2_request(
    V2Prepare(session, 3, "/phase186/v2/second"));
  try {
    session.require_publisher_capacity(second);
    FAIL() << "a second publisher exceeded the frozen contract capacity";
  } catch (const u2r2::ProtocolError & error) {
    EXPECT_EQ("capacity_exceeded", error.code());
    EXPECT_FALSE(error.terminal());
  }
}

TEST(BridgeV2Session, ReplayReservationOccursBeforeMutationAndDuplicatesDoNotMutate)
{
  runtime::ProcessConnectionAuthority process(
    u2r2::ProtocolLimits::defaults());
  auto session = ActiveV2Session(process);
  const auto request_wire = V2Prepare(session, 2);
  const auto request = session.parse_v2_request(request_wire);
  std::size_t mutations = 0;
  const auto response = u2r2::encode_frame(
    {
      {"op", "publisher_ready"},
      {"protocolVersion", 2},
      {"requestId", 2},
      {"status", "ok"},
      {"sessionId", session.session_id()},
      {"connectionGeneration", session.connection_generation()},
    },
    {});

  const auto first = session.execute_replayable(
    request_wire,
    request,
    65552,
    [&]() {
      ++mutations;
      return runtime::ReplayMutationResult::success(response);
    });
  EXPECT_EQ(u2r2::ReplayDecision::begin_mutation, first);
  auto first_write = session.try_begin_write();
  ASSERT_TRUE(first_write.has_value());
  EXPECT_EQ(response, first_write->frame().bytes());
  first_write->release();

  const auto duplicate = session.execute_replayable(
    request_wire,
    request,
    65552,
    [&]() {
      ++mutations;
      return runtime::ReplayMutationResult::success(response);
    });
  EXPECT_EQ(u2r2::ReplayDecision::replay_cached, duplicate);
  EXPECT_EQ(1U, mutations);
  auto replay_write = session.try_begin_write();
  ASSERT_TRUE(replay_write.has_value());
  EXPECT_EQ(response, replay_write->frame().bytes());
  replay_write->release();

  auto conflicting_wire = V2Prepare(session, 2, "/phase186/v2/conflict");
  const auto conflicting = session.parse_v2_request(conflicting_wire);
  EXPECT_THROW(
    session.execute_replayable(
      conflicting_wire,
      conflicting,
      65552,
      [&]() {
        ++mutations;
        return runtime::ReplayMutationResult::success(response);
      }),
    u2r2::ProtocolError);
  EXPECT_EQ(1U, mutations);
}

TEST(BridgeV2Session, ExhaustedControlCapacityPreventsMutation)
{
  runtime::ProcessConnectionAuthority process(
    u2r2::ProtocolLimits::defaults());
  auto session = ActiveV2Session(process);
  auto held = session.try_reserve_control(
    u2r2::ProtocolLimits::defaults().reserved_control_queue_bytes());
  ASSERT_TRUE(held.has_value());

  const auto request_wire = V2Prepare(session, 2);
  const auto request = session.parse_v2_request(request_wire);
  std::size_t mutations = 0;
  try {
    (void)session.execute_replayable(
      request_wire,
      request,
      1,
      [&]() {
        ++mutations;
        return runtime::ReplayMutationResult::success({});
      });
    FAIL() << "request was admitted without response capacity";
  } catch (const u2r2::ProtocolError & error) {
    EXPECT_EQ("capacity_exceeded", error.code());
    EXPECT_FALSE(error.terminal());
  }
  EXPECT_EQ(0U, mutations);
}
}  // namespace
