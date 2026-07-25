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
#include <cctype>
#include <chrono>
#include <cstring>
#include <functional>
#include <limits>
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
// U2R2 wire limits shared with the Unity frame writer. Keep these constants
// in sync before changing the binary frame envelope.
constexpr uint16_t kVersion = 1;
constexpr uint16_t kFlags = 0;
constexpr uint32_t kMaxHeaderBytes = 64U * 1024U;
constexpr uint32_t kMaxPayloadBytes = 64U * 1024U * 1024U;
constexpr uint8_t kCdrLittleEndianHeader[4] = {0x00, 0x01, 0x00, 0x00};
constexpr auto kReadStallTimeout = std::chrono::seconds(5);
constexpr int kHealthProtocolVersion = 1;
constexpr int kPublisherPreparationProtocolVersion = 1;
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
  std::string profile = "default";
  std::string reliability = "reliable";
  std::string durability = "volatile";
  std::string history = "keep_last";
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

struct PayloadView
{
  const uint8_t * data = nullptr;
  size_t size = 0;
};

class ScopedFd
{
public:
  explicit ScopedFd(int fd = -1) : fd_(fd) {}
  ~ScopedFd()
  {
    reset();
  }

  ScopedFd(const ScopedFd &) = delete;
  ScopedFd & operator=(const ScopedFd &) = delete;

  int get() const
  {
    return fd_;
  }

  bool valid() const
  {
    return fd_ >= 0;
  }

  void reset(int fd = -1)
  {
    if (fd_ >= 0) {
      ::close(fd_);
    }
    fd_ = fd;
  }

private:
  int fd_;
};

class ClientClosedException : public std::runtime_error
{
public:
  ClientClosedException()
  : std::runtime_error("client closed")
  {
  }
};

class ClientReadTimeoutException : public std::runtime_error
{
public:
  explicit ClientReadTimeoutException(size_t expected_bytes)
  : std::runtime_error(
      "bridge client stalled while sending " + std::to_string(expected_bytes) + " bytes")
  {
  }
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

bool is_lower_ascii_letter(char value)
{
  return value >= 'a' && value <= 'z';
}

bool is_upper_ascii_letter(char value)
{
  return value >= 'A' && value <= 'Z';
}

bool is_ascii_digit(char value)
{
  return value >= '0' && value <= '9';
}

bool is_valid_ros2_package_name(const std::string & value)
{
  if (
    value.size() < 2 || value.size() > 255 ||
    !is_lower_ascii_letter(value.front()) || value.back() == '_')
  {
    return false;
  }

  auto previous_was_separator = false;
  for (auto character : value) {
    if (
      !is_lower_ascii_letter(character) &&
      !is_ascii_digit(character) &&
      character != '_')
    {
      return false;
    }
    if (character == '_' && previous_was_separator) {
      return false;
    }
    previous_was_separator = character == '_';
  }
  return true;
}

bool is_valid_ros2_message_name(const std::string & value)
{
  if (value.empty() || value.size() > 255 || !is_upper_ascii_letter(value.front())) {
    return false;
  }

  return std::all_of(
    value.begin() + 1,
    value.end(),
    [](char character) {
      return
        is_lower_ascii_letter(character) ||
        is_upper_ascii_letter(character) ||
        is_ascii_digit(character);
    });
}

bool is_valid_ros2_message_type(const std::string & value)
{
  const auto package_separator = value.find('/');
  if (package_separator == std::string::npos) {
    return false;
  }

  constexpr const char * kMessageNamespace = "/msg/";
  constexpr size_t kMessageNamespaceLength = 5;
  if (
    value.compare(package_separator, kMessageNamespaceLength, kMessageNamespace) != 0)
  {
    return false;
  }

  const auto package_name = value.substr(0, package_separator);
  const auto message_name = value.substr(package_separator + kMessageNamespaceLength);
  return
    is_valid_ros2_package_name(package_name) &&
    is_valid_ros2_message_name(message_name);
}

bool contains_newline(const std::string & value)
{
  return value.find('\n') != std::string::npos || value.find('\r') != std::string::npos;
}

bool is_valid_ros2_topic_name(const std::string & value)
{
  if (value.empty() || value.front() != '/') {
    return false;
  }

  bool token_has_characters = false;
  for (size_t i = 1; i < value.size(); ++i) {
    const unsigned char ch = static_cast<unsigned char>(value[i]);
    if (ch == '/') {
      if (!token_has_characters) {
        return false;
      }
      token_has_characters = false;
      continue;
    }

    if (ch != '_' && std::isalnum(ch) == 0) {
      return false;
    }
    if (!token_has_characters && std::isdigit(ch) != 0) {
      return false;
    }
    token_has_characters = true;
  }

  return token_has_characters;
}

std::string qos_signature(const BridgeFrame & frame)
{
  auto append_field = [](std::string & signature, const std::string & value) {
      signature += std::to_string(value.size());
      signature.push_back(':');
      signature += value;
      signature.push_back('|');
    };

  std::string signature;
  append_field(signature, frame.schema_name);
  append_field(signature, frame.profile);
  append_field(signature, frame.reliability);
  append_field(signature, frame.durability);
  append_field(signature, frame.history);
  append_field(signature, std::to_string(frame.depth));
  return signature;
}

enum class PublisherContractDisposition
{
  CreatePublisher,
  ReusePublisher
};

class PublisherContractConflictException : public std::runtime_error
{
public:
  explicit PublisherContractConflictException(const std::string & message)
  : std::runtime_error(message)
  {
  }
};

class PublisherContractRegistry
{
public:
  PublisherContractDisposition register_or_validate(const BridgeFrame & frame)
  {
    const auto signature = qos_signature(frame);
    const auto registered = topic_signatures_.find(frame.topic);
    if (registered != topic_signatures_.end()) {
      if (registered->second != signature) {
        throw PublisherContractConflictException(
                "reject frame: topic '" + frame.topic +
                "' reused with different schemaName or QoS: was [" +
                registered->second + "] got [" + signature + "]");
      }
      return PublisherContractDisposition::ReusePublisher;
    }

    topic_signatures_.emplace(frame.topic, signature);
    return PublisherContractDisposition::CreatePublisher;
  }

