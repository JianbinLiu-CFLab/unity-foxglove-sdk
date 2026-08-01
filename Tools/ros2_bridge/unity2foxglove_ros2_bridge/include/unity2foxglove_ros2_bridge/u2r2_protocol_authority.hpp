// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Pure, transport-independent U2R2 replay, ordering, and resource authority.

#pragma once

#include <cstdint>
#include <functional>
#include <initializer_list>
#include <map>
#include <memory>
#include <mutex>
#include <optional>
#include <string>
#include <utility>
#include <vector>

#include <nlohmann/json_fwd.hpp>

#include "unity2foxglove_ros2_bridge/u2r2_protocol.hpp"

namespace unity2foxglove::ros2_bridge::u2r2
{
class ProtocolLimits final
{
public:
  static ProtocolLimits defaults();
  static ProtocolLimits from_diagnostic_snapshot(
    const std::map<std::string, uint64_t> & values);

  ProtocolLimits with(
    std::initializer_list<std::pair<const std::string, uint64_t>> overrides) const;
  std::map<std::string, uint64_t> to_diagnostic_snapshot() const;

  uint64_t max_connections() const;
  uint64_t max_data_sessions() const;
  uint64_t max_probes() const;
  uint64_t max_contracts() const;
  uint64_t max_outstanding_requests() const;
  uint64_t max_replay_entries() const;
  uint64_t max_replay_bytes() const;
  uint64_t max_tombstones() const;
  uint64_t fixed_frame_bytes() const;
  uint64_t max_header_bytes() const;
  uint64_t max_payload_bytes() const;
  uint64_t max_transient_bytes() const;
  uint64_t max_in_flight_bytes() const;
  uint64_t max_queued_bytes() const;
  uint64_t max_total_queue_depth() const;
  uint64_t max_per_contract_queue_depth() const;
  uint64_t max_per_contract_queue_bytes() const;
  uint64_t reserved_control_queue_depth() const;
  uint64_t reserved_control_queue_bytes() const;
  uint64_t control_burst_limit() const;
  uint64_t handshake_timeout_ms() const;
  uint64_t partial_frame_timeout_ms() const;
  uint64_t read_timeout_ms() const;
  uint64_t write_timeout_ms() const;
  uint64_t join_timeout_ms() const;
  uint64_t shutdown_timeout_ms() const;
  uint64_t max_json_depth() const;

private:
  explicit ProtocolLimits(std::map<std::string, uint64_t> values);
  uint64_t value(const std::string & name) const;

  const std::map<std::string, uint64_t> values_;
};

uint64_t checked_add(
  uint64_t current,
  uint64_t increment,
  uint64_t limit,
  const std::string & budget_name);

class FrameSize final
{
public:
  static FrameSize create(
    const ProtocolLimits & limits,
    uint64_t header_bytes,
    uint64_t payload_bytes);

  uint64_t header_bytes() const noexcept;
  uint64_t payload_bytes() const noexcept;
  uint64_t total_bytes() const noexcept;

private:
  FrameSize(
    uint64_t header_bytes,
    uint64_t payload_bytes,
    uint64_t total_bytes) noexcept;

  uint64_t header_bytes_;
  uint64_t payload_bytes_;
  uint64_t total_bytes_;
};

class CapacityCounter final
{
public:
  explicit CapacityCounter(uint64_t capacity);
  ~CapacityCounter();
  CapacityCounter(const CapacityCounter &) = delete;
  CapacityCounter & operator=(const CapacityCounter &) = delete;

  bool try_acquire();
  void release();
  uint64_t count() const;

private:
  struct Impl;
  std::unique_ptr<Impl> impl_;
};

enum class ConnectionRole
{
  data_session = 1,
  probe = 2,
};

class ResourceLease final
{
public:
  ResourceLease() = default;
  ~ResourceLease();
  ResourceLease(ResourceLease &&) noexcept = default;
  ResourceLease & operator=(ResourceLease && other) noexcept;
  ResourceLease(const ResourceLease &) = delete;
  ResourceLease & operator=(const ResourceLease &) = delete;

