TEST(Unity2FoxgloveRos2BridgeProtocol, SharedV1AuthorityFixtureMatchesCurrentCppProtocol)
{
  const auto fixture = LoadV1AuthorityFixture();
  ASSERT_EQ(1, fixture.at("fixtureVersion").get<int>());
  const auto & limits = fixture.at("limits");
  EXPECT_EQ(16, limits.at("fixedHeaderBytes").get<int>());
  EXPECT_EQ(kMaxHeaderBytes, limits.at("maxJsonHeaderBytes").get<uint32_t>());
  EXPECT_EQ(kMaxPayloadBytes, limits.at("maxPayloadBytes").get<uint32_t>());
  EXPECT_EQ(1024, limits.at("defaultQueueCapacityFrames").get<int>());
  EXPECT_EQ(68719476736ULL, limits.at("maxQueuedPayloadBytes").get<uint64_t>());
  EXPECT_EQ(1, limits.at("activeConnectionCount").get<int>());
  EXPECT_EQ(4, limits.at("listenBacklog").get<int>());
  EXPECT_EQ(
    std::chrono::milliseconds(5000),
    std::chrono::duration_cast<std::chrono::milliseconds>(kReadStallTimeout));

  const auto & health = fixture.at("health");
  const auto health_request = ReadFixtureFrame(health.at("request"));
  EXPECT_EQ("health_ping", health_request.header.at("op").get<std::string>());
  EXPECT_EQ(
    health.at("requestId").get<std::string>(),
    health_request.header.at("requestId").get<std::string>());
  EXPECT_EQ(
    kHealthProtocolVersion,
    health_request.header.at("protocolVersion").get<int>());
  EXPECT_TRUE(health_request.payload.empty());

  {
    const auto sockets = MakeConnectedSocketPair();
    ASSERT_NE(kInvalidSocket, sockets[0]);
    ASSERT_NE(kInvalidSocket, sockets[1]);
    ScopedFd writer(sockets[0]);
    ScopedFd reader(sockets[1]);
    const auto expected = HexToBytes(
      health.at("response").at("sidecarFrameHex").get<std::string>());
    write_health_pong_ok(writer.get(), health.at("requestId").get<std::string>());
    EXPECT_EQ(expected, ReadSocketBytes(reader.get(), expected.size()));
  }

  const auto & preparation = fixture.at("preparePublisher");
  const auto preparation_request = parse_prepare_publisher_frame(
    ReadFixtureFrame(preparation.at("request")));
  EXPECT_EQ(
    preparation.at("requestId").get<std::string>(),
    preparation_request.request_id);
  EXPECT_EQ(
    preparation.at("topic").get<std::string>(),
    preparation_request.frame.topic);
  EXPECT_EQ(
    preparation.at("schemaName").get<std::string>(),
    preparation_request.frame.schema_name);
  EXPECT_EQ("default", preparation_request.frame.profile);
  EXPECT_EQ("reliable", preparation_request.frame.reliability);
  EXPECT_EQ("volatile", preparation_request.frame.durability);
  EXPECT_EQ("keep_last", preparation_request.frame.history);
  EXPECT_EQ(10, preparation_request.frame.depth);

  {
    const auto sockets = MakeConnectedSocketPair();
    ASSERT_NE(kInvalidSocket, sockets[0]);
    ASSERT_NE(kInvalidSocket, sockets[1]);
    ScopedFd writer(sockets[0]);
    ScopedFd reader(sockets[1]);
    const auto expected = HexToBytes(
      preparation.at("response").at("sidecarFrameHex").get<std::string>());
    write_u2r2_frame(
      writer.get(),
      publisher_ready_ok(preparation_request.request_id),
      {});
    EXPECT_EQ(expected, ReadSocketBytes(reader.get(), expected.size()));
  }

  const auto & publish = fixture.at("publish");
  const auto publish_frame = parse_publish_frame(
    ReadFixtureFrame(publish.at("frame")));
  EXPECT_EQ(publish.at("topic").get<std::string>(), publish_frame.topic);
  EXPECT_EQ(publish.at("schemaName").get<std::string>(), publish_frame.schema_name);
  EXPECT_EQ(publish.at("encoding").get<std::string>(), publish_frame.encoding);
  EXPECT_EQ(publish.at("logTimeNs").get<uint64_t>(), publish_frame.log_time_ns);
  EXPECT_EQ(publish.at("sequence").get<uint64_t>(), publish_frame.sequence);
  EXPECT_EQ(
    HexToBytes(publish.at("payloadHex").get<std::string>()),
    publish_frame.payload);

  const std::array<std::string, 19> expected_negative_ids = {
    "bad_magic",
    "bad_version",
    "bad_flags",
    "oversized_header",
    "oversized_payload",
    "truncated_fixed",
    "truncated_header",
    "truncated_payload",
    "partial_payload_stall",
    "duplicate_operation",
    "unknown_operation",
    "illegal_sequence",
    "invalid_utf8",
    "trailing_json_root",
    "invalid_topic",
    "invalid_type",
    "invalid_delivery_policy",
    "correlation_mismatch",
    "peer_close"
  };
  const auto & negative_vectors = fixture.at("negativeVectors");
  ASSERT_EQ(expected_negative_ids.size(), negative_vectors.size());
  for (size_t index = 0; index < expected_negative_ids.size(); ++index) {
    EXPECT_EQ(
      expected_negative_ids[index],
      negative_vectors[index].at("id").get<std::string>());
    EXPECT_EQ(
      "reject",
      negative_vectors[index].at("expected").get<std::string>());
  }

  const auto & execution =
    fixture.at("v2").at("legacyV1NegativeExecution");
  EXPECT_EQ(1, execution.at("schemaVersion").get<int>());
  EXPECT_EQ("negativeVectors", execution.at("catalog").get<std::string>());
  const auto & executable_vectors = execution.at("vectors");
  ASSERT_EQ(expected_negative_ids.size(), executable_vectors.size());
  for (size_t index = 0; index < executable_vectors.size(); ++index) {
    const auto & vector = executable_vectors[index];
    EXPECT_EQ(
      expected_negative_ids[index],
      vector.at("id").get<std::string>());
    ASSERT_TRUE(vector.at("action").is_string());
    ASSERT_FALSE(vector.at("action").get<std::string>().empty());
    ASSERT_TRUE(vector.at("expectedFailure").is_string());
    ASSERT_FALSE(vector.at("expectedFailure").get<std::string>().empty());
    const auto & consumers = vector.at("consumers");
    ASSERT_TRUE(consumers.is_array());
    ASSERT_FALSE(consumers.empty());
    const auto consumes_cpp =
      std::find(consumers.begin(), consumers.end(), "cpp") !=
      consumers.end();
    if (consumes_cpp) {
      ExpectExecutableV1NegativeRejected(fixture, vector);
    }
  }
}

