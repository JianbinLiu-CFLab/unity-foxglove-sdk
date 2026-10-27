// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Pure, transport-independent U2R2 replay, ordering, and resource authority.

#include "unity2foxglove_ros2_bridge/u2r2_protocol_authority.hpp"

#include <algorithm>
#include <array>
#include <atomic>
#include <cctype>
#include <deque>
#include <limits>
#include <list>
#include <mutex>
#include <stdexcept>
#include <unordered_map>
#include <unordered_set>

#include <nlohmann/json.hpp>

namespace unity2foxglove::ros2_bridge::u2r2
{
#include "u2r2_protocol_authority/ValidationAndIdentity.inl"
#include "u2r2_protocol_authority/LimitsAndResources.inl"
#include "u2r2_protocol_authority/SchedulerAndReplay.inl"
#include "u2r2_protocol_authority/RequestReplay.inl"
#include "u2r2_protocol_authority/Contracts.inl"
#include "u2r2_protocol_authority/Lifecycle.inl"
}  // namespace unity2foxglove::ros2_bridge::u2r2