  bool release();

private:
  friend class SessionResourceAuthority;
  explicit ResourceLease(std::shared_ptr<void> settlement);
  std::shared_ptr<void> settlement_;
};

class SessionResourceAuthority final
{
public:
  explicit SessionResourceAuthority(const ProtocolLimits & limits);
  ~SessionResourceAuthority();
  SessionResourceAuthority(const SessionResourceAuthority &) = delete;
  SessionResourceAuthority & operator=(const SessionResourceAuthority &) = delete;

  std::optional<ResourceLease> try_acquire(ConnectionRole role);
  uint64_t connection_count() const;

private:
  struct Impl;
  std::shared_ptr<Impl> impl_;
};

class RequestIdCounter final
{
public:
  explicit RequestIdCounter(uint64_t current = 0) noexcept;
  uint64_t next();
  bool is_faulted() const;

private:
  mutable std::mutex mutex_;
  uint64_t current_;
  bool faulted_{false};
};

struct ContractKey
{
  ContractKey(uint64_t contract_id, uint64_t generation);

  uint64_t contract_id;
  uint64_t generation;

  bool operator==(const ContractKey &) const noexcept = default;
};

enum class ContractDirection
{
  publish = 1,
  subscribe = 2,
};

struct ContractIdentity
{
  ContractIdentity(
    ContractKey key,
    ContractDirection direction,
    std::string topic,
    std::string schema_name,
    Qos qos);

  ContractKey key;
  ContractDirection direction;
  std::string topic;
  std::string schema_name;
  Qos qos;

  bool operator==(const ContractIdentity &) const noexcept = default;
};

enum class QueueOverflowPolicy
{
  reject = 1,
  drop_oldest = 2,
  replace_latest = 3,
};

enum class EnqueueDisposition
{
  accepted = 1,
  rejected = 2,
  dropped_oldest = 3,
  replaced_latest = 4,
};

class OutboundFrame final
{
public:
  static OutboundFrame control(
    std::string token,
    std::vector<uint8_t> bytes);
  static OutboundFrame data(
    std::string token,
    ContractKey contract,
    uint64_t sequence,
    std::vector<uint8_t> bytes);

  const std::string & token() const noexcept;
  bool is_control() const noexcept;
  const ContractKey & contract() const noexcept;
  uint64_t sequence() const noexcept;
  const std::vector<uint8_t> & bytes() const noexcept;
  uint64_t byte_count() const noexcept;

private:
  OutboundFrame(
    std::string token,
    bool is_control,
    ContractKey contract,
    uint64_t sequence,
    std::vector<uint8_t> bytes);

  std::string token_;
  bool is_control_;
  ContractKey contract_;
  uint64_t sequence_;
  std::vector<uint8_t> bytes_;
};

class ControlReservation final
{
public:
  ControlReservation() = default;
  ~ControlReservation();
  ControlReservation(ControlReservation &&) noexcept = default;
  ControlReservation & operator=(ControlReservation && other) noexcept;
  ControlReservation(const ControlReservation &) = delete;
  ControlReservation & operator=(const ControlReservation &) = delete;

  void commit(OutboundFrame frame);
  bool try_commit(OutboundFrame frame);
  bool try_cancel();

private:
  friend class BoundedOutboundScheduler;
  friend class RequestReplayAuthority;
  friend class ContractAuthority;
  explicit ControlReservation(std::shared_ptr<void> settlement);
  bool try_commit_fenced(
    OutboundFrame frame,
    const ContractKey & fence_contract);
  std::shared_ptr<void> settlement_;
};

class DataReservation final
{
public:
  DataReservation() = default;
  ~DataReservation();
  DataReservation(DataReservation &&) noexcept = default;
  DataReservation & operator=(DataReservation && other) noexcept;
  DataReservation(const DataReservation &) = delete;
  DataReservation & operator=(const DataReservation &) = delete;

