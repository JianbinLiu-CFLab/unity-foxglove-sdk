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

#include <algorithm>
#include <atomic>
#include <array>
#include <condition_variable>
#include <exception>
#include <fstream>
#include <limits>
#include <sstream>
#include <string>
#include <thread>
#include <vector>

// Include the production translation unit directly to exercise internal parser helpers.
#define UNITY2FOXGLOVE_ROS2_BRIDGE_TESTING
#include "../src/unity2foxglove_ros2_bridge.cpp"

namespace
{
#ifndef U2R2_PROTOCOL_FIXTURE_PATH
#error "U2R2_PROTOCOL_FIXTURE_PATH must identify the shared v1 authority fixture"
#endif

nlohmann::json LoadV1AuthorityFixture()
{
  std::ifstream input(U2R2_PROTOCOL_FIXTURE_PATH, std::ios::binary);
  if (!input) {
    throw std::runtime_error(
            std::string("unable to open shared U2R2 v1 authority fixture: ") +
            U2R2_PROTOCOL_FIXTURE_PATH);
  }
  nlohmann::json fixture;
  input >> fixture;
  return fixture;
}

std::vector<uint8_t> HexToBytes(const std::string & hex)
{
  if (hex.size() % 2 != 0) {
    throw std::runtime_error("fixture hex contains an incomplete byte");
  }
  std::vector<uint8_t> bytes;
  bytes.reserve(hex.size() / 2);
  for (size_t offset = 0; offset < hex.size(); offset += 2) {
    bytes.push_back(static_cast<uint8_t>(
      std::stoul(hex.substr(offset, 2), nullptr, 16)));
  }
  return bytes;
}

RawFrame ReadFixtureFrame(const nlohmann::json & vector)
{
  const auto bytes = HexToBytes(vector.at("frameHex").get<std::string>());
  if (bytes.size() < 16 ||
    bytes[0] != 'U' || bytes[1] != '2' || bytes[2] != 'R' || bytes[3] != '2')
  {
    throw std::runtime_error("fixture frame has an invalid fixed header");
  }
  const auto header_length = read_u32_le(&bytes[8]);
  const auto payload_length = read_u32_le(&bytes[12]);
  if (bytes.size() != 16U + header_length + payload_length) {
    throw std::runtime_error("fixture frame length does not match its fixed header");
  }
  const std::string header_json(
    bytes.begin() + 16,
    bytes.begin() + 16 + header_length);
  if (header_json != vector.at("headerJson").get<std::string>()) {
    throw std::runtime_error("fixture frame JSON does not match headerJson");
  }

  RawFrame raw;
  raw.header = nlohmann::json::parse(header_json);
  raw.payload.assign(bytes.begin() + 16 + header_length, bytes.end());
  if (raw.header != vector.at("header")) {
    throw std::runtime_error("fixture frame JSON does not match structured header");
  }
  if (raw.payload.size() != vector.at("payloadLength").get<size_t>()) {
    throw std::runtime_error("fixture payload length does not match structured metadata");
  }
  return raw;
}

std::vector<uint8_t> ReadSocketBytes(SocketHandle socket, size_t count)
{
  std::vector<uint8_t> bytes(count, 0);
  size_t offset = 0;
  while (offset < count) {
    const auto received = receive_socket(socket, bytes.data() + offset, count - offset);
    if (received <= 0) {
      throw std::runtime_error("fixture response socket closed before the frame completed");
    }
    offset += static_cast<size_t>(received);
  }
  return bytes;
}

std::vector<uint8_t> ReadSocketWireFrame(SocketHandle socket)
{
  auto bytes = ReadSocketBytes(socket, 16);
  const auto header_length = read_u32_le(&bytes[8]);
  const auto payload_length = read_u32_le(&bytes[12]);
  const auto remainder =
    ReadSocketBytes(socket, header_length + payload_length);
  bytes.insert(bytes.end(), remainder.begin(), remainder.end());
  return bytes;
}

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

const nlohmann::json & ExecutableV1BaseVector(
  const nlohmann::json & fixture,
  const std::string & base_frame)
{
  if (base_frame == "preparePublisher.request") {
    return fixture.at("preparePublisher").at("request");
  }
  if (base_frame == "preparePublisher.response") {
    return fixture.at("preparePublisher").at("response");
  }
  if (base_frame == "publish.frame") {
    return fixture.at("publish").at("frame");
  }
  throw std::runtime_error(
          "unknown executable v1 base frame: " + base_frame);
}

std::vector<uint8_t> ExecutableV1BaseFrame(
  const nlohmann::json & fixture,
  const nlohmann::json & vector)
{
  return HexToBytes(
    ExecutableV1BaseVector(
      fixture,
      vector.at("baseFrame").get<std::string>())
    .at("frameHex").get<std::string>());
}

void SetU32At(std::vector<uint8_t> & bytes, size_t offset, uint32_t value)
{
  if (offset + 4U > bytes.size()) {
    throw std::runtime_error("executable v1 u32 patch is outside the frame");
  }
  bytes[offset] = static_cast<uint8_t>(value & 0xffU);
  bytes[offset + 1U] = static_cast<uint8_t>((value >> 8U) & 0xffU);
  bytes[offset + 2U] = static_cast<uint8_t>((value >> 16U) & 0xffU);
  bytes[offset + 3U] = static_cast<uint8_t>((value >> 24U) & 0xffU);
}

void ReadExecutableV1Body(
  const std::vector<uint8_t> & frame,
  std::vector<uint8_t> & header,
  std::vector<uint8_t> & payload)
{
  if (frame.size() < 16U) {
    throw std::runtime_error("executable v1 base frame is too short");
  }
  const auto header_length = read_u32_le(&frame[8]);
  const auto payload_length = read_u32_le(&frame[12]);
  if (
    frame.size() !=
    16U + static_cast<size_t>(header_length) +
    static_cast<size_t>(payload_length))
  {
    throw std::runtime_error(
            "executable v1 base frame has inconsistent lengths");
  }
  header.assign(
    frame.begin() + 16,
    frame.begin() + 16 + header_length);
  payload.assign(
    frame.begin() + 16 + header_length,
    frame.end());
}

std::vector<uint8_t> RebuildExecutableV1Frame(
  const std::vector<uint8_t> & base,
  const std::vector<uint8_t> & header,
  const std::vector<uint8_t> & payload)
{
  if (base.size() < 16U ||
    header.size() > std::numeric_limits<uint32_t>::max() ||
    payload.size() > std::numeric_limits<uint32_t>::max())
  {
    throw std::runtime_error("executable v1 rebuild input is invalid");
  }
  std::vector<uint8_t> frame(
    16U + header.size() + payload.size(),
    0U);
  std::copy_n(base.begin(), 16, frame.begin());
  SetU32At(frame, 8, static_cast<uint32_t>(header.size()));
  SetU32At(frame, 12, static_cast<uint32_t>(payload.size()));
  std::copy(header.begin(), header.end(), frame.begin() + 16);
  std::copy(
    payload.begin(),
    payload.end(),
    frame.begin() + 16 + header.size());
  return frame;
}

std::vector<uint8_t> BuildExecutableV1Wire(
  const nlohmann::json & fixture,
  const nlohmann::json & vector)
{
  auto frame = ExecutableV1BaseFrame(fixture, vector);
  const auto action = vector.at("action").get<std::string>();
  if (action == "patch_frame") {
    const auto replacement =
      HexToBytes(vector.at("replacementHex").get<std::string>());
    const auto offset = vector.at("offset").get<size_t>();
    if (offset + replacement.size() > frame.size()) {
      throw std::runtime_error("executable v1 patch is outside the frame");
    }
    std::copy(
      replacement.begin(),
      replacement.end(),
      frame.begin() + offset);
    return frame;
  }
  if (action == "truncate_frame" || action == "stream_frame") {
    size_t length;
    if (vector.contains("length")) {
      length = vector.at("length").get<size_t>();
    } else {
      const auto trim = vector.at("trimBytes").get<size_t>();
      if (trim >= frame.size()) {
        throw std::runtime_error("executable v1 trim removes the whole frame");
      }
      length = frame.size() - trim;
    }
    if (length >= frame.size()) {
      throw std::runtime_error("executable v1 truncation is not shorter");
    }
    frame.resize(length);
    return frame;
  }
  if (action == "patch_json") {
    auto decoded = u2r2::decode_frame(frame);
    const auto path = vector.at("path").get<std::string>();
    if (path == "qos.history") {
      decoded.header.at("qos")["history"] = vector.at("value");
    } else if (path.find('.') == std::string::npos) {
      decoded.header[path] = vector.at("value");
    } else {
      throw std::runtime_error(
              "unsupported executable v1 JSON path: " + path);
    }
    return u2r2::encode_frame(decoded.header, decoded.payload);
  }

  std::vector<uint8_t> header;
  std::vector<uint8_t> payload;
  ReadExecutableV1Body(frame, header, payload);
  if (action == "duplicate_json_property") {
    if (header.empty() || header.front() != '{') {
      throw std::runtime_error(
              "executable v1 duplicate base header is not an object");
    }
    const auto prefix =
      nlohmann::json(vector.at("property").get<std::string>()).dump() +
      ":" + vector.at("value").dump() + ",";
    header.insert(
      header.begin() + 1,
      prefix.begin(),
      prefix.end());
    return RebuildExecutableV1Frame(frame, header, payload);
  }
  if (action == "replace_header_utf8") {
    const auto needle_text = vector.at("needle").get<std::string>();
    const std::vector<uint8_t> needle(
      needle_text.begin(),
      needle_text.end());
    const auto replacement =
      HexToBytes(vector.at("replacementHex").get<std::string>());
    const auto found = std::search(
      header.begin(),
      header.end(),
      needle.begin(),
      needle.end());
    if (found == header.end() || replacement.size() > needle.size()) {
      throw std::runtime_error(
              "executable v1 UTF-8 replacement target is invalid");
    }
    std::copy(replacement.begin(), replacement.end(), found);
    return RebuildExecutableV1Frame(frame, header, payload);
  }
  if (action == "append_json_root") {
    const auto suffix = vector.at("suffix").get<std::string>();
    header.insert(header.end(), suffix.begin(), suffix.end());
    return RebuildExecutableV1Frame(frame, header, payload);
  }
  throw std::runtime_error("unknown executable v1 action: " + action);
}

std::string ExpectedCppV1FailureFragment(const std::string & failure)
{
  if (failure == "magic") {
    return "magic";
  }
  if (failure == "version" || failure == "flags") {
    return "envelope version or reserved flags";
  }
  if (failure == "header_length") {
    return "JSON header length is out of range";
  }
  if (failure == "payload_length") {
    return "payload length is out of range";
  }
  if (failure == "fixed_header") {
    return "shorter than its fixed header";
  }
  if (failure == "header_completion" || failure == "payload_completion") {
    return "truncated or trailing bytes";
  }
  if (failure == "duplicate_property") {
    return "duplicate property";
  }
  if (failure == "operation") {
    return "first legacy U2R2 frame";
  }
  if (failure == "utf8" || failure == "json_root") {
    return "JSON header is invalid";
  }
  if (failure == "topic") {
    return "topic";
  }
  if (failure == "schema_name") {
    return "schemaName";
  }
  if (failure == "qos") {
    return "qos.depth";
  }
  throw std::runtime_error(
          "unknown executable C++ v1 failure classification: " + failure);
}

void ExpectExecutableV1NegativeRejected(
  const nlohmann::json & fixture,
  const nlohmann::json & vector)
{
  const auto action = vector.at("action").get<std::string>();
  const auto expected_failure =
    vector.at("expectedFailure").get<std::string>();
  if (action == "stream_frame") {
    const auto wire = BuildExecutableV1Wire(fixture, vector);
    const auto sockets = MakeConnectedSocketPair();
    ASSERT_NE(kInvalidSocket, sockets[0]);
    ASSERT_NE(kInvalidSocket, sockets[1]);
    ScopedFd writer(sockets[0]);
    ScopedFd reader(sockets[1]);
    configure_client_timeouts(reader.get());
    write_all(writer.get(), wire);
    const auto termination = vector.at("termination").get<std::string>();
    const auto limits = termination == "timeout"
      ? u2r2::ProtocolLimits::defaults().with({
          {"partialFrameTimeoutMs", vector.at("timeoutMs").get<uint64_t>()},
        })
      : u2r2::ProtocolLimits::defaults();
    if (termination == "eof") {
      ASSERT_EQ(0, ShutdownSocketWrite(writer.get()));
    } else if (termination != "timeout") {
      FAIL() << "unknown executable v1 stream termination: " << termination;
    }
    bridge_runtime::BridgeSessionProtocol protocol(limits);
    if (termination == "timeout") {
      try {
        (void)read_accounted_wire_frame(
          reader.get(),
          protocol,
          []() {return true;});
        FAIL() << "partial executable v1 frame did not time out";
      } catch (const u2r2::ProtocolError & error) {
        EXPECT_EQ("timeout", error.code());
        EXPECT_TRUE(error.terminal());
      }
      EXPECT_EQ("partial_payload_timeout", expected_failure);
    } else {
      EXPECT_THROW(
        (void)read_accounted_wire_frame(
          reader.get(),
          protocol,
          []() {return true;}),
        ClientClosedException);
      EXPECT_EQ("peer_close", expected_failure);
    }
    return;
  }

  const auto wire = BuildExecutableV1Wire(fixture, vector);
  std::string rejection;
  try {
    if (
      expected_failure == "topic" ||
      expected_failure == "schema_name" ||
      expected_failure == "qos")
    {
      (void)parse_prepare_publisher_frame(
        raw_frame_from_wire(wire, u2r2::ProtocolLimits::defaults()));
    } else {
      (void)u2r2::parse_legacy_v1_first_frame(wire);
    }
  } catch (const std::exception & error) {
    rejection = error.what();
  }
  EXPECT_FALSE(rejection.empty());
  EXPECT_NE(
    std::string::npos,
    rejection.find(ExpectedCppV1FailureFragment(expected_failure)))
    << rejection;
}
}  // namespace

