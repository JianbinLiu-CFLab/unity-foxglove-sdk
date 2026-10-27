TEST(Unity2FoxgloveRos2BridgeProtocol, PublisherReuseSignatureCapturesSchemaAndEveryQosField)
{
  const auto baseline = parse_publish_frame(MakePublishRawFrame());
  const auto baseline_signature = qos_signature(baseline);

  auto changed = baseline;
  changed.schema_name = "foxglove_msgs/msg/CompressedImage";
  EXPECT_NE(baseline_signature, qos_signature(changed));

  changed = baseline;
  changed.profile = "sensor_data";
  EXPECT_NE(baseline_signature, qos_signature(changed));

  changed = baseline;
  changed.reliability = "best_effort";
  EXPECT_NE(baseline_signature, qos_signature(changed));

  changed = baseline;
  changed.durability = "transient_local";
  EXPECT_NE(baseline_signature, qos_signature(changed));

  const auto keep_all = parse_publish_frame(
    MakePublishRawFrame(
      "/unity/tf",
      "foxglove_msgs/msg/FrameTransform",
      WireQosContract{"default", "reliable", "volatile", "keep_all", 0}));
  const auto system_default_history = parse_publish_frame(
    MakePublishRawFrame(
      "/unity/tf",
      "foxglove_msgs/msg/FrameTransform",
      WireQosContract{"default", "reliable", "volatile", "system_default", 0}));
  EXPECT_EQ(keep_all.topic, system_default_history.topic);
  EXPECT_EQ(keep_all.schema_name, system_default_history.schema_name);
  EXPECT_NE(qos_signature(keep_all), qos_signature(system_default_history));

  changed = baseline;
  changed.depth = 37;
  EXPECT_NE(baseline_signature, qos_signature(changed));
}

TEST(Unity2FoxgloveRos2BridgeProtocol, PublisherContractRegistryReusesIdenticalContract)
{
  PublisherContractRegistry registry;
  const auto first = parse_publish_frame(MakePublishRawFrame());
  auto repeated_raw = MakePublishRawFrame();
  repeated_raw.header["logTimeNs"] = 5678;
  repeated_raw.header["sequence"] = 8;
  repeated_raw.payload = {0x00, 0x01, 0x00, 0x00, 0x30, 0x40};
  const auto repeated = parse_publish_frame(repeated_raw);

  EXPECT_EQ(
    PublisherContractDisposition::CreatePublisher,
    registry.register_or_validate(first));
  EXPECT_EQ(
    PublisherContractDisposition::ReusePublisher,
    registry.register_or_validate(repeated));
}

TEST(Unity2FoxgloveRos2BridgeProtocol, PublisherContractRegistryKeepsTopicsIndependent)
{
  PublisherContractRegistry registry;
  const auto topic_a = parse_publish_frame(MakePublishRawFrame("/unity/topic_a"));
  const auto topic_b = parse_publish_frame(
    MakePublishRawFrame(
      "/unity/topic_b",
      "foxglove_msgs/msg/FrameTransform",
      WireQosContract{"sensor_data", "best_effort", "volatile", "keep_last", 5}));

  EXPECT_EQ(
    PublisherContractDisposition::CreatePublisher,
    registry.register_or_validate(topic_a));
  EXPECT_EQ(
    PublisherContractDisposition::CreatePublisher,
    registry.register_or_validate(topic_b));
  EXPECT_EQ(
    PublisherContractDisposition::ReusePublisher,
    registry.register_or_validate(topic_a));
  EXPECT_EQ(
    PublisherContractDisposition::ReusePublisher,
    registry.register_or_validate(topic_b));
}

