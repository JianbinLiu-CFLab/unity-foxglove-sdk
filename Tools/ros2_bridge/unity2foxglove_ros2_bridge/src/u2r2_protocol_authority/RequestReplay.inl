
struct RequestReplayAuthority::Impl final
{
  struct Entry
  {
    std::vector<uint8_t> request;
    uint64_t reserved_response_bytes;
    std::vector<uint8_t> response;
    bool completed{false};
    bool claimed{false};
    std::shared_ptr<void> scheduler_identity;
    std::shared_ptr<ControlReservation> reservation;
    std::optional<ContractIdentity> bound_identity;
    std::optional<Operation> bound_operation;
  };

  explicit Impl(const ProtocolLimits & value)
  : limits(value)
  {
  }

  mutable std::mutex mutex;
  ProtocolLimits limits;
  std::unordered_map<uint64_t, Entry> entries;
  std::list<uint64_t> completed_order;
  uint64_t high_water_mark{0};
  uint64_t outstanding_requests{0};
  uint64_t replay_bytes{0};
  bool closed{false};

  void evict(uint64_t request_id)
  {
    const auto found = entries.find(request_id);
    if (found == entries.end() || !found->second.completed) {
      throw std::logic_error("the U2R2 replay eviction target is invalid");
    }
    const auto bytes =
      static_cast<uint64_t>(found->second.request.size()) +
      static_cast<uint64_t>(found->second.response.size());
    if (replay_bytes < bytes) {
      throw std::logic_error("replay byte accounting underflow");
    }
    replay_bytes -= bytes;
    entries.erase(found);
    completed_order.remove(request_id);
  }
};

RequestReplayAuthority::RequestReplayAuthority(
  const ProtocolLimits & limits)
: impl_(std::make_shared<Impl>(limits))
{
}

RequestReplayAuthority::~RequestReplayAuthority() = default;

ReplayAdmission RequestReplayAuthority::admit(
  uint64_t request_id,
  const std::vector<uint8_t> & canonical_request,
  uint64_t maximum_response_bytes,
  BoundedOutboundScheduler & scheduler)
{
  return admit_impl(
    request_id, canonical_request, maximum_response_bytes, scheduler, nullptr, std::nullopt);
}

ReplayAdmission RequestReplayAuthority::admit_contract(
  uint64_t request_id,
  const std::vector<uint8_t> & canonical_request,
  uint64_t maximum_response_bytes,
  BoundedOutboundScheduler & scheduler,
  const ContractIdentity & identity,
  Operation operation)
{
  return admit_impl(
    request_id, canonical_request, maximum_response_bytes, scheduler, &identity, operation);
}