TEST(Unity2FoxgloveRos2BridgeProtocol, SharedV1AuthorityFixtureMatchesCurrentCppProtocol)
{
  const auto fixture = LoadV1AuthorityFixture();
  ASSERT_EQ(1, fixture.at("fixtureVersion").get<int>());
  const auto & limits = fixture.at("limits");
  EXPECT_EQ(16, limits.at("fixedHeaderBytes").get<int>());
  EXPECT_EQ(kMaxHeaderBytes, limits.at("maxJsonHeaderBytes").get<uint32_t>());
  EXPECT_EQ(kMaxPayloadBytes, limits.at("maxPayloadBytes").get<uint32_t>());
  EXPECT_EQ(1024, limits.at("defaultQueueCapacityFrames").get<int>());
  EXPECT_EQ(68719476736ULL, limits.at("maxQueuedPayloadBytes").get<uint64_t>());
  EXPECT_EQ(1, limits.at("activeConnectionCount").get<int>());
  EXPECT_EQ(4, limits.at("listenBacklog").get<int>());
  EXPECT_EQ(
    std::chrono::milliseconds(5000),
    std::chrono::duration_cast<std::chrono::milliseconds>(kReadStallTimeout));

  const auto & health = fixture.at("health");
  const auto health_request = ReadFixtureFrame(health.at("request"));
  EXPECT_EQ("health_ping", health_request.header.at("op").get<std::string>());
  EXPECT_EQ(
    health.at("requestId").get<std::string>(),
    health_request.header.at("requestId").get<std::string>());
  EXPECT_EQ(
    kHealthProtocolVersion,
    health_request.header.at("protocolVersion").get<int>());
  EXPECT_TRUE(health_request.payload.empty());

  {
    const auto sockets = MakeConnectedSocketPair();
    ASSERT_NE(kInvalidSocket, sockets[0]);
    ASSERT_NE(kInvalidSocket, sockets[1]);
    ScopedFd writer(sockets[0]);
    ScopedFd reader(sockets[1]);
    const auto expected = HexToBytes(
      health.at("response").at("sidecarFrameHex").get<std::string>());
    write_health_pong_ok(writer.get(), health.at("requestId").get<std::string>());
    EXPECT_EQ(expected, ReadSocketBytes(reader.get(), expected.size()));
  }

  const auto & preparation = fixture.at("preparePublisher");
  const auto preparation_request = parse_prepare_publisher_frame(
    ReadFixtureFrame(preparation.at("request")));
  EXPECT_EQ(
    preparation.at("requestId").get<std::string>(),
    preparation_request.request_id);
  EXPECT_EQ(
    preparation.at("topic").get<std::string>(),
    preparation_request.frame.topic);
  EXPECT_EQ(
    preparation.at("schemaName").get<std::string>(),
    preparation_request.frame.schema_name);
  EXPECT_EQ("default", preparation_request.frame.profile);
  EXPECT_EQ("reliable", preparation_request.frame.reliability);
  EXPECT_EQ("volatile", preparation_request.frame.durability);
  EXPECT_EQ("keep_last", preparation_request.frame.history);
  EXPECT_EQ(10, preparation_request.frame.depth);

  {
    const auto sockets = MakeConnectedSocketPair();
    ASSERT_NE(kInvalidSocket, sockets[0]);
    ASSERT_NE(kInvalidSocket, sockets[1]);
    ScopedFd writer(sockets[0]);
    ScopedFd reader(sockets[1]);
    const auto expected = HexToBytes(
      preparation.at("response").at("sidecarFrameHex").get<std::string>());
    write_u2r2_frame(
      writer.get(),
      publisher_ready_ok(preparation_request.request_id),
      {});
    EXPECT_EQ(expected, ReadSocketBytes(reader.get(), expected.size()));
  }

  const auto & publish = fixture.at("publish");
  const auto publish_frame = parse_publish_frame(
    ReadFixtureFrame(publish.at("frame")));
  EXPECT_EQ(publish.at("topic").get<std::string>(), publish_frame.topic);
  EXPECT_EQ(publish.at("schemaName").get<std::string>(), publish_frame.schema_name);
  EXPECT_EQ(publish.at("encoding").get<std::string>(), publish_frame.encoding);
  EXPECT_EQ(publish.at("logTimeNs").get<uint64_t>(), publish_frame.log_time_ns);
  EXPECT_EQ(publish.at("sequence").get<uint64_t>(), publish_frame.sequence);
  EXPECT_EQ(
    HexToBytes(publish.at("payloadHex").get<std::string>()),
    publish_frame.payload);

  const std::array<std::string, 19> expected_negative_ids = {
    "bad_magic",
    "bad_version",
    "bad_flags",
    "oversized_header",
    "oversized_payload",
    "truncated_fixed",
    "truncated_header",
    "truncated_payload",
    "partial_payload_stall",
    "duplicate_operation",
    "unknown_operation",
    "illegal_sequence",
    "invalid_utf8",
    "trailing_json_root",
    "invalid_topic",
    "invalid_type",
    "invalid_delivery_policy",
    "correlation_mismatch",
    "peer_close"
  };
  const auto & negative_vectors = fixture.at("negativeVectors");
  ASSERT_EQ(expected_negative_ids.size(), negative_vectors.size());
  for (size_t index = 0; index < expected_negative_ids.size(); ++index) {
    EXPECT_EQ(
      expected_negative_ids[index],
      negative_vectors[index].at("id").get<std::string>());
    EXPECT_EQ(
      "reject",
      negative_vectors[index].at("expected").get<std::string>());
  }

  const auto & execution =
    fixture.at("v2").at("legacyV1NegativeExecution");
  EXPECT_EQ(1, execution.at("schemaVersion").get<int>());
  EXPECT_EQ("negativeVectors", execution.at("catalog").get<std::string>());
  const auto & executable_vectors = execution.at("vectors");
  ASSERT_EQ(expected_negative_ids.size(), executable_vectors.size());
  for (size_t index = 0; index < executable_vectors.size(); ++index) {
    const auto & vector = executable_vectors[index];
    EXPECT_EQ(
      expected_negative_ids[index],
      vector.at("id").get<std::string>());
    ASSERT_TRUE(vector.at("action").is_string());
    ASSERT_FALSE(vector.at("action").get<std::string>().empty());
    ASSERT_TRUE(vector.at("expectedFailure").is_string());
    ASSERT_FALSE(vector.at("expectedFailure").get<std::string>().empty());
    const auto & consumers = vector.at("consumers");
    ASSERT_TRUE(consumers.is_array());
    ASSERT_FALSE(consumers.empty());
    const auto consumes_cpp =
      std::find(consumers.begin(), consumers.end(), "cpp") !=
      consumers.end();
    if (consumes_cpp) {
      ExpectExecutableV1NegativeRejected(fixture, vector);
    }
  }
}

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
  HealthReadinessDoesNotInitializeTheDeferredRosNode)
{
  rclcpp::init(0, nullptr);
  const auto sockets = MakeConnectedSocketPair();
  ASSERT_NE(kInvalidSocket, sockets[0]);
  ASSERT_NE(kInvalidSocket, sockets[1]);
  ScopedFd client_socket(sockets[0]);
  ScopedFd server_socket(sockets[1]);

  size_t node_creation_attempts = 0;
  DeferredBridgeSession session(
    PayloadFormat::CdrWithEncapsulation,
    [&]() -> rclcpp::Node::SharedPtr {
      ++node_creation_attempts;
      throw std::runtime_error("health readiness must not initialize ROS");
    });
  RawFrame health;
  health.header = {
    {"op", "health_ping"},
    {"requestId", "phase184-deferred-health"},
    {"protocolVersion", 1}
  };

  dispatch_deferred_frame(server_socket.get(), health, session);
  const auto response = read_raw_frame(
    client_socket.get(),
    rclcpp::Node::SharedPtr {});

  EXPECT_EQ("health_pong", response.header.value("op", ""));
  EXPECT_EQ("phase184-deferred-health", response.header.value("requestId", ""));
  EXPECT_EQ("ok", response.header.value("status", ""));
  EXPECT_EQ(0U, node_creation_attempts);
  rclcpp::shutdown();
}

