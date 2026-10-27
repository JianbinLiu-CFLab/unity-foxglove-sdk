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
#include <type_traits>
#include <unordered_map>
#include <unordered_set>
#include <vector>

#include <nlohmann/json.hpp>

#include "unity2foxglove_ros2_bridge/u2r2_protocol_authority.hpp"

namespace
{
namespace u2r2 = unity2foxglove::ros2_bridge::u2r2;

static_assert(!std::is_copy_constructible_v<u2r2::ReplayAdmission>);
static_assert(!std::is_copy_assignable_v<u2r2::ReplayAdmission>);
static_assert(!std::is_copy_constructible_v<u2r2::RegistrationAdmission>);
static_assert(!std::is_copy_assignable_v<u2r2::RegistrationAdmission>);
static_assert(!std::is_copy_constructible_v<u2r2::RemovalAdmission>);
static_assert(!std::is_copy_assignable_v<u2r2::RemovalAdmission>);

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

#include "test_u2r2_protocol_authority/Helpers.inl"
#include "test_u2r2_protocol_authority/AdmissionAndScheduler.inl"
#include "test_u2r2_protocol_authority/ReplayAndLifecycle.inl"
#include "test_u2r2_protocol_authority/ConcurrencyAndResources.inl"
#include "test_u2r2_protocol_authority/ScenarioAndContract.inl"
#include "test_u2r2_protocol_authority/RemainingAuthorityTests.inl"