ReplayAdmission RequestReplayAuthority::admit_impl(
  uint64_t request_id,
  const std::vector<uint8_t> & canonical_request,
  uint64_t maximum_response_bytes,
  BoundedOutboundScheduler & scheduler,
  const ContractIdentity * bound_identity,
  std::optional<Operation> bound_operation)
{
  if (request_id == 0) {
    throw ProtocolError(
            "invalid_request_id",
            "a U2R2 request ID must be nonzero",
            true);
  }
  if (request_id == std::numeric_limits<uint64_t>::max()) {
    throw ProtocolError(
            "request_id_exhausted",
            "the U2R2 request ID space exhausted before the session high-water mark could wrap",
            true);
  }
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  if (state->closed) {
    throw std::logic_error("the U2R2 replay authority is closed");
  }

  const auto retained = state->entries.find(request_id);
  if (retained != state->entries.end()) {
    if (canonical_request != retained->second.request) {
      throw ProtocolError(
              "request_id_conflict",
              "a retained U2R2 request ID has different canonical bytes",
              true);
    }
    if (!retained->second.completed) {
      throw ProtocolError(
              "request_in_flight",
              "the identical U2R2 request is still in flight",
              false);
    }
    if (retained->second.scheduler_identity.get() != scheduler.impl_.get()) {
      throw std::logic_error(
              "the retained U2R2 response belongs to another scheduler");
    }
    auto replay_reservation = scheduler.try_reserve_control(
      static_cast<uint64_t>(retained->second.response.size()));
    if (!replay_reservation) {
      CapacityExceeded("no control capacity remains for exact replay");
    }
    replay_reservation->commit(
      OutboundFrame::control(
        "replay:" + std::to_string(request_id),
        retained->second.response));
    ReplayAdmission result(
      state,
      request_id,
      ReplayDecision::replay_cached,
      retained->second.response,
      {},
      retained->second.scheduler_identity,
      retained->second.bound_identity,
      retained->second.bound_operation);
    result.settled_ = true;
    return result;
  }

  if (request_id <= state->high_water_mark) {
    throw ProtocolError(
            "stale_request",
            "the U2R2 request ID is below the retained session high-water mark",
            false);
  }
  if (
    state->outstanding_requests ==
    state->limits.max_outstanding_requests())
  {
    CapacityExceeded("the outstanding U2R2 request limit is exhausted");
  }

  const auto request_bytes = static_cast<uint64_t>(canonical_request.size());
  if (
    request_bytes > state->limits.max_replay_bytes() ||
    maximum_response_bytes >
    state->limits.max_replay_bytes() - request_bytes)
  {
    CapacityExceeded(
      "the request and response reservation exceed replay bytes");
  }
  const auto requested_replay_bytes =
    request_bytes + maximum_response_bytes;

  auto response_reservation =
    scheduler.try_reserve_control(maximum_response_bytes);
  if (!response_reservation) {
    CapacityExceeded(
      "no control capacity remains for the required response");
  }

  std::vector<uint64_t> evictions;
  auto projected_count = static_cast<uint64_t>(state->entries.size());
  auto projected_bytes = state->replay_bytes;
  auto cursor = state->completed_order.begin();
  while (
    projected_count >= state->limits.max_replay_entries() ||
    projected_bytes > state->limits.max_replay_bytes() ||
    requested_replay_bytes >
    state->limits.max_replay_bytes() - projected_bytes)
  {
    if (cursor == state->completed_order.end()) {
      response_reservation->try_cancel();
      CapacityExceeded(
        "the bounded replay cache cannot admit this request");
    }
    const auto entry = state->entries.find(*cursor);
    if (entry == state->entries.end() || !entry->second.completed) {
      throw std::logic_error("the U2R2 replay order is inconsistent");
    }
    const auto entry_bytes =
      static_cast<uint64_t>(entry->second.request.size()) +
      static_cast<uint64_t>(entry->second.response.size());
    evictions.push_back(*cursor);
    --projected_count;
    projected_bytes -= entry_bytes;
    ++cursor;
  }
  for (const auto evicted : evictions) {
    state->evict(evicted);
  }

  auto reservation = std::make_shared<ControlReservation>(
    std::move(*response_reservation));
  typename Impl::Entry entry{
    canonical_request,
    maximum_response_bytes,
    {},
    false,
    false,
    scheduler.impl_,
    std::move(reservation),
    bound_identity ? std::optional<ContractIdentity>(*bound_identity) : std::nullopt,
    bound_operation};
  state->entries.emplace(request_id, std::move(entry));
  state->replay_bytes += requested_replay_bytes;
  ++state->outstanding_requests;
  state->high_water_mark = request_id;
  auto rollback = std::make_shared<RollbackSettlement>(
    [state, request_id]() {
      (void)RequestReplayAuthority::try_abandon(
        state, request_id, false);
    });
  return ReplayAdmission(
    state,
    request_id,
    ReplayDecision::begin_mutation,
    {},
    std::move(rollback));
}

void RequestReplayAuthority::complete(
  ReplayAdmission & admission,
  const std::vector<uint8_t> & exact_response)
{
  finish(
    admission,
    OutboundFrame::control(
      "response:" + std::to_string(admission.request_id()),
      exact_response),
    false,
    false);
}

void RequestReplayAuthority::abort(
  ReplayAdmission & admission,
  const std::vector<uint8_t> & exact_error_response)
{
  finish(
    admission,
    OutboundFrame::control(
      "error:" + std::to_string(admission.request_id()),
      exact_error_response),
    false,
    false);
}

