
ContractSequence::ContractSequence(uint64_t starting_sequence) noexcept
: last_accepted_(starting_sequence)
{
}

void ContractSequence::admit(uint64_t sequence)
{
  if (faulted_) {
    throw ProtocolError(
            "contract_sequence_fault",
            "the U2R2 contract sequence is already faulted",
            false);
  }
  if (last_accepted_ == std::numeric_limits<uint64_t>::max()) {
    faulted_ = true;
    throw ProtocolError(
            "contract_sequence_exhausted",
            "the U2R2 contract sequence exhausted before wrap",
            false);
  }
  if (sequence != last_accepted_ + 1) {
    faulted_ = true;
    throw ProtocolError(
            "contract_sequence_fault",
            "the U2R2 contract sequence is not strictly monotonic",
            false);
  }
  last_accepted_ = sequence;
}

uint64_t ContractSequence::last_accepted() const noexcept
{
  return last_accepted_;
}

bool ContractSequence::is_faulted() const noexcept
{
  return faulted_;
}

RegistrationAdmission::RegistrationAdmission(
  std::shared_ptr<void> owner,
  ContractIdentity identity,
  std::shared_ptr<void> scheduler,
  std::shared_ptr<void> replay,
  uint64_t response_request_id,
  std::shared_ptr<void> rollback)
: owner_(std::move(owner)),
  identity_(std::move(identity)),
  scheduler_(std::move(scheduler)),
  replay_(std::move(replay)),
  rollback_(std::move(rollback)),
  response_request_id_(response_request_id)
{
}

RegistrationAdmission::~RegistrationAdmission() = default;

bool RegistrationAdmission::replayed() const noexcept
{
  return replayed_;
}

RemovalAdmission::RemovalAdmission(
  std::shared_ptr<void> owner,
  ContractIdentity identity,
  std::shared_ptr<void> scheduler,
  std::shared_ptr<void> replay,
  uint64_t response_request_id,
  std::shared_ptr<void> rollback)
: owner_(std::move(owner)),
  identity_(std::move(identity)),
  scheduler_(std::move(scheduler)),
  replay_(std::move(replay)),
  rollback_(std::move(rollback)),
  response_request_id_(response_request_id)
{
}

RemovalAdmission::~RemovalAdmission() = default;

bool RemovalAdmission::replayed() const noexcept
{
  return replayed_;
}

struct ContractAuthority::Impl final
{
  enum class State
  {
    registering = 1,
    ready = 2,
    removing = 3,
  };

  struct Entry
  {
    ContractIdentity identity;
    State state;
    ContractSequence sequence;
  };

  Impl(
    const ProtocolLimits & value,
    ContractAuthority::SemanticErrorFrameFactory error_frame_factory)
  : limits(value),
    semantic_error_frame_factory(std::move(error_frame_factory))
  {
  }

  mutable std::mutex mutex;
  ProtocolLimits limits;
  ContractAuthority::SemanticErrorFrameFactory semantic_error_frame_factory;
  std::unordered_map<ContractKey, Entry, ContractKeyHash> contracts;
  std::unordered_map<ContractKey, ContractIdentity, ContractKeyHash> tombstones;
  std::deque<ContractKey> tombstone_order;
  std::shared_ptr<void> bound_scheduler;
  std::shared_ptr<void> bound_replay;
  bool closed{false};

  void ensure_authority_pair(
    const std::shared_ptr<void> & scheduler,
    const std::shared_ptr<void> & replay) const
  {
    if (!bound_scheduler && !bound_replay) {
      return;
    }
    if (
      bound_scheduler.get() != scheduler.get() ||
      bound_replay.get() != replay.get())
    {
      throw std::logic_error(
              "the U2R2 contract authority belongs to another scheduler and replay authority");
    }
  }

  void bind_authority_pair(
    const std::shared_ptr<void> & scheduler,
    const std::shared_ptr<void> & replay)
  {
    ensure_authority_pair(scheduler, replay);
    if (bound_scheduler) {
      return;
    }
    bound_scheduler = scheduler;
    bound_replay = replay;
  }

  void add_tombstone(
    const ContractIdentity & identity,
    BoundedOutboundScheduler & scheduler)
  {
    if (tombstones.emplace(identity.key, identity).second) {
      tombstone_order.push_back(identity.key);
    }
    while (
      static_cast<uint64_t>(tombstones.size()) >
      limits.max_tombstones())
    {
      const auto evicted = tombstone_order.front();
      tombstones.erase(evicted);
      tombstone_order.pop_front();
      scheduler.forget_contract(evicted);
    }
  }