TEST(
  Unity2FoxgloveRos2BridgeProtocol,
  IdleReadStopsWhenTheOwningRosContextStops)
{
  const auto sockets = MakeConnectedSocketPair();
  ASSERT_NE(kInvalidSocket, sockets[0]);
  ASSERT_NE(kInvalidSocket, sockets[1]);
  ScopedFd client_socket(sockets[0]);
  ScopedFd server_socket(sockets[1]);
  configure_client_timeouts(server_socket.get());

  std::atomic<bool> context_ok {true};
  std::atomic<int> outcome {0};
  std::thread reader(
    [&]() {
      std::vector<uint8_t> buffer;
      try {
        (void)read_exact(
          server_socket.get(),
          buffer,
          4,
          rclcpp::Node::SharedPtr {},
          [&]() {return context_ok.load();});
        outcome.store(1);
      } catch (const ClientClosedException &) {
        outcome.store(2);
      } catch (...) {
        outcome.store(3);
      }
    });

  std::this_thread::sleep_for(std::chrono::milliseconds(50));
  context_ok.store(false);
  const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(2);
  while (outcome.load() == 0 && std::chrono::steady_clock::now() < deadline) {
    std::this_thread::sleep_for(std::chrono::milliseconds(10));
  }

  const bool stopped_before_socket_close = outcome.load() != 0;
  if (!stopped_before_socket_close) {
    EXPECT_EQ(0, ShutdownSocketWrite(client_socket.get()));
  }
  reader.join();

  EXPECT_TRUE(stopped_before_socket_close);
  EXPECT_EQ(2, outcome.load());
}

