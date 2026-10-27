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
  EXPECT_EQ(wire.size(), protocol.transient_bytes());

  write_all(
    client_socket.get(),
    std::vector<uint8_t>(wire.begin() + 16, wire.end()));
  reader.join();

  ASSERT_EQ(nullptr, reader_error);
  ASSERT_TRUE(received.has_value());
  EXPECT_EQ(wire, received->bytes);
  EXPECT_EQ(wire.size(), protocol.in_flight_bytes());
  EXPECT_EQ(wire.size(), protocol.transient_bytes());
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