void RequestReplayAuthority::finish(
  ReplayAdmission & admission,
  OutboundFrame exact_response,
  bool priority_fence,
  bool require_claimed,
  std::optional<ContractKey> fence_contract)
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  if (
    admission.owner_.get() != state.get() ||
    admission.decision_ != ReplayDecision::begin_mutation ||
    admission.settled_)
  {
    throw std::logic_error(
            "the U2R2 replay admission is not pending");
  }
  const auto found = state->entries.find(admission.request_id_);
  if (found == state->entries.end() || found->second.completed) {
    throw std::logic_error("the U2R2 replay entry is not pending");
  }
  auto & entry = found->second;
  if (entry.claimed != require_claimed) {
    throw std::logic_error(
            require_claimed ?
            "the U2R2 replay entry is not claimed by a contract" :
            "the U2R2 replay entry is owned by a contract transaction");
  }
  if (exact_response.byte_count() > entry.reserved_response_bytes) {
    CapacityExceeded(
      "the exact response exceeds its pre-mutation reservation");
  }
  const auto exact_bytes = exact_response.bytes();
  const auto exact_byte_count = exact_response.byte_count();
  if (priority_fence != fence_contract.has_value()) {
    throw std::logic_error(
            "a fenced U2R2 response requires exactly one contract");
  }
  const auto committed = priority_fence
    ? entry.reservation->try_commit_fenced(
      std::move(exact_response),
      *fence_contract)
    : entry.reservation->try_commit(std::move(exact_response));
  if (!committed) {
    throw std::logic_error("the U2R2 response reservation is settled");
  }
  entry.response = exact_bytes;
  entry.completed = true;
  if (entry.reserved_response_bytes < exact_byte_count) {
    throw std::logic_error("replay reservation accounting underflow");
  }
  state->replay_bytes -=
    entry.reserved_response_bytes - exact_byte_count;
  if (state->outstanding_requests == 0) {
    throw std::logic_error("outstanding request accounting underflow");
  }
  --state->outstanding_requests;
  state->completed_order.push_back(admission.request_id_);
  admission.settled_ = true;
  DisarmRollback(admission.rollback_);
}

void RequestReplayAuthority::cancel_pending(ReplayAdmission & admission)
{
  cancel(admission, false);
}

void RequestReplayAuthority::cancel(
  ReplayAdmission & admission,
  bool require_claimed)
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  if (admission.owner_.get() != state.get()) {
    throw std::logic_error(
            "the U2R2 replay admission belongs to another authority");
  }
  if (admission.settled_) {
    return;
  }
  if (admission.decision_ != ReplayDecision::begin_mutation) {
    throw std::logic_error("the U2R2 replay admission is not pending");
  }
  const auto found = state->entries.find(admission.request_id_);
  if (
    found == state->entries.end() ||
    found->second.completed ||
    found->second.claimed != require_claimed)
  {
    throw std::logic_error("the U2R2 replay admission is not pending");
  }
  auto & entry = found->second;
  entry.reservation->try_cancel();
  const auto reserved =
    static_cast<uint64_t>(entry.request.size()) +
    entry.reserved_response_bytes;
  if (
    state->replay_bytes < reserved ||
    state->outstanding_requests == 0)
  {
    throw std::logic_error("replay cancellation accounting underflow");
  }
  state->replay_bytes -= reserved;
  --state->outstanding_requests;
  state->entries.erase(found);
  admission.settled_ = true;
  DisarmRollback(admission.rollback_);
}

bool RequestReplayAuthority::try_abandon(
  ReplayAdmission & admission,
  bool require_claimed) noexcept
{
  if (
    admission.owner_.get() != impl_.get() ||
    admission.decision_ != ReplayDecision::begin_mutation ||
    admission.settled_)
  {
    return false;
  }
  if (!try_abandon(impl_, admission.request_id_, require_claimed)) {
    return false;
  }
  admission.settled_ = true;
  DisarmRollback(admission.rollback_);
  return true;
}

bool RequestReplayAuthority::try_abandon(
  const std::shared_ptr<Impl> & state,
  uint64_t request_id,
  bool require_claimed) noexcept
{
  try {
    std::lock_guard<std::mutex> lock(state->mutex);
    const auto found = state->entries.find(request_id);
    if (
      found == state->entries.end() ||
      found->second.completed ||
      found->second.claimed != require_claimed)
    {
      return false;
    }
    auto & entry = found->second;
    entry.reservation->try_cancel();
    const auto reserved =
      static_cast<uint64_t>(entry.request.size()) +
      entry.reserved_response_bytes;
    if (
      state->replay_bytes < reserved ||
      state->outstanding_requests == 0)
    {
      return false;
    }
    state->replay_bytes -= reserved;
    --state->outstanding_requests;
    state->entries.erase(found);
    return true;
  } catch (...) {
    return false;
  }
}