  bool try_commit(OutboundFrame frame);
  bool try_cancel();

private:
  friend class BoundedOutboundScheduler;
  explicit DataReservation(std::shared_ptr<void> settlement);
  std::shared_ptr<void> settlement_;
};

class ByteLease final
{
public:
  ByteLease() = default;
  ~ByteLease();
  ByteLease(ByteLease &&) noexcept = default;
  ByteLease & operator=(ByteLease && other) noexcept;
  ByteLease(const ByteLease &) = delete;
  ByteLease & operator=(const ByteLease &) = delete;

  bool release();

private:
  friend class BoundedOutboundScheduler;
  explicit ByteLease(std::shared_ptr<void> settlement);
  std::shared_ptr<void> settlement_;
};

class WriteLease final
{
public:
  WriteLease() = default;
  ~WriteLease();
  WriteLease(WriteLease &&) noexcept = default;
  WriteLease & operator=(WriteLease && other) noexcept;
  WriteLease(const WriteLease &) = delete;
  WriteLease & operator=(const WriteLease &) = delete;

  const OutboundFrame & frame() const;
  bool release();

private:
  friend class BoundedOutboundScheduler;
  WriteLease(OutboundFrame frame, std::shared_ptr<void> settlement);
  std::shared_ptr<OutboundFrame> frame_;
  std::shared_ptr<void> settlement_;
};

class BoundedOutboundScheduler final
{
public:
  explicit BoundedOutboundScheduler(const ProtocolLimits & limits);
  ~BoundedOutboundScheduler();
  BoundedOutboundScheduler(const BoundedOutboundScheduler &) = delete;
  BoundedOutboundScheduler & operator=(const BoundedOutboundScheduler &) = delete;

  std::optional<ControlReservation> try_reserve_control(uint64_t bytes);
  std::optional<DataReservation> try_reserve_data(
    const ContractKey & key,
    uint64_t bytes);
  EnqueueDisposition enqueue_data(
    OutboundFrame frame,
    QueueOverflowPolicy policy);
  std::optional<ByteLease> try_reserve_transient(uint64_t bytes);
  std::optional<ByteLease> try_begin_read(uint64_t bytes);
  std::optional<WriteLease> try_begin_write();
  void revoke_contract(const ContractKey & key);
  bool is_contract_revoked_and_drained(const ContractKey & key) const;

  uint64_t queued_bytes() const;
  uint64_t total_queued_depth() const;
  uint64_t data_queued_depth() const;
  uint64_t transient_bytes() const;
  uint64_t in_flight_bytes() const;
  uint64_t revoked_contract_count() const;
  bool is_closed() const;

private:
  friend class ContractAuthority;
  friend class RequestReplayAuthority;
  struct Impl;
  void activate_contract(const ContractKey & key);
  void retire_contract(const ContractKey & key);
  void forget_contract(const ContractKey & key);
  void close();
  std::shared_ptr<Impl> impl_;
};

enum class ReplayDecision
{
  begin_mutation = 1,
  replay_cached = 2,
};

class ReplayAdmission final
{
public:
  ReplayAdmission() = default;
  ~ReplayAdmission();
  ReplayAdmission(ReplayAdmission &&) noexcept = default;
  ReplayAdmission & operator=(ReplayAdmission &&) noexcept = default;
  ReplayAdmission(const ReplayAdmission &) = delete;
  ReplayAdmission & operator=(const ReplayAdmission &) = delete;

  ReplayDecision decision() const noexcept;
  uint64_t request_id() const noexcept;
  const std::vector<uint8_t> & cached_response() const noexcept;

private:
  friend class RequestReplayAuthority;
  ReplayAdmission(
    std::shared_ptr<void> owner,
    uint64_t request_id,
    ReplayDecision decision,
    std::vector<uint8_t> cached_response,
    std::shared_ptr<void> rollback = {});

