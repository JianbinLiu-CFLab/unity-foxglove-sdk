
struct BoundedOutboundScheduler::Impl final
{
  struct ControlEntry
  {
    OutboundFrame frame;
    std::optional<ContractKey> fence_contract;
  };

  explicit Impl(const ProtocolLimits & value)
  : limits(value)
  {
  }

  mutable std::mutex mutex;
  ProtocolLimits limits;
  std::deque<ControlEntry> control;
  std::unordered_map<
    ContractKey,
    std::deque<OutboundFrame>,
    ContractKeyHash> data;
  std::deque<ContractKey> round_robin;
  std::unordered_set<ContractKey, ContractKeyHash> active;
  std::unordered_set<ContractKey, ContractKeyHash> revoked;
  std::unordered_set<ContractKey, ContractKeyHash> retire_when_drained;
  std::unordered_map<ContractKey, uint64_t, ContractKeyHash>
  data_reserved_depth_by_contract;
  std::unordered_map<ContractKey, uint64_t, ContractKeyHash>
  data_reserved_bytes_by_contract;
  uint64_t control_depth_used{0};
  uint64_t control_bytes_used{0};
  uint64_t data_queued_depth{0};
  uint64_t data_queued_bytes{0};
  uint64_t data_reserved_depth{0};
  uint64_t data_reserved_bytes{0};
  uint64_t transient_bytes{0};
  uint64_t in_flight_bytes{0};
  uint64_t control_burst{0};
  bool reader_active{false};
  bool writer_active{false};
  std::optional<ContractKey> writer_contract;
  bool closed{false};

  void activate(const ContractKey & key)
  {
    if (active.insert(key).second) {
      round_robin.push_back(key);
    }
  }

  void remove_from_round_robin(const ContractKey & key)
  {
    active.erase(key);
    round_robin.erase(
      std::remove(round_robin.begin(), round_robin.end(), key),
      round_robin.end());
  }

  bool is_drained(const ContractKey & key) const
  {
    return
      data.find(key) == data.end() &&
      data_reserved_depth_by_contract.find(key) ==
      data_reserved_depth_by_contract.end() &&
      (!writer_active ||
      !writer_contract.has_value() ||
      *writer_contract != key);
  }

  void ensure_revoked_admission(const ContractKey & key) const
  {
    if (revoked.find(key) != revoked.end()) {
      return;
    }
    const auto bound =
      limits.max_contracts() + limits.max_tombstones();
    if (static_cast<uint64_t>(revoked.size()) >= bound) {
      throw std::logic_error(
              "the U2R2 revoked-contract lifecycle exceeded its bound");
    }
  }

  void revoke(const ContractKey & key)
  {
    ensure_revoked_admission(key);
    revoked.insert(key);
    const auto found = data.find(key);
    if (found != data.end()) {
      for (const auto & frame : found->second) {
        if (
          data_queued_depth == 0 ||
          data_queued_bytes < frame.byte_count())
        {
          throw std::logic_error("data queue accounting underflow");
        }
        --data_queued_depth;
        data_queued_bytes -= frame.byte_count();
      }
      data.erase(found);
    }
    remove_from_round_robin(key);
  }

  void try_forget_retired(const ContractKey & key)
  {
    if (
      retire_when_drained.find(key) == retire_when_drained.end() ||
      !is_drained(key))
    {
      return;
    }
    retire_when_drained.erase(key);
    revoked.erase(key);
  }

  uint64_t contract_bytes(
    const std::deque<OutboundFrame> & queue) const
  {
    uint64_t result = 0;
    for (const auto & frame : queue) {
      result = checked_add(
        result,
        frame.byte_count(),
        limits.max_per_contract_queue_bytes(),
        "per-contract queued bytes");
    }
    return result;
  }

  uint64_t reserved_depth(const ContractKey & key) const
  {
    const auto found = data_reserved_depth_by_contract.find(key);
    return
      found == data_reserved_depth_by_contract.end()
      ? 0
      : found->second;
  }

  uint64_t reserved_bytes(const ContractKey & key) const
  {
    const auto found = data_reserved_bytes_by_contract.find(key);
    return
      found == data_reserved_bytes_by_contract.end()
      ? 0
      : found->second;
  }

