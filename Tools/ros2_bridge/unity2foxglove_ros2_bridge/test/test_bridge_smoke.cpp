// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/ros2_bridge
// Purpose: Protocol logic tests for the Unity2Foxglove ROS 2 bridge sidecar.

#ifdef _WIN32
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <winsock2.h>
#include <ws2tcpip.h>
#endif

#include <gtest/gtest.h>

#include <array>
#include <exception>
#include <limits>
#include <thread>

// Include the production translation unit directly to exercise internal parser helpers.
#define UNITY2FOXGLOVE_ROS2_BRIDGE_TESTING
#include "../src/unity2foxglove_ros2_bridge.cpp"

namespace
{
struct WireQosContract
{
  std::string profile = "default";
  std::string reliability = "reliable";
  std::string durability = "volatile";
  std::string history = "keep_last";
  int depth = 10;
};

RawFrame MakePublishRawFrame(
  const std::string & topic = "/unity/tf",
  const std::string & schema_name = "foxglove_msgs/msg/FrameTransform",
  const WireQosContract & qos = WireQosContract{})
{
  RawFrame raw;
  raw.header = {
    {"op", "publish"},
    {"topic", topic},
    {"schemaName", schema_name},
    {"encoding", "cdr"},
    {"logTimeNs", 1234},
    {"sequence", 7},
    {"qos", {
      {"profile", qos.profile},
      {"reliability", qos.reliability},
      {"durability", qos.durability},
      {"history", qos.history},
      {"depth", qos.depth}
    }}
  };
  raw.payload = {0x00, 0x01, 0x00, 0x00, 0x10, 0x20};
  return raw;
}

RawFrame MakePreparePublisherRawFrame(
  const std::string & request_id = "phase184-prepare-1",
  const std::string & topic = "/unity/tf",
  const std::string & schema_name = "foxglove_msgs/msg/FrameTransform",
  const WireQosContract & qos = WireQosContract{})
{
  auto raw = MakePublishRawFrame(topic, schema_name, qos);
  raw.header["op"] = "prepare_publisher";
  raw.header["requestId"] = request_id;
  raw.header["protocolVersion"] = 1;
  raw.header.erase("logTimeNs");
  raw.header.erase("sequence");
  raw.payload.clear();
  return raw;
}

rmw_qos_profile_t MakeRmwQosProfile(const WireQosContract & contract)
{
  const auto frame = parse_publish_frame(
    MakePublishRawFrame("/unity/qos", "foxglove_msgs/msg/FrameTransform", contract));
  return make_qos(frame).get_rmw_qos_profile();
}

void ExpectPublisherContractConflictRejectedWithoutMutation(
  const BridgeFrame & registered,
  const BridgeFrame & conflicting)
{
  PublisherContractRegistry registry;

  EXPECT_EQ(
    PublisherContractDisposition::CreatePublisher,
    registry.register_or_validate(registered));
  EXPECT_THROW(registry.register_or_validate(conflicting), std::runtime_error);
  EXPECT_THROW(registry.register_or_validate(conflicting), std::runtime_error);
  EXPECT_EQ(
    PublisherContractDisposition::ReusePublisher,
    registry.register_or_validate(registered));
}

std::array<SocketHandle, 2> MakeConnectedSocketPair()
{
  std::array<SocketHandle, 2> sockets = {kInvalidSocket, kInvalidSocket};
#ifdef _WIN32
  static WinsockRuntime winsock;
  ScopedFd listener(::socket(AF_INET, SOCK_STREAM, IPPROTO_TCP));
  if (!listener.valid()) {
    return sockets;
  }
  sockaddr_in address {};
  address.sin_family = AF_INET;
  address.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
  address.sin_port = 0;
  if (::bind(
      listener.get(),
      reinterpret_cast<sockaddr *>(&address),
      static_cast<SocketLength>(sizeof(address))) != 0 ||
    ::listen(listener.get(), 1) != 0)
  {
    return sockets;
  }
  SocketLength address_length = static_cast<SocketLength>(sizeof(address));
  if (::getsockname(
      listener.get(),
      reinterpret_cast<sockaddr *>(&address),
      &address_length) != 0)
  {
    return sockets;
  }
  ScopedFd client(::socket(AF_INET, SOCK_STREAM, IPPROTO_TCP));
  if (!client.valid() ||
    ::connect(
      client.get(),
      reinterpret_cast<sockaddr *>(&address),
      static_cast<SocketLength>(sizeof(address))) != 0)
  {
    return sockets;
  }
  ScopedFd server(::accept(listener.get(), nullptr, nullptr));
  if (!server.valid()) {
    return sockets;
  }
  sockets[0] = client.release();
  sockets[1] = server.release();
#else
  int raw[2] = {-1, -1};
  if (::socketpair(AF_UNIX, SOCK_STREAM, 0, raw) == 0) {
    sockets[0] = raw[0];
    sockets[1] = raw[1];
  }
#endif
  return sockets;
}

int ShutdownSocketWrite(SocketHandle socket)
{
#ifdef _WIN32
  return ::shutdown(socket, SD_SEND);
#else
  return ::shutdown(socket, SHUT_WR);
#endif
}
}  // namespace

TEST(Unity2FoxgloveRos2BridgeProtocol, ValidatesTopicNames)
{
  EXPECT_TRUE(is_valid_ros2_topic_name("/unity/tf"));
  EXPECT_TRUE(is_valid_ros2_topic_name("/unity2foxglove/point_cloud_2"));
  EXPECT_FALSE(is_valid_ros2_topic_name(""));
  EXPECT_FALSE(is_valid_ros2_topic_name("unity/tf"));
  EXPECT_FALSE(is_valid_ros2_topic_name("/unity//tf"));
  EXPECT_FALSE(is_valid_ros2_topic_name("/unity/tf-with-dash"));
  EXPECT_FALSE(is_valid_ros2_topic_name("/1st/sensor"));
  EXPECT_FALSE(is_valid_ros2_topic_name("/unity/2nd_sensor"));
}

TEST(Unity2FoxgloveRos2BridgeProtocol, AcceptsCanonicalRos2MessageTypes)
{
  const std::array<std::string, 3> message_types = {
    "foxglove_msgs/msg/FrameTransform",
    "sensor_msgs/msg/Image",
    "unity2foxglove_foxrun_interfaces_v1/msg/Phase181State48D288ED82F1Envelope"
  };

  for (const auto & message_type : message_types) {
    SCOPED_TRACE("message_type=" + message_type);
    const auto frame = parse_publish_frame(
      MakePublishRawFrame("/unity/message", message_type));
    EXPECT_EQ(message_type, frame.schema_name);
  }
}