  std::shared_ptr<void> owner_;
  uint64_t request_id_{0};
  ReplayDecision decision_{ReplayDecision::begin_mutation};
  std::vector<uint8_t> cached_response_;
  std::shared_ptr<void> rollback_;
  bool settled_{false};
};

class RequestReplayAuthority final
{
public:
  explicit RequestReplayAuthority(const ProtocolLimits & limits);
  ~RequestReplayAuthority();
  RequestReplayAuthority(const RequestReplayAuthority &) = delete;
  RequestReplayAuthority & operator=(const RequestReplayAuthority &) = delete;

  ReplayAdmission admit(
    uint64_t request_id,
    const std::vector<uint8_t> & canonical_request,
    uint64_t maximum_response_bytes,
    BoundedOutboundScheduler & scheduler);
  void complete(
    ReplayAdmission & admission,
    const std::vector<uint8_t> & exact_response);
  void abort(
    ReplayAdmission & admission,
    const std::vector<uint8_t> & exact_error_response);
  void cancel_pending(ReplayAdmission & admission);

  uint64_t high_water_mark() const;
  uint64_t outstanding_requests() const;
  uint64_t retained_entries() const;
  uint64_t replay_bytes() const;
  bool is_closed() const;

private:
  friend class ContractAuthority;
  struct Impl;
  void finish(
    ReplayAdmission & admission,
    OutboundFrame exact_response,
    bool priority_fence,
    bool require_claimed,
    std::optional<ContractKey> fence_contract = std::nullopt);
  void cancel(
    ReplayAdmission & admission,
    bool require_claimed);
  bool try_claim_for_contract(
    ReplayAdmission & admission,
    const BoundedOutboundScheduler & scheduler);
  void release_contract_claim(
    ReplayAdmission & admission,
    const BoundedOutboundScheduler & scheduler);
  bool is_cached_for(
    const ReplayAdmission & admission,
    const BoundedOutboundScheduler & scheduler) const;
  bool try_abandon(
    ReplayAdmission & admission,
    bool require_claimed) noexcept;
  static bool try_abandon(
    const std::shared_ptr<Impl> & state,
    uint64_t request_id,
    bool require_claimed) noexcept;
  void close();
  std::shared_ptr<Impl> impl_;
};

enum class MessageAdmission
{
  accepted = 1,
  late_tombstone = 2,
};

class ContractSequence final
{
public:
  explicit ContractSequence(uint64_t starting_sequence = 0) noexcept;
  void admit(uint64_t sequence);
  uint64_t last_accepted() const noexcept;
  bool is_faulted() const noexcept;

private:
  uint64_t last_accepted_;
  bool faulted_{false};
};

class RegistrationAdmission final
{
public:
  RegistrationAdmission() = default;
  ~RegistrationAdmission();
  RegistrationAdmission(RegistrationAdmission &&) noexcept = default;
  RegistrationAdmission & operator=(RegistrationAdmission &&) noexcept = default;
  RegistrationAdmission(const RegistrationAdmission &) = delete;
  RegistrationAdmission & operator=(const RegistrationAdmission &) = delete;
  bool replayed() const noexcept;

private:
  friend class ContractAuthority;
  RegistrationAdmission(
    std::shared_ptr<void> owner,
    ContractIdentity identity,
    std::shared_ptr<void> scheduler,
    std::shared_ptr<void> replay,
    uint64_t response_request_id,
    std::shared_ptr<void> rollback = {});
  std::shared_ptr<void> owner_;
  std::optional<ContractIdentity> identity_;
  std::shared_ptr<void> scheduler_;
  std::shared_ptr<void> replay_;
  std::shared_ptr<void> rollback_;
  uint64_t response_request_id_{0};
  bool replayed_{false};
  bool settled_{false};
};

class RemovalAdmission final
{
public:
  RemovalAdmission() = default;
  ~RemovalAdmission();
  RemovalAdmission(RemovalAdmission &&) noexcept = default;
  RemovalAdmission & operator=(RemovalAdmission &&) noexcept = default;
  RemovalAdmission(const RemovalAdmission &) = delete;
  RemovalAdmission & operator=(const RemovalAdmission &) = delete;
  bool replayed() const noexcept;

private:
  friend class ContractAuthority;
  RemovalAdmission(
    std::shared_ptr<void> owner,
    ContractIdentity identity,
    std::shared_ptr<void> scheduler,
    std::shared_ptr<void> replay,
    uint64_t response_request_id,
    std::shared_ptr<void> rollback = {});
  std::shared_ptr<void> owner_;
  std::optional<ContractIdentity> identity_;
  std::shared_ptr<void> scheduler_;
  std::shared_ptr<void> replay_;
  std::shared_ptr<void> rollback_;
  uint64_t response_request_id_{0};
  bool replayed_{false};
  bool settled_{false};
};

class ContractAuthority final
{
public:
  using SemanticErrorFrameFactory = std::function<OutboundFrame(
      Operation,
      uint64_t,
      const ProtocolError &)>;