  void release_data_reservation(
    const ContractKey & key,
    uint64_t bytes)
  {
    const auto depth = data_reserved_depth_by_contract.find(key);
    const auto byte_count = data_reserved_bytes_by_contract.find(key);
    if (
      depth == data_reserved_depth_by_contract.end() ||
      byte_count == data_reserved_bytes_by_contract.end() ||
      depth->second == 0 ||
      byte_count->second < bytes ||
      data_reserved_depth == 0 ||
      data_reserved_bytes < bytes)
    {
      throw std::logic_error("data reservation accounting underflow");
    }
    --depth->second;
    byte_count->second -= bytes;
    --data_reserved_depth;
    data_reserved_bytes -= bytes;
    if (depth->second == 0) {
      data_reserved_depth_by_contract.erase(depth);
    }
    if (byte_count->second == 0) {
      data_reserved_bytes_by_contract.erase(byte_count);
    }
  }

  bool can_fit_data(
    const ContractKey & key,
    const std::deque<OutboundFrame> & queue,
    uint64_t incoming_bytes,
    uint64_t removing_depth,
    uint64_t removing_bytes) const
  {
    const auto queue_depth = static_cast<uint64_t>(queue.size());
    if (removing_depth > queue_depth ||
      removing_bytes > data_queued_bytes)
    {
      throw std::logic_error("U2R2 queue accounting underflow");
    }
    const auto queued_contract_depth = queue_depth - removing_depth;
    const auto reserved_contract_depth = reserved_depth(key);
    const auto current_contract_bytes = contract_bytes(queue);
    if (removing_bytes > current_contract_bytes) {
      throw std::logic_error("U2R2 contract queue accounting underflow");
    }
    const auto queued_contract_bytes =
      current_contract_bytes - removing_bytes;
    const auto reserved_contract_bytes = reserved_bytes(key);
    const auto data_depth_limit =
      limits.max_total_queue_depth() -
      limits.reserved_control_queue_depth();
    const auto data_byte_limit =
      limits.max_queued_bytes() -
      limits.reserved_control_queue_bytes();
    const auto queued_data_depth = data_queued_depth - removing_depth;
    const auto queued_data_bytes = data_queued_bytes - removing_bytes;
    if (
      queued_contract_depth > limits.max_per_contract_queue_depth() ||
      reserved_contract_depth >
      limits.max_per_contract_queue_depth() - queued_contract_depth ||
      queued_contract_bytes > limits.max_per_contract_queue_bytes() ||
      reserved_contract_bytes >
      limits.max_per_contract_queue_bytes() - queued_contract_bytes ||
      queued_data_depth > data_depth_limit ||
      data_reserved_depth > data_depth_limit - queued_data_depth ||
      queued_data_bytes > data_byte_limit ||
      data_reserved_bytes > data_byte_limit - queued_data_bytes)
    {
      return false;
    }
    const auto contract_depth =
      queued_contract_depth + reserved_contract_depth;
    const auto retained_contract_bytes =
      queued_contract_bytes + reserved_contract_bytes;
    const auto retained_data_depth =
      queued_data_depth + data_reserved_depth;
    const auto retained_data_bytes =
      queued_data_bytes + data_reserved_bytes;
    return
      contract_depth < limits.max_per_contract_queue_depth() &&
      retained_contract_bytes <= limits.max_per_contract_queue_bytes() &&
      incoming_bytes <=
      limits.max_per_contract_queue_bytes() - retained_contract_bytes &&
      retained_data_depth < data_depth_limit &&
      retained_data_bytes <= data_byte_limit &&
      incoming_bytes <= data_byte_limit - retained_data_bytes;
  }
};

BoundedOutboundScheduler::BoundedOutboundScheduler(
  const ProtocolLimits & limits)
: impl_(std::make_shared<Impl>(limits))
{
}

BoundedOutboundScheduler::~BoundedOutboundScheduler() = default;

std::optional<ControlReservation>
BoundedOutboundScheduler::try_reserve_control(uint64_t bytes)
{
  auto state = impl_;
  {
    std::lock_guard<std::mutex> lock(state->mutex);
    if (
      state->closed ||
      state->control_depth_used >=
      state->limits.reserved_control_queue_depth() ||
      state->control_bytes_used >
      state->limits.reserved_control_queue_bytes() ||
      bytes >
      state->limits.reserved_control_queue_bytes() -
      state->control_bytes_used)
    {
      return std::nullopt;
    }
    ++state->control_depth_used;
    state->control_bytes_used += bytes;
  }

  auto settlement = std::make_shared<ControlSettlement>(
    bytes,
    [state, bytes](
      OutboundFrame frame,
      std::optional<ContractKey> fence_contract) {
      std::lock_guard<std::mutex> lock(state->mutex);
      if (state->closed) {
        throw std::logic_error(
                "the U2R2 outbound scheduler is closed");
      }
      if (
        state->control_depth_used == 0 ||
        state->control_bytes_used < bytes)
      {
        throw std::logic_error(
                "the U2R2 control reservation is not active");
      }
      state->control_bytes_used -= bytes - frame.byte_count();
      Impl::ControlEntry entry{
        std::move(frame),
        std::move(fence_contract)};
      if (entry.fence_contract.has_value()) {
        const auto insertion = std::find_if(
          state->control.begin(),
          state->control.end(),
          [](const Impl::ControlEntry & queued) {
            return !queued.fence_contract.has_value();
          });
        state->control.insert(insertion, std::move(entry));
      } else {
        state->control.push_back(std::move(entry));
      }
    },
    [state, bytes]() {
      std::lock_guard<std::mutex> lock(state->mutex);
      if (state->closed) {
        return;
      }
      if (
        state->control_depth_used == 0 ||
        state->control_bytes_used < bytes)
      {
        throw std::logic_error(
                "the U2R2 control reservation is not active");
      }
      --state->control_depth_used;
      state->control_bytes_used -= bytes;
    });
  return ControlReservation(settlement);
}