TEST(
  Unity2FoxgloveRos2BridgeProtocol,
  LaterDeferredSessionRetainsTheProcessLevelRosNode)
{
  auto context = std::make_shared<rclcpp::Context>();
  context->init(0, nullptr);
  rclcpp::NodeOptions options;
  options.context(context);
  auto process_node = std::make_shared<rclcpp::Node>(
    "phase184_bridge_deferred_process_node_test",
    options);

  size_t node_creation_attempts = 0;
  DeferredBridgeSession session(
    PayloadFormat::CdrWithEncapsulation,
    process_node,
    [&]() -> rclcpp::Node::SharedPtr {
      ++node_creation_attempts;
      return process_node;
    });

  EXPECT_EQ(process_node, session.node());
  EXPECT_NO_THROW(session.spin_some());
  EXPECT_EQ(0U, node_creation_attempts);
  context->shutdown("phase184 deferred process node test complete");
}

namespace
{
namespace bridge_runtime = unity2foxglove::ros2_bridge::runtime;
namespace u2r2 = unity2foxglove::ros2_bridge::u2r2;

struct GenerationPublisherLifetime final
{
  explicit GenerationPublisherLifetime(std::atomic<size_t> & destruction_count)
  : destruction_count_(&destruction_count)
  {
  }

  ~GenerationPublisherLifetime()
  {
    ++(*destruction_count_);
  }

  std::atomic<size_t> * destruction_count_;
};
}  // namespace

TEST(
  Unity2FoxgloveRos2BridgeProtocol,
  AccountedWireReadHoldsExactlyOneFullFrameLeaseUntilCompletion)
{
  const auto sockets = MakeConnectedSocketPair();
  ASSERT_NE(kInvalidSocket, sockets[0]);
  ASSERT_NE(kInvalidSocket, sockets[1]);
  ScopedFd client_socket(sockets[0]);
  ScopedFd server_socket(sockets[1]);
  configure_client_timeouts(client_socket.get());
  configure_client_timeouts(server_socket.get());

  bridge_runtime::BridgeSessionProtocol protocol(
    u2r2::ProtocolLimits::defaults());
  const auto wire = u2r2::encode_frame(
    {
      {"op", "health_ping"},
      {"protocolVersion", 1},
      {"requestId", "phase186c-accounted-read"},
    },
    {});
  std::optional<AccountedWireFrame> received;
  std::exception_ptr reader_error;
  std::thread reader(
    [&]() {
      try {
        received.emplace(
          read_accounted_wire_frame(
            server_socket.get(),
            protocol,
            []() {return true;}));
      } catch (...) {
        reader_error = std::current_exception();
      }
    });

  write_all(
    client_socket.get(),
    std::vector<uint8_t>(wire.begin(), wire.begin() + 16));
  const auto deadline =
    std::chrono::steady_clock::now() + std::chrono::seconds(2);
  while (
    protocol.in_flight_bytes() == 0 &&
    std::chrono::steady_clock::now() < deadline)
  {
    std::this_thread::sleep_for(std::chrono::milliseconds(5));
  }
  EXPECT_EQ(wire.size(), protocol.in_flight_bytes());
  EXPECT_EQ(wire.size() * 2U, protocol.transient_bytes());

  write_all(
    client_socket.get(),
    std::vector<uint8_t>(wire.begin() + 16, wire.end()));
  reader.join();

  ASSERT_EQ(nullptr, reader_error);
  ASSERT_TRUE(received.has_value());
  EXPECT_EQ(wire, received->bytes);
  EXPECT_EQ(wire.size(), protocol.in_flight_bytes());
  EXPECT_EQ(wire.size() * 2U, protocol.transient_bytes());
  received.reset();
  EXPECT_EQ(0U, protocol.in_flight_bytes());
  EXPECT_EQ(0U, protocol.transient_bytes());
}