TEST(
  Unity2FoxgloveRos2BridgeProtocol,
  PublisherContractRegistryRejectsSchemaAndEveryQosConflictWithoutMutation)
{
  const auto baseline = parse_publish_frame(MakePublishRawFrame());

  auto changed_schema = baseline;
  changed_schema.schema_name = "foxglove_msgs/msg/CompressedImage";
  ExpectPublisherContractConflictRejectedWithoutMutation(baseline, changed_schema);

  const auto changed_profile = parse_publish_frame(
    MakePublishRawFrame(
      "/unity/tf",
      "foxglove_msgs/msg/FrameTransform",
      WireQosContract{"sensor_data", "reliable", "volatile", "keep_last", 10}));
  ExpectPublisherContractConflictRejectedWithoutMutation(baseline, changed_profile);

  const auto changed_reliability = parse_publish_frame(
    MakePublishRawFrame(
      "/unity/tf",
      "foxglove_msgs/msg/FrameTransform",
      WireQosContract{"default", "best_effort", "volatile", "keep_last", 10}));
  ExpectPublisherContractConflictRejectedWithoutMutation(baseline, changed_reliability);

  const auto changed_durability = parse_publish_frame(
    MakePublishRawFrame(
      "/unity/tf",
      "foxglove_msgs/msg/FrameTransform",
      WireQosContract{"default", "reliable", "transient_local", "keep_last", 10}));
  ExpectPublisherContractConflictRejectedWithoutMutation(baseline, changed_durability);

  const auto keep_all = parse_publish_frame(
    MakePublishRawFrame(
      "/unity/tf",
      "foxglove_msgs/msg/FrameTransform",
      WireQosContract{"default", "reliable", "volatile", "keep_all", 0}));
  const auto system_default_history = parse_publish_frame(
    MakePublishRawFrame(
      "/unity/tf",
      "foxglove_msgs/msg/FrameTransform",
      WireQosContract{"default", "reliable", "volatile", "system_default", 0}));
  ExpectPublisherContractConflictRejectedWithoutMutation(keep_all, system_default_history);

  const auto changed_depth = parse_publish_frame(
    MakePublishRawFrame(
      "/unity/tf",
      "foxglove_msgs/msg/FrameTransform",
      WireQosContract{"default", "reliable", "volatile", "keep_last", 37}));
  ExpectPublisherContractConflictRejectedWithoutMutation(baseline, changed_depth);
}

