// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/ros2_bridge
// Purpose: Experimental localhost TCP to ROS 2 GenericPublisher sidecar.

#include <arpa/inet.h>
#include <errno.h>
#include <netinet/in.h>
#include <sys/select.h>
#include <sys/socket.h>
#include <unistd.h>

#include <algorithm>
#include <chrono>
#include <cstring>
#include <memory>
#include <stdexcept>
#include <string>
#include <unordered_map>
#include <utility>
#include <vector>

#include <nlohmann/json.hpp>
#include <rclcpp/rclcpp.hpp>
#include <rclcpp/serialized_message.hpp>

namespace
{
constexpr uint16_t kVersion = 1;
constexpr uint16_t kFlags = 0;
constexpr uint32_t kMaxHeaderBytes = 64U * 1024U;
constexpr uint32_t kMaxPayloadBytes = 64U * 1024U * 1024U;
constexpr uint8_t kCdrLittleEndianHeader[4] = {0x00, 0x01, 0x00, 0x00};
constexpr int kHealthProtocolVersion = 1;
constexpr const char * kSidecarName = "unity2foxglove_ros2_bridge";
constexpr const char * kSidecarVersion = "0.1.0";

enum class PayloadFormat
{
  CdrWithEncapsulation,
  CdrBodyOnly
};

struct Options
{
  std::string host = "127.0.0.1";
  int port = 8767;
  PayloadFormat payload_format = PayloadFormat::CdrWithEncapsulation;
};

struct BridgeFrame
{
  std::string topic;
  std::string schema_name;
  std::string encoding;
  std::string profile_name = "Reliable Default";
  std::string reliability = "reliable";
  std::string durability = "volatile";
  int depth = 10;
  uint64_t log_time_ns = 0;
  uint64_t sequence = 0;
  std::vector<uint8_t> payload;
};

struct RawFrame
{
  nlohmann::json header;
  std::vector<uint8_t> payload;
};

uint16_t read_u16_le(const uint8_t * data)
{
  return static_cast<uint16_t>(data[0]) |
    static_cast<uint16_t>(static_cast<uint16_t>(data[1]) << 8);
}

uint32_t read_u32_le(const uint8_t * data)
{
  return static_cast<uint32_t>(data[0]) |
    (static_cast<uint32_t>(data[1]) << 8) |
    (static_cast<uint32_t>(data[2]) << 16) |
    (static_cast<uint32_t>(data[3]) << 24);
}

void write_u16_le(std::vector<uint8_t> & bytes, uint16_t value)
{
  bytes.push_back(static_cast<uint8_t>(value & 0xff));
  bytes.push_back(static_cast<uint8_t>((value >> 8) & 0xff));
}

void write_u32_le(std::vector<uint8_t> & bytes, uint32_t value)
{
  bytes.push_back(static_cast<uint8_t>(value & 0xff));
  bytes.push_back(static_cast<uint8_t>((value >> 8) & 0xff));
  bytes.push_back(static_cast<uint8_t>((value >> 16) & 0xff));
  bytes.push_back(static_cast<uint8_t>((value >> 24) & 0xff));
}

bool has_prefix(const std::string & value, const std::string & prefix)
{
  return value.size() >= prefix.size() &&
    std::equal(prefix.begin(), prefix.end(), value.begin());
}

std::string qos_signature(const BridgeFrame & frame)
{
  return frame.schema_name + "\n" + frame.reliability + "\n" + frame.durability + "\n" +
         std::to_string(frame.depth);
}

rclcpp::QoS make_qos(const BridgeFrame & frame)
{
  auto qos = rclcpp::QoS(rclcpp::KeepLast(static_cast<size_t>(frame.depth)));
  if (frame.reliability == "best_effort") {
    qos.best_effort();
  } else {
    qos.reliable();
  }

  if (frame.durability == "transient_local") {
    qos.transient_local();
  } else {
    qos.durability_volatile();
  }
  return qos;
}

std::string resolve_loopback_ipv4(const std::string & host)
{
  if (host == "localhost") {
    return "127.0.0.1";
  }

  in_addr addr {};
  if (inet_pton(AF_INET, host.c_str(), &addr) != 1) {
    throw std::runtime_error("reject non-loopback host '" + host + "': Phase 94 accepts only IPv4 loopback hosts");
  }

  const uint32_t host_order = ntohl(addr.s_addr);
  if ((host_order >> 24) != 127U) {
    throw std::runtime_error("reject non-loopback host '" + host + "': do not bind 0.0.0.0, LAN, or public interfaces");
  }

  return host;
}

PayloadFormat parse_payload_format(const std::string & value)
{
  if (value == "cdr-with-encapsulation") {
    return PayloadFormat::CdrWithEncapsulation;
  }
  if (value == "cdr-body-only") {
    return PayloadFormat::CdrBodyOnly;
  }
  throw std::runtime_error("unsupported --payload-format: " + value);
}

Options parse_args(const std::vector<std::string> & args)
{
  Options options;
  for (size_t i = 1; i < args.size(); ++i) {
    const std::string & arg = args[i];
    if (arg == "--host" && i + 1 < args.size()) {
      options.host = args[++i];
    } else if (arg == "--port" && i + 1 < args.size()) {
      options.port = std::stoi(args[++i]);
    } else if (arg == "--payload-format" && i + 1 < args.size()) {
      options.payload_format = parse_payload_format(args[++i]);
    } else {
      throw std::runtime_error(
              "usage: unity2foxglove_ros2_bridge --host 127.0.0.1 --port 8767 "
              "--payload-format cdr-with-encapsulation|cdr-body-only");
    }
  }

  resolve_loopback_ipv4(options.host);
  if (options.port <= 0 || options.port > 65535) {
    throw std::runtime_error("--port must be in 1..65535");
  }
  return options;
}

int create_listen_socket(const std::string & host, int port)
{
  const auto resolved = resolve_loopback_ipv4(host);
  const int fd = ::socket(AF_INET, SOCK_STREAM, 0);
  if (fd < 0) {
    throw std::runtime_error("socket() failed");
  }

  int opt = 1;
  ::setsockopt(fd, SOL_SOCKET, SO_REUSEADDR, &opt, sizeof(opt));

  sockaddr_in address {};
  address.sin_family = AF_INET;
  address.sin_port = htons(static_cast<uint16_t>(port));
  if (inet_pton(AF_INET, resolved.c_str(), &address.sin_addr) != 1) {
    ::close(fd);
    throw std::runtime_error("failed to parse loopback bind address");
  }

  if (::bind(fd, reinterpret_cast<sockaddr *>(&address), sizeof(address)) != 0) {
    ::close(fd);
    throw std::runtime_error("bind() failed");
  }
  if (::listen(fd, 4) != 0) {
    ::close(fd);
    throw std::runtime_error("listen() failed");
  }
  return fd;
}

int accept_with_timeout(int listen_fd)
{
  fd_set read_fds;
  FD_ZERO(&read_fds);
  FD_SET(listen_fd, &read_fds);
  timeval timeout {};
  timeout.tv_sec = 0;
  timeout.tv_usec = 250000;
  const int ready = ::select(listen_fd + 1, &read_fds, nullptr, nullptr, &timeout);
  if (ready < 0) {
    if (errno == EINTR) {
      return -1;
    }
    throw std::runtime_error("select() failed");
  }
  if (ready == 0) {
    return -1;
  }

  sockaddr_in client_address {};
  socklen_t length = sizeof(client_address);
  const int client_fd = ::accept(listen_fd, reinterpret_cast<sockaddr *>(&client_address), &length);
  if (client_fd < 0) {
    if (errno == EINTR) {
      return -1;
    }
    throw std::runtime_error("accept() failed");
  }

  timeval receive_timeout {};
  receive_timeout.tv_sec = 5;
  receive_timeout.tv_usec = 0;
  ::setsockopt(client_fd, SOL_SOCKET, SO_RCVTIMEO, &receive_timeout, sizeof(receive_timeout));
  return client_fd;
}

bool read_exact(int fd, std::vector<uint8_t> & buffer, size_t count)
{
  buffer.assign(count, 0);
  size_t offset = 0;
  while (offset < count) {
    const ssize_t received = ::recv(fd, buffer.data() + offset, count - offset, 0);
    if (received == 0) {
      if (offset == 0) {
        return false;
      }
      throw std::runtime_error("short read from bridge client");
    }
    if (received < 0) {
      if (errno == EINTR) {
        continue;
      }
      throw std::runtime_error("socket read failed");
    }
    offset += static_cast<size_t>(received);
  }
  return true;
}

void write_all(int fd, const std::vector<uint8_t> & bytes)
{
  size_t offset = 0;
  while (offset < bytes.size()) {
    const ssize_t sent = ::send(fd, bytes.data() + offset, bytes.size() - offset, 0);
    if (sent <= 0) {
      if (errno == EINTR) {
        continue;
      }
      throw std::runtime_error("socket write failed");
    }
    offset += static_cast<size_t>(sent);
  }
}

void write_u2r2_frame(int fd, const nlohmann::json & header, const std::vector<uint8_t> & payload)
{
  const auto header_text = header.dump();
  if (header_text.empty() || header_text.size() > kMaxHeaderBytes) {
    throw std::runtime_error("health response JSON header length is invalid");
  }
  if (payload.size() > kMaxPayloadBytes) {
    throw std::runtime_error("health response payload length is invalid");
  }

  std::vector<uint8_t> frame;
  frame.reserve(16 + header_text.size() + payload.size());
  frame.push_back('U');
  frame.push_back('2');
  frame.push_back('R');
  frame.push_back('2');
  write_u16_le(frame, kVersion);
  write_u16_le(frame, kFlags);
  write_u32_le(frame, static_cast<uint32_t>(header_text.size()));
  write_u32_le(frame, static_cast<uint32_t>(payload.size()));
  frame.insert(frame.end(), header_text.begin(), header_text.end());
  frame.insert(frame.end(), payload.begin(), payload.end());
  write_all(fd, frame);
}

RawFrame read_raw_frame(int fd)
{
  std::vector<uint8_t> fixed_header;
  if (!read_exact(fd, fixed_header, 16)) {
    throw std::runtime_error("client closed");
  }

  if (fixed_header[0] != 'U' || fixed_header[1] != '2' || fixed_header[2] != 'R' || fixed_header[3] != '2') {
    throw std::runtime_error("reject frame: bad magic");
  }
  if (read_u16_le(&fixed_header[4]) != kVersion) {
    throw std::runtime_error("reject frame: unsupported version");
  }
  if (read_u16_le(&fixed_header[6]) != kFlags) {
    throw std::runtime_error("reject frame: non-zero flags");
  }

  const uint32_t header_length = read_u32_le(&fixed_header[8]);
  const uint32_t payload_length = read_u32_le(&fixed_header[12]);
  if (header_length == 0 || header_length > kMaxHeaderBytes) {
    throw std::runtime_error("reject frame: invalid JSON header length");
  }
  if (payload_length > kMaxPayloadBytes) {
    throw std::runtime_error("reject frame: invalid payload length");
  }

  std::vector<uint8_t> header_bytes;
  if (!read_exact(fd, header_bytes, header_length)) {
    throw std::runtime_error("unexpected EOF while reading JSON header");
  }
  std::vector<uint8_t> payload;
  if (payload_length > 0 && !read_exact(fd, payload, payload_length)) {
    throw std::runtime_error("unexpected EOF while reading payload");
  }

  nlohmann::json header;
  try {
    header = nlohmann::json::parse(header_bytes.begin(), header_bytes.end());
  } catch (const std::exception & ex) {
    throw std::runtime_error(std::string("reject frame: invalid JSON header: ") + ex.what());
  }

  RawFrame raw;
  raw.header = std::move(header);
  raw.payload = std::move(payload);
  return raw;
}

BridgeFrame parse_publish_frame(const RawFrame & raw)
{
  const auto op = raw.header.value("op", "publish");
  if (op != "publish") {
    throw std::runtime_error("reject frame: unsupported op '" + op + "'");
  }
  if (raw.payload.empty()) {
    throw std::runtime_error("reject frame: invalid payload length");
  }

  BridgeFrame frame;
  try {
    frame.topic = raw.header.at("topic").get<std::string>();
    frame.schema_name = raw.header.at("schemaName").get<std::string>();
    frame.encoding = raw.header.at("encoding").get<std::string>();
    frame.log_time_ns = raw.header.at("logTimeNs").get<uint64_t>();
    frame.sequence = raw.header.at("sequence").get<uint64_t>();
    if (raw.header.contains("profileName") && !raw.header["profileName"].is_null()) {
      frame.profile_name = raw.header.at("profileName").get<std::string>();
    }
    if (raw.header.contains("qos") && !raw.header["qos"].is_null()) {
      const auto & qos = raw.header.at("qos");
      frame.reliability = qos.at("reliability").get<std::string>();
      frame.durability = qos.at("durability").get<std::string>();
      frame.depth = qos.at("depth").get<int>();
    }
  } catch (const std::exception & ex) {
    throw std::runtime_error(std::string("reject frame: missing or invalid JSON field: ") + ex.what());
  }
  frame.payload = raw.payload;

  if (frame.topic.empty() || frame.topic[0] != '/') {
    throw std::runtime_error("reject frame: topic must start with /");
  }
  if (!has_prefix(frame.schema_name, "foxglove_msgs/msg/")) {
    throw std::runtime_error("reject frame: schemaName must start with foxglove_msgs/msg/");
  }
  if (frame.encoding != "cdr") {
    throw std::runtime_error("reject frame: encoding must be cdr");
  }
  if (frame.reliability != "reliable" && frame.reliability != "best_effort") {
    throw std::runtime_error("reject frame: qos.reliability must be reliable or best_effort");
  }
  if (frame.durability != "volatile" && frame.durability != "transient_local") {
    throw std::runtime_error("reject frame: qos.durability must be volatile or transient_local");
  }
  if (frame.depth < 1) {
    throw std::runtime_error("reject frame: qos.depth must be >= 1");
  }
  return frame;
}

void write_health_pong_ok(int fd, const std::string & request_id)
{
  nlohmann::json response = {
    {"op", "health_pong"},
    {"requestId", request_id},
    {"protocolVersion", kHealthProtocolVersion},
    {"status", "ok"},
    {"sidecarName", kSidecarName},
    {"sidecarVersion", kSidecarVersion}
  };
  write_u2r2_frame(fd, response, {});
}

void write_health_pong_error(
  int fd,
  const std::string & request_id,
  const std::string & error_code,
  const std::string & message)
{
  nlohmann::json response = {
    {"op", "health_pong"},
    {"requestId", request_id},
    {"protocolVersion", kHealthProtocolVersion},
    {"status", "error"},
    {"errorCode", error_code},
    {"message", message}
  };
  write_u2r2_frame(fd, response, {});
}

void handle_health_ping(int fd, const RawFrame & raw)
{
  const auto request_id = raw.header.value("requestId", std::string());
  if (request_id.empty()) {
    write_health_pong_error(fd, request_id, "malformed_request", "health_ping requires requestId");
    return;
  }

  const auto protocol_version = raw.header.value("protocolVersion", -1);
  if (protocol_version != kHealthProtocolVersion) {
    write_health_pong_error(fd, request_id, "unsupported_protocol", "Unsupported health protocol version");
    return;
  }

  write_health_pong_ok(fd, request_id);
}

std::vector<uint8_t> payload_for_publish(const BridgeFrame & frame, PayloadFormat format)
{
  if (format == PayloadFormat::CdrWithEncapsulation) {
    return frame.payload;
  }

  if (frame.payload.size() < 4 ||
    !std::equal(std::begin(kCdrLittleEndianHeader), std::end(kCdrLittleEndianHeader), frame.payload.begin()))
  {
    throw std::runtime_error("reject frame: cdr-body-only requires 00 01 00 00 encapsulation header");
  }
  return std::vector<uint8_t>(frame.payload.begin() + 4, frame.payload.end());
}

class BridgeNode
{
public:
  explicit BridgeNode(rclcpp::Node::SharedPtr node, PayloadFormat payload_format)
  : node_(std::move(node)), payload_format_(payload_format)
  {
  }

