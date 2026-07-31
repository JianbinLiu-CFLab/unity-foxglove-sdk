// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/ros2_bridge
// Purpose: RED-first sidecar process, admission, and generation ownership tests.

#ifdef _WIN32
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <winsock2.h>
#include <ws2tcpip.h>
#else
#include <arpa/inet.h>
#include <netinet/in.h>
#include <sys/socket.h>
#endif

#include <cstddef>
#include <cstdint>
#include <memory>
#include <string>

#include <gtest/gtest.h>
#include <rclcpp/context.hpp>

#include "unity2foxglove_ros2_bridge/bridge_lifecycle.hpp"
#include "unity2foxglove_ros2_bridge/bridge_process.hpp"

namespace
{
namespace runtime = unity2foxglove::ros2_bridge::runtime;
namespace u2r2 = unity2foxglove::ros2_bridge::u2r2;

sockaddr_in Ipv4(const char * value)
{
  sockaddr_in peer {};
  peer.sin_family = AF_INET;
  EXPECT_EQ(1, ::inet_pton(AF_INET, value, &peer.sin_addr));
  return peer;
}

TEST(BridgePeerValidation, AcceptsOnlyIpv4LoopbackAfterAccept)
{
  const auto loopback = Ipv4("127.0.0.1");
  const auto other_loopback = Ipv4("127.255.255.254");
  const auto wildcard = Ipv4("0.0.0.0");
  const auto lan = Ipv4("192.168.1.20");
  const auto public_peer = Ipv4("8.8.8.8");

  EXPECT_TRUE(runtime::is_ipv4_loopback_peer(
    reinterpret_cast<const sockaddr *>(&loopback),
    sizeof(loopback)));
  EXPECT_TRUE(runtime::is_ipv4_loopback_peer(
    reinterpret_cast<const sockaddr *>(&other_loopback),
    sizeof(other_loopback)));
  EXPECT_FALSE(runtime::is_ipv4_loopback_peer(
    reinterpret_cast<const sockaddr *>(&wildcard),
    sizeof(wildcard)));
  EXPECT_FALSE(runtime::is_ipv4_loopback_peer(
    reinterpret_cast<const sockaddr *>(&lan),
    sizeof(lan)));
  EXPECT_FALSE(runtime::is_ipv4_loopback_peer(
    reinterpret_cast<const sockaddr *>(&public_peer),
    sizeof(public_peer)));
  EXPECT_FALSE(runtime::is_ipv4_loopback_peer(
    reinterpret_cast<const sockaddr *>(&loopback),
    sizeof(loopback) - 1));

  sockaddr_in6 ipv6 {};
  ipv6.sin6_family = AF_INET6;
  EXPECT_EQ(1, ::inet_pton(AF_INET6, "::1", &ipv6.sin6_addr));
  EXPECT_FALSE(runtime::is_ipv4_loopback_peer(
    reinterpret_cast<const sockaddr *>(&ipv6),
    sizeof(ipv6)));
  EXPECT_FALSE(runtime::is_ipv4_loopback_peer(nullptr, 0));
}

TEST(BridgeConnectionAuthority, BoundsPreclassificationAndRoleLeasesIndependently)
{
  const auto limits = u2r2::ProtocolLimits::defaults().with({
    {"maxConnections", 2},
    {"maxDataSessions", 1},
    {"maxProbes", 1},
  });
  runtime::ProcessConnectionAuthority authority(limits);

  auto accepted_a = authority.try_acquire_accepted();
  auto accepted_b = authority.try_acquire_accepted();
  EXPECT_TRUE(accepted_a.has_value());
  EXPECT_TRUE(accepted_b.has_value());
  EXPECT_FALSE(authority.try_acquire_accepted().has_value());
  EXPECT_EQ(2U, authority.accepted_count());

  auto data = authority.try_acquire_role(u2r2::ConnectionRole::data_session);
  auto probe = authority.try_acquire_role(u2r2::ConnectionRole::probe);
  EXPECT_TRUE(data.has_value());
  EXPECT_TRUE(probe.has_value());
  EXPECT_FALSE(
    authority.try_acquire_role(u2r2::ConnectionRole::data_session).has_value());
  EXPECT_FALSE(authority.try_acquire_role(u2r2::ConnectionRole::probe).has_value());
  EXPECT_EQ(2U, authority.classified_count());

  accepted_a->release();
  EXPECT_EQ(1U, authority.accepted_count());
  EXPECT_TRUE(authority.try_acquire_accepted().has_value());
}

struct TrackedGeneration final
{
  explicit TrackedGeneration(std::size_t * destroyed)
  : destroyed_(destroyed)
  {
  }

  ~TrackedGeneration()
  {
    ++*destroyed_;
  }

  std::size_t * destroyed_;
};

TEST(BridgeGenerationOwnership, DestroysEntitiesBeforeReleasingDataLeaseExactlyOnce)
{
  const auto limits = u2r2::ProtocolLimits::defaults();
  runtime::ProcessConnectionAuthority authority(limits);
  auto data = authority.try_acquire_role(u2r2::ConnectionRole::data_session);
  ASSERT_TRUE(data.has_value());

  std::size_t destroyed = 0;
  runtime::GenerationOwnership generation(std::move(*data));
  auto & entities = generation.emplace_entities<TrackedGeneration>(&destroyed);
  EXPECT_EQ(&destroyed, entities.destroyed_);
  EXPECT_FALSE(
    authority.try_acquire_role(u2r2::ConnectionRole::data_session).has_value());

  EXPECT_TRUE(generation.release());
  EXPECT_EQ(1U, destroyed);
  EXPECT_TRUE(
    authority.try_acquire_role(u2r2::ConnectionRole::data_session).has_value());
  EXPECT_FALSE(generation.release());
  EXPECT_EQ(1U, destroyed);
}

TEST(BridgeProcessOwnership, NodeExecutorAndSpinThreadRemainProcessOwned)
{
  auto context = std::make_shared<rclcpp::Context>();
  context->init(0, nullptr);
  runtime::ProcessRosOwner owner("phase186c_process_owner_test", context);

  auto first = owner.require_node();
  auto second = owner.require_node();
  EXPECT_EQ(first.get(), second.get());
  EXPECT_TRUE(owner.spin_thread_running());
  first.reset();
  second.reset();

  EXPECT_TRUE(owner.stop());
  EXPECT_FALSE(owner.spin_thread_running());
  EXPECT_FALSE(owner.current_node());
  EXPECT_FALSE(owner.stop());
  context->shutdown("phase186c process owner test complete");
}

TEST(BridgeConnectionAuthority, AllocatesFreshProcessLocalSessionGenerations)
{
  runtime::ProcessConnectionAuthority authority(
    u2r2::ProtocolLimits::defaults());
  const auto first = authority.allocate_session_identity();
  const auto second = authority.allocate_session_identity();

  EXPECT_FALSE(first.session_id().empty());
  EXPECT_FALSE(second.session_id().empty());
  EXPECT_NE(first.session_id(), second.session_id());
  EXPECT_EQ(1U, first.connection_generation());
  EXPECT_EQ(2U, second.connection_generation());
}
}  // namespace
