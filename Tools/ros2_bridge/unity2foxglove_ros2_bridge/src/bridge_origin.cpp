// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/ros2_bridge
// Purpose: Bounded process-owned ROS 2 publisher-origin classification.

#include "unity2foxglove_ros2_bridge/bridge_origin.hpp"

#include <algorithm>
#include <stdexcept>

namespace unity2foxglove::ros2_bridge::runtime
{
BridgeOriginRegistry::BridgeOriginRegistry(uint64_t maximum_publishers)
: maximum_publishers_(maximum_publishers)
{
  if (maximum_publishers_ == 0) {
    throw std::invalid_argument(
            "Bridge origin publisher capacity must be positive");
  }
  local_publishers_.reserve(static_cast<size_t>(maximum_publishers_));
}

bool BridgeOriginRegistry::TryRead(
  const rmw_gid_t & publisher_gid,
  GidBytes & bytes)
{
  if (
    publisher_gid.implementation_identifier == nullptr ||
    publisher_gid.implementation_identifier[0] == '\0')
  {
    return false;
  }
  std::copy(
    std::begin(publisher_gid.data),
    std::end(publisher_gid.data),
    bytes.begin());
  return std::any_of(
    bytes.begin(),
    bytes.end(),
    [](uint8_t value) {return value != 0U;});
}

void BridgeOriginRegistry::register_local(
  const rmw_gid_t & publisher_gid)
{
  GidBytes bytes{};
  if (!TryRead(publisher_gid, bytes)) {
    throw std::runtime_error(
            "the process-owned ROS publisher returned no usable GID");
  }
  std::lock_guard<std::mutex> lock(mutex_);
  if (
    static_cast<uint64_t>(local_publishers_.size()) >=
    maximum_publishers_)
  {
    throw std::runtime_error(
            "Bridge origin publisher capacity is exhausted");
  }
  local_publishers_.push_back(bytes);
}

BridgeSampleOrigin BridgeOriginRegistry::classify(
  const rmw_gid_t & publisher_gid) const
{
  GidBytes bytes{};
  if (!TryRead(publisher_gid, bytes)) {
    return BridgeSampleOrigin::missing;
  }
  std::lock_guard<std::mutex> lock(mutex_);
  const auto matches = static_cast<uint64_t>(std::count(
      local_publishers_.begin(),
      local_publishers_.end(),
      bytes));
  if (matches == 0U) {
    return BridgeSampleOrigin::external;
  }
  return
    matches == 1U
    ? BridgeSampleOrigin::local
    : BridgeSampleOrigin::ambiguous;
}

uint64_t BridgeOriginRegistry::size() const
{
  std::lock_guard<std::mutex> lock(mutex_);
  return static_cast<uint64_t>(local_publishers_.size());
}
}  // namespace unity2foxglove::ros2_bridge::runtime
