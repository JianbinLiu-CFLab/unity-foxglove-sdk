nlohmann::json LoadV1AuthorityFixture()
{
  std::ifstream input(U2R2_PROTOCOL_FIXTURE_PATH, std::ios::binary);
  if (!input) {
    throw std::runtime_error(
            std::string("unable to open shared U2R2 v1 authority fixture: ") +
            U2R2_PROTOCOL_FIXTURE_PATH);
  }
  nlohmann::json fixture;
  input >> fixture;
  return fixture;
}

std::vector<uint8_t> HexToBytes(const std::string & hex)
{
  if (hex.size() % 2 != 0) {
    throw std::runtime_error("fixture hex contains an incomplete byte");
  }
  std::vector<uint8_t> bytes;
  bytes.reserve(hex.size() / 2);
  for (size_t offset = 0; offset < hex.size(); offset += 2) {
    bytes.push_back(static_cast<uint8_t>(
      std::stoul(hex.substr(offset, 2), nullptr, 16)));
  }
  return bytes;
}

RawFrame ReadFixtureFrame(const nlohmann::json & vector)
{
  const auto bytes = HexToBytes(vector.at("frameHex").get<std::string>());
  if (bytes.size() < 16 ||
    bytes[0] != 'U' || bytes[1] != '2' || bytes[2] != 'R' || bytes[3] != '2')
  {
    throw std::runtime_error("fixture frame has an invalid fixed header");
  }
  const auto header_length = read_u32_le(&bytes[8]);
  const auto payload_length = read_u32_le(&bytes[12]);
  if (bytes.size() != 16U + header_length + payload_length) {
    throw std::runtime_error("fixture frame length does not match its fixed header");
  }
  const std::string header_json(
    bytes.begin() + 16,
    bytes.begin() + 16 + header_length);
  if (header_json != vector.at("headerJson").get<std::string>()) {
    throw std::runtime_error("fixture frame JSON does not match headerJson");
  }

  RawFrame raw;
  raw.header = nlohmann::json::parse(header_json);
  raw.payload.assign(bytes.begin() + 16 + header_length, bytes.end());
  if (raw.header != vector.at("header")) {
    throw std::runtime_error("fixture frame JSON does not match structured header");
  }
  if (raw.payload.size() != vector.at("payloadLength").get<size_t>()) {
    throw std::runtime_error("fixture payload length does not match structured metadata");
  }
  return raw;
}

std::vector<uint8_t> ReadSocketBytes(SocketHandle socket, size_t count)
{
  std::vector<uint8_t> bytes(count, 0);
  size_t offset = 0;
  while (offset < count) {
    const auto received = receive_socket(socket, bytes.data() + offset, count - offset);
    if (received <= 0) {
      throw std::runtime_error("fixture response socket closed before the frame completed");
    }
    offset += static_cast<size_t>(received);
  }
  return bytes;
}

std::vector<uint8_t> ReadSocketWireFrame(SocketHandle socket)
{
  auto bytes = ReadSocketBytes(socket, 16);
  const auto header_length = read_u32_le(&bytes[8]);
  const auto payload_length = read_u32_le(&bytes[12]);
  const auto remainder =
    ReadSocketBytes(socket, header_length + payload_length);
  bytes.insert(bytes.end(), remainder.begin(), remainder.end());
  return bytes;
}

struct WireQosContract
{
  std::string profile = "default";
  std::string reliability = "reliable";
  std::string durability = "volatile";
  std::string history = "keep_last";
  uint32_t depth = 10U;
};

RawFrame MakePublishRawFrame(
  const std::string & topic = "/unity/tf",
  const std::string & schema_name = "foxglove_msgs/msg/FrameTransform",
  const WireQosContract & qos = WireQosContract{})
{
  RawFrame raw;
  raw.header = {
    {"op", "publish"},
    {"topic", topic},
    {"schemaName", schema_name},
    {"encoding", "cdr"},
    {"logTimeNs", 1234},
    {"sequence", 7},
    {"qos", {
      {"profile", qos.profile},
      {"reliability", qos.reliability},
      {"durability", qos.durability},
      {"history", qos.history},
      {"depth", qos.depth}
    }}
  };
  raw.payload = {0x00, 0x01, 0x00, 0x00, 0x10, 0x20};
  return raw;
}

RawFrame MakePreparePublisherRawFrame(
  const std::string & request_id = "phase184-prepare-1",
  const std::string & topic = "/unity/tf",
  const std::string & schema_name = "foxglove_msgs/msg/FrameTransform",
  const WireQosContract & qos = WireQosContract{})
{
  auto raw = MakePublishRawFrame(topic, schema_name, qos);
  raw.header["op"] = "prepare_publisher";
  raw.header["requestId"] = request_id;
  raw.header["protocolVersion"] = 1;
  raw.header.erase("logTimeNs");
  raw.header.erase("sequence");
  raw.payload.clear();
  return raw;
}

