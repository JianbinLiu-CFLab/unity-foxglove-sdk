
ProtocolLimits::ProtocolLimits(std::map<std::string, uint64_t> values)
: values_(std::move(values))
{
  ValidateLimits(values_);
}

ProtocolLimits ProtocolLimits::defaults()
{
  constexpr uint64_t fixed = 16;
  constexpr uint64_t header = 64U * 1024U;
  constexpr uint64_t payload = 64U * 1024U * 1024U;
  constexpr uint64_t maximum_frame = fixed + header + payload;
  return ProtocolLimits({{
    {"maxConnections", 9},
    {"maxDataSessions", 1},
    {"maxProbes", 8},
    {"maxContracts", 64},
    {"maxOutstandingRequests", 8},
    {"maxReplayEntries", 16},
    {"maxReplayBytes", 4U * 1024U * 1024U},
    {"maxTombstones", 32},
    {"fixedFrameBytes", fixed},
    {"maxHeaderBytes", header},
    {"maxPayloadBytes", payload},
    {"maxTransientBytes", maximum_frame * 2U},
    {"maxInFlightBytes", maximum_frame * 2U},
    {"maxQueuedBytes", maximum_frame * 4U},
    {"maxTotalQueueDepth", 128},
    {"maxPerContractQueueDepth", 8},
    {"maxPerContractQueueBytes", maximum_frame * 2U},
    {"reservedControlQueueDepth", 8},
    {"reservedControlQueueBytes", 1024U * 1024U},
    {"controlBurstLimit", 2},
    {"handshakeTimeoutMs", 5000},
    {"partialFrameTimeoutMs", 2000},
    {"readTimeoutMs", 5000},
    {"writeTimeoutMs", 5000},
    {"joinTimeoutMs", 5000},
    {"shutdownTimeoutMs", 10000},
    {"maxJsonDepth", 64},
  }});
}

ProtocolLimits ProtocolLimits::from_diagnostic_snapshot(
  const std::map<std::string, uint64_t> & values)
{
  return ProtocolLimits(values);
}

ProtocolLimits ProtocolLimits::with(
  std::initializer_list<std::pair<const std::string, uint64_t>> overrides) const
{
  auto values = values_;
  for (const auto & [name, value] : overrides) {
    if (values.find(name) == values.end()) {
      InvalidConfiguration("unknown U2R2 limit: " + name);
    }
    values[name] = value;
  }
  return ProtocolLimits(std::move(values));
}

std::map<std::string, uint64_t> ProtocolLimits::to_diagnostic_snapshot() const
{
  return values_;
}

uint64_t ProtocolLimits::value(const std::string & name) const
{
  return values_.at(name);
}

#define U2R2_LIMIT_ACCESSOR(method, name) \
  uint64_t ProtocolLimits::method() const {return value(name);}
U2R2_LIMIT_ACCESSOR(max_connections, "maxConnections")
U2R2_LIMIT_ACCESSOR(max_data_sessions, "maxDataSessions")
U2R2_LIMIT_ACCESSOR(max_probes, "maxProbes")
U2R2_LIMIT_ACCESSOR(max_contracts, "maxContracts")
U2R2_LIMIT_ACCESSOR(max_outstanding_requests, "maxOutstandingRequests")
U2R2_LIMIT_ACCESSOR(max_replay_entries, "maxReplayEntries")
U2R2_LIMIT_ACCESSOR(max_replay_bytes, "maxReplayBytes")
U2R2_LIMIT_ACCESSOR(max_tombstones, "maxTombstones")
U2R2_LIMIT_ACCESSOR(fixed_frame_bytes, "fixedFrameBytes")
U2R2_LIMIT_ACCESSOR(max_header_bytes, "maxHeaderBytes")
U2R2_LIMIT_ACCESSOR(max_payload_bytes, "maxPayloadBytes")
U2R2_LIMIT_ACCESSOR(max_transient_bytes, "maxTransientBytes")
U2R2_LIMIT_ACCESSOR(max_in_flight_bytes, "maxInFlightBytes")
U2R2_LIMIT_ACCESSOR(max_queued_bytes, "maxQueuedBytes")
U2R2_LIMIT_ACCESSOR(max_total_queue_depth, "maxTotalQueueDepth")
U2R2_LIMIT_ACCESSOR(max_per_contract_queue_depth, "maxPerContractQueueDepth")
U2R2_LIMIT_ACCESSOR(max_per_contract_queue_bytes, "maxPerContractQueueBytes")
U2R2_LIMIT_ACCESSOR(reserved_control_queue_depth, "reservedControlQueueDepth")
U2R2_LIMIT_ACCESSOR(reserved_control_queue_bytes, "reservedControlQueueBytes")
U2R2_LIMIT_ACCESSOR(control_burst_limit, "controlBurstLimit")
U2R2_LIMIT_ACCESSOR(handshake_timeout_ms, "handshakeTimeoutMs")
U2R2_LIMIT_ACCESSOR(partial_frame_timeout_ms, "partialFrameTimeoutMs")
U2R2_LIMIT_ACCESSOR(read_timeout_ms, "readTimeoutMs")
U2R2_LIMIT_ACCESSOR(write_timeout_ms, "writeTimeoutMs")
U2R2_LIMIT_ACCESSOR(join_timeout_ms, "joinTimeoutMs")
U2R2_LIMIT_ACCESSOR(shutdown_timeout_ms, "shutdownTimeoutMs")
U2R2_LIMIT_ACCESSOR(max_json_depth, "maxJsonDepth")
#undef U2R2_LIMIT_ACCESSOR