TEST(
  Unity2FoxgloveRos2BridgeProtocol,
  AccountedWireWriteConsumesFrozenWallClockDeadline)
{
  const auto sockets = MakeConnectedSocketPair();
  ASSERT_NE(kInvalidSocket, sockets[0]);
  ASSERT_NE(kInvalidSocket, sockets[1]);
  ScopedFd client_socket(sockets[0]);
  ScopedFd server_socket(sockets[1]);
  configure_client_timeouts(client_socket.get());
  configure_client_timeouts(server_socket.get());

  const int socket_buffer_bytes = 1024;
  ASSERT_EQ(
    0,
    set_socket_option(
      client_socket.get(),
      SOL_SOCKET,
      SO_SNDBUF,
      &socket_buffer_bytes,
      static_cast<SocketLength>(sizeof(socket_buffer_bytes))));
  ASSERT_EQ(
    0,
    set_socket_option(
      server_socket.get(),
      SOL_SOCKET,
      SO_RCVBUF,
      &socket_buffer_bytes,
      static_cast<SocketLength>(sizeof(socket_buffer_bytes))));

  const auto limits = u2r2::ProtocolLimits::defaults().with({
    {"writeTimeoutMs", 25},
  });
  size_t buffered_bytes = 0;
  {
    ScopedNonBlockingSocket non_blocking(client_socket.get());
    const std::vector<uint8_t> fill(64U * 1024U, 0x5a);
    while (true) {
      const auto sent =
        send_socket(client_socket.get(), fill.data(), fill.size());
      if (sent > 0) {
        buffered_bytes += static_cast<size_t>(sent);
        ASSERT_LT(buffered_bytes, 256U * 1024U * 1024U)
          << "the test could not saturate the loopback send window";
        continue;
      }
      const auto error = last_socket_error();
      ASSERT_TRUE(socket_error_is_retryable_timeout(error))
        << "unexpected socket error while saturating the send window: "
        << socket_error_text(error);
      break;
    }
  }
  ASSERT_GT(buffered_bytes, 0U);

  const std::vector<uint8_t> blocked_response(1024U, 0x5a);
  const auto started = std::chrono::steady_clock::now();
  try {
    write_all_accounted(
      client_socket.get(),
      blocked_response,
      limits);
    FAIL() << "a blocked U2R2 write ignored its frozen wall-clock deadline";
  } catch (const u2r2::ProtocolError & error) {
    EXPECT_EQ("timeout", error.code());
    EXPECT_TRUE(error.terminal());
  }
  EXPECT_LT(
    std::chrono::steady_clock::now() - started,
    std::chrono::seconds(2));
}

TEST(
  Unity2FoxgloveRos2BridgeProtocol,
  AccountedWireReadConsumesPartialAndIdleWallClockDeadlines)
{
  const auto expect_timeout =
    [](
    u2r2::TimeoutKind idle_timeout,
    const u2r2::ProtocolLimits & limits,
    const std::vector<uint8_t> & prefix) {
      const auto sockets = MakeConnectedSocketPair();
      ASSERT_NE(kInvalidSocket, sockets[0]);
      ASSERT_NE(kInvalidSocket, sockets[1]);
      ScopedFd client_socket(sockets[0]);
      ScopedFd server_socket(sockets[1]);
      configure_client_timeouts(client_socket.get());
      configure_client_timeouts(server_socket.get());

      bridge_runtime::BridgeSessionProtocol protocol(limits);
      std::atomic<bool> reader_done{false};
      std::exception_ptr reader_error;
      std::thread reader(
        [&]() {
          try {
            (void)read_accounted_wire_frame(
              server_socket.get(),
              protocol,
              []() {return true;},
              idle_timeout);
          } catch (...) {
            reader_error = std::current_exception();
          }
          reader_done.store(true);
        });
      if (!prefix.empty()) {
        write_all(client_socket.get(), prefix);
      }

      const auto deadline =
        std::chrono::steady_clock::now() + std::chrono::seconds(2);
      while (
        !reader_done.load() &&
        std::chrono::steady_clock::now() < deadline)
      {
        std::this_thread::sleep_for(std::chrono::milliseconds(5));
      }
      const bool completed_before_forced_close = reader_done.load();
      if (!completed_before_forced_close) {
        shutdown_socket_both(client_socket.get());
      }
      reader.join();

      EXPECT_TRUE(completed_before_forced_close);
      ASSERT_NE(nullptr, reader_error);
      try {
        std::rethrow_exception(reader_error);
        FAIL() << "the accounted read ignored its frozen wall-clock deadline";
      } catch (const u2r2::ProtocolError & error) {
        EXPECT_EQ("timeout", error.code());
        EXPECT_TRUE(error.terminal());
      }
    };

  expect_timeout(
    u2r2::TimeoutKind::read,
    u2r2::ProtocolLimits::defaults().with({
      {"readTimeoutMs", 25},
    }),
    {});
  expect_timeout(
    u2r2::TimeoutKind::handshake,
    u2r2::ProtocolLimits::defaults().with({
      {"partialFrameTimeoutMs", 25},
    }),
    {'U'});
}

TEST(
  Unity2FoxgloveRos2BridgeProtocol,
  OwnedPreclassificationHandshakeTimesOutBeforeRoleOrGenerationAllocation)
{
  const auto sockets = MakeConnectedSocketPair();
  ASSERT_NE(kInvalidSocket, sockets[0]);
  ASSERT_NE(kInvalidSocket, sockets[1]);
  ScopedFd client_socket(sockets[0]);
  ScopedFd server_socket(sockets[1]);
  configure_client_timeouts(client_socket.get());
  configure_client_timeouts(server_socket.get());

  const auto limits = u2r2::ProtocolLimits::defaults().with({
    {"handshakeTimeoutMs", 25},
  });
  bridge_runtime::ProcessConnectionAuthority authority(limits);
  std::atomic<size_t> generation_count{0};
  std::atomic<bool> server_done{false};
  std::exception_ptr server_error;
  BridgeGenerationFactory generation_factory =
    [&]() -> std::unique_ptr<BridgeNode> {
      ++generation_count;
      throw std::runtime_error("handshake timeout must not create a generation");
    };
  std::thread server(
    [&]() {
      try {
        process_owned_client(
          server_socket.get(),
          authority,
          generation_factory,
          rclcpp::get_logger("phase186c_handshake_timeout_test"),
          []() {return true;});
      } catch (...) {
        server_error = std::current_exception();
      }
      server_done.store(true);
    });

  const auto deadline =
    std::chrono::steady_clock::now() + std::chrono::seconds(1);
  while (
    !server_done.load() &&
    std::chrono::steady_clock::now() < deadline)
  {
    std::this_thread::sleep_for(std::chrono::milliseconds(10));
  }
  const bool completed_before_forced_close = server_done.load();
  if (!completed_before_forced_close) {
    shutdown_socket_both(client_socket.get());
  }
  server.join();

  EXPECT_TRUE(completed_before_forced_close);
  ASSERT_NE(nullptr, server_error);
  try {
    std::rethrow_exception(server_error);
    FAIL() << "idle preclassification unexpectedly completed";
  } catch (const u2r2::ProtocolError & error) {
    EXPECT_EQ("timeout", error.code());
    EXPECT_TRUE(error.terminal());
  }
  EXPECT_EQ(0U, generation_count.load());
  EXPECT_EQ(0U, authority.classified_count());
}