TEST(Unity2FoxgloveRos2BridgeProtocol, RejectsNonCanonicalRos2MessageTypes)
{
  const std::array<std::string, 17> invalid_message_types = {
    "",
    "a/msg/Type",
    "foo__bar/msg/Type",
    "sensor_msgs",
    "sensor_msgs/msg",
    "sensor_msgs/msg/",
    "/sensor_msgs/msg/Image",
    "Sensor_msgs/msg/Image",
    "1sensor_msgs/msg/Image",
    "sensor-msgs/msg/Image",
    "sensor_msgs_/msg/Image",
    "sensor_msgs/srv/Image",
    "sensor_msgs/msg/image",
    "sensor_msgs/msg/Image_Name",
    "sensor_msgs/msg/Image/Extra",
    "sensor_msgs//msg/Image",
    "sensor_msgs/msg/État"
  };

  for (const auto & message_type : invalid_message_types) {
    SCOPED_TRACE("message_type=" + message_type);
    EXPECT_THROW(
      parse_publish_frame(MakePublishRawFrame("/unity/message", message_type)),
      std::runtime_error);
  }
}

TEST(Unity2FoxgloveRos2BridgeProtocol, ParsesDefaultQosContract)
{
  const WireQosContract expected;
  const auto frame = parse_publish_frame(MakePublishRawFrame());

  EXPECT_EQ(expected.profile, frame.profile);
  EXPECT_EQ(expected.reliability, frame.reliability);
  EXPECT_EQ(expected.durability, frame.durability);
  EXPECT_EQ(expected.history, frame.history);
  EXPECT_EQ(expected.depth, frame.depth);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, ParsesSensorDataQosContract)
{
  const WireQosContract expected{
    "sensor_data", "best_effort", "volatile", "keep_last", 5};
  const auto frame = parse_publish_frame(
    MakePublishRawFrame("/unity/qos", "foxglove_msgs/msg/FrameTransform", expected));

  EXPECT_EQ(expected.profile, frame.profile);
  EXPECT_EQ(expected.reliability, frame.reliability);
  EXPECT_EQ(expected.durability, frame.durability);
  EXPECT_EQ(expected.history, frame.history);
  EXPECT_EQ(expected.depth, frame.depth);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, ParsesSystemDefaultQosContract)
{
  const WireQosContract expected{
    "system_default", "system_default", "system_default", "system_default", 0};
  const auto frame = parse_publish_frame(
    MakePublishRawFrame("/unity/qos", "foxglove_msgs/msg/FrameTransform", expected));

  EXPECT_EQ(expected.profile, frame.profile);
  EXPECT_EQ(expected.reliability, frame.reliability);
  EXPECT_EQ(expected.durability, frame.durability);
  EXPECT_EQ(expected.history, frame.history);
  EXPECT_EQ(expected.depth, frame.depth);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, ParsesKeepAllQosContractWithZeroDepth)
{
  const WireQosContract expected{
    "default", "reliable", "transient_local", "keep_all", 0};
  const auto frame = parse_publish_frame(
    MakePublishRawFrame("/unity/qos", "foxglove_msgs/msg/FrameTransform", expected));

  EXPECT_EQ(expected.profile, frame.profile);
  EXPECT_EQ(expected.reliability, frame.reliability);
  EXPECT_EQ(expected.durability, frame.durability);
  EXPECT_EQ(expected.history, frame.history);
  EXPECT_EQ(expected.depth, frame.depth);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, ParsesKeepLastQosContractWithNonDefaultDepth)
{
  const WireQosContract expected{
    "default", "best_effort", "transient_local", "keep_last", 37};
  const auto frame = parse_publish_frame(
    MakePublishRawFrame("/unity/qos", "foxglove_msgs/msg/FrameTransform", expected));

  EXPECT_EQ(expected.profile, frame.profile);
  EXPECT_EQ(expected.reliability, frame.reliability);
  EXPECT_EQ(expected.durability, frame.durability);
  EXPECT_EQ(expected.history, frame.history);
  EXPECT_EQ(expected.depth, frame.depth);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, RejectsInvalidQosProfile)
{
  auto raw = MakePublishRawFrame();
  raw.header["qos"]["profile"] = "unknown_profile";

  EXPECT_THROW(parse_publish_frame(raw), std::runtime_error);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, RejectsInvalidQosReliability)
{
  auto raw = MakePublishRawFrame();
  raw.header["qos"]["reliability"] = "sometimes_reliable";

  EXPECT_THROW(parse_publish_frame(raw), std::runtime_error);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, RejectsInvalidQosDurability)
{
  auto raw = MakePublishRawFrame();
  raw.header["qos"]["durability"] = "persistent";

  EXPECT_THROW(parse_publish_frame(raw), std::runtime_error);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, RejectsInvalidQosHistory)
{
  auto raw = MakePublishRawFrame();
  raw.header["qos"]["history"] = "keep_some";

  EXPECT_THROW(parse_publish_frame(raw), std::runtime_error);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, RejectsNonIntegerQosDepthTypes)
{
  const std::array<nlohmann::json, 3> invalid_depths = {
    nlohmann::json("10"),
    nlohmann::json(10.0),
    nlohmann::json(10.5)
  };
  for (const auto & invalid_depth : invalid_depths) {
    SCOPED_TRACE("depth=" + invalid_depth.dump());
    auto raw = MakePublishRawFrame();
    raw.header["qos"]["depth"] = invalid_depth;
    EXPECT_THROW(parse_publish_frame(raw), std::runtime_error);
  }
}

TEST(Unity2FoxgloveRos2BridgeProtocol, RejectsOutOfRangeQosDepth)
{
  const auto above_int_max =
    static_cast<int64_t>(std::numeric_limits<int>::max()) + 1;
  const auto below_int_min =
    static_cast<int64_t>(std::numeric_limits<int>::min()) - 1;
  const std::array<nlohmann::json, 2> invalid_depths = {
    nlohmann::json(above_int_max),
    nlohmann::json(below_int_min)
  };
  for (const auto & invalid_depth : invalid_depths) {
    SCOPED_TRACE("depth=" + invalid_depth.dump());
    auto raw = MakePublishRawFrame();
    raw.header["qos"]["depth"] = invalid_depth;
    EXPECT_THROW(parse_publish_frame(raw), std::runtime_error);
  }
}

