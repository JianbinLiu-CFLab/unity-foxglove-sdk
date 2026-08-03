// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/ros2_bridge
// Purpose: Experimental localhost TCP to ROS 2 GenericPublisher sidecar.

#ifdef _WIN32
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <winsock2.h>
#include <ws2tcpip.h>
#else
#include <arpa/inet.h>
#include <errno.h>
#include <fcntl.h>
#include <netinet/in.h>
#include <sys/select.h>
#include <sys/socket.h>
#include <unistd.h>
#endif

#include <algorithm>
#include <array>
#include <atomic>
#include <cctype>
#include <chrono>
#include <cstddef>
#include <cstdio>
#include <cstring>
#include <functional>
#include <limits>
#include <memory>
#include <mutex>
#include <optional>
#include <stdexcept>
#include <string>
#include <thread>
#include <unordered_map>
#include <utility>
#include <vector>

#include <nlohmann/json.hpp>
#include <rclcpp/rclcpp.hpp>
#include <rclcpp/contexts/default_context.hpp>
#include <rclcpp/generic_subscription.hpp>
#include <rclcpp/serialized_message.hpp>
#include <rclcpp/typesupport_helpers.hpp>
#include <rclcpp/version.h>

#include "unity2foxglove_ros2_bridge/bridge_lifecycle.hpp"
#include "unity2foxglove_ros2_bridge/bridge_origin.hpp"
#include "unity2foxglove_ros2_bridge/bridge_process.hpp"
#include "unity2foxglove_ros2_bridge/bridge_session.hpp"

namespace
{
namespace bridge_runtime = unity2foxglove::ros2_bridge::runtime;
namespace u2r2 = unity2foxglove::ros2_bridge::u2r2;

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
constexpr uint64_t kControlResponseReservationBytes = 4096;
constexpr size_t kMaximumResponseErrorBytes = 512;

#ifdef _WIN32
using SocketHandle = SOCKET;
using SocketLength = int;
constexpr SocketHandle kInvalidSocket = INVALID_SOCKET;
#else
using SocketHandle = int;
using SocketLength = socklen_t;
constexpr SocketHandle kInvalidSocket = -1;
#endif

int last_socket_error()
{
#ifdef _WIN32
  return WSAGetLastError();
#else
  return errno;
#endif
}

bool socket_error_is_interrupted(int error)
{
#ifdef _WIN32
  return error == WSAEINTR;
#else
  return error == EINTR;
#endif
}

bool socket_error_is_retryable_timeout(int error)
{
#ifdef _WIN32
  return error == WSAEWOULDBLOCK || error == WSAETIMEDOUT;
#else
  return error == EAGAIN || error == EWOULDBLOCK;
#endif
}

std::string socket_error_text(int error)
{
#ifdef _WIN32
  return "WinSock error " + std::to_string(error);
#else
  return std::strerror(error);
#endif
}

void close_socket(SocketHandle socket)
{
  if (socket == kInvalidSocket) {
    return;
  }
#ifdef _WIN32
  ::closesocket(socket);
#else
  ::close(socket);
#endif
}

void shutdown_socket_both(SocketHandle socket)
{
  if (socket == kInvalidSocket) {
    return;
  }
#ifdef _WIN32
  (void)::shutdown(socket, SD_BOTH);
#else
  (void)::shutdown(socket, SHUT_RDWR);
#endif
}

int socket_select_width(SocketHandle socket)
{
#ifdef _WIN32
  (void)socket;
  return 0;
#else
  return socket + 1;
#endif
}

int set_socket_option(
  SocketHandle socket,
  int level,
  int option,
  const void * value,
  SocketLength length)
{
#ifdef _WIN32
  return ::setsockopt(
    socket,
    level,
    option,
    reinterpret_cast<const char *>(value),
    length);
#else
  return ::setsockopt(socket, level, option, value, length);
#endif
}

std::ptrdiff_t receive_socket(SocketHandle socket, uint8_t * data, size_t size)
{
  const auto bounded = static_cast<int>(
    std::min(size, static_cast<size_t>(std::numeric_limits<int>::max())));
#ifdef _WIN32
  return static_cast<std::ptrdiff_t>(
    ::recv(socket, reinterpret_cast<char *>(data), bounded, 0));
#else
  return static_cast<std::ptrdiff_t>(::recv(socket, data, static_cast<size_t>(bounded), 0));
#endif
}

std::ptrdiff_t send_socket(SocketHandle socket, const uint8_t * data, size_t size)
{
  const auto bounded = static_cast<int>(
    std::min(size, static_cast<size_t>(std::numeric_limits<int>::max())));
#ifdef _WIN32
  return static_cast<std::ptrdiff_t>(
    ::send(socket, reinterpret_cast<const char *>(data), bounded, 0));
#else
  return static_cast<std::ptrdiff_t>(::send(socket, data, static_cast<size_t>(bounded), 0));
#endif
}

void configure_client_timeouts(SocketHandle socket)
{
#ifdef _WIN32
  const DWORD timeout_ms = 250;
  set_socket_option(
    socket,
    SOL_SOCKET,
    SO_RCVTIMEO,
    &timeout_ms,
    static_cast<SocketLength>(sizeof(timeout_ms)));
  set_socket_option(
    socket,
    SOL_SOCKET,
    SO_SNDTIMEO,
    &timeout_ms,
    static_cast<SocketLength>(sizeof(timeout_ms)));
#else
  timeval timeout {};
  timeout.tv_sec = 0;
  timeout.tv_usec = 250000;
  set_socket_option(
    socket,
    SOL_SOCKET,
    SO_RCVTIMEO,
    &timeout,
    static_cast<SocketLength>(sizeof(timeout)));
  set_socket_option(
    socket,
    SOL_SOCKET,
    SO_SNDTIMEO,
    &timeout,
    static_cast<SocketLength>(sizeof(timeout)));
#endif
}

#ifdef _WIN32
class WinsockRuntime
{
public:
  WinsockRuntime()
  {
    WSADATA data {};
    const auto result = WSAStartup(MAKEWORD(2, 2), &data);
    if (result != 0) {
      throw std::runtime_error("WSAStartup failed: " + socket_error_text(result));
    }
    initialized_ = true;
  }

  ~WinsockRuntime()
  {
    if (initialized_) {
      WSACleanup();
    }
  }

  WinsockRuntime(const WinsockRuntime &) = delete;
  WinsockRuntime & operator=(const WinsockRuntime &) = delete;

private:
  bool initialized_ = false;
};
#endif

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

struct AccountedWireFrame
{
  std::vector<uint8_t> bytes;
  u2r2::ByteLease transient_lease;
  u2r2::ByteLease read_lease;
};

struct PayloadView
{
  const uint8_t * data = nullptr;
  size_t size = 0;
};

class ScopedFd
{
public:
  explicit ScopedFd(SocketHandle fd = kInvalidSocket) : fd_(fd) {}
  ~ScopedFd()
  {
    reset();
  }

  ScopedFd(const ScopedFd &) = delete;
  ScopedFd & operator=(const ScopedFd &) = delete;

  SocketHandle get() const
  {
    return fd_;
  }

  bool valid() const
  {
    return fd_ != kInvalidSocket;
  }

  SocketHandle release()
  {
    const auto released = fd_;
    fd_ = kInvalidSocket;
    return released;
  }