  void rollback_create(const std::string & topic)
  {
    const auto registered = topic_signatures_.find(topic);
    if (registered != topic_signatures_.end()) {
      topic_signatures_.erase(registered);
    }
  }

private:
  std::unordered_map<std::string, std::string> topic_signatures_;
};

int parse_qos_depth(const nlohmann::json & value)
{
  if (value.is_number_unsigned()) {
    const auto depth = value.get<uint64_t>();
    if (depth > static_cast<uint64_t>(std::numeric_limits<int>::max())) {
      throw std::runtime_error("qos.depth is outside the supported integer range");
    }
    return static_cast<int>(depth);
  }

  if (value.is_number_integer()) {
    const auto depth = value.get<int64_t>();
    if (
      depth < static_cast<int64_t>(std::numeric_limits<int>::min()) ||
      depth > static_cast<int64_t>(std::numeric_limits<int>::max()))
    {
      throw std::runtime_error("qos.depth is outside the supported integer range");
    }
    return static_cast<int>(depth);
  }

  throw std::runtime_error("qos.depth must be an integer");
}

void validate_qos_contract(const BridgeFrame & frame)
{
  if (
    frame.profile != "default" &&
    frame.profile != "sensor_data" &&
    frame.profile != "system_default")
  {
    throw std::runtime_error(
            "reject frame: qos.profile must be default, sensor_data, or system_default");
  }
  if (
    frame.reliability != "system_default" &&
    frame.reliability != "reliable" &&
    frame.reliability != "best_effort")
  {
    throw std::runtime_error(
            "reject frame: qos.reliability must be system_default, reliable, or best_effort");
  }
  if (
    frame.durability != "system_default" &&
    frame.durability != "volatile" &&
    frame.durability != "transient_local")
  {
    throw std::runtime_error(
            "reject frame: qos.durability must be system_default, volatile, or transient_local");
  }
  if (
    frame.history != "system_default" &&
    frame.history != "keep_last" &&
    frame.history != "keep_all")
  {
    throw std::runtime_error(
            "reject frame: qos.history must be system_default, keep_last, or keep_all");
  }
  if (frame.history == "keep_last") {
    if (frame.depth < 1) {
      throw std::runtime_error("reject frame: qos.depth must be >= 1 for keep_last");
    }
  } else if (frame.depth != 0) {
    throw std::runtime_error(
            "reject frame: qos.depth must be 0 unless qos.history is keep_last");
  }
}

rmw_qos_profile_t qos_profile_for(const BridgeFrame & frame)
{
  if (frame.profile == "default") {
    return rmw_qos_profile_default;
  }
  if (frame.profile == "sensor_data") {
    return rmw_qos_profile_sensor_data;
  }
  if (frame.profile == "system_default") {
    return rmw_qos_profile_system_default;
  }
  throw std::runtime_error("reject frame: unsupported qos.profile");
}

rmw_qos_reliability_policy_t qos_reliability_for(const BridgeFrame & frame)
{
  if (frame.reliability == "system_default") {
    return RMW_QOS_POLICY_RELIABILITY_SYSTEM_DEFAULT;
  }
  if (frame.reliability == "reliable") {
    return RMW_QOS_POLICY_RELIABILITY_RELIABLE;
  }
  if (frame.reliability == "best_effort") {
    return RMW_QOS_POLICY_RELIABILITY_BEST_EFFORT;
  }
  throw std::runtime_error("reject frame: unsupported qos.reliability");
}

rmw_qos_durability_policy_t qos_durability_for(const BridgeFrame & frame)
{
  if (frame.durability == "system_default") {
    return RMW_QOS_POLICY_DURABILITY_SYSTEM_DEFAULT;
  }
  if (frame.durability == "volatile") {
    return RMW_QOS_POLICY_DURABILITY_VOLATILE;
  }
  if (frame.durability == "transient_local") {
    return RMW_QOS_POLICY_DURABILITY_TRANSIENT_LOCAL;
  }
  throw std::runtime_error("reject frame: unsupported qos.durability");
}

rmw_qos_history_policy_t qos_history_for(const BridgeFrame & frame)
{
  if (frame.history == "system_default") {
    return RMW_QOS_POLICY_HISTORY_SYSTEM_DEFAULT;
  }
  if (frame.history == "keep_last") {
    return RMW_QOS_POLICY_HISTORY_KEEP_LAST;
  }
  if (frame.history == "keep_all") {
    return RMW_QOS_POLICY_HISTORY_KEEP_ALL;
  }
  throw std::runtime_error("reject frame: unsupported qos.history");
}

rclcpp::QoS make_qos(const BridgeFrame & frame)
{
  validate_qos_contract(frame);

  const auto base_profile = qos_profile_for(frame);
  auto qos = rclcpp::QoS(
    rclcpp::QoSInitialization::from_rmw(base_profile),
    base_profile);
  auto & rmw_profile = qos.get_rmw_qos_profile();
  rmw_profile.reliability = qos_reliability_for(frame);
  rmw_profile.durability = qos_durability_for(frame);
  rmw_profile.history = qos_history_for(frame);
  rmw_profile.depth = static_cast<size_t>(frame.depth);
  return qos;
}

std::string resolve_loopback_ipv4(const std::string & host)
{
  if (host == "localhost") {
    return "127.0.0.1";
  }

  in_addr addr {};
  if (inet_pton(AF_INET, host.c_str(), &addr) != 1) {
    throw std::runtime_error("reject non-loopback host '" + host + "': bridge accepts only IPv4 loopback hosts");
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
      try {
        options.port = std::stoi(args[++i]);
      } catch (const std::exception &) {
        throw std::runtime_error("--port must be an integer in 1..65535");
      }
    } else if (arg == "--payload-format" && i + 1 < args.size()) {
      options.payload_format = parse_payload_format(args[++i]);
    } else if (arg == "--host" || arg == "--port" || arg == "--payload-format") {
      throw std::runtime_error("missing value for " + arg);
    } else {
      throw std::runtime_error(
              "usage: unity2foxglove_ros2_bridge --host 127.0.0.1 --port 8767 "
              "--payload-format cdr-with-encapsulation|cdr-body-only");
    }
  }