TEST(Unity2FoxgloveRos2BridgeProtocol, RejectsNonPositiveKeepLastDepth)
{
  auto zero_depth = MakePublishRawFrame();
  zero_depth.header["qos"]["depth"] = 0;
  EXPECT_THROW(parse_publish_frame(zero_depth), std::runtime_error);

  auto negative_depth = MakePublishRawFrame();
  negative_depth.header["qos"]["depth"] = -1;
  EXPECT_THROW(parse_publish_frame(negative_depth), std::runtime_error);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, RejectsNonzeroDepthForNonKeepLastHistory)
{
  const std::array<std::string, 2> histories = {"keep_all", "system_default"};
  for (const auto & history : histories) {
    SCOPED_TRACE("history=" + history);
    auto raw = MakePublishRawFrame();
    raw.header["qos"]["history"] = history;
    raw.header["qos"]["depth"] = 1;
    EXPECT_THROW(parse_publish_frame(raw), std::runtime_error);
  }
}

TEST(Unity2FoxgloveRos2BridgeProtocol, RejectsMissingRequiredQosFields)
{
  const std::array<std::string, 5> required_fields = {
    "profile", "reliability", "durability", "history", "depth"};
  for (const auto & field : required_fields) {
    SCOPED_TRACE("field=" + field);
    auto raw = MakePublishRawFrame();
    raw.header["qos"].erase(field);
    EXPECT_THROW(parse_publish_frame(raw), std::runtime_error);
  }
}

TEST(Unity2FoxgloveRos2BridgeProtocol, DefaultsMissingQosObjectForLegacyPublishers)
{
  const WireQosContract expected;
  auto raw = MakePublishRawFrame();
  raw.header.erase("qos");

  const auto frame = parse_publish_frame(raw);
  EXPECT_EQ(expected.profile, frame.profile);
  EXPECT_EQ(expected.reliability, frame.reliability);
  EXPECT_EQ(expected.durability, frame.durability);
  EXPECT_EQ(expected.history, frame.history);
  EXPECT_EQ(expected.depth, frame.depth);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, DefaultsNullQosObjectForLegacyPublishers)
{
  const WireQosContract expected;
  auto raw = MakePublishRawFrame();
  raw.header["qos"] = nullptr;

  const auto frame = parse_publish_frame(raw);
  EXPECT_EQ(expected.profile, frame.profile);
  EXPECT_EQ(expected.reliability, frame.reliability);
  EXPECT_EQ(expected.durability, frame.durability);
  EXPECT_EQ(expected.history, frame.history);
  EXPECT_EQ(expected.depth, frame.depth);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, RejectsNonObjectQosValue)
{
  auto raw = MakePublishRawFrame();
  raw.header["qos"] = "default";

  EXPECT_THROW(parse_publish_frame(raw), std::runtime_error);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, ForwardsEncapsulatedPayloadByView)
{
  auto frame = parse_publish_frame(MakePublishRawFrame());
  std::vector<uint8_t> scratch;
  const auto payload = payload_for_publish(frame, PayloadFormat::CdrWithEncapsulation, scratch);

  EXPECT_TRUE(scratch.empty());
  ASSERT_EQ(frame.payload.size(), payload.size);
  EXPECT_EQ(frame.payload.data(), payload.data);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, PrependsEncapsulationForBodyOnlyPayload)
{
  auto frame = parse_publish_frame(MakePublishRawFrame());
  frame.payload = {0x10, 0x20, 0x30};
  std::vector<uint8_t> scratch;
  const auto payload = payload_for_publish(frame, PayloadFormat::CdrBodyOnly, scratch);

  const std::vector<uint8_t> expected = {0x00, 0x01, 0x00, 0x00, 0x10, 0x20, 0x30};
  ASSERT_EQ(expected.size(), payload.size);
  EXPECT_EQ(expected, scratch);
  EXPECT_EQ(scratch.data(), payload.data);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, RejectsEncapsulatedBodyOnlyPayload)
{
  auto frame = parse_publish_frame(MakePublishRawFrame());
  std::vector<uint8_t> scratch;

  EXPECT_THROW(payload_for_publish(frame, PayloadFormat::CdrBodyOnly, scratch), std::runtime_error);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, ParsesCorrelatedZeroPayloadPreparePublisherContract)
{
  const WireQosContract qos{
    "sensor_data", "best_effort", "transient_local", "keep_last", 37};
  const auto request = parse_prepare_publisher_frame(
    MakePreparePublisherRawFrame(
      "phase184-prepare-exact",
      "/unity/custom",
      "unity2foxglove_foxrun_interfaces_v1/msg/Phase181State48D288ED82F1Envelope",
      qos));

  EXPECT_EQ("phase184-prepare-exact", request.request_id);
  EXPECT_EQ(1, request.protocol_version);
  EXPECT_EQ("/unity/custom", request.frame.topic);
  EXPECT_EQ(
    "unity2foxglove_foxrun_interfaces_v1/msg/Phase181State48D288ED82F1Envelope",
    request.frame.schema_name);
  EXPECT_EQ("cdr", request.frame.encoding);
  EXPECT_EQ(qos.profile, request.frame.profile);
  EXPECT_EQ(qos.reliability, request.frame.reliability);
  EXPECT_EQ(qos.durability, request.frame.durability);
  EXPECT_EQ(qos.history, request.frame.history);
  EXPECT_EQ(qos.depth, request.frame.depth);
  EXPECT_TRUE(request.frame.payload.empty());
}

TEST(Unity2FoxgloveRos2BridgeProtocol, RejectsPreparePublisherPayloadAndProtocolMismatch)
{
  auto payload = MakePreparePublisherRawFrame();
  payload.payload = {0x00};
  EXPECT_THROW(parse_prepare_publisher_frame(payload), std::runtime_error);

  auto unsupported = MakePreparePublisherRawFrame();
  unsupported.header["protocolVersion"] = 2;
  EXPECT_THROW(parse_prepare_publisher_frame(unsupported), std::runtime_error);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, PreparePublisherProtocolVersionRequiresJsonInteger)
{
  const std::array<nlohmann::json, 3> invalid_versions = {
    nlohmann::json(1.0),
    nlohmann::json("1"),
    nlohmann::json(true)
  };

  for (const auto & invalid_version : invalid_versions) {
    SCOPED_TRACE("protocolVersion=" + invalid_version.dump());
    auto raw = MakePreparePublisherRawFrame();
    raw.header["protocolVersion"] = invalid_version;
    EXPECT_THROW(parse_prepare_publisher_frame(raw), std::runtime_error);
  }
}

