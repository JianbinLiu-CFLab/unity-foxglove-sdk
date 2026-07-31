// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/ros2_bridge
// Purpose: Bounded process-owned ROS 2 publisher-origin classification.

#pragma once

#include <array>
#include <cstddef>
#include <cstdint>
#include <mutex>
#include <vector>

#include <rmw/types.h>

namespace unity2foxglove::ros2_bridge::runtime
{
enum class BridgeSampleOrigin
{
  external = 1,
  local = 2,
  missing = 3,
  ambiguous = 4,
};

class BridgeOriginRegistry final
{
public:
  explicit BridgeOriginRegistry(uint64_t maximum_publishers);
  BridgeOriginRegistry(const BridgeOriginRegistry &) = delete;
  BridgeOriginRegistry & operator=(const BridgeOriginRegistry &) = delete;

  void register_local(const rmw_gid_t & publisher_gid);
  BridgeSampleOrigin classify(const rmw_gid_t & publisher_gid) const;
  uint64_t size() const;

private:
  using GidBytes = std::array<uint8_t, RMW_GID_STORAGE_SIZE>;

  static bool TryRead(
    const rmw_gid_t & publisher_gid,
    GidBytes & bytes);

  const uint64_t maximum_publishers_;
  mutable std::mutex mutex_;
  std::vector<GidBytes> local_publishers_;
};
}  // namespace unity2foxglove::ros2_bridge::runtime