  options.host = resolve_loopback_ipv4(options.host);
  if (options.port <= 0 || options.port > 65535) {
    throw std::runtime_error("--port must be in 1..65535");
  }
  return options;
}

int create_listen_socket(const std::string & host, int port, const rclcpp::Logger & logger)
{
  const auto resolved = resolve_loopback_ipv4(host);
  const int fd = ::socket(AF_INET, SOCK_STREAM, 0);
  if (fd < 0) {
    throw std::runtime_error("socket() failed");
  }

  int opt = 1;
  if (::setsockopt(fd, SOL_SOCKET, SO_REUSEADDR, &opt, sizeof(opt)) != 0) {
    RCLCPP_WARN(
      logger,
      "[unity2foxglove_ros2_bridge] SO_REUSEADDR failed, rapid restart may fail: %s",
      std::strerror(errno));
  }

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
  receive_timeout.tv_sec = 0;
  receive_timeout.tv_usec = 250000;
  ::setsockopt(client_fd, SOL_SOCKET, SO_RCVTIMEO, &receive_timeout, sizeof(receive_timeout));
  timeval send_timeout = receive_timeout;
  ::setsockopt(client_fd, SOL_SOCKET, SO_SNDTIMEO, &send_timeout, sizeof(send_timeout));
  return client_fd;
}