  void ensure_open() const
  {
    if (closed) {
      throw std::logic_error("the U2R2 contract authority is closed");
    }
  }
};

ContractAuthority::ContractAuthority(
  const ProtocolLimits & limits,
  SemanticErrorFrameFactory semantic_error_frame_factory)
: impl_(std::make_shared<Impl>(
    limits,
    std::move(semantic_error_frame_factory)))
{
  if (!impl_->semantic_error_frame_factory) {
    throw std::invalid_argument(
            "the semantic error frame factory is required");
  }
}

ContractAuthority::~ContractAuthority() = default;

RegistrationAdmission ContractAuthority::begin_registration(
  const ContractIdentity & identity,
  BoundedOutboundScheduler & scheduler,
  RequestReplayAuthority & replay,
  ReplayAdmission & response)
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  state->ensure_open();
  state->ensure_authority_pair(scheduler.impl_, replay.impl_);
  if (response.decision() == ReplayDecision::replay_cached) {
    if (!replay.is_cached_for(
      response, scheduler, &identity, Operation::RegisterSubscription)) {
      throw std::logic_error(
              "the replayed registration response is not bound to this contract");
    }
    state->bind_authority_pair(scheduler.impl_, replay.impl_);
    RegistrationAdmission result(
      state,
      identity,
      scheduler.impl_,
      replay.impl_,
      response.request_id());
    result.replayed_ = true;
    result.settled_ = true;
    return result;
  }
  if (!replay.try_claim_for_contract(
    response, scheduler, &identity, Operation::RegisterSubscription)) {
    throw std::logic_error(
            "registration requires the pending command response transaction bound to this contract");
  }
  state->bind_authority_pair(scheduler.impl_, replay.impl_);
  bool inserted = false;
  std::shared_ptr<void> rollback;
  try {
    if (identity.direction != ContractDirection::subscribe) {
      const ProtocolError error(
        "invalid_contract",
        "a register_subscription command requires subscribe direction",
        false);
      auto frame = state->semantic_error_frame_factory(
        Operation::SubscriptionReady,
        response.request_id(),
        error);
      if (!frame.is_control()) {
        throw std::logic_error(
                "the semantic error factory must return a control frame");
      }
      replay.finish(response, std::move(frame), false, true);
      throw error;
    }
    const auto active = state->contracts.find(identity.key);
    const auto removed = state->tombstones.find(identity.key);
    if (active != state->contracts.end() || removed != state->tombstones.end()) {
      const ProtocolError error(
        "invalid_contract",
        "the U2R2 contract ID and generation are already bound",
        false);
      auto frame = state->semantic_error_frame_factory(
        Operation::SubscriptionReady,
        response.request_id(),
        error);
      if (!frame.is_control()) {
        throw std::logic_error(
                "the semantic error factory must return a control frame");
      }
      replay.finish(response, std::move(frame), false, true);
      throw error;
    }
    if (
      static_cast<uint64_t>(state->contracts.size()) ==
      state->limits.max_contracts())
    {
      const ProtocolError error(
        "capacity_exceeded",
        "the U2R2 contract limit is exhausted",
        false);
      auto frame = state->semantic_error_frame_factory(
        Operation::SubscriptionReady,
        response.request_id(),
        error);
      if (!frame.is_control()) {
        throw std::logic_error(
                "the semantic error factory must return a control frame");
      }
      replay.finish(response, std::move(frame), false, true);
      throw error;
    }
    state->contracts.emplace(
      identity.key,
      typename Impl::Entry{
        identity,
        Impl::State::registering,
        ContractSequence()});
    inserted = true;
    scheduler.activate_contract(identity.key);
    rollback = std::make_shared<RollbackSettlement>(
      [state,
      replay_state = replay.impl_,
      scheduler_state = scheduler.impl_,
      key = identity.key,
      request_id = response.request_id()]() {
        std::lock_guard<std::mutex> contract_lock(state->mutex);
        const auto found = state->contracts.find(key);
        if (
          found == state->contracts.end() ||
          found->second.state != Impl::State::registering)
        {
          return;
        }
        (void)RequestReplayAuthority::try_abandon(
          replay_state, request_id, true);
        state->contracts.erase(found);
        std::lock_guard<std::mutex> scheduler_lock(scheduler_state->mutex);
        if (!scheduler_state->closed) {
          scheduler_state->revoke(key);
          scheduler_state->retire_when_drained.insert(key);
          scheduler_state->try_forget_retired(key);
        }
      });
    return RegistrationAdmission(
      state,
      identity,
      scheduler.impl_,
      replay.impl_,
      response.request_id(),
      rollback);
  } catch (...) {
    DisarmRollback(rollback);
    if (inserted) {
      state->contracts.erase(identity.key);
      scheduler.retire_contract(identity.key);
    }
    (void)replay.try_abandon(response, true);
    throw;
  }
}