TEST(Unity2FoxgloveRos2BridgeProtocol, PreparePublisherProtocolVersionRejectsNarrowingBoundaries)
{
  const std::array<nlohmann::json, 6> unsupported_versions = {
    nlohmann::json(-1),
    nlohmann::json(0),
    nlohmann::json(2),
    nlohmann::json(std::numeric_limits<int32_t>::max()),
    nlohmann::json(static_cast<uint64_t>(std::numeric_limits<uint32_t>::max()) + 2U),
    nlohmann::json(std::numeric_limits<uint64_t>::max())
  };

  for (const auto & unsupported_version : unsupported_versions) {
    SCOPED_TRACE("protocolVersion=" + unsupported_version.dump());
    auto raw = MakePreparePublisherRawFrame();
    raw.header["protocolVersion"] = unsupported_version;
    EXPECT_THROW(parse_prepare_publisher_frame(raw), std::runtime_error);
  }
}

TEST(Unity2FoxgloveRos2BridgeProtocol, PreparePublisherRequiresExplicitCompleteQos)
{
  auto missing_qos = MakePreparePublisherRawFrame();
  missing_qos.header.erase("qos");
  EXPECT_THROW(parse_prepare_publisher_frame(missing_qos), std::runtime_error);

  auto partial_qos = MakePreparePublisherRawFrame();
  partial_qos.header["qos"].erase("history");
  EXPECT_THROW(parse_prepare_publisher_frame(partial_qos), std::runtime_error);
}

TEST(
  Unity2FoxgloveRos2BridgeProtocol,
  PreparePublisherIsIdempotentAndLegacyPublishReusesPreparedPublisher)
{
  size_t create_count = 0;
  size_t publish_count = 0;
  GenericPublisherFactory factory =
    [&](const std::string &, const std::string &, const rclcpp::QoS &) {
      ++create_count;
      return [&](const rclcpp::SerializedMessage &) {
          ++publish_count;
        };
    };
  BridgeNode bridge(PayloadFormat::CdrWithEncapsulation, std::move(factory));
  const auto frame = parse_prepare_publisher_frame(
    MakePreparePublisherRawFrame()).frame;

  EXPECT_EQ(PublisherContractDisposition::CreatePublisher, bridge.prepare(frame));
  EXPECT_EQ(PublisherContractDisposition::ReusePublisher, bridge.prepare(frame));
  EXPECT_EQ(1U, create_count);
  EXPECT_EQ(0U, publish_count);

  bridge.publish(parse_publish_frame(MakePublishRawFrame()));
  EXPECT_EQ(1U, create_count);
  EXPECT_EQ(1U, publish_count);
}

TEST(
  Unity2FoxgloveRos2BridgeProtocol,
  PreparePublisherFailureReturnsCorrelatedErrorAndKeepsSessionUsable)
{
  const std::string unavailable_type = "missing_phase184_interfaces/msg/MissingEnvelope";
  const std::string available_type = "std_msgs/msg/String";
  std::vector<std::string> creation_attempts;
  size_t publish_count = 0;
  GenericPublisherFactory factory =
    [&](const std::string &, const std::string & message_type, const rclcpp::QoS &) {
      creation_attempts.push_back(message_type);
      if (message_type == unavailable_type) {
        throw std::runtime_error("typesupport unavailable");
      }
      return [&](const rclcpp::SerializedMessage &) {
          ++publish_count;
        };
    };
  BridgeNode bridge(PayloadFormat::CdrWithEncapsulation, std::move(factory));

  const auto rejected = handle_prepare_publisher_frame(
    MakePreparePublisherRawFrame(
      "phase184-prepare-missing",
      "/unity/custom",
      unavailable_type),
    bridge);
  EXPECT_EQ("publisher_ready", rejected.at("op").get<std::string>());
  EXPECT_EQ("phase184-prepare-missing", rejected.at("requestId").get<std::string>());
  EXPECT_EQ(1, rejected.at("protocolVersion").get<int>());
  EXPECT_EQ("error", rejected.at("status").get<std::string>());
  EXPECT_EQ("publisher_unavailable", rejected.at("errorCode").get<std::string>());
  EXPECT_FALSE(rejected.at("message").get<std::string>().empty());

  const auto ready = handle_prepare_publisher_frame(
    MakePreparePublisherRawFrame(
      "phase184-prepare-available",
      "/unity/custom",
      available_type),
    bridge);
  EXPECT_EQ("publisher_ready", ready.at("op").get<std::string>());
  EXPECT_EQ("phase184-prepare-available", ready.at("requestId").get<std::string>());
  EXPECT_EQ(1, ready.at("protocolVersion").get<int>());
  EXPECT_EQ("ok", ready.at("status").get<std::string>());
  EXPECT_FALSE(ready.contains("errorCode"));
  EXPECT_FALSE(ready.contains("message"));

  const std::vector<std::string> expected_attempts = {unavailable_type, available_type};
  EXPECT_EQ(expected_attempts, creation_attempts);
  EXPECT_EQ(0U, publish_count);
}