TEST(
  Unity2FoxgloveRos2BridgeProtocol,
  IndependentBridgeNodeSessionsAcceptReplacementQosForTheSameTopic)
{
  const auto first = parse_publish_frame(MakePublishRawFrame());
  const auto replacement = parse_publish_frame(
    MakePublishRawFrame(
      "/unity/tf",
      "foxglove_msgs/msg/FrameTransform",
      WireQosContract{"sensor_data", "best_effort", "volatile", "keep_last", 5}));
  size_t first_create_count = 0;
  size_t first_publish_count = 0;
  size_t replacement_create_count = 0;
  size_t replacement_publish_count = 0;

  {
    GenericPublisherFactory factory =
      [&](const std::string &, const std::string &, const rclcpp::QoS &) {
        ++first_create_count;
        return [&](const rclcpp::SerializedMessage &) {
            ++first_publish_count;
          };
      };
    BridgeNode first_client(PayloadFormat::CdrWithEncapsulation, std::move(factory));
    EXPECT_NO_THROW(first_client.publish(first));
    EXPECT_THROW(
      first_client.publish(replacement),
      std::runtime_error);
  }

  {
    GenericPublisherFactory factory =
      [&](const std::string &, const std::string &, const rclcpp::QoS &) {
        ++replacement_create_count;
        return [&](const rclcpp::SerializedMessage &) {
            ++replacement_publish_count;
          };
      };
    BridgeNode replacement_client(PayloadFormat::CdrWithEncapsulation, std::move(factory));
    EXPECT_NO_THROW(replacement_client.publish(replacement));
  }

  EXPECT_EQ(1U, first_create_count);
  EXPECT_EQ(1U, first_publish_count);
  EXPECT_EQ(1U, replacement_create_count);
  EXPECT_EQ(1U, replacement_publish_count);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, MakesCanonicalDefaultQos)
{
  const auto qos = MakeRmwQosProfile(WireQosContract{});

  EXPECT_EQ(RMW_QOS_POLICY_RELIABILITY_RELIABLE, qos.reliability);
  EXPECT_EQ(RMW_QOS_POLICY_DURABILITY_VOLATILE, qos.durability);
  EXPECT_EQ(RMW_QOS_POLICY_HISTORY_KEEP_LAST, qos.history);
  EXPECT_EQ(10U, qos.depth);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, MakesSensorDataQos)
{
  const auto qos = MakeRmwQosProfile(
    WireQosContract{"sensor_data", "best_effort", "volatile", "keep_last", 5});

  EXPECT_EQ(RMW_QOS_POLICY_RELIABILITY_BEST_EFFORT, qos.reliability);
  EXPECT_EQ(RMW_QOS_POLICY_DURABILITY_VOLATILE, qos.durability);
  EXPECT_EQ(RMW_QOS_POLICY_HISTORY_KEEP_LAST, qos.history);
  EXPECT_EQ(5U, qos.depth);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, MakesSystemDefaultQosWithoutDowngrade)
{
  const auto qos = MakeRmwQosProfile(
    WireQosContract{
      "system_default", "system_default", "system_default", "system_default", 0});

  EXPECT_EQ(RMW_QOS_POLICY_RELIABILITY_SYSTEM_DEFAULT, qos.reliability);
  EXPECT_EQ(RMW_QOS_POLICY_DURABILITY_SYSTEM_DEFAULT, qos.durability);
  EXPECT_EQ(RMW_QOS_POLICY_HISTORY_SYSTEM_DEFAULT, qos.history);
  EXPECT_EQ(0U, qos.depth);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, MakesDefaultProfileWithSystemDefaultOverrides)
{
  const auto qos = MakeRmwQosProfile(
    WireQosContract{
      "default", "system_default", "system_default", "system_default", 0});

  EXPECT_EQ(RMW_QOS_POLICY_RELIABILITY_SYSTEM_DEFAULT, qos.reliability);
  EXPECT_EQ(RMW_QOS_POLICY_DURABILITY_SYSTEM_DEFAULT, qos.durability);
  EXPECT_EQ(RMW_QOS_POLICY_HISTORY_SYSTEM_DEFAULT, qos.history);
  EXPECT_EQ(0U, qos.depth);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, MakesSystemDefaultProfileWithExplicitOverrides)
{
  const auto qos = MakeRmwQosProfile(
    WireQosContract{
      "system_default", "reliable", "transient_local", "keep_last", 37});

  EXPECT_EQ(RMW_QOS_POLICY_RELIABILITY_RELIABLE, qos.reliability);
  EXPECT_EQ(RMW_QOS_POLICY_DURABILITY_TRANSIENT_LOCAL, qos.durability);
  EXPECT_EQ(RMW_QOS_POLICY_HISTORY_KEEP_LAST, qos.history);
  EXPECT_EQ(37U, qos.depth);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, MakesKeepAllQosWithoutSynthesizingDepth)
{
  const auto qos = MakeRmwQosProfile(
    WireQosContract{"default", "reliable", "transient_local", "keep_all", 0});

  EXPECT_EQ(RMW_QOS_POLICY_RELIABILITY_RELIABLE, qos.reliability);
  EXPECT_EQ(RMW_QOS_POLICY_DURABILITY_TRANSIENT_LOCAL, qos.durability);
  EXPECT_EQ(RMW_QOS_POLICY_HISTORY_KEEP_ALL, qos.history);
  EXPECT_EQ(0U, qos.depth);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, MakesKeepLastQosWithNonDefaultDepth)
{
  const auto qos = MakeRmwQosProfile(
    WireQosContract{"default", "best_effort", "volatile", "keep_last", 37});

  EXPECT_EQ(RMW_QOS_POLICY_RELIABILITY_BEST_EFFORT, qos.reliability);
  EXPECT_EQ(RMW_QOS_POLICY_DURABILITY_VOLATILE, qos.durability);
  EXPECT_EQ(RMW_QOS_POLICY_HISTORY_KEEP_LAST, qos.history);
  EXPECT_EQ(37U, qos.depth);
}

