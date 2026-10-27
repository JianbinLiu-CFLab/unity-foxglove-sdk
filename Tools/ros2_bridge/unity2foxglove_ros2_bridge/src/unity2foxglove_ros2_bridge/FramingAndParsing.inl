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
      // Keep the frame-start clock monotonic across partial sends. Resetting
      // it here would let a peer extend one finite frame indefinitely by
      // accepting a byte just before each stall timeout.
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
  // The raw wire vector is already charged to the in-flight reader budget.
  // Charge one additional frame for decode/model materialization so the other
  // default transient frame remains available to reverse-direction callbacks.
  const auto transient_bytes = frame_size.total_bytes();
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