void ContractAuthority::commit_ready(
  RegistrationAdmission & admission,
  RequestReplayAuthority & replay,
  ReplayAdmission & response,
  OutboundFrame exact_ready_frame)
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  if (admission.replayed_) {
    if (
      admission.owner_.get() != state.get() ||
      admission.replay_.get() != replay.impl_.get() ||
      admission.response_request_id_ != response.request_id() ||
      response.decision() != ReplayDecision::replay_cached)
    {
      throw std::logic_error(
              "the U2R2 replayed registration belongs elsewhere");
    }
    return;
  }
  if (
    admission.owner_.get() != state.get() ||
    admission.replay_.get() != replay.impl_.get() ||
    admission.response_request_id_ != response.request_id() ||
    admission.settled_ ||
    !admission.identity_.has_value())
  {
    throw std::logic_error(
            "the U2R2 registration admission is not pending");
  }
  const auto found = state->contracts.find(admission.identity_->key);
  if (
    found == state->contracts.end() ||
    found->second.state != Impl::State::registering ||
    found->second.identity != *admission.identity_)
  {
    throw std::logic_error(
            "the U2R2 registration admission is not pending");
  }
  replay.finish(
    response,
    std::move(exact_ready_frame),
    true,
    true,
    admission.identity_->key);
  found->second.state = Impl::State::ready;
  admission.settled_ = true;
  DisarmRollback(admission.rollback_);
}

void ContractAuthority::cancel_registration(
  RegistrationAdmission & admission,
  BoundedOutboundScheduler & scheduler,
  RequestReplayAuthority & replay,
  ReplayAdmission & response)
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  if (
    admission.owner_.get() != state.get() ||
    admission.scheduler_.get() != scheduler.impl_.get())
  {
    throw std::logic_error(
            "the U2R2 registration admission belongs elsewhere");
  }
  if (
    admission.replay_.get() != replay.impl_.get() ||
    admission.response_request_id_ != response.request_id())
  {
    throw std::logic_error(
            "the U2R2 registration response transaction belongs elsewhere");
  }
  if (admission.settled_) {
    return;
  }
  if (!admission.identity_.has_value()) {
    throw std::logic_error(
            "the U2R2 registration admission is not pending");
  }
  const auto found = state->contracts.find(admission.identity_->key);
  if (
    found == state->contracts.end() ||
    found->second.state != Impl::State::registering)
  {
    throw std::logic_error(
            "the U2R2 registration admission is not pending");
  }
  replay.cancel(response, true);
  state->contracts.erase(found);
  scheduler.retire_contract(admission.identity_->key);
  admission.settled_ = true;
  DisarmRollback(admission.rollback_);
}

void ContractAuthority::abort_registration(
  RegistrationAdmission & admission,
  BoundedOutboundScheduler & scheduler,
  RequestReplayAuthority & replay,
  ReplayAdmission & response,
  const ProtocolError & error)
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  if (
    admission.owner_.get() != state.get() ||
    admission.scheduler_.get() != scheduler.impl_.get())
  {
    throw std::logic_error(
            "the U2R2 registration admission belongs elsewhere");
  }
  if (
    admission.replay_.get() != replay.impl_.get() ||
    admission.response_request_id_ != response.request_id())
  {
    throw std::logic_error(
            "the U2R2 registration response transaction belongs elsewhere");
  }
  if (admission.settled_) {
    return;
  }
  if (!admission.identity_.has_value()) {
    throw std::logic_error(
            "the U2R2 registration admission is not pending");
  }
  const auto found = state->contracts.find(admission.identity_->key);
  if (
    found == state->contracts.end() ||
    found->second.state != Impl::State::registering)
  {
    throw std::logic_error(
            "the U2R2 registration admission is not pending");
  }
  auto frame = state->semantic_error_frame_factory(
    Operation::SubscriptionReady,
    response.request_id(),
    error);
  if (!frame.is_control()) {
    throw std::logic_error(
            "the semantic error factory must return a control frame");
  }
  replay.finish(response, std::move(frame), false, true);
  state->contracts.erase(found);
  scheduler.retire_contract(admission.identity_->key);
  admission.settled_ = true;
  DisarmRollback(admission.rollback_);
}