TEST(
  Unity2FoxgloveRos2BridgeProtocol,
  V2PublisherQosDepthPreservesTheFullUnsigned32BitDomain)
{
  constexpr uint32_t expected_depth = 2147483648U;
  const auto limits = u2r2::ProtocolLimits::defaults();
  const auto wire = u2r2::encode_frame(
    {
      {"op", "prepare_publisher"},
      {"protocolVersion", 2},
      {"requestId", 1},
      {"sessionId", "phase187-qos-domain"},
      {"connectionGeneration", 1},
      {"topic", "/phase187/qos_domain"},
      {"schemaName", "std_msgs/msg/String"},
      {"encoding", "cdr"},
      {"qos", {
          {"profile", "default"},
          {"reliability", "reliable"},
          {"durability", "volatile"},
          {"history", "keep_last"},
          {"depth", static_cast<uint64_t>(expected_depth)},
        }},
    },
    {},
    limits);

  const auto frame = bridge_frame_from_v2_wire(
    wire,
    u2r2::Operation::PreparePublisher,
    limits);
  EXPECT_EQ(expected_depth, frame.depth);
  EXPECT_EQ(
    static_cast<size_t>(expected_depth),
    make_qos(frame).get_rmw_qos_profile().depth);
}

TEST(
  Unity2FoxgloveRos2BridgeProtocol,
  BoundedV2ErrorMessageNeverSplitsUtf8CodePoints)
{
  std::string message(kMaximumResponseErrorBytes - 1U, 'x');
  message += "\xE2\x82\xAC";

  const auto bounded = bounded_response_message(message);

  EXPECT_EQ(kMaximumResponseErrorBytes - 1U, bounded.size());
  EXPECT_NO_THROW(
    (void)u2r2::encode_frame(
      {
        {"op", "publisher_ready"},
        {"protocolVersion", 2},
        {"requestId", 1},
        {"status", "error"},
        {"sessionId", "phase187-utf8-boundary"},
        {"connectionGeneration", 1},
        {"errorCode", "invalid_contract"},
        {"message", bounded},
        {"terminal", false},
      },
      {}));
}

