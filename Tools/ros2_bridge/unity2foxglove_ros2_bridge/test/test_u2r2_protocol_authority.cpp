// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/ros2_bridge
// Purpose: Cross-language authority tests for bounded U2R2 replay and ordering.

#include <gtest/gtest.h>

#include <algorithm>
#include <atomic>
#include <cstdint>
#include <fstream>
#include <functional>
#include <limits>
#include <map>
#include <stdexcept>
#include <string>
#include <thread>
#include <unordered_map>
#include <unordered_set>
#include <vector>

#include <nlohmann/json.hpp>

#include "unity2foxglove_ros2_bridge/u2r2_protocol_authority.hpp"

namespace
{
namespace u2r2 = unity2foxglove::ros2_bridge::u2r2;

#ifndef U2R2_PROTOCOL_FIXTURE_PATH
#error "U2R2_PROTOCOL_FIXTURE_PATH must identify the shared U2R2 authority fixture"
#endif

nlohmann::json LoadFixture()
{
  std::ifstream input(U2R2_PROTOCOL_FIXTURE_PATH, std::ios::binary);
  if (!input) {
    throw std::runtime_error("unable to open shared U2R2 authority fixture");
  }
  nlohmann::json fixture;
  input >> fixture;
  return fixture;
}

nlohmann::json LoadAuthority()
{
  return LoadFixture().at("v2").at("commit2");
}

nlohmann::json RegisterSubscriptionHeader()
{
  const auto fixture = LoadFixture();
  for (const auto & vector : fixture.at("v2").at("operations")) {
    if (vector.at("id").get<std::string>() == "register_subscription") {
      return vector.at("header");
    }
  }
  throw std::runtime_error("missing register_subscription fixture");
}

nlohmann::json InvalidQosShape(
  const nlohmann::json & valid,
  const std::string & mutation)
{
  if (mutation == "non_object") {
    return "default";
  }
  auto value = valid;
  if (mutation == "missing_axis") {
    value.erase("profile");
  } else if (mutation == "extra_axis") {
    value["deadline"] = 1;
  } else if (mutation == "profile_non_string") {
    value["profile"] = 1;
  } else if (mutation == "reliability_non_string") {
    value["reliability"] = 1;
  } else if (mutation == "durability_non_string") {
    value["durability"] = 1;
  } else if (mutation == "history_non_string") {
    value["history"] = 1;
  } else if (mutation == "depth_negative") {
    value["depth"] = -1;
  } else if (mutation == "depth_fraction") {
    value["depth"] = 1.5;
  } else if (mutation == "depth_above_uint32") {
    value["depth"] = uint64_t{4294967296ULL};
  } else {
    throw std::logic_error("unknown qos-shape mutation");
  }
  return value;
}

u2r2::Message ParseRegistration(
  const std::string & topic,
  const std::string & schema_name,
  const nlohmann::json & qos)
{
  auto header = RegisterSubscriptionHeader();
  header["topic"] = topic;
  header["schemaName"] = schema_name;
  header["qos"] = qos;
  return u2r2::parse_v2({std::move(header), {}});
}

std::vector<uint8_t> Bytes(const std::string & hex)
{
  std::vector<uint8_t> result;
  for (size_t offset = 0; offset < hex.size(); offset += 2) {
    result.push_back(static_cast<uint8_t>(
      std::stoul(hex.substr(offset, 2), nullptr, 16)));
  }
  return result;
}

uint64_t ParseU64(const nlohmann::json & value)
{
  return std::stoull(value.get<std::string>());
}

u2r2::ProtocolLimits LimitsFrom(const nlohmann::json & source)
{
  std::map<std::string, uint64_t> values;
  for (auto item = source.begin(); item != source.end(); ++item) {
    values.emplace(item.key(), item.value().get<uint64_t>());
  }
  return u2r2::ProtocolLimits::from_diagnostic_snapshot(values);
}

template<typename Callback>
void ExpectProtocolError(const nlohmann::json & scenario, Callback callback)
{
  ASSERT_TRUE(scenario.contains("expectedErrorCode"))
    << "missing expectedErrorCode in scenario "
    << scenario.value("id", "<nested>");
  try {
    callback();
    FAIL() << "expected ProtocolError";
  } catch (const u2r2::ProtocolError & error) {
    EXPECT_EQ(scenario.at("expectedErrorCode").get<std::string>(), error.code());
    EXPECT_EQ(scenario.at("terminal").get<bool>(), error.terminal());
  }
}

void EnqueueControl(u2r2::BoundedOutboundScheduler & scheduler, const std::string & token)
{
  auto reservation = scheduler.try_reserve_control(1);
  ASSERT_TRUE(reservation.has_value());
  reservation->commit(u2r2::OutboundFrame::control(token, Bytes("01")));
}

u2r2::OutboundFrame DrainOne(u2r2::BoundedOutboundScheduler & scheduler)
{
  auto writer = scheduler.try_begin_write();
  if (!writer.has_value()) {
    throw std::runtime_error("expected an outbound frame");
  }
  return writer->frame();
}

std::vector<std::string> DrainAll(u2r2::BoundedOutboundScheduler & scheduler)
{
  std::vector<std::string> result;
  while (auto writer = scheduler.try_begin_write()) {
    result.push_back(writer->frame().token());
  }
  return result;
}

u2r2::ContractKey Key(const nlohmann::json & scenario)
{
  return {
    scenario.at("contractId").get<uint64_t>(),
    scenario.at("generation").get<uint64_t>()};
}

u2r2::ContractIdentity Identity(
  const u2r2::ContractKey & key,
  const std::string & topic = "/camera/front",
  const std::string & schema_name = "sensor_msgs/msg/Image",
  u2r2::ContractDirection direction = u2r2::ContractDirection::subscribe)
{
  return {
    key,
    direction,
    topic,
    schema_name,
    {"default", "reliable", "volatile", "keep_last", 10}};
}

u2r2::ContractIdentity AliasIdentity(
  u2r2::ContractIdentity identity,
  const std::string & mutation)
{
  if (mutation == "topic") {
    identity.topic = "/camera/rear";
  } else if (mutation == "schemaName") {
    identity.schema_name = "demo_interfaces/msg/Telemetry";
  } else if (mutation == "profile") {
    identity.qos.profile = "sensor_data";
  } else if (mutation == "reliability") {
    identity.qos.reliability = "best_effort";
  } else if (mutation == "durability") {
    identity.qos.durability = "transient_local";
  } else if (mutation == "history") {
    identity.qos.history = "keep_all";
    identity.qos.depth = 0;
  } else if (mutation == "depth") {
    identity.qos.depth = 11;
  } else if (mutation == "direction") {
    identity.direction = u2r2::ContractDirection::publish;
  } else {
    throw std::logic_error("unknown identity alias mutation");
  }
  return identity;
}

std::vector<uint8_t> RequestBytes(
  const std::string & operation,
  const u2r2::ContractIdentity & identity)
{
  const auto canonical = nlohmann::json{
    {"op", operation},
    {"contractId", identity.key.contract_id},
    {"generation", identity.key.generation},
    {"direction", identity.direction == u2r2::ContractDirection::publish
      ? "publish"
      : "subscribe"},
    {"topic", identity.topic},
    {"schemaName", identity.schema_name},
    {"qos", {
      {"profile", identity.qos.profile},
      {"reliability", identity.qos.reliability},
      {"durability", identity.qos.durability},
      {"history", identity.qos.history},
      {"depth", identity.qos.depth},
    }},
  }.dump();
  return {canonical.begin(), canonical.end()};
}

std::string OperationToken(u2r2::Operation operation)
{
  if (operation == u2r2::Operation::SubscriptionReady) {
    return "subscription_ready";
  }
  if (operation == u2r2::Operation::SubscriptionRemoved) {
    return "subscription_removed";
  }
  throw std::logic_error("unexpected contract response operation");
}

u2r2::OutboundFrame DefaultSemanticErrorFrame(
  u2r2::Operation operation,
  uint64_t request_id,
  const u2r2::ProtocolError & error)
{
  return u2r2::OutboundFrame::control(
    OperationToken(operation) + ":" + std::to_string(request_id) + ":" +
    error.code(),
    Bytes("ee"));
}

void Race(size_t worker_count, const std::function<void()> & action)
{
  std::atomic<size_t> ready{0};
  std::atomic<bool> go{false};
  std::vector<std::thread> workers;
  workers.reserve(worker_count);
  for (size_t index = 0; index < worker_count; ++index) {
    workers.emplace_back([&]() {
      ready.fetch_add(1);
      while (!go.load()) {
        std::this_thread::yield();
      }
      action();
    });
  }
  while (ready.load() != worker_count) {
    std::this_thread::yield();
  }
  go.store(true);
  for (auto & worker : workers) {
    worker.join();
  }
}

void RaceTwo(
  const std::function<void()> & first,
  const std::function<void()> & second)
{
  std::atomic<size_t> ready{0};
  std::atomic<bool> go{false};
  std::thread first_thread([&]() {
    ready.fetch_add(1);
    while (!go.load()) {
      std::this_thread::yield();
    }
    first();
  });
  std::thread second_thread([&]() {
    ready.fetch_add(1);
    while (!go.load()) {
      std::this_thread::yield();
    }
    second();
  });
  while (ready.load() != 2) {
    std::this_thread::yield();
  }
  go.store(true);
  first_thread.join();
  second_thread.join();
}

void RegisterAndRemove(
  u2r2::ContractAuthority & authority,
  u2r2::BoundedOutboundScheduler & scheduler,
  u2r2::RequestReplayAuthority & replay,
  const u2r2::ContractKey & key,
  uint64_t first_request_id)
{
  const auto identity = Identity(key);
  auto register_response = replay.admit(
    first_request_id,
    RequestBytes("register_subscription", identity),
    1,
    scheduler);
  auto registration = authority.begin_registration(
    identity, scheduler, replay, register_response);
  authority.commit_ready(
    registration,
    replay,
    register_response,
    u2r2::OutboundFrame::control("subscription_ready", Bytes("01")));
  (void)DrainOne(scheduler);
  auto remove_response = replay.admit(
    first_request_id + 1,
    RequestBytes("unregister_subscription", identity),
    1,
    scheduler);
  auto removal = authority.begin_unregister(
    identity, scheduler, replay, remove_response);
  ASSERT_TRUE(authority.try_commit_removed(
      removal,
      scheduler,
      replay,
      remove_response,
      u2r2::OutboundFrame::control("subscription_removed", Bytes("02"))));
  (void)DrainOne(scheduler);
}

void RegisterReady(
  u2r2::ContractAuthority & authority,
  u2r2::BoundedOutboundScheduler & scheduler,
  u2r2::RequestReplayAuthority & replay,
  const u2r2::ContractIdentity & identity,
  uint64_t request_id)
{
  auto response = replay.admit(
    request_id,
    RequestBytes("register_subscription", identity),
    1,
    scheduler);
  auto registration = authority.begin_registration(
    identity, scheduler, replay, response);
  authority.commit_ready(
    registration,
    replay,
    response,
    u2r2::OutboundFrame::control(
      "subscription_ready",
      Bytes("01")));
  (void)DrainOne(scheduler);
}

TEST(U2R2ProtocolAuthority, SharedCommit2LedgerDrivesEveryBoundedAuthorityScenario)
{
  const auto authority_json = LoadAuthority();
  const auto limits_json = authority_json.at("limits");
  const auto limits = LimitsFrom(limits_json);
  const auto & scenarios = authority_json.at("scenarios");
  ASSERT_EQ(56U, scenarios.size());

  std::unordered_set<std::string> consumed;
  for (const auto & scenario : scenarios) {
    const auto id = scenario.at("id").get<std::string>();
    SCOPED_TRACE(id);
    ASSERT_TRUE(consumed.insert(id).second) << id;

    if (id == "sender_starts_at_one") {
      u2r2::RequestIdCounter counter;
      EXPECT_EQ(scenario.at("expectedIds").at(0).get<uint64_t>(), counter.next());
      EXPECT_EQ(scenario.at("expectedIds").at(1).get<uint64_t>(), counter.next());
    } else if (id == "receiver_accepts_higher_first") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      u2r2::RequestReplayAuthority replay(limits);
      auto admission = replay.admit(
        scenario.at("requestId").get<uint64_t>(), Bytes("01"), 1, scheduler);
      EXPECT_EQ(u2r2::ReplayDecision::begin_mutation, admission.decision());
      replay.complete(admission, Bytes("aa"));
      EXPECT_EQ(scenario.at("expectedHighWater").get<uint64_t>(), replay.high_water_mark());
    } else if (id == "retained_identical_replay") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      u2r2::RequestReplayAuthority replay(limits);
      const auto request = Bytes(scenario.at("canonicalRequestHex").get<std::string>());
      const auto response = Bytes(scenario.at("responseHex").get<std::string>());
      auto first = replay.admit(
        scenario.at("requestId").get<uint64_t>(), request, response.size(), scheduler);
      replay.complete(first, response);
      (void)DrainOne(scheduler);
      auto repeated = replay.admit(
        scenario.at("requestId").get<uint64_t>(), request, response.size(), scheduler);
      EXPECT_EQ(u2r2::ReplayDecision::replay_cached, repeated.decision());
      EXPECT_EQ(response, repeated.cached_response());
      EXPECT_EQ(response, DrainOne(scheduler).bytes());
    } else if (id == "retained_payload_conflict") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      u2r2::RequestReplayAuthority replay(limits);
      const auto request_id = scenario.at("requestId").get<uint64_t>();
      auto first = replay.admit(
        request_id, Bytes(scenario.at("canonicalRequestHex").get<std::string>()), 1, scheduler);
      replay.complete(first, Bytes("aa"));
      (void)DrainOne(scheduler);
      ExpectProtocolError(scenario, [&]() {
        (void)replay.admit(
          request_id,
          Bytes(scenario.at("conflictingRequestHex").get<std::string>()),
          1,
          scheduler);
      });
    } else if (id == "stale_after_replay_eviction") {
      const auto bounded = limits.with({
        {"maxOutstandingRequests", 2}, {"maxReplayEntries", 2},
        {"maxReplayBytes", 64},
        {"reservedControlQueueDepth", 4}, {"reservedControlQueueBytes", 64}});
      u2r2::BoundedOutboundScheduler scheduler(bounded);
      u2r2::RequestReplayAuthority replay(bounded);
      for (const auto & value : scenario.at("requestIds")) {
        const auto request_id = value.get<uint64_t>();
        auto admission = replay.admit(
          request_id, {static_cast<uint8_t>(request_id)}, 1, scheduler);
        replay.complete(admission, {static_cast<uint8_t>(request_id + 10)});
        (void)DrainOne(scheduler);
      }
      ExpectProtocolError(scenario, [&]() {
        (void)replay.admit(
          scenario.at("staleRequestId").get<uint64_t>(), Bytes("01"), 1, scheduler);
      });
      auto next = replay.admit(4, Bytes("04"), 1, scheduler);
      EXPECT_EQ(u2r2::ReplayDecision::begin_mutation, next.decision());
    } else if (id == "control_reserved_before_mutation") {
      const auto bounded = limits.with({
        {"reservedControlQueueDepth", 1}, {"reservedControlQueueBytes", 8},
        {"controlBurstLimit", 1}});
      u2r2::BoundedOutboundScheduler scheduler(bounded);
      auto occupied = scheduler.try_reserve_control(8);
      ASSERT_TRUE(occupied.has_value());
      occupied->commit(u2r2::OutboundFrame::control("occupied", std::vector<uint8_t>(8)));
      u2r2::RequestReplayAuthority replay(bounded);
      ExpectProtocolError(scenario, [&]() {
        (void)replay.admit(
          scenario.at("requestId").get<uint64_t>(), Bytes("01"), 1, scheduler);
      });
      EXPECT_EQ(0U, replay.high_water_mark());
      EXPECT_EQ(0U, replay.outstanding_requests());
    } else if (id == "replay_bytes_max_plus_one") {
      const auto bounded = limits.with({{"maxReplayBytes", 8}});
      u2r2::BoundedOutboundScheduler scheduler(bounded);
      u2r2::RequestReplayAuthority replay(bounded);
      ExpectProtocolError(scenario, [&]() {
        (void)replay.admit(
          scenario.at("requestId").get<uint64_t>(),
          std::vector<uint8_t>(4), 5, scheduler);
      });
      EXPECT_EQ(0U, replay.high_water_mark());
    } else if (id == "ready_precedes_message") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      u2r2::ContractAuthority contracts(limits, DefaultSemanticErrorFrame);
      u2r2::RequestReplayAuthority replay(limits);
      const auto key = Key(scenario);
      const auto identity = Identity(key);
      auto ready_response = replay.admit(
        1, RequestBytes("register_subscription", identity), 1, scheduler);
      auto registration = contracts.begin_registration(
        identity, scheduler, replay, ready_response);
      ExpectProtocolError(scenario, [&]() {
        (void)contracts.admit_message(
          identity,
          scenario.at("firstSequence").get<uint64_t>());
      });
      contracts.commit_ready(
        registration,
        replay,
        ready_response,
        u2r2::OutboundFrame::control("subscription_ready", Bytes("01")));
      EXPECT_EQ(
        u2r2::MessageAdmission::accepted,
        contracts.admit_message(
          identity,
          scenario.at("firstSequence").get<uint64_t>()));
      (void)scheduler.enqueue_data(
        u2r2::OutboundFrame::data("message", key, 1, Bytes("02")),
        u2r2::QueueOverflowPolicy::reject);
      EXPECT_EQ(
        scenario.at("expectedOrder").get<std::vector<std::string>>(),
        DrainAll(scheduler));
    } else if (id == "unregister_fences_writer") {
      const auto key = Key(scenario);
      u2r2::BoundedOutboundScheduler scheduler(limits);
      u2r2::ContractAuthority contracts(limits, DefaultSemanticErrorFrame);
      u2r2::RequestReplayAuthority replay(limits);
      const auto identity = Identity(key);
      auto ready_response = replay.admit(
        1, RequestBytes("register_subscription", identity), 1, scheduler);
      auto registration = contracts.begin_registration(
        identity, scheduler, replay, ready_response);
      contracts.commit_ready(
        registration,
        replay,
        ready_response,
        u2r2::OutboundFrame::control("subscription_ready", Bytes("01")));
      std::vector<std::string> order{DrainOne(scheduler).token()};
      EXPECT_EQ(
        u2r2::EnqueueDisposition::accepted,
        scheduler.enqueue_data(
          u2r2::OutboundFrame::data("message", key, 1, Bytes("aa")),
          u2r2::QueueOverflowPolicy::reject));
      auto writer = scheduler.try_begin_write();
      ASSERT_TRUE(writer.has_value());
      order.push_back(writer->frame().token());
      auto removed_response = replay.admit(
        2, RequestBytes("unregister_subscription", identity), 1, scheduler);
      auto removal = contracts.begin_unregister(
        identity, scheduler, replay, removed_response);
      EXPECT_FALSE(contracts.try_commit_removed(
          removal,
          scheduler,
          replay,
          removed_response,
          u2r2::OutboundFrame::control("subscription_removed", Bytes("02"))));
      EXPECT_EQ(
        u2r2::EnqueueDisposition::rejected,
        scheduler.enqueue_data(
          u2r2::OutboundFrame::data("late", key, 2, Bytes("bb")),
          u2r2::QueueOverflowPolicy::reject));
      writer.reset();
      EXPECT_TRUE(contracts.try_commit_removed(
          removal,
          scheduler,
          replay,
          removed_response,
          u2r2::OutboundFrame::control("subscription_removed", Bytes("02"))));
      order.push_back(DrainOne(scheduler).token());
      EXPECT_EQ(scenario.at("expectedOrder").get<std::vector<std::string>>(), order);
      EXPECT_EQ(
        u2r2::MessageAdmission::late_tombstone,
        contracts.admit_message(identity, 2));
    } else if (id == "bounded_generation_tombstones") {
      const auto bounded = limits.with({{"maxTombstones", 1}});
      u2r2::BoundedOutboundScheduler scheduler(bounded);
      u2r2::RequestReplayAuthority replay(bounded);
      u2r2::ContractAuthority contracts(bounded, DefaultSemanticErrorFrame);
      const u2r2::ContractKey first{
        scenario.at("firstContractId").get<uint64_t>(),
        scenario.at("generation").get<uint64_t>()};
      const u2r2::ContractKey second{
        scenario.at("secondContractId").get<uint64_t>(),
        scenario.at("generation").get<uint64_t>()};
      RegisterAndRemove(contracts, scheduler, replay, first, 1);
      RegisterAndRemove(contracts, scheduler, replay, second, 3);
      EXPECT_EQ(1U, contracts.tombstone_count());
      EXPECT_EQ(
        scenario.at("expectedRevokedContracts").get<uint64_t>(),
        scheduler.revoked_contract_count());
      ExpectProtocolError(scenario, [&]() {
        (void)contracts.admit_message(Identity(first), 1);
      });
      EXPECT_EQ(
        u2r2::MessageAdmission::late_tombstone,
        contracts.admit_message(Identity(second), 1));
    } else if (id == "unknown_contract_faults") {
      u2r2::ContractAuthority contracts(limits, DefaultSemanticErrorFrame);
      ExpectProtocolError(scenario, [&]() {
        (void)contracts.admit_message(Identity(Key(scenario)), 1);
      });
    } else if (id == "sequence_starts_one_and_is_monotonic") {
      u2r2::ContractSequence sequence;
      for (const auto & value : scenario.at("acceptedSequences")) {
        sequence.admit(value.get<uint64_t>());
      }
      EXPECT_EQ(2U, sequence.last_accepted());
      ExpectProtocolError(scenario, [&]() {
        sequence.admit(scenario.at("rejectedSequence").get<uint64_t>());
      });
    } else if (id == "sequence_faults_before_wrap") {
      u2r2::ContractSequence sequence(ParseU64(scenario.at("startingSequence")));
      sequence.admit(ParseU64(scenario.at("lastAcceptedSequence")));
      ExpectProtocolError(scenario, [&]() {sequence.admit(0);});
      EXPECT_TRUE(sequence.is_faulted());
    } else if (
      id == "drop_oldest_is_contract_local" ||
      id == "replace_latest_is_contract_local")
    {
      const auto bounded = limits.with({
        {"fixedFrameBytes", 1}, {"maxHeaderBytes", 1}, {"maxPayloadBytes", 8},
        {"maxTransientBytes", 16}, {"maxInFlightBytes", 16},
        {"maxPerContractQueueDepth", 2}, {"maxPerContractQueueBytes", 16},
        {"maxTotalQueueDepth", 8}, {"maxQueuedBytes", 128},
        {"reservedControlQueueDepth", 2}, {"reservedControlQueueBytes", 16}});
      u2r2::BoundedOutboundScheduler scheduler(bounded);
      const u2r2::ContractKey cold{1, 1};
      const u2r2::ContractKey hot{2, 1};
      const auto policy = id == "drop_oldest_is_contract_local"
        ? u2r2::QueueOverflowPolicy::drop_oldest
        : u2r2::QueueOverflowPolicy::replace_latest;
      (void)scheduler.enqueue_data(
        u2r2::OutboundFrame::data("cold-1", cold, 1, Bytes("01")),
        u2r2::QueueOverflowPolicy::reject);
      (void)scheduler.enqueue_data(
        u2r2::OutboundFrame::data("hot-1", hot, 1, Bytes("02")), policy);
      (void)scheduler.enqueue_data(
        u2r2::OutboundFrame::data("hot-2", hot, 2, Bytes("03")), policy);
      const auto disposition = scheduler.enqueue_data(
        u2r2::OutboundFrame::data("hot-3", hot, 3, Bytes("04")), policy);
      EXPECT_EQ(
        id == "drop_oldest_is_contract_local"
          ? u2r2::EnqueueDisposition::dropped_oldest
          : u2r2::EnqueueDisposition::replaced_latest,
        disposition);
      const auto drained = DrainAll(scheduler);
      EXPECT_NE(drained.end(), std::find(drained.begin(), drained.end(), "cold-1"));
      std::vector<std::string> hot_values;
      for (const auto & token : drained) {
        if (token.rfind("hot-", 0) == 0) {
          hot_values.push_back(token);
        }
      }
      EXPECT_EQ(scenario.at("retainedHotTokens").get<std::vector<std::string>>(), hot_values);
    } else if (id == "zero_byte_replace_releases_depth") {
      u2r2::BoundedOutboundScheduler scheduler(
        limits.with({{"maxPerContractQueueDepth", 1}}));
      const u2r2::ContractKey key{1, 1};
      EXPECT_EQ(
        u2r2::EnqueueDisposition::accepted,
        scheduler.enqueue_data(
          u2r2::OutboundFrame::data(
            "zero-byte-victim",
            key,
            1,
            {}),
          u2r2::QueueOverflowPolicy::reject));
      EXPECT_EQ(
        u2r2::EnqueueDisposition::replaced_latest,
        scheduler.enqueue_data(
          u2r2::OutboundFrame::data(
            "replacement",
            key,
            2,
            Bytes("01")),
          u2r2::QueueOverflowPolicy::replace_latest));
      EXPECT_EQ(
        scenario.at("expectedQueuedDepth").get<uint64_t>(),
        scheduler.data_queued_depth());
      EXPECT_EQ(
        scenario.at("expectedQueuedBytes").get<uint64_t>(),
        scheduler.queued_bytes());
      EXPECT_EQ(
        scenario.at("expectedOrder").get<std::vector<std::string>>(),
        DrainAll(scheduler));
    } else if (id == "per_contract_fifo_round_robin") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      const u2r2::ContractKey a{1, 1};
      const u2r2::ContractKey b{2, 1};
      (void)scheduler.enqueue_data(u2r2::OutboundFrame::data("a-1", a, 1, Bytes("01")), u2r2::QueueOverflowPolicy::reject);
      (void)scheduler.enqueue_data(u2r2::OutboundFrame::data("a-2", a, 2, Bytes("02")), u2r2::QueueOverflowPolicy::reject);
      (void)scheduler.enqueue_data(u2r2::OutboundFrame::data("b-1", b, 1, Bytes("03")), u2r2::QueueOverflowPolicy::reject);
      (void)scheduler.enqueue_data(u2r2::OutboundFrame::data("b-2", b, 2, Bytes("04")), u2r2::QueueOverflowPolicy::reject);
      EXPECT_EQ(scenario.at("expectedOrder").get<std::vector<std::string>>(), DrainAll(scheduler));
    } else if (id == "bounded_control_priority_allows_data") {
      u2r2::BoundedOutboundScheduler scheduler(limits.with({{"controlBurstLimit", 2}}));
      const u2r2::ContractKey key{1, 1};
      (void)scheduler.enqueue_data(
        u2r2::OutboundFrame::data("data-1", key, 1, Bytes("01")),
        u2r2::QueueOverflowPolicy::reject);
      EnqueueControl(scheduler, "control-1");
      EnqueueControl(scheduler, "control-2");
      EnqueueControl(scheduler, "control-3");
      EXPECT_EQ(scenario.at("expectedOrder").get<std::vector<std::string>>(), DrainAll(scheduler));
    } else if (id == "fenced_control_yields_to_other_contract_data") {
      const auto bounded = limits.with({{"controlBurstLimit", 1}});
      u2r2::BoundedOutboundScheduler scheduler(bounded);
      EnqueueControl(scheduler, "control-prime");
      EXPECT_EQ("control-prime", DrainOne(scheduler).token());

      u2r2::RequestReplayAuthority replay(bounded);
      u2r2::ContractAuthority contracts(bounded, DefaultSemanticErrorFrame);
      const auto identity = Identity(Key(scenario));
      auto response = replay.admit(
        scenario.at("requestId").get<uint64_t>(),
        RequestBytes("register_subscription", identity),
        1,
        scheduler);
      auto registration = contracts.begin_registration(
        identity,
        scheduler,
        replay,
        response);
      contracts.commit_ready(
        registration,
        replay,
        response,
        u2r2::OutboundFrame::control(
          "subscription_ready",
          Bytes("01")));

      const u2r2::ContractKey other{
        identity.key.contract_id + 1,
        identity.key.generation};
      EXPECT_EQ(
        u2r2::EnqueueDisposition::accepted,
        scheduler.enqueue_data(
          u2r2::OutboundFrame::data(
            "same-data",
            identity.key,
            1,
            Bytes("01")),
          u2r2::QueueOverflowPolicy::reject));
      EXPECT_EQ(
        u2r2::EnqueueDisposition::accepted,
        scheduler.enqueue_data(
          u2r2::OutboundFrame::data(
            "other-data",
            other,
            1,
            Bytes("02")),
          u2r2::QueueOverflowPolicy::reject));
      EXPECT_EQ(
        scenario.at("expectedOrder").get<std::vector<std::string>>(),
        DrainAll(scheduler));
    } else if (id == "reserved_control_survives_full_data_budget") {
      const auto bounded = limits.with({
        {"fixedFrameBytes", 1}, {"maxHeaderBytes", 1}, {"maxPayloadBytes", 8},
        {"maxTransientBytes", 16}, {"maxInFlightBytes", 16},
        {"maxPerContractQueueDepth", 2}, {"maxPerContractQueueBytes", 16},
        {"maxTotalQueueDepth", 3}, {"maxQueuedBytes", 24},
        {"reservedControlQueueDepth", 1}, {"reservedControlQueueBytes", 8},
        {"controlBurstLimit", 1}});
      u2r2::BoundedOutboundScheduler scheduler(bounded);
      const u2r2::ContractKey key{1, 1};
      const auto data_tokens =
        scenario.at("dataTokens").get<std::vector<std::string>>();
      EXPECT_EQ(
        u2r2::EnqueueDisposition::accepted,
        scheduler.enqueue_data(
          u2r2::OutboundFrame::data(data_tokens.at(0), key, 1, std::vector<uint8_t>(8)),
          u2r2::QueueOverflowPolicy::reject));
      EXPECT_EQ(
        u2r2::EnqueueDisposition::accepted,
        scheduler.enqueue_data(
          u2r2::OutboundFrame::data(data_tokens.at(1), key, 2, std::vector<uint8_t>(8)),
          u2r2::QueueOverflowPolicy::reject));
      EXPECT_EQ(
        u2r2::EnqueueDisposition::rejected,
        scheduler.enqueue_data(
          u2r2::OutboundFrame::data("data-overflow", key, 3, Bytes("01")),
          u2r2::QueueOverflowPolicy::reject));
      auto reservation = scheduler.try_reserve_control(8);
      EXPECT_EQ(
        scenario.at("expectedControlReserved").get<bool>(),
        reservation.has_value());
      ASSERT_TRUE(reservation.has_value());
      reservation->commit(u2r2::OutboundFrame::control(
          scenario.at("controlToken").get<std::string>(),
          std::vector<uint8_t>(8)));
    } else if (id == "queued_writer_accounting_exact") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      const u2r2::ContractKey key{1, 1};
      const auto bytes = scenario.at("frameBytes").get<size_t>();
      (void)scheduler.enqueue_data(
        u2r2::OutboundFrame::data("frame", key, 1, std::vector<uint8_t>(bytes)),
        u2r2::QueueOverflowPolicy::reject);
      EXPECT_EQ(
        scenario.at("expectedQueuedBeforeWrite").get<uint64_t>(),
        scheduler.queued_bytes());
      auto writer = scheduler.try_begin_write();
      ASSERT_TRUE(writer.has_value());
      EXPECT_EQ(
        scenario.at("expectedQueuedDuringWrite").get<uint64_t>(),
        scheduler.queued_bytes());
      EXPECT_EQ(
        scenario.at("expectedInFlightDuringWrite").get<uint64_t>(),
        scheduler.in_flight_bytes());
      writer.reset();
      EXPECT_EQ(
        scenario.at("expectedFinalBytes").get<uint64_t>(),
        scheduler.queued_bytes());
      EXPECT_EQ(
        scenario.at("expectedFinalBytes").get<uint64_t>(),
        scheduler.in_flight_bytes());
    } else if (id == "byte_reservations_release_exactly_once") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      auto transient = scheduler.try_reserve_transient(8);
      auto reader = scheduler.try_begin_read(16);
      ASSERT_TRUE(transient.has_value());
      ASSERT_TRUE(reader.has_value());
      transient.reset();
      reader.reset();
      EXPECT_EQ(scenario.at("expectedFinalBytes").get<uint64_t>(), scheduler.transient_bytes());
      EXPECT_EQ(scenario.at("expectedFinalBytes").get<uint64_t>(), scheduler.in_flight_bytes());
    } else if (id == "concurrent_lease_settlement_exactly_once") {
      const auto workers = scenario.at("workerCount").get<size_t>();
      const auto iterations = scenario.at("iterations").get<size_t>();
      u2r2::BoundedOutboundScheduler scheduler(limits);
      auto transient_sentinel = scheduler.try_reserve_transient(3);
      ASSERT_TRUE(transient_sentinel.has_value());
      for (size_t iteration = 0; iteration < iterations; ++iteration) {
        auto transient = scheduler.try_reserve_transient(1);
        ASSERT_TRUE(transient.has_value());
        Race(workers, [&]() {(void)transient->release();});
        ASSERT_EQ(3U, scheduler.transient_bytes());
      }
      transient_sentinel->release();
      EXPECT_EQ(
        scenario.at("expectedFinalBytes").get<uint64_t>(),
        scheduler.transient_bytes());

      auto reader = scheduler.try_begin_read(8);
      ASSERT_TRUE(reader.has_value());
      Race(workers, [&]() {(void)reader->release();});
      EXPECT_EQ(
        scenario.at("expectedFinalBytes").get<uint64_t>(),
        scheduler.in_flight_bytes());

      const u2r2::ContractKey key{1, 1};
      auto in_flight_sentinel = scheduler.try_begin_read(3);
      ASSERT_TRUE(in_flight_sentinel.has_value());
      ASSERT_EQ(
        u2r2::EnqueueDisposition::accepted,
        scheduler.enqueue_data(
          u2r2::OutboundFrame::data(
            "writer", key, 1, std::vector<uint8_t>(8)),
          u2r2::QueueOverflowPolicy::reject));
      auto writer = scheduler.try_begin_write();
      ASSERT_TRUE(writer.has_value());
      Race(workers, [&]() {(void)writer->release();});
      ASSERT_EQ(3U, scheduler.in_flight_bytes());
      in_flight_sentinel->release();
      EXPECT_EQ(
        scenario.at("expectedFinalBytes").get<uint64_t>(),
        scheduler.in_flight_bytes());

      u2r2::SessionResourceAuthority resources(limits);
      auto resource_sentinel =
        resources.try_acquire(u2r2::ConnectionRole::probe);
      ASSERT_TRUE(resource_sentinel.has_value());
      for (size_t iteration = 0; iteration < iterations; ++iteration) {
        auto resource =
          resources.try_acquire(u2r2::ConnectionRole::data_session);
        ASSERT_TRUE(resource.has_value());
        Race(workers, [&]() {(void)resource->release();});
        ASSERT_EQ(1U, resources.connection_count());
      }
      resource_sentinel->release();
      EXPECT_EQ(
        scenario.at("expectedFinalConnections").get<uint64_t>(),
        resources.connection_count());

      for (size_t iteration = 0; iteration < iterations; ++iteration) {
        auto sentinel = scheduler.try_reserve_control(1);
        ASSERT_TRUE(sentinel.has_value());
        sentinel->commit(
          u2r2::OutboundFrame::control("sentinel", Bytes("01")));
        auto control = scheduler.try_reserve_control(1);
        ASSERT_TRUE(control.has_value());
        std::atomic<size_t> commit_wins{0};
        std::atomic<size_t> cancel_wins{0};
        RaceTwo(
          [&]() {
            if (control->try_commit(
                u2r2::OutboundFrame::control("race", Bytes("01"))))
            {
              commit_wins.fetch_add(1);
            }
          },
          [&]() {
            if (control->try_cancel()) {
              cancel_wins.fetch_add(1);
            }
          });
        ASSERT_EQ(1U, commit_wins.load() + cancel_wins.load());
        const auto drained = DrainAll(scheduler);
        EXPECT_EQ(
          1,
          std::count(drained.begin(), drained.end(), "sentinel"));
        EXPECT_EQ(
          commit_wins.load(),
          static_cast<size_t>(
            std::count(drained.begin(), drained.end(), "race")));
        ASSERT_EQ(0U, scheduler.queued_bytes());
        ASSERT_EQ(0U, scheduler.in_flight_bytes());
      }

      auto invalid = scheduler.try_reserve_control(1);
      ASSERT_TRUE(invalid.has_value());
      EXPECT_THROW(
        invalid->commit(
          u2r2::OutboundFrame::data(
            "not-control", key, 2, Bytes("01"))),
        std::invalid_argument);
      EXPECT_TRUE(invalid->try_cancel());
      EXPECT_EQ(
        scenario.at("expectedFinalBytes").get<uint64_t>(),
        scheduler.queued_bytes());

      // Move-assigning over a live lease must settle the destination first.
      auto first_transient = scheduler.try_reserve_transient(2);
      auto second_transient = scheduler.try_reserve_transient(3);
      ASSERT_TRUE(first_transient.has_value());
      ASSERT_TRUE(second_transient.has_value());
      *first_transient = std::move(*second_transient);
      EXPECT_EQ(3U, scheduler.transient_bytes());
      first_transient->release();
      EXPECT_EQ(0U, scheduler.transient_bytes());

      auto first_control = scheduler.try_reserve_control(2);
      auto second_control = scheduler.try_reserve_control(3);
      ASSERT_TRUE(first_control.has_value());
      ASSERT_TRUE(second_control.has_value());
      *first_control = std::move(*second_control);
      EXPECT_EQ(3U, scheduler.queued_bytes());
      first_control->try_cancel();
      EXPECT_EQ(0U, scheduler.queued_bytes());

      u2r2::SessionResourceAuthority other_resources(limits);
      auto first_resource =
        resources.try_acquire(u2r2::ConnectionRole::data_session);
      auto second_resource =
        other_resources.try_acquire(u2r2::ConnectionRole::data_session);
      ASSERT_TRUE(first_resource.has_value());
      ASSERT_TRUE(second_resource.has_value());
      *first_resource = std::move(*second_resource);
      EXPECT_EQ(0U, resources.connection_count());
      EXPECT_EQ(1U, other_resources.connection_count());
      first_resource->release();
      EXPECT_EQ(0U, other_resources.connection_count());

      u2r2::BoundedOutboundScheduler first_writer_scheduler(limits);
      u2r2::BoundedOutboundScheduler second_writer_scheduler(limits);
      EnqueueControl(first_writer_scheduler, "first");
      EnqueueControl(second_writer_scheduler, "second");
      auto first_writer = first_writer_scheduler.try_begin_write();
      auto second_writer = second_writer_scheduler.try_begin_write();
      ASSERT_TRUE(first_writer.has_value());
      ASSERT_TRUE(second_writer.has_value());
      *first_writer = std::move(*second_writer);
      EXPECT_EQ(0U, first_writer_scheduler.in_flight_bytes());
      EXPECT_EQ(1U, second_writer_scheduler.in_flight_bytes());
      first_writer->release();
      EXPECT_EQ(0U, second_writer_scheduler.in_flight_bytes());
    } else if (id == "terminal_close_cancels_pending_authorities") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      u2r2::RequestReplayAuthority replay(limits);
      auto pending = replay.admit(
        scenario.at("requestId").get<uint64_t>(),
        Bytes("01"),
        1,
        scheduler);
      u2r2::ContractAuthority contracts(limits, DefaultSemanticErrorFrame);
      const auto identity = Identity(Key(scenario));
      auto register_response = replay.admit(
        scenario.at("requestId").get<uint64_t>() + 1,
        RequestBytes("register_subscription", identity),
        1,
        scheduler);
      auto registration = contracts.begin_registration(
        identity, scheduler, replay, register_response);
      const auto second = Identity({
        identity.key.contract_id + 1,
        identity.key.generation});
      auto ready_response = replay.admit(
        scenario.at("requestId").get<uint64_t>() + 2,
        RequestBytes("register_subscription", second),
        1,
        scheduler);
      auto ready = contracts.begin_registration(
        second, scheduler, replay, ready_response);
      contracts.commit_ready(
        ready,
        replay,
        ready_response,
        u2r2::OutboundFrame::control("subscription_ready", Bytes("01")));
      (void)DrainOne(scheduler);
      auto removal_response = replay.admit(
        scenario.at("requestId").get<uint64_t>() + 3,
        RequestBytes("unregister_subscription", second),
        1,
        scheduler);
      auto removal = contracts.begin_unregister(
        second, scheduler, replay, removal_response);

      EXPECT_EQ(3U, replay.outstanding_requests());
      EXPECT_EQ(2U, contracts.contract_count());
      EXPECT_EQ(1U, scheduler.revoked_contract_count());
      for (
        auto call = 0;
        call < scenario.at("closeCalls").get<int>();
        ++call)
      {
        contracts.close(scheduler, replay);
      }
      EXPECT_TRUE(contracts.is_closed());
      EXPECT_TRUE(replay.is_closed());
      EXPECT_TRUE(scheduler.is_closed());
      EXPECT_EQ(
        scenario.at("expectedContracts").get<uint64_t>(),
        contracts.contract_count());
      EXPECT_EQ(
        scenario.at("expectedOutstandingRequests").get<uint64_t>(),
        replay.outstanding_requests());
      EXPECT_EQ(
        scenario.at("expectedRetainedEntries").get<uint64_t>(),
        replay.retained_entries());
      EXPECT_EQ(
        scenario.at("expectedReplayBytes").get<uint64_t>(),
        replay.replay_bytes());
      EXPECT_EQ(
        scenario.at("expectedReservedDepth").get<uint64_t>(),
        scheduler.total_queued_depth());
      EXPECT_EQ(
        scenario.at("expectedReservedBytes").get<uint64_t>(),
        scheduler.queued_bytes());
      EXPECT_EQ(
        scenario.at("expectedRevokedContracts").get<uint64_t>(),
        scheduler.revoked_contract_count());
    } else if (id == "terminal_close_rejects_wrong_authorities") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      u2r2::RequestReplayAuthority replay(limits);
      u2r2::BoundedOutboundScheduler wrong_scheduler(limits);
      u2r2::RequestReplayAuthority wrong_replay(limits);
      u2r2::ContractAuthority contracts(limits, DefaultSemanticErrorFrame);
      const auto identity = Identity(Key(scenario));
      auto response = replay.admit(
        scenario.at("requestId").get<uint64_t>(),
        RequestBytes("register_subscription", identity),
        1,
        scheduler);
      auto registration = contracts.begin_registration(
        identity,
        scheduler,
        replay,
        response);
      auto wrong_response = wrong_replay.admit(
        scenario.at("wrongRequestId").get<uint64_t>(),
        Bytes("bb"),
        1,
        wrong_scheduler);

      EXPECT_THROW(
        contracts.close(wrong_scheduler, wrong_replay),
        std::logic_error);
      EXPECT_FALSE(contracts.is_closed());
      EXPECT_FALSE(replay.is_closed());
      EXPECT_FALSE(scheduler.is_closed());
      EXPECT_FALSE(wrong_replay.is_closed());
      EXPECT_FALSE(wrong_scheduler.is_closed());
      EXPECT_EQ(1U, contracts.contract_count());
      EXPECT_EQ(
        scenario.at("expectedBoundOutstanding").get<uint64_t>(),
        replay.outstanding_requests());
      EXPECT_EQ(
        scenario.at("expectedBoundReservedDepth").get<uint64_t>(),
        scheduler.total_queued_depth());
      EXPECT_EQ(
        scenario.at("expectedBoundOutstanding").get<uint64_t>(),
        wrong_replay.outstanding_requests());
      EXPECT_EQ(
        scenario.at("expectedBoundReservedDepth").get<uint64_t>(),
        wrong_scheduler.total_queued_depth());

      contracts.close(scheduler, replay);
      EXPECT_TRUE(contracts.is_closed());
      EXPECT_TRUE(replay.is_closed());
      EXPECT_TRUE(scheduler.is_closed());
      EXPECT_EQ(0U, contracts.contract_count());
      EXPECT_EQ(
        scenario.at("expectedClosedOutstanding").get<uint64_t>(),
        replay.outstanding_requests());
      EXPECT_EQ(
        scenario.at("expectedClosedReservedDepth").get<uint64_t>(),
        scheduler.total_queued_depth());
      EXPECT_THROW(
        contracts.close(wrong_scheduler, wrong_replay),
        std::logic_error);
      EXPECT_FALSE(wrong_replay.is_closed());
      EXPECT_FALSE(wrong_scheduler.is_closed());

      wrong_replay.cancel_pending(wrong_response);
      EXPECT_EQ(
        scenario.at("expectedClosedOutstanding").get<uint64_t>(),
        wrong_replay.outstanding_requests());
      EXPECT_EQ(
        scenario.at("expectedClosedReservedDepth").get<uint64_t>(),
        wrong_scheduler.total_queued_depth());
    } else if (id == "revoked_capacity_rejection_has_no_side_effects") {
      const auto bounded = limits.with({
        {"maxContracts", scenario.at("maxContracts").get<uint64_t>()},
        {"maxTombstones", scenario.at("maxTombstones").get<uint64_t>()}});
      u2r2::BoundedOutboundScheduler scheduler(bounded);
      const auto expected_bound =
        scenario.at("expectedBound").get<uint64_t>();
      for (
        uint64_t contract_id = 1;
        contract_id <= expected_bound;
        ++contract_id)
      {
        scheduler.revoke_contract({contract_id, 1});
      }
      EXPECT_EQ(expected_bound, scheduler.revoked_contract_count());

      for (
        auto attempt = 0;
        attempt < scenario.at("attackAttempts").get<int>();
        ++attempt)
      {
        const auto contract_id =
          expected_bound + 1U + static_cast<uint64_t>(attempt);
        EXPECT_THROW(
          scheduler.revoke_contract({contract_id, 1}),
          std::logic_error);
        EXPECT_EQ(expected_bound, scheduler.revoked_contract_count());
      }

      scheduler.revoke_contract({1, 1});
      EXPECT_EQ(expected_bound, scheduler.revoked_contract_count());
    } else if (id == "unregister_revoked_capacity_is_atomic") {
      const auto bounded = limits.with({
        {"maxContracts", scenario.at("maxContracts").get<uint64_t>()},
        {"maxTombstones", scenario.at("maxTombstones").get<uint64_t>()}});
      u2r2::BoundedOutboundScheduler scheduler(bounded);
      u2r2::RequestReplayAuthority replay(bounded);
      u2r2::ContractAuthority contracts(bounded, DefaultSemanticErrorFrame);
      const auto generation = scenario.at("generation").get<uint64_t>();
      const auto target = Identity({
        scenario.at("targetContractId").get<uint64_t>(),
        generation});
      const auto filler = Identity({
        scenario.at("fillerContractId").get<uint64_t>(),
        generation});
      RegisterReady(
        contracts,
        scheduler,
        replay,
        target,
        scenario.at("targetRegisterRequestId").get<uint64_t>());
      RegisterReady(
        contracts,
        scheduler,
        replay,
        filler,
        scenario.at("fillerRegisterRequestId").get<uint64_t>());

      EXPECT_EQ(
        u2r2::EnqueueDisposition::accepted,
        scheduler.enqueue_data(
          u2r2::OutboundFrame::data(
            "filler",
            filler.key,
            1,
            Bytes("01")),
          u2r2::QueueOverflowPolicy::reject));
      auto writer = scheduler.try_begin_write();
      ASSERT_TRUE(writer.has_value());
      auto filler_response = replay.admit(
        scenario.at("fillerUnregisterRequestId").get<uint64_t>(),
        RequestBytes("unregister_subscription", filler),
        1,
        scheduler);
      auto filler_removal = contracts.begin_unregister(
        filler,
        scheduler,
        replay,
        filler_response);
      contracts.cancel_removal(
        filler_removal,
        scheduler,
        replay,
        filler_response);

      for (const auto & value : scenario.at("revokedFillerIds")) {
        scheduler.revoke_contract({
          value.get<uint64_t>(),
          generation});
      }
      EXPECT_EQ(
        scenario.at("expectedRevokedAtCapacity").get<uint64_t>(),
        scheduler.revoked_contract_count());

      auto failed_response = replay.admit(
        scenario.at("failedUnregisterRequestId").get<uint64_t>(),
        RequestBytes("unregister_subscription", target),
        1,
        scheduler);
      EXPECT_THROW(
        (void)contracts.begin_unregister(
          target,
          scheduler,
          replay,
          failed_response),
        std::logic_error);
      EXPECT_EQ(
        scenario.at("expectedContractsAfterFailure").get<uint64_t>(),
        contracts.contract_count());
      EXPECT_EQ(
        u2r2::MessageAdmission::accepted,
        contracts.admit_message(target, 1));
      replay.cancel_pending(failed_response);
      EXPECT_EQ(
        scenario.at("expectedOutstandingAfterCancel").get<uint64_t>(),
        replay.outstanding_requests());
      EXPECT_EQ(
        scenario.at("expectedReservedDepthAfterCancel").get<uint64_t>(),
        scheduler.total_queued_depth());

      writer.reset();
      EXPECT_EQ(
        scenario.at("expectedRevokedAfterRelease").get<uint64_t>(),
        scheduler.revoked_contract_count());
      auto retry_response = replay.admit(
        scenario.at("retryUnregisterRequestId").get<uint64_t>(),
        RequestBytes("unregister_subscription", target),
        1,
        scheduler);
      auto retry_removal = contracts.begin_unregister(
        target,
        scheduler,
        replay,
        retry_response);
      EXPECT_TRUE(contracts.try_commit_removed(
          retry_removal,
          scheduler,
          replay,
          retry_response,
          u2r2::OutboundFrame::control(
            "subscription_removed",
            Bytes("02"))));
      (void)DrainOne(scheduler);
      EXPECT_EQ(0U, contracts.contract_count());
      EXPECT_EQ(0U, replay.outstanding_requests());
      contracts.close(scheduler, replay);
    } else if (id == "one_reader_and_one_writer") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      auto reader = scheduler.try_begin_read(1);
      ASSERT_TRUE(reader.has_value());
      EXPECT_FALSE(scheduler.try_begin_read(1).has_value());
      EnqueueControl(scheduler, "control");
      auto writer = scheduler.try_begin_write();
      ASSERT_TRUE(writer.has_value());
      EXPECT_FALSE(scheduler.try_begin_write().has_value());
    } else if (id == "capacity_counter_max_plus_one") {
      u2r2::CapacityCounter counter(scenario.at("capacity").get<uint64_t>());
      EXPECT_TRUE(counter.try_acquire());
      EXPECT_TRUE(counter.try_acquire());
      EXPECT_FALSE(counter.try_acquire());
      counter.release();
      counter.release();
      EXPECT_EQ(scenario.at("expectedFinalCount").get<uint64_t>(), counter.count());
      EXPECT_THROW(counter.release(), std::logic_error);
    } else if (id == "checked_frame_size_bounds") {
      const auto maximum = u2r2::FrameSize::create(
        limits, limits.max_header_bytes(), limits.max_payload_bytes());
      EXPECT_EQ(
        limits.fixed_frame_bytes() + limits.max_header_bytes() + limits.max_payload_bytes(),
        maximum.total_bytes());
      EXPECT_GE(limits.max_per_contract_queue_bytes(), maximum.total_bytes());
      EXPECT_GE(limits.max_queued_bytes(), maximum.total_bytes());
      EXPECT_GE(limits.max_transient_bytes(), maximum.total_bytes());
      EXPECT_GE(limits.max_in_flight_bytes(), maximum.total_bytes());
      ExpectProtocolError(scenario, [&]() {
        (void)u2r2::FrameSize::create(
          limits, limits.max_header_bytes(), limits.max_payload_bytes() + 1);
      });
      ExpectProtocolError(scenario, [&]() {
        (void)u2r2::checked_add(
          std::numeric_limits<uint64_t>::max(), 1,
          std::numeric_limits<uint64_t>::max(), "overflow");
      });
    } else if (id == "request_counter_exhausts_before_wrap") {
      u2r2::RequestIdCounter counter(ParseU64(scenario.at("startingRequestId")));
      EXPECT_EQ(ParseU64(scenario.at("lastRequestId")), counter.next());
      ExpectProtocolError(scenario, [&]() {(void)counter.next();});
      EXPECT_TRUE(counter.is_faulted());
    } else if (id == "request_counter_is_thread_safe") {
      const auto workers = scenario.at("workerCount").get<size_t>();
      const auto iterations = scenario.at("iterationsPerWorker").get<size_t>();
      const auto expected_unique = scenario.at("expectedUniqueIds").get<size_t>();
      u2r2::RequestIdCounter counter;
      std::vector<uint64_t> ids(expected_unique);
      std::atomic<size_t> next_index{0};
      Race(workers, [&]() {
        for (size_t iteration = 0; iteration < iterations; ++iteration) {
          const auto index = next_index.fetch_add(1);
          ids.at(index) = counter.next();
        }
      });
      EXPECT_EQ(expected_unique, next_index.load());
      std::sort(ids.begin(), ids.end());
      EXPECT_EQ(scenario.at("expectedFirstId").get<uint64_t>(), ids.front());
      EXPECT_EQ(scenario.at("expectedLastId").get<uint64_t>(), ids.back());
      EXPECT_EQ(
        ids.end(),
        std::adjacent_find(ids.begin(), ids.end()));
    } else if (id == "wrong_generation_is_not_a_tombstone") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      u2r2::RequestReplayAuthority replay(limits);
      u2r2::ContractAuthority contracts(limits, DefaultSemanticErrorFrame);
      const u2r2::ContractKey removed{
        scenario.at("contractId").get<uint64_t>(),
        scenario.at("removedGeneration").get<uint64_t>()};
      RegisterAndRemove(contracts, scheduler, replay, removed, 1);
      const u2r2::ContractKey wrong{
        scenario.at("contractId").get<uint64_t>(),
        scenario.at("wrongGeneration").get<uint64_t>()};
      ExpectProtocolError(scenario, [&]() {
        (void)contracts.admit_message(Identity(wrong), 1);
      });
    } else if (id == "failed_reservation_has_no_side_effects") {
      const auto bounded = limits.with({
        {"reservedControlQueueDepth", 1}, {"reservedControlQueueBytes", 1},
        {"controlBurstLimit", 1}});
      u2r2::BoundedOutboundScheduler scheduler(bounded);
      EnqueueControl(scheduler, "occupied");
      u2r2::RequestReplayAuthority replay(bounded);
      size_t mutations = 0;
      ExpectProtocolError(scenario, [&]() {
        auto admission = replay.admit(
          scenario.at("requestId").get<uint64_t>(), Bytes("01"), 1, scheduler);
        if (admission.decision() == u2r2::ReplayDecision::begin_mutation) {
          ++mutations;
        }
      });
      EXPECT_EQ(scenario.at("expectedHighWater").get<uint64_t>(), replay.high_water_mark());
      EXPECT_EQ(scenario.at("expectedMutationCount").get<size_t>(), mutations);
    } else if (id == "replay_advances_high_water_once") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      u2r2::RequestReplayAuthority replay(limits);
      const auto request = Bytes(scenario.at("canonicalRequestHex").get<std::string>());
      const auto response = Bytes(scenario.at("responseHex").get<std::string>());
      size_t mutations = 0;
      auto first = replay.admit(
        scenario.at("requestId").get<uint64_t>(), request, response.size(), scheduler);
      if (first.decision() == u2r2::ReplayDecision::begin_mutation) {
        ++mutations;
      }
      replay.complete(first, response);
      (void)DrainOne(scheduler);
      auto repeated = replay.admit(
        scenario.at("requestId").get<uint64_t>(), request, response.size(), scheduler);
      EXPECT_EQ(u2r2::ReplayDecision::replay_cached, repeated.decision());
      EXPECT_EQ(response, repeated.cached_response());
      EXPECT_EQ(scenario.at("expectedHighWater").get<uint64_t>(), replay.high_water_mark());
      EXPECT_EQ(scenario.at("expectedMutationCount").get<size_t>(), mutations);
    } else if (id == "pending_request_identity_is_atomic") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      u2r2::RequestReplayAuthority replay(limits);
      const auto request = Bytes(scenario.at("canonicalRequestHex").get<std::string>());
      auto pending = replay.admit(
        scenario.at("requestId").get<uint64_t>(), request, 3, scheduler);
      EXPECT_EQ(u2r2::ReplayDecision::begin_mutation, pending.decision());
      EXPECT_EQ(scenario.at("expectedHighWater").get<uint64_t>(), replay.high_water_mark());
      ExpectProtocolError(scenario.at("identicalPending"), [&]() {
        (void)replay.admit(
          scenario.at("requestId").get<uint64_t>(), request, 3, scheduler);
      });
      ExpectProtocolError(scenario.at("conflictingPending"), [&]() {
        (void)replay.admit(
          scenario.at("requestId").get<uint64_t>(),
          Bytes(scenario.at("conflictingRequestHex").get<std::string>()),
          3,
          scheduler);
      });
      ExpectProtocolError(scenario.at("lowerPending"), [&]() {
        (void)replay.admit(
          scenario.at("lowerRequestId").get<uint64_t>(), Bytes("06"), 1, scheduler);
      });
      auto higher = replay.admit(
        scenario.at("higherRequestId").get<uint64_t>(), Bytes("08"), 1, scheduler);
      EXPECT_EQ(u2r2::ReplayDecision::begin_mutation, higher.decision());
    } else if (id == "replay_completion_abort_exactly_once") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      u2r2::RequestReplayAuthority replay(limits);
      const auto request = Bytes(scenario.at("canonicalRequestHex").get<std::string>());
      const auto complete_response =
        Bytes(scenario.at("completeResponseHex").get<std::string>());
      const auto abort_response =
        Bytes(scenario.at("abortResponseHex").get<std::string>());
      size_t mutations = 0;
      auto completed = replay.admit(
        scenario.at("completeRequestId").get<uint64_t>(),
        request, complete_response.size(), scheduler);
      ++mutations;
      replay.complete(completed, complete_response);
      EXPECT_THROW(replay.complete(completed, complete_response), std::logic_error);
      (void)DrainOne(scheduler);
      auto aborted = replay.admit(
        scenario.at("abortRequestId").get<uint64_t>(),
        request, abort_response.size(), scheduler);
      ++mutations;
      replay.abort(aborted, abort_response);
      EXPECT_THROW(replay.abort(aborted, abort_response), std::logic_error);
      (void)DrainOne(scheduler);
      EXPECT_EQ(
        scenario.at("expectedOutstandingRequests").get<uint64_t>(),
        replay.outstanding_requests());
      EXPECT_EQ(scenario.at("expectedMutationCount").get<size_t>(), mutations);
      auto replayed_abort = replay.admit(
        scenario.at("abortRequestId").get<uint64_t>(),
        request, abort_response.size(), scheduler);
      EXPECT_EQ(u2r2::ReplayDecision::replay_cached, replayed_abort.decision());
      EXPECT_EQ(abort_response, replayed_abort.cached_response());
    } else if (id == "all_named_counters_are_bounded") {
      const std::unordered_set<std::string> expected_names{
        "connections", "dataSessions", "probes", "contracts",
        "outstandingRequests"};
      for (const auto & name : scenario.at("counterNames")) {
        EXPECT_NE(expected_names.end(), expected_names.find(name.get<std::string>()));
      }
      u2r2::SessionResourceAuthority resources(limits);
      std::vector<u2r2::ResourceLease> leases;
      auto data = resources.try_acquire(u2r2::ConnectionRole::data_session);
      ASSERT_TRUE(data.has_value());
      leases.push_back(std::move(*data));
      for (uint64_t index = 0; index < limits.max_probes(); ++index) {
        auto probe = resources.try_acquire(u2r2::ConnectionRole::probe);
        ASSERT_TRUE(probe.has_value());
        leases.push_back(std::move(*probe));
      }
      EXPECT_FALSE(
        resources.try_acquire(u2r2::ConnectionRole::data_session).has_value());
      EXPECT_FALSE(resources.try_acquire(u2r2::ConnectionRole::probe).has_value());
      EXPECT_EQ(limits.max_connections(), resources.connection_count());
      leases.clear();
      EXPECT_EQ(0U, resources.connection_count());

      const auto contract_limits = limits.with({
        {"maxContracts", 2}, {"reservedControlQueueDepth", 3}});
      u2r2::BoundedOutboundScheduler contract_scheduler(contract_limits);
      u2r2::ContractAuthority contracts(
        contract_limits,
        DefaultSemanticErrorFrame);
      u2r2::RequestReplayAuthority contract_replay(contract_limits);
      const auto first_identity = Identity({1, 1});
      auto first_response = contract_replay.admit(
        1,
        RequestBytes("register_subscription", first_identity),
        1,
        contract_scheduler);
      auto first_registration = contracts.begin_registration(
        first_identity,
        contract_scheduler,
        contract_replay,
        first_response);
      contracts.commit_ready(
        first_registration,
        contract_replay,
        first_response,
        u2r2::OutboundFrame::control("subscription_ready:1", Bytes("01")));
      (void)DrainOne(contract_scheduler);
      const auto second_identity = Identity({2, 1});
      auto second_response = contract_replay.admit(
        2,
        RequestBytes("register_subscription", second_identity),
        1,
        contract_scheduler);
      auto second_registration = contracts.begin_registration(
        second_identity,
        contract_scheduler,
        contract_replay,
        second_response);
      contracts.commit_ready(
        second_registration,
        contract_replay,
        second_response,
        u2r2::OutboundFrame::control("subscription_ready:2", Bytes("01")));
      (void)DrainOne(contract_scheduler);
      ExpectProtocolError(scenario, [&]() {
        const auto third_identity = Identity({3, 1});
        auto third_response = contract_replay.admit(
          3,
          RequestBytes("register_subscription", third_identity),
          1,
          contract_scheduler);
        (void)contracts.begin_registration(
          third_identity,
          contract_scheduler,
          contract_replay,
          third_response);
      });

      const auto replay_limits = limits.with({
        {"maxOutstandingRequests", 2}, {"reservedControlQueueDepth", 3}});
      u2r2::BoundedOutboundScheduler replay_scheduler(replay_limits);
      u2r2::RequestReplayAuthority replay(replay_limits);
      (void)replay.admit(1, Bytes("01"), 1, replay_scheduler);
      (void)replay.admit(2, Bytes("02"), 1, replay_scheduler);
      ExpectProtocolError(scenario, [&]() {
        (void)replay.admit(3, Bytes("03"), 1, replay_scheduler);
      });
    } else if (id == "limits_diagnostic_snapshot_is_immutable") {
      const auto snapshot = limits.to_diagnostic_snapshot();
      EXPECT_EQ(scenario.at("expectedLimitCount").get<size_t>(), snapshot.size());
      EXPECT_EQ(
        snapshot,
        u2r2::ProtocolLimits::defaults().to_diagnostic_snapshot());
      auto source = snapshot;
      const auto independent = u2r2::ProtocolLimits::from_diagnostic_snapshot(source);
      source["maxConnections"] = 999;
      EXPECT_EQ(limits.max_connections(), independent.max_connections());
    } else if (id == "limits_configuration_fails_closed") {
      for (const auto & mutation_value : scenario.at("invalidMutations")) {
        const auto mutation = mutation_value.get<std::string>();
        auto values = limits.to_diagnostic_snapshot();
        if (mutation == "missing_field") {
          values.erase("maxConnections");
          ExpectProtocolError(scenario, [&]() {
            (void)u2r2::ProtocolLimits::from_diagnostic_snapshot(values);
          });
        } else if (mutation == "unknown_field") {
          values["unknownLimit"] = 1;
          ExpectProtocolError(scenario, [&]() {
            (void)u2r2::ProtocolLimits::from_diagnostic_snapshot(values);
          });
        } else if (mutation == "zero_value") {
          values["readTimeoutMs"] = 0;
          ExpectProtocolError(scenario, [&]() {
            (void)u2r2::ProtocolLimits::from_diagnostic_snapshot(values);
          });
        } else if (mutation == "data_sessions_not_one") {
          values["maxDataSessions"] = 2;
          values["maxConnections"] =
            values["maxDataSessions"] + values["maxProbes"];
          ExpectProtocolError(scenario, [&]() {
            (void)u2r2::ProtocolLimits::from_diagnostic_snapshot(values);
          });
        } else if (mutation == "connections_below_roles") {
          values["maxConnections"] =
            values["maxDataSessions"] + values["maxProbes"] - 1;
          ExpectProtocolError(scenario, [&]() {
            (void)u2r2::ProtocolLimits::from_diagnostic_snapshot(values);
          });
        } else if (mutation == "per_contract_below_max_frame") {
          values["maxPerContractQueueBytes"] =
            values["fixedFrameBytes"] + values["maxHeaderBytes"] +
            values["maxPayloadBytes"] - 1;
          ExpectProtocolError(scenario, [&]() {
            (void)u2r2::ProtocolLimits::from_diagnostic_snapshot(values);
          });
        } else if (mutation == "queued_below_max_frame") {
          values["maxQueuedBytes"] =
            values["fixedFrameBytes"] + values["maxHeaderBytes"] +
            values["maxPayloadBytes"] - 1;
          ExpectProtocolError(scenario, [&]() {
            (void)u2r2::ProtocolLimits::from_diagnostic_snapshot(values);
          });
        } else if (mutation == "control_depth_exceeds_total") {
          values["reservedControlQueueDepth"] = values["maxTotalQueueDepth"] + 1;
          ExpectProtocolError(scenario, [&]() {
            (void)u2r2::ProtocolLimits::from_diagnostic_snapshot(values);
          });
        } else if (mutation == "control_burst_exceeds_depth") {
          values["controlBurstLimit"] = values["reservedControlQueueDepth"] + 1;
          ExpectProtocolError(scenario, [&]() {
            (void)u2r2::ProtocolLimits::from_diagnostic_snapshot(values);
          });
        } else if (mutation == "revoked_bound_overflow") {
          values["maxContracts"] = std::numeric_limits<uint64_t>::max();
          values["maxTombstones"] = 1;
          ExpectProtocolError(scenario, [&]() {
            (void)u2r2::ProtocolLimits::from_diagnostic_snapshot(values);
          });
        } else if (mutation == "unknown_with_field") {
          ExpectProtocolError(scenario, [&]() {
            (void)limits.with({{"unknownLimit", 1}});
          });
        } else {
          FAIL() << "unknown limits mutation: " << mutation;
        }
      }
    } else if (id == "ready_unregister_full_ordering") {
      const auto key = Key(scenario);
      u2r2::BoundedOutboundScheduler scheduler(limits);
      u2r2::ContractAuthority contracts(limits, DefaultSemanticErrorFrame);
      u2r2::RequestReplayAuthority replay(limits);
      const auto identity = Identity(key);
      auto ready_response = replay.admit(
        1, RequestBytes("register_subscription", identity), 1, scheduler);
      auto registration = contracts.begin_registration(
        identity, scheduler, replay, ready_response);
      auto pre_ready = scenario;
      pre_ready["expectedErrorCode"] =
        scenario.at("expectedPreReadyErrorCode");
      ExpectProtocolError(pre_ready, [&]() {
        (void)contracts.admit_message(
          identity,
          scenario.at("firstSequence").get<uint64_t>());
      });
      contracts.commit_ready(
        registration,
        replay,
        ready_response,
        u2r2::OutboundFrame::control("subscription_ready", Bytes("01")));
      EXPECT_EQ(
        u2r2::MessageAdmission::accepted,
        contracts.admit_message(identity, 1));
      std::vector<std::string> order{DrainOne(scheduler).token()};
      (void)scheduler.enqueue_data(
        u2r2::OutboundFrame::data("queued", key, 2, Bytes("01")),
        u2r2::QueueOverflowPolicy::reject);
      (void)scheduler.enqueue_data(
        u2r2::OutboundFrame::data("writer", key, 3, Bytes("02")),
        u2r2::QueueOverflowPolicy::reject);
      auto writer = scheduler.try_begin_write();
      ASSERT_TRUE(writer.has_value());
      order.push_back(writer->frame().token());
      auto removed_response = replay.admit(
        2, RequestBytes("unregister_subscription", identity), 1, scheduler);
      auto removal = contracts.begin_unregister(
        identity, scheduler, replay, removed_response);
      EXPECT_EQ(0U, scheduler.data_queued_depth());
      EXPECT_FALSE(contracts.try_commit_removed(
          removal,
          scheduler,
          replay,
          removed_response,
          u2r2::OutboundFrame::control("subscription_removed", Bytes("03"))));
      writer.reset();
      EXPECT_TRUE(contracts.try_commit_removed(
          removal,
          scheduler,
          replay,
          removed_response,
          u2r2::OutboundFrame::control("subscription_removed", Bytes("03"))));
      order.push_back(DrainOne(scheduler).token());
      EXPECT_EQ(scenario.at("expectedOrder").get<std::vector<std::string>>(), order);
      EXPECT_EQ(
        u2r2::MessageAdmission::late_tombstone,
        contracts.admit_message(identity, 2));
    } else if (id == "contract_identity_validation") {
      for (const auto & topic : scenario.at("validTopics")) {
        const auto parsed = ParseRegistration(
          topic.get<std::string>(), "sensor_msgs/msg/Image",
          scenario.at("validQos").at(0));
        EXPECT_EQ(topic.get<std::string>(), parsed.topic);
        EXPECT_EQ("sensor_msgs/msg/Image", parsed.schema_name);
      }
      for (const auto & topic : scenario.at("invalidTopics")) {
        ExpectProtocolError(scenario, [&]() {
          (void)ParseRegistration(
            topic.get<std::string>(), "sensor_msgs/msg/Image",
            scenario.at("validQos").at(0));
        });
      }
      for (const auto & type : scenario.at("validTypes")) {
        const auto parsed = ParseRegistration(
          "/camera/front", type.get<std::string>(),
          scenario.at("validQos").at(0));
        EXPECT_EQ(type.get<std::string>(), parsed.schema_name);
      }
      for (const auto & type : scenario.at("invalidTypes")) {
        ExpectProtocolError(scenario, [&]() {
          (void)ParseRegistration(
            "/camera/front", type.get<std::string>(),
            scenario.at("validQos").at(0));
        });
      }
      for (const auto & qos : scenario.at("validQos")) {
        const auto parsed = ParseRegistration(
          "/camera/front", "sensor_msgs/msg/Image", qos);
        ASSERT_TRUE(parsed.qos.has_value());
        EXPECT_EQ(qos.at("profile").get<std::string>(), parsed.qos->profile);
        EXPECT_EQ(
          qos.at("reliability").get<std::string>(),
          parsed.qos->reliability);
        EXPECT_EQ(
          qos.at("durability").get<std::string>(),
          parsed.qos->durability);
        EXPECT_EQ(qos.at("history").get<std::string>(), parsed.qos->history);
        EXPECT_EQ(qos.at("depth").get<uint32_t>(), parsed.qos->depth);
      }
      for (const auto & qos : scenario.at("invalidQos")) {
        ExpectProtocolError(scenario, [&]() {
          (void)ParseRegistration(
            "/camera/front", "sensor_msgs/msg/Image", qos);
        });
      }
      for (const auto & mutation : scenario.at("invalidQosShapes")) {
        const auto invalid = InvalidQosShape(
          scenario.at("validQos").at(0),
          mutation.get<std::string>());
        ExpectProtocolError(scenario, [&]() {
          (void)ParseRegistration(
            "/camera/front", "sensor_msgs/msg/Image", invalid);
        });
      }
      const auto & boundaries = scenario.at("typeLengthBoundaries");
      const auto valid_package = std::string(
        boundaries.at("validPackageLength").get<size_t>(), 'a');
      const auto invalid_package = std::string(
        boundaries.at("invalidPackageLength").get<size_t>(), 'a');
      const auto valid_type =
        "T" + std::string(
        boundaries.at("validTypeLength").get<size_t>() - 1, 'a');
      const auto invalid_type =
        "T" + std::string(
        boundaries.at("invalidTypeLength").get<size_t>() - 1, 'a');
      (void)ParseRegistration(
        "/camera/front",
        valid_package + "/msg/" + valid_type,
        scenario.at("validQos").at(0));
      ExpectProtocolError(scenario, [&]() {
        (void)ParseRegistration(
          "/camera/front",
          invalid_package + "/msg/Image",
          scenario.at("validQos").at(0));
      });
      ExpectProtocolError(scenario, [&]() {
        (void)ParseRegistration(
          "/camera/front",
          "sensor_msgs/msg/" + invalid_type,
          scenario.at("validQos").at(0));
      });
      for (const auto & invalid : scenario.at("invalidDirections")) {
        ExpectProtocolError(scenario, [&]() {
          (void)u2r2::ContractIdentity(
            {41, 7},
            static_cast<u2r2::ContractDirection>(
              invalid.get<uint32_t>()),
            "/camera/front",
            "sensor_msgs/msg/Image",
            {"default", "reliable", "volatile", "keep_last", 10});
        });
      }
    } else if (id == "contract_identity_alias_and_replay") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      u2r2::RequestReplayAuthority replay(limits);
      u2r2::ContractAuthority contracts(limits, DefaultSemanticErrorFrame);
      const u2r2::ContractKey key{
        scenario.at("contractId").get<uint64_t>(),
        scenario.at("generation").get<uint64_t>()};
      const auto identity = Identity(key);
      const auto register_request =
        RequestBytes("register_subscription", identity);
      const auto register_id =
        scenario.at("registerRequestId").get<uint64_t>();
      auto ready_response = replay.admit(
        register_id, register_request, 1, scheduler);
      auto registration = contracts.begin_registration(
        identity, scheduler, replay, ready_response);
      contracts.commit_ready(
        registration,
        replay,
        ready_response,
        u2r2::OutboundFrame::control("subscription_ready", Bytes("01")));
      (void)DrainOne(scheduler);

      auto repeated = replay.admit(
        register_id, register_request, 1, scheduler);
      EXPECT_EQ(u2r2::ReplayDecision::replay_cached, repeated.decision());
      auto replayed_registration = contracts.begin_registration(
        identity, scheduler, replay, repeated);
      EXPECT_TRUE(replayed_registration.replayed());
      contracts.commit_ready(
        replayed_registration,
        replay,
        repeated,
        u2r2::OutboundFrame::control("must-not-send", Bytes("01")));
      EXPECT_EQ(
        scenario.at("expectedResponseCount").get<uint64_t>(),
        scheduler.total_queued_depth());
      EXPECT_EQ("replay:21", DrainOne(scheduler).token());

      auto request_id = scenario.at("aliasStartRequestId").get<uint64_t>();
      for (const auto & mutation_value : scenario.at("aliasMutations")) {
        auto alias = identity;
        const auto mutation = mutation_value.get<std::string>();
        if (mutation == "topic") {
          alias.topic = "/camera/rear";
        } else if (mutation == "schemaName") {
          alias.schema_name = "demo_interfaces/msg/Telemetry";
        } else if (mutation == "profile") {
          alias.qos.profile = "sensor_data";
        } else if (mutation == "reliability") {
          alias.qos.reliability = "best_effort";
        } else if (mutation == "durability") {
          alias.qos.durability = "transient_local";
        } else if (mutation == "history") {
          alias.qos.history = "keep_all";
          alias.qos.depth = 0;
        } else if (mutation == "depth") {
          alias.qos.depth = 11;
        } else if (mutation == "direction") {
          alias.direction = u2r2::ContractDirection::publish;
        } else {
          FAIL() << mutation;
        }
        auto alias_response = replay.admit(
          request_id++,
          RequestBytes("register_subscription", alias),
          1,
          scheduler);
        ExpectProtocolError(scenario, [&]() {
          (void)contracts.begin_registration(
            alias, scheduler, replay, alias_response);
        });
        EXPECT_NE(
          std::string::npos,
          DrainOne(scheduler).token().find("subscription_ready"));
      }
      EXPECT_EQ(1U, contracts.contract_count());
    } else if (id == "fresh_registration_requires_subscribe_direction") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      u2r2::RequestReplayAuthority replay(limits);
      u2r2::ContractAuthority contracts(limits, DefaultSemanticErrorFrame);
      const auto identity = Identity(
        Key(scenario),
        "/camera/front",
        "sensor_msgs/msg/Image",
        u2r2::ContractDirection::publish);
      const auto request = RequestBytes("register_subscription", identity);
      const auto request_id = scenario.at("requestId").get<uint64_t>();
      auto response = replay.admit(
        request_id,
        request,
        1,
        scheduler);
      ExpectProtocolError(scenario, [&]() {
        (void)contracts.begin_registration(
          identity,
          scheduler,
          replay,
          response);
      });
      const auto first = DrainOne(scheduler);
      EXPECT_EQ(
        scenario.at("expectedResponseToken").get<std::string>(),
        first.token());
      EXPECT_EQ(
        Bytes(scenario.at("expectedResponseHex").get<std::string>()),
        first.bytes());
      EXPECT_EQ(0U, contracts.contract_count());
      EXPECT_EQ(0U, replay.outstanding_requests());

      auto repeated = replay.admit(
        request_id,
        request,
        1,
        scheduler);
      EXPECT_EQ(u2r2::ReplayDecision::replay_cached, repeated.decision());
      EXPECT_EQ(first.bytes(), repeated.cached_response());
      EXPECT_EQ(first.bytes(), DrainOne(scheduler).bytes());
    } else if (id == "message_requires_frozen_contract_identity") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      u2r2::RequestReplayAuthority replay(limits);
      u2r2::ContractAuthority contracts(limits, DefaultSemanticErrorFrame);
      const auto identity = Identity(Key(scenario));
      auto response = replay.admit(
        1,
        RequestBytes("register_subscription", identity),
        1,
        scheduler);
      auto registration = contracts.begin_registration(
        identity,
        scheduler,
        replay,
        response);
      contracts.commit_ready(
        registration,
        replay,
        response,
        u2r2::OutboundFrame::control(
          "subscription_ready",
          Bytes("01")));
      (void)DrainOne(scheduler);

      for (const auto & mutation : scenario.at("aliasMutations")) {
        const auto alias = AliasIdentity(
          identity,
          mutation.get<std::string>());
        ExpectProtocolError(scenario, [&]() {
          (void)contracts.admit_message(alias, 1);
        });
      }
      EXPECT_EQ(
        u2r2::MessageAdmission::accepted,
        contracts.admit_message(identity, 1));
    } else if (id == "composed_register_unregister_single_response") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      u2r2::RequestReplayAuthority replay(limits);
      u2r2::ContractAuthority contracts(limits, DefaultSemanticErrorFrame);
      const u2r2::ContractKey key{
        scenario.at("contractId").get<uint64_t>(),
        scenario.at("generation").get<uint64_t>()};
      const auto identity = Identity(key);
      const auto register_request =
        RequestBytes("register_subscription", identity);
      const auto register_id =
        scenario.at("registerRequestId").get<uint64_t>();
      std::vector<std::string> order;
      auto ready_response = replay.admit(
        register_id, register_request, 1, scheduler);
      auto registration = contracts.begin_registration(
        identity, scheduler, replay, ready_response);
      contracts.commit_ready(
        registration,
        replay,
        ready_response,
        u2r2::OutboundFrame::control("subscription_ready", Bytes("01")));
      EXPECT_EQ(
        scenario.at("expectedRegisterResponses").get<uint64_t>(),
        scheduler.total_queued_depth());
      order.push_back(DrainOne(scheduler).token());

      auto ready_replay = replay.admit(
        register_id, register_request, 1, scheduler);
      auto replayed_registration = contracts.begin_registration(
        identity, scheduler, replay, ready_replay);
      contracts.commit_ready(
        replayed_registration,
        replay,
        ready_replay,
        u2r2::OutboundFrame::control("must-not-send", Bytes("01")));
      EXPECT_EQ(
        scenario.at("expectedReplayResponses").get<uint64_t>(),
        scheduler.total_queued_depth());
      order.push_back(DrainOne(scheduler).token());

      EXPECT_EQ(
        u2r2::MessageAdmission::accepted,
        contracts.admit_message(identity, 1));
      EXPECT_EQ(
        u2r2::EnqueueDisposition::accepted,
        scheduler.enqueue_data(
          u2r2::OutboundFrame::data("message", key, 1, Bytes("01")),
          u2r2::QueueOverflowPolicy::reject));
      order.push_back(DrainOne(scheduler).token());

      const auto unregister_request =
        RequestBytes("unregister_subscription", identity);
      const auto unregister_id =
        scenario.at("unregisterRequestId").get<uint64_t>();
      auto removed_response = replay.admit(
        unregister_id, unregister_request, 1, scheduler);
      auto removal = contracts.begin_unregister(
        identity, scheduler, replay, removed_response);
      EXPECT_TRUE(contracts.try_commit_removed(
          removal,
          scheduler,
          replay,
          removed_response,
          u2r2::OutboundFrame::control(
            "subscription_removed", Bytes("02"))));
      EXPECT_EQ(
        scenario.at("expectedUnregisterResponses").get<uint64_t>(),
        scheduler.total_queued_depth());
      order.push_back(DrainOne(scheduler).token());

      auto removed_replay = replay.admit(
        unregister_id, unregister_request, 1, scheduler);
      auto replayed_removal = contracts.begin_unregister(
        identity, scheduler, replay, removed_replay);
      EXPECT_TRUE(contracts.try_commit_removed(
          replayed_removal,
          scheduler,
          replay,
          removed_replay,
          u2r2::OutboundFrame::control("must-not-send", Bytes("02"))));
      EXPECT_EQ(
        scenario.at("expectedReplayResponses").get<uint64_t>(),
        scheduler.total_queued_depth());
      order.push_back(DrainOne(scheduler).token());
      EXPECT_EQ(
        scenario.at("expectedOrder").get<std::vector<std::string>>(),
        order);
      EXPECT_EQ(0U, scheduler.total_queued_depth());
      EXPECT_EQ(0U, contracts.contract_count());
      EXPECT_EQ(1U, contracts.tombstone_count());
    } else if (id == "fenced_response_fifo_and_transaction_binding") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      u2r2::RequestReplayAuthority replay(limits);
      u2r2::ContractAuthority contracts(limits, DefaultSemanticErrorFrame);
      const u2r2::ContractKey key{
        scenario.at("contractId").get<uint64_t>(),
        scenario.at("generation").get<uint64_t>()};
      const auto identity = Identity(key);
      auto registration_response = replay.admit(
        scenario.at("registerRequestId").get<uint64_t>(),
        RequestBytes("register_subscription", identity),
        1,
        scheduler);
      auto registration = contracts.begin_registration(
        identity,
        scheduler,
        replay,
        registration_response);
      auto decoy_response = replay.admit(
        scenario.at("decoyRequestId").get<uint64_t>(),
        Bytes("ee"),
        1,
        scheduler);
      EXPECT_THROW(
        contracts.commit_ready(
          registration,
          replay,
          decoy_response,
          u2r2::OutboundFrame::control("wrong", Bytes("ff"))),
        std::logic_error);
      replay.cancel_pending(decoy_response);
      contracts.commit_ready(
        registration,
        replay,
        registration_response,
        u2r2::OutboundFrame::control("subscription_ready", Bytes("01")));

      auto removal_response = replay.admit(
        scenario.at("unregisterRequestId").get<uint64_t>(),
        RequestBytes("unregister_subscription", identity),
        1,
        scheduler);
      auto removal = contracts.begin_unregister(
        identity,
        scheduler,
        replay,
        removal_response);
      EXPECT_TRUE(contracts.try_commit_removed(
          removal,
          scheduler,
          replay,
          removal_response,
          u2r2::OutboundFrame::control(
            "subscription_removed", Bytes("02"))));
      EXPECT_EQ(
        scenario.at("expectedOrder").get<std::vector<std::string>>(),
        DrainAll(scheduler));
      EXPECT_EQ(
        scenario.at("expectedOutstandingRequests").get<uint64_t>(),
        replay.outstanding_requests());
      EXPECT_EQ(0U, contracts.contract_count());
      EXPECT_EQ(1U, contracts.tombstone_count());
    } else if (id == "semantic_rejections_commit_exact_replay") {
      const auto bounded = limits.with({{"maxContracts", 1}});
      u2r2::BoundedOutboundScheduler scheduler(bounded);
      u2r2::RequestReplayAuthority replay(bounded);
      const auto & cases = scenario.at("cases");
      const auto kind_for_request = [&](uint64_t request_id) {
          if (request_id == scenario.at("duplicateRequestId").get<uint64_t>()) {
            return std::string("duplicate");
          }
          if (request_id == scenario.at("capacityRequestId").get<uint64_t>()) {
            return std::string("capacity");
          }
          if (request_id == scenario.at("unknownRequestId").get<uint64_t>()) {
            return std::string("unknown");
          }
          throw std::logic_error("unexpected semantic-rejection request ID");
        };
      const auto case_for_kind = [&](const std::string & kind)
          -> const nlohmann::json & {
          for (const auto & item : cases) {
            if (item.at("kind").get<std::string>() == kind) {
              return item;
            }
          }
          throw std::logic_error("missing semantic-rejection case");
        };
      u2r2::ContractAuthority contracts(
        bounded,
        [&](u2r2::Operation operation,
          uint64_t request_id,
          const u2r2::ProtocolError & error)
        {
          const auto & item = case_for_kind(kind_for_request(request_id));
          EXPECT_EQ(
            item.at("responseOperation").get<std::string>(),
            OperationToken(operation));
          EXPECT_EQ(item.at("errorCode").get<std::string>(), error.code());
          EXPECT_EQ(item.at("terminal").get<bool>(), error.terminal());
          return u2r2::OutboundFrame::control(
            OperationToken(operation) + ":" + std::to_string(request_id) +
            ":" + error.code(),
            Bytes(item.at("responseHex").get<std::string>()));
        });

      const auto identity = Identity(Key(scenario));
      auto initial_response = replay.admit(
        1,
        RequestBytes("register_subscription", identity),
        1,
        scheduler);
      auto initial_registration = contracts.begin_registration(
        identity,
        scheduler,
        replay,
        initial_response);
      contracts.commit_ready(
        initial_registration,
        replay,
        initial_response,
        u2r2::OutboundFrame::control("subscription_ready", Bytes("01")));
      (void)DrainOne(scheduler);

      const auto expect_exact_semantic_replay =
        [&](const std::string & kind,
          uint64_t request_id,
          const std::string & request_operation,
          const u2r2::ContractIdentity & rejected_identity,
          bool registration)
        {
          const auto & item = case_for_kind(kind);
          const auto request = RequestBytes(request_operation, rejected_identity);
          auto response = replay.admit(request_id, request, 1, scheduler);
          try {
            if (registration) {
              (void)contracts.begin_registration(
                rejected_identity,
                scheduler,
                replay,
                response);
            } else {
              (void)contracts.begin_unregister(
                rejected_identity,
                scheduler,
                replay,
                response);
            }
            FAIL() << "expected semantic rejection";
          } catch (const u2r2::ProtocolError & error) {
            EXPECT_EQ(item.at("errorCode").get<std::string>(), error.code());
            EXPECT_EQ(item.at("terminal").get<bool>(), error.terminal());
          }
          const auto first = DrainOne(scheduler);
          EXPECT_EQ(
            Bytes(item.at("responseHex").get<std::string>()),
            first.bytes());
          EXPECT_NE(
            std::string::npos,
            first.token().find(
              item.at("responseOperation").get<std::string>()));

          auto repeated = replay.admit(request_id, request, 1, scheduler);
          EXPECT_EQ(u2r2::ReplayDecision::replay_cached, repeated.decision());
          EXPECT_EQ(
            Bytes(item.at("responseHex").get<std::string>()),
            repeated.cached_response());
          EXPECT_EQ(
            Bytes(item.at("responseHex").get<std::string>()),
            DrainOne(scheduler).bytes());
        };

      expect_exact_semantic_replay(
        "duplicate",
        scenario.at("duplicateRequestId").get<uint64_t>(),
        "register_subscription",
        identity,
        true);
      const auto capacity_identity = Identity(
        {identity.key.contract_id + 1, 1});
      expect_exact_semantic_replay(
        "capacity",
        scenario.at("capacityRequestId").get<uint64_t>(),
        "register_subscription",
        capacity_identity,
        true);
      const auto unknown_identity = Identity(
        {identity.key.contract_id + 2, 1});
      expect_exact_semantic_replay(
        "unknown",
        scenario.at("unknownRequestId").get<uint64_t>(),
        "unregister_subscription",
        unknown_identity,
        false);
      EXPECT_EQ(
        scenario.at("expectedOutstandingRequests").get<uint64_t>(),
        replay.outstanding_requests());
      EXPECT_EQ(1U, contracts.contract_count());
    } else if (id == "contract_claim_blocks_external_cancel") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      u2r2::RequestReplayAuthority replay(limits);
      u2r2::ContractAuthority contracts(
        limits,
        [&](u2r2::Operation operation,
          uint64_t request_id,
          const u2r2::ProtocolError & error)
        {
          EXPECT_EQ(
            scenario.at("responseOperation").get<std::string>(),
            OperationToken(operation));
          EXPECT_EQ(
            scenario.at("abortErrorCode").get<std::string>(),
            error.code());
          return u2r2::OutboundFrame::control(
            OperationToken(operation) + ":" + std::to_string(request_id),
            Bytes(scenario.at("abortResponseHex").get<std::string>()));
        });
      const auto identity = Identity(Key(scenario));
      const auto request = RequestBytes("register_subscription", identity);
      const auto request_id = scenario.at("requestId").get<uint64_t>();
      auto response = replay.admit(request_id, request, 1, scheduler);
      auto registration = contracts.begin_registration(
        identity,
        scheduler,
        replay,
        response);
      EXPECT_THROW(replay.cancel_pending(response), std::logic_error);
      EXPECT_THROW(
        replay.abort(
          response,
          Bytes(scenario.at("abortResponseHex").get<std::string>())),
        std::logic_error);
      contracts.abort_registration(
        registration,
        scheduler,
        replay,
        response,
        u2r2::ProtocolError(
          scenario.at("abortErrorCode").get<std::string>(),
          "registration backend rejected the contract",
          false));
      EXPECT_EQ(
        Bytes(scenario.at("abortResponseHex").get<std::string>()),
        DrainOne(scheduler).bytes());
      EXPECT_EQ(
        scenario.at("expectedOutstandingRequests").get<uint64_t>(),
        replay.outstanding_requests());
      EXPECT_EQ(0U, contracts.contract_count());
      auto repeated = replay.admit(request_id, request, 1, scheduler);
      EXPECT_EQ(u2r2::ReplayDecision::replay_cached, repeated.decision());
      EXPECT_EQ(
        Bytes(scenario.at("abortResponseHex").get<std::string>()),
        DrainOne(scheduler).bytes());
    } else if (id == "cached_replay_rejects_wrong_scheduler") {
      u2r2::BoundedOutboundScheduler original(limits);
      u2r2::BoundedOutboundScheduler wrong(limits);
      u2r2::RequestReplayAuthority replay(limits);
      const auto request =
        Bytes(scenario.at("canonicalRequestHex").get<std::string>());
      const auto response =
        Bytes(scenario.at("responseHex").get<std::string>());
      const auto request_id = scenario.at("requestId").get<uint64_t>();
      auto first = replay.admit(
        request_id,
        request,
        static_cast<uint64_t>(response.size()),
        original);
      replay.complete(first, response);
      (void)DrainOne(original);
      EXPECT_THROW(
        (void)replay.admit(
          request_id,
          request,
          static_cast<uint64_t>(response.size()),
          wrong),
        std::logic_error);
      EXPECT_EQ(
        scenario.at("expectedWrongSchedulerDepth").get<uint64_t>(),
        wrong.total_queued_depth());
      auto repeated = replay.admit(
        request_id,
        request,
        static_cast<uint64_t>(response.size()),
        original);
      EXPECT_EQ(u2r2::ReplayDecision::replay_cached, repeated.decision());
      EXPECT_EQ(response, DrainOne(original).bytes());
    } else if (id == "invalid_overflow_policy_has_no_side_effects") {
      u2r2::BoundedOutboundScheduler scheduler(limits);
      ExpectProtocolError(scenario, [&]() {
        (void)scheduler.enqueue_data(
          u2r2::OutboundFrame::data(
            "invalid", {1, 1}, 1, Bytes("01")),
          static_cast<u2r2::QueueOverflowPolicy>(
            scenario.at("invalidPolicy").get<int>()));
      });
      EXPECT_EQ(
        scenario.at("expectedQueuedDepth").get<uint64_t>(),
        scheduler.total_queued_depth());
      EXPECT_EQ(
        scenario.at("expectedQueuedBytes").get<uint64_t>(),
        scheduler.queued_bytes());
    } else if (id == "replay_responses_respect_control_fairness") {
      auto bounded = limits.with({{"controlBurstLimit", 2}});
      u2r2::BoundedOutboundScheduler scheduler(bounded);
      u2r2::RequestReplayAuthority replay(bounded);
      EXPECT_EQ(
        u2r2::EnqueueDisposition::accepted,
        scheduler.enqueue_data(
          u2r2::OutboundFrame::data("data", {1, 1}, 1, Bytes("01")),
          u2r2::QueueOverflowPolicy::reject));
      for (const auto & id_value : scenario.at("requestIds")) {
        const auto request_id = id_value.get<uint64_t>();
        auto response = replay.admit(
          request_id,
          {static_cast<uint8_t>(request_id)},
          1,
          scheduler);
        replay.complete(
          response,
          {static_cast<uint8_t>(request_id + 1)});
      }
      EXPECT_EQ(
        scenario.at("expectedOrder").get<std::vector<std::string>>(),
        DrainAll(scheduler));
    } else if (id == "response_transaction_rejects_wrong_scheduler") {
      u2r2::BoundedOutboundScheduler original(limits);
      u2r2::BoundedOutboundScheduler wrong(limits);
      u2r2::RequestReplayAuthority replay(limits);
      u2r2::ContractAuthority contracts(limits, DefaultSemanticErrorFrame);
      const auto identity = Identity(Key(scenario));
      auto response = replay.admit(
        scenario.at("requestId").get<uint64_t>(),
        RequestBytes("register_subscription", identity),
        1,
        original);
      EXPECT_THROW(
        contracts.begin_registration(
          identity, wrong, replay, response),
        std::logic_error);
      replay.cancel_pending(response);
      EXPECT_EQ(
        scenario.at("expectedOutstandingRequests").get<uint64_t>(),
        replay.outstanding_requests());
      EXPECT_EQ(
        scenario.at("expectedReservedDepth").get<uint64_t>(),
        original.total_queued_depth());
    } else if (id == "codec_consumes_session_limit_snapshot") {
      const auto bounded = limits.with({
        {"maxHeaderBytes", scenario.at("maxHeaderBytes").get<uint64_t>()},
        {"maxPayloadBytes", scenario.at("maxPayloadBytes").get<uint64_t>()},
        {"maxJsonDepth", scenario.at("maxJsonDepth").get<uint64_t>()}});
      const auto simple = nlohmann::json{{"ok", 1}};
      EXPECT_NO_THROW((void)u2r2::encode_frame(simple, Bytes("01"), bounded));
      auto wire_error = scenario;
      wire_error["expectedErrorCode"] =
        scenario.at("expectedWireErrorCode");
      ExpectProtocolError(wire_error, [&]() {
        (void)u2r2::encode_frame(
          nlohmann::json{{"padding", std::string(100, 'x')}},
          {},
          bounded);
      });
      ExpectProtocolError(wire_error, [&]() {
        (void)u2r2::encode_frame(simple, Bytes("0102"), bounded);
      });
      ExpectProtocolError(wire_error, [&]() {
        (void)u2r2::encode_frame(
          nlohmann::json{{"one", {{"two", {{"three", 1}}}}}},
          {},
          bounded);
      });
      auto configuration_error = scenario;
      configuration_error["expectedErrorCode"] =
        scenario.at("expectedConfigurationErrorCode");
      const auto invalid_fixed = limits.with({{"fixedFrameBytes", 1}});
      ExpectProtocolError(configuration_error, [&]() {
        (void)u2r2::encode_frame(simple, {}, invalid_fixed);
      });
    } else if (id == "pure_peer_close_transition") {
      u2r2::PureSessionLifecycle lifecycle;
      ExpectProtocolError(scenario, [&]() {lifecycle.peer_closed();});
      EXPECT_EQ(u2r2::PureSessionState::closed, lifecycle.state());
    } else if (id == "pure_timeout_transitions") {
      const std::unordered_map<std::string, u2r2::TimeoutKind> kinds{
        {"handshake", u2r2::TimeoutKind::handshake},
        {"partial_frame", u2r2::TimeoutKind::partial_frame},
        {"read", u2r2::TimeoutKind::read},
        {"write", u2r2::TimeoutKind::write},
        {"join", u2r2::TimeoutKind::join},
        {"shutdown", u2r2::TimeoutKind::shutdown}};
      for (const auto & kind : scenario.at("timeoutKinds")) {
        u2r2::PureSessionLifecycle lifecycle(limits);
        const auto parsed_kind = kinds.at(kind.get<std::string>());
        ExpectProtocolError(scenario, [&]() {
          lifecycle.timeout(
            parsed_kind, lifecycle.limit_for(parsed_kind));
        });
        EXPECT_EQ(u2r2::PureSessionState::closed, lifecycle.state());
      }
    } else {
      FAIL() << "unconsumed Commit2 fixture scenario: " << id;
    }
  }
  EXPECT_EQ(scenarios.size(), consumed.size());
}
}  // namespace