TEST(
  Unity2FoxgloveRos2BridgeProtocol,
  SameSocketSessionContinuesFromUnavailablePrepareToReadyAndLegacyPublish)
{
  auto context = std::make_shared<rclcpp::Context>();
  context->init(0, nullptr);
  rclcpp::NodeOptions options;
  options.context(context);
  auto node = std::make_shared<rclcpp::Node>(
    "phase184_bridge_session_loop_test",
    options);

  const auto sockets = MakeConnectedSocketPair();
  ASSERT_NE(kInvalidSocket, sockets[0]);
  ASSERT_NE(kInvalidSocket, sockets[1]);
  ScopedFd client_socket(sockets[0]);
  ScopedFd server_socket(sockets[1]);

  const std::string topic = "/phase184/session";
  const std::string unavailable_type = "missing_phase184_interfaces/msg/MissingEnvelope";
  const std::string available_type = "std_msgs/msg/String";
  std::vector<std::string> creation_attempts;
  size_t publish_count = 0;
  GenericPublisherFactory factory =
    [&](const std::string &, const std::string & message_type, const rclcpp::QoS &) {
      creation_attempts.push_back(message_type);
      if (message_type == unavailable_type) {
        throw std::runtime_error("typesupport unavailable");
      }
      return [&](const rclcpp::SerializedMessage &) {
          ++publish_count;
        };
    };
  BridgeNode bridge(PayloadFormat::CdrWithEncapsulation, std::move(factory));

  std::exception_ptr server_failure;
  std::thread server_thread(
    [&]() {
      try {
        process_client(server_socket.get(), bridge, node);
      } catch (...) {
        server_failure = std::current_exception();
      }
    });

  RawFrame unavailable_ack;
  RawFrame ready_ack;
  std::exception_ptr client_failure;
  try {
    const auto unavailable = MakePreparePublisherRawFrame(
      "phase184-session-unavailable",
      topic,
      unavailable_type);
    write_u2r2_frame(client_socket.get(), unavailable.header, unavailable.payload);
    unavailable_ack = read_raw_frame(client_socket.get(), node);

    const auto ready = MakePreparePublisherRawFrame(
      "phase184-session-ready",
      topic,
      available_type);
    write_u2r2_frame(client_socket.get(), ready.header, ready.payload);
    ready_ack = read_raw_frame(client_socket.get(), node);

    const auto publish = MakePublishRawFrame(topic, available_type);
    write_u2r2_frame(client_socket.get(), publish.header, publish.payload);
  } catch (...) {
    client_failure = std::current_exception();
  }

  EXPECT_EQ(0, ShutdownSocketWrite(client_socket.get()));
  server_thread.join();
  context->shutdown("phase184 bridge session loop test complete");

  if (client_failure) {
    try {
      std::rethrow_exception(client_failure);
    } catch (const std::exception & ex) {
      ADD_FAILURE() << "client session failed: " << ex.what();
    }
  }
  if (server_failure) {
    try {
      std::rethrow_exception(server_failure);
    } catch (const std::exception & ex) {
      ADD_FAILURE() << "server session failed: " << ex.what();
    }
  }

  EXPECT_EQ("publisher_ready", unavailable_ack.header.value("op", ""));
  EXPECT_EQ("phase184-session-unavailable", unavailable_ack.header.value("requestId", ""));
  EXPECT_EQ("error", unavailable_ack.header.value("status", ""));
  EXPECT_EQ("publisher_unavailable", unavailable_ack.header.value("errorCode", ""));
  EXPECT_TRUE(unavailable_ack.payload.empty());

  EXPECT_EQ("publisher_ready", ready_ack.header.value("op", ""));
  EXPECT_EQ("phase184-session-ready", ready_ack.header.value("requestId", ""));
  EXPECT_EQ("ok", ready_ack.header.value("status", ""));
  EXPECT_FALSE(ready_ack.header.contains("errorCode"));
  EXPECT_TRUE(ready_ack.payload.empty());

  const std::vector<std::string> expected_attempts = {unavailable_type, available_type};
  EXPECT_EQ(expected_attempts, creation_attempts);
  EXPECT_EQ(1U, publish_count);
}

TEST(
  Unity2FoxgloveRos2BridgeProtocol,
  MalformedHealthFieldTypesReturnAnErrorWithoutEscapingTheClientSession)
{
  auto context = std::make_shared<rclcpp::Context>();
  context->init(0, nullptr);
  rclcpp::NodeOptions options;
  options.context(context);
  auto node = std::make_shared<rclcpp::Node>(
    "phase184_bridge_health_type_test",
    options);

  const auto sockets = MakeConnectedSocketPair();
  ASSERT_NE(kInvalidSocket, sockets[0]);
  ASSERT_NE(kInvalidSocket, sockets[1]);
  ScopedFd client_socket(sockets[0]);
  ScopedFd server_socket(sockets[1]);
  BridgeNode bridge(
    PayloadFormat::CdrWithEncapsulation,
    [](const std::string &, const std::string &, const rclcpp::QoS &) {
      return [](const rclcpp::SerializedMessage &) {};
    });

  std::exception_ptr server_failure;
  std::thread server_thread(
    [&]() {
      try {
        process_client(server_socket.get(), bridge, node);
      } catch (...) {
        server_failure = std::current_exception();
      }
    });

  RawFrame invalid_request_id;
  invalid_request_id.header = {
    {"op", "health_ping"},
    {"requestId", 184},
    {"protocolVersion", 1}
  };
  write_u2r2_frame(client_socket.get(), invalid_request_id.header, {});
  const auto invalid_request_id_response = read_raw_frame(client_socket.get(), node);
  EXPECT_EQ("health_pong", invalid_request_id_response.header.value("op", ""));
  EXPECT_EQ("", invalid_request_id_response.header.value("requestId", ""));
  EXPECT_EQ("error", invalid_request_id_response.header.value("status", ""));
  EXPECT_EQ("malformed_request", invalid_request_id_response.header.value("errorCode", ""));

  RawFrame invalid_protocol;
  invalid_protocol.header = {
    {"op", "health_ping"},
    {"requestId", "phase184-health-type"},
    {"protocolVersion", "one"}
  };
  write_u2r2_frame(client_socket.get(), invalid_protocol.header, {});
  const auto invalid_protocol_response = read_raw_frame(client_socket.get(), node);
  EXPECT_EQ("health_pong", invalid_protocol_response.header.value("op", ""));
  EXPECT_EQ("phase184-health-type", invalid_protocol_response.header.value("requestId", ""));
  EXPECT_EQ("error", invalid_protocol_response.header.value("status", ""));
  EXPECT_EQ("malformed_request", invalid_protocol_response.header.value("errorCode", ""));

  nlohmann::json valid_health = {
    {"op", "health_ping"},
    {"requestId", "phase184-health-recovery"},
    {"protocolVersion", 1}
  };
  write_u2r2_frame(client_socket.get(), valid_health, {});
  const auto recovered = read_raw_frame(client_socket.get(), node);
  EXPECT_EQ("health_pong", recovered.header.value("op", ""));
  EXPECT_EQ("phase184-health-recovery", recovered.header.value("requestId", ""));
  EXPECT_EQ("ok", recovered.header.value("status", ""));

  EXPECT_EQ(0, ShutdownSocketWrite(client_socket.get()));
  server_thread.join();
  context->shutdown("phase184 bridge health type test complete");
  if (server_failure) {
    try {
      std::rethrow_exception(server_failure);
    } catch (const std::exception & ex) {
      ADD_FAILURE() << "server session failed: " << ex.what();
    }
  }
}

