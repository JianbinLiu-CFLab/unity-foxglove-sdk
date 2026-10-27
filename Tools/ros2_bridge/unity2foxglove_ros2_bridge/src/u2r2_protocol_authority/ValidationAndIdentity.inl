namespace
{
constexpr std::array<const char *, 27> kLimitNames{{
  "maxConnections",
  "maxDataSessions",
  "maxProbes",
  "maxContracts",
  "maxOutstandingRequests",
  "maxReplayEntries",
  "maxReplayBytes",
  "maxTombstones",
  "fixedFrameBytes",
  "maxHeaderBytes",
  "maxPayloadBytes",
  "maxTransientBytes",
  "maxInFlightBytes",
  "maxQueuedBytes",
  "maxTotalQueueDepth",
  "maxPerContractQueueDepth",
  "maxPerContractQueueBytes",
  "reservedControlQueueDepth",
  "reservedControlQueueBytes",
  "controlBurstLimit",
  "handshakeTimeoutMs",
  "partialFrameTimeoutMs",
  "readTimeoutMs",
  "writeTimeoutMs",
  "joinTimeoutMs",
  "shutdownTimeoutMs",
  "maxJsonDepth",
}};

[[noreturn]] void InvalidConfiguration(const std::string & message)
{
  throw ProtocolError("invalid_configuration", message, true);
}

[[noreturn]] void CapacityExceeded(const std::string & message)
{
  throw ProtocolError("capacity_exceeded", message, false);
}

uint64_t ConfigurationAdd(uint64_t left, uint64_t right)
{
  if (right > std::numeric_limits<uint64_t>::max() - left) {
    InvalidConfiguration("U2R2 limit arithmetic overflowed");
  }
  return left + right;
}

void ValidateLimits(const std::map<std::string, uint64_t> & values)
{
  if (values.size() != kLimitNames.size()) {
    InvalidConfiguration(
      "the U2R2 limit snapshot must contain exactly the named limits");
  }
  for (const auto * name : kLimitNames) {
    const auto found = values.find(name);
    if (found == values.end()) {
      InvalidConfiguration(
        "the U2R2 limit snapshot must contain exactly the named limits");
    }
    if (found->second == 0) {
      InvalidConfiguration("every U2R2 limit must be nonzero");
    }
  }
  for (const auto & [name, unused] : values) {
    (void)unused;
    if (std::find(kLimitNames.begin(), kLimitNames.end(), name) ==
      kLimitNames.end())
    {
      InvalidConfiguration(
        "the U2R2 limit snapshot must contain exactly the named limits");
    }
  }
  if (
    values.at("maxHeaderBytes") > std::numeric_limits<uint32_t>::max() ||
    values.at("maxPayloadBytes") > std::numeric_limits<uint32_t>::max())
  {
    InvalidConfiguration(
      "U2R2 wire header and payload limits must fit unsigned 32-bit lengths");
  }
  if (values.at("maxJsonDepth") > 64) {
    InvalidConfiguration(
      "U2R2 JSON depth cannot exceed the protocol maximum of 64");
  }
  if (values.at("maxDataSessions") != 1) {
    InvalidConfiguration("maxDataSessions must be exactly one");
  }

  (void)ConfigurationAdd(
    values.at("maxContracts"), values.at("maxTombstones"));
  const auto role_total = ConfigurationAdd(
    values.at("maxDataSessions"), values.at("maxProbes"));
  if (values.at("maxConnections") < role_total) {
    InvalidConfiguration(
      "maxConnections must contain all data-session and probe leases");
  }

  const auto maximum_frame = ConfigurationAdd(
    values.at("fixedFrameBytes"),
    ConfigurationAdd(
      values.at("maxHeaderBytes"), values.at("maxPayloadBytes")));
  if (
    values.at("maxPerContractQueueBytes") < maximum_frame ||
    values.at("maxQueuedBytes") < maximum_frame ||
    values.at("maxTransientBytes") < maximum_frame ||
    values.at("maxInFlightBytes") < maximum_frame)
  {
    InvalidConfiguration(
      "every frame-holding byte budget must contain one maximum frame");
  }
  if (
    values.at("reservedControlQueueDepth") >=
    values.at("maxTotalQueueDepth"))
  {
    InvalidConfiguration(
      "reserved control depth must leave at least one data slot");
  }
  if (
    values.at("maxTotalQueueDepth") <
    ConfigurationAdd(
      values.at("reservedControlQueueDepth"),
      values.at("maxPerContractQueueDepth")))
  {
    InvalidConfiguration(
      "the total queue depth must contain the control reserve and one contract");
  }
  if (
    values.at("maxQueuedBytes") <
    ConfigurationAdd(
      values.at("reservedControlQueueBytes"),
      values.at("maxPerContractQueueBytes")))
  {
    InvalidConfiguration(
      "the queued-byte budget must contain the control reserve and one contract");
  }
  if (
    values.at("controlBurstLimit") >
    values.at("reservedControlQueueDepth"))
  {
    InvalidConfiguration(
      "the control burst limit cannot exceed reserved control depth");
  }
  if (
    values.at("maxReplayEntries") <
    values.at("maxOutstandingRequests"))
  {
    InvalidConfiguration(
      "replay entry capacity must contain every outstanding request");
  }
}

struct LeaseSettlement final
{
  explicit LeaseSettlement(std::function<void()> callback)
  : callback_(std::move(callback))
  {
  }

  bool settle()
  {
    bool expected = false;
    if (!settled_.compare_exchange_strong(expected, true)) {
      return false;
    }
    callback_();
    return true;
  }

private:
  std::atomic<bool> settled_{false};
  std::function<void()> callback_;
};

struct RollbackSettlement final
{
  explicit RollbackSettlement(std::function<void()> callback)
  : callback_(std::move(callback))
  {
  }

  ~RollbackSettlement()
  {
    abandon();
  }

  void disarm() noexcept
  {
    settled_.store(true);
  }

  void abandon() noexcept
  {
    bool expected = false;
    if (!settled_.compare_exchange_strong(expected, true)) {
      return;
    }
    try {
      callback_();
    } catch (...) {
      // Admission destruction is a last-resort rollback and cannot throw.
    }
  }

private:
  std::atomic<bool> settled_{false};
  std::function<void()> callback_;
};

struct ControlSettlement final
{
  ControlSettlement(
    uint64_t reserved_bytes,
    std::function<void(
      OutboundFrame,
      std::optional<ContractKey>)> commit,
    std::function<void()> cancel)
  : reserved_bytes_(reserved_bytes),
    commit_(std::move(commit)),
    cancel_(std::move(cancel))
  {
  }

  bool try_commit(
    OutboundFrame frame,
    std::optional<ContractKey> fence_contract)
  {
    std::lock_guard<std::mutex> lock(mutex_);
    if (settled_) {
      return false;
    }
    if (!frame.is_control()) {
      throw std::invalid_argument(
              "a control reservation requires a control frame");
    }
    if (frame.byte_count() > reserved_bytes_) {
      CapacityExceeded(
        "the U2R2 control response exceeds its reservation");
    }
    commit_(std::move(frame), fence_contract);
    settled_ = true;
    return true;
  }

  bool try_cancel()
  {
    std::lock_guard<std::mutex> lock(mutex_);
    if (settled_) {
      return false;
    }
    cancel_();
    settled_ = true;
    return true;
  }

private:
  std::mutex mutex_;
  uint64_t reserved_bytes_;
  bool settled_{false};
  std::function<void(
    OutboundFrame,
    std::optional<ContractKey>)> commit_;
  std::function<void()> cancel_;
};

struct DataSettlement final
{
  DataSettlement(
    ContractKey key,
    uint64_t reserved_bytes,
    std::function<bool(OutboundFrame)> commit,
    std::function<void()> cancel)
  : key_(key),
    reserved_bytes_(reserved_bytes),
    commit_(std::move(commit)),
    cancel_(std::move(cancel))
  {
  }

  bool try_commit(OutboundFrame frame)
  {
    std::lock_guard<std::mutex> lock(mutex_);
    if (settled_) {
      return false;
    }
    if (frame.is_control()) {
      throw std::invalid_argument(
              "a data reservation requires a data frame");
    }
    if (frame.contract() != key_ || frame.byte_count() != reserved_bytes_) {
      throw std::invalid_argument(
              "a data frame must exactly match its reservation");
    }
    const auto committed = commit_(std::move(frame));
    settled_ = true;
    return committed;
  }

  bool try_cancel()
  {
    std::lock_guard<std::mutex> lock(mutex_);
    if (settled_) {
      return false;
    }
    cancel_();
    settled_ = true;
    return true;
  }

private:
  std::mutex mutex_;
  ContractKey key_;
  uint64_t reserved_bytes_;
  bool settled_{false};
  std::function<bool(OutboundFrame)> commit_;
  std::function<void()> cancel_;
};

template<typename T>
std::shared_ptr<T> SettlementAs(const std::shared_ptr<void> & value)
{
  return std::static_pointer_cast<T>(value);
}

void DisarmRollback(std::shared_ptr<void> & value) noexcept
{
  if (!value) {
    return;
  }
  SettlementAs<RollbackSettlement>(value)->disarm();
  value.reset();
}

struct ContractKeyHash final
{
  size_t operator()(const ContractKey & key) const noexcept
  {
    const auto left = std::hash<uint64_t>{}(key.contract_id);
    const auto right = std::hash<uint64_t>{}(key.generation);
    return left ^ (right + 0x9e3779b9U + (left << 6U) + (left >> 2U));
  }
};

bool IsAsciiAlpha(char value)
{
  return
    (value >= 'a' && value <= 'z') ||
    (value >= 'A' && value <= 'Z');
}

bool IsAsciiAlnumOrUnderscore(char value)
{
  return IsAsciiAlpha(value) ||
         (value >= '0' && value <= '9') ||
         value == '_';
}

[[noreturn]] void InvalidContract(const std::string & message)
{
  throw ProtocolError("invalid_contract", message, false);
}

void ValidateTopic(const std::string & topic)
{
  if (topic.size() > 255) {
    InvalidContract("a U2R2 topic cannot exceed 255 ASCII characters");
  }
  if (topic.size() < 2 || topic.front() != '/' || topic.back() == '/') {
    InvalidContract("a U2R2 topic must be a canonical absolute name");
  }
  size_t segment_start = 1;
  for (size_t index = 1; index <= topic.size(); ++index) {
    if (index != topic.size() && topic[index] != '/') {
      if (!IsAsciiAlnumOrUnderscore(topic[index])) {
        InvalidContract("a U2R2 topic contains a non-canonical character");
      }
      continue;
    }
    if (
      index == segment_start ||
      (!IsAsciiAlpha(topic[segment_start]) &&
      topic[segment_start] != '_'))
    {
      InvalidContract(
        "every U2R2 topic segment must start with an ASCII letter");
    }
    segment_start = index + 1;
  }
}

bool IsCanonicalPackage(const std::string & package)
{
  if (
    package.size() < 2 || package.size() > 255 ||
    package.front() < 'a' || package.front() > 'z' ||
    package.back() == '_' ||
    package.find("__") != std::string::npos)
  {
    return false;
  }
  return std::all_of(
    package.begin(),
    package.end(),
    [](char value) {
      return
        (value >= 'a' && value <= 'z') ||
        (value >= '0' && value <= '9') ||
        value == '_';
    });
}

bool IsCanonicalType(const std::string & type)
{
  if (
    type.empty() || type.size() > 255 ||
    type.front() < 'A' || type.front() > 'Z' ||
    type.front() == '_' || type.back() == '_' ||
    type.find("__") != std::string::npos)
  {
    return false;
  }
  return std::all_of(type.begin(), type.end(), IsAsciiAlpha) ||
         std::all_of(
    type.begin(),
    type.end(),
    [](char value) {
      return IsAsciiAlpha(value) || (value >= '0' && value <= '9');
    });
}

void ValidateSchemaName(const std::string & schema_name)
{
  const auto first = schema_name.find('/');
  const auto second =
    first == std::string::npos
    ? std::string::npos
    : schema_name.find('/', first + 1);
  if (
    first == std::string::npos ||
    second == std::string::npos ||
    schema_name.find('/', second + 1) != std::string::npos ||
    schema_name.substr(first + 1, second - first - 1) != "msg" ||
    !IsCanonicalPackage(schema_name.substr(0, first)) ||
    !IsCanonicalType(schema_name.substr(second + 1)))
  {
    InvalidContract(
      "a U2R2 schemaName must be canonical package/msg/Type");
  }
}

std::string RequiredContractString(
  const nlohmann::json & header,
  const std::string & name)
{
  const auto found = header.find(name);
  if (
    found == header.end() ||
    !found->is_string() ||
    found->get<std::string>().empty())
  {
    InvalidContract(
      "U2R2 contract field " + name + " must be a nonempty string");
  }
  return found->get<std::string>();
}

std::string RequiredQosString(
  const nlohmann::json & qos,
  const std::string & name,
  std::initializer_list<const char *> allowed)
{
  const auto found = qos.find(name);
  if (found == qos.end() || !found->is_string()) {
    InvalidContract("U2R2 qos field " + name + " must be a string");
  }
  const auto value = found->get<std::string>();
  if (std::find(allowed.begin(), allowed.end(), value) == allowed.end()) {
    InvalidContract("U2R2 qos field " + name + " is invalid");
  }
  return value;
}
}  // namespace