std::optional<DataReservation>
BoundedOutboundScheduler::try_reserve_data(
  const ContractKey & key,
  uint64_t bytes)
{
  if (bytes == 0) {
    throw std::invalid_argument(
            "a data reservation must contain a non-empty frame");
  }
  auto state = impl_;
  {
    std::lock_guard<std::mutex> lock(state->mutex);
    if (
      state->closed ||
      state->revoked.find(key) != state->revoked.end())
    {
      return std::nullopt;
    }
    static const std::deque<OutboundFrame> empty;
    const auto found = state->data.find(key);
    const auto & queue = found == state->data.end() ? empty : found->second;
    if (!state->can_fit_data(key, queue, bytes, 0, 0)) {
      return std::nullopt;
    }
    ++state->data_reserved_depth;
    state->data_reserved_bytes += bytes;
    ++state->data_reserved_depth_by_contract[key];
    state->data_reserved_bytes_by_contract[key] += bytes;
  }

  auto settlement = std::make_shared<DataSettlement>(
    key,
    bytes,
    [state, key, bytes](OutboundFrame frame) {
      std::lock_guard<std::mutex> lock(state->mutex);
      if (state->closed) {
        return false;
      }
      state->release_data_reservation(key, bytes);
      if (state->revoked.find(key) != state->revoked.end()) {
        state->try_forget_retired(key);
        return false;
      }
      auto & queue = state->data[key];
      queue.push_back(std::move(frame));
      ++state->data_queued_depth;
      state->data_queued_bytes += bytes;
      state->activate(key);
      return true;
    },
    [state, key, bytes]() {
      std::lock_guard<std::mutex> lock(state->mutex);
      if (state->closed) {
        return;
      }
      state->release_data_reservation(key, bytes);
      state->try_forget_retired(key);
    });
  return DataReservation(settlement);
}

EnqueueDisposition BoundedOutboundScheduler::enqueue_data(
  OutboundFrame frame,
  QueueOverflowPolicy policy)
{
  if (
    policy != QueueOverflowPolicy::reject &&
    policy != QueueOverflowPolicy::drop_oldest &&
    policy != QueueOverflowPolicy::replace_latest)
  {
    InvalidContract("the U2R2 queue overflow policy is invalid");
  }
  if (frame.is_control()) {
    throw std::invalid_argument("a data queue requires a data frame");
  }
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  if (state->closed) {
    return EnqueueDisposition::rejected;
  }
  const auto key = frame.contract();
  if (state->revoked.find(key) != state->revoked.end()) {
    return EnqueueDisposition::rejected;
  }
  auto [found, inserted] = state->data.try_emplace(key);
  auto & queue = found->second;
  if (state->can_fit_data(key, queue, frame.byte_count(), 0, 0)) {
    queue.push_back(std::move(frame));
    ++state->data_queued_depth;
    state->data_queued_bytes += queue.back().byte_count();
    state->activate(key);
    return EnqueueDisposition::accepted;
  }
  if (policy == QueueOverflowPolicy::reject || queue.empty()) {
    if (queue.empty()) {
      state->data.erase(key);
    }
    return EnqueueDisposition::rejected;
  }

  const auto victim_bytes =
    policy == QueueOverflowPolicy::drop_oldest
    ? queue.front().byte_count()
    : queue.back().byte_count();
  if (!state->can_fit_data(
      key,
      queue,
      frame.byte_count(),
      1,
      victim_bytes))
  {
    return EnqueueDisposition::rejected;
  }
  if (policy == QueueOverflowPolicy::drop_oldest) {
    queue.pop_front();
  } else {
    queue.pop_back();
  }
  --state->data_queued_depth;
  state->data_queued_bytes -= victim_bytes;
  queue.push_back(std::move(frame));
  ++state->data_queued_depth;
  state->data_queued_bytes += queue.back().byte_count();
  state->activate(key);
  return
    policy == QueueOverflowPolicy::drop_oldest
    ? EnqueueDisposition::dropped_oldest
    : EnqueueDisposition::replaced_latest;
}