TEST(
  Unity2FoxgloveRos2BridgeProtocol,
  LegacyPublishContractFailureDoesNotDropHealthyPublishersInTheSameSession)
{
  auto context = std::make_shared<rclcpp::Context>();
  context->init(0, nullptr);
  rclcpp::NodeOptions options;
  options.context(context);
  auto node = std::make_shared<rclcpp::Node>(
    "phase184_bridge_publish_isolation_test",
    options);

  const auto sockets = MakeConnectedSocketPair();
  ASSERT_NE(kInvalidSocket, sockets[0]);
  ASSERT_NE(kInvalidSocket, sockets[1]);
  ScopedFd client_socket(sockets[0]);
  ScopedFd server_socket(sockets[1]);

  const std::string unavailable_type = "missing_phase184_interfaces/msg/MissingEnvelope";
  const std::string available_type = "std_msgs/msg/String";
  size_t publish_count = 0;
  GenericPublisherFactory factory =
    [&](const std::string &, const std::string & message_type, const rclcpp::QoS &) {
      if (message_type == unavailable_type) {
        throw std::runtime_error("typesupport unavailable");
      }
      return [&](const rclcpp::SerializedMessage &) {
          ++publish_count;
        };
    };
  BridgeNode bridge(PayloadFormat::CdrWithEncapsulation, std::move(factory));

  std::exception_ptr server_failure;
  std::thread server_thread(
    [&]() {
      try {
        process_client(server_socket.get(), bridge, node);
      } catch (...) {
        server_failure = std::current_exception();
      }
    });

  const auto unavailable = MakePublishRawFrame(
    "/phase184/unavailable",
    unavailable_type);
  write_u2r2_frame(client_socket.get(), unavailable.header, unavailable.payload);
  const auto available = MakePublishRawFrame(
    "/phase184/healthy",
    available_type);
  write_u2r2_frame(client_socket.get(), available.header, available.payload);
  EXPECT_EQ(0, ShutdownSocketWrite(client_socket.get()));

  server_thread.join();
  context->shutdown("phase184 bridge publish isolation test complete");

  if (server_failure) {
    try {
      std::rethrow_exception(server_failure);
    } catch (const std::exception & ex) {
      ADD_FAILURE() << "server session failed: " << ex.what();
    }
  }
  EXPECT_EQ(1U, publish_count);
}

TEST(
  Unity2FoxgloveRos2BridgeProtocol,
  PreparePublisherConflictReturnsErrorWithoutReplacingPreparedPublisher)
{
  size_t create_count = 0;
  GenericPublisherFactory factory =
    [&](const std::string &, const std::string &, const rclcpp::QoS &) {
      ++create_count;
      return [](const rclcpp::SerializedMessage &) {};
    };
  BridgeNode bridge(PayloadFormat::CdrWithEncapsulation, std::move(factory));

  const auto ready = handle_prepare_publisher_frame(
    MakePreparePublisherRawFrame("phase184-prepare-first"),
    bridge);
  EXPECT_EQ("ok", ready.at("status").get<std::string>());

  const auto conflict = handle_prepare_publisher_frame(
    MakePreparePublisherRawFrame(
      "phase184-prepare-conflict",
      "/unity/tf",
      "foxglove_msgs/msg/FrameTransform",
      WireQosContract{"sensor_data", "best_effort", "volatile", "keep_last", 5}),
    bridge);
  EXPECT_EQ("error", conflict.at("status").get<std::string>());
  EXPECT_EQ("publisher_contract_conflict", conflict.at("errorCode").get<std::string>());
  EXPECT_EQ(1U, create_count);

  const auto original = handle_prepare_publisher_frame(
    MakePreparePublisherRawFrame("phase184-prepare-original"),
    bridge);
  EXPECT_EQ("ok", original.at("status").get<std::string>());
  EXPECT_EQ(1U, create_count);
}

TEST(
  Unity2FoxgloveRos2BridgeProtocol,
  GenericPublisherFactoryReceivesExactCustomTypeAndPublishesExactSerializedBytes)
{
  const std::string custom_type =
    "unity2foxglove_foxrun_interfaces_v1/msg/Phase181State48D288ED82F1Envelope";
  const std::vector<uint8_t> first_payload =
    {0x00, 0x01, 0x00, 0x00, 0x12, 0x34, 0x56, 0x78};
  const std::vector<uint8_t> second_payload =
    {0x00, 0x01, 0x00, 0x00, 0xab, 0xcd};
  std::string created_topic;
  std::string created_type;
  std::vector<std::vector<uint8_t>> published_payloads;
  size_t create_count = 0;

  GenericPublisherFactory factory =
    [&](const std::string & topic, const std::string & message_type, const rclcpp::QoS &) {
      ++create_count;
      created_topic = topic;
      created_type = message_type;
      return [&](const rclcpp::SerializedMessage & message) {
          const auto & serialized = message.get_rcl_serialized_message();
          published_payloads.emplace_back(
            serialized.buffer,
            serialized.buffer + serialized.buffer_length);
        };
    };
  BridgeNode bridge(PayloadFormat::CdrWithEncapsulation, std::move(factory));

  auto first_raw = MakePublishRawFrame("/unity/custom", custom_type);
  first_raw.payload = first_payload;
  bridge.publish(parse_publish_frame(first_raw));

  auto second_raw = MakePublishRawFrame("/unity/custom", custom_type);
  second_raw.header["logTimeNs"] = 5678;
  second_raw.header["sequence"] = 8;
  second_raw.payload = second_payload;
  bridge.publish(parse_publish_frame(second_raw));

  EXPECT_EQ(1U, create_count);
  EXPECT_EQ("/unity/custom", created_topic);
  EXPECT_EQ(custom_type, created_type);
  ASSERT_EQ(2U, published_payloads.size());
  EXPECT_EQ(first_payload, published_payloads[0]);
  EXPECT_EQ(second_payload, published_payloads[1]);
}

TEST(
  Unity2FoxgloveRos2BridgeProtocol,
  UnavailableTypesupportFailsClosedWithoutPollutingPublisherRegistry)
{
  const std::string unavailable_type = "missing_phase184_interfaces/msg/MissingEnvelope";
  const std::string available_type =
    "unity2foxglove_foxrun_interfaces_v1/msg/Phase181State48D288ED82F1Envelope";
  std::vector<std::string> creation_attempts;
  size_t publish_count = 0;

  GenericPublisherFactory factory =
    [&](const std::string &, const std::string & message_type, const rclcpp::QoS &) {
      creation_attempts.push_back(message_type);
      if (message_type == unavailable_type) {
        throw std::runtime_error("typesupport unavailable");
      }
      return [&](const rclcpp::SerializedMessage &) {
          ++publish_count;
        };
    };
  BridgeNode bridge(PayloadFormat::CdrWithEncapsulation, std::move(factory));

  EXPECT_THROW(
    bridge.publish(
      parse_publish_frame(
        MakePublishRawFrame("/unity/custom", unavailable_type))),
    std::runtime_error);
  EXPECT_NO_THROW(
    bridge.publish(
      parse_publish_frame(
        MakePublishRawFrame("/unity/custom", available_type))));
  EXPECT_NO_THROW(
    bridge.publish(
      parse_publish_frame(
        MakePublishRawFrame("/unity/custom", available_type))));
  EXPECT_THROW(
    bridge.publish(
      parse_publish_frame(
        MakePublishRawFrame(
          "/unity/custom",
          "unity2foxglove_foxrun_interfaces_v1/msg/OtherEnvelope"))),
    std::runtime_error);

  const std::vector<std::string> expected_attempts = {unavailable_type, available_type};
  EXPECT_EQ(expected_attempts, creation_attempts);
  EXPECT_EQ(2U, publish_count);
}