uint64_t checked_add(
  uint64_t current,
  uint64_t increment,
  uint64_t limit,
  const std::string & budget_name)
{
  if (current > limit || increment > limit - current) {
    CapacityExceeded(
      "the U2R2 " +
      (budget_name.empty() ? std::string("budget") : budget_name) +
      " is exhausted");
  }
  return current + increment;
}

FrameSize::FrameSize(
  uint64_t header_bytes,
  uint64_t payload_bytes,
  uint64_t total_bytes) noexcept
: header_bytes_(header_bytes),
  payload_bytes_(payload_bytes),
  total_bytes_(total_bytes)
{
}

FrameSize FrameSize::create(
  const ProtocolLimits & limits,
  uint64_t header_bytes,
  uint64_t payload_bytes)
{
  if (
    header_bytes > limits.max_header_bytes() ||
    payload_bytes > limits.max_payload_bytes())
  {
    CapacityExceeded(
      "the U2R2 frame exceeds its header or payload budget");
  }
  const auto variable = checked_add(
    header_bytes,
    payload_bytes,
    std::numeric_limits<uint64_t>::max(),
    "frame");
  return FrameSize(
    header_bytes,
    payload_bytes,
    checked_add(
      limits.fixed_frame_bytes(),
      variable,
      std::numeric_limits<uint64_t>::max(),
      "frame"));
}

uint64_t FrameSize::header_bytes() const noexcept {return header_bytes_;}
uint64_t FrameSize::payload_bytes() const noexcept {return payload_bytes_;}
uint64_t FrameSize::total_bytes() const noexcept {return total_bytes_;}

struct CapacityCounter::Impl final
{
  explicit Impl(uint64_t capacity_value)
  : capacity(capacity_value)
  {
  }
  mutable std::mutex mutex;
  uint64_t capacity;
  uint64_t count{0};
};

CapacityCounter::CapacityCounter(uint64_t capacity)
: impl_(std::make_unique<Impl>(capacity))
{
  if (capacity == 0) {
    throw std::invalid_argument("capacity must be nonzero");
  }
}

CapacityCounter::~CapacityCounter() = default;

bool CapacityCounter::try_acquire()
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  if (impl_->count == impl_->capacity) {
    return false;
  }
  ++impl_->count;
  return true;
}

void CapacityCounter::release()
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  if (impl_->count == 0) {
    throw std::logic_error("the U2R2 capacity counter is already empty");
  }
  --impl_->count;
}

uint64_t CapacityCounter::count() const
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  return impl_->count;
}

ResourceLease::ResourceLease(std::shared_ptr<void> settlement)
: settlement_(std::move(settlement))
{
}

ResourceLease::~ResourceLease()
{
  release();
}

ResourceLease & ResourceLease::operator=(ResourceLease && other) noexcept
{
  if (this != &other) {
    try {
      release();
    } catch (...) {
      std::terminate();
    }
    settlement_ = std::move(other.settlement_);
  }
  return *this;
}

bool ResourceLease::release()
{
  if (!settlement_) {
    return false;
  }
  return SettlementAs<LeaseSettlement>(settlement_)->settle();
}

struct SessionResourceAuthority::Impl final
{
  explicit Impl(const ProtocolLimits & value)
  : limits(value)
  {
  }
  mutable std::mutex mutex;
  ProtocolLimits limits;
  uint64_t connections{0};
  uint64_t data_sessions{0};
  uint64_t probes{0};
};