bool read_exact(
  int fd,
  std::vector<uint8_t> & buffer,
  size_t count,
  const rclcpp::Node::SharedPtr & node)
{
  buffer.assign(count, 0);
  size_t offset = 0;
  auto stalled_since = std::chrono::steady_clock::time_point {};
  while (offset < count) {
    const ssize_t received = ::recv(fd, buffer.data() + offset, count - offset, 0);
    if (received == 0) {
      if (offset == 0) {
        return false;
      }
      throw ClientClosedException();
    }
    if (received < 0) {
      if (errno == EINTR) {
        continue;
      }
      if (errno == EAGAIN || errno == EWOULDBLOCK) {
        const auto now = std::chrono::steady_clock::now();
        if (stalled_since == std::chrono::steady_clock::time_point {}) {
          stalled_since = now;
        }
        if (now - stalled_since >= kReadStallTimeout) {
          throw ClientReadTimeoutException(count);
        }
        rclcpp::spin_some(node);
        continue;
      }
      throw std::runtime_error("socket read failed");
    }
    offset += static_cast<size_t>(received);
    stalled_since = std::chrono::steady_clock::time_point {};
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
      if (errno == EAGAIN || errno == EWOULDBLOCK) {
        fd_set write_fds;
        FD_ZERO(&write_fds);
        FD_SET(fd, &write_fds);
        timeval timeout {};
        timeout.tv_sec = 0;
        timeout.tv_usec = 250000;
        const int ready = ::select(fd + 1, nullptr, &write_fds, nullptr, &timeout);
        if (ready > 0) {
          continue;
        }
        if (ready < 0 && errno == EINTR) {
          continue;
        }
        throw std::runtime_error("socket write timed out");
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
    throw std::runtime_error("U2R2 response JSON header length is invalid");
  }
  if (payload.size() > kMaxPayloadBytes) {
    throw std::runtime_error("U2R2 response payload length is invalid");
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

RawFrame read_raw_frame(int fd, const rclcpp::Node::SharedPtr & node)
{
  std::vector<uint8_t> fixed_header;
  if (!read_exact(fd, fixed_header, 16, node)) {
    throw ClientClosedException();
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
  if (!read_exact(fd, header_bytes, header_length, node)) {
    throw ClientClosedException();
  }
  std::vector<uint8_t> payload;
  if (payload_length > 0 && !read_exact(fd, payload, payload_length, node)) {
    throw ClientClosedException();
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
    // Maintained pre-184 Ros2BridgePublisher callers do not carry a QoS
    // object. Preserve their established portable Default contract while
    // requiring every field whenever an explicit QoS object is present.
    if (raw.header.contains("qos") && !raw.header["qos"].is_null()) {
      const auto & qos = raw.header.at("qos");
      if (!qos.is_object()) {
        throw std::runtime_error("qos must be an object");
      }
      frame.profile = qos.at("profile").get<std::string>();
      frame.reliability = qos.at("reliability").get<std::string>();
      frame.durability = qos.at("durability").get<std::string>();
      frame.history = qos.at("history").get<std::string>();
      frame.depth = parse_qos_depth(qos.at("depth"));
    }
  } catch (const std::exception & ex) {
    throw std::runtime_error(std::string("reject frame: missing or invalid JSON field: ") + ex.what());
  }
  frame.payload = raw.payload;

  if (frame.topic.empty() || frame.topic[0] != '/') {
    throw std::runtime_error("reject frame: topic must start with /");
  }
  if (contains_newline(frame.topic)) {
    throw std::runtime_error("reject frame: topic must not contain newline");
  }
  if (!is_valid_ros2_topic_name(frame.topic)) {
    throw std::runtime_error("reject frame: topic contains invalid ROS 2 characters");
  }
  if (!is_valid_ros2_message_type(frame.schema_name)) {
    throw std::runtime_error(
            "reject frame: schemaName must use canonical ROS 2 package/msg/Type grammar");
  }
  if (frame.encoding != "cdr") {
    throw std::runtime_error("reject frame: encoding must be cdr");
  }
  validate_qos_contract(frame);
  return frame;
}

struct PublisherPreparationRequest
{
  std::string request_id;
  int protocol_version = 0;
  BridgeFrame frame;
};

PublisherPreparationRequest parse_prepare_publisher_frame(const RawFrame & raw)
{
  if (!raw.payload.empty()) {
    throw std::runtime_error("reject frame: prepare_publisher payload must be empty");
  }
  if (
    !raw.header.contains("qos") ||
    raw.header["qos"].is_null())
  {
    throw std::runtime_error(
            "reject frame: prepare_publisher requires an explicit complete qos object");
  }

  PublisherPreparationRequest request;
  nlohmann::json protocol_version;
  try {
    const auto op = raw.header.at("op").get<std::string>();
    if (op != "prepare_publisher") {
      throw std::runtime_error("op must be prepare_publisher");
    }
    request.request_id = raw.header.at("requestId").get<std::string>();
    protocol_version = raw.header.at("protocolVersion");
  } catch (const std::exception & ex) {
    throw std::runtime_error(
            std::string("reject frame: missing or invalid prepare_publisher field: ") + ex.what());
  }
  if (request.request_id.empty() || contains_newline(request.request_id)) {
    throw std::runtime_error(
            "reject frame: prepare_publisher requestId must be non-empty and contain no newline");
  }
  if (
    !protocol_version.is_number_integer() &&
    !protocol_version.is_number_unsigned())
  {
    throw std::runtime_error(
            "reject frame: prepare_publisher protocolVersion must be a JSON integer");
  }
  const auto supported_protocol =
    protocol_version.is_number_unsigned()
    ? protocol_version.get<uint64_t>() ==
    static_cast<uint64_t>(kPublisherPreparationProtocolVersion)
    : protocol_version.get<int64_t>() ==
    static_cast<int64_t>(kPublisherPreparationProtocolVersion);
  if (!supported_protocol) {
    throw std::runtime_error("reject frame: unsupported prepare_publisher protocol version");
  }
  request.protocol_version = kPublisherPreparationProtocolVersion;

  // Reuse the maintained publish contract parser so topic/type/encoding and
  // every QoS axis have exactly one validation path. The synthetic payload
  // and counters exist only to satisfy the legacy publish envelope fields.
  auto publish_contract = raw;
  publish_contract.header["logTimeNs"] = 0;
  publish_contract.header["sequence"] = 0;
  publish_contract.payload = {0};
  request.frame = parse_publish_frame(publish_contract);
  request.frame.payload.clear();
  return request;
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
  const auto request_id_it = raw.header.find("requestId");
  if (request_id_it == raw.header.end() || !request_id_it->is_string()) {
    write_health_pong_error(
      fd,
      std::string(),
      "malformed_request",
      "health_ping requires a string requestId");
    return;
  }
  const auto request_id = request_id_it->get<std::string>();
  if (request_id.empty()) {
    write_health_pong_error(
      fd,
      request_id,
      "malformed_request",
      "health_ping requires a non-empty requestId");
    return;
  }

  const auto protocol_it = raw.header.find("protocolVersion");
  if (protocol_it == raw.header.end() ||
    (!protocol_it->is_number_integer() && !protocol_it->is_number_unsigned()))
  {
    write_health_pong_error(
      fd,
      request_id,
      "malformed_request",
      "health_ping requires an integer protocolVersion");
    return;
  }
  if (*protocol_it != kHealthProtocolVersion) {
    write_health_pong_error(fd, request_id, "unsupported_protocol", "Unsupported health protocol version");
    return;
  }

  write_health_pong_ok(fd, request_id);
}

PayloadView payload_for_publish(
  const BridgeFrame & frame,
  PayloadFormat format,
  std::vector<uint8_t> & scratch)
{
  if (format == PayloadFormat::CdrWithEncapsulation) {
    scratch.clear();
    return PayloadView{frame.payload.data(), frame.payload.size()};
  }

  if (frame.payload.size() >= 4 &&
    std::equal(std::begin(kCdrLittleEndianHeader), std::end(kCdrLittleEndianHeader), frame.payload.begin()))
  {
    throw std::runtime_error("reject frame: cdr-body-only expects payload without CDR encapsulation header");
  }

  scratch.assign(std::begin(kCdrLittleEndianHeader), std::end(kCdrLittleEndianHeader));
  scratch.insert(scratch.end(), frame.payload.begin(), frame.payload.end());
  return PayloadView{scratch.data(), scratch.size()};
}

using SerializedPublishCallback =
  std::function<void(const rclcpp::SerializedMessage &)>;
using GenericPublisherFactory =
  std::function<SerializedPublishCallback(
      const std::string &,
      const std::string &,
      const rclcpp::QoS &)>;

class BridgeNode
{
public:
  explicit BridgeNode(rclcpp::Node::SharedPtr node, PayloadFormat payload_format)
  : node_(std::move(node)), payload_format_(payload_format)
  {
    if (!node_) {
      throw std::invalid_argument("bridge node is required");
    }

    const auto publisher_node = node_;
    publisher_factory_ =
      [publisher_node](
      const std::string & topic,
      const std::string & message_type,
      const rclcpp::QoS & qos)
      {
        // create_generic_publisher performs the rosidl_typesupport_cpp lookup
        // for the exact canonical message type before returning a publisher.
        auto publisher =
          publisher_node->create_generic_publisher(topic, message_type, qos);
        return [publisher = std::move(publisher)](
          const rclcpp::SerializedMessage & message)
          {
            publisher->publish(message);
          };
      };
  }

  BridgeNode(PayloadFormat payload_format, GenericPublisherFactory publisher_factory)
  : payload_format_(payload_format), publisher_factory_(std::move(publisher_factory))
  {
    if (!publisher_factory_) {
      throw std::invalid_argument("generic publisher factory is required");
    }
  }

  PublisherContractDisposition prepare(const BridgeFrame & frame)
  {
    const auto disposition = publisher_contracts_.register_or_validate(frame);
    auto publisher_it = publishers_.find(frame.topic);
    if (disposition == PublisherContractDisposition::CreatePublisher) {
      try {
        auto qos = make_qos(frame);
        auto publisher = publisher_factory_(frame.topic, frame.schema_name, qos);
        if (!publisher) {
          throw std::runtime_error(
                  "generic publisher factory returned no publisher for type '" +
                  frame.schema_name + "'");
        }
        const auto inserted = publishers_.emplace(frame.topic, std::move(publisher));
        if (!inserted.second) {
          throw std::runtime_error(
                  "bridge publisher registry is inconsistent for topic '" + frame.topic + "'");
        }
        publisher_it = inserted.first;
      } catch (...) {
        publisher_contracts_.rollback_create(frame.topic);
        throw;
      }
      if (node_) {
        RCLCPP_INFO(
          node_->get_logger(),
          "[unity2foxglove_ros2_bridge] publisher %s %s "
          "profile=%s reliability=%s durability=%s history=%s depth=%d",
          frame.topic.c_str(),
          frame.schema_name.c_str(),
          frame.profile.c_str(),
          frame.reliability.c_str(),
          frame.durability.c_str(),
          frame.history.c_str(),
          frame.depth);
      }
    } else if (publisher_it == publishers_.end()) {
      throw std::runtime_error(
              "bridge publisher registry is inconsistent for topic '" + frame.topic + "'");
    }

    return disposition;
  }

  void publish(const BridgeFrame & frame)
  {
    prepare(frame);
    const auto publisher_it = publishers_.find(frame.topic);
    if (publisher_it == publishers_.end()) {
      throw std::runtime_error(
              "bridge publisher registry is inconsistent for topic '" + frame.topic + "'");
    }

    const auto payload = payload_for_publish(frame, payload_format_, payload_scratch_);
    rclcpp::SerializedMessage serialized(payload.size);
    auto & ros_message = serialized.get_rcl_serialized_message();
    if (ros_message.buffer_capacity < payload.size) {
      throw std::runtime_error("serialized message buffer capacity is too small");
    }
    std::memcpy(ros_message.buffer, payload.data, payload.size);
    ros_message.buffer_length = payload.size;
    publisher_it->second(serialized);

    const auto count = ++counts_[frame.topic];
    if (node_ && (count == 1 || count % 20 == 0)) {
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
  GenericPublisherFactory publisher_factory_;
  PublisherContractRegistry publisher_contracts_;
  std::unordered_map<std::string, SerializedPublishCallback> publishers_;
  std::unordered_map<std::string, size_t> counts_;
  std::vector<uint8_t> payload_scratch_;
};

nlohmann::json publisher_ready_ok(const std::string & request_id)
{
  return {
    {"op", "publisher_ready"},
    {"requestId", request_id},
    {"protocolVersion", kPublisherPreparationProtocolVersion},
    {"status", "ok"}
  };
}

nlohmann::json publisher_ready_error(
  const std::string & request_id,
  const std::string & error_code,
  const std::string & message)
{
  return {
    {"op", "publisher_ready"},
    {"requestId", request_id},
    {"protocolVersion", kPublisherPreparationProtocolVersion},
    {"status", "error"},
    {"errorCode", error_code},
    {"message", message}
  };
}

nlohmann::json handle_prepare_publisher_frame(const RawFrame & raw, BridgeNode & bridge)
{
  std::string request_id;
  if (
    raw.header.is_object() &&
    raw.header.contains("requestId") &&
    raw.header["requestId"].is_string())
  {
    request_id = raw.header["requestId"].get<std::string>();
  }

  PublisherPreparationRequest request;
  try {
    request = parse_prepare_publisher_frame(raw);
  } catch (const std::exception & ex) {
    return publisher_ready_error(request_id, "invalid_contract", ex.what());
  }

  try {
    bridge.prepare(request.frame);
    return publisher_ready_ok(request.request_id);
  } catch (const PublisherContractConflictException & ex) {
    return publisher_ready_error(
      request.request_id,
      "publisher_contract_conflict",
      ex.what());
  } catch (const std::exception & ex) {
    return publisher_ready_error(
      request.request_id,
      "publisher_unavailable",
      ex.what());
  }
}

void process_client(
  int client_fd,
  BridgeNode & bridge,
  const rclcpp::Node::SharedPtr & node)
{
  if (!node) {
    throw std::invalid_argument("bridge session node is required");
  }

  const auto context = node->get_node_base_interface()->get_context();
  while (rclcpp::ok(context)) {
    try {
      const auto raw = read_raw_frame(client_fd, node);
      if (!raw.header.contains("op") || !raw.header["op"].is_string()) {
        throw std::runtime_error("reject frame: missing or invalid op");
      }
      const auto op = raw.header.at("op").get<std::string>();
      if (op == "health_ping") {
        handle_health_ping(client_fd, raw);
      } else if (op == "prepare_publisher") {
        const auto response = handle_prepare_publisher_frame(raw, bridge);
        write_u2r2_frame(client_fd, response, {});
      } else if (op == "publish") {
        const auto frame = parse_publish_frame(raw);
        try {
          bridge.publish(frame);
        } catch (const std::exception & ex) {
          RCLCPP_WARN(
            node->get_logger(),
            "[unity2foxglove_ros2_bridge] dropped publish frame for topic '%s': %s",
            frame.topic.c_str(),
            ex.what());
          continue;
        }
      } else {
        throw std::runtime_error("reject frame: unsupported op '" + op + "'");
      }
      rclcpp::spin_some(node);
    } catch (const ClientClosedException &) {
      break;
    } catch (const ClientReadTimeoutException & ex) {
      RCLCPP_WARN(node->get_logger(), "[unity2foxglove_ros2_bridge] %s", ex.what());
      break;
    } catch (const std::exception & ex) {
      RCLCPP_WARN(node->get_logger(), "[unity2foxglove_ros2_bridge] %s", ex.what());
      break;
    } catch (...) {
      RCLCPP_WARN(
        node->get_logger(),
        "[unity2foxglove_ros2_bridge] client session failed with an unknown exception");
      break;
    }
  }
}

void process_client(
  int client_fd,
  const rclcpp::Node::SharedPtr & node,
  PayloadFormat payload_format)
{
  BridgeNode bridge(node, payload_format);
  process_client(client_fd, bridge, node);
}
}  // namespace

#ifndef UNITY2FOXGLOVE_ROS2_BRIDGE_TESTING
int main(int argc, char ** argv)
{
  auto non_ros_args = rclcpp::init_and_remove_ros_arguments(argc, argv);
  auto node = std::make_shared<rclcpp::Node>("unity2foxglove_ros2_bridge");

  try {
    const auto options = parse_args(non_ros_args);
    ScopedFd listen_fd(create_listen_socket(options.host, options.port, node->get_logger()));

    RCLCPP_INFO(
      node->get_logger(),
      "[unity2foxglove_ros2_bridge] listening on %s:%d",
      options.host.c_str(),
      options.port);

    while (rclcpp::ok()) {
      ScopedFd client_fd(accept_with_timeout(listen_fd.get()));
      if (!client_fd.valid()) {
        rclcpp::spin_some(node);
        continue;
      }

      RCLCPP_INFO(node->get_logger(), "[unity2foxglove_ros2_bridge] client connected");
      try {
        process_client(client_fd.get(), node, options.payload_format);
      } catch (const std::exception & ex) {
        RCLCPP_WARN(
          node->get_logger(),
          "[unity2foxglove_ros2_bridge] client session escaped: %s",
          ex.what());
      } catch (...) {
        RCLCPP_WARN(
          node->get_logger(),
          "[unity2foxglove_ros2_bridge] client session escaped with an unknown exception");
      }
      RCLCPP_INFO(node->get_logger(), "[unity2foxglove_ros2_bridge] client disconnected");
    }
  } catch (const std::exception & ex) {
    RCLCPP_ERROR(node->get_logger(), "[unity2foxglove_ros2_bridge] %s", ex.what());
    rclcpp::shutdown();
    return 1;
  }

  rclcpp::shutdown();
  return 0;
}
#endif