  void publish(const BridgeFrame & frame)
  {
    const auto signature = qos_signature(frame);
    auto topic_signature = topic_signature_.find(frame.topic);
    if (topic_signature != topic_signature_.end() && topic_signature->second != signature) {
      throw std::runtime_error("reject frame: topic reused with different schemaName or QoS");
    }
    topic_signature_[frame.topic] = signature;

    const std::string key = frame.topic + "\n" + signature;
    auto publisher = publishers_[key];
    if (!publisher) {
      auto qos = make_qos(frame);
      publisher = node_->create_generic_publisher(frame.topic, frame.schema_name, qos);
      publishers_[key] = publisher;
      RCLCPP_INFO(
        node_->get_logger(),
        "[unity2foxglove_ros2_bridge] publisher %s %s reliability=%s durability=%s depth=%d",
        frame.topic.c_str(),
        frame.schema_name.c_str(),
        frame.reliability.c_str(),
        frame.durability.c_str(),
        frame.depth);
    }

    const auto payload = payload_for_publish(frame, payload_format_);
    rclcpp::SerializedMessage serialized(payload.size());
    auto & ros_message = serialized.get_rcl_serialized_message();
    if (ros_message.buffer_capacity < payload.size()) {
      throw std::runtime_error("serialized message buffer capacity is too small");
    }
    std::memcpy(ros_message.buffer, payload.data(), payload.size());
    ros_message.buffer_length = payload.size();
    publisher->publish(serialized);

    const auto count = ++counts_[key];
    if (count == 1 || count % 20 == 0) {
      RCLCPP_INFO(
        node_->get_logger(),
        "[unity2foxglove_ros2_bridge] published %s count=%zu",
        frame.topic.c_str(),
        count);
    }
  }

private:
  rclcpp::Node::SharedPtr node_;
  PayloadFormat payload_format_;
  std::unordered_map<std::string, std::string> topic_signature_;
  std::unordered_map<std::string, rclcpp::GenericPublisher::SharedPtr> publishers_;
  std::unordered_map<std::string, size_t> counts_;
};

void process_client(int client_fd, BridgeNode & bridge, const rclcpp::Node::SharedPtr & node)
{
  while (rclcpp::ok()) {
    try {
      const auto raw = read_raw_frame(client_fd);
      const auto op = raw.header.value("op", "publish");
      if (op == "health_ping") {
        handle_health_ping(client_fd, raw);
      } else {
        const auto frame = parse_publish_frame(raw);
        bridge.publish(frame);
      }
      rclcpp::spin_some(node);
    } catch (const std::runtime_error & ex) {
      const std::string message = ex.what();
      if (message == "client closed") {
        break;
      }
      RCLCPP_WARN(node->get_logger(), "[unity2foxglove_ros2_bridge] %s", message.c_str());
      break;
    }
  }
}
}  // namespace

