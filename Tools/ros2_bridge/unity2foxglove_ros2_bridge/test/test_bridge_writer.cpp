// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/ros2_bridge
// Purpose: RED-first coverage for the one-writer session core.

#include <atomic>
#include <chrono>
#include <thread>
#include <vector>

#include <gtest/gtest.h>

#include "unity2foxglove_ros2_bridge/bridge_writer.hpp"

namespace
{
namespace runtime = unity2foxglove::ros2_bridge::runtime;
namespace u2r2 = unity2foxglove::ros2_bridge::u2r2;
using namespace std::chrono_literals;

TEST(BridgeWriterCore, GrantsExactlyOneWriterLease)
{
  runtime::BridgeWriterCore writer(u2r2::ProtocolLimits::defaults());

  auto first = writer.try_attach_writer();
  ASSERT_TRUE(first.has_value());
  EXPECT_FALSE(writer.try_attach_writer().has_value());

  EXPECT_TRUE(first->release());
  EXPECT_FALSE(first->release());
  auto replacement = writer.try_attach_writer();
  ASSERT_TRUE(replacement.has_value());
}

TEST(BridgeWriterCore, EnqueuedControlWakesAnIdleWriter)
{
  runtime::BridgeWriterCore writer(u2r2::ProtocolLimits::defaults());
  auto lease = writer.try_attach_writer();
  ASSERT_TRUE(lease.has_value());
  const auto observed = writer.wake_generation();
  std::atomic<bool> awakened{false};

  std::thread waiter(
    [&]() {
      awakened = writer.wait_for_change(
        *lease,
        observed,
        2s);
    });
  std::this_thread::sleep_for(20ms);
  writer.enqueue_control("ready", {1U, 2U, 3U});
  waiter.join();

  EXPECT_TRUE(awakened.load());
  auto write = writer.try_begin_write(*lease);
  ASSERT_TRUE(write.has_value());
  EXPECT_TRUE(write->frame().is_control());
  EXPECT_EQ(std::vector<uint8_t>({1U, 2U, 3U}), write->frame().bytes());
}

TEST(BridgeWriterCore, AcceptedDataSignalsAndPreservesSchedulerAuthority)
{
  runtime::BridgeWriterCore writer(u2r2::ProtocolLimits::defaults());
  auto lease = writer.try_attach_writer();
  ASSERT_TRUE(lease.has_value());
  const u2r2::ContractKey key{7U, 11U};
  const auto observed = writer.wake_generation();

  EXPECT_EQ(
    u2r2::EnqueueDisposition::accepted,
    writer.enqueue_data(
      u2r2::OutboundFrame::data(
        "message",
        key,
        1U,
        {4U, 5U, 6U}),
      u2r2::QueueOverflowPolicy::reject));
  EXPECT_GT(writer.wake_generation(), observed);

  auto write = writer.try_begin_write(*lease);
  ASSERT_TRUE(write.has_value());
  EXPECT_FALSE(write->frame().is_control());
  EXPECT_EQ(1U, write->frame().sequence());
  EXPECT_EQ(std::vector<uint8_t>({4U, 5U, 6U}), write->frame().bytes());
}

TEST(BridgeWriterCore, CloseWakesWaiterAndRejectsFutureWriterWork)
{
  runtime::BridgeWriterCore writer(u2r2::ProtocolLimits::defaults());
  auto lease = writer.try_attach_writer();
  ASSERT_TRUE(lease.has_value());
  const auto observed = writer.wake_generation();
  std::atomic<bool> changed{true};

  std::thread waiter(
    [&]() {
      changed = writer.wait_for_change(
        *lease,
        observed,
        2s);
    });
  std::this_thread::sleep_for(20ms);
  writer.close();
  waiter.join();

  EXPECT_FALSE(changed.load());
  EXPECT_TRUE(writer.is_closed());
  EXPECT_FALSE(writer.try_attach_writer().has_value());
  EXPECT_FALSE(writer.try_begin_write(*lease).has_value());
}

TEST(BridgeWriterCore, DrainRejectsAdmissionsButPreservesCommittedFrames)
{
  runtime::BridgeWriterCore writer(u2r2::ProtocolLimits::defaults());
  auto lease = writer.try_attach_writer();
  ASSERT_TRUE(lease.has_value());
  writer.enqueue_control("committed", {7U, 8U, 9U});
  const auto observed = writer.wake_generation();

  writer.begin_drain();

  EXPECT_GT(writer.wake_generation(), observed);
  EXPECT_FALSE(writer.try_attach_writer().has_value());
  EXPECT_FALSE(writer.try_reserve_control(1U).has_value());
  EXPECT_FALSE(writer.try_reserve_data({1U, 1U}, 1U).has_value());
  EXPECT_FALSE(writer.try_reserve_transient(1U).has_value());
  EXPECT_FALSE(writer.try_begin_read(1U).has_value());
  EXPECT_EQ(
    u2r2::EnqueueDisposition::rejected,
    writer.enqueue_data(
      u2r2::OutboundFrame::data(
        "late",
        {1U, 1U},
        1U,
        {1U}),
      u2r2::QueueOverflowPolicy::reject));

  auto committed = writer.try_begin_write(*lease);
  ASSERT_TRUE(committed.has_value());
  EXPECT_EQ(std::vector<uint8_t>({7U, 8U, 9U}), committed->frame().bytes());
  committed->release();
  EXPECT_FALSE(writer.try_begin_write(*lease).has_value());
}
}  // namespace