MessageAdmission ContractAuthority::admit_message(
  const ContractIdentity & identity,
  uint64_t sequence)
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  state->ensure_open();
  const auto tombstone = state->tombstones.find(identity.key);
  if (tombstone != state->tombstones.end()) {
    if (tombstone->second != identity) {
      throw ProtocolError(
              "contract_identity_mismatch",
              "the U2R2 message identity does not match its frozen contract",
              true);
    }
    return MessageAdmission::late_tombstone;
  }
  const auto found = state->contracts.find(identity.key);
  if (found == state->contracts.end()) {
    throw ProtocolError(
            "unknown_contract",
            "the U2R2 message references an unknown contract generation",
            true);
  }
  if (found->second.identity != identity) {
    throw ProtocolError(
            "contract_identity_mismatch",
            "the U2R2 message identity does not match its frozen contract",
            true);
  }
  if (found->second.state == Impl::State::registering) {
    throw ProtocolError(
            "contract_not_ready",
            "the U2R2 subscription_ready response is not committed",
            true);
  }
  if (found->second.state == Impl::State::removing) {
    return MessageAdmission::late_tombstone;
  }
  found->second.sequence.admit(sequence);
  return MessageAdmission::accepted;
}

RemovalAdmission ContractAuthority::begin_unregister(
  const ContractIdentity & identity,
  BoundedOutboundScheduler & scheduler,
  RequestReplayAuthority & replay,
  ReplayAdmission & response)
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  state->ensure_open();
  state->ensure_authority_pair(scheduler.impl_, replay.impl_);
  if (response.decision() == ReplayDecision::replay_cached) {
    if (!replay.is_cached_for(
      response, scheduler, &identity, Operation::UnregisterSubscription)) {
      throw std::logic_error(
              "the replayed unregister response is not bound to this contract");
    }
    state->bind_authority_pair(scheduler.impl_, replay.impl_);
    RemovalAdmission result(
      state,
      identity,
      scheduler.impl_,
      replay.impl_,
      response.request_id());
    result.replayed_ = true;
    result.settled_ = true;
    return result;
  }
  if (!replay.try_claim_for_contract(
    response, scheduler, &identity, Operation::UnregisterSubscription)) {
    throw std::logic_error(
            "unregister requires the pending command response transaction bound to this contract");
  }
  state->bind_authority_pair(scheduler.impl_, replay.impl_);
  bool removing = false;
  std::shared_ptr<void> rollback;
  try {
    const auto found = state->contracts.find(identity.key);
    if (
      found == state->contracts.end() ||
      found->second.state != Impl::State::ready)
    {
      const ProtocolError error(
        "unknown_contract",
        "the U2R2 unregister request references no ready contract",
        true);
      auto frame = state->semantic_error_frame_factory(
        Operation::SubscriptionRemoved,
        response.request_id(),
        error);
      if (!frame.is_control()) {
        throw std::logic_error(
                "the semantic error factory must return a control frame");
      }
      replay.finish(response, std::move(frame), false, true);
      throw error;
    }
    if (found->second.identity != identity) {
      const ProtocolError error(
        "invalid_contract",
        "the U2R2 unregister identity conflicts with the registered contract",
        false);
      auto frame = state->semantic_error_frame_factory(
        Operation::SubscriptionRemoved,
        response.request_id(),
        error);
      if (!frame.is_control()) {
        throw std::logic_error(
                "the semantic error factory must return a control frame");
      }
      replay.finish(response, std::move(frame), false, true);
      throw error;
    }
    scheduler.revoke_contract(identity.key);
    found->second.state = Impl::State::removing;
    removing = true;
    rollback = std::make_shared<RollbackSettlement>(
      [state,
      replay_state = replay.impl_,
      scheduler_state = scheduler.impl_,
      key = identity.key,
      request_id = response.request_id()]() {
        std::lock_guard<std::mutex> contract_lock(state->mutex);
        const auto found = state->contracts.find(key);
        if (
          found == state->contracts.end() ||
          found->second.state != Impl::State::removing)
        {
          return;
        }
        (void)RequestReplayAuthority::try_abandon(
          replay_state, request_id, true);
        state->contracts.erase(found);
        std::lock_guard<std::mutex> scheduler_lock(scheduler_state->mutex);
        if (!scheduler_state->closed) {
          scheduler_state->revoke(key);
          scheduler_state->retire_when_drained.insert(key);
          scheduler_state->try_forget_retired(key);
        }
      });
    return RemovalAdmission(
      state,
      identity,
      scheduler.impl_,
      replay.impl_,
      response.request_id(),
      rollback);
  } catch (...) {
    DisarmRollback(rollback);
    if (removing) {
      state->contracts.erase(identity.key);
      scheduler.retire_contract(identity.key);
    }
    (void)replay.try_abandon(response, true);
    throw;
  }
}