SessionResourceAuthority::SessionResourceAuthority(const ProtocolLimits & limits)
: impl_(std::make_shared<Impl>(limits))
{
}

SessionResourceAuthority::~SessionResourceAuthority() = default;

std::optional<ResourceLease> SessionResourceAuthority::try_acquire(
  ConnectionRole role)
{
  auto state = impl_;
  {
    std::lock_guard<std::mutex> lock(state->mutex);
    if (state->connections == state->limits.max_connections()) {
      return std::nullopt;
    }
    if (role == ConnectionRole::data_session) {
      if (state->data_sessions == state->limits.max_data_sessions()) {
        return std::nullopt;
      }
      ++state->data_sessions;
    } else if (role == ConnectionRole::probe) {
      if (state->probes == state->limits.max_probes()) {
        return std::nullopt;
      }
      ++state->probes;
    } else {
      throw std::invalid_argument("unknown U2R2 connection role");
    }
    ++state->connections;
  }
  auto settlement = std::make_shared<LeaseSettlement>(
    [state, role]() {
      std::lock_guard<std::mutex> lock(state->mutex);
      if (state->connections == 0) {
        throw std::logic_error("no U2R2 connection lease is active");
      }
      if (role == ConnectionRole::data_session) {
        if (state->data_sessions == 0) {
          throw std::logic_error("no U2R2 data lease is active");
        }
        --state->data_sessions;
      } else {
        if (state->probes == 0) {
          throw std::logic_error("no U2R2 probe lease is active");
        }
        --state->probes;
      }
      --state->connections;
    });
  return ResourceLease(settlement);
}

uint64_t SessionResourceAuthority::connection_count() const
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  return impl_->connections;
}

RequestIdCounter::RequestIdCounter(uint64_t current) noexcept
: current_(current)
{
}

uint64_t RequestIdCounter::next()
{
  std::lock_guard<std::mutex> lock(mutex_);
  if (
    faulted_ ||
    current_ >= std::numeric_limits<uint64_t>::max() - 1)
  {
    faulted_ = true;
    throw ProtocolError(
            "request_id_exhausted",
            "the U2R2 request ID counter is exhausted",
            true);
  }
  return ++current_;
}

bool RequestIdCounter::is_faulted() const
{
  std::lock_guard<std::mutex> lock(mutex_);
  return faulted_;
}

ContractKey::ContractKey(uint64_t id, uint64_t generation_value)
: contract_id(id),
  generation(generation_value)
{
  if (contract_id == 0 || generation == 0) {
    InvalidContract("a U2R2 contract identity must be nonzero");
  }
}

ContractIdentity::ContractIdentity(
  ContractKey key_value,
  ContractDirection direction_value,
  std::string topic_value,
  std::string schema_name_value,
  Qos qos_value)
: key(key_value),
  direction(direction_value),
  topic(std::move(topic_value)),
  schema_name(std::move(schema_name_value)),
  qos(std::move(qos_value))
{
  if (
    direction != ContractDirection::publish &&
    direction != ContractDirection::subscribe)
  {
    InvalidContract("the U2R2 contract direction is invalid");
  }
  ValidateTopic(topic);
  ValidateSchemaName(schema_name);
  nlohmann::json header{
    {"topic", topic},
    {"schemaName", schema_name},
    {"encoding", "cdr"},
    {"qos", {
      {"profile", qos.profile},
      {"reliability", qos.reliability},
      {"durability", qos.durability},
      {"history", qos.history},
      {"depth", qos.depth},
    }},
  };
  std::string parsed_topic;
  std::string parsed_schema;
  std::optional<Qos> parsed_qos;
  parse_contract_fields(
    header,
    direction == ContractDirection::publish
    ? Operation::PreparePublisher
    : Operation::RegisterSubscription,
    parsed_topic,
    parsed_schema,
    parsed_qos);
}

OutboundFrame::OutboundFrame(
  std::string token,
  bool is_control,
  ContractKey contract,
  uint64_t sequence,
  std::vector<uint8_t> bytes)
: token_(std::move(token)),
  is_control_(is_control),
  contract_(contract),
  sequence_(sequence),
  bytes_(std::move(bytes))
{
}

OutboundFrame OutboundFrame::control(
  std::string token,
  std::vector<uint8_t> bytes)
{
  return OutboundFrame(
    std::move(token), true, ContractKey(1, 1), 0, std::move(bytes));
}