TEST(
  Unity2FoxgloveRos2BridgeProtocol,
  GenericSubscriptionReceivesExactExternalSerializedCdr)
{
  auto context = std::make_shared<rclcpp::Context>();
  context->init(0, nullptr);
  bridge_runtime::ProcessRosOwner ros_owner(
    "phase186_d_sidecar_subscription_probe",
    context);
  BridgeNode bridge(
    ros_owner.require_node(),
    PayloadFormat::CdrWithEncapsulation);
  const u2r2::ContractIdentity identity(
    u2r2::ContractKey(41U, 7U),
    u2r2::ContractDirection::subscribe,
    "/phase186/d/external",
    "std_msgs/msg/String",
    u2r2::Qos{
      "default", "reliable", "volatile", "keep_last", 10U});

  std::mutex received_mutex;
  std::condition_variable received_changed;
  std::vector<uint8_t> received;
  uint64_t receive_time_ns = 0;
  auto subscription = bridge.subscribe(
    identity,
    [&](const uint8_t * data,
      size_t size,
      uint64_t time_ns,
      bridge_runtime::BridgeSampleOrigin origin) {
      EXPECT_EQ(bridge_runtime::BridgeSampleOrigin::external, origin);
      {
        std::lock_guard<std::mutex> lock(received_mutex);
        received.assign(data, data + size);
        receive_time_ns = time_ns;
      }
      received_changed.notify_all();
      return bridge_runtime::BridgeSerializedAdmission::accepted;
    });

  rclcpp::NodeOptions options;
  options.context(context);
  auto external = std::make_shared<rclcpp::Node>(
    "phase186_d_external_publisher_probe",
    options);
  auto publisher = external->create_generic_publisher(
    identity.topic,
    identity.schema_name,
    rclcpp::QoS(10));
  const auto discovery_deadline =
    std::chrono::steady_clock::now() + std::chrono::seconds(5);
  while (
    publisher->get_subscription_count() == 0 &&
    std::chrono::steady_clock::now() < discovery_deadline)
  {
    std::this_thread::sleep_for(std::chrono::milliseconds(10));
  }
  ASSERT_GT(publisher->get_subscription_count(), 0U);

  const std::vector<uint8_t> payload{
    0x00U, 0x01U, 0x00U, 0x00U, 0x02U, 0x00U, 0x00U, 0x00U,
    0x41U, 0x00U};
  rclcpp::SerializedMessage serialized(payload.size());
  auto & raw = serialized.get_rcl_serialized_message();
  ASSERT_GE(raw.buffer_capacity, payload.size());
  std::memcpy(raw.buffer, payload.data(), payload.size());
  raw.buffer_length = payload.size();
  publisher->publish(serialized);

  {
    std::unique_lock<std::mutex> lock(received_mutex);
    ASSERT_TRUE(received_changed.wait_for(
        lock,
        std::chrono::seconds(5),
        [&]() {return !received.empty();}));
  }
  EXPECT_EQ(payload, received);
  EXPECT_GT(receive_time_ns, 0U);

  subscription.reset();
  publisher.reset();
  external.reset();
  EXPECT_TRUE(ros_owner.stop());
  context->shutdown("Phase186-D external subscription probe complete");
}

