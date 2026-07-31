// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/ros2_bridge
// Purpose: Bounded sidecar connection and generation ownership.

#pragma once

#ifdef _WIN32
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <winsock2.h>
#include <ws2tcpip.h>
#else
#include <netinet/in.h>
#include <sys/socket.h>
#endif

#include <cstddef>
#include <cstdint>
#include <memory>
#include <optional>
#include <stdexcept>
#include <utility>

#include "unity2foxglove_ros2_bridge/u2r2_protocol.hpp"
#include "unity2foxglove_ros2_bridge/u2r2_protocol_authority.hpp"

namespace unity2foxglove::ros2_bridge::runtime
{
bool is_ipv4_loopback_peer(
  const sockaddr * address,
  std::size_t address_length) noexcept;

inline constexpr const char * kLegacyPublishBusyLog =
  "busy: legacy v1 publish data session already leased";

class AcceptedConnectionLease final
{
public:
  AcceptedConnectionLease() = default;
  ~AcceptedConnectionLease() = default;
  AcceptedConnectionLease(AcceptedConnectionLease &&) noexcept = default;
  AcceptedConnectionLease & operator=(AcceptedConnectionLease &&) noexcept =
    default;
  AcceptedConnectionLease(const AcceptedConnectionLease &) = delete;
  AcceptedConnectionLease & operator=(const AcceptedConnectionLease &) =
    delete;

  bool release();

private:
  friend class ProcessConnectionAuthority;
  explicit AcceptedConnectionLease(std::shared_ptr<void> settlement);
  std::shared_ptr<void> settlement_;
};

class ProcessConnectionAuthority final
{
public:
  explicit ProcessConnectionAuthority(const u2r2::ProtocolLimits & limits);
  ~ProcessConnectionAuthority();
  ProcessConnectionAuthority(const ProcessConnectionAuthority &) = delete;
  ProcessConnectionAuthority & operator=(const ProcessConnectionAuthority &) =
    delete;

  std::optional<AcceptedConnectionLease> try_acquire_accepted();
  std::optional<u2r2::ResourceLease> try_acquire_role(
    u2r2::ConnectionRole role);
  u2r2::SessionIdentity allocate_session_identity();
  uint64_t accepted_count() const;
  uint64_t classified_count() const;
  const u2r2::ProtocolLimits & limits() const noexcept;

private:
  struct Impl;
  std::shared_ptr<Impl> impl_;
};

class GenerationOwnership final
{
public:
  explicit GenerationOwnership(u2r2::ResourceLease data_lease);
  ~GenerationOwnership();
  GenerationOwnership(GenerationOwnership &&) noexcept = default;
  GenerationOwnership & operator=(GenerationOwnership &&) noexcept = default;
  GenerationOwnership(const GenerationOwnership &) = delete;
  GenerationOwnership & operator=(const GenerationOwnership &) = delete;

  template<typename Entity, typename ... Arguments>
  Entity & emplace_entities(Arguments && ... arguments)
  {
    if (released_ || entities_) {
      throw std::logic_error(
              "generation entities are already owned or released");
    }
    auto entity =
      std::make_shared<Entity>(std::forward<Arguments>(arguments)...);
    auto * result = entity.get();
    entities_ = std::move(entity);
    return *result;
  }

  template<typename Entity>
  Entity & adopt_entities(std::unique_ptr<Entity> entity)
  {
    if (released_ || entities_) {
      throw std::logic_error(
              "generation entities are already owned or released");
    }
    if (!entity) {
      throw std::invalid_argument("generation entities are required");
    }
    auto shared = std::shared_ptr<Entity>(std::move(entity));
    auto * result = shared.get();
    entities_ = std::move(shared);
    return *result;
  }

  bool release();

private:
  std::optional<u2r2::ResourceLease> data_lease_;
  std::shared_ptr<void> entities_;
  bool released_{false};
};
}  // namespace unity2foxglove::ros2_bridge::runtime