  void reset(SocketHandle fd = kInvalidSocket)
  {
    if (valid()) {
      close_socket(fd_);
    }
    fd_ = fd;
  }

private:
  SocketHandle fd_;
};

class ScopedNonBlockingSocket final
{
public:
  explicit ScopedNonBlockingSocket(SocketHandle socket)
  : socket_(socket)
  {
#ifdef _WIN32
    u_long enabled = 1;
    if (::ioctlsocket(socket_, FIONBIO, &enabled) != 0) {
      throw std::runtime_error(
              "failed to enter non-blocking U2R2 write mode: " +
              socket_error_text(last_socket_error()));
    }
#else
    original_flags_ = ::fcntl(socket_, F_GETFL, 0);
    if (
      original_flags_ < 0 ||
      ::fcntl(socket_, F_SETFL, original_flags_ | O_NONBLOCK) != 0)
    {
      throw std::runtime_error(
              "failed to enter non-blocking U2R2 write mode: " +
              socket_error_text(last_socket_error()));
    }
#endif
    active_ = true;
  }

  ~ScopedNonBlockingSocket()
  {
    if (!active_) {
      return;
    }
#ifdef _WIN32
    u_long disabled = 0;
    (void)::ioctlsocket(socket_, FIONBIO, &disabled);
#else
    (void)::fcntl(socket_, F_SETFL, original_flags_);
#endif
  }

  ScopedNonBlockingSocket(const ScopedNonBlockingSocket &) = delete;
  ScopedNonBlockingSocket & operator=(const ScopedNonBlockingSocket &) =
    delete;

private:
  SocketHandle socket_;
#ifndef _WIN32
  int original_flags_{0};
#endif
  bool active_{false};
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
      "bridge client stalled while receiving " + std::to_string(expected_bytes) + " bytes")
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

SocketHandle create_listen_socket(
  const std::string & host,
  int port,
  const rclcpp::Logger & logger)
{
  const auto resolved = resolve_loopback_ipv4(host);
  const SocketHandle fd = ::socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
  if (fd == kInvalidSocket) {
    throw std::runtime_error(
            "socket() failed: " + socket_error_text(last_socket_error()));
  }

  int opt = 1;
#ifdef _WIN32
  if (set_socket_option(
      fd,
      SOL_SOCKET,
      SO_EXCLUSIVEADDRUSE,
      &opt,
      static_cast<SocketLength>(sizeof(opt))) != 0)
  {
    const auto error = last_socket_error();
    RCLCPP_WARN(
      logger,
      "[unity2foxglove_ros2_bridge] SO_EXCLUSIVEADDRUSE failed: %s",
      socket_error_text(error).c_str());
  }
#else
  if (set_socket_option(
      fd,
      SOL_SOCKET,
      SO_REUSEADDR,
      &opt,
      static_cast<SocketLength>(sizeof(opt))) != 0)
  {
    const auto error = last_socket_error();
    RCLCPP_WARN(
      logger,
      "[unity2foxglove_ros2_bridge] SO_REUSEADDR failed, rapid restart may fail: %s",
      socket_error_text(error).c_str());
  }
#endif

  sockaddr_in address {};
  address.sin_family = AF_INET;
  address.sin_port = htons(static_cast<uint16_t>(port));
  if (inet_pton(AF_INET, resolved.c_str(), &address.sin_addr) != 1) {
    close_socket(fd);
    throw std::runtime_error("failed to parse loopback bind address");
  }

  if (::bind(fd, reinterpret_cast<sockaddr *>(&address), sizeof(address)) != 0) {
    const auto error = last_socket_error();
    close_socket(fd);
    throw std::runtime_error("bind() failed: " + socket_error_text(error));
  }
  if (::listen(fd, 4) != 0) {
    const auto error = last_socket_error();
    close_socket(fd);
    throw std::runtime_error("listen() failed: " + socket_error_text(error));
  }
  return fd;
}

struct AcceptedSocket
{
  SocketHandle fd{kInvalidSocket};
  sockaddr_storage peer{};
  SocketLength peer_length{0};