TEST(Unity2FoxgloveRos2BridgeProtocol, ValidatesTopicNames)
{
  EXPECT_TRUE(is_valid_ros2_topic_name("/unity/tf"));
  EXPECT_TRUE(is_valid_ros2_topic_name("/unity2foxglove/point_cloud_2"));
  EXPECT_FALSE(is_valid_ros2_topic_name(""));
  EXPECT_FALSE(is_valid_ros2_topic_name("unity/tf"));
  EXPECT_FALSE(is_valid_ros2_topic_name("/unity//tf"));
  EXPECT_FALSE(is_valid_ros2_topic_name("/unity/tf-with-dash"));
  EXPECT_FALSE(is_valid_ros2_topic_name("/1st/sensor"));
  EXPECT_FALSE(is_valid_ros2_topic_name("/unity/2nd_sensor"));
}

TEST(Unity2FoxgloveRos2BridgeProtocol, AcceptsCanonicalRos2MessageTypes)
{
  const std::array<std::string, 3> message_types = {
    "foxglove_msgs/msg/FrameTransform",
    "sensor_msgs/msg/Image",
    "unity2foxglove_foxrun_interfaces_v1/msg/Phase181State48D288ED82F1Envelope"
  };

  for (const auto & message_type : message_types) {
    SCOPED_TRACE("message_type=" + message_type);
    const auto frame = parse_publish_frame(
      MakePublishRawFrame("/unity/message", message_type));
    EXPECT_EQ(message_type, frame.schema_name);
  }
}

TEST(Unity2FoxgloveRos2BridgeProtocol, RejectsNonCanonicalRos2MessageTypes)
{
  const std::array<std::string, 17> invalid_message_types = {
    "",
    "a/msg/Type",
    "foo__bar/msg/Type",
    "sensor_msgs",
    "sensor_msgs/msg",
    "sensor_msgs/msg/",
    "/sensor_msgs/msg/Image",
    "Sensor_msgs/msg/Image",
    "1sensor_msgs/msg/Image",
    "sensor-msgs/msg/Image",
    "sensor_msgs_/msg/Image",
    "sensor_msgs/srv/Image",
    "sensor_msgs/msg/image",
    "sensor_msgs/msg/Image_Name",
    "sensor_msgs/msg/Image/Extra",
    "sensor_msgs//msg/Image",
    "sensor_msgs/msg/État"
  };

  for (const auto & message_type : invalid_message_types) {
    SCOPED_TRACE("message_type=" + message_type);
    EXPECT_THROW(
      parse_publish_frame(MakePublishRawFrame("/unity/message", message_type)),
      std::runtime_error);
  }
}

