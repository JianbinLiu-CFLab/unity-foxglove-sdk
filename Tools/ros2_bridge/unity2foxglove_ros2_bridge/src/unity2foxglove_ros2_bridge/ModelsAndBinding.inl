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
  uint32_t depth = 10U;
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

uint32_t parse_qos_depth(const nlohmann::json & value)
{
  if (value.is_number_unsigned()) {
    const auto depth = value.get<uint64_t>();
    if (depth > static_cast<uint64_t>(std::numeric_limits<uint32_t>::max())) {
      throw std::runtime_error(
              "qos.depth is outside the supported unsigned 32-bit range");
    }
    return static_cast<uint32_t>(depth);
  }

  if (value.is_number_integer()) {
    const auto depth = value.get<int64_t>();
    if (
      depth < 0 ||
      static_cast<uint64_t>(depth) >
      static_cast<uint64_t>(std::numeric_limits<uint32_t>::max()))
    {
      throw std::runtime_error(
              "qos.depth is outside the supported unsigned 32-bit range");
    }
    return static_cast<uint32_t>(depth);
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
    if (frame.depth < 1U) {
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