std::optional<ByteLease>
BoundedOutboundScheduler::try_reserve_transient(uint64_t bytes)
{
  auto state = impl_;
  {
    std::lock_guard<std::mutex> lock(state->mutex);
    if (
      state->closed ||
      state->transient_bytes > state->limits.max_transient_bytes() ||
      bytes >
      state->limits.max_transient_bytes() - state->transient_bytes)
    {
      return std::nullopt;
    }
    state->transient_bytes += bytes;
  }
  auto settlement = std::make_shared<LeaseSettlement>(
    [state, bytes]() {
      std::lock_guard<std::mutex> lock(state->mutex);
      if (state->transient_bytes < bytes) {
        throw std::logic_error("transient byte accounting underflow");
      }
      state->transient_bytes -= bytes;
    });
  return ByteLease(settlement);
}

std::optional<ByteLease>
BoundedOutboundScheduler::try_begin_read(uint64_t bytes)
{
  auto state = impl_;
  {
    std::lock_guard<std::mutex> lock(state->mutex);
    if (
      state->closed ||
      state->reader_active ||
      state->in_flight_bytes > state->limits.max_in_flight_bytes() ||
      bytes >
      state->limits.max_in_flight_bytes() - state->in_flight_bytes)
    {
      return std::nullopt;
    }
    state->reader_active = true;
    state->in_flight_bytes += bytes;
  }
  auto settlement = std::make_shared<LeaseSettlement>(
    [state, bytes]() {
      std::lock_guard<std::mutex> lock(state->mutex);
      if (!state->reader_active || state->in_flight_bytes < bytes) {
        throw std::logic_error("reader byte accounting underflow");
      }
      state->reader_active = false;
      state->in_flight_bytes -= bytes;
    });
  return ByteLease(settlement);
}

std::optional<WriteLease> BoundedOutboundScheduler::try_begin_write()
{
  auto state = impl_;
  std::optional<OutboundFrame> selected;
  {
    std::lock_guard<std::mutex> lock(state->mutex);
    if (state->closed || state->writer_active) {
      return std::nullopt;
    }
    bool choose_control = false;
    std::optional<ContractKey> selected_contract;
    if (state->control.empty()) {
      if (state->round_robin.empty()) {
        return std::nullopt;
      }
      selected_contract = state->round_robin.front();
    } else if (
      state->round_robin.empty() ||
      state->control_burst < state->limits.control_burst_limit())
    {
      choose_control = true;
    } else {
      const auto eligible = std::find_if(
        state->round_robin.begin(),
        state->round_robin.end(),
        [&](const ContractKey & candidate) {
          return std::none_of(
            state->control.begin(),
            state->control.end(),
            [&](const Impl::ControlEntry & control) {
              return
                control.fence_contract.has_value() &&
                *control.fence_contract == candidate;
            });
        });
      if (eligible == state->round_robin.end()) {
        choose_control = true;
      } else {
        selected_contract = *eligible;
      }
    }

    const auto candidate_bytes =
      choose_control
      ? state->control.front().frame.byte_count()
      : state->data.at(*selected_contract).front().byte_count();
    if (
      state->in_flight_bytes > state->limits.max_in_flight_bytes() ||
      candidate_bytes >
      state->limits.max_in_flight_bytes() - state->in_flight_bytes)
    {
      return std::nullopt;
    }

    if (choose_control) {
      selected.emplace(std::move(state->control.front().frame));
      state->control.pop_front();
      --state->control_depth_used;
      state->control_bytes_used -= candidate_bytes;
      ++state->control_burst;
      state->writer_contract.reset();
    } else {
      const auto key = *selected_contract;
      const auto scheduled = std::find(
        state->round_robin.begin(),
        state->round_robin.end(),
        key);
      if (
        scheduled == state->round_robin.end() ||
        state->active.erase(key) != 1)
      {
        throw std::logic_error(
                "the selected U2R2 contract is not scheduled");
      }
      state->round_robin.erase(scheduled);
      auto & queue = state->data.at(key);
      selected.emplace(std::move(queue.front()));
      queue.pop_front();
      --state->data_queued_depth;
      state->data_queued_bytes -= candidate_bytes;
      if (queue.empty()) {
        state->data.erase(key);
      } else {
        state->activate(key);
      }
      state->control_burst = 0;
      state->writer_contract = key;
    }
    state->writer_active = true;
    state->in_flight_bytes += candidate_bytes;
  }

  const auto bytes = selected->byte_count();
  auto settlement = std::make_shared<LeaseSettlement>(
    [state, bytes]() {
      std::lock_guard<std::mutex> lock(state->mutex);
      if (!state->writer_active || state->in_flight_bytes < bytes) {
        throw std::logic_error("writer byte accounting underflow");
      }
      const auto completed_contract = state->writer_contract;
      state->writer_active = false;
      state->writer_contract.reset();
      state->in_flight_bytes -= bytes;
      if (completed_contract.has_value()) {
        state->try_forget_retired(*completed_contract);
      }
    });
  return WriteLease(std::move(*selected), settlement);
}

