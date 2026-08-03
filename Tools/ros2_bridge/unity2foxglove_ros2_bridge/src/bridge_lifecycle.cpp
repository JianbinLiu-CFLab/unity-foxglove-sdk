// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/ros2_bridge
// Purpose: Bounded sidecar connection and generation ownership.

#include "unity2foxglove_ros2_bridge/bridge_lifecycle.hpp"

#include <atomic>
#include <mutex>

namespace unity2foxglove::ros2_bridge::runtime
{
namespace
{
struct AcceptedSettlement final
{
  explicit AcceptedSettlement(std::shared_ptr<u2r2::CapacityCounter> counter)
  : counter_(std::move(counter))
  {
  }

  ~AcceptedSettlement()
  {
    settle();
  }

  void settle()
  {
    if (!settled_.exchange(true)) {
      counter_->release();
    }
  }

  std::shared_ptr<u2r2::CapacityCounter> counter_;
  std::atomic<bool> settled_{false};
};
}  // namespace

struct ProcessConnectionAuthority::Impl final
{
  explicit Impl(const u2r2::ProtocolLimits & value)
  : limits(value),
    accepted(std::make_shared<u2r2::CapacityCounter>(
        value.max_connections())),
    resources(value)
  {
  }

  const u2r2::ProtocolLimits limits;
  std::shared_ptr<u2r2::CapacityCounter> accepted;
  u2r2::SessionResourceAuthority resources;
  std::mutex identity_mutex;
  u2r2::SidecarSessionIdentityAllocator identities;
};

bool is_ipv4_loopback_peer(
  const sockaddr * address,
  std::size_t address_length) noexcept
{
  if (
    address == nullptr ||
    address_length < sizeof(sockaddr_in) ||
    address->sa_family != AF_INET)
  {
    return false;
  }
  const auto * ipv4 = reinterpret_cast<const sockaddr_in *>(address);
  const uint32_t host = ntohl(ipv4->sin_addr.s_addr);
  return (host & 0xff000000U) == 0x7f000000U;
}

AcceptedConnectionLease::AcceptedConnectionLease(
  std::shared_ptr<void> settlement)
: settlement_(std::move(settlement))
{
}

bool AcceptedConnectionLease::release()
{
  if (!settlement_) {
    return false;
  }
  settlement_.reset();
  return true;
}

ProcessConnectionAuthority::ProcessConnectionAuthority(
  const u2r2::ProtocolLimits & limits)
: impl_(std::make_shared<Impl>(limits))
{
}

ProcessConnectionAuthority::~ProcessConnectionAuthority() = default;

std::optional<AcceptedConnectionLease>
ProcessConnectionAuthority::try_acquire_accepted()
{
  if (!impl_->accepted->try_acquire()) {
    return std::nullopt;
  }
  return AcceptedConnectionLease(
    std::make_shared<AcceptedSettlement>(impl_->accepted));
}

std::optional<u2r2::ResourceLease>
ProcessConnectionAuthority::try_acquire_role(u2r2::ConnectionRole role)
{
  return impl_->resources.try_acquire(role);
}

u2r2::SessionIdentity
ProcessConnectionAuthority::allocate_session_identity()
{
  std::lock_guard<std::mutex> lock(impl_->identity_mutex);
  return impl_->identities.allocate();
}

uint64_t ProcessConnectionAuthority::accepted_count() const
{
  return impl_->accepted->count();
}

uint64_t ProcessConnectionAuthority::classified_count() const
{
  return impl_->resources.connection_count();
}

const u2r2::ProtocolLimits &
ProcessConnectionAuthority::limits() const noexcept
{
  return impl_->limits;
}

GenerationOwnership::GenerationOwnership(u2r2::ResourceLease data_lease)
: data_lease_(std::move(data_lease))
{
}

GenerationOwnership::~GenerationOwnership()
{
  try {
    release();
  } catch (...) {
  }
}

bool GenerationOwnership::release()
{
  if (released_) {
    return false;
  }
  released_ = true;
  entities_.reset();
  if (data_lease_) {
    data_lease_->release();
    data_lease_.reset();
  }
  return true;
}
}  // namespace unity2foxglove::ros2_bridge::runtime
