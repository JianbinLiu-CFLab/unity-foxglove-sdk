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

using BridgeAdmissionDiagnosticSink = std::function<void(
    const std::string &,
    bridge_runtime::BridgeSerializedAdmission,
    uint64_t)>;

const char * bridge_admission_name(
  bridge_runtime::BridgeSerializedAdmission admission)
{
  using Admission = bridge_runtime::BridgeSerializedAdmission;
  switch (admission) {
    case Admission::accepted:
      return "accepted";
    case Admission::inactive:
      return "inactive";
    case Admission::unsupported_representation:
      return "unsupported_representation";
    case Admission::payload_too_large:
      return "payload_too_large";
    case Admission::capacity_rejected:
      return "capacity_rejected";
    case Admission::suppressed_local:
      return "suppressed_local";
    case Admission::invalid_origin:
      return "invalid_origin";
  }
  return "unknown";
}

enum class BridgeAdmissionLogLevel
{
  debug,
  warning,
};

constexpr BridgeAdmissionLogLevel bridge_admission_log_level(
  bridge_runtime::BridgeSerializedAdmission admission) noexcept
{
  if (admission ==
    bridge_runtime::BridgeSerializedAdmission::suppressed_local)
  {
    return BridgeAdmissionLogLevel::debug;
  }
  return BridgeAdmissionLogLevel::warning;
}

class BridgeAdmissionDiagnostics final
{
public:
  BridgeAdmissionDiagnostics(
    std::string topic,
    BridgeAdmissionDiagnosticSink sink)
  : topic_(std::move(topic)), sink_(std::move(sink))
  {
  }

  void observe(bridge_runtime::BridgeSerializedAdmission admission) noexcept
  {
    const auto index = rejection_index(admission);
    if (!index || !sink_) {
      return;
    }
    const auto count = saturating_increment(counts_[*index]);
    if ((count & (count - 1U)) != 0U) {
      return;
    }
    try {
      sink_(topic_, admission, count);
    } catch (...) {
      // Diagnostics must never turn a rejected sample into a callback failure.
    }
  }

private:
  static std::optional<size_t> rejection_index(
    bridge_runtime::BridgeSerializedAdmission admission) noexcept
  {
    using Admission = bridge_runtime::BridgeSerializedAdmission;
    switch (admission) {
      case Admission::inactive:
        return 0U;
      case Admission::unsupported_representation:
        return 1U;
      case Admission::payload_too_large:
        return 2U;
      case Admission::capacity_rejected:
        return 3U;
      case Admission::suppressed_local:
        return 4U;
      case Admission::invalid_origin:
        return 5U;
      case Admission::accepted:
        return std::nullopt;
    }
    return std::nullopt;
  }

  static uint64_t saturating_increment(
    std::atomic<uint64_t> & counter) noexcept
  {
    auto current = counter.load(std::memory_order_relaxed);
    while (current != std::numeric_limits<uint64_t>::max()) {
      if (counter.compare_exchange_weak(
          current,
          current + 1U,
          std::memory_order_relaxed,
          std::memory_order_relaxed))
      {
        return current + 1U;
      }
    }
    return current;
  }

  std::string topic_;
  BridgeAdmissionDiagnosticSink sink_;
  std::array<std::atomic<uint64_t>, 6U> counts_{};
};

enum class BridgePublisherLifecycleStage
{
  created,
  retired,
};

using BridgePublisherLifecycleObserver = std::function<void(
    BridgePublisherLifecycleStage,
    const rmw_gid_t &,
    std::weak_ptr<rclcpp::GenericPublisher>,
    std::weak_ptr<bridge_runtime::BridgeOriginRegistry>)>;

class BridgeLocalPublisher final
{
public:
  BridgeLocalPublisher(
    std::shared_ptr<rclcpp::GenericPublisher> publisher,
    std::shared_ptr<bridge_runtime::BridgeOriginRegistry> origin_registry,
    BridgePublisherLifecycleObserver lifecycle_observer)
  : publisher_(std::move(publisher)),
    origin_registry_(std::move(origin_registry)),
    lifecycle_observer_(std::move(lifecycle_observer))
  {
    if (!publisher_) {
      throw std::invalid_argument("Bridge local publisher is required");
    }
    if (!origin_registry_) {
      throw std::invalid_argument("Bridge origin registry is required");
    }
    gid_ = publisher_->get_gid();
    notify(BridgePublisherLifecycleStage::created, publisher_, origin_registry_);
  }

  ~BridgeLocalPublisher()
  {
    const std::weak_ptr<rclcpp::GenericPublisher> publisher_lifetime =
      publisher_;
    const std::weak_ptr<bridge_runtime::BridgeOriginRegistry>
      registry_lifetime = origin_registry_;
    // Keep origin authority alive until the publisher can no longer emit.
    publisher_.reset();
    notify(
      BridgePublisherLifecycleStage::retired,
      publisher_lifetime,
      registry_lifetime);
  }

