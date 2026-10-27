TEST(
  Unity2FoxgloveRos2BridgeProtocol,
  OwnedV2NonterminalParseAndReplayErrorsRespondAndSessionContinues)
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
  std::exception_ptr server_error;
  BridgeGenerationFactory generation_factory =
    []() -> std::unique_ptr<BridgeNode> {
      GenericPublisherFactory publisher_factory =
        [](const std::string &, const std::string &, const rclcpp::QoS &) {
          return [](const rclcpp::SerializedMessage &) {};
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
          rclcpp::get_logger("phase187_v2_error_response_test"),
          []() {return true;});
      } catch (...) {
        server_error = std::current_exception();
      }
    });
  SocketThreadJoiner server_joiner(client_socket.get(), server);

  const auto hello = u2r2::encode_frame(
    {
      {"op", "hello"},
      {"protocolVersion", 2},
      {"requestId", 10},
      {"clientName", "phase187-error-recovery"},
      {"capabilities", nlohmann::json::array({"publish"})},
    },
    {});
  write_all(client_socket.get(), hello);
  const auto hello_ack = u2r2::parse_v2(
    u2r2::decode_frame(ReadSocketWireFrame(client_socket.get())));
  ASSERT_EQ(u2r2::Operation::HelloAck, hello_ack.operation);

  const auto prepare_request = [&](uint64_t request_id) {
    return u2r2::encode_frame(
      {
        {"op", "prepare_publisher"},
        {"protocolVersion", 2},
        {"requestId", request_id},
        {"sessionId", hello_ack.session_id},
        {"connectionGeneration", hello_ack.connection_generation},
        {"topic", "/phase187/v2/stale"},
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
  };
  const auto health_request = [&](uint64_t request_id) {
    return u2r2::encode_frame(
      {
        {"op", "health_ping"},
        {"protocolVersion", 2},
        {"requestId", request_id},
        {"sessionId", hello_ack.session_id},
        {"connectionGeneration", hello_ack.connection_generation},
      },
      {});
  };

  const auto stale_prepare = prepare_request(9);
  write_all(client_socket.get(), stale_prepare);
  const auto stale_response = u2r2::parse_v2(
    u2r2::decode_frame(ReadSocketWireFrame(client_socket.get())));
  EXPECT_EQ(u2r2::Operation::PublisherReady, stale_response.operation);
  EXPECT_EQ(9U, stale_response.request_id);
  EXPECT_EQ("error", stale_response.status);
  EXPECT_EQ("stale_request", stale_response.error_code);
  EXPECT_FALSE(stale_response.terminal);

  write_all(client_socket.get(), health_request(8));
  const auto stale_health_response = u2r2::parse_v2(
    u2r2::decode_frame(ReadSocketWireFrame(client_socket.get())));
  EXPECT_EQ(u2r2::Operation::HealthPong, stale_health_response.operation);
  EXPECT_EQ(8U, stale_health_response.request_id);
  EXPECT_EQ("error", stale_health_response.status);
  EXPECT_EQ("stale_request", stale_health_response.error_code);
  EXPECT_FALSE(stale_health_response.terminal);

  const auto health = health_request(20);
  write_all(client_socket.get(), health);
  const auto health_response = u2r2::parse_v2(
    u2r2::decode_frame(ReadSocketWireFrame(client_socket.get())));
  EXPECT_EQ(u2r2::Operation::HealthPong, health_response.operation);
  EXPECT_EQ(20U, health_response.request_id);
  EXPECT_EQ("ok", health_response.status);

  const auto replay_stale_prepare = prepare_request(12);
  write_all(client_socket.get(), replay_stale_prepare);
  const auto replay_stale_response = u2r2::parse_v2(
    u2r2::decode_frame(ReadSocketWireFrame(client_socket.get())));
  EXPECT_EQ(
    u2r2::Operation::PublisherReady,
    replay_stale_response.operation);
  EXPECT_EQ(12U, replay_stale_response.request_id);
  EXPECT_EQ("error", replay_stale_response.status);
  EXPECT_EQ("stale_request", replay_stale_response.error_code);
  EXPECT_FALSE(replay_stale_response.terminal);

  write_all(client_socket.get(), health_request(21));
  const auto recovered = u2r2::parse_v2(
    u2r2::decode_frame(ReadSocketWireFrame(client_socket.get())));
  EXPECT_EQ(u2r2::Operation::HealthPong, recovered.operation);
  EXPECT_EQ(21U, recovered.request_id);
  EXPECT_EQ("ok", recovered.status);

  EXPECT_EQ(0, ShutdownSocketWrite(client_socket.get()));
  server.join();
  ASSERT_EQ(nullptr, server_error);
  EXPECT_EQ(0U, authority.classified_count());
}

TEST(
  Unity2FoxgloveRos2BridgeProtocol,
  OwnedV2PeerHalfCloseDrainsCommittedUnknownContractResponse)
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
  std::exception_ptr server_error;
  BridgeGenerationFactory generation_factory =
    []() -> std::unique_ptr<BridgeNode> {
      GenericPublisherFactory publisher_factory =
        [](const std::string &, const std::string &, const rclcpp::QoS &) {
          return [](const rclcpp::SerializedMessage &) {};
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
          rclcpp::get_logger("phase187_v2_half_close_drain_test"),
          []() {return true;});
      } catch (...) {
        server_error = std::current_exception();
      }
    });
  SocketThreadJoiner server_joiner(client_socket.get(), server);

  write_all(
    client_socket.get(),
    u2r2::encode_frame(
      {
        {"op", "hello"},
        {"protocolVersion", 2},
        {"requestId", 30},
        {"clientName", "phase187-half-close-drain"},
        {"capabilities", nlohmann::json::array({"subscribe"})},
      },
      {}));
  const auto hello_ack = u2r2::parse_v2(
    u2r2::decode_frame(ReadSocketWireFrame(client_socket.get())));
  ASSERT_EQ(u2r2::Operation::HelloAck, hello_ack.operation);

  write_all(
    client_socket.get(),
    u2r2::encode_frame(
      {
        {"op", "unregister_subscription"},
        {"protocolVersion", 2},
        {"requestId", 31},
        {"sessionId", hello_ack.session_id},
        {"connectionGeneration", hello_ack.connection_generation},
        {"contractId", 404},
      },
      {}));
  ASSERT_EQ(0, ShutdownSocketWrite(client_socket.get()));

  const auto response = u2r2::parse_v2(
    u2r2::decode_frame(ReadSocketWireFrame(client_socket.get())));
  server.join();

  ASSERT_EQ(nullptr, server_error);
  EXPECT_EQ(u2r2::Operation::SubscriptionRemoved, response.operation);
  EXPECT_EQ(31U, response.request_id);
  EXPECT_EQ("error", response.status);
  EXPECT_EQ("unknown_contract", response.error_code);
  EXPECT_TRUE(response.terminal);
  EXPECT_EQ(0U, authority.classified_count());
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