TEST(
  Unity2FoxgloveRos2BridgeProtocol,
  OwnedV2SubscriptionStreamsExternalCdrThroughTheSingleSocketWriter)
{
  auto context = std::make_shared<rclcpp::Context>();
  context->init(0, nullptr);
  bridge_runtime::ProcessRosOwner ros_owner(
    "phase186_d_owned_subscription_probe",
    context);
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
    [&]() {
      return std::make_unique<BridgeNode>(
        ros_owner.require_node(),
        PayloadFormat::CdrWithEncapsulation);
    };
  std::thread server(
    [&]() {
      try {
        process_owned_client(
          server_socket.get(),
          authority,
          generation_factory,
          rclcpp::get_logger("phase186_d_owned_subscription_test"),
          [context]() {return rclcpp::ok(context);});
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
        {"requestId", 1},
        {"clientName", "phase186-d-subscription"},
        {"capabilities", nlohmann::json::array({"subscribe"})},
      },
      {}));
  const auto hello_ack = u2r2::parse_v2(
    u2r2::decode_frame(ReadSocketWireFrame(client_socket.get())));
  ASSERT_EQ(u2r2::Operation::HelloAck, hello_ack.operation);

  const std::string topic = "/phase186/d/owned_external";
  const std::string schema = "std_msgs/msg/String";
  const auto registration = u2r2::encode_frame(
    {
      {"op", "register_subscription"},
      {"protocolVersion", 2},
      {"requestId", 2},
      {"sessionId", hello_ack.session_id},
      {"connectionGeneration", hello_ack.connection_generation},
      {"contractId", 41},
      {"topic", topic},
      {"schemaName", schema},
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
  write_all(client_socket.get(), registration);
  const auto ready = u2r2::parse_v2(
    u2r2::decode_frame(ReadSocketWireFrame(client_socket.get())));
  ASSERT_EQ(u2r2::Operation::SubscriptionReady, ready.operation);
  ASSERT_EQ("ok", ready.status);
  ASSERT_EQ(41U, ready.contract_id);

  rclcpp::NodeOptions options;
  options.context(context);
  auto external = std::make_shared<rclcpp::Node>(
    "phase186_d_owned_external_publisher",
    options);
  auto publisher = external->create_generic_publisher(
    topic,
    schema,
    rclcpp::QoS(10));
  const auto discovery_deadline =
    std::chrono::steady_clock::now() + std::chrono::seconds(5);
  while (
    publisher->get_subscription_count() == 0 &&
    std::chrono::steady_clock::now() < discovery_deadline)
  {
    std::this_thread::sleep_for(std::chrono::milliseconds(10));
  }
  ASSERT_GT(publisher->get_subscription_count(), 0U);

  const std::vector<uint8_t> payload{
    0x00U, 0x01U, 0x00U, 0x00U, 0x02U, 0x00U, 0x00U, 0x00U,
    0x41U, 0x00U};
  rclcpp::SerializedMessage serialized(payload.size());
  auto & raw = serialized.get_rcl_serialized_message();
  ASSERT_GE(raw.buffer_capacity, payload.size());
  std::memcpy(raw.buffer, payload.data(), payload.size());
  raw.buffer_length = payload.size();
  publisher->publish(serialized);

  const auto message_wire = ReadSocketWireFrame(client_socket.get());
  const auto message_frame = u2r2::decode_frame(message_wire);
  const auto message = u2r2::parse_v2(message_frame);
  EXPECT_EQ(u2r2::Operation::Message, message.operation);
  EXPECT_EQ(41U, message.contract_id);
  EXPECT_EQ(1U, message.sequence);
  EXPECT_EQ(topic, message.topic);
  EXPECT_EQ(schema, message.schema_name);
  EXPECT_EQ(payload, message_frame.payload);

  write_all(
    client_socket.get(),
    u2r2::encode_frame(
      {
        {"op", "unregister_subscription"},
        {"protocolVersion", 2},
        {"requestId", 3},
        {"sessionId", hello_ack.session_id},
        {"connectionGeneration", hello_ack.connection_generation},
        {"contractId", 41},
      },
      {}));
  const auto removed = u2r2::parse_v2(
    u2r2::decode_frame(ReadSocketWireFrame(client_socket.get())));
  EXPECT_EQ(u2r2::Operation::SubscriptionRemoved, removed.operation);
  EXPECT_EQ("ok", removed.status);
  EXPECT_EQ(41U, removed.contract_id);

  const auto removal_deadline =
    std::chrono::steady_clock::now() + std::chrono::seconds(5);
  while (
    publisher->get_subscription_count() != 0 &&
    std::chrono::steady_clock::now() < removal_deadline)
  {
    std::this_thread::sleep_for(std::chrono::milliseconds(10));
  }
  EXPECT_EQ(0U, publisher->get_subscription_count());

  EXPECT_EQ(0, ShutdownSocketWrite(client_socket.get()));
  server.join();
  ASSERT_EQ(nullptr, server_error);
  EXPECT_EQ(0U, authority.classified_count());

  publisher.reset();
  external.reset();
  EXPECT_TRUE(ros_owner.stop());
  context->shutdown("Phase186-D owned subscription probe complete");
}