  void publish(const rclcpp::SerializedMessage & message)
  {
    std::call_once(
      origin_registered_,
      [&]() {
        origin_registry_->register_local(gid_);
      });
    publisher_->publish(message);
  }

private:
  void notify(
    BridgePublisherLifecycleStage stage,
    std::weak_ptr<rclcpp::GenericPublisher> publisher,
    std::weak_ptr<bridge_runtime::BridgeOriginRegistry> registry) noexcept
  {
    if (!lifecycle_observer_) {
      return;
    }
    try {
      lifecycle_observer_(stage, gid_, std::move(publisher), std::move(registry));
    } catch (...) {
      // Test/diagnostic observation cannot alter publisher ownership.
    }
  }

  std::shared_ptr<rclcpp::GenericPublisher> publisher_;
  std::shared_ptr<bridge_runtime::BridgeOriginRegistry> origin_registry_;
  BridgePublisherLifecycleObserver lifecycle_observer_;
  std::once_flag origin_registered_;
  rmw_gid_t gid_{};
};

class BridgeNode
{
public:
  explicit BridgeNode(
    rclcpp::Node::SharedPtr node,
    PayloadFormat payload_format,
    uint64_t maximum_publishers =
    u2r2::ProtocolLimits::defaults().max_contracts(),
    BridgeAdmissionDiagnosticSink admission_diagnostic_sink = {},
    BridgePublisherLifecycleObserver publisher_lifecycle_observer = {})
  : node_(std::move(node)),
    payload_format_(payload_format),
    origin_registry_(
      std::make_shared<bridge_runtime::BridgeOriginRegistry>(
        maximum_publishers)),
    admission_diagnostic_sink_(std::move(admission_diagnostic_sink)),
    maximum_publishers_(maximum_publishers)
  {
    if (!node_) {
      throw std::invalid_argument("bridge node is required");
    }
    if (maximum_publishers_ == 0) {
      throw std::invalid_argument("publisher capacity must be positive");
    }
    if (!admission_diagnostic_sink_) {
      const auto logger = node_->get_logger();
      admission_diagnostic_sink_ =
        [logger](
        const std::string & topic,
        bridge_runtime::BridgeSerializedAdmission admission,
        uint64_t count) {
          if (bridge_admission_log_level(admission) ==
            BridgeAdmissionLogLevel::debug)
          {
            RCLCPP_DEBUG(
              logger,
              "[unity2foxglove_ros2_bridge] suppressed local ROS subscription "
              "sample topic='%s' reason=%s count=%llu",
              topic.c_str(),
              bridge_admission_name(admission),
              static_cast<unsigned long long>(count));
          } else {
            RCLCPP_WARN(
              logger,
              "[unity2foxglove_ros2_bridge] rejected ROS subscription "
              "sample topic='%s' reason=%s count=%llu",
              topic.c_str(),
              bridge_admission_name(admission),
              static_cast<unsigned long long>(count));
          }
        };
    }

    const auto publisher_node = node_;
    const auto origin_registry = origin_registry_;
    publisher_factory_ =
      [publisher_node,
      origin_registry,
      publisher_lifecycle_observer = std::move(publisher_lifecycle_observer)](
      const std::string & topic,
      const std::string & message_type,
      const rclcpp::QoS & qos)
      {
        // create_generic_publisher performs the rosidl_typesupport_cpp lookup
        // for the exact canonical message type before returning a publisher.
        auto publisher = std::make_shared<BridgeLocalPublisher>(
          publisher_node->create_generic_publisher(topic, message_type, qos),
          origin_registry,
          publisher_lifecycle_observer);
        return [publisher = std::move(publisher)](
          const rclcpp::SerializedMessage & message)
          {
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

#ifdef UNITY2FOXGLOVE_ROS2_BRIDGE_TESTING
  std::weak_ptr<bridge_runtime::BridgeOriginRegistry>
  origin_registry_for_testing() const noexcept
  {
    return origin_registry_;
  }
#endif

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
          "profile=%s reliability=%s durability=%s history=%s depth=%u",
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
    BridgeFrame contract;
    contract.topic = identity.topic;
    contract.schema_name = identity.schema_name;
    contract.encoding = "cdr";
    contract.profile = identity.qos.profile;
    contract.reliability = identity.qos.reliability;
    contract.durability = identity.qos.durability;
    contract.history = identity.qos.history;
    contract.depth = identity.qos.depth;
    auto qos = make_qos(contract);
    const auto logger = node_->get_logger();
    const auto origin_registry = origin_registry_;
    auto admission_diagnostics =
      std::make_shared<BridgeAdmissionDiagnostics>(
      identity.topic,
      admission_diagnostic_sink_);
    auto receive =
      [callback = std::move(callback),
      logger,
      origin_registry,
      admission_diagnostics = std::move(admission_diagnostics)](
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
          const auto admission = callback(
            serialized.buffer,
            serialized.buffer_length,
            now < 0 ? 0U : static_cast<uint64_t>(now),
            origin_registry->classify(
              info.get_rmw_message_info().publisher_gid));
          admission_diagnostics->observe(admission);
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
  BridgeAdmissionDiagnosticSink admission_diagnostic_sink_;
  GenericPublisherFactory publisher_factory_;
  uint64_t maximum_publishers_;
  PublisherContractRegistry publisher_contracts_;
  std::unordered_map<std::string, SerializedPublishCallback> publishers_;
  std::unordered_map<std::string, size_t> counts_;
  std::vector<uint8_t> payload_scratch_;
};