bool RequestReplayAuthority::try_claim_for_contract(
  ReplayAdmission & admission,
  const BoundedOutboundScheduler & scheduler)
{
  return try_claim_for_contract(admission, scheduler, nullptr, std::nullopt);
}

bool RequestReplayAuthority::try_claim_for_contract(
  ReplayAdmission & admission,
  const BoundedOutboundScheduler & scheduler,
  const ContractIdentity * bound_identity,
  std::optional<Operation> bound_operation)
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  if (
    admission.owner_.get() != state.get() ||
    admission.decision_ != ReplayDecision::begin_mutation ||
    admission.settled_)
  {
    return false;
  }
  const auto found = state->entries.find(admission.request_id_);
  if (
    found == state->entries.end() ||
    found->second.completed ||
    found->second.claimed ||
    found->second.scheduler_identity.get() != scheduler.impl_.get() ||
    (found->second.bound_identity.has_value() &&
     (!bound_identity || !bound_operation.has_value() ||
      *found->second.bound_identity != *bound_identity ||
      found->second.bound_operation != bound_operation)))
  {
    return false;
  }
  found->second.claimed = true;
  return true;
}

void RequestReplayAuthority::release_contract_claim(
  ReplayAdmission & admission,
  const BoundedOutboundScheduler & scheduler)
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  if (
    admission.owner_.get() != state.get() ||
    admission.decision_ != ReplayDecision::begin_mutation ||
    admission.settled_)
  {
    throw std::logic_error(
            "the U2R2 replay admission has no matching contract claim");
  }
  const auto found = state->entries.find(admission.request_id_);
  if (
    found == state->entries.end() ||
    found->second.completed ||
    !found->second.claimed ||
    found->second.scheduler_identity.get() != scheduler.impl_.get())
  {
    throw std::logic_error(
            "the U2R2 replay admission has no matching contract claim");
  }
  found->second.claimed = false;
}

bool RequestReplayAuthority::is_cached_for(
  const ReplayAdmission & admission,
  const BoundedOutboundScheduler & scheduler) const
{
  return is_cached_for(admission, scheduler, nullptr, std::nullopt);
}

bool RequestReplayAuthority::is_cached_for(
  const ReplayAdmission & admission,
  const BoundedOutboundScheduler & scheduler,
  const ContractIdentity * bound_identity,
  std::optional<Operation> bound_operation) const
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  if (
    admission.owner_.get() != state.get() ||
    admission.decision_ != ReplayDecision::replay_cached ||
    !admission.settled_)
  {
    return false;
  }
  if (admission.bound_identity_.has_value()) {
    return
      admission.scheduler_identity_.get() == scheduler.impl_.get() &&
      bound_identity && bound_operation.has_value() &&
      admission.bound_identity_ == *bound_identity &&
      admission.bound_operation_ == bound_operation;
  }
  const auto found = state->entries.find(admission.request_id_);
  return
    found != state->entries.end() &&
    found->second.completed &&
    found->second.scheduler_identity.get() == scheduler.impl_.get() &&
    (!found->second.bound_identity.has_value() ||
     (bound_identity && bound_operation.has_value() &&
      *found->second.bound_identity == *bound_identity &&
      found->second.bound_operation == bound_operation));
}

uint64_t RequestReplayAuthority::high_water_mark() const
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  return impl_->high_water_mark;
}

uint64_t RequestReplayAuthority::outstanding_requests() const
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  return impl_->outstanding_requests;
}

uint64_t RequestReplayAuthority::retained_entries() const
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  return static_cast<uint64_t>(impl_->entries.size());
}

uint64_t RequestReplayAuthority::replay_bytes() const
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  return impl_->replay_bytes;
}

bool RequestReplayAuthority::is_closed() const
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  return impl_->closed;
}

void RequestReplayAuthority::close()
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  if (state->closed) {
    return;
  }
  for (auto & [request_id, entry] : state->entries) {
    (void)request_id;
    if (!entry.completed) {
      entry.reservation->try_cancel();
    }
  }
  state->entries.clear();
  state->completed_order.clear();
  state->outstanding_requests = 0;
  state->replay_bytes = 0;
  state->closed = true;
}