int main(int argc, char ** argv)
{
  auto non_ros_args = rclcpp::init_and_remove_ros_arguments(argc, argv);
  auto node = std::make_shared<rclcpp::Node>("unity2foxglove_ros2_bridge");

  try {
    const auto options = parse_args(non_ros_args);
    const int listen_fd = create_listen_socket(options.host, options.port);
    BridgeNode bridge(node, options.payload_format);

    RCLCPP_INFO(
      node->get_logger(),
      "[unity2foxglove_ros2_bridge] listening on %s:%d",
      options.host.c_str(),
      options.port);

    while (rclcpp::ok()) {
      const int client_fd = accept_with_timeout(listen_fd);
      if (client_fd < 0) {
        rclcpp::spin_some(node);
        continue;
      }

      RCLCPP_INFO(node->get_logger(), "[unity2foxglove_ros2_bridge] client connected");
      process_client(client_fd, bridge, node);
      ::close(client_fd);
      RCLCPP_INFO(node->get_logger(), "[unity2foxglove_ros2_bridge] client disconnected");
    }

    ::close(listen_fd);
  } catch (const std::exception & ex) {
    RCLCPP_ERROR(node->get_logger(), "[unity2foxglove_ros2_bridge] %s", ex.what());
    rclcpp::shutdown();
    return 1;
  }

  rclcpp::shutdown();
  return 0;
}