  bool valid() const noexcept
  {
    return fd != kInvalidSocket;
  }
};

AcceptedSocket accept_with_timeout(SocketHandle listen_fd)
{
  fd_set read_fds;
  FD_ZERO(&read_fds);
  FD_SET(listen_fd, &read_fds);
  timeval timeout {};
  timeout.tv_sec = 0;
  timeout.tv_usec = 250000;
  const int ready = ::select(
    socket_select_width(listen_fd),
    &read_fds,
    nullptr,
    nullptr,
    &timeout);
  if (ready < 0) {
    const auto error = last_socket_error();
    if (socket_error_is_interrupted(error)) {
      return {};
    }
    throw std::runtime_error("select() failed: " + socket_error_text(error));
  }
  if (ready == 0) {
    return {};
  }

  AcceptedSocket accepted;
  accepted.peer_length =
    static_cast<SocketLength>(sizeof(accepted.peer));
  accepted.fd = ::accept(
    listen_fd,
    reinterpret_cast<sockaddr *>(&accepted.peer),
    &accepted.peer_length);
  if (accepted.fd == kInvalidSocket) {
    const auto error = last_socket_error();
    if (socket_error_is_interrupted(error)) {
      return {};
    }
    throw std::runtime_error("accept() failed: " + socket_error_text(error));
  }

  configure_client_timeouts(accepted.fd);
  return accepted;
}

using RosContextOk = std::function<bool()>;

struct AccountedReadClock
{
  std::chrono::steady_clock::time_point started{
    std::chrono::steady_clock::now()};
  std::optional<std::chrono::steady_clock::time_point> first_byte;
};

uint64_t elapsed_milliseconds(
  std::chrono::steady_clock::time_point since)
{
  return static_cast<uint64_t>(
    std::chrono::duration_cast<std::chrono::milliseconds>(
      std::chrono::steady_clock::now() - since).count());
}

void enforce_accounted_read_timeout(
  const u2r2::ProtocolLimits & limits,
  u2r2::TimeoutKind idle_timeout,
  const AccountedReadClock & clock)
{
  u2r2::PureSessionLifecycle lifecycle(limits);
  lifecycle.timeout(
    idle_timeout,
    elapsed_milliseconds(clock.started));
  if (clock.first_byte) {
    lifecycle.timeout(
      u2r2::TimeoutKind::partial_frame,
      elapsed_milliseconds(*clock.first_byte));
  }
}

bool read_exact_accounted(
  SocketHandle fd,
  uint8_t * destination,
  size_t count,
  const RosContextOk & context_ok,
  const u2r2::ProtocolLimits & limits,
  u2r2::TimeoutKind idle_timeout,
  AccountedReadClock & clock)
{
  size_t offset = 0;
  while (offset < count) {
    if (!context_ok()) {
      throw ClientClosedException();
    }
    enforce_accounted_read_timeout(
      limits,
      idle_timeout,
      clock);
    const auto received =
      receive_socket(fd, destination + offset, count - offset);
    if (received == 0) {
      if (offset == 0 && !clock.first_byte) {
        return false;
      }
      throw ClientClosedException();
    }
    if (received < 0) {
      const auto error = last_socket_error();
      if (socket_error_is_interrupted(error)) {
        continue;
      }
      if (socket_error_is_retryable_timeout(error)) {
        enforce_accounted_read_timeout(
          limits,
          idle_timeout,
          clock);
        continue;
      }
      throw std::runtime_error(
              "socket read failed: " + socket_error_text(error));
    }
    if (!clock.first_byte) {
      clock.first_byte = std::chrono::steady_clock::now();
    }
    offset += static_cast<size_t>(received);
    enforce_accounted_read_timeout(
      limits,
      idle_timeout,
      clock);
  }
  return true;
}

bool read_exact_into(
  SocketHandle fd,
  uint8_t * destination,
  size_t count,
  const rclcpp::Node::SharedPtr & node,
  const RosContextOk & context_ok)
{
  if (!context_ok) {
    throw std::invalid_argument("ROS context predicate is required");
  }
  size_t offset = 0;
  auto stalled_since = std::chrono::steady_clock::time_point {};
  while (offset < count) {
    if (!context_ok()) {
      throw ClientClosedException();
    }
    const auto received =
      receive_socket(fd, destination + offset, count - offset);
    if (received == 0) {
      if (offset == 0) {
        return false;
      }
      throw ClientClosedException();
    }
    if (received < 0) {
      const auto error = last_socket_error();
      if (socket_error_is_interrupted(error)) {
        continue;
      }
      if (socket_error_is_retryable_timeout(error)) {
        // A receive timeout before the first byte is an ordinary idle session.
        // Only a partially received frame is subject to the bounded stall clock.
        if (offset == 0) {
          if (node) {
            rclcpp::spin_some(node);
          }
          continue;
        }
        const auto now = std::chrono::steady_clock::now();
        if (stalled_since == std::chrono::steady_clock::time_point {}) {
          stalled_since = now;
        }
        if (now - stalled_since >= kReadStallTimeout) {
          throw ClientReadTimeoutException(count);
        }
        if (node) {
          rclcpp::spin_some(node);
        }
        continue;
      }
      throw std::runtime_error("socket read failed: " + socket_error_text(error));
    }
    offset += static_cast<size_t>(received);
    stalled_since = std::chrono::steady_clock::time_point {};
  }
  return true;
}

bool read_exact(
  SocketHandle fd,
  std::vector<uint8_t> & buffer,
  size_t count,
  const rclcpp::Node::SharedPtr & node,
  const RosContextOk & context_ok)
{
  buffer.assign(count, 0);
  return read_exact_into(
    fd,
    buffer.data(),
    count,
    node,
    context_ok);
}

bool read_exact(
  SocketHandle fd,
  std::vector<uint8_t> & buffer,
  size_t count,
  const rclcpp::Node::SharedPtr & node)
{
  return read_exact(
    fd,
    buffer,
    count,
    node,
    [&node]()
    {
      if (!node) {
        return rclcpp::ok();
      }
      return rclcpp::ok(node->get_node_base_interface()->get_context());
    });
}

void write_all(SocketHandle fd, const std::vector<uint8_t> & bytes)
{
  size_t offset = 0;
  while (offset < bytes.size()) {
    const auto sent = send_socket(fd, bytes.data() + offset, bytes.size() - offset);
    if (sent <= 0) {
      const auto error = last_socket_error();
      if (socket_error_is_interrupted(error)) {
        continue;
      }
      if (socket_error_is_retryable_timeout(error)) {
        fd_set write_fds;
        FD_ZERO(&write_fds);
        FD_SET(fd, &write_fds);
        timeval timeout {};
        timeout.tv_sec = 0;
        timeout.tv_usec = 250000;
        const int ready = ::select(
          socket_select_width(fd),
          nullptr,
          &write_fds,
          nullptr,
          &timeout);
        if (ready > 0) {
          continue;
        }
        if (ready < 0 && socket_error_is_interrupted(last_socket_error())) {
          continue;
        }
        throw std::runtime_error("socket write timed out");
      }
      throw std::runtime_error("socket write failed: " + socket_error_text(error));
    }
    offset += static_cast<size_t>(sent);
  }
}

struct AccountedWriteClock
{
  std::chrono::steady_clock::time_point stalled_since{
    std::chrono::steady_clock::now()};
};

uint64_t enforce_accounted_write_timeout(
  const u2r2::ProtocolLimits & limits,
  const AccountedWriteClock & clock)
{
  u2r2::PureSessionLifecycle lifecycle(limits);
  const auto elapsed = elapsed_milliseconds(clock.stalled_since);
  lifecycle.timeout(u2r2::TimeoutKind::write, elapsed);
  return lifecycle.limit_for(u2r2::TimeoutKind::write) - elapsed;
}

void write_all_accounted(
  SocketHandle fd,
  const std::vector<uint8_t> & bytes,
  const u2r2::ProtocolLimits & limits)
{
  ScopedNonBlockingSocket non_blocking(fd);
  AccountedWriteClock clock;
  size_t offset = 0;
  while (offset < bytes.size()) {
    (void)enforce_accounted_write_timeout(limits, clock);
    const auto sent =
      send_socket(fd, bytes.data() + offset, bytes.size() - offset);
    if (sent > 0) {
      (void)enforce_accounted_write_timeout(limits, clock);
      offset += static_cast<size_t>(sent);
      clock.stalled_since = std::chrono::steady_clock::now();
      continue;
    }

    const auto error = last_socket_error();
    if (socket_error_is_interrupted(error)) {
      continue;
    }
    if (socket_error_is_retryable_timeout(error)) {
      const auto remaining_ms =
        enforce_accounted_write_timeout(limits, clock);
      fd_set write_fds;
      FD_ZERO(&write_fds);
      FD_SET(fd, &write_fds);
      const auto poll_ms =
        static_cast<uint32_t>(std::min<uint64_t>(remaining_ms, 250U));
      timeval timeout {};
      timeout.tv_sec = static_cast<long>(poll_ms / 1000U);
      timeout.tv_usec = static_cast<long>((poll_ms % 1000U) * 1000U);
      const int ready = ::select(
        socket_select_width(fd),
        nullptr,
        &write_fds,
        nullptr,
        &timeout);
      (void)enforce_accounted_write_timeout(limits, clock);
      if (ready > 0) {
        continue;
      }
      if (ready < 0 && socket_error_is_interrupted(last_socket_error())) {
        continue;
      }
      if (ready < 0) {
        throw std::runtime_error(
                "socket write select failed: " +
                socket_error_text(last_socket_error()));
      }
      continue;
    }
    throw std::runtime_error(
            "socket write failed: " + socket_error_text(error));
  }
}

void write_u2r2_frame(
  SocketHandle fd,
  const nlohmann::json & header,
  const std::vector<uint8_t> & payload)
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

RawFrame read_raw_frame(SocketHandle fd, const rclcpp::Node::SharedPtr & node)
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

AccountedWireFrame read_accounted_wire_frame(
  SocketHandle fd,
  bridge_runtime::BridgeSessionProtocol & protocol,
  const RosContextOk & context_ok,
  u2r2::TimeoutKind idle_timeout = u2r2::TimeoutKind::read)
{
  const auto fixed_size =
    static_cast<size_t>(protocol.limits().fixed_frame_bytes());
  if (fixed_size != 16U) {
    throw u2r2::ProtocolError(
            "invalid_frame",
            "the U2R2 fixed frame size is unsupported",
            true);
  }

  std::array<uint8_t, 16> fixed_header{};
  AccountedReadClock read_clock;
  if (!read_exact_accounted(
      fd,
      fixed_header.data(),
      fixed_header.size(),
      context_ok,
      protocol.limits(),
      idle_timeout,
      read_clock))
  {
    throw ClientClosedException();
  }

  const auto frame_size = u2r2::FrameSize::create(
    protocol.limits(),
    read_u32_le(&fixed_header[8]),
    read_u32_le(&fixed_header[12]));
  auto read_lease = protocol.try_begin_read(frame_size.total_bytes());
  if (!read_lease) {
    throw u2r2::ProtocolError(
            "capacity_exceeded",
            "no in-flight read capacity remains for the U2R2 frame",
            false);
  }
  const auto transient_bytes = u2r2::checked_add(
    frame_size.total_bytes(),
    frame_size.total_bytes(),
    protocol.limits().max_transient_bytes(),
    "sidecar frame parsing");
  auto transient_lease =
    protocol.try_reserve_transient(transient_bytes);
  if (!transient_lease) {
    throw u2r2::ProtocolError(
            "capacity_exceeded",
            "no transient capacity remains for U2R2 frame parsing",
            false);
  }

  std::vector<uint8_t> wire(
    static_cast<size_t>(frame_size.total_bytes()));
  std::copy(fixed_header.begin(), fixed_header.end(), wire.begin());
  const auto remainder_size = wire.size() - fixed_header.size();
  if (remainder_size > 0 &&
    !read_exact_accounted(
      fd,
      wire.data() + fixed_header.size(),
      remainder_size,
      context_ok,
      protocol.limits(),
      idle_timeout,
      read_clock))
  {
    throw ClientClosedException();
  }
  return AccountedWireFrame{
    std::move(wire),
    std::move(*transient_lease),
    std::move(*read_lease)};
}

RawFrame raw_frame_from_wire(
  const std::vector<uint8_t> & wire,
  const u2r2::ProtocolLimits & limits)
{
  auto decoded = u2r2::decode_frame(wire, limits);
  return RawFrame{std::move(decoded.header), std::move(decoded.payload)};
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

void write_health_pong_ok(SocketHandle fd, const std::string & request_id)
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
  SocketHandle fd,
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

void handle_health_ping(SocketHandle fd, const RawFrame & raw)
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

#if RCLCPP_VERSION_MAJOR < 20
class BridgeGenericSubscriptionWithInfo final
  : public rclcpp::GenericSubscription
{
public:
  using Callback = std::function<void(
      const std::shared_ptr<rclcpp::SerializedMessage> &,
      const rclcpp::MessageInfo &)>;

  BridgeGenericSubscriptionWithInfo(
    rclcpp::node_interfaces::NodeBaseInterface * node_base,
    const std::shared_ptr<rcpputils::SharedLibrary> & typesupport,
    const std::string & topic,
    const std::string & message_type,
    const rclcpp::QoS & qos,
    Callback callback,
    const rclcpp::SubscriptionOptions & options)
  : rclcpp::GenericSubscription(
      node_base,
      typesupport,
      topic,
      message_type,
      qos,
      [](std::shared_ptr<rclcpp::SerializedMessage>) {},
      options),
    callback_(std::move(callback))
  {
    if (!callback_) {
      throw std::invalid_argument(
              "a Bridge generic subscription callback is required");
    }
  }

  void handle_message(
    std::shared_ptr<void> & message,
    const rclcpp::MessageInfo & message_info) override
  {
    callback_(
      std::static_pointer_cast<rclcpp::SerializedMessage>(message),
      message_info);
  }

  void handle_serialized_message(
    const std::shared_ptr<rclcpp::SerializedMessage> & message,
    const rclcpp::MessageInfo & message_info) override
  {
    callback_(message, message_info);
  }

private:
  Callback callback_;
};
#endif

class BridgeNode
{
public:
  explicit BridgeNode(
    rclcpp::Node::SharedPtr node,
    PayloadFormat payload_format,
    uint64_t maximum_publishers =
    u2r2::ProtocolLimits::defaults().max_contracts())
  : node_(std::move(node)),
    payload_format_(payload_format),
    origin_registry_(
      std::make_shared<bridge_runtime::BridgeOriginRegistry>(
        maximum_publishers)),
    maximum_publishers_(maximum_publishers)
  {
    if (!node_) {
      throw std::invalid_argument("bridge node is required");
    }
    if (maximum_publishers_ == 0) {
      throw std::invalid_argument("publisher capacity must be positive");
    }

    const auto publisher_node = node_;
    const auto origin_registry = origin_registry_;
    publisher_factory_ =
      [publisher_node, origin_registry](
      const std::string & topic,
      const std::string & message_type,
      const rclcpp::QoS & qos)
      {
        // create_generic_publisher performs the rosidl_typesupport_cpp lookup
        // for the exact canonical message type before returning a publisher.
        auto publisher =
          publisher_node->create_generic_publisher(topic, message_type, qos);
        auto origin_registered = std::make_shared<std::once_flag>();
        return [
          publisher = std::move(publisher),
          origin_registry,
          origin_registered](
          const rclcpp::SerializedMessage & message)
          {
            std::call_once(
              *origin_registered,
              [&]() {
                origin_registry->register_local(publisher->get_gid());
              });
            publisher->publish(message);
          };
      };
  }

  BridgeNode(
    PayloadFormat payload_format,
    GenericPublisherFactory publisher_factory,
    uint64_t maximum_publishers =
    u2r2::ProtocolLimits::defaults().max_contracts())
  : payload_format_(payload_format),
    origin_registry_(
      std::make_shared<bridge_runtime::BridgeOriginRegistry>(
        maximum_publishers)),
    publisher_factory_(std::move(publisher_factory)),
    maximum_publishers_(maximum_publishers)
  {
    if (!publisher_factory_) {
      throw std::invalid_argument("generic publisher factory is required");
    }
    if (maximum_publishers_ == 0) {
      throw std::invalid_argument("publisher capacity must be positive");
    }
  }

  PublisherContractDisposition prepare(const BridgeFrame & frame)
  {
    const auto disposition = publisher_contracts_.register_or_validate(frame);
    auto publisher_it = publishers_.find(frame.topic);
    if (disposition == PublisherContractDisposition::CreatePublisher) {
      if (publishers_.size() >= maximum_publishers_) {
        publisher_contracts_.rollback_create(frame.topic);
        throw std::runtime_error(
                "bridge publisher capacity is exhausted");
      }
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
    publish_prepared(frame);
  }

  void publish_prepared(const BridgeFrame & frame)
  {
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

  std::shared_ptr<void> subscribe(
    const u2r2::ContractIdentity & identity,
    bridge_runtime::BridgeSerializedCallback callback)
  {
    if (!node_) {
      throw std::logic_error(
              "generic subscriptions require the process-owned ROS node");
    }
    if (!callback) {
      throw std::invalid_argument(
              "generic subscriptions require a serialized callback");
    }
    if (identity.qos.depth >
      static_cast<uint64_t>(std::numeric_limits<int>::max()))
    {
      throw std::runtime_error(
              "subscription QoS depth exceeds the supported integer range");
    }
    BridgeFrame contract;
    contract.topic = identity.topic;
    contract.schema_name = identity.schema_name;
    contract.encoding = "cdr";
    contract.profile = identity.qos.profile;
    contract.reliability = identity.qos.reliability;
    contract.durability = identity.qos.durability;
    contract.history = identity.qos.history;
    contract.depth = static_cast<int>(identity.qos.depth);
    auto qos = make_qos(contract);
    const auto logger = node_->get_logger();
    const auto origin_registry = origin_registry_;
    auto receive =
      [callback = std::move(callback), logger, origin_registry](
        std::shared_ptr<rclcpp::SerializedMessage> message,
        const rclcpp::MessageInfo & info) {
        if (!message) {
          return;
        }
        const auto & serialized =
          message->get_rcl_serialized_message();
        const auto now = std::chrono::duration_cast<std::chrono::nanoseconds>(
          std::chrono::system_clock::now().time_since_epoch()).count();
        try {
          (void)callback(
            serialized.buffer,
            serialized.buffer_length,
            now < 0 ? 0U : static_cast<uint64_t>(now),
            origin_registry->classify(
              info.get_rmw_message_info().publisher_gid));
        } catch (const std::exception & error) {
          RCLCPP_ERROR(
            logger,
            "[unity2foxglove_ros2_bridge] serialized subscription "
            "callback rejected a sample: %s",
            error.what());
        } catch (...) {
          RCLCPP_ERROR(
            logger,
            "[unity2foxglove_ros2_bridge] serialized subscription "
            "callback rejected a sample with an unknown error");
        }
      };
#if RCLCPP_VERSION_MAJOR < 20
    rclcpp::SubscriptionOptions options;
    const auto topics = node_->get_node_topics_interface();
    auto subscription =
      std::make_shared<BridgeGenericSubscriptionWithInfo>(
      topics->get_node_base_interface(),
      rclcpp::get_typesupport_library(
        identity.schema_name,
        "rosidl_typesupport_cpp"),
      identity.topic,
      identity.schema_name,
      qos,
      std::move(receive),
      options);
    topics->add_subscription(subscription, options.callback_group);
#else
    auto subscription = node_->create_generic_subscription(
      identity.topic,
      identity.schema_name,
      qos,
      std::move(receive));
#endif
    if (!subscription) {
      throw std::runtime_error(
              "generic subscription factory returned no subscription");
    }
    RCLCPP_INFO(
      node_->get_logger(),
      "[unity2foxglove_ros2_bridge] subscription %s %s "
      "profile=%s reliability=%s durability=%s history=%s depth=%u",
      identity.topic.c_str(),
      identity.schema_name.c_str(),
      identity.qos.profile.c_str(),
      identity.qos.reliability.c_str(),
      identity.qos.durability.c_str(),
      identity.qos.history.c_str(),
      identity.qos.depth);
    return std::static_pointer_cast<void>(subscription);
  }

private:
  rclcpp::Node::SharedPtr node_;
  PayloadFormat payload_format_;
  std::shared_ptr<bridge_runtime::BridgeOriginRegistry> origin_registry_;
  GenericPublisherFactory publisher_factory_;
  uint64_t maximum_publishers_;
  PublisherContractRegistry publisher_contracts_;
  std::unordered_map<std::string, SerializedPublishCallback> publishers_;
  std::unordered_map<std::string, size_t> counts_;
  std::vector<uint8_t> payload_scratch_;
};

using NodeFactory = std::function<rclcpp::Node::SharedPtr()>;

class DeferredBridgeSession
{
public:
  DeferredBridgeSession(PayloadFormat payload_format, NodeFactory node_factory)
  : DeferredBridgeSession(payload_format, rclcpp::Node::SharedPtr {}, std::move(node_factory))
  {
  }

  DeferredBridgeSession(
    PayloadFormat payload_format,
    rclcpp::Node::SharedPtr process_node,
    NodeFactory node_factory)
  : payload_format_(payload_format),
    node_factory_(std::move(node_factory)),
    node_(std::move(process_node))
  {
    if (!node_factory_) {
      throw std::invalid_argument("deferred bridge node factory is required");
    }
  }

  BridgeNode & require_bridge()
  {
    if (!bridge_) {
      if (!node_) {
        node_ = node_factory_();
      }
      if (!node_) {
        throw std::runtime_error("deferred bridge node factory returned no node");
      }
      bridge_ = std::make_unique<BridgeNode>(node_, payload_format_);
    }
    return *bridge_;
  }

  const rclcpp::Node::SharedPtr & node() const
  {
    return node_;
  }

  rclcpp::Logger logger() const
  {
    return node_ ? node_->get_logger() : rclcpp::get_logger(kSidecarName);
  }

  void spin_some() const
  {
    if (node_) {
      rclcpp::spin_some(node_);
    }
  }

private:
  PayloadFormat payload_format_;
  NodeFactory node_factory_;
  rclcpp::Node::SharedPtr node_;
  std::unique_ptr<BridgeNode> bridge_;
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

std::string publisher_request_id(const RawFrame & raw)
{
  if (
    raw.header.is_object() &&
    raw.header.contains("requestId") &&
    raw.header["requestId"].is_string())
  {
    return raw.header["requestId"].get<std::string>();
  }
  return {};
}

void dispatch_deferred_frame(
  SocketHandle client_fd,
  const RawFrame & raw,
  DeferredBridgeSession & session)
{
  if (!raw.header.contains("op") || !raw.header["op"].is_string()) {
    throw std::runtime_error("reject frame: missing or invalid op");
  }

  const auto op = raw.header.at("op").get<std::string>();
  if (op == "health_ping") {
    handle_health_ping(client_fd, raw);
    return;
  }

  if (op == "prepare_publisher") {
    nlohmann::json response;
    try {
      response = handle_prepare_publisher_frame(raw, session.require_bridge());
    } catch (const std::exception & ex) {
      response = publisher_ready_error(
        publisher_request_id(raw),
        "publisher_unavailable",
        ex.what());
    }
    write_u2r2_frame(client_fd, response, {});
    return;
  }

  if (op == "publish") {
    const auto frame = parse_publish_frame(raw);
    try {
      session.require_bridge().publish(frame);
    } catch (const std::exception & ex) {
      RCLCPP_WARN(
        session.logger(),
        "[unity2foxglove_ros2_bridge] dropped publish frame for topic '%s': %s",
        frame.topic.c_str(),
        ex.what());
    }
    return;
  }

  throw std::runtime_error("reject frame: unsupported op '" + op + "'");
}

void process_deferred_client(
  SocketHandle client_fd,
  DeferredBridgeSession & session)
{
  while (rclcpp::ok()) {
    try {
      const auto raw = read_raw_frame(client_fd, session.node());
      dispatch_deferred_frame(client_fd, raw, session);
      session.spin_some();
    } catch (const ClientClosedException &) {
      break;
    } catch (const ClientReadTimeoutException & ex) {
      RCLCPP_WARN(session.logger(), "[unity2foxglove_ros2_bridge] %s", ex.what());
      break;
    } catch (const std::exception & ex) {
      RCLCPP_WARN(session.logger(), "[unity2foxglove_ros2_bridge] %s", ex.what());
      break;
    } catch (...) {
      RCLCPP_WARN(
        session.logger(),
        "[unity2foxglove_ros2_bridge] client session failed with an unknown exception");
      break;
    }
  }
}

using BridgeGenerationFactory =
  std::function<std::unique_ptr<BridgeNode>()>;

std::string bounded_response_message(const std::string & message)
{
  if (message.size() <= kMaximumResponseErrorBytes) {
    return message;
  }
  return message.substr(0, kMaximumResponseErrorBytes);
}

void drain_session_writer(
  SocketHandle client_fd,
  bridge_runtime::BridgeSessionProtocol & protocol)
{
  while (auto write = protocol.try_begin_write()) {
    try {
      write_all_accounted(
        client_fd,
        write->frame().bytes(),
        protocol.limits());
    } catch (...) {
      write->release();
      throw;
    }
    write->release();
  }
}

class SessionSocketWriter final
{
public:
  SessionSocketWriter(
    SocketHandle socket,
    bridge_runtime::BridgeSessionProtocol & protocol)
  : socket_(socket),
    protocol_(protocol),
    thread_([this]() {run();})
  {
  }

  ~SessionSocketWriter()
  {
    stop_and_join();
  }

  SessionSocketWriter(const SessionSocketWriter &) = delete;
  SessionSocketWriter & operator=(const SessionSocketWriter &) = delete;

  void stop_and_join() noexcept
  {
    bool expected = false;
    if (stopped_.compare_exchange_strong(expected, true)) {
      stop_requested_.store(true);
      try {
        protocol_.close();
      } catch (...) {
      }
    }
    if (thread_.joinable()) {
      thread_.join();
    }
  }

  void rethrow_failure() const
  {
    std::exception_ptr failure;
    {
      std::lock_guard<std::mutex> lock(failure_mutex_);
      failure = failure_;
    }
    if (failure) {
      std::rethrow_exception(failure);
    }
  }

private:
  void run() noexcept
  {
    try {
      while (!stop_requested_.load()) {
        const auto observed = protocol_.wake_generation();
        while (!stop_requested_.load()) {
          auto write = protocol_.try_begin_write();
          if (!write) {
            break;
          }
          try {
            write_all_accounted(
              socket_,
              write->frame().bytes(),
              protocol_.limits());
          } catch (...) {
            write->release();
            throw;
          }
          write->release();
        }
        if (
          stop_requested_.load() ||
          protocol_.wake_generation() != observed)
        {
          continue;
        }
        (void)protocol_.wait_for_writer_change(
          observed,
          std::chrono::milliseconds(50));
      }
    } catch (...) {
      {
        std::lock_guard<std::mutex> lock(failure_mutex_);
        failure_ = std::current_exception();
      }
      stop_requested_.store(true);
      shutdown_socket_both(socket_);
    }
  }

  SocketHandle socket_;
  bridge_runtime::BridgeSessionProtocol & protocol_;
  mutable std::mutex failure_mutex_;
  std::exception_ptr failure_;
  std::atomic<bool> stop_requested_{false};
  std::atomic<bool> stopped_{false};
  std::thread thread_;
};

std::vector<uint8_t> encode_v2_ok_response(
  const bridge_runtime::BridgeSessionProtocol & protocol,
  const u2r2::Message & request,
  const char * operation)
{
  nlohmann::json header = {
    {"op", operation},
    {"protocolVersion", 2},
    {"requestId", request.request_id},
    {"status", "ok"},
    {"sessionId", protocol.session_id()},
    {"connectionGeneration", protocol.connection_generation()},
  };
  if (request.operation == u2r2::Operation::Publish) {
    header["messageId"] = request.message_id;
  }
  return u2r2::encode_frame(header, {}, protocol.limits());
}

std::vector<uint8_t> encode_v2_error_response(
  const bridge_runtime::BridgeSessionProtocol & protocol,
  const u2r2::Message & request,
  const char * operation,
  const char * error_code,
  const std::string & message)
{
  bool terminal = false;
  if (!u2r2::try_get_stable_error_terminal(error_code, terminal)) {
    throw std::invalid_argument("v2 response requires a stable error code");
  }
  return u2r2::encode_frame(
    {
      {"op", operation},
      {"protocolVersion", 2},
      {"requestId", request.request_id},
      {"status", "error"},
      {"sessionId", protocol.session_id()},
      {"connectionGeneration", protocol.connection_generation()},
      {"errorCode", error_code},
      {"message", bounded_response_message(message)},
      {"terminal", terminal},
    },
    {},
    protocol.limits());
}

struct V2ErrorResponseRoute final
{
  u2r2::Operation operation;
  const char * name;
};

std::optional<V2ErrorResponseRoute> v2_error_response_route(
  u2r2::Operation request_operation,
  const std::string & error_code)
{
  std::optional<V2ErrorResponseRoute> route;
  switch (request_operation) {
    case u2r2::Operation::PreparePublisher:
      route = V2ErrorResponseRoute{
        u2r2::Operation::PublisherReady,
        "publisher_ready"};
      break;
    case u2r2::Operation::Publish:
      route = V2ErrorResponseRoute{
        u2r2::Operation::PublishResult,
        "publish_result"};
      break;
    case u2r2::Operation::RegisterSubscription:
      route = V2ErrorResponseRoute{
        u2r2::Operation::SubscriptionReady,
        "subscription_ready"};
      break;
    case u2r2::Operation::UnregisterSubscription:
      route = V2ErrorResponseRoute{
        u2r2::Operation::SubscriptionRemoved,
        "subscription_removed"};
      break;
    default:
      break;
  }
  if (
    route &&
    u2r2::is_stable_error_allowed_for_response(
      error_code,
      route->operation))
  {
    return route;
  }
  if (u2r2::is_stable_error_allowed_for_response(
      error_code,
      u2r2::Operation::Fault))
  {
    return V2ErrorResponseRoute{u2r2::Operation::Fault, "fault"};
  }
  return std::nullopt;
}

bool schedule_v2_protocol_error(
  const std::vector<uint8_t> & request_wire,
  const u2r2::ProtocolError & error,
  bridge_runtime::BridgeSessionProtocol & protocol,
  const RosContextOk & context_ok)
{
  u2r2::Message request;
  try {
    request = u2r2::parse_v2(
      u2r2::decode_frame(request_wire, protocol.limits()));
  } catch (...) {
    return false;
  }
  if (!request.is_request) {
    return false;
  }
  const auto route = v2_error_response_route(
    request.operation,
    error.code());
  if (!route) {
    return false;
  }

  const auto response = encode_v2_error_response(
    protocol,
    request,
    route->name,
    error.code().c_str(),
    error.what());
  const auto deadline =
    std::chrono::steady_clock::now() +
    std::chrono::milliseconds(protocol.limits().join_timeout_ms());
  while (context_ok()) {
    try {
      protocol.enqueue_control(
        "protocol-error:" + std::to_string(request.request_id),
        response);
      return true;
    } catch (const u2r2::ProtocolError & enqueue_error) {
      if (
        enqueue_error.code() != "capacity_exceeded" ||
        enqueue_error.terminal() ||
        std::chrono::steady_clock::now() >= deadline)
      {
        return false;
      }
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(1));
  }
  return false;
}

BridgeFrame bridge_frame_from_v2_wire(
  const std::vector<uint8_t> & wire,
  u2r2::Operation operation,
  const u2r2::ProtocolLimits & limits)
{
  auto raw = raw_frame_from_wire(wire, limits);
  if (operation == u2r2::Operation::PreparePublisher) {
    raw.header["logTimeNs"] = 0;
    raw.header["sequence"] = 0;
    raw.payload = {0};
  }
  return parse_publish_frame(raw);
}

void schedule_legacy_health_response(
  const bridge_runtime::FirstFrameClassification & first,
  bridge_runtime::BridgeSessionProtocol & protocol)
{
  const auto & request = *first.legacy_message;
  protocol.enqueue_control(
    "v1-health:" + request.request_id,
    u2r2::encode_frame(
      {
        {"op", "health_pong"},
        {"requestId", request.request_id},
        {"protocolVersion", kHealthProtocolVersion},
        {"status", "ok"},
        {"sidecarName", kSidecarName},
        {"sidecarVersion", kSidecarVersion},
      },
      {},
      protocol.limits()));
}

void schedule_legacy_preparation(
  const std::vector<uint8_t> & wire,
  bridge_runtime::BridgeSessionProtocol & protocol,
  const std::function<BridgeNode &()> & require_bridge)
{
  auto reservation =
    protocol.try_reserve_control(kControlResponseReservationBytes);
  if (!reservation) {
    throw u2r2::ProtocolError(
            "capacity_exceeded",
            "no response capacity remains for legacy publisher preparation",
            false);
  }

  auto raw = raw_frame_from_wire(wire, protocol.limits());
  const auto request_id = publisher_request_id(raw);
  nlohmann::json response;
  try {
    const auto request = parse_prepare_publisher_frame(raw);
    try {
      require_bridge().prepare(request.frame);
      response = publisher_ready_ok(request.request_id);
    } catch (const PublisherContractConflictException & ex) {
      response = publisher_ready_error(
        request.request_id,
        "publisher_contract_conflict",
        bounded_response_message(ex.what()));
    } catch (const std::exception & ex) {
      response = publisher_ready_error(
        request.request_id,
        "publisher_unavailable",
        bounded_response_message(ex.what()));
    }
  } catch (const std::exception & ex) {
    response = publisher_ready_error(
      request_id,
      "invalid_contract",
      bounded_response_message(ex.what()));
  }

  auto exact = u2r2::encode_frame(response, {}, protocol.limits());
  reservation->commit(
    u2r2::OutboundFrame::control(
      "v1-prepare:" + request_id,
      std::move(exact)));
}

void dispatch_owned_legacy_data_frame(
  const std::vector<uint8_t> & wire,
  bridge_runtime::BridgeSessionProtocol & protocol,
  const std::function<BridgeNode &()> & require_bridge,
  const rclcpp::Logger & logger)
{
  const auto legacy = u2r2::parse_legacy_v1_first_frame(wire);
  if (legacy.operation == "health_ping") {
    throw u2r2::ProtocolError(
            "invalid_frame",
            "legacy health probes require a dedicated one-shot connection",
            true);
  }
  if (legacy.operation == "prepare_publisher") {
    schedule_legacy_preparation(wire, protocol, require_bridge);
    return;
  }

  try {
    const auto frame =
      parse_publish_frame(raw_frame_from_wire(wire, protocol.limits()));
    require_bridge().publish(frame);
  } catch (const std::exception & ex) {
    RCLCPP_WARN(
      logger,
      "[unity2foxglove_ros2_bridge] dropped legacy publish frame: %s",
      ex.what());
  }
}

void run_owned_legacy_data_session(
  SocketHandle client_fd,
  bridge_runtime::BridgeSessionProtocol & protocol,
  const std::function<BridgeNode &()> & require_bridge,
  const rclcpp::Logger & logger,
  const RosContextOk & context_ok)
{
  while (context_ok()) {
    try {
      const auto wire =
        read_accounted_wire_frame(client_fd, protocol, context_ok);
      dispatch_owned_legacy_data_frame(
        wire.bytes,
        protocol,
        require_bridge,
        logger);
      drain_session_writer(client_fd, protocol);
    } catch (const ClientClosedException &) {
      return;
    }
  }
}

bridge_runtime::ReplayMutationResult dispatch_owned_v2_request(
  const u2r2::Message & request,
  const BridgeFrame * bridge_frame,
  bridge_runtime::BridgeSessionProtocol & protocol,
  const std::function<BridgeNode &()> & require_bridge)
{
  if (request.operation == u2r2::Operation::HealthPing) {
    return bridge_runtime::ReplayMutationResult::success(
      encode_v2_ok_response(protocol, request, "health_pong"));
  }

  if (request.operation == u2r2::Operation::PreparePublisher) {
    if (!bridge_frame) {
      throw std::logic_error("publisher preparation frame is required");
    }
    try {
      protocol.require_publisher_capacity(request);
      require_bridge().prepare(*bridge_frame);
      protocol.mark_publisher_ready(request);
      return bridge_runtime::ReplayMutationResult::success(
        encode_v2_ok_response(protocol, request, "publisher_ready"));
    } catch (const u2r2::ProtocolError & ex) {
      if (!u2r2::is_stable_error_allowed_for_response(
          ex.code(),
          u2r2::Operation::PublisherReady))
      {
        throw;
      }
      return bridge_runtime::ReplayMutationResult::error(
        encode_v2_error_response(
          protocol,
          request,
          "publisher_ready",
          ex.code().c_str(),
          ex.what()));
    } catch (const PublisherContractConflictException & ex) {
      return bridge_runtime::ReplayMutationResult::error(
        encode_v2_error_response(
          protocol,
          request,
          "publisher_ready",
          "invalid_contract",
          ex.what()));
    } catch (const std::exception & ex) {
      return bridge_runtime::ReplayMutationResult::error(
        encode_v2_error_response(
          protocol,
          request,
          "publisher_ready",
          "publisher_unavailable",
          ex.what()));
    }
  }

  if (request.operation == u2r2::Operation::Publish) {
    if (!bridge_frame) {
      throw std::logic_error("publish frame is required");
    }
    protocol.require_publisher_ready(request);
    try {
      require_bridge().publish_prepared(*bridge_frame);
      return bridge_runtime::ReplayMutationResult::success(
        encode_v2_ok_response(protocol, request, "publish_result"));
    } catch (const std::exception & ex) {
      return bridge_runtime::ReplayMutationResult::error(
        encode_v2_error_response(
          protocol,
          request,
          "publish_result",
          "invalid_contract",
          ex.what()));
    }
  }

  throw u2r2::ProtocolError(
          "invalid_frame",
          "the U2R2 operation is not supported by the publish sidecar",
          true);
}

void run_owned_v2_data_session(
  SocketHandle client_fd,
  bridge_runtime::ProcessConnectionAuthority & authority,
  bridge_runtime::BridgeSessionProtocol & protocol,
  const std::function<BridgeNode &()> & require_bridge,
  const RosContextOk & context_ok)
{
  protocol.bind_v2_identity(authority.allocate_session_identity());
  SessionSocketWriter writer(client_fd, protocol);
  try {
    while (context_ok()) {
      const auto wire =
        read_accounted_wire_frame(client_fd, protocol, context_ok);
      try {
        const auto request = protocol.parse_v2_request(wire.bytes);
        if (request.operation == u2r2::Operation::RegisterSubscription) {
          (void)protocol.register_subscription(
            wire.bytes,
            request,
            kControlResponseReservationBytes,
            [&](const u2r2::ContractIdentity & identity,
              bridge_runtime::BridgeSerializedCallback callback) {
              return require_bridge().subscribe(
                identity,
                std::move(callback));
            });
          continue;
        }
        if (request.operation == u2r2::Operation::UnregisterSubscription) {
          (void)protocol.unregister_subscription(
            wire.bytes,
            request,
            kControlResponseReservationBytes);
          continue;
        }
        std::optional<BridgeFrame> bridge_frame;
        if (
          request.operation == u2r2::Operation::PreparePublisher ||
          request.operation == u2r2::Operation::Publish)
        {
          bridge_frame.emplace(bridge_frame_from_v2_wire(
              wire.bytes,
              request.operation,
              protocol.limits()));
        }
        (void)protocol.execute_replayable(
          wire.bytes,
          request,
          kControlResponseReservationBytes,
          [&]() {
            return dispatch_owned_v2_request(
              request,
              bridge_frame ? &*bridge_frame : nullptr,
              protocol,
              require_bridge);
          });
      } catch (const u2r2::ProtocolError & error) {
        const auto responded = schedule_v2_protocol_error(
          wire.bytes,
          error,
          protocol,
          context_ok);
        if (!responded || error.terminal()) {
          throw;
        }
      }
    }
  } catch (const ClientClosedException &) {
  } catch (...) {
    writer.stop_and_join();
    throw;
  }
  writer.stop_and_join();
  writer.rethrow_failure();
}

void process_owned_client(
  SocketHandle client_fd,
  bridge_runtime::ProcessConnectionAuthority & authority,
  const BridgeGenerationFactory & generation_factory,
  const rclcpp::Logger & logger,
  const RosContextOk & context_ok)
{
  if (!generation_factory) {
    throw std::invalid_argument("bridge generation factory is required");
  }
  if (!context_ok) {
    throw std::invalid_argument("ROS context predicate is required");
  }

  bridge_runtime::BridgeSessionProtocol protocol(authority.limits());
  std::optional<AccountedWireFrame> first_wire(
    read_accounted_wire_frame(
      client_fd,
      protocol,
      context_ok,
      u2r2::TimeoutKind::handshake));
  const auto first =
    protocol.accept_first_frame(first_wire->bytes);

  if (first.role == bridge_runtime::FirstFrameRole::probe) {
    auto probe_lease =
      authority.try_acquire_role(u2r2::ConnectionRole::probe);
    if (!probe_lease) {
      RCLCPP_WARN(
        logger,
        "[unity2foxglove_ros2_bridge] busy: legacy v1 probe capacity exhausted");
      return;
    }
    schedule_legacy_health_response(first, protocol);
    drain_session_writer(client_fd, protocol);
    probe_lease->release();
    return;
  }

  auto data_lease =
    authority.try_acquire_role(u2r2::ConnectionRole::data_session);
  if (!data_lease) {
    if (first.dialect == u2r2::Dialect::V2) {
      protocol.enqueue_control(
        "v2-busy",
        bridge_runtime::make_v2_busy_response(
          first.v2_message->request_id,
          protocol.limits()));
      drain_session_writer(client_fd, protocol);
    } else if (first.legacy_message->operation == "prepare_publisher") {
      protocol.enqueue_control(
        "v1-busy:" + first.legacy_message->request_id,
        u2r2::encode_frame(
          publisher_ready_error(
            first.legacy_message->request_id,
            "publisher_unavailable",
            "data session already leased"),
          {},
          protocol.limits()));
      drain_session_writer(client_fd, protocol);
    } else {
      RCLCPP_WARN(
        logger,
        "[unity2foxglove_ros2_bridge] %s",
        bridge_runtime::kLegacyPublishBusyLog);
    }
    return;
  }

  bridge_runtime::GenerationOwnership generation(std::move(*data_lease));
  BridgeNode * bridge = nullptr;
  const auto require_bridge =
    [&]() -> BridgeNode & {
      if (!bridge) {
        bridge =
          &generation.adopt_entities(generation_factory());
      }
      return *bridge;
    };

  try {
    if (first.dialect == u2r2::Dialect::V2) {
      first_wire.reset();
      run_owned_v2_data_session(
        client_fd,
        authority,
        protocol,
        require_bridge,
        context_ok);
    } else {
      dispatch_owned_legacy_data_frame(
        first_wire->bytes,
        protocol,
        require_bridge,
        logger);
      drain_session_writer(client_fd, protocol);
      first_wire.reset();
      run_owned_legacy_data_session(
        client_fd,
        protocol,
        require_bridge,
        logger,
        context_ok);
    }
  } catch (...) {
    generation.release();
    throw;
  }
  generation.release();
}

void process_client(
  SocketHandle client_fd,
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
  SocketHandle client_fd,
  const rclcpp::Node::SharedPtr & node,
  PayloadFormat payload_format)
{
  BridgeNode bridge(node, payload_format);
  process_client(client_fd, bridge, node);
}

class ConnectionWorkerSet final
{
public:
  explicit ConnectionWorkerSet(uint64_t maximum_workers)
  {
    workers_.reserve(static_cast<size_t>(maximum_workers));
  }

  ~ConnectionWorkerSet()
  {
    stop_and_join_all();
  }

  ConnectionWorkerSet(const ConnectionWorkerSet &) = delete;
  ConnectionWorkerSet & operator=(const ConnectionWorkerSet &) = delete;

  template<typename Work>
  void start(std::shared_ptr<ScopedFd> socket, Work work)
  {
    auto done = std::make_shared<std::atomic<bool>>(false);
    workers_.push_back(Worker{socket, done, std::thread {}});
    try {
      workers_.back().thread = std::thread(
        [socket = std::move(socket),
        done,
        work = std::move(work)]() mutable {
          try {
            work();
          } catch (...) {
          }
          done->store(true);
        });
    } catch (...) {
      workers_.pop_back();
      throw;
    }
  }

  void reap_completed()
  {
    auto worker = workers_.begin();
    while (worker != workers_.end()) {
      if (!worker->done->load()) {
        ++worker;
        continue;
      }
      if (worker->thread.joinable()) {
        worker->thread.join();
      }
      worker = workers_.erase(worker);
    }
  }

  void stop_and_join_all()
  {
    for (const auto & worker : workers_) {
      shutdown_socket_both(worker.socket->get());
    }
    for (auto & worker : workers_) {
      if (worker.thread.joinable()) {
        worker.thread.join();
      }
    }
    workers_.clear();
  }

private:
  struct Worker
  {
    std::shared_ptr<ScopedFd> socket;
    std::shared_ptr<std::atomic<bool>> done;
    std::thread thread;
  };

  std::vector<Worker> workers_;
};
}  // namespace

#ifndef UNITY2FOXGLOVE_ROS2_BRIDGE_TESTING
int main(int argc, char ** argv)
{
#ifdef _WIN32
  std::unique_ptr<WinsockRuntime> winsock;
  try {
    winsock = std::make_unique<WinsockRuntime>();
  } catch (const std::exception & ex) {
    std::fprintf(
      stderr,
      "[unity2foxglove_ros2_bridge] %s\n",
      ex.what());
    return 1;
  }
#endif

  auto non_ros_args = rclcpp::init_and_remove_ros_arguments(argc, argv);
  auto logger = rclcpp::get_logger(kSidecarName);

  try {
    const auto options = parse_args(non_ros_args);
    const auto limits = u2r2::ProtocolLimits::defaults();
    bridge_runtime::ProcessConnectionAuthority authority(limits);
    const auto context = rclcpp::contexts::get_global_default_context();
    bridge_runtime::ProcessRosOwner ros_owner(kSidecarName, context);
    ConnectionWorkerSet workers(limits.max_connections());
    const BridgeGenerationFactory generation_factory =
      [&ros_owner,
      payload_format = options.payload_format,
      maximum_publishers = limits.max_contracts()]() {
        return std::make_unique<BridgeNode>(
          ros_owner.require_node(),
          payload_format,
          maximum_publishers);
      };
    ScopedFd listen_fd(create_listen_socket(options.host, options.port, logger));

    RCLCPP_INFO(
      logger,
      "[unity2foxglove_ros2_bridge] listening on %s:%d",
      options.host.c_str(),
      options.port);

    while (rclcpp::ok()) {
      workers.reap_completed();
      auto accepted = accept_with_timeout(listen_fd.get());
      if (!accepted.valid()) {
        continue;
      }

      ScopedFd candidate(accepted.fd);
      const auto peer_length = accepted.peer_length > 0
        ? static_cast<size_t>(accepted.peer_length)
        : 0U;
      if (!bridge_runtime::is_ipv4_loopback_peer(
          reinterpret_cast<const sockaddr *>(&accepted.peer),
          peer_length))
      {
        RCLCPP_WARN(
          logger,
          "[unity2foxglove_ros2_bridge] rejected non-IPv4-loopback peer after accept");
        continue;
      }

      auto accepted_lease = authority.try_acquire_accepted();
      if (!accepted_lease) {
        RCLCPP_WARN(
          logger,
          "[unity2foxglove_ros2_bridge] busy: preclassification connection capacity exhausted");
        continue;
      }

      auto client =
        std::make_shared<ScopedFd>(candidate.release());
      RCLCPP_INFO(logger, "[unity2foxglove_ros2_bridge] client connected");
      workers.start(
        client,
        [client,
        accepted_lease = std::move(*accepted_lease),
        &authority,
        generation_factory,
        logger,
        context]() mutable {
          try {
            process_owned_client(
              client->get(),
              authority,
              generation_factory,
              logger,
              [context]() {return rclcpp::ok(context);});
          } catch (const ClientReadTimeoutException & ex) {
            RCLCPP_WARN(
              logger,
              "[unity2foxglove_ros2_bridge] %s",
              ex.what());
          } catch (const u2r2::ProtocolError & ex) {
            RCLCPP_WARN(
              logger,
              "[unity2foxglove_ros2_bridge] protocol %s: %s",
              ex.code().c_str(),
              ex.what());
          } catch (const std::exception & ex) {
            RCLCPP_WARN(
              logger,
              "[unity2foxglove_ros2_bridge] client session escaped: %s",
              ex.what());
          } catch (...) {
            RCLCPP_WARN(
              logger,
              "[unity2foxglove_ros2_bridge] client session escaped with an unknown exception");
          }
          accepted_lease.release();
          RCLCPP_INFO(
            logger,
            "[unity2foxglove_ros2_bridge] client disconnected");
        });
    }
    workers.stop_and_join_all();
    ros_owner.stop();
  } catch (const std::exception & ex) {
    RCLCPP_ERROR(logger, "[unity2foxglove_ros2_bridge] %s", ex.what());
    rclcpp::shutdown();
    return 1;
  }

  rclcpp::shutdown();
  return 0;
}
#endif