void BoundedOutboundScheduler::revoke_contract(const ContractKey & key)
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  if (state->closed) {
    return;
  }
  state->revoke(key);
}

bool BoundedOutboundScheduler::is_contract_revoked_and_drained(
  const ContractKey & key) const
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  return
    state->revoked.find(key) != state->revoked.end() &&
    state->is_drained(key);
}

void BoundedOutboundScheduler::activate_contract(const ContractKey & key)
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  if (state->closed) {
    return;
  }
  state->revoked.erase(key);
  state->retire_when_drained.erase(key);
}

void BoundedOutboundScheduler::retire_contract(const ContractKey & key)
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  if (state->closed) {
    return;
  }
  state->revoke(key);
  state->retire_when_drained.insert(key);
  state->try_forget_retired(key);
}

void BoundedOutboundScheduler::forget_contract(const ContractKey & key)
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  if (state->closed) {
    return;
  }
  if (!state->is_drained(key)) {
    state->retire_when_drained.insert(key);
    return;
  }
  state->retire_when_drained.erase(key);
  state->revoked.erase(key);
}

void BoundedOutboundScheduler::close()
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  if (state->closed) {
    return;
  }
  state->closed = true;
  state->control.clear();
  state->data.clear();
  state->data_reserved_depth_by_contract.clear();
  state->data_reserved_bytes_by_contract.clear();
  state->round_robin.clear();
  state->active.clear();
  state->revoked.clear();
  state->retire_when_drained.clear();
  state->control_depth_used = 0;
  state->control_bytes_used = 0;
  state->data_queued_depth = 0;
  state->data_queued_bytes = 0;
  state->data_reserved_depth = 0;
  state->data_reserved_bytes = 0;
  state->control_burst = 0;
}

uint64_t BoundedOutboundScheduler::queued_bytes() const
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  return impl_->control_bytes_used + impl_->data_queued_bytes;
}

uint64_t BoundedOutboundScheduler::total_queued_depth() const
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  return impl_->control_depth_used + impl_->data_queued_depth;
}

uint64_t BoundedOutboundScheduler::data_queued_depth() const
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  return impl_->data_queued_depth;
}

uint64_t BoundedOutboundScheduler::transient_bytes() const
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  return impl_->transient_bytes;
}

uint64_t BoundedOutboundScheduler::in_flight_bytes() const
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  return impl_->in_flight_bytes;
}

uint64_t BoundedOutboundScheduler::revoked_contract_count() const
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  return static_cast<uint64_t>(impl_->revoked.size());
}

bool BoundedOutboundScheduler::is_closed() const
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  return impl_->closed;
}

ReplayAdmission::ReplayAdmission(
  std::shared_ptr<void> owner,
  uint64_t request_id,
  ReplayDecision decision,
  std::vector<uint8_t> cached_response,
  std::shared_ptr<void> rollback,
  std::shared_ptr<void> scheduler_identity,
  std::optional<ContractIdentity> bound_identity,
  std::optional<Operation> bound_operation)
: owner_(std::move(owner)),
  request_id_(request_id),
  decision_(decision),
  cached_response_(std::move(cached_response)),
  rollback_(std::move(rollback)),
  scheduler_identity_(std::move(scheduler_identity)),
  bound_identity_(std::move(bound_identity)),
  bound_operation_(std::move(bound_operation))
{
}

ReplayAdmission::~ReplayAdmission() = default;

ReplayDecision ReplayAdmission::decision() const noexcept
{
  return decision_;
}

uint64_t ReplayAdmission::request_id() const noexcept
{
  return request_id_;
}

const std::vector<uint8_t> & ReplayAdmission::cached_response() const noexcept
{
  return cached_response_;
}