TEST(Unity2FoxgloveRos2BridgeProtocol, ParsesDefaultQosContract)
{
  const WireQosContract expected;
  const auto frame = parse_publish_frame(MakePublishRawFrame());

  EXPECT_EQ(expected.profile, frame.profile);
  EXPECT_EQ(expected.reliability, frame.reliability);
  EXPECT_EQ(expected.durability, frame.durability);
  EXPECT_EQ(expected.history, frame.history);
  EXPECT_EQ(expected.depth, frame.depth);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, ParsesSensorDataQosContract)
{
  const WireQosContract expected{
    "sensor_data", "best_effort", "volatile", "keep_last", 5};
  const auto frame = parse_publish_frame(
    MakePublishRawFrame("/unity/qos", "foxglove_msgs/msg/FrameTransform", expected));

  EXPECT_EQ(expected.profile, frame.profile);
  EXPECT_EQ(expected.reliability, frame.reliability);
  EXPECT_EQ(expected.durability, frame.durability);
  EXPECT_EQ(expected.history, frame.history);
  EXPECT_EQ(expected.depth, frame.depth);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, ParsesSystemDefaultQosContract)
{
  const WireQosContract expected{
    "system_default", "system_default", "system_default", "system_default", 0};
  const auto frame = parse_publish_frame(
    MakePublishRawFrame("/unity/qos", "foxglove_msgs/msg/FrameTransform", expected));

  EXPECT_EQ(expected.profile, frame.profile);
  EXPECT_EQ(expected.reliability, frame.reliability);
  EXPECT_EQ(expected.durability, frame.durability);
  EXPECT_EQ(expected.history, frame.history);
  EXPECT_EQ(expected.depth, frame.depth);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, ParsesKeepAllQosContractWithZeroDepth)
{
  const WireQosContract expected{
    "default", "reliable", "transient_local", "keep_all", 0};
  const auto frame = parse_publish_frame(
    MakePublishRawFrame("/unity/qos", "foxglove_msgs/msg/FrameTransform", expected));

  EXPECT_EQ(expected.profile, frame.profile);
  EXPECT_EQ(expected.reliability, frame.reliability);
  EXPECT_EQ(expected.durability, frame.durability);
  EXPECT_EQ(expected.history, frame.history);
  EXPECT_EQ(expected.depth, frame.depth);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, ParsesKeepLastQosContractWithNonDefaultDepth)
{
  const WireQosContract expected{
    "default", "best_effort", "transient_local", "keep_last", 37};
  const auto frame = parse_publish_frame(
    MakePublishRawFrame("/unity/qos", "foxglove_msgs/msg/FrameTransform", expected));

  EXPECT_EQ(expected.profile, frame.profile);
  EXPECT_EQ(expected.reliability, frame.reliability);
  EXPECT_EQ(expected.durability, frame.durability);
  EXPECT_EQ(expected.history, frame.history);
  EXPECT_EQ(expected.depth, frame.depth);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, RejectsInvalidQosProfile)
{
  auto raw = MakePublishRawFrame();
  raw.header["qos"]["profile"] = "unknown_profile";

  EXPECT_THROW(parse_publish_frame(raw), std::runtime_error);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, RejectsInvalidQosReliability)
{
  auto raw = MakePublishRawFrame();
  raw.header["qos"]["reliability"] = "sometimes_reliable";

  EXPECT_THROW(parse_publish_frame(raw), std::runtime_error);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, RejectsInvalidQosDurability)
{
  auto raw = MakePublishRawFrame();
  raw.header["qos"]["durability"] = "persistent";

  EXPECT_THROW(parse_publish_frame(raw), std::runtime_error);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, RejectsInvalidQosHistory)
{
  auto raw = MakePublishRawFrame();
  raw.header["qos"]["history"] = "keep_some";

  EXPECT_THROW(parse_publish_frame(raw), std::runtime_error);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, RejectsNonIntegerQosDepthTypes)
{
  const std::array<nlohmann::json, 3> invalid_depths = {
    nlohmann::json("10"),
    nlohmann::json(10.0),
    nlohmann::json(10.5)
  };
  for (const auto & invalid_depth : invalid_depths) {
    SCOPED_TRACE("depth=" + invalid_depth.dump());
    auto raw = MakePublishRawFrame();
    raw.header["qos"]["depth"] = invalid_depth;
    EXPECT_THROW(parse_publish_frame(raw), std::runtime_error);
  }
}

TEST(Unity2FoxgloveRos2BridgeProtocol, RejectsQosDepthOutsideUnsigned32BitDomain)
{
  const auto above_uint32 =
    static_cast<uint64_t>(std::numeric_limits<uint32_t>::max()) + 1U;
  const std::array<nlohmann::json, 2> invalid_depths = {
    nlohmann::json(above_uint32),
    nlohmann::json(-1)
  };
  for (const auto & invalid_depth : invalid_depths) {
    SCOPED_TRACE("depth=" + invalid_depth.dump());
    auto raw = MakePublishRawFrame();
    raw.header["qos"]["depth"] = invalid_depth;
    EXPECT_THROW(parse_publish_frame(raw), std::runtime_error);
  }
}

TEST(Unity2FoxgloveRos2BridgeProtocol, RejectsNonPositiveKeepLastDepth)
{
  auto zero_depth = MakePublishRawFrame();
  zero_depth.header["qos"]["depth"] = 0;
  EXPECT_THROW(parse_publish_frame(zero_depth), std::runtime_error);

  auto negative_depth = MakePublishRawFrame();
  negative_depth.header["qos"]["depth"] = -1;
  EXPECT_THROW(parse_publish_frame(negative_depth), std::runtime_error);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, RejectsNonzeroDepthForNonKeepLastHistory)
{
  const std::array<std::string, 2> histories = {"keep_all", "system_default"};
  for (const auto & history : histories) {
    SCOPED_TRACE("history=" + history);
    auto raw = MakePublishRawFrame();
    raw.header["qos"]["history"] = history;
    raw.header["qos"]["depth"] = 1;
    EXPECT_THROW(parse_publish_frame(raw), std::runtime_error);
  }
}