TEST(
  Unity2FoxgloveRos2BridgeProtocol,
  ProductionGenericPublisherLookupRejectsUnavailableTypesupportWithoutCaching)
{
  auto context = std::make_shared<rclcpp::Context>();
  context->init(0, nullptr);
  {
    rclcpp::NodeOptions options;
    options.context(context);
    auto node = std::make_shared<rclcpp::Node>(
      "phase184_bridge_typesupport_test",
      options);
    BridgeNode bridge(node, PayloadFormat::CdrWithEncapsulation);

    EXPECT_THROW(
      bridge.publish(
        parse_publish_frame(
          MakePublishRawFrame(
            "/phase184/typesupport",
            "missing_phase184_interfaces/msg/MissingEnvelope"))),
      std::runtime_error);

    auto available = MakePublishRawFrame(
      "/phase184/typesupport",
      "std_msgs/msg/String");
    available.payload = {
      0x00, 0x01, 0x00, 0x00,
      0x06, 0x00, 0x00, 0x00,
      'h', 'e', 'l', 'l', 'o', 0x00
    };
    EXPECT_NO_THROW(bridge.publish(parse_publish_frame(available)));
  }
  context->shutdown("phase184 bridge typesupport test complete");
}

TEST(Unity2FoxgloveRos2BridgeProtocol, PublisherReuseSignatureCapturesSchemaAndEveryQosField)
{
  const auto baseline = parse_publish_frame(MakePublishRawFrame());
  const auto baseline_signature = qos_signature(baseline);

  auto changed = baseline;
  changed.schema_name = "foxglove_msgs/msg/CompressedImage";
  EXPECT_NE(baseline_signature, qos_signature(changed));

  changed = baseline;
  changed.profile = "sensor_data";
  EXPECT_NE(baseline_signature, qos_signature(changed));

  changed = baseline;
  changed.reliability = "best_effort";
  EXPECT_NE(baseline_signature, qos_signature(changed));

  changed = baseline;
  changed.durability = "transient_local";
  EXPECT_NE(baseline_signature, qos_signature(changed));

  const auto keep_all = parse_publish_frame(
    MakePublishRawFrame(
      "/unity/tf",
      "foxglove_msgs/msg/FrameTransform",
      WireQosContract{"default", "reliable", "volatile", "keep_all", 0}));
  const auto system_default_history = parse_publish_frame(
    MakePublishRawFrame(
      "/unity/tf",
      "foxglove_msgs/msg/FrameTransform",
      WireQosContract{"default", "reliable", "volatile", "system_default", 0}));
  EXPECT_EQ(keep_all.topic, system_default_history.topic);
  EXPECT_EQ(keep_all.schema_name, system_default_history.schema_name);
  EXPECT_NE(qos_signature(keep_all), qos_signature(system_default_history));

  changed = baseline;
  changed.depth = 37;
  EXPECT_NE(baseline_signature, qos_signature(changed));
}

TEST(Unity2FoxgloveRos2BridgeProtocol, PublisherContractRegistryReusesIdenticalContract)
{
  PublisherContractRegistry registry;
  const auto first = parse_publish_frame(MakePublishRawFrame());
  auto repeated_raw = MakePublishRawFrame();
  repeated_raw.header["logTimeNs"] = 5678;
  repeated_raw.header["sequence"] = 8;
  repeated_raw.payload = {0x00, 0x01, 0x00, 0x00, 0x30, 0x40};
  const auto repeated = parse_publish_frame(repeated_raw);

  EXPECT_EQ(
    PublisherContractDisposition::CreatePublisher,
    registry.register_or_validate(first));
  EXPECT_EQ(
    PublisherContractDisposition::ReusePublisher,
    registry.register_or_validate(repeated));
}

TEST(Unity2FoxgloveRos2BridgeProtocol, PublisherContractRegistryKeepsTopicsIndependent)
{
  PublisherContractRegistry registry;
  const auto topic_a = parse_publish_frame(MakePublishRawFrame("/unity/topic_a"));
  const auto topic_b = parse_publish_frame(
    MakePublishRawFrame(
      "/unity/topic_b",
      "foxglove_msgs/msg/FrameTransform",
      WireQosContract{"sensor_data", "best_effort", "volatile", "keep_last", 5}));

  EXPECT_EQ(
    PublisherContractDisposition::CreatePublisher,
    registry.register_or_validate(topic_a));
  EXPECT_EQ(
    PublisherContractDisposition::CreatePublisher,
    registry.register_or_validate(topic_b));
  EXPECT_EQ(
    PublisherContractDisposition::ReusePublisher,
    registry.register_or_validate(topic_a));
  EXPECT_EQ(
    PublisherContractDisposition::ReusePublisher,
    registry.register_or_validate(topic_b));
}

TEST(
  Unity2FoxgloveRos2BridgeProtocol,
  PublisherContractRegistryRejectsSchemaAndEveryQosConflictWithoutMutation)
{
  const auto baseline = parse_publish_frame(MakePublishRawFrame());

  auto changed_schema = baseline;
  changed_schema.schema_name = "foxglove_msgs/msg/CompressedImage";
  ExpectPublisherContractConflictRejectedWithoutMutation(baseline, changed_schema);

  const auto changed_profile = parse_publish_frame(
    MakePublishRawFrame(
      "/unity/tf",
      "foxglove_msgs/msg/FrameTransform",
      WireQosContract{"sensor_data", "reliable", "volatile", "keep_last", 10}));
  ExpectPublisherContractConflictRejectedWithoutMutation(baseline, changed_profile);

  const auto changed_reliability = parse_publish_frame(
    MakePublishRawFrame(
      "/unity/tf",
      "foxglove_msgs/msg/FrameTransform",
      WireQosContract{"default", "best_effort", "volatile", "keep_last", 10}));
  ExpectPublisherContractConflictRejectedWithoutMutation(baseline, changed_reliability);

  const auto changed_durability = parse_publish_frame(
    MakePublishRawFrame(
      "/unity/tf",
      "foxglove_msgs/msg/FrameTransform",
      WireQosContract{"default", "reliable", "transient_local", "keep_last", 10}));
  ExpectPublisherContractConflictRejectedWithoutMutation(baseline, changed_durability);

  const auto keep_all = parse_publish_frame(
    MakePublishRawFrame(
      "/unity/tf",
      "foxglove_msgs/msg/FrameTransform",
      WireQosContract{"default", "reliable", "volatile", "keep_all", 0}));
  const auto system_default_history = parse_publish_frame(
    MakePublishRawFrame(
      "/unity/tf",
      "foxglove_msgs/msg/FrameTransform",
      WireQosContract{"default", "reliable", "volatile", "system_default", 0}));
  ExpectPublisherContractConflictRejectedWithoutMutation(keep_all, system_default_history);

  const auto changed_depth = parse_publish_frame(
    MakePublishRawFrame(
      "/unity/tf",
      "foxglove_msgs/msg/FrameTransform",
      WireQosContract{"default", "reliable", "volatile", "keep_last", 37}));
  ExpectPublisherContractConflictRejectedWithoutMutation(baseline, changed_depth);
}

