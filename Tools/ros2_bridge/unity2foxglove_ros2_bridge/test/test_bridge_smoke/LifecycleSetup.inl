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

TEST(
  Unity2FoxgloveRos2BridgeProtocol,
  AccountedIdleReadStopsWhenTheOwningContextStops)
{
  const auto sockets = MakeConnectedSocketPair();
  ASSERT_NE(kInvalidSocket, sockets[0]);
  ASSERT_NE(kInvalidSocket, sockets[1]);
  ScopedFd client_socket(sockets[0]);
  ScopedFd server_socket(sockets[1]);
  configure_client_timeouts(server_socket.get());

  bridge_runtime::BridgeSessionProtocol protocol(
    u2r2::ProtocolLimits::defaults());
  std::atomic<bool> context_ok{true};
  std::atomic<int> outcome{0};
  std::thread reader(
    [&]() {
      try {
        (void)read_accounted_wire_frame(
          server_socket.get(),
          protocol,
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
  const auto deadline =
    std::chrono::steady_clock::now() + std::chrono::seconds(2);
  while (
    outcome.load() == 0 &&
    std::chrono::steady_clock::now() < deadline)
  {
    std::this_thread::sleep_for(std::chrono::milliseconds(10));
  }

  const bool stopped_before_socket_close = outcome.load() != 0;
  if (!stopped_before_socket_close) {
    EXPECT_EQ(0, ShutdownSocketWrite(client_socket.get()));
  }
  reader.join();

  EXPECT_TRUE(stopped_before_socket_close);
  EXPECT_EQ(2, outcome.load());
  EXPECT_EQ(0U, protocol.in_flight_bytes());
  EXPECT_EQ(0U, protocol.transient_bytes());
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