OutboundFrame OutboundFrame::data(
  std::string token,
  ContractKey contract,
  uint64_t sequence,
  std::vector<uint8_t> bytes)
{
  if (sequence == 0) {
    throw ProtocolError(
            "contract_sequence_fault",
            "a U2R2 data sequence must be nonzero",
            false);
  }
  return OutboundFrame(
    std::move(token), false, contract, sequence, std::move(bytes));
}

const std::string & OutboundFrame::token() const noexcept {return token_;}
bool OutboundFrame::is_control() const noexcept {return is_control_;}
const ContractKey & OutboundFrame::contract() const noexcept {return contract_;}
uint64_t OutboundFrame::sequence() const noexcept {return sequence_;}
const std::vector<uint8_t> & OutboundFrame::bytes() const noexcept {return bytes_;}
uint64_t OutboundFrame::byte_count() const noexcept
{
  return static_cast<uint64_t>(bytes_.size());
}

ControlReservation::ControlReservation(std::shared_ptr<void> settlement)
: settlement_(std::move(settlement))
{
}

ControlReservation::~ControlReservation()
{
  try_cancel();
}

ControlReservation & ControlReservation::operator=(
  ControlReservation && other) noexcept
{
  if (this != &other) {
    try {
      try_cancel();
    } catch (...) {
      std::terminate();
    }
    settlement_ = std::move(other.settlement_);
  }
  return *this;
}

void ControlReservation::commit(OutboundFrame frame)
{
  if (!try_commit(std::move(frame))) {
    throw std::logic_error("the control reservation is settled");
  }
}

bool ControlReservation::try_commit(OutboundFrame frame)
{
  if (!settlement_) {
    return false;
  }
  return SettlementAs<ControlSettlement>(settlement_)->try_commit(
    std::move(frame), std::nullopt);
}

bool ControlReservation::try_commit_fenced(
  OutboundFrame frame,
  const ContractKey & fence_contract)
{
  if (!settlement_) {
    return false;
  }
  return SettlementAs<ControlSettlement>(settlement_)->try_commit(
    std::move(frame), fence_contract);
}

bool ControlReservation::try_cancel()
{
  if (!settlement_) {
    return false;
  }
  return SettlementAs<ControlSettlement>(settlement_)->try_cancel();
}

DataReservation::DataReservation(std::shared_ptr<void> settlement)
: settlement_(std::move(settlement))
{
}

DataReservation::~DataReservation()
{
  try_cancel();
}

DataReservation & DataReservation::operator=(DataReservation && other) noexcept
{
  if (this != &other) {
    try {
      try_cancel();
    } catch (...) {
      std::terminate();
    }
    settlement_ = std::move(other.settlement_);
  }
  return *this;
}

bool DataReservation::try_commit(OutboundFrame frame)
{
  if (!settlement_) {
    return false;
  }
  return SettlementAs<DataSettlement>(settlement_)->try_commit(
    std::move(frame));
}

bool DataReservation::try_cancel()
{
  if (!settlement_) {
    return false;
  }
  return SettlementAs<DataSettlement>(settlement_)->try_cancel();
}

ByteLease::ByteLease(std::shared_ptr<void> settlement)
: settlement_(std::move(settlement))
{
}

ByteLease::~ByteLease()
{
  release();
}

ByteLease & ByteLease::operator=(ByteLease && other) noexcept
{
  if (this != &other) {
    try {
      release();
    } catch (...) {
      std::terminate();
    }
    settlement_ = std::move(other.settlement_);
  }
  return *this;
}

bool ByteLease::release()
{
  if (!settlement_) {
    return false;
  }
  return SettlementAs<LeaseSettlement>(settlement_)->settle();
}

WriteLease::WriteLease(
  OutboundFrame frame,
  std::shared_ptr<void> settlement)
: frame_(std::make_shared<OutboundFrame>(std::move(frame))),
  settlement_(std::move(settlement))
{
}

WriteLease::~WriteLease()
{
  release();
}

WriteLease & WriteLease::operator=(WriteLease && other) noexcept
{
  if (this != &other) {
    try {
      release();
    } catch (...) {
      std::terminate();
    }
    frame_ = std::move(other.frame_);
    settlement_ = std::move(other.settlement_);
  }
  return *this;
}

const OutboundFrame & WriteLease::frame() const
{
  if (!frame_) {
    throw std::logic_error("the U2R2 writer lease has no frame");
  }
  return *frame_;
}

bool WriteLease::release()
{
  if (!settlement_) {
    return false;
  }
  return SettlementAs<LeaseSettlement>(settlement_)->settle();
}