TEST(
  Unity2FoxgloveRos2BridgeProtocol,
  IndependentBridgeNodeSessionsAcceptReplacementQosForTheSameTopic)
{
  const auto first = parse_publish_frame(MakePublishRawFrame());
  const auto replacement = parse_publish_frame(
    MakePublishRawFrame(
      "/unity/tf",
      "foxglove_msgs/msg/FrameTransform",
      WireQosContract{"sensor_data", "best_effort", "volatile", "keep_last", 5}));
  size_t first_create_count = 0;
  size_t first_publish_count = 0;
  size_t replacement_create_count = 0;
  size_t replacement_publish_count = 0;

  {
    GenericPublisherFactory factory =
      [&](const std::string &, const std::string &, const rclcpp::QoS &) {
        ++first_create_count;
        return [&](const rclcpp::SerializedMessage &) {
            ++first_publish_count;
          };
      };
    BridgeNode first_client(PayloadFormat::CdrWithEncapsulation, std::move(factory));
    EXPECT_NO_THROW(first_client.publish(first));
    EXPECT_THROW(
      first_client.publish(replacement),
      std::runtime_error);
  }

  {
    GenericPublisherFactory factory =
      [&](const std::string &, const std::string &, const rclcpp::QoS &) {
        ++replacement_create_count;
        return [&](const rclcpp::SerializedMessage &) {
            ++replacement_publish_count;
          };
      };
    BridgeNode replacement_client(PayloadFormat::CdrWithEncapsulation, std::move(factory));
    EXPECT_NO_THROW(replacement_client.publish(replacement));
  }

  EXPECT_EQ(1U, first_create_count);
  EXPECT_EQ(1U, first_publish_count);
  EXPECT_EQ(1U, replacement_create_count);
  EXPECT_EQ(1U, replacement_publish_count);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, MakesCanonicalDefaultQos)
{
  const auto qos = MakeRmwQosProfile(WireQosContract{});

  EXPECT_EQ(RMW_QOS_POLICY_RELIABILITY_RELIABLE, qos.reliability);
  EXPECT_EQ(RMW_QOS_POLICY_DURABILITY_VOLATILE, qos.durability);
  EXPECT_EQ(RMW_QOS_POLICY_HISTORY_KEEP_LAST, qos.history);
  EXPECT_EQ(10U, qos.depth);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, MakesSensorDataQos)
{
  const auto qos = MakeRmwQosProfile(
    WireQosContract{"sensor_data", "best_effort", "volatile", "keep_last", 5});

  EXPECT_EQ(RMW_QOS_POLICY_RELIABILITY_BEST_EFFORT, qos.reliability);
  EXPECT_EQ(RMW_QOS_POLICY_DURABILITY_VOLATILE, qos.durability);
  EXPECT_EQ(RMW_QOS_POLICY_HISTORY_KEEP_LAST, qos.history);
  EXPECT_EQ(5U, qos.depth);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, MakesSystemDefaultQosWithoutDowngrade)
{
  const auto qos = MakeRmwQosProfile(
    WireQosContract{
      "system_default", "system_default", "system_default", "system_default", 0});

  EXPECT_EQ(RMW_QOS_POLICY_RELIABILITY_SYSTEM_DEFAULT, qos.reliability);
  EXPECT_EQ(RMW_QOS_POLICY_DURABILITY_SYSTEM_DEFAULT, qos.durability);
  EXPECT_EQ(RMW_QOS_POLICY_HISTORY_SYSTEM_DEFAULT, qos.history);
  EXPECT_EQ(0U, qos.depth);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, MakesDefaultProfileWithSystemDefaultOverrides)
{
  const auto qos = MakeRmwQosProfile(
    WireQosContract{
      "default", "system_default", "system_default", "system_default", 0});

  EXPECT_EQ(RMW_QOS_POLICY_RELIABILITY_SYSTEM_DEFAULT, qos.reliability);
  EXPECT_EQ(RMW_QOS_POLICY_DURABILITY_SYSTEM_DEFAULT, qos.durability);
  EXPECT_EQ(RMW_QOS_POLICY_HISTORY_SYSTEM_DEFAULT, qos.history);
  EXPECT_EQ(0U, qos.depth);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, MakesSystemDefaultProfileWithExplicitOverrides)
{
  const auto qos = MakeRmwQosProfile(
    WireQosContract{
      "system_default", "reliable", "transient_local", "keep_last", 37});

  EXPECT_EQ(RMW_QOS_POLICY_RELIABILITY_RELIABLE, qos.reliability);
  EXPECT_EQ(RMW_QOS_POLICY_DURABILITY_TRANSIENT_LOCAL, qos.durability);
  EXPECT_EQ(RMW_QOS_POLICY_HISTORY_KEEP_LAST, qos.history);
  EXPECT_EQ(37U, qos.depth);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, MakesKeepAllQosWithoutSynthesizingDepth)
{
  const auto qos = MakeRmwQosProfile(
    WireQosContract{"default", "reliable", "transient_local", "keep_all", 0});

  EXPECT_EQ(RMW_QOS_POLICY_RELIABILITY_RELIABLE, qos.reliability);
  EXPECT_EQ(RMW_QOS_POLICY_DURABILITY_TRANSIENT_LOCAL, qos.durability);
  EXPECT_EQ(RMW_QOS_POLICY_HISTORY_KEEP_ALL, qos.history);
  EXPECT_EQ(0U, qos.depth);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, MakesKeepLastQosWithNonDefaultDepth)
{
  const auto qos = MakeRmwQosProfile(
    WireQosContract{"default", "best_effort", "volatile", "keep_last", 37});

  EXPECT_EQ(RMW_QOS_POLICY_RELIABILITY_BEST_EFFORT, qos.reliability);
  EXPECT_EQ(RMW_QOS_POLICY_DURABILITY_VOLATILE, qos.durability);
  EXPECT_EQ(RMW_QOS_POLICY_HISTORY_KEEP_LAST, qos.history);
  EXPECT_EQ(37U, qos.depth);
}
