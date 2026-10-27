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
  const auto maximum = std::min(message.size(), kMaximumResponseErrorBytes);
  size_t cursor = 0U;
  const auto is_continuation = [](unsigned char byte) noexcept {
      return (byte & 0xc0U) == 0x80U;
    };
  while (cursor < maximum) {
    const auto first = static_cast<unsigned char>(message[cursor]);
    size_t width = 0U;
    if (first <= 0x7fU) {
      width = 1U;
    } else if (first >= 0xc2U && first <= 0xdfU) {
      width = 2U;
    } else if (first >= 0xe0U && first <= 0xefU) {
      width = 3U;
    } else if (first >= 0xf0U && first <= 0xf4U) {
      width = 4U;
    } else {
      break;
    }
    if (width > maximum - cursor || width > message.size() - cursor) {
      break;
    }
    const auto second = width > 1U
      ? static_cast<unsigned char>(message[cursor + 1U])
      : 0U;
    if (
      (width > 1U && !is_continuation(second)) ||
      (width > 2U &&
      !is_continuation(static_cast<unsigned char>(message[cursor + 2U]))) ||
      (width > 3U &&
      !is_continuation(static_cast<unsigned char>(message[cursor + 3U]))) ||
      (first == 0xe0U && second < 0xa0U) ||
      (first == 0xedU && second > 0x9fU) ||
      (first == 0xf0U && second < 0x90U) ||
      (first == 0xf4U && second > 0x8fU))
    {
      break;
    }
    cursor += width;
  }
  return message.substr(0, cursor);
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
      try {
        protocol_.begin_drain();
      } catch (...) {
        try {
          protocol_.close();
        } catch (...) {
        }
      }
      stop_requested_.store(true);
    }
    if (thread_.joinable()) {
      thread_.join();
    }
    try {
      protocol_.close();
    } catch (...) {
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
      while (true) {
        const auto observed = protocol_.wake_generation();
        while (true) {
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
        if (stop_requested_.load()) {
          break;
        }
        if (
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