bool ContractAuthority::try_commit_removed(
  RemovalAdmission & admission,
  BoundedOutboundScheduler & scheduler,
  RequestReplayAuthority & replay,
  ReplayAdmission & response,
  OutboundFrame exact_removed_frame)
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  if (admission.replayed_) {
    if (
      admission.owner_.get() != state.get() ||
      admission.scheduler_.get() != scheduler.impl_.get() ||
      admission.replay_.get() != replay.impl_.get() ||
      admission.response_request_id_ != response.request_id() ||
      response.decision() != ReplayDecision::replay_cached)
    {
      throw std::logic_error(
              "the U2R2 replayed removal belongs elsewhere");
    }
    return true;
  }
  if (
    admission.owner_.get() != state.get() ||
    admission.scheduler_.get() != scheduler.impl_.get() ||
    admission.replay_.get() != replay.impl_.get() ||
    admission.response_request_id_ != response.request_id() ||
    admission.settled_ ||
    !admission.identity_.has_value())
  {
    throw std::logic_error(
            "the U2R2 removal admission is not pending");
  }
  const auto found = state->contracts.find(admission.identity_->key);
  if (
    found == state->contracts.end() ||
    found->second.state != Impl::State::removing ||
    found->second.identity != *admission.identity_)
  {
    throw std::logic_error(
            "the U2R2 removal admission is not pending");
  }
  if (!scheduler.is_contract_revoked_and_drained(admission.identity_->key)) {
    return false;
  }
  replay.finish(
    response,
    std::move(exact_removed_frame),
    true,
    true,
    admission.identity_->key);
  const auto identity = found->second.identity;
  state->contracts.erase(found);
  state->add_tombstone(identity, scheduler);
  admission.settled_ = true;
  DisarmRollback(admission.rollback_);
  return true;
}

void ContractAuthority::cancel_removal(
  RemovalAdmission & admission,
  BoundedOutboundScheduler & scheduler,
  RequestReplayAuthority & replay,
  ReplayAdmission & response)
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  if (
    admission.owner_.get() != state.get() ||
    admission.scheduler_.get() != scheduler.impl_.get())
  {
    throw std::logic_error(
            "the U2R2 removal admission belongs elsewhere");
  }
  if (
    admission.replay_.get() != replay.impl_.get() ||
    admission.response_request_id_ != response.request_id())
  {
    throw std::logic_error(
            "the U2R2 removal response transaction belongs elsewhere");
  }
  if (admission.settled_) {
    return;
  }
  if (!admission.identity_.has_value()) {
    throw std::logic_error(
            "the U2R2 removal admission is not pending");
  }
  const auto found = state->contracts.find(admission.identity_->key);
  if (
    found == state->contracts.end() ||
    found->second.state != Impl::State::removing)
  {
    throw std::logic_error(
            "the U2R2 removal admission is not pending");
  }
  replay.cancel(response, true);
  state->contracts.erase(found);
  scheduler.retire_contract(admission.identity_->key);
  admission.settled_ = true;
  DisarmRollback(admission.rollback_);
}