TEST(
  Unity2FoxgloveRos2BridgeProtocol,
  OwnedLegacyHealthProbeIsOneShotAndDoesNotCreatePublisherGeneration)
{
  const auto sockets = MakeConnectedSocketPair();
  ASSERT_NE(kInvalidSocket, sockets[0]);
  ASSERT_NE(kInvalidSocket, sockets[1]);
  ScopedFd client_socket(sockets[0]);
  ScopedFd server_socket(sockets[1]);
  configure_client_timeouts(client_socket.get());
  configure_client_timeouts(server_socket.get());

  bridge_runtime::ProcessConnectionAuthority authority(
    u2r2::ProtocolLimits::defaults());
  std::atomic<size_t> generation_count{0};
  std::exception_ptr server_error;
  BridgeGenerationFactory generation_factory =
    [&]() -> std::unique_ptr<BridgeNode> {
      ++generation_count;
      throw std::runtime_error("legacy health must not create a ROS generation");
    };

  std::thread server(
    [&]() {
      try {
        process_owned_client(
          server_socket.get(),
          authority,
          generation_factory,
          rclcpp::get_logger("phase186c_v1_probe_test"),
          []() {return true;});
      } catch (...) {
        server_error = std::current_exception();
      }
    });

  const auto health_request = u2r2::encode_frame(
    {
      {"op", "health_ping"},
      {"protocolVersion", 1},
      {"requestId", "phase186c-health"},
    },
    {});
  write_all(client_socket.get(), health_request);
  const auto response = u2r2::decode_frame(
    ReadSocketWireFrame(client_socket.get()));
  server.join();

  ASSERT_EQ(nullptr, server_error);
  EXPECT_EQ("health_pong", response.header.at("op").get<std::string>());
  EXPECT_EQ(
    "phase186c-health",
    response.header.at("requestId").get<std::string>());
  EXPECT_EQ("ok", response.header.at("status").get<std::string>());
  EXPECT_EQ(0U, generation_count.load());
  EXPECT_EQ(0U, authority.classified_count());
}

TEST(
  Unity2FoxgloveRos2BridgeProtocol,
  OwnedV2PreparationAndPublishReplayMutateOnceAndTearDownGeneration)
{
  const auto sockets = MakeConnectedSocketPair();
  ASSERT_NE(kInvalidSocket, sockets[0]);
  ASSERT_NE(kInvalidSocket, sockets[1]);
  ScopedFd client_socket(sockets[0]);
  ScopedFd server_socket(sockets[1]);
  configure_client_timeouts(client_socket.get());
  configure_client_timeouts(server_socket.get());

  bridge_runtime::ProcessConnectionAuthority authority(
    u2r2::ProtocolLimits::defaults());
  std::atomic<size_t> generation_count{0};
  std::atomic<size_t> publisher_create_count{0};
  std::atomic<size_t> publisher_destroy_count{0};
  std::atomic<size_t> publish_count{0};
  std::exception_ptr server_error;
  BridgeGenerationFactory generation_factory =
    [&]() -> std::unique_ptr<BridgeNode> {
      ++generation_count;
      GenericPublisherFactory publisher_factory =
        [&](const std::string &, const std::string &, const rclcpp::QoS &) {
          ++publisher_create_count;
          auto lifetime = std::make_shared<GenerationPublisherLifetime>(
            publisher_destroy_count);
          return [lifetime, &publish_count](
            const rclcpp::SerializedMessage &) {
              ++publish_count;
            };
        };
      return std::make_unique<BridgeNode>(
        PayloadFormat::CdrWithEncapsulation,
        std::move(publisher_factory));
    };

  std::thread server(
    [&]() {
      try {
        process_owned_client(
          server_socket.get(),
          authority,
          generation_factory,
          rclcpp::get_logger("phase186c_v2_replay_test"),
          []() {return true;});
      } catch (...) {
        server_error = std::current_exception();
      }
    });

  const auto hello = u2r2::encode_frame(
    {
      {"op", "hello"},
      {"protocolVersion", 2},
      {"requestId", 1},
      {"clientName", "phase186c-smoke"},
      {"capabilities", nlohmann::json::array({"publish"})},
    },
    {});
  write_all(client_socket.get(), hello);
  const auto hello_ack = u2r2::parse_v2(
    u2r2::decode_frame(ReadSocketWireFrame(client_socket.get())));
  ASSERT_EQ(u2r2::Operation::HelloAck, hello_ack.operation);

  const auto prepare = u2r2::encode_frame(
    {
      {"op", "prepare_publisher"},
      {"protocolVersion", 2},
      {"requestId", 2},
      {"sessionId", hello_ack.session_id},
      {"connectionGeneration", hello_ack.connection_generation},
      {"topic", "/phase186/v2/state"},
      {"schemaName", "std_msgs/msg/String"},
      {"encoding", "cdr"},
      {"qos", {
          {"profile", "default"},
          {"reliability", "reliable"},
          {"durability", "volatile"},
          {"history", "keep_last"},
          {"depth", 10},
        }},
    },
    {});
  write_all(client_socket.get(), prepare);
  const auto first_ready = ReadSocketWireFrame(client_socket.get());
  write_all(client_socket.get(), prepare);
  const auto replayed_ready = ReadSocketWireFrame(client_socket.get());
  EXPECT_EQ(first_ready, replayed_ready);
  const auto ready =
    u2r2::parse_v2(u2r2::decode_frame(first_ready));
  EXPECT_EQ(u2r2::Operation::PublisherReady, ready.operation);
  EXPECT_EQ("ok", ready.status);

  const auto publish = u2r2::encode_frame(
    {
      {"op", "publish"},
      {"protocolVersion", 2},
      {"requestId", 3},
      {"messageId", 41},
      {"sessionId", hello_ack.session_id},
      {"connectionGeneration", hello_ack.connection_generation},
      {"topic", "/phase186/v2/state"},
      {"schemaName", "std_msgs/msg/String"},
      {"encoding", "cdr"},
      {"logTimeNs", 186},
      {"sequence", 41},
      {"qos", {
          {"profile", "default"},
          {"reliability", "reliable"},
          {"durability", "volatile"},
          {"history", "keep_last"},
          {"depth", 10},
        }},
    },
    {0x00, 0x01, 0x00, 0x00, 0x01});
  write_all(client_socket.get(), publish);
  const auto first_result = ReadSocketWireFrame(client_socket.get());
  write_all(client_socket.get(), publish);
  const auto replayed_result = ReadSocketWireFrame(client_socket.get());
  EXPECT_EQ(first_result, replayed_result);
  const auto result =
    u2r2::parse_v2(u2r2::decode_frame(first_result));
  EXPECT_EQ(u2r2::Operation::PublishResult, result.operation);
  EXPECT_EQ("ok", result.status);
  EXPECT_EQ(41U, result.message_id);

  EXPECT_EQ(0, ShutdownSocketWrite(client_socket.get()));
  server.join();

  ASSERT_EQ(nullptr, server_error);
  EXPECT_EQ(1U, generation_count.load());
  EXPECT_EQ(1U, publisher_create_count.load());
  EXPECT_EQ(1U, publish_count.load());
  EXPECT_EQ(1U, publisher_destroy_count.load());
  EXPECT_EQ(0U, authority.classified_count());
  auto replacement =
    authority.try_acquire_role(u2r2::ConnectionRole::data_session);
  ASSERT_TRUE(replacement.has_value());
  replacement->release();
}