TEST(Unity2FoxgloveRos2BridgeProtocol, RejectsMissingRequiredQosFields)
{
  const std::array<std::string, 5> required_fields = {
    "profile", "reliability", "durability", "history", "depth"};
  for (const auto & field : required_fields) {
    SCOPED_TRACE("field=" + field);
    auto raw = MakePublishRawFrame();
    raw.header["qos"].erase(field);
    EXPECT_THROW(parse_publish_frame(raw), std::runtime_error);
  }
}

TEST(Unity2FoxgloveRos2BridgeProtocol, DefaultsMissingQosObjectForLegacyPublishers)
{
  const WireQosContract expected;
  auto raw = MakePublishRawFrame();
  raw.header.erase("qos");

  const auto frame = parse_publish_frame(raw);
  EXPECT_EQ(expected.profile, frame.profile);
  EXPECT_EQ(expected.reliability, frame.reliability);
  EXPECT_EQ(expected.durability, frame.durability);
  EXPECT_EQ(expected.history, frame.history);
  EXPECT_EQ(expected.depth, frame.depth);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, DefaultsNullQosObjectForLegacyPublishers)
{
  const WireQosContract expected;
  auto raw = MakePublishRawFrame();
  raw.header["qos"] = nullptr;

  const auto frame = parse_publish_frame(raw);
  EXPECT_EQ(expected.profile, frame.profile);
  EXPECT_EQ(expected.reliability, frame.reliability);
  EXPECT_EQ(expected.durability, frame.durability);
  EXPECT_EQ(expected.history, frame.history);
  EXPECT_EQ(expected.depth, frame.depth);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, RejectsNonObjectQosValue)
{
  auto raw = MakePublishRawFrame();
  raw.header["qos"] = "default";

  EXPECT_THROW(parse_publish_frame(raw), std::runtime_error);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, ForwardsEncapsulatedPayloadByView)
{
  auto frame = parse_publish_frame(MakePublishRawFrame());
  std::vector<uint8_t> scratch;
  const auto payload = payload_for_publish(frame, PayloadFormat::CdrWithEncapsulation, scratch);

  EXPECT_TRUE(scratch.empty());
  ASSERT_EQ(frame.payload.size(), payload.size);
  EXPECT_EQ(frame.payload.data(), payload.data);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, PrependsEncapsulationForBodyOnlyPayload)
{
  auto frame = parse_publish_frame(MakePublishRawFrame());
  frame.payload = {0x10, 0x20, 0x30};
  std::vector<uint8_t> scratch;
  const auto payload = payload_for_publish(frame, PayloadFormat::CdrBodyOnly, scratch);

  const std::vector<uint8_t> expected = {0x00, 0x01, 0x00, 0x00, 0x10, 0x20, 0x30};
  ASSERT_EQ(expected.size(), payload.size);
  EXPECT_EQ(expected, scratch);
  EXPECT_EQ(scratch.data(), payload.data);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, RejectsEncapsulatedBodyOnlyPayload)
{
  auto frame = parse_publish_frame(MakePublishRawFrame());
  std::vector<uint8_t> scratch;

  EXPECT_THROW(payload_for_publish(frame, PayloadFormat::CdrBodyOnly, scratch), std::runtime_error);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, ParsesCorrelatedZeroPayloadPreparePublisherContract)
{
  const WireQosContract qos{
    "sensor_data", "best_effort", "transient_local", "keep_last", 37};
  const auto request = parse_prepare_publisher_frame(
    MakePreparePublisherRawFrame(
      "phase184-prepare-exact",
      "/unity/custom",
      "unity2foxglove_foxrun_interfaces_v1/msg/Phase181State48D288ED82F1Envelope",
      qos));

  EXPECT_EQ("phase184-prepare-exact", request.request_id);
  EXPECT_EQ(1, request.protocol_version);
  EXPECT_EQ("/unity/custom", request.frame.topic);
  EXPECT_EQ(
    "unity2foxglove_foxrun_interfaces_v1/msg/Phase181State48D288ED82F1Envelope",
    request.frame.schema_name);
  EXPECT_EQ("cdr", request.frame.encoding);
  EXPECT_EQ(qos.profile, request.frame.profile);
  EXPECT_EQ(qos.reliability, request.frame.reliability);
  EXPECT_EQ(qos.durability, request.frame.durability);
  EXPECT_EQ(qos.history, request.frame.history);
  EXPECT_EQ(qos.depth, request.frame.depth);
  EXPECT_TRUE(request.frame.payload.empty());
}