void ContractAuthority::abort_removal(
  RemovalAdmission & admission,
  BoundedOutboundScheduler & scheduler,
  RequestReplayAuthority & replay,
  ReplayAdmission & response,
  const ProtocolError & error)
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  if (
    admission.owner_.get() != state.get() ||
    admission.scheduler_.get() != scheduler.impl_.get())
  {
    throw std::logic_error(
            "the U2R2 removal admission belongs elsewhere");
  }
  if (
    admission.replay_.get() != replay.impl_.get() ||
    admission.response_request_id_ != response.request_id())
  {
    throw std::logic_error(
            "the U2R2 removal response transaction belongs elsewhere");
  }
  if (admission.settled_) {
    return;
  }
  if (!admission.identity_.has_value()) {
    throw std::logic_error(
            "the U2R2 removal admission is not pending");
  }
  const auto found = state->contracts.find(admission.identity_->key);
  if (
    found == state->contracts.end() ||
    found->second.state != Impl::State::removing)
  {
    throw std::logic_error(
            "the U2R2 removal admission is not pending");
  }
  auto frame = state->semantic_error_frame_factory(
    Operation::SubscriptionRemoved,
    response.request_id(),
    error);
  if (!frame.is_control()) {
    throw std::logic_error(
            "the semantic error factory must return a control frame");
  }
  replay.finish(response, std::move(frame), false, true);
  found->second.state = Impl::State::ready;
  scheduler.activate_contract(admission.identity_->key);
  admission.settled_ = true;
  DisarmRollback(admission.rollback_);
}

void ContractAuthority::close(
  BoundedOutboundScheduler & scheduler,
  RequestReplayAuthority & replay)
{
  auto state = impl_;
  std::lock_guard<std::mutex> lock(state->mutex);
  state->ensure_authority_pair(scheduler.impl_, replay.impl_);
  state->bind_authority_pair(scheduler.impl_, replay.impl_);
  if (state->closed) {
    return;
  }
  replay.close();
  scheduler.close();
  state->contracts.clear();
  state->tombstones.clear();
  state->tombstone_order.clear();
  state->closed = true;
}

uint64_t ContractAuthority::contract_count() const
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  return static_cast<uint64_t>(impl_->contracts.size());
}

uint64_t ContractAuthority::tombstone_count() const
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  return static_cast<uint64_t>(impl_->tombstones.size());
}

bool ContractAuthority::is_closed() const
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  return impl_->closed;
}

void parse_contract_fields(
  const nlohmann::json & header,
  Operation operation,
  std::string & topic,
  std::string & schema_name,
  std::optional<Qos> & qos)
{
  topic.clear();
  schema_name.clear();
  qos.reset();
  const auto has_contract_shape =
    operation == Operation::PreparePublisher ||
    operation == Operation::Publish ||
    operation == Operation::RegisterSubscription ||
    operation == Operation::Message;
  if (!has_contract_shape) {
    return;
  }

  topic = RequiredContractString(header, "topic");
  schema_name = RequiredContractString(header, "schemaName");
  ValidateTopic(topic);
  ValidateSchemaName(schema_name);
  if (operation == Operation::Message) {
    return;
  }

  if (RequiredContractString(header, "encoding") != "cdr") {
    InvalidContract("a U2R2 ROS contract requires cdr encoding");
  }
  const auto found = header.find("qos");
  if (
    found == header.end() ||
    !found->is_object() ||
    found->size() != 5 ||
    !found->contains("profile") ||
    !found->contains("reliability") ||
    !found->contains("durability") ||
    !found->contains("history") ||
    !found->contains("depth"))
  {
    InvalidContract("U2R2 qos must be an exact five-axis object");
  }
  static const std::unordered_set<std::string> expected_fields{
    "profile", "reliability", "durability", "history", "depth"};
  for (const auto & [name, unused] : found->items()) {
    (void)unused;
    if (expected_fields.find(name) == expected_fields.end()) {
      InvalidContract("U2R2 qos must be an exact five-axis object");
    }
  }

  Qos parsed{
    RequiredQosString(
      *found, "profile", {"default", "sensor_data", "system_default"}),
    RequiredQosString(
      *found, "reliability",
      {"reliable", "best_effort", "system_default"}),
    RequiredQosString(
      *found, "durability",
      {"volatile", "transient_local", "system_default"}),
    RequiredQosString(
      *found, "history", {"keep_last", "keep_all", "system_default"}),
    0};
  const auto depth = found->find("depth");
  if (
    depth == found->end() ||
    !depth->is_number_unsigned() ||
    depth->get<uint64_t>() > std::numeric_limits<uint32_t>::max())
  {
    InvalidContract(
      "U2R2 qos depth must be an unsigned 32-bit integer");
  }
  parsed.depth = depth->get<uint32_t>();
  if (
    (parsed.history == "keep_last" && parsed.depth == 0) ||
    (parsed.history != "keep_last" && parsed.depth != 0))
  {
    InvalidContract(
      "U2R2 qos depth is positive only for keep_last history");
  }
  qos = std::move(parsed);
}