rmw_qos_profile_t MakeRmwQosProfile(const WireQosContract & contract)
{
  const auto frame = parse_publish_frame(
    MakePublishRawFrame("/unity/qos", "foxglove_msgs/msg/FrameTransform", contract));
  return make_qos(frame).get_rmw_qos_profile();
}

void ExpectPublisherContractConflictRejectedWithoutMutation(
  const BridgeFrame & registered,
  const BridgeFrame & conflicting)
{
  PublisherContractRegistry registry;

  EXPECT_EQ(
    PublisherContractDisposition::CreatePublisher,
    registry.register_or_validate(registered));
  EXPECT_THROW(registry.register_or_validate(conflicting), std::runtime_error);
  EXPECT_THROW(registry.register_or_validate(conflicting), std::runtime_error);
  EXPECT_EQ(
    PublisherContractDisposition::ReusePublisher,
    registry.register_or_validate(registered));
}

std::array<SocketHandle, 2> MakeConnectedSocketPair()
{
  std::array<SocketHandle, 2> sockets = {kInvalidSocket, kInvalidSocket};
#ifdef _WIN32
  static WinsockRuntime winsock;
  ScopedFd listener(::socket(AF_INET, SOCK_STREAM, IPPROTO_TCP));
  if (!listener.valid()) {
    return sockets;
  }
  sockaddr_in address {};
  address.sin_family = AF_INET;
  address.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
  address.sin_port = 0;
  if (::bind(
      listener.get(),
      reinterpret_cast<sockaddr *>(&address),
      static_cast<SocketLength>(sizeof(address))) != 0 ||
    ::listen(listener.get(), 1) != 0)
  {
    return sockets;
  }
  SocketLength address_length = static_cast<SocketLength>(sizeof(address));
  if (::getsockname(
      listener.get(),
      reinterpret_cast<sockaddr *>(&address),
      &address_length) != 0)
  {
    return sockets;
  }
  ScopedFd client(::socket(AF_INET, SOCK_STREAM, IPPROTO_TCP));
  if (!client.valid() ||
    ::connect(
      client.get(),
      reinterpret_cast<sockaddr *>(&address),
      static_cast<SocketLength>(sizeof(address))) != 0)
  {
    return sockets;
  }
  ScopedFd server(::accept(listener.get(), nullptr, nullptr));
  if (!server.valid()) {
    return sockets;
  }
  sockets[0] = client.release();
  sockets[1] = server.release();
#else
  int raw[2] = {-1, -1};
  if (::socketpair(AF_UNIX, SOCK_STREAM, 0, raw) == 0) {
    sockets[0] = raw[0];
    sockets[1] = raw[1];
  }
#endif
  return sockets;
}

int ShutdownSocketWrite(SocketHandle socket)
{
#ifdef _WIN32
  return ::shutdown(socket, SD_SEND);
#else
  return ::shutdown(socket, SHUT_WR);
#endif
}

class SocketThreadJoiner final
{
public:
  SocketThreadJoiner(SocketHandle socket, std::thread & thread)
  : socket_(socket), thread_(thread)
  {
  }

  ~SocketThreadJoiner()
  {
    if (!thread_.joinable()) {
      return;
    }
    shutdown_socket_both(socket_);
    try {
      thread_.join();
    } catch (...) {
    }
  }

  SocketThreadJoiner(const SocketThreadJoiner &) = delete;
  SocketThreadJoiner & operator=(const SocketThreadJoiner &) = delete;

private:
  SocketHandle socket_;
  std::thread & thread_;
};

const nlohmann::json & ExecutableV1BaseVector(
  const nlohmann::json & fixture,
  const std::string & base_frame)
{
  if (base_frame == "preparePublisher.request") {
    return fixture.at("preparePublisher").at("request");
  }
  if (base_frame == "preparePublisher.response") {
    return fixture.at("preparePublisher").at("response");
  }
  if (base_frame == "publish.frame") {
    return fixture.at("publish").at("frame");
  }
  throw std::runtime_error(
          "unknown executable v1 base frame: " + base_frame);
}

std::vector<uint8_t> ExecutableV1BaseFrame(
  const nlohmann::json & fixture,
  const nlohmann::json & vector)
{
  return HexToBytes(
    ExecutableV1BaseVector(
      fixture,
      vector.at("baseFrame").get<std::string>())
    .at("frameHex").get<std::string>());
}