TEST(Unity2FoxgloveRos2BridgeProtocol, RejectsPreparePublisherPayloadAndProtocolMismatch)
{
  auto payload = MakePreparePublisherRawFrame();
  payload.payload = {0x00};
  EXPECT_THROW(parse_prepare_publisher_frame(payload), std::runtime_error);

  auto unsupported = MakePreparePublisherRawFrame();
  unsupported.header["protocolVersion"] = 2;
  EXPECT_THROW(parse_prepare_publisher_frame(unsupported), std::runtime_error);
}

TEST(Unity2FoxgloveRos2BridgeProtocol, PreparePublisherProtocolVersionRequiresJsonInteger)
{
  const std::array<nlohmann::json, 3> invalid_versions = {
    nlohmann::json(1.0),
    nlohmann::json("1"),
    nlohmann::json(true)
  };

  for (const auto & invalid_version : invalid_versions) {
    SCOPED_TRACE("protocolVersion=" + invalid_version.dump());
    auto raw = MakePreparePublisherRawFrame();
    raw.header["protocolVersion"] = invalid_version;
    EXPECT_THROW(parse_prepare_publisher_frame(raw), std::runtime_error);
  }
}

TEST(Unity2FoxgloveRos2BridgeProtocol, PreparePublisherProtocolVersionRejectsNarrowingBoundaries)
{
  const std::array<nlohmann::json, 6> unsupported_versions = {
    nlohmann::json(-1),
    nlohmann::json(0),
    nlohmann::json(2),
    nlohmann::json(std::numeric_limits<int32_t>::max()),
    nlohmann::json(static_cast<uint64_t>(std::numeric_limits<uint32_t>::max()) + 2U),
    nlohmann::json(std::numeric_limits<uint64_t>::max())
  };

  for (const auto & unsupported_version : unsupported_versions) {
    SCOPED_TRACE("protocolVersion=" + unsupported_version.dump());
    auto raw = MakePreparePublisherRawFrame();
    raw.header["protocolVersion"] = unsupported_version;
    EXPECT_THROW(parse_prepare_publisher_frame(raw), std::runtime_error);
  }
}

TEST(Unity2FoxgloveRos2BridgeProtocol, PreparePublisherRequiresExplicitCompleteQos)
{
  auto missing_qos = MakePreparePublisherRawFrame();
  missing_qos.header.erase("qos");
  EXPECT_THROW(parse_prepare_publisher_frame(missing_qos), std::runtime_error);

  auto partial_qos = MakePreparePublisherRawFrame();
  partial_qos.header["qos"].erase("history");
  EXPECT_THROW(parse_prepare_publisher_frame(partial_qos), std::runtime_error);
}

TEST(
  Unity2FoxgloveRos2BridgeProtocol,
  PreparePublisherIsIdempotentAndLegacyPublishReusesPreparedPublisher)
{
  size_t create_count = 0;
  size_t publish_count = 0;
  GenericPublisherFactory factory =
    [&](const std::string &, const std::string &, const rclcpp::QoS &) {
      ++create_count;
      return [&](const rclcpp::SerializedMessage &) {
          ++publish_count;
        };
    };
  BridgeNode bridge(PayloadFormat::CdrWithEncapsulation, std::move(factory));
  const auto frame = parse_prepare_publisher_frame(
    MakePreparePublisherRawFrame()).frame;

  EXPECT_EQ(PublisherContractDisposition::CreatePublisher, bridge.prepare(frame));
  EXPECT_EQ(PublisherContractDisposition::ReusePublisher, bridge.prepare(frame));
  EXPECT_EQ(1U, create_count);
  EXPECT_EQ(0U, publish_count);
