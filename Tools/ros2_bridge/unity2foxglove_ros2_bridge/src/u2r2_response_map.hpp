// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Internal U2R2 request-to-response mapping.

#pragma once

#include "unity2foxglove_ros2_bridge/u2r2_protocol.hpp"

namespace unity2foxglove::ros2_bridge::u2r2::internal
{
inline bool TryGetSuccessResponse(
  Operation request_operation,
  Operation & response_operation)
{
  switch (request_operation) {
    case Operation::Hello:
      response_operation = Operation::HelloAck;
      return true;
    case Operation::HealthPing:
      response_operation = Operation::HealthPong;
      return true;
    case Operation::PreparePublisher:
      response_operation = Operation::PublisherReady;
      return true;
    case Operation::Publish:
      response_operation = Operation::PublishResult;
      return true;
    case Operation::RegisterSubscription:
      response_operation = Operation::SubscriptionReady;
      return true;
    case Operation::UnregisterSubscription:
      response_operation = Operation::SubscriptionRemoved;
      return true;
    default:
      response_operation = Operation::Unknown;
      return false;
  }
}

inline bool IsRequest(Operation operation)
{
  Operation response_operation;
  return TryGetSuccessResponse(operation, response_operation);
}
}  // namespace unity2foxglove::ros2_bridge::u2r2::internal