void SetU32At(std::vector<uint8_t> & bytes, size_t offset, uint32_t value)
{
  if (offset + 4U > bytes.size()) {
    throw std::runtime_error("executable v1 u32 patch is outside the frame");
  }
  bytes[offset] = static_cast<uint8_t>(value & 0xffU);
  bytes[offset + 1U] = static_cast<uint8_t>((value >> 8U) & 0xffU);
  bytes[offset + 2U] = static_cast<uint8_t>((value >> 16U) & 0xffU);
  bytes[offset + 3U] = static_cast<uint8_t>((value >> 24U) & 0xffU);
}

void ReadExecutableV1Body(
  const std::vector<uint8_t> & frame,
  std::vector<uint8_t> & header,
  std::vector<uint8_t> & payload)
{
  if (frame.size() < 16U) {
    throw std::runtime_error("executable v1 base frame is too short");
  }
  const auto header_length = read_u32_le(&frame[8]);
  const auto payload_length = read_u32_le(&frame[12]);
  if (
    frame.size() !=
    16U + static_cast<size_t>(header_length) +
    static_cast<size_t>(payload_length))
  {
    throw std::runtime_error(
            "executable v1 base frame has inconsistent lengths");
  }
  header.assign(
    frame.begin() + 16,
    frame.begin() + 16 + header_length);
  payload.assign(
    frame.begin() + 16 + header_length,
    frame.end());
}

std::vector<uint8_t> RebuildExecutableV1Frame(
  const std::vector<uint8_t> & base,
  const std::vector<uint8_t> & header,
  const std::vector<uint8_t> & payload)
{
  if (base.size() < 16U ||
    header.size() > std::numeric_limits<uint32_t>::max() ||
    payload.size() > std::numeric_limits<uint32_t>::max())
  {
    throw std::runtime_error("executable v1 rebuild input is invalid");
  }
  std::vector<uint8_t> frame(
    16U + header.size() + payload.size(),
    0U);
  std::copy_n(base.begin(), 16, frame.begin());
  SetU32At(frame, 8, static_cast<uint32_t>(header.size()));
  SetU32At(frame, 12, static_cast<uint32_t>(payload.size()));
  std::copy(header.begin(), header.end(), frame.begin() + 16);
  std::copy(
    payload.begin(),
    payload.end(),
    frame.begin() + 16 + header.size());
  return frame;
}

std::vector<uint8_t> BuildExecutableV1Wire(
  const nlohmann::json & fixture,
  const nlohmann::json & vector)
{
  auto frame = ExecutableV1BaseFrame(fixture, vector);
  const auto action = vector.at("action").get<std::string>();
  if (action == "patch_frame") {
    const auto replacement =
      HexToBytes(vector.at("replacementHex").get<std::string>());
    const auto offset = vector.at("offset").get<size_t>();
    if (offset + replacement.size() > frame.size()) {
      throw std::runtime_error("executable v1 patch is outside the frame");
    }
    std::copy(
      replacement.begin(),
      replacement.end(),
      frame.begin() + offset);
    return frame;
  }
  if (action == "truncate_frame" || action == "stream_frame") {
    size_t length;
    if (vector.contains("length")) {
      length = vector.at("length").get<size_t>();
    } else {
      const auto trim = vector.at("trimBytes").get<size_t>();
      if (trim >= frame.size()) {
        throw std::runtime_error("executable v1 trim removes the whole frame");
      }
      length = frame.size() - trim;
    }
    if (length >= frame.size()) {
      throw std::runtime_error("executable v1 truncation is not shorter");
    }
    frame.resize(length);
    return frame;
  }
  if (action == "patch_json") {
    auto decoded = u2r2::decode_frame(frame);
    const auto path = vector.at("path").get<std::string>();
    if (path == "qos.history") {
      decoded.header.at("qos")["history"] = vector.at("value");
    } else if (path.find('.') == std::string::npos) {
      decoded.header[path] = vector.at("value");
    } else {
      throw std::runtime_error(
              "unsupported executable v1 JSON path: " + path);
    }
    return u2r2::encode_frame(decoded.header, decoded.payload);
  }

  std::vector<uint8_t> header;
  std::vector<uint8_t> payload;
  ReadExecutableV1Body(frame, header, payload);
  if (action == "duplicate_json_property") {
    if (header.empty() || header.front() != '{') {
      throw std::runtime_error(
              "executable v1 duplicate base header is not an object");
    }
    const auto prefix =
      nlohmann::json(vector.at("property").get<std::string>()).dump() +
      ":" + vector.at("value").dump() + ",";
    header.insert(
      header.begin() + 1,
      prefix.begin(),
      prefix.end());
    return RebuildExecutableV1Frame(frame, header, payload);
  }
  if (action == "replace_header_utf8") {
    const auto needle_text = vector.at("needle").get<std::string>();
    const std::vector<uint8_t> needle(
      needle_text.begin(),
      needle_text.end());
    const auto replacement =
      HexToBytes(vector.at("replacementHex").get<std::string>());
    const auto found = std::search(
      header.begin(),
      header.end(),
      needle.begin(),
      needle.end());
    if (found == header.end() || replacement.size() > needle.size()) {
      throw std::runtime_error(
              "executable v1 UTF-8 replacement target is invalid");
    }
    std::copy(replacement.begin(), replacement.end(), found);
    return RebuildExecutableV1Frame(frame, header, payload);
  }
  if (action == "append_json_root") {
    const auto suffix = vector.at("suffix").get<std::string>();
    header.insert(header.end(), suffix.begin(), suffix.end());
    return RebuildExecutableV1Frame(frame, header, payload);
  }
  throw std::runtime_error("unknown executable v1 action: " + action);
}

