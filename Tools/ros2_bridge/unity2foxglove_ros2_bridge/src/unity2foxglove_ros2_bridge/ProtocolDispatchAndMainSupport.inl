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
    case u2r2::Operation::HealthPing:
      route = V2ErrorResponseRoute{
        u2r2::Operation::HealthPong,
        "health_pong"};
      break;
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