TEST(
  Unity2FoxgloveRos2BridgeProtocol,
  OwnedV2SecondDataSessionGetsStableBusyWithoutCreatingGeneration)
{
  const auto sockets = MakeConnectedSocketPair();
  ASSERT_NE(kInvalidSocket, sockets[0]);
  ASSERT_NE(kInvalidSocket, sockets[1]);
  ScopedFd client_socket(sockets[0]);
  ScopedFd server_socket(sockets[1]);
  configure_client_timeouts(client_socket.get());
  configure_client_timeouts(server_socket.get());

  bridge_runtime::ProcessConnectionAuthority authority(
    u2r2::ProtocolLimits::defaults());
  auto active_data =
    authority.try_acquire_role(u2r2::ConnectionRole::data_session);
  ASSERT_TRUE(active_data.has_value());

  std::atomic<size_t> generation_count{0};
  std::exception_ptr server_error;
  BridgeGenerationFactory generation_factory =
    [&]() -> std::unique_ptr<BridgeNode> {
      ++generation_count;
      throw std::runtime_error("busy session must not create a ROS generation");
    };
  std::thread server(
    [&]() {
      try {
        process_owned_client(
          server_socket.get(),
          authority,
          generation_factory,
          rclcpp::get_logger("phase186c_v2_busy_test"),
          []() {return true;});
      } catch (...) {
        server_error = std::current_exception();
      }
    });

  write_all(
    client_socket.get(),
    u2r2::encode_frame(
      {
        {"op", "hello"},
        {"protocolVersion", 2},
        {"requestId", 91},
        {"clientName", "phase186c-busy"},
        {"capabilities", nlohmann::json::array({"publish"})},
      },
      {}));
  const auto response = u2r2::parse_v2(
    u2r2::decode_frame(ReadSocketWireFrame(client_socket.get())));
  server.join();

  ASSERT_EQ(nullptr, server_error);
  EXPECT_EQ(u2r2::Operation::Busy, response.operation);
  EXPECT_EQ(91U, response.request_id);
  EXPECT_EQ("error", response.status);
  EXPECT_EQ("busy", response.error_code);
  EXPECT_TRUE(response.terminal);
  EXPECT_EQ(0U, generation_count.load());
  EXPECT_EQ(1U, authority.classified_count());
  active_data->release();
  EXPECT_EQ(0U, authority.classified_count());
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
  LegacyPublisherCapacityFailsBeforeASecondPublisherFactoryMutation)
{
  size_t publisher_creations = 0;
  GenericPublisherFactory factory =
    [&](const std::string &, const std::string &, const rclcpp::QoS &) {
      ++publisher_creations;
      return [](const rclcpp::SerializedMessage &) {};
    };
  BridgeNode bridge(
    PayloadFormat::CdrWithEncapsulation,
    std::move(factory),
    1);

  EXPECT_NO_THROW(bridge.prepare(
      parse_prepare_publisher_frame(
        MakePreparePublisherRawFrame(
          "capacity-1",
          "/phase186/v1/first")).frame));
  EXPECT_THROW(
    bridge.prepare(
      parse_prepare_publisher_frame(
        MakePreparePublisherRawFrame(
          "capacity-2",
          "/phase186/v1/second")).frame),
    std::runtime_error);
  EXPECT_EQ(1U, publisher_creations);
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

TEST(
  Unity2FoxgloveRos2BridgeProtocol,
  GenericSubscriptionReceivesExactExternalSerializedCdr)
{
  auto context = std::make_shared<rclcpp::Context>();
  context->init(0, nullptr);
  bridge_runtime::ProcessRosOwner ros_owner(
    "phase186_d_sidecar_subscription_probe",
    context);
  BridgeNode bridge(
    ros_owner.require_node(),
    PayloadFormat::CdrWithEncapsulation);
  const u2r2::ContractIdentity identity(
    u2r2::ContractKey(41U, 7U),
    u2r2::ContractDirection::subscribe,
    "/phase186/d/external",
    "std_msgs/msg/String",
    u2r2::Qos{
      "default", "reliable", "volatile", "keep_last", 10U});

  std::mutex received_mutex;
  std::condition_variable received_changed;
  std::vector<uint8_t> received;
  uint64_t receive_time_ns = 0;
  auto subscription = bridge.subscribe(
    identity,
    [&](const uint8_t * data,
      size_t size,
      uint64_t time_ns,
      bridge_runtime::BridgeSampleOrigin origin) {
      EXPECT_EQ(bridge_runtime::BridgeSampleOrigin::external, origin);
      {
        std::lock_guard<std::mutex> lock(received_mutex);
        received.assign(data, data + size);
        receive_time_ns = time_ns;
      }
      received_changed.notify_all();
      return bridge_runtime::BridgeSerializedAdmission::accepted;
    });

  rclcpp::NodeOptions options;
  options.context(context);
  auto external = std::make_shared<rclcpp::Node>(
    "phase186_d_external_publisher_probe",
    options);
  auto publisher = external->create_generic_publisher(
    identity.topic,
    identity.schema_name,
    rclcpp::QoS(10));
  const auto discovery_deadline =
    std::chrono::steady_clock::now() + std::chrono::seconds(5);
  while (
    publisher->get_subscription_count() == 0 &&
    std::chrono::steady_clock::now() < discovery_deadline)
  {
    std::this_thread::sleep_for(std::chrono::milliseconds(10));
  }
  ASSERT_GT(publisher->get_subscription_count(), 0U);

  const std::vector<uint8_t> payload{
    0x00U, 0x01U, 0x00U, 0x00U, 0x02U, 0x00U, 0x00U, 0x00U,
    0x41U, 0x00U};
  rclcpp::SerializedMessage serialized(payload.size());
  auto & raw = serialized.get_rcl_serialized_message();
  ASSERT_GE(raw.buffer_capacity, payload.size());
  std::memcpy(raw.buffer, payload.data(), payload.size());
  raw.buffer_length = payload.size();
  publisher->publish(serialized);

  {
    std::unique_lock<std::mutex> lock(received_mutex);
    ASSERT_TRUE(received_changed.wait_for(
        lock,
        std::chrono::seconds(5),
        [&]() {return !received.empty();}));
  }
  EXPECT_EQ(payload, received);
  EXPECT_GT(receive_time_ns, 0U);

  subscription.reset();
  publisher.reset();
  external.reset();
  EXPECT_TRUE(ros_owner.stop());
  context->shutdown("Phase186-D external subscription probe complete");
}

TEST(
  Unity2FoxgloveRos2BridgeProtocol,
  OwnedV2SubscriptionStreamsExternalCdrThroughTheSingleSocketWriter)
{
  auto context = std::make_shared<rclcpp::Context>();
  context->init(0, nullptr);
  bridge_runtime::ProcessRosOwner ros_owner(
    "phase186_d_owned_subscription_probe",
    context);
  const auto sockets = MakeConnectedSocketPair();
  ASSERT_NE(kInvalidSocket, sockets[0]);
  ASSERT_NE(kInvalidSocket, sockets[1]);
  ScopedFd client_socket(sockets[0]);
  ScopedFd server_socket(sockets[1]);
  configure_client_timeouts(client_socket.get());
  configure_client_timeouts(server_socket.get());

  bridge_runtime::ProcessConnectionAuthority authority(
    u2r2::ProtocolLimits::defaults());
  std::exception_ptr server_error;
  BridgeGenerationFactory generation_factory =
    [&]() {
      return std::make_unique<BridgeNode>(
        ros_owner.require_node(),
        PayloadFormat::CdrWithEncapsulation);
    };
  std::thread server(
    [&]() {
      try {
        process_owned_client(
          server_socket.get(),
          authority,
          generation_factory,
          rclcpp::get_logger("phase186_d_owned_subscription_test"),
          [context]() {return rclcpp::ok(context);});
      } catch (...) {
        server_error = std::current_exception();
      }
    });

  write_all(
    client_socket.get(),
    u2r2::encode_frame(
      {
        {"op", "hello"},
        {"protocolVersion", 2},
        {"requestId", 1},
        {"clientName", "phase186-d-subscription"},
        {"capabilities", nlohmann::json::array({"subscribe"})},
      },
      {}));
  const auto hello_ack = u2r2::parse_v2(
    u2r2::decode_frame(ReadSocketWireFrame(client_socket.get())));
  ASSERT_EQ(u2r2::Operation::HelloAck, hello_ack.operation);

  const std::string topic = "/phase186/d/owned_external";
  const std::string schema = "std_msgs/msg/String";
  const auto registration = u2r2::encode_frame(
    {
      {"op", "register_subscription"},
      {"protocolVersion", 2},
      {"requestId", 2},
      {"sessionId", hello_ack.session_id},
      {"connectionGeneration", hello_ack.connection_generation},
      {"contractId", 41},
      {"topic", topic},
      {"schemaName", schema},
      {"encoding", "cdr"},
      {"qos", {
          {"profile", "default"},
          {"reliability", "reliable"},
          {"durability", "volatile"},
          {"history", "keep_last"},
          {"depth", 10},
        }},
    },
    {});
  write_all(client_socket.get(), registration);
  const auto ready = u2r2::parse_v2(
    u2r2::decode_frame(ReadSocketWireFrame(client_socket.get())));
  ASSERT_EQ(u2r2::Operation::SubscriptionReady, ready.operation);
  ASSERT_EQ("ok", ready.status);
  ASSERT_EQ(41U, ready.contract_id);

  rclcpp::NodeOptions options;
  options.context(context);
  auto external = std::make_shared<rclcpp::Node>(
    "phase186_d_owned_external_publisher",
    options);
  auto publisher = external->create_generic_publisher(
    topic,
    schema,
    rclcpp::QoS(10));
  const auto discovery_deadline =
    std::chrono::steady_clock::now() + std::chrono::seconds(5);
  while (
    publisher->get_subscription_count() == 0 &&
    std::chrono::steady_clock::now() < discovery_deadline)
  {
    std::this_thread::sleep_for(std::chrono::milliseconds(10));
  }
  ASSERT_GT(publisher->get_subscription_count(), 0U);

  const std::vector<uint8_t> payload{
    0x00U, 0x01U, 0x00U, 0x00U, 0x02U, 0x00U, 0x00U, 0x00U,
    0x41U, 0x00U};
  rclcpp::SerializedMessage serialized(payload.size());
  auto & raw = serialized.get_rcl_serialized_message();
  ASSERT_GE(raw.buffer_capacity, payload.size());
  std::memcpy(raw.buffer, payload.data(), payload.size());
  raw.buffer_length = payload.size();
  publisher->publish(serialized);

  const auto message_wire = ReadSocketWireFrame(client_socket.get());
  const auto message_frame = u2r2::decode_frame(message_wire);
  const auto message = u2r2::parse_v2(message_frame);
  EXPECT_EQ(u2r2::Operation::Message, message.operation);
  EXPECT_EQ(41U, message.contract_id);
  EXPECT_EQ(1U, message.sequence);
  EXPECT_EQ(topic, message.topic);
  EXPECT_EQ(schema, message.schema_name);
  EXPECT_EQ(payload, message_frame.payload);

  write_all(
    client_socket.get(),
    u2r2::encode_frame(
      {
        {"op", "unregister_subscription"},
        {"protocolVersion", 2},
        {"requestId", 3},
        {"sessionId", hello_ack.session_id},
        {"connectionGeneration", hello_ack.connection_generation},
        {"contractId", 41},
      },
      {}));
  const auto removed = u2r2::parse_v2(
    u2r2::decode_frame(ReadSocketWireFrame(client_socket.get())));
  EXPECT_EQ(u2r2::Operation::SubscriptionRemoved, removed.operation);
  EXPECT_EQ("ok", removed.status);
  EXPECT_EQ(41U, removed.contract_id);

  const auto removal_deadline =
    std::chrono::steady_clock::now() + std::chrono::seconds(5);
  while (
    publisher->get_subscription_count() != 0 &&
    std::chrono::steady_clock::now() < removal_deadline)
  {
    std::this_thread::sleep_for(std::chrono::milliseconds(10));
  }
  EXPECT_EQ(0U, publisher->get_subscription_count());

  EXPECT_EQ(0, ShutdownSocketWrite(client_socket.get()));
  server.join();
  ASSERT_EQ(nullptr, server_error);
  EXPECT_EQ(0U, authority.classified_count());

  publisher.reset();
  external.reset();
  EXPECT_TRUE(ros_owner.stop());
  context->shutdown("Phase186-D owned subscription probe complete");
}