std::string ExpectedCppV1FailureFragment(const std::string & failure)
{
  if (failure == "magic") {
    return "magic";
  }
  if (failure == "version" || failure == "flags") {
    return "envelope version or reserved flags";
  }
  if (failure == "header_length") {
    return "JSON header length is out of range";
  }
  if (failure == "payload_length") {
    return "payload length is out of range";
  }
  if (failure == "fixed_header") {
    return "shorter than its fixed header";
  }
  if (failure == "header_completion" || failure == "payload_completion") {
    return "truncated or trailing bytes";
  }
  if (failure == "duplicate_property") {
    return "duplicate property";
  }
  if (failure == "operation") {
    return "first legacy U2R2 frame";
  }
  if (failure == "utf8" || failure == "json_root") {
    return "JSON header is invalid";
  }
  if (failure == "topic") {
    return "topic";
  }
  if (failure == "schema_name") {
    return "schemaName";
  }
  if (failure == "qos") {
    return "qos.depth";
  }
  throw std::runtime_error(
          "unknown executable C++ v1 failure classification: " + failure);
}

void ExpectExecutableV1NegativeRejected(
  const nlohmann::json & fixture,
  const nlohmann::json & vector)
{
  const auto action = vector.at("action").get<std::string>();
  const auto expected_failure =
    vector.at("expectedFailure").get<std::string>();
  if (action == "stream_frame") {
    const auto wire = BuildExecutableV1Wire(fixture, vector);
    const auto sockets = MakeConnectedSocketPair();
    ASSERT_NE(kInvalidSocket, sockets[0]);
    ASSERT_NE(kInvalidSocket, sockets[1]);
    ScopedFd writer(sockets[0]);
    ScopedFd reader(sockets[1]);
    configure_client_timeouts(reader.get());
    write_all(writer.get(), wire);
    const auto termination = vector.at("termination").get<std::string>();
    const auto limits = termination == "timeout"
      ? u2r2::ProtocolLimits::defaults().with({
          {"partialFrameTimeoutMs", vector.at("timeoutMs").get<uint64_t>()},
        })
      : u2r2::ProtocolLimits::defaults();
    if (termination == "eof") {
      ASSERT_EQ(0, ShutdownSocketWrite(writer.get()));
    } else if (termination != "timeout") {
      FAIL() << "unknown executable v1 stream termination: " << termination;
    }
    bridge_runtime::BridgeSessionProtocol protocol(limits);
    if (termination == "timeout") {
      try {
        (void)read_accounted_wire_frame(
          reader.get(),
          protocol,
          []() {return true;});
        FAIL() << "partial executable v1 frame did not time out";
      } catch (const u2r2::ProtocolError & error) {
        EXPECT_EQ("timeout", error.code());
        EXPECT_TRUE(error.terminal());
      }
      EXPECT_EQ("partial_payload_timeout", expected_failure);
    } else {
      EXPECT_THROW(
        (void)read_accounted_wire_frame(
          reader.get(),
          protocol,
          []() {return true;}),
        ClientClosedException);
      EXPECT_EQ("peer_close", expected_failure);
    }
    return;
  }

  const auto wire = BuildExecutableV1Wire(fixture, vector);
  std::string rejection;
  try {
    if (
      expected_failure == "topic" ||
      expected_failure == "schema_name" ||
      expected_failure == "qos")
    {
      (void)parse_prepare_publisher_frame(
        raw_frame_from_wire(wire, u2r2::ProtocolLimits::defaults()));
    } else {
      (void)u2r2::parse_legacy_v1_first_frame(wire);
    }
  } catch (const std::exception & error) {
    rejection = error.what();
  }
  EXPECT_FALSE(rejection.empty());
  EXPECT_NE(
    std::string::npos,
    rejection.find(ExpectedCppV1FailureFragment(expected_failure)))
    << rejection;
}
}  // namespace