  ContractAuthority(
    const ProtocolLimits & limits,
    SemanticErrorFrameFactory semantic_error_frame_factory);
  ~ContractAuthority();
  ContractAuthority(const ContractAuthority &) = delete;
  ContractAuthority & operator=(const ContractAuthority &) = delete;

  RegistrationAdmission begin_registration(
    const ContractIdentity & identity,
    BoundedOutboundScheduler & scheduler,
    RequestReplayAuthority & replay,
    ReplayAdmission & response);
  void commit_ready(
    RegistrationAdmission & admission,
    RequestReplayAuthority & replay,
    ReplayAdmission & response,
    OutboundFrame exact_ready_frame);
  void cancel_registration(
    RegistrationAdmission & admission,
    BoundedOutboundScheduler & scheduler,
    RequestReplayAuthority & replay,
    ReplayAdmission & response);
  void abort_registration(
    RegistrationAdmission & admission,
    BoundedOutboundScheduler & scheduler,
    RequestReplayAuthority & replay,
    ReplayAdmission & response,
    const ProtocolError & error);
  MessageAdmission admit_message(
    const ContractIdentity & identity,
    uint64_t sequence);
  RemovalAdmission begin_unregister(
    const ContractIdentity & identity,
    BoundedOutboundScheduler & scheduler,
    RequestReplayAuthority & replay,
    ReplayAdmission & response);
  bool try_commit_removed(
    RemovalAdmission & admission,
    BoundedOutboundScheduler & scheduler,
    RequestReplayAuthority & replay,
    ReplayAdmission & response,
    OutboundFrame exact_removed_frame);
  void cancel_removal(
    RemovalAdmission & admission,
    BoundedOutboundScheduler & scheduler,
    RequestReplayAuthority & replay,
    ReplayAdmission & response);
  void abort_removal(
    RemovalAdmission & admission,
    BoundedOutboundScheduler & scheduler,
    RequestReplayAuthority & replay,
    ReplayAdmission & response,
    const ProtocolError & error);
  void close(
    BoundedOutboundScheduler & scheduler,
    RequestReplayAuthority & replay);

  uint64_t contract_count() const;
  uint64_t tombstone_count() const;
  bool is_closed() const;

private:
  struct Impl;
  std::shared_ptr<Impl> impl_;
};

void parse_contract_fields(
  const nlohmann::json & header,
  Operation operation,
  std::string & topic,
  std::string & schema_name,
  std::optional<Qos> & qos);

enum class PureSessionState
{
  active = 1,
  closed = 2,
};

enum class TimeoutKind
{
  handshake = 1,
  partial_frame = 2,
  read = 3,
  write = 4,
  join = 5,
  shutdown = 6,
};

class PureSessionLifecycle final
{
public:
  PureSessionLifecycle() = default;
  explicit PureSessionLifecycle(const ProtocolLimits & limits);

  uint64_t limit_for(TimeoutKind kind) const;
  bool has_timed_out(TimeoutKind kind, uint64_t elapsed_ms) const;
  void timeout(TimeoutKind kind, uint64_t elapsed_ms);
  void peer_closed();
  PureSessionState state() const noexcept;

private:
  std::optional<ProtocolLimits> limits_;
  PureSessionState state_{PureSessionState::active};
};
}  // namespace unity2foxglove::ros2_bridge::u2r2
